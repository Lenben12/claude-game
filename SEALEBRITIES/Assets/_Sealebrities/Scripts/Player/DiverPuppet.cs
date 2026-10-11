using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Procedural animation of a bean diver built by DiverBuilder (menu crew, intro, co-op avatars).
    /// Idle breathing, walk cycle, swim pose with a flutter kick, sitting with dangling legs, held items
    /// (camera / lamp with a light cone / two-handed rod), googly pupils and a talking mouth. No per-frame allocations.
    public sealed class DiverPuppet : MonoBehaviour, IDiverPuppet
    {
        // ------------------------------------------------------------------ rig (filled by DiverBuilder)
        internal Transform pivot, head, maskPivot, mouth, hatT, pupilL, pupilR;
        internal Transform shoulderL, shoulderR, elbowL, elbowR, handL, handR, hipL, hipR, kneeL, kneeR;
        internal DiverLook look;
        internal int hat;

        Quaternion pupilBaseL, pupilBaseR;
        Vector3 pivotBase;

        // ------------------------------------------------------------------ pose input
        bool swimming, seated, lightOn;
        float speed;
        HoldItem rightItem, leftItem;
        float talkTarget;

        /// Extra menu-crew props (not part of IDiverPuppet).
        public bool Ukulele;
        /// 0..1 slow leg swing while seated (dock edge).
        public float LegDangle = 1f;
        /// Seconds offset so a crew does not move in sync.
        public float Phase;
        /// Optional head turn (degrees, + = right) e.g. for the crew looking at each other.
        public float HeadYaw;

        // ------------------------------------------------------------------ smoothed state
        float swimW, seatW, walkW, talk, maskDown, walkPhase, kickPhase, t;
        Quaternion qShL, qShR, qElL, qElR, qHipL, qHipR, qKnL, qKnR, qPivot;
        bool poseInit;

        // ------------------------------------------------------------------ items
        Transform camModel, lampModel, rodModel, ukeModel, lampTip;
        Light lampLight;
        MeshRenderer beam;
        static Texture2D beamTex;

        public Transform Head => head;
        /// Point above the head (2.15 m) for a nameplate / speaker icon (co-op).
        public Transform NameAnchor { get; private set; }
        /// Smoothed talking loudness 0..1 (for a speaker icon next to the name).
        public float Talking01 => talk;

        internal void InitRig()
        {
            NameAnchor = DiverMeshes.Node(transform, "NameAnchor", new Vector3(0f, 2.15f, 0f));
            pupilBaseL = pupilL != null ? pupilL.localRotation : Quaternion.identity;
            pupilBaseR = pupilR != null ? pupilR.localRotation : Quaternion.identity;
            pivotBase = pivot.localPosition;
            seated = look.sitting;
            seatW = seated ? 1f : 0f;
            Phase = Mathf.Abs(look.suit.r * 13.7f + look.suit.g * 7.1f + look.suit.b * 3.3f) % 6.28f;
        }

        public void SetPose(bool swimming, bool seated, float speed, HoldItem right, HoldItem left, bool lightOn)
        {
            this.swimming = swimming;
            this.seated = seated;
            this.speed = Mathf.Max(0f, speed);
            rightItem = right;
            leftItem = left;
            this.lightOn = lightOn;
        }

        public void SetTalking(float loudness01) => talkTarget = Mathf.Clamp01(loudness01);

        // ------------------------------------------------------------------ animation
        void LateUpdate()
        {
            if (pivot == null) return;
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            if (dt <= 0f) return;
            t += dt;
            float time = t + Phase;

            float k = 1f - Mathf.Exp(-6f * dt);
            swimW = Mathf.Lerp(swimW, swimming && !seated ? 1f : 0f, 1f - Mathf.Exp(-4f * dt));
            seatW = Mathf.Lerp(seatW, seated ? 1f : 0f, k);
            float walkTarget = !swimming && !seated ? Mathf.Clamp01(speed / 1.6f) : 0f;
            walkW = Mathf.Lerp(walkW, walkTarget, 1f - Mathf.Exp(-8f * dt));
            talk = Mathf.Lerp(talk, talkTarget, 1f - Mathf.Exp(-20f * dt));
            bool rod = rightItem == HoldItem.FishingRod || leftItem == HoldItem.FishingRod;
            maskDown = Mathf.MoveTowards(maskDown, swimming || hat != 0 ? 1f : 0f, dt * 2.5f);

            walkPhase = Mathf.Repeat(walkPhase + dt * Mathf.Max(0.2f, speed) * (Mathf.PI * 2f / 1.5f), Mathf.PI * 2f);
            kickPhase = Mathf.Repeat(kickPhase + dt * (3.2f + Mathf.Min(speed, 3f) * 1.6f), Mathf.PI * 2f);

            // ---- body
            float breathe = Mathf.Sin(time * 1.6f) * 0.006f;
            float bob = Mathf.Abs(Mathf.Sin(walkPhase)) * 0.035f * walkW;
            pivot.localPosition = pivotBase + new Vector3(0f, breathe + bob - 0.3f * seatW, 0f);
            float roll = Mathf.Sin(walkPhase) * 3f * walkW + Mathf.Sin(time * 0.7f) * 1.2f * swimW;
            Quaternion pivotTarget = Quaternion.Euler(80f * swimW - 4f * seatW + Mathf.Sin(time * 0.9f) * 2f * swimW, 0f, roll);
            qPivot = poseInit ? Quaternion.Slerp(qPivot, pivotTarget, 1f - Mathf.Exp(-10f * dt)) : pivotTarget;
            pivot.localRotation = qPivot;

            // ---- legs
            float swing = Mathf.Sin(walkPhase) * 30f * walkW;
            float kick = Mathf.Sin(kickPhase) * 20f * swimW;
            float dangle = Mathf.Sin(time * 1.3f) * 12f * LegDangle * seatW;
            float aHipL = swing + kick - 85f * seatW + dangle * 0.3f;
            float aHipR = -swing - kick - 85f * seatW - dangle * 0.3f;
            float aKneeL = Mathf.Max(0f, Mathf.Sin(walkPhase + 1.6f)) * 45f * walkW + (8f + Mathf.Sin(kickPhase) * 10f) * swimW + (85f + dangle) * seatW;
            float aKneeR = Mathf.Max(0f, Mathf.Sin(walkPhase + 1.6f + Mathf.PI)) * 45f * walkW + (8f - Mathf.Sin(kickPhase) * 10f) * swimW + (85f - dangle) * seatW;
            Set(ref qHipL, hipL, Quaternion.Euler(aHipL, 0f, -3f), dt);
            Set(ref qHipR, hipR, Quaternion.Euler(aHipR, 0f, 3f), dt);
            Set(ref qKnL, kneeL, Quaternion.Euler(aKneeL, 0f, 0f), dt);
            Set(ref qKnR, kneeR, Quaternion.Euler(aKneeR, 0f, 0f), dt);

            // ---- arms
            ArmTarget(true, rightItem, rod, time, out Vector3 shR, out float elR);
            ArmTarget(false, leftItem, rod, time, out Vector3 shL, out float elL);
            Set(ref qShR, shoulderR, Quaternion.Euler(shR), dt);
            Set(ref qShL, shoulderL, Quaternion.Euler(shL), dt);
            Set(ref qElR, elbowR, Quaternion.Euler(elR, 0f, 0f), dt);
            Set(ref qElL, elbowL, Quaternion.Euler(elL, 0f, 0f), dt);
            poseInit = true;

            // ---- head, face
            if (head != null)
                head.localRotation = Quaternion.Euler(-28f * swimW + Mathf.Sin(time * 9f) * 3f * talk + Mathf.Sin(time * 0.5f) * 2f, HeadYaw + Mathf.Sin(time * 0.37f) * 4f * (1f - swimW), 0f);
            if (mouth != null) mouth.localScale = new Vector3(1f - talk * 0.15f, 1f + talk * 1.8f, 1f);
            if (maskPivot != null) maskPivot.localRotation = Quaternion.Euler(Mathf.Lerp(-42f, 0f, maskDown), 0f, 0f);
            if (pupilL != null) pupilL.localRotation = pupilBaseL * Quaternion.Euler(Mathf.Sin(time * 2.3f) * 4f, Mathf.Sin(time * 3.1f) * 5f, 0f);
            if (pupilR != null) pupilR.localRotation = pupilBaseR * Quaternion.Euler(Mathf.Sin(time * 2.7f + 1f) * 4f, Mathf.Sin(time * 2.9f + 2f) * 5f, 0f);

            UpdateItems(rod, time);
        }

        void Set(ref Quaternion cur, Transform tr, Quaternion target, float dt)
        {
            if (tr == null) return;
            cur = poseInit ? Quaternion.Slerp(cur, target, 1f - Mathf.Exp(-12f * dt)) : target;
            tr.localRotation = cur;
        }

        /// Shoulder euler + elbow bend for one arm. Negative X raises the arm forward, Z spreads it (right arm +).
        void ArmTarget(bool right, HoldItem item, bool rod, float time, out Vector3 sh, out float el)
        {
            float side = right ? 1f : -1f;
            float armSwing = (right ? 1f : -1f) * Mathf.Sin(walkPhase) * 24f * walkW;
            // land rest / walk
            Vector3 land = new Vector3(armSwing + Mathf.Sin(time * 1.1f + side) * 2f, 0f, side * (12f + Mathf.Sin(time * 1.6f) * 1.5f));
            float landEl = -10f - Mathf.Max(0f, armSwing) * 0.6f;
            // swimming: arms trailing along the body
            Vector3 swim = new Vector3(14f, 0f, side * 9f);
            float swimEl = -6f;

            if (rod)
            {
                if (right) { land = new Vector3(-48f, 0f, 16f); landEl = -58f; }
                else { land = new Vector3(-62f, 22f, -4f); landEl = -40f; }
            }
            else if (Ukulele && seatW > 0.5f)
            {
                float strum = Mathf.Sin(time * 9f) * 9f;
                if (right) { land = new Vector3(-38f + strum, -25f, 20f); landEl = -70f; }
                else { land = new Vector3(-50f, -5f, -35f); landEl = -50f; }
            }
            else if (item == HoldItem.Camera)
            {
                land = new Vector3(-72f, 0f, side * 8f); landEl = -38f;
                swim = new Vector3(-165f, 0f, side * 10f); swimEl = -25f;
            }
            else if (item == HoldItem.Flashlight)
            {
                land = new Vector3(-80f, 0f, side * 6f); landEl = -12f;
                swim = new Vector3(-170f, 0f, side * 14f); swimEl = -8f;
            }

            // seated on a dock / boat: hands resting on the thighs
            if (seatW > 0.01f && !rod && !Ukulele && item == HoldItem.None)
            {
                Vector3 sit = new Vector3(-28f, 0f, side * 16f);
                land = Vector3.Lerp(land, sit, seatW);
                landEl = Mathf.Lerp(landEl, -42f, seatW);
            }
            sh = Vector3.Lerp(land, swim, swimW);
            el = Mathf.Lerp(landEl, swimEl, swimW);
        }

        // ------------------------------------------------------------------ held items
        void UpdateItems(bool rod, float time)
        {
            bool cam = rightItem == HoldItem.Camera || leftItem == HoldItem.Camera;
            bool lamp = rightItem == HoldItem.Flashlight || leftItem == HoldItem.Flashlight;
            Transform yawRoot = transform;

            if (cam && camModel == null) camModel = BuildCamera();
            if (lamp && lampModel == null) lampModel = BuildLamp();
            if (rod && rodModel == null) rodModel = BuildRod();
            if (Ukulele && ukeModel == null) ukeModel = BuildUkulele();

            float aimPitch = Mathf.Lerp(0f, 12f, swimW);
            if (camModel != null)
            {
                Show(camModel, cam && !rod);
                if (cam && !rod)
                {
                    Transform h = rightItem == HoldItem.Camera ? handR : handL;
                    camModel.SetPositionAndRotation(h.position + h.rotation * new Vector3(0f, -0.04f, 0.02f), yawRoot.rotation * Quaternion.Euler(aimPitch, 0f, 0f));
                }
            }
            if (lampModel != null)
            {
                bool show = lamp && !rod;
                Show(lampModel, show);
                if (show)
                {
                    Transform h = rightItem == HoldItem.Flashlight ? handR : handL;
                    lampModel.SetPositionAndRotation(h.position + h.rotation * new Vector3(0f, -0.03f, 0f), yawRoot.rotation * Quaternion.Euler(aimPitch + 6f, 0f, 0f));
                    bool on = lightOn;
                    if (lampLight != null && lampLight.enabled != on) lampLight.enabled = on;
                    if (beam != null)
                    {
                        Vector3 tip = lampTip.position;
                        bool under = tip.y < Waves.HeightAt(tip.x, tip.z) - 0.2f;
                        bool showBeam = on && under;
                        if (beam.enabled != showBeam) beam.enabled = showBeam;
                    }
                }
                else if (lampLight != null && lampLight.enabled) lampLight.enabled = false;
            }
            if (rodModel != null)
            {
                Show(rodModel, rod);
                if (rod) rodModel.SetPositionAndRotation(handR.position + handR.rotation * new Vector3(0f, -0.03f, 0f), yawRoot.rotation * Quaternion.Euler(-52f + Mathf.Sin(time * 0.8f) * 2f, 4f, 0f));
            }
            if (ukeModel != null)
            {
                bool show = Ukulele && seatW > 0.5f;
                Show(ukeModel, show);
                if (show) ukeModel.SetPositionAndRotation(pivot.position + pivot.rotation * new Vector3(0.02f, 1.12f - DiverBuilder.PivotY, 0.3f), pivot.rotation * Quaternion.Euler(0f, 0f, 58f));
            }
        }

        static Material M(Color c) => LowPoly.Mat(c);

        static void Show(Transform t, bool on)
        {
            if (t != null && t.gameObject.activeSelf != on) t.gameObject.SetActive(on);
        }

        Transform ItemRoot(string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(transform, false);
            return t;
        }

        Transform BuildCamera()
        {
            var r = ItemRoot("AvatarCamera");
            Material body = M(LowPoly.Hex("#F2C94C")), dark = M(LowPoly.Hex("#2E3140"));
            DiverMeshes.Part(r, "Body", PlayerMeshes.SharedBlockCentered, body, new Vector3(0f, 0f, 0.02f), new Vector3(0.09f, 0.075f, 0.13f));
            DiverMeshes.Part(r, "Lens", DiverMeshes.Capsule(0.032f, 0.07f, 10), dark, new Vector3(0f, 0f, 0.07f), Vector3.one, new Vector3(90f, 0f, 0f), false);
            DiverMeshes.Part(r, "Handle", PlayerMeshes.SharedBlockCentered, dark, new Vector3(0f, 0.052f, 0.0f), new Vector3(0.025f, 0.022f, 0.1f), default, false);
            return r;
        }

        Transform BuildLamp()
        {
            var r = ItemRoot("AvatarLamp");
            Material body = M(LowPoly.Hex("#F2C94C")), dark = M(LowPoly.Hex("#2E3140"));
            DiverMeshes.Part(r, "Body", DiverMeshes.Capsule(0.026f, 0.17f, 10), body, new Vector3(0f, 0f, -0.04f), Vector3.one, new Vector3(90f, 0f, 0f));
            DiverMeshes.Part(r, "Head", DiverMeshes.Taper(0.03f, 0.042f, 10), dark, new Vector3(0f, 0f, 0.1f), new Vector3(1f, 0.06f, 1f), new Vector3(90f, 0f, 0f), false);
            lampTip = DiverMeshes.Node(r, "Tip", new Vector3(0f, 0f, 0.17f));
            DiverMeshes.Part(lampTip, "Lens", DiverMeshes.Sphere(5, 10), MaterialLibrary.Emissive(LowPoly.Hex("#FFF1C9"), LowPoly.Hex("#FFD9A0"), 2f), new Vector3(0f, 0f, -0.008f), new Vector3(0.07f, 0.07f, 0.02f), default, false);

            var lg = new GameObject("AvatarLampLight");
            lg.transform.SetParent(lampTip, false);
            lampLight = lg.AddComponent<Light>();
            lampLight.type = LightType.Spot;
            lampLight.color = LowPoly.Hex("#FFD9A0");
            lampLight.intensity = 3f;
            lampLight.range = 14f;
            lampLight.spotAngle = 55f;
            lampLight.innerSpotAngle = 30f;
            lampLight.shadows = LightShadows.None;
            lampLight.enabled = false;

            if (beamTex == null) beamTex = BuildBeamTexture();
            var bm = new Material(MaterialLibrary.UnlitShader) { name = "SB_AvatarBeam" };
            bm.SetTexture("_BaseMap", beamTex);
            bm.SetColor("_BaseColor", new Color(1f, 0.92f, 0.72f, 0.16f));
            MaterialLibrary.MakeTransparent(bm, true);
            const float len = 4.5f;
            float rad = Mathf.Tan(25f * Mathf.Deg2Rad) * len;
            beam = DiverMeshes.Part(lampTip, "Beam", DiverMeshes.Beam(), bm, Vector3.zero, new Vector3(rad, rad, len), default, false);
            beam.receiveShadows = false;
            beam.enabled = false;
            return r;
        }

        /// Alpha falls off along the beam (v = 0 at the lamp, 1 at the far end).
        static Texture2D BuildBeamTexture()
        {
            var tex = new Texture2D(4, 32, TextureFormat.RGBA32, false) { name = "SB_BeamFade", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[4 * 32];
            for (int y = 0; y < 32; y++)
            {
                float v = y / 31f;
                float a = Mathf.Clamp01(1f - v) * Mathf.Clamp01(v * 8f);
                for (int x = 0; x < 4; x++) px[y * 4 + x] = new Color(1f, 1f, 1f, a * a);
            }
            tex.SetPixels(px);
            tex.Apply(false, true);
            return tex;
        }

        Transform BuildRod()
        {
            var r = ItemRoot("AvatarRod");
            Material rod = M(LowPoly.Hex("#2E2A40")), band = M(LowPoly.Hex("#3D7BC4")), reel = M(LowPoly.Hex("#8E949C"));
            DiverMeshes.Part(r, "Blank", DiverMeshes.Taper(0.016f, 0.005f, 8), rod, new Vector3(0f, 0f, -0.25f), new Vector3(1f, 1.9f, 1f), new Vector3(90f, 0f, 0f));
            DiverMeshes.Part(r, "Band", DiverMeshes.Taper(0.018f, 0.018f, 8), band, new Vector3(0f, 0f, 0.12f), new Vector3(1f, 0.05f, 1f), new Vector3(90f, 0f, 0f), false);
            DiverMeshes.Part(r, "Reel", DiverMeshes.Capsule(0.04f, 0.06f, 10), reel, new Vector3(-0.03f, -0.06f, 0.0f), Vector3.one, new Vector3(0f, 0f, 90f), false);
            return r;
        }

        Transform BuildUkulele()
        {
            var r = ItemRoot("Ukulele");
            Material wood = M(LowPoly.Hex("#C9874A")), dark = M(LowPoly.Hex("#5E4436")), hole = M(LowPoly.Hex("#2A1E1E"));
            Mesh sph = DiverMeshes.Sphere(6, 12);
            DiverMeshes.Part(r, "Lower", sph, wood, new Vector3(0f, -0.06f, 0f), new Vector3(0.2f, 0.2f, 0.07f));
            DiverMeshes.Part(r, "Upper", sph, wood, new Vector3(0f, 0.07f, 0f), new Vector3(0.16f, 0.15f, 0.065f));
            DiverMeshes.Part(r, "Hole", sph, hole, new Vector3(0f, 0.01f, 0.03f), new Vector3(0.05f, 0.05f, 0.01f), default, false);
            DiverMeshes.Part(r, "Neck", PlayerMeshes.SharedBlockCentered, dark, new Vector3(0f, 0.26f, 0f), new Vector3(0.04f, 0.26f, 0.025f));
            DiverMeshes.Part(r, "HeadStock", PlayerMeshes.SharedBlockCentered, dark, new Vector3(0f, 0.41f, -0.005f), new Vector3(0.055f, 0.07f, 0.025f), default, false);
            return r;
        }

        void OnDestroy()
        {
            // item materials: only the beam material is ours
            if (beam != null && beam.sharedMaterial != null) Destroy(beam.sharedMaterial);
        }

        /// Turns shadow casting off for every renderer (distant / many avatars).
        public void SetShadows(bool on)
        {
            var rs = GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < rs.Length; i++) rs[i].shadowCastingMode = on ? ShadowCastingMode.On : ShadowCastingMode.Off;
        }
    }
}
