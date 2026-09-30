using System;
using System.Collections.Generic;
using Disappearance.Shared;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Disappearance.UIControls
{
    internal sealed class ContextHintRenderer
    {
        internal struct VisibleRow
        {
            internal RegisteredAction Action;
            internal ActionBinding Binding;
            internal string Text;
            internal float Alpha;
        }

        private readonly ActionRegistry registry;
        private readonly ActiveControlHints controlHints;
        private readonly GameFadeVisibility fadeVisibility;
        private readonly List<VisibleRow> visibleRows = new List<VisibleRow>();
        internal float LastCompactAlpha { get; private set; }

        internal ContextHintRenderer(ActionRegistry registry, ActiveControlHints controlHints,
            GameFadeVisibility fadeVisibility)
        {
            this.registry = registry;
            this.controlHints = controlHints;
            this.fadeVisibility = fadeVisibility;
        }

        internal void Draw(string contextId, bool showSharedHints)
        {
            visibleRows.Clear();
            LastCompactAlpha = 0f;
            float fade = fadeVisibility.Alpha;
            if (fade <= 0f) return;
            List<RegisteredAction> actions = showSharedHints
                ? registry.VisibleActions(contextId) : new List<RegisteredAction>();
            bool gamepad = controlHints.GamepadActive && Gamepad.current != null;
            float scale = Mathf.Min(Screen.width / 1920f, Screen.height / 1080f);
            float rowHeight = 48f * scale;
            float glyph = 40f * scale;
            float gap = 10f * scale;
            var label = new GUIStyle(GUI.skin.label) {
                alignment = TextAnchor.MiddleLeft,
                fontSize = Mathf.Max(12, Mathf.RoundToInt(23f * scale))
            };
            JapaneseGuiFont.Apply(label);
            label.normal.textColor = new Color(0.87f, 0.93f, 1f);
            var separator = new GUIStyle(GUI.skin.label) {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.Max(10, Mathf.RoundToInt(19f * scale))
            };
            separator.normal.textColor = new Color(0.82f, 0.91f, 1f);
            float width = 0f;
            float maxAlpha = 0f;
            for (int i = 0; i < actions.Count; i++)
            {
                RegisteredAction action = actions[i];
                float alpha = Mathf.Clamp01((float)action.State.Alpha * fade);
                if (alpha <= 0f) continue;
                ActionBinding[] bindings = gamepad ? action.Definition.Gamepad : action.Definition.KeyboardMouse;
                if (bindings.Length == 0) bindings = gamepad ? action.Definition.KeyboardMouse : action.Definition.Gamepad;
                if (bindings.Length == 0) continue;
                string text = ActionText(action);
                visibleRows.Add(new VisibleRow { Action = action, Binding = bindings[0], Text = text, Alpha = alpha });
                width = Mathf.Max(width, MeasureRow(bindings[0], text, label, glyph, gap, scale));
                maxAlpha = Mathf.Max(maxAlpha, alpha);
            }
            if (visibleRows.Count > 0)
            {
                width = Mathf.Min(width, Screen.width - 32f * scale);
                var panel = new Rect(Screen.width - width - 24f * scale,
                    Screen.height - visibleRows.Count * rowHeight - 24f * scale,
                    width, visibleRows.Count * rowHeight);
                Color old = GUI.color;
                try
                {
                    GUI.color = new Color(0f, 0f, 0f, 0.72f * maxAlpha);
                    GUI.DrawTexture(panel, Texture2D.whiteTexture);
                    GUI.color = new Color(0.78f, 0.9f, 1f, 0.24f * maxAlpha);
                    for (int i = 1; i < visibleRows.Count; i++)
                        GUI.DrawTexture(new Rect(panel.x + 10f * scale,
                            panel.y + i * rowHeight, panel.width - 20f * scale,
                            Mathf.Max(1f, scale)), Texture2D.whiteTexture);
                }
                finally { GUI.color = old; }
                for (int i = 0; i < visibleRows.Count; i++)
                    DrawRow(new Rect(panel.x, panel.y + i * rowHeight, panel.width, rowHeight),
                        visibleRows[i], label, separator, scale);
            }
            DrawCompact(contextId, gamepad, fade, scale);
        }

        private void DrawCompact(string contextId, bool gamepad, float fade, float scale)
        {
            List<RegisteredAction> actions = registry.VisibleCompactActions(contextId);
            if (actions.Count == 0) return;
            float glyph = 28f * scale;
            float rowHeight = 34f * scale;
            float gap = 7f * scale;
            var label = new GUIStyle(GUI.skin.label) {
                alignment = TextAnchor.MiddleLeft,
                fontSize = Mathf.Max(11, Mathf.RoundToInt(17f * scale))
            };
            JapaneseGuiFont.Apply(label);
            label.normal.textColor = Color.white;
            var shadow = new GUIStyle(label);
            shadow.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
            int drawn = 0;
            for (int i = 0; i < actions.Count; i++)
            {
                RegisteredAction action = actions[i];
                float alpha = Mathf.Clamp01((float)action.State.Alpha * fade);
                if (alpha <= 0f) continue;
                ActionBinding[] bindings = gamepad ? action.Definition.Gamepad : action.Definition.KeyboardMouse;
                if (bindings.Length == 0)
                    bindings = gamepad ? action.Definition.KeyboardMouse : action.Definition.Gamepad;
                if (bindings.Length == 0) continue;
                string text = ActionText(action);
                float width = Mathf.Min(MeasureRow(bindings[0], text, label, glyph, gap, scale),
                    Screen.width - 40f * scale);
                var row = new VisibleRow { Action = action, Binding = bindings[0], Text = text, Alpha = alpha };
                LastCompactAlpha = Mathf.Max(LastCompactAlpha, alpha);
                var rect = new Rect(20f * scale,
                    Screen.height - (drawn + 1) * rowHeight - 18f * scale, width, rowHeight);
                DrawCompactRow(rect, row, label, shadow, scale, glyph, gap);
                drawn++;
            }
        }

        private static string ActionText(RegisteredAction action)
        {
            string text = action.Definition.Label;
            if (action.Definition.HoldSeconds > 0)
                text += "（" + action.Definition.HoldSeconds.ToString("0.#") + "秒長押し）";
            if (action.State.HasToggled) text += action.State.Toggled ? " ON" : " OFF";
            return text;
        }

        internal static float MeasureRow(ActionBinding binding, string text, GUIStyle label,
            float glyph, float gap, float scale)
        {
            float width = 14f * scale;
            for (int i = 0; i < binding.Controls.Length; i++)
            {
                if (i > 0) width += 18f * scale + gap;
                width += ControlGlyphRenderer.Width(glyph, binding.Controls[i]) + gap;
            }
            return width + 8f * scale +
                Mathf.Ceil(label.CalcSize(new GUIContent(text)).x) + 18f * scale;
        }

        internal static void DrawRow(Rect panel, VisibleRow row, GUIStyle label,
            GUIStyle separator, float scale)
        {
            Color old = GUI.color;
            try
            {
                GUI.color = new Color(old.r, old.g, old.b, old.a * row.Alpha *
                    (row.Action.State.Enabled ? 1f : 0.48f));
                float glyph = 40f * scale;
                float gap = 10f * scale;
                float x = panel.x + 14f * scale;
                float y = panel.y + (panel.height - glyph) * 0.5f;
                for (int i = 0; i < row.Binding.Controls.Length; i++)
                {
                    if (i > 0)
                    {
                        string separatorText = row.Binding.Mode == "chord" ? "+" : "/";
                        GUI.Label(new Rect(x, y, 18f * scale, glyph), separatorText, separator);
                        x += 18f * scale + gap;
                    }
                    string path = row.Binding.Controls[i];
                    bool controller = path.StartsWith("<Gamepad>", StringComparison.Ordinal);
                    float used = ControlGlyphRenderer.DrawControl(new Rect(x, y, glyph, glyph), path, controller);
                    x += used + gap;
                }
                GUI.Label(new Rect(x + 8f * scale, panel.y,
                    Mathf.Max(0f, panel.xMax - x - 18f * scale), panel.height), row.Text, label);
                if (row.Action.State.HasProgress)
                {
                    float progress = Mathf.Clamp01((float)row.Action.State.Progress);
                    GUI.color = new Color(0.38f, 0.76f, 1f, row.Alpha);
                    GUI.DrawTexture(new Rect(panel.x, panel.yMax - 3f * scale, panel.width * progress,
                        3f * scale), Texture2D.whiteTexture);
                }
            }
            finally { GUI.color = old; }
        }

        private static void DrawCompactRow(Rect panel, VisibleRow row, GUIStyle label,
            GUIStyle shadow, float scale, float glyph, float gap)
        {
            Color old = GUI.color;
            try
            {
                GUI.color = new Color(old.r, old.g, old.b, old.a * row.Alpha *
                    (row.Action.State.Enabled ? 1f : 0.48f));
                float x = panel.x;
                float y = panel.y + (panel.height - glyph) * 0.5f;
                for (int i = 0; i < row.Binding.Controls.Length; i++)
                {
                    string path = row.Binding.Controls[i];
                    bool controller = path.StartsWith("<Gamepad>", StringComparison.Ordinal);
                    float used = ControlGlyphRenderer.DrawControl(new Rect(x, y, glyph, glyph), path, controller);
                    x += used + gap;
                }
                var textRect = new Rect(x + 3f * scale, panel.y,
                    Mathf.Max(0f, panel.xMax - x - 3f * scale), panel.height);
                var shadowRect = new Rect(textRect.x + scale, textRect.y + scale,
                    textRect.width, textRect.height);
                GUI.Label(shadowRect, row.Text, shadow);
                GUI.Label(textRect, row.Text, label);
                if (row.Action.State.HasProgress)
                {
                    float progress = Mathf.Clamp01((float)row.Action.State.Progress);
                    GUI.color = new Color(0.38f, 0.76f, 1f, row.Alpha);
                    GUI.DrawTexture(new Rect(panel.x, panel.yMax - 2f * scale,
                        panel.width * progress, Mathf.Max(1f, 2f * scale)), Texture2D.whiteTexture);
                }
            }
            finally { GUI.color = old; }
        }

    }
}
