using UnityEngine;

namespace Sealebrities
{
    /// Tileable value noise for the Boat module's procedural textures.
    public static class BoatNoise
    {
        public static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)seed * 2246822519u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFFu) / 16777215f;
            }
        }

        static int Wrap(int a, int p)
        {
            int r = a % p;
            return r < 0 ? r + p : r;
        }

        /// Value noise on a lattice that repeats every (px, py) cells.
        public static float Value(float x, float y, int px, int py, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            int xa = Wrap(x0, px), xb = Wrap(x0 + 1, px), ya = Wrap(y0, py), yb = Wrap(y0 + 1, py);
            float a = Hash(xa, ya, seed), b = Hash(xb, ya, seed), c = Hash(xa, yb, seed), d = Hash(xb, yb, seed);
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        /// Tileable fbm on the unit square (u, v in 0..1) with base lattice frequencies fx, fy.
        public static float Fbm(float u, float v, int fx, int fy, int octaves, int seed)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += amp * Value(u * fx, v * fy, fx, fy, seed + i * 101);
                norm += amp;
                amp *= 0.5f;
                fx *= 2;
                fy *= 2;
            }
            return sum / norm;
        }

        public static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }
    }

    /// Procedural textures of the Boat module. All are generated lazily once and shared.
    public static class BoatTextures
    {
        static Texture2D hullA, hullN, deckA, deckN, cabinA, cabinN, metalA, metalN, crateA, chute, soft, foam, drumBlue, drumOrange;
        static Texture2D[] fish = new Texture2D[3];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            hullA = hullN = deckA = deckN = cabinA = cabinN = metalA = metalN = crateA = chute = soft = foam = drumBlue = drumOrange = null;
            fish = new Texture2D[3];
        }

        public static Texture2D HullAlbedo { get { if (hullA == null) BuildHull(); return hullA; } }
        public static Texture2D HullNormal { get { if (hullN == null) BuildHull(); return hullN; } }
        public static Texture2D DeckAlbedo { get { if (deckA == null) BuildDeck(); return deckA; } }
        public static Texture2D DeckNormal { get { if (deckN == null) BuildDeck(); return deckN; } }
        public static Texture2D CabinAlbedo { get { if (cabinA == null) BuildCabin(); return cabinA; } }
        public static Texture2D CabinNormal { get { if (cabinN == null) BuildCabin(); return cabinN; } }
        public static Texture2D MetalAlbedo { get { if (metalA == null) BuildMetal(); return metalA; } }
        public static Texture2D MetalNormal { get { if (metalN == null) BuildMetal(); return metalN; } }
        public static Texture2D CrateAlbedo { get { if (crateA == null) crateA = BuildCrate(); return crateA; } }
        public static Texture2D Parachute { get { if (chute == null) chute = BuildChute(); return chute; } }
        public static Texture2D Soft { get { if (soft == null) soft = BuildSoft(); return soft; } }
        public static Texture2D Foam { get { if (foam == null) foam = BuildFoam(); return foam; } }
        public static Texture2D DrumBlue { get { if (drumBlue == null) drumBlue = BuildDrum(new Color(0.12f, 0.27f, 0.55f), 3); return drumBlue; } }
        public static Texture2D DrumOrange { get { if (drumOrange == null) drumOrange = BuildDrum(new Color(0.78f, 0.36f, 0.08f), 7); return drumOrange; } }

        /// 0 mackerel, 1 sardine, 2 sea bream
        public static Texture2D Fish(int kind)
        {
            kind = Mathf.Clamp(kind, 0, 2);
            if (fish[kind] == null) fish[kind] = BuildFish(kind);
            return fish[kind];
        }

        // ------------------------------------------------------------------ helpers
        static Texture2D NewTex(int w, int h, bool linear, string name, TextureWrapMode wrap = TextureWrapMode.Repeat)
        {
            return new Texture2D(w, h, TextureFormat.RGBA32, true, linear)
            {
                name = name, wrapMode = wrap, filterMode = FilterMode.Trilinear, anisoLevel = 4
            };
        }

        static Texture2D Finish(Texture2D t, Color32[] px)
        {
            t.SetPixels32(px);
            t.Apply(true, true);
            return t;
        }

        static Color32 C32(Color c, float a = 1f)
        {
            return new Color32((byte)(Mathf.Clamp01(c.r) * 255f), (byte)(Mathf.Clamp01(c.g) * 255f), (byte)(Mathf.Clamp01(c.b) * 255f), (byte)(Mathf.Clamp01(a) * 255f));
        }

        /// Tangent-space normal map (RGB = xyz, A = 1, works with both DXT5nm and RGB unpacking) from a wrapping height field.
        static Texture2D NormalFromHeight(float[] h, int w, int hgt, float strength, string name, bool wrapX = true, bool wrapY = true)
        {
            var px = new Color32[w * hgt];
            for (int y = 0; y < hgt; y++)
            {
                int yd = wrapY ? (y - 1 + hgt) % hgt : Mathf.Max(0, y - 1);
                int yu = wrapY ? (y + 1) % hgt : Mathf.Min(hgt - 1, y + 1);
                for (int x = 0; x < w; x++)
                {
                    int xl = wrapX ? (x - 1 + w) % w : Mathf.Max(0, x - 1);
                    int xr = wrapX ? (x + 1) % w : Mathf.Min(w - 1, x + 1);
                    float dx = (h[y * w + xl] - h[y * w + xr]) * strength;
                    float dy = (h[yd * w + x] - h[yu * w + x]) * strength;
                    Vector3 n = new Vector3(dx, dy, 1f).normalized;
                    px[y * w + x] = new Color32((byte)((n.x * 0.5f + 0.5f) * 255f), (byte)((n.y * 0.5f + 0.5f) * 255f), (byte)((n.z * 0.5f + 0.5f) * 255f), 255);
                }
            }
            return Finish(NewTex(w, hgt, true, name, wrapX ? TextureWrapMode.Repeat : TextureWrapMode.Clamp), px);
        }

        static float S(float a, float b, float x) => BoatNoise.Smooth(a, b, x);

        // ------------------------------------------------------------------ hull paint
        static readonly string[] GlyphKeys = { "R", "O", "S", "T", "L", "A", "U", "B", "E" };
        static readonly string[][] GlyphRows =
        {
            new[] { "11110", "10001", "10001", "11110", "10100", "10010", "10001" },
            new[] { "01110", "10001", "10001", "10001", "10001", "10001", "01110" },
            new[] { "01111", "10000", "10000", "01110", "00001", "00001", "11110" },
            new[] { "11111", "00100", "00100", "00100", "00100", "00100", "00100" },
            new[] { "10000", "10000", "10000", "10000", "10000", "10000", "11111" },
            new[] { "01110", "10001", "10001", "11111", "10001", "10001", "10001" },
            new[] { "10001", "10001", "10001", "10001", "10001", "10001", "01110" },
            new[] { "11110", "10001", "10001", "11110", "10001", "10001", "11110" },
            new[] { "11111", "10000", "10000", "11110", "10000", "10000", "11111" },
        };

        static string[] Glyph(char ch)
        {
            for (int i = 0; i < GlyphKeys.Length; i++) if (GlyphKeys[i][0] == ch) return GlyphRows[i];
            return null;
        }

        static void DrawText(float[] mask, int w, int x0, int yTop, int cell, string text)
        {
            int x = x0;
            foreach (char ch in text)
            {
                var g = Glyph(ch);
                if (g != null)
                    for (int gy = 0; gy < 7; gy++)
                        for (int gx = 0; gx < 5; gx++)
                        {
                            if (g[gy][gx] != '1') continue;
                            for (int py = 0; py < cell; py++)
                                for (int pxl = 0; pxl < cell; pxl++)
                                {
                                    int xx = x + gx * cell + pxl, yy = yTop - gy * cell - py;
                                    if (xx >= 0 && xx < w && yy >= 0 && yy * w + xx < mask.Length) mask[yy * w + xx] = 1f;
                                }
                        }
                x += 6 * cell;
            }
        }

        static void BuildHull()
        {
            const int W = 1024, H = 512, HH = 256;
            var px = new Color32[W * H];
            var hgt = new float[W * H];
            var streak = new float[W * H];
            var nameMask = new float[W * H];
            var rng = new System.Random(4242);

            int RowOf(int half, float y)
            {
                float vn = (y - BoatHull.TexY0) / BoatHull.TexYRange;
                return half * HH + Mathf.Clamp((int)((0.02f + vn * 0.96f) * HH), 0, HH - 1);
            }

            // rust streaks running down from bolts and scuppers
            for (int half = 0; half < 2; half++)
            {
                int side = half == 1 ? 1 : -1;
                for (int k = 0; k < 90; k++)
                {
                    float t = (float)rng.NextDouble();
                    int type = rng.Next(5);
                    float startY = type == 0 ? 0.18f + (float)rng.NextDouble() * 0.3f : BoatHull.SheerY(t) - 0.13f - (float)rng.NextDouble() * 0.04f;
                    if (t > 0.88f && rng.NextDouble() < 0.5) startY = BoatHull.SheerY(t) - 0.05f; // hawse/anchor area
                    float lenM = 0.12f + (float)rng.NextDouble() * (type == 0 ? 0.3f : 0.75f);
                    float sigma = 1.0f + (float)rng.NextDouble() * 2.8f;
                    float strength = 0.3f + (float)rng.NextDouble() * 0.7f;
                    float u = side > 0 ? t : 1f - t;
                    int cx = (int)(u * W);
                    int cy0 = RowOf(half, startY);
                    int len = Mathf.Max(4, (int)(lenM / BoatHull.TexYRange * 0.96f * HH));
                    float phase = (float)rng.NextDouble() * 6f;
                    for (int dy = 0; dy < len; dy++)
                    {
                        int row = cy0 - dy;
                        if (row < half * HH) break;
                        float f = 1f - dy / (float)len;
                        f = f * f * (0.75f + 0.25f * Mathf.Sin(dy * 0.31f + phase));
                        float wob = Mathf.Sin(dy * 0.09f + phase) * 1.4f;
                        float sg = sigma * (1f + dy / (float)len * 0.7f);
                        int rr = (int)(sg * 3f) + 1;
                        for (int dx = -rr; dx <= rr; dx++)
                        {
                            int c = cx + dx;
                            if (c < 0 || c >= W) continue;
                            float x = dx - wob;
                            streak[row * W + c] += strength * f * Mathf.Exp(-x * x / (2f * sg * sg));
                        }
                    }
                }
            }

            // boat name near the bow on both sides (port half is mirrored in u, text still reads left to right)
            {
                const int cell = 4;
                float tStart = 0.652f, yTopM = 0.62f;
                DrawText(nameMask, W, (int)(tStart * W), RowOf(1, yTopM), cell, "ROSTLAUBE");
                DrawText(nameMask, W, (int)((1f - 0.86f) * W), RowOf(0, yTopM), cell, "ROSTLAUBE");
            }

            var sheerS = new float[W];
            var sheerP = new float[W];
            for (int c = 0; c < W; c++)
            {
                float u = (c + 0.5f) / W;
                sheerS[c] = BoatHull.SheerY(u);
                sheerP[c] = BoatHull.SheerY(1f - u);
            }

            var rust1 = new Color(0.42f, 0.17f, 0.06f);
            var rust2 = new Color(0.63f, 0.33f, 0.12f);
            var streakCol = new Color(0.47f, 0.22f, 0.08f);
            for (int r = 0; r < H; r++)
            {
                int half = r / HH;
                int side = half == 1 ? 1 : -1;
                float local = ((r - half * HH) + 0.5f) / HH;
                float y = BoatHull.TexY0 + (local - 0.02f) / 0.96f * BoatHull.TexYRange;
                for (int c = 0; c < W; c++)
                {
                    int idx = r * W + c;
                    float u = (c + 0.5f) / W;
                    float t = side > 0 ? u : 1f - u;
                    float sy = side > 0 ? sheerS[c] : sheerP[c];
                    float nLow = BoatNoise.Fbm(u, local, 8, 2, 4, 11 + half);
                    float nChip = BoatNoise.Fbm(u, local, 64, 16, 4, 37 + half);
                    float nFine = BoatNoise.Value(u * 512f, local * 128f, 512, 128, 71 + half);

                    Color col;
                    float h = 0.3f;
                    if (y < 0.04f)
                    {
                        col = new Color(0.33f, 0.11f, 0.08f) * (0.8f + 0.35f * nLow);
                        float alg = S(-0.28f, 0.02f, y) * S(0.35f, 0.65f, nLow + 0.15f);
                        col = Color.Lerp(col, new Color(0.17f, 0.21f, 0.09f), alg * 0.85f);
                        float barn = S(0.7f, 0.74f, nChip) * S(0.0f, -0.3f, y);
                        col = Color.Lerp(col, new Color(0.62f, 0.6f, 0.52f), barn * 0.8f);
                        h = 0.2f + barn * 0.6f;
                    }
                    else if (y < 0.11f)
                    {
                        col = new Color(0.86f, 0.85f, 0.79f) * (0.85f + 0.2f * nLow);
                        col = Color.Lerp(col, new Color(0.35f, 0.33f, 0.22f), S(0.5f, 0.8f, nLow) * 0.4f);
                    }
                    else
                    {
                        col = Color.Lerp(new Color(0.07f, 0.35f, 0.36f), new Color(0.22f, 0.52f, 0.5f), S(0.35f, 0.75f, nLow));
                        if (y > sy - 0.12f && y < sy - 0.045f) col = new Color(0.84f, 0.83f, 0.77f) * (0.88f + 0.15f * nLow);
                    }

                    // steel plate seams with weld beads and rivets
                    float zm = t * BoatHull.Length;
                    float sd = Mathf.Abs(zm / 1.25f - Mathf.Round(zm / 1.25f)) * 1.25f;
                    float seam = 1f - S(0.004f, 0.012f, sd);
                    if (y > 0.11f) seam = Mathf.Max(seam, 1f - S(0.004f, 0.012f, Mathf.Abs(y - 0.48f)));
                    col *= 1f - seam * 0.22f;
                    h += seam * 0.25f;
                    float rivA = Mathf.Abs(y * 16f - Mathf.Round(y * 16f));
                    if (sd < 0.03f && rivA < 0.1f) { h += 0.35f; col *= 0.92f; }

                    // chipped paint: primer, then rust
                    float edge = S(sy - 0.25f, sy, y) * 0.12f + S(0.8f, 1f, t) * 0.07f + (1f - S(0f, 0.25f, Mathf.Abs(y - 0.06f))) * 0.06f;
                    // thresholds tuned to the noise distribution (p85 ~0.65, p95 ~0.72): ~15 % primer, ~7 % rust + edge wear
                    float chip = S(0.64f, 0.66f, nChip + edge);
                    float core = S(0.68f, 0.715f, nChip + edge);
                    if (y >= 0.04f) col = Color.Lerp(col, new Color(0.55f, 0.55f, 0.5f), chip);
                    col = Color.Lerp(col, Color.Lerp(rust1, rust2, nFine), core);
                    h -= chip * 0.22f + core * 0.1f;
                    h += (nFine - 0.5f) * 0.25f * core;

                    float st = Mathf.Clamp01(streak[idx]) * (y > 0.04f ? 0.85f : 0.4f);
                    col = Color.Lerp(col, streakCol * (0.8f + 0.4f * nFine), st * 0.72f);

                    float nm = nameMask[idx];
                    if (nm > 0f)
                    {
                        float wear = S(0.25f, 0.45f, nFine * 0.5f + nLow * 0.5f + 0.15f) * (1f - core);
                        col = Color.Lerp(col, new Color(0.9f, 0.88f, 0.8f), nm * wear * 0.95f);
                        h += nm * 0.08f;
                    }

                    col *= 0.88f + 0.18f * nLow;
                    px[idx] = C32(col);
                    hgt[idx] = h;
                }
            }
            hullA = Finish(NewTex(W, H, false, "BoatHullPaint", TextureWrapMode.Clamp), px);
            hullN = NormalFromHeight(hgt, W, H, 2.2f, "BoatHullNormal", false, false);
        }

        // ------------------------------------------------------------------ deck planks (tiles every 0.96 m, planks along v)
        static void BuildDeck()
        {
            const int N = 512, planks = 8, pw = N / planks;
            var px = new Color32[N * N];
            var hgt = new float[N * N];
            for (int y = 0; y < N; y++)
            {
                float v = (y + 0.5f) / N;
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N;
                    int plank = x / pw;
                    float pxf = (x % pw + 0.5f) / pw;
                    float joint = BoatNoise.Hash(plank, 1, 5);
                    float dv = Mathf.Abs(v - joint);
                    dv = Mathf.Min(dv, 1f - dv);
                    int seg = (v > joint) ? 1 : 0;
                    float tone = BoatNoise.Hash(plank, 2 + seg, 5);
                    float grain = BoatNoise.Fbm(u, v + plank * 0.37f, 128, 4, 3, 9 + plank * 7 + seg);
                    float fiber = BoatNoise.Value(u * 512f, v * 24f, 512, 24, 33);
                    float weather = BoatNoise.Fbm(u, v, 4, 4, 4, 19);

                    Color wood = Color.Lerp(new Color(0.40f, 0.30f, 0.20f), new Color(0.56f, 0.44f, 0.30f), tone);
                    wood *= 0.78f + 0.36f * grain + 0.1f * (fiber - 0.5f);
                    Color grey = new Color(0.5f, 0.48f, 0.44f) * (0.82f + 0.3f * grain);
                    Color c = Color.Lerp(wood, grey, Mathf.Clamp01(0.35f + 0.45f * weather));
                    c = Color.Lerp(c, new Color(0.2f, 0.17f, 0.13f), S(0.62f, 0.8f, weather) * 0.35f);

                    float h = 0.5f + (grain - 0.5f) * 0.3f + (fiber - 0.5f) * 0.08f;
                    float gap = 1f - S(0.03f, 0.07f, Mathf.Min(pxf, 1f - pxf));
                    float jl = 1f - S(0.0015f, 0.004f, dv);
                    float dark = Mathf.Max(gap, jl * 0.8f);
                    c = Color.Lerp(c, new Color(0.07f, 0.06f, 0.05f), dark);
                    h -= dark * 0.5f;

                    // nails near the butt joints
                    float nd1 = new Vector2((pxf - 0.25f) * pw, (dv - 0.012f) * N).magnitude;
                    float nd2 = new Vector2((pxf - 0.75f) * pw, (dv - 0.012f) * N).magnitude;
                    float nail = 1f - S(1.2f, 2.4f, Mathf.Min(nd1, nd2));
                    float halo = (1f - S(2f, 6f, Mathf.Min(nd1, nd2))) * 0.6f;
                    c = Color.Lerp(c, new Color(0.38f, 0.2f, 0.09f), halo * 0.5f);
                    c = Color.Lerp(c, new Color(0.15f, 0.13f, 0.12f), nail);
                    h += nail * 0.1f;

                    int i = y * N + x;
                    px[i] = C32(c);
                    hgt[i] = h;
                }
            }
            deckA = Finish(NewTex(N, N, false, "BoatDeckWood"), px);
            deckN = NormalFromHeight(hgt, N, N, 3f, "BoatDeckNormal");
        }

        // ------------------------------------------------------------------ weathered off-white cabin paint (tiles per meter)
        static void BuildCabin()
        {
            const int N = 256;
            var px = new Color32[N * N];
            var hgt = new float[N * N];
            for (int y = 0; y < N; y++)
            {
                float v = (y + 0.5f) / N;
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N;
                    float streaks = BoatNoise.Fbm(u, v, 48, 2, 3, 3);
                    float blot = BoatNoise.Fbm(u, v, 6, 6, 4, 5);
                    float chipN = BoatNoise.Fbm(u, v, 24, 24, 4, 8);
                    Color c = new Color(0.84f, 0.83f, 0.78f) * (0.9f + 0.12f * blot);
                    c = Color.Lerp(c, new Color(0.45f, 0.42f, 0.34f), S(0.55f, 0.75f, streaks) * 0.35f);
                    float chip = S(0.64f, 0.66f, chipN);
                    float core = S(0.69f, 0.72f, chipN);
                    c = Color.Lerp(c, new Color(0.48f, 0.55f, 0.58f), chip);
                    c = Color.Lerp(c, new Color(0.45f, 0.2f, 0.07f), core);
                    int i = y * N + x;
                    px[i] = C32(c);
                    hgt[i] = 0.5f - chip * 0.25f - core * 0.15f + (streaks - 0.5f) * 0.05f;
                }
            }
            cabinA = Finish(NewTex(N, N, false, "BoatCabinPaint"), px);
            cabinN = NormalFromHeight(hgt, N, N, 2.5f, "BoatCabinNormal");
        }

        // ------------------------------------------------------------------ rusty steel
        static void BuildMetal()
        {
            const int N = 256;
            var px = new Color32[N * N];
            var hgt = new float[N * N];
            for (int y = 0; y < N; y++)
            {
                float v = (y + 0.5f) / N;
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N;
                    float big = BoatNoise.Fbm(u, v, 6, 6, 5, 21);
                    float fine = BoatNoise.Fbm(u, v, 64, 64, 2, 23);
                    Color c = new Color(0.33f, 0.34f, 0.35f) * (0.85f + 0.3f * fine);
                    float rust = S(0.5f, 0.62f, big + (fine - 0.5f) * 0.2f);
                    Color rc = Color.Lerp(new Color(0.36f, 0.15f, 0.06f), new Color(0.62f, 0.32f, 0.13f), fine);
                    c = Color.Lerp(c, rc, rust);
                    float pit = S(0.68f, 0.75f, fine) * rust;
                    c *= 1f - pit * 0.4f;
                    int i = y * N + x;
                    px[i] = C32(c);
                    hgt[i] = 0.5f + rust * (fine - 0.5f) * 0.6f - pit * 0.3f;
                }
            }
            metalA = Finish(NewTex(N, N, false, "BoatMetal"), px);
            metalN = NormalFromHeight(hgt, N, N, 2f, "BoatMetalNormal");
        }

        static Texture2D BuildDrum(Color paint, int seed)
        {
            const int N = 256;
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
            {
                float v = (y + 0.5f) / N;
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N;
                    float big = BoatNoise.Fbm(u, v, 8, 4, 4, seed);
                    float fine = BoatNoise.Fbm(u, v, 64, 32, 2, seed + 9);
                    Color c = paint * (0.85f + 0.25f * big);
                    float rim = Mathf.Max(1f - S(0.02f, 0.12f, v), S(0.88f, 0.98f, v));
                    float rust = Mathf.Max(S(0.62f, 0.7f, big + rim * 0.35f), rim * 0.6f * S(0.35f, 0.6f, fine));
                    c = Color.Lerp(c, Color.Lerp(new Color(0.36f, 0.15f, 0.06f), new Color(0.6f, 0.3f, 0.11f), fine), rust);
                    float scratch = S(0.82f, 0.86f, BoatNoise.Value(u * 256f, v * 8f, 256, 8, seed + 3));
                    c = Color.Lerp(c, new Color(0.55f, 0.55f, 0.52f), scratch * 0.5f);
                    px[y * N + x] = C32(c);
                }
            }
            return Finish(NewTex(N, N, false, "BoatDrum"), px);
        }

        // ------------------------------------------------------------------ delivery crate (each face 0..1)
        static Texture2D BuildCrate()
        {
            const int N = 256, planks = 4;
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
            {
                float v = (y + 0.5f) / N;
                int plank = (int)(v * planks);
                float pv = v * planks - plank;
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N;
                    float grain = BoatNoise.Fbm(u + plank * 0.21f, v, 3, 96, 3, 41 + plank);
                    Color c = Color.Lerp(new Color(0.62f, 0.47f, 0.28f), new Color(0.78f, 0.62f, 0.4f), BoatNoise.Hash(plank, 0, 41)) * (0.8f + 0.35f * grain);
                    float gap = 1f - S(0.02f, 0.06f, Mathf.Min(pv, 1f - pv));
                    float frame = 1f - S(0.06f, 0.08f, Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v)));
                    // orange shop band with a black arrow
                    float band = (v > 0.4f && v < 0.6f) ? 1f : 0f;
                    float wear = S(0.3f, 0.5f, BoatNoise.Fbm(u, v, 16, 16, 3, 44));
                    c = Color.Lerp(c, new Color(0.95f, 0.45f, 0.08f), band * wear * 0.9f);
                    float ax = Mathf.Abs(u - 0.5f);
                    bool arrowHead = v > 0.62f && v < 0.82f && ax < (0.82f - v) * 0.7f;
                    bool arrowStem = v > 0.2f && v <= 0.62f && ax < 0.045f;
                    if (arrowHead || arrowStem) c = Color.Lerp(c, new Color(0.08f, 0.07f, 0.06f), wear * 0.9f);
                    c = Color.Lerp(c, c * 0.55f, frame);
                    c = Color.Lerp(c, new Color(0.12f, 0.09f, 0.06f), gap);
                    px[y * N + x] = C32(c);
                }
            }
            return Finish(NewTex(N, N, false, "BoatCrate", TextureWrapMode.Clamp), px);
        }

        // ------------------------------------------------------------------ parachute gores (u around, v rim..apex)
        static Texture2D BuildChute()
        {
            const int W = 256, H = 64, gores = 12;
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
            {
                float v = (y + 0.5f) / H;
                for (int x = 0; x < W; x++)
                {
                    float u = (x + 0.5f) / W;
                    int g = (int)(u * gores);
                    float gu = u * gores - g;
                    Color c = (g % 2 == 0) ? new Color(0.86f, 0.12f, 0.08f) : new Color(0.93f, 0.92f, 0.88f);
                    float seam = 1f - S(0.02f, 0.05f, Mathf.Min(gu, 1f - gu));
                    float weave = BoatNoise.Value(u * 256f, v * 64f, 256, 64, 51);
                    c *= 0.9f + 0.1f * weave;
                    c = Color.Lerp(c, c * 0.6f, seam);
                    if (v > 0.95f) c = new Color(0.2f, 0.2f, 0.2f);
                    c *= Mathf.Lerp(0.85f, 1f, v);
                    px[y * W + x] = C32(c);
                }
            }
            return Finish(NewTex(W, H, false, "BoatParachute"), px);
        }

        // ------------------------------------------------------------------ particle sprites
        static Texture2D BuildSoft()
        {
            const int N = 64;
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                    float r2 = dx * dx + dy * dy;
                    float a = Mathf.Clamp01(1f - r2);
                    a = a * a;
                    float n = BoatNoise.Fbm((x + 0.5f) / N, (y + 0.5f) / N, 4, 4, 3, 61);
                    px[y * N + x] = C32(Color.white, a * (0.65f + 0.35f * n));
                }
            var t = NewTex(N, N, false, "BoatSoftParticle", TextureWrapMode.Clamp);
            return Finish(t, px);
        }

        static Texture2D BuildFoam()
        {
            const int N = 128;
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N, v = (y + 0.5f) / N;
                    float dx = u * 2f - 1f, dy = v * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float n = BoatNoise.Fbm(u, v, 8, 8, 4, 63);
                    float a = (1f - S(0.55f, 1f, r)) * S(0.42f, 0.62f, n + (1f - r) * 0.15f);
                    px[y * N + x] = C32(new Color(0.95f, 0.97f, 0.98f), a);
                }
            return Finish(NewTex(N, N, false, "BoatFoam", TextureWrapMode.Clamp), px);
        }

        // ------------------------------------------------------------------ fish skins (u tail..head, v belly..back)
        static Texture2D BuildFish(int kind)
        {
            const int W = 64, H = 32;
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
            {
                float v = (y + 0.5f) / H;
                for (int x = 0; x < W; x++)
                {
                    float u = (x + 0.5f) / W;
                    Color belly = new Color(0.86f, 0.88f, 0.9f), back;
                    Color c;
                    if (kind == 0)
                    {
                        back = new Color(0.08f, 0.3f, 0.36f);
                        c = Color.Lerp(belly, back, S(0.45f, 0.62f, v));
                        float bars = Mathf.Sin(u * 42f + Mathf.Sin(v * 11f) * 1.6f);
                        if (v > 0.58f && bars > 0.45f) c = Color.Lerp(c, new Color(0.03f, 0.05f, 0.06f), 0.85f);
                    }
                    else if (kind == 1)
                    {
                        back = new Color(0.18f, 0.32f, 0.52f);
                        c = Color.Lerp(belly, back, S(0.5f, 0.7f, v));
                        for (int s = 0; s < 3; s++)
                        {
                            float d = new Vector2((u - (0.72f - s * 0.1f)) * 2f, v - 0.58f).magnitude;
                            if (d < 0.05f) c = new Color(0.08f, 0.1f, 0.14f);
                        }
                    }
                    else
                    {
                        back = new Color(0.42f, 0.44f, 0.48f);
                        c = Color.Lerp(new Color(0.8f, 0.7f, 0.64f), back, S(0.55f, 0.85f, v));
                        if (Mathf.Sin(u * 30f) > 0.7f && v > 0.35f) c *= 0.88f;
                        if (new Vector2((u - 0.12f) * 2f, v - 0.55f).magnitude < 0.08f) c = new Color(0.12f, 0.12f, 0.12f);
                    }
                    float eye = new Vector2((u - 0.9f) * 2f, v - 0.6f).magnitude;
                    if (eye < 0.075f) c = new Color(0.85f, 0.75f, 0.3f);
                    if (eye < 0.045f) c = new Color(0.02f, 0.02f, 0.02f);
                    px[y * W + x] = C32(c);
                }
            }
            return Finish(NewTex(W, H, false, "BoatFish", TextureWrapMode.Clamp), px);
        }
    }
}
