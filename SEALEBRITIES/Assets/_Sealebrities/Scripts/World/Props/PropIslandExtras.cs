using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Q54 / H.2 / L.6 / K.8 extras on the landmark islands (called by PropMiniIslands per island):
    ///  * a rest spot on every island: bench (rest seat), striped sun sail, an old air station („E – Luft auffüllen“),
    ///  * two golden-shell spots per island (beaches), a logbook-page spot on the bench,
    ///  * one secret each – kelp: „G+H 1989“ carved into a palm in a tiny cove behind the summit knoll (swim round),
    ///    wreck: Gus' hammock in a sea cave at the waterline (entrance 1 m under water, SE side), blue drop: the golden
    ///    egg nest on the pillar (treasure; the radio-buoy secret is under water), trench: an old TV set buried under
    ///    palm fronds on the far beach that shows static only at night,
    ///  * the Fog Isle (K.8): Toni's camp (driftwood hut, hammock, tape reels on strings, a wok satellite dish), the
    ///    small south beach and the rest spot.
    public static class PropIslandExtras
    {
        public delegate float HeightFn(float x, float z);
        public delegate float ShoreFn(float ang);

        public static void Build(Transform root, PropBatch batch, Transform cols, string id, Vector3 c, float R, HeightFn H, ShoreFn shore, PropExclusion spacing, System.Random rng)
        {
            var site = DiveSites.Get(id);
            Vector3 toSite = site != null ? Flat(site.center - c) : Vector3.forward;
            // ---- rest spot: on the flattest dry point 25–55 % of the radius toward the site
            Vector3 rest = BestFlat(c, R, H, toSite, spacing, 0.25f, 0.55f, 1.0f);
            if (rest != Vector3.zero)
            {
                float yaw = Mathf.Atan2(toSite.x, toSite.z) * Mathf.Rad2Deg;
                RestSpot(root, batch, cols, rest, yaw, id, rng);
                spacing.Add(rest.x, rest.z, 4.5f);
            }
            // ---- two golden shells on the beach (opposite sides)
            int shells = 0;
            for (int k = 0; k < 24 && shells < 2; k++)
            {
                float ang = (k * 137.5f + id.Length * 31f) * Mathf.Deg2Rad;
                float rr = shore(ang) * 0.86f;
                float x = c.x + Mathf.Cos(ang) * rr, z = c.z + Mathf.Sin(ang) * rr;
                float g = H(x, z);
                if (g < 0.2f || g > 1.4f || spacing.Blocked(x, z)) continue;
                PropInfo.MiniShellSpots.Add(new Vector3(x, g + 0.02f, z));
                spacing.Add(x, z, 9f);
                shells++;
            }
            // ---- the secret
            switch (id)
            {
                case "kelp_forest": KelpCarving(batch, cols, c, H, shore, toSite, spacing); break;
                case "wreck": WreckSeaCave(root, batch, cols, c, H, shore); break;
                case "deep_trench": TrenchTV(root, batch, c, H, shore, toSite, spacing); break;
                case "fog_isle": FogCamp(root, batch, cols, c, R, H, shore, toSite, spacing, rng); break;
            }
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward; }

        static Vector3 BestFlat(Vector3 c, float R, HeightFn H, Vector3 pref, PropExclusion spacing, float r0, float r1, float minG)
        {
            Vector3 best = Vector3.zero;
            float bestCost = float.MaxValue;
            for (int i = 0; i < 48; i++)
            {
                float a = i * Mathf.PI * 2f / 48f;
                Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                for (float f = r0; f <= r1; f += 0.1f)
                {
                    float x = c.x + d.x * R * f, z = c.z + d.z * R * f;
                    float g = H(x, z);
                    if (g < minG || spacing.Blocked(x, z)) continue;
                    float sl = Mathf.Max(Mathf.Abs(H(x + 1.5f, z) - g), Mathf.Abs(H(x, z + 1.5f) - g), Mathf.Abs(H(x - 1.5f, z) - g), Mathf.Abs(H(x, z - 1.5f) - g));
                    float cost = sl * 4f - Vector3.Dot(d, pref) * 0.8f;
                    if (cost < bestCost) { bestCost = cost; best = new Vector3(x, g, z); }
                }
            }
            return best;
        }

        /// Bench + sun sail + old air station (E.4) – the island rest spot.
        public static void RestSpot(Transform root, PropBatch batch, Transform cols, Vector3 p, float yaw, string id, System.Random rng)
        {
            Quaternion q = Quaternion.Euler(0f, yaw, 0f);
            Vector3 bench = p;
            PropKit.Bench(batch, cols, root, bench, yaw, true, true, false);
            // sun sail: three poles and a striped triangle
            Vector3 s0 = p + q * new Vector3(-1.6f, 0f, -1.0f), s1 = p + q * new Vector3(1.7f, 0f, -0.8f), s2 = p + q * new Vector3(0.1f, 0f, 1.8f);
            Vector3[] poles = { s0, s1, s2 };
            float[] hs = { 2.6f, 2.4f, 2.0f };
            for (int i = 0; i < 3; i++)
            {
                poles[i].y = PropGround.H(poles[i].x, poles[i].z);
                batch.W(PropMats.WoodDark, poles[i]).Cylinder(poles[i] - Vector3.up * 0.3f, Quaternion.identity, 0.06f, 0.05f, hs[i] + 0.3f, 5, false, true);
                PropCol.Capsule(cols, poles[i] + Vector3.up * hs[i] * 0.5f, 0.07f, hs[i], Quaternion.identity, "SailPole");
            }
            Vector3 t0 = poles[0] + Vector3.up * hs[0], t1 = poles[1] + Vector3.up * hs[1], t2 = poles[2] + Vector3.up * hs[2];
            var sailA = PropMats.Flat(LowPoly.Hex("#F2C94C")); var sailB = PropMats.Flat(LowPoly.Hex("#3FA9A0"));
            Vector3 m01 = (t0 + t1) * 0.5f - Vector3.up * 0.15f;
            batch.W(sailA, p).Tri2(t0, m01, t2 - Vector3.up * 0.1f, Vector3.up);
            batch.W(sailB, p).Tri2(m01, t1, t2 - Vector3.up * 0.1f, Vector3.up);
            // the old air station: a dented tank rack + compressor box with a gauge and a hose
            Vector3 a = p + q * new Vector3(2.6f, 0f, 0.8f);
            a.y = PropGround.H(a.x, a.z);
            var met = batch.W(PropMats.Flat(LowPoly.Hex("#9AA0A8")), a);
            for (int i = 0; i < 3; i++) met.Cylinder(a + q * new Vector3(-0.35f + i * 0.33f, 0f, -0.3f), Quaternion.identity, 0.14f, 0.14f, 1.1f, 8, false, true);
            batch.W(PropMats.Flat(LowPoly.Hex("#F2C94C")), a).Box(a + q * new Vector3(0f, 0.4f, 0.25f), new Vector3(0.9f, 0.8f, 0.5f), q);
            batch.W(PropMats.White, a).Cylinder(a + q * new Vector3(0.2f, 0.6f, 0.51f), q * Quaternion.Euler(90f, 0f, 0f), 0.09f, 0.09f, 0.02f, 8, true, true);
            batch.W(PropMats.Ink, a).Rod(a + q * new Vector3(-0.3f, 0.5f, 0.5f), a + q * new Vector3(-0.6f, 0.05f, 0.9f), 0.025f, 0.025f, 4);
            PropCol.Box(cols, a + Vector3.up * 0.55f, new Vector3(1.1f, 1.1f, 0.9f), q, "AirStation");
            var st = new GameObject("AirStation_" + id);
            st.transform.SetParent(root, false);
            st.transform.position = a + Vector3.up * 0.7f;
            var sc = st.AddComponent<SphereCollider>();
            sc.isTrigger = true;
            sc.radius = 1.0f;
            var air = st.AddComponent<PropAirStation>();
            air.hiss = PropFX.Hiss(st.transform, a + q * new Vector3(-0.6f, 0.1f, 0.9f));
            PropInfo.AirStations.Add(a);
            PropInfo.SetSpot("rest_" + id, bench + Vector3.up * 0.47f, yaw);
            PropInfo.SetSpot("page_mini_" + id, bench + q * new Vector3(0.45f, 0.5f, 0.05f), yaw);
            PropInfo.AddLandSpot(t2 + Vector3.up * 0.05f, LandSpotKind.Perch, 0.4f, id);
        }

        // ------------------------------------------------------------------ kelp island: the carving in the cove
        static void KelpCarving(PropBatch batch, Transform cols, Vector3 c, HeightFn H, ShoreFn shore, Vector3 toSite, PropExclusion spacing)
        {
            // far side (away from the site and the boat landing), on the beach of a tiny cove
            Vector3 far = -toSite;
            float ang = Mathf.Atan2(far.z, far.x) + 0.35f;
            Vector3 d = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
            float rr = shore(ang) * 0.8f;
            Vector3 p = c + d * rr;
            p.y = H(p.x, p.z);
            // a leaning palm trunk stump with the carving (a pale heart + "G+H 1989" plate)
            Quaternion q = Quaternion.LookRotation(d);
            batch.W(PropMats.PalmTrunk, p).Cylinder(p - Vector3.up * 0.3f, Quaternion.Euler(8f, ang * Mathf.Rad2Deg, 0f), 0.2f, 0.16f, 2.6f, 7, false, true);
            PropCol.Capsule(cols, p + Vector3.up * 1.2f, 0.22f, 2.6f, Quaternion.identity, "CarvingPalm");
            Vector3 face = p + d * 0.2f + Vector3.up * 1.3f;
            PropKit.Board(batch, face, q, 0.42f, 0.2f, "G+H 1989", PropPal.WoodLight, PropPal.WoodDark);
            PropInfo.SetSpot("kelp_carving", face + d * 0.05f, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg);
            // the cove is cut off from the rest of the island by a wall of boulders (only reachable by swimming round)
            Vector3 lat = Vector3.Cross(Vector3.up, d);
            for (int i = -3; i <= 3; i++)
            {
                Vector3 b = p - d * 3.2f + lat * (i * 1.6f) + d * (Mathf.Abs(i) * 0.7f);
                b.y = H(b.x, b.z) + 0.6f;
                var sc = new Vector3(1.5f, 2.2f, 1.4f);
                int seed = 6100 + i;
                PropRocks.BuildSplit(batch, b, b, sc, Quaternion.Euler(0f, i * 23f, 0f), seed, 1, 0.16f, PropMats.BeachRock, PropMats.Moss, PropMats.Crevice);
                PropCol.Mesh(cols, PropRocks.ColliderMesh(sc, seed, 1, 0.16f), b, Quaternion.Euler(0f, i * 23f, 0f), Vector3.one, "CoveRock");
            }
            spacing.Add(p.x, p.z, 5f);
        }

        // ------------------------------------------------------------------ wreck island: the sea cave with Gus' hammock
        static void WreckSeaCave(Transform root, PropBatch batch, Transform cols, Vector3 c, HeightFn H, ShoreFn shore)
        {
            // south-east side: a free-standing rock outcrop in 2.4–3.6 m water just off the shore, mouth seaward and
            // submerged, the chamber ceiling above the water line with a dry ledge inside
            float ang = -45f * Mathf.Deg2Rad;
            Vector3 d = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
            Vector3 mouth = Vector3.zero;
            for (float r = shore(ang) * 0.9f; r < shore(ang) + 30f; r += 0.75f)
            {
                Vector3 p = c + d * r;
                float g = H(p.x, p.z);
                if (g < -2.4f && g > -4f) { mouth = new Vector3(p.x, g, p.z); break; }
            }
            if (mouth == Vector3.zero) return;
            float yaw = Mathf.Atan2(-d.x, -d.z) * Mathf.Rad2Deg;     // +z of the cave points back toward the island
            Vector4[] rings =
            {
                new Vector4(0f, -0.4f, 1.0f, 1.3f), new Vector4(0f, 1.0f, 1.1f, 1.5f), new Vector4(0.3f, 2.6f, 1.6f, 3.6f),
                new Vector4(0.5f, 4.4f, 2.2f, 4.3f), new Vector4(0.4f, 6.2f, 2.2f, 4.3f), new Vector4(0.2f, 7.6f, 1.5f, 3.4f),
            };
            float floorY = Mathf.Max(PropGrotto.FitFloor(mouth, yaw, rings), -2.4f);
            var g2 = PropGrotto.Build(batch, cols, new Vector3(mouth.x, floorY, mouth.z), yaw, rings, 4747, PropMats.BeachRock, PropMats.Moss, 3f, 1.6f, true);
            // the dry ledge inside (a ramp from under the water up to +0.4 m)
            Vector3 l0 = g2.W(new Vector3(0.4f, 0f, 3.6f)), l1 = g2.W(new Vector3(0.4f, 0f, 7.2f));
            float wy = Waves.SeaLevel;
            Vector3 ledgeC = (l0 + l1) * 0.5f;
            Quaternion lq = Quaternion.Euler(0f, yaw, 0f);
            batch.W(PropMats.RockMid, ledgeC).Box(new Vector3(ledgeC.x, wy - 1.2f, ledgeC.z), new Vector3(3.4f, 3.2f, 3.0f), lq);
            PropCol.Box(cols, new Vector3(ledgeC.x, wy - 1.2f, ledgeC.z), new Vector3(3.4f, 3.2f, 3.0f), lq, "CaveLedge");
            Vector3 ramp = g2.W(new Vector3(0.4f, 0f, 2.4f));
            Quaternion rq = lq * Quaternion.Euler(-28f, 0f, 0f);
            batch.W(PropMats.RockMid, ramp).Box(new Vector3(ramp.x, wy - 0.5f, ramp.z), new Vector3(2.4f, 0.3f, 2.4f), rq);
            PropCol.Box(cols, new Vector3(ramp.x, wy - 0.5f, ramp.z), new Vector3(2.4f, 0.3f, 2.4f), rq, "CaveRamp");
            // Gus' hammock between two driftwood posts on the ledge
            Vector3 top = new Vector3(ledgeC.x, wy + 0.4f, ledgeC.z);
            Vector3 side = lq * Vector3.right;
            Vector3 ha = top + side * 1.2f + Vector3.up * 1.1f - lq * Vector3.forward * 0.6f, hb = top - side * 1.0f + Vector3.up * 1.1f + lq * Vector3.forward * 0.6f;
            foreach (var hp in new[] { ha, hb })
                batch.W(PropMats.Driftwood, hp).Cylinder(new Vector3(hp.x, top.y - 0.1f, hp.z), Quaternion.identity, 0.07f, 0.06f, 1.4f, 6, false, true);
            PropKit.Hammock(batch, cols, ha, hb, 1, false);
            PropInfo.SetSpot("wreck_hammock", (ha + hb) * 0.5f - Vector3.up * 0.35f, yaw);
            PropInfo.SetSpot("wreck_sea_cave", g2.W(new Vector3(0f, 0.5f, -1f)), yaw);
        }

        // ------------------------------------------------------------------ trench island: the TV on the far beach
        static void TrenchTV(Transform root, PropBatch batch, Vector3 c, HeightFn H, ShoreFn shore, Vector3 toSite, PropExclusion spacing)
        {
            Vector3 far = -toSite;
            float ang = Mathf.Atan2(far.z, far.x) - 0.3f;
            Vector3 d = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
            Vector3 p = c + d * shore(ang) * 0.82f;
            p.y = H(p.x, p.z);
            Quaternion q = Quaternion.LookRotation(-d) * Quaternion.Euler(-12f, 0f, 6f);
            var tv = new GameObject("BuriedTV");
            tv.transform.SetParent(root, false);
            tv.transform.SetPositionAndRotation(p + Vector3.up * 0.1f, q);
            var b = new PropMeshBuilder();
            b.Box(new Vector3(0f, 0.22f, 0f), new Vector3(0.62f, 0.48f, 0.5f));
            b.Box(new Vector3(0.22f, 0.5f, -0.05f), new Vector3(0.03f, 0.1f, 0.03f));
            PropUtil.MeshObject(tv.transform, "Body", b.ToMesh("TrenchTV"), PropMats.TvBody, Vector3.zero, Quaternion.identity);
            var scr = new PropMeshBuilder();
            scr.Box(new Vector3(0f, 0.24f, 0.251f), new Vector3(0.46f, 0.34f, 0.01f));
            var mat = PropMats.EmissiveInstance(new Color(0.12f, 0.13f, 0.15f), Color.black);
            PropUtil.MeshObject(tv.transform, "Screen", scr.ToMesh("TrenchTVScreen"), mat, Vector3.zero, Quaternion.identity, false);
            tv.AddComponent<PropNightStatic>().screen = mat;
            // palm fronds over it
            for (int i = 0; i < 4; i++)
            {
                Vector3 fp = p + Quaternion.Euler(0f, i * 70f, 0f) * new Vector3(0.3f, 0.55f, 0f);
                batch.W(i % 2 == 0 ? PropMats.PalmLeafDry : PropMats.PalmLeaf, fp).Tri2(fp + new Vector3(-0.9f, -0.2f, 0.1f), fp + new Vector3(0.9f, -0.3f, -0.1f), fp + new Vector3(0f, 0.05f, 0.5f), Vector3.up);
            }
            PropInfo.SetSpot("trench_tv", p + Vector3.up * 0.6f);
            spacing.Add(p.x, p.z, 3f);
        }

        // ------------------------------------------------------------------ Fog Isle: Toni's camp (K.8)
        static void FogCamp(Transform root, PropBatch batch, Transform cols, Vector3 c, float R, HeightFn H, ShoreFn shore, Vector3 toSite, PropExclusion spacing, System.Random rng)
        {
            // south side faces the site (and the buoy lane): the camp sits above the south beach
            Vector3 south = Vector3.back;
            Vector3 camp = BestFlat(c, R, H, south, spacing, 0.3f, 0.6f, 1.2f);
            if (camp == Vector3.zero) camp = new Vector3(c.x, H(c.x, c.z - R * 0.4f), c.z - R * 0.4f);
            float yaw = 180f;
            Quaternion q = Quaternion.Euler(0f, yaw, 0f);
            // driftwood hut: lean-to of logs with a palm-thatch roof
            var dw = batch.W(PropMats.Driftwood, camp);
            for (int i = 0; i < 7; i++)
            {
                Vector3 b0 = camp + q * new Vector3(-1.6f + i * 0.53f, 0f, -1.2f);
                b0.y = PropGround.H(b0.x, b0.z);
                dw.Rod(b0 - Vector3.up * 0.2f, b0 + Vector3.up * 2.0f, 0.09f, 0.08f, 6);
            }
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 f0 = camp + q * new Vector3(s * 1.6f, 0f, 1.0f);
                f0.y = PropGround.H(f0.x, f0.z);
                dw.Rod(f0 - Vector3.up * 0.2f, f0 + Vector3.up * 1.4f, 0.08f, 0.07f, 6);
                dw.Rod(f0 + Vector3.up * 1.4f, camp + q * new Vector3(s * 1.6f, 2.0f, -1.2f) + Vector3.up * (camp.y - camp.y), 0.07f, 0.06f, 5);
            }
            Vector3 r0 = camp + q * new Vector3(0f, 2.05f, -1.25f), r1 = camp + q * new Vector3(0f, 1.45f, 1.15f);
            batch.W(PropMats.Thatch, camp).Box((r0 + r1) * 0.5f, new Vector3(3.6f, 0.12f, (r1 - r0).magnitude + 0.4f), Quaternion.LookRotation(r1 - r0) * Quaternion.Euler(0f, 0f, 0f));
            PropCol.Box(cols, camp + q * new Vector3(0f, 1.0f, -1.25f), new Vector3(3.4f, 2.0f, 0.2f), q, "ToniHutBack");
            // round 3 (GDD §19): the rest of the lean-to is solid too – both front posts, the side beams at head height
            // and the thatch roof (the open front stays the way in)
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 f0 = camp + q * new Vector3(s * 1.6f, 0f, 1.0f);
                f0.y = PropGround.H(f0.x, f0.z);
                PropSolid.Segment(cols, f0 - Vector3.up * 0.1f, f0 + Vector3.up * 1.35f, 0.09f, "ToniHutPost");
                PropSolid.Segment(cols, f0 + Vector3.up * 1.4f, camp + q * new Vector3(s * 1.6f, 2.0f, -1.2f), 0.08f, "ToniHutBeam");
            }
            PropSolid.Box(cols, (r0 + r1) * 0.5f, new Vector3(3.6f, 0.16f, (r1 - r0).magnitude + 0.4f), Quaternion.LookRotation(r1 - r0), "ToniHutRoof");
            // hammock + tape reels on strings + the wok satellite dish
            Vector3 ha = camp + q * new Vector3(-1.5f, 1.3f, 0.6f), hb = camp + q * new Vector3(1.5f, 1.3f, -0.4f);
            PropKit.Hammock(batch, cols, ha, hb, 1, false);
            Vector3 s0 = camp + q * new Vector3(-1.6f, 1.95f, 1.0f), s1 = camp + q * new Vector3(2.8f, 1.6f, 2.2f);
            s1.y = PropGround.H(s1.x, s1.z) + 1.6f;
            batch.W(PropMats.Rope, camp).Rod(s0, s1, 0.01f, 0.01f, 3);
            for (int i = 1; i < 6; i++)
            {
                Vector3 hp = Vector3.Lerp(s0, s1, i / 6f) - Vector3.up * (0.2f + 0.1f * Mathf.Sin(i));
                batch.W(PropMats.Ink, hp).Cylinder(hp, Quaternion.Euler(90f, i * 30f, 0f), 0.09f, 0.09f, 0.02f, 8, true, true);
            }
            Vector3 wok = camp + q * new Vector3(2.4f, 0f, -0.8f);
            wok.y = PropGround.H(wok.x, wok.z);
            batch.W(PropMats.DarkMetal, wok).Rod(wok, wok + Vector3.up * 1.6f, 0.04f, 0.03f, 5);
            batch.W(PropMats.Metal, wok).Cylinder(wok + Vector3.up * 1.6f, Quaternion.Euler(-35f, yaw + 30f, 0f), 0.45f, 0.12f, 0.2f, 8, true, false);
            PropSolid.Segment(cols, wok, wok + Vector3.up * 1.55f, 0.06f, "WokPole");   // round 3
            PropKit.FirePit(batch, root, camp + q * new Vector3(0.2f, 0f, 2.4f) + Vector3.up * (PropGround.H((camp + q * new Vector3(0.2f, 0f, 2.4f)).x, (camp + q * new Vector3(0.2f, 0f, 2.4f)).z) - camp.y), 77, false);
            PropKit.Log(batch, cols, camp + q * new Vector3(-1.0f, 0.2f, 3.0f), yaw + 80f, 1.8f, 0.17f, PropMats.Driftwood);
            PropInfo.SetSpot("toni_fog_camp", camp + q * new Vector3(0.6f, 0f, 1.4f), yaw);
            PropInfo.SetSpot("toni_camp", camp + q * new Vector3(0.6f, 0f, 1.4f), yaw);
            PropSpots.Register("fog_camp", camp + q * new Vector3(0.6f, 0f, 1.4f));
            spacing.Add(camp.x, camp.z, 5.5f);
            // the rest spot east of the camp
            Vector3 rest = BestFlat(c, R, H, Vector3.right, spacing, 0.25f, 0.55f, 1.0f);
            if (rest != Vector3.zero) { RestSpot(root, batch, cols, rest, 90f, "fog_isle_camp", rng); spacing.Add(rest.x, rest.z, 4f); }
            // the south beach: a towel, a bucket and a drift log
            Vector3 beach = c + south * shore(-Mathf.PI * 0.5f) * 0.85f;
            beach.y = H(beach.x, beach.z);
            PropBeach.Towel(batch, beach, 20f);
            PropKit.Bucket(batch, beach + new Vector3(1.2f, 0f, 0.6f), PropMats.Orange);
            PropInfo.SetSpot("fog_beach", beach);
        }

        // ------------------------------------------------------------------ the Fog Isle buoy lane (K.8)
        /// Six yellow buoys from (−60, 420) to (−60, 560) – visible only once Toni's map is known (StoryFlags fog_map).
        public static void BuoyLane(Transform parent)
        {
            var root = new GameObject("FogBuoyLane");
            root.transform.SetParent(parent, false);
            var holder = new GameObject("Buoys");
            holder.transform.SetParent(root.transform, false);
            for (int i = 0; i < 6; i++)
            {
                float z = Mathf.Lerp(420f, 560f, i / 5f);
                float x = WorldLayout.FogIsland.x + (i % 2 == 0 ? -12f : 12f);
                PropKit.MooringBuoy(holder.transform, new Vector3(x, 0f, z), PropMats.DrumYellow, 1.1f);
            }
            var gate = root.AddComponent<PropFlagGate>();
            gate.flag = StoryKeys.FogMap;
            gate.target = holder;
        }
    }

    /// Screen of the buried TV on the trench island: static noise only at night (L.6 #10 – the clue says "signals").
    public class PropNightStatic : MonoBehaviour
    {
        public Material screen;
        float next;
        bool on;

        void Update()
        {
            if (screen == null || Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.12f;
            bool night = EnvState.Night01 > 0.45f;
            if (!night)
            {
                if (on) { on = false; PropMats.SetEmission(screen, Color.black); }
                return;
            }
            on = true;
            float f = 0.6f + 0.4f * Mathf.PerlinNoise(Time.time * 9f, transform.position.x);
            PropMats.SetEmission(screen, new Color(0.7f, 0.75f, 0.85f) * (1.6f * f));
        }
    }

    /// The Fog Isle under water (K.3 / L.6 #13): the Nebelgrotte – a reef-wall cave at 22–28 m with an air-pocket
    /// chamber holding Toni's sound archive (secret „fog_archive“), dark, with a guideline.
    public static class PropFogIsle
    {
        public static void Underwater(Transform decor, DiveSite site)
        {
            var batch = new PropBatch(0f);
            var cols = new GameObject("GrottoColliders").transform;
            cols.SetParent(decor, false);
            // the outer rim of the seamount lagoon (22–26 m) north-west of the site centre
            if (!PropReefs.FindSpot(site.center + new Vector3(-50f, 0f, 10f), 60f, 22f, 26f, null, 13, out Vector3 p)) return;
            Vector3 toC = site.center - p;
            toC.y = 0f;
            float yaw = Mathf.Atan2(-toC.x, -toC.z) * Mathf.Rad2Deg;   // passage runs away from the lagoon
            float floorY = PropGrotto.FitFloor(p, yaw, PropGrotto.Tube);
            var g = PropGrotto.Build(batch, cols, new Vector3(p.x, floorY, p.z), yaw, PropGrotto.Tube, 2828, PropMats.ReefRockDark, PropMats.Flat(LowPoly.Hex("#7A3E8E")), 4f, 2f, true);
            PropGrotto.Guideline(batch, g, 0.35f);
            int last = g.centers.Length - 1;
            Vector3 pocket = g.W(g.centers[last - 1] + new Vector3(0f, g.rings[last - 1].w - 0.35f, 0f));
            PropUnder.AirPocket(decor, batch, pocket, 2.0f);
            // Toni's archive: a shelf of tape reels on a rock shelf in the chamber
            Vector3 shelf = g.W(g.centers[last - 1] + new Vector3(g.rings[last - 1].z * 0.55f, 0.6f, 0.3f));
            Quaternion q = Quaternion.Euler(0f, yaw - 90f, 0f);
            batch.W(PropMats.WoodDark, shelf).Box(shelf, new Vector3(1.2f, 0.06f, 0.4f), q);
            for (int i = 0; i < 6; i++)
                batch.W(i % 2 == 0 ? PropMats.Ink : PropMats.Flat(PropPal.HullBlue), shelf).Cylinder(shelf + q * new Vector3(-0.5f + i * 0.2f, 0.12f, 0f), q * Quaternion.Euler(0f, 0f, 90f), 0.1f, 0.1f, 0.03f, 8, true, true);
            PropInfo.SetSpot("fog_archive", shelf + Vector3.up * 0.25f, yaw);
            PropInfo.SetSpot("fog_grotto", g.W(new Vector3(0f, 0.5f, 1f)), yaw);
            PropInfo.AddHabitat(g.W(g.centers[last - 1] + Vector3.up * 0.8f), 2.5f, HabitatKind.Overhang, "fog_grotto");
            PropInfo.AddHabitat(g.W(g.centers[3] + Vector3.up * 0.8f), 2.5f, HabitatKind.Crevice, "fog_grotto");
            batch.Flush(decor, "Nebelgrotte", ShadowCastingMode.On, 200f);
        }
    }
}
