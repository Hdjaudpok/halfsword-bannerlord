using System;
using System.IO;
using HalfSwordCombat.Physics;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace HalfSwordCombat.Lab
{
    /// <summary>
    /// Step 3a: Half Sword's real arm poses (atlas sampled from the player's own install) drive the main agent's upper
    /// body; the Half Sword physics body follows. Scripted aim (no input): 2-4 s guard, 4-8 s one circle of radius 60,
    /// 8-13 s diagonal swings back and forth, 13-15 s overhead -> low vertical swing, 15-17 s guard, 17 s release.
    /// Logged every 0.25 s: aim inputs and the right hand of the driver vs the physics body.
    /// </summary>
    internal sealed class AimProbe : MissionBehavior
    {
        public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;

        private const string AtlasClass = "onehand_shield";

        private float _t0 = -1f;
        private int _phase;
        private float _nextLog;
        private ActiveRagdoll _ragdoll;
        private ArmAtlas _atlas;
        private ArmRetarget _retarget;
        private ArmAtlas.Pose _pose;
        private float _x, _y, _yaw, _speed;

        private float T => Mission.CurrentTime - _t0;

        public override void OnMissionTick(float dt)
        {
            Watchdog.Enter("AimProbe.OnMissionTick");
            try
            {
                Agent main = Mission.MainAgent;
                if (main == null || !main.IsActive())
                {
                    return;
                }
                if (_t0 < 0f)
                {
                    Watchdog.Start();
                    _t0 = Mission.CurrentTime;
                    string atlasPath = ArmAtlas.DefaultPath(AtlasClass);
                    if (!File.Exists(HalfSwordProfile.DefaultPath) || !File.Exists(atlasPath))
                    {
                        ProbeLog.Write("AIM: missing Half Sword profile or arm atlas (" + atlasPath + ")");
                        _phase = 9;
                        return;
                    }
                    _atlas = ArmAtlas.Load(atlasPath);
                    ProbeLog.Write("AIM: atlas " + _atlas.Samples + " poses, HS arm " + ProbeLog.F(_atlas.UpperArmLength) + "+" +
                                   ProbeLog.F(_atlas.ForearmLength) + " m | 2s build, 4s circle, 8s diagonal swings, 13s vertical, 17s release");
                }
                float t = T;
                if (_phase == 0 && t > 2f)
                {
                    _phase = 1;
                    _ragdoll = ActiveRagdoll.Build(main, Mission.Scene, HalfSwordProfile.Load(HalfSwordProfile.DefaultPath), ProbeLog.Write, true);
                    _retarget = new ArmRetarget(_ragdoll.BoneIndex, _ragdoll.Parents);
                    _ragdoll.DriverOverride = frames => { if (_pose != null) _retarget.Apply(frames, _pose); };
                }
                else if (_phase == 1 && t > 17f)
                {
                    _phase = 2;
                    ProbeLog.Write("AIM: released, driver updates=" + _ragdoll.DriverUpdates + " hook writes=" + _ragdoll.HookWrites);
                    _ragdoll.Release();
                    _ragdoll = null;
                }
                else if (_phase == 2 && t > 19f)
                {
                    _phase = 3;
                    ProbeLog.Write("PROBE_DONE");
                    if (Environment.GetEnvironmentVariable("HSC_AUTOQUIT") == "1")
                    {
                        Utilities.QuitGame();
                    }
                }

                if (_ragdoll != null)
                {
                    Script(t);
                    _pose = _atlas.Query(_x, _y, _yaw, _speed);
                    _ragdoll.Step(dt, main);
                    if (t >= _nextLog)
                    {
                        _nextLog = t + 0.25f;
                        Vec3 drv = _ragdoll.DriverBoneWorld("r_hand"), body = _ragdoll.BodyBoneWorld("r_hand"), pelvis = _ragdoll.DriverBoneWorld("pelvis");
                        ProbeLog.Write("AIM t=" + ProbeLog.F(t) + " aim=(" + ProbeLog.F(_x) + "," + ProbeLog.F(_y) + ") yaw=" + ProbeLog.F(_yaw) +
                                       " speed=" + ProbeLog.F(_speed) + " | r_hand driver-pelvis=(" + ProbeLog.F(drv.x - pelvis.x) + "," +
                                       ProbeLog.F(drv.y - pelvis.y) + "," + ProbeLog.F(drv.z - pelvis.z) + ") gap_cm=" +
                                       (drv.Distance(body) * 100f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
                    }
                }
            }
            catch (Exception e)
            {
                ProbeLog.Write("Probe EXCEPTION (aim): " + e);
                _ragdoll?.Release();
                _ragdoll = null;
                _phase = 2;
            }
            finally
            {
                Watchdog.Exit();
            }
        }

        // Aim script in Half Sword's aim space (degrees; X right, Y down) with its swing direction convention:
        // yaw = atan2(-vx, vy) of the aim velocity (Willie_BP: MakeRotFromX(AimVelocity rotated by yaw -90)).
        private void Script(float t)
        {
            if (t < 4f)
            {
                Set(0f, 0f, 0f, 0f);
            }
            else if (t < 8f)
            {
                float a = (t - 4f) / 4f * 2f * (float)Math.PI;
                Set(60f * (float)Math.Cos(a), 60f * (float)Math.Sin(a), 0f, 0f);
            }
            else if (t < 13f)
            {
                float u = (t - 8f) % 1.25f;
                bool forward = ((int)((t - 8f) / 1.25f)) % 2 == 0;
                Swing(u, forward ? -70f : 70f, forward ? -40f : 40f, forward ? 70f : -70f, forward ? 40f : -40f);
            }
            else if (t < 15f)
            {
                Swing(Math.Min(t - 13f, 1.2f), 0f, -80f, 0f, 70f);
            }
            else
            {
                Set(0f, 0f, 0f, 0f);
            }
        }

        // A swing from A to B in 0.3 s at full speed, then the speed decays over 0.6 s while holding B.
        private void Swing(float u, float ax, float ay, float bx, float by)
        {
            float yaw = (float)(Math.Atan2(-(bx - ax), by - ay) * 180.0 / Math.PI);
            if (u < 0.3f)
            {
                float k = u / 0.3f;
                Set(ax + (bx - ax) * k, ay + (by - ay) * k, yaw, 75f);
            }
            else
            {
                Set(bx, by, yaw, Math.Max(0f, 75f * (1f - (u - 0.3f) / 0.6f)));
            }
        }

        private void Set(float x, float y, float yaw, float speed)
        {
            _x = x;
            _y = y;
            _yaw = yaw;
            _speed = speed;
        }
    }
}
