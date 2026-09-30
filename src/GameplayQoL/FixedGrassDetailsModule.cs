using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Disappearance.GameplayQoL
{
    internal sealed class FixedGrassDetailsModule
    {
        private sealed class Conversion
        {
            internal Terrain Terrain;
            internal TerrainData Original;
            internal TerrainData Clone;
            internal readonly List<GameObject> Prototypes = new List<GameObject>();
            internal readonly List<Material> Materials = new List<Material>();
        }

        private readonly ManualLogSource log;
        private readonly bool enabledAtStartup;
        private readonly List<Conversion> conversions = new List<Conversion>();
        private Mesh crossedCards;
        private float nextScan;

        internal FixedGrassDetailsModule(ConfigFile config, ManualLogSource log)
        {
            this.log = log;
            enabledAtStartup = config.Bind("Terrain", "Enabled", true,
                "Replace grass_weed_03 and grass_weed_06 billboards with fixed crossed cards. Restart to apply changes.").Value;
        }

        internal void Update()
        {
            if (!enabledAtStartup || SceneManager.GetActiveScene().name != "VillageScene" ||
                Time.unscaledTime < nextScan) return;
            nextScan = Time.unscaledTime + 0.5f;
            foreach (Terrain terrain in Terrain.activeTerrains)
            {
                if (terrain == null || terrain.gameObject.scene.name != "VillageScene" ||
                    terrain.terrainData == null || Owns(terrain)) continue;
                try { Convert(terrain); }
                catch (Exception error) { log.LogError("Fixed grass conversion failed on " + terrain.name + ": " + error); }
            }
        }

        private bool Owns(Terrain terrain)
        {
            foreach (Conversion conversion in conversions)
                if (conversion.Terrain == terrain) return true;
            return false;
        }

        private void Convert(Terrain terrain)
        {
            TerrainData original = terrain.terrainData;
            DetailPrototype[] source = original.detailPrototypes;
            bool matching = false;
            foreach (DetailPrototype detail in source)
                if (IsTarget(detail)) { matching = true; break; }
            if (!matching) return;

            var conversion = new Conversion { Terrain = terrain, Original = original };
            try
            {
                conversion.Clone = UnityEngine.Object.Instantiate(original);
                conversion.Clone.name = original.name + "_FixedGrassRuntime";
                conversion.Clone.hideFlags = HideFlags.DontSave;
                DetailPrototype[] details = conversion.Clone.detailPrototypes;
                int count = 0;
                for (int index = 0; index < details.Length; index++)
                {
                    DetailPrototype detail = details[index];
                    if (!IsTarget(detail)) continue;
                    GameObject prototype = CreatePrototype(detail.prototypeTexture, conversion);
                    DetailPrototype replacement = new DetailPrototype {
                        prototype = prototype, prototypeTexture = null,
                        minWidth = detail.minWidth, maxWidth = detail.maxWidth,
                        minHeight = detail.minHeight, maxHeight = detail.maxHeight,
                        noiseSeed = detail.noiseSeed, noiseSpread = detail.noiseSpread,
                        holeEdgePadding = detail.holeEdgePadding, density = detail.density,
                        healthyColor = Color.white, dryColor = Color.white,
                        renderMode = DetailRenderMode.Grass, usePrototypeMesh = true,
                        useInstancing = false, useDensityScaling = detail.useDensityScaling,
                        alignToGround = detail.alignToGround,
                        positionJitter = detail.positionJitter, targetCoverage = detail.targetCoverage
                    };
                    if (!replacement.Validate(out string error))
                        throw new InvalidOperationException("Invalid fixed grass prototype: " + error);
                    details[index] = replacement;
                    count++;
                }
                conversion.Clone.detailPrototypes = details;
                terrain.terrainData = conversion.Clone;
                conversions.Add(conversion);
                log.LogInfo("Converted " + count + " grass detail types on terrain " + terrain.name + ".");
            }
            catch
            {
                Release(conversion);
                throw;
            }
        }

        private static bool IsTarget(DetailPrototype detail)
        {
            if (detail == null || detail.renderMode != DetailRenderMode.GrassBillboard ||
                detail.usePrototypeMesh || detail.prototypeTexture == null) return false;
            string name = detail.prototypeTexture.name;
            return name == "grass_weed_03" || name == "grass_weed_06";
        }

        private GameObject CreatePrototype(Texture2D texture, Conversion conversion)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("URP Lit shader was not found.");
            if (crossedCards == null) crossedCards = CreateCrossedCards();
            var material = new Material(shader) { name = "FixedGrass_" + texture.name };
            conversion.Materials.Add(material);
            material.SetTexture("_BaseMap", texture);
            material.SetTexture("_MainTex", texture);
            material.SetFloat("_AlphaClip", 1f);
            material.SetFloat("_Cutoff", 0.5f);
            material.SetFloat("_Cull", 0f);
            material.EnableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)RenderQueue.AlphaTest;
            material.enableInstancing = true;
            var prototype = new GameObject(material.name);
            conversion.Prototypes.Add(prototype);
            prototype.hideFlags = HideFlags.HideAndDontSave;
            prototype.transform.position = new Vector3(0f, -10000f, 0f);
            prototype.AddComponent<MeshFilter>().sharedMesh = crossedCards;
            prototype.AddComponent<MeshRenderer>().sharedMaterial = material;
            UnityEngine.Object.DontDestroyOnLoad(prototype);
            return prototype;
        }

        private static Mesh CreateCrossedCards()
        {
            Mesh mesh = new Mesh { name = "FixedGrass_X_Cards" };
            mesh.vertices = new[] {
                new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f),
                new Vector3(-0.5f, 1f, 0f), new Vector3(0.5f, 1f, 0f),
                new Vector3(0f, 0f, -0.5f), new Vector3(0f, 0f, 0.5f),
                new Vector3(0f, 1f, -0.5f), new Vector3(0f, 1f, 0.5f)
            };
            mesh.uv = new[] {
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 1f), new Vector2(1f, 1f)
            };
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3, 4, 6, 5, 5, 6, 7 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        internal void OnSceneChanged()
        {
            foreach (Conversion conversion in conversions) Release(conversion);
            conversions.Clear();
            nextScan = 0;
        }

        internal void Stop()
        {
            OnSceneChanged();
            if (crossedCards != null) { UnityEngine.Object.Destroy(crossedCards); crossedCards = null; }
        }

        private static void Release(Conversion conversion)
        {
            if (conversion.Terrain != null && conversion.Terrain.terrainData == conversion.Clone)
                conversion.Terrain.terrainData = conversion.Original;
            if (conversion.Clone != null) UnityEngine.Object.Destroy(conversion.Clone);
            foreach (GameObject prototype in conversion.Prototypes)
                if (prototype != null) UnityEngine.Object.Destroy(prototype);
            foreach (Material material in conversion.Materials)
                if (material != null) UnityEngine.Object.Destroy(material);
        }
    }
}

