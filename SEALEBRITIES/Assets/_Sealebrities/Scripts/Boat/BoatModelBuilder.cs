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

    /// Builds the "Rostlaube" in the low-poly toy style: a chunky faceted ~7.5 m teal fishing boat (rusty boot stripe,
    /// white strake, painted name), wooden deck boards, a boxy white wheelhouse with teal window frames and a red roof,
    /// a blocky outboard, oil drums, tire fenders, railings, a stern dive ladder, a wooden ship's wheel and an anchor winch.
    /// Everything is flat-shaded solid colors; one renderer with one submesh per color.
    public static class BoatModelBuilder
    {
        // Wheelhouse: narrow and set slightly aft so the side walkways stay >= 0.65 m wide (character capsule).
        public const float WheelhouseHalfWidth = 0.55f;
        const float WT = 0.06f, CabZ0 = -1.1f, CabZ1 = 0.7f, CabY1 = 2.35f;
        const float CZ = CabZ1 - 1.1f;   // z offset of all helm/roof fittings relative to their design positions
        static readonly Vector3 JunkCratePos = new Vector3(-0.68f, BoatHull.DeckY + 0.17f, -1.85f);
        static float Y0 => BoatHull.DeckY;

        public static BoatParts Build(Transform root, BoatController boat)
        {
            var parts = new BoatParts();
            var set = new BoatMeshSet();

            // ---------------------------------------------------------- hull, deck, bulwarks
            BoatHull.BuildOuter(set);
            BoatHull.BuildInner(set[BoatMats.Cabin]);
            BoatHull.BuildDeck(set);
            BoatHull.BuildGunwale(set[BoatMats.DeckDark]);
            BoatHull.BuildName(set[BoatMats.HullWhite], "ROSTLAUBE");

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
            var frame = set[BoatMats.Frame];
            var glass = set[BoatMats.Glass];
            float WX = WheelhouseHalfWidth;
            float fz = CabZ1 - WT * 0.5f;
            const float winY0 = 1.25f, winY1 = 1.9f, winC = (winY0 + winY1) * 0.5f, winH = winY1 - winY0;

            // front wall: white panels, two big windows in chunky teal frames
            cab.Box(new Vector3(0f, (Y0 + winY0) * 0.5f, fz), new Vector3(2f * WX, winY0 - Y0, WT));
            cab.Box(new Vector3(0f, (winY1 + CabY1) * 0.5f, fz), new Vector3(2f * WX, CabY1 - winY1, WT));
            frame.Box(new Vector3(-WX + 0.04f, winC, fz), new Vector3(0.08f, winH, WT + 0.02f));
            frame.Box(new Vector3(0f, winC, fz), new Vector3(0.09f, winH, WT + 0.02f));
            frame.Box(new Vector3(WX - 0.04f, winC, fz), new Vector3(0.08f, winH, WT + 0.02f));
            frame.Box(new Vector3(0f, winY0, fz), new Vector3(2f * WX + 0.02f, 0.06f, WT + 0.03f));
            frame.Box(new Vector3(0f, winY1, fz), new Vector3(2f * WX + 0.02f, 0.06f, WT + 0.03f));
            float paneW = WX - 0.1f, paneX = 0.045f + paneW * 0.5f;
            glass.Box(new Vector3(-paneX, winC, fz), new Vector3(paneW, winH, 0.012f));
            glass.Box(new Vector3(paneX, winC, fz), new Vector3(paneW, winH, 0.012f));

            // side walls with long windows
            float midZ = (CabZ0 + CabZ1) * 0.5f, lenZ = CabZ1 - CabZ0;
            const float sY0 = 1.2f, sY1 = 1.85f, sC = (sY0 + sY1) * 0.5f, sH = sY1 - sY0;
            float wz0 = CabZ0 + 0.25f, wz1 = CabZ1 - 0.25f, wzc = (wz0 + wz1) * 0.5f, wzl = wz1 - wz0;
            for (int s = -1; s <= 1; s += 2)
            {
                float x = s * (WX - WT * 0.5f);
                cab.Box(new Vector3(x, (Y0 + sY0) * 0.5f, midZ), new Vector3(WT, sY0 - Y0, lenZ));
                cab.Box(new Vector3(x, (sY1 + CabY1) * 0.5f, midZ), new Vector3(WT, CabY1 - sY1, lenZ));
                cab.Box(new Vector3(x, sC, (CabZ0 + wz0) * 0.5f), new Vector3(WT, sH, 0.25f));
                cab.Box(new Vector3(x, sC, (wz1 + CabZ1) * 0.5f), new Vector3(WT, sH, 0.25f));
                glass.Box(new Vector3(x, sC, wzc), new Vector3(0.012f, sH, wzl));
                frame.Box(new Vector3(x, sY0, wzc), new Vector3(WT + 0.03f, 0.06f, wzl + 0.08f));
                frame.Box(new Vector3(x, sY1, wzc), new Vector3(WT + 0.03f, 0.06f, wzl + 0.08f));
                frame.Box(new Vector3(x, sC, wz0), new Vector3(WT + 0.02f, sH, 0.06f));
                frame.Box(new Vector3(x, sC, wz1), new Vector3(WT + 0.02f, sH, 0.06f));
                // teal corner posts make the box read as a little hut
                frame.Box(new Vector3(s * (WX - 0.02f), (Y0 + CabY1) * 0.5f, CabZ1 - 0.02f), new Vector3(0.07f, CabY1 - Y0, 0.07f));
                frame.Box(new Vector3(s * (WX - 0.02f), (Y0 + CabY1) * 0.5f, CabZ0 + 0.02f), new Vector3(0.07f, CabY1 - Y0, 0.07f));
            }

            // back header beam and the red roof (chunky slab with a bevel)
            cab.Box(new Vector3(0f, (2.15f + CabY1) * 0.5f, CabZ0 + WT * 0.5f), new Vector3(2f * WX, CabY1 - 2.15f, WT));
            float rz0 = CabZ0 - 0.4f, rz1 = CabZ1 + 0.15f;
            set[BoatMats.Roof].TaperBox(new Vector3(0f, CabY1 + 0.04f, (rz0 + rz1) * 0.5f),
                new Vector2(2f * WX + 0.22f, rz1 - rz0), new Vector2(2f * WX + 0.08f, rz1 - rz0 - 0.14f), 0.08f, Quaternion.identity);

            // wooden console + dark instrument panel
            set[BoatMats.DeckDark].Box(new Vector3(0f, (Y0 + 1.0f) * 0.5f, 0.92f + CZ), new Vector3(2f * (WX - WT), 1.0f - Y0, 0.24f));
            Quaternion panelRot = Quaternion.Euler(-25f, 0f, 0f);
            Vector3 panelC = new Vector3(0f, 1.03f, 0.9f + CZ);
            set[BoatMats.Engine].Box(panelC, new Vector3(2f * (WX - WT) - 0.02f, 0.04f, 0.3f), panelRot);
            Vector3 pn = panelRot * Vector3.up;
            float[] gx = { -0.36f, -0.22f, 0.22f };
            for (int i = 0; i < gx.Length; i++)
            {
                Vector3 gc = panelC + panelRot * new Vector3(gx[i], 0.021f, 0.02f);
                set[BoatMats.Steel].Cylinder(gc - pn * 0.005f, gc + pn * 0.012f, 0.05f, 0.05f, 8, false, true);
                set[BoatMats.Gauge].Disc(gc + pn * 0.0145f, pn, 0.038f, 8);
            }
            // compass on the dash
            set[BoatMats.Engine].Cylinder(new Vector3(0f, 1.0f, 1.0f + CZ), new Vector3(0f, 1.06f, 1.0f + CZ), 0.06f, 0.055f, 8);
            set[BoatMats.Glass].Ellipsoid(new Vector3(0f, 1.06f, 1.0f + CZ), new Vector3(0.05f, 0.04f, 0.05f), Quaternion.identity, 8, 2, 0f, 90f);
            // throttle housing
            set[BoatMats.Engine].Box(new Vector3(0.33f, 1.02f, 0.86f + CZ), new Vector3(0.09f, 0.07f, 0.16f));

            // wooden ship's wheel (animated)
            {
                var ws = new BoatMeshSet();
                ws[BoatMats.DeckDark].Torus(Vector3.zero, Quaternion.Euler(90f, 0f, 0f), 0.18f, 0.022f, 12, 4);
                ws[BoatMats.Steel].Cylinder(new Vector3(0f, 0f, -0.03f), new Vector3(0f, 0f, 0.26f), 0.035f, 0.025f, 6);
                for (int k = 0; k < 6; k++)
                {
                    float a = (90f + k * 60f) * Mathf.Deg2Rad;
                    Vector3 dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                    ws[BoatMats.Deck].Beam(dir * 0.03f, dir * 0.2f, 0.022f, 0.016f);
                    ws[BoatMats.Deck].Cylinder(dir * 0.2f, dir * 0.25f, 0.016f, 0.012f, 5);
                }
                var go = ws.Build("SteeringWheel", root, new Vector3(0f, 1.08f, 0.72f + CZ), Quaternion.Euler(30f, 0f, 0f), true);
                parts.steeringWheel = go.transform;
                parts.wheelBaseRot = go.transform.localRotation;
            }
            // throttle lever (animated)
            {
                var ls = new BoatMeshSet();
                ls[BoatMats.Steel].Cylinder(Vector3.zero, new Vector3(0f, 0.16f, 0f), 0.013f, 0.013f, 5);
                ls[BoatMats.BobberRed].Sphere(new Vector3(0f, 0.17f, 0f), 0.03f, 6, 4);
                var go = ls.Build("ThrottleLever", root, new Vector3(0.33f, 1.05f, 0.86f + CZ), Quaternion.identity, false);
                parts.throttleLever = go.transform;
                parts.leverBaseRot = go.transform.localRotation;
            }

            // driver seat
            var metal = set[BoatMats.Metal];
            metal.Cylinder(new Vector3(0f, Y0, 0.25f + CZ), new Vector3(0f, 0.7f, 0.25f + CZ), 0.04f, 0.04f, 6);
            metal.Cylinder(new Vector3(0f, Y0, 0.25f + CZ), new Vector3(0f, Y0 + 0.03f, 0.25f + CZ), 0.17f, 0.17f, 8, false, true);
            set[BoatMats.Cushion].TaperBox(new Vector3(0f, 0.75f, 0.25f + CZ), new Vector2(0.46f, 0.42f), new Vector2(0.42f, 0.38f), 0.1f, Quaternion.identity);
            set[BoatMats.Cushion].Box(new Vector3(0f, 1.0f, 0.06f + CZ), new Vector3(0.44f, 0.36f, 0.08f));
            metal.Beam(new Vector3(0f, 0.7f, 0.1f + CZ), new Vector3(0f, 0.86f, 0.06f + CZ), 0.05f, 0.035f);
            var seat = new GameObject("DriverSeat").transform;
            seat.SetParent(root, false);
            seat.localPosition = new Vector3(0f, Y0 - 0.25f, 0.3f + CZ);
            seat.localRotation = Quaternion.identity;
            parts.seat = seat;

            // roof gear: mast, radar, masthead light, antenna, searchlight, nav lights
            float mz = 0.1f + CZ;
            metal.Cylinder(new Vector3(0f, CabY1 + 0.08f, mz), new Vector3(0f, CabY1 + 1.05f, mz), 0.04f, 0.034f, 6);
            set[BoatMats.Engine].Box(new Vector3(0f, CabY1 + 0.84f, mz), new Vector3(0.16f, 0.1f, 0.16f));
            set[BoatMats.NavWhite].Sphere(new Vector3(0f, CabY1 + 1.1f, mz), 0.055f, 6, 3);
            set[BoatMats.Rubber].Cylinder(new Vector3(0.5f, CabY1 + 0.08f, -0.9f + CZ), new Vector3(0.5f, CabY1 + 1.9f, -0.9f + CZ), 0.012f, 0.006f, 4);
            set[BoatMats.LifeOrange].Box(new Vector3(0.5f, CabY1 + 1.92f, -0.9f + CZ), new Vector3(0.03f, 0.03f, 0.03f));
            metal.Box(new Vector3(-0.42f, CabY1 + 0.14f, 0.95f + CZ), new Vector3(0.07f, 0.12f, 0.07f));
            metal.Cylinder(new Vector3(-0.42f, CabY1 + 0.26f, 0.85f + CZ), new Vector3(-0.42f, CabY1 + 0.26f, 1.1f + CZ), 0.08f, 0.095f, 8, true, true);
            set[BoatMats.NavWhite].Disc(new Vector3(-0.42f, CabY1 + 0.26f, 1.101f + CZ), Vector3.forward, 0.085f, 8);
            set[BoatMats.NavRed].Box(new Vector3(-WX - 0.03f, 2.05f, 0.95f + CZ), new Vector3(0.06f, 0.09f, 0.13f));
            set[BoatMats.NavGreen].Box(new Vector3(WX + 0.03f, 2.05f, 0.95f + CZ), new Vector3(0.06f, 0.09f, 0.13f));
            {
                var rs = new BoatMeshSet();
                rs[BoatMats.Cabin].TaperBox(Vector3.zero, new Vector2(0.9f, 0.13f), new Vector2(0.82f, 0.07f), 0.08f, Quaternion.identity);
                rs[BoatMats.Engine].Box(new Vector3(0f, -0.07f, 0f), new Vector3(0.09f, 0.07f, 0.09f));
                var go = rs.Build("Radar", root, new Vector3(0f, CabY1 + 0.96f, mz), Quaternion.identity, true);
                parts.radar = go.transform;
            }

            // life ring on the port wall (orange/white quarters)
            Quaternion ringRot = Quaternion.Euler(0f, 0f, 90f);
            Vector3 ringC = new Vector3(-WX - 0.08f, 1.5f, -0.15f + CZ);
            for (int k = 0; k < 4; k++)
                set[k % 2 == 0 ? BoatMats.LifeOrange : BoatMats.White].Torus(ringC, ringRot, 0.25f, 0.065f, 12, 4, k * 3, k * 3 + 3);
        }

        // ------------------------------------------------------------------ aft deck props
        static void BuildDeckProps(BoatMeshSet set)
        {
            // oil drums
            DrumAt(set, BoatMats.DrumBlue, new Vector3(-0.68f, Y0, -3.0f), 0.3f);
            DrumAt(set, BoatMats.DrumOrange, new Vector3(-0.68f, Y0, -2.38f), -0.2f);
            // cooler box
            set[BoatMats.Cooler].Box(new Vector3(0.62f, Y0 + 0.2f, -2.4f), new Vector3(0.55f, 0.4f, 0.38f));
            set[BoatMats.CoolerLid].TaperBox(new Vector3(0.62f, Y0 + 0.43f, -2.4f), new Vector2(0.58f, 0.41f), new Vector2(0.52f, 0.35f), 0.06f, Quaternion.identity);
            set[BoatMats.Engine].Box(new Vector3(0.62f, Y0 + 0.3f, -2.62f), new Vector3(0.22f, 0.05f, 0.05f));
            // coiled rope
            for (int k = 0; k < 3; k++)
                set[BoatMats.Rope].Torus(new Vector3(0.45f, Y0 + 0.03f + k * 0.045f, -1.55f), Quaternion.Euler(0f, k * 20f, 0f), 0.2f - k * 0.02f, 0.026f, 10, 4);
            set[BoatMats.Rope].Cylinder(new Vector3(0.62f, Y0 + 0.15f, -1.6f), new Vector3(0.7f, Y0 + 0.03f, -2.0f), 0.024f, 0.024f, 4);
            // dive tanks strapped to the starboard bulwark
            for (int k = 0; k < 2; k++)
            {
                float z = -1.98f + k * 0.24f;
                Vector3 b0 = new Vector3(1.0f, Y0, z);
                set[BoatMats.Tank].Cylinder(b0, b0 + Vector3.up * 0.6f, 0.095f, 0.095f, 8, false, false);
                set[BoatMats.Tank].Cylinder(b0 + Vector3.up * 0.6f, b0 + Vector3.up * 0.68f, 0.095f, 0.04f, 8, false, true);
                set[BoatMats.Steel].Cylinder(b0 + Vector3.up * 0.68f, b0 + Vector3.up * 0.76f, 0.028f, 0.028f, 6);
                set[BoatMats.Engine].Box(b0 + Vector3.up * 0.78f, new Vector3(0.09f, 0.045f, 0.045f));
            }
            set[BoatMats.Strap].Box(new Vector3(1.0f, Y0 + 0.42f, -1.86f), new Vector3(0.21f, 0.05f, 0.47f));
            // a small crate of junk
            Quaternion cr = Quaternion.Euler(0f, 12f, 0f);
            set[BoatMats.Crate].Box(JunkCratePos, new Vector3(0.45f, 0.34f, 0.36f), cr);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    set[BoatMats.CrateDark].Box(JunkCratePos + cr * new Vector3(sx * 0.21f, 0f, sz * 0.165f), new Vector3(0.05f, 0.35f, 0.05f), cr);
            set[BoatMats.EngineGrey].Box(JunkCratePos + new Vector3(0.02f, 0.2f, 0f), new Vector3(0.18f, 0.08f, 0.12f), Quaternion.Euler(0f, 40f, 8f));
        }

        static void DrumAt(BoatMeshSet set, Material m, Vector3 bottom, float lidRot)
        {
            const float r = 0.29f, h = 0.88f;
            var d = set[m];
            d.Cylinder(bottom, bottom + Vector3.up * h, r, r, 8, false, true);
            // rolling hoops and the lid rim stick out a little (same paint)
            d.Tube(bottom + Vector3.up * 0.28f, bottom + Vector3.up * 0.32f, r + 0.015f, r - 0.01f, 8, false);
            d.Tube(bottom + Vector3.up * 0.56f, bottom + Vector3.up * 0.6f, r + 0.015f, r - 0.01f, 8, false);
            d.Tube(bottom + Vector3.up * (h - 0.03f), bottom + Vector3.up * (h + 0.015f), r + 0.01f, r - 0.03f, 8, false);
            set[BoatMats.HullRust].Cylinder(bottom + Vector3.up * (h - 0.002f), bottom + Vector3.up * (h + 0.004f), r * 0.55f, r * 0.45f, 6, false, true, lidRot);
            set[BoatMats.Metal].Cylinder(bottom + new Vector3(0.14f, h, 0.06f), bottom + new Vector3(0.14f, h + 0.025f, 0.06f), 0.035f, 0.035f, 6);
        }

        // ------------------------------------------------------------------ outboard, platform, ladder
        static void BuildStern(BoatMeshSet set, Transform root, BoatParts parts)
        {
            float zt = BoatHull.Z(0f);
            set[BoatMats.Metal].Box(new Vector3(0f, 0.44f, zt - 0.07f), new Vector3(0.32f, 0.24f, 0.14f));

            // chunky outboard motor on a steering pivot
            var os = new BoatMeshSet();
            os[BoatMats.EngineGrey].Box(new Vector3(0f, -0.02f, 0.02f), new Vector3(0.16f, 0.22f, 0.13f));
            os[BoatMats.Engine].TaperBox(new Vector3(0f, 0.33f, -0.13f), new Vector2(0.42f, 0.58f), new Vector2(0.32f, 0.44f), 0.36f, Quaternion.identity, new Vector2(0f, -0.02f));
            os[BoatMats.LifeOrange].Box(new Vector3(0f, 0.2f, -0.13f), new Vector3(0.435f, 0.05f, 0.595f));
            os[BoatMats.EngineGrey].TaperBox(new Vector3(0f, 0.12f, -0.11f), new Vector2(0.3f, 0.42f), new Vector2(0.4f, 0.56f), 0.1f, Quaternion.identity);
            os[BoatMats.EngineGrey].TaperBox(new Vector3(0f, -0.39f, -0.02f), new Vector2(0.12f, 0.2f), new Vector2(0.15f, 0.24f), 0.82f, Quaternion.identity);
            os[BoatMats.EngineGrey].Box(new Vector3(0f, -0.8f, -0.05f), new Vector3(0.3f, 0.025f, 0.38f));
            os[BoatMats.EngineGrey].Cylinder(new Vector3(0f, -0.93f, -0.2f), new Vector3(0f, -0.93f, 0.2f), 0.075f, 0.06f, 6);
            os[BoatMats.EngineGrey].Box(new Vector3(0f, -1.05f, 0.04f), new Vector3(0.03f, 0.17f, 0.16f));
            os[BoatMats.Metal].Cylinder(new Vector3(0f, 0.42f, -0.38f), new Vector3(0f, 0.5f, -0.44f), 0.035f, 0.04f, 6, false, true);
            os[BoatMats.LifeOrange].Box(new Vector3(0f, 0.36f, 0.165f), new Vector3(0.2f, 0.07f, 0.015f));
            var outboard = os.Build("Outboard", root, new Vector3(0f, 0.5f, zt - 0.2f), Quaternion.identity, true);
            parts.outboardPivot = outboard.transform;

            var ex = new GameObject("Exhaust").transform;
            ex.SetParent(outboard.transform, false);
            ex.localPosition = new Vector3(0f, 0.52f, -0.43f);
            parts.exhaust = ex;

            var ps = new BoatMeshSet();
            ps[BoatMats.Steel].Cylinder(new Vector3(0f, 0f, 0.03f), new Vector3(0f, 0f, -0.08f), 0.04f, 0.028f, 6);
            for (int k = 0; k < 3; k++)
            {
                Quaternion r = Quaternion.Euler(0f, 0f, k * 120f) * Quaternion.Euler(0f, 28f, 0f);
                ps[BoatMats.Steel].Box(Quaternion.Euler(0f, 0f, k * 120f) * new Vector3(0f, 0.095f, -0.02f), new Vector3(0.09f, 0.13f, 0.014f), r);
            }
            var prop = ps.Build("Propeller", outboard.transform, new Vector3(0f, -0.93f, -0.22f), Quaternion.identity, false);
            parts.propeller = prop.transform;
            parts.propLocal = new Vector3(0f, 0.5f - 0.93f, zt - 0.42f);

            // swim platform (starboard side of the motor): three planks on two brackets
            for (int k = 0; k < 3; k++)
            {
                float z = zt - 0.08f - k * 0.15f;
                set[k == 1 ? BoatMats.DeckB : BoatMats.Deck].Box(new Vector3(0.72f, 0.09f, z), new Vector3(0.76f, 0.08f, 0.135f));
            }
            set[BoatMats.Metal].Beam(new Vector3(0.45f, -0.15f, zt - 0.01f), new Vector3(0.45f, 0.05f, zt - 0.4f), 0.05f, 0.05f);
            set[BoatMats.Metal].Beam(new Vector3(1.0f, -0.15f, zt - 0.01f), new Vector3(1.0f, 0.05f, zt - 0.4f), 0.05f, 0.05f);

            // dive ladder into the water + grab rails
            var st = set[BoatMats.Steel];
            float lz = zt - 0.42f;
            const float rr = 0.022f;
            for (int s = 0; s < 2; s++)
            {
                float x = s == 0 ? 0.52f : 0.92f;
                st.Cylinder(new Vector3(x, 0.06f, lz), new Vector3(x, -1.25f, lz - 0.03f), rr, rr, 6);
                st.Cylinder(new Vector3(x, 0.13f, lz + 0.02f), new Vector3(x, 0.95f, lz + 0.1f), rr, rr, 6);
                st.Cylinder(new Vector3(x, 0.95f, lz + 0.1f), new Vector3(x, 0.98f, lz + 0.3f), rr, rr, 6);
                st.Cylinder(new Vector3(x, 0.98f, lz + 0.3f), new Vector3(x, 0.58f, zt + 0.03f), rr, rr, 6);
            }
            for (int k = 0; k < 5; k++)
            {
                float y = -0.1f - k * 0.28f;
                float zz = lz - 0.01f - k * 0.006f;
                set[BoatMats.LifeOrange].Box(new Vector3(0.72f, y, zz), new Vector3(0.4f, 0.035f, 0.06f));
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
                    st.Cylinder(bottom, top, 0.02f, 0.02f, 5);
                    if (i > 0)
                    {
                        st.Cylinder(prevTop, top, 0.024f, 0.024f, 5);
                        st.Cylinder(prevMid, mid, 0.017f, 0.017f, 5);
                    }
                    prevTop = top;
                    prevMid = mid;
                }
                st.Cylinder(prevTop, bowTop, 0.024f, 0.024f, 5);
                st.Cylinder(prevMid, bowMid, 0.017f, 0.017f, 5);
            }
            st.Cylinder(new Vector3(0f, BoatHull.SheerY(tb), BoatHull.Z(tb)), bowTop, 0.022f, 0.022f, 5);
            set[BoatMats.LifeOrange].Sphere(bowTop + Vector3.up * 0.02f, 0.035f, 5, 3);

            // bow roller
            float rz = BoatHull.Z(tb) + 0.06f, ry = BoatHull.SheerY(tb) + 0.07f;
            var metal = set[BoatMats.Metal];
            set[BoatMats.Rubber].Cylinder(new Vector3(-0.06f, ry, rz), new Vector3(0.06f, ry, rz), 0.045f, 0.045f, 6);
            metal.Box(new Vector3(-0.08f, ry - 0.02f, rz - 0.08f), new Vector3(0.02f, 0.13f, 0.24f));
            metal.Box(new Vector3(0.08f, ry - 0.02f, rz - 0.08f), new Vector3(0.02f, 0.13f, 0.24f));
            parts.rollerLocal = new Vector3(0f, ry + 0.03f, rz + 0.02f);

            // anchor winch on the foredeck
            const float wz = 2.45f;
            metal.Box(new Vector3(0f, Y0 + 0.025f, wz), new Vector3(0.5f, 0.05f, 0.36f));
            metal.Box(new Vector3(-0.2f, Y0 + 0.2f, wz), new Vector3(0.04f, 0.34f, 0.28f));
            metal.Box(new Vector3(0.2f, Y0 + 0.2f, wz), new Vector3(0.04f, 0.34f, 0.28f));
            set[BoatMats.Engine].Cylinder(new Vector3(-0.18f, Y0 + 0.25f, wz), new Vector3(0.18f, Y0 + 0.25f, wz), 0.075f, 0.075f, 6);
            set[BoatMats.Rope].Cylinder(new Vector3(-0.15f, Y0 + 0.25f, wz), new Vector3(0.15f, Y0 + 0.25f, wz), 0.12f, 0.12f, 8);
            metal.Beam(new Vector3(0.215f, Y0 + 0.25f, wz), new Vector3(0.3f, Y0 + 0.25f, wz), 0.035f, 0.035f);
            metal.Beam(new Vector3(0.3f, Y0 + 0.25f, wz), new Vector3(0.3f, Y0 + 0.4f, wz), 0.035f, 0.035f);
            set[BoatMats.LifeOrange].Cylinder(new Vector3(0.3f, Y0 + 0.4f, wz), new Vector3(0.39f, Y0 + 0.4f, wz), 0.022f, 0.022f, 5);
            set[BoatMats.Rope].Cylinder(new Vector3(0f, Y0 + 0.35f, wz + 0.05f), parts.rollerLocal + new Vector3(0f, 0.02f, -0.05f), 0.016f, 0.016f, 4, false, false);

            // anchor (chunky Danforth style) – own object, origin at the shackle
            var an = new BoatMeshSet();
            var am = an[BoatMats.Metal];
            am.Cylinder(Vector3.zero, new Vector3(0f, -0.07f, 0f), 0.035f, 0.035f, 5);
            am.Box(new Vector3(0f, -0.38f, 0f), new Vector3(0.06f, 0.62f, 0.04f));
            am.Cylinder(new Vector3(-0.28f, -0.68f, 0f), new Vector3(0.28f, -0.68f, 0f), 0.025f, 0.025f, 5);
            // flukes: wide at the crown, pointed toward the shackle
            am.TaperBox(new Vector3(-0.1f, -0.54f, 0.03f), new Vector2(0.16f, 0.018f), new Vector2(0.06f, 0.018f), 0.32f, Quaternion.Euler(-18f, 0f, -8f));
            am.TaperBox(new Vector3(0.1f, -0.54f, 0.03f), new Vector2(0.16f, 0.018f), new Vector2(0.06f, 0.018f), 0.32f, Quaternion.Euler(-18f, 0f, 8f));
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
                    float x = Mathf.Max(BoatHull.HullXAtY(t, yc), BoatHull.HullXAtY(t, yc + 0.3f)) + 0.1f;
                    Vector3 c = new Vector3(side * x, yc, z);
                    set[BoatMats.Rubber].Torus(c, Quaternion.Euler(0f, 0f, 90f), 0.23f, 0.09f, 10, 4);
                    Vector3 top = c + Vector3.up * 0.3f;
                    Vector3 rail = new Vector3(side * (BoatHull.SheerHalf(t) - 0.02f), BoatHull.SheerY(t) + 0.03f, z);
                    set[BoatMats.Rope].Cylinder(top, rail, 0.016f, 0.016f, 4, false, false);
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
