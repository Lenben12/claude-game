using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Procedural low-poly mesh assembly for the Boat module (boat, helicopter, crates, fishing props).
    /// Geometry is transformed by Matrix (rigid transforms / positive scale only). Vertex normals given to V() are only
    /// orientation hints: the *Auto methods wind every triangle so its face normal agrees with them. The final mesh is
    /// always faceted (every triangle has its own vertices and its flat face normal), no UVs/tangents (solid colors only).
    public class BoatMeshBuilder
    {
        public readonly List<Vector3> Verts = new List<Vector3>(128);
        public readonly List<Vector3> Norms = new List<Vector3>(128);
        public readonly List<int> Tris = new List<int>(384);

        Matrix4x4 m = Matrix4x4.identity;

        /// Rigid transform with positive scale. Hint normals are transformed by the matrix itself: for such matrices
        /// M·n always keeps a positive dot product with the true normal M^-T·n, which is all the winding needs.
        public Matrix4x4 Matrix
        {
            get => m;
            set => m = value;
        }

        public int Count => Verts.Count;

        public int V(Vector3 p, Vector3 n)
        {
            Verts.Add(m.MultiplyPoint3x4(p));
            Vector3 wn = m.MultiplyVector(n);
            Norms.Add(wn.sqrMagnitude > 1e-12f ? wn.normalized : Vector3.up);
            return Verts.Count - 1;
        }

        public void Tri(int a, int b, int c) { Tris.Add(a); Tris.Add(b); Tris.Add(c); }

        public void TriAuto(int a, int b, int c)
        {
            Vector3 f = Vector3.Cross(Verts[b] - Verts[a], Verts[c] - Verts[a]);
            Vector3 n = Norms[a] + Norms[b] + Norms[c];
            if (Vector3.Dot(f, n) < 0f) Tri(a, c, b); else Tri(a, b, c);
        }

        public void QuadAuto(int a, int b, int c, int d) { TriAuto(a, b, c); TriAuto(a, c, d); }

        /// Triangle from positions, facing 'outward' (a direction hint).
        public void TriOut(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 outward)
        {
            int a = V(p0, outward), b = V(p1, outward), c = V(p2, outward);
            TriAuto(a, b, c);
        }

        /// Quad from positions (in order around the edge), facing 'outward'.
        public void QuadOut(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 outward)
        {
            int a = V(p0, outward), b = V(p1, outward), c = V(p2, outward), d = V(p3, outward);
            QuadAuto(a, b, c, d);
        }

        // ------------------------------------------------------------------ primitives
        public void Box(Vector3 center, Vector3 size) => Box(center, size, Quaternion.identity);

        /// Axis-aligned (in the rotated frame) box.
        public void Box(Vector3 center, Vector3 size, Quaternion rot)
        {
            Vector3 h = size * 0.5f;
            Face(center, rot, Vector3.right, Vector3.forward, Vector3.up, h.x, h.z, h.y);
            Face(center, rot, Vector3.left, Vector3.forward, Vector3.up, h.x, h.z, h.y);
            Face(center, rot, Vector3.up, Vector3.right, Vector3.forward, h.y, h.x, h.z);
            Face(center, rot, Vector3.down, Vector3.right, Vector3.forward, h.y, h.x, h.z);
            Face(center, rot, Vector3.forward, Vector3.right, Vector3.up, h.z, h.x, h.y);
            Face(center, rot, Vector3.back, Vector3.right, Vector3.up, h.z, h.x, h.y);
        }

        void Face(Vector3 c, Quaternion r, Vector3 n, Vector3 u, Vector3 v, float hn, float hu, float hv)
        {
            Vector3 fc = c + r * (n * hn);
            Vector3 U = r * (u * hu), W = r * (v * hv), N = r * n;
            QuadOut(fc - U - W, fc + U - W, fc + U + W, fc - U + W, N);
        }

        /// Box whose top face is smaller than its bottom face (chunky cowlings, housings, cabins).
        /// bottom = (x, z) size at -height/2, top = (x, z) size at +height/2, topShift moves the top face (x, z).
        public void TaperBox(Vector3 center, Vector2 bottom, Vector2 top, float height, Quaternion rot, Vector2 topShift = default)
        {
            float hy = height * 0.5f;
            var b = new Vector3[4];
            var t = new Vector3[4];
            for (int i = 0; i < 4; i++)
            {
                float sx = (i == 0 || i == 3) ? -1f : 1f, sz = i < 2 ? -1f : 1f;
                b[i] = center + rot * new Vector3(sx * bottom.x * 0.5f, -hy, sz * bottom.y * 0.5f);
                t[i] = center + rot * new Vector3(sx * top.x * 0.5f + topShift.x, hy, sz * top.y * 0.5f + topShift.y);
            }
            Vector3 c = center;
            QuadOut(t[0], t[1], t[2], t[3], rot * Vector3.up);
            QuadOut(b[0], b[1], b[2], b[3], rot * Vector3.down);
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                Vector3 mid = (b[i] + b[j] + t[i] + t[j]) * 0.25f;
                QuadOut(b[i], b[j], t[j], t[i], mid - c);
            }
        }

        /// Box between two points (e.g. a plank or beam) with a cross-section of width x height.
        public void Beam(Vector3 a, Vector3 b, float width, float height)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 1e-5f) return;
            Quaternion rot = Quaternion.LookRotation(d / len, Mathf.Abs(d.y / len) > 0.98f ? Vector3.forward : Vector3.up);
            Box((a + b) * 0.5f, new Vector3(width, height, len), rot);
        }

        static void Basis(Vector3 d, out Vector3 u, out Vector3 w)
        {
            u = Vector3.Cross(d, Mathf.Abs(d.y) < 0.95f ? Vector3.up : Vector3.right).normalized;
            w = Vector3.Cross(d, u);
        }

        /// Faceted (tapered) prism from a to b with 'seg' sides.
        public void Cylinder(Vector3 a, Vector3 b, float ra, float rb, int seg, bool capA = true, bool capB = true, float angleOffset = 0f)
        {
            Vector3 axis = b - a;
            float len = axis.magnitude;
            if (len < 1e-5f) return;
            seg = Mathf.Max(3, seg);
            Vector3 d = axis / len;
            Basis(d, out var u, out var w);
            int start = Count;
            for (int i = 0; i <= seg; i++)
            {
                float ang = i / (float)seg * Mathf.PI * 2f + angleOffset;
                Vector3 radial = u * Mathf.Cos(ang) + w * Mathf.Sin(ang);
                Vector3 n = (radial + d * ((ra - rb) / len)).normalized;
                V(a + radial * ra, n);
                V(b + radial * rb, n);
            }
            for (int i = 0; i < seg; i++)
            {
                int i0 = start + i * 2;
                QuadAuto(i0, i0 + 1, i0 + 3, i0 + 2);
            }
            if (capA && ra > 0f) Disc(a, -d, ra, seg, angleOffset, u, w);
            if (capB && rb > 0f) Disc(b, d, rb, seg, angleOffset, u, w);
        }

        /// Open faceted tube wall (outer + inner surface and a rim), e.g. a bucket.
        public void Tube(Vector3 a, Vector3 b, float rOuter, float rInner, int seg, bool bottom)
        {
            Cylinder(a, b, rOuter, rOuter, seg, bottom, false);
            Vector3 axis = b - a;
            float len = axis.magnitude;
            if (len < 1e-5f) return;
            Vector3 d = axis / len;
            Basis(d, out var u, out var w);
            for (int i = 0; i < seg; i++)
            {
                float a0 = i / (float)seg * Mathf.PI * 2f, a1 = (i + 1) / (float)seg * Mathf.PI * 2f;
                Vector3 r0 = u * Mathf.Cos(a0) + w * Mathf.Sin(a0), r1 = u * Mathf.Cos(a1) + w * Mathf.Sin(a1);
                Vector3 inward = -(r0 + r1);
                QuadOut(a + r0 * rInner + d * 0.01f, a + r1 * rInner + d * 0.01f, b + r1 * rInner, b + r0 * rInner, inward);
                QuadOut(b + r0 * rOuter, b + r1 * rOuter, b + r1 * rInner, b + r0 * rInner, d);
            }
            if (bottom) Disc(a + d * 0.012f, d, rInner, seg, 0f, u, w);
        }

        public void Disc(Vector3 c, Vector3 n, float r, int seg, float angleOffset = 0f)
        {
            Basis(n, out var u, out var w);
            Disc(c, n, r, seg, angleOffset, u, w);
        }

        void Disc(Vector3 c, Vector3 n, float r, int seg, float angleOffset, Vector3 u, Vector3 w)
        {
            int ci = V(c, n);
            int start = Count;
            for (int i = 0; i <= seg; i++)
            {
                float ang = i / (float)seg * Mathf.PI * 2f + angleOffset;
                V(c + (u * Mathf.Cos(ang) + w * Mathf.Sin(ang)) * r, n);
            }
            for (int i = 0; i < seg; i++) TriAuto(ci, start + i, start + i + 1);
        }

        /// Faceted ellipsoid (or a latitude band of it). Latitudes in degrees (-90 bottom .. 90 top).
        public void Ellipsoid(Vector3 c, Vector3 radii, Quaternion rot, int seg, int rings, float lat0 = -90f, float lat1 = 90f, bool inward = false)
        {
            int start = Count;
            Vector3 inv = new Vector3(1f / Mathf.Max(1e-4f, radii.x), 1f / Mathf.Max(1e-4f, radii.y), 1f / Mathf.Max(1e-4f, radii.z));
            for (int r = 0; r <= rings; r++)
            {
                float lat = Mathf.Lerp(lat0, lat1, r / (float)rings) * Mathf.Deg2Rad;
                for (int s = 0; s <= seg; s++)
                {
                    float lon = s / (float)seg * Mathf.PI * 2f;
                    Vector3 unit = new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon));
                    Vector3 n = rot * Vector3.Scale(unit, inv);
                    V(c + rot * Vector3.Scale(unit, radii), inward ? -n : n);
                }
            }
            int row = seg + 1;
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < seg; s++)
                {
                    int i0 = start + r * row + s;
                    QuadAuto(i0, i0 + 1, i0 + row + 1, i0 + row);
                }
        }

        public void Sphere(Vector3 c, float radius, int seg = 6, int rings = 4) => Ellipsoid(c, Vector3.one * radius, Quaternion.identity, seg, rings);

        /// Faceted torus around the rotated Y axis. Segment range [u0, u1) out of segU allows multi-colored rings.
        public void Torus(Vector3 c, Quaternion rot, float R, float r, int segU, int segV, int u0 = 0, int u1 = -1)
        {
            if (u1 < 0) u1 = segU;
            int start = Count;
            int row = segV + 1;
            for (int i = u0; i <= u1; i++)
            {
                float th = i / (float)segU * Mathf.PI * 2f;
                Vector3 radial = new Vector3(Mathf.Cos(th), 0f, Mathf.Sin(th));
                for (int j = 0; j <= segV; j++)
                {
                    float ph = j / (float)segV * Mathf.PI * 2f + Mathf.PI / segV;
                    Vector3 n = radial * Mathf.Cos(ph) + Vector3.up * Mathf.Sin(ph);
                    V(c + rot * (radial * R + n * r), rot * n);
                }
            }
            for (int i = 0; i < u1 - u0; i++)
                for (int j = 0; j < segV; j++)
                {
                    int i0 = start + i * row + j;
                    QuadAuto(i0, i0 + row, i0 + row + 1, i0 + 1);
                }
        }

        public void Clear() { Verts.Clear(); Norms.Clear(); Tris.Clear(); }

        /// Appends this builder's triangles as faceted geometry (own vertices, flat face normal). Degenerate triangles are dropped.
        public void AppendFlat(List<Vector3> outVerts, List<Vector3> outNorms, List<int> outTris)
        {
            for (int t = 0; t + 2 < Tris.Count; t += 3)
            {
                Vector3 a = Verts[Tris[t]], b = Verts[Tris[t + 1]], c = Verts[Tris[t + 2]];
                Vector3 f = Vector3.Cross(b - a, c - a);
                float sq = f.sqrMagnitude;
                if (!(sq > 1e-16f) || float.IsInfinity(sq)) continue;   // also rejects NaN
                Vector3 n = f / Mathf.Sqrt(sq);
                int i = outVerts.Count;
                outVerts.Add(a); outVerts.Add(b); outVerts.Add(c);
                outNorms.Add(n); outNorms.Add(n); outNorms.Add(n);
                outTris.Add(i); outTris.Add(i + 1); outTris.Add(i + 2);
            }
        }

        public Mesh ToMesh(string name)
        {
            var v = new List<Vector3>(Tris.Count);
            var n = new List<Vector3>(Tris.Count);
            var t = new List<int>(Tris.Count);
            AppendFlat(v, n, t);
            var mesh = new Mesh { name = name };
            if (v.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    /// A set of builders, one per material, merged into a single multi-submesh renderer (faceted, flat colors).
    public class BoatMeshSet
    {
        readonly List<Material> mats = new List<Material>();
        readonly List<BoatMeshBuilder> builders = new List<BoatMeshBuilder>();
        Matrix4x4 matrix = Matrix4x4.identity;

        public Matrix4x4 Matrix
        {
            get => matrix;
            set { matrix = value; for (int i = 0; i < builders.Count; i++) builders[i].Matrix = value; }
        }

        public void SetTRS(Vector3 pos, Quaternion rot) => Matrix = Matrix4x4.TRS(pos, rot, Vector3.one);
        public void SetTRS(Vector3 pos, Quaternion rot, Vector3 scale) => Matrix = Matrix4x4.TRS(pos, rot, scale);
        public void ResetMatrix() => Matrix = Matrix4x4.identity;

        public BoatMeshBuilder this[Material m]
        {
            get
            {
                int i = mats.IndexOf(m);
                if (i >= 0) return builders[i];
                var b = new BoatMeshBuilder { Matrix = matrix };
                mats.Add(m);
                builders.Add(b);
                return b;
            }
        }

        public bool IsEmpty
        {
            get
            {
                for (int i = 0; i < builders.Count; i++) if (builders[i].Tris.Count > 0) return false;
                return true;
            }
        }

        public Mesh ToMesh(string name, List<Material> usedMats)
        {
            int cap = 0;
            for (int i = 0; i < builders.Count; i++) cap += builders[i].Tris.Count;
            var verts = new List<Vector3>(cap);
            var norms = new List<Vector3>(cap);
            var subs = new List<List<int>>();
            usedMats.Clear();
            for (int i = 0; i < builders.Count; i++)
            {
                var b = builders[i];
                if (b.Tris.Count == 0) continue;
                var tris = new List<int>(b.Tris.Count);
                b.AppendFlat(verts, norms, tris);
                if (tris.Count == 0) continue;
                subs.Add(tris);
                usedMats.Add(mats[i]);
            }
            var mesh = new Mesh { name = name };
            if (verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.subMeshCount = subs.Count;
            for (int s = 0; s < subs.Count; s++) mesh.SetTriangles(subs[s], s);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// Creates a child GameObject with one MeshRenderer that holds every material as a submesh.
        public GameObject Build(string name, Transform parent, bool shadows = true)
        {
            return Build(name, parent, Vector3.zero, Quaternion.identity, shadows);
        }

        public GameObject Build(string name, Transform parent, Vector3 localPos, Quaternion localRot, bool shadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            var used = new List<Material>();
            var mesh = ToMesh(name, used);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = used.ToArray();
            mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return go;
        }
    }
}
