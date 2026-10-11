using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Sealebrities
{
    /// The diver's handheld camera – a HOTBAR ITEM (playtest feedback #6/#7): model, filming and photos only while
    /// Hands has HoldItem.Camera active in a hand (right or left; the model mirrors into the offhand).
    ///
    /// Controls (GameInput): Aim (RMB) = viewfinder, Use (LMB) = start/stop recording, mouse wheel = zoom while
    /// aiming, Photo (P) while aiming = photo into the phone gallery (Gallery.Add + shutter flash + toast).
    ///
    /// Filming (feedback #16): an animal is only a target when it is really framed (FilmShotEvaluator: central 70 %,
    /// ≥ 8 % of the screen height, ≤ min(12 m, visibility), clear line of sight). Recording a target for 1.5 s makes a
    /// clip (Footage.AddClip, per player). Only AIMED footage registers the animal in the Sealog (Journal.RegisterFilmed,
    /// forwarded to the host on co-op clients); hip-filming records lower-quality clips only. Every new species
    /// discovery saves an automatic photo to the gallery. Works identically on co-op clients: creatures there are
    /// proxies in CreatureRegistry and only the ICreature interface is used.
    /// The viewfinder overlay is drawn by the UI module from the IHandCamera state (+ Hint, CurrentCreature).
    [DefaultExecutionOrder(150)]
    public class FilmHandCamera : MonoBehaviour, IHandCamera
    {
        public const float CaptureSeconds = 1.5f;     // seconds of usable footage until an instance becomes a clip / is logged
        public const float MaxClipSeconds = 8f;       // an instance's clip keeps improving up to this length
        /// Framing score (coverage × centering × distance × light × steadiness) a moment needs to count as footage.
        public const float MinFraming = 0.3f;
        /// Lowest stored clip quality.
        public const float MinQuality = 0.1f;
        /// Clip quality multiplier when filming from the hip (not aiming).
        public const float HipFactor = 0.6f;
        public const float EvalInterval = 0.12f;
        /// Default gameplay FOV (art bible §1.13: 70° vertical). Zoom works relative to the player's current FOV.
        public const float BaseFov = 70f;
        public const float BankDelay = 2f;            // seconds out of the water until the dive counts as finished
        public const float PhotoCooldown = 0.6f;
        const float TargetStickiness = 1.25f;         // a new target must score 25 % better to steal the lock
        const float NewSpeciesBias = 2.5f;            // target score multiplier for species not yet in the Sealog
        const float MaxEvalStep = 0.25f;              // clamp accumulated time after frame hitches
        const float CoarseMargin = 15f;               // long animals: center may be farther than their nearest point

        // ------------------------------------------------------------------ IHandCamera
        public bool IsAiming { get; private set; }
        public bool IsRecording => recording;
        public float TapeSecondsLeft => tapeLeft;
        public float TapeSecondsTotal => tapeTotal;
        public float Zoom => zoom;
        public SpeciesDef CurrentTarget { get; private set; }
        public float CurrentTargetProgress01 { get; private set; }
        public float CurrentTargetQuality { get; private set; }

        // ------------------------------------------------------------------ extras for UI / quests (not in IHandCamera)
        /// The camera is the active item of one of the local player's hands.
        public bool IsHeld { get; private set; }
        public Hand HeldHand { get; private set; }
        /// Why the current view does (not) count – for viewfinder icons.
        public FilmHint Hint { get; private set; }
        /// The animal behind CurrentTarget (null when none).
        public ICreature CurrentCreature => target;
        /// The current target is framed well enough to count as footage.
        public bool CurrentTargetCounts { get; private set; }
        /// Current scan range in meters (min(12 m, visibility)).
        public float ScanRange { get; private set; } = FilmShotEvaluator.MaxScanRange;
        /// Clips captured since the last time the player left the water.
        public int ClipsThisDive => clipsThisDive;
        /// Size of the current target (cm), 0 when none.
        public float CurrentTargetSizeCm { get; private set; }
        /// Raised after a manual photo was saved (not for automatic discovery photos).
        public static event Action<PhotoEntry> PhotoTaken;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { PhotoTaken = null; }

        // ------------------------------------------------------------------ state
        bool recording;
        float tapeLeft = 90f, tapeTotal = 90f;
        float zoom = 1f, zoomSetting = 1f;
        float cameraQuality = 0.6f, maxZoom = 1.5f, lampRange;
        int cameraTier = -1;
        float evalTimer, outOfWaterTimer;
        int clipsThisDive;
        bool usable;

        bool fovDirty;
        float zoomBaseFov = BaseFov;
        Camera fovCamera;

        ICreature target;
        FilmInstanceTrack targetTrack;
        readonly Dictionary<ICreature, FilmInstanceTrack> tracks = new Dictionary<ICreature, FilmInstanceTrack>(FilmRefComparer.Instance);
        readonly Stack<FilmInstanceTrack> pool = new Stack<FilmInstanceTrack>();

        FilmCameraRig rig;
        Camera rigView;
        Color cuffColor = FilmRigBuilder.DefaultCuff;

        FilmPhoto photo;
        bool photoRequested;
        float photoCooldown;
        SpeciesDef pendingDiscovery;
        string pendingDiscoverySite;
        int discoveryDelay;

        float lastHipHint = -100f, lastPhotoHint = -100f;
        int hipHints;

        static string StorageFullText => Loc.Tr("Speicher voll – an Land oder auf dem Boot sichern.", "Storage full – save it on land or on the boat.");

        // ------------------------------------------------------------------ lifecycle
        void Awake()
        {
            Refs.HandCamera = this;
            photo = new FilmPhoto(transform);
            cuffColor = CuffFromSettings();
        }

        void OnEnable()
        {
            GameEvents.GameStarted += OnGameStarted;
            GameEvents.GearChanged += OnGearChanged;
            GameEvents.ReturnedToMenu += OnReturnedToMenu;
            GameEvents.PlayerFainted += OnPlayerFainted;
            Settings.Changed += OnSettingsChanged;
        }

        void OnDisable()
        {
            GameEvents.GameStarted -= OnGameStarted;
            GameEvents.GearChanged -= OnGearChanged;
            GameEvents.ReturnedToMenu -= OnReturnedToMenu;
            GameEvents.PlayerFainted -= OnPlayerFainted;
            Settings.Changed -= OnSettingsChanged;
        }

        void OnDestroy()
        {
            if (ReferenceEquals(Refs.HandCamera, this)) Refs.HandCamera = null;
            if (fovDirty && fovCamera != null) fovCamera.fieldOfView = zoomBaseFov;
            if (rig != null) { rig.Destroy(); rig = null; }
            if (photo != null) { photo.Dispose(); photo = null; }
        }

        // ------------------------------------------------------------------ events
        void OnGameStarted()
        {
            if (recording) SetRecording(false);
            ClearTracks();
            ClearTarget();
            zoomSetting = 1f;
            zoom = 1f;
            outOfWaterTimer = 0f;
            clipsThisDive = 0;
            photoRequested = false;
            pendingDiscovery = null;
            hipHints = 0;
            RefreshGear(true);
        }

        void OnGearChanged() => RefreshGear(false);

        void OnReturnedToMenu()
        {
            if (recording) SetRecording(false);
            IsAiming = false;
            ClearTracks();
            ClearTarget();
            photoRequested = false;
            pendingDiscovery = null;
            fovDirty = false;          // the player module owns the menu FOV
            if (photo != null) photo.CancelFlash();
        }

        void OnPlayerFainted()
        {
            // the dive's footage is lost (Footage.LoseDive is called by GameManager.HandleFaint)
            if (recording) SetRecording(false);
            IsAiming = false;
            ClearTracks();
            ClearTarget();
            clipsThisDive = 0;
            pendingDiscovery = null;
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
            maxZoom = Mathf.Max(1f, st.maxZoom);
            lampRange = st.hasLamp ? st.lampRange : 0f;
            zoomSetting = Mathf.Clamp(zoomSetting, 1f, maxZoom);

            var cam = GearDatabase.Equipped(gm.State, GearSlot.Camera);
            int tier = cam != null ? Mathf.Clamp(cam.tier, 0, 4) : 0;
            if (tier != cameraTier)
            {
                cameraTier = tier;
                if (rig != null) RebuildRig();
            }
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
            rig = FilmRigBuilder.Build(Mathf.Max(0, cameraTier), cuffColor);
            rig.root.transform.SetParent(view.transform, false);
            rig.ResetPose(false);       // rises into view when equipped
            rig.root.SetActive(false);
            rigView = view;
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
                if (recording) SetRecording(false);
                IsAiming = false;
                IsHeld = false;
                usable = false;
                zoom = 1f;
                ClearTarget();
                Hint = FilmHint.None;
                return;
            }

            EnsureRig(view);

            bool blocked = UIState.BlocksGameplay;
            bool incapacitated = player.IsSeated || player.IsFainting;
            bool held = Hands.TryGetHeld(HoldItem.Camera, out Hand hand);
            IsHeld = held;
            if (held) HeldHand = hand;
            if (recording && (!held || incapacitated)) SetRecording(false);
            usable = held && !blocked && !incapacitated;

            UpdateDive(player, dt);

            // input (ignored while a UI blocks gameplay or the camera is not in hand)
            if (usable)
            {
                IsAiming = GameInput.AimHeld;
                if (IsAiming)
                {
                    float step = GameInput.ZoomStep;
                    if (step != 0f) zoomSetting = Mathf.Clamp(zoomSetting + step * ZoomStepSize, 1f, maxZoom);
                }
                if (GameInput.RecordPressed) ToggleRecording();
                if (GameInput.PhotoPressed)
                {
                    if (IsAiming) RequestPhoto();
                    else if (Time.unscaledTime - lastPhotoHint > 6f)
                    {
                        lastPhotoHint = Time.unscaledTime;
                        GameEvents.RaiseToast(Loc.Tr("Foto: erst zielen ", "Photo: aim first ") + Key(GameAction.Aim));
                    }
                }
            }
            else IsAiming = false;

            // smooth zoom (only while aiming)
            float zt = IsAiming ? zoomSetting : 1f;
            zoom = Mathf.Lerp(zoom, zt, 1f - Mathf.Exp(-dt * 9f));
            if (Mathf.Abs(zoom - zt) < 0.002f) zoom = zt;

            // storage (paused while a menu/phone is open)
            if (recording && !blocked)
            {
                tapeLeft -= dt;
                if (tapeLeft <= 0f)
                {
                    tapeLeft = 0f;
                    SetRecording(false);
                    GameEvents.RaiseToast(StorageFullText);
                }
            }

            // shot evaluation: while recording (accumulates) or aiming (viewfinder feedback only)
            if (usable && (recording || IsAiming))
            {
                evalTimer += dt;
                if (evalTimer >= EvalInterval)
                {
                    float step = Mathf.Min(evalTimer, MaxEvalStep);
                    evalTimer = 0f;
                    Evaluate(player, view, step);
                }
            }
            else if (!recording && !IsAiming)
            {
                ClearTarget();
                Hint = FilmHint.None;
            }
        }

        void LateUpdate()
        {
            var player = Refs.Player;
            bool alive = Alive(player);
            Camera view = alive ? player.View : null;
            var gm = GameManager.I;
            bool gameplay = gm != null && gm.InGame && !UIState.MainMenuOpen;

            // FOV = base / zoom; only touch the camera while zoomed (and once to restore it)
            if (view != null)
            {
                if (Mathf.Abs(zoom - 1f) > 0.0005f && gameplay)
                {
                    if (!fovDirty || fovCamera != view)
                    {
                        zoomBaseFov = view.fieldOfView;
                        fovCamera = view;
                        fovDirty = true;
                    }
                    view.fieldOfView = zoomBaseFov / zoom;
                }
                else if (fovDirty)
                {
                    // restore the gameplay FOV; in menu mode the player module owns the FOV (menu camera)
                    if (gameplay && fovCamera != null) fovCamera.fieldOfView = zoomBaseFov;
                    fovDirty = false;
                }
            }

            float dt = Time.deltaTime;
            if (photo != null) photo.Tick(dt);

            if (rig == null || rig.root == null) return;
            bool show = usable && alive && view != null;
            float baseFov = fovDirty ? zoomBaseFov : (view != null ? view.fieldOfView : BaseFov);
            var input = new FilmRigInput
            {
                visible = show,
                leftHand = HeldHand == Hand.Left,
                aiming = IsAiming,
                recording = recording,
                lampOn = show && player.FlashlightOn,
                underwater = show && player.IsUnderwater,
                hasTarget = CurrentTarget != null,
                framingOk = CurrentTargetCounts,
                zoom = zoom,
                zoom01 = maxZoom > 1.001f ? (zoom - 1f) / (maxZoom - 1f) : 0f,
                fieldOfView = view != null ? view.fieldOfView : BaseFov,
                baseFov = baseFov,
                tape01 = tapeTotal > 0f ? tapeLeft / tapeTotal : 0f,
                progress01 = CurrentTargetProgress01,
                lookDelta = show ? GameInput.Look : Vector2.zero,
                moveSpeed = show ? player.Velocity.magnitude : 0f,
            };
            rig.Animate(input, dt);

            // photos render after the player's camera moved this frame
            if (view != null && gameplay)
            {
                if (photoRequested) { photoRequested = false; TakePhoto(view, player); }
                // the automatic discovery photo waits a few frames: its render + PNG write must not land on the same
                // frame as the "Neu im Sealog" popup and its sound
                else if (pendingDiscovery != null && --discoveryDelay <= 0) { var sp = pendingDiscovery; pendingDiscovery = null; SaveDiscoveryPhoto(view, sp, pendingDiscoverySite); }
            }
            else { photoRequested = false; pendingDiscovery = null; }
        }

        // ------------------------------------------------------------------ recording / storage
        void ToggleRecording()
        {
            if (recording) { SetRecording(false); return; }
            var gm = GameManager.I;
            if (gm == null || !gm.InGame) return;
            if (tapeLeft <= 0.05f)
            {
                GameEvents.RaiseToast(StorageFullText);
                return;
            }
            SetRecording(true);
        }

        void SetRecording(bool on)
        {
            if (recording == on) return;
            recording = on;
            evalTimer = on ? EvalInterval : 0f;   // evaluate right away when starting
            GameEvents.RaiseRecordingChanged(on);
        }

        /// The dive ends once the player has been out of the water for a moment: storage resets, footage is banked.
        void UpdateDive(IPlayer player, float dt)
        {
            if (player.IsInWater) { outOfWaterTimer = 0f; return; }
            outOfWaterTimer += dt;
            if (outOfWaterTimer < BankDelay || recording) return;
            if (tapeLeft < tapeTotal) tapeLeft = tapeTotal;
            if (tracks.Count > 0) ClearTracks();
            clipsThisDive = 0;
            if (Footage.DiveCount > 0) Footage.BankDive();
        }

        // ------------------------------------------------------------------ evaluation
        void Evaluate(IPlayer player, Camera view, float dt)
        {
            Transform camT = view.transform;
            float steadiness = 1f - Mathf.Clamp01(player.LookAngularSpeed / 200f);
            ScanRange = FilmShotEvaluator.ScanRange();
            var ctx = new FilmShotContext
            {
                cam = view,
                camPos = camT.position,
                camFwd = camT.forward,
                aspect = view.aspect,
                ignoreRoot = player.Root,
                steadiness = steadiness,
                lampOn = player.FlashlightOn,
                lampRange = lampRange > 0f ? lampRange : 10f,
                night01 = EnvState.Night01,
                maxRange = ScanRange,
            };

            ICreature best = null;
            SpeciesDef bestSpecies = null;
            FilmShot bestShot = default;
            float bestScore = 0f;
            float lockedScore = -1f;
            FilmShot lockedShot = default;
            SpeciesDef lockedSpecies = null;
            FilmHint nearMiss = FilmHint.NoAnimal;
            float nearMissOff = float.MaxValue;

            float coarse = ScanRange + CoarseMargin;
            Vector3 camPos = ctx.camPos;
            var list = CreatureRegistry.Active;
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                if (c == null) continue;
                if (c is Object uo && uo == null) continue;   // destroyed but not yet unregistered (Unity fake-null)
                if (c.IsHallucination || !c.IsAlive) continue;
                if (c.Transform == null) continue;
                if ((c.Position - camPos).sqrMagnitude > coarse * coarse) continue;
                var sp = c.Species;
                if (sp == null) continue;

                var fail = FilmShotEvaluator.Check(c, sp, ctx, out var shot);
                if (fail == FilmHint.NoAnimal) continue;
                if (fail != FilmHint.None)
                {
                    if (shot.offCenter < nearMissOff) { nearMissOff = shot.offCenter; nearMiss = fail; }
                    continue;
                }
                bool locked = ReferenceEquals(c, target);
                float score = Mathf.Max(0.01f, shot.framing) * Mathf.Max(1, sp.baseValue) * Progression.SizeMultiplier(c.Tier);
                // an animal that is not in the Sealog yet wins over an already logged one in the same frame
                // (playtest: a logged turtle in the background kept the lock while the player framed a new parrotfish)
                if (!Journal.IsDiscovered(sp.id)) score *= NewSpeciesBias;
                if (!locked && score <= bestScore) continue;   // cannot win – skip the raycasts
                if (FilmShotEvaluator.IsOccluded(ctx, c, c.WorldBounds))
                {
                    if (shot.offCenter < nearMissOff) { nearMissOff = shot.offCenter; nearMiss = FilmHint.Blocked; }
                    continue;
                }
                if (locked) { lockedScore = score; lockedShot = shot; lockedSpecies = sp; }
                if (score > bestScore) { bestScore = score; best = c; bestShot = shot; bestSpecies = sp; }
            }

            // hysteresis: keep the current target unless something clearly better shows up (or it is fully filmed)
            if (target != null && lockedScore > 0f && !ReferenceEquals(best, target))
            {
                // the lock is not sticky once the locked animal is logged and either fully filmed or a new species shows up
                bool lockedMaxed = targetTrack != null && targetTrack.registered
                                   && (targetTrack.seconds >= MaxClipSeconds || (bestSpecies != null && !Journal.IsDiscovered(bestSpecies.id)));
                if (!lockedMaxed && bestScore < lockedScore * TargetStickiness)
                {
                    best = target; bestShot = lockedShot; bestSpecies = lockedSpecies;
                }
            }

            if (best == null)
            {
                ClearTarget();
                Hint = nearMiss;
                return;
            }

            target = best;
            CurrentTarget = bestSpecies;
            CurrentTargetSizeCm = best.SizeCm;
            float framing = bestShot.framing;
            bool counts = framing >= MinFraming;
            CurrentTargetCounts = counts;
            float q = Mathf.Clamp(framing * (IsAiming ? 1f : HipFactor) * cameraQuality, MinQuality, 1.6f);
            CurrentTargetQuality = q;

            bool accumulate = recording && counts;
            var track = GetTrack(best, bestSpecies, accumulate);
            if (accumulate) Accumulate(best, bestSpecies, track, q, dt, IsAiming);
            targetTrack = track;

            if (track == null) CurrentTargetProgress01 = 0f;
            else if (IsAiming) CurrentTargetProgress01 = track.registered ? 1f : Mathf.Clamp01(track.aimedSeconds / CaptureSeconds);
            else CurrentTargetProgress01 = track.clip != null ? 1f : Mathf.Clamp01(track.seconds / CaptureSeconds);

            if (track != null && track.registered) Hint = FilmHint.Logged;
            else if (!counts)
                Hint = steadiness < 0.55f ? FilmHint.Shaky : bestShot.light < 0.5f ? FilmHint.TooDark : FilmHint.Weak;
            else if (recording && !IsAiming) Hint = FilmHint.AimToLog;
            else Hint = FilmHint.Good;
        }

        FilmInstanceTrack GetTrack(ICreature c, SpeciesDef sp, bool create)
        {
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

        void Accumulate(ICreature c, SpeciesDef sp, FilmInstanceTrack t, float q, float dt, bool aimed)
        {
            if (t.seconds < MaxClipSeconds)
            {
                float add = Mathf.Min(dt, MaxClipSeconds - t.seconds);
                t.seconds += add;
                t.qualitySum += q * add;
                t.actionSum += Mathf.Clamp01(c.Activity01) * add;
            }
            if (aimed && t.aimedSeconds < MaxClipSeconds) t.aimedSeconds += dt;

            if (t.clip == null)
            {
                if (t.seconds >= CaptureSeconds) CreateClip(c, sp, t, aimed);
            }
            else UpdateClip(t);

            if (!t.registered && t.clip != null && t.aimedSeconds >= CaptureSeconds) Register(c, sp, t);
        }

        void CreateClip(ICreature c, SpeciesDef sp, FilmInstanceTrack t, bool aimed)
        {
            var gm = GameManager.I;
            if (gm == null || !gm.InGame) return;

            DiveSite site = WorldInfo.SiteAt(c.Position);
            var clip = new FootageClip
            {
                speciesId = sp.id,
                sizeCm = c.SizeCm,
                tier = (int)c.Tier,
                quality = Mathf.Clamp(t.AvgQuality, MinQuality, 1.6f),
                seconds = t.seconds,
                action = Mathf.Clamp01(t.AvgAction),
                siteId = site != null ? site.id : null,
            };
            t.clip = clip;
            clipsThisDive++;
            Footage.AddClip(clip);

            // hip-filming an animal that is not in the Sealog yet: remind the player that only aimed footage logs it
            if (!aimed && !Journal.IsDiscovered(sp.id)) HipHint();
        }

        /// Aimed footage: the animal goes into the Sealog (shared journal; co-op clients forward it to the host).
        void Register(ICreature c, SpeciesDef sp, FilmInstanceTrack t)
        {
            var clip = t.clip;
            if (clip == null) return;
            var e = Journal.Find(sp.id);
            // value including the record bonus (×1.5), so the journal's best clip value is right from the start
            clip.isRecord = e != null && e.discovered && clip.sizeCm > e.recordCm + 0.05f;
            var result = Journal.RegisterFilmed(sp, clip.sizeCm, clip.Tier, clip.Value);
            clip.isNewSpecies = result.isNew;
            clip.isRecord = result.isRecord;
            t.registered = true;

            if (result.isNew)
            {
                pendingDiscovery = sp;
                pendingDiscoverySite = clip.siteId;
                discoveryDelay = 3;
            }
        }

        /// Longer / better footage of an already captured instance improves its clip in place (no new clip).
        void UpdateClip(FilmInstanceTrack t)
        {
            var gm = GameManager.I;
            if (gm == null) return;
            var clip = t.clip;
            if (!gm.State.diveFootage.Contains(clip)) return;   // already banked or lost
            clip.seconds = Mathf.Min(MaxClipSeconds, t.seconds);
            float avgQ = Mathf.Clamp(t.AvgQuality, MinQuality, 1.6f);
            if (avgQ > clip.quality)
            {
                clip.quality = avgQ;
                clip.action = Mathf.Clamp01(t.AvgAction);
            }
            // the shared journal's "best clip" stat is only touched where the shared state lives (host / offline)
            if (t.registered && Net.HasAuthority)
            {
                var e = Journal.Find(clip.speciesId);
                float v = clip.Value;
                if (e != null && v > e.bestClipValue) e.bestClipValue = v;
            }
        }

        void HipHint()
        {
            if (hipHints >= 4 || Time.unscaledTime - lastHipHint < 40f) return;
            lastHipHint = Time.unscaledTime;
            hipHints++;
            GameEvents.RaiseToast(Loc.Tr("Fürs Sealog zielen ", "Aim to log it in the Sealog ") + Key(GameAction.Aim));
        }

        static string Key(GameAction a) => "[" + GameInput.BindingLabel(a) + "]";

        void ClearTarget()
        {
            target = null;
            targetTrack = null;
            CurrentTarget = null;
            CurrentTargetQuality = 0f;
            CurrentTargetProgress01 = 0f;
            CurrentTargetSizeCm = 0f;
            CurrentTargetCounts = false;
        }

        void ClearTracks()
        {
            foreach (var t in tracks.Values)
            {
                t.Reset();
                pool.Push(t);
            }
            tracks.Clear();
            targetTrack = null;
        }

        // ------------------------------------------------------------------ photos
        void RequestPhoto()
        {
            if (photoCooldown > 0f) return;
            photoCooldown = PhotoCooldown;
            photoRequested = true;
        }

        void TakePhoto(Camera view, IPlayer player)
        {
            SpeciesDef sp = CurrentTargetCounts ? CurrentTarget : null;
            var site = WorldInfo.SiteAt(player.Root != null ? player.Root.position : view.transform.position);
            var entry = Capture(view, sp, site != null ? site.id : null);
            if (entry == null)
            {
                GameEvents.RaiseToast(Loc.Tr("Foto fehlgeschlagen", "Photo failed"));
                return;
            }
            photo.Flash(view);                  // the shutter sound is played by the FX module (Gallery.Added)
            if (rig != null) rig.Shutter();
            string name = sp != null && Journal.IsDiscovered(sp.id) ? " · " + sp.Name : "";
            GameEvents.RaiseToast(Loc.Tr("Foto gespeichert", "Photo saved") + name);
            PhotoTaken?.Invoke(entry);
        }

        /// Automatic gallery photo of a newly discovered species (no flash, no toast – the Sealog popup covers it).
        void SaveDiscoveryPhoto(Camera view, SpeciesDef sp, string siteId)
        {
            if (sp == null) return;
            Capture(view, sp, siteId);
        }

        PhotoEntry Capture(Camera view, SpeciesDef sp, string siteId)
        {
            if (photo == null || view == null) return null;
            bool rigWasShown = rig != null && rig.root != null && rig.root.activeSelf;
            if (rigWasShown) rig.root.SetActive(false);
            Texture2D tex;
            try { tex = photo.Capture(view); }
            finally { if (rigWasShown && rig != null && rig.root != null) rig.root.SetActive(true); }
            if (tex == null) return null;
            return Gallery.Add(tex, sp != null ? sp.id : "", siteId ?? "");
        }
    }
}
