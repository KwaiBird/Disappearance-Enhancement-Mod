using UnityEngine;

namespace Disappearance.Shared
{
    // Attached only to mod-created menu canvases. Native fades and game UI are
    // read-only; destroying the owned canvas removes this visibility layer.
    internal sealed class GameFadeCanvas : MonoBehaviour
    {
        private CanvasGroup visibility;
        internal static void Attach(Canvas canvas)
        {
            if (canvas.GetComponent<GameFadeCanvas>() == null) canvas.gameObject.AddComponent<GameFadeCanvas>();
        }
        private void Awake()
        {
            visibility = gameObject.AddComponent<CanvasGroup>();
            visibility.alpha = 0f;
        }
        private void OnEnable() { if (visibility != null) visibility.alpha = 0f; Canvas.willRenderCanvases += Apply; }
        private void OnDisable() { Canvas.willRenderCanvases -= Apply; }
        private void Apply() { if (visibility != null) visibility.alpha = GameFadeVisibility.Shared.Alpha; }
    }
}
