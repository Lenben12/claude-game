using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Procedural low-poly meshes for the hand camera models (phone in a bag, action cam, camcorder, pro housing,
    /// cinema rig). Every primitive is chunky and faceted (flat normals, few sides, single-chamfer boxes) to match the
    /// game's low-poly toy look. Primitive meshes are cached and shared between rebuilds; combined per-rig meshes are
    /// owned (and destroyed) by the rig. Surface colors come from FilmMaterials (flat matte palette colors).
    public static class FilmMesh
    {
        static readonly Dictionary<string, Mesh> Cache = new Dictionary<string, Mesh>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Cache.Clear(); }

        static bool TryGet(string key, out Mesh m) => Cache.TryGetValue(key, out m) && m != null;

        static Mesh Store(string key, Mesh m)
        {
            m.name = "Film_" + key;
            m.RecalculateBounds();
            Cache[key] = m;
            return m;
        }

        // ------------------------------------------------------------------ faceted builder
        sealed class Flat
        {
            public readonly List<Vector3> v = new List<Vector3>(64);
            public readonly List<Vector3> n = new List<Vector3>(64);
            public readonly List<Vector2> uv = new List<Vector2>(64);
            public readonly List<int> t = new List<int>(64);

            /// Adds a triangle facing 'outward' (direction hint); degenerate triangles are dropped.
            public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
            {
                Vector3 f = Vector3.Cross(b - a, c - a);
                float sq = f.sqrMagnitude;
                if (!(sq >= 1e-18f)) return;   // degenerate (or NaN)
                if (Vector3.Dot(f, outward) < 0f) { var tmp = b; b = c; c = tmp; f = -f; }
                // manual normalization: Vector3.normalized returns ZERO below a magnitude of 1e-5, which is exactly the
                // size of the millimeter chamfer-corner triangles here -> zero normals -> NaN pixels -> a huge bloom
                // blob over the camera model
                Vector3 nn = f / Mathf.Sqrt(sq);
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(c);
                n.Add(nn); n.Add(nn); n.Add(nn);
                uv.Add(Planar(a, nn)); uv.Add(Planar(b, nn)); uv.Add(Planar(c, nn));
                t.Add(i); t.Add(i + 1); t.Add(i + 2);
            }

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
            {
                Tri(a, b, c, outward);
                Tri(a, c, d, outward);
            }

            static Vector2 Planar(Vector3 p, Vector3 nn)
            {
                Vector3 an = new Vector3(Mathf.Abs(nn.x), Mathf.Abs(nn.y), Mathf.Abs(nn.z));
                Vector2 q = an.x >= an.y && an.x >= an.z ? new Vector2(p.z, p.y) : an.y >= an.z ? new Vector2(p.x, p.z) : new Vector2(p.x, p.y);
                return q * 20f;
            }

            public Mesh ToMesh()
            {
                var m = new Mesh();
                m.SetVertices(v);
                m.SetNormals(n);
                m.SetUVs(0, uv);
                m.SetTriangles(t, 0);
                return m;
            }
        }

        /// Few sides for small round parts, a few more for big ones (keeps the silhouette chunky but readable).
        static int LowSeg(int requested, float radius)
        {
            if (requested <= 5) return Mathf.Max(3, requested);
            int s = radius < 0.01f ? 6 : radius < 0.05f ? 8 : 10;
            return Mathf.Min(requested, s);
        }

        // ------------------------------------------------------------------ chamfered box
        /// Box centered at the origin. 'radius' becomes one flat chamfer on every edge (radius 0 = sharp box);
        /// 'seg' is ignored (kept for compatibility).
        public static Mesh RoundedBox(Vector3 size, float radius, int seg = 3)
        {
            float maxR = Mathf.Min(size.x, Mathf.Min(size.y, size.z)) * 0.5f;
            float b = Mathf.Clamp(radius * 0.9f, 0f, maxR * 0.9f);
            if (b < 0.0002f) b = 0f;
            string key = $"cb{size.x:F4}_{size.y:F4}_{size.z:F4}_{b:F4}";
            if (TryGet(key, out var cached)) return cached;

            Vector3 h = size * 0.5f;
            var f = new Flat();
            if (b <= 0f)
            {
                for (int axis = 0; axis < 3; axis++)
                    for (int sg = -1; sg <= 1; sg += 2)
                    {
                        Vector3 nrm = Axis(axis) * sg;
                        Vector3 u = Axis((axis + 1) % 3), w = Axis((axis + 2) % 3);
                        Vector3 c = Vector3.Scale(nrm, h);
                        Vector3 U = Vector3.Scale(u, h), W = Vector3.Scale(w, h);
                        f.Quad(c - U - W, c + U - W, c + U + W, c - U + W, nrm);
                    }
                return Store(key, f.ToMesh());
            }

            Vector3 inner = h - Vector3.one * b;
            Vector3 P(int sx, int sy, int sz, int axis)
            {
                Vector3 p = new Vector3(sx * inner.x, sy * inner.y, sz * inner.z);
                if (axis == 0) p.x += sx * b; else if (axis == 1) p.y += sy * b; else p.z += sz * b;
                return p;
            }
            int[] s = { -1, 1 };
            foreach (int sg in s)
            {
                f.Quad(P(sg, -1, -1, 0), P(sg, 1, -1, 0), P(sg, 1, 1, 0), P(sg, -1, 1, 0), new Vector3(sg, 0f, 0f));
                f.Quad(P(-1, sg, -1, 1), P(1, sg, -1, 1), P(1, sg, 1, 1), P(-1, sg, 1, 1), new Vector3(0f, sg, 0f));
                f.Quad(P(-1, -1, sg, 2), P(1, -1, sg, 2), P(1, 1, sg, 2), P(-1, 1, sg, 2), new Vector3(0f, 0f, sg));
            }
            foreach (int a in s)
                foreach (int c in s)
                {
                    f.Quad(P(a, c, -1, 0), P(a, c, -1, 1), P(a, c, 1, 1), P(a, c, 1, 0), new Vector3(a, c, 0f));
                    f.Quad(P(a, -1, c, 0), P(a, -1, c, 2), P(a, 1, c, 2), P(a, 1, c, 0), new Vector3(a, 0f, c));
                    f.Quad(P(-1, a, c, 1), P(-1, a, c, 2), P(1, a, c, 2), P(1, a, c, 1), new Vector3(0f, a, c));
                }
            foreach (int x in s)
                foreach (int y in s)
                    foreach (int z in s)
                        f.Tri(P(x, y, z, 0), P(x, y, z, 1), P(x, y, z, 2), new Vector3(x, y, z));
            return Store(key, f.ToMesh());
        }

        static Vector3 Axis(int i) => i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;

        public static Mesh Box(Vector3 size) => RoundedBox(size, 0f, 0);

        // ------------------------------------------------------------------ prism / frustum
        /// Faceted frustum along +Y, centered at the origin (always flat-sided; a flat face points toward +X).
        /// Segment counts above the low-poly budget are reduced. 'hard' is kept for compatibility.
        public static Mesh Frustum(float rBottom, float rTop, float height, int segments = 20, bool caps = true, bool hard = false)
        {
            segments = LowSeg(segments, Mathf.Max(rBottom, rTop));
            string key = $"fr{rBottom:F4}_{rTop:F4}_{height:F4}_{segments}_{(caps ? 1 : 0)}";
            if (TryGet(key, out var cached)) return cached;

            var f = new Flat();
            float hy = height * 0.5f;
            float offset = Mathf.PI / segments;
            Vector3 Ring(float r, float y, int i)
            {
                float a = i / (float)segments * Mathf.PI * 2f + offset;
                return new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
            }
            for (int i = 0; i < segments; i++)
            {
                Vector3 b0 = Ring(rBottom, -hy, i), b1 = Ring(rBottom, -hy, i + 1);
                Vector3 t0 = Ring(rTop, hy, i), t1 = Ring(rTop, hy, i + 1);
                Vector3 mid = (b0 + b1 + t0 + t1) * 0.25f;
                f.Quad(b0, t0, t1, b1, new Vector3(mid.x, 0f, mid.z));
                if (caps)
                {
                    if (rTop > 0.0001f) f.Tri(new Vector3(0f, hy, 0f), t0, t1, Vector3.up);
                    if (rBottom > 0.0001f) f.Tri(new Vector3(0f, -hy, 0f), b0, b1, Vector3.down);
                }
            }
            return Store(key, f.ToMesh());
        }

        public static Mesh Cylinder(float radius, float height, int segments = 20) => Frustum(radius, radius, height, segments, true, false);

        // ------------------------------------------------------------------ ball / dome
        /// Faceted ball (or the +Y dome when 'hemisphere' is true) centered at the origin.
        public static Mesh Sphere(float radius, int segments = 16, bool hemisphere = false)
        {
            int lon = LowSeg(Mathf.Max(6, segments), radius);
            int lat = hemisphere ? (lon >= 10 ? 3 : 2) : (lon >= 10 ? 5 : 4);
            string key = $"sp{radius:F4}_{lon}_{(hemisphere ? 1 : 0)}";
            if (TryGet(key, out var cached)) return cached;

            float latMax = hemisphere ? Mathf.PI * 0.5f : Mathf.PI;
            var f = new Flat();
            Vector3 Pt(int i, int j)
            {
                float th = i / (float)lat * latMax;
                float ph = j / (float)lon * Mathf.PI * 2f;
                return new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph)) * radius;
            }
            for (int i = 0; i < lat; i++)
                for (int j = 0; j < lon; j++)
                {
                    Vector3 a = Pt(i, j), b = Pt(i, j + 1), c = Pt(i + 1, j + 1), d = Pt(i + 1, j);
                    f.Quad(a, b, c, d, (a + b + c + d) * 0.25f);
                }
            return Store(key, f.ToMesh());
        }

        // ------------------------------------------------------------------ quad
        /// Flat quad in the XY plane facing -Z (toward a viewer behind the device). UV (0,0) bottom-left as seen by the viewer.
        public static Mesh Quad(float width, float height)
        {
            string key = $"qd{width:F4}_{height:F4}";
            if (TryGet(key, out var cached)) return cached;
            float w = width * 0.5f, h = height * 0.5f;
            var m = new Mesh();
            m.vertices = new[] { new Vector3(-w, -h, 0f), new Vector3(-w, h, 0f), new Vector3(w, h, 0f), new Vector3(w, -h, 0f) };
            m.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            m.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            return Store(key, m);
        }

        // ------------------------------------------------------------------ combine
        /// Merges every MeshFilter below 'source' into one mesh per material, parented to 'target' (which must share
        /// the coordinate frame the parts were built in). Destroys 'source'. Returns the created meshes.
        /// The camera surfaces are already flat matte palette colors (FilmMaterials.Surface), so no restyle is needed.
        public static List<Mesh> CombineStatic(Transform source, Transform target)
        {
            var created = new List<Mesh>();
            var filters = source.GetComponentsInChildren<MeshFilter>(true);
            var groups = new Dictionary<Material, List<CombineInstance>>();
            var order = new List<Material>();
            Matrix4x4 toTarget = target.worldToLocalMatrix;
            foreach (var mf in filters)
            {
                var mr = mf.GetComponent<MeshRenderer>();
                if (mr == null || mf.sharedMesh == null || mr.sharedMaterial == null) continue;
                var mat = mr.sharedMaterial;
                if (!groups.TryGetValue(mat, out var list))
                {
                    list = new List<CombineInstance>();
                    groups[mat] = list;
                    order.Add(mat);
                }
                list.Add(new CombineInstance { mesh = mf.sharedMesh, subMeshIndex = 0, transform = toTarget * mf.transform.localToWorldMatrix });
            }

            foreach (var mat in order)
            {
                var list = groups[mat];
                int vcount = 0;
                foreach (var ci in list) vcount += ci.mesh.vertexCount;
                var mesh = new Mesh { name = "Film_Combined_" + mat.name };
                if (vcount > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.CombineMeshes(list.ToArray(), true, true);
                mesh.RecalculateBounds();
                created.Add(mesh);

                var go = new GameObject("Combined_" + mat.name);
                go.transform.SetParent(target, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                FilmRigBuilder.ConfigureRenderer(r);
            }

            source.gameObject.SetActive(false);
            source.SetParent(null, false);
            Object.Destroy(source.gameObject);
            return created;
        }
    }
}
