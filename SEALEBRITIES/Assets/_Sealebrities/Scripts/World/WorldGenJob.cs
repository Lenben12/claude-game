using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Sealebrities
{
    /// Runs the heavy, Unity-free parts of world generation (heightfield, splatmap, texture pixels) on worker threads.
    /// The world builder coroutine polls the progress and consumes the results on the main thread.
    public sealed class WorldGenJob
    {
        public const int AlphamapResolution = 1024;
        public const int CloudTextureSize = 1024;

        public float[,] Heights;            // normalized 0..1, [z, x]
        public float[,,] Splat;             // [z, x, layer]
        public WorldPixels[] TerrainPixels; // one per WorldSurface
        public byte[] OceanRipples, StarDot, Moon, Clouds;
        public Exception Error;

        int heightRows, splatRows, texturesDoneCount;
        volatile bool heightsDone, splatDone, texturesDone;

        public bool HeightsDone => heightsDone;
        public bool SplatDone => splatDone;
        public bool TexturesDone => texturesDone;
        public float HeightProgress => Mathf.Clamp01(Volatile.Read(ref heightRows) / (float)WorldLayout.HeightmapResolution);
        public float SplatProgress => Mathf.Clamp01(Volatile.Read(ref splatRows) / (float)AlphamapResolution);
        static readonly int TextureJobs = Enum.GetValues(typeof(WorldSurface)).Length + 4;
        public float TextureProgress => Mathf.Clamp01(Volatile.Read(ref texturesDoneCount) / (float)TextureJobs);

        static float OriginX => -WorldLayout.TerrainSize * 0.5f;
        static float OriginZ => -WorldLayout.TerrainSize * 0.5f;

        public void StartHeightsAndTextures()
        {
            // initialize static layout caches on the main thread before workers use them
            WorldHeightField.Height(0f, 0f);
            Run(ComputeHeights, () => heightsDone = true);
            Run(ComputeTextures, () => texturesDone = true);
        }

        public void StartSplat() => Run(ComputeSplat, () => splatDone = true);

        void Run(Action work, Action done)
        {
            Action body = () =>
            {
                try { work(); }
                catch (Exception e) { Error = e; }
                finally { done(); }
            };
            try { Task.Run(body); }
            catch (Exception) { body(); }   // no thread pool available: run inline
        }

        void ComputeHeights()
        {
            int res = WorldLayout.HeightmapResolution;
            var h = new float[res, res];
            float cell = WorldLayout.TerrainSize / (res - 1);
            float minY = WorldLayout.TerrainMinY, range = WorldLayout.TerrainHeightRange;
            float ox = OriginX, oz = OriginZ;
            Parallel.For(0, res, zi =>
            {
                float z = oz + zi * cell;
                for (int xi = 0; xi < res; xi++)
                {
                    float y = WorldHeightField.Height(ox + xi * cell, z);
                    float n = (y - minY) / range;
                    h[zi, xi] = n < 0f ? 0f : (n > 1f ? 1f : n);
                }
                Interlocked.Increment(ref heightRows);
            });
            Heights = h;
        }

        void ComputeSplat()
        {
            var h = Heights;
            if (h == null) return;
            Splat = WorldSplat.Compute(h, WorldLayout.HeightmapResolution, WorldLayout.TerrainSize, OriginX, OriginZ,
                WorldLayout.TerrainMinY, WorldLayout.TerrainHeightRange, AlphamapResolution,
                _ => Interlocked.Increment(ref splatRows));
        }

        void ComputeTextures()
        {
            var surfaces = (WorldSurface[])Enum.GetValues(typeof(WorldSurface));
            var px = new WorldPixels[surfaces.Length];
            var work = new Action[surfaces.Length + 4];
            for (int i = 0; i < surfaces.Length; i++)
            {
                int k = i;
                work[i] = () => { px[k] = WorldTextureGen.Terrain(surfaces[k], WorldTextures.TerrainTextureSize); Interlocked.Increment(ref texturesDoneCount); };
            }
            work[surfaces.Length] = () => { OceanRipples = WorldTextureGen.OceanRipples(256); Interlocked.Increment(ref texturesDoneCount); };
            work[surfaces.Length + 1] = () => { StarDot = WorldTextureGen.StarDot(32); Interlocked.Increment(ref texturesDoneCount); };
            work[surfaces.Length + 2] = () => { Moon = WorldTextureGen.Moon(128); Interlocked.Increment(ref texturesDoneCount); };
            work[surfaces.Length + 3] = () => { Clouds = WorldTextureGen.Clouds(CloudTextureSize); Interlocked.Increment(ref texturesDoneCount); };
            Parallel.Invoke(work);
            TerrainPixels = px;
        }
    }
}
