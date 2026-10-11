using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// L.5 „much more detailed“ – the hand-made pass over the 0.62 island (spots 3, 5, 8, 15, 16, 17 + the trail rule):
    ///  * the swim raft with its 2 m diving board, the driftwood beach (sculptures, the bottle-message board, Toni's
    ///    wrecked raft from day 10), the „Kanal Tiefsee“ base camp of 1987, Horst's tent,
    ///  * the jungle path: a fallen-log bridge, Gus' ruined tree-house lookout (climbable), a mushroom ring, bananas,
    ///  * the lagoon: 6 sandbars with birds, 3 rowboat wrecklets (one with the outboard coil for Toni), mooring buoys,
    ///    a sunken jetty,
    ///  * the coast: tide-line debris, crab burrows, small rock pools, coconut palms (shake them – E.1),
    ///  * a prop cluster every ~17 m along every trail and a detail in every empty 20 × 20 m patch of open land,
    ///  * the 12 drop zones of the chopper mishap „falsche Stelle“ (L.12) and the island's land-fauna spots (L.11).
    public static class PropDetails
    {
        static float G(float x, float z) => PropGround.H(x, z);
        static float G(Vector3 p) => PropGround.H(p.x, p.z);
        static Vector3 Ground(float x, float z) => new Vector3(x, G(x, z), z);

        public static IEnumerator Build(Transform island, PropExclusion excl)
        {
            var root = PropUtil.WorldRoot("Details", island);
            var cols = new GameObject("Colliders").transform;
            cols.SetParent(root, false);
            var batch = new PropBatch(64f);
            var rng = new System.Random(5577);

            SwimRaft(root, batch, cols, excl);
            DriftwoodBeach(root, batch, cols, excl, rng);
            if (PropRun.Due()) yield return null;
            BaseCamp(batch, cols, excl, rng);
            HorstTent(batch, cols, excl);
            JunglePath(root, batch, cols, excl, rng);
            if (PropRun.Due()) yield return null;
            Lagoon(root, batch, cols, rng);
            Coast(root, batch, cols, excl, rng);
            if (PropRun.Due()) yield return null;
            Palms(root, batch, cols, excl, rng);
            if (PropRun.Due()) yield return null;
            TrailClusters(batch, cols, excl, rng);
            if (PropRun.Due()) yield return null;
            Coverage(batch, cols, excl, rng);
            DropZones(excl);
            FaunaSpots();

            PropReef.QuietShadows(batch);
            batch.Flush(root, "DetailsMesh", ShadowCastingMode.On, 220f);
        }

        // ------------------------------------------------------------------ swim raft (L.5 #3)
        static void SwimRaft(Transform root, PropBatch batch, Transform cols, PropExclusion excl)
        {
            Vector3 a = WorldLayout.SwimRaft;
            Vector3 toShore = WorldLayout.Hut - a;
            toShore.y = 0f;
            float yaw = Mathf.Atan2(-toShore.x, -toShore.z) * Mathf.Rad2Deg + 90f;   // the ladder (−x) faces the shore
            float deck = PropKit.SwimRaft(batch, cols, root, a, yaw);
            // anchor chain to the seabed
            Vector3 sb = new Vector3(a.x, G(a.x, a.z), a.z);
            batch.W(PropMats.DarkMetal, a).Rod(new Vector3(a.x, deck - 0.9f, a.z), sb, 0.02f, 0.02f, 3);
            PropInfo.SetSpot("swim_raft", new Vector3(a.x, deck, a.z), yaw);
            PropInfo.SetSpot("swim_raft_deck", new Vector3(a.x, deck, a.z) + Quaternion.Euler(0f, yaw, 0f) * new Vector3(-0.6f, 0.01f, 0.8f));
            PropInfo.AddLandSpot(new Vector3(a.x, deck + 0.05f, a.z), LandSpotKind.Perch, 1.2f, "lagoon");
        }

        // ------------------------------------------------------------------ driftwood beach + Toni's raft (L.5 #5, K.8)
        static void DriftwoodBeach(Transform root, PropBatch batch, Transform cols, PropExclusion excl, System.Random rng)
        {
            Vector3 b = WorldLayout.DriftwoodBeach;
            b.y = G(b);
            // driftwood sculptures: a turtle, a ray and an octopus (village carving craft)
            Vector3 t = b + new Vector3(-5f, 0f, 2.5f);
            t.y = G(t);
            var dw = batch.W(PropMats.Driftwood, t);
            dw.Ico(t + Vector3.up * 0.35f, new Vector3(0.7f, 0.3f, 0.9f), Quaternion.identity, 1);
            dw.Ico(t + new Vector3(0f, 0.35f, 1.0f), new Vector3(0.2f, 0.18f, 0.25f), Quaternion.identity, 0);
            for (int s = -1; s <= 1; s += 2) for (int f = -1; f <= 1; f += 2) dw.Box(t + new Vector3(s * 0.75f, 0.2f, f * 0.45f), new Vector3(0.45f, 0.06f, 0.2f), Quaternion.Euler(0f, s * f * 30f, 0f));
            PropCol.Box(cols, t + Vector3.up * 0.3f, new Vector3(1.4f, 0.6f, 1.9f), Quaternion.identity, "TurtleSculpture");
            Vector3 ray = b + new Vector3(-3f, 0f, -5f);
            ray.y = G(ray);
            batch.W(PropMats.Driftwood, ray).Rod(ray, ray + Vector3.up * 1.2f, 0.07f, 0.06f, 5);
            batch.W(PropMats.Driftwood, ray).Face3(ray + new Vector3(-1.1f, 1.3f, 0f), ray + new Vector3(1.1f, 1.3f, 0f), ray + new Vector3(0f, 1.45f, 0.8f), Vector3.up);
            batch.W(PropMats.Driftwood, ray).Face3(ray + new Vector3(1.1f, 1.3f, 0f), ray + new Vector3(-1.1f, 1.3f, 0f), ray + new Vector3(0f, 1.3f, -0.4f), Vector3.up);
            batch.W(PropMats.Driftwood, ray).Rod(ray + new Vector3(0f, 1.3f, -0.4f), ray + new Vector3(0f, 1.25f, -1.4f), 0.03f, 0.01f, 3);
            Vector3 oc = b + new Vector3(-6f, 0f, 7f);
            oc.y = G(oc);
            batch.W(PropMats.Driftwood, oc).Ico(oc + Vector3.up * 0.75f, new Vector3(0.35f, 0.45f, 0.35f), Quaternion.identity, 1);
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3f;
                Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                batch.W(PropMats.Driftwood, oc).Rod(oc + Vector3.up * 0.45f + d * 0.2f, oc + d * 0.9f + Vector3.up * 0.05f, 0.06f, 0.03f, 4);
            }
            // the bottle-message board (messages on a plank wall, a shelf with bottles)
            Vector3 bb = b + new Vector3(-4f, 0f, 9.5f);
            bb.y = G(bb);
            for (int s = -1; s <= 1; s += 2) batch.W(PropMats.Driftwood, bb).Rod(bb + new Vector3(s * 1.0f, -0.3f, 0f), bb + new Vector3(s * 1.0f, 1.8f, 0f), 0.06f, 0.05f, 5);
            batch.W(PropMats.Driftwood, bb).Box(bb + new Vector3(0f, 1.2f, 0f), new Vector3(2.2f, 1.0f, 0.06f), Quaternion.identity);
            for (int i = 0; i < 7; i++) batch.W(PropMats.Paper, bb).Box(bb + new Vector3(-0.85f + (i % 4) * 0.55f, 1.0f + (i / 4) * 0.4f, -0.04f), new Vector3(0.3f, 0.22f, 0.01f), Quaternion.Euler(0f, 0f, (i * 13) % 20 - 10f));
            batch.W(PropMats.Driftwood, bb).Box(bb + new Vector3(0f, 0.6f, -0.12f), new Vector3(2.0f, 0.05f, 0.2f), Quaternion.identity);
            for (int i = 0; i < 5; i++) batch.W(PropMats.BottleGlass, bb).Cylinder(bb + new Vector3(-0.8f + i * 0.4f, 0.63f, -0.12f), Quaternion.identity, 0.05f, 0.05f, 0.22f, 6, true, true);
            PropCol.Box(cols, bb + Vector3.up * 0.9f, new Vector3(2.2f, 1.8f, 0.3f), Quaternion.identity, "BottleBoard");
            PropInfo.SetSpot("driftwood_board", bb);
            PropInfo.SetSpot("driftwood_board_page", bb + new Vector3(0.6f, 0.02f - bb.y + G(bb + new Vector3(0.6f, 0f, -1.4f)), -1.4f));
            excl.Add(b.x, b.z, 9f);
            // Toni's wrecked raft in the shallows (K.8) – shown from the day he washes up (story flag toni_arrived)
            Vector3 r = WorldLayout.DriftwoodRaft;
            var raft = new GameObject("ToniRaft");
            raft.transform.SetParent(root, false);
            var rb = new PropMeshBuilder();
            for (int i = 0; i < 6; i++) rb.Cylinder(new Vector3(-1.25f + i * 0.5f, 0f, -1.4f), Quaternion.Euler(90f, 0f, 0f), 0.22f, 0.2f, 2.8f, 7, true, true);
            rb.Box(new Vector3(0f, 0.25f, 0.6f), new Vector3(3.0f, 0.12f, 0.15f));
            rb.Box(new Vector3(0f, 0.25f, -0.8f), new Vector3(3.0f, 0.12f, 0.15f));
            rb.Rod(new Vector3(0.3f, 0.2f, 0f), new Vector3(1.2f, 2.4f, 0.4f), 0.06f, 0.05f, 5);
            var raftMesh = rb.ToMesh("ToniRaft");
            float ry = Mathf.Max(G(r.x, r.z) + 0.15f, Waves.SeaLevel - 0.1f);
            var rmr = PropUtil.MeshObject(raft.transform, "Raft", raftMesh, PropMats.Driftwood, new Vector3(r.x, ry, r.z), Quaternion.Euler(4f, 35f, -9f));
            var sail = new PropMeshBuilder();
            sail.Tri2(new Vector3(1.2f, 2.4f, 0.4f), new Vector3(0.4f, 0.4f, 0.05f), new Vector3(1.9f, 0.7f, -0.2f), Vector3.forward);
            PropUtil.MeshObject(raft.transform, "Sail", sail.ToMesh("ToniSail"), PropMats.WaveCloth(0), new Vector3(r.x, ry, r.z), Quaternion.Euler(4f, 35f, -9f));
            var gate = root.gameObject.AddComponent<PropFlagGate>();
            gate.flag = StoryKeys.ToniArrived;
            gate.target = raft;
            PropSpots.Register("toni_raft", new Vector3(r.x, Waves.SeaLevel, r.z));
            PropSpots.Register("driftwood", b + new Vector3(0f, 0f, 1.5f));
            Vector3 lie = b + new Vector3(-1.5f, 0f, -2f);
            lie.y = G(lie);
            PropSpots.Register("toni_lie", lie);
            PropInfo.SetSpot("toni_beach", lie);
            // Toni's recorder sank 30 m off the beach at 3–4 m (npc quest object at q_recorder)
            Vector3 off = new Vector3(r.x - b.x, 0f, r.z - b.z).normalized;
            if (PropReefs.FindSpot(b + off * 30f, 20f, 3f, 4.2f, null, 71, out Vector3 rec)) PropSpots.Register("q_recorder", rec + Vector3.up * 0.1f);
            // driftwood pieces for Mira's M1 (npc objects at driftwood:0..4): one here, the rest around the coast
            PropSpots.Register("driftwood:0", NearDry(b + new Vector3(-6f, 0f, -2f), 0.3f));
            PropSpots.Register("driftwood:1", NearDry(b + new Vector3(7f, 0f, 1f), 0.3f));
        }

        static Vector3 NearDry(Vector3 p, float minG)
        {
            for (float r = 0f; r <= 14f; r += 0.75f)
            {
                int n = r < 0.1f ? 1 : Mathf.CeilToInt(2f * Mathf.PI * r / 0.75f);
                for (int i = 0; i < n; i++)
                {
                    float a = i * Mathf.PI * 2f / n;
                    float x = p.x + Mathf.Cos(a) * r, z = p.z + Mathf.Sin(a) * r;
                    float g = G(x, z);
                    if (g >= minG) return new Vector3(x, g, z);
                }
            }
            return new Vector3(p.x, G(p), p.z);
        }

        /// A flat open spot near p: slope < maxSlope, ground ≥ minG, ≥ minTrail off every trail, not blocked.
        static bool FlatSpot(Vector3 p, float maxR, float maxSlope, float minG, float minTrail, PropExclusion excl, out Vector3 pos)
        {
            for (float r = 0f; r <= maxR; r += 1f)
            {
                int n = r < 0.1f ? 1 : Mathf.CeilToInt(2f * Mathf.PI * r / 1.5f);
                for (int i = 0; i < n; i++)
                {
                    float a = i * Mathf.PI * 2f / n + r * 0.3f;
                    float x = p.x + Mathf.Cos(a) * r, z = p.z + Mathf.Sin(a) * r;
                    float g = G(x, z);
                    if (g < minG || PropGround.Slope(x, z, 1.5f) > maxSlope) continue;
                    if (excl != null && excl.Blocked(x, z)) continue;
                    if (minTrail > 0f && PropPaths.DistanceToPath(x, z) < minTrail) continue;
                    pos = new Vector3(x, g, z);
                    return true;
                }
            }
            pos = Vector3.zero;
            return false;
        }

        // ------------------------------------------------------------------ the „Kanal Tiefsee“ base camp of 1987 (L.5 #8)
        static void BaseCamp(PropBatch batch, Transform cols, PropExclusion excl, System.Random rng)
        {
            // between the pool beach and the ravine, ≥ 8 m off the ravine-north trail, slope < 15°
            Vector3 a = WorldFeatures.PoolBeach, b = new Vector3(WorldLayout.Ravine.x, 0f, WorldLayout.Ravine.z + 14f);
            if (!FlatSpot(Vector3.Lerp(a, b, 0.5f), 22f, 0.27f, 3f, 8f, excl, out Vector3 c)) return;
            float yaw = PropUtil.Range(rng, 0f, 360f);
            Quaternion q = Quaternion.Euler(0f, yaw, 0f);
            // the collapsed tent frame + a faded canvas
            var rust = batch.W(PropMats.Rust, c);
            for (int s = -1; s <= 1; s += 2)
            {
                rust.Rod(c + q * new Vector3(s * 1.1f, 0f, -1.2f), c + q * new Vector3(0f, 1.2f, -1.2f), 0.03f, 0.03f, 4);
                rust.Rod(c + q * new Vector3(s * 1.1f, 0f, 1.2f), c + q * new Vector3(s * 0.3f, 0.7f, 1.0f), 0.03f, 0.03f, 4);
            }
            rust.Rod(c + q * new Vector3(0f, 1.2f, -1.2f), c + q * new Vector3(0f, 0.6f, 1.2f), 0.03f, 0.03f, 4);
            batch.W(PropMats.Flat(LowPoly.Hex("#8C8A5A")), c).Quad2(c + q * new Vector3(-1.2f, 0.05f, -1.3f), c + q * new Vector3(0f, 1.15f, -1.3f), c + q * new Vector3(0f, 0.55f, 1.3f), c + q * new Vector3(-1.3f, 0.05f, 1.3f), Vector3.up);
            // tripod, cable reel, a crate of 1987 tapes, the rusty sign
            Vector3 tri = c + q * new Vector3(2.4f, 0f, 0.4f);
            tri.y = G(tri);
            for (int i = 0; i < 3; i++)
            {
                float ang = i * Mathf.PI * 2f / 3f;
                batch.W(PropMats.DarkMetal, tri).Rod(tri + new Vector3(Mathf.Cos(ang) * 0.5f, 0f, Mathf.Sin(ang) * 0.5f), tri + Vector3.up * 1.3f, 0.025f, 0.02f, 4);
            }
            batch.W(PropMats.Ink, tri).Box(tri + Vector3.up * 1.42f, new Vector3(0.3f, 0.22f, 0.4f), q);
            Vector3 reel = c + q * new Vector3(-2.3f, 0f, 1.5f);
            reel.y = G(reel);
            Quaternion rq = q * Quaternion.Euler(0f, 0f, 90f);
            batch.W(PropMats.Wood2, reel).Cylinder(reel + Vector3.up * 0.4f - rq * Vector3.up * 0.3f, rq, 0.4f, 0.4f, 0.06f, 10, true, true);
            batch.W(PropMats.Wood2, reel).Cylinder(reel + Vector3.up * 0.4f + rq * Vector3.up * 0.24f, rq, 0.4f, 0.4f, 0.06f, 10, true, true);
            batch.W(PropMats.Ink, reel).Cylinder(reel + Vector3.up * 0.4f - rq * Vector3.up * 0.24f, rq, 0.26f, 0.26f, 0.48f, 10);
            PropCol.Capsule(cols, reel + Vector3.up * 0.4f, 0.4f, 0.6f, rq, "CableReel");
            Vector3 crate = c + q * new Vector3(1.2f, 0f, 2.0f);
            crate.y = G(crate);
            PropKit.Crate(batch, cols, crate, 0.6f, yaw + 20f, PropMats.Wood3);
            for (int i = 0; i < 5; i++) batch.W(PropMats.Ink, crate).Cylinder(crate + new Vector3(-0.2f + i * 0.1f, 0.62f, 0f), Quaternion.Euler(0f, 0f, 90f), 0.09f, 0.09f, 0.03f, 8, true, true);
            Vector3 sign = c + q * new Vector3(0f, 0f, -3.2f);
            sign.y = G(sign);
            PropKit.Sign(batch, cols, sign, yaw + 180f, Loc.Tr("KANAL TIEFSEE|BASISLAGER", "CHANNEL DEEP SEA|BASE CAMP"), LowPoly.Hex("#A0583A"), LowPoly.Hex("#EDE6DA"), 1.4f, 0.5f);
            PropKit.FirePit(batch, null, c + q * new Vector3(-1.0f, 0f, -2.6f) + Vector3.up * (G(c + q * new Vector3(-1.0f, 0f, -2.6f)) - c.y), 4141, false);
            PropInfo.SetSpot("base_camp", c, yaw);
            PropInfo.SetSpot("base_camp_page", crate + new Vector3(0f, 0.63f, 0.15f));
            excl.Add(c.x, c.z, 4.5f);
        }

        // ------------------------------------------------------------------ Horst's tent (A.2)
        static void HorstTent(PropBatch batch, Transform cols, PropExclusion excl)
        {
            Vector3 hut = WorldLayout.Hut;
            if (!FlatSpot(new Vector3(hut.x + 3f, 0f, hut.z - 11f), 8f, 0.3f, 1f, 2.5f, excl, out Vector3 p)) return;
            float yaw = 200f;
            PropKit.Tent(batch, cols, p, yaw, LowPoly.Hex("#EE7F2D"), 2.2f, 1.8f, 1.4f);
            Vector3 chair = p + Quaternion.Euler(0f, yaw, 0f) * new Vector3(1.6f, 0f, 1.8f);
            chair.y = G(chair);
            PropKit.Stool(batch, chair);
            PropKit.Cooler(batch, cols, chair + new Vector3(0.7f, 0f, 0.3f), yaw, PropMats.Flat(LowPoly.Hex("#3FB8C4")));
            PropSpots.Register("horst_tent", p + Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, 0f, 1.8f));
            excl.Add(p.x, p.z, 3.5f);
        }

        // ------------------------------------------------------------------ jungle path (L.5 #15)
        static void JunglePath(Transform root, PropBatch batch, Transform cols, PropExclusion excl, System.Random rng)
        {
            // Gus' ruined tree-house lookout: a platform 3.2 m up in the jungle, ≥ 6 m off the trails, with a ladder
            Vector3 jungle = new Vector3(5f, 0f, 20f);
            if (FlatSpot(jungle, 30f, 0.3f, 4f, 6f, excl, out Vector3 tp))
            {
                const float H = 3.2f;
                float yaw = 30f;
                Quaternion q = Quaternion.Euler(0f, yaw, 0f);
                var trunk = batch.W(PropMats.TreeBark, tp);
                trunk.Cylinder(tp - Vector3.up * 0.3f, Quaternion.identity, 0.45f, 0.32f, 9f, 8, false, true);
                PropCol.Capsule(cols, tp + Vector3.up * 4f, 0.45f, 8f, Quaternion.identity, "TreeHouseTrunk");
                for (int i = 0; i < 6; i++)
                {
                    Vector3 cp = tp + new Vector3(PropUtil.Range(rng, -2.2f, 2.2f), PropUtil.Range(rng, 8f, 9.5f), PropUtil.Range(rng, -2.2f, 2.2f));
                    batch.W(i % 2 == 0 ? PropMats.Foliage : PropMats.FoliageDark, cp).Ico(cp, new Vector3(2f, 1.3f, 2f), Quaternion.Euler(0f, i * 40f, 0f), 1);
                }
                Vector3 pc = tp + q * new Vector3(0f, H, 1.4f);
                var wood = batch.W(PropMats.Wood2, pc);
                for (int i = -4; i <= 4; i++)
                {
                    if (i == 2) continue;   // a missing plank
                    wood.Box(pc + q * new Vector3(i * 0.32f, 0f, 0f), new Vector3(0.3f, 0.06f, 2.6f), q * Quaternion.Euler(0f, 0f, (i % 3) * 1.5f));
                }
                for (int s = -1; s <= 1; s += 2)
                {
                    wood.Box(pc + q * new Vector3(s * 1.4f, 0.5f, 0f), new Vector3(0.06f, 0.06f, 2.6f), q);
                    for (int f = -1; f <= 1; f += 2) PropKit.Log(batch, null, pc + q * new Vector3(s * 1.35f, -H * 0.5f, f * 1.2f), 0f, H, 0.08f, PropMats.WoodDark, false);
                }
                // a torn roof cloth + Gus' old lookout chair
                batch.W(PropMats.Flat(LowPoly.Hex("#8C8A5A")), pc).Quad2(pc + q * new Vector3(-1.4f, 1.7f, -1.2f), pc + q * new Vector3(1.4f, 1.7f, -1.2f), pc + q * new Vector3(1.4f, 1.4f, 0.8f), pc + q * new Vector3(-1.0f, 1.3f, 0.6f), Vector3.up);
                PropKit.Stool(batch, pc + q * new Vector3(0.6f, 0.03f, 0.6f));
                PropCol.Box(cols, pc - Vector3.up * 0.04f, new Vector3(2.9f, 0.14f, 2.6f), q, "TreeHousePlatform");
                for (int s = -1; s <= 1; s += 2) PropCol.Box(cols, pc + q * new Vector3(s * 1.4f, 0.5f, 0f), new Vector3(0.1f, 1.0f, 2.6f), q, "TreeHouseRail");
                Vector3 lf = tp + q * new Vector3(0f, 0f, 2.9f);
                lf.y = G(lf);
                var lad = batch.W(PropMats.Wood, lf);
                for (int s = -1; s <= 1; s += 2) lad.Box(lf + q * new Vector3(s * 0.25f, (H + 0.9f) * 0.5f, 0f), new Vector3(0.06f, H + 0.9f, 0.06f), q);
                for (float y = 0.3f; y < H + 0.8f; y += 0.3f) lad.Box(lf + q * new Vector3(0f, y, 0f), new Vector3(0.5f, 0.04f, 0.05f), q);
                var l = PropLadder.Create(root, "TreeHouseLadder", lf, tp.y + H + 0.9f, pc + q * new Vector3(0f, 0.05f, 0.3f), q * Vector3.forward, LadderInfo.Wood);
                l.proximityTop = true;
                PropInfo.SetSpot("tree_house", pc, yaw);
                PropInfo.SetSpot("tree_house_floor", pc + q * new Vector3(-0.7f, 0.04f, -0.6f));
                PropInfo.AddLandSpot(tp + Vector3.up * 8.5f, LandSpotKind.Roost, 2f, "jungle");
                excl.Add(tp.x, tp.z, 4.5f);
            }
            // a fallen-log bridge over the creek below the pool (where the cave trail crosses it)
            Vector3 creek = WorldFeatures.PoolCenter + WorldFeatures.FallDir * (WorldFeatures.PoolRadius + 7f);
            creek.y = G(creek);
            Vector3 lat = Vector3.Cross(Vector3.up, WorldFeatures.FallDir);
            PropKit.Log(batch, cols, creek + Vector3.up * 0.45f + lat * 3f, Mathf.Atan2(lat.x, lat.z) * Mathf.Rad2Deg + 90f, 4.5f, 0.32f, PropMats.TreeBark);
            // a mushroom ring and wild banana plants in the jungle
            if (FlatSpot(new Vector3(-20f, 0f, 0f), 30f, 0.4f, 4f, 5f, excl, out Vector3 mr))
            {
                PropKit.MushroomRing(batch, mr, 1.3f, 99);
                PropInfo.SetSpot("mushroom_ring", mr);
                excl.Add(mr.x, mr.z, 2f);
            }
            var banana = PropPlants.Banana(808);
            for (int i = 0; i < 9; i++)
            {
                float a = i * 2.39996f;
                Vector3 p = new Vector3(Mathf.Cos(a) * (28f + i * 4f), 0f, Mathf.Sin(a) * (28f + i * 4f));
                if (!FlatSpot(p, 10f, 0.5f, 2.5f, 3f, excl, out Vector3 bp)) continue;
                batch.Add(banana, Matrix4x4.TRS(bp - Vector3.up * 0.05f, Quaternion.Euler(0f, i * 47f, 0f), Vector3.one * PropUtil.Range(rng, 0.9f, 1.25f)));
                excl.Add(bp.x, bp.z, 1.5f);
            }
        }

        // ------------------------------------------------------------------ lagoon (L.5 #16)
        static void Lagoon(Transform root, PropBatch batch, Transform cols, System.Random rng)
        {
            // 6 sandbars with birds: just breaking the surface, 0.3–0.9 m water, around the island
            int bars = 0;
            for (int k = 0; k < 36 && bars < 6; k++)
            {
                float a = (k * 61f + 23f) * Mathf.Deg2Rad;
                Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                for (float r = 90f; r < 210f; r += 2f)
                {
                    float x = d.x * r, z = d.z * r;
                    float g = G(x, z);
                    if (g > -0.35f || g < -0.95f) continue;
                    if (Vector2.Distance(new Vector2(x, z), new Vector2(WorldLayout.DockEnd.x, WorldLayout.DockEnd.z)) < 30f) break;
                    if (Vector2.Distance(new Vector2(x, z), new Vector2(WorldLayout.Village.x, WorldLayout.Village.z - 26f)) < 25f) break;
                    Vector3 c = new Vector3(x, g, z);
                    PropKit.Sandbar(batch, new Vector3(x, Waves.SeaLevel - 0.08f, z), PropUtil.Range(rng, 5f, 9f), PropUtil.Range(rng, 2.5f, 3.5f), a * Mathf.Rad2Deg + 90f, 300 + bars);
                    PropInfo.AddLandSpot(new Vector3(x, Waves.SeaLevel + 0.1f, z), LandSpotKind.Shore, 2f, "sandbar");
                    PropInfo.SetSpot("sandbar_" + bars, new Vector3(x, Waves.SeaLevel + 0.1f, z));
                    bars++;
                    break;
                }
            }
            // 3 rowboat wrecklets at 2–3.5 m; the one nearest the hut keeps an outboard motor with the copper coil
            int wrecks = 0;
            float[] ang = { -40f, 150f, 230f };
            for (int i = 0; i < ang.Length; i++)
            {
                Vector3 d = new Vector3(Mathf.Cos(ang[i] * Mathf.Deg2Rad), 0f, Mathf.Sin(ang[i] * Mathf.Deg2Rad));
                if (!PropReefs.FindSpot(d * 150f, 60f, 2.4f, 3.4f, null, 700 + i, out Vector3 wp)) continue;
                float y = PropUtil.Range(rng, 0f, 360f);
                PropBeach.RowingBoat(batch, Matrix4x4.TRS(wp + Vector3.up * 0.1f, Quaternion.Euler(10f, y, 28f), Vector3.one), i == 0 ? PropMats.Flat(LowPoly.Hex("#3FA9A0")) : PropMats.Shutter, true);
                PropInfo.SetSpot("wrecklet_" + wrecks, wp);
                PropInfo.AddHabitat(wp + Vector3.up * 0.4f, 2f, HabitatKind.Overhang, "lagoon");
                if (i == 0)
                {
                    Vector3 motor = wp + Quaternion.Euler(0f, y, 0f) * new Vector3(0f, 0.4f, -1.9f);
                    batch.W(PropMats.Flat(LowPoly.Hex("#4F7FAE")), motor).Box(motor, new Vector3(0.35f, 0.4f, 0.5f), Quaternion.Euler(10f, y, 28f));
                    batch.W(PropMats.Flat(LowPoly.Hex("#4F7FAE")), motor).Box(motor - Vector3.up * 0.45f, new Vector3(0.12f, 0.6f, 0.12f), Quaternion.Euler(10f, y, 28f));
                    PropSpots.Register("q_coil", motor + Vector3.up * 0.1f);
                    PropInfo.SetSpot("outboard_coil", motor);
                }
                wrecks++;
            }
            // a sunken jetty: old posts and a few planks between the hut beach and the dock, 1.5–2.5 m
            if (PropReefs.FindSpot(new Vector3(WorldLayout.Hut.x + 40f, 0f, WorldLayout.Hut.z + 32f), 30f, 1.5f, 2.6f, null, 811, out Vector3 sj))
            {
                Vector3 dir = new Vector3(0.8f, 0f, 0.6f).normalized;
                for (int i = 0; i < 6; i++)
                {
                    Vector3 p = sj + dir * (i * 1.8f);
                    float g = G(p);
                    batch.W(PropMats.WoodWet, p).Cylinder(new Vector3(p.x, g - 0.3f, p.z) + Vector3.Cross(Vector3.up, dir) * 0.9f, Quaternion.Euler(PropUtil.Range(rng, -10f, 10f), 0f, PropUtil.Range(rng, -10f, 10f)), 0.12f, 0.1f, PropUtil.Range(rng, 0.8f, 1.6f), 6, false, true);
                    batch.W(PropMats.WoodWet, p).Cylinder(new Vector3(p.x, g - 0.3f, p.z) - Vector3.Cross(Vector3.up, dir) * 0.9f, Quaternion.Euler(PropUtil.Range(rng, -10f, 10f), 0f, PropUtil.Range(rng, -10f, 10f)), 0.12f, 0.1f, PropUtil.Range(rng, 0.6f, 1.4f), 6, false, true);
                    if (i % 2 == 0) batch.W(PropMats.WoodWet, p).Box(new Vector3(p.x, g + 0.08f, p.z), new Vector3(2.0f, 0.06f, 0.28f), Quaternion.LookRotation(dir) * Quaternion.Euler(0f, 90f, PropUtil.Range(rng, -12f, 12f)));
                }
                PropInfo.SetSpot("sunken_jetty", sj);
                PropInfo.AddHabitat(sj + Vector3.up * 0.4f, 4f, HabitatKind.Crevice, "lagoon");
            }
            // mooring buoys in front of the hut beach and the village
            Vector3[] moor = { WorldLayout.BoatSpawn + new Vector3(-8f, 0f, 14f), WorldLayout.BoatSpawn + new Vector3(6f, 0f, -16f), WorldLayout.Village + new Vector3(16f, 0f, -34f), WorldLayout.Village + new Vector3(-4f, 0f, -36f) };
            for (int i = 0; i < moor.Length; i++)
                if (G(moor[i].x, moor[i].z) < -0.8f) PropKit.MooringBuoy(root, moor[i], i % 2 == 0 ? PropMats.BuoyOrange : PropMats.White, 0.7f);
            // sea grapes for Ada's A2 in the sea grass off the hut beach (npc objects at seagrape:0..5)
            for (int i = 0; i < 6; i++)
                if (PropReefs.FindSpot(new Vector3(WorldLayout.Hut.x + 30f - i * 6f, 0f, WorldLayout.Hut.z + 32f + i * 4f), 30f, 1.1f, 2.9f, null, 900 + i, out Vector3 sg))
                {
                    PropReef.SeaGrassPatch(batch, rng, sg, 1.6f, null);
                    PropSpots.Register("seagrape:" + i, sg + Vector3.up * 0.1f);
                    PropInfo.AddHabitat(sg + Vector3.up * 0.2f, 1.6f, HabitatKind.SeaGrass, "lagoon");
                }
        }

        // ------------------------------------------------------------------ coast (L.5 #17)
        static void Coast(Transform root, PropBatch batch, Transform cols, PropExclusion excl, System.Random rng)
        {
            int lines = 0, burrows = 0, pools = 0;
            for (int k = 0; k < 72; k++)
            {
                float a = k * 5f * Mathf.Deg2Rad + 0.11f;
                Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                // the beach line: walking inward, the first point with 0.2–0.6 m ground
                Vector3 shore = Vector3.zero;
                for (float r = 200f; r > 50f; r -= 0.75f)
                {
                    float g = G(d.x * r, d.z * r);
                    if (g >= 0.25f && g <= 0.7f) { shore = new Vector3(d.x * r, g, d.z * r); break; }
                    if (g > 0.7f) break;
                }
                if (shore == Vector3.zero || excl.Blocked(shore.x, shore.z)) continue;
                Vector3 along = Vector3.Cross(Vector3.up, d);
                if (k % 3 == 0) { PropKit.TideLine(batch, shore - along * 2.5f + d * 0.5f, shore + along * 2.5f + d * 0.5f, 200 + k); lines++; }
                if (k % 4 == 1)
                {
                    Vector3 bp = shore - d * 2.5f;
                    bp.y = G(bp);
                    if (bp.y > 0.4f && !excl.Blocked(bp.x, bp.z))
                    {
                        PropKit.Burrow(batch, bp);
                        PropKit.Burrow(batch, bp + along * 0.9f + d * 0.4f);
                        PropInfo.AddLandSpot(bp + Vector3.up * 0.05f, LandSpotKind.Burrow, 1.2f, "coast");
                        burrows++;
                    }
                }
                if (k % 9 == 4)
                {
                    // a small rock pool: a ring of rocks in the wet sand with a starfish
                    Vector3 rp = shore + d * 1.6f;
                    rp.y = G(rp);
                    for (int i = 0; i < 6; i++)
                    {
                        float ra = i * Mathf.PI / 3f;
                        Vector3 p = rp + new Vector3(Mathf.Cos(ra), 0f, Mathf.Sin(ra)) * 0.9f;
                        p.y = G(p);
                        float s = PropUtil.Range(rng, 0.2f, 0.35f);
                        PropRocks.BuildSplit(batch, p, p, new Vector3(s * 1.4f, s * 0.6f, s), Quaternion.Euler(0f, i * 60f, 0f), 7700 + k * 7 + i, 0, 0.18f, PropMats.BeachRock, null, PropMats.Crevice);
                    }
                    PropPOI2.Starfish(batch, rp + Vector3.up * 0.02f, k * 33f, 0.16f, PropMats.Starfish);
                    pools++;
                }
            }
            PropInfo.SetSpot("coast_details", new Vector3(lines, burrows, pools));
        }

        // ------------------------------------------------------------------ coconut palms (E.1): shake for a coconut
        static void Palms(Transform root, PropBatch batch, Transform cols, PropExclusion excl, System.Random rng)
        {
            var model = PropPlants.Palm(9191);
            var lite = model;
            int placed = 0, quest = 0;
            // groves near the hut, the dock beach, the driftwood beach and the village beach
            Vector3[] groves = { WorldLayout.Hut + new Vector3(12f, 0f, 14f), WorldLayout.Hut + new Vector3(-6f, 0f, 12f), WorldLayout.DockStart + new Vector3(-8f, 0f, 14f), WorldLayout.DriftwoodBeach + new Vector3(-4f, 0f, -6f), WorldLayout.DriftwoodBeach + new Vector3(6f, 0f, -5f), WorldLayout.Village + new Vector3(10f, 0f, -8f), WorldLayout.Village + new Vector3(-12f, 0f, -6f), WorldLayout.TidePools + new Vector3(-12f, 0f, 8f) };
            for (int gi = 0; gi < groves.Length; gi++)
            {
                for (int k = 0; k < 3; k++)
                {
                    Vector2 o = PropUtil.InDisc(rng, 5f);
                    Vector3 p = groves[gi] + new Vector3(o.x, 0f, o.y);
                    if (!FlatSpot(p, 6f, 0.5f, 0.6f, 2.5f, excl, out Vector3 bp)) continue;
                    float yaw = PropUtil.Range(rng, 0f, 360f);
                    float s = PropUtil.Range(rng, 0.95f, 1.15f);
                    batch.Add(model, Matrix4x4.TRS(bp - Vector3.up * 0.05f, Quaternion.Euler(0f, yaw, 0f), Vector3.one * s));
                    PropCol.Capsule(cols, bp + Vector3.up * 2.5f, 0.22f, 5f, Quaternion.identity, "PalmTrunk");
                    // coconut cluster + the shake interactable (trigger around the trunk)
                    Vector3 crown = bp + Vector3.up * (model.height * s * 0.92f);
                    for (int c = 0; c < 3; c++) batch.W(PropMats.Coconut, crown).Ico(crown + new Vector3(Mathf.Cos(c * 2.1f) * 0.25f, -0.35f, Mathf.Sin(c * 2.1f) * 0.25f), Vector3.one * 0.16f, Quaternion.identity, 0);
                    var go = new GameObject("CoconutPalm_" + placed);
                    go.transform.SetParent(root, false);
                    go.transform.position = bp + Vector3.up * 1.0f;
                    var sc = go.AddComponent<CapsuleCollider>();
                    sc.isTrigger = true;
                    sc.radius = 0.7f;
                    sc.height = 2.2f;
                    var palm = go.AddComponent<PropPalm>();
                    palm.palmId = "p" + placed.ToString("00");
                    palm.crown = crown;
                    if (quest < 6 && k == 0) { PropSpots.Register("coconut:" + quest, NearDry(bp + new Vector3(0.9f, 0f, 0.6f), 0.3f)); quest++; }
                    PropInfo.AddLandSpot(crown, LandSpotKind.Palm, 1.2f, "palms");
                    excl.Add(bp.x, bp.z, 2.4f);
                    placed++;
                }
            }
            PropInfo.SetSpot("coconut_palms", new Vector3(placed, 0f, 0f));
        }

        // ------------------------------------------------------------------ a prop cluster every ~17 m of trail
        static void TrailClusters(PropBatch batch, Transform cols, PropExclusion excl, System.Random rng)
        {
            var samples = PropPaths.Samples;
            if (samples.Count < 2) return;
            float acc = 0f;
            int n = 0;
            Vector3 prev = samples[0].pos;
            for (int i = 1; i < samples.Count; i++)
            {
                Vector3 p = samples[i].pos;
                float step = (p - prev).magnitude;
                prev = p;
                if (step > 4f) { acc = 0f; continue; }     // a new route starts
                acc += step;
                if (acc < 17f) continue;
                acc = 0f;
                Vector3 dir = samples[i].dir;
                dir.y = 0f;
                if (dir.sqrMagnitude < 1e-4f) continue;
                dir.Normalize();
                Vector3 lat = Vector3.Cross(Vector3.up, dir) * (n % 2 == 0 ? 1f : -1f);
                Vector3 c = p + lat * (samples[i].halfWidth + 1.8f);
                c.y = G(c);
                if (c.y < 0.4f || excl.Blocked(c.x, c.z) || PropGround.Slope(c.x, c.z, 1f) > 0.7f) { n++; continue; }
                switch (n % 6)
                {
                    case 0: PropKit.RockCluster(batch, cols, c, 1100 + n, 0.9f); break;
                    case 1: PropKit.Log(batch, cols, c + Vector3.up * 0.18f, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg + PropUtil.Range(rng, -20f, 20f), PropUtil.Range(rng, 1.6f, 2.6f), 0.18f, PropMats.TreeBark); break;
                    case 2: Flowers(batch, c, rng); break;
                    case 3: PropKit.RockCluster(batch, cols, c, 1200 + n, 0.6f); Flowers(batch, c + lat * 0.9f, rng); break;
                    case 4: if (n % 12 == 4) PropKit.Bench(batch, cols, null, c, Mathf.Atan2(-lat.x, -lat.z) * Mathf.Rad2Deg, false); else PropKit.Bucket(batch, c, PropMats.Orange, 0.9f); break;
                    default: PropKit.MushroomRing(batch, c, 0.5f, 1300 + n); break;
                }
                excl.Add(c.x, c.z, 1.6f);
                n++;
            }
            PropInfo.SetSpot("trail_clusters", new Vector3(n, 0f, 0f));
        }

        static void Flowers(PropBatch batch, Vector3 c, System.Random rng)
        {
            Material[] petals = { PropMats.FlowerPink, PropMats.FlowerYellow, PropMats.FlowerWhite, PropMats.FlowerPurple };
            for (int i = 0; i < 7; i++)
            {
                Vector2 o = PropUtil.InDisc(rng, 0.8f);
                Vector3 p = new Vector3(c.x + o.x, 0f, c.z + o.y);
                p.y = G(p);
                batch.W(PropMats.Grass, p).Rod(p, p + Vector3.up * 0.35f, 0.012f, 0.01f, 3);
                batch.W(petals[(i + (int)(c.x * 3f)) & 3], p).Octa(p + Vector3.up * 0.37f, 0.06f);
            }
        }

        // ------------------------------------------------------------------ no empty 20 × 20 m patch of open land
        static void Coverage(PropBatch batch, Transform cols, PropExclusion excl, System.Random rng)
        {
            const float cell = 20f;
            int added = 0;
            for (float x = -130f; x <= 150f; x += cell)
                for (float z = -150f; z <= 150f; z += cell)
                {
                    float cx = x + cell * 0.5f, cz = z + cell * 0.5f;
                    float g = G(cx, cz);
                    if (g < 0.8f) continue;
                    if (PropJungle.JungleAt(cx, cz, g) > 0.45f) continue;          // the jungle fills it
                    if (excl.Blocked(cx, cz, 4f)) continue;                          // something already stands here
                    if (PropGround.Slope(cx, cz, 2f) > 0.6f) continue;              // cliffs / plateaus edges
                    Vector3 p = new Vector3(cx + PropUtil.Range(rng, -4f, 4f), 0f, cz + PropUtil.Range(rng, -4f, 4f));
                    p.y = G(p);
                    if (PropPaths.DistanceToPath(p.x, p.z) < 2.5f) continue;
                    if ((added & 1) == 0) PropKit.RockCluster(batch, cols, p, 1500 + added, PropUtil.Range(rng, 0.7f, 1.2f));
                    else { Flowers(batch, p, rng); PropKit.RockCluster(batch, null, p + new Vector3(1.2f, 0f, 0.4f), 1600 + added, 0.4f); }
                    excl.Add(p.x, p.z, 2f);
                    added++;
                }
            PropInfo.SetSpot("coverage_fill", new Vector3(added, 0f, 0f));
        }

        // ------------------------------------------------------------------ chopper drop zones (L.12)
        static void DropZones(PropExclusion excl)
        {
            PropInfo.DropZones.Clear();
            PropInfo.DropZoneIds.Clear();
            Vector3 lh = WorldLayout.Lighthouse, toHut = WorldLayout.Hut - lh;
            toHut.y = 0f;
            toHut.Normalize();
            var hl = WorldFeatures.SecondBeachHeadlands;
            Vector3 headland = hl != null && hl.Length > 1 ? hl[1].position : WorldLayout.SecondBeach + new Vector3(30f, 0f, 3f);
            (string id, Vector3 p)[] bases =
            {
                ("village_beach", WorldLayout.Village + new Vector3(-2f, 0f, -12f)),
                ("tide_pools", WorldLayout.TidePools - WorldFeatures.TideSeaward * 9f),
                ("lighthouse_yard", lh + toHut * 13f),
                ("pool_beach", WorldFeatures.PoolBeach),
                ("radio_plateau", WorldLayout.RadioTowerRuins + new Vector3(7f, 0f, 6f)),
                ("driftwood_beach", WorldLayout.DriftwoodBeach + new Vector3(-6f, 0f, 3f)),
                ("bay_beach", WorldFeatures.HiddenBayBeach + new Vector3(3f, 0f, 4f)),
                ("ravine_rim", new Vector3(WorldLayout.Ravine.x + 8f, 0f, WorldLayout.Ravine.z + 12f)),
                ("headland", headland),
                ("helipad_meadow", WorldLayout.Helipad + new Vector3(-11f, 0f, 6f)),
                ("waterfall_plateau", WorldLayout.WaterfallTop - WorldFeatures.FallDir * 12f),
                ("hut_beach", WorldLayout.Hut + new Vector3(20f, 0f, -6f)),
            };
            foreach (var b in bases)
            {
                Vector3 best = Vector3.zero;
                for (int pass = 0; pass < 2 && best == Vector3.zero; pass++)
                for (float r = 0f; r <= 16f && best == Vector3.zero; r += 1f)
                {
                    int n = r < 0.1f ? 1 : Mathf.CeilToInt(2f * Mathf.PI * r / 1.5f);
                    for (int i = 0; i < n; i++)
                    {
                        float a = i * Mathf.PI * 2f / n;
                        float x = b.p.x + Mathf.Cos(a) * r, z = b.p.z + Mathf.Sin(a) * r;
                        float g = G(x, z);
                        if (g < 0.6f || PropGround.Slope(x, z, 1.2f) > 0.3f || (pass == 0 && excl.Blocked(x, z, 0.5f))) continue;
                        // ≥ 3 m from the water edge
                        bool dry = true;
                        for (int k = 0; k < 8 && dry; k++)
                        {
                            float ka = k * Mathf.PI / 4f;
                            if (G(x + Mathf.Cos(ka) * 3f, z + Mathf.Sin(ka) * 3f) < 0.25f) dry = false;
                        }
                        if (!dry) continue;
                        best = new Vector3(x, g, z);
                        break;
                    }
                }
                if (best == Vector3.zero) best = new Vector3(b.p.x, Mathf.Max(G(b.p), 0.6f), b.p.z);
                PropInfo.DropZones.Add(best);
                PropInfo.DropZoneIds.Add(b.id);
            }
        }

        // ------------------------------------------------------------------ island land-fauna spots (L.11)
        static void FaunaSpots()
        {
            // north cliffs (tropicbirds nest on ledges), the lighthouse railing / roof (frigatebirds soar over it)
            for (int i = 0; i < 6; i++)
            {
                float a = (60f + i * 18f) * Mathf.Deg2Rad;
                Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                for (float r = 70f; r < 160f; r += 1f)
                {
                    float x = d.x * r, z = d.z * r;
                    float g = G(x, z);
                    if (g < 4f) continue;
                    if (PropGround.Slope(x, z, 1.5f) > 1.0f && G(x + d.x * 3f, z + d.z * 3f) < g - 3f)
                    {
                        PropInfo.AddLandSpot(new Vector3(x, g + 0.1f, z), LandSpotKind.Nest, 1.2f, "north_cliffs", d);
                        break;
                    }
                }
            }
            // gulls: dock posts and the hut beach shore line
            PropInfo.AddLandSpot(WorldLayout.Hut + new Vector3(22f, 0.6f, -2f), LandSpotKind.Shore, 6f, "hut_beach");
            PropInfo.AddLandSpot(WorldLayout.DriftwoodBeach + new Vector3(0f, 0.6f, 3f), LandSpotKind.Shore, 6f, "driftwood_beach");
            // the pig swims at the village beach 10:00–12:00
            PropInfo.AddLandSpot(new Vector3(WorldLayout.Village.x + 8f, Waves.SeaLevel, WorldLayout.Village.z - 28f), LandSpotKind.Water, 6f, "village_beach");
            // goats also graze on the meadow near the helipad
            PropInfo.AddLandSpot(WorldLayout.Helipad + new Vector3(-12f, 4.1f, 8f), LandSpotKind.Meadow, 7f, "helipad_meadow");
        }
    }
}
