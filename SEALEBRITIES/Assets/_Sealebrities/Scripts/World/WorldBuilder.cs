using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Entry point of the World module: builds the low-poly mesh terrain, ocean, shoreline foam, sky and the day/night
    /// cycle, then runs the props builders (IslandProps, UnderwaterDecor) and fills WorldInfo. Heavy math runs on worker
    /// threads while this coroutine keeps the loading screen alive.
    public static class WorldBuilder
    {
        /// The terrain as data (exact surface heights, chunk meshes' source). Null until built.
        public static WorldTerrainData Ground { get; private set; }
        public static WorldOcean Ocean { get; private set; }
        public static WorldCoast Coast { get; private set; }
        public static WorldDayNight DayNight { get; private set; }

        // L.5: the 0.62 island (Testversion 3: (300, 55, −230) → (60, 10, 20))
        static readonly Vector3 MenuCameraPosition = new Vector3(186f, 34f, -143f);
        static readonly Vector3 MenuCameraLookAt = new Vector3(37f, 6f, 12f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Ground = null; Ocean = null; Coast = null; DayNight = null; }

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
            job.Start();
            while (!job.Done)
            {
                Report(progress, 0.02f + 0.32f * job.Progress, "Insel wird vermessen …", "Surveying the island …");
                yield return null;
            }
            if (job.Error != null) Debug.LogException(job.Error);
            var terrain = job.Terrain;
            if (terrain == null)
            {
                Debug.LogError("Sealebrities: terrain generation failed – using a flat seabed.");
                try { terrain = WorldGenJob.FlatFallback(); }
                catch (Exception e) { Debug.LogException(e); terrain = null; }
            }
            else Debug.Log("Sealebrities: low-poly terrain " + terrain.TriangleCount + " triangles in " + terrain.Chunks.Length + " chunks.");

            // the exact triangulated surface is the ground truth for props, creatures, boat and player
            Ground = terrain;
            WorldInfo.Terrain = null;
            WorldInfo.HeightProvider = terrain != null ? terrain.Height : (Func<float, float, float>)null;

            // ---------------------------------------------------------------- terrain meshes
            Report(progress, 0.35f, "Terrain wird geformt …", "Shaping the terrain …");
            yield return null;
            if (terrain != null)
            {
                yield return Guard(WorldTerrain.CreateObjects(world, terrain,
                    p => Report(progress, 0.35f + 0.25f * p, "Terrain wird geformt …", "Shaping the terrain …")), "Terrain");
            }
            FillWorldInfo();
            Report(progress, 0.62f, "Himmel und Meer …", "Sky and sea …");
            yield return null;

            // ---------------------------------------------------------------- ocean, coast & sky
            try { Ocean = WorldOcean.Create(world, job.Water); }
            catch (Exception e) { Debug.LogError("Sealebrities: ocean failed."); Debug.LogException(e); }
            try { Coast = WorldCoast.Create(world, job.Coast); }
            catch (Exception e) { Debug.LogError("Sealebrities: shoreline foam failed."); Debug.LogException(e); }
            job.Water = null; job.Coast = null;
            yield return null;
            try { DayNight = WorldSky.Create(world); }
            catch (Exception e) { Debug.LogError("Sealebrities: sky failed."); Debug.LogException(e); }
            if (WorldInfo.Sun == null) WorldInfo.Sun = RenderSettings.sun;
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
    }
}
