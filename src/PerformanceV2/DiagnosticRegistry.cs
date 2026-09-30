using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Unity.Mono;
using BepInEx.Unity.Mono.Bootstrap;
using Disappearance.UIControls;
using UnityEngine;

namespace Disappearance.PerformanceV2
{
    // Public boundary is primitives and JSON; no plugin-owned objects cross DLLs.
    internal sealed class DiagnosticRegistry
    {
        private sealed class Item
        {
            internal string Owner, Id, Label, Value;
            internal int Order, Sequence = -1;
            internal bool Visible;
            internal float Updated;
        }

        private readonly Dictionary<string, Item> items = new Dictionary<string, Item>(StringComparer.Ordinal);
        private readonly Action<string> log;
        internal readonly string InstanceId = Guid.NewGuid().ToString("N");
        internal int Generation { get; private set; } = 1;
        internal int SceneEpoch { get; private set; }
        internal DiagnosticRegistry(Action<string> log) { this.log = log; }

        private static bool Live(string owner)
        {
            var loader = UnityChainloader.Instance;
            return !string.IsNullOrEmpty(owner) && loader != null &&
                loader.Plugins.TryGetValue(owner, out PluginInfo info) &&
                info.Instance is BaseUnityPlugin plugin && plugin.isActiveAndEnabled;
        }

        private static bool Id(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 80) return false;
            foreach (char c in id)
                if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') &&
                    !(c >= '0' && c <= '9') && c != '_' && c != '-' && c != '.') return false;
            return true;
        }

        private bool Header(Dictionary<string, object> d) =>
            Stage2Json.TryInteger(d, "schema", out int schema) && schema == 1 &&
            Stage2Json.TryString(d, "instanceId", out string instance) && instance == InstanceId &&
            Stage2Json.TryInteger(d, "generation", out int generation) && generation == Generation;

        internal bool Register(string owner, string id, string json)
        {
            string error = null;
            if (!Live(owner) || !Id(id) || !Stage2Json.TryParse(json, out object parsed, out error) ||
                !Stage2Json.TryObject(parsed, out var d) || !Header(d) ||
                !Stage2Json.TryString(d, "label", out string label) || string.IsNullOrWhiteSpace(label) || label.Length > 256 ||
                !Stage2Json.TryInteger(d, "order", out int order))
            { log("P6 diagnostic definition rejected: " + owner + "/" + id + " " + error); return false; }
            string key = owner + "/" + id;
            if (!items.TryGetValue(key, out Item item)) { item = new Item { Owner = owner, Id = id }; items.Add(key, item); }
            item.Label = label; item.Order = order;
            return true;
        }

        internal bool Update(string owner, string id, string json)
        {
            if (!Live(owner) || !items.TryGetValue(owner + "/" + id, out Item item) ||
                !Stage2Json.TryParse(json, out object parsed, out _) || !Stage2Json.TryObject(parsed, out var d) ||
                !Header(d) || !Stage2Json.TryInteger(d, "sceneEpoch", out int epoch) || epoch != SceneEpoch ||
                !Stage2Json.TryInteger(d, "sequence", out int sequence) || sequence < 0 ||
                !Stage2Json.TryString(d, "valueText", out string value) || value.Length > 512 ||
                !Stage2Json.TryBool(d, "visible", out bool visible)) return false;
            if (sequence < item.Sequence || (sequence == item.Sequence &&
                (value != item.Value || visible != item.Visible))) return false;
            item.Sequence = sequence; item.Value = value; item.Visible = visible; item.Updated = Time.unscaledTime;
            return true;
        }

        internal void Unregister(string owner)
        {
            foreach (string key in items.Keys.Where(key => key.StartsWith(owner + "/", StringComparison.Ordinal)).ToArray())
                items.Remove(key);
        }

        internal void ChangedScene()
        {
            SceneEpoch++;
            foreach (Item item in items.Values) { item.Visible = false; item.Value = null; item.Sequence = -1; }
        }

        internal string[] VisibleRows()
        {
            float now = Time.unscaledTime;
            return items.Values.Where(x => x.Visible && now - x.Updated < 1.5f && Live(x.Owner))
                .OrderBy(x => x.Order).ThenBy(x => x.Owner, StringComparer.Ordinal).ThenBy(x => x.Id, StringComparer.Ordinal)
                .Select(x => x.Label + ": " + x.Value).ToArray();
        }
    }
}
