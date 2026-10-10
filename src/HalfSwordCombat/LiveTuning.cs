using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using HalfSwordCombat.Lab;
using TaleWorlds.Library;

namespace HalfSwordCombat
{
    /// <summary>
    /// Values that can be changed while the game runs (user's request 2026-10-03: small adjustments without restarting):
    /// Documents\Mount and Blade II Bannerlord\Configs\HalfSwordCombat_tuning.txt, "key = number" or "key = x y z" per
    /// line, '#' comments. Re-read whenever the file changes (checked twice a second). A key can be specialised per Half
    /// Sword class ("twohand.main_item_rot = ..." wins over "main_item_rot = ..."). <see cref="Version"/> changes on reload.
    /// The hsc.get / hsc.set / hsc.unset console commands (HscCommands) read and write the same file.
    /// </summary>
    internal static class LiveTuning
    {
        private static readonly Dictionary<string, float[]> Values = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);
        private static DateTime _stamp;
        private static float _nextCheck;

        public static int Version { get; private set; }

        public static string Path => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                                                            "Mount and Blade II Bannerlord", "Configs", "HalfSwordCombat_tuning.txt");

        /// <summary>Call every tick with a clock in seconds; reloads at most twice a second when the file changed.</summary>
        public static void Poll(float now)
        {
            if (now < _nextCheck)
            {
                return;
            }
            _nextCheck = now + 0.5f;
            Reload(false);
        }

        private static void Reload(bool force)
        {
            try
            {
                if (!File.Exists(Path))
                {
                    if (Values.Count > 0)
                    {
                        Values.Clear();
                        Version++;
                    }
                    return;
                }
                DateTime stamp = File.GetLastWriteTimeUtc(Path);
                if (stamp == _stamp && !force)
                {
                    return;
                }
                _stamp = stamp;
                Values.Clear();
                foreach (string raw in File.ReadAllLines(Path))
                {
                    string line = raw;
                    int hash = line.IndexOf('#');
                    if (hash >= 0)
                    {
                        line = line.Substring(0, hash);
                    }
                    int eq = line.IndexOf('=');
                    if (eq <= 0)
                    {
                        continue;
                    }
                    string[] parts = line.Substring(eq + 1).Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
                    var numbers = new List<float>();
                    foreach (string p in parts)
                    {
                        if (float.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
                        {
                            numbers.Add(f);
                        }
                    }
                    if (numbers.Count > 0)
                    {
                        Values[line.Substring(0, eq).Trim()] = numbers.ToArray();
                    }
                }
                Version++;
                ProbeLog.Write("LIVE tuning reloaded (" + Values.Count + " values) from " + Path);
            }
            catch (Exception e)
            {
                ProbeLog.Write("LIVE tuning read error: " + e.Message);
            }
        }

        public static float Get(string cls, string key, float fallback)
        {
            float[] v = Find(cls, key);
            return v != null ? v[0] : fallback;
        }

        public static Vec3 GetVec(string cls, string key)
        {
            float[] v = Find(cls, key);
            return v == null ? Vec3.Zero : new Vec3(v[0], v.Length > 1 ? v[1] : 0f, v.Length > 2 ? v[2] : 0f);
        }

        /// <summary>Frame from "&lt;key&gt;_rot" (degrees about local x, then y, then z) and "&lt;key&gt;_pos" (metres).</summary>
        public static MatrixFrame GetFrame(string cls, string key)
        {
            Vec3 rot = GetVec(cls, key + "_rot") * ((float)Math.PI / 180f), pos = GetVec(cls, key + "_pos");
            Mat3 m = Mat3.Identity;
            m.RotateAboutSide(rot.x);
            m.RotateAboutForward(rot.y);
            m.RotateAboutUp(rot.z);
            return new MatrixFrame(m, pos);
        }

        private static float[] Find(string cls, string key)
        {
            if (cls != null && Values.TryGetValue(cls + "." + key, out float[] v))
            {
                return v;
            }
            return Values.TryGetValue(key, out v) ? v : null;
        }

        /// <summary>Console (hsc.get): the current values, all or the keys containing <paramref name="filter"/>.</summary>
        public static string List(string filter)
        {
            Reload(false);
            var keys = new List<string>(Values.Keys);
            keys.Sort(StringComparer.OrdinalIgnoreCase);
            var sb = new System.Text.StringBuilder();
            foreach (string key in keys)
            {
                if (filter == null || key.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    sb.Append(key).Append(" =");
                    foreach (float f in Values[key])
                    {
                        sb.Append(' ').Append(f.ToString(CultureInfo.InvariantCulture));
                    }
                    sb.Append('\n');
                }
            }
            return sb.Length > 0 ? sb.ToString().TrimEnd() : "no tuning values" + (filter == null ? "" : " matching " + filter) + " in " + Path;
        }

        /// <summary>
        /// Console (hsc.set): writes "key = values" into the file (the file stays the one source of truth, so a value set
        /// from the console is visible and editable there) and reloads at once. Replaces the key's line, keeping its
        /// comment, or appends one.
        /// </summary>
        public static string Set(string key, IList<string> values)
        {
            foreach (string v in values)
            {
                if (!float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                {
                    return "not a number: " + v;
                }
            }
            string line = key + " = " + string.Join(" ", values);
            Rewrite(key, line);
            return line + " (tuning v" + Version + ")";
        }

        /// <summary>Console (hsc.unset): removes the key's lines from the file and reloads.</summary>
        public static string Unset(string key) =>
            Rewrite(key, null) ? key + " removed (tuning v" + Version + ")" : key + " was not set";

        private static bool Rewrite(string key, string replacement)
        {
            var lines = File.Exists(Path) ? new List<string>(File.ReadAllLines(Path)) : new List<string>();
            bool found = false;
            // From the end: the last line of a key is the one that counts, earlier duplicates go.
            for (int i = lines.Count - 1; i >= 0; i--)
            {
                if (!string.Equals(KeyOf(lines[i]), key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (replacement != null && !found)
                {
                    int hash = lines[i].IndexOf('#');
                    lines[i] = hash >= 0 ? replacement + "  " + lines[i].Substring(hash) : replacement;
                }
                else
                {
                    lines.RemoveAt(i);
                }
                found = true;
            }
            if (!found && replacement != null)
            {
                lines.Add(replacement);
            }
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
            File.WriteAllLines(Path, lines);
            Reload(true);
            return found;
        }

        private static string KeyOf(string line)
        {
            int hash = line.IndexOf('#');
            if (hash >= 0)
            {
                line = line.Substring(0, hash);
            }
            int eq = line.IndexOf('=');
            return eq > 0 ? line.Substring(0, eq).Trim() : null;
        }
    }
}
