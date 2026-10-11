using UnityEngine;

namespace Sealebrities
{
    /// Round 2b body states of the first-person diver:
    ///  * CROUCH (L.6): hold Down (Ctrl) on land – capsule 1.2 m, eye 1.0 m, speed ×0.45; you only stand up where the
    ///    head has room. Squeezes behind secrets are 0.95 × 1.25 m. Walking into a low opening shows „Strg – Ducken“.
    ///  * LYING (L.9): while lying on a co-op sleep spot (PropInfo.LocalSleepSpot, SleepVote.LocalLying) the camera lies
    ///    down at the spot looking up; moving or jumping gets up (SleepVote.SetLying(false)).
    ///  * CURRENTS (K.3): PropInfo.CurrentAt pushes the diver (swimming and diving).
    ///  * MOTION STATE (L.13): published every frame for the co-op avatar (NetLocalAvatar.PublishMotion) and used by the
    ///    selfie puppet – Idle / Walk / Run / Jump / Fall / Land / Ladder / Swim / Dive / Sit / Carry / Selfie / Crouch
    ///    with the cycle phase (two steps / one stroke / two rungs per cycle).
    public partial class PlayerController
    {
        public const float CrouchHeight = 1.2f;
        public const float CrouchEyeHeight = 1.0f;

        bool crouching;
        float crouchHintTimer;
        bool crouchHint;
        string crouchPrompt, crouchPromptKey;
        Language crouchPromptLang;

        MotionState motionNow;
        float motionPhase, landFlash;

        // lying (L.9)
        float lieYawOff, liePitchOff;
        bool lyingCam;

        /// Crouching on land right now.
        public bool IsCrouching => crouching;
        /// Lying on a co-op sleep spot (L.9): the camera lies down, the hands hide.
        public bool IsLyingDown => lyingCam;
        /// L.13 motion state published this frame.
        public MotionState CurrentMotion => motionNow;
        public float MotionPhase01 => motionPhase;

        float CurHeight => cc != null ? cc.height : Height;

        /// „Strg halten – Ducken“ while walking into a low opening (null otherwise).
        public string CrouchPrompt
        {
            get
            {
                if (!crouchHint || crouching) return null;
                string key = GameInput.BindingLabel(GameAction.Down);
                if (!ReferenceEquals(key, crouchPromptKey) || crouchPromptLang != Loc.Current || crouchPrompt == null)
                {
                    crouchPromptKey = key;
                    crouchPromptLang = Loc.Current;
                    crouchPrompt = key + Loc.Tr(" halten – Ducken", " hold – Crouch");
                }
                return crouchPrompt;
            }
        }

        // ------------------------------------------------------------------ crouch
        void SetCrouch(bool on)
        {
            crouching = on;
            if (cc == null) return;
            float h = on ? CrouchHeight : Height;
            cc.height = h;
            cc.center = new Vector3(0f, h * 0.5f, 0f);
        }

        /// Room above a crouching diver to stand up (only the extra head space is tested).
        bool CanStandUp()
        {
            Vector3 p = transform.position;
            return !Physics.CheckCapsule(p + Vector3.up * (CrouchHeight - Radius + 0.02f), p + Vector3.up * (Height - Radius), Radius * 0.9f,
                                         Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        /// Land movement hook: crouch on Down, stand up when there is room. Returns the speed factor.
        float UpdateCrouch(bool input, Vector2 mv, float dt)
        {
            bool want = input && GameInput.Descend && !seated && !ladderClimbing;
            if (want != crouching)
            {
                if (want) SetCrouch(true);
                else if (CanStandUp()) SetCrouch(false);
            }
            // walking into a low opening: hint the crouch key
            crouchHintTimer -= dt;
            if (crouchHintTimer <= 0f)
            {
                crouchHintTimer = 0.2f;
                crouchHint = false;
                if (!crouching && grounded && mv.y > 0.3f)
                {
                    Vector3 p = transform.position;
                    Vector3 f = YawForward();
                    int mask = Physics.DefaultRaycastLayers;
                    bool headBlocked = Physics.SphereCast(p + Vector3.up * 1.45f, 0.18f, f, out _, 0.75f, mask, QueryTriggerInteraction.Ignore);
                    bool lowFree = !Physics.SphereCast(p + Vector3.up * 0.75f, 0.24f, f, out _, 0.9f, mask, QueryTriggerInteraction.Ignore);
                    crouchHint = headBlocked && lowFree;
                }
            }
            return crouching ? 0.45f : 1f;
        }

        // ------------------------------------------------------------------ lying on a co-op sleep spot (L.9)
        bool LyingOnSpot(out SleepSpotInfo spot)
        {
            spot = default;
            int i = PropInfo.LocalSleepSpot;
            if (!SleepVote.LocalLying || i < 0 || i >= PropInfo.SleepSpots.Count) return false;
            spot = PropInfo.SleepSpots[i];
            return true;
        }

        /// While lying: no walking; moving / jumping gets up. Returns true when the input was consumed.
        bool UpdateLying(bool input)
        {
            if (!LyingOnSpot(out _))
            {
                if (lyingCam) { lyingCam = false; lieYawOff = liePitchOff = 0f; }
                return false;
            }
            if (!lyingCam)
            {
                lyingCam = true;
                lieYawOff = 0f;
                liePitchOff = 0f;
                Hands.StowAll();
            }
            if (input && (GameInput.Move.sqrMagnitude > 0.25f || GameInput.JumpPressed))
            {
                SleepVote.SetLying(false);
                PropInfo.LocalSleepSpot = -1;
                lyingCam = false;
                return true;
            }
            return true;
        }

        /// Camera pose while lying: at the head end of the spot, looking up toward the feet (mouse looks around ±60°).
        void ApplyLyingCamera()
        {
            if (!lyingCam || !LyingOnSpot(out SleepSpotInfo s) || camT == null) return;
            Quaternion q = Quaternion.Euler(0f, s.yaw + lieYawOff, 0f) * Quaternion.Euler(-62f + liePitchOff, 0f, 0f);
            camT.SetPositionAndRotation(s.lie, q);
        }

        // ------------------------------------------------------------------ currents (K.3)
        /// Flow at the diver (zero on land / seated / on a ladder).
        Vector3 CurrentPush()
        {
            if (!isInWater || seated || ladderClimbing || unconscious) return Vector3.zero;
            if (PropInfo.Currents.Count == 0) return Vector3.zero;
            return PropInfo.CurrentAt(transform.position);
        }

        // ------------------------------------------------------------------ motion state (L.13)
        void UpdateMotionState(float dt)
        {
            MotionState s;
            float spd;
            float cycleMeters;   // distance per full cycle; ≤ 0 = time based
            Vector3 v = measuredVel;
            float planar = new Vector2(v.x, v.z).magnitude;
            landFlash = Mathf.Max(0f, landFlash - dt);
            if (seated) { s = MotionState.Sit; spd = 0f; cycleMeters = 0f; }
            else if (OnGenericLadder) { s = MotionState.Ladder; spd = Mathf.Abs(v.y); cycleMeters = -1f; }
            else if (selfie) { s = MotionState.Selfie; spd = isInWater ? v.magnitude : planar; cycleMeters = 1.1f; }
            else if (lyingCam) { s = MotionState.Lie; spd = 0f; cycleMeters = 0f; }
            else if (Carry.IsCarrying) { s = MotionState.Carry; spd = isInWater ? v.magnitude : planar; cycleMeters = isInWater ? 1.6f : 1.0f; }
            else if (isInWater)
            {
                bool dive = mode == PlayerMoveMode.Dive || isUnderwater;
                s = dive ? MotionState.Dive : MotionState.Swim;
                spd = v.magnitude;
                cycleMeters = dive ? 1.6f : 1.4f;
            }
            else if (crouching) { s = MotionState.Crouch; spd = planar; cycleMeters = 0.8f; }
            else if (!grounded && vel.y > 0.5f) { s = MotionState.Jump; spd = planar; cycleMeters = 0f; }
            else if (!grounded && vel.y < -2.6f) { s = MotionState.Fall; spd = planar; cycleMeters = 0f; }
            else if (landFlash > 0f) { s = MotionState.Land; spd = planar; cycleMeters = 0f; }
            else if (planar > 4.6f) { s = MotionState.Run; spd = planar; cycleMeters = -2f; }
            else if (planar > 0.25f) { s = MotionState.Walk; spd = planar; cycleMeters = -2f; }
            else { s = MotionState.Idle; spd = planar; cycleMeters = 0f; }

            if (s == MotionState.Ladder) motionPhase = ladCycle;
            else if (cycleMeters <= -1.5f)
            {
                // the puppet's cadence: stride grows with speed, max 4.2 steps/s → one cycle = two steps
                float stride = Mathf.Clamp(0.45f + spd * 0.12f, 0.5f, 1.25f);
                float steps = Mathf.Min(Mathf.Max(0.2f, spd) / stride, 4.2f);
                motionPhase = Mathf.Repeat(motionPhase + dt * steps * 0.5f, 1f);
            }
            else if (cycleMeters > 0f) motionPhase = Mathf.Repeat(motionPhase + spd * dt / cycleMeters, 1f);
            else motionPhase = Mathf.Repeat(motionPhase + dt * 0.25f, 1f);
            motionNow = s;
            NetLocalAvatar.PublishMotion(s, spd, motionPhase);
        }
    }
}
