using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// World-anchored seafloor depth on the corners of a 3 m grid, stored as the ocean colour-ramp coordinate
    /// u = sqrt(depth / RampDepth) (0 = shore .. 1 = open sea). The ocean gives every vertex the value at its own
    /// position, so the water colour blends smoothly with depth – no classes, no squares (art bible §1.6).
    public sealed class WorldWaterMap
    {
        public const float CellSize = 3f;
        /// Depth at which the ramp reaches the open-sea colour.
        public const float RampDepth = 35f;
        public readonly int MinX, MinZ, Size;
        readonly byte[] value;   // u * 255

        WorldWaterMap(int min, int size, byte[] v) { MinX = min; MinZ = min; Size = size; value = v; }

        /// Thread-safe build from a height function (world y at x/z).
        public static WorldWaterMap Build(Func<float, float, float> height)
        {
            int min = (int)Math.Floor(WorldTerrainData.Origin / CellSize);
            int max = (int)Math.Ceiling(-WorldTerrainData.Origin / CellSize);
            int size = max - min + 1;
            var v = new byte[size * size];
            Parallel.For(0, size, j =>
            {
                float z = (min + j) * CellSize;
                for (int i = 0; i < size; i++)
                {
                    float x = (min + i) * CellSize;
                    // average of the point and a 1.5 m cross: the ramp follows the seafloor, not single facets
                    float d = 2f * (Waves.SeaLevel - height(x, z));
                    d += Waves.SeaLevel - height(x + 1.5f, z) + Waves.SeaLevel - height(x - 1.5f, z);
                    d += Waves.SeaLevel - height(x, z + 1.5f) + Waves.SeaLevel - height(x, z - 1.5f);
                    v[j * size + i] = (byte)(RampU(d / 6f) * 255f + 0.5f);
                }
            });
            return new WorldWaterMap(min, size, v);
        }

        /// Ramp coordinate for a seafloor depth (0 at or above the waterline .. 1 from RampDepth on).
        public static float RampU(float depth)
        {
            if (depth <= 0f) return 0f;
            float t = depth / RampDepth;
            return t >= 1f ? 1f : (float)Math.Sqrt(t);
        }

        /// Seafloor depth for a ramp coordinate (inverse of RampU).
        public static float DepthOfU(float u) => u * u * RampDepth;

        /// Ramp coordinate at the 3 m grid corner (gx * CellSize, gz * CellSize); outside the map: open sea.
        public float UAt(int gx, int gz)
        {
            int i = gx - MinX, j = gz - MinZ;
            if (i < 0 || j < 0 || i >= Size || j >= Size) return 1f;
            return value[j * Size + i] * (1f / 255f);
        }

        // ------------------------------------------------------------------ colour ramp (art bible §1.6)
        // Art polish pass: the albedo is pre-compensated for the noon sun + sky ambient + grade (the build measured the
        // lit sea ≈ 1.65 × its albedo: #417CB5 instead of the How to Fish #2F6290 near the dock), and the sea is
        // ≥ 95 % opaque from 4 m on – the reef props and the pale seabed showed through as blue blotches from the dock.
        // Only the last metre at the beach stays see-through turquoise.
        // (second/third pass: the shallow house reef (2-4 m) still showed through from the dock end → effectively
        // opaque from 2.5 m on; only the beach strip stays see-through turquoise)
        // Round 2 (Q4, player answer: a wide turquoise lagoon, then blue): below 6 m the ramp follows the Q4 table
        // 0–0.6 m #4FB8C8 α 0.55 · 1.5 m #46ADC4 α 0.70 · 3.5 m #3D9DBE α 0.86 · 6 m #357FAE α 0.93, with the same
        // per-depth pre-compensation the calibration measured between the art-bible table and the screen (×0.93 at the
        // beach → ×0.78 from 6 m on). 15 m and 35 m keep their calibrated values (open sea unchanged, ▣ #326D9B near).
        static readonly float[] StopDepth = { 0f, 0.6f, 1.5f, 3.5f, 6f, 15f, 35f };
        static readonly float[] StopR = { 0x49, 0x47, 0x39, 0x30, 0x29, 0x24, 0x20 };
        static readonly float[] StopG = { 0xAB, 0xA6, 0x8E, 0x7C, 0x63, 0x4A, 0x3F };
        static readonly float[] StopB = { 0xBA, 0xB4, 0xA1, 0x96, 0x88, 0x74, 0x66 };
        static readonly float[] StopA = { 0.55f, 0.55f, 0.70f, 0.86f, 0.93f, 1.0f, 1.0f };

        /// sRGB colour (alpha = opacity) of the sea surface over a seafloor depth.
        public static Color RampColor(float depth)
        {
            if (depth <= 0f) depth = 0f;
            int last = StopDepth.Length - 1;
            if (depth >= StopDepth[last]) return Rgba(last, last, 0f);
            for (int i = 0; i < last; i++)
            {
                if (depth <= StopDepth[i + 1])
                {
                    float t = (depth - StopDepth[i]) / (StopDepth[i + 1] - StopDepth[i]);
                    return Rgba(i, i + 1, t);
                }
            }
            return Rgba(last, last, 0f);
        }

        static Color Rgba(int a, int b, float t)
        {
            const float k = 1f / 255f;
            return new Color((StopR[a] + (StopR[b] - StopR[a]) * t) * k, (StopG[a] + (StopG[b] - StopG[a]) * t) * k,
                             (StopB[a] + (StopB[b] - StopB[a]) * t) * k, StopA[a] + (StopA[b] - StopA[a]) * t);
        }

        /// Brightness of the facet rows of the ramp texture (each ocean triangle picks one: ±8 % facet variance).
        public static readonly float[] FacetRows = { 0.915f, 0.97f, 1.03f, 1.085f };
    }

    /// Shoreline foam geometry: a lumpy 1.6-2.8 m band along the sea-level contour of the terrain triangles, plus a
    /// thin second wave line 2-4 m offshore where the beach shelves gently.
    public sealed class WorldCoastData
    {
        public Vector3[] Verts, Normals;
        public Vector3[] LineVerts, LineNormals;

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

            // per point: averaged seaward direction, a band width that swells and narrows along the coast
            // (period ≈ 9 m) with small lumps, and whether a second wave line fits offshore (gentle beach)
            int pc = pts.Count;
            var nrm = new Vector2[pc];
            var width = new float[pc];
            var line = new bool[pc];
            for (int p = 0; p < pc; p++)
            {
                Vector2 v = dirs[p];
                float l = v.magnitude;
                nrm[p] = l > 1e-5f ? v / l : Vector2.zero;
                var q = pts[p];
                float swell = WorldNoise.Perlin(q.x * 0.11f + 3.7f, q.z * 0.11f - 8.1f);
                float lump = WorldTerrainData.Hash01(Mathf.RoundToInt(q.x * 2f), Mathf.RoundToInt(q.z * 2f), 4242);
                width[p] = Mathf.Clamp(2.2f + 1.5f * swell + 0.45f * (lump - 0.5f), 1.6f, 2.8f);
                if (nrm[p] != Vector2.zero)
                {
                    float h3 = terrain.Height(q.x + nrm[p].x * 3.2f, q.z + nrm[p].y * 3.2f);
                    float h5 = terrain.Height(q.x + nrm[p].x * 5f, q.z + nrm[p].y * 5f);
                    line[p] = h3 > -1.3f && h3 < -0.12f && h5 < -0.2f && h5 > -2.2f;
                }
            }

            int segCount = segs.Count / 2;
            var verts = new List<Vector3>(segCount * 12);
            var norms = new List<Vector3>(segCount * 12);
            var lverts = new List<Vector3>(segCount * 6);
            var lnorms = new List<Vector3>(segCount * 6);
            for (int k = 0; k < segCount; k++)
            {
                int ip = segs[k * 2], iq = segs[k * 2 + 1];
                Vector3 p = pts[ip], q = pts[iq];
                Vector3 mid = (p + q) * 0.5f;
                if (WorldFeatures.SuppressFoam(mid.x, mid.z)) continue;
                Vector3 np = new Vector3(nrm[ip].x, 0f, nrm[ip].y), nq = new Vector3(nrm[iq].x, 0f, nrm[iq].y);
                Vector3 pin = p - np * 0.4f, qin = q - nq * 0.4f;
                Vector3 pmid = p + np * (width[ip] * 0.42f) + Vector3.up * 0.06f, qmid = q + nq * (width[iq] * 0.42f) + Vector3.up * 0.06f;
                Vector3 pout = p + np * width[ip], qout = q + nq * width[iq];
                Quad(verts, norms, pin, qin, qmid, pmid);
                Quad(verts, norms, pmid, qmid, qout, pout);

                if (line[ip] && line[iq])
                {
                    float op = 2.6f + 1.2f * WorldNoise.Perlin(p.x * 0.07f, p.z * 0.07f + 3f);
                    float oq = 2.6f + 1.2f * WorldNoise.Perlin(q.x * 0.07f, q.z * 0.07f + 3f);
                    Quad(lverts, lnorms, p + np * op, q + nq * oq, q + nq * (oq + 0.75f), p + np * (op + 0.75f));
                }
            }
            if (verts.Count == 0) return null;
            return new WorldCoastData { Verts = verts.ToArray(), Normals = norms.ToArray(), LineVerts = lverts.ToArray(), LineNormals = lnorms.ToArray() };
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
        static void Quad(List<Vector3> v, List<Vector3> n, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            Tri(v, n, a, b, c);
            Tri(v, n, a, c, d);
        }

        static void Tri(List<Vector3> v, List<Vector3> n, Vector3 a, Vector3 b, Vector3 c)
        {
            var nrm = Vector3.Cross(b - a, c - a);
            if (nrm.y < 0f) { var t = b; b = c; c = t; nrm = -nrm; }
            float l = nrm.magnitude;
            nrm = l > 1e-8f ? nrm / l : Vector3.up;
            v.Add(a); v.Add(b); v.Add(c);
            n.Add(nrm); n.Add(nrm); n.Add(nrm);
        }
    }

    /// The white foam band around every coast (island, landmark islands, sea stacks): sits on the water at the shore,
    /// slides gently in and out (0.12 Hz), hidden from below. A fainter second wave line breathes offshore.
    [DefaultExecutionOrder(110)]
    public sealed class WorldCoast : MonoBehaviour
    {
        public const int RenderQueue = 2985;   // after the ocean surface
        MeshRenderer r, lineR;
        Transform lineT;
        Material mat, lineMat;
        Camera cam;
        int idBase;
        Color lineCol;

        public static WorldCoast Create(Transform parent, WorldCoastData data)
        {
            if (data == null || data.Verts == null || data.Verts.Length == 0) return null;
            var go = new GameObject("CoastFoam");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = BuildMesh("SB_CoastFoam", data.Verts, data.Normals);
            var c = go.AddComponent<WorldCoast>();
            c.idBase = Shader.PropertyToID("_BaseColor");
            c.r = go.AddComponent<MeshRenderer>();
            var col = LowPoly.Foam;
            col.a = 0.92f;
            c.mat = FoamMaterial("SB_CoastFoam", col, RenderQueue);
            Setup(c.r, c.mat);

            if (data.LineVerts != null && data.LineVerts.Length > 0)
            {
                var lgo = new GameObject("CoastFoamLine");
                lgo.transform.SetParent(go.transform.parent, false);
                lgo.AddComponent<MeshFilter>().sharedMesh = BuildMesh("SB_CoastFoamLine", data.LineVerts, data.LineNormals);
                c.lineR = lgo.AddComponent<MeshRenderer>();
                c.lineCol = LowPoly.Foam;
                c.lineCol.a = 0.4f;
                c.lineMat = FoamMaterial("SB_CoastFoamLine", c.lineCol, RenderQueue - 1);
                Setup(c.lineR, c.lineMat);
                c.lineT = lgo.transform;
            }
            return c;
        }

        static Mesh BuildMesh(string name, Vector3[] verts, Vector3[] normals)
        {
            var mesh = new Mesh { name = name };
            if (verts.Length > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            var idx = new int[verts.Length];
            for (int i = 0; i < idx.Length; i++) idx[i] = i;
            mesh.SetIndices(idx, MeshTopology.Triangles, 0);
            mesh.RecalculateBounds();
            var b = mesh.bounds;
            b.Expand(new Vector3(0f, 1f, 0f));
            mesh.bounds = b;
            mesh.UploadMeshData(true);
            return mesh;
        }

        static Material FoamMaterial(string name, Color col, int queue)
        {
            var m = new Material(MaterialLibrary.Lit) { name = name };
            m.SetColor("_BaseColor", col);
            m.SetFloat("_Smoothness", 0.2f);
            m.SetFloat("_Metallic", 0f);
            MaterialLibrary.MakeTransparent(m, false);
            m.renderQueue = queue;
            return m;
        }

        static void Setup(MeshRenderer r, Material m)
        {
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = true;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
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
            if (lineR != null && lineR.enabled != show) lineR.enabled = show;
            if (!show) return;

            // on an ~8 % beach a few cm of rise slide the foam's landward edge ~0.4 m in and out
            float t = Time.time;
            float slide = Mathf.Sin(t * 0.754f);
            transform.localPosition = new Vector3(0f, 0.07f + 0.045f * slide + 0.015f * Mathf.Sin(t * 1.9f + 1f), 0f);
            if (lineR != null)
            {
                lineT.localPosition = new Vector3(0f, 0.05f + 0.03f * Mathf.Sin(t * 0.754f + 2.2f), 0f);
                lineCol.a = 0.4f * (0.55f + 0.45f * Mathf.Sin(t * 0.754f + 1.2f));
                lineMat.SetColor(idBase, lineCol);
            }
        }

        void OnDestroy()
        {
            if (mat != null) Destroy(mat);
            if (lineMat != null) Destroy(lineMat);
            if (lineT != null) Destroy(lineT.gameObject);
        }
    }
}
