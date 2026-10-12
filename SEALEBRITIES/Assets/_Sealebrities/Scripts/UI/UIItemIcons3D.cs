using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Sealebrities
{
    /// Round 3 (GDD §19 „Chopper-Shop: jedes Item mit eigenem Bild“, §20 Q32 „Katalog mit großen Bildern“): one big,
    /// unique 3D picture per shop item. Every item id gets its own small low-poly model (built procedurally here in the
    /// How-to-Fish item look – smooth soft shapes, flat matte palette colours; the hand cameras reuse FilmRigBuilder's
    /// models without the mitten), rendered once by a temporary camera on the icon-studio layer (31) far below the world
    /// (soft 3/4 view, flat studio light, transparent background, 2× supersampled, soft drop shadow) into a 256 px
    /// Texture2D that is cached per key. Never leaves objects behind, works before the world exists (only needs a GPU),
    /// and returns null when rendering is impossible – callers then keep their 2D glyph (UIItemIcon does that).
    ///
    /// Keys: gear id ("tank_t2"), backpack items "item:&lt;id&gt;" (= ItemDatabase.DeliveryId), snacks "food:&lt;id&gt;"
    /// (= FoodDatabase.DeliveryId) – the same strings as Delivery.itemId, so delivery trackers can show the picture too.
    /// Bare item / food ids work as well. Unknown ids get a wooden crate with a sticker in a colour hashed from the key.
    public static class UIItemIcons3D
    {
        public const int Size = 256;
        const int Super = 2;
        const int Px = Size * Super;
        /// The icon studio layer (also used by CreatureIcons; the view-model light mirroring skips it).
        const int Layer = GameLayers.CreatureIcons;
        const float Fov = 18f;
        /// Far below the world (deepest seabed ≈ −1000 m) and away from the creature icon stage (y −5000); not further
        /// out, so float precision stays well below a millimetre.
        static readonly Vector3 Stage = new Vector3(0f, -3000f, 0f);
        static readonly Color32 ShadowColor = new Color32(30, 27, 43, 255);

        static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();
        static readonly HashSet<string> Failed = new HashSet<string>();
        static readonly List<Vector3> verts = new List<Vector3>(2048);
        static readonly List<Light> disabledLights = new List<Light>();
        static Texture2D readback, softDisc;
        static Color32[] bufA, bufB, outPx;
        static float[] alphaA, alphaB;

        /// Number of icons rendered so far (tests: lazy rendering) and the duration of the last render.
        public static int RenderCount { get; private set; }
        public static float LastRenderMs { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Cache.Clear();
            Failed.Clear();
            verts.Clear();
            disabledLights.Clear();
            readback = null;
            softDisc = null;
            bufA = bufB = outPx = null;
            alphaA = alphaB = null;
            RenderCount = 0;
            LastRenderMs = 0f;
        }

        // ================================================================== public API
        public static string Key(GearItem g) => g != null ? g.id : null;
        public static string Key(ItemDef d) => d != null ? ItemDatabase.DeliveryId(d) : null;
        public static string Key(FoodDef f) => f != null ? FoodDatabase.DeliveryId(f) : null;

        /// The cached picture or null (never renders).
        public static Texture2D Peek(string key) => key != null && Cache.TryGetValue(key, out var t) && t != null ? t : null;

        /// True when rendering this key failed (no GPU, empty image) – show the glyph instead.
        public static bool HasFailed(string key) => key != null && Failed.Contains(key);

        /// The picture for a key, rendered right now when it is not cached yet (null when rendering is impossible).
        public static Texture2D Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            var cached = Peek(key);
            if (cached != null) return cached;
            if (Failed.Contains(key)) return null;
            float t0 = Time.realtimeSinceStartup;
            Texture2D tex = null;
            try { tex = Render(key); }
            catch (Exception e)
            {
                Debug.LogWarning("Sealebrities: shop icon render failed for " + key + " (" + e.Message + ")");
                tex = null;
            }
            LastRenderMs = (Time.realtimeSinceStartup - t0) * 1000f;
            if (tex == null)
            {
                Failed.Add(key);
                return null;
            }
            tex.name = "SB_ItemIcon_" + key;
            Cache[key] = tex;
            RenderCount++;
            return tex;
        }

        /// Soft round white spotlight (tint it) – the "soft round background" behind a picture.
        public static Texture2D SoftDisc
        {
            get
            {
                if (softDisc != null) return softDisc;
                const int n = 128;
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float k = Mathf.Clamp01((d - 0.6f) / 0.4f);
                        float a = 1f - k * k * (3f - 2f * k);
                        px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                    }
                softDisc = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "SB_ShopSoftDisc", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                softDisc.SetPixels32(px);
                softDisc.Apply(false, true);
                return softDisc;
            }
        }

        // ------------------------------------------------------------------ test helpers (pixel statistics)
        /// FNV-1a hash over all pixels (two identical pictures → same hash).
        public static uint PixelHash(Texture2D t)
        {
            if (t == null) return 0u;
            var px = t.GetPixels32(0);
            uint h = 2166136261u;
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                h = (h ^ c.r) * 16777619u;
                h = (h ^ c.g) * 16777619u;
                h = (h ^ c.b) * 16777619u;
                h = (h ^ c.a) * 16777619u;
            }
            return h;
        }

        /// Fraction of (mostly) opaque pixels.
        public static float Coverage(Texture2D t)
        {
            if (t == null) return 0f;
            var px = t.GetPixels32(0);
            int n = 0;
            for (int i = 0; i < px.Length; i++) if (px[i].a > 128) n++;
            return px.Length > 0 ? n / (float)px.Length : 0f;
        }

        /// Fraction of opaque pixels on the 2 px border (> 0.05 = the model is cut off by the frame).
        public static float EdgeCoverage(Texture2D t)
        {
            if (t == null) return 0f;
            var px = t.GetPixels32(0);
            int w = t.width, h = t.height, n = 0, tot = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (x > 1 && x < w - 2 && y > 1 && y < h - 2) continue;
                    tot++;
                    if (px[y * w + x].a > 128) n++;
                }
            return tot > 0 ? n / (float)tot : 0f;
        }

        /// All pictures of the keys on one light sheet (columns × rows, reading order) – AutoShot / playtest review.
        public static Texture2D ContactSheet(IList<string> keys, int columns)
        {
            if (keys == null || keys.Count == 0) return null;
            int n = keys.Count;
            columns = Mathf.Clamp(columns, 1, n);
            int rows = (n + columns - 1) / columns;
            const int Gap = 12;
            int w = columns * (Size + Gap) + Gap, h = rows * (Size + Gap) + Gap;
            var px = new Color32[w * h];
            var bg = new Color32(0xEA, 0xF4, 0xF6, 255);
            for (int i = 0; i < px.Length; i++) px[i] = bg;
            for (int i = 0; i < n; i++)
            {
                int col = i % columns, row = i / columns;
                int ox = Gap + col * (Size + Gap), oy = h - (row + 1) * (Size + Gap);
                var tex = Get(keys[i]);
                Color32[] src = tex != null ? tex.GetPixels32(0) : null;
                for (int y = 0; y < Size; y++)
                    for (int x = 0; x < Size; x++)
                    {
                        Color32 under = new Color32(255, 255, 255, 255);
                        if (src == null)
                        {
                            bool cross = Mathf.Abs(x - y) < 4 || Mathf.Abs(x - (Size - 1 - y)) < 4;
                            px[(oy + y) * w + ox + x] = cross ? new Color32(224, 85, 58, 255) : under;
                            continue;
                        }
                        var c = src[y * Size + x];
                        float a = c.a / 255f;
                        px[(oy + y) * w + ox + x] = new Color32((byte)(c.r * a + under.r * (1f - a)), (byte)(c.g * a + under.g * (1f - a)), (byte)(c.b * a + under.b * (1f - a)), 255);
                    }
            }
            var sheet = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "SB_ShopIconSheet" };
            sheet.SetPixels32(px);
            sheet.Apply(false, false);
            return sheet;
        }

        // ================================================================== render
        struct View
        {
            public float yaw, pitch, roll, margin;
            public View(float yaw, float pitch, float roll = 0f, float margin = 1.14f)
            {
                this.yaw = yaw; this.pitch = pitch; this.roll = roll; this.margin = margin;
            }
        }

        static readonly View Upright = new View(30f, -18f);

        /// Camera looks along +Z: yaw shows a side (front + right), pitch shows the top, roll lays long items on the
        /// picture's diagonal (applied last, in the picture plane).
        static Quaternion Orientation(View v) =>
            Quaternion.AngleAxis(v.roll, Vector3.forward) * Quaternion.AngleAxis(v.pitch, Vector3.right) * Quaternion.AngleAxis(v.yaw, Vector3.up);

        static Texture2D Render(string key)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return null;
            GameObject model = null, camGo = null, keyLight = null, fillLight = null, rimLight = null;
            FilmCameraRig rig = null;
            RenderTexture rt = null;
            RenderTexture prevActive = RenderTexture.active;
            bool prevFog = RenderSettings.fog;
            SphericalHarmonicsL2 prevProbe = RenderSettings.ambientProbe;
            AmbientMode prevMode = RenderSettings.ambientMode;
            Color prevAmbient = RenderSettings.ambientLight;
            Light prevSun = RenderSettings.sun;
            disabledLights.Clear();
            try
            {
                model = new GameObject("SB_ShopIconModel");
                var root = model.transform;
                root.SetPositionAndRotation(Stage, Quaternion.identity);
                View view;
                try { view = BuildModel(key, root, out rig); }
                catch (Exception e)
                {
                    Debug.LogWarning("Sealebrities: shop icon model failed for " + key + ", using a crate (" + e.Message + ")");
                    for (int i = root.childCount - 1; i >= 0; i--) Object.DestroyImmediate(root.GetChild(i).gameObject);
                    view = BuildCrate(root, key);
                }
                root.rotation = Orientation(view);
                SetLayer(root, Layer);
                if (!Measure(root, out Vector3 min, out Vector3 max)) return null;
                // every model is drawn ~1 m big: millimetre details (sesame, rings) stay exact far from the origin
                float ext = Mathf.Max(max.x - min.x, Mathf.Max(max.y - min.y, max.z - min.z));
                if (ext > 1e-5f)
                {
                    float k = 1f / ext;
                    root.localScale = Vector3.one * k;
                    min = Stage + (min - Stage) * k;
                    max = Stage + (max - Stage) * k;
                }

                Vector3 size = max - min;
                Vector3 c = (min + max) * 0.5f;
                float half = Mathf.Max(Mathf.Max(size.x, size.y) * 0.5f, 0.005f) * view.margin;
                float front = half / Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad);
                camGo = new GameObject("SB_ShopIconCamera") { layer = Layer };
                camGo.transform.SetPositionAndRotation(new Vector3(c.x, c.y, min.z - front), Quaternion.identity);
                var cam = camGo.AddComponent<Camera>();
                cam.enabled = false;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                cam.cullingMask = 1 << Layer;
                cam.fieldOfView = Fov;
                cam.nearClipPlane = Mathf.Max(0.005f, front * 0.5f);
                cam.farClipPlane = front + size.z + 1f;
                cam.allowHDR = false;
                cam.allowMSAA = false;
                cam.useOcclusionCulling = false;
                var data = cam.GetUniversalAdditionalCameraData();
                if (data != null)
                {
                    data.renderPostProcessing = false;
                    data.antialiasing = AntialiasingMode.None;
                    data.renderShadows = false;
                }

                // flat How-to-Fish studio light: clear key from the upper left front, cool fill from the right, warm rim
                keyLight = MakeLight("SB_ShopIconKey", new Vector3(0.45f, -0.7f, 0.55f), new Color(1f, 0.97f, 0.92f), 1.1f);
                fillLight = MakeLight("SB_ShopIconFill", new Vector3(-0.65f, -0.15f, 0.6f), new Color(0.86f, 0.92f, 1f), 0.45f);
                rimLight = MakeLight("SB_ShopIconRim", new Vector3(-0.2f, -0.35f, -0.9f), new Color(1f, 0.95f, 0.88f), 0.45f);
                var lights = Object.FindObjectsByType<Light>();
                for (int i = 0; i < lights.Length; i++)
                {
                    var l = lights[i];
                    if (l == null || !l.enabled || l.type != LightType.Directional || l.gameObject.layer == Layer) continue;
                    l.enabled = false;
                    disabledLights.Add(l);
                }
                RenderSettings.sun = keyLight.GetComponent<Light>();
                RenderSettings.fog = false;
                var amb = new Color(0.52f, 0.53f, 0.58f);
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = amb;
                var sh = new SphericalHarmonicsL2();
                sh.AddAmbientLight(amb);
                RenderSettings.ambientProbe = sh;

                EnsureBuffers();
                rt = RenderTexture.GetTemporary(Px, Px, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default, 1);
                cam.targetTexture = rt;
                cam.Render();
                Readback(rt, bufA);
                if (AlphaLost(bufA))
                {
                    // the pipeline dropped the alpha channel: render once more on white and derive it from the difference
                    cam.backgroundColor = new Color(1f, 1f, 1f, 0f);
                    cam.Render();
                    Readback(rt, bufB);
                    RecoverAlpha();
                }
                cam.targetTexture = null;
                return Finish();
            }
            finally
            {
                RenderTexture.active = prevActive;
                for (int i = 0; i < disabledLights.Count; i++) if (disabledLights[i] != null) disabledLights[i].enabled = true;
                disabledLights.Clear();
                RenderSettings.sun = prevSun;
                RenderSettings.fog = prevFog;
                RenderSettings.ambientMode = prevMode;
                RenderSettings.ambientLight = prevAmbient;
                RenderSettings.ambientProbe = prevProbe;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                Kill(model); Kill(camGo); Kill(keyLight); Kill(fillLight); Kill(rimLight);
                if (rig != null)
                {
                    for (int i = 0; i < rig.owned.Count; i++) if (rig.owned[i] != null) Object.DestroyImmediate(rig.owned[i]);
                    rig.owned.Clear();
                    rig.lampGlows.Clear();
                    rig.root = null;
                }
            }
        }

        static void EnsureBuffers()
        {
            if (bufA == null) bufA = new Color32[Px * Px];
            if (bufB == null) bufB = new Color32[Px * Px];
            if (outPx == null) outPx = new Color32[Size * Size];
            if (alphaA == null) alphaA = new float[Size * Size];
            if (alphaB == null) alphaB = new float[Size * Size];
            if (readback == null) readback = new Texture2D(Px, Px, TextureFormat.RGBA32, false) { name = "SB_ShopIconReadback" };
        }

        static void Readback(RenderTexture rt, Color32[] into)
        {
            RenderTexture.active = rt;
            readback.ReadPixels(new Rect(0, 0, Px, Px), 0, 0, false);
            readback.GetPixelData<Color32>(0).CopyTo(into);
        }

        static bool AlphaLost(Color32[] px)
        {
            int opaque = 0;
            if (px[0].a > 128) opaque++;
            if (px[Px - 1].a > 128) opaque++;
            if (px[(Px - 1) * Px].a > 128) opaque++;
            if (px[Px * Px - 1].a > 128) opaque++;
            return opaque >= 3;
        }

        /// Black (bufA) and white (bufB) renders → alpha = 1 − difference, colour = black render / alpha.
        static void RecoverAlpha()
        {
            for (int i = 0; i < bufA.Length; i++)
            {
                var b = bufA[i];
                var w = bufB[i];
                float diff = ((w.r - b.r) + (w.g - b.g) + (w.b - b.b)) / (3f * 255f);
                float a = Mathf.Clamp01(1f - diff);
                if (a < 0.004f) { bufA[i] = new Color32(0, 0, 0, 0); continue; }
                float inv = 1f / a;
                bufA[i] = new Color32((byte)Mathf.Min(255f, b.r * inv), (byte)Mathf.Min(255f, b.g * inv), (byte)Mathf.Min(255f, b.b * inv), (byte)Mathf.RoundToInt(a * 255f));
            }
        }

        /// 2×2 alpha-weighted downsample, blank check, soft drop shadow → the final mip-mapped texture.
        static Texture2D Finish()
        {
            int covered = 0;
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float r = 0f, g = 0f, b = 0f, a = 0f;
                    for (int dy = 0; dy < Super; dy++)
                        for (int dx = 0; dx < Super; dx++)
                        {
                            var c = bufA[(y * Super + dy) * Px + x * Super + dx];
                            float ca = c.a / 255f;
                            r += c.r * ca; g += c.g * ca; b += c.b * ca; a += ca;
                        }
                    float inv = a > 1e-4f ? 1f / a : 0f;
                    byte alpha = (byte)Mathf.Clamp(a / (Super * Super) * 255f, 0f, 255f);
                    if (alpha > 128) covered++;
                    outPx[y * Size + x] = new Color32((byte)Mathf.Clamp(r * inv, 0f, 255f), (byte)Mathf.Clamp(g * inv, 0f, 255f), (byte)Mathf.Clamp(b * inv, 0f, 255f), alpha);
                }
            if (covered < Size * Size / 250) return null;   // nothing visible
            AddShadow();
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.SetPixels32(outPx);
            tex.Apply(true, false);
            return tex;
        }

        /// Soft ink-violet drop shadow down-right under the item (art bible §2.5: icons with a soft drop shadow).
        static void AddShadow()
        {
            const int Radius = 6, Ox = 5, Oy = 8;
            const float Strength = 0.24f;
            for (int i = 0; i < outPx.Length; i++) alphaA[i] = outPx[i].a / 255f;
            BlurH(alphaA, alphaB, Radius);
            BlurV(alphaB, alphaA, Radius);
            BlurH(alphaA, alphaB, Radius);
            BlurV(alphaB, alphaA, Radius);
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    int sx = x - Ox, sy = y + Oy;
                    if (sx < 0 || sy >= Size) continue;
                    float sa = alphaA[sy * Size + sx] * Strength;
                    if (sa <= 0.002f) continue;
                    int i = y * Size + x;
                    var c = outPx[i];
                    float ia = c.a / 255f;
                    float k = sa * (1f - ia);
                    float oa = ia + k;
                    if (oa <= 1e-4f) continue;
                    float inv = 1f / oa;
                    outPx[i] = new Color32((byte)Mathf.Clamp((c.r * ia + ShadowColor.r * k) * inv, 0f, 255f),
                                           (byte)Mathf.Clamp((c.g * ia + ShadowColor.g * k) * inv, 0f, 255f),
                                           (byte)Mathf.Clamp((c.b * ia + ShadowColor.b * k) * inv, 0f, 255f),
                                           (byte)Mathf.Clamp(oa * 255f, 0f, 255f));
                }
        }

        static void BlurH(float[] src, float[] dst, int r)
        {
            float norm = 1f / (2 * r + 1);
            for (int y = 0; y < Size; y++)
            {
                int row = y * Size;
                float acc = 0f;
                for (int k = -r; k <= r; k++) acc += src[row + Mathf.Clamp(k, 0, Size - 1)];
                for (int x = 0; x < Size; x++)
                {
                    dst[row + x] = acc * norm;
                    acc += src[row + Mathf.Min(x + r + 1, Size - 1)] - src[row + Mathf.Max(x - r, 0)];
                }
            }
        }

        static void BlurV(float[] src, float[] dst, int r)
        {
            float norm = 1f / (2 * r + 1);
            for (int x = 0; x < Size; x++)
            {
                float acc = 0f;
                for (int k = -r; k <= r; k++) acc += src[Mathf.Clamp(k, 0, Size - 1) * Size + x];
                for (int y = 0; y < Size; y++)
                {
                    dst[y * Size + x] = acc * norm;
                    acc += src[Mathf.Min(y + r + 1, Size - 1) * Size + x] - src[Mathf.Max(y - r, 0) * Size + x];
                }
            }
        }

        /// Tight bounds (world space) of every visible mesh vertex below the root.
        static bool Measure(Transform root, out Vector3 min, out Vector3 max)
        {
            min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            bool any = false;
            var filters = root.GetComponentsInChildren<MeshFilter>(false);
            for (int i = 0; i < filters.Length; i++)
            {
                var mf = filters[i];
                if (mf == null || mf.sharedMesh == null) continue;
                var mr = mf.GetComponent<MeshRenderer>();
                if (mr == null || !mr.enabled) continue;
                var m = mf.sharedMesh;
                Matrix4x4 mtx = mf.transform.localToWorldMatrix;
                if (m.isReadable)
                {
                    m.GetVertices(verts);
                    for (int k = 0; k < verts.Count; k++)
                    {
                        Vector3 p = mtx.MultiplyPoint3x4(verts[k]);
                        min = Vector3.Min(min, p);
                        max = Vector3.Max(max, p);
                        any = true;
                    }
                }
                else
                {
                    Bounds b = m.bounds;
                    for (int k = 0; k < 8; k++)
                    {
                        Vector3 p = mtx.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3((k & 1) == 0 ? -1f : 1f, (k & 2) == 0 ? -1f : 1f, (k & 4) == 0 ? -1f : 1f)));
                        min = Vector3.Min(min, p);
                        max = Vector3.Max(max, p);
                        any = true;
                    }
                }
            }
            verts.Clear();
            return any;
        }

        static GameObject MakeLight(string name, Vector3 dir, Color c, float intensity)
        {
            var go = new GameObject(name) { layer = Layer };
            go.transform.SetPositionAndRotation(Stage, Quaternion.LookRotation(dir.normalized));
            var l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.color = c;
            l.intensity = intensity;
            l.shadows = LightShadows.None;
            l.cullingMask = 1 << Layer;
            return go;
        }

        static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
        }

        static void Kill(GameObject go)
        {
            if (go != null) Object.DestroyImmediate(go);
        }

        static uint Hash(string s)
        {
            uint h = 2166136261u;
            if (s != null) for (int i = 0; i < s.Length; i++) h = (h ^ s[i]) * 16777619u;
            return h;
        }

        // ================================================================== model kit (shared cached meshes only)
        static Material Mat(string hex) => LowPoly.Mat(LowPoly.Hex(hex));
        static Material Glow(string baseHex, string emitHex, float intensity) => MaterialLibrary.Emissive(LowPoly.Hex(baseHex), LowPoly.Hex(emitHex), intensity);

        static Transform Node(Transform p, Vector3 pos, Vector3 euler)
        {
            var t = new GameObject("n").transform;
            t.SetParent(p, false);
            t.localPosition = pos;
            t.localRotation = Quaternion.Euler(euler);
            return t;
        }

        static MeshRenderer Part(Transform p, Mesh m, Material mat, Vector3 pos, Quaternion rot, Vector3 scale)
        {
            var go = new GameObject("p");
            var t = go.transform;
            t.SetParent(p, false);
            t.localPosition = pos;
            t.localRotation = rot;
            t.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>();
            if (m != null && m.subMeshCount > 1)
            {
                var mats = new Material[m.subMeshCount];
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                r.sharedMaterials = mats;
            }
            else r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return r;
        }

        static MeshRenderer Part(Transform p, Mesh m, Material mat, Vector3 pos, Vector3 euler = default) => Part(p, m, mat, pos, Quaternion.Euler(euler), Vector3.one);

        /// Rounded box centred at c.
        static MeshRenderer Box(Transform p, Vector3 c, Vector3 size, float round, Material m, Vector3 euler = default) =>
            Part(p, FilmMesh.RoundedBox(size, round), m, c, euler);

        /// Smooth ellipsoid centred at c.
        static MeshRenderer Blob(Transform p, Vector3 c, Vector3 radii, Material m, Vector3 euler = default) =>
            Part(p, FilmMesh.Ellipsoid(radii, 20, 12), m, c, euler);

        static MeshRenderer Ball(Transform p, Vector3 c, float r, Material m) => Part(p, FilmMesh.Sphere(r, 20), m, c);

        /// Cylinder with soft rims along local Y, centred at c.
        static MeshRenderer Disc(Transform p, Vector3 c, float r, float h, Material m, Vector3 euler = default) =>
            Part(p, FilmMesh.RoundedCylinder(r, h, 28, Mathf.Min(r, h * 0.5f) * 0.35f), m, c, euler);

        /// Frustum along local Y, centred at c (closed caps).
        static MeshRenderer Cone(Transform p, Vector3 c, float rBottom, float rTop, float h, Material m, Vector3 euler = default) =>
            Part(p, FilmMesh.Frustum(rBottom, rTop, h, 28, true), m, c, euler);

        /// Open sleeve (label, stripe, tape) around a body along local Y, centred at c.
        static MeshRenderer Band(Transform p, Vector3 c, float r, float h, Material m, Vector3 euler = default) =>
            Part(p, FilmMesh.Frustum(r, r, h, 28, false), m, c, euler);

        /// Torus (ring in the local XZ plane).
        static MeshRenderer Ring(Transform p, Vector3 c, float major, float minor, Material m, Vector3 euler = default) =>
            Part(p, DiverMeshes.Torus(major, minor, 28, 8), m, c, Quaternion.Euler(euler), Vector3.one);

        static MeshRenderer RingS(Transform p, Vector3 c, float major, float minor, Material m, Vector3 euler, Vector3 scale) =>
            Part(p, DiverMeshes.Torus(major, minor, 28, 8), m, c, Quaternion.Euler(euler), scale);

        /// Smooth capsule from basePos along local Y.
        static MeshRenderer Caps(Transform p, Vector3 basePos, float r, float len, Material m, Vector3 euler = default) =>
            Part(p, DiverMeshes.SmoothCapsule(r, len, 24, 5, 2), m, basePos, euler);

        /// Tapered capsule from basePos along local Y (round end rBottom at the base).
        static MeshRenderer TCaps(Transform p, Vector3 basePos, float rBottom, float rTop, float len, Material m, Vector3 euler = default) =>
            Part(p, DiverMeshes.TaperCapsule(rBottom, rTop, len, 18, 3), m, basePos, euler);

        /// Rod (frustum) from a to b.
        static MeshRenderer Seg(Transform p, Vector3 a, Vector3 b, float ra, float rb, Material m)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 1e-5f) return null;
            return Part(p, FilmMesh.Frustum(ra, rb, len, 18, true), m, (a + b) * 0.5f, Quaternion.FromToRotation(Vector3.up, d / len), Vector3.one);
        }

        static MeshRenderer Lathe(Transform p, string key, Vector2[] prof, Material m, Vector3 pos, Vector3 euler, Vector3 scale) =>
            Part(p, DiverMeshes.Cached("shopicon_" + key, () => DiverMeshes.LatheY(prof, 20)), m, pos, Quaternion.Euler(euler), scale);

        /// Flat shape in the local XY plane (front −Z), extruded by depth.
        static MeshRenderer Plate(Transform p, string key, Vector2[] outline, float depth, Material m, Vector3 pos, Vector3 euler = default) =>
            Part(p, DiverMeshes.Plate("shopicon_" + key, outline, depth), m, pos, euler);

        /// Point on a vertical cylinder of radius r around the local Y axis: angle 0 = front (−Z), 90 = right (+X).
        static Vector3 Around(float r, float deg, float y)
        {
            float a = deg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(a) * r, y, -Mathf.Cos(a) * r);
        }

        // ================================================================== models by key
        static View BuildModel(string key, Transform root, out FilmCameraRig rig)
        {
            rig = null;
            var food = FoodDatabase.FromDeliveryId(key);
            if (food != null) return BuildFood(root, food);
            var item = ItemDatabase.FromDeliveryId(key);
            if (item != null) return BuildItem(root, item, key);
            var gear = GearDatabase.Get(key);
            if (gear != null) return BuildGear(root, gear, out rig);
            item = ItemDatabase.Get(key);
            if (item != null)
            {
                var f = item.Food;
                return f != null ? BuildFood(root, f) : BuildItem(root, item, key);
            }
            food = FoodDatabase.Get(key);
            if (food != null) return BuildFood(root, food);
            return BuildCrate(root, key);
        }

        static View BuildGear(Transform t, GearItem g, out FilmCameraRig rig)
        {
            rig = null;
            switch (g.slot)
            {
                case GearSlot.Tank: return BuildTank(t, g.tier);
                case GearSlot.Fins: return g.tier >= 3 ? BuildScooter(t) : BuildFins(t, g.tier);
                case GearSlot.Lamp: return g.tier >= 3 ? BuildStrobes(t) : g.tier == 2 ? BuildVideoLight(t) : BuildFlashlight(t);
                case GearSlot.Camera: return BuildCamera(t, g.tier, out rig);
                case GearSlot.CameraMod: return g.nightVision ? BuildNightVision(t) : BuildZoomLens(t);
                case GearSlot.Rod: return BuildRod(t, g.tier);
                case GearSlot.Boat: return g.tier >= 2 ? BuildRib(t) : g.tier == 1 ? BuildOutboard(t) : BuildOldBoat(t);
                case GearSlot.Compressor: return BuildCompressor(t);
                case GearSlot.BoatWinch: return BuildWinch(t);
                case GearSlot.Radio: return BuildRadio(t);
                case GearSlot.Part: return BuildBattery(t);
                default: return BuildCrate(t, g.id);
            }
        }

        static View BuildItem(Transform t, ItemDef d, string key)
        {
            switch (d.id)
            {
                case ItemIds.SpearT1: return BuildWoodSpear(t);
                case ItemIds.SpearT2: return BuildSteelSpear(t);
                case ItemIds.Speargun: return BuildSpeargun(t);
                case ItemIds.Stick: return BuildStick(t);
                default: return BuildCrate(t, key);
            }
        }

        // ------------------------------------------------------------------ tanks (DiverGear colours)
        static void TankBody(Transform t, float x, float z, float r, float len, Material body)
        {
            Caps(t, new Vector3(x, 0f, z), r, len, body);
            Cone(t, new Vector3(x, len + r * 0.08f, z), r * 0.45f, r * 0.32f, r * 0.3f, body);
        }

        static void Valve(Transform t, float x, float top, float z, Material body, Material knob)
        {
            Disc(t, new Vector3(x, top + 0.035f, z), 0.026f, 0.07f, body);
            Disc(t, new Vector3(x, top + 0.078f, z), 0.006f, 0.02f, body);
            Ring(t, new Vector3(x, top + 0.088f, z), 0.022f, 0.008f, knob);
            Disc(t, new Vector3(x + 0.035f, top + 0.045f, z), 0.02f, 0.06f, body, new Vector3(0f, 0f, 90f));
        }

        static View BuildTank(Transform t, int tier)
        {
            Material valve = Mat("#3A3E52"), chrome = Mat("#B8BEC6"), dark = Mat("#2E3140");
            switch (tier)
            {
                case 0:
                    return BuildSnorkel(t);
                case 1:
                {
                    // scrap tank: small, dented, duct tape, rust spots
                    float r = 0.085f, len = 0.46f;
                    TankBody(t, 0f, 0f, r, len, Mat("#9AA0A8"));
                    Blob(t, Around(r * 0.86f, -35f, 0.22f), new Vector3(0.03f, 0.044f, 0.014f), Mat("#7E848D"), new Vector3(0f, 35f, 0f));
                    RingS(t, new Vector3(0f, 0.3f, 0f), r + 0.002f, 0.01f, Mat("#C1C7CE"), new Vector3(0f, 0f, 6f), new Vector3(1f, 2.2f, 1f));
                    Box(t, Around(r + 0.002f, 30f, 0.15f), new Vector3(0.05f, 0.034f, 0.006f), 0.002f, Mat("#C1C7CE"), new Vector3(0f, -30f, 18f));
                    Blob(t, Around(r * 0.98f, 8f, 0.09f), new Vector3(0.018f, 0.012f, 0.006f), Mat("#A0583A"), new Vector3(0f, -8f, 0f));
                    Blob(t, Around(r * 0.98f, -70f, 0.38f), new Vector3(0.012f, 0.017f, 0.006f), Mat("#A0583A"), new Vector3(0f, 70f, 0f));
                    Valve(t, 0f, len + r * 0.25f, 0f, valve, dark);
                    return new View(30f, -16f, -6f);
                }
                case 2:
                {
                    // 12 L steel: blue, black rubber boot, white label, turquoise band, chrome knob
                    float r = 0.098f, len = 0.52f;
                    TankBody(t, 0f, 0f, r, len, Mat("#4F7FAE"));
                    Cone(t, new Vector3(0f, 0.05f, 0f), r + 0.006f, r + 0.004f, 0.1f, dark);
                    Band(t, new Vector3(0f, 0.23f, 0f), r + 0.002f, 0.07f, Mat("#EDE6DA"));
                    Ring(t, new Vector3(0f, 0.38f, 0f), r + 0.003f, 0.012f, Mat("#3FB8C4"));
                    Valve(t, 0f, len + r * 0.25f, 0f, valve, chrome);
                    return new View(32f, -16f);
                }
                case 3:
                {
                    // twin grey tanks with dark bands and a chrome manifold
                    float r = 0.08f, len = 0.5f, x = 0.086f;
                    Material body = Mat("#8E949C");
                    for (int s = -1; s <= 1; s += 2)
                    {
                        TankBody(t, s * x, 0f, r, len, body);
                        Ring(t, new Vector3(s * x, 0.11f, 0f), r + 0.003f, 0.011f, dark);
                        Ring(t, new Vector3(s * x, 0.34f, 0f), r + 0.003f, 0.011f, dark);
                        Valve(t, s * x, len + r * 0.25f, 0f, valve, chrome);
                    }
                    Seg(t, new Vector3(-x, len + r * 0.25f + 0.045f, 0f), new Vector3(x, len + r * 0.25f + 0.045f, 0f), 0.016f, 0.016f, chrome);
                    return new View(26f, -14f);
                }
                case 4:
                {
                    // nitrox: light body, yellow + green shoulder band, green diamond sticker, yellow knob
                    float r = 0.102f, len = 0.55f;
                    Material yellow = Mat("#F2C94C"), green = Mat("#6E9445");
                    TankBody(t, 0f, 0f, r, len, Mat("#DDE2E6"));
                    Band(t, new Vector3(0f, 0.43f, 0f), r + 0.004f, 0.08f, yellow);
                    Band(t, new Vector3(0f, 0.375f, 0f), r + 0.004f, 0.024f, green);
                    Box(t, Around(r + 0.002f, -12f, 0.2f), new Vector3(0.05f, 0.05f, 0.005f), 0.004f, green, new Vector3(0f, 12f, 45f));
                    Valve(t, 0f, len + r * 0.25f, 0f, valve, yellow);
                    return new View(30f, -16f);
                }
                default:
                {
                    // trimix rig: twin black tanks, orange bands + knobs, backplate, pressure gauge on a hose
                    float r = 0.09f, len = 0.58f, x = 0.096f;
                    Material body = Mat("#2E3140"), orange = Mat("#EE7F2D");
                    Box(t, new Vector3(0f, 0.33f, r + 0.022f), new Vector3(0.36f, 0.52f, 0.022f), 0.008f, Mat("#4A4F66"));
                    for (int s = -1; s <= 1; s += 2)
                    {
                        TankBody(t, s * x, 0f, r, len, body);
                        Ring(t, new Vector3(s * x, 0.11f, 0f), r + 0.003f, 0.013f, orange);
                        Ring(t, new Vector3(s * x, 0.43f, 0f), r + 0.003f, 0.013f, orange);
                        Valve(t, s * x, len + r * 0.25f, 0f, valve, orange);
                    }
                    Seg(t, new Vector3(-x, len + r * 0.25f + 0.045f, 0f), new Vector3(x, len + r * 0.25f + 0.045f, 0f), 0.017f, 0.017f, chrome);
                    Vector3 gauge = new Vector3(x + 0.075f, 0.19f, -r - 0.03f);
                    Seg(t, new Vector3(x + 0.04f, len + 0.05f, 0f), gauge + new Vector3(0f, 0.035f, 0f), 0.007f, 0.007f, dark);
                    Disc(t, gauge, 0.034f, 0.02f, dark, new Vector3(90f, 0f, 0f));
                    Disc(t, gauge + new Vector3(0f, 0f, -0.011f), 0.026f, 0.004f, Mat("#EDE6DA"), new Vector3(90f, 0f, 0f));
                    Box(t, gauge + new Vector3(0.006f, 0.006f, -0.014f), new Vector3(0.004f, 0.02f, 0.002f), 0.001f, Mat("#E04848"), new Vector3(0f, 0f, -40f));
                    return new View(26f, -14f);
                }
            }
        }

        /// The starter "Schnorchel-Set": mask + J snorkel.
        static View BuildSnorkel(Transform t)
        {
            Material frame = Mat("#3FB8C4"), skirt = Mat("#2E3140"), glass = Mat("#BFE3EE"), yellow = Mat("#F2C94C");
            Box(t, new Vector3(0f, 0.1f, 0.006f), new Vector3(0.18f, 0.086f, 0.05f), 0.022f, skirt);
            Box(t, new Vector3(0f, 0.1f, -0.016f), new Vector3(0.172f, 0.078f, 0.03f), 0.02f, frame);
            Blob(t, new Vector3(-0.041f, 0.103f, -0.032f), new Vector3(0.034f, 0.03f, 0.006f), glass);
            Blob(t, new Vector3(0.041f, 0.103f, -0.032f), new Vector3(0.034f, 0.03f, 0.006f), glass);
            Box(t, new Vector3(0f, 0.08f, -0.03f), new Vector3(0.024f, 0.03f, 0.012f), 0.006f, frame);
            RingS(t, new Vector3(0f, 0.1f, 0.06f), 0.085f, 0.007f, skirt, Vector3.zero, new Vector3(1f, 1f, 0.75f));
            Seg(t, new Vector3(0.12f, 0.02f, 0.02f), new Vector3(0.12f, 0.3f, 0.03f), 0.012f, 0.012f, yellow);
            Seg(t, new Vector3(0.12f, 0.02f, 0.02f), new Vector3(0.085f, -0.015f, -0.01f), 0.012f, 0.012f, yellow);
            Ball(t, new Vector3(0.12f, 0.02f, 0.02f), 0.012f, yellow);
            Blob(t, new Vector3(0.078f, -0.02f, -0.016f), new Vector3(0.016f, 0.012f, 0.02f), skirt);
            Blob(t, new Vector3(0.12f, 0.31f, 0.03f), new Vector3(0.016f, 0.02f, 0.016f), Mat("#EE7F2D"));
            Box(t, new Vector3(0.1f, 0.12f, 0.024f), new Vector3(0.034f, 0.012f, 0.012f), 0.004f, skirt);
            return new View(24f, -10f);
        }

        // ------------------------------------------------------------------ fins + scooter
        static readonly Vector2[] FinPool = { new Vector2(-0.075f, -0.05f), new Vector2(0.075f, -0.05f), new Vector2(0.105f, 0.15f), new Vector2(0.085f, 0.23f), new Vector2(0.035f, 0.27f), new Vector2(-0.035f, 0.27f), new Vector2(-0.085f, 0.23f), new Vector2(-0.105f, 0.15f) };
        static readonly Vector2[] FinSplitL = { new Vector2(-0.075f, -0.05f), new Vector2(-0.006f, -0.05f), new Vector2(-0.012f, 0.39f), new Vector2(-0.07f, 0.42f), new Vector2(-0.13f, 0.38f) };
        static readonly Vector2[] FinSplitR = { new Vector2(0.006f, -0.05f), new Vector2(0.075f, -0.05f), new Vector2(0.13f, 0.38f), new Vector2(0.07f, 0.42f), new Vector2(0.012f, 0.39f) };
        static readonly Vector2[] FinCarbon = { new Vector2(-0.07f, -0.05f), new Vector2(0.07f, -0.05f), new Vector2(0.095f, 0.4f), new Vector2(0.06f, 0.5f), new Vector2(0f, 0.53f), new Vector2(-0.06f, 0.5f), new Vector2(-0.095f, 0.4f) };

        static View BuildFins(Transform t, int tier)
        {
            Material blade = Mat(tier == 0 ? "#F2C94C" : tier == 1 ? "#3FB8C4" : "#2B2533");
            Material pocket = Mat(tier == 0 ? "#F7DC7A" : "#2E3140");
            Material strap = Mat(tier == 0 ? "#EE7F2D" : "#4A4F66");
            for (int s = -1; s <= 1; s += 2)
            {
                var f = Node(t, new Vector3(s * 0.09f, 0f, s * 0.025f), new Vector3(0f, 0f, -s * 7f));
                Blob(f, new Vector3(0f, -0.02f, 0f), new Vector3(0.062f, 0.075f, 0.042f), pocket);
                RingS(f, new Vector3(0f, -0.075f, 0.03f), 0.04f, 0.008f, strap, new Vector3(60f, 0f, 0f), Vector3.one);
                Vector3 at = new Vector3(0f, 0.03f, -0.004f);
                switch (tier)
                {
                    case 0:
                        Plate(f, "fin_pool", FinPool, 0.026f, blade, at);
                        Box(f, new Vector3(0f, 0.2f, -0.018f), new Vector3(0.09f, 0.012f, 0.006f), 0.003f, strap);   // squeaky toy stripe
                        break;
                    case 1:
                        Plate(f, "fin_split_l", FinSplitL, 0.02f, blade, at);
                        Plate(f, "fin_split_r", FinSplitR, 0.02f, blade, at);
                        break;
                    default:
                    {
                        Plate(f, "fin_carbon", FinCarbon, 0.016f, blade, at);
                        Material rail = Mat("#4A4F66");
                        for (int k = -1; k <= 1; k += 2)
                            Seg(f, new Vector3(k * 0.072f, 0.01f, -0.004f), new Vector3(k * 0.094f, 0.43f, -0.004f), 0.011f, 0.009f, rail);
                        Seg(f, new Vector3(0f, 0.08f, -0.012f), new Vector3(0f, 0.5f, -0.012f), 0.0045f, 0.0045f, Mat("#8E6CD9"));
                        break;
                    }
                }
            }
            return new View(16f, -24f, 0f, 1.1f);
        }

        static View BuildScooter(Transform t)
        {
            var n = Node(t, Vector3.zero, new Vector3(0f, 0f, -90f));   // built along +Y, nose → +X
            Material body = Mat("#F2C94C"), dark = Mat("#2E3140"), stripe = Mat("#EE7F2D");
            Caps(n, Vector3.zero, 0.085f, 0.5f, body);
            Blob(n, new Vector3(0f, 0.47f, 0f), new Vector3(0.066f, 0.05f, 0.066f), dark);
            Band(n, new Vector3(0f, 0.3f, 0f), 0.087f, 0.035f, stripe);
            RingS(n, new Vector3(0f, -0.035f, 0f), 0.078f, 0.012f, dark, Vector3.zero, new Vector3(1f, 2.2f, 1f));
            for (int i = 0; i < 3; i++)
            {
                float a = i * 120f;
                Vector3 o = Quaternion.Euler(0f, a, 0f) * new Vector3(0.035f, 0f, 0f);
                Blob(n, new Vector3(o.x, -0.035f, o.z), new Vector3(0.03f, 0.004f, 0.011f), dark, new Vector3(0f, a, 18f));
            }
            for (int s = -1; s <= 1; s += 2)
            {
                Seg(n, new Vector3(0f, 0.36f, s * 0.07f), new Vector3(0f, 0.36f, s * 0.15f), 0.014f, 0.014f, dark);
                Ball(n, new Vector3(0f, 0.36f, s * 0.15f), 0.016f, dark);
            }
            Box(n, new Vector3(-0.08f, 0.2f, 0f), new Vector3(0.008f, 0.07f, 0.05f), 0.004f, Glow("#3FB8C4", "#3FB8C4", 0.8f));
            return new View(28f, -22f);
        }

        // ------------------------------------------------------------------ lamps
        static View BuildFlashlight(Transform t)
        {
            var n = Node(t, Vector3.zero, new Vector3(0f, 0f, -90f));   // lens → +X
            Material yellow = Mat("#F2C94C"), head = Mat("#2E3140"), tape = Mat("#C1C7CE");
            Caps(n, Vector3.zero, 0.021f, 0.16f, yellow);
            Cone(n, new Vector3(0f, 0.172f, 0f), 0.022f, 0.032f, 0.045f, head);
            Blob(n, new Vector3(0f, 0.195f, 0f), new Vector3(0.028f, 0.006f, 0.028f), Glow("#FFF1C9", "#FFD9A0", 0.8f));
            Band(n, new Vector3(0f, 0.065f, 0f), 0.0228f, 0.016f, tape);
            Band(n, new Vector3(0f, 0.105f, 0f), 0.0226f, 0.011f, tape, new Vector3(0f, 0f, 14f));
            for (int k = 0; k < 3; k++) Ring(n, new Vector3(0f, 0.022f + k * 0.008f, 0f), 0.0215f, 0.0018f, head);
            Box(n, new Vector3(-0.02f, 0.13f, 0f), new Vector3(0.008f, 0.02f, 0.012f), 0.003f, head);   // switch on top (node −X = up)
            return new View(32f, -20f, 32f, 1.08f);
        }

        static View BuildVideoLight(Transform t)
        {
            var n = Node(t, Vector3.zero, new Vector3(0f, 0f, -90f));
            Material dark = Mat("#2E3140"), fins = Mat("#4A4F66"), orange = Mat("#EE7F2D");
            Caps(n, Vector3.zero, 0.026f, 0.13f, dark);
            for (int k = 0; k < 3; k++) Ring(n, new Vector3(0f, 0.1f + k * 0.012f, 0f), 0.027f, 0.004f, fins);
            Box(n, new Vector3(0f, 0.165f, 0f), new Vector3(0.11f, 0.05f, 0.11f), 0.014f, dark);
            Box(n, new Vector3(0f, 0.19f, 0f), new Vector3(0.094f, 0.004f, 0.094f), 0.003f, Mat("#1E1B2B"));
            Material led = Glow("#EAF4FF", "#DDEBFF", 1.1f);
            for (int ix = -1; ix <= 1; ix++)
                for (int iz = -1; iz <= 1; iz++)
                    Box(n, new Vector3(ix * 0.029f, 0.192f, iz * 0.029f), new Vector3(0.022f, 0.005f, 0.022f), 0.003f, led);
            Box(n, new Vector3(-0.026f, 0.07f, 0f), new Vector3(0.012f, 0.026f, 0.016f), 0.004f, orange);
            Disc(n, new Vector3(0f, 0.004f, 0f), 0.027f, 0.016f, orange);
            return new View(40f, -18f, 26f, 1.1f);
        }

        static View BuildStrobes(Transform t)
        {
            Material dark = Mat("#2E3140"), grey = Mat("#8E949C"), orange = Mat("#EE7F2D"), rubber = Mat("#1E1B2B");
            Material diff = Glow("#EAF4FF", "#DDEBFF", 0.9f);
            Box(t, Vector3.zero, new Vector3(0.36f, 0.028f, 0.07f), 0.01f, dark);
            for (int s = -1; s <= 1; s += 2)
            {
                Seg(t, new Vector3(s * 0.16f, -0.01f, 0f), new Vector3(s * 0.16f, -0.13f, 0f), 0.017f, 0.017f, rubber);
                Ball(t, new Vector3(s * 0.16f, -0.13f, 0f), 0.017f, rubber);
                Vector3 a0 = new Vector3(s * 0.12f, 0.02f, 0f), j1 = new Vector3(s * 0.2f, 0.15f, -0.02f), j2 = new Vector3(s * 0.17f, 0.28f, -0.05f);
                Ball(t, a0, 0.018f, orange);
                Seg(t, a0, j1, 0.011f, 0.011f, grey);
                Ball(t, j1, 0.018f, orange);
                Seg(t, j1, j2, 0.011f, 0.011f, grey);
                Ball(t, j2, 0.018f, orange);
                var h = Node(t, j2 + new Vector3(0f, 0.04f, -0.02f), new Vector3(-18f, s * 14f, 0f));
                Disc(h, Vector3.zero, 0.048f, 0.06f, dark, new Vector3(90f, 0f, 0f));
                Blob(h, new Vector3(0f, 0f, -0.032f), new Vector3(0.042f, 0.042f, 0.012f), diff);
                Ring(h, new Vector3(0f, 0f, -0.028f), 0.047f, 0.006f, orange, new Vector3(90f, 0f, 0f));
            }
            return new View(18f, -16f);
        }

        // ------------------------------------------------------------------ radio, battery
        static View BuildRadio(Transform t)
        {
            Material orange = Mat("#EE7F2D"), dark = Mat("#2E3140"), plate = Mat("#3A3E52"), black = Mat("#1E1B2B");
            Box(t, new Vector3(0f, 0.06f, 0f), new Vector3(0.075f, 0.12f, 0.038f), 0.014f, orange);
            Box(t, new Vector3(0f, 0.05f, -0.019f), new Vector3(0.058f, 0.07f, 0.004f), 0.006f, plate);
            for (int k = 0; k < 4; k++) Box(t, new Vector3(0f, 0.025f + k * 0.014f, -0.0215f), new Vector3(0.04f, 0.005f, 0.003f), 0.0015f, black);
            Box(t, new Vector3(0f, 0.098f, -0.0195f), new Vector3(0.044f, 0.02f, 0.003f), 0.003f, Glow("#9FE4EC", "#9FE4EC", 0.7f));
            Seg(t, new Vector3(0.024f, 0.115f, 0f), new Vector3(0.024f, 0.21f, 0f), 0.005f, 0.0035f, dark);
            Ball(t, new Vector3(0.024f, 0.212f, 0f), 0.008f, orange);
            Disc(t, new Vector3(-0.018f, 0.124f, 0f), 0.008f, 0.012f, dark);
            Box(t, new Vector3(0f, 0.07f, 0.022f), new Vector3(0.022f, 0.07f, 0.008f), 0.004f, dark);
            // coiled cable down to the earpiece
            Vector3 c0 = new Vector3(-0.045f, 0.03f, 0f);
            Vector3 dir = new Vector3(-0.45f, -1f, -0.2f).normalized;
            Quaternion along = Quaternion.FromToRotation(Vector3.up, dir);
            for (int k = 0; k < 7; k++)
                Part(t, DiverMeshes.Torus(0.011f, 0.0026f, 16, 6), dark, c0 + dir * (k * 0.016f), along * Quaternion.Euler(12f, 0f, 0f), Vector3.one);
            Vector3 ear = c0 + dir * 0.13f;
            Blob(t, ear, new Vector3(0.024f, 0.028f, 0.018f), dark);
            Ring(t, ear + new Vector3(0f, 0f, -0.012f), 0.016f, 0.004f, Mat("#3FB8C4"), new Vector3(90f, 0f, 0f));
            return new View(30f, -14f);
        }

        static View BuildBattery(Transform t)
        {
            Material body = Mat("#2E3140"), red = Mat("#C94F3D"), label = Mat("#E8D9B0"), steel = Mat("#B8BEC6");
            Box(t, new Vector3(0f, 0.07f, 0f), new Vector3(0.09f, 0.14f, 0.06f), 0.012f, body);
            Box(t, new Vector3(0f, 0.124f, 0f), new Vector3(0.092f, 0.034f, 0.062f), 0.01f, red);
            Box(t, new Vector3(0f, 0.062f, -0.0305f), new Vector3(0.07f, 0.05f, 0.002f), 0.004f, label);
            Box(t, new Vector3(0f, 0.062f, -0.032f), new Vector3(0.05f, 0.008f, 0.002f), 0.002f, Mat("#EE7F2D"), new Vector3(0f, 0f, 20f));
            for (int k = 0; k < 4; k++) Ring(t, new Vector3(-0.022f, 0.145f + k * 0.006f, 0f), 0.008f, 0.0018f, steel);
            Disc(t, new Vector3(0.022f, 0.152f, 0f), 0.007f, 0.02f, steel);
            Part(t, FilmMesh.Cylinder(0.012f, 0.006f, 6), steel, new Vector3(0.022f, 0.144f, 0f));
            return new View(30f, -22f);
        }

        // ------------------------------------------------------------------ cameras (FilmRigBuilder models, mitten hidden)
        static View BuildCamera(Transform t, int tier, out FilmCameraRig rig)
        {
            rig = null;
            try { rig = FilmRigBuilder.Build(Mathf.Clamp(tier, 0, 4), FilmRigBuilder.DefaultCuff, false); }
            catch (Exception e)
            {
                Debug.LogWarning("Sealebrities: camera model for the shop icon failed (" + e.Message + "), using a simple one");
                rig = null;
            }
            if (rig == null || rig.root == null) return BuildSimpleCamera(t, tier);
            var r = rig.root.transform;
            r.SetParent(t, false);
            r.localPosition = Vector3.zero;
            r.localRotation = Quaternion.identity;
            r.localScale = Vector3.one;
            HideMitten(r, rig);
            // the phone shows its screen side, the real cameras their lens (+Z) at a 3/4 angle
            return tier == 0 ? new View(22f, -12f, 0f, 1.08f) : new View(146f, -14f, 0f, 1.1f);
        }

        static void HideMitten(Transform r, FilmCameraRig rig)
        {
            var all = r.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                var x = all[i];
                if (x != null && x != r && (x.name == "Hand" || x.name == "HandStatic")) x.gameObject.SetActive(false);
            }
            var glove = FilmMaterials.Flat(FilmRigBuilder.Glove);
            var pad = FilmMaterials.Flat(FilmRigBuilder.GlovePad);
            var rs = r.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < rs.Length; i++)
            {
                var mr = rs[i];
                if (mr == null) continue;
                var m = mr.sharedMaterial;
                if (m != null && (m == glove || m == pad)) mr.enabled = false;
            }
            if (rig.cuff != null) rig.cuff.enabled = false;
        }

        /// Fallback if the filming builder fails: a simple body + lens per tier colour.
        static View BuildSimpleCamera(Transform t, int tier)
        {
            string[] bodies = { "#2E3140", "#1E1B2B", "#F2C94C", "#8E949C", "#3A3E52" };
            Material body = Mat(bodies[Mathf.Clamp(tier, 0, 4)]), dark = Mat("#1E1B2B"), chrome = Mat("#B8BEC6");
            float w = 0.06f + tier * 0.02f;
            Box(t, Vector3.zero, new Vector3(w, 0.045f + tier * 0.01f, 0.04f + tier * 0.012f), 0.008f, body);
            Disc(t, new Vector3(w * 0.2f, 0f, 0.03f + tier * 0.008f), 0.016f + tier * 0.004f, 0.03f + tier * 0.01f, dark, new Vector3(90f, 0f, 0f));
            Ring(t, new Vector3(w * 0.2f, 0f, 0.046f + tier * 0.012f), 0.017f + tier * 0.004f, 0.003f, chrome, new Vector3(90f, 0f, 0f));
            Ball(t, new Vector3(-w * 0.35f, 0.03f + tier * 0.005f, 0f), 0.006f, Mat("#E04848"));
            return new View(146f, -14f);
        }

        static View BuildZoomLens(Transform t)
        {
            var n = Node(t, Vector3.zero, new Vector3(0f, 0f, -90f));   // front glass → +X
            Material black = Mat("#2A2633"), rib = Mat("#3A3E52"), orange = Mat("#EE7F2D"), chrome = Mat("#B8BEC6");
            Disc(n, new Vector3(0f, 0.006f, 0f), 0.03f, 0.012f, chrome);
            Disc(n, new Vector3(0f, 0.036f, 0f), 0.034f, 0.05f, black);
            for (int k = 0; k < 6; k++) Ring(n, new Vector3(0f, 0.066f + k * 0.007f, 0f), 0.0345f, 0.0028f, rib);
            Disc(n, new Vector3(0f, 0.114f, 0f), 0.036f, 0.05f, black);
            Ring(n, new Vector3(0f, 0.132f, 0f), 0.0365f, 0.0035f, orange);
            Cone(n, new Vector3(0f, 0.153f, 0f), 0.037f, 0.044f, 0.03f, black);
            Ring(n, new Vector3(0f, 0.168f, 0f), 0.042f, 0.004f, black);
            Blob(n, new Vector3(0f, 0.167f, 0f), new Vector3(0.036f, 0.007f, 0.036f), Mat("#1E3A5C"));
            Blob(n, new Vector3(0.012f, 0.172f, -0.012f), new Vector3(0.009f, 0.002f, 0.005f), Mat("#EAF4F6"));
            return new View(42f, -12f, 22f, 1.1f);
        }

        static View BuildNightVision(Transform t)
        {
            Material olive = Mat("#556B3A"), dark = Mat("#2E3140");
            Box(t, new Vector3(0f, 0.04f, 0f), new Vector3(0.09f, 0.065f, 0.075f), 0.012f, olive);
            Disc(t, new Vector3(0f, 0.04f, -0.05f), 0.028f, 0.03f, dark, new Vector3(90f, 0f, 0f));
            Blob(t, new Vector3(0f, 0.04f, -0.066f), new Vector3(0.023f, 0.023f, 0.005f), Glow("#6EE07A", "#7CFF6E", 1.4f));
            Material ir = Glow("#E04848", "#FF5050", 1f);
            for (int k = 0; k < 6; k++)
            {
                float a = k * 60f * Mathf.Deg2Rad;
                Ball(t, new Vector3(Mathf.Cos(a) * 0.036f, 0.04f + Mathf.Sin(a) * 0.036f, -0.04f), 0.0045f, ir);
            }
            Box(t, new Vector3(0f, 0f, 0f), new Vector3(0.05f, 0.014f, 0.05f), 0.004f, dark);
            Disc(t, new Vector3(0.047f, 0.05f, 0.01f), 0.011f, 0.01f, Mat("#EE7F2D"), new Vector3(0f, 0f, 90f));
            Box(t, new Vector3(0f, 0.077f, 0.01f), new Vector3(0.03f, 0.01f, 0.03f), 0.004f, dark);
            return new View(30f, -15f);
        }

        // ------------------------------------------------------------------ boat
        static View BuildOutboard(Transform t)
        {
            Material white = Mat("#EDE6DA"), orange = Mat("#EE7F2D"), dark = Mat("#3A3E52"), grey = Mat("#8E949C"), steel = Mat("#4A4F66");
            Box(t, new Vector3(0f, 0.6f, 0f), new Vector3(0.32f, 0.22f, 0.2f), 0.06f, white);
            Box(t, new Vector3(0.01f, 0.69f, 0f), new Vector3(0.28f, 0.05f, 0.18f), 0.024f, white);
            Box(t, new Vector3(0f, 0.53f, 0f), new Vector3(0.325f, 0.03f, 0.205f), 0.012f, orange);
            Box(t, new Vector3(0f, 0.62f, -0.101f), new Vector3(0.12f, 0.04f, 0.004f), 0.008f, orange);
            Box(t, new Vector3(0f, 0.44f, 0f), new Vector3(0.26f, 0.14f, 0.17f), 0.04f, dark);
            Box(t, new Vector3(0.03f, 0.17f, 0f), new Vector3(0.1f, 0.42f, 0.07f), 0.025f, grey);
            Box(t, new Vector3(0.04f, -0.04f, 0f), new Vector3(0.24f, 0.012f, 0.16f), 0.005f, grey);
            Caps(t, new Vector3(-0.08f, -0.1f, 0f), 0.035f, 0.25f, grey, new Vector3(0f, 0f, -90f));
            Box(t, new Vector3(0.03f, -0.17f, 0f), new Vector3(0.05f, 0.08f, 0.012f), 0.004f, grey);
            Vector3 hub = new Vector3(0.18f, -0.1f, 0f);
            Blob(t, hub, new Vector3(0.022f, 0.022f, 0.022f), steel);
            for (int i = 0; i < 3; i++)
            {
                float a = i * 120f;
                Vector3 o = Quaternion.Euler(a, 0f, 0f) * new Vector3(0f, 0.042f, 0f);
                Blob(t, hub + o, new Vector3(0.008f, 0.036f, 0.018f), steel, new Vector3(a, 0f, 0f));
            }
            Seg(t, new Vector3(-0.15f, 0.5f, 0f), new Vector3(-0.4f, 0.47f, 0f), 0.016f, 0.014f, dark);
            Caps(t, new Vector3(-0.38f, 0.468f, 0f), 0.02f, 0.09f, orange, new Vector3(0f, 0f, 90f));
            Box(t, new Vector3(-0.17f, 0.42f, 0f), new Vector3(0.05f, 0.16f, 0.12f), 0.01f, dark);
            for (int s = -1; s <= 1; s += 2) Seg(t, new Vector3(-0.2f, 0.38f, s * 0.04f), new Vector3(-0.26f, 0.38f, s * 0.04f), 0.008f, 0.008f, orange);
            return new View(28f, -12f);
        }

        static View BuildRib(Transform t)
        {
            Material tube = Mat("#EE7F2D"), strake = Mat("#3A3E52"), hull = Mat("#4A4F66"), deck = Mat("#C8CED6"),
                     white = Mat("#EDE6DA"), dark = Mat("#2E3140"), seat = Mat("#3FB8C4"), grey = Mat("#8E949C");
            const float r = 0.1f;
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 a = new Vector3(0f, 0.08f, s * 0.3f), b = new Vector3(0.75f, 0.08f, s * 0.27f), c = new Vector3(1.05f, 0.12f, s * 0.05f);
                Seg(t, a, b, r, r, tube);
                Seg(t, b, c, r, r * 0.85f, tube);
                Ball(t, a, r, tube);
                Ball(t, b, r, tube);
                Ball(t, c, r * 0.85f, tube);
                Seg(t, a + new Vector3(0f, -0.01f, s * r * 0.96f), b + new Vector3(0f, -0.01f, s * r * 0.96f), 0.013f, 0.013f, strake);
            }
            Box(t, new Vector3(0.45f, 0f, 0f), new Vector3(0.95f, 0.08f, 0.5f), 0.03f, hull);
            Blob(t, new Vector3(0.93f, 0.02f, 0f), new Vector3(0.15f, 0.06f, 0.16f), hull);
            Box(t, new Vector3(0.45f, 0.045f, 0f), new Vector3(0.9f, 0.02f, 0.46f), 0.008f, deck);
            Box(t, new Vector3(0.52f, 0.17f, 0f), new Vector3(0.17f, 0.22f, 0.18f), 0.02f, white);
            Box(t, new Vector3(0.6f, 0.31f, 0f), new Vector3(0.025f, 0.09f, 0.2f), 0.008f, Mat("#BFE3EE"), new Vector3(0f, 0f, -25f));
            RingS(t, new Vector3(0.43f, 0.25f, 0f), 0.04f, 0.006f, dark, new Vector3(0f, 0f, 65f), Vector3.one);
            Box(t, new Vector3(0.26f, 0.11f, 0f), new Vector3(0.16f, 0.08f, 0.26f), 0.02f, seat);
            Box(t, new Vector3(-0.08f, 0.22f, 0f), new Vector3(0.14f, 0.14f, 0.11f), 0.035f, white);
            Box(t, new Vector3(-0.1f, 0.06f, 0f), new Vector3(0.045f, 0.2f, 0.045f), 0.01f, grey);
            return new View(36f, -24f);
        }

        static View BuildOldBoat(Transform t)
        {
            Material white = Mat("#EDE6DA"), red = Mat("#A8484A"), rust = Mat("#A0583A"), wood = Mat("#C9B08E"), dark = Mat("#2E3140"), smoke = Mat("#B8BEC6");
            Box(t, new Vector3(0.5f, 0.12f, 0f), new Vector3(1.0f, 0.24f, 0.42f), 0.08f, white);
            Blob(t, new Vector3(0.98f, 0.14f, 0f), new Vector3(0.14f, 0.12f, 0.2f), white);
            Box(t, new Vector3(0.5f, 0.02f, 0f), new Vector3(0.98f, 0.06f, 0.4f), 0.03f, red);
            Box(t, new Vector3(0.5f, 0.2f, 0f), new Vector3(1.005f, 0.035f, 0.425f), 0.012f, rust);
            Box(t, new Vector3(0.35f, 0.36f, 0f), new Vector3(0.32f, 0.26f, 0.3f), 0.03f, wood);
            Box(t, new Vector3(0.35f, 0.5f, 0f), new Vector3(0.36f, 0.03f, 0.34f), 0.01f, red);
            Box(t, new Vector3(0.35f, 0.39f, -0.152f), new Vector3(0.2f, 0.08f, 0.006f), 0.006f, dark);
            Seg(t, new Vector3(0.24f, 0.5f, 0.06f), new Vector3(0.24f, 0.66f, 0.06f), 0.025f, 0.022f, dark);
            Ball(t, new Vector3(0.22f, 0.72f, 0.06f), 0.04f, smoke);
            Ball(t, new Vector3(0.17f, 0.8f, 0.06f), 0.05f, smoke);
            return new View(34f, -20f);
        }

        static View BuildCompressor(Transform t)
        {
            Material red = Mat("#C94F3D"), dark = Mat("#3A3E52"), steel = Mat("#8E949C"), yellow = Mat("#F2C94C"), white = Mat("#EDE6DA"), rubber = Mat("#2E3140");
            Box(t, new Vector3(0f, 0.02f, 0f), new Vector3(0.5f, 0.04f, 0.36f), 0.01f, dark);
            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2) Seg(t, new Vector3(sx * 0.22f, 0.04f, sz * 0.15f), new Vector3(sx * 0.22f, 0.36f, sz * 0.15f), 0.013f, 0.013f, steel);
                Seg(t, new Vector3(sx * 0.22f, 0.36f, -0.15f), new Vector3(sx * 0.22f, 0.36f, 0.15f), 0.013f, 0.013f, steel);
            }
            Caps(t, new Vector3(-0.22f, 0.16f, 0f), 0.11f, 0.44f, red, new Vector3(0f, 0f, -90f));
            Box(t, new Vector3(0.02f, 0.3f, 0f), new Vector3(0.2f, 0.12f, 0.16f), 0.02f, dark);
            Box(t, new Vector3(0.02f, 0.365f, 0f), new Vector3(0.21f, 0.016f, 0.17f), 0.006f, yellow);
            Disc(t, new Vector3(0.02f, 0.3f, -0.1f), 0.07f, 0.03f, steel, new Vector3(90f, 0f, 0f));
            Box(t, new Vector3(0.02f, 0.355f, -0.118f), new Vector3(0.024f, 0.03f, 0.012f), 0.004f, red);
            Vector3 g = new Vector3(0.17f, 0.2f, -0.115f);
            Disc(t, g, 0.034f, 0.014f, white, new Vector3(90f, 0f, 0f));
            Ring(t, g + new Vector3(0f, 0f, -0.006f), 0.034f, 0.005f, dark, new Vector3(90f, 0f, 0f));
            Box(t, g + new Vector3(0.006f, 0.008f, -0.009f), new Vector3(0.004f, 0.024f, 0.002f), 0.001f, red, new Vector3(0f, 0f, -35f));
            for (int k = 0; k < 3; k++) Ring(t, new Vector3(0.27f + k * 0.016f, 0.14f, 0.04f), 0.06f, 0.008f, rubber, new Vector3(0f, 0f, 90f));
            return new View(32f, -22f);
        }

        static View BuildWinch(Transform t)
        {
            Material yellow = Mat("#F2C94C"), dark = Mat("#3A3E52"), rope = Mat("#C9B08E"), orange = Mat("#EE7F2D"), steel = Mat("#8E949C");
            Box(t, new Vector3(0f, 0.03f, 0f), new Vector3(0.24f, 0.06f, 0.24f), 0.01f, dark);
            Seg(t, new Vector3(0f, 0.06f, 0f), new Vector3(0f, 1.2f, 0f), 0.05f, 0.045f, yellow);
            Ball(t, new Vector3(0f, 1.18f, 0f), 0.05f, yellow);
            Seg(t, new Vector3(0f, 1.16f, 0f), new Vector3(-0.62f, 1.4f, 0f), 0.04f, 0.034f, yellow);
            Disc(t, new Vector3(-0.62f, 1.36f, 0f), 0.06f, 0.05f, dark, new Vector3(90f, 0f, 0f));
            Seg(t, new Vector3(-0.62f, 1.32f, 0f), new Vector3(-0.62f, 0.74f, 0f), 0.007f, 0.007f, rope);
            Seg(t, new Vector3(-0.62f, 0.75f, 0f), new Vector3(-0.62f, 0.69f, 0f), 0.012f, 0.012f, steel);
            Ring(t, new Vector3(-0.62f, 0.65f, 0f), 0.035f, 0.01f, steel, new Vector3(90f, 0f, 0f));
            Disc(t, new Vector3(0.11f, 0.62f, 0f), 0.075f, 0.14f, dark, new Vector3(0f, 0f, 90f));
            Disc(t, new Vector3(0.11f, 0.62f, 0f), 0.088f, 0.1f, rope, new Vector3(0f, 0f, 90f));
            Seg(t, new Vector3(0.19f, 0.62f, 0f), new Vector3(0.19f, 0.52f, -0.09f), 0.012f, 0.012f, orange);
            Caps(t, new Vector3(0.19f, 0.52f, -0.09f), 0.017f, 0.07f, orange, new Vector3(0f, 0f, -90f));
            return new View(30f, -10f, 0f, 1.1f);
        }

        // ------------------------------------------------------------------ rods (built along +X: butt left, tip right, reel hanging below)
        static View BuildRod(Transform t, int tier)
        {
            var n = Node(t, Vector3.zero, new Vector3(0f, 0f, -90f));   // node +Y = rod axis (→ +X), node +X = down
            Material chrome = Mat("#B8BEC6"), dark = Mat("#2E3140");
            switch (tier)
            {
                case 0:
                {
                    // Gus' bamboo rod: light cane with darker nodes, twine grip, a wooden spool reel
                    Material cane = Mat("#D8C27A"), node = Mat("#A88A50"), twine = Mat("#C9B08E"), wood = Mat("#8E6C50");
                    TCaps(n, Vector3.zero, 0.02f, 0.008f, 1.0f, cane);
                    for (int k = 1; k <= 4; k++) Ring(n, new Vector3(0f, k * 0.2f, 0f), Mathf.Lerp(0.02f, 0.008f, k * 0.2f) + 0.001f, 0.0035f, node);
                    Cone(n, new Vector3(0f, 0.12f, 0f), 0.024f, 0.023f, 0.2f, twine);
                    Seg(n, new Vector3(0f, 0.27f, 0f), new Vector3(0.045f, 0.27f, 0f), 0.005f, 0.005f, wood);
                    Disc(n, new Vector3(0.07f, 0.27f, 0f), 0.042f, 0.03f, wood, new Vector3(90f, 0f, 0f));
                    Disc(n, new Vector3(0.07f, 0.27f, 0f), 0.034f, 0.034f, Mat("#EDE6DA"), new Vector3(90f, 0f, 0f));
                    Seg(n, new Vector3(0.07f, 0.27f, -0.02f), new Vector3(0.09f, 0.29f, -0.035f), 0.004f, 0.004f, dark);
                    Ball(n, new Vector3(0.09f, 0.29f, -0.038f), 0.009f, dark);
                    for (int k = 0; k < 4; k++) Ring(n, new Vector3(0.022f, 0.42f + k * 0.15f, 0f), 0.009f, 0.0018f, dark, new Vector3(0f, 0f, 90f));
                    return new View(16f, -8f, 34f, 1.05f);
                }
                case 1:
                {
                    // carbon rod: dark blank, cork grip, turquoise band, small silver spinning reel with an orange knob
                    Material blank = Mat("#3A3E52"), cork = Mat("#C9A06A"), band = Mat("#3FB8C4"), orange = Mat("#EE7F2D");
                    TCaps(n, Vector3.zero, 0.018f, 0.006f, 1.0f, blank);
                    Cone(n, new Vector3(0f, 0.15f, 0f), 0.021f, 0.018f, 0.26f, cork);
                    Disc(n, new Vector3(0f, 0.012f, 0f), 0.021f, 0.024f, dark);
                    Band(n, new Vector3(0f, 0.31f, 0f), 0.0175f, 0.03f, band);
                    Seg(n, new Vector3(0f, 0.22f, 0f), new Vector3(0.05f, 0.22f, 0f), 0.006f, 0.006f, chrome);
                    Blob(n, new Vector3(0.078f, 0.22f, 0f), new Vector3(0.028f, 0.034f, 0.026f), chrome);
                    Disc(n, new Vector3(0.078f, 0.262f, 0f), 0.03f, 0.034f, chrome);
                    Ring(n, new Vector3(0.078f, 0.282f, 0f), 0.032f, 0.0035f, orange);
                    Seg(n, new Vector3(0.078f, 0.22f, -0.026f), new Vector3(0.11f, 0.22f, -0.05f), 0.004f, 0.004f, dark);
                    Ball(n, new Vector3(0.112f, 0.22f, -0.053f), 0.01f, orange);
                    for (int k = 0; k < 5; k++)
                    {
                        float y = 0.42f + k * 0.13f, o = 0.026f - k * 0.003f;
                        Seg(n, new Vector3(0f, y, 0f), new Vector3(o, y, 0f), 0.0025f, 0.0025f, chrome);
                        Ring(n, new Vector3(o + 0.006f, y, 0f), 0.009f - k * 0.001f, 0.0018f, chrome);
                    }
                    Ball(n, new Vector3(0f, 1.0f, 0f), 0.006f, chrome);
                    return new View(16f, -8f, 34f, 1.05f);
                }
                case 2:
                {
                    // deep sea rod: thick black blank with orange wraps, gimbal butt, big gold + red conventional reel on top
                    Material blank = Mat("#2E3140"), wrap = Mat("#EE7F2D"), eva = Mat("#1E1B2B"), gold = Mat("#E8C65A"), red = Mat("#C94F3D");
                    TCaps(n, Vector3.zero, 0.024f, 0.01f, 0.95f, blank);
                    Disc(n, new Vector3(0f, 0.014f, 0f), 0.025f, 0.03f, chrome);
                    Box(n, new Vector3(0f, 0f, 0f), new Vector3(0.052f, 0.006f, 0.008f), 0.002f, dark);
                    Cone(n, new Vector3(0f, 0.13f, 0f), 0.026f, 0.025f, 0.18f, eva);
                    Band(n, new Vector3(0f, 0.27f, 0f), 0.024f, 0.06f, chrome);
                    Cone(n, new Vector3(0f, 0.42f, 0f), 0.027f, 0.024f, 0.14f, eva);
                    for (int k = 0; k < 4; k++)
                    {
                        float y = 0.55f + k * 0.1f;
                        Band(n, new Vector3(0f, y, 0f), Mathf.Lerp(0.024f, 0.01f, y / 0.95f) + 0.0018f, 0.018f, wrap);
                    }
                    Vector3 reel = new Vector3(-0.075f, 0.27f, 0f);
                    Seg(n, new Vector3(-0.02f, 0.27f, 0f), reel, 0.008f, 0.008f, chrome);
                    for (int s = -1; s <= 1; s += 2) Disc(n, reel + new Vector3(0f, 0f, s * 0.036f), 0.056f, 0.014f, gold, new Vector3(90f, 0f, 0f));
                    Disc(n, reel, 0.046f, 0.06f, red, new Vector3(90f, 0f, 0f));
                    for (int s = -1; s <= 1; s += 2) Seg(n, reel + new Vector3(-0.05f, s * 0.03f, -0.036f), reel + new Vector3(-0.05f, s * 0.03f, 0.036f), 0.004f, 0.004f, chrome);
                    Ring(n, reel + new Vector3(0f, 0f, 0.048f), 0.016f, 0.005f, dark, new Vector3(90f, 0f, 0f));
                    Seg(n, reel + new Vector3(0f, 0f, 0.05f), reel + new Vector3(0.03f, -0.035f, 0.06f), 0.005f, 0.005f, chrome);
                    Blob(n, reel + new Vector3(0.035f, -0.04f, 0.065f), new Vector3(0.01f, 0.016f, 0.01f), dark);
                    for (int k = 0; k < 4; k++)
                    {
                        float y = 0.5f + k * 0.12f;
                        Seg(n, new Vector3(0f, y, 0f), new Vector3(-0.026f, y, 0f), 0.003f, 0.003f, chrome);
                        Disc(n, new Vector3(-0.03f, y, 0f), 0.009f, 0.012f, chrome, new Vector3(90f, 0f, 0f));
                    }
                    Disc(n, new Vector3(0f, 0.955f, 0f), 0.013f, 0.02f, chrome, new Vector3(90f, 0f, 0f));
                    return new View(14f, -6f, 34f, 1.05f);
                }
                default:
                {
                    // Dr. Fin's research rod: white blank, violet wraps, turquoise grip, glowing bobber on the line
                    Material blank = Mat("#EDE6DA"), violet = Mat("#8E6CD9"), grip = Mat("#3FB8C4"), white = Mat("#F4EFE6");
                    TCaps(n, Vector3.zero, 0.019f, 0.007f, 1.0f, blank);
                    Cone(n, new Vector3(0f, 0.14f, 0f), 0.023f, 0.021f, 0.24f, grip);
                    for (int k = 0; k < 4; k++)
                    {
                        float y = 0.36f + k * 0.16f;
                        Band(n, new Vector3(0f, y, 0f), Mathf.Lerp(0.019f, 0.007f, y) + 0.0018f, 0.016f, violet);
                    }
                    Seg(n, new Vector3(0f, 0.22f, 0f), new Vector3(0.05f, 0.22f, 0f), 0.006f, 0.006f, chrome);
                    Blob(n, new Vector3(0.078f, 0.22f, 0f), new Vector3(0.028f, 0.034f, 0.026f), white);
                    Disc(n, new Vector3(0.078f, 0.262f, 0f), 0.03f, 0.034f, violet);
                    Seg(n, new Vector3(0f, 1.0f, 0f), new Vector3(0.24f, 1.02f, 0f), 0.0016f, 0.0016f, white);
                    Blob(n, new Vector3(0.265f, 1.022f, 0f), new Vector3(0.024f, 0.02f, 0.02f), Glow("#F2C94C", "#FFD95A", 1.3f));
                    Blob(n, new Vector3(0.29f, 1.024f, 0f), new Vector3(0.016f, 0.017f, 0.017f), Mat("#E04848"));
                    return new View(16f, -8f, 34f, 1.05f);
                }
            }
        }

        // ------------------------------------------------------------------ weapons (PlayerHands look, chunkier for the picture)
        static readonly Vector2[] WoodTip = { new Vector2(0.019f, 0f), new Vector2(0.018f, 0.06f), new Vector2(0.01f, 0.16f), new Vector2(0f, 0.24f) };
        static readonly Vector2[] SteelLeaf = { new Vector2(0f, 0f), new Vector2(0.028f, 0.03f), new Vector2(0.05f, 0.12f), new Vector2(0.035f, 0.22f), new Vector2(0f, 0.32f) };
        static readonly Vector2[] GunBarb = { new Vector2(0f, 0f), new Vector2(0.014f, 0.012f), new Vector2(0.009f, 0.045f), new Vector2(0f, 0.08f) };

        static View BuildWoodSpear(Transform t)
        {
            var n = Node(t, Vector3.zero, new Vector3(0f, 0f, -90f));
            Material shaft = Mat("#B88A5A"), wrap = Mat("#6E4A2E"), lace = Mat("#3FB8C4"), charred = Mat("#4A3326");
            TCaps(n, Vector3.zero, 0.02f, 0.017f, 1.0f, shaft);
            Band(n, new Vector3(0f, 0.3f, 0f), 0.0225f, 0.22f, wrap);
            for (int k = 0; k < 3; k++) Ring(n, new Vector3(0f, 0.24f + k * 0.06f, 0f), 0.023f, 0.0035f, lace, new Vector3(0f, 0f, k % 2 == 0 ? 12f : -12f));
            Lathe(n, "wood_tip", WoodTip, charred, new Vector3(0f, 0.97f, 0f), Vector3.zero, Vector3.one);
            Ring(n, new Vector3(0f, 0.975f, 0f), 0.02f, 0.005f, wrap);
            return new View(12f, -10f, 34f, 1.04f);
        }

        static View BuildSteelSpear(Transform t)
        {
            var n = Node(t, Vector3.zero, new Vector3(0f, 0f, -90f));
            Material shaft = Mat("#8E6C50"), grip = Mat("#3A1E2E"), steel = Mat("#C8CED6"), accent = Mat("#3FB8C4");
            TCaps(n, Vector3.zero, 0.021f, 0.018f, 1.05f, shaft);
            Band(n, new Vector3(0f, 0.3f, 0f), 0.0235f, 0.26f, grip);
            Ring(n, new Vector3(0f, 0.165f, 0f), 0.0235f, 0.0045f, accent);
            Ring(n, new Vector3(0f, 0.435f, 0f), 0.0235f, 0.0045f, accent);
            TCaps(n, new Vector3(0f, 0.99f, 0f), 0.026f, 0.02f, 0.09f, steel);
            Lathe(n, "steel_leaf", SteelLeaf, steel, new Vector3(0f, 1.06f, 0f), Vector3.zero, new Vector3(1f, 1f, 0.3f));
            Cone(n, new Vector3(0f, -0.015f, 0f), 0.004f, 0.02f, 0.05f, steel);
            return new View(12f, -10f, 34f, 1.04f);
        }

        static View BuildSpeargun(Transform t)
        {
            var n = Node(t, Vector3.zero, new Vector3(0f, 0f, -90f));   // barrel along node +Y (→ +X), node +X = down
            Material dark = Mat("#2E3140"), rubber = Mat("#3A1E2E"), accent = Mat("#EE7F2D"), steel = Mat("#C8CED6");
            TCaps(n, Vector3.zero, 0.026f, 0.023f, 0.85f, dark);
            Blob(n, new Vector3(0f, 0.84f, 0f), new Vector3(0.034f, 0.04f, 0.032f), dark);
            for (int s = -1; s <= 1; s += 2) Ring(n, new Vector3(-0.004f, 0.74f + s * 0.025f, s * 0.012f), 0.032f, 0.0065f, rubber, new Vector3(90f, s * 12f, 0f));
            Blob(n, new Vector3(0f, 0.01f, 0f), new Vector3(0.032f, 0.04f, 0.032f), accent);
            Caps(n, new Vector3(0.015f, 0.2f, 0f), 0.019f, 0.13f, accent, new Vector3(0f, 0f, -74f));
            Ring(n, new Vector3(0.036f, 0.27f, 0f), 0.02f, 0.0035f, dark, new Vector3(90f, 0f, 0f));
            Box(n, new Vector3(0.034f, 0.262f, 0f), new Vector3(0.016f, 0.006f, 0.006f), 0.002f, dark, new Vector3(0f, 0f, 20f));
            Disc(n, new Vector3(0.032f, 0.46f, 0f), 0.022f, 0.014f, dark, new Vector3(90f, 0f, 0f));
            TCaps(n, new Vector3(-0.034f, 0.06f, 0f), 0.0055f, 0.0055f, 0.92f, steel);
            Lathe(n, "gun_barb", GunBarb, steel, new Vector3(-0.034f, 0.97f, 0f), Vector3.zero, Vector3.one);
            return new View(16f, -10f, 14f, 1.06f);
        }

        static View BuildStick(Transform t)
        {
            var n = Node(t, Vector3.zero, new Vector3(0f, 0f, -90f));
            Material wood = Mat("#9A6B43"), bark = Mat("#6E4A2E"), leaf = Mat("#7FA243");
            TCaps(n, Vector3.zero, 0.024f, 0.02f, 0.4f, wood);
            var b = Node(n, new Vector3(0f, 0.37f, 0f), new Vector3(-8f, 0f, 7f));
            TCaps(b, Vector3.zero, 0.02f, 0.016f, 0.36f, wood);
            Blob(b, new Vector3(0.014f, 0.12f, 0f), new Vector3(0.016f, 0.022f, 0.016f), bark);
            var tw = Node(b, new Vector3(0f, 0.24f, 0f), new Vector3(0f, 0f, -40f));
            TCaps(tw, Vector3.zero, 0.006f, 0.0025f, 0.11f, bark);
            Blob(tw, new Vector3(0f, 0.11f, 0f), new Vector3(0.018f, 0.03f, 0.005f), leaf);
            return new View(12f, -10f, 30f, 1.06f);
        }

        // ------------------------------------------------------------------ snacks (PlayerHands food colours)
        static View BuildFood(Transform t, FoodDef f)
        {
            switch (f.id)
            {
                case "seaweed_chips": return BuildSnackBag(t, false);
                case "shark_gummies": return BuildSnackBag(t, true);
                case "krill_bar": return BuildKrillBars(t);
                case "galley_coffee": return BuildCoffee(t);
                case "fish_sandwich": return BuildSandwich(t);
                case "glow_soda": return BuildGlowSoda(t);
                case "water_bottle": return BuildWaterPack(t);
                default: return BuildCrate(t, FoodDatabase.DeliveryId(f));
            }
        }

        static readonly Vector2[] FinTri = { new Vector2(-0.008f, 0f), new Vector2(0.01f, 0f), new Vector2(-0.004f, 0.016f) };
        static readonly Vector2[] TailTri = { new Vector2(0f, -0.006f), new Vector2(0f, 0.006f), new Vector2(0.016f, 0.012f), new Vector2(0.016f, -0.012f) };

        static View BuildSnackBag(Transform t, bool gummies)
        {
            Material bag = Mat(gummies ? "#3FB8C4" : "#6E9445"), crimp = Mat(gummies ? "#1C8E96" : "#4E6F2E"), label = Mat("#F4EFE6");
            Blob(t, new Vector3(0f, 0.1f, 0f), new Vector3(0.07f, 0.095f, 0.03f), bag);
            Box(t, new Vector3(0f, 0.195f, 0f), new Vector3(0.13f, 0.018f, 0.016f), 0.005f, crimp);
            Box(t, new Vector3(0f, 0.005f, 0f), new Vector3(0.13f, 0.018f, 0.016f), 0.005f, crimp);
            Blob(t, new Vector3(0f, 0.1f, -0.026f), new Vector3(0.045f, 0.036f, 0.006f), label);
            if (!gummies)
            {
                Blob(t, new Vector3(0f, 0.1f, -0.031f), new Vector3(0.016f, 0.026f, 0.003f), Mat("#7FA243"), new Vector3(0f, 0f, 25f));
                Material chip = Mat("#7E9A3A"), dark = Mat("#4E6F2E");
                Blob(t, new Vector3(-0.05f, 0.012f, -0.06f), new Vector3(0.026f, 0.004f, 0.018f), chip, new Vector3(12f, 20f, 8f));
                Blob(t, new Vector3(0.035f, 0.01f, -0.07f), new Vector3(0.024f, 0.004f, 0.017f), dark, new Vector3(-10f, -30f, 0f));
                Blob(t, new Vector3(0f, 0.03f, -0.058f), new Vector3(0.022f, 0.004f, 0.016f), chip, new Vector3(35f, 0f, 20f));
                return new View(22f, -16f);
            }
            // the label shows a pink fin; three gummy sharks in pink / orange / blue lie in front of the bag
            Plate(t, "gummy_fin_label", FinTri, 0.003f, Mat("#E0607E"), new Vector3(0f, 0.094f, -0.032f), Vector3.zero);
            string[] cols = { "#E0607E", "#F28C4A", "#4FA3E0" };
            Vector3[] pos = { new Vector3(-0.05f, 0.012f, -0.065f), new Vector3(0.04f, 0.012f, -0.075f), new Vector3(-0.005f, 0.03f, -0.055f) };
            float[] yaw = { 20f, -25f, 5f };
            for (int i = 0; i < 3; i++)
            {
                var g = Node(t, pos[i], new Vector3(i == 2 ? 30f : 0f, yaw[i], 0f));
                Material m = Mat(cols[i]);
                Blob(g, Vector3.zero, new Vector3(0.026f, 0.01f, 0.011f), m);
                Plate(g, "gummy_fin", FinTri, 0.006f, m, new Vector3(0f, 0.006f, 0f), new Vector3(0f, 0f, 0f));
                Plate(g, "gummy_tail", TailTri, 0.006f, m, new Vector3(0.022f, 0f, 0f), Vector3.zero);
            }
            return new View(22f, -16f);
        }

        static View BuildKrillBars(Transform t)
        {
            Material pink = Mat("#E0607E"), yellow = Mat("#F2C94C"), orange = Mat("#F28C4A");
            Box(t, new Vector3(0.012f, 0.014f, 0.05f), new Vector3(0.13f, 0.026f, 0.042f), 0.008f, yellow, new Vector3(0f, -8f, 0f));
            Box(t, new Vector3(0.012f, 0.0285f, 0.05f), new Vector3(0.03f, 0.003f, 0.043f), 0.001f, pink, new Vector3(0f, -8f, 0f));
            Box(t, new Vector3(-0.035f, 0.016f, -0.02f), new Vector3(0.07f, 0.03f, 0.046f), 0.006f, yellow, new Vector3(0f, 12f, 0f));
            Box(t, new Vector3(0.04f, 0.014f, -0.035f), new Vector3(0.09f, 0.024f, 0.036f), 0.009f, pink, new Vector3(0f, 12f, 0f));
            for (int k = 0; k < 4; k++)
                Blob(t, new Vector3(0.012f + k * 0.018f, 0.027f, -0.03f - k * 0.004f), new Vector3(0.006f, 0.003f, 0.004f), orange, new Vector3(0f, k * 30f, 0f));
            return new View(26f, -34f);
        }

        static View BuildCoffee(Transform t)
        {
            Material white = Mat("#EDE6DA"), coffee = Mat("#5A3424"), band = Mat("#3FB8C4"), steam = Mat("#F4EFE6");
            Cone(t, new Vector3(0f, 0.045f, 0f), 0.04f, 0.044f, 0.09f, white);
            Part(t, FilmMesh.Cylinder(0.04f, 0.004f, 28), coffee, new Vector3(0f, 0.0915f, 0f));
            Ring(t, new Vector3(0f, 0.09f, 0f), 0.042f, 0.0035f, white);
            Ring(t, new Vector3(0.05f, 0.048f, 0f), 0.024f, 0.007f, white, new Vector3(90f, 0f, 0f));
            Band(t, new Vector3(0f, 0.03f, 0f), 0.0428f, 0.014f, band);
            Box(t, new Vector3(0f, 0.062f, -0.043f), new Vector3(0.004f, 0.022f, 0.002f), 0.001f, Mat("#284F80"));
            Box(t, new Vector3(0f, 0.069f, -0.043f), new Vector3(0.014f, 0.003f, 0.002f), 0.001f, Mat("#284F80"));
            Blob(t, new Vector3(-0.01f, 0.13f, 0f), new Vector3(0.014f, 0.018f, 0.014f), steam);
            Blob(t, new Vector3(0.012f, 0.162f, 0f), new Vector3(0.011f, 0.015f, 0.011f), steam);
            return new View(24f, -26f);
        }

        static View BuildSandwich(Transform t)
        {
            Material bun = Mat("#D9A066"), lettuce = Mat("#7FA243"), fish = Mat("#E8B56A"), tomato = Mat("#D94848"), sesame = Mat("#F4EFE6");
            Blob(t, new Vector3(0f, 0.015f, 0f), new Vector3(0.06f, 0.016f, 0.04f), bun);
            Blob(t, new Vector3(0f, 0.03f, 0f), new Vector3(0.064f, 0.007f, 0.044f), lettuce);
            for (int k = 0; k < 4; k++)
            {
                float a = k * 90f + 30f;
                Vector3 o = Quaternion.Euler(0f, a, 0f) * new Vector3(0.058f, 0f, 0f);
                Blob(t, new Vector3(o.x, 0.028f, o.z * 0.7f), new Vector3(0.016f, 0.006f, 0.012f), lettuce, new Vector3(0f, a, 15f));
            }
            Part(t, FilmMesh.Cylinder(0.026f, 0.007f, 20), tomato, new Vector3(-0.022f, 0.037f, -0.006f));
            Blob(t, new Vector3(0.012f, 0.043f, 0f), new Vector3(0.07f, 0.01f, 0.03f), fish);
            Blob(t, new Vector3(0f, 0.058f, 0f), new Vector3(0.06f, 0.025f, 0.04f), bun);
            for (int k = 0; k < 7; k++)
            {
                float a = k * 51f * Mathf.Deg2Rad, rr = 0.012f + (k % 3) * 0.011f;
                Blob(t, new Vector3(Mathf.Cos(a) * rr * 1.3f, 0.081f - rr * 0.18f, Mathf.Sin(a) * rr - 0.004f), new Vector3(0.0035f, 0.0018f, 0.0022f), sesame, new Vector3(0f, k * 40f, 0f));
            }
            return new View(26f, -24f);
        }

        static View BuildGlowSoda(Transform t)
        {
            Material can = Glow("#7FE0C8", "#5FD6B8", 0.7f), silver = Mat("#B8BEC6"), dark = Mat("#2E3140");
            Part(t, FilmMesh.RoundedCylinder(0.033f, 0.12f, 28, 0.006f), can, new Vector3(0f, 0.06f, 0f));
            Cone(t, new Vector3(0f, 0.126f, 0f), 0.033f, 0.027f, 0.012f, silver);
            Ring(t, new Vector3(0.008f, 0.133f, 0f), 0.006f, 0.0015f, silver);
            Band(t, new Vector3(0f, 0.06f, 0f), 0.0335f, 0.05f, dark);
            Box(t, Around(0.0345f, -15f, 0.06f), new Vector3(0.012f, 0.045f, 0.003f), 0.002f, Glow("#F2C94C", "#FFD95A", 1.2f), new Vector3(0f, 15f, 25f));
            Seg(t, new Vector3(-0.006f, 0.12f, 0f), new Vector3(-0.022f, 0.19f, 0.01f), 0.004f, 0.004f, Mat("#E0607E"));
            return new View(22f, -18f);
        }

        static View BuildWaterPack(Transform t)
        {
            Material bottle = Mat("#9FD8E8"), cap = Mat("#3778AB"), label = Mat("#EDE6DA"), stripe = Mat("#3FA9B5"), tray = Mat("#C9A06A");
            Box(t, new Vector3(0f, 0.012f, -0.005f), new Vector3(0.15f, 0.024f, 0.13f), 0.01f, tray);
            Vector3[] pos = { new Vector3(-0.035f, 0.02f, 0.022f), new Vector3(0.035f, 0.02f, 0.022f), new Vector3(0f, 0.02f, -0.04f) };
            for (int i = 0; i < pos.Length; i++)
            {
                Vector3 p = pos[i];
                Caps(t, p, 0.028f, 0.16f, bottle);
                Cone(t, p + new Vector3(0f, 0.168f, 0f), 0.016f, 0.012f, 0.02f, bottle);
                Disc(t, p + new Vector3(0f, 0.183f, 0f), 0.0125f, 0.014f, cap);
                Band(t, p + new Vector3(0f, 0.08f, 0f), 0.0285f, 0.045f, label);
                Band(t, p + new Vector3(0f, 0.08f, 0f), 0.0288f, 0.008f, stripe);
            }
            return new View(22f, -18f);
        }

        /// Unknown key: a delivery crate (wood, slats, orange strap) with a sticker in a colour hashed from the key.
        static View BuildCrate(Transform t, string key)
        {
            uint h = Hash(key);
            Color sticker = Color.HSVToRGB((h % 360u) / 360f, 0.55f, 0.95f);
            Material wood = Mat("#C9A06A"), dark = Mat("#8E6C50");
            Box(t, new Vector3(0f, 0.15f, 0f), new Vector3(0.42f, 0.3f, 0.3f), 0.02f, wood);
            for (int k = 0; k < 3; k++) Box(t, new Vector3(0f, 0.05f + k * 0.1f, -0.152f), new Vector3(0.42f, 0.018f, 0.006f), 0.003f, dark);
            for (int s = -1; s <= 1; s += 2) Box(t, new Vector3(s * 0.2f, 0.15f, -0.142f), new Vector3(0.026f, 0.3f, 0.026f), 0.006f, dark);
            Box(t, new Vector3(0f, 0.15f, 0f), new Vector3(0.06f, 0.305f, 0.305f), 0.004f, Mat("#EE7F2D"));
            Box(t, new Vector3(0.12f, 0.2f, -0.155f), new Vector3(0.08f, 0.06f, 0.004f), 0.01f, LowPoly.Mat(sticker));
            return Upright;
        }
    }

    /// A picture slot for an item: shows the 2D glyph at first and swaps in the 3D picture as soon as it is rendered
    /// (the owning page decides when – UIAppShop renders the visible ones first, a few per frame).
    public sealed class UIItemIcon : UnityEngine.UIElements.VisualElement
    {
        public readonly string Key;
        readonly UIGlyph glyph;
        bool resolved;

        public UIItemIcon(string key, UIGlyph.DrawFn fallback, Color fallbackColor, float size)
        {
            Key = key;
            pickingMode = UnityEngine.UIElements.PickingMode.Ignore;
            UIKit.Size(this, size, size);
            UIKit.Center(this);
            var tex = UIItemIcons3D.Peek(key);
            if (tex != null)
            {
                UIKit.Image(this, tex);
                resolved = true;
                return;
            }
            if (fallback != null)
            {
                glyph = new UIGlyph(fallback, fallbackColor, size * 0.52f);
                Add(glyph);
            }
            if (string.IsNullOrEmpty(key) || UIItemIcons3D.HasFailed(key)) resolved = true;
        }

        /// True once the 3D picture is shown (or rendering failed and the glyph stays).
        public bool Resolved => resolved;

        /// Swaps in the picture; render = false only takes it from the cache. Returns Resolved.
        public bool TryResolve(bool render)
        {
            if (resolved) return true;
            var tex = render ? UIItemIcons3D.Get(Key) : UIItemIcons3D.Peek(Key);
            if (tex != null)
            {
                UIKit.Image(this, tex);
                if (glyph != null) glyph.style.display = UnityEngine.UIElements.DisplayStyle.None;
                resolved = true;
            }
            else if (UIItemIcons3D.HasFailed(Key)) resolved = true;
            return resolved;
        }
    }
}
