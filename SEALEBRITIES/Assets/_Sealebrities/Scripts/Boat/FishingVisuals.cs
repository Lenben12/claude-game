using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Procedural models for the fishing minigame: rod (base + bendable tip), bobber, small fish and the dock props.
    public static class FishingVisuals
    {
        static Mesh fishMesh;
        static Material bucketWater;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { fishMesh = null; bucketWater = null; }

        /// Fish body at 1 m length along +Z (head at +Z), UV u tail..head, v belly..back.
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
            var b = new BoatMeshBuilder();
            float[] z = { -0.5f, -0.42f, -0.28f, -0.08f, 0.12f, 0.3f, 0.42f, 0.5f };
            float[] hw = { 0.012f, 0.028f, 0.055f, 0.08f, 0.085f, 0.068f, 0.042f, 0.004f };
            float[] hh = { 0.03f, 0.065f, 0.125f, 0.17f, 0.172f, 0.14f, 0.085f, 0.008f };
            const int seg = 14;
            var P = new Vector3[z.Length, seg + 1];
            var UV = new Vector2[z.Length, seg + 1];
            for (int r = 0; r < z.Length; r++)
                for (int c = 0; c <= seg; c++)
                {
                    float a = c / (float)seg * Mathf.PI * 2f;
                    P[r, c] = new Vector3(hw[r] * Mathf.Cos(a), hh[r] * Mathf.Sin(a), z[r]);
                    UV[r, c] = new Vector2(z[r] + 0.5f, Mathf.Sin(a) * 0.5f + 0.5f);
                }
            b.Grid(P, UV, Vector3.zero, true);
            Fin(b, new Vector3(0f, 0f, -0.46f), new Vector3(0f, 0.18f, -0.7f), new Vector3(0f, -0.18f, -0.7f));
            Fin(b, new Vector3(0f, 0.15f, -0.15f), new Vector3(0f, 0.27f, -0.02f), new Vector3(0f, 0.16f, 0.14f));
            Fin(b, new Vector3(0f, -0.15f, -0.2f), new Vector3(0f, -0.22f, -0.12f), new Vector3(0f, -0.15f, -0.02f));
            return b.ToMesh("FishingFish");
        }

        static void Fin(BoatMeshBuilder b, Vector3 a, Vector3 c, Vector3 d)
        {
            Vector2 uv = new Vector2(0.05f, 0.8f);
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 n = new Vector3(side, 0f, 0f);
                Vector3 off = n * 0.002f;
                int i0 = b.V(a + off, n, uv), i1 = b.V(c + off, n, uv), i2 = b.V(d + off, n, uv);
                b.TriAuto(i0, i1, i2);
            }
        }

        /// 0 mackerel, 1 sardine, 2 sea bream
        public static GameObject BuildFish(Transform parent, int kind, float length, float heightMul, float widthMul, bool shadows)
        {
            var go = new GameObject("Fish");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = FishMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = BoatMats.Fish(kind);
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
            s[BoatMats.Cork].Cylinder(new Vector3(0f, 0f, -0.06f), new Vector3(0f, 0f, 0.3f), 0.017f, 0.02f, 10);
            s[BoatMats.Rod].Cylinder(new Vector3(0f, 0f, 0.3f), new Vector3(0f, 0f, 1.15f), 0.0095f, 0.0065f, 8, false, false);
            s[BoatMats.Chrome].Box(new Vector3(0f, -0.03f, 0.16f), new Vector3(0.015f, 0.035f, 0.05f));
            s[BoatMats.Engine].Cylinder(new Vector3(-0.022f, -0.07f, 0.16f), new Vector3(0.022f, -0.07f, 0.16f), 0.042f, 0.042f, 14);
            s[BoatMats.Chrome].Cylinder(new Vector3(0.022f, -0.07f, 0.16f), new Vector3(0.05f, -0.07f, 0.16f), 0.006f, 0.006f, 6);
            s[BoatMats.Chrome].Beam(new Vector3(0.05f, -0.07f, 0.16f), new Vector3(0.05f, -0.11f, 0.18f), 0.008f, 0.008f);
            s[BoatMats.Cork].Cylinder(new Vector3(0.05f, -0.11f, 0.18f), new Vector3(0.075f, -0.11f, 0.18f), 0.008f, 0.008f, 6);
            s[BoatMats.Chrome].Torus(new Vector3(0f, -0.02f, 0.55f), Quaternion.Euler(90f, 0f, 0f), 0.01f, 0.002f, 10, 4);
            s[BoatMats.Chrome].Torus(new Vector3(0f, -0.016f, 0.9f), Quaternion.Euler(90f, 0f, 0f), 0.008f, 0.002f, 10, 4);
            s.Build("RodBase", root, shadows);

            tipPivot = new GameObject("RodTipPivot").transform;
            tipPivot.SetParent(root, false);
            tipPivot.localPosition = new Vector3(0f, 0f, 1.15f);
            var ts = new BoatMeshSet();
            ts[BoatMats.Rod].Cylinder(Vector3.zero, new Vector3(0f, 0f, 0.75f), 0.0065f, 0.0025f, 6, false, true);
            ts[BoatMats.Chrome].Torus(new Vector3(0f, -0.012f, 0.3f), Quaternion.Euler(90f, 0f, 0f), 0.006f, 0.0015f, 8, 4);
            ts[BoatMats.Chrome].Torus(new Vector3(0f, -0.009f, 0.6f), Quaternion.Euler(90f, 0f, 0f), 0.005f, 0.0015f, 8, 4);
            ts.Build("RodTip", tipPivot, shadows);

            tip = new GameObject("Tip").transform;
            tip.SetParent(tipPivot, false);
            tip.localPosition = new Vector3(0f, 0f, 0.75f);
            return root;
        }

        public static Transform BuildBobber(Transform parent)
        {
            var s = new BoatMeshSet();
            s[BoatMats.BobberRed].Ellipsoid(Vector3.zero, new Vector3(0.035f, 0.04f, 0.035f), Quaternion.identity, 14, 6, 0f, 90f);
            s[BoatMats.BobberWhite].Ellipsoid(Vector3.zero, new Vector3(0.035f, 0.05f, 0.035f), Quaternion.identity, 14, 6, -90f, 0f);
            s[BoatMats.BobberRed].Cylinder(new Vector3(0f, 0.035f, 0f), new Vector3(0f, 0.11f, 0f), 0.004f, 0.003f, 6);
            s[BoatMats.Chrome].Cylinder(new Vector3(0f, -0.048f, 0f), new Vector3(0f, -0.065f, 0f), 0.003f, 0.003f, 4);
            return s.Build("Bobber", parent, false).transform;
        }

        /// Rod holder with a resting rod, a bucket (for the catch) and a tackle box. Local +Z points to the water.
        public static void BuildSpot(Transform root, out Transform idleRod, out Transform bucketFishRoot)
        {
            if (bucketWater == null) bucketWater = MaterialLibrary.Transparent(new Color(0.18f, 0.32f, 0.36f, 0.75f), 0.95f);
            var s = new BoatMeshSet();
            Quaternion rodRot = Quaternion.Euler(-62f, 0f, 0f);
            Vector3 rodDir = rodRot * Vector3.forward;
            Vector3 rodRoot = new Vector3(0.35f, 0.32f, -0.08f);

            // rod holder: block, post and tilted tube
            s[BoatMats.DeckDark].Box(new Vector3(0.35f, 0.04f, -0.12f), new Vector3(0.24f, 0.08f, 0.26f), 2f);
            s[BoatMats.Metal].Cylinder(new Vector3(0.35f, 0.08f, -0.12f), rodRoot - rodDir * 0.1f, 0.022f, 0.022f, 8);
            s[BoatMats.Metal].Tube(rodRoot - rodDir * 0.12f, rodRoot + rodDir * 0.16f, 0.03f, 0.024f, 10, true);

            // bucket with water and a handle
            Vector3 bc = new Vector3(-0.35f, 0f, 0.05f);
            s[BoatMats.Bucket].Tube(bc, bc + Vector3.up * 0.36f, 0.17f, 0.158f, 18, true);
            s[BoatMats.Bucket].Torus(bc + Vector3.up * 0.36f, Quaternion.identity, 0.165f, 0.009f, 18, 4);
            s[BoatMats.Chrome].Torus(bc + Vector3.up * 0.36f, Quaternion.Euler(90f, 0f, 0f), 0.165f, 0.005f, 20, 4, 10, 20);
            s[bucketWater].Disc(bc + Vector3.up * 0.25f, Vector3.up, 0.156f, 16);

            // tackle box
            s[BoatMats.CoolerLid].Box(new Vector3(0.02f, 0.08f, -0.42f), new Vector3(0.32f, 0.16f, 0.2f), 2f);
            s[BoatMats.Chrome].Box(new Vector3(0.02f, 0.12f, -0.525f), new Vector3(0.05f, 0.03f, 0.012f));
            s[BoatMats.Engine].Box(new Vector3(0.02f, 0.17f, -0.42f), new Vector3(0.16f, 0.015f, 0.03f));
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
