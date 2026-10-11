using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Procedural low-poly meshes for the hand camera models (phone in a bag, action cam, camcorder, pro housing,
    /// cinema rig) and the gloved mitten hand. Art bible §1.13: held items are low-poly SILHOUETTES with SMOOTH normals
    /// (soft like How to Fish) – few sides, chunky single-chamfer boxes, but adjacent faces closer than
    /// SmoothCreaseDegrees share an averaged normal; sharper edges (box faces, cylinder caps) stay crisp.
    /// Primitive meshes are cached and shared between rebuilds; combined per-rig meshes are owned (and destroyed) by
    /// the rig. Surface colors come from FilmMaterials (flat matte palette colors).
    public static class FilmMesh
    {
        /// Faces meeting at a smaller angle than this are shaded smoothly (6-sided cylinders still read round).
        public const float SmoothCreaseDegrees = 62f;

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

        // ------------------------------------------------------------------ triangle soup builder
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
                // size of the millimeter chamfer-corner triangles here -> zero normals -> NaN pixels -> bloom blobs
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

            /// Triangle with explicit per-corner normals (rounded box edges); winding follows 'outward'.
            public void TriN(Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc, Vector3 outward)
            {
                Vector3 f = Vector3.Cross(b - a, c - a);
                float sq = f.sqrMagnitude;
                if (!(sq >= 1e-18f)) return;
                if (Vector3.Dot(f, outward) < 0f)
                {
                    var tmp = b; b = c; c = tmp;
                    var tn = nb; nb = nc; nc = tn;
                }
                Vector3 nn = f / Mathf.Sqrt(sq);
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(c);
                n.Add(na); n.Add(nb); n.Add(nc);
                uv.Add(Planar(a, nn)); uv.Add(Planar(b, nn)); uv.Add(Planar(c, nn));
                t.Add(i); t.Add(i + 1); t.Add(i + 2);
            }

            public void QuadN(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 na, Vector3 nb, Vector3 nc, Vector3 nd, Vector3 outward)
            {
                TriN(a, b, c, na, nb, nc, outward);
                TriN(a, c, d, na, nc, nd, outward);
            }

            static Vector2 Planar(Vector3 p, Vector3 nn)
            {
                Vector3 an = new Vector3(Mathf.Abs(nn.x), Mathf.Abs(nn.y), Mathf.Abs(nn.z));
                Vector2 q = an.x >= an.y && an.x >= an.z ? new Vector2(p.z, p.y) : an.y >= an.z ? new Vector2(p.x, p.z) : new Vector2(p.x, p.y);
                return q * 20f;
            }

            /// smooth = true averages the normals of coincident corners whose faces meet below the crease angle
            /// (corner-angle weighted, so thin chamfer strips blend as much as the big faces next to them).
            public Mesh ToMesh(bool smooth)
            {
                var m = new Mesh();
                m.SetVertices(v);
                m.SetNormals(smooth ? SmoothNormals() : n);
                m.SetUVs(0, uv);
                m.SetTriangles(t, 0);
                return m;
            }

            List<Vector3> SmoothNormals()
            {
                int count = v.Count;
                var angle = new float[count];
                for (int i = 0; i + 2 < count; i += 3)
                {
                    angle[i] = Corner(v[i], v[i + 1], v[i + 2]);
                    angle[i + 1] = Corner(v[i + 1], v[i + 2], v[i]);
                    angle[i + 2] = Corner(v[i + 2], v[i], v[i + 1]);
                }

                var groups = new Dictionary<Vector3Int, List<int>>(count / 2 + 1);
                for (int i = 0; i < count; i++)
                {
                    var key = Key(v[i]);
                    if (!groups.TryGetValue(key, out var list)) { list = new List<int>(6); groups[key] = list; }
                    list.Add(i);
                }

                float cosCrease = Mathf.Cos(SmoothCreaseDegrees * Mathf.Deg2Rad);
                var result = new List<Vector3>(count);
                for (int i = 0; i < count; i++)
                {
                    Vector3 face = n[i];
                    Vector3 sum = Vector3.zero;
                    var list = groups[Key(v[i])];
                    for (int k = 0; k < list.Count; k++)
                    {
                        int j = list[k];
                        if (Vector3.Dot(n[j], face) >= cosCrease) sum += n[j] * angle[j];
                    }
                    float sq = sum.sqrMagnitude;
                    result.Add(sq > 1e-20f ? sum / Mathf.Sqrt(sq) : face);
                }
                return result;
            }

            static Vector3Int Key(Vector3 p) =>
                new Vector3Int(Mathf.RoundToInt(p.x * 10000f), Mathf.RoundToInt(p.y * 10000f), Mathf.RoundToInt(p.z * 10000f));

            static float Corner(Vector3 at, Vector3 b, Vector3 c)
            {
                Vector3 e1 = b - at, e2 = c - at;
                float l = Mathf.Sqrt(e1.sqrMagnitude * e2.sqrMagnitude);
                if (!(l > 1e-20f)) return 0f;
                return Mathf.Acos(Mathf.Clamp(Vector3.Dot(e1, e2) / l, -1f, 1f));
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
        /// Box centered at the origin. 'radius' becomes one chamfer on every edge (radius 0 = sharp box); with the
        /// smooth normals the chamfers read as soft rounded edges. 'seg' is ignored (kept for compatibility).
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
                return Store(key, f.ToMesh(false));
            }

            // Every vertex P(…, axis) lies on the face whose normal points along 'axis' and takes exactly that normal:
            // the big faces stay perfectly flat while each chamfer strip blends from one face normal to the next
            // (a soft rounded edge, no pillowing of large faces like screens or phone backs).
            Vector3 inner = h - Vector3.one * b;
            Vector3 P(int sx, int sy, int sz, int axis)
            {
                Vector3 p = new Vector3(sx * inner.x, sy * inner.y, sz * inner.z);
                if (axis == 0) p.x += sx * b; else if (axis == 1) p.y += sy * b; else p.z += sz * b;
                return p;
            }
            Vector3 N(int sx, int sy, int sz, int axis) => axis == 0 ? new Vector3(sx, 0f, 0f) : axis == 1 ? new Vector3(0f, sy, 0f) : new Vector3(0f, 0f, sz);
            void Q(int ax, int ay, int az, int aa, int bx, int by, int bz, int ba, int cx, int cy, int cz, int ca, int dx, int dy, int dz, int da, Vector3 outward)
                => f.QuadN(P(ax, ay, az, aa), P(bx, by, bz, ba), P(cx, cy, cz, ca), P(dx, dy, dz, da),
                           N(ax, ay, az, aa), N(bx, by, bz, ba), N(cx, cy, cz, ca), N(dx, dy, dz, da), outward);
            int[] s = { -1, 1 };
            foreach (int sg in s)
            {
                Q(sg, -1, -1, 0, sg, 1, -1, 0, sg, 1, 1, 0, sg, -1, 1, 0, new Vector3(sg, 0f, 0f));
                Q(-1, sg, -1, 1, 1, sg, -1, 1, 1, sg, 1, 1, -1, sg, 1, 1, new Vector3(0f, sg, 0f));
                Q(-1, -1, sg, 2, 1, -1, sg, 2, 1, 1, sg, 2, -1, 1, sg, 2, new Vector3(0f, 0f, sg));
            }
            foreach (int a in s)
                foreach (int c in s)
                {
                    Q(a, c, -1, 0, a, c, -1, 1, a, c, 1, 1, a, c, 1, 0, new Vector3(a, c, 0f));
                    Q(a, -1, c, 0, a, -1, c, 2, a, 1, c, 2, a, 1, c, 0, new Vector3(a, 0f, c));
                    Q(-1, a, c, 1, -1, a, c, 2, 1, a, c, 2, 1, a, c, 1, new Vector3(0f, a, c));
                }
            foreach (int x in s)
                foreach (int y in s)
                    foreach (int z in s)
                        f.TriN(P(x, y, z, 0), P(x, y, z, 1), P(x, y, z, 2), N(x, y, z, 0), N(x, y, z, 1), N(x, y, z, 2), new Vector3(x, y, z));
            return Store(key, f.ToMesh(false));
        }

        static Vector3 Axis(int i) => i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;

        public static Mesh Box(Vector3 size) => RoundedBox(size, 0f, 0);

        // ------------------------------------------------------------------ prism / frustum
        /// Low-poly frustum along +Y, centered at the origin (smooth sides, hard caps; a flat face points toward +X).
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
                float a = (i % segments) / (float)segments * Mathf.PI * 2f + offset;
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
            return Store(key, f.ToMesh(true));
        }

        public static Mesh Cylinder(float radius, float height, int segments = 20) => Frustum(radius, radius, height, segments, true, false);

        // ------------------------------------------------------------------ ball / dome
        /// Low-poly ball (or the +Y dome when 'hemisphere' is true) centered at the origin, smooth shaded.
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
                float ph = (j % lon) / (float)lon * Mathf.PI * 2f;
                return new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph)) * radius;
            }
            for (int i = 0; i < lat; i++)
                for (int j = 0; j < lon; j++)
                {
                    Vector3 a = Pt(i, j), b = Pt(i, j + 1), c = Pt(i + 1, j + 1), d = Pt(i + 1, j);
                    f.Quad(a, b, c, d, (a + b + c + d) * 0.25f);
                }
            return Store(key, f.ToMesh(true));
        }

        // ------------------------------------------------------------------ ellipsoid (soft blobs: mitten, thumb)
        /// Smooth low-poly ellipsoid centered at the origin with the given semi-axes (exact analytic normals).
        public static Mesh Ellipsoid(Vector3 radii, int lon = 10, int lat = 6)
        {
            lon = Mathf.Clamp(lon, 6, 16);
            lat = Mathf.Clamp(lat, 4, 10);
            string key = $"el{radii.x:F4}_{radii.y:F4}_{radii.z:F4}_{lon}_{lat}";
            if (TryGet(key, out var cached)) return cached;

            var verts = new List<Vector3>((lat + 1) * (lon + 1));
            var norms = new List<Vector3>(verts.Capacity);
            var uvs = new List<Vector2>(verts.Capacity);
            var tris = new List<int>(lat * lon * 6);
            float ax = Mathf.Max(1e-4f, radii.x), ay = Mathf.Max(1e-4f, radii.y), az = Mathf.Max(1e-4f, radii.z);
            for (int i = 0; i <= lat; i++)
            {
                float th = i / (float)lat * Mathf.PI;
                float sy = Mathf.Cos(th), sr = Mathf.Sin(th);
                for (int j = 0; j <= lon; j++)
                {
                    float ph = (j % lon) / (float)lon * Mathf.PI * 2f;
                    Vector3 u = new Vector3(sr * Mathf.Cos(ph), sy, sr * Mathf.Sin(ph));
                    verts.Add(new Vector3(u.x * ax, u.y * ay, u.z * az));
                    Vector3 nn = new Vector3(u.x / ax, u.y / ay, u.z / az);
                    float l = nn.magnitude;
                    norms.Add(l > 1e-8f ? nn / l : Vector3.up);
                    uvs.Add(new Vector2(j / (float)lon, i / (float)lat));
                }
            }
            int row = lon + 1;
            for (int i = 0; i < lat; i++)
                for (int j = 0; j < lon; j++)
                {
                    int a = i * row + j, b = a + 1, c = a + row, d = c + 1;
                    // outward winding (clockwise seen from outside)
                    if (i > 0) { tris.Add(a); tris.Add(b); tris.Add(c); }
                    if (i < lat - 1) { tris.Add(b); tris.Add(d); tris.Add(c); }
                }
            var m = new Mesh();
            m.SetVertices(verts);
            m.SetNormals(norms);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            return Store(key, m);
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

        // ------------------------------------------------------------------ bounds
        /// Bounds of every mesh below 'node', expressed in the local space of 'space' (build-time helper).
        public static Bounds LocalBounds(Transform node, Transform space)
        {
            bool any = false;
            var result = new Bounds(Vector3.zero, Vector3.zero);
            var filters = node.GetComponentsInChildren<MeshFilter>(true);
            Matrix4x4 toSpace = space.worldToLocalMatrix;
            for (int i = 0; i < filters.Length; i++)
            {
                var mf = filters[i];
                if (mf == null || mf.sharedMesh == null) continue;
                Bounds b = mf.sharedMesh.bounds;
                Matrix4x4 m = toSpace * mf.transform.localToWorldMatrix;
                Vector3 c = b.center, e = b.extents;
                for (int k = 0; k < 8; k++)
                {
                    var p = m.MultiplyPoint3x4(new Vector3(
                        c.x + ((k & 1) == 0 ? -e.x : e.x),
                        c.y + ((k & 2) == 0 ? -e.y : e.y),
                        c.z + ((k & 4) == 0 ? -e.z : e.z)));
                    if (!any) { result = new Bounds(p, Vector3.zero); any = true; }
                    else result.Encapsulate(p);
                }
            }
            return result;
        }

        // ------------------------------------------------------------------ combine
        /// Merges every MeshFilter below 'source' into one mesh per material, parented to 'target'. Destroys 'source'.
        public static List<Mesh> CombineStatic(Transform source, Transform target) => CombineStaticMany(target, source);

        /// Merges every MeshFilter below the given sources into one mesh per material (all sources together), parented
        /// to 'target' (any transform hierarchy and scale is baked in). Destroys the sources. Returns the created meshes.
        public static List<Mesh> CombineStaticMany(Transform target, params Transform[] sources)
        {
            var created = new List<Mesh>();
            var groups = new Dictionary<Material, List<CombineInstance>>();
            var order = new List<Material>();
            Matrix4x4 toTarget = target.worldToLocalMatrix;
            for (int s = 0; s < sources.Length; s++)
            {
                var source = sources[s];
                if (source == null) continue;
                var filters = source.GetComponentsInChildren<MeshFilter>(true);
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

            for (int s = 0; s < sources.Length; s++)
            {
                var source = sources[s];
                if (source == null) continue;
                source.gameObject.SetActive(false);
                source.SetParent(null, false);
                Object.Destroy(source.gameObject);
            }
            return created;
        }
    }
}
