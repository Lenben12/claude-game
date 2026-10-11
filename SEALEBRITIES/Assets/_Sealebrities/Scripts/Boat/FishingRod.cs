using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// The fishing rod as a two-handed hotbar item (HoldItem.FishingRod, How to Fish style). Shown as a first-person
    /// view model while it is in an active hotbar slot; the Player module's hands hold it (they look for the camera
    /// descendant named "FishingRod", see FishingVisuals.BuildViewRod). The rod stays under the camera the whole time
    /// it is held (lowered while the phone is up / climbing) so the hands never lose it.
    /// Controls (GameAction.Use, LMB by default): hold to charge → release to cast (0.35 s wind-up over the shoulder,
    /// 0.25 s whip), the bobber flies 4–17 m depending on the charge. After FishMarket.BiteDelay the bobber is pulled
    /// under (splash + plop): click within the strike window to hook, the fish is reeled in, dangles on the line and
    /// goes into the shared cooler box (FishMarket.RollCatch → FishMarket.AddCatch; it is NOT sold – the phone's market
    /// app sells it). Clicking while waiting reels the line back in. Works on the dock, beach, rocks and the boat.
    [DefaultExecutionOrder(80)]
    public class FishingRodTool : MonoBehaviour
    {
        public enum RodState { Hidden, Ready, Charging, Casting, Waiting, Bite, Reeling, Landed, Retrieving }

        /// Current state of the local player's rod (UI may show a power bar while Charging).
        public static RodState State { get; private set; }
        /// 0..1 cast power while charging, -1 otherwise.
        public static float Charge01 { get; private set; } = -1f;
        /// True while the rod view model exists under the player camera (the rod is in the player's hands).
        public static bool ViewModelVisible { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { State = RodState.Hidden; Charge01 = -1f; ViewModelVisible = false; }

        // ------------------------------------------------------------------ tuning
        const float WindUpTime = 0.35f, WhipTime = 0.25f, ChargeTime = 1.1f, SettleTime = 0.35f;
        const float MinCast = 4f, MaxCast = 17f;
        const float ReleaseAt = 0.6f;            // fraction of the whip when the bobber leaves the tip
        const float HangLength = 0.36f;          // bobber dangling below the tip
        const float FishHang = 0.55f;            // line length between tip and a landed fish
        const float LandedTime = 1.35f, RetrieveTime = 0.55f;
        const float EquipTime = 0.25f, StowTime = 0.2f;
        const float WindUpPitch = -58f, WhipPitch = 34f, FishingPitch = 21f;

        static readonly System.Random Rng = new System.Random();

        // ------------------------------------------------------------------ objects
        RodViewModel vm;
        Transform bobber;
        LineRenderer line;
        readonly Vector3[] linePts = new Vector3[16];
        ParticleSystem splash;
        GameObject catchGo;
        float catchLength;

        // ------------------------------------------------------------------ state
        RodState st = RodState.Hidden;
        float stT, charge, castPower, castDist, flightTime, releaseT, whipFrom;
        bool released, targetWater;
        Vector3 castFrom, castTarget;
        float biteTimer, nibbleTimer, nibble, strikeTimer;
        float equip01, pitch, yawOff, tipBend, crankDeg, reelSoundTimer, splashTimer, idleTime, phoneBlend;
        Vector3 bobPos, bobPrev, retrieveFrom, fishFrom;
        bool bobValid;
        float blockedUntil;
        FishItem hooked;
        bool hookedCommitted;
        float reelTime;
        string promptWritten;
        int lastTier = -2, cachedTier;
        bool tierDirty = true;

        readonly KeyPrompt promptCast = new KeyPrompt(GameAction.Use, "Auswerfen (halten)", "Cast (hold)");
        readonly KeyPrompt promptReel = new KeyPrompt(GameAction.Use, "Einholen", "Reel in");
        readonly KeyPrompt promptStrike = new KeyPrompt(GameAction.Use, "Anschlagen!", "Strike!");
        static readonly string[] BarDe = BuildBars("Wurf "), BarEn = BuildBars("Cast ");

        static string[] BuildBars(string prefix)
        {
            var a = new string[11];
            for (int i = 0; i <= 10; i++) a[i] = prefix + new string('|', i) + new string('·', 10 - i);
            return a;
        }

        // ------------------------------------------------------------------ creation
        public static FishingRodTool Create(Transform parent)
        {
            var go = new GameObject("FishingRodTool");
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<FishingRodTool>();
            t.Init();
            return t;
        }

        void Init()
        {
            bobber = FishingVisuals.BuildBobber(transform);
            bobber.gameObject.SetActive(false);

            var lg = new GameObject("FishingLine");
            lg.transform.SetParent(transform, false);
            line = lg.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = linePts.Length;
            line.widthMultiplier = 0.006f;
            line.numCapVertices = 0;
            line.sharedMaterial = BoatMats.Line;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;

            splash = BoatParticles.Splash(transform);

            GameEvents.GameStarted += OnGameStarted;
            GameEvents.ReturnedToMenu += OnReturnedToMenu;
            GameEvents.GearChanged += OnGearChanged;
        }

        void OnDestroy()
        {
            GameEvents.GameStarted -= OnGameStarted;
            GameEvents.ReturnedToMenu -= OnReturnedToMenu;
            GameEvents.GearChanged -= OnGearChanged;
            ReleasePrompt();
            DestroyViewModel();
            if (catchGo != null) Destroy(catchGo);
            ViewModelVisible = false;
            State = RodState.Hidden;
            Charge01 = -1f;
        }

        void OnGameStarted()
        {
            tierDirty = true;
            ResetAll(false);
        }

        void OnReturnedToMenu() => ResetAll(false);

        /// The equipped rod may have changed: re-read its tier (the view model is rebuilt when it differs).
        void OnGearChanged() => tierDirty = true;

        void DestroyViewModel()
        {
            if (vm != null && vm.root != null) Destroy(vm.root.gameObject);
            vm = null;
        }

        // ------------------------------------------------------------------ helpers
        static IPlayer LocalPlayer
        {
            get
            {
                var p = Refs.Player;
                if (p == null || (p is Object o && o == null)) return null;
                return p;
            }
        }

        static int RodTier()
        {
            var gm = GameManager.I;
            if (gm == null || gm.State == null) return 0;
            var rod = GearDatabase.Equipped(gm.State, GearSlot.Rod);
            return rod != null ? Mathf.Clamp(rod.tier, 0, 2) : 0;
        }

        Vector3 TipPos
        {
            get
            {
                if (vm != null && vm.tip != null) return vm.tip.position;
                var p = LocalPlayer;
                if (p != null && p.View != null) return p.View.transform.position + p.View.transform.forward * 1.2f + Vector3.up * 0.6f;
                return transform.position;
            }
        }

        // ------------------------------------------------------------------ frame: logic
        void Update()
        {
            float dt = Time.deltaTime;
            var gm = GameManager.I;
            var p = LocalPlayer;
            Camera view = p != null ? p.View : null;
            bool inGame = gm != null && gm.InGame && !UIState.MainMenuOpen && !UIState.LoadingOpen;
            bool held = inGame && view != null && !p.IsSeated && Hands.IsHeld(HoldItem.FishingRod);
            bool busy = p is PlayerController pcBusy && pcBusy != null && pcBusy.IsBusy;   // climbing out, rescue
            // raised in front of the player; while the phone is up both hands hold the phone, so the rod is lowered out
            // of view (the session – line and bobber – stays alive, so fish can be listed while waiting for a bite)
            bool raised = held && !p.IsFainting && !busy && !UIState.PhoneOpen;
            bool canFish = raised && !p.IsInWater && !p.IsUnderwater;

            // stowed, jumped into the water, fainted …: drop the cast (a hooked fish is kept). The phone keeps it.
            bool session = st != RodState.Hidden && st != RodState.Ready && st != RodState.Charging;
            if (session && (!held || p.IsInWater || p.IsUnderwater || p.IsFainting || busy)) CancelSession();

            // equip / stow animation
            float target = raised ? 1f : 0f;
            equip01 = Mathf.MoveTowards(equip01, target, dt / (target > equip01 ? EquipTime : StowTime));
            bool show = view != null && inGame && (held || equip01 > 0.001f);
            if (show)
            {
                if (tierDirty) { cachedTier = RodTier(); tierDirty = false; }
                int tier = cachedTier;
                if (vm == null || vm.root == null || tier != lastTier)
                {
                    DestroyViewModel();
                    vm = FishingVisuals.BuildViewRod(view.transform, tier);
                    lastTier = tier;
                    bobValid = false;
                }
                if (vm.root.parent != view.transform) vm.root.SetParent(view.transform, false);
                if (!vm.root.gameObject.activeSelf) { vm.root.gameObject.SetActive(true); bobValid = false; }
                if (st == RodState.Hidden) EnterState(RodState.Ready);
            }
            else
            {
                if (vm != null && vm.root != null && vm.root.gameObject.activeSelf) vm.root.gameObject.SetActive(false);
                if (st != RodState.Hidden) ResetAll(true);
            }
            ViewModelVisible = show;

            if (st == RodState.Hidden) { Charge01 = -1f; State = st; return; }

            stT += dt;
            bool blocked = UIState.BlocksGameplay;
            if (blocked) blockedUntil = Time.unscaledTime + 0.2f;
            bool input = canFish && !blocked && Time.unscaledTime >= blockedUntil && equip01 >= 0.999f;

            switch (st)
            {
                case RodState.Ready:
                    if (input && GameInput.UsePressed) StartCharge();
                    break;
                case RodState.Charging:
                    charge = Mathf.Clamp01((stT - 0.08f) / ChargeTime);
                    if (!canFish || blocked) EnterState(RodState.Ready);          // phone opened mid-swing: no cast
                    else if (!GameInput.UseHeld) StartCast();
                    break;
                case RodState.Casting: UpdateCasting(); break;
                case RodState.Waiting:
                    if (input && GameInput.UsePressed) { StartRetrieve(); break; }
                    UpdateWaiting(dt);
                    break;
                case RodState.Bite:
                    if (input && GameInput.UsePressed) { Hook(); break; }
                    strikeTimer -= dt;
                    if (strikeTimer <= 0f)
                    {
                        GameEvents.RaiseToast(Loc.Tr("Weg ist er …", "It got away …"));
                        StartRetrieve();
                    }
                    break;
                case RodState.Reeling: UpdateReeling(dt); break;
                case RodState.Landed:
                    if (stT >= LandedTime) FinishLanded();
                    break;
                case RodState.Retrieving:
                    if (stT >= RetrieveTime) EnterState(RodState.Ready);
                    break;
            }

            // the line snaps when the player walks / sails away from the bobber
            if ((st == RodState.Waiting || st == RodState.Bite) && (bobber.position - TipPos).sqrMagnitude > SnapDist() * SnapDist())
            {
                GameEvents.RaiseToast(Loc.Tr("Schnur gerissen!", "The line snapped!"));
                bobValid = false;
                EnterState(RodState.Ready);
            }

            State = st;
            Charge01 = st == RodState.Charging ? charge : -1f;

            // pose the rod now (camera-local), so the Player module's hands (LateUpdate) grip this frame's pose
            if (st != RodState.Hidden && vm != null && vm.root != null && vm.root.gameObject.activeSelf && dt > 0f) PoseRig(dt);
        }

        float SnapDist() => Mathf.Max(28f, castDist + 10f);

        void EnterState(RodState s)
        {
            st = s;
            stT = 0f;
            if (s == RodState.Ready || s == RodState.Hidden)
            {
                charge = 0f;
                if (catchGo != null) { Destroy(catchGo); catchGo = null; }
                hooked = null;
                hookedCommitted = false;
            }
        }

        /// Ends the current cast at once (a hooked fish still goes into the cooler box).
        void CancelSession()
        {
            if (hooked != null && !hookedCommitted) CommitCatch(false);
            if (catchGo != null) { Destroy(catchGo); catchGo = null; }
            bobValid = false;
            EnterState(RodState.Ready);
        }

        void ResetAll(bool keepVisualState)
        {
            if (catchGo != null) { Destroy(catchGo); catchGo = null; }
            hooked = null;
            hookedCommitted = false;
            charge = 0f;
            bobValid = false;
            st = RodState.Hidden;
            stT = 0f;
            State = RodState.Hidden;
            Charge01 = -1f;
            if (!keepVisualState) equip01 = 0f;
            if (bobber != null) bobber.gameObject.SetActive(false);
            if (line != null) line.enabled = false;
            if (vm != null && vm.root != null && !keepVisualState) vm.root.gameObject.SetActive(false);
            ReleasePrompt();
        }

        // ------------------------------------------------------------------ casting
        void StartCharge()
        {
            EnterState(RodState.Charging);
            charge = 0f;
            BoatSynth.PlayAt(BoatSynth.Reel, TipPos, 0.12f, 2.1f, 8f);
        }

        void StartCast()
        {
            castPower = Mathf.Max(0.12f, charge);
            whipFrom = pitch;
            released = false;
            EnterState(RodState.Casting);
        }

        void UpdateCasting()
        {
            if (!released && stT >= WhipTime * ReleaseAt)
            {
                released = true;
                releaseT = stT;
                ChooseTarget();
                castFrom = bobValid ? bobPos : TipPos;
                float d = Vector3.Distance(new Vector3(castFrom.x, 0f, castFrom.z), new Vector3(castTarget.x, 0f, castTarget.z));
                flightTime = 0.45f + d * 0.035f;
                BoatSynth.PlayAt(BoatSynth.Reel, castFrom, 0.3f, 1.8f, 15f);
            }
            if (!released) return;
            float s = Mathf.Clamp01((stT - releaseT) / flightTime);
            if (s < 1f) return;
            // landed
            if (targetWater)
            {
                Vector3 w = new Vector3(castTarget.x, Waves.HeightAt(castTarget.x, castTarget.z), castTarget.z);
                BoatParticles.EmitAt(splash, w, 12);
                BoatSynth.PlayAt(BoatSynth.Plop, w, 0.6f, 1f, 30f);
                EnterState(RodState.Waiting);
                biteTimer = FishMarket.BiteDelay(Rng);
                nibbleTimer = Mathf.Lerp(1.5f, 3.5f, (float)Rng.NextDouble());
                nibble = 0f;
            }
            else
            {
                BoatSynth.PlayAt(BoatSynth.Thunk, castTarget, 0.25f, 1.6f, 20f);
                GameEvents.RaiseToast(Loc.Tr("Da ist kein Wasser.", "That's not water."));
                StartRetrieve();
            }
        }

        void ChooseTarget()
        {
            var p = LocalPlayer;
            Vector3 origin = p != null && p.Root != null ? p.Root.position : transform.position;
            Vector3 fwd = p != null && p.View != null ? p.View.transform.forward : Vector3.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = p != null && p.Root != null ? p.Root.forward : Vector3.forward;
            fwd.y = 0f;
            fwd.Normalize();
            castDist = Mathf.Lerp(MinCast, MaxCast, castPower) * (1f + 0.08f * Mathf.Max(0, cachedTier));
            targetWater = false;
            for (float d = castDist; d >= 2.5f; d -= 0.75f)
            {
                Vector3 c = origin + fwd * d;
                if (!IsWater(c)) continue;
                castTarget = new Vector3(c.x, Waves.SeaLevel, c.z);
                castDist = d;
                targetWater = true;
                return;
            }
            Vector3 e = origin + fwd * castDist;
            float y = BoatSurface.Below(new Vector3(e.x, Mathf.Max(origin.y, Waves.SeaLevel) + 6f, e.z), 30f, out _);
            castTarget = new Vector3(e.x, y + 0.04f, e.z);
        }

        /// Open water deep enough for a bobber, not under the dock / a rock / the boat.
        static bool IsWater(Vector3 c)
        {
            if (WorldInfo.GroundHeight(c) > Waves.SeaLevel - 0.3f) return false;
            if (BoatSurface.IsBlocked(c)) return false;
            var boat = Refs.Boat;
            if (boat != null && !(boat is Object bo && bo == null) && boat.Root != null)
            {
                Vector3 lp = boat.Root.InverseTransformPoint(c);
                if (Mathf.Abs(lp.x) < 1.4f && lp.z > -4.6f && lp.z < 4.1f) return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ waiting / bite / reel
        void UpdateWaiting(float dt)
        {
            biteTimer -= dt;
            nibbleTimer -= dt;
            if (nibbleTimer <= 0f && biteTimer > 0.8f)
            {
                nibble = 1f;
                // nibbles come more often shortly before the real bite
                nibbleTimer = biteTimer < 5f ? Mathf.Lerp(0.6f, 1.4f, (float)Rng.NextDouble()) : Mathf.Lerp(1.8f, 4.5f, (float)Rng.NextDouble());
                Vector3 w = bobber.position;
                BoatParticles.EmitAt(splash, w, 2, 0.6f);
                BoatSynth.PlayAt(BoatSynth.Plop, w, 0.12f, 1.4f, 15f);
            }
            nibble = Mathf.MoveTowards(nibble, 0f, dt * 3.5f);
            if (biteTimer > 0f) return;

            EnterState(RodState.Bite);
            strikeTimer = FishMarket.StrikeWindow + 0.2f;
            Vector3 b = bobber.position;
            BoatParticles.EmitAt(splash, b, 16);
            BoatSynth.PlayAt(BoatSynth.Plop, b, 0.9f, 0.75f, 30f);
            BoatSynth.PlayAt(BoatSynth.Splash, b, 0.35f, 1.4f, 30f);
        }

        void Hook()
        {
            hooked = FishMarket.RollCatch(Rng);
            hookedCommitted = false;
            var kind = hooked != null ? hooked.Kind : null;
            float kg = hooked != null ? hooked.weightKg : 0.5f;
            if (catchGo != null) Destroy(catchGo);
            catchGo = FishingVisuals.BuildCatch(transform, kind, kg, true, out catchLength);
            fishFrom = bobber.position;
            catchGo.transform.position = fishFrom;
            reelTime = 1.15f + Mathf.Clamp(kg * 0.07f, 0f, 1.1f);
            reelSoundTimer = 0f;
            splashTimer = 0f;
            BoatParticles.EmitAt(splash, fishFrom, 18);
            BoatSynth.PlayAt(BoatSynth.Splash, fishFrom, 0.55f, 1.25f, 30f);
            EnterState(RodState.Reeling);
        }

        Vector3 HangPoint => TipPos + Vector3.down * FishHang;

        void UpdateReeling(float dt)
        {
            float s = Mathf.Clamp01(stT / reelTime);
            if (catchGo != null)
            {
                Vector3 to = HangPoint;
                Vector3 p;
                if (s < 0.45f)
                {
                    // fight along the surface toward the shore / boat
                    float a = s / 0.45f;
                    Vector3 mid = Vector3.Lerp(fishFrom, to, 0.55f);
                    p = Vector3.Lerp(fishFrom, mid, a * a * (3f - 2f * a));
                    p.x += Mathf.Sin(stT * 13f) * 0.18f * (1f - a);
                    p.z += Mathf.Cos(stT * 11f) * 0.18f * (1f - a);
                    p.y = Waves.HeightAt(p.x, p.z) - 0.03f;
                    splashTimer -= dt;
                    if (splashTimer <= 0f)
                    {
                        splashTimer = 0.14f;
                        BoatParticles.EmitAt(splash, p, 4);
                    }
                    catchGo.transform.position = p;
                    // lying in the water, head (catch-local +Y) toward the angler, back up, tail beating
                    Vector3 dir = to - p;
                    dir.y = 0f;
                    if (dir.sqrMagnitude > 1e-3f)
                        catchGo.transform.rotation = Quaternion.LookRotation(Vector3.down, dir.normalized) * Quaternion.Euler(0f, 0f, Mathf.Sin(stT * 24f) * 30f);
                }
                else
                {
                    // lifted out of the water in an arc
                    float a = (s - 0.45f) / 0.55f;
                    Vector3 mid = Vector3.Lerp(fishFrom, to, 0.55f);
                    mid.y = Waves.HeightAt(mid.x, mid.z);
                    Vector3 ctrl = (mid + to) * 0.5f + Vector3.up * 1.2f;
                    float u = 1f - a;
                    p = u * u * mid + 2f * u * a * ctrl + a * a * to;
                    catchGo.transform.position = p;
                    catchGo.transform.rotation = Quaternion.Euler(0f, Time.time * 40f, Mathf.Sin(stT * 26f) * 22f);
                }
            }
            reelSoundTimer -= dt;
            if (reelSoundTimer <= 0f)
            {
                reelSoundTimer = 0.55f;
                BoatSynth.PlayAt(BoatSynth.Reel, TipPos, 0.4f, 1.1f, 15f);
            }
            if (s >= 1f)
            {
                EnterState(RodState.Landed);
                CommitCatch(true);
            }
        }

        /// The fish goes into the shared cooler box (on co-op clients via the host). Not sold.
        void CommitCatch(bool announce)
        {
            if (hooked == null || hookedCommitted) return;
            hookedCommitted = true;
            var f = hooked;
            FishMarket.AddCatch(f);   // raises GameEvents.FishCaught
            var kind = f.Kind;
            string kg = f.weightKg.ToString("0.0#", Loc.Culture);
            string name = f.Name;
            if (kind != null && (kind.rarity == Rarity.Rare || kind.rarity == Rarity.Epic))
                GameEvents.RaiseBanner(Loc.Tr($"{name}! {kg} kg", $"{name}! {kg} kg"), new Color(1f, 0.76f, 0.2f));
            else
                GameEvents.RaiseToast(Loc.Tr($"{name} · {kg} kg → Kühlbox", $"{name} · {kg} kg → cooler box"));
            var gm = GameManager.I;
            if (announce && gm != null && gm.State.fishListedTotal == 0 && gm.State.fishCaught <= 3)
                GameEvents.RaiseToast(Loc.Tr("Verkaufen: Handy → Fischmarkt", "Sell it: phone → Fish Market"));
        }

        void FinishLanded()
        {
            Vector3 p = catchGo != null ? catchGo.transform.position : TipPos;
            BoatParticles.Puff(p + Vector3.down * catchLength * 0.4f, new Color(0.85f, 0.93f, 1f, 0.7f), 8, 0.18f);
            BoatSynth.PlayAt(BoatSynth.Plop, p, 0.45f, 1.5f, 15f);
            bobValid = false;
            EnterState(RodState.Ready);
        }

        void StartRetrieve()
        {
            retrieveFrom = bobber.gameObject.activeSelf ? bobber.position : TipPos;
            BoatSynth.PlayAt(BoatSynth.Reel, TipPos, 0.35f, 1.3f, 15f);
            EnterState(RodState.Retrieving);
        }

        // ------------------------------------------------------------------ frame: visuals
        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (vm == null || vm.root == null || !vm.root.gameObject.activeSelf || st == RodState.Hidden)
            {
                if (bobber != null && bobber.gameObject.activeSelf) bobber.gameObject.SetActive(false);
                if (line != null && line.enabled) line.enabled = false;
                ReleasePrompt();
                return;
            }
            if (dt <= 0f) return;
            UpdateBobberAndLine(dt);   // world space: after the camera got its final pose
            WritePrompt();
        }

        void PoseRig(float dt)
        {
            idleTime += dt;
            float targetPitch = 0f, targetYaw = 0f, targetBend = 0f;
            Vector3 offset = Vector3.zero;
            bool scripted = false;
            switch (st)
            {
                case RodState.Charging:
                {
                    float w = Smooth01(stT / WindUpTime);
                    pitch = Mathf.Lerp(pitch, WindUpPitch, w) + (charge >= 1f ? Mathf.Sin(idleTime * 55f) * 0.8f : 0f);
                    yawOff = Mathf.Lerp(yawOff, 9f, w);
                    offset = new Vector3(0.025f, 0.05f, -0.05f) * w;
                    targetBend = -7f * w;
                    scripted = true;
                    break;
                }
                case RodState.Casting:
                {
                    float s = Mathf.Clamp01(stT / WhipTime);
                    if (stT < WhipTime)
                    {
                        float e = s * s;   // accelerate into the whip
                        pitch = Mathf.Lerp(whipFrom, WhipPitch, e);
                        yawOff = Mathf.Lerp(yawOff, -3f, e);
                        offset = Vector3.Lerp(new Vector3(0.025f, 0.05f, -0.05f), new Vector3(-0.01f, -0.02f, 0.05f), e);
                        targetBend = Mathf.Lerp(-10f, 14f, e);
                    }
                    else
                    {
                        float a = Smooth01((stT - WhipTime) / SettleTime);
                        pitch = Mathf.Lerp(WhipPitch, FishingPitch, a);
                        yawOff = Mathf.Lerp(-3f, 0f, a);
                        offset = Vector3.Lerp(new Vector3(-0.01f, -0.02f, 0.05f), new Vector3(0f, -0.01f, 0.02f), a);
                        targetBend = 4f * (1f - a);
                    }
                    scripted = true;
                    break;
                }
                case RodState.Waiting:
                    targetPitch = FishingPitch + nibble * 2.5f + Mathf.Sin(idleTime * 2.2f) * 0.5f;
                    targetBend = nibble * 7f;
                    offset = new Vector3(0f, -0.01f, 0.02f);
                    break;
                case RodState.Bite:
                    targetPitch = FishingPitch + 5f + Mathf.Sin(idleTime * 31f) * 3f;
                    targetBend = 24f + Mathf.Sin(idleTime * 27f) * 4f;
                    offset = new Vector3(0f, -0.015f, 0.025f);
                    break;
                case RodState.Reeling:
                {
                    float s = Mathf.Clamp01(stT / Mathf.Max(0.1f, reelTime));
                    targetPitch = Mathf.Lerp(8f, -10f, s) + Mathf.Sin(idleTime * 23f) * 2f * (1f - s);
                    targetBend = Mathf.Lerp(28f, 9f, s) + Mathf.Sin(idleTime * 19f) * 3f * (1f - s);
                    offset = new Vector3(0f, 0.01f, -0.02f);
                    crankDeg += 950f * dt;
                    break;
                }
                case RodState.Landed:
                    targetPitch = -6f;
                    targetBend = 9f + Mathf.Sin(idleTime * 21f) * 2f;
                    break;
                case RodState.Retrieving:
                    targetPitch = 0f;
                    crankDeg += 800f * dt;
                    break;
                default:
                    targetPitch = Mathf.Sin(idleTime * 2.2f) * 0.6f;
                    break;
            }
            if (!scripted)
            {
                float k = 1f - Mathf.Exp(-10f * dt);
                pitch = Mathf.Lerp(pitch, targetPitch, k);
                yawOff = Mathf.Lerp(yawOff, targetYaw, k);
            }
            tipBend = Mathf.Lerp(tipBend, targetBend, 1f - Mathf.Exp(-14f * dt));

            // idle sway (±4 mm at 0.35 Hz), walk bob and look lag from the player controller
            Vector3 sway = new Vector3(Mathf.Sin(idleTime * 2.2f) * 0.004f, Mathf.Sin(idleTime * 1.6f + 0.7f) * 0.003f, 0f);
            if (Refs.Player is PlayerController pc && pc != null)
            {
                float b = pc.BobAmount;
                float ph = pc.BobPhase;
                sway += new Vector3(Mathf.Sin(ph) * 0.006f * b, -Mathf.Abs(Mathf.Sin(ph)) * 0.01f * b, 0f);
                Vector2 look = Vector2.ClampMagnitude(pc.LookDelta * 0.0018f, 0.025f);
                sway -= new Vector3(look.x, look.y, 0f);
            }
            float e01 = Smooth01(equip01);
            phoneBlend = Mathf.MoveTowards(phoneBlend, UIState.PhoneOpen ? 1f : 0f, dt * 3f);
            Vector3 equipDrop = Vector3.Lerp(new Vector3(0.03f, -0.3f, -0.05f), new Vector3(0.05f, -0.75f, -0.1f), phoneBlend) * (1f - e01);
            float dropPitch = Mathf.Lerp(25f, 60f, phoneBlend);

            // the rod swings around a point between the hands (rod space PivotLocal)
            Quaternion rot = Quaternion.AngleAxis(pitch + (1f - e01) * dropPitch, Vector3.right) * Quaternion.AngleAxis(yawOff, Vector3.up) * RodViewModel.BaseRotation;
            Vector3 pivot = RodViewModel.PivotCamera + offset + sway + equipDrop;
            vm.rig.localRotation = rot;
            vm.rig.localPosition = pivot - rot * RodViewModel.PivotLocal;
            vm.tipPivot.localRotation = Quaternion.Euler(tipBend, 0f, 0f);
            vm.SetCrank(crankDeg);
        }

        static float Smooth01(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        void UpdateBobberAndLine(float dt)
        {
            Vector3 tip = TipPos;
            float sag = 0f;
            Vector3 end;
            bool showBobber = true;

            switch (st)
            {
                case RodState.Casting when released:
                {
                    float s = Mathf.Clamp01((stT - releaseT) / Mathf.Max(0.05f, flightTime));
                    Vector3 to = targetWater ? new Vector3(castTarget.x, Waves.HeightAt(castTarget.x, castTarget.z), castTarget.z) : castTarget;
                    float arc = 1.1f + castDist * 0.16f;
                    bobPos = Vector3.Lerp(castFrom, to, s) + Vector3.up * (arc * 4f * s * (1f - s));
                    bobPrev = bobPos;
                    bobber.rotation = Quaternion.Euler(s * 300f, 0f, 0f);
                    end = bobPos + Vector3.up * 0.1f;
                    sag = 0.08f;
                    break;
                }
                case RodState.Waiting:
                case RodState.Bite:
                {
                    float wave = Waves.HeightAt(castTarget.x, castTarget.z);
                    float dip = st == RodState.Bite
                        ? 0.2f + Mathf.Sin(stT * 30f) * 0.04f
                        : nibble * 0.06f * Mathf.Abs(Mathf.Sin(stT * 28f));
                    float jx = st == RodState.Bite ? Mathf.Sin(stT * 13f) * 0.06f : 0f;
                    bobPos = new Vector3(castTarget.x + jx, wave - dip, castTarget.z);
                    bobPrev = bobPos;
                    Vector3 n = Waves.NormalAt(castTarget.x, castTarget.z);
                    bobber.rotation = Quaternion.FromToRotation(Vector3.up, n) * Quaternion.Euler(Mathf.Sin(idleTime * 1.7f) * 6f, 0f, Mathf.Sin(idleTime * 1.3f) * 6f);
                    end = bobPos + Vector3.up * 0.1f;
                    float dist = Vector3.Distance(tip, end);
                    sag = st == RodState.Bite ? 0.03f : 0.45f * Mathf.Min(1f, dist / 8f);
                    break;
                }
                case RodState.Reeling:
                case RodState.Landed:
                {
                    showBobber = false;
                    if (catchGo != null)
                    {
                        if (st == RodState.Landed)
                        {
                            // dangle below the tip, wiggling
                            Vector3 hang = HangPoint;
                            catchGo.transform.position = hang;
                            Vector3 toCam = tip - hang;
                            toCam.y = 0f;
                            Quaternion face = toCam.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(toCam.normalized, Vector3.up) : Quaternion.identity;
                            catchGo.transform.rotation = face * Quaternion.Euler(0f, 90f + Mathf.Sin(stT * 7f) * 25f, Mathf.Sin(stT * 17f) * 14f);
                            float pop = stT > LandedTime - 0.25f ? Mathf.Clamp01((LandedTime - stT) / 0.25f) : 1f;
                            catchGo.transform.localScale = Vector3.one * Mathf.Max(0.01f, pop);
                        }
                        end = catchGo.transform.position;
                    }
                    else end = HangPoint;
                    sag = 0f;
                    break;
                }
                case RodState.Retrieving:
                {
                    float s = Smooth01(stT / RetrieveTime);
                    Vector3 hang = tip + Vector3.down * HangLength;
                    float lift = 0.6f * 4f * s * (1f - s);
                    bobPos = Vector3.Lerp(retrieveFrom, hang, s) + Vector3.up * lift;
                    bobPrev = bobPos;
                    bobValid = true;
                    end = bobPos + Vector3.up * 0.1f;
                    sag = 0.05f;
                    break;
                }
                default:
                {
                    // dangling below the tip: damped pendulum (Verlet) so it swings when the player turns
                    Vector3 rest = tip + Vector3.down * HangLength;
                    if (!bobValid || (bobPos - tip).sqrMagnitude > 4f)
                    {
                        bobPos = bobPrev = rest;
                        bobValid = true;
                    }
                    float h = Mathf.Min(dt, 0.05f);
                    Vector3 next = bobPos + (bobPos - bobPrev) * 0.94f + Vector3.down * (9.81f * h * h);
                    Vector3 d = next - tip;
                    float len = d.magnitude;
                    if (len > HangLength) next = tip + d / len * HangLength;
                    bobPrev = bobPos;
                    bobPos = next;
                    Vector3 swing = tip - bobPos;
                    bobber.rotation = swing.sqrMagnitude > 1e-4f ? Quaternion.FromToRotation(Vector3.up, swing.normalized) : Quaternion.identity;
                    end = bobPos + (tip - bobPos).normalized * 0.1f;
                    break;
                }
            }

            if (bobber.gameObject.activeSelf != showBobber) bobber.gameObject.SetActive(showBobber);
            if (showBobber) bobber.position = bobPos;

            if (!line.enabled) line.enabled = true;
            int n2 = linePts.Length;
            float distAll = Vector3.Distance(tip, end);
            float sagM = sag * Mathf.Min(1f, distAll / 5f);
            for (int i = 0; i < n2; i++)
            {
                float s = i / (n2 - 1f);
                linePts[i] = Vector3.Lerp(tip, end, s) + Vector3.down * (sagM * 4f * s * (1f - s) * (1f + 0.3f * (1f - s)));
            }
            line.SetPositions(linePts);
        }

        // ------------------------------------------------------------------ prompt
        /// Short control hint while no interactable is targeted (the interactor owns the prompt otherwise).
        void WritePrompt()
        {
            string want = null;
            switch (st)
            {
                case RodState.Ready: want = promptCast.Text; break;
                case RodState.Charging:
                {
                    int i = Mathf.Clamp(Mathf.RoundToInt(charge * 10f), 0, 10);
                    want = Loc.Current == Language.German ? BarDe[i] : BarEn[i];
                    break;
                }
                case RodState.Waiting: want = promptReel.Text; break;
                case RodState.Bite: want = promptStrike.Text; break;
            }
            if (UIState.BlocksGameplay) want = null;
            string cur = InteractionPrompt.Text;
            if (cur == null || ReferenceEquals(cur, promptWritten))
            {
                InteractionPrompt.Text = want;
                promptWritten = want;
            }
            else promptWritten = null;   // someone else (an interactable) owns the prompt this frame
        }

        void ReleasePrompt()
        {
            if (promptWritten != null && ReferenceEquals(InteractionPrompt.Text, promptWritten)) InteractionPrompt.Text = null;
            promptWritten = null;
        }
    }
}
