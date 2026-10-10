using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace HalfSwordCombat.Lab
{
    /// <summary>
    /// Lab only: logs every managed exception where it is thrown, with the throwing thread's full stack, before any
    /// handler runs. On this PC a crash can freeze the game instead of showing BLSE's report: the report window
    /// deadlocks with ReShade's RenoDX DLSS add-on (MODLOG 2026-10-04), so the report never reaches disk and this log
    /// is the only record of the error. The same site is logged at most 3 times, 400 lines in all.
    /// </summary>
    internal static class LabExceptions
    {
        private const int PerSite = 3;
        private const int Total = 400;

        [ThreadStatic] private static bool _inside;
        private static readonly Dictionary<string, int> Seen = new Dictionary<string, int>();
        private static int _logged;

        public static void Install()
        {
            AppDomain.CurrentDomain.FirstChanceException += OnFirstChance;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandled;
            ProbeLog.Write("LAB exception log on (first chance, unhandled)");
        }

        private static void OnFirstChance(object sender, FirstChanceExceptionEventArgs e)
        {
            if (_inside || _logged >= Total)
            {
                return;
            }
            _inside = true;
            try
            {
                Exception ex = e.Exception;
                // The handler runs on the throwing thread at the throw point: this stack is the thrower's callers.
                string stack = new System.Diagnostics.StackTrace(1, false).ToString();
                string site = ex.GetType().FullName + "|" + FirstLine(stack);
                lock (Seen)
                {
                    Seen.TryGetValue(site, out int n);
                    if (n >= PerSite)
                    {
                        return;
                    }
                    Seen[site] = n + 1;
                    _logged++;
                }
                Thread t = Thread.CurrentThread;
                ProbeLog.Write("FIRSTCHANCE thread=" + t.ManagedThreadId + (t.Name != null ? " '" + t.Name + "'" : "") +
                               (t.IsThreadPoolThread ? " pool" : "") + " " + ex.GetType().FullName + ": " + ex.Message +
                               "\n" + stack.TrimEnd());
            }
            catch
            {
                // Never let the logger throw from inside the runtime's notification.
            }
            finally
            {
                _inside = false;
            }
        }

        private static void OnUnhandled(object sender, UnhandledExceptionEventArgs e)
        {
            ProbeLog.Write("UNHANDLED terminating=" + e.IsTerminating + " thread=" + Thread.CurrentThread.ManagedThreadId + "\n" + e.ExceptionObject);
        }

        private static string FirstLine(string stack)
        {
            int nl = stack.IndexOf('\n');
            return (nl >= 0 ? stack.Substring(0, nl) : stack).Trim();
        }
    }
}
