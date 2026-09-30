using System;
using System.IO;
using System.Collections;
using InstantHorror.Scripts.InteractionSystems;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using InstantHorror.Scripts;
using InstantHorror.Scripts.Data;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Disappearance.Shared;
using GamePlayerInput = InstantHorror.Scripts.PlayerInput;
using InstantHorror.Scripts.GameEventSystems.FadeInOut;

namespace Disappearance.GameplayQoL
{
    internal sealed class CheckpointModule : MonoBehaviour
    {
        private const string VillageScene = "VillageScene";
        private static readonly PropertyInfo GameStateProperty = typeof(GamePlayerInput).GetProperty(
            "GameStateHandler", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo PauseUiField = typeof(PauseMenuHandler).GetField(
            "pauseMenuUIHandler", BindingFlags.Instance | BindingFlags.NonPublic);
        private BepInEx.Logging.ManualLogSource Logger => GameplayQoLPlugin.Log;
        private static readonly string[] StageNames = CheckpointStages.Names;
        private readonly CrewCheckpoint crew = new CrewCheckpoint();
        private bool menuOpen;
        internal bool IsOpen => menuOpen;
        internal bool DestinationHints => menuOpen && !unlockConfirmation && !debugMenu;
        private bool debugMenu;
        private bool unlockConfirmation;
        private bool draftMosaic;
        private PauseMenuHandler pauseOwner;
        private PauseMenuUIHandler pauseUi;
        private bool reloading;
        private int selectedStage;
        private int openedFrame;
        private int stickDirection;
        private float nextStickMove;
        private float previousTimeScale;
        private UnityEngine.InputSystem.PlayerInput playerInput;
        private InputActionMap playerMap;
        private InputActionMap pauseMap;
        private bool playerMapWasEnabled;
        private bool pauseMapWasEnabled;
        private ModalSession modal;
        private int modalToken;
        private int capturedScene;
        private readonly NativeMenuCanvas presentation = new NativeMenuCanvas(message => GameplayQoLPlugin.Log.LogInfo(message));
        private GUIStyle titleStyle;
        private GUIStyle rowStyle;
        private GUIStyle helpStyle;
        private readonly ActiveControlHints controlHints = new ActiveControlHints();
        private static CheckpointModule instance;
        private Harmony harmony;
        private CheckpointProgressStore progressStore;
        private bool observedSession;
        private int observedStage;
        private bool pendingResume;
        private bool pendingSaveArrival = true;
        private int pendingStage;
        private bool pendingWithMosaic;
        private int crewRestoreScene = -1;

        internal CheckpointProgress Progress => progressStore.Progress;

        private void Awake()
        {
            instance = this;
            progressStore = new CheckpointProgressStore(
                Path.Combine(Paths.ConfigPath, "local.disappearance.gameplayqol.checkpoints.json"),
                message => Logger.LogWarning(message));
            SceneManager.sceneLoaded += OnSceneLoaded;
            harmony = new Harmony(GameplayQoLPlugin.PluginId + ".checkpoint");
            try
            {
                harmony.Patch(AccessTools.Method(typeof(GamePlayerInput), "OnPause"),
                    prefix: new HarmonyMethod(typeof(CheckpointModule), nameof(BeforeGamePause)));
                harmony.Patch(AccessTools.Method(typeof(PlayerSpawnHandler), "SpawnPlayer"),
                    prefix: new HarmonyMethod(typeof(CheckpointModule), nameof(BeforeSpawnPlayer)));
                harmony.Patch(AccessTools.Method(typeof(TextEventHandler), "Show"),
                    prefix: new HarmonyMethod(typeof(CheckpointModule), nameof(BeforeTextEventShow)));
                harmony.Patch(AccessTools.Method(typeof(FadeInOutUIHandler), "FadeInGameStart"),
                    prefix: new HarmonyMethod(typeof(CheckpointModule), nameof(BeforeGameStartFade)));
                harmony.Patch(AccessTools.Method(typeof(living_birds.scripts.Crow), "OnTriggerEnter"),
                    prefix: new HarmonyMethod(typeof(CheckpointModule), nameof(BeforeCrowTrigger)));
                harmony.Patch(AccessTools.Method(typeof(GamePlayerInput), "Start"),
                    postfix: new HarmonyMethod(typeof(CheckpointModule), nameof(ProtectCrewRestoreInput)));
                harmony.Patch(AccessTools.Method(typeof(GamePlayerInput), "Update"),
                    prefix: new HarmonyMethod(typeof(CheckpointModule), nameof(ProtectCrewRestoreInput)));
                Logger.LogInfo("Checkpoint progress and debug selector ready: Ctrl+F2 or LB+RB+View/Select.");
            }
            catch (Exception exception)
            {
                harmony.UnpatchSelf();
                harmony = null;
                Logger.LogError("Could not patch pause input: " + exception);
            }
        }

        private static bool BeforeGamePause()
        {
            if (instance == null || SceneManager.GetActiveScene().name != VillageScene)
                return true;
            if (instance.menuOpen)
                return false;
            return !CheckpointShortcut.PadChordHeld(Gamepad.current);
        }

        private static bool BeforeSpawnPlayer(PlayerSpawnHandler __instance)
        {
            if (instance == null || !instance.pendingResume || __instance == null) return true;
            int stage = instance.pendingStage;
            __instance.isPassedHole = CheckpointStages.Rank(stage) >= CheckpointStages.Rank(1);
            __instance.isPassedForest = CheckpointStages.Rank(stage) >= CheckpointStages.Rank(2);
            __instance.isPassedTunnel = CheckpointStages.Rank(stage) >= CheckpointStages.Rank(3);
            PlayerPrefsHandler.SetWithMosaic(instance.pendingWithMosaic);
            PlayerPrefs.Save();
            instance.Logger.LogInfo("Applying pending checkpoint before native spawn: " + StageNames[stage]);
            if (stage != CheckpointStages.CrewDeparted) return true;
            instance.StartCoroutine(instance.ResumeAfterCrew(__instance));
            return false;
        }

        private static bool BeforeGameStartFade(FadeInOutUIHandler __instance)
        {
            if (instance == null || __instance.gameObject.scene.name != VillageScene) return true;
            // Awake precedes sceneLoaded. A death reload still has the previous observed
            // destination here; an explicit new-game/other destination takes precedence.
            bool restoringCrew = instance.pendingResume
                ? instance.pendingStage == CheckpointStages.CrewDeparted
                : instance.observedSession && instance.observedStage == CheckpointStages.CrewDeparted;
            if (!restoringCrew) return true;
            var image = AccessTools.Field(typeof(FadeInOutUIHandler), "fadeImage").GetValue(__instance) as UnityEngine.UI.Image;
            if (image == null) return true;
            image.color = new Color(0f, 0f, 0f, 1f);
            instance.crewRestoreScene = __instance.gameObject.scene.handle;
            // CrewCheckpoint owns the single FadeIn after reconstruction. Do not start
            // the native delayed task, which would reset alpha to black two seconds later.
            return false;
        }

        private static bool BeforeCrowTrigger(living_birds.scripts.Crow __instance, Collider other)
        {
            // Ignore only loading-time Player contacts in this restored scene. Normal
            // walking, new games, every other destination and other scenes remain native.
            return instance == null || __instance.gameObject.scene.handle != instance.crewRestoreScene ||
                PlayerSpawnHandler.Instance == null || PlayerSpawnHandler.Instance.CanMove ||
                other == null || !other.CompareTag("Player");
        }

        private static void ProtectCrewRestoreInput(GamePlayerInput __instance)
        {
            if (instance == null || __instance.gameObject.scene.handle != instance.crewRestoreScene) return;
            var state = GameStateProperty.GetValue(__instance) as GameStateHandler;
            if (state != null && state.CurrentState.Value != GameState.OnEvent) state.ChangeState(GameState.OnEvent);
        }

        internal static void FinishCrewRestore(int scene)
        {
            if (instance != null && instance.crewRestoreScene == scene) instance.crewRestoreScene = -1;
        }

        private static void BeforeTextEventShow(TextEvent textEvent, ref Action onCompleted)
        {
            if (instance == null || SceneManager.GetActiveScene().name != VillageScene ||
                !instance.crew.IsDepartureText(textEvent)) return;
            CheckpointModule owner = instance;
            int scene = SceneManager.GetActiveScene().handle;
            Action original = onCompleted;
            onCompleted = () =>
            {
                original?.Invoke();
                if (owner != null && SceneManager.GetActiveScene().handle == scene)
                    owner.crew.Complete();
            };
        }

        private IEnumerator ResumeAfterCrew(PlayerSpawnHandler spawn)
        {
            int scene = SceneManager.GetActiveScene().handle;
            spawn.SetCanMove(false);
            // Let native Start methods initialize the door, text UI and player.
            for (int frame = 0; frame < 10; frame++) yield return null;
            if (SceneManager.GetActiveScene().handle != scene) yield break;
            if (!crew.Restore(spawn))
            {
                Logger.LogError("Crew checkpoint restore unavailable; falling back to native start without saving the failed destination.");
                pendingResume = false;
                observedSession = true;
                observedStage = 0;
                FinishCrewRestore(scene);
                spawn.SpawnPlayer();
                // The native startup fade was suppressed in Awake; release black on fallback.
                var fade = UnityEngine.Object.FindObjectOfType<FadeInOutUIHandler>();
                if (fade != null) fade.FadeIn();
            }
        }

        internal static bool HideCrewGateMarker(InteractableItemBase owner) =>
            instance != null && instance.crew.HidesMarker(owner);

        internal bool PrepareResume(int stage, bool withMosaic, bool persistSelection)
        {
            if (stage < 0 || stage >= StageNames.Length) return false;
            if (persistSelection && !progressStore.Progress.IsUnlocked(stage)) return false;
            pendingResume = true;
            pendingSaveArrival = true;
            pendingStage = stage;
            pendingWithMosaic = withMosaic;
            PlayerPrefsHandler.SetWithMosaic(withMosaic);
            PlayerPrefs.Save();
            return true;
        }

        internal void PrepareNewGame(bool withMosaic)
        {
            pendingResume = true;
            pendingSaveArrival = true;
            pendingStage = 0;
            pendingWithMosaic = withMosaic;
            PlayerPrefsHandler.SetWithMosaic(withMosaic);
            PlayerPrefs.Save();
        }

        internal bool UnlockAllPoints()
        {
            bool changed = progressStore.UnlockAll();
            if (changed) Logger.LogInfo("All checkpoint destinations were permanently unlocked.");
            return changed;
        }

        private void Update()
        {
            if (reloading || SceneManager.GetActiveScene().name != VillageScene)
                return;

            ObserveCheckpointArrival();

            Keyboard keyboard = Keyboard.current;
            Gamepad pad = Gamepad.current;
            controlHints.Update();
            bool openKey = CheckpointShortcut.KeyboardChordCompletedThisFrame(keyboard);
            bool openPad = CheckpointShortcut.PadChordCompletedThisFrame(pad);
            if (!menuOpen)
            {
                if (openKey || openPad)
                    OpenDebugMenu();
                return;
            }
            if (Time.frameCount == openedFrame)
                return;
            if (openKey || openPad ||
                keyboard != null && (keyboard.escapeKey.wasPressedThisFrame || keyboard.backspaceKey.wasPressedThisFrame) ||
                pad != null && pad.buttonEast.wasPressedThisFrame)
            {
                if (unlockConfirmation) { unlockConfirmation = false; selectedStage = 0; return; }
                CloseMenu();
                return;
            }

            int direction = 0;
            if (keyboard != null && (keyboard.upArrowKey.wasPressedThisFrame || keyboard.wKey.wasPressedThisFrame) ||
                pad != null && pad.dpad.up.wasPressedThisFrame)
                direction = -1;
            else if (keyboard != null && (keyboard.downArrowKey.wasPressedThisFrame || keyboard.sKey.wasPressedThisFrame) ||
                pad != null && pad.dpad.down.wasPressedThisFrame)
                direction = 1;
            if (direction == 0 && pad != null)
            {
                float y = pad.leftStick.ReadValue().y;
                int current = y > 0.55f ? -1 : y < -0.55f ? 1 : 0;
                if (current != 0 && (current != stickDirection || Time.unscaledTime >= nextStickMove))
                {
                    direction = current;
                    nextStickMove = Time.unscaledTime + (current == stickDirection ? 0.18f : 0.34f);
                }
                stickDirection = current;
            }
            if (direction != 0)
            {
                if (!debugMenu && !unlockConfirmation)
                    selectedStage = CheckpointChoices.Move(progressStore.Progress, selectedStage, direction);
                else
                {
                    int count = unlockConfirmation ? 2 : StageNames.Length;
                    selectedStage = debugMenu
                        ? CheckpointStages.Order[(Array.IndexOf(CheckpointStages.Order, selectedStage) + direction + count) % count]
                        : (selectedStage + direction + count) % count;
                }
            }

            if (!debugMenu && !unlockConfirmation && UnlockShortcut.Completed(keyboard, pad))
            { unlockConfirmation = true; selectedStage = 1; openedFrame = Time.frameCount; return; }
            if (!debugMenu && !unlockConfirmation)
            {
                bool left = keyboard != null && (keyboard.leftArrowKey.wasPressedThisFrame || keyboard.aKey.wasPressedThisFrame) ||
                    pad != null && pad.dpad.left.wasPressedThisFrame;
                bool right = keyboard != null && (keyboard.rightArrowKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame) ||
                    pad != null && pad.dpad.right.wasPressedThisFrame;
                if (left || right) draftMosaic = !draftMosaic;
            }

            bool confirm = ConfirmInput.WasPressedThisFrame(keyboard, pad);
            if (confirm)
            {
                if (debugMenu) RecallSelectedStage(false);
                else ConfirmNormalSelection();
            }
        }

        private void ObserveCheckpointArrival()
        {
            PlayerSpawnHandler spawn = FindObjectOfType<PlayerSpawnHandler>();
            if (spawn == null || !spawn.CanMove) return;
            int stage = CurrentStage(spawn);
            if (!observedSession)
            {
                observedSession = true;
                observedStage = stage;
                bool shouldSave = pendingResume && pendingSaveArrival || !progressStore.Progress.HasResume;
                if (shouldSave && progressStore.RecordArrival(stage,
                    pendingResume ? pendingWithMosaic : PlayerPrefsHandler.GetWithMosaic()))
                    Logger.LogInfo("Checkpoint arrival saved: " + StageNames[stage]);
                pendingResume = false;
                return;
            }
            if (stage == observedStage) return;
            int previous = observedStage;
            observedStage = stage;
            if (CheckpointStages.Rank(stage) > CheckpointStages.Rank(previous) && progressStore.RecordArrival(stage, PlayerPrefsHandler.GetWithMosaic()))
                Logger.LogInfo("Checkpoint arrival saved: " + StageNames[stage]);
        }

        private void OpenDebugMenu()
        {
            if (menuOpen || modalToken != 0 || GameplayQoLPlugin.Instance == null || GameplayQoLPlugin.Instance.Modal.IsOccupied)
                return;
            PauseMenuHandler pauseMenu = FindObjectOfType<PauseMenuHandler>();
            if (pauseMenu != null && pauseMenu.CurrentPauseStatus == PauseMenuHandler.PauseStatus.Paused)
            {
                pauseMenu.Resume();
                if (pauseMenu.CurrentPauseStatus != PauseMenuHandler.PauseStatus.UnPaused)
                    return;
            }
            PlayerSpawnHandler spawn = FindObjectOfType<PlayerSpawnHandler>();
            GamePlayerInput gameInput = FindObjectOfType<GamePlayerInput>();
            GameStateHandler gameState = gameInput == null || GameStateProperty == null ? null :
                GameStateProperty.GetValue(gameInput) as GameStateHandler;
            if (spawn == null || !spawn.CanMove || gameState == null ||
                gameState.CurrentState.Value != GameState.PlayGame)
            {
                Logger.LogWarning("Debug checkpoint selector unavailable in the current game state.");
                return;
            }
            int sceneHandle = SceneManager.GetActiveScene().handle;
            ModalSession session = GameplayQoLPlugin.Instance.Modal;
            if (!session.TryAcquire(ModalOwner.Checkpoint, sceneHandle, out int token))
            {
                Logger.LogWarning("Checkpoint modal unavailable: sceneHandle=" + sceneHandle +
                    " owner=" + session.Owner);
                return;
            }
            modal = session;
            modalToken = token;
            MenuInputIsolation.Transition();
            capturedScene = sceneHandle;
            try
            {
                debugMenu = true;
                unlockConfirmation = false;
                pauseOwner = null;
                pauseUi = null;
                selectedStage = CurrentStage(spawn);
                previousTimeScale = Time.timeScale;
                playerInput = FindObjectOfType<UnityEngine.InputSystem.PlayerInput>();
                playerMap = playerInput == null || playerInput.actions == null ? null :
                    playerInput.actions.FindActionMap("Player", false);
                pauseMap = playerInput == null || playerInput.actions == null ? null :
                    playerInput.actions.FindActionMap("Pause", false);
                playerMapWasEnabled = playerMap != null && playerMap.enabled;
                pauseMapWasEnabled = pauseMap != null && pauseMap.enabled;
                playerMap?.Disable();
                pauseMap?.Disable();
                Time.timeScale = 0f;
                menuOpen = true;
                openedFrame = Time.frameCount;
                stickDirection = 0;
                nextStickMove = 0f;
                modal.Activate(ModalOwner.Checkpoint, token);
            }
            catch (Exception exception)
            {
                Logger.LogError("Checkpoint selector could not open: " + exception);
                CloseMenu();
            }
        }

        private int CurrentStage(PlayerSpawnHandler spawn)
        {
            return spawn.isPassedTunnel ? 3 : spawn.isPassedForest ? 2 : spawn.isPassedHole ? 1 : crew.Completed ? CheckpointStages.CrewDeparted : 0;
        }

        internal bool OpenNormalFromPause(PauseMenuHandler pauseMenu)
        {
            if (menuOpen || modalToken != 0 || pauseMenu == null ||
                pauseMenu.CurrentPauseStatus != PauseMenuHandler.PauseStatus.Paused ||
                GameplayQoLPlugin.Instance == null) return false;
            PlayerSpawnHandler spawn = FindObjectOfType<PlayerSpawnHandler>();
            if (spawn == null || !spawn.CanMove) return false;
            int sceneHandle = SceneManager.GetActiveScene().handle;
            ModalSession session = GameplayQoLPlugin.Instance.Modal;
            if (!session.TryAcquire(ModalOwner.Checkpoint, sceneHandle, out int token)) return false;
            modal = session;
            modalToken = token;
            MenuInputIsolation.Transition();
            capturedScene = sceneHandle;
            try
            {
                debugMenu = false;
                unlockConfirmation = false;
                pauseOwner = pauseMenu;
                pauseUi = PauseUiField?.GetValue(pauseMenu) as PauseMenuUIHandler;
                pauseUi?.Close();
                selectedStage = progressStore.Progress.HasResume ? progressStore.Progress.ResumeStage : CurrentStage(spawn);
                draftMosaic = progressStore.Progress.HasResume
                    ? progressStore.Progress.ResumeWithMosaic : PlayerPrefsHandler.GetWithMosaic();
                previousTimeScale = Time.timeScale;
                playerInput = FindObjectOfType<UnityEngine.InputSystem.PlayerInput>();
                playerMap = playerInput?.actions?.FindActionMap("Player", false);
                pauseMap = playerInput?.actions?.FindActionMap("Pause", false);
                playerMapWasEnabled = playerMap != null && playerMap.enabled;
                pauseMapWasEnabled = pauseMap != null && pauseMap.enabled;
                playerMap?.Disable();
                pauseMap?.Disable();
                Time.timeScale = 0f;
                menuOpen = true;
                openedFrame = Time.frameCount;
                stickDirection = 0;
                nextStickMove = 0f;
                modal.Activate(ModalOwner.Checkpoint, token);
                return true;
            }
            catch (Exception exception)
            {
                Logger.LogError("Normal checkpoint selector could not open: " + exception);
                CloseMenu();
                return false;
            }
        }

        private void CloseMenu()
        {
            if (modalToken == 0) return;
            int token = modalToken;
            MenuInputIsolation.Transition();
            modal?.BeginClosing(ModalOwner.Checkpoint, token);
            menuOpen = false;
            presentation.Hide();
            try
            {
                if (SceneManager.GetActiveScene().handle == capturedScene)
                {
                    Time.timeScale = previousTimeScale;
                    try { RestoreMap(playerMap, playerMapWasEnabled); }
                    catch (Exception exception) { Logger?.LogError("Could not restore Player input map: " + exception); }
                    try { RestoreMap(pauseMap, pauseMapWasEnabled); }
                    catch (Exception exception) { Logger?.LogError("Could not restore Pause input map: " + exception); }
                    if (pauseOwner != null && pauseOwner.CurrentPauseStatus == PauseMenuHandler.PauseStatus.Paused)
                        pauseUi?.Open();
                }
                else if (Time.timeScale == 0f) Time.timeScale = 1f;
            }
            finally
            {
                playerInput = null;
                playerMap = null;
                pauseMap = null;
                modalToken = 0;
                capturedScene = 0;
                modal?.Release(ModalOwner.Checkpoint, token);
                modal = null;
                pauseOwner = null;
                pauseUi = null;
            }
        }

        private void ConfirmNormalSelection()
        {
            if (unlockConfirmation)
            {
                if (selectedStage == 0) UnlockAllPoints();
                unlockConfirmation = false;
                selectedStage = 0;
                return;
            }
            if (!progressStore.Progress.IsUnlocked(selectedStage)) return;
            if (!PrepareResume(selectedStage, draftMosaic, true)) return;
            RecallSelectedStage(true);
        }

        private static void RestoreMap(InputActionMap map, bool wasEnabled)
        {
            if (map == null) return;
            if (wasEnabled) map.Enable();
            else map.Disable();
        }

        private void RecallSelectedStage(bool normalSelection)
        {
            PlayerSpawnHandler spawn = FindObjectOfType<PlayerSpawnHandler>();
            if (spawn == null)
            {
                Logger.LogWarning("Checkpoint selection cancelled: PlayerSpawnHandler was not found.");
                CloseMenu();
                return;
            }
            bool hole = spawn.isPassedHole;
            bool forest = spawn.isPassedForest;
            bool tunnel = spawn.isPassedTunnel;
            int target = selectedStage;
            Logger.LogInfo((normalSelection ? "Loading selected checkpoint: " : "Debug-loading checkpoint: ") + StageNames[target]);
            spawn.isPassedHole = CheckpointStages.Rank(target) >= CheckpointStages.Rank(1);
            spawn.isPassedForest = CheckpointStages.Rank(target) >= CheckpointStages.Rank(2);
            spawn.isPassedTunnel = CheckpointStages.Rank(target) >= CheckpointStages.Rank(3);
            if (!normalSelection)
            {
                PrepareResume(target, PlayerPrefsHandler.GetWithMosaic(), false);
                pendingSaveArrival = false;
            }
            reloading = true;
            try
            {
                CloseMenu();
                Time.timeScale = 1f;
                // The enemy death path reloads this scene; the new PlayerSpawnExecutor
                // invokes the persistent PlayerSpawnHandler and its native stage events.
                SceneManager.LoadScene(VillageScene);
            }
            catch (Exception exception)
            {
                spawn.isPassedHole = hole;
                spawn.isPassedForest = forest;
                spawn.isPassedTunnel = tunnel;
                reloading = false;
                Logger.LogError("Selected checkpoint could not be loaded: " + exception);
                if (SceneManager.GetActiveScene().name == VillageScene && debugMenu)
                {
                    OpenDebugMenu();
                    if (menuOpen) selectedStage = target;
                }
            }
        }

        private void OnGUI()
        {
            if (!menuOpen || Event.current.type != EventType.Repaint)
                return;
            Transform menuSource = pauseUi == null ? null : pauseUi.transform;
            if (menuSource == null)
                foreach (PauseMenuUIHandler candidate in Resources.FindObjectsOfTypeAll<PauseMenuUIHandler>())
                    if (candidate != null && candidate.gameObject.scene == SceneManager.GetActiveScene())
                    { menuSource = candidate.transform; break; }
            // Pause child screens use the same crisp Overlay presentation as settings.
            presentation.Begin(menuSource, false);
            presentation.Background(new Color(0, 0, 0, .88f));
            float scale = Mathf.Min(Screen.width / 1920f, Screen.height / 1080f);
            float width = 900f * scale;
            float height = (!debugMenu && !unlockConfirmation ? 650f : 560f) * scale;
            float x = (Screen.width - width) * 0.5f;
            float y = (Screen.height - height) * 0.5f;
            if (titleStyle == null || titleStyle.fontSize != Mathf.RoundToInt(36f * scale))
            {
                titleStyle = new GUIStyle(GUI.skin.label) {
                    fontSize = Mathf.RoundToInt(36f * scale), fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
                rowStyle = new GUIStyle(GUI.skin.label) {
                    fontSize = Mathf.RoundToInt(32f * scale), alignment = TextAnchor.MiddleLeft
                };
                helpStyle = new GUIStyle(GUI.skin.label) {
                    fontSize = Mathf.RoundToInt(22f * scale), alignment = TextAnchor.MiddleLeft
                };
                JapaneseGuiFont.Apply(titleStyle);
                JapaneseGuiFont.Apply(rowStyle);
                JapaneseGuiFont.Apply(helpStyle);
                titleStyle.normal.textColor = rowStyle.normal.textColor =
                    helpStyle.normal.textColor = Color.white;
            }
            Color previousColor = GUI.color;

            GUI.color = Color.white;
            string heading = unlockConfirmation
                ? "全ての再開地点を解放します。\n<color=#FF0000>解放状態は元に戻せません。</color>"
                : debugMenu ? "復帰ポイントを選択（デバッグ）" : "再開地点を選ぶ";
            presentation.Label(new Rect(x + 24f * scale, y + 18f * scale,
                width - 48f * scale, unlockConfirmation ? 100f * scale : 60f * scale), heading, titleStyle, Color.white);
            if (unlockConfirmation)
            {
                string[] confirms = { "解放する", "キャンセル" };
                for (int i = 0; i < confirms.Length; i++)
                {
                    Rect confirmRow = new Rect(x + 50f * scale, y + (160f + i * 82f) * scale,
                        width - 100f * scale, 68f * scale);

                    presentation.Label(new Rect(confirmRow.x + 20f * scale, confirmRow.y,
                        confirmRow.width - 40f * scale, confirmRow.height),
                        (i == selectedStage ? "<color=#FF0000>▶</color>  " : "    ") + confirms[i], rowStyle, Color.white);
                }
                presentation.End();
                GUI.color = previousColor;
                return;
            }
            if (!debugMenu)
                presentation.Label(new Rect(x + 50f * scale, y + 76f * scale, width - 100f * scale, 44f * scale),
                    "モード：" + (draftMosaic ? "配信者モード" : "通常モード"), helpStyle, Color.white);
            PlayerSpawnHandler spawn = FindObjectOfType<PlayerSpawnHandler>();
            int current = spawn == null ? -1 : CurrentStage(spawn);
            int[] stages = debugMenu ? CheckpointStages.Order : CheckpointChoices.Visible(progressStore.Progress);
            int rows = stages.Length;
            float start = debugMenu ? 108f : 126f;
            float spacing = debugMenu ? 82f : 72f;
            for (int i = 0; i < rows; i++)
            {
                Rect row = new Rect(x + 50f * scale, y + (start + i * spacing) * scale,
                    width - 100f * scale, 62f * scale);

                int stage = stages[i];
                GUI.color = Color.white;
                string label = StageNames[stage];
                presentation.Label(new Rect(row.x + 20f * scale, row.y, row.width - 40f * scale, row.height),
                    (stage == selectedStage ? "<color=#FF0000>▶</color>  " : "    ") + label +
                    (debugMenu && stage == current ? "   （現在）" : ""), rowStyle, GUI.color);
            }
            GUI.color = Color.white;
            if (GetComponent<PresentationClient>()?.Ready != true) DrawHelp(x, y, scale);
            presentation.End();
            GUI.color = previousColor;
        }

        private void DrawHelp(float x, float y, float scale)
        {
            using var fade = new GameFadeGUI();
            bool pad = controlHints.GamepadActive && Gamepad.current != null;
            float top = y + (debugMenu ? 448f : 538f) * scale;
            float bottom = y + (debugMenu ? 500f : 590f) * scale;
            float size = 42f * scale;
            if (pad)
            {
                ControlHintGUI.Stick(new Rect(x + 46f * scale, top, size, size), "L");
                GUI.Label(new Rect(x + 95f * scale, top, 22f * scale, size), "/", helpStyle);
                ControlHintGUI.Dpad(new Rect(x + 124f * scale, top, size, size));
                GUI.Label(new Rect(x + 178f * scale, top, 120f * scale, size), "選択", helpStyle);
                ControlHintGUI.Button(new Rect(x + 46f * scale, bottom, size, size),
                    ActiveControlHints.SouthButton(Gamepad.current), true);
                GUI.Label(new Rect(x + 100f * scale, bottom, 120f * scale, size), "決定", helpStyle);
                ControlHintGUI.Button(new Rect(x + 500f * scale, bottom, size, size),
                    ActiveControlHints.EastButton(Gamepad.current), true);
                GUI.Label(new Rect(x + 554f * scale, bottom, 120f * scale, size), "戻る", helpStyle);
            }
            else
            {
                ControlHintGUI.Button(new Rect(x + 46f * scale, top, size, size), "W", false);
                ControlHintGUI.Button(new Rect(x + 100f * scale, top, size, size), "S", false);
                GUI.Label(new Rect(x + 154f * scale, top, 120f * scale, size), "選択", helpStyle);
                ControlHintGUI.Button(new Rect(x + 46f * scale, bottom, size, size), "E", false);
                ControlHintGUI.Button(new Rect(x + 100f * scale, bottom, 90f * scale, size), "Enter", false);
                GUI.Label(new Rect(x + 202f * scale, bottom, 120f * scale, size), "決定", helpStyle);
                ControlHintGUI.Button(new Rect(x + 500f * scale, bottom, 70f * scale, size), "Esc", false);
                ControlHintGUI.Button(new Rect(x + 582f * scale, bottom, 126f * scale, size), "Backspace", false);
                GUI.Label(new Rect(x + 720f * scale, bottom, 120f * scale, size), "戻る", helpStyle);
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Native death reload has no spawn flag for this additional village point.
            if (scene.name == VillageScene && observedSession && observedStage == CheckpointStages.CrewDeparted && !pendingResume)
            {
                pendingResume = true;
                pendingStage = CheckpointStages.CrewDeparted;
                pendingWithMosaic = PlayerPrefsHandler.GetWithMosaic();
            }
            crew.Reset();
            CloseMenu();
            reloading = false;
            observedSession = false;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            CloseMenu();
            presentation.Dispose();
            harmony?.UnpatchSelf();
            harmony = null;
            if (instance == this) instance = null;
            ControlHintGUI.Dispose();
        }
    }
}

