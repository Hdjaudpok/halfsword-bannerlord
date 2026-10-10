using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace HalfSwordCombat.Lab
{
    /// <summary>
    /// Step 1 architecture test: can Bannerlord keep animating an agent (the Half Sword "driver skeleton") while we
    /// overwrite the displayed pose every frame, WITHOUT Skeleton.Freeze?
    /// Per subject and per frame: write a bent forearm with SetBoneLocalFrame + UpdateEntitialFramesFromLocalFrames,
    /// then measure (entity space, metres):
    ///   moved = hand displacement caused by our write             (~0.35 if the write applies)
    ///   gap   = this frame's animated hand vs last frame's written hand (~moved if the animation re-evaluates cleanly)
    ///   drift = this frame's animated hand vs last frame's animated hand (animation still running)
    /// Timeline: 3-7 s write in OnMissionTick, 7-11 s in OnPreDisplayMissionTick, 11 s stop, 13 s done.
    /// </summary>
    internal sealed class DriverProbe : MissionBehavior
    {
        public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;

        private sealed class Subject
        {
            public string Tag;
            public Agent Agent;
            public sbyte Forearm;
            public sbyte Hand;
            public bool HasPrev;
            public Vec3 PrevBefore;
            public Vec3 PrevAfter;
            public float Moved;
            public float Gap;
            public float Drift;
            public int Frames;
        }

        private static readonly bool Visual = Environment.GetEnvironmentVariable("HSC_DRIVER_VISUAL") == "1";

        private readonly List<Subject> _subjects = new List<Subject>();
        private float _t0 = -1f;
        private int _phase;
        private int _hook; // 0 none, 1 OnMissionTick, 2 OnPreDisplayMissionTick
        private float _nextLog;

        private float T => Mission.CurrentTime - _t0;

        public override void OnMissionTick(float dt)
        {
            Watchdog.Enter("DriverProbe.OnMissionTick");
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
                    Add("main", main);
                    Add("ally", Mission.Agents
                        .Where(a => a != main && a.IsHuman && a.IsActive() && a.Team == main.Team)
                        .OrderBy(a => a.Position.DistanceSquared(main.Position))
                        .FirstOrDefault());
                    ProbeLog.Write("DRIVER: subjects=" + string.Join(",", _subjects.Select(s => s.Tag + ":" + s.Agent.Name)) +
                                   " | 3-7s write in OnMissionTick, 7-11s in OnPreDisplayMissionTick, no Freeze");
                }

                float t = T;
                if (_phase == 0 && t > 3f) { _phase = 1; _hook = 1; ProbeLog.Write("DRIVER: write from OnMissionTick"); }
                else if (_phase == 1 && t > 7f) { Flush(); _phase = 2; _hook = 2; Reset(); ProbeLog.Write("DRIVER: write from OnPreDisplayMissionTick"); }
                else if (_phase == 2 && t > 11f) { Flush(); _phase = 3; _hook = 0; ProbeLog.Write("DRIVER: stopped writing"); }
                else if (_phase == 3 && t > 13f)
                {
                    _phase = 4;
                    ProbeLog.Write("PROBE_DONE");
                    if (Environment.GetEnvironmentVariable("HSC_AUTOQUIT") == "1")
                    {
                        Utilities.QuitGame();
                    }
                }

                if (_hook == 1)
                {
                    StepAll();
                }
                if (_hook != 0 && t >= _nextLog)
                {
                    _nextLog = t + 1f;
                    Flush();
                }
            }
            catch (Exception e)
            {
                ProbeLog.Write("Probe EXCEPTION (driver): " + e);
                _hook = 0;
                _phase = 3;
            }
            finally
            {
                Watchdog.Exit();
            }
        }

        public override void OnPreDisplayMissionTick(float dt)
        {
            if (_hook != 2)
            {
                return;
            }
            Watchdog.Enter("DriverProbe.OnPreDisplayMissionTick");
            try
            {
                StepAll();
            }
            catch (Exception e)
            {
                ProbeLog.Write("Probe EXCEPTION (driver predisplay): " + e);
                _hook = 0;
            }
            finally
            {
                Watchdog.Exit();
            }
        }

        private void Add(string tag, Agent agent)
        {
            if (agent == null)
            {
                ProbeLog.Write("DRIVER: no subject for " + tag);
                return;
            }
            Skeleton sk = agent.AgentVisuals.GetSkeleton();
            sbyte hand = agent.Monster.MainHandBoneIndex;
            sbyte forearm = sk.GetParentBoneIndex(hand);
            while (forearm >= 0 && sk.GetParentBoneIndex(forearm) != agent.Monster.RightUpperArmBoneIndex)
            {
                forearm = sk.GetParentBoneIndex(forearm);
            }
            if (Visual)
            {
                // Visual variant: the upper arm itself goes to its rest (T-pose) frame, an arm held straight out
                // sideways, which is obvious from the chase camera behind the player.
                forearm = agent.Monster.RightUpperArmBoneIndex;
            }
            _subjects.Add(new Subject { Tag = tag, Agent = agent, Forearm = forearm, Hand = hand });
        }

        private void StepAll()
        {
            foreach (Subject s in _subjects)
            {
                if (s.Agent == null || !s.Agent.IsActive() || s.Forearm < 0)
                {
                    continue;
                }
                Skeleton sk = s.Agent.AgentVisuals.GetSkeleton();
                Vec3 before = sk.GetBoneEntitialFrame(s.Hand).origin; // this frame's animated pose (the "driver")
                if (s.HasPrev)
                {
                    s.Gap += before.Distance(s.PrevAfter);
                    s.Drift += before.Distance(s.PrevBefore);
                }
                MatrixFrame local = sk.GetBoneLocalRestFrame(s.Forearm);
                if (!Visual)
                {
                    local.rotation.RotateAboutSide(1.4f);
                }
                sk.SetBoneLocalFrame(s.Forearm, local);
                if (Visual && s.Tag == "main")
                {
                    sbyte left = s.Agent.Monster.LeftUpperArmBoneIndex;
                    sk.SetBoneLocalFrame(left, sk.GetBoneLocalRestFrame(left));
                }
                sk.UpdateEntitialFramesFromLocalFrames();
                Vec3 after = sk.GetBoneEntitialFrame(s.Hand).origin;
                s.Moved += after.Distance(before);
                s.Frames++;
                s.PrevBefore = before;
                s.PrevAfter = after;
                s.HasPrev = true;
            }
        }

        private void Flush()
        {
            foreach (Subject s in _subjects)
            {
                if (s.Frames == 0)
                {
                    continue;
                }
                float n = s.Frames;
                ProbeLog.Write("DRIVER " + s.Tag + " t=" + ProbeLog.F(T) + " frames=" + s.Frames +
                               " moved=" + ProbeLog.F(s.Moved / n) + " gap=" + ProbeLog.F(s.Gap / Math.Max(1f, n - 1f)) +
                               " drift=" + ProbeLog.F(s.Drift / Math.Max(1f, n - 1f)) +
                               " speed=" + ProbeLog.F(s.Agent.Velocity.Length));
                s.Moved = s.Gap = s.Drift = 0f;
                s.Frames = 0;
            }
        }

        private void Reset()
        {
            foreach (Subject s in _subjects)
            {
                s.HasPrev = false;
                s.Moved = s.Gap = s.Drift = 0f;
                s.Frames = 0;
            }
        }
    }
}
