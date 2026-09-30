using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Disappearance.PerformanceV2
{
    // Captures and restores only properties owned by Performance v2.
    internal sealed partial class RenderToggles
    {
        private const string VineMaterial = "Tessen 1";
        private readonly Dictionary<MeshRenderer, ShadowCastingMode> vineShadows = new Dictionary<MeshRenderer, ShadowCastingMode>();
        private readonly Dictionary<Material, bool> vineInstancing = new Dictionary<Material, bool>();
        private readonly Dictionary<Light, LightShadows> pointShadows = new Dictionary<Light, LightShadows>();
        private readonly Dictionary<Light, LightShadows> sunShadows = new Dictionary<Light, LightShadows>();
        private readonly Dictionary<UniversalAdditionalCameraData, bool> cameraPostFx = new Dictionary<UniversalAdditionalCameraData, bool>();
        private readonly Action<string> log;
        private bool captured;
        private int sceneHandle;
        private int originalVSync;

        internal bool VineShadowsOff { get; private set; }
        internal bool VineInstancingOn { get; private set; }
        internal bool PointShadowsOff { get; private set; }
        internal bool SunShadowsOff { get; private set; }
        internal bool PostFxOff { get; private set; }
        internal bool VSyncOff { get; private set; }
        internal bool Ready => captured && sceneHandle == SceneManager.GetActiveScene().handle;
        internal int VineCount => vineShadows.Count;
        internal int CameraCount => cameraPostFx.Count;

        internal RenderToggles(PerformanceSettings settings, Action<string> log)
        {
            this.log = log;
            VineShadowsOff = settings.VineShadowsOff;
            VineInstancingOn = settings.VineInstancingOn;
            PointShadowsOff = settings.PointShadowsOff;
            SunShadowsOff = settings.SunShadowsOff;
            PostFxOff = settings.PostFxOff;
            VSyncOff = settings.VSyncOff;
        }

        internal void Capture(Scene scene)
        {
            Restore();
            if (scene.name != "VillageScene" || scene.handle != SceneManager.GetActiveScene().handle) return;
            sceneHandle = scene.handle;
            originalVSync = QualitySettings.vSyncCount;
            captured = true;
            try
            {
                foreach (MeshRenderer renderer in Resources.FindObjectsOfTypeAll<MeshRenderer>())
                {
                    if (renderer == null || renderer.gameObject.scene != scene) continue;
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        if (material == null || material.name != VineMaterial) continue;
                        if (!vineShadows.ContainsKey(renderer)) vineShadows.Add(renderer, renderer.shadowCastingMode);
                        if (!vineInstancing.ContainsKey(material)) vineInstancing.Add(material, material.enableInstancing);
                        break;
                    }
                }
                foreach (Light light in Resources.FindObjectsOfTypeAll<Light>())
                {
                    if (light == null || light.gameObject.scene != scene || light.shadows == LightShadows.None) continue;
                    if (light.type == LightType.Point) pointShadows.Add(light, light.shadows);
                    if (light.type == LightType.Directional) sunShadows.Add(light, light.shadows);
                }
                foreach (UniversalAdditionalCameraData camera in Resources.FindObjectsOfTypeAll<UniversalAdditionalCameraData>())
                    if (camera != null && camera.gameObject.scene == scene)
                        cameraPostFx.Add(camera, camera.renderPostProcessing);
                Apply();
                log("P6 render targets: vines=" + vineShadows.Count + ", vine materials=" + vineInstancing.Count +
                    ", point lights=" + pointShadows.Count + ", sun lights=" + sunShadows.Count +
                    ", cameras=" + cameraPostFx.Count + ", original vSync=" + originalVSync);
            }
            catch { Restore(); throw; }
        }

        internal void Toggle(Keyboard keyboard)
        {
            if (!Ready || keyboard == null) return;
            if (keyboard.f6Key.wasPressedThisFrame && sunShadows.Count > 0) { SunShadowsOff = !SunShadowsOff; Changed("F6 sun shadows=" + !SunShadowsOff); }
            if (keyboard.f7Key.wasPressedThisFrame && pointShadows.Count > 0) { PointShadowsOff = !PointShadowsOff; Changed("F7 point shadows=" + !PointShadowsOff); }
            if (keyboard.f8Key.wasPressedThisFrame && vineShadows.Count > 0) { VineShadowsOff = !VineShadowsOff; Changed("F8 vine shadows=" + !VineShadowsOff); }
            if (keyboard.f9Key.wasPressedThisFrame && vineInstancing.Count > 0) { VineInstancingOn = !VineInstancingOn; Changed("F9 vine instancing=" + VineInstancingOn); }
            if (keyboard.f10Key.wasPressedThisFrame && cameraPostFx.Count > 0) { PostFxOff = !PostFxOff; Changed("F10 camera PostFX=" + !PostFxOff); }
            if (keyboard.f11Key.wasPressedThisFrame) { VSyncOff = !VSyncOff; Changed("F11 vSync=" + !VSyncOff); }
        }

        private void Changed(string message) { Apply(); log(message + "; measuring a new window"); ComparisonChanged?.Invoke(); }
        internal event Action ComparisonChanged;

        private void Apply()
        {
            foreach (var entry in pointShadows) if (entry.Key != null) entry.Key.shadows = PointShadowsOff ? LightShadows.None : entry.Value;
            foreach (var entry in sunShadows) if (entry.Key != null) entry.Key.shadows = SunShadowsOff ? LightShadows.None : entry.Value;
            foreach (var entry in vineShadows) if (entry.Key != null) entry.Key.shadowCastingMode = VineShadowsOff ? ShadowCastingMode.Off : entry.Value;
            foreach (var entry in vineInstancing) if (entry.Key != null) entry.Key.enableInstancing = VineInstancingOn || entry.Value;
            foreach (var entry in cameraPostFx) if (entry.Key != null) entry.Key.renderPostProcessing = !PostFxOff && entry.Value;
            QualitySettings.vSyncCount = VSyncOff ? 0 : originalVSync;
        }

        internal void Restore()
        {
            foreach (var entry in pointShadows) if (entry.Key != null) entry.Key.shadows = entry.Value;
            foreach (var entry in sunShadows) if (entry.Key != null) entry.Key.shadows = entry.Value;
            foreach (var entry in vineShadows) if (entry.Key != null) entry.Key.shadowCastingMode = entry.Value;
            foreach (var entry in vineInstancing) if (entry.Key != null) entry.Key.enableInstancing = entry.Value;
            foreach (var entry in cameraPostFx) if (entry.Key != null) entry.Key.renderPostProcessing = entry.Value;
            if (captured) QualitySettings.vSyncCount = originalVSync;
            pointShadows.Clear(); sunShadows.Clear(); vineShadows.Clear(); vineInstancing.Clear(); cameraPostFx.Clear();
            captured = false;
        }
    }
}
