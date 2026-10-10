using System;
using TaleWorlds.Library;

namespace HalfSwordCombat.Physics
{
    /// <summary>
    /// Puts an <see cref="ArmAtlas"/> pose (Half Sword driver skeleton, character frame) on Bannerlord's human_skeleton.
    /// Both frames are right +X, forward +Y, up +Z. Each mapped bone takes the rotation of its Half Sword counterpart
    /// composed with a fixed axis map (Bannerlord local axis -> Half Sword local axis). The maps come from the two rest
    /// poses (Half Sword: T-pose, every bone frame = component axes; Bannerlord: A-pose, lab skeleton dump 2026-10-03):
    ///   spine, spine1, spine2            X up, Y forward, Z left
    ///   right clavicle, upper arm, forearm   X along, Y back, Z down     left ones: X along, Y back, Z up
    ///   right hand   X along, Y palm normal, Z thumb                       left hand: Z = -thumb
    /// so a Bannerlord bone points exactly like the Half Sword bone whatever the rest poses. Bone lengths stay
    /// Bannerlord's: positions follow the parent chain with this frame's animated local offsets. Pelvis, legs, neck,
    /// head, twist bones and fingers keep their animated local frames (on top of the retargeted parents).
    /// </summary>
    internal sealed class ArmRetarget
    {
        private enum Src { None, Spine, Spine1, Spine2, ClavR, UpperR, ForeR, HandR, ClavL, UpperL, ForeL, HandL }

        private static readonly Vec3[] Torso = { new Vec3(0, 0, 1), new Vec3(0, 1, 0), new Vec3(-1, 0, 0) };
        private static readonly Vec3[] LimbR = { new Vec3(1, 0, 0), new Vec3(0, -1, 0), new Vec3(0, 0, -1) };
        private static readonly Vec3[] LimbL = { new Vec3(-1, 0, 0), new Vec3(0, -1, 0), new Vec3(0, 0, 1) };
        private static readonly Vec3[] HandRMap = { new Vec3(1, 0, 0), new Vec3(0, 0, -1), new Vec3(0, 1, 0) };
        private static readonly Vec3[] HandLMap = { new Vec3(-1, 0, 0), new Vec3(0, 0, -1), new Vec3(0, -1, 0) };

        /// <summary>Half Sword class whose live tuning applies (hand_r_rot / hand_l_rot).</summary>
        public string TuningClass { get; set; }

        private readonly sbyte[] _parent;
        private readonly MatrixFrame?[] _fixedLocal;   // item bones: the skeleton's rest frame (item_grip 0)
        private readonly MatrixFrame?[] _gripLocal;    // item bones: the game's own hold, taken from its animation (item_grip 1)
        private readonly MatrixFrame[] _animLocal;     // item bones: this tick's animated local frame (hsc.grip, item_grip 2)
        private readonly int[] _stableTicks;           // item bones: ticks the animated local frame has stayed still
        private readonly Src[] _src;
        private readonly MatrixFrame[] _local, _out;

        // Two-handed grip (KeepHands): the left arm chain and the bones that move with each of its segments.
        private readonly sbyte _lShoulder, _lElbow, _lWrist, _rWrist, _rItem;
        private readonly bool[] _belowShoulder, _belowElbow, _belowWrist;

        /// <summary>
        /// Willie holds a two-handed weapon (HSLink's GRIP line): keep the left wrist where Half Sword has it relative to
        /// the right wrist. The rotations alone put it elsewhere, since the bones are Bannerlord's lengths (the Half Sword
        /// alone probe, 2026-10-05 21:57: Willie's wrists stay 0.14-0.15 m apart on the longsword grip, fast moves
        /// included). Live tuning hands_keep = 0 turns it off for A/B.
        /// </summary>
        public bool KeepHands { get; set; }

        /// <summary>Lab: the last two-handed placement (wrist distances before and after, reach clamped or not).</summary>
        public string HandsTelemetry { get; private set; } = "off";

        public ArmRetarget(Func<string, sbyte> boneIndex, sbyte[] parent, Func<sbyte, MatrixFrame> restLocal = null)
        {
            _parent = parent;
            int n = parent.Length;
            _src = new Src[n];
            _local = new MatrixFrame[n];
            _out = new MatrixFrame[n];
            _fixedLocal = new MatrixFrame?[n];
            _gripLocal = new MatrixFrame?[n];
            _animLocal = new MatrixFrame[n];
            _stableTicks = new int[n];
            if (restLocal != null)
            {
                // Item bones (ItemR / ItemL, named r_finger0 / l_finger0: what weapons hang on) stay fixed in the hand:
                // Half Sword's weapon is fixed in the hand, Bannerlord's animations turn the item bone to aim their own
                // swings. Which fixed frame is the live key item_grip (see ItemLocal).
                foreach (string item in new[] { "r_finger0", "l_finger0" })
                {
                    sbyte b = boneIndex(item);
                    if (b >= 0)
                    {
                        _fixedLocal[b] = restLocal(b);
                    }
                }
            }
            Map(boneIndex, "spine", Src.Spine);
            Map(boneIndex, "spine1", Src.Spine1);
            Map(boneIndex, "spine2", Src.Spine2);
            Map(boneIndex, "r_clavicle", Src.ClavR);
            Map(boneIndex, "r_upperarm_twist", Src.UpperR);
            Map(boneIndex, "r_foretwist", Src.ForeR);
            Map(boneIndex, "r_hand", Src.HandR);
            Map(boneIndex, "l_clavicle", Src.ClavL);
            Map(boneIndex, "l_upperarm_twist", Src.UpperL);
            Map(boneIndex, "l_foretwist", Src.ForeL);
            Map(boneIndex, "l_hand", Src.HandL);
            // human_skeleton (lab dump 2026-10-03): l_clavicle > l_upperarm_twist (shoulder joint) > l_upperarm_twist1 >
            // l_foretwist (elbow) > l_foretwist1 > l_hand (wrist) > l_finger0 (the item bone).
            _lShoulder = boneIndex("l_upperarm_twist");
            _lElbow = boneIndex("l_foretwist");
            _lWrist = boneIndex("l_hand");
            _rWrist = boneIndex("r_hand");
            _rItem = boneIndex("r_finger0");
            _belowShoulder = Subtree(_lShoulder);
            _belowElbow = Subtree(_lElbow);
            _belowWrist = Subtree(_lWrist);
        }

        // The bone and every bone under it.
        private bool[] Subtree(sbyte root)
        {
            var under = new bool[_parent.Length];
            if (root < 0)
            {
                return under;
            }
            for (int i = 0; i < _parent.Length; i++)
            {
                for (int b = i; b >= 0; b = _parent[b])
                {
                    if (b == root)
                    {
                        under[i] = true;
                        break;
                    }
                }
            }
            return under;
        }

        private void Map(Func<string, sbyte> boneIndex, string name, Src src)
        {
            sbyte b = boneIndex(name);
            if (b >= 0)
            {
                _src[b] = src;
            }
        }

        /// <summary>
        /// Overwrites the driver's entitial (model space) frames with the retargeted upper body, blended with the engine's
        /// animation by <paramref name="weight"/> (0 = animation, 1 = Half Sword pose).
        /// </summary>
        public void Apply(MatrixFrame[] entitial, ArmAtlas.Pose pose, float weight = 1f)
        {
            int n = _parent.Length;
            for (int i = 0; i < n; i++)
            {
                sbyte p = _parent[i];
                if (p < 0)
                {
                    _local[i] = entitial[i];
                    continue;
                }
                MatrixFrame animated = entitial[p].TransformToLocalNonOrthogonal(entitial[i]);
                _local[i] = _fixedLocal[i].HasValue ? ItemLocal(i, animated) : animated;
            }
            for (int i = 0; i < n; i++)
            {
                sbyte p = _parent[i];
                if (p < 0)
                {
                    _out[i] = entitial[i];
                    continue;
                }
                MatrixFrame parentOut = _out[p];
                Vec3 origin = parentOut.TransformToParent(_local[i].origin);
                Mat3 animated = new MatrixFrame(parentOut.rotation, Vec3.Zero).TransformToParent(new MatrixFrame(_local[i].rotation, Vec3.Zero)).rotation;
                Mat3 rotation = animated;
                if (_src[i] != Src.None)
                {
                    Mat3 mapped = Mapped(_src[i], pose);
                    if (_src[i] == Src.HandR || _src[i] == Src.HandL)
                    {
                        // Live tuning hand_r_rot / hand_l_rot: extra hand rotation in its own frame (degrees x, y, z).
                        MatrixFrame extra = LiveTuning.GetFrame(TuningClass, _src[i] == Src.HandR ? "hand_r" : "hand_l");
                        mapped = new MatrixFrame(mapped, Vec3.Zero).TransformToParent(new MatrixFrame(extra.rotation, Vec3.Zero)).rotation;
                    }
                    // Live tuning spine_weight (0..1, default 1): how much of Half Sword's spine the torso takes. Willie
                    // throws his upper body into big swings (lab 2026-10-05 23:15: our head 0.33-0.56 m ahead of the
                    // pelvis and down to 1.34 m while attacking); the user: "le personnage réagit pas bien, il se baisse".
                    float w = weight;
                    if (_src[i] == Src.Spine || _src[i] == Src.Spine1 || _src[i] == Src.Spine2)
                    {
                        w *= Math.Min(1f, Math.Max(0f, LiveTuning.Get(TuningClass, "spine_weight", 1f)));
                    }
                    rotation = w >= 0.999f ? mapped : w <= 0.001f ? animated : Blend(animated, mapped, w);
                }
                _out[i] = new MatrixFrame(rotation, origin);
            }
            HandsTelemetry = "off";
            if (KeepHands && _lShoulder >= 0 && _lElbow >= 0 && _lWrist >= 0 && _rWrist >= 0)
            {
                KeepLeftOnRight(pose, weight);
            }
            Array.Copy(_out, entitial, n);
        }

        // Two-bone IK on the left arm: the wrist goes to the right wrist plus Half Sword's right-to-left wrist vector. Both
        // frames are right +X, forward +Y, up +Z, and the hands already point as Half Sword's (the rotations are copied),
        // so that vector is the same relative to the right hand in both games. The elbow stays in the plane the copied
        // rotations gave it (shoulder, target, current elbow). The hand and its item bone keep Half Sword's rotation and
        // only move with the wrist.
        private void KeepLeftOnRight(ArmAtlas.Pose pose, float weight)
        {
            Vec3 s = _out[_lShoulder].origin, e = _out[_lElbow].origin, w = _out[_lWrist].origin;
            Vec3 hsVector = pose.Wrist[1] - pose.Wrist[0];
            float before = (w - _out[_rWrist].origin).Length;
            // Oracle for "the left hand is on the weapon": each wrist's distance to the drawn weapon's long axis (the item
            // bone's +Z through its origin, ITEM_R; the copy is drawn on that frame, LinkMarker). On a real two-handed
            // grip both wrists sit about a hand's radius from it.
            float axisR = AxisDistance(_out[_rWrist].origin), axisBefore = AxisDistance(w);
            Vec3 blVector = w - _out[_rWrist].origin;
            float dirErr = AngleBetween(blVector, hsVector);
            Vec3 target = Vec3.Lerp(w, _out[_rWrist].origin + hsVector, Math.Min(1f, Math.Max(0f, weight)));
            float upper = (e - s).Length, fore = (w - e).Length;
            Vec3 toTarget = target - s;
            float reach = toTarget.Length;
            if (upper < 1e-4f || fore < 1e-4f || reach < 1e-4f)
            {
                return;
            }
            Vec3 dir = toTarget * (1f / reach);
            float d = Math.Min(Math.Max(reach, Math.Abs(upper - fore) + 1e-3f), upper + fore - 1e-3f);
            bool clamped = d < reach - 1e-4f;
            // Bend direction: the current elbow's offset from the shoulder-target line; straight arm: keep the old one.
            Vec3 bend = (e - s) - dir * Vec3.DotProduct(e - s, dir);
            if (bend.Length < 1e-4f)
            {
                bend = new Vec3(0f, 0f, -1f) - dir * dir.z;
            }
            bend.Normalize();
            float cosShoulder = Math.Min(1f, Math.Max(-1f, (upper * upper + d * d - fore * fore) / (2f * upper * d)));
            float sinShoulder = (float)Math.Sqrt(1f - cosShoulder * cosShoulder);
            Vec3 elbow = s + dir * (upper * cosShoulder) + bend * (upper * sinShoulder);
            Turn(_belowShoulder, s, e - s, elbow - s, _lWrist);
            Vec3 wrist = _out[_lWrist].origin;
            Turn(_belowElbow, elbow, wrist - elbow, s + dir * d - elbow, _lWrist);
            float after = (_out[_lWrist].origin - _out[_rWrist].origin).Length;
            HandsTelemetry = "keep hs=" + Lab.ProbeLog.F(hsVector.Length) + " before=" + Lab.ProbeLog.F(before) +
                             " after=" + Lab.ProbeLog.F(after) + " dirErrBefore=" + Lab.ProbeLog.F(dirErr) + "deg" +
                             " toWeaponAxis cm R=" + Lab.ProbeLog.F(axisR * 100f) + " L before=" + Lab.ProbeLog.F(axisBefore * 100f) +
                             " after=" + Lab.ProbeLog.F(AxisDistance(_out[_lWrist].origin) * 100f) +
                             (clamped ? " clamped (reach " + Lab.ProbeLog.F(reach) + " > arm " + Lab.ProbeLog.F(upper + fore) + ")" : "");
        }

        // Distance from p to the drawn main item's long axis (item bone origin, +Z); -1 without the item bone.
        private float AxisDistance(Vec3 p)
        {
            if (_rItem < 0)
            {
                return -0.01f;
            }
            Vec3 axis = _out[_rItem].rotation.u;
            axis.Normalize();
            Vec3 d = p - _out[_rItem].origin;
            return (d - axis * Vec3.DotProduct(d, axis)).Length;
        }

        private static float AngleBetween(Vec3 a, Vec3 b)
        {
            float la = a.Length, lb = b.Length;
            if (la < 1e-5f || lb < 1e-5f)
            {
                return 0f;
            }
            float c = Math.Min(1f, Math.Max(-1f, Vec3.DotProduct(a, b) / (la * lb)));
            return (float)(Math.Acos(c) * 180.0 / Math.PI);
        }

        // Turns every bone of a subtree about pivot by the shortest rotation from a to b. Bones under keepRotation (the
        // hand and its item bone) only move with it: their rotations stay Half Sword's.
        private void Turn(bool[] subtree, Vec3 pivot, Vec3 a, Vec3 b, sbyte keepRotation)
        {
            if (a.Length < 1e-5f || b.Length < 1e-5f)
            {
                return;
            }
            a.Normalize();
            b.Normalize();
            Vec3 axis = Vec3.CrossProduct(a, b);
            float sin = axis.Length, cos = Vec3.DotProduct(a, b);
            if (sin < 1e-6f)
            {
                return; // already aligned (a half turn cannot happen here: the bend keeps the elbow on its side)
            }
            axis *= 1f / sin;
            float angle = (float)Math.Atan2(sin, cos);
            Vec3 shift = Vec3.Zero;
            bool shiftKnown = false;
            for (int i = 0; i < _out.Length; i++)
            {
                if (!subtree[i])
                {
                    continue;
                }
                bool keep = keepRotation >= 0 && _belowWrist[i];
                if (keep)
                {
                    if (!shiftKnown)
                    {
                        Vec3 wrist = _out[keepRotation].origin;
                        shift = pivot + RotateAxis(axis, angle, wrist - pivot) - wrist;
                        shiftKnown = true;
                    }
                    _out[i].origin += shift;
                    continue;
                }
                MatrixFrame f = _out[i];
                Mat3 r = f.rotation;
                _out[i] = new MatrixFrame(new Mat3(RotateAxis(axis, angle, r.s), RotateAxis(axis, angle, r.f), RotateAxis(axis, angle, r.u)),
                                          pivot + RotateAxis(axis, angle, f.origin - pivot));
            }
        }

        // Rodrigues' rotation of v about a unit axis.
        private static Vec3 RotateAxis(Vec3 axis, float angle, Vec3 v)
        {
            float c = (float)Math.Cos(angle), s = (float)Math.Sin(angle);
            return v * c + Vec3.CrossProduct(axis, v) * s + axis * (Vec3.DotProduct(axis, v) * (1f - c));
        }

        // The item bone's frame in its hand. Live key item_grip:
        //  0 = the skeleton's rest frame (bind pose: not how the game holds anything, lab 2026-10-04: the axe hung beside
        //      the fist);
        //  1 = the game's own hold, taken from its animation once it has stayed still for 20 ticks (not mid-draw) and
        //      then held (no idle sway), taken again after ResetGrip (another item in the hand); the default;
        //  2 = the game's hold live (follows the native animation's small item bone motions).
        private MatrixFrame ItemLocal(int bone, MatrixFrame animated)
        {
            MatrixFrame previous = _animLocal[bone];
            bool still = Angle(previous.rotation, animated.rotation) < 0.5f && (animated.origin - previous.origin).Length < 0.005f;
            _stableTicks[bone] = still ? _stableTicks[bone] + 1 : 0;
            _animLocal[bone] = animated;
            float mode = LiveTuning.Get(TuningClass, "item_grip", 1f);
            if (mode < 0.5f)
            {
                return _fixedLocal[bone].Value;
            }
            if (mode >= 1.5f)
            {
                return animated;
            }
            if (!_gripLocal[bone].HasValue)
            {
                if (_stableTicks[bone] < 20)
                {
                    return animated; // the game is still moving the item (drawing it): follow until it settles
                }
                _gripLocal[bone] = animated;
            }
            return _gripLocal[bone].Value;
        }

        /// <summary>Another item in a hand: take the game's hold for it again on the next pose.</summary>
        public void ResetGrip() => Array.Clear(_gripLocal, 0, _gripLocal.Length);

        /// <summary>Console (hsc.grip): per item bone, the held frame against the rest and the animated frames.</summary>
        public string DescribeGrip(Func<string, sbyte> boneIndex)
        {
            var sb = new System.Text.StringBuilder();
            foreach (string name in new[] { "r_finger0", "l_finger0" })
            {
                sbyte b = boneIndex(name);
                if (b < 0 || !_fixedLocal[b].HasValue)
                {
                    continue;
                }
                MatrixFrame rest = _fixedLocal[b].Value, anim = _animLocal[b];
                sb.Append(name).Append(": rest o=").Append(V(rest.origin)).Append(" | anim o=").Append(V(anim.origin))
                  .Append(" | anim vs rest: ").Append(Lab.ProbeLog.F(Angle(rest.rotation, anim.rotation))).Append(" deg, ")
                  .Append(Lab.ProbeLog.F((anim.origin - rest.origin).Length * 100f)).Append(" cm");
                if (_gripLocal[b].HasValue)
                {
                    MatrixFrame grip = _gripLocal[b].Value;
                    sb.Append(" | held vs anim: ").Append(Lab.ProbeLog.F(Angle(grip.rotation, anim.rotation))).Append(" deg, ")
                      .Append(Lab.ProbeLog.F((anim.origin - grip.origin).Length * 100f)).Append(" cm");
                }
                sb.Append('\n');
            }
            return sb.Length > 0 ? sb.ToString().TrimEnd() : "no item bones";
        }

        private static float Angle(Mat3 a, Mat3 b)
        {
            Quat qa = ToQuat(a), qb = ToQuat(b);
            float dot = Math.Abs(qa.X * qb.X + qa.Y * qb.Y + qa.Z * qb.Z + qa.W * qb.W);
            return (float)(2.0 * Math.Acos(Math.Min(1f, dot)) * 180.0 / Math.PI);
        }

        private static string V(Vec3 v) => "(" + Lab.ProbeLog.F(v.x) + "," + Lab.ProbeLog.F(v.y) + "," + Lab.ProbeLog.F(v.z) + ")";

        private static Mat3 Mapped(Src src, ArmAtlas.Pose pose)
        {
            switch (src)
            {
                case Src.Spine: return Compose(pose.Spine2, Torso);
                case Src.Spine1: return Compose(pose.Spine4, Torso);
                case Src.Spine2: return Compose(pose.Spine5, Torso);
                case Src.ClavR: return Compose(pose.Clavicle[0], LimbR);
                case Src.UpperR: return Compose(pose.UpperArm[0], LimbR);
                case Src.ForeR: return Compose(pose.Forearm[0], LimbR);
                case Src.HandR: return Compose(pose.Hand[0], HandRMap);
                case Src.ClavL: return Compose(pose.Clavicle[1], LimbL);
                case Src.UpperL: return Compose(pose.UpperArm[1], LimbL);
                case Src.ForeL: return Compose(pose.Forearm[1], LimbL);
                case Src.HandL: return Compose(pose.Hand[1], HandLMap);
                default: return Mat3.Identity;
            }
        }

        private static Mat3 Blend(Mat3 a, Mat3 b, float t)
        {
            Quat q = ArmAtlas.Nlerp(ToQuat(a), ToQuat(b), t);
            return new Mat3(Rotate(q, new Vec3(1, 0, 0)), Rotate(q, new Vec3(0, 1, 0)), Rotate(q, new Vec3(0, 0, 1)));
        }

        // Rows s, f, u are the local axes (the rotation's columns): Shepperd's method.
        private static Quat ToQuat(Mat3 m)
        {
            Vec3 s = m.s, f = m.f, u = m.u;
            s.Normalize();
            f.Normalize();
            u.Normalize();
            float trace = s.x + f.y + u.z;
            if (trace > 0f)
            {
                float k = (float)Math.Sqrt(trace + 1f) * 2f;
                return new Quat((f.z - u.y) / k, (u.x - s.z) / k, (s.y - f.x) / k, 0.25f * k);
            }
            if (s.x > f.y && s.x > u.z)
            {
                float k = (float)Math.Sqrt(1f + s.x - f.y - u.z) * 2f;
                return new Quat(0.25f * k, (f.x + s.y) / k, (u.x + s.z) / k, (f.z - u.y) / k);
            }
            if (f.y > u.z)
            {
                float k = (float)Math.Sqrt(1f + f.y - s.x - u.z) * 2f;
                return new Quat((f.x + s.y) / k, 0.25f * k, (u.y + f.z) / k, (u.x - s.z) / k);
            }
            float k2 = (float)Math.Sqrt(1f + u.z - s.x - f.y) * 2f;
            return new Quat((u.x + s.z) / k2, (u.y + f.z) / k2, 0.25f * k2, (s.y - f.x) / k2);
        }

        // Bannerlord local axis i points where the Half Sword bone sends its mapped local axis.
        private static Mat3 Compose(Quat q, Vec3[] map) => new Mat3(Rotate(q, map[0]), Rotate(q, map[1]), Rotate(q, map[2]));

        private static Vec3 Rotate(Quat q, Vec3 v)
        {
            var u = new Vec3(q.X, q.Y, q.Z);
            Vec3 t = Vec3.CrossProduct(u, v) * 2f;
            return v + t * q.W + Vec3.CrossProduct(u, t);
        }
    }
}
