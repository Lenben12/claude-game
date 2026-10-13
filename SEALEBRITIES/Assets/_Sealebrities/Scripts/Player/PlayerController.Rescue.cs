using UnityEngine;

namespace Sealebrities
{
    /// F.1 co-op rescue, player side (Normal/Hard, 2+ players in a session). Replication, the tow pairs, the "{name}
    /// hat dich gerettet!" / "Gerettet!" notifications, +1 rescue and the achievement live in the Net module's NetRescue;
    /// this file owns the body:
    ///  * out of air under water → UNCONSCIOUS for DifficultySettings.unconsciousSeconds (30 s Normal / 20 s Hard):
    ///    no control, hands stowed, the body floats up at 0.25 m/s with the head slumped (IsFainting is true, so every
    ///    module treats the diver as incapacitated and predators leave it alone);
    ///  * a friend within 2 m: "E – Abschleppen" (PlayerInteractor asks TryGetTowPrompt) → the towed body follows
    ///    1.2 m behind the tower at its head height (NetRescue.TryGetTowAnchor) and is dragged free when it gets caught
    ///    on coral or rock; the tower swims ×0.7 and uses air ×1.2 (Movement/Air);
    ///  * the head reaches 0.8 m below the surface → revived on this machine: control back, 30 % air (or a full
    ///    breath without a tank), the dive's clips are kept, no rescue fee;
    ///  * countdown over, or every other diver unconscious too → the normal faint (footage lost, fee, rescue point).
    /// Solo and offline nothing changes; Relaxed keeps the rescue drone.
    public partial class PlayerController
    {
        /// Float-up speed of an unconscious diver (F.1).
        const float UnconsciousRise = 0.25f;
        /// Air after a revive with a tank (F.1: 30 %).
        const float ReviveAir01 = 0.3f;
        /// A towed body farther than this from its place behind the tower (stuck on geometry) is dragged free.
        const float TowDragFreeDistance = 2.5f;

        bool unconscious;
        float unconsciousLeft, towStuck;
        string towPrompt, letGoPrompt, towPromptKey;
        Language towPromptLang;

        /// The local diver is unconscious and can be towed (F.1).
        public bool IsUnconscious => unconscious;
        /// Seconds until the unconscious phase turns into a normal faint (0 when conscious) – for the UI countdown.
        public float UnconsciousSecondsLeft => unconscious ? Mathf.Max(0f, unconsciousLeft) : 0f;
        /// The local diver tows a friend right now (swim ×0.7, air ×1.2).
        public bool IsTowing => Net.IsOnline && NetRescue.LocalTowTarget >= 0;

        /// Test hook (AutoPlaytestMP F.1): empties the tank under water so the real "out of air" path runs (unconscious in
        /// a session on Normal/Hard). Returns true when the diver is unconscious now.
        public bool DebugKnockOut()
        {
            if (!initialized || menuMode || !isUnderwater || IsFainting) return false;
            airLeft = 0f;
            if (!CanGoUnconscious()) return false;
            StartUnconscious();
            return unconscious;
        }

        /// Out of air under water in a session: unconscious instead of an instant faint?
        bool CanGoUnconscious()
        {
            if (!Net.IsOnline || Net.PlayerCount < 2 || !diff.canFaint || diff.unconsciousSeconds <= 0f) return false;
            return !EveryoneElseUnconscious();
        }

        static bool EveryoneElseUnconscious()
        {
            var players = Net.Players;
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (p == null || p.id == Net.LocalId) continue;
                if (!NetRescue.IsUnconscious(p.id)) return false;
            }
            return true;
        }

        void StartUnconscious()
        {
            if (unconscious || fainting) return;
            unconscious = true;
            unconsciousLeft = Mathf.Max(1f, diff.unconsciousSeconds);
            sprintingUnderwater = false;
            vel *= 0.3f;
            if (NetRescue.LocalTowTarget >= 0) NetRescue.StopTow();
            Hands.StowAll();
            NetRescue.SetLocalUnconscious(true, unconsciousLeft);
            GameEvents.RaiseBanner(Loc.Tr("Bewusstlos – ein Freund kann dich retten!", "Unconscious – a friend can save you!"), new Color(0.86f, 0.22f, 0.24f));
            trauma = Mathf.Max(trauma, 0.3f);
        }

        /// Called every gameplay frame after UpdateAir.
        void UpdateRescue(float dt)
        {
            // ---- tower: let go automatically when climbing out, sitting down or passing out yourself
            if (Net.IsOnline && NetRescue.LocalTowTarget >= 0 && (unconscious || fainting || seated || !isInWater || vaulting || ladderClimbing || rescueActive))
                NetRescue.StopTow();

            if (!unconscious) return;
            unconsciousLeft -= dt;
            if (!Net.IsOnline)
            {
                // the session ended while we were out: the normal faint
                EndUnconsciousIntoFaint();
                return;
            }
            if (!isInWater || depth < NetRescue.ReviveDepth)
            {
                Revive(NetRescue.LocalTowedBy);
                return;
            }
            if (unconsciousLeft <= 0f || EveryoneElseUnconscious()) EndUnconsciousIntoFaint();
        }

        void Revive(int rescuerId)
        {
            if (!unconscious) return;
            unconscious = false;
            NetRescue.ReportRevived(rescuerId);   // events, "{name} hat dich gerettet!", the rescuer's reward
            if (rescuerId < 0) GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr("Wieder wach!", "Awake again!"));
            airLeft = gear.hasTank ? airCapacity * ReviveAir01 : airCapacity;
            lowAirWarned = false;
            emptyWarned = false;
            tooDeepWarned = false;
            vel = Vector3.zero;
            trauma = 0f;
            if (isInWater && mode == PlayerMoveMode.Dive && depth < 0.45f)
            {
                mode = PlayerMoveMode.Surface;
                lastWaveValid = false;
            }
        }

        void EndUnconsciousIntoFaint()
        {
            if (!unconscious) return;
            unconscious = false;
            NetRescue.SetLocalUnconscious(false, 0f);
            StartFaint();
        }

        void ResetRescue()
        {
            if (unconscious)
            {
                unconscious = false;
                NetRescue.SetLocalUnconscious(false, 0f);
            }
            unconsciousLeft = 0f;
            if (Net.IsOnline && NetRescue.LocalTowTarget >= 0) NetRescue.StopTow();
        }

        /// Unconscious body: floats up slowly, or follows the friend who tows it (1.2 m behind the tower's head).
        void MoveUnconscious(float dt)
        {
            if (cc == null || !cc.enabled) return;
            Vector3 p = transform.position;
            Vector3 want;
            bool towed = false;
            Vector3 target = p;
            if (NetRescue.TryGetTowAnchor(out Vector3 anchorHead, out Quaternion anchorRot))
            {
                Vector3 eyeOffset = head != null ? head.position - p : Vector3.up * eyeHeight;
                target = anchorHead - eyeOffset;
                want = Vector3.ClampMagnitude((target - p) * 4f, 5f);
                yaw = Mathf.MoveTowardsAngle(yaw, anchorRot.eulerAngles.y, 150f * dt);
                towed = true;
            }
            else
            {
                float t = Time.time;
                want = new Vector3(Mathf.Sin(t * 0.31f) * 0.05f, UnconsciousRise, Mathf.Cos(t * 0.27f) * 0.05f);
            }
            vel = Vector3.Lerp(vel, want, 1f - Mathf.Exp(-3f * dt));
            cc.Move(vel * dt);
            // caught on coral / a rock while the friend swims on: the tow drags the body free (no rescue fails on geometry;
            // the anchor lies on the tower's own path through open water)
            if (towed) SampleTowTrail(target, dt);
            else towTrailN = 0;
            Vector3 off = target - transform.position;
            if (towed && off.sqrMagnitude > TowDragFreeDistance * TowDragFreeDistance)
            {
                towStuck += dt;
                if (towStuck > 0.5f) DragFree(target, dt);
            }
            else towStuck = 0f;
            ProbeGround();
            platform = null;
            platformRb = null;
            bobAmp = Mathf.Lerp(bobAmp, 0f, 1f - Mathf.Exp(-8f * dt));
            // the head slumps forward
            pitch = Mathf.MoveTowards(pitch, 28f, 25f * dt);
        }

        // ---- drag-free without tunnelling (the tow anchor can lie inside a wall when the tower faces away from it)
        readonly Vector3[] towTrail = new Vector3[8];
        int towTrailN, towTrailHead;
        float towTrailT;
        const float TowSkin = 0.03f;

        void CapsuleAt(Vector3 pos, out Vector3 a, out Vector3 b, out float r)
        {
            r = cc.radius * 0.95f;
            float half = Mathf.Max(0f, cc.height * 0.5f - cc.radius);
            Vector3 c = pos + cc.center;
            a = c - Vector3.up * half;
            b = c + Vector3.up * half;
        }

        bool CapsuleFree(Vector3 pos)
        {
            CapsuleAt(pos, out Vector3 a, out Vector3 b, out float r);
            return !Physics.CheckCapsule(a, b, r, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        /// Every 0.25 s while towed: remember where the body could be (free capsule along the tower's path).
        void SampleTowTrail(Vector3 target, float dt)
        {
            towTrailT -= dt;
            if (towTrailT > 0f) return;
            towTrailT = 0.25f;
            bool was = cc.enabled;
            if (was) cc.enabled = false;
            bool free = CapsuleFree(target);
            if (was) cc.enabled = true;
            if (!free) return;
            towTrail[towTrailHead] = target;
            towTrailHead = (towTrailHead + 1) % towTrail.Length;
            if (towTrailN < towTrail.Length) towTrailN++;
        }

        /// The newest remembered free point of the tow trail (false = none yet).
        bool LastFreeTrailPoint(out Vector3 p)
        {
            for (int i = 1; i <= towTrailN; i++)
            {
                Vector3 q = towTrail[(towTrailHead - i + towTrail.Length) % towTrail.Length];
                if (CapsuleFree(q)) { p = q; return true; }
            }
            p = default;
            return false;
        }

        /// Moves the stuck body toward the anchor (or, when the anchor itself is inside geometry, toward the last free
        /// point of the tower's trail) – capsule-cast so it never ends inside a collider; never re-enables the controller
        /// in an overlapping capsule.
        void DragFree(Vector3 target, float dt)
        {
            cc.enabled = false;
            Vector3 p = transform.position;
            if (!CapsuleFree(target) && LastFreeTrailPoint(out Vector3 free)) target = free;
            Vector3 off = target - p;
            float dist = off.magnitude;
            if (dist > 1e-4f)
            {
                float stepLen = Mathf.Min(dist, Mathf.Max(3f, dist * 2f) * dt);
                Vector3 dir = off / dist;
                CapsuleAt(p, out Vector3 a, out Vector3 b, out float r);
                if (Physics.CapsuleCast(a, b, r, dir, out RaycastHit hit, stepLen + TowSkin, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    stepLen = Mathf.Max(0f, hit.distance - TowSkin);
                p += dir * stepLen;
            }
            if (!CapsuleFree(p))
            {
                // already overlapping (pushed in earlier): jump to the newest free trail point, else the anchor itself
                if (LastFreeTrailPoint(out Vector3 q)) p = q;
                else if (CapsuleFree(target)) p = target;
            }
            transform.position = p;
            cc.enabled = true;
        }

        // ------------------------------------------------------------------ tower side (PlayerInteractor)
        /// "E – Abschleppen" next to an unconscious friend, "E – Loslassen" while towing. False = nothing to offer.
        public bool TryGetTowPrompt(out string prompt)
        {
            prompt = null;
            if (!Net.IsOnline || unconscious || fainting || seated || !isInWater || IsBusy || head == null) return false;
            if (NetRescue.LocalTowTarget < 0 && !NetRescue.FindTowable(head.position, NetRescue.TowRange, out _, out _)) return false;
            string key = GameInput.BindingLabel(GameAction.Interact);
            if (!ReferenceEquals(key, towPromptKey) || towPromptLang != Loc.Current || towPrompt == null)
            {
                towPromptKey = key;
                towPromptLang = Loc.Current;
                towPrompt = key + Loc.Tr(" – Abschleppen", " – Tow");
                letGoPrompt = key + Loc.Tr(" – Loslassen", " – Let go");
            }
            prompt = NetRescue.LocalTowTarget >= 0 ? letGoPrompt : towPrompt;
            return true;
        }

        /// E: grab the nearest unconscious friend, or let go.
        public void ToggleTow()
        {
            if (!Net.IsOnline || unconscious || fainting || head == null) return;
            if (NetRescue.LocalTowTarget >= 0)
            {
                NetRescue.StopTow();
                return;
            }
            if (NetRescue.FindTowable(head.position, NetRescue.TowRange, out int id, out _) && NetRescue.StartTow(id))
                FXSfx.Play(FXSound.Equip, 0.7f);
        }
    }
}
