using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Dock, helipad (+ Wanda's hangar) and lighthouse in the low-poly style (chunky planks, 6-8 sided posts, flat colors).
    public static class PropStructures
    {
        static float G(float x, float z) => PropGround.H(x, z);
        static float G(Vector3 p) => PropGround.H(p.x, p.z);

        // ================================================================== dock
        /// The dock (built by PropDock: weathered planks with gaps, posts with wet bands, working ladder, lanterns, ...).
        public static IEnumerator BuildDock(Transform island, PropExclusion excl) => PropDock.Build(island, excl);

        // ================================================================== helipad
        public static IEnumerator BuildHelipad(Transform island, PropExclusion excl)
        {
            Vector3 c = WorldLayout.Helipad;
            const float R = 6f;
            const int Sides = 12;
            PropGround.MinMax(c.x, c.z, R + 0.5f, out float gmin, out float gmax);
            float top = Mathf.Max(gmax + 0.2f, 0.9f);
            float bottom = gmin - 0.8f;
            PropInfo.HelipadTopY = top;

            var root = PropUtil.WorldRoot("Helipad", island);
            var cols = new GameObject("Colliders").transform;
            cols.SetParent(root, false);
            var batch = new PropBatch(0f);

            // flat concrete disk: the top carries the crisp "H" block texture, a yellow ring is geometry
            Vector3 ct = new Vector3(c.x, top, c.z);
            batch.B(PropMats.Helipad).Disk(ct, R, Sides, Quaternion.identity, true, 1f, true, 15f);
            // (a +y rotation of -15° turns the ring's vertex angles by +15°, matching the disk's angle offset)
            batch.B(PropMats.Marking).Ring(ct + Vector3.up * 0.012f, R - 0.75f, R - 0.4f, Sides, Quaternion.Euler(0f, -15f, 0f));
            // L.12: a nicer landing zone – white edge band, chevrons toward the hangar, a turquoise rim
            batch.B(PropMats.White).Ring(ct + Vector3.up * 0.01f, R - 0.18f, R - 0.02f, Sides, Quaternion.Euler(0f, -15f, 0f));
            batch.B(PropMats.Concrete).Cylinder(new Vector3(c.x, top - 0.35f, c.z), Quaternion.identity, R + 0.05f, R, 0.35f, Sides, false, false, 1f, 1f, 0f, false, 15f);
            batch.B(PropMats.ConcreteDark).Cylinder(new Vector3(c.x, bottom, c.z), Quaternion.identity, R + 0.15f, R + 0.05f, top - 0.35f - bottom, Sides, false, false, 1f, 1f, 0f, false, 15f);
            batch.B(PropMats.Shutter).Cylinder(new Vector3(c.x, top - 0.12f, c.z), Quaternion.identity, R + 0.1f, R + 0.1f, 0.1f, Sides, false, false, 1f, 1f, 0f, false, 15f);
            var cb = new PropMeshBuilder();
            cb.Cylinder(new Vector3(0f, bottom - top, 0f), Quaternion.identity, R, R, top - bottom, 24, true, true);
            PropCol.Mesh(cols, cb.ToMesh("HelipadCollider", false), ct, Quaternion.identity, Vector3.one, "HelipadCol");

            // edge lights (emissive, switched by a night controller)
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI * 2f / 8f;
                Vector3 p = ct + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (R - 0.2f);
                batch.B(PropMats.DarkMetal).Cylinder(p, Quaternion.identity, 0.1f, 0.09f, 0.06f, 6, false, true);
                batch.B(PropMats.PadBulb).Ico(p + Vector3.up * 0.07f, new Vector3(0.08f, 0.07f, 0.08f), Quaternion.identity, 0);
            }
            var lightsGo = new GameObject("PadLights");
            lightsGo.transform.SetParent(root, false);
            lightsGo.transform.position = ct + Vector3.up * 1.2f;
            var padLight = lightsGo.AddComponent<Light>();
            padLight.type = LightType.Point;
            padLight.color = new Color(1f, 0.85f, 0.55f);
            padLight.range = 11f;
            padLight.intensity = 0.9f;
            padLight.shadows = LightShadows.None;
            PropNightLight.Attach(lightsGo, padLight, PropMats.PadBulb, new Color(3f, 2.4f, 0.8f), 0.15f, 0.4f, false);

            // steps toward the hut when the pad stands above the ground
            Vector3 dir = WorldLayout.Hut - c;
            dir.y = 0f;
            dir.Normalize();
            Quaternion sr = Quaternion.LookRotation(dir);
            for (int i = 0; i < 25; i++)
            {
                float stepTop = top - (i + 1) * 0.2f;
                Vector3 p = c + dir * (R + 0.16f + i * 0.32f);
                float g = G(p.x, p.z);
                if (stepTop < g - 0.02f) break;
                float h = stepTop - (g - 0.3f);
                Vector3 sc = new Vector3(p.x, stepTop - h * 0.5f, p.z);
                batch.B(i % 2 == 0 ? PropMats.Concrete : PropMats.ConcreteDark).Box(sc, new Vector3(2.2f, h, 0.32f), sr);
                PropCol.Box(cols, sc, new Vector3(2.2f, h, 0.32f), sr);
            }

            // windsock
            Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;
            Vector3 sockPos = c + side * (R + 2.2f) - dir * 1.5f;
            sockPos.y = G(sockPos.x, sockPos.z);
            Windsock(root, sockPos, batch);
            PropCol.Capsule(cols, sockPos + Vector3.up * 2.5f, 0.1f, 5f, Quaternion.identity);

            // drums and a pallet
            var rng = new System.Random(55);
            Vector3 dp = c - side * (R + 2.5f) - dir * 1f;
            for (int i = 0; i < 3; i++)
            {
                Vector3 p = dp + new Vector3(PropUtil.Range(rng, -0.9f, 0.9f), 0f, PropUtil.Range(rng, -0.9f, 0.9f));
                p.y = G(p.x, p.z) - 0.04f;
                Drum(batch, p, i == 1 ? PropMats.DrumRed : PropMats.DrumYellow);
                PropCol.Capsule(cols, p + Vector3.up * 0.44f, 0.3f, 0.88f, Quaternion.identity);
            }
            Vector3 pal = dp + dir * 2.2f;
            pal.y = G(pal.x, pal.z);
            for (int k = 0; k < 5; k++)
                batch.B(PropMats.WoodLight).Box(pal + sr * new Vector3(-0.48f + k * 0.24f, 0.13f, 0f), new Vector3(0.18f, 0.04f, 1.2f), sr);
            for (int k = -1; k <= 1; k++)
                batch.B(PropMats.Wood).Box(pal + sr * new Vector3(0f, 0.06f, k * 0.5f), new Vector3(1.1f, 0.11f, 0.11f), sr);
            PropKit.Crate(batch, cols, pal + Vector3.up * 0.15f, 0.55f, 12f, PropMats.Wood3);

            // flowering border bushes and a few lamp posts make the pad a tidy little airfield
            for (int i = 0; i < 10; i++)
            {
                float a = (i * 36f + 18f) * Mathf.Deg2Rad;
                Vector3 p = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (R + 1.4f);
                if (Vector3.Dot((p - c).normalized, dir) > 0.8f) continue;    // keep the steps free
                if (Vector3.Distance(p, sockPos) < 2f || Vector3.Distance(p, dp) < 3.2f) continue;
                p.y = G(p.x, p.z);
                batch.B(i % 3 == 0 ? PropMats.FoliageLight : PropMats.Foliage).Ico(p + Vector3.up * 0.3f, new Vector3(0.45f, 0.4f, 0.45f), Quaternion.Euler(0f, i * 31f, 0f), 0);
                if (i % 2 == 0) batch.B(PropMats.FlowerPink).Octa(p + new Vector3(0.15f, 0.62f, 0.1f), 0.07f);
                else batch.B(PropMats.FlowerYellow).Octa(p + new Vector3(-0.12f, 0.6f, 0.15f), 0.07f);
            }

            batch.Flush(root, "HelipadMesh", ShadowCastingMode.On, 0f);
            excl.Add(c.x, c.z, R + 4.5f);
            excl.Add(sockPos.x, sockPos.z, 1.5f);
            excl.Add(dp.x, dp.z, 3f);
            PropInfo.SetSpot("wanda_pad", ct + side * (R - 1.5f), Mathf.Atan2(-side.x, -side.z) * Mathf.Rad2Deg);
            PropInfo.AddLandSpot(sockPos + Vector3.up * 5.1f, LandSpotKind.Perch, 1f, "helipad");
            PropInfo.AddLandSpot(new Vector3(c.x, top, c.z) - dir * 9f, LandSpotKind.Meadow, 8f, "helipad");
            if (PropRun.Due()) yield return null;

            yield return Hangar(root, excl);
        }

        /// K.5 / L.12: Wanda's hangar shack on the helipad plateau – a wide open front toward the pad, a workbench,
        /// the radio, a hammock, spare rotor blades, a fuel pump and the „CHOPPER SHOP“ sign.
        static IEnumerator Hangar(Transform root, PropExclusion excl)
        {
            var batch = new PropBatch(0f);
            var cols = new GameObject("HangarColliders").transform;
            cols.SetParent(root, false);
            Vector3 h = WorldLayout.HangarSpot;
            Vector3 toPad = WorldLayout.Helipad - h;
            toPad.y = 0f;
            toPad.Normalize();
            float yaw = Mathf.Atan2(toPad.x, toPad.z) * Mathf.Rad2Deg;
            Quaternion r = Quaternion.Euler(0f, yaw, 0f);
            PropGround.MinMaxRect(h, r, 3.3f, 3.0f, 0.8f, out float gmin, out float gmax);
            float floorY = gmax + 0.18f;
            var spec = PropShack.Default(6.2f, 5.4f, 77);
            spec.doorX = 0f;
            spec.doorW = 3.6f;
            spec.wallH = 2.9f;
            spec.pitchDeg = 24f;
            spec.windowLeft = true;
            spec.windowRight = true;
            Vector3 door = PropShack.Build(batch, cols, h, yaw, floorY, spec);
            Vector3 W(float x, float y, float z) => new Vector3(h.x, floorY, h.z) + r * new Vector3(x, y, z);

            // workbench with the radio along the back wall
            PropKit.Table(batch, cols, W(0.8f, 0f, -2.2f), yaw, 2.2f, 0.7f, PropMats.Wood2);
            Vector3 radio = W(1.4f, 0.77f, -2.25f);
            batch.W(PropMats.Flat(PropPal.HullBlue), radio).Box(radio + Vector3.up * 0.16f, new Vector3(0.5f, 0.32f, 0.3f), r);
            batch.W(PropMats.Ink, radio).Cylinder(radio + r * new Vector3(0.12f, 0.16f, 0.16f), r * Quaternion.Euler(90f, 0f, 0f), 0.04f, 0.04f, 0.03f, 6, true, true);
            batch.W(PropMats.Metal, radio).Rod(radio + r * new Vector3(-0.2f, 0.32f, 0f), radio + r * new Vector3(-0.35f, 1.2f, 0.05f), 0.008f, 0.006f, 3);
            batch.W(PropMats.Ink, radio).Box(radio + r * new Vector3(-0.45f, 0.08f, 0.1f), new Vector3(0.14f, 0.16f, 0.1f), r);
            batch.W(PropMats.Orange, radio).Box(W(-0.2f, 0.8f, -2.1f), new Vector3(0.3f, 0.06f, 0.22f), r * Quaternion.Euler(0f, 15f, 0f));
            // tool wall: a board with spanners and a coil of rope
            Vector3 tw = W(-1.8f, 1.4f, -2.62f);
            batch.W(PropMats.WoodLight, tw).Box(tw, new Vector3(1.6f, 1.0f, 0.04f), r);
            for (int i = 0; i < 5; i++)
                batch.W(PropMats.Metal, tw).Box(tw + r * new Vector3(-0.6f + i * 0.3f, 0.05f, 0.04f), new Vector3(0.05f, 0.5f, 0.02f), r * Quaternion.Euler(0f, 0f, (i - 2) * 9f));
            PropKit.RopeCoil(batch, W(-2.4f, 0f, -1.9f), 0.32f);
            // spare rotor blades leaning on the side wall, a red tool cart, oil drums
            for (int i = 0; i < 2; i++)
                batch.W(PropMats.DarkMetal, h).Box(W(2.75f, 1.35f, 0.4f - i * 0.5f), new Vector3(0.07f, 2.6f, 0.28f), r * Quaternion.Euler(0f, 0f, 8f + i * 4f));
            batch.W(PropMats.Flat(PropPal.LighthouseRed), h).Box(W(-2.3f, 0.45f, 0.7f), new Vector3(0.7f, 0.7f, 0.5f), r);
            for (int i = 0; i < 3; i++) batch.W(PropMats.DarkMetal, h).Box(W(-2.3f, 0.3f + i * 0.2f, 0.95f), new Vector3(0.6f, 0.02f, 0.02f), r);
            PropKit.Barrel(batch, cols, W(2.4f, 0f, -2.2f), PropMats.DrumYellow);
            // Wanda's hammock across the back corner (her siesta spot) – also the W1 quest's drum delivery point
            Vector3 ha = W(-2.7f, 1.7f, -2.3f), hb = W(-0.6f, 1.7f, -0.2f);
            PropKit.Hammock(batch, cols, ha, hb, 2, false);
            // the shop sign over the open front + a little delivery board
            Vector3 sign = W(0f, spec.wallH + 0.55f, 2.75f);
            PropKit.Board(batch, sign, r, 2.4f, 0.5f, "CHOPPER SHOP", LowPoly.Hex("#EE7F2D"), LowPoly.Hex("#1E1B2B"));
            Vector3 board = W(3.6f, 0f, 2.6f);
            board.y = G(board);
            PropKit.Sign(batch, cols, board, yaw, "WANDAS|WIRBEL", LowPoly.Hex("#3FB8C4"), LowPoly.Hex("#EDE6DA"), 1.2f, 0.42f);
            // fuel pump next to the pad
            Vector3 pump = W(-3.9f, 0f, 2.4f);
            pump.y = G(pump);
            batch.W(PropMats.Flat(PropPal.LighthouseRed), pump).Box(pump + Vector3.up * 0.65f, new Vector3(0.5f, 1.3f, 0.4f), r);
            batch.W(PropMats.White, pump).Box(pump + Vector3.up * 1.05f + r * new Vector3(0f, 0f, 0.205f), new Vector3(0.36f, 0.25f, 0.01f), r);
            batch.W(PropMats.Ink, pump).Rod(pump + Vector3.up * 0.9f + r * new Vector3(0.26f, 0f, 0f), pump + Vector3.up * 0.4f + r * new Vector3(0.6f, 0f, 0.3f), 0.025f, 0.025f, 4);
            PropCol.Box(cols, pump + Vector3.up * 0.65f, new Vector3(0.55f, 1.3f, 0.45f), r, "FuelPump");
            // a lamp over the door
            PropKit.LanternPost(batch, cols, root, W(2.2f, 0f, 3.4f) - Vector3.up * (floorY - G(W(2.2f, 0f, 3.4f))), yaw + 180f, 2.2f);

            batch.Flush(root, "HangarMesh", ShadowCastingMode.On, 0f);
            excl.Add(h.x, h.z, 5.5f);
            excl.Add(door.x, door.z, 2.5f);
            PropInfo.SetSpot("hangar", W(0f, 0f, 0f), yaw);
            PropInfo.SetSpot("hangar_door", door, yaw);
            PropInfo.SetSpot("hangar_hammock", (ha + hb) * 0.5f - Vector3.up * 0.5f, yaw);
            PropInfo.AddLandSpot(W(0f, spec.wallH + 1.6f, 0f), LandSpotKind.Perch, 2f, "helipad");
            yield break;
        }

        /// Standing oil drum: 8-sided body with two darker rims.
        public static void Drum(PropBatch batch, Vector3 basePos, Material mat)
        {
            const float r = 0.29f, h = 0.88f;
            batch.W(mat, basePos).Cylinder(basePos, Quaternion.identity, r, r, h, 8, false, true);
            var rim = batch.W(PropMats.DarkMetal, basePos);
            rim.Cylinder(basePos + Vector3.up * 0.28f, Quaternion.identity, r + 0.012f, r + 0.012f, 0.05f, 8);
            rim.Cylinder(basePos + Vector3.up * 0.6f, Quaternion.identity, r + 0.012f, r + 0.012f, 0.05f, 8);
        }

        static void Windsock(Transform root, Vector3 groundPos, PropBatch batch)
        {
            const float poleH = 5f;
            batch.B(PropMats.Metal).Cylinder(groundPos - Vector3.up * 0.3f, Quaternion.identity, 0.08f, 0.06f, poleH + 0.3f, 6, false, true);
            var pivot = new GameObject("Windsock");
            pivot.transform.SetParent(root, false);
            pivot.transform.position = groundPos + Vector3.up * poleH;
            var sockT = new GameObject("Sock").transform;
            sockT.SetParent(pivot.transform, false);

            // four orange / white segments (6-sided frustums), each segment its own color
            var orange = new PropMeshBuilder();
            var white = new PropMeshBuilder();
            Quaternion along = Quaternion.FromToRotation(Vector3.up, Vector3.right);
            const int segs = 4;
            const float len = 2.3f, rA = 0.34f, rB = 0.13f;
            for (int i = 0; i < segs; i++)
            {
                float t0 = (float)i / segs, t1 = (float)(i + 1) / segs;
                var b = i % 2 == 0 ? orange : white;
                b.Cylinder(new Vector3(0.12f + t0 * len, 0f, 0f), along, Mathf.Lerp(rA, rB, t0), Mathf.Lerp(rA, rB, t1), len / segs, 6);
                b.Cylinder(new Vector3(0.12f + t0 * len, 0f, 0f), along, Mathf.Lerp(rA, rB, t0) - 0.01f, Mathf.Lerp(rA, rB, t1) - 0.01f, len / segs, 6, false, false, 1f, 1f, 0f, true);
            }
            PropUtil.MeshObject(sockT, "SockOrange", orange.ToMesh("WindsockOrange"), PropMats.Orange, Vector3.zero, Quaternion.identity);
            PropUtil.MeshObject(sockT, "SockWhite", white.ToMesh("WindsockWhite"), PropMats.White, Vector3.zero, Quaternion.identity);
            var hb = new PropMeshBuilder();
            hb.Torus(new Vector3(0.12f, 0f, 0f), along, 0.34f, 0.025f, 8, 3);
            hb.Rod(Vector3.zero, new Vector3(0.12f, 0f, 0f), 0.03f, 0.03f, 4);
            PropUtil.MeshObject(sockT, "Hoop", hb.ToMesh("WindsockHoop"), PropMats.Metal, Vector3.zero, Quaternion.identity);
            var ws = pivot.AddComponent<PropWindsock>();
            ws.sock = sockT;
            ws.baseYaw = 35f;
        }

        // ================================================================== lighthouse
        /// Height of the stone plinth the tower stands on (the cellar of L.6 #11 is inside it).
        const float PlinthH = 2.4f;
        /// Clear height of the round room in the tower base (above it the tower is solid).
        const float RoomH = 2.9f;

        public static IEnumerator BuildLighthouse(Transform island, PropExclusion excl)
        {
            Vector3 c = WorldLayout.Lighthouse;
            PropGround.MinMax(c.x, c.z, 3.8f, out float gmin, out float gmax);
            float baseTop = Mathf.Max(gmax + PlinthH, 1.5f);
            float footY = gmin - 1.2f;
            float cellarY = gmax + 0.06f;

            var root = PropUtil.WorldRoot("Lighthouse", island);
            var cols = new GameObject("Colliders").transform;
            cols.SetParent(root, false);
            var batch = new PropBatch(0f);

            Vector3 toHut = WorldLayout.Hut - c;
            toHut.y = 0f;
            toHut.Normalize();
            // snap the door to the octagon face nearest to the hut (face centres at k·45°) – the opening is cut there
            float doorDeg = Mathf.Round(Mathf.Atan2(toHut.z, toHut.x) * Mathf.Rad2Deg / 45f) * 45f;
            toHut = new Vector3(Mathf.Cos(doorDeg * Mathf.Deg2Rad), 0f, Mathf.Sin(doorDeg * Mathf.Deg2Rad));
            Quaternion dr = Quaternion.LookRotation(toHut);
            Vector3 L(float x, float y, float z) => new Vector3(c.x, 0f, c.z) + dr * new Vector3(x, 0f, z) + Vector3.up * y;

            // ---- stone plinth with the cellar inside (outer wall, cellar floor, top slab with the trapdoor hole)
            var stone = batch.B(PropMats.Stone);
            stone.Cylinder(new Vector3(c.x, footY, c.z), Quaternion.identity, 3.75f, 3.5f, baseTop - footY, 8, false, false, 1f, 1f, 0f, false, 22.5f);
            stone.Cylinder(new Vector3(c.x, cellarY, c.z), Quaternion.identity, 2.95f, 2.95f, baseTop - 0.25f - cellarY, 8, false, false, 1f, 1f, 0f, true, 22.5f);
            batch.B(PropMats.Flat(PropMats.Value(PropPal.Concrete, 0.9f))).Ring(new Vector3(c.x, baseTop + 0.01f, c.z), 2.42f, 3.5f, 8, Quaternion.Euler(0f, -22.5f, 0f));
            batch.B(PropMats.CaveFloor).Disk(new Vector3(c.x, cellarY, c.z), 2.95f, 8, Quaternion.identity, true, 1f, false, 22.5f);
            batch.B(PropMats.WoodDark).Disk(new Vector3(c.x, baseTop - 0.25f, c.z), 2.95f, 8, Quaternion.identity, false, 1f, false, 22.5f);
            var ring = new PropMeshBuilder();
            ring.Cylinder(Vector3.zero, Quaternion.identity, 3.75f, 3.5f, baseTop - footY, 16, false, false);
            ring.Cylinder(new Vector3(0f, cellarY - footY, 0f), Quaternion.identity, 2.95f, 2.95f, baseTop - cellarY, 16, false, false, 1f, 1f, 0f, true);
            ring.DoubleSide(0, 0);
            PropCol.Mesh(cols, ring.ToMesh("LighthousePlinth", false), new Vector3(c.x, footY, c.z), Quaternion.identity, Vector3.one, "Plinth");
            PropCol.Box(cols, new Vector3(c.x, cellarY - 0.3f, c.z), new Vector3(5.9f, 0.6f, 5.9f), Quaternion.identity, "CellarFloor");
            // top slab (ring outside the tower + the room floor with a 0.8 m trapdoor hole at local (0.9, −0.6))
            Vector3 hole = new Vector3(0.75f, 0f, -0.55f);
            SlabWithHole(cols, c, dr, baseTop, hole, 0.8f);
            if (PropRun.Due()) yield return null;

            // ---- chunky 8-sided tower with red / white bands; hollow base room with a door, solid above
            const float H = 15.5f, r0 = 2.4f, r1 = 1.65f;
            const int bands = 6;
            Vector3 tb = new Vector3(c.x, baseTop, c.z);
            for (int i = 0; i < bands; i++)
            {
                float t0 = (float)i / bands, t1 = (float)(i + 1) / bands;
                var m = i % 2 == 0 ? PropMats.White : PropMats.RedMetal;
                if (i == 0) DoorBand(batch.B(m), tb, r0, Mathf.Lerp(r0, r1, t1), H / bands + 0.01f, doorDeg, false);
                else batch.B(m).Cylinder(tb + Vector3.up * (H * t0), Quaternion.identity, Mathf.Lerp(r0, r1, t0), Mathf.Lerp(r0, r1, t1), H / bands + 0.01f, 8, false, false, 1f, 1f, 0f, false, 22.5f);
            }
            // inside of the base room
            float rIn0 = r0 - 0.42f, rIn1 = Mathf.Lerp(r0, r1, RoomH / H) - 0.42f;
            DoorBand(batch.B(PropMats.Whitewash(1)), tb, rIn0, rIn1, RoomH, doorDeg, true);
            DoorReveal(batch.B(PropMats.Whitewash(0)), tb, r0, Mathf.Lerp(r0, r1, 1f / bands), H / bands, rIn0, rIn1, RoomH, doorDeg);
            batch.B(PropMats.WoodDark).Disk(tb + Vector3.up * RoomH, rIn1 + 0.05f, 8, Quaternion.identity, false, 1f, false, 22.5f);
            // walls of the room as boxes around the door gap (colliders), the solid tower above
            RoomWalls(cols, c, dr, baseTop, r0, rIn0);
            var tcb = new PropMeshBuilder();
            tcb.Cylinder(Vector3.zero, Quaternion.identity, Mathf.Lerp(r0, r1, RoomH / H), r1, H - RoomH, 16, true, true);
            PropCol.Mesh(cols, tcb.ToMesh("LighthouseTower", false), tb + Vector3.up * RoomH, Quaternion.identity, Vector3.one, "Tower");

            // door facing the hut path (open), stone stairs down the plinth
            float apothem = r0 * Mathf.Cos(Mathf.PI / 8f);
            batch.B(PropMats.Wood).Box(tb + toHut * (apothem - 0.06f) + Vector3.up * 1.1f + dr * new Vector3(0.62f, 0f, 0f), new Vector3(0.14f, 2.32f, 0.3f), dr);
            batch.B(PropMats.Wood).Box(tb + toHut * (apothem - 0.06f) + Vector3.up * 1.1f + dr * new Vector3(-0.62f, 0f, 0f), new Vector3(0.14f, 2.32f, 0.3f), dr);
            batch.B(PropMats.Wood).Box(tb + toHut * (apothem - 0.06f) + Vector3.up * 2.22f, new Vector3(1.38f, 0.14f, 0.3f), dr);
            Quaternion leaf = dr * Quaternion.Euler(0f, -100f, 0f);
            batch.B(PropMats.Shutter).Box(tb + toHut * (apothem - 0.2f) + dr * new Vector3(-0.55f, 0f, 0f) + Vector3.up * 1.05f + leaf * new Vector3(0.5f, 0f, 0f), new Vector3(1.0f, 2.05f, 0.08f), leaf);
            for (int i = 0; i < 16; i++)
            {
                float stepTop = baseTop - (i + 1) * 0.2f;
                Vector3 p = new Vector3(c.x, 0f, c.z) + toHut * (3.62f + i * 0.3f);
                float g = G(p);
                if (stepTop < g - 0.02f) break;
                float hh = stepTop - (g - 0.3f);
                Vector3 sc = new Vector3(p.x, stepTop - hh * 0.5f, p.z);
                batch.B(i % 2 == 0 ? PropMats.Stone : PropMats.Concrete).Box(sc, new Vector3(1.5f, hh, 0.3f), dr);
                PropCol.Box(cols, sc, new Vector3(1.5f, hh, 0.3f), dr, "Step");
            }
            // small windows spiralling up
            for (int i = 0; i < 4; i++)
            {
                float y = 4.2f + i * 3f;
                float a = 45f * (1 + i * 2) * Mathf.Deg2Rad;   // octagon face centers (vertices sit at 22.5° + k·45°)
                Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                float rr = Mathf.Lerp(r0, r1, y / H) * Mathf.Cos(Mathf.PI / 8f);
                Quaternion wr = Quaternion.LookRotation(d);
                batch.B(PropMats.Trim).Box(tb + d * (rr - 0.02f) + Vector3.up * y, new Vector3(0.7f, 0.95f, 0.16f), wr);
                batch.B(PropMats.LanternRoom).Box(tb + d * (rr + 0.04f) + Vector3.up * y, new Vector3(0.48f, 0.72f, 0.06f), wr);
            }
            if (PropRun.Due()) yield return null;

            // ---- the base room (rug + trapdoor, oil cans, Gus' raincoat, a closed spiral stair) and the cellar
            RoomAndCellar(root, batch, cols, c, dr, baseTop, cellarY, hole, rIn0);
            if (PropRun.Due()) yield return null;

            // ---- gallery: chunky platform with a simple railing
            float gy = baseTop + H;
            var dm = batch.B(PropMats.DarkMetal);
            dm.Cylinder(new Vector3(c.x, gy - 0.6f, c.z), Quaternion.identity, r1 - 0.05f, 2.8f, 0.6f, 8, false, false, 1f, 1f, 0f, false, 22.5f);
            dm.Cylinder(new Vector3(c.x, gy, c.z), Quaternion.identity, 2.8f, 2.8f, 0.16f, 8, false, true, 1f, 1f, 0f, false, 22.5f);
            const int posts = 8;
            for (int i = 0; i < posts; i++)
            {
                float a0 = (22.5f + i * 45f) * Mathf.Deg2Rad, a1 = (22.5f + (i + 1) * 45f) * Mathf.Deg2Rad;
                Vector3 p0 = new Vector3(c.x + Mathf.Cos(a0) * 2.68f, gy + 0.16f, c.z + Mathf.Sin(a0) * 2.68f);
                Vector3 p1 = new Vector3(c.x + Mathf.Cos(a1) * 2.68f, gy + 0.16f, c.z + Mathf.Sin(a1) * 2.68f);
                dm.Rod(p0, p0 + Vector3.up * 1.0f, 0.045f, 0.045f, 4);
                dm.Beam(p0 + Vector3.up * 1.0f, p1 + Vector3.up * 1.0f, 0.08f, 0.08f);
                dm.Beam(p0 + Vector3.up * 0.5f, p1 + Vector3.up * 0.5f, 0.05f, 0.05f);
                // solid railing segment
                Vector3 seg = p1 - p0;
                PropCol.Box(cols, (p0 + p1) * 0.5f + Vector3.up * 0.55f, new Vector3(0.12f, 1.1f, seg.magnitude), Quaternion.LookRotation(seg.normalized), "GalleryRail");
                PropInfo.AddLandSpot(p0 + Vector3.up * 1.05f, LandSpotKind.Railing, 0.5f, "lighthouse");
            }
            // walkable gallery floor
            var gcb = new PropMeshBuilder();
            gcb.Cylinder(Vector3.zero, Quaternion.identity, 2.8f, 2.8f, 0.16f, 8, true, true, 1f, 1f, 0f, false, 22.5f);
            var gmc = PropCol.Mesh(cols, gcb.ToMesh("LighthouseGallery", false), new Vector3(c.x, gy, c.z), Quaternion.identity, Vector3.one, "GalleryFloor");
            gmc.convex = true;
            PropInfo.LighthouseGallery = new Vector3(c.x, gy + 0.16f, c.z);

            // lamp room: dark lantern room, glass, mullions, red cone roof with a ball
            float ly = gy + 0.16f;
            batch.B(PropMats.LanternRoom).Cylinder(new Vector3(c.x, ly, c.z), Quaternion.identity, 1.4f, 1.4f, 0.55f, 8, false, false, 1f, 1f, 0f, false, 22.5f);
            var lrb = new PropMeshBuilder();
            lrb.Cylinder(Vector3.zero, Quaternion.identity, 1.42f, 1.42f, 2.4f, 8, true, true, 1f, 1f, 0f, false, 22.5f);
            var lrc = PropCol.Mesh(cols, lrb.ToMesh("LanternRoomCol", false), new Vector3(c.x, ly, c.z), Quaternion.identity, Vector3.one, "LanternRoom");
            lrc.convex = true;
            batch.B(PropMats.Glass).Cylinder(new Vector3(c.x, ly + 0.55f, c.z), Quaternion.identity, 1.32f, 1.32f, 1.8f, 8, false, false, 1f, 1f, 0f, false, 22.5f);
            for (int i = 0; i < 8; i++)
            {
                float a = (22.5f + i * 45f) * Mathf.Deg2Rad;
                Vector3 p = new Vector3(c.x + Mathf.Cos(a) * 1.33f, ly + 0.55f, c.z + Mathf.Sin(a) * 1.33f);
                dm.Rod(p, p + Vector3.up * 1.8f, 0.06f, 0.06f, 4);
            }
            float roofY = ly + 2.35f;
            batch.B(PropMats.RedMetal).Cylinder(new Vector3(c.x, roofY, c.z), Quaternion.identity, 1.7f, 0f, 1.35f, 8, true, false, 1f, 1f, 0f, false, 22.5f);
            dm.Cylinder(new Vector3(c.x, roofY - 0.1f, c.z), Quaternion.identity, 1.5f, 1.5f, 0.1f, 8, true, false, 1f, 1f, 0f, false, 22.5f);
            dm.Ico(new Vector3(c.x, roofY + 1.45f, c.z), Vector3.one * 0.22f, Quaternion.identity, 0);
            dm.Rod(new Vector3(c.x, roofY + 1.6f, c.z), new Vector3(c.x, roofY + 2.6f, c.z), 0.035f, 0.02f, 4);
            PropInfo.AddLandSpot(new Vector3(c.x, roofY + 1.7f, c.z), LandSpotKind.Perch, 0.5f, "lighthouse");

            // lamp: lens (static, emissive) + rotating pivot with beams and spot lights
            Vector3 lampC = new Vector3(c.x, ly + 1.35f, c.z);
            batch.B(PropMats.Lens).Cylinder(lampC - Vector3.up * 0.4f, Quaternion.identity, 0.42f, 0.42f, 0.8f, 8, true, true);
            dm.Cylinder(new Vector3(c.x, ly + 0.55f, c.z), Quaternion.identity, 0.26f, 0.2f, 0.42f, 6, false, true);

            var pivot = new GameObject("LighthouseLamp");
            pivot.transform.SetParent(root, false);
            pivot.transform.position = lampC;
            var lamp = pivot.AddComponent<PropLighthouseLamp>();
            lamp.pivot = pivot.transform;
            lamp.lens = PropMats.Lens;

            var beamMat = new Material(MaterialLibrary.UnlitShader) { name = "Prop_LighthouseBeam" };
            beamMat.SetTexture("_BaseMap", PropTex.Beam);
            beamMat.SetColor("_BaseColor", new Color(1f, 0.95f, 0.75f, 0f));
            MaterialLibrary.MakeTransparent(beamMat, true);
            beamMat.SetFloat("_DstBlend", (float)BlendMode.One);
            lamp.beamMat = beamMat;

            var beams = new Renderer[2];
            var spots = new Light[2];
            for (int i = 0; i < 2; i++)
            {
                Vector3 fwd = i == 0 ? Vector3.forward : Vector3.back;
                // pitch sign flips for the backward beam so both tilt slightly downward
                Quaternion tiltDown = Quaternion.Euler(i == 0 ? 2.5f : -2.5f, 0f, 0f);
                var bb = new PropMeshBuilder();
                bb.Cylinder(Vector3.zero, Quaternion.FromToRotation(Vector3.up, fwd), 0.3f, 8f, 110f, 10, false, false, 1f, 1f / 110f);
                var mr = PropUtil.MeshObject(pivot.transform, "Beam" + i, bb.ToMesh("LighthouseBeam", false), beamMat, Vector3.zero, tiltDown, false);
                mr.receiveShadows = false;
                mr.enabled = false;
                beams[i] = mr;

                var sgo = new GameObject("Spot" + i);
                sgo.transform.SetParent(pivot.transform, false);
                sgo.transform.localRotation = Quaternion.LookRotation(tiltDown * fwd);
                var sl = sgo.AddComponent<Light>();
                sl.type = LightType.Spot;
                sl.color = new Color(1f, 0.93f, 0.75f);
                sl.range = 180f;
                sl.spotAngle = 10f;
                sl.innerSpotAngle = 4f;
                sl.intensity = 0f;
                sl.shadows = LightShadows.None;
                sl.enabled = false;
                spots[i] = sl;
            }
            lamp.beams = beams;
            lamp.spots = spots;
            lamp.spotIntensity = 60f;
            var glow = PropUtil.AddPointLight(pivot.transform, lampC, new Color(1f, 0.85f, 0.6f), 14f, 0f, "LampGlow");
            glow.enabled = false;
            lamp.glow = glow;
            PropInfo.SetSpot("lighthouse_lamp", lampC);

            LighthouseArea(root, batch, cols, c, baseTop, toHut, gy, H, r0, r1, roofY, ly);

            batch.Flush(root, "LighthouseMesh", ShadowCastingMode.On, 0f);
            excl.Add(c.x, c.z, 8f);
            excl.AddLine(c, c + toHut * 7.5f, 2f);
        }

        const float DoorW = 1.1f, DoorH = 2.15f;

        /// An octagonal band (vertices at 22.5° + k·45°, like the tower cylinders) with the door opening cut into the
        /// face centred at doorDeg. inner = the faces point inward (the room's inside).
        static void DoorBand(PropMeshBuilder b, Vector3 baseC, float rBottom, float rTop, float h, float doorDeg, bool inner)
        {
            float R(float y) => Mathf.Lerp(rBottom, rTop, y / h);
            for (int k = 0; k < 8; k++)
            {
                float a0 = (22.5f + k * 45f) * Mathf.Deg2Rad, a1 = a0 + 45f * Mathf.Deg2Rad;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                float cDeg = 45f + k * 45f;
                Vector3 outward = new Vector3(Mathf.Cos(cDeg * Mathf.Deg2Rad), 0f, Mathf.Sin(cDeg * Mathf.Deg2Rad)) * (inner ? -1f : 1f);
                Vector3 P(float t, float y) => baseC + Vector3.up * y + Vector3.Lerp(d0, d1, t) * R(y);
                if (Mathf.Abs(Mathf.DeltaAngle(cDeg, doorDeg)) > 1f)
                {
                    b.QuadAuto(P(0f, 0f), P(1f, 0f), P(1f, h), P(0f, h), Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, outward);
                    continue;
                }
                float faceW = (P(1f, 0f) - P(0f, 0f)).magnitude;
                float tL = 0.5f - DoorW * 0.5f / faceW, tR = 0.5f + DoorW * 0.5f / faceW;
                float dh = Mathf.Min(DoorH, h - 0.05f);
                b.QuadAuto(P(0f, 0f), P(tL, 0f), P(tL, dh), P(0f, dh), Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, outward);
                b.QuadAuto(P(tR, 0f), P(1f, 0f), P(1f, dh), P(tR, dh), Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, outward);
                b.QuadAuto(P(0f, dh), P(1f, dh), P(1f, h), P(0f, h), Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, outward);
            }
        }

        /// The door's reveal: jamb sides and the lintel underside between the outer band and the room's inner shell.
        static void DoorReveal(PropMeshBuilder b, Vector3 baseC, float rOb, float rOt, float hO, float rIb, float rIt, float hI, float doorDeg)
        {
            float k = (doorDeg - 45f) / 45f;
            float a0 = (22.5f + k * 45f) * Mathf.Deg2Rad, a1 = a0 + 45f * Mathf.Deg2Rad;
            Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
            Vector3 PO(float t, float y) => baseC + Vector3.up * y + Vector3.Lerp(d0, d1, t) * Mathf.Lerp(rOb, rOt, y / hO);
            Vector3 PI(float t, float y) => baseC + Vector3.up * y + Vector3.Lerp(d0, d1, t) * Mathf.Lerp(rIb, rIt, y / hI);
            float wO = (PO(1f, 0f) - PO(0f, 0f)).magnitude, wI = (PI(1f, 0f) - PI(0f, 0f)).magnitude;
            float tLO = 0.5f - DoorW * 0.5f / wO, tRO = 0.5f + DoorW * 0.5f / wO, tLI = 0.5f - DoorW * 0.5f / wI, tRI = 0.5f + DoorW * 0.5f / wI;
            Vector3 dir = new Vector3(Mathf.Cos(doorDeg * Mathf.Deg2Rad), 0f, Mathf.Sin(doorDeg * Mathf.Deg2Rad));
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            b.QuadAuto(PO(tLO, 0f), PI(tLI, 0f), PI(tLI, DoorH), PO(tLO, DoorH), Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, side);
            b.QuadAuto(PO(tRO, 0f), PI(tRI, 0f), PI(tRI, DoorH), PO(tRO, DoorH), Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, -side);
            b.QuadAuto(PO(tLO, DoorH), PO(tRO, DoorH), PI(tRI, DoorH), PI(tLI, DoorH), Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector3.down);
        }

        /// Plinth top: the walkway ring outside the tower + the base room floor with a square trapdoor hole (colliders).
        static void SlabWithHole(Transform cols, Vector3 c, Quaternion dr, float top, Vector3 holeLocal, float hs)
        {
            Vector3 L(float x, float z) => new Vector3(c.x, top - 0.12f, c.z) + dr * new Vector3(x, 0f, z);
            // the room floor: four boxes around the hole inside the square [-R, R]² (R ≤ the room radius, so no box
            // corner sticks out of the octagonal plinth)
            const float R = 2.1f;
            float hx0 = holeLocal.x - hs * 0.5f, hx1 = holeLocal.x + hs * 0.5f, hz0 = holeLocal.z - hs * 0.5f, hz1 = holeLocal.z + hs * 0.5f;
            PropCol.Box(cols, L((-R + hx0) * 0.5f, 0f), new Vector3(hx0 + R, 0.24f, 2f * R), dr, "Slab");
            PropCol.Box(cols, L((hx1 + R) * 0.5f, 0f), new Vector3(R - hx1, 0.24f, 2f * R), dr, "Slab");
            PropCol.Box(cols, L(holeLocal.x, (-R + hz0) * 0.5f), new Vector3(hs, 0.24f, hz0 + R), dr, "Slab");
            PropCol.Box(cols, L(holeLocal.x, (hz1 + R) * 0.5f), new Vector3(hs, 0.24f, R - hz1), dr, "Slab");
            // the walkway ring outside the tower (r 2.3 … 3.5): one box per octagon side (vertices at 22.5° + k·45°)
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f * Mathf.Deg2Rad;
                Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 m = new Vector3(c.x, top - 0.12f, c.z) + d * 2.75f;
                PropCol.Box(cols, m, new Vector3(2.6f, 0.24f, 0.95f), Quaternion.LookRotation(d), "Walkway");
            }
        }

        /// The base room walls (8 wall boxes, the one facing the hut has the door gap) – colliders only.
        static void RoomWalls(Transform cols, Vector3 c, Quaternion dr, float baseTop, float r0, float rIn)
        {
            float mid = (r0 + rIn) * 0.5f, th = r0 - rIn;
            for (int i = 0; i < 8; i++)
            {
                float a = (i * 45f) * Mathf.Deg2Rad;
                Vector3 d = dr * new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                float faceW = 2f * r0 * Mathf.Sin(Mathf.PI / 8f) + 0.1f;
                Quaternion q = Quaternion.LookRotation(d);
                Vector3 center = new Vector3(c.x, baseTop + RoomH * 0.5f, c.z) + d * mid;
                if (i != 0) { PropCol.Box(cols, center, new Vector3(faceW, RoomH, th), q, "RoomWall"); continue; }
                // door face: two jambs + lintel (door 1.1 m wide, 2.15 m high)
                float side = (faceW - 1.1f) * 0.5f;
                PropCol.Box(cols, center + q * new Vector3(-(1.1f + side) * 0.5f, 0f, 0f), new Vector3(side, RoomH, th), q, "RoomWall");
                PropCol.Box(cols, center + q * new Vector3((1.1f + side) * 0.5f, 0f, 0f), new Vector3(side, RoomH, th), q, "RoomWall");
                PropCol.Box(cols, center + q * new Vector3(0f, (2.15f + RoomH) * 0.5f - RoomH * 0.5f, 0f), new Vector3(1.1f, RoomH - 2.15f, th), q, "RoomLintel");
            }
            PropCol.Box(cols, new Vector3(c.x, baseTop + RoomH + 0.1f, c.z), new Vector3(2f * rIn, 0.2f, 2f * rIn), dr, "RoomCeiling");
        }

        /// Base room: rug over the trapdoor (PropTrapdoor), oil cans, a raincoat, a closed spiral stair. Cellar: Gus'
        /// 1987 broadcast desk with plans and a chart (secret lighthouse_cellar), the golden microphone (treasure),
        /// tape shelves and a dim bulb.
        static void RoomAndCellar(Transform root, PropBatch batch, Transform cols, Vector3 c, Quaternion dr, float baseTop, float cellarY, Vector3 hole, float rIn)
        {
            Vector3 L(float x, float y, float z) => new Vector3(c.x, y, c.z) + dr * new Vector3(x, 0f, z);
            // wooden floor boards of the room (the hole stays open under the hatch)
            for (float x = -rIn + 0.15f; x < rIn; x += 0.3f)
            {
                for (float z = -rIn + 0.15f; z < rIn; z += 0.6f)
                {
                    if (x * x + z * z > rIn * rIn) continue;
                    if (Mathf.Abs(x - hole.x) < 0.45f && Mathf.Abs(z - hole.z) < 0.45f) continue;
                    batch.W(((int)(x * 10f + z * 3f) & 1) == 0 ? PropMats.Wood : PropMats.Wood2, c).Box(L(x, baseTop + 0.02f, z), new Vector3(0.28f, 0.04f, 0.58f), dr);
                }
            }
            // trapdoor hatch (hinged at its −z edge) and the rug lying on it
            var trap = new GameObject("Trapdoor");
            trap.transform.SetParent(root, false);
            trap.transform.SetPositionAndRotation(L(hole.x, baseTop + 0.05f, hole.z), dr);
            var hingeGo = new GameObject("Hatch").transform;
            hingeGo.SetParent(trap.transform, false);
            hingeGo.localPosition = new Vector3(0f, 0f, -0.42f);
            var hm = new PropMeshBuilder();
            hm.Box(new Vector3(0f, -0.02f, 0.42f), new Vector3(0.82f, 0.05f, 0.82f));
            hm.Box(new Vector3(0f, 0.01f, 0.7f), new Vector3(0.08f, 0.03f, 0.06f));
            PropUtil.MeshObject(hingeGo, "HatchMesh", hm.ToMesh("Hatch"), PropMats.WoodDark, Vector3.zero, Quaternion.identity);
            var hatchCol = hingeGo.gameObject.AddComponent<BoxCollider>();
            hatchCol.center = new Vector3(0f, -0.02f, 0.42f);
            hatchCol.size = new Vector3(0.82f, 0.06f, 0.82f);
            var rugT = new GameObject("Rug").transform;
            rugT.SetParent(trap.transform, false);
            rugT.localPosition = new Vector3(0f, 0.02f, 0f);
            var rm = new PropMeshBuilder();
            rm.Box(Vector3.zero, new Vector3(1.6f, 0.02f, 1.2f));
            PropUtil.MeshObject(rugT, "RugA", rm.ToMesh("LighthouseRug"), PropMats.RugA, Vector3.zero, Quaternion.identity);
            var rs = new PropMeshBuilder();
            for (int i = -1; i <= 1; i++) rs.Box(new Vector3(i * 0.45f, 0.012f, 0f), new Vector3(0.14f, 0.006f, 1.2f));
            PropUtil.MeshObject(rugT, "RugB", rs.ToMesh("LighthouseRugStripes"), PropMats.RugB, Vector3.zero, Quaternion.identity);
            var tsc = trap.AddComponent<BoxCollider>();
            tsc.isTrigger = true;
            tsc.center = new Vector3(0f, 0.15f, 0f);
            tsc.size = new Vector3(1.4f, 0.4f, 1.1f);
            var td = trap.AddComponent<PropTrapdoor>();
            td.rug = rugT;
            td.hatch = hingeGo;
            // the cellar ladder (enabled once the hatch is open)
            var ladderRoot = new GameObject("CellarLadder");
            ladderRoot.transform.SetParent(root, false);
            Vector3 lf = L(hole.x, cellarY, hole.z + 0.3f);
            var lm = new PropMeshBuilder();
            for (int s = -1; s <= 1; s += 2) lm.Box(new Vector3(s * 0.25f, (baseTop + 0.8f - cellarY) * 0.5f, 0f), new Vector3(0.06f, baseTop + 0.8f - cellarY, 0.06f));
            for (float y = 0.3f; y < baseTop - cellarY + 0.5f; y += 0.3f) lm.Box(new Vector3(0f, y, 0f), new Vector3(0.5f, 0.04f, 0.05f));
            var lmr = PropUtil.MeshObject(ladderRoot.transform, "Ladder", lm.ToMesh("CellarLadder"), PropMats.Wood, Vector3.zero, Quaternion.identity);
            lmr.transform.SetPositionAndRotation(lf, dr);
            var cl = PropLadder.Create(ladderRoot.transform, "CellarLadderClimb", lf, baseTop + 0.8f, L(hole.x, baseTop + 0.05f, hole.z - 0.8f), dr * Vector3.back, LadderInfo.Wood);
            cl.proximityTop = true;
            cl.proximityBottom = true;
            td.ladder = ladderRoot;
            PropInfo.SetSpot("cellar", L(0f, cellarY, 0f));

            // room dressing: oil cans, raincoat hook, a closed spiral stair with a chain
            for (int i = 0; i < 3; i++) PropKit.Barrel(batch, null, L(-1.2f + i * 0.4f, baseTop, 1.3f - i * 0.15f) , i == 1 ? PropMats.DrumRed : PropMats.DrumYellow);
            Vector3 coat = L(-1.75f, baseTop + 1.4f, -0.4f);
            batch.W(PropMats.Flat(LowPoly.Hex("#F2C94C")), coat).Box(coat - Vector3.up * 0.45f, new Vector3(0.5f, 0.9f, 0.18f), dr * Quaternion.Euler(0f, 90f, 0f));
            batch.W(PropMats.Flat(LowPoly.Hex("#F2C94C")), coat).Cylinder(coat + Vector3.up * 0.02f, Quaternion.identity, 0.16f, 0.18f, 0.16f, 6, true, true);
            Vector3 stair = L(0.3f, baseTop, 1.1f);
            for (int i = 0; i < 9; i++)
            {
                Quaternion q = dr * Quaternion.Euler(0f, i * 40f, 0f);
                batch.W(PropMats.DarkMetal, stair).Box(stair + Vector3.up * (0.2f + i * 0.28f) + q * new Vector3(0.45f, 0f, 0f), new Vector3(0.8f, 0.05f, 0.32f), q);
            }
            batch.W(PropMats.DarkMetal, stair).Cylinder(stair, Quaternion.identity, 0.06f, 0.06f, RoomH, 6);
            batch.W(PropMats.Rope, stair).Rod(stair + Vector3.up * 0.9f + dr * new Vector3(-0.6f, 0f, -0.5f), stair + Vector3.up * 0.9f + dr * new Vector3(0.7f, 0f, -0.4f), 0.02f, 0.02f, 3);
            PropCol.Capsule(cols, stair + Vector3.up * RoomH * 0.5f, 0.85f, RoomH, Quaternion.identity, "SpiralStair");
            var bulb = PropUtil.AddPointLight(root, L(0f, baseTop + 2.4f, 0f), new Color(1f, 0.82f, 0.6f), 5f, 1.1f, "RoomBulb");
            PropNearLight.Attach(bulb, 30f);

            // cellar: broadcast desk + chart + tapes; dim bulb
            Vector3 desk = L(-1.2f, cellarY, -0.9f);
            PropKit.Table(batch, cols, desk, Mathf.Atan2(dr.eulerAngles.y, 1f) * 0f + dr.eulerAngles.y + 40f, 1.3f, 0.7f, PropMats.Wood2);
            batch.W(PropMats.Flat(PropPal.HullBlue), desk).Box(desk + Vector3.up * 0.95f, new Vector3(0.6f, 0.36f, 0.4f), dr * Quaternion.Euler(0f, 40f, 0f));
            batch.W(PropMats.Paper, desk).Box(desk + Vector3.up * 0.78f + dr * new Vector3(0.35f, 0f, 0.1f), new Vector3(0.5f, 0.01f, 0.36f), dr * Quaternion.Euler(0f, 52f, 0f));
            for (int i = 0; i < 3; i++)
            {
                Vector3 sh = L(1.6f, cellarY + 0.3f + i * 0.5f, 0.9f);
                batch.W(PropMats.WoodLight, sh).Box(sh, new Vector3(1.0f, 0.05f, 0.35f), dr * Quaternion.Euler(0f, -45f, 0f));
                for (int k = 0; k < 6; k++)
                    batch.W(k % 2 == 0 ? PropMats.Ink : PropMats.Flat(PropPal.HullBlue), sh).Cylinder(sh + Vector3.up * 0.03f + dr * Quaternion.Euler(0f, -45f, 0f) * new Vector3(-0.4f + k * 0.16f, 0f, 0f), Quaternion.Euler(0f, 0f, 90f), 0.11f, 0.11f, 0.03f, 8, true, true);
            }
            var cb = PropUtil.AddPointLight(root, L(0f, baseTop - 0.6f, 0f), new Color(1f, 0.7f, 0.4f), 4.5f, 0.6f, "CellarBulb");
            PropNearLight.Attach(cb, 25f);
            PropInfo.SetSpot("cellar_desk", desk + Vector3.up * 0.8f);
        }

        static void FenceCol(Transform cols, Vector3 a, Vector3 b)
        {
            Vector3 seg = b - a;
            seg.y = 0f;
            if (seg.sqrMagnitude < 0.01f) return;
            PropCol.Box(cols, (a + b) * 0.5f + Vector3.up * 0.5f, new Vector3(0.1f, 1.1f, seg.magnitude + 0.1f), Quaternion.LookRotation(seg.normalized), "Fence");
        }

        /// Lighthouse surroundings: the wall ladder up to the gallery (sea side), the maintenance ladder to the roof
        /// platform (L.6 #3), the rope ladder down the north cliff to the beach (Q7), the picket fence with gates toward
        /// the path and the rope ladder, a bench facing the sea, a picnic table, flower boxes and the coin telescope.
        static void LighthouseArea(Transform root, PropBatch batch, Transform cols, Vector3 c, float baseTop, Vector3 toHut, float gy, float H, float r0, float r1, float roofY, float ly)
        {
            var rng = new System.Random(808);
            // ---- ladder on the sea side (opposite the door)
            Vector3 outDir = -toHut;
            Vector3 side = Vector3.Cross(Vector3.up, outDir).normalized;
            float lr = 2.95f;
            Vector3 foot = new Vector3(c.x, baseTop, c.z) + outDir * lr;
            float topY = gy + 1.25f;
            var dm = batch.B(PropMats.DarkMetal);
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 rb = foot + side * (s * 0.3f);
                dm.Beam(rb - Vector3.up * 0.1f, new Vector3(rb.x, topY, rb.z), 0.06f, 0.06f);
                dm.Beam(new Vector3(rb.x, topY, rb.z), new Vector3(rb.x, topY, rb.z) - outDir * 0.35f, 0.05f, 0.05f);
            }
            for (float y = baseTop + 0.3f; y < topY - 0.1f; y += 0.3f)
                dm.Rod(foot - side * 0.3f + Vector3.up * (y - baseTop), foot + side * 0.3f + Vector3.up * (y - baseTop), 0.025f, 0.025f, 4);
            // brackets to the tower wall
            for (float y = 1.5f; y < H; y += 3.5f)
            {
                float wr = Mathf.Lerp(r0, r1, y / H) * Mathf.Cos(Mathf.PI / 8f);
                Vector3 a = new Vector3(c.x, baseTop + y, c.z) + outDir * wr;
                Vector3 b = new Vector3(c.x, baseTop + y, c.z) + outDir * lr;
                dm.Beam(a + side * 0.3f, b + side * 0.3f, 0.05f, 0.05f);
                dm.Beam(a - side * 0.3f, b - side * 0.3f, 0.05f, 0.05f);
            }
            var ladder = PropLadder.Create(root, "LighthouseLadder", foot, topY, new Vector3(c.x, gy + 0.2f, c.z) + outDir * 2.15f, outDir, LadderInfo.Metal);
            ladder.bottomStand = new Vector3(foot.x, baseTop + 0.05f, foot.z) + outDir * 0.25f + side * 0.55f;
            ladder.proximityTop = true;      // the gallery railing may block the ray to the ladder top
            ladder.proximityBottom = true;
            PropInfo.LighthouseLadderFoot = foot;

            // ---- L.6 #3: maintenance ladder on the sea side of the lantern room up to a little roof platform
            Vector3 seaDir = (Quaternion.Euler(0f, 35f, 0f) * outDir).normalized;
            Vector3 seaSide = Vector3.Cross(Vector3.up, seaDir).normalized;
            float platY = roofY + 0.25f;
            Vector3 mFoot = new Vector3(c.x, ly, c.z) + seaDir * 1.62f;
            var mm = batch.B(PropMats.Metal);
            for (int s = -1; s <= 1; s += 2)
                mm.Rod(mFoot + seaSide * (s * 0.22f), new Vector3(mFoot.x, platY + 1.0f, mFoot.z) + seaSide * (s * 0.22f), 0.025f, 0.025f, 4);
            for (float y = ly + 0.3f; y < platY + 0.8f; y += 0.3f)
                mm.Rod(new Vector3(mFoot.x, y, mFoot.z) - seaSide * 0.22f, new Vector3(mFoot.x, y, mFoot.z) + seaSide * 0.22f, 0.018f, 0.018f, 4);
            // platform hugging the cone: a small grate with a railing
            Vector3 plat = new Vector3(c.x, platY, c.z) + seaDir * 1.25f;
            mm.Box(plat, new Vector3(1.3f, 0.06f, 1.0f), Quaternion.LookRotation(seaDir));
            Quaternion pq = Quaternion.LookRotation(seaDir);
            for (int s = -1; s <= 1; s += 2)
                mm.Rod(plat + pq * new Vector3(s * 0.62f, 0f, 0.45f), plat + pq * new Vector3(s * 0.62f, 0.9f, 0.45f), 0.025f, 0.025f, 4);
            mm.Rod(plat + pq * new Vector3(-0.62f, 0.9f, 0.45f), plat + pq * new Vector3(0.62f, 0.9f, 0.45f), 0.025f, 0.025f, 4);
            PropCol.Box(cols, plat, new Vector3(1.3f, 0.12f, 1.0f), pq, "RoofPlatform");
            PropCol.Box(cols, plat + pq * new Vector3(0f, 0.5f, 0.5f), new Vector3(1.3f, 1.0f, 0.06f), pq, "RoofRail");
            var ml = PropLadder.Create(root, "MaintenanceLadder", mFoot, platY + 1.0f, plat + Vector3.up * 0.05f - seaDir * 0.1f, seaDir, LadderInfo.Metal);
            ml.bottomStand = new Vector3(mFoot.x, ly + 0.04f, mFoot.z) + seaDir * 0.5f;
            ml.proximityTop = true;
            ml.proximityBottom = true;
            PropInfo.SetSpot("lighthouse_roof", plat + Vector3.up * 0.06f);
            // the carving sits on the cone right above the platform (PropCollect places the secret there)
            PropInfo.SetSpot("lighthouse_carving", new Vector3(c.x, platY + 0.55f, c.z) + seaDir * 0.85f, Mathf.Atan2(seaDir.x, seaDir.z) * Mathf.Rad2Deg + 180f);

            // ---- Q7: rope ladder from the cliff edge north of the lighthouse down to the north beach
            bool rope = PlanRopeLadder(c, out var ropePlan);
            float ropeAngle = 0f;
            if (rope)
            {
                ropeAngle = Mathf.Atan2(ropePlan.dir.z, ropePlan.dir.x);
                RopeLadder(root, batch, cols, ropePlan);
            }

            // ---- picket fence around the plateau, gates toward the path and the rope ladder
            float fr = 8.6f;
            int n = 72;
            var picket = batch.B(PropMats.Trim);
            Vector3 prev = Vector3.zero, colStart = Vector3.zero;
            bool havePrev = false, haveCol = false;
            int colCount = 0;
            float gateA = Mathf.Atan2(toHut.z, toHut.x);
            for (int i = 0; i <= n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                float da = Mathf.DeltaAngle(a * Mathf.Rad2Deg, gateA * Mathf.Rad2Deg);
                bool gate = Mathf.Abs(da) < 11f || (rope && Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, ropeAngle * Mathf.Rad2Deg)) < 9f);
                Vector3 p = new Vector3(c.x + Mathf.Cos(a) * fr, 0f, c.z + Mathf.Sin(a) * fr);
                p.y = PropGround.H(p.x, p.z);
                if (gate)
                {
                    if (haveCol && colCount > 0) FenceCol(cols, colStart, prev);
                    havePrev = false;
                    haveCol = false;
                    continue;
                }
                Quaternion pr = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f);
                picket.Box(p + Vector3.up * 0.42f, new Vector3(0.08f, 0.95f, 0.035f), pr);
                picket.Face3(p + Vector3.up * 0.9f + pr * new Vector3(-0.04f, 0f, 0.018f), p + Vector3.up * 0.9f + pr * new Vector3(0.04f, 0f, 0.018f), p + Vector3.up * 1.0f + pr * new Vector3(0f, 0f, 0.018f), pr * Vector3.forward);
                if (havePrev)
                {
                    picket.Beam(prev + Vector3.up * 0.3f, p + Vector3.up * 0.3f, 0.05f, 0.07f);
                    picket.Beam(prev + Vector3.up * 0.72f, p + Vector3.up * 0.72f, 0.05f, 0.07f);
                }
                if (!haveCol) { colStart = p; haveCol = true; colCount = 0; }
                else if (++colCount >= 3) { FenceCol(cols, colStart, p); colStart = p; colCount = 0; }
                if (i % 6 == 0) PropInfo.AddLandSpot(p + Vector3.up * 1.0f, LandSpotKind.Perch, 0.4f, "lighthouse");
                prev = p;
                havePrev = true;
            }
            if (haveCol && colCount > 0) FenceCol(cols, colStart, prev);

            // ---- bench facing the sea, a picnic table and the coin telescope
            Vector3 benchDir = Quaternion.Euler(0f, 35f, 0f) * outDir;
            Vector3 bp = new Vector3(c.x, 0f, c.z) + benchDir * 6.4f;
            bp.y = PropGround.H(bp.x, bp.z);
            float bYaw = Mathf.Atan2(benchDir.x, benchDir.z) * Mathf.Rad2Deg;
            PropKit.Bench(batch, cols, root, bp, bYaw, true);
            PropInfo.LighthouseBench = bp + Vector3.up * 0.5f;

            Vector3 pt = new Vector3(c.x, 0f, c.z) + (Quaternion.Euler(0f, -70f, 0f) * toHut) * 6.2f;
            pt.y = PropGround.H(pt.x, pt.z);
            PropKit.PicnicTable(batch, cols, pt, Mathf.Atan2(toHut.x, toHut.z) * Mathf.Rad2Deg + 20f);
            batch.B(PropMats.Flat(PropPal.Coral)).Box(pt + Vector3.up * 0.79f, new Vector3(0.4f, 0.03f, 0.3f), Quaternion.Euler(0f, 25f, 0f));
            PropInfo.SetSpot("lighthouse_picnic", pt);

            Vector3 telDir = Quaternion.Euler(0f, -40f, 0f) * outDir;
            Vector3 tp = new Vector3(c.x, 0f, c.z) + telDir * 7.2f;
            tp.y = PropGround.H(tp.x, tp.z);
            var met = batch.B(PropMats.Flat(PropPal.HullBlue));
            met.Cylinder(tp - Vector3.up * 0.1f, Quaternion.identity, 0.16f, 0.1f, 1.15f, 6, false, true);
            met.Cylinder(tp + Vector3.up * 1.05f, Quaternion.identity, 0.08f, 0.08f, 0.12f, 6, false, true);
            Quaternion tr = Quaternion.LookRotation(telDir) * Quaternion.Euler(-12f, 0f, 0f);
            Vector3 tc = tp + Vector3.up * 1.28f;
            batch.B(PropMats.Flat(PropPal.Sun)).Cylinder(tc - tr * Vector3.forward * 0.35f, tr * Quaternion.Euler(90f, 0f, 0f), 0.11f, 0.13f, 0.7f, 8, true, true);
            batch.B(PropMats.Ink).Cylinder(tc + tr * Vector3.forward * 0.35f, tr * Quaternion.Euler(90f, 0f, 0f), 0.135f, 0.135f, 0.04f, 8, true, true);
            met.Box(tc - Vector3.up * 0.14f, new Vector3(0.18f, 0.16f, 0.22f), Quaternion.LookRotation(telDir));
            var tgo = new GameObject("CoinTelescope");
            tgo.transform.SetParent(root, false);
            tgo.transform.position = tp + Vector3.up * 0.7f;
            var tcol = tgo.AddComponent<CapsuleCollider>();
            tcol.direction = 1;
            tcol.radius = 0.22f;
            tcol.height = 1.5f;
            tgo.AddComponent<PropTelescope>().lookDir = telDir;
            PropInfo.SetSpot("coin_telescope", tp);

            // flower boxes on the plinth walkway
            for (int i = 0; i < 4; i++)
            {
                float a = (67.5f + i * 90f) * Mathf.Deg2Rad;
                Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                if (Vector3.Dot(d, toHut) > 0.85f || Vector3.Dot(d, outDir) > 0.85f) continue;
                Vector3 fb = new Vector3(c.x, baseTop, c.z) + d * 3.2f;
                Quaternion fq = Quaternion.LookRotation(d);
                batch.B(PropMats.Shutter).Box(fb + Vector3.up * 0.15f, new Vector3(0.8f, 0.3f, 0.3f), fq);
                for (int k = 0; k < 3; k++)
                {
                    Vector3 bpp = fb + fq * new Vector3(-0.25f + k * 0.25f, 0.36f, 0f);
                    batch.B(PropMats.GrassDark).Ico(bpp, new Vector3(0.14f, 0.12f, 0.12f), Quaternion.Euler(0f, k * 40f, 0f), 0);
                    batch.B(k == 1 ? PropMats.FlowerYellow : PropMats.FlowerMagenta).Octa(bpp + new Vector3(0f, 0.12f, 0.03f), 0.05f);
                }
            }
        }

        /// Q7 on the 0.62 island: the lighthouse summit drops ~29 m to the north beach over a convex cliff. The rope
        /// ladder therefore hangs in two straight sections that stay clear of the rock: from a short timber boom at the
        /// rim down to a stand-off platform halfway (bolted to the face), and from there to the beach.
        struct RopePlan { public Vector3 rim, boom, plat, foot, dir; }

        static bool PlanRopeLadder(Vector3 c, out RopePlan plan)
        {
            plan = default;
            float best = float.MaxValue;
            float topY = PropGround.H(c.x, c.z);
            for (int ai = 0; ai <= 20; ai++)
            {
                float ang = (60f + ai * 3f) * Mathf.Deg2Rad;             // around due north (+z)
                Vector3 d = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                float H(float s) => PropGround.H(c.x + d.x * s, c.z + d.z * s);
                // rim: the last plateau point; beach: the first dry point at the foot of the cliff
                float sr = -1f;
                for (float s = 5f; s < 20f; s += 0.25f) { if (H(s) < topY - 0.8f) { sr = s - 0.25f; break; } }
                if (sr < 0f) continue;
                float sb = -1f;
                for (float s = sr + 1f; s < sr + 14f; s += 0.25f)
                {
                    float h = H(s);
                    if (h <= 1.6f && h >= 0.15f && H(s + 1.5f) > -0.2f) { sb = s + 0.4f; break; }
                    if (h < 0.15f) break;
                }
                if (sb < 0f) continue;
                float footY = H(sb);
                float boomOut = 1.2f, platOut = 1.1f;
                for (int tries = 0; tries < 12; tries++)
                {
                    float sB = sr + boomOut;
                    float yB = topY + 0.9f;
                    float yP = (yB + footY) * 0.5f - 0.5f;
                    float sFace = sr;
                    for (float s = sr; s < sb; s += 0.1f) { if (H(s) <= yP) { sFace = s; break; } }
                    float sP = sFace + platOut;
                    bool ok = Clear(H, sB, yB - 0.9f, sP, yP) && Clear(H, sP, yP, sb, footY);
                    if (ok)
                    {
                        float cost = (sb - sr) + Mathf.Abs(ang - Mathf.PI * 0.5f) * 3f + boomOut + platOut;
                        if (cost < best)
                        {
                            best = cost;
                            plan = new RopePlan
                            {
                                dir = d,
                                rim = new Vector3(c.x + d.x * sr, PropGround.H(c.x + d.x * sr, c.z + d.z * sr), c.z + d.z * sr),
                                boom = new Vector3(c.x + d.x * sB, yB, c.z + d.z * sB),
                                plat = new Vector3(c.x + d.x * sP, yP, c.z + d.z * sP),
                                foot = new Vector3(c.x + d.x * sb, footY, c.z + d.z * sb),
                            };
                        }
                        break;
                    }
                    if (tries % 2 == 0) platOut += 0.3f; else boomOut = Mathf.Min(2.6f, boomOut + 0.3f);
                }
            }
            return best < float.MaxValue;
        }

        /// The straight segment (s0, y0) → (s1, y1) stays ≥ 0.3 m above the terrain profile.
        static bool Clear(System.Func<float, float> H, float s0, float y0, float s1, float y1)
        {
            for (int i = 1; i < 20; i++)
            {
                float t = i / 20f;
                float s = Mathf.Lerp(s0, s1, t), y = Mathf.Lerp(y0, y1, t);
                if (H(s) > y - 0.3f) return false;
            }
            return true;
        }

        static void RopeSection(PropBatch batch, Vector3 top, Vector3 bottom, Vector3 side)
        {
            var rope = batch.B(PropMats.Rope);
            var rung = batch.B(PropMats.Wood2);
            for (int s = -1; s <= 1; s += 2) rope.Rod(top + side * (s * 0.24f), bottom + side * (s * 0.24f), 0.022f, 0.022f, 4);
            float len = (bottom - top).magnitude;
            Quaternion q = Quaternion.LookRotation(Vector3.Cross(side, Vector3.up));
            for (float t = 0.3f; t < len - 0.1f; t += 0.3f)
                rung.Box(Vector3.Lerp(top, bottom, t / len), new Vector3(0.56f, 0.04f, 0.06f), q * Quaternion.Euler(0f, 90f, 0f));
        }

        static void RopeLadder(Transform root, PropBatch batch, Transform cols, RopePlan p)
        {
            Vector3 d = p.dir;
            Vector3 side = Vector3.Cross(Vector3.up, d).normalized;
            Quaternion q = Quaternion.LookRotation(d);
            // the boom: two timbers from anchor posts behind the rim out over the edge, a crossbar the ropes hang from
            Vector3 anchor = p.rim - d * 1.4f;
            anchor.y = PropGround.H(anchor.x, anchor.z);
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 a = anchor + side * (s * 0.4f);
                batch.B(PropMats.WoodDark).Cylinder(a - Vector3.up * 0.5f, Quaternion.identity, 0.1f, 0.09f, 1.6f, 6, false, true);
                batch.B(PropMats.Rope).Torus(a + Vector3.up * 0.85f, Quaternion.identity, 0.12f, 0.03f, 8, 3);
                batch.B(PropMats.WoodDark).Beam(a + Vector3.up * 0.95f, p.boom + side * (s * 0.4f) + Vector3.up * 0.05f, 0.12f, 0.14f);
                PropCol.Capsule(cols, a + Vector3.up * 0.4f, 0.11f, 1.1f, Quaternion.identity, "BoomPost");
            }
            batch.B(PropMats.WoodDark).Beam(p.boom - side * 0.55f, p.boom + side * 0.55f, 0.1f, 0.12f);
            // upper section: boom → platform; lower section: platform → beach
            Vector3 platTop = p.plat + Vector3.up * 0.9f;
            RopeSection(batch, p.boom, p.plat + d * 0.05f, side);
            RopeSection(batch, platTop - d * 0.55f, p.foot, side);
            // the stand-off platform: planks, a rail on three sides, two struts back into the rock
            Vector3 pc = p.plat - d * 0.25f;
            batch.B(PropMats.Wood).Box(pc - Vector3.up * 0.04f, new Vector3(1.5f, 0.08f, 1.5f), q);
            for (int s = -1; s <= 1; s += 2)
            {
                batch.B(PropMats.WoodDark).Box(pc + q * new Vector3(s * 0.72f, 0.5f, 0f), new Vector3(0.06f, 0.06f, 1.5f), q);
                batch.B(PropMats.WoodDark).Beam(pc + q * new Vector3(s * 0.6f, -0.05f, 0.6f), pc + q * new Vector3(s * 0.6f, -1.6f, -1.4f), 0.07f, 0.07f);
                for (int k = -1; k <= 1; k += 2) batch.B(PropMats.WoodDark).Box(pc + q * new Vector3(s * 0.72f, 0.25f, k * 0.7f), new Vector3(0.06f, 0.6f, 0.06f), q);
            }
            batch.B(PropMats.WoodDark).Box(pc + q * new Vector3(0f, 0.5f, -0.72f), new Vector3(1.5f, 0.06f, 0.06f), q);
            PropCol.Box(cols, pc - Vector3.up * 0.06f, new Vector3(1.5f, 0.12f, 1.5f), q, "RopePlatform");
            for (int s = -1; s <= 1; s += 2) PropCol.Box(cols, pc + q * new Vector3(s * 0.72f, 0.5f, 0f), new Vector3(0.08f, 1.0f, 1.5f), q, "RopePlatformRail");
            PropCol.Box(cols, pc + q * new Vector3(0f, 0.5f, -0.72f), new Vector3(1.5f, 1.0f, 0.08f), q, "RopePlatformRail");
            PropInfo.AddLandSpot(pc + Vector3.up * 0.55f, LandSpotKind.Nest, 0.6f, "cliffs");
            // the two climbs (L.13 ladders; PropLadder = fade fallback). The climber faces the cliff (outward = d).
            var lower = PropLadder.Create(root, "RopeLadderLower", p.foot, p.plat.y + 0.9f, p.plat - d * 0.2f + Vector3.up * 0.04f, d, LadderInfo.Rope);
            lower.bottomStand = p.foot + d * 0.9f + Vector3.up * 0.05f;
            lower.proximityTop = true;
            lower.proximityBottom = true;
            Vector3 topStand = p.rim - d * 0.9f;
            topStand.y = PropGround.H(topStand.x, topStand.z) + 0.05f;
            var upper = PropLadder.Create(root, "RopeLadderUpper", p.plat + d * 0.05f, p.boom.y, topStand, d, LadderInfo.Rope);
            upper.bottomStand = p.plat - d * 0.35f + Vector3.up * 0.04f;
            upper.proximityTop = true;
            upper.proximityBottom = true;
            PropInfo.SetSpot("rope_ladder_top", p.rim);
            PropInfo.SetSpot("rope_ladder_platform", p.plat);
            PropInfo.SetSpot("rope_ladder_bottom", p.foot);
        }
    }
}
