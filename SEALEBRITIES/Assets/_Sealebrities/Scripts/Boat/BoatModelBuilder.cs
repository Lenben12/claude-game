using UnityEngine;

namespace Sealebrities
{
    /// References to the animated parts and anchor points of a built boat (all local to the boat root).
    public class BoatParts
    {
        public Transform seat, steeringWheel, outboardPivot, propeller, throttleLever, radar, anchor, exhaust;
        public Quaternion wheelBaseRot, leverBaseRot;
        public Vector3 exitLocal, rescueLocal, ladderDeckLocal, propLocal, sternWakeLocal, anchorStowLocal, rollerLocal;
        public Vector3 bowSprayLeftLocal, bowSprayRightLocal;
    }

    /// Builds the "Rostlaube": a ~7.5 m rusty teal steel fishing boat with wooden deck, wheelhouse, outboard,
    /// oil drums, tire fenders, railings, a stern dive ladder and an anchor winch.
    public static class BoatModelBuilder
    {
        // Wheelhouse: narrow and set slightly aft so the side walkways stay >= 0.65 m wide (character capsule).
        public const float WheelhouseHalfWidth = 0.55f;
        const float WT = 0.06f, CabZ0 = -1.1f, CabZ1 = 0.7f, CabY1 = 2.35f;
        const float CZ = CabZ1 - 1.1f;   // z offset of all helm/roof fittings relative to their design positions
        static readonly Vector3 JunkCratePos = new Vector3(-0.68f, BoatHull.DeckY + 0.17f, -1.85f);
        static float Y0 => BoatHull.DeckY;

        /// Rounded box / superellipsoid (e < 1 is boxier) via the grid builder.
        public static void SuperEllipsoid(BoatMeshBuilder b, Vector3 c, Vector3 r, float e1, float e2, int seg, int rings, Quaternion rot)
        {
            var p = new Vector3[rings + 1, seg + 1];
            var uv = new Vector2[rings + 1, seg + 1];
            for (int i = 0; i <= rings; i++)
            {
                float eta = Mathf.Lerp(-Mathf.PI * 0.5f, Mathf.PI * 0.5f, i / (float)rings);
                float ce = F(Mathf.Cos(eta), e1), se = F(Mathf.Sin(eta), e1);
                for (int j = 0; j <= seg; j++)
                {
                    float om = Mathf.Lerp(-Mathf.PI, Mathf.PI, j / (float)seg);
                    Vector3 q = new Vector3(r.x * ce * F(Mathf.Cos(om), e2), r.y * se, r.z * ce * F(Mathf.Sin(om), e2));
                    p[i, j] = c + rot * q;
                    uv[i, j] = new Vector2(j / (float)seg, i / (float)rings);
                }
            }
            b.Grid(p, uv, c, true);
        }

        static float F(float w, float e) => Mathf.Sign(w) * Mathf.Pow(Mathf.Abs(w), e);

        public static BoatParts Build(Transform root, BoatController boat)
        {
            var parts = new BoatParts();
            var set = new BoatMeshSet();

            // ---------------------------------------------------------- hull, deck, bulwarks
            BoatHull.BuildOuter(set[BoatMats.Hull]);
            set[BoatMats.Hull].RecalculateNormals();
            BoatHull.BuildInner(set[BoatMats.Cabin]);
            BoatHull.BuildDeck(set[BoatMats.Deck]);
            BoatHull.BuildGunwale(set[BoatMats.DeckDark]);

            BuildWheelhouse(set, root, parts);
            BuildDeckProps(set);
            BuildStern(set, root, parts);
            BuildBow(set, root, parts);
            BuildTires(set);

            set.Build("BoatBody", root, true);

            parts.exitLocal = new Vector3(0f, Y0 + 0.08f, CabZ0 - 0.25f);
            parts.rescueLocal = new Vector3(0f, Y0 + 1f, -1.9f);
            parts.ladderDeckLocal = new Vector3(0.35f, Y0 + 0.08f, -3.15f);
            parts.sternWakeLocal = new Vector3(0f, 0f, -4.4f);
            parts.bowSprayLeftLocal = new Vector3(-0.55f, 0.15f, 2.6f);
            parts.bowSprayRightLocal = new Vector3(0.55f, 0.15f, 2.6f);

            BuildColliders(root, boat, parts);
            return parts;
        }

        // ------------------------------------------------------------------ wheelhouse + helm
        static void BuildWheelhouse(BoatMeshSet set, Transform root, BoatParts parts)
        {
            var cab = set[BoatMats.Cabin];
            float WX = WheelhouseHalfWidth;
            float fz = CabZ1 - WT * 0.5f;
            // front wall with two windows
            cab.Box(new Vector3(0f, (Y0 + 1.25f) * 0.5f, fz), new Vector3(2f * WX, 1.25f - Y0, WT));
            cab.Box(new Vector3(0f, (1.9f + CabY1) * 0.5f, fz), new Vector3(2f * WX, CabY1 - 1.9f, WT));
            cab.Box(new Vector3(-WX + 0.03f, 1.575f, fz), new Vector3(0.06f, 0.65f, WT));
            cab.Box(new Vector3(0f, 1.575f, fz), new Vector3(0.08f, 0.65f, WT));
            cab.Box(new Vector3(WX - 0.03f, 1.575f, fz), new Vector3(0.06f, 0.65f, WT));
            // side walls with long windows
            float midZ = (CabZ0 + CabZ1) * 0.5f, lenZ = CabZ1 - CabZ0;
            float winZ0 = CabZ0 + 0.25f, winZ1 = CabZ1 - 0.25f, winC = (winZ0 + winZ1) * 0.5f, winL = winZ1 - winZ0;
            for (int s = -1; s <= 1; s += 2)
            {
                float x = s * (WX - WT * 0.5f);
                cab.Box(new Vector3(x, (Y0 + 1.2f) * 0.5f, midZ), new Vector3(WT, 1.2f - Y0, lenZ));
                cab.Box(new Vector3(x, (1.85f + CabY1) * 0.5f, midZ), new Vector3(WT, CabY1 - 1.85f, lenZ));
                cab.Box(new Vector3(x, 1.525f, (CabZ0 + winZ0) * 0.5f), new Vector3(WT, 0.65f, 0.25f));
                cab.Box(new Vector3(x, 1.525f, (winZ1 + CabZ1) * 0.5f), new Vector3(WT, 0.65f, 0.25f));
                set[BoatMats.Glass].Box(new Vector3(x, 1.525f, winC), new Vector3(0.012f, 0.65f, winL));
                // window frames (rubber seal)
                var rub = set[BoatMats.Rubber];
                rub.Box(new Vector3(x, 1.2f, winC), new Vector3(WT + 0.02f, 0.025f, winL));
                rub.Box(new Vector3(x, 1.85f, winC), new Vector3(WT + 0.02f, 0.025f, winL));
            }
            float paneW = WX - 0.1f, paneX = 0.04f + paneW * 0.5f;
            set[BoatMats.Glass].Box(new Vector3(-paneX, 1.575f, fz), new Vector3(paneW, 0.65f, 0.012f));
            set[BoatMats.Glass].Box(new Vector3(paneX, 1.575f, fz), new Vector3(paneW, 0.65f, 0.012f));
            set[BoatMats.Rubber].Box(new Vector3(0f, 1.25f, fz), new Vector3(2f * WX, 0.025f, WT + 0.02f));
            set[BoatMats.Rubber].Box(new Vector3(0f, 1.9f, fz), new Vector3(2f * WX, 0.025f, WT + 0.02f));
            // back header beam and roof
            cab.Box(new Vector3(0f, (2.15f + CabY1) * 0.5f, CabZ0 + WT * 0.5f), new Vector3(2f * WX, CabY1 - 2.15f, WT));
            float rz0 = CabZ0 - 0.4f, rz1 = CabZ1 + 0.15f;
            cab.Box(new Vector3(0f, CabY1 + 0.04f, (rz0 + rz1) * 0.5f), new Vector3(2f * WX + 0.2f, 0.08f, rz1 - rz0));
            set[BoatMats.Rubber].Box(new Vector3(0f, CabY1 + 0.005f, (rz0 + rz1) * 0.5f), new Vector3(2f * WX + 0.22f, 0.03f, rz1 - rz0 + 0.02f));

            // console + instrument panel
            cab.Box(new Vector3(0f, (Y0 + 1.0f) * 0.5f, 0.92f + CZ), new Vector3(2f * (WX - WT), 1.0f - Y0, 0.24f));
            Quaternion panelRot = Quaternion.Euler(-25f, 0f, 0f);
            Vector3 panelC = new Vector3(0f, 1.03f, 0.9f + CZ);
            set[BoatMats.Engine].Box(panelC, new Vector3(2f * (WX - WT) - 0.02f, 0.04f, 0.3f), panelRot);
            Vector3 pn = panelRot * Vector3.up;
            float[] gx = { -0.36f, -0.22f, 0.22f };
            for (int i = 0; i < gx.Length; i++)
            {
                Vector3 gc = panelC + panelRot * new Vector3(gx[i], 0.021f, 0.02f);
                set[BoatMats.Chrome].Cylinder(gc - pn * 0.005f, gc + pn * 0.012f, 0.05f, 0.05f, 14, false, true);
                set[BoatMats.Gauge].Disc(gc + pn * 0.0145f, pn, 0.04f, 14);
            }
            // compass on the dash
            set[BoatMats.Engine].Cylinder(new Vector3(0f, 1.0f, 1.0f + CZ), new Vector3(0f, 1.06f, 1.0f + CZ), 0.06f, 0.055f, 14);
            set[BoatMats.Glass].Ellipsoid(new Vector3(0f, 1.06f, 1.0f + CZ), new Vector3(0.05f, 0.04f, 0.05f), Quaternion.identity, 14, 6, 0f, 90f);
            // throttle housing
            set[BoatMats.Engine].Box(new Vector3(0.33f, 1.02f, 0.86f + CZ), new Vector3(0.09f, 0.07f, 0.16f));

            // steering wheel (animated)
            {
                var ws = new BoatMeshSet();
                ws[BoatMats.Engine].Torus(Vector3.zero, Quaternion.Euler(90f, 0f, 0f), 0.19f, 0.017f, 28, 8);
                ws[BoatMats.Chrome].Cylinder(new Vector3(0f, 0f, -0.025f), new Vector3(0f, 0f, 0.26f), 0.035f, 0.02f, 12);
                for (int k = 0; k < 3; k++)
                {
                    float a = (90f + k * 120f) * Mathf.Deg2Rad;
                    ws[BoatMats.Chrome].Beam(Vector3.zero, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 0.18f, 0.022f, 0.012f);
                }
                ws[BoatMats.Engine].Cylinder(new Vector3(0.17f, 0f, 0f), new Vector3(0.17f, 0f, -0.07f), 0.016f, 0.016f, 8);
                var go = ws.Build("SteeringWheel", root, new Vector3(0f, 1.08f, 0.72f + CZ), Quaternion.Euler(30f, 0f, 0f), true);
                parts.steeringWheel = go.transform;
                parts.wheelBaseRot = go.transform.localRotation;
            }
            // throttle lever (animated)
            {
                var ls = new BoatMeshSet();
                ls[BoatMats.Chrome].Cylinder(Vector3.zero, new Vector3(0f, 0.16f, 0f), 0.011f, 0.011f, 8);
                ls[BoatMats.BobberRed].Sphere(new Vector3(0f, 0.17f, 0f), 0.028f, 10, 6);
                var go = ls.Build("ThrottleLever", root, new Vector3(0.33f, 1.05f, 0.86f + CZ), Quaternion.identity, false);
                parts.throttleLever = go.transform;
                parts.leverBaseRot = go.transform.localRotation;
            }

            // driver seat
            var metal = set[BoatMats.Metal];
            metal.Cylinder(new Vector3(0f, Y0, 0.25f + CZ), new Vector3(0f, 0.7f, 0.25f + CZ), 0.035f, 0.035f, 10);
            metal.Cylinder(new Vector3(0f, Y0, 0.25f + CZ), new Vector3(0f, Y0 + 0.02f, 0.25f + CZ), 0.16f, 0.16f, 14, false, true);
            set[BoatMats.Cushion].Box(new Vector3(0f, 0.75f, 0.25f + CZ), new Vector3(0.46f, 0.1f, 0.42f), 2f);
            set[BoatMats.Cushion].Box(new Vector3(0f, 1.0f, 0.06f + CZ), new Vector3(0.44f, 0.36f, 0.07f), 2f);
            metal.Beam(new Vector3(0f, 0.7f, 0.1f + CZ), new Vector3(0f, 0.86f, 0.06f + CZ), 0.04f, 0.03f);
            var seat = new GameObject("DriverSeat").transform;
            seat.SetParent(root, false);
            seat.localPosition = new Vector3(0f, Y0 - 0.25f, 0.3f + CZ);
            seat.localRotation = Quaternion.identity;
            parts.seat = seat;

            // roof gear: mast, radar, masthead light, antenna, searchlight, nav lights
            float mz = 0.1f + CZ;
            metal.Cylinder(new Vector3(0f, CabY1 + 0.08f, mz), new Vector3(0f, CabY1 + 1.05f, mz), 0.035f, 0.03f, 10);
            set[BoatMats.Engine].Box(new Vector3(0f, CabY1 + 0.84f, mz), new Vector3(0.16f, 0.1f, 0.16f));
            set[BoatMats.NavWhite].Sphere(new Vector3(0f, CabY1 + 1.1f, mz), 0.05f, 10, 6);
            set[BoatMats.Rubber].Cylinder(new Vector3(0.5f, CabY1 + 0.08f, -0.9f + CZ), new Vector3(0.5f, CabY1 + 1.9f, -0.9f + CZ), 0.01f, 0.004f, 6);
            metal.Box(new Vector3(-0.42f, CabY1 + 0.14f, 0.95f + CZ), new Vector3(0.06f, 0.12f, 0.06f));
            metal.Cylinder(new Vector3(-0.42f, CabY1 + 0.26f, 0.85f + CZ), new Vector3(-0.42f, CabY1 + 0.26f, 1.1f + CZ), 0.08f, 0.09f, 14, true, false);
            set[BoatMats.NavWhite].Disc(new Vector3(-0.42f, CabY1 + 0.26f, 1.101f + CZ), Vector3.forward, 0.08f, 14);
            set[BoatMats.NavRed].Box(new Vector3(-WX - 0.03f, 2.05f, 0.95f + CZ), new Vector3(0.05f, 0.08f, 0.12f));
            set[BoatMats.NavGreen].Box(new Vector3(WX + 0.03f, 2.05f, 0.95f + CZ), new Vector3(0.05f, 0.08f, 0.12f));
            {
                var rs = new BoatMeshSet();
                rs[BoatMats.Cabin].Box(Vector3.zero, new Vector3(0.9f, 0.07f, 0.12f), 2f);
                rs[BoatMats.Engine].Box(new Vector3(0f, -0.06f, 0f), new Vector3(0.08f, 0.06f, 0.08f));
                var go = rs.Build("Radar", root, new Vector3(0f, CabY1 + 0.96f, mz), Quaternion.identity, true);
                parts.radar = go.transform;
            }

            // life ring on the port wall (orange/white bands)
            Quaternion ringRot = Quaternion.Euler(0f, 0f, 90f);
            Vector3 ringC = new Vector3(-WX - 0.07f, 1.5f, -0.15f + CZ);
            for (int k = 0; k < 8; k++)
                set[k % 2 == 0 ? BoatMats.LifeOrange : BoatMats.White].Torus(ringC, ringRot, 0.26f, 0.055f, 32, 10, k * 4, k * 4 + 4);
        }

        // ------------------------------------------------------------------ aft deck props
        static void BuildDeckProps(BoatMeshSet set)
        {
            // oil drums
            DrumAt(set, BoatMats.DrumBlue, new Vector3(-0.68f, Y0, -3.0f));
            DrumAt(set, BoatMats.DrumOrange, new Vector3(-0.68f, Y0, -2.38f));
            // cooler box
            set[BoatMats.Cooler].Box(new Vector3(0.62f, Y0 + 0.2f, -2.4f), new Vector3(0.55f, 0.4f, 0.38f), 2f);
            set[BoatMats.CoolerLid].Box(new Vector3(0.62f, Y0 + 0.425f, -2.4f), new Vector3(0.57f, 0.05f, 0.4f), 2f);
            set[BoatMats.Engine].Box(new Vector3(0.62f, Y0 + 0.3f, -2.62f), new Vector3(0.2f, 0.04f, 0.04f));
            // coiled rope
            for (int k = 0; k < 4; k++)
                set[BoatMats.Rope].Torus(new Vector3(0.45f, Y0 + 0.025f + k * 0.035f, -1.55f), Quaternion.identity, 0.2f - k * 0.012f, 0.02f, 20, 6);
            set[BoatMats.Rope].Cylinder(new Vector3(0.62f, Y0 + 0.15f, -1.6f), new Vector3(0.7f, Y0 + 0.02f, -2.0f), 0.02f, 0.02f, 6);
            // dive tanks strapped to the starboard bulwark
            for (int k = 0; k < 2; k++)
            {
                float z = -1.98f + k * 0.24f;
                Vector3 b0 = new Vector3(1.0f, Y0, z);
                set[BoatMats.Tank].Cylinder(b0, b0 + Vector3.up * 0.62f, 0.09f, 0.09f, 14, false, false);
                set[BoatMats.Tank].Ellipsoid(b0 + Vector3.up * 0.62f, new Vector3(0.09f, 0.07f, 0.09f), Quaternion.identity, 14, 5, 0f, 90f);
                set[BoatMats.Chrome].Cylinder(b0 + Vector3.up * 0.68f, b0 + Vector3.up * 0.76f, 0.025f, 0.025f, 8);
                set[BoatMats.Engine].Box(b0 + Vector3.up * 0.78f, new Vector3(0.08f, 0.04f, 0.04f));
            }
            set[BoatMats.Strap].Box(new Vector3(1.0f, Y0 + 0.42f, -1.86f), new Vector3(0.2f, 0.04f, 0.46f));
            // a small crate of junk
            set[BoatMats.Crate].Box(JunkCratePos, new Vector3(0.45f, 0.34f, 0.36f), Quaternion.Euler(0f, 12f, 0f), 1f, true);
        }

        static void DrumAt(BoatMeshSet set, Material m, Vector3 bottom)
        {
            const float r = 0.29f, h = 0.88f;
            set[m].Cylinder(bottom, bottom + Vector3.up * h, r, r, 22, false, true, 1f / h);
            set[m].Torus(bottom + Vector3.up * 0.3f, Quaternion.identity, r, 0.012f, 22, 5);
            set[m].Torus(bottom + Vector3.up * 0.58f, Quaternion.identity, r, 0.012f, 22, 5);
            set[m].Torus(bottom + Vector3.up * (h - 0.005f), Quaternion.identity, r - 0.01f, 0.014f, 22, 5);
            set[BoatMats.Metal].Cylinder(bottom + new Vector3(0.12f, h, 0.05f), bottom + new Vector3(0.12f, h + 0.02f, 0.05f), 0.03f, 0.03f, 8);
        }

        // ------------------------------------------------------------------ outboard, platform, ladder
        static void BuildStern(BoatMeshSet set, Transform root, BoatParts parts)
        {
            float zt = BoatHull.Z(0f);
            set[BoatMats.Metal].Box(new Vector3(0f, 0.44f, zt - 0.07f), new Vector3(0.3f, 0.22f, 0.14f));

            // outboard motor on a steering pivot
            var os = new BoatMeshSet();
            os[BoatMats.EngineGrey].Box(new Vector3(0f, -0.02f, 0.02f), new Vector3(0.14f, 0.2f, 0.12f));
            SuperEllipsoid(os[BoatMats.Engine], new Vector3(0f, 0.32f, -0.13f), new Vector3(0.22f, 0.2f, 0.3f), 0.45f, 0.45f, 20, 12, Quaternion.identity);
            SuperEllipsoid(os[BoatMats.EngineGrey], new Vector3(0f, 0.13f, -0.11f), new Vector3(0.2f, 0.08f, 0.27f), 0.3f, 0.5f, 18, 6, Quaternion.identity);
            os[BoatMats.LifeOrange].Box(new Vector3(0f, 0.3f, 0.165f), new Vector3(0.2f, 0.06f, 0.012f));
            SuperEllipsoid(os[BoatMats.EngineGrey], new Vector3(0f, -0.39f, -0.02f), new Vector3(0.07f, 0.45f, 0.11f), 0.15f, 0.7f, 14, 6, Quaternion.identity);
            os[BoatMats.EngineGrey].Box(new Vector3(0f, -0.8f, -0.05f), new Vector3(0.3f, 0.02f, 0.38f));
            os[BoatMats.EngineGrey].Ellipsoid(new Vector3(0f, -0.93f, 0f), new Vector3(0.07f, 0.07f, 0.2f), Quaternion.identity, 14, 8);
            os[BoatMats.EngineGrey].Box(new Vector3(0f, -1.05f, 0.04f), new Vector3(0.025f, 0.16f, 0.16f));
            os[BoatMats.Metal].Cylinder(new Vector3(0f, 0.42f, -0.36f), new Vector3(0f, 0.5f, -0.42f), 0.03f, 0.035f, 8, false, true);
            var outboard = os.Build("Outboard", root, new Vector3(0f, 0.5f, zt - 0.2f), Quaternion.identity, true);
            parts.outboardPivot = outboard.transform;

            var ex = new GameObject("Exhaust").transform;
            ex.SetParent(outboard.transform, false);
            ex.localPosition = new Vector3(0f, 0.52f, -0.43f);
            parts.exhaust = ex;

            var ps = new BoatMeshSet();
            ps[BoatMats.Steel].Cylinder(new Vector3(0f, 0f, 0.03f), new Vector3(0f, 0f, -0.07f), 0.035f, 0.025f, 10);
            for (int k = 0; k < 3; k++)
            {
                Quaternion r = Quaternion.Euler(0f, 0f, k * 120f) * Quaternion.Euler(0f, 28f, 0f);
                ps[BoatMats.Steel].Box(Quaternion.Euler(0f, 0f, k * 120f) * new Vector3(0f, 0.095f, -0.02f), new Vector3(0.08f, 0.13f, 0.01f), r);
            }
            var prop = ps.Build("Propeller", outboard.transform, new Vector3(0f, -0.93f, -0.22f), Quaternion.identity, false);
            parts.propeller = prop.transform;
            parts.propLocal = new Vector3(0f, 0.5f - 0.93f, zt - 0.42f);

            // swim platform (starboard side of the motor) with brackets
            var dd = set[BoatMats.DeckDark];
            dd.Box(new Vector3(0.72f, 0.09f, zt - 0.23f), new Vector3(0.76f, 0.08f, 0.46f), 1.5f);
            set[BoatMats.Metal].Beam(new Vector3(0.45f, -0.15f, zt - 0.01f), new Vector3(0.45f, 0.05f, zt - 0.4f), 0.04f, 0.04f);
            set[BoatMats.Metal].Beam(new Vector3(1.0f, -0.15f, zt - 0.01f), new Vector3(1.0f, 0.05f, zt - 0.4f), 0.04f, 0.04f);

            // dive ladder into the water + grab rails
            var st = set[BoatMats.Steel];
            float lz = zt - 0.42f;
            for (int s = 0; s < 2; s++)
            {
                float x = s == 0 ? 0.52f : 0.92f;
                st.Cylinder(new Vector3(x, 0.06f, lz), new Vector3(x, -1.25f, lz - 0.03f), 0.018f, 0.018f, 8);
                st.Cylinder(new Vector3(x, 0.13f, lz + 0.02f), new Vector3(x, 0.95f, lz + 0.1f), 0.018f, 0.018f, 8);
                st.Cylinder(new Vector3(x, 0.95f, lz + 0.1f), new Vector3(x, 0.98f, lz + 0.3f), 0.018f, 0.018f, 8);
                st.Cylinder(new Vector3(x, 0.98f, lz + 0.3f), new Vector3(x, 0.58f, zt + 0.03f), 0.018f, 0.018f, 8);
            }
            for (int k = 0; k < 5; k++)
            {
                float y = -0.1f - k * 0.28f;
                float zz = lz - 0.01f - k * 0.006f;
                st.Cylinder(new Vector3(0.52f, y, zz), new Vector3(0.92f, y, zz), 0.016f, 0.016f, 8);
            }
        }

        // ------------------------------------------------------------------ bow: railing, winch, roller, anchor
        static void BuildBow(BoatMeshSet set, Transform root, BoatParts parts)
        {
            var st = set[BoatMats.Steel];
            float[] ts = { 0.72f, 0.79f, 0.86f, 0.93f };
            float tb = 0.985f;
            Vector3 bowTop = new Vector3(0f, BoatHull.SheerY(tb) + 0.55f, BoatHull.Z(tb));
            Vector3 bowMid = new Vector3(0f, BoatHull.SheerY(tb) + 0.3f, BoatHull.Z(tb));
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 prevTop = Vector3.zero, prevMid = Vector3.zero;
                for (int i = 0; i < ts.Length; i++)
                {
                    float t = ts[i];
                    Vector3 bottom = new Vector3(side * (BoatHull.SheerHalf(t) - 0.04f), BoatHull.SheerY(t) + 0.035f, BoatHull.Z(t));
                    Vector3 top = bottom + Vector3.up * 0.52f, mid = bottom + Vector3.up * 0.27f;
                    st.Cylinder(bottom, top, 0.016f, 0.016f, 8);
                    if (i > 0)
                    {
                        st.Cylinder(prevTop, top, 0.019f, 0.019f, 8, false, false);
                        st.Cylinder(prevMid, mid, 0.013f, 0.013f, 8, false, false);
                    }
                    prevTop = top;
                    prevMid = mid;
                }
                st.Cylinder(prevTop, bowTop, 0.019f, 0.019f, 8, false, false);
                st.Cylinder(prevMid, bowMid, 0.013f, 0.013f, 8, false, false);
            }
            st.Cylinder(new Vector3(0f, BoatHull.SheerY(tb), BoatHull.Z(tb)), bowTop, 0.018f, 0.018f, 8);

            // bow roller
            float rz = BoatHull.Z(tb) + 0.06f, ry = BoatHull.SheerY(tb) + 0.07f;
            var metal = set[BoatMats.Metal];
            metal.Cylinder(new Vector3(-0.06f, ry, rz), new Vector3(0.06f, ry, rz), 0.04f, 0.04f, 12);
            metal.Box(new Vector3(-0.075f, ry - 0.02f, rz - 0.08f), new Vector3(0.015f, 0.12f, 0.24f));
            metal.Box(new Vector3(0.075f, ry - 0.02f, rz - 0.08f), new Vector3(0.015f, 0.12f, 0.24f));
            parts.rollerLocal = new Vector3(0f, ry + 0.03f, rz + 0.02f);

            // anchor winch on the foredeck
            const float wz = 2.45f;
            metal.Box(new Vector3(0f, Y0 + 0.02f, wz), new Vector3(0.5f, 0.04f, 0.36f));
            metal.Box(new Vector3(-0.2f, Y0 + 0.2f, wz), new Vector3(0.03f, 0.34f, 0.28f));
            metal.Box(new Vector3(0.2f, Y0 + 0.2f, wz), new Vector3(0.03f, 0.34f, 0.28f));
            metal.Cylinder(new Vector3(-0.19f, Y0 + 0.25f, wz), new Vector3(0.19f, Y0 + 0.25f, wz), 0.07f, 0.07f, 14);
            set[BoatMats.Rope].Cylinder(new Vector3(-0.16f, Y0 + 0.25f, wz), new Vector3(0.16f, Y0 + 0.25f, wz), 0.115f, 0.115f, 14);
            metal.Beam(new Vector3(0.215f, Y0 + 0.25f, wz), new Vector3(0.3f, Y0 + 0.25f, wz), 0.03f, 0.03f);
            metal.Beam(new Vector3(0.3f, Y0 + 0.25f, wz), new Vector3(0.3f, Y0 + 0.4f, wz), 0.03f, 0.03f);
            set[BoatMats.Engine].Cylinder(new Vector3(0.3f, Y0 + 0.4f, wz), new Vector3(0.38f, Y0 + 0.4f, wz), 0.018f, 0.018f, 8);
            set[BoatMats.Rope].Cylinder(new Vector3(0f, Y0 + 0.35f, wz + 0.05f), parts.rollerLocal + new Vector3(0f, 0.02f, -0.05f), 0.013f, 0.013f, 6, false, false);

            // anchor (Danforth style) – own object, origin at the shackle
            var an = new BoatMeshSet();
            var am = an[BoatMats.Metal];
            am.Cylinder(Vector3.zero, new Vector3(0f, -0.06f, 0f), 0.03f, 0.03f, 8);
            am.Box(new Vector3(0f, -0.38f, 0f), new Vector3(0.05f, 0.62f, 0.03f));
            am.Cylinder(new Vector3(-0.28f, -0.68f, 0f), new Vector3(0.28f, -0.68f, 0f), 0.02f, 0.02f, 8);
            am.Box(new Vector3(-0.1f, -0.54f, 0.03f), new Vector3(0.14f, 0.32f, 0.012f), Quaternion.Euler(-18f, 0f, -8f));
            am.Box(new Vector3(0.1f, -0.54f, 0.03f), new Vector3(0.14f, 0.32f, 0.012f), Quaternion.Euler(-18f, 0f, 8f));
            var anchor = an.Build("Anchor", root, parts.rollerLocal + new Vector3(0f, -0.02f, 0.1f), Quaternion.identity, true);
            parts.anchor = anchor.transform;
            parts.anchorStowLocal = anchor.transform.localPosition;
        }

        // ------------------------------------------------------------------ tire fenders
        static void BuildTires(BoatMeshSet set)
        {
            float[] zs = { -2.3f, -0.3f, 1.5f };
            for (int side = -1; side <= 1; side += 2)
                foreach (float z in zs)
                {
                    float t = BoatHull.T(z);
                    float yc = Mathf.Min(0.42f, BoatHull.SheerY(t) - 0.28f);
                    // the hull flares outward with height: clear the widest point the tire spans
                    float x = Mathf.Max(BoatHull.HullXAtY(t, yc), BoatHull.HullXAtY(t, yc + 0.3f)) + 0.09f;
                    Vector3 c = new Vector3(side * x, yc, z);
                    set[BoatMats.Rubber].Torus(c, Quaternion.Euler(0f, 0f, 90f), 0.23f, 0.085f, 20, 8);
                    Vector3 top = c + Vector3.up * 0.3f;
                    Vector3 rail = new Vector3(side * (BoatHull.SheerHalf(t) - 0.02f), BoatHull.SheerY(t) + 0.03f, z);
                    set[BoatMats.Rope].Cylinder(top, rail, 0.012f, 0.012f, 6, false, false);
                }
        }

        // ------------------------------------------------------------------ colliders and interactables
        static GameObject Child(Transform root, string name, Vector3 localPos, Quaternion localRot)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            return go;
        }

        static BoxCollider AddBox(GameObject go, Vector3 center, Vector3 size)
        {
            var bc = go.AddComponent<BoxCollider>();
            bc.center = center;
            bc.size = size;
            return bc;
        }

        static void BoxChild(Transform parent, string name, Vector3 center, Vector3 size, Quaternion rot)
        {
            var go = Child(parent, name, center, rot);
            AddBox(go, Vector3.zero, size);
        }

        static void BuildColliders(Transform root, BoatController boat, BoatParts parts)
        {
            var cols = Child(root, "Colliders", Vector3.zero, Quaternion.identity).transform;

            // convex lower hull: its top face is the deck the player walks on
            var hullGo = Child(cols, "HullCollider", Vector3.zero, Quaternion.identity);
            var mc = hullGo.AddComponent<MeshCollider>();
            mc.sharedMesh = BoatHull.BuildColliderMesh();
            mc.convex = true;

            // bulwark walls (with railing height on the foredeck)
            float[] ts = { 0.0f, 0.08f, 0.16f, 0.24f, 0.32f, 0.40f, 0.48f, 0.56f, 0.64f, 0.72f, 0.79f, 0.86f, 0.92f, 0.97f };
            for (int side = -1; side <= 1; side += 2)
                for (int i = 0; i < ts.Length - 1; i++)
                {
                    float ta = ts[i], tb = ts[i + 1];
                    float wa = Mathf.Max(0.02f, BoatHull.DeckHalf(ta)), wb = Mathf.Max(0.02f, BoatHull.DeckHalf(tb));
                    if (tb >= 0.97f) wb = 0.02f;
                    Vector3 pa = new Vector3(side * wa, 0f, BoatHull.Z(ta)), pb = new Vector3(side * wb, 0f, BoatHull.Z(tb));
                    Vector3 dir = pb - pa;
                    float len = dir.magnitude;
                    if (len < 0.01f) continue;
                    dir /= len;
                    Vector3 outN = Vector3.Cross(Vector3.up, dir);
                    if (Mathf.Sign(outN.x) != side) outN = -outN;
                    float top = Mathf.Max(BoatHull.SheerY(ta), BoatHull.SheerY(tb)) + 0.04f;
                    if (ta >= 0.7f) top += 0.5f;
                    float h = top - BoatHull.DeckY;
                    Vector3 c = (pa + pb) * 0.5f + outN * 0.06f;
                    c.y = BoatHull.DeckY + h * 0.5f;
                    BoxChild(cols, "Bulwark", c, new Vector3(0.12f, h, len + 0.06f), Quaternion.LookRotation(dir, Vector3.up));
                }
            // low transom wall (can be stepped/jumped over to reach the platform)
            float zt = BoatHull.Z(0f);
            BoxChild(cols, "Transom", new Vector3(0f, BoatHull.DeckY + 0.14f, zt + 0.03f), new Vector3(2f * BoatHull.SheerHalf(0f), 0.28f, 0.12f), Quaternion.identity);

            // wheelhouse
            float WX = WheelhouseHalfWidth;
            BoxChild(cols, "CabFront", new Vector3(0f, (Y0 + CabY1) * 0.5f, CabZ1 - WT * 0.5f), new Vector3(2f * WX, CabY1 - Y0, WT), Quaternion.identity);
            BoxChild(cols, "CabLeft", new Vector3(-(WX - WT * 0.5f), (Y0 + CabY1) * 0.5f, (CabZ0 + CabZ1) * 0.5f), new Vector3(WT, CabY1 - Y0, CabZ1 - CabZ0), Quaternion.identity);
            BoxChild(cols, "CabRight", new Vector3(WX - WT * 0.5f, (Y0 + CabY1) * 0.5f, (CabZ0 + CabZ1) * 0.5f), new Vector3(WT, CabY1 - Y0, CabZ1 - CabZ0), Quaternion.identity);
            float rz0 = CabZ0 - 0.4f, rz1 = CabZ1 + 0.15f;
            BoxChild(cols, "CabRoof", new Vector3(0f, CabY1 + 0.04f, (rz0 + rz1) * 0.5f), new Vector3(2f * WX + 0.2f, 0.08f, rz1 - rz0), Quaternion.identity);
            BoxChild(cols, "CabHeader", new Vector3(0f, (2.15f + CabY1) * 0.5f, CabZ0 + WT * 0.5f), new Vector3(2f * WX, CabY1 - 2.15f, WT), Quaternion.identity);

            // deck props
            BoxChild(cols, "Drums", new Vector3(-0.68f, Y0 + 0.44f, -2.69f), new Vector3(0.58f, 0.88f, 1.2f), Quaternion.identity);
            BoxChild(cols, "Cooler", new Vector3(0.62f, Y0 + 0.22f, -2.4f), new Vector3(0.57f, 0.45f, 0.4f), Quaternion.identity);
            BoxChild(cols, "Tanks", new Vector3(1.0f, Y0 + 0.4f, -1.86f), new Vector3(0.2f, 0.8f, 0.46f), Quaternion.identity);
            BoxChild(cols, "JunkCrate", JunkCratePos, new Vector3(0.45f, 0.34f, 0.36f), Quaternion.Euler(0f, 12f, 0f));
            BoxChild(cols, "Platform", new Vector3(0.72f, 0.09f, zt - 0.23f), new Vector3(0.76f, 0.08f, 0.46f), Quaternion.identity);
            BoxChild(cols, "OutboardBox", new Vector3(0f, 0.45f, zt - 0.32f), new Vector3(0.46f, 1.1f, 0.62f), Quaternion.identity);

            // interactables
            var helm = Child(root, "Helm", Vector3.zero, Quaternion.identity);
            AddBox(helm, new Vector3(0f, (Y0 + 1.12f) * 0.5f, 0.88f + CZ), new Vector3(2f * (WX - WT) - 0.02f, 1.12f - Y0, 0.36f));
            AddBox(helm, new Vector3(0f, 1.08f, 0.72f + CZ), new Vector3(0.45f, 0.42f, 0.14f));
            helm.AddComponent<BoatHelmInteractable>().boat = boat;

            var seatGo = Child(root, "HelmSeat", Vector3.zero, Quaternion.identity);
            AddBox(seatGo, new Vector3(0f, (Y0 + 1.18f) * 0.5f, 0.24f + CZ), new Vector3(0.48f, 1.18f - Y0, 0.44f));
            seatGo.AddComponent<BoatHelmInteractable>().boat = boat;

            var ladder = Child(root, "Ladder", Vector3.zero, Quaternion.identity);
            AddBox(ladder, new Vector3(0.72f, -0.55f, zt - 0.44f), new Vector3(0.62f, 1.6f, 0.14f));
            ladder.AddComponent<BoatLadderInteractable>().boat = boat;

            var winch = Child(root, "AnchorWinch", Vector3.zero, Quaternion.identity);
            AddBox(winch, new Vector3(0f, Y0 + 0.26f, 2.45f), new Vector3(0.72f, 0.5f, 0.44f));
            winch.AddComponent<BoatAnchorInteractable>().boat = boat;
        }
    }
}
