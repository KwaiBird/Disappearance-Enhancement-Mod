using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Disappearance.PerformanceV2
{
    // Owns the v2 file; legacy Performance settings are read only.
    internal sealed class PerformanceSettings
    {
        private readonly string path;
        private readonly Action<string> log;
        private readonly Dictionary<string, string> values;
        private readonly Dictionary<string, string> source;
        internal bool VineShadowsOff => Bool("Vines.DisableShadowCasting", false);
        internal bool VineInstancingOn => Bool("Vines.EnableInstancing", true);
        internal bool PointShadowsOff => Bool("Lights.DisablePointLightShadows", true);
        internal bool SunShadowsOff => Bool("Lights.DisableDirectionalShadows", false);
        internal bool PostFxOff => Bool("Rendering.DisablePostProcessing", false);
        internal bool VSyncOff => Bool("Rendering.DisableVSync", false);
        internal bool ShowOverlay => Bool("Diagnostics.ShowOverlay", false);
        internal bool TraceRenderToggles => Bool("Diagnostics.TraceRenderToggles", false);
        internal float ReportSeconds => Number("Diagnostics.ReportSeconds", 10f, 2f, float.MaxValue);

        internal PerformanceSettings(string path, Action<string> log)
        {
            this.path = path; this.log = log;
            bool firstRun = !File.Exists(path);
            values = Parse(firstRun ? "" : File.ReadAllText(path));
            string oldPath = Path.Combine(Path.GetDirectoryName(path), "local.disappearance.performance.cfg");
            source = !firstRun && File.Exists(oldPath) ? Parse(File.ReadAllText(oldPath)) : new Dictionary<string, string>();
            if (firstRun && File.Exists(oldPath)) log("Performance P6 first run: using new defaults; legacy Config left unchanged.");
            if (!firstRun && File.Exists(oldPath))
            {
                using (var hash = SHA256.Create())
                    log("Performance legacy Config SHA-256=" + BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(oldPath))).Replace("-", "").ToLowerInvariant());
            }
        }

        internal void Migrate()
        {
            BoolMigration("S12", "Vines.DisableShadowCasting", false);
            BoolMigration("S13", "Vines.EnableInstancing", true);
            BoolMigration("S14", "Lights.DisablePointLightShadows", true);
            BoolMigration("S15", "Lights.DisableDirectionalShadows", false);
            BoolMigration("S16", "Rendering.DisablePostProcessing", false);
            BoolMigration("S17", "Rendering.DisableVSync", false);
            BoolMigration("S18", "Diagnostics.ShowOverlay", false);
            NumberMigration("S19", "Diagnostics.ReportSeconds", 10f, 2f, float.MaxValue);
            Save();
        }

        private void BoolMigration(string id, string key, bool fallback)
        {
            string origin = values.ContainsKey(key) ? "destination" :
                values.ContainsKey("Migration." + id) ? "receipt-default" : source.ContainsKey(key) ? "legacy" : "default";
            string raw = origin == "destination" ? values[key] : origin == "legacy" ? source[key] : fallback.ToString();
            values[key] = (bool.TryParse(raw, out bool parsed) ? parsed : fallback).ToString().ToLowerInvariant();
            if (!values.ContainsKey("Migration." + id)) values["Migration." + id] = "v1:" + origin;
            log(id + " " + origin + " -> " + values[key]);
        }

        private void NumberMigration(string id, string key, float fallback, float min, float max)
        {
            string origin = values.ContainsKey(key) ? "destination" :
                values.ContainsKey("Migration." + id) ? "receipt-default" : source.ContainsKey(key) ? "legacy" : "default";
            string raw = origin == "destination" ? values[key] : origin == "legacy" ? source[key] : fallback.ToString(CultureInfo.InvariantCulture);
            float parsed = TryFinite(raw, out float number) ? Math.Max(min, Math.Min(max, number)) : fallback;
            values[key] = parsed.ToString("R", CultureInfo.InvariantCulture);
            if (!values.ContainsKey("Migration." + id)) values["Migration." + id] = "v1:" + origin;
            log(id + " " + origin + " -> " + values[key]);
        }

        internal void SetOverlay(bool visible)
        {
            string before = values["Diagnostics.ShowOverlay"];
            values["Diagnostics.ShowOverlay"] = visible.ToString().ToLowerInvariant();
            try { Save(); } catch { values["Diagnostics.ShowOverlay"] = before; throw; }
        }

        private bool Bool(string key, bool fallback) => values.TryGetValue(key, out string raw) && bool.TryParse(raw, out bool value) ? value : fallback;
        private float Number(string key, float fallback, float min, float max) => values.TryGetValue(key, out string raw) && TryFinite(raw, out float value) ? Math.Max(min, Math.Min(max, value)) : fallback;
        private static bool TryFinite(string raw, out float value) => float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !float.IsNaN(value) && !float.IsInfinity(value);

        private void Save()
        {
            var output = new StringBuilder("# Disappearance Performance v2: render comparisons and diagnostics.\n");
            foreach (var group in values.OrderBy(x => x.Key).GroupBy(x => x.Key.Substring(0, x.Key.IndexOf('.'))))
            {
                output.Append('\n').Append('[').Append(group.Key).Append("]\n");
                foreach (var entry in group) output.Append(entry.Key.Substring(group.Key.Length + 1)).Append(" = ").Append(entry.Value).Append('\n');
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(output.ToString());
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        private static Dictionary<string, string> Parse(string text)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string section = "";
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim().TrimStart('\uFEFF');
                if (line.StartsWith("#") || line.StartsWith(";")) continue;
                if (line.StartsWith("[") && line.EndsWith("]")) { section = line.Substring(1, line.Length - 2); continue; }
                int equal = line.IndexOf('=');
                if (equal > 0 && section.Length > 0) result[section + "." + line.Substring(0, equal).Trim()] = line.Substring(equal + 1).Trim();
            }
            return result;
        }
    }
}
