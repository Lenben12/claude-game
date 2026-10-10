using UnityEngine;

namespace Sealebrities
{
    /// Movement state of the first-person player.
    public enum PlayerMoveMode { Land, Surface, Dive }

    /// First-person diver: mouse look, walking, surface swimming, diving, boat seats, air and fainting.
    /// Created and registered as Refs.Player by PlayerFactory.
    /// Split into partial files: core/look/camera (this file), PlayerController.Movement.cs, PlayerController.Air.cs.
    [DefaultExecutionOrder(50)]
    public partial class PlayerController : MonoBehaviour, IPlayer
    {
        // ------------------------------------------------------------------ dimensions
        public const float Height = 1.75f;
        public const float Radius = 0.32f;
        public const float EyeHeight = 1.62f;
        /// Eye height above the seat transform while seated (the seat marks where the player's feet/root go).
        public const float SeatedEyeHeight = 1.3f;
        public const float DefaultFov = 72f;
        const float MenuFov = 60f;
        /// Built-in "Ignore Raycast" layer: the default raycasts of other modules never hit the player capsule.
        public const int PlayerLayer = 2;

        // ------------------------------------------------------------------ references
        CharacterController cc;
        Camera cam;
        Transform camT, head, worldRoot;
        PlayerFlashlight flashlight;
        PlayerHands hands;
        readonly PlayerMenuCamera menuCam = new PlayerMenuCamera();
        bool initialized, subscribed;

        // ------------------------------------------------------------------ high level state
        bool menuMode;
        int gameStartedFrame = -1;
        int lookSkipFrames;
        PlayerMoveMode mode = PlayerMoveMode.Land;

        // look
        float yaw, pitch, seatYaw;
        Vector2 lookDelta;
        float lookAngularSpeed;
        Quaternion lastCamRot = Quaternion.identity;
        bool camRotValid;

        // measured world velocity
        Vector3 measuredVel, lastPos;
        bool lastPosValid;

        // camera effects
        float eyeHeight = EyeHeight, landDip, trauma, faintAnim, bobPhase, bobAmp;

        // ------------------------------------------------------------------ IPlayer
        public Transform Root => transform;
        public Camera View => cam;
        public Vector3 Velocity => measuredVel;
        public bool IsUnderwater => isUnderwater;
        public bool IsInWater => isInWater;
        public float Depth => depth;
        public float Air01 => airCapacity > 0f ? Mathf.Clamp01(airLeft / airCapacity) : 0f;
        public float AirSecondsLeft => airLeft;
        public float MaxSafeDepth => gear.maxDepth;
        public bool FlashlightOn => flashlight != null && flashlight.On;
        public bool IsSeated => seated;
        public bool IsFainting { get; private set; }
        public float LookAngularSpeed => lookAngularSpeed;

        // ------------------------------------------------------------------ module-internal state (used by PlayerHands/Flashlight/Interactor/Narcosis)
        /// True while the player is controlled in first person (not the main-menu orbit).
        public bool InGameplay => initialized && !menuMode;
        /// Climbing out of the water, being rescued or fainting: no interaction possible.
        public bool IsBusy => vaulting || rescueActive || IsFainting;
        public PlayerMoveMode Mode => mode;
        public bool Grounded => grounded;
        /// Own movement speed (relative to the platform/water), m/s.
        public float MoveSpeed => mode == PlayerMoveMode.Land ? new Vector2(vel.x, vel.z).magnitude : vel.magnitude;
        public float BobPhase => bobPhase;
        public float BobAmount => bobAmp;
        public Vector2 LookDelta => lookDelta;
        public bool CanVaultHint => canVaultHint;
        public GearStats Gear => gear;
        public DifficultySettings Diff => diff;
        /// 0..1 fainting animation weight.
        public float FaintAmount => faintAnim;

        /// Unity-aware null check for interface references that may point at destroyed objects.
        public static bool Alive(object o) => o != null && !(o is Object uo && uo == null);

        // ------------------------------------------------------------------ setup
        public void Init(Camera camera, Transform headPivot, Transform root, PlayerFlashlight light, PlayerHands viewHands)
        {
            cc = GetComponent<CharacterController>();
            cam = camera;
            camT = camera.transform;
            head = headPivot;
            worldRoot = root;
            flashlight = light;
            hands = viewHands;
            yaw = transform.eulerAngles.y;
            pitch = 0f;
            airCapacity = airLeft = Mathf.Max(1f, gear.airSeconds);
            RefreshStats();
            RefillAir();
            Subscribe();
            initialized = true;
            menuMode = false;
            if (WantMenu()) EnterMenuMode();
        }

        void Subscribe()
        {
            if (subscribed) return;
            subscribed = true;
            GameEvents.GameStarted += OnGameStarted;
            GameEvents.ReturnedToMenu += OnReturnedToMenu;
            GameEvents.DayStarted += OnDayStarted;
            GameEvents.GearChanged += OnGearChanged;
            GameEvents.PlayerBumped += OnBumped;
        }

        void OnDestroy()
        {
            if (subscribed)
            {
                GameEvents.GameStarted -= OnGameStarted;
                GameEvents.ReturnedToMenu -= OnReturnedToMenu;
                GameEvents.DayStarted -= OnDayStarted;
                GameEvents.GearChanged -= OnGearChanged;
                GameEvents.PlayerBumped -= OnBumped;
                subscribed = false;
            }
            if (ReferenceEquals(Refs.Player, this)) Refs.Player = null;
        }

        /// Spawn point next to the bed in the hut (also used as the wake-up point after sleeping).
        public static Vector3 ResolveSpawn(out float yawDeg)
        {
            Vector3 p = WorldInfo.PlayerSpawn;
            yawDeg = WorldInfo.PlayerSpawnYaw;
            if (p == Vector3.zero)
            {
                p = WorldLayout.PlayerSpawn;
                p.y = Mathf.Max(WorldInfo.GroundHeight(p), Waves.SeaLevel + 0.5f);
                yawDeg = WorldLayout.PlayerSpawnYaw;
            }
            // snap onto the floor directly below (hut floor / terrain)
            if (Physics.Raycast(p + Vector3.up * 1.2f, Vector3.down, out RaycastHit hit, 4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                p = hit.point;
            return p + Vector3.up * 0.05f;
        }

        // ------------------------------------------------------------------ events
        void OnGameStarted()
        {
            if (!initialized) return;
            gameStartedFrame = Time.frameCount;
            RefreshStats();
            ResetTransientState();
            pitch = 0f;
            Vector3 p = ResolveSpawn(out float y);
            TeleportTo(p, y);
            RefillAir();
            if (flashlight != null) flashlight.ForceOff();
            // restore the first-person view right away if the UI already closed the menu
            // (otherwise Update switches as soon as UIState.MainMenuOpen goes false)
            if (menuMode && !WantMenu()) ExitMenuMode();
        }

        void OnReturnedToMenu()
        {
            if (!initialized) return;
            EnterMenuMode();
        }

        void OnDayStarted(int day)
        {
            if (!initialized || menuMode) return;
            if (Time.frameCount == gameStartedFrame) return;    // NewGame/LoadGame raise DayStarted right after GameStarted
            var gm = GameManager.I;
            if (gm == null || !gm.InGame) return;
            // A sleep always resets the clock to MorningTime; the midnight rollover does not.
            if (Mathf.Abs(gm.State.timeOfDay - GameManager.MorningTime) > 0.01f) return;
            ResetTransientState();
            pitch = 0f;
            Vector3 p = ResolveSpawn(out float y);
            TeleportTo(p, y);
            RefillAir();
        }

        void OnGearChanged()
        {
            if (!initialized) return;
            float ratio = Air01;
            RefreshStats();
            airLeft = Mathf.Clamp(ratio * airCapacity, 0f, airCapacity);
        }

        void RefreshStats()
        {
            var gm = GameManager.I;
            if (gm != null)
            {
                gear = gm.Gear;
                diff = gm.Difficulty;
            }
            airCapacity = Mathf.Max(1f, gear.airSeconds);
            if (airLeft > airCapacity) airLeft = airCapacity;
            if (flashlight != null) flashlight.Refresh(gear);
        }

        void ResetTransientState()
        {
            if (seated) { seated = false; seat = null; seatYaw = 0f; }
            if (vaulting) { vaulting = false; vaultRef = null; }
            IsFainting = false;
            rescueActive = false;
            pendingFade = null;
            trauma = 0f;
            faintAnim = 0f;
            landDip = 0f;
            tooDeepWarned = false;
            lowAirWarned = false;
            sprintingUnderwater = false;
            vel = Vector3.zero;
            platform = null;
            platformRb = null;
            canVaultHint = false;
        }

        // ------------------------------------------------------------------ menu mode
        static bool WantMenu()
        {
            var gm = GameManager.I;
            return UIState.MainMenuOpen || gm == null || !gm.InGame;
        }

        void EnterMenuMode()
        {
            menuMode = true;
            ResetTransientState();
            if (cc != null) cc.enabled = false;
            if (flashlight != null) flashlight.ForceOff();
            if (hands != null) hands.SetVisible(false);
            if (isUnderwater)
            {
                isUnderwater = false;
                GameEvents.RaiseUnderwaterChanged(false);
            }
            isInWater = false;
            depth = 0f;
            mode = PlayerMoveMode.Land;
            measuredVel = Vector3.zero;
            lookAngularSpeed = 0f;
            camRotValid = false;
            if (camT != null)
            {
                camT.SetParent(worldRoot, true);
                cam.fieldOfView = MenuFov;
                menuCam.Begin(camT);
            }
        }

        void ExitMenuMode()
        {
            menuMode = false;
            if (camT != null)
            {
                camT.SetParent(head, false);
                camT.localPosition = Vector3.zero;
                camT.localRotation = Quaternion.identity;
                cam.fieldOfView = DefaultFov;
            }
            if (cc != null && !seated) cc.enabled = true;
            if (hands != null) hands.SetVisible(true);
            lastPosValid = false;
            camRotValid = false;
            lookSkipFrames = 2;     // ignore the mouse-delta spike when the cursor gets locked
            RecheckWaterAfterTeleport();
        }

        // ------------------------------------------------------------------ frame loop
        void Update()
        {
            if (!initialized) return;
            CheckPendingFade();

            bool wantMenu = WantMenu();
            if (wantMenu != menuMode)
            {
                if (wantMenu) EnterMenuMode();
                else ExitMenuMode();
            }
            if (menuMode) return;

            float dt = Time.deltaTime;
            if (dt <= 0f || UIState.PauseOpen || UIState.LoadingOpen)
            {
                lookDelta = Vector2.zero;
                return;
            }
            dt = Mathf.Min(dt, 0.1f);

            bool input = !UIState.BlocksGameplay && !IsFainting && !rescueActive && !vaulting;
            UpdateLook(input);

            if (seated) FollowSeat();
            else if (vaulting) UpdateVault(dt);
            else UpdateMovement(input, dt);

            UpdateWaterState(dt);
            UpdateVaultHint(dt);
            UpdateAir(dt);
            MeasureVelocity(dt);
        }

        void LateUpdate()
        {
            if (!initialized) return;
            if (menuMode)
            {
                menuCam.Tick(Time.unscaledDeltaTime);
                return;
            }
            float dt = Time.deltaTime;
            if (seated) FollowSeat();
            if (dt <= 0f) return;
            UpdateCameraPose(Mathf.Min(dt, 0.1f));
            MeasureLook(dt);
        }

        // ------------------------------------------------------------------ look
        void UpdateLook(bool input)
        {
            lookDelta = Vector2.zero;
            if (input)
            {
                Vector2 d = GameInput.Look;
                if (lookSkipFrames > 0) { lookSkipFrames--; d = Vector2.zero; }
                // slower look when the camera is zoomed in (the filming module may narrow the FOV)
                float fovScale = cam != null ? Mathf.Clamp(cam.fieldOfView / DefaultFov, 0.15f, 1.25f) : 1f;
                d = Vector2.ClampMagnitude(d * fovScale, 60f);
                lookDelta = d;
                pitch = Mathf.Clamp(pitch - d.y, -85f, 85f);
                if (seated) seatYaw = Mathf.Clamp(seatYaw + d.x, -110f, 110f);
                else yaw = Mathf.Repeat(yaw + d.x, 360f);
            }
            head.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            if (!seated) transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        void MeasureLook(float dt)
        {
            Quaternion r = camT.rotation;
            if (camRotValid)
            {
                float speed = Quaternion.Angle(lastCamRot, r) / dt;
                lookAngularSpeed = Mathf.Lerp(lookAngularSpeed, speed, 1f - Mathf.Exp(-10f * dt));
            }
            else lookAngularSpeed = Mathf.Lerp(lookAngularSpeed, 0f, 0.5f);
            lastCamRot = r;
            camRotValid = true;
        }

        void MeasureVelocity(float dt)
        {
            Vector3 p = transform.position;
            if (lastPosValid)
            {
                Vector3 v = (p - lastPos) / dt;
                measuredVel = Vector3.Lerp(measuredVel, v, 1f - Mathf.Exp(-20f * dt));
            }
            else measuredVel = Vector3.zero;
            lastPos = p;
            lastPosValid = true;
        }

        // ------------------------------------------------------------------ camera pose (bob, sway, shake, faint)
        void UpdateCameraPose(float dt)
        {
            eyeHeight = Mathf.Lerp(eyeHeight, seated ? SeatedEyeHeight : EyeHeight, 1f - Mathf.Exp(-6f * dt));
            landDip = Mathf.Lerp(landDip, 0f, 1f - Mathf.Exp(-7f * dt));
            head.localPosition = new Vector3(0f, eyeHeight - landDip, 0f);
            head.localRotation = Quaternion.Euler(pitch, 0f, 0f);

            // walking head bob: one vertical dip per step, one lateral sway per two steps
            float vb = (1f - Mathf.Cos(2f * bobPhase)) * 0.5f;
            Vector3 offset = new Vector3(Mathf.Sin(bobPhase) * 0.016f, -vb * 0.032f, 0f) * bobAmp;

            float t = Time.time;
            float roll = 0f, extraPitch = 0f, extraYaw = 0f;
            if (isUnderwater && !seated)
            {
                roll += Mathf.Sin(t * 0.55f) * 0.9f;
                extraPitch += Mathf.Sin(t * 0.43f + 1.3f) * 0.5f;
                roll -= Mathf.Clamp(Vector3.Dot(vel, transform.right), -3f, 3f) * 1.4f;   // bank into lateral motion
                offset.y += Mathf.Sin(t * 0.8f) * 0.012f;
            }
            else if (mode == PlayerMoveMode.Surface && isInWater)
            {
                Vector3 p = transform.position;
                Vector3 n = Waves.NormalAt(p.x, p.z);
                roll -= Vector3.Dot(n, transform.right) * Mathf.Rad2Deg * 0.6f;
                extraPitch += Vector3.Dot(n, transform.forward) * Mathf.Rad2Deg * 0.4f;
            }

            // camera shake (bumps)
            trauma = Mathf.Max(0f, trauma - dt * 1.1f);
            float s = trauma * trauma;
            if (s > 0.0001f)
            {
                float tt = t * 17f;
                offset += new Vector3(Mathf.PerlinNoise(tt, 0.3f) - 0.5f, Mathf.PerlinNoise(0.7f, tt) - 0.5f, 0f) * (0.09f * s);
                extraPitch += (Mathf.PerlinNoise(tt, 5.1f) - 0.5f) * 7f * s;
                roll += (Mathf.PerlinNoise(9.2f, tt) - 0.5f) * 9f * s;
                extraYaw += (Mathf.PerlinNoise(tt, 13.7f) - 0.5f) * 7f * s;
            }

            // fainting: slow tilt and sink
            faintAnim = Mathf.MoveTowards(faintAnim, IsFainting ? 1f : 0f, dt * (IsFainting ? 0.55f : 2f));
            float fa = faintAnim * faintAnim * (3f - 2f * faintAnim);
            roll += fa * 30f;
            extraPitch += fa * 18f;
            offset.y -= fa * 0.22f;

            camT.localPosition = offset;
            camT.localRotation = Quaternion.Euler(extraPitch, extraYaw, roll);
        }
    }
}
