using System;
using System.IO;
using BepInEx;
using BepInEx.Unity.Mono;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Disappearance.Lighting
{
    [BepInPlugin(PluginId, "Disappearance Lighting", "0.1.9")]
    public sealed partial class LightingPlugin : BaseUnityPlugin
    {
        public const string PluginId = "local.disappearance.lighting";
        private LightingSettings settings;
        private readonly BloomEffect bloom = new BloomEffect();
        private readonly TvCrewCameraGlare crew = new TvCrewCameraGlare();
        private readonly SceneLightAtmosphere atmosphere = new SceneLightAtmosphere();
        private readonly BridgePoleLodAlignment bridgePole = new BridgePoleLodAlignment();
        private Scene scene;
        private bool captured, bloomFailed, crewFailed, atmosphereFailed, bridgePoleFailed, stopped;
        private float nextCapture;

        private void Awake()
        {
            try
            {
                settings = new LightingSettings(Path.Combine(Paths.ConfigPath, "local.disappearance.lighting.cfg"), Logger.LogInfo);
                settings.Migrate();
            }
            catch (Exception error) { Logger.LogError("Lighting settings unavailable: " + error); stopped = true; return; }
            scene = SceneManager.GetActiveScene();
            SceneManager.activeSceneChanged += OnSceneChanged;
            nextCapture = Time.unscaledTime + .5f;
            Logger.LogInfo("Lighting P5 loaded: Bloom, scene atmosphere, TV crew camera.");
        }

        private void OnSceneChanged(Scene previous, Scene current)
        {
            Release();
            scene = current;
            captured = bloomFailed = crewFailed = atmosphereFailed = bridgePoleFailed = false;
            nextCapture = Time.unscaledTime + .5f;
        }

        private void Capture()
        {
            if (captured || scene.name != "VillageScene" || scene.handle != SceneManager.GetActiveScene().handle || Time.unscaledTime < nextCapture) return;
            captured = true;
            Attempt(ref bloomFailed, () => bloom.Capture(scene, settings), bloom.Restore, "Bloom");
            Attempt(ref crewFailed, () => crew.Apply(scene), crew.Restore, "TV crew camera");
            if (settings.EnableLightAtmosphere)
                Attempt(ref atmosphereFailed, () => atmosphere.Capture(scene, message => Logger.LogInfo(message)), atmosphere.Restore, "Scene light atmosphere");
            Attempt(ref bridgePoleFailed, () => bridgePole.Capture(scene, message => Logger.LogInfo(message)),
                bridgePole.Restore, "Bridge pole LOD1 alignment");
            Logger.LogInfo("Lighting targets: bloom=" + bloom.Ready + ", camera=" + crew.RendererCount + ", halos=" + atmosphere.HaloCount +
                " (village=" + atmosphere.VillageHaloCount + ", tunnel=" + atmosphere.TunnelHaloCount +
                "), beams=" + atmosphere.BeamCount + ".");
        }

        private void Attempt(ref bool failed, Action apply, Action restore, string label)
        {
            if (failed) return;
            try { apply(); }
            catch (Exception error)
            {
                failed = true;
                try { restore(); } catch (Exception rollback) { Logger.LogError(label + " restore failed: " + rollback); }
                Logger.LogError(label + " failed independently: " + error);
            }
        }

        private void Update()
        {
            if (stopped) return;
            Scene current = SceneManager.GetActiveScene();
            if (current.handle != scene.handle) OnSceneChanged(scene, current);
            Capture();
#if P5_HALO_DIAGNOSTICS
            PollHaloComparison();
#endif
        }

        private void LateUpdate()
        {
            if (captured && !atmosphereFailed && settings.EnableLightAtmosphere && scene.handle == SceneManager.GetActiveScene().handle)
                Attempt(ref atmosphereFailed, atmosphere.Update, atmosphere.Restore, "Scene light atmosphere update");
        }

        private void Release()
        {
            foreach (Action restore in new Action[] { bloom.Restore, crew.Restore, atmosphere.Restore, bridgePole.Restore })
                try { restore(); } catch (Exception error) { Logger.LogError("Lighting release: " + error); }
        }

        private void OnDisable() => Stop();
        private void OnDestroy() => Stop();
        private void Stop()
        {
            if (stopped) return;
            stopped = true;
            SceneManager.activeSceneChanged -= OnSceneChanged;
            Release();
        }
    }
}
