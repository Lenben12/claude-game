using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// K.3 „more to do under water“ – the props part: today's finds and salvage on every site (+ the lagoon), Dr. Fin's
    /// research stake spots, the challenge board at every site buoy, the currents (Riffkanal, kelp surge, wreck hull,
    /// blue-drop drift, Fog Isle channel) and the caves / swim-throughs (Kelptunnel, Seelöwen-Grotte with its air
    /// pocket, the stuck chest, the Blue Chamber, the radio buoy crack, the lava tube with the TV antenna mast).
    /// Everything is placed with fixed seeds per site (identical on every co-op machine).
    public static class PropUnder
    {
        static float G(float x, float z) => PropGround.H(x, z);

        /// Kelp tunnel corridor (the kelp builder keeps it free of stalks): from A to B, half width.
        public static Vector3 KelpTunnelA, KelpTunnelB;
        public const float KelpTunnelHalfWidth = 1.9f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { KelpTunnelA = KelpTunnelB = Vector3.zero; }

        /// True inside the kelp tunnel corridor (used by PropKelp while placing stalks).
        public static bool InKelpTunnel(float x, float z)
        {
            if (KelpTunnelA == KelpTunnelB) return false;
            Vector2 a = new Vector2(KelpTunnelA.x, KelpTunnelA.z), b = new Vector2(KelpTunnelB.x, KelpTunnelB.z), p = new Vector2(x, z);
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
            return (a + ab * t - p).sqrMagnitude < KelpTunnelHalfWidth * KelpTunnelHalfWidth;
        }

        /// Called before PropKelp.Build: the 30 m tunnel through the dense stand around the kelp buoy.
        public static void PlanKelpTunnel(DiveSite site)
        {
            Vector3 c = site.center;
            KelpTunnelA = new Vector3(c.x - 16f, 0f, c.z - 7f);
            KelpTunnelB = new Vector3(c.x + 13f, 0f, c.z + 6f);
        }

        // ================================================================== per-site extras
        public static IEnumerator SiteExtras(Transform decor, DiveSite site)
        {
            var batch = new PropBatch(64f);
            var cols = new GameObject("ExtraColliders").transform;
            cols.SetParent(decor, false);
            var rng = new System.Random(FindsDatabase.StableHash(site.id) & 0x7FFFFFFF);
            Stakes(site);
            switch (site.id)
            {
                case "kelp_forest": Kelp(decor, batch, cols, rng, site); break;
                case "wreck": WreckOutside(decor, batch, cols, rng, site); break;
                case "blue_drop": Blue(decor, batch, cols, rng, site); break;
                case "deep_trench": Trench(decor, batch, cols, rng, site); break;
                case "fog_isle":
                    PropInfo.AddCurrent("fog_channel", "fog_isle", new Vector3(site.center.x - 6f, -6f, site.center.z - 70f), new Vector3(site.center.x + 2f, -6f, site.center.z - 30f), 9f, 0.5f);
                    break;
            }
            PropReef.QuietShadows(batch);
            batch.Flush(decor, "SiteExtras", ShadowCastingMode.On, 160f);
            if (PropRun.Due()) yield return null;
            PropFindsSite.Create(decor, site.id, site);
        }

        /// Dr. Fin's three research stakes per site (K.3): the npc module spawns them at QuestSpots "stake:site:n";
        /// the props publish good seabed spots inside the site's depth band.
        static void Stakes(DiveSite site)
        {
            float min = Mathf.Max(2f, site.minDepth), max = Mathf.Max(min + 2f, Mathf.Min(site.maxDepth, site.minDepth + 14f));
            int salt = FindsDatabase.StableHash(site.id) & 0xFFFF;
            for (int n = 0; n < 3; n++)
            {
                float ang = (n * 120f + salt % 90) * Mathf.Deg2Rad;
                Vector3 c = new Vector3(site.center.x + Mathf.Cos(ang) * site.radius * 0.35f, 0f, site.center.z + Mathf.Sin(ang) * site.radius * 0.35f);
                if (!PropReefs.FindSpot(c, site.radius * 0.6f, min, max, null, 40 + n, out Vector3 p)) continue;
                PropSpots.Register("stake:" + site.id + ":" + n, p);
            }
        }

        // ------------------------------------------------------------------ kelp
        static void Kelp(Transform decor, PropBatch batch, Transform cols, System.Random rng, DiveSite site)
        {
            // ---- tunnel: guideline rope with little floats, two marker buoys at the ends, the surge along it
            if (KelpTunnelA != KelpTunnelB)
            {
                Vector3 a = KelpTunnelA, b = KelpTunnelB;
                Vector3 prev = Vector3.zero;
                for (int i = 0; i <= 12; i++)
                {
                    Vector3 p = Vector3.Lerp(a, b, i / 12f);
                    p.y = G(p.x, p.z) + 0.5f;
                    if (i > 0) batch.W(PropMats.White, p).Rod(prev, p, 0.014f, 0.014f, 3);
                    if (i % 3 == 0)
                    {
                        batch.W(PropMats.DarkMetal, p).Rod(p, p - Vector3.up * 0.55f, 0.02f, 0.02f, 4);
                        batch.W(PropMats.BuoyOrange, p).Ico(p + Vector3.up * 0.1f, Vector3.one * 0.07f, Quaternion.identity, 0);
                    }
                    prev = p;
                }
                for (int e = 0; e < 2; e++)
                {
                    Vector3 p = e == 0 ? a : b;
                    p.y = G(p.x, p.z);
                    batch.W(PropMats.Rope, p).Rod(p, p + Vector3.up * 5f, 0.015f, 0.015f, 3);
                    batch.W(PropMats.BuoyOrange, p).Ico(p + Vector3.up * 5.2f, Vector3.one * 0.28f, Quaternion.identity, 0);
                    PropRocks.Build(batch.B(PropMats.Rock, p), p, new Vector3(0.5f, 0.3f, 0.5f), Quaternion.identity, 4400 + e, 0);
                    PropSolid.Rock(cols, p, new Vector3(0.5f, 0.3f, 0.5f), Quaternion.identity, 4400 + e, 0, 0.14f, true, "TunnelRock");   // round 3
                }
                Vector3 mid = (a + b) * 0.5f;
                float y = Mathf.Min(G(a.x, a.z), G(b.x, b.z)) + 4f;
                PropInfo.AddCurrent("kelp_surge", "kelp_forest", new Vector3(a.x, y, a.z), new Vector3(b.x, y, b.z), 7f, 0.8f);
                PropInfo.SetSpot("kelp_tunnel", new Vector3(mid.x, G(mid.x, mid.z) + 1f, mid.z));
            }

            // ---- Seelöwen-Grotte: a rocky swim-through mound at 10–15 m with an air pocket at its end
            Vector3 gc = site.center + new Vector3(site.radius * 0.45f, 0f, -site.radius * 0.25f);
            if (PropReefs.FindSpot(gc, 50f, 10f, 16f, null, 77, out Vector3 gp))
            {
                float yaw = Mathf.Atan2(site.center.x - gp.x, site.center.z - gp.z) * Mathf.Rad2Deg + 180f;   // mouth faces the site centre
                float floorY = PropGrotto.FitFloor(gp, yaw, PropGrotto.Tube);
                var g = PropGrotto.Build(batch, cols, new Vector3(gp.x, floorY, gp.z), yaw, PropGrotto.Tube, 515, PropMats.Rock, PropMats.Flat(PropPal.KelpDark), 3f, 1.8f, true);
                PropGrotto.Guideline(batch, g, 0.35f);
                Vector3 pocket = g.W(g.centers[g.centers.Length - 2] + new Vector3(0f, 2.6f, 0f));
                AirPocket(decor, batch, pocket, 1.9f);
                PropInfo.SetSpot("sea_lion_grotto", g.W(g.centers[g.centers.Length - 2] + Vector3.up * 0.5f), yaw);
                PropInfo.AddHabitat(g.W(new Vector3(0f, 1f, -1f)), 3f, HabitatKind.Overhang, "kelp_forest");
                PropInfo.AddHabitat(g.W(g.centers[3] + Vector3.up * 0.6f), 2f, HabitatKind.Crevice, "kelp_forest");
                // anemones and sponges on the mound
                for (int i = 0; i < 10; i++)
                {
                    Vector3 lp = g.centers[rng.Next(g.centers.Length)] + new Vector3(PropUtil.Range(rng, -2f, 2f), 4.4f, 0f);
                    Vector3 wp = g.W(lp);
                    PropReef.Place(batch, rng, wp, Vector3.up, i % 3 == 0 ? PropCoralType.Anemone : PropCoralType.Sponge, 0.9f, -3f, cols);
                }
            }

            // ---- the stuck chest (two players – treasure „kelp_chest“)
            Vector3 cc = site.center + new Vector3(-site.radius * 0.3f, 0f, site.radius * 0.35f);
            if (PropReefs.FindSpot(cc, 40f, 12f, 20f, null, 91, out Vector3 cp))
            {
                CoopChest(decor, batch, cols, cp, PropUtil.Range(rng, 0f, 360f), "kelp_chest", "coop_kelp_chest");
            }
        }

        /// A heavy sea chest wedged between rocks: „E – Zu zweit drücken“ (PropCoop) lifts the lid; the treasure inside
        /// (PropCollect, requireFlag) can then be taken.
        static void CoopChest(Transform decor, PropBatch batch, Transform cols, Vector3 p, float yaw, string spot, string flag)
        {
            Quaternion q = Quaternion.Euler(0f, yaw, 0f);
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 rp = p + q * new Vector3(s * 1.1f, 0.2f, 0f);
                var m = PropRocks.Mesh(new Vector3(1.1f, 1.0f, 1.3f), 9100 + s, 1, 0.16f);
                batch.Add(m, PropMats.RockDark, Matrix4x4.TRS(rp, q, Vector3.one));
                PropCol.Mesh(cols, m, rp, q, Vector3.one, "ChestRock");
            }
            batch.W(PropMats.WoodDark, p).Box(p + Vector3.up * 0.25f, new Vector3(1.0f, 0.5f, 0.62f), q);
            batch.W(PropMats.Gold, p).Box(p + Vector3.up * 0.25f + q * new Vector3(0f, 0f, 0.315f), new Vector3(0.14f, 0.18f, 0.02f), q);
            for (int s = -1; s <= 1; s += 2) batch.W(PropMats.DarkMetal, p).Box(p + Vector3.up * 0.25f + q * new Vector3(s * 0.35f, 0f, 0f), new Vector3(0.06f, 0.52f, 0.64f), q);
            PropCol.Box(cols, p + Vector3.up * 0.25f, new Vector3(1.0f, 0.5f, 0.62f), q, "Chest");
            var lidRoot = new GameObject("ChestLid");
            lidRoot.transform.SetParent(decor, false);
            lidRoot.transform.SetPositionAndRotation(p + Vector3.up * 0.5f + q * new Vector3(0f, 0f, -0.31f), q);
            var lid = new PropMeshBuilder();
            lid.Box(new Vector3(0f, 0.09f, 0.31f), new Vector3(1.02f, 0.18f, 0.64f));
            PropUtil.MeshObject(lidRoot.transform, "Lid", lid.ToMesh("ChestLid"), PropMats.WoodDark, Vector3.zero, Quaternion.identity);
            var trig = new GameObject("ChestCoop");
            trig.transform.SetParent(decor, false);
            trig.transform.position = p + Vector3.up * 0.4f;
            var sc = trig.AddComponent<SphereCollider>();
            sc.isTrigger = true;
            sc.radius = 0.9f;
            var ca = trig.AddComponent<PropCoopAction>();
            ca.spotId = spot;
            ca.flag = flag;
            ca.verbDe = "Zu zweit aufstemmen";
            ca.verbEn = "Pry open together";
            ca.soloDe = "Klemmt – zu zweit?";
            ca.soloEn = "Stuck – two players?";
            ca.soloPresses = 5;   // GDD Q56: alone there is a detour – pry it open with 5 quick presses
            ca.moving = lidRoot.transform;
            ca.openEuler = new Vector3(-75f, 0f, 0f);
            PropInfo.SetSpot(spot, p + Vector3.up * 0.55f, yaw);
            PropInfo.SetSpot(spot + "_two_player", p, yaw);
        }

        /// Air pocket (GDD 14.6): a silvery mirror ceiling in a cave chamber; „E – Luft holen“ refills the tank.
        public static void AirPocket(Transform decor, PropBatch batch, Vector3 p, float radius)
        {
            var mirror = PropMats.Flat(LowPoly.Hex("#BFD8E6"));
            batch.W(mirror, p).Disk(p, radius, 8, Quaternion.identity, false);
            batch.W(PropMats.Flat(LowPoly.Hex("#E8F2F7")), p).Ring(p - Vector3.up * 0.01f, radius * 0.6f, radius * 0.75f, 8, Quaternion.identity);
            var go = new GameObject("AirPocket");
            go.transform.SetParent(decor, false);
            go.transform.position = p - Vector3.up * 0.8f;
            var sc = go.AddComponent<SphereCollider>();
            sc.isTrigger = true;
            sc.radius = radius;
            go.AddComponent<PropAirPocket>();
            PropFX.Bubbles(decor, p - Vector3.up * 1.4f, 0.8f, 0.4f);
        }

        // ------------------------------------------------------------------ wreck (outside)
        static void WreckOutside(Transform decor, PropBatch batch, Transform cols, System.Random rng, DiveSite site)
        {
            Vector3 c = PropInfo.WreckCenter != Vector3.zero ? PropInfo.WreckCenter : site.center;
            // the hull current along the starboard side (GDD 14.8)
            float yaw = PropInfo.SpotYaw("wreck_axis");
            Vector3 fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward, right = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
            Vector3 a = c - fwd * 32f + right * 9f, b = c + fwd * 32f + right * 9f;
            a.y = G(a.x, a.z) + 4f;
            b.y = G(b.x, b.z) + 4f;
            PropInfo.AddCurrent("wreck_hull", "wreck", a, b, 6f, 0.7f);
            // anchor-chain trail: from the bow down to the anchor 25 m ahead
            Vector3 bow = c + fwd * 34f;
            Vector3 anchor = c + fwd * 58f + right * 4f;
            anchor.y = G(anchor.x, anchor.z);
            Vector3 hawse = bow + Vector3.up * 2f;
            hawse.y = Mathf.Max(hawse.y, G(bow.x, bow.z) + 5f);
            var ch = batch.W(PropMats.Rust, anchor);
            int links = 46;
            for (int i = 0; i < links; i++)
            {
                float t = i / (float)links;
                Vector3 p = Vector3.Lerp(hawse, anchor, t);
                float g = G(p.x, p.z) + 0.12f;
                p.y = Mathf.Max(g, Mathf.Lerp(hawse.y, anchor.y, Mathf.Sqrt(t)));
                Vector3 d = (anchor - hawse).normalized;
                ch.Torus(p, Quaternion.LookRotation(d) * Quaternion.Euler(0f, 0f, i % 2 == 0 ? 0f : 90f) * Quaternion.Euler(90f, 0f, 0f), 0.22f, 0.06f, 6, 3);
            }
            // the anchor
            Quaternion aq = Quaternion.Euler(0f, yaw + 30f, 75f);
            ch.Box(anchor + Vector3.up * 0.4f, new Vector3(0.25f, 2.4f, 0.25f), aq);
            ch.Box(anchor + aq * new Vector3(0f, -0.9f, 0f) + Vector3.up * 0.4f, new Vector3(1.8f, 0.3f, 0.3f), aq);
            PropInfo.SetSpot("wreck_anchor", anchor);
            PropSpots.Register("wreck_anchor", new Vector3(anchor.x, Waves.SeaLevel, anchor.z));
        }

        // ------------------------------------------------------------------ blue drop
        static void Blue(Transform decor, PropBatch batch, Transform cols, System.Random rng, DiveSite site)
        {
            Vector3 c = site.center;
            float R = site.radius;
            // wall cells sorted by depth band
            Vector3 crack = Vector3.zero, chamber = Vector3.zero, ledge = Vector3.zero, d0 = Vector3.zero, d1 = Vector3.zero;
            float bestCrack = float.MaxValue, bestCh = float.MaxValue, bestLedge = float.MaxValue;
            var wall = new List<Vector3>();
            for (float x = -R; x <= R; x += 5f)
                for (float z = -R; z <= R; z += 5f)
                {
                    if (x * x + z * z > R * R) continue;
                    float wx = c.x + x, wz = c.z + z;
                    float g = G(wx, wz), d = -g;
                    float sl = PropGround.Slope(wx, wz, 2.5f);
                    if (sl > 0.65f && d > 15f && d < 60f) wall.Add(new Vector3(wx, g, wz));
                    if (sl > 0.7f && Mathf.Abs(d - 18f) < 1.5f) { float k = Mathf.Abs(d - 18f) - sl * 0.2f; if (k < bestCrack) { bestCrack = k; crack = new Vector3(wx, g, wz); } }
                    if (sl < 0.35f && Mathf.Abs(d - 45f) < 3f) { float k = Mathf.Abs(d - 45f) + sl; if (k < bestCh) { bestCh = k; chamber = new Vector3(wx, g, wz); } }
                    if (sl < 0.3f && Mathf.Abs(d - 28f) < 2.5f) { float k = Mathf.Abs(d - 28f) + sl * 2f; if (k < bestLedge) { bestLedge = k; ledge = new Vector3(wx, g, wz); } }
                }
            // ---- drift current along the wall (1.2 m/s) at ~25 m: the two wall cells ~100 m apart in that band
            float bestPair = float.MaxValue;
            for (int i = 0; i < wall.Count; i++)
            {
                if (Mathf.Abs(-wall[i].y - 25f) > 6f) continue;
                for (int j = i + 1; j < wall.Count; j++)
                {
                    if (Mathf.Abs(-wall[j].y - 25f) > 6f) continue;
                    float dist = Vector2.Distance(new Vector2(wall[i].x, wall[i].z), new Vector2(wall[j].x, wall[j].z));
                    float k = Mathf.Abs(dist - 100f);
                    if (k < bestPair) { bestPair = k; d0 = wall[i]; d1 = wall[j]; }
                }
            }
            if (bestPair < 40f)
            {
                Vector3 off0 = Outward(d0) * 6f, off1 = Outward(d1) * 6f;
                PropInfo.AddCurrent("blue_drift", "blue_drop", d0 + off0 + Vector3.up * 2f, d1 + off1 + Vector3.up * 2f, 12f, 1.2f);
                PropInfo.SetSpot("blue_drift", d0 + off0 + Vector3.up * 2f);
            }
            // ---- radio buoy crack at 18 m (secret „blue_buoy“, dark, beeps within 30 m under water)
            if (bestCrack < float.MaxValue)
            {
                Vector3 o = Outward(crack);
                Vector3 side = Vector3.Cross(Vector3.up, o).normalized;
                Quaternion q = Quaternion.LookRotation(o);
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 rp = crack + o * 1.4f + side * (s * 1.35f) + Vector3.up * 1.2f;
                    var m = PropRocks.Mesh(new Vector3(1.6f, 3.2f, 2.2f), 9300 + s, 1, 0.14f);
                    batch.Add(m, PropMats.RockDark, Matrix4x4.TRS(rp, q, Vector3.one));
                    PropCol.Mesh(cols, m, rp, q, Vector3.one, "CrackRock");
                }
                Vector3 roof = crack + o * 1.2f + Vector3.up * 3.1f;
                var rm = PropRocks.Mesh(new Vector3(3.6f, 0.8f, 2.4f), 9310, 1, 0.12f);
                batch.Add(rm, PropMats.Rock, Matrix4x4.TRS(roof, q, Vector3.one));
                PropCol.Mesh(cols, rm, roof, q, Vector3.one, "CrackRoof");
                Vector3 buoy = crack + o * 0.9f + Vector3.up * 0.4f;
                // the TV-studio radio buoy: a dented yellow float with an antenna and a faded sticker
                batch.W(PropMats.DrumYellow, buoy).Ico(buoy + Vector3.up * 0.25f, new Vector3(0.32f, 0.28f, 0.32f), Quaternion.Euler(10f, 20f, 30f), 0);
                batch.W(PropMats.DarkMetal, buoy).Rod(buoy + Vector3.up * 0.45f, buoy + new Vector3(0.2f, 1.1f, 0.1f), 0.015f, 0.01f, 3);
                batch.W(PropMats.RedMetal, buoy).Box(buoy + Vector3.up * 0.26f + o * 0.3f, new Vector3(0.25f, 0.12f, 0.02f), q);
                var bgo = new GameObject("RadioBuoyBeep");
                bgo.transform.SetParent(decor, false);
                bgo.transform.position = buoy + Vector3.up * 0.3f;
                PropSoundLoop.Attach(bgo, PropSound.Beep, 30f, 0.55f, true);
                PropInfo.SetSpot("blue_buoy", buoy + Vector3.up * 0.55f, Mathf.Atan2(o.x, o.z) * Mathf.Rad2Deg);
            }
            // ---- the Blue Chamber at 45 m (free-standing rock grotto on a ledge of the wall)
            if (bestCh < float.MaxValue)
            {
                Vector3 o = Outward(chamber);
                float yaw = Mathf.Atan2(-o.x, -o.z) * Mathf.Rad2Deg;   // mouth toward the open blue
                float floorY = PropGrotto.FitFloor(chamber, yaw, PropGrotto.Small);
                var g = PropGrotto.Build(batch, cols, new Vector3(chamber.x, floorY, chamber.z), yaw, PropGrotto.Small, 717, PropMats.RockDark, PropMats.Flat(LowPoly.Hex("#2E5F93")), 4f, 1.6f, true);
                PropGrotto.Guideline(batch, g, 0.35f);
                // blue glow spots inside (bioluminescent sponges)
                for (int i = 1; i < g.centers.Length; i++)
                {
                    Vector3 lp = g.W(g.centers[i] + new Vector3(g.rings[i].z * 0.7f * (i % 2 == 0 ? 1f : -1f), 0.25f, 0f));
                    batch.W(PropMats.Bio, lp).Ico(lp, Vector3.one * 0.12f, Quaternion.identity, 0);
                }
                PropInfo.SetSpot("blue_chamber", g.W(g.centers[g.centers.Length - 2] + Vector3.up * 0.6f), yaw);
                PropInfo.AddHabitat(g.W(new Vector3(0f, 1f, 1f)), 2.5f, HabitatKind.Overhang, "blue_drop");
            }
            // ---- the drop-off ledge (treasure „drop_ledge“): a small shelf with a rusty tool box
            if (bestLedge < float.MaxValue)
            {
                batch.W(PropMats.Rust, ledge).Box(ledge + Vector3.up * 0.15f, new Vector3(0.6f, 0.3f, 0.35f), Quaternion.Euler(0f, 30f, 8f));
                PropInfo.SetSpot("drop_ledge", ledge + Vector3.up * 0.35f);
            }
        }

        /// Horizontal direction downhill (away from the wall) at a seabed point.
        static Vector3 Outward(Vector3 p)
        {
            Vector3 n = PropGround.Normal(p.x, p.z, 2f);
            Vector3 o = new Vector3(n.x, 0f, n.z);
            return o.sqrMagnitude > 1e-4f ? o.normalized : Vector3.forward;
        }

        // ------------------------------------------------------------------ deep trench
        static void Trench(Transform decor, PropBatch batch, Transform cols, System.Random rng, DiveSite site)
        {
            Vector3 c = site.center;
            // ---- lava tube at 85 m with glowing creatures (decor glow dots)
            if (PropReefs.FindSpot(c + new Vector3(30f, 0f, -25f), site.radius * 0.7f, 82f, 88f, null, 85, out Vector3 tp))
            {
                float yaw = PropUtil.Range(rng, 0f, 360f);
                float floorY = PropGrotto.FitFloor(tp, yaw, PropGrotto.Tube);
                var g = PropGrotto.Build(batch, cols, new Vector3(tp.x, floorY, tp.z), yaw, PropGrotto.Tube, 858, PropMats.Basalt, PropMats.VolcanicDark, 4f, 2f, true);
                PropGrotto.Guideline(batch, g, 0.4f);
                for (int i = 0; i < 26; i++)
                {
                    int k = 1 + rng.Next(g.centers.Length - 1);
                    var r = g.rings[k];
                    float a = PropUtil.Range(rng, 0.2f, 2.9f);
                    Vector3 lp = g.centers[k] + new Vector3(Mathf.Cos(a) * r.z * 0.92f, Mathf.Sin(a) * r.w * 0.92f, PropUtil.Range(rng, -1f, 1f));
                    Vector3 wp = g.W(lp);
                    batch.W(i % 3 == 0 ? PropMats.BioGreen : PropMats.Bio, wp).Ico(wp, Vector3.one * PropUtil.Range(rng, 0.05f, 0.11f), Quaternion.identity, 0);
                }
                var hum = new GameObject("LavaTubeHum");
                hum.transform.SetParent(decor, false);
                hum.transform.position = g.W(g.centers[g.centers.Length - 2] + Vector3.up * 1.5f);
                PropSoundLoop.Attach(hum, PropSound.Hum, 22f, 0.35f, true);
                PropInfo.SetSpot("lava_tube", g.W(g.centers[g.centers.Length - 2] + Vector3.up * 0.8f), yaw);
                PropInfo.AddHabitat(g.W(g.centers[4] + Vector3.up * 0.8f), 3f, HabitatKind.Crevice, "deep_trench");
            }
            // ---- the TV antenna mast (M4) on the 92 m ledge; the last tape lies next to it (npc N2 / q_tape)
            if (PropReefs.FindSpot(c + new Vector3(-30f, 0f, 20f), site.radius * 0.8f, 89f, 95f, null, 92, out Vector3 mp))
            {
                Quaternion q = Quaternion.Euler(14f, PropUtil.Range(rng, 0f, 360f), 6f);
                var met = batch.W(PropMats.Rust, mp);
                const float H = 7f;
                for (int s = 0; s < 3; s++)
                {
                    float a = s * Mathf.PI * 2f / 3f;
                    Vector3 b0 = mp + q * new Vector3(Mathf.Cos(a) * 0.6f, 0f, Mathf.Sin(a) * 0.6f);
                    Vector3 b1 = mp + q * new Vector3(Mathf.Cos(a) * 0.15f, H, Mathf.Sin(a) * 0.15f);
                    met.Rod(b0, b1, 0.05f, 0.04f, 4);
                }
                for (int k = 1; k < 7; k++)
                {
                    float y = k * H / 7f, w = Mathf.Lerp(0.6f, 0.15f, y / H);
                    for (int s = 0; s < 3; s++)
                    {
                        float a0 = s * Mathf.PI * 2f / 3f, a1 = (s + 1) * Mathf.PI * 2f / 3f;
                        met.Rod(mp + q * new Vector3(Mathf.Cos(a0) * w, y, Mathf.Sin(a0) * w), mp + q * new Vector3(Mathf.Cos(a1) * w, y, Mathf.Sin(a1) * w), 0.025f, 0.025f, 3);
                    }
                }
                // the antenna dish + TV-studio sign at the top
                Vector3 top = mp + q * new Vector3(0f, H, 0f);
                batch.W(PropMats.Metal, top).Cylinder(top, q * Quaternion.Euler(70f, 0f, 0f), 0.9f, 0.2f, 0.4f, 8, true, false);
                batch.W(PropMats.RedMetal, top).Box(top - q * new Vector3(0f, 1.2f, 0f) + q * new Vector3(0f, 0f, 0.2f), new Vector3(1.2f, 0.4f, 0.05f), q);
                batch.W(PropMats.Bio, top).Ico(top + q * new Vector3(0f, 0.3f, 0f), Vector3.one * 0.08f, Quaternion.identity, 0);
                PropCol.Capsule(cols, mp + q * new Vector3(0f, H * 0.5f, 0f), 0.6f, H, q, "AntennaMast");
                PropInfo.SetSpot("antenna_mast", mp + Vector3.up * 0.3f);
                PropSpots.Register("antenna", mp);
                PropSpots.Register("m4_mast", mp);
                Vector3 tape = mp + new Vector3(3f, 0f, 2f);
                tape.y = G(tape.x, tape.z) + 0.2f;
                PropSpots.Register("q_tape", tape);
            }
            // ---- the old camera on a ledge at ~60 m (treasure „trench_camera“)
            if (PropReefs.FindSpot(c + new Vector3(10f, 0f, 35f), site.radius * 0.8f, 55f, 65f, null, 60, out Vector3 cp))
            {
                PropInfo.SetSpot("trench_camera", cp + Vector3.up * 0.2f);
                PropRocks.Build(batch.B(PropMats.Basalt, cp), cp - Vector3.up * 0.2f, new Vector3(1.4f, 0.6f, 1.1f), Quaternion.identity, 6060, 0);
                PropSolid.Rock(cols, cp - Vector3.up * 0.2f, new Vector3(1.4f, 0.6f, 1.1f), Quaternion.identity, 6060, 0, 0.14f, true, "CameraLedge");   // round 3
            }
            // the ledge the rivals' sub rests on (R4)
            if (PropReefs.FindSpot(c, site.radius * 0.8f, 82f, 88f, null, 4, out Vector3 lp2))
                PropSpots.Register("trench_ledge", lp2 + Vector3.up * 0.6f);
        }
    }

    /// An air pocket in a cave (K.3, GDD 14.6): „E – Luft holen“ refills the tank in 1.5 s while diving.
    public class PropAirPocket : PropHold
    {
        protected override string VerbDe => "Luft holen";
        protected override string VerbEn => "Breathe";

        void Awake() { seconds = 1.5f; range = 3f; }

        protected override bool Available()
        {
            var p = Refs.Player;
            return p != null && p.IsUnderwater && p.Air01 < 0.98f;
        }

        protected override void Complete()
        {
            var p = Refs.Player;
            if (p == null) return;
            p.RefillAir();
            FXEffects.Bubbles(transform.position, 14);
            GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr("Luftblase!", "Air pocket!"), "100 %");
        }
    }

    // ====================================================================== finds and salvage (K.3)
    /// Today's finds of one site (Finds.Today – identical on every co-op machine): small finds in sand mounds that
    /// glint within 6 m („E – Graben“, 1.5 s), heavy salvage half buried („E – Bergen“: carried with both hands).
    /// Rebuilt when a new day starts; hidden once taken (shared state).
    public class PropFindsSite : MonoBehaviour
    {
        public string siteId;
        Vector3 center;
        float radius, minDepth, maxDepth;
        int builtDay = int.MinValue, builtSeed;
        readonly List<FindSpawn> spawns = new List<FindSpawn>();
        readonly List<PropFindMound> mounds = new List<PropFindMound>();
        float next;

        /// Positions of today's finds by id (the boat winch / map may read them).
        public static readonly Dictionary<string, Vector3> Positions = new Dictionary<string, Vector3>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Positions.Clear(); }

        public static PropFindsSite Create(Transform parent, string siteId, DiveSite site)
        {
            var go = new GameObject("Finds_" + siteId);
            go.transform.SetParent(parent, false);
            var f = go.AddComponent<PropFindsSite>();
            f.siteId = siteId;
            if (site != null)
            {
                f.center = site.center;
                f.radius = site.radius;
                f.minDepth = Mathf.Max(1.5f, site.minDepth + 0.5f);
                f.maxDepth = Mathf.Max(f.minDepth + 3f, Mathf.Min(site.maxDepth, site.minDepth + (site.maxDepth - site.minDepth) * 0.6f));
            }
            else
            {
                f.center = Vector3.zero;
                f.radius = 0f;
                f.minDepth = 1.5f;
                f.maxDepth = 5f;
            }
            return f;
        }

        void OnEnable()
        {
            GameEvents.DayStarted += OnDay;
            GameEvents.GameStarted += Rebuild;
            GameEvents.ReturnedToMenu += Clear;
            Finds.Changed += Refresh;
            Net.StateSynced += Refresh;
            next = 0f;
        }

        void OnDisable()
        {
            GameEvents.DayStarted -= OnDay;
            GameEvents.GameStarted -= Rebuild;
            GameEvents.ReturnedToMenu -= Clear;
            Finds.Changed -= Refresh;
            Net.StateSynced -= Refresh;
        }

        void OnDay(int day) => Rebuild();

        void Update()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 2f;
            var gm = GameManager.I;
            if (gm == null || !gm.InGame || gm.State == null) return;
            if (gm.State.day != builtDay || gm.State.saveSeed != builtSeed) Rebuild();
        }

        void Clear()
        {
            for (int i = 0; i < mounds.Count; i++)
            {
                if (mounds[i] == null) continue;
                Positions.Remove(mounds[i].id);
                Destroy(mounds[i].gameObject);
            }
            mounds.Clear();
            builtDay = int.MinValue;
        }

        void Rebuild()
        {
            var gm = GameManager.I;
            if (gm == null || !gm.InGame || gm.State == null) return;
            Clear();
            builtDay = gm.State.day;
            builtSeed = gm.State.saveSeed;
            Finds.Today(siteId, spawns);
            for (int i = 0; i < spawns.Count; i++)
            {
                var s = spawns[i];
                if (!Place(s, i, out Vector3 p)) continue;
                var m = PropFindMound.Create(transform, s, p);
                mounds.Add(m);
                Positions[s.id] = p;
            }
            Refresh();
        }

        void Refresh()
        {
            for (int i = 0; i < mounds.Count; i++) if (mounds[i] != null) mounds[i].Refresh();
        }

        bool Place(FindSpawn s, int index, out Vector3 pos)
        {
            for (int k = 0; k < 6; k++)
            {
                float u = (s.u + k * 0.618f) % 1f, v = (s.v + k * 0.37f) % 1f;
                float a = u * Mathf.PI * 2f;
                Vector3 c;
                if (radius > 0f) c = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (radius * (0.15f + 0.7f * Mathf.Sqrt(v)));
                else
                {
                    // the lagoon: from the shoreline 10–50 m out
                    Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    float shore = 60f;
                    for (float r = 40f; r < 260f; r += 2f) if (PropGround.H(dir.x * r, dir.z * r) < -1f) { shore = r; break; }
                    c = dir * (shore + 10f + v * 40f);
                }
                if (PropReefs.FindSpot(c, 14f, minDepth, maxDepth, null, index * 7 + k, out pos)) return true;
            }
            pos = Vector3.zero;
            return false;
        }
    }

    /// One find: a sand mound (small find) or a half-buried salvage model (heavy). E digs / recovers it.
    public class PropFindMound : PropHold
    {
        public string id;
        public bool heavy;
        public GameObject model;
        public ParticleSystem glint;
        bool glinting, visible = true;
        float nextGlint;

        protected override string VerbDe => heavy ? "Bergen" : "Graben";
        protected override string VerbEn => heavy ? "Recover" : "Dig";

        void Awake() { range = 3.2f; }

        protected override bool Available() => !Finds.IsTaken(id) && (!heavy || !Carry.IsCarrying || BoatWinch.CanHook(transform.position));

        protected override string UnavailableHint => heavy && !Finds.IsTaken(id) && Carry.IsCarrying ? PropPrompt.Text("Hände sind voll", "Hands are full") : null;

        protected override void Complete()
        {
            if (heavy)
            {
                // K.3 boat winch (crew equipment, ≤ 40 m from the boat): hook it with a lift bag instead of carrying
                if (BoatWinch.CanHook(transform.position))
                {
                    if (!BoatWinch.Hook(id, transform.position, null)) return;
                }
                else
                {
                    if (Carry.IsCarrying) return;
                    if (!Finds.Take(id)) return;
                    Carry.Pick(id);
                    GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr("Schwer!", "Heavy!"), Loc.Tr("Zum Boot oder Steg", "To the boat or dock"));
                }
            }
            else Finds.Take(id);
            FXEffects.Bubbles(transform.position + Vector3.up * 0.2f, 8);
            Refresh();
        }

        protected override void OnStart() { FXEffects.Bubbles(transform.position + Vector3.up * 0.1f, 4); }

        public void Refresh()
        {
            bool taken = Finds.IsTaken(id);
            if (visible == !taken) return;
            visible = !taken;
            if (model != null) model.SetActive(visible);
            var col = GetComponent<Collider>();
            if (col != null) col.enabled = visible;
            if (!visible && glint != null) glint.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        protected override void Update()
        {
            base.Update();
            if (Time.unscaledTime < nextGlint) return;
            nextGlint = Time.unscaledTime + 0.5f;
            bool g = visible && PropCam.Valid && (PropCam.Position - transform.position).sqrMagnitude < 6f * 6f;
            if (g == glinting || glint == null) return;
            glinting = g;
            if (g) glint.Play(true);
            else glint.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        static Mesh moundMesh;
        static readonly Dictionary<string, Mesh> salvage = new Dictionary<string, Mesh>();
        static readonly Dictionary<string, Material> salvageMat = new Dictionary<string, Material>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { moundMesh = null; salvage.Clear(); salvageMat.Clear(); }

        public static PropFindMound Create(Transform parent, FindSpawn s, Vector3 p)
        {
            var go = new GameObject(s.id.Replace(':', '_'));
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, s.w * 360f, 0f));
            var f = go.AddComponent<PropFindMound>();
            f.id = s.id;
            f.heavy = s.heavy;
            f.seconds = s.heavy ? 1.2f : 1.5f;
            var model = new GameObject("Model");
            model.transform.SetParent(go.transform, false);
            f.model = model;
            if (moundMesh == null)
            {
                var b = new PropMeshBuilder();
                b.Ico(new Vector3(0f, -0.04f, 0f), new Vector3(0.45f, 0.16f, 0.4f), Quaternion.identity, 0);
                b.Ico(new Vector3(0.18f, -0.02f, 0.1f), new Vector3(0.2f, 0.08f, 0.18f), Quaternion.identity, 0);
                moundMesh = b.ToMesh("FindMound");
            }
            PropUtil.MeshObject(model.transform, "Mound", moundMesh, PropMats.Flat(PropMats.Value(PropPal.Seabed, 1.05f)), Vector3.zero, Quaternion.identity, false);
            if (s.heavy)
            {
                string kind = s.def != null ? s.def.id : "amphora";
                var mesh = SalvageMesh(kind, out var mat);
                PropUtil.MeshObject(model.transform, "Salvage", mesh, mat, new Vector3(0f, 0.05f, 0f), Quaternion.Euler(18f, 0f, 24f), false);
            }
            else if (s.def != null && s.def.id == "coins")
            {
                var b = new PropMeshBuilder();
                b.Cylinder(new Vector3(0.12f, 0.1f, 0f), Quaternion.Euler(70f, 0f, 0f), 0.05f, 0.05f, 0.012f, 7, true, true);
                PropUtil.MeshObject(model.transform, "Coin", b.ToMesh("FindCoin"), PropMats.Gold, Vector3.zero, Quaternion.identity, false);
            }
            var sc = go.AddComponent<SphereCollider>();
            sc.isTrigger = true;
            sc.radius = s.heavy ? 0.9f : 0.6f;
            sc.center = Vector3.up * 0.15f;
            f.glint = PropFX.Sparkle(go.transform, p + Vector3.up * 0.2f, 0.25f);
            if (f.glint != null) f.glint.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return f;
        }

        static Mesh SalvageMesh(string kind, out Material mat)
        {
            if (salvage.TryGetValue(kind, out var m)) { mat = salvageMat[kind]; return m; }
            var b = new PropMeshBuilder();
            Color brass = LowPoly.Hex("#B8913A");
            switch (kind)
            {
                case "ship_bell":
                    b.Lathe(Vector3.zero, Quaternion.identity, new[] { new Vector2(0.3f, 0f), new Vector2(0.26f, 0.08f), new Vector2(0.18f, 0.35f), new Vector2(0.14f, 0.48f), new Vector2(0.05f, 0.52f), new Vector2(0.05f, 0.6f), new Vector2(0f, 0.62f) }, 10);
                    mat = PropMats.Flat(brass); break;
                case "amphora":
                    b.Lathe(Vector3.zero, Quaternion.identity, new[] { new Vector2(0.02f, 0f), new Vector2(0.14f, 0.1f), new Vector2(0.24f, 0.35f), new Vector2(0.2f, 0.6f), new Vector2(0.08f, 0.75f), new Vector2(0.09f, 0.85f), new Vector2(0f, 0.86f) }, 8);
                    for (int s = -1; s <= 1; s += 2) b.Torus(new Vector3(s * 0.13f, 0.72f, 0f), Quaternion.Euler(90f, 0f, 0f), 0.06f, 0.02f, 6, 3);
                    mat = PropMats.Flat(LowPoly.Hex("#B5653F")); break;
                case "outboard":
                    b.Box(new Vector3(0f, 0.55f, 0f), new Vector3(0.4f, 0.4f, 0.55f));
                    b.Box(new Vector3(0f, 0.2f, 0.1f), new Vector3(0.14f, 0.5f, 0.14f));
                    b.Box(new Vector3(0f, -0.05f, 0.15f), new Vector3(0.36f, 0.06f, 0.06f));
                    mat = PropMats.Flat(LowPoly.Hex("#4F7FAE")); break;
                case "tv_1980":
                    b.Box(new Vector3(0f, 0.25f, 0f), new Vector3(0.6f, 0.48f, 0.45f));
                    b.Box(new Vector3(0f, 0.27f, 0.23f), new Vector3(0.44f, 0.34f, 0.02f));
                    mat = PropMats.TvBody; break;
                case "ship_wheel":
                    b.Torus(new Vector3(0f, 0.05f, 0f), Quaternion.identity, 0.45f, 0.04f, 10, 4);
                    for (int i = 0; i < 8; i++) { float a = i * Mathf.PI / 4f; b.Rod(new Vector3(0f, 0.05f, 0f), new Vector3(Mathf.Cos(a) * 0.62f, 0.05f, Mathf.Sin(a) * 0.62f), 0.025f, 0.02f, 4); }
                    b.Cylinder(Vector3.zero, Quaternion.identity, 0.08f, 0.08f, 0.1f, 6, true, true);
                    mat = PropMats.Flat(LowPoly.Hex("#7A5232")); break;
                case "cannon":
                    b.Cylinder(new Vector3(0f, 0.15f, -0.6f), Quaternion.Euler(90f, 0f, 0f), 0.2f, 0.13f, 1.3f, 8, true, true);
                    b.Ico(new Vector3(0f, 0.15f, -0.68f), Vector3.one * 0.16f, Quaternion.identity, 0);
                    mat = PropMats.Flat(LowPoly.Hex("#3B3A4E")); break;
                case "brass_helmet":
                    b.Ico(new Vector3(0f, 0.25f, 0f), Vector3.one * 0.24f, Quaternion.identity, 1);
                    b.Cylinder(Vector3.zero, Quaternion.identity, 0.26f, 0.26f, 0.08f, 10, false, false);
                    mat = PropMats.Flat(brass); break;
                default:
                    b.Box(new Vector3(0f, 0.2f, 0f), new Vector3(0.8f, 0.4f, 0.5f));
                    b.Box(new Vector3(0f, 0.45f, 0f), new Vector3(0.82f, 0.12f, 0.52f));
                    mat = PropMats.WoodDark; break;
            }
            m = b.ToMesh("Salvage_" + kind);
            salvage[kind] = m;
            salvageMat[kind] = mat;
            return m;
        }
    }

    /// The challenge board at a site buoy (K.3): „E – Foto-Aufgaben lesen“ shows today's two challenges (done ✓).
    public class PropChallengeBoard : MonoBehaviour, IInteractable
    {
        float last = -10f;
        static readonly System.Text.StringBuilder sb = new System.Text.StringBuilder(160);

        public string GetPrompt() => CanInteract() ? PropPrompt.Interact("Foto-Aufgaben lesen", "Read photo tasks") : null;

        public bool CanInteract() => GameManager.I != null && GameManager.I.InGame && Time.unscaledTime - last > 0.6f;

        public void Interact()
        {
            if (!CanInteract()) return;
            last = Time.unscaledTime;
            var today = Challenges.Today;
            sb.Clear();
            if (today == null || today.Count == 0) sb.Append(Loc.Tr("Heute keine Aufgaben.", "No tasks today."));
            else
                for (int i = 0; i < today.Count; i++)
                {
                    var def = ChallengeDatabase.Get(today[i]);
                    if (def == null) continue;
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(Challenges.IsDone(today[i]) ? "✓ " : "• ").Append(def.Title);
                }
            GameEvents.RaiseBanner(Loc.Tr("Foto-Aufgaben des Tages", "Today's photo tasks") + "\n" + sb, new Color(0.96f, 0.91f, 0.78f));
        }
    }
}
