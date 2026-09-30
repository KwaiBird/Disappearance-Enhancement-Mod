using TMPro;
using UnityEngine;

namespace Disappearance.Shared
{
    // Keep font assets, never a borrowed label: another view may rebuild its UI.
    internal sealed class MenuTextStyle
    {
        private readonly TMP_FontAsset font;
        private readonly Material material;

        internal MenuTextStyle(TMP_Text source)
        {
            font = source == null ? null : source.font;
            if (font == null) return;
            Material candidate = source.fontSharedMaterial;
            material = candidate != null && candidate.mainTexture == font.material.mainTexture
                ? candidate : font.material;
        }

        internal bool IsValid => font != null && material != null;
        internal string FontName => font == null ? "missing" : font.name;

        internal void Apply(TMP_Text text)
        {
            if (!IsValid || text == null) return;
            text.font = font;
            text.fontSharedMaterial = material;
        }

        internal static void ConfigureCanvas(Canvas target, Canvas source)
        {
            target.renderMode = RenderMode.ScreenSpaceOverlay;
            GameFadeCanvas.Attach(target);
            if (source != null)
            {
                target.targetDisplay = source.targetDisplay;
                target.sortingLayerID = source.sortingLayerID;
                target.sortingOrder = source.sortingOrder;
                target.pixelPerfect = source.pixelPerfect;
            }
            target.additionalShaderChannels = (source == null ? AdditionalCanvasShaderChannels.None : source.additionalShaderChannels) |
                AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
        }
    }
}
