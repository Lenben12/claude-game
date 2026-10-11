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
    [DefaultExecutionOrder(60)]
    public class PlayerHands : MonoBehaviour
    {
        /// Same render queue as the filming module's camera model (FilmMaterials.ViewModelQueue): opaque shader,
        /// drawn in the transparent pass before the ocean, so the view model stays out of the SSAO prepass.
        public const int ViewModelQueue = 2600;

        enum Mode { Hidden, Empty, Lamp, Camera, RodMain, RodSupport, Phone, PhoneTap }

        sealed class ViewHand
        {
            public Hand hand;
            public float side;                // -1 left, +1 right
            public Transform root, geo, palm, lampRoot, lens;
            public Renderer lensR;
            public int lampTier = -1;
            public bool lensLit;
            public Mode shown = Mode.Hidden, want = Mode.Hidden;
            public float presence;
            public Vector3 pos;
            public Quaternion rot = Quaternion.identity;
            public bool poseValid;
        }

        // palette (art bible §1.13)
        static readonly Color Glove = LowPoly.Hex("#343849");
        static readonly Color PalmPad = LowPoly.Hex("#2A2D3A");
        static readonly Color LampYellow = LowPoly.Hex("#F2C94C");
        static readonly Color LampDark = LowPoly.Hex("#2E3140");
        static readonly Color LampAccent = LowPoly.Hex("#EE7F2D");
        static readonly Color Tape = LowPoly.Hex("#A9AEB4");
        static readonly Color RodColor = LowPoly.Hex("#2E2A40");
        static readonly Color RodBand = LowPoly.Hex("#3D7BC4");
        static readonly Color ReelColor = LowPoly.Hex("#8E949C");

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
            cuffColor = Settings.Look.cuff;
            glove = ViewModelMat(LowPoly.Mat(Glove));
            pad = ViewModelMat(LowPoly.Mat(PalmPad));
            cuffMat = ViewModelMat(LowPoly.Mat(cuffColor));
            lensOff = ViewModelMat(LowPoly.Mat(LowPoly.Hex("#DCE9F0")));
            lensOn = ViewModelMat(MaterialLibrary.Emissive(new Color(1f, 0.97f, 0.88f), LowPoly.Hex("#FFD9A0"), 3f));

            left = BuildHand(Hand.Left);
            right = BuildHand(Hand.Right);
            if (cam != null) phone = new PlayerPhone(transform, cam);
            BuildFallbackRod();
            Hands.Changed += OnHandsChanged;
        }

        ViewHand BuildHand(Hand which)
        {
            var h = new ViewHand { hand = which, side = which == Hand.Right ? 1f : -1f };
            h.root = DiverMeshes.Node(transform, which == Hand.Right ? "RightHand" : "LeftHand", Vector3.zero);
            h.geo = DiverMeshes.Node(h.root, "Geo", Vector3.zero);
            if (which == Hand.Right) h.geo.localScale = new Vector3(-1f, 1f, 1f);
            // built as a LEFT hand: palm centre at the origin, fingers +Z, back of the hand +Y, thumb +X
            h.palm = DiverMeshes.Node(h.geo, "Palm", Vector3.zero);
            Mesh sph = DiverMeshes.Sphere(6, 12);
            VM(DiverMeshes.Part(h.palm, "Mitten", sph, glove, new Vector3(0f, 0f, 0.022f), new Vector3(0.085f, 0.044f, 0.10f), default, false));
            VM(DiverMeshes.Part(h.palm, "Pad", sph, pad, new Vector3(0f, -0.017f, 0.016f), new Vector3(0.062f, 0.014f, 0.066f), default, false));
            VM(DiverMeshes.Part(h.palm, "Thumb", DiverMeshes.Capsule(0.0135f, 0.052f, 10), glove, new Vector3(0.033f, -0.006f, -0.014f), Vector3.one, new Vector3(78f, -28f, 0f), false));
            VM(DiverMeshes.Part(h.palm, "Cuff", DiverMeshes.Taper(0.031f, 0.032f, 12), cuffMat, new Vector3(0f, 0f, -0.072f), new Vector3(1f, 0.04f, 1f), new Vector3(90f, 0f, 0f), false));
            VM(DiverMeshes.Part(h.palm, "Sleeve", DiverMeshes.Taper(0.036f, 0.031f, 12), glove, new Vector3(0f, 0f, -0.25f), new Vector3(1f, 0.18f, 1f), new Vector3(90f, 0f, 0f), false));
            h.lampRoot = DiverMeshes.Node(h.geo, "Lamp", Vector3.zero);
            h.lampRoot.gameObject.SetActive(false);
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
            VM(DiverMeshes.Part(h.lampRoot, "Body", DiverMeshes.Capsule(r, 0.15f, 12), body, new Vector3(0f, 0f, -0.07f), Vector3.one, new Vector3(90f, 0f, 0f), false));
            VM(DiverMeshes.Part(h.lampRoot, "Head", DiverMeshes.Taper(r * 1.05f, headR, 12), tier == 3 ? accent : dark, new Vector3(0f, 0f, 0.055f), new Vector3(1f, 0.04f, 1f), new Vector3(90f, 0f, 0f), false));
            h.lens = DiverMeshes.Node(h.lampRoot, "Lens", new Vector3(0f, 0f, 0.096f));
            var lr = DiverMeshes.Part(h.lens, "Glass", DiverMeshes.Sphere(5, 12), lensOff, Vector3.zero, new Vector3(headR * 1.75f, headR * 1.75f, 0.01f), default, false);
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
        static void ResetStatics() { viewModelMats.Clear(); }

        void OnHandsChanged()
        {
            // a fresh selection starts the equip animation right away (LateUpdate picks up the new wish)
            camRigSearch = 0f;
            extRodSearch = 0f;
        }

        // ------------------------------------------------------------------ per frame
        void LateUpdate()
        {
            if (!built || pc == null || !pc.InGameplay) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            dt = Mathf.Min(dt, 0.1f);
            time += dt;

            UpdateCuffColor();
            FindExternal(dt);

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
        }

        void UpdateCuffColor()
        {
            Color c = Settings.Data.suitColor;
            Color cuff = Color.Lerp(c, Color.white, 0.6f);
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
            bool phoneOpen = UIState.PhoneOpen && phone != null && !PhoneFlat(dt);
            bool hidden = pc.IsSeated || pc.IsBusy || pc.IsFainting;
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
                return;
            }
            left.want = ModeFor(Hands.Active(Hand.Left));
            right.want = ModeFor(Hands.Active(Hand.Right));
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
            if (pc.Mode == PlayerMoveMode.Land && pc.Grounded && !pc.IsSeated)
            {
                float ph = pc.BobPhase, b = pc.BobAmount;
                o.x += Mathf.Sin(ph) * 0.006f * b;
                o.y -= Mathf.Abs(Mathf.Sin(ph)) * 0.010f * b;
            }
            o.x -= sway.x;
            o.y -= sway.y;
            o.y -= pc.FaintAmount * 0.3f;
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
            fbRodRoot.localPosition = grip - rot * FallbackMainGrip;
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
                default:
                {
                    // empty hand: out of view on land, breaststroke while swimming (left-hand poses, mirrored)
                    Vector3 restP = inWater ? new Vector3(-0.18f, -0.31f, 0.33f) : new Vector3(-0.2f, -0.46f, 0.28f);
                    Quaternion restQ = Quaternion.Euler(-4f, 12f, -28f);
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
            if (built) Hands.Changed -= OnHandsChanged;
            if (phone != null) phone.Destroy();
        }
    }
}
