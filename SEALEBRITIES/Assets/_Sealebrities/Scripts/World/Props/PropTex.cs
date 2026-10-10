using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Procedural textures of the props module (albedo + normal maps), generated once and cached.
    /// Height-derived normal maps are stored as RGB normals (works with both DXT5nm-style and RGB unpacking).
    public static class PropTex
    {
        static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();
        const float TAU = Mathf.PI * 2f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Cache.Clear(); }

        static Texture2D Get(string key, Action gen)
        {
            if (Cache.TryGetValue(key, out var t) && t != null) return t;
            gen();
            if (Cache.TryGetValue(key, out t) && t != null) return t;
            return key.EndsWith("_n") ? Texture2D.normalTexture : Texture2D.grayTexture;
        }

        static float Frac(float v) => v - Mathf.Floor(v);
        static float S(float a, float b, float x) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((x - a) / (b - a)));

        static Texture2D Store(string key, int w, int h, Color32[] px, bool linear = false, bool alphaMips = false,
            TextureWrapMode wu = TextureWrapMode.Repeat, TextureWrapMode wv = TextureWrapMode.Repeat)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true, linear)
            {
                name = "PropTex_" + key,
                wrapModeU = wu,
                wrapModeV = wv,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4
            };
            if (alphaMips)
            {
                WriteAlphaMips(t, px, w, h);
                t.Apply(false, true);
            }
            else
            {
                t.SetPixels32(px);
                t.Apply(true, true);
            }
            Cache[key] = t;
            return t;
        }

        /// Box-filtered mips that keep cut-out coverage (alpha is boosted on smaller mips) and avoid dark fringes.
        static void WriteAlphaMips(Texture2D t, Color32[] src, int w, int h)
        {
            t.SetPixels32(src, 0);
            int mips = t.mipmapCount;
            var cur = src;
            int cw = w, ch = h;
            for (int m = 1; m < mips; m++)
            {
                int nw = Mathf.Max(1, cw / 2), nh = Mathf.Max(1, ch / 2);
                var next = new Color32[nw * nh];
                float boost = 1f + 0.3f * m;
                for (int y = 0; y < nh; y++)
                {
                    for (int x = 0; x < nw; x++)
                    {
                        float r = 0, g = 0, b = 0, a = 0, ra = 0, ga = 0, ba = 0;
                        for (int k = 0; k < 4; k++)
                        {
                            int sx = Mathf.Min(cw - 1, x * 2 + (k & 1)), sy = Mathf.Min(ch - 1, y * 2 + (k >> 1));
                            var c = cur[sy * cw + sx];
                            r += c.r; g += c.g; b += c.b; a += c.a;
                            ra += c.r * c.a; ga += c.g * c.a; ba += c.b * c.a;
                        }
                        Color32 o;
                        if (a > 0.5f) o = new Color32((byte)(ra / a), (byte)(ga / a), (byte)(ba / a), 0);
                        else o = new Color32((byte)(r / 4), (byte)(g / 4), (byte)(b / 4), 0);
                        o.a = (byte)Mathf.Min(255f, a / 4f * boost);
                        next[y * nw + x] = o;
                    }
                }
                t.SetPixels32(next, m);
                cur = next; cw = nw; ch = nh;
            }
        }

        static Texture2D StoreNormal(string key, float[] hgt, int w, int h, float strength, bool wrap = true)
        {
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                int yd = wrap ? (y - 1 + h) % h : Mathf.Max(0, y - 1);
                int yu = wrap ? (y + 1) % h : Mathf.Min(h - 1, y + 1);
                for (int x = 0; x < w; x++)
                {
                    int xl = wrap ? (x - 1 + w) % w : Mathf.Max(0, x - 1);
                    int xr = wrap ? (x + 1) % w : Mathf.Min(w - 1, x + 1);
                    float dx = (hgt[y * w + xr] - hgt[y * w + xl]) * strength;
                    float dy = (hgt[yu * w + x] - hgt[yd * w + x]) * strength;
                    var n = new Vector3(-dx, -dy, 1f).normalized;
                    px[y * w + x] = new Color32((byte)((n.x * 0.5f + 0.5f) * 255f), (byte)((n.y * 0.5f + 0.5f) * 255f), (byte)((n.z * 0.5f + 0.5f) * 255f), 255);
                }
            }
            return Store(key, w, h, px, true, false, wrap ? TextureWrapMode.Repeat : TextureWrapMode.Clamp, wrap ? TextureWrapMode.Repeat : TextureWrapMode.Clamp);
        }

        // =================================================================== wood planks
        public static Texture2D Planks => Get("planks", GenPlanks);
        public static Texture2D PlanksN => Get("planks_n", GenPlanks);

        static void GenPlanks()
        {
            const int Sz = 512, rows = 8;
            var px = new Color32[Sz * Sz];
            var hg = new float[Sz * Sz];
            Color warm = new Color(0.50f, 0.37f, 0.25f), gray = new Color(0.62f, 0.59f, 0.54f), gap = new Color(0.09f, 0.07f, 0.05f);
            Color rust = new Color(0.40f, 0.22f, 0.11f), nail = new Color(0.22f, 0.20f, 0.19f);
            for (int y = 0; y < Sz; y++)
            {
                float v = (y + 0.5f) / Sz;
                int row = Mathf.Min(rows - 1, (int)(v * rows));
                float fv = v * rows - row;
                float seamOff = PropNoise.Hash01(row, 7, 101);
                for (int x = 0; x < Sz; x++)
                {
                    float u = (x + 0.5f) / Sz;
                    float uu = u * 2f + seamOff * 2f;
                    int seg = Mathf.FloorToInt(uu);
                    float fu = uu - seg;
                    int segW = ((seg % 2) + 2) % 2;
                    float boardRand = PropNoise.Hash01(row, segW, 202);
                    float grain = PropNoise.Fbm(u * 4f, v * 64f, 4, 11, 4, 64);
                    float figure = Frac(fv * 2.2f + PropNoise.Fbm(u * 3f, v * 8f, 3, 12, 3, 8) * 3f + boardRand * 7f);
                    float weather = PropNoise.Fbm(u * 6f, v * 6f, 4, 13, 6, 6);
                    Color c = Color.Lerp(warm, gray, Mathf.Clamp01(0.2f + weather * 0.75f + (boardRand - 0.5f) * 0.55f));
                    c *= 0.78f + grain * 0.38f;
                    c *= 1f - 0.13f * (1f - S(0f, 0.12f, figure));
                    float edgeV = Mathf.Min(fv, 1f - fv) * (Sz / (float)rows);
                    float edgeU = Mathf.Min(fu, 1f - fu) * (Sz / 2f);
                    float edge = Mathf.Min(edgeV, edgeU);
                    float bevel = S(1.5f, 6f, edge);
                    float h = bevel * (0.75f + 0.25f * grain);
                    if (edge < 1.5f) c = gap;
                    else c *= 0.72f + 0.28f * bevel;
                    float nu = edgeU - 10f;
                    float nv1 = (fv - 0.3f) * (Sz / (float)rows), nv2 = (fv - 0.7f) * (Sz / (float)rows);
                    float nd = Mathf.Sqrt(nu * nu + Mathf.Min(nv1 * nv1, nv2 * nv2));
                    if (nd < 6f) c = Color.Lerp(c, rust, (1f - nd / 6f) * 0.55f);
                    if (nd < 2.2f) { c = nail; h -= 0.08f; }
                    c.a = 1f;
                    px[y * Sz + x] = c;
                    hg[y * Sz + x] = h;
                }
            }
            Store("planks", Sz, Sz, px);
            StoreNormal("planks_n", hg, Sz, Sz, 3f);
        }

        // =================================================================== corrugated rusty roof
        public static Texture2D Corrugated => Get("corr", GenCorrugated);
        public static Texture2D CorrugatedN => Get("corr_n", GenCorrugated);

        static void GenCorrugated()
        {
            const int Sz = 256;
            var px = new Color32[Sz * Sz];
            var hg = new float[Sz * Sz];
            for (int y = 0; y < Sz; y++)
            {
                float v = (y + 0.5f) / Sz;
                for (int x = 0; x < Sz; x++)
                {
                    float u = (x + 0.5f) / Sz;
                    float ridge = 0.5f + 0.5f * Mathf.Cos(u * 16f * TAU);
                    float n = PropNoise.Fbm(u * 4f, v * 4f, 4, 21, 4, 4);
                    float streak = PropNoise.Fbm(u * 24f, v * 2f, 3, 22, 24, 2);
                    float rustMask = S(0.42f, 0.62f, n * 0.75f + streak * 0.45f);
                    Color zinc = new Color(0.58f, 0.6f, 0.61f) * (0.82f + 0.25f * PropNoise.Fbm(u * 16f, v * 16f, 2, 23, 16, 16));
                    Color rust = Color.Lerp(new Color(0.34f, 0.16f, 0.07f), new Color(0.62f, 0.33f, 0.13f), PropNoise.Fbm(u * 12f, v * 12f, 3, 24, 12, 12));
                    Color c = Color.Lerp(zinc, rust, rustMask);
                    c *= 0.8f + 0.2f * ridge;
                    c.a = 1f;
                    px[y * Sz + x] = c;
                    hg[y * Sz + x] = ridge * 0.9f + rustMask * 0.1f * n;
                }
            }
            Store("corr", Sz, Sz, px);
            StoreNormal("corr_n", hg, Sz, Sz, 2.5f);
        }

        // =================================================================== palm bark
        public static Texture2D Bark => Get("bark", GenBark);
        public static Texture2D BarkN => Get("bark_n", GenBark);

        static void GenBark()
        {
            const int Sz = 256;
            var px = new Color32[Sz * Sz];
            var hg = new float[Sz * Sz];
            for (int y = 0; y < Sz; y++)
            {
                float v = (y + 0.5f) / Sz;
                for (int x = 0; x < Sz; x++)
                {
                    float u = (x + 0.5f) / Sz;
                    float fr = Frac(v * 10f + PropNoise.Fbm(u * 4f, v * 2f, 2, 31, 4, 2) * 0.25f);
                    float groove = 1f - S(0f, 0.18f, fr);
                    float bulge = S(0f, 0.5f, fr) * (1f - 0.5f * S(0.6f, 1f, fr));
                    float fib = PropNoise.Fbm(u * 40f, v * 3f, 3, 32, 40, 3);
                    Color c = Color.Lerp(new Color(0.42f, 0.37f, 0.31f), new Color(0.56f, 0.51f, 0.44f), PropNoise.Fbm(u * 3f, v * 5f, 3, 33, 3, 5));
                    float lichen = S(0.62f, 0.72f, PropNoise.Fbm(u * 8f, v * 8f, 4, 34, 8, 8));
                    c = Color.Lerp(c, new Color(0.58f, 0.6f, 0.5f), lichen * 0.6f);
                    c *= (0.8f + 0.35f * fib) * (1f - 0.45f * groove);
                    c.a = 1f;
                    px[y * Sz + x] = c;
                    hg[y * Sz + x] = bulge * 0.7f + fib * 0.3f - groove * 0.3f;
                }
            }
            Store("bark", Sz, Sz, px);
            StoreNormal("bark_n", hg, Sz, Sz, 4f);
        }

        // =================================================================== leaves (alpha cut-out)
        public static Texture2D PalmLeaf => Get("leaf_palm", () => GenLeaf("leaf_palm", 26, 0.95f, 0.66f,
            new Color(0.17f, 0.36f, 0.09f), new Color(0.40f, 0.48f, 0.15f), new Color(0.55f, 0.55f, 0.25f), 0.12f, new Color(0.52f, 0.40f, 0.20f), false));

        public static Texture2D PalmLeafDry => Get("leaf_palm_dry", () => GenLeaf("leaf_palm_dry", 26, 0.95f, 0.5f,
            new Color(0.45f, 0.33f, 0.18f), new Color(0.58f, 0.46f, 0.28f), new Color(0.6f, 0.5f, 0.32f), 0.6f, new Color(0.35f, 0.25f, 0.14f), false));

        public static Texture2D FernLeaf => Get("leaf_fern", () => GenLeaf("leaf_fern", 22, 0.45f, 0.72f,
            new Color(0.14f, 0.32f, 0.08f), new Color(0.30f, 0.46f, 0.12f), new Color(0.25f, 0.35f, 0.12f), 0.05f, new Color(0.45f, 0.38f, 0.18f), true));

        static void GenLeaf(string key, int leaflets, float slant, float coverage, Color baseC, Color tipC, Color ribC, float dryChance, Color dryC, bool fern)
        {
            const int Sz = 256;
            var px = new Color32[Sz * Sz];
            float aa = leaflets / (float)Sz * 1.5f;
            for (int y = 0; y < Sz; y++)
            {
                float v = (y + 0.5f) / Sz;
                float L = fern
                    ? Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Clamp01(v)), 0.8f) * (1f - 0.35f * v)
                    : Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Clamp01(v * 0.9f + 0.08f)), 0.55f) * Mathf.Clamp01(v / 0.06f);
                L = Mathf.Max(0.001f, L * 0.98f);
                for (int x = 0; x < Sz; x++)
                {
                    float u = (x + 0.5f) / Sz;
                    float d = Mathf.Abs(u - 0.5f) * 2f;
                    int side = u < 0.5f ? 0 : 1;
                    Color c = baseC;
                    float alpha = 0f;
                    if (d < 0.035f && v < 0.985f)
                    {
                        c = ribC;
                        alpha = 1f;
                    }
                    else if (d < L)
                    {
                        float phase = v * leaflets - d * slant + side * 0.5f;
                        int li = Mathf.FloorToInt(phase);
                        float f = phase - li;
                        float taper = 1f - d / L;
                        float halfW = coverage * 0.5f * Mathf.Sqrt(Mathf.Clamp01(taper)) * (0.65f + 0.35f * (1f - d));
                        float dist = Mathf.Abs(f - 0.5f);
                        alpha = Mathf.Clamp01((halfW - dist) / aa + 0.5f);
                        float dl = d / L;
                        float n = PropNoise.Fbm(u * 8f, v * 8f, 3, 41 + side, 8, 8);
                        c = Color.Lerp(baseC, tipC, dl * 0.8f + n * 0.25f) * (0.85f + 0.25f * n);
                        if (dist < halfW * 0.18f) c = Color.Lerp(c, ribC, 0.35f);
                        c *= 0.85f + 0.15f * Mathf.Clamp01(1f - dist / Mathf.Max(0.001f, halfW));
                        if (PropNoise.Hash01(li, side, 77) < dryChance) c = Color.Lerp(c, dryC, S(0.55f, 0.9f, dl));
                    }
                    if (alpha <= 0.001f) c = baseC;
                    c.a = alpha;
                    px[y * Sz + x] = c;
                }
            }
            Store(key, Sz, Sz, px, false, true, TextureWrapMode.Clamp, TextureWrapMode.Clamp);
        }

        /// Cluster of small leaves for shrub cards.
        public static Texture2D LeafClump => Get("leaf_clump", GenLeafClump);

        static void GenLeafClump()
        {
            const int Sz = 256;
            var px = new Color32[Sz * Sz];
            Color fill = new Color(0.16f, 0.32f, 0.09f, 0f);
            for (int i = 0; i < px.Length; i++) px[i] = fill;
            var rng = new System.Random(4242);
            for (int l = 0; l < 95; l++)
            {
                float r = Mathf.Sqrt((float)rng.NextDouble()) * 0.4f;
                float a = (float)rng.NextDouble() * TAU;
                float cx = 0.5f + Mathf.Cos(a) * r, cy = 0.5f + Mathf.Sin(a) * r * 0.9f;
                float ang = a + PropUtil.Range(rng, -0.6f, 0.6f);
                float len = PropUtil.Range(rng, 0.07f, 0.13f), wid = len * PropUtil.Range(rng, 0.32f, 0.45f);
                Color col = new Color(PropUtil.Range(rng, 0.13f, 0.30f), PropUtil.Range(rng, 0.30f, 0.50f), PropUtil.Range(rng, 0.06f, 0.16f));
                float ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
                int x0 = Mathf.Max(0, (int)((cx - len) * Sz)), x1 = Mathf.Min(Sz - 1, (int)((cx + len) * Sz));
                int y0 = Mathf.Max(0, (int)((cy - len) * Sz)), y1 = Mathf.Min(Sz - 1, (int)((cy + len) * Sz));
                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        float dx = (x + 0.5f) / Sz - cx, dy = (y + 0.5f) / Sz - cy;
                        float lx = (dx * ca + dy * sa) / len;       // -1..1 along the leaf
                        float ly = (-dx * sa + dy * ca) / wid;      // across
                        float prof = Mathf.Pow(Mathf.Max(0f, 1f - lx * lx), 0.7f);
                        if (Mathf.Abs(ly) > prof) continue;
                        float shade = 0.75f + 0.25f * (lx * 0.5f + 0.5f);
                        Color c = col * shade;
                        if (Mathf.Abs(ly) < 0.08f) c *= 0.8f;
                        c.a = 1f;
                        px[y * Sz + x] = c;
                    }
                }
            }
            Store("leaf_clump", Sz, Sz, px, false, true, TextureWrapMode.Clamp, TextureWrapMode.Clamp);
        }

        /// Broad tropical leaf with midrib and lateral veins (opaque, mapped u across / v along one leaf).
        public static Texture2D BigLeaf => Get("leaf_big", GenBigLeaf);

        static void GenBigLeaf()
        {
            const int Sz = 256;
            var px = new Color32[Sz * Sz];
            for (int y = 0; y < Sz; y++)
            {
                float v = (y + 0.5f) / Sz;
                for (int x = 0; x < Sz; x++)
                {
                    float u = (x + 0.5f) / Sz;
                    float d = Mathf.Abs(u - 0.5f) * 2f;
                    float n = PropNoise.Fbm(u * 6f, v * 6f, 4, 51, 6, 6);
                    Color c = Color.Lerp(new Color(0.10f, 0.26f, 0.06f), new Color(0.22f, 0.42f, 0.11f), n * 0.8f + v * 0.2f);
                    float vein = Frac(v * 9f - d * 1.4f);
                    float vk = 1f - S(0f, 0.06f, Mathf.Min(vein, 1f - vein));
                    c = Color.Lerp(c, new Color(0.34f, 0.5f, 0.2f), vk * 0.55f * (1f - d * 0.6f));
                    if (d < 0.03f) c = new Color(0.42f, 0.55f, 0.24f);
                    c *= 0.9f + 0.15f * d;
                    c.a = 1f;
                    px[y * Sz + x] = c;
                }
            }
            Store("leaf_big", Sz, Sz, px, false, false, TextureWrapMode.Clamp, TextureWrapMode.Clamp);
        }

        // =================================================================== rock / concrete
        public static Texture2D Rock => Get("rock", GenRock);
        public static Texture2D RockN => Get("rock_n", GenRock);

        static void GenRock()
        {
            const int Sz = 256;
            var px = new Color32[Sz * Sz];
            var hg = new float[Sz * Sz];
            for (int y = 0; y < Sz; y++)
            {
                float v = (y + 0.5f) / Sz;
                for (int x = 0; x < Sz; x++)
                {
                    float u = (x + 0.5f) / Sz;
                    float n = PropNoise.Fbm(u * 4f, v * 4f, 5, 61, 4, 4);
                    float cr = PropNoise.Ridged(u * 5f, v * 5f, 3, 62, 5, 5);
                    float crack = S(0.82f, 0.95f, cr);
                    float spk = PropNoise.Hash01(x, y, 63);
                    float tone = PropNoise.Fbm(u * 2f, v * 2f, 3, 64, 2, 2);
                    Color c = Color.Lerp(new Color(0.36f, 0.35f, 0.34f), new Color(0.64f, 0.62f, 0.58f), n);
                    c = Color.Lerp(c, c * new Color(1.08f, 0.95f, 0.82f), tone);
                    c *= 0.9f + 0.18f * spk;
                    c *= 1f - 0.45f * crack;
                    c.a = 1f;
                    px[y * Sz + x] = c;
                    hg[y * Sz + x] = n * 0.8f + (1f - crack) * 0.2f + spk * 0.03f;
                }
            }
            Store("rock", Sz, Sz, px);
            StoreNormal("rock_n", hg, Sz, Sz, 5f);
        }

        public static Texture2D Concrete => Get("concrete", GenConcrete);
        public static Texture2D ConcreteN => Get("concrete_n", GenConcrete);

        static void GenConcrete()
        {
            const int Sz = 256;
            var px = new Color32[Sz * Sz];
            var hg = new float[Sz * Sz];
            for (int y = 0; y < Sz; y++)
            {
                float v = (y + 0.5f) / Sz;
                for (int x = 0; x < Sz; x++)
                {
                    float u = (x + 0.5f) / Sz;
                    float n = PropNoise.Fbm(u * 8f, v * 8f, 4, 71, 8, 8);
                    float stain = PropNoise.Fbm(u * 2f, v * 2f, 3, 72, 2, 2);
                    float pit = PropNoise.Hash01(x, y, 73) < 0.006f ? 1f : 0f;
                    Color c = new Color(0.62f, 0.61f, 0.58f) * (0.84f + 0.22f * n) * (1f - 0.2f * S(0.5f, 0.8f, stain));
                    c *= 1f - 0.4f * pit;
                    c.a = 1f;
                    px[y * Sz + x] = c;
                    hg[y * Sz + x] = n * 0.4f - pit * 0.3f;
                }
            }
            Store("concrete", Sz, Sz, px);
            StoreNormal("concrete_n", hg, Sz, Sz, 3f);
        }

        /// Helipad top (unit UV over the disk): concrete, yellow ring, white H, worn paint.
        public static Texture2D Helipad => Get("helipad", GenHelipad);
        public static Texture2D HelipadN => Get("helipad_n", GenHelipad);

        static void GenHelipad()
        {
            const int Sz = 512;
            var px = new Color32[Sz * Sz];
            var hg = new float[Sz * Sz];
            Color white = new Color(0.9f, 0.9f, 0.86f), yellow = new Color(0.93f, 0.72f, 0.12f);
            for (int y = 0; y < Sz; y++)
            {
                float v = (y + 0.5f) / Sz;
                for (int x = 0; x < Sz; x++)
                {
                    float u = (x + 0.5f) / Sz;
                    float px01 = (u - 0.5f) * 2f, pz = (v - 0.5f) * 2f;
                    float r = Mathf.Sqrt(px01 * px01 + pz * pz);
                    float n = PropNoise.Fbm(u * 24f, v * 24f, 4, 81);
                    float stain = PropNoise.Fbm(u * 5f, v * 5f, 3, 82);
                    float crack = S(0.9f, 0.97f, PropNoise.Ridged(u * 6f, v * 6f, 3, 83));
                    Color c = new Color(0.6f, 0.6f, 0.57f) * (0.82f + 0.24f * n) * (1f - 0.22f * S(0.45f, 0.8f, stain));
                    float wear = S(0.3f, 0.55f, PropNoise.Fbm(u * 18f, v * 18f, 4, 84));
                    float paint = 0f;
                    Color pc = white;
                    if (r > 0.78f && r < 0.86f) { paint = 1f; pc = yellow; }
                    float ax = Mathf.Abs(px01), az = Mathf.Abs(pz);
                    bool hBar = ax > 0.2f && ax < 0.34f && az < 0.44f;
                    bool hCross = ax <= 0.2f && az < 0.065f;
                    if (hBar || hCross) { paint = 1f; pc = white; }
                    if (r > 0.95f) c *= 0.75f;
                    c = Color.Lerp(c, pc * (0.9f + 0.1f * n), paint * (0.35f + 0.65f * wear));
                    c *= 1f - 0.5f * crack;
                    c.a = 1f;
                    px[y * Sz + x] = c;
                    hg[y * Sz + x] = n * 0.35f + paint * 0.05f - crack * 0.25f;
                }
            }
            Store("helipad", Sz, Sz, px, false, false, TextureWrapMode.Clamp, TextureWrapMode.Clamp);
            StoreNormal("helipad_n", hg, Sz, Sz, 2.5f, false);
        }

        /// Lighthouse paint: 4 white/red bands along v (0 = bottom of the tower), dirt streaks, peeling paint.
        public static Texture2D Lighthouse => Get("lighthouse", GenLighthouse);

        static void GenLighthouse()
        {
            const int W = 256, H = 512;
            var px = new Color32[W * H];
            Color white = new Color(0.9f, 0.89f, 0.85f), red = new Color(0.66f, 0.1f, 0.08f);
            for (int y = 0; y < H; y++)
            {
                float v = (y + 0.5f) / H;
                for (int x = 0; x < W; x++)
                {
                    float u = (x + 0.5f) / W;
                    float wob = (PropNoise.Fbm(u * 8f, v * 2f, 2, 91, 8) - 0.5f) * 0.012f;
                    int band = Mathf.FloorToInt((v + wob) * 4f);
                    Color c = (band % 2 == 1) ? red : white;
                    float dirt = PropNoise.Fbm(u * 16f, v * 1.5f, 3, 92, 16);
                    c *= 1f - 0.22f * S(0.45f, 0.75f, dirt);
                    float peel = PropNoise.Fbm(u * 12f, v * 24f, 3, 93, 12);
                    if (peel > 0.7f) c = Color.Lerp(c, new Color(0.55f, 0.54f, 0.5f), 0.8f);
                    c *= 1f - 0.3f * (1f - S(0f, 0.06f, v));
                    c *= 0.93f + 0.1f * PropNoise.Fbm(u * 40f, v * 80f, 2, 94, 40);
                    c.a = 1f;
                    px[y * W + x] = c;
                }
            }
            Store("lighthouse", W, H, px, false, false, TextureWrapMode.Repeat, TextureWrapMode.Clamp);
        }

        // =================================================================== painted / rusty metal
        public static Texture2D PaintedMetal(string key, Color paint, float rust, int ridges = 0, bool plates = false)
            => Get("pm_" + key, () => GenPaintedMetal("pm_" + key, paint, rust, ridges, plates));
        public static Texture2D PaintedMetalN(string key, Color paint, float rust, int ridges = 0, bool plates = false)
            => Get("pm_" + key + "_n", () => GenPaintedMetal("pm_" + key, paint, rust, ridges, plates));

        static void GenPaintedMetal(string key, Color paint, float rust, int ridges, bool plates)
        {
            const int Sz = 256;
            var px = new Color32[Sz * Sz];
            var hg = new float[Sz * Sz];
            int sd = key.GetHashCode() & 0xFFFF;
            for (int y = 0; y < Sz; y++)
            {
                float v = (y + 0.5f) / Sz;
                for (int x = 0; x < Sz; x++)
                {
                    float u = (x + 0.5f) / Sz;
                    float n1 = PropNoise.Fbm(u * 5f, v * 5f, 4, sd, 5, 5);
                    float streak = PropNoise.Fbm(u * 30f, v * 2f, 3, sd + 1, 30, 2);
                    float rustMask = S(1f - rust, 1f - rust + 0.22f, n1 * 0.7f + streak * 0.42f);
                    float chipN = PropNoise.Fbm(u * 20f, v * 20f, 3, sd + 2, 20, 20);
                    Color pc = paint * (0.85f + 0.2f * PropNoise.Fbm(u * 10f, v * 10f, 2, sd + 3, 10, 10));
                    if (chipN > 0.7f) pc = Color.Lerp(pc, new Color(0.45f, 0.3f, 0.25f), 0.7f);
                    Color rc = Color.Lerp(new Color(0.3f, 0.14f, 0.06f), new Color(0.6f, 0.32f, 0.12f), PropNoise.Fbm(u * 14f, v * 14f, 3, sd + 4, 14, 14));
                    Color c = Color.Lerp(pc, rc, rustMask);
                    float h = (1f - rustMask) * 0.12f + rustMask * n1 * 0.15f;
                    if (ridges > 0)
                    {
                        float ridge = 0.5f + 0.5f * Mathf.Cos(u * ridges * TAU);
                        c *= 0.84f + 0.16f * ridge;
                        h += ridge * 0.8f;
                    }
                    if (plates)
                    {
                        float fv = Frac(v * 2f), fu = Frac(u * 2f + (v * 2f >= 1f ? 0.5f : 0f));
                        float e = Mathf.Min(Mathf.Min(fv, 1f - fv) * Sz * 0.5f, Mathf.Min(fu, 1f - fu) * Sz * 0.5f);
                        if (e < 2.5f) { c *= 0.7f; h += 0.2f; }
                    }
                    c.a = 1f;
                    px[y * Sz + x] = c;
                    hg[y * Sz + x] = h;
                }
            }
            Store(key, Sz, Sz, px);
            StoreNormal(key + "_n", hg, Sz, Sz, ridges > 0 ? 2.5f : 3f);
        }

        /// Sunken ship hull plating: plates, weld seams, rust streaks, algae/encrusting growth.
        public static Texture2D Hull(string key, Color paint) => Get("hull_" + key, () => GenHull("hull_" + key, paint));
        public static Texture2D HullN(string key, Color paint) => Get("hull_" + key + "_n", () => GenHull("hull_" + key, paint));

        static void GenHull(string key, Color paint)
        {
            const int Sz = 256;
            var px = new Color32[Sz * Sz];
            var hg = new float[Sz * Sz];
            int sd = key.GetHashCode() & 0xFFFF;
            for (int y = 0; y < Sz; y++)
            {
                float v = (y + 0.5f) / Sz;
                int row = Mathf.FloorToInt(v * 4f);
                float fv = v * 4f - row;
                for (int x = 0; x < Sz; x++)
                {
                    float u = (x + 0.5f) / Sz;
                    float fu = Frac(u * 2f + (row % 2) * 0.5f);
                    float seam = Mathf.Min(Mathf.Min(fv, 1f - fv) * Sz / 4f, Mathf.Min(fu, 1f - fu) * Sz / 2f);
                    float weld = 1f - S(0.5f, 2.5f, seam);
                    float n = PropNoise.Fbm(u * 6f, v * 6f, 4, sd, 6, 6);
                    float drip = PropNoise.Fbm(u * 40f, v * 2f, 3, sd + 1, 40, 2);
                    float rustMask = S(0.48f, 0.72f, n * 0.6f + drip * 0.5f + (1f - fv) * 0.08f);
                    float growth = S(0.42f, 0.62f, PropNoise.Fbm(u * 8f, v * 8f, 4, sd + 2, 8, 8));
                    PropNoise.Cell(u * 18f, v * 18f, sd + 3, 18, out float f1, out _);
                    float barnacle = 1f - S(0.12f, 0.24f, f1);
                    Color c = paint * (0.8f + 0.25f * n);
                    c = Color.Lerp(c, Color.Lerp(new Color(0.28f, 0.13f, 0.06f), new Color(0.55f, 0.3f, 0.12f), drip), rustMask);
                    c = Color.Lerp(c, new Color(0.24f, 0.29f, 0.17f) * (0.8f + 0.4f * n), growth * 0.75f);
                    c = Color.Lerp(c, new Color(0.62f, 0.58f, 0.5f), barnacle * growth * 0.8f);
                    c *= 1f - 0.25f * weld;
                    c.a = 1f;
                    px[y * Sz + x] = c;
                    hg[y * Sz + x] = n * 0.3f + weld * 0.25f + barnacle * growth * 0.35f + growth * 0.1f;
                }
            }
            Store(key, Sz, Sz, px);
            StoreNormal(key + "_n", hg, Sz, Sz, 4f);
        }

        // =================================================================== text textures
        static void DrawText(Color32[] px, int w, int h, string text, float cx, float topY, float glyphH, Color ink, Color shadow, float wobble, int seed, float maxWidth)
        {
            float cs = glyphH / 7f;
            float tw = PropFont.Width(text) * cs;
            if (tw > maxWidth) { cs *= maxWidth / tw; tw = maxWidth; }
            float x0 = cx - tw * 0.5f;
            float realH = cs * 7f;
            int ymin = Mathf.Max(0, Mathf.FloorToInt(topY - realH - cs * 2f)), ymax = Mathf.Min(h - 1, Mathf.CeilToInt(topY + cs));
            int xmin = Mathf.Max(0, Mathf.FloorToInt(x0 - cs)), xmax = Mathf.Min(w - 1, Mathf.CeilToInt(x0 + tw + cs * 2f));
            float sh = cs * 0.45f;
            for (int y = ymin; y <= ymax; y++)
            {
                for (int x = xmin; x <= xmax; x++)
                {
                    float wx = (PropNoise.Fbm(x * 0.03f, y * 0.03f, 2, seed) - 0.5f) * wobble * cs;
                    float wy = (PropNoise.Fbm(x * 0.03f, y * 0.03f, 2, seed + 5) - 0.5f) * wobble * cs;
                    float gx = (x + 0.5f + wx - x0) / cs, gy = (topY - (y + 0.5f) + wy) / cs;
                    float aa = 1.2f / cs;
                    float ink01 = PropFont.Coverage(text, gx, gy, 0.5f, aa);
                    float sx = (x + 0.5f + wx - x0 - sh) / cs, sy = (topY - (y + 0.5f) + wy - sh) / cs;
                    float sh01 = PropFont.Coverage(text, sx, sy, 0.55f, aa) * shadow.a;
                    if (ink01 <= 0f && sh01 <= 0f) continue;
                    Color c = px[y * w + x];
                    float a = c.a;
                    c = Color.Lerp(c, new Color(shadow.r, shadow.g, shadow.b), sh01);
                    float brush = 0.75f + 0.25f * PropNoise.Fbm(x * 0.15f, y * 0.02f, 3, seed + 9);
                    float worn = S(0.62f, 0.75f, PropNoise.Fbm(x * 0.06f, y * 0.06f, 3, seed + 13));
                    c = Color.Lerp(c, ink * brush, ink01 * (1f - worn * 0.75f));
                    c.a = a;
                    px[y * w + x] = c;
                }
            }
        }

        /// Hand-painted wooden sign of the hut.
        public static Texture2D HutSign => Get("sign_hut", GenHutSign);

        static void GenHutSign()
        {
            const int W = 512, H = 128;
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
            {
                float v = (y + 0.5f) / H;
                int row = Mathf.FloorToInt(v * 3f);
                float fv = v * 3f - row;
                for (int x = 0; x < W; x++)
                {
                    float u = (x + 0.5f) / W;
                    float grain = PropNoise.Fbm(u * 6f, v * 40f, 4, 101 + row);
                    Color c = Color.Lerp(new Color(0.40f, 0.29f, 0.19f), new Color(0.55f, 0.47f, 0.38f), PropNoise.Fbm(u * 5f, v * 5f, 3, 105)) * (0.8f + 0.35f * grain);
                    float e = Mathf.Min(fv, 1f - fv) * H / 3f;
                    if (e < 1.5f) c = new Color(0.1f, 0.07f, 0.05f);
                    float edge = Mathf.Min(Mathf.Min(u, 1f - u) * W, Mathf.Min(v, 1f - v) * H);
                    if (edge < 4f) c *= 0.6f;
                    c.a = 1f;
                    px[y * W + x] = c;
                }
            }
            DrawText(px, W, H, "SEALEBRITY", W * 0.5f, H * 0.9f, H * 0.46f, new Color(0.14f, 0.66f, 0.68f), new Color(0.05f, 0.04f, 0.03f, 0.8f), 0.35f, 7, W * 0.9f);
            DrawText(px, W, H, "DIVE CO.", W * 0.5f, H * 0.36f, H * 0.24f, new Color(0.96f, 0.88f, 0.68f), new Color(0.05f, 0.04f, 0.03f, 0.7f), 0.3f, 9, W * 0.6f);
            Store("sign_hut", W, H, px, false, false, TextureWrapMode.Clamp, TextureWrapMode.Clamp);
        }

        /// FinTube poster on the hut wall.
        public static Texture2D Poster => Get("poster", GenPoster);

        static void GenPoster()
        {
            const int Sz = 256;
            var px = new Color32[Sz * Sz];
            for (int y = 0; y < Sz; y++)
            {
                float v = (y + 0.5f) / Sz;
                for (int x = 0; x < Sz; x++)
                {
                    float u = (x + 0.5f) / Sz;
                    float age = PropNoise.Fbm(u * 6f, v * 6f, 4, 111);
                    Color c = new Color(0.92f, 0.9f, 0.84f) * (0.88f + 0.12f * age);
                    // rounded red play button
                    float bx = Mathf.Abs(u - 0.5f) - 0.26f, by = Mathf.Abs(v - 0.62f) - 0.15f;
                    float rd = Mathf.Sqrt(Mathf.Max(bx + 0.06f, 0f) * Mathf.Max(bx + 0.06f, 0f) + Mathf.Max(by + 0.06f, 0f) * Mathf.Max(by + 0.06f, 0f));
                    if (bx < 0f && by < 0f || rd < 0.06f) c = new Color(0.84f, 0.1f, 0.1f) * (0.9f + 0.1f * age);
                    // white triangle
                    float tx = u - 0.46f, ty = v - 0.62f;
                    if (tx > 0f && tx < 0.13f && Mathf.Abs(ty) < (0.13f - tx) * 0.62f) c = new Color(0.97f, 0.96f, 0.94f);
                    if (PropNoise.Fbm(u * 3f, v * 3f, 3, 112) > 0.72f) c *= 0.85f;
                    c.a = 1f;
                    px[y * Sz + x] = c;
                }
            }
            DrawText(px, Sz, Sz, "FINTUBE", Sz * 0.5f, Sz * 0.36f, Sz * 0.12f, new Color(0.12f, 0.12f, 0.12f), new Color(0, 0, 0, 0f), 0.1f, 15, Sz * 0.8f);
            Store("poster", Sz, Sz, px, false, false, TextureWrapMode.Clamp, TextureWrapMode.Clamp);
        }

        /// Ship name painted on the stern with rust drips.
        public static Texture2D NamePlate => Get("nameplate", GenNamePlate);

        static void GenNamePlate()
        {
            const int W = 512, H = 64;
            var px = new Color32[W * H];
            Color paint = new Color(0.14f, 0.2f, 0.26f);
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    float u = (x + 0.5f) / W, v = (y + 0.5f) / H;
                    float n = PropNoise.Fbm(u * 24f, v * 3f, 4, 121);
                    Color c = Color.Lerp(paint, new Color(0.4f, 0.2f, 0.09f), S(0.5f, 0.75f, n));
                    c = Color.Lerp(c, new Color(0.24f, 0.29f, 0.17f), S(0.55f, 0.7f, PropNoise.Fbm(u * 30f, v * 4f, 3, 122)) * 0.7f);
                    c.a = 1f;
                    px[y * W + x] = c;
                }
            }
            DrawText(px, W, H, "MS CLICKBAIT", W * 0.5f, H * 0.88f, H * 0.62f, new Color(0.86f, 0.85f, 0.8f), new Color(0, 0, 0, 0f), 0.15f, 21, W * 0.94f);
            // rust drips below letters
            var drip = new float[W];
            for (int y = H - 1; y >= 0; y--)
            {
                for (int x = 0; x < W; x++)
                {
                    Color c = px[y * W + x];
                    bool letter = c.r > 0.7f && c.g > 0.7f;
                    drip[x] = letter ? 0.8f : drip[x] * 0.94f;
                    if (!letter && drip[x] > 0.05f)
                    {
                        float k = drip[x] * PropNoise.Hash01(x / 3, 0, 123);
                        c = Color.Lerp(c, new Color(0.5f, 0.27f, 0.1f), k * 0.8f);
                        c.a = 1f;
                        px[y * W + x] = c;
                    }
                }
            }
            Store("nameplate", W, H, px, false, false, TextureWrapMode.Clamp, TextureWrapMode.Clamp);
        }

        // =================================================================== fabric / kelp
        public static Texture2D Fabric(string key, Color a, Color b, int stripes, bool vertical)
            => Get("fab_" + key, () => GenFabric("fab_" + key, a, b, stripes, vertical));

        static void GenFabric(string key, Color a, Color b, int stripes, bool vertical)
        {
            const int Sz = 128;
            var px = new Color32[Sz * Sz];
            int sd = key.GetHashCode() & 0xFFFF;
            for (int y = 0; y < Sz; y++)
            {
                float v = (y + 0.5f) / Sz;
                for (int x = 0; x < Sz; x++)
                {
                    float u = (x + 0.5f) / Sz;
                    float s = vertical ? u : v;
                    Color c = stripes > 0 && (Mathf.FloorToInt(s * stripes) % 2 == 1) ? b : a;
                    float weave = ((x + y) % 2 == 0) ? 1f : 0.93f;
                    c *= weave * (0.86f + 0.18f * PropNoise.Fbm(u * 8f, v * 8f, 3, sd, 8, 8));
                    c.a = 1f;
                    px[y * Sz + x] = c;
                }
            }
            Store(key, Sz, Sz, px);
        }

        public static Texture2D Kelp => Get("kelp", GenKelp);

        static void GenKelp()
        {
            const int W = 128, H = 256;
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
            {
                float v = (y + 0.5f) / H;
                for (int x = 0; x < W; x++)
                {
                    float u = (x + 0.5f) / W;
                    float n = PropNoise.Fbm(u * 4f, v * 8f, 4, 131, 4, 8);
                    float wr = PropNoise.Fbm(u * 24f, v * 6f, 3, 132, 24, 6);
                    Color c = Color.Lerp(new Color(0.36f, 0.32f, 0.1f), new Color(0.62f, 0.5f, 0.16f), n) * (0.82f + 0.25f * wr);
                    float edge = Mathf.Abs(u - 0.5f) * 2f;
                    c *= 1f - 0.25f * S(0.75f, 1f, edge);
                    c.a = 1f;
                    px[y * W + x] = c;
                }
            }
            Store("kelp", W, H, px);
        }

        // =================================================================== coral surfaces
        public static Texture2D Pores => Get("pores", () => GenPores("pores", 10, 0.22f, 141));
        public static Texture2D PoresN => Get("pores_n", () => GenPores("pores", 10, 0.22f, 141));
        public static Texture2D Sponge => Get("sponge", () => GenPores("sponge", 6, 0.3f, 151));
        public static Texture2D SpongeN => Get("sponge_n", () => GenPores("sponge", 6, 0.3f, 151));

        static void GenPores(string key, int freq, float size, int seed)
        {
            const int Sz = 128;
            var px = new Color32[Sz * Sz];
            var hg = new float[Sz * Sz];
            for (int y = 0; y < Sz; y++)
            {
                float v = (y + 0.5f) / Sz;
                for (int x = 0; x < Sz; x++)
                {
                    float u = (x + 0.5f) / Sz;
                    PropNoise.Cell(u * freq, v * freq, seed, freq, out float f1, out float f2);
                    float pore = S(size * 0.5f, size, f1);
                    float n = PropNoise.Fbm(u * 6f, v * 6f, 3, seed + 1, 6, 6);
                    Color c = new Color(0.93f, 0.91f, 0.87f) * (0.55f + 0.45f * pore) * (0.85f + 0.2f * n);
                    c.a = 1f;
                    px[y * Sz + x] = c;
                    hg[y * Sz + x] = pore * 0.8f + n * 0.2f;
                }
            }
            Store(key, Sz, Sz, px);
            StoreNormal(key + "_n", hg, Sz, Sz, 3f);
        }

        public static Texture2D Brain => Get("brain", GenBrain);
        public static Texture2D BrainN => Get("brain_n", GenBrain);

        static void GenBrain()
        {
            const int Sz = 256;
            var px = new Color32[Sz * Sz];
            var hg = new float[Sz * Sz];
            for (int y = 0; y < Sz; y++)
            {
                float v = (y + 0.5f) / Sz;
                for (int x = 0; x < Sz; x++)
                {
                    float u = (x + 0.5f) / Sz;
                    float n = PropNoise.Fbm(u * 3f, v * 3f, 4, 161, 3, 3);
                    float m = Frac(n * 9f);
                    float ridge = 1f - Mathf.Abs(2f * m - 1f);
                    float sept = 0.5f + 0.5f * Mathf.Sin((u + v) * 220f);
                    Color c = new Color(0.92f, 0.9f, 0.82f) * (0.5f + 0.5f * S(0.08f, 0.6f, ridge));
                    c *= 0.92f + 0.08f * sept;
                    c.a = 1f;
                    px[y * Sz + x] = c;
                    hg[y * Sz + x] = ridge;
                }
            }
            Store("brain", Sz, Sz, px);
            StoreNormal("brain_n", hg, Sz, Sz, 3f);
        }

        /// Sea fan lattice (alpha cut-out network from Worley cell borders).
        public static Texture2D Lattice => Get("lattice", GenLattice);

        static void GenLattice()
        {
            const int Sz = 256;
            var px = new Color32[Sz * Sz];
            for (int y = 0; y < Sz; y++)
            {
                float v = (y + 0.5f) / Sz;
                for (int x = 0; x < Sz; x++)
                {
                    float u = (x + 0.5f) / Sz;
                    PropNoise.Cell(u * 12f, v * 12f, 171, 12, out float f1, out float f2);
                    float e = f2 - f1;
                    float a = 1f - S(0.07f, 0.12f, e);
                    float n = PropNoise.Fbm(u * 8f, v * 8f, 2, 172, 8, 8);
                    Color c = new Color(0.95f, 0.93f, 0.9f) * (0.85f + 0.15f * n);
                    c.a = a;
                    px[y * Sz + x] = c;
                }
            }
            Store("lattice", Sz, Sz, px, false, true);
        }

        // =================================================================== particles / beams
        public static Texture2D SoftDot => Get("softdot", GenSoftDot);

        static void GenSoftDot()
        {
            const int Sz = 64;
            var px = new Color32[Sz * Sz];
            for (int y = 0; y < Sz; y++)
            {
                for (int x = 0; x < Sz; x++)
                {
                    float dx = (x + 0.5f) / Sz * 2f - 1f, dy = (y + 0.5f) / Sz * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Pow(Mathf.Clamp01(1f - r), 2f);
                    px[y * Sz + x] = new Color(1f, 1f, 1f, a);
                }
            }
            Store("softdot", Sz, Sz, px, false, false, TextureWrapMode.Clamp, TextureWrapMode.Clamp);
        }

        public static Texture2D Bubble => Get("bubble", GenBubble);

        static void GenBubble()
        {
            const int Sz = 64;
            var px = new Color32[Sz * Sz];
            for (int y = 0; y < Sz; y++)
            {
                for (int x = 0; x < Sz; x++)
                {
                    float dx = (x + 0.5f) / Sz * 2f - 1f, dy = (y + 0.5f) / Sz * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float ring = S(0.62f, 0.86f, r) * (1f - S(0.9f, 1f, r));
                    float hx = dx + 0.35f, hy = dy - 0.35f;
                    float hl = Mathf.Clamp01(1f - Mathf.Sqrt(hx * hx + hy * hy) / 0.22f);
                    float a = Mathf.Clamp01(ring * 0.85f + hl + (r < 0.9f ? 0.08f : 0f));
                    px[y * Sz + x] = new Color(1f, 1f, 1f, a);
                }
            }
            Store("bubble", Sz, Sz, px, false, false, TextureWrapMode.Clamp, TextureWrapMode.Clamp);
        }

        /// Light beam gradient: v = 0 at the lamp, 1 at the far end; soft across u.
        public static Texture2D Beam => Get("beam", GenBeam);

        static void GenBeam()
        {
            const int W = 32, H = 128;
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
            {
                float v = (y + 0.5f) / H;
                for (int x = 0; x < W; x++)
                {
                    float a = Mathf.Pow(1f - v, 1.6f) * (0.75f + 0.25f * Mathf.Sin((x + 0.5f) / W * TAU * 2f));
                    px[y * W + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
                }
            }
            Store("beam", W, H, px, false, false, TextureWrapMode.Repeat, TextureWrapMode.Clamp);
        }
    }
}
