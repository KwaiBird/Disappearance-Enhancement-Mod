using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Disappearance.Lighting
{
    internal sealed class TvCrewCameraGlare
    {
        private readonly Dictionary<MeshRenderer, Material[]> originals = new Dictionary<MeshRenderer, Material[]>();
        private readonly List<Material> copies = new List<Material>();
        private readonly List<GameObject> lensOverlays = new List<GameObject>();
        private Mesh lensMesh;
        private Material lensMaterial;

        internal int RendererCount => originals.Count;

        internal void Apply(Scene scene)
        {
            Restore();
            foreach (MeshRenderer renderer in Resources.FindObjectsOfTypeAll<MeshRenderer>())
            {
                if (renderer == null || renderer.gameObject.scene != scene ||
                    renderer.name != "ビデオカメラ.018" || !IsTvCrewCamera(renderer.transform))
                    continue;

                Material[] source = renderer.sharedMaterials;
                if (source.Length != 2 || source[0] == null || source[1] == null ||
                    source[0].name != "マテリアル" || source[1].name != "画面用")
                    continue;

                Material body = new Material(source[0]) { name = source[0].name + " (crew camera matte)" };
                copies.Add(body);
                Material screen = new Material(source[1]) { name = source[1].name + " (crew camera dark screen)" };
                copies.Add(screen);
                originals.Add(renderer, source);

                // The original black body has broad white specular highlights under the player spotlight.
                SetFinish(body, new Color(0.06f, 0.06f, 0.06f, 1f), 0.22f);
                SetBaseColor(screen, new Color(0.035f, 0.045f, 0.055f, 1f));
                SetFinish(screen, new Color(0.04f, 0.05f, 0.06f, 1f), 0.3f);

                renderer.sharedMaterials = new[] { body, screen };
                AddLensOverlay(renderer.transform, source[0]);
            }
        }

        private void AddLensOverlay(Transform cameraMesh, Material source)
        {
            if (lensMesh == null)
                lensMesh = CreateLensMesh();
            if (lensMaterial == null)
            {
                lensMaterial = new Material(source) { name = "TV crew camera lens" };
                SetBaseColor(lensMaterial, new Color(0.012f, 0.025f, 0.04f, 1f));
                SetFinish(lensMaterial, new Color(0.18f, 0.22f, 0.28f, 1f), 0.88f);
                if (lensMaterial.HasProperty("_Metallic")) lensMaterial.SetFloat("_Metallic", 0.1f);
                lensMaterial.enableInstancing = true;
            }

            // The extracted mesh places the lens face on its local -Y side, centered at Z=-0.00195.
            GameObject lens = new GameObject("Crew camera reflective lens");
            lensOverlays.Add(lens);
            lens.transform.SetParent(cameraMesh, false);
            lens.transform.localPosition = new Vector3(0f, -0.01625f, -0.00195f);
            lens.AddComponent<MeshFilter>().sharedMesh = lensMesh;
            MeshRenderer renderer = lens.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = lensMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static Mesh CreateLensMesh()
        {
            const int segments = 16;
            const float radius = 0.0033f;
            Vector3[] vertices = new Vector3[segments + 1];
            Vector3[] normals = new Vector3[vertices.Length];
            int[] triangles = new int[segments * 3];
            vertices[0] = new Vector3(0f, -0.00008f, 0f);
            normals[0] = Vector3.down;
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                float x = Mathf.Cos(angle);
                float z = Mathf.Sin(angle);
                vertices[i + 1] = new Vector3(radius * x, 0f, radius * z);
                normals[i + 1] = new Vector3(0.25f * x, -1f, 0.25f * z).normalized;
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = (i + 1) % segments + 1;
            }
            Mesh mesh = new Mesh { name = "TV crew camera lens (16 triangles)" };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        private static bool IsTvCrewCamera(Transform part)
        {
            for (Transform node = part.parent; node != null; node = node.parent)
                if (node.name == "TVCamera")
                    return true;
            return false;
        }

        private static void SetBaseColor(Material material, Color color)
        {
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        }

        private static void SetFinish(Material material, Color specular, float smoothness)
        {
            if (material.HasProperty("_SpecColor")) material.SetColor("_SpecColor", specular);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", smoothness);
        }

        internal void Restore()
        {
            foreach (GameObject lens in lensOverlays)
                if (lens != null) Object.Destroy(lens);
            lensOverlays.Clear();
            foreach (var entry in originals)
                if (entry.Key != null) entry.Key.sharedMaterials = entry.Value;
            foreach (Material material in copies)
                if (material != null) Object.Destroy(material);
            if (lensMaterial != null) Object.Destroy(lensMaterial);
            if (lensMesh != null) Object.Destroy(lensMesh);
            lensMaterial = null;
            lensMesh = null;
            originals.Clear();
            copies.Clear();
        }
    }
}
