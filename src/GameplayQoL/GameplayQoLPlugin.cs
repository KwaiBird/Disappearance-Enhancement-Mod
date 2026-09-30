using System;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using Disappearance.Shared;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Disappearance.GameplayQoL
{
    [BepInPlugin(PluginId, PluginName, PluginVersion)]
    public sealed class GameplayQoLPlugin : BaseUnityPlugin
    {
        public const string PluginId = "local.disappearance.gameplayqol";
        public const string PluginName = "Disappearance Gameplay QoL";
        public const string PluginVersion = "0.4.18";

        internal static GameplayQoLPlugin Instance { get; private set; }
        private static ManualLogSource sharedLog;
        internal static ManualLogSource Log => Instance == null ? sharedLog : Instance.Logger;
        internal readonly ModalSession Modal = new ModalSession();
        internal bool PresentationAvailable => presentation != null && presentation.Ready;
        private PresentationClient presentation;
        private FixedGrassDetailsModule fixedGrass;
        internal CheckpointModule Checkpoints { get; private set; }

        public string GetActiveModalOwner() => Modal.Owner.ToString();

        private void Awake()
        {
            Instance = this;
            sharedLog = Logger;
            bool grassSettingsReady = false;
            try
            {
                var migration = new LegacyConfigMigration(Config.ConfigFilePath, Logger.LogInfo);
                migration.Migrate(new LegacyConfigMigration.Spec("S28", "Terrain.Enabled",
                    LegacyConfigMigration.ValueType.Boolean, "true", 0, 0,
                    new LegacyConfigMigration.Source("local.disappearance.fixedgrassdetails.cfg", "Terrain.Enabled"),
                    new LegacyConfigMigration.Source("local.disappearance.uicontrols.cfg", "Terrain.Enabled")));
                Config.Reload();
                grassSettingsReady = true;
            }
            catch (Exception exception) { Logger.LogError("Fixed grass settings migration failed: " + exception); }
            try { gameObject.AddComponent<MenuInputIsolation>(); }
            catch (Exception exception) { Logger.LogError("Menu isolation initialization failed: " + exception); }
            try { gameObject.AddComponent<KeyOcclusionModule>(); }
            catch (Exception exception) { Logger.LogError("Key occlusion module initialization failed: " + exception); }
            try { gameObject.AddComponent<InteractionMarkerModule>(); }
            catch (Exception exception) { Logger.LogError("Interaction marker module initialization failed: " + exception); }
            try { gameObject.AddComponent<SprintModule>(); }
            catch (Exception exception) { Logger.LogError("Sprint module initialization failed: " + exception); }
            try { Checkpoints = gameObject.AddComponent<CheckpointModule>(); }
            catch (Exception exception) { Logger.LogError("Checkpoint module initialization failed: " + exception); }
            try { gameObject.AddComponent<TitleCheckpointMenu>(); }
            catch (Exception exception) { Logger.LogError("Title checkpoint menu initialization failed: " + exception); }
            try { gameObject.AddComponent<PauseCheckpointMenu>(); }
            catch (Exception exception) { Logger.LogError("Pause checkpoint menu initialization failed: " + exception); }
            try { gameObject.AddComponent<DialogueModule>(); }
            catch (Exception exception) { Logger.LogError("Dialogue module initialization failed: " + exception); }
            try { gameObject.AddComponent<PadlockModule>(); }
            catch (Exception exception) { Logger.LogError("Padlock module initialization failed: " + exception); }
            try { gameObject.AddComponent<ForestSignToggleModule>(); }
            catch (Exception exception) { Logger.LogError("Forest sign command initialization failed: " + exception); }
            try { presentation = gameObject.AddComponent<PresentationClient>(); }
            catch (Exception exception) { Logger.LogError("Presentation client initialization failed: " + exception); }
            try { if (grassSettingsReady) { Config.Reload(); fixedGrass = new FixedGrassDetailsModule(Config, Logger); } }
            catch (Exception exception) { Logger.LogError("Fixed grass module initialization failed: " + exception); }
            SceneManager.sceneLoaded += OnSceneChanged;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            Logger.LogInfo("GameplayQoL stage 3 modules initialized.");
        }

        private void Update()
        {
            try { fixedGrass?.Update(); }
            catch (Exception exception)
            {
                Logger.LogError("Fixed grass module stopped after failure: " + exception);
                try { fixedGrass?.Stop(); }
                catch (Exception restore) { Logger.LogError("Fixed grass cleanup failed: " + restore); }
                fixedGrass = null;
            }
        }

        private void OnSceneChanged(Scene scene, LoadSceneMode mode) { fixedGrass?.OnSceneChanged(); }
        private void OnSceneUnloaded(Scene scene) { fixedGrass?.OnSceneChanged(); }

        private void OnDisable() { fixedGrass?.Stop(); }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneChanged;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            fixedGrass?.Stop();
            if (Instance == this) Instance = null;
        }
    }
}
