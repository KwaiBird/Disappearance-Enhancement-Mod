using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Disappearance.GameplayQoL
{
    internal sealed class ForestSignSettings
    {
        private readonly string path;
        private readonly Action<string> log;
        private readonly Dictionary<string, string> values;

        internal bool GlowForestSigns => Bool("Signs.GlowForestSigns", false);
        internal float GlowIntensity => Number("Signs.GlowIntensity", 1.5f, 0f, 4f);

        internal ForestSignSettings(string path, Action<string> log)
        {
            this.path = path;
            this.log = log;
            values = Read(path);
        }

        internal void Migrate()
        {
            string directory = Path.GetDirectoryName(path);
            var lighting = ReadSource(Path.Combine(directory, "local.disappearance.lighting.cfg"), "Lighting");
            var performance = ReadSource(Path.Combine(directory, "local.disappearance.performance.cfg"), "Performance");
            MigrateBool("S20", "Signs.GlowForestSigns", false, lighting, performance);
            MigrateNumber("S21", "Signs.GlowIntensity", 1.5f, 0f, 4f, lighting, performance);
            Save();
        }

        private Dictionary<string, string> ReadSource(string sourcePath, string label)
        {
            if (!File.Exists(sourcePath)) return new Dictionary<string, string>(StringComparer.Ordinal);
            byte[] bytes = File.ReadAllBytes(sourcePath);
            using (var hash = SHA256.Create())
                log("Forest signs " + label + " source SHA-256=" +
                    BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant());
            return Parse(Encoding.UTF8.GetString(bytes));
        }

        private string Source(string id, string key, Dictionary<string, string> lighting,
            Dictionary<string, string> performance, out string origin)
        {
            if (values.TryGetValue(key, out string raw)) { origin = "destination"; return raw; }
            if (values.ContainsKey("Migration." + id)) { origin = "receipt-default"; return null; }
            if (lighting.TryGetValue(key, out raw)) { origin = "lighting"; return raw; }
            if (performance.TryGetValue(key, out raw)) { origin = "performance"; return raw; }
            origin = "default";
            return null;
        }

        private void MigrateBool(string id, string key, bool fallback,
            Dictionary<string, string> lighting, Dictionary<string, string> performance)
        {
            string raw = Source(id, key, lighting, performance, out string origin);
            values[key] = (bool.TryParse(raw, out bool value) ? value : fallback).ToString().ToLowerInvariant();
            if (!values.ContainsKey("Migration." + id)) values["Migration." + id] = "v2:" + origin;
            log(id + " " + origin + " -> " + values[key]);
        }

        private void MigrateNumber(string id, string key, float fallback, float min, float max,
            Dictionary<string, string> lighting, Dictionary<string, string> performance)
        {
            string raw = Source(id, key, lighting, performance, out string origin);
            float value = TryFinite(raw, out float parsed) ? Math.Max(min, Math.Min(max, parsed)) : fallback;
            values[key] = value.ToString("R", CultureInfo.InvariantCulture);
            if (!values.ContainsKey("Migration." + id)) values["Migration." + id] = "v2:" + origin;
            log(id + " " + origin + " -> " + values[key]);
        }

        internal void SetGlowForestSigns(bool value) =>
            values["Signs.GlowForestSigns"] = value.ToString().ToLowerInvariant();

        internal void BeginSession()
        {
            SetGlowForestSigns(false);
            Save();
        }

        private bool Bool(string key, bool fallback) =>
            values.TryGetValue(key, out string raw) && bool.TryParse(raw, out bool value) ? value : fallback;

        private float Number(string key, float fallback, float min, float max) =>
            values.TryGetValue(key, out string raw) && TryFinite(raw, out float value)
                ? Math.Max(min, Math.Min(max, value)) : fallback;

        private static bool TryFinite(string raw, out float value) =>
            float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
            !float.IsNaN(value) && !float.IsInfinity(value);

        internal void Save()
        {
            var text = new StringBuilder("# Disappearance GameplayQoL: forest sign settings and migration receipts.\n");
            foreach (var group in values.OrderBy(pair => pair.Key)
                .GroupBy(pair => pair.Key.Substring(0, pair.Key.IndexOf('.'))))
            {
                text.Append('\n').Append('[').Append(group.Key).Append("]\n");
                foreach (var pair in group)
                    text.Append(pair.Key.Substring(group.Key.Length + 1)).Append(" = ").Append(pair.Value).Append('\n');
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(text.ToString());
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static Dictionary<string, string> Read(string file) =>
            Parse(File.Exists(file) ? File.ReadAllText(file) : "");

        private static Dictionary<string, string> Parse(string text)
        {
            var data = new Dictionary<string, string>(StringComparer.Ordinal);
            string section = "";
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim().TrimStart('\uFEFF');
                if (line.StartsWith("#") || line.StartsWith(";")) continue;
                if (line.StartsWith("[") && line.EndsWith("]"))
                { section = line.Substring(1, line.Length - 2); continue; }
                int equals = line.IndexOf('=');
                if (equals > 0 && section.Length > 0)
                    data[section + "." + line.Substring(0, equals).Trim()] = line.Substring(equals + 1).Trim();
            }
            return data;
        }
    }
}
