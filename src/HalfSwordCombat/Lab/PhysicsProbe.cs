using System;
using HalfSwordCombat.Physics;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace HalfSwordCombat.Lab
{
    /// <summary>
    /// Phase 2a: our own physics (BEPUphysics v1) drives the main agent's frozen skeleton.
    /// Timeline after the main agent is ready: 3s freeze + build ragdoll, 4s push the chest backwards,
    /// 3-11s simulate and write bones, 11s release, 13s done.
    /// </summary>
    internal sealed class PhysicsProbe : MissionBehavior
    {
        public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;

        private float _t0 = -1f;
        private int _phase;
        private bool _pushed;
        private float _nextLog;
        private Ragdoll _ragdoll;
        private sbyte _head = -1;

        private float T => Mission.CurrentTime - _t0;

        public override void OnMissionTick(float dt)
        {
            Watchdog.Enter("PhysicsProbe.OnMissionTick");
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
                    Watchdog.Start();
                    _t0 = Mission.CurrentTime;
                    _head = main.Monster.HeadLookDirectionBoneIndex;
                    ProbeLog.Write("PHYSICS: main=" + main.Name + " mounted=" + main.HasMount);
                }
                float t = T;

                if (_phase == 0 && t > 3f)
                {
                    _phase = 1;
                    sk.Freeze(true);
                    _ragdoll = Ragdoll.Build(main, Mission.Scene, ProbeLog.Write);
                    ProbeLog.Write("PHYSICS: ragdoll active, frozen=" + sk.IsFrozen());
                }
                else if (_phase == 1)
                {
                    if (!_pushed && t > 4f)
                    {
                        _pushed = true;
                        Vec3 push = -main.LookDirection * 35f + new Vec3(0f, 0f, 5f);
                        _ragdoll.Push("spine2", push, sk);
                        ProbeLog.Write("PHYSICS: push chest impulse=" + V(push));
                    }
                    _ragdoll.Step(dt, sk);
                    if (t >= _nextLog)
                    {
                        _nextLog = t + 0.25f;
                        ProbeLog.Write("PHYSICS t=" + ProbeLog.F(t) + " pelvis=" + V(_ragdoll.WorldOf(0)) +
                                       " head=" + V(_ragdoll.WorldOf(_head)) + " agentPos=" + V(main.Position));
                    }
                    if (t > 11f)
                    {
                        _phase = 2;
                        _ragdoll = null;
                        sk.Freeze(false);
                        ProbeLog.Write("PHYSICS: released, frozen=" + sk.IsFrozen());
                    }
                }
                else if (_phase == 2 && t > 13f)
                {
                    _phase = 3;
                    ProbeLog.Write("PROBE_DONE");
                    if (Environment.GetEnvironmentVariable("HSC_AUTOQUIT") == "1")
                    {
                        Utilities.QuitGame();
                    }
                }
            }
            catch (Exception e)
            {
                ProbeLog.Write("Probe EXCEPTION (physics): " + e);
                _phase = 2;
                _ragdoll = null;
            }
            finally
            {
                Watchdog.Exit();
            }
        }

        private static string V(Vec3 v) => "(" + ProbeLog.F(v.x) + "," + ProbeLog.F(v.y) + "," + ProbeLog.F(v.z) + ")";
    }
}
