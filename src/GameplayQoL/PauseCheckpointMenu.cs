using System;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using Disappearance.Shared;

namespace Disappearance.GameplayQoL
{
    internal sealed class PauseCheckpointMenu : MonoBehaviour
    {
        private static readonly FieldInfo PauseUiField = AccessTools.Field(typeof(PauseMenuHandler), "pauseMenuUIHandler");
        private static readonly FieldInfo PauseRootField = AccessTools.Field(typeof(PauseMenuUIHandler), "pauseMenu");
        private static readonly PropertyInfo SelectedPauseProperty = AccessTools.Property(typeof(PauseMenuUIHandler), "SelectedPauseMenu");
        private static PauseCheckpointMenu instance;
        private Harmony harmony;
        private int selected;
        private GUIStyle rowStyle, titleStyle;
        private readonly NativeMenuCanvas presentation = new NativeMenuCanvas(message => GameplayQoLPlugin.Log.LogInfo(message));
        private MenuCanvasProjection confirmationProjection;

        private CheckpointModule Checkpoints => GameplayQoLPlugin.Instance?.Checkpoints;

        private void Awake()
        {
            instance = this;
            harmony = new Harmony(GameplayQoLPlugin.PluginId + ".pause-checkpoints");
            try
            {
                harmony.Patch(AccessTools.Method(typeof(PauseMenuHandler), "Pause"),
                    prefix: new HarmonyMethod(typeof(PauseCheckpointMenu), nameof(BeforePause)),
                    postfix: new HarmonyMethod(typeof(PauseCheckpointMenu), nameof(AfterPause)));
                harmony.Patch(AccessTools.Method(typeof(PauseMenuHandler), "Resume"),
                    postfix: new HarmonyMethod(typeof(PauseCheckpointMenu), nameof(AfterResume)));
                harmony.Patch(AccessTools.Method(typeof(PauseMenuHandler), "Up"),
                    prefix: new HarmonyMethod(typeof(PauseCheckpointMenu), nameof(BeforeUp)));
                harmony.Patch(AccessTools.Method(typeof(PauseMenuHandler), "Down"),
                    prefix: new HarmonyMethod(typeof(PauseCheckpointMenu), nameof(BeforeDown)));
                harmony.Patch(AccessTools.Method(typeof(PauseMenuHandler), "Left"),
                    prefix: new HarmonyMethod(typeof(PauseCheckpointMenu), nameof(BeforeUp)));
                harmony.Patch(AccessTools.Method(typeof(PauseMenuHandler), "Right"),
                    prefix: new HarmonyMethod(typeof(PauseCheckpointMenu), nameof(BeforeDown)));
                harmony.Patch(AccessTools.Method(typeof(PauseMenuHandler), "DecisionOnPauseMenu"),
                    prefix: new HarmonyMethod(typeof(PauseCheckpointMenu), nameof(BeforeDecision)));
                harmony.Patch(AccessTools.Method(typeof(PauseMenuUIHandler), "Open"),
                    postfix: new HarmonyMethod(typeof(PauseCheckpointMenu), nameof(AfterPauseUiOpen)));
                harmony.Patch(AccessTools.Method(typeof(BackToTitleMenuUIHandler), "Open"),
                    postfix: new HarmonyMethod(typeof(PauseCheckpointMenu), nameof(AfterBackToTitleOpen)));
                harmony.Patch(AccessTools.Method(typeof(BackToTitleMenuUIHandler), "Close"),
                    postfix: new HarmonyMethod(typeof(PauseCheckpointMenu), nameof(AfterBackToTitleClose)));
            }
            catch (Exception exception)
            {
                harmony.UnpatchSelf();
                harmony = null;
                GameplayQoLPlugin.Log.LogError("Pause checkpoint menu patch failed: " + exception);
            }
        }

        private static void BeforePause(PauseMenuHandler __instance, out bool __state)
        {
            __state = __instance.CurrentPauseStatus == PauseMenuHandler.PauseStatus.UnPaused;
        }

        private static void AfterPause(PauseMenuHandler __instance, bool __state)
        {
            // Harmony postfixes also run when a modal rejects the original Pause.
            // A rejected call must not reopen the underlying pause table.
            if (!__state || !Handles(__instance)) return;
            instance.selected = 0;
            instance.HideNativePause(__instance);
        }

        private static void AfterResume(PauseMenuHandler __instance)
        {
            if (instance != null && __instance.CurrentPauseStatus == PauseMenuHandler.PauseStatus.UnPaused)
                instance.selected = 0;
        }

        private static bool BeforeUp(PauseMenuHandler __instance)
        {
            if (!Handles(__instance)) return true;
            instance.selected = Math.Max(0, instance.selected - 1);
            return false;
        }

        private static bool BeforeDown(PauseMenuHandler __instance)
        {
            if (!Handles(__instance)) return true;
            instance.selected = Math.Min(3, instance.selected + 1);
            return false;
        }

        private static bool BeforeDecision(PauseMenuHandler __instance)
        {
            if (!Handles(__instance)) return true;
            PauseMenuUIHandler ui = PauseUiField?.GetValue(__instance) as PauseMenuUIHandler;
            if (instance.selected == 1)
            {
                instance.Checkpoints?.OpenNormalFromPause(__instance);
                return false;
            }
            PauseMenuUIHandler.PauseMenu nativeSelection = instance.selected == 0
                ? PauseMenuUIHandler.PauseMenu.Resume
                : instance.selected == 2 ? PauseMenuUIHandler.PauseMenu.Setting
                : PauseMenuUIHandler.PauseMenu.BackToTitle;
            SelectedPauseProperty?.SetValue(ui, nativeSelection, null);
            return true;
        }

        private static void AfterPauseUiOpen(PauseMenuUIHandler __instance)
        {
            if (instance == null) return;
            instance.SetPauseRoot(__instance, false);
        }

        private static void AfterBackToTitleOpen(BackToTitleMenuUIHandler __instance)
        {
            if (instance == null || instance.Checkpoints == null) return;
            CheckpointProgress progress = instance.Checkpoints.Progress;
            var root = AccessTools.Field(typeof(BackToTitleMenuUIHandler), "backToTitleMenu")
                ?.GetValue(__instance) as GameObject;
            if (root == null) return;
            TMP_Text body = null;
            foreach (TMP_Text label in root.GetComponentsInChildren<TMP_Text>(true))
                if (label.name == "CautionText") body = label;
            if (body == null) return;

            // Keep the native menu lifecycle and Yes/No input. Additional labels
            // are direct children, so the game's Open/Close toggles them too.
            ConfigureConfirmationLabel(body, "タイトル画面に戻ります。", 0, 235, 1000, Color.white);
            foreach (TMP_Text label in root.GetComponentsInChildren<TMP_Text>(true))
                if (label.text == "注意！")
                {
                    ConfigureConfirmationLabel(label, "注意！", 0, 365, 450, Color.white);
                    label.fontSize = 72;
                    label.rectTransform.sizeDelta = new Vector2(450, 100);
                }
            ConfirmationLine(body, "ResumeLabel", "現在の再開地点", -130, 125, 280, TextAlignmentOptions.MidlineLeft);
            ConfirmationLine(body, "ResumeValue", progress.HasResume ? StageName(progress.ResumeStage) : "未保存",
                180, 125, 320, TextAlignmentOptions.MidlineLeft);
            ConfirmationLine(body, "ModeLabel", "モード", -130, 70, 280, TextAlignmentOptions.MidlineLeft);
            ConfirmationLine(body, "ModeValue", progress.ResumeWithMosaic ? "配信者モード" : "通常モード",
                180, 70, 320, TextAlignmentOptions.MidlineLeft);
            ConfirmationLine(body, "ResumeExplanation", progress.HasResume
                ? "次回はこの地点から再開します。" : "再開地点はまだ保存されていません。", 0, -45, 1000);
            ConfirmationLine(body, "ProgressWarning", "再開地点より後の進行状況は保持されません。",
                0, -120, 1100, TextAlignmentOptions.Center, Color.red);
            ConfirmationLine(body, "ConfirmQuestion", "よろしいですか？", 0, -195, 1000);
            PositionConfirmationChoice(root.transform.Find("NoButton"), -160);
            PositionConfirmationChoice(root.transform.Find("YesButton"), 160);
            instance.confirmationProjection?.Dispose();
            instance.confirmationProjection = null;
            try
            {
                instance.confirmationProjection = new MenuCanvasProjection((RectTransform)root.transform,
                    "GameplayQoL_TitleConfirmationCanvas");
            }
            catch (Exception exception)
            {
                GameplayQoLPlugin.Log.LogError("Title confirmation Overlay projection failed: " + exception);
            }
        }

        private static void AfterBackToTitleClose()
        {
            if (instance == null) return;
            instance.confirmationProjection?.Dispose();
            instance.confirmationProjection = null;
        }

        private static void ConfigureConfirmationLabel(TMP_Text label, string text, float x, float y,
            float width, Color color, TextAlignmentOptions alignment = TextAlignmentOptions.Center)
        {
            label.richText = true;
            label.color = color;
            label.fontSize = 32;
            label.enableAutoSizing = false;
            label.alignment = alignment;
            label.lineSpacing = 0;
            label.text = text;
            RectTransform rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, 60);
        }

        private static void ConfirmationLine(TMP_Text template, string name, string text, float x, float y,
            float width, TextAlignmentOptions alignment = TextAlignmentOptions.Center, Color? color = null)
        {
            string childName = "GameplayQoL" + name;
            Transform child = template.transform.parent.Find(childName);
            TMP_Text label = child == null
                ? Instantiate(template, template.transform.parent, false) : child.GetComponent<TMP_Text>();
            label.name = childName;
            ConfigureConfirmationLabel(label, text, x, y, width, color ?? Color.white, alignment);
            label.gameObject.SetActive(true);
        }

        private static void PositionConfirmationChoice(Transform button, float x)
        {
            if (button == null) return;
            var rect = button as RectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = new Vector2(x, -310);
            foreach (TMP_Text label in button.GetComponentsInChildren<TMP_Text>(true))
            {
                ConfigureConfirmationLabel(label, label.text, 0, 0, 180, Color.white);
                label.fontSize = 36;
            }
            var triangle = button.Find("Triangle") as RectTransform;
            if (triangle != null)
            {
                triangle.anchorMin = triangle.anchorMax = triangle.pivot = new Vector2(.5f, .5f);
                triangle.anchoredPosition = new Vector2(-120, 0);
                triangle.sizeDelta = new Vector2(60, 60);
            }
        }

        private static bool Handles(PauseMenuHandler menu)
        {
            return instance != null && instance.Checkpoints != null && !instance.Checkpoints.IsOpen &&
                GameplayQoLPlugin.Instance?.Modal.IsOccupied != true &&
                menu != null && menu.CurrentPauseStatus == PauseMenuHandler.PauseStatus.Paused &&
                menu.SelectedMode == PauseMenuHandler.Mode.PauseMenu;
        }

        private void Update()
        {
            PauseMenuHandler menu = FindObjectOfType<PauseMenuHandler>();
            if (Handles(menu)) HideNativePause(menu); else presentation.Hide();
        }

        private void HideNativePause(PauseMenuHandler menu)
        {
            PauseMenuUIHandler ui = PauseUiField?.GetValue(menu) as PauseMenuUIHandler;
            SetPauseRoot(ui, false);
        }

        private void SetPauseRoot(PauseMenuUIHandler ui, bool active)
        {
            GameObject root = ui == null ? null : PauseRootField?.GetValue(ui) as GameObject;
            if (root == null) return;
            root.SetActive(true);
            // The original full-screen controls table is a sibling of the three
            // choices. Only retire those choices; keep Image and its table overlay.
            foreach (Transform child in root.transform)
                if (child.name == "ResumeButton" || child.name == "SettingButton" || child.name == "BackToTitleButton")
                    child.gameObject.SetActive(active);
        }

        private void OnGUI()
        {
            PauseMenuHandler menu = FindObjectOfType<PauseMenuHandler>();
            if (!Handles(menu) || Event.current.type != EventType.Repaint) return;
            float scale = Mathf.Min(Screen.width / 1920f, Screen.height / 1080f);
            float height = 60f * scale;
            float x = 96f * scale, y = 866f * scale;
            EnsureStyles(scale);
            Color previous = GUI.color;
            presentation.Begin((PauseUiField.GetValue(menu) as PauseMenuUIHandler)?.transform);
            presentation.Background(Color.clear);
            GUI.color = Color.white;
            string[] labels = { "ゲームに戻る", "再開地点を選ぶ", "設定", "タイトルに戻る" };
            float pointerWidth = rowStyle.fontSize * 1.6f;
            float[] widths = new float[labels.Length];
            float totalWidth = 0f;
            for (int i = 0; i < labels.Length; i++)
            {
                widths[i] = pointerWidth + presentation.PreferredWidth(labels[i], rowStyle.fontSize);
                totalWidth += widths[i];
            }
            float gap = Mathf.Min(72f * scale,
                Mathf.Max(12f * scale, (Screen.width - x - 24f * scale - totalWidth) / (labels.Length - 1)));
            for (int i = 0; i < labels.Length; i++)
            {
                Rect row = new Rect(x, y, widths[i], height);
                presentation.Label(row,
                    (i == selected ? "<color=#FF0000>▶</color>  " : "    ") + labels[i], rowStyle, Color.white);
                x += widths[i] + gap;
            }
            presentation.End();
            GUI.color = previous;
        }

        private void EnsureStyles(float scale)
        {
            int size = Mathf.RoundToInt(36f * scale);
            if (rowStyle != null && rowStyle.fontSize == size) return;
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(34f * scale),
                fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            rowStyle = new GUIStyle(GUI.skin.label) { fontSize = size, alignment = TextAnchor.MiddleLeft };
            JapaneseGuiFont.Apply(titleStyle); JapaneseGuiFont.Apply(rowStyle);
        }

        private static string StageName(int stage)
        {
            string[] values = CheckpointStages.Names;
            return stage >= 0 && stage < values.Length ? values[stage] : "不明";
        }

        private void OnDestroy()
        {
            confirmationProjection?.Dispose();
            confirmationProjection = null;
            presentation.Dispose();
            harmony?.UnpatchSelf();
            if (instance == this) instance = null;
        }
    }
}
