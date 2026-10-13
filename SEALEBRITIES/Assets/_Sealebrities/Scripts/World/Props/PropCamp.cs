using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Round 3 (GDD §19 "verstecktes Lager im Dschungel auf der anderen Inselseite" + the player's answers): the hidden
    /// camp of eccentric dropout hermits („Aussteiger-Einsiedler“, 4–6 of them with their own roles) who revere the sea and
    /// distrust strangers – NOT a real people, no stereotypes, no costumes:
    /// handmade shrines to the sea (shells, driftwood, glass floats), patched sail-cloth tents, a palm lean-to, hammocks,
    /// the leader's shelter with a chest of sea finds, a fire ring with log benches, a fish-smoking rack whose thin smoke is
    /// the only hint from afar, drying racks, junk sculptures of driftwood and ship debris (steering wheel, buoys, bottle
    /// chimes, a TV set, the coconut friend „Kokos“), trap lines with tin-can rattles, a sharpening bench (the hermits
    /// teach spear sharpening – NPC step), a goat pen and a chicken run, a rain catcher, a garden patch and a sea lookout.
    /// Terrain: WorldHeightField.CampHollow (a flat floor in a wooded bowl, HermitCampRadius) + a narrow game trail from
    /// the waterfall-grotto clearing (no dirt path, no signpost: cairns and a shell charm only).
    /// Publishes NPC anchors for the NPC step (PropSpots "hermit_*", see SpotIds) and the discovery flag
    /// StoryKeys.HermitCampFound (PropCampDiscovery). Everything deterministic (fixed seeds).
    public static class PropCamp
    {
        /// Every NPC anchor id this builder registers (QuestSpots + PropInfo spots), for the NPC step and the tests.
        public static readonly string[] SpotIds =
        {
            "hermit_camp", "hermit_gap", "hermit_trail_start", "hermit_trail_bend",
            "hermit_fire_0", "hermit_fire_1", "hermit_fire_2", "hermit_fire_3",
            "hermit_tent_0", "hermit_tent_1", "hermit_tent_2", "hermit_shelter", "hermit_leanto",
            "hermit_workbench", "hermit_grind_lesson", "hermit_drying", "hermit_smoker", "hermit_totem_0", "hermit_totem_1",
            "hermit_goats", "hermit_chickens", "hermit_garden", "hermit_rain", "hermit_lookout", "hermit_guard_0", "hermit_guard_1",
            "hermit_spawn_0", "hermit_spawn_1", "hermit_spawn_2", "hermit_spawn_3", "hermit_spawn_4", "hermit_spawn_5",
            "hermit_shrine", "hermit_shrine_sea",
            // one work spot per hermit role (GDD §19 answers: 4–6 hermits with their own roles)
            "hermit_role_leader", "hermit_role_smith", "hermit_role_hunter", "hermit_role_cook", "hermit_role_lookout", "hermit_role_gardener",
        };

        static float G(float x, float z) => PropGround.H(x, z);
        static float G(Vector3 p) => PropGround.H(p.x, p.z);

        static Vector3 C, F, Rt;          // centre (on the floor), forward (toward the entrance gap), right
        static PropBatch batch, full, lite, under;
        static Transform root, cols;
        static GameObject treeCols;

        /// Ground point at polar (angle from the entrance direction, counter-clockwise, degrees; radius m).
        static Vector3 Polar(float deg, float r)
        {
            float a = deg * Mathf.Deg2Rad;
            Vector3 d = F * Mathf.Cos(a) - Rt * Mathf.Sin(a);
            Vector3 p = C + d * r;
            p.y = G(p);
            return p;
        }

        /// Yaw (degrees) that makes +z of an object at p face the target.
        static float Face(Vector3 p, Vector3 target)
        {
            Vector3 d = target - p;
            d.y = 0f;
            return d.sqrMagnitude < 1e-6f ? 0f : Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        }

        static Material Cloth(int i)
        {
            // weathered sail cloth and tarps: faded orange, grey-blue, stained cream, olive
            string[] c = { "#C9824E", "#7E8FA0", "#D8CBB0", "#7F8656", "#B0705A" };
            return PropMats.Flat(LowPoly.Hex(c[((i % c.Length) + c.Length) % c.Length]));
        }

        public static IEnumerator Build(Transform island, PropExclusion excl)
        {
            Vector3 c = WorldLayout.HermitCamp;
            C = new Vector3(c.x, G(c.x, c.z), c.z);
            F = WorldFeatures.CampEntranceDir;
            Rt = Vector3.Cross(Vector3.up, F).normalized;
            root = PropUtil.WorldRoot("HermitCamp", island);
            cols = new GameObject("Colliders").transform;
            cols.SetParent(root, false);
            treeCols = new GameObject("TreeColliders");
            treeCols.transform.SetParent(root, false);
            batch = new PropBatch(0f);
            full = new PropBatch(64f);
            lite = new PropBatch(64f);
            under = new PropBatch(0f);
            var rng = new System.Random(19870);

            // keep the jungle off the camp floor and the trail
            excl.Add(C.x, C.z, WorldLayout.HermitCampRadius + 0.5f);
            excl.AddLine(WorldFeatures.CampTrailStart, WorldFeatures.CampTrailGap, 1.6f);
            excl.AddLine(WorldFeatures.CampTrailBend, WorldFeatures.CampTrailGap, 1.6f);
            excl.Add(WorldFeatures.CampLookout.x, WorldFeatures.CampLookout.z, 3.5f);

            FireRing();
            Tents();
            if (PropRun.Due()) yield return null;
            LeaderShelter();
            LeanTo();
            Workbench();
            if (PropRun.Due()) yield return null;
            Racks();
            Totems(rng);
            Pens();
            if (PropRun.Due()) yield return null;
            RainCatcher();
            Garden(rng);
            TrapLines(rng);
            Lookout();
            SeaShrine();
            TrailMarks(rng);
            if (PropRun.Due()) yield return null;
            RimJungle(rng, excl);
            Spots();
            Discovery();

            batch.Flush(root, "CampMesh", ShadowCastingMode.On, 180f);
            under.Flush(root, "CampPlants", ShadowCastingMode.On, 110f);
            PropBatch.FlushLod(full, lite, root, "CampTrees", ShadowCastingMode.On, PropJungle.TreeNear, PropJungle.TreeFar);
            batch = full = lite = under = null;
        }

        // ------------------------------------------------------------------ fire ring
        static void FireRing()
        {
            // the hermits' cooking fire is a real campfire spot (light it with 2 driftwood, cook on it – PropSurvival)
            Vector3 fire = Polar(200f, 1.2f);
            try { PropSurvival.CreateCampfire(root, "camp_hermits", fire); }
            catch (System.Exception e) { Debug.LogError("Sealebrities props: hermit campfire failed: " + e); }
            PropInfo.SetSpot("hermit_fire", fire);
            Vector3 cook = fire + (C - fire).normalized * 0.2f + F * 1.35f;
            cook.y = G(cook);
            PropSpots.Register("hermit_role_cook", cook, Face(cook, fire));
            // three log benches around it (sit: PropSeat), a cooking tripod with a dented pot
            for (int i = 0; i < 3; i++)
            {
                float a = 35f + i * 120f;
                Vector3 lp = fire + (F * Mathf.Cos(a * Mathf.Deg2Rad) - Rt * Mathf.Sin(a * Mathf.Deg2Rad)) * 2.6f;
                lp.y = G(lp);
                // Log lies along the yaw's right axis: facing the fire = a bench tangential to the ring
                float yaw = Face(lp, fire);
                PropKit.Log(batch, cols, lp + Vector3.up * 0.2f, yaw, 2.1f, 0.21f, i == 1 ? PropMats.Driftwood : PropMats.WoodDark, true);
                PropKit.Seat(root, lp + Vector3.up * 0.42f, Face(lp, fire), lp - (fire - lp).normalized * 0.9f);
            }
            var tri = batch.W(PropMats.WoodDark, fire);
            Vector3 top = fire + Vector3.up * 1.35f;
            for (int i = 0; i < 3; i++)
            {
                float a = i * 2.094f + 0.5f;
                tri.Rod(fire + new Vector3(Mathf.Cos(a) * 0.95f, -0.05f, Mathf.Sin(a) * 0.95f), top, 0.035f, 0.028f, 5);
            }
            batch.W(PropMats.Rope, fire).Rod(top, top - Vector3.up * 0.55f, 0.008f, 0.008f, 3);
            Vector3 pot = top - Vector3.up * 0.82f;
            batch.W(PropMats.DarkMetal, fire).Cylinder(pot, Quaternion.identity, 0.2f, 0.17f, 0.24f, 8, true, false);
            batch.W(PropMats.DarkMetal, fire).Torus(pot + Vector3.up * 0.26f, Quaternion.Euler(90f, 0f, 0f), 0.16f, 0.01f, 8, 3);
            // a stack of firewood + a cut log as a chopping block
            Vector3 wood = Polar(300f, 4.4f);
            for (int i = 0; i < 6; i++)
            {
                Vector3 lp = wood + Rt * (-0.4f + (i % 3) * 0.3f) + Vector3.up * (0.12f + (i / 3) * 0.22f);
                batch.W(PropMats.Driftwood, wood).Cylinder(lp - F * 0.55f, Quaternion.LookRotation(F) * Quaternion.Euler(90f, 0f, 0f), 0.11f, 0.1f, 1.1f, 6, true, true);
            }
            PropCol.Box(cols, wood + Vector3.up * 0.3f, new Vector3(1.1f, 0.6f, 1.2f), Quaternion.LookRotation(F), "Firewood");
            Vector3 block = wood + Rt * 1.3f;
            block.y = G(block);
            batch.W(PropMats.TreeTrunk, block).Cylinder(block - Vector3.up * 0.05f, Quaternion.identity, 0.3f, 0.28f, 0.5f, 8, false, true);
            batch.W(PropMats.WoodLight, block).Disk(block + Vector3.up * 0.451f, 0.27f, 8, Quaternion.identity, true);
            PropCol.Capsule(cols, block + Vector3.up * 0.25f, 0.3f, 0.5f, Quaternion.identity, "ChoppingBlock");
        }

        // ------------------------------------------------------------------ tents
        static void Tents()
        {
            float[] ang = { 118f, 138f, 192f };
            for (int i = 0; i < 3; i++)
            {
                Vector3 p = Polar(ang[i], 8.6f);
                float yaw = Face(p, C);
                PropKit.Tent(batch, cols, p, yaw, LowPoly.Hex(i == 0 ? "#C9824E" : i == 1 ? "#7E8FA0" : "#D8CBB0"), 2.4f, 2.0f, 1.5f);
                // patches sewn on the canvas + a rope with a drying shirt
                Quaternion q = Quaternion.Euler(0f, yaw, 0f);
                for (int k = 0; k < 2; k++)
                {
                    Vector3 pc = p + q * new Vector3(k == 0 ? -0.55f : 0.5f, 0.75f + k * 0.2f, -0.2f + k * 0.5f);
                    batch.W(Cloth(i + 2 + k), p).Box(pc, new Vector3(0.36f, 0.3f, 0.02f), q * Quaternion.Euler(0f, k == 0 ? -90f : 90f, k == 0 ? 35f : -35f));
                }
                PropSpots.Register("hermit_tent_" + i, p + q * new Vector3(0f, 0f, 1.9f), yaw + 180f);
            }
            // two oil lanterns on driftwood posts (they glow at night – a hint through the trees)
            Vector3 l0 = Polar(126f, 5.6f), l1 = Polar(232f, 5.4f);
            PropKit.LanternPost(batch, cols, root, l0, Face(l0, C), 1.75f, true);
            PropKit.LanternPost(batch, cols, root, l1, Face(l1, C), 1.75f, true);
        }

        // ------------------------------------------------------------------ the leader's shelter (sail roof, sea finds)
        static void LeaderShelter()
        {
            Vector3 p = Polar(236f, 8.4f);
            float yaw = Face(p, C);
            Quaternion q = Quaternion.Euler(0f, yaw, 0f);
            Vector3 P(float x, float y, float z) { var w = p + q * new Vector3(x, 0f, z); w.y = G(w) + y; return w; }
            // four driftwood posts, a ridge pole, a big patched sail as the roof
            var post = batch.W(PropMats.Driftwood, p);
            Vector3[] posts = { P(-1.8f, 0f, -1.3f), P(1.8f, 0f, -1.3f), P(-1.8f, 0f, 1.3f), P(1.8f, 0f, 1.3f) };
            float[] ph = { 2.0f, 2.0f, 2.5f, 2.5f };
            for (int i = 0; i < 4; i++)
            {
                post.Rod(posts[i] - Vector3.up * 0.3f, posts[i] + Vector3.up * ph[i], 0.08f, 0.07f, 6, true);
                PropCol.Capsule(cols, posts[i] + Vector3.up * ph[i] * 0.5f, 0.1f, ph[i] + 0.3f, Quaternion.identity, "ShelterPost");
            }
            Vector3 b0 = posts[0] + Vector3.up * 2.0f, b1 = posts[1] + Vector3.up * 2.0f, f0 = posts[2] + Vector3.up * 2.5f, f1 = posts[3] + Vector3.up * 2.5f;
            post.Rod(f0, f1, 0.07f, 0.07f, 6, true);
            post.Rod(b0, b1, 0.07f, 0.07f, 6, true);
            var sail = batch.W(Cloth(2), p);
            // sagging sail in 3 strips (a patched look)
            for (int s = 0; s < 3; s++)
            {
                float t0 = s / 3f, t1 = (s + 1) / 3f;
                Vector3 a0 = Vector3.Lerp(b0, b1, t0), a1 = Vector3.Lerp(b0, b1, t1), c0 = Vector3.Lerp(f0, f1, t0), c1 = Vector3.Lerp(f0, f1, t1);
                Vector3 sag = Vector3.down * 0.18f;
                var m = batch.W(Cloth(s == 1 ? 0 : 2), p);
                m.Quad2(a0, a1, (a1 + c1) * 0.5f + sag + q * new Vector3(0f, 0f, 0f), (a0 + c0) * 0.5f + sag, Vector3.up);
                m.Quad2((a0 + c0) * 0.5f + sag, (a1 + c1) * 0.5f + sag, c1 + q * new Vector3(0f, -0.05f, 0.35f), c0 + q * new Vector3(0f, -0.05f, 0.35f), Vector3.up);
            }
            PropCol.Box(cols, P(0f, 2.3f, 0f), new Vector3(3.8f, 0.12f, 3.0f), q * Quaternion.Euler(-9f, 0f, 0f), "ShelterRoof");
            // inside: the leader's place – a driftwood "throne" with a shell back, a woven chest of everything the sea
            // brought (glass floats, bottles, shells – the hermits keep every gift of the sea), a hanging shell mobile
            Vector3 seat = P(0.2f, 0f, -0.6f);
            Quaternion sq = q;
            var tw = batch.W(PropMats.Driftwood, seat);
            tw.Box(seat + sq * new Vector3(0f, 0.42f, 0f), new Vector3(0.9f, 0.08f, 0.6f), sq);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    tw.Box(seat + sq * new Vector3(sx * 0.38f, 0.21f, sz * 0.24f), new Vector3(0.08f, 0.42f, 0.08f), sq);
            for (int k = 0; k < 5; k++)
                tw.Rod(seat + sq * new Vector3(-0.4f + k * 0.2f, 0.45f, -0.28f), seat + sq * new Vector3(-0.45f + k * 0.22f, 1.35f + 0.12f * Mathf.Sin(k * 1.3f), -0.36f), 0.03f, 0.02f, 4, true);
            for (int k = 0; k < 7; k++)
            {
                Vector3 sh = seat + sq * new Vector3(-0.36f + k * 0.12f, 0.95f + 0.25f * Mathf.Sin(k * 0.9f), -0.33f);
                batch.W(k % 2 == 0 ? PropMats.FlowerWhite : PropMats.FlowerPink, seat).Ico(sh, new Vector3(0.07f, 0.06f, 0.025f), sq, 0);
            }
            PropCol.Box(cols, seat + sq * new Vector3(0f, 0.5f, -0.05f), new Vector3(0.95f, 1.0f, 0.7f), sq, "LeaderSeat");
            // the chest of sea finds (Gus' lost IOUs ended up in it too – the "village_loft" secret, PropCollect)
            Vector3 chest = P(-1.1f, 0f, -0.75f);
            float cyaw = yaw + 8f;
            Quaternion cq = Quaternion.Euler(0f, cyaw, 0f);
            batch.W(PropMats.Thatch, chest).Box(chest + Vector3.up * 0.25f, new Vector3(0.8f, 0.5f, 0.55f), cq);
            batch.W(PropMats.ThatchDark, chest).Box(chest + Vector3.up * 0.52f, new Vector3(0.84f, 0.06f, 0.59f), cq);
            PropCol.Box(cols, chest + Vector3.up * 0.27f, new Vector3(0.84f, 0.55f, 0.6f), cq, "SeaChest");
            PropInfo.SetSpot("village_loft", chest + Vector3.up * 0.56f);
            for (int k = 0; k < 4; k++)
            {
                Vector3 fl = P(0.95f + (k % 2) * 0.3f, 0f, -0.95f + (k / 2) * 0.32f);
                batch.W(k % 2 == 0 ? PropMats.BottleGlass : PropMats.Glass, fl).Ico(fl + Vector3.up * 0.13f, Vector3.one * 0.13f, Quaternion.identity, 1);
                batch.W(PropMats.Rope, fl).Torus(fl + Vector3.up * 0.13f, Quaternion.Euler(90f, k * 30f, 0f), 0.135f, 0.01f, 8, 3);
            }
            Vector3 mob = (b0 + b1 + f0 + f1) * 0.25f - Vector3.up * 0.25f;
            batch.W(PropMats.Driftwood, mob).Rod(mob - q * Vector3.right * 0.5f, mob + q * Vector3.right * 0.5f, 0.02f, 0.02f, 4);
            for (int k = 0; k < 6; k++)
            {
                Vector3 h = mob + q * Vector3.right * (-0.45f + k * 0.18f);
                float len = 0.2f + 0.15f * (k % 3);
                batch.W(PropMats.Rope, mob).Rod(h, h - Vector3.up * len, 0.004f, 0.004f, 3);
                batch.W(k % 2 == 0 ? PropMats.FlowerWhite : PropMats.FlowerPink, mob).Ico(h - Vector3.up * (len + 0.04f), new Vector3(0.05f, 0.04f, 0.02f), Quaternion.identity, 0);
            }
            PropSpots.Register("hermit_role_leader", seat + sq * new Vector3(0f, 0f, 0.55f), yaw);            // a board with the hermits' own tide calendar (they live by the tides – notches + shells)
            Vector3 board = P(1.75f, 1.25f, 1.1f);
            Quaternion bq = q * Quaternion.Euler(0f, 180f, 0f);
            batch.W(PropMats.Driftwood, board).Box(board, new Vector3(0.9f, 0.7f, 0.05f), bq);
            for (int i = 0; i < 9; i++)
                batch.W(PropMats.WoodDark, board).Box(board + bq * new Vector3(-0.35f + i * 0.085f, 0.18f, -0.03f), new Vector3(0.012f, 0.18f, 0.01f), bq);
            // the old tide-calendar logbook page (collectible) lies on the shelter floor below the board
            PropInfo.SetSpot("tide_calendar_page", P(-0.2f, 0.03f, -0.35f));
            PropSpots.Register("hermit_shelter", P(0f, 0f, 1.6f), yaw + 180f);
        }

        // ------------------------------------------------------------------ palm lean-to + hammock
        static void LeanTo()
        {
            Vector3 p = Polar(286f, 8.6f);
            float yaw = Face(p, C);
            Quaternion q = Quaternion.Euler(0f, yaw, 0f);
            Vector3 P(float x, float y, float z) { var w = p + q * new Vector3(x, 0f, z); w.y = G(w) + y; return w; }
            var wood = batch.W(PropMats.WoodDark, p);
            Vector3 fl = P(-1.4f, 0f, 0.8f), fr = P(1.4f, 0f, 0.8f), bl = P(-1.4f, 0f, -0.9f), br = P(1.4f, 0f, -0.9f);
            wood.Rod(fl - Vector3.up * 0.3f, fl + Vector3.up * 1.9f, 0.07f, 0.06f, 6, true);
            wood.Rod(fr - Vector3.up * 0.3f, fr + Vector3.up * 1.9f, 0.07f, 0.06f, 6, true);
            wood.Rod(fl + Vector3.up * 1.9f, fr + Vector3.up * 1.9f, 0.06f, 0.06f, 6, true);
            PropCol.Capsule(cols, fl + Vector3.up * 0.8f, 0.09f, 2.2f, Quaternion.identity, "LeanToPost");
            PropCol.Capsule(cols, fr + Vector3.up * 0.8f, 0.09f, 2.2f, Quaternion.identity, "LeanToPost");
            // thatch of palm fronds sloping to the back
            for (int k = 0; k < 7; k++)
            {
                float x = -1.35f + k * 0.45f;
                Vector3 a = fl + (fr - fl) * ((x + 1.4f) / 2.8f) + Vector3.up * 1.95f;
                Vector3 b = Vector3.Lerp(bl, br, (x + 1.4f) / 2.8f) + Vector3.up * 0.15f;
                batch.W(k % 2 == 0 ? PropMats.PalmLeafDry : PropMats.Thatch, p).Beam(a, b, 0.5f, 0.06f);
            }
            PropCol.Box(cols, (fl + fr + bl + br) * 0.25f + Vector3.up * 1.0f, new Vector3(2.9f, 0.15f, 2.2f), q * Quaternion.Euler(-48f, 0f, 0f), "LeanToRoof");
            // sleeping mats + a blanket
            for (int i = 0; i < 2; i++)
            {
                Vector3 m = P(-0.6f + i * 1.2f, 0.03f, -0.1f);
                batch.W(i == 0 ? PropMats.Thatch : PropMats.Blanket, m).Box(m, new Vector3(0.8f, 0.05f, 1.6f), q);
            }
            PropSpots.Register("hermit_leanto", P(0f, 0f, 1.6f), yaw + 180f);
            // a hammock between two posts beside the lean-to (players may sleep here too – registered sleep spot)
            Vector3 ha = Polar(304f, 7.6f), hb = Polar(320f, 8.4f);
            var hw = batch.W(PropMats.Driftwood, ha);
            hw.Rod(ha - Vector3.up * 0.3f, ha + Vector3.up * 1.85f, 0.09f, 0.08f, 6, true);
            hw.Rod(hb - Vector3.up * 0.3f, hb + Vector3.up * 1.85f, 0.09f, 0.08f, 6, true);
            PropCol.Capsule(cols, ha + Vector3.up * 0.8f, 0.1f, 2.2f, Quaternion.identity, "HammockPost");
            PropCol.Capsule(cols, hb + Vector3.up * 0.8f, 0.1f, 2.2f, Quaternion.identity, "HammockPost");
            Vector3 hookA = ha + Vector3.up * 1.6f, hookB = hb + Vector3.up * 1.6f;
            Vector3 dir = (hookB - hookA).normalized;
            hookA += dir * 0.15f; hookB -= dir * 0.15f;
            PropKit.Hammock(batch, cols, hookA, hookB, 1, false);
            PropKit.HammockBed(root, "camp_hammock", hookA, hookB);
            // a second hammock in the sleeping corner (the hermits sleep in hammocks, off the ground)
            Vector3 hc = Polar(328f, 10.4f), hd = Polar(343f, 10.6f);
            var hw2 = batch.W(PropMats.Driftwood, hc);
            hw2.Rod(hc - Vector3.up * 0.3f, hc + Vector3.up * 1.85f, 0.09f, 0.08f, 6, true);
            hw2.Rod(hd - Vector3.up * 0.3f, hd + Vector3.up * 1.85f, 0.09f, 0.08f, 6, true);
            PropCol.Capsule(cols, hc + Vector3.up * 0.8f, 0.1f, 2.2f, Quaternion.identity, "HammockPost");
            PropCol.Capsule(cols, hd + Vector3.up * 0.8f, 0.1f, 2.2f, Quaternion.identity, "HammockPost");
            Vector3 hookC = hc + Vector3.up * 1.6f, hookD = hd + Vector3.up * 1.6f;
            Vector3 dir2 = (hookD - hookC).normalized;
            hookC += dir2 * 0.15f; hookD -= dir2 * 0.15f;
            PropKit.Hammock(batch, cols, hookC, hookD, 2, false);
            PropKit.HammockBed(root, "camp_hammock_2", hookC, hookD);
        }

        // ------------------------------------------------------------------ sharpening bench (the hermits' lesson)
        static void Workbench()
        {
            Vector3 p = Polar(338f, 7.0f);
            float yaw = Face(p, C);
            Quaternion q = Quaternion.Euler(0f, yaw, 0f);
            PropKit.Table(batch, cols, p, yaw, 1.6f, 0.7f, PropMats.Driftwood);
            // a hand-cranked whetstone wheel on the bench, spears and a stick in a rack, flat stones
            Vector3 wc = p + q * new Vector3(-0.4f, 0.98f, 0f);
            batch.W(PropMats.Flat(LowPoly.Hex("#A9AEB4")), wc).Cylinder(wc - q * new Vector3(0.05f, 0f, 0f), q * Quaternion.Euler(0f, 0f, -90f), 0.22f, 0.22f, 0.1f, 12, true, true);
            batch.W(PropMats.WoodDark, wc).Box(wc - Vector3.up * 0.13f, new Vector3(0.14f, 0.16f, 0.32f), q);
            batch.W(PropMats.DarkMetal, wc).Rod(wc + q * new Vector3(0.06f, 0f, 0f), wc + q * new Vector3(0.22f, 0f, 0f), 0.015f, 0.015f, 5);
            for (int i = 0; i < 3; i++)
            {
                Vector3 s = p + q * new Vector3(0.25f + i * 0.2f, 0.8f, 0.12f - i * 0.08f);
                PropRocks.Build(batch.W(PropMats.Stone, s), s, new Vector3(0.09f, 0.04f, 0.07f), q * Quaternion.Euler(0f, i * 40f, 0f), 700 + i, 0, 0.1f);
            }
            Vector3 rack = p + q * new Vector3(0f, 0f, -0.75f);
            rack.y = G(rack);
            var rw = batch.W(PropMats.WoodDark, rack);
            rw.Box(rack + q * new Vector3(0f, 0.9f, 0f), new Vector3(1.3f, 0.06f, 0.08f), q);
            for (int s = -1; s <= 1; s += 2) rw.Box(rack + q * new Vector3(s * 0.62f, 0.5f, 0f), new Vector3(0.07f, 1.0f, 0.07f), q);
            for (int i = 0; i < 4; i++)
            {
                Vector3 b = rack + q * new Vector3(-0.45f + i * 0.3f, 0.02f, 0.12f);
                batch.W(i == 2 ? PropMats.WoodLight : PropMats.Driftwood, rack).Rod(b, b + q * new Vector3(0f, 2.0f, -0.25f), 0.025f, 0.02f, 5);
                if (i != 2) batch.W(PropMats.Stone, rack).Cylinder(b + q * new Vector3(0f, 2.0f, -0.25f), q * Quaternion.Euler(-7f, 0f, 0f), 0.03f, 0f, 0.16f, 5, true, false);
            }
            PropCol.Box(cols, rack + q * new Vector3(0f, 0.9f, 0f), new Vector3(1.35f, 1.8f, 0.3f), q, "SpearRack");
            PropSpots.Register("hermit_workbench", p + q * new Vector3(0f, 0f, 0.9f), yaw + 180f);
            PropSpots.Register("hermit_grind_lesson", p + q * new Vector3(0.5f, 0f, 1.1f), yaw + 180f);
            PropSpots.Register("hermit_role_smith", p + q * new Vector3(-0.35f, 0f, 0.7f), yaw + 180f);
            PropInfo.SetSpot("camp_grinder", p, yaw);
        }

        // ------------------------------------------------------------------ fish drying + smoking racks
        static void Racks()
        {
            Vector3 d = Polar(45f, 6.5f);
            PropKit.NetRack(batch, cols, d, Face(d, C) + 90f, 2.4f);
            // a line of drying fish on a second pole
            Vector3 l0 = Polar(82f, 6.6f), l1 = Polar(98f, 6.6f);
            var wood = batch.W(PropMats.WoodDark, l0);
            wood.Rod(l0 - Vector3.up * 0.3f, l0 + Vector3.up * 1.7f, 0.05f, 0.045f, 5, true);
            wood.Rod(l1 - Vector3.up * 0.3f, l1 + Vector3.up * 1.7f, 0.05f, 0.045f, 5, true);
            PropCol.Capsule(cols, l0 + Vector3.up * 0.7f, 0.07f, 2f, Quaternion.identity, "Pole");
            PropCol.Capsule(cols, l1 + Vector3.up * 0.7f, 0.07f, 2f, Quaternion.identity, "Pole");
            Vector3 ta = l0 + Vector3.up * 1.65f, tb = l1 + Vector3.up * 1.65f;
            batch.W(PropMats.Rope, ta).Rod(ta, tb, 0.01f, 0.01f, 3);
            for (int i = 1; i < 7; i++)
            {
                Vector3 h = Vector3.Lerp(ta, tb, i / 7f) - Vector3.up * (0.06f * Mathf.Sin(i / 7f * Mathf.PI));
                Vector3 fish = h - Vector3.up * 0.22f;
                batch.W(PropMats.Flat(LowPoly.Hex(i % 2 == 0 ? "#8A7A5E" : "#9C8A6A")), h).Ico(fish, new Vector3(0.05f, 0.2f, 0.09f), Quaternion.identity, 0);
                batch.W(PropMats.Flat(LowPoly.Hex("#7A6A50")), h).Tri2(fish - Vector3.up * 0.18f, fish - Vector3.up * 0.3f + Rt * 0.07f, fish - Vector3.up * 0.3f - Rt * 0.07f, F);
            }
            PropSpots.Register("hermit_drying", Polar(88f, 5.2f), 0f);
            Vector3 hunter = Polar(42f, 5.0f);
            PropSpots.Register("hermit_role_hunter", hunter, Face(hunter, d));

            // the smoking rack: a little closed box of planks over embers – its thin smoke column is the hint from afar
            Vector3 s = Polar(145f, 4.8f);
            float yaw = Face(s, C);
            Quaternion q = Quaternion.Euler(0f, yaw, 0f);
            batch.W(PropMats.Driftwood, s).Box(s + Vector3.up * 0.6f, new Vector3(1.0f, 1.2f, 0.8f), q);
            batch.W(PropMats.WoodDark, s).Box(s + Vector3.up * 1.25f, new Vector3(1.15f, 0.08f, 0.95f), q * Quaternion.Euler(6f, 0f, 0f));
            batch.W(PropMats.Ink, s).Box(s + q * new Vector3(0f, 0.25f, 0.41f), new Vector3(0.5f, 0.3f, 0.02f), q);
            batch.W(PropMats.FireGlow, s).Box(s + q * new Vector3(0f, 0.18f, 0.415f), new Vector3(0.36f, 0.12f, 0.02f), q);
            PropCol.Box(cols, s + Vector3.up * 0.65f, new Vector3(1.05f, 1.3f, 0.85f), q, "SmokingRack");
            var smoke = PropFX.Smoke(root, s + Vector3.up * 1.45f);
            if (smoke != null)
            {
                var main = smoke.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(10f, 14f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 2.0f);
                main.maxParticles = 90;
                var em = smoke.emission;
                em.rateOverTime = 5f;
            }
            PropSpots.Register("hermit_smoker", s + q * new Vector3(0f, 0f, 1.1f), yaw + 180f);
        }

        // ------------------------------------------------------------------ junk sculptures (driftwood + ship debris)
        static void Totems(System.Random rng)
        {
            for (int t = 0; t < 2; t++)
            {
                Vector3 p = Polar(t == 0 ? 28f : -28f, 10.2f);
                var w = batch.W(PropMats.Driftwood, p);
                float h = 3.0f + t * 0.4f;
                w.Rod(p - Vector3.up * 0.4f, p + Vector3.up * h, 0.13f, 0.1f, 7, true);
                PropCol.Capsule(cols, p + Vector3.up * (h * 0.5f), 0.16f, h + 0.4f, Quaternion.identity, "JunkTotem");
                float yaw = Face(p, C + F * 30f);
                Quaternion q = Quaternion.Euler(0f, yaw, 0f);
                // a ship's steering wheel / a life ring, buoys, a hanging bottle chime, a TV set, a coconut "face"
                Vector3 top = p + Vector3.up * (h - 0.35f);
                if (t == 0)
                {
                    batch.W(PropMats.WoodDark, p).Torus(top, q * Quaternion.Euler(90f, 0f, 0f), 0.42f, 0.04f, 12, 4);
                    for (int k = 0; k < 8; k++)
                    {
                        float a = k * Mathf.PI / 4f;
                        Vector3 dk = q * new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                        batch.W(PropMats.WoodDark, p).Rod(top + dk * 0.05f, top + dk * 0.55f, 0.025f, 0.03f, 5);
                    }
                }
                else
                {
                    for (int k = 0; k < 8; k++)
                    {
                        float a0 = k * Mathf.PI / 4f, a1 = (k + 1) * Mathf.PI / 4f;
                        batch.W(k % 2 == 0 ? PropMats.Orange : PropMats.White, p).Rod(top + q * new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f) * 0.32f, top + q * new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f) * 0.32f, 0.07f, 0.07f, 5, true);
                    }
                }
                Vector3 tv = p + Vector3.up * (h * 0.52f) + q * new Vector3(0f, 0f, 0.2f);
                batch.W(PropMats.TvBody, p).Box(tv, new Vector3(0.5f, 0.4f, 0.36f), q);
                batch.W(PropMats.Screen, p).Box(tv + q * new Vector3(0f, 0f, 0.181f), new Vector3(0.36f, 0.28f, 0.01f), q);
                for (int k = 0; k < 3; k++)
                {
                    Vector3 b = p + Vector3.up * (0.9f + k * 0.35f) + q * new Vector3(k % 2 == 0 ? 0.22f : -0.22f, 0f, 0.05f);
                    batch.W(k == 1 ? PropMats.BuoyWhite : PropMats.BuoyOrange, p).Ico(b, Vector3.one * 0.17f, Quaternion.identity, 1);
                }
                // bottle chime on a crossbar
                Vector3 bar = p + Vector3.up * (h - 0.95f);
                batch.W(PropMats.Driftwood, p).Rod(bar - q * Vector3.right * 0.7f, bar + q * Vector3.right * 0.7f, 0.04f, 0.035f, 5);
                for (int k = 0; k < 4; k++)
                {
                    Vector3 hk = bar + q * Vector3.right * (-0.55f + k * 0.37f);
                    float len = 0.25f + 0.12f * (k % 3);
                    batch.W(PropMats.Rope, p).Rod(hk, hk - Vector3.up * len, 0.006f, 0.006f, 3);
                    batch.W(PropMats.BottleGlass, p).Cylinder(hk - Vector3.up * (len + 0.22f), Quaternion.identity, 0.045f, 0.03f, 0.22f, 6, true, true);
                }
                PropSpots.Register("hermit_totem_" + t, p + q * new Vector3(0f, 0f, -1.2f), yaw);
                PropSpots.Register("hermit_guard_" + t, Polar(t == 0 ? 12f : -12f, 9.0f), Face(Polar(0f, 9f), C + F * 30f));
            }
            // "Kokos" – the coconut friend on a crate by the fire (a castaway's buddy): coconut head with a painted face,
            // a life-jacket body
            Vector3 kp = Polar(252f, 3.4f);
            PropKit.Crate(batch, cols, kp, 0.55f, Face(kp, C));
            Vector3 body = kp + Vector3.up * 0.8f;
            Quaternion kq = Quaternion.Euler(0f, Face(kp, C), 0f);
            // a map piece lies on the crate at Kokos' feet (PropSurvival.PlaceMapPieces)
            PropInfo.SetSpot("camp_map_piece", kp + Vector3.up * 0.565f + kq * new Vector3(0.05f, 0f, 0.17f));
            batch.W(PropMats.Orange, kp).Box(body, new Vector3(0.36f, 0.4f, 0.24f), kq);
            Vector3 head = body + Vector3.up * 0.34f;
            batch.W(PropMats.Coconut, kp).Ico(head, new Vector3(0.16f, 0.18f, 0.16f), kq, 1);
            for (int s = -1; s <= 1; s += 2)
                batch.W(PropMats.White, kp).Octa(head + kq * new Vector3(s * 0.06f, 0.04f, 0.15f), 0.025f);
            batch.W(PropMats.FlowerRed, kp).Box(head + kq * new Vector3(0f, -0.06f, 0.155f), new Vector3(0.09f, 0.015f, 0.01f), kq);
        }

        // ------------------------------------------------------------------ goats + chickens (moved from the old village)
        static void Pens()
        {
            Vector3 gp = Polar(62f, 9.6f);
            Fence(gp, 4.2f, 3.4f, Face(gp, C));
            batch.W(PropMats.Thatch, gp).Box(gp + Vector3.up * 1.3f - F * 1.2f, new Vector3(1.8f, 0.1f, 1.4f), Quaternion.Euler(0f, Face(gp, C), 8f));
            batch.W(PropMats.Flat(LowPoly.Hex("#D9C27A")), gp).Box(gp + Vector3.up * 0.22f + Rt * 1.1f, new Vector3(0.9f, 0.45f, 0.6f), Quaternion.Euler(0f, 20f, 0f));
            PropInfo.SetSpot("goat_pen", gp);
            PropInfo.AddLandSpot(gp + Vector3.up * 0.1f, LandSpotKind.Pen, 1.8f, "goat_pen");
            PropSpots.Register("hermit_goats", Polar(58f, 7.0f), 0f);

            Vector3 cr = Polar(108f, 10.4f);
            Fence(cr, 2.6f, 2.0f, Face(cr, C));
            Quaternion q = Quaternion.Euler(0f, Face(cr, C), 0f);
            batch.W(PropMats.Driftwood, cr).Box(cr + q * new Vector3(0.6f, 0.45f, -0.5f), new Vector3(0.9f, 0.7f, 0.7f), q);
            batch.W(PropMats.Thatch, cr).Box(cr + q * new Vector3(0.6f, 0.86f, -0.5f), new Vector3(1.1f, 0.08f, 0.9f), q * Quaternion.Euler(10f, 0f, 0f));
            PropInfo.SetSpot("chicken_run", cr);
            PropInfo.AddLandSpot(cr + Vector3.up * 0.1f, LandSpotKind.Pen, 1.1f, "chicken_run");
            PropSpots.Register("hermit_chickens", Polar(108f, 8.2f), 0f);
        }

        static void Fence(Vector3 c, float w, float d, float yaw)
        {
            Quaternion q = Quaternion.Euler(0f, yaw, 0f);
            Vector3[] k = { c + q * new Vector3(-w * 0.5f, 0f, -d * 0.5f), c + q * new Vector3(w * 0.5f, 0f, -d * 0.5f), c + q * new Vector3(w * 0.5f, 0f, d * 0.5f), c + q * new Vector3(-w * 0.5f, 0f, d * 0.5f) };
            for (int i = 0; i < 4; i++)
            {
                Vector3 a = k[i], b = k[(i + 1) % 4];
                // a gap (gate) in the side facing the camp centre
                bool gate = i == 2;
                int n = Mathf.Max(2, Mathf.RoundToInt((b - a).magnitude / 1.1f));
                for (int j = 0; j <= n; j++)
                {
                    Vector3 p = Vector3.Lerp(a, b, j / (float)n);
                    p.y = G(p);
                    batch.W(PropMats.Driftwood, p).Rod(p - Vector3.up * 0.2f, p + Vector3.up * 0.95f, 0.05f, 0.045f, 5, true);
                }
                a.y = G(a); b.y = G(b);
                Vector3 seg = b - a; seg.y = 0f;
                float len = seg.magnitude;
                if (gate)
                {
                    Vector3 m = (a + b) * 0.5f;
                    Vector3 dir = seg / len;
                    Vector3 g0 = m - dir * 0.5f, g1 = m + dir * 0.5f;
                    g0.y = G(g0); g1.y = G(g1);
                    Rails(a, g0); Rails(g1, b);
                    // the gate leaf swung open
                    batch.W(PropMats.Wood2, g1).Box(g1 + Vector3.up * 0.5f + Quaternion.Euler(0f, 70f, 0f) * dir * 0.45f, new Vector3(0.06f, 0.7f, 0.9f), Quaternion.LookRotation(Quaternion.Euler(0f, 70f, 0f) * dir));
                    continue;
                }
                Rails(a, b);
            }
        }

        static void Rails(Vector3 a, Vector3 b)
        {
            for (int r = 0; r < 2; r++) batch.W(PropMats.Wood2, a).Beam(a + Vector3.up * (0.35f + r * 0.4f), b + Vector3.up * (0.35f + r * 0.4f), 0.05f, 0.07f);
            Vector3 seg = b - a; seg.y = 0f;
            float len = seg.magnitude;
            if (len < 0.2f) return;
            PropCol.Box(cols, (a + b) * 0.5f + Vector3.up * 0.5f, new Vector3(0.12f, 1.0f, len), Quaternion.LookRotation(seg / len), "PenFence");
        }

        // ------------------------------------------------------------------ rain catcher, garden
        static void RainCatcher()
        {
            Vector3 p = Polar(212f, 10.6f);
            PropKit.Barrel(batch, cols, p, PropMats.DrumBlue);
            var w = batch.W(PropMats.Driftwood, p);
            var tarp = batch.W(Cloth(1), p);
            Vector3 mouth = p + Vector3.up * 1.0f;
            Vector3[] corners = new Vector3[4];
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * 0.5f + 0.4f;
                Vector3 post = p + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 1.1f;
                post.y = G(post);
                w.Rod(post - Vector3.up * 0.2f, post + Vector3.up * 1.7f, 0.04f, 0.035f, 5, true);
                corners[i] = post + Vector3.up * 1.65f;
            }
            for (int i = 0; i < 4; i++) tarp.Tri2(corners[i], corners[(i + 1) % 4], mouth, Vector3.up);
            PropSpots.Register("hermit_rain", Polar(212f, 8.8f), 0f);
        }

        static void Garden(System.Random rng)
        {
            Vector3 g = Polar(262f, 10.4f);
            PropSpots.Register("hermit_garden", Polar(262f, 8.6f), 0f);
            Vector3 gard = Polar(258f, 8.9f);
            PropSpots.Register("hermit_role_gardener", gard, Face(gard, g));
            for (int i = 0; i < 7; i++)
            {
                Vector3 p = g + Rt * PropUtil.Range(rng, -1.3f, 1.3f) + F * PropUtil.Range(rng, -0.9f, 0.9f);
                p.y = G(p);
                PropJungle.AddPlant(under, under, rng, p, i % 3 == 0 ? 4 : 3, 0.8f);
            }
            // a low border of stones
            for (int i = 0; i < 12; i++)
            {
                float a = i / 12f * Mathf.PI * 2f;
                Vector3 s = g + (F * Mathf.Cos(a) * 1.8f + Rt * Mathf.Sin(a) * 1.4f);
                s.y = G(s) + 0.04f;
                PropRocks.Build(batch.W(PropMats.RockMid, s), s, new Vector3(0.18f, 0.12f, 0.15f), Quaternion.Euler(0f, i * 31f, 0f), 900 + i, 0, 0.18f);
            }
        }

        // ------------------------------------------------------------------ trap lines (tin-can rattles) on the rim
        static void TrapLines(System.Random rng)
        {
            float[] arcs = { 34f, 90f, 150f, 178f, 235f, 290f, 330f };
            for (int k = 0; k < arcs.Length - 1; k++)
            {
                if (k == 2) continue;   // keep the lookout notch free
                Vector3 prev = Vector3.zero;
                for (int i = 0; i <= 4; i++)
                {
                    float a = Mathf.Lerp(arcs[k], arcs[k + 1], i / 4f);
                    Vector3 p = Polar(a, 14.2f + 0.6f * Mathf.Sin(i * 1.7f + k));
                    batch.W(PropMats.Driftwood, p).Rod(p - Vector3.up * 0.15f, p + Vector3.up * 0.55f, 0.03f, 0.025f, 4, true);
                    Vector3 top = p + Vector3.up * 0.48f;
                    if (i > 0)
                    {
                        batch.W(PropMats.Rope, p).Rod(prev, top, 0.006f, 0.006f, 3);
                        Vector3 can = Vector3.Lerp(prev, top, 0.5f) - Vector3.up * 0.1f;
                        batch.W(PropMats.Metal, p).Cylinder(can - Vector3.up * 0.06f, Quaternion.Euler(PropUtil.Range(rng, -15f, 15f), 0f, PropUtil.Range(rng, -15f, 15f)), 0.035f, 0.035f, 0.1f, 6, true, false);
                    }
                    prev = top;
                }
            }
            // two spring snares on the game trail (bent sapling + noose) – decoration
            for (int i = 0; i < 2; i++)
            {
                Vector3 p = Vector3.Lerp(WorldFeatures.CampTrailStart, WorldFeatures.CampTrailBend, 0.35f + i * 0.4f) + Rt * (i == 0 ? 1.6f : -1.7f);
                p.y = G(p);
                Vector3 tip = p + Vector3.up * 1.1f + F * 0.6f;
                batch.W(PropMats.Stem, p).Rod(p, p + Vector3.up * 0.7f, 0.03f, 0.025f, 4);
                batch.W(PropMats.Stem, p).Rod(p + Vector3.up * 0.7f, tip, 0.025f, 0.015f, 4);
                batch.W(PropMats.Rope, p).Rod(tip, tip - Vector3.up * 0.75f, 0.005f, 0.005f, 3);
                batch.W(PropMats.Rope, p).Torus(tip - Vector3.up * 0.82f, Quaternion.identity, 0.1f, 0.006f, 8, 3);
            }
        }

        // ------------------------------------------------------------------ the sea lookout on the west rim
        static void Lookout()
        {
            Vector3 lk = WorldFeatures.CampLookout;
            lk.y = G(lk);
            Vector3 toSea = lk - C; toSea.y = 0f; toSea.Normalize();
            float yaw = Mathf.Atan2(toSea.x, toSea.z) * Mathf.Rad2Deg;
            Vector3 bench = lk - toSea * 0.6f;
            bench.y = G(bench);
            PropKit.Log(batch, cols, bench + Vector3.up * 0.2f, yaw, 1.8f, 0.2f, PropMats.Driftwood, true);
            PropKit.Seat(root, bench + Vector3.up * 0.42f, yaw, bench - toSea * 1.0f);
            // a spyglass on a driftwood tripod and an unlit signal pyre (they never stopped hoping for a ship)
            Vector3 sp = lk + Vector3.Cross(Vector3.up, toSea) * 1.4f;
            sp.y = G(sp);
            Vector3 head = sp + Vector3.up * 1.25f;
            for (int i = 0; i < 3; i++)
            {
                float a = i * 2.094f;
                batch.W(PropMats.Driftwood, sp).Rod(sp + new Vector3(Mathf.Cos(a) * 0.4f, 0f, Mathf.Sin(a) * 0.4f), head, 0.025f, 0.02f, 4);
            }
            batch.W(PropMats.Gold, sp).Cylinder(head - toSea * 0.3f, Quaternion.LookRotation(toSea) * Quaternion.Euler(80f, 0f, 0f), 0.035f, 0.05f, 0.75f, 8, true, true);
            PropCol.Capsule(cols, sp + Vector3.up * 0.6f, 0.25f, 1.3f, Quaternion.identity, "Spyglass");
            Vector3 pyre = lk - Vector3.Cross(Vector3.up, toSea) * 1.8f;
            pyre.y = G(pyre);
            for (int i = 0; i < 7; i++)
            {
                float a = i * 0.9f;
                Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                batch.W(PropMats.Driftwood, pyre).Rod(pyre + d * 0.55f, pyre + Vector3.up * 1.4f - d * 0.05f, 0.05f, 0.03f, 5, true);
            }
            PropCol.Capsule(cols, pyre + Vector3.up * 0.6f, 0.45f, 1.3f, Quaternion.identity, "SignalPyre");
            PropSpots.Register("hermit_lookout", lk - toSea * 1.6f, yaw);
            PropSpots.Register("hermit_role_lookout", lk + Vector3.Cross(Vector3.up, toSea) * 0.8f, yaw);
            PropInfo.SetSpot("camp_lookout", lk, yaw);
        }

        // ------------------------------------------------------------------ shrines to the sea (shells + driftwood)
        /// The hermits revere the sea: a shrine in the camp (stacked flat stones, a driftwood arch hung with shells and
        /// glass floats, a big conch, offering bowls, little lights) facing west toward the sea, and an altar of shells at
        /// the lookout above the cliff. Handmade, no real culture's symbols.
        static void SeaShrine()
        {
            Vector3 sea = WorldFeatures.CampLookout - C; sea.y = 0f; sea.Normalize();
            Vector3 lat = Vector3.Cross(Vector3.up, sea);
            Vector3 p = Polar(85f, 3.6f);
            Quaternion q = Quaternion.LookRotation(sea);
            // three stacked flat stones as the altar
            for (int k = 0; k < 3; k++)
            {
                Vector3 s = p + Vector3.up * (0.12f + k * 0.2f);
                float w = 1.25f - k * 0.25f;
                PropRocks.Build(batch.W(k == 1 ? PropMats.RockMid : PropMats.Rock, s), s, new Vector3(w * 0.5f, 0.11f, w * 0.32f), q * Quaternion.Euler(0f, k * 9f - 9f, 0f), 7300 + k, 0, 0.1f);
            }
            PropCol.Box(cols, p + Vector3.up * 0.33f, new Vector3(1.25f, 0.66f, 0.8f), q, "Shrine");
            // the driftwood arch behind it (on the land side), hung with shells and glass floats
            Vector3 back = p - sea * 0.55f;
            back.y = G(back);
            var dw = batch.W(PropMats.Driftwood, back);
            Vector3 l0 = back - lat * 0.85f, r0 = back + lat * 0.85f;
            l0.y = G(l0); r0.y = G(r0);
            Vector3 lt = l0 + Vector3.up * 1.9f, rtop = r0 + Vector3.up * 1.9f, apex = back + Vector3.up * 2.35f;
            dw.Rod(l0 - Vector3.up * 0.3f, lt, 0.07f, 0.055f, 6, true);
            dw.Rod(r0 - Vector3.up * 0.3f, rtop, 0.07f, 0.055f, 6, true);
            dw.Rod(lt, apex, 0.055f, 0.05f, 6, true);
            dw.Rod(rtop, apex, 0.055f, 0.05f, 6, true);
            PropCol.Capsule(cols, l0 + Vector3.up * 0.9f, 0.09f, 2.0f, Quaternion.identity, "ShrineArch");
            PropCol.Capsule(cols, r0 + Vector3.up * 0.9f, 0.09f, 2.0f, Quaternion.identity, "ShrineArch");
            for (int k = 0; k < 7; k++)
            {
                float t = (k + 0.5f) / 7f;
                Vector3 top = t < 0.5f ? Vector3.Lerp(lt, apex, t * 2f) : Vector3.Lerp(apex, rtop, t * 2f - 1f);
                float len = 0.25f + 0.2f * Mathf.Abs(Mathf.Sin(k * 1.7f));
                batch.W(PropMats.Rope, back).Rod(top, top - Vector3.up * len, 0.005f, 0.005f, 3);
                Vector3 h = top - Vector3.up * (len + 0.05f);
                if (k % 3 == 1) batch.W(PropMats.BottleGlass, back).Ico(h, Vector3.one * 0.08f, Quaternion.identity, 1);
                else batch.W(k % 2 == 0 ? PropMats.FlowerWhite : PropMats.FlowerPink, back).Ico(h, new Vector3(0.06f, 0.05f, 0.025f), q, 0);
            }
            // on the altar: a big conch, two offering bowls (a fish, fruit), little lights in shells
            Vector3 top0 = p + Vector3.up * 0.62f;
            batch.W(PropMats.FlowerPink, top0).Cylinder(top0 + lat * 0.05f, q * Quaternion.Euler(0f, 0f, 90f) * Quaternion.Euler(0f, 0f, 0f), 0.11f, 0.02f, 0.38f, 7, true, true);
            batch.W(PropMats.White, top0).Ico(top0 - lat * 0.08f + Vector3.up * 0.05f, new Vector3(0.12f, 0.09f, 0.1f), q, 1);
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 bowl = top0 + lat * (s * 0.38f) + sea * 0.08f;
                batch.W(PropMats.Coconut, bowl).Cylinder(bowl, Quaternion.identity, 0.12f, 0.08f, 0.07f, 8, true, false);
                if (s < 0) batch.W(PropMats.Flat(LowPoly.Hex("#8FB3C8")), bowl).Ico(bowl + Vector3.up * 0.08f, new Vector3(0.11f, 0.035f, 0.045f), q, 0);
                else batch.W(PropMats.FlowerOrange, bowl).Ico(bowl + Vector3.up * 0.08f, Vector3.one * 0.05f, Quaternion.identity, 0);
                batch.W(PropMats.FireGlow, bowl).Octa(bowl + sea * 0.18f + Vector3.up * 0.06f, 0.025f);
            }
            // a ring of white shells on the ground in front of it
            for (int k = 0; k < 14; k++)
            {
                float a = k / 14f * Mathf.PI * 2f;
                Vector3 s = p + sea * 1.1f + (sea * Mathf.Cos(a) * 0.6f + lat * Mathf.Sin(a) * 0.9f);
                s.y = G(s) + 0.02f;
                batch.W(k % 3 == 0 ? PropMats.FlowerPink : PropMats.FlowerWhite, s).Ico(s, new Vector3(0.05f, 0.025f, 0.04f), Quaternion.Euler(0f, k * 40f, 0f), 0);
            }
            PropSpots.Register("hermit_shrine", p + sea * 1.4f, Mathf.Atan2(-sea.x, -sea.z) * Mathf.Rad2Deg);

            // the sea altar at the lookout: a cairn of shells and stones with a driftwood frame, open toward the sea
            Vector3 lk = WorldFeatures.CampLookout;
            lk.y = G(lk);
            Vector3 al = lk + sea * 1.3f;
            al.y = G(al);
            for (int k = 0; k < 4; k++)
            {
                Vector3 s = al + Vector3.up * (0.1f + k * 0.16f);
                float w = 0.5f - k * 0.09f;
                PropRocks.Build(batch.W(PropMats.RockMid, s), s, new Vector3(w, 0.1f, w * 0.85f), Quaternion.Euler(0f, k * 33f, 0f), 7400 + k, 0, 0.12f);
            }
            batch.W(PropMats.FlowerWhite, al).Ico(al + Vector3.up * 0.75f, new Vector3(0.1f, 0.08f, 0.09f), q, 1);
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 pb = al + lat * (s * 0.55f);
                pb.y = G(pb);
                batch.W(PropMats.Driftwood, pb).Rod(pb - Vector3.up * 0.2f, pb + Vector3.up * 1.3f, 0.045f, 0.035f, 5, true);
            }
            batch.W(PropMats.Driftwood, al).Rod(al + lat * -0.55f + Vector3.up * 1.25f, al + lat * 0.55f + Vector3.up * 1.25f, 0.035f, 0.035f, 5);
            PropCol.Capsule(cols, al + Vector3.up * 0.4f, 0.5f, 0.9f, Quaternion.identity, "SeaAltar");
            PropSpots.Register("hermit_shrine_sea", al - sea * 1.3f, Mathf.Atan2(sea.x, sea.z) * Mathf.Rad2Deg);
        }

        // ------------------------------------------------------------------ hints along the game trail
        static void TrailMarks(System.Random rng)
        {
            Vector3 a = WorldFeatures.CampTrailStart, b = WorldFeatures.CampTrailBend, gap = WorldFeatures.CampTrailGap;
            Vector3[] cairns = { a + Rt * 1.3f + F * -0.4f, Vector3.Lerp(a, b, 0.55f) - Rt * 1.2f, b + Rt * 1.1f, Vector3.Lerp(b, gap, 0.6f) - Rt * 1.0f };
            for (int i = 0; i < cairns.Length; i++)
            {
                Vector3 p = cairns[i];
                p.y = G(p);
                float y = p.y;
                for (int k = 0; k < 3 + (i % 2); k++)
                {
                    float s = 0.22f - k * 0.045f;
                    Vector3 sp = new Vector3(p.x + PropUtil.Range(rng, -0.03f, 0.03f), y + s * 0.4f, p.z + PropUtil.Range(rng, -0.03f, 0.03f));
                    PropRocks.Build(batch.W(k % 2 == 0 ? PropMats.Rock : PropMats.RockMid, sp), sp, new Vector3(s, s * 0.55f, s * 0.9f), Quaternion.Euler(0f, k * 53f + i * 20f, 0f), 1200 + i * 7 + k, 0, 0.12f);
                    y += s * 0.85f;
                }
            }
            // a shell charm hanging from a stick at the start (the hermits' sign: "someone lives here")
            Vector3 st = a - Rt * 1.4f;
            st.y = G(st);
            batch.W(PropMats.Driftwood, st).Rod(st - Vector3.up * 0.2f, st + Vector3.up * 1.6f + F * 0.25f, 0.035f, 0.025f, 5, true);
            Vector3 hang = st + Vector3.up * 1.55f + F * 0.2f;
            for (int k = 0; k < 4; k++)
            {
                Vector3 h = hang + F * (k * 0.06f);
                float len = 0.25f + 0.08f * k;
                batch.W(PropMats.Rope, st).Rod(h, h - Vector3.up * len, 0.004f, 0.004f, 3);
                batch.W(k % 2 == 0 ? PropMats.FlowerWhite : PropMats.FlowerPink, st).Ico(h - Vector3.up * (len + 0.03f), new Vector3(0.045f, 0.035f, 0.02f), Quaternion.identity, 0);
            }
            PropSpots.Register("hermit_trail_start", new Vector3(a.x, G(a.x, a.z), a.z), 0f);
            PropSpots.Register("hermit_trail_bend", new Vector3(b.x, G(b.x, b.z), b.z), 0f);
            Vector3 g = gap - F * 1.5f;
            g.y = G(g);
            PropSpots.Register("hermit_gap", g, Mathf.Atan2(F.x, F.z) * Mathf.Rad2Deg);
        }

        // ------------------------------------------------------------------ a dense ring of jungle around the hollow
        static void RimJungle(System.Random rng, PropExclusion excl)
        {
            int trees = 0;
            for (int i = 0; i < 26; i++)
            {
                float a = i / 26f * 360f + PropUtil.Range(rng, -5f, 5f);
                // leave the trail gap and the lookout notch open-ish (a tree right in front of the gap would block it)
                float rel = Mathf.DeltaAngle(a, 0f);
                if (Mathf.Abs(rel) < 16f) continue;
                Vector3 lk = WorldFeatures.CampLookout - C; lk.y = 0f;
                float lkAng = Mathf.Atan2(-Vector3.Dot(lk, Rt), Vector3.Dot(lk, F)) * Mathf.Rad2Deg;
                if (Mathf.Abs(Mathf.DeltaAngle(a, lkAng)) < 12f) continue;
                float r = PropUtil.Range(rng, 15.5f, 21f);
                Vector3 p = Polar(a, r);
                if (p.y < 10f || PropGround.Slope(p.x, p.z, 1.5f) > 0.95f) continue;
                PropJungle.AddTree(full, lite, treeCols, rng, p, PropUtil.Range(rng, 0.95f, 1.2f));
                excl.Add(p.x, p.z, 2.4f);
                trees++;
                // undergrowth between the trunks: bushes, ferns, taro (passable)
                for (int k = 0; k < 3; k++)
                {
                    Vector3 q = Polar(a + PropUtil.Range(rng, -7f, 7f), r + PropUtil.Range(rng, -3.5f, 1.5f));
                    if (Vector2.Distance(new Vector2(q.x, q.z), new Vector2(C.x, C.z)) < WorldLayout.HermitCampRadius + 0.8f) continue;
                    PropJungle.AddPlant(under, under, rng, q, k == 0 ? 0 : k == 1 ? 2 : 3, PropUtil.Range(rng, 1.0f, 1.35f));
                }
            }
            // screen the trail start beside the grotto clearing (bushes – passable – and two trees)
            Vector3 s = WorldFeatures.CampTrailStart;
            for (int k = 0; k < 7; k++)
            {
                Vector3 q = s + F * PropUtil.Range(rng, -2.5f, -0.5f) + Rt * ((k % 2 == 0 ? 1f : -1f) * PropUtil.Range(rng, 1.2f, 2.6f)) - F * 1.5f;
                q.y = G(q);
                PropJungle.AddPlant(under, under, rng, q, k % 3 == 0 ? 1 : 0, PropUtil.Range(rng, 1.1f, 1.4f));
            }
            Debug.Log("Sealebrities props: hermit camp with " + trees + " rim trees.");
        }

        // ------------------------------------------------------------------ NPC anchors
        static void Spots()
        {
            PropSpots.Register("hermit_camp", C, Mathf.Atan2(F.x, F.z) * Mathf.Rad2Deg);
            PropInfo.SetSpot("hermit_fire_pit", C);
            Vector3 fire = PropInfo.Spot("hermit_fire", out var f) ? f : C;
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f + 45f;
                Vector3 p = fire + (F * Mathf.Cos(a * Mathf.Deg2Rad) - Rt * Mathf.Sin(a * Mathf.Deg2Rad)) * 1.9f;
                p.y = G(p);
                PropSpots.Register("hermit_fire_" + i, p, Face(p, fire));
            }
            float[] sa = { 20f, 75f, 130f, 185f, 240f, 300f };
            for (int i = 0; i < sa.Length; i++)
            {
                Vector3 p = Polar(sa[i], 4.6f + (i % 2) * 1.6f);
                PropSpots.Register("hermit_spawn_" + i, p, Face(p, C));
            }
            // fauna: a little meadow for the chickens to roam and perches on the totems
            PropInfo.AddLandSpot(C + Vector3.up * 0.1f, LandSpotKind.Meadow, 5f, "hermit_camp");
        }

        static void Discovery()
        {
            var go = new GameObject("CampDiscovery");
            go.transform.SetParent(root, false);
            go.transform.position = C;
            go.AddComponent<PropCampDiscovery>();
        }
    }

    /// Sets a story flag the first time the local player stands within `radius` (horizontal) of this object – the hermit
    /// camp (StoryKeys.HermitCampFound: the phone map shows it from then on) and the volcano island
    /// (StoryKeys.VolcanoLanded). StoryFlags.Set forwards to the host. Checks twice a second, no allocations.
    public class PropCampDiscovery : MonoBehaviour
    {
        public string flag = StoryKeys.HermitCampFound;
        public float radius = WorldLayout.HermitCampRadius + 1f;
        /// Only counts when the player is on land / in shallow water (not just floating past on the boat far below).
        public float minY = -1.5f;
        float next;

        void Update()
        {
            if (Time.time < next) return;
            next = Time.time + 0.5f;
            if (GameManager.I == null || !GameManager.I.InGame) return;
            if (StoryFlags.Has(flag)) { enabled = false; return; }
            var pl = Refs.Player;
            if (pl == null) return;
            var p = pl.Root;
            if (p == null) return;
            Vector3 d = p.position - transform.position;
            if (p.position.y < minY) return;
            d.y = 0f;
            if (d.sqrMagnitude < radius * radius)
            {
                StoryFlags.Set(flag);
                enabled = false;
            }
        }
    }
}