using System;
using System.Globalization;
using System.IO;
using TaleWorlds.Library;

namespace HalfSwordCombat.Physics
{
    /// <summary>
    /// How Half Sword holds its weapons, applied to any Bannerlord item (user's request 2026-10-03: the Half Sword pose
    /// with Bannerlord's items held like Half Sword's). Measured from the player's own demo by tools/halfsword/HSGrip
    /// (private file port\hs_grip.txt, Half Sword hand frames in the character convention):
    ///  - right hand: the blade direction, the edge direction and the grip point (arming sword: the blade leaves the fist
    ///    on the thumb side, about 30 degrees towards the fingers);
    ///  - left hand: the shield's height and width directions and its grip point (tagre, centre grip).
    /// Bannerlord item meshes run along their item bone (r_finger0 / l_finger0): weapons with the blade on +Z and the
    /// edge on X, shields with the height on Y, the width on X and the face on Z. The offset turns the item so these
    /// axes match Half Sword's in the (retargeted) hand, and moves its grip (a shield: its centre) to Half Sword's grip.
    /// </summary>
    internal static class HsGrip
    {
        private static Vec3[] _right, _left; // long axis, cross axis, grip point
        private static bool _tried;

        public static string Path => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                                                            "halfsword-decomp", "port", "hs_grip.txt");

        private static bool Load()
        {
            if (_tried)
            {
                return _right != null && _left != null;
            }
            _tried = true;
            if (!File.Exists(Path))
            {
                return false;
            }
            var right = new Vec3[3];
            var left = new Vec3[3];
            int found = 0;
            foreach (string raw in File.ReadAllLines(Path))
            {
                string[] p = raw.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length != 5 || p[0].StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }
                Vec3 v = new Vec3(F(p[2]), F(p[3]), F(p[4]));
                Vec3[] side = p[0] == "right" ? right : p[0] == "left" ? left : null;
                int k = p[1] == "blade" || p[1] == "height" ? 0 : p[1] == "edge" || p[1] == "width" ? 1 : p[1] == "grip" ? 2 : -1;
                if (side != null && k >= 0)
                {
                    side[k] = v;
                    found++;
                }
            }
            if (found < 6)
            {
                return false;
            }
            _right = right;
            _left = left;
            return true;
        }

        /// <summary>
        /// Item offset in its item bone frame, or null without the measurement.
        /// <paramref name="itemRestInHand"/> = the item bone's rest frame in its hand bone (Bannerlord skeleton).
        /// </summary>
        public static MatrixFrame? Offset(bool rightHand, MatrixFrame itemRestInHand, Vec3 meshMin, Vec3 meshMax, bool shield)
        {
            if (!Load() || (shield == rightHand))
            {
                return null; // measured: a weapon in the right hand, a shield in the left one
            }
            Vec3[] d = rightHand ? _right : _left;
            Mat3 rest = itemRestInHand.rotation;
            rest.Orthonormalize();
            Vec3 along = rest.TransformToLocal(ToBannerlordHand(d[0], rightHand));
            Vec3 cross = rest.TransformToLocal(ToBannerlordHand(d[1], rightHand));
            along.Normalize();
            Mat3 o;
            if (!shield)
            {
                Vec3 u = along, s = cross - u * Vec3.DotProduct(cross, u);
                s.Normalize();
                o = new Mat3(s, Vec3.CrossProduct(u, s), u);
            }
            else
            {
                Vec3 f = along, s = cross - f * Vec3.DotProduct(cross, f);
                s.Normalize();
                o = new Mat3(s, f, Vec3.CrossProduct(s, f));
            }
            Vec3 grip = new MatrixFrame(rest, itemRestInHand.origin).TransformToLocal(ToBannerlordHand(d[2], rightHand));
            if (shield)
            {
                Vec3 centre = (meshMin + meshMax) * 0.5f;
                centre.z = 0f; // the hand stays behind the face
                grip -= o.TransformToParent(centre);
            }
            return new MatrixFrame(o, grip);
        }

        // Bannerlord hand axes in Half Sword hand coordinates (ArmRetarget's hand maps): right x = x, y = -z, z = y;
        // left x = -x, y = -z, z = -y.
        private static Vec3 ToBannerlordHand(Vec3 u, bool right) => right ? new Vec3(u.x, -u.z, u.y) : new Vec3(-u.x, -u.z, -u.y);

        private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
