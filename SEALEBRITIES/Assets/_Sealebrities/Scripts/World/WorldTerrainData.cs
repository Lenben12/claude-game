using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Sealebrities
{
    /// Color class of one terrain triangle; each class is one shared flat LowPoly material (one submesh per chunk).
    /// SandPink (round 2b, K.8): the pink coral sand of the secret Fog Isle (its beaches and lagoon floor).
    public enum TerrainClass : byte { Sand, SandAlt, WetSand, Grass, GrassDark, Soil, Rock, RockDark, Seabed, SeabedAlt, DeepRock, GrassDry, SandPink }

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

    /// The whole low-poly terrain as data: 16 x 16 chunks of 128 m, ultra fine (1.6 m) where the island's points of interest
    /// need crisp shapes (waterfall cliff, cave, ravine, tide pools) and on the house reef's seabed detail zone, fine
    /// (3.2 m) around the island, the house reef, every dive site and the landmark islands, coarse (6.4 m) elsewhere
    /// (≈ 450k triangles in total). Built on worker threads from WorldHeightField;
    /// Height() is thread-safe and returns the height of the actual triangulated surface, so props and creatures sit
    /// exactly on the rendered ground.
    public sealed class WorldTerrainData
    {
        public const float ChunkSize = 128f;
        public const int ChunksPerSide = 16;
        public const int UltraCells = 80, FineCells = 40, CoarseCells = 20;   // each must divide the next (crack-free stitching)
        /// Number of TerrainClass values (= max submeshes per chunk).
        public const int Classes = 13;
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
        static float fogSiteX, fogSiteZ = 640f;
        static float[] pathX, pathZ;
        static float pathMinX, pathMaxX, pathMinZ, pathMaxZ;
        static int[][] pathGrid;                 // segment start indices per PathCell grid cell
        const float PathCell = 16f, PathGridMin = -320f;
        const int PathGridN = 40;
        static readonly float[] yardX = new float[4], yardZ = new float[4], yardR0 = new float[4], yardR1 = new float[4];
        static Vector2[][] pathPolylines = new Vector2[0][];

        /// Sampled centre lines (world x/z) of every dirt path and trail the terrain paints – normally the props' trail
        /// network (PropPaths.Routes sampled exactly like PropPaths does, 1 m spacing, boardwalk excluded). Valid after
        /// PrepareStatics (always once the world is built).
        public static IReadOnlyList<Vector2[]> PathPolylines => pathPolylines;

        /// Fallback trails to the points of interest, used only when the props' trail network is unavailable: the L.5
        /// route set of the 0.62 island (Testversion-3 map literals wrapped in WorldLayout.Isl, the rest anchor-relative).
        public static Vector2[][] TrailControlPoints()
        {
            var rv = WorldLayout.Ravine;
            var rt = WorldLayout.RadioTowerRuins;
            var heli = WorldLayout.Helipad;
            var sb = WorldLayout.SecondBeach;
            var tp = WorldLayout.TidePools;
            var wt = WorldLayout.WaterfallTop;
            var cave = WorldLayout.CaveEntrance;
            Vector2 rampTop = XZ(WorldFeatures.FallRampTop), rampBottom = XZ(WorldFeatures.FallRampBottom), poolBeach = XZ(WorldFeatures.PoolBeach);
            Vector2 caveFront = XZ(cave) - XZ(WorldFeatures.CaveDir) * 6f;
            return new[]
            {
                // lighthouse/waterfall junction → down the cliff ramp → pool beach
                new[] { WorldLayout.Isl(22f, 114f), new Vector2(wt.x + 22f, wt.z + 20f), new Vector2(wt.x + 9f, wt.z + 9f), rampTop, rampBottom, rampBottom + new Vector2(9f, -9f), poolBeach },
                // foot of the ramp → Waterfall Grotto
                new[] { rampBottom, XZ(WorldFeatures.FallFoot) + XZ(WorldFeatures.FallDir) * 9f, caveFront },
                // pool beach → rope bridge (north rim)
                new[] { poolBeach, WorldLayout.Isl(-44f, 22f), WorldLayout.Isl(-33f, 2f), new Vector2(rv.x - 3f, rv.z + 20f), new Vector2(rv.x, rv.z + 11f) },
                // rope bridge (south rim) → radio tower ruins
                new[] { new Vector2(rv.x, rv.z - 11f), new Vector2(rv.x - 6f, rv.z - 20f), WorldLayout.Isl(-48f, -76f), WorldLayout.Isl(-78f, -96f), new Vector2(rt.x + 10f, rt.z + 3f) },
                // radio tower → gully → hidden bay beach
                new[] { new Vector2(rt.x - 6f, rt.z + 8f), XZ(WorldFeatures.BayGullyTop), XZ(WorldFeatures.BayGullyBottom), XZ(WorldFeatures.HiddenBayBeach) },
                // ravine (south) → helipad
                new[] { new Vector2(rv.x + 2f, rv.z - 16f), new Vector2(-1f, -45f), new Vector2(14f, -47f), new Vector2(heli.x - 9f, heli.z - 2f) },
                // helipad → village Altriff
                new[] { new Vector2(heli.x - 2f, heli.z - 10f), WorldLayout.Isl(60f, -100f), WorldLayout.Isl(52f, -130f), new Vector2(sb.x + 4f, sb.z + 22f), new Vector2(sb.x, sb.z + 10f) },
                // village → tide pools along the south-east coast
                new[] { new Vector2(sb.x + 12f, sb.z + 10f), WorldLayout.Isl(80f, -158f), WorldLayout.Isl(110f, -138f), WorldLayout.Isl(132f, -114f), new Vector2(tp.x - 6f, tp.z + 4f) },
                // tide pools → hut
                new[] { new Vector2(tp.x - 8f, tp.z + 9f), WorldLayout.Isl(134f, -62f), WorldLayout.Isl(124f, -36f), WorldLayout.Isl(113f, -10f) },
            };
        }

        static Vector2 XZ(Vector3 p) => new Vector2(p.x, p.z);

        /// Caches dive site and path layout in plain arrays. Call on the main thread before building.
        public static void PrepareStatics()
        {
            if (prepared) return;
            WorldHeightField.Height(0f, 0f);   // static ctor reads DiveSites / WorldLayout on the main thread
            var sites = DiveSites.All;
            int n = sites != null ? sites.Count : 0;
            siteX = new float[n]; siteZ = new float[n]; siteR = new float[n];
            fogSiteX = WorldLayout.FogIsland.x; fogSiteZ = WorldLayout.FogIsland.z - 60f;
            for (int i = 0; i < n; i++)
            {
                siteX[i] = sites[i].center.x; siteZ[i] = sites[i].center.z; siteR[i] = sites[i].radius;
                if (sites[i].id == "fog_isle") { fogSiteX = siteX[i]; fogSiteZ = siteZ[i]; }
            }
            SetYard(0, WorldLayout.Hut, 7f, 13f);
            SetYard(1, WorldLayout.Helipad, 6f, 11f);
            SetYard(2, WorldLayout.Lighthouse, 5f, 9f);
            SetYard(3, WorldLayout.RadioTowerRuins, 7f, 11f);
            BuildPaths();
            prepared = true;
        }

        static void SetYard(int i, Vector3 p, float r0, float r1) { yardX[i] = p.x; yardZ[i] = p.z; yardR0[i] = r0; yardR1[i] = r1; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { prepared = false; }

        /// Dirt paths under the props' path stones: hut -> lighthouse and hut -> helipad (same control points as the props),
        /// plus the trails to the points of interest.
        static void BuildPaths()
        {
            var hut = WorldLayout.Hut;
            float stepsX = hut.x + 7.2f + 2.5f;
            var lh = WorldLayout.Lighthouse;
            float tx = hut.x - lh.x, tz = hut.z - lh.z, tl = Mathf.Sqrt(tx * tx + tz * tz);
            float doorX = lh.x + tx / tl * 3.4f, doorZ = lh.z + tz / tl * 3.4f;
            // (fallback only: the same L.5 routes the props draw – Testversion-3 literals scaled by WorldLayout.Isl)
            var main = new[]
            {
                new Vector2(stepsX + 1.5f, hut.z + 1f), new Vector2(stepsX - 1f, hut.z + 9f), WorldLayout.Isl(110f, 36f), WorldLayout.Isl(94f, 50f),
                WorldLayout.Isl(72f, 70f), WorldLayout.Isl(48f, 92f), WorldLayout.Isl(22f, 114f), WorldLayout.Isl(2f, 131f), WorldLayout.Isl(-14f, 142f),
                new Vector2(doorX, doorZ),
            };
            var heli = WorldLayout.Helipad;
            var side = new[]
            {
                new Vector2(hut.x + 6f, hut.z - 7f), WorldLayout.Isl(112f, -8f), WorldLayout.Isl(98f, -32f), new Vector2(heli.x + 9f, heli.z + 9f),
            };
            var dock = new[]
            {
                new Vector2(stepsX + 2f, hut.z - 1.5f), WorldLayout.Isl(140f, 13.5f), WorldLayout.Isl(150f, 9.5f), new Vector2(WorldLayout.DockStart.x - 10f, WorldLayout.DockStart.z + 0.2f),
            };
            // The props module owns the trail network (PropPaths.Routes, pure functions of WorldLayout): paint exactly its
            // sampled dirt lines (same seeds and wobble) so the terrain dirt lies under the props' strips and stones.
            var dense = new List<Vector2[]>();
            try
            {
                var routes = PropPaths.Routes;
                for (int r = 0; r < routes.Count; r++)
                {
                    var rp = routes[r].points;
                    if (routes[r].boardwalk || rp == null || rp.Length < 2) continue;
                    dense.Add(PropPaths.SampleSpline(rp, 1f, 40 + r * 13, 1.4f).ToArray());
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("Sealebrities: trail network from the props unavailable, using the world's own trails (" + e.Message + ").");
                dense.Clear();
            }

            var xs = new List<float>();
            var zs = new List<float>();
            var lines = new List<Vector2[]>();
            if (dense.Count > 0)
            {
                for (int p = 0; p < dense.Count; p++)
                {
                    if (p > 0) { xs.Add(float.NaN); zs.Add(float.NaN); }   // break between two paths
                    for (int i = 0; i < dense[p].Length; i++) { xs.Add(dense[p][i].x); zs.Add(dense[p][i].y); }
                    lines.Add(dense[p]);
                }
            }
            else
            {
                var all = new List<Vector2[]> { dock, main, side };
                all.AddRange(TrailControlPoints());
                for (int p = 0; p < all.Count; p++)
                {
                    if (p > 0) { xs.Add(float.NaN); zs.Add(float.NaN); }   // break between two paths
                    int first = xs.Count;
                    Spline(all[p], xs, zs);
                    var line = new Vector2[xs.Count - first];
                    for (int i = 0; i < line.Length; i++) line[i] = new Vector2(xs[first + i], zs[first + i]);
                    lines.Add(line);
                }
            }
            pathPolylines = lines.ToArray();
            pathX = xs.ToArray(); pathZ = zs.ToArray();
            pathMinX = pathMinZ = float.MaxValue; pathMaxX = pathMaxZ = float.MinValue;
            for (int i = 0; i < pathX.Length; i++)
            {
                if (float.IsNaN(pathX[i])) continue;
                pathMinX = Math.Min(pathMinX, pathX[i]); pathMaxX = Math.Max(pathMaxX, pathX[i]);
                pathMinZ = Math.Min(pathMinZ, pathZ[i]); pathMaxZ = Math.Max(pathMaxZ, pathZ[i]);
            }

            // spatial index: every segment is listed in each grid cell its (padded) bounds touch
            var cells = new List<int>[PathGridN * PathGridN];
            for (int i = 0; i < pathX.Length - 1; i++)
            {
                float ax = pathX[i], az = pathZ[i], bx = pathX[i + 1], bz = pathZ[i + 1];
                if (float.IsNaN(ax) || float.IsNaN(bx)) continue;
                int c0 = CellOf(Math.Min(ax, bx) - 3f), c1 = CellOf(Math.Max(ax, bx) + 3f);
                int r0 = CellOf(Math.Min(az, bz) - 3f), r1 = CellOf(Math.Max(az, bz) + 3f);
                for (int r = r0; r <= r1; r++)
                    for (int c = c0; c <= c1; c++)
                    {
                        int k = r * PathGridN + c;
                        (cells[k] ??= new List<int>(8)).Add(i);
                    }
            }
            pathGrid = new int[cells.Length][];
            for (int k = 0; k < cells.Length; k++) pathGrid[k] = cells[k] != null ? cells[k].ToArray() : null;
        }

        static int CellOf(float v)
        {
            int c = (int)Math.Floor((v - PathGridMin) / PathCell);
            return c < 0 ? 0 : (c >= PathGridN ? PathGridN - 1 : c);
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

        /// 0..1 dirt yard around the hut, the helipad, the lighthouse and the radio tower ruins (tighter than the flat
        /// building plateaus).
        static float Yard(float x, float z)
        {
            float best = 0f;
            for (int i = 0; i < yardX.Length; i++)
                best = Math.Max(best, 1f - SmoothStep(yardR0[i], yardR1[i], Dist(x, z, yardX[i], yardZ[i])));
            return best;
        }

        static float Dist(float x, float z, float cx, float cz)
        {
            float dx = x - cx, dz = z - cz;
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        /// 0..1, 1 on the dirt paths.
        static float PathMask(float x, float z)
        {
            if (pathX == null || pathGrid == null || x < pathMinX - 3f || x > pathMaxX + 3f || z < pathMinZ - 3f || z > pathMaxZ + 3f) return 0f;
            var segs = pathGrid[CellOf(z) * PathGridN + CellOf(x)];
            if (segs == null) return 0f;
            float best = float.MaxValue;
            for (int s = 0; s < segs.Length; s++)
            {
                int i = segs[s];
                float ax = pathX[i], az = pathZ[i], bx = pathX[i + 1], bz = pathZ[i + 1];
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
                    res[cz * ChunksPerSide + cx] = IsUltra(cx, cz) ? UltraCells : (IsFine(cx, cz) ? FineCells : CoarseCells);

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
            // the landmark islands (beaches, rocks and their shallow shelf)
            var mi = WorldFeatures.MiniIslands;
            for (int i = 0; i < mi.Length; i++)
                if (RectCircle(x0, z0, x1, z1, mi[i].center.x, mi[i].center.z, mi[i].shoreRadius + 18f)) return true;
            // round 3: the volcano island and its black-sand shelf
            var vol = WorldLayout.VolcanoIsland;
            if (RectCircle(x0, z0, x1, z1, vol.x, vol.z, WorldLayout.VolcanoShoreRadius + 40f)) return true;
            var ab = WorldLayout.VolcanoAbyss;
            if (RectCircle(x0, z0, x1, z1, ab.x, ab.z, WorldLayout.VolcanoAbyssRadius + 6f)) return true;
            return false;
        }

        /// Chunks holding a point of interest whose shape needs 1.6 m facets (cliff, cave passage, ravine, tide pools) and
        /// the house reef's seabed detail zone (L.3/Q11: reef crest, spur-and-groove channels, short sand ripples).
        static bool IsUltra(int cx, int cz)
        {
            float x0 = Origin + cx * ChunkSize, z0 = Origin + cz * ChunkSize;
            float x1 = x0 + ChunkSize, z1 = z0 + ChunkSize;
            var rd = WorldFeatures.ReefDetailRect;
            if (x0 > rd.x - 1f && x1 < rd.z + 1f && z0 > rd.y - 1f && z1 < rd.w + 1f) return true;
            if (RectCircle(x0, z0, x1, z1, WorldLayout.WaterfallTop.x, WorldLayout.WaterfallTop.z, 30f)) return true;
            if (RectCircle(x0, z0, x1, z1, WorldLayout.WaterfallPool.x, WorldLayout.WaterfallPool.z, 16f)) return true;
            var cave = WorldLayout.CaveEntrance + WorldFeatures.CaveDir * 6.5f;
            if (RectCircle(x0, z0, x1, z1, cave.x, cave.z, 13f)) return true;
            if (RectCircle(x0, z0, x1, z1, WorldLayout.TidePools.x, WorldLayout.TidePools.z, 20f)) return true;
            float rz = WorldLayout.Ravine.z;
            return x1 > WorldFeatures.RavineWestX + 8f && x0 < WorldFeatures.RavineEastX - 8f && z1 > rz - 14f && z0 < rz + 14f;
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
            const int classes = Classes;
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
                        var k = (byte)VolcanoTint(FogIsleTint(Classify(mx, mz, my, upY, hash), mx, mz, hash), mx, mz, my, hash);
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

        /// K.8 "distinct look" of the secret Fog Isle: its sand – beaches and the seamount lagoon floor – is pink coral sand
        /// (dithered border ~70 m around the island and the site centre). Everything else keeps its class.
        static TerrainClass FogIsleTint(TerrainClass k, float x, float z, uint hash)
        {
            if (k != TerrainClass.Sand && k != TerrainClass.SandAlt && k != TerrainClass.Seabed && k != TerrainClass.SeabedAlt) return k;
            var isl = WorldLayout.FogIsland;
            float dx = x - isl.x, dz = z - isl.z;
            float d = MathF.Sqrt(dx * dx + dz * dz);
            float sx = x - fogSiteX, sz = z - fogSiteZ;
            float ds = MathF.Sqrt(sx * sx + sz * sz);
            float near = MathF.Min(d, ds);
            if (near > 80f) return k;
            float r = ((hash >> 7) & 0x3FF) / 1023f;
            return near < 62f + 16f * r ? TerrainClass.SandPink : k;
        }

        /// Round 3 "every island individual": the volcano island has black sand (DeepRock facets with a few dark-grey
        /// sparkles), dark lava rock, a darker sea floor around it and lush dark-green fern slopes. Others keep their class.
        static TerrainClass VolcanoTint(TerrainClass k, float x, float z, float h, uint hash)
        {
            if (!WorldHeightField.InVolcanoZone(x, z, out float d)) return k;
            float r = ((hash >> 7) & 0x3FF) / 1023f;
            float shore = WorldLayout.VolcanoShoreRadius;
            switch (k)
            {
                case TerrainClass.Sand:
                case TerrainClass.SandAlt:
                    return r < 0.12f ? TerrainClass.RockDark : TerrainClass.DeepRock;
                case TerrainClass.WetSand:
                    return TerrainClass.DeepRock;
                case TerrainClass.Seabed:
                case TerrainClass.SeabedAlt:
                    // black sand on the shelf, fading into the normal pale sand ~50–70 m out
                    if (d < shore + 45f + 20f * r) return r < 0.2f ? TerrainClass.RockDark : TerrainClass.DeepRock;
                    return k;
                case TerrainClass.Rock:
                    return h > 0.3f && r < 0.6f ? TerrainClass.RockDark : k;
                case TerrainClass.Grass:
                case TerrainClass.GrassDry:
                    return r < 0.75f ? TerrainClass.GrassDark : k;
                case TerrainClass.Soil:
                    return TerrainClass.RockDark;
                default:
                    return k;
            }
        }

        /// Flat color class of one triangle (centroid x/z, centroid height h, normal.y, per-triangle hash for dithering).
        static TerrainClass Classify(float x, float z, float h, float ny, uint hash)
        {
            float r1 = (hash & 0x3FF) / 1023f;            // dithers class borders into scattered facets
            float r2 = ((hash >> 10) & 0x3FF) / 1023f;    // picks the occasional variant facet
            float r3 = ((hash >> 20) & 0x3FF) / 1023f;    // dry-grass fringe
            float slope = ny >= 1f ? 0f : MathF.Acos(Math.Max(-1f, ny)) * 57.29578f;
            float n2 = WorldNoise.Perlin(x * 0.11f + 2.5f, z * 0.11f + 9.5f);
            var feature = h > -1.5f ? WorldHeightField.Hint(x, z, h) : GroundHint.None;

            if (h > 0.35f)
            {
                float rockAt = 34f + 7f * n2;
                if (slope > rockAt + 13f) return TerrainClass.RockDark;
                if (slope > rockAt) return r2 < 0.2f ? TerrainClass.RockDark : TerrainClass.Rock;
                switch (feature)
                {
                    case GroundHint.Rock: return r2 < 0.3f ? TerrainClass.RockDark : TerrainClass.Rock;
                    case GroundHint.Sand: return r2 < 0.1f ? TerrainClass.SandAlt : TerrainClass.Sand;
                    case GroundHint.Soil: return r2 < 0.12f ? TerrainClass.GrassDry : TerrainClass.Soil;
                    case GroundHint.WetSand: return TerrainClass.WetSand;
                }
                // small trampled yards (hut, helipad, lighthouse) fade out in scattered facets; paths are solid dirt
                if (Yard(x, z) > 0.15f + 0.7f * r1 || PathMask(x, z) > 0.3f + 0.4f * r1) return TerrainClass.Soil;
                // round 3: the hermits' game trail is only a faint strip of trampled, dry grass (no dirt path)
                if (h > 12f && WorldHeightField.CampTrailMask(x, z) > 0.45f + 0.5f * r1) return r2 < 0.25f ? TerrainClass.Soil : TerrainClass.GrassDry;
                // art polish: the sand/grass border follows a coherent noise edge with only a little per-facet jitter
                // (jagged triangle rows, How to Fish) – the old per-facet random 0.2-0.8 threshold spread a 20 m
                // checkerboard of big green/sand facets over flat beach terraces where the blend sits near 0.5
                float grassT = GrassBlend(x, z, h, n2) - GrassEdge(x, z) + 0.5f;
                if (grassT > 0.5f + 0.08f * (r1 - 0.5f))
                {
                    // dry, yellowish grass fringe (2-4 m) along the sand border (~30 % of its facets)
                    if (grassT < 0.78f && r3 < 0.3f) return TerrainClass.GrassDry;
                    // coherent light/dark meadow patches (darker jungle uphill) with a narrow dithered border
                    // and a few accent facets, instead of a busy per-triangle mosaic
                    float jungle = SmoothStep(-0.2f, 0.25f, WorldNoise.Fbm(x * 0.012f - 4f, z * 0.012f + 6f, 2)) * 0.75f
                                   + 0.4f * SmoothStep(6f, 18f, h);
                    bool dark = jungle > 0.5f + 0.16f * (r1 - 0.5f);
                    if (r2 < 0.035f) dark = !dark;
                    return dark ? TerrainClass.GrassDark : TerrainClass.Grass;
                }
                // a 1.2-2.5 m band of wet sand right behind the waterline
                if (h < 0.1f + 0.14f * r1) return TerrainClass.WetSand;
                return r2 < 0.07f ? TerrainClass.SandAlt : TerrainClass.Sand;
            }
            if (h > -0.7f)
            {
                if (feature == GroundHint.Rock || slope > 32f) return r2 < 0.3f ? TerrainClass.RockDark : TerrainClass.Rock;
                return h > 0.1f + 0.14f * r1 ? TerrainClass.Sand : TerrainClass.WetSand;
            }
            if (feature == GroundHint.Rock) return r2 < 0.35f ? TerrainClass.RockDark : TerrainClass.Rock;

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
            // rock platforms, beaches, creek beds and gravel floors of the points of interest stay bare
            if (WorldHeightField.Hint(x, z, h) != GroundHint.None) return 0f;
            float n2 = WorldNoise.Perlin(x * 0.11f + 2.5f, z * 0.11f + 9.5f);
            // shifted by the same coherent edge noise the terrain colors use, so 0.5 is exactly the painted border
            float g = GrassBlend(x, z, h, n2) - GrassEdge(x, z) + 0.5f;
            return g < 0f ? 0f : (g > 1f ? 1f : g);
        }

        /// Grass/sand border threshold (around 0.5): a low-frequency noise so the border forms coherent patches.
        static float GrassEdge(float x, float z) => 0.5f + 0.35f * WorldNoise.Perlin(x * 0.09f + 5.3f, z * 0.09f - 1.7f);

        /// True on the trampled dirt yards (hut, helipad, lighthouse, radio tower ruins) and the dirt paths and trails the
        /// terrain colors as Soil. Valid after PrepareStatics (always the case once the world is built).
        public static bool IsBareSoil(float x, float z)
        {
            if (!prepared) return false;
            return Yard(x, z) > 0.5f || PathMask(x, z) > 0.5f;
        }

        /// Ground class the terrain paints at (x, z) for a ground height h (ignores slope-rock and per-facet dithering):
        /// lets props avoid putting grass or palms on rock platforms, gravel beds or wet sand.
        public static GroundHint GroundAt(float x, float z, float h)
        {
            var hint = WorldHeightField.Hint(x, z, h);
            if (hint != GroundHint.None) return hint;
            if (prepared && (Yard(x, z) > 0.5f || PathMask(x, z) > 0.5f)) return GroundHint.Soil;
            if (h < 0.2f) return GroundHint.WetSand;
            return GrassAmount(x, z, h) < 0.5f ? GroundHint.Sand : GroundHint.None;
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
