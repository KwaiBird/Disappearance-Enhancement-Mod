using System;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.Mono;
using BepInEx.Unity.Mono.Bootstrap;
using Disappearance.Shared;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Disappearance.UIControls
{
    [BepInPlugin(PluginId, PluginName, PluginVersion)]
    public sealed class UIControlsPlugin : BaseUnityPlugin
    {
        public const string PluginId = "local.disappearance.uicontrols";
        public const string PluginName = "Disappearance UI Controls";
        public const string PluginVersion = "0.5.34";

        private ActionRegistry registry;
        private ContextHintRenderer hints;
        private TargetCueRenderer targetCues;
        private TitleMenuHints titleHints;
        private PauseMenuHints pauseHints;
        private PauseMenuNavigation pauseNavigation;
        private SettingsRegistry settings;
        private SettingsPresentation settingsView;
        private ReadableTextModule readableText;
        private ReticleModule reticle;
        private MenuBindingsModule menuBindings;
        private ControlsTableModule controlsTable;
        private PauseMenuHandler pauseMenu;
        private readonly ActiveControlHints activeControls = new ActiveControlHints();
        private readonly GameFadeVisibility fadeVisibility = new GameFadeVisibility();
        private ConfigEntry<bool> showSharedHints;
        private bool selectCandidate;
        private bool selectChorded;
        private float nextStandardHintState;
        private int standardHintSequence;

        private void Awake()
        {
            bool migrated = false;
            try
            {
                var migration = new LegacyConfigMigration(Config.ConfigFilePath, Logger.LogInfo);
                var readable = new LegacyConfigMigration.Source("local.disappearance.readableui.cfg", "Display.ScaleVillageUI");
                migration.Migrate(
                    new LegacyConfigMigration.Spec("S23", "Display.ScaleVillageUI", LegacyConfigMigration.ValueType.Boolean,
                        "true", 0, 0, readable),
                    new LegacyConfigMigration.Spec("S24", "Display.ReferenceWidth", LegacyConfigMigration.ValueType.Integer,
                        "1920", 640, int.MaxValue,
                        new LegacyConfigMigration.Source("local.disappearance.readableui.cfg", "Display.ReferenceWidth")),
                    new LegacyConfigMigration.Spec("S25", "Display.ReferenceHeight", LegacyConfigMigration.ValueType.Integer,
                        "1080", 360, int.MaxValue,
                        new LegacyConfigMigration.Source("local.disappearance.readableui.cfg", "Display.ReferenceHeight")),
                    new LegacyConfigMigration.Spec("S26", "Reticle.DiameterAt1080p", LegacyConfigMigration.ValueType.Number,
                        "12", 4, 40,
                        new LegacyConfigMigration.Source("local.disappearance.reticle.cfg", "Reticle.DiameterAt1080p")),
                    new LegacyConfigMigration.Spec("S27", "Reticle.Opacity", LegacyConfigMigration.ValueType.Number,
                        "0.2", .1, 1,
                        new LegacyConfigMigration.Source("local.disappearance.reticle.cfg", "Reticle.Opacity")));
                Config.Reload();
                migrated = true;
            }
            catch (Exception error) { Logger.LogError("UI display settings migration failed: " + error); }
            string instanceId = Guid.NewGuid().ToString("N");
            registry = new ActionRegistry(Logger, OwnerAlive, instanceId);
            showSharedHints = Config.Bind("Display", "ShowSharedHints", true,
                "Show the shared lower-right gameplay hints. Toggle with H or an unmodified Select/View tap.");
            settings = new SettingsRegistry(instanceId, OwnerAlive);
            try { settingsView = new SettingsPresentation(settings, Logger); }
            catch (Exception e) { Logger.LogError("Settings view unavailable; action presentation remains active: " + e); }
            try
            {
                if (migrated)
                {
                    readableText = new ReadableTextModule(Config, Logger);
                    readableText.Start();
                }
            }
            catch (Exception e)
            {
                readableText?.Stop();
                readableText = null;
                Logger.LogError("Readable text module unavailable; action presentation remains active: " + e);
            }
            try { if (migrated) reticle = new ReticleModule(Config, Logger); }
            catch (Exception e) { Logger.LogError("Reticle module unavailable; action presentation remains active: " + e); }
            menuBindings = new MenuBindingsModule(Logger);
            registry.BeginScene();
            RegisterStandardActions();
            activeControls.PreferConnectedGamepad();
            hints = new ContextHintRenderer(registry, activeControls, fadeVisibility);
            targetCues = new TargetCueRenderer(registry, activeControls, fadeVisibility);
            titleHints = new TitleMenuHints(activeControls, fadeVisibility, registry);
            pauseHints = new PauseMenuHints(activeControls, registry, fadeVisibility);
            pauseNavigation = new PauseMenuNavigation();
            try { controlsTable = new ControlsTableModule(registry, Logger); controlsTable.Start(); }
            catch (Exception e)
            {
                controlsTable?.Stop();
                controlsTable = null;
                Logger.LogError("Controls table module unavailable; action presentation remains active: " + e);
            }
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            Logger.LogInfo("UIControls presentation ready. API v2 instance " + instanceId + ".");
        }

        public int GetApiVersion() => 2;
        public int GetSettingsApiVersion() => 2;
        public int GetSettingsGeneration() => settings == null ? 0 : settings.Generation;
        public string RegisterSetting(string ownerId, string settingId, string descriptorJson) => settings == null ? "{\"schema\":2,\"status\":\"stale_generation\"}" : settings.Register(ownerId, settingId, descriptorJson);
        public void UnregisterSettings(string ownerId) { settings?.Unregister(ownerId); }
        public string GetPresentationInstanceId() => registry == null ? "" : registry.InstanceId;
        public int GetActionGeneration() => registry == null ? 0 : registry.Generation;
        public int GetSceneEpoch() => registry == null ? 0 : registry.SceneEpoch;

        public bool RegisterAction(string ownerId, string actionId, string definitionJson)
        {
            return registry != null && registry.RegisterAction(ownerId, actionId, definitionJson);
        }

        public bool UpdateActionState(string ownerId, string actionId, string stateJson)
        {
            return registry != null && registry.UpdateActionState(ownerId, actionId, stateJson,
                Time.unscaledTime);
        }

        public void ClearContext(string ownerId, string contextId)
        {
            if (registry != null) registry.ClearContext(ownerId, contextId);
        }

        public bool UpdateTargetCueState(string ownerId, string actionId, string cueJson)
        {
            return registry != null && registry.UpdateTargetCueState(ownerId, actionId, cueJson,
                Time.unscaledTime);
        }

        public void UnregisterOwner(string ownerId)
        {
            if (registry != null) registry.UnregisterOwner(ownerId);
        }

        private void Update()
        {
            if (registry == null) return;
            registry.Update(Time.unscaledTime);
            activeControls.Update();
            UpdateSharedHintToggle();
            PublishStandardHintState();
            pauseNavigation?.Update(pauseMenu, FeatureModalActive());
            settings.Update();
            try { settingsView?.Update(); }
            catch (Exception e)
            {
                Logger.LogError("Settings view stopped after failure; action presentation remains active: " + e);
                var failedView = settingsView;
                settingsView = null;
                try { failedView?.Dispose(); }
                catch (Exception restore) { Logger.LogError("Settings view cleanup failed: " + restore); }
            }
            try { readableText?.Update(); }
            catch (Exception e)
            {
                Logger.LogError("Readable text module stopped after failure: " + e);
                try { readableText?.Stop(); }
                catch (Exception restore) { Logger.LogError("Readable text cleanup failed: " + restore); }
                readableText = null;
            }
            try { reticle?.Update(); }
            catch (Exception e)
            {
                Logger.LogError("Reticle module stopped after failure: " + e);
                try { reticle?.Stop(); }
                catch (Exception restore) { Logger.LogError("Reticle cleanup failed: " + restore); }
                reticle = null;
            }
            try { menuBindings?.Update(); }
            catch (Exception e)
            {
                Logger.LogError("Menu bindings module stopped after failure: " + e);
                try { menuBindings?.Stop(); }
                catch (Exception restore) { Logger.LogError("Menu bindings cleanup failed: " + restore); }
                menuBindings = null;
            }
            try { controlsTable?.Update(); }
            catch (Exception e)
            {
                Logger.LogError("Controls table module stopped after failure: " + e);
                try { controlsTable?.Stop(); }
                catch (Exception restore) { Logger.LogError("Controls table cleanup failed: " + restore); }
                controlsTable = null;
            }
        }

        private void LateUpdate()
        {
            if (reticle == null) return;
            if (pauseMenu == null && SceneManager.GetActiveScene().name == "VillageScene")
                pauseMenu = FindObjectOfType<PauseMenuHandler>();
            reticle.UpdatePauseVisibility(pauseMenu);
        }

        private void OnGUI()
        {
            if (registry == null) return;
            string context = CurrentContext();
            titleHints.Draw();
            pauseHints.Draw(pauseMenu, FeatureModalActive(), settingsView);
            hints.Draw(context, showSharedHints == null || showSharedHints.Value);
            targetCues.Draw(context);
        }

        private void OnDisable()
        {
            readableText?.Stop();
            reticle?.Stop();
            menuBindings?.Stop();
            controlsTable?.Stop();
            pauseNavigation?.Dispose(); pauseNavigation = null;
            settingsView?.Dispose();
            settingsView = null;
            settings?.Reset();
        }

        private void OnEnable()
        {
            if (registry != null && pauseNavigation == null) pauseNavigation = new PauseMenuNavigation();
            try { readableText?.Start(); }
            catch (Exception e) { Logger.LogError("Readable text reactivation failed: " + e); }
            try { controlsTable?.Start(); }
            catch (Exception e) { Logger.LogError("Controls table reactivation failed: " + e); }
            if (settings != null && settingsView == null)
            {
                try { settingsView = new SettingsPresentation(settings, Logger); }
                catch (Exception e) { Logger.LogError("Settings view reactivation failed: " + e); }
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            readableText?.OnSceneChanged();
            reticle?.OnSceneChanged();
            menuBindings?.OnSceneChanged();
            controlsTable?.OnSceneChanged();
            pauseMenu = null;
            settings.Reset();
            registry.BeginScene();
            standardHintSequence = 0;
            nextStandardHintState = 0f;
            RegisterStandardActions();
        }

        private void OnSceneUnloaded(Scene scene)
        {
            readableText?.OnSceneChanged();
            reticle?.OnSceneChanged();
            menuBindings?.OnSceneChanged();
            controlsTable?.OnSceneChanged();
            pauseMenu = null;
            settingsView?.Clear();
            settings.Reset();
            registry.BeginScene();
            standardHintSequence = 0;
            nextStandardHintState = 0f;
        }

        private bool OwnerAlive(string ownerId)
        {
            if (ownerId == PluginId) return this != null && isActiveAndEnabled;
            PluginInfo info;
            UnityChainloader chainloader = UnityChainloader.Instance;
            if (string.IsNullOrEmpty(ownerId) || chainloader == null ||
                !chainloader.Plugins.TryGetValue(ownerId, out info) || info == null) return false;
            BaseUnityPlugin plugin = info.Instance as BaseUnityPlugin;
            return plugin != null && plugin.isActiveAndEnabled;
        }

        private void RegisterStandardActions()
        {
            RegisterStandard("standard.look", "視点移動", 10,
                Bind("single", "<Mouse>/delta"), Bind("single", "<Gamepad>/rightStick"), "none");
            RegisterStandard("standard.move", "歩き移動", 20,
                Bind("directional", "<Keyboard>/w", "<Keyboard>/a", "<Keyboard>/s", "<Keyboard>/d"),
                Bind("single", "<Gamepad>/leftStick"), "none");
            RegisterStandard("standard.sprint", "走り移動", 30,
                Bind("single", "<Keyboard>/leftShift"), Bind("single", "<Gamepad>/leftStickPress"), "none");
            RegisterStandard("standard.interact", "アイテムを調べる / 決定", 40,
                Bindings(Bind("single", "<Keyboard>/e"), Bind("single", "<Keyboard>/enter"),
                    Bind("single", "<Keyboard>/numpadEnter")),
                Bind("single", "<Gamepad>/buttonSouth"), "native_next");
            RegisterStandard("standard.menu", "メニュー画面を開く", 50,
                Bind("single", "<Keyboard>/escape"), Bind("single", "<Gamepad>/start"), "hint");
            RegisterStandard("display.toggle_hints", "操作説明の表示切替", 80,
                Bind("single", "<Keyboard>/h"), Bind("single", "<Gamepad>/select"), "none");
        }

        private void UpdateSharedHintToggle()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.hKey.wasPressedThisFrame) ToggleSharedHints();

            Gamepad pad = Gamepad.current;
            if (pad == null)
            {
                selectCandidate = false;
                selectChorded = false;
                return;
            }
            if (pad.selectButton.wasPressedThisFrame)
            {
                selectCandidate = true;
                selectChorded = pad.leftShoulder.isPressed || pad.rightShoulder.isPressed;
            }
            if (selectCandidate && pad.selectButton.isPressed &&
                (pad.leftShoulder.isPressed || pad.rightShoulder.isPressed))
                selectChorded = true;
            if (selectCandidate && pad.selectButton.wasReleasedThisFrame)
            {
                if (!selectChorded) ToggleSharedHints();
                selectCandidate = false;
                selectChorded = false;
            }
        }

        private void ToggleSharedHints()
        {
            if (showSharedHints == null) return;
            showSharedHints.Value = !showSharedHints.Value;
            Logger.LogInfo("Shared gameplay hints " + (showSharedHints.Value ? "shown." : "hidden."));
        }

        private void PublishStandardHintState()
        {
            if (SceneManager.GetActiveScene().name != "VillageScene" ||
                Time.unscaledTime < nextStandardHintState) return;
            nextStandardHintState = Time.unscaledTime + 0.25f;
            string json = "{\"schema\":2,\"instanceId\":\"" + registry.InstanceId +
                "\",\"generation\":" + registry.Generation + ",\"sceneEpoch\":" + registry.SceneEpoch +
                ",\"sequence\":" + standardHintSequence++ + ",\"contextId\":\"village\"," +
                "\"visible\":true,\"enabled\":true,\"alpha\":1}";
            if (!registry.UpdateActionState(PluginId, "standard.menu", json, Time.unscaledTime))
                Logger.LogError("Could not publish the built-in menu hint.");
        }

        private void RegisterStandard(string actionId, string label, int order, string keyboard,
            string gamepad, string presentation)
        {
            var json = new StringBuilder();
            json.Append("{\"schema\":2,\"instanceId\":\"").Append(registry.InstanceId)
                .Append("\",\"generation\":").Append(registry.Generation)
                .Append(",\"label\":\"").Append(label).Append("\",\"order\":").Append(order)
                .Append(",\"keyboardMouse\":").Append(keyboard)
                .Append(",\"gamepad\":").Append(gamepad)
                .Append(",\"holdSeconds\":0,\"contexts\":[\"title\",\"village\",\"novel_opening\",\"novel_ending\",\"conversation\",\"text_event\"],")
                .Append("\"tableContexts\":[\"pause\",\"loading\"],\"presentation\":\"")
                .Append(presentation).Append("\"}");
            if (!registry.RegisterAction(PluginId, actionId, json.ToString()))
                Logger.LogError("Could not register built-in action " + actionId + ".");
        }

        private static string Bind(string mode, params string[] controls)
        {
            var json = new StringBuilder("[{\"mode\":\"").Append(mode).Append("\",\"controls\":[");
            for (int i = 0; i < controls.Length; i++)
            {
                if (i > 0) json.Append(',');
                json.Append('\"').Append(controls[i]).Append('\"');
            }
            return json.Append("]}]").ToString();
        }

        private static string Bindings(params string[] singleBindingArrays)
        {
            var result = new StringBuilder("[");
            for (int i = 0; i < singleBindingArrays.Length; i++)
            {
                if (i > 0) result.Append(',');
                string item = singleBindingArrays[i];
                result.Append(item.Substring(1, item.Length - 2));
            }
            return result.Append(']').ToString();
        }

        private bool FeatureModalActive()
        {
            var chain = UnityChainloader.Instance;
            if (chain != null && chain.Plugins.TryGetValue("local.disappearance.gameplayqol", out PluginInfo info))
            {
                var owner = info.Instance;
                if (owner?.GetType().GetMethod("GetActiveModalOwner")?.Invoke(owner, null) is string active && active != "None") return true;
            }
            return registry.HasVisibleContext("padlock") || registry.HasVisibleContext("checkpoint");
        }

        internal string CurrentContext()
        {
            switch (SceneManager.GetActiveScene().name)
            {
                case "TitleScene": return "title";
                case "OpeningTextScene": return "novel_opening";
                case "EndingTextScene": return "novel_ending";
                case "VillageScene":
                    // Feature UI owns its instructions immediately, including the
                    // frame before its action registry state has been published.
                    if (FeatureModalActive()) return null;
                    if (pauseMenu == null) pauseMenu = FindObjectOfType<PauseMenuHandler>();
                    if (pauseMenu != null && pauseMenu.CurrentPauseStatus == PauseMenuHandler.PauseStatus.Paused)
                        return null;
                    if (registry.HasVisibleContext("padlock")) return "padlock";
                    if (registry.HasVisibleContext("checkpoint")) return "checkpoint";
                    if (registry.HasVisibleContext("conversation")) return "conversation";
                    if (registry.HasVisibleContext("text_event")) return "text_event";
                    return "village";
                default: return "text_event";
            }
        }

        private void OnDestroy()
        {
            pauseNavigation?.Dispose(); pauseNavigation = null;
            readableText?.Stop();
            reticle?.Stop();
            menuBindings?.Stop();
            controlsTable?.Stop();
            settingsView?.Dispose();
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            if (registry != null) registry.UnregisterOwner(PluginId);
            ControlHintGUI.Dispose();
            ControlGlyphs.DisposeSprites();
        }
    }
}
