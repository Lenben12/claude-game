using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Materials and procedural textures for the hand camera models. Everything is cached and shared
    /// between rig rebuilds, so swapping materials at runtime never allocates.
    public static class FilmMaterials
    {
        public const int ScreenStandby = 0, ScreenLive = 1, ScreenWeak = 2, ScreenGood = 3, ScreenCaptured = 4;

        static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();
        static Texture2D grain, screenTex;
        static Material[] screens, tapes;
        static Material recDot, lampOff, lampOn;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Cache.Clear();
            grain = null; screenTex = null; screens = null; tapes = null; recDot = null; lampOff = null; lampOn = null;
        }

        // ------------------------------------------------------------------ textures
        /// Fine grain + micro scratches, values ~0.8..1 (multiplied with the tint) for a used, semi-realistic surface.
        public static Texture2D Grain
        {
            get
            {
                if (grain != null) return grain;
                const int N = 128;
                var px = new Color32[N * N];
                var rng = new System.Random(4711);
                float ox = (float)rng.NextDouble() * 100f, oy = (float)rng.NextDouble() * 100f;
                for (int y = 0; y < N; y++)
                {
                    for (int x = 0; x < N; x++)
                    {
                        // tileable-ish: sample noise on a torus by mirroring coordinates
                        float fx = x / (float)N, fy = y / (float)N;
                        float n1 = Mathf.PerlinNoise(ox + Mathf.Sin(fx * Mathf.PI * 2f) * 1.6f, oy + Mathf.Sin(fy * Mathf.PI * 2f) * 1.6f);
                        float n2 = Mathf.PerlinNoise(ox * 2f + fx * 24f, oy * 2f + fy * 24f);
                        float speck = (float)rng.NextDouble();
                        float v = 0.86f + n1 * 0.08f + (n2 - 0.5f) * 0.05f + (speck - 0.5f) * 0.035f;
                        byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
                        px[y * N + x] = new Color32(b, b, b, 255);
                    }
                }
                // scratches: short bright/dark diagonal lines
                for (int s = 0; s < 26; s++)
                {
                    int x0 = rng.Next(N), y0 = rng.Next(N), len = 6 + rng.Next(22);
                    float ang = (float)rng.NextDouble() * Mathf.PI;
                    float dx = Mathf.Cos(ang), dy = Mathf.Sin(ang);
                    bool bright = rng.NextDouble() < 0.6;
                    for (int i = 0; i < len; i++)
                    {
                        int x = ((int)(x0 + dx * i) % N + N) % N, y = ((int)(y0 + dy * i) % N + N) % N;
                        var c = px[y * N + x];
                        int v = bright ? Mathf.Min(255, c.r + 22) : Mathf.Max(0, c.r - 26);
                        px[y * N + x] = new Color32((byte)v, (byte)v, (byte)v, 255);
                    }
                }
                grain = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "Film_Grain", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, anisoLevel = 2 };
                grain.SetPixels32(px);
                grain.Apply(true, true);
                return grain;
            }
        }

        /// Tiny viewfinder graphic (gradient, rule-of-thirds, corner brackets, center cross) for the device screens.
        public static Texture2D ScreenTexture
        {
            get
            {
                if (screenTex != null) return screenTex;
                const int W = 128, H = 72;
                var px = new Color32[W * H];
                for (int y = 0; y < H; y++)
                {
                    for (int x = 0; x < W; x++)
                    {
                        float fy = y / (float)(H - 1);
                        float v = Mathf.Lerp(0.16f, 0.34f, fy);                         // gradient (water-ish)
                        float caustic = Mathf.PerlinNoise(x * 0.09f, y * 0.11f);
                        v += (caustic - 0.5f) * 0.08f;
                        if (x == W / 3 || x == 2 * W / 3 || y == H / 3 || y == 2 * H / 3) v += 0.12f;   // thirds
                        bool cornerX = x < 14 || x >= W - 14, cornerY = y < 10 || y >= H - 10;
                        bool edgeX = x == 4 || x == 5 || x == W - 5 || x == W - 6;
                        bool edgeY = y == 4 || y == 5 || y == H - 5 || y == H - 6;
                        if ((cornerX && cornerY) && ((edgeX && y >= 4 && y < H - 4) || (edgeY && x >= 4 && x < W - 4))) v = 0.95f; // brackets
                        int cx = W / 2, cy = H / 2;
                        if ((Mathf.Abs(x - cx) <= 6 && (y == cy)) || (Mathf.Abs(y - cy) <= 5 && x == cx)) v = 0.9f;               // cross
                        byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
                        px[y * W + x] = new Color32(b, b, b, 255);
                    }
                }
                screenTex = new Texture2D(W, H, TextureFormat.RGBA32, true) { name = "Film_Screen", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                screenTex.SetPixels32(px);
                screenTex.Apply(true, true);
                return screenTex;
            }
        }

        // ------------------------------------------------------------------ surfaces
        /// Shared textured PBR surface (cached by color/smoothness/metallic/tiling).
        public static Material Surface(Color c, float smoothness, float metallic = 0f, float tiling = 1f)
        {
            string key = $"s{c.r:F3}{c.g:F3}{c.b:F3}_{smoothness:F2}_{metallic:F2}_{tiling:F1}";
            if (Cache.TryGetValue(key, out var m) && m != null) return m;
            m = MaterialLibrary.Textured(Grain, c, smoothness, metallic, new Vector2(tiling, tiling));
            m.name = "Film_Surface";
            Cache[key] = m;
            return m;
        }

        /// Like Surface but rendered from both sides (open shells such as a matte box).
        public static Material SurfaceTwoSided(Color c, float smoothness, float metallic = 0f)
        {
            string key = $"d{c.r:F3}{c.g:F3}{c.b:F3}_{smoothness:F2}_{metallic:F2}";
            if (Cache.TryGetValue(key, out var m) && m != null) return m;
            m = MaterialLibrary.Textured(Grain, c, smoothness, metallic);
            MaterialLibrary.MakeTwoSided(m);
            m.name = "Film_SurfaceTwoSided";
            Cache[key] = m;
            return m;
        }

        /// Transparent two-sided glass/plastic (bags, housings, dome ports).
        public static Material Glass(Color c) => MaterialLibrary.Transparent(c, 0.95f, true);

        public static Material Glow(Color c, float intensity) => MaterialLibrary.Emissive(c * 0.25f, c, intensity);

        static Material MakeEmissive(string name, Color baseColor, Color emission, Texture2D map)
        {
            var m = new Material(MaterialLibrary.Lit) { name = name };
            m.SetColor("_BaseColor", baseColor);
            m.SetFloat("_Smoothness", 0.85f);
            m.SetColor("_EmissionColor", emission);
            if (map != null)
            {
                m.SetTexture("_BaseMap", map);
                m.SetTexture("_EmissionMap", map);
            }
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            return m;
        }

        /// Screen material per viewfinder state (standby, live, weak, good, captured).
        public static Material Screen(int state)
        {
            if (screens == null)
            {
                Color[] tint =
                {
                    new Color(0.35f, 0.45f, 0.55f) * 0.35f,
                    new Color(0.55f, 0.72f, 0.88f) * 0.85f,
                    new Color(1.00f, 0.60f, 0.15f) * 0.95f,
                    new Color(0.25f, 1.00f, 0.50f) * 0.95f,
                    new Color(0.35f, 0.95f, 1.00f) * 1.40f,
                };
                screens = new Material[tint.Length];
                for (int i = 0; i < tint.Length; i++)
                    screens[i] = MakeEmissive("Film_Screen" + i, new Color(0.02f, 0.025f, 0.03f), tint[i], ScreenTexture);
            }
            return screens[Mathf.Clamp(state, 0, screens.Length - 1)];
        }

        /// Storage bar on the screen: 0 = plenty, 1 = low, 2 = almost full.
        public static Material Tape(int state)
        {
            if (tapes == null)
            {
                tapes = new[]
                {
                    MakeEmissive("Film_Tape0", new Color(0.1f, 0.3f, 0.15f), new Color(0.3f, 1f, 0.45f) * 1.2f, null),
                    MakeEmissive("Film_Tape1", new Color(0.3f, 0.2f, 0.05f), new Color(1f, 0.7f, 0.15f) * 1.2f, null),
                    MakeEmissive("Film_Tape2", new Color(0.3f, 0.05f, 0.05f), new Color(1f, 0.2f, 0.15f) * 1.4f, null),
                };
            }
            return tapes[Mathf.Clamp(state, 0, tapes.Length - 1)];
        }

        /// Bright red emissive "REC" dot / tally light.
        public static Material RecDot
        {
            get
            {
                if (recDot == null) recDot = MakeEmissive("Film_RecDot", new Color(0.5f, 0.02f, 0.02f), new Color(1f, 0.08f, 0.05f) * 4f, null);
                return recDot;
            }
        }

        /// Strobe / LED panel diffuser, off and on (on while the player's flashlight is on).
        public static Material LampDiffuser(bool on)
        {
            if (lampOff == null)
            {
                lampOff = Surface(new Color(0.78f, 0.8f, 0.82f), 0.85f);
                lampOn = MakeEmissive("Film_LampOn", new Color(0.95f, 0.96f, 1f), new Color(1f, 0.97f, 0.9f) * 3.2f, null);
            }
            return on ? lampOn : lampOff;
        }
    }
}
