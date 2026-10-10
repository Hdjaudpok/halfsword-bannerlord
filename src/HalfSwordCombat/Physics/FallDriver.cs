using System;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace HalfSwordCombat.Physics
{
    /// <summary>
    /// Keeps the animated pelvis on the physical one while the body is down. The engine only takes bone rotations from
    /// the pose hook; bone positions always come from the animation (lab runs 17-20). So a fallen body needs:
    ///  - a fall animation whose progress follows the physical pelvis height (the animated pelvis goes down with it);
    ///  - the agent moved under the physical pelvis (Half Sword's capsule follows the fallen body).
    /// The visible pose stays 100% physics: the animation only supplies the pelvis position.
    /// </summary>
    internal sealed class FallDriver
    {
        private const float MaxProgress = 0.97f; // last frames of a fall: lying
        private const float LyingPelvisHeight = 0.15f;
        private const float TeleportDeadzone = 0.02f;

        private readonly float _standPelvisHeight;
        private ActionIndexCache _action = ActionIndexCache.act_none;

        public FallDriver(float standPelvisHeight)
        {
            _standPelvisHeight = standPelvisHeight;
        }

        public bool Active { get; private set; }
        public string ActionName { get; private set; } = "";
        public float Progress { get; private set; }

        /// <summary>Call once per tick after the physics step while the body is down.</summary>
        public void Update(Agent agent, ActiveRagdoll body)
        {
            Vec3 pelvis = body.BodyBoneWorld("pelvis");
            if (!Active)
            {
                // Fall direction from the physical body: head ahead of the pelvis along the agent's facing = forward fall.
                Vec3 lean = body.BodyBoneWorld("head") - pelvis;
                Vec3 facing = agent.LookDirection;
                bool forward = lean.x * facing.x + lean.y * facing.y > 0f;
                ActionName = forward ? "act_death_fall_front" : "act_death_fall_back";
                _action = ActionIndexCache.Create(ActionName);
                agent.SetActionChannel(0, in _action, ignorePriority: true);
                Active = true;
            }

            float drop = (_standPelvisHeight - (pelvis.z - body.GroundZ)) / Math.Max(0.1f, _standPelvisHeight - LyingPelvisHeight);
            Progress = MBMath.ClampFloat(drop, 0f, 1f) * MaxProgress;
            agent.SetCurrentActionProgress(0, Progress);

            // Agent under the body: shift it by the horizontal gap between the physical and the animated pelvis.
            Vec3 gap = pelvis - body.DriverBoneWorld("pelvis");
            gap.z = 0f;
            if (gap.Length > TeleportDeadzone)
            {
                agent.TeleportToPosition(agent.Position + gap);
            }
        }

        /// <summary>Back to normal animation (getting up is the animation's job from here).</summary>
        public void Stop(Agent agent)
        {
            if (Active)
            {
                agent.SetActionChannel(0, ActionIndexCache.act_none, ignorePriority: true);
                Active = false;
            }
        }
    }
}
