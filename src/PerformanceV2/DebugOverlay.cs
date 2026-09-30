using System;
using Disappearance.Shared;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Disappearance.PerformanceV2
{
    internal sealed class DebugOverlay
    {
        private GUIStyle style;
        private readonly PerformanceSettings settings;
        private readonly RenderToggles toggles;
        private readonly PerformanceDiagnostics diagnostics;
        private readonly DiagnosticRegistry registry;
        internal DebugOverlay(PerformanceSettings settings, RenderToggles toggles, PerformanceDiagnostics diagnostics, DiagnosticRegistry registry)
        { this.settings = settings; this.toggles = toggles; this.diagnostics = diagnostics; this.registry = registry; }

        internal void Draw()
        {
            if (!settings.ShowOverlay || SceneManager.GetActiveScene().name != "VillageScene") return;
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label);
                style.fontSize = Math.Max(18, Screen.height / 65);
                style.fontStyle = FontStyle.Bold;
                style.normal.textColor = Color.white;
                style.alignment = TextAnchor.UpperLeft;
                style.wordWrap = true;
                JapaneseGuiFont.Apply(style);
            }
            var lines = new System.Collections.Generic.List<string> {
                "性能　Ctrl+Shift+F1：両パネルを非表示",
                diagnostics.HasSample
                    ? "FPS " + diagnostics.Fps.ToString("F1") + "   frame p50 " + diagnostics.P50.ToString("F1") + "ms   p95 " + diagnostics.P95.ToString("F1") + "ms"
                    : "Measuring performance...",
                diagnostics.HasSample ? diagnostics.CounterLine1 : "Waiting for a 2-second sample",
                diagnostics.HasSample ? diagnostics.CounterLine2 : " ",
                "F6 Sun shadows: " + (toggles.SunShadowsOff ? "OFF" : "ON"),
                "F7 Point shadows: " + (toggles.PointShadowsOff ? "OFF" : "ON"),
                "F8 Vine shadows: " + (toggles.VineShadowsOff ? "OFF" : "ON"),
                "F9 Vine instancing: " + (toggles.VineInstancingOn ? "ON" : "OFF"),
                "F10 Camera PostFX: " + (toggles.PostFxOff ? "OFF" : "ON"),
                "F11 vSync: " + (QualitySettings.vSyncCount == 0 ? "OFF" : "ON (" + QualitySettings.vSyncCount + ")")
            };
            // Share the available height between the two panels on smaller screens.
            var adjustment = new System.Collections.Generic.List<string> { "画面・照明調整" };
            adjustment.AddRange(registry.VisibleRows());
            style.fontSize = Math.Max(14, Screen.height / 65);
            float maxRows = lines.Count + adjustment.Count;
            style.fontSize = Math.Min(style.fontSize, Math.Max(12, (int)((Screen.height - 110) / (maxRows * 1.9f))));
            Rect performance = DiagnosticOverlayLayout.Draw(lines, style);
            if (adjustment.Count > 1) DiagnosticOverlayLayout.Draw(adjustment, style, performance.yMax + 12f);
        }
    }
}
