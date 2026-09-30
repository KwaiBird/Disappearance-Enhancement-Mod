using UnityEngine;

namespace Disappearance.Shared
{
    // IMGUI otherwise falls through Unity's generic CJK fallback.  Select a
    // Japanese Windows face explicitly so Mod-owned Japanese text uses Japanese
    // glyph forms consistently with the rest of the localized UI.
    internal static class JapaneseGuiFont
    {
        private static readonly string[] PreferredFaces = {
            "Yu Gothic UI", "Yu Gothic", "Meiryo UI", "Meiryo",
            "BIZ UDPGothic", "MS Gothic"
        };

        private static Font cached;

        internal static Font Get()
        {
            if (cached != null) return cached;
            cached = Font.CreateDynamicFontFromOSFont(PreferredFaces, 32);
            return cached;
        }

        internal static void Apply(GUIStyle style)
        {
            if (style == null) return;
            Font font = Get();
            if (font != null) style.font = font;
        }
    }
}
