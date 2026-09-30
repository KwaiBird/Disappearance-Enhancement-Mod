namespace Disappearance.Comfort
{
    internal static class ComfortWire
    {
        internal static readonly string[] ActionIds = { "fov.decrease", "fov.increase", "fov.reset", "light.mode", "light.fov_fit", "vignette.toggle" };
        private static readonly string[] Labels = { "視野角を狭める", "視野角を広げる", "視野角を戻す", "照明モード", "照射角の視野角追従", "周辺減光の切替" };
        private static readonly string[] Keys = { "f3", "f4", "f5", "home", "end", "delete" };
        internal static string Setting(SettingSpec spec, string instance, int generation) =>
            "{\"schema\":2,\"instanceId\":\"" + instance + "\",\"generation\":" + generation + ",\"kind\":\"number\",\"label\":\"" + spec.Label + "\",\"slot\":\"" + spec.Id + "\",\"min\":" + SettingSpec.Number(spec.Min) + ",\"max\":" + SettingSpec.Number(spec.Max) + ",\"step\":" + SettingSpec.Number(spec.Step) + ",\"decimals\":" + spec.Decimals + ",\"trimTrailingZeros\":" + (spec.Trim ? "true" : "false") + ",\"order\":" + spec.Order + "}";
        internal static string Action(int i, string instance, int generation) =>
            "{\"schema\":2,\"instanceId\":\"" + instance + "\",\"generation\":" + generation + ",\"label\":\"" + Labels[i] + "\",\"order\":" + (100 + i) + ",\"keyboardMouse\":[{\"mode\":\"single\",\"controls\":[\"<Keyboard>/" + Keys[i] + "\"]}],\"gamepad\":[],\"holdSeconds\":0,\"contexts\":[\"village\"],\"tableContexts\":[],\"presentation\":\"overlay\"}";
        // Overlay commands are excluded from normal hints and the operation table.
        internal static string State(string instance, int generation, int epoch, int sequence, bool available) =>
            "{\"schema\":2,\"instanceId\":\"" + instance + "\",\"generation\":" + generation + ",\"sceneEpoch\":" + epoch + ",\"sequence\":" + sequence + ",\"contextId\":\"village\",\"visible\":false,\"alpha\":1,\"enabled\":" + (available ? "true" : "false") + "}";
    }
}
