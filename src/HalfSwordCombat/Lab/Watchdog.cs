using System;
using System.Diagnostics;
using System.Threading;

namespace HalfSwordCombat.Lab
{
    /// <summary>
    /// Lab-only hang detector. The mission tick stamps a heartbeat; a background thread reports when it stops,
    /// whether the main thread was inside our code at that moment, and the main thread's managed stack.
    /// An empty or engine-only managed stack means the main thread is stuck in native engine code.
    /// </summary>
    internal static class Watchdog
    {
        private static Thread _main;
        private static Thread _thread;
        private static long _lastBeat;
        private static volatile string _where = "";
        private static volatile bool _inside;

        public static void Start()
        {
            if (_thread != null)
            {
                return;
            }
            _main = Thread.CurrentThread;
            _thread = new Thread(Loop) { IsBackground = true, Name = "HSC lab watchdog" };
            _thread.Start();
        }

        public static void Beat() => Interlocked.Exchange(ref _lastBeat, DateTime.UtcNow.Ticks);

        public static void Enter(string where)
        {
            _where = where;
            _inside = true;
        }

        public static void Exit()
        {
            _inside = false;
            Beat();
        }

        private static void Loop()
        {
            bool reported = false;
            double longest = 0;
            // Suspending the main thread to read its stack can itself deadlock the engine: opt-in only (lab runs 11-14).
            bool sampleStack = Environment.GetEnvironmentVariable("HSC_WATCHDOG_STACK") == "1";
            while (true)
            {
                Thread.Sleep(250);
                long last = Interlocked.Read(ref _lastBeat);
                if (last == 0)
                {
                    continue;
                }
                double silent = (DateTime.UtcNow.Ticks - last) / 1e7;
                if (silent < 1.0)
                {
                    if (reported)
                    {
                        ProbeLog.Write("WATCHDOG: mission ticks resumed after a " + longest.ToString("0.0") + " s stall");
                    }
                    reported = false;
                    longest = 0;
                    continue;
                }
                longest = Math.Max(longest, silent);
                if (silent > 2.5 && !reported)
                {
                    reported = true;
                    ProbeLog.Write("WATCHDOG: no mission tick for " + silent.ToString("0.0") + " s, insideOurCode=" + _inside +
                                   " where=" + _where + " mainThreadState=" + _main.ThreadState);
                    if (sampleStack)
                    {
                        ProbeLog.Write("WATCHDOG main thread managed stack:\n" + MainStack());
                    }
                }
            }
        }

        private static string MainStack()
        {
#pragma warning disable 618 // Thread.Suspend/Resume: obsolete, but the only way to sample another thread on .NET Framework
            try
            {
                // StackTrace(Thread, bool) is not in netstandard2.0 but exists in the game's .NET Framework 4.8 runtime.
                var ctor = typeof(StackTrace).GetConstructor(new[] { typeof(Thread), typeof(bool) });
                if (ctor == null)
                {
                    return "<StackTrace(Thread) not available in this runtime>";
                }
                _main.Suspend();
                try
                {
                    return ctor.Invoke(new object[] { _main, false }).ToString();
                }
                finally
                {
                    _main.Resume();
                }
            }
            catch (Exception e)
            {
                return "<stack unavailable: " + e.GetType().Name + ": " + e.Message + ">";
            }
#pragma warning restore 618
        }
    }
}
