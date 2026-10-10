using HarmonyLib;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.MissionViews;
using TaleWorlds.MountAndBlade.View.Screens;

namespace HalfSwordCombat.Combat
{
    /// <summary>State shared with the Harmony patches: which agent the player drives the Half Sword way.</summary>
    internal static class HsControl
    {
        /// <summary>The player's agent while it is under Half Sword control, null otherwise.</summary>
        public static Agent Agent;

        /// <summary>An aim button is held: the mouse drives the arm, the camera barely turns.</summary>
        public static bool Guarding;

        public static float CameraScaleX = 1f, CameraScaleY = 1f;

        /// <summary>Left Alt held while guarding: Half Sword's manual thrust (not Bannerlord's Show Indicators).</summary>
        public static bool ManualThrust;

        public static void Clear()
        {
            Agent = null;
            Guarding = false;
            ManualThrust = false;
            CameraScaleX = CameraScaleY = 1f;
        }
    }

    /// <summary>
    /// No native combat for the player under Half Sword control (user's design, 2026-10-03: the old system must not
    /// overlap Half Sword's): the attack (9) and defend (10) keys only hold the arm guards, and the native kick is off.
    /// ControlTick sets the movement and event flags from the keys; they are cleared right after, before the agent tick
    /// reads them.
    /// </summary>
    [HarmonyPatch(typeof(MissionMainAgentController), "ControlTick")]
    internal static class NativeAttackPatch
    {
        private static void Postfix(MissionMainAgentController __instance)
        {
            Agent agent = HsControl.Agent;
            if (agent == null || agent != __instance.Mission?.MainAgent)
            {
                return;
            }
            agent.MovementFlags &= ~(Agent.MovementControlFlag.AttackMask | Agent.MovementControlFlag.DefendMask);
            if ((agent.EventControlFlags & Agent.EventControlFlag.Kick) != 0)
            {
                agent.EventControlFlags &= ~Agent.EventControlFlag.Kick;
            }
        }
    }

    /// <summary>
    /// Left Alt is Half Sword's manual thrust while an arm guards; Bannerlord reads the same key as Show Indicators (game
    /// key 5: troop outlines and labels). During a manual thrust that key reads as up, everywhere else it is untouched.
    /// </summary>
    [HarmonyPatch(typeof(TaleWorlds.InputSystem.InputContext), "IsGameKeyDown", new[] { typeof(int) })]
    internal static class ShowIndicatorsPatch
    {
        private static bool Prefix(int gameKey, ref bool __result)
        {
            if (gameKey == 5 && HsControl.ManualThrust)
            {
                __result = false;
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// No native attack-direction arrows around the crosshair under Half Sword control: they show the native swing the
    /// mouse would pick, which no longer exists (the arm follows the mouse instead).
    /// </summary>
    [HarmonyPatch(typeof(TaleWorlds.MountAndBlade.ViewModelCollection.HUD.CrosshairVM), "SetArrowProperties")]
    internal static class AttackArrowsPatch
    {
        private static void Prefix(ref double topArrowOpacity, ref double rightArrowOpacity, ref double bottomArrowOpacity, ref double leftArrowOpacity)
        {
            if (HsControl.Agent != null)
            {
                topArrowOpacity = rightArrowOpacity = bottomArrowOpacity = leftArrowOpacity = 0.0;
            }
        }
    }

    /// <summary>
    /// Lab only: "lab_cam = yaw distance height" in the live tuning file orbits the rendered view around the controlled
    /// agent (yaw 0 = in front of it, 90 = its right side), to inspect the pose from any side without touching the
    /// mouse. Only the drawn camera frame changes: the mission keeps the real camera, so the agent still faces forward.
    /// </summary>
    [HarmonyPatch(typeof(MissionScreen), "UpdateCamera")]
    internal static class LabCameraPatch
    {
        private static void Postfix(MissionScreen __instance)
        {
            Agent agent = HsControl.Agent;
            if (!SubModule.LabMode || agent == null || __instance.CombatCamera == null)
            {
                return;
            }
            TaleWorlds.Library.Vec3 cam = LiveTuning.GetVec(null, "lab_cam");
            if (cam.y <= 0f)
            {
                return;
            }
            TaleWorlds.Library.Vec3 forward = agent.AgentVisuals.GetGlobalFrame().rotation.f; // body facing
            forward.z = 0f;
            forward.Normalize();
            TaleWorlds.Library.Vec3 right = TaleWorlds.Library.Vec3.CrossProduct(forward, TaleWorlds.Library.Vec3.Up);
            float yaw = cam.x * (float)System.Math.PI / 180f;
            TaleWorlds.Library.Vec3 around = forward * (float)System.Math.Cos(yaw) + right * (float)System.Math.Sin(yaw);
            // "lab_look = side forward up" (metres from the feet, body axes) is the point looked at; default chest height.
            TaleWorlds.Library.Vec3 look = LiveTuning.GetVec(null, "lab_look");
            if (look.LengthSquared < 1e-6f)
            {
                look = new TaleWorlds.Library.Vec3(0f, 0f, 1.3f);
            }
            TaleWorlds.Library.Vec3 target = agent.Position + right * look.x + forward * look.y + TaleWorlds.Library.Vec3.Up * look.z;
            TaleWorlds.Library.Vec3 eye = agent.Position + around * cam.y + TaleWorlds.Library.Vec3.Up * cam.z;
            TaleWorlds.Library.Vec3 back = eye - target;
            back.Normalize();
            TaleWorlds.Library.Vec3 up = TaleWorlds.Library.Vec3.Up - back * TaleWorlds.Library.Vec3.DotProduct(TaleWorlds.Library.Vec3.Up, back);
            up.Normalize();
            TaleWorlds.Library.Vec3 side = TaleWorlds.Library.Vec3.CrossProduct(up, back);
            __instance.CombatCamera.Frame = new TaleWorlds.Library.MatrixFrame(new TaleWorlds.Library.Mat3(side, up, back), eye);
            __instance.SceneView?.SetCamera(__instance.CombatCamera); // the view copies the camera when it is set
        }
    }

    /// <summary>
    /// While an aim button is held the mouse moves the arm: the camera turn is scaled down.
    /// HandleUserInput (1.4.8) zeroes _cameraBearingDelta / _cameraElevationDelta, adds this frame's mouse and adds them
    /// to CameraBearing / CameraElevation itself, so the old postfix that scaled the deltas afterwards changed nothing
    /// (decompiled MissionScreen lines 2979-3040, MODLOG 2026-10-06). The prefix keeps the angles from before the call;
    /// the postfix keeps only the scaled share of what the call turned. The agent's look direction is built from these
    /// angles (MissionMainAgentController), so it follows the same rule. Live tuning (user, 2026-10-06 11:40):
    ///  - cam_guard (default 0.5): share of the mouse the camera keeps while guarding. Half Sword's own rule (1-11 %,
    ///    faster at the disc edge) was "trop fixe, pas assez dynamique"; cam_guard = -1 brings it back;
    ///  - cam_down (default 0.5): when the camera already looks below the horizon, a downward move keeps this share
    ///    ("quand la caméra est en bas, moins d'impact vers le bas"), guarding or not;
    ///  - cam_fix = 0: the game's own camera, nothing scaled (A/B).
    /// </summary>
    [HarmonyPatch(typeof(MissionScreen), "HandleUserInput")]
    internal static class CameraPatch
    {
        private const float Deg = 57.29578f;

        private static readonly System.Action<MissionScreen, float> SetElevation =
            AccessTools.MethodDelegate<System.Action<MissionScreen, float>>(
                AccessTools.PropertySetter(typeof(MissionScreen), nameof(MissionScreen.CameraElevation)));

        // Oracle for the lab CAM line: radians HandleUserInput turned, and radians kept, summed since the last line
        // (the Down pair: downward moves that started below the horizon, the ones cam_down scales).
        private static float _rawBearing, _keptBearing, _rawElevation, _keptElevation, _rawDown, _keptDown, _bearing, _elevation;

        private static void Prefix(MissionScreen __instance, out TaleWorlds.Library.Vec2 __state)
        {
            __state = new TaleWorlds.Library.Vec2(__instance.CameraBearing, __instance.CameraElevation);
        }

        private static void Postfix(MissionScreen __instance, TaleWorlds.Library.Vec2 __state)
        {
            if (HsControl.Agent == null)
            {
                return;
            }
            float turnedBearing = TaleWorlds.Library.MBMath.WrapAngle(__instance.CameraBearing - __state.x);
            float turnedElevation = __instance.CameraElevation - __state.y;
            bool fix = LiveTuning.Get(null, "cam_fix", 1f) >= 0.5f;
            float kx = 1f, ky = 1f;
            if (fix && HsControl.Guarding)
            {
                float guard = LiveTuning.Get(null, "cam_guard", 0.5f);
                kx = guard >= 0f ? guard : HsControl.CameraScaleX;
                ky = guard >= 0f ? guard : HsControl.CameraScaleY;
            }
            bool down = turnedElevation < 0f && __state.y < 0f; // looking below the horizon and moving further down
            if (fix && down)
            {
                ky *= LiveTuning.Get(null, "cam_down", 0.5f);
            }
            _rawBearing += System.Math.Abs(turnedBearing);
            _rawElevation += System.Math.Abs(turnedElevation);
            _keptBearing += System.Math.Abs(turnedBearing * kx);
            _keptElevation += System.Math.Abs(turnedElevation * ky);
            if (down)
            {
                _rawDown -= turnedElevation;
                _keptDown -= turnedElevation * ky;
            }
            if (kx != 1f || ky != 1f)
            {
                // Both ends are inside the game's elevation clamp, so the scaled angle is too.
                __instance.CameraBearing = __state.x + turnedBearing * kx;
                SetElevation(__instance, __state.y + turnedElevation * ky);
            }
            _bearing = __instance.CameraBearing;
            _elevation = __instance.CameraElevation;
        }

        /// <summary>Lab CAM line: guard, fix, scales, camera angles and the turned / kept sums (degrees), look pitch.</summary>
        public static string Telemetry(Agent main)
        {
            float look = (float)System.Math.Asin(System.Math.Max(-1f, System.Math.Min(1f, main.LookDirection.z))) * Deg;
            string s = "guard=" + (HsControl.Guarding ? 1 : 0) + " fix=" + (LiveTuning.Get(null, "cam_fix", 1f) >= 0.5f ? 1 : 0) +
                       " scale=" + Lab.ProbeLog.F(HsControl.CameraScaleX) + "/" + Lab.ProbeLog.F(HsControl.CameraScaleY) +
                       " bearing=" + Lab.ProbeLog.F(_bearing * Deg) + " elev=" + Lab.ProbeLog.F(_elevation * Deg) +
                       " rawB=" + Lab.ProbeLog.F(_rawBearing * Deg) + " keptB=" + Lab.ProbeLog.F(_keptBearing * Deg) +
                       " rawE=" + Lab.ProbeLog.F(_rawElevation * Deg) + " keptE=" + Lab.ProbeLog.F(_keptElevation * Deg) +
                       " look=" + Lab.ProbeLog.F(look) +
                       " rawDown=" + Lab.ProbeLog.F(_rawDown * Deg) + " keptDown=" + Lab.ProbeLog.F(_keptDown * Deg);
            _rawBearing = _keptBearing = _rawElevation = _keptElevation = _rawDown = _keptDown = 0f;
            return s;
        }
    }

    /// <summary>
    /// Lab only: "lab_mouse = x y" adds that many mouse units to every InputContext.GetMouseMoveX / Y read, so a script can
    /// turn the camera and drive the arm without the player's mouse (the oracle for CameraPatch). Absent or 0 = no effect;
    /// never leave it set when the player plays.
    /// </summary>
    [HarmonyPatch(typeof(TaleWorlds.InputSystem.InputContext), "GetMouseMoveX")]
    internal static class LabMouseXPatch
    {
        private static void Postfix(ref float __result)
        {
            if (SubModule.LabMode)
            {
                __result += LiveTuning.GetVec(null, "lab_mouse").x;
            }
        }
    }

    [HarmonyPatch(typeof(TaleWorlds.InputSystem.InputContext), "GetMouseMoveY")]
    internal static class LabMouseYPatch
    {
        private static void Postfix(ref float __result)
        {
            if (SubModule.LabMode)
            {
                __result += LiveTuning.GetVec(null, "lab_mouse").y;
            }
        }
    }
}
