using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Collects static geometry per material (and optionally per grid cell), then merges it with Mesh.CombineMeshes
    /// into UInt32-indexed meshes. Cells get a LODGroup that culls them beyond a distance.
    /// All input geometry is faceted (PropMeshBuilder.ToMesh is flat by default), so merged chunks keep the low-poly look;
    /// one draw call per chunk and flat color.
    public sealed class PropBatch
    {
        struct Key : IEquatable<Key>
        {
            public int mat, cx, cz;
            public bool Equals(Key o) => mat == o.mat && cx == o.cx && cz == o.cz;
            public override bool Equals(object obj) => obj is Key k && Equals(k);
            public override int GetHashCode() => (mat * 73856093) ^ (cx * 19349663) ^ (cz * 83492791);
        }

        readonly float cellSize;
        readonly List<Material> mats = new List<Material>();
        readonly Dictionary<Material, int> matIndex = new Dictionary<Material, int>();
        readonly Dictionary<Key, List<CombineInstance>> groups = new Dictionary<Key, List<CombineInstance>>();
        readonly Dictionary<Key, PropMeshBuilder> builders = new Dictionary<Key, PropMeshBuilder>();
        readonly Dictionary<int, ShadowCastingMode> shadowOverride = new Dictionary<int, ShadowCastingMode>();
        readonly List<Mesh> temps = new List<Mesh>();

        public PropBatch(float cellSize = 0f) { this.cellSize = cellSize; }

        int Index(Material mat)
        {
            if (mat == null) mat = MaterialLibrary.Opaque(Color.magenta);
            if (!matIndex.TryGetValue(mat, out int i))
            {
                i = mats.Count;
                mats.Add(mat);
                matIndex[mat] = i;
            }
            return i;
        }

        void Cell(Vector3 p, out int cx, out int cz)
        {
            if (cellSize <= 0f) { cx = 0; cz = 0; return; }
            cx = Mathf.FloorToInt(p.x / cellSize);
            cz = Mathf.FloorToInt(p.z / cellSize);
        }

        /// Builder for unique geometry of a material (cell chosen from worldPos when the batch is cell based).
        public PropMeshBuilder B(Material mat, Vector3 worldPos = default)
        {
            var k = new Key { mat = Index(mat) };
            Cell(worldPos, out k.cx, out k.cz);
            if (!builders.TryGetValue(k, out var b))
            {
                b = new PropMeshBuilder();
                builders[k] = b;
            }
            return b;
        }

        /// Builder for world-space geometry of a material (its matrix is reset to identity).
        public PropMeshBuilder W(Material mat, Vector3 worldPos = default)
        {
            var b = B(mat, worldPos);
            b.M = Matrix4x4.identity;
            return b;
        }

        public void SetShadows(Material mat, ShadowCastingMode mode) => shadowOverride[Index(mat)] = mode;

        /// Adds an instance of a shared mesh (one sub mesh).
        public void Add(Mesh mesh, Material mat, Matrix4x4 trs, int subMesh = 0)
        {
            if (mesh == null) return;
            var k = new Key { mat = Index(mat) };
            Cell(trs.MultiplyPoint3x4(Vector3.zero), out k.cx, out k.cz);
            if (!groups.TryGetValue(k, out var list))
            {
                list = new List<CombineInstance>();
                groups[k] = list;
            }
            list.Add(new CombineInstance { mesh = mesh, transform = trs, subMeshIndex = subMesh });
        }

        public void Add(PropMeshPart part, Matrix4x4 trs)
        {
            if (part.mesh != null) Add(part.mesh, part.mat, trs);
        }

        public void Add(PropModel model, Matrix4x4 trs)
        {
            if (model == null) return;
            for (int i = 0; i < model.parts.Count; i++) Add(model.parts[i], trs);
        }

        public bool IsEmpty => groups.Count == 0 && builders.Count == 0;

        static long CellKey(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;

        /// Merges everything into renderers under parent. Returns the batch root.
        /// cullDistance > 0 adds a LODGroup per cell (scaled by the graphics quality tier, see PropQuality).
        public GameObject Flush(Transform parent, string name, ShadowCastingMode shadows = ShadowCastingMode.On, float cullDistance = 0f, bool receiveShadows = true)
        {
            var root = NewRoot(parent, name);
            var cells = FlushCells(root.transform, name, shadows, receiveShadows);
            foreach (var kv in cells)
                if (cullDistance > 0f && kv.Value.renderers.Count > 0) PropQuality.RegisterCull(kv.Value.holder.gameObject, kv.Value.renderers, cullDistance);
            return root;
        }

        /// Merges two batches built on the same cell grid (full detail + lite) into one LODGroup per cell:
        /// the full meshes up to nearDistance, the lite meshes up to farDistance (both scaled by the quality tier).
        public static GameObject FlushLod(PropBatch full, PropBatch lite, Transform parent, string name, ShadowCastingMode shadows,
            float nearDistance, float farDistance, ShadowCastingMode liteShadows = ShadowCastingMode.On)
        {
            var root = NewRoot(parent, name);
            var a = full.FlushCells(root.transform, name + "_LOD0", shadows, true);
            var b = lite.FlushCells(root.transform, name + "_LOD1", liteShadows, true);
            var empty = new List<Renderer>();
            foreach (var kv in a)
            {
                var lo = b.TryGetValue(kv.Key, out var bc) ? bc.renderers : empty;
                if (bc != null)
                {
                    // parent the lite renderers under the same holder so the LODGroup covers both
                    for (int i = 0; i < lo.Count; i++) if (lo[i] != null) lo[i].transform.SetParent(kv.Value.holder, true);
                    if (bc.holder != null && bc.holder != kv.Value.holder && bc.holder.childCount == 0) UnityEngine.Object.Destroy(bc.holder.gameObject);
                }
                PropQuality.RegisterLod(kv.Value.holder.gameObject, kv.Value.renderers, lo, nearDistance, farDistance);
            }
            foreach (var kv in b)
            {
                if (a.ContainsKey(kv.Key)) continue;
                PropQuality.RegisterLod(kv.Value.holder.gameObject, empty, kv.Value.renderers, nearDistance, farDistance);
            }
            return root;
        }

        static GameObject NewRoot(Transform parent, string name)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            return root;
        }

        public sealed class CellOut
        {
            public Transform holder;
            public readonly List<Renderer> renderers = new List<Renderer>();
        }

        /// Merges the geometry into one renderer per (cell, material) under root; returns the renderers per cell.
        public Dictionary<long, CellOut> FlushCells(Transform root, string name, ShadowCastingMode shadows, bool receiveShadows)
        {
            // builder geometry becomes temporary meshes
            foreach (var kv in builders)
            {
                var b = kv.Value;
                if (b.Empty) continue;
                var mesh = b.ToMesh(name + "_part");
                temps.Add(mesh);
                if (!groups.TryGetValue(kv.Key, out var list))
                {
                    list = new List<CombineInstance>();
                    groups[kv.Key] = list;
                }
                list.Add(new CombineInstance { mesh = mesh, transform = Matrix4x4.identity, subMeshIndex = 0 });
            }
            builders.Clear();

            // group keys by cell
            var cellMap = new Dictionary<long, List<Key>>();
            foreach (var k in groups.Keys)
            {
                long ck = CellKey(k.cx, k.cz);
                if (!cellMap.TryGetValue(ck, out var l)) { l = new List<Key>(); cellMap[ck] = l; }
                l.Add(k);
            }

            var result = new Dictionary<long, CellOut>();
            var palOn = new List<CombineInstance>();
            var palOff = new List<CombineInstance>();
            foreach (var cell in cellMap)
            {
                var co = new CellOut { holder = root };
                if (cellSize > 0f)
                {
                    var cgo = new GameObject("Cell");
                    cgo.transform.SetParent(root, false);
                    co.holder = cgo.transform;
                }
                palOn.Clear();
                palOff.Clear();
                foreach (var k in cell.Value)
                {
                    var list = groups[k];
                    if (list.Count == 0) continue;
                    var mat = mats[k.mat];
                    var mode = shadowOverride.TryGetValue(k.mat, out var sm) ? sm : shadows;
                    string label = Label(mat);
                    var combined = new Mesh { name = name + "_" + label, indexFormat = IndexFormat.UInt32 };
                    combined.CombineMeshes(list.ToArray(), true, true, false);
                    if (combined.vertexCount == 0) { UnityEngine.Object.Destroy(combined); continue; }

                    // flat matte colors go into one palette mesh per cell (one draw call for all of them)
                    if (PropPalette.Accepts(mat) && PropPalette.TryUV(mat.GetColor("_BaseColor"), out var uv))
                    {
                        var uvs = new Vector2[combined.vertexCount];
                        for (int i = 0; i < uvs.Length; i++) uvs[i] = uv;
                        combined.uv = uvs;
                        temps.Add(combined);
                        (mode == ShadowCastingMode.Off ? palOff : palOn).Add(new CombineInstance { mesh = combined, transform = Matrix4x4.identity });
                        continue;
                    }
                    co.renderers.Add(MakeRenderer(co.holder, combined, mat, label, mode, receiveShadows));
                }
                for (int b = 0; b < 2; b++)
                {
                    var list = b == 0 ? palOn : palOff;
                    if (list.Count == 0) continue;
                    var merged = new Mesh { name = name + "_Palette", indexFormat = IndexFormat.UInt32 };
                    merged.CombineMeshes(list.ToArray(), true, true, false);
                    co.renderers.Add(MakeRenderer(co.holder, merged, PropPalette.Material, b == 0 ? "Palette" : "PaletteNoShadow", b == 0 ? ShadowCastingMode.On : ShadowCastingMode.Off, receiveShadows));
                }
                result[cell.Key] = co;
            }
            PropPalette.Commit();

            groups.Clear();
            for (int i = 0; i < temps.Count; i++) UnityEngine.Object.Destroy(temps[i]);
            temps.Clear();
            return result;
        }

        static Renderer MakeRenderer(Transform parent, Mesh mesh, Material mat, string label, ShadowCastingMode mode, bool receiveShadows)
        {
            LowPoly.Sanitize(mesh);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            var go = new GameObject(label);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = mode;
            mr.receiveShadows = receiveShadows;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            return mr;
        }

        /// Readable name for a merged renderer: shared flat materials are all called "SB_Opaque", so use their color.
        static string Label(Material mat)
        {
            if (mat.name == "SB_Opaque" && mat.HasProperty("_BaseColor"))
                return "Flat_" + ColorUtility.ToHtmlStringRGB(mat.GetColor("_BaseColor"));
            return mat.name;
        }

        /// Single-level LODGroup that hides the renderers beyond roughly cullDistance (scaled by the quality tier).
        public static void AddCull(GameObject holder, List<Renderer> renderers, float cullDistance)
        {
            PropQuality.RegisterCull(holder, renderers, cullDistance);
        }
    }

    /// One point-filtered 32 x 32 palette texture shared by all merged props: every flat matte color gets a texel and
    /// the merged meshes point at it by UV, so a whole cell of differently colored low-poly parts is a single draw
    /// call (feedback #21: smooth performance). Lighting is identical to the flat materials (same URP Lit, same
    /// smoothness, sRGB texel = the material color).
    public static class PropPalette
    {
        const int Size = 32;
        static Texture2D tex;
        static Material mat;
        static readonly Dictionary<int, int> Index = new Dictionary<int, int>();
        static readonly Color32[] Pixels = new Color32[Size * Size];
        static int count;
        static bool dirty;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { tex = null; mat = null; Index.Clear(); count = 0; dirty = false; }

        static void Ensure()
        {
            if (tex != null && mat != null) return;
            for (int i = 0; i < Pixels.Length; i++) Pixels[i] = new Color32(255, 255, 255, 255);
            Index.Clear();
            count = 0;
            tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                name = "SB_PropPalette",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0
            };
            tex.SetPixels32(Pixels);
            tex.Apply(false, false);
            mat = MaterialLibrary.Textured(tex, Color.white, LowPoly.Smoothness, 0f);
            mat.name = "SB_PropPalette";
        }

        public static Material Material { get { Ensure(); return mat; } }

        /// Flat matte opaque material without texture or emission (LowPoly.Mat / MaterialLibrary.Opaque at 0.08).
        public static bool Accepts(Material m)
        {
            if (m == null || m.name != "SB_Opaque" || m.shader != MaterialLibrary.Lit) return false;
            if (m.IsKeywordEnabled("_EMISSION") || m.mainTexture != null) return false;
            if (m.HasProperty("_Smoothness") && Mathf.Abs(m.GetFloat("_Smoothness") - LowPoly.Smoothness) > 0.01f) return false;
            if (m.HasProperty("_Metallic") && m.GetFloat("_Metallic") > 0.01f) return false;
            return m.HasProperty("_BaseColor") && m.GetColor("_BaseColor").a > 0.99f;
        }

        /// Palette UV of a color (registers it); false when the palette is full.
        public static bool TryUV(Color c, out Vector2 uv)
        {
            Ensure();
            Color32 c32 = c;
            int key = (c32.r << 16) | (c32.g << 8) | c32.b;
            if (!Index.TryGetValue(key, out int i))
            {
                if (count >= Size * Size) { uv = default; return false; }
                i = count++;
                Index[key] = i;
                Pixels[i] = new Color32(c32.r, c32.g, c32.b, 255);
                dirty = true;
            }
            uv = new Vector2((i % Size + 0.5f) / Size, (i / Size + 0.5f) / Size);
            return true;
        }

        /// Uploads newly registered colors (call after a flush).
        public static void Commit()
        {
            if (!dirty || tex == null) return;
            tex.SetPixels32(Pixels);
            tex.Apply(false, false);
            dirty = false;
        }
    }

    /// One mesh + its material (a part of a reusable model variant).
    public struct PropMeshPart
    {
        public Mesh mesh;
        public Material mat;
        public PropMeshPart(Mesh mesh, Material mat) { this.mesh = mesh; this.mat = mat; }
    }

    /// Reusable model variant made of several single-material meshes (palms, corals, bushes...).
    public sealed class PropModel
    {
        public readonly List<PropMeshPart> parts = new List<PropMeshPart>();
        public float height = 1f;
        public float radius = 1f;

        public void Add(PropMeshBuilder b, Material mat, string name)
        {
            if (b.Empty) return;
            var mesh = b.ToMesh(name);
            LowPoly.Sanitize(mesh);
            if (mesh.vertexCount == 0) { UnityEngine.Object.Destroy(mesh); return; }
            parts.Add(new PropMeshPart(mesh, mat));
        }
    }

    /// Helper that builds a model from per-material builders.
    public sealed class PropModelBuilder
    {
        readonly Dictionary<Material, PropMeshBuilder> map = new Dictionary<Material, PropMeshBuilder>();
        readonly List<Material> order = new List<Material>();

        public PropMeshBuilder B(Material mat)
        {
            if (!map.TryGetValue(mat, out var b))
            {
                b = new PropMeshBuilder();
                map[mat] = b;
                order.Add(mat);
            }
            return b;
        }

        public PropModel Build(string name, float height, float radius)
        {
            var m = new PropModel { height = height, radius = radius };
            for (int i = 0; i < order.Count; i++) m.Add(map[order[i]], order[i], name + "_" + i);
            return m;
        }
    }
}
