using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// The player's boat "Rostlaube": buoyancy on Waves, driving from the helm, anchor, effects and engine audio.
    /// Implements IBoat (Refs.Boat). Local space: +Z bow, y = 0 design waterline, deck at BoatHull.DeckY.
    public class BoatController : MonoBehaviour, IBoat
    {
        // ------------------------------------------------------------------ tuning
        const float Mass = 1500f;
        const float SpringK = 6150f;     // N per meter of submersion per buoyancy point (8 points carry 1500 kg at 0.3 m)
        const float DampC = 1100f;       // N per m/s per point
        const float MaxSpeed = 11f;      // m/s at boatSpeed 1
        const float ExitMaxSpeed = 3f;   // the driver can only get up below this speed

        static readonly Vector3[] BuoyPts =
        {
            new Vector3(-0.85f, -0.3f, -3.1f), new Vector3(0.85f, -0.3f, -3.1f),
            new Vector3(-0.95f, -0.3f, -1.1f), new Vector3(0.95f, -0.3f, -1.1f),
            new Vector3(-0.85f, -0.3f, 0.9f), new Vector3(0.85f, -0.3f, 0.9f),
            new Vector3(-0.45f, -0.3f, 2.6f), new Vector3(0.45f, -0.3f, 2.6f),
        };

        static bool helpShown;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { helpShown = false; }

        // ------------------------------------------------------------------ state
        Rigidbody rb;
        BoatParts parts;
        Vector3 spawnPos;
        float spawnYaw;

        bool driving;
        int seatedFrame = -1, exitFrame = -1;
        float seatedTime = -10f, exitTime = -10f;
        float throttle, steer, wheelAngle, propAngle, radarAngle;
        float forwardSpeed, submerged01;
        float boatSpeedMult = 1f;
        float beachedTimer, flippedTimer;
        bool towing;
        bool anchorHintShown;
        float lowThrottleTime, lastSpeedHint = -10f;
        bool throttleWasLow = true;

        enum AnchorState { Stowed, Lowering, Down, Raising }
        bool anchored;
        AnchorState anchorState = AnchorState.Stowed;
        Vector3 anchorPoint, anchorFloor;
        LineRenderer rope;
        readonly Vector3[] ropePts = new Vector3[12];

        ParticleSystem smoke, sprayL, sprayR, wake;
        Transform wakeEmitter;
        AudioSource engineSrc;

        // controls hint shown as the interaction prompt while driving (PlayerInteractor is inactive while seated)
        string drivePrompt;
        Language drivePromptLang;

        // ------------------------------------------------------------------ IBoat
        public Transform Root => transform;
        public bool PlayerDriving => driving;
        public bool Anchored => anchored;
        public Vector3 RescuePoint => transform.TransformPoint(parts != null ? parts.rescueLocal : new Vector3(0f, 1.25f, -1.9f));

        public bool PlayerAboard
        {
            get
            {
                if (driving) return true;
                var p = Refs.Player;
                if (p == null || p.IsInWater) return false;
                Transform r = p.Root;
                if (r == null) return false;
                Vector3 wp = r.position;
                Vector3 lp = transform.InverseTransformPoint(wp);
                if (lp.y < BoatHull.DeckY - 0.6f || lp.y > BoatHull.DeckY + 3.2f) return false;
                if (lp.z < -4.3f || lp.z > 3.75f) return false;
                float hw = lp.z < -3.74f ? 1.15f : BoatHull.DeckHalfAtZ(lp.z) + 0.15f;
                if (Mathf.Abs(lp.x) > hw) return false;
                return wp.y > Waves.HeightAt(wp.x, wp.z) - 0.2f;
            }
        }

        public void PlaceAt(Vector3 position, float yawDegrees)
        {
            position.y = Waves.HeightAt(position.x, position.z);
            Quaternion rot = Quaternion.Euler(0f, yawDegrees, 0f);
            if (rb != null)
            {
                rb.position = position;
                rb.rotation = rot;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            transform.SetPositionAndRotation(position, rot);
            throttle = 0f;
            steer = 0f;
            beachedTimer = 0f;
            flippedTimer = 0f;
            Physics.SyncTransforms();
            if (anchored) DropAnchor(true);
        }

        // ------------------------------------------------------------------ creation
        public static BoatController Create(Transform parent, Vector3 pos, float yaw)
        {
            var go = new GameObject("Boat_Rostlaube");
            go.transform.SetParent(parent, false);
            pos.y = Waves.SeaLevel;
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            var boat = go.AddComponent<BoatController>();
            boat.Init(pos, yaw);
            return boat;
        }

        void Init(Vector3 pos, float yaw)
        {
            spawnPos = pos;
            spawnYaw = yaw;
            parts = BoatModelBuilder.Build(transform, this);

            rb = gameObject.AddComponent<Rigidbody>();
            rb.mass = Mass;
            rb.useGravity = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            rb.linearDamping = 0.05f;
            rb.angularDamping = 0.3f;
            rb.maxAngularVelocity = 3f;
            rb.automaticCenterOfMass = false;
            rb.centerOfMass = new Vector3(0f, -0.1f, -0.2f);
            rb.automaticInertiaTensor = false;
            rb.inertiaTensor = new Vector3(7300f, 8000f, 2600f);
            rb.inertiaTensorRotation = Quaternion.identity;

            // effects
            smoke = BoatParticles.EngineSmoke(parts.exhaust, Vector3.zero);
            sprayL = BoatParticles.BowSpray(transform, parts.bowSprayLeftLocal, -1f);
            sprayR = BoatParticles.BowSpray(transform, parts.bowSprayRightLocal, 1f);
            wake = BoatParticles.Wake(transform);
            wakeEmitter = wake.transform;
            engineSrc = BoatSynth.LoopSource(parts.outboardPivot.gameObject, BoatSynth.Engine, 3f, 70f, AudioRolloffMode.Linear);

            // anchor rope
            var ropeGo = new GameObject("AnchorRope");
            ropeGo.transform.SetParent(transform.parent, false);
            rope = ropeGo.AddComponent<LineRenderer>();
            rope.useWorldSpace = true;
            rope.positionCount = ropePts.Length;
            rope.widthMultiplier = 0.025f;
            rope.numCapVertices = 2;
            rope.sharedMaterial = MaterialLibrary.Unlit(new Color(0.8f, 0.7f, 0.48f));   // flat rope color (low-poly look)
            rope.shadowCastingMode = ShadowCastingMode.Off;
            rope.receiveShadows = false;
            rope.enabled = false;

            GameEvents.GameStarted += OnGameStarted;
            GameEvents.ReturnedToMenu += OnReturnedToMenu;
            GameEvents.GearChanged += OnGearChanged;
            GameEvents.DayStarted += OnDayStarted;

            PlaceAt(spawnPos, spawnYaw);
            DropAnchor(true);
        }

        void OnDestroy()
        {
            GameEvents.GameStarted -= OnGameStarted;
            GameEvents.ReturnedToMenu -= OnReturnedToMenu;
            GameEvents.GearChanged -= OnGearChanged;
            GameEvents.DayStarted -= OnDayStarted;
            ClearDrivePrompt();
            if (rope != null) Destroy(rope.gameObject);
            if (parts != null && parts.anchor != null && parts.anchor.parent != transform) Destroy(parts.anchor.gameObject);
            if (ReferenceEquals(Refs.Boat, this)) Refs.Boat = null;
        }

        void OnGameStarted()
        {
            driving = false;
            ClearDrivePrompt();
            towing = false;
            anchorHintShown = false;
            OnGearChanged();
            anchored = true;
            PlaceAt(spawnPos, spawnYaw);
        }

        /// After a night's sleep the boat is back at the dock, like the player is back in the hut.
        /// Same "slept" check as the player module: the midnight rollover (clock not at morning) does not move it.
        void OnDayStarted(int day)
        {
            var gm = GameManager.I;
            if (driving || PlayerAboard || gm == null || !gm.InGame) return;
            if (Mathf.Abs(gm.State.timeOfDay - GameManager.MorningTime) > 0.01f) return;
            towing = false;
            anchored = true;
            PlaceAt(spawnPos, spawnYaw);   // re-drops the anchor at the dock
        }

        void OnReturnedToMenu()
        {
            ClearDrivePrompt();
            if (!driving) return;
            driving = false;
            var p = Refs.Player;
            if (p != null && p.IsSeated) p.ClearSeated(transform.TransformPoint(parts.exitLocal));
        }

        void OnGearChanged()
        {
            boatSpeedMult = GameManager.I != null ? Mathf.Max(0.5f, GameManager.I.Gear.boatSpeed) : 1f;
        }

        // ------------------------------------------------------------------ helm / ladder / anchor (called by interactables)
        public bool CanEnterHelm()
        {
            var p = Refs.Player;
            // cooldown: the same E press that gets the driver up must not re-seat them through the interactor
            if (Time.frameCount == exitFrame || Time.time - exitTime < 0.4f) return false;
            return !driving && p != null && GameManager.I != null && GameManager.I.InGame && !p.IsInWater && !p.IsSeated && !p.IsFainting;
        }

        public void EnterHelm()
        {
            if (!CanEnterHelm()) return;
            Refs.Player.SetSeated(parts.seat);
            driving = true;
            seatedFrame = Time.frameCount;
            seatedTime = Time.time;
            throttle = 0f;
            if (!helpShown)
            {
                helpShown = true;
                GameEvents.RaiseToast(Loc.Tr("W/S Gas, A/D lenken, Q Anker, E aussteigen", "W/S throttle, A/D steer, Q anchor, E get up"));
            }
            else if (anchored)
            {
                GameEvents.RaiseToast(Loc.Tr("Der Anker ist unten – Q zum Lichten", "The anchor is down – Q to raise it"));
                anchorHintShown = true;
            }
        }

        public void ExitHelm()
        {
            if (!driving) return;
            driving = false;
            ClearDrivePrompt();
            exitFrame = Time.frameCount;
            exitTime = Time.time;
            var p = Refs.Player;
            if (p != null) p.ClearSeated(transform.TransformPoint(parts.exitLocal));
        }

        public bool CanClimbAboard()
        {
            var p = Refs.Player;
            if (p == null || GameManager.I == null || !GameManager.I.InGame || p.IsSeated || p.IsFainting) return false;
            if (p.IsInWater) return true;
            Transform r = p.Root;
            return r != null && transform.InverseTransformPoint(r.position).y < BoatHull.DeckY - 0.3f;
        }

        public void ClimbAboard()
        {
            if (!CanClimbAboard()) return;
            var p = Refs.Player;
            Vector3 pos = transform.TransformPoint(parts.ladderDeckLocal);
            p.TeleportTo(pos, transform.eulerAngles.y);
            p.RefillAir();
            if (GameManager.I != null && GameManager.I.InGame) Footage.BankDive();
            BoatSynth.PlayAt(BoatSynth.Splash, pos, 0.35f, 1.2f, 30f);
        }

        /// Walking on land/dock next to the boat (the dock edge is ~6 m from the moored hull: too far to jump).
        public bool CanBoardFromDock()
        {
            var p = Refs.Player;
            var gm = GameManager.I;
            if (p == null || gm == null || !gm.InGame || driving) return false;
            if (p.IsInWater || p.IsUnderwater || p.IsSeated || p.IsFainting) return false;
            return !PlayerAboard;
        }

        public void BoardFromDock()
        {
            if (!CanBoardFromDock()) return;
            Vector3 pos = transform.TransformPoint(parts.ladderDeckLocal);
            Refs.Player.TeleportTo(pos, transform.eulerAngles.y);
            BoatSynth.PlayAt(BoatSynth.Thunk, pos, 0.45f, 1.15f, 30f);
        }

        public void ToggleAnchor()
        {
            if (anchored) RaiseAnchor();
            else
            {
                DropAnchor(false);
                if (driving) throttle = 0f;
            }
        }

        void DropAnchor(bool instant)
        {
            anchored = true;
            anchorPoint = rb != null ? rb.position : transform.position;
            Vector3 roller = transform.TransformPoint(parts.rollerLocal);
            float ground = WorldInfo.GroundHeight(roller);
            float floorY = Mathf.Min(Mathf.Max(ground, Waves.SeaLevel - 45f), Waves.SeaLevel - 0.6f);
            anchorFloor = new Vector3(roller.x, floorY, roller.z);
            if (parts.anchor.parent == transform) parts.anchor.SetParent(transform.parent, true);
            if (instant)
            {
                parts.anchor.position = anchorFloor;
                parts.anchor.rotation = Quaternion.Euler(75f, transform.eulerAngles.y + 20f, 0f);
                anchorState = AnchorState.Down;
            }
            else
            {
                anchorState = AnchorState.Lowering;
                float depth = Mathf.Max(0f, Waves.SeaLevel - ground);
                GameEvents.RaiseToast(Loc.Tr($"Anker geworfen ({Loc.Meters(depth)} tief)", $"Anchor dropped ({Loc.Meters(depth)} deep)"));
                BoatSynth.PlayAt(BoatSynth.Splash, roller, 0.6f, 0.8f, 50f);
            }
        }

        void RaiseAnchor()
        {
            anchored = false;
            anchorState = AnchorState.Raising;
            anchorHintShown = false;
            GameEvents.RaiseToast(Loc.Tr("Anker gelichtet", "Anchor raised"));
        }

        // ------------------------------------------------------------------ input + animation
        void Update()
        {
            float dt = Time.deltaTime;
            var p = Refs.Player;
            bool inGame = GameManager.I != null && GameManager.I.InGame;

            if (driving && Time.time - seatedTime > 0.5f && (p == null || !p.IsSeated)) driving = false;

            bool canControl = driving && inGame && !UIState.BlocksGameplay;
            if (canControl && Time.frameCount != seatedFrame && Time.time - seatedTime > 0.25f && GameInput.InteractPressed)
            {
                if (Mathf.Abs(forwardSpeed) > ExitMaxSpeed)
                {
                    if (Time.time - lastSpeedHint > 2f)
                    {
                        lastSpeedHint = Time.time;
                        GameEvents.RaiseToast(Loc.Tr("Zu schnell zum Aufstehen – erst Gas weg!", "Too fast to get up – ease off the throttle first!"));
                    }
                }
                else
                {
                    ExitHelm();
                    canControl = false;
                }
            }
            if (inGame && !UIState.BlocksGameplay && GameInput.AnchorPressed && (driving || PlayerAboard)) ToggleAnchor();

            // controls hint (UIHud hides prompts while menus block gameplay)
            if (canControl) InteractionPrompt.Text = DrivePrompt();
            else if (!driving) ClearDrivePrompt();

            float tThr = canControl ? GameInput.Move.y : 0f;
            float tSteer = canControl ? GameInput.Move.x : 0f;
            if (anchored && canControl && Mathf.Abs(tThr) > 0.1f && !anchorHintShown)
            {
                anchorHintShown = true;
                GameEvents.RaiseToast(Loc.Tr("Der Anker ist unten – Q zum Lichten", "The anchor is down – Q to raise it"));
            }
            if (anchored) tThr *= 0.15f;
            throttle = Mathf.MoveTowards(throttle, tThr, dt * (Mathf.Abs(tThr) > Mathf.Abs(throttle) ? 1.4f : 2.5f));
            steer = Mathf.MoveTowards(steer, tSteer, dt * 2.2f);

            Animate(dt);
            UpdateFx(dt);
        }

        string DrivePrompt()
        {
            if (drivePrompt == null || drivePromptLang != Loc.Current)
            {
                drivePromptLang = Loc.Current;
                drivePrompt = Loc.Tr("W/S Gas · A/D Lenken · Q Anker · E Aufstehen", "W/S throttle · A/D steer · Q anchor · E get up");
            }
            return drivePrompt;
        }

        /// Clears the prompt only while it is still ours (the player's interactor owns it otherwise).
        void ClearDrivePrompt()
        {
            if (drivePrompt != null && ReferenceEquals(InteractionPrompt.Text, drivePrompt)) InteractionPrompt.Text = null;
        }

        void Animate(float dt)
        {
            wheelAngle = Mathf.Lerp(wheelAngle, steer * 140f, 1f - Mathf.Exp(-dt * 8f));
            parts.steeringWheel.localRotation = parts.wheelBaseRot * Quaternion.Euler(0f, 0f, -wheelAngle);
            parts.outboardPivot.localRotation = Quaternion.Euler(0f, -steer * 25f, 0f);
            parts.throttleLever.localRotation = parts.leverBaseRot * Quaternion.Euler(throttle * 35f, 0f, 0f);
            if (driving)
            {
                propAngle = Mathf.Repeat(propAngle + (220f + throttle * 2400f) * dt, 360f);
                radarAngle = Mathf.Repeat(radarAngle + 80f * dt, 360f);
                parts.propeller.localRotation = Quaternion.Euler(0f, 0f, propAngle);
                parts.radar.localRotation = Quaternion.Euler(0f, radarAngle, 0f);
            }
        }

        void UpdateFx(float dt)
        {
            float speed = Mathf.Abs(forwardSpeed);
            float thr = Mathf.Abs(throttle);
            bool engineOn = driving;

            BoatParticles.SetRate(smoke, engineOn ? Mathf.Lerp(4f, 22f, thr) : 0f);
            float spray = (submerged01 > 0.2f && speed > 3f) ? (speed - 3f) * 14f : 0f;
            BoatParticles.SetRate(sprayL, spray);
            BoatParticles.SetRate(sprayR, spray);
            BoatParticles.SetRate(wake, (submerged01 > 0.2f && speed > 0.8f) ? speed * 6f : 0f);

            // backfire when the throttle is slammed open from idle
            if (engineOn)
            {
                if (thr < 0.15f) { throttleWasLow = true; lowThrottleTime = Time.time; }
                else if (throttleWasLow && thr > 0.6f)
                {
                    throttleWasLow = false;
                    if (Time.time - lowThrottleTime < 0.9f && Random.value < 0.4f) Backfire();
                }
            }

            float target = engineOn ? 0.22f + 0.4f * thr : 0f;
            engineSrc.volume = Mathf.MoveTowards(engineSrc.volume, target, dt * 0.8f);
            engineSrc.pitch = 0.7f + 0.75f * thr + speed * 0.015f;
            if (engineSrc.volume > 0.001f) { if (!engineSrc.isPlaying) engineSrc.Play(); }
            else if (engineSrc.isPlaying) engineSrc.Stop();
        }

        void Backfire()
        {
            if (smoke == null) return;
            var ep = new ParticleSystem.EmitParams
            {
                position = parts.exhaust.position,
                applyShapeToPosition = true,
                startColor = new Color(0.12f, 0.12f, 0.13f, 0.85f),
                startSize = 0.55f
            };
            smoke.Emit(ep, 10);
            BoatSynth.PlayAt(BoatSynth.Backfire, parts.exhaust.position, 0.8f, Random.Range(0.85f, 1.1f), 90f);
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            // wake emitter sits on the water behind the stern
            Vector3 w = transform.TransformPoint(parts.sternWakeLocal);
            w.y = Waves.HeightAt(w.x, w.z) + 0.05f;
            wakeEmitter.position = w;
            UpdateAnchor(dt);
        }

        void UpdateAnchor(float dt)
        {
            Transform a = parts.anchor;
            Vector3 roller = transform.TransformPoint(parts.rollerLocal);
            switch (anchorState)
            {
                case AnchorState.Stowed:
                    if (rope.enabled) rope.enabled = false;
                    return;
                case AnchorState.Lowering:
                    a.position = Vector3.MoveTowards(a.position, anchorFloor, 7f * dt);
                    a.rotation = Quaternion.Slerp(a.rotation, Quaternion.Euler(75f, transform.eulerAngles.y + 20f, 0f), dt * 0.8f);
                    if ((a.position - anchorFloor).sqrMagnitude < 0.0025f) anchorState = AnchorState.Down;
                    break;
                case AnchorState.Down:
                    break;
                case AnchorState.Raising:
                {
                    Vector3 stow = transform.TransformPoint(parts.anchorStowLocal);
                    a.position = Vector3.MoveTowards(a.position, stow, 5f * dt);
                    a.rotation = Quaternion.Slerp(a.rotation, transform.rotation, dt * 3f);
                    if ((a.position - stow).sqrMagnitude < 0.01f)
                    {
                        a.SetParent(transform, false);
                        a.localPosition = parts.anchorStowLocal;
                        a.localRotation = Quaternion.identity;
                        anchorState = AnchorState.Stowed;
                        rope.enabled = false;
                        return;
                    }
                    break;
                }
            }

            // rope with a little sag
            Vector3 end = a.position;
            if (!rope.enabled) rope.enabled = true;
            float sag = Mathf.Min(1.5f, Vector3.Distance(roller, end) * 0.06f);
            int n = ropePts.Length;
            for (int i = 0; i < n; i++)
            {
                float s = i / (n - 1f);
                ropePts[i] = Vector3.Lerp(roller, end, s) + Vector3.down * (sag * 4f * s * (1f - s));
            }
            rope.SetPositions(ropePts);
        }

        // ------------------------------------------------------------------ physics
        void FixedUpdate()
        {
            if (rb == null) return;
            float dt = Time.fixedDeltaTime;
            Vector3 pos = rb.position;
            Quaternion rot = rb.rotation;

            // buoyancy (spring + damper per hull point)
            int sub = 0;
            for (int i = 0; i < BuoyPts.Length; i++)
            {
                Vector3 wp = pos + rot * BuoyPts[i];
                float d = Waves.HeightAt(wp.x, wp.z) - wp.y;
                if (d <= 0f) continue;
                sub++;
                Vector3 pv = rb.GetPointVelocity(wp);
                float f = Mathf.Min(d, 1.2f) * SpringK - pv.y * DampC;
                if (f > 0f) rb.AddForceAtPosition(new Vector3(0f, f, 0f), wp, ForceMode.Force);
            }
            float sub01 = sub / (float)BuoyPts.Length;
            submerged01 = sub01;

            Vector3 fwd = rot * Vector3.forward;
            Vector3 flatFwd = new Vector3(fwd.x, 0f, fwd.z);
            flatFwd = flatFwd.sqrMagnitude > 1e-4f ? flatFwd.normalized : Vector3.forward;
            Vector3 flatRight = new Vector3(flatFwd.z, 0f, -flatFwd.x);
            Vector3 v = rb.linearVelocity;
            float vf = Vector3.Dot(v, flatFwd), vl = Vector3.Dot(v, flatRight);
            forwardSpeed = vf;
            float maxSpeed = MaxSpeed * boatSpeedMult;

            if (sub01 > 0f)
            {
                float extra = (!driving && !anchored) ? 0.35f : 0f;
                Vector3 drag = -flatFwd * (vf * (0.08f + extra) + vf * Mathf.Abs(vf) * 0.004f) - flatRight * (vl * 3f);
                drag.y = -v.y * 0.4f;
                rb.AddForce(drag * sub01, ForceMode.Acceleration);
                rb.AddTorque(-rb.angularVelocity * (1.2f * sub01), ForceMode.Acceleration);
                Vector3 up = rot * Vector3.up;
                rb.AddTorque(Vector3.Cross(up, Vector3.up) * (3f * sub01), ForceMode.Acceleration);
            }

            if (driving && sub01 > 0f)
            {
                Vector3 propW = pos + rot * parts.propLocal;
                bool propWet = Waves.DepthOf(propW) > -0.05f;
                if (!anchored && Mathf.Abs(throttle) > 0.02f)
                {
                    float target = throttle > 0f ? throttle * maxSpeed * 1.1f : throttle * maxSpeed * 0.35f;
                    float acc = Mathf.Clamp((target - vf) * 1.1f, -4f, 3.4f);
                    if (!propWet) acc *= 0.3f;
                    rb.AddForce(flatFwd * acc, ForceMode.Acceleration);
                }
                // bow lifts with speed
                float speed01 = Mathf.Clamp01(vf / Mathf.Max(1f, maxSpeed));
                Vector3 right = rot * Vector3.right;
                rb.AddTorque(right * (-speed01 * 1.3f * sub01), ForceMode.Acceleration);

                // rudder: yaw-rate controller scaled with speed (outboards also turn a little at low speed)
                float yawAuth = Mathf.Max(Mathf.Clamp01(Mathf.Abs(vf) / 4f), Mathf.Abs(throttle) * 0.35f);
                float dir = vf >= -0.3f ? 1f : -1f;
                float desiredYaw = steer * 0.8f * yawAuth * dir;
                rb.AddTorque(Vector3.up * ((desiredYaw - rb.angularVelocity.y) * 2.5f), ForceMode.Acceleration);
                rb.AddTorque(fwd * (-steer * Mathf.Clamp01(Mathf.Abs(vf) / maxSpeed) * 1.8f), ForceMode.Acceleration);
            }

            // "parked": while someone walks on deck without driving, the boat holds still horizontally so the
            // character controller is not left behind by a drifting deck
            if (!driving && PlayerAboard)
            {
                rb.AddForce(new Vector3(-v.x, 0f, -v.z) * 2.5f, ForceMode.Acceleration);
                rb.AddTorque(Vector3.up * (-rb.angularVelocity.y * 3f), ForceMode.Acceleration);
            }

            if (anchored)
            {
                Vector3 to = anchorPoint - pos;
                to.y = 0f;
                float dist = to.magnitude;
                Vector3 acc = new Vector3(-v.x, 0f, -v.z) * 1.1f;
                if (dist > 2.5f) acc += to / dist * Mathf.Min((dist - 2.5f) * 0.6f, 4f);
                rb.AddForce(acc, ForceMode.Acceleration);
                rb.AddTorque(Vector3.up * (-rb.angularVelocity.y * 1.5f + Mathf.Sin(Time.time * 0.11f) * 0.015f), ForceMode.Acceleration);
            }

            // stuck on a beach while driving: tow back to deep water after a while
            if (driving && sub <= 1) beachedTimer += dt; else beachedTimer = 0f;
            if (beachedTimer > 6f && !towing) StartTow();

            // capsized: put it back upright
            if ((rot * Vector3.up).y < 0.35f) flippedTimer += dt; else flippedTimer = 0f;
            if (flippedTimer > 3f)
            {
                flippedTimer = 0f;
                PlaceAt(pos, rot.eulerAngles.y);
            }
        }

        void StartTow()
        {
            towing = true;
            beachedTimer = 0f;
            Fader.FadeThrough(() =>
            {
                towing = false;
                Vector3 from = rb.position;
                if (FindDeepWater(from, out Vector3 p, out float yaw))
                {
                    PlaceAt(p, yaw);
                    GameEvents.RaiseToast(Loc.Tr("Festgefahren! Ein Fischer hat dich freigeschleppt.", "Stuck! A fisherman towed you free."));
                }
            }, 1.5f);
        }

        static bool FindDeepWater(Vector3 from, out Vector3 point, out float yaw)
        {
            for (float r = 10f; r <= 200f; r += 10f)
                for (int k = 0; k < 16; k++)
                {
                    float a = k / 16f * Mathf.PI * 2f;
                    Vector3 dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                    Vector3 p = from + dir * r;
                    if (WorldInfo.GroundHeight(p) < Waves.SeaLevel - 3f)
                    {
                        point = p;
                        yaw = a * Mathf.Rad2Deg;
                        return true;
                    }
                }
            point = from;
            yaw = 0f;
            return false;
        }
    }
}
