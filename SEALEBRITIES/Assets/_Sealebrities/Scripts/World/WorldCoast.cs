using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// World-anchored water color classes on a 3 m grid (0 = shallow turquoise .. 3 = deep blue), from the seafloor depth.
    /// The ocean colors its faceted triangles from this map, so lagoons stay bright and the open sea is deep blue.
    public sealed class WorldWaterMap
    {
        public const float CellSize = 3f;
        public const int Classes = 4;
        public readonly int MinX, MinZ, Size;
        readonly byte[] value;   // continuous class * 60

        WorldWaterMap(int min, int size, byte[] v) { MinX = min; MinZ = min; Size = size; value = v; }

        /// Thread-safe build from a height function (world y at x/z).
        public static WorldWaterMap Build(Func<float, float, float> height)
        {
            int min = (int)Math.Floor(WorldTerrainData.Origin / CellSize);
            int max = (int)Math.Floor(-WorldTerrainData.Origin / CellSize);
            int size = max - min + 1;
            var v = new byte[size * size];
            Parallel.For(0, size, j =>
            {
                float z = (min + j) * CellSize + CellSize * 0.5f;
                for (int i = 0; i < size; i++)
                {
                    float x = (min + i) * CellSize + CellSize * 0.5f;
                    float depth = Waves.SeaLevel - height(x, z);
                    v[j * size + i] = (byte)(ClassOfDepth(depth) * 60f + 0.5f);
                }
            });
            return new WorldWaterMap(min, size, v);
        }

        /// 0 (<= 1.2 m) .. 1 (4.5 m) .. 2 (13 m) .. 3 (>= 32 m).
        public static float ClassOfDepth(float depth)
        {
            if (depth <= 1.2f) return 0f;
            if (depth < 4.5f) return (depth - 1.2f) / 3.3f;
            if (depth < 13f) return 1f + (depth - 4.5f) / 8.5f;
            if (depth < 32f) return 2f + (depth - 13f) / 19f;
            return 3f;
        }

        /// Continuous class of a 3 m world cell (outside the map: deep).
        public float ClassAt(int cellX, int cellZ)
        {
            int i = cellX - MinX, j = cellZ - MinZ;
            if (i < 0 || j < 0 || i >= Size || j >= Size) return 3f;
            return value[j * Size + i] * (1f / 60f);
        }

        /// Discrete class for one triangle: clean color bands (How to Fish look) with only a narrow, jagged
        /// one-or-two-triangle border between classes (world-anchored hash). A wide dither zone read as noisy
        /// light/dark "confetti" over the whole reef flat.
        public static int Pick(float c, uint hash)
        {
            int k = (int)c;
            if (k >= Classes - 1) return Classes - 1;
            float f = c - k;
            const float lo = 0.44f, hi = 0.56f;
            float p = f <= lo ? 0f : (f >= hi ? 1f : (f - lo) / (hi - lo));
            return ((hash & 0xFFFF) / 65535f) < p ? k + 1 : k;
        }
    }

    /// Shoreline foam geometry: a thin faceted band along the sea-level contour of the terrain triangles.
    public sealed class WorldCoastData
    {
        public Vector3[] Verts, Normals;

        /// Thread-safe (reads only terrain data). Returns null when the terrain never crosses sea level.
        public static WorldCoastData Build(WorldTerrainData terrain)
        {
            float sea = Waves.SeaLevel;
            var pts = new List<Vector3>(4096);
            var dirs = new List<Vector2>(4096);
            var segs = new List<int>(8192);
            var index = new Dictionary<long, int>(4096);

            foreach (var c in terrain.Chunks)
            {
                if (c == null || c.MinY > sea || c.MaxY < sea) continue;
                int n = c.N, s = n + 1;
                for (int j = 0; j < n; j++)
                {
                    for (int i = 0; i < n; i++)
                    {
                        int a = j * s + i, b = a + 1, cc = a + s, d = cc + 1;
                        if (c.DiagBC[j * n + i]) { Segment(c, a, cc, b, sea, pts, dirs, segs, index); Segment(c, b, cc, d, sea, pts, dirs, segs, index); }
                        else { Segment(c, a, cc, d, sea, pts, dirs, segs, index); Segment(c, a, d, b, sea, pts, dirs, segs, index); }
                    }
                }
            }
            if (segs.Count == 0) return null;

            // per point: averaged seaward direction and a varying band width
            int pc = pts.Count;
            var nrm = new Vector2[pc];
            var width = new float[pc];
            for (int p = 0; p < pc; p++)
            {
                Vector2 v = dirs[p];
                float l = v.magnitude;
                nrm[p] = l > 1e-5f ? v / l : Vector2.zero;
                var q = pts[p];
                float h = WorldTerrainData.Hash01(Mathf.RoundToInt(q.x * 2f), Mathf.RoundToInt(q.z * 2f), 4242);
                width[p] = 0.9f + 1.3f * h;
            }

            int segCount = segs.Count / 2;
            var verts = new Vector3[segCount * 12];
            var norms = new Vector3[segCount * 12];
            int o = 0;
            for (int k = 0; k < segCount; k++)
            {
                int ip = segs[k * 2], iq = segs[k * 2 + 1];
                Vector3 p = pts[ip], q = pts[iq];
                Vector3 np = new Vector3(nrm[ip].x, 0f, nrm[ip].y), nq = new Vector3(nrm[iq].x, 0f, nrm[iq].y);
                Vector3 pin = p - np * 0.35f, qin = q - nq * 0.35f;
                Vector3 pmid = p + np * (width[ip] * 0.45f) + Vector3.up * 0.05f, qmid = q + nq * (width[iq] * 0.45f) + Vector3.up * 0.05f;
                Vector3 pout = p + np * width[ip], qout = q + nq * width[iq];
                o = Quad(verts, norms, o, pin, qin, qmid, pmid);
                o = Quad(verts, norms, o, pmid, qmid, qout, pout);
            }
            return new WorldCoastData { Verts = verts, Normals = norms };
        }

        static void Segment(WorldTerrainChunk c, int a, int b, int d, float sea, List<Vector3> pts, List<Vector2> dirs, List<int> segs, Dictionary<long, int> index)
        {
            float ya = c.Vy[a] - sea, yb = c.Vy[b] - sea, yd = c.Vy[d] - sea;
            bool ua = ya > 0f, ub = yb > 0f, ud = yd > 0f;
            if (ua == ub && ub == ud) return;

            // downhill (seaward) direction of this facet = horizontal part of its normal
            float ex1 = c.Vx[b] - c.Vx[a], ey1 = yb - ya, ez1 = c.Vz[b] - c.Vz[a];
            float ex2 = c.Vx[d] - c.Vx[a], ey2 = yd - ya, ez2 = c.Vz[d] - c.Vz[a];
            float nx = ey1 * ez2 - ez1 * ey2, ny = ez1 * ex2 - ex1 * ez2, nz = ex1 * ey2 - ey1 * ex2;
            if (ny < 0f) { nx = -nx; nz = -nz; }
            float hl = MathF.Sqrt(nx * nx + nz * nz);
            if (hl < 1e-6f) return;
            var dir = new Vector2(nx / hl, nz / hl);

            int p0 = -1, p1 = -1;
            if (ua != ub) p0 = Point(c, a, b, sea, pts, dirs, index);
            if (ub != ud) { int p = Point(c, b, d, sea, pts, dirs, index); if (p0 < 0) p0 = p; else p1 = p; }
            if (ud != ua) { int p = Point(c, d, a, sea, pts, dirs, index); if (p0 < 0) p0 = p; else p1 = p; }
            if (p0 < 0 || p1 < 0 || p0 == p1) return;
            segs.Add(p0); segs.Add(p1);
            dirs[p0] += dir; dirs[p1] += dir;
        }

        /// Sea-level crossing on a grid edge (deterministic: always interpolated from the lower vertex index).
        static int Point(WorldTerrainChunk c, int u, int v, float sea, List<Vector3> pts, List<Vector2> dirs, Dictionary<long, int> index)
        {
            if (u > v) { int t = u; u = v; v = t; }
            float yu = c.Vy[u] - sea, yv = c.Vy[v] - sea;
            float t01 = yu / (yu - yv);
            float x = c.Vx[u] + (c.Vx[v] - c.Vx[u]) * t01;
            float z = c.Vz[u] + (c.Vz[v] - c.Vz[u]) * t01;
            long key = ((long)Mathf.RoundToInt(x * 50f) << 32) ^ (uint)Mathf.RoundToInt(z * 50f);
            if (index.TryGetValue(key, out int id)) return id;
            id = pts.Count;
            pts.Add(new Vector3(x, sea, z));
            dirs.Add(Vector2.zero);
            index[key] = id;
            return id;
        }

        /// Two flat-shaded, upward-facing triangles.
        static int Quad(Vector3[] v, Vector3[] n, int o, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            o = Tri(v, n, o, a, b, c);
            return Tri(v, n, o, a, c, d);
        }

        static int Tri(Vector3[] v, Vector3[] n, int o, Vector3 a, Vector3 b, Vector3 c)
        {
            var nrm = Vector3.Cross(b - a, c - a);
            if (nrm.y < 0f) { var t = b; b = c; c = t; nrm = -nrm; }
            float l = nrm.magnitude;
            nrm = l > 1e-8f ? nrm / l : Vector3.up;
            v[o] = a; v[o + 1] = b; v[o + 2] = c;
            n[o] = nrm; n[o + 1] = nrm; n[o + 2] = nrm;
            return o + 3;
        }
    }

    /// The white foam line around the island: sits on the water at the shore, bobs gently, hidden from below.
    [DefaultExecutionOrder(110)]
    public sealed class WorldCoast : MonoBehaviour
    {
        public const int RenderQueue = 2985;   // after the ocean surface
        MeshRenderer r;
        Material mat;
        Camera cam;

        public static WorldCoast Create(Transform parent, WorldCoastData data)
        {
            if (data == null || data.Verts == null || data.Verts.Length == 0) return null;
            var go = new GameObject("CoastFoam");
            go.transform.SetParent(parent, false);
            var mesh = new Mesh { name = "SB_CoastFoam" };
            if (data.Verts.Length > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(data.Verts);
            mesh.SetNormals(data.Normals);
            var idx = new int[data.Verts.Length];
            for (int i = 0; i < idx.Length; i++) idx[i] = i;
            mesh.SetIndices(idx, MeshTopology.Triangles, 0);
            mesh.RecalculateBounds();
            var b = mesh.bounds;
            b.Expand(new Vector3(0f, 1f, 0f));
            mesh.bounds = b;
            mesh.UploadMeshData(true);

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var c = go.AddComponent<WorldCoast>();
            c.r = go.AddComponent<MeshRenderer>();
            var col = LowPoly.Foam;
            col.a = 0.9f;
            c.mat = new Material(MaterialLibrary.Lit) { name = "SB_CoastFoam" };
            c.mat.SetColor("_BaseColor", col);
            c.mat.SetFloat("_Smoothness", 0.2f);
            c.mat.SetFloat("_Metallic", 0f);
            MaterialLibrary.MakeTransparent(c.mat, false);
            c.mat.renderQueue = RenderQueue;
            c.r.sharedMaterial = c.mat;
            c.r.shadowCastingMode = ShadowCastingMode.Off;
            c.r.receiveShadows = true;
            c.r.lightProbeUsage = LightProbeUsage.Off;
            c.r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return c;
        }

        void LateUpdate()
        {
            if (cam == null || !cam.isActiveAndEnabled)
            {
                cam = Camera.main;
                if (cam == null && Refs.Player != null) cam = Refs.Player.View;
            }
            bool show = true;
            if (cam != null)
            {
                Vector3 p = cam.transform.position;
                show = p.y > Waves.HeightAt(p.x, p.z) - 0.05f;
            }
            if (r.enabled != show) r.enabled = show;
            if (!show) return;
            float t = Time.time;
            transform.localPosition = new Vector3(0f, 0.07f + 0.07f * Mathf.Sin(t * 1.15f) + 0.03f * Mathf.Sin(t * 2.3f + 1f), 0f);
        }

        void OnDestroy()
        {
            if (mat != null) Destroy(mat);
        }
    }
}
