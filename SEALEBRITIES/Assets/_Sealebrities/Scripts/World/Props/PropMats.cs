using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Shared materials of the props module (created lazily, cached, shared between all props so batches stay small).
    /// UV conventions: "world" materials expect UVs in meters (tiling = 1 / texture size in meters).
    public static class PropMats
    {
        static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Cache.Clear(); }

        static Material Get(string k, Func<Material> make)
        {
            if (Cache.TryGetValue(k, out var m) && m != null) return m;
            m = make();
            m.name = "Prop_" + k;
            Cache[k] = m;
            return m;
        }

        static Material Tex(Texture2D a, Color tint, float smooth, float metal, float tileMeters, Texture2D n = null, float bump = 1f)
        {
            var m = MaterialLibrary.Textured(a, tint, smooth, metal, new Vector2(1f / tileMeters, 1f / tileMeters), n);
            if (n != null && m.HasProperty("_BumpScale")) m.SetFloat("_BumpScale", bump);
            return m;
        }

        static Material Plain(Color c, float smooth, float metal = 0f)
        {
            var m = new Material(MaterialLibrary.Lit);
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            m.enableInstancing = true;
            return m;
        }

        static Material TwoSided(Material m) { MaterialLibrary.MakeTwoSided(m); return m; }

        public static void AlphaClip(Material m, float cutoff)
        {
            m.SetFloat("_AlphaClip", 1f);
            m.SetFloat("_Cutoff", cutoff);
            m.EnableKeyword("_ALPHATEST_ON");
            m.SetOverrideTag("RenderType", "TransparentCutout");
            m.renderQueue = (int)RenderQueue.AlphaTest;
        }

        /// New emissive material instance (not cached) so its emission can be animated (night lights).
        public static Material EmissiveInstance(Color baseColor, Color emission)
        {
            var m = new Material(MaterialLibrary.Lit) { name = "Prop_Emissive" };
            m.SetColor("_BaseColor", baseColor);
            m.SetFloat("_Smoothness", 0.6f);
            m.SetColor("_EmissionColor", emission);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            return m;
        }

        public static void SetEmission(Material m, Color c)
        {
            if (m != null) m.SetColor("_EmissionColor", c);
        }

        static Material Emissive(Color baseColor, Color emission) => EmissiveInstance(baseColor, emission);

        // ================================================================== island
        public static Material Wood => Get("wood", () => Tex(PropTex.Planks, Color.white, 0.12f, 0f, 2f, PropTex.PlanksN, 0.8f));
        public static Material WoodDark => Get("wood_dark", () => Tex(PropTex.Planks, new Color(0.58f, 0.48f, 0.4f), 0.15f, 0f, 2f, PropTex.PlanksN, 0.8f));
        public static Material WoodWet => Get("wood_wet", () => Tex(PropTex.Planks, new Color(0.3f, 0.34f, 0.24f), 0.4f, 0f, 2f, PropTex.PlanksN, 1f));
        public static Material WoodPale => Get("wood_pale", () => Tex(PropTex.Planks, new Color(1.08f, 1.0f, 0.9f), 0.2f, 0f, 1.2f, PropTex.PlanksN, 0.6f));
        public static Material Roof => Get("roof", () => Tex(PropTex.Corrugated, Color.white, 0.35f, 0.25f, 2f, PropTex.CorrugatedN, 1f));
        public static Material Bark => Get("bark", () => MaterialLibrary.Textured(PropTex.Bark, Color.white, 0.12f, 0f, new Vector2(1f, 1f), PropTex.BarkN));
        public static Material BarkCharred => Get("bark_charred", () => MaterialLibrary.Textured(PropTex.Bark, new Color(0.25f, 0.22f, 0.2f), 0.1f, 0f, new Vector2(1f, 1f), PropTex.BarkN));

        public static Material PalmLeaf => Get("palm_leaf", () =>
        {
            var m = MaterialLibrary.Textured(PropTex.PalmLeaf, Color.white, 0.28f, 0f, Vector2.one);
            AlphaClip(m, 0.42f);
            return m;
        });

        public static Material PalmLeafDry => Get("palm_leaf_dry", () =>
        {
            var m = MaterialLibrary.Textured(PropTex.PalmLeafDry, Color.white, 0.15f, 0f, Vector2.one);
            AlphaClip(m, 0.42f);
            return m;
        });

        public static Material FernLeaf => Get("fern_leaf", () =>
        {
            var m = MaterialLibrary.Textured(PropTex.FernLeaf, Color.white, 0.25f, 0f, Vector2.one);
            AlphaClip(m, 0.42f);
            return TwoSided(m);
        });

        public static Material BushLeaf => Get("bush_leaf", () =>
        {
            var m = MaterialLibrary.Textured(PropTex.LeafClump, Color.white, 0.3f, 0f, Vector2.one);
            AlphaClip(m, 0.45f);
            return TwoSided(m);
        });

        public static Material BigLeaf => Get("big_leaf", () => TwoSided(MaterialLibrary.Textured(PropTex.BigLeaf, Color.white, 0.5f, 0f, Vector2.one)));
        public static Material Stem => Get("stem", () => Plain(new Color(0.24f, 0.36f, 0.12f), 0.3f));
        public static Material Coconut => Get("coconut", () => Plain(new Color(0.32f, 0.3f, 0.12f), 0.35f));
        public static Material FlowerRed => Get("flower_red", () => Plain(new Color(0.86f, 0.12f, 0.08f), 0.45f));
        public static Material FlowerOrange => Get("flower_orange", () => Plain(new Color(0.98f, 0.55f, 0.08f), 0.45f));
        public static Material FlowerPink => Get("flower_pink", () => Plain(new Color(0.92f, 0.35f, 0.6f), 0.45f));

        public static Material Rock => Get("rock", () => Tex(PropTex.Rock, new Color(0.66f, 0.62f, 0.56f), 0.18f, 0f, 3f, PropTex.RockN, 1.2f));
        public static Material RockDark => Get("rock_dark", () => Tex(PropTex.Rock, new Color(0.34f, 0.32f, 0.31f), 0.22f, 0f, 3f, PropTex.RockN, 1.3f));
        public static Material RockLight => Get("rock_light", () => Tex(PropTex.Rock, new Color(0.86f, 0.83f, 0.74f), 0.2f, 0f, 2.5f, PropTex.RockN, 1.1f));
        public static Material Basalt => Get("basalt", () => Tex(PropTex.Rock, new Color(0.17f, 0.17f, 0.18f), 0.3f, 0f, 4f, PropTex.RockN, 1.6f));

        public static Material Concrete => Get("concrete", () => Tex(PropTex.Concrete, Color.white, 0.15f, 0f, 2f, PropTex.ConcreteN, 0.8f));
        public static Material Helipad => Get("helipad", () => MaterialLibrary.Textured(PropTex.Helipad, Color.white, 0.18f, 0f, Vector2.one, PropTex.HelipadN));
        public static Material Lighthouse => Get("lighthouse", () => MaterialLibrary.Textured(PropTex.Lighthouse, Color.white, 0.3f, 0f, Vector2.one));

        public static Material RedMetal => Get("red_metal", () => Tex(PropTex.PaintedMetal("red", new Color(0.62f, 0.1f, 0.08f), 0.3f), Color.white, 0.4f, 0.3f, 1.5f, PropTex.PaintedMetalN("red", new Color(0.62f, 0.1f, 0.08f), 0.3f)));
        public static Material DarkMetal => Get("dark_metal", () => Tex(PropTex.PaintedMetal("dark", new Color(0.12f, 0.12f, 0.13f), 0.25f), Color.white, 0.35f, 0.5f, 1.5f, PropTex.PaintedMetalN("dark", new Color(0.12f, 0.12f, 0.13f), 0.25f)));
        public static Material Metal => Get("metal", () => Tex(PropTex.PaintedMetal("galv", new Color(0.6f, 0.61f, 0.62f), 0.18f), Color.white, 0.45f, 0.6f, 1.5f, PropTex.PaintedMetalN("galv", new Color(0.6f, 0.61f, 0.62f), 0.18f)));
        public static Material Rust => Get("rust", () => Tex(PropTex.PaintedMetal("rust", new Color(0.42f, 0.25f, 0.14f), 0.85f), Color.white, 0.2f, 0.2f, 1.5f, PropTex.PaintedMetalN("rust", new Color(0.42f, 0.25f, 0.14f), 0.85f)));
        public static Material DrumBlue => Get("drum_blue", () => Tex(PropTex.PaintedMetal("drum_blue", new Color(0.1f, 0.28f, 0.58f), 0.45f, 0, true), Color.white, 0.35f, 0.3f, 1.2f));
        public static Material DrumRed => Get("drum_red", () => Tex(PropTex.PaintedMetal("drum_red", new Color(0.68f, 0.12f, 0.08f), 0.35f, 0, true), Color.white, 0.35f, 0.3f, 1.2f));
        public static Material DrumYellow => Get("drum_yellow", () => Tex(PropTex.PaintedMetal("drum_yellow", new Color(0.85f, 0.62f, 0.1f), 0.4f, 0, true), Color.white, 0.35f, 0.3f, 1.2f));

        public static Material Rubber => Get("rubber", () => Plain(new Color(0.06f, 0.06f, 0.065f), 0.28f));
        public static Material Rope => Get("rope", () => Tex(PropTex.Fabric("rope", new Color(0.66f, 0.56f, 0.4f), new Color(0.55f, 0.46f, 0.32f), 8, false), Color.white, 0.08f, 0f, 0.3f));
        public static Material FabricStripe => Get("fab_stripe", () => Tex(PropTex.Fabric("stripe", new Color(0.92f, 0.92f, 0.88f), new Color(0.1f, 0.45f, 0.62f), 6, true), Color.white, 0.12f, 0f, 1f));
        public static Material FabricSock => Get("fab_sock", () =>
        {
            var m = MaterialLibrary.Textured(PropTex.Fabric("sock", new Color(0.96f, 0.42f, 0.08f), new Color(0.95f, 0.95f, 0.92f), 5, false), Color.white, 0.15f, 0f, Vector2.one);
            return TwoSided(m);
        });
        public static Material FabricBlanket => Get("fab_blanket", () => Tex(PropTex.Fabric("blanket", new Color(0.62f, 0.14f, 0.12f), new Color(0.88f, 0.82f, 0.68f), 10, false), Color.white, 0.1f, 0f, 1f));
        public static Material FabricMattress => Get("fab_mattress", () => Tex(PropTex.Fabric("mattress", new Color(0.86f, 0.86f, 0.82f), new Color(0.72f, 0.76f, 0.8f), 12, true), Color.white, 0.1f, 0f, 1f));
        public static Material FabricChair => Get("fab_chair", () => Tex(PropTex.Fabric("chair", new Color(0.24f, 0.38f, 0.3f), new Color(0.2f, 0.33f, 0.26f), 0, false), Color.white, 0.1f, 0f, 0.6f));
        public static Material FabricRug => Get("fab_rug", () => Tex(PropTex.Fabric("rug", new Color(0.7f, 0.42f, 0.18f), new Color(0.3f, 0.2f, 0.32f), 7, true), Color.white, 0.05f, 0f, 2f));
        public static Material Parasol => Get("parasol", () => TwoSided(Tex(PropTex.Fabric("parasol", new Color(0.95f, 0.85f, 0.25f), new Color(0.95f, 0.95f, 0.9f), 8, true), Color.white, 0.15f, 0f, 1f)));
        public static Material PlasticWhite => Get("plastic_white", () => Plain(new Color(0.88f, 0.88f, 0.85f), 0.55f));
        public static Material PlasticBeige => Get("plastic_beige", () => Plain(new Color(0.62f, 0.57f, 0.48f), 0.45f));
        public static Material Surfboard => Get("surfboard", () => Tex(PropTex.Fabric("surf", new Color(0.95f, 0.94f, 0.9f), new Color(0.1f, 0.6f, 0.62f), 3, false), Color.white, 0.75f, 0f, 1.2f));
        public static Material Screen => Get("screen", () => Plain(new Color(0.05f, 0.07f, 0.06f), 0.92f));
        public static Material LaptopScreen => Get("laptop_screen", () => Emissive(new Color(0.1f, 0.12f, 0.16f), new Color(0.25f, 0.45f, 0.7f)));
        public static Material Sign => Get("sign", () => MaterialLibrary.Textured(PropTex.HutSign, Color.white, 0.18f, 0f, Vector2.one));
        public static Material Poster => Get("poster", () => MaterialLibrary.Textured(PropTex.Poster, Color.white, 0.1f, 0f, Vector2.one));
        public static Material Ash => Get("ash", () => Plain(new Color(0.12f, 0.115f, 0.11f), 0.05f));
        public static Material Gold => Get("gold", () => Plain(new Color(1f, 0.77f, 0.32f), 0.82f, 1f));
        public static Material Glass => Get("glass", () =>
        {
            var m = new Material(MaterialLibrary.Lit);
            m.SetColor("_BaseColor", new Color(0.75f, 0.85f, 0.9f, 0.22f));
            m.SetFloat("_Smoothness", 0.95f);
            MaterialLibrary.MakeTransparent(m, false);
            return m;
        });

        // animated emissive instances (one per light group)
        public static Material HutBulb => Get("bulb_hut", () => Emissive(new Color(1f, 0.92f, 0.75f), Color.black));
        public static Material DockBulb => Get("bulb_dock", () => Emissive(new Color(1f, 0.9f, 0.7f), Color.black));
        public static Material PadBulb => Get("bulb_pad", () => Emissive(new Color(0.9f, 0.85f, 0.6f), Color.black));
        public static Material FireGlow => Get("fire_glow", () => Emissive(new Color(0.2f, 0.08f, 0.03f), Color.black));
        public static Material Lens => Get("lens", () => Emissive(new Color(0.95f, 0.92f, 0.8f), Color.black));

        // ================================================================== underwater
        static readonly Color[] CoralColors =
        {
            new Color(0.58f, 0.32f, 0.68f), new Color(0.76f, 0.8f, 0.36f), new Color(0.92f, 0.5f, 0.6f),
            new Color(0.36f, 0.56f, 0.82f), new Color(0.96f, 0.56f, 0.26f), new Color(0.56f, 0.42f, 0.28f),
        };

        public static int CoralCount => CoralColors.Length;

        public static Material Coral(int i)
        {
            i = ((i % CoralColors.Length) + CoralColors.Length) % CoralColors.Length;
            return Get("coral" + i, () => Tex(PropTex.Pores, CoralColors[i], 0.32f, 0f, 0.5f, PropTex.PoresN, 0.8f));
        }

        static readonly Color[] BrainColors = { new Color(0.74f, 0.63f, 0.36f), new Color(0.5f, 0.57f, 0.33f), new Color(0.82f, 0.56f, 0.5f) };
        public static int BrainCount => BrainColors.Length;
        public static Material Brain(int i)
        {
            i = ((i % BrainColors.Length) + BrainColors.Length) % BrainColors.Length;
            return Get("brain" + i, () => MaterialLibrary.Textured(PropTex.Brain, BrainColors[i], 0.3f, 0f, new Vector2(1.5f, 1.5f), PropTex.BrainN));
        }

        static readonly Color[] TableColors = { new Color(0.56f, 0.74f, 0.67f), new Color(0.8f, 0.66f, 0.5f) };
        public static Material Table(int i)
        {
            i = ((i % TableColors.Length) + TableColors.Length) % TableColors.Length;
            return Get("table" + i, () => Tex(PropTex.Pores, TableColors[i], 0.3f, 0f, 0.6f, PropTex.PoresN, 0.8f));
        }

        static readonly Color[] FanColors = { new Color(0.62f, 0.26f, 0.68f), new Color(0.88f, 0.26f, 0.2f), new Color(0.96f, 0.76f, 0.26f) };
        public static int FanCount => FanColors.Length;
        public static Material Fan(int i)
        {
            i = ((i % FanColors.Length) + FanColors.Length) % FanColors.Length;
            return Get("fan" + i, () =>
            {
                var m = MaterialLibrary.Textured(PropTex.Lattice, FanColors[i], 0.25f, 0f, new Vector2(1.6f, 1.6f));
                AlphaClip(m, 0.4f);
                return TwoSided(m);
            });
        }

        static readonly Color[] SpongeColors = { new Color(0.52f, 0.2f, 0.56f), new Color(0.96f, 0.5f, 0.16f), new Color(0.95f, 0.84f, 0.3f) };
        public static int SpongeCount => SpongeColors.Length;
        public static Material Sponge(int i)
        {
            i = ((i % SpongeColors.Length) + SpongeColors.Length) % SpongeColors.Length;
            return Get("sponge" + i, () => Tex(PropTex.Sponge, SpongeColors[i], 0.25f, 0f, 0.6f, PropTex.SpongeN, 1f));
        }

        static readonly Color[] WhipColors = { new Color(0.85f, 0.2f, 0.16f), new Color(0.95f, 0.78f, 0.25f), new Color(0.6f, 0.3f, 0.7f) };
        public static int WhipCount => WhipColors.Length;
        public static Material Whip(int i)
        {
            i = ((i % WhipColors.Length) + WhipColors.Length) % WhipColors.Length;
            return Get("whip" + i, () => Plain(WhipColors[i], 0.35f));
        }

        public static Material AnemoneBody => Get("anemone_body", () => Plain(new Color(0.78f, 0.56f, 0.46f), 0.45f));
        public static Material AnemoneTentacle => Get("anemone_tentacle", () =>
        {
            var m = Plain(new Color(0.95f, 0.62f, 0.8f), 0.55f);
            m.SetColor("_EmissionColor", new Color(0.08f, 0.03f, 0.06f));
            m.EnableKeyword("_EMISSION");
            return m;
        });
        public static Material SeaGrass => Get("seagrass", () => TwoSided(Plain(new Color(0.26f, 0.46f, 0.16f), 0.3f)));
        public static Material Kelp => Get("kelp", () => TwoSided(MaterialLibrary.Textured(PropTex.Kelp, new Color(1f, 0.95f, 0.85f), 0.35f, 0f, Vector2.one)));

        public static Material HullUpper => Get("hull_upper", () =>
            Tex(PropTex.Hull("upper", new Color(0.16f, 0.22f, 0.28f)), Color.white, 0.25f, 0.2f, 4f, PropTex.HullN("upper", new Color(0.16f, 0.22f, 0.28f)), 1.2f));
        public static Material HullLower => Get("hull_lower", () =>
            Tex(PropTex.Hull("lower", new Color(0.46f, 0.12f, 0.08f)), Color.white, 0.25f, 0.2f, 4f, PropTex.HullN("lower", new Color(0.46f, 0.12f, 0.08f)), 1.2f));
        public static Material Deck => Get("deck", () =>
            Tex(PropTex.Hull("deck", new Color(0.36f, 0.3f, 0.22f)), Color.white, 0.2f, 0.1f, 3f, PropTex.HullN("deck", new Color(0.36f, 0.3f, 0.22f)), 1f));
        public static Material Superstructure => Get("superstructure", () =>
            Tex(PropTex.Hull("white", new Color(0.78f, 0.78f, 0.74f)), Color.white, 0.25f, 0.1f, 3f, PropTex.HullN("white", new Color(0.78f, 0.78f, 0.74f)), 1f));
        public static Material Crane => Get("crane", () =>
            Tex(PropTex.Hull("yellow", new Color(0.8f, 0.6f, 0.12f)), Color.white, 0.3f, 0.2f, 3f, PropTex.HullN("yellow", new Color(0.8f, 0.6f, 0.12f)), 1f));
        public static Material Funnel => Get("funnel", () =>
            Tex(PropTex.Hull("funnel", new Color(0.62f, 0.14f, 0.1f)), Color.white, 0.3f, 0.2f, 3f));

        static readonly Color[] ContainerColors = { new Color(0.6f, 0.16f, 0.1f), new Color(0.12f, 0.3f, 0.52f), new Color(0.18f, 0.42f, 0.25f), new Color(0.85f, 0.45f, 0.1f) };
        public static int ContainerCount => ContainerColors.Length;
        public static Material Container(int i)
        {
            i = ((i % ContainerColors.Length) + ContainerColors.Length) % ContainerColors.Length;
            return Get("container" + i, () => Tex(PropTex.PaintedMetal("cont" + i, ContainerColors[i], 0.55f, 14, false), Color.white, 0.3f, 0.25f, 2.6f,
                PropTex.PaintedMetalN("cont" + i, ContainerColors[i], 0.55f, 14, false), 1.2f));
        }

        public static Material HoleDark => Get("hole_dark", () => Plain(new Color(0.012f, 0.014f, 0.016f), 0.05f));
        public static Material NamePlate => Get("nameplate", () => MaterialLibrary.Textured(PropTex.NamePlate, Color.white, 0.25f, 0.1f, Vector2.one));
        public static Material Bone => Get("bone", () => Tex(PropTex.Pores, new Color(0.84f, 0.8f, 0.7f), 0.22f, 0f, 0.8f, PropTex.PoresN, 0.6f));
        public static Material Chimney => Get("chimney", () => Tex(PropTex.Rock, new Color(0.11f, 0.095f, 0.09f), 0.15f, 0f, 2f, PropTex.RockN, 1.8f));
        public static Material VentGlow => Get("vent_glow", () => Emissive(new Color(0.3f, 0.08f, 0.02f), new Color(2.4f, 0.75f, 0.15f)));
        public static Material Bio => Get("bio", () => Emissive(new Color(0.05f, 0.2f, 0.25f), new Color(0.2f, 1.6f, 2.2f)));
        public static Material BioGreen => Get("bio_green", () => Emissive(new Color(0.05f, 0.25f, 0.1f), new Color(0.4f, 2.0f, 0.6f)));
        public static Material TubeWorm => Get("tubeworm", () => Plain(new Color(0.88f, 0.86f, 0.8f), 0.35f));
        public static Material WormPlume => Get("worm_plume", () => Emissive(new Color(0.8f, 0.08f, 0.06f), new Color(0.35f, 0.02f, 0.01f)));
        public static Material BuoyOrange => Get("buoy_orange", () => Plain(new Color(0.98f, 0.42f, 0.06f), 0.55f));
        public static Material BuoyWhite => Get("buoy_white", () => Plain(new Color(0.92f, 0.92f, 0.9f), 0.55f));

        /// Two-sided flag material in a dive site's map color (one per site).
        public static Material Flag(Color c) => Get("flag_" + ColorUtility.ToHtmlStringRGB(c), () => TwoSided(Plain(c, 0.2f)));

        // ================================================================== prewarm
        /// Generates island textures/materials spread over several frames.
        public static IEnumerator PrewarmIsland()
        {
            Func<object>[] steps =
            {
                () => Wood, () => WoodDark, () => WoodWet, () => WoodPale, () => Roof, () => Bark, () => BarkCharred,
                () => PalmLeaf, () => PalmLeafDry, () => FernLeaf, () => BushLeaf, () => BigLeaf,
                () => Rock, () => RockDark, () => RockLight, () => Concrete, () => Helipad, () => Lighthouse,
                () => RedMetal, () => DarkMetal, () => Metal, () => Rust, () => DrumBlue, () => DrumRed, () => DrumYellow,
                () => Rope, () => FabricStripe, () => FabricSock, () => FabricBlanket, () => FabricMattress, () => FabricChair,
                () => FabricRug, () => Parasol, () => Surfboard, () => Sign, () => Poster,
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
                () => Coral(0), () => Brain(0), () => Fan(0), () => Sponge(0), () => Kelp,
                () => HullUpper, () => HullLower, () => Deck, () => Superstructure, () => Crane, () => Funnel,
                () => Container(0), () => Container(1), () => Container(2), () => Container(3), () => NamePlate,
                () => Bone, () => Chimney, () => Basalt, () => RockLight,
            };
            for (int i = 0; i < steps.Length; i++)
            {
                steps[i]();
                if (PropRun.Due()) yield return null;
            }
        }
    }
}
