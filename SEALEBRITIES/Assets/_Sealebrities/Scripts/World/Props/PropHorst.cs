using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Round 3 (player answer): Horst no longer hangs around on the dock (he was in the way) – "Horst's corner" is on the
    /// beach south of the hut, well off the hut → dock boardwalk: a hammock between two palms (his home spot, also a
    /// registered sleep spot), his bench with the badly painted „HAFENMEISTER“ sign (built by PropDock.Extras at
    /// BenchSpot), a cooler, a thermos on a driftwood side table and a fishing rod leaning on a palm.
    /// NPC anchors (QuestSpots): "horst_hammock" (lying, centre of the cloth), "horst_home" (standing beside it),
    /// "horst_bench" (PropDock), "horst_corner" (the place itself). Deterministic.
    public static class PropHorst
    {
        /// Centre of the hammock (ground level), ~26 m south of the dock boardwalk on the dry beach.
        public static readonly Vector3 Corner = new Vector3(93f, 0f, -22f);
        /// Horst's bench: 3.5 m inland of the hammock, facing the sea (east).
        public static readonly Vector3 BenchSpot = new Vector3(89.5f, 0f, -18.5f);
        public const float BenchYaw = 100f;

        static float G(float x, float z) => PropGround.H(x, z);

        public static IEnumerator Build(Transform island, PropExclusion excl)
        {
            var root = PropUtil.WorldRoot("HorstCorner", island);
            var cols = new GameObject("Colliders").transform;
            cols.SetParent(root, false);
            var treeCols = new GameObject("PalmColliders");
            treeCols.transform.SetParent(root, false);
            var batch = new PropBatch(0f);
            var full = new PropBatch(64f);
            var lite = new PropBatch(64f);
            var rng = new System.Random(4242);

            Vector3 c = Corner;
            c.y = G(c.x, c.z);
            Vector3 along = Vector3.forward;              // the hammock hangs parallel to the shore (north–south)
            Vector3 sea = Vector3.right;
            // two palms leaning a little seaward and outward
            Vector3 pa = c - along * 2.6f, pb = c + along * 2.6f;
            pa.y = G(pa.x, pa.z); pb.y = G(pb.x, pb.z);
            PropJungle.AddPalm(full, lite, treeCols, rng, pa, sea - along * 0.35f);
            PropJungle.AddPalm(full, lite, treeCols, rng, pb, sea + along * 0.35f);
            excl.Add(c.x, c.z, 4.5f);
            excl.Add(BenchSpot.x, BenchSpot.z, 2.5f);
            // the hammock (hooks on the trunks at 1.45 m), registered as a sleep spot (players may nap there too)
            Vector3 ha = pa + Vector3.up * 1.45f + along * 0.28f, hb = pb + Vector3.up * 1.45f - along * 0.28f;
            PropKit.Hammock(batch, cols, ha, hb, 0, false);
            PropKit.HammockBed(root, "horst_hammock", ha, hb);
            // rope wraps on the trunks
            for (int s = 0; s < 2; s++)
            {
                Vector3 hk = s == 0 ? ha - along * 0.2f : hb + along * 0.2f;
                batch.W(PropMats.Rope, hk).Torus(hk, Quaternion.identity, 0.2f, 0.025f, 8, 3);
            }
            float sag = Mathf.Min(0.7f, (hb - ha).magnitude * 0.22f);
            Vector3 lie = (ha + hb) * 0.5f - Vector3.up * sag;
            Vector3 home = c - sea * 1.3f;
            home.y = G(home.x, home.z);
            PropSpots.Register("horst_hammock", lie, 0f);
            PropSpots.Register("horst_home", home, 90f);
            PropSpots.Register("horst_corner", c, 90f);

            // a cooler (beer + bait), a driftwood side table with his thermos and a tin cup, a fishing rod on the palm
            Vector3 co = c - sea * 1.6f + along * 1.6f;
            co.y = G(co.x, co.z);
            PropKit.Cooler(batch, cols, co, 15f, PropMats.Flat(LowPoly.Hex("#3FA9B5")));
            Vector3 tb = c - sea * 1.2f - along * 1.3f;
            tb.y = G(tb.x, tb.z);
            var tw = batch.W(PropMats.Driftwood, tb);
            tw.Box(tb + Vector3.up * 0.5f, new Vector3(0.55f, 0.06f, 0.45f), Quaternion.Euler(0f, 12f, 0f));
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    tw.Box(tb + Quaternion.Euler(0f, 12f, 0f) * new Vector3(sx * 0.22f, 0.25f, sz * 0.17f), new Vector3(0.05f, 0.5f, 0.05f), Quaternion.Euler(0f, 12f, 0f));
            PropCol.Box(cols, tb + Vector3.up * 0.27f, new Vector3(0.58f, 0.55f, 0.48f), Quaternion.Euler(0f, 12f, 0f), "SideTable");
            Vector3 th = tb + Vector3.up * 0.53f + new Vector3(0.1f, 0f, 0.05f);
            batch.W(PropMats.Flat(LowPoly.Hex("#B8454A")), th).Cylinder(th, Quaternion.identity, 0.05f, 0.05f, 0.26f, 8, false, true);
            batch.W(PropMats.Metal, th).Cylinder(th + Vector3.up * 0.26f, Quaternion.identity, 0.045f, 0.04f, 0.05f, 8, false, true);
            Vector3 cup = tb + Vector3.up * 0.53f + new Vector3(-0.13f, 0f, -0.06f);
            batch.W(PropMats.Metal, cup).Cylinder(cup, Quaternion.identity, 0.04f, 0.035f, 0.08f, 7, true, false);
            Vector3 rodFoot = pb + sea * 0.35f + along * 0.25f;
            rodFoot.y = G(rodFoot.x, rodFoot.z);
            batch.W(PropMats.WoodDark, rodFoot).Rod(rodFoot, pb + Vector3.up * 2.6f + sea * 0.1f + along * 0.05f, 0.022f, 0.008f, 4);
            batch.W(PropMats.DarkMetal, rodFoot).Cylinder(rodFoot + Vector3.up * 0.55f + sea * -0.02f, Quaternion.Euler(0f, 0f, 90f), 0.05f, 0.05f, 0.05f, 8, true, true);
            // his flip-flops under the hammock
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 ff = c + along * (s * 0.12f) - sea * 0.4f;
                ff.y = G(ff.x, ff.z) + 0.01f;
                batch.W(PropMats.Flat(LowPoly.Hex("#F2C94C")), ff).Box(ff, new Vector3(0.11f, 0.02f, 0.26f), Quaternion.Euler(0f, 8f * s, 0f));
            }
            PropInfo.AddLandSpot(c + Vector3.up * 0.1f, LandSpotKind.Meadow, 3f, "horst_corner");

            batch.Flush(root, "HorstCornerMesh", ShadowCastingMode.On, 220f);
            PropBatch.FlushLod(full, lite, root, "HorstPalms", ShadowCastingMode.On, PropJungle.TreeNear, PropJungle.TreeFar);
            yield break;
        }
    }
}
