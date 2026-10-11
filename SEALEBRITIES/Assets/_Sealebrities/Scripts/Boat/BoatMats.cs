using UnityEngine;

namespace Sealebrities
{
    /// Shared materials of the Boat module (boat, helicopter, crate, fishing). Low-poly look: solid, matte, flat colors
    /// from the LowPoly palette (no textures). Opaque ones come from MaterialLibrary's cache, so identical colors batch
    /// with the rest of the world. Created lazily, reused everywhere.
    public static class BoatMats
    {
        // ------------------------------------------------------------------ boat colors
        // Art bible (How to Fish): medium-key, slightly desaturated paint, violet-leaning darks, no neon, no pure white.
        public static readonly Color HullTealColor = LowPoly.Hex("#3A9A98");
        public static readonly Color HullBottomColor = LowPoly.Hex("#87403A");
        public static readonly Color RubberColor = LowPoly.Hex("#2E2C38");
        public static readonly Color EngineColor = LowPoly.Hex("#363646");
        public static readonly Color DrumBlueColor = LowPoly.Hex("#3D7BC4");
        public static readonly Color DrumOrangeColor = LowPoly.Hex("#EE7F2D");
        public static readonly Color HeliYellowColor = LowPoly.Hex("#F2C94C");
        public static readonly Color HeliRedColor = LowPoly.Hex("#D0503F");

        static Material hullTeal, hullBottom, hullRust, hullWhite, deck, deckB, deckC, deckDark, cabin, roof, frame, metal, steel,
            rubber, rope, glass, cushion, engine, engineGrey, drumBlue, drumOrange, lifeOrange, white, navRed, navGreen, navWhite,
            gauge, line, crate, crateDark, crateBand, strap, chuteRed, chuteWhite, heliYellow, heliRed, heliGlass, heliDark,
            rotorBlur, smoke, foam, spray, dust, splinter, bobberRed, bobberWhite, bucket, bucketWater, cork, rod, pilot, helmet,
            cooler, coolerLid, tank, tackle, ink;
        static Material[][] fish = new Material[3][];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            hullTeal = hullBottom = hullRust = hullWhite = deck = deckB = deckC = deckDark = cabin = roof = frame = metal = steel = null;
            rubber = rope = glass = cushion = engine = engineGrey = drumBlue = drumOrange = lifeOrange = white = navRed = navGreen = navWhite = null;
            gauge = line = crate = crateDark = crateBand = strap = chuteRed = chuteWhite = heliYellow = heliRed = heliGlass = heliDark = null;
            rotorBlur = smoke = foam = spray = dust = splinter = bobberRed = bobberWhite = bucket = bucketWater = cork = rod = pilot = helmet = null;
            cooler = coolerLid = tank = tackle = ink = null;
            fish = new Material[3][];
        }

        static Material Get(ref Material m, System.Func<Material> make)
        {
            if (m == null) m = make();
            return m;
        }

        static Material Flat(ref Material m, Color c)
        {
            if (m == null) m = LowPoly.Mat(c);
            return m;
        }

        // ------------------------------------------------------------------ boat
        public static Material HullTeal => Flat(ref hullTeal, HullTealColor);
        public static Material HullBottom => Flat(ref hullBottom, HullBottomColor);
        public static Material HullRust => Flat(ref hullRust, LowPoly.Rust);
        public static Material HullWhite => Flat(ref hullWhite, LowPoly.White);
        public static Material Deck => Flat(ref deck, LowPoly.Wood);
        public static Material DeckB => Flat(ref deckB, LowPoly.Hex("#A88466"));
        public static Material DeckC => Flat(ref deckC, LowPoly.Hex("#8A6A50"));
        public static Material DeckDark => Flat(ref deckDark, LowPoly.WoodDark);
        public static Material Cabin => Flat(ref cabin, LowPoly.White);
        public static Material Roof => Flat(ref roof, LowPoly.Roof);
        public static Material Frame => Flat(ref frame, HullTealColor);
        public static Material Metal => Flat(ref metal, LowPoly.Metal);
        public static Material Steel => Flat(ref steel, LowPoly.Hex("#A3A9B2"));
        /// Kept for older call sites: no shiny chrome in the low-poly look, just light steel.
        public static Material Chrome => Steel;
        public static Material Rubber => Flat(ref rubber, RubberColor);
        public static Material Rope => Flat(ref rope, LowPoly.Hex("#C9B48A"));
        public static Material Glass => Get(ref glass, () => MaterialLibrary.Transparent(new Color(0.5f, 0.61f, 0.72f, 0.35f), 0.55f, true));
        public static Material Cushion => Flat(ref cushion, LowPoly.Hex("#C8584A"));
        public static Material Engine => Flat(ref engine, EngineColor);
        public static Material EngineGrey => Flat(ref engineGrey, LowPoly.Hex("#8A929B"));
        public static Material DrumBlue => Flat(ref drumBlue, DrumBlueColor);
        public static Material DrumOrange => Flat(ref drumOrange, DrumOrangeColor);
        public static Material LifeOrange => Flat(ref lifeOrange, LowPoly.SuitOrange);
        public static Material White => Flat(ref white, LowPoly.White);
        public static Material Ink => Flat(ref ink, LowPoly.Ink);
        public static Material NavRed => Get(ref navRed, () => MaterialLibrary.Emissive(new Color(0.6f, 0.08f, 0.06f), new Color(1f, 0.1f, 0.06f), 3f));
        public static Material NavGreen => Get(ref navGreen, () => MaterialLibrary.Emissive(new Color(0.08f, 0.5f, 0.15f), new Color(0.15f, 1f, 0.3f), 3f));
        public static Material NavWhite => Get(ref navWhite, () => MaterialLibrary.Emissive(new Color(0.9f, 0.9f, 0.85f), new Color(1f, 0.95f, 0.85f), 2.5f));
        public static Material Gauge => Get(ref gauge, () => MaterialLibrary.Emissive(new Color(0.1f, 0.3f, 0.18f), new Color(0.35f, 1f, 0.5f), 0.6f));
        public static Material Line => Get(ref line, () => MaterialLibrary.Unlit(new Color(0.96f, 0.96f, 0.92f)));
        public static Material Cooler => Flat(ref cooler, LowPoly.White);
        public static Material CoolerLid => Flat(ref coolerLid, DrumBlueColor);
        public static Material Tank => Flat(ref tank, LowPoly.CoralYellow);
        public static Material Tackle => Flat(ref tackle, LowPoly.Hex("#4E8A55"));

        // ------------------------------------------------------------------ crate + parachute
        public static Material Crate => Flat(ref crate, LowPoly.Wood);
        public static Material CrateDark => Flat(ref crateDark, LowPoly.WoodDark);
        public static Material CrateBand => Flat(ref crateBand, LowPoly.SuitOrange);
        public static Material Strap => Flat(ref strap, LowPoly.Ink);
        public static Material ChuteRed => Flat(ref chuteRed, LowPoly.Hex("#D9504A"));
        public static Material ChuteWhite => Flat(ref chuteWhite, LowPoly.White);

        // ------------------------------------------------------------------ helicopter
        public static Material HeliYellow => Flat(ref heliYellow, HeliYellowColor);
        public static Material HeliRed => Flat(ref heliRed, HeliRedColor);
        public static Material HeliGlass => Get(ref heliGlass, () => MaterialLibrary.Transparent(new Color(0.46f, 0.6f, 0.74f, 0.55f), 0.6f, false));
        public static Material HeliDark => Flat(ref heliDark, EngineColor);
        public static Material RotorBlur => Get(ref rotorBlur, () => MaterialLibrary.Unlit(new Color(0.2f, 0.21f, 0.24f, 0.14f), true));
        public static Material Pilot => Flat(ref pilot, LowPoly.Hex("#5E8A3A"));
        public static Material Helmet => Flat(ref helmet, LowPoly.White);

        // ------------------------------------------------------------------ fishing
        public static Material BobberRed => Flat(ref bobberRed, LowPoly.Hex("#E0503F"));
        public static Material BobberWhite => Flat(ref bobberWhite, LowPoly.White);
        public static Material Bucket => Flat(ref bucket, DrumBlueColor);
        public static Material BucketWater => Get(ref bucketWater, () => MaterialLibrary.Transparent(new Color(LowPoly.Water.r, LowPoly.Water.g, LowPoly.Water.b, 0.8f), 0.4f));
        public static Material Cork => Flat(ref cork, LowPoly.Hex("#C8955E"));
        public static Material Rod => Flat(ref rod, LowPoly.Hex("#2E3140"));

        static readonly string[] FishBack = { "#2F8590", "#4B78B6", "#9C98A8" };
        static readonly string[] FishFlank = { "#7FC1C2", "#9DB8DA", "#D2B6B4" };
        static readonly string[] FishBelly = { "#EEF3F0", "#F2F4F6", "#F4DCCF" };

        /// Fish materials per kind (0 mackerel, 1 sardine, 2 sea bream), matching FishingVisuals.FishMesh submeshes:
        /// 0 back + fins, 1 flanks, 2 belly, 3 eyes.
        public static Material[] FishMats(int kind)
        {
            kind = Mathf.Clamp(kind, 0, 2);
            if (fish[kind] == null)
                fish[kind] = new[]
                {
                    LowPoly.Mat(LowPoly.Hex(FishBack[kind])), LowPoly.Mat(LowPoly.Hex(FishFlank[kind])),
                    LowPoly.Mat(LowPoly.Hex(FishBelly[kind])), LowPoly.Mat(LowPoly.Ink)
                };
            return fish[kind];
        }

        // ------------------------------------------------------------------ particles
        static Material Particle(Texture2D tex, string name)
        {
            var m = new Material(MaterialLibrary.ParticlesUnlit) { name = name };
            m.SetTexture("_BaseMap", tex);
            m.SetTexture("_MainTex", tex);
            m.SetColor("_BaseColor", Color.white);
            MaterialLibrary.MakeTransparent(m, true);
            return m;
        }

        public static Material Smoke => Get(ref smoke, () => Particle(BoatTextures.Soft, "SB_BoatSmoke"));
        public static Material Foam => Get(ref foam, () => Particle(BoatTextures.Foam, "SB_BoatFoam"));
        public static Material Spray => Get(ref spray, () => Particle(BoatTextures.Soft, "SB_BoatSpray"));
        public static Material Dust => Get(ref dust, () => Particle(BoatTextures.Soft, "SB_BoatDust"));
        public static Material Splinter => Get(ref splinter, () => Particle(BoatTextures.Soft, "SB_BoatSplinter"));
    }
}
