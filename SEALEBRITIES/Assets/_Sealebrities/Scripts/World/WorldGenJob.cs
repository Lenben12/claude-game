using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Sealebrities
{
    /// Runs the heavy, Unity-free parts of world generation on worker threads: the low-poly terrain (vertex data,
    /// stitching, color classes), the water color map and the shoreline foam. The world builder coroutine polls the
    /// progress and turns the results into meshes on the main thread.
    public sealed class WorldGenJob
    {
        public WorldTerrainData Terrain;
        public WorldWaterMap Water;
        public WorldCoastData Coast;
        public Exception Error;

        int chunksDone, stage;
        volatile bool done;

        public bool Done => done;

        /// 0..1: chunks dominate, the water map and the coast are short tails.
        public float Progress
        {
            get
            {
                float chunks = Mathf.Clamp01(Volatile.Read(ref chunksDone) / (float)(WorldTerrainData.ChunksPerSide * WorldTerrainData.ChunksPerSide));
                int s = Volatile.Read(ref stage);
                return s == 0 ? chunks * 0.8f : (s == 1 ? 0.85f : 0.95f);
            }
        }

        public void Start()
        {
            // static layout caches must be filled on the main thread before the workers read them
            WorldTerrainData.PrepareStatics();
            Action body = () =>
            {
                try { Work(); }
                catch (Exception e) { Error = e; }
                finally { done = true; }
            };
            try { Task.Run(body); }
            catch (Exception) { body(); }   // no thread pool available: run inline
        }

        void Work()
        {
            var terrain = new WorldTerrainData(WorldHeightField.Height);
            terrain.Build(() => Interlocked.Increment(ref chunksDone));
            Terrain = terrain;

            Volatile.Write(ref stage, 1);
            try { Water = WorldWaterMap.Build(terrain.Height); }
            catch (Exception e) { Error = e; }

            Volatile.Write(ref stage, 2);
            try { Coast = WorldCoastData.Build(terrain); }
            catch (Exception e) { Error = e; }
        }

        /// Synchronous emergency terrain (flat seabed at -20 m) used when the real generation failed.
        public static WorldTerrainData FlatFallback()
        {
            WorldTerrainData.PrepareStatics();
            var t = new WorldTerrainData((x, z) => -20f);
            t.Build(null);
            return t;
        }
    }
}
