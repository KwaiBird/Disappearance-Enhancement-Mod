using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Disappearance.Shared
{
    // Linked into each owner DLL. Reads legacy files but writes only the owner's destination file.
    internal sealed class LegacyConfigMigration
    {
        internal enum ValueType { Boolean, Integer, Number }

        internal sealed class Source
        {
            internal string File;
            internal string Key;
            internal Source(string file, string key) { File = file; Key = key; }
        }

        internal sealed class Spec
        {
            internal string Id;
            internal string Key;
            internal ValueType Type;
            internal string Default;
            internal double Min;
            internal double Max;
            internal Source[] Sources;
            internal Spec(string id, string key, ValueType type, string fallback,
                double min, double max, params Source[] sources)
            { Id = id; Key = key; Type = type; Default = fallback; Min = min; Max = max; Sources = sources; }
        }

        private readonly string path;
        private readonly Action<string> log;
        private readonly Dictionary<string, Dictionary<string, string>> sources =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        internal LegacyConfigMigration(string path, Action<string> log)
        { this.path = path; this.log = log; }

        internal void Migrate(params Spec[] specs)
        {
            string original = File.Exists(path) ? File.ReadAllText(path) : "";
            var destination = Parse(original);
            var changes = new Dictionary<string, string>(StringComparer.Ordinal);
            var notes = new List<string>();
            foreach (Spec spec in specs)
            {
                string raw = null;
                string origin;
                string value;
                if (destination.TryGetValue(spec.Key, out raw))
                {
                    origin = "destination";
                    value = Normalize(spec, raw) ?? spec.Default;
                    if (value == spec.Default && Normalize(spec, raw) == null) origin += ":invalid-default";
                }
                else if (destination.ContainsKey("Migration." + spec.Id))
                {
                    origin = "receipt-default";
                    value = spec.Default;
                }
                else
                {
                    origin = "default";
                    value = spec.Default;
                    foreach (Source source in spec.Sources)
                    {
                        Dictionary<string, string> entries = ReadSource(source.File);
                        if (!entries.TryGetValue(source.Key, out string candidate)) continue;
                        string normalized = Normalize(spec, candidate);
                        if (normalized == null)
                        {
                            notes.Add(spec.Id + " invalid " + source.File + ":" + source.Key + "=" + candidate);
                            continue;
                        }
                        raw = candidate;
                        value = normalized;
                        origin = source.File + ":" + source.Key;
                        break;
                    }
                }
                if (!destination.TryGetValue(spec.Key, out string oldValue) || oldValue != value)
                    changes[spec.Key] = value;
                string receipt = "Migration." + spec.Id;
                if (!destination.ContainsKey(receipt)) changes[receipt] = "v1:" + origin;
                notes.Add(spec.Id + " origin=" + origin + " raw=" + (raw ?? "<missing>") + " applied=" + value);
            }
            if (changes.Count == 0) return;

            string candidateText = original;
            foreach (KeyValuePair<string, string> pair in changes)
                candidateText = Upsert(candidateText, pair.Key, pair.Value);
            AtomicSave(candidateText);
            log("Config migration committed: " + string.Join("; ", notes));
            try { File.AppendAllLines(path + ".migration.log",
                new[] { DateTime.UtcNow.ToString("o") + " " + string.Join("; ", notes) }); }
            catch (Exception error) { log("Config migration audit write failed: " + error.Message); }
        }

        private Dictionary<string, string> ReadSource(string name)
        {
            if (sources.TryGetValue(name, out Dictionary<string, string> entries)) return entries;
            string sourcePath = Path.Combine(Path.GetDirectoryName(path), name);
            if (!File.Exists(sourcePath)) return sources[name] = new Dictionary<string, string>(StringComparer.Ordinal);
            byte[] bytes = File.ReadAllBytes(sourcePath);
            using (var hash = SHA256.Create())
                log("Config migration source " + name + " SHA-256=" +
                    BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant());
            return sources[name] = Parse(Encoding.UTF8.GetString(bytes));
        }

        private static string Normalize(Spec spec, string raw)
        {
            if (spec.Type == ValueType.Boolean)
                return bool.TryParse(raw, out bool boolean) ? boolean.ToString().ToLowerInvariant() : null;
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ||
                double.IsNaN(number) || double.IsInfinity(number)) return null;
            number = Math.Max(spec.Min, Math.Min(spec.Max, number));
            if (spec.Type == ValueType.Integer)
            {
                if (Math.Truncate(number) != number) return null;
                return ((int)number).ToString(CultureInfo.InvariantCulture);
            }
            return number.ToString("R", CultureInfo.InvariantCulture);
        }

        private static Dictionary<string, string> Parse(string text)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            string section = "";
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim().TrimStart('\uFEFF');
                if (line.StartsWith("#") || line.StartsWith(";")) continue;
                if (line.StartsWith("[") && line.EndsWith("]"))
                { section = line.Substring(1, line.Length - 2); continue; }
                int equals = line.IndexOf('=');
                if (equals > 0 && section.Length > 0)
                    values[section + "." + line.Substring(0, equals).Trim()] = line.Substring(equals + 1).Trim();
            }
            return values;
        }

        private static string Upsert(string text, string path, string value)
        {
            int dot = path.IndexOf('.');
            string section = path.Substring(0, dot), key = path.Substring(dot + 1);
            var lines = new List<string>(text.Replace("\r\n", "\n").Split('\n'));
            int sectionStart = -1, sectionEnd = lines.Count, keyLine = -1;
            string current = "";
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i].Trim();
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    if (sectionStart >= 0 && sectionEnd == lines.Count) sectionEnd = i;
                    current = line.Substring(1, line.Length - 2);
                    if (current == section) { sectionStart = i; sectionEnd = lines.Count; }
                    continue;
                }
                int equals = line.IndexOf('=');
                if (current == section && equals > 0 && line.Substring(0, equals).Trim() == key)
                    keyLine = i;
            }
            if (keyLine >= 0) lines[keyLine] = key + " = " + value;
            else if (sectionStart >= 0) lines.Insert(sectionEnd, key + " = " + value);
            else
            {
                if (lines.Count > 0 && lines[lines.Count - 1].Length != 0) lines.Add("");
                lines.Add("[" + section + "]");
                lines.Add(key + " = " + value);
            }
            return string.Join("\n", lines).TrimEnd('\n') + "\n";
        }

        private void AtomicSave(string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(text);
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
