using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// The fishing rod as a two-handed hotbar item (HoldItem.FishingRod, How to Fish style). Shown as a first-person
    /// view model while it is in an active hotbar slot; the Player module's hands hold it (they look for the camera
    /// descendant named "FishingRod", see FishingVisuals.BuildViewRod). The rod stays under the camera the whole time
    /// it is held (lowered while the phone is up / climbing) so the hands never lose it.
    ///
    /// Controls (GameAction.Use, LMB by default): hold to charge → release to cast (0.35 s wind-up over the shoulder,
    /// 0.25 s whip); the bobber flies castMin..castMax of the equipped rod (D.3: bamboo 4–17 m … research rod 6–28 m).
    /// Where it lands decides the table (D.2): in ≥ 12 m of water and ≥ 60 m from every shore the OPEN-SEA table
    /// (bigger fish, 20–45 s bites – carbon rod or better), otherwise the SHORE table (12–30 s). Bites ÷ fishSpeed.
    /// The bobber is pulled under: click within the strike window to hook → the REEL MINIGAME (D.1, see ReelFight):
    /// hold Use to reel, release to let the line breathe, counter a run with the opposite direction (A/D or by
    /// dragging the view against it). Landed fish dangle from the tip and go into the shared cooler box
    /// (FishMarket.AddCatch – NOT sold, the phone's market app sells them). Clicking while waiting reels the line in.
    /// Works on the dock, beaches, rocks and the boat. The fight state is published in FishingReel for the UI gauge.
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
        /// The current cast fishes on the open-sea table (D.2) – for the HUD / quests.
        public static bool OnOpenSea { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { State = RodState.Hidden; Charge01 = -1f; ViewModelVisible = false; OnOpenSea = false; }

        // ------------------------------------------------------------------ tuning
        const float WindUpTime = 0.35f, WhipTime = 0.25f, ChargeTime = 1.1f, SettleTime = 0.35f;
        const float ReleaseAt = 0.6f;            // fraction of the whip when the bobber leaves the tip
        const float HangLength = 0.36f;          // bobber dangling below the tip
        const float FishHang = 0.55f;            // line length between tip and a landed fish
        const float LandedTime = 1.35f, RetrieveTime = 0.55f, EscapeTime = 1.4f;
        const float EquipTime = 0.25f, StowTime = 0.2f;
        const float WindUpPitch = -58f, WhipPitch = 34f, FishingPitch = 21f;

        static readonly System.Random Rng = new System.Random();

        // ------------------------------------------------------------------ objects
        RodViewModel vm;
        Transform bobber;
        LineRenderer line;
        readonly Vector3[] linePts = new Vector3[16];
        ParticleSystem splash;
        GameObject catchGo, escapeGo;
        float catchLength;
        AudioSource dragSrc;

        // ------------------------------------------------------------------ state
        RodState st = RodState.Hidden;
        float stT, charge, castPower, castDist, flightTime, releaseT, whipFrom;
        bool released, targetWater;
        Vector3 castFrom, castTarget;
        float biteTimer, nibbleTimer, nibble, strikeTimer;
        float equip01, pitch, yawOff, tipBend, crankDeg, reelSoundTimer, splashTimer, idleTime, phoneBlend;
        Vector3 bobPos, bobPrev, retrieveFrom;
        bool bobValid, retrieveNoBobber;
        float blockedUntil;
        FishItem hooked;
        bool hookedCommitted, hookedFromBoat;
        string promptWritten;
        int lastTier = -2, cachedTier;
        bool tierDirty = true;

        // round 2: rod stats, tables, the fight
        GearStats gear;
        bool seaTable, rarePing;
        int weakRodToastDay = -1, openSeaHintDay = -1;
        readonly ReelFight fight = new ReelFight();
        bool lifting;
        float liftT, liftDur;
        Vector3 liftFrom, fightFrom, fishPos;
        float lateral, pullBack, lookYawSpeed;
        float escapeT;
        Vector3 escapeDir;
        bool leapSplashed;
        int hintShownFor = -1;

        readonly KeyPrompt promptCast = new KeyPrompt(GameAction.Use, "Auswerfen (halten)", "Cast (hold)");
        readonly KeyPrompt promptReel = new KeyPrompt(GameAction.Use, "Einholen", "Reel in");
        readonly KeyPrompt promptStrike = new KeyPrompt(GameAction.Use, "Anschlagen!", "Strike!");
        readonly KeyPrompt promptHold = new KeyPrompt(GameAction.Use, "Halten – Einholen", "Hold – reel in");
        static readonly string[] BarDe = BuildBars("Wurf "), BarEn = BuildBars("Cast ");

        // fallback tension bar (shown in the interaction prompt when no UI gauge is installed)
        const int BarSlots = 20;
        string[] reelBars;
        Language reelBarsLang;
        string reelBarsKey;

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

            // the reel's drag buzzing while a fish takes line (2D-ish: it is the player's own rod)
            dragSrc = BoatSynth.LoopSource(gameObject, BoatSynth.Drag, 1f, 20f, AudioRolloffMode.Linear);
            dragSrc.spatialBlend = 0.2f;

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
            if (escapeGo != null) Destroy(escapeGo);
            ViewModelVisible = false;
            State = RodState.Hidden;
            Charge01 = -1f;
            FishingReel.Clear();
        }

        void OnGameStarted()
        {
            tierDirty = true;
            weakRodToastDay = -1;
            openSeaHintDay = -1;
            ResetAll(false);
        }

        void OnReturnedToMenu() => ResetAll(false);

        /// The equipped rod may have changed: re-read its stats (the view model is rebuilt when the tier differs).
        void OnGearChanged() => tierDirty = true;

        void DestroyViewModel()
        {
            if (vm != null && vm.root != null) Destroy(vm.root.gameObject);
            vm = null;
            nearLine = null;   // a child of the view model
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

        void RefreshGear()
        {
            var gm = GameManager.I;
            if (gm != null && gm.State != null)
            {
                gear = gm.Gear;
                cachedTier = Mathf.Clamp(gear.rodTier, 0, 3);
            }
            else
            {
                gear = new GearStats { castMin = 4f, castMax = 17f, reelPower = 1f, lineStrength = 1f, fishSpeed = 1f };
                cachedTier = 0;
            }
            if (gear.castMax <= gear.castMin) { gear.castMin = 4f; gear.castMax = 17f; }
            tierDirty = false;
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

        static bool LocalOnBoat()
        {
            var b = Refs.Boat;
            return b != null && !(b is Object o && o == null) && b.PlayerAboard;
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

            // stowed, jumped into the water, fainted …: drop the cast (a fish mid-fight gets away). The phone keeps it.
            bool session = st != RodState.Hidden && st != RodState.Ready && st != RodState.Charging;
            if (session && (!held || p.IsInWater || p.IsUnderwater || p.IsFainting || busy)) CancelSession();

            // equip / stow animation
            float target = raised ? 1f : 0f;
            equip01 = Mathf.MoveTowards(equip01, target, dt / (target > equip01 ? EquipTime : StowTime));
            bool show = view != null && inGame && (held || equip01 > 0.001f);
            if (show)
            {
                if (tierDirty) RefreshGear();
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
            UpdateEscape(dt);

            if (st == RodState.Hidden)
            {
                Charge01 = -1f;
                State = st;
                if (FishingReel.Active) FishingReel.Clear();
                StopDrag();
                return;
            }

            stT += dt;
            bool blocked = UIState.BlocksGameplay;
            if (blocked) blockedUntil = Time.unscaledTime + 0.2f;
            bool input = canFish && !blocked && Time.unscaledTime >= blockedUntil && equip01 >= 0.999f;

            // camera yaw speed (°/s, smoothed) for the "pull against the run" counter-steer
            float rawYaw = 0f;
            if (p is PlayerController pcl && pcl != null && dt > 0f) rawYaw = pcl.LookDelta.x / dt;
            lookYawSpeed = Mathf.Lerp(lookYawSpeed, rawYaw, 1f - Mathf.Exp(-12f * dt));

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
                        GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr("Verpasst – weg ist er", "Missed – it got away"));
                        StartRetrieve();
                    }
                    break;
                case RodState.Reeling: UpdateReeling(dt, input, blocked); break;
                case RodState.Landed:
                    if (stT >= LandedTime) FinishLanded();
                    break;
                case RodState.Retrieving:
                    if (stT >= RetrieveTime) EnterState(RodState.Ready);
                    break;
            }

            // the line snaps when the player walks / sails away from the bobber or the fighting fish
            if ((st == RodState.Waiting || st == RodState.Bite) && (bobber.position - TipPos).sqrMagnitude > SnapDist() * SnapDist())
            {
                GameEvents.RaiseNotify(NotifyKind.Warning, Loc.Tr("Schnur gerissen!", "Line snapped!"));
                SetBobberGlow(false);
                bobValid = false;
                EnterState(RodState.Ready);
            }
            else if (st == RodState.Reeling && !lifting && catchGo != null && (catchGo.transform.position - TipPos).sqrMagnitude > SnapDist() * SnapDist())
                OnFightLost(true);

            State = st;
            Charge01 = st == RodState.Charging ? charge : -1f;
            if (st != RodState.Reeling && FishingReel.Active) FishingReel.Clear();
            if (st != RodState.Reeling) StopDrag();

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
                lifting = false;
                rarePing = false;
                retrieveNoBobber = false;
            }
        }

        /// Ends the current cast at once. A fish mid-fight gets away; a fish already lifted out goes into the box.
        void CancelSession()
        {
            if (st == RodState.Reeling && hooked != null && !hookedCommitted)
            {
                if (lifting) CommitCatch(false);
                else
                {
                    GameEvents.RaiseNotify(NotifyKind.Warning, Loc.Tr("Abgehauen!", "Got away!"));
                    FishingReel.RaiseEscaped();
                    ReleaseFishVisual();
                }
            }
            else if (hooked != null && !hookedCommitted && st == RodState.Landed) CommitCatch(false);
            if (catchGo != null) { Destroy(catchGo); catchGo = null; }
            SetBobberGlow(false);
            bobValid = false;
            EnterState(RodState.Ready);
        }

        void ResetAll(bool keepVisualState)
        {
            if (catchGo != null) { Destroy(catchGo); catchGo = null; }
            hooked = null;
            hookedCommitted = false;
            lifting = false;
            charge = 0f;
            bobValid = false;
            st = RodState.Hidden;
            stT = 0f;
            State = RodState.Hidden;
            Charge01 = -1f;
            OnOpenSea = false;
            if (!keepVisualState) equip01 = 0f;
            SetBobberGlow(false);
            if (bobber != null) bobber.gameObject.SetActive(false);
            if (line != null) line.enabled = false;
            if (nearLine != null) nearLine.enabled = false;
            if (vm != null && vm.root != null && !keepVisualState) vm.root.gameObject.SetActive(false);
            FishingReel.Clear();
            StopDrag();
            ReleasePrompt();
        }

        // ------------------------------------------------------------------ casting
        void StartCharge()
        {
            if (tierDirty) RefreshGear();
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
                DecideTable(w);
                biteTimer = FishMarket.BiteDelay(Rng, seaTable);
                nibbleTimer = Mathf.Lerp(1.5f, 3.5f, (float)Rng.NextDouble());
                nibble = 0f;
            }
            else
            {
                BoatSynth.PlayAt(BoatSynth.Thunk, castTarget, 0.25f, 1.6f, 20f);
                GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr("Da ist kein Wasser", "That's not water"));
                StartRetrieve();
            }
        }

        /// D.2: open sea (≥ 12 m deep, ≥ 60 m from every shore) uses the sea table – if the rod allows it.
        void DecideTable(Vector3 bobberPos)
        {
            bool open = FishMarket.IsOpenSea(bobberPos);
            int day = GameManager.I != null ? GameManager.I.State.day : 0;
            if (open && !FishMarket.RodAllowsOpenSea(gear))
            {
                seaTable = false;
                if (weakRodToastDay != day)
                {
                    weakRodToastDay = day;
                    GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr("Bambusrute zu schwach", "Bamboo rod too weak"), Loc.Tr("Hochsee", "Open sea"));
                }
            }
            else
            {
                seaTable = open;
                if (open && openSeaHintDay != day)
                {
                    openSeaHintDay = day;
                    GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr("Hochsee – große Fische!", "Open sea – big fish!"));
                }
            }
            OnOpenSea = seaTable;
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
            castDist = Mathf.Lerp(gear.castMin, gear.castMax, castPower);
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

        // ------------------------------------------------------------------ waiting / bite
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

            // the fish is decided at the bite, so the research rod can glow for a rare one (D.3)
            hooked = FishMarket.RollCatch(Rng, seaTable);
            hookedCommitted = false;
            var kind = hooked != null ? hooked.Kind : null;
            rarePing = gear.rodRarePing && kind != null && kind.rarity >= Rarity.Rare;

            EnterState(RodState.Bite);
            strikeTimer = FishMarket.StrikeWindow + 0.2f;
            Vector3 b = bobber.position;
            BoatParticles.EmitAt(splash, b, 16);
            BoatSynth.PlayAt(BoatSynth.Plop, b, 0.9f, 0.75f, 30f);
            BoatSynth.PlayAt(BoatSynth.Splash, b, 0.35f, 1.4f, 30f);
            if (rarePing)
            {
                SetBobberGlow(true);
                BoatSynth.PlayAt(BoatSynth.Sparkle, b, 0.7f, 1f, 40f);
            }
        }

        void SetBobberGlow(bool on)
        {
            if (bobber != null) FishingVisuals.SetBobberGlow(bobber, on);
        }

        // ------------------------------------------------------------------ the fight (D.1)
        void Hook()
        {
            if (hooked == null) hooked = FishMarket.RollCatch(Rng, seaTable);
            hookedCommitted = false;
            hookedFromBoat = LocalOnBoat();
            SetBobberGlow(false);
            var kind = hooked != null ? hooked.Kind : null;
            float kg = hooked != null ? hooked.weightKg : 0.5f;
            if (catchGo != null) Destroy(catchGo);
            catchGo = FishingVisuals.BuildCatch(transform, kind, kg, true, out catchLength);
            fightFrom = bobber.position;
            fightFrom.y = Waves.HeightAt(fightFrom.x, fightFrom.z);
            fishPos = fightFrom;
            catchGo.transform.position = fishPos;
            lateral = 0f;
            pullBack = 0f;
            lifting = false;
            leapSplashed = false;
            reelSoundTimer = 0f;
            splashTimer = 0f;

            var gm = GameManager.I;
            var diff = gm != null ? gm.Difficulty : DifficultySettings.For(Difficulty.OpenWater);
            bool beginner = gm != null && gm.State.fishCaught < 5;
            fight.Begin(kind, gear, diff, FishingReel.Assist || GameManager.AutomatedRun, Rng, beginner);
            BuildReelBars();
            // the first fights of a save: one short control hint (L.10 style)
            if (gm != null && gm.State.fishCaught < 2 && hintShownFor != gm.State.fishCaught)
            {
                hintShownFor = gm.State.fishCaught;
                GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr("Halten: einholen · Loslassen: nachgeben", "Hold: reel in · Release: give line"),
                                       GameInput.BindingLabel(GameAction.Use));
            }

            BoatParticles.EmitAt(splash, fishPos, 18);
            BoatSynth.PlayAt(BoatSynth.Splash, fishPos, 0.55f, 1.25f, 30f);
            EnterState(RodState.Reeling);
            Publish();
        }

        void UpdateReeling(float dt, bool input, bool blocked)
        {
            if (lifting)
            {
                UpdateLift(dt);
                return;
            }
            // a menu/phone over the game pauses the fight (the fish waits politely)
            if (!blocked)
            {
                bool hold = input && GameInput.UseHeld;
                float steer = input ? GameInput.Move.x : 0f;
                var r = fight.Step(dt, hold, steer, input ? lookYawSpeed : 0f);
                switch (r)
                {
                    case ReelFight.Result.Landed: StartLift(); break;
                    case ReelFight.Result.Snapped: OnFightLost(true); return;
                    case ReelFight.Result.Escaped: OnFightLost(false); return;
                }
            }
            Publish();
            if (!lifting) UpdateFightFish(dt);
            UpdateFightAudio(dt);
        }

        void Publish()
        {
            FishingReel.Active = st == RodState.Reeling && !lifting;
            FishingReel.Tension = fight.Tension;
            FishingReel.GreenMin = fight.GreenMin;
            FishingReel.GreenMax = fight.GreenMax;
            FishingReel.Progress01 = fight.Progress;
            FishingReel.RunDir = fight.RunDir;
            FishingReel.Running = fight.Running;
            FishingReel.Countering = fight.Countering;
            FishingReel.Reeling = fight.Reeling;
            FishingReel.Snap01 = fight.Snap01;
            FishingReel.Slack01 = fight.Slack01;
            FishingReel.Kind = fight.Kind;
            FishingReel.Zone = fight.Zone;
            FishingReel.LockStrafe = FishingReel.Active;
        }

        /// Where the fight ends before the lift: in the water just in front of the rod tip.
        Vector3 NearPoint()
        {
            Vector3 tip = TipPos;
            Vector3 away = new Vector3(fightFrom.x - tip.x, 0f, fightFrom.z - tip.z);
            float d = away.magnitude;
            away = d > 0.01f ? away / d : Vector3.forward;
            Vector3 n = new Vector3(tip.x, 0f, tip.z) + away * Mathf.Min(0.6f, d);
            n.y = Waves.HeightAt(n.x, n.z);
            return n;
        }

        void UpdateFightFish(float dt)
        {
            if (catchGo == null) return;
            Vector3 tip = TipPos;
            Vector3 near = NearPoint();
            Vector3 toFish = new Vector3(fightFrom.x - near.x, 0f, fightFrom.z - near.z);
            float startDist = toFish.magnitude;
            Vector3 away = startDist > 0.01f ? toFish / startDist : Vector3.forward;
            Vector3 side = new Vector3(away.z, 0f, -away.x);   // the angler's right (looking toward the fish)
            var kind = fight.Kind;
            float strength = kind != null ? kind.runStrength : 0f;

            // runs pull line out and sideways; reeling wins it back
            if (fight.Running)
            {
                float k = fight.Countering ? 0.55f : 1f;
                pullBack = Mathf.Min(pullBack + (0.5f + strength * 0.012f) * k * dt, 3.5f + startDist * 0.2f);
                lateral += fight.RunDir * (0.9f + strength * 0.012f) * k * dt;
            }
            else
            {
                if (fight.Reeling) pullBack = Mathf.Max(0f, pullBack - 1.6f * dt);
                lateral = Mathf.MoveTowards(lateral, 0f, 0.5f * dt);
            }
            float rem = 1f - fight.Progress;
            float maxLat = 1.2f + 0.3f * startDist * rem;
            lateral = Mathf.Clamp(lateral, -maxLat, maxLat);

            float e = Smooth01(fight.Progress);
            Vector3 basePos = Vector3.Lerp(fightFrom, near, e) + away * (pullBack * rem) + side * (lateral * Mathf.Max(0.25f, rem));
            float water = Waves.HeightAt(basePos.x, basePos.z);
            float y = water - 0.05f;

            // big fish leap out of the water on some runs (How to Fish "tail-walk")
            float leapLen = 0.95f;
            bool leaping = fight.Leap && fight.RunAge >= 0f && fight.RunAge <= leapLen;
            float pitchDeg = 0f;
            if (leaping)
            {
                float s = fight.RunAge / leapLen;
                float h = 0.7f + 0.35f * catchLength;
                y = water + 4f * h * s * (1f - s);
                pitchDeg = Mathf.Lerp(-55f, 55f, s);
                if (!leapSplashed) { leapSplashed = true; BoatParticles.EmitAt(splash, basePos, 22); BoatSynth.PlayAt(BoatSynth.Splash, basePos, 0.5f, 1.15f, 40f); }
            }
            else if (leapSplashed && fight.RunAge > leapLen)
            {
                leapSplashed = false;
                BoatParticles.EmitAt(splash, basePos, 26);
                BoatSynth.PlayAt(BoatSynth.Splash, basePos, 0.7f, 0.95f, 45f);
            }
            else if (fight.RunAge < 0f) leapSplashed = false;

            fishPos = new Vector3(basePos.x, y, basePos.z);
            catchGo.transform.position = fishPos;

            // head toward the angler, away and to the side while running; the tail beats faster under tension
            Vector3 head = -away;
            if (fight.Running) head = (away * 0.6f + side * fight.RunDir).normalized;
            float beat = Mathf.Lerp(14f, 30f, fight.Tension / 100f);
            Quaternion lie = Quaternion.LookRotation(Vector3.down, head) * Quaternion.Euler(0f, 0f, Mathf.Sin(idleTime * beat) * (fight.Running ? 38f : 26f));
            // (catch root: +Y = head, −Z = back – see FishingVisuals.BuildCatch)
            if (leaping) lie = Quaternion.AngleAxis(pitchDeg, Vector3.Cross(Vector3.up, head).normalized) * Quaternion.LookRotation(head, Vector3.up) * Quaternion.Euler(90f, 0f, 0f);
            catchGo.transform.rotation = lie;

            // splashes: more when it runs or the line is tight
            splashTimer -= dt;
            if (splashTimer <= 0f && !leaping)
            {
                splashTimer = Mathf.Lerp(0.4f, 0.1f, fight.Tension / 100f) * (fight.Running ? 0.6f : 1f);
                BoatParticles.EmitAt(splash, fishPos, fight.Running ? 6 : 3);
            }
        }

        void UpdateFightAudio(float dt)
        {
            // reel clicks while cranking, the drag buzzes when a run takes line at high tension
            reelSoundTimer -= dt;
            if (fight.Reeling && reelSoundTimer <= 0f)
            {
                reelSoundTimer = 0.45f;
                BoatSynth.PlayAt(BoatSynth.Reel, TipPos, 0.32f, 1.05f + fight.Tension * 0.003f, 15f);
            }
            bool drag = fight.Running && fight.Tension > 70f;
            float want = drag ? Mathf.Lerp(0.25f, 0.85f, (fight.Tension - 70f) / 30f) * Settings.SfxVolume : 0f;
            if (dragSrc != null)
            {
                dragSrc.volume = Mathf.MoveTowards(dragSrc.volume, want, dt * 4f);
                dragSrc.pitch = 0.85f + fight.Tension * 0.004f;
                if (dragSrc.volume > 0.001f) { if (!dragSrc.isPlaying) dragSrc.Play(); }
                else if (dragSrc.isPlaying) dragSrc.Stop();
            }
        }

        void StopDrag()
        {
            if (dragSrc != null && dragSrc.isPlaying) { dragSrc.volume = 0f; dragSrc.Stop(); }
        }

        Vector3 HangPoint => TipPos + Vector3.down * (FishHang + Mathf.Max(0f, catchLength - 0.4f) * 0.35f);

        void StartLift()
        {
            lifting = true;
            liftT = 0f;
            liftFrom = catchGo != null ? catchGo.transform.position : fishPos;
            liftDur = 0.55f + Mathf.Clamp(catchLength * 0.2f, 0f, 0.35f);
            FishingReel.Active = false;
            FishingReel.LockStrafe = false;
            StopDrag();
            BoatParticles.EmitAt(splash, liftFrom, 20);
            BoatSynth.PlayAt(BoatSynth.Splash, liftFrom, 0.6f, 1.2f, 30f);
        }

        void UpdateLift(float dt)
        {
            liftT += dt;
            float a = Mathf.Clamp01(liftT / liftDur);
            if (catchGo != null)
            {
                Vector3 to = HangPoint;
                Vector3 ctrl = (liftFrom + to) * 0.5f + Vector3.up * 1.2f;
                float u = 1f - a;
                catchGo.transform.position = u * u * liftFrom + 2f * u * a * ctrl + a * a * to;
                catchGo.transform.rotation = Quaternion.Euler(0f, idleTime * 40f, Mathf.Sin(stT * 26f) * 22f);
            }
            reelSoundTimer -= dt;
            if (reelSoundTimer <= 0f)
            {
                reelSoundTimer = 0.3f;
                BoatSynth.PlayAt(BoatSynth.Reel, TipPos, 0.4f, 1.25f, 15f);
            }
            if (a >= 1f)
            {
                lifting = false;
                EnterState(RodState.Landed);
                CommitCatch(true);
            }
        }

        /// The fight is over without a catch: snapped (tension too high too long) or escaped (line too slack).
        void OnFightLost(bool snapped)
        {
            Vector3 at = catchGo != null ? catchGo.transform.position : fishPos;
            if (snapped)
            {
                GameEvents.RaiseNotify(NotifyKind.Warning, Loc.Tr("Schnur gerissen!", "Line snapped!"));
                BoatSynth.PlayAt(BoatSynth.Snap, TipPos, 0.8f, 1f, 25f);
                var rec = PlayerRecords.Local;
                if (rec != null) { rec.snaps++; Achievements.OnLocalCounter(rec); }
                FishingReel.RaiseSnapped();
            }
            else
            {
                GameEvents.RaiseNotify(NotifyKind.Warning, Loc.Tr("Abgehauen!", "Got away!"));
                BoatSynth.PlayAt(BoatSynth.Splash, at, 0.5f, 1.3f, 30f);
                FishingReel.RaiseEscaped();
            }
            ReleaseFishVisual();
            hooked = null;
            hookedCommitted = false;
            FishingReel.Clear();
            StopDrag();
            retrieveFrom = at;
            retrieveNoBobber = snapped;   // the bobber is gone with the fish
            BoatSynth.PlayAt(BoatSynth.Reel, TipPos, 0.35f, 1.3f, 15f);
            EnterState(RodState.Retrieving);
            retrieveNoBobber = snapped;
            bobValid = false;
        }

        /// The hooked fish swims off (snapped line, escaped, rod put away mid-fight).
        void ReleaseFishVisual()
        {
            if (catchGo == null) return;
            if (escapeGo != null) Destroy(escapeGo);
            escapeGo = catchGo;
            catchGo = null;
            escapeT = 0f;
            Vector3 tip = TipPos;
            Vector3 away = escapeGo.transform.position - tip;
            away.y = 0f;
            escapeDir = away.sqrMagnitude > 1e-3f ? away.normalized : Vector3.forward;
            BoatParticles.EmitAt(splash, escapeGo.transform.position, 14);
        }

        void UpdateEscape(float dt)
        {
            if (escapeGo == null) return;
            escapeT += dt;
            float a = escapeT / EscapeTime;
            if (a >= 1f)
            {
                Destroy(escapeGo);
                escapeGo = null;
                return;
            }
            Vector3 p = escapeGo.transform.position + escapeDir * (4f * dt);
            p.y = Waves.HeightAt(p.x, p.z) - 0.05f - a * 0.9f;
            escapeGo.transform.position = p;
            escapeGo.transform.rotation = Quaternion.LookRotation(Vector3.down, escapeDir) * Quaternion.Euler(0f, 0f, Mathf.Sin(escapeT * 34f) * 35f);
            if (a > 0.6f) escapeGo.transform.localScale = Vector3.one * Mathf.Max(0.01f, (1f - a) / 0.4f);
        }

        /// The fish goes into the shared cooler box (on co-op clients via the host). Not sold.
        void CommitCatch(bool announce)
        {
            if (hooked == null || hookedCommitted) return;
            hookedCommitted = true;
            var f = hooked;
            f.fromBoat = hookedFromBoat;
            f.openSea = seaTable;
            FishMarket.AddCatch(f);   // raises GameEvents.FishCaught + FishMarket.Caught
            var rec = PlayerRecords.Local;
            if (rec != null) rec.fishCaught++;
            var kind = f.Kind;
            if (kind != null)
            {
                if (kind.junk) Achievements.Unlock("boot");
                else if (kind.id == "tuna") Achievements.Unlock("tuna");
                else if (kind.id == "marlin") Achievements.Unlock("captain_poke");
            }
            FishingReel.RaiseLanded(f);

            string kg = f.weightKg.ToString("0.0#", Loc.Culture) + " kg";
            string name = f.Name;
            if (kind != null && kind.rarity >= Rarity.Rare && !kind.junk)
            {
                Color c = kind.rarity >= Rarity.Legendary ? new Color(1f, 0.82f, 0.25f) : kind.rarity == Rarity.Epic ? new Color(0.75f, 0.55f, 1f) : new Color(1f, 0.76f, 0.2f);
                GameEvents.RaiseBanner(name + "! " + kg, c);
                BoatSynth.PlayAt(BoatSynth.Sparkle, TipPos, 0.6f, 0.9f, 20f);
            }
            else GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr($"{name} → Kühlbox", $"{name} → cooler box"), kg);
            var gm = GameManager.I;
            if (announce && gm != null && gm.State.fishListedTotal == 0 && gm.State.fishCaught <= 3)
                GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr("Verkaufen: Handy → Fischmarkt", "Sell: phone → Fish Market"));
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
            SetBobberGlow(false);
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
                if (nearLine != null && nearLine.enabled) nearLine.enabled = false;
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
                    if (lifting)
                    {
                        float s = Mathf.Clamp01(liftT / Mathf.Max(0.1f, liftDur));
                        targetPitch = Mathf.Lerp(-4f, -14f, s);
                        targetBend = Mathf.Lerp(26f, 10f, s);
                        crankDeg += 1300f * dt;
                        offset = new Vector3(0f, 0.015f, -0.03f);
                        break;
                    }
                    // the rod tip bows with the tension, swings toward the run, shivers in the red
                    float t01 = fight.Tension / 100f;
                    float shake = fight.Zone == 3 ? Mathf.Sin(idleTime * 47f) * 2.5f : 0f;
                    targetPitch = (fight.Reeling ? 2f : 9f) - t01 * 10f + Mathf.Sin(idleTime * 23f) * 1.5f * t01 + shake * 0.5f;
                    targetBend = 6f + t01 * 34f + shake;
                    targetYaw = fight.Running ? fight.RunDir * (fight.Countering ? -3f : 8f) * t01 : 0f;
                    offset = new Vector3(0f, fight.Reeling ? 0.012f : 0f, -0.02f * t01);
                    if (fight.Reeling) crankDeg += (700f + 500f * (1f - t01)) * dt;
                    else if (fight.Running && fight.Tension > 85f) crankDeg -= 900f * dt;   // the drag gives line
                    break;
                }
                case RodState.Landed:
                    targetPitch = -6f;
                    targetBend = 9f + Mathf.Max(0f, catchLength - 0.4f) * 8f + Mathf.Sin(idleTime * 21f) * 2f;
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
                    // the line is taut under tension and hangs loose when slack
                    if (st == RodState.Reeling && !lifting)
                    {
                        float dist = Vector3.Distance(tip, end);
                        sag = Mathf.Lerp(0.4f, 0f, fight.Tension / 35f) * Mathf.Min(1f, dist / 6f);
                    }
                    else sag = 0f;
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
                    sag = retrieveNoBobber ? 0.35f * (1f - s) : 0.05f;
                    showBobber = !retrieveNoBobber;
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
            // co-op avatars (D.1): the real float position and reel tension instead of the net module's guess
            NetLocalAvatar.PublishFishing((byte)st, bobPos, showBobber, FishingReel.Active ? FishingReel.Tension / 100f : 0f);

            if (!line.enabled) line.enabled = true;
            int n2 = linePts.Length;
            float distAll = Vector3.Distance(tip, end);
            float sagM = sag * Mathf.Min(1f, distAll / 5f);
            // a line pinned in the red trembles
            float jitter = st == RodState.Reeling && !lifting && fight.Zone == 3 ? 0.012f : 0f;
            // 1d: the first 1.5 m from the tip is drawn on the view-model layer (child of the rod rig), the rest in the
            // world – a dock plank right in front of the eye can never cut the line off at the tip
            EnsureNearLine();
            float sNear = nearLine != null && distAll > 0.01f ? Mathf.Clamp01(NearLineLength / distAll) : 0f;
            for (int i = 0; i < n2; i++)
            {
                float s = Mathf.Lerp(sNear, 1f, i / (n2 - 1f));
                linePts[i] = LinePoint(tip, end, s, sagM, jitter, i);
            }
            line.SetPositions(linePts);
            if (nearLine != null)
            {
                for (int i = 0; i < nearPts.Length; i++) nearPts[i] = LinePoint(tip, end, sNear * i / (nearPts.Length - 1f), sagM, jitter, i);
                nearLine.SetPositions(nearPts);
                if (!nearLine.enabled) nearLine.enabled = true;
            }
        }

        Vector3 LinePoint(Vector3 tip, Vector3 end, float s, float sagM, float jitter, int i)
        {
            Vector3 pt = Vector3.Lerp(tip, end, s) + Vector3.down * (sagM * 4f * s * (1f - s) * (1f + 0.3f * (1f - s)));
            if (jitter > 0f) pt += Vector3.up * (Mathf.Sin(idleTime * 70f + s * 27f) * jitter * 4f * s * (1f - s));
            return pt;
        }

        const float NearLineLength = 1.5f;
        LineRenderer nearLine;
        readonly Vector3[] nearPts = new Vector3[5];

        /// The near line lives under the rod rig (so the Player module moves it to the view-model layer) and is rebuilt
        /// with the view model.
        void EnsureNearLine()
        {
            if (nearLine != null || vm == null || vm.root == null) return;
            var go = new GameObject("FishingLineNear");
            go.transform.SetParent(vm.root, false);
            nearLine = go.AddComponent<LineRenderer>();
            nearLine.useWorldSpace = true;
            nearLine.positionCount = nearPts.Length;
            nearLine.widthMultiplier = 0.006f;
            nearLine.numCapVertices = 0;
            nearLine.sharedMaterial = BoatMats.Line;
            nearLine.shadowCastingMode = ShadowCastingMode.Off;
            nearLine.receiveShadows = false;
            nearLine.enabled = false;
            PlayerViewModelCamera.MarkDirty();
        }

        // ------------------------------------------------------------------ prompt
        /// Text tension bar for the interaction prompt (fallback when the UI has no gauge): one glyph per 5 %,
        /// '·' below green, '-' green, '=' yellow, '!' red, '|' = the needle. Built once per fight (no per-frame strings).
        void BuildReelBars()
        {
            string key = GameInput.BindingLabel(GameAction.Use);
            reelBarsLang = Loc.Current;
            reelBarsKey = key;
            if (reelBars == null) reelBars = new string[BarSlots + 1];
            var chars = new char[BarSlots];
            for (int i = 0; i < BarSlots; i++)
            {
                float t = (i + 0.5f) * (100f / BarSlots);
                chars[i] = t < fight.GreenMin ? '·' : t <= fight.GreenMax ? '-' : t <= FishMarket.ReelYellowMax ? '=' : '!';
            }
            string suffix = "]  " + key + Loc.Tr(" halten – Einholen", " hold – reel in");
            for (int m = 0; m <= BarSlots; m++)
            {
                var c = (char[])chars.Clone();
                int idx = Mathf.Clamp(m, 0, BarSlots - 1);
                c[idx] = '|';
                reelBars[m] = "[" + new string(c) + suffix;
            }
        }

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
                case RodState.Reeling:
                    if (lifting) break;
                    if (FishingReel.GaugeByUI) want = promptHold.Text;
                    else
                    {
                        if (reelBars == null || reelBarsLang != Loc.Current || !ReferenceEquals(reelBarsKey, GameInput.BindingLabel(GameAction.Use))) BuildReelBars();
                        int m = Mathf.Clamp(Mathf.FloorToInt(fight.Tension / (100f / BarSlots)), 0, BarSlots);
                        want = reelBars[m];
                    }
                    break;
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
