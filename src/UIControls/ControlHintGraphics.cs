using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Disappearance.Shared;

namespace Disappearance.UIControls
{
    // Reusable, text-driven versions of the game's outlined control glyphs.
    // The source pause image is left intact; labels remain editable UI text.
    internal static class ControlHintGraphics
    {
        private static readonly Dictionary<int, Sprite> glyphs = new Dictionary<int, Sprite>();
        internal static readonly Color Ink = new Color(0.82f, 0.91f, 1f);

        internal static RectTransform Rect(Transform parent, string name, float x, float y, float width, float height)
        {
            RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
            rect.localScale = Vector3.one;
            return rect;
        }

        internal static TMP_Text Text(TMP_Text source, Transform parent, string name, string value,
            float x, float y, float width, float height, float size, TextAlignmentOptions alignment)
        {
            var rect = Rect(parent, name, x, y, width, height);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = source.font;
            text.fontSharedMaterial = source.fontSharedMaterial;
            text.fontStyle = source.fontStyle;
            text.color = Ink;
            text.fontSize = size;
            text.enableAutoSizing = true;
            text.fontSizeMin = size * 0.7f;
            text.fontSizeMax = size;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Truncate;
            text.alignment = alignment;
            text.raycastTarget = false;
            text.text = value;
            return text;
        }

        internal static void Icon(TMP_Text source, Transform parent, string name, string value,
            float x, float y, float width, float height, bool controller = false)
        {
            var rect = Rect(parent, name, x, y, width, height);
            var image = rect.gameObject.AddComponent<Image>();
            ControlGlyphs.Shape shape = ControlGlyphs.ShapeFor(value, controller);
            image.sprite = GlyphSprite(shape, height, width / height);
            image.type = Image.Type.Simple;
            image.color = Color.white;
            image.raycastTarget = false;
            // PlayStation face symbols are already part of their sprites.
            if (shape == ControlGlyphs.Shape.Mouse || shape == ControlGlyphs.Shape.Dpad ||
                shape >= ControlGlyphs.Shape.PlayStationCross) return;
            GlyphText(source, rect, "Glyph", value, width - 12, height - 8, height);
        }

        internal static void ControllerIcon(TMP_Text source, Transform parent, string name, string value,
            float x, float y, float size)
        {
            if (value == "L" || value == "R" || value == "L3")
            {
                var rect = Rect(parent, name, x, y, size, size);
                var image = rect.gameObject.AddComponent<Image>();
                image.sprite = GlyphSprite(value == "L3" ? ControlGlyphs.Shape.StickPress :
                    ControlGlyphs.Shape.Stick, size);
                image.raycastTarget = false;
                TMP_Text label = Text(source, rect, "StickSide", value == "L3" ? "L" : value,
                    0, size * ControlGlyphs.StickLabelYOffset, size - 12,
                    size * ControlGlyphs.StickLabelHeight,
                    value == "L3" ? ControlGlyphs.StickPressFontSizeFor(size) :
                    ControlGlyphs.StickFontSizeFor(size), TextAlignmentOptions.Center);
                label.enableAutoSizing = false;
                label.overflowMode = TextOverflowModes.Overflow;
                return;
            }

            Icon(source, parent, name, value, x, y,
                ControlGlyphs.WidthFor(value, true, size), size, true);
        }

        private static void GlyphText(TMP_Text source, Transform parent, string name,
            string value, float width, float height, float iconHeight)
        {
            float size = ControlGlyphs.FontSizeFor(value, iconHeight);
            TMP_Text label = Text(source, parent, name, value, 0, 0, width, height,
                size, TextAlignmentOptions.Center);
            label.enableAutoSizing = false;
        }

        internal static void MouseIcon(Transform parent, float x, float y, float width = 54, float height = 64)
        {
            var rect = Rect(parent, "Mouse", x, y, width, height);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = GlyphSprite(ControlGlyphs.Shape.Mouse, height);
            image.raycastTarget = false;
        }

        internal static void Dots(Transform parent, string name, float left, float right, float y)
        {
            // Fixed pitch makes the leader look like the original pause image.
            for (float x = left; x < right; x += 5.5f)
            {
                var dot = Rect(parent, name, x, y, 2f, 2f);
                var image = dot.gameObject.AddComponent<Image>();
                image.color = Ink;
                image.raycastTarget = false;
            }
        }

        private static Sprite GlyphSprite(ControlGlyphs.Shape shape, float height, float aspect = 1f)
        {
            int heightId = Mathf.RoundToInt(Mathf.Clamp(height, 20f, 192f) / 4f) * 4;
            int aspectId = shape == ControlGlyphs.Shape.Key ||
                shape == ControlGlyphs.Shape.ControllerWide ?
                Mathf.RoundToInt(Mathf.Clamp(aspect, 0.7f, 4f) * 100f) : 100;
            int id = ((int)shape * 256 + heightId) * 1000 + aspectId;
            Sprite sprite;
            if (!glyphs.TryGetValue(id, out sprite) || sprite == null)
            {
                var texture = ControlGlyphs.CreateForDisplay(shape, heightId, aspectId / 100f);
                sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect);
                glyphs[id] = sprite;
            }
            return sprite;
        }
        internal static void Dispose()
        {
            foreach (var sprite in glyphs.Values)
                if (sprite != null) { Object.Destroy(sprite.texture); Object.Destroy(sprite); }
            glyphs.Clear();
        }
    }
}

