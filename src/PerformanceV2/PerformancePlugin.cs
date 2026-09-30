using System;
using System.IO;
using BepInEx;
using BepInEx.Unity.Mono;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Disappearance.PerformanceV2
{
    [BepInPlugin(PluginId, "Disappearance Performance P6", "0.1.1")]
    public sealed class PerformancePlugin : BaseUnityPlugin
    {
        public const string PluginId = "local.disappearance.performance.v2";
        private PerformanceSettings settings;
        private RenderToggles toggles;
        private PerformanceDiagnostics diagnostics;
        private DebugOverlay overlay;
        private DiagnosticRegistry registry;
        private RenderToggleAudit renderAudit;
        private Scene scene;
        private float nextCapture;
        private bool stopped;
        private bool captureFailed;
        private bool f1ConflictLogged;

        private void Awake()
        {
            try
            {
                settings = new PerformanceSettings(Path.Combine(Paths.ConfigPath, PluginId + ".cfg"), Logger.LogInfo);
                settings.Migrate();
                toggles = new RenderToggles(settings, Logger.LogInfo);
                diagnostics = new PerformanceDiagnostics(Logger.LogInfo);
                toggles.ComparisonChanged += diagnostics.Reset;
                if (settings.TraceRenderToggles) renderAudit = new RenderToggleAudit(toggles, Logger.LogInfo);
                registry = new DiagnosticRegistry(Logger.LogWarning);
                overlay = new DebugOverlay(settings, toggles, diagnostics, registry);
                scene = SceneManager.GetActiveScene();
                nextCapture = Time.unscaledTime + .5f;
                SceneManager.activeSceneChanged += Changed;
                Logger.LogInfo("Performance P6 loaded: F6-F11 comparisons, Ctrl+Shift+F1 diagnostics. No F12 binding.");
            }
            catch (Exception error) { Logger.LogError("Performance P6 initialization failed: " + error); Stop(); }
        }

        private void Changed(Scene previous, Scene current)
        {
            toggles?.Restore();
            diagnostics?.Reset();
            registry?.ChangedScene();
            scene = current;
            captureFailed = false;
            nextCapture = Time.unscaledTime + .5f;
        }

        private void Capture()
        {
            if (captureFailed || toggles.Ready || scene.name != "VillageScene" ||
                scene.handle != SceneManager.GetActiveScene().handle || Time.unscaledTime < nextCapture) return;
            try { toggles.Capture(scene); diagnostics.Reset(); renderAudit?.Schedule(); }
            catch (Exception error) { captureFailed = true; Logger.LogError("Performance render capture failed: " + error); }
        }

        private void Update()
        {
            if (stopped) return;
            Scene current = SceneManager.GetActiveScene();
            if (current.handle != scene.handle) Changed(scene, current);
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f1Key.wasPressedThisFrame &&
                (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed) &&
                (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed))
            {
                if (F1ClaimedByGame())
                {
                    if (!f1ConflictLogged) { Logger.LogWarning("Ctrl+Shift+F1 disabled: active game InputAction claims F1."); f1ConflictLogged = true; }
                }
                else
                {
                    try { settings.SetOverlay(!settings.ShowOverlay); Logger.LogInfo("Diagnostic panels visible=" + settings.ShowOverlay); }
                    catch (Exception error) { Logger.LogError("P6 overlay setting failed: " + error); }
                }
            }
            Capture();
            if (toggles.Ready)
            {
                try { toggles.Toggle(keyboard); }
                catch (Exception error) { captureFailed = true; toggles.Restore(); Logger.LogError("P6 comparison failed and was restored: " + error); }
                diagnostics.Tick(settings.ReportSeconds, toggles);
            }
        }

        private static bool F1ClaimedByGame()
        {
            foreach (InputActionAsset asset in Resources.FindObjectsOfTypeAll<InputActionAsset>())
            {
                if (asset == null || !asset.enabled) continue;
                foreach (InputBinding binding in asset.bindings)
                {
                    string path = binding.effectivePath;
                    if (path != null && path.EndsWith("/f1", StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            return false;
        }

        private void LateUpdate() { if (!stopped) renderAudit?.Tick(); }
        private void OnGUI() { if (!stopped) overlay?.Draw(); }
        public int GetDiagnosticsApiVersion() => 1;
        public string GetDiagnosticsInstanceId() => registry?.InstanceId;
        public int GetDiagnosticsGeneration() => registry?.Generation ?? 0;
        public int GetDiagnosticsSceneEpoch() => registry?.SceneEpoch ?? 0;
        public bool RegisterDiagnostic(string ownerId, string itemId, string definitionJson) =>
            !stopped && registry != null && registry.Register(ownerId, itemId, definitionJson);
        public bool UpdateDiagnostic(string ownerId, string itemId, string stateJson) =>
            !stopped && registry != null && registry.Update(ownerId, itemId, stateJson);
        public void UnregisterDiagnostics(string ownerId) => registry?.Unregister(ownerId);
        private void OnDisable() => Stop();
        private void OnDestroy() => Stop();
        private void Stop()
        {
            if (stopped) return;
            stopped = true;
            SceneManager.activeSceneChanged -= Changed;
            renderAudit?.Dispose();
            try { toggles?.Restore(); } catch (Exception error) { Logger.LogError("P6 restore failed: " + error); }
            if (toggles != null && diagnostics != null) toggles.ComparisonChanged -= diagnostics.Reset;
            diagnostics?.Dispose();
        }
    }
}
