using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// H.1 – the walk/swim-in interior of the MS Clickbait (ship-local frame of PropWreck: x = beam, starboard +,
    /// y = up from the keel, z = length, bow +). Midship between z −17 and +12.5:
    ///  * entrance A: the torn 3 × 2.5 m hole in the port hull (z −7.8 … −4.8), entrance B: the open hatch on deck,
    ///  * a 2.2 m corridor (tween deck, floor y 3.4) with the galley (port, Gus' radio, floating pots, the mannequin)
    ///    and the captain's cabin (starboard, locked – key from the moray nest; the pirate hat secret, page 19),
    ///  * the dark cargo hold under the hatch (floor y 1.4): moray nest with the key, 400 cheese crates, two treasures,
    ///    the heavy two-player door to the side room (treasure 3) and the 1.2 m vent as the solo detour,
    ///  * white guideline ropes from both entrances, three portholes, a door that slams in the current (+ a rolling
    ///    cheese wheel) and the mannequin whose mask drops.
    /// The interior geometry is batched separately and culled beyond 60 m.
    public static class PropWreckInterior
    {
        const float TweenY = 3.4f, HoldY = 1.4f, DeckBottom = 6.85f, DeckTop = 7.1f;
        const float Aft = -17f, HoldAft = -3.3f, HoldFwd = 8.3f, Fwd = 12.5f;
        const float HatchX = 3.35f, HatchZ0 = -2.85f, HatchZ1 = 4.85f;
        const float HoleZ0 = -7.8f, HoleZ1 = -4.8f, HoleY0 = 3.7f, HoleY1 = 6.2f;
        const float HB = 5.5f, WL = 3.5f;

        static Matrix4x4 S;
        static Quaternion R;
        static PropBatch shell, inner;
        static PropMeshBuilder col;

        static Vector3 W(Vector3 local) => S.MultiplyPoint3x4(local);
        static Vector3 W(float x, float y, float z) => S.MultiplyPoint3x4(new Vector3(x, y, z));

        static PropMeshBuilder SB(Material m) { var b = shell.B(m); b.M = S; return b; }
        static PropMeshBuilder IB(Material m) { var b = inner.B(m); b.M = S; return b; }

        static Material Bulkhead => PropMats.Flat(LowPoly.Hex("#5A5662"));
        static Material BulkheadDark => PropMats.Flat(LowPoly.Hex("#45414E"));
        static Material Floor => PropMats.Flat(LowPoly.Hex("#5E4A40"));

        /// Solid box (visual + collider), ship-local, min/max corners.
        static void Block(Material m, Vector3 min, Vector3 max, bool collide = true)
        {
            Vector3 c = (min + max) * 0.5f, s = max - min;
            IB(m).Box(c, s, Quaternion.identity);
            if (collide) col.Box(c, s, Quaternion.identity);
        }

        /// Hull-plate frame around entrance A (replaces the port upper-band panel of strip 5) + jagged shards.
        public static void HullFrame(PropMeshBuilder up, PropMeshBuilder colB, float z0, float z1, System.Random rng)
        {
            void Panel(float za, float zb, float ya, float yb)
            {
                Vector3 a = new Vector3(-HB, ya, za), b = new Vector3(-HB, ya, zb), c = new Vector3(-HB, yb, zb), d = new Vector3(-HB, yb, za);
                up.QuadAuto(a, b, c, d, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector3.left);
                colB.QuadAuto(a, b, c, d, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector3.left);
            }
            Panel(z0, HoleZ0, WL, 7f);
            Panel(HoleZ1, z1, WL, 7f);
            Panel(HoleZ0, HoleZ1, WL, HoleY0);
            Panel(HoleZ0, HoleZ1, HoleY1, 7f);
            for (int i = 0; i < 7; i++)
            {
                float t = i / 6f;
                Vector3 e = i < 4 ? new Vector3(-HB - 0.05f, i % 2 == 0 ? HoleY0 : HoleY1, Mathf.Lerp(HoleZ0, HoleZ1, t * 1.4f)) : new Vector3(-HB - 0.05f, Mathf.Lerp(HoleY0, HoleY1, (i - 4) / 2f), i % 2 == 0 ? HoleZ0 : HoleZ1);
                Quaternion q = Quaternion.Euler(PropUtil.Range(rng, -40f, 40f), PropUtil.Range(rng, -30f, 30f), PropUtil.Range(rng, 20f, 60f));
                up.Box(e, new Vector3(0.05f, PropUtil.Range(rng, 0.25f, 0.45f), PropUtil.Range(rng, 0.25f, 0.5f)), q);
            }
        }

        public static void Build(PropBatch shellBatch, PropMeshBuilder colB, Matrix4x4 shipM, Quaternion shipRot, Transform decor, Transform cols, System.Random rng, float deckZ0, float deckZ1)
        {
            S = shipM;
            R = shipRot;
            shell = shellBatch;
            col = colB;
            inner = new PropBatch(0f);

            // ---------------------------------------------------------------- deck around the open hatch (strips 5–7)
            void DeckBox(float x0, float x1, float z0, float z1)
            {
                Vector3 c = new Vector3((x0 + x1) * 0.5f, (DeckBottom + DeckTop) * 0.5f, (z0 + z1) * 0.5f), s = new Vector3(x1 - x0, DeckTop - DeckBottom, z1 - z0);
                SB(PropMats.Deck).Box(c, s, Quaternion.identity);
                colB.Box(c, s, Quaternion.identity);
            }
            DeckBox(-HB, HB, deckZ0, HatchZ0);
            DeckBox(-HB, HB, HatchZ1, deckZ1);
            DeckBox(-HB, -HatchX, HatchZ0, HatchZ1);
            DeckBox(HatchX, HB, HatchZ0, HatchZ1);
            // the ceiling under the old (one-sided) deck over the aft rooms and the side room
            Block(BulkheadDark, new Vector3(-HB + 0.1f, DeckBottom - 0.1f, Aft), new Vector3(HB - 0.1f, DeckBottom + 0.12f, deckZ0), false);
            Block(BulkheadDark, new Vector3(-HB + 0.1f, DeckBottom - 0.1f, deckZ1), new Vector3(HB - 0.1f, DeckBottom + 0.12f, Fwd), false);

            // ---------------------------------------------------------------- inner hull lining (the hull is one-sided)
            for (int s = -1; s <= 1; s += 2)
            {
                float x = s * (HB - 0.12f);
                // tween deck sides (aft): vertical, with the hole on the port side
                if (s < 0)
                {
                    Block(Bulkhead, new Vector3(x - 0.06f, TweenY, Aft), new Vector3(x + 0.06f, DeckBottom, HoleZ0));
                    Block(Bulkhead, new Vector3(x - 0.06f, TweenY, HoleZ1), new Vector3(x + 0.06f, DeckBottom, HoldAft));
                    Block(Bulkhead, new Vector3(x - 0.06f, TweenY, HoleZ0), new Vector3(x + 0.06f, HoleY0, HoleZ1));
                    Block(Bulkhead, new Vector3(x - 0.06f, HoleY1, HoleZ0), new Vector3(x + 0.06f, DeckBottom, HoleZ1));
                }
                else Block(Bulkhead, new Vector3(x - 0.06f, TweenY, Aft), new Vector3(x + 0.06f, DeckBottom, HoldAft));
                // hold sides: vertical upper part + slanted lower part (down to the floor)
                Block(BulkheadDark, new Vector3(x - 0.06f, WL, HoldAft), new Vector3(x + 0.06f, DeckBottom, Fwd));
                Vector3 a = new Vector3(s * (HB - 0.15f), WL, HoldAft), b = new Vector3(s * 4.1f, HoldY, HoldAft);
                Vector3 a1 = new Vector3(a.x, a.y, Fwd), b1 = new Vector3(b.x, b.y, Fwd);
                IB(BulkheadDark).Quad2(a, a1, b1, b, Vector3.up);
                col.Quad2(a, a1, b1, b, Vector3.up);
            }
            // floors
            Block(Floor, new Vector3(-HB + 0.1f, TweenY - 0.15f, Aft), new Vector3(HB - 0.1f, TweenY, HoldAft));
            Block(PropMats.Rust, new Vector3(-4.2f, HoldY - 0.2f, HoldAft), new Vector3(4.2f, HoldY, Fwd));
            // bulkheads: aft end, the step down into the hold, forward end
            Block(Bulkhead, new Vector3(-HB, TweenY, Aft - 0.15f), new Vector3(HB, DeckBottom, Aft));
            Block(BulkheadDark, new Vector3(-4.3f, HoldY, HoldAft - 0.12f), new Vector3(4.3f, TweenY - 0.15f, HoldAft));
            Block(Bulkhead, new Vector3(-HB, TweenY, HoldAft - 0.12f), new Vector3(-1.1f, DeckBottom, HoldAft));
            Block(Bulkhead, new Vector3(1.1f, TweenY, HoldAft - 0.12f), new Vector3(HB, DeckBottom, HoldAft));
            Block(BulkheadDark, new Vector3(-HB, HoldY, Fwd), new Vector3(HB, DeckBottom, Fwd + 0.15f));

            // ---------------------------------------------------------------- corridor walls (x = ±1.1)
            const float t = 0.08f;
            // port wall: galley door z −15.0 … −13.8, passage z −8.0 … −4.6
            Block(Bulkhead, new Vector3(-1.1f - t, TweenY, Aft), new Vector3(-1.1f + t, DeckBottom, -15.0f));
            Block(Bulkhead, new Vector3(-1.1f - t, TweenY, -13.8f), new Vector3(-1.1f + t, DeckBottom, -8.0f));
            Block(Bulkhead, new Vector3(-1.1f - t, TweenY, -4.6f), new Vector3(-1.1f + t, DeckBottom, HoldAft));
            Block(Bulkhead, new Vector3(-1.1f - t, TweenY + 2.2f, -15.0f), new Vector3(-1.1f + t, DeckBottom, -13.8f));
            // starboard wall: cabin door z −13.0 … −11.9, store door z −8.4 … −7.4
            Block(Bulkhead, new Vector3(1.1f - t, TweenY, Aft), new Vector3(1.1f + t, DeckBottom, -13.0f));
            Block(Bulkhead, new Vector3(1.1f - t, TweenY, -11.9f), new Vector3(1.1f + t, DeckBottom, -8.4f));
            Block(Bulkhead, new Vector3(1.1f - t, TweenY, -7.4f), new Vector3(1.1f + t, DeckBottom, HoldAft));
            Block(Bulkhead, new Vector3(1.1f - t, TweenY + 2.2f, -13.0f), new Vector3(1.1f + t, DeckBottom, -11.9f));
            Block(Bulkhead, new Vector3(1.1f - t, TweenY + 2.2f, -8.4f), new Vector3(1.1f + t, DeckBottom, -7.4f));
            // room walls across the beam
            Block(Bulkhead, new Vector3(-HB, TweenY, -11.3f - t), new Vector3(-1.1f, DeckBottom, -11.3f + t));   // galley front
            Block(Bulkhead, new Vector3(-HB, TweenY, -8.0f - t), new Vector3(-1.1f, DeckBottom, -8.0f + t));     // passage aft wall
            Block(Bulkhead, new Vector3(-HB, TweenY, -4.6f - t), new Vector3(-1.1f, DeckBottom, -4.6f + t));     // passage fwd wall
            Block(Bulkhead, new Vector3(1.1f, TweenY, -11.3f - t), new Vector3(HB, DeckBottom, -11.3f + t));     // cabin front
            Block(Bulkhead, new Vector3(1.1f, TweenY, -6.0f - t), new Vector3(HB, DeckBottom, -6.0f + t));       // store fwd wall
            // closed storage between galley and passage (port) – filled block
            Block(BulkheadDark, new Vector3(-HB + 0.1f, TweenY, -11.2f), new Vector3(-1.2f, DeckBottom - 0.05f, -8.1f), true);

            // ---------------------------------------------------------------- the hold: two-player door + vent
            // forward hold bulkhead at z = HoldFwd: door x −0.9 … 0.9, y HoldY … 3.6; vent x 3.0 … 4.2, y 5.2 … 6.4
            Block(BulkheadDark, new Vector3(-HB, HoldY, HoldFwd - 0.12f), new Vector3(-0.9f, DeckBottom, HoldFwd));
            Block(BulkheadDark, new Vector3(0.9f, HoldY, HoldFwd - 0.12f), new Vector3(3.0f, DeckBottom, HoldFwd));
            Block(BulkheadDark, new Vector3(4.2f, HoldY, HoldFwd - 0.12f), new Vector3(HB, DeckBottom, HoldFwd));
            Block(BulkheadDark, new Vector3(3.0f, HoldY, HoldFwd - 0.12f), new Vector3(4.2f, 5.2f, HoldFwd));
            Block(BulkheadDark, new Vector3(3.0f, 6.4f, HoldFwd - 0.12f), new Vector3(4.2f, DeckBottom, HoldFwd));
            Block(BulkheadDark, new Vector3(-0.9f, 3.6f, HoldFwd - 0.12f), new Vector3(0.9f, DeckBottom, HoldFwd));
            // the vent duct (1.2 m square, 1.4 m long, a grate hanging loose)
            IB(PropMats.Metal).Box(new Vector3(2.94f, 5.8f, HoldFwd - 0.7f), new Vector3(0.06f, 1.3f, 1.4f), Quaternion.identity);
            IB(PropMats.Metal).Box(new Vector3(4.26f, 5.8f, HoldFwd - 0.7f), new Vector3(0.06f, 1.3f, 1.4f), Quaternion.identity);
            IB(PropMats.Metal).Box(new Vector3(3.6f, 5.14f, HoldFwd - 0.7f), new Vector3(1.3f, 0.06f, 1.4f), Quaternion.identity);
            IB(PropMats.Metal).Box(new Vector3(3.6f, 6.46f, HoldFwd - 0.7f), new Vector3(1.3f, 0.06f, 1.4f), Quaternion.identity);
            for (int i = 0; i < 4; i++) IB(PropMats.DarkMetal).Box(new Vector3(3.15f + i * 0.3f, 4.75f, HoldFwd - 1.6f), new Vector3(0.04f, 0.9f, 0.04f), Quaternion.Euler(30f, 0f, 0f));
            PropInfo.SetSpot("wreck_vent", W(3.6f, 5.8f, HoldFwd - 1.5f));
            // the heavy door: a slab that slides up when two divers push together
            var doorGo = new GameObject("WreckCoopDoor");
            doorGo.transform.SetParent(decor, false);
            doorGo.transform.SetPositionAndRotation(W(0f, HoldY, HoldFwd - 0.06f), R);
            var leaf = new GameObject("Leaf").transform;
            leaf.SetParent(doorGo.transform, false);
            var lm = new PropMeshBuilder();
            lm.Box(new Vector3(0f, 1.1f, 0f), new Vector3(1.8f, 2.2f, 0.14f));
            for (int i = 0; i < 3; i++) lm.Box(new Vector3(0f, 0.4f + i * 0.7f, -0.09f), new Vector3(1.7f, 0.1f, 0.05f));
            lm.Box(new Vector3(0.55f, 1.0f, -0.12f), new Vector3(0.12f, 0.35f, 0.08f));
            PropUtil.MeshObject(leaf, "DoorLeaf", lm.ToMesh("WreckDoorLeaf"), PropMats.Rust, Vector3.zero, Quaternion.identity);
            var block = leaf.gameObject.AddComponent<BoxCollider>();
            block.center = new Vector3(0f, 1.1f, 0f);
            block.size = new Vector3(1.8f, 2.2f, 0.16f);
            var trig = new GameObject("PushZone");
            trig.transform.SetParent(doorGo.transform, false);
            trig.transform.localPosition = new Vector3(0f, 1.1f, -0.6f);
            var tsc = trig.AddComponent<BoxCollider>();
            tsc.isTrigger = true;
            tsc.size = new Vector3(2.2f, 2.2f, 1.0f);
            var coop = trig.AddComponent<PropCoopAction>();
            coop.spotId = "wreck_door_two_player";
            coop.flag = "coop_wreck_door";
            coop.soloDe = "Zu schwer – der Lüftungsschacht?";
            coop.soloEn = "Too heavy – the vent?";
            coop.moving = leaf;
            coop.openOffset = new Vector3(0f, 2.1f, 0f);
            coop.blocker = block;
            PropInfo.SetSpot("wreck_door_two_player", W(0f, HoldY + 0.1f, HoldFwd - 1.2f));

            // ---------------------------------------------------------------- the captain's cabin door (locked)
            var cdoor = new GameObject("CabinDoor");
            cdoor.transform.SetParent(decor, false);
            cdoor.transform.SetPositionAndRotation(W(1.1f, TweenY, -13.0f), R);
            var cleaf = new GameObject("Leaf").transform;
            cleaf.SetParent(cdoor.transform, false);
            var cm = new PropMeshBuilder();
            cm.Box(new Vector3(0f, 1.08f, 0.55f), new Vector3(0.06f, 2.16f, 1.08f));
            cm.Box(new Vector3(-0.05f, 1.05f, 0.9f), new Vector3(0.06f, 0.08f, 0.12f));
            PropUtil.MeshObject(cleaf, "CabinLeaf", cm.ToMesh("WreckCabinLeaf"), PropMats.WoodDark, Vector3.zero, Quaternion.identity);
            var cblock = cleaf.gameObject.AddComponent<BoxCollider>();
            cblock.center = new Vector3(0f, 1.08f, 0.55f);
            cblock.size = new Vector3(0.1f, 2.16f, 1.1f);
            var kd = cdoor.AddComponent<PropKeyDoor>();
            kd.leaf = cleaf;
            kd.blocker = cblock;
            var csc = cdoor.AddComponent<BoxCollider>();
            csc.isTrigger = true;
            csc.center = new Vector3(-0.5f, 1.1f, 0.55f);
            csc.size = new Vector3(1.0f, 2.2f, 1.2f);

            // ---------------------------------------------------------------- the store door that slams in the current
            var sdoor = new GameObject("SlamDoor");
            sdoor.transform.SetParent(decor, false);
            sdoor.transform.SetPositionAndRotation(W(1.1f, TweenY, -8.4f), R);
            var sleaf = new GameObject("Leaf").transform;
            sleaf.SetParent(sdoor.transform, false);
            var sm = new PropMeshBuilder();
            sm.Box(new Vector3(0f, 1.08f, 0.5f), new Vector3(0.06f, 2.16f, 1.0f));
            PropUtil.MeshObject(sleaf, "StoreLeaf", sm.ToMesh("WreckStoreLeaf"), PropMats.Rust, Vector3.zero, Quaternion.identity);
            // round 3 (GDD §19): the store door leaf is solid and swings with its kinematic body (open, it leaves 1.2 m of
            // the corridor free – the interior passage stays passable; when it slams, the player is pushed out)
            var sblock = sleaf.gameObject.AddComponent<BoxCollider>();
            sblock.center = new Vector3(0f, 1.08f, 0.5f);
            sblock.size = new Vector3(0.08f, 2.16f, 1.0f);
            PropSolid.Kinematic(sleaf.gameObject);
            PropSolid.Count++;
            var slam = sdoor.AddComponent<PropSlamDoor>();
            slam.leaf = sleaf;
            var wheel = new GameObject("CheeseWheel").transform;
            wheel.SetParent(decor, false);
            wheel.SetPositionAndRotation(W(3.2f, TweenY + 0.25f, -7.0f), R * Quaternion.Euler(0f, 0f, 90f));
            var wm = new PropMeshBuilder();
            wm.Cylinder(new Vector3(0f, -0.1f, 0f), Quaternion.identity, 0.25f, 0.25f, 0.2f, 10, true, true);
            PropUtil.MeshObject(wheel, "Wheel", wm.ToMesh("CheeseWheel"), PropMats.Flat(PropPal.Sun), Vector3.zero, Quaternion.identity);
            slam.cheese = wheel;
            slam.rollTo = W(-0.3f, TweenY + 0.25f, -7.6f);

            // ---------------------------------------------------------------- galley (port aft room)
            Galley(decor, rng);
            // ---------------------------------------------------------------- captain's cabin (starboard aft room)
            Cabin(decor, rng);
            // ---------------------------------------------------------------- the hold
            Hold(decor, rng);

            // ---------------------------------------------------------------- portholes (god-ray slivers)
            var glass = PropMats.EmissiveInstance(new Color(0.55f, 0.8f, 0.9f), new Color(0.25f, 0.5f, 0.65f));
            Vector3[] ph = { new Vector3(-HB + 0.2f, 5.4f, -14.2f), new Vector3(HB - 0.2f, 5.4f, -14.2f), new Vector3(-HB + 0.2f, 5.6f, 1.5f) };
            for (int i = 0; i < ph.Length; i++)
            {
                float s = Mathf.Sign(ph[i].x);
                Quaternion q = Quaternion.Euler(0f, 0f, 90f);
                IB(glass).Cylinder(ph[i], q, 0.22f, 0.22f, 0.04f, 10, true, true);
                IB(PropMats.Metal).Torus(ph[i], q, 0.25f, 0.035f, 10, 3);
                SB(PropMats.HoleDark).Cylinder(new Vector3(s * (HB + 0.02f), ph[i].y, ph[i].z), q, 0.22f, 0.22f, 0.03f, 10, true, true);
                // a soft light sliver falling in
                var beam = PropMats.Flat(new Color(0.42f, 0.62f, 0.7f));
                Vector3 p0 = ph[i] - new Vector3(s * 0.05f, 0f, 0f), p1 = ph[i] + new Vector3(-s * 2.2f, -2.1f, 0.3f);
                IB(beam).Tri2(p0 + Vector3.forward * 0.18f, p0 - Vector3.forward * 0.18f, p1, Vector3.up);
            }

            // ---------------------------------------------------------------- guideline ropes (GDD 14.6)
            Guide(new[] { new Vector3(-HB - 1.2f, 3.9f, -6.3f), new Vector3(-3.2f, TweenY + 0.4f, -6.3f), new Vector3(0f, TweenY + 0.4f, -6.3f), new Vector3(0f, TweenY + 0.4f, -3.6f), new Vector3(0f, HoldY + 0.5f, -1.8f), new Vector3(0f, HoldY + 0.5f, 3f), new Vector3(0f, HoldY + 0.5f, HoldFwd - 1.4f) });
            Guide(new[] { new Vector3(HatchX - 0.3f, DeckTop + 0.9f, 0.5f), new Vector3(HatchX - 0.4f, DeckTop - 0.2f, 0.5f), new Vector3(HatchX - 0.6f, HoldY + 0.5f, 0.5f), new Vector3(0f, HoldY + 0.5f, 1.5f) });
            Guide(new[] { new Vector3(0f, TweenY + 0.4f, -6.3f), new Vector3(0f, TweenY + 0.4f, -14.4f), new Vector3(-2.6f, TweenY + 0.4f, -14.4f) });

            // the interior volume (H.1: "Tier im Wrack" challenge, music, fauna)
            PropInfo.SetWreckInterior(S * Matrix4x4.TRS(new Vector3(0f, 4.15f, (Aft + Fwd) * 0.5f), Quaternion.identity, Vector3.one), new Vector3(HB, 2.85f, (Fwd - Aft) * 0.5f));
            PropInfo.SetSpot("wreck_entrance_a", W(-HB - 1.5f, 4.6f, (HoleZ0 + HoleZ1) * 0.5f));
            PropInfo.SetSpot("wreck_entrance_b", W(0f, DeckTop + 1.5f, 1f));
            PropInfo.SetSpot("wreck_hold", W(0f, HoldY + 0.3f, 2f));

            PropReef.QuietShadows(inner);
            inner.Flush(decor, "WreckInterior", ShadowCastingMode.Off, 60f);
            inner = null;
            shell = null;
            col = null;
        }

        static void Guide(Vector3[] pts)
        {
            for (int i = 1; i < pts.Length; i++)
            {
                IB(PropMats.White).Rod(pts[i - 1], pts[i], 0.014f, 0.014f, 3);
                IB(PropMats.DarkMetal).Box(pts[i] - Vector3.up * 0.15f, new Vector3(0.05f, 0.3f, 0.05f), Quaternion.identity);
            }
        }

        // ------------------------------------------------------------------ rooms
        static void Galley(Transform decor, System.Random rng)
        {
            // stove + counter along the hull side, a table, shelves, pots drifting under the ceiling
            Block(PropMats.Metal, new Vector3(-5.2f, TweenY, -16.8f), new Vector3(-4.3f, TweenY + 0.95f, -14.6f));
            IB(PropMats.DarkMetal).Box(new Vector3(-4.75f, TweenY + 0.97f, -15.7f), new Vector3(0.8f, 0.04f, 2.0f), Quaternion.identity);
            Block(PropMats.Wood2, new Vector3(-5.2f, TweenY, -14.4f), new Vector3(-4.5f, TweenY + 0.9f, -11.6f));
            Block(PropMats.Wood, new Vector3(-3.4f, TweenY + 0.72f, -15.2f), new Vector3(-2.0f, TweenY + 0.8f, -13.4f));
            for (int i = 0; i < 4; i++)
                IB(PropMats.WoodDark).Box(new Vector3(i < 2 ? -3.3f : -2.1f, TweenY + 0.36f, i % 2 == 0 ? -15.1f : -13.5f), new Vector3(0.06f, 0.72f, 0.06f), Quaternion.identity);
            Block(PropMats.WoodDark, new Vector3(-2.2f, TweenY + 1.6f, -16.95f), new Vector3(-1.3f, TweenY + 1.65f, -16.6f), false);
            for (int i = 0; i < 6; i++)
            {
                Vector3 p = new Vector3(PropUtil.Range(rng, -4.6f, -1.6f), PropUtil.Range(rng, 5.2f, 6.4f), PropUtil.Range(rng, -16.4f, -11.8f));
                Quaternion q = Quaternion.Euler(PropUtil.Range(rng, 0f, 360f), PropUtil.Range(rng, 0f, 360f), 0f);
                IB(i % 2 == 0 ? PropMats.Metal : PropMats.RedMetal).Cylinder(p, q, 0.16f, 0.14f, 0.2f, 8, false, true);
            }
            // Gus' old radio on the counter (M2) – the npc module spawns the interactive one at m2_radio
            Vector3 radio = new Vector3(-4.85f, TweenY + 0.9f, -12.6f);
            IB(PropMats.Flat(PropPal.HullBlue)).Box(radio + Vector3.up * 0.15f, new Vector3(0.35f, 0.3f, 0.5f), Quaternion.identity);
            PropInfo.SetSpot("gus_radio", W(radio + Vector3.up * 0.32f));
            PropSpots.Register("m2_radio", W(radio + Vector3.up * 0.32f));
            // page 20 (the key clue) lies on the galley table
            PropInfo.SetSpot("page_galley", W(-2.7f, TweenY + 0.83f, -14.3f));
            // the mannequin in the dark corner (aft-inboard)
            var man = new GameObject("Mannequin");
            man.transform.SetParent(decor, false);
            man.transform.SetPositionAndRotation(W(-1.6f, TweenY, -16.5f), R * Quaternion.Euler(0f, 160f, 0f));
            PropMannequin.Build(man.transform);
        }

        static void Cabin(Transform decor, System.Random rng)
        {
            // bunk, chart table with a cheese wheel, a locker
            Block(PropMats.WoodDark, new Vector3(3.3f, TweenY, -16.9f), new Vector3(5.3f, TweenY + 0.55f, -15.1f));
            IB(PropMats.Blanket).Box(new Vector3(4.3f, TweenY + 0.6f, -16f), new Vector3(1.9f, 0.1f, 1.7f), Quaternion.identity);
            Block(PropMats.Wood2, new Vector3(1.4f, TweenY + 0.85f, -16.6f), new Vector3(2.9f, TweenY + 0.92f, -15.4f));
            for (int i = 0; i < 4; i++) IB(PropMats.WoodDark).Box(new Vector3(i < 2 ? 1.5f : 2.8f, TweenY + 0.43f, i % 2 == 0 ? -16.5f : -15.5f), new Vector3(0.06f, 0.86f, 0.06f), Quaternion.identity);
            IB(PropMats.Paper).Box(new Vector3(2.15f, TweenY + 0.93f, -16f), new Vector3(1.1f, 0.01f, 0.8f), Quaternion.Euler(0f, 8f, 0f));
            IB(PropMats.Flat(PropPal.Sun)).Cylinder(new Vector3(2.5f, TweenY + 0.93f, -15.8f), Quaternion.identity, 0.24f, 0.24f, 0.16f, 10, true, true);
            Block(PropMats.Rust, new Vector3(4.6f, TweenY, -12.6f), new Vector3(5.3f, TweenY + 1.9f, -11.5f));
            // the pirate tricorn hat (secret „wreck_cabin“) on the bunk, page 19 on the chart table
            PropInfo.SetSpot("wreck_cabin_hat", W(4.4f, TweenY + 0.68f, -15.8f));
            PropInfo.SetSpot("page_cabin", W(1.8f, TweenY + 0.95f, -15.9f));
        }

        static void Hold(Transform decor, System.Random rng)
        {
            // 400 cheese crates (a big stack)
            var crate = PropMats.Wood3;
            var cheese = PropMats.Flat(PropPal.Sun);
            for (int ix = 0; ix < 3; ix++)
                for (int iz = 0; iz < 3; iz++)
                    for (int iy = 0; iy < 4 - (ix + iz) / 2; iy++)
                    {
                        Vector3 c = new Vector3(1.6f + ix * 0.85f, HoldY + 0.4f + iy * 0.8f, 2.2f + iz * 0.85f);
                        Quaternion q = Quaternion.Euler(0f, PropUtil.Range(rng, -6f, 6f), 0f);
                        IB(crate).Box(c, new Vector3(0.8f, 0.78f, 0.8f), q);
                        IB(cheese).Cylinder(c + q * new Vector3(0f, -0.15f, -0.405f), q * Quaternion.Euler(90f, 0f, 0f), 0.18f, 0.18f, 0.01f, 8, true, false);
                    }
            col.Box(new Vector3(2.45f, HoldY + 1.2f, 3.05f), new Vector3(2.5f, 2.4f, 2.5f), Quaternion.identity);
            PropInfo.SetSpot("hold_cheese", W(1.6f, HoldY + 0.8f * 4f + 0.05f, 2.2f));
            // gold: in an open crate at the port side
            Block(PropMats.WoodDark, new Vector3(-3.6f, HoldY, 5.6f), new Vector3(-2.6f, HoldY + 0.5f, 6.4f));
            PropInfo.SetSpot("hold_gold", W(-3.1f, HoldY + 0.55f, 6.0f));
            // the moray nest: a pile of pipes and rocks in the aft-port corner with two dark holes
            Vector3 nest = new Vector3(-3.0f, HoldY, -2.2f);
            for (int i = 0; i < 5; i++)
            {
                Vector3 p = nest + new Vector3(PropUtil.Range(rng, -0.6f, 0.6f), 0.3f + i * 0.15f, PropUtil.Range(rng, -0.5f, 0.5f));
                Quaternion q = Quaternion.Euler(0f, PropUtil.Range(rng, 0f, 180f), 90f);
                Vector3 axis = q * Vector3.up;
                p -= axis * 0.8f;
                IB(PropMats.Rust).Cylinder(p, q, 0.22f, 0.22f, 1.6f, 6, false, false);
                // dark pipe mouths at both ends (moray homes)
                IB(PropMats.HoleDark).Disk(p + axis * 0.02f, 0.2f, 6, q, false);
                IB(PropMats.HoleDark).Disk(p + axis * 1.58f, 0.2f, 6, q, true);
            }
            PropRocks.Build(IB(PropMats.RockDark), nest + new Vector3(0.6f, 0.4f, 0.3f), new Vector3(1.1f, 0.8f, 0.9f), Quaternion.identity, 4242, 0);
            col.Box(nest + new Vector3(0f, 0.5f, 0f), new Vector3(1.6f, 1f, 1.2f), Quaternion.identity);
            PropInfo.WreckHoles.Add(W(nest + new Vector3(-0.4f, 0.6f, 0.62f)));
            PropInfo.WreckHoleNormals.Add(R * Vector3.forward);
            PropInfo.WreckHoles.Add(W(nest + new Vector3(0.5f, 0.45f, 0.62f)));
            PropInfo.WreckHoleNormals.Add(R * Vector3.forward);
            var key = new GameObject("MorayKey");
            key.transform.SetParent(decor, false);
            key.transform.position = W(nest + new Vector3(0f, 0.95f, 0.65f));
            var km = new GameObject("Key");
            km.transform.SetParent(key.transform, false);
            var kb = new PropMeshBuilder();
            kb.Torus(Vector3.zero, Quaternion.Euler(90f, 0f, 0f), 0.05f, 0.015f, 8, 3);
            kb.Box(new Vector3(0.11f, 0f, 0f), new Vector3(0.14f, 0.02f, 0.02f));
            kb.Box(new Vector3(0.16f, -0.025f, 0f), new Vector3(0.02f, 0.05f, 0.02f));
            PropUtil.MeshObject(km.transform, "KeyMesh", kb.ToMesh("CabinKey"), PropMats.Gold, Vector3.zero, R, false);
            var ksc = key.AddComponent<SphereCollider>();
            ksc.isTrigger = true;
            ksc.radius = 0.6f;
            key.AddComponent<PropMorayKey>().model = km;
            PropInfo.SetSpot("moray_nest", W(nest + Vector3.up * 0.6f));
            // the side room: a small safe (treasure 3 „hold_safe“) and a chair
            Block(PropMats.DarkMetal, new Vector3(-0.5f, HoldY, Fwd - 1.2f), new Vector3(0.5f, HoldY + 0.9f, Fwd - 0.4f));
            PropInfo.SetSpot("hold_safe", W(0f, HoldY + 0.95f, Fwd - 0.8f));
            // a hermit crab wearing a tea cup (gag) on the hold floor
            IB(PropMats.White).Cylinder(new Vector3(-1.2f, HoldY + 0.02f, 4.2f), Quaternion.identity, 0.07f, 0.05f, 0.08f, 8, false, true);
            IB(PropMats.Crab).Box(new Vector3(-1.2f, HoldY + 0.02f, 4.32f), new Vector3(0.08f, 0.04f, 0.06f), Quaternion.identity);
        }
    }

    /// The galley mannequin (H.1 scare + gag): an old diving suit in the dark corner. When a diver comes within 2.5 m
    /// its mask drops and shows a painted smiley – with a squeaky honk. Resets after 5 minutes.
    public class PropMannequin : MonoBehaviour
    {
        public Transform mask;
        float dropped = -1f, next;
        Vector3 mask0;
        Quaternion maskRot0;

        public static void Build(Transform root)
        {
            var b = new PropMeshBuilder();
            b.Cylinder(new Vector3(0f, 0f, 0f), Quaternion.identity, 0.24f, 0.28f, 1.1f, 8, false, true);
            b.Ico(new Vector3(0f, 1.3f, 0f), new Vector3(0.24f, 0.26f, 0.24f), Quaternion.identity, 1);
            for (int s = -1; s <= 1; s += 2) b.Rod(new Vector3(s * 0.26f, 1.0f, 0f), new Vector3(s * 0.32f, 0.45f, 0.1f), 0.07f, 0.06f, 5, true);
            PropUtil.MeshObject(root, "Suit", b.ToMesh("MannequinSuit"), PropMats.Flat(LowPoly.Hex("#3B3A4E")), Vector3.zero, Quaternion.identity);
            var face = new PropMeshBuilder();
            face.Disk(new Vector3(0f, 1.32f, 0.255f), 0.15f, 10, Quaternion.Euler(-90f, 0f, 0f), true);
            PropUtil.MeshObject(root, "Smiley", face.ToMesh("MannequinFace"), PropMats.Flat(PropPal.Sun), Vector3.zero, Quaternion.identity);
            var dots = new PropMeshBuilder();
            dots.Box(new Vector3(-0.05f, 1.36f, 0.262f), new Vector3(0.03f, 0.04f, 0.01f));
            dots.Box(new Vector3(0.05f, 1.36f, 0.262f), new Vector3(0.03f, 0.04f, 0.01f));
            dots.Box(new Vector3(0f, 1.27f, 0.262f), new Vector3(0.12f, 0.02f, 0.01f));
            PropUtil.MeshObject(root, "SmileyInk", dots.ToMesh("MannequinInk"), PropMats.Ink, Vector3.zero, Quaternion.identity);
            var mask = new GameObject("Mask").transform;
            mask.SetParent(root, false);
            mask.localPosition = new Vector3(0f, 1.33f, 0.27f);
            var mb = new PropMeshBuilder();
            mb.Cylinder(Vector3.zero, Quaternion.Euler(90f, 0f, 0f), 0.17f, 0.17f, 0.06f, 10, true, true);
            PropUtil.MeshObject(mask, "MaskRim", mb.ToMesh("MannequinMask"), PropMats.Ink, Vector3.zero, Quaternion.identity);
            var gl = new PropMeshBuilder();
            gl.Disk(new Vector3(0f, 0f, 0.065f), 0.14f, 10, Quaternion.Euler(-90f, 0f, 0f), true);
            PropUtil.MeshObject(mask, "MaskGlass", gl.ToMesh("MannequinGlass"), PropMats.Flat(LowPoly.Hex("#2E5F93")), Vector3.zero, Quaternion.identity);
            var cap = root.gameObject.AddComponent<CapsuleCollider>();
            cap.center = new Vector3(0f, 0.75f, 0f);
            cap.radius = 0.3f;
            cap.height = 1.6f;
            root.gameObject.AddComponent<PropMannequin>().mask = mask;
        }

        void Start()
        {
            if (mask != null) { mask0 = mask.localPosition; maskRot0 = mask.localRotation; }
        }

        void Update()
        {
            if (mask == null) return;
            if (dropped >= 0f)
            {
                dropped += Time.deltaTime;
                float t = Mathf.Clamp01(dropped / 0.6f);
                mask.localPosition = mask0 + new Vector3(0.05f, -1.15f * t * t, 0.15f * t);
                mask.localRotation = maskRot0 * Quaternion.Euler(70f * t, 0f, 25f * t);
                if (dropped > 300f) { dropped = -1f; mask.localPosition = mask0; mask.localRotation = maskRot0; }
                return;
            }
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.25f;
            var p = Refs.Player;
            if (p == null || p.Root == null) return;
            if ((p.Root.position - transform.position).sqrMagnitude > 2.5f * 2.5f) return;
            dropped = 0f;
            PropSound.PlayAt(PropSound.Honk, transform.position + Vector3.up * 1.3f, 0.8f, 15f);
            FXEffects.Bubbles(mask.position, 6);
        }
    }

    /// A store door in the wreck corridor that slams shut in the current when a diver passes (no damage) – then a
    /// cheese wheel rolls out (the gag). Re-arms after 3 minutes.
    public class PropSlamDoor : MonoBehaviour
    {
        public Transform leaf, cheese;
        public Vector3 rollTo;
        float t = -1f, armedAt, next;
        Vector3 cheese0;
        Quaternion cheeseRot0;

        void Start()
        {
            if (leaf != null) leaf.localRotation = Quaternion.Euler(0f, -80f, 0f);
            if (cheese != null) { cheese0 = cheese.position; cheeseRot0 = cheese.rotation; }
        }

        void Update()
        {
            if (t >= 0f)
            {
                t += Time.deltaTime;
                if (leaf != null)
                {
                    float close = t < 0.25f ? 1f - t / 0.25f : (t < 3f ? 0f : Mathf.Clamp01((t - 3f) / 2f));
                    leaf.localRotation = Quaternion.Euler(0f, -80f * close, 0f);
                }
                if (cheese != null && t > 3.2f)
                {
                    float r = Mathf.Clamp01((t - 3.2f) / 2.2f);
                    cheese.position = Vector3.Lerp(cheese0, rollTo, r);
                    cheese.rotation = cheeseRot0 * Quaternion.Euler(0f, r * 540f, 0f);
                }
                if (t > 180f) { t = -1f; if (cheese != null) { cheese.position = cheese0; cheese.rotation = cheeseRot0; } }
                return;
            }
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.25f;
            var p = Refs.Player;
            if (p == null || p.Root == null || !p.IsUnderwater) return;
            if ((p.Root.position - transform.position).sqrMagnitude > 3.5f * 3.5f) return;
            t = 0f;
            armedAt = Time.time;
            PropSound.PlayAt(PropSound.Slam, transform.position + Vector3.up, 0.9f, 25f);
            FXEffects.Bubbles(transform.position + Vector3.up * 1.2f, 12);
        }
    }
}
