using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// L.8 – the village Altriff („Oldreef“): a long-established island community of fishers and boat builders (respect
    /// rules: ordinary homes, crafts and modern things side by side – no costumes, no "tribe" clichés).
    /// Terrace WorldLayout.Village (48 × 24 m at 1.7 m), the beach in front, the stilt lagoon south of it:
    ///  * the meeting house (open long house, woven roof, benches, the 2.5 m tide-calendar wheel, a solar panel and a
    ///    satellite dish – they watch your channel), the card table where Pearl beats Horst,
    ///  * the boat shed with two outrigger canoes under construction (secret 12 „Gus' Schuldscheine“ in the loft –
    ///    climb the canoe rack), Ada's kiosk, the music stage, the smokehouse, net-drying racks, gardens, the goat pen
    ///    and the chicken run, a weaving corner, the village grill, four terrace houses incl. the guesthouse,
    ///  * six stilt houses on a boardwalk over the lagoon, the village jetty with lanterns (second boat landing point),
    ///  * village beach east: the kids' football goal, canoe racks, drying nets.
    /// Publishes every NPC work / evening spot to QuestSpots (v_*), the berth (village_mooring) and the land-fauna spots.
    public static class PropVillage
    {
        static float G(float x, float z) => PropGround.H(x, z);
        static float G(Vector3 p) => PropGround.H(p.x, p.z);

        static Vector3 V;                 // terrace centre
        static PropBatch batch;
        static Transform cols, root;

        /// Local (x east, z north/inland) → world, on the ground.
        static Vector3 L(float x, float z) { var p = new Vector3(V.x + x, 0f, V.z + z); p.y = G(p); return p; }
        static Vector3 L(float x, float y, float z) => new Vector3(V.x + x, y, V.z + z);

        static Material Wood => PropMats.Wood;
        static Material WoodDark => PropMats.WoodDark;
        static Material Roof => PropMats.Thatch;
        static Material RoofDark => PropMats.ThatchDark;
        static Material Teal => PropMats.Flat(LowPoly.Hex("#3FA9A0"));
        static Material OrangeC => PropMats.Flat(LowPoly.Hex("#EE7F2D"));
        static Material Yellow => PropMats.Flat(LowPoly.Hex("#F2C94C"));

        public static IEnumerator Build(Transform island, PropExclusion excl)
        {
            V = WorldLayout.Village;
            root = PropUtil.WorldRoot("Village", island);
            cols = new GameObject("Colliders").transform;
            cols.SetParent(root, false);
            batch = new PropBatch(0f);
            var rng = new System.Random(8484);

            if (!BuildHouses)
            {
                // round 3 (GDD §19 "Die Dorf-Häuser entfernen"): no village any more – only the old wooden jetty (second
                // boat landing, the diving helmet lies beside it) and a few canoes / a net rack on the open south beach.
                // The villagers' work spots fall back to QuestSpots' computed beach positions; the goats and chickens live
                // in the hermit camp now (PropCamp). The NPC step relocates / repurposes the villagers.
                BeachRemnants(rng);
                if (PropRun.Due()) yield return null;
                Jetty();
                Spots();
                excl.AddLine(L(6f, -10f), L(6f, -30f), 3f);
                excl.Add(V.x + 2f, V.z - 18f, 4f);
                batch.Flush(root, "SouthBeachMesh", ShadowCastingMode.On, 600f);
                batch = null;
                yield break;
            }

            MeetingHouse();
            if (PropRun.Due()) yield return null;
            BoatShed();
            Kiosk();
            Stage();
            if (PropRun.Due()) yield return null;
            TerraceHouses(rng);
            Crafts(rng);
            if (PropRun.Due()) yield return null;
            PensAndGardens(rng);
            Beach(rng);
            if (PropRun.Due()) yield return null;
            Jetty();
            StiltHouses(rng);
            if (PropRun.Due()) yield return null;
            Spots();

            // keep the jungle / beach clutter off the terrace, the beach and the boardwalk
            for (float x = -26f; x <= 26f; x += 6f)
                for (float z = -14f; z <= 12f; z += 6f)
                    if ((x * x) / (27f * 27f) + (z * z) / (15f * 15f) < 1f) excl.Add(V.x + x, V.z + z, 5f);
            excl.AddLine(L(6f, -12f), L(6f, -30f), 3f);
            excl.AddLine(L(-14f, -20f), L(-14f, -38f), 8f);
            excl.Add(V.x + 18f, V.z - 10f, 6f);

            batch.Flush(root, "VillageMesh", ShadowCastingMode.On, 600f);
            batch = null;
        }

        /// Round 3: false = the village houses are gone (GDD §19). The old builders stay for reference / a later return.
        public const bool BuildHouses = false;

        /// Round 3: what is left of the village beach – two canoes pulled up on the sand and a net rack by the jetty.
        static void BeachRemnants(System.Random rng)
        {
            PropKit.NetRack(batch, cols, L(-6f, -12.5f), 8f, 2.6f);
            for (int i = 0; i < 2; i++)
            {
                Vector3 cp = L(-1f + i * 4.2f, -17.5f);
                if (cp.y < 0.2f) continue;
                PropKit.Canoe(batch, Matrix4x4.TRS(cp + Vector3.up * 0.15f, Quaternion.Euler(0f, 12f - i * 24f, 0f), Vector3.one), i == 0 ? OrangeC : Teal, PropMats.Wood3);
                PropCol.Box(cols, cp + Vector3.up * 0.3f, new Vector3(0.8f, 0.6f, 5.2f), Quaternion.Euler(0f, 12f - i * 24f, 0f), "Canoe");
            }
        }

        // ------------------------------------------------------------------ building blocks
        /// Woven-wall house: floor, walls with a door (front = +z of yaw … here the front faces `front`), two window
        /// gaps, a steep thatch gable roof with an overhang, a small porch with a bench. Colliders for walls + roof.
        static void House(Vector3 c, float yaw, float w, float d, float floorY, int variant, bool porchBench = true)
        {
            Quaternion q = Quaternion.Euler(0f, yaw, 0f);
            Vector3 P(float x, float y, float z) => new Vector3(c.x, floorY, c.z) + q * new Vector3(x, y, z);
            const float wallH = 2.4f;
            var wallMat = variant % 3 == 0 ? PropMats.Flat(LowPoly.Hex("#B89A6E")) : variant % 3 == 1 ? PropMats.Flat(LowPoly.Hex("#A8865E")) : PropMats.Flat(LowPoly.Hex("#C2A57A"));
            var frame = WoodDark;
            // floor platform
            batch.W(Wood, c).Box(P(0f, -0.1f, 0f), new Vector3(w + 0.3f, 0.2f, d + 0.3f), q);
            PropCol.Box(cols, P(0f, -0.1f, 0f), new Vector3(w + 0.3f, 0.2f, d + 0.3f), q, "HouseFloor");
            // walls: back, left, right full; front with a door gap (1.0 m) in the middle
            batch.W(wallMat, c).Box(P(0f, wallH * 0.5f, -d * 0.5f), new Vector3(w, wallH, 0.12f), q);
            batch.W(wallMat, c).Box(P(-w * 0.5f, wallH * 0.5f, 0f), new Vector3(0.12f, wallH, d), q);
            batch.W(wallMat, c).Box(P(w * 0.5f, wallH * 0.5f, 0f), new Vector3(0.12f, wallH, d), q);
            float side = (w - 1.0f) * 0.5f;
            batch.W(wallMat, c).Box(P(-(0.5f + side * 0.5f), wallH * 0.5f, d * 0.5f), new Vector3(side, wallH, 0.12f), q);
            batch.W(wallMat, c).Box(P(0.5f + side * 0.5f, wallH * 0.5f, d * 0.5f), new Vector3(side, wallH, 0.12f), q);
            batch.W(wallMat, c).Box(P(0f, wallH - 0.2f, d * 0.5f), new Vector3(1.0f, 0.4f, 0.12f), q);
            // a closed door leaf in a teal / orange colour, frame posts at the corners, windows with shutters
            batch.W(variant % 2 == 0 ? Teal : OrangeC, c).Box(P(0f, 1.0f, d * 0.5f + 0.04f), new Vector3(0.96f, 2.0f, 0.06f), q);
            for (int sx = -1; sx <= 1; sx += 2) for (int sz = -1; sz <= 1; sz += 2)
                    batch.W(frame, c).Box(P(sx * w * 0.5f, wallH * 0.5f, sz * d * 0.5f), new Vector3(0.16f, wallH + 0.1f, 0.16f), q);
            for (int s = -1; s <= 1; s += 2)
            {
                batch.W(PropMats.Ink, c).Box(P(s * w * 0.5f, 1.45f, 0f), new Vector3(0.14f, 0.6f, 0.7f), q);
                batch.W(variant % 2 == 0 ? Yellow : Teal, c).Box(P(s * (w * 0.5f + 0.08f), 1.45f, 0.5f), new Vector3(0.04f, 0.64f, 0.34f), q * Quaternion.Euler(0f, s * 20f, 0f));
            }
            // wave-patterned cloth band under the eaves (village colours)
            var cloth = PropMats.WaveCloth(variant);
            Vector3 cb = P(0f, wallH - 0.15f, d * 0.5f + 0.07f);
            Vector3 cx = q * Vector3.right * (w * 0.5f), cy = Vector3.up * 0.15f;
            batch.W(cloth, c).QuadAuto(cb - cx - cy, cb - cx + cy, cb + cx + cy, cb + cx - cy, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(w / 1.2f, 1f), new Vector2(w / 1.2f, 0f), q * Vector3.forward);
            PropCol.Box(cols, P(0f, wallH * 0.5f, 0f), new Vector3(w, wallH, d), q, "HouseBody");
            // steep thatch roof (gable along x), overhang 0.5 m
            float rh = 1.7f, ov = 0.5f;
            Vector3 ridgeA = P(-w * 0.5f - ov, wallH + rh, 0f), ridgeB = P(w * 0.5f + ov, wallH + rh, 0f);
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 eA = P(-w * 0.5f - ov, wallH - 0.25f, s * (d * 0.5f + ov)), eB = P(w * 0.5f + ov, wallH - 0.25f, s * (d * 0.5f + ov));
                var rb = batch.W(s > 0 ? Roof : RoofDark, c);
                Vector3 n = Vector3.Cross(eB - eA, ridgeA - eA) * -s;
                rb.QuadAuto(eA, eB, ridgeB, ridgeA, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, q * new Vector3(0f, 1f, s));
                // thickness edge
                rb.QuadAuto(eA, eB, eB - Vector3.up * 0.12f, eA - Vector3.up * 0.12f, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, q * new Vector3(0f, 0f, s));
                rb.QuadAuto(eA - Vector3.up * 0.12f, eB - Vector3.up * 0.12f, ridgeB - Vector3.up * 0.12f, ridgeA - Vector3.up * 0.12f, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, q * new Vector3(0f, -1f, -s * 0.2f));
            }
            for (int s = -1; s <= 1; s += 2)
            {
                // gable triangles
                Vector3 g0 = P(s * w * 0.5f, wallH, -d * 0.5f), g1 = P(s * w * 0.5f, wallH, d * 0.5f), g2 = P(s * w * 0.5f, wallH + rh - 0.3f, 0f);
                batch.W(wallMat, c).Face3(g0, g1, g2, q * new Vector3(s, 0f, 0f));
            }
            batch.W(RoofDark, c).Beam(ridgeA + Vector3.up * 0.05f, ridgeB + Vector3.up * 0.05f, 0.18f, 0.12f);
            PropCol.Box(cols, P(0f, wallH + rh * 0.45f, 0f), new Vector3(w + 0.6f, rh * 0.9f, d * 0.8f), q, "HouseRoof");
            // porch: steps + a bench by the door
            if (porchBench)
            {
                Vector3 bench = P(-(w * 0.5f - 0.9f), 0f, d * 0.5f + 0.55f);
                bench.y = floorY;
                if (floorY - G(bench) < 0.4f) PropKit.Bench(batch, cols, root, bench, yaw, true);
            }
            float drop = floorY - G(P(0f, 0f, d * 0.5f + 0.6f));
            for (int i = 0; i < 4 && drop > 0.15f && drop < 1.2f; i++)   // (no steps over the water – stilt houses)
            {
                float sy = floorY - (i + 1) * 0.2f;
                Vector3 sp = P(0f, 0f, d * 0.5f + 0.35f + i * 0.3f);
                float g = G(sp);
                if (sy < g - 0.05f) break;
                float hh = sy - (g - 0.2f);
                batch.W(Wood, c).Box(new Vector3(sp.x, sy - hh * 0.5f, sp.z), new Vector3(1.2f, hh, 0.3f), q);
                PropCol.Box(cols, new Vector3(sp.x, sy - hh * 0.5f, sp.z), new Vector3(1.2f, hh, 0.3f), q, "Step");
            }
            PropInfo.AddLandSpot(P(w * 0.5f + 0.07f, 1.2f, 0f), LandSpotKind.Wall, 0.6f, "village", q * Vector3.right);
            PropInfo.AddLandSpot(ridgeA * 0.5f + ridgeB * 0.5f + Vector3.up * 0.2f, LandSpotKind.Perch, 1f, "village");
        }

        static void Posts(Vector3 top, float bottomY, float r = 0.12f)
        {
            batch.W(WoodDark, top).Cylinder(new Vector3(top.x, bottomY, top.z), Quaternion.identity, r, r * 0.9f, top.y - bottomY, 6, false, true);
            // round 3: piles are solid (swimmers bump into them under the jetty)
            float len = top.y - bottomY;
            if (len > 0.5f && cols != null) PropCol.Capsule(cols, new Vector3(top.x, (top.y + bottomY) * 0.5f, top.z), r + 0.02f, len, Quaternion.identity, "Pile");
        }

        // ------------------------------------------------------------------ meeting house + tide calendar
        static void MeetingHouse()
        {
            Vector3 c = L(0f, 5.8f);
            float fy = c.y + 0.25f;
            const float W = 11f, D = 6.5f, H = 2.9f;
            // raised wooden floor
            batch.W(Wood, c).Box(new Vector3(c.x, fy - 0.12f, c.z), new Vector3(W, 0.24f, D), Quaternion.identity);
            PropCol.Box(cols, new Vector3(c.x, fy - 0.12f, c.z), new Vector3(W, 0.24f, D), Quaternion.identity, "MeetingFloor");
            // 10 posts
            for (int i = 0; i < 5; i++)
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 p = new Vector3(c.x - W * 0.5f + 0.3f + i * (W - 0.6f) / 4f, fy + H, c.z + s * (D * 0.5f - 0.3f));
                    Posts(p, fy - 0.3f, 0.14f);
                    PropCol.Capsule(cols, new Vector3(p.x, fy + H * 0.5f, p.z), 0.15f, H, Quaternion.identity, "MeetingPost");
                }
            // long hip roof of woven palm (two slopes + hip ends)
            float eave = fy + H - 0.1f, ridgeY = fy + H + 2.2f;
            Vector3 ra = new Vector3(c.x - W * 0.3f, ridgeY, c.z), rb = new Vector3(c.x + W * 0.3f, ridgeY, c.z);
            Vector3 e00 = new Vector3(c.x - W * 0.5f - 0.8f, eave, c.z - D * 0.5f - 0.8f), e10 = new Vector3(c.x + W * 0.5f + 0.8f, eave, c.z - D * 0.5f - 0.8f);
            Vector3 e01 = new Vector3(c.x - W * 0.5f - 0.8f, eave, c.z + D * 0.5f + 0.8f), e11 = new Vector3(c.x + W * 0.5f + 0.8f, eave, c.z + D * 0.5f + 0.8f);
            var rf = batch.W(Roof, c);
            rf.QuadAuto(e00, e10, rb, ra, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, new Vector3(0f, 1f, -1f));
            rf.QuadAuto(e11, e01, ra, rb, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, new Vector3(0f, 1f, 1f));
            batch.W(RoofDark, c).Face3(e01, e00, ra, Vector3.left + Vector3.up);
            batch.W(RoofDark, c).Face3(e10, e11, rb, Vector3.right + Vector3.up);
            // underside (seen from inside)
            var ru = batch.W(RoofDark, c);
            ru.QuadAuto(e00, e10, rb, ra, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, new Vector3(0f, -1f, 1f));
            ru.QuadAuto(e11, e01, ra, rb, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, new Vector3(0f, -1f, -1f));
            batch.W(WoodDark, c).Beam(ra, rb, 0.2f, 0.2f);
            PropCol.Box(cols, new Vector3(c.x, (eave + ridgeY) * 0.5f, c.z), new Vector3(W + 1.2f, ridgeY - eave, D * 0.7f), Quaternion.identity, "MeetingRoof");
            // fringe of the thatch
            for (float x = -W * 0.5f - 0.6f; x <= W * 0.5f + 0.6f; x += 0.4f)
                for (int s = -1; s <= 1; s += 2)
                    batch.W(RoofDark, c).Box(new Vector3(c.x + x, eave - 0.12f, c.z + s * (D * 0.5f + 0.8f)), new Vector3(0.32f, 0.22f, 0.06f), Quaternion.Euler(0f, 0f, (x * 7f) % 10f - 5f));
            // benches along three sides + the card table (Pearl vs Horst) near the front
            PropKit.Bench(batch, cols, root, new Vector3(c.x - 3.2f, fy, c.z + 1.6f), 180f, true);
            PropKit.Bench(batch, cols, root, new Vector3(c.x + 3.2f, fy, c.z + 1.6f), 180f, true);
            PropKit.Bench(batch, cols, root, new Vector3(c.x - 4.6f, fy, c.z - 1.2f), 90f, true);
            PropKit.Bench(batch, cols, root, new Vector3(c.x + 4.6f, fy, c.z - 1.2f), -90f, true);
            Vector3 ct = new Vector3(c.x, fy, c.z - 1.4f);
            PropKit.Table(batch, cols, ct, 0f, 1.0f, 0.8f, PropMats.Wood3);
            for (int i = 0; i < 6; i++) batch.W(PropMats.White, ct).Box(ct + new Vector3(-0.3f + i * 0.11f, 0.78f, 0.05f * (i % 2)), new Vector3(0.07f, 0.005f, 0.1f), Quaternion.Euler(0f, i * 25f, 0f));
            PropKit.Stool(batch, ct + new Vector3(-0.85f, 0f, 0f));
            PropKit.Stool(batch, ct + new Vector3(0.85f, 0f, 0f));
            // the tide calendar wheel (2.5 m) on a board at the back: moon phases, fish seasons, festival days
            Vector3 wc = new Vector3(c.x + 2.5f, fy + 1.6f, c.z + D * 0.5f - 0.15f);
            Quaternion wq = Quaternion.Euler(-90f, 0f, 0f);
            batch.W(WoodDark, wc).Box(wc + new Vector3(0f, 0f, 0.08f), new Vector3(2.9f, 2.9f, 0.1f), Quaternion.identity);
            batch.W(PropMats.Wood3, wc).Cylinder(wc - new Vector3(0f, 0f, 0.0f), Quaternion.Euler(90f, 0f, 0f), 1.25f, 1.25f, 0.08f, 24, true, true);
            Material[] seg = { Teal, OrangeC, Yellow, PropMats.Flat(LowPoly.Hex("#8E6C50")) };
            for (int i = 0; i < 12; i++)
            {
                float a0 = i * Mathf.PI * 2f / 12f, a1 = (i + 1) * Mathf.PI * 2f / 12f;
                Vector3 p0 = wc + new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f) * 1.18f - Vector3.forward * 0.045f;
                Vector3 p1 = wc + new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f) * 1.18f - Vector3.forward * 0.045f;
                Vector3 q0 = wc + new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f) * 0.85f - Vector3.forward * 0.045f;
                Vector3 q1 = wc + new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f) * 0.85f - Vector3.forward * 0.045f;
                batch.W(seg[i % 4], wc).QuadAuto(q0, p0, p1, q1, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector3.back);
                // moon phase dots on the inner ring
                Vector3 m = wc + new Vector3(Mathf.Cos((a0 + a1) * 0.5f), Mathf.Sin((a0 + a1) * 0.5f), 0f) * 0.62f - Vector3.forward * 0.05f;
                batch.W(i % 3 == 0 ? PropMats.White : PropMats.Ink, wc).Cylinder(m, Quaternion.Euler(90f, 0f, 0f), 0.09f, 0.09f, 0.02f, 8, true, true);
            }
            // the Night of the Eye: one segment marked with a painted eye
            Vector3 eye = wc + new Vector3(0f, 0.3f, 0f) - Vector3.forward * 0.06f;
            batch.W(PropMats.White, wc).Cylinder(eye, Quaternion.Euler(90f, 0f, 0f), 0.22f, 0.22f, 0.02f, 10, true, true);
            batch.W(PropMats.Flat(LowPoly.Hex("#2F6FD6")), wc).Cylinder(eye - Vector3.forward * 0.01f, Quaternion.Euler(90f, 0f, 0f), 0.1f, 0.1f, 0.02f, 8, true, true);
            batch.W(WoodDark, wc).Cylinder(wc - Vector3.forward * 0.07f, Quaternion.Euler(90f, 0f, 0f), 0.14f, 0.14f, 0.04f, 8, true, true);
            PropInfo.SetSpot("tide_calendar", new Vector3(wc.x, fy, wc.z - 1.2f), 0f);
            PropInfo.SetSpot("tide_calendar_page", new Vector3(c.x + 4.4f, fy + 0.02f, c.z + D * 0.5f - 0.6f));
            // modern things: a solar panel on the roof, a satellite dish on a post
            Vector3 sp = new Vector3(c.x - 2.8f, (eave + ridgeY) * 0.5f + 0.2f, c.z - 1.3f);
            batch.W(PropMats.Flat(LowPoly.Hex("#2E3E5C")), sp).Box(sp, new Vector3(2.2f, 0.06f, 1.2f), Quaternion.Euler(-28f, 0f, 0f));
            for (int i = 0; i < 3; i++) batch.W(PropMats.Metal, sp).Box(sp + new Vector3(-0.73f + i * 0.73f, 0.04f, 0f), new Vector3(0.03f, 0.02f, 1.22f), Quaternion.Euler(-28f, 0f, 0f));
            Vector3 dish = L(W * 0.5f + 1.4f, 5.8f + D * 0.5f);
            batch.W(PropMats.DarkMetal, dish).Rod(dish, dish + Vector3.up * 2.6f, 0.05f, 0.04f, 6);
            batch.W(PropMats.White, dish).Cylinder(dish + Vector3.up * 2.6f, Quaternion.Euler(-50f, 200f, 0f), 0.55f, 0.12f, 0.22f, 10, true, false);
            PropCol.Capsule(cols, dish + Vector3.up * 1.3f, 0.08f, 2.6f, Quaternion.identity, "DishPost");
            // lantern string along the front
            PropKit.LightString(batch, new Vector3(c.x - W * 0.5f, eave - 0.2f, c.z - D * 0.5f - 0.5f), new Vector3(c.x + W * 0.5f, eave - 0.2f, c.z - D * 0.5f - 0.5f), 0.35f, PropMats.LanternGlow);
            PropInfo.SetSpot("meeting_house", new Vector3(c.x, fy, c.z - D * 0.5f + 0.8f), 180f);
            PropInfo.AddLandSpot(new Vector3(c.x, ridgeY + 0.2f, c.z), LandSpotKind.Perch, 3f, "village");
            PropSpots.Register("v_meeting", new Vector3(c.x, fy, c.z - 0.2f));
            PropSpots.Register("v_pearl", ct + new Vector3(-0.85f, 0f, 0f));
            PropSpots.Register("v_cards_h", ct + new Vector3(0.85f, 0f, 0f));
            PropSpots.Register("v_calendar", new Vector3(wc.x, fy, wc.z - 1.1f));
            PropSpots.Register("v_eve1", new Vector3(c.x - 2.2f, fy, c.z - 2.4f));
        }

        // ------------------------------------------------------------------ boat shed (+ the loft secret)
        static void BoatShed()
        {
            Vector3 c = L(-16f, -1f);
            float fy = c.y;
            const float W = 9f, D = 6f, H = 3.4f;
            Quaternion q = Quaternion.identity;
            // back + side walls (open front to the beach), roof
            var wall = PropMats.Flat(LowPoly.Hex("#A8865E"));
            batch.W(wall, c).Box(new Vector3(c.x, fy + H * 0.5f, c.z + D * 0.5f), new Vector3(W, H, 0.14f), q);
            batch.W(wall, c).Box(new Vector3(c.x - W * 0.5f, fy + H * 0.5f, c.z), new Vector3(0.14f, H, D), q);
            batch.W(wall, c).Box(new Vector3(c.x + W * 0.5f, fy + H * 0.5f, c.z), new Vector3(0.14f, H, D), q);
            PropCol.Box(cols, new Vector3(c.x, fy + H * 0.5f, c.z + D * 0.5f), new Vector3(W, H, 0.2f), q, "ShedWall");
            PropCol.Box(cols, new Vector3(c.x - W * 0.5f, fy + H * 0.5f, c.z), new Vector3(0.2f, H, D), q, "ShedWall");
            PropCol.Box(cols, new Vector3(c.x + W * 0.5f, fy + H * 0.5f, c.z), new Vector3(0.2f, H, D), q, "ShedWall");
            for (int s = -1; s <= 1; s += 2) Posts(new Vector3(c.x + s * (W * 0.5f - 0.1f), fy + H, c.z - D * 0.5f + 0.1f), fy - 0.3f, 0.15f);
            Vector3 r0 = new Vector3(c.x, fy + H + 1.6f, c.z), e0 = new Vector3(c.x, fy + H - 0.1f, c.z - D * 0.5f - 0.7f), e1 = new Vector3(c.x, fy + H - 0.1f, c.z + D * 0.5f + 0.4f);
            var rf = batch.W(Roof, c);
            Vector3 hx = Vector3.right * (W * 0.5f + 0.5f);
            rf.QuadAuto(e0 - hx, e0 + hx, r0 + hx, r0 - hx, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, new Vector3(0f, 1f, -1f));
            rf.QuadAuto(e1 + hx, e1 - hx, r0 - hx, r0 + hx, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, new Vector3(0f, 1f, 1f));
            var ru = batch.W(RoofDark, c);
            ru.QuadAuto(e0 - hx, e0 + hx, r0 + hx, r0 - hx, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, new Vector3(0f, -1f, 1f));
            ru.QuadAuto(e1 + hx, e1 - hx, r0 - hx, r0 + hx, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, new Vector3(0f, -1f, -1f));
            for (int s = -1; s <= 1; s += 2) batch.W(wall, c).Face3(new Vector3(c.x + s * W * 0.5f, fy + H, c.z - D * 0.5f), new Vector3(c.x + s * W * 0.5f, fy + H, c.z + D * 0.5f), new Vector3(c.x + s * W * 0.5f, fy + H + 1.5f, c.z), new Vector3(s, 0f, 0f));
            PropCol.Box(cols, new Vector3(c.x, fy + H + 0.8f, c.z), new Vector3(W + 1f, 1.6f, D * 0.8f), q, "ShedRoof");
            // rafters + the loft (planks over the back half at 3.0 m)
            for (int i = 0; i < 5; i++) batch.W(WoodDark, c).Box(new Vector3(c.x - W * 0.5f + 0.4f + i * (W - 0.8f) / 4f, fy + H - 0.05f, c.z), new Vector3(0.14f, 0.16f, D), q);
            float loftY = fy + 3.0f;
            batch.W(Wood, c).Box(new Vector3(c.x - 1.8f, loftY, c.z + 1.6f), new Vector3(5.2f, 0.08f, 2.6f), q);
            PropCol.Box(cols, new Vector3(c.x - 1.8f, loftY - 0.02f, c.z + 1.6f), new Vector3(5.2f, 0.12f, 2.6f), q, "Loft");
            // the cigar box with Gus' IOUs in the rafters (secret 12)
            Vector3 box = new Vector3(c.x - 3.6f, loftY + 0.06f, c.z + 2.3f);
            batch.W(PropMats.Flat(LowPoly.Hex("#8E5A3A")), box).Box(box + Vector3.up * 0.05f, new Vector3(0.3f, 0.1f, 0.2f), Quaternion.Euler(0f, 18f, 0f));
            PropInfo.SetSpot("village_loft", box + Vector3.up * 0.05f);
            // the canoe rack along the west wall = the way up (rungs every 0.3 m → PropLadder)
            Vector3 rackFoot = new Vector3(c.x - W * 0.5f + 0.45f, fy, c.z + 0.6f);
            var rack = batch.W(WoodDark, c);
            for (int s = -1; s <= 1; s += 2) rack.Box(rackFoot + new Vector3(0f, 1.6f, s * 0.35f), new Vector3(0.1f, 3.2f, 0.1f), q);
            for (float y = 0.3f; y < 3.1f; y += 0.3f) rack.Box(rackFoot + new Vector3(0f, y, 0f), new Vector3(0.08f, 0.06f, 0.8f), q);
            for (int k = 0; k < 2; k++)
                PropKit.Canoe(batch, Matrix4x4.TRS(rackFoot + new Vector3(0.25f, 1.0f + k * 1.1f, 1.6f), Quaternion.Euler(0f, 0f, 90f) * Quaternion.Euler(0f, 0f, 0f), Vector3.one * 0.8f), k == 0 ? Teal : OrangeC, PropMats.Wood3);
            var lad = PropLadder.Create(root, "BoatShedRack", rackFoot, loftY + 0.9f, new Vector3(c.x - W * 0.5f + 1.3f, loftY + 0.06f, c.z + 1.4f), Vector3.right, LadderInfo.Wood);
            lad.bottomStand = rackFoot + Vector3.right * 0.8f + Vector3.up * 0.05f;
            lad.proximityTop = true;
            lad.proximityBottom = true;
            // two outrigger canoes under construction on trestles + planks, a tool table, wood shavings
            for (int k = 0; k < 2; k++)
            {
                Vector3 cc = new Vector3(c.x + 1.0f, fy + 0.75f, c.z - 1.6f + k * 2.2f);
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 t = cc + new Vector3(s * 1.6f, -0.75f, 0f);
                    batch.W(WoodDark, t).Box(t + Vector3.up * 0.35f, new Vector3(0.12f, 0.7f, 0.9f), q);
                }
                PropKit.Canoe(batch, Matrix4x4.TRS(cc, Quaternion.Euler(0f, 90f, 0f), Vector3.one * (k == 0 ? 1f : 0.95f)), k == 0 ? PropMats.Wood3 : PropMats.WoodLight, PropMats.WoodDark);
                PropCol.Box(cols, cc, new Vector3(4.2f, 0.6f, 0.8f), q, "CanoeBuild");
            }
            PropKit.Table(batch, cols, new Vector3(c.x + 3.4f, fy, c.z + 2.2f), 90f, 1.6f, 0.7f, PropMats.Wood3);
            for (int i = 0; i < 6; i++) batch.W(PropMats.WoodLight, c).Box(new Vector3(c.x + 3.2f - i * 0.1f, fy + 0.05f + i * 0.06f, c.z - 2.2f), new Vector3(0.18f, 0.05f, 2.4f), Quaternion.Euler(0f, i * 3f, 0f));
            PropKit.Sign(batch, cols, new Vector3(c.x + 2.4f, fy, c.z - D * 0.5f - 0.9f), 0f, "KIELHOLZ|BOOTSBAU", LowPoly.Hex("#3FA9A0"), LowPoly.Hex("#EDE6DA"), 1.4f, 0.5f);
            PropSpots.Register("v_boatshed", new Vector3(c.x + 1.2f, fy, c.z - D * 0.5f - 1.4f));
            PropSpots.Register("v_mira", new Vector3(c.x + 1.0f, fy, c.z - 0.4f));
            PropInfo.SetSpot("boat_shed", new Vector3(c.x, fy, c.z - D * 0.5f - 1f), 180f);
        }

        // ------------------------------------------------------------------ Ada's kiosk
        static void Kiosk()
        {
            Vector3 c = L(12f, 2.4f);
            float fy = c.y;
            Quaternion q = Quaternion.identity;
            const float W = 3.2f, D = 2.4f;
            var wall = PropMats.Flat(LowPoly.Hex("#C2A57A"));
            batch.W(wall, c).Box(new Vector3(c.x, fy + 1.2f, c.z + D * 0.5f), new Vector3(W, 2.4f, 0.12f), q);
            for (int s = -1; s <= 1; s += 2) batch.W(wall, c).Box(new Vector3(c.x + s * W * 0.5f, fy + 1.2f, c.z), new Vector3(0.12f, 2.4f, D), q);
            // counter (top at 0.93 m, its front at z −1.4 local = the v_kiosk line)
            Vector3 counter = new Vector3(c.x, fy + 0.46f, c.z - D * 0.5f + 0.25f);
            batch.W(OrangeC, c).Box(counter, new Vector3(W, 0.92f, 0.5f), q);
            batch.W(PropMats.Wood3, c).Box(counter + new Vector3(0f, 0.48f, -0.05f), new Vector3(W + 0.1f, 0.06f, 0.65f), q);
            PropCol.Box(cols, new Vector3(c.x, fy + 1.2f, c.z), new Vector3(W, 2.4f, D), q, "Kiosk");
            // awning + sign + a menu board + jars
            Vector3 aw = new Vector3(c.x, fy + 2.3f, c.z - D * 0.5f - 0.6f);
            batch.W(PropMats.WaveCloth(1), c).QuadAuto(aw + new Vector3(-W * 0.5f - 0.2f, -0.35f, -0.5f), aw + new Vector3(-W * 0.5f - 0.2f, 0.25f, 0.7f), aw + new Vector3(W * 0.5f + 0.2f, 0.25f, 0.7f), aw + new Vector3(W * 0.5f + 0.2f, -0.35f, -0.5f),
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(3f, 1f), new Vector2(3f, 0f), Vector3.up);
            var roof = batch.W(Roof, c);
            roof.Box(new Vector3(c.x, fy + 2.55f, c.z), new Vector3(W + 0.5f, 0.14f, D + 0.4f), Quaternion.Euler(-6f, 0f, 0f));
            PropKit.Board(batch, new Vector3(c.x, fy + 2.85f, c.z - D * 0.5f - 0.05f), q * Quaternion.Euler(0f, 180f, 0f), 1.8f, 0.4f, "TANTE ADA", LowPoly.Hex("#F2C94C"), LowPoly.Hex("#1E1B2B"));
            for (int i = 0; i < 4; i++) batch.W(i % 2 == 0 ? PropMats.Glass : Yellow, c).Cylinder(counter + new Vector3(-1.3f + i * 0.18f, 0.51f, 0.1f), Quaternion.identity, 0.06f, 0.06f, 0.16f, 6, true, true);
            batch.W(PropMats.Flat(PropPal.Ink), c).Box(new Vector3(c.x + W * 0.5f + 0.4f, fy + 0.9f, c.z - D * 0.5f), new Vector3(0.05f, 0.9f, 0.6f), Quaternion.Euler(0f, 0f, -8f));
            PropKit.Stool(batch, new Vector3(c.x - 0.8f, fy, c.z - D * 0.5f - 1.0f));
            PropKit.Stool(batch, new Vector3(c.x + 0.6f, fy, c.z - D * 0.5f - 1.1f));
            // npc kiosk items rest on the counter (v_kiosk + (±0.45, 0.95, 0)); Ada stands behind it
            PropSpots.Register("v_kiosk", new Vector3(c.x, fy, counter.z - 0.05f));
            PropSpots.Register("v_ada", new Vector3(c.x, fy, c.z + 0.4f));
            PropInfo.SetSpot("kiosk", new Vector3(c.x, fy, c.z - D * 0.5f - 0.8f), 0f);
            PropInfo.AddLandSpot(new Vector3(c.x + W * 0.5f + 0.07f, fy + 1.4f, c.z), LandSpotKind.Wall, 0.5f, "village", Vector3.right);
        }

        // ------------------------------------------------------------------ music stage (Lantern Evening)
        static void Stage()
        {
            Vector3 c = L(6f, -6f);
            float top = c.y + 0.5f;
            batch.W(Wood, c).Box(new Vector3(c.x, top - 0.25f, c.z), new Vector3(5f, 0.5f, 3.4f), Quaternion.identity);
            PropCol.Box(cols, new Vector3(c.x, top - 0.25f, c.z), new Vector3(5f, 0.5f, 3.4f), Quaternion.identity, "Stage");
            for (int i = 0; i < 2; i++) batch.W(PropMats.Wood3, c).Box(new Vector3(c.x - 1.2f + i * 0.3f, top - 0.38f + i * 0.0f - 0.12f * i, c.z - 1.95f - i * 0.3f), new Vector3(1.4f, 0.24f, 0.3f), Quaternion.identity);
            PropCol.Box(cols, new Vector3(c.x - 1.05f, top - 0.38f, c.z - 1.95f), new Vector3(1.4f, 0.25f, 0.3f), Quaternion.identity, "StageStep");
            // backdrop with the village cloth, two posts with a string of lanterns
            for (int s = -1; s <= 1; s += 2) Posts(new Vector3(c.x + s * 2.4f, top + 2.6f, c.z + 1.6f), top - 0.4f, 0.09f);
            Vector3 bd = new Vector3(c.x, top + 1.55f, c.z + 1.65f);
            batch.W(PropMats.WaveCloth(2), c).QuadAuto(bd + new Vector3(-2.3f, -1.0f, 0f), bd + new Vector3(-2.3f, 1.0f, 0f), bd + new Vector3(2.3f, 1.0f, 0f), bd + new Vector3(2.3f, -1.0f, 0f),
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(3.5f, 1f), new Vector2(3.5f, 0f), Vector3.back);
            batch.W(PropMats.WaveCloth(2), c).QuadAuto(bd + new Vector3(2.3f, -1.0f, 0.01f), bd + new Vector3(2.3f, 1.0f, 0.01f), bd + new Vector3(-2.3f, 1.0f, 0.01f), bd + new Vector3(-2.3f, -1.0f, 0.01f),
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(3.5f, 1f), new Vector2(3.5f, 0f), Vector3.forward);
            PropKit.LightString(batch, new Vector3(c.x - 2.4f, top + 2.55f, c.z + 1.6f), new Vector3(c.x + 2.4f, top + 2.55f, c.z + 1.6f), 0.3f, PropMats.LanternGlow);
            // Juno's marimba
            Vector3 m = new Vector3(c.x + 0.6f, top, c.z + 0.2f);
            for (int s = -1; s <= 1; s += 2) batch.W(WoodDark, m).Box(m + new Vector3(s * 0.7f, 0.4f, 0f), new Vector3(0.08f, 0.8f, 0.5f), Quaternion.identity);
            for (int i = 0; i < 10; i++) batch.W(i % 2 == 0 ? PropMats.Wood3 : PropMats.WoodLight, m).Box(m + new Vector3(-0.6f + i * 0.13f, 0.82f, 0f), new Vector3(0.1f, 0.04f, 0.5f - i * 0.025f), Quaternion.identity);
            PropCol.Box(cols, m + Vector3.up * 0.42f, new Vector3(1.5f, 0.85f, 0.55f), Quaternion.identity, "Marimba");
            PropSpots.Register("v_stage", new Vector3(c.x - 0.4f, top, c.z - 0.3f));
            PropInfo.SetSpot("stage", new Vector3(c.x, top, c.z), 180f);
            // village grill next to the stage (E.1 – shared food, like the hut campfire)
            Vector3 grill = L(1.5f, -4.5f);
            PropBeach.GrillGrate(root, batch, grill);
            PropInfo.SetSpot("grill_village", grill);
            for (int i = 0; i < 3; i++)
            {
                float a = (i * 120f + 30f) * Mathf.Deg2Rad;
                PropKit.Log(batch, cols, grill + new Vector3(Mathf.Cos(a) * 2.2f, 0.18f, Mathf.Sin(a) * 2.2f), i * 60f + 90f, 1.6f, 0.17f, PropMats.Wood2);
            }
            PropSpots.Register("v_teen", grill + new Vector3(2.2f, 0f, 0.4f));
        }

        // ------------------------------------------------------------------ terrace houses (4 incl. the guesthouse)
        static void TerraceHouses(System.Random rng)
        {
            Vector3[] pos = { L(-21f, 6.5f), L(-9f, 9f), L(9f, 9.5f), L(18.5f, 6.5f) };
            float[] yaw = { 160f, 180f, 185f, 200f };
            for (int i = 0; i < pos.Length; i++)
            {
                PropGround.MinMax(pos[i].x, pos[i].z, 2.6f, out float mn, out float mx);
                House(pos[i], yaw[i], 4.2f, 3.6f, mx + 0.25f, i, true);
            }
            PropInfo.SetSpot("guesthouse", pos[3] + Quaternion.Euler(0f, yaw[3], 0f) * new Vector3(0f, 0f, 3.0f), yaw[3]);
            PropSpots.Register("v_guesthouse", pos[3] + Quaternion.Euler(0f, yaw[3], 0f) * new Vector3(0.6f, 0f, 2.8f));
            // Grandpa's hammock between the west house and a palm (he naps here)
            Vector3 ha = L(-19.5f, 3.6f) + Vector3.up * 1.1f, hb = L(-16.6f, 4.8f) + Vector3.up * 1.1f;
            Posts(ha + Vector3.up * 0.4f, ha.y - 1.4f, 0.09f);
            Posts(hb + Vector3.up * 0.4f, hb.y - 1.4f, 0.09f);
            PropKit.Hammock(batch, cols, ha, hb, 1, false);
            PropSpots.Register("v_grandpa", (ha + hb) * 0.5f - Vector3.up * 0.55f);
            // where he carves driftwood animals during the day (south of the hammock, away from the house)
            PropSpots.Register("v_grandpa_carve", L(-17.6f, 2.2f));
        }

        // ------------------------------------------------------------------ crafts: weaving, nets, smokehouse
        static void Crafts(System.Random rng)
        {
            // weaving corner: two mats on frames, a basket of fibre, stools
            Vector3 wv = L(-5.5f, 1.6f);
            for (int i = 0; i < 2; i++)
            {
                Vector3 f = wv + new Vector3(i * 1.6f, 0f, 0f);
                for (int s = -1; s <= 1; s += 2) batch.W(WoodDark, f).Rod(f + new Vector3(s * 0.55f, 0f, 0f), f + new Vector3(s * 0.55f, 1.2f, -0.2f), 0.035f, 0.03f, 5);
                Vector3 mc = f + new Vector3(0f, 0.75f, -0.12f);
                batch.W(PropMats.WaveCloth(i), f).QuadAuto(mc + new Vector3(-0.5f, -0.4f, 0.05f), mc + new Vector3(-0.5f, 0.4f, -0.05f), mc + new Vector3(0.5f, 0.4f, -0.05f), mc + new Vector3(0.5f, -0.4f, 0.05f),
                    new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f), Vector3.back);
                batch.W(PropMats.WaveCloth(i), f).QuadAuto(mc + new Vector3(0.5f, -0.4f, 0.06f), mc + new Vector3(0.5f, 0.4f, -0.04f), mc + new Vector3(-0.5f, 0.4f, -0.04f), mc + new Vector3(-0.5f, -0.4f, 0.06f),
                    new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f), Vector3.forward);
                PropKit.Stool(batch, f + new Vector3(0f, 0f, -0.9f));
            }
            batch.W(PropMats.Thatch, wv).Cylinder(wv + new Vector3(0.8f, 0f, 0.8f), Quaternion.identity, 0.3f, 0.36f, 0.35f, 8, false, true);
            PropSpots.Register("v_weaver1", wv + new Vector3(0f, 0f, -0.9f));
            PropSpots.Register("v_weaver2", wv + new Vector3(1.6f, 0f, -0.9f));
            // net-drying racks + Tomas' spot, a fisher couple mending
            PropKit.NetRack(batch, cols, L(-8f, -9.5f), 10f, 3.0f);
            PropKit.NetRack(batch, cols, L(-13f, -10.5f), -5f, 2.6f);
            PropKit.CrabTrap(batch, L(-10.5f, -8.2f), 30f);
            PropKit.CrabTrap(batch, L(-10.1f, -7.6f), 70f, 0.8f);
            PropKit.RopeCoil(batch, L(-6.4f, -8.2f), 0.3f);
            PropSpots.Register("v_nets", L(-8f, -8.4f));
            PropSpots.Register("v_tomas", L(-9.2f, -8.3f));
            PropSpots.Register("v_fisher1", L(-11.6f, -9.6f));
            PropSpots.Register("v_fisher2", L(-12.8f, -9.2f));
            // smokehouse: a small hut with a smoking chimney
            Vector3 sm = L(-21.5f, -2.5f);
            PropGround.MinMax(sm.x, sm.z, 1.6f, out _, out float smx);
            House(sm, 110f, 2.4f, 2.0f, smx + 0.15f, 5, false);
            var smoke = new GameObject("Smokehouse");
            smoke.transform.SetParent(root, false);
            PropFX.Smoke(smoke.transform, new Vector3(sm.x, smx + 3.9f, sm.z));
            for (int i = 0; i < 5; i++) batch.W(PropMats.Flat(LowPoly.Hex("#B5653F")), sm).Box(sm + new Vector3(1.8f, 1.2f, -0.6f + i * 0.3f), new Vector3(0.05f, 0.35f, 0.12f), Quaternion.Euler(0f, 0f, 10f));
            batch.W(WoodDark, sm).Box(sm + new Vector3(1.8f, 1.45f, 0f), new Vector3(0.05f, 0.05f, 1.6f), Quaternion.identity);
        }

        // ------------------------------------------------------------------ gardens, goat pen, chicken run
        static void PensAndGardens(System.Random rng)
        {
            // vegetable gardens: raised beds with rows of plants
            for (int b = 0; b < 3; b++)
            {
                Vector3 g = L(-2f + b * 3.2f, 10.5f);
                batch.W(PropMats.WoodDark, g).Box(g + Vector3.up * 0.12f, new Vector3(2.6f, 0.24f, 1.4f), Quaternion.identity);
                batch.W(PropMats.Flat(PropPal.Soil), g).Box(g + Vector3.up * 0.25f, new Vector3(2.4f, 0.04f, 1.2f), Quaternion.identity);
                for (int r = 0; r < 3; r++)
                    for (int k = 0; k < 6; k++)
                    {
                        Vector3 p = g + new Vector3(-1.0f + k * 0.4f, 0.32f, -0.4f + r * 0.4f);
                        batch.W(r == 1 ? PropMats.FoliageLight : PropMats.Foliage, g).Ico(p, new Vector3(0.14f, 0.12f, 0.14f), Quaternion.Euler(0f, k * 40f, 0f), 0);
                        if (b == 1 && r != 1 && k % 2 == 0) batch.W(PropMats.FlowerRed, g).Octa(p + Vector3.up * 0.1f, 0.04f);
                    }
                PropCol.Box(cols, g + Vector3.up * 0.15f, new Vector3(2.6f, 0.3f, 1.4f), Quaternion.identity, "GardenBed");
            }
            // goat pen (Kapitän Meck & herd) – fence, a shelter, hay
            Vector3 gp = L(-27f, 3f);
            Pen(gp, 6f, 5f, 0f, "goat_pen");
            batch.W(Roof, gp).Box(gp + new Vector3(1.8f, 1.5f, 1.6f), new Vector3(2.0f, 0.1f, 1.6f), Quaternion.Euler(8f, 0f, 0f));
            for (int s = -1; s <= 1; s += 2) Posts(gp + new Vector3(1.8f + s * 0.9f, 1.5f, 1.0f), gp.y - 0.2f, 0.06f);
            batch.W(PropMats.Flat(LowPoly.Hex("#D9C27A")), gp).Box(gp + new Vector3(-1.4f, 0.25f, 1.2f), new Vector3(1.0f, 0.5f, 0.7f), Quaternion.Euler(0f, 15f, 0f));
            PropInfo.SetSpot("goat_pen", gp);
            PropInfo.AddLandSpot(gp + Vector3.up * 0.1f, LandSpotKind.Pen, 2.6f, "goat_pen");
            // chicken run with a little coop
            Vector3 cr = L(23f, -2.5f);
            Pen(cr, 4f, 3.2f, 0f, "chicken_run");
            batch.W(PropMats.Flat(LowPoly.Hex("#C2A57A")), cr).Box(cr + new Vector3(1.0f, 0.6f, 0.8f), new Vector3(1.2f, 0.8f, 0.9f), Quaternion.identity);
            batch.W(Roof, cr).Box(cr + new Vector3(1.0f, 1.1f, 0.8f), new Vector3(1.4f, 0.1f, 1.1f), Quaternion.Euler(10f, 0f, 0f));
            PropInfo.SetSpot("chicken_run", cr);
            PropInfo.AddLandSpot(cr + Vector3.up * 0.1f, LandSpotKind.Pen, 1.6f, "chicken_run");
            // village palms with fruit bats (roosts) – the palms themselves are planted by the jungle builder
            PropInfo.AddLandSpot(L(-24f, -6f) + Vector3.up * 7f, LandSpotKind.Roost, 2f, "village");
            PropInfo.AddLandSpot(L(24f, 8f) + Vector3.up * 7f, LandSpotKind.Roost, 2f, "village");
            PropInfo.AddLandSpot(L(0f, -10f) + Vector3.up * 0.1f, LandSpotKind.Meadow, 6f, "village");
        }

        static void Pen(Vector3 c, float w, float d, float yaw, string id)
        {
            Quaternion q = Quaternion.Euler(0f, yaw, 0f);
            Vector3[] corners = { c + q * new Vector3(-w * 0.5f, 0f, -d * 0.5f), c + q * new Vector3(w * 0.5f, 0f, -d * 0.5f), c + q * new Vector3(w * 0.5f, 0f, d * 0.5f), c + q * new Vector3(-w * 0.5f, 0f, d * 0.5f) };
            for (int i = 0; i < 4; i++)
            {
                Vector3 a = corners[i], b = corners[(i + 1) % 4];
                int n = Mathf.Max(2, Mathf.RoundToInt((b - a).magnitude / 1.2f));
                for (int k = 0; k <= n; k++)
                {
                    Vector3 p = Vector3.Lerp(a, b, k / (float)n);
                    p.y = G(p);
                    batch.W(WoodDark, p).Box(p + Vector3.up * 0.5f, new Vector3(0.1f, 1.0f, 0.1f), q);
                }
                a.y = G(a); b.y = G(b);
                for (int r = 0; r < 2; r++) batch.W(PropMats.Wood2, a).Beam(a + Vector3.up * (0.4f + r * 0.4f), b + Vector3.up * (0.4f + r * 0.4f), 0.06f, 0.08f);
                Vector3 seg = b - a;
                seg.y = 0f;
                PropCol.Box(cols, (a + b) * 0.5f + Vector3.up * 0.5f, new Vector3(0.12f, 1.0f, seg.magnitude), Quaternion.LookRotation(seg.normalized), "PenFence");
            }
        }

        // ------------------------------------------------------------------ the beach: football goal, canoes, nets
        static void Beach(System.Random rng)
        {
            // village beach east (Village + (18, 0, −10)): the kids' football goal
            Vector3 goal = L(18f, -11f);
            Quaternion gq = Quaternion.Euler(0f, 90f, 0f);
            var w = batch.W(PropMats.White, goal);
            for (int s = -1; s <= 1; s += 2) w.Box(goal + gq * new Vector3(s * 1.5f, 0.6f, 0f), new Vector3(0.08f, 1.2f, 0.08f), gq);
            w.Box(goal + gq * new Vector3(0f, 1.2f, 0f), new Vector3(3.08f, 0.08f, 0.08f), gq);
            for (int s = -1; s <= 1; s += 2) w.Rod(goal + gq * new Vector3(s * 1.5f, 1.2f, 0f), goal + gq * new Vector3(s * 1.5f, 0f, -0.9f), 0.025f, 0.025f, 4);
            for (int i = 0; i <= 6; i++) w.Rod(goal + gq * new Vector3(-1.5f + i * 0.5f, 1.2f, 0f), goal + gq * new Vector3(-1.5f + i * 0.5f, 0f, -0.9f), 0.008f, 0.008f, 3);
            for (int k = 1; k < 4; k++) w.Rod(goal + gq * new Vector3(-1.5f, 1.2f - k * 0.3f, -k * 0.22f), goal + gq * new Vector3(1.5f, 1.2f - k * 0.3f, -k * 0.22f), 0.008f, 0.008f, 3);
            for (int s = -1; s <= 1; s += 2) PropCol.Capsule(cols, goal + gq * new Vector3(s * 1.5f, 0.6f, 0f), 0.06f, 1.2f, Quaternion.identity, "GoalPost");
            Vector3 ball = goal + gq * new Vector3(0.3f, 0.12f, 3.2f);
            batch.W(PropMats.White, ball).Ico(ball, Vector3.one * 0.12f, Quaternion.identity, 1);
            PropSpots.Register("v_lio", goal + gq * new Vector3(0.8f, 0f, 4.5f));
            PropInfo.SetSpot("village_football", goal);
            // canoe racks and drying nets on the beach
            PropKit.CanoeRack(batch, cols, L(12f, -13.5f), 95f, 2, 33);
            PropKit.CanoeRack(batch, cols, L(-3f, -14f), 85f, 1, 34);
            PropKit.NetRack(batch, cols, L(-18f, -12f), 5f, 2.6f);
            // two outrigger canoes pulled up on the sand
            for (int i = 0; i < 2; i++)
            {
                Vector3 cp = L(-6f + i * 4.5f, -18.5f);
                PropKit.Canoe(batch, Matrix4x4.TRS(cp + Vector3.up * 0.15f, Quaternion.Euler(0f, 12f - i * 20f, 0f), Vector3.one), i == 0 ? OrangeC : Teal, PropMats.Wood3);
            }
            // shell wind chimes on a driftwood frame
            Vector3 ch = L(14.5f, -6f);
            batch.W(PropMats.Driftwood, ch).Rod(ch, ch + Vector3.up * 2.0f, 0.05f, 0.04f, 5);
            batch.W(PropMats.Driftwood, ch).Rod(ch + Vector3.up * 1.95f, ch + new Vector3(0.9f, 1.95f, 0f), 0.04f, 0.03f, 5);
            for (int i = 0; i < 5; i++)
            {
                Vector3 h = ch + new Vector3(0.15f + i * 0.17f, 1.95f, 0f);
                batch.W(PropMats.Rope, h).Rod(h, h - Vector3.up * (0.4f + 0.1f * (i % 3)), 0.005f, 0.005f, 3);
                batch.W(i % 2 == 0 ? PropMats.FlowerWhite : PropMats.FlowerPink, h).Ico(h - Vector3.up * (0.45f + 0.1f * (i % 3)), new Vector3(0.05f, 0.04f, 0.02f), Quaternion.identity, 0);
            }
        }

        // ------------------------------------------------------------------ the village jetty (boat landing, lanterns)
        static void Jetty()
        {
            float deckY = PropInfo.DockDeckY;
            Vector3 a = L(6f, -14f), b = L(6f, -30f);
            Vector3 dir = (b - a);
            dir.y = 0f;
            float len = dir.magnitude;
            dir /= len;
            Vector3 lat = Vector3.Cross(Vector3.up, dir);
            Quaternion q = Quaternion.LookRotation(dir);
            Material[] planks = { PropMats.Wood, PropMats.Wood2, PropMats.Wood3 };
            int n = Mathf.RoundToInt(len / 0.3f);
            for (int i = 0; i < n; i++)
            {
                Vector3 p = a + dir * (i * 0.3f + 0.15f);
                float g = G(p);
                float y = Mathf.Max(deckY, g + 0.25f);
                batch.W(planks[i % 3], p).Box(new Vector3(p.x, y - 0.04f, p.z), new Vector3(2.0f, 0.08f, 0.27f), q);
            }
            for (float t = 0f; t <= len + 0.01f; t += 2.0f)
            {
                Vector3 p = a + dir * t;
                float g = G(p);
                float y = Mathf.Max(deckY, g + 0.25f);
                for (int s = -1; s <= 1; s += 2) Posts(new Vector3(p.x, y + 0.05f, p.z) + lat * (s * 0.95f), g - 0.6f, 0.11f);
                PropCol.Box(cols, new Vector3(p.x, y - 0.1f, p.z) + dir * 1.0f, new Vector3(2.0f, 0.2f, 2.05f), q, "VillageJetty");
                if (Mathf.RoundToInt(t) % 4 == 0 && t > 1f)
                {
                    // lantern posts every 4 m (they glow at night – the Lantern Evening gathers here)
                    PropKit.LanternPost(batch, cols, root, new Vector3(p.x, y, p.z) + lat * 1.05f, Mathf.Atan2(-lat.x, -lat.z) * Mathf.Rad2Deg, 1.6f, Mathf.RoundToInt(t) % 8 == 0);
                    PropInfo.AddLandSpot(new Vector3(p.x, y + 1.7f, p.z) + lat * 1.05f, LandSpotKind.Railing, 0.3f, "village_jetty");
                }
            }
            // mooring bollards + a ladder into the water at the end
            Vector3 end = b;
            float ey = Mathf.Max(deckY, G(end) + 0.25f);
            for (int s = -1; s <= 1; s += 2)
                batch.W(PropMats.DarkMetal, end).Cylinder(new Vector3(end.x, ey, end.z) + lat * (s * 0.7f) - dir * 0.4f, Quaternion.identity, 0.12f, 0.1f, 0.4f, 6, false, true);
            Vector3 lfoot = new Vector3(end.x, Waves.SeaLevel - 1.4f, end.z) + dir * 1.05f;
            var lm = batch.W(PropMats.Metal, end);
            for (int s = -1; s <= 1; s += 2) lm.Rod(lfoot + lat * (s * 0.25f), new Vector3(lfoot.x, ey + 0.8f, lfoot.z) + lat * (s * 0.25f), 0.03f, 0.03f, 5);
            for (float y = lfoot.y + 0.3f; y < ey; y += 0.3f) lm.Rod(new Vector3(lfoot.x, y, lfoot.z) - lat * 0.25f, new Vector3(lfoot.x, y, lfoot.z) + lat * 0.25f, 0.02f, 0.02f, 4);
            PropLadder.Create(root, "VillageJettyLadder", lfoot, ey + 0.8f, new Vector3(end.x, ey + 0.02f, end.z) - dir * 0.6f, dir, LadderInfo.Metal, true);
            // berth for the player's boat on the east side of the jetty end (L.8: second boat landing point)
            Vector3 berth = end - dir * 3f + lat * -3.4f;
            berth.y = Waves.SeaLevel;
            PropInfo.SetSpot("village_mooring", berth, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg);
            PropInfo.SetSpot("village_jetty", new Vector3(end.x, ey, end.z) - dir * 1.5f, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg);
            PropInfo.SetSpot("lantern_jetty", new Vector3(end.x, ey, end.z) - dir * 4f);
            PropSpots.Register("v_jetty", new Vector3(a.x, Mathf.Max(deckY, G(a) + 0.25f), a.z) + dir * 2.5f);
            PropSpots.Register("v_jetty_end", new Vector3(end.x, ey, end.z) - dir * 1.2f);
            Vector3 lanterns = end + dir * 6f;
            lanterns.y = Waves.SeaLevel;
            PropSpots.Register("v_lanterns", lanterns);
            Vector3 canoe = end + dir * 3f + lat * 3f;
            canoe.y = Waves.SeaLevel;
            PropSpots.Register("v_canoe_start", canoe);
            // the diving helmet (treasure) lies in the village lagoon at ~2.5 m beside the jetty
            if (PropReefs.FindSpot(end + lat * 9f, 18f, 1.8f, 3.2f, null, 61, out Vector3 hp)) PropInfo.SetSpot("helmet_spot", hp + Vector3.up * 0.1f);
        }

        // ------------------------------------------------------------------ six stilt houses on a boardwalk
        static void StiltHouses(System.Random rng)
        {
            float deckY = PropInfo.DockDeckY + 0.2f;
            Vector3 a = L(-14f, -16f), b = L(-14f, -38f);
            Vector3 dir = Vector3.back;
            Quaternion q = Quaternion.LookRotation(dir);
            // the boardwalk spine
            for (float t = 0f; t < 22.01f; t += 0.3f)
            {
                Vector3 p = a + dir * t;
                float g = G(p);
                float y = Mathf.Max(deckY, g + 0.2f);
                batch.W(((int)(t * 10f) % 3) == 0 ? PropMats.Wood2 : PropMats.Wood, p).Box(new Vector3(p.x, y - 0.04f, p.z), new Vector3(1.8f, 0.08f, 0.28f), q);
            }
            for (float t = 0f; t <= 22.01f; t += 2.2f)
            {
                Vector3 p = a + dir * t;
                float g = G(p);
                float y = Mathf.Max(deckY, g + 0.2f);
                for (int s = -1; s <= 1; s += 2) Posts(new Vector3(p.x + s * 0.85f, y, p.z), g - 0.6f, 0.1f);
                PropCol.Box(cols, new Vector3(p.x, y - 0.1f, p.z) + dir * 1.1f, new Vector3(1.8f, 0.2f, 2.25f), q, "Boardwalk");
                // a rope rail on the outer posts
            }
            // six houses: three each side, porches onto the boardwalk
            for (int i = 0; i < 6; i++)
            {
                int side = i % 2 == 0 ? -1 : 1;
                float t = 4.5f + (i / 2) * 6.5f;
                Vector3 hc = a + dir * t + Vector3.right * (side * 4.2f);
                float floorY = deckY + 0.35f;
                float g = G(hc);
                // stilts
                for (int sx = -1; sx <= 1; sx += 2) for (int sz = -1; sz <= 1; sz += 2)
                        Posts(new Vector3(hc.x + sx * 1.8f, floorY, hc.z + sz * 1.6f), g - 0.8f, 0.12f);
                House(hc, side < 0 ? 90f : -90f, 3.8f, 3.4f, floorY, 10 + i, false);
                // porch bridge to the boardwalk
                Vector3 pb = a + dir * t + Vector3.right * (side * 1.6f);
                batch.W(PropMats.Wood3, pb).Box(new Vector3(pb.x, floorY - 0.06f, pb.z), new Vector3(1.6f, 0.1f, 1.2f), Quaternion.identity);
                PropCol.Box(cols, new Vector3(pb.x, floorY - 0.08f, pb.z), new Vector3(1.6f, 0.14f, 1.2f), Quaternion.identity, "Porch");
                // a hanging net, a little boat tied up
                if (i % 2 == 0) PropKit.MooringBuoy(root, hc + Vector3.right * (side * 3.4f) + dir * 1.5f, Teal, 0.5f);
            }
            PropInfo.SetSpot("stilt_houses", a + dir * 11f, 180f);
            // the beach end of the boardwalk: a dinghy and paddles
            PropKit.LanternPost(batch, cols, root, a + Vector3.right * 1.2f, 0f, 1.7f, true);
        }

        // ------------------------------------------------------------------ remaining NPC spots
        static void Spots()
        {
            PropSpots.Register("village", L(0f, 0f));
            // evening / home spots for the background villagers by their houses (npc:bg1 … bg6)
            Vector3[] homes = { L(-21f, 3.5f), L(-9f, 6f), L(9f, 6.5f), L(18.5f, 3.6f), L(-10f, -27f), L(-18f, -31f) };
            for (int i = 0; i < homes.Length; i++) PropInfo.SetSpot("npc:bg" + (i + 1) + ":home", homes[i]);
        }
    }
}
