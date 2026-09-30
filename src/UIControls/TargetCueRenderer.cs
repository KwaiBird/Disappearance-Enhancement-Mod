using Disappearance.Shared;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Disappearance.UIControls
{
    // Draws only target-local controls/labels. Never calls the help-row renderer.
    internal sealed class TargetCueRenderer
    {
        private readonly ActionRegistry registry;
        private readonly ActiveControlHints controlHints;
        private readonly GameFadeVisibility fadeVisibility;

        internal TargetCueRenderer(ActionRegistry registry, ActiveControlHints controls, GameFadeVisibility fade)
        { this.registry = registry; controlHints = controls; fadeVisibility = fade; }

        internal void Draw(string context)
        {
            float fade = fadeVisibility.Alpha;
            if (fade <= 0f) return;
            bool pad = controlHints.GamepadActive && Gamepad.current != null;
            float scale = Mathf.Min(Screen.width / 1920f, Screen.height / 1080f);
            foreach (RegisteredTargetCue cue in registry.VisibleTargetCues(context))
            {
                ActionBinding[] bindings = pad ? cue.Definition.Gamepad : cue.Definition.KeyboardMouse;
                if (bindings.Length == 0) bindings = pad ? cue.Definition.KeyboardMouse : cue.Definition.Gamepad;
                if (bindings.Length == 0) continue;
                float alpha = (float)cue.State.Alpha * fade;
                if (alpha <= 0f) continue;
                ActionBinding binding = bindings[0];
                float size = 82f * scale, gap = 7f * scale, separatorWidth = 18f * scale;
                float width = 0f;
                foreach (string path in binding.Controls) width += ControlGlyphRenderer.Width(size, path);
                width += (binding.Controls.Length - 1) * (separatorWidth + gap * 2f);
                float x = (float)cue.State.Position.X * Screen.width - width * 0.5f;
                float y = (float)cue.State.Position.Y * Screen.height - size * 0.5f;
                Color saved = GUI.color;
                try
                {
                    GUI.color = new Color(saved.r, saved.g, saved.b,
                        saved.a * alpha * (cue.State.Enabled ? 1f : 0.48f));
                    var style = new GUIStyle(GUI.skin.label) {
                        fontSize = Mathf.Max(12, Mathf.RoundToInt(23f * scale)),
                        alignment = TextAnchor.MiddleCenter
                    };
                    JapaneseGuiFont.Apply(style);
                    style.normal.textColor = new Color(0.87f, 0.93f, 1f);
                    for (int i = 0; i < binding.Controls.Length; i++)
                    {
                        if (i > 0)
                        {
                            GUI.Label(new Rect(x + gap, y, separatorWidth, size),
                                binding.Mode == "chord" ? "+" : "/", style);
                            x += separatorWidth + gap * 2f;
                        }
                        string path = binding.Controls[i];
                        x += ControlGlyphRenderer.DrawControl(new Rect(x, y, size, size), path,
                            path.StartsWith("<Gamepad>"));
                    }
                    if (cue.State.Content == "control_label")
                    {
                        style.alignment = TextAnchor.MiddleLeft;
                        Vector2 labelSize = style.CalcSize(new GUIContent(cue.Definition.Label));
                        GUI.Label(new Rect(x + gap, y, labelSize.x, size), cue.Definition.Label, style);
                    }
                }
                finally { GUI.color = saved; }
            }
        }
    }
}
