using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Low-poly models for the fishing minigame: rod (base + bendable tip), bobber, small fish and the dock props.
    /// Flat-shaded solid colors, chunky shapes.
    public static class FishingVisuals
    {
        static Mesh fishMesh;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { fishMesh = null; }

        /// Faceted fish body at 1 m length along +Z (head at +Z). Submeshes: 0 back + fins, 1 flanks, 2 belly, 3 eyes
        /// (see BoatMats.FishMats).
        public static Mesh FishMesh
        {
            get
            {
                if (fishMesh == null) fishMesh = BuildFishMesh();
                return fishMesh;
            }
        }

        static Mesh BuildFishMesh()
        {
            var set = new FishSet();
            float[] z = { -0.5f, -0.34f, -0.1f, 0.16f, 0.36f, 0.5f };
            float[] hw = { 0.014f, 0.045f, 0.085f, 0.085f, 0.055f, 0.006f };
            float[] hh = { 0.032f, 0.09f, 0.17f, 0.165f, 0.11f, 0.01f };
            // hexagon section: top, upper flanks, lower flanks, bottom
            const int seg = 6;
            var P = new Vector3[z.Length, seg];
            for (int r = 0; r < z.Length; r++)
                for (int c = 0; c < seg; c++)
                {
                    float a = Mathf.PI * 0.5f - c / (float)seg * Mathf.PI * 2f;
                    P[r, c] = new Vector3(hw[r] * Mathf.Cos(a) * 1.15f, hh[r] * Mathf.Sin(a), z[r]);
                }
            for (int r = 0; r < z.Length - 1; r++)
                for (int c = 0; c < seg; c++)
                {
                    int c1 = (c + 1) % seg;
                    float my = (P[r, c].y + P[r, c1].y) / Mathf.Max(1e-4f, hh[r]);   // -2 .. 2
                    var b = my > 0.9f ? set.back : my < -0.9f ? set.belly : set.flank;
                    Vector3 mid = (P[r, c] + P[r + 1, c] + P[r + 1, c1] + P[r, c1]) * 0.25f;
                    b.QuadOut(P[r, c], P[r + 1, c], P[r + 1, c1], P[r, c1], new Vector3(mid.x, mid.y, 0f));
                }
            // tail, dorsal and anal fins (two-sided by geometry)
            Fin(set.back, new Vector3(0f, 0f, -0.46f), new Vector3(0f, 0.2f, -0.72f), new Vector3(0f, -0.2f, -0.72f));
            Fin(set.back, new Vector3(0f, 0.15f, -0.18f), new Vector3(0f, 0.28f, -0.04f), new Vector3(0f, 0.16f, 0.12f));
            Fin(set.back, new Vector3(0f, -0.15f, -0.22f), new Vector3(0f, -0.23f, -0.14f), new Vector3(0f, -0.15f, -0.02f));
            // eyes: small dark diamonds on both sides of the head
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 e = new Vector3(side * 0.069f, 0.045f, 0.33f);
                Vector3 n = new Vector3(side, 0f, 0.35f);
                set.eye.QuadOut(e + new Vector3(0f, 0.028f, 0f), e + new Vector3(0f, 0f, 0.026f), e + new Vector3(0f, -0.028f, 0f), e + new Vector3(0f, 0f, -0.026f), n);
            }

            var verts = new System.Collections.Generic.List<Vector3>();
            var norms = new System.Collections.Generic.List<Vector3>();
            var subs = new System.Collections.Generic.List<int>[4];
            var bs = new[] { set.back, set.flank, set.belly, set.eye };
            for (int i = 0; i < 4; i++)
            {
                subs[i] = new System.Collections.Generic.List<int>();
                bs[i].AppendFlat(verts, norms, subs[i]);
            }
            var mesh = new Mesh { name = "FishingFish" };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.subMeshCount = 4;
            for (int i = 0; i < 4; i++) mesh.SetTriangles(subs[i], i);
            mesh.RecalculateBounds();
            return mesh;
        }

        sealed class FishSet
        {
            public readonly BoatMeshBuilder back = new BoatMeshBuilder(), flank = new BoatMeshBuilder(),
                belly = new BoatMeshBuilder(), eye = new BoatMeshBuilder();
        }

        static void Fin(BoatMeshBuilder b, Vector3 a, Vector3 c, Vector3 d)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 off = new Vector3(side * 0.002f, 0f, 0f);
                b.TriOut(a + off, c + off, d + off, new Vector3(side, 0f, 0f));
            }
        }

        /// 0 mackerel, 1 sardine, 2 sea bream
        public static GameObject BuildFish(Transform parent, int kind, float length, float heightMul, float widthMul, bool shadows)
        {
            var go = new GameObject("Fish");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = FishMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = BoatMats.FishMats(kind);
            mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            go.transform.localScale = new Vector3(length * widthMul, length * heightMul, length);
            return go;
        }

        /// Spinning rod along +Z with the butt at the origin; the tip section hangs on tipPivot (bend it around X).
        public static Transform BuildRod(Transform parent, bool shadows, out Transform tipPivot, out Transform tip)
        {
            var root = new GameObject("FishingRod").transform;
            root.SetParent(parent, false);
            var s = new BoatMeshSet();
            s[BoatMats.Cork].Cylinder(new Vector3(0f, 0f, -0.06f), new Vector3(0f, 0f, 0.3f), 0.02f, 0.023f, 6);
            s[BoatMats.Rod].Cylinder(new Vector3(0f, 0f, 0.3f), new Vector3(0f, 0f, 1.15f), 0.011f, 0.0075f, 5, false, false);
            s[BoatMats.Steel].Box(new Vector3(0f, -0.03f, 0.16f), new Vector3(0.018f, 0.04f, 0.055f));
            s[BoatMats.LifeOrange].Cylinder(new Vector3(-0.024f, -0.075f, 0.16f), new Vector3(0.024f, -0.075f, 0.16f), 0.045f, 0.045f, 6);
            s[BoatMats.Steel].Cylinder(new Vector3(0.024f, -0.075f, 0.16f), new Vector3(0.05f, -0.075f, 0.16f), 0.008f, 0.008f, 4);
            s[BoatMats.Steel].Beam(new Vector3(0.05f, -0.075f, 0.16f), new Vector3(0.05f, -0.115f, 0.18f), 0.01f, 0.01f);
            s[BoatMats.Cork].Cylinder(new Vector3(0.05f, -0.115f, 0.18f), new Vector3(0.078f, -0.115f, 0.18f), 0.01f, 0.01f, 4);
            s[BoatMats.Steel].Box(new Vector3(0f, -0.018f, 0.55f), new Vector3(0.006f, 0.022f, 0.006f));
            s[BoatMats.Steel].Box(new Vector3(0f, -0.015f, 0.9f), new Vector3(0.005f, 0.018f, 0.005f));
            s.Build("RodBase", root, shadows);

            tipPivot = new GameObject("RodTipPivot").transform;
            tipPivot.SetParent(root, false);
            tipPivot.localPosition = new Vector3(0f, 0f, 1.15f);
            var ts = new BoatMeshSet();
            ts[BoatMats.Rod].Cylinder(Vector3.zero, new Vector3(0f, 0f, 0.75f), 0.0075f, 0.003f, 4, false, true);
            ts[BoatMats.BobberRed].Cylinder(new Vector3(0f, 0f, 0.66f), new Vector3(0f, 0f, 0.75f), 0.0045f, 0.0035f, 4, false, true);
            ts[BoatMats.Steel].Box(new Vector3(0f, -0.012f, 0.3f), new Vector3(0.004f, 0.014f, 0.004f));
            ts[BoatMats.Steel].Box(new Vector3(0f, -0.009f, 0.6f), new Vector3(0.004f, 0.012f, 0.004f));
            ts.Build("RodTip", tipPivot, shadows);

            tip = new GameObject("Tip").transform;
            tip.SetParent(tipPivot, false);
            tip.localPosition = new Vector3(0f, 0f, 0.75f);
            return root;
        }

        /// Simple diamond bobber: red top cone, white bottom cone, a red antenna.
        public static Transform BuildBobber(Transform parent)
        {
            var s = new BoatMeshSet();
            s[BoatMats.BobberRed].Cylinder(Vector3.zero, new Vector3(0f, 0.045f, 0f), 0.04f, 0.008f, 6, false, true);
            s[BoatMats.BobberWhite].Cylinder(Vector3.zero, new Vector3(0f, -0.055f, 0f), 0.04f, 0.006f, 6, false, true);
            s[BoatMats.BobberRed].Cylinder(new Vector3(0f, 0.045f, 0f), new Vector3(0f, 0.11f, 0f), 0.005f, 0.004f, 4);
            return s.Build("Bobber", parent, false).transform;
        }

        /// Rod holder with a resting rod, a bucket (for the catch) and a tackle box. Local +Z points to the water.
        public static void BuildSpot(Transform root, out Transform idleRod, out Transform bucketFishRoot)
        {
            var s = new BoatMeshSet();
            Quaternion rodRot = Quaternion.Euler(-62f, 0f, 0f);
            Vector3 rodDir = rodRot * Vector3.forward;
            Vector3 rodRoot = new Vector3(0.35f, 0.32f, -0.08f);

            // rod holder: wooden block, post and tilted tube
            s[BoatMats.DeckDark].Box(new Vector3(0.35f, 0.045f, -0.12f), new Vector3(0.26f, 0.09f, 0.28f));
            s[BoatMats.Metal].Cylinder(new Vector3(0.35f, 0.09f, -0.12f), rodRoot - rodDir * 0.1f, 0.025f, 0.025f, 5);
            s[BoatMats.Metal].Tube(rodRoot - rodDir * 0.12f, rodRoot + rodDir * 0.16f, 0.034f, 0.027f, 6, true);

            // bucket with water and a handle
            Vector3 bc = new Vector3(-0.35f, 0f, 0.05f);
            s[BoatMats.Bucket].Tube(bc, bc + Vector3.up * 0.36f, 0.175f, 0.16f, 8, true);
            s[BoatMats.Bucket].Tube(bc + Vector3.up * 0.34f, bc + Vector3.up * 0.37f, 0.187f, 0.16f, 8, false);
            s[BoatMats.Steel].Torus(bc + Vector3.up * 0.36f, Quaternion.Euler(90f, 0f, 0f), 0.17f, 0.007f, 12, 3, 6, 12);
            s[BoatMats.BucketWater].Disc(bc + Vector3.up * 0.25f, Vector3.up, 0.158f, 8);

            // tackle box
            s[BoatMats.Tackle].Box(new Vector3(0.02f, 0.07f, -0.42f), new Vector3(0.32f, 0.14f, 0.2f));
            s[BoatMats.Tackle].TaperBox(new Vector3(0.02f, 0.16f, -0.42f), new Vector2(0.33f, 0.21f), new Vector2(0.29f, 0.17f), 0.04f, Quaternion.identity);
            s[BoatMats.Steel].Box(new Vector3(0.02f, 0.12f, -0.525f), new Vector3(0.05f, 0.035f, 0.014f));
            s[BoatMats.Ink].Box(new Vector3(0.02f, 0.195f, -0.42f), new Vector3(0.16f, 0.02f, 0.035f));
            s.Build("FishingProps", root, true);

            idleRod = BuildRod(root, true, out _, out _);
            idleRod.localPosition = rodRoot;
            idleRod.localRotation = rodRot;

            bucketFishRoot = new GameObject("BucketFish").transform;
            bucketFishRoot.SetParent(root, false);
            bucketFishRoot.localPosition = bc;
        }
    }
}
