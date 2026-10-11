using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// The island trail network (GDD 15.33, art bible §1.8): dirt paths 1.6–2.4 m wide (PathDirt #A88763) connecting
    /// hut → dock → helipad → waterfall → cave → ravine (rope bridge) → lighthouse → tide pools → second beach →
    /// radio tower ruins → hidden bay, a plank boardwalk over the sand to the dock, and signposts with pictograms at
    /// the junctions. Routes are pure functions of WorldLayout (Routes / DistanceToPath), so the world module may paint
    /// the same lines into the terrain before the props exist.
    public static class PropPaths
    {
        public struct Route
        {
            public string name;
            public Vector2[] points;
            public bool boardwalk;
        }

        /// One sample of the built paths (for path dressing and collectible placement).
        public struct Sample
        {
            public Vector3 pos;
            public Vector3 dir;
            public float halfWidth;
        }

        public static readonly List<Sample> Samples = new List<Sample>();

        static List<Route> routes;
        static List<Vector2> dense;     // all routes sampled every ~1 m (NaN breaks between routes)
        /// Short connector trails added by the POI builders (e.g. from a route end to where the bridge actually is).
        static readonly List<Vector2[]> Connections = new List<Vector2[]>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { routes = null; dense = null; Samples.Clear(); Connections.Clear(); }

        public static void ClearConnections() => Connections.Clear();

        /// Adds a short straight trail between two points (built by Build together with the routes).
        public static void AddConnection(Vector2 a, Vector2 b)
        {
            if ((a - b).sqrMagnitude < 1f) return;
            Connections.Add(new[] { a, Vector2.Lerp(a, b, 0.5f), b });
        }

        static Vector2 V(float x, float z) => new Vector2(x, z);
        static Vector2 XZ(Vector3 p) => new Vector2(p.x, p.z);
        /// A Testversion-3 map literal on the round-2b island (L.5: × WorldLayout.IslandScale).
        static Vector2 I(float x, float z) => WorldLayout.Isl(x, z);

        /// Front of the Waterfall Grotto mouth (L.5: the cave opens in the waterfall cliff).
        public static Vector2 CaveFront => XZ(WorldLayout.CaveEntrance) - XZ(WorldFeatures.CaveDir) * 6f;

        /// Control points of every trail (deterministic, from WorldLayout only). Round 2b (L.5): the probe routes of
        /// scratchpad/design2_l/r3 (681 m in total, every trail ≤ 3 m steeper than 32°) + a spur to the driftwood beach.
        public static List<Route> Routes
        {
            get
            {
                if (routes != null) return routes;
                var hut = WorldLayout.Hut;
                var lh = WorldLayout.Lighthouse;
                Vector2 toHut = new Vector2(hut.x - lh.x, hut.z - lh.z).normalized;
                Vector2 door = new Vector2(lh.x, lh.z) + toHut * 3.4f;
                float stepsX = hut.x + 7.2f + 2.5f;
                var heli = WorldLayout.Helipad;
                var wt = WorldLayout.WaterfallTop;
                var rv = WorldLayout.Ravine;
                var tp = WorldLayout.TidePools;
                var sb = WorldLayout.SecondBeach;
                var rt = WorldLayout.RadioTowerRuins;
                var dw = WorldLayout.DriftwoodBeach;
                routes = new List<Route>
                {
                    new Route { name = "dock", boardwalk = true, points = new[] { V(stepsX + 2f, hut.z - 1.5f), I(140f, 13.5f), I(150f, 9.5f), V(WorldLayout.DockStart.x - 10f, WorldLayout.DockStart.z + 0.2f) } },
                    new Route { name = "lighthouse", points = new[] { V(stepsX + 1.5f, hut.z + 1f), V(stepsX - 1f, hut.z + 9f), I(110f, 36f), I(94f, 50f), I(72f, 70f), I(48f, 92f), I(22f, 114f), I(2f, 131f), I(-14f, 142f), door } },
                    new Route { name = "helipad", points = new[] { V(hut.x + 6f, hut.z - 7f), I(112f, -8f), I(98f, -32f), V(heli.x + 9f, heli.z + 9f) } },
                    // L.5: from the lighthouse trail over the plateau, down the ramp in the waterfall cliff to the pool beach
                    new Route { name = "waterfall", points = new[] { I(22f, 114f), V(wt.x + 22f, wt.z + 20f), V(wt.x + 9f, wt.z + 9f), XZ(WorldFeatures.FallRampTop), XZ(WorldFeatures.FallRampBottom), XZ(WorldFeatures.FallRampBottom) + V(9f, -9f), XZ(WorldFeatures.PoolBeach) } },
                    // L.5: the Waterfall Grotto opens in the same cliff – from the foot of the ramp along the amphitheater floor
                    new Route { name = "cave", points = new[] { XZ(WorldFeatures.FallRampBottom), XZ(WorldFeatures.FallFoot) + XZ(WorldFeatures.FallDir) * 9f, CaveFront } },
                    new Route { name = "ravine_north", points = new[] { XZ(WorldFeatures.PoolBeach), I(-44f, 22f), I(-33f, 2f), V(rv.x - 3f, rv.z + 20f), V(rv.x, rv.z + 11f) } },
                    new Route { name = "ravine_south", points = new[] { V(rv.x, rv.z - 11f), V(rv.x - 6f, rv.z - 20f), I(-48f, -76f), I(-78f, -96f), V(rt.x + 10f, rt.z + 3f) } },
                    // through the gully the world cuts into the cove's southern ridge, down to the hidden beach
                    new Route { name = "bay", points = new[] { V(rt.x - 6f, rt.z + 8f), XZ(WorldFeatures.BayGullyTop), Vector2.Lerp(XZ(WorldFeatures.BayGullyTop), XZ(WorldFeatures.BayGullyBottom), 0.5f), XZ(WorldFeatures.BayGullyBottom), XZ(WorldFeatures.HiddenBayBeach) } },
                    new Route { name = "heli_ravine", points = new[] { V(rv.x + 2f, rv.z - 16f), V(-1f, -45f), V(14f, -47f), V(heli.x - 9f, heli.z - 2f) } },
                    // the village Altriff (L.8) on the old second beach: the trail ends on the terrace
                    new Route { name = "second_beach", points = new[] { V(heli.x - 2f, heli.z - 10f), I(60f, -100f), I(52f, -130f), V(sb.x + 4f, sb.z + 22f), V(sb.x, sb.z + 10f) } },
                    new Route { name = "tidepools", points = new[] { V(sb.x + 12f, sb.z + 10f), I(80f, -158f), I(110f, -138f), I(132f, -114f), V(tp.x - 6f, tp.z + 4f) } },
                    new Route { name = "tide_hut", points = new[] { V(tp.x - 8f, tp.z + 9f), I(134f, -62f), I(124f, -36f), I(113f, -10f) } },
                    // round 2b: spur from the lighthouse trail down to the driftwood beach on the north-east coast (K.8)
                    new Route { name = "driftwood", points = new[] { I(72f, 70f), V(52f, 60f), V(57f, 76f), V(dw.x - 1f, dw.z - 8f) } },
                };
                return routes;
            }
        }

        /// Signpost junctions: position and up to three (target, direction point) boards.
        struct Junction
        {
            public Vector2 at;
            public PropIcon[] icons;
            public Vector2[] toward;
        }

        static Junction[] Junctions()
        {
            var hut = WorldLayout.Hut;
            var lh = WorldLayout.Lighthouse;
            var heli = WorldLayout.Helipad;
            var wt = WorldLayout.WaterfallTop;
            var rv = WorldLayout.Ravine;
            var tp = WorldLayout.TidePools;
            var sb = WorldLayout.SecondBeach;
            var rt = WorldLayout.RadioTowerRuins;
            var bay = WorldLayout.HiddenBay;
            var dw = WorldLayout.DriftwoodBeach;
            float stepsX = hut.x + 7.2f + 2.5f;
            Vector2 rampFoot = XZ(WorldFeatures.FallRampBottom);
            return new[]
            {
                // L.5 junctions at the 0.62 layout (every sign points along the first stretch of its trail)
                new Junction { at = V(stepsX + 4.5f, hut.z + 4.5f), icons = new[] { PropIcon.Lighthouse, PropIcon.Dock, PropIcon.Helipad }, toward = new[] { I(110f, 36f), I(150f, 9.5f), I(112f, -8f) } },
                new Junction { at = I(22f, 114f) + V(2.5f, -2.5f), icons = new[] { PropIcon.Lighthouse, PropIcon.Waterfall, PropIcon.Hut }, toward = new[] { I(2f, 131f), V(wt.x + 22f, wt.z + 20f), I(48f, 92f) } },
                new Junction { at = I(72f, 70f) + V(2.5f, 1.5f), icons = new[] { PropIcon.Driftwood, PropIcon.Lighthouse, PropIcon.Hut }, toward = new[] { V(52f, 60f), I(48f, 92f), I(94f, 50f) } },
                new Junction { at = rampFoot + V(2.5f, -2f), icons = new[] { PropIcon.Cave, PropIcon.Waterfall, PropIcon.Bridge }, toward = new[] { XZ(WorldFeatures.FallFoot) + XZ(WorldFeatures.FallDir) * 9f, XZ(WorldFeatures.FallRampTop), rampFoot + V(9f, -9f) } },
                new Junction { at = XZ(WorldFeatures.PoolBeach) + V(2.5f, -1f), icons = new[] { PropIcon.Waterfall, PropIcon.Bridge }, toward = new[] { XZ(WorldLayout.WaterfallPool), I(-44f, 22f) } },
                new Junction { at = V(rv.x + 4f, rv.z - 15f), icons = new[] { PropIcon.Bridge, PropIcon.Radio, PropIcon.Helipad }, toward = new[] { V(rv.x, rv.z), I(-48f, -76f), V(-1f, -45f) } },
                new Junction { at = V(heli.x - 7f, heli.z - 9f), icons = new[] { PropIcon.Helipad, PropIcon.Village, PropIcon.Bridge }, toward = new[] { V(heli.x, heli.z), I(60f, -100f), V(14f, -47f) } },
                new Junction { at = V(tp.x - 3f, tp.z + 10f), icons = new[] { PropIcon.TidePools, PropIcon.Hut, PropIcon.Village }, toward = new[] { V(tp.x, tp.z), I(134f, -62f), I(132f, -114f) } },
                new Junction { at = V(rt.x + 4f, rt.z + 9f), icons = new[] { PropIcon.Radio, PropIcon.Bay, PropIcon.Bridge }, toward = new[] { V(rt.x, rt.z), XZ(WorldFeatures.BayGullyTop), I(-78f, -96f) } },
                new Junction { at = V(sb.x + 8f, sb.z + 14f), icons = new[] { PropIcon.Village, PropIcon.TidePools, PropIcon.Helipad }, toward = new[] { V(sb.x, sb.z), I(80f, -158f), I(52f, -130f) } },
                new Junction { at = XZ(WorldFeatures.BayGullyBottom) + V(3f, 1f), icons = new[] { PropIcon.Bay, PropIcon.Radio }, toward = new[] { V(bay.x, bay.z), XZ(WorldFeatures.BayGullyTop) } },
                new Junction { at = V(dw.x + 1.5f, dw.z - 10f), icons = new[] { PropIcon.Driftwood, PropIcon.Hut }, toward = new[] { V(dw.x, dw.z), V(57f, 76f) } },
            };
        }

        // ------------------------------------------------------------------ sampling
        /// Catmull-Rom spline through control points with a gentle deterministic wobble, sampled at a fixed spacing.
        public static List<Vector2> SampleSpline(IList<Vector2> cps, float spacing, int seed, float wobble = 1.4f)
        {
            var d = new List<Vector2>();
            for (int i = 0; i < cps.Count - 1; i++)
            {
                Vector2 p0 = cps[Mathf.Max(0, i - 1)], p1 = cps[i], p2 = cps[i + 1], p3 = cps[Mathf.Min(cps.Count - 1, i + 2)];
                int steps = Mathf.Max(4, Mathf.CeilToInt((p2 - p1).magnitude / 0.25f));
                for (int s = 0; s < steps; s++)
                {
                    float t = s / (float)steps;
                    float t2 = t * t, t3 = t2 * t;
                    Vector2 q = 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
                    d.Add(q);
                }
            }
            d.Add(cps[cps.Count - 1]);
            var outp = new List<Vector2>();
            float acc = 0f, total = 0f;
            outp.Add(d[0]);
            for (int i = 1; i < d.Count; i++)
            {
                float l = (d[i] - d[i - 1]).magnitude;
                acc += l;
                total += l;
                if (acc >= spacing && l > 1e-6f)
                {
                    acc = 0f;
                    Vector2 dir = (d[i] - d[i - 1]) / l;
                    Vector2 perp = new Vector2(-dir.y, dir.x);
                    // the wobble fades to zero at both ends so the routes still meet at their junctions
                    float ends = Mathf.Clamp01(Mathf.Min(i, d.Count - 1 - i) / 24f);
                    float wob = (PropNoise.Value(total * 0.08f, 0.5f, seed) - 0.5f) * wobble * ends;
                    outp.Add(d[i] + perp * wob);
                }
            }
            if ((outp[outp.Count - 1] - d[d.Count - 1]).sqrMagnitude > 0.04f) outp.Add(d[d.Count - 1]);
            return outp;
        }

        static void EnsureDense()
        {
            if (dense != null) return;
            dense = new List<Vector2>();
            var rs = Routes;
            for (int r = 0; r < rs.Count; r++)
            {
                var pts = SampleSpline(rs[r].points, 1f, 40 + r * 13, rs[r].boardwalk ? 0.4f : 1.4f);
                dense.AddRange(pts);
                dense.Add(new Vector2(float.NaN, float.NaN));
            }
        }

        /// Horizontal distance to the nearest trail center line (large when far away). Deterministic, main thread.
        public static float DistanceToPath(float x, float z)
        {
            EnsureDense();
            float best = float.MaxValue;
            for (int i = 0; i < dense.Count - 1; i++)
            {
                Vector2 a = dense[i], b = dense[i + 1];
                if (float.IsNaN(a.x) || float.IsNaN(b.x)) continue;
                if (Mathf.Abs(a.x - x) > 30f || Mathf.Abs(a.y - z) > 30f) continue;
                Vector2 ab = b - a;
                float l2 = ab.sqrMagnitude;
                float t = l2 > 1e-6f ? Mathf.Clamp01(((x - a.x) * ab.x + (z - a.y) * ab.y) / l2) : 0f;
                float dx = x - (a.x + ab.x * t), dz = z - (a.y + ab.y * t);
                float d2 = dx * dx + dz * dz;
                if (d2 < best) best = d2;
            }
            return best == float.MaxValue ? 999f : Mathf.Sqrt(best);
        }

        // ------------------------------------------------------------------ build
        static float G(float x, float z) => PropGround.H(x, z);

        /// Builds the dirt strips, the boardwalk, stepping stones on steep bits and the signposts.
        /// skip: positions where no strip should be drawn (rope bridge deck, structures).
        public static IEnumerator Build(Transform island, PropExclusion excl, PropExclusion pathMask, PropExclusion skip)
        {
            Samples.Clear();
            PropInfo.PathPoints.Clear();
            var root = PropUtil.WorldRoot("Paths", island);
            var cols = new GameObject("Colliders").transform;
            cols.SetParent(root, false);
            var batch = new PropBatch(64f);
            var detail = new PropBatch(64f);
            var rng = new System.Random(3131);
            var rs = Routes;
            for (int r = 0; r < rs.Count; r++)
            {
                var route = rs[r];
                var pts = SampleSpline(route.points, 1f, 40 + r * 13, route.boardwalk ? 0.4f : 1.4f);
                if (route.boardwalk) Boardwalk(batch, cols, pts, excl, rng);
                else DirtStrip(batch, detail, pts, excl, pathMask, skip, rng, 900 + r * 31);
                // tiki torches along the main trail and the boardwalk (lit at night)
                if (route.name == "lighthouse" || route.boardwalk)
                {
                    float acc = 0f;
                    int torches = 0, maxTorches = route.boardwalk ? 2 : 5;
                    float every = route.boardwalk ? 22f : 34f;
                    for (int i = 1; i < pts.Count && torches < maxTorches; i++)
                    {
                        acc += (pts[i] - pts[i - 1]).magnitude;
                        if (acc < every && !(route.boardwalk && i == 3)) continue;
                        acc = 0f;
                        Vector2 d = (pts[i] - pts[i - 1]).normalized;
                        Vector2 side = new Vector2(-d.y, d.x) * ((torches % 2 == 0) ? 1.5f : -1.5f);
                        Vector2 tp = pts[i] + side;
                        float g = G(tp.x, tp.y);
                        if (g < 0.3f || skip.Blocked(tp.x, tp.y)) continue;
                        PropBeach.Torch(root, batch, new Vector3(tp.x, g, tp.y), torches + r * 10);
                        PropCol.Capsule(cols, new Vector3(tp.x, g + 0.9f, tp.y), 0.08f, 1.8f, Quaternion.identity, "Torch");
                        excl.Add(tp.x, tp.y, 0.8f);
                        torches++;
                    }
                }
                if (PropRun.Due()) yield return null;
            }

            for (int c = 0; c < Connections.Count; c++)
            {
                var pts = SampleSpline(Connections[c], 1f, 700 + c, 0.3f);
                DirtStrip(batch, detail, pts, excl, pathMask, skip, rng, 1700 + c * 17);
            }

            // signposts
            var js = Junctions();
            for (int i = 0; i < js.Length; i++)
            {
                Signpost(batch, cols, js[i], i);
                excl.Add(js[i].at.x, js[i].at.y, 1.2f);
            }

            batch.SetShadows(PropMats.PathDirt, ShadowCastingMode.Off);
            batch.SetShadows(PropMats.PathDirtDark, ShadowCastingMode.Off);
            batch.Flush(root, "PathMesh", ShadowCastingMode.On, 240f);
            detail.Flush(root, "PathStones", ShadowCastingMode.Off, 110f);
        }

        static void DirtStrip(PropBatch batch, PropBatch detail, List<Vector2> pts, PropExclusion excl, PropExclusion pathMask, PropExclusion skip, System.Random rng, int seed)
        {
            int n = pts.Count;
            if (n < 2) return;
            var left = new Vector3[n];
            var right = new Vector3[n];
            var c = new Vector3[n];
            var ml = new Vector3[n];
            var mr = new Vector3[n];
            var ok = new bool[n];
            for (int i = 0; i < n; i++)
            {
                Vector2 p = pts[i];
                Vector2 d2 = i + 1 < n ? pts[i + 1] - p : p - pts[i - 1];
                d2 = d2.sqrMagnitude > 1e-8f ? d2.normalized : Vector2.right;
                Vector2 perp = new Vector2(-d2.y, d2.x);
                float g = G(p.x, p.y);
                // fade on the beach, skip the bridge deck and under water
                float hw = Mathf.Lerp(0.8f, 1.2f, PropNoise.Value(i * 0.21f, 1.7f, seed));
                if (g < 1.4f) hw *= Mathf.Clamp01((g - 0.7f) / 0.7f);
                ok[i] = g > 0.75f && hw > 0.25f && !skip.Blocked(p.x, p.y);
                float jl = PropUtil.Range(rng, -0.12f, 0.12f), jr = PropUtil.Range(rng, -0.12f, 0.12f);
                Vector2 L = p + perp * (hw + jl), Rr = p - perp * (hw + jr);
                Vector2 Lm = p + perp * (hw * 0.5f), Rm = p - perp * (hw * 0.5f);
                left[i] = new Vector3(L.x, G(L.x, L.y) + 0.035f, L.y);
                right[i] = new Vector3(Rr.x, G(Rr.x, Rr.y) + 0.035f, Rr.y);
                ml[i] = new Vector3(Lm.x, G(Lm.x, Lm.y) + 0.05f, Lm.y);
                mr[i] = new Vector3(Rm.x, G(Rm.x, Rm.y) + 0.05f, Rm.y);
                c[i] = new Vector3(p.x, g + 0.055f, p.y);
                if (ok[i])
                {
                    var dir = new Vector3(d2.x, 0f, d2.y);
                    Samples.Add(new Sample { pos = new Vector3(p.x, g, p.y), dir = dir, halfWidth = hw });
                    PropInfo.PathPoints.Add(new Vector3(p.x, g, p.y));
                    excl.Add(p.x, p.y, hw + 0.6f);
                    pathMask.Add(p.x, p.y, Mathf.Max(0.3f, hw - 0.25f));
                }
            }
            for (int i = 0; i < n - 1; i++)
            {
                if (!ok[i] || !ok[i + 1]) continue;
                Vector3 cell = c[i];
                var mat = PropNoise.Hash01(i, seed, 5) < 0.18f ? PropMats.PathDirtDark : PropMats.PathDirt;
                var b = batch.W(mat, cell);
                b.QuadAuto(left[i], left[i + 1], ml[i + 1], ml[i], Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector3.up);
                b.QuadAuto(ml[i], ml[i + 1], c[i + 1], c[i], Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector3.up);
                b.QuadAuto(c[i], c[i + 1], mr[i + 1], mr[i], Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector3.up);
                b.QuadAuto(mr[i], mr[i + 1], right[i + 1], right[i], Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector3.up);

                // stepping stones where the path climbs steeply, a few pebbles elsewhere
                float slope = Mathf.Abs(c[i + 1].y - c[i].y) / Mathf.Max(0.2f, Vector2.Distance(pts[i], pts[i + 1]));
                if (slope > 0.32f && i % 2 == 0)
                {
                    float s = PropUtil.Range(rng, 0.32f, 0.44f);
                    Vector3 sp = (c[i] + c[i + 1]) * 0.5f;
                    PropRocks.Build(detail.W(PropMats.Stone, sp), sp, new Vector3(s, 0.09f, s * 0.85f), Quaternion.Euler(0f, PropUtil.Range(rng, 0f, 360f), 0f), rng.Next(), 0, 0.12f);
                }
                else if (PropNoise.Hash01(i, seed, 9) < 0.06f)
                {
                    Vector3 sp = Vector3.Lerp(left[i], right[i], PropUtil.Range(rng, 0.1f, 0.9f));
                    float s = PropUtil.Range(rng, 0.08f, 0.16f);
                    PropRocks.Build(detail.W(PropMats.RockMid, sp), sp, new Vector3(s, s * 0.6f, s), Quaternion.Euler(0f, PropUtil.Range(rng, 0f, 360f), 0f), rng.Next(), 0, 0.2f);
                }
            }
        }

        /// Plank boardwalk across the sand (hut → dock): weathered boards on two stringers, slightly raised.
        static void Boardwalk(PropBatch batch, Transform cols, List<Vector2> pts, PropExclusion excl, System.Random rng)
        {
            Material[] boards = { PropMats.Wood, PropMats.Wood2, PropMats.Wood3 };
            float acc = 0f;
            Vector3 prev = Vector3.zero;
            bool havePrev = false;
            for (int i = 0; i < pts.Count; i++)
            {
                Vector2 p = pts[i];
                Vector2 d2 = i + 1 < pts.Count ? pts[i + 1] - p : p - pts[i - 1];
                d2 = d2.sqrMagnitude > 1e-8f ? d2.normalized : Vector2.right;
                float g = G(p.x, p.y);
                Vector3 pos = new Vector3(p.x, Mathf.Max(g, 0.2f) + 0.12f, p.y);
                Vector3 dir = new Vector3(d2.x, 0f, d2.y);
                excl.Add(p.x, p.y, 1.6f);
                Samples.Add(new Sample { pos = new Vector3(p.x, g, p.y), dir = dir, halfWidth = 0.6f });
                Quaternion r = Quaternion.LookRotation(dir) * Quaternion.Euler(0f, PropUtil.Range(rng, -2f, 2f), 0f);
                // two boards per meter
                for (int k = 0; k < 2; k++)
                {
                    Vector3 bp = pos + dir * (k * 0.5f - 0.25f);
                    bp.y = Mathf.Max(G(bp.x, bp.z), 0.2f) + 0.11f + PropUtil.Range(rng, -0.01f, 0.01f);
                    batch.W(boards[rng.Next(boards.Length)], bp).Box(bp, new Vector3(1.25f, 0.05f, 0.42f), r);
                }
                if (havePrev)
                {
                    Vector3 side = Vector3.Cross(Vector3.up, dir) * 0.45f;
                    var dk = batch.W(PropMats.WoodDark, pos);
                    dk.Beam(prev + side - Vector3.up * 0.06f, pos + side - Vector3.up * 0.06f, 0.08f, 0.08f);
                    dk.Beam(prev - side - Vector3.up * 0.06f, pos - side - Vector3.up * 0.06f, 0.08f, 0.08f);
                }
                acc += 1f;
                prev = pos;
                havePrev = true;
            }
        }

        static void Signpost(PropBatch batch, Transform cols, Junction j, int index)
        {
            var rng = new System.Random(5000 + index);
            float g = G(j.at.x, j.at.y);
            Vector3 b = new Vector3(j.at.x, g, j.at.y);
            var post = batch.W(PropMats.WoodDark, b);
            post.Cylinder(b - Vector3.up * 0.4f, Quaternion.Euler(PropUtil.Range(rng, -2f, 2f), 0f, PropUtil.Range(rng, -2f, 2f)), 0.07f, 0.065f, 2.45f, 6, false, true);
            PropCol.Capsule(cols, b + Vector3.up * 1.0f, 0.09f, 2.1f, Quaternion.identity, "Signpost");
            for (int k = 0; k < j.icons.Length && k < j.toward.Length; k++)
            {
                Vector2 to = j.toward[k] - j.at;
                if (to.sqrMagnitude < 1e-4f) to = Vector2.right;
                to.Normalize();
                float yaw = Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg;
                Quaternion r = Quaternion.Euler(0f, yaw, PropUtil.Range(rng, -3f, 3f));
                float y = 1.85f - k * 0.34f;
                Vector3 center = b + Vector3.up * y + r * new Vector3(0f, 0f, 0.38f);
                Vector3 fwd = r * Vector3.forward, right = r * Vector3.right, up = r * Vector3.up;
                var wood = batch.W(k % 2 == 0 ? PropMats.Wood : PropMats.Wood3, center);
                // arrow board: a box plus a pointed end
                wood.Box(center - fwd * 0.08f, new Vector3(0.05f, 0.26f, 0.62f), r);
                Vector3 tipBase = center + fwd * 0.23f;
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 o = right * (0.025f * s);
                    wood.Face3(tipBase + up * 0.13f + o, tipBase - up * 0.13f + o, tipBase + fwd * 0.16f + o, right * s);
                }
                wood.QuadAuto(tipBase + up * 0.13f + right * 0.025f, tipBase + up * 0.13f - right * 0.025f, tipBase + fwd * 0.16f - right * 0.025f, tipBase + fwd * 0.16f + right * 0.025f,
                    Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, up + fwd);
                wood.QuadAuto(tipBase - up * 0.13f + right * 0.025f, tipBase - up * 0.13f - right * 0.025f, tipBase + fwd * 0.16f - right * 0.025f, tipBase + fwd * 0.16f + right * 0.025f,
                    Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, -up + fwd);
                // pictogram on both faces
                var icon = batch.W(PropMats.SignIcon(j.icons[k]), center);
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 face = center + right * (0.028f * s) - fwd * 0.05f;
                    Vector3 u = fwd * (0.11f * s), v = up * 0.11f;
                    // u runs along the board so the icon reads left → right from both sides
                    icon.QuadAuto(face - u - v, face - u + v, face + u + v, face + u - v, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f), right * s);
                }
            }
        }
    }
}
