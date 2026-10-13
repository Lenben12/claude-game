using UnityEngine;

namespace Sealebrities
{
    /// Round 3 (GDD §20 Q19/Q20): low-poly models of the new under-water content in the "How to Fish" look – treasure
    /// chests, four small wrecks to explore (sailboat, seaplane, trawler with an enterable wheelhouse, pirate ship) and
    /// the funny finds / meme props (throne, gaming PC, shopping cart, scuba gnome, rubber duck, rubber dino,
    /// boombox, disco ball, sunglasses, the "stonks" arrow). Every builder returns a root at the origin (ground contact
    /// at y = 0, front = +Z) in metres; `solid` adds local colliders (world placement) – icons build without them.
    /// Flat-shaded PropMeshBuilder meshes, one material per part, no shadows on small parts.
    public static class SeaModels
    {
        // ------------------------------------------------------------------ palette
        public static readonly Color WoodOld = Hex("#6B4A2E"), WoodWet = Hex("#4E3826"), WoodPale = Hex("#9C7A52"),
            Gold = Hex("#E8C65A"), GoldDeep = Hex("#B8902E"), Iron = Hex("#4A4F55"), RustC = Hex("#8A4A2A"),
            HullWhite = Hex("#D9DCD6"), HullBlue = Hex("#2F5F8A"), HullRed = Hex("#B3402E"), Algae = Hex("#5E7A3A"),
            Black = Hex("#1E1B2B"), Crimson = Hex("#B3261E"), Sand = Hex("#D8C49A");

        static Color Hex(string h) => LowPoly.Hex(h);
        static Material M(Color c) => LowPoly.Mat(c);

        static readonly PropMeshBuilder B = new PropMeshBuilder();

        /// Bakes the shared builder into a child mesh object and clears it.
        static GameObject Part(Transform parent, string name, Material m, bool shadows = false)
        {
            var mr = PropUtil.MeshObject(parent, name, B.ToMesh("SB_Sea_" + name), m, Vector3.zero, Quaternion.identity, shadows);
            B.Clear();
            return mr != null ? mr.gameObject : null;
        }

        static Transform Root(string name)
        {
            var go = new GameObject(name);
            return go.transform;
        }

        static void Col(Transform root, Vector3 c, Vector3 size, Quaternion rot, bool solid, string name = "Solid")
        {
            if (!solid) return;
            PropSolid.LocalBox(root, c, size, rot, name);
        }

        // ================================================================== treasure chest
        /// Chest: wooden box with gold bands, a hinged lid (child "LidPivot"), a pile of gold inside (child "Loot") and
        /// a glint on the lock (child "Glint"). 0.9 × 0.6 × 0.56 m.
        public static Transform Chest(bool solid, bool pirate)
        {
            var r = Root("SeaChest");
            Color wood = pirate ? WoodWet : WoodOld;
            B.Box(new Vector3(0f, 0.22f, 0f), new Vector3(0.9f, 0.44f, 0.56f));
            Part(r, "Base", M(wood), true);
            for (int s = -1; s <= 1; s += 2)
                B.Box(new Vector3(s * 0.3f, 0.225f, 0f), new Vector3(0.06f, 0.46f, 0.585f));
            B.Box(new Vector3(0f, 0.04f, 0f), new Vector3(0.93f, 0.06f, 0.59f));
            Part(r, "Bands", M(pirate ? Gold : GoldDeep));
            B.Box(new Vector3(0f, 0.34f, 0.29f), new Vector3(0.13f, 0.15f, 0.03f));
            Part(r, "Lock", M(Gold));
            // gold inside (seen when the lid opens; hidden once looted)
            var loot = new GameObject("Loot").transform;
            loot.SetParent(r, false);
            for (int i = 0; i < 9; i++)
            {
                float a = i * 2.4f;
                B.Cylinder(new Vector3(Mathf.Cos(a) * 0.25f * (i % 3) / 2f, 0.38f + (i % 4) * 0.012f, Mathf.Sin(a) * 0.14f * (i % 3) / 2f),
                    Quaternion.Euler(8f * (i % 3), 0f, 10f * ((i + 1) % 3)), 0.055f, 0.055f, 0.012f, 7, true, true);
            }
            B.Ico(new Vector3(0f, 0.44f, 0f), new Vector3(0.28f, 0.06f, 0.16f), Quaternion.identity, 0);
            Part(loot, "Coins", M(Gold));
            B.Ico(new Vector3(0.12f, 0.47f, 0.05f), Vector3.one * 0.045f, Quaternion.Euler(20f, 30f, 0f), 0);
            Part(loot, "Gem", PropMats.EmissiveInstance(Hex("#3FD4FF"), new Color(0.2f, 0.9f, 1.2f)));
            // lid on its hinge (rotate LidPivot around X to open)
            var pivot = new GameObject("LidPivot").transform;
            pivot.SetParent(r, false);
            pivot.localPosition = new Vector3(0f, 0.44f, -0.28f);
            B.Box(new Vector3(0f, 0.06f, 0.28f), new Vector3(0.92f, 0.12f, 0.58f));
            B.Box(new Vector3(0f, 0.15f, 0.28f), new Vector3(0.88f, 0.07f, 0.46f));
            Part(pivot, "Lid", M(wood), true);
            for (int s = -1; s <= 1; s += 2) B.Box(new Vector3(s * 0.3f, 0.1f, 0.28f), new Vector3(0.065f, 0.2f, 0.6f));
            Part(pivot, "LidBands", M(pirate ? Gold : GoldDeep));
            // a glint that says "treasure!" from a few metres away
            B.Octa(new Vector3(0f, 0.36f, 0.31f), new Vector3(0.05f, 0.05f, 0.02f));
            Part(r, "Glint", PropMats.EmissiveInstance(Gold, new Color(1.4f, 1.0f, 0.3f)));
            Col(r, new Vector3(0f, 0.3f, 0f), new Vector3(0.9f, 0.6f, 0.56f), Quaternion.identity, solid, "ChestSolid");
            return r;
        }

        // ================================================================== wrecks
        /// A hull "trough" (bottom, sloped sides, pointed bow, transom) – enterable from above. len along Z.
        static void Trough(Transform r, float len, float wid, float h, Color plank, Color keel, bool solid, string name)
        {
            float hl = len * 0.5f, hw = wid * 0.5f;
            float bowLen = wid * 0.9f;
            float mid = hl - bowLen;   // where the bow wedge starts
            // bottom
            B.Box(new Vector3(0f, 0.15f, -bowLen * 0.5f), new Vector3(wid * 0.82f, 0.3f, len - bowLen));
            // sides (lean out a little)
            for (int s = -1; s <= 1; s += 2)
                B.Box(new Vector3(s * hw * 0.92f, h * 0.5f, -bowLen * 0.5f), new Vector3(0.22f, h, len - bowLen), Quaternion.Euler(0f, 0f, -s * 8f));
            // bow wedge
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 a = new Vector3(s * hw * 0.92f, h * 0.5f, mid);
                Vector3 b = new Vector3(0f, h * 0.55f, hl);
                Vector3 d = b - a;
                B.Box((a + b) * 0.5f, new Vector3(0.22f, h * 1.05f, d.magnitude + 0.2f), Quaternion.LookRotation(d.normalized, Vector3.up));
            }
            // transom
            B.Box(new Vector3(0f, h * 0.5f, -hl + 0.12f), new Vector3(wid * 0.95f, h, 0.24f));
            Part(r, name, M(plank), true);
            // keel / rub rail
            B.Box(new Vector3(0f, 0.05f, 0f), new Vector3(0.3f, 0.12f, len * 0.96f));
            for (int s = -1; s <= 1; s += 2) B.Box(new Vector3(s * hw * 1.0f, h - 0.06f, -bowLen * 0.5f), new Vector3(0.16f, 0.14f, len - bowLen), Quaternion.Euler(0f, 0f, -s * 8f));
            Part(r, name + "Rail", M(keel));
            if (!solid) return;
            PropSolid.LocalBox(r, new Vector3(0f, 0.15f, -bowLen * 0.5f), new Vector3(wid * 0.82f, 0.3f, len - bowLen), Quaternion.identity, "HullBottom");
            for (int s = -1; s <= 1; s += 2)
                PropSolid.LocalBox(r, new Vector3(s * hw * 0.92f, h * 0.5f, -bowLen * 0.5f), new Vector3(0.24f, h, len - bowLen), Quaternion.Euler(0f, 0f, -s * 8f), "HullSide");
            PropSolid.LocalBox(r, new Vector3(0f, h * 0.5f, -hl + 0.12f), new Vector3(wid * 0.95f, h, 0.26f), Quaternion.identity, "HullStern");
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 a = new Vector3(s * hw * 0.92f, h * 0.5f, mid);
                Vector3 b = new Vector3(0f, h * 0.55f, hl);
                Vector3 d = b - a;
                PropSolid.LocalBox(r, (a + b) * 0.5f, new Vector3(0.24f, h * 1.05f, d.magnitude), Quaternion.LookRotation(d.normalized, Vector3.up), "HullBow");
            }
        }

        /// Sunken sailboat (8 m): white hull, broken mast, torn sail, a little cabin – the chest lies in the cockpit.
        public static Transform Sailboat(bool solid, out Vector3 chestLocal)
        {
            var r = Root("Wreck_Sailboat");
            Trough(r, 8f, 2.6f, 1.3f, HullWhite, HullBlue, solid, "Hull");
            // cabin (forward half), open hatch
            B.Box(new Vector3(0f, 1.6f, 0.9f), new Vector3(1.7f, 0.7f, 2.4f));
            Part(r, "Cabin", M(Hex("#C9CCC4")), true);
            B.Box(new Vector3(0f, 1.98f, 0.9f), new Vector3(1.8f, 0.08f, 2.5f));
            Part(r, "CabinRoof", M(HullBlue));
            // broken mast + boom + torn sail
            B.Rod(new Vector3(0f, 1.9f, 1.6f), new Vector3(0.5f, 6.2f, 1.2f), 0.09f, 0.07f, 6, true);
            B.Rod(new Vector3(0.15f, 2.4f, 1.4f), new Vector3(0.6f, 2.7f, -2.6f), 0.06f, 0.05f, 6, true);
            Part(r, "Mast", M(Iron), true);
            B.Tri2(new Vector3(0.35f, 2.6f, 1.3f), new Vector3(0.48f, 5.6f, 1.25f), new Vector3(0.62f, 2.8f, -2.2f), Vector3.right);
            Part(r, "Sail", M(Hex("#E8E2D0")));
            // algae + barnacle lumps
            for (int i = 0; i < 6; i++) B.Ico(new Vector3((i % 2 == 0 ? -1f : 1f) * 1.25f, 0.4f + (i % 3) * 0.35f, -3f + i * 1.1f), new Vector3(0.18f, 0.12f, 0.25f), Quaternion.Euler(0f, i * 40f, 0f), 0);
            Part(r, "Algae", M(Algae));
            Col(r, new Vector3(0f, 1.6f, 0.9f), new Vector3(1.7f, 0.8f, 2.4f), Quaternion.identity, solid, "CabinSolid");
            if (solid) PropSolid.LocalSegment(r, new Vector3(0f, 1.9f, 1.6f), new Vector3(0.5f, 6.2f, 1.2f), 0.1f, "MastSolid");
            chestLocal = new Vector3(0.1f, 0.3f, -2.2f);
            return r;
        }

        /// Crashed seaplane (11 m): fuselage, one wing, floats, tail, a bent propeller.
        public static Transform Seaplane(bool solid, out Vector3 chestLocal)
        {
            var r = Root("Wreck_Seaplane");
            var fus = new Vector2[]
            {
                new Vector2(0.05f, 0f), new Vector2(0.55f, 0.4f), new Vector2(0.85f, 1.6f), new Vector2(0.9f, 3.5f),
                new Vector2(0.75f, 5.5f), new Vector2(0.45f, 7.6f), new Vector2(0.18f, 9.2f), new Vector2(0.02f, 9.6f),
            };
            // lathe along +Z: rotate the lathe's up axis onto -Z (nose at +Z)
            B.Lathe(new Vector3(0f, 1.3f, 4.6f), Quaternion.Euler(-90f, 0f, 0f), fus, 8);
            Part(r, "Fuselage", M(Hex("#E2C24A")), true);
            B.Ico(new Vector3(0f, 1.95f, 3.2f), new Vector3(0.6f, 0.38f, 0.8f), Quaternion.identity, 1);
            Part(r, "Cockpit", M(Hex("#2A3E5A")));
            // wing (left one broke off and lies beside it)
            B.Box(new Vector3(0f, 2.35f, 2.4f), new Vector3(9.5f, 0.18f, 1.7f));
            Part(r, "Wing", M(Hex("#D9B23A")), true);
            B.Box(new Vector3(-3.6f, 0.15f, -2.5f), new Vector3(4.2f, 0.18f, 1.6f), Quaternion.Euler(0f, 35f, 6f));
            Part(r, "WingBroken", M(Hex("#C9A02E")));
            // floats
            for (int s = -1; s <= 1; s += 2)
            {
                B.Lathe(new Vector3(s * 1.6f, 0.35f, 4.3f), Quaternion.Euler(-90f, 0f, 0f),
                    new[] { new Vector2(0.02f, 0f), new Vector2(0.3f, 0.6f), new Vector2(0.32f, 3f), new Vector2(0.15f, 4.4f), new Vector2(0.02f, 4.6f) }, 6);
                B.Rod(new Vector3(s * 1.6f, 0.6f, 3f), new Vector3(s * 0.6f, 1.4f, 3f), 0.05f, 0.05f, 4);
            }
            Part(r, "Floats", M(HullWhite));
            // tail
            B.Box(new Vector3(0f, 2.2f, -4.6f), new Vector3(0.12f, 1.5f, 1.1f), Quaternion.Euler(-12f, 0f, 0f));
            B.Box(new Vector3(0f, 1.6f, -4.7f), new Vector3(3f, 0.1f, 0.9f));
            Part(r, "Tail", M(Hex("#D9B23A")));
            // propeller (bent blade)
            B.Box(new Vector3(0f, 1.3f, 5.05f), new Vector3(0.18f, 0.18f, 0.25f));
            B.Box(new Vector3(0f, 1.95f, 5.1f), new Vector3(0.16f, 1.2f, 0.05f), Quaternion.Euler(0f, 0f, 8f));
            B.Box(new Vector3(0.5f, 0.85f, 5.15f), new Vector3(1.1f, 0.16f, 0.05f), Quaternion.Euler(30f, 0f, -20f));
            Part(r, "Propeller", M(Iron));
            B.Box(new Vector3(0.92f, 1.2f, 1.2f), new Vector3(0.06f, 1.1f, 0.8f), Quaternion.Euler(0f, 25f, 0f));
            Part(r, "DoorOpen", M(Hex("#C9A02E")));
            if (solid)
            {
                PropSolid.LocalSegment(r, new Vector3(0f, 1.3f, -4.2f), new Vector3(0f, 1.3f, 4.6f), 0.85f, "FuselageSolid");
                PropSolid.LocalBox(r, new Vector3(0f, 2.35f, 2.4f), new Vector3(9.5f, 0.2f, 1.7f), Quaternion.identity, "WingSolid");
                for (int s = -1; s <= 1; s += 2) PropSolid.LocalSegment(r, new Vector3(s * 1.6f, 0.35f, 2.2f), new Vector3(s * 1.6f, 0.35f, 6.2f), 0.3f, "FloatSolid");
            }
            chestLocal = new Vector3(2.4f, 0f, 0.6f);   // beside the open door, on the sand
            return r;
        }

        /// Fishing trawler (13 m) with an ENTERABLE wheelhouse (door opening, window) – the "sunken room" with a chest.
        public static Transform Trawler(bool solid, out Vector3 chestLocal, out Vector3 roomLocal)
        {
            var r = Root("Wreck_Trawler");
            Trough(r, 13f, 4.2f, 2.2f, HullRed, Iron, solid, "Hull");
            // a broken deck (a few planks over the hold)
            for (int i = 0; i < 4; i++) B.Box(new Vector3(-0.6f + (i % 2) * 1.4f, 2.05f, 3.4f - i * 0.9f), new Vector3(1.1f, 0.1f, 0.7f), Quaternion.Euler(0f, 0f, (i % 2 == 0 ? 4f : -6f)));
            Part(r, "DeckPlanks", M(WoodWet), true);
            // wheelhouse at the stern: 2.8 × 2.3 × 2.6 inner room, door gap on the right, window at the front
            float zc = -3.6f, hx = 1.5f, hz = 1.4f, wy0 = 0.3f, wh = 2.4f;
            Color wall = Hex("#E0DDD4");
            B.Box(new Vector3(0f, wy0 + wh * 0.5f, zc - hz), new Vector3(hx * 2f, wh, 0.15f));                       // back
            B.Box(new Vector3(-hx, wy0 + wh * 0.5f, zc), new Vector3(0.15f, wh, hz * 2f));                           // left
            B.Box(new Vector3(hx, wy0 + wh * 0.5f, zc - hz * 0.6f), new Vector3(0.15f, wh, hz * 0.8f));               // right (door gap)
            B.Box(new Vector3(hx, wy0 + wh - 0.3f, zc + hz * 0.45f), new Vector3(0.15f, 0.6f, hz * 1.1f));             // over the door
            B.Box(new Vector3(0f, wy0 + 0.5f, zc + hz), new Vector3(hx * 2f, 1f, 0.15f));                              // front, below the window
            B.Box(new Vector3(0f, wy0 + wh - 0.25f, zc + hz), new Vector3(hx * 2f, 0.5f, 0.15f));                      // front, above
            B.Box(new Vector3(-hx + 0.3f, wy0 + 1.4f, zc + hz), new Vector3(0.6f, 0.9f, 0.15f));
            B.Box(new Vector3(hx - 0.3f, wy0 + 1.4f, zc + hz), new Vector3(0.6f, 0.9f, 0.15f));
            Part(r, "Wheelhouse", M(wall), true);
            B.Box(new Vector3(0f, wy0 + wh + 0.08f, zc), new Vector3(hx * 2f + 0.3f, 0.16f, hz * 2f + 0.3f));
            Part(r, "WheelhouseRoof", M(HullBlue), true);
            // inside: the wheel and a chart table
            B.Torus(new Vector3(0f, wy0 + 1.25f, zc + hz - 0.35f), Quaternion.Euler(90f, 0f, 0f), 0.32f, 0.03f, 10, 3);
            for (int k = 0; k < 4; k++)
            {
                float a = k * Mathf.PI * 0.25f;
                B.Beam(new Vector3(Mathf.Cos(a) * 0.42f, wy0 + 1.25f + Mathf.Sin(a) * 0.42f, zc + hz - 0.35f), new Vector3(-Mathf.Cos(a) * 0.42f, wy0 + 1.25f - Mathf.Sin(a) * 0.42f, zc + hz - 0.35f), 0.04f, 0.04f);
            }
            B.Rod(new Vector3(0f, wy0, zc + hz - 0.35f), new Vector3(0f, wy0 + 1.2f, zc + hz - 0.35f), 0.06f, 0.06f, 5);
            Part(r, "Wheel", M(WoodPale));
            B.Box(new Vector3(-hx + 0.55f, wy0 + 0.8f, zc - 0.3f), new Vector3(0.9f, 0.08f, 1.2f));
            B.Box(new Vector3(-hx + 0.55f, wy0 + 0.4f, zc - 0.3f), new Vector3(0.1f, 0.8f, 0.1f));
            Part(r, "ChartTable", M(WoodOld));
            // A-frame + net + buoys
            B.Rod(new Vector3(-1.6f, 2f, -6f), new Vector3(0f, 5f, -6.2f), 0.1f, 0.08f, 5);
            B.Rod(new Vector3(1.6f, 2f, -6f), new Vector3(0f, 5f, -6.2f), 0.1f, 0.08f, 5);
            Part(r, "AFrame", M(RustC), true);
            B.Ico(new Vector3(0.6f, 2.4f, 1.8f), new Vector3(1.4f, 0.4f, 1.1f), Quaternion.Euler(0f, 20f, 0f), 1);
            Part(r, "Net", M(Hex("#3E7A4A")));
            for (int i = 0; i < 4; i++) B.Ico(new Vector3(-1.2f + i * 0.5f, 2.3f + (i % 2) * 0.2f, 0.8f), Vector3.one * 0.2f, Quaternion.identity, 0);
            Part(r, "Buoys", M(Hex("#EE7F2D")));
            if (solid)
            {
                PropSolid.LocalBox(r, new Vector3(0f, wy0 + wh * 0.5f, zc - hz), new Vector3(hx * 2f, wh, 0.18f), Quaternion.identity, "WhBack");
                PropSolid.LocalBox(r, new Vector3(-hx, wy0 + wh * 0.5f, zc), new Vector3(0.18f, wh, hz * 2f), Quaternion.identity, "WhLeft");
                PropSolid.LocalBox(r, new Vector3(hx, wy0 + wh * 0.5f, zc - hz * 0.6f), new Vector3(0.18f, wh, hz * 0.8f), Quaternion.identity, "WhRight");
                PropSolid.LocalBox(r, new Vector3(0f, wy0 + 0.5f, zc + hz), new Vector3(hx * 2f, 1f, 0.18f), Quaternion.identity, "WhFront");
                PropSolid.LocalBox(r, new Vector3(0f, wy0 + wh + 0.08f, zc), new Vector3(hx * 2f + 0.3f, 0.18f, hz * 2f + 0.3f), Quaternion.identity, "WhRoof");
            }
            chestLocal = new Vector3(0.4f, wy0, zc - 0.5f);
            roomLocal = new Vector3(0f, wy0 + 1.2f, zc);
            return r;
        }

        /// Old pirate ship (15 m), lying on its side: dark planks, stern castle, fallen mast, cannons – a rich chest.
        public static Transform PirateWreck(bool solid, out Vector3 chestLocal)
        {
            var r = Root("Wreck_Pirates");
            var hull = new GameObject("Tilted").transform;
            hull.SetParent(r, false);
            hull.localRotation = Quaternion.Euler(0f, 0f, 22f);
            Trough(hull, 15f, 4.6f, 2.8f, WoodWet, Hex("#3A2A1E"), solid, "Hull");
            B.Box(new Vector3(0f, 3.4f, -6.1f), new Vector3(4.2f, 1.4f, 2.6f));
            Part(hull, "SternCastle", M(WoodOld), true);
            for (int i = 0; i < 3; i++) B.Box(new Vector3(-0.8f + i * 0.8f, 3.6f, -7.42f), new Vector3(0.45f, 0.5f, 0.06f));
            Part(hull, "SternWindows", PropMats.EmissiveInstance(Hex("#2A3040"), new Color(0.05f, 0.12f, 0.12f)));
            // cannons poking out of the side
            for (int i = 0; i < 4; i++)
                B.Rod(new Vector3(2.1f, 1.6f, -3f + i * 2.2f), new Vector3(3.1f, 1.7f, -3f + i * 2.2f), 0.16f, 0.12f, 6, true);
            Part(hull, "Cannons", M(Iron));
            if (solid) PropSolid.LocalBox(hull, new Vector3(0f, 3.4f, -6.1f), new Vector3(4.2f, 1.4f, 2.6f), Quaternion.identity, "CastleSolid");
            // fallen mast across the sand + a ragged black flag
            B.Rod(new Vector3(-0.5f, 0.35f, 2f), new Vector3(-9.5f, 0.6f, 6f), 0.22f, 0.16f, 6, true);
            Part(r, "Mast", M(WoodOld), true);
            B.Quad2(new Vector3(-7.5f, 0.75f, 5.1f), new Vector3(-6.2f, 0.7f, 4.5f), new Vector3(-6.5f, 0.05f, 3.6f), new Vector3(-7.8f, 0.1f, 4.2f), Vector3.up);
            Part(r, "Flag", M(Black));
            if (solid) PropSolid.LocalSegment(r, new Vector3(-0.5f, 0.35f, 2f), new Vector3(-9.5f, 0.6f, 6f), 0.22f, "MastSolid");
            // spilled treasure around the bow
            for (int i = 0; i < 14; i++)
            {
                float a = i * 1.9f;
                B.Cylinder(new Vector3(3.2f + Mathf.Cos(a) * 0.9f * (i % 4) / 3f, 0.03f + (i % 3) * 0.01f, 6.4f + Mathf.Sin(a) * 0.7f),
                    Quaternion.Euler(10f * (i % 3), 0f, 6f * (i % 2)), 0.06f, 0.06f, 0.014f, 7, true, true);
            }
            Part(r, "Coins", M(Gold));
            chestLocal = new Vector3(-0.6f, 0.3f, -2.5f);
            return r;
        }

        // ================================================================== meme props
        public static Transform Throne(bool solid)
        {
            var r = Root("Meme_Throne");
            B.Box(new Vector3(0f, 0.3f, 0f), new Vector3(1f, 0.6f, 0.9f));
            B.Box(new Vector3(0f, 1.05f, -0.4f), new Vector3(1f, 1.5f, 0.16f));
            for (int s = -1; s <= 1; s += 2)
            {
                B.Box(new Vector3(s * 0.48f, 0.78f, 0.05f), new Vector3(0.12f, 0.12f, 0.8f));
                B.Box(new Vector3(s * 0.48f, 0.66f, 0.38f), new Vector3(0.1f, 0.25f, 0.1f));
                B.Ico(new Vector3(s * 0.48f, 1.88f, -0.4f), Vector3.one * 0.11f, Quaternion.identity, 0);
            }
            B.Cylinder(new Vector3(0f, 1.75f, -0.4f), Quaternion.Euler(90f, 0f, 0f), 0.42f, 0.42f, 0.16f, 10);
            Part(r, "Frame", M(Gold), true);
            B.Box(new Vector3(0f, 0.65f, 0.04f), new Vector3(0.8f, 0.12f, 0.74f));
            B.Box(new Vector3(0f, 1.1f, -0.3f), new Vector3(0.74f, 1.0f, 0.06f));
            Part(r, "Cushion", M(Crimson));
            // the king's little crown sits on the hermit crab's shell (the crab itself is added by SeaMemeActor)
            Vector3 cc = new Vector3(0f, 0.83f, 0.094f);
            B.Cylinder(cc, Quaternion.identity, 0.055f, 0.06f, 0.045f, 8);
            for (int k = 0; k < 5; k++)
            {
                float a = k * Mathf.PI * 2f / 5f;
                B.Cylinder(cc + new Vector3(Mathf.Cos(a) * 0.052f, 0.04f, Mathf.Sin(a) * 0.052f), Quaternion.identity, 0.016f, 0f, 0.04f, 4);
            }
            Part(r, "Crown", M(Gold));
            for (int k = 0; k < 5; k++)
            {
                float a = k * Mathf.PI * 2f / 5f + 0.6f;
                B.Octa(cc + new Vector3(Mathf.Cos(a) * 0.06f, 0.022f, Mathf.Sin(a) * 0.06f), 0.01f);
            }
            B.Octa(new Vector3(0f, 1.75f, -0.31f), 0.07f);
            Part(r, "Gems", PropMats.EmissiveInstance(Hex("#D9455A"), new Color(0.6f, 0.05f, 0.1f)));
            Col(r, new Vector3(0f, 0.4f, 0f), new Vector3(1f, 0.8f, 0.9f), Quaternion.identity, solid);
            Col(r, new Vector3(0f, 1.1f, -0.4f), new Vector3(1f, 1.6f, 0.2f), Quaternion.identity, solid);
            return r;
        }

        /// The gaming PC: tower with RGB (child "Rgb" – animated), monitor (child "Screen"), keyboard, a gaming chair.
        public static Transform GamingPc(bool solid, out Material rgb, out Material screen)
        {
            var r = Root("Meme_GamingPc");
            rgb = PropMats.EmissiveInstance(Hex("#3FD4FF"), new Color(0.3f, 1.4f, 2f));
            screen = PropMats.EmissiveInstance(Hex("#1E2A5C"), new Color(0.25f, 0.45f, 1.4f));
            // tower (tilted in the sand)
            var tower = new GameObject("Tower").transform;
            tower.SetParent(r, false);
            tower.localPosition = new Vector3(-0.7f, 0f, 0f);
            tower.localRotation = Quaternion.Euler(0f, 20f, 7f);
            B.Box(new Vector3(0f, 0.45f, 0f), new Vector3(0.24f, 0.9f, 0.5f));
            Part(tower, "Case", M(Black), true);
            for (int k = 0; k < 3; k++) B.Torus(new Vector3(0.0f, 0.2f + k * 0.25f, 0.255f), Quaternion.Euler(90f, 0f, 0f), 0.08f, 0.012f, 10, 3);
            B.Box(new Vector3(0.123f, 0.45f, 0f), new Vector3(0.01f, 0.8f, 0.04f));
            B.Box(new Vector3(0.123f, 0.86f, 0f), new Vector3(0.01f, 0.03f, 0.44f));
            Part(tower, "Rgb", rgb);
            // monitor on a crate
            B.Box(new Vector3(0.3f, 0.25f, 0.1f), new Vector3(0.8f, 0.5f, 0.5f));
            Part(r, "Crate", M(WoodWet), true);
            B.Box(new Vector3(0.3f, 0.86f, 0.05f), new Vector3(0.95f, 0.56f, 0.05f), Quaternion.Euler(-8f, -10f, 0f));
            B.Box(new Vector3(0.3f, 0.56f, 0.12f), new Vector3(0.08f, 0.14f, 0.08f));
            Part(r, "Monitor", M(Black));
            B.Box(new Vector3(0.3f, 0.86f, 0.08f), new Vector3(0.86f, 0.48f, 0.01f), Quaternion.Euler(-8f, -10f, 0f));
            Part(r, "Screen", screen);
            B.Box(new Vector3(0.3f, 0.52f, 0.33f), new Vector3(0.7f, 0.03f, 0.2f), Quaternion.Euler(0f, -6f, 0f));
            Part(r, "Keyboard", M(Hex("#2B2D3A")));
            // gaming chair, fallen over
            var chair = new GameObject("Chair").transform;
            chair.SetParent(r, false);
            chair.localPosition = new Vector3(0.6f, 0f, 1.2f);
            chair.localRotation = Quaternion.Euler(0f, -30f, 75f);
            B.Box(new Vector3(0f, 0.5f, 0f), new Vector3(0.55f, 0.12f, 0.55f));
            B.Box(new Vector3(0f, 0.95f, -0.25f), new Vector3(0.55f, 0.85f, 0.12f));
            Part(chair, "Seat", M(Black));
            B.Box(new Vector3(0f, 0.95f, -0.18f), new Vector3(0.12f, 0.86f, 0.02f));
            Part(chair, "Stripe", M(Crimson));
            B.Rod(new Vector3(0f, 0.45f, 0f), new Vector3(0f, 0.05f, 0f), 0.04f, 0.04f, 5);
            Part(chair, "Post", M(Iron));
            Col(r, new Vector3(0.3f, 0.25f, 0.1f), new Vector3(0.8f, 0.5f, 0.5f), Quaternion.identity, solid);
            if (solid) PropSolid.LocalBox(tower, new Vector3(0f, 0.45f, 0f), new Vector3(0.24f, 0.9f, 0.5f), Quaternion.identity, "TowerSolid");
            return r;
        }

        /// Shopping cart, tipped over a little; the fish model goes inside (MemeActor adds it).
        public static Transform ShoppingCart(bool solid)
        {
            var r = Root("Meme_Cart");
            var body = new GameObject("Cart").transform;
            body.SetParent(r, false);
            body.localRotation = Quaternion.Euler(0f, 0f, 12f);
            float w = 0.55f, l = 0.85f, y0 = 0.35f, h = 0.5f;
            // wire basket: bars on all sides
            for (int i = 0; i <= 6; i++)
            {
                float z = -l * 0.5f + l * i / 6f;
                B.Beam(new Vector3(-w * 0.5f, y0, z), new Vector3(-w * 0.5f, y0 + h, z), 0.015f, 0.015f);
                B.Beam(new Vector3(w * 0.5f, y0, z), new Vector3(w * 0.5f, y0 + h, z), 0.015f, 0.015f);
            }
            for (int i = 0; i <= 4; i++)
            {
                float x = -w * 0.5f + w * i / 4f;
                B.Beam(new Vector3(x, y0, -l * 0.5f), new Vector3(x, y0 + h, -l * 0.5f), 0.015f, 0.015f);
                B.Beam(new Vector3(x, y0, l * 0.5f), new Vector3(x, y0 + h * 0.8f, l * 0.5f), 0.015f, 0.015f);
                B.Beam(new Vector3(x, y0, -l * 0.5f), new Vector3(x, y0, l * 0.5f), 0.015f, 0.015f);
            }
            for (int k = 0; k < 3; k++)
            {
                float yy = y0 + h * k / 2f;
                B.Beam(new Vector3(-w * 0.5f, yy, -l * 0.5f), new Vector3(-w * 0.5f, yy, l * 0.5f), 0.02f, 0.02f);
                B.Beam(new Vector3(w * 0.5f, yy, -l * 0.5f), new Vector3(w * 0.5f, yy, l * 0.5f), 0.02f, 0.02f);
                B.Beam(new Vector3(-w * 0.5f, yy, -l * 0.5f), new Vector3(w * 0.5f, yy, -l * 0.5f), 0.02f, 0.02f);
            }
            // chassis + legs
            for (int s = -1; s <= 1; s += 2)
            {
                B.Beam(new Vector3(s * w * 0.4f, 0.1f, -l * 0.5f), new Vector3(s * w * 0.4f, 0.1f, l * 0.5f), 0.025f, 0.025f);
                B.Beam(new Vector3(s * w * 0.4f, 0.1f, -l * 0.5f), new Vector3(s * w * 0.45f, y0, -l * 0.5f), 0.025f, 0.025f);
                B.Beam(new Vector3(s * w * 0.4f, 0.1f, l * 0.45f), new Vector3(s * w * 0.45f, y0, l * 0.45f), 0.025f, 0.025f);
            }
            Part(body, "Wire", M(Hex("#B8C4CC")));
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    B.Cylinder(new Vector3(sx * w * 0.4f - sx * 0.02f, 0.06f, sz * l * 0.42f), Quaternion.Euler(0f, 0f, 90f), 0.06f, 0.06f, 0.04f, 8, true, true);
            Part(body, "Wheels", M(Black));
            B.Beam(new Vector3(-w * 0.55f, y0 + h + 0.12f, -l * 0.55f), new Vector3(w * 0.55f, y0 + h + 0.12f, -l * 0.55f), 0.05f, 0.05f);
            B.Box(new Vector3(0f, y0 + h + 0.04f, -l * 0.53f), new Vector3(w, 0.1f, 0.04f));
            Part(body, "Handle", M(Crimson));
            Col(r, new Vector3(0f, 0.45f, 0f), new Vector3(0.6f, 0.9f, 0.9f), Quaternion.Euler(0f, 0f, 12f), solid);
            return r;
        }

        public static Transform Gnome(bool solid)
        {
            var r = Root("Meme_Gnome");
            B.Box(new Vector3(-0.06f, 0.03f, 0.02f), new Vector3(0.09f, 0.06f, 0.14f));
            B.Box(new Vector3(0.06f, 0.03f, 0.02f), new Vector3(0.09f, 0.06f, 0.14f));
            Part(r, "Boots", M(Hex("#5A3A22")));
            B.Cylinder(new Vector3(0f, 0.05f, 0f), Quaternion.identity, 0.12f, 0.1f, 0.2f, 8, true, true);
            Part(r, "Body", M(Hex("#2F6FD6")));
            B.Cylinder(new Vector3(0f, 0.2f, 0f), Quaternion.identity, 0.105f, 0.105f, 0.03f, 8);
            Part(r, "Belt", M(Black));
            B.Ico(new Vector3(0f, 0.31f, 0.01f), new Vector3(0.085f, 0.085f, 0.08f), Quaternion.identity, 1);
            Part(r, "Face", M(Hex("#F2B8A0")));
            B.Cylinder(new Vector3(0f, 0.35f, 0f), Quaternion.Euler(-12f, 0f, 0f), 0.095f, 0f, 0.28f, 8, true);
            Part(r, "Hat", M(Crimson));
            B.Cylinder(new Vector3(0f, 0.29f, 0.05f), Quaternion.Euler(170f, 0f, 0f), 0.08f, 0f, 0.17f, 7, true);
            Part(r, "Beard", M(Hex("#F4EFE6")));
            B.Box(new Vector3(0f, 0.33f, 0.08f), new Vector3(0.14f, 0.05f, 0.03f));
            Part(r, "Mask", M(Hex("#1E3A5C")));
            B.Rod(new Vector3(0.07f, 0.32f, 0.06f), new Vector3(0.09f, 0.52f, 0.02f), 0.012f, 0.012f, 5, true);
            Part(r, "Snorkel", M(Hex("#F2D23C")));
            B.Rod(new Vector3(-0.1f, 0.15f, 0.02f), new Vector3(-0.17f, 0.24f, 0.06f), 0.025f, 0.022f, 5, true);
            B.Ico(new Vector3(-0.18f, 0.25f, 0.07f), Vector3.one * 0.03f, Quaternion.identity, 0);
            Part(r, "Arm", M(Hex("#2F6FD6")));
            Col(r, new Vector3(0f, 0.25f, 0f), new Vector3(0.26f, 0.5f, 0.26f), Quaternion.identity, solid);
            return r;
        }

        /// One rubber duck (0.22 m): body, head, beak, eyes.
        public static Transform Duck()
        {
            var r = Root("Duck");
            B.Ico(new Vector3(0f, 0.06f, 0f), new Vector3(0.09f, 0.065f, 0.12f), Quaternion.identity, 1);
            B.Ico(new Vector3(0f, 0.15f, 0.06f), Vector3.one * 0.055f, Quaternion.identity, 1);
            B.Cylinder(new Vector3(0f, 0.1f, -0.1f), Quaternion.Euler(-60f, 0f, 0f), 0.04f, 0f, 0.06f, 5);
            Part(r, "Body", M(Hex("#F2D23C")));
            B.Box(new Vector3(0f, 0.14f, 0.12f), new Vector3(0.05f, 0.016f, 0.05f));
            Part(r, "Beak", M(Hex("#F28C1E")));
            B.Octa(new Vector3(0.035f, 0.17f, 0.1f), 0.011f);
            B.Octa(new Vector3(-0.035f, 0.17f, 0.1f), 0.011f);
            Part(r, "Eyes", M(Black));
            return r;
        }

        public static Transform RubberDino(bool solid)
        {
            var r = Root("Meme_Dino");
            Material green = M(Hex("#4FB84A")), belly = M(Hex("#F2D23C"));
            B.Ico(new Vector3(0f, 0.3f, 0f), new Vector3(0.14f, 0.16f, 0.22f), Quaternion.Euler(-20f, 0f, 0f), 1);
            B.Ico(new Vector3(0f, 0.52f, 0.17f), new Vector3(0.08f, 0.08f, 0.11f), Quaternion.Euler(10f, 0f, 0f), 1);
            B.Cylinder(new Vector3(0f, 0.25f, -0.15f), Quaternion.Euler(-110f, 0f, 0f), 0.1f, 0.01f, 0.36f, 7);
            for (int s = -1; s <= 1; s += 2)
            {
                B.Cylinder(new Vector3(s * 0.08f, 0f, 0.02f), Quaternion.identity, 0.05f, 0.045f, 0.2f, 6, true);
                B.Rod(new Vector3(s * 0.1f, 0.38f, 0.14f), new Vector3(s * 0.11f, 0.33f, 0.21f), 0.015f, 0.012f, 4, true);
            }
            Part(r, "Body", green);
            B.Ico(new Vector3(0f, 0.28f, 0.08f), new Vector3(0.1f, 0.12f, 0.1f), Quaternion.Euler(-20f, 0f, 0f), 0);
            Part(r, "Belly", belly);
            B.Box(new Vector3(0f, 0.49f, 0.25f), new Vector3(0.1f, 0.012f, 0.04f));
            B.Octa(new Vector3(0.05f, 0.56f, 0.22f), 0.014f);
            B.Octa(new Vector3(-0.05f, 0.56f, 0.22f), 0.014f);
            Part(r, "Face", M(Black));
            Col(r, new Vector3(0f, 0.3f, 0f), new Vector3(0.3f, 0.6f, 0.6f), Quaternion.identity, solid);
            return r;
        }

        /// Boombox for the crab rave; returns the EQ material (animated by the actor).
        public static Transform Boombox(out Material eq)
        {
            var r = Root("Boombox");
            eq = PropMats.EmissiveInstance(Hex("#3FD46A"), new Color(0.3f, 1.6f, 0.5f));
            B.Box(new Vector3(0f, 0.15f, 0f), new Vector3(0.5f, 0.28f, 0.15f));
            Part(r, "Box", M(Hex("#2B2D3A")));
            for (int s = -1; s <= 1; s += 2)
            {
                B.Torus(new Vector3(s * 0.15f, 0.14f, 0.078f), Quaternion.Euler(90f, 0f, 0f), 0.07f, 0.012f, 10, 3);
                B.Cylinder(new Vector3(s * 0.15f, 0.14f, 0.07f), Quaternion.Euler(90f, 0f, 0f), 0.065f, 0.02f, 0.025f, 8);
            }
            B.Beam(new Vector3(-0.18f, 0.29f, 0f), new Vector3(-0.18f, 0.36f, 0f), 0.02f, 0.02f);
            B.Beam(new Vector3(0.18f, 0.29f, 0f), new Vector3(0.18f, 0.36f, 0f), 0.02f, 0.02f);
            B.Beam(new Vector3(-0.18f, 0.36f, 0f), new Vector3(0.18f, 0.36f, 0f), 0.025f, 0.025f);
            Part(r, "Speakers", M(Hex("#8A8F99")));
            for (int k = 0; k < 4; k++) B.Box(new Vector3(-0.03f + k * 0.02f, 0.21f + (k % 2) * 0.01f, 0.078f), new Vector3(0.012f, 0.04f + (k % 3) * 0.015f, 0.005f));
            Part(r, "Eq", eq);
            return r;
        }

        /// Disco ball (mirror facets, emissive) – the actor spins it.
        public static Transform DiscoBall()
        {
            var r = Root("DiscoBall");
            B.Ico(Vector3.zero, Vector3.one * 0.16f, Quaternion.identity, 1);
            Part(r, "Ball", PropMats.EmissiveInstance(Hex("#E8ECEF"), new Color(0.9f, 0.9f, 1.1f)));
            B.Rod(new Vector3(0f, 0.15f, 0f), new Vector3(0f, 0.9f, 0f), 0.006f, 0.006f, 3);
            Part(r, "String", M(Hex("#B8C4CC")));
            return r;
        }

        /// "Deal with it" sunglasses for a fish head: two black lens plates + bridge (in fish model space, 1 m fish).
        public static void Sunglasses(Transform parent, float eyeZ, float eyeY, float halfW)
        {
            for (int s = -1; s <= 1; s += 2)
                B.Box(new Vector3(s * (halfW + 0.006f), eyeY, eyeZ), new Vector3(0.012f, 0.05f, 0.075f));
            B.Box(new Vector3(0f, eyeY + 0.022f, eyeZ + 0.005f), new Vector3(halfW * 2f + 0.02f, 0.012f, 0.012f));
            for (int s = -1; s <= 1; s += 2)
                B.Box(new Vector3(s * (halfW + 0.006f), eyeY + 0.015f, eyeZ - 0.06f), new Vector3(0.008f, 0.008f, 0.09f));
            Part(parent, "Sunglasses", M(Black));
            for (int s = -1; s <= 1; s += 2)
                B.Box(new Vector3(s * (halfW + 0.013f), eyeY + 0.012f, eyeZ + 0.02f), new Vector3(0.002f, 0.01f, 0.02f));
            Part(parent, "SunglassesGlint", M(Hex("#F4EFE6")));
        }

        /// The green "stonks" arrow (1.4 m, pointing up-right, emissive).
        public static Transform StonksArrow()
        {
            var r = Root("StonksArrow");
            B.Box(new Vector3(0f, 0f, 0f), new Vector3(0.12f, 0.12f, 1f));
            B.Cylinder(new Vector3(0f, 0f, 0.5f), Quaternion.Euler(90f, 0f, 0f), 0.22f, 0f, 0.4f, 4, true);
            Part(r, "Arrow", PropMats.EmissiveInstance(Hex("#3FD46A"), new Color(0.3f, 1.7f, 0.5f)));
            return r;
        }
    }
}
