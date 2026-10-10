using System;
using HalfSwordCombat.Lab;
using TaleWorlds.MountAndBlade;

namespace HalfSwordCombat
{
    public sealed class SubModule : MBSubModuleBase
    {
        // Lab code only runs when the lab launcher sets HSC_LAB=1, so normal play is never affected.
        internal static bool LabMode => Environment.GetEnvironmentVariable("HSC_LAB") == "1";

        internal static string Probes => (Environment.GetEnvironmentVariable("HSC_PROBE") ?? "").ToLowerInvariant();

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            ProbeLog.Write("SubModule loaded, lab=" + LabMode + " probes=" + Probes + " runtime=" +
                           System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
            if (LabMode)
            {
                LabExceptions.Install();
            }
            // One class at a time: a patch that fails (e.g. after a game update) must not take the others down with it.
            var harmony = new HarmonyLib.Harmony("com.halfswordcombat");
            foreach (Type type in typeof(SubModule).Assembly.GetTypes())
            {
                if (type.GetCustomAttributes(typeof(HarmonyLib.HarmonyPatch), false).Length == 0)
                {
                    continue;
                }
                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                    ProbeLog.Write("Harmony patch applied: " + type.Name);
                }
                catch (Exception e)
                {
                    ProbeLog.Write("Harmony patch FAILED: " + type.Name + ": " + e.GetBaseException().Message);
                }
            }
        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            base.OnBeforeInitialModuleScreenSetAsRoot();
            try
            {
                HscCommands.Register();
            }
            catch (Exception e)
            {
                ProbeLog.Write("Console commands FAILED: " + e.GetBaseException().Message);
            }
        }

        protected override void OnApplicationTick(float dt)
        {
            base.OnApplicationTick(dt);
            if (LabMode)
            {
                AutoTest.Tick();
                Link.HsLink.LabTick();
            }
        }

        public override void OnMissionBehaviorInitialize(Mission mission)
        {
            base.OnMissionBehaviorInitialize(mission);
            if (!LabMode)
            {
                mission.AddMissionBehavior(new Combat.HsPlayerBehavior());
                return;
            }
            if (Probes.Contains("player"))
            {
                mission.AddMissionBehavior(new Combat.HsPlayerBehavior());
            }
            if (Probes.Contains("puppet"))
            {
                mission.AddMissionBehavior(new PuppetProbe());
            }
            if (Probes.Contains("physics"))
            {
                mission.AddMissionBehavior(new PhysicsProbe());
            }
            if (Probes.Contains("driver"))
            {
                mission.AddMissionBehavior(new DriverProbe());
            }
            if (Probes.Contains("active"))
            {
                mission.AddMissionBehavior(new ActiveProbe());
            }
            if (Probes.Contains("aim"))
            {
                mission.AddMissionBehavior(new AimProbe());
            }
            if (Probes.Contains("idle"))
            {
                mission.AddMissionBehavior(new IdleProbe());
            }
            if (Probes.Contains("ragdoll") || Probes.Contains("force") || Probes.Contains("bone"))
            {
                mission.AddMissionBehavior(new RagdollBoneProbe());
            }
        }
    }
}
