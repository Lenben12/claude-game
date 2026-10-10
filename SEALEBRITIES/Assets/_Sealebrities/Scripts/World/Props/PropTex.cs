using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
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
            Color white = LowPoly.White;
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
            Text(px, W, H, a, (W - TextWidth(a)) / 2, H - 3, LowPoly.White);
            Text(px, W, H, b, (W - TextWidth(b)) / 2, H - 12, LowPoly.CoralYellow);
            return LowPoly.BlockTexture(W, H, px, "SB_HutSign");
        });

        /// FinTube poster: red play button over "FINTUBE".
        public static Texture2D Poster => Get("poster", () =>
        {
            const int W = 48, H = 40;
            var px = Fill(W, H, LowPoly.White);
            Color red = LowPoly.Roof;
            Rect(px, W, H, 10, 16, 37, 33, red);
            Rect(px, W, H, 11, 15, 36, 15, red);
            Rect(px, W, H, 11, 34, 36, 34, red);
            // white play triangle
            for (int i = 0; i < 8; i++) Rect(px, W, H, 20 + i, 18 + i / 2, 20 + i, 31 - i / 2, LowPoly.White);
            string t = "FINTUBE";
            Text(px, W, H, t, (W - TextWidth(t)) / 2, 11, LowPoly.Ink);
            return LowPoly.BlockTexture(W, H, px, "SB_Poster");
        });

        /// Ship name on the stern of the wreck.
        public static Texture2D NamePlate => Get("nameplate", () =>
        {
            const int W = 76, H = 11;
            Color hull = PropMats.Shade(LowPoly.Ink, 0.2f, 0.05f, -0.02f);
            var px = Fill(W, H, hull);
            string t = "MS CLICKBAIT";
            Text(px, W, H, t, (W - TextWidth(t)) / 2, 8, PropMats.Shade(LowPoly.White, -0.1f));
            return LowPoly.BlockTexture(W, H, px, "SB_NamePlate");
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
