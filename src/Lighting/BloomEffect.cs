using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Disappearance.Lighting
{
    internal sealed class BloomEffect
    {
        private Bloom effect;
        private int sceneHandle;
        private bool active, thresholdOverride;
        private float threshold;
        internal bool Ready => effect != null && SceneManager.GetActiveScene().handle == sceneHandle;
        internal float EffectiveThreshold => Ready ? effect.threshold.value : 0;

        internal void Capture(Scene scene, LightingSettings settings)
        {
            if (Ready) return;
            Restore();
            if (scene.name != "VillageScene" || SceneManager.GetActiveScene().handle != scene.handle) return;
            foreach (Volume volume in Resources.FindObjectsOfTypeAll<Volume>())
            {
                if (volume == null || volume.gameObject.scene != scene || volume.gameObject.name != "PostProcess") continue;
                VolumeProfile profile = volume.profile;
                if (profile == null || !profile.TryGet(out Bloom found)) continue;
                effect = found;
                sceneHandle = scene.handle;
                active = found.active;
                threshold = found.threshold.value;
                thresholdOverride = found.threshold.overrideState;
                Apply(settings);
                return;
            }
        }

        private void Apply(LightingSettings settings)
        {
            if (!Ready) return;
            float offset = Mathf.Clamp(settings.BloomOffset, 0f, Mathf.Max(0f, 10f - threshold));
            effect.active = active;
            effect.threshold.overrideState = thresholdOverride || offset > 0f;
            effect.threshold.value = threshold + offset;
        }

        internal void Restore()
        {
            if (Ready)
            {
                effect.active = active;
                effect.threshold.value = threshold;
                effect.threshold.overrideState = thresholdOverride;
            }
            effect = null;
            sceneHandle = 0;
        }
    }
}
