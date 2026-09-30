using System;
using System.Reflection;
using HarmonyLib;
using StarterAssets;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Disappearance.Comfort
{
    internal sealed class PlayerSettingsBinding : IDisposable
    {
        private readonly Harmony patch = new Harmony(ComfortPlugin.PluginId + ".playersettings");
        private readonly MethodInfo sensitivitySetter = AccessTools.PropertySetter(typeof(PlayerSettings), "Sensitivity");
        private readonly MethodInfo lightSetter = AccessTools.PropertySetter(typeof(PlayerSettings), "LightIntensity");
        private PlayerSettings model;
        private Light light;
        private float modelSensitivity, controllerSensitivity, modelLight, lightIntensity;
        private bool sensitivityCaptured, lightCaptured, internalCall;
        private static PlayerSettings started;
        internal FirstPersonController Controller { get; private set; }
        internal bool SensitivityReady => sensitivityCaptured && model != null && Controller != null;
        internal bool BrightnessReady => lightCaptured && model != null && light != null && model.RawLightIntensity > 0;
        internal PlayerSettingsBinding()
        {
            try
            {
                patch.Patch(AccessTools.Method(typeof(PlayerSettings), "Start"), postfix: new HarmonyMethod(typeof(PlayerSettingsBinding), nameof(Started)));
                patch.Patch(AccessTools.Method(typeof(PlayerSettings), "SetSensitivity"), prefix: new HarmonyMethod(typeof(PlayerSettingsBinding), nameof(BeforeSensitivity)), postfix: new HarmonyMethod(typeof(PlayerSettingsBinding), nameof(AfterSensitivity)), finalizer: new HarmonyMethod(typeof(PlayerSettingsBinding), nameof(FinalizeExternal)));
                patch.Patch(AccessTools.Method(typeof(PlayerSettings), "SetLightIntensity"), prefix: new HarmonyMethod(typeof(PlayerSettingsBinding), nameof(BeforeLight)), postfix: new HarmonyMethod(typeof(PlayerSettingsBinding), nameof(AfterLight)), finalizer: new HarmonyMethod(typeof(PlayerSettingsBinding), nameof(FinalizeExternal)));
                if (sensitivitySetter == null) ComfortPlugin.Log.LogError("Sensitivity model setter absent: sensitivity unavailable.");
                if (lightSetter == null) ComfortPlugin.Log.LogError("LightIntensity model setter absent: brightness unavailable.");
            }
            catch { patch.UnpatchSelf(); throw; }
        }
        private static void Started(PlayerSettings __instance) { started = __instance; }
        internal void Bind(FirstPersonController controller, PlayerLight playerLight)
        {
            PlayerSettings settings = UnityEngine.Object.FindObjectOfType<PlayerSettings>();
            if (model != null && model != settings) Release();
            // Positive raw values are also evidence of completed Start when enabling in an existing scene.
            if (settings == null || (started != settings && !(settings.RawSensitivity > 0 && settings.RawLightIntensity > 0))) return;
            model = settings;
            if (controller != Controller)
            {
                ReleaseSensitivity();
                Controller = controller;
                if (controller != null && sensitivitySetter != null && FovMath.Finite(settings.RawSensitivity))
                {
                    modelSensitivity = settings.Sensitivity; controllerSensitivity = controller.RotationSpeed; sensitivityCaptured = true;
                    try { internalCall = true; settings.SetSensitivity(settings.RawSensitivity); }
                    catch { ReleaseSensitivity(); throw; }
                    finally { internalCall = false; }
                }
            }
            Light nextLight = playerLight == null ? null : playerLight.GetComponent<Light>();
            if (light != nextLight)
            {
                ReleaseLight(); light = nextLight;
                if (light != null && lightSetter != null && settings.RawLightIntensity > 0 && FovMath.Finite(settings.RawLightIntensity))
                {
                    modelLight = settings.LightIntensity; lightIntensity = light.intensity; lightCaptured = true;
                    try { ApplyBrightness(ComfortPlugin.Instance.Store.Number("Display.BrightnessLevel", 5)); }
                    catch { ReleaseLight(); throw; }
                }
            }
        }
        internal void ApplyBrightness(double level)
        {
            if (!BrightnessReady) throw new InvalidOperationException("brightness unavailable");
            try { internalCall = true; model.SetLightIntensity(model.RawLightIntensity * (float)level / 5); }
            finally { internalCall = false; }
        }
        internal Action SnapshotBrightness()
        {
            PlayerSettings capturedModel = model; Light capturedLight = light;
            float m = model.LightIntensity, v = light.intensity;
            return () => { if (capturedModel != null) lightSetter.Invoke(capturedModel, new object[] { m }); if (capturedLight != null) capturedLight.intensity = v; };
        }
        private sealed class ExternalCall
        {
            internal PlayerSettings Model;
            internal RestoreOnce Rollback;
            internal bool Complete;
        }
        private static PlayerSettingsBinding Active => ComfortPlugin.Instance?.Binding;
        private static void BeforeSensitivity(PlayerSettings __instance, out ExternalCall __state)
        {
            __state = null; var b = Active;
            if (b == null || b.internalCall || !b.SensitivityReady || b.model != __instance) return;
            var target = b.Controller; float oldModel = __instance.Sensitivity, oldValue = target.RotationSpeed;
            __state = new ExternalCall { Model = __instance, Rollback = new RestoreOnce(() => { if (__instance != null) b.sensitivitySetter.Invoke(__instance, new object[] { oldModel }); if (target != null) target.RotationSpeed = oldValue; }) };
        }
        private static void BeforeLight(PlayerSettings __instance, out ExternalCall __state)
        {
            __state = null; var b = Active;
            if (b == null || b.internalCall || !b.BrightnessReady || b.model != __instance) return;
            __state = new ExternalCall { Model = __instance, Rollback = new RestoreOnce(b.SnapshotBrightness()) };
        }
        private static void AfterSensitivity(PlayerSettings __instance, ExternalCall __state)
        {
            var b = Active; if (__state == null || b == null) return;
            if (!FovMath.Finite(__instance.Sensitivity)) { __state.Rollback.Restore(); __state.Complete = true; return; }
            b.modelSensitivity = __instance.Sensitivity; b.controllerSensitivity = b.Controller.RotationSpeed; __state.Complete = true;
        }
        private static void AfterLight(PlayerSettings __instance, ExternalCall __state)
        {
            var b = Active; if (__state == null || b == null) return;
            double level = __instance.LightIntensity / __instance.RawLightIntensity * 5;
            if (!ComfortPlugin.Instance.WriteNumber("player_brightness", level)) __state.Rollback.Restore();
            else { b.modelLight = __instance.LightIntensity; b.lightIntensity = b.light.intensity; }
            __state.Complete = true;
        }
        private static Exception FinalizeExternal(Exception __exception, ExternalCall __state)
        {
            if (__state != null && !__state.Complete) __state.Rollback.Restore();
            return __exception;
        }
        private void ReleaseSensitivity()
        {
            try { if (sensitivityCaptured && model != null) sensitivitySetter.Invoke(model, new object[] { modelSensitivity }); }
            finally
            {
                try { if (sensitivityCaptured && Controller != null) Controller.RotationSpeed = controllerSensitivity; }
                finally { sensitivityCaptured = false; Controller = null; }
            }
        }
        private void ReleaseLight()
        {
            try { if (lightCaptured && model != null) lightSetter.Invoke(model, new object[] { modelLight }); }
            finally
            {
                try { if (lightCaptured && light != null) light.intensity = lightIntensity; }
                finally { lightCaptured = false; light = null; }
            }
        }
        internal void Release()
        {
            try { ReleaseSensitivity(); }
            finally { try { ReleaseLight(); } finally { model = null; } }
        }
        public void Dispose() { try { Release(); } finally { patch.UnpatchSelf(); started = null; } }
    }
}
