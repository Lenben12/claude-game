using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    public enum PropUV { Default, LongU }

    /// Procedural mesh builder (low-poly style). All primitives are given in "builder space" and transformed by M on insertion.
    /// Winding convention (Unity): clockwise seen from the front; front normal = cross(b - a, c - a).
    /// ToMesh() produces FACETED output by default: every triangle gets its own three vertices and its face normal,
    /// degenerate and non-finite triangles are dropped (no NaN can ever reach a renderer).
    public sealed class PropMeshBuilder
    {
        public readonly List<Vector3> V = new List<Vector3>(256);
        public readonly List<Vector3> N = new List<Vector3>(256);
        public readonly List<Vector2> UV = new List<Vector2>(256);
        public readonly List<int> T = new List<int>(512);

        Matrix4x4 m = Matrix4x4.identity, nm = Matrix4x4.identity;

        public Matrix4x4 M
        {
            get => m;
            set { m = value; nm = value.inverse.transpose; }
        }

        public int VertexCount => V.Count;
        public int IndexCount => T.Count;
        public bool Empty => T.Count == 0;

        public void Clear()
        {
            V.Clear(); N.Clear(); UV.Clear(); T.Clear();
            M = Matrix4x4.identity;
        }

        public int Vert(Vector3 p, Vector3 n, Vector2 uv)
        {
            V.Add(m.MultiplyPoint3x4(p));
            Vector3 wn = nm.MultiplyVector(n);
            float l = wn.magnitude;
            N.Add(l > 1e-8f && !float.IsNaN(l) ? wn / l : Vector3.up);
            UV.Add(uv);
            return V.Count - 1;
        }

        public void Tri(int a, int b, int c) { T.Add(a); T.Add(b); T.Add(c); }
        public void Quad(int a, int b, int c, int d) { Tri(a, b, c); Tri(a, c, d); }

        /// Flat quad whose winding is chosen so the face points toward "outward".
        public void QuadAuto(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector2 t0, Vector2 t1, Vector2 t2, Vector2 t3, Vector3 outward)
        {
            Vector3 n = Vector3.Cross(p1 - p0, p2 - p0);
            if (n.sqrMagnitude < 1e-14f) n = Vector3.Cross(p2 - p0, p3 - p0);
            if (Vector3.Dot(n, outward) < 0f)
            {
                var tp = p1; p1 = p3; p3 = tp;
                var tt = t1; t1 = t3; t3 = tt;
                n = -n;
            }
            n = Unit(n, outward);
            int a = Vert(p0, n, t0), b = Vert(p1, n, t1), c = Vert(p2, n, t2), d = Vert(p3, n, t3);
            Quad(a, b, c, d);
        }

        public void TriAuto(Vector3 p0, Vector3 p1, Vector3 p2, Vector2 t0, Vector2 t1, Vector2 t2, Vector3 outward)
        {
            Vector3 n = Vector3.Cross(p1 - p0, p2 - p0);
            if (Vector3.Dot(n, outward) < 0f)
            {
                var tp = p1; p1 = p2; p2 = tp;
                var tt = t1; t1 = t2; t2 = tt;
                n = -n;
            }
            n = Unit(n, outward);
            int a = Vert(p0, n, t0), b = Vert(p1, n, t1), c = Vert(p2, n, t2);
            Tri(a, b, c);
        }

        /// Unit vector without Vector3.normalized's 1e-5 cut-off (it returns ZERO for the cross product of a
        /// millimeter-sized face, and a zero normal renders as NaN pixels). Falls back to 'fallback', then to up.
        static Vector3 Unit(Vector3 v, Vector3 fallback)
        {
            float sq = v.sqrMagnitude;
            if (sq > 1e-24f && !float.IsInfinity(sq)) return v / Mathf.Sqrt(sq);
            sq = fallback.sqrMagnitude;
            if (sq > 1e-24f && !float.IsInfinity(sq)) return fallback / Mathf.Sqrt(sq);
            return Vector3.up;
        }

        /// Triangle facing "outward" (no UVs needed for flat colors).
        public void Face3(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 outward) =>
            TriAuto(p0, p1, p2, Vector2.zero, Vector2.zero, Vector2.zero, outward);

        /// Two-sided flat triangle (front toward "up" plus a back face): leaves, blades, fans, flags.
        public void Tri2(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 up)
        {
            TriAuto(p0, p1, p2, Vector2.zero, Vector2.zero, Vector2.zero, up);
            TriAuto(p0, p1, p2, Vector2.zero, Vector2.zero, Vector2.zero, -up);
        }

        /// Two-sided flat quad.
        public void Quad2(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 up)
        {
            Tri2(p0, p1, p2, up);
            Tri2(p0, p2, p3, up);
        }

        // ------------------------------------------------------------------ box
        public void Box(Vector3 c, Vector3 size, Quaternion r, float uvScale = 1f, Vector2 uvOff = default, PropUV mode = PropUV.Default)
        {
            Vector3 ax = r * new Vector3(size.x * 0.5f, 0f, 0f);
            Vector3 ay = r * new Vector3(0f, size.y * 0.5f, 0f);
            Vector3 az = r * new Vector3(0f, 0f, size.z * 0.5f);
            Face(c + ax, ax, az, ay, uvScale, uvOff, mode);
            Face(c - ax, -ax, az, ay, uvScale, uvOff, mode);
            Face(c + ay, ay, ax, az, uvScale, uvOff, mode);
            Face(c - ay, -ay, ax, az, uvScale, uvOff, mode);
            Face(c + az, az, ax, ay, uvScale, uvOff, mode);
            Face(c - az, -az, ax, ay, uvScale, uvOff, mode);
        }

        public void Box(Vector3 c, Vector3 size) => Box(c, size, Quaternion.identity);

        /// Axis-aligned box from min/max corners (builder space).
        public void BoxMinMax(Vector3 min, Vector3 max)
        {
            Box((min + max) * 0.5f, max - min, Quaternion.identity);
        }

        void Face(Vector3 center, Vector3 nDir, Vector3 U, Vector3 Vv, float uvScale, Vector2 off, PropUV mode)
        {
            if (mode == PropUV.LongU && Vv.sqrMagnitude > U.sqrMagnitude) { var t = U; U = Vv; Vv = t; }
            float lu = U.magnitude * 2f * uvScale, lv = Vv.magnitude * 2f * uvScale;
            Vector3 p0 = center - U - Vv, p1 = center - U + Vv, p2 = center + U + Vv, p3 = center + U - Vv;
            QuadAuto(p0, p1, p2, p3, off, off + new Vector2(0f, lv), off + new Vector2(lu, lv), off + new Vector2(lu, 0f), nDir);
        }

        /// Box spanning from point a to point b (thickness w x h), e.g. beams, rails, braces.
        public void Beam(Vector3 a, Vector3 b, float w, float h, float uvScale = 1f, Vector3? upHint = null)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 1e-4f) return;
            Vector3 up = upHint ?? Vector3.up;
            if (Mathf.Abs(Vector3.Dot(d / len, up.normalized)) > 0.98f) up = Vector3.forward;
            Quaternion r = Quaternion.LookRotation(d / len, up);
            Box((a + b) * 0.5f, new Vector3(w, h, len), r, uvScale, default, PropUV.LongU);
        }

        // ------------------------------------------------------------------ round primitives (keep "sides" low: 4-10)
        /// Frustum / cylinder along the rotated up axis. r1 = 0 gives a cone.
        public void Cylinder(Vector3 baseC, Quaternion r, float r0, float r1, float h, int sides,
            bool capBottom = false, bool capTop = false, float uTile = 1f, float vTile = 1f, float uOff = 0f, bool flip = false, float angleOffDeg = 0f)
        {
            if (sides < 3) sides = 3;
            Vector3 up = r * Vector3.up;
            int start = V.Count;
            float slope = r0 - r1;
            float a0 = angleOffDeg * Mathf.Deg2Rad;
            for (int i = 0; i <= sides; i++)
            {
                float a = a0 + i * Mathf.PI * 2f / sides;
                Vector3 d = r * new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 n = (d * h + up * slope).normalized;
                if (flip) n = -n;
                float u = uOff + (float)i / sides * uTile;
                Vert(baseC + d * r0, n, new Vector2(u, 0f));
                Vert(baseC + d * r1 + up * h, n, new Vector2(u, h * vTile));
            }
            for (int i = 0; i < sides; i++)
            {
                int a = start + i * 2, b = a + 1, c = a + 3, d = a + 2;
                if (!flip) Quad(a, b, c, d); else Quad(a, d, c, b);
            }
            if (capTop && r1 > 0f) Disk(baseC + up * h, r1, sides, r, true, vTile, false, angleOffDeg);
            if (capBottom && r0 > 0f) Disk(baseC, r0, sides, r, false, vTile, false, angleOffDeg);
        }

        /// Cylinder from point a to point b.
        public void Rod(Vector3 a, Vector3 b, float r0, float r1, int sides, bool caps = false, float uTile = 1f, float vTile = 1f)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 1e-4f) return;
            Cylinder(a, Quaternion.FromToRotation(Vector3.up, d / len), r0, r1, len, sides, caps, caps, uTile, vTile);
        }

        /// Flat polygon disk. unitUV maps the disk to [0,1]^2 (painted pads); otherwise planar meters * uvScale.
        public void Disk(Vector3 c, float radius, int sides, Quaternion r, bool facingUp, float uvScale = 1f, bool unitUV = false, float angleOffDeg = 0f)
        {
            if (sides < 3) sides = 3;
            Vector3 n = r * (facingUp ? Vector3.up : Vector3.down);
            int ci = Vert(c, n, unitUV ? new Vector2(0.5f, 0.5f) : Vector2.zero);
            int first = V.Count;
            float a0 = angleOffDeg * Mathf.Deg2Rad;
            for (int i = 0; i < sides; i++)
            {
                float a = a0 + i * Mathf.PI * 2f / sides;
                float x = Mathf.Cos(a), z = Mathf.Sin(a);
                Vector2 uv = unitUV ? new Vector2(0.5f + x * 0.5f, 0.5f + z * 0.5f) : new Vector2(x * radius * uvScale, z * radius * uvScale);
                Vert(c + r * new Vector3(x * radius, 0f, z * radius), n, uv);
            }
            for (int i = 0; i < sides; i++)
            {
                int a = first + i, b = first + (i + 1) % sides;
                if (facingUp) Tri(ci, b, a); else Tri(ci, a, b);
            }
        }

        /// Flat ring (annulus) facing up (helipad markings, rims).
        public void Ring(Vector3 c, float rIn, float rOut, int sides, Quaternion r)
        {
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                Vector3 d0 = r * new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = r * new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                Vector3 up = r * Vector3.up;
                QuadAuto(c + d0 * rIn, c + d0 * rOut, c + d1 * rOut, c + d1 * rIn, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, up);
            }
        }

        /// Surface of revolution. prof = (radius, y) from bottom to top. radiusMul(angle01, profile01) adds variation.
        public void Lathe(Vector3 c, Quaternion r, IList<Vector2> prof, int sides, float uTile = 1f, float vTile = 1f, Func<float, float, float> radiusMul = null)
        {
            int k = prof.Count;
            if (k < 2) return;
            int start = V.Count;
            for (int j = 0; j < k; j++)
            {
                float pt = (float)j / (k - 1);
                for (int i = 0; i <= sides; i++)
                {
                    float a01 = (float)(i % sides) / sides;
                    float a = a01 * Mathf.PI * 2f;
                    float mul = radiusMul != null ? radiusMul(a01, pt) : 1f;
                    if (float.IsNaN(mul) || float.IsInfinity(mul)) mul = 1f;
                    Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    Vector3 p = new Vector3(dir.x * prof[j].x * mul, prof[j].y, dir.z * prof[j].x * mul);
                    Vert(c + r * p, r * dir, new Vector2((float)i / sides * uTile, pt * vTile));
                }
            }
            for (int j = 0; j < k - 1; j++)
            {
                for (int i = 0; i < sides; i++)
                {
                    int a = start + j * (sides + 1) + i;
                    int b = a + sides + 1;
                    Quad(a, b, b + 1, a + 1);
                }
            }
        }

        /// Tube along a polyline (parallel-transport frames).
        public void Tube(IList<Vector3> pts, IList<float> radii, int sides, float uTile = 1f, float vTile = 1f, bool capEnd = false)
        {
            int n = pts.Count;
            if (n < 2) return;
            int start = V.Count;
            Vector3 prevN = Vector3.zero;
            float len = 0f;
            Vector3 lastT = Vector3.up;
            for (int i = 0; i < n; i++)
            {
                Vector3 t = i == 0 ? pts[1] - pts[0] : i == n - 1 ? pts[n - 1] - pts[n - 2] : pts[i + 1] - pts[i - 1];
                if (t.sqrMagnitude < 1e-12f) t = lastT;
                t.Normalize();
                lastT = t;
                Vector3 nn;
                if (i == 0)
                {
                    Vector3 refv = Mathf.Abs(t.y) < 0.95f ? Vector3.up : Vector3.right;
                    nn = Vector3.Cross(t, refv).normalized;
                }
                else
                {
                    nn = prevN - t * Vector3.Dot(prevN, t);
                    if (nn.sqrMagnitude < 1e-10f)
                    {
                        Vector3 refv = Mathf.Abs(t.y) < 0.95f ? Vector3.up : Vector3.right;
                        nn = Vector3.Cross(t, refv);
                    }
                    nn.Normalize();
                }
                prevN = nn;
                Vector3 b = Vector3.Cross(nn, t);
                if (i > 0) len += (pts[i] - pts[i - 1]).magnitude;
                for (int j = 0; j <= sides; j++)
                {
                    float a = j * Mathf.PI * 2f / sides;
                    Vector3 d = nn * Mathf.Cos(a) + b * Mathf.Sin(a);
                    Vert(pts[i] + d * radii[i], d, new Vector2((float)j / sides * uTile, len * vTile));
                }
            }
            for (int i = 0; i < n - 1; i++)
            {
                for (int j = 0; j < sides; j++)
                {
                    int a = start + i * (sides + 1) + j;
                    int b = a + sides + 1;
                    Quad(a, b, b + 1, a + 1);
                }
            }
            if (capEnd && radii[n - 1] > 0.0001f)
            {
                int ring = start + (n - 1) * (sides + 1);
                Vector3 tipLocal = pts[n - 1] + lastT * radii[n - 1] * 1.2f;
                int tip = Vert(tipLocal, lastT, new Vector2(0.5f, (len + radii[n - 1]) * vTile));
                for (int j = 0; j < sides; j++) Tri(ring + j, tip, ring + j + 1);
            }
        }

        /// Cube-sphere blob with a radial function. Prefer Ico() for the low-poly look.
        public void Blob(Vector3 c, Vector3 scale, Quaternion r, int res, Func<Vector3, float> radius, float uvTile = 1f)
        {
            if (res < 1) res = 1;
            for (int f = 0; f < 6; f++)
            {
                Vector3 n = BlobN[f], u = BlobU[f];
                Vector3 v = Vector3.Cross(u, n);
                int fs = V.Count;
                for (int j = 0; j <= res; j++)
                {
                    for (int i = 0; i <= res; i++)
                    {
                        Vector3 p = n + u * (2f * i / res - 1f) + v * (2f * j / res - 1f);
                        Vector3 d = p.normalized;
                        float rr = radius != null ? radius(d) : 1f;
                        if (float.IsNaN(rr) || float.IsInfinity(rr)) rr = 1f;
                        Vector3 pos = c + r * Vector3.Scale(d * rr, scale);
                        Vert(pos, r * d, new Vector2((float)i / res, (float)j / res) * uvTile);
                    }
                }
                for (int j = 0; j < res; j++)
                {
                    for (int i = 0; i < res; i++)
                    {
                        int a = fs + j * (res + 1) + i;
                        int b = a + res + 1;
                        Quad(a, b, b + 1, a + 1);
                    }
                }
            }
        }

        static readonly Vector3[] BlobN = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        static readonly Vector3[] BlobU = { Vector3.forward, Vector3.forward, Vector3.right, Vector3.right, Vector3.up, Vector3.up };

        /// Icosphere (detail 0 = 20 faces, 1 = 80, 2 = 320). radii (optional, one per vertex, see PropIco.VertexCount)
        /// jitters the vertices for chunky rocks, bushes and lumps. scale = half extents.
        public void Ico(Vector3 c, Vector3 scale, Quaternion r, int detail, float[] radii = null)
        {
            PropIco.Get(detail, out var dirs, out var tris);
            int start = V.Count;
            for (int i = 0; i < dirs.Length; i++)
            {
                Vector3 d = dirs[i];
                float rr = radii != null && i < radii.Length ? radii[i] : 1f;
                if (float.IsNaN(rr) || float.IsInfinity(rr) || rr <= 0f) rr = 1f;
                Vert(c + r * Vector3.Scale(d * rr, scale), r * d, new Vector2(d.x * 0.5f + 0.5f, d.y * 0.5f + 0.5f));
            }
            for (int i = 0; i < tris.Length; i += 3) Tri(start + tris[i], start + tris[i + 1], start + tris[i + 2]);
        }

        /// Low-poly torus (keep segR ~8-12, segr 3-4).
        public void Torus(Vector3 c, Quaternion r, float R, float rr, int segR, int segr, float uTile = 1f, float vTile = 1f)
        {
            int start = V.Count;
            Vector3 up = Vector3.up;
            for (int i = 0; i <= segR; i++)
            {
                float phi = i * Mathf.PI * 2f / segR;
                Vector3 dir = new Vector3(Mathf.Cos(phi), 0f, Mathf.Sin(phi));
                for (int j = 0; j <= segr; j++)
                {
                    float th = j * Mathf.PI * 2f / segr;
                    Vector3 n = dir * Mathf.Cos(th) + up * Mathf.Sin(th);
                    Vector3 p = dir * R + n * rr;
                    Vert(c + r * p, r * n, new Vector2((float)i / segR * uTile, (float)j / segr * vTile));
                }
            }
            for (int i = 0; i < segR; i++)
            {
                for (int j = 0; j < segr; j++)
                {
                    int a = start + i * (segr + 1) + j;
                    int b = a + 1;
                    int cc = a + segr + 2;
                    int d = a + segr + 1;
                    Quad(a, b, cc, d);
                }
            }
        }

        /// Tiny octahedron (bioluminescent specks, embers, berries).
        public void Octa(Vector3 c, float s) => Octa(c, new Vector3(s, s, s));

        public void Octa(Vector3 c, Vector3 s)
        {
            int px = Vert(c + new Vector3(s.x, 0, 0), Vector3.right, Vector2.zero);
            int nx = Vert(c + new Vector3(-s.x, 0, 0), Vector3.left, Vector2.zero);
            int py = Vert(c + new Vector3(0, s.y, 0), Vector3.up, Vector2.zero);
            int ny = Vert(c + new Vector3(0, -s.y, 0), Vector3.down, Vector2.zero);
            int pz = Vert(c + new Vector3(0, 0, s.z), Vector3.forward, Vector2.zero);
            int nz = Vert(c + new Vector3(0, 0, -s.z), Vector3.back, Vector2.zero);
            AutoTri(py, px, pz, c); AutoTri(py, pz, nx, c); AutoTri(py, nx, nz, c); AutoTri(py, nz, px, c);
            AutoTri(ny, pz, px, c); AutoTri(ny, nx, pz, c); AutoTri(ny, nz, nx, c); AutoTri(ny, px, nz, c);
        }

        /// Triangle on existing vertices, wound to face away from the given builder-space center.
        void AutoTri(int a, int b, int c, Vector3 centerLocal)
        {
            Vector3 cw = m.MultiplyPoint3x4(centerLocal);
            Vector3 fn = Vector3.Cross(V[b] - V[a], V[c] - V[a]);
            if (Vector3.Dot(fn, (V[a] + V[b] + V[c]) / 3f - cw) >= 0f) Tri(a, b, c); else Tri(a, c, b);
        }

        // ------------------------------------------------------------------ post processing
        static readonly Dictionary<Vector3Int, Vector3> WeldMap = new Dictionary<Vector3Int, Vector3>(1024);
        static readonly List<Vector3> Acc = new List<Vector3>(1024);

        static Vector3Int Q(Vector3 p) => new Vector3Int(Mathf.RoundToInt(p.x * 400f), Mathf.RoundToInt(p.y * 400f), Mathf.RoundToInt(p.z * 400f));

        /// Recomputes area-weighted vertex normals (only needed when vertices are displaced along N before ToMesh).
        public void SmoothNormals(int vStart, int tStart, bool weld = true)
        {
            int nv = V.Count - vStart;
            if (nv <= 0) return;
            Acc.Clear();
            for (int i = 0; i < nv; i++) Acc.Add(Vector3.zero);
            for (int t = tStart; t + 2 < T.Count; t += 3)
            {
                int a = T[t], b = T[t + 1], c = T[t + 2];
                if (a < vStart || b < vStart || c < vStart) continue;
                Vector3 fn = Vector3.Cross(V[b] - V[a], V[c] - V[a]);
                Acc[a - vStart] += fn; Acc[b - vStart] += fn; Acc[c - vStart] += fn;
            }
            if (weld)
            {
                WeldMap.Clear();
                for (int i = 0; i < nv; i++)
                {
                    var k = Q(V[vStart + i]);
                    WeldMap.TryGetValue(k, out var s);
                    WeldMap[k] = s + Acc[i];
                }
                for (int i = 0; i < nv; i++)
                {
                    Vector3 s = WeldMap[Q(V[vStart + i])];
                    N[vStart + i] = s.sqrMagnitude > 1e-20f ? s.normalized : N[vStart + i];
                }
            }
            else
            {
                for (int i = 0; i < nv; i++)
                {
                    Vector3 s = Acc[i];
                    if (s.sqrMagnitude > 1e-20f) N[vStart + i] = s.normalized;
                }
            }
        }

        /// Duplicates vertices/triangles added since the marks with flipped normals and winding (true two-sided geometry).
        public void DoubleSide(int vStart, int tStart)
        {
            int vEnd = V.Count, tEnd = T.Count;
            int off = vEnd - vStart;
            for (int i = vStart; i < vEnd; i++)
            {
                V.Add(V[i]); N.Add(-N[i]); UV.Add(UV[i]);
            }
            for (int t = tStart; t < tEnd; t += 3)
            {
                T.Add(T[t] + off); T.Add(T[t + 2] + off); T.Add(T[t + 1] + off);
            }
        }

        /// Appends another builder's geometry (already in its final space) transformed by mtx.
        public void Append(PropMeshBuilder other, Matrix4x4 mtx)
        {
            Matrix4x4 nmx = mtx.inverse.transpose;
            int off = V.Count;
            for (int i = 0; i < other.V.Count; i++)
            {
                V.Add(mtx.MultiplyPoint3x4(other.V[i]));
                Vector3 n = nmx.MultiplyVector(other.N[i]);
                N.Add(Unit(n, Vector3.up));
                UV.Add(other.UV[i]);
            }
            for (int i = 0; i < other.T.Count; i++) T.Add(other.T[i] + off);
        }

        public Bounds GetBounds()
        {
            if (V.Count == 0) return new Bounds();
            var b = new Bounds(V[0], Vector3.zero);
            for (int i = 1; i < V.Count; i++) b.Encapsulate(V[i]);
            return b;
        }

        static bool Finite(Vector3 p) =>
            !(float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z) || float.IsInfinity(p.x) || float.IsInfinity(p.y) || float.IsInfinity(p.z));

        // scratch lists for ToMesh (main thread only)
        static readonly List<Vector3> SV = new List<Vector3>(4096);
        static readonly List<Vector3> SN = new List<Vector3>(4096);
        static readonly List<Vector2> SU = new List<Vector2>(4096);
        static readonly List<int> ST = new List<int>(4096);

        /// Builds the mesh. flat = true (default): faceted flat shading (unwelded triangles with face normals);
        /// flat = false keeps the shared vertices (used for colliders). Invalid geometry is always removed.
        public Mesh ToMesh(string name, bool flat = true)
        {
            SV.Clear(); SN.Clear(); SU.Clear(); ST.Clear();
            if (flat)
            {
                for (int t = 0; t + 2 < T.Count; t += 3)
                {
                    int ia = T[t], ib = T[t + 1], ic = T[t + 2];
                    Vector3 a = V[ia], b = V[ib], c = V[ic];
                    if (!Finite(a) || !Finite(b) || !Finite(c)) continue;
                    Vector3 n = Vector3.Cross(b - a, c - a);
                    float l = n.magnitude;
                    if (!(l > 1e-10f) || float.IsInfinity(l)) continue;
                    n /= l;
                    int k = SV.Count;
                    SV.Add(a); SV.Add(b); SV.Add(c);
                    SN.Add(n); SN.Add(n); SN.Add(n);
                    SU.Add(UV[ia]); SU.Add(UV[ib]); SU.Add(UV[ic]);
                    ST.Add(k); ST.Add(k + 1); ST.Add(k + 2);
                }
            }
            else
            {
                for (int i = 0; i < V.Count; i++)
                {
                    Vector3 p = V[i];
                    SV.Add(Finite(p) ? p : Vector3.zero);
                    Vector3 n = N[i];
                    SN.Add(Finite(n) && n.sqrMagnitude > 1e-12f ? n : Vector3.up);
                    SU.Add(UV[i]);
                }
                for (int t = 0; t + 2 < T.Count; t += 3)
                {
                    int ia = T[t], ib = T[t + 1], ic = T[t + 2];
                    if (ia == ib || ib == ic || ia == ic) continue;
                    ST.Add(ia); ST.Add(ib); ST.Add(ic);
                }
            }
            var mesh = new Mesh { name = name, indexFormat = SV.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(SV);
            mesh.SetNormals(SN);
            mesh.SetUVs(0, SU);
            mesh.SetTriangles(ST, 0, true);
            return mesh;
        }
    }

    /// Cached unit icospheres (detail 0..2) with outward winding.
    public static class PropIco
    {
        static Vector3[][] dirs;
        static int[][] tris;

        public static int VertexCount(int detail)
        {
            Ensure();
            return dirs[Mathf.Clamp(detail, 0, 2)].Length;
        }

        public static Vector3[] Dirs(int detail)
        {
            Ensure();
            return dirs[Mathf.Clamp(detail, 0, 2)];
        }

        public static void Get(int detail, out Vector3[] d, out int[] t)
        {
            Ensure();
            int k = Mathf.Clamp(detail, 0, 2);
            d = dirs[k];
            t = tris[k];
        }

        static void Ensure()
        {
            if (dirs != null) return;
            dirs = new Vector3[3][];
            tris = new int[3][];
            float g = (1f + Mathf.Sqrt(5f)) * 0.5f;
            var v = new List<Vector3>
            {
                new Vector3(-1, g, 0), new Vector3(1, g, 0), new Vector3(-1, -g, 0), new Vector3(1, -g, 0),
                new Vector3(0, -1, g), new Vector3(0, 1, g), new Vector3(0, -1, -g), new Vector3(0, 1, -g),
                new Vector3(g, 0, -1), new Vector3(g, 0, 1), new Vector3(-g, 0, -1), new Vector3(-g, 0, 1),
            };
            for (int i = 0; i < v.Count; i++) v[i] = v[i].normalized;
            var t = new List<int>
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
                1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
                4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };
            Orient(v, t);
            dirs[0] = v.ToArray();
            tris[0] = t.ToArray();
            for (int level = 1; level <= 2; level++)
            {
                var nv = new List<Vector3>(v);
                var nt = new List<int>(t.Count * 4);
                var mid = new Dictionary<long, int>();
                int Mid(int a, int b)
                {
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (mid.TryGetValue(key, out int idx)) return idx;
                    nv.Add(((nv[a] + nv[b]) * 0.5f).normalized);
                    idx = nv.Count - 1;
                    mid[key] = idx;
                    return idx;
                }
                for (int i = 0; i < t.Count; i += 3)
                {
                    int a = t[i], b = t[i + 1], c = t[i + 2];
                    int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                    nt.Add(a); nt.Add(ab); nt.Add(ca);
                    nt.Add(b); nt.Add(bc); nt.Add(ab);
                    nt.Add(c); nt.Add(ca); nt.Add(bc);
                    nt.Add(ab); nt.Add(bc); nt.Add(ca);
                }
                Orient(nv, nt);
                dirs[level] = nv.ToArray();
                tris[level] = nt.ToArray();
                v = nv;
                t = nt;
            }
        }

        /// Makes every triangle wind so that cross(b - a, c - a) points away from the origin.
        static void Orient(List<Vector3> v, List<int> t)
        {
            for (int i = 0; i < t.Count; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), a + b + c) < 0f)
                {
                    int tmp = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = tmp;
                }
            }
        }
    }
}
