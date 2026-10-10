using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Materials and procedural textures for the hand camera models in the low-poly toy look: camera bodies, gloves and
    /// trims are flat, matte palette colors (no grain, no gloss, no metal); only screens, REC dot, tape bar, lamps and
    /// glass keep their special materials. Everything is cached and shared between rig rebuilds, so swapping materials
    /// at runtime never allocates.
    public static class FilmMaterials
    {
        public const int ScreenStandby = 0, ScreenLive = 1, ScreenWeak = 2, ScreenGood = 3, ScreenCaptured = 4;

        static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();
        static Texture2D screenTex;
        static Material[] screens, tapes;
        static Material recDot, lampOff, lampOn;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Cache.Clear();
            ViewModelCache.Clear();
            screenTex = null; screens = null; tapes = null; recDot = null; lampOff = null; lampOn = null;
        }

        // ------------------------------------------------------------------ textures
        /// Tiny viewfinder graphic (gradient, rule-of-thirds, corner brackets, center cross) for the device screens.
        public static Texture2D ScreenTexture
        {
            get
            {
                if (screenTex != null) return screenTex;
                const int W = 128, H = 72;
                var px = new Color32[W * H];
                for (int y = 0; y < H; y++)
                {
                    for (int x = 0; x < W; x++)
                    {
                        float fy = y / (float)(H - 1);
                        float v = Mathf.Lerp(0.16f, 0.34f, Mathf.Floor(fy * 4f) / 3f);   // stepped gradient (flat bands)
                        if (x == W / 3 || x == 2 * W / 3 || y == H / 3 || y == 2 * H / 3) v += 0.12f;   // thirds
                        bool cornerX = x < 14 || x >= W - 14, cornerY = y < 10 || y >= H - 10;
                        bool edgeX = x == 4 || x == 5 || x == W - 5 || x == W - 6;
                        bool edgeY = y == 4 || y == 5 || y == H - 5 || y == H - 6;
                        if ((cornerX && cornerY) && ((edgeX && y >= 4 && y < H - 4) || (edgeY && x >= 4 && x < W - 4))) v = 0.95f; // brackets
                        int cx = W / 2, cy = H / 2;
                        if ((Mathf.Abs(x - cx) <= 6 && (y == cy)) || (Mathf.Abs(y - cy) <= 5 && x == cx)) v = 0.9f;               // cross
                        byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
                        px[y * W + x] = new Color32(b, b, b, 255);
                    }
                }
                screenTex = new Texture2D(W, H, TextureFormat.RGBA32, true) { name = "Film_Screen", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                screenTex.SetPixels32(px);
                screenTex.Apply(true, true);
                return screenTex;
            }
        }

        // ------------------------------------------------------------------ view-model queue
        /// Render queue of every opaque camera-model material: still the opaque Lit shader (depth write on), but drawn
        /// in the transparent pass, before the ocean surface (2980) so submerged parts are still tinted by it. That keeps
        /// the model out of the depth-normals prepass: the screen-space AO, whose sample disc is huge at 0.3-0.5 m from
        /// the eye, no longer leaves a blotchy "dirty" pattern on its flat colors.
        public const int ViewModelQueue = 2600;

        /// Cached copy of a shared (world) material on the view-model queue; the shared one is never modified.
        static Material ViewModel(Material shared)
        {
            if (shared == null) return null;
            if (ViewModelCache.TryGetValue(shared, out var m) && m != null) return m;
            m = new Material(shared) { name = shared.name + "_VM" };
            m.renderQueue = ViewModelQueue;
            ViewModelCache[shared] = m;
            return m;
        }

        static readonly Dictionary<Material, Material> ViewModelCache = new Dictionary<Material, Material>();

        // ------------------------------------------------------------------ surfaces
        /// Flat, matte camera surface in the low-poly look. The requested (semi-realistic) color is mapped to a toy
        /// tone by FlatColor; smoothness and tiling are ignored, metallic only picks the light-grey "metal" tone.
        /// Identical colors share one material (a view-model copy of LowPoly.Mat), so all rigs batch together.
        public static Material Surface(Color c, float smoothness, float metallic = 0f, float tiling = 1f)
            => ViewModel(LowPoly.Mat(FlatColor(c, metallic)));

        /// Like Surface but rendered from both sides (open shells such as a matte box).
        public static Material SurfaceTwoSided(Color c, float smoothness, float metallic = 0f)
        {
            Color flat = FlatColor(c, metallic);
            string key = $"d{flat.r:F3}{flat.g:F3}{flat.b:F3}";
            if (Cache.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(MaterialLibrary.Lit) { name = "Film_FlatTwoSided" };
            m.SetColor("_BaseColor", flat);
            m.SetFloat("_Smoothness", LowPoly.Smoothness);
            m.SetFloat("_Metallic", 0f);
            MaterialLibrary.MakeTwoSided(m);
            m.enableInstancing = true;
            m.renderQueue = ViewModelQueue;
            Cache[key] = m;
            return m;
        }

        /// Maps a semi-realistic camera color to the flat toy palette: dark neutrals are lifted a bit so the facets
        /// still read, colors get a touch brighter and more saturated, metals become plain light grey.
        public static Color FlatColor(Color c, float metallic)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            if (metallic > 0.5f && s < 0.25f && v > 0.35f)
                return Color.HSVToRGB(h, s * 0.5f, Mathf.Clamp(v * 0.85f + 0.1f, 0.6f, 0.82f));   // chrome / alu
            if (v < 0.3f && s < 0.3f)
                return Color.HSVToRGB(h, Mathf.Min(s, 0.25f), Mathf.Lerp(0.15f, 0.3f, v / 0.3f));    // rubber, neoprene, black plastic
            if (s < 0.15f)
                return Color.HSVToRGB(h, s, Mathf.Max(v, 0.3f));                                       // greys / whites
            return Color.HSVToRGB(h, Mathf.Min(1f, s * 1.08f), Mathf.Lerp(Mathf.Max(v, 0.5f), 1f, 0.2f)); // colors
        }

        /// Transparent two-sided glass/plastic (bags, housings, dome ports).
        public static Material Glass(Color c) => MaterialLibrary.Transparent(c, 0.95f, true);

        public static Material Glow(Color c, float intensity) => ViewModel(MaterialLibrary.Emissive(c * 0.25f, c, intensity));

        static Material MakeEmissive(string name, Color baseColor, Color emission, Texture2D map)
        {
            var m = new Material(MaterialLibrary.Lit) { name = name, renderQueue = ViewModelQueue };
            m.SetColor("_BaseColor", baseColor);
            m.SetFloat("_Smoothness", 0.85f);
            m.SetColor("_EmissionColor", emission);
            if (map != null)
            {
                m.SetTexture("_BaseMap", map);
                m.SetTexture("_EmissionMap", map);
            }
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            return m;
        }

        /// Screen material per viewfinder state (standby, live, weak, good, captured).
        public static Material Screen(int state)
        {
            if (screens == null)
            {
                Color[] tint =
                {
                    new Color(0.35f, 0.45f, 0.55f) * 0.35f,
                    new Color(0.55f, 0.72f, 0.88f) * 0.85f,
                    new Color(1.00f, 0.60f, 0.15f) * 0.95f,
                    new Color(0.25f, 1.00f, 0.50f) * 0.95f,
                    new Color(0.35f, 0.95f, 1.00f) * 1.40f,
                };
                screens = new Material[tint.Length];
                for (int i = 0; i < tint.Length; i++)
                    screens[i] = MakeEmissive("Film_Screen" + i, new Color(0.02f, 0.025f, 0.03f), tint[i], ScreenTexture);
            }
            return screens[Mathf.Clamp(state, 0, screens.Length - 1)];
        }

        /// Storage bar on the screen: 0 = plenty, 1 = low, 2 = almost full.
        public static Material Tape(int state)
        {
            if (tapes == null)
            {
                tapes = new[]
                {
                    MakeEmissive("Film_Tape0", new Color(0.1f, 0.3f, 0.15f), new Color(0.3f, 1f, 0.45f) * 1.2f, null),
                    MakeEmissive("Film_Tape1", new Color(0.3f, 0.2f, 0.05f), new Color(1f, 0.7f, 0.15f) * 1.2f, null),
                    MakeEmissive("Film_Tape2", new Color(0.3f, 0.05f, 0.05f), new Color(1f, 0.2f, 0.15f) * 1.4f, null),
                };
            }
            return tapes[Mathf.Clamp(state, 0, tapes.Length - 1)];
        }

        /// Bright red emissive "REC" dot / tally light.
        public static Material RecDot
        {
            get
            {
                if (recDot == null) recDot = MakeEmissive("Film_RecDot", new Color(0.5f, 0.02f, 0.02f), new Color(1f, 0.08f, 0.05f) * 4f, null);
                return recDot;
            }
        }

        /// Strobe / LED panel diffuser, off and on (on while the player's flashlight is on).
        public static Material LampDiffuser(bool on)
        {
            if (lampOff == null)
            {
                lampOff = Surface(new Color(0.78f, 0.8f, 0.82f), 0.85f);
                lampOn = MakeEmissive("Film_LampOn", new Color(0.95f, 0.96f, 1f), new Color(1f, 0.97f, 0.9f) * 3.2f, null);
            }
            return on ? lampOn : lampOff;
        }
    }
}
