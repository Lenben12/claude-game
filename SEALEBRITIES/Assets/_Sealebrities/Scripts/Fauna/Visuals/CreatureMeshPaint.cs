using System;
using UnityEngine;

namespace Sealebrities
{
    /// Tiny CPU image of flat color blocks (becomes a point-filtered texture, see LowPoly.BlockTexture).
    internal sealed class CreatureMeshImage
    {
        public readonly int W, H;
        public readonly Color[] C;
        public bool WrapU, WrapV;

        public CreatureMeshImage(int w, int h)
        {
            W = Math.Max(1, w); H = Math.Max(1, h);
            C = new Color[W * H];
        }

        public Color Get(int x, int y)
        {
            x = WrapU ? ((x % W) + W) % W : (x < 0 ? 0 : (x >= W ? W - 1 : x));
            y = WrapV ? ((y % H) + H) % H : (y < 0 ? 0 : (y >= H ? H - 1 : y));
            return C[y * W + x];
        }

        /// Point sample (what the GPU does with FilterMode.Point).
        public Color Sample(Vector2 uv)
        {
            float fx = uv.x * W, fy = uv.y * H;
            int x = float.IsNaN(fx) ? 0 : (int)Math.Floor(fx);
            int y = float.IsNaN(fy) ? 0 : (int)Math.Floor(fy);
            return Get(x, y);
        }

        /// Bilinear sample (FilterMode.Bilinear on the GPU) – smooth "character" skins.
        public Color SampleBilinear(Vector2 uv)
        {
            float fx = uv.x * W - 0.5f, fy = uv.y * H - 0.5f;
            if (float.IsNaN(fx) || float.IsNaN(fy)) return C[0];
            int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy);
            float tx = fx - x0, ty = fy - y0;
            Color a = Get(x0, y0), b = Get(x0 + 1, y0), c = Get(x0, y0 + 1), d = Get(x0 + 1, y0 + 1);
            Color ab = CreatureMeshMath.Mix(a, b, tx), cd = CreatureMeshMath.Mix(c, d, tx);
            return CreatureMeshMath.Mix(ab, cd, ty);
        }

        public Color Sample(Vector2 uv, bool bilinear) => bilinear ? SampleBilinear(uv) : Sample(uv);

        public Color32[] ToColor32()
        {
            var r = new Color32[C.Length];
            for (int i = 0; i < C.Length; i++)
            {
                Color c = C[i];
                r[i] = new Color32(B(c.r), B(c.g), B(c.b), B(c.a));
            }
            return r;
        }

        static byte B(float f) => (byte)(f <= 0f ? 0 : (f >= 1f ? 255 : (int)(f * 255f + 0.5f)));
    }

    /// Low-poly creature painting: solid colors and crisp color-block patterns (clownfish bands, tang palette,
    /// whale-shark spots ...) on tiny textures. No noise, no scales, no normal maps. Mottled species are painted
    /// as random tone blocks that the mesh samples once per facet (see CreatureMeshFlat).
    internal static partial class CreatureMeshPaint
    {
        // palette anchors (same values as Core/LowPoly; kept managed so baking can run off the main thread)
        static readonly Color White = new Color(0.957f, 0.945f, 0.918f, 1f);
        static readonly Color Ink = new Color(0.133f, 0.145f, 0.18f, 1f);

        static Color Mix(Color a, Color b, float t) => CreatureMeshMath.Mix(a, b, CreatureMeshMath.Clamp01(t));
        static Color Mul(Color c, float k) => new Color(Math.Min(1f, c.r * k), Math.Min(1f, c.g * k), Math.Min(1f, c.b * k), 1f);
        static Color Rgb(float r, float g, float b) => new Color(r, g, b, 1f);

        static int SeedOf(string id)
        {
            int h = 17;
            if (id != null) foreach (char ch in id) h = h * 31 + ch;
            return h & 0x7FFF;
        }

        /// Species colors, nudged toward bright toy tones for the sunny shallows (the deep stays as designed).
        sealed class Pal
        {
            public readonly Color A, B, C;
            public readonly int Seed;
            public readonly string Id, Variant;
            public readonly SpeciesDef S;

            public Pal(SpeciesDef s)
            {
                S = s;
                Id = s.id ?? "";
                Variant = s.variant ?? "";
                Seed = SeedOf(s.id);
                bool shallow = s.minDepth < 50f;
                A = Toy(s.colorA, shallow);
                B = Toy(s.colorB, shallow);
                C = Toy(s.colorC, shallow);
            }

            static Color Toy(Color c, bool shallow)
            {
                c.a = 1f;
                if (!shallow) return c;
                c = CreatureMeshMath.Saturate(c, 1.12f);
                return new Color(Math.Min(1f, c.r * 1.04f + 0.01f), Math.Min(1f, c.g * 1.04f + 0.01f), Math.Min(1f, c.b * 1.04f + 0.01f), 1f);
            }

            /// Deterministic 0..1 per texel.
            public float R(int x, int y, int salt) => CreatureMeshNoise.Rand(x, y, salt, Seed);
        }

        delegate Color LoftFn(int x, int y, float u, float elev, int eRow);

        /// Loft layout image: u (x) along the body, v (y) around (0 belly, 0.5 back). eRow = mirrored row (0 belly .. H/2-1 back).
        static CreatureMeshImage Loft(int w, int h, LoftFn f)
        {
            var img = new CreatureMeshImage(w, h) { WrapU = false, WrapV = true };
            for (int y = 0; y < h; y++)
            {
                float v = (y + 0.5f) / h;
                float elev = 1f - CreatureMeshMath.Abs(1f - 2f * v);
                int eRow = Math.Min(y, h - 1 - y);
                for (int x = 0; x < w; x++)
                {
                    Color c = f(x, y, (x + 0.5f) / w, elev, eRow);
                    c.a = 1f;
                    img.C[y * w + x] = c;
                }
            }
            return img;
        }

        // ============================================================================================ skin

        public static CreatureMeshImage Skin(CreatureMeshContext ctx)
        {
            var s = ctx.S;
            var P = new Pal(s);
            if (ctx.Layout == CreatureMeshLayout.Bell)
            {
                var img = new CreatureMeshImage(2, 2);
                for (int i = 0; i < img.C.Length; i++) img.C[i] = P.A;
                return img;
            }
            var r2 = SkinR2(ctx, P);
            if (r2 != null) return r2;
            if (ctx.Layout == CreatureMeshLayout.Disc) return Disc(ctx, P);

            switch (P.Id)
            {
                case "clownfish": return Clownfish(P);
                case "blue_tang": return Tang(P);
                case "emperor_angelfish": return Angelfish(ctx, P);
                case "parrotfish": return Parrotfish(P);
                case "lionfish": return Lionfish(P);
                case "barracuda": return Barracuda(P);
                case "garibaldi": return TwoTone(P, 16, 16, 0.3f, P.B, null);
                case "leopard_shark": return LeopardShark(P);
                case "whale_shark": return WhaleShark(P);
                case "giant_sea_bass": return SpeckledBass(P);
                case "hammerhead": return Hammerhead(ctx, P);
                case "sea_lion": return SeaLion(P);
                case "sunfish": return Sunfish(P);
                case "anglerfish":
                    return Mottle(P, 16, 8, P.A, Mix(P.A, Ink, 0.55f), Mix(Mul(P.A, 1.9f), Rgb(0.32f, 0.3f, 0.38f), 0.5f), 0.3f, 0.22f, Mix(P.A, Rgb(0.3f, 0.29f, 0.34f), 0.45f), 0.28f);
                case "vampire_squid":
                    return Mottle(P, 16, 8, P.A, P.B, Mul(P.A, 1.35f), 0.28f, 0.16f, Mix(P.A, P.B, 0.5f), 0.25f);
                case "octopus":
                    return Mottle(P, 16, 8, P.A, Mix(P.A, P.C, 0.6f), Mix(P.A, P.B, 0.55f), 0.25f, 0.2f, Mix(P.A, P.B, 0.7f), 0.28f);
                case "giant_squid":
                    return Mottle(P, 16, 8, P.A, Mix(P.A, P.C, 0.65f), Mix(P.A, P.B, 0.5f), 0.25f, 0.2f, Mix(P.A, P.B, 0.6f), 0.25f);
                case "cuttlefish": return Cuttlefish(P);
                case "seahorse":
                    return Mottle(P, 32, 8, P.A, P.C, P.B, 0.14f, 0.38f, P.A, -1f);
                case "moray": return Moray(P);
                case "grouper": return Grouper(P);
                case "green_turtle": return TurtleSkin(P);
            }

            // generic fallbacks by pattern (future species)
            switch (s.pattern)
            {
                case PatternType.Mottled:
                    return Mottle(P, 16, 8, P.A, Mix(P.A, P.C, 0.6f), Mix(P.A, P.B, 0.5f), 0.25f, 0.2f, Mix(P.A, P.B, 0.6f), 0.28f);
                case PatternType.Spots:
                    return SpeckledBass(P);
                case PatternType.VerticalBands:
                    return Loft(32, 16, (x, y, u, e, r) => x > 3 && x < 30 && x % 6 < 2 ? P.B : (e < 0.3f ? Mix(P.A, P.B, 0.4f) : P.A));
                case PatternType.HorizontalStripes:
                    return Loft(32, 16, (x, y, u, e, r) => r % 3 == 0 ? P.B : P.A);
                case PatternType.BellyGradient:
                    return TwoTone(P, 16, 16, 0.34f, P.B, null);
                default:
                    return TwoTone(P, 16, 16, 0.28f, Mix(P.A, White, 0.25f), null);
            }
        }

        /// Back color A, belly color below bellyLine (elevation 0..1), one blended row in between.
        static CreatureMeshImage TwoTone(Pal P, int w, int h, float bellyLine, Color belly, LoftFn extra)
        {
            Color mid = Mix(P.A, belly, 0.5f);
            return Loft(w, h, (x, y, u, e, r) =>
            {
                if (extra != null)
                {
                    Color c = extra(x, y, u, e, r);
                    if (c.a > 0.5f) return c;
                }
                if (e < bellyLine) return belly;
                if (e < bellyLine + 0.13f) return mid;
                return P.A;
            });
        }

        static readonly Color None = new Color(0f, 0f, 0f, 0f);

        /// Random tone blocks (sampled once per facet): base / dark / light with probabilities, lighter belly.
        static CreatureMeshImage Mottle(Pal P, int w, int h, Color baseC, Color dark, Color light, float pDark, float pLight, Color belly, float bellyLine)
        {
            return Loft(w, h, (x, y, u, e, r) =>
            {
                float n = 0.55f * P.R(x / 2, y / 2, 3) + 0.45f * P.R(x, y, 4);
                Color c = n < pDark ? dark : (n > 1f - pLight ? light : baseC);
                if (bellyLine > 0f && e < bellyLine) c = n < pDark * 0.5f ? Mix(belly, dark, 0.35f) : belly;
                return c;
            });
        }

        static void BandRange(float u0, float u1, int w, out int x0, out int x1)
        {
            x0 = (int)Math.Floor(u0 * w);
            x1 = (int)Math.Ceiling(u1 * w) - 1;
        }

        // ---------------------------------------------------------------------------------- reef fish

        /// Orange with three white bands edged in black; the middle band bulges forward on the back.
        static CreatureMeshImage Clownfish(Pal P)
        {
            const int W = 64;
            Color body = P.A, belly = Mix(P.A, White, 0.2f), white = Mix(P.B, White, 0.35f), black = Mix(P.C, Ink, 0.5f);
            float[] u0 = { 0.165f, 0.45f, 0.83f };
            float[] u1 = { 0.245f, 0.56f, 0.875f };
            return Loft(W, 16, (x, y, u, e, r) =>
            {
                Color c = e < 0.26f ? belly : body;
                for (int i = 0; i < 3; i++)
                {
                    float a0 = u0[i], a1 = u1[i];
                    if (i == 1 && e > 0.55f) a0 -= 0.035f;
                    if (i == 0 && e > 0.7f) a1 += 0.016f;
                    BandRange(a0, a1, W, out int x0, out int x1);
                    if (x >= x0 && x <= x1) return white;
                    if (x == x0 - 1 || x == x1 + 1) return black;
                }
                return c;
            });
        }

        /// Blue tang: royal blue, black "palette" (band from the eye to the tail + dark cap with a blue window), yellow tail base.
        static CreatureMeshImage Tang(Pal P)
        {
            Color blue = P.A, belly = Mix(P.A, Rgb(0.55f, 0.78f, 1f), 0.4f), black = Mix(P.B, Ink, 0.3f), yellow = P.C;
            return Loft(32, 32, (x, y, u, e, r) =>
            {
                if (u > 0.9f && e > 0.12f && e < 0.88f) return yellow;
                float cen = CreatureMeshMath.Lerp(0.7f, 0.56f, CreatureMeshMath.Clamp01((u - 0.12f) / 0.76f));
                bool band = CreatureMeshMath.Abs(e - cen) < 0.1f && u > 0.12f && u < 0.9f;
                bool cap = e > 0.76f && u > 0.27f && u < 0.78f;
                float wx = (u - 0.53f) / 0.13f, wy = (e - 0.875f) / 0.07f;
                bool window = wx * wx + wy * wy < 1f;
                if ((band || cap) && !window) return black;
                return e < 0.22f ? belly : blue;
            });
        }

        /// Emperor angelfish: yellow and blue stripes stepping up toward the tail, dark eye mask, black bar with pale
        /// rims, pale snout.
        static CreatureMeshImage Angelfish(CreatureMeshContext ctx, Pal P)
        {
            const int W = 32;
            Color blue = P.A, yellow = P.B, navy = Mix(P.C, Ink, 0.25f), rim = Rgb(0.62f, 0.82f, 1f), snout = Rgb(0.8f, 0.88f, 0.96f);
            float eu = ctx.EyeU > 0f ? ctx.EyeU : 0.13f;
            return Loft(W, 32, (x, y, u, e, r) =>
            {
                if (u < 0.07f) return snout;
                if (u >= 0.185f && u < 0.245f && e > 0.12f && e < 0.92f) return Ink;
                if ((u >= 0.155f && u < 0.185f || u >= 0.245f && u < 0.275f) && e > 0.12f && e < 0.92f) return rim;
                if (CreatureMeshMath.Abs(u - eu) < 0.05f && e > 0.38f && e < 0.94f) return navy;
                if (u >= 0.275f && e > 0.16f)
                {
                    int k = r + (x - 8) / 8;   // yellow one-row stripes every third row, stepping up toward the tail
                    if (k % 3 == 0) return yellow;
                }
                return blue;
            });
        }

        /// Parrotfish: teal back, pink belly, pink/yellow face streaks.
        static CreatureMeshImage Parrotfish(Pal P)
        {
            Color teal = P.A, pink = P.B, yellow = P.C;
            return TwoTone(P, 32, 16, 0.3f, pink, (x, y, u, e, r) =>
            {
                if (x < 5 && r == 4) return pink;
                if (x < 4 && r == 6) return yellow;
                if (x >= 5 && x < 28 && r == 7 && x % 4 == 0) return Mix(teal, yellow, 0.45f);
                return None;
            });
        }

        /// Lionfish: thick red bands with thin cream gaps, slightly slanted.
        static CreatureMeshImage Lionfish(Pal P)
        {
            Color red = P.A, cream = Mix(P.B, White, 0.2f), dark = Mix(P.C, P.A, 0.3f);
            return Loft(32, 16, (x, y, u, e, r) =>
            {
                int k = x + r / 3;
                Color c = k % 3 == 2 ? cream : (k % 6 == 0 ? dark : red);
                if (e < 0.2f) c = k % 3 == 2 ? cream : Mix(red, cream, 0.45f);
                return c;
            });
        }

        /// Barracuda: silver flanks, dark slate back, oblique bars, a few black blotches toward the tail.
        static CreatureMeshImage Barracuda(Pal P)
        {
            Color silver = P.A, back = P.B, belly = Mix(P.A, White, 0.55f), bar = Mix(P.A, P.B, 0.55f);
            return Loft(32, 16, (x, y, u, e, r) =>
            {
                if (e > 0.8f) return back;
                if (e < 0.3f) return belly;
                if (e >= 0.55f && x >= 5 && x < 29 && (x + r) % 4 == 0) return bar;
                if (e < 0.55f && x >= 18 && x < 30 && P.R(x, y, 9) > 0.82f) return Ink;
                return silver;
            });
        }

        // ---------------------------------------------------------------------------------- big fish / sharks

        static CreatureMeshImage LeopardShark(Pal P)
        {
            Color back = P.A, dark = P.B, belly = P.C;
            return Loft(48, 16, (x, y, u, e, r) =>
            {
                if (e < 0.3f) return belly;
                if (e < 0.4f) return Mix(back, belly, 0.5f);
                if (e > 0.72f && x >= 6 && x < 44 && (x - 6) % 5 < 2) return dark;
                if (e < 0.72f && x >= 5 && x < 44 && (x + y) % 2 == 0 && P.R(x, y, 5) > 0.72f) return dark;
                return back;
            });
        }

        /// Whale shark: dark blue-grey back with a grid of white spots and pale ridge lines, white belly.
        static CreatureMeshImage WhaleShark(Pal P)
        {
            Color back = P.A, white = Mix(P.B, White, 0.15f), ridge = Mix(P.A, P.B, 0.22f);
            return Loft(64, 32, (x, y, u, e, r) =>
            {
                if (e < 0.3f) return white;
                if (e < 0.37f) return Mix(back, white, 0.4f);
                bool head = u < 0.17f;
                bool spot = head ? (x % 2 == 1 && r % 2 == (x / 2) % 2) : (x % 3 == 1 && r % 2 == (x / 3) % 2);
                if (spot && u > 0.03f && u < 0.93f) return white;
                if ((r == 10 || r == 13) && u > 0.16f && u < 0.85f) return ridge;
                return back;
            });
        }

        /// Dark body with scattered darker / pale facets (giant sea bass), lighter belly.
        static CreatureMeshImage SpeckledBass(Pal P)
        {
            Color back = P.A, belly = Mix(P.A, P.B, 0.6f), dark = Mix(P.C, Ink, 0.2f), pale = Mix(P.A, P.B, 0.75f);
            return Loft(16, 8, (x, y, u, e, r) =>
            {
                if (e < 0.3f) return belly;
                float n = P.R(x, y, 7);
                if (n < 0.16f) return dark;
                if (n > 0.88f) return pale;
                return back;
            });
        }

        static CreatureMeshImage Hammerhead(CreatureMeshContext ctx, Pal P)
        {
            Color slit = Mix(P.A, Ink, 0.45f);
            int gx = ctx.GillU > 0f ? (int)(ctx.GillU * 48f) : 9;
            return TwoTone(P, 48, 16, 0.36f, Mix(P.B, White, 0.2f), (x, y, u, e, r) =>
                e > 0.4f && e < 0.66f && x >= gx && x <= gx + 4 && (x - gx) % 2 == 0 ? slit : (e > 0.86f ? Mul(P.A, 0.88f) : None));
        }

        /// Sea lion: brown back, sandy belly, light muzzle so the big dark eyes pop, dark nose tip.
        static CreatureMeshImage SeaLion(Pal P)
        {
            Color nose = Mix(P.C, Ink, 0.35f), muzzle = Mix(P.B, White, 0.3f);
            return TwoTone(P, 32, 16, 0.3f, P.B, (x, y, u, e, r) =>
            {
                if (x == 0) return nose;
                if (x <= 3 && e < 0.8f) return muzzle;
                if (x <= 5 && e < 0.55f) return muzzle;
                if (e > 0.86f) return Mul(P.A, 0.9f);
                return None;
            });
        }

        static CreatureMeshImage Sunfish(Pal P)
        {
            return TwoTone(P, 16, 16, 0.32f, P.B, (x, y, u, e, r) =>
                e > 0.86f ? Mix(P.A, P.C, 0.35f) : (x == 0 && e > 0.3f ? Mix(P.A, P.C, 0.4f) : None));
        }

        // ---------------------------------------------------------------------------------- mottled (faceted)

        static CreatureMeshImage Cuttlefish(Pal P)
        {
            Color dark = Mix(P.C, Ink, 0.15f), light = Mix(P.A, P.B, 0.5f), seam = Mix(P.B, White, 0.3f);
            return Loft(16, 16, (x, y, u, e, r) =>
            {
                if (e > 0.72f && x % 3 == 0 && x > 1 && x < 15) return dark;
                if (e > 0.42f && e < 0.56f) return seam;
                float n = 0.55f * P.R(x / 2, y / 2, 3) + 0.45f * P.R(x, y, 4);
                if (e < 0.3f) return n < 0.15f ? light : Mix(P.A, P.B, 0.7f);
                return n < 0.24f ? Mix(P.A, P.C, 0.6f) : (n > 0.8f ? light : P.A);
            });
        }

        /// Giant moray: yellow-brown with dark leopard blotches (per facet), paler throat.
        static CreatureMeshImage Moray(Pal P)
        {
            Color baseC = P.A, dark = Mix(P.B, P.A, 0.15f), speck = P.C;
            return Loft(32, 8, (x, y, u, e, r) =>
            {
                float n = 0.5f * P.R(x / 2, y / 2, 3) + 0.5f * P.R(x, y, 4);
                float pd = u < 0.08f ? 0.18f : 0.42f;
                if (e < 0.3f && u < 0.2f) return Mix(baseC, speck, 0.45f);
                if (n < pd) return dark;
                if (n > 0.92f) return speck;
                return baseC;
            });
        }

        static CreatureMeshImage Grouper(Pal P)
        {
            Color baseC = P.A, dark = Mix(P.C, Ink, 0.1f), light = Mix(P.B, White, 0.1f), belly = Mix(P.A, P.B, 0.6f);
            return Loft(16, 8, (x, y, u, e, r) =>
            {
                float n = 0.55f * P.R(x / 2, y / 2, 3) + 0.45f * P.R(x, y, 4);
                float pd = x % 4 < 2 ? 0.42f : 0.2f;   // loose vertical bars
                if (e < 0.28f) return n < 0.12f ? dark : belly;
                if (n < pd) return dark;
                if (n > 0.86f) return light;
                return baseC;
            });
        }

        /// Turtle head/neck (u < 0.45) and flippers (u >= 0.5): faceted olive scales, pale throat, darker beak.
        static CreatureMeshImage TurtleSkin(Pal P)
        {
            Color baseC = P.A, dark = Mix(P.A, P.C, 0.55f), light = Mix(P.B, Rgb(0.95f, 0.9f, 0.62f), 0.2f), throat = Mix(P.B, Rgb(0.98f, 0.93f, 0.7f), 0.45f);
            Color beak = Rgb(0.55f, 0.5f, 0.3f);
            return Loft(32, 8, (x, y, u, e, r) =>
            {
                if (x == 0) return beak;
                float n = 0.5f * P.R(x / 2, y / 2, 3) + 0.5f * P.R(x, y, 4);
                if (u < 0.47f && e < 0.3f) return throat;
                if (u >= 0.47f && (x % 4 == 3)) return light;   // pale scale rows on the flippers
                if (n < 0.28f) return dark;
                if (n > 0.72f) return light;
                return baseC;
            });
        }

        // ---------------------------------------------------------------------------------- rays

        static CreatureMeshImage Disc(CreatureMeshContext ctx, Pal P)
        {
            bool manta = P.Variant == "manta", eagleRay = P.Variant == "eagle";
            // square texels on the disc: the top / bottom halves each cover 48 % of the rows
            int W = manta ? 64 : 32;
            int H = Math.Max(16, Math.Min(128, 2 * (int)Math.Round(W * ctx.DiscLength / 0.96f)));
            var img = new CreatureMeshImage(W, H);
            for (int y = 0; y < H; y++)
            {
                float v = (y + 0.5f) / H;
                bool top = v >= 0.5f;
                float along = CreatureMeshMath.Clamp01(top ? (v - 0.52f) / 0.48f : (0.48f - v) / 0.48f);
                for (int x = 0; x < W; x++)
                {
                    float xx = (x + 0.5f) / W - 0.5f, ax = Math.Min(0.499f, CreatureMeshMath.Abs(xx));
                    float zl = ctx.DiscLeadZ != null ? ctx.DiscLeadZ(ax) : ctx.DiscFront;
                    float zt = ctx.DiscTrailZ != null ? ctx.DiscTrailZ(ax) : ctx.DiscFront - ctx.DiscLength;
                    float z = ctx.DiscFront - along * ctx.DiscLength;
                    float sc = zl - zt > 1e-4f ? CreatureMeshMath.Clamp01((zl - z) / (zl - zt)) : 0.5f;
                    bool rim = ax > 0.42f || sc < 0.1f || sc > 0.9f;
                    Color c;
                    if (manta)
                    {
                        if (top)
                        {
                            c = P.A;
                            bool patch = along > 0.1f && along < 0.56f && ax > 0.07f && ax < 0.33f && sc < 0.64f - 1.5f * (ax - 0.07f) && sc > 0.12f;
                            if (patch) c = Mix(P.B, P.A, 0.18f);
                            else if (sc < 0.1f && ax < 0.4f) c = Mix(P.A, P.C, 0.7f);
                        }
                        else
                        {
                            c = Mix(P.B, White, 0.3f);
                            if (ax > 0.36f || (sc > 0.8f && ax > 0.12f)) c = Mix(P.A, P.B, 0.4f);
                            else if (ax < 0.14f && along > 0.3f && along < 0.62f && P.R(x, y, 13) > 0.84f) c = P.C;
                        }
                    }
                    else
                    {
                        if (top)
                        {
                            c = P.A;
                            // 2x2 blue spots, at most one jittered spot per 4x4 cell
                            int cx = x >> 2, cy = y >> 2, lx = x & 3, ly = y & 3;
                            int ox = (int)(P.R(cx, cy, 1) * 2.99f), oy = (int)(P.R(cx, cy, 2) * 2.99f);
                            bool spot = P.R(cx, cy, 3) < 0.55f && lx >= ox && lx <= ox + 1 && ly >= oy && ly <= oy + 1;
                            if (rim) c = Mix(P.A, P.C, 0.45f);
                            else if (spot) c = eagleRay ? Mix(P.B, White, 0.15f) : Mix(P.B, Rgb(0.55f, 0.85f, 1f), 0.2f);
                            else if (ax < 0.05f && along > 0.15f && along < 0.62f) c = Mul(P.A, 0.84f);
                        }
                        else
                        {
                            c = Rgb(0.93f, 0.9f, 0.84f);
                            if (rim) c = Mix(c, P.A, 0.3f);
                        }
                    }
                    c.a = 1f;
                    img.C[y * W + x] = c;
                }
            }
            return img;
        }

        // ============================================================================================ fins

        /// Fin atlas 8 x 16: 4 bands (0 median, 1 caudal, 2 paired, 3 arms/tails/special), each 4 rows along the fin
        /// base (s); x = base (0) -> edge (7).
        public static CreatureMeshImage Fins(CreatureMeshContext ctx)
        {
            var P = new Pal(ctx.S);
            const int W = 8, H = 16;
            var img = new CreatureMeshImage(W, H);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    Color c = FinTexel(ctx, P, y / 4, x, y % 4, y);
                    c.a = 1f;
                    img.C[y * W + x] = c;
                }
            return img;
        }

        static Color FinMottle(Pal P, int x, int y, Color a, Color dark, Color light)
        {
            float n = P.R(x, y, 21);
            return n < 0.3f ? dark : (n > 0.78f ? light : a);
        }

        static Color FinTexel(CreatureMeshContext ctx, Pal P, int band, int x, int s, int y)
        {
            Color A = P.A, B = P.B, C = P.C;
            if (FinR2(ctx, P, band, x, s, y, out Color r2)) return r2;
            switch (P.Id)
            {
                case "clownfish":
                    if (band == 2) return x >= 7 ? Mix(C, Ink, 0.5f) : A;
                    return x >= 6 ? Mix(C, Ink, 0.5f) : (x == 5 ? Mix(B, White, 0.3f) : A);
                case "blue_tang":
                    if (band == 1) return (s == 0 || s == 3) && x >= 3 ? Mix(B, Ink, 0.3f) : C;
                    if (band == 2) return x >= 5 ? C : A;
                    return x >= 6 ? Mix(B, Ink, 0.3f) : A;
                case "emperor_angelfish":
                    if (band == 1) return Mix(B, Rgb(1f, 0.6f, 0.15f), 0.2f);
                    if (band == 2) return x >= 6 ? B : Mix(C, Ink, 0.2f);
                    if (x == 7) return Mix(A, White, 0.55f);
                    return (x + s) % 2 == 0 ? A : B;
                case "parrotfish":
                    if (band == 1) return x >= 6 ? C : ((s == 0 || s == 3) && x >= 3 ? B : A);
                    if (band == 2) return x >= 6 ? B : C;
                    return x < 4 ? A : (x < 6 ? B : Mix(A, Rgb(0.3f, 0.5f, 1f), 0.4f));
                case "lionfish":
                    return x % 2 == 0 ? A : Mix(B, White, 0.2f);
                case "garibaldi":
                    return x >= 6 ? Mul(C, 0.92f) : A;
                case "barracuda":
                    if (band == 1) return x >= 5 ? Mix(B, Ink, 0.4f) : B;
                    return Mix(A, B, 0.5f);
                case "giant_sea_bass":
                    return x >= 7 ? Mix(B, White, 0.25f) : Mix(A, C, 0.5f);
                case "grouper":
                    return FinMottle(P, x, y, Mix(A, C, 0.25f), Mix(C, Ink, 0.1f), Mix(B, White, 0.1f));
                case "seahorse":
                    return x >= 6 ? Mix(B, White, 0.2f) : Mix(A, White, 0.35f);
                case "hammerhead":
                    return x >= 6 ? Mix(C, Ink, 0.2f) : A;
                case "leopard_shark":
                    return x >= 7 ? Mix(A, B, 0.5f) : (P.R(x, y, 23) > 0.75f ? B : A);
                case "whale_shark":
                    return x < 6 && (x + s) % 3 == 0 ? Mix(B, White, 0.15f) : A;
                case "manta":
                    return A;
                case "bluespotted_ray":
                    if (band == 3) return x >= 6 ? Mix(A, Ink, 0.3f) : ((s == 1 || s == 2) && x >= 1 ? Mix(B, Rgb(0.55f, 0.85f, 1f), 0.2f) : A);
                    return A;
                case "octopus":
                    if (band == 3 && (s == 0 || s == 3)) return Mix(B, White, 0.15f);
                    return FinMottle(P, x, y, A, Mix(A, C, 0.6f), Mix(A, B, 0.5f));
                case "giant_squid":
                    if (band == 3 && (s == 0 || s == 3)) return B;
                    return FinMottle(P, x, y, A, Mix(A, C, 0.6f), Mix(A, B, 0.45f));
                case "cuttlefish":
                    if (band == 0 && x >= 6) return Mix(B, White, 0.45f);
                    return FinMottle(P, x, y, A, Mix(A, C, 0.6f), Mix(A, B, 0.5f));
                case "vampire_squid":
                    if (band == 3 && s == 0 && x < 6) return Mix(A, White, 0.22f);
                    return x >= 6 ? Ink : A;
                case "sea_lion":
                    return x >= 6 ? Mix(C, Ink, 0.3f) : C;
                case "sunfish":
                    if (band == 1) return x >= 5 ? C : B;
                    return x >= 6 ? C : A;
                case "anglerfish":
                    if (band == 0) return x >= 6 ? Mix(A, B, 0.35f) : Mix(A, White, 0.12f);
                    return x >= 6 ? Mix(A, Ink, 0.4f) : A;
            }
            if (ctx.S.pattern == PatternType.Mottled) return FinMottle(P, x, y, A, Mix(A, C, 0.6f), Mix(A, B, 0.5f));
            return x >= 6 ? C : Mix(A, C, 0.35f);
        }

        // ============================================================================================ shell

        /// Turtle palette 8 x 2: 0 seam, 1-2 vertebral, 3-4 costal, 5-6 marginal, 7 plastron.
        public static CreatureMeshImage Shell(CreatureMeshContext ctx)
        {
            var P = new Pal(ctx.S);
            Color A = P.A, B = P.B, C = P.C;
            var pal = new Color[8];
            pal[0] = Mix(C, Ink, 0.45f);
            pal[1] = Mix(A, B, 0.28f);
            pal[2] = Mix(A, B, 0.4f);
            pal[3] = Mix(A, Rgb(0.55f, 0.48f, 0.22f), 0.18f);
            pal[4] = Mix(A, C, 0.22f);
            pal[5] = Mix(A, C, 0.5f);
            pal[6] = Mix(A, C, 0.36f);
            pal[7] = Mix(B, Rgb(0.98f, 0.9f, 0.58f), 0.55f);
            var img = new CreatureMeshImage(8, 2);
            for (int y = 0; y < 2; y++)
                for (int x = 0; x < 8; x++) { Color c = pal[x]; c.a = 1f; img.C[y * 8 + x] = c; }
            return img;
        }

        /// uv of a shell palette entry.
        public static Vector2 ShellUv(int entry) => new Vector2((entry + 0.5f) / 8f, 0.5f);
    }
}
