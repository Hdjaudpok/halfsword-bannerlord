using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace HalfSwordCombat.Lab
{
    /// <summary>Append-only, flushed-per-line log: the lab's evidence file. HSC_LOG overrides the path.</summary>
    internal static class ProbeLog
    {
        private static readonly object Gate = new object();
        private static string _path;

        internal static string LogPath
        {
            get
            {
                if (_path != null)
                {
                    return _path;
                }
                string custom = Environment.GetEnvironmentVariable("HSC_LOG");
                if (string.IsNullOrEmpty(custom))
                {
                    string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    custom = Path.Combine(docs, "mods", "halfsword-bannerlord", "logs", "lab.log");
                }
                Directory.CreateDirectory(Path.GetDirectoryName(custom));
                _path = custom;
                return _path;
            }
        }

        public static void Write(string line)
        {
            try
            {
                lock (Gate)
                {
                    string stamp = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
                    File.AppendAllText(LogPath, stamp + " " + line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
                // Logging must never crash the game.
            }
        }

        public static string F(float v) => v.ToString("0.000", CultureInfo.InvariantCulture);
    }
}
