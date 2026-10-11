using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Mesh toolkit of the npc+story module. Characters are SMOOTH (L.13: round beans, smooth normals, many segments);
    /// vehicles and props built here are FACETED like the world (Flat()). Every mesh is built once, cached by key and
    /// shared – callers never destroy them. No Unity object is created per frame.
    public static class NpcMesh
    {
        static readonly Dictionary<string, Mesh> cache = new Dictionary<string, Mesh>();
        static readonly Dictionary<long, Material> mats = new Dictionary<long, Material>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { cache.Clear(); mats.Clear(); }

        public static Mesh Cached(string key, Func<Mesh> build)
        {
            if (cache.TryGetValue(key, out var m) && m != null) return m;
            m = build();
            if (m != null) m.name = "SB_Npc_" + key;
            cache[key] = m;
            return m;
        }

        // ------------------------------------------------------------------ materials
        /// Character cloth/skin: URP Lit with a soft sheen (L.13: neoprene 0.18, skin 0.10).
        public static Material Mat(Color c, float smoothness = 0.12f)
        {
            long k = Key(c, smoothness);
            if (mats.TryGetValue(k, out var m) && m != null) return m;
            m = MaterialLibrary.Opaque(c, smoothness, 0f);
            mats[k] = m;
            return m;
        }

        /// Matte world material (vehicles, props) – the shared low-poly material.
        public static Material World(Color c) => LowPoly.Mat(c);

        public static Material Glow(Color c, float intensity = 1.6f) => MaterialLibrary.Emissive(c, c, intensity);

        public static Material Glass(Color c) => MaterialLibrary.Transparent(c, 0.85f, true);

        static long Key(Color c, float s)
        {
            unchecked
            {
                long r = (long)Mathf.RoundToInt(c.r * 255f), g = Mathf.RoundToInt(c.g * 255f), b = Mathf.RoundToInt(c.b * 255f);
                long a = Mathf.RoundToInt(c.a * 255f), sm = Mathf.RoundToInt(s * 100f);
                return r | (g << 8) | (b << 16) | (a << 24) | (sm << 32);
            }
        }

        public static Color Hex(string hex) => LowPoly.Hex(hex);

        // ------------------------------------------------------------------ lathe
        /// Revolves a profile (x = radius, y = height, bottom → top) around +Y with smooth normals.
        /// sub(profileSegment, radialSegment) picks the submesh (null = all 0). Radial segment 0 starts at +X and runs
        /// counter-clockwise seen from above (segment segs/4 ≈ +Z = the front of a character).
        public static Mesh Lathe(Vector2[] prof, int segs, int subCount = 1, Func<int, int, int> sub = null)
        {
            int n = prof.Length;
            int ring = segs + 1;
            var verts = new Vector3[n * ring];
            var norms = new Vector3[verts.Length];
            var uvs = new Vector2[verts.Length];
            float total = 0f;
            var acc = new float[n];
            for (int i = 1; i < n; i++) { total += Vector2.Distance(prof[i], prof[i - 1]); acc[i] = total; }
            for (int i = 0; i < n; i++)
            {
                Vector2 a = prof[Mathf.Max(0, i - 1)], b = prof[Mathf.Min(n - 1, i + 1)];
                Vector2 t = b - a;
                Vector2 nn = new Vector2(t.y, -t.x);
                if (nn.sqrMagnitude < 1e-14f) nn = new Vector2(1f, 0f);
                nn.Normalize();
                if (prof[i].x <= 1e-5f) nn = new Vector2(0f, nn.y >= 0f ? 1f : -1f);
                for (int j = 0; j <= segs; j++)
                {
                    float ang = j / (float)segs * Mathf.PI * 2f;
                    float c = Mathf.Cos(ang), s = Mathf.Sin(ang);
                    int k = i * ring + j;
                    verts[k] = new Vector3(c * prof[i].x, prof[i].y, s * prof[i].x);
                    norms[k] = new Vector3(c * nn.x, nn.y, s * nn.x);
                    uvs[k] = new Vector2(j / (float)segs, total > 0f ? acc[i] / total : 0f);
                }
            }
            var tris = new List<int>[Mathf.Max(1, subCount)];
            for (int q = 0; q < tris.Length; q++) tris[q] = new List<int>(n * segs * 6 / tris.Length + 6);
            for (int i = 0; i < n - 1; i++)
            {
                if ((prof[i] - prof[i + 1]).sqrMagnitude < 1e-12f) continue;
                for (int j = 0; j < segs; j++)
                {
                    int q = sub != null ? Mathf.Clamp(sub(i, j), 0, tris.Length - 1) : 0;
                    int A = i * ring + j, B = (i + 1) * ring + j, C = A + 1, D = B + 1;
                    var l = tris[q];
                    l.Add(A); l.Add(B); l.Add(D);
                    l.Add(A); l.Add(D); l.Add(C);
                }
            }
            var m = new Mesh();
            if (verts.Length > 65000) m.indexFormat = IndexFormat.UInt32;
            m.vertices = verts;
            m.normals = norms;
            m.uv = uvs;
            m.subMeshCount = tris.Length;
            for (int q = 0; q < tris.Length; q++) m.SetTriangles(tris[q], q, false);
            m.RecalculateBounds();
            return m;
        }

        /// Smooth profile through control points (Catmull-Rom, `steps` samples per span). Keeps the end points.
        public static Vector2[] Smooth(Vector2[] ctrl, int steps)
        {
            if (ctrl == null || ctrl.Length < 3 || steps <= 1) return ctrl;
            var o = new List<Vector2>(ctrl.Length * steps);
            for (int i = 0; i < ctrl.Length - 1; i++)
            {
                Vector2 p0 = ctrl[Mathf.Max(0, i - 1)], p1 = ctrl[i], p2 = ctrl[i + 1], p3 = ctrl[Mathf.Min(ctrl.Length - 1, i + 2)];
                for (int s = 0; s < steps; s++)
                {
                    float t = s / (float)steps, t2 = t * t, t3 = t2 * t;
                    Vector2 v = 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
                    if (v.x < 0f) v.x = 0f;
                    o.Add(v);
                }
            }
            o.Add(ctrl[ctrl.Length - 1]);
            return o.ToArray();
        }

        // ------------------------------------------------------------------ primitives (unit or sized)
        /// Unit sphere (diameter 1) centred at the origin.
        public static Mesh Sphere(int rings = 14, int segs = 24) => Cached("sph" + rings + "_" + segs, () =>
        {
            var p = new Vector2[rings + 1];
            for (int i = 0; i <= rings; i++)
            {
                float a = Mathf.PI * i / rings;
                p[i] = new Vector2(i == 0 || i == rings ? 0f : 0.5f * Mathf.Sin(a), -0.5f * Mathf.Cos(a));
            }
            return Lathe(p, segs);
        });

        /// Upper half sphere (dome), radius 0.5, flat side at y = 0 closed with a cap.
        public static Mesh Dome(int rings = 8, int segs = 24) => Cached("dome" + rings + "_" + segs, () =>
        {
            var p = new Vector2[rings + 2];
            p[0] = new Vector2(0f, 0f);
            for (int i = 0; i <= rings; i++)
            {
                float a = Mathf.PI * 0.5f * i / rings;
                p[i + 1] = new Vector2(i == rings ? 0f : 0.5f * Mathf.Cos(a), 0.5f * Mathf.Sin(a));
            }
            return Lathe(p, segs);
        });

        /// Capsule along +Y from 0 to length, radius r, round caps.
        public static Mesh Capsule(float r, float length, int segs = 16) =>
            Cached("cap" + F(r) + "_" + F(length) + "_" + segs, () =>
            {
                const int cap = 5;
                r = Mathf.Min(r, length * 0.5f);
                var p = new List<Vector2>();
                for (int i = 0; i <= cap; i++)
                {
                    float a = Mathf.PI * 0.5f * i / cap;
                    p.Add(new Vector2(i == 0 ? 0f : r * Mathf.Sin(a), r - r * Mathf.Cos(a)));
                }
                for (int i = 0; i <= cap; i++)
                {
                    float a = Mathf.PI * 0.5f * i / cap;
                    p.Add(new Vector2(i == cap ? 0f : r * Mathf.Cos(a), length - r + r * Mathf.Sin(a)));
                }
                return Lathe(p.ToArray(), segs);
            });

        /// Tapered limb along −Y (from the joint down) with round ends: radius r0 at the top, r1 at the bottom.
        public static Mesh Limb(float r0, float r1, float length, int segs = 16) =>
            Cached("limb" + F(r0) + "_" + F(r1) + "_" + F(length) + "_" + segs, () =>
            {
                const int cap = 4;
                var p = new List<Vector2>();
                // bottom cap (y = -length)
                for (int i = 0; i <= cap; i++)
                {
                    float a = Mathf.PI * 0.5f * i / cap;
                    p.Add(new Vector2(i == 0 ? 0f : r1 * Mathf.Sin(a), -length - r1 * Mathf.Cos(a) + r1 * 0.2f));
                }
                for (int i = 0; i <= cap; i++)
                {
                    float a = Mathf.PI * 0.5f * i / cap;
                    p.Add(new Vector2(i == cap ? 0f : r0 * Mathf.Cos(a), r0 * Mathf.Sin(a) * 0.9f));
                }
                return Lathe(p.ToArray(), segs);
            });

        /// Cylinder along +Y (0..h), radius r (rb at the bottom when given), closed caps.
        public static Mesh Cylinder(float r, float h, int segs = 20, float rb = -1f) =>
            Cached("cyl" + F(r) + "_" + F(h) + "_" + segs + "_" + F(rb), () =>
            {
                float r0 = rb > 0f ? rb : r;
                var p = new[] { new Vector2(0f, 0f), new Vector2(r0 * 0.98f, 0f), new Vector2(r0, 0.004f), new Vector2(r, h - 0.004f), new Vector2(r * 0.98f, h), new Vector2(0f, h) };
                return Lathe(p, segs);
            });

        /// Cone along +Y (base radius r at y = 0, apex at y = h).
        public static Mesh Cone(float r, float h, int segs = 16) =>
            Cached("cone" + F(r) + "_" + F(h) + "_" + segs, () => Lathe(new[] { new Vector2(0f, 0f), new Vector2(r, 0f), new Vector2(0f, h) }, segs));

        /// Torus in the XZ plane.
        public static Mesh Torus(float major, float minor, int segs = 24, int sides = 10) =>
            Cached("tor" + F(major) + "_" + F(minor) + "_" + segs + "_" + sides, () =>
            {
                int rs = segs + 1, ss = sides + 1;
                var v = new Vector3[rs * ss];
                var nrm = new Vector3[v.Length];
                for (int i = 0; i < rs; i++)
                {
                    float a = i / (float)segs * Mathf.PI * 2f;
                    Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    for (int j = 0; j < ss; j++)
                    {
                        float b = j / (float)sides * Mathf.PI * 2f;
                        Vector3 n = dir * Mathf.Cos(b) + Vector3.up * Mathf.Sin(b);
                        v[i * ss + j] = dir * major + n * minor;
                        nrm[i * ss + j] = n;
                    }
                }
                var t = new List<int>(segs * sides * 6);
                for (int i = 0; i < segs; i++)
                    for (int j = 0; j < sides; j++)
                    {
                        int A = i * ss + j, B = A + 1, C = (i + 1) * ss + j, D = C + 1;
                        t.Add(A); t.Add(B); t.Add(D);
                        t.Add(A); t.Add(D); t.Add(C);
                    }
                var m = new Mesh { vertices = v, normals = nrm };
                m.SetTriangles(t, 0);
                m.RecalculateBounds();
                return m;
            });

        /// Rounded box (superellipsoid-like): size w × h × d, roundness 0 (box) … 1 (ellipsoid). Smooth normals.
        public static Mesh RoundBox(float w, float h, float d, float round = 0.25f, int res = 8) =>
            Cached("rbox" + F(w) + "_" + F(h) + "_" + F(d) + "_" + F(round) + "_" + res, () =>
            {
                var verts = new List<Vector3>();
                var tris = new List<int>();
                float e = Mathf.Lerp(0.18f, 1f, Mathf.Clamp01(round));
                Vector3 half = new Vector3(w, h, d) * 0.5f;
                // 6 faces of a subdivided cube, pushed to the superellipsoid surface
                for (int face = 0; face < 6; face++)
                {
                    Vector3 n = FaceN(face), u = FaceU(face), vv = Vector3.Cross(n, u);
                    int start = verts.Count;
                    for (int y = 0; y <= res; y++)
                        for (int x = 0; x <= res; x++)
                        {
                            Vector3 c = n + u * (x / (float)res * 2f - 1f) + vv * (y / (float)res * 2f - 1f);
                            Vector3 s = c.normalized;
                            // superellipsoid: e = 1 sphere, e → 0 box (extent 1 along every axis either way)
                            Vector3 p = new Vector3(SignPow(s.x, e), SignPow(s.y, e), SignPow(s.z, e));
                            verts.Add(Vector3.Scale(p, half));
                        }
                    for (int y = 0; y < res; y++)
                        for (int x = 0; x < res; x++)
                        {
                            int a = start + y * (res + 1) + x, b = a + 1, c = a + res + 1, dd = c + 1;
                            tris.Add(a); tris.Add(c); tris.Add(dd);
                            tris.Add(a); tris.Add(dd); tris.Add(b);
                        }
                }
                var m = new Mesh();
                m.SetVertices(verts);
                m.SetTriangles(tris, 0);
                FixWindingOutward(m);
                m.RecalculateBounds();
                return m;
            });

        static float SignPow(float v, float e) => Mathf.Sign(v) * Mathf.Pow(Mathf.Abs(v), e);

        static Vector3 FaceN(int f)
        {
            switch (f) { case 0: return Vector3.right; case 1: return Vector3.left; case 2: return Vector3.up; case 3: return Vector3.down; case 4: return Vector3.forward; default: return Vector3.back; }
        }

        static Vector3 FaceU(int f)
        {
            switch (f) { case 0: case 1: return Vector3.forward; case 2: case 3: return Vector3.right; default: return Vector3.up; }
        }

        /// Flat shape in the XY plane (front faces −Z), extruded by depth and centred on z = 0. Outline counter-clockwise.
        public static Mesh Plate(string key, Vector2[] outline, float depth) => Cached("plate_" + key, () =>
        {
            int n = outline.Length;
            Vector2 c = Vector2.zero;
            for (int i = 0; i < n; i++) c += outline[i];
            c /= n;
            float h = depth * 0.5f;
            var v = new List<Vector3>();
            var nr = new List<Vector3>();
            var t = new List<int>();
            for (int side = 0; side < 2; side++)
            {
                float z = side == 0 ? -h : h;
                Vector3 nn = side == 0 ? Vector3.back : Vector3.forward;
                int start = v.Count;
                v.Add(new Vector3(c.x, c.y, z)); nr.Add(nn);
                for (int i = 0; i < n; i++) { v.Add(new Vector3(outline[i].x, outline[i].y, z)); nr.Add(nn); }
                for (int i = 0; i < n; i++)
                {
                    int a = start + 1 + i, b = start + 1 + (i + 1) % n;
                    t.Add(start); t.Add(a); t.Add(b);
                }
            }
            for (int i = 0; i < n; i++)
            {
                Vector2 p0 = outline[i], p1 = outline[(i + 1) % n];
                Vector2 e = p1 - p0;
                Vector3 nn = new Vector3(e.y, -e.x, 0f).normalized;
                if (Vector2.Dot(new Vector2(nn.x, nn.y), (p0 + p1) * 0.5f - c) < 0f) nn = -nn;
                int s = v.Count;
                v.Add(new Vector3(p0.x, p0.y, -h)); v.Add(new Vector3(p1.x, p1.y, -h));
                v.Add(new Vector3(p1.x, p1.y, h)); v.Add(new Vector3(p0.x, p0.y, h));
                nr.Add(nn); nr.Add(nn); nr.Add(nn); nr.Add(nn);
                t.Add(s); t.Add(s + 1); t.Add(s + 2); t.Add(s); t.Add(s + 2); t.Add(s + 3);
            }
            var m = new Mesh();
            m.SetVertices(v);
            m.SetNormals(nr);
            FixWinding(v, nr, t);
            m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        });

        /// Ellipse outline (for Plate).
        public static Vector2[] Ellipse(float rx, float ry, int n)
        {
            var pts = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                float a = Mathf.PI * 2f * i / n;
                pts[i] = new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
            }
            return pts;
        }

        /// Smile / arc band in the XY plane facing −Z (mouths, brows). curve > 0 = smile (ends up).
        public static Mesh Arc(string key, float width, float thickness, float curve, float depth = 0.006f, int steps = 12) =>
            Cached("arc_" + key, () =>
            {
                var outline = new List<Vector2>();
                for (int i = 0; i <= steps; i++)
                {
                    float u = i / (float)steps, x = (u - 0.5f) * width;
                    float y = curve * (4f * (u - 0.5f) * (u - 0.5f)) - curve * 0.5f;
                    outline.Add(new Vector2(x, y - thickness * 0.5f * Mathf.Sin(u * Mathf.PI) - thickness * 0.15f));
                }
                for (int i = steps; i >= 0; i--)
                {
                    float u = i / (float)steps, x = (u - 0.5f) * width;
                    float y = curve * (4f * (u - 0.5f) * (u - 0.5f)) - curve * 0.5f;
                    outline.Add(new Vector2(x, y + thickness * 0.5f * Mathf.Sin(u * Mathf.PI) + thickness * 0.15f));
                }
                return BandMesh(outline, depth);
            });

        /// Strip outline (two rows: first half bottom left→right, second half top right→left) extruded – non-convex safe.
        static Mesh BandMesh(List<Vector2> outline, float depth)
        {
            int n = outline.Count / 2;
            float h = depth * 0.5f;
            var v = new List<Vector3>();
            var nr = new List<Vector3>();
            var t = new List<int>();
            for (int side = 0; side < 2; side++)
            {
                float z = side == 0 ? -h : h;
                Vector3 nn = side == 0 ? Vector3.back : Vector3.forward;
                int s0 = v.Count;
                for (int i = 0; i < outline.Count; i++) { v.Add(new Vector3(outline[i].x, outline[i].y, z)); nr.Add(nn); }
                for (int i = 0; i < n - 1; i++)
                {
                    int b0 = s0 + i, b1 = s0 + i + 1, t0 = s0 + outline.Count - 1 - i, t1 = s0 + outline.Count - 2 - i;
                    t.Add(b0); t.Add(t0); t.Add(t1);
                    t.Add(b0); t.Add(t1); t.Add(b1);
                }
            }
            var m = new Mesh();
            m.SetVertices(v);
            m.SetNormals(nr);
            FixWinding(v, nr, t);
            m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }

        /// Swept tube along a polyline (moustache tufts, straps, chains, cables). Smooth normals.
        public static Mesh Sweep(string key, Vector3[] path, float r0, float r1, int sides = 10) => Cached("sweep_" + key, () =>
        {
            int n = path.Length;
            var v = new List<Vector3>();
            var nr = new List<Vector3>();
            var t = new List<int>();
            Vector3 prevSide = Vector3.zero;
            for (int i = 0; i < n; i++)
            {
                Vector3 fwd = (path[Mathf.Min(n - 1, i + 1)] - path[Mathf.Max(0, i - 1)]).normalized;
                if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
                Vector3 side = i == 0 ? Vector3.Cross(fwd, Mathf.Abs(fwd.y) > 0.9f ? Vector3.right : Vector3.up).normalized
                                      : Vector3.Cross(Vector3.Cross(fwd, prevSide), fwd).normalized;
                if (side.sqrMagnitude < 1e-6f) side = Vector3.right;
                prevSide = side;
                Vector3 up = Vector3.Cross(side, fwd);
                float u = n > 1 ? i / (float)(n - 1) : 0f;
                float r = Mathf.Lerp(r0, r1, u) * (i == 0 || i == n - 1 ? 0.35f : 1f);
                for (int s = 0; s <= sides; s++)
                {
                    float a = s / (float)sides * Mathf.PI * 2f;
                    Vector3 dir = side * Mathf.Cos(a) + up * Mathf.Sin(a);
                    v.Add(path[i] + dir * r);
                    nr.Add(dir);
                }
            }
            for (int i = 0; i < n - 1; i++)
                for (int s = 0; s < sides; s++)
                {
                    int a = i * (sides + 1) + s, b = a + 1, c = a + sides + 1, d = c + 1;
                    t.Add(a); t.Add(c); t.Add(d);
                    t.Add(a); t.Add(d); t.Add(b);
                }
            var m = new Mesh();
            m.SetVertices(v);
            m.SetNormals(nr);
            FixWinding(v, nr, t);
            m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        });

        /// Quad in the XY plane facing −Z, centred.
        public static Mesh Quad(float w, float h) => Cached("quad" + F(w) + "_" + F(h), () =>
        {
            var m = new Mesh();
            m.vertices = new[] { new Vector3(-w * 0.5f, -h * 0.5f, 0f), new Vector3(-w * 0.5f, h * 0.5f, 0f), new Vector3(w * 0.5f, h * 0.5f, 0f), new Vector3(w * 0.5f, -h * 0.5f, 0f) };
            m.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            m.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            m.RecalculateBounds();
            return m;
        });

        /// Faceted copy (world style) of a cached mesh.
        public static Mesh Flat(Mesh smooth, string key) => Cached("flat_" + key, () => LowPoly.Flatten(smooth));

        // ------------------------------------------------------------------ helpers
        static void FixWinding(List<Vector3> v, List<Vector3> nr, List<int> t)
        {
            for (int i = 0; i + 2 < t.Count; i += 3)
            {
                Vector3 f = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
                Vector3 avg = nr[t[i]] + nr[t[i + 1]] + nr[t[i + 2]];
                if (Vector3.Dot(f, avg) < 0f) { int tmp = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = tmp; }
            }
        }

        /// Flips triangles whose normal points toward the mesh centre (closed convex-ish meshes).
        static void FixWindingOutward(Mesh m)
        {
            var v = m.vertices;
            var t = m.triangles;
            for (int i = 0; i + 2 < t.Length; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                Vector3 f = Vector3.Cross(b - a, c - a);
                if (Vector3.Dot(f, a + b + c) < 0f) { int tmp = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = tmp; }
            }
            m.triangles = t;
            m.RecalculateNormals();
            WeldNormals(m);
        }

        /// Averages normals of vertices at the same position (cube-face seams of RoundBox).
        static void WeldNormals(Mesh m)
        {
            var v = m.vertices;
            m.RecalculateNormals();
            var n = m.normals;
            var map = new Dictionary<Vector3Int, Vector3>();
            for (int i = 0; i < v.Length; i++)
            {
                var k = new Vector3Int(Mathf.RoundToInt(v[i].x * 2000f), Mathf.RoundToInt(v[i].y * 2000f), Mathf.RoundToInt(v[i].z * 2000f));
                map.TryGetValue(k, out var s);
                map[k] = s + n[i];
            }
            for (int i = 0; i < v.Length; i++)
            {
                var k = new Vector3Int(Mathf.RoundToInt(v[i].x * 2000f), Mathf.RoundToInt(v[i].y * 2000f), Mathf.RoundToInt(v[i].z * 2000f));
                n[i] = map[k].normalized;
            }
            m.normals = n;
        }

        static string F(float v) => v.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

        // ------------------------------------------------------------------ scene helpers
        public static Transform Node(Transform parent, string name, Vector3 localPos, Vector3 euler = default)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            t.localRotation = Quaternion.Euler(euler);
            return t;
        }

        public static MeshRenderer Part(Transform parent, string name, Mesh mesh, Material mat, Vector3 pos, Vector3 scale, Vector3 euler = default, bool shadows = true)
        {
            var go = new GameObject(name);
            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            t.localRotation = Quaternion.Euler(euler);
            t.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            r.receiveShadows = true;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return r;
        }

        public static MeshRenderer Part(Transform parent, string name, Mesh mesh, Material mat, Vector3 pos) =>
            Part(parent, name, mesh, mat, pos, Vector3.one);
    }
}
