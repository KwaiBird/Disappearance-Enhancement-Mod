using System.Collections.Generic;
using UnityEngine;

namespace Disappearance.Shared
{
    // Reusable IMGUI panel layout: measure every label before drawing the background.
    internal static class DiagnosticOverlayLayout
    {
        internal static Rect Draw(IReadOnlyList<string> lines, GUIStyle style, float top = 12f)
        {
            const float margin = 12f;
            const float padding = 14f;
            const float gap = 4f;
            float width = Mathf.Min(900f, Screen.width * .45f);
            float contentWidth = width - padding * 2f;
            float[] heights = new float[lines.Count];
            float totalHeight = padding * 2f;
            for (int i = 0; i < lines.Count; i++)
            {
                float measured = style.CalcHeight(new GUIContent(lines[i]), contentWidth);
                heights[i] = Mathf.Max(measured + style.fontSize * .35f, style.fontSize * 1.55f);
                totalHeight += heights[i] + (i == 0 ? 0f : gap);
            }
            Rect panel = new Rect(Screen.width - width - margin, top, width, totalHeight);
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, .82f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = previous;
            float y = panel.y + padding;
            for (int i = 0; i < lines.Count; i++)
            {
                GUI.Label(new Rect(panel.x + padding, y, contentWidth, heights[i]), lines[i], style);
                y += heights[i] + gap;
            }
            return panel;
        }
    }
}
