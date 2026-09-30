using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Disappearance.Comfort
{
    // Screen-edge comfort is kept with the player presentation owner.
    internal sealed class VignetteEffect
    {
        private Vignette effect;
        private int sceneHandle;
        private bool active;
        private float intensity, smoothness;
        internal bool Ready => effect != null && SceneManager.GetActiveScene().handle == sceneHandle;
        internal bool Active => Ready && effect.active;

        internal void Bind(Scene scene, ComfortConfigStore store)
        {
            if (Ready) return;
            Release();
            if (scene.name != "VillageScene" || SceneManager.GetActiveScene().handle != scene.handle) return;
            foreach (Volume volume in Resources.FindObjectsOfTypeAll<Volume>())
            {
                if (volume == null || volume.gameObject.scene != scene || volume.gameObject.name != "PostProcess") continue;
                VolumeProfile profile = volume.profile;
                if (profile == null || !profile.TryGet(out Vignette found)) continue;
                effect = found;
                sceneHandle = scene.handle;
                active = found.active;
                intensity = found.intensity.value;
                smoothness = found.smoothness.value;
                Apply(store);
                return;
            }
        }

        internal void Apply(ComfortConfigStore store)
        {
            if (!Ready) return;
            effect.active = active && store.Get("Rendering.DisableVignette", "false") != "true";
            effect.intensity.value = (float)FovMath.Clamp(store.Number("Rendering.VignetteIntensity", .35), 0, 1);
            effect.smoothness.value = (float)FovMath.Clamp(store.Number("Rendering.VignetteSmoothness", .65), .01, 1);
        }

        internal void Release()
        {
            if (Ready)
            {
                effect.active = active;
                effect.intensity.value = intensity;
                effect.smoothness.value = smoothness;
            }
            effect = null;
            sceneHandle = 0;
        }
    }
}
