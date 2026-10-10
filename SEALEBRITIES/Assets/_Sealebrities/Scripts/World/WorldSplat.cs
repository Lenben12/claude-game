using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sealebrities
{
    /// Computes the terrain splatmap (layer weights) from the heightfield. Pure C#; runs on worker threads.
    /// Layer order matches WorldSurface: BeachSand, WetSand, Grass, Soil, Rock, SeabedSand, Silt, JungleGrass.
    public static class WorldSplat
    {
        public const int Layers = 8;

        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        static float SmoothStep(float e0, float e1, float x)
        {
            float t = Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        /// heights: normalized [z, x] heightmap of resolution hRes covering terrainSize meters starting at (originX, originZ),
        /// heightScale/minY convert to meters. Returns alphamaps [z, x, layer] of resolution aRes.
        public static float[,,] Compute(float[,] heights, int hRes, float terrainSize, float originX, float originZ,
                                        float minY, float heightScale, int aRes, Action<int> rowDone = null)
        {
            var map = new float[aRes, aRes, Layers];
            float cell = terrainSize / (hRes - 1);           // meters per heightmap sample
            float texel = terrainSize / (aRes - 1);          // terrain shaders map texel i to i / (res - 1)

            Parallel.For(0, aRes, az =>
            {
                var w = new float[Layers];
                for (int ax = 0; ax < aRes; ax++)
                {
                    float wx = originX + ax * texel;
                    float wz = originZ + az * texel;
                    float fx = (wx - originX) / cell, fz = (wz - originZ) / cell;
                    int ix = (int)fx, iz = (int)fz;
                    if (ix < 1) ix = 1; if (ix > hRes - 3) ix = hRes - 3;
                    if (iz < 1) iz = 1; if (iz > hRes - 3) iz = hRes - 3;
                    float tx = Clamp01(fx - ix), tz = Clamp01(fz - iz);

                    float h00 = heights[iz, ix], h10 = heights[iz, ix + 1], h01 = heights[iz + 1, ix], h11 = heights[iz + 1, ix + 1];
                    float hn = (h00 * (1 - tx) + h10 * tx) * (1 - tz) + (h01 * (1 - tx) + h11 * tx) * tz;
                    float h = minY + hn * heightScale;

                    // slope from central differences around the cell
                    float gx = (heights[iz, ix + 2] - heights[iz, ix - 1] + heights[iz + 1, ix + 2] - heights[iz + 1, ix - 1]) * heightScale / (6f * cell);
                    float gz = (heights[iz + 2, ix] - heights[iz - 1, ix] + heights[iz + 2, ix + 1] - heights[iz - 1, ix + 1]) * heightScale / (6f * cell);
                    float slope = MathF.Atan(MathF.Sqrt(gx * gx + gz * gz)) * 57.29578f;

                    Weights(wx, wz, h, slope, w);
                    for (int l = 0; l < Layers; l++) map[az, ax, l] = w[l];
                }
                rowDone?.Invoke(az);
            });
            return map;
        }

        /// Layer weights (normalized) for one point.
        public static void Weights(float x, float z, float h, float slope, float[] w)
        {
            float n1 = WorldNoise.Fbm(x * 0.03f + 17f, z * 0.03f - 5f, 3);
            float n2 = WorldNoise.Perlin(x * 0.11f + 2.5f, z * 0.11f + 9.5f);

            float landT = SmoothStep(-0.25f, 0.25f, h);
            float waterT = 1f - landT;

            // above water: sand near the sea, jungle grass/soil above ~3 m
            float grassT = SmoothStep(1.6f, 2.8f, h + 0.7f * n1 + 0.35f * n2);
            float yard = WorldHeightField.YardHint(x, z);
            grassT *= 1f - 0.85f * yard;
            float beach = landT * (1f - grassT);
            float green = landT * grassT;
            // forest-floor soil patches, more of it on the steeper jungle slopes
            float soilFrac = Clamp01(SmoothStep(0.05f, 0.45f, 0.8f * n1 + 0.4f * WorldNoise.Perlin(x * 0.05f + 3f, z * 0.05f)) * 0.7f
                                     + 0.35f * SmoothStep(16f, 30f, slope));
            float soil = green * soilFrac;
            float grass = green * (1f - soilFrac);
            // trampled yards (hut, helipad, lighthouse) mix in sandy soil
            soil += landT * yard * 0.55f;
            beach += landT * yard * 0.35f;

            // wet band at the waterline
            float wet = SmoothStep(-1.4f, -0.35f, h) * (1f - SmoothStep(0.35f, 0.95f, h + 0.25f * n2));

            // underwater: seabed sand, dark silt in the deep
            float deepT = SmoothStep(-45f, -62f, h + 5f * n1);
            float seabed = waterT * (1f - deepT);
            float silt = waterT * deepT;

            // rock on steep slopes and on rocky sites
            float rockSlope = h > 0f ? SmoothStep(30f, 42f, slope + 7f * n2) : SmoothStep(26f, 38f, slope + 7f * n2);
            float hint = h < -0.8f ? WorldHeightField.RockHint(x, z) * Clamp01(0.75f + 0.5f * n2) : 0f;
            float rock = Clamp01(MathF.Max(rockSlope, hint));

            // two grass scales blended by low-frequency noise hide texture tiling; the dark jungle variant
            // dominates in the wetter interior and on the hills
            float jungle = Clamp01(SmoothStep(-0.25f, 0.3f, WorldNoise.Fbm(x * 0.012f - 4f, z * 0.012f + 6f, 3)) * 0.85f
                                   + 0.3f * SmoothStep(6f, 18f, h));

            float keep = 1f - rock;
            w[0] = beach * keep * (1f - wet);
            w[1] = wet * keep;
            w[2] = grass * (1f - jungle) * keep * (1f - wet);
            w[3] = soil * keep * (1f - wet);
            w[4] = rock;
            w[5] = seabed * keep * (1f - wet);
            w[6] = silt * keep;
            w[7] = grass * jungle * keep * (1f - wet);

            float sum = 0f;
            for (int i = 0; i < Layers; i++) sum += w[i];
            if (sum < 1e-4f) { w[5] = 1f; sum = 1f; for (int i = 0; i < Layers; i++) if (i != 5) w[i] = 0f; }
            float inv = 1f / sum;
            for (int i = 0; i < Layers; i++) w[i] *= inv;
        }
    }
}
