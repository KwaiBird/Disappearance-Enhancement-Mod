using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Disappearance.Comfort
{
    // This owns only the new Comfort file. No Config.Bind auto-save can pre-empt migration.
    internal sealed class ComfortConfigStore
    {
        internal readonly string Path;
        private readonly string directory;
        private Dictionary<string, string> data;
        private string committedText;
        private readonly Action<string> log;
        internal ComfortConfigStore(string path, Action<string> log)
        {
            Path = path; directory = System.IO.Path.GetDirectoryName(path); this.log = log;
            committedText = File.Exists(path) ? File.ReadAllText(path) : "";
            data = Parse(committedText);
        }
        internal Dictionary<string, string> Snapshot() => new Dictionary<string, string>(data);
        internal void Restore(Dictionary<string, string> snapshot) { data = snapshot; }
        internal bool Contains(string key) => data.ContainsKey(key);
        internal string Get(string key, string fallback) => data.TryGetValue(key, out string v) ? v : fallback;
        internal double Number(string key, double fallback)
        {
            return TryNumber(Get(key, ""), out double n) ? n : fallback;
        }
        internal void Set(string key, double value) { data[key] = SettingSpec.Number(value); }
        internal void Set(string key, string value) { data[key] = value; }
        internal static bool TryNumber(string text, out double value) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && FovMath.Finite(value);

        internal void Migrate(double? vertical = null, double? aspect = null)
        {
            Dictionary<string, string> before = Snapshot();
            var notes = new List<string>();
            try
            {
                foreach (SettingSpec spec in SettingSpec.All)
                {
                    bool fov = spec.Id == "horizontal_fov";
                    if (fov && (!vertical.HasValue || !aspect.HasValue)) { notes.Add("S04 pending camera baseline"); continue; }
                    double fallback = fov ? FovMath.DefaultHorizontal(vertical.Value, aspect.Value) : spec.Default;
                    bool receipt = Contains("Migration." + spec.MigrationId);
                    string raw = Get(spec.Path, null), origin = "existing-destination-key";
                    double value;
                    if (raw != null) { if (!TryNumber(raw, out value)) { value = fallback; origin += ":invalid-default"; } }
                    else if (receipt) { value = fallback; origin = "receipt-default"; }
                    else
                    {
                        string source = fov ? "local.disappearance.performance.cfg" : "local.disappearance.comfortcamera.cfg";
                        raw = Legacy(source, spec.Path, notes);
                        origin = source + ":" + spec.Path;
                        if (!TryNumber(raw, out value))
                        {
                            origin = "default-or-legacy-fallback";
                            if (fov)
                            {
                                string old = Legacy(source, "Camera.VerticalFov", notes);
                                value = TryNumber(old, out double v) && v > 0 && v < 180 ? FovMath.Horizontal(v, aspect.Value) : fallback;
                            }
                            else if (spec.Id == "player_brightness") value = LegacyNumber("local.disappearance.gamesettings.cfg", "GameSettings.Brightness", 5, notes);
                            else
                            {
                                double level = LegacyNumber("local.disappearance.gamesettings.cfg", "GameSettings.Sensitivity", 5, notes);
                                bool mouse = spec.Id == "mouse_sensitivity";
                                value = level * LegacyNumber("local.disappearance.looksensitivity.cfg", mouse ? "Look.MouseSensitivity" : "Look.ControllerSensitivity", 1, notes);
                                if (!FovMath.Finite(value)) value = fallback;
                            }
                        }
                    }
                    double corrected = FovMath.Clamp(value, fov ? fallback : spec.Min, spec.Max);
                    if (value != corrected) Set("Migration.RangeV2_" + spec.MigrationId, "v2:" + SettingSpec.Number(value) + "->" + SettingSpec.Number(corrected));
                    Set(spec.Path, corrected);
                    if (!receipt) Set("Migration." + spec.MigrationId, "v1:" + origin);
                    notes.Add(spec.MigrationId + " origin=" + origin + " raw=" + raw + " parsed=" + SettingSpec.Number(value) + " applied=" + SettingSpec.Number(corrected));
                }
                MigrateChoice("S05", "Lights.MatchFlashlightToFov", "true", new[] { "true", "false" }, notes);
                MigrateChoice("S06", "Lights.PlayerIllumination", "Spot", new[] { "Spot", "Ambient", "Off" }, notes);
                MigrateChoice("S08", "Rendering.DisableVignette", "false", new[] { "true", "false" }, notes);
                MigrateRange("S09", "Rendering.VignetteIntensity", .35, 0, 1, notes);
                MigrateRange("S10", "Rendering.VignetteSmoothness", .65, .01, 1, notes);
                string ambientKey = "Lights.AmbientBrightness";
                if (!Contains(ambientKey)) Set(ambientKey, Contains("Migration.S07") ? "0.18" : Legacy("local.disappearance.performance.cfg", ambientKey, notes) ?? "0.18");
                double original = Number(ambientKey, .18);
                Set(ambientKey, FovMath.Clamp(original, 0, 1));
                if (!Contains("Migration.S07")) Set("Migration.S07", "v1:resolved-ambient");
                notes.Add("S07 parsed=" + SettingSpec.Number(original) + " applied=" + Get(ambientKey, ""));
                Save();
                log("Comfort migration committed: " + string.Join("; ", notes));
                // Audit logging cannot turn a successfully committed config into an apparent failed write.
                try { File.AppendAllLines(Path + ".migration.log", new[] { DateTime.UtcNow.ToString("o") + " " + string.Join("; ", notes) }); }
                catch (Exception e) { log("Migration audit write failed: " + e.Message); }
            }
            catch { Restore(before); throw; }
        }
        private void MigrateChoice(string id, string key, string fallback, string[] choices, List<string> notes)
        {
            string raw = Get(key, null);
            string origin = "existing-destination-key";
            if (raw == null) { origin = Contains("Migration." + id) ? "receipt-default" : "legacy-or-default"; raw = origin == "receipt-default" ? fallback : Legacy("local.disappearance.performance.cfg", key, notes) ?? fallback; }
            string value = Array.Find(choices, x => string.Equals(x, raw, StringComparison.OrdinalIgnoreCase)) ?? fallback;
            Set(key, value);
            if (!Contains("Migration." + id)) Set("Migration." + id, "v1:" + origin);
            notes.Add(id + " origin=" + origin + " raw=" + raw + " applied=" + value);
        }
        private void MigrateRange(string id, string key, double fallback, double min, double max, List<string> notes)
        {
            string raw = Get(key, null), origin = "existing-destination-key";
            if (raw == null)
            {
                origin = Contains("Migration." + id) ? "receipt-default" : "legacy-or-default";
                raw = origin == "receipt-default" ? null : Legacy("local.disappearance.performance.cfg", key, notes);
            }
            double value = TryNumber(raw, out double parsed) ? FovMath.Clamp(parsed, min, max) : fallback;
            Set(key, value);
            if (!Contains("Migration." + id)) Set("Migration." + id, "v1:" + origin);
            notes.Add(id + " origin=" + origin + " raw=" + raw + " applied=" + Get(key, ""));
        }
        private string Legacy(string filename, string key, List<string> notes)
        {
            string path = System.IO.Path.Combine(directory, filename);
            if (!File.Exists(path)) return null;
            byte[] bytes = File.ReadAllBytes(path);
            using (var hash = SHA256.Create()) notes.Add(filename + " sha256=" + BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant());
            return Parse(Encoding.UTF8.GetString(bytes)).TryGetValue(key, out string value) ? value : null;
        }
        private double LegacyNumber(string file, string key, double fallback, List<string> notes) => TryNumber(Legacy(file, key, notes), out double value) ? value : fallback;
        internal bool TryExternal(out Dictionary<string, string> candidate)
        {
            string current = File.Exists(Path) ? File.ReadAllText(Path) : "";
            candidate = current == committedText ? null : Parse(current);
            return candidate != null;
        }
        internal void Save()
        {
            var text = new StringBuilder("# Disappearance Comfort: settings and migration receipts are committed together.\n");
            foreach (var group in data.OrderBy(x => x.Key).GroupBy(x => x.Key.Substring(0, x.Key.IndexOf('.'))))
            {
                text.Append('\n').Append('[').Append(group.Key).Append("]\n");
                foreach (var pair in group) text.Append(pair.Key.Substring(group.Key.Length + 1)).Append(" = ").Append(pair.Value).Append('\n');
            }
            string value = text.ToString();
            AtomicWrite(Path, value);
            committedText = value;
        }
        internal static void AtomicWrite(string path, string text)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(text);
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        internal static Dictionary<string, string> Parse(string text)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            string section = "";
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim().TrimStart('\uFEFF');
                if (line.StartsWith("#") || line.StartsWith(";")) continue;
                if (line.StartsWith("[") && line.EndsWith("]")) { section = line.Substring(1, line.Length - 2); continue; }
                int equals = line.IndexOf('=');
                if (equals > 0 && section.Length > 0) values[section + "." + line.Substring(0, equals).Trim()] = line.Substring(equals + 1).Trim();
            }
            return values;
        }
    }
}
