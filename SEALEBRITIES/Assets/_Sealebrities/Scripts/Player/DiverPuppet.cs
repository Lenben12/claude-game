using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Procedural animation of a diver built by DiverBuilder (menu crew, intro, co-op avatars, selfie view, the
    /// character preview). Implements IDiverMotion – idle with weight shift and fidgets, walk / run (round 3: knee flex
    /// in the swing phase, heel kick when running, foot roll, pelvis twist + roll with counter-rotating shoulders on the
    /// spine joint, head stabilised, pumping arms), jump / fall / land squash, ladder (alternating hands AND feet on the
    /// rungs, body pressed to the ladder), crouch, sit, lie (round 3: really IN the bed / hammock – the pelvis rests on
    /// the surface, the torso and legs follow the hammock curve, arms relaxed at the sides; see LiePose / LieEye), carry,
    /// selfie, unconscious float, surface breaststroke with a frog kick vs. dive flutter / dolphin kick with pointed fins)
    /// and expressions (eyelids that blink every 3–6 s, 8 mouth shapes, brows, blush). Held items (camera / lamp with a
    /// light cone / two-handed rod / ukulele / stick / spear / speargun / food / tool) and the Q51 strap gear.
    /// No per-frame allocations.
    public sealed class DiverPuppet : MonoBehaviour, IDiverPuppet, IDiverMotion
    {
        public const int MouthShapes = 8;

        // ------------------------------------------------------------------ rig (filled by DiverBuilder)
        internal Transform rig, pivot, chest, head, maskPivot, maskStrap, mouth, hatT, pupilL, pupilR, noseT, earL, earR;
        internal Transform shoulderL, shoulderR, elbowL, elbowR, handL, handR, hipL, hipR, kneeL, kneeR, footL, footR;
        internal Transform eyeL, eyeR, lidL, lidR, browL, browR, blushL, blushR;
        internal Transform[] mouths;
        internal int baseMouth, eyeStyle, browStyle;
        internal DiverLook look;
        internal int hat;
        internal float heightScale = 1f, mouthBaseScale = 1f;
        /// Q51 gear built by DiverGear (null when not built): lamp on the chest strap, camera on its lanyard, dive scooter.
        internal Transform strapLamp, strapCam, scooter, scooterProp, backGear;
        Transform finL, finR;
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
        // lying (round 3): incline of the torso (head up) and of the legs (feet up), hammock sway
        float lieHeadRise = 7f, lieFeetRise = 0f, lieHeadRiseCur = 7f, lieFeetRiseCur;
        bool lieHammock;

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
        float swimW, diveW, seatW, walkW, runW, ladderW, crouchW, lieW, carryW, selfieW, limpW, airW, fallW, talk, maskDown, maskNeck, t;
        Vector3 maskBase;
        bool maskBaseValid;
        float walkPhase, kickPhase, climbPhase, strokePhase, landSquash, prevSpeed, accelLean, blush;
        float blinkTimer = 3f, blinkT = 1f, lidL01, lidR01;
        float fidgetTimer = 12f, fidgetT = 1f, weightTimer = 5f, weightSide, weightCur;
        int fidgetKind;
        MotionState prevMotion;
        Quaternion qShL, qShR, qElL, qElR, qHipL, qHipR, qKnL, qKnR, qFtL, qFtR, qPivot, qChest;
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
            NameAnchor = DiverMeshes.Node(transform, "NameAnchor", new Vector3(0f, 2.15f * heightScale, 0f));
            finL = footL != null ? footL.Find("Fin") : null;
            finR = footR != null ? footR.Find("Fin") : null;
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

        /// Round 3: how the diver lies in MotionState.Lie – torso incline (head end up, degrees), leg incline (feet up)
        /// and whether it is a hammock (gentle sway, knees bent). NetAvatar / tests pass the sleep spot's values.
        public void SetLiePose(float headRise, float feetRise, bool hammock)
        {
            lieHeadRise = Mathf.Clamp(headRise, -10f, 40f);
            lieFeetRise = Mathf.Clamp(feetRise, -10f, 40f);
            lieHammock = hammock;
        }

        // ================================================================== lying geometry (shared with the camera)
        /// Head nod (degrees, chin toward the chest) while lying in a bed / hammock.
        public const float LieNodBed = 10f, LieNodHammock = 16f;
        const float LieKneeBed = 5f, LieKneeHammock = 14f;

        /// Root position / yaw that lays the diver on a sleep spot (pelvis on the mattress / cloth, head at the head end)
        /// plus the torso and leg inclines. Spots registered with PropBed.RegisterBed / RegisterHammock carry the exact
        /// values; older registrations (only the head-end eye `lie`) are derived.
        public static void LiePose(in SleepSpotInfo s, out Vector3 root, out float yaw, out float headRise, out float feetRise, out bool hammock)
        {
            hammock = s.kind == SleepSpotInfo.Hammock;
            yaw = s.yaw;
            if (s.hip != Vector3.zero)
            {
                root = s.hip;
                headRise = s.headRise;
                feetRise = s.feetRise;
                return;
            }
            Vector3 fwd = Quaternion.Euler(0f, s.yaw, 0f) * Vector3.forward;
            headRise = hammock ? 20f : 7f;
            feetRise = hammock ? 14f : 0f;
            root = new Vector3(s.lie.x, s.lie.y - (hammock ? 0.3f : 0.25f), s.lie.z) + fwd * 0.74f;
        }

        /// The eye position and face orientation of a diver lying at `root` (pelvis contact point) – the first-person
        /// camera while lying uses the same numbers as the avatar, so both agree.
        public static Vector3 LieEye(Vector3 root, float yaw, float headRise, bool hammock, float scale, out Quaternion face)
        {
            float nod = hammock ? LieNodHammock : LieNodBed;
            Quaternion torso = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(-90f + headRise, 0f, 0f);
            Vector3 pelvis = root + Vector3.up * (DiverBuilder.BackDepth * scale);
            Vector3 headPivot = pelvis + torso * new Vector3(0f, (DiverBuilder.HeadY - DiverBuilder.PivotY) * scale, 0f);
            face = torso * Quaternion.Euler(nod, 0f, 0f);
            return headPivot + face * new Vector3(0f, (DiverBuilder.EyeY - DiverBuilder.HeadY) * scale, 0.115f * scale);
        }

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
            // mask: over the eyes in the water, on the forehead on land – and with a hat on land it hangs around the
            // neck (round 3: the face stays readable)
            maskDown = Mathf.MoveTowards(maskDown, swimW > 0.5f ? 1f : 0f, dt * 2.5f);
            maskNeck = Mathf.MoveTowards(maskNeck, swimW < 0.5f && hat != 0 ? 1f : 0f, dt * 2.5f);
            lieHeadRiseCur = Mathf.Lerp(lieHeadRiseCur, lieHeadRise, 1f - Mathf.Exp(-6f * dt));
            lieFeetRiseCur = Mathf.Lerp(lieFeetRiseCur, lieFeetRise, 1f - Mathf.Exp(-6f * dt));

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

            // ---- pelvis
            float s = Mathf.Sin(walkPhase), c = Mathf.Cos(walkPhase);
            float land = 1f - swimW;
            float runK = runW * walkW, walkK = walkW * (1f - runW);
            float breathe = Mathf.Sin(time * (lieW > 0.5f ? 1.1f : 1.6f)) * 0.005f;
            float bobAmp = Mathf.Lerp(0.032f, 0.07f, runW) * walkW * land;
            float bob = (Mathf.Abs(c) - 0.55f) * bobAmp;
            float lieDrop = (DiverBuilder.PivotY - DiverBuilder.BackDepth) * lieW;
            float y = breathe + bob - 0.49f * seatW - 0.28f * crouchW - lieDrop - 0.05f * landSquash;
            float z = 0.13f * ladderW - 0.02f * seatW;
            float x = weightCur * 0.014f + c * 0.012f * walkK * land;
            pivot.localPosition = pivotBase + new Vector3(x, y, z);

            float lean = 9f * runK + 3f * walkK + accelLean + 14f * crouchW + 5f * ladderW + 5f * carryW * walkW;
            float hipYaw = s * Mathf.Lerp(7f, 10f, runW) * walkW * land;
            float hipRoll = c * 3f * walkW * land + weightCur * 2.5f;
            float undulate = Mathf.Sin(kickPhase) * (2f + 6f * runW) * diveW;
            float swimPitch = 80f * diveW + 30f * (swimW - diveW) + undulate + Mathf.Sin(time * 0.9f) * 2f * swimW;
            float pitch = swimPitch + lean * land - 4f * seatW;
            pitch = Mathf.Lerp(pitch, -90f + lieHeadRiseCur, lieW);
            float roll = hipRoll * (1f - lieW) + Mathf.Sin(time * 0.7f) * 1.2f * swimW + (lieHammock ? Mathf.Sin(time * 1.4f) * 1.6f * lieW : 0f);
            Quaternion pivotTarget = Quaternion.Euler(pitch, hipYaw * (1f - lieW), roll);
            if (limpW > 0.01f)
            {
                float sway = Mathf.Sin(time * 0.7f) * 6f;
                pivotTarget = Quaternion.Slerp(pivotTarget, Quaternion.Euler(18f + sway, 0f, 72f), limpW);
            }
            float k = Mathf.Lerp(10f, 18f, walkW);
            qPivot = poseInit ? Quaternion.Slerp(qPivot, pivotTarget, 1f - Mathf.Exp(-k * dt)) : pivotTarget;
            pivot.localRotation = qPivot;
            // footfall squash (run) + land squash; air stretch
            float foot = Mathf.Pow(Mathf.Abs(s), 6f) * 0.035f * runK;
            float squash = foot + 0.08f * landSquash - 0.05f * airW * (1f - fallW);
            pivot.localScale = new Vector3(1f + squash * 0.5f, 1f - squash, 1f + squash * 0.5f);

            // ---- chest on the spine: counter-rotating shoulders, a little forward lean, arched while diving
            if (chest != null)
            {
                float chestPitch = (2f + 5f * runW) * walkW * land - 10f * diveW - 6f * (swimW - diveW) + Mathf.Sin(time * 1.6f) * 0.8f * idleW
                                   + 4f * crouchW + 3f * seatW * Mathf.Sin(time * 0.6f);
                float chestYaw = -hipYaw * 1.7f * (1f - lieW);
                float chestRoll = -hipRoll * 0.6f;
                Quaternion ct = Quaternion.Euler(chestPitch, chestYaw, chestRoll);
                qChest = poseInit ? Quaternion.Slerp(qChest, ct, 1f - Mathf.Exp(-k * dt)) : ct;
                chest.localRotation = qChest;
            }

            // ---- legs
            float climbL = Mathf.Sin(climbPhase * Mathf.PI * 2f), climbR = -climbL;
            LegTarget(false, time, s, c, climbL, out Vector3 hipLE, out float kneeLA, out float footLA);
            LegTarget(true, time, s, c, climbR, out Vector3 hipRE, out float kneeRA, out float footRA);
            Set(ref qHipL, hipL, Quaternion.Euler(hipLE), dt, k);
            Set(ref qHipR, hipR, Quaternion.Euler(hipRE), dt, k);
            Set(ref qKnL, kneeL, Quaternion.Euler(kneeLA, 0f, 0f), dt, k);
            Set(ref qKnR, kneeR, Quaternion.Euler(kneeRA, 0f, 0f), dt, k);
            Set(ref qFtL, footL, Quaternion.Euler(footLA, 0f, 0f), dt, k);
            Set(ref qFtR, footR, Quaternion.Euler(footRA, 0f, 0f), dt, k);

            // ---- arms
            ArmTarget(true, rightItem, rod, time, s, climbR, fidget, out Vector3 shR, out float elR);
            ArmTarget(false, leftItem, rod, time, s, climbL, fidget, out Vector3 shL, out float elL);
            Set(ref qShR, shoulderR, Quaternion.Euler(shR), dt, k);
            Set(ref qShL, shoulderL, Quaternion.Euler(shL), dt, k);
            Set(ref qElR, elbowR, Quaternion.Euler(elR, 0f, 0f), dt, k);
            Set(ref qElL, elbowL, Quaternion.Euler(elL, 0f, 0f), dt, k);
            poseInit = true;

            // ---- head (stabilised against the walk twist), face
            if (head != null)
            {
                float hp = -26f * diveW - 14f * (swimW - diveW) + Mathf.Sin(time * 9f) * 3f * talk + Mathf.Sin(time * 0.5f) * 2f * idleW
                           - 12f * ladderW + 6f * crouchW + 20f * limpW + (fidgetKind == 2 ? 25f * fidget : 0f) - 6f * selfieW
                           + (lieHammock ? LieNodHammock : LieNodBed) * lieW + Mathf.Abs(c) * 2f * walkW * land;
                float hy = HeadYaw * (1f - limpW) * (1f - lieW) + Mathf.Sin(time * 0.37f) * 4f * idleW + (fidgetKind == 0 ? 28f * fidget : 0f)
                           - (hipYaw * -0.7f) * 0.9f * (1f - lieW);
                float hr = 6f * selfieW + (expression == (int)SelfieExpression.Laugh ? Mathf.Sin(time * 14f) * 4f : 0f) + 12f * limpW;
                head.localRotation = Quaternion.Euler(hp, hy, hr);
            }
            UpdateFace(dt, time);
            if (maskPivot != null)
            {
                if (!maskBaseValid) { maskBase = maskPivot.localPosition; maskBaseValid = true; }
                float n = maskNeck * maskNeck * (3f - 2f * maskNeck);
                // the neck pose: pivot at the collar (head space), glass tipped down onto the chest, strap tightened
                maskPivot.localPosition = Vector3.Lerp(maskBase, new Vector3(0f, -0.06f, 0f), n);
                maskPivot.localRotation = Quaternion.Slerp(Quaternion.Euler(Mathf.Lerp(-38f, 0f, maskDown), 0f, 0f), Quaternion.Euler(10f, 0f, 0f), n);
                if (maskStrap != null) maskStrap.localScale = new Vector3(Mathf.Lerp(1f, 0.6f, n), 1f, Mathf.Lerp(0.9f, 0.62f, n));
            }

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
                walkPhase = netPhase * TwoPi;
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

        void Set(ref Quaternion cur, Transform tr, Quaternion target, float dt, float rate = 12f)
        {
            if (tr == null) return;
            cur = poseInit ? Quaternion.Slerp(cur, target, 1f - Mathf.Exp(-rate * dt)) : target;
            tr.localRotation = cur;
        }

        /// Hip euler, knee bend (+ = flex) and foot pitch (+ = toes down) of one leg. Positive hip X swings the leg back.
        void LegTarget(bool right, float time, float s, float c, float climb, out Vector3 hipE, out float knee, out float foot)
        {
            float side = right ? 1f : -1f;
            // ---- land: walk / run (the right leg is half a cycle behind the left)
            float ls = right ? -s : s, lc = right ? -c : c;
            float hipAmp = Mathf.Lerp(26f, 44f, runW) * walkW * (1f - crouchW * 0.5f);
            float swing = Mathf.Max(0f, -lc);                         // swinging forward → knee flexes
            float kneeAmp = Mathf.Lerp(40f, 98f, runW) * walkW;
            float hipA = ls * hipAmp - 4f * runW * walkW;
            float kneeA = 5f + swing * kneeAmp + Mathf.Max(0f, ls) * 22f * runW * walkW;   // running: heel kicks up behind
            // idle: weight on one leg, the other relaxed
            float relax = Mathf.Max(0f, -weightCur * side);
            hipA += weightCur * side * 2.5f;
            kneeA += relax * 9f;
            float spread = 3.5f + 2f * relax;
            // feet stay roughly flat; a toe-off when the leg is far behind
            float footA = -(hipA + kneeA) * 0.6f + Mathf.Max(0f, ls - 0.6f) * 30f * walkW;

            // ---- water: flutter (dive) / dolphin (sprint) / frog kick (surface)
            float ph = kickPhase + (right ? Mathf.PI : 0f);
            float flutter = Mathf.Sin(ph);
            float fHip = flutter * 18f * (1f - limpW), fKnee = 10f + Mathf.Max(0f, Mathf.Sin(ph + 1.3f)) * 26f, fFoot = 72f + flutter * 10f;
            float dol = runW * diveW;
            fHip = Mathf.Lerp(fHip, Mathf.Sin(kickPhase) * 24f, dol);
            fKnee = Mathf.Lerp(fKnee, 12f + Mathf.Max(0f, Mathf.Sin(kickPhase + 1.3f)) * 34f, dol);
            float frog = 0.5f - 0.5f * Mathf.Cos(strokePhase * Mathf.PI * 2f);
            float sHip = -12f - 30f * frog, sKnee = 22f + 78f * frog, sFoot = 50f - 30f * frog, sSpread = 6f + 22f * frog;
            float dw = swimW > 0.01f ? Mathf.Clamp01(diveW / swimW) : 1f;
            float wHip = Mathf.Lerp(sHip, fHip, dw), wKnee = Mathf.Lerp(sKnee, fKnee, dw), wFoot = Mathf.Lerp(sFoot, fFoot, dw), wSpread = Mathf.Lerp(sSpread, 3f, dw);

            hipA = Mathf.Lerp(hipA, wHip, swimW);
            kneeA = Mathf.Lerp(kneeA, wKnee, swimW);
            footA = Mathf.Lerp(footA, wFoot, swimW);
            spread = Mathf.Lerp(spread, wSpread, swimW);

            // ---- full-body states on top
            if (crouchW > 0.01f)
            {
                hipA = Mathf.Lerp(hipA, -72f + ls * 14f * walkW, crouchW);
                kneeA = Mathf.Lerp(kneeA, 112f + swing * 20f * walkW, crouchW);
                footA = Mathf.Lerp(footA, -(-72f + 112f) * 0.85f, crouchW);
            }
            if (seatW > 0.01f)
            {
                float dangle = Mathf.Sin(time * 1.3f + (right ? 0.9f : 0f)) * 12f * LegDangle;
                hipA = Mathf.Lerp(hipA, -86f + dangle * 0.3f, seatW);
                kneeA = Mathf.Lerp(kneeA, 84f + dangle, seatW);
                footA = Mathf.Lerp(footA, 12f, seatW);
                spread = Mathf.Lerp(spread, 5f, seatW);
            }
            if (ladderW > 0.01f)
            {
                // alternating steps on the rungs, toes down
                hipA = Mathf.Lerp(hipA, -40f + climb * 20f, ladderW);
                kneeA = Mathf.Lerp(kneeA, 70f + climb * 26f, ladderW);
                footA = Mathf.Lerp(footA, 20f, ladderW);
                spread = Mathf.Lerp(spread, 4f, ladderW);
            }
            if (airW > 0.01f)
            {
                float flail = fallW * Mathf.Sin(time * 9f + (right ? Mathf.PI : 0f)) * 18f;
                hipA = Mathf.Lerp(hipA, -24f + flail, airW);
                kneeA = Mathf.Lerp(kneeA, 50f + flail * 0.6f, airW);
                footA = Mathf.Lerp(footA, 20f, airW);
            }
            if (lieW > 0.01f)
            {
                // the legs follow the bed / hammock: flexed by the torso + leg inclines, knees a little bent
                hipA = Mathf.Lerp(hipA, -(lieHeadRiseCur + lieFeetRiseCur) - (lieHammock ? LieKneeHammock * 0.5f : 0f), lieW);
                kneeA = Mathf.Lerp(kneeA, lieHammock ? LieKneeHammock : LieKneeBed, lieW);
                footA = Mathf.Lerp(footA, 28f, lieW);
                spread = Mathf.Lerp(spread, 5f, lieW);
            }
            if (limpW > 0.01f)
            {
                hipA = Mathf.Lerp(hipA, 6f + Mathf.Sin(time * 0.6f + side) * 5f, limpW);
                kneeA = Mathf.Lerp(kneeA, right ? 30f : 20f, limpW);
                footA = Mathf.Lerp(footA, 55f, limpW);
            }
            hipE = new Vector3(hipA, 0f, side * spread);
            knee = kneeA;
            foot = footA;
        }

        /// Shoulder euler + elbow bend for one arm. Negative X raises the arm forward, Z spreads it (right arm +).
        void ArmTarget(bool right, HoldItem item, bool rod, float time, float walkSin, float climb, float fidget, out Vector3 sh, out float el)
        {
            float side = right ? 1f : -1f;
            float armSwing = side * walkSin * Mathf.Lerp(24f, 50f, runW) * walkW;
            // land rest / walk (run: elbows bent ~88° and pumping)
            Vector3 landP = new Vector3(armSwing + Mathf.Sin(time * 1.1f + side) * 2f, -side * 6f * runW * walkW, side * (10f + Mathf.Sin(time * 1.6f) * 1.5f + 4f * runW * walkW));
            float landEl = Mathf.Lerp(-14f - Mathf.Max(0f, -armSwing) * 0.45f, -88f + armSwing * 0.3f, runW * walkW);
            // idle fidgets: adjust the mask (right), scratch the head (left), check the wrist computer (left)
            if (fidget > 0.01f)
            {
                if (fidgetKind == 0 && right) { landP = Vector3.Lerp(landP, new Vector3(-150f, 0f, 22f), fidget); landEl = Mathf.Lerp(landEl, -100f, fidget); }
                else if (fidgetKind == 1 && !right) { landP = Vector3.Lerp(landP, new Vector3(-150f, 0f, -42f), fidget); landEl = Mathf.Lerp(landEl, -112f + Mathf.Sin(time * 18f) * 10f, fidget); }
                else if (fidgetKind == 2 && !right) { landP = Vector3.Lerp(landP, new Vector3(-68f, 25f, -10f), fidget); landEl = Mathf.Lerp(landEl, -98f, fidget); }
            }
            // swimming: dive = arms trailing along the body (a slow scull); surface = breaststroke
            Vector3 swim = new Vector3(16f + Mathf.Sin(time * 1.4f + side) * 6f, 0f, side * (10f + Mathf.Sin(time * 1.4f) * 4f));
            float swimEl = -10f;
            float surf = swimW - diveW;
            if (surf > 0.01f)
            {
                float u = strokePhase;
                // reach forward → sweep out → pull in → recover
                float a = Mathf.Sin(u * Mathf.PI * 2f), b = Mathf.Cos(u * Mathf.PI * 2f);
                Vector3 stroke = new Vector3(-120f + 50f * b, 0f, side * (30f + 35f * a));
                float strokeEl = -40f - 35f * Mathf.Max(0f, -b);
                float sw = Mathf.Clamp01(surf / Mathf.Max(0.01f, swimW));
                swim = Vector3.Lerp(swim, stroke, sw);
                swimEl = Mathf.Lerp(swimEl, strokeEl, sw);
            }

            if (rod)
            {
                if (right) { landP = new Vector3(-48f, 0f, 16f); landEl = -58f; }
                else { landP = new Vector3(-62f, 22f, -4f); landEl = -40f; }
            }
            else if (Ukulele && seatW > 0.5f)
            {
                float strum = Mathf.Sin(time * 9f) * 9f;
                if (right) { landP = new Vector3(-38f + strum, -25f, 20f); landEl = -70f; }
                else { landP = new Vector3(-50f, -5f, -35f); landEl = -50f; }
            }
            else if (item == HoldItem.Camera)
            {
                landP = new Vector3(-72f, 0f, side * 8f); landEl = -38f;
                swim = new Vector3(-165f, 0f, side * 10f); swimEl = -25f;
            }
            else if (item == HoldItem.Flashlight)
            {
                landP = new Vector3(-80f, 0f, side * 6f); landEl = -12f;
                swim = new Vector3(-170f, 0f, side * 14f); swimEl = -8f;
            }
            else if (item == HoldItem.Stick)
            {
                // round 3: the stick rests ready over the shoulder (forearm up), held forward under water
                landP = new Vector3(-24f + armSwing * 0.3f, 0f, side * 14f); landEl = -96f;
                swim = new Vector3(-150f, 0f, side * 12f); swimEl = -18f;
            }
            else if (item == HoldItem.Spear)
            {
                // a staff at the side on land (arm a little forward), pointed ahead under water
                landP = new Vector3(-26f + armSwing * 0.3f, 0f, side * 16f); landEl = -50f;
                swim = new Vector3(-158f, 0f, side * 8f); swimEl = -14f;
            }
            else if (item == HoldItem.SpearGun || (item == HoldItem.None && (rightItem == HoldItem.SpearGun || leftItem == HoldItem.SpearGun)))
            {
                // two-handed speargun: the grip hand at the chest, the other one forward on the barrel
                bool grip = item == HoldItem.SpearGun;
                landP = grip ? new Vector3(-48f, 0f, side * 10f) : new Vector3(-62f, 0f, -side * 2f); landEl = grip ? -62f : -28f;
                swim = grip ? new Vector3(-150f, 0f, side * 6f) : new Vector3(-172f, 0f, -side * 4f); swimEl = grip ? -40f : -8f;
            }
            else if (item == HoldItem.Food || item == HoldItem.Tool)
            {
                landP = new Vector3(-45f, 0f, side * 10f); landEl = -64f;
                swim = new Vector3(-150f, 0f, side * 12f); swimEl = -30f;
            }

            // seated on a dock / boat: hands resting on the thighs
            if (seatW > 0.01f && !rod && !Ukulele && item == HoldItem.None)
            {
                Vector3 sit = new Vector3(-30f, 0f, side * 14f);
                landP = Vector3.Lerp(landP, sit, seatW);
                landEl = Mathf.Lerp(landEl, -44f, seatW);
            }
            sh = Vector3.Lerp(landP, swim, swimW);
            el = Mathf.Lerp(landEl, swimEl, swimW);

            // ---- full-body states on top
            if (carryW > 0.01f)
            {
                sh = Vector3.Lerp(sh, new Vector3(-68f, 0f, side * 8f), carryW);
                el = Mathf.Lerp(el, -32f, carryW);
            }
            if (crouchW > 0.01f && item == HoldItem.None && !rod)
            {
                sh = Vector3.Lerp(sh, new Vector3(-38f + armSwing * 0.4f, 0f, side * 18f), crouchW);
                el = Mathf.Lerp(el, -45f, crouchW);
            }
            if (ladderW > 0.01f)
            {
                // hands alternate on the rungs: reach up, grip, pull down
                Vector3 g = new Vector3(-148f + climb * 30f, 0f, side * 12f);
                sh = Vector3.Lerp(sh, g, ladderW);
                el = Mathf.Lerp(el, -40f - climb * 34f, ladderW);
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
                // round 3: relaxed arms beside the body on the mattress / cloth (no forearms sticking up)
                float drift = Mathf.Sin(time * 0.3f + side) * 2f;
                sh = Vector3.Lerp(sh, new Vector3(6f + drift, 0f, side * 13f), lieW);
                el = Mathf.Lerp(el, -14f, lieW);
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
                case SelfieExpression.Wow: mouthShape = 5; lidL = lidR = -84f; browUp = 0.009f; pupilScale = 1.12f; break;
                case SelfieExpression.Scared: mouthShape = 6; lidL = lidR = -86f; browUp = 0.012f; pupilScale = 0.72f; break;
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
            if (mouth != null) mouth.localScale = new Vector3(1f - talk * 0.15f, 1f + talk * 1.4f, 1f) * mouthBaseScale;

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
            bool resting = lieW > 0.5f;
            Show(strapLamp, ownsLamp && !lamp && !resting);
            Show(strapCam, ownsCam && !cam && !resting);
            Show(backGear, !resting);   // nobody sleeps with the tank on (it would poke through the hammock cloth)
            Show(finL, !resting);       // … or with fins
            Show(finR, !resting);
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
                Show(camModel, cam && !rod && !resting);
                if (cam && !rod && !resting)
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
                bool show = lamp && !rod && !resting;
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
                bool show = rod && !resting;
                Show(rodModel, show);
                if (show) rodModel.SetPositionAndRotation(handR.position + handR.rotation * new Vector3(0f, -0.03f, 0f), yawRoot.rotation * Quaternion.Euler(-52f + Mathf.Sin(time * 0.8f) * 2f, 4f, 0f));
            }
            if (ukeModel != null)
            {
                bool show = Ukulele && seatW > 0.5f;
                Show(ukeModel, show);
                Transform body = chest != null ? chest : pivot;
                if (show) ukeModel.SetPositionAndRotation(body.position + body.rotation * new Vector3(0.02f, 0.04f * heightScale, 0.24f * heightScale), body.rotation * Quaternion.Euler(0f, 0f, 58f));
            }
            UpdateRound3Items(yawRoot, aimPitch, time);
        }

        // round 3 held items of co-op avatars (stick, spear, speargun, food, small tools)
        Transform stickModel, spearModel, gunModel, foodModel, toolModel;

        void UpdateRound3Items(Transform yawRoot, float aimPitch, float time)
        {
            float under = swimW;
            // stick ready over the shoulder on land (pointing up-back), forward under water
            PlaceHeld(ref stickModel, HoldItem.Stick, yawRoot, Mathf.Lerp(-118f, aimPitch, under), new Vector3(0f, -0.01f, 0.02f));
            // spear: a staff (point up) on land, aimed forward under water
            PlaceHeld(ref spearModel, HoldItem.Spear, yawRoot, Mathf.Lerp(-78f, aimPitch, under), new Vector3(0f, -0.02f, 0f));
            PlaceHeld(ref gunModel, HoldItem.SpearGun, yawRoot, Mathf.Lerp(14f, aimPitch, under), new Vector3(0f, -0.03f, 0.03f));
            PlaceHeld(ref foodModel, HoldItem.Food, yawRoot, 0f, new Vector3(0f, -0.02f, 0.04f));
            PlaceHeld(ref toolModel, HoldItem.Tool, yawRoot, 0f, new Vector3(0f, -0.02f, 0.04f));
        }

        Transform BuildHeld(HoldItem item)
        {
            switch (item)
            {
                case HoldItem.Stick: return BuildStick();
                case HoldItem.Spear: return BuildSpear();
                case HoldItem.SpearGun: return BuildSpeargun();
                case HoldItem.Food: return BuildFoodItem();
                default: return BuildToolItem();
            }
        }

        void PlaceHeld(ref Transform model, HoldItem item, Transform yawRoot, float pitch, Vector3 offset)
        {
            bool held = rightItem == item || leftItem == item;
            if (held && model == null) model = BuildHeld(item);
            if (model == null) return;
            bool show = held && limpW < 0.5f && lieW < 0.5f;
            Show(model, show);
            if (!show) return;
            Transform h = rightItem == item ? handR : handL;
            if (h == null) return;
            model.SetPositionAndRotation(h.position + h.rotation * offset, yawRoot.rotation * Quaternion.Euler(pitch, 0f, 0f));
        }

        /// A knobbly driftwood stick (~0.8 m, two bends, a knot and a twig), gripped near its lower end.
        Transform BuildStick()
        {
            var r = ItemRoot("AvatarStick");
            Material wood = MaterialLibrary.Opaque(LowPoly.Hex("#A27650"), 0.12f, 0f), bark = M(LowPoly.Hex("#6E4A2E"));
            var a = DiverMeshes.Node(r, "A", new Vector3(0f, 0f, -0.12f), new Vector3(90f, 0f, 0f));
            DiverMeshes.Part(a, "Seg", DiverMeshes.TaperCapsule(0.021f, 0.018f, 0.36f, 8, 2), wood, Vector3.zero, Vector3.one);
            var b = DiverMeshes.Node(a, "B", new Vector3(0f, 0.33f, 0f), new Vector3(-9f, 0f, 6f));
            DiverMeshes.Part(b, "Seg", DiverMeshes.TaperCapsule(0.018f, 0.013f, 0.32f, 8, 2), wood, Vector3.zero, Vector3.one);
            var cN = DiverMeshes.Node(b, "C", new Vector3(0f, 0.3f, 0f), new Vector3(12f, 0f, -5f));
            DiverMeshes.Part(cN, "Seg", DiverMeshes.TaperCapsule(0.013f, 0.008f, 0.16f, 6, 2), wood, Vector3.zero, Vector3.one);
            DiverMeshes.Part(b, "Knot", DiverMeshes.Sphere(4, 8), bark, new Vector3(0.012f, 0.12f, 0f), new Vector3(0.03f, 0.04f, 0.03f), default, false);
            DiverMeshes.Part(b, "Twig", DiverMeshes.TaperCapsule(0.005f, 0.002f, 0.09f, 5, 1), bark, new Vector3(0f, 0.2f, 0f), Vector3.one, new Vector3(0f, 0f, -42f), false);
            return r;
        }

        /// A spear: shaft with a leather grip wrap and a steel leaf tip with a collar.
        Transform BuildSpear()
        {
            var r = ItemRoot("AvatarSpear");
            Material shaft = MaterialLibrary.Opaque(LowPoly.Hex("#B88A5A"), 0.15f, 0f), tip = MaterialLibrary.Opaque(LowPoly.Hex("#C8CED6"), 0.5f, 0.6f);
            Material wrap = M(LowPoly.Hex("#6E4A2E")), band = M(LowPoly.Hex("#3FB8C4"));
            var axis = DiverMeshes.Node(r, "Axis", new Vector3(0f, 0f, -0.55f), new Vector3(90f, 0f, 0f));
            DiverMeshes.Part(axis, "Shaft", DiverMeshes.TaperCapsule(0.014f, 0.012f, 1.62f, 8, 2), shaft, Vector3.zero, Vector3.one);
            DiverMeshes.Part(axis, "Wrap", DiverMeshes.Taper(0.0165f, 0.0165f, 8, false), wrap, new Vector3(0f, 0.45f, 0f), new Vector3(1f, 0.2f, 1f), default, false);
            DiverMeshes.Part(axis, "Band", DiverMeshes.Torus(0.015f, 0.004f, 8, 3), band, new Vector3(0f, 1.5f, 0f), Vector3.one, default, false);
            DiverMeshes.Part(axis, "Tip", DiverMeshes.Cached("spear_leaf", () => DiverMeshes.LatheY(new[] { new Vector2(0f, 0f), new Vector2(0.016f, 0.01f), new Vector2(0.026f, 0.06f), new Vector2(0.016f, 0.12f), new Vector2(0f, 0.17f) }, 6)),
                tip, new Vector3(0f, 1.6f, 0f), new Vector3(1f, 1f, 0.45f), default, false);
            return r;
        }

        /// The speargun: a dark barrel, a bright grip with a trigger, two rubber bands at the muzzle, the spear on top.
        Transform BuildSpeargun()
        {
            var r = ItemRoot("AvatarSpeargun");
            Material body = MaterialLibrary.Opaque(LowPoly.Hex("#2E3140"), 0.3f, 0f), accent = M(LowPoly.Hex("#EE7F2D"));
            Material steel = MaterialLibrary.Opaque(LowPoly.Hex("#C8CED6"), 0.5f, 0.6f), rubber = M(LowPoly.Hex("#3A1E2E"));
            var axis = DiverMeshes.Node(r, "Axis", new Vector3(0f, 0f, -0.12f), new Vector3(90f, 0f, 0f));
            DiverMeshes.Part(axis, "Barrel", DiverMeshes.TaperCapsule(0.024f, 0.02f, 0.66f, 10, 2), body, Vector3.zero, Vector3.one);
            DiverMeshes.Part(axis, "Muzzle", DiverMeshes.Sphere(4, 8), body, new Vector3(0f, 0.64f, 0f), new Vector3(0.05f, 0.04f, 0.06f), default, false);
            for (int s = -1; s <= 1; s += 2)
                DiverMeshes.Part(axis, "Band", DiverMeshes.Torus(0.03f, 0.006f, 10, 3), rubber, new Vector3(s * 0.012f, 0.6f, -0.02f), Vector3.one, new Vector3(0f, 0f, 90f + s * 12f), false);
            DiverMeshes.Part(axis, "Spear", DiverMeshes.TaperCapsule(0.005f, 0.005f, 0.86f, 6, 1), steel, new Vector3(0f, 0.05f, -0.03f), Vector3.one, default, false);
            DiverMeshes.Part(axis, "Barb", DiverMeshes.Cached("gun_barb", () => DiverMeshes.LatheY(new[] { new Vector2(0f, 0f), new Vector2(0.01f, 0.006f), new Vector2(0f, 0.05f) }, 6)), steel, new Vector3(0f, 0.9f, -0.03f), Vector3.one, default, false);
            DiverMeshes.Part(r, "Grip", DiverMeshes.SmoothCapsule(0.017f, 0.1f, 10, 3, 1), accent, new Vector3(0f, -0.075f, -0.03f), Vector3.one, new Vector3(-18f, 0f, 0f), false);
            DiverMeshes.Part(r, "Trigger", DiverMeshes.Torus(0.016f, 0.004f, 8, 3), body, new Vector3(0f, -0.03f, 0.02f), Vector3.one, new Vector3(0f, 0f, 90f), false);
            return r;
        }

        /// Food in the hand: a coconut with a straw.
        Transform BuildFoodItem()
        {
            var r = ItemRoot("AvatarFood");
            DiverMeshes.Part(r, "Nut", DiverMeshes.Sphere(6, 10), MaterialLibrary.Opaque(LowPoly.Hex("#7A5236"), 0.15f, 0f), new Vector3(0f, 0.03f, 0f), Vector3.one * 0.1f, default, false);
            DiverMeshes.Part(r, "Cut", DiverMeshes.Taper(0.028f, 0.028f, 10, true), M(LowPoly.Hex("#F4EFE6")), new Vector3(0f, 0.075f, 0f), new Vector3(1f, 0.004f, 1f), default, false);
            DiverMeshes.Part(r, "Straw", DiverMeshes.SmoothCapsule(0.004f, 0.1f, 6, 1, 1), M(LowPoly.Hex("#E0607E")), new Vector3(0.008f, 0.07f, 0f), Vector3.one, new Vector3(0f, 0f, -18f), false);
            return r;
        }

        /// A tool / bottle in the hand.
        Transform BuildToolItem()
        {
            var r = ItemRoot("AvatarTool");
            Material glass = MaterialLibrary.Opaque(LowPoly.Hex("#9FD8E8"), 0.7f, 0f);
            DiverMeshes.Part(r, "Bottle", DiverMeshes.SmoothCapsule(0.026f, 0.13f, 10, 3, 1), glass, new Vector3(0f, -0.02f, 0f), Vector3.one, default, false);
            DiverMeshes.Part(r, "Neck", DiverMeshes.SmoothCapsule(0.011f, 0.05f, 8, 2, 1), glass, new Vector3(0f, 0.1f, 0f), Vector3.one, default, false);
            DiverMeshes.Part(r, "Cap", DiverMeshes.Taper(0.012f, 0.012f, 8, true), M(LowPoly.Hex("#2E3140")), new Vector3(0f, 0.145f, 0f), new Vector3(1f, 0.012f, 1f), default, false);
            return r;
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
