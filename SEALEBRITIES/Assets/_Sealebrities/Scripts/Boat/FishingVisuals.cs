using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Low-poly models for fishing: the first-person rod (How to Fish style: soft, smooth shaded, small; the Player
    /// module's mitten hands hold it), the bobber, fish per FishMarket kind, the old boot and the dock rod rack.
    /// The world-side props stay faceted; the view model uses smooth normals (art bible §1.13).
    public static class FishingVisuals
    {
        static Mesh fishMesh;
        static readonly Dictionary<string, Material[]> kindMats = new Dictionary<string, Material[]>();
        static readonly Dictionary<int, Material> viewMats = new Dictionary<int, Material>();

        /// Render queue of first-person view models (same as the Player/Filming view models): opaque shader drawn
        /// after the opaque pass so SSAO never mottles the flat colours of the hands.
        public const int ViewModelQueue = 2600;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { fishMesh = null; kindMats.Clear(); viewMats.Clear(); }

        // ------------------------------------------------------------------ fish
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
            // eyes: dark diamonds on both sides of the head
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 e = new Vector3(side * 0.069f, 0.045f, 0.33f);
                Vector3 n = new Vector3(side, 0f, 0.35f);
                set.eye.QuadOut(e + new Vector3(0f, 0.028f, 0f), e + new Vector3(0f, 0f, 0.026f), e + new Vector3(0f, -0.028f, 0f), e + new Vector3(0f, 0f, -0.026f), n);
            }

            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var subs = new List<int>[4];
            var bs = new[] { set.back, set.flank, set.belly, set.eye };
            for (int i = 0; i < 4; i++)
            {
                subs[i] = new List<int>();
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

        /// 0 mackerel, 1 sardine, 2 sea bream (fixed colours).
        public static GameObject BuildFish(Transform parent, int kind, float length, float heightMul, float widthMul, bool shadows)
        {
            return BuildFish(parent, BoatMats.FishMats(kind), length, heightMul, widthMul, shadows);
        }

        public static GameObject BuildFish(Transform parent, Material[] mats, float length, float heightMul, float widthMul, bool shadows)
        {
            var go = new GameObject("Fish");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = FishMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mats;
            mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            go.transform.localScale = new Vector3(length * widthMul, length * heightMul, length);
            return go;
        }

        /// Materials of a FishMarket kind (back, flank, belly, eye), cached per kind.
        public static Material[] KindMats(FishKind k)
        {
            if (k == null) return BoatMats.FishMats(0);
            if (kindMats.TryGetValue(k.id, out var m) && m != null && m[0] != null) return m;
            m = new[] { LowPoly.Mat(k.back), LowPoly.Mat(k.flank), LowPoly.Mat(k.belly), LowPoly.Mat(LowPoly.Ink) };
            kindMats[k.id] = m;
            return m;
        }

        /// A caught fish (or the old boot for junk) with its mouth / hanging point at the local origin and the body
        /// hanging down along -Y (head up), so it can dangle from the line. Length in meters.
        public static GameObject BuildCatch(Transform parent, FishKind kind, float kg, bool shadows, out float length)
        {
            var root = new GameObject(kind != null ? "Catch_" + kind.id : "Catch");
            root.transform.SetParent(parent, false);
            if (kind != null && kind.junk)
            {
                length = 0.3f;
                BuildBoot(root.transform, shadows);
                return root;
            }
            length = kind != null ? Mathf.Clamp(kind.LengthFor(kg), 0.12f, 1.4f) : 0.3f;
            var fish = BuildFish(root.transform, KindMats(kind), length, kind != null ? kind.heightMul : 1f, kind != null ? kind.widthMul : 1f, shadows);
            // mesh: head at +Z (z = +0.5). Hang head-up: +Z → +Y, mouth at the origin.
            fish.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            fish.transform.localPosition = new Vector3(0f, -length * 0.5f, 0f);
            return root;
        }

        /// Soggy old boot (junk catch), hanging from its top (origin) downward.
        static void BuildBoot(Transform parent, bool shadows)
        {
            var s = new BoatMeshSet();
            var leather = LowPoly.Mat(LowPoly.Hex("#5A4632"));
            var dark = LowPoly.Mat(LowPoly.Hex("#3C2E22"));
            s[leather].Cylinder(new Vector3(0f, -0.02f, 0f), new Vector3(0f, -0.2f, 0f), 0.055f, 0.06f, 7);
            s[leather].Box(new Vector3(0f, -0.235f, 0.05f), new Vector3(0.1f, 0.075f, 0.2f));
            s[dark].Box(new Vector3(0f, -0.28f, 0.05f), new Vector3(0.11f, 0.02f, 0.22f));
            s[dark].Cylinder(new Vector3(0f, -0.018f, 0f), new Vector3(0f, 0f, 0f), 0.062f, 0.062f, 7);
            s.Build("Boot", parent, Vector3.zero, Quaternion.Euler(0f, 0f, 12f), shadows);
        }

        // ------------------------------------------------------------------ bobber + dock props
        /// Simple diamond bobber: red top cone, white bottom cone, a red antenna.
        public static Transform BuildBobber(Transform parent)
        {
            var s = new BoatMeshSet();
            s[BoatMats.BobberRed].Cylinder(Vector3.zero, new Vector3(0f, 0.045f, 0f), 0.04f, 0.008f, 6, false, true);
            s[BoatMats.BobberWhite].Cylinder(Vector3.zero, new Vector3(0f, -0.055f, 0f), 0.04f, 0.006f, 6, false, true);
            s[BoatMats.BobberRed].Cylinder(new Vector3(0f, 0.045f, 0f), new Vector3(0f, 0.11f, 0f), 0.005f, 0.004f, 4);
            return s.Build("Bobber", parent, false).transform;
        }

        /// Spinning rod along +Z with the butt at the origin (world prop for the rack); the tip section hangs on tipPivot.
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

        /// Rod rack with a resting rod, a bucket (shows the cooler-box catch) and a tackle box. Local +Z points to the water.
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

        // ------------------------------------------------------------------ first-person rod (view model)
        /// Matte palette colour on the view-model render queue (cached per colour; the shared world material is never modified).
        public static Material ViewMat(Color c)
        {
            int key = ((int)(c.r * 255f) << 16) | ((int)(c.g * 255f) << 8) | (int)(c.b * 255f);
            if (viewMats.TryGetValue(key, out var m) && m != null) return m;
            var shared = LowPoly.Mat(c);
            m = new Material(shared) { name = "SB_RodVM" };
            m.renderQueue = ViewModelQueue;
            viewMats[key] = m;
            return m;
        }

        /// Colours of a rod tier: 0 bamboo, 1 carbon (art bible: navy with a blue band), 2 deep-sea (white with orange).
        static void RodColors(int tier, out Color grip, out Color blank, out Color band, out Color reel, out Color node)
        {
            switch (tier)
            {
                case 0:
                    grip = LowPoly.Hex("#B98B5A"); blank = LowPoly.Hex("#C9AE68"); band = LowPoly.Hex("#A8484A");
                    reel = LowPoly.Hex("#8E949C"); node = LowPoly.Hex("#8E7440");
                    break;
                case 2:
                    grip = LowPoly.Hex("#1E1B2B"); blank = LowPoly.Hex("#EDE6DA"); band = LowPoly.Hex("#EE7F2D");
                    reel = LowPoly.Hex("#F2C94C"); node = LowPoly.Hex("#EE7F2D");
                    break;
                default:
                    grip = LowPoly.Hex("#2E2A40"); blank = LowPoly.Hex("#2E2A40"); band = LowPoly.Hex("#3D7BC4");
                    reel = LowPoly.Hex("#8E949C"); node = LowPoly.Hex("#3D7BC4");
                    break;
            }
        }

        /// Builds the first-person rod under `parent` (the player camera). The hands are NOT part of it: the Player
        /// module (PlayerHands) finds the camera descendant named "FishingRod…" and attaches its mitten hands at its
        /// grip points (rod space, +Z along the rod from the butt: main hand z ≈ 0.23 on the fore grip, the other hand
        /// at the reel crank ≈ (0.06, −0.105, 0.18)). The reel hangs on −Y, the crank is on +X.
        public static RodViewModel BuildViewRod(Transform parent, int tier)
        {
            var vm = new RodViewModel { tier = tier };
            vm.root = new GameObject("RodViewRoot").transform;
            vm.root.SetParent(parent, false);
            vm.rig = new GameObject("FishingRod").transform;   // name + frame are the Player module's contract
            vm.rig.SetParent(vm.root, false);

            RodColors(tier, out Color grip, out Color blank, out Color band, out Color reelC, out Color node);
            Material mGrip = ViewMat(grip), mBlank = ViewMat(blank), mBand = ViewMat(band), mReel = ViewMat(reelC), mNode = ViewMat(node);
            Material mDark = ViewMat(LowPoly.Hex("#1E1B2B")), mLine = ViewMat(LowPoly.Hex("#E8E2D0")), mGuide = ViewMat(LowPoly.Hex("#8E949C"));
            float th = tier == 2 ? 1.15f : 1f;   // the deep-sea rod is a bit chunkier
            const float seat = RodViewModel.ReelZ;
            const float baseEnd = RodViewModel.BaseLength;

            // ---- base: butt, rear grip, reel seat, fore grip (main hand), band, lower blank, guides, reel
            var b = new SmoothMeshSet();
            b.Tube(mDark, new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 0.015f), 0.0062f * th, 0.0076f * th, 10, true, false);
            b.Tube(mGrip, new Vector3(0f, 0f, 0.015f), new Vector3(0f, 0f, seat - 0.045f), 0.0076f * th, 0.0072f * th, 10, false, false);
            b.Tube(mReel, new Vector3(0f, 0f, seat - 0.045f), new Vector3(0f, 0f, seat + 0.04f), 0.0062f * th, 0.0062f * th, 10, true, true);
            b.Tube(mGrip, new Vector3(0f, 0f, seat + 0.04f), new Vector3(0f, 0f, 0.33f), 0.007f * th, 0.0064f * th, 10, true, true);
            b.Tube(mBand, new Vector3(0f, 0f, 0.33f), new Vector3(0f, 0f, 0.36f), 0.0058f * th, 0.0056f * th, 10, true, true);
            b.Tube(mBlank, new Vector3(0f, 0f, 0.36f), new Vector3(0f, 0f, baseEnd), 0.005f * th, 0.0033f * th, 8, false, false);
            if (tier == 0)
            {
                // bamboo nodes
                for (float z = 0.56f; z < baseEnd - 0.05f; z += 0.22f)
                {
                    float r = Mathf.Lerp(0.005f, 0.0033f, (z - 0.36f) / (baseEnd - 0.36f)) * 1.25f;
                    b.Tube(mNode, new Vector3(0f, 0f, z - 0.008f), new Vector3(0f, 0f, z + 0.008f), r, r, 8, true, true);
                }
            }
            float[] guides = { 0.6f, 0.95f };
            for (int i = 0; i < guides.Length; i++)
            {
                float z = guides[i];
                float gr = 0.0075f - i * 0.0015f;
                b.Tube(mGuide, new Vector3(0f, -0.003f, z), new Vector3(0f, -0.012f - gr, z + 0.004f), 0.0012f, 0.0012f, 4, false, false);
                b.Ellipsoid(mGuide, new Vector3(0f, -0.012f - gr, z + 0.004f), new Vector3(gr, gr, 0.0016f), Quaternion.identity, 8, 3);
            }
            // spinning reel under the seat, crank on the right (+X)
            float rs = tier == 2 ? 1.2f : 1f;
            b.Tube(mReel, new Vector3(0f, -0.005f, seat), new Vector3(0f, -0.04f * rs, seat + 0.004f), 0.0035f, 0.0035f, 6, false, false);
            Vector3 bodyC = new Vector3(0f, -0.055f * rs, seat);
            b.Tube(mReel, bodyC + new Vector3(0f, 0f, -0.026f * rs), bodyC + new Vector3(0f, 0f, 0.018f * rs), 0.016f * rs, 0.019f * rs, 10, true, false);
            b.Tube(mLine, bodyC + new Vector3(0f, 0f, 0.018f * rs), bodyC + new Vector3(0f, 0f, 0.038f * rs), 0.021f * rs, 0.021f * rs, 10, true, true);
            b.Ellipsoid(mReel, bodyC + new Vector3(0f, 0f, 0.042f * rs), new Vector3(0.012f, 0.012f, 0.006f) * rs, Quaternion.identity, 8, 4);
            vm.crankPivotLocal = bodyC + new Vector3(0.034f * rs, 0f, 0f);
            b.Tube(mReel, bodyC + new Vector3(0.016f * rs, 0f, 0f), vm.crankPivotLocal, 0.004f, 0.004f, 6, false, true);
            b.Build("RodBase", vm.rig);

            // ---- crank (spins around local X while reeling); the knob ends near the support-hand grip point
            vm.crank = new GameObject("Crank").transform;
            vm.crank.SetParent(vm.rig, false);
            vm.crank.localPosition = vm.crankPivotLocal;
            var c = new SmoothMeshSet();
            Vector3 armEnd = new Vector3(0f, -0.04f, 0.008f);
            c.Tube(mReel, Vector3.zero, armEnd, 0.003f, 0.0025f, 6, true, true);
            c.Tube(mDark, armEnd, armEnd + new Vector3(0.022f, -0.003f, 0.002f), 0.0055f, 0.005f, 8, true, true);
            c.Build("CrankArm", vm.crank);

            // ---- tip section (bends around local X)
            vm.tipPivot = new GameObject("TipPivot").transform;
            vm.tipPivot.SetParent(vm.rig, false);
            vm.tipPivot.localPosition = new Vector3(0f, 0f, baseEnd);
            var t = new SmoothMeshSet();
            t.Tube(mBlank, Vector3.zero, new Vector3(0f, 0f, RodViewModel.TipLength), 0.0033f * th, 0.0014f * th, 6, false, false);
            t.Ellipsoid(mBand, new Vector3(0f, 0f, RodViewModel.TipLength), new Vector3(0.0026f, 0.0026f, 0.0035f), Quaternion.identity, 6, 3);
            t.Tube(mGuide, new Vector3(0f, -0.002f, 0.35f), new Vector3(0f, -0.009f, 0.353f), 0.001f, 0.001f, 4, false, false);
            t.Ellipsoid(mGuide, new Vector3(0f, -0.0115f, 0.354f), new Vector3(0.0035f, 0.0035f, 0.0012f), Quaternion.identity, 6, 3);
            t.Build("RodTip", vm.tipPivot);
            vm.tip = new GameObject("Tip").transform;
            vm.tip.SetParent(vm.tipPivot, false);
            vm.tip.localPosition = new Vector3(0f, 0f, RodViewModel.TipLength + 0.003f);
            return vm;
        }
    }

    /// Transforms of the first-person rod view model (see FishingVisuals.BuildViewRod). Rod space: origin at the
    /// butt, +Z along the rod, reel seat at ReelZ, the main hand's grip around GripZ.
    public sealed class RodViewModel
    {
        public const float ReelZ = 0.17f, GripZ = 0.23f, BaseLength = 1.36f, TipLength = 0.8f;
        /// Camera-space rotation of the rod at rest (fitted to How to Fish: fore grip ≈ 67 % / 75 % of the screen,
        /// the rod leaves the bottom edge at x ≈ 71 % and the top edge around x ≈ 54 %).
        public static readonly Quaternion BaseRotation =
            Quaternion.LookRotation(new Vector3(0f, 0.7507f, 0.6606f), Vector3.up) * Quaternion.Euler(0f, 0f, -20f);
        /// Camera-space position of the reel seat at rest.
        public static readonly Vector3 ReelSeatPosition = new Vector3(0.2f, -0.21f, 0.42f);
        /// The cast swings the rod around this rod-space point (between the hands).
        public static readonly Vector3 PivotLocal = new Vector3(0f, 0f, 0.2f);
        /// Camera-space position of the swing pivot at rest.
        public static readonly Vector3 PivotCamera = ReelSeatPosition + BaseRotation * new Vector3(0f, 0f, 0.2f - ReelZ);

        public Transform root, rig, tipPivot, tip, crank;
        public Vector3 crankPivotLocal;
        public int tier;

        public void SetCrank(float degrees)
        {
            if (crank != null) crank.localRotation = Quaternion.Euler(degrees, 0f, 0f);
        }
    }

    /// Smooth-shaded mesh assembly (one submesh per material) for small first-person parts.
    /// Triangles are wound automatically so their face normal agrees with the vertex normals.
    public sealed class SmoothMeshSet
    {
        readonly List<Material> mats = new List<Material>();
        readonly List<List<int>> tris = new List<List<int>>();
        readonly List<Vector3> verts = new List<Vector3>(512);
        readonly List<Vector3> norms = new List<Vector3>(512);

        List<int> T(Material m)
        {
            int i = mats.IndexOf(m);
            if (i >= 0) return tris[i];
            mats.Add(m);
            var l = new List<int>(256);
            tris.Add(l);
            return l;
        }

        int V(Vector3 p, Vector3 n)
        {
            verts.Add(p);
            norms.Add(n.sqrMagnitude > 1e-12f ? n.normalized : Vector3.up);
            return verts.Count - 1;
        }

        void Tri(List<int> t, int a, int b, int c)
        {
            Vector3 f = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
            if (f.sqrMagnitude < 1e-18f) return;   // degenerate (poles)
            Vector3 n = norms[a] + norms[b] + norms[c];
            if (Vector3.Dot(f, n) < 0f) { t.Add(a); t.Add(c); t.Add(b); }
            else { t.Add(a); t.Add(b); t.Add(c); }
        }

        static void Basis(Vector3 d, out Vector3 u, out Vector3 w)
        {
            u = Vector3.Cross(d, Mathf.Abs(d.y) < 0.95f ? Vector3.up : Vector3.right).normalized;
            w = Vector3.Cross(d, u);
        }

        /// Smooth (tapered) tube from a to b; caps are flat.
        public void Tube(Material m, Vector3 a, Vector3 b, float ra, float rb, int seg, bool capA, bool capB)
        {
            Vector3 axis = b - a;
            float len = axis.magnitude;
            if (len < 1e-6f) return;
            var t = T(m);
            seg = Mathf.Max(3, seg);
            Vector3 d = axis / len;
            Basis(d, out var u, out var w);
            float slope = (ra - rb) / len;
            int start = verts.Count;
            for (int i = 0; i <= seg; i++)
            {
                float ang = i / (float)seg * Mathf.PI * 2f;
                Vector3 radial = u * Mathf.Cos(ang) + w * Mathf.Sin(ang);
                Vector3 n = radial + d * slope;
                V(a + radial * ra, n);
                V(b + radial * rb, n);
            }
            for (int i = 0; i < seg; i++)
            {
                int i0 = start + i * 2;
                Tri(t, i0, i0 + 1, i0 + 3);
                Tri(t, i0, i0 + 3, i0 + 2);
            }
            if (capA && ra > 0f) Disc(t, a, -d, ra, seg, u, w);
            if (capB && rb > 0f) Disc(t, b, d, rb, seg, u, w);
        }

        void Disc(List<int> t, Vector3 c, Vector3 n, float r, int seg, Vector3 u, Vector3 w)
        {
            int ci = V(c, n);
            int start = verts.Count;
            for (int i = 0; i <= seg; i++)
            {
                float ang = i / (float)seg * Mathf.PI * 2f;
                V(c + (u * Mathf.Cos(ang) + w * Mathf.Sin(ang)) * r, n);
            }
            for (int i = 0; i < seg; i++) Tri(t, ci, start + i, start + i + 1);
        }

        /// Smooth ellipsoid.
        public void Ellipsoid(Material m, Vector3 c, Vector3 radii, Quaternion rot, int seg, int rings)
        {
            var t = T(m);
            seg = Mathf.Max(3, seg);
            rings = Mathf.Max(2, rings);
            Vector3 inv = new Vector3(1f / Mathf.Max(1e-5f, radii.x), 1f / Mathf.Max(1e-5f, radii.y), 1f / Mathf.Max(1e-5f, radii.z));
            int start = verts.Count;
            for (int r = 0; r <= rings; r++)
            {
                float lat = Mathf.Lerp(-90f, 90f, r / (float)rings) * Mathf.Deg2Rad;
                for (int s = 0; s <= seg; s++)
                {
                    float lon = s / (float)seg * Mathf.PI * 2f;
                    Vector3 unit = new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon));
                    V(c + rot * Vector3.Scale(unit, radii), rot * Vector3.Scale(unit, inv));
                }
            }
            int row = seg + 1;
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < seg; s++)
                {
                    int i0 = start + r * row + s;
                    Tri(t, i0, i0 + 1, i0 + row + 1);
                    Tri(t, i0, i0 + row + 1, i0 + row);
                }
        }

        public GameObject Build(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var used = new List<Material>();
            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            int sub = 0;
            for (int i = 0; i < tris.Count; i++) if (tris[i].Count > 0) sub++;
            mesh.subMeshCount = sub;
            int k = 0;
            for (int i = 0; i < tris.Count; i++)
            {
                if (tris[i].Count == 0) continue;
                mesh.SetTriangles(tris[i], k++);
                used.Add(mats[i]);
            }
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = used.ToArray();
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            BoatOwnedMesh.Own(go);
            return go;
        }
    }
}
