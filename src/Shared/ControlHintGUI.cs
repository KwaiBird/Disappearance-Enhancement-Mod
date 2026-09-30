using System.Collections.Generic;
using UnityEngine;

namespace Disappearance.Shared
{
    internal static class ControlHintGUI
    {
        private static readonly Dictionary<int, Texture2D> glyphs = new Dictionary<int, Texture2D>();

        internal static void Button(Rect rect, string label, bool controller)
        {
            ControlGlyphs.Shape shape = ControlGlyphs.ShapeFor(label, controller);
            GUI.DrawTexture(rect, Glyph(shape, rect));
            if (shape == ControlGlyphs.Shape.Mouse || shape == ControlGlyphs.Shape.Dpad ||
                shape >= ControlGlyphs.Shape.PlayStationCross) return;
            var style = new GUIStyle(GUI.skin.label) {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.RoundToInt(ControlGlyphs.FontSizeFor(label, rect.height))
            };
            style.normal.textColor = new Color(0.82f, 0.91f, 1f);
            GUI.Label(rect, label, style);
        }

        internal static void Stick(Rect rect, string label)
        {
            bool pressed = label == "L3";
            GUI.DrawTexture(rect, Glyph(pressed ? ControlGlyphs.Shape.StickPress :
                ControlGlyphs.Shape.Stick, rect));
            var style = new GUIStyle(GUI.skin.label) {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.RoundToInt(pressed ?
                    ControlGlyphs.StickPressFontSizeFor(rect.height) :
                    ControlGlyphs.StickFontSizeFor(rect.height))
            };
            style.normal.textColor = new Color(0.82f, 0.91f, 1f);
            GUI.Label(new Rect(rect.x, rect.y + rect.height * (0.5f -
                ControlGlyphs.StickLabelYOffset - ControlGlyphs.StickLabelHeight * 0.5f),
                rect.width, rect.height * ControlGlyphs.StickLabelHeight),
                pressed ? "L" : label, style);
        }

        internal static void Dpad(Rect rect)
        {
            GUI.DrawTexture(rect, Glyph(ControlGlyphs.Shape.Dpad, rect));
        }

        private static Texture2D Glyph(ControlGlyphs.Shape shape, Rect rect)
        {
            float referenceHeight = rect.height * 1080f / Mathf.Max(1f, Screen.height);
            int height = Mathf.RoundToInt(Mathf.Clamp(referenceHeight, 20f, 192f) / 4f) * 4;
            int aspect = shape == ControlGlyphs.Shape.Key ||
                shape == ControlGlyphs.Shape.ControllerWide ?
                Mathf.RoundToInt(Mathf.Clamp(rect.width / Mathf.Max(1f, rect.height), 0.7f, 4f) * 100f) : 100;
            int id = ((int)shape * 256 + height) * 1000 + aspect;
            Texture2D texture;
            if (!glyphs.TryGetValue(id, out texture) || texture == null)
            {
                texture = ControlGlyphs.CreateForDisplay(shape, height, aspect / 100f);
                glyphs[id] = texture;
            }
            return texture;
        }

        internal static void Dispose()
        {
            foreach (Texture2D texture in glyphs.Values)
                if (texture != null) Object.Destroy(texture);
            glyphs.Clear();
        }
    }
}
