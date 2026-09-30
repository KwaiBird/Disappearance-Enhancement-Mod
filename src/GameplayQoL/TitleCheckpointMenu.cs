using System;
using System.Reflection;
using HarmonyLib;
using InstantHorror.Scripts;
using InstantHorror.Scripts.Data;
using InstantHorror.Scripts.GameEventSystems.FadeInOut;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Disappearance.Shared;

namespace Disappearance.GameplayQoL
{
    internal sealed class TitleCheckpointMenu : MonoBehaviour
    {
        private enum View { Main, NewGameMode, Checkpoints, UnlockConfirm }
        private const int NewGameRow = 0, ContinueRow = 1, DestinationsRow = 2, QuitRow = 3;
        private static readonly string[] Stages = CheckpointStages.Names;
        private static readonly FieldInfo FadeField = AccessTools.Field(typeof(TitleSceneHandler), "fadeInOutUIHandler");
        private static readonly MethodInfo FadeOutMethod = AccessTools.Method(typeof(FadeInOutUIHandler),
            "FadeOut", new[] { typeof(Action) });
        private static TitleCheckpointMenu instance;
        private Harmony harmony;
        private TitleSceneHandler native;
        private GameObject nativeButtons;
        private GameObject nativeTitleImage;
        private bool nativeTitleImageWasActive;
        private View view;
        private int selected;
        private int selectionBeforeUnlock;
        private bool draftMosaic;
        private bool titleActive;
        private bool loading;
        private int openedFrame;
        private int stickDirection;
        private float nextStickMove;
        private GUIStyle titleStyle, rowStyle, helpStyle, modeStyle;
        private readonly NativeMenuCanvas presentation = new NativeMenuCanvas(message => GameplayQoLPlugin.Log.LogInfo(message));
        private readonly ActiveControlHints controls = new ActiveControlHints();

        private CheckpointModule Checkpoints => GameplayQoLPlugin.Instance?.Checkpoints;
        private int DefaultMainSelection => Checkpoints != null && Checkpoints.Progress.HasResume ? ContinueRow : NewGameRow;
        internal bool Active => titleActive && !loading;
        internal bool BackHint => Active && view != View.Main;
        internal bool DestinationHints => Active && view == View.Checkpoints;
        internal bool HasDestinations => Checkpoints != null && CheckpointChoices.Visible(Checkpoints.Progress).Length != 0;
        internal bool SelectionHints => Active && (!DestinationHints || HasDestinations);

        private void Awake()
        {
            instance = this;
            harmony = new Harmony(GameplayQoLPlugin.PluginId + ".title-checkpoints");
            try
            {
                var prefix = new HarmonyMethod(typeof(TitleCheckpointMenu), nameof(AllowNativeTitleInput));
                harmony.Patch(AccessTools.Method(typeof(TitleSceneHandler), "OnUp"), prefix: prefix);
                harmony.Patch(AccessTools.Method(typeof(TitleSceneHandler), "OnDown"), prefix: prefix);
                harmony.Patch(AccessTools.Method(typeof(TitleSceneHandler), "OnInteract"), prefix: prefix);
            }
            catch (Exception exception)
            {
                harmony.UnpatchSelf();
                harmony = null;
                GameplayQoLPlugin.Log.LogError("Title checkpoint menu input patch failed: " + exception);
            }
        }

        private static bool AllowNativeTitleInput()
        {
            return instance == null || !instance.titleActive;
        }

        private void Update()
        {
            bool inTitle = SceneManager.GetActiveScene().name == "TitleScene";
            if (!inTitle)
            {
                presentation.Hide();
                titleActive = false;
                native = null;
                nativeButtons = null;
                nativeTitleImage = null;
                loading = false;
                return;
            }
            controls.Update();
            if (!titleActive)
            {
                native = FindObjectOfType<TitleSceneHandler>();
                if (native == null) return;
                titleActive = true;
                view = View.Main;
                selected = DefaultMainSelection;
                draftMosaic = Checkpoints != null && Checkpoints.Progress.HasResume
                    ? Checkpoints.Progress.ResumeWithMosaic : PlayerPrefsHandler.GetWithMosaic();
                nativeButtons = GameObject.Find("Buttons");
                if (nativeButtons != null) nativeButtons.SetActive(false);
                nativeTitleImage = GameObject.Find("TitleImage");
                nativeTitleImageWasActive = nativeTitleImage != null && nativeTitleImage.activeSelf;
                openedFrame = Time.frameCount;
                GameplayQoLPlugin.Log.LogInfo("Checkpoint-aware title menu active.");
            }
            if (loading) { presentation.Hide(); return; }
            if (Time.frameCount == openedFrame) return;
            HandleInput();
        }

        private void HandleInput()
        {
            Keyboard keyboard = Keyboard.current;
            Gamepad pad = Gamepad.current;
            int direction = VerticalDirection(keyboard, pad);
            if (direction != 0) Move(direction);

            bool horizontalLeft = keyboard != null && (keyboard.leftArrowKey.wasPressedThisFrame || keyboard.aKey.wasPressedThisFrame) ||
                pad != null && pad.dpad.left.wasPressedThisFrame;
            bool horizontalRight = keyboard != null && (keyboard.rightArrowKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame) ||
                pad != null && pad.dpad.right.wasPressedThisFrame;
            if (view == View.Checkpoints && UnlockShortcut.Completed(keyboard, pad))
            { selectionBeforeUnlock = selected; view = View.UnlockConfirm; selected = 1; openedFrame = Time.frameCount; return; }
            if (view == View.Checkpoints && HasDestinations && (horizontalLeft || horizontalRight)) draftMosaic = !draftMosaic;

            bool cancel = keyboard != null && (keyboard.escapeKey.wasPressedThisFrame || keyboard.backspaceKey.wasPressedThisFrame) ||
                pad != null && pad.buttonEast.wasPressedThisFrame;
            if (cancel && view != View.Main)
            {
                bool confirmingUnlock = view == View.UnlockConfirm;
                view = confirmingUnlock ? View.Checkpoints : View.Main;
                selected = confirmingUnlock ? selectionBeforeUnlock : DefaultMainSelection;
                return;
            }
            if (ConfirmInput.WasPressedThisFrame(keyboard, pad)) Confirm();
        }

        private int VerticalDirection(Keyboard keyboard, Gamepad pad)
        {
            int direction = 0;
            if (keyboard != null && (keyboard.upArrowKey.wasPressedThisFrame || keyboard.wKey.wasPressedThisFrame) ||
                pad != null && pad.dpad.up.wasPressedThisFrame) direction = -1;
            else if (keyboard != null && (keyboard.downArrowKey.wasPressedThisFrame || keyboard.sKey.wasPressedThisFrame) ||
                pad != null && pad.dpad.down.wasPressedThisFrame) direction = 1;
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
            return direction;
        }

        private void Move(int direction)
        {
            if (view == View.Checkpoints)
            {
                selected = CheckpointChoices.Move(Checkpoints.Progress, selected, direction);
                return;
            }
            int count = view == View.Main ? 4 : 2;
            selected = (selected + direction + count) % count;
            if (view == View.Main && selected == ContinueRow && !Checkpoints.Progress.HasResume)
                selected = (selected + direction + count) % count;
        }

        private void Confirm()
        {
            CheckpointModule checkpoints = Checkpoints;
            if (checkpoints == null) return;
            if (view == View.Main)
            {
                switch (selected)
                {
                    case NewGameRow:
                        view = View.NewGameMode;
                        selected = 0;
                        return;
                    case ContinueRow:
                        if (!checkpoints.Progress.HasResume) return;
                        checkpoints.PrepareResume(checkpoints.Progress.ResumeStage,
                            checkpoints.Progress.ResumeWithMosaic, false);
                        FadeTo("VillageScene");
                        return;
                    case DestinationsRow:
                        view = View.Checkpoints;
                        selected = checkpoints.Progress.HasResume ? checkpoints.Progress.ResumeStage : 0;
                        draftMosaic = checkpoints.Progress.HasResume
                            ? checkpoints.Progress.ResumeWithMosaic : PlayerPrefsHandler.GetWithMosaic();
                        return;
                    case QuitRow:
                        Application.Quit();
                        return;
                }
            }
            if (view == View.NewGameMode)
            {
                draftMosaic = selected == 1;
                checkpoints.PrepareNewGame(draftMosaic);
                FadeTo("OpeningTextScene");
                return;
            }
            if (view == View.Checkpoints)
            {
                if (!checkpoints.Progress.IsUnlocked(selected)) return;
                if (!checkpoints.PrepareResume(selected, draftMosaic, true)) return;
                FadeTo("VillageScene");
                return;
            }
            if (view == View.UnlockConfirm)
            {
                if (selected == 0) checkpoints.UnlockAllPoints();
                view = View.Checkpoints;
                selected = selectionBeforeUnlock;
            }
        }

        private void FadeTo(string scene)
        {
            loading = true;
            FadeInOutUIHandler fade = native == null ? null : FadeField?.GetValue(native) as FadeInOutUIHandler;
            Action load = () => SceneManager.LoadScene(scene);
            if (fade != null && FadeOutMethod != null) FadeOutMethod.Invoke(fade, new object[] { load });
            else load();
        }

        private void OnGUI()
        {
            if (!titleActive || loading || Event.current.type != EventType.Repaint) return;
            // Every title view shares the same logo and lower menu area.
            if (nativeTitleImage != null)
                nativeTitleImage.SetActive(nativeTitleImageWasActive);
            float scale = Mathf.Min(Screen.width / 1920f, Screen.height / 1080f);
            float width = 860f * scale;
            float x = (Screen.width - width) * 0.5f;
            float y = (view == View.NewGameMode ? 650f : 622f) * scale;
            EnsureStyles(scale);
            Color previous = GUI.color;
            presentation.Begin(nativeButtons == null ? native.transform : nativeButtons.transform);
            presentation.Background(Color.clear);
            GUI.color = Color.white;
            if (view == View.Main) DrawMain(x, y, width, scale);
            else if (view == View.NewGameMode) DrawNewGame(x, y, width, scale);
            else if (view == View.Checkpoints) DrawCheckpoints(x, y, width, scale);
            else DrawUnlockConfirm(x, y, width, scale);
            presentation.End();
            GUI.color = previous;
        }

        private void DrawMain(float x, float y, float width, float scale)
        {
            DrawTitle(x, y, width, scale, "");
            CheckpointProgress progress = Checkpoints.Progress;
            string mode = progress.ResumeWithMosaic ? "配信者モード" : "通常モード";
            string[] labels = {
                "はじめから",
                progress.HasResume ? "続きから［" + mode + "］" : "続きから",
                "再開地点を選ぶ", "終了する"
            };
            DrawRows(x, y + 34f * scale, width, scale, labels, i => i != ContinueRow || progress.HasResume);
        }

        private void DrawNewGame(float x, float y, float width, float scale)
        {
            DrawTitle(x, y, width, scale, "モードを選択");
            DrawRows(x, y + 86f * scale, width, scale,
                new[] { "通常モード（モザイクなし）", "配信者モード（モザイクあり）" }, _ => true);
            if (!GameplayQoLPlugin.Instance.PresentationAvailable)
                DrawBackHelp(x, y + 362f * scale, scale);
        }

        private void DrawCheckpoints(float x, float y, float width, float scale)
        {
            DrawTitle(x, y, width, scale, "再開地点を選ぶ");
            int[] stages = CheckpointChoices.Visible(Checkpoints.Progress);
            if (stages.Length == 0)
                presentation.Label(new Rect(x + 30f * scale, y + 112f * scale, width - 60f * scale, 60f * scale),
                    "（選択できる再開地点はありません）", modeStyle, Color.white);
            else
            {
                presentation.Label(new Rect(x + 54f * scale, y + 76f * scale, width - 108f * scale, 44f * scale),
                    "モード：" + (draftMosaic ? "配信者モード" : "通常モード"), modeStyle, Color.white);
                var labels = new string[stages.Length];
                for (int i = 0; i < stages.Length; i++) labels[i] = Stages[stages[i]];
                DrawRows(x, y + 112f * scale, width, scale, labels,
                    _ => true, 56f, Array.IndexOf(stages, selected));
            }
            if (!GameplayQoLPlugin.Instance.PresentationAvailable)
            {
                DrawBackHelp(x, y + 360f * scale, scale);
                DrawUnlockHelp(x, y + 410f * scale, scale);
            }
        }

        private void DrawUnlockConfirm(float x, float y, float width, float scale)
        {
            DrawTitle(x, y, width, scale, "全再開地点を解放");
            presentation.Label(new Rect(x + 30f * scale, y + 66f * scale,
                width - 60f * scale, 90f * scale),
                "すべての再開地点を選べるようになります。\n<color=#FF0000>解放状態は元に戻せません。</color>", modeStyle, Color.white);
            DrawRows(x, y + 158f * scale, width, scale,
                new[] { "解放する", "キャンセル" }, _ => true);
        }

        private void DrawTitle(float x, float y, float width, float scale, string text)
        {
            presentation.Label(new Rect(x + 30f * scale, y + 18f * scale, width - 60f * scale, 92f * scale), text, titleStyle, Color.white);
        }

        private void DrawRows(float x, float y, float width, float scale, string[] labels,
            Func<int, bool> enabled, float spacing = 76f, int? selectedRow = null)
        {
            for (int i = 0; i < labels.Length; i++)
            {
                Rect row = new Rect(x + 52f * scale, y + i * spacing * scale,
                    width - 104f * scale, 60f * scale);
                bool active = enabled(i);
                GUI.color = active ? Color.white : new Color(0.48f, 0.48f, 0.48f, 1f);
                presentation.Label(new Rect(row.x + 18f * scale, row.y, row.width - 36f * scale, row.height),
                    (i == (selectedRow ?? selected) ? "<color=#FF0000>▶</color>  " : "    ") + labels[i], rowStyle, GUI.color);
            }
            GUI.color = Color.white;
        }

        private void DrawBackHelp(float x, float y, float scale)
        {
            using var fade = new GameFadeGUI();
            bool pad = controls.GamepadActive && Gamepad.current != null;
            float size = 42f * scale;
            float cursor = x + 54f * scale;
            if (pad)
            {
                ControlHintGUI.Button(new Rect(cursor, y, size, size),
                    ActiveControlHints.EastButton(Gamepad.current), true);
                cursor += 54f * scale;
            }
            else
            {
                ControlHintGUI.Button(new Rect(cursor, y, 70f * scale, size), "Esc", false);
                cursor += 78f * scale;
                GUI.Label(new Rect(cursor, y, 22f * scale, size), "/", helpStyle);
                cursor += 30f * scale;
                ControlHintGUI.Button(new Rect(cursor, y, 126f * scale, size), "Backspace", false);
                cursor += 134f * scale;
            }
            GUI.Label(new Rect(cursor, y, 120f * scale, size), "戻る", helpStyle);
        }

        private void DrawUnlockHelp(float x, float y, float scale)
        {
            using var fade = new GameFadeGUI();
            bool pad = controls.GamepadActive && Gamepad.current != null;
            float size = 42f * scale;
            float cursor = x + 54f * scale;
            string[] labels = pad
                ? new[] { "LB", "RB", ActiveControlHints.NorthButton(Gamepad.current) }
                : new[] { "Ctrl", "Shift", "U" };
            float[] widths = pad ? new[] { 42f, 42f, 42f } : new[] { 78f, 92f, 42f };
            for (int i = 0; i < labels.Length; i++)
            {
                ControlHintGUI.Button(new Rect(cursor, y, widths[i] * scale, size), labels[i], pad);
                cursor += widths[i] * scale;
                if (i + 1 < labels.Length)
                {
                    GUI.Label(new Rect(cursor, y, 28f * scale, size), "+", helpStyle);
                    cursor += 28f * scale;
                }
            }
            GUI.Label(new Rect(cursor + 8f * scale, y, 240f * scale, size), "全再開地点を解放", helpStyle);
        }

        private void EnsureStyles(float scale)
        {
            int size = Mathf.RoundToInt(48f * scale);
            if (rowStyle != null && rowStyle.fontSize == size) return;
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(34f * scale),
                fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            rowStyle = new GUIStyle(GUI.skin.label) { fontSize = size, alignment = TextAnchor.MiddleLeft };
            helpStyle = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(21f * scale), alignment = TextAnchor.MiddleLeft };
            modeStyle = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(23f * scale), alignment = TextAnchor.MiddleCenter };
            JapaneseGuiFont.Apply(titleStyle); JapaneseGuiFont.Apply(rowStyle);
            JapaneseGuiFont.Apply(helpStyle); JapaneseGuiFont.Apply(modeStyle);
        }

        private void OnDestroy()
        {
            if (nativeTitleImage != null) nativeTitleImage.SetActive(nativeTitleImageWasActive);
            presentation.Dispose();
            harmony?.UnpatchSelf();
            if (instance == this) instance = null;
        }
    }
}
