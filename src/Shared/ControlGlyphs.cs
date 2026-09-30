using UnityEngine;

namespace Disappearance.Shared
{
    internal static class ControlGlyphs
    {
        private static Sprite playStationSouthSprite;

        internal enum Shape { Key, Controller, Mouse, Stick, ControllerWide, Dpad, StickPress,
            PlayStationCross, PlayStationCircle, PlayStationSquare, PlayStationTriangle }

        internal const float StickLabelYOffset = 0.025f;
        internal const float StickLabelHeight = 0.62f;
        internal static float StickFontSizeFor(float height) => Mathf.Max(10f, height * 0.33f);
        internal static float StickPressFontSizeFor(float height) => Mathf.Max(10f, height * 0.26f);

        // IMGUI hints and the TMP controls table use the same glyph decisions.
        internal static Shape ShapeFor(string label, bool controller)
        {
            if (label == "Mouse") return Shape.Mouse;
            if (!controller) return Shape.Key;
            if (label == "Dpad") return Shape.Dpad;
            switch (label)
            {
                case "×": return Shape.PlayStationCross;
                case "○": return Shape.PlayStationCircle;
                case "□": return Shape.PlayStationSquare;
                case "△": return Shape.PlayStationTriangle;
                default: return label.Length > 1 ? Shape.ControllerWide : Shape.Controller;
            }
        }

        internal static float WidthFor(string label, bool controller, float height)
        {
            if (ShapeFor(label, controller) == Shape.Mouse) return height * 54f / 64f;
            return label.Length <= 1 ? height :
                Mathf.Max(height, height * (0.45f + 0.4f * label.Length));
        }

        internal static float FontSizeFor(string label, float height) =>
            Mathf.Max(10f, height * (label.Length >= 3 ? 0.4f : 0.46f));

        internal static Sprite PlayStationSouthSprite
        {
            get
            {
                if (playStationSouthSprite == null)
                {
                    Texture2D texture = CreateForDisplay(Shape.PlayStationCross, 54f);
                    playStationSouthSprite = Sprite.Create(texture,
                        new Rect(0f, 0f, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f));
                }
                return playStationSouthSprite;
            }
        }

        internal static void DisposeSprites()
        {
            if (playStationSouthSprite == null) return;
            Object.Destroy(playStationSouthSprite.texture);
            Object.Destroy(playStationSouthSprite);
            playStationSouthSprite = null;
        }

        // Thresholds are distances, not output values. Unity Mathf.SmoothStep
        // interpolates from/to with a normalized t, so it is not GLSL smoothstep.
        internal static float SmoothCoverage(float start, float end, float distance)
        {
            float t = Mathf.Clamp01((distance - start) / (end - start));
            return t * t * (3f - 2f * t);
        }

        private static float SegmentDistance(float px, float py, float ax, float ay, float bx, float by)
        {
            float dx = bx - ax, dy = by - ay;
            float t = Mathf.Clamp01(((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy));
            float x = px - ax - dx * t, y = py - ay - dy * t;
            return Mathf.Sqrt(x * x + y * y);
        }

        private static float EllipseDistance(float px, float py, float rx, float ry)
        {
            // Approximate signed distance in the final pixel geometry. Scaling a
            // square ellipse texture would also scale its horizontal stroke.
            float k0 = Mathf.Sqrt(px * px / (rx * rx) + py * py / (ry * ry));
            float k1 = Mathf.Sqrt(px * px / (rx * rx * rx * rx) +
                py * py / (ry * ry * ry * ry));
            return k1 < 0.0001f ? -Mathf.Min(rx, ry) : k0 * (k0 - 1f) / k1;
        }

        // Glyph textures are drawn at several UI sizes. Preserve a legible line
        // width after a small icon is downscaled from the 192-pixel source.
        internal static Texture2D CreateForDisplay(Shape shape, float displayHeight,
            float keyAspect = 1f)
        {
            float height = Mathf.Max(20f, displayHeight);
            float outlineScale = Mathf.Clamp(Mathf.Pow(74f / height, 1.4f), 1f, 2.5f);
            return Create(shape, keyAspect, outlineScale);
        }

        internal static Texture2D Create(Shape shape, float keyAspect = 1f, float outlineScale = 1f)
        {
            const int side = 192;
            // Generate the key at its final aspect ratio. Stretching a square outline
            // or slicing its corners distorts narrow keys and wide labels.
            float aspect = shape == Shape.Key || shape == Shape.ControllerWide ?
                Mathf.Clamp(keyAspect, 0.7f, 4f) :
                shape == Shape.Mouse ? 54f / 64f : 1f;
            int width = Mathf.RoundToInt(side * aspect);
            var texture = new Texture2D(width, side, TextureFormat.RGBA32, false);
            texture.name = "ControlGlyph" + shape;
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color32[width * side];
            for (int y = 0; y < side; y++)
            for (int x = 0; x < width; x++)
            {
                float px = (x + 0.5f - width * 0.5f) * (96f / side);
                float py = (y + 0.5f - side * 0.5f) * (96f / side);
                float distance;
                if (shape == Shape.Key)
                {
                    float qx = Mathf.Abs(px) - (48f * aspect - 11f);
                    float qy = Mathf.Abs(py) - 37f;
                    distance = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) +
                        Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f)) + Mathf.Min(Mathf.Max(qx, qy), 0f) - 5f;
                }
                else if (shape == Shape.Controller || shape == Shape.Stick ||
                    shape == Shape.StickPress ||
                    shape >= Shape.PlayStationCross)
                    distance = Mathf.Sqrt(px * px + py * py) - 40f;
                else if (shape == Shape.ControllerWide)
                    distance = EllipseDistance(px, py, 43f * aspect, 33f);
                else if (shape == Shape.Mouse)
                {
                    float qx = Mathf.Abs(px) - 9f, qy = Mathf.Abs(py) - 20f;
                    distance = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) +
                        Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f)) +
                        Mathf.Min(Mathf.Max(qx, qy), 0f) - 22f;
                }
                else if (shape == Shape.Dpad) distance = 100f;
                else distance = (Mathf.Sqrt(px * px / (31f * 31f) + py * py / (42f * 42f)) - 1f) * 33f;
                // Key and controller casings share a quiet outline; their labels
                // and face symbols remain the primary marks.
                bool button = shape == Shape.Key || shape == Shape.Controller ||
                    shape == Shape.Stick || shape == Shape.StickPress ||
                    shape == Shape.ControllerWide || shape >= Shape.PlayStationCross;
                float outlineWidth = button ? 1.8f : 3.6f;
                float borderWidth = outlineWidth * outlineScale;
                float rim = 1f - SmoothCoverage(borderWidth * 0.25f, borderWidth, Mathf.Abs(distance));
                float glow = (1f - SmoothCoverage(0f, 6f, Mathf.Abs(distance))) * 0.32f;
                float alpha = button ? rim * .65f : Mathf.Clamp01(rim + glow);
                if (shape == Shape.Stick || shape == Shape.StickPress)
                {
                    // Two concentric circles distinguish a stick from a face button.
                    float stick = Mathf.Abs(Mathf.Sqrt(px * px + py * py) - 29f);
                    alpha = Mathf.Max(alpha, .75f *
                        (1f - SmoothCoverage(borderWidth * .25f, borderWidth, stick)));
                    if (shape == Shape.StickPress)
                    {
                        // The press indicator overlays the upper side of the L stick.
                        // The arrowhead clears the inner rim before reaching the L.
                        float arrow = Mathf.Min(SegmentDistance(px, py, 0f, 47f, 0f, 14.4f),
                            Mathf.Min(SegmentDistance(px, py, -7.2f, 20.8f, 0f, 14.4f),
                                SegmentDistance(px, py, 7.2f, 20.8f, 0f, 14.4f)));
                        alpha = Mathf.Max(alpha, .9f *
                            (1f - SmoothCoverage(borderWidth * .25f, borderWidth, arrow)));
                    }
                }
                if (shape == Shape.Dpad)
                {
                    // A directional cross has no face-button casing.
                    float vx = Mathf.Abs(px) - 6f, vy = Mathf.Abs(py) - 31f;
                    float hx = Mathf.Abs(px) - 31f, hy = Mathf.Abs(py) - 6f;
                    float vertical = Mathf.Sqrt(Mathf.Max(vx, 0f) * Mathf.Max(vx, 0f) +
                        Mathf.Max(vy, 0f) * Mathf.Max(vy, 0f)) + Mathf.Min(Mathf.Max(vx, vy), 0f);
                    float horizontal = Mathf.Sqrt(Mathf.Max(hx, 0f) * Mathf.Max(hx, 0f) +
                        Mathf.Max(hy, 0f) * Mathf.Max(hy, 0f)) + Mathf.Min(Mathf.Max(hx, hy), 0f);
                    alpha = Mathf.Max(alpha, .9f *
                        (1f - SmoothCoverage(-.5f, 1.5f, Mathf.Min(vertical, horizontal))));
                }
                if (shape >= Shape.PlayStationCross)
                {
                    // Leave more space between the face symbol and its button rim.
                    // Scale only the symbol geometry; keep its stroke width legible.
                    const float symbolScale = 0.85f;
                    float sx = px / symbolScale, sy = py / symbolScale;
                    float symbol = 100f;
                    if (shape == Shape.PlayStationCross)
                        symbol = Mathf.Min(SegmentDistance(sx, sy, -19f, -19f, 19f, 19f),
                            SegmentDistance(sx, sy, -19f, 19f, 19f, -19f));
                    else if (shape == Shape.PlayStationCircle)
                        symbol = Mathf.Abs(Mathf.Sqrt(sx * sx + sy * sy) - 20f);
                    else if (shape == Shape.PlayStationSquare)
                        symbol = Mathf.Abs(Mathf.Max(Mathf.Abs(sx), Mathf.Abs(sy)) - 20f);
                    else if (shape == Shape.PlayStationTriangle)
                        symbol = Mathf.Min(SegmentDistance(sx, sy, 0f, 23f, -22f, -18f),
                            Mathf.Min(SegmentDistance(sx, sy, -22f, -18f, 22f, -18f),
                                SegmentDistance(sx, sy, 22f, -18f, 0f, 23f)));
                    symbol *= symbolScale;
                    alpha = Mathf.Max(alpha, 1f - SmoothCoverage(1.4f * outlineScale,
                        3.1f * outlineScale, symbol));
                }
                if (shape == Shape.Mouse && py > 3f && py < 39f &&
                    Mathf.Abs(px) < 1.8f * outlineScale)
                    alpha = Mathf.Max(alpha, 0.9f);
                if (shape == Shape.Mouse && Mathf.Abs(py - 3f) < 1.8f * outlineScale &&
                    Mathf.Abs(px) < 29f)
                    alpha = Mathf.Max(alpha, 0.9f);
                pixels[y * width + x] = new Color(0.78f, 0.9f, 1f, alpha);
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        // VN prompts use the game's light filled button style. Keep this separate
        // from the outlined glyphs used by the controls table and HUD.
        internal static Texture2D CreateFilledButton(Shape shape, float keyAspect = 1f)
        {
            const int side = 192;
            float aspect = shape == Shape.Key ? Mathf.Clamp(keyAspect, 0.7f, 4f) : 1f;
            int width = Mathf.RoundToInt(side * aspect);
            var texture = new Texture2D(width, side, TextureFormat.RGBA32, false);
            texture.name = "FilledControlGlyph" + shape;
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color32[width * side];
            Color face = new Color(0.82f, 0.9f, 0.98f);
            Color rim = new Color(0.95f, 0.98f, 1f);
            Color symbolColor = new Color(0.19f, 0.25f, 0.31f);
            for (int y = 0; y < side; y++)
            for (int x = 0; x < width; x++)
            {
                float px = (x + 0.5f - width * 0.5f) * (96f / side);
                float py = (y + 0.5f - side * 0.5f) * (96f / side);
                float distance;
                if (shape == Shape.Key)
                {
                    float qx = Mathf.Abs(px) - (48f * aspect - 11f);
                    float qy = Mathf.Abs(py) - 37f;
                    distance = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) +
                        Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f)) + Mathf.Min(Mathf.Max(qx, qy), 0f) - 5f;
                }
                else
                    distance = Mathf.Sqrt(px * px + py * py) - 40f;
                float alpha = 1f - SmoothCoverage(-0.5f, 1.5f, distance);
                Color color = Color.Lerp(face, rim, 1f - SmoothCoverage(-5f, 1f, Mathf.Abs(distance)));
                if (shape == Shape.PlayStationCircle)
                {
                    float symbol = Mathf.Abs(Mathf.Sqrt(px * px + py * py) - 20f);
                    color = Color.Lerp(color, symbolColor,
                        1f - SmoothCoverage(1.4f, 3.1f, symbol));
                }
                color.a = alpha;
                pixels[y * width + x] = color;
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }
    }
}
