using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Round-2b additions to the points of interest (L.5 spots 7, 9, 10, 11, 14; L.6 secrets #2, #4, #5; Q7/Q8):
    ///  * waterfall: the alcove behind the falling water closed by rocks with a 0.95 × 1.25 m squeeze (crouch),
    ///    stepping stones across the creek, the rope swing over the pool, mossy boulders,
    ///  * ravine: rope ladders at both ends of the full-depth part (x −23 north wall, x −2 south wall), Gus' piggy
    ///    bank in a side crevice behind a boulder 3 m east of the west ladder, fish bunting on the bridge,
    ///  * radio tower: a platform 10 m up on the standing tower, the ladder starts 2.5 m above the ground (stacked
    ///    crates as the way up), the old radio up there crackles (audible within 25 m), the „Hochspannung“ sign,
    ///  * tide pools: Dr. Fin's measuring stakes and a bench with a giant magnifier,
    ///  * hidden bay: a rock grotto with Gus' paddle (M2) and a treasure, a hidden campfire behind the boulders.
    public static class PropPOI3
    {
        static float G(float x, float z) => PropGround.H(x, z);
        static float G(Vector3 p) => PropGround.H(p.x, p.z);
        static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-8f ? v.normalized : Vector3.forward; }

        // ================================================================== waterfall
        public static IEnumerator Waterfall(Transform island, PropExclusion excl)
        {
            if (PropInfo.WaterfallNiche == Vector3.zero || PropInfo.PoolCenter == Vector3.zero) yield break;
            var root = PropUtil.WorldRoot("WaterfallExtras", island);
            var cols = new GameObject("Colliders").transform;
            cols.SetParent(root, false);
            var batch = new PropBatch(0f);
            var rng = new System.Random(6262);
            Vector3 d = WorldFeatures.FallDir;
            Vector3 side = Vector3.Cross(Vector3.up, d).normalized;
            Vector3 n = PropInfo.WaterfallNiche;
            Vector3 P = PropInfo.PoolCenter;
            float pr = WorldFeatures.PoolRadius;
            Quaternion q = Quaternion.LookRotation(d);

            // ---- L.6 #2: the alcove behind the falling water, closed by rocks; squeeze on the right (0.95 × 1.25)
            float floorY = n.y - 0.02f;
            Vector3 c = n - side * 0.3f;
            const float wallH = 2.6f;
            void Wall(Vector3 a, Vector3 b, float y0, float y1)
            {
                Vector3 m = (a + b) * 0.5f;
                Vector3 ab = b - a;
                ab.y = 0f;
                PropCol.Box(cols, new Vector3(m.x, (y0 + y1) * 0.5f, m.z), new Vector3(0.6f, y1 - y0, ab.magnitude), Quaternion.LookRotation(ab.normalized), "AlcoveWall");
            }
            // side walls (from the cliff to the front wall)
            Vector3 l0 = c - side * 2.0f - d * 0.9f, l1 = c - side * 2.0f + d * 0.75f;
            Vector3 r0 = c + side * 2.0f - d * 0.9f, r1 = c + side * 2.0f + d * 0.75f;
            Wall(l0, l1, floorY - 0.5f, floorY + wallH + 0.6f);
            Wall(r0, r1, floorY - 0.5f, floorY + wallH + 0.6f);
            // front wall with the squeeze gap at its right end
            Vector3 f0 = c - side * 2.0f + d * 0.75f, gapL = c + side * 0.9f + d * 0.75f, gapR = c + side * 1.85f + d * 0.75f;
            Wall(f0, gapL, floorY - 0.5f, floorY + wallH + 0.6f);
            Wall(gapR, r1, floorY - 0.5f, floorY + wallH + 0.6f);
            Wall(gapL, gapR, floorY + 1.25f, floorY + wallH + 0.6f);
            // roof slab
            Vector3 roofC = c + Vector3.up * (wallH + 0.35f) - d * 0.1f;
            roofC.y = floorY + wallH + 0.35f;
            PropCol.Box(cols, roofC, new Vector3(4.6f, 0.5f, 2.2f), q, "AlcoveRoof");
            batch.W(PropMats.Flat(PropPal.CaveFloor), c).Box(new Vector3(c.x, floorY - 0.06f, c.z), new Vector3(3.8f, 0.12f, 1.6f), q);
            // visual rocks: big mossy boulders covering the boxes (no colliders – the boxes are exact)
            void Rock(Vector3 p, Vector3 s, int seed) => PropRocks.BuildSplit(batch, p, p, s, Quaternion.Euler(0f, seed * 37f, 0f), seed, 1, 0.12f, PropMats.RockMid, PropMats.Moss, PropMats.Crevice);
            Rock(new Vector3(l0.x, floorY + 1.3f, l0.z) + d * 0.8f, new Vector3(0.55f, 1.6f, 1.0f), 9101);
            Rock(new Vector3(r0.x, floorY + 1.3f, r0.z) + d * 0.8f, new Vector3(0.55f, 1.6f, 1.0f), 9102);
            Rock(new Vector3(f0.x, floorY + 1.3f, f0.z) + side * 0.75f, new Vector3(0.8f, 1.55f, 0.45f), 9103);
            Rock(new Vector3(f0.x, floorY + 1.1f, f0.z) + side * 2.05f, new Vector3(0.45f, 1.4f, 0.45f), 9104);
            Rock(new Vector3(gapR.x, floorY + 1.2f, gapR.z) + side * 0.3f, new Vector3(0.28f, 1.5f, 0.4f), 9105);
            Rock(new Vector3((gapL.x + gapR.x) * 0.5f, floorY + 2.1f, (gapL.z + gapR.z) * 0.5f), new Vector3(0.6f, 0.8f, 0.45f), 9106);
            Rock(roofC + Vector3.up * 0.2f, new Vector3(2.4f, 0.45f, 1.3f), 9107);
            excl.Add(c.x, c.z, 3.5f);
            PropInfo.SetSpot("waterfall_alcove", n, Mathf.Atan2(-d.x, -d.z) * Mathf.Rad2Deg);
            PropInfo.SetSpot("waterfall_squeeze", (gapL + gapR) * 0.5f + Vector3.up * floorY - Vector3.up * gapL.y);

            // ---- Q7 stepping stones across the creek (between the falls and the pool)
            Vector3 foot = WorldFeatures.FallFoot, edge = WorldFeatures.Creek[4];
            Vector3 mid = Vector3.Lerp(foot, edge, 0.55f);
            for (int i = -2; i <= 2; i++)
            {
                Vector3 sp = mid + side * (i * 0.9f) + d * PropUtil.Range(rng, -0.2f, 0.2f);
                float g = G(sp);
                float top = Mathf.Max(g + 0.25f, WorldFeatures.PoolWaterY + 0.12f);
                Vector3 sc = new Vector3(0.42f, Mathf.Max(0.2f, (top - g) * 0.6f + 0.15f), 0.38f);
                Vector3 rp = new Vector3(sp.x, top - sc.y * 0.9f, sp.z);
                PropRocks.BuildSplit(batch, rp, rp, sc, Quaternion.Euler(0f, i * 40f, 0f), 9200 + i, 0, 0.08f, PropMats.RockMid, PropMats.Moss, null);
                PropCol.Box(cols, new Vector3(sp.x, top - 0.2f, sp.z), new Vector3(0.7f, 0.4f, 0.65f), Quaternion.Euler(0f, i * 40f, 0f), "SteppingStone");
            }
            PropInfo.SetSpot("stepping_stones", mid);

            // ---- L.5 #7 rope swing over the pool from a leaning tree on the flatter side bank
            Vector3 bank = side;
            float hl = G(P + side * (pr + 2.5f)), hr = G(P - side * (pr + 2.5f));
            if (Mathf.Abs(hr - (P.y + 0.6f)) < Mathf.Abs(hl - (P.y + 0.6f))) bank = -side;
            Vector3 baseP = P + bank * (pr + 2.6f);
            baseP.y = G(baseP);
            Vector3 pivotP = P + bank * (pr - 1.4f) + Vector3.up * 0f;
            pivotP.y = P.y + 6.2f;
            var trunk = batch.W(PropMats.TreeBark, baseP);
            Vector3 k1 = Vector3.Lerp(baseP, pivotP, 0.4f) + Vector3.up * 1.6f, k2 = Vector3.Lerp(baseP, pivotP, 0.75f) + Vector3.up * 0.9f;
            trunk.Rod(baseP - Vector3.up * 0.3f, k1, 0.28f, 0.24f, 7);
            trunk.Rod(k1, k2, 0.24f, 0.2f, 7);
            trunk.Rod(k2, pivotP + Vector3.up * 0.25f + bank * 0.4f, 0.2f, 0.16f, 7, true);
            PropCol.Capsule(cols, (baseP + k1) * 0.5f, 0.3f, (k1 - baseP).magnitude + 0.4f, Quaternion.FromToRotation(Vector3.up, (k1 - baseP).normalized), "SwingTree");
            for (int i = 0; i < 5; i++)
            {
                Vector3 fp = k2 + new Vector3(PropUtil.Range(rng, -1.6f, 1.6f), PropUtil.Range(rng, 0.8f, 1.8f), PropUtil.Range(rng, -1.6f, 1.6f));
                batch.W(i % 2 == 0 ? PropMats.Foliage : PropMats.FoliageLight, fp).Ico(fp, new Vector3(1.3f, 0.9f, 1.3f), Quaternion.Euler(0f, i * 50f, 0f), 1);
            }
            var swing = new GameObject("RopeSwing");
            swing.transform.SetParent(root, false);
            swing.transform.SetPositionAndRotation(pivotP, Quaternion.LookRotation(bank));   // +z = toward the bank, −z over the pool
            var pivot = new GameObject("Pivot").transform;
            pivot.SetParent(swing.transform, false);
            const float L = 4.4f;
            var rope = new PropMeshBuilder();
            rope.Rod(Vector3.zero, new Vector3(0f, -L, 0f), 0.03f, 0.03f, 4);
            rope.Ico(new Vector3(0f, -L, 0f), new Vector3(0.12f, 0.1f, 0.12f), Quaternion.identity, 0);
            rope.Box(new Vector3(0f, -L - 0.05f, 0f), new Vector3(0.5f, 0.06f, 0.1f));
            PropUtil.MeshObject(pivot, "Rope", rope.ToMesh("SwingRope"), PropMats.Rope, Vector3.zero, Quaternion.identity);
            var knot = new GameObject("Knot").transform;
            knot.SetParent(pivot, false);
            knot.localPosition = new Vector3(0f, -L - 0.4f, 0f);
            var trig = new GameObject("SwingGrab");
            trig.transform.SetParent(pivot, false);
            trig.transform.localPosition = new Vector3(0f, -L, 0f);
            var tsc = trig.AddComponent<SphereCollider>();
            tsc.isTrigger = true;
            tsc.radius = 0.8f;
            var rs = swing.AddComponent<PropRopeSwing>();
            rs.pivot = pivot;
            rs.knot = knot;
            rs.releaseDir = -bank;
            PropInfo.SetSpot("rope_swing", baseP + bank * -1.5f);
            excl.Add(baseP.x, baseP.z, 3f);

            batch.Flush(root, "WaterfallExtrasMesh", ShadowCastingMode.On, 400f);
        }

        // ================================================================== ravine
        public static IEnumerator Ravine(Transform island, PropExclusion excl)
        {
            var root = PropUtil.WorldRoot("RavineExtras", island);
            var cols = new GameObject("Colliders").transform;
            cols.SetParent(root, false);
            var batch = new PropBatch(0f);
            float rim = WorldFeatures.RavineRimY, floor = rim - WorldFeatures.RavineDepth;
            // ---- Q8 ladders: west on the north wall (x −23), east on the south wall (x −2)
            Vector3 westFoot = Vector3.zero;
            for (int k = 0; k < 2; k++)
            {
                float x = k == 0 ? -23f : -2f;
                float s = k == 0 ? 1f : -1f;                 // +1 north wall (+z), −1 south wall
                float axis = WorldFeatures.RavineAxisZ(x);
                float fl = G(x, axis);
                for (float o = -3f; o <= 3f; o += 0.5f) fl = Mathf.Min(fl, G(x, axis + o));
                // walk from the axis toward the wall: foot where the floor starts to rise, top where the wall stops
                // rising (the rim – on the 0.62 island the walls are 4–9 m high, not always the full RavineRimY)
                float zf = axis, zt = axis;
                float of = 0f;
                for (float o = 0f; o < 12f; o += 0.25f) { if (G(x, axis + s * o) > fl + 0.35f) { zf = axis + s * Mathf.Max(0f, o - 0.3f); of = o; break; } }
                for (float o = of; o < 16f; o += 0.25f)
                {
                    float h0 = G(x, axis + s * o), h1 = G(x, axis + s * (o + 0.75f));
                    if (h1 < h0 + 0.12f || h0 > rim - 0.25f) { zt = axis + s * (o + 0.6f); break; }
                }
                Vector3 bottom = new Vector3(x, G(x, zf), zf);
                Vector3 top = new Vector3(x, G(x, zt), zt);
                if (top.y - bottom.y < 3f) continue;
                Vector3 outward = new Vector3(0f, 0f, -s);
                HangingLadder(root, batch, cols, top, bottom, k == 0 ? "RavineLadderWest" : "RavineLadderEast", outward);
                excl.Add(top.x, top.z, 2f);
                if (k == 0) westFoot = bottom;
                PropInfo.SetSpot(k == 0 ? "ravine_ladder_west" : "ravine_ladder_east", bottom);
            }
            if (PropRun.Due()) yield return null;
            // ---- L.6 #5 Gus' piggy bank: side crevice in the south wall 3 m east of the west ladder, behind a boulder
            if (westFoot != Vector3.zero)
            {
                float x = westFoot.x + 3f;
                float axis = WorldFeatures.RavineAxisZ(x);
                float fl = G(x, axis);
                for (float o = -3f; o <= 3f; o += 0.5f) fl = Mathf.Min(fl, G(x, axis + o));
                float zw = axis;
                for (float o = 0f; o < 8f; o += 0.25f) { if (G(x, axis - o) > fl + 0.6f) { zw = axis - o; break; } }
                Vector3 niche = new Vector3(x, G(x, zw + 0.6f) + 0.02f, zw + 0.6f);
                // two flanking rocks + an overhang (hides it from above) + the boulder in front (hides it from the bridge)
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 rp = niche + new Vector3(s * 1.0f, 0.7f, -0.2f);
                    PropPOI.BigRock(batch, cols, rp, new Vector3(0.5f, 0.9f, 0.7f), Quaternion.Euler(0f, s * 20f, 0f), 9300 + s, PropMats.RockMid, null);
                }
                PropPOI.BigRock(batch, cols, niche + new Vector3(0f, 1.75f, -0.1f), new Vector3(1.4f, 0.4f, 0.9f), Quaternion.Euler(0f, 10f, 4f), 9310, PropMats.Cliff, PropMats.Moss);
                PropPOI.BigRock(batch, cols, niche + new Vector3(0.6f, 0.75f, 1.6f), new Vector3(0.9f, 0.85f, 0.8f), Quaternion.Euler(0f, 35f, 0f), 9311, PropMats.RockMid, PropMats.Moss);
                PropInfo.SetSpot("ravine_piggy", niche + new Vector3(0f, 0f, -0.15f), 0f);
                PropInfo.SetSpot("ravine_floor_page", westFoot + new Vector3(1.2f, 0.02f, -1.0f));
            }
            // ---- fish bunting along the bridge handrails (L.5 #9)
            if (PropInfo.BridgeNorth != Vector3.zero && PropInfo.BridgeSouth != Vector3.zero)
            {
                Vector3 a = PropInfo.BridgeNorth, b = PropInfo.BridgeSouth;
                Vector3 lat = Vector3.Cross(Vector3.up, Flat(b - a));
                for (int s = -1; s <= 1; s += 2)
                    PropKit.Bunting(batch, a + lat * (s * 0.8f) + Vector3.up * 1.4f, b + lat * (s * 0.8f) + Vector3.up * 1.4f, 0.9f, 7 + s);
            }
            batch.Flush(root, "RavineExtrasMesh", ShadowCastingMode.On, 300f);
        }

        /// A rope ladder hanging from a rim (two anchor posts) down a steep wall to a floor point.
        public static PropLadder HangingLadder(Transform root, PropBatch batch, Transform cols, Vector3 top, Vector3 bottom, string name, Vector3 outward)
        {
            outward = Flat(outward);
            Vector3 side = Vector3.Cross(Vector3.up, outward).normalized;
            Vector3 railTop = top + outward * 0.2f + Vector3.up * 0.9f;
            var rope = batch.W(PropMats.Rope, top);
            var rung = batch.W(PropMats.Wood2, top);
            // the ladder lies against the wall: from the rim edge down to the foot
            Vector3 lower = bottom + outward * 0.15f;
            for (int s = -1; s <= 1; s += 2) rope.Rod(railTop + side * (s * 0.24f), lower + side * (s * 0.24f), 0.022f, 0.022f, 4);
            float len = (lower - railTop).magnitude;
            Vector3 dir = (lower - railTop) / Mathf.Max(0.01f, len);
            for (float t = 0.3f; t < len - 0.05f; t += 0.3f)
            {
                Vector3 p = railTop + dir * t;
                // keep the rungs just outside the wall
                float g = G(p);
                if (p.y < g + 0.08f) p.y = g + 0.08f;
                rung.Box(p, new Vector3(0.56f, 0.04f, 0.06f), Quaternion.LookRotation(outward));
            }
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 post = top - outward * 0.5f + side * (s * 0.45f);
                post.y = G(post);
                batch.W(PropMats.WoodDark, post).Cylinder(post - Vector3.up * 0.4f, Quaternion.identity, 0.08f, 0.07f, 1.4f, 6, false, true);
                batch.W(PropMats.Rope, post).Torus(post + Vector3.up * 0.75f, Quaternion.identity, 0.1f, 0.025f, 8, 3);
                PropCol.Capsule(cols, post + Vector3.up * 0.4f, 0.09f, 1.0f, Quaternion.identity, "LadderPost");
            }
            var l = PropLadder.Create(root, name, bottom, railTop.y, top + Vector3.up * 0.05f - outward * 0.7f, outward, LadderInfo.Rope);
            l.bottomStand = bottom + outward * 0.8f + Vector3.up * 0.05f;
            l.proximityTop = true;
            l.proximityBottom = true;
            return l;
        }

        // ================================================================== radio tower
        public static IEnumerator RadioTower(Transform island, PropExclusion excl)
        {
            Vector3 pad = PropInfo.RadioTowerBase;
            if (pad == Vector3.zero) yield break;
            var root = PropUtil.WorldRoot("RadioTowerExtras", island);
            var cols = new GameObject("Colliders").transform;
            cols.SetParent(root, false);
            var batch = new PropBatch(0f);
            Quaternion yaw = Quaternion.Euler(0f, 17f, 0f);
            Quaternion tilt = yaw * Quaternion.Euler(4f, 0f, -5f);
            var M = Matrix4x4.TRS(pad, tilt, Vector3.one);
            Vector3 T(float x, float y, float z) => M.MultiplyPoint3x4(new Vector3(x, y, z));
            float Half(float y) => Mathf.Lerp(1.6f, 0.55f, y / 18f);
            const float PlatY = 10.0f;
            // ---- the platform (grating + railing) just below the broken top
            float ph = Half(PlatY) + 0.55f;
            var met = batch.W(PropMats.Rust, pad);
            met.Box(T(0f, PlatY, 0f), new Vector3(ph * 2f, 0.08f, ph * 2f), tilt);
            for (int i = 0; i < 4; i++)
            {
                Quaternion f = tilt * Quaternion.Euler(0f, i * 90f, 0f);
                Vector3 e = T(0f, PlatY, 0f) + f * new Vector3(0f, 0.5f, ph);
                if (i == 2) { met.Box(e + f * new Vector3(-ph * 0.6f, 0.45f, 0f), new Vector3(ph * 0.8f, 0.05f, 0.05f), f); }
                else met.Box(e + Vector3.up * 0.45f, new Vector3(ph * 2f, 0.05f, 0.05f), f);
                for (int k = -1; k <= 1; k += 2) met.Box(T(0f, PlatY, 0f) + f * new Vector3(k * ph, 0.5f, ph), new Vector3(0.05f, 1.0f, 0.05f), f);
            }
            PropCol.Box(cols, T(0f, PlatY - 0.05f, 0f), new Vector3(ph * 2f, 0.2f, ph * 2f), tilt, "Platform");
            for (int i = 0; i < 4; i++)
            {
                if (i == 2) continue;   // the ladder side stays open
                Quaternion f = tilt * Quaternion.Euler(0f, i * 90f, 0f);
                PropCol.Box(cols, T(0f, PlatY, 0f) + f * new Vector3(0f, 0.5f, ph), new Vector3(ph * 2f, 1f, 0.08f), f, "PlatformRail");
            }
            // ---- the old radio up there (secret „radio_tower“) with a crackle audible within 25 m
            Vector3 radio = T(0.3f, PlatY + 0.05f, 0.4f);
            batch.W(PropMats.Flat(PropPal.HullBlue), radio).Box(radio + Vector3.up * 0.18f, new Vector3(0.5f, 0.36f, 0.32f), tilt);
            batch.W(PropMats.Metal, radio).Rod(radio + Vector3.up * 0.36f, radio + Vector3.up * 1.2f + tilt * new Vector3(0.2f, 0f, 0f), 0.01f, 0.006f, 3);
            var crackle = new GameObject("RadioCrackle");
            crackle.transform.SetParent(root, false);
            crackle.transform.position = radio + Vector3.up * 0.3f;
            PropSoundLoop.Attach(crackle, PropSound.Crackle, 25f, 0.5f, false);
            PropInfo.SetSpot("radio_platform", radio + Vector3.up * 0.5f);
            // ---- the ladder on the −z face: from 2.5 m to the platform (the lowest 2.5 m are missing)
            Quaternion lf = tilt * Quaternion.Euler(0f, 180f, 0f);
            Vector3 foot = T(0f, 2.5f, -(Half(2.5f) + 0.12f));
            Vector3 topP = T(0f, PlatY + 1.0f, -(Half(PlatY) + 0.12f));
            var lad = batch.W(PropMats.Metal, pad);
            Vector3 lside = tilt * Vector3.right;
            for (int s = -1; s <= 1; s += 2) lad.Rod(foot + lside * (s * 0.25f), topP + lside * (s * 0.25f), 0.025f, 0.025f, 4);
            float llen = (topP - foot).magnitude;
            for (float t = 0.15f; t < llen; t += 0.3f)
            {
                Vector3 p = Vector3.Lerp(foot, topP, t / llen);
                lad.Rod(p - lside * 0.25f, p + lside * 0.25f, 0.018f, 0.018f, 4);
            }
            // broken stub of the lower ladder, hanging
            lad.Rod(foot + lside * 0.25f, foot + lside * 0.3f - Vector3.up * 0.6f + lf * Vector3.forward * 0.2f, 0.02f, 0.02f, 4);
            Vector3 outward = Flat(lf * Vector3.forward);
            // stacked crates as the way up (0.8 / 1.6 / 2.4 m) next to the ladder foot
            Vector3 groundBelow = new Vector3(foot.x, G(foot), foot.z) + outward * 0.9f;
            float gy = groundBelow.y;
            Vector3[] stack = { groundBelow + lside * 1.9f + outward * 0.8f, groundBelow + lside * 1.0f + outward * 0.4f, groundBelow + lside * 0.1f + outward * 0.1f };
            for (int i = 0; i < 3; i++)
                for (int h = 0; h <= i; h++)
                {
                    Vector3 cb = new Vector3(stack[i].x, G(stack[i]) + h * 0.8f, stack[i].z);
                    PropKit.Crate(batch, cols, cb, 0.8f, 17f + i * 7f + h * 11f, h % 2 == 0 ? PropMats.Wood3 : PropMats.Wood2);
                }
            Vector3 crateTop = new Vector3(stack[2].x, G(stack[2]) + 2.4f, stack[2].z);
            var l = PropLadder.Create(root, "RadioTowerLadder", foot, topP.y, T(0f, PlatY + 0.08f, 0f) - outward * 0.3f, outward, LadderInfo.Metal);
            l.bottomStand = crateTop + Vector3.up * 0.05f;
            l.proximityTop = true;
            l.proximityBottom = true;
            PropInfo.SetSpot("radio_crates", crateTop);
            // ---- the sign „Hochspannung (seit 1987 nicht mehr)“
            Vector3 sp = pad + yaw * new Vector3(-4.6f, 0f, -4.8f);
            sp.y = G(sp);
            PropKit.Sign(batch, cols, sp, 17f + 200f, Loc.Tr("HOCHSPANNUNG|SEIT 1987 NICHT MEHR", "HIGH VOLTAGE|NOT SINCE 1987"), LowPoly.Hex("#F2C94C"), LowPoly.Hex("#1E1B2B"), 1.3f, 0.5f);
            // the M3 lens shard and Toni's valve wait in the shack (npc quest objects)
            if (PropInfo.RadioShack != Vector3.zero)
            {
                PropSpots.Register("m3_shard3", PropInfo.RadioShack + new Vector3(-0.3f, 0f, 0.15f));
                PropSpots.Register("q_valve", PropInfo.RadioShack + new Vector3(0.25f, 0f, -0.1f));
            }
            PropInfo.AddLandSpot(T(0f, PlatY + 1.05f, ph), LandSpotKind.Perch, 0.5f, "radio_tower");
            PropInfo.AddLandSpot(pad + Vector3.up * 0.1f + yaw * new Vector3(6f, 0f, 6f), LandSpotKind.Meadow, 7f, "radio_tower");
            batch.Flush(root, "RadioTowerExtrasMesh", ShadowCastingMode.On, 500f);
        }

        // ================================================================== tide pools
        public static IEnumerator TidePools(Transform island, PropExclusion excl)
        {
            var root = PropUtil.WorldRoot("TidePoolExtras", island);
            var cols = new GameObject("Colliders").transform;
            cols.SetParent(root, false);
            var batch = new PropBatch(0f);
            Vector3 c = WorldLayout.TidePools;
            var spots = WorldFeatures.TidePoolSpots;
            // Dr. Fin's measuring stakes at three pools (yellow, black depth marks)
            for (int i = 0; i < 3 && spots != null && i < spots.Length; i++)
            {
                var sp = spots[i * 3 % spots.Length];
                Vector3 p = sp.position + WorldFeatures.TideSeaward * (sp.radius * 0.6f);
                p.y = G(p);
                batch.W(PropMats.DrumYellow, p).Box(p + Vector3.up * 0.55f, new Vector3(0.07f, 1.3f, 0.07f), Quaternion.Euler(0f, i * 30f, 0f));
                for (int k = 0; k < 4; k++) batch.W(PropMats.Ink, p).Box(p + Vector3.up * (0.2f + k * 0.25f), new Vector3(0.075f, 0.03f, 0.075f), Quaternion.Euler(0f, i * 30f, 0f));
                batch.W(PropMats.Orange, p).Tri2(p + Vector3.up * 1.2f, p + Vector3.up * 0.95f, p + Vector3.up * 1.08f + WorldFeatures.TideAlong * 0.3f, WorldFeatures.TideSeaward);
                PropInfo.AddLandSpot(p + Vector3.up * 1.22f, LandSpotKind.Perch, 0.2f, "tide_pools");
            }
            // the bench with a giant magnifier on the landward edge, facing the pools
            Vector3 bp = c - WorldFeatures.TideSeaward * 7.5f + WorldFeatures.TideAlong * 3f;
            bp.y = G(bp);
            float yaw = Mathf.Atan2(WorldFeatures.TideSeaward.x, WorldFeatures.TideSeaward.z) * Mathf.Rad2Deg;
            if (bp.y > 0.3f && !excl.Blocked(bp.x, bp.z, -1f))
            {
                PropKit.Bench(batch, cols, root, bp, yaw, true);
                Quaternion q = Quaternion.Euler(0f, yaw, 0f);
                Vector3 mg = bp + q * new Vector3(0.95f, 0.45f, 0.1f);
                batch.W(PropMats.DarkMetal, mg).Torus(mg + Vector3.up * 0.4f, q * Quaternion.Euler(0f, 0f, 80f) * Quaternion.Euler(90f, 0f, 0f), 0.32f, 0.04f, 12, 4);
                batch.W(PropMats.Flat(LowPoly.Hex("#BFE3EA")), mg).Cylinder(mg + Vector3.up * 0.4f, q * Quaternion.Euler(0f, 0f, 80f), 0.3f, 0.3f, 0.02f, 12, true, true);
                batch.W(PropMats.WoodDark, mg).Rod(mg + Vector3.up * 0.05f, mg + Vector3.up * 0.1f + q * new Vector3(0.12f, -0.4f, 0f), 0.04f, 0.035f, 6);
                PropInfo.SetSpot("tide_bench", bp + Vector3.up * 0.47f, yaw);
                excl.Add(bp.x, bp.z, 2f);
            }
            // marine iguanas bask on the warm rocks (L.11)
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = c + WorldFeatures.TideAlong * (-14f + i * 9f) + WorldFeatures.TideSeaward * 4f;
                p.y = G(p);
                if (p.y > 0.2f) PropInfo.AddLandSpot(p + Vector3.up * 0.1f, LandSpotKind.Bask, 2f, "tide_pools");
            }
            batch.Flush(root, "TidePoolExtrasMesh", ShadowCastingMode.On, 250f);
            yield break;
        }

        // ================================================================== hidden bay
        public static IEnumerator HiddenBay(Transform island, PropExclusion excl)
        {
            var root = PropUtil.WorldRoot("HiddenBayExtras", island);
            var cols = new GameObject("Colliders").transform;
            cols.SetParent(root, false);
            var batch = new PropBatch(0f);
            Vector3 beach = WorldFeatures.HiddenBayBeach;
            beach.y = G(beach);
            // ---- the bay grotto: a free-standing rock mound on the north side of the beach, mouth to the sand
            // (probed against the 0.62 island: (−94.5, −9) on the cove's north shore, the passage running east)
            Vector3 mouth = new Vector3(beach.x - 6.5f, 0f, beach.z + 10f);
            for (int i = 0; i < 20 && G(mouth) < 0.4f; i++) mouth = new Vector3(beach.x - 6.5f + (i % 5) * 1.2f, 0f, beach.z + 10f + (i / 5) * 1.2f);
            mouth.y = G(mouth);
            float yaw = 90f;    // +z of the grotto points east, into the ridge
            float floorY = PropGrotto.FitFloor(mouth, yaw, PropGrotto.Small);
            var g2 = PropGrotto.Build(batch, cols, new Vector3(mouth.x, floorY, mouth.z), yaw, PropGrotto.Small, 1919, PropMats.Rock, PropMats.Moss, 2.5f, 1.6f, true);
            int last = g2.centers.Length - 1;
            Vector3 inner = g2.W(g2.centers[last - 1] + new Vector3(0.6f, 0.02f, 0f));
            PropSpots.Register("q_paddle", inner + new Vector3(-0.8f, 0.05f, 0.3f));
            PropInfo.SetSpot("bay_cave", inner + new Vector3(0.6f, 0.05f, 0.6f));
            PropInfo.SetSpot("bay_cave_mouth", g2.W(new Vector3(0f, 0f, -1.5f)));
            // a lantern stub and an old oar rack inside
            batch.W(PropMats.WoodDark, inner).Box(inner + new Vector3(-1.2f, 0.5f, 0.6f), new Vector3(0.1f, 1.0f, 1.4f), Quaternion.identity);
            excl.Add(mouth.x, mouth.z + 4f, 7f);
            // ---- the hidden campfire behind the boulders south of the beach (L.5 #11)
            Vector3 fire = new Vector3(beach.x + 3f, 0f, beach.z - 9f);
            for (int i = 0; i < 16; i++)
            {
                Vector3 p = new Vector3(beach.x + 1f + (i % 4) * 1.6f, 0f, beach.z - 7f - (i / 4) * 1.6f);
                if (G(p) > 0.5f && PropGround.Slope(p.x, p.z, 1f) < 0.3f && !excl.Blocked(p.x, p.z)) { fire = p; break; }
            }
            fire.y = G(fire);
            PropKit.FirePit(batch, root, fire, 3131, false);
            PropKit.Log(batch, cols, fire + new Vector3(1.4f, 0.18f, 0.2f), 80f, 1.6f, 0.16f, PropMats.Driftwood);
            PropKit.Log(batch, cols, fire + new Vector3(-0.3f, 0.18f, 1.4f), 10f, 1.4f, 0.15f, PropMats.Driftwood);
            batch.W(PropMats.Flat(PropPal.HullBlue), fire).Cylinder(fire + new Vector3(0.9f, 0.02f, -0.9f), Quaternion.identity, 0.12f, 0.1f, 0.2f, 8, false, true);
            PropInfo.SetSpot("bay_campfire", fire);
            excl.Add(fire.x, fire.z, 3f);
            // iguanas bask on the bay rocks
            for (int i = 0; i < 3; i++)
            {
                Vector3 p = beach + new Vector3(-6f + i * 6f, 0f, -12f + (i % 2) * 22f);
                p.y = G(p);
                if (p.y > 0.2f) PropInfo.AddLandSpot(p + Vector3.up * 0.1f, LandSpotKind.Bask, 2.5f, "hidden_bay");
            }
            batch.Flush(root, "HiddenBayExtrasMesh", ShadowCastingMode.On, 300f);
            yield break;
        }
    }
}
