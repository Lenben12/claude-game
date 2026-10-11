using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// First-person view model in the How to Fish style (art bible §1.13): small soft mitten hands that only show what
    /// the Hands hotbar says is held. Right / left hand each show their active item (lamp model built here, the camera
    /// model comes from the filming module with its own gloved hand, the rod is followed with both hands), empty hands
    /// stay out of view on land and do short breaststrokes while swimming. While the phone is open the left hand raises
    /// the old scratched 3D phone (PlayerPhone) and the right hand hovers next to it.
    /// Motion: idle sway ±4 mm at 0.35 Hz, walk bob 10 / 6 mm, look lag 0.06 s, equip 0.25 s, stow 0.20 s.
    /// Round 2: lighter neoprene gloves (#4A4F66, pads #3A3E52) and cuffs from DiverLook.cuff, every held item ×0.85
    /// and +10 % lighter (Q1/Q2); the wrist dive computer on the left wrist in the water (Q40, WristComputer); items pull
    /// back when a wall/plank/hull is right in front of the eye (1d); eating in first person (FoodEaten, E.1/E.2);
    /// the walk bob follows the head-bob toggle (Q99).
    [DefaultExecutionOrder(60)]
    public class PlayerHands : MonoBehaviour
    {
        /// Same render queue as the filming module's camera model (FilmMaterials.ViewModelQueue): opaque shader,
        /// drawn in the transparent pass, so the view model stays out of the SSAO prepass. Everything below the camera
        /// is rendered by the view-model overlay camera (PlayerViewModelCamera) on top of the world.
        public const int ViewModelQueue = 2600;

        enum Mode { Hidden, Empty, Lamp, Camera, RodMain, RodSupport, Phone, PhoneTap, Eat, Ladder }

        sealed class ViewHand
        {
            public Hand hand;
            public float side;                // -1 left, +1 right
            public Transform root, geo, palm, lampRoot, lens, foodRoot;
            public Renderer lensR;
            /// Food models built for this hand (by food id), shown while eating.
            public Dictionary<string, Transform> foods;
            public Transform food;
            public int lampTier = -1;
            public bool lensLit;
            public Mode shown = Mode.Hidden, want = Mode.Hidden;
            public float presence;
            public Vector3 pos;
            public Quaternion rot = Quaternion.identity;
            public bool poseValid;
        }

        // palette (art bible §1.13 + round 2 Q1/Q2): lighter neoprene gloves everywhere, items +10 % value
        static readonly Color Glove = DiverGear.Glove;
        static readonly Color PalmPad = DiverGear.GlovePad;
        static readonly Color LampYellow = Lift(LowPoly.Hex("#F2C94C"));
        static readonly Color LampDark = Lift(LowPoly.Hex("#2E3140"));
        static readonly Color LampAccent = Lift(LowPoly.Hex("#EE7F2D"));
        static readonly Color Tape = Lift(LowPoly.Hex("#A9AEB4"));
        static readonly Color RodColor = Lift(LowPoly.Hex("#2E2A40"));
        static readonly Color RodBand = Lift(LowPoly.Hex("#3D7BC4"));
        static readonly Color ReelColor = Lift(LowPoly.Hex("#8E949C"));

        /// Q1: every held item (hand included) at 0.85 of the art-bible §1.13 size – one-hand item ≤ 210 × 230 px.
        public const float ItemScale = 0.85f;

        /// Q1 "items +10 % value": HSV value × 1.1 (+0.03 so the dark lamp bodies lift visibly too).
        public static Color Lift(Color c)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            Color o = Color.HSVToRGB(h, s, Mathf.Min(1f, v * 1.1f + 0.03f));
            o.a = c.a;
            return o;
        }

        /// 1d part 2: how far the held items are pulled back/down because a wall, plank or hull is right in front of
        /// the eye (metres, smoothed). The filming module's camera rig may add it to its pose as well.
        public static float PullBack { get; private set; }

        static float lampStolenUntil = -1f;

        /// G.3 "octopus_lamp": the lamp is gone from the hand (and dark) for a few seconds, then drops back into it.
        /// Called by the fauna mischief (J.4) and by PlayerController on GameEvents.Mischief – idempotent.
        public static void StealLamp(float seconds)
        {
            if (!(seconds > 0f)) return;
            lampStolenUntil = Mathf.Max(lampStolenUntil, Time.time + Mathf.Min(seconds, 30f));
        }

        /// True while an octopus holds the lamp (PlayerFlashlight keeps it dark).
        public static bool LampStolen => Time.time < lampStolenUntil;

        // camera-space poses (x right, y up, z forward)
        static readonly Vector3 RightItemPos = new Vector3(0.20f, -0.20f, 0.46f);
        static readonly Vector3 RightItemRot = new Vector3(2f, -6f, -4f);
        static readonly Vector3 LeftItemPos = new Vector3(-0.20f, -0.21f, 0.46f);
        static readonly Vector3 LeftItemRot = new Vector3(2f, 6f, 4f);
        static readonly Vector3 RodGrip = new Vector3(0.18f, -0.15f, 0.45f);
        static readonly Vector3 RodTopPoint = new Vector3(0.169f, 1.19f, 1.7f);
        static readonly Vector3 ShoulderR = new Vector3(0.26f, -0.5f, 0.04f);

        // breaststroke key poses for the LEFT hand (right hand mirrored)
        static readonly Vector3 K0p = new Vector3(-0.10f, -0.21f, 0.30f);
        static readonly Vector3 K1p = new Vector3(-0.06f, -0.14f, 0.48f);
        static readonly Vector3 K2p = new Vector3(-0.29f, -0.19f, 0.38f);
        static readonly Quaternion K0q = Quaternion.Euler(-18f, 22f, 40f);
        static readonly Quaternion K1q = Quaternion.Euler(-6f, 4f, -12f);
        static readonly Quaternion K2q = Quaternion.Euler(-2f, -26f, -62f);

        // Q40: empty left hand in the water – forearm in from the lower left, the back of the wrist toward the eye
        static readonly Vector3 WatchRestP = new Vector3(-0.165f, -0.16f, 0.38f);
        static readonly Quaternion WatchRestQ = Quaternion.LookRotation(new Vector3(0.78f, 0.18f, 0.6f), new Vector3(0.15f, 0.85f, -0.5f));

        // rod grip points in rod space (+Z along the rod from the butt)
        static readonly Vector3 FallbackMainGrip = new Vector3(0f, 0f, 0.22f);
        static readonly Vector3 FallbackSupport = new Vector3(-0.05f, -0.075f, 0.16f);
        static readonly Vector3 ExternalMainGrip = new Vector3(0f, 0f, 0.23f);
        static readonly Vector3 ExternalSupport = new Vector3(0.06f, -0.105f, 0.18f);

        PlayerController pc;
        Camera cam;
        ViewHand left, right;
        PlayerPhone phone;
        bool built;
        float time, strokePhase, strokeW;
        Vector2 sway;

        Material glove, pad, cuffMat, lensOff, lensOn;
        Color cuffColor;
        float cuffCheck;

        // Q40 wrist dive computer on the left wrist
        WristComputer wrist;

        // 1d pull-back probe (SphereCast from the eye)
        float pullBack;
        const float PullProbeRadius = 0.10f, PullProbeLength = 0.6f, PullStart = 0.55f;
        static readonly int PullMask = Physics.DefaultRaycastLayers & ~(1 << 4) & ~(1 << PlayerViewModelCamera.ViewModelLayer);

        // eating (E.1/E.2): the hand brings the food to the mouth, 3 bites (food) or 2 sips (drink)
        const float EatDuration = 1.9f;
        FoodDef eatFood;
        Hand eatHand;
        float eatT = 100f;
        int eatBites;
        bool Eating => eatT < EatDuration && eatFood != null;

        // fallback rod (shown while the rod is held and no rod model from the fishing module is under the camera)
        Transform fbRod, fbRodRoot;
        float castT = 10f;
        Transform extRod;
        float extRodSearch;
        // camera rig of the filming module (does it bring its own gloved hand?)
        Transform camRig;
        bool camRigHasHand = true;
        float camRigSearch;

        /// Lamp lens in camera space while the lamp is in hand and raised (PlayerFlashlight places the light there).
        public bool TryGetLampLens(out Vector3 camLocalPos, out Hand hand)
        {
            camLocalPos = Vector3.zero;
            hand = Hand.Left;
            ViewHand h = left.shown == Mode.Lamp ? left : right.shown == Mode.Lamp ? right : null;
            if (h == null || h.lens == null || h.presence < 0.6f || !gameObject.activeInHierarchy) return false;
            camLocalPos = transform.localRotation * transform.InverseTransformPoint(h.lens.position) + transform.localPosition;
            hand = h.hand;
            return true;
        }

        /// True while the 3D phone is up (raised or raising).
        public bool PhoneUp => left != null && left.shown == Mode.Phone;

        public void Init(PlayerController controller)
        {
            pc = controller;
            cam = GetComponentInParent<Camera>();
            if (!built) Build();
        }

        public void SetVisible(bool v)
        {
            if (gameObject.activeSelf != v) gameObject.SetActive(v);
            if (!built) return;
            if (!v)
            {
                ResetHand(left);
                ResetHand(right);
                if (phone != null) phone.SetVisible(false);
                if (fbRod != null) fbRod.gameObject.SetActive(false);
            }
        }

        void ResetHand(ViewHand h)
        {
            h.shown = Mode.Hidden;
            h.want = Mode.Hidden;
            h.presence = 0f;
            h.poseValid = false;
            ApplyModels(h);
        }

        // ------------------------------------------------------------------ build
        void Build()
        {
            built = true;
            cuffColor = ResolvedCuff();
            glove = ViewModelMat(LowPoly.Mat(Glove));
            pad = ViewModelMat(LowPoly.Mat(PalmPad));
            cuffMat = ViewModelMat(LowPoly.Mat(cuffColor));
            lensOff = ViewModelMat(LowPoly.Mat(LowPoly.Hex("#DCE9F0")));
            lensOn = ViewModelMat(MaterialLibrary.Emissive(new Color(1f, 0.97f, 0.88f), LowPoly.Hex("#FFD9A0"), 3f));

            left = BuildHand(Hand.Left);
            right = BuildHand(Hand.Right);
            if (cam != null) phone = new PlayerPhone(transform, cam);
            BuildFallbackRod();
            wrist = new WristComputer(left.palm);   // rides on the wrist of the left mitten (also when it wraps the lamp)
            Hands.Changed += OnHandsChanged;
            GameEvents.FoodEaten += OnFoodEaten;
        }

        /// The cuff colour of the local look (Auto = suit lightened 55 %, DiverLook.ResolvedCuff) without building a look.
        static Color ResolvedCuff()
        {
            var d = Settings.Data;
            if (d == null) return LowPoly.Hex("#3FB8C4");
            Color c = d.cuffColor.a <= 0f ? Color.Lerp(d.suitColor, Color.white, 0.55f) : d.cuffColor;
            c.a = 1f;
            return c;
        }

        ViewHand BuildHand(Hand which)
        {
            var h = new ViewHand { hand = which, side = which == Hand.Right ? 1f : -1f };
            h.root = DiverMeshes.Node(transform, which == Hand.Right ? "RightHand" : "LeftHand", Vector3.zero);
            h.root.localScale = Vector3.one * ItemScale;   // Q1: hands and held items ×0.85
            h.geo = DiverMeshes.Node(h.root, "Geo", Vector3.zero);
            if (which == Hand.Right) h.geo.localScale = new Vector3(-1f, 1f, 1f);
            // built as a LEFT hand: palm centre at the origin, fingers +Z, back of the hand +Y, thumb +X
            h.palm = DiverMeshes.Node(h.geo, "Palm", Vector3.zero);
            // L.13: ~900 triangles per mitten – smooth 14 × 24 body, palm pad, three knuckle bumps, a rounded thumb with
            // its own joint; 24-segment cuff and sleeve (the view model is close to the eye: no visible facets)
            Mesh sph = DiverMeshes.Sphere(14, 24);
            Mesh bump = DiverMeshes.Sphere(6, 10);
            VM(DiverMeshes.Part(h.palm, "Mitten", sph, glove, new Vector3(0f, 0f, 0.022f), new Vector3(0.085f, 0.044f, 0.10f), default, false));
            VM(DiverMeshes.Part(h.palm, "Pad", DiverMeshes.Sphere(8, 16), pad, new Vector3(0f, -0.017f, 0.016f), new Vector3(0.062f, 0.014f, 0.066f), default, false));
            for (int k = 0; k < 3; k++)
                VM(DiverMeshes.Part(h.palm, "Knuckle", bump, glove, new Vector3(-0.024f + k * 0.022f, 0.016f, 0.05f), new Vector3(0.024f, 0.012f, 0.02f), default, false));
            var thumbBase = DiverMeshes.Node(h.palm, "ThumbBase", new Vector3(0.033f, -0.006f, -0.014f), new Vector3(78f, -28f, 0f));
            VM(DiverMeshes.Part(thumbBase, "Thumb1", DiverMeshes.SmoothCapsule(0.0138f, 0.03f, 14, 4, 1), glove, Vector3.zero, Vector3.one, default, false));
            var thumbJoint = DiverMeshes.Node(thumbBase, "ThumbJoint", new Vector3(0f, 0.024f, 0f), new Vector3(-14f, 0f, 0f));
            VM(DiverMeshes.Part(thumbJoint, "Thumb2", DiverMeshes.SmoothCapsule(0.0128f, 0.03f, 14, 4, 1), glove, Vector3.zero, Vector3.one, default, false));
            VM(DiverMeshes.Part(h.palm, "Cuff", DiverMeshes.Taper(0.031f, 0.032f, 24), cuffMat, new Vector3(0f, 0f, -0.072f), new Vector3(1f, 0.04f, 1f), new Vector3(90f, 0f, 0f), false));
            VM(DiverMeshes.Part(h.palm, "Sleeve", DiverMeshes.Taper(0.036f, 0.031f, 24), glove, new Vector3(0f, 0f, -0.25f), new Vector3(1f, 0.18f, 1f), new Vector3(90f, 0f, 0f), false));
            h.lampRoot = DiverMeshes.Node(h.geo, "Lamp", Vector3.zero);
            h.lampRoot.gameObject.SetActive(false);
            // food lying on the open palm while eating (the eat pose turns the palm up; not mirrored: hand-root space)
            h.foodRoot = DiverMeshes.Node(h.root, "Food", new Vector3(0f, -0.04f, 0.015f), new Vector3(0f, 0f, 180f));
            h.foodRoot.gameObject.SetActive(false);
            ApplyModels(h);
            return h;
        }

        static void VM(MeshRenderer r)
        {
            r.receiveShadows = false;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        }

        /// Lamp tiers: 1 yellow with duct tape, 2 dark with an orange switch, 3 dark with an orange head and ring.
        void BuildLamp(ViewHand h, int tier)
        {
            h.lampTier = tier;
            h.lensLit = false;
            for (int i = h.lampRoot.childCount - 1; i >= 0; i--) Destroy(h.lampRoot.GetChild(i).gameObject);
            h.lens = null;
            h.lensR = null;
            if (tier <= 0) return;
            Material body = ViewModelMat(LowPoly.Mat(tier == 1 ? LampYellow : LampDark));
            Material dark = ViewModelMat(LowPoly.Mat(LampDark));
            Material accent = ViewModelMat(LowPoly.Mat(LampAccent));
            Material tape = ViewModelMat(LowPoly.Mat(Tape));
            float r = tier == 1 ? 0.021f : 0.023f;
            float headR = tier == 1 ? 0.03f : 0.034f;
            // L.13 item look: 24-segment body and head, a knurled grip ring, a rounded lens
            VM(DiverMeshes.Part(h.lampRoot, "Body", DiverMeshes.SmoothCapsule(r, 0.15f, 24, 4, 2), body, new Vector3(0f, 0f, -0.07f), Vector3.one, new Vector3(90f, 0f, 0f), false));
            VM(DiverMeshes.Part(h.lampRoot, "Head", DiverMeshes.Taper(r * 1.05f, headR, 24), tier == 3 ? accent : dark, new Vector3(0f, 0f, 0.055f), new Vector3(1f, 0.04f, 1f), new Vector3(90f, 0f, 0f), false));
            for (int k = 0; k < 3; k++)
                VM(DiverMeshes.Part(h.lampRoot, "Knurl", DiverMeshes.Torus(r * 1.02f, 0.0018f, 24, 5), dark, new Vector3(0f, 0f, -0.035f + k * 0.006f), Vector3.one, new Vector3(90f, 0f, 0f), false));
            h.lens = DiverMeshes.Node(h.lampRoot, "Lens", new Vector3(0f, 0f, 0.096f));
            var lr = DiverMeshes.Part(h.lens, "Glass", DiverMeshes.Sphere(8, 20), lensOff, Vector3.zero, new Vector3(headR * 1.75f, headR * 1.75f, 0.01f), default, false);
            VM(lr);
            h.lensR = lr;
            if (tier == 1)
            {
                VM(DiverMeshes.Part(h.lampRoot, "Tape", DiverMeshes.Taper(r * 1.12f, r * 1.12f, 12), tape, new Vector3(0f, 0f, 0.0f), new Vector3(1f, 0.026f, 1f), new Vector3(90f, 0f, 0f), false));
                VM(DiverMeshes.Part(h.lampRoot, "Tape2", DiverMeshes.Taper(r * 1.1f, r * 1.1f, 12), tape, new Vector3(0f, 0f, 0.032f), new Vector3(1f, 0.012f, 1f), new Vector3(90f, 0f, 18f), false));
            }
            else
            {
                VM(DiverMeshes.Part(h.lampRoot, "Switch", PlayerMeshes.SharedBlockCentered, accent, new Vector3(0f, r * 0.95f, 0.01f), new Vector3(0.012f, 0.009f, 0.026f), default, false));
                VM(DiverMeshes.Part(h.lampRoot, "Tail", DiverMeshes.Taper(r * 1.02f, r * 1.02f, 12), accent, new Vector3(0f, 0f, -0.075f), new Vector3(1f, 0.016f, 1f), new Vector3(90f, 0f, 0f), false));
                if (tier == 3) VM(DiverMeshes.Part(h.lampRoot, "Ring", DiverMeshes.Taper(r * 1.12f, r * 1.12f, 12), accent, new Vector3(0f, 0f, 0.045f), new Vector3(1f, 0.01f, 1f), new Vector3(90f, 0f, 0f), false));
            }
            PlayerViewModelCamera.MarkDirty();   // new parts onto the view-model layer before they are drawn
        }

        void BuildFallbackRod()
        {
            fbRodRoot = DiverMeshes.Node(transform, "RodPose", Vector3.zero);
            fbRod = DiverMeshes.Node(fbRodRoot, "FallbackRod", Vector3.zero);
            Material rod = ViewModelMat(LowPoly.Mat(RodColor)), band = ViewModelMat(LowPoly.Mat(RodBand)), reel = ViewModelMat(LowPoly.Mat(ReelColor));
            Material dark = ViewModelMat(LowPoly.Mat(LampDark));
            // thin like the HtF rod: the handle stays ~30 px thick at 1080p
            VM(DiverMeshes.Part(fbRod, "Grip", DiverMeshes.Taper(0.0098f, 0.0088f, 10), rod, Vector3.zero, new Vector3(1f, 0.32f, 1f), new Vector3(90f, 0f, 0f), false));
            VM(DiverMeshes.Part(fbRod, "Blank", DiverMeshes.Taper(0.0072f, 0.0026f, 8), rod, new Vector3(0f, 0f, 0.3f), new Vector3(1f, 1.6f, 1f), new Vector3(90f, 0f, 0f), false));
            VM(DiverMeshes.Part(fbRod, "Band", DiverMeshes.Taper(0.0082f, 0.008f, 10), band, new Vector3(0f, 0f, 0.36f), new Vector3(1f, 0.035f, 1f), new Vector3(90f, 0f, 0f), false));
            VM(DiverMeshes.Part(fbRod, "Stem", DiverMeshes.Capsule(0.004f, 0.04f, 6), reel, new Vector3(0f, -0.04f, 0.16f), Vector3.one, default, false));
            VM(DiverMeshes.Part(fbRod, "Spool", DiverMeshes.Capsule(0.022f, 0.045f, 12), reel, new Vector3(0.022f, -0.058f, 0.16f), Vector3.one, new Vector3(0f, 0f, 90f), false));
            VM(DiverMeshes.Part(fbRod, "Crank", DiverMeshes.Capsule(0.0035f, 0.035f, 6), dark, new Vector3(-0.025f, -0.06f, 0.16f), Vector3.one, new Vector3(0f, 0f, 135f), false));
            VM(DiverMeshes.Part(fbRod, "Knob", DiverMeshes.Sphere(5, 8), dark, new Vector3(-0.05f, -0.075f, 0.16f), Vector3.one * 0.016f, default, false));
            for (int i = 0; i < 3; i++)
                VM(DiverMeshes.Part(fbRod, "Guide" + i, DiverMeshes.Torus(0.008f - i * 0.0015f, 0.0012f, 8, 3), reel, new Vector3(0f, -0.012f, 0.6f + i * 0.42f), Vector3.one, new Vector3(90f, 0f, 0f), false));
            fbRodRoot.localScale = Vector3.one * ItemScale;   // Q1
            fbRodRoot.gameObject.SetActive(false);
        }

        /// Cached copy of a shared world material on the view-model queue (the shared one is never modified).
        public static Material ViewModelMat(Material shared)
        {
            if (shared == null) return null;
            if (viewModelMats.TryGetValue(shared, out var m) && m != null) return m;
            m = new Material(shared) { name = shared.name + "_VM" };
            m.renderQueue = ViewModelQueue;
            viewModelMats[shared] = m;
            return m;
        }

        static readonly Dictionary<Material, Material> viewModelMats = new Dictionary<Material, Material>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { viewModelMats.Clear(); PullBack = 0f; lampStolenUntil = -1f; }

        void OnHandsChanged()
        {
            // a fresh selection starts the equip animation right away (LateUpdate picks up the new wish)
            camRigSearch = 0f;
            extRodSearch = 0f;
            PlayerViewModelCamera.MarkDirty();   // the filming / fishing modules may build their model now
        }

        // ------------------------------------------------------------------ per frame
        void LateUpdate()
        {
            if (!built || pc == null || !pc.InGameplay) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            dt = Mathf.Min(dt, 0.1f);
            time += dt;

            UpdateCuffColor(dt);
            FindExternal(dt);
            UpdatePullBack(dt);
            UpdateEating(dt);

            bool inWater = pc.IsInWater && !pc.IsSeated;
            float swim01 = inWater ? Mathf.Clamp01(pc.MoveSpeed / 2.2f) : 0f;
            float strokeTarget = inWater && !pc.IsFainting ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(swim01 * 1.8f - 0.1f)) : 0f;
            strokeW = Mathf.Lerp(strokeW, strokeTarget, 1f - Mathf.Exp(-3f * dt));
            if (inWater) strokePhase = Mathf.Repeat(strokePhase + dt * Mathf.Lerp(0.4f, 0.95f, swim01), 1000f);
            Vector2 swayTarget = Vector2.ClampMagnitude(pc.LookDelta * 0.0018f, 0.025f);
            sway = Vector2.Lerp(sway, swayTarget, 1f - Mathf.Exp(-dt / 0.06f));

            DecideModes(dt);
            Step(left, dt);
            Step(right, dt);

            if (castT < 10f) castT += dt;
            bool rodShown = left.shown == Mode.RodMain || right.shown == Mode.RodMain;
            if (rodShown && GameInput.UsePressed && !UIState.BlocksGameplay && castT > 0.9f) castT = 0f;

            UpdateLampTier();
            Vector3 common = CommonOffset();
            PoseRod(rodShown, common);
            PosePhone(common, dt);
            Pose(left, common, inWater, dt);
            Pose(right, common, inWater, dt);
            UpdateLens();
            UpdateWrist(inWater, dt);
        }

        /// Q40: the dive computer shows on the left wrist in or on the water, whenever the left hand shows a pose it
        /// rides on (empty-hand glance / swim stroke, the lamp). Hidden with the filming rig's own hand, the phone, the
        /// rod, while eating, fainting or climbing.
        void UpdateWrist(bool inWater, float dt)
        {
            if (wrist == null) return;
            Mode m = left.shown;
            bool pose = m == Mode.Empty || m == Mode.Lamp || (m == Mode.Camera && !camRigHasHand);
            bool show = inWater && pose && left.presence > 0.35f && !pc.IsFainting && !pc.IsBusy && cam != null;
            wrist.Tick(pc, cam != null ? cam.transform : null, left.palm, show, dt);
        }

        /// 1d part 2: a SphereCast from the eye – something solid closer than 0.55 m pulls the held items back/down
        /// by (0.55 − d) × 0.8 (smoothed 0.08 s), so a hand never visually pokes into a wall, plank or hull.
        void UpdatePullBack(float dt)
        {
            float target = 0f;
            if (cam != null)
            {
                Transform ct = cam.transform;
                if (Physics.SphereCast(ct.position, PullProbeRadius, ct.forward, out RaycastHit hit, PullProbeLength, PullMask, QueryTriggerInteraction.Ignore)
                    && hit.distance < PullStart)
                    target = (PullStart - hit.distance) * 0.8f;
            }
            pullBack = Mathf.Lerp(pullBack, target, 1f - Mathf.Exp(-dt / 0.08f));
            PullBack = pullBack;
        }

        void UpdateCuffColor(float dt)
        {
            cuffCheck -= dt;
            if (cuffCheck > 0f) return;
            cuffCheck = 0.5f;
            Color cuff = ResolvedCuff();
            if ((cuff - cuffColor).maxColorComponent < 0.01f && (cuffColor - cuff).maxColorComponent < 0.01f) return;
            cuffColor = cuff;
            var m = ViewModelMat(LowPoly.Mat(cuffColor));
            SetCuff(left, m);
            SetCuff(right, m);
            cuffMat = m;
        }

        static void SetCuff(ViewHand h, Material m)
        {
            var t = h.palm.Find("Cuff");
            if (t != null) { var r = t.GetComponent<MeshRenderer>(); if (r != null) r.sharedMaterial = m; }
        }

        float flatCheck;
        bool phoneFlat;

        /// The UI's preference / fallback for a flat 2D phone (then no 3D phone in the hand). Polled twice a second.
        bool PhoneFlat(float dt)
        {
            flatCheck -= dt;
            if (flatCheck <= 0f)
            {
                flatCheck = 0.5f;
                try { phoneFlat = UIPrefs.PhoneFlat; }
                catch (System.Exception) { phoneFlat = false; }
            }
            return phoneFlat || PhoneDisplay.Texture == null;
        }

        void DecideModes(float dt)
        {
            // L.13: on a ladder both mittens grip the rungs (held items are stowed by the controller)
            if (pc.OnGenericLadder && !pc.IsFainting)
            {
                left.want = Mode.Ladder;
                right.want = Mode.Ladder;
                return;
            }
            bool phoneOpen = UIState.PhoneOpen && phone != null && !PhoneFlat(dt);
            // the selfie view shows the own bean diver instead (K.4); lying on a sleep spot (L.9) shows nothing
            bool hidden = pc.IsSeated || pc.IsBusy || pc.IsFainting || SelfieState.Active || pc.IsLyingDown;
            if (phoneOpen && !hidden)
            {
                left.want = Mode.Phone;
                right.want = Mode.PhoneTap;
                return;
            }
            if (hidden)
            {
                left.want = Mode.Hidden;
                right.want = Mode.Hidden;
                return;
            }
            if (Hands.TryGetHeld(HoldItem.FishingRod, out Hand rodHand))
            {
                (rodHand == Hand.Right ? right : left).want = Mode.RodMain;
                (rodHand == Hand.Right ? left : right).want = Mode.RodSupport;
            }
            else
            {
                left.want = ModeFor(Hands.Active(Hand.Left));
                right.want = ModeFor(Hands.Active(Hand.Right));
            }
            // the octopus has the lamp (G.3): that hand is empty until it drops it back
            if (LampStolen)
            {
                if (left.want == Mode.Lamp) left.want = Mode.Empty;
                if (right.want == Mode.Lamp) right.want = Mode.Empty;
            }
            // eating borrows one hand for ~2 s (the hotbar itself is not changed)
            if (Eating) (eatHand == Hand.Right ? right : left).want = Mode.Eat;
        }

        static Mode ModeFor(HoldItem i)
        {
            switch (i)
            {
                case HoldItem.Flashlight: return Mode.Lamp;
                case HoldItem.Camera: return Mode.Camera;
                default: return Mode.Empty;
            }
        }

        void Step(ViewHand h, float dt)
        {
            if (h.want != h.shown)
            {
                // an empty / hidden hand has nothing to put away: switch at once and raise the new item
                bool quiet = h.shown == Mode.Empty || h.shown == Mode.Hidden;
                h.presence = quiet ? 0f : Mathf.MoveTowards(h.presence, 0f, dt / 0.20f);
                if (h.presence <= 0f)
                {
                    h.shown = h.want;
                    ApplyModels(h);
                }
            }
            else h.presence = Mathf.MoveTowards(h.presence, 1f, dt / 0.25f);
        }

        void ApplyModels(ViewHand h)
        {
            bool mitten = h.shown != Mode.Hidden && !(h.shown == Mode.Camera && camRigHasHand);
            if (h.geo.gameObject.activeSelf != (h.shown != Mode.Hidden)) h.geo.gameObject.SetActive(h.shown != Mode.Hidden);
            if (h.palm.gameObject.activeSelf != mitten) h.palm.gameObject.SetActive(mitten);
            bool lamp = h.shown == Mode.Lamp;
            if (h.lampRoot.gameObject.activeSelf != lamp) h.lampRoot.gameObject.SetActive(lamp);
            bool food = h.shown == Mode.Eat && h.food != null;
            if (h.foodRoot.gameObject.activeSelf != food) h.foodRoot.gameObject.SetActive(food);
        }

        // ------------------------------------------------------------------ eating (E.1/E.2)
        /// Buffs.Apply raised FoodEaten (grill, coconut, fridge, boat cooler, kiosk): the local diver eats it in first
        /// person – a free hand (else the lamp hand; never the hand with the filming camera) brings it to the mouth.
        void OnFoodEaten(FoodDef f)
        {
            if (!built || f == null || pc == null || !pc.InGameplay) return;
            HoldItem r = Hands.Active(Hand.Right), l = Hands.Active(Hand.Left);
            if (r == HoldItem.None) eatHand = Hand.Right;
            else if (l == HoldItem.None) eatHand = Hand.Left;
            else if (r != HoldItem.Camera) eatHand = Hand.Right;
            else if (l != HoldItem.Camera) eatHand = Hand.Left;
            else return;
            ViewHand h = eatHand == Hand.Right ? right : left;
            eatFood = f;
            eatT = 0f;
            eatBites = 0;
            h.food = FoodModel(h, f);
            if (h.food != null) h.food.localScale = Vector3.one;
            if (h.shown == Mode.Eat) ApplyModels(h);
            PlayerViewModelCamera.MarkDirty();
            FXSfx.Play(FXSound.Equip, 0.45f);
        }

        void UpdateEating(float dt)
        {
            if (eatFood == null) return;
            eatT += dt;
            bool drink = eatFood.kind == FoodKind.Drink;
            int count = drink ? 2 : 3;
            // bites between 0.45 s (the hand is up) and 1.55 s
            float u = (eatT - 0.45f) / 1.1f;
            int due = Mathf.Clamp(Mathf.FloorToInt(u * count + 0.5f), 0, count);
            ViewHand h = eatHand == Hand.Right ? right : left;
            while (eatBites < due)
            {
                eatBites++;
                if (h.shown == Mode.Eat)
                {
                    FXSfx.Play(drink ? FXSound.Splash : FXSound.Click, drink ? 0.22f : 0.35f);
                    pc.Nudge(0.006f);
                    // food gets smaller with every bite (drinks stay)
                    if (!drink && h.food != null) h.food.localScale = Vector3.one * (1f - 0.22f * eatBites);
                }
            }
            if (eatT >= EatDuration + 0.6f)
            {
                eatFood = null;
                if (h.food != null) h.food.localScale = Vector3.one;
            }
        }

        /// Food models by id (built once per hand, flat colours, smooth low-poly). Pose: held above the mitten.
        Transform FoodModel(ViewHand h, FoodDef f)
        {
            if (h.foods == null) h.foods = new Dictionary<string, Transform>(4);
            if (h.foods.TryGetValue(f.id, out Transform t) && t != null) { Activate(h, t); return t; }
            t = DiverMeshes.Node(h.foodRoot, "Food_" + f.id, Vector3.zero);
            BuildFood(t, f);
            h.foods[f.id] = t;
            Activate(h, t);
            return t;
        }

        static void Activate(ViewHand h, Transform t)
        {
            for (int i = 0; i < h.foodRoot.childCount; i++)
            {
                var c = h.foodRoot.GetChild(i);
                bool on = c == t;
                if (c.gameObject.activeSelf != on) c.gameObject.SetActive(on);
            }
        }

        static Material FM(string hex) => ViewModelMat(LowPoly.Mat(Lift(LowPoly.Hex(hex))));

        static void FoodPart(Transform p, string name, Mesh m, Material mat, Vector3 pos, Vector3 scale, Vector3 euler = default)
        {
            var r = DiverMeshes.Part(p, name, m, mat, pos, scale, euler, false);
            // rounded slabs carry two submeshes (front / back + sides): same material on both
            if (m != null && m.subMeshCount > 1) r.sharedMaterials = new[] { mat, mat };
            VM(r);
        }

        static void BuildFood(Transform t, FoodDef f)
        {
            Mesh sph = DiverMeshes.Sphere(8, 16);
            switch (f.id)
            {
                case FoodDatabase.GrilledFish:
                {
                    // fish on a stick: stick up from the fist, grilled fish with stripes and a tail
                    FoodPart(t, "Stick", DiverMeshes.Capsule(0.004f, 0.16f, 8), FM("#C9A06A"), new Vector3(0f, -0.04f, 0f), Vector3.one);
                    FoodPart(t, "Fish", sph, FM("#C97A3A"), new Vector3(0f, 0.075f, 0f), new Vector3(0.032f, 0.1f, 0.05f));
                    FoodPart(t, "Belly", sph, FM("#E8B56A"), new Vector3(0f, 0.07f, 0.008f), new Vector3(0.026f, 0.08f, 0.04f));
                    for (int i = 0; i < 3; i++)
                        FoodPart(t, "Grill", DiverMeshes.Capsule(0.0035f, 0.05f, 6), FM("#5A3424"), new Vector3(0.012f, 0.05f + i * 0.022f, -0.022f), Vector3.one, new Vector3(0f, 0f, 90f));
                    FoodPart(t, "Tail", DiverMeshes.Plate("food_tail", new[] { new Vector2(0f, 0f), new Vector2(0.028f, 0.03f), new Vector2(-0.028f, 0.03f) }, 0.006f), FM("#A85E2C"), new Vector3(0f, 0.12f, 0f), Vector3.one, new Vector3(0f, 90f, 0f));
                    break;
                }
                case FoodDatabase.Coconut:
                {
                    FoodPart(t, "Nut", sph, FM("#7A5236"), new Vector3(0f, 0.02f, 0f), Vector3.one * 0.085f);
                    FoodPart(t, "Cut", DiverMeshes.Taper(0.024f, 0.024f, 14, true), FM("#F4EFE6"), new Vector3(0f, 0.058f, 0f), new Vector3(1f, 0.004f, 1f));
                    FoodPart(t, "Straw", DiverMeshes.Capsule(0.0035f, 0.09f, 8), FM("#E0607E"), new Vector3(0.006f, 0.05f, -0.004f), Vector3.one, new Vector3(-22f, 0f, -8f));
                    FoodPart(t, "Umbrella", DiverMeshes.Taper(0.022f, 0.002f, 8, false), FM("#3FB8C4"), new Vector3(-0.012f, 0.09f, 0f), new Vector3(1f, 0.016f, 1f), new Vector3(0f, 0f, 14f));
                    break;
                }
                case "galley_coffee":
                {
                    FoodPart(t, "Mug", DiverMeshes.Taper(0.026f, 0.028f, 16, true), FM("#EDE6DA"), new Vector3(0f, 0f, 0f), new Vector3(1f, 0.07f, 1f));
                    FoodPart(t, "Coffee", DiverMeshes.Taper(0.024f, 0.024f, 16, true), FM("#5A3424"), new Vector3(0f, 0.062f, 0f), new Vector3(1f, 0.004f, 1f));
                    FoodPart(t, "Handle", DiverMeshes.Torus(0.016f, 0.005f, 12, 5), FM("#EDE6DA"), new Vector3(0.03f, 0.035f, 0f), Vector3.one, new Vector3(90f, 0f, 0f));
                    FoodPart(t, "Band", DiverMeshes.Taper(0.0285f, 0.0285f, 16, false), FM("#3FB8C4"), new Vector3(0f, 0.045f, 0f), new Vector3(1f, 0.008f, 1f));
                    break;
                }
                case "fish_sandwich":
                {
                    FoodPart(t, "BunBottom", sph, FM("#D9A066"), new Vector3(0f, 0.01f, 0f), new Vector3(0.1f, 0.03f, 0.06f));
                    FoodPart(t, "Lettuce", sph, FM("#7FA243"), new Vector3(0f, 0.025f, 0f), new Vector3(0.105f, 0.012f, 0.065f));
                    FoodPart(t, "Fish", sph, FM("#E8B56A"), new Vector3(0f, 0.035f, 0f), new Vector3(0.11f, 0.018f, 0.05f));
                    FoodPart(t, "BunTop", sph, FM("#D9A066"), new Vector3(0f, 0.052f, 0f), new Vector3(0.1f, 0.04f, 0.06f));
                    break;
                }
                case "krill_bar":
                {
                    FoodPart(t, "Bar", DiverMeshes.RoundedSlab("food_bar", 0.03f, 0.1f, 0.016f, 0.006f, 3), FM("#E0607E"), new Vector3(0f, 0.04f, 0f), Vector3.one);
                    FoodPart(t, "Wrapper", DiverMeshes.RoundedSlab("food_wrap", 0.033f, 0.05f, 0.019f, 0.006f, 3), FM("#F2C94C"), new Vector3(0f, 0.01f, 0f), Vector3.one);
                    break;
                }
                case "seaweed_chips":
                case "shark_gummies":
                {
                    bool chips = f.id == "seaweed_chips";
                    FoodPart(t, "Bag", sph, FM(chips ? "#6E9445" : "#3FB8C4"), new Vector3(0f, 0.04f, 0f), new Vector3(0.07f, 0.1f, 0.03f));
                    FoodPart(t, "Label", sph, FM("#F4EFE6"), new Vector3(0f, 0.045f, -0.013f), new Vector3(0.04f, 0.035f, 0.008f));
                    FoodPart(t, "Snack", sph, FM(chips ? "#4E6F2E" : "#E0607E"), new Vector3(0.006f, 0.098f, 0f), new Vector3(0.022f, 0.012f, 0.016f), new Vector3(0f, 0f, 20f));
                    break;
                }
                case "coconut_curry":
                case "sea_grape_salad":
                {
                    bool curry = f.id == "coconut_curry";
                    FoodPart(t, "Bowl", DiverMeshes.Taper(0.026f, 0.045f, 18, true), FM("#C9874A"), Vector3.zero, new Vector3(1f, 0.035f, 1f));
                    FoodPart(t, "Content", sph, FM(curry ? "#E8A23A" : "#7FA243"), new Vector3(0f, 0.034f, 0f), new Vector3(0.08f, 0.018f, 0.08f));
                    FoodPart(t, "Spoon", DiverMeshes.Capsule(0.003f, 0.08f, 6), FM("#B8BEC6"), new Vector3(0.015f, 0.03f, 0f), Vector3.one, new Vector3(0f, 0f, -30f));
                    break;
                }
                default:
                {
                    if (f.kind == FoodKind.Drink)
                    {
                        // can or bottle: glow soda glows a little, ginger fizz is a yellow bottle
                        bool glow = f.id == "glow_soda";
                        Material body = glow ? ViewModelMat(MaterialLibrary.Emissive(LowPoly.Hex("#7FE0C8"), LowPoly.Hex("#5FD6B8"), 0.6f)) : FM("#F2C94C");
                        if (glow)
                        {
                            FoodPart(t, "Can", DiverMeshes.Taper(0.02f, 0.02f, 16, true), body, Vector3.zero, new Vector3(1f, 0.09f, 1f));
                            FoodPart(t, "Lid", DiverMeshes.Taper(0.02f, 0.016f, 16, true), FM("#B8BEC6"), new Vector3(0f, 0.09f, 0f), new Vector3(1f, 0.006f, 1f));
                        }
                        else
                        {
                            FoodPart(t, "Bottle", DiverMeshes.Capsule(0.019f, 0.1f, 16), body, Vector3.zero, Vector3.one);
                            FoodPart(t, "Neck", DiverMeshes.Capsule(0.008f, 0.04f, 10), body, new Vector3(0f, 0.09f, 0f), Vector3.one);
                            FoodPart(t, "Cap", DiverMeshes.Taper(0.009f, 0.009f, 10, true), FM("#E0607E"), new Vector3(0f, 0.125f, 0f), new Vector3(1f, 0.008f, 1f));
                        }
                    }
                    else
                        FoodPart(t, "Snack", DiverMeshes.RoundedSlab("food_generic", 0.04f, 0.08f, 0.02f, 0.008f, 3), FM("#E8B56A"), new Vector3(0f, 0.03f, 0f), Vector3.one);
                    break;
                }
            }
        }

        void UpdateLampTier()
        {
            GearStats g = pc.Gear;
            int tier = g.hasLamp ? Mathf.Clamp(Mathf.RoundToInt(g.lampIntensity), 1, 3) : 1;
            if (left.lampTier != tier) BuildLamp(left, tier);
            if (right.lampTier != tier) BuildLamp(right, tier);
        }

        void UpdateLens()
        {
            bool lit = pc.FlashlightOn;
            SetLens(left, lit && left.shown == Mode.Lamp);
            SetLens(right, lit && right.shown == Mode.Lamp);
        }

        void SetLens(ViewHand h, bool lit)
        {
            if (h.lensR == null || h.lensLit == lit) return;
            h.lensR.sharedMaterial = lit ? lensOn : lensOff;
            h.lensLit = lit;
        }

        // ------------------------------------------------------------------ external models (filming camera, fishing rod)
        /// Looks for the filming module's camera rig and the fishing module's rod among the camera's direct children.
        /// Searches only when the children changed (or rarely as a safety net): reading names allocates.
        void FindExternal(float dt)
        {
            Transform camT = cam != null ? cam.transform : transform.parent;
            if (camT == null) return;
            int count = camT.childCount;
            bool changed = count != lastChildCount;
            lastChildCount = count;

            camRigSearch -= dt;
            if (camRig == null && (changed || camRigSearch <= 0f))
            {
                camRigSearch = 2f;
                Transform found = null;
                for (int i = 0; i < count; i++)
                {
                    var c = camT.GetChild(i);
                    if (c == transform) continue;
                    if (c.name.StartsWith("HandCamera", System.StringComparison.Ordinal)) { found = c; break; }
                }
                camRig = found;
                bool has = found == null || HasDescendant(found, "Hand", 3);
                if (has != camRigHasHand)
                {
                    camRigHasHand = has;
                    ApplyModels(left);
                    ApplyModels(right);
                }
            }

            // the fishing module's rod: a camera child or grandchild named "FishingRod…" (RodViewRoot/FishingRod)
            extRodSearch -= dt;
            if (extRod == null && Hands.IsHeld(HoldItem.FishingRod) && (changed || extRodSearch <= 0f))
            {
                extRodSearch = 1f;
                for (int i = 0; i < count && extRod == null; i++)
                {
                    var c = camT.GetChild(i);
                    if (c == transform || c == camRig) continue;
                    if (IsRodName(c.name)) { extRod = c; break; }
                    for (int k = 0; k < c.childCount; k++)
                    {
                        var g = c.GetChild(k);
                        if (IsRodName(g.name)) { extRod = g; break; }
                    }
                }
            }
        }

        static bool IsRodName(string n) =>
            n.StartsWith("FishingRod", System.StringComparison.Ordinal) || n.StartsWith("HandRod", System.StringComparison.Ordinal);

        int lastChildCount = -1;

        /// The fishing module's rod when it is shown right now (null → our fallback rod is used).
        Transform ActiveExtRod => extRod != null && extRod.gameObject.activeInHierarchy ? extRod : null;

        static bool HasDescendant(Transform t, string name, int depth)
        {
            for (int i = 0; i < t.childCount; i++)
            {
                var c = t.GetChild(i);
                if (c.name == name) return true;
                if (depth > 0 && HasDescendant(c, name, depth - 1)) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ poses
        /// Idle sway + walk bob + look lag shared by everything in the hands.
        Vector3 CommonOffset()
        {
            float w = 2f * Mathf.PI * 0.35f;
            Vector3 o = new Vector3(Mathf.Sin(time * w) * 0.004f, Mathf.Sin(time * w * 1.3f + 1f) * 0.003f, 0f);
            // walk bob follows the head-bob toggle (Q99)
            if (pc.Mode == PlayerMoveMode.Land && pc.Grounded && !pc.IsSeated && (Settings.Data == null || Settings.Data.headBob))
            {
                float ph = pc.BobPhase, b = pc.BobAmount;
                o.x += Mathf.Sin(ph) * 0.006f * b;
                o.y -= Mathf.Abs(Mathf.Sin(ph)) * 0.010f * b;
            }
            o.x -= sway.x;
            o.y -= sway.y;
            o.y -= pc.FaintAmount * 0.3f;
            // 1d: something solid right in front of the eye pulls the items back and a little down
            o.z -= pullBack;
            o.y -= pullBack * 0.45f;
            return o;
        }

        void PoseRod(bool rodShown, Vector3 common)
        {
            bool useFallback = rodShown && ActiveExtRod == null;
            if (fbRodRoot.gameObject.activeSelf != useFallback) fbRodRoot.gameObject.SetActive(useFallback);
            if (!useFallback) return;
            ViewHand main = left.shown == Mode.RodMain ? left : right;
            float pres = main.presence;
            float drop = (1f - pres) * (1f - pres);
            Vector3 axis = (RodTopPoint - RodGrip).normalized;
            Quaternion baseRot = Quaternion.LookRotation(axis, new Vector3(0f, 0f, -1f));
            Vector3 grip = RodGrip + common + new Vector3(0f, -0.3f * drop, 0f);
            // cast: 0.35 s wind-up back over the shoulder, 0.25 s whip forward, 0.4 s settle
            float ang = 0f;
            if (castT < 0.35f) ang = -50f * Mathf.Sin(castT / 0.35f * Mathf.PI * 0.5f);
            else if (castT < 0.6f) { float u = (castT - 0.35f) / 0.25f; ang = Mathf.Lerp(-50f, 25f, u * u); }
            else if (castT < 1.0f) { float u = (castT - 0.6f) / 0.4f; ang = Mathf.Lerp(25f, 0f, 1f - (1f - u) * (1f - u)); }
            Quaternion cast = Quaternion.Euler(ang + 25f * drop, 0f, 0f);
            Quaternion rot = cast * baseRot;
            fbRodRoot.localRotation = rot;
            fbRodRoot.localPosition = grip - rot * (FallbackMainGrip * fbRodRoot.localScale.x);
        }

        void PosePhone(Vector3 common, float dt)
        {
            if (phone == null) return;
            bool show = left.shown == Mode.Phone;
            phone.SetVisible(show);
            if (!show) return;
            phone.RefreshScreen();
            float fov = cam != null ? cam.fieldOfView : 70f;
            float tanV = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            float tanH = tanV * (cam != null ? cam.aspect : 16f / 9f);
            // the raised phone fills ~70 % of the screen height (screen area ~58 %) so the 540 x 1080 UI stays readable
            float d = PlayerPhone.Height / (2f * tanV * 0.70f);
            Vector3 c = new Vector3(-0.03f * 2f * tanH * d, -0.02f * 2f * tanV * d, d);
            float w = left.presence;
            float drop = (1f - w) * (1f - w);
            c += common * 0.35f;
            c.y -= 0.22f * drop;
            phone.Root.localPosition = c;
            phone.Root.localRotation = Quaternion.Euler(-3f + 40f * drop, 6f, 2f);
            phone.SetRaised(w > 0.95f && UIState.PhoneOpen);
        }

        void Pose(ViewHand h, Vector3 common, bool inWater, float dt)
        {
            if (h.shown == Mode.Hidden) { h.poseValid = false; return; }
            float drop = (1f - h.presence) * (1f - h.presence);
            Vector3 p;
            Quaternion q;
            bool attached = false;   // follows a moving model exactly (no smoothing)
            h.palm.localPosition = Vector3.zero;
            h.palm.localRotation = Quaternion.identity;

            switch (h.shown)
            {
                case Mode.Lamp:
                {
                    p = (h.hand == Hand.Right ? RightItemPos : LeftItemPos) + common;
                    q = Quaternion.Euler(h.hand == Hand.Right ? RightItemRot : LeftItemRot);
                    // the hand wraps the lamp body from the outer side, palm facing the lamp
                    h.palm.localPosition = new Vector3(-0.03f, -0.012f, -0.04f);
                    h.palm.localRotation = Quaternion.Euler(0f, 0f, 72f);
                    break;
                }
                case Mode.Camera:
                {
                    p = (h.hand == Hand.Right ? RightItemPos : LeftItemPos) + common + new Vector3(0f, -0.045f, -0.01f);
                    q = Quaternion.Euler(h.hand == Hand.Right ? RightItemRot : LeftItemRot) * Quaternion.Euler(0f, 0f, 180f);
                    break;
                }
                case Mode.RodMain:
                case Mode.RodSupport:
                {
                    Transform er = ActiveExtRod;
                    Transform rod = er != null ? er : fbRodRoot;
                    bool ext = er != null;
                    Vector3 local = h.shown == Mode.RodMain ? (ext ? ExternalMainGrip : FallbackMainGrip) : (ext ? ExternalSupport : FallbackSupport);
                    p = transform.InverseTransformPoint(rod.TransformPoint(local));
                    Vector3 shoulder = new Vector3(ShoulderR.x * h.side, ShoulderR.y, ShoulderR.z);
                    Vector3 fwd = p - shoulder;
                    if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
                    q = Quaternion.LookRotation(fwd.normalized, new Vector3(-0.45f * h.side, 1f, -0.2f));
                    attached = true;
                    break;
                }
                case Mode.Phone:
                {
                    Transform pr = phone != null ? phone.Root : null;
                    if (pr == null) goto default;
                    // behind the phone, holding its lower half (closer, the mitten poked through the screen)
                    p = pr.localPosition + pr.localRotation * new Vector3(-0.026f, -0.072f, 0.036f);
                    q = pr.localRotation * Quaternion.LookRotation(new Vector3(0.35f, 1f, 0f), new Vector3(0f, 0f, 1f));
                    attached = true;
                    break;
                }
                case Mode.PhoneTap:
                {
                    Transform pr = phone != null ? phone.Root : null;
                    if (pr == null) goto default;
                    float hover = Mathf.Sin(time * 1.7f) * 0.004f;
                    // mostly below the phone: only the fingertips reach up toward its lower right corner
                    // (the phone is only ~16 cm from the eye: further in, this hand covered a third of the screen)
                    p = pr.localPosition + pr.localRotation * new Vector3(0.105f, -0.15f + hover, 0.01f);
                    q = pr.localRotation * Quaternion.LookRotation(new Vector3(-0.55f, 0.8f, 0.25f), new Vector3(0f, 0f, -1f));
                    attached = true;
                    break;
                }
                case Mode.Ladder:
                {
                    PoseLadderHand(h, out p, out q);
                    attached = true;
                    // a short fade-in from below the screen
                    p.y -= 0.25f * drop;
                    break;
                }
                case Mode.Eat:
                {
                    // palm up in front of the mouth, the food on it; a bite = a little push toward the face
                    bool drink = eatFood != null && eatFood.kind == FoodKind.Drink;
                    int count = drink ? 2 : 3;
                    float u = Mathf.Clamp01((eatT - 0.45f) / 1.1f) * count;
                    float b = eatFood != null && eatT > 0.35f && eatT < 1.6f ? Mathf.Sin((u - Mathf.Floor(u)) * Mathf.PI) : 0f;
                    p = new Vector3(h.side * 0.05f, -0.135f, 0.27f) + common * 0.5f;
                    if (drink)
                    {
                        p += new Vector3(-h.side * 0.012f, 0.025f * b, -0.025f * b);
                        q = Quaternion.Euler(-30f - 24f * b, -h.side * 10f, 180f);
                    }
                    else
                    {
                        p += new Vector3(-h.side * 0.008f, 0.016f * b, -0.05f * b);
                        q = Quaternion.Euler(-14f - 12f * b, -h.side * 14f, 180f);
                    }
                    break;
                }
                default:
                {
                    // empty hand: out of view on land, breaststroke while swimming (left-hand poses, mirrored);
                    // in the water the empty LEFT hand rests in a "glance at the dive computer" pose (Q40)
                    bool glance = inWater && h.hand == Hand.Left && wrist != null;
                    Vector3 restP = glance ? WatchRestP : inWater ? new Vector3(-0.18f, -0.31f, 0.33f) : new Vector3(-0.2f, -0.46f, 0.28f);
                    Quaternion restQ = glance ? WatchRestQ : Quaternion.Euler(-4f, 12f, -28f);
                    if (glance) restP.y += Mathf.Sin(time * 0.9f) * 0.003f;
                    SampleStroke(strokePhase, out Vector3 sp, out Quaternion sq);
                    float w = strokeW;
                    p = Vector3.Lerp(restP, sp, w);
                    q = Quaternion.Slerp(restQ, sq, w);
                    if (inWater)
                    {
                        float scull = 1f - Mathf.Clamp01(w / 0.35f);
                        p.x += Mathf.Sin(time * 2.1f) * 0.01f * scull;
                        p.z += Mathf.Cos(time * 2.1f) * 0.006f * scull;
                    }
                    if (h.hand == Hand.Right)
                    {
                        p.x = -p.x;
                        q = new Quaternion(q.x, -q.y, -q.z, q.w);
                    }
                    p += common;
                    break;
                }
            }

            if (!attached)
            {
                p.y -= 0.25f * drop;
                q = q * Quaternion.Euler(30f * drop, 0f, 0f);
            }
            if (!h.poseValid || attached)
            {
                h.pos = p;
                h.rot = q;
                h.poseValid = true;
            }
            else
            {
                float k = 1f - Mathf.Exp(-dt / 0.06f);
                h.pos = Vector3.Lerp(h.pos, p, k);
                h.rot = Quaternion.Slerp(h.rot, q, k);
            }
            h.root.localPosition = h.pos;
            h.root.localRotation = h.rot;
        }

        /// L.13 first-person ladder hands: each mitten holds a rung anchored in the WORLD (so it stays on the ladder while
        /// the player looks around), the hands move one at a time – every two rungs a hand lets go and reaches up in a
        /// short arc (≈ 0.22 s reach + pull). During the mantle both hands press on the platform edge.
        void PoseLadderHand(ViewHand h, out Vector3 p, out Quaternion q)
        {
            LadderInfo l = pc.CurrentLadder;
            Vector3 o = l.outward;
            o.y = 0f;
            if (o.sqrMagnitude < 1e-6f) o = Vector3.forward;
            o.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, -o);
            float sp = Mathf.Max(0.1f, l.rungSpacing);
            Vector3 rail = new Vector3(l.bottom.x, 0f, l.bottom.z);
            Vector3 eye = cam != null ? cam.transform.position : transform.position;
            float sideSign = h.hand == Hand.Right ? 1f : -1f;
            Vector3 world;
            if (pc.LadderMantling)
            {
                world = rail + right * (0.2f * sideSign) - o * 0.12f;
                world.y = l.top.y + 0.03f;
            }
            else
            {
                float H = pc.LadderRootY - l.bottom.y;
                float g = (H + 1.25f + (h.hand == Hand.Right ? sp : 0f)) / (2f * sp);
                float frac = g - Mathf.Floor(g);
                float yRel, pullIn = 0f;
                // grip band +0.17 … −0.20 m around the eye (0.34 m in front of it): both ends stay within ~31° of the
                // view axis, inside the ±35° half FOV. A hand holds 62 % of its cycle (the rung sinks exactly as fast as
                // the body rises – it stays on the rung) and reaches up for the rest; the two hands are half a cycle
                // apart, so one always holds. (At +0.25 the top mitten left the frame – AutoShot r2_21; at −0.27 the
                // bottom one did – playtest step 33.)
                const float Top = 0.17f, Hold = 0.62f;
                float drop = 2f * sp;
                if (frac <= Hold) yRel = Top - frac * drop;               // holding: the rung sinks as the body rises
                else
                {
                    float u = (frac - Hold) / (1f - Hold);                // reaching up to the next rung
                    float e = u * u * (3f - 2f * u);
                    yRel = Mathf.Lerp(Top - Hold * drop, Top, e);
                    pullIn = Mathf.Sin(u * Mathf.PI) * 0.08f;
                }
                world = rail + right * (0.15f * sideSign) + o * (0.06f + pullIn);
                world.y = eye.y + yRel;
            }
            // palm toward the wall, fingers up over the rung, back of the hand toward the climber
            Vector3 fingers = (Vector3.up * 0.85f - o * 0.5f).normalized;
            Vector3 back = (o * 0.85f + Vector3.up * 0.45f).normalized;
            Quaternion qw = Quaternion.LookRotation(fingers, back);
            p = transform.InverseTransformPoint(world);
            q = Quaternion.Inverse(transform.rotation) * qw;
        }

        static void SampleStroke(float phase, out Vector3 p, out Quaternion q)
        {
            float f = phase - Mathf.Floor(phase);
            if (f < 0.3f)
            {
                float a = Mathf.SmoothStep(0f, 1f, f / 0.3f);
                p = Vector3.Lerp(K0p, K1p, a);
                q = Quaternion.Slerp(K0q, K1q, a);
            }
            else if (f < 0.7f)
            {
                float a = Mathf.SmoothStep(0f, 1f, (f - 0.3f) / 0.4f);
                p = Vector3.Lerp(K1p, K2p, a);
                q = Quaternion.Slerp(K1q, K2q, a);
            }
            else
            {
                float a = Mathf.SmoothStep(0f, 1f, (f - 0.7f) / 0.3f);
                p = Vector3.Lerp(K2p, K0p, a);
                q = Quaternion.Slerp(K2q, K0q, a);
            }
        }

        void OnDestroy()
        {
            if (built)
            {
                Hands.Changed -= OnHandsChanged;
                GameEvents.FoodEaten -= OnFoodEaten;
            }
            if (phone != null) phone.Destroy();
            PullBack = 0f;
        }
    }
}
