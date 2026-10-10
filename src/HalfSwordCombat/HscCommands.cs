using System.Collections.Generic;
using HalfSwordCombat.Combat;
using HalfSwordCombat.Lab;
using TaleWorlds.Library;

namespace HalfSwordCombat
{
    /// <summary>
    /// Console commands to watch and adjust the mod while the game runs, from the developer console or an agent through
    /// GABS (core/run_command):
    ///   hsc.status             player control, weapon class, buttons, thrust, aim, guard fades, held items, tuning version
    ///   hsc.get [filter]       live tuning values, all or the keys containing filter
    ///   hsc.set key v [v v]    writes the key into the live tuning file and reloads it at once
    ///   hsc.unset key          removes the key from the live tuning file
    ///   hsc.grip               how each item bone is held: rest frame, the game's animation, the held frame
    ///   hsc.dump               writes the drawn pose (every bone and held item) to the lab log
    ///   hsc.loadout [list|next|prev|n]  equips the player with a test weapon set (Lab/LabLoadouts)
    /// </summary>
    internal static class HscCommands
    {
        [CommandLineFunctionality.CommandLineArgumentFunction("status", "hsc")]
        public static string Status(List<string> args) => HsPlayerBehavior.Current?.Describe() ?? "HalfSwordCombat: no mission";

        [CommandLineFunctionality.CommandLineArgumentFunction("get", "hsc")]
        public static string Get(List<string> args)
        {
            args = Clean(args);
            return LiveTuning.List(args.Count > 0 ? args[0] : null);
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("set", "hsc")]
        public static string Set(List<string> args)
        {
            args = Clean(args);
            return args.Count < 2 ? "usage: hsc.set key value [value value]" : LiveTuning.Set(args[0], args.GetRange(1, args.Count - 1));
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("unset", "hsc")]
        public static string Unset(List<string> args)
        {
            args = Clean(args);
            return args.Count != 1 ? "usage: hsc.unset key" : LiveTuning.Unset(args[0]);
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("grip", "hsc")]
        public static string Grip(List<string> args) => HsPlayerBehavior.Current?.DescribeGrip() ?? "HalfSwordCombat: no mission";

        [CommandLineFunctionality.CommandLineArgumentFunction("dump", "hsc")]
        public static string Dump(List<string> args) => HsPlayerBehavior.Current?.DumpNow() ?? "HalfSwordCombat: no mission";

        [CommandLineFunctionality.CommandLineArgumentFunction("loadout", "hsc")]
        public static string Loadout(List<string> args) => LabLoadouts.Run(Clean(args));

        /// <summary>
        /// The engine collects console commands once at start-up, possibly before this module's assembly was loaded. Same
        /// steps as the engine's own callback (ManagedExtensions.CollectCommandLineFunctions): collect the commands not
        /// registered yet and hand each to the native console, so other modules' commands are not affected.
        /// </summary>
        internal static void Register()
        {
            if (CommandLineFunctionality.HasFunctionForCommand("hsc.status"))
            {
                return;
            }
            List<string> added = CommandLineFunctionality.CollectCommandLineFunctions();
            foreach (string name in added)
            {
                TaleWorlds.Engine.Utilities.AddCommandLineFunction(name);
            }
            ProbeLog.Write("Console commands registered: " + string.Join(", ", added));
        }

        // The console splits on single spaces: drop the empty pieces double spaces leave.
        private static List<string> Clean(List<string> args) => args.FindAll(a => a.Length > 0);
    }
}
