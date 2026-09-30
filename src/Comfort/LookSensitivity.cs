using System;
using HarmonyLib;
using StarterAssets;

namespace Disappearance.Comfort
{
    internal sealed class LookSensitivity : IDisposable
    {
        private readonly Harmony patch = new Harmony(ComfortPlugin.PluginId + ".looksensitivity");
        internal LookSensitivity()
        {
            try
            {
            patch.Patch(AccessTools.Method(typeof(FirstPersonController), "CameraRotation"),
                prefix: new HarmonyMethod(typeof(LookSensitivity), nameof(Before)),
                postfix: new HarmonyMethod(typeof(LookSensitivity), nameof(After)),
                finalizer: new HarmonyMethod(typeof(LookSensitivity), nameof(FinalizeCall)));
            }
            catch { patch.UnpatchSelf(); throw; }
        }
        private static void Before(FirstPersonController __instance, out RestoreOnce __state)
        {
            __state = null;
            var plugin = ComfortPlugin.Instance;
            if (plugin == null || plugin.Binding == null || !plugin.Binding.SensitivityReady || plugin.Binding.Controller != __instance) return;
            var input = __instance.GetComponent<UnityEngine.InputSystem.PlayerInput>();
            double level = plugin.Store.Number(input != null && input.currentControlScheme == "Gamepad" ? "Look.ControllerLevel" : "Look.MouseLevel", 5);
            float previous = __instance.RotationSpeed;
            __state = new RestoreOnce(() => { if (__instance != null) __instance.RotationSpeed = previous; });
            __instance.RotationSpeed = previous * (float)level / 5;
        }
        private static void After(RestoreOnce __state) => __state?.Restore();
        private static Exception FinalizeCall(Exception __exception, RestoreOnce __state) { __state?.Restore(); return __exception; }
        public void Dispose() => patch.UnpatchSelf();
    }
}
