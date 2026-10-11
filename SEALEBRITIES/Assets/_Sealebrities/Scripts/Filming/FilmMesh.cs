using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Procedural meshes for the hand camera models (phone in a bag, action cam, camcorder, pro housing, cinema rig),
    /// the gloved mitten and the golden camera trophy.
    ///
    /// L.13 "held items much smoother" (supersedes the single-chamfer look of round 2a): boxes get real rounded edges
    /// (1–3 segments per quarter round depending on the bevel radius, exact normals), cylinders get bevelled rims and
    /// up to 32 sides (24 for lenses and knobs), spheres and ellipsoids are finer. Big flat faces stay perfectly flat;
    /// only where faces meet at > 50° without a bevel (cylinder caps) the edge is hard (split normals).
    /// Primitive meshes are cached and shared between rebuilds; combined per-rig meshes are owned (and destroyed) by
    /// the rig. Surface colours come from FilmMaterials (flat matte palette colours).
    public static class FilmMesh
    {
        /// Legacy: faces meeting at a smaller angle than this are shaded smoothly (Frustum sides).
        public const float SmoothCreaseDegrees = 50f;

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
            public readonly List<Vector3> v = new List<Vector3>(256);
            public readonly List<Vector3> n = new List<Vector3>(256);
            public readonly List<Vector2> uv = new List<Vector2>(256);
            public readonly List<int> t = new List<int>(256);

            /// Adds a triangle facing 'outward' (direction hint) with its face normal; degenerate triangles are dropped.
            public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
            {
                Vector3 f = Vector3.Cross(b - a, c - a);
                float sq = f.sqrMagnitude;
                if (!(sq >= 1e-20f)) return;   // degenerate (or NaN)
                if (Vector3.Dot(f, outward) < 0f) { var tmp = b; b = c; c = tmp; f = -f; }
                // manual normalization: Vector3.normalized returns ZERO below a magnitude of 1e-5, which is exactly the
                // size of millimeter bevel triangles here -> zero normals -> NaN pixels -> bloom blobs
                Vector3 nn = f / Mathf.Sqrt(sq);
                Add(a, b, c, nn, nn, nn, nn);
            }

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
            {
                Tri(a, b, c, outward);
                Tri(a, c, d, outward);
            }

            /// Triangle with explicit per-corner normals; winding follows 'outward'.
            public void TriN(Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc, Vector3 outward)
            {
                Vector3 f = Vector3.Cross(b - a, c - a);
                float sq = f.sqrMagnitude;
                if (!(sq >= 1e-20f)) return;
                if (Vector3.Dot(f, outward) < 0f)
                {
                    var tmp = b; b = c; c = tmp;
                    var tn = nb; nb = nc; nc = tn;
                }
                Add(a, b, c, Safe(na), Safe(nb), Safe(nc), f / Mathf.Sqrt(sq));
            }

            public void QuadN(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 na, Vector3 nb, Vector3 nc, Vector3 nd, Vector3 outward)
            {
                TriN(a, b, c, na, nb, nc, outward);
                TriN(a, c, d, na, nc, nd, outward);
            }

            void Add(Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc, Vector3 face)
            {
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(c);
                n.Add(na); n.Add(nb); n.Add(nc);
                uv.Add(Planar(a, face)); uv.Add(Planar(b, face)); uv.Add(Planar(c, face));
                t.Add(i); t.Add(i + 1); t.Add(i + 2);
            }

            static Vector3 Safe(Vector3 nrm)
            {
                float sq = nrm.sqrMagnitude;
                return sq > 1e-20f && !float.IsNaN(sq) ? nrm / Mathf.Sqrt(sq) : Vector3.up;
            }

            static Vector2 Planar(Vector3 p, Vector3 nn)
            {
                Vector3 an = new Vector3(Mathf.Abs(nn.x), Mathf.Abs(nn.y), Mathf.Abs(nn.z));
                Vector2 q = an.x >= an.y && an.x >= an.z ? new Vector2(p.z, p.y) : an.y >= an.z ? new Vector2(p.x, p.z) : new Vector2(p.x, p.y);
                return q * 20f;
            }

            /// smooth = true averages the normals of coincident corners whose faces meet below the crease angle
            /// (corner-angle weighted). Primitives with explicit normals use smooth = false.
            public Mesh ToMesh(bool smooth)
            {
                var m = new Mesh();
                if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
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
                new Vector3Int(Mathf.RoundToInt(p.x * 100000f), Mathf.RoundToInt(p.y * 100000f), Mathf.RoundToInt(p.z * 100000f));

            static float Corner(Vector3 at, Vector3 b, Vector3 c)
            {
                Vector3 e1 = b - at, e2 = c - at;
                float l = Mathf.Sqrt(e1.sqrMagnitude * e2.sqrMagnitude);
                if (!(l > 1e-20f)) return 0f;
                return Mathf.Acos(Mathf.Clamp(Vector3.Dot(e1, e2) / l, -1f, 1f));
            }
        }

        static Vector3 Axis(int i) => i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;

        static Vector3 Norm(Vector3 d)
        {
            float sq = d.sqrMagnitude;
            return sq > 1e-20f ? d / Mathf.Sqrt(sq) : Vector3.up;
        }

        // ------------------------------------------------------------------ rounded box
        /// Bevel segments per quarter round for a bevel radius (model space, before the rig's 0.53× scale):
        /// ≥ 8 mm → 3, ≥ 3 mm → 2, smaller → 1 (a single blended chamfer is enough below ~2 px on screen).
        public static int AutoBevelSegments(float radius) => radius >= 0.008f ? 3 : radius >= 0.003f ? 2 : 1;

        /// Box centred at the origin with rounded edges and corners of 'radius' (0 = sharp box). seg ≤ 0 picks the
        /// segment count from the radius (AutoBevelSegments). Big faces stay perfectly flat; every bevel vertex carries
        /// the exact normal of the rounded surface. Triangles: 12 + 24·seg + 8·seg² (seg 2 → 92, seg 3 → 156).
        public static Mesh RoundedBox(Vector3 size, float radius, int seg = -1)
        {
            float maxR = Mathf.Min(size.x, Mathf.Min(size.y, size.z)) * 0.5f;
            float r = Mathf.Clamp(radius, 0f, maxR * 0.95f);
            if (r < 0.0002f) r = 0f;
            int s = r <= 0f ? 0 : Mathf.Clamp(seg > 0 ? seg : AutoBevelSegments(r), 1, 4);
            string key = $"rb{size.x:F4}_{size.y:F4}_{size.z:F4}_{r:F4}_{s}";
            if (TryGet(key, out var cached)) return cached;

            Vector3 h = size * 0.5f;
            var f = new Flat();
            if (r <= 0f)
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

            Vector3 hi = h - Vector3.one * r;   // inner box
            // flat faces
            for (int axis = 0; axis < 3; axis++)
            {
                int a1 = (axis + 1) % 3, a2 = (axis + 2) % 3;
                for (int sg = -1; sg <= 1; sg += 2)
                {
                    Vector3 nrm = Axis(axis) * sg;
                    Vector3 c = nrm * (hi[axis] + r);
                    Vector3 U = Axis(a1) * hi[a1], W = Axis(a2) * hi[a2];
                    f.QuadN(c - U - W, c + U - W, c + U + W, c - U + W, nrm, nrm, nrm, nrm, nrm);
                }
            }
            // edge strips: along axis c between faces a and b (directions normalize((s-m)·A + m·B) so they meet the
            // corner patches exactly)
            for (int along = 0; along < 3; along++)
            {
                int a = (along + 1) % 3, b = (along + 2) % 3;
                Vector3 A = Axis(a), B = Axis(b), C = Axis(along);
                for (int sa = -1; sa <= 1; sa += 2)
                    for (int sb = -1; sb <= 1; sb += 2)
                    {
                        Vector3 corner = A * (sa * hi[a]) + B * (sb * hi[b]);
                        Vector3 c0 = corner - C * hi[along], c1 = corner + C * hi[along];
                        for (int m = 0; m < s; m++)
                        {
                            Vector3 d0 = Norm(A * (sa * (s - m)) + B * (sb * m));
                            Vector3 d1 = Norm(A * (sa * (s - m - 1)) + B * (sb * (m + 1)));
                            Vector3 o = (d0 + d1) * 0.5f;
                            f.QuadN(c0 + d0 * r, c0 + d1 * r, c1 + d1 * r, c1 + d0 * r, d0, d1, d1, d0, o);
                        }
                    }
            }
            // corner patches: triangular grid on each octant of the rounding sphere
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                    {
                        Vector3 o = new Vector3(sx * hi.x, sy * hi.y, sz * hi.z);
                        Vector3 outward = new Vector3(sx, sy, sz);
                        for (int i = 0; i < s; i++)
                            for (int j = 0; j < s - i; j++)
                            {
                                Vector3 n0 = Oct(i, j, s, sx, sy, sz), n1 = Oct(i + 1, j, s, sx, sy, sz), n2 = Oct(i, j + 1, s, sx, sy, sz);
                                f.TriN(o + n0 * r, o + n1 * r, o + n2 * r, n0, n1, n2, outward);
                                if (i + j < s - 1)
                                {
                                    Vector3 n3 = Oct(i + 1, j + 1, s, sx, sy, sz);
                                    f.TriN(o + n1 * r, o + n3 * r, o + n2 * r, n1, n3, n2, outward);
                                }
                            }
                    }
            return Store(key, f.ToMesh(false));
        }

        /// Direction of grid vertex (i, j, k = s − i − j) on an octant.
        static Vector3 Oct(int i, int j, int s, int sx, int sy, int sz) => Norm(new Vector3(sx * i, sy * j, sz * (s - i - j)));

        public static Mesh Box(Vector3 size) => RoundedBox(size, 0f, 0);

        // ------------------------------------------------------------------ prism / frustum
        /// Frustum along +Y, centred at the origin (smooth sides, hard caps). 'segments' 3..48; 'hard' is legacy.
        public static Mesh Frustum(float rBottom, float rTop, float height, int segments = 20, bool caps = true, bool hard = false)
        {
            segments = Mathf.Clamp(segments, 3, 48);
            string key = $"fr{rBottom:F4}_{rTop:F4}_{height:F4}_{segments}_{(caps ? 1 : 0)}";
            if (TryGet(key, out var cached)) return cached;

            var f = new Flat();
            float hy = height * 0.5f;
            float offset = Mathf.PI / segments;
            float slope = (rBottom - rTop) / Mathf.Max(1e-5f, height);
            for (int i = 0; i < segments; i++)
            {
                float a0 = i / (float)segments * Mathf.PI * 2f + offset, a1 = (i + 1) / (float)segments * Mathf.PI * 2f + offset;
                Vector3 u0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), u1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                Vector3 b0 = u0 * rBottom - Vector3.up * hy, b1 = u1 * rBottom - Vector3.up * hy;
                Vector3 t0 = u0 * rTop + Vector3.up * hy, t1 = u1 * rTop + Vector3.up * hy;
                // segments ≤ 5 (matte box, prisms) keep hard side edges; rounder shapes get smooth side normals
                if (segments <= 5)
                {
                    Vector3 mid = (b0 + b1 + t0 + t1) * 0.25f;
                    f.Quad(b0, t0, t1, b1, new Vector3(mid.x, 0f, mid.z));
                }
                else
                {
                    Vector3 n0 = Norm(u0 + Vector3.up * slope), n1 = Norm(u1 + Vector3.up * slope);
                    f.QuadN(b0, t0, t1, b1, n0, n0, n1, n1, (u0 + u1) * 0.5f);
                }
                if (caps)
                {
                    if (rTop > 0.0001f) f.TriN(new Vector3(0f, hy, 0f), t0, t1, Vector3.up, Vector3.up, Vector3.up, Vector3.up);
                    if (rBottom > 0.0001f) f.TriN(new Vector3(0f, -hy, 0f), b0, b1, Vector3.down, Vector3.down, Vector3.down, Vector3.down);
                }
            }
            return Store(key, f.ToMesh(false));
        }

        public static Mesh Cylinder(float radius, float height, int segments = 20) => Frustum(radius, radius, height, segments, true, false);

        /// Cylinder along +Y with softly bevelled rims (lens rings, knobs, dials): bevel = rim radius (≤ 45 % of the
        /// radius / height). Triangles: 8 × segments.
        public static Mesh RoundedCylinder(float radius, float height, int segments, float bevel)
        {
            segments = Mathf.Clamp(segments, 6, 48);
            float b = Mathf.Clamp(bevel, 0f, Mathf.Min(radius, height * 0.5f) * 0.9f);
            if (b < 0.0002f) return Cylinder(radius, height, segments);
            string key = $"rc{radius:F4}_{height:F4}_{segments}_{b:F4}";
            if (TryGet(key, out var cached)) return cached;

            var f = new Flat();
            float hy = height * 0.5f;
            float offset = Mathf.PI / segments;
            float ri = radius - b, yi = hy - b;
            for (int i = 0; i < segments; i++)
            {
                float a0 = i / (float)segments * Mathf.PI * 2f + offset, a1 = (i + 1) / (float)segments * Mathf.PI * 2f + offset;
                Vector3 u0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), u1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                Vector3 o = (u0 + u1) * 0.5f;
                // caps
                f.TriN(new Vector3(0f, hy, 0f), u0 * ri + Vector3.up * hy, u1 * ri + Vector3.up * hy, Vector3.up, Vector3.up, Vector3.up, Vector3.up);
                f.TriN(new Vector3(0f, -hy, 0f), u0 * ri - Vector3.up * hy, u1 * ri - Vector3.up * hy, Vector3.down, Vector3.down, Vector3.down, Vector3.down);
                // top bevel: cap edge (normal up) → side top (normal radial)
                f.QuadN(u0 * ri + Vector3.up * hy, u1 * ri + Vector3.up * hy, u1 * radius + Vector3.up * yi, u0 * radius + Vector3.up * yi,
                        Vector3.up, Vector3.up, u1, u0, o + Vector3.up);
                // side
                f.QuadN(u0 * radius + Vector3.up * yi, u1 * radius + Vector3.up * yi, u1 * radius - Vector3.up * yi, u0 * radius - Vector3.up * yi,
                        u0, u1, u1, u0, o);
                // bottom bevel
                f.QuadN(u0 * radius - Vector3.up * yi, u1 * radius - Vector3.up * yi, u1 * ri - Vector3.up * hy, u0 * ri - Vector3.up * hy,
                        u0, u1, Vector3.down, Vector3.down, o + Vector3.down);
            }
            return Store(key, f.ToMesh(false));
        }

        // ------------------------------------------------------------------ ball / dome
        /// Smooth ball (or the +Y dome when 'hemisphere' is true) centred at the origin. segments = sides (6..32).
        public static Mesh Sphere(float radius, int segments = 16, bool hemisphere = false)
        {
            int lon = Mathf.Clamp(segments, 6, 32);
            int lat = Mathf.Max(hemisphere ? 2 : 3, hemisphere ? lon / 4 : lon / 2);
            string key = $"sp{radius:F4}_{lon}_{(hemisphere ? 1 : 0)}";
            if (TryGet(key, out var cached)) return cached;

            float latMax = hemisphere ? Mathf.PI * 0.5f : Mathf.PI;
            var f = new Flat();
            Vector3 Dir(int i, int j)
            {
                float th = i / (float)lat * latMax;
                float ph = (j % lon) / (float)lon * Mathf.PI * 2f;
                return new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
            }
            for (int i = 0; i < lat; i++)
                for (int j = 0; j < lon; j++)
                {
                    Vector3 a = Dir(i, j), b = Dir(i, j + 1), c = Dir(i + 1, j + 1), d = Dir(i + 1, j);
                    Vector3 o = (a + b + c + d) * 0.25f;
                    if (i == 0) f.TriN(a * radius, c * radius, d * radius, a, c, d, o);
                    else if (i == lat - 1 && !hemisphere) f.TriN(a * radius, b * radius, c * radius, a, b, c, o);
                    else f.QuadN(a * radius, b * radius, c * radius, d * radius, a, b, c, d, o);
                }
            return Store(key, f.ToMesh(false));
        }

        // ------------------------------------------------------------------ ellipsoid (soft blobs: mitten, thumb)
        /// Smooth ellipsoid centred at the origin with the given semi-axes (exact analytic normals, indexed).
        public static Mesh Ellipsoid(Vector3 radii, int lon = 12, int lat = 8)
        {
            lon = Mathf.Clamp(lon, 6, 24);
            lat = Mathf.Clamp(lat, 4, 14);
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

        // ------------------------------------------------------------------ bounds / stats
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

        /// Triangles rendered below a node (build-time statistics, L.13 budget ≤ 2,500 per held item).
        public static int TriangleCount(Transform node)
        {
            if (node == null) return 0;
            int n = 0;
            var filters = node.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                var m = filters[i] != null ? filters[i].sharedMesh : null;
                if (m == null) continue;
                for (int s = 0; s < m.subMeshCount; s++) n += (int)(m.GetIndexCount(s) / 3);
            }
            return n;
        }

        // ------------------------------------------------------------------ combine
        /// Merges every MeshFilter below 'source' into one mesh per material, parented to 'target'. Destroys 'source'.
        public static List<Mesh> CombineStatic(Transform source, Transform target) => CombineStaticMany(target, source);

        /// Merges every MeshFilter below the given sources into one mesh per material (all sources together), parented
        /// to 'target' (any transform hierarchy and scale is baked in; mirrored parts get their normals right because
        /// CombineMeshes applies the full matrix). Destroys the sources. Returns the created meshes.
        public static List<Mesh> CombineStaticMany(Transform target, params Transform[] sources)
        {
            var created = new List<Mesh>();
            var groups = new Dictionary<Material, List<CombineInstance>>();
            var order = new List<Material>();
            Matrix4x4 toTarget = target.worldToLocalMatrix;
            int layer = target.gameObject.layer;
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

                var go = new GameObject("Combined_" + mat.name) { layer = layer };
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
