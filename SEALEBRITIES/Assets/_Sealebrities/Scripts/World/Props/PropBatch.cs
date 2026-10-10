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

        /// Merges everything into renderers under parent. Returns the batch root.
        public GameObject Flush(Transform parent, string name, ShadowCastingMode shadows = ShadowCastingMode.On, float cullDistance = 0f, bool receiveShadows = true)
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

            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;

            // group keys by cell
            var cellMap = new Dictionary<long, List<Key>>();
            foreach (var k in groups.Keys)
            {
                long ck = ((long)k.cx << 32) ^ (uint)k.cz;
                if (!cellMap.TryGetValue(ck, out var l)) { l = new List<Key>(); cellMap[ck] = l; }
                l.Add(k);
            }

            var renderers = new List<Renderer>();
            foreach (var cell in cellMap)
            {
                Transform cellT = root.transform;
                if (cellSize > 0f)
                {
                    var cgo = new GameObject("Cell");
                    cgo.transform.SetParent(root.transform, false);
                    cellT = cgo.transform;
                }
                renderers.Clear();
                foreach (var k in cell.Value)
                {
                    var list = groups[k];
                    if (list.Count == 0) continue;
                    var mat = mats[k.mat];
                    string label = Label(mat);
                    var combined = new Mesh { name = name + "_" + label, indexFormat = IndexFormat.UInt32 };
                    combined.CombineMeshes(list.ToArray(), true, true, false);
                    if (combined.vertexCount == 0) { UnityEngine.Object.Destroy(combined); continue; }
                    LowPoly.Sanitize(combined);
                    combined.RecalculateBounds();
                    combined.UploadMeshData(true);

                    var go = new GameObject(label);
                    go.transform.SetParent(cellT, false);
                    go.AddComponent<MeshFilter>().sharedMesh = combined;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = mat;
                    mr.shadowCastingMode = shadowOverride.TryGetValue(k.mat, out var sm) ? sm : shadows;
                    mr.receiveShadows = receiveShadows;
                    mr.lightProbeUsage = LightProbeUsage.Off;
                    mr.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
                    renderers.Add(mr);
                }
                if (cullDistance > 0f && renderers.Count > 0) AddCull(cellT.gameObject, renderers, cullDistance);
            }

            groups.Clear();
            for (int i = 0; i < temps.Count; i++) UnityEngine.Object.Destroy(temps[i]);
            temps.Clear();
            return root;
        }

        /// Readable name for a merged renderer: shared flat materials are all called "SB_Opaque", so use their color.
        static string Label(Material mat)
        {
            if (mat.name == "SB_Opaque" && mat.HasProperty("_BaseColor"))
                return "Flat_" + ColorUtility.ToHtmlStringRGB(mat.GetColor("_BaseColor"));
            return mat.name;
        }

        /// Single-level LODGroup that hides the renderers beyond roughly cullDistance (60° FOV assumed).
        public static void AddCull(GameObject holder, List<Renderer> renderers, float cullDistance)
        {
            var lod = holder.GetComponent<LODGroup>();
            if (lod == null) lod = holder.AddComponent<LODGroup>();
            var arr = renderers.ToArray();
            lod.SetLODs(new[] { new LOD(0.01f, arr) });
            lod.RecalculateBounds();
            float size = Mathf.Max(1f, lod.size);
            float h = size / (cullDistance * 2f * Mathf.Tan(30f * Mathf.Deg2Rad));
            lod.SetLODs(new[] { new LOD(Mathf.Clamp(h, 0.0005f, 0.9f), arr) });
            lod.fadeMode = LODFadeMode.None;
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
