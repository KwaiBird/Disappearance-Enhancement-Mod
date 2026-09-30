using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using Disappearance.UIControls;
using Disappearance.Shared;
using StarterAssets;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Disappearance.Comfort
{
    [BepInPlugin(PluginId, "Disappearance Comfort", "0.4.9")]
    public sealed class ComfortPlugin : BaseUnityPlugin
    {
        public const string PluginId = "local.disappearance.comfort";
        internal static ComfortPlugin Instance;
        internal static ManualLogSource Log;
        internal ComfortConfigStore Store;
        internal PlayerSettingsBinding Binding;
        internal FieldOfView Fov;
        internal PlayerLighting Lighting;
        internal VignetteEffect Vignette;
        private CameraMotion motion;
        private LookSensitivity sensitivity;
        private SpawnGrounding grounding;
        private PresentationClient presentation;
        private DiagnosticClient diagnostic;
        private SceneCoordinator scenes;
        private bool stopped;
        private float nextConfig;
        private void Awake()
        {
            Instance = this; Log = Logger; Config.SaveOnConfigSet = false;
            Store = new ComfortConfigStore(Path.Combine(Paths.ConfigPath, "local.disappearance.comfort.cfg"), Logger.LogInfo);
            try { Store.Migrate(); }
            catch (Exception e) { Logger.LogError("Comfort configuration unavailable; no patches installed: " + e); stopped = true; return; }
            TryModule("PlayerSettings", () => Binding = new PlayerSettingsBinding());
            TryModule("CameraMotion", () => motion = new CameraMotion());
            if (Binding != null) TryModule("LookSensitivity", () => sensitivity = new LookSensitivity());
            TryModule("SpawnGrounding", () => grounding = new SpawnGrounding());
            TryModule("FOV/EnemyVisibility", () => Fov = new FieldOfView());
            Lighting = new PlayerLighting();
            Vignette = new VignetteEffect();
            scenes = new SceneCoordinator(this, () => { motion?.Release(); grounding?.Reset(); });
            presentation = new PresentationClient(this);
            diagnostic = new DiagnosticClient(PluginId, message => Logger.LogWarning(message),
                new DiagnosticClient.Row { Id="fov", Label="水平FOV（F3/F4/F5）", Order=100, Value=() => Fov != null && Fov.Ready ? Fov.CurrentHorizontal.ToString("F0") + "°　初期値 " + Fov.Default.ToString("F0") + "°" : null },
                new DiagnosticClient.Row { Id="lighting", Label="照明モード（Home）", Order=110, Value=() => Lighting.Ready ? Store.Get("Lights.PlayerIllumination", "Spot") : null },
                new DiagnosticClient.Row { Id="angle", Label="照射角の視野角追従（End）", Order=120, Value=() => Lighting.Ready ? (Store.Get("Lights.MatchFlashlightToFov", "true") == "true" ? "ON" : "OFF") + "　照射角 " + Lighting.Angle.ToString("F0") + "°" : null },
                new DiagnosticClient.Row { Id="brightness", Label="明るさ（設定画面）", Order=130, Value=() => Ready("player_brightness") && Lighting.Ready ? Store.Number("Display.BrightnessLevel", 5).ToString("0.##") + "　ライト強度 " + Lighting.Intensity.ToString("F1") : null },
                new DiagnosticClient.Row { Id="vignette", Label="周辺減光（Delete）", Order=140, Value=() => Vignette.Ready ? (Vignette.Active ? "ON" : "OFF") : null });
            Logger.LogInfo("Comfort P4 implementation loaded. Settings v2; optional presentation v2. " + Store.Path);
        }
        private void TryModule(string name, Action start)
        {
            try { start(); } catch (Exception e) { Logger.LogError(name + " failed independently: " + e); }
        }
        internal bool Ready(string id)
        {
            if (stopped) return false;
            switch (id)
            {
                case "mouse_sensitivity": case "controller_sensitivity": return sensitivity != null && Binding != null && Binding.SensitivityReady;
                case "player_brightness": return Binding != null && Binding.BrightnessReady;
                case "horizontal_fov": return Fov != null && Fov.Ready;
                default: return false;
            }
        }
        public int GetSettingsApiVersion() => 2;
        internal static string Status(string status) => "{\"schema\":2,\"status\":\"" + status + "\"}";
        private double Minimum(SettingSpec spec) => spec.Id == "horizontal_fov" ? Fov.Default : spec.Min;
        private double Default(SettingSpec spec) => spec.Id == "horizontal_fov" ? Fov.Default : spec.Default;
        private bool Accept(SettingSpec spec, double value) => spec.Accept(value) && value >= Minimum(spec);
        public string ReadSetting(string settingId)
        {
            SettingSpec spec = SettingSpec.Find(settingId);
            if (spec == null) return Status("unknown_id");
            if (!Ready(settingId)) return Status("unavailable");
            return "{\"schema\":2,\"status\":\"ok\",\"value\":" + SettingSpec.Number(Store.Number(spec.Path, Default(spec))) +
                ",\"min\":" + SettingSpec.Number(Minimum(spec)) + ",\"max\":" + SettingSpec.Number(spec.Max) +
                ",\"defaultValue\":" + SettingSpec.Number(Default(spec)) + "}";
        }
        public string WriteSetting(string settingId, string requestJson)
        {
            SettingSpec spec = SettingSpec.Find(settingId);
            if (spec == null) return Status("unknown_id");
            if (!Stage2Json.TryParse(requestJson, out object raw, out _) || !Stage2Json.TryObject(raw, out var request)) return Status("invalid_value");
            if (!Stage2Json.TryInteger(request, "schema", out int schema) || schema != 2) return Status("unsupported_version");
            if (!Stage2Json.TryString(request, "operation", out string operation) || (operation != "reset" && operation != "set")) return Status("invalid_value");
            if (!Ready(settingId)) return Status("unavailable");
            double value = Default(spec);
            if (operation == "reset" ? request.Count != 2 : request.Count != 3 || !Stage2Json.TryNumber(request, "value", out value)) return Status("invalid_value");
            if (!Accept(spec, value)) return Status("invalid_value");
            return WriteNumber(settingId, value) ? ReadSetting(settingId) : Status("apply_failed");
        }
        internal bool WriteNumber(string id, double value)
        {
            SettingSpec spec = SettingSpec.Find(id);
            if (spec == null || !Ready(id) || !Accept(spec, value)) return false;
            var previous = Store.Snapshot();
            Action rollback = () => { };
            try
            {
                if (id == "player_brightness") rollback = Binding.SnapshotBrightness();
                if (id == "horizontal_fov") { Action a = Fov.Snapshot(), b = Lighting.Snapshot(); rollback = () => { try { a(); } finally { b(); } }; }
            }
            catch (Exception e) { Logger.LogError(e); return false; }
            return WriteTransaction.Run(() =>
            {
                ApplyValue(spec, value);
            }, Store.Save, () => { Store.Restore(previous); rollback(); }, e => Logger.LogError("Comfort setting rollback: " + e));
        }
        private void ApplyValue(SettingSpec spec, double value)
        {
            Store.Set(spec.Path, value);
            if (spec.Id == "player_brightness") Binding.ApplyBrightness(value);
            if (spec.Id == "horizontal_fov") { Fov.Apply(value); Lighting.Apply(Store, Fov); }
        }
        private bool WriteLightOptions(string mode, string match, double ambient)
        {
            if (stopped || !Lighting.Ready || (mode != "Spot" && mode != "Ambient" && mode != "Off") || (match != "true" && match != "false") || !FovMath.Finite(ambient) || ambient < 0 || ambient > 1) return false;
            var previous = Store.Snapshot(); Action restore = Lighting.Snapshot();
            return WriteTransaction.Run(() => { Store.Set("Lights.PlayerIllumination", mode); Store.Set("Lights.MatchFlashlightToFov", match); Store.Set("Lights.AmbientBrightness", ambient); Lighting.Apply(Store, Fov); }, Store.Save,
                () => { Store.Restore(previous); restore(); }, e => Logger.LogError("Comfort lights rollback: " + e));
        }
        private void Update()
        {
            if (stopped) return;
            scenes.Update();
            presentation.Update();
            diagnostic.Update();
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && Fov != null && Fov.Ready)
            {
                double value = Store.Number("Camera.HorizontalFov", Fov.Baseline);
                if (keyboard.f3Key.wasPressedThisFrame) WriteNumber("horizontal_fov", Math.Max(Fov.Default, value - 5));
                if (keyboard.f4Key.wasPressedThisFrame) WriteNumber("horizontal_fov", Math.Min(SettingSpec.Find("horizontal_fov").Max, value + 5));
                if (keyboard.f5Key.wasPressedThisFrame) WriteNumber("horizontal_fov", Fov.Default);
            }
            if (keyboard != null && Lighting.Ready)
            {
                string mode = Store.Get("Lights.PlayerIllumination", "Spot"), match = Store.Get("Lights.MatchFlashlightToFov", "true");
                if (keyboard.homeKey.wasPressedThisFrame) WriteLightOptions(mode == "Spot" ? "Ambient" : mode == "Ambient" ? "Off" : "Spot", match, Store.Number("Lights.AmbientBrightness", .18));
                if (keyboard.endKey.wasPressedThisFrame) WriteLightOptions(mode, match == "true" ? "false" : "true", Store.Number("Lights.AmbientBrightness", .18));
            }
            if (keyboard != null && Vignette.Ready && keyboard.deleteKey.wasPressedThisFrame)
            {
                var previous = Store.Snapshot();
                bool disabled = Store.Get("Rendering.DisableVignette", "false") == "true";
                WriteTransaction.Run(() => { Store.Set("Rendering.DisableVignette", disabled ? "false" : "true"); Vignette.Apply(Store); }, Store.Save,
                    () => { Store.Restore(previous); Vignette.Apply(Store); }, e => Logger.LogError("Vignette toggle rollback: " + e));
            }
            if (Time.unscaledTime >= nextConfig)
            {
                nextConfig = Time.unscaledTime + 1;
                try { ApplyExternalConfig(); } catch (Exception e) { Logger.LogWarning("Comfort config reload failed: " + e.Message); }
            }
        }
        private void ApplyExternalConfig()
        {
            if (!Store.TryExternal(out var candidate)) return;
            var changes = new Dictionary<SettingSpec, double>();
            foreach (SettingSpec spec in SettingSpec.All)
            {
                if (!candidate.TryGetValue(spec.Path, out string raw)) continue;
                if (!ComfortConfigStore.TryNumber(raw, out double value) || !Ready(spec.Id) || !Accept(spec, value)) return;
                if (value == Store.Number(spec.Path, spec.Default)) continue;
                if (!Ready(spec.Id)) return; // Retain the external file intact until every requested target is ready.
                changes[spec] = value;
            }
            string mode = candidate.TryGetValue("Lights.PlayerIllumination", out string m) ? m : Store.Get("Lights.PlayerIllumination", "Spot");
            string match = candidate.TryGetValue("Lights.MatchFlashlightToFov", out string f) ? f.ToLowerInvariant() : Store.Get("Lights.MatchFlashlightToFov", "true");
            double ambient = Store.Number("Lights.AmbientBrightness", .18);
            if (candidate.TryGetValue("Lights.AmbientBrightness", out string a) && !ComfortConfigStore.TryNumber(a, out ambient)) return;
            bool lightsChanged = mode != Store.Get("Lights.PlayerIllumination", "Spot") || match != Store.Get("Lights.MatchFlashlightToFov", "true") || ambient != Store.Number("Lights.AmbientBrightness", .18);
            if ((mode != "Spot" && mode != "Ambient" && mode != "Off") || (match != "true" && match != "false") || !FovMath.Finite(ambient) || ambient < 0 || ambient > 1 || (lightsChanged && !Lighting.Ready)) return;
            string disableVignette = candidate.TryGetValue("Rendering.DisableVignette", out string disabled) ? disabled.ToLowerInvariant() : Store.Get("Rendering.DisableVignette", "false");
            double vignetteIntensity = Store.Number("Rendering.VignetteIntensity", .35);
            double vignetteSmoothness = Store.Number("Rendering.VignetteSmoothness", .65);
            if (candidate.TryGetValue("Rendering.VignetteIntensity", out string intensity) && !ComfortConfigStore.TryNumber(intensity, out vignetteIntensity)) return;
            if (candidate.TryGetValue("Rendering.VignetteSmoothness", out string smoothness) && !ComfortConfigStore.TryNumber(smoothness, out vignetteSmoothness)) return;
            bool vignetteChanged = disableVignette != Store.Get("Rendering.DisableVignette", "false") ||
                vignetteIntensity != Store.Number("Rendering.VignetteIntensity", .35) ||
                vignetteSmoothness != Store.Number("Rendering.VignetteSmoothness", .65);
            if ((disableVignette != "true" && disableVignette != "false") || vignetteIntensity < 0 || vignetteIntensity > 1 ||
                vignetteSmoothness < .01 || vignetteSmoothness > 1 || (vignetteChanged && !Vignette.Ready)) return;
            if (changes.Count == 0 && !lightsChanged && !vignetteChanged) return;
            var previous = Store.Snapshot(); var restores = new List<Action>();
            foreach (SettingSpec spec in changes.Keys)
            {
                if (spec.Id == "player_brightness") restores.Add(Binding.SnapshotBrightness());
                if (spec.Id == "horizontal_fov") restores.Add(Fov.Snapshot());
            }
            restores.Add(Lighting.Snapshot());
            WriteTransaction.Run(() =>
            {
                foreach (var pair in changes) ApplyValue(pair.Key, pair.Value);
                Store.Set("Lights.PlayerIllumination", mode); Store.Set("Lights.MatchFlashlightToFov", match); Store.Set("Lights.AmbientBrightness", ambient);
                Lighting.Apply(Store, Fov);
                Store.Set("Rendering.DisableVignette", disableVignette);
                Store.Set("Rendering.VignetteIntensity", vignetteIntensity);
                Store.Set("Rendering.VignetteSmoothness", vignetteSmoothness);
                Vignette.Apply(Store);
            }, Store.Save, () => { Store.Restore(previous); foreach (Action restore in restores) try { restore(); } catch (Exception e) { Logger.LogError(e); } Vignette.Apply(Store); }, e => Logger.LogError("Config transaction failed: " + e));
        }
        private void OnDisable() => Stop();
        private void OnDestroy() => Stop();
        private void Stop()
        {
            if (stopped) return; stopped = true;
            Fov?.DisableEnemy();
            foreach (Action release in new Action[] { () => scenes?.Dispose(), () => presentation?.Dispose(), () => diagnostic?.Dispose(), () => sensitivity?.Dispose(), () => grounding?.Dispose(), () => Binding?.Dispose(), () => Fov?.Dispose(), () => Lighting?.Release(), () => Vignette?.Release(), () => motion?.Dispose() })
                try { release(); } catch (Exception e) { Logger.LogError("Comfort shutdown: " + e); }
            Instance = null;
        }
    }

    internal sealed class SceneCoordinator : IDisposable
    {
        private readonly ComfortPlugin plugin;
        private readonly Action reset;
        private Scene scene;
        private float next;
        private bool bindingFailed, fovFailed, lightingFailed, vignetteFailed;
        internal SceneCoordinator(ComfortPlugin plugin, Action reset)
        {
            this.plugin = plugin; this.reset = reset; scene = SceneManager.GetActiveScene();
            SceneManager.activeSceneChanged += Changed;
        }
        private void Changed(Scene old, Scene current) { Release(); scene = current; next = 0; bindingFailed = fovFailed = lightingFailed = vignetteFailed = false; }
        internal void Update()
        {
            if (scene != SceneManager.GetActiveScene()) Changed(scene, SceneManager.GetActiveScene());
            if (scene.name != "VillageScene") return;
            if (Time.unscaledTime >= next)
            {
                next = Time.unscaledTime + .5f;
                FirstPersonController controller = Find<FirstPersonController>(); PlayerLight light = Find<PlayerLight>();
                Attempt(ref bindingFailed, () => plugin.Binding?.Bind(controller, light), () => plugin.Binding?.Release(), "PlayerSettings");
                Attempt(ref fovFailed, () => plugin.Fov?.Discover(scene, plugin.Store), () => plugin.Fov?.Release(), "FOV");
                Attempt(ref lightingFailed, () => { plugin.Lighting.Bind(light, scene); plugin.Lighting.Apply(plugin.Store, plugin.Fov); }, plugin.Lighting.Release, "Lighting");
                Attempt(ref vignetteFailed, () => plugin.Vignette.Bind(scene, plugin.Store), plugin.Vignette.Release, "Vignette");
            }
            if (!fovFailed && plugin.Fov != null && plugin.Fov.AspectChanged)
                Attempt(ref fovFailed, () => { plugin.WriteNumber("horizontal_fov", FovMath.Clamp(plugin.Store.Number("Camera.HorizontalFov", plugin.Fov.Default), plugin.Fov.Default, SettingSpec.Find("horizontal_fov").Max)); plugin.Lighting.Apply(plugin.Store, plugin.Fov); }, () => plugin.Fov.Release(), "FOV aspect");
        }
        private T Find<T>() where T : Component
        {
            foreach (T value in UnityEngine.Object.FindObjectsOfType<T>()) if (value.gameObject.scene == scene) return value;
            return null;
        }
        private static void Attempt(ref bool failed, Action apply, Action restore, string name)
        {
            if (failed) return;
            try { apply(); }
            catch (Exception e) { failed = true; try { restore(); } catch (Exception r) { ComfortPlugin.Log.LogError(r); } ComfortPlugin.Log.LogError(name + " failed for this scene: " + e); }
        }
        private void Release()
        {
            plugin.Fov?.DisableEnemy();
            foreach (Action release in new Action[] { () => plugin.Fov?.Release(), () => plugin.Lighting?.Release(), () => plugin.Vignette?.Release(), () => plugin.Binding?.Release(), reset })
                try { release(); } catch (Exception e) { ComfortPlugin.Log.LogError(e); }
        }
        public void Dispose() { SceneManager.activeSceneChanged -= Changed; Release(); }
    }
}
