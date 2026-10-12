using UnityEngine;

namespace Sealebrities
{
    /// L.13 generic ladders: the player climbs every ladder the props publish in PropInfo.Ladders (dock, ravine,
    /// lighthouse rope ladder to the north beach, maintenance ladder, radio tower, hut roof, tree house, canoe rack,
    /// jetty, wreck island …). Space / E within 1.2 m while facing it grabs it (from below, or from the platform while
    /// looking over the edge); W (or Space held) / S climb at 0.65 / 0.8 m/s with an eased rise per 0.30 m rung, E lets
    /// go; grabbing with Space from below (e.g. out of the water) climbs all the way up by itself; the top
    /// ends in a 0.55 s mantle onto the platform, the bottom steps off (or lets go into the water). Held items are
    /// stowed and restored afterwards; both first-person mittens grip the rungs (PlayerHands), the avatar shows the
    /// Ladder motion state (NetLocalAvatar). Once ladders exist the props' fade-teleport fallback (PropLadder) steps
    /// aside (PropInfo.LaddersHandledByPlayer).
    public partial class PlayerController
    {
        const float LadderGrab = 1.2f;              // horizontal reach to the rail line
        const float LadderUpSpeed = 0.65f, LadderDownSpeed = 0.8f;
        const float LadderStandOff = 0.40f;         // root ↔ rail line: the eye stays ~0.4 m from the rungs
        const float LadderMantleBelowTop = 1.05f;   // root height below the top stand where the mantle starts

        bool genLadder;
        LadderInfo lad;
        int ladStage;                 // 0 attach, 1 climb, 2 mantle (top), 3 step off (bottom)
        float ladT, ladDur;
        Vector3 ladFrom, ladTo;
        float ladH, ladShownH, ladMinY, ladMaxY, ladYaw, ladCycle;
        int ladRung;
        bool ladIgnoreInput, ladWasInWater, ladFromTop;
        int ladSavedRight = -1, ladSavedLeft = -1;
        float ladProbeTimer;
        int ladCandidate = -1;
        bool ladCandidateTop;
        float ladderRoll;
        string ladPromptUp, ladPromptDown, ladPromptRopeUp, ladPromptRopeDown, ladPromptKey;
        Language ladPromptLang;

        /// The local player is on one of the generic ladders right now.
        public bool OnGenericLadder => ladderClimbing && genLadder;
        /// 0..1 cycle of the alternating hands (one cycle = two rungs) – PlayerHands, NetLocalAvatar.
        public float LadderCycle01 => ladCycle;
        /// The ladder being climbed (valid while OnGenericLadder).
        public LadderInfo CurrentLadder => lad;
        /// The mantle onto the platform is running (both hands on the top rail).
        public bool LadderMantling => OnGenericLadder && ladStage == 2;
        /// Root height the climb shows right now (world y).
        public float LadderRootY => ladShownH;

        /// "E – Hochklettern" / "E – Runterklettern" while standing at a ladder on land (null when none).
        public string LadderLandPrompt
        {
            get
            {
                if (ladCandidate < 0 || isInWater || ladderClimbing) return null;
                string key = GameInput.BindingLabel(GameAction.Interact);
                if (!ReferenceEquals(key, ladPromptKey) || ladPromptLang != Loc.Current || ladPromptUp == null)
                {
                    ladPromptKey = key;
                    ladPromptLang = Loc.Current;
                    ladPromptUp = key + Loc.Tr(" – Hochklettern", " – Climb up");
                    ladPromptDown = key + Loc.Tr(" – Runterklettern", " – Climb down");
                    ladPromptRopeUp = key + Loc.Tr(" – Strickleiter hoch", " – Climb the rope ladder");
                    ladPromptRopeDown = key + Loc.Tr(" – Strickleiter runter", " – Climb down the rope ladder");
                }
                bool rope = ladCandidate < PropInfo.Ladders.Count && PropInfo.Ladders[ladCandidate].kind == LadderInfo.Rope;
                if (ladCandidateTop) return rope ? ladPromptRopeDown : ladPromptDown;
                return rope ? ladPromptRopeUp : ladPromptUp;
            }
        }

        // ------------------------------------------------------------------ finding a ladder
        /// Every 0.15 s: the nearest ladder the player could grab right now (bottom approach or from the top).
        void UpdateLadderProbe(float dt)
        {
            if (PropInfo.Ladders.Count > 0 && !PropInfo.LaddersHandledByPlayer) PropInfo.LaddersHandledByPlayer = true;
            if (ladderClimbing || seated || vaulting || IsFainting || rescueActive || selfie)
            {
                ladCandidate = -1;
                return;
            }
            ladProbeTimer -= dt;
            if (ladProbeTimer > 0f) return;
            ladProbeTimer = 0.15f;
            ladCandidate = FindLadder(out ladCandidateTop);
        }

        int FindLadder(out bool fromTop)
        {
            fromTop = false;
            var list = PropInfo.Ladders;
            if (list.Count == 0) return -1;
            Vector3 p = transform.position;
            Vector3 fwd = YawForward();
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < list.Count; i++)
            {
                LadderInfo l = list[i];
                if (!l.Usable) continue;   // e.g. the lighthouse cellar ladder while the trapdoor is still shut
                Vector3 o = l.outward;
                float dx = p.x - l.bottom.x, dz = p.z - l.bottom.z;
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                // ---- from below: next to the rails, on their outer side, facing the wall (in the water: any side)
                float side = dx * o.x + dz * o.z;
                bool below = p.y < l.top.y - 0.6f && p.y > l.bottom.y - 1.7f;
                // side > −0.45: on the cliff platform between the two rope ladders the lower ladder's top stand lies
                // 0.25 m behind the upper ladder's rail line – with −0.25 the upper half could not be grabbed from there
                if (below && d < LadderGrab && side > -0.45f)
                {
                    float face = -(fwd.x * o.x + fwd.z * o.z);
                    if (face > 0.35f || (isInWater && face > -0.3f))
                    {
                        if (d < bestD) { bestD = d; best = i; fromTop = false; }
                        continue;
                    }
                }
                // ---- from the top: standing at the top stand, looking out over the edge
                float tx = p.x - l.top.x, tz = p.z - l.top.z;
                float td = Mathf.Sqrt(tx * tx + tz * tz);
                if (!isInWater && Mathf.Abs(p.y - l.top.y) < 0.9f && td < 1.0f)
                {
                    float look = fwd.x * o.x + fwd.z * o.z;
                    if (look > 0.45f && td < bestD) { bestD = td; best = i; fromTop = true; }
                }
            }
            return best;
        }

        /// Index of the usable ladder whose rail foot is horizontally nearest to p (within maxDist), −1 = none.
        int NearestLadder(Vector3 p, float maxDist)
        {
            var list = PropInfo.Ladders;
            int best = -1;
            float bestD = maxDist * maxDist;
            for (int i = 0; i < list.Count; i++)
            {
                var l = list[i];
                if (!l.Usable) continue;
                float dx = l.bottom.x - p.x, dz = l.bottom.z - p.z;
                float d = dx * dx + dz * dz;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// Grabs the ladder found by the probe (E on land, Space/E in the water). False when none is in reach.
        /// autoUp: climbs up by itself (S or E take over) – no gameplay caller uses it any more (W / Space held climbs).
        public bool TryClimbLadder(bool autoUp = false)
        {
            if (ladderClimbing || seated || vaulting || IsFainting || rescueActive || cc == null || !cc.enabled) return false;
            int idx = FindLadder(out bool top);
            if (idx < 0) return false;
            StartGenericLadder(idx, top);
            ladAuto = autoUp && !top;
            return true;
        }

        bool ladAuto;

        void StartGenericLadder(int idx, bool fromTop)
        {
            lad = PropInfo.Ladders[idx];
            if (lad.rungSpacing < 0.1f) lad.rungSpacing = 0.3f;
            ladAuto = false;
            // stow what is in the hands (restored at the end)
            ladSavedRight = Hands.ActiveSlot(Hand.Right);
            ladSavedLeft = Hands.ActiveSlot(Hand.Left);
            Hands.StowAll();
            if (selfie) ExitSelfie();
            if (crouching) SetCrouch(false);

            genLadder = true;
            ladderClimbing = true;
            cc.enabled = false;
            vel = Vector3.zero;
            canVaultHint = false;
            canLadderHint = false;
            sprintingUnderwater = false;
            Vector3 o = lad.outward;
            ladYaw = Mathf.Atan2(-o.x, -o.z) * Mathf.Rad2Deg;
            ladMinY = lad.bottom.y;
            ladMaxY = Mathf.Max(ladMinY + 0.2f, lad.top.y - LadderMantleBelowTop);
            Vector3 p = transform.position;
            ladH = fromTop ? ladMaxY : Mathf.Clamp(p.y, ladMinY, ladMaxY);
            ladShownH = ladH;
            ladRung = Mathf.FloorToInt((ladH - ladMinY) / lad.rungSpacing);
            ladFrom = p;
            ladTo = RailPoint(ladH);
            ladStage = 0;
            ladT = 0f;
            ladFromTop = fromTop;
            // from the water / ground: a short reach for the rungs (longer when the grab point is further away)
            ladDur = fromTop ? 0.7f : Mathf.Clamp(Vector3.Distance(ladFrom, ladTo) * 0.45f, 0.22f, 0.5f);
            ladIgnoreInput = true;
            ladWasInWater = isInWater;
            if (mode == PlayerMoveMode.Dive) mode = PlayerMoveMode.Surface;
            landDip = Mathf.Max(landDip, 0.03f);
            LadderGrabSound(1f);
        }

        Vector3 RailPoint(float rootY) => new Vector3(lad.bottom.x, rootY, lad.bottom.z) + lad.outward * LadderStandOff;

        // ------------------------------------------------------------------ climbing
        void UpdateGenericLadder(float dt)
        {
            // attach / mantle / step-off turn toward the ladder; while climbing the mouse looks around (±80°, UpdateLook)
            if (ladStage != 1) yaw = Mathf.MoveTowardsAngle(yaw, ladYaw, 360f * dt);
            switch (ladStage)
            {
                case 0:   // attach
                {
                    ladT += dt / Mathf.Max(0.05f, ladDur);
                    Vector3 pos;
                    if (ladFromTop)
                    {
                        // round 3: from the top first step out over the edge (at platform height, turning to face the
                        // ladder), then lower onto the rungs – the straight line cut through the planks / railing
                        float a = Smooth01(ladT / 0.5f), b = Smooth01((ladT - 0.4f) / 0.6f);
                        pos = new Vector3(Mathf.Lerp(ladFrom.x, ladTo.x, a), Mathf.Lerp(ladFrom.y, ladTo.y, b) + Mathf.Sin(a * Mathf.PI) * 0.05f * (1f - b),
                                          Mathf.Lerp(ladFrom.z, ladTo.z, a));
                    }
                    else pos = Vector3.Lerp(ladFrom, ladTo, Smooth01(ladT));
                    transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
                    if (ladT >= 1f) { ladStage = 1; ladT = 0f; }
                    break;
                }
                case 1:   // climb
                {
                    // W / Space (held) up, S down, E lets go; releasing stops (ladAuto only for scripted callers)
                    float mv = 0f;
                    bool letGo = false;
                    if (!ladIgnoreInput && !UIState.BlocksGameplay)
                    {
                        mv = GameInput.Move.y;
                        // Space grabs the ladder (Jump) – holding that same key climbs, also when Jump and Swim up are bound apart
                        if (GameInput.Ascend || GameInput.Held(GameAction.Jump)) mv = Mathf.Max(mv, 1f);
                        if (mv < -0.1f) ladAuto = false;
                        letGo = GameInput.InteractPressed;
                        if (letGo) ladAuto = false;
                    }
                    if (ladAuto && mv <= 0.1f) mv = 1f;
                    ladIgnoreInput = false;
                    if (letGo) { LetGoOfLadder(); return; }
                    float v = mv > 0f ? LadderUpSpeed * mv : LadderDownSpeed * mv;
                    if (!UIState.BlocksGameplay && GameInput.Sprint) v *= 1.6f;   // long ladders (29 m rope ladder): Shift hurries
                    ladH = Mathf.Clamp(ladH + v * dt, ladMinY, ladMaxY);
                    if (mv > 0.1f && ladH >= ladMaxY - 0.0005f) { BeginMantle(); return; }
                    if (mv < -0.1f && ladH <= ladMinY + 0.0005f) { StepOffBottom(); return; }

                    // rise per rung with an ease (stop-and-pull), not linear
                    float sp = lad.rungSpacing;
                    float u = (ladH - ladMinY) / sp;
                    float k = Mathf.Floor(u);
                    float f = u - k;
                    float eased = k + f * f * (3f - 2f * f);
                    ladShownH = Mathf.Min(ladMaxY, ladMinY + eased * sp);
                    ladCycle = Mathf.Repeat((ladShownH - ladMinY) / (2f * sp), 1f);
                    int rung = (int)k;
                    if (rung != ladRung)
                    {
                        ladRung = rung;
                        LadderGrabSound(Mathf.Abs(v) > 0.01f ? 0.8f : 0.4f);
                    }
                    // the body keeps facing the ladder; only the head (yaw on the root) follows the mouse within ±80°
                    transform.SetPositionAndRotation(RailPoint(ladShownH), Quaternion.Euler(0f, yaw, 0f));
                    // wet drips: leaving the water on the ladder
                    if (ladWasInWater && !isInWater)
                    {
                        ladWasInWater = false;
                        FXSfx.Play(FXSound.Splash, 0.2f);
                    }
                    break;
                }
                case 2:   // mantle onto the platform
                {
                    ladT += dt / 0.55f;
                    float rise = Smooth01(ladT / 0.6f);
                    float across = Smooth01((ladT - 0.35f) / 0.65f);
                    transform.SetPositionAndRotation(new Vector3(Mathf.Lerp(ladFrom.x, ladTo.x, across), Mathf.Lerp(ladFrom.y, ladTo.y, rise),
                        Mathf.Lerp(ladFrom.z, ladTo.z, across)), Quaternion.Euler(0f, yaw, 0f));
                    landDip = Mathf.Max(landDip, Mathf.Sin(Mathf.Clamp01(ladT) * Mathf.PI) * 0.04f);
                    if (ladT >= 1f) EndGenericLadder(ladTo, false);
                    break;
                }
                default:   // 3: step off at the bottom
                {
                    ladT += dt / 0.3f;
                    transform.SetPositionAndRotation(Vector3.Lerp(ladFrom, ladTo, Smooth01(ladT)), Quaternion.Euler(0f, yaw, 0f));
                    if (ladT >= 1f) EndGenericLadder(ladTo, false);
                    break;
                }
            }
            pitch = Mathf.Clamp(pitch, -50f, 60f);
        }

        void BeginMantle()
        {
            ladStage = 2;
            ladT = 0f;
            ladFrom = transform.position;
            ladTo = lad.top + Vector3.up * 0.06f;
            ladCycle = 0f;
            LadderGrabSound(1f);
        }

        void StepOffBottom()
        {
            float water = Waves.HeightAt(lad.bottom.x, lad.bottom.z);
            if (ladMinY < water - 0.3f)
            {
                // the foot of the ladder is in the water: just let go and swim
                LetGoOfLadder();
                return;
            }
            ladStage = 3;
            ladT = 0f;
            ladFrom = transform.position;
            ladTo = RailPoint(ladMinY) + lad.outward * 0.45f + Vector3.up * 0.05f;
        }

        void LetGoOfLadder()
        {
            Vector3 push = lad.outward * 1.2f + Vector3.up * 0.3f;
            EndGenericLadder(transform.position + lad.outward * 0.1f, true);
            vel = push;
        }

        void EndGenericLadder(Vector3 at, bool released)
        {
            ladderClimbing = false;
            genLadder = false;
            ladCycle = 0f;
            transform.position = at;
            if (released)
            {
                cc.enabled = true;
                Physics.SyncTransforms();
                RecheckWaterAfterTeleport();
            }
            else
            {
                FinishClimb();
                RecheckWaterAfterTeleport();
            }
            RestoreLadderHands();
        }

        void RestoreLadderHands()
        {
            if (ladSavedRight >= 0 && Hands.Active(Hand.Right) == HoldItem.None) TryReselect(Hand.Right, ladSavedRight);
            if (ladSavedLeft >= 0 && Hands.Active(Hand.Left) == HoldItem.None) TryReselect(Hand.Left, ladSavedLeft);
            ladSavedRight = ladSavedLeft = -1;
        }

        void TryReselect(Hand h, int slot)
        {
            HoldItem item = Hands.SlotItem(slot);
            if (item == HoldItem.None || !Hands.SlotUsable(slot) || Hands.SlotHand(slot) != h) return;
            if (item == HoldItem.FishingRod && isInWater) return;   // the rod does not go swimming
            Hands.Select(slot);
        }

        /// Called from ResetTransientState / seats / teleports: drop off the ladder without restoring anything.
        void CancelGenericLadder()
        {
            genLadder = false;
            ladCycle = 0f;
            ladSavedRight = ladSavedLeft = -1;
        }

        void LadderGrabSound(float vol)
        {
            switch (lad.kind)
            {
                case LadderInfo.Rope: FXSfx.Play(FXSound.Equip, 0.16f * vol); break;
                case LadderInfo.Metal: FXSfx.Play(FXSound.Click, 0.32f * vol); break;
                default: FXSfx.Play(FXSound.Click, 0.22f * vol); break;
            }
        }

        /// Camera roll ±1.5° alternating with the hands (head-bob toggle).
        float LadderCameraRoll(float dt)
        {
            float target = OnGenericLadder && ladStage == 1 ? Mathf.Sin(ladCycle * Mathf.PI * 2f) * 1.5f : 0f;
            ladderRoll = Mathf.Lerp(ladderRoll, target, 1f - Mathf.Exp(-10f * dt));
            return ladderRoll;
        }
    }
}
