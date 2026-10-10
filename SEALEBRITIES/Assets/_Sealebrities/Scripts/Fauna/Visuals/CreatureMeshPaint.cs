using System;
using UnityEngine;

namespace Sealebrities
{
    /// CPU image (sRGB colors + height field) produced by the painters.
    internal sealed class CreatureMeshImage
    {
        public readonly int W, H;
        public readonly Color[] C;
        public readonly float[] Hgt;
        public bool WrapU, WrapV;

        public CreatureMeshImage(int w, int h)
        {
            W = w; H = h;
            C = new Color[w * h];
            Hgt = new float[w * h];
        }

        public Color Sample(Vector2 uv)
        {
            float x = uv.x * W - 0.5f, y = uv.y * H - 0.5f;
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            float fx = x - x0, fy = y - y0;
            Color a = Px(x0, y0), b = Px(x0 + 1, y0), c = Px(x0, y0 + 1), d = Px(x0 + 1, y0 + 1);
            return CreatureMeshMath.Mix(CreatureMeshMath.Mix(a, b, fx), CreatureMeshMath.Mix(c, d, fx), fy);
        }

        Color Px(int x, int y)
        {
            x = WrapU ? ((x % W) + W) % W : (x < 0 ? 0 : (x >= W ? W - 1 : x));
            y = WrapV ? ((y % H) + H) % H : (y < 0 ? 0 : (y >= H ? H - 1 : y));
            return C[y * W + x];
        }

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

        /// Set false on platforms without worker threads.
        public static bool AllowParallel = true;

        /// Runs row(0..count-1), in parallel when allowed (painting is pure managed code).
        public static void Rows(int count, Action<int> row)
        {
            if (AllowParallel && count >= 16)
            {
                System.Threading.Tasks.Parallel.For(0, count, row);
                return;
            }
            for (int i = 0; i < count; i++) row(i);
        }

        /// Tangent-space normal map from the height field (x = +u, y = +v). texelU/V: surface size of one texel
        /// (keeps bumps isotropic). The overall strength is auto-calibrated so the 97th percentile slope equals targetSlope.
        public Color32[] NormalMap(float targetSlope, float texelU, float texelV)
        {
            int n = W * H;
            var gx = new float[n];
            var gy = new float[n];
            float iu = 1f / (2f * Math.Max(1e-6f, texelU)), iv = 1f / (2f * Math.Max(1e-6f, texelV));
            Rows(H, y =>
            {
                for (int x = 0; x < W; x++)
                {
                    gx[y * W + x] = (Hgt[Idx(x + 1, y)] - Hgt[Idx(x - 1, y)]) * iu;
                    gy[y * W + x] = (Hgt[Idx(x, y + 1)] - Hgt[Idx(x, y - 1)]) * iv;
                }
            });
            // percentile of the slope magnitude from a strided sample
            int samples = Math.Min(n, 4096);
            var mags = new float[samples];
            int stride = Math.Max(1, n / samples);
            for (int i = 0; i < samples; i++)
            {
                int k = (int)(((long)i * stride + (i * 7919L) % stride) % n);
                mags[i] = (float)Math.Sqrt(gx[k] * gx[k] + gy[k] * gy[k]);
            }
            Array.Sort(mags);
            float p = mags[(int)(samples * 0.97f)];
            float scale = p > 1e-6f ? targetSlope / p : 0f;
            var r = new Color32[n];
            Rows(H, y =>
            {
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    Vector3 nn = CreatureMeshMath.Norm(new Vector3(-gx[i] * scale, -gy[i] * scale, 1f));
                    r[i] = new Color32(B(nn.x * 0.5f + 0.5f), B(nn.y * 0.5f + 0.5f), B(nn.z * 0.5f + 0.5f), 255);
                }
            });
            return r;
        }

        int Idx(int x, int y)
        {
            x = WrapU ? ((x % W) + W) % W : (x < 0 ? 0 : (x >= W ? W - 1 : x));
            y = WrapV ? ((y % H) + H) % H : (y < 0 ? 0 : (y >= H ? H - 1 : y));
            return y * W + x;
        }
    }

    /// Procedural painting of creature skins, fins and eyes.
    internal static class CreatureMeshPaint
    {
        const float Pi = CreatureMeshMath.Pi;
        const float TwoPi = CreatureMeshMath.TwoPi;

        static float Sm(float a, float b, float x) => CreatureMeshMath.Smooth(a, b, x);
        static Color Mix(Color a, Color b, float t) => CreatureMeshMath.Mix(a, b, CreatureMeshMath.Clamp01(t));
        static Color Mul(Color c, float k) => CreatureMeshMath.Mul(c, k);
        static float Luma(Color c) => CreatureMeshMath.Luma(c);
        static float N(Vector3 p, int seed) => CreatureMeshNoise.Perlin(p, seed);
        static float Fbm(Vector3 p, int oct, int seed) => CreatureMeshNoise.Fbm(p, oct, seed);

        static int SeedOf(string id)
        {
            int h = 17;
            if (id != null) foreach (char ch in id) h = h * 31 + ch;
            return h & 0x7FFF;
        }

        internal struct Texel
        {
            public float u, v;
            public float along;   // 0 head .. 1 tail
            public float elev;    // 0 belly .. 1 back
            public float side;    // -1 left .. +1 right
            public float arc;     // surface distance around from the belly (m)
            public float perim;   // local perimeter (m)
            public Vector3 q;     // 3D noise coordinate in meters (body length 1)
            public bool top;
        }

        // ============================================================================================ skin

        public static CreatureMeshImage Skin(CreatureMeshContext ctx, int w, int h)
        {
            var img = new CreatureMeshImage(w, h) { WrapU = false, WrapV = ctx.Layout == CreatureMeshLayout.Loft };
            var p = new SkinPainter(ctx);
            CreatureMeshImage.Rows(h, y =>
            {
                var tx = new Texel();
                float v = (y + 0.5f) / h;
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w;
                    MapTexel(ctx, u, v, ref tx);
                    Color c = p.Paint(ref tx, out float hh);
                    c.r = CreatureMeshMath.Clamp01(c.r); c.g = CreatureMeshMath.Clamp01(c.g); c.b = CreatureMeshMath.Clamp01(c.b);
                    c.a = 1f;
                    img.C[y * w + x] = c;
                    img.Hgt[y * w + x] = hh;
                }
            });
            return img;
        }

        /// Surface size of one skin texel (for normal map strength).
        public static void SkinTexelSize(CreatureMeshContext ctx, int w, int h, out float tu, out float tv)
        {
            if (ctx.Layout == CreatureMeshLayout.Disc) { tu = ctx.DiscWidth / w; tv = ctx.DiscLength / (h * 0.48f); return; }
            float r = ctx.SkinRadius != null ? ctx.SkinRadius(0.4f) : 0.1f;
            tu = ctx.SkinLength / w;
            tv = TwoPi * r / h;
        }

        internal static void MapTexel(CreatureMeshContext ctx, float u, float v, ref Texel t)
        {
            t.u = u; t.v = v;
            if (ctx.Layout == CreatureMeshLayout.Disc)
            {
                t.top = v >= 0.5f;
                float a = t.top ? (v - 0.52f) / 0.48f : (0.48f - v) / 0.48f;
                t.along = CreatureMeshMath.Clamp01(a);
                float x = (u - 0.5f) * ctx.DiscWidth;
                float z = ctx.DiscFront - t.along * ctx.DiscLength;
                t.elev = t.top ? 1f : 0f;
                t.side = CreatureMeshMath.Clamp(x / (ctx.DiscWidth * 0.5f), -1f, 1f);
                t.q = new Vector3(x, t.top ? 0f : 3f, z);
                t.arc = x; t.perim = ctx.DiscWidth;
                return;
            }
            t.top = true;
            t.along = u;
            t.elev = 1f - CreatureMeshMath.Abs(1f - 2f * v);
            float ang = TwoPi * v;
            t.side = CreatureMeshMath.Sin(ang);
            float R = ctx.SkinRadius != null ? ctx.SkinRadius(u) : 0.1f;
            t.perim = TwoPi * R;
            t.arc = v * t.perim;
            t.q = new Vector3(u * ctx.SkinLength, -CreatureMeshMath.Cos(ang) * R, CreatureMeshMath.Sin(ang) * R);
        }

        sealed class SkinPainter
        {
            readonly CreatureMeshContext ctx;
            readonly SpeciesDef s;
            readonly Color A, B, C;
            readonly string variant;
            readonly BodyPlan plan;
            readonly int seed;
            readonly bool tang;

            public SkinPainter(CreatureMeshContext c)
            {
                ctx = c; s = c.S;
                A = s.colorA; B = s.colorB; C = s.colorC;
                A.a = B.a = C.a = 1f;
                variant = s.variant ?? "";
                plan = s.plan;
                seed = SeedOf(s.id);
                tang = Luma(B) < Luma(A);
            }

            /// Scale pattern: returns 0..1 height (1 = middle of a scale, 0 = its free edge); edge = darkness 0..1.
            float Scales(ref Texel t, float size, out float edge)
            {
                float x = t.q.x / size, y = t.arc / (size * 0.8f);
                float row = (float)Math.Floor(y);
                float fy = y - row;
                float off = ((int)row & 1) == 0 ? 0f : 0.5f;
                float fx = CreatureMeshMath.Frac(x + off + 0.28f * (1f - (2f * fy - 1f) * (2f * fy - 1f)));
                edge = Sm(0.78f, 0.96f, fx) * (1f - Sm(0.96f, 1f, fx));
                return Sm(0f, 0.85f, fx) * (1f - Sm(0.88f, 1f, fx));
            }

            public Color Paint(ref Texel t, out float h)
            {
                Vector3 q = t.q;
                float n1 = Fbm(q * 9f, 3, seed);
                float n2 = N(q * 37f, seed + 5);
                h = 0.5f + (n2 - 0.5f) * 0.08f;
                Color c;
                if (ctx.Layout == CreatureMeshLayout.Disc) c = Disc(ref t, n1, n2, ref h);
                else
                {
                    switch (s.pattern)
                    {
                        case PatternType.BellyGradient: c = BellyGradient(ref t, n1, n2, ref h); break;
                        case PatternType.VerticalBands: c = VerticalBands(ref t, n1, n2, ref h); break;
                        case PatternType.HorizontalStripes: c = HorizontalStripes(ref t, n1, n2, ref h); break;
                        case PatternType.Spots: c = Spots(ref t, n1, n2, ref h); break;
                        case PatternType.Mottled: c = Mottled(ref t, n1, n2, ref h); break;
                        case PatternType.Translucent: c = Mix(A, B, n1 * 0.3f); break;
                        default: c = Solid(ref t, n1, n2, ref h); break;
                    }
                    c = Overlays(ref t, c, n1, n2, ref h);
                }
                // natural variation and subtle countershading
                c = Mul(c, 0.93f + 0.14f * n1);
                if (ctx.Layout == CreatureMeshLayout.Loft) c = Mul(c, 1f - 0.08f * Sm(0.8f, 1f, t.elev));
                return c;
            }

            // ------------------------------------------------------------------ patterns

            Color Solid(ref Texel t, float n1, float n2, ref float h)
            {
                switch (plan)
                {
                    case BodyPlan.Anglerfish:
                    {
                        // velvety black-brown with faint pale speckles and warty bumps
                        float sp = Sm(0.72f, 0.8f, N(t.q * 60f, seed + 9));
                        Color c = Mix(A, Mul(A, 1.6f), 0.4f * (1f - t.elev));
                        c = Mix(c, Mix(A, new Color(0.35f, 0.33f, 0.36f), 0.6f), sp * 0.35f);
                        float wart = CreatureMeshNoise.Worley(t.q * 45f, seed, out _, out _);
                        h += (1f - Sm(0.1f, 0.45f, wart)) * 0.35f;
                        return c;
                    }
                    case BodyPlan.VampireSquid:
                    {
                        Color c = Mix(A, B, 0.35f + 0.45f * (1f - t.elev));
                        c = Mix(c, Mul(A, 1.35f), Sm(0.6f, 0.75f, n1) * 0.4f);
                        return c;
                    }
                    default:
                    {
                        // e.g. garibaldi: bright single color, lighter belly from colorB
                        Color c = Mix(B, A, Sm(0.15f, 0.55f, t.elev));
                        c = Mix(c, Mul(A, 1.1f), Sm(0.55f, 0.75f, n1) * 0.25f);
                        return c;
                    }
                }
            }

            Color BellyGradient(ref Texel t, float n1, float n2, ref float h)
            {
                float k = Sm(0.26f, 0.62f, t.elev + (n1 - 0.5f) * 0.1f);
                Color c = Mix(B, A, k);
                switch (plan)
                {
                    case BodyPlan.Fish:
                    {
                        // parrotfish-like: pastel scale margins and facial streaks in colorC
                        float e;
                        Scales(ref t, 0.045f, out e);
                        c = Mix(c, Mix(B, C, 0.3f), e * 0.55f);
                        if (t.along < 0.2f)
                        {
                            float st = CreatureMeshMath.Abs(CreatureMeshMath.Sin((t.elev * 9f + t.along * 14f) * Pi));
                            c = Mix(c, C, Sm(0.85f, 0.97f, st) * (1f - Sm(0.14f, 0.2f, t.along)) * 0.8f);
                        }
                        break;
                    }
                    case BodyPlan.SeaLion:
                    {
                        // wet fur: fine streaks along the body, darker muzzle
                        float fur = N(new Vector3(t.q.x * 18f, t.q.y * 160f, t.q.z * 160f), seed + 3);
                        c = Mul(c, 0.9f + 0.2f * fur);
                        c = Mix(c, Mul(A, 0.7f), (1f - Sm(0.0f, 0.05f, t.along)) * 0.6f);
                        h += (fur - 0.5f) * 0.25f;
                        break;
                    }
                    case BodyPlan.Sunfish:
                    {
                        float sp = Sm(0.62f, 0.7f, Fbm(t.q * 14f, 2, seed + 11));
                        c = Mix(c, C, sp * 0.45f);
                        h += (Fbm(t.q * 50f, 2, seed + 4) - 0.5f) * 0.5f;
                        break;
                    }
                    case BodyPlan.Shark:
                        // dusky dorsal tone and a clean white belly line
                        c = Mix(c, Mul(A, 0.85f), Sm(0.7f, 1f, t.elev) * 0.5f);
                        break;
                }
                return c;
            }

            Color VerticalBands(ref Texel t, float n1, float n2, ref float h)
            {
                float a = t.along, e = t.elev;
                if (variant == "lionfish")
                {
                    // many thin red / cream zebra stripes, slanted and slightly wavy
                    float w = (n1 - 0.5f) * 0.03f;
                    float x = a * 17f + (e - 0.5f) * 1.6f * (0.6f + a) + w * 17f;
                    float st = 0.5f + 0.5f * CreatureMeshMath.Cos(x * TwoPi);
                    Color c = Mix(B, A, Sm(0.35f, 0.62f, st));
                    float thin = 0.5f + 0.5f * CreatureMeshMath.Cos((x + 0.5f) * TwoPi * 2f);
                    c = Mix(c, Mul(C, 0.9f), Sm(0.93f, 1f, thin) * 0.35f);
                    c = Mix(c, Mix(B, Color.white, 0.4f), Sm(0.3f, 0.0f, e) * 0.5f);
                    return c;
                }
                if (variant == "barracuda")
                {
                    Color c = Mix(Mix(A, Color.white, 0.55f), A, Sm(0.2f, 0.55f, e));
                    c = Mix(c, Mul(C, 0.75f), Sm(0.72f, 1f, e) * 0.7f);
                    // dark oblique bars on the upper flank
                    float x = a * 19f - e * 0.8f;
                    float bar = 0.5f + 0.5f * CreatureMeshMath.Cos(x * TwoPi);
                    float upper = Sm(0.45f, 0.62f, e) * Sm(0.12f, 0.2f, a) * (1f - Sm(0.88f, 0.95f, a));
                    c = Mix(c, Mix(B, A, 0.25f), Sm(0.72f, 0.9f, bar) * upper * 0.6f);
                    // scattered black blotches toward the tail
                    float bl = CreatureMeshNoise.Worley(t.q * 22f, seed, out _, out float cr);
                    c = Mix(c, new Color(0.08f, 0.09f, 0.1f), (1f - Sm(0.18f, 0.28f, bl)) * Sm(0.55f, 0.75f, a) * Sm(0.2f, 0.4f, e) * (cr > 0.55f ? 1f : 0f));
                    // silvery shimmer
                    c = Mix(c, Color.white, Sm(0.6f, 0.95f, N(t.q * 50f, seed + 2)) * 0.15f);
                    return c;
                }
                // clownfish: three white bands with black edges, the middle one bulging forward
                {
                    Color c = Mix(A, Mix(A, new Color(1f, 0.85f, 0.4f), 0.25f), (1f - e) * 0.4f);
                    float[] centers = { 0.215f, 0.5f, 0.86f };
                    float[] widths = { 0.05f, 0.065f, 0.032f };
                    float black = 0f, white = 0f;
                    for (int i = 0; i < 3; i++)
                    {
                        float cc = centers[i];
                        if (i == 1) cc -= 0.05f * CreatureMeshMath.Sin(Pi * CreatureMeshMath.Clamp01((e - 0.1f) / 0.8f));
                        if (i == 0) cc += 0.025f * CreatureMeshMath.Sin(Pi * e) - 0.01f;
                        float d = CreatureMeshMath.Abs(a - cc) + (N(t.q * 25f, seed + i) - 0.5f) * 0.012f;
                        float wd = widths[i];
                        white = Math.Max(white, 1f - Sm(wd - 0.004f, wd + 0.002f, d));
                        black = Math.Max(black, 1f - Sm(wd + 0.008f, wd + 0.014f, d));
                    }
                    c = Mix(c, Mul(C, 1f), black);
                    c = Mix(c, Mix(B, new Color(0.95f, 0.97f, 1f), 0.5f), white);
                    return c;
                }
            }

            Color HorizontalStripes(ref Texel t, float n1, float n2, ref float h)
            {
                float a = t.along, e = t.elev;
                if (tang)
                {
                    // royal blue with the black "palette": band from the eye to the tail plus a loop under the dorsal
                    Color c = Mix(Mix(A, new Color(0.55f, 0.7f, 1f), 0.25f), A, Sm(0.2f, 0.5f, e));
                    float center = CreatureMeshMath.Lerp(0.66f, 0.53f, CreatureMeshMath.Clamp01((a - 0.12f) / 0.8f));
                    float band = 1f - Sm(0.07f, 0.1f, CreatureMeshMath.Abs(e - center)) ;
                    band *= Sm(0.1f, 0.16f, a) * (1f - Sm(0.86f, 0.92f, a));
                    // loop: ellipse around a blue window below the dorsal fin
                    float ex = (a - 0.5f) / 0.2f, ey = (e - 0.8f) / 0.075f;
                    float rr = Sqrt(ex * ex + ey * ey);
                    float ring = (1f - Sm(1.0f, 1.25f, rr)) * Sm(0.62f, 0.85f, rr);
                    float topEdge = Sm(0.88f, 0.94f, e) * Sm(0.25f, 0.3f, a) * (1f - Sm(0.78f, 0.84f, a));
                    float dark = Math.Max(band, Math.Max(ring, topEdge));
                    c = Mix(c, B, dark);
                    // yellow tail base
                    c = Mix(c, C, Sm(0.9f, 0.95f, a) * Sm(0.1f, 0.25f, e) * (1f - Sm(0.8f, 0.95f, e)));
                    return c;
                }
                else
                {
                    // angelfish: blue with dense yellow stripes, dark mask over the eye, black bar behind the head
                    Color c = A;
                    // thin disc body: height grows ~linearly with the arc from the belly (R = perimeter / 2 pi)
                    float R = ctx.SkinRadius != null ? ctx.SkinRadius(a) : 0.2f;
                    float yPhys = (2f * e - 1f) * R * 1.5f;
                    float x = yPhys / 0.042f + a * 3.2f + (n1 - 0.5f) * 0.5f;
                    float st = 0.5f + 0.5f * CreatureMeshMath.Cos(x * TwoPi);
                    float body = Sm(0.2f, 0.25f, a);
                    c = Mix(c, B, Sm(0.45f, 0.6f, st) * body);
                    // mask
                    float eu = ctx.EyeU > 0f ? ctx.EyeU : 0.13f;
                    float mask = (1f - Sm(0.03f, 0.05f, CreatureMeshMath.Abs(a - eu))) * Sm(0.3f, 0.42f, e) * (1f - Sm(0.9f, 0.98f, e));
                    c = Mix(c, C, mask);
                    // black bar behind the head with pale blue rim
                    float bar = 1f - Sm(0.022f, 0.03f, CreatureMeshMath.Abs(a - 0.215f));
                    float rim = (1f - Sm(0.034f, 0.042f, CreatureMeshMath.Abs(a - 0.215f))) - bar;
                    float barMask = Sm(0.2f, 0.3f, e) * (1f - Sm(0.82f, 0.9f, e));
                    c = Mix(c, new Color(0.55f, 0.75f, 1f), Math.Max(0f, rim) * barMask);
                    c = Mix(c, new Color(0.04f, 0.05f, 0.08f), bar * barMask);
                    // pale snout
                    c = Mix(c, new Color(0.75f, 0.85f, 0.95f), 1f - Sm(0.03f, 0.07f, a));
                    return c;
                }
            }

            Color Spots(ref Texel t, float n1, float n2, ref float h)
            {
                float a = t.along, e = t.elev;
                if (plan == BodyPlan.Shark && variant == "whale")
                {
                    Color belly = Mix(B, new Color(0.85f, 0.88f, 0.9f), 0.3f);
                    Color c = Mix(belly, A, Sm(0.22f, 0.42f, e + (n1 - 0.5f) * 0.08f));
                    // checkerboard of pale lines (front half) + white spots
                    float lineA = 1f - Sm(0.004f, 0.008f, CreatureMeshMath.Abs(CreatureMeshMath.Frac(t.q.x / 0.045f) - 0.5f) * 0.045f);
                    float lineB = 1f - Sm(0.003f, 0.006f, CreatureMeshMath.Abs(CreatureMeshMath.Frac(t.arc / 0.05f) - 0.5f) * 0.05f);
                    float lines = Math.Max(lineA * 0.9f, lineB * 0.7f) * Sm(0.35f, 0.5f, e) * Sm(0.1f, 0.2f, a) * (1f - Sm(0.55f, 0.7f, a));
                    c = Mix(c, Mix(B, A, 0.25f), lines * 0.85f);
                    float wsize = a < 0.15f ? 0.016f : 0.026f;
                    float f1 = CreatureMeshNoise.Worley(t.q / wsize, seed, out _, out float cr);
                    float rad = 0.2f + 0.12f * cr;
                    float spot = 1f - Sm(rad - 0.04f, rad + 0.03f, f1);
                    c = Mix(c, Mix(B, Color.white, 0.2f), spot * Sm(0.3f, 0.42f, e) * 0.95f);
                    return c;
                }
                if (plan == BodyPlan.Shark)
                {
                    // leopard shark: dark saddles across the back + dark oval spots on the flanks, pale belly
                    Color c = Mix(C, A, Sm(0.25f, 0.45f, e));
                    float wob = (Fbm(t.q * 30f, 2, seed + 4) - 0.5f) * 0.5f;
                    float ph = CreatureMeshMath.Frac((a - 0.12f) / 0.085f + wob * 0.3f);
                    float saddle = (1f - Sm(0.08f, 0.14f, CreatureMeshMath.Abs(ph - 0.5f))) * Sm(0.76f, 0.86f, e + wob * 0.15f) * Sm(0.1f, 0.14f, a) * (1f - Sm(0.9f, 0.96f, a));
                    float f1 = CreatureMeshNoise.Worley(t.q / 0.026f, seed, out _, out float cr);
                    float spot = (1f - Sm(0.2f + 0.08f * cr, 0.3f + 0.08f * cr, f1)) * Sm(0.34f, 0.44f, e) * (1f - Sm(0.78f, 0.84f, e)) * (cr > 0.25f ? 1f : 0f);
                    c = Mix(c, B, Math.Max(saddle, spot) * 0.8f);
                    return c;
                }
                // fish (giant sea bass): dark body with scattered pale and dark spots
                {
                    Color c = Mix(Mix(A, B, 0.6f), A, Sm(0.2f, 0.5f, e));
                    float f1 = CreatureMeshNoise.Worley(t.q / 0.035f, seed, out _, out float cr);
                    float spot = 1f - Sm(0.18f + 0.1f * cr, 0.3f + 0.1f * cr, f1);
                    c = Mix(c, cr > 0.5f ? Mul(C, 0.85f) : Mix(A, B, 0.85f), spot * 0.7f);
                    float e2;
                    float sc = Scales(ref t, 0.03f, out e2);
                    c = Mul(c, 1f - e2 * 0.12f);
                    h += sc * 0.2f;
                    return c;
                }
            }

            Color Mottled(ref Texel t, float n1, float n2, ref float h)
            {
                float a = t.along, e = t.elev;
                Vector3 q = t.q;
                switch (plan)
                {
                    case BodyPlan.Eel:
                    {
                        // giant moray: dark leopard spots on a yellow-brown base, fine pale speckles
                        Color c = Mix(Mix(A, C, 0.35f), A, Sm(0.1f, 0.4f, e));
                        float f1 = CreatureMeshNoise.Worley(q / 0.022f, seed, out float f2, out float cr);
                        float spot = 1f - Sm(0.25f + 0.1f * cr, 0.38f + 0.1f * cr, f1);
                        c = Mix(c, B, spot * (a < 0.08f ? 0.6f : 0.9f));
                        float sp = Sm(0.72f, 0.8f, N(q * 120f, seed + 4));
                        c = Mix(c, C, sp * 0.35f);
                        h += (Fbm(new Vector3(q.x * 80f, q.y * 30f, q.z * 30f), 2, seed + 6) - 0.5f) * 0.6f;
                        return c;
                    }
                    case BodyPlan.Octopus:
                    case BodyPlan.Squid:
                    case BodyPlan.Cuttlefish:
                    {
                        Color c = Mix(Mix(A, B, 0.55f), A, Sm(0.1f, 0.5f, e));
                        float m = Fbm(q * 14f, 3, seed + 1);
                        c = Mix(c, C, Sm(0.52f, 0.66f, m) * 0.75f);
                        // chromatophores: dense small dots
                        float f1 = CreatureMeshNoise.Worley(q / 0.012f, seed + 3, out _, out float cr);
                        c = Mix(c, Mul(C, 0.85f), (1f - Sm(0.15f, 0.3f, f1)) * (cr > 0.4f ? 0.45f : 0.2f));
                        // pale spots
                        float f3 = CreatureMeshNoise.Worley(q / 0.04f, seed + 7, out _, out float cr3);
                        c = Mix(c, Mix(B, Color.white, 0.3f), (1f - Sm(0.12f, 0.2f, f3)) * (cr3 > 0.6f ? 0.85f : 0f));
                        if (plan == BodyPlan.Cuttlefish)
                        {
                            // zebra display bands on the back and a pale fin seam
                            float z = CreatureMeshMath.Frac(a * 14f + (n1 - 0.5f) * 1.2f);
                            c = Mix(c, Mul(C, 0.7f), Sm(0.55f, 0.65f, z) * (1f - Sm(0.8f, 0.9f, z)) * Sm(0.72f, 0.82f, e) * 0.7f);
                            c = Mix(c, Mix(B, Color.white, 0.5f), (1f - Sm(0.02f, 0.05f, CreatureMeshMath.Abs(e - 0.5f))) * 0.6f);
                        }
                        // papillae bumps
                        float bump = CreatureMeshNoise.Worley(q / (plan == BodyPlan.Octopus ? 0.018f : 0.03f), seed + 9, out _, out _);
                        h += (1f - Sm(0.0f, 0.5f, bump)) * (plan == BodyPlan.Octopus ? 0.5f : 0.2f);
                        return c;
                    }
                    case BodyPlan.Turtle:
                    {
                        // head / flipper scales: dark plates with pale seams; u >= 0.5 is the flipper region
                        float size = t.u >= 0.5f ? 0.03f : 0.022f;
                        float f1 = CreatureMeshNoise.Worley(q / size, seed, out float f2, out float cr);
                        float seam = 1f - Sm(0.0f, 0.12f, f2 - f1);
                        Color plate = Mix(Mix(A, C, 0.5f), A, cr);
                        Color c = Mix(plate, Mix(B, new Color(0.95f, 0.9f, 0.7f), 0.4f), seam * 0.85f);
                        c = Mix(c, Mix(B, Color.white, 0.4f), Sm(0.3f, 0.0f, e) * 0.5f);
                        h += (1f - seam) * 0.5f + cr * 0.1f;
                        return c;
                    }
                    case BodyPlan.Seahorse:
                    {
                        Color c = Mix(A, B, Sm(0.45f, 0.62f, Fbm(q * 18f, 3, seed)));
                        c = Mix(c, C, Sm(0.6f, 0.7f, Fbm(q * 30f, 2, seed + 2)) * 0.6f);
                        float f1 = CreatureMeshNoise.Worley(q / 0.012f, seed + 5, out _, out float cr);
                        c = Mix(c, new Color(1f, 0.97f, 0.9f), (1f - Sm(0.1f, 0.2f, f1)) * (cr > 0.7f ? 0.8f : 0f));
                        return c;
                    }
                    default:
                    {
                        // grouper: irregular dark blotches arranged in vague bars + pale spots, scales
                        Color c = Mix(Mix(A, B, 0.55f), A, Sm(0.15f, 0.45f, e));
                        float bars = 0.5f + 0.5f * CreatureMeshMath.Cos((a * 7f + (n1 - 0.5f) * 1.5f) * TwoPi);
                        float m = Fbm(q * 11f, 3, seed + 1) + bars * 0.15f;
                        c = Mix(c, C, Sm(0.55f, 0.66f, m) * 0.8f * Sm(0.15f, 0.35f, e));
                        float f1 = CreatureMeshNoise.Worley(q / 0.028f, seed + 7, out _, out float cr);
                        c = Mix(c, Mix(B, Color.white, 0.2f), (1f - Sm(0.12f, 0.22f, f1)) * (cr > 0.55f ? 0.6f : 0f));
                        float e2;
                        float sc = Scales(ref t, 0.026f, out e2);
                        c = Mul(c, 1f - e2 * 0.12f);
                        h += sc * 0.2f;
                        return c;
                    }
                }
            }

            Color Disc(ref Texel t, float n1, float n2, ref float h)
            {
                float x = t.q.x, z = t.q.z;
                float a = t.along;
                // chord-relative coordinate (0 leading edge .. 1 trailing edge) and rim proximity
                float axo = Math.Min(0.499f, CreatureMeshMath.Abs(x));
                float zl = ctx.DiscLeadZ != null ? ctx.DiscLeadZ(axo) : ctx.DiscFront;
                float zt = ctx.DiscTrailZ != null ? ctx.DiscTrailZ(axo) : ctx.DiscFront - ctx.DiscLength;
                float sc = zl - zt > 1e-4f ? CreatureMeshMath.Clamp01((zl - z) / (zl - zt)) : 0.5f;
                float rim = Math.Max(1f - Sm(0f, 0.12f, Math.Min(sc, 1f - sc)), Sm(0.42f, 0.5f, axo));
                if (!t.top)
                {
                    // belly: white with dusky margins, mouth and gill slits
                    Color white = Mix(B, Color.white, 0.6f);
                    if (variant != "manta") white = Mix(new Color(0.93f, 0.9f, 0.86f), Mix(C, Color.white, 0.5f), 0.3f);
                    Color c = white;
                    float edge = variant == "manta" ? Math.Max(Sm(0.75f, 1f, sc) * Sm(0.12f, 0.3f, axo), Sm(0.36f, 0.48f, axo)) * 0.75f : rim * 0.3f;
                    c = Mix(c, Mul(A, 0.9f), edge);
                    if (variant == "manta")
                    {
                        // individual dark belly spots
                        float f1 = CreatureMeshNoise.Worley(t.q / 0.05f, seed + 11, out _, out float cr);
                        c = Mix(c, Mul(C, 0.9f), (1f - Sm(0.15f, 0.25f, f1)) * (cr > 0.7f ? 1f : 0f) * (1f - Sm(0.2f, 0.4f, CreatureMeshMath.Abs(t.side))));
                    }
                    // gill slits: 5 pairs
                    for (int i = 0; i < 5; i++)
                    {
                        float gz = 0.2f + i * 0.045f;
                        float d = CreatureMeshMath.Abs(a - gz);
                        float gx = CreatureMeshMath.Abs(CreatureMeshMath.Abs(x) - 0.07f * (variant == "manta" ? 1.2f : 1f));
                        float slit = (1f - Sm(0.004f, 0.008f, d)) * (1f - Sm(0.02f, 0.03f, gx));
                        c = Mix(c, Mul(c, 0.55f), slit);
                        h -= slit * 0.4f;
                    }
                    float mouth = (1f - Sm(0.004f, 0.008f, CreatureMeshMath.Abs(a - 0.1f))) * (1f - Sm(0.05f, 0.06f, CreatureMeshMath.Abs(x)));
                    c = Mix(c, new Color(0.3f, 0.2f, 0.2f), mouth * 0.7f);
                    return c;
                }
                if (variant == "manta")
                {
                    // black back, pale shoulder chevrons, lighter leading edge
                    Color c = A;
                    float ax = CreatureMeshMath.Abs(x);
                    // pale shoulder patches: triangles behind the head, edges broken up by noise
                    float wob = (Fbm(t.q * 9f, 2, seed + 3) - 0.5f) * 0.1f;
                    float inFront = Sm(0.1f, 0.16f, sc + wob);                                  // behind the leading edge band
                    float inRear = 1f - Sm(0f, 0.06f, sc - (0.62f - 1.5f * (ax - 0.07f)) + wob);  // diagonal rear boundary
                    float inSide = Sm(0.07f, 0.11f, ax) * (1f - Sm(0.3f, 0.35f, ax + wob));
                    float chevron = inFront * inRear * inSide;
                    c = Mix(c, Mix(B, A, 0.28f), chevron * 0.85f);
                    c = Mix(c, Mul(C, 1.25f), (1f - Sm(0f, 0.1f, sc)) * 0.35f);
                    c = Mix(c, Mul(A, 1.4f), n1 * 0.2f);
                    return c;
                }
                // stingray top: warm brown with bright blue spots, sandy rim
                {
                    Color c = Mix(Mul(A, 1.05f), Mix(A, C, 0.6f), rim * 0.45f);
                    c = Mix(c, Mul(A, 0.8f), Sm(0.55f, 0.7f, n1) * 0.4f);
                    // darker raised midline over the body
                    c = Mix(c, Mul(A, 0.82f), CreatureMeshMath.Gauss(x, 0f, 0.07f) * Sm(0.15f, 0.3f, a) * 0.35f);
                    float f1 = CreatureMeshNoise.Worley(t.q / 0.045f, seed, out _, out float cr);
                    float rad = 0.16f + 0.16f * cr;
                    float spot = 1f - Sm(rad - 0.05f, rad + 0.02f, f1);
                    Color blue = Mix(B, new Color(0.55f, 0.85f, 1f), 0.25f);
                    c = Mix(c, blue, spot * 0.95f);
                    float ring = (1f - Sm(rad + 0.02f, rad + 0.06f, f1)) - spot;
                    c = Mix(c, Mul(A, 0.65f), Math.Max(0f, ring) * 0.5f);
                    h += spot * 0.15f;
                    return c;
                }
            }

            // ------------------------------------------------------------------ overlays

            Color Overlays(ref Texel t, Color c, float n1, float n2, ref float h)
            {
                float a = t.along, e = t.elev;
                switch (plan)
                {
                    case BodyPlan.Fish:
                    case BodyPlan.Anglerfish:
                    case BodyPlan.Sunfish:
                    {
                        if (plan == BodyPlan.Fish && s.pattern != PatternType.Mottled && s.pattern != PatternType.Spots)
                        {
                            float e2;
                            float sc = Scales(ref t, variant == "barracuda" ? 0.018f : 0.034f, out e2);
                            c = Mul(c, 1f - e2 * 0.1f);
                            h += sc * 0.22f;
                        }
                        if (ctx.GillU > 0f)
                        {
                            // gill cover: arc convex toward the tail
                            float g = ctx.GillU + 0.035f * CreatureMeshMath.Sin(Pi * CreatureMeshMath.Clamp01((e - 0.05f) / 0.85f)) - 0.02f;
                            float d = a - g;
                            float line = (1f - Sm(0.002f, 0.006f, CreatureMeshMath.Abs(d))) * Sm(0.08f, 0.2f, e) * (1f - Sm(0.82f, 0.92f, e));
                            c = Mix(c, Mul(c, 0.62f), line * 0.8f);
                            h -= line * 0.5f;
                            h += (1f - Sm(-0.01f, 0.004f, d)) * 0.08f;
                        }
                        if (plan == BodyPlan.Fish)
                        {
                            // lateral line
                            float ll = CreatureMeshMath.Lerp(0.72f, 0.58f, CreatureMeshMath.Clamp01((a - 0.2f) / 0.7f));
                            float l = (1f - Sm(0.004f, 0.01f, CreatureMeshMath.Abs(e - ll))) * Sm(0.2f, 0.26f, a) * (1f - Sm(0.86f, 0.92f, a));
                            c = Mix(c, Mul(c, 1.18f), l * 0.35f);
                        }
                        break;
                    }
                    case BodyPlan.Shark:
                    {
                        if (ctx.GillU > 0f)
                        {
                            for (int i = 0; i < 5; i++)
                            {
                                float gx = ctx.GillU + i * 0.017f * (variant == "whale" ? 1.4f : 1f);
                                float d = CreatureMeshMath.Abs(a - gx - (e - 0.45f) * 0.012f);
                                float span = Sm(0.26f, 0.32f, e) * (1f - Sm(0.58f + i * 0.005f, 0.64f, e));
                                float slit = (1f - Sm(0.0015f, 0.004f, d)) * span;
                                c = Mix(c, Mul(c, 0.45f), slit * 0.9f);
                                h -= slit * 0.6f;
                            }
                        }
                        // dermal denticles: very fine grain
                        h += (N(t.q * 260f, seed + 13) - 0.5f) * 0.12f;
                        break;
                    }
                    case BodyPlan.SeaLion:
                    {
                        // mouth line and dark nose tip
                        float ml = (1f - Sm(0.0015f, 0.004f, CreatureMeshMath.Abs(e - 0.36f + a * 0.6f))) * (1f - Sm(0.045f, 0.06f, a));
                        c = Mix(c, Mul(A, 0.35f), ml * 0.8f);
                        c = Mix(c, Mul(C, 0.6f), (1f - Sm(0.008f, 0.02f, a)) * 0.8f);
                        break;
                    }
                }
                return c;
            }

            static float Sqrt(float x) => CreatureMeshMath.Sqrt(x);
        }

        // ============================================================================================ fins

        /// Fin atlas: 4 bands (0 median, 1 caudal, 2 paired, 3 special/arms); u = base->edge, v = along the base.
        public static CreatureMeshImage Fins(CreatureMeshContext ctx, int w, int h)
        {
            var img = new CreatureMeshImage(w, h);
            var s = ctx.S;
            int seed = SeedOf(s.id) + 101;
            Color A = s.colorA, B = s.colorB, C = s.colorC;
            A.a = B.a = C.a = 1f;
            string variant = s.variant ?? "";
            bool tang = Luma(B) < Luma(A);
            CreatureMeshImage.Rows(h, y =>
            {
                int band = Math.Min(3, y * 4 / h);
                float sv = (y - band * h / 4f + 0.5f) / (h / 4f);
                sv = CreatureMeshMath.Clamp01((sv - 0.05f) / 0.9f);
                for (int x = 0; x < w; x++)
                {
                    float r = CreatureMeshMath.Clamp01(((x + 0.5f) / w - 0.02f) / 0.96f);
                    Vector3 q = new Vector3(r * 0.3f, sv * 0.4f, band * 2f);
                    float n1 = Fbm(q * 12f, 3, seed);
                    float rays = 0.5f + 0.5f * CreatureMeshMath.Cos(sv * TwoPi * 11f);
                    float hh = 0.5f + Sm(0.6f, 1f, rays) * 0.25f * (1f - r);
                    Color c = FinColor(ctx, band, r, sv, n1, rays, A, B, C, variant, tang, q, seed, ref hh);
                    c.r = CreatureMeshMath.Clamp01(c.r); c.g = CreatureMeshMath.Clamp01(c.g); c.b = CreatureMeshMath.Clamp01(c.b); c.a = 1f;
                    img.C[y * w + x] = c;
                    img.Hgt[y * w + x] = hh;
                }
            });
            return img;
        }

        static Color FinColor(CreatureMeshContext ctx, int band, float r, float sv, float n1, float rays, Color A, Color B, Color C,
                              string variant, bool tang, Vector3 q, int seed, ref float hh)
        {
            var s = ctx.S;
            float rayDark = Sm(0.75f, 1f, rays) * 0.18f;
            switch (s.plan)
            {
                case BodyPlan.Fish:
                {
                    Color c;
                    if (variant == "lionfish")
                    {
                        // cream membranes, red banded rays
                        float bands = 0.5f + 0.5f * CreatureMeshMath.Cos((r * 7f + sv * 0.6f) * TwoPi);
                        c = Mix(Mix(B, Color.white, 0.2f), A, Sm(0.45f, 0.7f, bands) * Sm(0.55f, 1f, rays + 0.2f));
                        if (band == 1 || band == 2)
                        {
                            float f1 = CreatureMeshNoise.Worley(q / 0.025f, seed, out _, out float cr);
                            c = Mix(c, Mul(C, 0.9f), (1f - Sm(0.2f, 0.3f, f1)) * (cr > 0.4f ? 0.8f : 0f));
                        }
                        return Mul(c, 1f - rayDark * 0.5f);
                    }
                    if (variant == "tall")
                    {
                        if (tang)
                        {
                            if (band == 1)
                            {
                                c = C;
                                float m = Math.Min(sv, 1f - sv);
                                c = Mix(c, new Color(0.05f, 0.05f, 0.08f), (1f - Sm(0.08f, 0.14f, m)) * Sm(0.2f, 0.5f, r));
                                return Mul(c, 1f - rayDark);
                            }
                            if (band == 2) return Mul(Mix(Mix(A, C, 0.6f), C, r), 1f - rayDark);
                            c = Mix(B, A, Sm(0.1f, 0.3f, r) * 0.3f);
                            c = Mix(c, Mix(A, new Color(0.5f, 0.8f, 1f), 0.4f), (1f - Sm(0.03f, 0.06f, CreatureMeshMath.Abs(r - 0.82f))) * 0.9f);
                            c = Mix(c, new Color(0.03f, 0.03f, 0.05f), Sm(0.9f, 0.96f, r));
                            return Mul(c, 1f - rayDark);
                        }
                        if (band == 1) return Mul(Mix(B, Mul(B, 0.85f), n1), 1f - rayDark);
                        if (band == 2) return Mul(Mix(C, B, Sm(0.6f, 1f, r) * 0.6f), 1f - rayDark);
                        float st = 0.5f + 0.5f * CreatureMeshMath.Cos((r * 4f + sv * 6f) * TwoPi);
                        c = Mix(A, B, Sm(0.5f, 0.65f, st));
                        c = Mix(c, new Color(0.6f, 0.85f, 1f), Sm(0.86f, 0.95f, r));
                        return Mul(c, 1f - rayDark);
                    }
                    if (variant == "barracuda")
                    {
                        c = Mix(Mul(A, 0.75f), Mul(C, 0.6f), r);
                        if (band == 1)
                        {
                            c = Mix(Mul(C, 0.55f), Mul(C, 0.35f), r);
                            float lobe = Math.Min(sv, 1f - sv);
                            c = Mix(c, Color.white, Sm(0.82f, 0.95f, r) * (1f - Sm(0.1f, 0.2f, lobe)) * 0.8f);
                        }
                        return Mul(c, 1f - rayDark);
                    }
                    if (variant == "grouper" || s.pattern == PatternType.Mottled || s.pattern == PatternType.Spots)
                    {
                        c = Mix(Mul(A, 0.9f), Mul(C, 0.9f), Sm(0.5f, 0.65f, n1) * 0.7f);
                        float f1 = CreatureMeshNoise.Worley(q / 0.03f, seed + 2, out _, out float cr);
                        c = Mix(c, Mix(B, Color.white, 0.2f), (1f - Sm(0.12f, 0.2f, f1)) * (cr > 0.6f ? 0.5f : 0f));
                        c = Mix(c, Mix(B, Color.white, 0.3f), Sm(0.93f, 0.99f, r) * 0.6f);
                        return Mul(c, 1f - rayDark);
                    }
                    if (s.pattern == PatternType.VerticalBands)
                    {
                        // clownfish: orange fins with black margins and a thin white rim
                        c = Mix(A, Mul(A, 1.08f), r);
                        float m = band == 2 ? 0.86f : 0.72f;
                        c = Mix(c, C, Sm(m - 0.06f, m, r));
                        c = Mix(c, Color.white, Sm(0.95f, 0.99f, r) * 0.6f);
                        return Mul(c, 1f - rayDark * 0.6f);
                    }
                    if (s.pattern == PatternType.BellyGradient)
                    {
                        // parrotfish: teal fins with pink margin and a yellow streak
                        c = Mix(A, Mix(A, C, 0.5f), (1f - Sm(0.05f, 0.1f, CreatureMeshMath.Abs(r - 0.45f))) * 0.7f);
                        c = Mix(c, B, Sm(0.8f, 0.9f, r));
                        return Mul(c, 1f - rayDark);
                    }
                    // garibaldi & solid fish
                    c = Mix(A, Mul(Mix(A, C, 0.5f), 1.05f), r * 0.6f);
                    return Mul(c, 1f - rayDark);
                }
                case BodyPlan.Shark:
                {
                    Color c = Mix(A, Mul(A, 0.75f), Sm(0.55f, 1f, r) * 0.6f);
                    if (variant == "whale")
                    {
                        float f1 = CreatureMeshNoise.Worley(q / 0.02f, seed, out _, out float cr);
                        c = Mix(c, Mix(B, Color.white, 0.2f), (1f - Sm(0.2f, 0.3f, f1)) * 0.85f);
                    }
                    else if (s.pattern == PatternType.Spots)
                    {
                        float f1 = CreatureMeshNoise.Worley(q / 0.035f, seed, out _, out float cr);
                        c = Mix(c, B, (1f - Sm(0.2f, 0.3f, f1)) * (cr > 0.4f ? 0.7f : 0f));
                    }
                    if (band == 2) c = Mix(c, Mix(B, Color.white, 0.4f), Sm(0.45f, 0.55f, sv) * 0.25f);
                    hh = 0.5f + (N(q * 200f, seed) - 0.5f) * 0.1f;
                    return c;
                }
                case BodyPlan.Ray:
                {
                    if (band == 3)
                    {
                        // whip tail: body color, bright side stripes on the bluespotted ray, dark tip
                        Color c = Mul(A, 0.95f);
                        if (variant != "manta")
                        {
                            float st = 1f - Sm(0.04f, 0.07f, CreatureMeshMath.Abs(CreatureMeshMath.Abs(sv - 0.5f) - 0.25f));
                            c = Mix(c, B, st * Sm(0.0f, 0.15f, r) * (1f - Sm(0.55f, 0.7f, r)));
                            c = Mix(c, Mul(A, 0.4f), Sm(0.65f, 0.85f, r));
                        }
                        return c;
                    }
                    return Mix(A, Mul(A, 0.8f), r);
                }
                case BodyPlan.Eel:
                {
                    Color c = Mix(Mix(A, C, 0.3f), Mul(B, 1.1f), Sm(0.55f, 0.7f, n1) * 0.7f);
                    c = Mix(c, Color.white, Sm(0.9f, 1f, r) * 0.3f);
                    return c;
                }
                case BodyPlan.Turtle:
                    return Mix(A, B, n1 * 0.5f);
                case BodyPlan.SeaLion:
                {
                    // leathery dark flippers with fine wrinkles
                    Color c = Mix(Mul(C, 0.9f), Mul(C, 1.25f), n1);
                    float wr = N(new Vector3(q.x * 120f, q.y * 25f, q.z), seed + 3);
                    hh = 0.5f + (wr - 0.5f) * 0.6f;
                    return Mul(c, 0.9f + 0.15f * wr);
                }
                case BodyPlan.Sunfish:
                {
                    Color c = Mix(A, Mul(C, 0.95f), Sm(0.4f, 1f, r) * 0.6f);
                    c = Mix(c, Mix(B, A, 0.4f), Sm(0.6f, 0.7f, n1) * 0.3f);
                    return c;
                }
                case BodyPlan.Anglerfish:
                    return Mix(Mul(A, 1.2f), Mul(C, 1.1f), r);
                case BodyPlan.Seahorse:
                {
                    Color c = Mix(Mix(A, Color.white, 0.35f), Mix(A, Color.white, 0.65f), r);
                    return Mul(c, 1f - rayDark * 1.4f);
                }
                case BodyPlan.Octopus:
                case BodyPlan.Squid:
                case BodyPlan.Cuttlefish:
                case BodyPlan.VampireSquid:
                {
                    if (band == 3)
                    {
                        // arms: u along the arm, v around (0/1 = oral side with suckers)
                        float around = sv;
                        Color c = Mix(Mix(A, B, 0.45f), A, Sm(0.1f, 0.4f, 1f - CreatureMeshMath.Abs(1f - 2f * around)));
                        if (s.plan == BodyPlan.VampireSquid) c = Mix(A, B, 0.4f + 0.3f * n1);
                        else c = Mix(c, C, Sm(0.55f, 0.66f, n1) * 0.6f);
                        float oral = 1f - Sm(0.15f, 0.22f, Math.Min(around, 1f - around));
                        if (s.plan != BodyPlan.VampireSquid)
                        {
                            // two staggered rows of suckers along the oral side
                            float size = CreatureMeshMath.Lerp(0.045f, 0.015f, r);
                            float row = Math.Min(around, 1f - around) < 0.085f ? 0f : 0.5f;
                            float cell = CreatureMeshMath.Frac(r / size + row);
                            float ac = (Math.Min(around, 1f - around) - (row > 0f ? 0.13f : 0.045f)) / 0.04f;
                            float d = Sqrt((cell - 0.5f) * (cell - 0.5f) * 4f + ac * ac);
                            float sucker = (1f - Sm(0.6f, 0.8f, d)) * oral;
                            Color sc = Mix(B, new Color(1f, 0.92f, 0.88f), 0.5f);
                            c = Mix(c, sc, sucker * 0.9f);
                            c = Mix(c, Mul(sc, 0.75f), (1f - Sm(0.15f, 0.3f, d)) * oral * 0.7f);
                            hh = 0.5f + sucker * 0.5f - (1f - Sm(0.15f, 0.3f, d)) * oral * 0.3f;
                        }
                        else
                        {
                            // pale fleshy cirri side, dark arm tips
                            c = Mix(c, Mul(B, 0.6f), Sm(0.75f, 0.95f, r) * 0.7f);
                        }
                        return c;
                    }
                    // fins / web
                    if (s.plan == BodyPlan.Cuttlefish)
                    {
                        Color c = Mix(Mix(A, B, 0.3f), Mix(A, B, 0.65f), r);
                        c = Mix(c, Mul(C, 1.1f), Sm(0.55f, 0.7f, n1) * 0.4f);
                        c = Mix(c, Mix(Color.white, B, 0.4f), Sm(0.86f, 0.96f, r) * 0.75f);
                        return c;
                    }
                    if (s.plan == BodyPlan.VampireSquid) return Mix(A, B, 0.3f + 0.4f * r + 0.2f * n1);
                    return Mix(Mix(A, B, 0.3f), Mul(C, 1.1f), Sm(0.5f, 0.66f, n1) * 0.6f);
                }
                default:
                    return Mix(A, B, r * 0.3f);
            }
        }

        static float Sqrt(float x) => CreatureMeshMath.Sqrt(x);
        static float N(Vector3 p, int seed, float scale) => CreatureMeshNoise.Perlin(p * scale, seed);

        // ============================================================================================ turtle shell

        /// Carapace (v >= 0.52, top-down: u across, v front->rear) and plastron (v <= 0.48).
        public static CreatureMeshImage Shell(CreatureMeshContext ctx, int w, int h)
        {
            var img = new CreatureMeshImage(w, h);
            var s = ctx.S;
            int seed = SeedOf(s.id) + 211;
            Color A = s.colorA, B = s.colorB, C = s.colorC;
            A.a = B.a = C.a = 1f;
            CreatureMeshImage.Rows(h, y =>
            {
                float v = (y + 0.5f) / h;
                bool top = v >= 0.5f;
                float along = top ? CreatureMeshMath.Clamp01((v - 0.52f) / 0.48f) : CreatureMeshMath.Clamp01((0.48f - v) / 0.48f);
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w;
                    float px = (u - 0.5f) * 2f;          // -1..1 across
                    float pz = along * 2f - 1f;          // -1 front .. 1 rear
                    Vector3 q = new Vector3(px * 0.3f, top ? 0f : 4f, pz * 0.4f);
                    float n1 = Fbm(q * 10f, 3, seed);
                    float hh = 0.5f;
                    Color c;
                    if (top) c = Carapace(px, pz, n1, q, seed, A, B, C, ref hh);
                    else
                    {
                        // plastron: pale yellow plates
                        c = Mix(new Color(0.92f, 0.86f, 0.62f), Mix(B, Color.white, 0.4f), 0.4f);
                        float sx = CreatureMeshMath.Abs(px);
                        float seamX = 1f - Sm(0.0f, 0.03f, sx);
                        float rows = CreatureMeshMath.Abs(CreatureMeshMath.Frac(pz * 2.5f + 0.5f) - 0.5f);
                        float seamZ = 1f - Sm(0.0f, 0.03f, rows);
                        float seam = Math.Max(seamX, seamZ) * (1f - Sm(0.55f, 0.7f, sx));
                        c = Mix(c, Mul(c, 0.7f), seam * 0.6f);
                        c = Mul(c, 0.92f + 0.12f * n1);
                        hh -= seam * 0.4f;
                    }
                    c.r = CreatureMeshMath.Clamp01(c.r); c.g = CreatureMeshMath.Clamp01(c.g); c.b = CreatureMeshMath.Clamp01(c.b); c.a = 1f;
                    img.C[y * w + x] = c;
                    img.Hgt[y * w + x] = hh;
                }
            });
            return img;
        }

        static Color Carapace(float px, float pz, float n1, Vector3 q, int seed, Color A, Color B, Color C, ref float hh)
        {
            // scute layout: 5 vertebral plates on the midline, 4 costal plates per side, marginal ring
            float r = Sqrt(px * px + pz * pz);
            float sx = CreatureMeshMath.Abs(px);
            float seam = 0f;
            float cx, cz;     // scute center (for radiating streaks)
            if (r > 0.86f)
            {
                // marginal scutes
                float ang = (float)Math.Atan2(pz, px);
                float k = CreatureMeshMath.Frac(ang / TwoPi * 24f);
                seam = Math.Max(1f - Sm(0f, 0.05f, CreatureMeshMath.Abs(k - 0.5f) * 2f - 0.9f), 1f - Sm(0f, 0.03f, r - 0.86f));
                seam = Math.Max(1f - Sm(0f, 0.04f, CreatureMeshMath.Abs(k) < 0.5f ? k : 1f - k), 1f - Sm(0.0f, 0.025f, r - 0.86f));
                cx = CreatureMeshMath.Cos(ang) * 0.93f; cz = CreatureMeshMath.Sin(ang) * 0.93f;
            }
            else if (sx < 0.26f)
            {
                // vertebral: 5 rows
                float f = (pz + 0.86f) / 1.72f * 5f;
                int row = (int)CreatureMeshMath.Clamp((float)Math.Floor(f), 0f, 4f);
                float fr = f - row;
                seam = Math.Max(1f - Sm(0f, 0.06f, Math.Min(fr, 1f - fr)), 1f - Sm(0f, 0.025f, 0.26f - sx));
                cx = 0f; cz = -0.86f + (row + 0.5f) * 1.72f / 5f;
            }
            else
            {
                // costal: 4 per side, slanted seams
                float f = (pz + 0.8f + (sx - 0.26f) * 0.4f) / 1.6f * 4f;
                int row = (int)CreatureMeshMath.Clamp((float)Math.Floor(f), 0f, 3f);
                float fr = f - row;
                seam = Math.Max(1f - Sm(0f, 0.05f, Math.Min(fr, 1f - fr)), 1f - Sm(0f, 0.025f, sx - 0.26f));
                cx = CreatureMeshMath.Sign(px) * 0.55f; cz = -0.8f + (row + 0.5f) * 0.4f - 0.1f;
            }
            float dx = px - cx, dz = pz - cz;
            float ang2 = (float)Math.Atan2(dz, dx);
            float streak = 0.5f + 0.5f * CreatureMeshMath.Sin(ang2 * 9f + n1 * 4f);
            float dist = Sqrt(dx * dx + dz * dz);
            Color baseC = Mix(Mix(A, C, 0.4f), A, n1);
            Color c = Mix(baseC, Mix(B, new Color(0.85f, 0.75f, 0.45f), 0.5f), Sm(0.55f, 0.95f, streak) * Sm(0.05f, 0.25f, dist) * 0.38f);
            c = Mix(c, Mul(C, 0.7f), Sm(0.6f, 0.8f, Fbm(q * 18f, 2, seed + 3)) * 0.4f);
            c = Mix(c, Mul(C, 0.55f), seam * 0.85f);
            hh = 0.5f - seam * 0.5f + (1f - Sm(0f, 0.3f, dist)) * 0.15f + (streak - 0.5f) * 0.05f;
            return c;
        }

        // ============================================================================================ jelly bell

        /// Translucent bell: u around, v apex (0) -> rim (1). Alpha carries the translucency.
        public static CreatureMeshImage Bell(CreatureMeshContext ctx, int w, int h)
        {
            var img = new CreatureMeshImage(w, h) { WrapU = true };
            var s = ctx.S;
            int seed = SeedOf(s.id) + 307;
            Color A = s.colorA, B = s.colorB, C = s.colorC;
            CreatureMeshImage.Rows(h, y =>
            {
                float v = (y + 0.5f) / h;
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w;
                    float ang = u * TwoPi;
                    Vector3 q = new Vector3(CreatureMeshMath.Cos(ang) * v, CreatureMeshMath.Sin(ang) * v, 0f);
                    float n1 = Fbm(q * 6f, 3, seed);
                    // 16 radial canals branching toward the rim, ring canal at the rim
                    float k = CreatureMeshMath.Frac(u * 16f);
                    float canal = 1f - Sm(0.0f, 0.06f, CreatureMeshMath.Abs(k - 0.5f) * 2f * (0.5f + v * 2f) - 0.0f);
                    float k2 = CreatureMeshMath.Frac(u * 64f);
                    float branch = (1f - Sm(0f, 0.12f, CreatureMeshMath.Abs(k2 - 0.5f) * 2f)) * Sm(0.55f, 0.8f, v);
                    float ring = 1f - Sm(0.0f, 0.03f, CreatureMeshMath.Abs(v - 0.93f));
                    float rimDots = (1f - Sm(0.2f, 0.45f, CreatureMeshMath.Abs(CreatureMeshMath.Frac(u * 96f) - 0.5f) * 2f)) * Sm(0.95f, 0.99f, v);
                    float lines = Math.Max(canal * Sm(0.15f, 0.35f, v) * 0.8f, Math.Max(branch * 0.5f, ring * 0.8f));
                    Color c = Mix(A, Color.white, 0.25f + 0.15f * n1);
                    c = Mix(c, Mix(C, Color.white, 0.4f), lines);
                    c = Mix(c, Mix(B, Color.white, 0.2f), rimDots * 0.8f);
                    float alpha = CreatureMeshMath.Lerp(0.16f, 0.3f, v) + lines * 0.25f + rimDots * 0.3f + n1 * 0.06f;
                    c.a = CreatureMeshMath.Clamp01(alpha);
                    img.C[y * w + x] = c;
                    img.Hgt[y * w + x] = 0.5f + lines * 0.3f + (n1 - 0.5f) * 0.2f;
                }
            });
            return img;
        }

        // ============================================================================================ eyes

        /// Eye texture: pupil at the center (uv 0.5, 0.5), texture up = eye up.
        public static CreatureMeshImage Eye(CreatureMeshEyeStyle style, int size)
        {
            var img = new CreatureMeshImage(size, size);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float px = (x + 0.5f) / size * 2f - 1f, py = (y + 0.5f) / size * 2f - 1f;
                    float r = Sqrt(px * px + py * py);
                    Color c = EyeColor(style, px, py, r, out float hh);
                    c.a = 1f;
                    img.C[y * size + x] = c;
                    img.Hgt[y * size + x] = hh;
                }
            return img;
        }

        static Color EyeColor(CreatureMeshEyeStyle style, float px, float py, float r, out float hh)
        {
            hh = 0.5f;
            Color sclera = new Color(0.94f, 0.94f, 0.9f);
            Color black = new Color(0.02f, 0.02f, 0.025f);
            // catch light (painted; the material is glossy too)
            float glint = 1f - Sm(0.05f, 0.11f, Sqrt((px + 0.22f) * (px + 0.22f) + (py - 0.24f) * (py - 0.24f)));
            float glint2 = 1f - Sm(0.02f, 0.05f, Sqrt((px - 0.18f) * (px - 0.18f) + (py + 0.2f) * (py + 0.2f)));
            float ang = (float)Math.Atan2(py, px);
            float fib = 0.5f + 0.5f * CreatureMeshMath.Sin(ang * 23f);
            Color c;
            switch (style)
            {
                case CreatureMeshEyeStyle.Friendly:
                {
                    Color iris = Mix(new Color(0.95f, 0.62f, 0.12f), new Color(0.75f, 0.42f, 0.08f), fib * 0.5f);
                    c = Mix(sclera, iris, 1f - Sm(0.6f, 0.64f, r));
                    c = Mix(c, Mul(iris, 0.6f), (1f - Sm(0.56f, 0.6f, r)) * Sm(0.5f, 0.58f, r));
                    c = Mix(c, black, 1f - Sm(0.44f, 0.47f, r));
                    break;
                }
                case CreatureMeshEyeStyle.Fish:
                {
                    Color iris = Mix(new Color(0.78f, 0.74f, 0.6f), new Color(0.95f, 0.85f, 0.45f), fib * 0.4f);
                    c = Mix(Mix(iris, sclera, 0.3f), iris, 1f - Sm(0.75f, 0.85f, r));
                    c = Mix(c, black, 1f - Sm(0.5f, 0.53f, r));
                    break;
                }
                case CreatureMeshEyeStyle.Shark:
                {
                    Color iris = Mix(new Color(0.22f, 0.26f, 0.24f), new Color(0.38f, 0.42f, 0.36f), fib * 0.5f);
                    c = Mix(new Color(0.3f, 0.3f, 0.32f), iris, 1f - Sm(0.8f, 0.9f, r));
                    c = Mix(c, black, 1f - Sm(0.55f, 0.6f, r + CreatureMeshMath.Abs(px) * 0.0f));
                    break;
                }
                case CreatureMeshEyeStyle.Squid:
                {
                    Color iris = Mix(new Color(0.75f, 0.78f, 0.82f), new Color(0.45f, 0.5f, 0.6f), fib);
                    c = Mix(new Color(0.55f, 0.35f, 0.38f), iris, 1f - Sm(0.82f, 0.9f, r));
                    c = Mix(c, black, 1f - Sm(0.6f, 0.64f, r));
                    break;
                }
                case CreatureMeshEyeStyle.Octopus:
                case CreatureMeshEyeStyle.Cuttle:
                {
                    Color iris = Mix(new Color(0.9f, 0.75f, 0.35f), new Color(0.6f, 0.55f, 0.3f), fib * 0.6f);
                    c = Mix(sclera, iris, 1f - Sm(0.82f, 0.88f, r));
                    float pupil;
                    if (style == CreatureMeshEyeStyle.Octopus)
                    {
                        // horizontal bar pupil
                        pupil = (1f - Sm(0.5f, 0.55f, CreatureMeshMath.Abs(px))) * (1f - Sm(0.12f, 0.16f, CreatureMeshMath.Abs(py)));
                    }
                    else
                    {
                        // W-shaped pupil
                        float wy = 0.12f * CreatureMeshMath.Cos(px * Pi * 2.2f) - 0.02f;
                        pupil = (1f - Sm(0.55f, 0.6f, CreatureMeshMath.Abs(px))) * (1f - Sm(0.09f, 0.13f, CreatureMeshMath.Abs(py - wy)));
                    }
                    c = Mix(c, black, pupil);
                    break;
                }
                case CreatureMeshEyeStyle.Turtle:
                case CreatureMeshEyeStyle.Mammal:
                {
                    Color iris = Mix(new Color(0.28f, 0.17f, 0.08f), new Color(0.42f, 0.28f, 0.12f), fib * 0.5f);
                    c = Mix(new Color(0.85f, 0.83f, 0.78f), iris, 1f - Sm(0.8f, 0.86f, r));
                    c = Mix(c, black, 1f - Sm(style == CreatureMeshEyeStyle.Mammal ? 0.55f : 0.42f, style == CreatureMeshEyeStyle.Mammal ? 0.6f : 0.46f, r));
                    break;
                }
                case CreatureMeshEyeStyle.Vampire:
                {
                    Color iris = Mix(new Color(0.75f, 0.12f, 0.12f), new Color(0.35f, 0.65f, 1f), Sm(0.3f, 0.9f, fib) * 0.5f);
                    c = Mix(new Color(0.2f, 0.05f, 0.08f), iris, 1f - Sm(0.82f, 0.9f, r));
                    c = Mix(c, black, 1f - Sm(0.45f, 0.5f, r));
                    break;
                }
                default: // Angler: small milky eye
                {
                    c = Mix(new Color(0.55f, 0.58f, 0.62f), new Color(0.2f, 0.22f, 0.26f), 1f - Sm(0.4f, 0.6f, r));
                    break;
                }
            }
            c = Mix(c, Color.white, Math.Max(glint, glint2 * 0.7f) * 0.95f);
            hh = 0.5f + (1f - Sm(0.0f, 0.9f, r)) * 0.2f;
            return c;
        }
    }
}
