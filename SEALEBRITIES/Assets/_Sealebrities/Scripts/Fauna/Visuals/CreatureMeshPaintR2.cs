using System;
using UnityEngine;

namespace Sealebrities
{
    /// Round-2 creature painting (G.1, K.8, L.1, L.11): crabs, starfish, dolphin, humpback, birds, bat, lizards,
    /// goat, pig, capuchin and the new fish / shark / eel / ray / seahorse / cuttlefish variants.
    /// New body plans use a "palette loft": the first PalBodyCols columns are the body loft (u along the body),
    /// the last 8 columns are solid palette entries for limbs, beaks, horns … (PalUv).
    internal static partial class CreatureMeshPaint
    {
        public const int PalW = 32, PalBodyCols = 24, PalH = 16;
        /// Max body u (centre of the last body column – safe with bilinear sampling).
        public const float PalBodyU = (PalBodyCols - 0.5f) / PalW;

        /// uv of palette entry k (0..7) in a palette-loft skin.
        public static Vector2 PalUv(int k) => new Vector2((PalBodyCols + (k < 0 ? 0 : (k > 7 ? 7 : k)) + 0.5f) / PalW, 0.5f);

        /// Palette loft: body(x, y, u01, elev, eRow) paints the body region, pal[] the 8 palette columns.
        static CreatureMeshImage PalLoft(Color[] pal, LoftFn body)
        {
            var img = new CreatureMeshImage(PalW, PalH) { WrapU = false, WrapV = true };
            for (int y = 0; y < PalH; y++)
            {
                float v = (y + 0.5f) / PalH;
                float elev = 1f - CreatureMeshMath.Abs(1f - 2f * v);
                int eRow = Math.Min(y, PalH - 1 - y);
                for (int x = 0; x < PalW; x++)
                {
                    Color c = x < PalBodyCols ? body(x, y, (x + 0.5f) / PalBodyCols, elev, eRow) : pal[Math.Min(7, x - PalBodyCols)];
                    c.a = 1f;
                    img.C[y * PalW + x] = c;
                }
            }
            return img;
        }

        static Color Hx(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);

        // ============================================================================================ skins

        static CreatureMeshImage SkinR2(CreatureMeshContext ctx, Pal P)
        {
            var s = ctx.S;
            Color A = P.A, B = P.B, C = P.C;
            switch (s.plan)
            {
                case BodyPlan.Crab: return CrabSkin(P);
                case BodyPlan.Starfish: return StarSkin(P);
                case BodyPlan.Dolphin: return DolphinSkin(P);
                case BodyPlan.Whale: return WhaleSkin(P);
                case BodyPlan.Bird: return BirdSkin(P);
                case BodyPlan.Bat: return BatSkin(P);
                case BodyPlan.Lizard: return LizardSkin(P);
                case BodyPlan.Quadruped: return P.Variant == "pig" ? PigSkin(P) : GoatSkin(P);
                case BodyPlan.Primate: return CapuchinSkin(P);
            }
            switch (P.Id)
            {
                case "cleaner_wrasse": return CleanerWrasse(P);
                case "sardine": return Sardine(P);
                case "flying_fish": return TwoTone(P, 32, 16, 0.36f, Mix(P.B, White, 0.2f), (x, y, u, e, r) => e > 0.78f ? Mul(P.A, 0.82f) : None);
                case "yellowfin": return Yellowfin(P);
                case "lanternfish": return Lanternfish(P);
                case "mandarinfish": return Mandarin(P);
                case "coelacanth": return Coelacanth(P);
                case "goblin_shark": return TwoTone(P, 48, 16, 0.4f, Mix(P.B, White, 0.1f), (x, y, u, e, r) => u < 0.12f && e > 0.4f ? Mix(P.A, P.C, 0.25f) : (e > 0.84f ? Mix(P.A, P.C, 0.18f) : None));
                case "gulper_eel": return Loft(32, 8, (x, y, u, e, r) => u > 0.9f ? P.C : (u < 0.12f ? Mix(P.A, P.B, 0.6f) : (P.R(x, y, 3) > 0.8f ? P.B : P.A)));
                case "ribbon_eel": return Loft(32, 8, (x, y, u, e, r) => u < 0.06f ? P.B : (e < 0.25f && u < 0.2f ? Mix(P.B, White, 0.3f) : (e > 0.9f ? Mix(P.A, Ink, 0.25f) : P.A)));
                case "pygmy_seahorse":
                {
                    var pal = new[] { P.C, P.B, P.A, P.A, P.A, P.A, P.A, P.A };
                    return PalLoft(pal, (x, y, u, e, r) =>
                    {
                        float n = P.R(x, y, 31);
                        return n < 0.25f ? Mix(P.A, P.C, 0.35f) : (n > 0.82f ? Mix(P.A, White, 0.3f) : P.A);
                    });
                }
                case "flamboyant_cuttle":
                    return Loft(16, 16, (x, y, u, e, r) =>
                    {
                        float n = 0.55f * P.R(x / 2, y / 2, 3) + 0.45f * P.R(x, y, 4);
                        if (e < 0.3f) return Mix(P.B, White, 0.15f);
                        if (n < 0.28f) return P.C;
                        if (n > 0.72f) return P.B;
                        return P.A;
                    });
            }
            return null;
        }

        // ---------------------------------------------------------------------------------- crabs

        static CreatureMeshImage CrabSkin(Pal P)
        {
            Color A = P.A, B = P.B, C = P.C;
            Color[] pal;
            LoftFn body;
            switch (P.Variant)
            {
                case "hermit":
                    pal = new[] { A, Mix(C, A, 0.35f), A, Mix(C, White, 0.2f), Mix(B, White, 0.12f), Mix(B, Rgb(0.63f, 0.35f, 0.23f), 0.55f), Mix(A, B, 0.4f), Mix(A, White, 0.4f) };
                    body = (x, y, u, e, r) => e < 0.3f ? Mix(A, White, 0.35f) : (P.R(x, y, 5) > 0.78f ? C : A);
                    break;
                case "flat":
                    pal = new[] { B, C, A, Mix(B, White, 0.3f), A, B, Mix(A, B, 0.5f), Mix(B, White, 0.4f) };
                    body = (x, y, u, e, r) =>
                    {
                        if (e < 0.3f) return Mix(B, White, 0.35f);
                        float n = P.R(x / 2, y / 2, 3) * 0.6f + P.R(x, y, 4) * 0.4f;
                        if (e > 0.82f && n > 0.6f) return Mix(C, A, 0.3f);
                        return n < 0.35f ? Mix(A, Ink, 0.15f) : (n > 0.8f ? B : A);
                    };
                    break;
                case "coconut":
                    pal = new[] { Mix(A, B, 0.15f), B, A, Mix(B, Ink, 0.2f), Mix(A, Rgb(0.45f, 0.35f, 0.62f), 0.5f), B, Mix(A, C, 0.3f), Mix(B, White, 0.35f) };
                    body = (x, y, u, e, r) => e < 0.3f ? Mix(B, White, 0.25f) : (P.R(x, y, 6) > 0.82f ? Mix(A, B, 0.45f) : (e > 0.8f ? Mul(A, 1.12f) : A));
                    break;
                default:   // ghost crab: sand coloured, slightly darker rim, pale claws
                    pal = new[] { Mix(A, White, 0.12f), Mix(A, C, 0.22f), Mix(B, A, 0.25f), Mix(A, C, 0.45f), A, B, Mix(A, C, 0.15f), B };
                    body = (x, y, u, e, r) =>
                    {
                        if (e < 0.3f) return Mix(B, A, 0.35f);
                        if (e > 0.82f && P.R(x, y, 7) > 0.7f) return Mix(A, White, 0.2f);
                        return P.R(x, y, 8) > 0.86f ? Mix(A, C, 0.18f) : A;
                    };
                    break;
            }
            return PalLoft(pal, body);
        }

        // ---------------------------------------------------------------------------------- starfish

        /// Starfish layout (Custom, 16 × 16): rows 0–7 underside, rows 8–15 top (v = radius), x = angle; column 15 = spikes.
        public const float StarSpikeU = 15.5f / 16f;

        static CreatureMeshImage StarSkin(Pal P)
        {
            var img = new CreatureMeshImage(16, 16);
            bool thorns = P.Variant == "thorns";
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    Color c;
                    if (x == 15) c = thorns ? P.C : Mix(P.B, White, 0.3f);
                    else if (y < 8) c = Mix(P.A, White, 0.45f);   // underside
                    else
                    {
                        float rad = (y - 8 + 0.5f) / 8f;
                        float n = P.R(x, y, 11);
                        if (thorns) c = n < 0.3f ? P.B : (n > 0.85f ? Mix(P.A, P.C, 0.3f) : P.A);
                        else
                        {
                            c = rad > 0.82f ? Mix(P.A, P.B, 0.5f) : P.A;
                            if (n > 0.8f) c = Mix(P.B, White, 0.2f);
                            else if (n < 0.12f) c = Mix(P.A, P.C, 0.5f);
                        }
                    }
                    c.a = 1f;
                    img.C[y * 16 + x] = c;
                }
            return img;
        }

        public static Vector2 StarUv(float angle01, float radius01, bool top)
        {
            float u = (0.02f + 0.9f * CreatureMeshMath.Clamp01(angle01)) * (15f / 16f);
            float v = top ? 0.5f + 0.48f * CreatureMeshMath.Clamp01(radius01) + 0.01f : 0.48f - 0.47f * CreatureMeshMath.Clamp01(radius01);
            return new Vector2(u, v);
        }

        // ---------------------------------------------------------------------------------- marine mammals

        static CreatureMeshImage DolphinSkin(Pal P)
        {
            Color back = P.C, flank = P.A, belly = Mix(P.B, White, 0.15f);
            return Loft(32, 16, (x, y, u, e, r) =>
            {
                float cape = 0.66f + 0.12f * CreatureMeshMath.Sin(u * 3.1f);   // the darker cape dips toward the dorsal fin
                if (e < 0.3f) return belly;
                if (e < 0.38f) return Mix(flank, belly, 0.5f);
                if (e > cape) return back;
                if (u < 0.06f && e > 0.45f) return Mix(flank, back, 0.4f);    // beak top
                return flank;
            });
        }

        static CreatureMeshImage WhaleSkin(Pal P)
        {
            Color back = P.A, belly = Mix(P.B, White, 0.05f), dark = P.C, groove = Mix(P.A, P.B, 0.45f);
            return Loft(32, 32, (x, y, u, e, r) =>
            {
                float n = P.R(x, y, 13);
                if (e < 0.34f)
                {
                    // ventral pleats from the chin to the navel (2-row stripes stay crisp with bilinear filtering)
                    if (u > 0.04f && u < 0.48f) return (r / 2) % 2 == 0 ? belly : groove;
                    return n < 0.2f ? Mix(belly, back, 0.3f) : belly;
                }
                if (e < 0.42f) return Mix(back, belly, 0.45f);
                if (e > 0.8f) return Mix(back, dark, 0.35f);
                if (n > 0.95f) return Mix(back, belly, 0.35f);                         // a few pale scars
                return back;
            });
        }

        // ---------------------------------------------------------------------------------- birds and bats

        static CreatureMeshImage BirdSkin(Pal P)
        {
            Color A = P.A, B = P.B, C = P.C;
            Color[] pal;
            LoftFn body;
            switch (P.Variant)
            {
                case "frigate":
                    pal = new[] { Rgb(0.56f, 0.58f, 0.61f), Mix(Ink, A, 0.3f), C, Ink, A, B, Mix(A, C, 0.35f), A };
                    body = (x, y, u, e, r) => u > 0.18f && u < 0.36f && e < 0.36f ? B : (e > 0.7f ? Mix(A, C, 0.28f) : A);
                    break;
                case "tropic":
                    pal = new[] { C, Mix(C, Ink, 0.4f), Rgb(0.23f, 0.23f, 0.31f), B, A, B, Mix(A, B, 0.12f), A };
                    body = (x, y, u, e, r) => u < 0.14f && e > 0.5f && e < 0.66f ? B : (e > 0.84f && u > 0.4f && x % 3 == 0 ? Mix(A, B, 0.6f) : A);
                    break;
                default:   // seagull: white head and belly, grey mantle, yellow beak with a red dot
                    pal = new[] { C, Hx(0xD9455A), Hx(0xE9B07A), Ink, A, Hx(0xF2C94C), B, A };
                    body = (x, y, u, e, r) => e > 0.7f && u > 0.36f ? B : A;
                    break;
            }
            return PalLoft(pal, body);
        }

        static CreatureMeshImage BatSkin(Pal P)
        {
            Color A = P.A, B = P.B, C = P.C;
            var pal = new[] { Ink, Mix(A, Rgb(0.85f, 0.55f, 0.5f), 0.45f), Mix(C, A, 0.3f), C, B, Mix(A, B, 0.5f), A, Mix(A, B, 0.35f) };
            return PalLoft(pal, (x, y, u, e, r) => u < 0.45f && u > 0.15f ? B : (u < 0.15f ? Mix(A, B, 0.3f) : (e < 0.3f ? Mix(A, B, 0.3f) : A)));
        }

        // ---------------------------------------------------------------------------------- land animals

        static CreatureMeshImage LizardSkin(Pal P)
        {
            Color A = P.A, B = P.B, C = P.C;
            if (P.Variant == "gecko")
            {
                var palG = new[] { Mix(A, White, 0.3f), Ink, A, Mix(B, A, 0.35f), C, B, Hx(0x3FA9B5), Mix(A, Ink, 0.2f) };
                return PalLoft(palG, (x, y, u, e, r) =>
                {
                    if (e < 0.3f) return Mix(B, A, 0.4f);
                    float n = P.R(x, y, 15);
                    if (u > 0.08f && u < 0.6f && e > 0.62f && n > 0.68f) return C;          // red dorsal spots
                    if (n < 0.1f) return Mix(A, B, 0.6f);                                 // gold dust
                    if (u < 0.08f && e > 0.6f) return Mix(A, Hx(0x3FA9B5), 0.35f);       // blue eye ring area
                    return A;
                });
            }
            var pal = new[] { Mix(C, White, 0.25f), Ink, A, Mix(C, A, 0.4f), B, C, Hx(0x3B2A2E), Mix(A, Ink, 0.3f) };
            return PalLoft(pal, (x, y, u, e, r) =>
            {
                float n = 0.55f * P.R(x / 2, y / 2, 3) + 0.45f * P.R(x, y, 4);
                if (e < 0.28f) return Mix(A, C, 0.45f);
                if (n < 0.25f) return B;                       // rusty red patches (breeding colours)
                if (n > 0.8f) return C;
                return A;
            });
        }

        static CreatureMeshImage GoatSkin(Pal P)
        {
            Color A = P.A, B = P.B, C = P.C;
            var pal = new[] { Mix(B, Rgb(0.6f, 0.58f, 0.55f), 0.5f), C, A, B, Hx(0xE8A9A0), Mix(C, B, 0.3f), B, A };
            // white goat with a brown head and two soft brown saddle patches (bilinear: round soft borders)
            return PalLoft(pal, (x, y, u, e, r) =>
            {
                if (u < 0.12f) return B;
                float p1 = (u - 0.62f) * (u - 0.62f) / 0.012f + (e - 0.82f) * (e - 0.82f) / 0.05f;
                float p2 = (u - 0.32f) * (u - 0.32f) / 0.006f + (e - 0.55f) * (e - 0.55f) / 0.03f;
                return p1 < 1f || p2 < 1f ? B : A;
            });
        }

        static CreatureMeshImage PigSkin(Pal P)
        {
            Color A = P.A, B = P.B, C = P.C;
            var pal = new[] { B, Mix(C, Ink, 0.4f), A, C, Mix(B, Hx(0xD9455A), 0.25f), C, A, B };
            return PalLoft(pal, (x, y, u, e, r) =>
            {
                if (e < 0.3f) return Mix(A, B, 0.5f);
                float spot = (u - 0.7f) * (u - 0.7f) / 0.008f + (e - 0.78f) * (e - 0.78f) / 0.02f;
                return spot < 1f ? Mix(A, C, 0.5f) : A;   // one brown spot on the back ("Truffle")
            });
        }

        static CreatureMeshImage CapuchinSkin(Pal P)
        {
            Color A = P.A, B = P.B, C = P.C;
            var pal = new[] { Hx(0xE2B985), C, A, C, B, Mix(A, C, 0.25f), Hx(0xD9A08A), Ink };
            return PalLoft(pal, (x, y, u, e, r) =>
            {
                if (u < 0.3f && e < 0.55f) return B;            // cream chest and throat
                if (e > 0.75f && u < 0.2f) return C;            // dark cap
                return A;
            });
        }

        // ---------------------------------------------------------------------------------- new fish

        static CreatureMeshImage CleanerWrasse(Pal P)
        {
            // electric blue back, white belly, black stripe from the snout to the tail that widens backwards
            return Loft(32, 16, (x, y, u, e, r) =>
            {
                float w = CreatureMeshMath.Lerp(0.05f, 0.13f, u);
                if (CreatureMeshMath.Abs(e - 0.5f) < w && u > 0.02f) return P.C;
                if (e < 0.5f) return Mix(P.B, White, 0.1f);
                return e > 0.8f ? Mix(P.A, Ink, 0.12f) : P.A;
            });
        }

        static CreatureMeshImage Sardine(Pal P)
        {
            // silver flank, blue-grey back, one row of dark spots along the flank
            return Loft(32, 16, (x, y, u, e, r) =>
            {
                if (e > 0.7f) return P.C;
                if (e < 0.32f) return P.B;
                if (r == 5 && x > 6 && x < 24 && x % 3 == 0) return Mix(P.C, Ink, 0.35f);
                return P.A;
            });
        }

        static CreatureMeshImage Yellowfin(Pal P)
        {
            // dark blue back, silver flanks with faint bars, a yellow band along the side
            return Loft(48, 16, (x, y, u, e, r) =>
            {
                if (e > 0.72f) return P.A;
                if (e > 0.62f && e <= 0.72f && u > 0.1f && u < 0.75f) return P.C;
                if (e < 0.3f) return Mix(P.B, White, 0.15f);
                if (e < 0.62f && x % 4 == 0 && u > 0.2f && u < 0.8f) return Mix(P.B, P.A, 0.25f);
                return P.B;
            });
        }

        static CreatureMeshImage Lanternfish(Pal P)
        {
            return Loft(32, 16, (x, y, u, e, r) => e > 0.7f ? P.A : (e < 0.3f ? Mix(P.B, White, 0.15f) : P.B));
        }

        static CreatureMeshImage Mandarin(Pal P)
        {
            // psychedelic: blue base with orange wavy stripes and green-teal rims
            return Loft(32, 32, (x, y, u, e, r) =>
            {
                int k = (int)(u * 12f + CreatureMeshMath.Sin(e * 9f + u * 4f) * 1.6f);
                if (u < 0.08f) return k % 2 == 0 ? P.B : P.A;
                if ((k & 1) == 0) return P.B;
                if (P.R(x, y, 17) > 0.86f) return P.C;
                return P.A;
            });
        }

        static CreatureMeshImage Coelacanth(Pal P)
        {
            // deep steel blue with irregular white blotches (per facet), slightly lighter belly
            return Loft(16, 8, (x, y, u, e, r) =>
            {
                float n = P.R(x, y, 19);
                if (n > 0.8f) return Mix(P.C, P.B, 0.2f);
                if (e < 0.25f) return Mix(P.A, P.B, 0.35f);
                return n < 0.15f ? Mix(P.A, Ink, 0.2f) : P.A;
            });
        }

        // ============================================================================================ fins

        static bool FinR2(CreatureMeshContext ctx, Pal P, int band, int x, int s, int y, out Color c)
        {
            Color A = P.A, B = P.B, C = P.C;
            c = default;
            switch (ctx.S.plan)
            {
                case BodyPlan.Dolphin:
                    c = x >= 6 ? Mix(C, Ink, 0.15f) : Mix(A, C, 0.45f);
                    return true;
                case BodyPlan.Whale:
                    if (band == 2) c = x <= 1 ? Mix(A, B, 0.35f) : (P.R(x, y, 23) > 0.85f ? Mix(B, A, 0.4f) : Mix(B, White, 0.05f));   // long white pectorals
                    else if (band == 1) c = x >= 6 ? Mix(B, A, 0.25f) : A;                                                     // flukes, pale trailing edge
                    else c = A;
                    return true;
                case BodyPlan.Bird:
                    if (P.Variant == "frigate") { c = band == 2 && x <= 1 ? Mix(A, C, 0.5f) : A; return true; }
                    if (P.Variant == "tropic") { c = band == 2 ? (s == 3 && x <= 3 ? B : (s == 1 && x <= 2 ? B : A)) : A; return true; }
                    // gull: grey wings with black tips and a white trailing edge, white tail
                    if (band == 2) c = s == 3 ? (x >= 6 ? White : Ink) : (x >= 6 ? A : B);
                    else c = A;
                    return true;
                case BodyPlan.Bat:
                    c = band == 2 ? ((s & 1) == 0 && x <= 1 ? Mix(C, A, 0.5f) : C) : Mix(C, A, 0.3f);
                    return true;
                case BodyPlan.Starfish:
                case BodyPlan.Crab:
                case BodyPlan.Lizard:
                case BodyPlan.Quadruped:
                case BodyPlan.Primate:
                    c = A;
                    return true;
            }
            switch (P.Id)
            {
                case "cleaner_wrasse": c = band == 1 ? (x >= 3 && (s == 1 || s == 2) ? C : Mix(A, White, 0.2f)) : (x >= 6 ? C : A); return true;
                case "sardine": c = x >= 6 ? Mix(C, Ink, 0.2f) : Mix(A, C, 0.4f); return true;
                case "flying_fish": c = band == 2 ? (x >= 5 ? Mix(C, White, 0.25f) : Mix(C, A, 0.35f)) : Mix(A, C, 0.3f); return true;
                case "yellowfin": c = band == 0 || band == 3 ? C : (band == 2 ? Mix(A, B, 0.3f) : Mix(A, C, 0.15f)); return true;
                case "lanternfish": c = Mix(A, B, 0.4f); return true;
                case "mandarinfish": c = x >= 6 ? C : ((x + s) % 2 == 0 ? B : A); return true;
                case "coelacanth": c = x >= 6 ? Mix(C, A, 0.35f) : (P.R(x, y, 29) > 0.75f ? C : A); return true;
                case "goblin_shark": c = x >= 6 ? Mix(C, A, 0.4f) : A; return true;
                case "gulper_eel": c = band == 3 ? C : A; return true;
                case "ribbon_eel": c = band == 0 ? B : A; return true;
                case "pygmy_seahorse": c = Mix(A, White, 0.3f); return true;
                case "flamboyant_cuttle":
                    if (band == 0) c = x >= 5 ? B : C;     // yellow-rimmed purple skirt
                    else c = (x + s) % 3 == 0 ? C : A;
                    return true;
                case "spotted_eagle_ray": c = band == 3 ? Mix(A, Ink, 0.2f) : A; return true;
            }
            return false;
        }
    }
}
