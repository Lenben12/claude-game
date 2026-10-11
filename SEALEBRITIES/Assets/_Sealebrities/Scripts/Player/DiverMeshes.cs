using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Smooth-shaded low-poly meshes for the bean divers, the first-person mittens and the held items
    /// (art bible §1.12/§1.13: characters, hands and items are soft blobs with smooth normals, the world stays faceted).
    /// Every mesh is built once, cached by key and shared – callers never destroy them.
    public static class DiverMeshes
    {
        static readonly Dictionary<string, Mesh> cache = new Dictionary<string, Mesh>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { cache.Clear(); }

        /// Returns the cached mesh for key, building it on first use.
        public static Mesh Cached(string key, Func<Mesh> build)
        {
            if (cache.TryGetValue(key, out var m) && m != null) return m;
            m = build();
            if (m != null) m.name = "SB_Diver_" + key;
            cache[key] = m;
            return m;
        }

        // ------------------------------------------------------------------ lathe
        /// Revolves a profile (x = radius, y = height, ordered bottom → top) around +Y with smooth normals.
        /// sub(profileSegment, radialSegment) picks the submesh of each quad (null = all in submesh 0).
        /// UV: u = angle 0..1, v = arc length 0..1 along the profile.
        public static Mesh LatheY(Vector2[] prof, int segs, int subCount = 1, Func<int, int, int> sub = null)
        {
            int n = prof.Length;
            int ring = segs + 1;
            var verts = new Vector3[n * ring];
            var norms = new Vector3[verts.Length];
            var uvs = new Vector2[verts.Length];
            var acc = new float[n];
            float total = 0f;
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
            m.vertices = verts;
            m.normals = norms;
            m.uv = uvs;
            m.subMeshCount = tris.Length;
            for (int q = 0; q < tris.Length; q++) m.SetTriangles(tris[q], q, false);
            m.RecalculateBounds();
            return m;
        }

        /// Unit-diameter sphere (radius 0.5) centered at the origin.
        public static Mesh Sphere(int rings = 7, int segs = 12) => Cached("sphere" + rings + "_" + segs, () =>
        {
            var p = new Vector2[rings + 1];
            for (int i = 0; i <= rings; i++)
            {
                float a = Mathf.PI * i / rings;
                p[i] = new Vector2(i == 0 || i == rings ? 0f : 0.5f * Mathf.Sin(a), -0.5f * Mathf.Cos(a));
            }
            return LatheY(p, segs);
        });

        /// Capsule along +Y from y = 0 to y = length with round caps (actual size, not unit).
        public static Mesh Capsule(float radius, float length, int segs = 10) =>
            Cached("caps" + radius.ToString("0.###") + "_" + length.ToString("0.###") + "_" + segs, () =>
            {
                const int cap = 3;
                float r = Mathf.Min(radius, length * 0.5f);
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
                return LatheY(p.ToArray(), segs);
            });

        /// L.13 smooth capsule along +Y from y = 0 to y = length: `segs` radial segments and `capRings` rings per round
        /// cap (the old Capsule has 3) – used for the limbs (16 × 8 rings) so bends and silhouettes never show facets.
        public static Mesh SmoothCapsule(float radius, float length, int segs = 16, int capRings = 5, int bodyRings = 2) =>
            Cached("scaps" + radius.ToString("0.####") + "_" + length.ToString("0.####") + "_" + segs + "_" + capRings + "_" + bodyRings, () =>
            {
                float r = Mathf.Min(radius, length * 0.5f);
                var p = new List<Vector2>(capRings * 2 + bodyRings + 2);
                for (int i = 0; i <= capRings; i++)
                {
                    float a = Mathf.PI * 0.5f * i / capRings;
                    p.Add(new Vector2(i == 0 ? 0f : r * Mathf.Sin(a), r - r * Mathf.Cos(a)));
                }
                // straight part split into a few rings (smooth bending under per-vertex lighting)
                for (int i = 1; i < bodyRings; i++)
                    p.Add(new Vector2(r, Mathf.Lerp(r, length - r, i / (float)bodyRings)));
                for (int i = 0; i <= capRings; i++)
                {
                    float a = Mathf.PI * 0.5f * i / capRings;
                    p.Add(new Vector2(i == capRings ? 0f : r * Mathf.Cos(a), length - r + r * Mathf.Sin(a)));
                }
                return LatheY(p.ToArray(), segs);
            });

        /// L.13: resamples a lathe profile (bottom → top) to `rings` points along a centripetal Catmull-Rom spline, so a
        /// hand-tuned 9–13 point profile becomes a round 18–24 ring silhouette. End points (radius 0 poles) are kept.
        public static Vector2[] Resample(Vector2[] prof, int rings)
        {
            int n = prof.Length;
            if (n < 3 || rings <= n) return prof;
            // arc length of the polyline
            var acc = new float[n];
            for (int i = 1; i < n; i++) acc[i] = acc[i - 1] + Vector2.Distance(prof[i], prof[i - 1]);
            float total = acc[n - 1];
            if (total <= 1e-6f) return prof;
            var outp = new Vector2[rings];
            int seg = 0;
            for (int k = 0; k < rings; k++)
            {
                float s = total * k / (rings - 1);
                while (seg < n - 2 && acc[seg + 1] < s) seg++;
                float len = Mathf.Max(1e-6f, acc[seg + 1] - acc[seg]);
                float t = Mathf.Clamp01((s - acc[seg]) / len);
                Vector2 p0 = prof[Mathf.Max(0, seg - 1)], p1 = prof[seg], p2 = prof[seg + 1], p3 = prof[Mathf.Min(n - 1, seg + 2)];
                Vector2 v = CatmullRom(p0, p1, p2, p3, t);
                v.x = Mathf.Max(0f, v.x);
                outp[k] = v;
            }
            outp[0] = prof[0];
            outp[rings - 1] = prof[n - 1];
            return outp;
        }

        static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        /// Tapered tube along +Y (y = 0 radius r0 → y = 1 radius r1, flat caps). Scale Y for the length.
        public static Mesh Taper(float r0, float r1, int segs = 10, bool caps = true) =>
            Cached("taper" + r0.ToString("0.####") + "_" + r1.ToString("0.####") + "_" + segs + (caps ? "c" : ""), () =>
            {
                var p = caps
                    ? new[] { new Vector2(0f, 0f), new Vector2(r0 * 0.7f, 0f), new Vector2(r0, 0.02f), new Vector2(r1, 0.98f), new Vector2(r1 * 0.7f, 1f), new Vector2(0f, 1f) }
                    : new[] { new Vector2(r0, 0f), new Vector2(r1, 1f) };
                return LatheY(p, segs);
            });

        /// Torus around +Y (ring in the XZ plane).
        public static Mesh Torus(float major, float minor, int segs = 16, int sides = 6) =>
            Cached("torus" + major.ToString("0.###") + "_" + minor.ToString("0.###") + "_" + segs + "_" + sides, () =>
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

        /// Flat shape in the XY plane facing -Z (front) and +Z (back), extruded by depth (centered on z = 0).
        /// outline: convex or star-shaped around its centroid, counter-clockwise when seen from -Z.
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
            // front (-Z) and back (+Z) fans
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
                    if (side == 0) { t.Add(start); t.Add(b); t.Add(a); }
                    else { t.Add(start); t.Add(a); t.Add(b); }
                }
            }
            // rim
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
                // orientation-independent winding: pick the one facing outward
                Vector3 f = Vector3.Cross(v[s + 1] - v[s], v[s + 2] - v[s]);
                if (Vector3.Dot(f, nn) < 0f) { t.Add(s); t.Add(s + 1); t.Add(s + 2); t.Add(s); t.Add(s + 2); t.Add(s + 3); }
                else { t.Add(s); t.Add(s + 2); t.Add(s + 1); t.Add(s); t.Add(s + 3); t.Add(s + 2); }
            }
            var m = new Mesh();
            m.SetVertices(v);
            m.SetNormals(nr);
            m.SetTriangles(FixWinding(v, nr, t), 0);
            m.RecalculateBounds();
            return m;
        });

        /// Flat crescent / band between two arcs in the XY plane (front -Z), used for mouths.
        public static Mesh Crescent(string key, float width, float outerDepth, float innerDepth, float thickness, int steps = 8) =>
            Cached("cres_" + key, () =>
            {
                var v = new List<Vector3>();
                var nr = new List<Vector3>();
                var t = new List<int>();
                for (int i = 0; i <= steps; i++)
                {
                    float u = i / (float)steps;
                    float x = (u - 0.5f) * width;
                    float s = Mathf.Sin(u * Mathf.PI);
                    v.Add(new Vector3(x, -outerDepth * s, -thickness)); nr.Add(Vector3.back);
                    v.Add(new Vector3(x, -innerDepth * s, -thickness)); nr.Add(Vector3.back);
                }
                for (int i = 0; i < steps; i++)
                {
                    int a = i * 2;
                    t.Add(a); t.Add(a + 1); t.Add(a + 3);
                    t.Add(a); t.Add(a + 3); t.Add(a + 2);
                }
                var m = new Mesh();
                m.SetVertices(v);
                m.SetNormals(nr);
                // make sure the front faces -Z
                for (int i = 0; i + 2 < t.Count; i += 3)
                {
                    Vector3 f = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
                    if (f.z > 0f) { int tmp = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = tmp; }
                }
                m.SetTriangles(t, 0);
                m.RecalculateBounds();
                return m;
            });

        /// Rounded rectangle slab (phone body): width (X) x height (Y) x depth (Z), corner radius r.
        /// Submesh 0 = front face (-Z), submesh 1 = back face (+Z) and sides. UVs: front/back map XY to 0..1.
        public static Mesh RoundedSlab(string key, float w, float hgt, float depth, float r, int cornerSteps = 3) =>
            Cached("slab_" + key, () =>
            {
                var outline = new List<Vector2>();
                float hw = w * 0.5f - r, hh = hgt * 0.5f - r;
                Vector2[] centers = { new Vector2(hw, hh), new Vector2(-hw, hh), new Vector2(-hw, -hh), new Vector2(hw, -hh) };
                for (int c = 0; c < 4; c++)
                    for (int s = 0; s <= cornerSteps; s++)
                    {
                        float a = (c * 90f + s * 90f / cornerSteps) * Mathf.Deg2Rad;
                        outline.Add(centers[c] + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
                    }
                int n = outline.Count;
                float hz = depth * 0.5f;
                var v = new List<Vector3>();
                var nr = new List<Vector3>();
                var uv = new List<Vector2>();
                var front = new List<int>();
                var back = new List<int>();
                for (int side = 0; side < 2; side++)
                {
                    float z = side == 0 ? -hz : hz;
                    Vector3 nn = side == 0 ? Vector3.back : Vector3.forward;
                    int st = v.Count;
                    v.Add(new Vector3(0f, 0f, z)); nr.Add(nn); uv.Add(new Vector2(0.5f, 0.5f));
                    for (int i = 0; i < n; i++)
                    {
                        v.Add(new Vector3(outline[i].x, outline[i].y, z)); nr.Add(nn);
                        uv.Add(new Vector2(outline[i].x / w + 0.5f, outline[i].y / hgt + 0.5f));
                    }
                    var l = side == 0 ? front : back;
                    for (int i = 0; i < n; i++)
                    {
                        int a = st + 1 + i, b = st + 1 + (i + 1) % n;
                        // outline is counter-clockwise seen from +Z → clockwise seen from -Z
                        if (side == 0) { l.Add(st); l.Add(a); l.Add(b); }
                        else { l.Add(st); l.Add(b); l.Add(a); }
                    }
                }
                // smooth sides
                int s0 = v.Count;
                for (int i = 0; i < n; i++)
                {
                    Vector2 prev = outline[(i - 1 + n) % n], next = outline[(i + 1) % n];
                    Vector2 e = next - prev;
                    Vector3 nn = new Vector3(e.y, -e.x, 0f).normalized;
                    v.Add(new Vector3(outline[i].x, outline[i].y, -hz)); nr.Add(nn); uv.Add(new Vector2(i / (float)n, 0f));
                    v.Add(new Vector3(outline[i].x, outline[i].y, hz)); nr.Add(nn); uv.Add(new Vector2(i / (float)n, 1f));
                }
                for (int i = 0; i < n; i++)
                {
                    int a = s0 + i * 2, b = s0 + ((i + 1) % n) * 2;
                    back.Add(a); back.Add(a + 1); back.Add(b + 1);
                    back.Add(a); back.Add(b + 1); back.Add(b);
                }
                var m = new Mesh();
                m.SetVertices(v);
                m.SetNormals(nr);
                m.SetUVs(0, uv);
                m.subMeshCount = 2;
                m.SetTriangles(FixWinding(v, nr, front), 0);
                m.SetTriangles(FixWinding(v, nr, back), 1);
                m.RecalculateBounds();
                return m;
            });

        /// Flips triangles whose geometric normal points against the stored vertex normal.
        static List<int> FixWinding(List<Vector3> v, List<Vector3> nr, List<int> t)
        {
            for (int i = 0; i + 2 < t.Count; i += 3)
            {
                Vector3 f = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
                Vector3 avg = nr[t[i]] + nr[t[i + 1]] + nr[t[i + 2]];
                if (Vector3.Dot(f, avg) < 0f) { int tmp = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = tmp; }
            }
            return t;
        }

        /// Quad in the XY plane facing -Z, centered, with UV 0..1 (phone screen).
        public static Mesh Quad(string key, float w, float h) => Cached("quad_" + key, () =>
        {
            var m = new Mesh();
            m.vertices = new[] { new Vector3(-w * 0.5f, -h * 0.5f, 0f), new Vector3(-w * 0.5f, h * 0.5f, 0f), new Vector3(w * 0.5f, h * 0.5f, 0f), new Vector3(w * 0.5f, -h * 0.5f, 0f) };
            m.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            m.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            m.RecalculateBounds();
            return m;
        });

        /// Open cone along +Z (apex at the origin), length 1, end radius 1; UV v runs 0 (apex) → 1 (end). Light beams.
        public static Mesh Beam(int segs = 14) => Cached("beam" + segs, () =>
        {
            var p = new[] { new Vector2(0.0001f, 0f), new Vector2(1f, 1f) };
            var m = LatheY(p, segs);
            // rotate Y-up lathe to +Z
            var v = m.vertices;
            var n = m.normals;
            var q = Quaternion.Euler(90f, 0f, 0f);
            for (int i = 0; i < v.Length; i++) { v[i] = q * v[i]; n[i] = q * n[i]; }
            m.vertices = v;
            m.normals = n;
            m.RecalculateBounds();
            return m;
        });

        // ------------------------------------------------------------------ scene helpers
        public static Transform Node(Transform parent, string name, Vector3 localPos, Vector3 euler = default)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            t.localRotation = Quaternion.Euler(euler);
            return t;
        }

        /// Child with a mesh renderer (no shadows when shadows = false).
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

        /// A limb segment: capsule of the given radius from the parent's origin downward (−Y) over length.
        public static MeshRenderer Limb(Transform parent, string name, float radius, float length, Material mat, bool shadows = true)
        {
            // L.13 LOD0 budget: 12 radial × 6 rings (2-ring caps + 2 body rings) – smooth normals keep the tubes round
            return Part(parent, name, SmoothCapsule(radius, length + radius * 0.4f, 12, 2, 2), mat,
                        new Vector3(0f, -length - radius * 0.2f, 0f), Vector3.one, default, shadows);
        }
    }
}
