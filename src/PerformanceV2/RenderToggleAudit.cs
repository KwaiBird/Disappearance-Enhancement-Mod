using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Disappearance.PerformanceV2
{
    // Opt-in, read-only evidence. Never reapply properties to conceal an external overwrite.
    internal sealed class RenderToggleAudit : IDisposable
    {
        private readonly RenderToggles toggles;
        private readonly Action<string> log;
        private int sequence, stage;
        private float started;

        internal RenderToggleAudit(RenderToggles toggles, Action<string> log)
        {
            this.toggles = toggles; this.log = log;
            toggles.ComparisonChanged += Schedule;
            RenderPipelineManager.endCameraRendering += AfterCamera;
            log("P6 render audit enabled: readback immediately, after game-camera render, and after 1 second. Audit windows are not benchmarks.");
        }

        internal void Schedule()
        {
            sequence++; stage = 1; started = Time.unscaledTime;
            Read("applied", null, false);
        }

        internal void Tick()
        {
            if (stage == 0 || !toggles.Ready || Time.unscaledTime - started < 3f) return;
            stage = 0;
            log("P6 audit: no eligible game-camera callback completed the delayed readback; using LateUpdate (not render proof).");
            Read("after-3s-no-render-callback", Camera.main, true);
        }

        private void AfterCamera(ScriptableRenderContext context, Camera camera)
        {
            if (stage == 0 || !toggles.Ready || camera == null || camera.cameraType != CameraType.Game ||
                camera.targetTexture != null || camera.gameObject.scene != SceneManager.GetActiveScene()) return;
            if (stage == 1) { stage = 2; Read("after-render", camera, false); }
            else if (Time.unscaledTime - started >= 1f) { stage = 0; Read("after-1s-render", camera, true); }
        }

        private void Read(string phase, Camera camera, bool scan)
        {
            try { toggles.WriteAudit(message => log("P6 audit #" + sequence + " " + phase + ": " + message), camera, scan); }
            catch (Exception error) { stage = 0; log("P6 audit failed (comparison unchanged): " + error); }
        }

        public void Dispose()
        {
            toggles.ComparisonChanged -= Schedule;
            RenderPipelineManager.endCameraRendering -= AfterCamera;
            stage = 0;
        }
    }

    internal sealed partial class RenderToggles
    {
        internal void WriteAudit(Action<string> write, Camera camera, bool scan)
        {
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            write("scene=" + SceneManager.GetActiveScene().name + ", frame=" + Time.frameCount +
                ", camera=" + (camera == null ? "none" : Path(camera.transform) + " pos=" + camera.transform.position +
                " rotation=" + camera.transform.eulerAngles + " fov=" + camera.fieldOfView + " mask=" + camera.cullingMask) +
                ", pipeline=" + (pipeline == null ? "not-URP" : pipeline.name + " mainShadows=" + pipeline.supportsMainLightShadows +
                " additionalShadows=" + pipeline.supportsAdditionalLightShadows + " shadowDistance=" + pipeline.shadowDistance +
                " srpBatcher=" + pipeline.useSRPBatcher) + ", instancingSupported=" + SystemInfo.supportsInstancing +
                ", ambient=" + RenderSettings.ambientMode + "/" + RenderSettings.ambientLight);
            AuditLights(write, "F6", sunShadows, SunShadowsOff, camera, scan);
            AuditLights(write, "F7", pointShadows, PointShadowsOff, camera, scan);
            int live = 0, mismatch = 0, active = 0, visible = 0, originalOff = 0, staticBatch = 0, materialsReplaced = 0;
            foreach (var entry in vineShadows)
            {
                MeshRenderer renderer = entry.Key;
                if (renderer == null) continue;
                live++;
                if (entry.Value == ShadowCastingMode.Off) originalOff++;
                if (renderer.shadowCastingMode != (VineShadowsOff ? ShadowCastingMode.Off : entry.Value)) mismatch++;
                if (renderer.enabled && renderer.gameObject.activeInHierarchy) active++;
                if (renderer.isVisible) visible++;
                if (renderer.isPartOfStaticBatch) staticBatch++;
                bool retained = false;
                foreach (Material material in renderer.sharedMaterials)
                    if (material != null && vineInstancing.ContainsKey(material)) retained = true;
                if (!retained) materialsReplaced++;
            }
            write("F8 requestedOff=" + VineShadowsOff + ", tracked=" + vineShadows.Count + ", live=" + live +
                ", active=" + active + ", visibleAnyCamera=" + visible + ", originalOff=" + originalOff +
                ", mismatches=" + mismatch + ", staticBatch=" + staticBatch + ", noCapturedMaterial=" + materialsReplaced);
            foreach (var entry in vineInstancing)
            {
                Material material = entry.Key;
                if (material == null) { write("F9 captured material destroyed"); continue; }
                write("F9 material=" + material.name + "#" + material.GetInstanceID() + ", original=" + entry.Value +
                    ", requested=" + VineInstancingOn + ", expected=" + (VineInstancingOn || entry.Value) +
                    ", actual=" + material.enableInstancing + ", shader=" + (material.shader == null ? "none" : material.shader.name) +
                    ", receiveShadows=" + (material.HasProperty("_ReceiveShadows") ? material.GetFloat("_ReceiveShadows").ToString() : "absent") +
                    ", receiveShadowsOffKeyword=" + material.IsKeywordEnabled("_RECEIVE_SHADOWS_OFF") +
                    ", shadowCasterPass=" + material.GetShaderPassEnabled("ShadowCaster"));
            }
            if (!scan) return;
            int found = 0, missingRenderers = 0, missingMaterials = 0;
            foreach (MeshRenderer renderer in Resources.FindObjectsOfTypeAll<MeshRenderer>())
            {
                if (renderer == null || renderer.gameObject.scene.handle != sceneHandle) continue;
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null || !material.name.StartsWith(VineMaterial, StringComparison.Ordinal)) continue;
                    found++;
                    if (!vineShadows.ContainsKey(renderer)) missingRenderers++;
                    if (!vineInstancing.ContainsKey(material)) missingMaterials++;
                    break;
                }
            }
            write("fresh vine scan=" + found + ", untrackedRenderers=" + missingRenderers + ", untrackedMaterialRenderers=" + missingMaterials);
            foreach (Light light in Resources.FindObjectsOfTypeAll<Light>())
            {
                if (light == null || light.gameObject.scene.handle != sceneHandle ||
                    (light.type != LightType.Point && light.type != LightType.Directional && light.type != LightType.Spot)) continue;
                if (!pointShadows.ContainsKey(light) && !sunShadows.ContainsKey(light))
                    write("untracked light " + LightInfo(light, camera));
            }
        }

        private static void AuditLights(Action<string> write, string key, Dictionary<Light, LightShadows> targets,
            bool off, Camera camera, bool details)
        {
            int live = 0, mismatch = 0, active = 0;
            foreach (var entry in targets)
            {
                Light light = entry.Key;
                if (light == null) continue;
                live++;
                LightShadows expected = off ? LightShadows.None : entry.Value;
                if (light.shadows != expected) mismatch++;
                if (light.isActiveAndEnabled) active++;
                if (details || light.shadows != expected)
                    write(key + " original=" + entry.Value + ", expected=" + expected + ", " + LightInfo(light, camera));
            }
            write(key + " requestedOff=" + off + ", tracked=" + targets.Count + ", live=" + live +
                ", active=" + active + ", mismatches=" + mismatch);
        }

        private static string LightInfo(Light light, Camera camera) =>
            Path(light.transform) + "#" + light.GetInstanceID() + ", type=" + light.type + ", actual=" + light.shadows +
            ", active=" + light.isActiveAndEnabled + ", strength=" + light.shadowStrength + ", intensity=" + light.intensity +
            ", range=" + light.range + ", position=" + light.transform.position + ", distance=" +
            (camera == null ? "unknown" : Vector3.Distance(camera.transform.position, light.transform.position).ToString("F1")) +
            ", bakeType=" + light.bakingOutput.lightmapBakeType + ", mask=" + light.cullingMask;

        private static string Path(Transform node)
        {
            string result = node.name;
            while (node.parent != null) { node = node.parent; result = node.name + "/" + result; }
            return result;
        }
    }
}
