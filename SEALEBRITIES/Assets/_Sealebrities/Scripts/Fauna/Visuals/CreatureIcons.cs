using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Sealebrities
{
    /// 256x256 RGBA journal icons (transparent background, 3/4 side view), cached per species.
    /// Rendered with a temporary camera on layer 31 far below the world; falls back to a CPU rasterized
    /// version of the same model when GPU rendering is unavailable. Never leaves objects behind.
    public static class CreatureIcons
    {
        const int Size = 256;
        const int Super = 2;
        const int Layer = 31;
        static readonly Vector3 Stage = new Vector3(0f, -5000f, 0f);
        static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();

        /// Round 3: species whose look is not a CreatureFactory model (the curiosities / memes – a throne, a gaming PC,
        /// a crab rave …) provide their own model here (any renderers; root at the origin, about 1 m long). Null = the
        /// normal creature model.
        public static Func<SpeciesDef, GameObject> CustomModel;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Cache.Clear(); CustomModel = null; }

        public static Texture2D Get(SpeciesDef s)
        {
            if (s == null) return null;
            if (Cache.TryGetValue(s.id, out var cached) && cached != null) return cached;

            Texture2D tex = null;
            try { tex = RenderGpu(s); }
            catch (Exception e) { Debug.LogWarning("Sealebrities: icon render failed for " + s.id + ", using fallback (" + e.Message + ")"); tex = null; }
            if (tex == null && !s.meme)
            {
                try { tex = RenderCpu(s); }
                catch (Exception e) { Debug.LogWarning("Sealebrities: icon fallback failed for " + s.id + " (" + e.Message + ")"); tex = null; }
            }
            if (tex == null) tex = Silhouette(s);
            tex.name = "SB_Icon_" + s.id;
            Cache[s.id] = tex;
            return tex;
        }

        // ------------------------------------------------------------------------------------------ view

        static void ViewFor(SpeciesDef s, out Vector3 fwd, out Vector3 up) => CreatureMeshRaster.IconView(s, out fwd, out up);

        /// Icon-only pose: eels get a gentle S-curve so they fill the frame.
        static void Pose(SpeciesDef s, CreatureRig rig)
        {
            if (rig == null || rig.spine == null || s.plan != BodyPlan.Eel) return;
            for (int i = 0; i < rig.spine.Length; i++)
                if (rig.spine[i] != null) rig.spine[i].localRotation = Quaternion.Euler(0f, 24f * Mathf.Sin(i * 1.25f + 0.6f), 0f);
        }

        static void Fit(IList<Vector3> v, Vector3 fwd, Vector3 up, out Vector3 right, out Vector3 up2, out Vector3 center, out float halfExtent, out float depth)
        {
            right = Vector3.Cross(up, fwd).normalized;
            up2 = Vector3.Cross(fwd, right);
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            for (int i = 0; i < v.Count; i++)
            {
                float x = Vector3.Dot(v[i], right), y = Vector3.Dot(v[i], up2), z = Vector3.Dot(v[i], fwd);
                if (x < minX) minX = x; if (x > maxX) maxX = x;
                if (y < minY) minY = y; if (y > maxY) maxY = y;
                if (z < minZ) minZ = z; if (z > maxZ) maxZ = z;
            }
            center = right * ((minX + maxX) * 0.5f) + up2 * ((minY + maxY) * 0.5f) + fwd * ((minZ + maxZ) * 0.5f);
            halfExtent = Mathf.Max(maxX - minX, maxY - minY) * 0.5f;
            depth = maxZ - minZ;
        }

        // ------------------------------------------------------------------------------------------ GPU

        static Texture2D RenderGpu(SpeciesDef s)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return null;
            ViewFor(s, out Vector3 fwd, out Vector3 up);

            GameObject model = null, shot = null, camGo = null, key = null, fill = null, rim = null;
            Mesh baked = null;
            RenderTexture rt = null;
            Texture2D big = null;
            RenderTexture prevActive = RenderTexture.active;
            bool prevFog = RenderSettings.fog;
            SphericalHarmonicsL2 prevProbe = RenderSettings.ambientProbe;
            AmbientMode prevMode = RenderSettings.ambientMode;
            Color prevAmbient = RenderSettings.ambientLight;
            Light prevSun = RenderSettings.sun;
            var disabled = new List<Light>();
            try
            {
                GameObject custom = null;
                if (CustomModel != null)
                {
                    try { custom = CustomModel(s); }
                    catch (Exception e) { Debug.LogWarning("Sealebrities: custom icon model failed for " + s.id + ": " + e.Message); custom = null; }
                }
                model = custom != null ? custom : CreatureFactory.Build(s);
                model.name = "SB_IconModel_" + s.id;
                var mt = model.transform;
                mt.SetPositionAndRotation(Stage, Quaternion.identity);
                mt.localScale = Vector3.one;
                SetLayer(mt, Layer);
                var rig = model.GetComponent<CreatureRig>();
                if (rig != null && rig.lureLight != null) rig.lureLight.range = 2f;
                Pose(s, rig);

                // CPU-skin the (posed) model into a static mesh: no dependency on skinning update timing
                Material[] shotMats;
                Vector3 shotPos;
                Quaternion shotRot;
                if (custom != null)
                {
                    baked = BakeAll(model, out shotMats);
                    if (baked == null) return null;
                    shotPos = Stage;
                    shotRot = Quaternion.identity;
                }
                else
                {
                    var smr = model.GetComponentInChildren<SkinnedMeshRenderer>();
                    if (smr == null) return null;
                    baked = new Mesh { name = "SB_IconBake" };
                    smr.BakeMesh(baked);
                    smr.enabled = false;
                    shotMats = smr.sharedMaterials;
                    shotPos = smr.transform.position;
                    shotRot = smr.transform.rotation;
                }
                shot = new GameObject("SB_IconMesh");
                shot.layer = Layer;
                shot.transform.SetPositionAndRotation(shotPos, shotRot);
                shot.AddComponent<MeshFilter>().sharedMesh = baked;
                var mr = shot.AddComponent<MeshRenderer>();
                mr.sharedMaterials = shotMats;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.lightProbeUsage = LightProbeUsage.Off;
                mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                Fit(baked.vertices, fwd, up, out Vector3 right, out Vector3 up2, out Vector3 center, out float half, out float depth);
                center += shotPos - Stage;

                const float fov = 20f;
                float dist = half / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * 1.12f + depth * 0.5f;
                Vector3 target = Stage + center;
                camGo = new GameObject("SB_IconCamera");
                camGo.layer = Layer;
                camGo.transform.SetPositionAndRotation(target - fwd * dist, Quaternion.LookRotation(fwd, up2));
                var cam = camGo.AddComponent<Camera>();
                cam.enabled = false;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                cam.cullingMask = 1 << Layer;
                cam.fieldOfView = fov;
                cam.nearClipPlane = Mathf.Max(0.01f, dist - depth * 0.8f - 0.5f);
                cam.farClipPlane = dist + depth + 1f;
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

                // bright, neutral studio light: one clear key from the upper front so every facet reads, soft fill, small rim
                key = MakeLight("SB_IconKey", (fwd * 0.5f + Vector3.down * 0.8f + right * 0.3f).normalized, new Color(1f, 0.98f, 0.95f), 1.15f);
                fill = MakeLight("SB_IconFill", (fwd * 0.5f + Vector3.up * 0.25f - right * 0.75f).normalized, new Color(0.9f, 0.94f, 1f), 0.42f);
                rim = MakeLight("SB_IconRim", (-fwd + Vector3.down * 0.35f).normalized, new Color(0.95f, 0.97f, 1f), 0.35f);

                var lights = Object.FindObjectsByType<Light>();
                for (int i = 0; i < lights.Length; i++)
                {
                    var l = lights[i];
                    if (l == null || !l.enabled || l.type != LightType.Directional || l.gameObject.layer == Layer) continue;
                    l.enabled = false;
                    disabled.Add(l);
                }
                RenderSettings.sun = key.GetComponent<Light>();
                RenderSettings.fog = false;
                var amb = new Color(0.5f, 0.52f, 0.55f);
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = amb;
                var sh = new SphericalHarmonicsL2();
                sh.AddAmbientLight(amb);
                RenderSettings.ambientProbe = sh;

                int px = Size * Super;
                rt = RenderTexture.GetTemporary(px, px, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default, 1);
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                big = new Texture2D(px, px, TextureFormat.RGBA32, false);
                big.ReadPixels(new Rect(0, 0, px, px), 0, 0, false);
                var src = big.GetPixels32();
                cam.targetTexture = null;
                return Finish(src, px);
            }
            finally
            {
                RenderTexture.active = prevActive;
                for (int i = 0; i < disabled.Count; i++) if (disabled[i] != null) disabled[i].enabled = true;
                RenderSettings.sun = prevSun;
                RenderSettings.fog = prevFog;
                RenderSettings.ambientMode = prevMode;
                RenderSettings.ambientLight = prevAmbient;
                RenderSettings.ambientProbe = prevProbe;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                Kill(model); Kill(shot); Kill(camGo); Kill(key); Kill(fill); Kill(rim);
                if (big != null) Object.DestroyImmediate(big);
                if (baked != null) Object.DestroyImmediate(baked);
            }
        }

        /// Round 3: every renderer of a custom (meme) model baked into one static mesh in model-root space (one
        /// submesh per renderer material). The model's own renderers are switched off.
        static Mesh BakeAll(GameObject model, out Material[] materials)
        {
            materials = null;
            var rs = model.GetComponentsInChildren<Renderer>(true);
            var cis = new List<CombineInstance>();
            var mats = new List<Material>();
            var temps = new List<Mesh>();
            Matrix4x4 toRoot = model.transform.worldToLocalMatrix;
            int verts = 0;
            try
            {
                for (int i = 0; i < rs.Length; i++)
                {
                    var r = rs[i];
                    if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                    Mesh m = null;
                    Matrix4x4 mtx;
                    if (r is SkinnedMeshRenderer sk)
                    {
                        m = new Mesh { name = "SB_IconPart" };
                        sk.BakeMesh(m, true);
                        temps.Add(m);
                        mtx = toRoot * Matrix4x4.TRS(sk.transform.position, sk.transform.rotation, Vector3.one);
                    }
                    else
                    {
                        var mf = r.GetComponent<MeshFilter>();
                        if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                        m = mf.sharedMesh;
                        mtx = toRoot * r.transform.localToWorldMatrix;
                    }
                    var sm = r.sharedMaterials;
                    for (int k = 0; k < m.subMeshCount; k++)
                    {
                        cis.Add(new CombineInstance { mesh = m, subMeshIndex = k, transform = mtx });
                        mats.Add(sm.Length > 0 ? sm[Mathf.Min(k, sm.Length - 1)] : null);
                    }
                    verts += m.vertexCount;
                    r.enabled = false;
                }
                if (cis.Count == 0) return null;
                var outMesh = new Mesh { name = "SB_IconBake" };
                if (verts > 65000) outMesh.indexFormat = IndexFormat.UInt32;
                outMesh.CombineMeshes(cis.ToArray(), false, true);
                materials = mats.ToArray();
                return outMesh;
            }
            finally
            {
                for (int i = 0; i < temps.Count; i++) if (temps[i] != null) Object.DestroyImmediate(temps[i]);
            }
        }

        static GameObject MakeLight(string name, Vector3 dir, Color c, float intensity)
        {
            var go = new GameObject(name);
            go.layer = Layer;
            go.transform.SetPositionAndRotation(Stage, Quaternion.LookRotation(dir));
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

        /// Validates the readback, repairs a lost alpha channel and downsamples 2x2 (alpha weighted).
        static Texture2D Finish(Color32[] src, int px)
        {
            int covered = 0, opaqueCorners = 0;
            for (int i = 0; i < src.Length; i++) if (src[i].a > 128) covered++;
            int[] corners = { 0, px - 1, (px - 1) * px, px * px - 1 };
            for (int i = 0; i < corners.Length; i++) if (src[corners[i]].a > 128) opaqueCorners++;
            if (opaqueCorners >= 3)
            {
                // alpha was not preserved by the pipeline: the clear color is black, key it out
                covered = 0;
                for (int i = 0; i < src.Length; i++)
                {
                    var c = src[i];
                    int m = Math.Max(c.r, Math.Max(c.g, c.b));
                    c.a = (byte)Math.Min(255, m * 64);
                    src[i] = c;
                    if (c.a > 128) covered++;
                }
            }
            if (covered < src.Length / 200) return null;   // nothing visible: let the CPU fallback draw it

            var res = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float r = 0, g = 0, b = 0, a = 0;
                    for (int dy = 0; dy < Super; dy++)
                        for (int dx = 0; dx < Super; dx++)
                        {
                            var c = src[(y * Super + dy) * px + x * Super + dx];
                            float ca = c.a / 255f;
                            r += c.r * ca; g += c.g * ca; b += c.b * ca; a += ca;
                        }
                    float inv = a > 1e-4f ? 1f / a : 0f;
                    res[y * Size + x] = new Color32((byte)Mathf.Clamp(r * inv, 0f, 255f), (byte)Mathf.Clamp(g * inv, 0f, 255f), (byte)Mathf.Clamp(b * inv, 0f, 255f),
                                                    (byte)Mathf.Clamp(a / (Super * Super) * 255f, 0f, 255f));
                }
            return MakeIcon(res);
        }

        static Texture2D MakeIcon(Color32[] px)
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.SetPixels32(px);
            tex.Apply(true, false);
            return tex;
        }

        // ------------------------------------------------------------------------------------------ CPU fallback

        static Texture2D RenderCpu(SpeciesDef s)
        {
            var bake = CreatureMeshBakery.Get(s);
            var px = CreatureMeshRaster.RenderFlat(bake, Size);
            return MakeIcon(px);
        }

        /// Last resort: a flat side-profile silhouette in the species colors.
        static Texture2D Silhouette(SpeciesDef s)
        {
            var px = new Color32[Size * Size];
            Color a = s.colorA, b = s.colorB;
            float hgt = Mathf.Clamp(s.bodyHeight, 0.15f, 0.8f);
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float u = (x + 0.5f) / Size * 2f - 1f, v = (y + 0.5f) / Size * 2f - 1f;
                    // body ellipse (head left) + tail triangle
                    float bx = (u + 0.15f) / 0.62f, by = v / (hgt * 0.9f);
                    bool body = bx * bx + by * by <= 1f;
                    bool tail = u > 0.42f && u < 0.85f && Mathf.Abs(v) < (u - 0.42f) * 0.9f + 0.04f;
                    if (!body && !tail) { px[y * Size + x] = new Color32(0, 0, 0, 0); continue; }
                    Color c = Color.Lerp(b, a, Mathf.Clamp01(0.5f + v / Mathf.Max(0.1f, hgt)));
                    bool eye = (u + 0.55f) * (u + 0.55f) + (v - hgt * 0.25f) * (v - hgt * 0.25f) < 0.0025f;
                    if (eye) c = Color.black;
                    c.a = 1f;
                    px[y * Size + x] = c;
                }
            return MakeIcon(px);
        }
    }
}
