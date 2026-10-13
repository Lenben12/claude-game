using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Round 3 (GDD §19 "realistischere Wolken"): layered, faceted low-poly cumulus in the How to Fish style – a wide
    /// base of flattened puffs on a flat bottom, a smaller middle layer and a cauliflower top (2–3 stacked layers), flat
    /// shaded facets. Colours are computed per facet on the CPU (vertex colours, unlit): lit tops white by day / warm
    /// #FFD9B8 at golden hour / coral at sunset, shaded sides, undersides #DCE5F1 by day (never darker) / #C9B3C8 at
    /// golden hour, dark blue-grey at night – authored as on-screen colours and pre-compensated for the grade like the
    /// sky dome. Distant clouds fade into the horizon haze by their own (lighter) aerial perspective: the scene fog is
    /// pre-compensated per cloud, so they stay readable instead of vanishing in the 0.0016 sea haze.
    /// Coverage follows the daily weather (Weather.Cloudiness: 5 clouds on a sunny day … 15 bigger, taller and greyer
    /// ones on a showery day) and they drift with Weather.Wind01. The layout is seeded by save + day (+ weather) and the
    /// drift runs on the shared game clock, so every co-op machine shows the same sky. Clouds dissolve before they cover
    /// the sun on clear days (the sun disc stays clear). They live in a toroidal 3 x 3 km field around the camera
    /// (world-anchored, real parallax) and shrink to nothing before wrapping to the far side.
    /// Cost: ≤ 16 renderers with one shared material (SRP batcher), meshes rebaked once per day, colours refreshed
    /// round-robin (≤ 2 clouds per frame, only when the light, the fog or the distance changed). No per-frame allocations.
    public sealed class WorldClouds
    {
        public const int MaxClouds = 16;
        /// Opaque pass, after the stars and the moon (WorldSky) so clouds cover them, before every transparent.
        public const int RenderQueue = 2450;
        const int Templates = 6;
        const float Half = 1500f;                      // field half-size around the camera
        const float GrowSeconds = 10f;                 // clouds form / dissolve over this time when the cover changes
        const int RecolorsPerFrame = 2;
        /// Own aerial perspective (lighter than the sea haze): ~63 % of the cloud colour is left at 1 km.
        const float HazeMeters = 2200f;

        sealed class Template
        {
            public Vector3[] v;     // unwelded, flat shaded: x −1..1, y 0..1, z −0.5..0.5
            public float[] h;       // height fraction per vertex
            public float[] jit;     // brightness per face (soft facets)
            public int[] tris;
        }

        readonly Template[] templates = new Template[Templates];
        readonly Transform[] objs = new Transform[MaxClouds];
        readonly MeshRenderer[] rends = new MeshRenderer[MaxClouds];
        readonly Mesh[] meshes = new Mesh[MaxClouds];
        readonly Vector3[][] verts = new Vector3[MaxClouds][];
        readonly Vector3[][] faceN = new Vector3[MaxClouds][];
        readonly Color[][] cols = new Color[MaxClouds][];
        readonly Vector2[] anchor = new Vector2[MaxClouds];
        readonly float[] altitude = new float[MaxClouds];
        readonly float[] cloudH = new float[MaxClouds];
        readonly float[] cloudR = new float[MaxClouds];      // horizontal radius (m) for the sun clearance
        readonly float[] present = new float[MaxClouds];
        readonly float[] lastDist = new float[MaxClouds];
        readonly float[] dist = new float[MaxClouds];
        readonly int[] colorVersion = new int[MaxClouds];
        readonly bool[] on = new bool[MaxClouds];
        readonly Material mat;
        readonly bool vertexColored, linearSpace;

        // palette keys by sun elevation (on-screen sRGB targets → pre-graded linear)
        static readonly float[] Keys = { -12f, -6f, -2f, 1f, 4f, 8f, 14f, 24f };
        static readonly string[] TopHex = { "#46516F", "#6B6686", "#B48C9E", "#FFB894", "#FFC6A2", "#FFD9B8", "#FDEBD9", "#F7F9FC" };
        static readonly string[] SideHex = { "#363F5D", "#58597B", "#987F98", "#D8A2AC", "#E2B6B6", "#E6CBCB", "#E8E6EC", "#E9EEF5" };
        static readonly string[] BottomHex = { "#2B3352", "#4A4B6D", "#7C6F91", "#AE95B1", "#BCA4BF", "#C9B3C8", "#D2CEDD", "#DCE5F1" };
        readonly Color[] topLin = new Color[Keys.Length], sideLin = new Color[Keys.Length], botLin = new Color[Keys.Length];

        // current palette (versioned: clouds recolour when it moved noticeably)
        Color palTop, palSide, palBot;
        Vector3 palLight = Vector3.up;
        int version = 1;
        // fog state seen by the last version bump
        Color fogSeen;
        float fogDensSeen = -1f;
        bool fogOnSeen;
        FogMode fogModeSeen;

        // layout / weather
        int layoutSeed = int.MinValue;
        float cover = 0.35f, targetCover = 0.35f, grey;
        Vector2 windVel = new Vector2(-2.4f, 0.9f);
        Vector2 drift;
        bool shown = true;
        int rr;                 // round-robin recolour pointer
        int visibleCount;

        public Material Material => mat;
        /// Cloud cover in use (0 clear … 1 overcast; follows Weather.Cloudiness of the day).
        public float Cover => cover;
        /// 0 white fair-weather cumulus … 1 grey rain clouds.
        public float Grey => grey;
        /// Clouds currently drawn (after the edge / sun / grow fades).
        public int VisibleCount => visibleCount;
        /// Clouds the current cover asks for.
        public int WantedCount => CountFor(cover);
        /// World offset of the whole field (wind × shared game clock).
        public Vector2 Drift => drift;
        public float WindSpeed => windVel.magnitude;
        /// Lit-top colour in use (pre-graded linear) – golden at sunset, white at noon (tests).
        public Color TopColor => palTop;
        public Color BottomColor => palBot;
        /// Slow azimuth drift of the far horizon cloud bands (degrees, WorldSkyDome).
        public float BandYaw { get; private set; }

        /// Clouds a cloud cover asks for: 5 on a sunny day (0.1), 8 fair, 13 cloudy, 15 showers.
        public static int CountFor(float c) => Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(4f, 16f, Mathf.Clamp01(c))), 0, MaxClouds);

        /// World position of a cloud slot (its base centre) and whether it is drawn.
        public bool CloudInfo(int i, out Vector3 position)
        {
            position = Vector3.zero;
            if (i < 0 || i >= MaxClouds || objs[i] == null) return false;
            position = objs[i].position;
            return on[i];
        }

        public WorldClouds(Transform parent)
        {
            linearSpace = QualitySettings.activeColorSpace == ColorSpace.Linear;
            for (int k = 0; k < Keys.Length; k++)
            {
                topLin[k] = Pre(TopHex[k]);
                sideLin[k] = Pre(SideHex[k]);
                botLin[k] = Pre(BottomHex[k]);
            }
            palTop = topLin[Keys.Length - 1]; palSide = sideLin[Keys.Length - 1]; palBot = botLin[Keys.Length - 1];

            var shader = MaterialLibrary.ParticlesUnlit;
            vertexColored = shader != null && shader.isSupported;
            if (vertexColored)
            {
                // the sky dome's vertex-colour shader as a solid, depth-writing surface (render states only: same variant)
                mat = new Material(shader) { name = "SB_Cloud" };
                mat.SetColor("_BaseColor", Color.white);
                MaterialLibrary.MakeTransparent(mat, false);
                mat.SetFloat("_SrcBlend", (float)BlendMode.One);
                mat.SetFloat("_DstBlend", (float)BlendMode.Zero);
                mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                mat.SetFloat("_DstBlendAlpha", (float)BlendMode.Zero);
                mat.SetFloat("_ZWrite", 1f);
                mat.SetShaderPassEnabled("DepthOnly", false);
                mat.SetShaderPassEnabled("DepthNormals", false);
                mat.SetShaderPassEnabled("DepthNormalsOnly", false);
                mat.SetShaderPassEnabled("ShadowCaster", false);
                mat.SetShaderPassEnabled("MotionVectors", false);
                mat.renderQueue = RenderQueue;
            }
            else
            {
                // fallback: lit white clouds with a bright emission floor (no vertex colours)
                mat = new Material(MaterialLibrary.Lit) { name = "SB_Cloud" };
                mat.SetColor("_BaseColor", new Color(0.97f, 0.97f, 1f));
                mat.SetFloat("_Smoothness", 0f);
                mat.SetColor("_EmissionColor", new Color(0.55f, 0.58f, 0.64f));
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }

            var rng = new System.Random(2718);
            for (int t = 0; t < Templates; t++) templates[t] = BuildTemplate(rng, t);

            var root = new GameObject("Clouds").transform;
            root.SetParent(parent, false);
            for (int i = 0; i < MaxClouds; i++)
            {
                var tpl = templates[i % Templates];
                var go = new GameObject("Cloud" + i);
                go.transform.SetParent(root, false);
                int vc = tpl.v.Length;
                verts[i] = new Vector3[vc];
                cols[i] = new Color[vc];
                faceN[i] = new Vector3[vc / 3];
                for (int v = 0; v < vc; v++) cols[i][v] = Color.white;
                var m = new Mesh { name = "SB_Cloud" + i };
                m.MarkDynamic();
                m.SetVertices(tpl.v);
                m.SetColors(cols[i]);
                m.SetTriangles(tpl.tris, 0, false);
                if (!vertexColored) m.RecalculateNormals();
                m.RecalculateBounds();
                meshes[i] = m;
                go.AddComponent<MeshFilter>().sharedMesh = m;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                r.allowOcclusionWhenDynamic = false;
                r.enabled = false;
                objs[i] = go.transform;
                rends[i] = r;
            }
        }

        static Color Pre(string hex) => SkyGrade.InverseLinear(SkyGrade.AcesHueFix(SkyGrade.Hex(hex)));

        static float Range(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);

        static float Smooth01(float t)
        {
            t = t < 0f ? 0f : t > 1f ? 1f : t;
            return t * t * (3f - 2f * t);
        }

        /// Snaps the cover and the grow-in to their targets (tests / screenshots).
        public void Settle()
        {
            cover = targetCover;
            int want = CountFor(cover);
            for (int i = 0; i < MaxClouds; i++) present[i] = i < want ? 1f : 0f;
            version++;
        }

        // ------------------------------------------------------------------ per frame
        /// sunDir = unit vector toward the sun, hazeLin = horizon haze colour (pre-graded linear), fogBank = Fog Isle
        /// bank thickness (0..1: the clouds drown in it).
        public void Tick(Vector3 cam, Vector3 sunDir, float sunElev, bool show, Color hazeLin, float fogBank, float dt)
        {
            if (show != shown)
            {
                shown = show;
                if (!show)
                {
                    for (int i = 0; i < MaxClouds; i++) { rends[i].enabled = false; on[i] = false; }
                    visibleCount = 0;
                }
            }

            // ---- weather of the day → layout (seeded by save + day + weather: the same on every co-op machine)
            var gm = GameManager.I;
            bool inGame = gm != null && gm.InGame && gm.State != null;
            int seed;
            float wind01;
            if (inGame)
            {
                var s = gm.State;
                var kind = Weather.Today;
                targetCover = Weather.Cloudiness(kind);
                wind01 = Weather.Wind01(kind);
                unchecked { seed = s.saveSeed * 92821 + s.day * 7919 + (int)kind * 131; }
            }
            else
            {
                targetCover = 0.35f;
                wind01 = 0.15f;
                seed = 777;
            }
            if (seed != layoutSeed) BuildLayout(seed, targetCover, wind01);
            cover = Mathf.MoveTowards(cover, targetCover, dt / 40f);
            grey = Smooth01((cover - 0.45f) / 0.43f);

            // ---- drift on the shared clock (game time of day; the menu runs on its own clock)
            float secs = inGame ? gm.State.timeOfDay * GameManager.DayLengthSeconds : Time.unscaledTime;
            float span = 2f * Half;
            drift = new Vector2(Mathf.Repeat(windVel.x * secs, span), Mathf.Repeat(windVel.y * secs, span));
            BandYaw = Mathf.Repeat(secs * windVel.magnitude * 0.004f, 360f);

            if (!show) return;

            UpdatePalette(sunDir, sunElev);
            UpdateFogVersion();

            // ---- fog / haze factors (per cloud: from its distance)
            bool fogOn = RenderSettings.fog;
            FogMode mode = RenderSettings.fogMode;
            float dens = RenderSettings.fogDensity;
            Color fogLin = RenderSettings.fogColor.linear;
            fogBank = Mathf.Clamp01(fogBank);

            int want = CountFor(cover);
            float sunClear = (1f - grey) * Smooth01((sunElev + 3f) / 3f);
            visibleCount = 0;
            for (int i = 0; i < MaxClouds; i++)
            {
                present[i] = Mathf.MoveTowards(present[i], i < want ? 1f : 0f, dt / GrowSeconds);
                float rx = Mathf.Repeat(anchor[i].x + drift.x - cam.x + Half, span) - Half;
                float rz = Mathf.Repeat(anchor[i].y + drift.y - cam.z + Half, span) - Half;
                float edge = Mathf.Max(Mathf.Abs(rx), Mathf.Abs(rz)) / Half;
                float k = 1f - Smooth01((edge - 0.8f) / 0.18f);
                k *= Smooth01(present[i]);

                // the sun stays clear: a cloud drifting in front of it dissolves (clear days only)
                float cy = altitude[i] + cloudH[i] * 0.45f - cam.y;
                float d = Mathf.Sqrt(rx * rx + cy * cy + rz * rz);
                if (sunClear > 0.001f && d > 1f && k > 0.02f)
                {
                    float cosA = (rx * sunDir.x + cy * sunDir.y + rz * sunDir.z) / d;
                    if (cosA > 0.7f)
                    {
                        float ang = Mathf.Acos(Mathf.Min(cosA, 1f)) * Mathf.Rad2Deg;
                        float rad = Mathf.Atan(cloudR[i] / d) * Mathf.Rad2Deg;
                        float clear = Smooth01((ang - (rad * 0.55f + 1.5f)) / (rad * 0.45f + 6.5f));
                        k *= Mathf.Lerp(1f, clear, sunClear);
                    }
                }

                bool vis = k > 0.02f;
                var r = rends[i];
                if (!vis)
                {
                    if (on[i]) { r.enabled = false; on[i] = false; }
                    continue;
                }
                var t = objs[i];
                t.position = new Vector3(cam.x + rx, altitude[i], cam.z + rz);
                t.localScale = new Vector3(k, k, k);
                dist[i] = d;
                visibleCount++;
                bool dirty = colorVersion[i] != version || Mathf.Abs(d - lastDist[i]) > lastDist[i] * 0.04f;
                if (!on[i])
                {
                    // becoming visible: colour it right away (never one frame with stale colours)
                    if (dirty && vertexColored) Recolor(i, hazeLin, fogLin, fogOn, mode, dens, fogBank);
                    r.enabled = true;
                    on[i] = true;
                }
            }

            // ---- round-robin recolour of the visible clouds whose light, fog or distance changed
            if (!vertexColored) return;
            int done = 0;
            for (int n = 0; n < MaxClouds && done < RecolorsPerFrame; n++)
            {
                int i = rr;
                rr = (rr + 1) % MaxClouds;
                if (!on[i]) continue;
                if (colorVersion[i] == version && Mathf.Abs(dist[i] - lastDist[i]) <= lastDist[i] * 0.04f) continue;
                Recolor(i, hazeLin, fogLin, fogOn, mode, dens, fogBank);
                done++;
            }
        }

        void UpdatePalette(Vector3 sunDir, float sunElev)
        {
            // key colours by sun elevation
            int last = Keys.Length - 1, k = last - 1;
            float f = 1f;
            if (sunElev <= Keys[0]) { k = 0; f = 0f; }
            else if (sunElev < Keys[last])
            {
                for (int i = 0; i < last; i++)
                    if (sunElev <= Keys[i + 1]) { k = i; f = Smooth01((sunElev - Keys[i]) / (Keys[i + 1] - Keys[i])); break; }
            }
            Color top = Color.Lerp(topLin[k], topLin[k + 1], f);
            Color side = Color.Lerp(sideLin[k], sideLin[k + 1], f);
            Color bot = Color.Lerp(botLin[k], botLin[k + 1], f);
            // rainy / overcast days: greyer and darker, the bases most
            if (grey > 0.001f)
            {
                top = Greyed(top, 0.82f, grey);
                side = Greyed(side, 0.62f, grey);
                bot = Greyed(bot, 0.45f, grey);
            }

            // shading light: the sun (the moon at night), never lower than 8° so the tops always catch it
            Vector3 l = sunElev > -4f ? sunDir : -sunDir;
            float hl = Mathf.Sqrt(l.x * l.x + l.z * l.z);
            float e = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(l.y, -1f, 1f)), 8f * Mathf.Deg2Rad, 70f * Mathf.Deg2Rad);
            if (hl < 1e-4f) { l = Vector3.up; }
            else
            {
                float ce = Mathf.Cos(e) / hl;
                l = new Vector3(l.x * ce, Mathf.Sin(e), l.z * ce);
            }

            float diff = Mathf.Abs(top.r - palTop.r) + Mathf.Abs(top.g - palTop.g) + Mathf.Abs(top.b - palTop.b)
                       + Mathf.Abs(side.r - palSide.r) + Mathf.Abs(side.g - palSide.g) + Mathf.Abs(side.b - palSide.b)
                       + Mathf.Abs(bot.r - palBot.r) + Mathf.Abs(bot.g - palBot.g) + Mathf.Abs(bot.b - palBot.b);
            float turn = 1f - Vector3.Dot(l, palLight);
            if (diff > 0.012f || turn > 0.0002f)
            {
                palTop = top; palSide = side; palBot = bot; palLight = l;
                version++;
            }
        }

        static Color Greyed(Color c, float darken, float g)
        {
            float lum = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
            var gc = new Color(lum * 0.93f * darken, lum * 0.96f * darken, lum * 1.04f * darken, 1f);
            return Color.Lerp(c, gc, 0.85f * g);
        }

        void UpdateFogVersion()
        {
            bool fogOn = RenderSettings.fog;
            FogMode mode = RenderSettings.fogMode;
            float dens = RenderSettings.fogDensity;
            Color fc = RenderSettings.fogColor;
            bool changed = fogOn != fogOnSeen || mode != fogModeSeen
                           || Mathf.Abs(dens - fogDensSeen) > Mathf.Max(fogDensSeen, 1e-5f) * 0.03f
                           || Mathf.Abs(fc.r - fogSeen.r) + Mathf.Abs(fc.g - fogSeen.g) + Mathf.Abs(fc.b - fogSeen.b) > 0.01f;
            if (!changed) return;
            fogOnSeen = fogOn; fogModeSeen = mode; fogDensSeen = dens; fogSeen = fc;
            version++;
        }

        static float FogVis(bool fogOn, FogMode mode, float dens, float d)
        {
            if (!fogOn) return 1f;
            switch (mode)
            {
                case FogMode.Linear:
                {
                    float s = RenderSettings.fogStartDistance, e = RenderSettings.fogEndDistance;
                    return e > s ? Mathf.Clamp01((e - d) / (e - s)) : 1f;
                }
                case FogMode.Exponential: return Mathf.Exp(-dens * d);
                default: { float x = dens * d; return Mathf.Exp(-x * x); }
            }
        }

        /// Per-facet colours of one cloud for the current palette, with its own haze and the scene fog pre-compensated
        /// (URP fogs by view depth ≈ 0.93 × distance across the view).
        void Recolor(int i, Color hazeLin, Color fogLin, bool fogOn, FogMode mode, float dens, float fogBank)
        {
            float d = dist[i];
            float vis = Mathf.Max(FogVis(fogOn, mode, dens, d * 0.93f), 0.04f);
            float visD = Mathf.Exp(-d / HazeMeters);
            if (visD < vis) visD = vis;
            visD = Mathf.Lerp(visD, vis, fogBank);            // inside the Fog Isle bank they drown in the fog
            float inv = 1f / vis;
            float a = visD * inv;
            Color b = new Color((hazeLin.r * (1f - visD) - fogLin.r * (1f - vis)) * inv,
                                (hazeLin.g * (1f - visD) - fogLin.g * (1f - vis)) * inv,
                                (hazeLin.b * (1f - visD) - fogLin.b * (1f - vis)) * inv, 0f);

            var tpl = templates[i % Templates];
            var fn = faceN[i];
            var c = cols[i];
            var h = tpl.h;
            var jit = tpl.jit;
            Vector3 l = palLight;
            Color top = palTop, side = palSide, bot = palBot;
            int faces = fn.Length;
            for (int fIdx = 0; fIdx < faces; fIdx++)
            {
                Vector3 n = fn[fIdx];
                float ndl = n.x * l.x + n.y * l.y + n.z * l.z;
                float lit = Smooth01((ndl + 0.25f) / 1.05f);
                float down = Smooth01((-n.y - 0.2f) / 0.6f);
                float j = jit[fIdx];
                int o = fIdx * 3;
                for (int q = 0; q < 3; q++)
                {
                    float hv = h[o + q];
                    float sun = lit * (0.6f + 0.4f * hv);
                    float r = side.r + (top.r - side.r) * sun;
                    float g = side.g + (top.g - side.g) * sun;
                    float bl = side.b + (top.b - side.b) * sun;
                    float lowK = (1f - hv) * (1f - hv) * 0.55f * (1f - lit);
                    float under = down > lowK ? down : lowK;
                    r += (bot.r - r) * under; g += (bot.g - g) * under; bl += (bot.b - bl) * under;
                    r = Clamp(r * j * a + b.r); g = Clamp(g * j * a + b.g); bl = Clamp(bl * j * a + b.b);
                    c[o + q] = linearSpace ? new Color(r, g, bl, 1f) : new Color(G(r), G(g), G(bl), 1f);
                }
            }
            meshes[i].SetColors(c);
            colorVersion[i] = version;
            lastDist[i] = d;
        }

        static float Clamp(float v) => v < 0f ? 0f : v > 12f ? 12f : v;
        static float G(float c) => c <= 0.0031308f ? c * 12.92f : 1.055f * Mathf.Pow(c, 1f / 2.4f) - 0.055f;

        // ------------------------------------------------------------------ layout (once per day / weather)
        void BuildLayout(int seed, float dayCover, float wind01)
        {
            layoutSeed = seed;
            var rng = new System.Random(seed);
            float g = Smooth01((dayCover - 0.45f) / 0.43f);

            // trade wind from the east (clouds travel west-north-west), ± 25° per day
            float ang = Mathf.Atan2(0.4f, -1f) + Range(rng, -0.44f, 0.44f);
            float speed = Mathf.Lerp(1.6f, 5.5f, Mathf.Clamp01(wind01));
            windVel = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * speed;
            float heading = Mathf.Atan2(-windVel.y, windVel.x);   // yaw that turns the cloud's long axis (x) along the wind

            // a jittered 4 x 4 grid of 750 m cells, slots take the cells in a seeded order (never clumped)
            int cellsN = 16;
            var order = new int[cellsN];
            for (int i = 0; i < cellsN; i++) order[i] = i;
            for (int i = cellsN - 1; i > 0; i--) { int j = rng.Next(i + 1); int tmp = order[i]; order[i] = order[j]; order[j] = tmp; }
            float sizeMul = Mathf.Lerp(1f, 1.4f, Mathf.Clamp01(dayCover));
            for (int i = 0; i < MaxClouds; i++)
            {
                int cell = order[i % cellsN];
                int gx = cell % 4, gz = cell / 4;
                anchor[i] = new Vector2((gx + 0.5f + Range(rng, -0.32f, 0.32f)) / 4f * 2f * Half - Half,
                                        (gz + 0.5f + Range(rng, -0.32f, 0.32f)) / 4f * 2f * Half - Half);
                float len = Range(rng, 75f, 145f) * sizeMul;
                float hgt = len * Range(rng, 0.2f, 0.28f) * Mathf.Lerp(1f, 1.35f, g);
                float dep = len * Range(rng, 0.5f, 0.72f);
                altitude[i] = Range(rng, 210f, 310f) * Mathf.Lerp(1f, 0.85f, g);
                cloudH[i] = hgt;
                cloudR[i] = 0.5f * Mathf.Max(len, dep);
                float yaw = heading + Range(rng, -0.45f, 0.45f);
                Bake(i, len, hgt, dep, yaw);
            }

            cover = dayCover;
            targetCover = dayCover;
            int want = CountFor(cover);
            for (int i = 0; i < MaxClouds; i++)
            {
                // the new sky is there at once: a new day starts during the sleep fade (or at midnight, dark clouds on a
                // dark sky) and a loaded game behind the loading screen – clouds inflating in view would look odd
                present[i] = i < want ? 1f : 0f;
                colorVersion[i] = 0;
                lastDist[i] = 1f;
            }
            version++;
        }

        /// Bakes size and yaw into the slot's own mesh (transform = position + uniform fade scale only) and the
        /// per-face normals the recolour uses.
        void Bake(int i, float len, float hgt, float dep, float yaw)
        {
            var tpl = templates[i % Templates];
            var src = tpl.v;
            var dst = verts[i];
            float c = Mathf.Cos(yaw), s = Mathf.Sin(yaw);
            float sx = len * 0.5f;
            for (int v = 0; v < src.Length; v++)
            {
                float x = src[v].x * sx, y = src[v].y * hgt, z = src[v].z * dep;
                dst[v] = new Vector3(x * c + z * s, y, -x * s + z * c);
            }
            var fn = faceN[i];
            for (int f = 0; f < fn.Length; f++)
            {
                Vector3 a = dst[f * 3], b = dst[f * 3 + 1], e = dst[f * 3 + 2];
                Vector3 n = Vector3.Cross(b - a, e - a);
                float m = n.magnitude;
                fn[f] = m > 1e-6f ? n / m : Vector3.down;
            }
            var mesh = meshes[i];
            mesh.SetVertices(dst);
            if (!vertexColored) mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        public void Destroy()
        {
            if (mat != null) Object.Destroy(mat);
            for (int i = 0; i < MaxClouds; i++) if (meshes[i] != null) Object.Destroy(meshes[i]);
        }

        // ------------------------------------------------------------------ templates
        /// A cumulus: 5–7 flattened base puffs on a flat bottom, 3–4 middle puffs, 1–3 cauliflower top puffs and two
        /// side bulges (jittered icospheres, 80 faces each). Faces buried inside another puff are dropped. Normalised to
        /// x −1..1, y 0..1, z −0.5..0.5; size, aspect and yaw come per cloud.
        static Template BuildTemplate(System.Random rng, int variant)
        {
            var puffs = new List<Vector4>(16);   // xyz centre, w radius
            int nBase = 5 + variant % 3;
            for (int p = 0; p < nBase; p++)
            {
                float f = p / (float)(nBase - 1);
                float mid = 1f - Mathf.Abs(f - 0.5f) * 2f;
                float r = Mathf.Lerp(0.27f, 0.42f, mid) * Range(rng, 0.88f, 1.12f);
                puffs.Add(new Vector4((f - 0.5f) * 1.7f + Range(rng, -0.08f, 0.08f), r * Range(rng, 0.12f, 0.25f), Range(rng, -0.16f, 0.16f), r));
            }
            int nMid = 3 + variant % 2;
            for (int p = 0; p < nMid; p++)
            {
                float f = nMid == 1 ? 0.5f : p / (float)(nMid - 1);
                float r = Range(rng, 0.26f, 0.34f);
                puffs.Add(new Vector4((f - 0.5f) * 0.95f + Range(rng, -0.1f, 0.1f), 0.38f + Range(rng, 0f, 0.08f), Range(rng, -0.1f, 0.1f), r));
            }
            int nTop = 1 + variant % 3;
            for (int p = 0; p < nTop; p++)
            {
                float f = nTop == 1 ? 0.5f : p / (float)(nTop - 1);
                float r = Range(rng, 0.19f, 0.26f);
                puffs.Add(new Vector4((f - 0.5f) * 0.5f + Range(rng, -0.12f, 0.12f), 0.64f + Range(rng, 0f, 0.1f), Range(rng, -0.08f, 0.08f), r));
            }
            for (int p = 0; p < 2; p++)
            {
                float r = Range(rng, 0.17f, 0.24f);
                puffs.Add(new Vector4(Range(rng, -0.5f, 0.5f), r * 0.5f + 0.1f, (p == 0 ? 1f : -1f) * Range(rng, 0.24f, 0.32f), r));
            }

            BuildIcosphere(out var unit, out var faces);
            var verts = new List<Vector3>(puffs.Count * 240);
            var jit = new List<float>(puffs.Count * 80);
            var pv = new Vector3[unit.Count];
            for (int p = 0; p < puffs.Count; p++)
            {
                Vector4 P = puffs[p];
                var c = new Vector3(P.x, P.y, P.z);
                for (int i = 0; i < unit.Count; i++)
                {
                    var q = unit[i] * (P.w * Range(rng, 0.88f, 1.1f));
                    q.y *= 0.82f;
                    q += c;
                    if (q.y < 0f) q.y = 0f;               // flat cloud base
                    pv[i] = q;
                }
                for (int f = 0; f < faces.Length; f += 3)
                {
                    Vector3 a = pv[faces[f]], b = pv[faces[f + 1]], e = pv[faces[f + 2]];
                    if (Buried(puffs, p, a) && Buried(puffs, p, b) && Buried(puffs, p, e)) continue;
                    verts.Add(a); verts.Add(b); verts.Add(e);
                    // flat bottom faces share one colour (they overlap at y = 0); the others vary ±3.5 %
                    Vector3 n = Vector3.Cross(b - a, e - a);
                    bool bottom = n.sqrMagnitude < 1e-12f || n.normalized.y < -0.9f;
                    jit.Add(bottom ? 1f : 1f + Range(rng, -0.035f, 0.035f));
                }
            }

            // normalise: x −1..1, y 0..1, z −0.5..0.5
            float minX = float.MaxValue, maxX = float.MinValue, maxY = 0.01f, minZ = float.MaxValue, maxZ = float.MinValue;
            for (int i = 0; i < verts.Count; i++)
            {
                var v = verts[i];
                if (v.x < minX) minX = v.x; if (v.x > maxX) maxX = v.x;
                if (v.y > maxY) maxY = v.y;
                if (v.z < minZ) minZ = v.z; if (v.z > maxZ) maxZ = v.z;
            }
            float cx = (minX + maxX) * 0.5f, cz = (minZ + maxZ) * 0.5f;
            float sx = 2f / Mathf.Max(maxX - minX, 0.01f), sz = 1f / Mathf.Max(maxZ - minZ, 0.01f), sy = 1f / maxY;
            var t = new Template
            {
                v = new Vector3[verts.Count],
                h = new float[verts.Count],
                jit = jit.ToArray(),
                tris = new int[verts.Count],
            };
            for (int i = 0; i < verts.Count; i++)
            {
                var v = verts[i];
                t.v[i] = new Vector3((v.x - cx) * sx, v.y * sy, (v.z - cz) * sz);
                t.h[i] = Mathf.Clamp01(v.y * sy);
                t.tris[i] = i;
            }
            return t;
        }

        /// True when the point lies well inside another puff (its surface encloses ≥ 0.8 of the nominal radius).
        static bool Buried(List<Vector4> puffs, int self, Vector3 p)
        {
            for (int q = 0; q < puffs.Count; q++)
            {
                if (q == self) continue;
                Vector4 P = puffs[q];
                float dx = p.x - P.x, dy = (p.y - P.y) / 0.82f, dz = p.z - P.z;
                float r = P.w * 0.8f;
                if (dx * dx + dy * dy + dz * dz < r * r) return true;
            }
            return false;
        }

        /// Unit icosphere with one subdivision (42 vertices, 80 faces; Cross(b − a, c − a) points outward = front face).
        static void BuildIcosphere(out List<Vector3> pts, out int[] faces)
        {
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            pts = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            for (int i = 0; i < pts.Count; i++) pts[i] = pts[i].normalized;
            int[] f0 =
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };
            var mid = new Dictionary<long, int>();
            var sub = new List<int>(240);
            for (int f = 0; f < f0.Length; f += 3)
            {
                int a = f0[f], b = f0[f + 1], c = f0[f + 2];
                int ab = Mid(pts, mid, a, b), bc = Mid(pts, mid, b, c), ca = Mid(pts, mid, c, a);
                sub.Add(a); sub.Add(ab); sub.Add(ca);
                sub.Add(b); sub.Add(bc); sub.Add(ab);
                sub.Add(c); sub.Add(ca); sub.Add(bc);
                sub.Add(ab); sub.Add(bc); sub.Add(ca);
            }
            faces = sub.ToArray();
        }

        static int Mid(List<Vector3> pts, Dictionary<long, int> cache, int a, int b)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (cache.TryGetValue(key, out int id)) return id;
            id = pts.Count;
            pts.Add(((pts[a] + pts[b]) * 0.5f).normalized);
            cache[key] = id;
            return id;
        }
    }
}
