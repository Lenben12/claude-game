using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Sealebrities
{
    /// The diver's handheld camera – a HOTBAR ITEM: model, filming and photos only while Hands has HoldItem.Camera
    /// active in a hand (right or left; the model mirrors into the offhand).
    ///
    /// K.2 "just keep recording – the Sealog needs a photo" (replaces the round-1 target lock):
    ///  • Under water the camera RECORDS BY ITSELF 0.5 s after diving in (Settings „Unter Wasser automatisch filmen“,
    ///    default on) and stops again when you leave the water. Use (LMB) from the hip = recording on/off (also on land:
    ///    crabs, birds, goats count). Switching it off under water keeps it off until the next dive.
    ///  • Every 0.12 s every animal visible in the viewfinder is DETECTED (FilmDetector / FilmShotEvaluator: in frame,
    ///    within visibility, big enough, not occluded). Each detected INDIVIDUAL keeps its own track; after 1.0 s it
    ///    counts as a sighting (L.2) and becomes a FootageClip (quality = average frame weight, improves up to 8 s) with
    ///    crowd (others in frame), animal event (K.3), selfie flags (K.4) and depth. Max 3 clips per species and dive –
    ///    a better individual takes the weakest slot (the upload keeps the best clip per species anyway).
    ///  • SMART TAPE: storage only runs while at least one animal is in frame.
    ///  • PHOTO: Aim (RMB) + Use (LMB) or Photo (P) – P also from the hip. The SEALOG photo subject is the biggest
    ///    detected animal in the central 40 % frame (≥ 10 % of the screen height, bright enough); it registers the
    ///    species / size record (Journal.RegisterPhoto, co-op clients forward it), the other animals in the photo count
    ///    as sightings. Every photo goes to the gallery and is checked against today's photo challenges (K.3), the
    ///    post-finale legend & record hunt and the selfie achievement.
    ///  • SELFIE VIEW (K.4, SelfieState.Active – the player module's camera): Use = record, Photo / RMB = photo; clips
    ///    with the diver and animals in frame get the selfie bonus (× 1.3, × 1.15 more when the expression fits).
    ///  • Camera mods (Q32): zoom lens (max zoom from GearStats) and the night-vision module – lamp key while aiming
    ///    toggles it: green viewfinder, darkness counts as full light, green photos.
    ///  • 1d: the model lives on the view-model layer (overlay camera) and is pulled back/down when the eye is right in
    ///    front of a wall or plank.
    /// The viewfinder data is published in ViewfinderInfo (K.2 hint set, brackets, Sealog subject), the livestream hook
    /// in FilmFeed. Works identically on co-op clients (creatures are proxies; only ICreature is used).
    [DefaultExecutionOrder(150)]
    public class FilmHandCamera : MonoBehaviour, IHandCamera
    {
        // detection / photo thresholds live in FilmShotEvaluator (constants owner, J.5)
        public const float CaptureSeconds = FilmShotEvaluator.CaptureSeconds;
        public const float MaxClipSeconds = FilmShotEvaluator.MaxClipSeconds;
        public const float MinQuality = FilmShotEvaluator.MinQuality;
        /// Legacy names (round 1).
        public const float MinFraming = 0f;
        public const float HipFactor = FilmShotEvaluator.HandHip;
        public const float EvalInterval = FilmShotEvaluator.DetectInterval;
        /// Default gameplay FOV (art bible §1.13: 70° vertical). Zoom works relative to the player's current FOV.
        public const float BaseFov = 70f;
        public const float BankDelay = 2f;            // seconds out of the water until the dive counts as finished
        public const float PhotoCooldown = FilmShotEvaluator.PhotoCooldown;
        /// K.2: auto-record starts this long after the lens went under water; it stops this long after leaving it.
        public const float AutoRecordDelay = 0.5f, AutoStopDelay = 0.4f;
        public const int MaxClipsPerSpeciesPerDive = 3;
        const float MaxEvalStep = 0.25f;
        const float SightingsFlushSeconds = 2f;
        const int MaxPendingSightings = 48;
        const float TrackPruneAge = 30f;
        const int TrackPruneAbove = 320;
        // 1d pull-back: sphere cast from the eye
        const float PullRadius = 0.10f, PullLength = 0.6f, PullStart = 0.55f, PullFactor = 0.8f, PullSmooth = 0.08f;

        // ------------------------------------------------------------------ IHandCamera
        public bool IsAiming { get; private set; }
        public bool IsRecording => recording;
        public float TapeSecondsLeft => tapeLeft;
        public float TapeSecondsTotal => tapeTotal;
        public float Zoom => zoom;
        /// Legacy: the Sealog subject, else the most prominent detected animal (null = none).
        public SpeciesDef CurrentTarget { get; private set; }
        /// Legacy: 1 = a Sealog photo is possible right now, else how close the animal is to the Sealog size.
        public float CurrentTargetProgress01 { get; private set; }
        /// Legacy: frame weight (0..1.6) of CurrentTarget.
        public float CurrentTargetQuality { get; private set; }

        // ------------------------------------------------------------------ extras for UI / quests / tests
        /// The camera is the active item of one of the local player's hands.
        public bool IsHeld { get; private set; }
        public Hand HeldHand { get; private set; }
        /// Selfie view with the camera in hand (K.4).
        public bool IsSelfie { get; private set; }
        /// K.2 viewfinder hint (also in ViewfinderInfo.Hint).
        public ShotHint ViewHint { get; private set; }
        /// Legacy reason for older viewfinders (see FilmHint).
        public FilmHint Hint { get; private set; }
        /// The animal behind CurrentTarget (null when none).
        public ICreature CurrentCreature { get; private set; }
        /// A valid Sealog photo subject is in the frame.
        public bool CurrentTargetCounts { get; private set; }
        /// Current detection range in meters (K.2: clamp(visibility × 0.9, 4, 25)).
        public float ScanRange { get; private set; } = FilmShotEvaluator.DetectRangeMax;
        /// Clips captured since the last time the player left the water.
        public int ClipsThisDive => clipsThisDive;
        /// Size of the current target (cm), 0 when none.
        public float CurrentTargetSizeCm { get; private set; }
        /// Animals detected in frame right now.
        public int DetectedCount => detector.Count;
        /// Recording was started automatically (under water).
        public bool AutoRecording => recording && autoStarted;
        /// Q32 night-vision module owned / switched on / showing (switched on + aiming).
        public bool NightVisionOwned => nvOwned;
        public bool NightVisionOn => nvOwned && nvOn;
        public bool NightVisionActive => nvOwned && nvOn && IsAiming && usable && !IsSelfie;
        /// The camera used for detection and photos (the selfie lens in the selfie view).
        public Camera Lens => lens;
        /// The detection of the last tick (read-only use – FilmFeed is the stable API).
        public FilmDetector Detector => detector;

        /// Set by the player module while the selfie view uses its own camera (else a camera named "…Selfie…" or the
        /// player's view is used).
        public static Camera SelfieLens;
        /// True while the lamp key toggles the night-vision module (aiming with the module owned) – the player's lamp
        /// should ignore the key then.
        public static bool ConsumesLampKey => instance != null && instance.IsAiming && instance.nvOwned && !instance.IsSelfie && instance.usable;
        /// Raised after a photo was saved (gallery entry).
        public static event Action<PhotoEntry> PhotoTaken;
        /// Raised after every photo with the full analysis (ONE reused report instance – copy what you need).
        public static event Action<FilmPhotoReport> PhotoAnalyzed;

        static FilmHandCamera instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { PhotoTaken = null; PhotoAnalyzed = null; SelfieLens = null; instance = null; }

        // ------------------------------------------------------------------ state
        bool recording, autoStarted, autoSuppressed;
        float autoTimer, autoStopTimer;
        float tapeLeft = 90f, tapeTotal = 90f;
        float zoom = 1f, zoomSetting = 1f;
        float cameraQuality = 0.6f, maxZoom = 1.5f, lampRange;
        bool hasLamp, nvOwned, nvOn;
        int cameraTier = -1;
        bool rigGold;
        float evalTimer, outOfWaterTimer;
        int clipsThisDive;
        bool usable, published;

        bool fovDirty;
        float zoomBaseFov = BaseFov, lastWrittenFov = -1f;
        Camera fovCamera;

        readonly FilmDetector detector = new FilmDetector();
        readonly Dictionary<ICreature, FilmInstanceTrack> tracks = new Dictionary<ICreature, FilmInstanceTrack>(FilmRefComparer.Instance);
        readonly Stack<FilmInstanceTrack> pool = new Stack<FilmInstanceTrack>();
        readonly ICreature[] pruneKeys = new ICreature[64];
        readonly FootageClip scratchClip = new FootageClip();

        FilmCameraRig rig;
        Camera rigView;
        Color cuffColor = FilmRigBuilder.DefaultCuff;

        FilmPhoto photo;
        FilmNightVision nightVision;
        bool photoRequested;
        float photoCooldown;
        readonly FilmPhotoReport report = new FilmPhotoReport();

        // viewfinder brackets (refreshed every frame between detection ticks)
        readonly ICreature[] bracketCreature = new ICreature[ViewfinderInfo.MaxBrackets];
        int bracketCount;

        // sightings batched per species (flushed every 2 s and when the dive ends)
        readonly SpeciesDef[] sightSpecies = new SpeciesDef[MaxPendingSightings];
        readonly int[] sightCount = new int[MaxPendingSightings];
        readonly float[] sightMin = new float[MaxPendingSightings], sightMax = new float[MaxPendingSightings];
        readonly int[] sightZones = new int[MaxPendingSightings];
        int sightN;
        float sightTimer;

        // lens / environment of the last tick
        Camera lens, foundSelfieLens;
        float lensSearchTimer;
        static readonly Camera[] camBuffer = new Camera[24];
        string zoneNow;
        int zoneBitNow;
        float depthNow;
        bool selfieMatchNow;
        float pullBack;
        float lastStorageToast = -100f;
        bool achLegendary, achBertha;

        static string StorageFullLine => Loc.Tr("Speicher voll", "Storage full");

        // ------------------------------------------------------------------ lifecycle
        void Awake()
        {
            instance = this;
            Refs.HandCamera = this;
            photo = new FilmPhoto(transform);
            nightVision = new FilmNightVision(transform);
            cuffColor = CuffFromSettings();
        }

        void OnEnable()
        {
            GameEvents.GameStarted += OnGameStarted;
            GameEvents.GearChanged += OnGearChanged;
            GameEvents.ReturnedToMenu += OnReturnedToMenu;
            GameEvents.PlayerFainted += OnPlayerFainted;
            GameEvents.SpeciesDiscovered += OnSpeciesDiscovered;
            Net.StateSynced += OnSealogChanged;
            Settings.Changed += OnSettingsChanged;
            Journal.FlushRequested += FlushSightings;
        }

        void OnDisable()
        {
            GameEvents.GameStarted -= OnGameStarted;
            GameEvents.GearChanged -= OnGearChanged;
            GameEvents.ReturnedToMenu -= OnReturnedToMenu;
            GameEvents.PlayerFainted -= OnPlayerFainted;
            GameEvents.SpeciesDiscovered -= OnSpeciesDiscovered;
            Net.StateSynced -= OnSealogChanged;
            Settings.Changed -= OnSettingsChanged;
            Journal.FlushRequested -= FlushSightings;
        }

        void OnDestroy()
        {
            if (ReferenceEquals(Refs.HandCamera, this)) Refs.HandCamera = null;
            if (ReferenceEquals(instance, this)) instance = null;
            if (fovDirty && fovCamera != null) fovCamera.fieldOfView = zoomBaseFov;
            if (rig != null) { rig.Destroy(); rig = null; }
            if (photo != null) { photo.Dispose(); photo = null; }
            if (nightVision != null) { nightVision.Dispose(); nightVision = null; }
            ClearView(false);
        }

        // ------------------------------------------------------------------ events
        void OnGameStarted()
        {
            if (recording) SetRecording(false, false);
            ClearTracks();
            ClearView(true);
            zoomSetting = 1f;
            zoom = 1f;
            outOfWaterTimer = 0f;
            clipsThisDive = 0;
            photoRequested = false;
            autoSuppressed = false;
            autoTimer = 0f;
            nvOn = false;
            sightN = 0;
            achLegendary = achBertha = false;
            FilmKnown.MarkDirty();
            FilmTrophy.ResetSession();
            FilmFeed.ResetDive();
            RefreshGear(true);
        }

        void OnGearChanged() => RefreshGear(false);

        void OnReturnedToMenu()
        {
            if (recording) SetRecording(false, false);
            IsAiming = false;
            ClearTracks();
            ClearView(true);
            photoRequested = false;
            sightN = 0;
            fovDirty = false;          // the player module owns the menu FOV
            if (photo != null) photo.CancelFlash();
            if (nightVision != null) nightVision.Hide();
        }

        void OnPlayerFainted()
        {
            // the dive's footage is lost (Footage.LoseDive is called by GameManager.HandleFaint); what was seen stays seen
            if (recording) SetRecording(false, false);
            IsAiming = false;
            FlushSightings();
            ClearTracks();
            ClearView(true);
            clipsThisDive = 0;
            FilmFeed.ResetDive();
        }

        void OnSpeciesDiscovered(SpeciesDef s, float cm, SizeTier tier) => OnSealogChanged();

        void OnSealogChanged()
        {
            FilmKnown.MarkDirty();
            CheckGold();
        }

        void OnSettingsChanged()
        {
            Color c = CuffFromSettings();
            if (c == cuffColor) return;
            cuffColor = c;
            if (rig != null) rig.SetCuffColor(c);
        }

        static Color CuffFromSettings()
        {
            Color c = Settings.Look.cuff;
            c.a = 1f;
            return c;
        }

        void RefreshGear(bool resetTape)
        {
            var gm = GameManager.I;
            if (gm == null) return;
            GearStats st = gm.Gear;
            float newTotal = Mathf.Max(1f, st.tapeSeconds);
            if (resetTape) tapeLeft = newTotal;
            else if (!Mathf.Approximately(newTotal, tapeTotal)) tapeLeft = Mathf.Clamp(tapeLeft + (newTotal - tapeTotal), 0f, newTotal);
            tapeTotal = newTotal;
            cameraQuality = st.cameraQuality;
            maxZoom = Mathf.Max(1f, st.maxZoom);          // includes the zoom lens (GearStats.zoomBonus)
            hasLamp = st.hasLamp;
            lampRange = st.hasLamp ? st.lampRange : 0f;
            nvOwned = st.nightVision;
            if (!nvOwned) nvOn = false;
            zoomSetting = Mathf.Clamp(zoomSetting, 1f, maxZoom);

            var cam = GearDatabase.Equipped(gm.State, GearSlot.Camera);
            int tier = cam != null ? Mathf.Clamp(cam.tier, 0, 4) : 0;
            if (tier != cameraTier)
            {
                cameraTier = tier;
                if (rig != null) RebuildRig();
            }
            CheckGold();
        }

        /// The golden camera (Q52): the hand camera turns gold once the Sealog is 100 %.
        void CheckGold()
        {
            bool g = FilmTrophy.Refresh();
            if (g == rigGold) return;
            rigGold = g;
            if (rig != null) RebuildRig();
        }

        // ------------------------------------------------------------------ model
        void EnsureRig(Camera view)
        {
            if (cameraTier < 0) RefreshGear(true);
            if (rig != null && rig.root == null) { rig.Destroy(); rig = null; }   // destroyed with an old camera
            if (rig == null) { BuildRig(view); return; }
            if (rigView != view)
            {
                rig.root.transform.SetParent(view.transform, false);
                rigView = view;
            }
        }

        void BuildRig(Camera view)
        {
            rig = FilmRigBuilder.Build(Mathf.Max(0, cameraTier), cuffColor, rigGold);
            rig.root.transform.SetParent(view.transform, false);
            rig.ResetPose(false);       // rises into view when equipped
            rig.root.SetActive(false);
            rigView = view;
            PlayerViewModelCamera.MarkDirty();
            // the model sits 0.3–0.55 m in front of the eye; make sure it is not cut by an unusually far near plane
            if (view.nearClipPlane > 0.2f) view.nearClipPlane = 0.08f;
        }

        void RebuildRig()
        {
            Camera view = rigView;
            rig.Destroy();
            rig = null;
            if (view != null) BuildRig(view);
        }

        // ------------------------------------------------------------------ per frame
        float ZoomStepSize => Mathf.Max(0.25f, (maxZoom - 1f) / 8f);

        static bool Alive(IPlayer p) => p != null && !(p is Object o && o == null);

        void Update()
        {
            float dt = Time.deltaTime;
            if (photoCooldown > 0f) photoCooldown -= dt;
            var gm = GameManager.I;
            var player = Refs.Player;
            bool inGame = gm != null && gm.InGame;
            bool alive = Alive(player);
            Camera view = alive ? player.View : null;

            if (!inGame || !alive || view == null)
            {
                GoIdle();
                return;
            }

            EnsureRig(view);

            bool blocked = UIState.BlocksGameplay;
            bool incapacitated = player.IsSeated || player.IsFainting;
            bool held = Hands.TryGetHeld(HoldItem.Camera, out Hand hand);
            IsHeld = held;
            if (held) HeldHand = hand;
            IsSelfie = held && SelfieState.Active;
            if (recording && (!held || incapacitated)) SetRecording(false, false);
            usable = held && !blocked && !incapacitated;

            UpdateDive(player, dt);
            UpdateAutoRecord(player, dt);

            if (usable) HandleInput(player);
            else IsAiming = false;

            // smooth zoom (only while aiming)
            float zt = IsAiming ? zoomSetting : 1f;
            zoom = Mathf.Lerp(zoom, zt, 1f - Mathf.Exp(-dt * 9f));
            if (Mathf.Abs(zoom - zt) < 0.002f) zoom = zt;

            // smart tape: only seconds with an animal in frame use storage (paused while a menu/phone is open)
            if (recording && !blocked && detector.Count > 0)
            {
                tapeLeft -= dt;
                if (tapeLeft <= 0f)
                {
                    tapeLeft = 0f;
                    SetRecording(false, false);
                    NotifyStorageFull();
                }
            }

            sightTimer += dt;
            if (sightTimer >= SightingsFlushSeconds) FlushSightings();
            FilmChallenges.SampleBoat();
        }

        void HandleInput(IPlayer player)
        {
            if (IsSelfie)
            {
                // K.4: Use = record, Photo / RMB = photo (the player module handles H, arm length and expressions)
                IsAiming = false;
                if (GameInput.RecordPressed) ToggleRecordingManual(player);
                if (GameInput.PhotoPressed || GameInput.AimPressed) RequestPhoto();
                return;
            }

            IsAiming = GameInput.AimHeld;
            if (IsAiming)
            {
                float step = GameInput.ZoomStep;
                if (step != 0f) zoomSetting = Mathf.Clamp(zoomSetting + step * ZoomStepSize, 1f, maxZoom);
                if (nvOwned && GameInput.FlashlightPressed) ToggleNightVision();
                if (GameInput.RecordPressed) RequestPhoto();          // LMB while aiming = photo (K.2)
            }
            else if (GameInput.RecordPressed) ToggleRecordingManual(player);
            if (GameInput.PhotoPressed) RequestPhoto();
        }

        void LateUpdate()
        {
            var player = Refs.Player;
            bool alive = Alive(player);
            Camera view = alive ? player.View : null;
            var gm = GameManager.I;
            bool gameplay = gm != null && gm.InGame && !UIState.MainMenuOpen;
            float dt = Time.deltaTime;

            // FOV = base / zoom; only touch the camera while zoomed (and once to restore it). The player module sets its
            // FOV (Q98 setting) every frame before us: whenever the FOV is not the one we wrote, it is the new base.
            if (view != null)
            {
                if (Mathf.Abs(zoom - 1f) > 0.0005f && gameplay && !IsSelfie)
                {
                    float current = view.fieldOfView;
                    if (!fovDirty || fovCamera != view || Mathf.Abs(current - lastWrittenFov) > 0.01f)
                    {
                        zoomBaseFov = current;
                        fovCamera = view;
                        fovDirty = true;
                    }
                    lastWrittenFov = Mathf.Clamp(zoomBaseFov / zoom, 1f, 179f);
                    view.fieldOfView = lastWrittenFov;
                }
                else if (fovDirty)
                {
                    // restore the gameplay FOV; in menu mode the player module owns the FOV (menu camera)
                    if (gameplay && fovCamera != null) fovCamera.fieldOfView = zoomBaseFov;
                    fovDirty = false;
                }
            }

            if (photo != null) photo.Tick(dt);

            // ---------------- detection (after the player's camera moved this frame)
            Camera lensCam = alive && view != null ? ResolveLens(view) : null;
            lens = lensCam;
            if (usable && gameplay && lensCam != null)
            {
                evalTimer += dt;
                if (evalTimer >= EvalInterval)
                {
                    float step = Mathf.Min(evalTimer, MaxEvalStep);
                    evalTimer = 0f;
                    Tick(player, lensCam, step);
                }
                else RefreshBrackets(lensCam);
            }
            else if (published) ClearView(true);

            // ---------------- night vision viewfinder (Q32)
            if (nightVision != null) nightVision.Tick(view, NightVisionActive && gameplay, dt);

            // ---------------- the camera model
            if (rig != null && rig.root != null)
            {
                bool show = usable && alive && view != null && !IsSelfie;
                UpdatePullBack(show ? view : null, player, dt);
                float baseFov = fovDirty ? zoomBaseFov : (view != null ? view.fieldOfView : BaseFov);
                var input = new FilmRigInput
                {
                    visible = show,
                    leftHand = HeldHand == Hand.Left,
                    aiming = IsAiming,
                    recording = recording,
                    lampOn = show && player.FlashlightOn,
                    underwater = show && player.IsUnderwater,
                    hasTarget = detector.Count > 0,
                    framingOk = detector.Subject >= 0,
                    screenState = ScreenState(),
                    zoom = zoom,
                    zoom01 = maxZoom > 1.001f ? (zoom - 1f) / (maxZoom - 1f) : 0f,
                    fieldOfView = view != null ? view.fieldOfView : BaseFov,
                    baseFov = baseFov,
                    tape01 = tapeTotal > 0f ? tapeLeft / tapeTotal : 0f,
                    progress01 = CurrentTargetProgress01,
                    lookDelta = show ? GameInput.Look : Vector2.zero,
                    moveSpeed = show ? player.Velocity.magnitude : 0f,
                    pullBack = pullBack,
                };
                rig.Animate(input, dt);
            }

            // ---------------- photos render after the player's camera moved this frame
            if (photoRequested)
            {
                photoRequested = false;
                if (gameplay && usable && lensCam != null && view != null) TakePhoto(player, lensCam, view);
            }
        }

        /// No game / no player: everything off.
        void GoIdle()
        {
            if (recording) SetRecording(false, false);
            IsAiming = false;
            IsHeld = false;
            IsSelfie = false;
            usable = false;
            zoom = 1f;
            if (published) ClearView(true);
            if (nightVision != null) nightVision.Hide();
        }

        // ------------------------------------------------------------------ recording / storage
        /// Starts recording (tests / other modules). Returns false when the camera is not usable or the storage is full.
        public bool StartRecording()
        {
            if (!usable || tapeLeft <= 0.05f) return false;
            SetRecording(true, false);
            return true;
        }

        public void StopRecording() => SetRecording(false, false);

        /// Queues a photo for this frame's LateUpdate (0.6 s cooldown). False when not possible right now.
        public bool RequestPhoto()
        {
            if (!usable || photoCooldown > 0f) return false;
            photoCooldown = PhotoCooldown;
            photoRequested = true;
            return true;
        }

        /// Night-vision module on/off (only when owned).
        public void SetNightVision(bool on) => nvOn = on && nvOwned;

        /// One log line of the camera state for test harnesses (allocates – not for per-frame use).
        public string DebugSummary()
        {
            var subject = detector.Subject >= 0 ? detector.Species[detector.Subject] : null;
            return (recording ? (autoStarted ? "REC(auto)" : "REC") : "stby") + (IsAiming ? " aim" : "") + (IsSelfie ? " selfie" : "")
                   + (NightVisionOn ? " nv" : "") + " · " + detector.Count + " animals/" + detector.DistinctSpecies + " species"
                   + " · subject " + (subject != null ? subject.id + " q" + detector.SubjectQuality.ToString("0.00") : "-")
                   + " · hint " + ViewHint + " · range " + ScanRange.ToString("0.0") + " m · tape " + tapeLeft.ToString("0") + "/" + tapeTotal.ToString("0")
                   + " s · clips " + clipsThisDive + " · tracks " + tracks.Count + " · zoom x" + zoom.ToString("0.0");
        }

        void ToggleRecordingManual(IPlayer player)
        {
            if (recording)
            {
                SetRecording(false, false);
                // the player stopped it under water: no auto-restart until the next dive
                if (player.IsInWater) autoSuppressed = true;
                return;
            }
            var gm = GameManager.I;
            if (gm == null || !gm.InGame) return;
            if (tapeLeft <= 0.05f)
            {
                NotifyStorageFull();
                return;
            }
            SetRecording(true, false);
        }

        void SetRecording(bool on, bool auto)
        {
            if (recording == on)
            {
                if (on && !auto) autoStarted = false;   // a manual start takes over an automatic one
                return;
            }
            recording = on;
            autoStarted = on && auto;
            autoStopTimer = 0f;
            if (on) evalTimer = EvalInterval;   // evaluate right away when starting
            GameEvents.RaiseRecordingChanged(on);
        }

        void ToggleNightVision()
        {
            nvOn = !nvOn;
            GameEvents.RaiseNotify(NotifyKind.Info, nvOn ? Loc.Tr("Nachtsicht an", "Night vision on") : Loc.Tr("Nachtsicht aus", "Night vision off"));
        }

        void NotifyStorageFull()
        {
            if (Time.unscaledTime - lastStorageToast < 4f) return;
            lastStorageToast = Time.unscaledTime;
            GameEvents.RaiseNotify(NotifyKind.Warning, StorageFullLine, Loc.Tr("Auftauchen", "Surface"));
        }

        /// K.2 auto-record: under water + camera in hand → recording starts after 0.5 s; an automatic recording stops
        /// again 0.4 s after the player left the water (so the dive is banked as before).
        void UpdateAutoRecord(IPlayer player, float dt)
        {
            if (!player.IsInWater)
            {
                autoSuppressed = false;
                autoTimer = 0f;
                if (recording && autoStarted)
                {
                    autoStopTimer += dt;
                    if (autoStopTimer >= AutoStopDelay) SetRecording(false, false);
                }
                return;
            }
            autoStopTimer = 0f;
            bool want = !recording && usable && player.IsUnderwater && Settings.Data.autoRecordUnderwater && !autoSuppressed && tapeLeft > 0.05f;
            if (!want) { autoTimer = 0f; return; }
            autoTimer += dt;
            if (autoTimer >= AutoRecordDelay)
            {
                autoTimer = 0f;
                SetRecording(true, true);
            }
        }

        /// The dive ends once the player has been out of the water for a moment: storage resets, footage is banked.
        void UpdateDive(IPlayer player, float dt)
        {
            if (player.IsInWater) { outOfWaterTimer = 0f; return; }
            outOfWaterTimer += dt;
            if (outOfWaterTimer < BankDelay || recording) return;
            if (tapeLeft < tapeTotal) tapeLeft = tapeTotal;
            if (tracks.Count > 0) ClearTracks();
            if (sightN > 0) FlushSightings();
            clipsThisDive = 0;
            if (Footage.DiveCount > 0)
            {
                Footage.BankDive();
                FilmFeed.ResetDive();
            }
        }

        // ------------------------------------------------------------------ detection
        Camera ResolveLens(Camera view)
        {
            if (!IsSelfie) return view;
            if (SelfieLens != null && SelfieLens.isActiveAndEnabled) return SelfieLens;
            if (foundSelfieLens != null && foundSelfieLens.isActiveAndEnabled) return foundSelfieLens;
            // the player module's selfie camera (named "…Selfie…"): looked up once a second while the view is on
            lensSearchTimer -= Time.unscaledDeltaTime;
            if (lensSearchTimer > 0f) return view;
            lensSearchTimer = 1f;
            foundSelfieLens = null;
            if (Camera.allCamerasCount > camBuffer.Length) return view;
            int n = Camera.GetAllCameras(camBuffer);
            for (int i = 0; i < n; i++)
            {
                var c = camBuffer[i];
                camBuffer[i] = null;
                if (c == null || c == view || !c.isActiveAndEnabled || foundSelfieLens != null) continue;
                if (c.name.IndexOf("selfie", StringComparison.OrdinalIgnoreCase) >= 0) foundSelfieLens = c;
            }
            return foundSelfieLens != null ? foundSelfieLens : view;
        }

        void BuildContext(IPlayer player, Camera lensCam, out FilmShotContext ctx)
        {
            Transform t = lensCam.transform;
            float steadyMul = Buffs.Multiplier(BuffType.SteadyHands);   // food buff: 1 − v (calmer hands)
            if (!(steadyMul > 0f)) steadyMul = 1f;
            float steadiness = 1f - Mathf.Clamp01(player.LookAngularSpeed * steadyMul / 200f);
            Vector3 p = t.position;
            ctx = new FilmShotContext
            {
                cam = lensCam,
                camPos = p,
                camFwd = t.forward,
                aspect = lensCam.aspect,
                ignoreRoot = player.Root,
                steadiness = steadiness,
                lampOn = player.FlashlightOn,
                lampRange = lampRange > 0f ? lampRange : 10f,
                night01 = EnvState.Night01,
                maxRange = FilmShotEvaluator.DetectRange(),
                aimed = IsAiming || IsSelfie,
                cameraQuality = cameraQuality,
                nightVision = NightVisionOn,
                camUnderwater = ReferenceEquals(lensCam, player.View) ? player.IsUnderwater : Waves.IsUnderwater(p),
                ambientBoost = Buffs.Multiplier(BuffType.NightEyes),
            };
        }

        void Tick(IPlayer player, Camera lensCam, float dt)
        {
            BuildContext(player, lensCam, out var ctx);
            detector.Run(ctx);
            ScanRange = ctx.maxRange;
            Vector3 lp = ctx.camPos;
            zoneNow = FilmZones.ZoneAt(lp);
            zoneBitNow = Zones.Bit(zoneNow);
            depthNow = Mathf.Max(0f, Waves.SeaLevel - lp.y);
            selfieMatchNow = IsSelfie && SelfieMatch(SelfieState.Expression);

            if (recording) Accumulate(dt);
            if (tracks.Count > TrackPruneAbove) PruneTracks();

            PublishViewfinder();
            PublishLegacy();
            published = true;
            FilmFeed.Publish(this, detector, zoneNow, depthNow);
            FilmChallenges.OnTick(detector, recording || IsAiming || IsSelfie);
        }

        /// K.4: the expression fits the scene – Scared + a predator in frame, Wow + a new species / Huge / Legendary;
        /// "automatic" (0) shows whatever fits.
        bool SelfieMatch(int expression)
        {
            bool predator = detector.DangerInFrame;
            bool wow = detector.WowInFrame;
            switch ((SelfieExpression)expression)
            {
                case SelfieExpression.Scared: return predator;
                case SelfieExpression.Wow: return wow;
                case SelfieExpression.Auto: return predator || wow;
                default: return false;
            }
        }

        // ------------------------------------------------------------------ clips (per individual)
        void Accumulate(float dt)
        {
            int n = detector.Count;
            int crowd = Mathf.Clamp(n - 1, 0, Progression.MaxCrowd);
            float now = Time.time;
            for (int i = 0; i < n; i++)
            {
                var c = detector.Creature[i];
                var sp = detector.Species[i];
                var t = GetTrack(c, sp, true);
                if (t == null) continue;
                ref var s = ref detector.Shot[i];
                t.lastSeen = now;
                float q = Mathf.Clamp(s.weight, MinQuality, FilmShotEvaluator.MaxQuality);
                if (t.seconds < MaxClipSeconds)
                {
                    float add = Mathf.Min(dt, MaxClipSeconds - t.seconds);
                    t.seconds += add;
                    t.qualitySum += q * add;
                    t.actionSum += Mathf.Clamp01(c.Activity01) * add;
                }
                if (crowd > t.crowd) t.crowd = crowd;
                if (t.eventId == null) t.eventId = FaunaEventInfo.EventAt(c.Position);
                if (IsSelfie)
                {
                    t.selfie = true;
                    if (selfieMatchNow) t.selfieMatch = true;
                }
                t.depth = s.depth;

                if (!t.registered)
                {
                    if (t.seconds >= CaptureSeconds)
                    {
                        t.registered = true;
                        CountSighting(t, sp, s.depth);
                        TryCreateClip(c, sp, t);
                    }
                }
                else if (t.clip != null) UpdateClip(t);
            }
        }

        FilmInstanceTrack GetTrack(ICreature c, SpeciesDef sp, bool create)
        {
            if (c == null || sp == null) return null;
            if (tracks.TryGetValue(c, out var t))
            {
                if (t.speciesId == sp.id && Mathf.Abs(t.sizeCm - c.SizeCm) < 0.01f) return t;
                // a pooled creature object now represents a different animal
                t.Reset();
                t.speciesId = sp.id;
                t.sizeCm = c.SizeCm;
                return t;
            }
            if (!create) return null;
            t = pool.Count > 0 ? pool.Pop() : new FilmInstanceTrack();
            t.Reset();
            t.speciesId = sp.id;
            t.sizeCm = c.SizeCm;
            tracks[c] = t;
            return t;
        }

        void TryCreateClip(ICreature c, SpeciesDef sp, FilmInstanceTrack t)
        {
            var gm = GameManager.I;
            if (gm == null || !gm.InGame) return;
            var list = gm.State.diveFootage;
            int count = 0;
            FootageClip weakest = null;
            float weakestValue = float.MaxValue;
            for (int i = 0; i < list.Count; i++)
            {
                var clip = list[i];
                if (clip == null || clip.speciesId != sp.id) continue;
                count++;
                float v = clip.Value;
                if (v < weakestValue) { weakestValue = v; weakest = clip; }
            }
            if (count < MaxClipsPerSpeciesPerDive)
            {
                var clip = new FootageClip();
                FillClip(clip, c, sp, t);
                t.clip = clip;
                clipsThisDive++;
                Footage.AddClip(clip);
                ClipAchievements(clip);
                return;
            }
            // the species already has its clips this dive: a better individual (bigger, closer, in an event …) takes
            // the weakest slot over
            FillClip(scratchClip, c, sp, t);
            if (weakest == null || scratchClip.Value <= weakestValue) return;
            DetachClip(weakest);
            FillClip(weakest, c, sp, t);
            t.clip = weakest;
            ClipAchievements(weakest);
        }

        void FillClip(FootageClip clip, ICreature c, SpeciesDef sp, FilmInstanceTrack t)
        {
            var gm = GameManager.I;
            DiveSite site = WorldInfo.SiteAt(c.Position);
            clip.speciesId = sp.id;
            clip.sizeCm = c.SizeCm;
            clip.tier = (int)c.Tier;
            clip.quality = Mathf.Clamp(t.AvgQuality, MinQuality, FilmShotEvaluator.MaxQuality);
            clip.seconds = Mathf.Min(MaxClipSeconds, t.seconds);
            clip.action = ClipAction(t);
            clip.siteId = site != null ? site.id : null;
            clip.crowd = Mathf.Clamp(t.crowd, 0, Progression.MaxCrowd);
            clip.eventId = t.eventId;
            clip.selfie = t.selfie;
            clip.selfieMatch = t.selfie && t.selfieMatch;
            clip.depth = t.depth;
            clip.isNewSpecies = t.photoNew;
            clip.isRecord = t.photoRecord;
            clip.day = gm != null ? gm.State.day : clip.day;
        }

        /// Average activity of the individual; an animal event adds +0.3 action (K.3).
        static float ClipAction(FilmInstanceTrack t) =>
            Mathf.Clamp01(t.AvgAction + (string.IsNullOrEmpty(t.eventId) ? 0f : EventActionBonus));

        /// K.3: clips filmed during an animal event get this much extra action.
        public const float EventActionBonus = 0.3f;

        void DetachClip(FootageClip clip)
        {
            foreach (var kv in tracks)
                if (ReferenceEquals(kv.Value.clip, clip)) kv.Value.clip = null;
        }

        /// Longer / better footage of an individual improves its clip in place (no new clip).
        void UpdateClip(FilmInstanceTrack t)
        {
            var gm = GameManager.I;
            if (gm == null) return;
            var clip = t.clip;
            if (!gm.State.diveFootage.Contains(clip)) { t.clip = null; return; }   // already banked or lost
            clip.seconds = Mathf.Min(MaxClipSeconds, t.seconds);
            float avgQ = Mathf.Clamp(t.AvgQuality, MinQuality, FilmShotEvaluator.MaxQuality);
            bool better = false;
            if (avgQ > clip.quality + 0.001f)
            {
                clip.quality = avgQ;
                clip.action = ClipAction(t);
                better = true;
            }
            if (t.crowd > clip.crowd) { clip.crowd = Mathf.Min(t.crowd, Progression.MaxCrowd); better = true; }
            if (clip.eventId == null && t.eventId != null) { clip.eventId = t.eventId; clip.action = ClipAction(t); better = true; }
            if (t.selfie && !clip.selfie) { clip.selfie = true; better = true; }
            if (t.selfie && t.selfieMatch && !clip.selfieMatch) { clip.selfieMatch = true; better = true; }
            clip.depth = t.depth;
            int whole = Mathf.FloorToInt(clip.seconds);
            if (whole > t.reportedSeconds || better)
            {
                t.reportedSeconds = Mathf.Max(t.reportedSeconds, whole);
                FilmFeed.RaiseClipImproved(clip);
            }
            // the shared journal's "best clip" stat is only touched where the shared state lives (host / offline)
            if (Net.HasAuthority)
            {
                var e = Journal.Find(clip.speciesId);
                if (e != null && e.discovered)
                {
                    float v = clip.Value;
                    if (v > e.bestClipValue) e.bestClipValue = v;
                }
            }
        }

        void ClipAchievements(FootageClip clip)
        {
            if (!achLegendary && clip.Tier == SizeTier.Legendary)
            {
                achLegendary = true;
                if (!Achievements.IsUnlocked("legendary")) Achievements.Unlock("legendary");
            }
            if (!achBertha && clip.speciesId == "humpback")
            {
                achBertha = true;
                if (!Achievements.IsUnlocked("big_bertha")) Achievements.Unlock("big_bertha");
            }
        }

        void ClearTracks()
        {
            foreach (var t in tracks.Values)
            {
                t.Reset();
                pool.Push(t);
            }
            tracks.Clear();
        }

        /// Drops tracks of animals not seen for a while (long recordings in the open world), clip-less ones first.
        void PruneTracks()
        {
            float cutoff = Time.time - TrackPruneAge;
            int n = 0;
            foreach (var kv in tracks)
            {
                if (n >= pruneKeys.Length) break;
                if (kv.Value.lastSeen < cutoff && kv.Value.clip == null) pruneKeys[n++] = kv.Key;
            }
            for (int i = 0; i < n; i++)
            {
                if (tracks.TryGetValue(pruneKeys[i], out var t))
                {
                    tracks.Remove(pruneKeys[i]);
                    t.Reset();
                    pool.Push(t);
                }
                pruneKeys[i] = null;
            }
        }

        // ------------------------------------------------------------------ sightings (L.2)
        void CountSighting(FilmInstanceTrack t, SpeciesDef sp, float depth)
        {
            if (t.sighted) return;
            t.sighted = true;
            QueueSighting(sp, depth);
        }

        void QueueSighting(SpeciesDef sp, float depth)
        {
            if (sp == null) return;
            for (int i = 0; i < sightN; i++)
            {
                if (!ReferenceEquals(sightSpecies[i], sp)) continue;
                sightCount[i]++;
                if (depth < sightMin[i]) sightMin[i] = depth;
                if (depth > sightMax[i]) sightMax[i] = depth;
                sightZones[i] |= zoneBitNow;
                return;
            }
            if (sightN >= MaxPendingSightings) FlushSightings();
            sightSpecies[sightN] = sp;
            sightCount[sightN] = 1;
            sightMin[sightN] = depth;
            sightMax[sightN] = depth;
            sightZones[sightN] = zoneBitNow;
            sightN++;
        }

        /// Journal.AddSightings per species (clients: core batches them to the host every 10 s).
        void FlushSightings()
        {
            sightTimer = 0f;
            if (sightN == 0) return;
            var gm = GameManager.I;
            bool ok = gm != null && gm.InGame;
            for (int i = 0; i < sightN; i++)
            {
                if (ok && sightSpecies[i] != null) Journal.AddSightings(sightSpecies[i].id, sightCount[i], sightMin[i], sightMax[i], sightZones[i]);
                sightSpecies[i] = null;
            }
            sightN = 0;
        }

        // ------------------------------------------------------------------ viewfinder (K.2)
        void PublishViewfinder()
        {
            int n = detector.Count;
            ViewfinderInfo.DetectedCount = n;
            int nb = Mathf.Min(n, ViewfinderInfo.MaxBrackets);
            for (int i = 0; i < nb; i++)
            {
                bracketCreature[i] = detector.Creature[i];
                ViewfinderInfo.Brackets[i] = detector.Shot[i].rect;
                ViewfinderInfo.BracketTier[i] = (byte)detector.Creature[i].Tier;
                ViewfinderInfo.BracketNew[i] = !detector.Known[i];
            }
            for (int i = nb; i < bracketCount; i++) bracketCreature[i] = null;
            bracketCount = nb;
            ViewfinderInfo.BracketCount = nb;
            ViewfinderInfo.SealogSubject = detector.Subject >= 0 ? detector.Species[detector.Subject] : null;
            ViewfinderInfo.ThumbQuality01 = detector.SubjectQuality;
            ViewHint = ComputeHint();
            ViewfinderInfo.Hint = ViewHint;
        }

        /// Brackets follow the animals every frame (the detection itself runs at 8 Hz).
        void RefreshBrackets(Camera lensCam)
        {
            for (int i = 0; i < bracketCount; i++)
            {
                var c = bracketCreature[i];
                if (c == null || c is Object o && o == null || !c.IsAlive) continue;
                if (FilmShotEvaluator.ViewportRect(lensCam, c.WorldBounds, out var r)) ViewfinderInfo.Brackets[i] = r;
            }
        }

        ShotHint ComputeHint()
        {
            if (!usable) return ShotHint.None;
            if (IsAiming || IsSelfie)
            {
                if (detector.Subject >= 0) return detector.Known[detector.Subject] ? ShotHint.SealogReady : ShotHint.New;
                if (detector.SubjectTooDark) return ShotHint.TooDark;
                if (detector.InFrame > 0) return ShotHint.Closer;
                return ShotHint.NoAnimals;
            }
            if (recording) return detector.Count > 0 ? ShotHint.Recording : ShotHint.NoAnimals;
            return ShotHint.None;
        }

        /// The legacy IHandCamera target fields + FilmHint for older readers.
        void PublishLegacy()
        {
            int best = detector.Subject >= 0 ? detector.Subject : (detector.Count > 0 ? 0 : -1);
            if (best >= 0)
            {
                var c = detector.Creature[best];
                CurrentCreature = c;
                CurrentTarget = detector.Species[best];
                CurrentTargetSizeCm = c.SizeCm;
                CurrentTargetQuality = Mathf.Clamp(detector.Shot[best].weight, 0f, FilmShotEvaluator.MaxQuality);
                CurrentTargetCounts = detector.Subject >= 0;
                CurrentTargetProgress01 = detector.Subject >= 0 ? 1f : Mathf.Clamp01(detector.Shot[best].extent / FilmShotEvaluator.SealogMinExtentFor(c.SizeCm));
            }
            else ClearTarget();

            if (!usable) Hint = FilmHint.None;
            else if (detector.Subject >= 0) Hint = detector.Known[detector.Subject] ? FilmHint.Logged : FilmHint.Good;
            else if (detector.SubjectTooDark) Hint = FilmHint.TooDark;
            else if (detector.Count > 0) Hint = FilmHint.Good;
            else if (detector.AnyTooSmall) Hint = FilmHint.TooSmall;
            else if (detector.AnyTooFar) Hint = FilmHint.TooFar;
            else Hint = FilmHint.NoAnimal;
        }

        void ClearTarget()
        {
            CurrentCreature = null;
            CurrentTarget = null;
            CurrentTargetQuality = 0f;
            CurrentTargetProgress01 = 0f;
            CurrentTargetSizeCm = 0f;
            CurrentTargetCounts = false;
        }

        void ClearView(bool raiseFeed)
        {
            detector.Clear();
            ViewfinderInfo.Clear();
            for (int i = 0; i < bracketCount; i++) bracketCreature[i] = null;
            bracketCount = 0;
            ClearTarget();
            Hint = FilmHint.None;
            ViewHint = ShotHint.None;
            FilmFeed.Clear(raiseFeed);
            published = false;
        }

        int ScreenState()
        {
            if (NightVisionOn && (IsAiming || recording)) return FilmMaterials.ScreenNight;
            if (!recording && !IsAiming) return FilmMaterials.ScreenStandby;
            if (detector.Subject >= 0) return detector.Known[detector.Subject] ? FilmMaterials.ScreenCaptured : FilmMaterials.ScreenNew;
            if (detector.SubjectTooDark) return FilmMaterials.ScreenWeak;
            return detector.Count > 0 ? FilmMaterials.ScreenGood : FilmMaterials.ScreenLive;
        }

        /// 1d: a sphere cast from the eye; something closer than 0.55 m pushes the held camera back/down (smoothed).
        void UpdatePullBack(Camera view, IPlayer player, float dt)
        {
            float target = 0f;
            if (view != null)
            {
                Transform vt = view.transform;
                if (Physics.SphereCast(vt.position, PullRadius, vt.forward, out RaycastHit hit, PullLength, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    var root = player != null ? player.Root : null;
                    bool own = root != null && hit.collider != null && hit.collider.transform.IsChildOf(root);
                    if (!own && hit.distance < PullStart) target = (PullStart - hit.distance) * PullFactor;
                }
            }
            pullBack = Mathf.Lerp(pullBack, target, 1f - Mathf.Exp(-dt / PullSmooth));
            if (pullBack < 0.0005f) pullBack = 0f;
        }

        // ------------------------------------------------------------------ photos (K.2 Sealog photo)
        void TakePhoto(IPlayer player, Camera lensCam, Camera view)
        {
            var gm = GameManager.I;
            if (gm == null || !gm.InGame) return;

            // a fresh analysis with this frame's final lens pose (the 8 Hz tick may be up to 0.12 s old)
            BuildContext(player, lensCam, out var ctx);
            detector.Run(ctx);
            Vector3 lp = ctx.camPos;
            string zone = FilmZones.ZoneAt(lp);
            zoneNow = zone;
            zoneBitNow = Zones.Bit(zone);
            FillReport(player, ctx, zone);

            var site = WorldInfo.SiteAt(player.Root != null ? player.Root.position : lp);
            var tex = CaptureTexture(lensCam, view);
            PhotoEntry entry = null;
            if (tex != null)
            {
                if (NightVisionOn) FilmNightVision.TintPhoto(tex);
                entry = Gallery.Add(tex, report.subject != null ? report.subject.id : "", site != null ? site.id : "");
            }
            report.photo = entry;
            if (entry == null)
            {
                GameEvents.RaiseNotify(NotifyKind.Warning, Loc.Tr("Foto fehlgeschlagen", "Photo failed"));
                return;
            }
            if (photo != null) photo.Flash(lensCam, ReferenceEquals(lensCam, view));   // the shutter sound: FX on Gallery.Added
            if (rig != null) rig.Shutter();

            // ---- Sealog: the subject registers species + size (shared, co-op clients forward it)
            if (report.subject != null)
            {
                var r = Journal.RegisterPhoto(report.subject, report.subjectCm, report.subjectTier, report.quality, entry.file, report.subjectDepth, zoneBitNow);
                report.isNew = r.isNew;
                report.isRecord = r.isRecord;
                var t = GetTrack(report.subjectCreature, report.subject, true);
                if (t != null)
                {
                    t.sighted = true;   // RegisterPhoto counted it as one sighting
                    if (r.isNew) t.photoNew = true;
                    if (r.isRecord) t.photoRecord = true;
                    if (t.clip != null)
                    {
                        if (r.isNew) t.clip.isNewSpecies = true;
                        if (r.isRecord) t.clip.isRecord = true;
                    }
                }
            }
            // ---- the other animals in the photo count as sightings (once per individual and dive)
            for (int i = 0; i < detector.Count; i++)
            {
                if (i == detector.Subject) continue;
                var t = GetTrack(detector.Creature[i], detector.Species[i], true);
                if (t != null && !t.sighted) { t.sighted = true; QueueSighting(detector.Species[i], detector.Shot[i].depth); }
            }

            if (report.selfie && report.HasSpecies("green_turtle") && !Achievements.IsUnlocked("gerald_selfie")) Achievements.Unlock("gerald_selfie");

            FilmPhotoBook.Record(entry, report);
            FilmLegends.OnPhoto(report);
            FilmChallenges.OnPhoto(report, lensCam);
            NotifyPhoto();

            PhotoTaken?.Invoke(entry);
            PhotoAnalyzed?.Invoke(report);

            // the viewfinder shows the fresh analysis right away
            PublishViewfinder();
            PublishLegacy();
            published = true;
        }

        void FillReport(IPlayer player, in FilmShotContext ctx, string zone)
        {
            report.Clear();
            int si = detector.Subject;
            if (si >= 0)
            {
                var c = detector.Creature[si];
                ref var s = ref detector.Shot[si];
                report.subject = detector.Species[si];
                report.subjectCreature = c;
                report.subjectCm = c.SizeCm;
                report.subjectTier = c.Tier;
                report.subjectDistance = s.distance;
                report.subjectDepth = s.depth;
                report.subjectFill = s.fill;
                report.subjectPos = c.WorldBounds.center;
                report.quality = detector.SubjectQuality;
                report.species[report.speciesCount++] = report.subject;
            }
            for (int i = 0; i < detector.DistinctSpecies && report.speciesCount < FilmPhotoReport.MaxSpecies; i++)
            {
                var sp = detector.DistinctList[i];
                if (sp == null || ReferenceEquals(sp, report.subject)) continue;
                report.species[report.speciesCount++] = sp;
            }
            report.animalCount = detector.Count;
            report.selfie = IsSelfie;
            report.selfieExpression = SelfieState.Expression;
            report.selfieMatch = IsSelfie && detector.Count > 0 && SelfieMatch(SelfieState.Expression);
            report.cameraPos = ctx.camPos;
            report.cameraPitch = Mathf.Asin(Mathf.Clamp(ctx.camFwd.y, -1f, 1f)) * Mathf.Rad2Deg;
            report.cameraDepth = Mathf.Max(0f, Waves.SeaLevel - ctx.camPos.y);
            report.lampOn = player.FlashlightOn;
            report.nightVision = NightVisionOn;
            var gm = GameManager.I;
            report.timeOfDay = gm != null ? gm.State.timeOfDay : 0.5f;
            report.zone = zone;
            report.eventId = FaunaEventInfo.EventAt(si >= 0 ? report.subjectPos : ctx.camPos);
            if (report.eventId == null) report.eventId = FaunaEventInfo.EventAt(ctx.camPos);
        }

        Texture2D CaptureTexture(Camera lensCam, Camera view)
        {
            if (photo == null || lensCam == null) return null;
            bool selfieLens = !ReferenceEquals(lensCam, view);
            bool rigWasShown = rig != null && rig.root != null && rig.root.activeSelf;
            if (rigWasShown) rig.root.SetActive(false);
            if (nightVision != null) nightVision.SetCaptureHidden(true);
            try
            {
                return selfieLens ? photo.Capture(lensCam, Mathf.Max(0.05f, lensCam.nearClipPlane)) : photo.Capture(lensCam);
            }
            finally
            {
                if (rigWasShown && rig != null && rig.root != null) rig.root.SetActive(true);
                if (nightVision != null) nightVision.SetCaptureHidden(false);
            }
        }

        /// One short notification per photo (L.10): new species → the Sealog popup covers it; records → the record
        /// popup; legends / gold records announce themselves (FilmLegends).
        void NotifyPhoto()
        {
            if (report.legendId != null || report.goldRecord) return;
            var sp = report.subject;
            if (sp != null)
            {
                if (report.isNew || report.isRecord) return;
                var e = Journal.Find(sp.id);
                int n = e != null ? Mathf.Max(1, e.photos) : 1;
                GameEvents.RaiseNotify(NotifyKind.Sealog, sp.Name, Loc.Tr("Foto ", "Photo ") + n);
            }
            else if (detector.Count > 0 || detector.InFrame > 0)
                GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr("Näher ran oder zoomen", "Get closer or zoom in"), Loc.Tr("Foto", "Photo"));
            else
                GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr("Foto gespeichert", "Photo saved"));
        }
    }
}
