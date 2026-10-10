using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using HalfSwordCombat.Lab;

namespace HalfSwordCombat.Link
{
    /// <summary>
    /// Passthrough step 2 (ai-game-modding-guides guide 02; universal-modder mashup-mods pattern 2: start small, prove one
    /// value crosses). Reads the one line the Half Sword side (UE4SS mod HSLink) rewrites about every 16 ms:
    /// "seq actorX actorY actorZ actorYaw pelvisX pelvisY pelvisZ handX handY handZ END", Half Sword world units (cm,
    /// X forward, Y right, Z up). The sender has no wall clock, so samples are timed here, when their seq changes; a
    /// watchdog reports the link stale when no new seq arrives for 0.5 s (Half Sword closed, paused or minimized without
    /// ticking). Lab: a LINK line twice a second.
    /// </summary>
    internal static class HsLink
    {
        public static readonly string StatePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "halfsword-decomp", "hslink_state.txt");

        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static long _lastSeq = -1, _seqAtLastLog = -1;
        private static double _lastNewAt = -1, _nextLog;

        public struct Sample
        {
            public long Seq;
            public float ActorX, ActorY, ActorZ, ActorYaw;
            public float PelvisX, PelvisY, PelvisZ, HandX, HandY, HandZ;
        }

        /// <summary>
        /// HSLink's third line, "GRIP seq twoHanded attached altGrip lDown searching releaseRate armLTonus END": Willie_BP's
        /// own two-handed grip state, read with the last pose (TryReadPose). False until a GRIP line arrives.
        /// </summary>
        public static bool TwoHanded { get; private set; }

        /// <summary>The GRIP line's left hand attached to the two-handed grip (L Hand In Offhand Attached).</summary>
        public static bool LeftAttached { get; private set; }

        /// <summary>The GRIP line's R Alt Grip: Willie holds his two-handed weapon in its alternate grip (half-swording).</summary>
        public static bool AltGrip { get; private set; }

        /// <summary>
        /// HSLink's WEAPON line, "WEAPON seq zx zy zz xx xy xz END": Willie's right weapon's BaseMesh +Z and +X axes in the
        /// POSE frame (right +X, forward +Y, up +Z, as Bannerlord's model axes). False without a weapon or the line.
        /// </summary>
        public static bool HasWeapon { get; private set; }

        public static TaleWorlds.Library.Vec3 WeaponZ { get; private set; }

        public static TaleWorlds.Library.Vec3 WeaponX { get; private set; }

        /// <summary>The last POSE read (TryReadPose), for telemetry.</summary>
        public static Physics.ArmAtlas.Pose LastPose { get; private set; }

        /// <summary>Seconds since a new sample last arrived (infinity before the first).</summary>
        public static double Age => _lastNewAt < 0 ? double.PositiveInfinity : Clock.Elapsed.TotalSeconds - _lastNewAt;

        /// <summary>The newest complete sample, or false (no file, half-written line, other format).</summary>
        public static bool TryRead(out Sample s)
        {
            s = default(Sample);
            try
            {
                string line;
                using (var stream = new FileStream(StatePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream))
                {
                    line = reader.ReadLine();
                }
                string[] p = line?.Split(' ');
                if (p == null || p.Length != 12 || p[11] != "END")
                {
                    return false;
                }
                CultureInfo c = CultureInfo.InvariantCulture;
                s.Seq = long.Parse(p[0], c);
                s.ActorX = float.Parse(p[1], c);
                s.ActorY = float.Parse(p[2], c);
                s.ActorZ = float.Parse(p[3], c);
                s.ActorYaw = float.Parse(p[4], c);
                s.PelvisX = float.Parse(p[5], c);
                s.PelvisY = float.Parse(p[6], c);
                s.PelvisZ = float.Parse(p[7], c);
                s.HandX = float.Parse(p[8], c);
                s.HandY = float.Parse(p[9], c);
                s.HandZ = float.Parse(p[10], c);
                if (s.Seq != _lastSeq)
                {
                    _lastSeq = s.Seq;
                    _lastNewAt = Clock.Elapsed.TotalSeconds;
                }
                return true;
            }
            catch (Exception)
            {
                return false; // no file yet, or caught mid-write
            }
        }

        /// <summary>
        /// Passthrough phase 1: Half Sword's live arm pose, read from the physical body. It is HSLink's second line,
        /// "POSE seq &lt;70 features&gt; END", in the arm atlas's features and frame (ArmAtlas.ReadFeatures), so
        /// ArmRetarget applies it like an atlas pose. False when there is no fresh, complete pose line.
        /// </summary>
        public static bool TryReadPose(out Physics.ArmAtlas.Pose pose)
        {
            pose = null;
            if (!TryRead(out Sample _) || Age > 0.5)
            {
                return false;
            }
            try
            {
                string text;
                using (var stream = new FileStream(StatePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream))
                {
                    text = reader.ReadToEnd();
                }
                bool grip = false, weapon = false;
                foreach (string line in text.Split('\n'))
                {
                    string[] t = line.Trim().Split(' ');
                    if (t.Length == 73 && t[0] == "POSE" && t[72] == "END")
                    {
                        int k = 2;
                        pose = Physics.ArmAtlas.ReadFeatures(t, ref k);
                    }
                    else if (t.Length == 10 && t[0] == "GRIP" && t[9] == "END")
                    {
                        TwoHanded = t[2] == "1";
                        LeftAttached = t[3] == "1";
                        AltGrip = t[4] == "1";
                        grip = true;
                    }
                    else if (t.Length == 9 && t[0] == "WEAPON" && t[8] == "END")
                    {
                        CultureInfo c = CultureInfo.InvariantCulture;
                        WeaponZ = new TaleWorlds.Library.Vec3(float.Parse(t[2], c), float.Parse(t[3], c), float.Parse(t[4], c));
                        WeaponX = new TaleWorlds.Library.Vec3(float.Parse(t[5], c), float.Parse(t[6], c), float.Parse(t[7], c));
                        weapon = true;
                    }
                }
                if (!grip)
                {
                    TwoHanded = LeftAttached = AltGrip = false; // an HSLink without the GRIP line
                }
                HasWeapon = weapon;
                if (pose != null)
                {
                    LastPose = pose;
                }
                return pose != null;
            }
            catch (Exception)
            {
                // no file, caught mid-write, or a malformed value
            }
            return false;
        }

        /// <summary>
        /// Half Sword's right hand relative to its pelvis, in the Half Sword actor's axes, converted to Bannerlord model
        /// axes (x right, y forward, z up) and metres (docs/PASSTHROUGH.md section 4). False when there is no fresh sample.
        /// </summary>
        public static bool TryHandFromPelvis(out TaleWorlds.Library.Vec3 offset)
        {
            offset = TaleWorlds.Library.Vec3.Zero;
            if (!TryRead(out Sample s) || Age > 0.5)
            {
                return false;
            }
            double yaw = s.ActorYaw * Math.PI / 180.0;
            float dx = s.HandX - s.PelvisX, dy = s.HandY - s.PelvisY, dz = s.HandZ - s.PelvisZ;
            float fwd = (float)(dx * Math.Cos(yaw) + dy * Math.Sin(yaw));
            float right = (float)(-dx * Math.Sin(yaw) + dy * Math.Cos(yaw));
            offset = new TaleWorlds.Library.Vec3(right / 100f, fwd / 100f, dz / 100f);
            return true;
        }

        /// <summary>Lab: call every application tick; logs a LINK line twice a second.</summary>
        public static void LabTick()
        {
            double now = Clock.Elapsed.TotalSeconds;
            if (now < _nextLog)
            {
                return;
            }
            _nextLog = now + 0.5;
            if (!TryRead(out Sample s))
            {
                ProbeLog.Write("LINK no sample (" + (File.Exists(StatePath) ? "file present, unreadable" : "no file") + ")");
                return;
            }
            long fresh = _seqAtLastLog < 0 ? 0 : s.Seq - _seqAtLastLog;
            _seqAtLastLog = s.Seq;
            // Hand relative to the Half Sword actor, in its own axes (forward, right, up), cm.
            double yaw = s.ActorYaw * Math.PI / 180.0;
            float dx = s.HandX - s.ActorX, dy = s.HandY - s.ActorY, dz = s.HandZ - s.ActorZ;
            float fwd = (float)(dx * Math.Cos(yaw) + dy * Math.Sin(yaw));
            float right = (float)(-dx * Math.Sin(yaw) + dy * Math.Cos(yaw));
            ProbeLog.Write("LINK seq=" + s.Seq + " new=" + fresh + " age=" + ProbeLog.F((float)Age) + "s" + (Age > 0.5 ? " STALE" : "") +
                           " actor=(" + ProbeLog.F(s.ActorX) + "," + ProbeLog.F(s.ActorY) + "," + ProbeLog.F(s.ActorZ) + ") yaw=" + ProbeLog.F(s.ActorYaw) +
                           " hand_r local fwd/right/up=(" + ProbeLog.F(fwd) + "," + ProbeLog.F(right) + "," + ProbeLog.F(dz) + ") cm");
        }
    }
}
