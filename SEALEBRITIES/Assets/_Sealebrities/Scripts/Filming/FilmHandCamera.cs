using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// The diver's handheld camera: procedural model in the right hand, aiming/zoom, recording with limited storage
    /// per dive, shot evaluation (framing x light x steadiness x camera) and clip/journal registration.
    /// The viewfinder overlay is drawn by the UI module from the IHandCamera state.
    [DefaultExecutionOrder(150)]
    public class FilmHandCamera : MonoBehaviour, IHandCamera
    {
        public const float CaptureSeconds = 1.5f;     // seconds of good footage until an instance becomes a clip
        public const float MaxClipSeconds = 8f;       // an instance's clip keeps improving up to this length
        public const float MinQuality = 0.25f;        // footage below this quality does not count
        public const float EvalInterval = 0.12f;
        public const float BaseFov = 72f;
        public const float BankDelay = 2f;            // seconds out of the water until the dive counts as finished
        const float TargetStickiness = 1.25f;         // a new target must score 25 % better to steal the lock
        const float MaxEvalStep = 0.25f;              // clamp accumulated time after frame hitches

        // ------------------------------------------------------------------ IHandCamera
        public bool IsAiming { get; private set; }
        public bool IsRecording => recording;
        public float TapeSecondsLeft => tapeLeft;
        public float TapeSecondsTotal => tapeTotal;
        public float Zoom => zoom;
        public SpeciesDef CurrentTarget { get; private set; }
        public float CurrentTargetProgress01 { get; private set; }
        public float CurrentTargetQuality { get; private set; }

        /// Clips captured since the last time the player left the water (not part of IHandCamera).
        public int ClipsThisDive => clipsThisDive;
        /// Size of the current target (cm), 0 when none (not part of IHandCamera).
        public float CurrentTargetSizeCm { get; private set; }

        // ------------------------------------------------------------------ state
        bool recording;
        float tapeLeft = 90f, tapeTotal = 90f;
        float zoom = 1f, zoomSetting = 1f;
        float cameraQuality = 0.6f, maxZoom = 1.5f, lampRange;
        int cameraTier = -1;
        float evalTimer, outOfWaterTimer;
        int clipsThisDive;
        bool visibleNow;

        bool fovDirty;
        Camera fovCamera;

        ICreature target;
        FilmInstanceTrack targetTrack;
        readonly Dictionary<ICreature, FilmInstanceTrack> tracks = new Dictionary<ICreature, FilmInstanceTrack>(FilmRefComparer.Instance);
        readonly Stack<FilmInstanceTrack> pool = new Stack<FilmInstanceTrack>();

        FilmCameraRig rig;
        Camera rigView;

        static string StorageFullText => Loc.Tr("Speicher voll – an Land oder auf dem Boot sichern.", "Storage full – save it on land or on the boat.");

        // ------------------------------------------------------------------ lifecycle
        void Awake()
        {
            Refs.HandCamera = this;
        }

        void OnEnable()
        {
            GameEvents.GameStarted += OnGameStarted;
            GameEvents.GearChanged += OnGearChanged;
            GameEvents.ReturnedToMenu += OnReturnedToMenu;
            GameEvents.PlayerFainted += OnPlayerFainted;
        }

        void OnDisable()
        {
            GameEvents.GameStarted -= OnGameStarted;
            GameEvents.GearChanged -= OnGearChanged;
            GameEvents.ReturnedToMenu -= OnReturnedToMenu;
            GameEvents.PlayerFainted -= OnPlayerFainted;
        }

        void OnDestroy()
        {
            if (ReferenceEquals(Refs.HandCamera, this)) Refs.HandCamera = null;
            if (fovDirty && fovCamera != null) fovCamera.fieldOfView = BaseFov;
            if (rig != null) { rig.Destroy(); rig = null; }
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
            RefreshGear(true);
        }

        void OnGearChanged() => RefreshGear(false);

        void OnReturnedToMenu()
        {
            if (recording) SetRecording(false);
            IsAiming = false;
            ClearTracks();
            ClearTarget();
        }

        void OnPlayerFainted()
        {
            // the dive's footage is lost (Footage.LoseDive is called by GameManager.HandleFaint)
            if (recording) SetRecording(false);
            IsAiming = false;
            ClearTracks();
            ClearTarget();
            clipsThisDive = 0;
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
            rig = FilmRigBuilder.Build(Mathf.Max(0, cameraTier));
            rig.root.transform.SetParent(view.transform, false);
            rig.ResetPose(false);       // slides up into view
            rig.root.SetActive(false);
            rigView = view;
            // the model sits 0.3–0.65 m in front of the eye; make sure it is not cut by an unusually far near plane
            if (view.nearClipPlane > 0.3f) view.nearClipPlane = 0.1f;
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

        void Update()
        {
            float dt = Time.deltaTime;
            var gm = GameManager.I;
            var player = Refs.Player;
            bool inGame = gm != null && gm.InGame;
            Camera view = player != null ? player.View : null;

            if (!inGame || player == null || view == null)
            {
                if (recording) SetRecording(false);
                IsAiming = false;
                visibleNow = false;
                zoom = 1f;
                ClearTarget();
                return;
            }

            EnsureRig(view);

            bool blocked = UIState.BlocksGameplay;
            bool incapacitated = player.IsSeated || player.IsFainting;
            if (incapacitated && recording) SetRecording(false);
            visibleNow = !blocked && !incapacitated;

            UpdateDive(player, dt);

            // input (ignored while a UI blocks gameplay)
            if (visibleNow)
            {
                IsAiming = GameInput.AimHeld;
                if (IsAiming)
                {
                    float step = GameInput.ZoomStep;
                    if (step != 0f) zoomSetting = Mathf.Clamp(zoomSetting + step * ZoomStepSize, 1f, maxZoom);
                }
                if (GameInput.RecordPressed) ToggleRecording();
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
            if (visibleNow && (recording || IsAiming))
            {
                evalTimer += dt;
                if (evalTimer >= EvalInterval)
                {
                    float step = Mathf.Min(evalTimer, MaxEvalStep);
                    evalTimer = 0f;
                    Evaluate(player, view, step);
                }
            }
            else if (!recording && !IsAiming) ClearTarget();
        }

        void LateUpdate()
        {
            var player = Refs.Player;
            Camera view = player != null ? player.View : null;

            // FOV = 72 / zoom; only touch the camera while zoomed (and once to restore it)
            if (view != null)
            {
                if (Mathf.Abs(zoom - 1f) > 0.0005f)
                {
                    view.fieldOfView = BaseFov / zoom;
                    fovDirty = true;
                    fovCamera = view;
                }
                else if (fovDirty)
                {
                    // restore the gameplay FOV; in menu mode the player module owns the FOV (menu camera)
                    var gm = GameManager.I;
                    bool gameplay = gm != null && gm.InGame && !UIState.MainMenuOpen;
                    if (gameplay && fovCamera != null) fovCamera.fieldOfView = BaseFov;
                    fovDirty = false;
                }
            }

            if (rig == null || rig.root == null) return;
            bool show = visibleNow && player != null && view != null;
            var input = new FilmRigInput
            {
                visible = show,
                aiming = IsAiming,
                recording = recording,
                lampOn = show && player.FlashlightOn,
                underwater = show && player.IsUnderwater,
                hasTarget = CurrentTarget != null,
                zoom = zoom,
                zoom01 = maxZoom > 1.001f ? (zoom - 1f) / (maxZoom - 1f) : 0f,
                fieldOfView = view != null ? view.fieldOfView : BaseFov,
                tape01 = tapeTotal > 0f ? tapeLeft / tapeTotal : 0f,
                quality = CurrentTargetQuality,
                progress01 = CurrentTargetProgress01,
                lookDelta = show ? GameInput.Look : Vector2.zero,
                moveSpeed = show ? player.Velocity.magnitude : 0f,
            };
            rig.Animate(input, Time.deltaTime);
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
            var ctx = new FilmShotContext
            {
                cam = view,
                camPos = camT.position,
                camFwd = camT.forward,
                aspect = view.aspect,
                ignoreRoot = player.Root,
                globalFactor = steadiness * (IsAiming ? 1f : 0.55f) * cameraQuality,
                lampOn = player.FlashlightOn,
                lampRange = lampRange > 0f ? lampRange : 10f,
                night01 = EnvState.Night01,
            };

            ICreature best = null;
            SpeciesDef bestSpecies = null;
            float bestScore = 0f, bestQ = 0f;
            float lockedScore = -1f, lockedQ = 0f;
            SpeciesDef lockedSpecies = null;

            const float coarseRange = FilmShotEvaluator.MaxRange + 12f;   // margin for very long animals
            Vector3 camPos = ctx.camPos;
            var list = CreatureRegistry.Active;
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                if (c == null) continue;
                if (c is Object uo && uo == null) continue;   // destroyed but not yet unregistered (Unity fake-null)
                if (c.IsHallucination || !c.IsAlive) continue;
                if (c.Transform == null) continue;
                if ((c.Position - camPos).sqrMagnitude > coarseRange * coarseRange) continue;
                var sp = c.Species;
                if (sp == null) continue;

                float q = FilmShotEvaluator.Quality(c, sp, ctx);
                if (q <= 0f) continue;
                float score = q * Mathf.Max(1, sp.baseValue) * Progression.SizeMultiplier(c.Tier);
                if (ReferenceEquals(c, target)) { lockedScore = score; lockedQ = q; lockedSpecies = sp; }
                if (score > bestScore) { bestScore = score; best = c; bestQ = q; bestSpecies = sp; }
            }

            // hysteresis: keep the current target unless something clearly better shows up (or it is fully filmed)
            if (target != null && lockedScore > 0f && !ReferenceEquals(best, target))
            {
                bool lockedMaxed = targetTrack != null && targetTrack.seconds >= MaxClipSeconds;
                if (!lockedMaxed && bestScore < lockedScore * TargetStickiness)
                {
                    best = target; bestQ = lockedQ; bestSpecies = lockedSpecies;
                }
            }

            if (best == null) { ClearTarget(); return; }

            target = best;
            CurrentTarget = bestSpecies;
            CurrentTargetQuality = bestQ;
            CurrentTargetSizeCm = best.SizeCm;

            bool counts = recording && bestQ >= MinQuality;
            var track = GetTrack(best, bestSpecies, counts);
            if (counts) Accumulate(best, bestSpecies, track, bestQ, dt);
            targetTrack = track;
            CurrentTargetProgress01 = track == null ? 0f : track.clip != null ? 1f : Mathf.Clamp01(track.seconds / CaptureSeconds);
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

        void Accumulate(ICreature c, SpeciesDef sp, FilmInstanceTrack t, float q, float dt)
        {
            if (t.seconds >= MaxClipSeconds) return;
            float add = Mathf.Min(dt, MaxClipSeconds - t.seconds);
            t.seconds += add;
            t.qualitySum += q * add;
            t.actionSum += Mathf.Clamp01(c.Activity01) * add;

            if (t.clip == null)
            {
                if (t.seconds >= CaptureSeconds) CreateClip(c, sp, t);
            }
            else UpdateClip(t);
        }

        void CreateClip(ICreature c, SpeciesDef sp, FilmInstanceTrack t)
        {
            var gm = GameManager.I;
            if (gm == null || !gm.InGame) return;

            DiveSite site = WorldInfo.SiteAt(c.Position);
            SizeTier tier = c.Tier;
            var clip = new FootageClip
            {
                speciesId = sp.id,
                sizeCm = c.SizeCm,
                tier = (int)tier,
                quality = Mathf.Clamp(t.AvgQuality, 0.1f, 1.6f),
                seconds = t.seconds,
                action = Mathf.Clamp01(t.AvgAction),
                siteId = site != null ? site.id : null,
            };

            var result = Journal.RegisterFilmed(sp, c.SizeCm, tier, clip.Value);
            clip.isNewSpecies = result.isNew;
            clip.isRecord = result.isRecord;

            // the journal stored the value before the record bonus was known
            t.entry = Journal.Get(sp.id);
            float v = clip.Value;
            if (t.entry != null && v > t.entry.bestClipValue) t.entry.bestClipValue = v;

            t.clip = clip;
            clipsThisDive++;
            Footage.AddClip(clip);
        }

        /// Longer / better footage of an already captured instance improves its clip in place (no new clip).
        void UpdateClip(FilmInstanceTrack t)
        {
            var gm = GameManager.I;
            if (gm == null) return;
            var clip = t.clip;
            if (!gm.State.diveFootage.Contains(clip)) return;   // already banked or lost
            clip.seconds = Mathf.Min(MaxClipSeconds, t.seconds);
            float avgQ = Mathf.Clamp(t.AvgQuality, 0.1f, 1.6f);
            if (avgQ > clip.quality)
            {
                clip.quality = avgQ;
                clip.action = Mathf.Clamp01(t.AvgAction);
            }
            if (t.entry != null)
            {
                float v = clip.Value;
                if (v > t.entry.bestClipValue) t.entry.bestClipValue = v;
            }
        }

        void ClearTarget()
        {
            target = null;
            targetTrack = null;
            CurrentTarget = null;
            CurrentTargetQuality = 0f;
            CurrentTargetProgress01 = 0f;
            CurrentTargetSizeCm = 0f;
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
    }
}
