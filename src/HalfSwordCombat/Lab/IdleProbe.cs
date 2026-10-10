using System;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.MountAndBlade;

namespace HalfSwordCombat.Lab
{
    /// <summary>
    /// Control run: no physics, no bone writes. Logs mission time, living agents and the first melee hits, with the
    /// watchdog on, for 60 s. Lab runs 3, 11, 12 and 13 froze 20-23 s after the battle started (about when the armies
    /// meet): if this run freezes too, the freeze is not caused by our code.
    /// </summary>
    internal sealed class IdleProbe : MissionBehavior
    {
        public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;

        private float _t0 = -1f;
        private float _next;
        private int _hits;
        private bool _done;

        private float T => Mission.CurrentTime - _t0;

        public override void OnMissionTick(float dt)
        {
            Watchdog.Enter("IdleProbe.OnMissionTick");
            try
            {
                if (_t0 < 0f)
                {
                    if (Mission.MainAgent == null)
                    {
                        return;
                    }
                    Watchdog.Start();
                    _t0 = Mission.CurrentTime;
                    ProbeLog.Write("IDLE: control run, no physics; logging every 2 s until 60 s");
                }
                float t = T;
                if (t >= _next)
                {
                    _next = t + 2f;
                    int alive = 0;
                    foreach (Agent a in Mission.Agents)
                    {
                        if (a.IsHuman && a.IsActive())
                        {
                            alive++;
                        }
                    }
                    ProbeLog.Write("IDLE t=" + ProbeLog.F(t) + " humans alive=" + alive + " hits=" + _hits +
                                   " main=" + (Mission.MainAgent != null && Mission.MainAgent.IsActive() ? "alive" : "gone"));
                }
                if (!_done && t > 60f)
                {
                    _done = true;
                    ProbeLog.Write("PROBE_DONE");
                    if (Environment.GetEnvironmentVariable("HSC_AUTOQUIT") == "1")
                    {
                        Utilities.QuitGame();
                    }
                }
            }
            finally
            {
                Watchdog.Exit();
            }
        }

        public override void OnAgentHit(Agent affectedAgent, Agent affectorAgent, in MissionWeapon affectorWeapon, in Blow blow, in AttackCollisionData attackCollisionData)
        {
            _hits++;
            if (_hits <= 3 && _t0 >= 0f)
            {
                ProbeLog.Write("IDLE: hit #" + _hits + " at t=" + ProbeLog.F(T) + " damage=" + blow.InflictedDamage +
                               (affectedAgent == Mission.MainAgent ? " (on main agent)" : ""));
            }
        }
    }
}
