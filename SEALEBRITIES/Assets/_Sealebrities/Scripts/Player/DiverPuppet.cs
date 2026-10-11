using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Procedural animation of a bean diver built by DiverBuilder (menu crew, intro, co-op avatars, selfie view, the
    /// character preview). Round 2b (L.13): implements IDiverMotion – the full motion set (idle with weight shift and
    /// fidgets, walk / run with bounce, hip sway, counter-rotating shoulders and footfall squash, jump / fall with
    /// windmilling arms / land squash, ladder with alternating hands, crouch, sit, lie, carry, selfie with the camera
    /// toward the lens, unconscious float, surface breaststroke vs. dive flutter / dolphin kick) and expressions
    /// (eyelids that blink every 3–6 s, 8 mouth shapes, brows, blush). The legacy SetPose (menu crew) maps onto the same
    /// states. Held items (camera / lamp with a light cone / two-handed rod / ukulele) and the Q51 strap gear.
    /// No per-frame allocations.
    public sealed class DiverPuppet : MonoBehaviour, IDiverPuppet, IDiverMotion
    {
        public const int MouthShapes = 8;

        // ------------------------------------------------------------------ rig (filled by DiverBuilder)
        internal Transform pivot, head, maskPivot, mouth, hatT, pupilL, pupilR;
        internal Transform shoulderL, shoulderR, elbowL, elbowR, handL, handR, hipL, hipR, kneeL, kneeR;
        internal Transform eyeL, eyeR, lidL, lidR, browL, browR, blushL, blushR;
        internal Transform[] mouths;
        internal int baseMouth, eyeStyle, browStyle;
        internal DiverLook look;
        internal int hat;
        /// Q51 gear built by DiverGear (null when not built): lamp on the chest strap, camera on its lanyard, dive scooter.
        internal Transform strapLamp, strapCam, scooter, scooterProp;
        /// The diver owns a lamp / camera (from the look, or seen in a hand once): shown on the strap while not held.
        bool ownsLamp, ownsCam;
        float propAngle;

        Quaternion pupilBaseL, pupilBaseR;
        Vector3 pivotBase, browBaseL, browBaseR;

        // ------------------------------------------------------------------ pose input
        MotionState motion = MotionState.Idle;
        bool hasNetPhase, lightOn;
        float netPhase, speed;
        HoldItem rightItem, leftItem;
        float talkTarget;
        int expression;

        /// Extra menu-crew props (not part of IDiverPuppet).
        public bool Ukulele;
        /// 0..1 slow leg swing while seated (dock edge).
        public float LegDangle = 1f;
        /// Seconds offset so a crew does not move in sync.
        public float Phase;
        /// Optional head turn (degrees, + = right) e.g. for the crew looking at each other.
        public float HeadYaw;
        /// Selfie view of the local player: the camera in the hand turns toward the diver (lens at the face).
        public bool SelfieLensFacesDiver = true;

        // ------------------------------------------------------------------ smoothed state
        float swimW, diveW, seatW, walkW, runW, ladderW, crouchW, lieW, carryW, selfieW, limpW, airW, fallW, talk, maskDown, t;
        float walkPhase, kickPhase, climbPhase, strokePhase, landSquash, prevSpeed, accelLean, blush;
        float blinkTimer = 3f, blinkT = 1f, lidL01, lidR01;
        float fidgetTimer = 12f, fidgetT = 1f, weightTimer = 5f, weightSide, weightCur;
        int fidgetKind;
        MotionState prevMotion;
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
        /// Current motion state (last SetMotion / SetPose).
        public MotionState Motion => motion;

        internal void InitRig()
        {
            NameAnchor = DiverMeshes.Node(transform, "NameAnchor", new Vector3(0f, 2.15f, 0f));
            pupilBaseL = pupilL != null ? pupilL.localRotation : Quaternion.identity;
            pupilBaseR = pupilR != null ? pupilR.localRotation : Quaternion.identity;
            if (browL != null) browBaseL = browL.localPosition;
            if (browR != null) browBaseR = browR.localPosition;
            pivotBase = pivot.localPosition;
            if (look.sitting) { motion = MotionState.Sit; seatW = 1f; }
            ownsLamp = look.lampOnStrap;
            ownsCam = look.cameraOnStrap;
            Phase = Mathf.Abs(look.suit.r * 13.7f + look.suit.g * 7.1f + look.suit.b * 3.3f) % 6.28f;
            blinkTimer = 2f + Mathf.Repeat(Phase * 1.7f, 3f);
            fidgetTimer = 8f + Mathf.Repeat(Phase * 3.1f, 8f);
        }

        // ------------------------------------------------------------------ IDiverPuppet (legacy)
        public void SetPose(bool swimming, bool seated, float speed, HoldItem right, HoldItem left, bool lightOn)
        {
            this.speed = Mathf.Max(0f, speed);
            rightItem = right;
            leftItem = left;
            this.lightOn = lightOn;
            hasNetPhase = false;
            if (seated) motion = MotionState.Sit;
            else if (swimming) motion = MotionState.Dive;
            else if (this.speed > 3.3f) motion = MotionState.Run;
            else if (this.speed > 0.15f) motion = MotionState.Walk;
            else motion = MotionState.Idle;
        }

        public void SetTalking(float loudness01) => talkTarget = Mathf.Clamp01(loudness01);

        // ------------------------------------------------------------------ IDiverMotion (L.13)
        public void SetMotion(MotionState state, float speed, float phase, HoldItem right, HoldItem left, bool lightOn)
        {
            motion = state;
            this.speed = float.IsNaN(speed) ? 0f : Mathf.Max(0f, speed);
            netPhase = float.IsNaN(phase) ? 0f : Mathf.Repeat(phase, 1f);
            hasNetPhase = true;
            rightItem = right;
            leftItem = left;
            this.lightOn = lightOn;
        }

        public void SetExpression(int expression) => this.expression = Mathf.Clamp(expression, 0, 6);

        // ------------------------------------------------------------------ animation
        void LateUpdate()
        {
            if (pivot == null) return;
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            if (dt <= 0f) return;
            t += dt;
            float time = t + Phase;

            UpdateWeights(dt);
            UpdatePhases(dt);
            talk = Mathf.Lerp(talk, talkTarget, 1f - Mathf.Exp(-20f * dt));
            bool rod = rightItem == HoldItem.FishingRod || leftItem == HoldItem.FishingRod;
            maskDown = Mathf.MoveTowards(maskDown, swimW > 0.5f || hat != 0 ? 1f : 0f, dt * 2.5f);

            // ---- idle life: weight shift every 4–7 s, a fidget every 10–20 s
            float idleW = Mathf.Clamp01(1f - walkW - swimW - seatW - ladderW - lieW - limpW - airW - crouchW);
            weightTimer -= dt;
            if (weightTimer <= 0f) { weightTimer = 4f + Mathf.Repeat(time * 7.3f, 3f); weightSide = weightSide > 0f ? -1f : 1f; }
            weightCur = Mathf.Lerp(weightCur, weightSide * idleW, 1f - Mathf.Exp(-2.5f * dt));
            fidgetTimer -= dt;
            if (fidgetTimer <= 0f && idleW > 0.8f && rightItem == HoldItem.None && leftItem == HoldItem.None)
            {
                fidgetTimer = 10f + Mathf.Repeat(time * 5.1f, 10f);
                fidgetT = 0f;
                fidgetKind = (fidgetKind + 1) % 3;
            }
            if (fidgetT < 1f) fidgetT += dt / 1.6f;
            float fidget = fidgetT < 1f ? Mathf.Sin(fidgetT * Mathf.PI) * idleW : 0f;

            // ---- body
            float s = Mathf.Sin(walkPhase), c = Mathf.Cos(walkPhase);
            float breathe = Mathf.Sin(time * (lieW > 0.5f ? 1.1f : 1.6f)) * 0.006f;
            float bounceAmp = Mathf.Lerp(0.04f, 0.07f, runW) * walkW;
            float bounce = (1f - Mathf.Abs(s)) * bounceAmp - bounceAmp * 0.5f;
            float crouchDrop = 0.22f * crouchW;
            float y = breathe + bounce - 0.3f * seatW - crouchDrop - 0.55f * lieW - 0.04f * landSquash;
            float z = 0.06f * ladderW;
            pivot.localPosition = pivotBase + new Vector3(weightCur * 0.012f, y, z);

            float lean = 10f * runW * walkW + 4f * walkW * (1f - runW) + accelLean + 15f * crouchW + 4f * ladderW + 6f * carryW * walkW;
            float roll = s * 4f * walkW + weightCur * 2.5f + Mathf.Sin(time * 0.7f) * 1.2f * swimW;
            float twist = s * 6f * walkW;
            float swimPitch = 80f * diveW + 18f * (swimW - diveW) + Mathf.Sin(time * 0.9f) * 2f * swimW;
            Quaternion pivotTarget = Quaternion.Euler(swimPitch + lean * (1f - swimW) - 4f * seatW - 86f * lieW, twist * (1f - swimW), roll * (1f - lieW));
            if (limpW > 0.01f)
            {
                float sway = Mathf.Sin(time * 0.7f) * 6f;
                pivotTarget = Quaternion.Slerp(pivotTarget, Quaternion.Euler(18f + sway, 0f, 72f), limpW);
            }
            qPivot = poseInit ? Quaternion.Slerp(qPivot, pivotTarget, 1f - Mathf.Exp(-10f * dt)) : pivotTarget;
            pivot.localRotation = qPivot;
            // footfall squash (run) + land squash; air stretch
            float foot = Mathf.Pow(Mathf.Abs(s), 6f) * 0.04f * runW * walkW;
            float squash = foot + 0.1f * landSquash - 0.06f * airW * (1f - fallW);
            pivot.localScale = new Vector3(1f + squash * 0.5f, 1f - squash, 1f + squash * 0.5f);

            // ---- legs
            float hipAmp = Mathf.Lerp(28f, 46f, runW) * walkW * (1f - crouchW * 0.6f);
            float kneeAmp = Mathf.Lerp(42f, 85f, runW) * walkW;
            float kickAmp = 20f * swimW * (1f - limpW);
            float dolphin = runW * diveW;   // sprinting under water: both legs together
            float kick = Mathf.Sin(kickPhase) * kickAmp;
            float dangle = Mathf.Sin(time * 1.3f) * 12f * LegDangle * seatW;
            float climbL = Mathf.Sin(climbPhase * Mathf.PI * 2f), climbR = -climbL;
            float aHipL = s * hipAmp + Mathf.Lerp(kick, Mathf.Sin(kickPhase) * 26f * swimW, dolphin) - 85f * seatW + dangle * 0.3f
                          + (-35f + climbL * 22f) * ladderW - 55f * crouchW - 20f * airW + 4f * limpW + weightCur * 3f;
            float aHipR = -s * hipAmp + Mathf.Lerp(-kick, Mathf.Sin(kickPhase) * 26f * swimW, dolphin) - 85f * seatW - dangle * 0.3f
                          + (-35f + climbR * 22f) * ladderW - 55f * crouchW - 20f * airW + 10f * limpW - weightCur * 3f;
            float aKneeL = Mathf.Max(0f, Mathf.Sin(walkPhase + 1.6f)) * kneeAmp + (8f + Mathf.Sin(kickPhase) * 10f) * swimW + (85f + dangle) * seatW
                           + (55f + climbL * 30f) * ladderW + 100f * crouchW + 45f * airW + 20f * limpW;
            float aKneeR = Mathf.Max(0f, Mathf.Sin(walkPhase + 1.6f + Mathf.PI)) * kneeAmp + (8f - Mathf.Sin(kickPhase) * 10f) * swimW + (85f - dangle) * seatW
                           + (55f + climbR * 30f) * ladderW + 100f * crouchW + 45f * airW + 30f * limpW;
            float legSpread = 3f + 4f * lieW;
            Set(ref qHipL, hipL, Quaternion.Euler(aHipL * (1f - lieW), 0f, -legSpread), dt);
            Set(ref qHipR, hipR, Quaternion.Euler(aHipR * (1f - lieW), 0f, legSpread), dt);
            Set(ref qKnL, kneeL, Quaternion.Euler(aKneeL * (1f - lieW) + 6f * lieW, 0f, 0f), dt);
            Set(ref qKnR, kneeR, Quaternion.Euler(aKneeR * (1f - lieW) + 6f * lieW, 0f, 0f), dt);

            // ---- arms
            ArmTarget(true, rightItem, rod, time, s, climbR, fidget, out Vector3 shR, out float elR);
            ArmTarget(false, leftItem, rod, time, s, climbL, fidget, out Vector3 shL, out float elL);
            Set(ref qShR, shoulderR, Quaternion.Euler(shR), dt);
            Set(ref qShL, shoulderL, Quaternion.Euler(shL), dt);
            Set(ref qElR, elbowR, Quaternion.Euler(elR, 0f, 0f), dt);
            Set(ref qElL, elbowL, Quaternion.Euler(elL, 0f, 0f), dt);
            poseInit = true;

            // ---- head (stabilised against the walk twist), face
            if (head != null)
            {
                float hp = -28f * diveW - 10f * (swimW - diveW) + Mathf.Sin(time * 9f) * 3f * talk + Mathf.Sin(time * 0.5f) * 2f * idleW
                           - 12f * ladderW + 8f * crouchW + 20f * limpW + (fidgetKind == 2 ? 25f * fidget : 0f) - 6f * selfieW;
                float hy = HeadYaw * (1f - limpW) + Mathf.Sin(time * 0.37f) * 4f * idleW - twist * 0.8f + (fidgetKind == 0 ? 28f * fidget : 0f);
                float hr = 6f * selfieW + (expression == (int)SelfieExpression.Laugh ? Mathf.Sin(time * 14f) * 4f : 0f) + 12f * limpW;
                head.localRotation = Quaternion.Euler(hp, hy, hr);
            }
            UpdateFace(dt, time);
            if (maskPivot != null) maskPivot.localRotation = Quaternion.Euler(Mathf.Lerp(-42f, 0f, maskDown), 0f, 0f);

            UpdateItems(rod, time);
            prevMotion = motion;
        }

        void UpdateWeights(float dt)
        {
            MotionState m = motion;
            bool water = m == MotionState.Swim || m == MotionState.Dive || m == MotionState.Tow;
            bool dive = m == MotionState.Dive || m == MotionState.Tow;
            bool loco = m == MotionState.Walk || m == MotionState.Run || m == MotionState.Carry || m == MotionState.Crouch || m == MotionState.Land
                        || (m == MotionState.Idle && speed > 0.4f) || (m == MotionState.Selfie && speed > 0.3f && !water);
            float k6 = 1f - Mathf.Exp(-6f * dt), k8 = 1f - Mathf.Exp(-8f * dt), k4 = 1f - Mathf.Exp(-4f * dt);
            swimW = Mathf.Lerp(swimW, water ? 1f : 0f, k4);
            diveW = Mathf.Lerp(diveW, dive ? 1f : 0f, k4);
            seatW = Mathf.Lerp(seatW, m == MotionState.Sit ? 1f : 0f, k6);
            float walkTarget = loco ? Mathf.Clamp01(speed / 1.4f) : 0f;
            walkW = Mathf.Lerp(walkW, walkTarget, k8);
            runW = Mathf.Lerp(runW, m == MotionState.Run || (dive && speed > 3.2f) ? 1f : 0f, k6);
            ladderW = Mathf.Lerp(ladderW, m == MotionState.Ladder ? 1f : 0f, k8);
            crouchW = Mathf.Lerp(crouchW, m == MotionState.Crouch ? 1f : 0f, k8);
            lieW = Mathf.Lerp(lieW, m == MotionState.Lie ? 1f : 0f, k4);
            carryW = Mathf.Lerp(carryW, m == MotionState.Carry ? 1f : 0f, k8);
            selfieW = Mathf.Lerp(selfieW, m == MotionState.Selfie ? 1f : 0f, k8);
            limpW = Mathf.Lerp(limpW, m == MotionState.Unconscious ? 1f : 0f, 1f - Mathf.Exp(-1.5f * dt));
            airW = Mathf.Lerp(airW, m == MotionState.Jump || m == MotionState.Fall ? 1f : 0f, k8);
            fallW = Mathf.Lerp(fallW, m == MotionState.Fall ? 1f : 0f, k6);
            // landing squash: 0.12 s after a jump / fall ends on the ground
            if ((prevMotion == MotionState.Fall || prevMotion == MotionState.Jump) && m != MotionState.Fall && m != MotionState.Jump && !water) landSquash = 1f;
            if (m == MotionState.Land && prevMotion != MotionState.Land) landSquash = 1f;
            landSquash = Mathf.MoveTowards(landSquash, 0f, dt / 0.12f);
            // start / stop: a short lean into the acceleration (0.15 s anticipation, small overshoot when stopping)
            float acc = (speed - prevSpeed) / Mathf.Max(dt, 0.001f);
            prevSpeed = speed;
            accelLean = Mathf.Lerp(accelLean, water ? 0f : Mathf.Clamp(acc * 0.9f, -4f, 7f), 1f - Mathf.Exp(-dt / 0.15f));
        }

        void UpdatePhases(float dt)
        {
            const float TwoPi = Mathf.PI * 2f;
            bool locomotion = motion == MotionState.Walk || motion == MotionState.Run || motion == MotionState.Carry || motion == MotionState.Crouch;
            if (hasNetPhase && locomotion)
            {
                // co-op: stay in step with the sender (phase = one cycle = two steps)
                float target = netPhase * TwoPi;
                walkPhase = target;
            }
            else
            {
                // cadence: stride grows with speed, max 4.2 steps/s (one step = π)
                float stride = Mathf.Clamp(0.45f + speed * 0.12f, 0.5f, 1.25f);
                float steps = Mathf.Min(Mathf.Max(0.2f, speed) / stride, 4.2f);
                walkPhase = Mathf.Repeat(walkPhase + dt * steps * Mathf.PI, TwoPi);
            }
            kickPhase = Mathf.Repeat(kickPhase + dt * (3.2f + Mathf.Min(speed, 3f) * 1.6f), TwoPi);
            if (motion == MotionState.Ladder)
            {
                if (hasNetPhase) climbPhase = netPhase;
                else climbPhase = Mathf.Repeat(climbPhase + dt * Mathf.Clamp(speed / 0.6f, 0f, 2f), 1f);
            }
            strokePhase = Mathf.Repeat(strokePhase + dt * Mathf.Lerp(0.35f, 0.9f, Mathf.Clamp01(speed / 2f)), 1f);
        }

        void Set(ref Quaternion cur, Transform tr, Quaternion target, float dt)
        {
            if (tr == null) return;
            cur = poseInit ? Quaternion.Slerp(cur, target, 1f - Mathf.Exp(-12f * dt)) : target;
            tr.localRotation = cur;
        }

        /// Shoulder euler + elbow bend for one arm. Negative X raises the arm forward, Z spreads it (right arm +).
        void ArmTarget(bool right, HoldItem item, bool rod, float time, float walkSin, float climb, float fidget, out Vector3 sh, out float el)
        {
            float side = right ? 1f : -1f;
            float armSwing = side * walkSin * Mathf.Lerp(22f, 45f, runW) * walkW;
            // land rest / walk (run: elbows bent 80° and pumping)
            Vector3 land = new Vector3(armSwing + Mathf.Sin(time * 1.1f + side) * 2f, 0f, side * (12f + Mathf.Sin(time * 1.6f) * 1.5f));
            float landEl = Mathf.Lerp(-10f - Mathf.Max(0f, armSwing) * 0.6f, -80f + armSwing * 0.25f, runW * walkW);
            // idle fidgets: adjust the mask (right), scratch the head (left), check the wrist computer (left)
            if (fidget > 0.01f)
            {
                if (fidgetKind == 0 && right) { land = Vector3.Lerp(land, new Vector3(-155f, 0f, 25f), fidget); landEl = Mathf.Lerp(landEl, -95f, fidget); }
                else if (fidgetKind == 1 && !right) { land = Vector3.Lerp(land, new Vector3(-150f, 0f, -45f), fidget); landEl = Mathf.Lerp(landEl, -110f + Mathf.Sin(time * 18f) * 10f, fidget); }
                else if (fidgetKind == 2 && !right) { land = Vector3.Lerp(land, new Vector3(-70f, 25f, -10f), fidget); landEl = Mathf.Lerp(landEl, -95f, fidget); }
            }
            // swimming: dive = arms trailing along the body; surface = breaststroke
            Vector3 swim = new Vector3(14f, 0f, side * 9f);
            float swimEl = -6f;
            float surf = swimW - diveW;
            if (surf > 0.01f)
            {
                float u = strokePhase;
                // reach forward → sweep out → pull in → recover
                float a = Mathf.Sin(u * Mathf.PI * 2f), b = Mathf.Cos(u * Mathf.PI * 2f);
                Vector3 stroke = new Vector3(-120f + 50f * b, 0f, side * (30f + 35f * a));
                float strokeEl = -40f - 35f * Mathf.Max(0f, -b);
                swim = Vector3.Lerp(swim, stroke, Mathf.Clamp01(surf / Mathf.Max(0.01f, swimW)));
                swimEl = Mathf.Lerp(swimEl, strokeEl, Mathf.Clamp01(surf / Mathf.Max(0.01f, swimW)));
            }

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

            // ---- full-body states on top
            if (carryW > 0.01f)
            {
                sh = Vector3.Lerp(sh, new Vector3(-68f, 0f, side * 8f), carryW);
                el = Mathf.Lerp(el, -32f, carryW);
            }
            if (crouchW > 0.01f && item == HoldItem.None && !rod)
            {
                sh = Vector3.Lerp(sh, new Vector3(-35f + armSwing * 0.4f, 0f, side * 18f), crouchW);
                el = Mathf.Lerp(el, -45f, crouchW);
            }
            if (ladderW > 0.01f)
            {
                // hands alternate on the rungs: reach up, grip, pull down
                Vector3 grip = new Vector3(-150f + climb * 28f, 0f, side * 14f);
                sh = Vector3.Lerp(sh, grip, ladderW);
                el = Mathf.Lerp(el, -45f - climb * 32f, ladderW);
            }
            if (airW > 0.01f)
            {
                Vector3 up = new Vector3(-150f, 0f, side * 28f);
                if (fallW > 0.01f)
                {
                    // windmill
                    float w = time * 11f + (right ? 0f : Mathf.PI);
                    up = Vector3.Lerp(up, new Vector3(-110f + Mathf.Sin(w) * 70f, 0f, side * (40f + Mathf.Cos(w) * 25f)), fallW);
                }
                sh = Vector3.Lerp(sh, up, airW);
                el = Mathf.Lerp(el, -20f, airW);
            }
            if (selfieW > 0.01f)
            {
                // the camera hand stretches toward the lens, the other one waves
                bool camHand = item == HoldItem.Camera || (right && item == HoldItem.None && rightItem != HoldItem.Camera && leftItem != HoldItem.Camera);
                Vector3 selfie = camHand ? new Vector3(-118f, -8f, side * 6f) : new Vector3(-150f + Mathf.Sin(time * 8f) * 10f, 0f, side * 55f);
                float selfieEl = camHand ? -6f : -70f + Mathf.Sin(time * 8f) * 15f;
                sh = Vector3.Lerp(sh, selfie, selfieW);
                el = Mathf.Lerp(el, selfieEl, selfieW);
            }
            if (lieW > 0.01f)
            {
                // hands folded on the belly (the torso is pitched −86°, so these rest on it instead of pointing up)
                sh = Vector3.Lerp(sh, new Vector3(-10f, 0f, side * 15f), lieW);
                el = Mathf.Lerp(el, -95f, lieW);
            }
            if (limpW > 0.01f)
            {
                float drift = Mathf.Sin(time * 0.8f + side) * 8f;
                sh = Vector3.Lerp(sh, new Vector3(10f + drift, 0f, side * 38f), limpW);
                el = Mathf.Lerp(el, -12f, limpW);
            }
        }

        // ------------------------------------------------------------------ face: lids, blink, brows, mouth, blush
        void UpdateFace(float dt, float time)
        {
            var ex = (SelfieExpression)expression;
            bool asleep = lieW > 0.6f || limpW > 0.5f;
            float baseLid = eyeStyle == 1 ? 15f : -55f;
            float lidL = baseLid, lidR = baseLid;
            int mouthShape = baseMouth;
            float browUp = 0f;
            bool happy = false;
            float pupilScale = 1f;
            switch (ex)
            {
                case SelfieExpression.Grin: mouthShape = 4; lidL = lidR = Mathf.Min(baseLid, -40f); happy = true; break;
                case SelfieExpression.Wow: mouthShape = 5; lidL = lidR = -84f; browUp = 0.014f; pupilScale = 1.12f; break;
                case SelfieExpression.Scared: mouthShape = 6; lidL = lidR = -86f; browUp = 0.018f; pupilScale = 0.72f; break;
                case SelfieExpression.Kissy: mouthShape = 7; lidL = lidR = 28f; happy = true; break;
                case SelfieExpression.Wink: mouthShape = 0; lidL = 92f; lidR = Mathf.Min(baseLid, -45f); happy = true; break;
                case SelfieExpression.Laugh: mouthShape = 1; lidL = lidR = 58f; happy = true; break;
            }
            if (talk > 0.35f && ex == SelfieExpression.Auto) mouthShape = 1;
            if (limpW > 0.5f) mouthShape = 2;
            if (asleep) { lidL = lidR = 92f; mouthShape = lieW > 0.6f ? 3 : mouthShape; }

            // blink every 3–6 s (0.14 s), not while the eyes are closed anyway
            blinkTimer -= dt;
            if (blinkTimer <= 0f)
            {
                blinkTimer = 3f + Mathf.Repeat(time * 3.7f, 3f);
                blinkT = 0f;
            }
            if (blinkT < 1f) blinkT += dt / 0.14f;
            float blinkAmt = blinkT < 1f ? Mathf.Sin(Mathf.Clamp01(blinkT) * Mathf.PI) : 0f;
            lidL = Mathf.Lerp(lidL, 92f, blinkAmt);
            lidR = Mathf.Lerp(lidR, 92f, blinkAmt);
            float k = 1f - Mathf.Exp(-22f * dt);
            lidL01 = Mathf.Lerp(lidL01, lidL, k);
            lidR01 = Mathf.Lerp(lidR01, lidR, k);
            if (this.lidL != null) this.lidL.localRotation = Quaternion.Euler(lidL01, 0f, 0f);
            if (this.lidR != null) this.lidR.localRotation = Quaternion.Euler(lidR01, 0f, 0f);

            if (browL != null) browL.localPosition = browBaseL + new Vector3(0f, browUp, 0f);
            if (browR != null) browR.localPosition = browBaseR + new Vector3(0f, browUp, 0f);

            if (mouths != null)
            {
                for (int i = 0; i < mouths.Length; i++)
                {
                    var m = mouths[i];
                    if (m == null) continue;
                    bool on = i == mouthShape;
                    if (m.gameObject.activeSelf != on) m.gameObject.SetActive(on);
                }
            }
            if (mouth != null) mouth.localScale = new Vector3(1f - talk * 0.15f, 1f + talk * 1.4f, 1f);

            blush = Mathf.MoveTowards(blush, happy ? 1f : 0f, dt * 3f);
            bool showBlush = blush > 0.05f;
            if (blushL != null && blushL.gameObject.activeSelf != showBlush) blushL.gameObject.SetActive(showBlush);
            if (blushR != null && blushR.gameObject.activeSelf != showBlush) blushR.gameObject.SetActive(showBlush);
            if (showBlush)
            {
                if (blushL != null) blushL.localScale = Vector3.one * blush;
                if (blushR != null) blushR.localScale = Vector3.one * blush;
            }

            if (pupilL != null)
            {
                pupilL.localRotation = pupilBaseL * Quaternion.Euler(Mathf.Sin(time * 2.3f) * 4f, Mathf.Sin(time * 3.1f) * 5f, 0f);
                pupilL.localScale = Vector3.one * pupilScale;
            }
            if (pupilR != null)
            {
                pupilR.localRotation = pupilBaseR * Quaternion.Euler(Mathf.Sin(time * 2.7f + 1f) * 4f, Mathf.Sin(time * 2.9f + 2f) * 5f, 0f);
                pupilR.localScale = Vector3.one * pupilScale;
            }
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

            // Q51: lamp / camera on the strap while not in a hand, the scooter only while swimming
            if (lamp) ownsLamp = true;
            if (cam) ownsCam = true;
            Show(strapLamp, ownsLamp && !lamp);
            Show(strapCam, ownsCam && !cam);
            if (scooter != null)
            {
                bool scoot = diveW > 0.55f && seatW < 0.5f && selfieW < 0.5f && limpW < 0.5f;
                Show(scooter, scoot);
                if (scoot && scooterProp != null)
                {
                    propAngle = Mathf.Repeat(propAngle + Time.deltaTime * (300f + speed * 600f), 360f);
                    scooterProp.localRotation = Quaternion.Euler(0f, propAngle, 0f);
                }
            }

            float aimPitch = Mathf.Lerp(0f, 12f, diveW);
            if (camModel != null)
            {
                Show(camModel, cam && !rod);
                if (cam && !rod)
                {
                    Transform h = rightItem == HoldItem.Camera ? handR : handL;
                    // selfie: the lens points back at the diver's face
                    Quaternion rot = yawRoot.rotation * Quaternion.Euler(aimPitch, 0f, 0f);
                    if (selfieW > 0.5f && SelfieLensFacesDiver && head != null)
                    {
                        Vector3 toFace = head.position + Vector3.up * 0.12f - h.position;
                        if (toFace.sqrMagnitude > 1e-4f) rot = Quaternion.LookRotation(toFace.normalized, Vector3.up);
                    }
                    camModel.SetPositionAndRotation(h.position + h.rotation * new Vector3(0f, -0.04f, 0.02f), rot);
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
                    bool on = lightOn && limpW < 0.5f;
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

        /// The avatar's hand camera (L.13 item look: rounded body, 24-segment lens with a ring).
        Transform BuildCamera()
        {
            var r = ItemRoot("AvatarCamera");
            Material body = MaterialLibrary.Opaque(LowPoly.Hex("#F2C94C"), 0.3f, 0f), dark = MaterialLibrary.Opaque(LowPoly.Hex("#2E3140"), 0.35f, 0f);
            var slab = DiverMeshes.Part(r, "Body", DiverMeshes.RoundedSlab("avatar_cam", 0.09f, 0.075f, 0.13f, 0.014f, 4), body, new Vector3(0f, 0f, 0.02f), Vector3.one);
            slab.sharedMaterials = new[] { body, body };
            DiverMeshes.Part(r, "Lens", DiverMeshes.SmoothCapsule(0.032f, 0.07f, 24, 3, 1), dark, new Vector3(0f, 0f, 0.07f), Vector3.one, new Vector3(90f, 0f, 0f), false);
            DiverMeshes.Part(r, "LensRing", DiverMeshes.Torus(0.033f, 0.005f, 24, 6), MaterialLibrary.Opaque(LowPoly.Hex("#EE7F2D"), 0.3f, 0f), new Vector3(0f, 0f, 0.135f), Vector3.one, new Vector3(90f, 0f, 0f), false);
            DiverMeshes.Part(r, "Handle", DiverMeshes.SmoothCapsule(0.012f, 0.1f, 12, 3, 1), dark, new Vector3(0f, 0.052f, -0.05f), Vector3.one, new Vector3(90f, 0f, 0f), false);
            return r;
        }

        Transform BuildLamp()
        {
            var r = ItemRoot("AvatarLamp");
            Material body = M(LowPoly.Hex("#F2C94C")), dark = M(LowPoly.Hex("#2E3140"));
            DiverMeshes.Part(r, "Body", DiverMeshes.SmoothCapsule(0.026f, 0.17f, 16, 4, 2), body, new Vector3(0f, 0f, -0.04f), Vector3.one, new Vector3(90f, 0f, 0f));
            DiverMeshes.Part(r, "Head", DiverMeshes.Taper(0.03f, 0.042f, 20), dark, new Vector3(0f, 0f, 0.1f), new Vector3(1f, 0.06f, 1f), new Vector3(90f, 0f, 0f), false);
            DiverMeshes.Part(r, "Knurl", DiverMeshes.Torus(0.031f, 0.004f, 24, 5), dark, new Vector3(0f, 0f, 0.09f), Vector3.one, new Vector3(90f, 0f, 0f), false);
            lampTip = DiverMeshes.Node(r, "Tip", new Vector3(0f, 0f, 0.17f));
            DiverMeshes.Part(lampTip, "Lens", DiverMeshes.Sphere(8, 16), MaterialLibrary.Emissive(LowPoly.Hex("#FFF1C9"), LowPoly.Hex("#FFD9A0"), 2f), new Vector3(0f, 0f, -0.008f), new Vector3(0.07f, 0.07f, 0.02f), default, false);

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
            Material rod = M(LowPoly.Hex("#2E2A40")), band = M(LowPoly.Hex("#3D7BC4")), reel = MaterialLibrary.Opaque(LowPoly.Hex("#8E949C"), 0.4f, 0.2f);
            DiverMeshes.Part(r, "Blank", DiverMeshes.Taper(0.016f, 0.005f, 12), rod, new Vector3(0f, 0f, -0.25f), new Vector3(1f, 1.9f, 1f), new Vector3(90f, 0f, 0f));
            DiverMeshes.Part(r, "Band", DiverMeshes.Taper(0.018f, 0.018f, 12), band, new Vector3(0f, 0f, 0.12f), new Vector3(1f, 0.05f, 1f), new Vector3(90f, 0f, 0f), false);
            DiverMeshes.Part(r, "Reel", DiverMeshes.SmoothCapsule(0.04f, 0.06f, 20, 3, 1), reel, new Vector3(-0.03f, -0.06f, 0.0f), Vector3.one, new Vector3(0f, 0f, 90f), false);
            return r;
        }

        Transform BuildUkulele()
        {
            var r = ItemRoot("Ukulele");
            Material wood = M(LowPoly.Hex("#C9874A")), dark = M(LowPoly.Hex("#5E4436")), hole = M(LowPoly.Hex("#2A1E1E"));
            Mesh sph = DiverMeshes.Sphere(10, 18);
            DiverMeshes.Part(r, "Lower", sph, wood, new Vector3(0f, -0.06f, 0f), new Vector3(0.2f, 0.2f, 0.07f));
            DiverMeshes.Part(r, "Upper", sph, wood, new Vector3(0f, 0.07f, 0f), new Vector3(0.16f, 0.15f, 0.065f));
            DiverMeshes.Part(r, "Hole", sph, hole, new Vector3(0f, 0.01f, 0.03f), new Vector3(0.05f, 0.05f, 0.01f), default, false);
            var neck = DiverMeshes.Part(r, "Neck", DiverMeshes.RoundedSlab("uke_neck", 0.04f, 0.26f, 0.025f, 0.008f, 3), dark, new Vector3(0f, 0.26f, 0f), Vector3.one);
            neck.sharedMaterials = new[] { dark, dark };
            var hs = DiverMeshes.Part(r, "HeadStock", DiverMeshes.RoundedSlab("uke_head", 0.055f, 0.07f, 0.025f, 0.01f, 3), dark, new Vector3(0f, 0.41f, -0.005f), Vector3.one, default, false);
            hs.sharedMaterials = new[] { dark, dark };
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
