using System;
using System.Linq;
using System.Text;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace HalfSwordCombat.Lab
{
    /// <summary>
    /// Answers two questions in a live battle, by logging state rather than guessing:
    ///  1. Does a living agent survive StartRagdollAsCorpse/EndRagdollAsCorpse and walk again?
    ///  2. Does Skeleton.SetBoneLocalFrame on the main agent's forearm survive the next animation tick,
    ///     when written from OnMissionTick vs OnPreDisplayMissionTick?
    /// Timeline (seconds after the main agent is ready) is printed in the log.
    /// </summary>
    internal sealed class RagdollBoneProbe : MissionBehavior
    {
        public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;

        private float _t0 = -1f;
        private int _phase;
        private float _nextLog;
        private Agent _a;
        private Agent _b;
        private sbyte _forearm = -1;
        private sbyte _hand = -1;
        private bool _wroteLastTick;
        private Vec3 _handAfterWrite;
        private int _boneHook; // 0 none, 1 OnMissionTick, 2 OnPreDisplayMissionTick

        private float T => Mission.CurrentTime - _t0;

        public override void OnMissionTick(float dt)
        {
            try
            {
                Agent main = Mission.MainAgent;
                if (_t0 < 0f)
                {
                    if (main == null || !main.IsActive())
                    {
                        return;
                    }
                    _t0 = Mission.CurrentTime;
                    ProbeLog.Write("Probe: main agent ready, active agents=" + Mission.Agents.Count +
                                   " | timeline: 3s ragdoll A (+force), 5s end A, 8s ragdoll B, 9s end B, 12-16s bone write in OnMissionTick, 16-20s in OnPreDisplayMissionTick, 22s done");
                    DumpSkeleton(main);
                }

                float t = T;
                // Lab run 2: StartRagdollAsCorpse + ApplyForceOnRagdoll in the same tick -> AccessViolationException,
                // then the main thread hung 9 s later. One variable per run now (HSC_PROBE), force only once the
                // ragdoll reports Active.
                if (_phase == 0 && t > 3f)
                {
                    _phase = 1;
                    if (Has("ragdoll") || Has("force"))
                    {
                        _a = PickEnemy(main, null);
                        StartRagdoll("A", _a, false);
                    }
                }
                else if (_phase == 1)
                {
                    if (Has("force") && !_forceDone && _a != null && RagState(_a) == "Active" && t > 3.3f)
                    {
                        _forceDone = true;
                        ApplyForce("A", _a);
                    }
                    if (t > 6f)
                    {
                        EndRagdoll("A", _a);
                        _phase = 4;
                    }
                }
                else if (_phase == 4 && t > 12f)
                {
                    _phase = Has("bone") ? 5 : 7;
                    if (_phase == 5) { _boneHook = 1; ProbeLog.Write("BONE: writing forearm from OnMissionTick"); }
                }
                else if (_phase == 5 && t > 16f) { _boneHook = 2; _phase = 6; ProbeLog.Write("BONE: writing forearm from OnPreDisplayMissionTick"); }
                else if (_phase == 6 && t > 20f) { _boneHook = 0; _phase = 7; ProbeLog.Write("BONE: stopped writing"); }
                else if (_phase == 7 && t > 22f)
                {
                    _phase = 8;
                    ProbeLog.Write("PROBE_DONE");
                    if (Environment.GetEnvironmentVariable("HSC_AUTOQUIT") == "1")
                    {
                        Utilities.QuitGame();
                    }
                }

                if (t >= _nextLog)
                {
                    _nextLog = t + 0.25f;
                    LogAgent("A", _a);
                    LogAgent("B", _b);
                }

                if (_boneHook == 1 && main != null && main.IsActive())
                {
                    BoneStep(main, "tick");
                }
            }
            catch (Exception e)
            {
                ProbeLog.Write("Probe EXCEPTION (tick): " + e);
                _phase = 8;
            }
        }

        public override void OnPreDisplayMissionTick(float dt)
        {
            try
            {
                Agent main = Mission.MainAgent;
                if (_boneHook == 2 && main != null && main.IsActive())
                {
                    BoneStep(main, "predisplay");
                }
            }
            catch (Exception e)
            {
                ProbeLog.Write("Probe EXCEPTION (predisplay): " + e);
                _boneHook = 0;
            }
        }

        private Agent PickEnemy(Agent main, Agent exclude)
        {
            Agent pick = Mission.Agents
                .Where(x => x != exclude && x.IsHuman && x.IsActive() && x.IsEnemyOf(main))
                .OrderBy(x => x.Position.DistanceSquared(main.Position))
                .FirstOrDefault();
            ProbeLog.Write("PickEnemy -> " + (pick == null ? "<none>" : pick.Index + " " + pick.Name + " dist=" + ProbeLog.F(pick.Position.Distance(main.Position))));
            return pick;
        }

        private static readonly string Probes = (Environment.GetEnvironmentVariable("HSC_PROBE") ?? "ragdoll,bone").ToLowerInvariant();
        private bool _forceDone;

        private static bool Has(string probe) => Probes.Contains(probe);

        private static string RagState(Agent agent) =>
            agent?.AgentVisuals != null ? agent.AgentVisuals.GetCurrentRagdollState().ToString() : "?";

        private void StartRagdoll(string tag, Agent agent, bool unused)
        {
            if (agent == null)
            {
                return;
            }
            ProbeLog.Write("RAGDOLL " + tag + ": StartRagdollAsCorpse (state before=" + RagState(agent) + ")");
            agent.StartRagdollAsCorpse();
            ProbeLog.Write("RAGDOLL " + tag + ": StartRagdollAsCorpse returned (state=" + RagState(agent) + ")");
        }

        private void ApplyForce(string tag, Agent agent)
        {
            // Direction/space of ApplyForceOnRagdoll is unknown: push backwards and up, then watch the positions.
            Vec3 force = -agent.LookDirection * 1500f + new Vec3(0f, 0f, 500f);
            sbyte thorax = agent.Monster.ThoraxLookDirectionBoneIndex;
            ProbeLog.Write("RAGDOLL " + tag + ": ApplyForceOnRagdoll bone=" + thorax + " force=" + V(force) + " (state=" + RagState(agent) + ")");
            agent.ApplyForceOnRagdoll(thorax, in force);
            ProbeLog.Write("RAGDOLL " + tag + ": ApplyForceOnRagdoll returned");
        }

        private void EndRagdoll(string tag, Agent agent)
        {
            if (agent == null)
            {
                return;
            }
            ProbeLog.Write("RAGDOLL " + tag + ": EndRagdollAsCorpse");
            agent.EndRagdollAsCorpse();
        }

        private void LogAgent(string tag, Agent agent)
        {
            if (agent == null)
            {
                return;
            }
            string rag = agent.AgentVisuals != null ? agent.AgentVisuals.GetCurrentRagdollState().ToString() : "?";
            string action = "?";
            try { action = agent.GetCurrentAction(0).GetName() + "@" + ProbeLog.F(agent.GetCurrentActionProgress(0)); } catch { }
            ProbeLog.Write("AGENT " + tag + " t=" + ProbeLog.F(T) + " state=" + agent.State + " active=" + agent.IsActive() +
                           " hp=" + ProbeLog.F(agent.Health) + " rag=" + rag + " pos=" + V(agent.Position) +
                           " vel=" + V(agent.Velocity) + " ai=" + agent.IsAIControlled + " act0=" + action);
        }

        private void DumpSkeleton(Agent main)
        {
            Skeleton sk = main.AgentVisuals.GetSkeleton();
            int n = sk.GetBoneCount();
            StringBuilder sb = new StringBuilder("SKELETON bones=" + n + ":");
            for (sbyte i = 0; i < n; i++)
            {
                sb.Append(' ').Append(i).Append('=').Append(sk.GetBoneName(i)).Append("(p").Append(sk.GetParentBoneIndex(i)).Append(')');
            }
            ProbeLog.Write(sb.ToString());

            // The forearm is the bone between the right upper arm and the main hand.
            Monster m = main.Monster;
            _hand = m.MainHandBoneIndex;
            sbyte cur = _hand;
            while (cur >= 0 && sk.GetParentBoneIndex(cur) != m.RightUpperArmBoneIndex)
            {
                cur = sk.GetParentBoneIndex(cur);
            }
            _forearm = cur;
            ProbeLog.Write("MONSTER rightUpperArm=" + m.RightUpperArmBoneIndex + " forearm=" + _forearm +
                           " mainHand=" + _hand + " mainHandItem=" + m.MainHandItemBoneIndex + " thorax=" + m.ThoraxLookDirectionBoneIndex);
        }

        private void BoneStep(Agent main, string hook)
        {
            if (_forearm < 0)
            {
                return;
            }
            Skeleton sk = main.AgentVisuals.GetSkeleton();
            Vec3 before = sk.GetBoneEntitialFrame(_hand).origin;
            if (_wroteLastTick)
            {
                // Did last frame's override survive until now? (distance from where we left the hand)
                ProbeLog.Write("BONE " + hook + " t=" + ProbeLog.F(T) + " handBeforeWrite=" + V(before) +
                               " driftFromLastWrite=" + ProbeLog.F(before.Distance(_handAfterWrite)));
            }
            MatrixFrame local = sk.GetBoneLocalRestFrame(_forearm);
            local.rotation.RotateAboutSide(1.4f); // ~80 degrees: an obviously wrong elbow, easy to see on screen
            sk.SetBoneLocalFrame(_forearm, local);
            sk.ForceUpdateBoneFrames();
            _handAfterWrite = sk.GetBoneEntitialFrame(_hand).origin;
            _wroteLastTick = true;
            ProbeLog.Write("BONE " + hook + " t=" + ProbeLog.F(T) + " handAfterWrite=" + V(_handAfterWrite) +
                           " moved=" + ProbeLog.F(_handAfterWrite.Distance(before)));
        }

        private static string V(Vec3 v) => "(" + ProbeLog.F(v.x) + "," + ProbeLog.F(v.y) + "," + ProbeLog.F(v.z) + ")";
    }
}
