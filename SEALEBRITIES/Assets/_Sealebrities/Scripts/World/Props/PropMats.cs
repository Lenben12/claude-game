using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Shared low-poly materials of the props module: solid matte palette colors (LowPoly.Mat), no surface textures.
    /// Color variety comes from separate parts and a few deterministic palette variants (Var / Pick), never from noise maps.
    /// The only textures are tiny point-filtered block textures for crisp graphics (helipad "H", signs, poster, ship name).
    public static class PropMats
    {
        static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Cache.Clear(); }

        static Material Get(string k, Func<Material> make)
        {
            if (Cache.TryGetValue(k, out var m) && m != null) return m;
            m = make();
            Cache[k] = m;
            return m;
        }

        // ================================================================== color helpers
        /// HSV shift of a palette color (dv = value, ds = saturation, dh = hue in 0..1 turns).
        public static Color Shade(Color c, float dv, float ds = 0f, float dh = 0f)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            return Color.HSVToRGB(Mathf.Repeat(h + dh, 1f), Mathf.Clamp01(s + ds), Mathf.Clamp01(v + dv));
        }

        /// Deterministic palette variant (0 = the color itself). Few variants keep the draw calls low.
        public static Color Variant(Color c, int i)
        {
            switch (((i % 4) + 4) % 4)
            {
                case 0: return c;
                case 1: return Shade(c, 0.06f, -0.03f, 0.006f);
                case 2: return Shade(c, -0.07f, 0.04f, -0.006f);
                default: return Shade(c, 0.025f, 0.06f, 0.012f);
            }
        }

        /// Shared flat matte material of a color.
        public static Material Flat(Color c) => LowPoly.Mat(c);
        public static Material Var(Color c, int i) => Flat(Variant(c, i));
        public static Material Pick(Color c, System.Random rng, int variants = 3) => Var(c, rng.Next(Mathf.Max(1, variants)));

        static Material Textured(Texture2D t) => MaterialLibrary.Textured(t, Color.white, LowPoly.Smoothness, 0f);

        /// New emissive material instance (not cached) so its emission can be animated (night lights).
        public static Material EmissiveInstance(Color baseColor, Color emission)
        {
            var m = new Material(MaterialLibrary.Lit) { name = "Prop_Emissive" };
            m.SetColor("_BaseColor", baseColor);
            m.SetFloat("_Smoothness", LowPoly.Smoothness);
            m.SetFloat("_Metallic", 0f);
            m.SetColor("_EmissionColor", emission);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            return m;
        }

        public static void SetEmission(Material m, Color c)
        {
            if (m != null) m.SetColor("_EmissionColor", c);
        }

        // ================================================================== island colors
        public static readonly Color ConcreteColor = Shade(LowPoly.Rock, 0.24f, -0.02f);
        public static readonly Color ShutterColor = Shade(LowPoly.Water, -0.06f, 0.05f);

        public static Material Wood => Flat(LowPoly.Wood);
        public static Material WoodLight => Flat(LowPoly.WoodLight);
        public static Material WoodDark => Flat(LowPoly.WoodDark);
        public static Material WoodWet => Flat(Shade(LowPoly.WoodDark, -0.16f, 0.05f));
        public static Material Driftwood => Flat(Shade(LowPoly.WoodLight, -0.08f, -0.18f));

        public static Material Wall => Flat(LowPoly.White);
        public static Material Wall2 => Flat(Shade(LowPoly.White, -0.05f, 0.01f));
        public static Material Roof => Flat(LowPoly.Roof);
        public static Material RoofDark => Flat(Shade(LowPoly.Roof, -0.1f, 0.02f));
        public static Material Shutter => Flat(ShutterColor);

        public static Material PalmTrunk => Flat(LowPoly.PalmTrunk);
        public static Material PalmTrunkDark => Flat(Shade(LowPoly.PalmTrunk, -0.08f, 0.02f));
        public static Material PalmLeaf => Flat(LowPoly.PalmLeaf);
        public static Material PalmLeafDark => Flat(LowPoly.PalmLeafDark);
        public static Material PalmLeafDry => Flat(LowPoly.PalmLeafDry);
        public static Material Coconut => Flat(Shade(LowPoly.WoodDark, -0.06f, 0.05f));

        public static Material Grass => Flat(LowPoly.Grass);
        public static Material GrassDark => Flat(LowPoly.GrassDark);
        public static Material GrassLight => Flat(Shade(LowPoly.Grass, 0.07f, -0.04f, -0.01f));
        public static Material Stem => Flat(Shade(LowPoly.GrassDark, -0.08f));
        public static Material TreeTrunk => Flat(Shade(LowPoly.WoodDark, -0.04f));

        public static Material FlowerRed => Flat(LowPoly.Roof);
        public static Material FlowerOrange => Flat(LowPoly.CoralOrange);
        public static Material FlowerPink => Flat(LowPoly.CoralPink);
        public static Material FlowerYellow => Flat(LowPoly.CoralYellow);
        public static Material FlowerWhite => Flat(LowPoly.White);

        public static Material Rock => Flat(LowPoly.Rock);
        public static Material RockDark => Flat(LowPoly.RockDark);
        public static Material RockLight => Flat(Shade(LowPoly.Rock, 0.14f, -0.02f));
        public static Material Stone => Flat(Color.Lerp(LowPoly.Rock, LowPoly.Sand, 0.45f));
        public static Material Basalt => Flat(LowPoly.DeepRock);

        public static Material Concrete => Flat(ConcreteColor);
        public static Material ConcreteDark => Flat(Shade(ConcreteColor, -0.12f));
        public static Material Marking => Flat(LowPoly.CoralYellow);
        public static Material Helipad => Get("helipad", () => Textured(PropTex.HelipadH));

        public static Material Metal => Flat(LowPoly.Metal);
        public static Material DarkMetal => Flat(Shade(LowPoly.Ink, 0.1f));
        public static Material Rust => Flat(LowPoly.Rust);
        public static Material RedMetal => Flat(LowPoly.Roof);
        public static Material DrumBlue => Flat(LowPoly.WaterDeep);
        public static Material DrumRed => Flat(LowPoly.Roof);
        public static Material DrumYellow => Flat(LowPoly.CoralYellow);

        public static Material Rubber => Flat(LowPoly.Ink);
        public static Material Rope => Flat(Shade(LowPoly.WoodLight, -0.06f, -0.08f));
        public static Material CushionA => Flat(LowPoly.Water);
        public static Material CushionB => Flat(LowPoly.CoralPink);
        public static Material ParasolA => Flat(LowPoly.CoralYellow);
        public static Material ParasolB => Flat(LowPoly.White);
        public static Material Orange => Flat(LowPoly.SuitOrange);
        public static Material White => Flat(LowPoly.White);
        public static Material Blanket => Flat(Shade(LowPoly.WaterDeep, 0.06f));
        public static Material Armchair => Flat(LowPoly.CoralOrange);
        public static Material RugA => Flat(LowPoly.CoralPurple);
        public static Material RugB => Flat(LowPoly.CoralYellow);
        public static Material TvBody => Flat(Shade(LowPoly.Sand, -0.12f, -0.05f));
        public static Material Screen => Flat(Shade(LowPoly.Ink, 0.06f, 0.05f, 0.02f));
        public static Material Ink => Flat(LowPoly.Ink);
        public static Material Ash => Flat(Shade(LowPoly.Ink, 0.16f, -0.05f));
        public static Material Gold => Flat(Shade(LowPoly.CoralYellow, 0.02f, 0.12f));
        public static Material Surfboard => Flat(LowPoly.CoralOrange);
        public static Material Sign => Get("sign", () => Textured(PropTex.HutSign));
        public static Material Poster => Get("poster", () => Textured(PropTex.Poster));
        public static Material LaptopScreen => Get("laptop_screen", () => EmissiveInstance(new Color(0.2f, 0.32f, 0.45f), new Color(0.3f, 0.6f, 0.9f)));
        public static Material Glass => Get("glass", () => MaterialLibrary.Transparent(new Color(0.78f, 0.92f, 0.96f, 0.3f), 0.5f));

        // animated emissive instances (one per light group)
        public static Material HutBulb => Get("bulb_hut", () => EmissiveInstance(new Color(1f, 0.92f, 0.75f), Color.black));
        public static Material DockBulb => Get("bulb_dock", () => EmissiveInstance(new Color(1f, 0.9f, 0.7f), Color.black));
        public static Material PadBulb => Get("bulb_pad", () => EmissiveInstance(new Color(0.95f, 0.88f, 0.55f), Color.black));
        public static Material FireGlow => Get("fire_glow", () => EmissiveInstance(LowPoly.CoralOrange, Color.black));
        public static Material Lens => Get("lens", () => EmissiveInstance(new Color(0.98f, 0.95f, 0.82f), Color.black));

        // ================================================================== underwater colors
        // One shared reef palette (8 colors); every coral family indexes into it so a reef chunk needs few materials.
        static readonly Color[] Reef =
        {
            LowPoly.CoralPink,                                     // 0 pink
            LowPoly.CoralOrange,                                   // 1 orange
            LowPoly.CoralPurple,                                   // 2 purple
            LowPoly.CoralYellow,                                   // 3 yellow
            Shade(LowPoly.CoralPink, 0.03f, 0.18f, -0.025f),       // 4 hot red-pink
            Shade(LowPoly.CoralPurple, 0.1f, -0.12f, 0.04f),       // 5 lilac
            Shade(LowPoly.WaterShallow, -0.14f, 0.16f, 0.03f),     // 6 teal
            Shade(LowPoly.Kelp, 0.2f, -0.04f),                     // 7 olive green
        };

        static int Wrap(int i, int n) => ((i % n) + n) % n;
        static Material ReefMat(int[] map, int i) => Flat(Reef[map[Wrap(i, map.Length)]]);

        static readonly int[] CoralMap = { 0, 1, 2, 3, 4, 5 };
        static readonly int[] BrainMap = { 3, 7, 1 };
        static readonly int[] TableMap = { 6, 5 };
        static readonly int[] FanMap = { 2, 4, 3 };
        static readonly int[] SpongeMap = { 2, 1, 3 };
        static readonly int[] WhipMap = { 4, 3, 2 };

        public static int CoralCount => CoralMap.Length;
        public static Material Coral(int i) => ReefMat(CoralMap, i);
        public static int BrainCount => BrainMap.Length;
        public static Material Brain(int i) => ReefMat(BrainMap, i);
        public static Material Table(int i) => ReefMat(TableMap, i);
        public static int FanCount => FanMap.Length;
        public static Material Fan(int i) => ReefMat(FanMap, i);
        public static int SpongeCount => SpongeMap.Length;
        public static Material Sponge(int i) => ReefMat(SpongeMap, i);
        public static Material SpongeInside => Flat(Shade(LowPoly.DeepRock, -0.1f));
        public static int WhipCount => WhipMap.Length;
        public static Material Whip(int i) => ReefMat(WhipMap, i);

        public static Material AnemoneBody => Flat(Reef[5]);
        public static Material AnemoneTentacle => Flat(Shade(LowPoly.CoralPink, 0.08f, -0.12f));
        public static Material SeaGrass => Flat(Shade(LowPoly.Grass, -0.08f, 0.02f, 0.03f));
        public static Material SeaGrassDark => Flat(Shade(LowPoly.Kelp, -0.04f, 0.04f));
        public static Material Kelp => Flat(LowPoly.Kelp);
        public static Material KelpDark => Flat(LowPoly.KelpDark);
        public static Material ReefRock => Flat(Color.Lerp(LowPoly.Rock, LowPoly.Seabed, 0.42f));
        public static Material ReefRockDark => Flat(Shade(Color.Lerp(LowPoly.Rock, LowPoly.Seabed, 0.3f), -0.1f));

        public static Material HullUpper => Flat(Shade(LowPoly.Ink, 0.2f, 0.05f, -0.02f));
        public static Material HullLower => Flat(LowPoly.Rust);
        public static Material Deck => Flat(Shade(LowPoly.Rust, -0.25f, -0.2f));
        public static Material Superstructure => Flat(Shade(LowPoly.White, -0.14f, 0.02f));
        public static Material Crane => Flat(Shade(LowPoly.CoralYellow, -0.18f, -0.05f));
        public static Material Funnel => Flat(LowPoly.Roof);

        static readonly Color[] ContainerColors =
        {
            Shade(LowPoly.Roof, -0.12f), Shade(LowPoly.WaterDeep, -0.08f), Shade(LowPoly.GrassDark, -0.15f), Shade(LowPoly.CoralOrange, -0.15f),
        };
        public static int ContainerCount => ContainerColors.Length;
        public static Material Container(int i) => Flat(ContainerColors[Wrap(i, ContainerColors.Length)]);
        public static Material ContainerEnd(int i) => Flat(Shade(ContainerColors[Wrap(i, ContainerColors.Length)], -0.1f));

        public static Material HoleDark => Flat(new Color(0.02f, 0.025f, 0.03f));
        public static Material NamePlate => Get("nameplate", () => Textured(PropTex.NamePlate));
        public static Material Bone => Flat(Shade(LowPoly.White, -0.07f, 0.06f));
        public static Material Chimney => Flat(Shade(LowPoly.DeepRock, -0.14f));
        public static Material VentGlow => Get("vent_glow", () => EmissiveInstance(new Color(0.35f, 0.1f, 0.03f), new Color(2.4f, 0.75f, 0.15f)));
        public static Material Bio => Get("bio", () => EmissiveInstance(new Color(0.05f, 0.2f, 0.25f), new Color(0.2f, 1.6f, 2.2f)));
        public static Material BioGreen => Get("bio_green", () => EmissiveInstance(new Color(0.05f, 0.25f, 0.1f), new Color(0.4f, 2.0f, 0.6f)));
        public static Material TubeWorm => Flat(LowPoly.White);
        public static Material WormPlume => Get("worm_plume", () => EmissiveInstance(new Color(0.85f, 0.12f, 0.1f), new Color(0.4f, 0.03f, 0.02f)));
        public static Material BuoyOrange => Flat(LowPoly.SuitOrange);
        public static Material BuoyWhite => Flat(LowPoly.White);

        /// Flag in a dive site's map color (two-sided geometry, so a plain material).
        public static Material Flag(Color c) => Flat(c);

        // ================================================================== prewarm
        /// Creates the island materials (cheap now: flat colors + three tiny block textures).
        public static IEnumerator PrewarmIsland()
        {
            Func<object>[] steps =
            {
                () => Wood, () => WoodLight, () => WoodDark, () => Wall, () => Roof, () => PalmLeaf, () => PalmTrunk,
                () => Grass, () => Rock, () => Concrete, () => Helipad, () => Sign, () => Poster, () => HutBulb,
                () => DockBulb, () => PadBulb, () => FireGlow, () => Lens, () => Glass, () => LaptopScreen,
            };
            for (int i = 0; i < steps.Length; i++)
            {
                steps[i]();
                if (PropRun.Due()) yield return null;
            }
        }

        public static IEnumerator PrewarmUnderwater()
        {
            Func<object>[] steps =
            {
                () => Coral(0), () => Brain(0), () => Fan(0), () => Sponge(0), () => Kelp, () => KelpDark,
                () => HullUpper, () => HullLower, () => NamePlate, () => Bone, () => VentGlow, () => Bio, () => BioGreen,
            };
            for (int i = 0; i < steps.Length; i++)
            {
                steps[i]();
                if (PropRun.Due()) yield return null;
            }
        }
    }
}
