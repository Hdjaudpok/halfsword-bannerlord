using System.Diagnostics;
using System.Text;

namespace HalfSwordCombat.Lab
{
    /// <summary>
    /// Third-party modules injected in the game process, logged at battle start. Lab runs 11-16 froze on a loader-lock
    /// deadlock between DiscordHook64.dll (Discord overlay) and renodx-dlss.addon64 (ReShade add-on).
    /// </summary>
    internal static class LabEnv
    {
        private static readonly string[] Watched = { "DiscordHook64.dll", "renodx-dlss.addon64", "dxgi.dll", "GameOverlayRenderer64.dll" };

        public static string Describe()
        {
            var found = new bool[Watched.Length];
            try
            {
                foreach (ProcessModule m in Process.GetCurrentProcess().Modules)
                {
                    for (int i = 0; i < Watched.Length; i++)
                    {
                        if (string.Equals(m.ModuleName, Watched[i], System.StringComparison.OrdinalIgnoreCase))
                        {
                            found[i] = true;
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                return "modules unavailable: " + e.Message;
            }
            var sb = new StringBuilder("injected modules:");
            for (int i = 0; i < Watched.Length; i++)
            {
                sb.Append(' ').Append(Watched[i]).Append('=').Append(found[i] ? "yes" : "no");
            }
            if (found[0] && found[1])
            {
                sb.Append(" | WARNING Discord overlay + RenoDX DLSS can deadlock (lab runs 11-16)");
            }
            return sb.ToString();
        }
    }
}
