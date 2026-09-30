using System;
using System.Collections.Generic;

namespace Disappearance.UIControls
{
    internal sealed class SettingDescriptor
    {
        internal string Id, Label;
        internal double Min, Max, Step;
        internal int Order, Decimals;
        internal bool Trim;
    }

    internal sealed class SettingsRegistry
    {
        internal const string Owner = "local.disappearance.comfort";
        internal readonly Dictionary<string, SettingDescriptor> Definitions = new Dictionary<string, SettingDescriptor>();
        private readonly Func<string, bool> alive;
        internal readonly string InstanceId;
        internal int Generation { get; private set; } = 1;
        internal SettingsRegistry(string instanceId, Func<string, bool> alive) { InstanceId = instanceId; this.alive = alive; }
        private static string Result(string status) => "{\"schema\":2,\"status\":\"" + status + "\"}";
        internal string Register(string owner, string id, string json)
        {
            if (!Stage2Json.TryParse(json, out object raw, out _) || !Stage2Json.TryObject(raw, out var d)) return Result("invalid_descriptor");
            if (!Stage2Json.TryInteger(d, "schema", out int schema) || schema != 2) return Result("unsupported_version");
            if (!Stage2Json.TryString(d, "instanceId", out string instance) || instance != InstanceId || !Stage2Json.TryInteger(d, "generation", out int generation) || generation != Generation) return Result("stale_generation");
            if (owner != Owner || !alive(owner) || !Stage2Json.TryString(d, "kind", out string kind) || kind != "number" || !Stage2Json.TryString(d, "slot", out string slot) || slot != id || !Stage2Json.TryString(d, "label", out string label) || string.IsNullOrWhiteSpace(label) || label.Length > 128 ||
                !Stage2Json.TryNumber(d, "min", out double min) || !Stage2Json.TryNumber(d, "max", out double max) || !Stage2Json.TryNumber(d, "step", out double step) || !Stage2Json.TryInteger(d, "decimals", out int decimals) || !Stage2Json.TryBool(d, "trimTrailingZeros", out bool trim) || !Stage2Json.TryInteger(d, "order", out int order) || d.Count != 12) return Result("invalid_descriptor");
            bool valid;
            switch (id)
            {
                case "mouse_sensitivity": valid = min == 1 && max == 10 && step == .25 && decimals == 2 && !trim; break;
                case "controller_sensitivity": valid = min == 1 && max == 10 && step == .25 && decimals == 2 && !trim; break;
                case "horizontal_fov": valid = min == 20 && max == 120 && step == 1 && decimals == 0 && !trim; break;
                case "player_brightness": valid = min == 0 && max == 10 && step == 1 && decimals == 2 && trim; break;
                default: valid = false; break;
            }
            if (!valid) return Result("invalid_descriptor");
            Definitions[id] = new SettingDescriptor { Id=id, Label=label, Min=min, Max=max, Step=step, Decimals=decimals, Trim=trim, Order=order };
            return Result("ok");
        }
        internal void Unregister(string owner) { if (owner == Owner) Definitions.Clear(); }
        internal void Update() { if (!alive(Owner)) Definitions.Clear(); }
        internal void Reset() { Definitions.Clear(); Generation++; }
    }
}
