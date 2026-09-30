using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Disappearance.Lighting
{
    // Small transparent meshes at existing emitters. No additional Light, shadow map,
    // camera, or full-screen render pass is created.
    internal sealed class SceneLightAtmosphere
    {
        private sealed class Halo
        {
            internal Transform Effect, Anchor;
            internal Vector3 LocalCenter, LocalNormal;
            internal float Size;
            internal Light Source;
            internal bool Tunnel;
            internal MeshRenderer Renderer;
            internal MaterialPropertyBlock Properties;
        }
        private readonly List<Light> sources = new List<Light>();
        private readonly List<GameObject> effects = new List<GameObject>();
        private readonly List<Halo> halos = new List<Halo>();
        private readonly TunnelEntranceAtmosphere tunnelEntrance = new TunnelEntranceAtmosphere();
        private Material haloMaterial;
        private Material villageHaloMaterial;
        private Material tunnelHaloMaterial;
        private Material beamMaterial;
        private Mesh haloMesh;
        private Mesh beamMesh;
        private Texture2D haloTexture;
        private Texture2D beamTexture;
        public int HaloCount => halos.Count;
        public int VillageHaloCount { get; private set; }
        public int TunnelHaloCount { get; private set; }
        public int BeamCount { get; private set; }

        public void Capture(Scene scene, System.Action<string> log)
        {
            Restore();
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
                return;

            haloTexture = MakeTexture(64, 64, true);
            beamTexture = MakeTexture(32, 64, false);
            haloMaterial = MakeMaterial(shader, haloTexture, new Color(0.85f, 0.92f, 1f, 0.27f), true);
            villageHaloMaterial = MakeMaterial(shader, haloTexture, new Color(0.85f, 0.92f, 1f, 0.24f), true);
            tunnelHaloMaterial = MakeMaterial(shader, haloTexture, new Color(1f, 0.67f, 0.30f, 0.42f), true);
            beamMaterial = MakeMaterial(shader, beamTexture, new Color(0.9f, 0.94f, 1f, 0.42f));
            haloMesh = MakeQuad();
            beamMesh = MakeBeam();

            foreach (Light light in Resources.FindObjectsOfTypeAll<Light>())
            {
                if (light == null || light.gameObject.scene != scene)
                    continue;
                if (light.type == LightType.Point && light.transform.parent != null &&
                    light.transform.parent.name.StartsWith("StreetLightPrefab_Dirt"))
                {
                    // Each two-head pole has point lights at local z +/-0.7.
                    // Preserve that separation instead of collapsing both halos
                    // onto the parent pole center.
                    CreateHalo(light.transform, light, "Soft lamp halo", haloMaterial,
                        light.transform.position + light.transform.parent.up * 0.27f, 0.95f);
                }
                else if (light.type == LightType.Spot && light.transform.parent != null &&
                    light.transform.parent.name == "SatelliteTruck" &&
                    light.name.StartsWith("Point Light"))
                {
                    CreateEffect(light.transform, light, "Headlight beam", beamMesh,
                        beamMaterial, false, Vector3.zero, 1f);
                    BeamCount++;
                }
            }
            foreach (MeshFilter filter in Resources.FindObjectsOfTypeAll<MeshFilter>())
            {
                if (filter == null || filter.gameObject.scene != scene) continue;
                if (filter.name == "UtilityPole" && filter.transform.parent != null &&
                    filter.transform.parent.name.StartsWith("UtilityPolePrefab_type4"))
                    CreateUtilityPoleHalo(filter, log);
                CreateTunnelLampHalos(filter, log);
            }
            tunnelEntrance.Capture(scene, log);
        }

        public void Update()
        {
            Camera camera = Camera.main;
            if (camera == null) return;
            foreach (Halo halo in halos)
            {
                if (halo.Effect == null) continue;
                bool active = halo.Anchor != null && halo.Anchor.gameObject.activeInHierarchy &&
                    (halo.Source == null || halo.Source.enabled);
                if (active)
                {
                    Vector3 center = halo.Anchor.TransformPoint(halo.LocalCenter);
                    Vector3 towardCamera = camera.transform.position - center;
                    float distance = towardCamera.magnitude;
                    if (distance < .05f) active = false;
                    else
                    {
                        Vector3 view = towardCamera / distance;
                        Vector3 normal = halo.Anchor.localToWorldMatrix.inverse.transpose
                            .MultiplyVector(halo.LocalNormal).normalized;
                        float facing = Vector3.Dot(normal, view);
                        if (halo.Tunnel)
                        {
                            // The original emissive panel remains visible as a
                            // thin sliver while its averaged normal crosses zero.
                            // Fade across that range so neighboring lamps do not
                            // abruptly appear in an unrelated distance order.
                            float visibility = Mathf.SmoothStep(0f, 1f,
                                Mathf.InverseLerp(-.2f, .08f, facing));
                            // Position fade applies only to the identified forest-side entrance.
                            visibility *= tunnelEntrance.Strength(halo.Anchor);
                            // Clip by rendered depth, pixel by pixel. Physics colliders on
                            // the wire gate cover its holes and cannot represent visibility.
                            active = visibility > .005f && (tunnelEntrance.Owns(halo.Anchor) ||
                                EmitterVisible(camera.transform.position, center));
                            if (active)
                            {
                                halo.Properties.SetColor("_BaseColor",
                                    new Color(1f, .67f, .30f, .42f * visibility));
                                halo.Renderer.SetPropertyBlock(halo.Properties);
                            }
                        }
                        else active = halo.LocalNormal == Vector3.zero || facing > 0f;
                        // Pull along the viewing ray, never down the fixture. This
                        // keeps the projected center fixed while placing the entire
                        // camera-facing quad in front of the sloping luminous face.
                        // Account for the projected size change caused by the pull.
                        float pull = 0f;
                        if (active && halo.LocalNormal != Vector3.zero)
                        {
                            float extent = halo.Size * .5f *
                                (Mathf.Abs(Vector3.Dot(normal, camera.transform.right)) +
                                 Mathf.Abs(Vector3.Dot(normal, camera.transform.up)));
                            pull = halo.Tunnel ?
                                Mathf.Min((extent + .015f) /
                                    (Mathf.Max(facing, 0f) + extent / distance),
                                    .04f) :
                                Mathf.Min((extent + .015f) / (facing + extent / distance),
                                    distance * .25f);
                        }
                        halo.Effect.position = center + view * pull;
                        halo.Effect.rotation = camera.transform.rotation;
                        halo.Effect.localScale = Vector3.one * (halo.Size * (1f - pull / distance));
                    }
                }
                if (halo.Effect.gameObject.activeSelf != active) halo.Effect.gameObject.SetActive(active);
            }
            for (int i = 0; i < effects.Count; i++)
                if (effects[i] != null && sources[i] != null && effects[i].name == "Headlight beam")
                {
                    bool active = sources[i].enabled && sources[i].gameObject.activeInHierarchy;
                    if (effects[i].activeSelf != active) effects[i].SetActive(active);
                }
        }

        private static bool EmitterVisible(Vector3 eye, Vector3 emitter)
        {
            Vector3 delta = emitter - eye;
            float distance = delta.magnitude;
            if (distance <= .08f) return true;
            // Stop before the emitting surface, retaining walls, gate and other fixtures.
            return !Physics.Raycast(eye, delta / distance, distance - .06f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

#if P5_HALO_DIAGNOSTICS
        internal void WriteTunnelHaloStates(Camera camera, string path)
        {
            var lines = new List<string>
            {
                "index,anchor,worldX,worldY,worldZ,screenX,screenY,screenDepth,distance,facing,pull,active,rendererEnabled,rendererVisible"
            };
            for (int index = 0; index < halos.Count; index++)
            {
                Halo halo = halos[index];
                if (halo.Effect == null || halo.Anchor == null ||
                    halo.Effect.name != "Soft tunnel lamp halo") continue;
                Vector3 center = halo.Anchor.TransformPoint(halo.LocalCenter);
                Vector3 screen = camera.WorldToScreenPoint(center);
                Vector3 direction = camera.transform.position - center;
                float distance = direction.magnitude;
                Vector3 normal = halo.Anchor.localToWorldMatrix.inverse.transpose
                    .MultiplyVector(halo.LocalNormal).normalized;
                float facing = distance > 0f ? Vector3.Dot(normal, direction / distance) : 0f;
                float pull = distance > 0f ? Vector3.Dot(halo.Effect.position - center, direction / distance) : 0f;
                MeshRenderer renderer = halo.Effect.GetComponent<MeshRenderer>();
                var invariant = System.Globalization.CultureInfo.InvariantCulture;
                lines.Add(string.Join(",", new[]
                {
                    index.ToString(invariant), halo.Anchor.name,
                    center.x.ToString("F4", invariant), center.y.ToString("F4", invariant),
                    center.z.ToString("F4", invariant), screen.x.ToString("F1", invariant),
                    screen.y.ToString("F1", invariant), screen.z.ToString("F3", invariant),
                    distance.ToString("F3", invariant), facing.ToString("F5", invariant),
                    pull.ToString("F3", invariant), halo.Effect.gameObject.activeSelf.ToString(),
                    renderer.enabled.ToString(), renderer.isVisible.ToString()
                }));
            }
            System.IO.File.WriteAllLines(path, lines.ToArray());
        }

        // Used only between diagnostic screenshots, after the normal LateUpdate.
        internal void SetHaloRenderersVisible(bool visible)
        {
            foreach (Halo halo in halos)
                if (halo.Effect != null) halo.Effect.GetComponent<MeshRenderer>().enabled = visible;
        }
#endif

        public void Restore()
        {
            tunnelEntrance.Restore();
            foreach (GameObject effect in effects)
                if (effect != null) Object.Destroy(effect);
            effects.Clear();
            sources.Clear();
            halos.Clear();
            VillageHaloCount = 0;
            TunnelHaloCount = 0;
            BeamCount = 0;
            if (haloMesh != null) Object.Destroy(haloMesh);
            if (beamMesh != null) Object.Destroy(beamMesh);
            if (haloMaterial != null) Object.Destroy(haloMaterial);
            if (villageHaloMaterial != null) Object.Destroy(villageHaloMaterial);
            if (tunnelHaloMaterial != null) Object.Destroy(tunnelHaloMaterial);
            if (beamMaterial != null) Object.Destroy(beamMaterial);
            if (haloTexture != null) Object.Destroy(haloTexture);
            if (beamTexture != null) Object.Destroy(beamTexture);
            haloMesh = beamMesh = null;
            haloMaterial = villageHaloMaterial = tunnelHaloMaterial = beamMaterial = null;
            haloTexture = beamTexture = null;
        }

        private GameObject CreateHalo(Transform anchor, Light source, string name,
            Material material, Vector3 worldPosition, float worldSize)
        {
            return CreateEffect(anchor, source, name, haloMesh, material, true,
                worldPosition, worldSize);
        }

        private GameObject CreateEffect(Transform anchor, Light source, string name,
            Mesh mesh, Material material, bool isHalo, Vector3 worldPosition, float worldSize)
        {
            GameObject effect = new GameObject(name);
            sources.Add(source);
            effects.Add(effect);
            effect.layer = anchor.gameObject.layer;
            if (!isHalo) effect.transform.SetParent(anchor, false);
            else SceneManager.MoveGameObjectToScene(effect, anchor.gameObject.scene);
            if (isHalo)
            {
                effect.transform.position = worldPosition;
                // An unparented scene object avoids shear when billboards rotate
                // under the original scene's nonuniformly scaled transforms.
                effect.transform.localScale = Vector3.one * worldSize;
                halos.Add(new Halo { Effect = effect.transform, Anchor = anchor,
                    LocalCenter = anchor.InverseTransformPoint(worldPosition), Size = worldSize, Source = source });
            }
            else
            {
                effect.transform.localPosition = Vector3.zero;
                effect.transform.localRotation = Quaternion.identity;
                effect.transform.localScale = Vector3.one;
            }
            effect.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = effect.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return effect;
        }

        private void CreateUtilityPoleHalo(MeshFilter filter, System.Action<string> log)
        {
            MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
            if (renderer == null || filter.sharedMesh == null) return;
            Material[] materials = renderer.sharedMaterials;
            if (materials.Length < 2 || !IsEmissive(materials[1])) return;
            // LOD0 only: LOD1 describes the same lamp. Static batching replaces
            // sharedMesh but preserves the original fixture transform.
            if (!renderer.isPartOfStaticBatch &&
                (filter.sharedMesh.name != "UtilityPole" ||
                 !Matches(filter.sharedMesh.bounds, EmitterGeometry.UtilityPoleBounds))) return;
            AddSurfaceHalo(filter.transform, EmitterGeometry.UtilityPoleCenters[0],
                EmitterGeometry.UtilityPoleNormals[0], villageHaloMaterial, .85f, "Soft utility lamp halo");
            VillageHaloCount++;
            log("Halo utility target: " + filter.transform.parent.name + ", active=" +
                filter.gameObject.activeInHierarchy + ", center=" +
                filter.transform.TransformPoint(EmitterGeometry.UtilityPoleCenters[0]).ToString("F3"));
        }

        private static bool IsEmissive(Material material)
        {
            if (material == null || !material.IsKeywordEnabled("_EMISSION") ||
                !material.HasProperty("_EmissionColor")) return false;
            Color color = material.GetColor("_EmissionColor");
            return Mathf.Max(color.r, Mathf.Max(color.g, color.b)) > .01f;
        }

        private static bool Matches(Bounds actual, Bounds expected) =>
            Vector3.Distance(actual.center, expected.center) < .01f &&
            Vector3.Distance(actual.extents, expected.extents) < .01f;

        private void AddSurfaceHalo(Transform anchor, Vector3 center, Vector3 normal,
            Material material, float size, string name)
        {
            CreateHalo(anchor, null, name, material, anchor.TransformPoint(center), size);
            Halo halo = halos[halos.Count - 1];
            halo.LocalNormal = normal;
            if (name == "Soft tunnel lamp halo")
            {
                halo.Tunnel = true;
                halo.Renderer = halo.Effect.GetComponent<MeshRenderer>();
                halo.Properties = new MaterialPropertyBlock();
            }
        }

        private void CreateTunnelLampHalos(MeshFilter filter, System.Action<string> log)
        {
            Mesh mesh = filter.sharedMesh;
            MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
            if (mesh == null || renderer == null) return;
            Vector3[] centers, normals;
            Bounds expected;
            switch (mesh.name)
            {
                case "curve_L":
                    centers = EmitterGeometry.CurveLeftCenters; normals = EmitterGeometry.CurveLeftNormals;
                    expected = EmitterGeometry.CurveLeftBounds; break;
                case "curve_R":
                    centers = EmitterGeometry.CurveRightCenters; normals = EmitterGeometry.CurveRightNormals;
                    expected = EmitterGeometry.CurveRightBounds; break;
                case "wall_s_e":
                    centers = EmitterGeometry.EntranceCenters; normals = EmitterGeometry.EntranceNormals;
                    expected = EmitterGeometry.EntranceBounds; break;
                case "wall_s_e.001":
                    centers = EmitterGeometry.ExitCenters; normals = EmitterGeometry.ExitNormals;
                    expected = EmitterGeometry.ExitBounds; break;
                default: return;
            }
            Material[] materials = renderer.sharedMaterials;
            if (!Matches(mesh.bounds, expected) || materials.Length < 3 ||
                materials[2] == null || materials[2].name != "obj1" || !IsEmissive(materials[2])) return;
            for (int i = 0; i < centers.Length; i++)
            {
                AddSurfaceHalo(filter.transform, centers[i], normals[i], tunnelHaloMaterial,
                    1.25f, "Soft tunnel lamp halo");
                TunnelHaloCount++;
            }
            log("Halo tunnel target: " + filter.name + ", mesh=" + mesh.name +
                ", active=" + filter.gameObject.activeInHierarchy + ", emitters=" + centers.Length);
        }

        private static Material MakeMaterial(Shader shader, Texture2D texture, Color color, bool halo = false)
        {
            Material material = new Material(shader);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)(halo ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            material.SetFloat("_ZWrite", 0f);
            if (material.HasProperty("_ZTest")) material.SetFloat("_ZTest", (float)CompareFunction.LessEqual);
            material.SetFloat("_Cull", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            if (halo)
            {
                // A halo must only add light. It must not darken the scene or
                // participate in opaque depth/normals/shadow passes.
                material.SetShaderPassEnabled("DepthOnly", false);
                material.SetShaderPassEnabled("DepthNormals", false);
                material.SetShaderPassEnabled("DepthNormalsOnly", false);
                material.SetShaderPassEnabled("ShadowCaster", false);
                material.SetShaderPassEnabled("MotionVectors", false);
            }
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", color);
            return material;
        }

        private static Texture2D MakeTexture(int width, int height, bool radial)
        {
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float u = (x + 0.5f) / width;
                float v = (y + 0.5f) / height;
                float alpha;
                if (radial)
                {
                    float r = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f;
                    alpha = Mathf.Pow(Mathf.Clamp01(1f - r), 2.2f);
                }
                else
                {
                    float across = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(u - 0.5f) * 2f), 1.5f);
                    float length = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(v * 7f)) *
                                   Mathf.Pow(1f - v, 1.8f);
                    alpha = across * length;
                }
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
            texture.Apply(false, true);
            return texture;
        }

        private static Mesh MakeQuad()
        {
            Mesh mesh = new Mesh { name = "Lamp halo quad" };
            mesh.vertices = new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f),
                new Vector3(-0.5f, 0.5f), new Vector3(0.5f, 0.5f) };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh MakeBeam()
        {
            const int rows = 6;
            const int columns = 3;
            Vector3[] vertices = new Vector3[2 * rows * columns];
            Vector2[] uv = new Vector2[vertices.Length];
            int[] triangles = new int[2 * (rows - 1) * (columns - 1) * 6];
            int index = 0;
            for (int plane = 0; plane < 2; plane++)
            {
                for (int row = 0; row < rows; row++)
                for (int column = 0; column < columns; column++)
                {
                    float v = row / (float)(rows - 1);
                    float u = column / (float)(columns - 1);
                    float width = Mathf.Lerp(0.08f, 3.2f, v);
                    float side = (u - 0.5f) * 2f * width;
                    int vertex = plane * rows * columns + row * columns + column;
                    vertices[vertex] = plane == 0 ? new Vector3(side, 0f, v * 14f) :
                        new Vector3(0f, side, v * 14f);
                    uv[vertex] = new Vector2(u, v);
                }
                for (int row = 0; row < rows - 1; row++)
                for (int column = 0; column < columns - 1; column++)
                {
                    int a = plane * rows * columns + row * columns + column;
                    triangles[index++] = a;
                    triangles[index++] = a + columns;
                    triangles[index++] = a + 1;
                    triangles[index++] = a + 1;
                    triangles[index++] = a + columns;
                    triangles[index++] = a + columns + 1;
                }
            }
            Mesh mesh = new Mesh { name = "Headlight crossed beam" };
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
