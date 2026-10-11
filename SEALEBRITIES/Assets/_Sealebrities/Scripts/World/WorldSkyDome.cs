using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Approximation of the How to Fish base grade the FX module applies (art bible §1.5: postExposure +0.45, saturation
    /// +8, contrast +12, ACES). The sky dome, the fog colour and the sky reflection are authored as ON-SCREEN target
    /// colours (art bible ▣ values) and pre-compensated through the inverse of this grade, so they measure right after
    /// post-processing. Pure math, allocation-free.
    public static class SkyGrade
    {
        // keep in sync with FXPostProcess (DayExposure / DaySaturation / DayContrast)
        public const float Exposure = 0.38f, Saturation = 8f, Contrast = 12f;

        static float Aces(float x)
        {
            x *= 0.6f;
            float v = (x * (2.51f * x + 0.03f)) / (x * (2.43f * x + 0.59f) + 0.14f);
            return v < 0f ? 0f : (v > 1f ? 1f : v);
        }

        static float ToGamma(float c) => c <= 0.0031308f ? c * 12.92f : 1.055f * Mathf.Pow(c, 1f / 2.4f) - 0.055f;
        static float ToLinear(float c) => c <= 0.04045f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f);

        /// sRGB display colour of a linear scene colour after the grade and tonemapping.
        public static Color Display(Color lin)
        {
            float k = Mathf.Pow(2f, Exposure);
            float r = lin.r * k, g = lin.g * k, b = lin.b * k;
            float lum = 0.2126f * r + 0.7152f * g + 0.0722f * b;
            float s = 1f + Saturation / 100f;
            r = Mathf.Max(0f, lum + (r - lum) * s); g = Mathf.Max(0f, lum + (g - lum) * s); b = Mathf.Max(0f, lum + (b - lum) * s);
            float c = 1f + Contrast / 100f;
            r = 0.18f * Mathf.Pow(Mathf.Max(r, 1e-5f) / 0.18f, c);
            g = 0.18f * Mathf.Pow(Mathf.Max(g, 1e-5f) / 0.18f, c);
            b = 0.18f * Mathf.Pow(Mathf.Max(b, 1e-5f) / 0.18f, c);
            return new Color(ToGamma(Aces(r)), ToGamma(Aces(g)), ToGamma(Aces(b)), lin.a);
        }

        /// Linear scene colour that displays as the sRGB target (closed-form inverse of every stage).
        public static Color InverseLinear(Color target)
        {
            float r = InvTone(target.r), g = InvTone(target.g), b = InvTone(target.b);
            // contrast
            float ic = 1f / (1f + Contrast / 100f);
            r = 0.18f * Mathf.Pow(Mathf.Max(r, 1e-6f) / 0.18f, ic);
            g = 0.18f * Mathf.Pow(Mathf.Max(g, 1e-6f) / 0.18f, ic);
            b = 0.18f * Mathf.Pow(Mathf.Max(b, 1e-6f) / 0.18f, ic);
            // saturation (around luminance, which it preserves)
            float lum = 0.2126f * r + 0.7152f * g + 0.0722f * b;
            float s = 1f / (1f + Saturation / 100f);
            r = Mathf.Max(0f, lum + (r - lum) * s); g = Mathf.Max(0f, lum + (g - lum) * s); b = Mathf.Max(0f, lum + (b - lum) * s);
            // exposure
            float k = Mathf.Pow(2f, -Exposure);
            return new Color(r * k, g * k, b * k, target.a);
        }

        /// Inverse of sRGB(ACES(x)) for one channel.
        static float InvTone(float srgb)
        {
            float T = ToLinear(Mathf.Clamp(srgb, 0.001f, 0.99f));
            float a = 2.51f - 2.43f * T, bq = 0.03f - 0.59f * T, c = -0.14f * T;
            float x = (-bq + Mathf.Sqrt(bq * bq - 4f * a * c)) / (2f * a);
            return Mathf.Max(0f, x / 0.6f);
        }

        /// Same as InverseLinear but returned in sRGB/gamma encoding (for RenderSettings / camera colours).
        public static Color InverseGamma(Color target)
        {
            var l = InverseLinear(target);
            return new Color(ToGamma(Mathf.Min(l.r, 1f)), ToGamma(Mathf.Min(l.g, 1f)), ToGamma(Mathf.Min(l.b, 1f)), target.a);
        }

        /// URP's real ACES (RRT + ODT) differs from the simple fit above. Measured in the build: saturated sky blues
        /// turn ~10° toward violet (#4A88BF → #3A68B8) and light colours come out brighter than the fit predicts
        /// (horizon #8DBDEA → #A0CEF5 after the hue fix). Blue targets are pre-rotated toward cyan and light targets
        /// pre-darkened, so the screen lands on the How to Fish colours (warm colours keep their hue).
        public static Color AcesHueFix(Color target)
        {
            Color.RGBToHSV(target, out float h, out float s, out float v);
            float deg = h * 360f;
            float w = Band(deg, 165f, 185f, 245f, 275f) * Mathf.Clamp01(s * 4f);
            if (w > 0f)
            {
                deg -= 8f * w;
                s *= 1f - 0.04f * w;
            }
            // value calibration measured in two builds: ×1.03 kept the zenith (v ≈ 0.75) exact, while the 30° sky
            // (v ≈ 0.83) and the horizon (v ≈ 0.92) came out ~8 / ~10 % too bright; the screen reacts ~2.5× to an
            // input change here (×0.95 at the zenith gave −18 %), so the light end is trimmed gently
            float mul = v <= 0.75f ? 1.03f
                      : v <= 0.83f ? Mathf.Lerp(1.03f, 0.99f, (v - 0.75f) / 0.08f)
                      : Mathf.Lerp(0.99f, 0.98f, Mathf.Clamp01((v - 0.83f) / 0.09f));
            v = Mathf.Min(1f, v * mul);
            var c = Color.HSVToRGB(Mathf.Repeat(deg / 360f, 1f), s, v);
            c.a = target.a;
            return c;
        }

        static float Band(float x, float a, float b, float c, float d)
        {
            if (x <= a || x >= d) return 0f;
            if (x < b) return Mathf.SmoothStep(0f, 1f, (x - a) / (b - a));
            if (x > c) return Mathf.SmoothStep(0f, 1f, (d - x) / (d - c));
            return 1f;
        }

        public static Color Hex(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }
    }

    /// The How to Fish sky: a camera-centred, vertex-coloured dome drawn first (background queue, no depth) with the
    /// URP Particles/Unlit shader that is already part of the build, plus a 2° HDR sun disc with a soft halo. Colours
    /// come from the art bible §1.2 table (zenith, 30°, horizon away from / toward the sun) by sun elevation and are
    /// pre-compensated for the post-processing grade, so the screen measures the targets (no white horizon band).
    /// Recoloured only when the sun moved noticeably (no per-frame allocation).
    public sealed class WorldSkyDome
    {
        public const int DomeQueue = 1000, SunQueue = 1001;
        public const float Radius = 5f;
        const int Seg = 64;
        static readonly float[] Rings = { -90f, -30f, -10f, -3f, 0f, 1f, 2.5f, 4.5f, 7f, 10f, 14f, 19f, 25f, 32f, 40f, 50f, 62f, 76f, 90f };

        // sun elevation keys and the four on-screen colours per key (art bible §1.2; -3/3/12/30 interpolated rows)
        static readonly float[] Keys = { -15f, -6f, -3f, 0f, 3f, 8f, 12f, 20f, 30f, 40f };
        static readonly string[,] Table =
        {
            // zenith      30° up       horizon away  horizon toward
            // (art polish: dawn/golden rows less lavender – How to Fish keeps a blue sky with a warm peach glow only
            // toward the sun; the playtest found mornings and the menu "much more purple" than the references)
            { "#121A3A", "#1A2650", "#2E4775", "#33497A" },   // night (moon up)
            { "#1F2A5C", "#33427A", "#4C5A8E", "#9A6E7E" },   // dusk −6°
            { "#2E3E78", "#4F5C94", "#6C70A0", "#C88478" },   // −3°
            { "#3E5292", "#6C78AE", "#9490B4", "#F4986A" },   // sunset 0°
            { "#4C68A8", "#7C8EC0", "#A8AEC8", "#F4AE7A" },   // 3°
            { "#5A80BC", "#88A6D0", "#AEC2DC", "#F4C08C" },   // golden hour 8°
            { "#5384BE", "#78A4D2", "#A6C2E0", "#DCCBB6" },   // 12°
            { "#5189C0", "#6EA6D4", "#9CC4E8", "#BBD3EA" },   // 20°
            { "#4D88BF", "#69A4D3", "#94C0E9", "#AECFEC" },   // 30°
            { "#4A88BF", "#64A3D3", "#8DBDEA", "#A4CBEE" },   // ≥ 40° (noon)
        };

        readonly Color[,] lin;            // pre-compensated linear colours of the table
        readonly Mesh mesh, sunMesh;
        readonly Vector3[] dirs;
        readonly Color[] cols;
        readonly Color[] sunCols;
        readonly Material mat, sunMat;
        readonly MeshRenderer r, sunR;
        readonly Transform t, sunT;
        readonly bool linearSpace;
        float lastElev = -999f, lastAz = -999f, lastFogVis = -1f;
        Vector3 sunDir = Vector3.up;
        bool sunVisible;

        /// Fog/haze colour (sRGB, pre-compensated) = horizon away from the sun × 0.94 (art bible §1.4).
        public Color FogColor { get; private set; } = new Color(0.52f, 0.70f, 0.86f);
        /// Average sky colour seen by reflections (sRGB, pre-compensated).
        public Color ReflectionColor { get; private set; } = new Color(0.5f, 0.68f, 0.85f);
        /// On-screen horizon colour away from the sun (for other modules, e.g. UI tinting).
        public Color HorizonScreen { get; private set; }
        public bool Valid => mat != null;

        public WorldSkyDome(Transform parent, Shader vertexColorShader)
        {
            linearSpace = QualitySettings.activeColorSpace == ColorSpace.Linear;
            int keys = Keys.Length;
            lin = new Color[keys, 4];
            for (int k = 0; k < keys; k++)
                for (int c = 0; c < 4; c++)
                    lin[k, c] = SkyGrade.InverseLinear(SkyGrade.AcesHueFix(SkyGrade.Hex(Table[k, c])));
            if (vertexColorShader == null) return;

            // ---- dome
            int rings = Rings.Length;
            dirs = new Vector3[rings * Seg];
            cols = new Color[dirs.Length];
            for (int i = 0; i < rings; i++)
                for (int s = 0; s < Seg; s++)
                {
                    float e = Rings[i] * Mathf.Deg2Rad, a = s / (float)Seg * Mathf.PI * 2f;
                    dirs[i * Seg + s] = new Vector3(Mathf.Sin(a) * Mathf.Cos(e), Mathf.Sin(e), Mathf.Cos(a) * Mathf.Cos(e));
                    cols[i * Seg + s] = Color.black;
                }
            var tris = new int[(rings - 1) * Seg * 6];
            int n = 0;
            for (int i = 0; i < rings - 1; i++)
                for (int s = 0; s < Seg; s++)
                {
                    int a0 = i * Seg + s, a1 = i * Seg + (s + 1) % Seg, b0 = a0 + Seg, b1 = a1 + Seg;
                    tris[n++] = a0; tris[n++] = b0; tris[n++] = a1;
                    tris[n++] = a1; tris[n++] = b0; tris[n++] = b1;
                }
            mesh = new Mesh { name = "SB_SkyDome" };
            mesh.MarkDynamic();
            mesh.SetVertices(dirs);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2000f);

            mat = Material(vertexColorShader, "SB_SkyDome", DomeQueue);
            var go = new GameObject("SkyDome");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            r = go.AddComponent<MeshRenderer>();
            Setup(r, mat);
            t = go.transform;
            t.localScale = Vector3.one * Radius;

            // ---- sun disc (2° wide, HDR so the bloom draws the halo) + soft halo ring
            const int ds = 32;
            var sv = new Vector3[1 + ds * 3];
            sunCols = new Color[sv.Length];
            var st = new int[ds * 3 + ds * 12];
            sv[0] = Vector3.zero;
            float rd = Mathf.Tan(1f * Mathf.Deg2Rad);
            for (int s = 0; s < ds; s++)
            {
                float a = s / (float)ds * Mathf.PI * 2f;
                var u = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                sv[1 + s] = u * rd;
                sv[1 + ds + s] = u * rd * 1.35f;
                sv[1 + ds * 2 + s] = u * rd * 4.2f;
            }
            int m = 0;
            for (int s = 0; s < ds; s++)
            {
                int s1 = (s + 1) % ds;
                st[m++] = 0; st[m++] = 1 + s1; st[m++] = 1 + s;
                // disc edge → inner halo, inner halo → outer halo
                st[m++] = 1 + s; st[m++] = 1 + s1; st[m++] = 1 + ds + s;
                st[m++] = 1 + s1; st[m++] = 1 + ds + s1; st[m++] = 1 + ds + s;
                st[m++] = 1 + ds + s; st[m++] = 1 + ds + s1; st[m++] = 1 + ds * 2 + s;
                st[m++] = 1 + ds + s1; st[m++] = 1 + ds * 2 + s1; st[m++] = 1 + ds * 2 + s;
            }
            sunMesh = new Mesh { name = "SB_SunDisc" };
            sunMesh.MarkDynamic();
            sunMesh.SetVertices(sv);
            sunMesh.SetColors(sunCols);
            sunMesh.SetTriangles(st, 0, false);
            sunMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 20f);
            sunMat = Material(vertexColorShader, "SB_SunDisc", SunQueue);
            var sgo = new GameObject("SunDisc");
            sgo.transform.SetParent(parent, false);
            sgo.AddComponent<MeshFilter>().sharedMesh = sunMesh;
            sunR = sgo.AddComponent<MeshRenderer>();
            Setup(sunR, sunMat);
            sunT = sgo.transform;
        }

        static Material Material(Shader shader, string name, int queue)
        {
            var m = new Material(shader) { name = name };
            m.SetColor("_BaseColor", Color.white);
            MaterialLibrary.MakeTransparent(m, true);
            // never in a depth/normal prepass (SSAO), never casting shadows: it is a pure background fill
            m.SetShaderPassEnabled("DepthOnly", false);
            m.SetShaderPassEnabled("DepthNormals", false);
            m.SetShaderPassEnabled("DepthNormalsOnly", false);
            m.SetShaderPassEnabled("ShadowCaster", false);
            m.SetShaderPassEnabled("MotionVectors", false);
            m.renderQueue = queue;
            return m;
        }

        static void Setup(MeshRenderer r, Material m)
        {
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            r.allowOcclusionWhenDynamic = false;
        }

        // ------------------------------------------------------------------ colours
        static int Segment(float x, out float f)
        {
            if (x <= Keys[0]) { f = 0f; return 0; }
            int last = Keys.Length - 1;
            if (x >= Keys[last]) { f = 1f; return last - 1; }
            for (int i = 0; i < last; i++)
                if (x <= Keys[i + 1]) { f = (x - Keys[i]) / (Keys[i + 1] - Keys[i]); return i; }
            f = 1f; return last - 1;
        }

        /// On-screen-calibrated linear sky colour for a direction (sun elevation/direction given).
        Color Eval(Vector3 d, int k, float f, Vector3 sun, float sunElev)
        {
            Color zen = Color.Lerp(lin[k, 0], lin[k + 1, 0], f);
            Color mid = Color.Lerp(lin[k, 1], lin[k + 1, 1], f);
            Color away = Color.Lerp(lin[k, 2], lin[k + 1, 2], f);
            Color toward = Color.Lerp(lin[k, 3], lin[k + 1, 3], f);

            // how much this direction faces the sun's azimuth (wider glow when the sun is low)
            float hx = d.x, hz = d.z, hl = Mathf.Sqrt(hx * hx + hz * hz);
            float sx = sun.x, sz = sun.z, sl = Mathf.Sqrt(sx * sx + sz * sz);
            float cosA = hl > 1e-4f && sl > 1e-4f ? (hx * sx + hz * sz) / (hl * sl) : 0f;
            float facing = Mathf.Pow(Mathf.Max(0f, 0.5f + 0.5f * cosA), Mathf.Lerp(3.5f, 9f, Mathf.Clamp01(sunElev / 30f)));
            Color horizon = Color.Lerp(away, toward, facing);

            float e = Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) * Mathf.Rad2Deg;
            Color c;
            if (e >= 30f) c = Color.Lerp(mid, zen, Mathf.SmoothStep(0f, 1f, (e - 30f) / 60f));
            else if (e >= 0f)
            {
                float u = e / 30f;
                // the sun-side glow fades out faster with height than the plain horizon brightening
                Color low = Color.Lerp(horizon, Color.Lerp(away, horizon, 0.35f), Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u * 2.2f)));
                c = Color.Lerp(low, mid, Mathf.Sin(u * Mathf.PI * 0.5f));
            }
            else c = horizon * Mathf.Lerp(1f, 0.82f, Mathf.Clamp01(-e / 8f));

            // soft bright halo around the sun itself (art bible: halo ≈ 3× the disc, lighter sky)
            if (sunElev > -2f)
            {
                float cs = Vector3.Dot(d, sun);
                float ang = Mathf.Acos(Mathf.Clamp(cs, -1f, 1f)) * Mathf.Rad2Deg;
                float halo = Mathf.Exp(-ang / 7f) * 0.55f * Mathf.Clamp01((sunElev + 2f) / 4f);
                c = Color.Lerp(c, toward * 1.25f + new Color(0.08f, 0.08f, 0.07f), halo);
            }
            return c;
        }

        /// Recolours the dome when the sun moved (≥ 0.25° elevation or 1° azimuth) or the fog near the dome changed.
        /// recolor = false (camera under water, dome hidden) only refreshes the fog/reflection colours.
        public void Update(Vector3 sun, float sunElev, float sunAzimuth, float fogVisAtDome, Color fogLinear, bool force, bool recolor = true)
        {
            int k = Segment(sunElev, out float f);
            Color away = Color.Lerp(lin[k, 2], lin[k + 1, 2], f);
            Color mid = Color.Lerp(lin[k, 1], lin[k + 1, 1], f);
            Color awayScreen = Color.Lerp(SkyGrade.Hex(Table[k, 2]), SkyGrade.Hex(Table[k + 1, 2]), f);
            HorizonScreen = awayScreen;
            // × 0.93 (bible: 0.94; 0.90 turned the far sea band darker than the near sea)
            Color fogScreen = new Color(awayScreen.r * 0.93f, awayScreen.g * 0.93f, awayScreen.b * 0.93f, 1f);
            FogColor = SkyGrade.InverseGamma(SkyGrade.AcesHueFix(fogScreen));
            ReflectionColor = ToGamma(Color.Lerp(away, mid, 0.45f));
            sunDir = sun;
            sunVisible = sunElev > -1.5f;

            if (mat == null || (!recolor && !force)) return;
            bool changed = force || Mathf.Abs(sunElev - lastElev) > 0.25f || Mathf.Abs(Mathf.DeltaAngle(sunAzimuth, lastAz)) > 1f
                           || Mathf.Abs(fogVisAtDome - lastFogVis) > 0.004f;
            if (!changed) return;
            lastElev = sunElev; lastAz = sunAzimuth; lastFogVis = fogVisAtDome;

            float vis = Mathf.Clamp(fogVisAtDome, 0.05f, 1f);
            for (int i = 0; i < dirs.Length; i++)
            {
                Color c = Eval(dirs[i], k, f, sun, sunElev);
                // the particles shader still fogs the (5 m) dome a little: pre-compensate so the sky stays exact
                if (vis < 0.999f)
                {
                    c.r = Mathf.Max(0f, (c.r - fogLinear.r * (1f - vis)) / vis);
                    c.g = Mathf.Max(0f, (c.g - fogLinear.g * (1f - vis)) / vis);
                    c.b = Mathf.Max(0f, (c.b - fogLinear.b * (1f - vis)) / vis);
                }
                c.a = 1f;
                cols[i] = linearSpace ? c : ToGamma(c);
            }
            mesh.SetColors(cols);

            // sun disc: warm white HDR core, halo fading out; dims into the horizon
            float lowK = Mathf.Clamp01((sunElev + 1.5f) / 6f);
            Color core = Color.Lerp(new Color(2.4f, 1.35f, 0.75f), new Color(3.0f, 2.95f, 2.7f), Mathf.Clamp01(sunElev / 25f)) * lowK;
            Color haloC = Color.Lerp(new Color(1.2f, 0.75f, 0.45f), new Color(1.05f, 1.05f, 0.98f), Mathf.Clamp01(sunElev / 25f));
            int ds = (sunCols.Length - 1) / 3;
            sunCols[0] = Enc(core, 1f);
            for (int s = 0; s < ds; s++)
            {
                sunCols[1 + s] = Enc(core, 1f);
                sunCols[1 + ds + s] = Enc(haloC, 0.45f * lowK);
                sunCols[1 + ds * 2 + s] = Enc(haloC, 0f);
            }
            sunMesh.SetColors(sunCols);
        }

        Color Enc(Color c, float a) { c.a = a; return linearSpace ? c : ToGamma(c); }

        static Color ToGamma(Color c) => new Color(G(c.r), G(c.g), G(c.b), c.a);
        static float G(float c) => c <= 0.0031308f ? c * 12.92f : 1.055f * Mathf.Pow(c, 1f / 2.4f) - 0.055f;

        /// Places dome and sun disc around a camera that is about to render (hidden under water).
        public void Follow(Vector3 camPos, bool show)
        {
            if (mat == null) return;
            if (r.enabled != show) r.enabled = show;
            bool sun = show && sunVisible;
            if (sunR.enabled != sun) sunR.enabled = sun;
            if (!show) return;
            t.position = camPos;
            if (sun)
            {
                float d = Radius * 0.9f;
                sunT.position = camPos + sunDir * d;
                sunT.rotation = Quaternion.LookRotation(-sunDir, Mathf.Abs(sunDir.y) > 0.99f ? Vector3.forward : Vector3.up);
                sunT.localScale = new Vector3(d, d, d);
            }
        }

        public void Destroy()
        {
            if (mesh != null) Object.Destroy(mesh);
            if (sunMesh != null) Object.Destroy(sunMesh);
            if (mat != null) Object.Destroy(mat);
            if (sunMat != null) Object.Destroy(sunMat);
        }
    }
}
