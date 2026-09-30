using System;
using System.IO;
using BepInEx;
using Disappearance.Shared;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Disappearance.GameplayQoL
{
    // GameplayQoL owns the G command, its setting, sign materials and restoration.
    internal sealed class ForestSignToggleModule : MonoBehaviour
    {
        private readonly ForestSignGlow signs = new ForestSignGlow();
        private ForestSignSettings settings;
        private Scene scene;
        private bool running;
        private bool captured;
        private bool failed;
        private float nextCapture;
        private DiagnosticClient diagnostic;

        internal bool Available => running && captured && !failed &&
            scene.name == "VillageScene" && scene.handle == SceneManager.GetActiveScene().handle &&
            signs.SignRendererCount > 0 && GameplayQoLPlugin.Instance != null &&
            !GameplayQoLPlugin.Instance.Modal.IsOccupied;

        private void Awake()
        {
            try
            {
                settings = new ForestSignSettings(
                    Path.Combine(Paths.ConfigPath, "local.disappearance.gameplayqol.cfg"),
                    GameplayQoLPlugin.Log.LogInfo);
                settings.Migrate();
                // G is an in-session visibility aid. A previous session must not
                // silently reveal the forest route on the next launch.
                settings.BeginSession();
            }
            catch (Exception error)
            {
                failed = true;
                GameplayQoLPlugin.Log.LogError("Forest sign settings unavailable: " + error);
            }
        }

        private void OnEnable()
        {
            if (settings == null) return;
            running = true;
            diagnostic = new DiagnosticClient(GameplayQoLPlugin.PluginId, message => GameplayQoLPlugin.Log.LogWarning(message),
                new DiagnosticClient.Row { Id="signs", Label="看板発光（G）", Order=150,
                    Value=() => captured && !failed && scene.name == "VillageScene" && scene.handle == SceneManager.GetActiveScene().handle && signs.SignRendererCount > 0 ? (settings.GlowForestSigns ? "ON" : "OFF") : null });
            scene = SceneManager.GetActiveScene();
            captured = false;
            failed = false;
            nextCapture = Time.unscaledTime + .5f;
            SceneManager.activeSceneChanged += Changed;
        }

        private void Changed(Scene previous, Scene current)
        {
            try { signs.Restore(); }
            catch (Exception error) { GameplayQoLPlugin.Log.LogError("Forest signs scene release: " + error); }
            scene = current;
            captured = failed = false;
            nextCapture = Time.unscaledTime + .5f;
        }

        private void Capture()
        {
            if (captured || failed || scene.name != "VillageScene" ||
                scene.handle != SceneManager.GetActiveScene().handle || Time.unscaledTime < nextCapture)
                return;
            captured = true;
            try
            {
                signs.Capture(scene);
                signs.Apply(settings.GlowForestSigns, settings.GlowIntensity);
                GameplayQoLPlugin.Log.LogInfo("Forest signs captured: renderers=" +
                    signs.SignRendererCount + ", glow=" + settings.GlowForestSigns + ".");
            }
            catch (Exception error)
            {
                failed = true;
                try { signs.Restore(); }
                catch (Exception rollback) { GameplayQoLPlugin.Log.LogError("Forest signs restore: " + rollback); }
                GameplayQoLPlugin.Log.LogError("Forest signs capture failed: " + error);
            }
        }

        private void Update()
        {
            if (!running) return;
            Scene current = SceneManager.GetActiveScene();
            if (current.handle != scene.handle) Changed(scene, current);
            Capture();
            diagnostic?.Update();
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.gKey.wasPressedThisFrame && Available)
                Toggle();
        }

        private void Toggle()
        {
            bool before = settings.GlowForestSigns;
            try
            {
                settings.SetGlowForestSigns(!before);
                signs.Apply(settings.GlowForestSigns, settings.GlowIntensity);
                settings.Save();
                GameplayQoLPlugin.Log.LogInfo("Forest sign glow=" + settings.GlowForestSigns + ".");
            }
            catch (Exception error)
            {
                settings.SetGlowForestSigns(before);
                try { signs.Restore(); signs.Capture(scene); signs.Apply(before, settings.GlowIntensity); }
                catch (Exception rollback) { failed = true; GameplayQoLPlugin.Log.LogError("Forest signs rollback: " + rollback); }
                GameplayQoLPlugin.Log.LogError("Forest sign toggle failed: " + error);
            }
        }

        private void OnDisable() => Stop();
        private void OnDestroy() => Stop();

        private void Stop()
        {
            if (!running) return;
            running = false;
            diagnostic?.Dispose(); diagnostic = null;
            SceneManager.activeSceneChanged -= Changed;
            try { signs.Restore(); }
            catch (Exception error) { GameplayQoLPlugin.Log.LogError("Forest signs stop: " + error); }
            captured = false;
        }
    }
}
