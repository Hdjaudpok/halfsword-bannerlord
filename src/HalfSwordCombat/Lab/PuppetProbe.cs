using System;
using System.Reflection;
using System.Text;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace HalfSwordCombat.Lab
{
    /// <summary>
    /// Decisive test for the "puppet" route: can we take over the main agent's own skeleton?
    /// Freeze(true) stops animation evaluation; then SetBoneLocalFrame + UpdateEntitialFramesFromLocalFrames
    /// (run 3 used ForceUpdateBoneFrames, which re-evaluates the animation and erased the write).
    /// Timeline after the main agent is ready: 1s dump capsules, 4s freeze, 4-7s write in OnMissionTick,
    /// 7-10s write in OnPreDisplayMissionTick, 10s unfreeze, 12s done.
    /// </summary>
    internal sealed class PuppetProbe : MissionBehavior
    {
        public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;

        private float _t0 = -1f;
        private int _phase;
        private int _hook; // 0 none, 1 OnMissionTick, 2 OnPreDisplayMissionTick
        private sbyte _forearm = -1;
        private sbyte _hand = -1;
        private bool _wrote;
        private Vec3 _handAfterWrite;
        private float _nextLog;

        private float T => Mission.CurrentTime - _t0;

        public override void OnMissionTick(float dt)
        {
            try
            {
                Agent main = Mission.MainAgent;
                if (main == null || !main.IsActive())
                {
                    return;
                }
                Skeleton sk = main.AgentVisuals.GetSkeleton();
                if (_t0 < 0f)
                {
                    _t0 = Mission.CurrentTime;
                    _hand = main.Monster.MainHandBoneIndex;
                    _forearm = sk.GetParentBoneIndex(_hand);
                    while (_forearm >= 0 && sk.GetParentBoneIndex(_forearm) != main.Monster.RightUpperArmBoneIndex)
                    {
                        _forearm = sk.GetParentBoneIndex(_forearm);
                    }
                    ProbeLog.Write("PUPPET: main=" + main.Name + " mounted=" + main.HasMount + " skeleton=" + sk.GetName() +
                                   " forearm=" + _forearm + " hand=" + _hand + " frozen=" + sk.IsFrozen());
                }

                float t = T;
                if (_phase == 0 && t > 1f)
                {
                    _phase = 1;
                    foreach (sbyte b in new[] { main.Monster.RightUpperArmBoneIndex, _forearm, _hand, main.Monster.ThoraxLookDirectionBoneIndex, main.Monster.PelvisBoneIndex })
                    {
                        ProbeLog.Write("CAPSULE bone=" + b + " " + sk.GetBoneName(b) + " " + DumpCapsule(sk, b));
                    }
                }
                else if (_phase == 1 && t > 4f)
                {
                    _phase = 2;
                    sk.Freeze(true);
                    _hook = 1;
                    ProbeLog.Write("PUPPET: Freeze(true) -> frozen=" + sk.IsFrozen() + "; write from OnMissionTick");
                }
                else if (_phase == 2 && t > 7f)
                {
                    _phase = 3;
                    _hook = 2;
                    _wrote = false;
                    ProbeLog.Write("PUPPET: write from OnPreDisplayMissionTick");
                }
                else if (_phase == 3 && t > 10f)
                {
                    _phase = 4;
                    _hook = 0;
                    sk.Freeze(false);
                    ProbeLog.Write("PUPPET: Freeze(false) -> frozen=" + sk.IsFrozen());
                }
                else if (_phase == 4 && t > 12f)
                {
                    _phase = 5;
                    ProbeLog.Write("PROBE_DONE");
                    if (Environment.GetEnvironmentVariable("HSC_AUTOQUIT") == "1")
                    {
                        Utilities.QuitGame();
                    }
                }

                if (t >= _nextLog && _phase < 5)
                {
                    _nextLog = t + 0.5f;
                    ProbeLog.Write("MAIN t=" + ProbeLog.F(t) + " pos=" + V(main.Position) + " vel=" + V(main.Velocity) +
                                   " frozen=" + sk.IsFrozen() + " hand=" + V(sk.GetBoneEntitialFrame(_hand).origin));
                }

                if (_hook == 1)
                {
                    Write(sk, "tick");
                }
            }
            catch (Exception e)
            {
                ProbeLog.Write("Probe EXCEPTION (puppet tick): " + e);
                _hook = 0;
                _phase = 4;
            }
        }

        public override void OnPreDisplayMissionTick(float dt)
        {
            try
            {
                Agent main = Mission.MainAgent;
                if (_hook == 2 && main != null && main.IsActive())
                {
                    Write(main.AgentVisuals.GetSkeleton(), "predisplay");
                }
            }
            catch (Exception e)
            {
                ProbeLog.Write("Probe EXCEPTION (puppet predisplay): " + e);
                _hook = 0;
            }
        }

        private void Write(Skeleton sk, string hook)
        {
            Vec3 before = sk.GetBoneEntitialFrame(_hand).origin;
            string drift = _wrote ? ProbeLog.F(before.Distance(_handAfterWrite)) : "-";
            MatrixFrame local = sk.GetBoneLocalRestFrame(_forearm);
            local.rotation.RotateAboutSide(1.4f); // ~80 degrees at the elbow: obvious on screen
            sk.SetBoneLocalFrame(_forearm, local);
            sk.UpdateEntitialFramesFromLocalFrames();
            _handAfterWrite = sk.GetBoneEntitialFrame(_hand).origin;
            _wrote = true;
            ProbeLog.Write("PUPPET " + hook + " t=" + ProbeLog.F(T) + " moved=" + ProbeLog.F(_handAfterWrite.Distance(before)) +
                           " driftSinceLastWrite=" + drift);
        }

        private static string DumpCapsule(Skeleton sk, sbyte bone)
        {
            CapsuleData data = default(CapsuleData);
            sk.GetBoneBody(bone, ref data);
            object boxed = data;
            StringBuilder sb = new StringBuilder();
            foreach (FieldInfo f in typeof(CapsuleData).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                sb.Append(f.Name).Append('=').Append(f.GetValue(boxed)).Append(' ');
            }
            return sb.ToString();
        }

        private static string V(Vec3 v) => "(" + ProbeLog.F(v.x) + "," + ProbeLog.F(v.y) + "," + ProbeLog.F(v.z) + ")";
    }
}
