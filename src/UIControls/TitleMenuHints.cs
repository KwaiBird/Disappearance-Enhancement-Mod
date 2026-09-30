using System.Collections.Generic;
using Disappearance.Shared;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Disappearance.UIControls
{
    internal sealed class TitleMenuHints
    {
        private readonly ActiveControlHints controls;
        private readonly GameFadeVisibility fade;
        private readonly ActionRegistry registry;
        internal int LastRowCount { get; private set; }
        internal float LastAlpha { get; private set; }

        internal TitleMenuHints(ActiveControlHints controls, GameFadeVisibility fade, ActionRegistry registry)
        {
            this.controls = controls;
            this.fade = fade;
            this.registry = registry;
        }

        internal void Draw()
        {
            LastRowCount = 0;
            LastAlpha = 0f;
            if (SceneManager.GetActiveScene().name != "TitleScene") return;
            float alpha = fade.Alpha;
            LastAlpha = alpha;
            if (alpha <= 0f) return;
            bool pad = controls.GamepadActive && Gamepad.current != null;
            float scale = Mathf.Min(Screen.width / 1920f, Screen.height / 1080f);
            float rowHeight = 48f * scale;
            var label = new GUIStyle(GUI.skin.label) {
                alignment = TextAnchor.MiddleLeft,
                fontSize = Mathf.Max(12, Mathf.RoundToInt(22f * scale))
            };
            JapaneseGuiFont.Apply(label);
            label.normal.textColor = new Color(0.87f, 0.93f, 1f);
            var separator = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter };
            var rows = new List<ContextHintRenderer.VisibleRow>();
            ContextHintRenderer.VisibleRow? backRow = null;
            bool ownsSelect = false, ownsConfirm = false;
            foreach (RegisteredAction action in registry.EmbeddedActions("title"))
            {
                if (action.Definition.ActionId == "title.select") ownsSelect = true;
                if (action.Definition.ActionId == "title.confirm") ownsConfirm = true;
                if (!action.State.Visible) continue;
                ActionBinding[] bindings = pad ? action.Definition.Gamepad : action.Definition.KeyboardMouse;
                if (bindings.Length == 0) continue;
                var row = new ContextHintRenderer.VisibleRow {
                    Action = action, Binding = bindings[0], Text = action.Definition.Label,
                    Alpha = Mathf.Clamp01((float)action.State.Alpha * alpha)
                };
                if (action.Definition.ActionId == "title.back") backRow = row;
                else rows.Add(row);
            }
            if (!ownsSelect) rows.Add(BaseRow("選択", pad ? new[] { "<Gamepad>/leftStick", "<Gamepad>/dpad" }
                : new[] { "<Keyboard>/w", "<Keyboard>/s" }, alpha));
            if (!ownsConfirm) rows.Add(BaseRow("決定", pad ? new[] { "<Gamepad>/buttonSouth" }
                : new[] { "<Keyboard>/e", "<Keyboard>/enter" }, alpha));
            if (backRow.HasValue) rows.Add(backRow.Value);
            LastRowCount = rows.Count;
            float width = 0f;
            foreach (var row in rows)
                width = Mathf.Max(width, ContextHintRenderer.MeasureRow(row.Binding,
                    row.Text, label, 40f * scale, 10f * scale, scale));
            var panel = new Rect(Screen.width - width - 24f * scale,
                Screen.height - rows.Count * rowHeight - 22f * scale, width, rows.Count * rowHeight);
            Color previous = GUI.color;
            try
            {
                GUI.color = new Color(0f, 0f, 0f, 0.72f * alpha);
                GUI.DrawTexture(panel, Texture2D.whiteTexture);
                GUI.color = new Color(0.78f, 0.9f, 1f, 0.28f * alpha);
                for (int i = 1; i < rows.Count; i++)
                    GUI.DrawTexture(new Rect(panel.x + 14f * scale, panel.y + i * rowHeight,
                        panel.width - 28f * scale, Mathf.Max(1f, scale)), Texture2D.whiteTexture);
                GUI.color = previous;
                for (int i = 0; i < rows.Count; i++)
                    ContextHintRenderer.DrawRow(new Rect(panel.x, panel.y + i * rowHeight,
                        panel.width, rowHeight), rows[i], label, separator, scale);
            }
            finally { GUI.color = previous; }
        }

        // Native title select/confirm remain available when GameplayQoL is absent.
        // They use exactly the same row and glyph renderer as feature-owned actions.
        private static ContextHintRenderer.VisibleRow BaseRow(string text, string[] paths, float alpha)
        {
            return new ContextHintRenderer.VisibleRow {
                Action = new RegisteredAction { State = new ActionState(new DisplayState { Enabled = true }) },
                Binding = new ActionBinding { Mode = "alternatives", Controls = paths },
                Text = text, Alpha = alpha
            };
        }
    }
}
