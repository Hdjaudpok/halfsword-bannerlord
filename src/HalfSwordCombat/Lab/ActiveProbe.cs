using System;
using System.IO;
using HalfSwordCombat.Physics;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace HalfSwordCombat.Lab
{
    /// <summary>
    /// Step 2: Half Sword active ragdoll on the main agent, driven by its Bannerlord animation.
    /// Timeline after the main agent is ready: 2s build, 2-7s stand, 7s moderate chest push, 11s strong push,
    /// 15s muscles and balance off (Half Sword "DED"), 19s release, 21s done.
    /// Logged every 0.25 s: pelvis/head height above ground and body-driver gaps, to compare with the Half Sword
    /// fight recording (median gaps: pelvis 0, spine 4, head 6, hands 13-22, feet 6 cm).
    /// Pose path: engine post-integrate hook (HSC_HOOK=0 disables it) plus direct bone writes from the mission tick
    /// (HSC_DIRECT=0 disables them). hook_err = how far the skeleton was from the pose we asked the hook for, before this
    /// tick's direct write. HOOKDIAG lines read the animation result back inside the callback, right after our writes.
    /// </summary>
    internal sealed class ActiveProbe : MissionBehavior
    {
        public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;

        private static readonly string[] GapBones = { "pelvis", "spine2", "head", "r_hand", "l_hand", "r_foot", "l_foot" };

        private float _t0 = -1f;
        private int _phase;
        private float _nextLog;
        private ActiveRagdoll _ragdoll;
        private HalfSwordProfile _profile;
        private float _errHead, _errHand, _errPelvis; // max hook pose errors (m) in the current log window
        private int _lastCalls, _lastWrites;
        private bool _errorLogged;
        private float _nextDiag;
        private FallDriver _fall;
        private float _standPelvisHeight;

        private float T => Mission.CurrentTime - _t0;

        public override void OnMissionTick(float dt)
        {
            Watchdog.Enter("ActiveProbe.OnMissionTick");
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
                    string path = HalfSwordProfile.DefaultPath;
                    if (!File.Exists(path))
                    {
                        ProbeLog.Write("ACTIVE: no Half Sword profile at " + path + " (run tools/halfsword/make_profile.py)");
                        _phase = 9;
                        return;
                    }
                    _profile = HalfSwordProfile.Load(path);
                    SkeletonDump.Write(main);
                    ProbeLog.Write("ACTIVE: profile " + _profile.Bodies.Count + " bodies, " + _profile.Joints.Count + " joints, " +
                                   _profile.Handles.Count + " handles | 2s build, 7s push 60, 11s push 220, 15s DED, 19s release");
                }

                float t = T;
                if (_phase == 0 && t > 2f)
                {
                    _phase = 1;
                    bool useHook = Environment.GetEnvironmentVariable("HSC_HOOK") != "0";
                    _ragdoll = ActiveRagdoll.Build(main, Mission.Scene, _profile, ProbeLog.Write, useHook);
                    // Hook = what head/face meshes use; direct writes = what the body mesh uses (only they move the root).
                    _ragdoll.DirectWrite = Environment.GetEnvironmentVariable("HSC_DIRECT") != "0" || !_ragdoll.UsesHook;
                    ProbeLog.Write("ACTIVE: pose path hook=" + _ragdoll.UsesHook + " direct=" + _ragdoll.DirectWrite);
                    _standPelvisHeight = _ragdoll.DriverBoneWorld("pelvis").z - _ragdoll.GroundZ;                }
                else if (_phase == 1 && t > 7f)
                {
                    _phase = 2;
                    Push(main, 60f, "push 1 (moderate)");
                }
                else if (_phase == 2 && t > 11f)
                {
                    _phase = 3;
                    Push(main, 220f, "push 2 (strong)");
                }
                else if (_phase == 3 && t > 15f)
                {
                    _phase = 4;
                    _ragdoll.Strength = 0f;
                    _ragdoll.Handles = false;
                    _fall = new FallDriver(_standPelvisHeight);
                    ProbeLog.Write("ACTIVE: DED (muscles 0, balance handles off), fall driver on (stand pelvis " + ProbeLog.F(_standPelvisHeight) + " m)");
                }
                else if (_phase == 4 && t > 19f)
                {
                    _phase = 5;
                    ProbeLog.Write("ACTIVE: released, driver updates=" + _ragdoll.DriverUpdates + " reuses=" + _ragdoll.DriverReuses +
                                   " hook calls=" + _ragdoll.HookCalls + " writes=" + _ragdoll.HookWrites);
                    _fall?.Stop(main);
                    _fall = null;
                    _ragdoll.Release();
                    _ragdoll = null;
                }
                else if (_phase == 5 && t > 21f)
                {
                    _phase = 6;
                    ProbeLog.Write("PROBE_DONE");
                    if (Environment.GetEnvironmentVariable("HSC_AUTOQUIT") == "1")
                    {
                        Utilities.QuitGame();
                    }
                }

                if (_ragdoll != null)
                {
                    // Before stepping: the skeleton holds what the engine kept from the pose we gave the hook last frame.
                    _errHead = Math.Max(_errHead, _ragdoll.HookPoseError("head"));
                    _errHand = Math.Max(_errHand, _ragdoll.HookPoseError("r_hand"));
                    _errPelvis = Math.Max(_errPelvis, _ragdoll.HookPoseError("pelvis"));
                    if (!_errorLogged && _ragdoll.HookError != null)
                    {
                        _errorLogged = true;
                        ProbeLog.Write("ACTIVE: hook EXCEPTION " + _ragdoll.HookError);
                    }
                    string diag = _ragdoll.TakeHookDiagnostics();
                    if (diag != null)
                    {
                        ProbeLog.Write("HOOKDIAG t=" + ProbeLog.F(t) + " " + diag);
                    }
                    if (_ragdoll.UsesHook && t >= _nextDiag)
                    {
                        _nextDiag = t + 0.5f;
                        _ragdoll.RequestHookDiagnostics();
                    }
                    _ragdoll.Step(dt, main);
                    _fall?.Update(main, _ragdoll);
                    if (t >= _nextLog)
                    {
                        _nextLog = t + 0.25f;
                        Log(t);
                    }
                }
            }
            catch (Exception e)
            {
                ProbeLog.Write("Probe EXCEPTION (active): " + e);
                _ragdoll?.Release();
                _ragdoll = null;
                _phase = 5;
            }
            finally
            {
                Watchdog.Exit();
            }
        }

        protected override void OnEndMission()
        {
            _ragdoll?.Release();
            _ragdoll = null;
            base.OnEndMission();
        }

        private void Push(Agent main, float newtonSeconds, string label)
        {
            Vec3 impulse = -main.LookDirection * newtonSeconds + new Vec3(0f, 0f, newtonSeconds * 0.1f);
            _ragdoll.Push("spine2", impulse);
            ProbeLog.Write("ACTIVE: " + label + " impulse=" + ProbeLog.F(impulse.Length) + " N.s");
        }

        private void Log(float t)
        {
            float g = _ragdoll.GroundZ;
            var sb = new System.Text.StringBuilder();
            sb.Append("ACTIVE t=").Append(ProbeLog.F(t))
              .Append(" pelvisH=").Append(ProbeLog.F(_ragdoll.BodyBoneWorld("pelvis").z - g))
              .Append(" headH=").Append(ProbeLog.F(_ragdoll.BodyBoneWorld("head").z - g))
              .Append(" gaps_cm");
            foreach (string b in GapBones)
            {
                sb.Append(' ').Append(b).Append('=').Append(Cm(_ragdoll.BodyBoneWorld(b).Distance(_ragdoll.DriverBoneWorld(b))));
            }
            if (_ragdoll.UsesHook)
            {
                sb.Append(" | hook calls=").Append(_ragdoll.HookCalls - _lastCalls).Append(" writes=").Append(_ragdoll.HookWrites - _lastWrites)
                  .Append(" thread=").Append(_ragdoll.HookThreadId).Append(" main=").Append(System.Threading.Thread.CurrentThread.ManagedThreadId)
                  .Append(" err_cm head=").Append(Cm(_errHead)).Append(" hand=").Append(Cm(_errHand)).Append(" pelvis=").Append(Cm(_errPelvis));
                _lastCalls = _ragdoll.HookCalls;
                _lastWrites = _ragdoll.HookWrites;
            }
            if (_fall != null && _fall.Active)
            {
                sb.Append(" | fall=").Append(_fall.ActionName).Append(" p=").Append(ProbeLog.F(_fall.Progress));
            }
            _errHead = _errHand = _errPelvis = 0f;
            ProbeLog.Write(sb.ToString());
        }

        private static string Cm(float metres) => (metres * 100f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
    }
}
