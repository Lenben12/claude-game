using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Chunky low-poly ~70 m cargo ship "MS CLICKBAIT" lying on the seabed, rolled ~12°: faceted chine hull (dark upper,
    /// rust-red lower), white superstructure, red funnel, yellow crane, hatches, dark hull breaches (moray homes),
    /// spilled containers and debris. Flat colors only; readable silhouette.
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

        /// Chine section from the starboard deck edge, around the keel, to the port deck edge (7 points).
        static void Section(float z, List<Vector2> outPts)
        {
            outPts.Clear();
            float hb = HalfBeam(z), keel = Keel(z), deck = DeckY(z);
            float wl = Mathf.Lerp(keel, deck, 0.5f);
            float bilge = Mathf.Min(wl - 0.2f, keel + (wl - keel) * 0.3f + 1.2f * FB(z));
            outPts.Add(new Vector2(hb, deck));
            outPts.Add(new Vector2(hb, wl));
            outPts.Add(new Vector2(hb * 0.74f, bilge));
            outPts.Add(new Vector2(0f, keel));
            outPts.Add(new Vector2(-hb * 0.74f, bilge));
            outPts.Add(new Vector2(-hb, wl));
            outPts.Add(new Vector2(-hb, deck));
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

            // ---------------------------------------------------------------- hull loft (14 sections, 7-point chine)
            const int NS = 14;
            var secs = new List<Vector2>[NS];
            var zs = new float[NS];
            for (int s = 0; s < NS; s++)
            {
                float t = (float)s / (NS - 1);
                zs[s] = Stern + L * t;
                secs[s] = new List<Vector2>(7);
                Section(zs[s], secs[s]);
            }
            var up = SB(PropMats.HullUpper);
            var lo = SB(PropMats.HullLower);
            for (int s = 0; s < NS - 1; s++)
            {
                var A = secs[s];
                var Bs = secs[s + 1];
                float mid = (DeckY(zs[s]) + Keel(zs[s])) * 0.5f;
                for (int k = 0; k < A.Count - 1; k++)
                {
                    Vector3 p00 = new Vector3(A[k].x, A[k].y, zs[s]);
                    Vector3 p01 = new Vector3(A[k + 1].x, A[k + 1].y, zs[s]);
                    Vector3 p10 = new Vector3(Bs[k].x, Bs[k].y, zs[s + 1]);
                    Vector3 p11 = new Vector3(Bs[k + 1].x, Bs[k + 1].y, zs[s + 1]);
                    Vector3 outward = new Vector3(p00.x + p01.x, (p00.y + p01.y) * 0.5f - mid, 0f);
                    if (outward.sqrMagnitude < 0.01f) outward = Vector3.down;
                    bool upper = k == 0 || k == A.Count - 2;
                    // H.1 entrance A: the port upper panel of strip 5 is replaced by a frame around the torn hole
                    if (s == 5 && k == A.Count - 2) continue;
                    (upper ? up : lo).QuadAuto(p00, p10, p11, p01, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, outward);
                    colB.QuadAuto(p00, p10, p11, p01, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, outward);
                }
            }
            // a white band along the sheer line
            var band = SB(PropMats.Superstructure);
            for (int s = 0; s < NS - 1; s++)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    float hb0 = HalfBeam(zs[s]) * side, hb1 = HalfBeam(zs[s + 1]) * side;
                    Vector3 a0 = new Vector3(hb0 + side * 0.03f, DeckY(zs[s]) - 0.15f, zs[s]), a1 = new Vector3(hb1 + side * 0.03f, DeckY(zs[s + 1]) - 0.15f, zs[s + 1]);
                    Vector3 b0 = a0 - Vector3.up * 0.45f, b1 = a1 - Vector3.up * 0.45f;
                    band.QuadAuto(a0, a1, b1, b0, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, new Vector3(side, 0f, 0f));
                }
            }

            // transom and bow caps
            Cap(up, secs[0], zs[0], Vector3.back);
            Cap(up, secs[NS - 1], zs[NS - 1], Vector3.forward);
            Cap(colB, secs[0], zs[0], Vector3.back);
            Cap(colB, secs[NS - 1], zs[NS - 1], Vector3.forward);

            // deck (flat facets)
            var deck = SB(PropMats.Deck);
            for (int s = 0; s < NS - 1; s++)
            {
                if (s >= 5 && s <= 7) continue;   // midship deck with the open hatch: PropWreckInterior
                for (int k = 0; k < 2; k++)
                {
                    Vector3 a0 = DeckPoint(zs[s], k / 2f), a1 = DeckPoint(zs[s], (k + 1) / 2f);
                    Vector3 b0 = DeckPoint(zs[s + 1], k / 2f), b1 = DeckPoint(zs[s + 1], (k + 1) / 2f);
                    deck.QuadAuto(a0, b0, b1, a1, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector3.up);
                    colB.QuadAuto(a0, b0, b1, a1, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector3.up);
                }
            }
            if (PropRun.Due()) yield return null;

            // name on the stern (crisp block texture)
            float ny = DeckY(Stern) - 1.35f;
            float nz = Stern - 0.06f;
            SB(PropMats.NamePlate).QuadAuto(new Vector3(-2.7f, ny - 0.39f, nz), new Vector3(-2.7f, ny + 0.39f, nz), new Vector3(2.7f, ny + 0.39f, nz), new Vector3(2.7f, ny - 0.39f, nz),
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f), Vector3.back);

            // ---------------------------------------------------------------- superstructure
            var white = SB(PropMats.Superstructure);
            var dark = SB(PropMats.HoleDark);
            BoxCol(white, colB, new Vector3(0f, 7f + 1.25f, -27.75f), new Vector3(9.4f, 3.1f, 11.5f));
            BoxCol(white, colB, new Vector3(0f, 9.8f + 1.3f, -27.75f), new Vector3(8.4f, 2.6f, 9.5f));
            BoxCol(white, colB, new Vector3(0f, 12.4f + 1.2f, -27.5f), new Vector3(11.6f, 2.4f, 6f));
            white.Box(new Vector3(0f, 14.9f, -27.5f), new Vector3(12f, 0.2f, 6.4f));
            for (float x = -3.6f; x <= 3.61f; x += 2.4f)
            {
                dark.Box(new Vector3(x, 8.3f, -22f + 0.02f), new Vector3(1.1f, 0.8f, 0.08f));
                dark.Box(new Vector3(x, 11.2f, -23f + 0.02f), new Vector3(1.1f, 0.8f, 0.08f));
            }
            for (float x = -4.8f; x <= 4.81f; x += 1.6f) dark.Box(new Vector3(x, 13.7f, -24.5f + 0.02f), new Vector3(1.2f, 1.0f, 0.08f));
            for (float z = -31.5f; z <= -23.5f; z += 2.6f)
            {
                for (int sx = -1; sx <= 1; sx += 2)
                {
                    dark.Box(new Vector3(sx * 4.71f, 8.4f, z), new Vector3(0.08f, 0.7f, 0.7f));
                    dark.Box(new Vector3(sx * 4.21f, 11.2f, z), new Vector3(0.08f, 0.7f, 0.7f));
                }
            }
            // funnel (red with a dark top band) and bridge mast
            Quaternion fr = Quaternion.Euler(-6f, 0f, 0f);
            Vector3 fb = new Vector3(0f, 12.4f, -31.6f);
            SB(PropMats.Funnel).Cylinder(fb, fr, 1.4f, 1.2f, 4.2f, 8, false, false, 1f, 1f, 0f, false, 22.5f);
            SB(PropMats.HoleDark).Cylinder(fb + fr * new Vector3(0f, 4.2f, 0f), fr, 1.2f, 1.15f, 0.8f, 8, false, true, 1f, 1f, 0f, false, 22.5f);
            // round 3 (GDD §19): funnel, masts, crane, deck gear, railings and the propeller join the wreck's collider mesh
            colB.Cylinder(fb, fr, 1.4f, 1.15f, 5.0f, 8, true, true, 1f, 1f, 0f, false, 22.5f);
            var rust = SB(PropMats.Rust);
            rust.Rod(new Vector3(0f, 14.9f, -27f), new Vector3(0f, 19.2f, -27f), 0.16f, 0.1f, 6);
            rust.Box(new Vector3(0f, 18.6f, -27f), new Vector3(2.6f, 0.16f, 0.3f));
            colB.Rod(new Vector3(0f, 14.9f, -27f), new Vector3(0f, 19.2f, -27f), 0.16f, 0.1f, 6, true);
            colB.Box(new Vector3(0f, 18.6f, -27f), new Vector3(2.6f, 0.16f, 0.3f));

            // foremast with stays (the thin stays stay passable)
            float fz = 25f, fdeck = DeckY(fz);
            rust.Rod(new Vector3(0f, fdeck, fz), new Vector3(0f, fdeck + 12f, fz), 0.3f, 0.18f, 6, true);
            rust.Rod(new Vector3(-3.2f, fdeck + 9f, fz), new Vector3(3.2f, fdeck + 9f, fz), 0.12f, 0.12f, 4, true);
            colB.Rod(new Vector3(0f, fdeck - 0.3f, fz), new Vector3(0f, fdeck + 12f, fz), 0.3f, 0.18f, 6, true);
            colB.Rod(new Vector3(-3.2f, fdeck + 9f, fz), new Vector3(3.2f, fdeck + 9f, fz), 0.12f, 0.12f, 4, true);
            rust.Rod(new Vector3(0f, fdeck + 11.8f, fz), new Vector3(0f, DeckY(33f) + 0.2f, 33f), 0.05f, 0.05f, 3);
            rust.Rod(new Vector3(0f, fdeck + 11.5f, fz), new Vector3(HalfBeam(20f) - 0.2f, DeckY(20f), 20f), 0.05f, 0.05f, 3);
            rust.Rod(new Vector3(0f, fdeck + 11.5f, fz), new Vector3(-HalfBeam(20f) + 0.2f, DeckY(20f), 20f), 0.05f, 0.05f, 3);

            // crane
            var crane = SB(PropMats.Crane);
            float cz = 6f, cdeck = DeckY(cz);
            crane.Cylinder(new Vector3(2.4f, cdeck - 0.2f, cz), Quaternion.identity, 0.85f, 0.75f, 2.8f, 8, false, true);
            colB.Cylinder(new Vector3(2.4f, cdeck - 0.4f, cz), Quaternion.identity, 0.85f, 0.75f, 3.0f, 8, true, true);
            BoxCol(crane, colB, new Vector3(2.4f, cdeck + 3.5f, cz), new Vector3(2f, 1.8f, 2.2f));
            Vector3 boomA = new Vector3(2.0f, cdeck + 3.7f, cz + 0.6f);
            Vector3 boomDir = new Vector3(-0.35f, 0.5f, 0.79f).normalized;
            Vector3 boomB = boomA + boomDir * 15f;
            crane.Rod(boomA, boomB, 0.34f, 0.2f, 6, true);
            colB.Rod(boomA, boomB, 0.34f, 0.2f, 6, true);
            rust.Rod(boomB, boomB - Vector3.up * 6.5f, 0.05f, 0.05f, 3);
            rust.Torus(boomB - Vector3.up * 6.8f, Quaternion.FromToRotation(Vector3.up, Vector3.right), 0.3f, 0.09f, 6, 3);

            // cargo hatches (one open: dark hole)
            float[] hz = { 16f, 1f, -12f };
            for (int i = 0; i < hz.Length; i++)
            {
                float hdk = DeckY(hz[i]);
                Hatch(rust, colB, new Vector3(0f, hdk, hz[i]), 7f, 8f, i != 1);
                if (i == 1) AddHole(S, shipRot, new Vector3(0f, hdk + 0.3f, hz[i]), Vector3.up);   // the open hatch (entrance B)
            }

            // deck clutter: bollards, vents, winch
            var dm = SB(PropMats.DarkMetal);
            for (int i = 0; i < 6; i++)
            {
                float z = -18f + i * 9f;
                for (int sx = -1; sx <= 1; sx += 2)
                {
                    Vector3 bp = DeckPoint(z, sx > 0 ? 0.08f : 0.92f);
                    dm.Cylinder(bp, Quaternion.identity, 0.2f, 0.2f, 0.45f, 6, false, true);
                    colB.Cylinder(bp - Vector3.up * 0.1f, Quaternion.identity, 0.2f, 0.2f, 0.55f, 6, false, true);
                }
            }
            for (int i = 0; i < 4; i++)
            {
                Vector3 vp = DeckPoint(-19f + i * 11f, i % 2 == 0 ? 0.2f : 0.8f);
                SB(PropMats.Superstructure).Cylinder(vp, Quaternion.identity, 0.24f, 0.24f, 1.2f, 6);
                dm.Cylinder(vp + Vector3.up * 1.2f, Quaternion.Euler(0f, 0f, 90f), 0.3f, 0.3f, 0.12f, 6, true, true);
                colB.Cylinder(vp - Vector3.up * 0.1f, Quaternion.identity, 0.26f, 0.26f, 1.65f, 6, false, true);
            }
            Vector3 wp = DeckPoint(30f, 0.5f);
            dm.Cylinder(wp + new Vector3(-1.2f, 0.5f, 0f), Quaternion.Euler(0f, 0f, -90f), 0.5f, 0.5f, 2.4f, 8, true, true);
            colB.Cylinder(wp + new Vector3(-1.2f, 0.5f, 0f), Quaternion.Euler(0f, 0f, -90f), 0.5f, 0.5f, 2.4f, 8, true, true);

            // railings along both deck edges (some posts missing)
            for (int sx = -1; sx <= 1; sx += 2)
            {
                Vector3 prev = Vector3.zero;
                bool has = false;
                for (float z = -20f; z <= 31f; z += 3.4f)
                {
                    Vector3 e = DeckPoint(z, sx > 0 ? 0.01f : 0.99f) - new Vector3(sx * 0.12f, 0f, 0f);
                    Vector3 top = e + Vector3.up * 1.1f;
                    if (rng.NextDouble() > 0.15) { rust.Box(e + Vector3.up * 0.55f, new Vector3(0.08f, 1.1f, 0.08f)); colB.Box(e + Vector3.up * 0.55f, new Vector3(0.1f, 1.1f, 0.1f)); }
                    if (has && rng.NextDouble() > 0.08) { rust.Beam(prev, top, 0.08f, 0.08f); colB.Beam(prev, top, 0.1f, 0.1f); }
                    prev = top;
                    has = true;
                }
            }

            // propeller and rudder
            Vector3 hub = new Vector3(0f, 2.3f, Stern - 0.4f);
            rust.Ico(hub, new Vector3(0.5f, 0.5f, 0.75f), Quaternion.identity, 0);
            colB.Ico(hub, new Vector3(0.5f, 0.5f, 0.75f), Quaternion.identity, 0);
            for (int i = 0; i < 4; i++)
            {
                Quaternion q = Quaternion.Euler(0f, 0f, i * 90f + 20f) * Quaternion.Euler(0f, 25f, 0f);
                rust.Box(hub + q * new Vector3(0f, 1.0f, 0f), new Vector3(0.75f, 1.6f, 0.1f), q);
                colB.Box(hub + q * new Vector3(0f, 1.0f, 0f), new Vector3(0.75f, 1.6f, 0.12f), q);
            }
            BoxCol(rust, colB, new Vector3(0f, 3.4f, Stern - 1.6f), new Vector3(0.4f, 3.6f, 2.2f));

            // ---------------------------------------------------------------- hull breaches (moray homes)
            float[] holeZ = { -12f, 3f, 14f, -5f, 9f, -24f };
            float[] holeY = { 2.6f, 3.0f, 2.2f, 2.5f, 2.9f, 3.3f };
            int[] holeSide = { -1, -1, -1, 1, 1, -1 };
            for (int i = 0; i < holeZ.Length; i++)
            {
                float hbz = HalfBeam(holeZ[i]);
                Vector3 hc = new Vector3(holeSide[i] * (hbz + 0.05f), holeY[i], holeZ[i]);
                Breach(dark, rust, hc, holeSide[i], PropUtil.Range(rng, 0.65f, 0.95f), rng);
                AddHole(S, shipRot, hc + new Vector3(holeSide[i] * 0.4f, 0f, 0f), new Vector3(holeSide[i], 0f, 0f));
            }

            // ---------------------------------------------------------------- H.1 interior (entrance A frame, deck, rooms)
            PropWreckInterior.HullFrame(up, colB, zs[5], zs[6], rng);
            try { PropWreckInterior.Build(batch, colB, S, shipRot, decor, cols, rng, zs[5], zs[8]); }
            catch (System.Exception e) { Debug.LogError("Sealebrities props: wreck interior failed: " + e); }
            PropInfo.SetSpot("wreck_axis", pos, yaw);
            if (PropRun.Due()) yield return null;

            // a few air bubbles leaking from the bridge
            PropFX.Bubbles(decor, S.MultiplyPoint3x4(new Vector3(-2f, 14.9f, -26f)), 1.5f, 0.3f);

            // collider of hull + deck + superstructure
            var colMesh = colB.ToMesh("WreckCollider", false);
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
                PropReef.Place(debris, rng, wpnt, shipRot * Vector3.up, type, 0.75f, -4f, cols);
            }
            if (PropRun.Due()) yield return null;

            // ---------------------------------------------------------------- L.3 growth on the hull plates
            // encrusting pads and soft corals on both sides (the chine points of the hull loft, not the deck) – own seed so
            // the deck growth and everything after keeps its layout; nothing below the seabed, the entrance hole stays free
            var hrng = new System.Random(1986);
            var hullSec = new List<Vector2>(7);
            int hullGrowth = 0;
            for (int i = 0; i < 140 && hullGrowth < 90; i++)
            {
                float z = PropUtil.Range(hrng, Stern + 3f, Stern + L - 4f);
                Section(z, hullSec);
                int side = hrng.NextDouble() < 0.5 ? 1 : -1;
                // segment 0 (deck edge → waterline) or 1 (waterline → bilge) of this side
                int seg = hrng.NextDouble() < 0.6 ? 0 : 1;
                int a = side > 0 ? seg : hullSec.Count - 1 - seg, b = side > 0 ? seg + 1 : hullSec.Count - 2 - seg;
                if (side < 0 && seg == 0 && z >= zs[5] - 0.5f && z <= zs[6] + 0.5f) continue;   // H.1 entrance A
                float t = PropUtil.Range(hrng, 0.15f, 0.85f);
                Vector2 q = Vector2.Lerp(hullSec[a], hullSec[b], t);
                Vector2 e = hullSec[b] - hullSec[a];
                Vector3 nLocal = new Vector3(e.y, -e.x, 0f).normalized;
                if (Vector3.Dot(nLocal, new Vector3(side, 0f, 0f)) < 0f) nLocal = -nLocal;
                Vector3 lp = new Vector3(q.x, q.y, z) + nLocal * 0.02f;
                Vector3 wpnt = S.MultiplyPoint3x4(lp);
                if (wpnt.y < G(wpnt.x, wpnt.z) + 0.3f) continue;   // the buried side of the rolled hull
                Vector3 wn = shipRot * nLocal;
                var kind = hrng.NextDouble() < 0.6 ? ReefColony.Encrust : ReefColony.SoftPuff;
                if (PropReefs.Colony(debris, null, hrng, kind, hrng.Next(PropReefKit.Colors), wpnt, wn, PropUtil.Range(hrng, 0.35f, 0.75f), -4f)) hullGrowth++;
            }
            if (PropRun.Due()) yield return null;

            // ---------------------------------------------------------------- spilled containers
            for (int i = 0; i < 10; i++)
            {
                bool longBox = rng.NextDouble() < 0.45;
                Vector3 size = new Vector3(2.44f, 2.6f, longBox ? 12.2f : 6.06f);
                Quaternion rot;
                if (i < 2)
                {
                    // still on deck (rolled with the ship), turned across the beam between hatches and mast
                    float z = i == 0 ? 22f : -5.5f;
                    Vector3 local = DeckPoint(z, 0.5f) + new Vector3(0f, 1.3f, 0f);
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
                debris.B(dmat, dp).Cylinder(dp + q * new Vector3(0f, -0.44f, 0f) + Vector3.up * 0.25f, q, 0.29f, 0.29f, 0.88f, 8, true, true);
                Vector3 dax = q * Vector3.up * 0.15f;
                PropSolid.Segment(cols, dp + Vector3.up * 0.25f - dax, dp + Vector3.up * 0.25f + dax, 0.29f, "Barrel");
            }
            for (int i = 0; i < 7; i++)
            {
                Vector3 dp = pos + yawRot * new Vector3(PropUtil.Range(rng, -16f, 18f), 0f, PropUtil.Range(rng, -38f, 38f));
                dp.y = G(dp.x, dp.z) + 0.05f;
                Quaternion q = Quaternion.Euler(PropUtil.Range(rng, -15f, 15f), PropUtil.Range(rng, 0f, 360f), PropUtil.Range(rng, -15f, 15f));
                debris.B(PropMats.Rust, dp).Box(dp, new Vector3(PropUtil.Range(rng, 1f, 3f), 0.08f, PropUtil.Range(rng, 1f, 2.5f)), q);
            }
            // half buried orange lifeboat
            Vector3 lifeboat = pos + yawRot * new Vector3(-14f, 0f, -18f);
            lifeboat.y = G(lifeboat.x, lifeboat.z) - 0.3f;
            var lifeTrs = Matrix4x4.TRS(lifeboat, Quaternion.Euler(8f, yaw + 60f, 25f), Vector3.one * 1.6f);
            PropBeach.RowingBoat(debris, lifeTrs, PropMats.BuoyOrange, true);
            PropSolid.RowingBoat(cols, lifeTrs, true);
            // sea whips and sponges around the wreck
            for (int i = 0; i < 70; i++)
            {
                Vector2 o = PropUtil.InDisc(rng, site.radius * 0.8f);
                float x = c.x + o.x, z = c.z + o.y;
                Vector3 lpnt = Quaternion.Inverse(yawRot) * (new Vector3(x, 0f, z) - pos);
                if (Mathf.Abs(lpnt.x) < 8f && Mathf.Abs(lpnt.z) < 38f) continue;
                float g = G(x, z);
                PropReef.Place(debris, rng, new Vector3(x, g, z), PropGround.Normal(x, z, 1f), PropReef.RandomType(rng, 1, 1, 0, 2, 3, 3), 1f, -6f, cols);
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
            vis.Box(c, size, Quaternion.identity);
            col.Box(c, size, Quaternion.identity);
        }

        static void Cap(PropMeshBuilder b, List<Vector2> sec, float z, Vector3 outward)
        {
            Vector3 cc = Vector3.zero;
            for (int i = 0; i < sec.Count; i++) cc += new Vector3(sec[i].x, sec[i].y, z);
            cc /= sec.Count;
            for (int i = 0; i < sec.Count; i++)
            {
                Vector2 a = sec[i], bb = sec[(i + 1) % sec.Count];
                b.Face3(cc, new Vector3(a.x, a.y, z), new Vector3(bb.x, bb.y, z), outward);
            }
        }

        static void Hatch(PropMeshBuilder b, PropMeshBuilder col, Vector3 c, float w, float len, bool closed)
        {
            const float h = 0.9f, t = 0.3f;
            BoxCol(b, col, c + new Vector3(w * 0.5f, h * 0.5f, 0f), new Vector3(t, h, len));
            BoxCol(b, col, c + new Vector3(-w * 0.5f, h * 0.5f, 0f), new Vector3(t, h, len));
            BoxCol(b, col, c + new Vector3(0f, h * 0.5f, len * 0.5f), new Vector3(w, h, t));
            BoxCol(b, col, c + new Vector3(0f, h * 0.5f, -len * 0.5f), new Vector3(w, h, t));
            if (closed)
            {
                for (int i = 0; i < 3; i++)
                    b.Box(c + new Vector3(0f, h + 0.12f, -len / 3f + i * len / 3f), new Vector3(w + 0.1f, 0.24f, len / 3f - 0.1f), Quaternion.Euler(i == 2 ? 6f : 0f, 0f, 0f));
                col.Box(c + new Vector3(0f, h + 0.1f, 0f), new Vector3(w, 0.2f, len));
            }
        }

        /// Dark jagged opening on the hull side (x = const plane) with a few bent plate shards around it.
        static void Breach(PropMeshBuilder darkB, PropMeshBuilder rustB, Vector3 c, int side, float radius, System.Random rng)
        {
            const int n = 7;
            var ring = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                float r = radius * PropUtil.Range(rng, 0.7f, 1.15f);
                ring[i] = c + new Vector3(0f, Mathf.Sin(a) * r * 1.1f, Mathf.Cos(a) * r * 1.3f);
            }
            Vector3 outward = new Vector3(side, 0f, 0f);
            for (int i = 0; i < n; i++) darkB.Face3(c, ring[i], ring[(i + 1) % n], outward);
            for (int i = 0; i < 4; i++)
            {
                Vector3 e = ring[rng.Next(n)];
                Vector3 toC = (c - e);
                toC = toC.sqrMagnitude > 1e-6f ? toC.normalized : Vector3.up;
                Quaternion q = Quaternion.LookRotation(toC, outward) * Quaternion.Euler(PropUtil.Range(rng, 25f, 60f), 0f, 0f);
                rustB.Box(e + toC * 0.12f + outward * 0.06f, new Vector3(PropUtil.Range(rng, 0.3f, 0.55f), 0.05f, PropUtil.Range(rng, 0.3f, 0.5f)), q);
            }
        }

        static void AddHole(Matrix4x4 S, Quaternion rot, Vector3 local, Vector3 localNormal)
        {
            PropInfo.WreckHoles.Add(S.MultiplyPoint3x4(local));
            PropInfo.WreckHoleNormals.Add(rot * localNormal);
        }

        /// Flat-colored container: body, darker end walls with door bars, a few ribs along the sides.
        static void Container(PropBatch batch, Transform cols, Vector3 c, Quaternion r, Vector3 size, int index, System.Random rng)
        {
            int ci = index + rng.Next(PropMats.ContainerCount);
            var b = batch.B(PropMats.Container(ci));
            b.M = Matrix4x4.identity;
            b.Box(c, size, r);
            var e = batch.B(PropMats.ContainerEnd(ci));
            e.M = Matrix4x4.identity;
            Vector3 h = size * 0.5f;
            for (int sz = -1; sz <= 1; sz += 2)
                e.Box(c + r * new Vector3(0f, 0f, sz * (h.z - 0.06f)), new Vector3(size.x + 0.08f, size.y + 0.08f, 0.14f), r);
            int ribs = size.z > 8f ? 5 : 3;
            for (int i = 1; i <= ribs; i++)
            {
                float z = -h.z + i * size.z / (ribs + 1);
                e.Box(c + r * new Vector3(0f, 0f, z), new Vector3(size.x + 0.05f, size.y + 0.05f, 0.1f), r);
            }
            bool open = index == 5;
            Vector3 door = c + r * new Vector3(0f, 0f, h.z + 0.02f);
            if (open)
            {
                var d = batch.B(PropMats.HoleDark);
                d.M = Matrix4x4.identity;
                d.Box(door + r * new Vector3(0f, 0f, 0.01f), new Vector3(size.x - 0.3f, size.y - 0.3f, 0.02f), r);
                PropInfo.WreckHoles.Add(door + r * Vector3.forward * 0.4f);
                PropInfo.WreckHoleNormals.Add(r * Vector3.forward);
            }
            else
            {
                var dm = batch.B(PropMats.DarkMetal);
                dm.M = Matrix4x4.identity;
                for (int i = -1; i <= 1; i += 2)
                    dm.Box(door + r * new Vector3(i * 0.5f, 0f, 0.04f), new Vector3(0.06f, size.y - 0.4f, 0.06f), r);
            }
            PropCol.Box(cols, c, size, r, "Container");
        }
    }
}
