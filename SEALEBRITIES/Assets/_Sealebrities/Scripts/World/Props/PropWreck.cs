using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Procedural ~70 m cargo ship "MS CLICKBAIT" lying on the seabed, rolled ~12°, with superstructure, mast, crane,
    /// open hatch, dark hull breaches (moray homes), spilled containers and debris.
    /// Ship-local frame: x = beam (starboard +), y = up (keel at 0), z = length (bow +z, stern at z = -35).
    public static class PropWreck
    {
        const float L = 70f, HB = 5.5f, D = 7f, Stern = -35f;

        static float Tz(float z) => (z - Stern) / L;
        static float FB(float z) { float t = Tz(z); return t > 0.72f ? Mathf.Clamp01((t - 0.72f) / 0.28f) : 0f; }
        static float FS(float z) { float t = Tz(z); return t < 0.16f ? Mathf.Clamp01((0.16f - t) / 0.16f) : 0f; }
        public static float HalfBeam(float z) => HB * (1f - 0.97f * Mathf.Pow(FB(z), 1.7f)) * (1f - 0.32f * Mathf.Pow(FS(z), 1.4f));
        public static float Keel(float z) => 2.2f * FB(z) * FB(z) + 2.6f * Mathf.Pow(FS(z), 1.6f);
        public static float DeckY(float z) => D + 1.3f * FB(z) * FB(z) + 0.5f * FS(z);

        static float G(float x, float z) => PropGround.H(x, z);

        /// Section polyline from the starboard deck edge, around the bottom, to the port deck edge (13 points).
        static void Section(float z, List<Vector2> outPts)
        {
            outPts.Clear();
            float hb = HalfBeam(z), keel = Keel(z), deck = DeckY(z);
            float dead = 1.4f * FB(z);
            float rb = Mathf.Min(1.5f, Mathf.Min(hb * 0.45f, (deck - keel) * 0.35f));
            float cx = hb - rb, cy = keel + dead + rb;
            float wl = Mathf.Clamp(3.4f, cy + 0.1f, deck - 0.1f);
            var star = new List<Vector2> { new Vector2(hb, deck), new Vector2(hb, wl) };
            for (int i = 0; i <= 3; i++)
            {
                float th = -i * 30f * Mathf.Deg2Rad;
                star.Add(new Vector2(cx + rb * Mathf.Cos(th), cy + rb * Mathf.Sin(th)));
            }
            outPts.AddRange(star);
            outPts.Add(new Vector2(0f, keel));
            for (int i = star.Count - 1; i >= 0; i--) outPts.Add(new Vector2(-star[i].x, star[i].y));
        }

        public static IEnumerator Build(Transform decor, DiveSite site)
        {
            var rng = new System.Random(1985);
            var batch = new PropBatch(0f);
            var debris = new PropBatch(64f);
            var cols = new GameObject("Colliders").transform;
            cols.SetParent(decor, false);

            // ---------------------------------------------------------------- placement
            Vector3 c = site.center;
            float yaw = 35f + PropUtil.Range(rng, -10f, 10f);
            Quaternion yawRot = Quaternion.Euler(0f, yaw, 0f);
            float sum = 0f;
            int n = 0;
            for (float z = -30f; z <= 30f; z += 6f)
            {
                for (int sx = -1; sx <= 1; sx++)
                {
                    Vector3 p = c + yawRot * new Vector3(sx * 5f, 0f, z);
                    sum += G(p.x, p.z);
                    n++;
                }
            }
            float baseY = sum / n + 0.25f;
            Vector3 pos = new Vector3(c.x, baseY, c.z);
            Quaternion shipRot = Quaternion.Euler(1.5f, yaw, -12f);
            Matrix4x4 S = Matrix4x4.TRS(pos, shipRot, Vector3.one);
            PropInfo.WreckCenter = pos + shipRot * new Vector3(0f, 4f, 0f);

            PropMeshBuilder SB(Material m) { var b = batch.B(m); b.M = S; return b; }

            var colB = new PropMeshBuilder { M = S };

            // ---------------------------------------------------------------- hull loft
            const int NS = 40;
            var secs = new List<Vector2>[NS];
            var zs = new float[NS];
            for (int s = 0; s < NS; s++)
            {
                zs[s] = Stern + L * s / (NS - 1);
                secs[s] = new List<Vector2>(13);
                Section(zs[s], secs[s]);
            }
            var up = SB(PropMats.HullUpper);
            var lo = SB(PropMats.HullLower);
            int upV = up.VertexCount, upT = up.IndexCount, loV = lo.VertexCount, loT = lo.IndexCount;
            for (int s = 0; s < NS - 1; s++)
            {
                var A = secs[s];
                var Bs = secs[s + 1];
                float arcA = 0f, arcB = 0f;
                float mid = (DeckY(zs[s]) + Keel(zs[s])) * 0.5f;
                for (int k = 0; k < A.Count - 1; k++)
                {
                    float la = (A[k + 1] - A[k]).magnitude, lb = (Bs[k + 1] - Bs[k]).magnitude;
                    Vector3 p00 = new Vector3(A[k].x, A[k].y, zs[s]);
                    Vector3 p01 = new Vector3(A[k + 1].x, A[k + 1].y, zs[s]);
                    Vector3 p10 = new Vector3(Bs[k].x, Bs[k].y, zs[s + 1]);
                    Vector3 p11 = new Vector3(Bs[k + 1].x, Bs[k + 1].y, zs[s + 1]);
                    Vector3 outward = new Vector3(p00.x + p01.x, (p00.y + p01.y) * 0.5f - mid, 0f);
                    if (outward.sqrMagnitude < 0.01f) outward = Vector3.down;
                    bool upper = k == 0 || k == A.Count - 2;
                    (upper ? up : lo).QuadAuto(p00, p10, p11, p01,
                        new Vector2(zs[s], arcA), new Vector2(zs[s + 1], arcB), new Vector2(zs[s + 1], arcB + lb), new Vector2(zs[s], arcA + la), outward);
                    if (s % 3 == 0 || s == NS - 2) colB.QuadAuto(p00, p10, p11, p01, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, outward);
                    arcA += la; arcB += lb;
                }
            }
            // collider loft uses every 3rd section, close the gaps with the remaining strips
            for (int s = 0; s < NS - 1; s++)
            {
                if (s % 3 == 0 || s == NS - 2) continue;
                var A = secs[s];
                var Bs = secs[s + 1];
                float mid = (DeckY(zs[s]) + Keel(zs[s])) * 0.5f;
                for (int k = 0; k < A.Count - 1; k++)
                {
                    Vector3 outward = new Vector3(A[k].x + A[k + 1].x, (A[k].y + A[k + 1].y) * 0.5f - mid, 0f);
                    if (outward.sqrMagnitude < 0.01f) outward = Vector3.down;
                    colB.QuadAuto(new Vector3(A[k].x, A[k].y, zs[s]), new Vector3(Bs[k].x, Bs[k].y, zs[s + 1]), new Vector3(Bs[k + 1].x, Bs[k + 1].y, zs[s + 1]), new Vector3(A[k + 1].x, A[k + 1].y, zs[s]),
                        Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, outward);
                }
            }
            up.SmoothNormals(upV, upT);
            lo.SmoothNormals(loV, loT);

            // transom and bow caps
            Cap(up, secs[0], zs[0], Vector3.back);
            Cap(up, secs[NS - 1], zs[NS - 1], Vector3.forward);
            Cap(colB, secs[0], zs[0], Vector3.back);
            Cap(colB, secs[NS - 1], zs[NS - 1], Vector3.forward);

            // deck
            var deck = SB(PropMats.Deck);
            for (int s = 0; s < NS - 1; s++)
            {
                for (int k = 0; k < 4; k++)
                {
                    Vector3 a0 = DeckPoint(zs[s], k / 4f), a1 = DeckPoint(zs[s], (k + 1) / 4f);
                    Vector3 b0 = DeckPoint(zs[s + 1], k / 4f), b1 = DeckPoint(zs[s + 1], (k + 1) / 4f);
                    deck.QuadAuto(a0, b0, b1, a1, new Vector2(a0.x, a0.z), new Vector2(b0.x, b0.z), new Vector2(b1.x, b1.z), new Vector2(a1.x, a1.z), Vector3.up);
                    colB.QuadAuto(a0, b0, b1, a1, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector3.up);
                }
            }
            if (PropRun.Due()) yield return null;

            // name on the stern
            float ny = DeckY(Stern) - 1.35f;
            float nz = Stern - 0.04f;
            SB(PropMats.NamePlate).QuadAuto(new Vector3(-2.7f, ny - 0.36f, nz), new Vector3(-2.7f, ny + 0.36f, nz), new Vector3(2.7f, ny + 0.36f, nz), new Vector3(2.7f, ny - 0.36f, nz),
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f), Vector3.back);

            // ---------------------------------------------------------------- superstructure
            var white = SB(PropMats.Superstructure);
            var dark = SB(PropMats.HoleDark);
            BoxCol(white, colB, new Vector3(0f, 7f + 1.25f, -27.75f), new Vector3(9.4f, 3.1f, 11.5f));
            BoxCol(white, colB, new Vector3(0f, 9.8f + 1.3f, -27.75f), new Vector3(8.4f, 2.6f, 9.5f));
            BoxCol(white, colB, new Vector3(0f, 12.4f + 1.2f, -27.5f), new Vector3(11.6f, 2.4f, 6f));
            white.Box(new Vector3(0f, 14.85f, -27.5f), new Vector3(12f, 0.1f, 6.4f));
            for (float x = -3.6f; x <= 3.61f; x += 1.8f)
            {
                dark.Box(new Vector3(x, 8.3f, -22f + 0.02f), new Vector3(0.9f, 0.7f, 0.06f));
                dark.Box(new Vector3(x, 11.2f, -23f + 0.02f), new Vector3(0.9f, 0.7f, 0.06f));
            }
            for (float x = -4.8f; x <= 4.81f; x += 1.2f) dark.Box(new Vector3(x, 13.7f, -24.5f + 0.02f), new Vector3(1.0f, 0.95f, 0.06f));
            for (float z = -32f; z <= -23.5f; z += 1.7f)
            {
                for (int sx = -1; sx <= 1; sx += 2)
                {
                    dark.Box(new Vector3(sx * 4.71f, 8.4f, z), new Vector3(0.06f, 0.5f, 0.5f));
                    dark.Box(new Vector3(sx * 4.21f, 11.2f, z), new Vector3(0.06f, 0.5f, 0.5f));
                }
            }
            // funnel and bridge mast
            SB(PropMats.Funnel).Cylinder(new Vector3(0f, 12.4f, -31.6f), Quaternion.Euler(-6f, 0f, 0f), 1.35f, 1.15f, 5f, 14, false, false, 6f, 1f);
            SB(PropMats.HoleDark).Cylinder(new Vector3(0f, 12.4f, -31.6f) + Quaternion.Euler(-6f, 0f, 0f) * new Vector3(0f, 4.5f, 0f), Quaternion.Euler(-6f, 0f, 0f), 1.17f, 1.16f, 0.55f, 14, false, true);
            var rust = SB(PropMats.Rust);
            rust.Rod(new Vector3(0f, 14.9f, -27f), new Vector3(0f, 19.2f, -27f), 0.12f, 0.08f, 6);
            rust.Box(new Vector3(0f, 18.6f, -27f), new Vector3(2.6f, 0.12f, 0.25f));

            // foremast with stays
            float fz = 25f, fdeck = DeckY(fz);
            rust.Rod(new Vector3(0f, fdeck, fz), new Vector3(0f, fdeck + 12f, fz), 0.26f, 0.15f, 8, true);
            rust.Rod(new Vector3(-3.2f, fdeck + 9f, fz), new Vector3(3.2f, fdeck + 9f, fz), 0.1f, 0.1f, 6, true);
            rust.Rod(new Vector3(0f, fdeck + 11.8f, fz), new Vector3(0f, DeckY(33f) + 0.2f, 33f), 0.03f, 0.03f, 4);
            rust.Rod(new Vector3(0f, fdeck + 11.5f, fz), new Vector3(HalfBeam(20f) - 0.2f, DeckY(20f), 20f), 0.03f, 0.03f, 4);
            rust.Rod(new Vector3(0f, fdeck + 11.5f, fz), new Vector3(-HalfBeam(20f) + 0.2f, DeckY(20f), 20f), 0.03f, 0.03f, 4);

            // crane
            var crane = SB(PropMats.Crane);
            float cz = 6f, cdeck = DeckY(cz);
            crane.Cylinder(new Vector3(2.4f, cdeck - 0.2f, cz), Quaternion.identity, 0.8f, 0.7f, 2.8f, 12, false, true, 4.6f, 1f);
            BoxCol(crane, colB, new Vector3(2.4f, cdeck + 3.5f, cz), new Vector3(2f, 1.8f, 2.2f));
            Vector3 boomA = new Vector3(2.0f, cdeck + 3.7f, cz + 0.6f);
            Vector3 boomDir = new Vector3(-0.35f, 0.5f, 0.79f).normalized;
            Vector3 boomB = boomA + boomDir * 15f;
            crane.Rod(boomA, boomB, 0.3f, 0.18f, 8, true, 1.5f, 1f);
            SB(PropMats.Rust).Rod(boomB, boomB - Vector3.up * 6.5f, 0.03f, 0.03f, 4);
            SB(PropMats.Rust).Torus(boomB - Vector3.up * 6.8f, Quaternion.FromToRotation(Vector3.up, Vector3.right), 0.28f, 0.07f, 10, 5);

            // cargo hatches (one open: dark hole)
            float[] hz = { 16f, 1f, -12f };
            for (int i = 0; i < hz.Length; i++)
            {
                float hdk = DeckY(hz[i]);
                Hatch(SB(PropMats.Rust), colB, new Vector3(0f, hdk, hz[i]), 7f, 8f, i != 1);
                if (i == 1)
                {
                    SB(PropMats.HoleDark).Box(new Vector3(0f, hdk + 0.06f, hz[i]), new Vector3(6.6f, 0.02f, 7.6f));
                    AddHole(S, shipRot, new Vector3(0f, hdk + 0.3f, hz[i]), Vector3.up);
                }
            }

            // deck clutter: bollards, vents, winch
            var dm = SB(PropMats.DarkMetal);
            for (int i = 0; i < 6; i++)
            {
                float z = -18f + i * 9f;
                for (int sx = -1; sx <= 1; sx += 2)
                {
                    Vector3 bp = DeckPoint(z, sx > 0 ? 0.08f : 0.92f);
                    dm.Cylinder(bp, Quaternion.identity, 0.18f, 0.18f, 0.45f, 8, false, true);
                }
            }
            for (int i = 0; i < 4; i++)
            {
                Vector3 vp = DeckPoint(-19f + i * 11f, i % 2 == 0 ? 0.2f : 0.8f);
                dm.Cylinder(vp, Quaternion.identity, 0.2f, 0.2f, 1.2f, 8);
                dm.Cylinder(vp + Vector3.up * 1.2f, Quaternion.Euler(0f, 0f, 90f), 0.24f, 0.24f, 0.1f, 8, true, true);
            }
            Vector3 wp = DeckPoint(30f, 0.5f);
            dm.Cylinder(wp + new Vector3(-1.2f, 0.5f, 0f), Quaternion.Euler(0f, 0f, -90f), 0.45f, 0.45f, 2.4f, 12, true, true);

            // railings along both deck edges (some posts missing)
            for (int sx = -1; sx <= 1; sx += 2)
            {
                var rail = new List<Vector3>();
                var rr = new List<float>();
                for (float z = -20f; z <= 31f; z += 2.2f)
                {
                    Vector3 e = DeckPoint(z, sx > 0 ? 0.01f : 0.99f) - new Vector3(sx * 0.12f, 0f, 0f);
                    if (rng.NextDouble() > 0.15) rust.Rod(e, e + Vector3.up * 1.1f, 0.035f, 0.035f, 4);
                    rail.Add(e + Vector3.up * 1.1f);
                    rr.Add(0.04f);
                }
                rust.Tube(rail, rr, 4, 1f, 1f);
            }

            // propeller and rudder
            Vector3 hub = new Vector3(0f, 2.3f, Stern - 0.4f);
            rust.Blob(hub, new Vector3(0.45f, 0.45f, 0.7f), Quaternion.identity, 3, null);
            for (int i = 0; i < 4; i++)
            {
                Quaternion q = Quaternion.Euler(0f, 0f, i * 90f + 20f) * Quaternion.Euler(0f, 25f, 0f);
                rust.Box(hub + q * new Vector3(0f, 1.0f, 0f), new Vector3(0.7f, 1.6f, 0.08f), q);
            }
            BoxCol(rust, colB, new Vector3(0f, 3.4f, Stern - 1.6f), new Vector3(0.35f, 3.6f, 2.2f));

            // ---------------------------------------------------------------- hull breaches (moray homes)
            float[] holeZ = { -12f, 3f, 14f, -5f, 9f, -24f };
            float[] holeY = { 2.6f, 3.0f, 2.2f, 2.5f, 2.9f, 3.3f };
            int[] holeSide = { -1, -1, -1, 1, 1, -1 };
            for (int i = 0; i < holeZ.Length; i++)
            {
                float hbz = HalfBeam(holeZ[i]);
                Vector3 hc = new Vector3(holeSide[i] * (hbz + 0.03f), holeY[i], holeZ[i]);
                Breach(SB(PropMats.HoleDark), SB(PropMats.Rust), hc, holeSide[i], PropUtil.Range(rng, 0.6f, 0.95f), rng);
                AddHole(S, shipRot, hc + new Vector3(holeSide[i] * 0.4f, 0f, 0f), new Vector3(holeSide[i], 0f, 0f));
            }

            // a few air bubbles leaking from the bridge
            PropFX.Bubbles(decor, S.MultiplyPoint3x4(new Vector3(-2f, 14.9f, -26f)), 1.5f, 0.3f);

            // collider of hull + deck + superstructure
            var colMesh = colB.ToMesh("WreckCollider");
            PropCol.Mesh(cols, colMesh, Vector3.zero, Quaternion.identity, Vector3.one, "WreckCol");
            if (PropRun.Due()) yield return null;

            // ---------------------------------------------------------------- growth on the wreck
            for (int i = 0; i < 48; i++)
            {
                float z = PropUtil.Range(rng, -20f, 31f);
                if (Mathf.Abs(z - 1f) < 4.5f) continue;
                Vector3 lp = DeckPoint(z, PropUtil.Range(rng, 0.08f, 0.92f));
                var type = PropReef.RandomType(rng, 2, 1, 1, 3, 4, 1);
                if (rng.NextDouble() < 0.18) type = PropCoralType.Anemone;
                Vector3 wpnt = S.MultiplyPoint3x4(lp);
                PropReef.Place(debris, rng, wpnt, shipRot * Vector3.up, type, 0.75f, -4f);
            }
            if (PropRun.Due()) yield return null;

            // ---------------------------------------------------------------- spilled containers
            for (int i = 0; i < 10; i++)
            {
                bool longBox = rng.NextDouble() < 0.45;
                Vector3 size = new Vector3(2.44f, 2.6f, longBox ? 12.2f : 6.06f);
                Vector3 local;
                Quaternion rot;
                if (i < 2)
                {
                    // still on deck (rolled with the ship), turned across the beam between hatches and mast
                    float z = i == 0 ? 22f : -5.5f;
                    local = DeckPoint(z, 0.5f) + new Vector3(0f, 1.3f, 0f);
                    Vector3 wpos = S.MultiplyPoint3x4(local);
                    rot = shipRot * Quaternion.Euler(0f, 90f + PropUtil.Range(rng, -6f, 6f), 0f);
                    Container(batch, cols, wpos, rot, new Vector3(2.44f, 2.6f, 6.06f), i, rng);
                    continue;
                }
                float lx = PropUtil.Range(rng, 9f, 24f) * (rng.NextDouble() < 0.8 ? 1f : -1f);
                float lz = PropUtil.Range(rng, -26f, 28f);
                Vector3 wp2 = pos + yawRot * new Vector3(lx, 0f, lz);
                float g = G(wp2.x, wp2.z);
                rot = Quaternion.Euler(PropUtil.Range(rng, -9f, 9f), yaw + PropUtil.Range(rng, -70f, 70f), PropUtil.Range(rng, -9f, 9f));
                if (i == 9) rot = yawRot * Quaternion.Euler(0f, 0f, -38f);
                Container(batch, cols, new Vector3(wp2.x, g + 1.3f - 0.35f, wp2.z), rot, size, i, rng);
            }

            // ---------------------------------------------------------------- seabed debris
            for (int i = 0; i < 9; i++)
            {
                Vector3 dp = pos + yawRot * new Vector3(PropUtil.Range(rng, -18f, 20f), 0f, PropUtil.Range(rng, -40f, 40f));
                if (Mathf.Abs((Quaternion.Inverse(yawRot) * (dp - pos)).x) < 7f) continue;
                dp.y = G(dp.x, dp.z);
                Material dmat = i % 3 == 0 ? PropMats.DrumRed : i % 3 == 1 ? PropMats.DrumBlue : PropMats.Rust;
                Quaternion q = Quaternion.Euler(0f, PropUtil.Range(rng, 0f, 360f), rng.NextDouble() < 0.6 ? 90f : 0f);
                debris.B(dmat, dp).Cylinder(dp + q * new Vector3(0f, -0.44f, 0f) + Vector3.up * 0.25f, q, 0.29f, 0.29f, 0.88f, 12, true, true, 1.8f, 1f);
            }
            for (int i = 0; i < 7; i++)
            {
                Vector3 dp = pos + yawRot * new Vector3(PropUtil.Range(rng, -16f, 18f), 0f, PropUtil.Range(rng, -38f, 38f));
                dp.y = G(dp.x, dp.z) + 0.05f;
                Quaternion q = Quaternion.Euler(PropUtil.Range(rng, -15f, 15f), PropUtil.Range(rng, 0f, 360f), PropUtil.Range(rng, -15f, 15f));
                debris.B(PropMats.Rust, dp).Box(dp, new Vector3(PropUtil.Range(rng, 1f, 3f), 0.05f, PropUtil.Range(rng, 1f, 2.5f)), q);
            }
            // half buried orange lifeboat
            Vector3 lifeboat = pos + yawRot * new Vector3(-14f, 0f, -18f);
            lifeboat.y = G(lifeboat.x, lifeboat.z) - 0.3f;
            PropBeach.RowingBoat(debris, Matrix4x4.TRS(lifeboat, Quaternion.Euler(8f, yaw + 60f, 25f), Vector3.one * 1.6f), PropMats.BuoyOrange, true);
            // sea whips and sponges around the wreck
            for (int i = 0; i < 70; i++)
            {
                Vector2 o = PropUtil.InDisc(rng, site.radius * 0.8f);
                float x = c.x + o.x, z = c.z + o.y;
                Vector3 lpnt = Quaternion.Inverse(yawRot) * (new Vector3(x, 0f, z) - pos);
                if (Mathf.Abs(lpnt.x) < 8f && Mathf.Abs(lpnt.z) < 38f) continue;
                float g = G(x, z);
                PropReef.Place(debris, rng, new Vector3(x, g, z), PropGround.Normal(x, z, 1f), PropReef.RandomType(rng, 1, 1, 0, 2, 3, 3), 1f, -6f);
            }

            batch.Flush(decor, "Wreck", ShadowCastingMode.On, 0f);
            if (PropRun.Due()) yield return null;
            PropReef.QuietShadows(debris);
            debris.Flush(decor, "WreckDebris", ShadowCastingMode.On, 150f);
        }

        /// Point on the deck: f = 0 starboard edge .. 1 port edge (ship-local).
        static Vector3 DeckPoint(float z, float f)
        {
            float hb = HalfBeam(z);
            float x = Mathf.Lerp(hb, -hb, f);
            float camber = 0.15f * (1f - (x / Mathf.Max(0.01f, hb)) * (x / Mathf.Max(0.01f, hb)));
            return new Vector3(x, DeckY(z) + camber, z);
        }

        static void BoxCol(PropMeshBuilder vis, PropMeshBuilder col, Vector3 c, Vector3 size)
        {
            vis.Box(c, size, Quaternion.identity, 1f);
            col.Box(c, size, Quaternion.identity, 1f);
        }

        static void Cap(PropMeshBuilder b, List<Vector2> sec, float z, Vector3 outward)
        {
            Vector3 cc = Vector3.zero;
            for (int i = 0; i < sec.Count; i++) cc += new Vector3(sec[i].x, sec[i].y, z);
            cc /= sec.Count;
            for (int i = 0; i < sec.Count; i++)
            {
                Vector2 a = sec[i], bb = sec[(i + 1) % sec.Count];
                b.TriAuto(cc, new Vector3(a.x, a.y, z), new Vector3(bb.x, bb.y, z), new Vector2(cc.x, cc.y), new Vector2(a.x, a.y), new Vector2(bb.x, bb.y), outward);
            }
        }

        static void Hatch(PropMeshBuilder b, PropMeshBuilder col, Vector3 c, float w, float len, bool closed)
        {
            const float h = 0.9f, t = 0.25f;
            BoxCol(b, col, c + new Vector3(w * 0.5f, h * 0.5f, 0f), new Vector3(t, h, len));
            BoxCol(b, col, c + new Vector3(-w * 0.5f, h * 0.5f, 0f), new Vector3(t, h, len));
            BoxCol(b, col, c + new Vector3(0f, h * 0.5f, len * 0.5f), new Vector3(w, h, t));
            BoxCol(b, col, c + new Vector3(0f, h * 0.5f, -len * 0.5f), new Vector3(w, h, t));
            if (closed)
            {
                for (int i = 0; i < 4; i++)
                    b.Box(c + new Vector3(0f, h + 0.1f, -len * 0.375f + i * len * 0.25f), new Vector3(w + 0.1f, 0.2f, len * 0.24f), Quaternion.Euler(i == 2 ? 6f : 0f, 0f, 0f));
                col.Box(c + new Vector3(0f, h + 0.1f, 0f), new Vector3(w, 0.2f, len));
            }
        }

        /// Dark jagged opening on the hull side (x = const plane) with bent plate shards around it.
        static void Breach(PropMeshBuilder darkB, PropMeshBuilder rustB, Vector3 c, int side, float radius, System.Random rng)
        {
            const int n = 11;
            var ring = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                float r = radius * PropUtil.Range(rng, 0.7f, 1.15f);
                ring[i] = c + new Vector3(0f, Mathf.Sin(a) * r * 1.1f, Mathf.Cos(a) * r * 1.3f);
            }
            Vector3 outward = new Vector3(side, 0f, 0f);
            for (int i = 0; i < n; i++)
                darkB.TriAuto(c, ring[i], ring[(i + 1) % n], Vector2.zero, Vector2.zero, Vector2.zero, outward);
            for (int i = 0; i < 7; i++)
            {
                Vector3 e = ring[rng.Next(n)];
                Vector3 toC = (c - e).normalized;
                Quaternion q = Quaternion.LookRotation(toC, outward) * Quaternion.Euler(PropUtil.Range(rng, 25f, 60f), 0f, 0f);
                rustB.Box(e + toC * 0.12f + outward * 0.05f, new Vector3(PropUtil.Range(rng, 0.25f, 0.5f), 0.03f, PropUtil.Range(rng, 0.25f, 0.45f)), q);
            }
        }

        static void AddHole(Matrix4x4 S, Quaternion rot, Vector3 local, Vector3 localNormal)
        {
            PropInfo.WreckHoles.Add(S.MultiplyPoint3x4(local));
            PropInfo.WreckHoleNormals.Add(rot * localNormal);
        }

        static void Container(PropBatch batch, Transform cols, Vector3 c, Quaternion r, Vector3 size, int index, System.Random rng)
        {
            Material mat = PropMats.Container(index + rng.Next(PropMats.ContainerCount));
            var b = batch.B(mat);
            b.M = Matrix4x4.identity;
            b.Box(c, size, r, 1f, new Vector2(PropUtil.Range(rng, 0f, 3f), 0f));
            var dm = batch.B(PropMats.DarkMetal);
            dm.M = Matrix4x4.identity;
            Vector3 h = size * 0.5f;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                        dm.Box(c + r * new Vector3(sx * (h.x - 0.08f), sy * (h.y - 0.08f), sz * (h.z - 0.08f)), new Vector3(0.2f, 0.2f, 0.2f), r);
            bool open = index == 5;
            Vector3 door = c + r * new Vector3(0f, 0f, h.z + 0.012f);
            if (open)
            {
                var d = batch.B(PropMats.HoleDark);
                d.M = Matrix4x4.identity;
                d.Box(door, new Vector3(size.x - 0.25f, size.y - 0.25f, 0.01f), r);
                PropInfo.WreckHoles.Add(door + r * Vector3.forward * 0.4f);
                PropInfo.WreckHoleNormals.Add(r * Vector3.forward);
            }
            else
            {
                for (int i = -2; i <= 2; i++)
                    if (i != 0) dm.Box(door + r * new Vector3(i * 0.48f, 0f, 0.03f), new Vector3(0.04f, size.y - 0.3f, 0.04f), r);
                dm.Box(door, new Vector3(0.02f, size.y - 0.2f, 0.02f), r);
            }
            PropCol.Box(cols, c, size, r, "Container");
        }
    }
}
