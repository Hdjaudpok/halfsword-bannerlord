using System;
using System.Globalization;
using System.IO;
using TaleWorlds.InputSystem;

namespace HalfSwordCombat.Link
{
    /// <summary>
    /// Passthrough phase 3, input (docs/PASSTHROUGH.md: Bannerlord has the focus; the mouse and combat keys go to Half
    /// Sword). Live tuning link_input = 1: every mission tick, the player's real mouse moves and combat buttons are
    /// written to %USERPROFILE%\halfsword-decomp\hslink_input.txt as
    ///   seq sumMouseX sumMouseY guardRight guardLeft alt END
    /// The mouse is Bannerlord's raw move summed since the start (y grows downward), so a reader that misses a line loses
    /// nothing. Buttons: the attack game key (Guard Right, left mouse by default), the defend game key (Guard Left, right
    /// mouse) and Left Alt (Thrust), as HsPlayerBehavior already maps them to Half Sword's bindings. HSLink (lab switch
    /// "input") replays them through Willie_BP's own input events. Read from the real input even in the lab profile,
    /// where HsPlayerBehavior's own controls are scripted (HSC_FAKE_INPUT).
    /// </summary>
    internal static class HsInputLink
    {
        public static readonly string StatePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "halfsword-decomp", "hslink_input.txt");

        private static long _seq;
        private static double _sumX, _sumY;

        // Double-click thrust (design 2026-10-03, the user's spear request 2026-10-05): Half Sword binds Thrust to Left Alt
        // only, a held state (Willie_BP: Alt Thrusting while R_Guarding raises Alt Thrusting Alpha). A second press of the
        // attack key within thrust_dclick seconds (live tuning, default 0.3) holds that same Thrust until the key is
        // released, so Half Sword thrusts exactly as with Left Alt.
        private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
        private static bool _rightWas, _clickThrust;
        private static double _rightPressAt = double.NegativeInfinity;

        public static bool Enabled => LiveTuning.Get(null, "link_input", 0f) >= 0.5f;

        /// <summary>Willie's weapons, for HSLink: %USERPROFILE%\halfsword-decomp\hslink_equip.txt, "right left twoHanded END".</summary>
        public static readonly string EquipPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "halfsword-decomp", "hslink_equip.txt");

        private static string _equip;
        private static double _equipAt;

        /// <summary>
        /// While the arms come from Half Sword (link_pose = 1), keeps Willie armed like the player (HsWeaponMap): the line is
        /// rewritten when the loadout changes and once a second (so a Half Sword restarted later still gets it).
        /// </summary>
        public static void TickEquip(TaleWorlds.MountAndBlade.Agent agent, double now)
        {
            if (agent == null || LiveTuning.Get(null, "link_pose", 0f) < 0.5f)
            {
                return;
            }
            string line = HsWeaponMap.For(agent).Line;
            if (line == _equip && now - _equipAt < 1.0)
            {
                return;
            }
            _equip = line;
            _equipAt = now;
            try
            {
                File.WriteAllText(EquipPath, line + " END\n");
            }
            catch (IOException)
            {
                _equip = null; // HSLink reading: retry next tick
            }
        }

        public static void Tick(IInputContext input)
        {
            if (input == null || !Enabled)
            {
                return;
            }
            _sumX += input.GetMouseMoveX();
            _sumY += input.GetMouseMoveY();
            bool right = input.IsGameKeyDown(9), left = input.IsGameKeyDown(10), alt = input.IsKeyDown(InputKey.LeftAlt);
            double now = Clock.Elapsed.TotalSeconds;
            if (right && !_rightWas)
            {
                _clickThrust = now - _rightPressAt <= LiveTuning.Get(null, "thrust_dclick", 0.3f);
                _rightPressAt = now;
            }
            if (!right)
            {
                _clickThrust = false;
            }
            _rightWas = right;
            alt |= _clickThrust;
            _seq++;
            CultureInfo c = CultureInfo.InvariantCulture;
            string line = _seq.ToString(c) + " " + _sumX.ToString("0.###", c) + " " + _sumY.ToString("0.###", c) + " " +
                          (right ? "1" : "0") + " " + (left ? "1" : "0") + " " + (alt ? "1" : "0") + " END\n";
            try
            {
                // Rewritten in place: HSLink reads the file while it is written and skips a line without END.
                using (var stream = new FileStream(StatePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                using (var writer = new StreamWriter(stream))
                {
                    writer.Write(line);
                }
            }
            catch (IOException)
            {
                // Half Sword reading at that instant: the next tick writes the same sums again.
            }
        }
    }
}
