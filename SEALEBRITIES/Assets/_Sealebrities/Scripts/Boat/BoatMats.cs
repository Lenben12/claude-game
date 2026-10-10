using UnityEngine;

namespace Sealebrities
{
    /// Shared materials of the Boat module (boat, helicopter, crate, fishing). Created lazily, reused everywhere.
    public static class BoatMats
    {
        static Material hull, deck, deckDark, cabin, metal, steel, chrome, rubber, rope, glass, cushion, engine, engineGrey,
            drumBlue, drumOrange, lifeOrange, white, navRed, navGreen, navWhite, gauge, line, crate, strap, chute,
            heliYellow, heliRed, heliGlass, heliDark, rotorBlur, smoke, foam, spray, dust, splinter, bobberRed, bobberWhite,
            bucket, cork, rod, pilot, helmet, cooler, coolerLid, tank, padMark;
        static Material[] fish = new Material[3];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            hull = deck = deckDark = cabin = metal = steel = chrome = rubber = rope = glass = cushion = engine = engineGrey = null;
            drumBlue = drumOrange = lifeOrange = white = navRed = navGreen = navWhite = gauge = line = crate = strap = chute = null;
            heliYellow = heliRed = heliGlass = heliDark = rotorBlur = smoke = foam = spray = dust = splinter = bobberRed = bobberWhite = null;
            bucket = cork = rod = pilot = helmet = cooler = coolerLid = tank = padMark = null;
            fish = new Material[3];
        }

        static Material Get(ref Material m, System.Func<Material> make)
        {
            if (m == null) m = make();
            return m;
        }

        public static Material Hull => Get(ref hull, () => MaterialLibrary.Textured(BoatTextures.HullAlbedo, Color.white, 0.4f, 0.05f, null, BoatTextures.HullNormal));
        public static Material Deck => Get(ref deck, () => MaterialLibrary.Textured(BoatTextures.DeckAlbedo, Color.white, 0.18f, 0f, null, BoatTextures.DeckNormal));
        public static Material DeckDark => Get(ref deckDark, () => MaterialLibrary.Textured(BoatTextures.DeckAlbedo, new Color(0.72f, 0.62f, 0.52f), 0.28f, 0f, null, BoatTextures.DeckNormal));
        public static Material Cabin => Get(ref cabin, () => MaterialLibrary.Textured(BoatTextures.CabinAlbedo, Color.white, 0.32f, 0f, null, BoatTextures.CabinNormal));
        public static Material Metal => Get(ref metal, () => MaterialLibrary.Textured(BoatTextures.MetalAlbedo, Color.white, 0.35f, 0.55f, new Vector2(2f, 2f), BoatTextures.MetalNormal));
        public static Material Steel => Get(ref steel, () => MaterialLibrary.Opaque(new Color(0.55f, 0.56f, 0.57f), 0.55f, 0.85f));
        public static Material Chrome => Get(ref chrome, () => MaterialLibrary.Opaque(new Color(0.75f, 0.76f, 0.78f), 0.8f, 0.95f));
        public static Material Rubber => Get(ref rubber, () => MaterialLibrary.Opaque(new Color(0.07f, 0.07f, 0.075f), 0.18f));
        public static Material Rope => Get(ref rope, () => MaterialLibrary.Opaque(new Color(0.62f, 0.55f, 0.4f), 0.1f));
        public static Material Glass => Get(ref glass, () => MaterialLibrary.Transparent(new Color(0.55f, 0.68f, 0.72f, 0.22f), 0.96f, true));
        public static Material Cushion => Get(ref cushion, () => MaterialLibrary.Opaque(new Color(0.42f, 0.08f, 0.07f), 0.45f));
        public static Material Engine => Get(ref engine, () => MaterialLibrary.Opaque(new Color(0.09f, 0.1f, 0.11f), 0.62f, 0.1f));
        public static Material EngineGrey => Get(ref engineGrey, () => MaterialLibrary.Opaque(new Color(0.42f, 0.43f, 0.45f), 0.45f, 0.4f));
        public static Material DrumBlue => Get(ref drumBlue, () => MaterialLibrary.Textured(BoatTextures.DrumBlue, Color.white, 0.35f, 0.3f));
        public static Material DrumOrange => Get(ref drumOrange, () => MaterialLibrary.Textured(BoatTextures.DrumOrange, Color.white, 0.35f, 0.3f));
        public static Material LifeOrange => Get(ref lifeOrange, () => MaterialLibrary.Opaque(new Color(0.95f, 0.35f, 0.05f), 0.4f));
        public static Material White => Get(ref white, () => MaterialLibrary.Opaque(new Color(0.9f, 0.9f, 0.88f), 0.4f));
        public static Material NavRed => Get(ref navRed, () => MaterialLibrary.Emissive(new Color(0.5f, 0.05f, 0.05f), new Color(1f, 0.08f, 0.05f), 3f));
        public static Material NavGreen => Get(ref navGreen, () => MaterialLibrary.Emissive(new Color(0.05f, 0.4f, 0.1f), new Color(0.1f, 1f, 0.25f), 3f));
        public static Material NavWhite => Get(ref navWhite, () => MaterialLibrary.Emissive(new Color(0.8f, 0.8f, 0.75f), new Color(1f, 0.95f, 0.85f), 2.5f));
        public static Material Gauge => Get(ref gauge, () => MaterialLibrary.Emissive(new Color(0.05f, 0.12f, 0.08f), new Color(0.3f, 0.9f, 0.45f), 0.6f));
        public static Material Line => Get(ref line, () => MaterialLibrary.Unlit(new Color(0.92f, 0.92f, 0.86f)));
        public static Material Crate => Get(ref crate, () => MaterialLibrary.Textured(BoatTextures.CrateAlbedo, Color.white, 0.2f));
        public static Material Strap => Get(ref strap, () => MaterialLibrary.Opaque(new Color(0.12f, 0.12f, 0.13f), 0.5f, 0.6f));
        public static Material Chute => Get(ref chute, () =>
        {
            var m = MaterialLibrary.Textured(BoatTextures.Parachute, Color.white, 0.25f);
            MaterialLibrary.MakeTwoSided(m);
            return m;
        });
        public static Material HeliYellow => Get(ref heliYellow, () => MaterialLibrary.Opaque(new Color(0.96f, 0.72f, 0.06f), 0.68f, 0.1f));
        public static Material HeliRed => Get(ref heliRed, () => MaterialLibrary.Opaque(new Color(0.78f, 0.08f, 0.06f), 0.62f, 0.1f));
        public static Material HeliGlass => Get(ref heliGlass, () => MaterialLibrary.Transparent(new Color(0.12f, 0.2f, 0.26f, 0.45f), 0.97f, false));
        public static Material HeliDark => Get(ref heliDark, () => MaterialLibrary.Opaque(new Color(0.12f, 0.12f, 0.13f), 0.4f, 0.5f));
        public static Material RotorBlur => Get(ref rotorBlur, () => MaterialLibrary.Unlit(new Color(0.08f, 0.08f, 0.09f, 0.16f), true));
        public static Material Pilot => Get(ref pilot, () => MaterialLibrary.Opaque(new Color(0.2f, 0.32f, 0.22f), 0.3f));
        public static Material Helmet => Get(ref helmet, () => MaterialLibrary.Opaque(new Color(0.95f, 0.95f, 0.92f), 0.7f));
        public static Material BobberRed => Get(ref bobberRed, () => MaterialLibrary.Opaque(new Color(0.95f, 0.1f, 0.06f), 0.7f));
        public static Material BobberWhite => Get(ref bobberWhite, () => MaterialLibrary.Opaque(new Color(0.95f, 0.95f, 0.93f), 0.7f));
        public static Material Bucket => Get(ref bucket, () => MaterialLibrary.Opaque(new Color(0.12f, 0.36f, 0.72f), 0.55f));
        public static Material Cork => Get(ref cork, () => MaterialLibrary.Opaque(new Color(0.6f, 0.45f, 0.28f), 0.15f));
        public static Material Rod => Get(ref rod, () => MaterialLibrary.Opaque(new Color(0.1f, 0.1f, 0.12f), 0.75f, 0.2f));
        public static Material Cooler => Get(ref cooler, () => MaterialLibrary.Opaque(new Color(0.88f, 0.88f, 0.85f), 0.45f));
        public static Material CoolerLid => Get(ref coolerLid, () => MaterialLibrary.Opaque(new Color(0.1f, 0.42f, 0.75f), 0.45f));
        public static Material Tank => Get(ref tank, () => MaterialLibrary.Opaque(new Color(0.95f, 0.78f, 0.1f), 0.6f, 0.3f));
        public static Material PadMark => Get(ref padMark, () => MaterialLibrary.Opaque(new Color(0.9f, 0.9f, 0.86f), 0.3f));

        public static Material Fish(int kind)
        {
            kind = Mathf.Clamp(kind, 0, 2);
            if (fish[kind] == null) fish[kind] = MaterialLibrary.Textured(BoatTextures.Fish(kind), Color.white, 0.78f, 0.25f);
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
