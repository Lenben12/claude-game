using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// The props palette from the "How to Fish" art bible (docs/ArtBible_HowToFish.md §1.7, §1.9-1.11, §2.4).
    /// Albedo values (sRGB). Kept here (not only in LowPoly) so the props look right even before the global palette
    /// commit of the world module lands; when LowPoly gets the same values nothing changes.
    public static class PropPal
    {
        static Color H(string s) => LowPoly.Hex(s);

        // ground
        public static readonly Color Sand = H("#E6CFA0"), WetSand = H("#C9AF86"), Seabed = H("#EAC27A");
        // (art polish: greens a step darker and less saturated – lit lawns measured lime #74A037..#90AB58 against How
        // to Fish's #596646 / #668443; grass-clump tips read neon yellow)
        public static readonly Color Grass = H("#5D7C45"), GrassDark = H("#486036"), GrassBase = H("#34502A"), GrassMid = H("#557A38"), GrassTip = H("#8AA352");
        public static readonly Color GrassDry = H("#8C9654"), DuneBase = H("#8A8A4E"), DuneTip = H("#C9C07A");
        public static readonly Color PathDirt = H("#A88763"), PathDirtDark = H("#957556"), Soil = H("#8E6A4C");

        // vegetation
        public static readonly Color Foliage = H("#5E8A3A"), FoliageDark = H("#46682C"), FoliageLight = H("#7A9C45");
        public static readonly Color Conifer = H("#3F5F35"), ConiferLight = H("#567A3E"), ConiferTrunk = H("#4A3A44");
        public static readonly Color TreeBark = H("#5E4A44"), Vine = H("#3F5A2C");
        public static readonly Color PalmTrunk = H("#7A4A50"), PalmRing = H("#5A3840"), PalmLeaf = H("#7FA243"), PalmLeafDark = H("#4E6F2E"), PalmLeafDry = H("#B5A050");
        public static readonly Color Coconut = H("#5A3E2E"), BananaStem = H("#8A8F4E");
        public static readonly Color FlowerMagenta = H("#A8478F"), FlowerPink = H("#E86FA0"), FlowerWhite = H("#F4EFE6"), FlowerPurple = H("#8E5CC2"), FlowerYellow = H("#F2C94C");

        // rocks
        public static readonly Color Rock = H("#787A86"), RockMid = H("#666876"), RockDark = H("#56545F"), BeachRock = H("#6A6672");
        public static readonly Color Cliff = H("#7E808C"), Crevice = H("#4A4658"), Moss = H("#6E9445"), DeepRock = H("#3B3A4E"), Volcanic = H("#56525E"), VolcanicDark = H("#423E4A");

        // wood and buildings
        public static readonly Color Wood = H("#8E6C50"), Wood2 = H("#7F6149"), Wood3 = H("#9A795D"), WoodDark = H("#5E4436"), WoodLight = H("#C9B08E"), WoodWet = H("#4A3A30");
        public static readonly Color ShackBoard = H("#8B6B5A"), ShackBoard2 = H("#7A5E50"), Shingle = H("#5A4E52");
        public static readonly Color Whitewash = H("#DCCFBE"), Whitewash2 = H("#CFC2B2"), Trim = H("#EDE6DA"), PlankGap = H("#3A2C2A"), AccentBoard = H("#7A4448");
        public static readonly Color Roof = H("#A8484A"), Turquoise = H("#3FA9B5"), WindowGlass = H("#7F9CB8");
        public static readonly Color Metal = H("#8E949C"), Rust = H("#A0583A"), RustDeep = H("#8A5A42"), White = H("#EDE6DA"), Ink = H("#1E1B2B");
        public static readonly Color LighthouseRed = H("#B8454A"), LanternRoom = H("#2A2833"), LampGlass = H("#FFF1C9");
        public static readonly Color Rope = H("#C9B48A"), BridgePlank = H("#8E6B4E"), Concrete = H("#A9A7AE"), Canvas = H("#E8DCC0");

        // water features
        public static readonly Color Waterfall = H("#4FA3C8"), Foam = H("#EEF4F6"), TidePool = H("#4FB0BE");
        public static readonly Color CaveInside = H("#2A2438"), CaveFloor = H("#3A3346"), Mushroom = H("#9BFF6E");

        // beach life / accents
        public static readonly Color Starfish = H("#F28C4A"), Coral = H("#FF7A5C"), Sun = H("#F2C94C"), SuitOrange = H("#EE7F2D"), Gold = H("#E8B83A");
        public static readonly Color Paper = H("#F1E6CC"), Crab = H("#D9554A"), Urchin = H("#4A3458");

        // reef (§2.4)
        public static readonly Color CoralPink = H("#F2708A"), CoralOrange = H("#F28C4A"), CoralPurple = H("#8E5CC2"), CoralYellow = H("#F2C94C");
        public static readonly Color CoralRed = H("#D9455A"), CoralLilac = H("#B59AD9"), CoralTeal = H("#3FA9A0"), CoralOlive = H("#7E9A3A");
        public static readonly Color Kelp = H("#7E9A3A"), KelpDark = H("#55702A"), SeaGrass = H("#6F8F3A"), ReefRock = H("#6E6F86");
        public static readonly Color HullBlue = H("#3B4A5E"), HullRust = H("#8A4A32"), WreckDeck = H("#5A4A44"), Porthole = H("#14121E");
    }

    /// Shared low-poly materials of the props module: solid matte palette colors (LowPoly.Mat), no surface textures.
    /// Color variety comes from separate parts and a few deterministic palette variants (Var / Pick), never from noise maps.
    /// The only textures are tiny point-filtered block textures for crisp graphics (helipad "H", signs, poster, ship name)
    /// and the 1 x 8 grass color ramps (art bible §1.9).
    public static class PropMats
    {
        static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Cache.Clear(); leafDay = 1f; }

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

        /// Multiplies the value (brightness) of a color: 1.06 = +6 %.
        public static Color Value(Color c, float mul)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            return Color.HSVToRGB(h, s, Mathf.Clamp01(v * mul));
        }

        /// Deterministic palette variant (0 = the color itself). Few variants keep the draw calls low.
        public static Color Variant(Color c, int i)
        {
            switch (((i % 4) + 4) % 4)
            {
                case 0: return c;
                case 1: return Shade(c, 0.05f, -0.02f, 0.005f);
                case 2: return Shade(c, -0.06f, 0.03f, -0.005f);
                default: return Shade(c, 0.02f, 0.05f, 0.01f);
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
            m.enableInstancing = true;
            return m;
        }

        public static void SetEmission(Material m, Color c)
        {
            if (m != null) m.SetColor("_EmissionColor", c);
        }

        // ================================================================== island colors
        public static readonly Color ConcreteColor = PropPal.Concrete;
        public static readonly Color ShutterColor = PropPal.Turquoise;

        public static Material Wood => Flat(PropPal.Wood);
        public static Material Wood2 => Flat(PropPal.Wood2);
        public static Material Wood3 => Flat(PropPal.Wood3);
        public static Material WoodLight => Flat(PropPal.WoodLight);
        public static Material WoodDark => Flat(PropPal.WoodDark);
        public static Material WoodWet => Flat(PropPal.WoodWet);
        public static Material Driftwood => Flat(Shade(PropPal.WoodLight, -0.06f, -0.16f));
        public static Material ShackBoard => Flat(PropPal.ShackBoard);
        public static Material ShackBoard2 => Flat(PropPal.ShackBoard2);
        public static Material Shingle => Flat(PropPal.Shingle);
        public static Material ShingleDark => Flat(Value(PropPal.Shingle, 0.88f));
        public static Material BridgePlank => Flat(PropPal.BridgePlank);

        /// Whitewashed clapboard in three value variants (±6 %).
        public static Material Whitewash(int i)
        {
            switch (((i % 3) + 3) % 3)
            {
                case 0: return Flat(PropPal.Whitewash);
                case 1: return Flat(PropPal.Whitewash2);
                default: return Flat(Value(PropPal.Whitewash, 1.04f));
            }
        }
        public static Material Wall => Whitewash(0);
        public static Material Wall2 => Whitewash(1);
        public static Material Trim => Flat(PropPal.Trim);
        public static Material PlankGap => Flat(PropPal.PlankGap);
        public static Material AccentBoard => Flat(PropPal.AccentBoard);
        public static Material Roof => Flat(PropPal.Roof);
        public static Material RoofDark => Flat(Value(PropPal.Roof, 0.86f));
        public static Material Shutter => Flat(PropPal.Turquoise);
        public static Material WindowGlass => Flat(PropPal.WindowGlass);

        public static Material PalmTrunk => Flat(PropPal.PalmTrunk);
        public static Material PalmTrunkAlt => Flat(Value(PropPal.PalmTrunk, 1.06f));
        public static Material PalmTrunkDark => Flat(PropPal.PalmRing);
        public static Material PalmRing => Flat(PropPal.PalmRing);
        public static Material PalmLeaf => Flat(PropPal.PalmLeaf);
        /// Underside of palm fronds and fern/taro leaves. How to Fish fronds seen against the sky read mid olive
        /// (#567144 / #6C8F4F, sunlight through the blade); a plain dark underside lit only by the ground ambient read
        /// near-black (#283B2D) from below. A small daylight-scaled emission fakes the translucency (SetDaylight).
        public static Material PalmLeafDark => Get(LeafUnderKey, () => EmissiveInstance(PropPal.PalmLeafDark, LeafGlow * leafDay));

        const string LeafUnderKey = "leaf_under";
        static readonly Color LeafGlow = new Color(0.18f, 0.27f, 0.10f);   // first try 0.30/0.41/0.22 read pastel (#788A66)
        static float leafDay = 1f;

        /// Called by the day/night cycle (cheap: only touches the material when the daylight changed noticeably).
        public static void SetDaylight(float day01)
        {
            day01 = Mathf.Clamp01(day01);
            if (Mathf.Abs(day01 - leafDay) < 0.01f) return;
            leafDay = day01;
            if (Cache.TryGetValue(LeafUnderKey, out var m) && m != null) m.SetColor("_EmissionColor", LeafGlow * day01);
        }
        public static Material PalmLeafDry => Flat(PropPal.PalmLeafDry);
        public static Material Coconut => Flat(PropPal.Coconut);

        public static Material Grass => Flat(PropPal.Grass);
        public static Material GrassDark => Flat(PropPal.GrassDark);
        public static Material GrassLight => Flat(PropPal.FoliageLight);
        public static Material Stem => Flat(PropPal.GrassBase);
        public static Material TreeTrunk => Flat(PropPal.TreeBark);
        public static Material TreeBark => Flat(PropPal.TreeBark);
        public static Material ConiferTrunk => Flat(PropPal.ConiferTrunk);
        public static Material Foliage => Flat(PropPal.Foliage);
        public static Material FoliageDark => Flat(PropPal.FoliageDark);
        public static Material FoliageLight => Flat(PropPal.FoliageLight);
        public static Material Conifer => Flat(PropPal.Conifer);
        public static Material ConiferLight => Flat(PropPal.ConiferLight);
        public static Material Vine => Flat(PropPal.Vine);
        public static Material BananaStem => Flat(PropPal.BananaStem);
        public static Material PathDirt => Flat(PropPal.PathDirt);
        public static Material PathDirtDark => Flat(PropPal.PathDirtDark);

        public static Material FlowerRed => Flat(PropPal.FlowerMagenta);
        public static Material FlowerMagenta => Flat(PropPal.FlowerMagenta);
        public static Material FlowerOrange => Flat(PropPal.CoralOrange);
        public static Material FlowerPink => Flat(PropPal.FlowerPink);
        public static Material FlowerYellow => Flat(PropPal.FlowerYellow);
        public static Material FlowerWhite => Flat(PropPal.FlowerWhite);
        public static Material FlowerPurple => Flat(PropPal.FlowerPurple);

        public static Material Rock => Flat(PropPal.Rock);
        public static Material RockMid => Flat(PropPal.RockMid);
        public static Material RockDark => Flat(PropPal.RockDark);
        public static Material RockLight => Flat(PropPal.Cliff);
        public static Material BeachRock => Flat(PropPal.BeachRock);
        public static Material Cliff => Flat(PropPal.Cliff);
        public static Material Crevice => Flat(PropPal.Crevice);
        public static Material Moss => Flat(PropPal.Moss);
        public static Material Stone => Flat(Color.Lerp(PropPal.Rock, PropPal.Sand, 0.4f));
        public static Material Basalt => Flat(PropPal.DeepRock);
        public static Material Volcanic => Flat(PropPal.Volcanic);
        public static Material VolcanicDark => Flat(PropPal.VolcanicDark);

        public static Material Concrete => Flat(ConcreteColor);
        public static Material ConcreteDark => Flat(Value(ConcreteColor, 0.84f));
        public static Material Marking => Flat(PropPal.Sun);
        public static Material Helipad => Get("helipad", () => Textured(PropTex.HelipadH));

        public static Material Metal => Flat(PropPal.Metal);
        public static Material DarkMetal => Flat(PropPal.LanternRoom);
        public static Material Rust => Flat(PropPal.Rust);
        public static Material RustDeep => Flat(PropPal.RustDeep);
        public static Material RedMetal => Flat(PropPal.LighthouseRed);
        public static Material LighthouseRed => Flat(PropPal.LighthouseRed);
        public static Material LanternRoom => Flat(PropPal.LanternRoom);
        public static Material DrumBlue => Flat(Shade(PropPal.Turquoise, -0.18f, 0.05f));
        public static Material DrumRed => Flat(PropPal.LighthouseRed);
        public static Material DrumYellow => Flat(PropPal.Sun);

        public static Material Rubber => Flat(PropPal.Ink);
        public static Material Rope => Flat(PropPal.Rope);
        public static Material Canvas => Flat(PropPal.Canvas);
        public static Material CushionA => Flat(PropPal.Turquoise);
        public static Material CushionB => Flat(PropPal.FlowerPink);
        public static Material ParasolA => Flat(PropPal.Sun);
        public static Material ParasolB => Flat(PropPal.Trim);
        public static Material Orange => Flat(PropPal.SuitOrange);
        public static Material White => Flat(PropPal.White);
        public static Material Blanket => Flat(Shade(PropPal.Turquoise, -0.1f));
        public static Material Armchair => Flat(PropPal.Coral);
        public static Material RugA => Flat(PropPal.FlowerPurple);
        public static Material RugB => Flat(PropPal.Sun);
        public static Material TvBody => Flat(Shade(PropPal.Sand, -0.14f, -0.05f));
        public static Material Screen => Flat(Shade(PropPal.Ink, 0.08f, 0.04f, 0.02f));
        public static Material Ink => Flat(PropPal.Ink);
        public static Material Ash => Flat(Shade(PropPal.Ink, 0.18f, -0.05f));
        public static Material Gold => Flat(PropPal.Gold);
        public static Material Paper => Flat(PropPal.Paper);
        public static Material Surfboard => Flat(PropPal.CoralOrange);
        public static Material Starfish => Flat(PropPal.Starfish);
        public static Material Crab => Flat(PropPal.Crab);
        public static Material Urchin => Flat(PropPal.Urchin);
        public static Material CaveInside => Flat(PropPal.CaveInside);
        public static Material CaveFloor => Flat(PropPal.CaveFloor);
        public static Material Sign => Get("sign", () => Textured(PropTex.HutSign));
        public static Material Poster => Get("poster", () => Textured(PropTex.Poster));
        public static Material GusPoster => Get("gus_poster", () => Textured(PropTex.GusPoster));
        public static Material LaptopScreen => Get("laptop_screen", () => EmissiveInstance(new Color(0.2f, 0.32f, 0.45f), new Color(0.3f, 0.6f, 0.9f)));
        public static Material Glass => Get("glass", () => MaterialLibrary.Transparent(new Color(0.78f, 0.9f, 0.96f, 0.32f), 0.5f));
        public static Material BottleGlass => Get("bottle_glass", () => MaterialLibrary.Transparent(new Color(0.45f, 0.72f, 0.55f, 0.6f), 0.7f));

        /// Turquoise water of pools (decor, the player cannot swim in it).
        public static Material PoolWater => Get("pool_water", () => MaterialLibrary.Transparent(new Color(PropPal.TidePool.r, PropPal.TidePool.g, PropPal.TidePool.b, 0.72f), 0.75f));
        /// Falling water ribbons (two-sided, slightly transparent).
        public static Material FallWater => Get("fall_water", () => MaterialLibrary.Transparent(new Color(PropPal.Waterfall.r, PropPal.Waterfall.g, PropPal.Waterfall.b, 0.86f), 0.7f, true));
        public static Material FallFoam => Get("fall_foam", () => MaterialLibrary.Transparent(new Color(PropPal.Foam.r, PropPal.Foam.g, PropPal.Foam.b, 0.9f), 0.3f, true));

        /// Grass clumps: one material for all blades, color from a 1 x 8 vertical ramp (uv.y = height fraction),
        /// rendered two-sided (single-sided blade triangles with up-facing normals).
        public static Material GrassRamp => Get("grass_ramp", () => TwoSided(Textured(PropTex.GrassRamp), "Prop_GrassRamp"));
        public static Material DuneRamp => Get("dune_ramp", () => TwoSided(Textured(PropTex.DuneRamp), "Prop_DuneRamp"));

        static Material TwoSided(Material m, string name)
        {
            m.name = name;
            m.SetFloat("_Cull", 0f);
            m.enableInstancing = true;
            return m;
        }

        /// Sign board with a pictogram (signposts, warning signs).
        public static Material SignIcon(PropIcon icon) => Get("icon_" + (int)icon, () => Textured(PropTex.Icon(icon)));

        // animated emissive instances (one per light group)
        public static Material HutBulb => Get("bulb_hut", () => EmissiveInstance(new Color(1f, 0.92f, 0.75f), Color.black));
        public static Material DockBulb => Get("bulb_dock", () => EmissiveInstance(new Color(1f, 0.9f, 0.7f), Color.black));
        public static Material PadBulb => Get("bulb_pad", () => EmissiveInstance(new Color(0.95f, 0.88f, 0.55f), Color.black));
        public static Material FireGlow => Get("fire_glow", () => EmissiveInstance(PropPal.CoralOrange, Color.black));
        public static Material Lens => Get("lens", () => EmissiveInstance(PropPal.LampGlass, Color.black));
        public static Material MushroomGlow => Get("mushroom", () => EmissiveInstance(PropPal.Mushroom, new Color(0.35f, 1.1f, 0.25f)));
        public static Material LanternGlow => Get("lantern_glow", () => EmissiveInstance(new Color(1f, 0.85f, 0.55f), new Color(2.4f, 1.6f, 0.7f)));
        public static Material Sparkle => Get("pickup_sparkle", () => EmissiveInstance(PropPal.Gold, new Color(0.55f, 0.42f, 0.08f)));

        // ================================================================== underwater colors (art bible §2.4)
        // One shared reef palette (8 colors); every coral family indexes into it so a reef chunk needs few materials.
        static readonly Color[] Reef =
        {
            PropPal.CoralPink,      // 0 pink
            PropPal.CoralOrange,    // 1 orange
            PropPal.CoralPurple,    // 2 purple
            PropPal.CoralYellow,    // 3 yellow
            PropPal.CoralRed,       // 4 red
            PropPal.CoralLilac,     // 5 lilac
            PropPal.CoralTeal,      // 6 teal
            PropPal.CoralOlive,     // 7 olive green
        };

        static int Wrap(int i, int n) => ((i % n) + n) % n;
        static Material ReefMat(int[] map, int i) => Flat(Reef[map[Wrap(i, map.Length)]]);

        static readonly int[] CoralMap = { 0, 1, 2, 3, 4, 5 };
        static readonly int[] BrainMap = { 3, 7, 1, 6 };
        static readonly int[] TableMap = { 6, 5, 2 };
        static readonly int[] FanMap = { 2, 4, 3, 5 };
        static readonly int[] SpongeMap = { 2, 1, 3, 4 };
        static readonly int[] WhipMap = { 4, 3, 2 };

        public static int ReefCount => Reef.Length;
        public static Color ReefColor(int i) => Reef[Wrap(i, Reef.Length)];
        /// Darker base tone (−25 %) and lighter tip tone (+15 %) of a reef color: corals are colonies in 2–3 flat tones.
        public static Material ReefBase(Color c) => Flat(Value(c, 0.75f));
        public static Material ReefTip(Color c) => Flat(Value(c, 1.15f));

        public static int CoralCount => CoralMap.Length;
        public static Material Coral(int i) => ReefMat(CoralMap, i);
        public static Color CoralColor(int i) => Reef[CoralMap[Wrap(i, CoralMap.Length)]];
        public static int BrainCount => BrainMap.Length;
        public static Material Brain(int i) => ReefMat(BrainMap, i);
        public static Color BrainColor(int i) => Reef[BrainMap[Wrap(i, BrainMap.Length)]];
        public static Material Table(int i) => ReefMat(TableMap, i);
        public static Color TableColor(int i) => Reef[TableMap[Wrap(i, TableMap.Length)]];
        public static int FanCount => FanMap.Length;
        public static Material Fan(int i) => ReefMat(FanMap, i);
        public static int SpongeCount => SpongeMap.Length;
        public static Material Sponge(int i) => ReefMat(SpongeMap, i);
        public static Color SpongeColor(int i) => Reef[SpongeMap[Wrap(i, SpongeMap.Length)]];
        public static Material SpongeInside => Flat(Shade(PropPal.DeepRock, -0.1f));
        public static int WhipCount => WhipMap.Length;
        public static Material Whip(int i) => ReefMat(WhipMap, i);

        public static Material AnemoneBody => Flat(Reef[5]);
        public static Material AnemoneTentacle => Flat(Shade(PropPal.CoralPink, 0.06f, -0.12f));
        public static Material SeaGrass => Flat(PropPal.SeaGrass);
        public static Material SeaGrassDark => Flat(PropPal.KelpDark);
        public static Material Kelp => Flat(PropPal.Kelp);
        public static Material KelpDark => Flat(PropPal.KelpDark);
        public static Material ReefRock => Flat(PropPal.ReefRock);
        public static Material ReefRockDark => Flat(Value(PropPal.ReefRock, 0.82f));

        public static Material HullUpper => Flat(PropPal.HullBlue);
        public static Material HullLower => Flat(PropPal.HullRust);
        public static Material Deck => Flat(PropPal.WreckDeck);
        public static Material Superstructure => Flat(Shade(PropPal.White, -0.16f, 0.02f));
        public static Material Crane => Flat(Shade(PropPal.Sun, -0.2f, -0.08f));
        public static Material Funnel => Flat(PropPal.LighthouseRed);

        static readonly Color[] ContainerColors =
        {
            Value(PropPal.LighthouseRed, 0.85f), Value(PropPal.HullBlue, 1.1f), Value(PropPal.FoliageDark, 0.95f), Value(PropPal.CoralOrange, 0.8f),
        };
        public static int ContainerCount => ContainerColors.Length;
        public static Material Container(int i) => Flat(ContainerColors[Wrap(i, ContainerColors.Length)]);
        public static Material ContainerEnd(int i) => Flat(Value(ContainerColors[Wrap(i, ContainerColors.Length)], 0.88f));

        public static Material HoleDark => Flat(PropPal.Porthole);
        public static Material NamePlate => Get("nameplate", () => Textured(PropTex.NamePlate));
        public static Material Bone => Flat(Shade(PropPal.White, -0.07f, 0.06f));
        public static Material Chimney => Flat(Shade(PropPal.DeepRock, -0.12f));
        public static Material VentGlow => Get("vent_glow", () => EmissiveInstance(new Color(0.35f, 0.1f, 0.03f), new Color(2.4f, 0.75f, 0.15f)));
        public static Material Bio => Get("bio", () => EmissiveInstance(new Color(0.05f, 0.2f, 0.25f), new Color(0.2f, 1.6f, 2.2f)));
        public static Material BioGreen => Get("bio_green", () => EmissiveInstance(new Color(0.05f, 0.25f, 0.1f), new Color(0.4f, 2.0f, 0.6f)));
        public static Material TubeWorm => Flat(PropPal.White);
        public static Material WormPlume => Get("worm_plume", () => EmissiveInstance(new Color(0.85f, 0.12f, 0.1f), new Color(0.4f, 0.03f, 0.02f)));
        public static Material BuoyOrange => Flat(PropPal.SuitOrange);
        public static Material BuoyWhite => Flat(PropPal.White);

        /// Flag in a dive site's map color (two-sided geometry, so a plain material).
        public static Material Flag(Color c) => Flat(c);

        // ================================================================== prewarm
        /// Creates the island materials (cheap: flat colors + a few tiny block textures).
        public static IEnumerator PrewarmIsland()
        {
            Func<object>[] steps =
            {
                () => Wood, () => WoodLight, () => WoodDark, () => Wall, () => Roof, () => PalmLeaf, () => PalmTrunk,
                () => Grass, () => Rock, () => Concrete, () => Helipad, () => Sign, () => Poster, () => HutBulb,
                () => DockBulb, () => PadBulb, () => FireGlow, () => Lens, () => Glass, () => LaptopScreen,
                () => GrassRamp, () => DuneRamp, () => PoolWater, () => FallWater, () => FallFoam, () => MushroomGlow,
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
