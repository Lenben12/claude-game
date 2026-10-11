using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Sealebrities
{
    /// Animated caustics projected by the sun (light cookie) while the camera is underwater, shallow and it is day.
    /// 16 tileable, looping frames are generated on a worker thread; the cookie texture is re-composed ~15x per
    /// second by blending neighbouring frames and fading toward white (= no effect) with the caustic strength.
    /// Low-poly look (art bible §2.3): a clean light web, cell ≈ 1.6 m, +25 % brightness on the lines (the cookie
    /// never darkens the sun by more than ~20 %), fully on to 8 m, gone by 18 m. Off on the Low graphics tier.
    sealed class FXCaustics
    {
        const int Size = 256;
        const int FrameCount = 16;
        const float LoopSeconds = 3.6f;
        const float UploadInterval = 1f / 12f;
        const float TileMeters = 9.6f;   // 6 cells across the tile -> 1.6 m cells
        const float FadeStart = 8f;
        const float MaxDepth = 18f;
        const float MaxStrength = 0.8f;
        const int Seed = 4711;

        bool tierEnabled = true;

        public void SetTier(GraphicsQuality q)
        {
            tierEnabled = q != GraphicsQuality.Low;
            if (!tierEnabled) Release();
        }

        Task<byte[][]> task;
        byte[][] frames;
        bool failed;

        Texture2D tex;
        byte[] work;
        int bpp = 1;

        Light sun;
        UniversalAdditionalLightData sunData;
        bool applied;

        float strength, phase, uploadTimer;
        Vector2 offset;

        public FXCaustics()
        {
            try
            {
                task = Task.Run(() => FXCausticGen.Generate(Size, FrameCount, Seed));
            }
            catch (Exception)
            {
                task = null;
                frames = FXCausticGen.Generate(Size, FrameCount, Seed);
            }
        }

        public void Dispose()
        {
            Release();
            if (tex != null) UnityEngine.Object.Destroy(tex);
            tex = null;
        }

        bool EnsureReady()
        {
            if (tex != null) return true;
            if (failed) return false;
            if (frames == null)
            {
                if (task == null || !task.IsCompleted) return false;
                if (task.IsFaulted || task.IsCanceled)
                {
                    failed = true;
                    Debug.LogWarning("Sealebrities FX: caustics generation failed: " + (task.Exception != null ? task.Exception.GetBaseException().Message : "cancelled"));
                    task = null;
                    return false;
                }
                frames = task.Result;
                task = null;
            }
            BuildTexture();
            return tex != null;
        }

        void BuildTexture()
        {
            bool r8 = SystemInfo.SupportsTextureFormat(TextureFormat.R8);
            bpp = r8 ? 1 : 4;
            tex = new Texture2D(Size, Size, r8 ? TextureFormat.R8 : TextureFormat.RGBA32, true, true)
            {
                name = "FX_Caustics",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 2
            };
            work = new byte[Size * Size * bpp];
            Compose(0f, 0f);
        }

        /// Blend frame(phase) into the texture; s = 0 gives plain white (cookie without effect).
        void Compose(float ph, float s)
        {
            float fp = ph * FrameCount;
            int i0 = (int)fp;
            float frac = fp - i0;
            i0 %= FrameCount;
            int i1 = (i0 + 1) % FrameCount;
            int t8 = (int)(frac * 256f);
            int it8 = 256 - t8;
            int s8 = (int)(FXMath.Clamp01(s) * 256f);
            byte[] a = frames[i0], b = frames[i1];
            int n = Size * Size;
            if (bpp == 1)
            {
                for (int i = 0; i < n; i++)
                {
                    int v = (a[i] * it8 + b[i] * t8) >> 8;
                    work[i] = (byte)(255 - (((255 - v) * s8) >> 8));
                }
            }
            else
            {
                for (int i = 0, o = 0; i < n; i++, o += 4)
                {
                    int v = (a[i] * it8 + b[i] * t8) >> 8;
                    byte c = (byte)(255 - (((255 - v) * s8) >> 8));
                    work[o] = c; work[o + 1] = c; work[o + 2] = c; work[o + 3] = 255;
                }
            }
            tex.SetPixelData(work, 0);
            tex.Apply(true, false);
        }

        void Release()
        {
            if (applied && sun != null) sun.cookie = null;
            applied = false;
            strength = 0f;
        }

        public void Tick(FXFrame f)
        {
            Light s = WorldInfo.Sun;
            if (s != sun)
            {
                Release();
                sun = s;
                sunData = null;
            }
            if (sun == null || !tierEnabled || !EnsureReady()) return;

            float depthFade = 1f - FXMath.SmoothRange(FadeStart, MaxDepth, f.depth);
            float target = (f.underwater && f.depth < MaxDepth) ? MaxStrength * f.under01 * f.day01 * depthFade : 0f;
            if (!f.underwater || (target <= 0f && strength <= 0.01f))
            {
                if (applied) Release();
                return;
            }
            strength = Mathf.MoveTowards(strength, target, f.dt * 1.5f);
            if (strength <= 0.01f)
            {
                if (applied) Release();
                return;
            }

            phase += f.dt / LoopSeconds;
            if (phase >= 1f) phase -= Mathf.Floor(phase);
            uploadTimer += f.dt;
            if (uploadTimer >= UploadInterval || !applied)
            {
                uploadTimer = 0f;
                Compose(phase, strength);
            }

            if (!applied)
            {
                if (sunData == null) sunData = sun.GetUniversalAdditionalLightData();
                sun.cookie = tex;
                if (sunData != null) sunData.lightCookieSize = new Vector2(TileMeters, TileMeters);
                applied = true;
            }

            // slow drift hides the tiling
            offset.x += f.dt * 0.21f;
            offset.y += f.dt * 0.13f;
            if (offset.x > TileMeters) offset.x -= TileMeters;
            if (offset.y > TileMeters) offset.y -= TileMeters;
            if (sunData != null) sunData.lightCookieOffset = offset;
        }
    }

    /// Thread-safe (no Unity API) generator of tileable, looping caustic frames: two periodic Voronoi layers whose
    /// feature points orbit on closed paths, rendered as a bright web where neighbouring cells meet (F2 - F1).
    static class FXCausticGen
    {
        sealed class Layer
        {
            readonly int n;
            readonly float[] bx, by, r1, p1, r2, p2;
            readonly int[] k1, k2;
            readonly float[] px, py;

            public Layer(int cells, System.Random rng, int dir)
            {
                n = cells;
                int c = n * n;
                bx = new float[c]; by = new float[c]; r1 = new float[c]; p1 = new float[c];
                r2 = new float[c]; p2 = new float[c]; k1 = new int[c]; k2 = new int[c];
                px = new float[c]; py = new float[c];
                for (int i = 0; i < c; i++)
                {
                    // strong jitter breaks the grid look; |offset| stays < 0.5 so a 3x3 search is exact
                    bx[i] = 0.5f + (float)(rng.NextDouble() - 0.5) * 0.6f;
                    by[i] = 0.5f + (float)(rng.NextDouble() - 0.5) * 0.6f;
                    r1[i] = 0.05f + (float)rng.NextDouble() * 0.07f;
                    r2[i] = 0.015f + (float)rng.NextDouble() * 0.025f;
                    p1[i] = (float)rng.NextDouble() * FXMath.TwoPi;
                    p2[i] = (float)rng.NextDouble() * FXMath.TwoPi;
                    k1[i] = rng.NextDouble() < 0.75 ? dir : -dir;
                    k2[i] = rng.NextDouble() < 0.5 ? 2 : -2;
                }
            }

            /// t in [0,1): integer angular frequencies make the motion loop exactly.
            public void SetTime(float t)
            {
                for (int i = 0; i < px.Length; i++)
                {
                    float a1 = FXMath.TwoPi * k1[i] * t + p1[i];
                    float a2 = FXMath.TwoPi * k2[i] * t + p2[i];
                    px[i] = bx[i] + r1[i] * MathF.Cos(a1) + r2[i] * MathF.Cos(a2);
                    py[i] = by[i] + r1[i] * MathF.Sin(a1) + r2[i] * MathF.Sin(a2);
                }
            }

            /// Distance to the nearest cell border (F2 - F1) in cell units at torus coordinates u, v.
            public float Edge(float u, float v)
            {
                u -= MathF.Floor(u);
                v -= MathF.Floor(v);
                float fx = u * n, fy = v * n;
                int ci = (int)fx, cj = (int)fy;
                if (ci >= n) ci = n - 1;
                if (cj >= n) cj = n - 1;
                fx -= ci; fy -= cj;
                float m1 = float.MaxValue, m2 = float.MaxValue;
                for (int dj = -1; dj <= 1; dj++)
                {
                    int row = cj + dj;
                    if (row < 0) row += n; else if (row >= n) row -= n;
                    for (int di = -1; di <= 1; di++)
                    {
                        int col = ci + di;
                        if (col < 0) col += n; else if (col >= n) col -= n;
                        int idx = row * n + col;
                        float dx = di + px[idx] - fx;
                        float dy = dj + py[idx] - fy;
                        float d2 = dx * dx + dy * dy;
                        if (d2 < m1) { m2 = m1; m1 = d2; }
                        else if (d2 < m2) m2 = d2;
                    }
                }
                return MathF.Sqrt(m2) - MathF.Sqrt(m1);
            }

            public float CellPixels(int size) => size / (float)n;
        }

        public static byte[][] Generate(int size, int frames, int seed)
        {
            var rng = new System.Random(seed);
            var a = new Layer(6, rng, 1);
            var b = new Layer(11, rng, -1);
            float cellA = a.CellPixels(size), cellB = b.CellPixels(size);
            var result = new byte[frames][];
            float inv = 1f / size;
            const float Tau = FXMath.TwoPi;
            for (int fr = 0; fr < frames; fr++)
            {
                float t = fr / (float)frames;
                float tw = Tau * t;
                a.SetTime(t);
                b.SetTime(t);
                var buf = new byte[size * size];
                for (int y = 0; y < size; y++)
                {
                    float v = (y + 0.5f) * inv;
                    for (int x = 0; x < size; x++)
                    {
                        float u = (x + 0.5f) * inv;
                        // periodic domain warp (integer frequencies keep it tileable and looping): rounded, curvy cells
                        float wu = u + 0.03f * MathF.Sin(Tau * 2f * v + tw) + 0.012f * MathF.Sin(Tau * (3f * u + 4f * v) - tw);
                        float wv = v + 0.03f * MathF.Sin(Tau * 2f * u - tw + 1.3f) + 0.012f * MathF.Sin(Tau * (5f * u - 2f * v) + tw);
                        // line thickness varies slowly over the tile (focused vs. diffuse caustics)
                        float widthMod = 0.5f + 0.5f * MathF.Sin(Tau * (u + 2f * v) + tw + 0.7f);
                        float ea = a.Edge(wu, wv) * cellA;   // pixels
                        float eb = b.Edge(wu + 0.37f, wv + 0.11f) * cellB;
                        float ca = Band(ea, 1.5f + 1.3f * widthMod, 0.9f, 4.5f);
                        float cb = Band(eb, 0.9f, 0.8f, 3f) * 0.35f;
                        float c = ca + cb * (1f - ca);
                        // low contrast: between the lines the cookie keeps 78 % of the sunlight at full strength
                        // (~82 % at the MaxStrength cap), the lines get the full sun
                        float val = 0.78f + 0.22f * FXMath.Clamp01(c);
                        buf[y * size + x] = (byte)(val * 255f + 0.5f);
                    }
                }
                result[fr] = buf;
            }
            return result;
        }

        /// Clean graphic light line (flat band with an anti-aliased edge) plus a faint glow;
        /// e = distance to the cell border in pixels.
        static float Band(float e, float halfWidth, float soft, float glow)
        {
            float x = (e - (halfWidth - soft)) / (2f * soft);
            x = FXMath.Clamp01(x);
            float core = 1f - x * x * (3f - 2f * x);
            float halo = MathF.Exp(-e / glow) * 0.18f;
            return FXMath.Clamp01(core + halo * (1f - core));
        }
    }
}
