using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Pictograms for signposts and signs (language independent: icons instead of words).
    public enum PropIcon { Hut, Dock, Helipad, Lighthouse, Waterfall, Cave, Bridge, TidePools, Beach, Radio, Bay, Warning, Fish }

    /// Textures of the props module. Low-poly style: NO surface/noise/normal maps. Only
    ///  - tiny point-filtered block textures for crisp graphics (helipad "H", hut sign, poster, ship name), and
    ///  - soft gradients for particles and the lighthouse beam (FX, not surfaces).
    public static class PropTex
    {
        static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();
        const float TAU = Mathf.PI * 2f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Cache.Clear(); }

        static Texture2D Get(string key, Func<Texture2D> gen)
        {
            if (Cache.TryGetValue(key, out var t) && t != null) return t;
            t = gen();
            if (t == null) t = Texture2D.whiteTexture;
            Cache[key] = t;
            return t;
        }

        static float S(float a, float b, float x) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((x - a) / (b - a)));

        // =================================================================== block graphics
        static Color[] Fill(int w, int h, Color c)
        {
            var px = new Color[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = c;
            return px;
        }

        static void Rect(Color[] px, int w, int h, int x0, int y0, int x1, int y1, Color c)
        {
            for (int y = Mathf.Max(0, y0); y <= Mathf.Min(h - 1, y1); y++)
                for (int x = Mathf.Max(0, x0); x <= Mathf.Min(w - 1, x1); x++)
                    px[y * w + x] = c;
        }

        /// Pixel text with the 5x7 font. (x0, yTop) = top-left pixel of the first glyph (y up, row 0 = bottom).
        static void Text(Color[] px, int w, int h, string text, int x0, int yTop, Color ink, int spacing = 1)
        {
            int x = x0;
            for (int k = 0; k < text.Length; k++)
            {
                char c = text[k];
                for (int row = 0; row < 7; row++)
                {
                    for (int col = 0; col < 5; col++)
                    {
                        if (!PropFont.Bit(c, col, row)) continue;
                        int px0 = x + col, py = yTop - row;
                        if (px0 < 0 || px0 >= w || py < 0 || py >= h) continue;
                        px[py * w + px0] = ink;
                    }
                }
                x += 5 + spacing;
            }
        }

        static int TextWidth(string text, int spacing = 1) => text.Length * 5 + Mathf.Max(0, text.Length - 1) * spacing;

        /// Helipad top: concrete with a white "H" (16 x 16, point filtered, mapped with unit UVs onto the pad disk).
        public static Texture2D HelipadH => Get("helipad_h", () =>
        {
            const int W = 16, H = 16;
            var px = Fill(W, H, PropMats.ConcreteColor);
            Color white = PropPal.Trim;
            Rect(px, W, H, 4, 4, 5, 11, white);
            Rect(px, W, H, 10, 4, 11, 11, white);
            Rect(px, W, H, 6, 7, 9, 8, white);
            return LowPoly.BlockTexture(W, H, px, "SB_HelipadH");
        });

        /// Hut sign: turquoise board, "SEALEBRITY" in white and "DIVE CO." in yellow.
        public static Texture2D HutSign => Get("hut_sign", () =>
        {
            const int W = 64, H = 20;
            Color board = PropMats.ShutterColor;
            var px = Fill(W, H, board);
            Color edge = PropMats.Shade(board, -0.18f);
            Rect(px, W, H, 0, 0, W - 1, 0, edge);
            Rect(px, W, H, 0, H - 1, W - 1, H - 1, edge);
            Rect(px, W, H, 0, 0, 0, H - 1, edge);
            Rect(px, W, H, W - 1, 0, W - 1, H - 1, edge);
            string a = "SEALEBRITY", b = "DIVE CO.";
            Text(px, W, H, a, (W - TextWidth(a)) / 2, H - 3, PropPal.Trim);
            Text(px, W, H, b, (W - TextWidth(b)) / 2, H - 12, PropPal.Sun);
            return LowPoly.BlockTexture(W, H, px, "SB_HutSign");
        });

        /// FinTube poster: red play button over "FINTUBE".
        public static Texture2D Poster => Get("poster", () =>
        {
            const int W = 48, H = 40;
            var px = Fill(W, H, PropPal.Trim);
            Color red = PropPal.LighthouseRed;
            Rect(px, W, H, 10, 16, 37, 33, red);
            Rect(px, W, H, 11, 15, 36, 15, red);
            Rect(px, W, H, 11, 34, 36, 34, red);
            // white play triangle
            for (int i = 0; i < 8; i++) Rect(px, W, H, 20 + i, 18 + i / 2, 20 + i, 31 - i / 2, PropPal.Trim);
            string t = "FINTUBE";
            Text(px, W, H, t, (W - TextWidth(t)) / 2, 11, PropPal.Ink);
            return LowPoly.BlockTexture(W, H, px, "SB_Poster");
        });

        /// Ship name on the stern of the wreck.
        public static Texture2D NamePlate => Get("nameplate", () =>
        {
            const int W = 76, H = 11;
            Color hull = PropPal.HullBlue;
            var px = Fill(W, H, hull);
            string t = "MS CLICKBAIT";
            Text(px, W, H, t, (W - TextWidth(t)) / 2, 8, PropMats.Shade(PropPal.White, -0.1f));
            return LowPoly.BlockTexture(W, H, px, "SB_NamePlate");
        });

        /// Onkel Gus' old documentary poster in the secret grotto: a giant eye in the dark sea, film-strip border, "GUS".
        public static Texture2D GusPoster => Get("gus_poster", () =>
        {
            const int W = 32, H = 44;
            Color sea = new Color(0.07f, 0.16f, 0.3f), deep = new Color(0.03f, 0.06f, 0.14f);
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    px[y * W + x] = Color.Lerp(deep, sea, y / (float)H);
            // film strip borders with sprocket holes
            Rect(px, W, H, 0, 0, 2, H - 1, PropPal.Ink);
            Rect(px, W, H, W - 3, 0, W - 1, H - 1, PropPal.Ink);
            for (int y = 2; y < H; y += 4)
            {
                Rect(px, W, H, 1, y, 1, y + 1, PropPal.Paper);
                Rect(px, W, H, W - 2, y, W - 2, y + 1, PropPal.Paper);
            }
            // the eye
            for (int y = 0; y < H; y++)
            {
                for (int x = 3; x < W - 3; x++)
                {
                    float dx = (x + 0.5f - W * 0.5f) / 11f, dy = (y + 0.5f - 27f) / 6f;
                    float lens = dx * dx + dy * dy;
                    if (lens > 1f) continue;
                    float ix = (x + 0.5f - W * 0.5f) / 4.2f, iy = (y + 0.5f - 27f) / 4.2f;
                    float ir = ix * ix + iy * iy;
                    Color c = PropPal.FlowerWhite;
                    if (ir < 1f) c = PropPal.Sun;
                    if (ir < 0.3f) c = PropPal.Ink;
                    px[y * W + x] = c;
                }
            }
            Text(px, W, H, "GUS", (W - TextWidth("GUS")) / 2, 12, PropPal.Coral);
            return LowPoly.BlockTexture(W, H, px, "SB_GusPoster");
        });

        // =================================================================== ramps (art bible §1.9)
        static Texture2D Ramp(string name, Color c0, Color c1, Color c2, float midAt)
        {
            const int N = 8;
            var px = new Color[N];
            for (int i = 0; i < N; i++)
            {
                float t = i / (float)(N - 1);
                px[i] = t < midAt ? Color.Lerp(c0, c1, t / midAt) : Color.Lerp(c1, c2, (t - midAt) / (1f - midAt));
            }
            var tex = new Texture2D(1, N, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            tex.SetPixels(px);
            tex.Apply(false, true);
            return tex;
        }

        /// Grass blades: dark base → mid green at 45 % → light tip.
        public static Texture2D GrassRamp => Get("grass_ramp", () => Ramp("SB_GrassRamp", PropPal.GrassBase, PropPal.GrassMid, PropPal.GrassTip, 0.45f));
        /// Dune grass: dry olive → straw.
        public static Texture2D DuneRamp => Get("dune_ramp", () => Ramp("SB_DuneRamp", PropPal.DuneBase, Color.Lerp(PropPal.DuneBase, PropPal.DuneTip, 0.55f), PropPal.DuneTip, 0.45f));

        // =================================================================== pictograms (signposts: icons instead of words)
        static readonly string[][] Icons =
        {
            new[] { // Hut
                "................", ".......##.......", "......####......", ".....######.....", "....########....", "...##########...",
                "..############..", "....########....", "....##xx####....", "....##xx##x#....", "....##xx####....", "....##xx####....",
                "....##xx####....", "................", "................", "................" },
            new[] { // Dock (anchor)
                "................", ".......##.......", "......#..#......", "......#..#......", ".......##.......", ".....######.....",
                ".......##.......", ".......##.......", ".......##.......", ".#.....##.....#.", ".##....##....##.", "..##...##...##..",
                "...####..####...", "....########....", "................", "................" },
            new[] { // Helipad
                "................", ".....######.....", "...##......##...", "..#..........#..", "..#..#....#..#..", ".#...#....#...#.",
                ".#...#....#...#.", ".#...######...#.", ".#...######...#.", ".#...#....#...#.", "..#..#....#..#..", "..#..........#..",
                "...##......##...", ".....######.....", "................", "................" },
            new[] { // Lighthouse
                "................", "......yyyy......", ".....######.....", "......#yy#......", ".....######.....", "......####......",
                "......rrrr......", "......####......", ".....######.....", ".....rrrrrr.....", ".....######.....", "....########....",
                "....rrrrrrrr....", "...##########...", "................", "................" },
            new[] { // Waterfall
                "................", "..xxxxxxxxxxxx..", "..xx##.##.##xx..", "..x.##.##.##.x..", "....##.##.##....", "....##.##.##....",
                "....##.##.##....", "....##.##.##....", "....##.##.##....", "....##.##.##....", "...#.#.#.#.#.#..", "..#.#.#.#.#.#.#.",
                ".##############.", "................", "................", "................" },
            new[] { // Cave
                "................", "......####......", "....########....", "...##########...", "..####xxxx####..", "..###xxxxxx###..",
                ".###xxxxxxxx###.", ".###xxxxxxxx###.", ".##xxxxxxxxxx##.", ".##xxxxxxxxxx##.", ".##xxxxxxxxxx##.", "################",
                "................", "................", "................", "................" },
            new[] { // Bridge
                "................", ".#............#.", ".#............#.", ".##..........##.", ".#.##......##.#.", ".#...######...#.",
                ".#............#.", ".#.#........#.#.", ".##.##....##.##.", ".#....####....#.", "xxx..........xxx", "xxxx........xxxx",
                "xxxxx......xxxxx", "................", "................", "................" },
            new[] { // Tide pools (starfish)
                "................", ".......##.......", ".......##.......", "......####......", "......####......", ".##############.",
                "..############..", "...##########...", "....########....", "....########....", "...####..####...", "...###....###...",
                "..###......###..", "..##........##..", "................", "................" },
            new[] { // Beach (palm + wave)
                "................", "....###..###....", "..##...##...##..", ".#....####....#.", ".....##..##.....", "....#..##..#....",
                ".......##.......", "........##......", "........##......", ".......##.......", ".......##.......", "......##........",
                "..bbb.##..bbb...", ".b...bbbbb...b..", "................", "................" },
            new[] { // Radio tower
                "................", "..#....##....#..", ".#..#..##..#..#.", ".#.#...##...#.#.", ".#.#..####..#.#.", ".#..#.#..#.#..#.",
                "..#..#....#..#..", "......#..#......", ".....######.....", ".....#.##.#.....", "....#..##..#....", "....#.#..#.#....",
                "...#.#....#.#...", "...##########...", "................", "................" },
            new[] { // Bay (skull)
                "................", ".....######.....", "....########....", "...##########...", "...##xx##xx##...", "...##xx##xx##...",
                "...##########...", "....####.###....", ".....######.....", ".....#.##.#.....", ".##..........##.", "...##......##...",
                ".....##..##.....", ".......##.......", ".....##..##.....", "...##......##..." },
            new[] { // Warning
                "................", ".......rr.......", "......rrrr......", "......r##r......", ".....rr##rr.....", ".....r#xx#r.....",
                "....rr#xx#rr....", "....r##xx##r....", "...rr##xx##rr...", "...r###xx###r...", "..rr########rr..", "..r####xx####r..",
                ".rr####xx####rr.", ".rrrrrrrrrrrrrr.", "................", "................" },
            new[] { // Fish
                "................", "................", "................", "................", ".....######....#", "...#########..##",
                "..##x#######.###", ".############.##", "..###########..#", "...#########....", ".....######.....", "................",
                "................", "................", "................", "................" },
        };

        /// 16 x 16 pictogram on a weathered board (point filtered). '#' white, 'x' ink, 'r' red, 'y' yellow, 'b' blue.
        public static Texture2D Icon(PropIcon icon) => Get("icon_" + (int)icon, () =>
        {
            int k = Mathf.Clamp((int)icon, 0, Icons.Length - 1);
            var rows = Icons[k];
            const int Sz = 16;
            Color bg = icon == PropIcon.Warning ? PropPal.Sun : icon == PropIcon.Fish ? PropPal.Turquoise : PropPal.Wood3;
            Color edge = PropMats.Value(bg, 0.72f);
            var px = Fill(Sz, Sz, bg);
            for (int r = 0; r < Sz && r < rows.Length; r++)
            {
                string row = rows[r];
                int y = Sz - 1 - r;
                for (int x = 0; x < Sz && x < row.Length; x++)
                {
                    Color c;
                    switch (row[x])
                    {
                        case '#': c = PropPal.Trim; break;
                        case 'x': c = PropPal.Ink; break;
                        case 'r': c = PropPal.LighthouseRed; break;
                        case 'y': c = PropPal.Sun; break;
                        case 'b': c = PropPal.Turquoise; break;
                        default: continue;
                    }
                    px[y * Sz + x] = c;
                }
            }
            for (int i = 0; i < Sz; i++)
            {
                px[i] = edge; px[(Sz - 1) * Sz + i] = edge; px[i * Sz] = edge; px[i * Sz + Sz - 1] = edge;
            }
            return LowPoly.BlockTexture(Sz, Sz, px, "SB_Icon_" + icon);
        });

        // =================================================================== particles / beams (FX gradients)
        static Texture2D Store(string name, int w, int h, Color32[] px, TextureWrapMode wu, TextureWrapMode wv)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true, false)
            {
                name = "PropTex_" + name,
                wrapModeU = wu,
                wrapModeV = wv,
                filterMode = FilterMode.Bilinear,
            };
            t.SetPixels32(px);
            t.Apply(true, true);
            return t;
        }

        public static Texture2D SoftDot => Get("softdot", () =>
        {
            const int Sz = 32;
            var px = new Color32[Sz * Sz];
            for (int y = 0; y < Sz; y++)
            {
                for (int x = 0; x < Sz; x++)
                {
                    float dx = (x + 0.5f) / Sz * 2f - 1f, dy = (y + 0.5f) / Sz * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Pow(Mathf.Clamp01(1f - r), 1.6f);
                    px[y * Sz + x] = new Color(1f, 1f, 1f, a);
                }
            }
            return Store("softdot", Sz, Sz, px, TextureWrapMode.Clamp, TextureWrapMode.Clamp);
        });

        public static Texture2D Bubble => Get("bubble", () =>
        {
            const int Sz = 32;
            var px = new Color32[Sz * Sz];
            for (int y = 0; y < Sz; y++)
            {
                for (int x = 0; x < Sz; x++)
                {
                    float dx = (x + 0.5f) / Sz * 2f - 1f, dy = (y + 0.5f) / Sz * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float ring = S(0.6f, 0.82f, r) * (1f - S(0.88f, 1f, r));
                    float hx = dx + 0.35f, hy = dy - 0.35f;
                    float hl = Mathf.Clamp01(1f - Mathf.Sqrt(hx * hx + hy * hy) / 0.25f);
                    float a = Mathf.Clamp01(ring + hl + (r < 0.9f ? 0.12f : 0f));
                    px[y * Sz + x] = new Color(1f, 1f, 1f, a);
                }
            }
            return Store("bubble", Sz, Sz, px, TextureWrapMode.Clamp, TextureWrapMode.Clamp);
        });

        /// Four-pointed glint for the collectible sparkle.
        public static Texture2D Star => Get("star", () =>
        {
            const int Sz = 32;
            var px = new Color32[Sz * Sz];
            for (int y = 0; y < Sz; y++)
            {
                for (int x = 0; x < Sz; x++)
                {
                    float dx = Mathf.Abs((x + 0.5f) / Sz * 2f - 1f), dy = Mathf.Abs((y + 0.5f) / Sz * 2f - 1f);
                    float cross = Mathf.Max(Mathf.Clamp01(1f - dx * 6f) * (1f - dy), Mathf.Clamp01(1f - dy * 6f) * (1f - dx));
                    float core = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy) * 2.6f);
                    float a = Mathf.Clamp01(Mathf.Pow(cross, 1.5f) + core);
                    px[y * Sz + x] = new Color(1f, 1f, 1f, a);
                }
            }
            return Store("star", Sz, Sz, px, TextureWrapMode.Clamp, TextureWrapMode.Clamp);
        });

        /// Light beam gradient: v = 0 at the lamp, 1 at the far end; soft across u.
        public static Texture2D Beam => Get("beam", () =>
        {
            const int W = 16, H = 64;
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
            return Store("beam", W, H, px, TextureWrapMode.Repeat, TextureWrapMode.Clamp);
        });
    }
}
