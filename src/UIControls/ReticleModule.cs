using System;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Disappearance.UIControls
{
    internal sealed class ReticleModule
    {
        private const float OutlineWidthAt1080p = 1f;
        private const float OutlineOpacity = 0.6f;
        private readonly ManualLogSource logger;
        private readonly ConfigEntry<float> diameter;
        private readonly ConfigEntry<float> opacity;
        private Image reticleImage;
        private CanvasGroup pauseVisibility;
        private PlayerSpawnHandler spawnHandler;
        private RectTransform reticleRect;
        private CanvasScaler canvasScaler;
        private Sprite originalSprite;
        private Color originalColor;
        private Image.Type originalImageType;
        private bool originalPreserveAspect;
        private bool originalRaycastTarget;
        private Vector2 originalSize;
        private Vector3 originalScale;
        private CanvasScaler.ScaleMode originalScaleMode;
        private Vector2 originalReference;
        private CanvasScaler.ScreenMatchMode originalMatchMode;
        private float originalMatch;
        private Sprite circleSprite;
        private Texture2D circleTexture;
        private float nextScanTime;

        internal ReticleModule(ConfigFile config, ManualLogSource logger)
        {
            this.logger = logger;
            diameter = config.Bind("Reticle", "DiameterAt1080p", 12f,
                new ConfigDescription("Solid circle diameter in pixels at 1920x1080; scales with resolution.",
                    new AcceptableValueRange<float>(4f, 40f)));
            opacity = config.Bind("Reticle", "Opacity", 0.2f,
                new ConfigDescription("White circle opacity.", new AcceptableValueRange<float>(0.1f, 1f)));
        }

        internal void Update()
        {
            if (SceneManager.GetActiveScene().name != "VillageScene" || reticleImage != null ||
                Time.unscaledTime < nextScanTime) return;
            nextScanTime = Time.unscaledTime + 1f;
            foreach (CenterCircleHandler handler in Resources.FindObjectsOfTypeAll<CenterCircleHandler>())
            {
                if (handler == null || !handler.gameObject.scene.isLoaded) continue;
                Image image = handler.GetComponent<Image>();
                RectTransform rect = handler.GetComponent<RectTransform>();
                CanvasScaler scaler = handler.GetComponentInParent<CanvasScaler>();
                if (image == null || rect == null || scaler == null ||
                    scaler.gameObject.name != "CenterCircleCanvas") continue;

                reticleImage = image;
                reticleRect = rect;
                canvasScaler = scaler;
                originalSprite = image.sprite;
                originalColor = image.color;
                originalImageType = image.type;
                originalPreserveAspect = image.preserveAspect;
                originalRaycastTarget = image.raycastTarget;
                originalSize = rect.sizeDelta;
                originalScale = rect.localScale;
                originalScaleMode = scaler.uiScaleMode;
                originalReference = scaler.referenceResolution;
                originalMatchMode = scaler.screenMatchMode;
                originalMatch = scaler.matchWidthOrHeight;

                try
                {
                    CreateCircleSprite();
                    image.sprite = circleSprite;
                    image.type = Image.Type.Simple;
                    image.preserveAspect = false;
                    image.color = Color.white;
                    image.raycastTarget = false;
                    // An owned visibility layer leaves CenterCircleHandler's game-state
                    // enabled/disabled decisions intact when the pause family closes.
                    pauseVisibility = image.gameObject.AddComponent<CanvasGroup>();
                    pauseVisibility.interactable = false;
                    pauseVisibility.blocksRaycasts = false;
                    spawnHandler = UnityEngine.Object.FindObjectOfType<PlayerSpawnHandler>();
                    UpdatePauseVisibility(null);
                    rect.localScale = Vector3.one;
                    float outlinedDiameter = diameter.Value + 2f * OutlineWidthAt1080p;
                    rect.sizeDelta = new Vector2(outlinedDiameter, outlinedDiameter);
                    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    scaler.referenceResolution = new Vector2(1920f, 1080f);
                    scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                    scaler.matchWidthOrHeight = 0f;
                    logger.LogInfo("Reticle replaced with an outlined circle at 1080p diameter " +
                        diameter.Value + ".");
                }
                catch
                {
                    Restore();
                    throw;
                }
                return;
            }
        }

        private void CreateCircleSprite()
        {
            const int size = 64;
            circleTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            circleTexture.name = "Disappearance Outlined Reticle";
            circleTexture.filterMode = FilterMode.Bilinear;
            circleTexture.wrapMode = TextureWrapMode.Clamp;
            circleTexture.hideFlags = HideFlags.DontSave;
            Color[] pixels = new Color[size * size];
            float outerRadius = size / 2f;
            float innerRadius = outerRadius * diameter.Value /
                (diameter.Value + 2f * OutlineWidthAt1080p);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - size / 2f;
                    float dy = y + 0.5f - size / 2f;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    float outerCoverage = Mathf.Clamp01(outerRadius - distance);
                    float innerCoverage = Mathf.Clamp01(innerRadius - distance);
                    float whiteAlpha = innerCoverage * opacity.Value;
                    float darkAlpha = (outerCoverage - innerCoverage) * OutlineOpacity;
                    float alpha = whiteAlpha + darkAlpha;
                    float white = alpha > 0f ? whiteAlpha / alpha : 0f;
                    pixels[y * size + x] = new Color(white, white, white, alpha);
                }
            }
            circleTexture.SetPixels(pixels);
            circleTexture.Apply(false, true);
            circleSprite = Sprite.Create(circleTexture, new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f), size);
            if (circleSprite == null) throw new InvalidOperationException("Circle sprite creation failed.");
            circleSprite.hideFlags = HideFlags.DontSave;
        }

        internal void UpdatePauseVisibility(PauseMenuHandler menu)
        {
            if (spawnHandler == null && reticleImage != null)
                spawnHandler = UnityEngine.Object.FindObjectOfType<PlayerSpawnHandler>();
            if (pauseVisibility != null)
                // Native movement is gated by CanMove during spawn preparation.
                // CenterCircleHandler still owns conversation/event visibility.
                pauseVisibility.alpha = spawnHandler == null || !spawnHandler.CanMove ||
                    (menu != null && menu.CurrentPauseStatus == PauseMenuHandler.PauseStatus.Paused) ? 0f : 1f;
        }

        internal void OnSceneChanged()
        {
            Restore();
            nextScanTime = 0f;
        }

        internal void Stop()
        {
            Restore();
        }

        private void Restore()
        {
            if (pauseVisibility != null)
            {
                pauseVisibility.alpha = 1f;
                UnityEngine.Object.Destroy(pauseVisibility);
                pauseVisibility = null;
            }
            if (reticleImage != null)
            {
                reticleImage.sprite = originalSprite;
                reticleImage.color = originalColor;
                reticleImage.type = originalImageType;
                reticleImage.preserveAspect = originalPreserveAspect;
                reticleImage.raycastTarget = originalRaycastTarget;
            }
            if (reticleRect != null)
            {
                reticleRect.sizeDelta = originalSize;
                reticleRect.localScale = originalScale;
            }
            if (canvasScaler != null)
            {
                canvasScaler.uiScaleMode = originalScaleMode;
                canvasScaler.referenceResolution = originalReference;
                canvasScaler.screenMatchMode = originalMatchMode;
                canvasScaler.matchWidthOrHeight = originalMatch;
            }
            reticleImage = null;
            spawnHandler = null;
            reticleRect = null;
            canvasScaler = null;
            if (circleSprite != null) UnityEngine.Object.Destroy(circleSprite);
            if (circleTexture != null) UnityEngine.Object.Destroy(circleTexture);
            circleSprite = null;
            circleTexture = null;
        }
    }
}
