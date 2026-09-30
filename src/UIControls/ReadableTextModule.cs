using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using InstantHorror.Scripts.InteractionSystems;
using InstantHorror.Scripts.Novel;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Disappearance.UIControls
{
    internal sealed class ReadableTextModule
    {
        private const string Original = "<color=\"red\">「緑→黄→青→ピンク」</color>";
        private const string Accessible = "「<color=#009E73>緑</color>→<color=#F0E442>黄</color>→" +
            "<color=#56B4E9>青</color>→<color=#CC79A7>ピンク</color>」";

        private readonly ManualLogSource logger;
        private readonly ConfigEntry<bool> scaleVillageUI;
        private readonly ConfigEntry<bool> scaleNovelUI;
        private readonly ConfigEntry<int> referenceWidth;
        private readonly ConfigEntry<int> referenceHeight;
        private Harmony harmony;
        private sealed class ScalerState
        {
            internal CanvasScaler.ScaleMode Mode;
            internal Vector2 Reference;
            internal CanvasScaler.ScreenMatchMode MatchMode;
            internal float Match;
            internal bool Novel;
        }
        private readonly Dictionary<CanvasScaler, ScalerState> scalers = new Dictionary<CanvasScaler, ScalerState>();
        private TextMeshProUGUI changedReport;
        private string originalReport;
        private string replacementReport;
        private float nextScanTime;

        internal ReadableTextModule(ConfigFile config, ManualLogSource logger)
        {
            this.logger = logger;
            scaleVillageUI = config.Bind("Display", "ScaleVillageUI", true,
                "Scale the village's main dialogue canvas with resolution.");
            scaleNovelUI = config.Bind("Display", "ScaleNovelUI", true,
                "Scale both opening and ending novel canvases with resolution.");
            referenceWidth = config.Bind("Display", "ReferenceWidth", 1920,
                "Reference width used for dialogue and novel canvases.");
            referenceHeight = config.Bind("Display", "ReferenceHeight", 1080,
                "Reference height used for dialogue and novel canvases.");
        }

        internal void Start()
        {
            if (harmony != null) return;
            harmony = new Harmony(UIControlsPlugin.PluginId + ".readabletext");
            try
            {
                var postfix = new HarmonyMethod(typeof(ReadableTextModule), nameof(AfterTextChanged));
                harmony.Patch(AccessTools.Method(typeof(TextItemUIHandler), nameof(TextItemUIHandler.Show)),
                    postfix: postfix);
                harmony.Patch(AccessTools.Method(typeof(TextItemUIHandler), nameof(TextItemUIHandler.ShowNextSentence)),
                    postfix: postfix);
                // Apply before the first sentence, independently of text completion/skip.
                harmony.Patch(AccessTools.Method(typeof(BottomBarController), nameof(BottomBarController.PlayScene)),
                    prefix: new HarmonyMethod(typeof(ReadableTextModule), nameof(BeforeNovelScene)));
                active = this;
                nextScanTime = 0f;
            }
            catch
            {
                harmony.UnpatchSelf();
                harmony = null;
                throw;
            }
        }

        private static ReadableTextModule active;

        private static void BeforeNovelScene(BottomBarController __instance)
        {
            active?.ScaleNovel(__instance);
        }

        private void ScaleNovel(BottomBarController bar)
        {
            if (!scaleNovelUI.Value || bar == null || bar.barText == null) return;
            string scene = bar.gameObject.scene.name;
            if (scene != "OpeningTextScene" && scene != "EndingTextScene") return;
            ApplyScaler(bar.barText.canvas, true);
        }

        private static void AfterTextChanged(TextMeshProUGUI ___text)
        {
            active?.ReplaceReportColors(___text);
        }

        private void ReplaceReportColors(TextMeshProUGUI text)
        {
            if (SceneManager.GetActiveScene().name != "VillageScene" || text == null) return;
            string current = text.text;
            if (current == null || !current.Contains(Original)) return;
            changedReport = text;
            originalReport = current;
            replacementReport = current.Replace(Original, Accessible);
            text.text = replacementReport;
        }

        internal void Update()
        {
            if (Time.unscaledTime < nextScanTime) return;
            nextScanTime = Time.unscaledTime + 1f;
            foreach (var entry in scalers)
                if (!(entry.Value.Novel ? scaleNovelUI.Value : scaleVillageUI.Value)) RestoreScaler(entry.Key, entry.Value);
            foreach (BottomBarController bar in Resources.FindObjectsOfTypeAll<BottomBarController>())
                if (bar != null && bar.gameObject.scene.isLoaded) ScaleNovel(bar);
            if (!scaleVillageUI.Value) return;
            foreach (Canvas canvas in Resources.FindObjectsOfTypeAll<Canvas>())
            {
                if (canvas == null || canvas.gameObject.scene.name != "VillageScene" ||
                    !canvas.gameObject.scene.isLoaded || canvas.gameObject.name != "Canvas" ||
                    canvas.transform.Find("TextEventUI") == null || canvas.transform.Find("TextItemUI") == null)
                    continue;
                ApplyScaler(canvas, false);
            }
        }

        private void ApplyScaler(Canvas canvas, bool novel)
        {
            if (canvas == null || canvas.renderMode == RenderMode.WorldSpace) return;
            CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler == null) return;
            if (!scalers.ContainsKey(scaler))
            {
                scalers.Add(scaler, new ScalerState { Mode = scaler.uiScaleMode,
                    Reference = scaler.referenceResolution, MatchMode = scaler.screenMatchMode,
                    Match = scaler.matchWidthOrHeight, Novel = novel });
                logger.LogInfo("Text canvas scaling: " + canvas.gameObject.scene.name + "/" + canvas.name);
            }
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(Math.Max(640, referenceWidth.Value), Math.Max(360, referenceHeight.Value));
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0f;
        }

        private static void RestoreScaler(CanvasScaler scaler, ScalerState state)
        {
            if (scaler == null) return;
            scaler.uiScaleMode = state.Mode;
            scaler.referenceResolution = state.Reference;
            scaler.screenMatchMode = state.MatchMode;
            scaler.matchWidthOrHeight = state.Match;
        }

        internal void OnSceneChanged()
        {
            Restore();
            nextScanTime = 0f;
            if (harmony != null) Update();
        }

        internal void Stop()
        {
            if (active == this) active = null;
            harmony?.UnpatchSelf();
            harmony = null;
            Restore();
        }

        private void Restore()
        {
            foreach (var entry in scalers) RestoreScaler(entry.Key, entry.Value);
            scalers.Clear();
            if (changedReport != null && changedReport.text == replacementReport)
                changedReport.text = originalReport;
            changedReport = null;
            originalReport = null;
            replacementReport = null;
        }
    }
}
