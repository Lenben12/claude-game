using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Procedural mesh assembly for the Boat module (boat, helicopter, crates, fishing props).
    /// Geometry is transformed by Matrix (rigid transforms / positive scale only). Triangles added through the
    /// *Auto methods are wound so that their face normal agrees with the supplied vertex normals.
    public class BoatMeshBuilder
    {
        public readonly List<Vector3> Verts = new List<Vector3>(256);
        public readonly List<Vector3> Norms = new List<Vector3>(256);
        public readonly List<Vector2> UVs = new List<Vector2>(256);
        public readonly List<int> Tris = new List<int>(768);

        Matrix4x4 m = Matrix4x4.identity, nm = Matrix4x4.identity;

        public Matrix4x4 Matrix
        {
            get => m;
            set { m = value; nm = value.inverse.transpose; }
        }

        public int Count => Verts.Count;

        public int V(Vector3 p, Vector3 n, Vector2 uv)
        {
            Verts.Add(m.MultiplyPoint3x4(p));
            Vector3 wn = nm.MultiplyVector(n);
            Norms.Add(wn.sqrMagnitude > 1e-12f ? wn.normalized : Vector3.up);
            UVs.Add(uv);
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

        public void Quad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 n, Vector2 uv0, Vector2 uv1, Vector2 uv2, Vector2 uv3)
        {
            int a = V(p0, n, uv0), b = V(p1, n, uv1), c = V(p2, n, uv2), d = V(p3, n, uv3);
            QuadAuto(a, b, c, d);
        }

        /// Flat quad with UVs from the corner positions (meters * uvScale) along the given axes.
        public void QuadPlanar(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 n, Vector3 uAxis, Vector3 vAxis, float uvScale)
        {
            Quad(p0, p1, p2, p3, n,
                new Vector2(Vector3.Dot(p0, uAxis), Vector3.Dot(p0, vAxis)) * uvScale,
                new Vector2(Vector3.Dot(p1, uAxis), Vector3.Dot(p1, vAxis)) * uvScale,
                new Vector2(Vector3.Dot(p2, uAxis), Vector3.Dot(p2, vAxis)) * uvScale,
                new Vector2(Vector3.Dot(p3, uAxis), Vector3.Dot(p3, vAxis)) * uvScale);
        }

        // ------------------------------------------------------------------ primitives
        public void Box(Vector3 center, Vector3 size, float uvScale = 1f) => Box(center, size, Quaternion.identity, uvScale, false);

        /// Axis-aligned (in the rotated frame) box. unitUV maps every face to 0..1, otherwise UVs are meters * uvScale.
        public void Box(Vector3 center, Vector3 size, Quaternion rot, float uvScale = 1f, bool unitUV = false)
        {
            Vector3 h = size * 0.5f;
            Vector2 off = unitUV ? Vector2.zero : new Vector2(center.x * 0.37f + center.z * 0.61f, center.y * 0.53f + center.x * 0.11f) * uvScale;
            BoxFace(center, rot, Vector3.right, Vector3.forward, Vector3.up, h.x, h.z, h.y, uvScale, unitUV, off);
            BoxFace(center, rot, Vector3.left, Vector3.forward, Vector3.up, h.x, h.z, h.y, uvScale, unitUV, off);
            BoxFace(center, rot, Vector3.up, Vector3.right, Vector3.forward, h.y, h.x, h.z, uvScale, unitUV, off);
            BoxFace(center, rot, Vector3.down, Vector3.right, Vector3.forward, h.y, h.x, h.z, uvScale, unitUV, off);
            BoxFace(center, rot, Vector3.forward, Vector3.right, Vector3.up, h.z, h.x, h.y, uvScale, unitUV, off);
            BoxFace(center, rot, Vector3.back, Vector3.right, Vector3.up, h.z, h.x, h.y, uvScale, unitUV, off);
        }

        void BoxFace(Vector3 c, Quaternion r, Vector3 n, Vector3 u, Vector3 v, float hn, float hu, float hv, float uvScale, bool unitUV, Vector2 off)
        {
            Vector3 fc = c + r * (n * hn);
            Vector3 U = r * (u * hu), W = r * (v * hv), N = r * n;
            Vector2 s = unitUV ? Vector2.one : new Vector2(hu * 2f * uvScale, hv * 2f * uvScale);
            Quad(fc - U - W, fc + U - W, fc + U + W, fc - U + W, N,
                off, off + new Vector2(s.x, 0f), off + s, off + new Vector2(0f, s.y));
        }

        /// Box between two points (e.g. a plank or beam) with a cross-section of width x height.
        public void Beam(Vector3 a, Vector3 b, float width, float height, float uvScale = 1f)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 1e-5f) return;
            Quaternion rot = Quaternion.LookRotation(d / len, Mathf.Abs(d.y / len) > 0.98f ? Vector3.forward : Vector3.up);
            Box((a + b) * 0.5f, new Vector3(width, height, len), rot, uvScale);
        }

        static void Basis(Vector3 d, out Vector3 u, out Vector3 w)
        {
            u = Vector3.Cross(d, Mathf.Abs(d.y) < 0.95f ? Vector3.up : Vector3.right).normalized;
            w = Vector3.Cross(d, u);
        }

        /// (Tapered) cylinder from a to b.
        public void Cylinder(Vector3 a, Vector3 b, float ra, float rb, int seg, bool capA = true, bool capB = true, float uvScale = 1f)
        {
            Vector3 axis = b - a;
            float len = axis.magnitude;
            if (len < 1e-5f) return;
            Vector3 d = axis / len;
            Basis(d, out var u, out var w);
            int start = Count;
            float circ = 2f * Mathf.PI * Mathf.Max(ra, rb);
            for (int i = 0; i <= seg; i++)
            {
                float ang = i / (float)seg * Mathf.PI * 2f;
                Vector3 radial = u * Mathf.Cos(ang) + w * Mathf.Sin(ang);
                Vector3 n = (radial + d * ((ra - rb) / len)).normalized;
                float uu = i / (float)seg * circ * uvScale;
                V(a + radial * ra, n, new Vector2(uu, 0f));
                V(b + radial * rb, n, new Vector2(uu, len * uvScale));
            }
            for (int i = 0; i < seg; i++)
            {
                int i0 = start + i * 2;
                QuadAuto(i0, i0 + 1, i0 + 3, i0 + 2);
            }
            if (capA && ra > 0f) Disc(a, -d, ra, seg, uvScale);
            if (capB && rb > 0f) Disc(b, d, rb, seg, uvScale);
        }

        /// Open (inside visible) tube wall: outer + inner surface and a rim, e.g. a bucket or exhaust pipe.
        public void Tube(Vector3 a, Vector3 b, float rOuter, float rInner, int seg, bool bottom, float uvScale = 1f)
        {
            Cylinder(a, b, rOuter, rOuter, seg, bottom, false, uvScale);
            Vector3 axis = b - a;
            float len = axis.magnitude;
            if (len < 1e-5f) return;
            Vector3 d = axis / len;
            Basis(d, out var u, out var w);
            int start = Count;
            for (int i = 0; i <= seg; i++)
            {
                float ang = i / (float)seg * Mathf.PI * 2f;
                Vector3 radial = u * Mathf.Cos(ang) + w * Mathf.Sin(ang);
                float uu = i / (float)seg * uvScale * 2f * Mathf.PI * rInner;
                V(a + radial * rInner + d * 0.01f, -radial, new Vector2(uu, 0f));
                V(b + radial * rInner, -radial, new Vector2(uu, len * uvScale));
            }
            for (int i = 0; i < seg; i++)
            {
                int i0 = start + i * 2;
                QuadAuto(i0, i0 + 1, i0 + 3, i0 + 2);
            }
            // rim
            int rs = Count;
            for (int i = 0; i <= seg; i++)
            {
                float ang = i / (float)seg * Mathf.PI * 2f;
                Vector3 radial = u * Mathf.Cos(ang) + w * Mathf.Sin(ang);
                V(b + radial * rOuter, d, new Vector2(i / (float)seg, 0f));
                V(b + radial * rInner, d, new Vector2(i / (float)seg, 0.1f));
            }
            for (int i = 0; i < seg; i++)
            {
                int i0 = rs + i * 2;
                QuadAuto(i0, i0 + 1, i0 + 3, i0 + 2);
            }
            if (bottom) Disc(a + d * 0.012f, d, rInner, seg, uvScale);
        }

        public void Disc(Vector3 c, Vector3 n, float r, int seg, float uvScale = 1f)
        {
            Basis(n, out var u, out var w);
            int ci = V(c, n, new Vector2(0.5f, 0.5f));
            int start = Count;
            for (int i = 0; i <= seg; i++)
            {
                float ang = i / (float)seg * Mathf.PI * 2f;
                float cs = Mathf.Cos(ang), sn = Mathf.Sin(ang);
                V(c + (u * cs + w * sn) * r, n, new Vector2(0.5f + cs * 0.5f, 0.5f + sn * 0.5f));
            }
            for (int i = 0; i < seg; i++) TriAuto(ci, start + i, start + i + 1);
        }

        /// Ellipsoid (or a latitude band of it) with radii in the rotated frame. Latitudes in degrees (-90 bottom .. 90 top).
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
                    Vector3 p = c + rot * Vector3.Scale(unit, radii);
                    Vector3 n = rot * Vector3.Scale(unit, inv);
                    if (inward) n = -n;
                    V(p, n, new Vector2(s / (float)seg, r / (float)rings));
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

        public void Sphere(Vector3 c, float radius, int seg = 12, int rings = 8) => Ellipsoid(c, Vector3.one * radius, Quaternion.identity, seg, rings);

        /// Torus around the rotated Y axis. Segment range [u0, u1) out of segU allows multi-colored rings.
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
                    float ph = j / (float)segV * Mathf.PI * 2f;
                    Vector3 n = radial * Mathf.Cos(ph) + Vector3.up * Mathf.Sin(ph);
                    Vector3 p = radial * R + n * r;
                    V(c + rot * p, rot * n, new Vector2(i / (float)segU * 4f, j / (float)segV));
                }
            }
            for (int i = 0; i < u1 - u0; i++)
                for (int j = 0; j < segV; j++)
                {
                    int i0 = start + i * row + j;
                    QuadAuto(i0, i0 + row, i0 + row + 1, i0 + 1);
                }
        }

        /// Smooth surface from a grid of points (rows x cols). Normals are computed by finite differences and
        /// oriented away from 'inside' (a point inside the closed surface). wrapCols: the last column repeats the first.
        public void Grid(Vector3[,] p, Vector2[,] uv, Vector3 inside, bool wrapCols)
        {
            int rows = p.GetLength(0), cols = p.GetLength(1);
            int start = Count;
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    Vector3 du = p[Mathf.Min(r + 1, rows - 1), c] - p[Mathf.Max(r - 1, 0), c];
                    int cp = wrapCols ? (c + 1 >= cols ? 1 : c + 1) : Mathf.Min(c + 1, cols - 1);
                    int cm = wrapCols ? (c - 1 < 0 ? cols - 2 : c - 1) : Mathf.Max(c - 1, 0);
                    Vector3 dv = p[r, cp] - p[r, cm];
                    Vector3 n = Vector3.Cross(du, dv);
                    if (n.sqrMagnitude < 1e-12f) n = p[r, c] - inside;
                    if (Vector3.Dot(n, p[r, c] - inside) < 0f) n = -n;
                    V(p[r, c], n, uv[r, c]);
                }
            for (int r = 0; r < rows - 1; r++)
                for (int c = 0; c < cols - 1; c++)
                {
                    int i0 = start + r * cols + c;
                    QuadAuto(i0, i0 + 1, i0 + cols + 1, i0 + cols);
                }
        }

        public void Clear() { Verts.Clear(); Norms.Clear(); UVs.Clear(); Tris.Clear(); }

        /// Replaces the normals by smooth normals computed from the triangles (shared vertices are averaged).
        public void RecalculateNormals()
        {
            for (int i = 0; i < Norms.Count; i++) Norms[i] = Vector3.zero;
            for (int t = 0; t < Tris.Count; t += 3)
            {
                int a = Tris[t], b = Tris[t + 1], c = Tris[t + 2];
                Vector3 f = Vector3.Cross(Verts[b] - Verts[a], Verts[c] - Verts[a]);
                Norms[a] += f; Norms[b] += f; Norms[c] += f;
            }
            for (int i = 0; i < Norms.Count; i++)
            {
                Vector3 n = Norms[i];
                Norms[i] = n.sqrMagnitude > 1e-14f ? n.normalized : Vector3.up;
            }
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            if (Verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(Verts);
            mesh.SetNormals(Norms);
            mesh.SetUVs(0, UVs);
            mesh.SetTriangles(Tris, 0);
            mesh.RecalculateBounds();
            if (Tris.Count > 0) mesh.RecalculateTangents();
            return mesh;
        }
    }

    /// A set of builders, one per material, merged into a single multi-submesh renderer.
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
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var subs = new List<List<int>>();
            usedMats.Clear();
            for (int i = 0; i < builders.Count; i++)
            {
                var b = builders[i];
                if (b.Tris.Count == 0) continue;
                int offset = verts.Count;
                verts.AddRange(b.Verts);
                norms.AddRange(b.Norms);
                uvs.AddRange(b.UVs);
                var tris = new List<int>(b.Tris.Count);
                for (int t = 0; t < b.Tris.Count; t++) tris.Add(b.Tris[t] + offset);
                subs.Add(tris);
                usedMats.Add(mats[i]);
            }
            var mesh = new Mesh { name = name };
            if (verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = subs.Count;
            for (int s = 0; s < subs.Count; s++) mesh.SetTriangles(subs[s], s);
            mesh.RecalculateBounds();
            if (verts.Count > 0) mesh.RecalculateTangents();
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
