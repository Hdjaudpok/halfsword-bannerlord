using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using TaleWorlds.Library;

namespace HalfSwordCombat.Physics
{
    /// <summary>
    /// Half Sword driver-skeleton poses sampled on the player's own install (tools/halfsword/HSPose + make_arm_atlas.py),
    /// in a right-handed character frame (right +X, forward +Y, up +Z, metres, pelvis-relative). The mod ships none of it.
    /// Query by the animation blueprint's inputs: aim point (degrees, disc of radius 90), swing direction (yaw) and swing
    /// speed (0..75). Guard poses are bilinear on the aim grid; swings blend the two nearest directions and the recorded
    /// speeds (0 = guard).
    /// </summary>
    internal sealed class ArmAtlas
    {
        /// <summary>Features of one pose. Quaternions as (x, y, z, w).</summary>
        internal sealed class Pose
        {
            public readonly Vec3[] Shoulder = new Vec3[2], Elbow = new Vec3[2], Wrist = new Vec3[2]; // 0 = right, 1 = left
            public readonly Quat[] Hand = new Quat[2], Clavicle = new Quat[2], UpperArm = new Quat[2], Forearm = new Quat[2];
            public Quat Pelvis, Spine2, Spine3, Spine4, Spine5;

            public static Pose Blend(Pose a, Pose b, float t)
            {
                if (t <= 0f) return a;
                if (t >= 1f) return b;
                var p = new Pose();
                for (int s = 0; s < 2; s++)
                {
                    p.Shoulder[s] = Vec3.Lerp(a.Shoulder[s], b.Shoulder[s], t);
                    p.Elbow[s] = Vec3.Lerp(a.Elbow[s], b.Elbow[s], t);
                    p.Wrist[s] = Vec3.Lerp(a.Wrist[s], b.Wrist[s], t);
                    p.Hand[s] = Nlerp(a.Hand[s], b.Hand[s], t);
                    p.Clavicle[s] = Nlerp(a.Clavicle[s], b.Clavicle[s], t);
                    p.UpperArm[s] = Nlerp(a.UpperArm[s], b.UpperArm[s], t);
                    p.Forearm[s] = Nlerp(a.Forearm[s], b.Forearm[s], t);
                }
                p.Pelvis = Nlerp(a.Pelvis, b.Pelvis, t);
                p.Spine2 = Nlerp(a.Spine2, b.Spine2, t);
                p.Spine3 = Nlerp(a.Spine3, b.Spine3, t);
                p.Spine4 = Nlerp(a.Spine4, b.Spine4, t);
                p.Spine5 = Nlerp(a.Spine5, b.Spine5, t);
                return p;
            }
        }

        /// <summary>One recording group: fixed swing direction and speed proxy, poses on the aim grid.</summary>
        private sealed class Group
        {
            public float Yaw, Speed; // swing direction and the mean swing speed the game actually used
            public readonly Dictionary<long, Pose> Grid = new Dictionary<long, Pose>();
            public float SpeedSum;
            public int Count;
        }

        private readonly Group _guard = new Group();
        private readonly List<Group> _swings = new List<Group>();
        private readonly float _step;

        public float UpperArmLength { get; private set; }
        public float ForearmLength { get; private set; }
        public int Samples { get; private set; }

        private ArmAtlas(float step)
        {
            _step = step;
        }

        public static string DefaultPath(string cls) =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "halfsword-decomp", "port", "arm_atlas_" + cls + ".txt");

        public static ArmAtlas Load(string path, float step = 15f)
        {
            var atlas = new ArmAtlas(step);
            var swings = new Dictionary<string, Group>();
            float upper = 0f, fore = 0f;
            foreach (string raw in File.ReadAllLines(path))
            {
                if (raw.Length == 0 || raw[0] == '#')
                {
                    continue;
                }
                string[] t = raw.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                int k = 0;
                float gx = P(t[k++]), gy = P(t[k++]), dir = P(t[k++]), proxy = P(t[k++]);
                k += 3; // actual X, Y, swing yaw (the commanded ones index the grid)
                float speed = P(t[k++]);
                Pose pose = ReadFeatures(t, ref k);
                upper += pose.Shoulder[0].Distance(pose.Elbow[0]);
                fore += pose.Elbow[0].Distance(pose.Wrist[0]);
                Group g;
                if (proxy <= 0f)
                {
                    g = atlas._guard;
                }
                else
                {
                    string key = dir.ToString(CultureInfo.InvariantCulture) + "/" + proxy.ToString(CultureInfo.InvariantCulture);
                    if (!swings.TryGetValue(key, out g))
                    {
                        g = new Group { Yaw = dir };
                        swings[key] = g;
                        atlas._swings.Add(g);
                    }
                }
                g.Grid[Key(gx, gy, step)] = pose;
                g.SpeedSum += speed;
                g.Count++;
                atlas.Samples++;
            }
            foreach (Group g in atlas._swings)
            {
                g.Speed = g.SpeedSum / Math.Max(1, g.Count);
            }
            atlas.UpperArmLength = upper / Math.Max(1, atlas.Samples);
            atlas.ForearmLength = fore / Math.Max(1, atlas.Samples);
            return atlas;
        }

        /// <summary>
        /// One pose's features from tokens t at k (moves k past them): per arm r then l, shoulder, elbow, wrist (xyz) and
        /// hand, clavicle, upper-arm, forearm rotations (xyzw), then pelvis and spine_02..05 rotations. The atlas files
        /// (make_arm_atlas.py) and HSLink's live POSE line (passthrough phase 1) share this layout.
        /// </summary>
        public static Pose ReadFeatures(string[] t, ref int k)
        {
            var pose = new Pose();
            for (int s = 0; s < 2; s++)
            {
                pose.Shoulder[s] = V(t, ref k);
                pose.Elbow[s] = V(t, ref k);
                pose.Wrist[s] = V(t, ref k);
                pose.Hand[s] = Q(t, ref k);
                pose.Clavicle[s] = Q(t, ref k);
                pose.UpperArm[s] = Q(t, ref k);
                pose.Forearm[s] = Q(t, ref k);
            }
            pose.Pelvis = Q(t, ref k);
            pose.Spine2 = Q(t, ref k);
            pose.Spine3 = Q(t, ref k);
            pose.Spine4 = Q(t, ref k);
            pose.Spine5 = Q(t, ref k);
            return pose;
        }

        /// <summary>Pose for the aim point (degrees), swing direction (yaw, degrees) and swing speed (0..75).</summary>
        public Pose Query(float x, float y, float swingYaw, float swingSpeed)
        {
            Pose guard = Bilinear(_guard, x, y);
            if (swingSpeed <= 0.5f || _swings.Count == 0)
            {
                return guard;
            }
            // Two nearest recorded directions, then speed between guard (0) and the recorded swing speeds.
            Pose swing = SwingAt(x, y, swingYaw, swingSpeed);
            return swing ?? guard;
        }

        private Pose SwingAt(float x, float y, float yaw, float speed)
        {
            // Directions present in the atlas, sorted.
            var dirs = new SortedSet<float>();
            foreach (Group g in _swings) dirs.Add(g.Yaw);
            float lo = float.NaN, hi = float.NaN;
            float a = Wrap(yaw);
            foreach (float d in dirs) { if (d <= a) lo = d; }
            foreach (float d in dirs) { if (d > a) { hi = d; break; } }
            if (float.IsNaN(lo)) lo = Max(dirs);   // wrap around
            if (float.IsNaN(hi)) hi = Min(dirs);
            float span = (hi - lo + 360f) % 360f;
            if (span <= 0f) span = 360f;           // a single recorded direction
            float td = ((a - lo + 360f) % 360f) / span;
            Pose pLo = AtSpeed(x, y, lo, speed), pHi = AtSpeed(x, y, hi, speed);
            if (pLo == null) return pHi;
            if (pHi == null) return pLo;
            return Pose.Blend(pLo, pHi, td);
        }

        private Pose AtSpeed(float x, float y, float yaw, float speed)
        {
            // Groups of this direction sorted by their recorded speed, guard (speed 0) below them.
            var list = new List<Group>();
            foreach (Group g in _swings) if (Math.Abs(g.Yaw - yaw) < 0.01f) list.Add(g);
            list.Sort((p, q) => p.Speed.CompareTo(q.Speed));
            Group below = _guard;
            float belowSpeed = 0f;
            foreach (Group g in list)
            {
                if (speed <= g.Speed)
                {
                    float t = (speed - belowSpeed) / Math.Max(0.01f, g.Speed - belowSpeed);
                    return Pose.Blend(Bilinear(below, x, y), Bilinear(g, x, y), t);
                }
                below = g;
                belowSpeed = g.Speed;
            }
            return Bilinear(below, x, y);
        }

        private Pose Bilinear(Group g, float x, float y)
        {
            // Clamp into the recorded disc, then interpolate the four grid neighbours present (missing ones dropped).
            float r = (float)Math.Sqrt(x * x + y * y);
            if (r > 90f) { x *= 90f / r; y *= 90f / r; }
            float fx = (float)Math.Floor(x / _step) * _step, fy = (float)Math.Floor(y / _step) * _step;
            float tx = (x - fx) / _step, ty = (y - fy) / _step;
            Pose p00 = Get(g, fx, fy), p10 = Get(g, fx + _step, fy), p01 = Get(g, fx, fy + _step), p11 = Get(g, fx + _step, fy + _step);
            Pose row0 = Mix(p00, p10, tx), row1 = Mix(p01, p11, tx);
            Pose p = Mix(row0, row1, ty);
            return p ?? Nearest(g, x, y);
        }

        private static Pose Mix(Pose a, Pose b, float t) => a == null ? b : b == null ? a : Pose.Blend(a, b, t);

        private Pose Get(Group g, float x, float y) => g.Grid.TryGetValue(Key(x, y, _step), out Pose p) ? p : null;

        private Pose Nearest(Group g, float x, float y)
        {
            Pose best = null;
            float bestD = float.MaxValue;
            foreach (KeyValuePair<long, Pose> kv in g.Grid)
            {
                float gx = ((kv.Key >> 16) - 1000) * _step, gy = ((kv.Key & 0xFFFF) - 1000) * _step;
                float d = (gx - x) * (gx - x) + (gy - y) * (gy - y);
                if (d < bestD) { bestD = d; best = kv.Value; }
            }
            return best;
        }

        private static long Key(float x, float y, float step) =>
            ((long)(Math.Round(x / step) + 1000) << 16) | (long)(Math.Round(y / step) + 1000);

        private static float Wrap(float deg)
        {
            deg %= 360f;
            if (deg < -180f) deg += 360f;
            if (deg >= 180f) deg -= 360f;
            return deg;
        }

        private static float Min(SortedSet<float> s) { foreach (float v in s) return v; return 0f; }

        private static float Max(SortedSet<float> s) { float m = 0f; foreach (float v in s) m = v; return m; }

        private static float P(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

        private static Vec3 V(string[] t, ref int k) { var v = new Vec3(P(t[k]), P(t[k + 1]), P(t[k + 2])); k += 3; return v; }

        private static Quat Q(string[] t, ref int k) { var q = new Quat(P(t[k]), P(t[k + 1]), P(t[k + 2]), P(t[k + 3])); k += 4; return q; }

        internal static Quat Nlerp(Quat a, Quat b, float t)
        {
            float dot = a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
            float sb = dot < 0f ? -t : t, sa = 1f - t;
            var q = new Quat(a.X * sa + b.X * sb, a.Y * sa + b.Y * sb, a.Z * sa + b.Z * sb, a.W * sa + b.W * sb);
            float n = (float)Math.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W);
            return n > 1e-6f ? new Quat(q.X / n, q.Y / n, q.Z / n, q.W / n) : a;
        }
    }

    /// <summary>Plain quaternion (x, y, z, w); TaleWorlds.Library.Quaternion uses a different layout.</summary>
    internal struct Quat
    {
        public float X, Y, Z, W;

        public Quat(float x, float y, float z, float w)
        {
            X = x; Y = y; Z = z; W = w;
        }
    }
}
