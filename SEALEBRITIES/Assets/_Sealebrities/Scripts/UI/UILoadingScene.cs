using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Sealebrities
{
    /// Round 3 loading screen scene (GDD §19 „Ladebildschirm viel besser gestalten“, §20 Q35 „animierte
    /// Unterwasser-Szene mit Tipps und Fakten“): a self-contained low-poly reef far below the world (stage at
    /// y −5600 on layer GameLayers.Preview) with its own camera rendering into a 16:9 RenderTexture that the
    /// UILoadingScreen shows full-screen behind its UI.
    ///
    /// Contents: the player's OWN diver (DiverModels.Build from Settings.Look) swimming slow laps with the hand camera and
    /// regulator bubbles, a school of blue tangs, two parrotfish, Gerald the turtle and a manta (CreatureFactory models +
    /// FaunaSwimAnimator), additive light shafts, a waving faceted surface band at the top, rising bubbles + plankton,
    /// a How-to-Fish seabed (sand dunes, violet rocks, chunky corals, sea grass) and swaying two-tone kelp.
    ///
    /// Independence from the world: no terrain / water / fog / time of day is read. While (and only while) the scene camera
    /// renders, its own fog (turquoise → deep blue, ExponentialSquared), flat ambient and sun are swapped in and every
    /// other directional light is switched off; everything is restored at endCameraRendering. No post-processing,
    /// no shadows. Animation runs on the unscaled dt handed in by UILoadingScreen.Tick (hitches only slow it down).
    /// Dispose (or Unity destroying the root) removes the callbacks, objects, meshes, materials, textures and the
    /// RenderTexture – zero cost while the loading screen is hidden.
    public sealed class UILoadingScene
    {
        // ------------------------------------------------------------------ layout (stage space: x right, y up, z away from the camera)
        static readonly Vector3 Stage = new Vector3(3200f, -5600f, -2600f);
        const int Layer = GameLayers.Preview;
        const float Fov = 40f;
        const float SurfaceY = 4.5f;
        const float FogDensity = 0.03f;
        static readonly Color FogColor = UIPalette.Rgb(0x2D90BC);
        static readonly Color Ambient = new Color(0.47f, 0.57f, 0.62f);
        static readonly Color SunColor = new Color(1f, 0.97f, 0.9f);
        const float SunIntensity = 0.62f;

        // the diver's lap: an ellipse in front of the camera (near side lower, far side higher)
        const float DiverA = 2.8f, DiverB = 1.3f, DiverZ = 6.6f, DiverY = 0.2f, DiverPeriod = 30f;
        const float DiverTheta0 = Mathf.PI + 0.55f;   // starts left, swimming toward the camera (face visible)

        // the blue tang school: a lazy ellipse behind the diver
        const float SchoolPeriod = 30f;
        static float SchoolOmega => 2f * Mathf.PI / SchoolPeriod;

        // ------------------------------------------------------------------ statics (test hooks)
        /// The live scene (null while the loading screen is hidden).
        public static UILoadingScene Current { get; private set; }
        /// Scenes built and not yet disposed (0 while hidden).
        public static int LiveCount { get; private set; }
        /// RenderTextures created by loading scenes and not yet released (0 while hidden).
        public static int LiveTextures { get; private set; }
        static int hookCount;
        /// True while a scene has its render-pipeline callbacks registered.
        public static bool Hooked => hookCount > 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Current = null; LiveCount = 0; LiveTextures = 0; hookCount = 0; ico = null; }

        // ------------------------------------------------------------------ instance
        GameObject root;
        UILoadingSceneHost host;
        Camera cam;
        Transform camT;
        RenderTexture rt;
        Light sun;
        readonly List<Object> owned = new List<Object>(16);
        readonly List<Light> foreign = new List<Light>(8);
        readonly List<Light> switchedOff = new List<Light>(8);
        bool disposed, counted, hooked, overriding;
        int renderedFrames;
        float time, lightScan, relayer;
        System.Random rng;

        // render state saved while the scene camera renders
        bool prevFog;
        FogMode prevFogMode;
        Color prevFogColor, prevAmbient;
        float prevFogDensity;
        AmbientMode prevAmbientMode;
        SphericalHarmonicsL2 prevProbe, ambientSH;
        Light prevSun;

        // diver
        GameObject diverGo;
        DiverPuppet puppet;
        float diverScale = 1f, diverPrevYaw, diverBank, bubbleTimer = 1.2f, burstTimer;
        int burstLeft;
        Vector3 diverPos;
        Color diverSuit;

        // fauna
        sealed class Swimmer
        {
            public Transform t;
            public FaunaSwimAnimator anim;
            public int kind;           // 0 school, 1 parrotfish, 2 turtle, 3 manta
            public float len, lag, side, up, phase, prevYaw;
        }
        readonly List<Swimmer> swimmers = new List<Swimmer>(20);

        // kelp (one dynamic mesh, two tones)
        struct Blade
        {
            public Vector3 basePos, right;
            public float height, width, phase, amp;
        }
        const int KelpSeg = 12;
        Blade[] blades;
        Vector3[] kelpVerts;
        Mesh kelpMesh;

        // surface band (unlit faceted, vertex coloured)
        const int CeilX = 26, CeilZ = 12;
        const float CeilZ0 = 8f, CeilZ1 = 44f;
        readonly Vector3[] ceilGrid = new Vector3[(CeilX + 1) * (CeilZ + 1)];
        readonly Vector3[] ceilVerts = new Vector3[CeilX * CeilZ * 6];
        readonly Color32[] ceilCols = new Color32[CeilX * CeilZ * 6];
        Mesh ceilMesh;
        static readonly Color CeilDark = UIPalette.Rgb(0x58C2D2), CeilLight = UIPalette.Rgb(0xBDEFF3);

        // light shafts
        const int Rays = 6;
        static readonly float[] RayX = { -8.5f, -4.2f, -0.8f, 2.6f, 6.2f, 10.5f };
        static readonly float[] RayZ = { 17f, 12f, 21f, 10f, 15f, 22f };
        static readonly float[] RayW = { 2.6f, 1.6f, 3.2f, 1.3f, 2.2f, 3f };
        static readonly float[] RayLen = { 13f, 11f, 14f, 10f, 12f, 14f };
        static readonly float[] RayPhase = { 0f, 1.7f, 3.1f, 4.4f, 2.3f, 5.6f };
        readonly Vector3[] rayVerts = new Vector3[Rays * 4];
        readonly Color32[] rayCols = new Color32[Rays * 4];
        Mesh rayMesh;

        // bubbles (regulator bursts + seabed vents) and drifting plankton specks – one dynamic quad mesh
        const int MaxBubbles = 72, Specks = 40, Quads = MaxBubbles + Specks;
        readonly Vector3[] bPos = new Vector3[Quads];
        readonly Vector3[] bVel = new Vector3[Quads];
        readonly float[] bSize = new float[Quads], bAge = new float[Quads], bLife = new float[Quads], bPhase = new float[Quads];
        readonly bool[] bAlive = new bool[Quads];
        readonly Vector3[] bubbleVerts = new Vector3[Quads * 4];
        readonly Color32[] bubbleCols = new Color32[Quads * 4];
        readonly Vector3[] vents = new Vector3[3];
        readonly float[] ventTimer = new float[3];
        int nextBubble, coralCount;
        Mesh bubbleMesh;

        UILoadingScene() { }

        /// Builds the scene; null when 3D rendering is unavailable or the build failed (the loading screen then shows its
        /// flat animated background).
        public static UILoadingScene Create()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return null;
            var s = new UILoadingScene();
            try { s.Build(); }
            catch (Exception e)
            {
                Debug.LogWarning("Sealebrities: the loading scene could not be built – using the flat loading screen. " + e.Message);
                Debug.LogException(e);
                s.Dispose();
                return null;
            }
            return s;
        }

        // ------------------------------------------------------------------ public state (UI + tests)
        public bool Alive => !disposed && root != null;
        public RenderTexture Texture => rt;
        public Camera Camera => cam;
        /// Frames the scene camera actually rendered (counted at endCameraRendering).
        public int RenderedFrames => renderedFrames;
        public float SceneTime => time;
        public bool HasDiver => diverGo != null;
        /// Centre of the swimming diver in stage space.
        public Vector3 DiverPosition => diverPos;
        public Color DiverSuit => diverSuit;
        public GameObject Root => root;
        public int FishCount { get { int n = 0; for (int i = 0; i < swimmers.Count; i++) if (swimmers[i].kind <= 1 && swimmers[i].t != null) n++; return n; } }
        public int BigAnimalCount { get { int n = 0; for (int i = 0; i < swimmers.Count; i++) if (swimmers[i].kind >= 2 && swimmers[i].t != null) n++; return n; } }
        public int BubbleCount { get { int n = 0; for (int i = 0; i < MaxBubbles; i++) if (bAlive[i]) n++; return n; } }
        public int KelpCount => blades != null ? blades.Length : 0;
        public int CoralCount => coralCount;
        /// True only between begin- and endCameraRendering of the scene camera (fog / ambient / sun swapped).
        public bool Overriding => overriding;
        /// Directional lights of the world currently switched off by the scene (0 outside its render).
        public int LightsHeldOff => switchedOff.Count;

        // ================================================================== build
        void Build()
        {
            rng = new System.Random(3535);
            var q = Settings.Data != null ? Settings.Data.quality : GraphicsQuality.High;
            bool low = q == GraphicsQuality.Low, high = q == GraphicsQuality.High;

            int h = Mathf.Clamp(Screen.height, 540, 1080);
            if (low) h = Mathf.Min(h, 720);
            int w = Mathf.RoundToInt(h * 16f / 9f);
            rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = "SB_LoadingScene", antiAliasing = low ? 1 : (high ? 4 : 2), useMipMap = false,
                filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
            };
            LiveTextures++;   // counted from creation: Dispose releases and uncounts it whatever happens below
            rt.Create();

            root = new GameObject("SB_LoadingScene");
            root.layer = Layer;
            root.transform.SetPositionAndRotation(Stage, Quaternion.identity);
            host = root.AddComponent<UILoadingSceneHost>();
            host.Owner = this;
            LiveCount++;
            counted = true;
            Current = this;

            BuildCamera(low);
            BuildSun();
            // water ambient: a little brighter from the surface above than from the seabed below
            ambientSH = new SphericalHarmonicsL2();
            ambientSH.AddAmbientLight(Ambient * 0.88f);
            ambientSH.AddDirectionalLight(Vector3.up, new Color(0.45f, 0.62f, 0.66f), 0.35f);

            BuildSeabed();
            BuildKelp();
            BuildSurface();
            BuildRays();
            BuildBubbles();
            Guarded(BuildDiver, "diver");
            Guarded(() => BuildFauna(low), "fauna");
            SetLayer(root.transform, Layer);

            RenderPipelineManager.beginCameraRendering += OnBegin;
            RenderPipelineManager.endCameraRendering += OnEnd;
            hooked = true;
            hookCount++;
            ScanLights();
            Place(0f);
        }

        static void Guarded(Action a, string what)
        {
            try { a(); }
            catch (Exception e)
            {
                Debug.LogWarning("Sealebrities: loading scene – " + what + " failed: " + e.Message);
                Debug.LogException(e);
            }
        }

        static void SetLayer(Transform t, int layer)
        {
            if (t.gameObject.layer != layer) t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
        }

        T Own<T>(T o) where T : Object
        {
            if (o != null) owned.Add(o);
            return o;
        }

        GameObject Child(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            go.layer = Layer;
            go.transform.SetParent(parent != null ? parent : root.transform, false);
            return go;
        }

        MeshRenderer Renderer(GameObject go, Mesh mesh, Material[] mats)
        {
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = mats;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            r.allowOcclusionWhenDynamic = false;
            return r;
        }

        void BuildCamera(bool low)
        {
            var go = Child("Camera");
            camT = go.transform;
            cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(FogColor.r, FogColor.g, FogColor.b, 1f);
            cam.cullingMask = GameLayers.PreviewMask;
            cam.fieldOfView = Fov;
            cam.nearClipPlane = 0.25f;
            cam.farClipPlane = 95f;
            cam.allowHDR = false;
            cam.allowMSAA = !low;
            cam.useOcclusionCulling = false;
            cam.depth = -60f;
            cam.targetTexture = rt;
            var data = cam.GetUniversalAdditionalCameraData();
            if (data != null)
            {
                data.renderPostProcessing = false;
                data.antialiasing = AntialiasingMode.None;
                data.renderShadows = false;
                data.requiresColorOption = CameraOverrideOption.Off;
                data.requiresDepthOption = CameraOverrideOption.Off;
                data.volumeLayerMask = 0;
                data.stopNaN = false;
                data.dithering = false;
            }

            // gradient backdrop glued to the camera: turquoise above, deep blue below, the fog colour at the horizon.
            // Background queue, no depth: everything else draws over it and the fog barely touches it (0.6 m away).
            var bd = Child("Backdrop", camT);
            bd.transform.localPosition = new Vector3(0f, 0f, 0.6f);
            float hh = 0.6f * Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad) * 1.25f, hw = hh * 16f / 9f * 1.05f;
            float[] stops = { -1f, -0.48f, 0f, 0.4f, 1f };
            Color[] cols = { UIPalette.Rgb(0x154A78), UIPalette.Rgb(0x1C6496), FogColor, UIPalette.Rgb(0x45B0D1), UIPalette.Rgb(0x74D6E1) };
            var v = new Vector3[stops.Length * 2];
            var c = new Color[v.Length];
            var uv = new Vector2[v.Length];
            var tris = new List<int>();
            for (int i = 0; i < stops.Length; i++)
            {
                v[i * 2] = new Vector3(-hw, stops[i] * hh, 0f);
                v[i * 2 + 1] = new Vector3(hw, stops[i] * hh, 0f);
                c[i * 2] = cols[i]; c[i * 2 + 1] = cols[i];
                uv[i * 2] = new Vector2(0f, 0.5f); uv[i * 2 + 1] = new Vector2(1f, 0.5f);
                if (i > 0)
                {
                    int a = (i - 1) * 2;
                    tris.Add(a); tris.Add(a + 2); tris.Add(a + 3);
                    tris.Add(a); tris.Add(a + 3); tris.Add(a + 1);
                }
            }
            var m = Own(new Mesh { name = "SB_LS_Backdrop" });
            m.vertices = v;
            m.colors = c;
            m.uv = uv;
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            var mat = Own(FXAssets.ParticleMaterial("SB_LS_Backdrop", Texture2D.whiteTexture, false, Color.white));
            if (mat != null)
            {
                mat.renderQueue = (int)RenderQueue.Background;
                mat.SetShaderPassEnabled("DepthOnly", false);
                mat.SetShaderPassEnabled("DepthNormals", false);
                mat.SetShaderPassEnabled("DepthNormalsOnly", false);
                mat.SetShaderPassEnabled("ShadowCaster", false);
                mat.SetShaderPassEnabled("MotionVectors", false);
                Renderer(bd, m, new[] { mat });
            }
        }

        void BuildSun()
        {
            var go = Child("Sun");
            go.transform.rotation = Quaternion.LookRotation(new Vector3(0.3f, -0.82f, 0.48f).normalized);
            sun = go.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = SunColor;
            sun.intensity = SunIntensity;
            sun.shadows = LightShadows.None;
            sun.cullingMask = GameLayers.PreviewMask;
            sun.enabled = false;   // only on while the scene camera renders
        }

        // ------------------------------------------------------------------ seabed: sand, rocks, corals, sea grass (one static mesh)
        /// Sand height (deterministic): gentle dunes, rising toward a far ridge.
        static float GroundY(float x, float z)
        {
            float y = -2.4f + Mathf.Max(0f, z - 20f) * 0.03f;
            y += 0.26f * Mathf.Sin(0.33f * x + 0.21f * z) + 0.15f * Mathf.Sin(0.83f * x - 0.57f * z + 1.3f) + 0.06f * Mathf.Sin(1.7f * x + 1.1f * z);
            float ridge = Mathf.Clamp01((z - 34f) / 22f);
            y += 2.4f * ridge * ridge * (3f - 2f * ridge) * (0.65f + 0.35f * Mathf.Sin(0.17f * x + 0.7f));
            return y;
        }

        sealed class Kit
        {
            readonly List<Vector3> v = new List<Vector3>(16384);
            readonly List<Vector3> n = new List<Vector3>(16384);
            readonly List<Color> colors = new List<Color>(32);
            readonly List<List<int>> tris = new List<List<int>>(32);

            List<int> Sub(Color c)
            {
                for (int i = 0; i < colors.Count; i++) if (colors[i] == c) return tris[i];
                colors.Add(c);
                var l = new List<int>(512);
                tris.Add(l);
                return l;
            }

            /// Flat-shaded triangle; a, b, c clockwise seen from the front (the normal is Cross(b − a, c − a)).
            public void Tri(Color col, Vector3 a, Vector3 b, Vector3 c)
            {
                Vector3 nn = Vector3.Cross(b - a, c - a);
                float m = nn.magnitude;
                if (m < 1e-9f) return;
                nn /= m;
                var t = Sub(col);
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(c);
                n.Add(nn); n.Add(nn); n.Add(nn);
                t.Add(i); t.Add(i + 1); t.Add(i + 2);
            }

            public int Count => v.Count;

            public Mesh Build(string name, out Material[] mats)
            {
                var m = new Mesh { name = name };
                if (v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
                m.SetVertices(v);
                m.SetNormals(n);
                m.subMeshCount = tris.Count;
                for (int i = 0; i < tris.Count; i++) m.SetTriangles(tris[i], i, false);
                m.RecalculateBounds();
                mats = new Material[colors.Count];
                for (int i = 0; i < colors.Count; i++) mats[i] = LowPoly.Mat(colors[i]);
                return m;
            }
        }

        float Rand(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        static Color Shade(Color c, float k) => k < 0f ? Color.Lerp(c, Color.black, -k) : Color.Lerp(c, Color.white, k);

        void BuildSeabed()
        {
            var kit = new Kit();
            Color sandA = UIPalette.Rgb(0xE2C890), sandB = UIPalette.Rgb(0xD3B782);
            const int Cols = 34, Rows = 24;
            var g = new Vector3[(Cols + 1) * (Rows + 1)];
            for (int j = 0; j <= Rows; j++)
            {
                float fz = j / (float)Rows;
                float z = 3f + 58f * (0.45f * fz + 0.55f * fz * fz);
                float half = 6f + 0.8f * z;
                for (int i = 0; i <= Cols; i++)
                {
                    float x = (i / (float)Cols * 2f - 1f) * half;
                    g[j * (Cols + 1) + i] = new Vector3(x, GroundY(x, z) + Rand(-0.05f, 0.05f), z);
                }
            }
            for (int j = 0; j < Rows; j++)
                for (int i = 0; i < Cols; i++)
                {
                    Vector3 p00 = g[j * (Cols + 1) + i], p10 = g[j * (Cols + 1) + i + 1];
                    Vector3 p01 = g[(j + 1) * (Cols + 1) + i], p11 = g[(j + 1) * (Cols + 1) + i + 1];
                    Color c = rng.NextDouble() < 0.22 ? sandB : sandA;
                    if (((i + j) & 1) == 0) { kit.Tri(c, p00, p01, p11); kit.Tri(c, p00, p11, p10); }
                    else { kit.Tri(c, p00, p01, p10); kit.Tri(c, p01, p11, p10); }
                }

            // violet-grey rocks (art bible §2.4), near ones first, then far silhouettes in the fog
            Color rockA = UIPalette.Rgb(0x7A7B92), rockB = UIPalette.Rgb(0x5E5F78);
            float[] rocks =
            {
                // x, z, size
                -4.6f, 13f, 1.3f,   -1.4f, 18.5f, 1.0f,   3.8f, 12.5f, 1.1f,   6.4f, 19f, 1.6f,   1.2f, 24f, 1.4f,
                -8.2f, 22f, 1.9f,   10.2f, 26f, 2.2f,   -12f, 30f, 2.6f,
                -18f, 40f, 5.5f,    7f, 36f, 4.5f,   21f, 44f, 6.5f,   -5f, 47f, 6f,   -26f, 52f, 7f,
            };
            for (int i = 0; i + 2 < rocks.Length; i += 3)
            {
                float x = rocks[i], z = rocks[i + 1], s = rocks[i + 2];
                Vector3 p = new Vector3(x, GroundY(x, z) - s * 0.15f, z);
                Rock(kit, p, new Vector3(s * Rand(0.9f, 1.3f), s * Rand(0.55f, 0.8f), s * Rand(0.8f, 1.1f)), Rand(0f, 360f), rockA, rockB, i * 1.7f);
            }

            // coral colonies (chunky 3D, 2–3 flat tones; art bible palette)
            Color[] pal =
            {
                UIPalette.Rgb(0xF2708A), UIPalette.Rgb(0xF28C4A), UIPalette.Rgb(0x8E5CC2), UIPalette.Rgb(0xF2C94C),
                UIPalette.Rgb(0xD9455A), UIPalette.Rgb(0xB59AD9), UIPalette.Rgb(0x3FA9A0), UIPalette.Rgb(0x7E9A3A),
            };
            int tables = 0;
            for (int k = 0; k < 30; k++)
            {
                // patches around the near rocks and in the middle distance
                float x, z;
                if (k < 16)
                {
                    int r = (k % 7) * 3;
                    x = rocks[r] + Rand(-2.2f, 2.2f);
                    z = rocks[r + 1] + Rand(-1.6f, 1.6f);
                }
                else
                {
                    x = Rand(-10f, 10f);
                    z = Rand(11f, 27f);
                }
                if (z < 10f) z = 10f;
                Vector3 p = new Vector3(x, GroundY(x, z) - 0.05f, z);
                Color col = pal[rng.Next(pal.Length)];
                float size = Rand(0.65f, 1.45f) * (z > 20f ? 1.25f : 1f);
                double roll = rng.NextDouble();
                if (roll < 0.45) Branching(kit, p, size, col);
                else if (roll < 0.68) Brain(kit, p, size * 0.55f, col);
                else if (roll < 0.86 || tables >= 4) Anemone(kit, p, size * 0.8f, col);
                else { Table(kit, p, size, col); tables++; }
                coralCount++;
                if (k < 3) vents[k] = p + Vector3.up * size * 0.6f;
            }

            // sea grass tufts
            Color grassA = UIPalette.Rgb(0x6F8F3A), grassB = UIPalette.Rgb(0x55702A);
            for (int t = 0; t < 9; t++)
            {
                float cx = Rand(-9f, 9f), cz = Rand(10.5f, 22f);
                for (int b = 0; b < 9; b++)
                {
                    float x = cx + Rand(-0.7f, 0.7f), z = cz + Rand(-0.5f, 0.5f);
                    Vector3 p = new Vector3(x, GroundY(x, z) - 0.05f, z);
                    float hgt = Rand(0.45f, 1.1f), wd = Rand(0.05f, 0.09f), bend = Rand(-0.25f, 0.25f);
                    kit.Tri((b & 1) == 0 ? grassA : grassB, p - Vector3.right * wd, p + new Vector3(bend, hgt, 0f), p + Vector3.right * wd);
                }
            }

            var mesh = Own(kit.Build("SB_LS_Seabed", out Material[] mats));
            Renderer(Child("Seabed"), mesh, mats);
        }

        // ---- shapes
        static Vector3[] ico;   // subdivided icosahedron (unit directions, 3 per face)

        static Vector3[] Ico()
        {
            if (ico != null) return ico;
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            Vector3[] p =
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            int[] f =
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };
            var outV = new Vector3[f.Length * 4];
            int o = 0;
            for (int i = 0; i < f.Length; i += 3)
            {
                Vector3 a = p[f[i]].normalized, b = p[f[i + 1]].normalized, c = p[f[i + 2]].normalized;
                Vector3 ab = (a + b).normalized, bc = (b + c).normalized, ca = (c + a).normalized;
                outV[o++] = a; outV[o++] = ab; outV[o++] = ca;
                outV[o++] = b; outV[o++] = bc; outV[o++] = ab;
                outV[o++] = c; outV[o++] = ca; outV[o++] = bc;
                outV[o++] = ab; outV[o++] = bc; outV[o++] = ca;
            }
            ico = outV;
            return ico;
        }

        static float Lump(Vector3 d, float seed) =>
            1f + 0.16f * Mathf.Sin(d.x * 4.1f + seed) * Mathf.Sin(d.y * 3.7f + seed * 1.7f) + 0.1f * Mathf.Sin(d.z * 5.3f + seed * 0.7f);

        static void Rock(Kit kit, Vector3 c, Vector3 scale, float yaw, Color top, Color side, float seed)
        {
            var d = Ico();
            Quaternion r = Quaternion.Euler(0f, yaw, 0f);
            for (int i = 0; i < d.Length; i += 3)
            {
                Vector3 a = c + r * Vector3.Scale(d[i] * Lump(d[i], seed), scale);
                Vector3 b = c + r * Vector3.Scale(d[i + 1] * Lump(d[i + 1], seed), scale);
                Vector3 e = c + r * Vector3.Scale(d[i + 2] * Lump(d[i + 2], seed), scale);
                if (a.y < c.y - scale.y * 0.3f && b.y < c.y - scale.y * 0.3f && e.y < c.y - scale.y * 0.3f) continue;   // buried
                float ny = (d[i] + d[i + 1] + d[i + 2]).normalized.y;
                kit.Tri(ny > 0.55f ? top : side, a, b, e);
            }
        }

        /// 5-sided prism (cone when r1 = 0) from a to b with optional caps.
        static void Prism(Kit kit, Color col, Vector3 a, Vector3 b, float r0, float r1, int sides, Color? topCap, Color? bottomCap, float rot = 0f)
        {
            Vector3 axis = b - a;
            float len = axis.magnitude;
            if (len < 1e-4f) return;
            axis /= len;
            Vector3 u = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.95f ? Vector3.up : Vector3.right).normalized;
            Vector3 w = Vector3.Cross(axis, u);
            for (int k = 0; k < sides; k++)
            {
                float a0 = rot + k * Mathf.PI * 2f / sides, a1 = rot + (k + 1) * Mathf.PI * 2f / sides;
                Vector3 d0 = u * Mathf.Cos(a0) + w * Mathf.Sin(a0), d1 = u * Mathf.Cos(a1) + w * Mathf.Sin(a1);
                Vector3 p0 = a + d0 * r0, p1 = a + d1 * r0, q0 = b + d0 * r1, q1 = b + d1 * r1;
                kit.Tri(col, p0, p1, q1);
                if (r1 > 1e-4f) kit.Tri(col, p0, q1, q0);
                if (topCap.HasValue && r1 > 1e-4f) kit.Tri(topCap.Value, b, q0, q1);
                if (bottomCap.HasValue) kit.Tri(bottomCap.Value, a, p1, p0);
            }
        }

        void Branching(Kit kit, Vector3 p, float size, Color col)
        {
            Color dark = Shade(col, -0.25f), tip = Shade(col, 0.15f);
            int n = 5 + rng.Next(3);
            for (int k = 0; k < n; k++)
            {
                float yaw = (k / (float)n) * Mathf.PI * 2f + Rand(-0.3f, 0.3f);
                float tilt = Rand(0.15f, 0.7f);
                Vector3 dir = new Vector3(Mathf.Sin(tilt) * Mathf.Cos(yaw), Mathf.Cos(tilt), Mathf.Sin(tilt) * Mathf.Sin(yaw));
                float len = size * Rand(0.55f, 1f);
                Vector3 p0 = p + new Vector3(Rand(-0.08f, 0.08f), 0f, Rand(-0.08f, 0.08f)) * size;
                Vector3 p1 = p0 + dir * len * 0.5f;
                Vector3 dir2 = (dir + new Vector3(Rand(-0.3f, 0.3f), 0.25f, Rand(-0.3f, 0.3f))).normalized;
                Vector3 p2 = p1 + dir2 * len * 0.5f;
                Prism(kit, dark, p0, p1, size * 0.085f, size * 0.07f, 5, null, null, k);
                Prism(kit, col, p1, p2, size * 0.07f, size * 0.055f, 5, tip, null, k);
                if ((k & 1) == 0)
                {
                    Vector3 side = (dir2 + new Vector3(Mathf.Cos(yaw + 1.3f), 0.1f, Mathf.Sin(yaw + 1.3f)) * 0.8f).normalized;
                    Prism(kit, col, p1, p1 + side * len * 0.32f, size * 0.05f, size * 0.04f, 5, tip, null, k);
                }
            }
        }

        void Brain(Kit kit, Vector3 p, float radius, Color col)
        {
            Color a = Shade(col, -0.1f), b = Shade(col, -0.32f);
            var d = Ico();
            Vector3 s = new Vector3(radius, radius * 0.68f, radius);
            Vector3 c = p - Vector3.up * radius * 0.12f;
            float seed = Rand(0f, 10f);
            for (int i = 0; i < d.Length; i += 3)
            {
                Vector3 cen = (d[i] + d[i + 1] + d[i + 2]) / 3f;
                if (cen.y < -0.12f) continue;
                Color tone = Mathf.Sin(cen.x * 9f + cen.z * 7f + seed) > 0f ? a : b;
                kit.Tri(tone, c + Vector3.Scale(d[i], s), c + Vector3.Scale(d[i + 1], s), c + Vector3.Scale(d[i + 2], s));
            }
        }

        void Table(Kit kit, Vector3 p, float size, Color col)
        {
            Color dark = Shade(col, -0.28f), top = Shade(col, 0.1f);
            float hgt = size * 0.55f, r = size * 0.75f;
            Prism(kit, dark, p, p + Vector3.up * hgt, size * 0.1f, size * 0.08f, 5, null, null);
            Vector3 a = p + Vector3.up * hgt, b = a + Vector3.up * 0.12f;
            Prism(kit, col, a, b, r, r * 1.05f, 8, top, dark, Rand(0f, 1f));
        }

        void Anemone(Kit kit, Vector3 p, float size, Color col)
        {
            Color body = Shade(col, -0.2f), tip = Shade(col, 0.25f);
            Vector3 top = p + Vector3.up * size * 0.3f;
            Prism(kit, body, p, top, size * 0.22f, size * 0.26f, 6, body, null);
            int n = 9;
            for (int k = 0; k < n; k++)
            {
                float yaw = k / (float)n * Mathf.PI * 2f + Rand(-0.2f, 0.2f);
                float tilt = Rand(0.2f, 0.75f);
                Vector3 dir = new Vector3(Mathf.Sin(tilt) * Mathf.Cos(yaw), Mathf.Cos(tilt), Mathf.Sin(tilt) * Mathf.Sin(yaw));
                Vector3 b0 = top + new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw)) * size * 0.14f;
                Prism(kit, tip, b0, b0 + dir * size * Rand(0.35f, 0.6f), size * 0.045f, 0f, 4, null, null, k);
            }
        }

        // ------------------------------------------------------------------ kelp
        void BuildKelp()
        {
            // x, z, height, width, tone (0 light, 1 dark)
            float[] spec =
            {
                -9.6f, 15.5f, 6.0f, 0.38f, 0f,   -8.4f, 17.5f, 5.4f, 0.34f, 1f,   -10.9f, 18.5f, 6.2f, 0.36f, 0f,   -7.3f, 13.2f, 5.0f, 0.32f, 1f,
                8.6f, 14.2f, 5.8f, 0.36f, 1f,    9.9f, 16.4f, 6.2f, 0.38f, 0f,    11.2f, 18.8f, 5.9f, 0.34f, 1f,    7.2f, 18.2f, 5.2f, 0.32f, 0f,
                2.6f, 29f, 5.8f, 0.42f, 0f,     -3.4f, 31f, 5.6f, 0.42f, 1f,
                -2.95f, 4.6f, 6.6f, 0.5f, 1f,    3.05f, 4.9f, 6.6f, 0.5f, 1f,
            };
            int n = spec.Length / 5, rows = KelpSeg + 1;
            blades = new Blade[n];
            kelpVerts = new Vector3[n * rows * 2];
            var normals = new Vector3[kelpVerts.Length];
            var trisA = new List<int>(n * KelpSeg * 6);
            var trisB = new List<int>(n * KelpSeg * 6);
            for (int b = 0; b < n; b++)
            {
                float x = spec[b * 5], z = spec[b * 5 + 1];
                float yaw = Rand(-0.5f, 0.5f);
                var bl = new Blade
                {
                    basePos = new Vector3(x, GroundY(x, z) - 0.2f, z),
                    right = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw)),
                    height = spec[b * 5 + 2],
                    width = spec[b * 5 + 3],
                    phase = Rand(0f, 6.283f),
                };
                bl.amp = 0.22f + 0.05f * bl.height;
                blades[b] = bl;
                Vector3 nrm = (Vector3.Cross(Vector3.up, bl.right) + Vector3.up * 0.3f).normalized;
                var list = spec[b * 5 + 4] < 0.5f ? trisA : trisB;
                for (int j = 0; j < rows; j++)
                {
                    int v = (b * rows + j) * 2;
                    normals[v] = nrm;
                    normals[v + 1] = nrm;
                    if (j == KelpSeg) continue;
                    // L0, L1, R1 / L0, R1, R0 – clockwise seen from the camera
                    list.Add(v); list.Add(v + 2); list.Add(v + 3);
                    list.Add(v); list.Add(v + 3); list.Add(v + 1);
                }
            }
            UpdateKelp();
            kelpMesh = Own(new Mesh { name = "SB_LS_Kelp" });
            kelpMesh.MarkDynamic();
            kelpMesh.vertices = kelpVerts;
            kelpMesh.normals = normals;
            kelpMesh.subMeshCount = 2;
            kelpMesh.SetTriangles(trisA, 0, false);
            kelpMesh.SetTriangles(trisB, 1, false);
            kelpMesh.bounds = new Bounds(new Vector3(0f, 1f, 18f), new Vector3(40f, 14f, 40f));
            Renderer(Child("Kelp"), kelpMesh, new[] { LowPoly.Mat(UIPalette.Rgb(0x7E9A3A)), LowPoly.Mat(UIPalette.Rgb(0x55702A)) });
        }

        void UpdateKelp()
        {
            if (blades == null || kelpVerts == null) return;
            int rows = KelpSeg + 1;
            for (int b = 0; b < blades.Length; b++)
            {
                var bl = blades[b];
                for (int j = 0; j < rows; j++)
                {
                    float f = j / (float)KelpSeg;
                    float bendW = f * Mathf.Sqrt(f);
                    float sway = bl.amp * bendW * Mathf.Sin(time * 0.95f + bl.phase - f * 2.2f)
                                 + bl.amp * 0.45f * f * Mathf.Sin(time * 0.57f + bl.phase * 1.7f);
                    Vector3 c = bl.basePos + new Vector3(sway, f * bl.height, sway * 0.3f);
                    float wj = j == KelpSeg ? bl.width * 0.12f
                        : bl.width * (0.62f + 0.38f * Mathf.Sin(j * 1.9f + bl.phase)) * (1f - 0.4f * f);
                    int v = (b * rows + j) * 2;
                    kelpVerts[v] = c - bl.right * (wj * 0.5f);
                    kelpVerts[v + 1] = c + bl.right * (wj * 0.5f);
                }
            }
        }

        // ------------------------------------------------------------------ surface band
        void BuildSurface()
        {
            int n = ceilVerts.Length;
            var tris = new int[n];
            for (int i = 0; i < n; i++) tris[i] = i;
            UpdateSurface();
            ceilMesh = Own(new Mesh { name = "SB_LS_Surface" });
            ceilMesh.MarkDynamic();
            ceilMesh.vertices = ceilVerts;
            ceilMesh.colors32 = ceilCols;
            ceilMesh.uv = new Vector2[n];
            ceilMesh.triangles = tris;
            ceilMesh.bounds = new Bounds(new Vector3(0f, SurfaceY, 26f), new Vector3(80f, 3f, 40f));
            var mat = Own(FXAssets.ParticleMaterial("SB_LS_Surface", Texture2D.whiteTexture, false, Color.white));
            if (mat != null) Renderer(Child("Surface"), ceilMesh, new[] { mat });
        }

        static float SurfaceHeight(float x, float z, float t) =>
            SurfaceY + 0.22f * Mathf.Sin(0.42f * x + 1.15f * t) + 0.15f * Mathf.Sin(0.61f * z - 0.9f * t + 0.3f * x)
            + 0.07f * Mathf.Sin(1.3f * x + 1.1f * z + 1.9f * t);

        void UpdateSurface()
        {
            for (int iz = 0; iz <= CeilZ; iz++)
            {
                float z = CeilZ0 + (CeilZ1 - CeilZ0) * (iz / (float)CeilZ);
                float half = 4f + 0.68f * z;
                for (int ix = 0; ix <= CeilX; ix++)
                {
                    float x = (ix / (float)CeilX * 2f - 1f) * half;
                    ceilGrid[iz * (CeilX + 1) + ix] = new Vector3(x, SurfaceHeight(x, z, time), z);
                }
            }
            int o = 0;
            for (int iz = 0; iz < CeilZ; iz++)
            {
                float zf = (iz + 0.5f) / CeilZ;
                // fade out toward the far edge (the fog hides the rest)
                float far = 1f - Mathf.Clamp01((zf - 0.72f) / 0.28f);
                for (int ix = 0; ix < CeilX; ix++)
                {
                    float xf = Mathf.Abs((ix + 0.5f) / CeilX * 2f - 1f);
                    float side = 1f - Mathf.Clamp01((xf - 0.86f) / 0.14f);
                    byte alpha = (byte)(255f * far * side);
                    Vector3 p00 = ceilGrid[iz * (CeilX + 1) + ix], p10 = ceilGrid[iz * (CeilX + 1) + ix + 1];
                    Vector3 p01 = ceilGrid[(iz + 1) * (CeilX + 1) + ix], p11 = ceilGrid[(iz + 1) * (CeilX + 1) + ix + 1];
                    bool flip = ((ix + iz) & 1) == 0;
                    Vector3 a = p00, b = p01, c = flip ? p11 : p10;
                    o = SurfaceTri(o, a, b, c, alpha);
                    if (flip) o = SurfaceTri(o, p00, p11, p10, alpha);
                    else o = SurfaceTri(o, p01, p11, p10, alpha);
                }
            }
        }

        int SurfaceTri(int o, Vector3 a, Vector3 b, Vector3 c, byte alpha)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            float m = n.magnitude;
            if (m > 1e-6f) n /= m;
            // facets tilted toward the camera / the sun read brighter (unlit, flat per triangle)
            float f = Mathf.Clamp01(0.5f + 2.6f * (0.55f * n.x - 0.83f * n.z));
            Color col = Color.Lerp(CeilDark, CeilLight, f);
            if (f > 0.86f) col = Color.Lerp(col, Color.white, (f - 0.86f) / 0.14f * 0.8f);
            var c32 = new Color32((byte)(col.r * 255f), (byte)(col.g * 255f), (byte)(col.b * 255f), alpha);
            ceilVerts[o] = a; ceilVerts[o + 1] = b; ceilVerts[o + 2] = c;
            ceilCols[o] = c32; ceilCols[o + 1] = c32; ceilCols[o + 2] = c32;
            return o + 3;
        }

        // ------------------------------------------------------------------ light shafts
        void BuildRays()
        {
            var uvs = new Vector2[Rays * 4];
            var tris = new int[Rays * 6];
            for (int i = 0; i < Rays; i++)
            {
                int v = i * 4, t = i * 6;
                uvs[v] = new Vector2(0f, 1f); uvs[v + 1] = new Vector2(1f, 1f); uvs[v + 2] = new Vector2(1f, 0f); uvs[v + 3] = new Vector2(0f, 0f);
                tris[t] = v; tris[t + 1] = v + 1; tris[t + 2] = v + 2;
                tris[t + 3] = v; tris[t + 4] = v + 2; tris[t + 5] = v + 3;
            }
            UpdateRays();
            rayMesh = Own(new Mesh { name = "SB_LS_Rays" });
            rayMesh.MarkDynamic();
            rayMesh.vertices = rayVerts;
            rayMesh.colors32 = rayCols;
            rayMesh.uv = uvs;
            rayMesh.triangles = tris;
            rayMesh.bounds = new Bounds(new Vector3(0f, 0f, 16f), new Vector3(50f, 20f, 30f));
            var tex = Own(FXAssets.GodRay(32, 128));
            var mat = Own(FXAssets.ParticleMaterial("SB_LS_Rays", tex, true, new Color(0xDF / 255f, 0xFB / 255f, 1f, 1f)));
            if (mat != null) Renderer(Child("Rays"), rayMesh, new[] { mat });
        }

        void UpdateRays()
        {
            Vector3 dir = new Vector3(0.34f, -1f, 0.1f).normalized;
            Vector3 eye = camT != null ? camT.localPosition : Vector3.zero;
            for (int i = 0; i < Rays; i++)
            {
                float sway = Mathf.Sin(time * 0.55f + RayPhase[i]) * 0.45f;
                Vector3 top = new Vector3(RayX[i] + sway, SurfaceY + 0.15f, RayZ[i]);
                Vector3 bottom = top + dir * RayLen[i];
                Vector3 right = Vector3.Cross(dir, eye - (top + bottom) * 0.5f);
                float rm = right.magnitude;
                right = rm > 1e-4f ? right / rm : Vector3.right;
                float w0 = RayW[i] * 0.5f, w1 = RayW[i] * 0.85f;
                int v = i * 4;
                rayVerts[v] = top - right * w0;
                rayVerts[v + 1] = top + right * w0;
                rayVerts[v + 2] = bottom + right * w1;
                rayVerts[v + 3] = bottom - right * w1;
                float a = 0.17f * (0.62f + 0.38f * Mathf.Sin(time * 0.37f + RayPhase[i] * 2.1f));
                var c = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
                rayCols[v] = c; rayCols[v + 1] = c; rayCols[v + 2] = c; rayCols[v + 3] = c;
            }
        }

        // ------------------------------------------------------------------ bubbles + plankton
        void BuildBubbles()
        {
            var uvs = new Vector2[Quads * 4];
            var tris = new int[Quads * 6];
            for (int i = 0; i < Quads; i++)
            {
                int v = i * 4, t = i * 6;
                uvs[v] = new Vector2(0f, 0f); uvs[v + 1] = new Vector2(0f, 1f); uvs[v + 2] = new Vector2(1f, 1f); uvs[v + 3] = new Vector2(1f, 0f);
                tris[t] = v; tris[t + 1] = v + 1; tris[t + 2] = v + 2;
                tris[t + 3] = v; tris[t + 4] = v + 2; tris[t + 5] = v + 3;
            }
            // plankton specks drift in a box in front of the camera
            for (int i = MaxBubbles; i < Quads; i++)
            {
                bAlive[i] = true;
                bPos[i] = new Vector3(Rand(-9f, 9f), Rand(-2f, 4f), Rand(3.5f, 20f));
                bVel[i] = new Vector3(Rand(-0.05f, 0.05f), Rand(-0.03f, 0.04f), Rand(-0.03f, 0.03f));
                bSize[i] = Rand(0.018f, 0.04f);
                bPhase[i] = Rand(0f, 6.283f);
            }
            // a few bubbles already on their way up so the very first frame is alive
            for (int k = 0; k < 10; k++)
            {
                Vector3 v0 = vents[k % 3];
                Emit(v0 + new Vector3(0f, Rand(0f, 4f), 0f), Rand(0.04f, 0.09f));
            }
            UpdateBubbles(0f);
            bubbleMesh = Own(new Mesh { name = "SB_LS_Bubbles" });
            bubbleMesh.MarkDynamic();
            bubbleMesh.vertices = bubbleVerts;
            bubbleMesh.colors32 = bubbleCols;
            bubbleMesh.uv = uvs;
            bubbleMesh.triangles = tris;
            bubbleMesh.bounds = new Bounds(new Vector3(0f, 1f, 14f), new Vector3(40f, 14f, 30f));
            var tex = Own(FXAssets.FacetBubble(64));
            var mat = Own(FXAssets.ParticleMaterial("SB_LS_Bubbles", tex, false, new Color(1f, 1f, 1f, 0.85f)));
            if (mat != null) Renderer(Child("Bubbles"), bubbleMesh, new[] { mat });
        }

        void Emit(Vector3 p, float size)
        {
            for (int k = 0; k < MaxBubbles; k++)
            {
                int i = (nextBubble + k) % MaxBubbles;
                if (bAlive[i]) continue;
                nextBubble = (i + 1) % MaxBubbles;
                bAlive[i] = true;
                bPos[i] = p;
                bVel[i] = new Vector3(Rand(-0.04f, 0.04f), 0.55f + size * 4f, Rand(-0.03f, 0.03f));
                bSize[i] = size;
                bAge[i] = 0f;
                bLife[i] = Rand(7f, 11f);
                bPhase[i] = Rand(0f, 6.283f);
                return;
            }
        }

        void UpdateBubbles(float dt)
        {
            // seabed vents
            for (int k = 0; k < vents.Length; k++)
            {
                if (vents[k] == Vector3.zero) continue;
                ventTimer[k] -= dt;
                if (ventTimer[k] > 0f) continue;
                ventTimer[k] = Rand(0.35f, 1.1f);
                Emit(vents[k] + new Vector3(Rand(-0.1f, 0.1f), 0f, Rand(-0.1f, 0.1f)), Rand(0.035f, 0.08f));
            }
            Vector3 right = camT != null ? camT.right : Vector3.right, up = camT != null ? camT.up : Vector3.up;
            for (int i = 0; i < Quads; i++)
            {
                int v = i * 4;
                if (!bAlive[i])
                {
                    bubbleVerts[v] = bubbleVerts[v + 1] = bubbleVerts[v + 2] = bubbleVerts[v + 3] = Vector3.zero;
                    continue;
                }
                float alpha, size;
                Vector3 p;
                if (i >= MaxBubbles)
                {
                    // plankton: slow drift, wrapped inside the box
                    bPos[i] += bVel[i] * dt;
                    p = bPos[i];
                    if (p.x < -9f) p.x += 18f; else if (p.x > 9f) p.x -= 18f;
                    if (p.y < -2f) p.y += 6f; else if (p.y > 4f) p.y -= 6f;
                    if (p.z < 3.5f) p.z += 16.5f; else if (p.z > 20f) p.z -= 16.5f;
                    bPos[i] = p;
                    p.x += Mathf.Sin(time * 0.7f + bPhase[i]) * 0.06f;
                    size = bSize[i];
                    alpha = 0.32f + 0.12f * Mathf.Sin(time * 1.3f + bPhase[i]);
                }
                else
                {
                    bAge[i] += dt;
                    bPos[i] += bVel[i] * dt;
                    p = bPos[i];
                    p.x += Mathf.Sin(bAge[i] * 5.5f + bPhase[i]) * 0.05f;
                    size = bSize[i] * (1f + 0.2f * bAge[i]);
                    float toTop = SurfaceY - 0.25f - p.y;
                    if (toTop <= 0f || bAge[i] >= bLife[i]) { bAlive[i] = false; bubbleVerts[v] = bubbleVerts[v + 1] = bubbleVerts[v + 2] = bubbleVerts[v + 3] = Vector3.zero; continue; }
                    alpha = Mathf.Clamp01(bAge[i] / 0.15f) * Mathf.Clamp01(toTop / 0.6f) * Mathf.Clamp01((bLife[i] - bAge[i]) / 0.5f) * 0.85f;
                }
                Vector3 r = right * size, u = up * size;
                bubbleVerts[v] = p - r - u;
                bubbleVerts[v + 1] = p - r + u;
                bubbleVerts[v + 2] = p + r + u;
                bubbleVerts[v + 3] = p + r - u;
                var c = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha) * 255f));
                bubbleCols[v] = c; bubbleCols[v + 1] = c; bubbleCols[v + 2] = c; bubbleCols[v + 3] = c;
            }
        }

        // ------------------------------------------------------------------ diver
        void BuildDiver()
        {
            var build = DiverModels.Build;
            if (build == null) { DiverBuilder.Install(); build = DiverModels.Build; }
            if (build == null) return;
            DiverLook look = Settings.Look;
            look.sitting = false;
            try { look.sponsor = DiverSponsor.CurrentIndex; } catch (Exception) { look.sponsor = 0; }
            diverGo = build(look);
            if (diverGo == null) return;
            diverGo.name = "LS_Diver";
            diverGo.transform.SetParent(root.transform, false);
            diverSuit = look.suit;
            diverScale = look.HeightScale;
            var cols = diverGo.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++) cols[i].enabled = false;
            DiverLod.Force(diverGo, true);
            puppet = diverGo.GetComponent<DiverPuppet>();
            if (puppet == null) puppet = diverGo.GetComponentInChildren<DiverPuppet>();
            if (puppet != null)
            {
                puppet.SetShadows(false);
                // diving with the hand camera – the vlogger at work
                puppet.SetPose(true, false, 0.9f, HoldItem.Camera, HoldItem.None, false);
                PlaceDiver(0f);
                // the puppet blends into a new pose over ~1 s: settle it now so the first frame already shows a swimmer
                if (Time.deltaTime > 0f)
                    for (int i = 0; i < 24; i++) puppet.SendMessage("LateUpdate", SendMessageOptions.DontRequireReceiver);
            }
            else
            {
                var p = diverGo.GetComponentInChildren<IDiverPuppet>();
                if (p != null) p.SetPose(true, false, 0.9f, HoldItem.Camera, HoldItem.None, false);
            }
            PlaceDiver(0f);
        }

        void PlaceDiver(float dt)
        {
            if (diverGo == null) return;
            float w = 2f * Mathf.PI / DiverPeriod;
            float th = DiverTheta0 + time * w;
            float s = Mathf.Sin(th), c = Mathf.Cos(th);
            Vector3 center = new Vector3(DiverA * c, DiverY + 0.32f * s + 0.12f * Mathf.Sin(time * 0.9f), DiverZ + DiverB * s);
            Vector3 vel = new Vector3(-DiverA * s * w, 0.32f * c * w + 0.108f * Mathf.Cos(time * 0.9f), DiverB * c * w);
            float yaw = Mathf.Atan2(vel.x, vel.z) * Mathf.Rad2Deg;
            float yawRate = dt > 1e-4f ? Mathf.DeltaAngle(diverPrevYaw, yaw) / dt : 0f;
            diverPrevYaw = yaw;
            // lean into the turn, nose follows the climb
            diverBank = Mathf.Lerp(diverBank, Mathf.Clamp(-yawRate * 0.9f, -16f, 16f), 1f - Mathf.Exp(-3f * dt));
            float pitch = -Mathf.Atan2(vel.y, new Vector2(vel.x, vel.z).magnitude) * Mathf.Rad2Deg * 0.6f;
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(pitch, 0f, diverBank);
            Vector3 fwd = rot * Vector3.forward, upv = rot * Vector3.up;
            // the pelvis pivot sits PivotY above the root and is about the middle of the swimming body (head ahead, fins behind)
            Vector3 rootPos = center - upv * (DiverBuilder.PivotY * diverScale);
            diverGo.transform.SetLocalPositionAndRotation(rootPos, rot);
            diverPos = center;

            // regulator: a burst of 5 bubbles every ~2.6 s
            bubbleTimer -= dt;
            if (bubbleTimer <= 0f) { bubbleTimer = 2.6f; burstLeft = 5; burstTimer = 0f; }
            if (burstLeft > 0 && dt > 0f)
            {
                burstTimer -= dt;
                if (burstTimer <= 0f)
                {
                    burstTimer = 0.07f;
                    burstLeft--;
                    Transform head = puppet != null ? puppet.Head : null;
                    Vector3 hp = head != null ? head.position - Stage : center + fwd * 0.7f;
                    Emit(hp + fwd * 0.12f + upv * 0.06f + new Vector3(Rand(-0.04f, 0.04f), 0f, Rand(-0.04f, 0.04f)), Rand(0.035f, 0.075f));
                }
            }
        }

        // ------------------------------------------------------------------ fauna
        void BuildFauna(bool low)
        {
            int school = low ? 8 : 12;
            for (int i = 0; i < school; i++)
            {
                var s = Make("blue_tang", 0, Rand(0.27f, 0.33f));
                if (s == null) break;
                s.lag = i * 0.16f + Rand(0f, 0.08f);
                s.side = Rand(-0.7f, 0.7f);
                s.up = Rand(-0.45f, 0.45f);
                s.phase = Rand(0f, 6.283f);
            }
            var p1 = Make("parrotfish", 1, 0.62f);
            if (p1 != null) { p1.phase = 0.4f; p1.side = 0f; }
            var p2 = Make("parrotfish", 1, 0.52f);
            if (p2 != null) { p2.phase = 3.3f; p2.side = 1.2f; }
            var turtle = Make("green_turtle", 2, 1.25f);
            if (turtle != null) turtle.phase = 9.5f;   // starts left of centre
            var manta = Make("manta", 3, 3.4f);
            if (manta != null) manta.phase = 12.5f;    // starts right of centre
            PlaceFauna(0f);
        }

        Swimmer Make(string id, int kind, float len)
        {
            var sp = SpeciesDatabase.Get(id);
            if (sp == null) return null;
            GameObject go = null;
            try { go = CreatureFactory.Build(sp); }
            catch (Exception e) { Debug.LogWarning("Sealebrities: loading scene could not build " + id + ": " + e.Message); }
            if (go == null) return null;
            go.name = "LS_" + id;
            go.transform.SetParent(root.transform, false);
            go.transform.localScale = Vector3.one * len;
            var cols = go.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++) cols[i].enabled = false;
            var rs = go.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rs.Length; i++)
            {
                rs[i].shadowCastingMode = ShadowCastingMode.Off;
                rs[i].reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            var s = new Swimmer { t = go.transform, kind = kind, len = len };
            var rig = go.GetComponent<CreatureRig>();
            if (rig != null)
            {
                if (rig.lureLight != null) rig.lureLight.enabled = false;
                try
                {
                    s.anim = new FaunaSwimAnimator();
                    s.anim.Setup(rig, sp);
                    s.anim.SetLureEnabled(false);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("Sealebrities: loading scene – no swim animation for " + id + ": " + e.Message);
                    s.anim = null;
                }
            }
            swimmers.Add(s);
            return s;
        }

        static Vector3 SchoolPos(float tau)
        {
            float a = tau * SchoolOmega;
            return new Vector3(6.2f * Mathf.Sin(a), 1.35f + 0.5f * Mathf.Sin(a * 0.5f + 1f), 12f + 2.8f * Mathf.Cos(a));
        }

        static Vector3 SchoolVel(float tau)
        {
            float a = tau * SchoolOmega, w = SchoolOmega;
            return new Vector3(6.2f * w * Mathf.Cos(a), 0.25f * w * Mathf.Cos(a * 0.5f + 1f), -2.8f * w * Mathf.Sin(a));
        }

        void PlaceFauna(float dt)
        {
            float schoolT0 = -0.9f / SchoolOmega;   // the school starts left of centre on the far side, heading right
            for (int i = 0; i < swimmers.Count; i++)
            {
                var s = swimmers[i];
                if (s.t == null) continue;
                Vector3 p, v;
                float roll = 0f, speed01 = 1f;
                switch (s.kind)
                {
                    case 0:
                    {
                        float tau = time + schoolT0 - s.lag;
                        p = SchoolPos(tau);
                        v = SchoolVel(tau);
                        Vector3 fwd = v.normalized;
                        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized, up = Vector3.Cross(fwd, right);
                        float wob = Mathf.Sin(time * 1.3f + s.phase);
                        p += right * (s.side + wob * 0.12f) + up * (s.up + Mathf.Sin(time * 0.9f + s.phase * 1.7f) * 0.1f);
                        v = fwd + right * (Mathf.Cos(time * 1.3f + s.phase) * 0.1f);
                        break;
                    }
                    case 1:
                    {
                        float a = time * 0.12f + s.phase;
                        p = new Vector3(4.8f * Mathf.Sin(a), -1.45f + 0.18f * Mathf.Sin(time * 0.5f + s.phase), 14f + s.side + 1.6f * Mathf.Cos(a));
                        v = new Vector3(4.8f * 0.12f * Mathf.Cos(a), 0.09f * Mathf.Cos(time * 0.5f + s.phase), -1.6f * 0.12f * Mathf.Sin(a));
                        speed01 = 0.8f;
                        break;
                    }
                    case 2:
                    {
                        float x = -14.5f + Mathf.Repeat(time * 0.42f + s.phase, 29f);
                        p = new Vector3(x, 1.0f + 0.45f * Mathf.Sin(time * 0.21f), 17.5f + 1f * Mathf.Sin(time * 0.07f));
                        v = new Vector3(0.42f, 0.0945f * Mathf.Cos(time * 0.21f), 0.07f * Mathf.Cos(time * 0.07f));
                        speed01 = 0.8f;
                        break;
                    }
                    default:
                    {
                        float x = 18.5f - Mathf.Repeat(time * 0.55f + s.phase, 37f);
                        p = new Vector3(x, 2.6f + 0.6f * Mathf.Sin(time * 0.16f + 1f), 24f + 1.4f * Mathf.Sin(time * 0.05f));
                        v = new Vector3(-0.55f, 0.096f * Mathf.Cos(time * 0.16f + 1f), 0.07f * Mathf.Cos(time * 0.05f));
                        roll = -24f;   // tilt the wing toward the camera so the wide silhouette reads
                        speed01 = 0.7f;
                        break;
                    }
                }
                if (v.sqrMagnitude < 1e-8f) v = Vector3.forward;
                Quaternion rot = Quaternion.LookRotation(v.normalized, Vector3.up);
                if (roll != 0f) rot *= Quaternion.Euler(0f, 0f, roll);
                s.t.SetLocalPositionAndRotation(p, rot);
                float yaw = Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;
                float yawRate = dt > 1e-4f ? Mathf.DeltaAngle(s.prevYaw, yaw) / dt : 0f;
                s.prevYaw = yaw;
                if (s.anim != null && dt > 0f) s.anim.Animate(dt, speed01, s.len, 0.35f, Mathf.Clamp(yawRate, -90f, 90f), 0f, 0f, 1f, time + s.phase);
            }
        }

        // ================================================================== per frame
        public void Tick(float dt)
        {
            if (disposed) return;
            if (root == null) { Dispose(); return; }   // destroyed from outside
            if (overriding) Restore();                  // a render that never reached endCameraRendering
            if (float.IsNaN(dt) || dt < 0f) dt = 0f;
            time += dt;
            lightScan -= dt;
            if (lightScan <= 0f) { lightScan = 2f; ScanLights(); }
            // the puppet builds its hand camera lazily (layer 0) – pull new parts onto the scene layer
            relayer -= dt;
            if (relayer <= 0f) { relayer = time < 3f ? 0.25f : 2f; SetLayer(root.transform, Layer); }
            Place(dt);
        }

        void Place(float dt)
        {
            if (camT != null)
            {
                camT.SetLocalPositionAndRotation(
                    new Vector3(Mathf.Sin(time * 0.21f) * 0.3f, Mathf.Sin(time * 0.17f + 1f) * 0.12f, 0f),
                    Quaternion.Euler(Mathf.Sin(time * 0.13f) * 0.6f, Mathf.Sin(time * 0.11f) * 1.3f, 0f));
            }
            PlaceDiver(dt);
            PlaceFauna(dt);
            UpdateKelp();
            if (kelpMesh != null) kelpMesh.vertices = kelpVerts;
            UpdateSurface();
            if (ceilMesh != null) { ceilMesh.vertices = ceilVerts; ceilMesh.colors32 = ceilCols; }
            UpdateRays();
            if (rayMesh != null) { rayMesh.vertices = rayVerts; rayMesh.colors32 = rayCols; }
            UpdateBubbles(dt);
            if (bubbleMesh != null) { bubbleMesh.vertices = bubbleVerts; bubbleMesh.colors32 = bubbleCols; }
        }

        // ================================================================== render-state override (only for the scene camera)
        void ScanLights()
        {
            foreign.Clear();
            var lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            for (int i = 0; i < lights.Length; i++)
            {
                var l = lights[i];
                if (l == null || l == sun || l.type != LightType.Directional) continue;
                foreign.Add(l);
            }
        }

        void OnBegin(ScriptableRenderContext ctx, Camera c)
        {
            if (disposed || overriding || c == null || c != cam) return;
            overriding = true;
            prevFog = RenderSettings.fog;
            prevFogMode = RenderSettings.fogMode;
            prevFogColor = RenderSettings.fogColor;
            prevFogDensity = RenderSettings.fogDensity;
            prevAmbientMode = RenderSettings.ambientMode;
            prevAmbient = RenderSettings.ambientLight;
            prevProbe = RenderSettings.ambientProbe;
            prevSun = RenderSettings.sun;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = FogColor;
            RenderSettings.fogDensity = FogDensity;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Ambient;
            RenderSettings.ambientProbe = ambientSH;

            switchedOff.Clear();
            for (int i = 0; i < foreign.Count; i++) SwitchOff(foreign[i]);
            SwitchOff(prevSun);   // a sun created since the last scan
            if (sun != null)
            {
                sun.enabled = true;
                RenderSettings.sun = sun;
            }
        }

        void SwitchOff(Light l)
        {
            if (l == null || l == sun || !l.enabled || l.type != LightType.Directional) return;
            l.enabled = false;
            switchedOff.Add(l);
        }

        void OnEnd(ScriptableRenderContext ctx, Camera c)
        {
            if (!overriding || c == null || c != cam) return;
            Restore();
            renderedFrames++;
        }

        void Restore()
        {
            overriding = false;
            if (sun != null) sun.enabled = false;
            for (int i = 0; i < switchedOff.Count; i++) if (switchedOff[i] != null) switchedOff[i].enabled = true;
            switchedOff.Clear();
            RenderSettings.fog = prevFog;
            RenderSettings.fogMode = prevFogMode;
            RenderSettings.fogColor = prevFogColor;
            RenderSettings.fogDensity = prevFogDensity;
            RenderSettings.ambientMode = prevAmbientMode;
            RenderSettings.ambientLight = prevAmbient;
            RenderSettings.ambientProbe = prevProbe;
            RenderSettings.sun = prevSun;
            prevSun = null;
        }

        // ================================================================== teardown
        /// Removes everything: callbacks, objects, runtime meshes / materials / textures and the RenderTexture.
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (hooked)
            {
                RenderPipelineManager.beginCameraRendering -= OnBegin;
                RenderPipelineManager.endCameraRendering -= OnEnd;
                hooked = false;
                hookCount = Mathf.Max(0, hookCount - 1);
            }
            if (overriding) Restore();
            if (cam != null) cam.targetTexture = null;
            if (host != null) host.Owner = null;
            host = null;
            if (root != null) Object.Destroy(root);
            root = null;
            diverGo = null;
            puppet = null;
            swimmers.Clear();
            for (int i = 0; i < owned.Count; i++) if (owned[i] != null) Object.Destroy(owned[i]);
            owned.Clear();
            kelpMesh = null; ceilMesh = null; rayMesh = null; bubbleMesh = null;
            if (rt != null)
            {
                rt.Release();
                Object.Destroy(rt);
                rt = null;
                LiveTextures = Mathf.Max(0, LiveTextures - 1);
            }
            if (counted)
            {
                counted = false;
                LiveCount = Mathf.Max(0, LiveCount - 1);
            }
            foreign.Clear();
            switchedOff.Clear();
            cam = null;
            camT = null;
            sun = null;
            if (Current == this) Current = null;
        }
    }
}
