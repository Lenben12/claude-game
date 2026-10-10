using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Procedural meshes for the hand camera models. Primitive meshes are cached and shared between rebuilds;
    /// combined per-rig meshes are owned (and destroyed) by the rig.
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

        // ------------------------------------------------------------------ rounded box
        /// Box centered at the origin with rounded edges/corners (radius 0 = sharp box). Smooth normals.
        public static Mesh RoundedBox(Vector3 size, float radius, int seg = 3)
        {
            float maxR = Mathf.Min(size.x, Mathf.Min(size.y, size.z)) * 0.5f;
            radius = Mathf.Clamp(radius, 0f, maxR * 0.999f);
            if (radius < 0.0002f) { radius = 0f; seg = 0; }
            string key = $"rb{size.x:F4}_{size.y:F4}_{size.z:F4}_{radius:F4}_{seg}";
            if (TryGet(key, out var cached)) return cached;

            Vector3 half = size * 0.5f;
            Vector3 inner = new Vector3(half.x - radius, half.y - radius, half.z - radius);
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            // Face table: normal, U, V with Cross(U, V) == normal (front-facing winding for Unity).
            Vector3[] faceN = { Vector3.forward, Vector3.back, Vector3.right, Vector3.left, Vector3.up, Vector3.down };
            Vector3[] faceU = { Vector3.right, Vector3.up, Vector3.up, Vector3.forward, Vector3.forward, Vector3.right };
            Vector3[] faceV = { Vector3.up, Vector3.right, Vector3.forward, Vector3.up, Vector3.right, Vector3.forward };

            for (int f = 0; f < 6; f++)
            {
                Vector3 n = faceN[f], u = faceU[f], v = faceV[f];
                float aN = Comp(half, n), aU = Comp(half, u), aV = Comp(half, v);
                float iU = Comp(inner, u), iV = Comp(inner, v);
                float[] su = Samples(aU, iU, radius, seg);
                float[] sv = Samples(aV, iV, radius, seg);
                int baseIndex = verts.Count;
                for (int j = 0; j < sv.Length; j++)
                {
                    for (int i = 0; i < su.Length; i++)
                    {
                        Vector3 p = n * aN + u * su[i] + v * sv[j];
                        Vector3 cl = new Vector3(Mathf.Clamp(p.x, -inner.x, inner.x), Mathf.Clamp(p.y, -inner.y, inner.y), Mathf.Clamp(p.z, -inner.z, inner.z));
                        Vector3 d = p - cl;
                        Vector3 nn = d.sqrMagnitude > 1e-12f ? d.normalized : n;
                        verts.Add(radius > 0f ? cl + nn * radius : p);
                        norms.Add(radius > 0f ? nn : n);
                        uvs.Add(new Vector2((su[i] + aU) / (2f * aU), (sv[j] + aV) / (2f * aV)));
                    }
                }
                int w = su.Length;
                for (int j = 0; j < sv.Length - 1; j++)
                {
                    for (int i = 0; i < w - 1; i++)
                    {
                        int p00 = baseIndex + j * w + i;
                        int p10 = p00 + 1;          // +U
                        int p01 = p00 + w;          // +V
                        int p11 = p01 + 1;
                        tris.Add(p00); tris.Add(p10); tris.Add(p11);
                        tris.Add(p00); tris.Add(p11); tris.Add(p01);
                    }
                }
            }

            var m = new Mesh();
            m.SetVertices(verts);
            m.SetNormals(norms);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            return Store(key, m);
        }

        /// Component of v along a cardinal axis (sign-independent).
        static float Comp(Vector3 v, Vector3 axis) => Mathf.Abs(v.x * axis.x + v.y * axis.y + v.z * axis.z);

        /// Sample positions along one face axis: dense on the rounded bands (uniform angles), sparse in the flat middle.
        static float[] Samples(float half, float inner, float radius, int seg)
        {
            if (radius <= 0f || seg <= 0) return new[] { -half, half };
            var list = new float[(seg + 1) * 2];
            int k = 0;
            for (int s = seg; s >= 0; s--)
            {
                float phi = (s / (float)seg) * 45f * Mathf.Deg2Rad;
                list[k++] = -inner - radius * Mathf.Tan(phi);
            }
            for (int s = 0; s <= seg; s++)
            {
                float phi = (s / (float)seg) * 45f * Mathf.Deg2Rad;
                list[k++] = inner + radius * Mathf.Tan(phi);
            }
            return list;
        }

        public static Mesh Box(Vector3 size) => RoundedBox(size, 0f, 0);

        // ------------------------------------------------------------------ cylinder / frustum
        /// Frustum along +Y, centered at the origin. 'hard' gives faceted sides (e.g. 4 segments = square matte box).
        public static Mesh Frustum(float rBottom, float rTop, float height, int segments = 20, bool caps = true, bool hard = false)
        {
            segments = Mathf.Max(3, segments);
            string key = $"cy{rBottom:F4}_{rTop:F4}_{height:F4}_{segments}_{(caps ? 1 : 0)}{(hard ? 1 : 0)}";
            if (TryGet(key, out var cached)) return cached;

            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            float hy = height * 0.5f;
            float slope = (rBottom - rTop) / Mathf.Max(0.0001f, height);
            float offset = hard ? Mathf.PI / segments : 0f;   // faceted shapes get a flat face toward +X/+Z

            if (!hard)
            {
                for (int i = 0; i <= segments; i++)
                {
                    float a = i / (float)segments * Mathf.PI * 2f;
                    float c = Mathf.Cos(a), s = Mathf.Sin(a);
                    Vector3 n = new Vector3(c, slope, s).normalized;
                    verts.Add(new Vector3(c * rBottom, -hy, s * rBottom)); norms.Add(n); uvs.Add(new Vector2(i / (float)segments, 0f));
                    verts.Add(new Vector3(c * rTop, hy, s * rTop)); norms.Add(n); uvs.Add(new Vector2(i / (float)segments, 1f));
                }
                for (int i = 0; i < segments; i++)
                {
                    int b0 = i * 2, t0 = b0 + 1, b1 = b0 + 2, t1 = b0 + 3;
                    tris.Add(b0); tris.Add(t0); tris.Add(t1);
                    tris.Add(b0); tris.Add(t1); tris.Add(b1);
                }
            }
            else
            {
                for (int i = 0; i < segments; i++)
                {
                    float a0 = i / (float)segments * Mathf.PI * 2f + offset;
                    float a1 = (i + 1) / (float)segments * Mathf.PI * 2f + offset;
                    float am = (a0 + a1) * 0.5f;
                    Vector3 n = new Vector3(Mathf.Cos(am), slope, Mathf.Sin(am)).normalized;
                    int b = verts.Count;
                    verts.Add(new Vector3(Mathf.Cos(a0) * rBottom, -hy, Mathf.Sin(a0) * rBottom));
                    verts.Add(new Vector3(Mathf.Cos(a0) * rTop, hy, Mathf.Sin(a0) * rTop));
                    verts.Add(new Vector3(Mathf.Cos(a1) * rBottom, -hy, Mathf.Sin(a1) * rBottom));
                    verts.Add(new Vector3(Mathf.Cos(a1) * rTop, hy, Mathf.Sin(a1) * rTop));
                    for (int k = 0; k < 4; k++) norms.Add(n);
                    uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(0, 1)); uvs.Add(new Vector2(1, 0)); uvs.Add(new Vector2(1, 1));
                    tris.Add(b); tris.Add(b + 1); tris.Add(b + 3);
                    tris.Add(b); tris.Add(b + 3); tris.Add(b + 2);
                }
            }

            if (caps)
            {
                // top cap (+Y)
                if (rTop > 0.0001f)
                {
                    int c = verts.Count;
                    verts.Add(new Vector3(0f, hy, 0f)); norms.Add(Vector3.up); uvs.Add(new Vector2(0.5f, 0.5f));
                    for (int i = 0; i <= segments; i++)
                    {
                        float a = i / (float)segments * Mathf.PI * 2f + offset;
                        verts.Add(new Vector3(Mathf.Cos(a) * rTop, hy, Mathf.Sin(a) * rTop)); norms.Add(Vector3.up);
                        uvs.Add(new Vector2(0.5f + Mathf.Cos(a) * 0.5f, 0.5f + Mathf.Sin(a) * 0.5f));
                    }
                    for (int i = 0; i < segments; i++) { tris.Add(c); tris.Add(c + 2 + i); tris.Add(c + 1 + i); }
                }
                // bottom cap (-Y)
                if (rBottom > 0.0001f)
                {
                    int c = verts.Count;
                    verts.Add(new Vector3(0f, -hy, 0f)); norms.Add(Vector3.down); uvs.Add(new Vector2(0.5f, 0.5f));
                    for (int i = 0; i <= segments; i++)
                    {
                        float a = i / (float)segments * Mathf.PI * 2f + offset;
                        verts.Add(new Vector3(Mathf.Cos(a) * rBottom, -hy, Mathf.Sin(a) * rBottom)); norms.Add(Vector3.down);
                        uvs.Add(new Vector2(0.5f + Mathf.Cos(a) * 0.5f, 0.5f + Mathf.Sin(a) * 0.5f));
                    }
                    for (int i = 0; i < segments; i++) { tris.Add(c); tris.Add(c + 1 + i); tris.Add(c + 2 + i); }
                }
            }

            var m = new Mesh();
            m.SetVertices(verts);
            m.SetNormals(norms);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            return Store(key, m);
        }

        public static Mesh Cylinder(float radius, float height, int segments = 20) => Frustum(radius, radius, height, segments, true, false);

        // ------------------------------------------------------------------ sphere / dome
        /// UV sphere (or the +Y hemisphere when 'hemisphere' is true) centered at the origin.
        public static Mesh Sphere(float radius, int segments = 16, bool hemisphere = false)
        {
            segments = Mathf.Max(6, segments);
            string key = $"sp{radius:F4}_{segments}_{(hemisphere ? 1 : 0)}";
            if (TryGet(key, out var cached)) return cached;

            int lat = hemisphere ? segments / 2 : segments;
            float latMax = hemisphere ? Mathf.PI * 0.5f : Mathf.PI;
            int lon = segments * 2;
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int i = 0; i <= lat; i++)
            {
                float t = i / (float)lat * latMax;
                float st = Mathf.Sin(t), ct = Mathf.Cos(t);
                for (int j = 0; j <= lon; j++)
                {
                    float p = j / (float)lon * Mathf.PI * 2f;
                    Vector3 n = new Vector3(st * Mathf.Cos(p), ct, st * Mathf.Sin(p));
                    verts.Add(n * radius); norms.Add(n); uvs.Add(new Vector2(j / (float)lon, 1f - i / (float)lat));
                }
            }
            int w = lon + 1;
            for (int i = 0; i < lat; i++)
            {
                for (int j = 0; j < lon; j++)
                {
                    int v00 = i * w + j, v01 = v00 + 1, v10 = v00 + w, v11 = v10 + 1;
                    tris.Add(v00); tris.Add(v01); tris.Add(v11);
                    tris.Add(v00); tris.Add(v11); tris.Add(v10);
                }
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

        // ------------------------------------------------------------------ combine
        /// Merges every MeshFilter below 'source' into one mesh per material, parented to 'target'
        /// (which must share the coordinate frame the parts were built in). Destroys 'source'. Returns the created meshes.
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
