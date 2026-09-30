using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace Disappearance.Shared
{
    // Copy the game's Japanese font and material, never modify their shared assets.
    internal sealed class NativeMenuCanvas : IDisposable
    {
        private GameObject root;
        private readonly List<TMP_Text> labels = new List<TMP_Text>();
        private MenuTextStyle textStyle;
        private readonly Action<string> log;
        private Sprite triangle;
        private readonly List<Image> pointers = new List<Image>();
        private Image background;
        private TMP_Text measurement;
        private int used;
        private float captureAfter;
        private bool captured;
        private RectTransform nativeCanvasRect;
        private Camera nativeCamera;
        private bool inheritEffects = true;

        internal NativeMenuCanvas(Action<string> log = null) { this.log = log; }

        internal void Begin(Transform source, bool inheritEffects = true)
        {
            if (root == null || root.scene != SceneManager.GetActiveScene() || this.inheritEffects != inheritEffects)
            {
                Dispose();
                this.inheritEffects = inheritEffects;
                TMP_Text template = source == null ? null : source.GetComponentInChildren<TMP_Text>(true);
                // Title buttons are baked sprites, whereas the credit panel and
                // village menus contain the matching Zen Old Mincho SDF face.
                foreach (TMP_Text candidate in Resources.FindObjectsOfTypeAll<TMP_Text>())
                    if (candidate != null && candidate.gameObject.scene == SceneManager.GetActiveScene() &&
                        candidate.font != null && candidate.font.name.Contains("ZenOldMincho"))
                    { template = candidate; break; }
                if (template == null) return;
                textStyle = new MenuTextStyle(template);
                if (!textStyle.IsValid) return;
                foreach (Image image in Resources.FindObjectsOfTypeAll<Image>())
                    if (image != null && image.gameObject.scene == SceneManager.GetActiveScene() &&
                        image.name == "Triangle" && image.sprite != null)
                    { triangle = image.sprite; break; }
                Canvas original = template.GetComponentInParent<Canvas>();
                Canvas ownerCanvas = source == null ? null : source.GetComponentInParent<Canvas>()?.rootCanvas;
                Transform fadeChild = ownerCanvas == null ? null : FindFadeChild(ownerCanvas.transform);
                string sceneName = SceneManager.GetActiveScene().name;
                bool nativeMenu = inheritEffects && (sceneName == "TitleScene" || sceneName == "VillageScene") &&
                    ownerCanvas != null && ownerCanvas.renderMode == RenderMode.ScreenSpaceCamera &&
                    ownerCanvas.worldCamera != null && ownerCanvas.isActiveAndEnabled && fadeChild != null;
                root = new GameObject("GameplayQoL_NativeMenu", typeof(RectTransform));
                if (nativeMenu)
                {
                    nativeCanvasRect = ownerCanvas.GetComponent<RectTransform>();
                    nativeCamera = ownerCanvas.worldCamera;
                    root.transform.SetParent(ownerCanvas.transform, false);
                    RectTransform host = (RectTransform)root.transform;
                    host.anchorMin = Vector2.zero; host.anchorMax = Vector2.one;
                    host.offsetMin = host.offsetMax = Vector2.zero;
                    root.transform.SetSiblingIndex(fadeChild.GetSiblingIndex());
                    log?.Invoke("Native " + sceneName + " menu attached below FadeInOut on " + ownerCanvas.name + ".");
                }
                else
                {
                    SceneManager.MoveGameObjectToScene(root, SceneManager.GetActiveScene());
                    var canvas = root.AddComponent<Canvas>();
                    MenuTextStyle.ConfigureCanvas(canvas, original);
                    log?.Invoke("Native " + sceneName + " canvas unavailable; using Overlay fallback.");
                }
                var measureObject = new GameObject("Measurement", typeof(RectTransform), typeof(TextMeshProUGUI));
                measureObject.transform.SetParent(root.transform, false);
                measurement = measureObject.GetComponent<TMP_Text>();
                textStyle.Apply(measurement);
                measurement.enableWordWrapping = false;
                measurement.richText = false;
                measurement.color = Color.clear;
                measurement.raycastTarget = false;
                captureAfter = Time.realtimeSinceStartup + 3f;
                captured = false;
                var panel = new GameObject("Background", typeof(RectTransform), typeof(Image));
                panel.transform.SetParent(root.transform, false);
                background = panel.GetComponent<Image>();
                background.raycastTarget = false;
                var rect = background.rectTransform;
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                log?.Invoke("Native menu assets: font=" + textStyle.FontName +
                    ", triangle=" + (triangle == null ? "missing" : triangle.name) + ".");
            }
            root.SetActive(true);
            used = 0;
        }

        internal void Background(Color color) { if (background != null) background.color = color; }

        internal float PreferredWidth(string value, float fontSize)
        {
            if (measurement == null || string.IsNullOrEmpty(value)) return 0f;
            measurement.fontSize = NativeLength(fontSize);
            return measurement.GetPreferredValues(value).x * (nativeCanvasRect == null ? 1f : Screen.width / nativeCanvasRect.rect.width);
        }

        private static Transform FindFadeChild(Transform canvas)
        {
            foreach (Transform node in canvas.GetComponentsInChildren<Transform>(true))
            {
                if (node.name != "FadeInOut") continue;
                Transform child = node;
                while (child.parent != null && child.parent != canvas) child = child.parent;
                if (child.parent == canvas) return child;
            }
            return null;
        }

        private float NativeLength(float pixels)
        {
            return nativeCanvasRect == null ? pixels : pixels * nativeCanvasRect.rect.width / Screen.width;
        }

        private Rect NativeRect(Rect screen)
        {
            if (nativeCanvasRect == null) return screen;
            Vector2 topLeft, bottomRight;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(nativeCanvasRect,
                    new Vector2(screen.xMin, Screen.height - screen.yMin), nativeCamera, out topLeft) ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(nativeCanvasRect,
                    new Vector2(screen.xMax, Screen.height - screen.yMax), nativeCamera, out bottomRight))
                return new Rect();
            return new Rect(topLeft.x - nativeCanvasRect.rect.xMin,
                nativeCanvasRect.rect.yMax - topLeft.y, bottomRight.x - topLeft.x, topLeft.y - bottomRight.y);
        }

        internal void Label(Rect rect, string text, GUIStyle style, Color color)
        {
            if (root == null || textStyle == null || !textStyle.IsValid) return;
            rect = NativeRect(rect);
            float fontSize = NativeLength(style.fontSize);
            if (used == labels.Count)
            {
                var obj = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
                obj.transform.SetParent(root.transform, false);
                var label = obj.GetComponent<TMP_Text>();
                textStyle.Apply(label);
                label.raycastTarget = false;
                label.enableWordWrapping = false;
                label.richText = true;
                labels.Add(label);
                var pointer = new GameObject("Triangle", typeof(RectTransform), typeof(Image));
                pointer.transform.SetParent(root.transform, false);
                var icon = pointer.GetComponent<Image>();
                icon.sprite = triangle; icon.color = Color.red; icon.raycastTarget = false;
                pointers.Add(icon);
            }
            TMP_Text target = labels[used];
            Image arrow = pointers[used++];
            bool selected = text.StartsWith("<color=#FF0000>▶</color>  ", StringComparison.Ordinal);
            bool row = selected || text.StartsWith("    ", StringComparison.Ordinal);
            arrow.gameObject.SetActive(selected && triangle != null);
            if (row)
            {
                text = selected ? text.Substring("<color=#FF0000>▶</color>  ".Length) : text.Substring(4);
                RectTransform icon = arrow.rectTransform;
                icon.anchorMin = icon.anchorMax = icon.pivot = new Vector2(0, 1);
                icon.sizeDelta = new Vector2(fontSize, fontSize);
                icon.anchoredPosition = new Vector2(rect.x, -rect.y - (rect.height - fontSize) * .5f);
                rect.x += fontSize * 1.6f; rect.width -= fontSize * 1.6f;
            }
            target.gameObject.SetActive(true);
            var transform = target.rectTransform;
            transform.anchorMin = transform.anchorMax = transform.pivot = new Vector2(0, 1);
            transform.anchoredPosition = new Vector2(rect.x, -rect.y);
            transform.sizeDelta = rect.size;
            target.fontSize = fontSize;
            target.color = color;
            target.alignment = style.alignment == TextAnchor.MiddleCenter ? TextAlignmentOptions.Center : TextAlignmentOptions.MidlineLeft;
            target.text = text;
        }

        internal void End()
        {
            for (int i = used; i < labels.Count; i++) { labels[i].gameObject.SetActive(false); pointers[i].gameObject.SetActive(false); }
            // Opt-in game-frame evidence for developer verification only.
            string capturePath = Environment.GetEnvironmentVariable("DISAPPEARANCE_UI_CAPTURE");
            if (!captured && root != null && used > 0 && Time.realtimeSinceStartup >= captureAfter && !string.IsNullOrEmpty(capturePath))
            {
                captured = true;
                foreach (TMP_Text label in labels)
                {
                    label.ForceMeshUpdate();
                    Material material = label.fontSharedMaterial;
                    Texture texture = material == null ? null : material.mainTexture;
                    log?.Invoke("Menu text diagnostic: text=" + label.text + ", font=" + label.font.name +
                        ", atlas=" + label.font.atlasWidth + "x" + label.font.atlasHeight +
                        ", material=" + (material == null ? "null" : material.name) +
                        ", shader=" + (material == null ? "null" : material.shader.name) +
                        ", texture=" + (texture == null ? "null" : texture.name + " " + texture.width + "x" + texture.height) +
                        ", glyphs=" + label.textInfo.characterCount);
                }
                ScreenCapture.CaptureScreenshot(capturePath);
                log?.Invoke("Native menu frame captured: " + capturePath);
            }
        }
        internal void Hide() { if (root != null) root.SetActive(false); }
        public void Dispose() { if (root != null) UnityEngine.Object.Destroy(root); root = null; textStyle = null; background = null; measurement = null; nativeCanvasRect = null; nativeCamera = null; triangle = null; labels.Clear(); pointers.Clear(); }
    }
}
