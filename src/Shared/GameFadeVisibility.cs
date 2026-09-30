using System.Collections.Generic;
using System.Reflection;
using InstantHorror.Scripts.GameEventSystems.FadeInOut;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Disappearance.Shared
{
    // IMGUI and late-added canvas children can otherwise appear above the game's fade image.
    internal sealed class GameFadeVisibility
    {
        private static readonly FieldInfo FadeImageField = typeof(FadeInOutUIHandler).GetField(
            "fadeImage", BindingFlags.Instance | BindingFlags.NonPublic);
        internal static readonly GameFadeVisibility Shared = new GameFadeVisibility();
        private static readonly List<Image> images = new List<Image>();
        private static int sceneHandle = int.MinValue, discoveredFrame;
        private static float nextDiscover;

        internal float Alpha
        {
            get
            {
                Scene scene = SceneManager.GetActiveScene();
                if (sceneHandle != scene.handle)
                {
                    sceneHandle = scene.handle; images.Clear(); nextDiscover = 0;
                    discoveredFrame = Time.frameCount;
                }
                if (Time.unscaledTime >= nextDiscover)
                {
                    nextDiscover = Time.unscaledTime + .2f;
                    images.Clear();
                    if (FadeImageField != null)
                        foreach (FadeInOutUIHandler handler in Resources.FindObjectsOfTypeAll<FadeInOutUIHandler>())
                            if (handler != null && handler.gameObject.scene == scene &&
                                FadeImageField.GetValue(handler) is Image image && image != null)
                                images.Add(image);
                }
                // A newly loaded scene may not have finished Awake yet. Never flash
                // above its black frame before discovering the native fade image.
                if (images.Count == 0) return Time.frameCount == discoveredFrame ? 0f : 1f;
                float opacity = 0;
                foreach (Image image in images)
                {
                    if (image == null || !image.isActiveAndEnabled || image.gameObject.scene != scene) continue;
                    Canvas canvas = image.canvas;
                    if (canvas == null || !canvas.isActiveAndEnabled) continue;
                    float alpha = Mathf.Clamp01(image.color.a);
                    foreach (CanvasGroup group in image.GetComponentsInParent<CanvasGroup>())
                    {
                        alpha *= group.alpha;
                        if (group.ignoreParentGroups) break;
                    }
                    opacity = Mathf.Max(opacity, alpha);
                }
                return 1f - Mathf.Clamp01(opacity);
            }
        }

    }
}
