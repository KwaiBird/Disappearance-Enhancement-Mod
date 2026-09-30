using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Disappearance.Lighting
{
    internal sealed class LightingSettings
    {
        private readonly string path;
        private readonly Action<string> log;
        private readonly Dictionary<string, string> values;
        internal float BloomOffset => Number("Rendering.BloomThresholdOffset", 0f, 0f, 10f);
        internal bool EnableLightAtmosphere => Bool("Lights.EnableLightAtmosphere", true);

        internal LightingSettings(string path, Action<string> log)
        {
            this.path = path;
            this.log = log;
            values = Parse(File.Exists(path) ? File.ReadAllText(path) : "");
        }

        internal void Migrate()
        {
            string sourcePath = Path.Combine(Path.GetDirectoryName(path), "local.disappearance.performance.cfg");
            Dictionary<string, string> source = new Dictionary<string, string>();
            if (File.Exists(sourcePath))
            {
                byte[] bytes = File.ReadAllBytes(sourcePath);
                source = Parse(Encoding.UTF8.GetString(bytes));
                using (var hash = SHA256.Create())
                    log("Lighting source Config SHA-256=" + BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant());
            }
            if (!values.ContainsKey("Rendering.BloomThresholdOffset") && !values.ContainsKey("Migration.S11") &&
                !source.ContainsKey("Rendering.BloomThresholdOffset") &&
                source.TryGetValue("Rendering.RaiseBloomThreshold", out string raised) &&
                bool.TryParse(raised, out bool enabled) && enabled)
            {
                source["Rendering.BloomThresholdOffset"] = source.TryGetValue("Rendering.BloomThresholdIncrease", out string increase) ? increase : "1";
            }
            NumberMigration("S11", "Rendering.BloomThresholdOffset", 0f, 0f, 10f, source);
            BoolMigration("S22", "Lights.EnableLightAtmosphere", true, source);
            Save();
        }

        private void BoolMigration(string id, string key, bool fallback, Dictionary<string, string> source)
        {
            string origin = "destination";
            if (!values.TryGetValue(key, out string raw))
            {
                origin = values.ContainsKey("Migration." + id) ? "receipt-default" : source.ContainsKey(key) ? "legacy" : "default";
                raw = origin == "legacy" ? source[key] : fallback.ToString();
            }
            values[key] = (bool.TryParse(raw, out bool value) ? value : fallback).ToString().ToLowerInvariant();
            if (!values.ContainsKey("Migration." + id)) values["Migration." + id] = "v1:" + origin;
            log(id + " " + origin + " -> " + values[key]);
        }

        private void NumberMigration(string id, string key, float fallback, float min, float max, Dictionary<string, string> source)
        {
            string origin = "destination";
            if (!values.TryGetValue(key, out string raw))
            {
                origin = values.ContainsKey("Migration." + id) ? "receipt-default" : source.ContainsKey(key) ? "legacy" : "default";
                raw = origin == "legacy" ? source[key] : fallback.ToString(CultureInfo.InvariantCulture);
            }
            float value = TryFinite(raw, out float parsed) ? Math.Max(min, Math.Min(max, parsed)) : fallback;
            values[key] = value.ToString("R", CultureInfo.InvariantCulture);
            if (!values.ContainsKey("Migration." + id)) values["Migration." + id] = "v1:" + origin;
            log(id + " " + origin + " -> " + values[key]);
        }

        private bool Bool(string key, bool fallback) => values.TryGetValue(key, out string raw) && bool.TryParse(raw, out bool value) ? value : fallback;
        private float Number(string key, float fallback, float min, float max) => values.TryGetValue(key, out string raw) && TryFinite(raw, out float value) ? Math.Max(min, Math.Min(max, value)) : fallback;
        private static bool TryFinite(string raw, out float value) => float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !float.IsNaN(value) && !float.IsInfinity(value);

        internal void Save()
        {
            var text = new StringBuilder("# Disappearance Lighting: environment settings and migration receipts.\n");
            foreach (var group in values.OrderBy(pair => pair.Key).GroupBy(pair => pair.Key.Substring(0, pair.Key.IndexOf('.'))))
            {
                text.Append('\n').Append('[').Append(group.Key).Append("]\n");
                foreach (var pair in group) text.Append(pair.Key.Substring(group.Key.Length + 1)).Append(" = ").Append(pair.Value).Append('\n');
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

        private static Dictionary<string, string> Parse(string text)
        {
            var data = new Dictionary<string, string>(StringComparer.Ordinal);
            string section = "";
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim().TrimStart('\uFEFF');
                if (line.StartsWith("#") || line.StartsWith(";")) continue;
                if (line.StartsWith("[") && line.EndsWith("]")) { section = line.Substring(1, line.Length - 2); continue; }
                int equals = line.IndexOf('=');
                if (equals > 0 && section.Length > 0) data[section + "." + line.Substring(0, equals).Trim()] = line.Substring(equals + 1).Trim();
            }
            return data;
        }
    }
}
