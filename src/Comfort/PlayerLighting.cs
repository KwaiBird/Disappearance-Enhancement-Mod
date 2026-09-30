using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Disappearance.Comfort
{
    internal sealed class PlayerLighting
    {
        private Light spot;
        private bool enabled, ambientCaptured;
        private float angle, innerAngle;
        private Scene scene;
        private AmbientMode ambientMode;
        private Color ambientColor;
        internal bool Ready => spot != null;
        internal float Intensity => Ready ? spot.intensity : 0;
        internal float Angle => Ready ? spot.spotAngle : 0;
        internal void Bind(PlayerLight player, Scene active)
        {
            Light next = player == null ? null : player.GetComponent<Light>();
            if (ReferenceEquals(next, spot)) return;
            ReleaseSpot();
            spot = next;
            if (spot == null) return;
            enabled = spot.enabled; angle = spot.spotAngle; innerAngle = spot.innerSpotAngle;
            if (!ambientCaptured) { scene = active; ambientMode = RenderSettings.ambientMode; ambientColor = RenderSettings.ambientLight; ambientCaptured = true; }
        }
        internal void Apply(ComfortConfigStore store, FieldOfView fov)
        {
            string mode = store.Get("Lights.PlayerIllumination", "Spot");
            if (spot != null)
            {
                spot.enabled = enabled && mode == "Spot";
                bool fit = mode == "Spot" && store.Get("Lights.MatchFlashlightToFov", "true") == "true" && fov != null && fov.Ready;
                var angles = FovSpotlightFit.Angles(angle, innerAngle, fov == null ? 0 : fov.Applied, fov == null ? 0 : fov.Baseline, fit);
                spot.spotAngle = angles.Outer;
                spot.innerSpotAngle = angles.Inner;
            }
            if (ambientCaptured && scene == SceneManager.GetActiveScene())
            {
                float b = (float)store.Number("Lights.AmbientBrightness", .18);
                RenderSettings.ambientMode = mode == "Ambient" ? AmbientMode.Flat : ambientMode;
                RenderSettings.ambientLight = mode == "Ambient" ? new Color(.9f * b, .95f * b, b, 1) : ambientColor;
            }
        }
        internal Action Snapshot()
        {
            Light light = spot; bool on = spot != null && spot.enabled;
            float outer = spot == null ? 0 : spot.spotAngle, inner = spot == null ? 0 : spot.innerSpotAngle;
            Scene active = SceneManager.GetActiveScene(); AmbientMode mode = RenderSettings.ambientMode; Color color = RenderSettings.ambientLight;
            return () => { if (light != null) { light.enabled = on; light.spotAngle = outer; light.innerSpotAngle = inner; } if (active == SceneManager.GetActiveScene() && active.isLoaded) { RenderSettings.ambientMode = mode; RenderSettings.ambientLight = color; } };
        }
        private void ReleaseSpot()
        {
            try { if (spot != null) { spot.enabled = enabled; spot.spotAngle = angle; spot.innerSpotAngle = innerAngle; } }
            finally { spot = null; }
        }
        internal void Release()
        {
            try { ReleaseSpot(); }
            finally
            {
                try { if (ambientCaptured && scene == SceneManager.GetActiveScene() && scene.isLoaded) { RenderSettings.ambientMode = ambientMode; RenderSettings.ambientLight = ambientColor; } }
                finally { ambientCaptured = false; }
            }
        }
    }
}
