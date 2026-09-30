using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Disappearance.Lighting
{
    // One statically batched LOD1 lamp sits above the LOD0 lamp and its halo.
    // A separate copy of the original LOD1 mesh lets the LODGroup keep AUTO.
    internal sealed class BridgePoleLodAlignment
    {
        private const string TargetName = "UtilityPolePrefab_type4 (3)";
        private LODGroup target;
        private LOD[] originalLods;
        private Renderer originalBody;
        private bool originalBodyEnabled;
        private GameObject replacement;

        internal bool Applied => replacement != null;

        internal void Capture(Scene scene, Action<string> log)
        {
            Restore();
            LODGroup group = null;
            foreach (LODGroup candidate in Resources.FindObjectsOfTypeAll<LODGroup>())
            {
                if (candidate == null || candidate.gameObject.scene != scene ||
                    candidate.name != TargetName || candidate.GetLODs().Length < 2)
                    continue;
                Vector3 position = candidate.transform.position;
                if (Mathf.Abs(position.x - 709.659f) > 2f ||
                    Mathf.Abs(position.z - 237.419f) > 2f)
                    continue;
                group = candidate;
                break;
            }
            if (group == null)
            {
                log("Bridge pole LOD target not found; no change.");
                return;
            }
            Transform high = group.transform.Find("UtilityPole");
            Transform low = group.transform.Find("UtilityPole_LOD1");
            Renderer lowRenderer = low == null ? null : low.GetComponent<Renderer>();
            if (high == null || lowRenderer == null)
            {
                log("Bridge pole LOD body not found; no change.");
                return;
            }
            Mesh sourceMesh = FindUnbatchedMesh(scene, group);
            if (sourceMesh == null)
            {
                log("Bridge pole original LOD1 mesh not found; no change.");
                return;
            }
            LOD[] lods = group.GetLODs();
            Renderer[] lowRenderers = (Renderer[])lods[1].renderers.Clone();
            int index = Array.IndexOf(lowRenderers, lowRenderer);
            if (index < 0)
            {
                log("Bridge pole LOD1 body not in LODGroup; no change.");
                return;
            }

            target = group;
            originalLods = lods;
            originalBody = lowRenderer;
            originalBodyEnabled = lowRenderer.enabled;
            replacement = new GameObject("Corrected bridge LOD1 body");
            replacement.transform.SetParent(group.transform, false);
            replacement.transform.localPosition = high.localPosition;
            replacement.transform.localRotation = high.localRotation;
            replacement.transform.localScale = high.localScale;
            replacement.AddComponent<MeshFilter>().sharedMesh = sourceMesh;
            MeshRenderer renderer = replacement.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = lowRenderer.sharedMaterials;
            renderer.shadowCastingMode = lowRenderer.shadowCastingMode;
            renderer.receiveShadows = lowRenderer.receiveShadows;
            renderer.lightmapIndex = lowRenderer.lightmapIndex;
            renderer.lightmapScaleOffset = lowRenderer.lightmapScaleOffset;
            renderer.lightProbeUsage = lowRenderer.lightProbeUsage;
            renderer.reflectionProbeUsage = lowRenderer.reflectionProbeUsage;
            lowRenderers[index] = renderer;
            LOD[] replacementLods = (LOD[])lods.Clone();
            replacementLods[1].renderers = lowRenderers;
            lowRenderer.enabled = false;
            group.SetLODs(replacementLods);
            log("Bridge pole LOD1 aligned to LOD0 at " +
                replacement.transform.position.ToString("F3") + "; AUTO preserved.");
        }

        private static Mesh FindUnbatchedMesh(Scene scene, LODGroup target)
        {
            foreach (MeshFilter filter in Resources.FindObjectsOfTypeAll<MeshFilter>())
            {
                if (filter == null || filter.gameObject.scene != scene ||
                    filter.name != "UtilityPole_LOD1" || filter.transform.parent == null ||
                    filter.transform.parent == target.transform ||
                    !filter.transform.parent.name.StartsWith("UtilityPolePrefab_type4"))
                    continue;
                Renderer renderer = filter.GetComponent<Renderer>();
                if (renderer == null || renderer.isPartOfStaticBatch ||
                    filter.sharedMesh == null || filter.sharedMesh.name != "UtilityPole_LOD1" ||
                    Vector3.Distance(filter.transform.position, target.transform.position) > 30f)
                    continue;
                return filter.sharedMesh;
            }
            return null;
        }

        internal void Restore()
        {
            if (target != null && originalLods != null) target.SetLODs(originalLods);
            if (originalBody != null) originalBody.enabled = originalBodyEnabled;
            if (replacement != null) UnityEngine.Object.Destroy(replacement);
            target = null;
            originalLods = null;
            originalBody = null;
            replacement = null;
        }
    }
}
