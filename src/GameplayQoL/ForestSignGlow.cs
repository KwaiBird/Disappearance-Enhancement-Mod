using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Disappearance.GameplayQoL
{
    internal sealed class ForestSignGlow
    {
        private readonly Dictionary<MeshRenderer, Material[]> originals = new Dictionary<MeshRenderer, Material[]>();
        private readonly List<Material> copies = new List<Material>();
        private bool applied;

        internal int SignRendererCount => originals.Count;

        internal void Capture(Scene scene)
        {
            Restore();
            foreach (MeshRenderer renderer in Resources.FindObjectsOfTypeAll<MeshRenderer>())
            {
                if (renderer == null || renderer.gameObject.scene != scene || !IsSign(renderer.transform))
                    continue;
                originals.Add(renderer, renderer.sharedMaterials);
            }
        }

        private static bool IsSign(Transform part)
        {
            for (Transform node = part; node != null; node = node.parent)
                if (node.name == "Boards" && node.parent != null && node.parent.name == "Forest")
                    return true;
            return false;
        }

        internal void Apply(bool enabled, float intensity)
        {
            if (enabled == applied) return;
            if (!enabled) { RestoreMaterials(); return; }
            foreach (var entry in originals)
            {
                if (entry.Key == null) continue;
                Material[] replacement = new Material[entry.Value.Length];
                for (int i = 0; i < replacement.Length; i++)
                {
                    Material source = entry.Value[i];
                    if (source == null) continue;
                    Material clone = new Material(source) { name = source.name + " (Forest sign glow)" };
                    copies.Add(clone);
                    Texture baseTexture = source.HasProperty("_BaseMap") ? source.GetTexture("_BaseMap") : source.mainTexture;
                    if (clone.HasProperty("_EmissionMap") && baseTexture != null)
                        clone.SetTexture("_EmissionMap", baseTexture);
                    if (clone.HasProperty("_EmissionColor"))
                        clone.SetColor("_EmissionColor", Color.white * intensity);
                    clone.EnableKeyword("_EMISSION");
                    replacement[i] = clone;
                }
                entry.Key.sharedMaterials = replacement;
            }
            applied = true;
        }

        private void RestoreMaterials()
        {
            foreach (var entry in originals)
                if (entry.Key != null) entry.Key.sharedMaterials = entry.Value;
            foreach (Material material in copies)
                if (material != null) Object.Destroy(material);
            copies.Clear();
            applied = false;
        }

        internal void Restore()
        {
            RestoreMaterials();
            originals.Clear();
        }
    }
}
