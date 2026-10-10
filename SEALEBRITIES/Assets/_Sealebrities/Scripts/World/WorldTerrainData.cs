using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Sealebrities
{
    /// Color class of one terrain triangle; each class is one shared flat LowPoly material (one submesh per chunk).
    public enum TerrainClass : byte { Sand, SandAlt, WetSand, Grass, GrassDark, Soil, Rock, RockDark, Seabed, SeabedAlt, DeepRock }

    /// One 128 m terrain chunk: a slightly jittered grid whose border vertices sit exactly on the chunk edge.
    /// Edges shared with a coarser neighbour are stitched (their extra vertices lie on the coarse edge), so there are no cracks.
    public sealed class WorldTerrainChunk
    {
        public int Cx, Cz, N;
        public float X0, Z0, Cell;
        /// Grid vertices, (N + 1)^2, index j * (N + 1) + i.
        public float[] Vx, Vy, Vz;
        /// Per cell: true = split along b-c (b = i+1,j / c = i,j+1), false = split along a-d.
        public bool[] DiagBC;
        public float MinY, MaxY;

        // render data (flat shaded, unwelded, sorted by class); released after upload
        public Vector3[] RenderVerts, RenderNormals;
        public int[] ClassCount;

        public int Stride => N + 1;

        /// Exact height of the triangulated surface (barycentric). x/z should lie inside the chunk.
        public float HeightAt(float x, float z)
        {
            float u = (x - X0) / Cell, v = (z - Z0) / Cell;
            int ci = (int)Math.Floor(u), cj = (int)Math.Floor(v);
            if (ci < 0) ci = 0; else if (ci > N - 1) ci = N - 1;
            if (cj < 0) cj = 0; else if (cj > N - 1) cj = N - 1;

            float best = float.NegativeInfinity, bestH = 0f;
            if (TestCell(ci, cj, x, z, ref best, ref bestH)) return bestH;
            // jitter is < 0.2 cells, so a point can only belong to a direct neighbour cell
            for (int k = 0; k < 8; k++)
            {
                int i = ci + NbI[k], j = cj + NbJ[k];
                if (i < 0 || j < 0 || i >= N || j >= N) continue;
                if (TestCell(i, j, x, z, ref best, ref bestH)) return bestH;
            }
            return bestH;
        }

        static readonly int[] NbI = { -1, 1, 0, 0, -1, 1, -1, 1 };
        static readonly int[] NbJ = { 0, 0, -1, 1, -1, -1, 1, 1 };

        bool TestCell(int i, int j, float x, float z, ref float best, ref float bestH)
        {
            int s = N + 1;
            int a = j * s + i, b = a + 1, c = a + s, d = c + 1;
            if (DiagBC[j * N + i])
                return Tri(a, c, b, x, z, ref best, ref bestH) || Tri(b, c, d, x, z, ref best, ref bestH);
            return Tri(a, c, d, x, z, ref best, ref bestH) || Tri(a, d, b, x, z, ref best, ref bestH);
        }

        bool Tri(int a, int b, int c, float x, float z, ref float best, ref float bestH)
        {
            float ax = Vx[a], az = Vz[a], bx = Vx[b], bz = Vz[b], cx = Vx[c], cz = Vz[c];
            float det = (bz - cz) * (ax - cx) + (cx - bx) * (az - cz);
            if (det > -1e-9f && det < 1e-9f) return false;
            float inv = 1f / det;
            float w0 = ((bz - cz) * (x - cx) + (cx - bx) * (z - cz)) * inv;
            float w1 = ((cz - az) * (x - cx) + (ax - cx) * (z - cz)) * inv;
            float w2 = 1f - w0 - w1;
            float m = w0 < w1 ? (w0 < w2 ? w0 : w2) : (w1 < w2 ? w1 : w2);
            if (m >= -1e-4f)
            {
                bestH = w0 * Vy[a] + w1 * Vy[b] + w2 * Vy[c];
                return true;
            }
            if (m > best)
            {
                // fallback for points just outside every candidate (float noise): clamp and renormalize the weights
                best = m;
                float c0 = w0 > 0f ? w0 : 0f, c1 = w1 > 0f ? w1 : 0f, c2 = w2 > 0f ? w2 : 0f;
                float sum = c0 + c1 + c2;
                if (sum < 1e-6f) { c0 = 1f; sum = 1f; }
                bestH = (c0 * Vy[a] + c1 * Vy[b] + c2 * Vy[c]) / sum;
            }
            return false;
        }
    }

    /// The whole low-poly terrain as data: 16 x 16 chunks of 128 m, fine (3.2 m) around the island, the house reef and the
    /// dive sites, coarse (6.4 m) elsewhere. Built on worker threads from WorldHeightField; Height() is thread-safe and
    /// returns the height of the actual triangulated surface, so props and creatures sit exactly on the rendered ground.
    public sealed class WorldTerrainData
    {
        public const float ChunkSize = 128f;
        public const int ChunksPerSide = 16;
        public const int FineCells = 40, CoarseCells = 20;   // coarse must divide fine (crack-free stitching)
        public const float Origin = -WorldLayout.TerrainSize * 0.5f;
        const float Jitter = 0.17f;                          // < 0.2 cells keeps every triangle valid

        public readonly WorldTerrainChunk[] Chunks = new WorldTerrainChunk[ChunksPerSide * ChunksPerSide];
        public int TriangleCount { get; private set; }

        readonly Func<float, float, float> source;
        readonly int[] res = new int[ChunksPerSide * ChunksPerSide];

        public WorldTerrainData(Func<float, float, float> heightSource) { source = heightSource; }

        // ------------------------------------------------------------------ static layout cache (main thread first)
        static bool prepared;
        static float[] siteX, siteZ, siteR;
        static float[] pathX, pathZ;
        static float pathMinX, pathMaxX, pathMinZ, pathMaxZ;
        static readonly float[] yardX = new float[3], yardZ = new float[3];

        /// Caches dive site and path layout in plain arrays. Call on the main thread before building.
        public static void PrepareStatics()
        {
            if (prepared) return;
            WorldHeightField.Height(0f, 0f);   // static ctor reads DiveSites / WorldLayout on the main thread
            var sites = DiveSites.All;
            int n = sites != null ? sites.Count : 0;
            siteX = new float[n]; siteZ = new float[n]; siteR = new float[n];
            for (int i = 0; i < n; i++) { siteX[i] = sites[i].center.x; siteZ[i] = sites[i].center.z; siteR[i] = sites[i].radius; }
            yardX[0] = WorldLayout.Hut.x; yardZ[0] = WorldLayout.Hut.z;
            yardX[1] = WorldLayout.Helipad.x; yardZ[1] = WorldLayout.Helipad.z;
            yardX[2] = WorldLayout.Lighthouse.x; yardZ[2] = WorldLayout.Lighthouse.z;
            BuildPaths();
            prepared = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { prepared = false; }

        /// Dirt paths under the props' path stones: hut -> lighthouse and hut -> helipad (same control points as the props).
        static void BuildPaths()
        {
            var hut = WorldLayout.Hut;
            float stepsX = hut.x + 7.2f + 2.5f;
            var lh = WorldLayout.Lighthouse;
            float tx = hut.x - lh.x, tz = hut.z - lh.z, tl = Mathf.Sqrt(tx * tx + tz * tz);
            float doorX = lh.x + tx / tl * 3.4f, doorZ = lh.z + tz / tl * 3.4f;
            var main = new[]
            {
                new Vector2(stepsX + 1.5f, hut.z + 1f), new Vector2(stepsX - 1f, hut.z + 9f), new Vector2(110f, 36f), new Vector2(94f, 50f),
                new Vector2(72f, 70f), new Vector2(48f, 92f), new Vector2(22f, 114f), new Vector2(2f, 131f), new Vector2(-14f, 142f),
                new Vector2(doorX, doorZ),
            };
            var heli = WorldLayout.Helipad;
            var side = new[]
            {
                new Vector2(hut.x + 6f, hut.z - 7f), new Vector2(112f, -8f), new Vector2(98f, -32f), new Vector2(heli.x + 9f, heli.z + 9f),
            };
            var xs = new List<float>();
            var zs = new List<float>();
            Spline(main, xs, zs);
            xs.Add(float.NaN); zs.Add(float.NaN);   // break between the two paths
            Spline(side, xs, zs);
            pathX = xs.ToArray(); pathZ = zs.ToArray();
            pathMinX = pathMinZ = float.MaxValue; pathMaxX = pathMaxZ = float.MinValue;
            for (int i = 0; i < pathX.Length; i++)
            {
                if (float.IsNaN(pathX[i])) continue;
                pathMinX = Math.Min(pathMinX, pathX[i]); pathMaxX = Math.Max(pathMaxX, pathX[i]);
                pathMinZ = Math.Min(pathMinZ, pathZ[i]); pathMaxZ = Math.Max(pathMaxZ, pathZ[i]);
            }
        }

        static void Spline(Vector2[] cps, List<float> xs, List<float> zs)
        {
            for (int i = 0; i < cps.Length - 1; i++)
            {
                Vector2 p0 = cps[Math.Max(0, i - 1)], p1 = cps[i], p2 = cps[i + 1], p3 = cps[Math.Min(cps.Length - 1, i + 2)];
                int steps = Math.Max(2, (int)Math.Ceiling((p2 - p1).magnitude / 2f));
                for (int s = 0; s < steps; s++)
                {
                    float t = s / (float)steps, t2 = t * t, t3 = t2 * t;
                    Vector2 q = 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
                    xs.Add(q.x); zs.Add(q.y);
                }
            }
            xs.Add(cps[cps.Length - 1].x); zs.Add(cps[cps.Length - 1].y);
        }

        /// 0..1 dirt yard around the hut, the helipad and the lighthouse (tighter than the flat building plateaus).
        static float Yard(float x, float z)
        {
            float a = 1f - SmoothStep(7f, 13f, Dist(x, z, yardX[0], yardZ[0]));
            float b = 1f - SmoothStep(6f, 11f, Dist(x, z, yardX[1], yardZ[1]));
            float c = 1f - SmoothStep(5f, 9f, Dist(x, z, yardX[2], yardZ[2]));
            return Math.Max(a, Math.Max(b, c));
        }

        static float Dist(float x, float z, float cx, float cz)
        {
            float dx = x - cx, dz = z - cz;
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        /// 0..1, 1 on the dirt paths.
        static float PathMask(float x, float z)
        {
            if (pathX == null || x < pathMinX - 3f || x > pathMaxX + 3f || z < pathMinZ - 3f || z > pathMaxZ + 3f) return 0f;
            float best = float.MaxValue;
            for (int i = 0; i < pathX.Length - 1; i++)
            {
                float ax = pathX[i], az = pathZ[i], bx = pathX[i + 1], bz = pathZ[i + 1];
                if (float.IsNaN(ax) || float.IsNaN(bx)) continue;
                float abx = bx - ax, abz = bz - az;
                float len2 = abx * abx + abz * abz;
                float t = len2 > 1e-6f ? Clamp01(((x - ax) * abx + (z - az) * abz) / len2) : 0f;
                float dx = x - (ax + abx * t), dz = z - (az + abz * t);
                float d2 = dx * dx + dz * dz;
                if (d2 < best) best = d2;
            }
            return 1f - SmoothStep(1.1f, 2.3f, MathF.Sqrt(best));
        }

        // ------------------------------------------------------------------ build (worker threads)
        public void Build(Action chunkDone)
        {
            if (!prepared) throw new InvalidOperationException("WorldTerrainData.PrepareStatics must run on the main thread first.");
            for (int cz = 0; cz < ChunksPerSide; cz++)
                for (int cx = 0; cx < ChunksPerSide; cx++)
                    res[cz * ChunksPerSide + cx] = IsFine(cx, cz) ? FineCells : CoarseCells;

            Parallel.For(0, Chunks.Length, k =>
            {
                Chunks[k] = BuildChunk(k % ChunksPerSide, k / ChunksPerSide);
                chunkDone?.Invoke();
            });
            int tris = 0;
            foreach (var c in Chunks) tris += 2 * c.N * c.N;
            TriangleCount = tris;
        }

        bool IsFine(int cx, int cz)
        {
            float x0 = Origin + cx * ChunkSize, z0 = Origin + cz * ChunkSize;
            float x1 = x0 + ChunkSize, z1 = z0 + ChunkSize;
            if (RectCircle(x0, z0, x1, z1, 0f, 0f, 340f)) return true;          // island + near shore
            for (int i = 0; i < siteX.Length; i++)
            {
                // the house reef blends 70 m beyond its radius; the other sites are explored up close inside their radius
                float pad = i == 0 ? 60f : 10f;
                if (RectCircle(x0, z0, x1, z1, siteX[i], siteZ[i], siteR[i] + pad)) return true;
            }
            return false;
        }

        static bool RectCircle(float x0, float z0, float x1, float z1, float cx, float cz, float r)
        {
            float px = cx < x0 ? x0 : (cx > x1 ? x1 : cx);
            float pz = cz < z0 ? z0 : (cz > z1 ? z1 : cz);
            float dx = px - cx, dz = pz - cz;
            return dx * dx + dz * dz < r * r;
        }

        int ResAt(int cx, int cz, int fallback)
        {
            if (cx < 0 || cz < 0 || cx >= ChunksPerSide || cz >= ChunksPerSide) return fallback;
            return res[cz * ChunksPerSide + cx];
        }

        WorldTerrainChunk BuildChunk(int cx, int cz)
        {
            int n = res[cz * ChunksPerSide + cx];
            int s = n + 1;
            float cell = ChunkSize / n;
            float x0 = Origin + cx * ChunkSize, z0 = Origin + cz * ChunkSize;
            var c = new WorldTerrainChunk
            {
                Cx = cx, Cz = cz, N = n, X0 = x0, Z0 = z0, Cell = cell,
                Vx = new float[s * s], Vy = new float[s * s], Vz = new float[s * s], DiagBC = new bool[n * n],
            };
            int rl = ResAt(cx - 1, cz, n), rr = ResAt(cx + 1, cz, n), rd = ResAt(cx, cz - 1, n), ru = ResAt(cx, cz + 1, n);
            int seed = n * 7919;
            float minY = float.MaxValue, maxY = float.MinValue;

            // ---- vertices
            for (int j = 0; j <= n; j++)
            {
                for (int i = 0; i <= n; i++)
                {
                    float x = x0 + i * cell, z = z0 + j * cell;
                    bool border = i == 0 || j == 0 || i == n || j == n;
                    float y;
                    if (!border)
                    {
                        int gx = cx * n + i, gz = cz * n + j;
                        x += (Hash01(gx, gz, seed + 1) - 0.5f) * 2f * Jitter * cell;
                        z += (Hash01(gx, gz, seed + 2) - 0.5f) * 2f * Jitter * cell;
                        y = source(x, z);
                    }
                    else if (i == 0 && rl < n) y = EdgeAlongZ(x, z0, j, n, rl, cell);
                    else if (i == n && rr < n) y = EdgeAlongZ(x, z0, j, n, rr, cell);
                    else if (j == 0 && rd < n) y = EdgeAlongX(z, x0, i, n, rd, cell);
                    else if (j == n && ru < n) y = EdgeAlongX(z, x0, i, n, ru, cell);
                    else y = source(x, z);
                    int k = j * s + i;
                    c.Vx[k] = x; c.Vy[k] = y; c.Vz[k] = z;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
            c.MinY = minY; c.MaxY = maxY;

            // ---- diagonals: follow the true surface where it matters (ridges, valleys), random on flat ground
            for (int j = 0; j < n; j++)
            {
                for (int i = 0; i < n; i++)
                {
                    int a = j * s + i, b = a + 1, cc = a + s, d = cc + 1;
                    float mx = (c.Vx[a] + c.Vx[d]) * 0.5f, mz = (c.Vz[a] + c.Vz[d]) * 0.5f;
                    float hc = source(mx, mz);
                    float eAD = Math.Abs((c.Vy[a] + c.Vy[d]) * 0.5f - hc);
                    float eBC = Math.Abs((c.Vy[b] + c.Vy[cc]) * 0.5f - hc);
                    bool bc;
                    if (Math.Abs(eAD - eBC) < 0.08f) bc = Hash01(cx * n + i, cz * n + j, seed + 3) < 0.5f;
                    else bc = eBC < eAD;
                    c.DiagBC[j * n + i] = bc;
                }
            }

            BuildRenderData(c, seed);
            return c;
        }

        /// Height of a vertex on an edge running along z that is shared with a coarser neighbour (m cells).
        float EdgeAlongZ(float x, float z0, int j, int n, int m, float cell)
        {
            int r = n / m;
            int rem = j % r;
            if (rem == 0) return source(x, z0 + j * cell);
            int j0 = j - rem;
            float t = rem / (float)r;
            return Lerp(source(x, z0 + j0 * cell), source(x, z0 + (j0 + r) * cell), t);
        }

        float EdgeAlongX(float z, float x0, int i, int n, int m, float cell)
        {
            int r = n / m;
            int rem = i % r;
            if (rem == 0) return source(x0 + i * cell, z);
            int i0 = i - rem;
            float t = rem / (float)r;
            return Lerp(source(x0 + i0 * cell, z), source(x0 + (i0 + r) * cell, z), t);
        }

        // ------------------------------------------------------------------ render data
        static void BuildRenderData(WorldTerrainChunk c, int seed)
        {
            int n = c.N, s = n + 1, triCount = 2 * n * n;
            const int classes = 11;
            var cls = new byte[triCount];
            var tri = new int[triCount * 3];
            var count = new int[classes];

            int t = 0;
            for (int j = 0; j < n; j++)
            {
                for (int i = 0; i < n; i++)
                {
                    int a = j * s + i, b = a + 1, cc = a + s, d = cc + 1;
                    bool bc = c.DiagBC[j * n + i];
                    for (int h = 0; h < 2; h++)
                    {
                        int p0, p1, p2;
                        if (bc) { if (h == 0) { p0 = a; p1 = cc; p2 = b; } else { p0 = b; p1 = cc; p2 = d; } }
                        else { if (h == 0) { p0 = a; p1 = cc; p2 = d; } else { p0 = a; p1 = d; p2 = b; } }
                        tri[t * 3] = p0; tri[t * 3 + 1] = p1; tri[t * 3 + 2] = p2;

                        float ex1 = c.Vx[p1] - c.Vx[p0], ey1 = c.Vy[p1] - c.Vy[p0], ez1 = c.Vz[p1] - c.Vz[p0];
                        float ex2 = c.Vx[p2] - c.Vx[p0], ey2 = c.Vy[p2] - c.Vy[p0], ez2 = c.Vz[p2] - c.Vz[p0];
                        float nx = ey1 * ez2 - ez1 * ey2, ny = ez1 * ex2 - ex1 * ez2, nz = ex1 * ey2 - ey1 * ex2;
                        float len = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
                        float upY = len > 1e-9f ? ny / len : 1f;

                        float mx = (c.Vx[p0] + c.Vx[p1] + c.Vx[p2]) / 3f;
                        float my = (c.Vy[p0] + c.Vy[p1] + c.Vy[p2]) / 3f;
                        float mz = (c.Vz[p0] + c.Vz[p1] + c.Vz[p2]) / 3f;
                        uint hash = Hash(c.Cx * n + i, c.Cz * n + j, seed + 11 + h);
                        var k = (byte)Classify(mx, mz, my, upY, hash);
                        cls[t] = k;
                        count[k]++;
                        t++;
                    }
                }
            }

            var start = new int[classes];
            int acc = 0;
            for (int k = 0; k < classes; k++) { start[k] = acc; acc += count[k]; }
            var verts = new Vector3[triCount * 3];
            var norms = new Vector3[triCount * 3];
            var fill = new int[classes];
            for (t = 0; t < triCount; t++)
            {
                int k = cls[t];
                int o = (start[k] + fill[k]++) * 3;
                int p0 = tri[t * 3], p1 = tri[t * 3 + 1], p2 = tri[t * 3 + 2];
                var v0 = new Vector3(c.Vx[p0], c.Vy[p0], c.Vz[p0]);
                var v1 = new Vector3(c.Vx[p1], c.Vy[p1], c.Vz[p1]);
                var v2 = new Vector3(c.Vx[p2], c.Vy[p2], c.Vz[p2]);
                var nrm = Vector3.Cross(v1 - v0, v2 - v0);
                float l = nrm.magnitude;
                nrm = l > 1e-9f ? nrm / l : Vector3.up;
                verts[o] = v0; verts[o + 1] = v1; verts[o + 2] = v2;
                norms[o] = nrm; norms[o + 1] = nrm; norms[o + 2] = nrm;
            }
            c.RenderVerts = verts;
            c.RenderNormals = norms;
            c.ClassCount = count;
        }

        /// Flat color class of one triangle (centroid x/z, centroid height h, normal.y, per-triangle hash for dithering).
        static TerrainClass Classify(float x, float z, float h, float ny, uint hash)
        {
            float r1 = (hash & 0x3FF) / 1023f;            // dithers class borders into scattered facets
            float r2 = ((hash >> 10) & 0x3FF) / 1023f;    // picks the occasional variant facet
            float slope = ny >= 1f ? 0f : MathF.Acos(Math.Max(-1f, ny)) * 57.29578f;
            float n2 = WorldNoise.Perlin(x * 0.11f + 2.5f, z * 0.11f + 9.5f);

            if (h > 0.35f)
            {
                float rockAt = 34f + 7f * n2;
                if (slope > rockAt + 13f) return TerrainClass.RockDark;
                if (slope > rockAt) return r2 < 0.2f ? TerrainClass.RockDark : TerrainClass.Rock;
                // small trampled yards (hut, helipad, lighthouse) fade out in scattered facets; paths are solid dirt
                if (Yard(x, z) > 0.15f + 0.7f * r1 || PathMask(x, z) > 0.3f + 0.4f * r1) return TerrainClass.Soil;
                float grassT = GrassBlend(x, z, h, n2);
                if (grassT > 0.2f + 0.6f * r1)
                {
                    // coherent light/dark meadow patches (darker jungle uphill) with a narrow dithered border
                    // and a few accent facets, instead of a busy per-triangle mosaic
                    float jungle = SmoothStep(-0.2f, 0.25f, WorldNoise.Fbm(x * 0.012f - 4f, z * 0.012f + 6f, 2)) * 0.75f
                                   + 0.4f * SmoothStep(6f, 18f, h);
                    bool dark = jungle > 0.5f + 0.16f * (r1 - 0.5f);
                    if (r2 < 0.035f) dark = !dark;
                    return dark ? TerrainClass.GrassDark : TerrainClass.Grass;
                }
                return r2 < 0.07f ? TerrainClass.SandAlt : TerrainClass.Sand;
            }
            if (h > -0.7f) return slope > 32f ? TerrainClass.Rock : TerrainClass.WetSand;

            float d = -h;
            float nd = WorldNoise.Fbm(x * 0.03f + 17f, z * 0.03f - 5f, 2);
            float rockAt2 = 30f + 7f * n2;
            bool steep = slope > rockAt2;
            if (d > 45f + 6f * nd + 6f * (r1 - 0.5f))
                return steep || r2 < 0.1f ? TerrainClass.RockDark : TerrainClass.DeepRock;
            float hint = WorldHeightField.RockHint(x, z) * Clamp01(0.75f + 0.5f * n2);
            if (steep || hint > 0.35f + 0.4f * r1)
                return d > 22f || slope > rockAt2 + 14f ? TerrainClass.RockDark : TerrainClass.Rock;
            // sand ripples as soft, coherent bands of the darker seabed tone (plus rare accent facets)
            float band = WorldNoise.Perlin(x * 0.045f + 11f, z * 0.045f - 7f);
            return band > 0.22f + 0.1f * (r1 - 0.5f) || r2 < 0.04f ? TerrainClass.SeabedAlt : TerrainClass.Seabed;
        }

        /// Sand (0) .. grass (1) blend of the dry land, exactly as Classify colors it (before the per-facet dithering:
        /// facets turn green where the blend exceeds 0.2..0.8).
        static float GrassBlend(float x, float z, float h, float n2)
        {
            float n1 = WorldNoise.Fbm(x * 0.03f + 17f, z * 0.03f - 5f, 2);
            return SmoothStep(1.3f, 2.7f, h + 0.8f * n1 + 0.35f * n2);
        }

        /// Shared "is this grass?" rule for props (grass tufts, dune tufts): 0 = sand-colored ground, 1 = grass, in
        /// between = the dithered sand/grass border. h = ground height (world y). Steep slopes (rock) are not
        /// considered here. Thread-safe and deterministic.
        public static float GrassAmount(float x, float z, float h)
        {
            if (h <= 0.35f) return 0f;
            float n2 = WorldNoise.Perlin(x * 0.11f + 2.5f, z * 0.11f + 9.5f);
            return GrassBlend(x, z, h, n2);
        }

        /// True on the trampled dirt yards (hut, helipad, lighthouse) and the dirt paths the terrain colors as Soil.
        /// Valid after PrepareStatics (always the case once the world is built).
        public static bool IsBareSoil(float x, float z)
        {
            if (!prepared) return false;
            return Yard(x, z) > 0.5f || PathMask(x, z) > 0.5f;
        }

        // ------------------------------------------------------------------ queries
        /// Height (world y) of the triangulated terrain surface. Thread-safe; clamps to the map.
        public float Height(float x, float z)
        {
            const float max = -Origin - 0.001f;
            if (x < Origin) x = Origin; else if (x > max) x = max;
            if (z < Origin) z = Origin; else if (z > max) z = max;
            int cx = (int)((x - Origin) / ChunkSize), cz = (int)((z - Origin) / ChunkSize);
            if (cx > ChunksPerSide - 1) cx = ChunksPerSide - 1;
            if (cz > ChunksPerSide - 1) cz = ChunksPerSide - 1;
            var c = Chunks[cz * ChunksPerSide + cx];
            return c != null ? c.HeightAt(x, z) : source(x, z);
        }

        /// Releases the per-chunk render arrays once the meshes exist.
        public void ReleaseRenderData(WorldTerrainChunk c) { c.RenderVerts = null; c.RenderNormals = null; }

        // ------------------------------------------------------------------ helpers
        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        static float Lerp(float a, float b, float t) => a + (b - a) * t;
        static float SmoothStep(float e0, float e1, float x)
        {
            float t = Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        public static uint Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return h;
            }
        }

        public static float Hash01(int x, int y, int seed) => (Hash(x, y, seed) & 0xFFFFFF) / 16777216f;
    }
}
