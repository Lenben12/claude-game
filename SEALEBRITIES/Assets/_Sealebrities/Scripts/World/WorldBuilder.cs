using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Entry point of the World module: builds terrain, ocean, sky and the day/night cycle, then runs the
    /// props builders (IslandProps, UnderwaterDecor) and fills WorldInfo. Heavy math runs on worker threads
    /// while this coroutine keeps the loading screen alive.
    public static class WorldBuilder
    {
        public static Terrain Terrain { get; private set; }
        public static WorldOcean Ocean { get; private set; }
        public static WorldDayNight DayNight { get; private set; }

        static readonly Vector3 MenuCameraPosition = new Vector3(300f, 55f, -230f);
        static readonly Vector3 MenuCameraLookAt = new Vector3(60f, 10f, 20f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Terrain = null; Ocean = null; DayNight = null; }

        public static IEnumerator Build(Transform root, Action<float, string> progress)
        {
            WorldInfo.Built = false;
            if (Refs.WorldRoot == null) Refs.WorldRoot = root;
            var world = new GameObject("World").transform;
            world.SetParent(root, false);

            // ---------------------------------------------------------------- background generation
            Report(progress, 0.01f, "Insel wird vermessen …", "Surveying the island …");
            yield return null;
            var job = new WorldGenJob();
            job.StartHeightsAndTextures();
            while (!job.HeightsDone)
            {
                Report(progress, 0.02f + 0.26f * job.HeightProgress, "Insel wird vermessen …", "Surveying the island …");
                yield return null;
            }
            if (job.Heights == null)
            {
                if (job.Error != null) Debug.LogException(job.Error);
                Debug.LogError("Sealebrities: heightfield generation failed – using a flat seabed.");
                job.Heights = FallbackHeights();
            }
            job.StartSplat();

            while (!job.TexturesDone)
            {
                Report(progress, 0.28f + 0.06f * job.TextureProgress, "Sand, Fels und Dschungel werden gemalt …", "Painting sand, rock and jungle …");
                yield return null;
            }
            EnsureTexturePixels(job);

            // ---------------------------------------------------------------- terrain
            Report(progress, 0.36f, "Texturen werden geladen …", "Loading textures …");
            yield return null;
            var layers = WorldTextures.CreateTerrainLayers(job.TerrainPixels);
            job.TerrainPixels = null;

            Report(progress, 0.42f, "Terrain wird geformt …", "Shaping the terrain …");
            yield return null;
            var td = new TerrainData { name = "SB_TerrainData" };
            td.heightmapResolution = WorldLayout.HeightmapResolution;
            td.size = new Vector3(WorldLayout.TerrainSize, WorldLayout.TerrainHeightRange, WorldLayout.TerrainSize);
            td.SetHeights(0, 0, job.Heights);
            yield return null;

            while (!job.SplatDone)
            {
                Report(progress, 0.5f + 0.06f * job.SplatProgress, "Meeresboden wird eingefärbt …", "Coloring the seabed …");
                yield return null;
            }
            Report(progress, 0.57f, "Meeresboden wird eingefärbt …", "Coloring the seabed …");
            yield return null;
            td.alphamapResolution = WorldGenJob.AlphamapResolution;
            td.baseMapResolution = 1024;
            td.terrainLayers = layers;
            if (job.Splat != null) td.SetAlphamaps(0, 0, job.Splat);
            else
            {
                if (job.Error != null) Debug.LogException(job.Error);
                Debug.LogError("Sealebrities: splatmap generation failed – terrain uses its first layer only.");
            }
            job.Splat = null;
            job.Heights = null;
            yield return null;

            Terrain = CreateTerrainObject(world, td);
            WorldInfo.Terrain = Terrain;
            FillWorldInfo();
            Report(progress, 0.62f, "Himmel und Meer …", "Sky and sea …");
            yield return null;

            // ---------------------------------------------------------------- ocean & sky
            var ripples = job.OceanRipples != null ? WorldTextures.Make(job.OceanRipples, 256, true, "SB_OceanRipples", TextureWrapMode.Repeat, 8) : null;
            Ocean = WorldOcean.Create(world, ripples);
            var starTex = job.StarDot != null ? WorldTextures.Make(job.StarDot, 32, false, "SB_StarDot", TextureWrapMode.Clamp, 1) : null;
            var moonTex = job.Moon != null ? WorldTextures.Make(job.Moon, 128, false, "SB_Moon", TextureWrapMode.Clamp, 1) : null;
            var cloudTex = job.Clouds != null ? WorldTextures.Make(job.Clouds, WorldGenJob.CloudTextureSize, false, "SB_Clouds", TextureWrapMode.Clamp, 4) : null;
            job.OceanRipples = job.StarDot = job.Moon = job.Clouds = null;
            DayNight = WorldSky.Create(world, starTex, moonTex, cloudTex);
            WorldInfo.Sun = RenderSettings.sun;
            Report(progress, 0.66f, "Himmel und Meer …", "Sky and sea …");
            yield return null;

            // ---------------------------------------------------------------- props (other engineer)
            Report(progress, 0.68f, "Hütte, Steg und Dschungel werden gebaut …", "Building hut, dock and jungle …");
            yield return null;
            yield return Guard(IslandProps.Build(root), "IslandProps");
            Report(progress, 0.84f, "Riffe und Wracks werden versenkt …", "Sinking reefs and wrecks …");
            yield return null;
            yield return Guard(UnderwaterDecor.Build(root), "UnderwaterDecor");

            WorldInfo.Built = true;
            Report(progress, 1f, "Welt fertig", "World ready");
        }

        static void Report(Action<float, string> progress, float f, string de, string en)
        {
            if (progress == null) return;
            try { progress(Mathf.Clamp01(f), Loc.Tr(de, en)); }
            catch (Exception e) { Debug.LogException(e); }
        }

        /// Steps a nested build coroutine and logs (instead of propagating) exceptions so a failing
        /// sub-builder never leaves the game stuck on the loading screen. Nested IEnumerators are run inline.
        static IEnumerator Guard(IEnumerator inner, string what)
        {
            if (inner == null) yield break;
            var stack = new Stack<IEnumerator>();
            stack.Push(inner);
            while (stack.Count > 0)
            {
                var e = stack.Peek();
                object current;
                try
                {
                    if (!e.MoveNext()) { stack.Pop(); continue; }
                    current = e.Current;
                }
                catch (Exception ex)
                {
                    Debug.LogError("Sealebrities: " + what + " failed while building the world.");
                    Debug.LogException(ex);
                    yield break;
                }
                if (current is IEnumerator nested && !(current is CustomYieldInstruction)) { stack.Push(nested); continue; }
                yield return current;
            }
        }

        static Terrain CreateTerrainObject(Transform parent, TerrainData td)
        {
            var go = Terrain.CreateTerrainGameObject(td);
            go.name = "Terrain";
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(-WorldLayout.TerrainSize * 0.5f, WorldLayout.TerrainMinY, -WorldLayout.TerrainSize * 0.5f);
            go.isStatic = false;

            var t = go.GetComponent<Terrain>();
            var mat = MaterialLibrary.Terrain();
            mat.EnableKeyword("_NORMALMAP");
            if (mat.HasProperty("_EnableInstancedPerPixelNormal"))
            {
                mat.SetFloat("_EnableInstancedPerPixelNormal", 1f);
                mat.EnableKeyword("_TERRAIN_INSTANCED_PERPIXEL_NORMAL");
            }
            t.materialTemplate = mat;
            t.drawInstanced = true;
            t.heightmapPixelError = 5f;
            t.basemapDistance = 500f;
            t.heightmapMaximumLOD = 0;
            t.drawTreesAndFoliage = false;
            t.allowAutoConnect = false;
            t.shadowCastingMode = ShadowCastingMode.On;
            t.reflectionProbeUsage = ReflectionProbeUsage.BlendProbesAndSkybox;
            t.Flush();

            if (go.GetComponent<TerrainCollider>() == null)
                go.AddComponent<TerrainCollider>().terrainData = td;
            return t;
        }

        static void FillWorldInfo()
        {
            float sea = Waves.SeaLevel;
            const float dockDeck = 1.3f;
            var spawn = WorldLayout.PlayerSpawn;
            WorldInfo.PlayerSpawn = new Vector3(spawn.x, WorldInfo.GroundHeight(spawn) + 1.0f, spawn.z);
            WorldInfo.PlayerSpawnYaw = WorldLayout.PlayerSpawnYaw;
            var bed = WorldLayout.Bed;
            WorldInfo.BedPosition = new Vector3(bed.x, WorldInfo.GroundHeight(bed) + 0.1f, bed.z);
            var dock = WorldLayout.DockEnd;
            WorldInfo.DockEndPosition = new Vector3(dock.x, sea + dockDeck, dock.z);
            var boat = WorldLayout.BoatSpawn;
            WorldInfo.BoatSpawn = new Vector3(boat.x, sea, boat.z);
            WorldInfo.BoatSpawnYaw = WorldLayout.BoatSpawnYaw;
            var heli = WorldLayout.Helipad;
            WorldInfo.HelipadPosition = new Vector3(heli.x, WorldInfo.GroundHeight(heli), heli.z);
            var fish = WorldLayout.FishingSpot;
            WorldInfo.FishingSpot = new Vector3(fish.x, sea + dockDeck, fish.z);
            var lh = WorldLayout.Lighthouse;
            WorldInfo.LighthousePosition = new Vector3(lh.x, WorldInfo.GroundHeight(lh), lh.z);
            WorldInfo.MenuCameraPosition = MenuCameraPosition;
            WorldInfo.MenuCameraLookAt = MenuCameraLookAt;
        }

        static float[,] FallbackHeights()
        {
            int res = WorldLayout.HeightmapResolution;
            var h = new float[res, res];
            float n = (-20f - WorldLayout.TerrainMinY) / WorldLayout.TerrainHeightRange;
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++) h[z, x] = n;
            return h;
        }

        /// Fills any texture that failed to generate on the worker threads with a small synchronous version.
        static void EnsureTexturePixels(WorldGenJob job)
        {
            if (job.Error != null) Debug.LogException(job.Error);
            int count = Enum.GetValues(typeof(WorldSurface)).Length;
            if (job.TerrainPixels == null || job.TerrainPixels.Length != count) job.TerrainPixels = new WorldPixels[count];
            for (int i = 0; i < count; i++)
                if (job.TerrainPixels[i] == null) job.TerrainPixels[i] = WorldTextureGen.Terrain((WorldSurface)i, 64);
        }
    }
}
