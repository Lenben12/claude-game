using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// The player's boat "Rostlaube": buoyancy on Waves, driving from the helm, anchor, effects and engine audio.
    /// Implements IBoat (Refs.Boat). Local space: +Z bow, y = 0 design waterline, deck at BoatHull.DeckY.
    /// Driving starts ONLY at the wheel: standing on the deck within HelmRange of the driver seat (feedback #8).
    /// Co-op: while NetFlags.BoatRemote another machine simulates the boat – physics and driver input are skipped,
    /// the rigidbody turns kinematic and the Net module moves Root (or calls ApplyRemotePose); passengers on deck are
    /// carried by the player's platform code. The seat refuses while NetFlags.SeatTakenByOther.
    public class BoatController : MonoBehaviour, IBoat
    {
        // ------------------------------------------------------------------ tuning
        const float Mass = 1500f;
        const float SpringK = 6150f;     // N per meter of submersion per buoyancy point (8 points carry 1500 kg at 0.3 m)
        const float DampC = 1100f;       // N per m/s per point
        const float MaxSpeed = 11f;      // m/s at boatSpeed 1
        const float ExitMaxSpeed = 3f;   // the driver can only get up below this speed
        /// Max horizontal distance between the player (on deck) and the driver seat to take the wheel.
        public const float HelmRange = 2f;

        static readonly Vector3[] BuoyPts =
        {
            new Vector3(-0.85f, -0.3f, -3.1f), new Vector3(0.85f, -0.3f, -3.1f),
            new Vector3(-0.95f, -0.3f, -1.1f), new Vector3(0.95f, -0.3f, -1.1f),
            new Vector3(-0.85f, -0.3f, 0.9f), new Vector3(0.85f, -0.3f, 0.9f),
            new Vector3(-0.45f, -0.3f, 2.6f), new Vector3(0.45f, -0.3f, 2.6f),
        };


        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { AnchorToggledLocally = null; lastNotDriverHint = -10f; }

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
        Vector3 raiseLastStow;
        bool raiseStowValid;
        LineRenderer rope;
        readonly Vector3[] ropePts = new Vector3[12];

        ParticleSystem smoke, sprayL, sprayR, wake;
        Transform wakeEmitter;
        AudioSource engineSrc;

        // controls hint shown as the interaction prompt while driving (PlayerInteractor is inactive while seated)
        string drivePrompt;
        Language drivePromptLang;
        string dpFwd, dpBack, dpLeft, dpRight, dpAnchor, dpExit, dpMap;

        // co-op: simulated elsewhere (NetFlags.BoatRemote)
        bool remote;
        Vector3 remoteVel, lastRemotePos;
        bool lastRemoteValid;
        float remoteThrottle, remoteSteer, remoteControlsTime = -10f;

        // round 2: fog bank (K.8), compass, compressor (E.4), winch davit (K.3), flag (L.7 / K.7)
        /// K.8: without Toni's map the fog bank pushes the boat outward (m/s it reaches against the bow).
        public const float FogDriftSpeed = 2f;
        bool fogMap, inFogBank, fogToastShown;
        // round 3: the volcano's current ring until the wreck is unlocked (VolcanoGate)
        bool inVolcanoRing, volcanoToastShown;
        float volcanoGateCheck;
        bool volcanoOpen = true;
        float compassAngle, compassSpinVel, compressorRpm, flagPoll, flagWave;
        AudioSource chugSrc;
        bool compressorOwned, winchOwned;
        int flagShown = -1, pennantShown = -1;
        GameObject flagGo;

        /// Model anchor points (winch pulley, foredeck crate spot …) for the Boat module's helpers.
        public BoatParts Parts => parts;
        /// The crew owns the boat compressor (E.4): the tank refills on deck (Player module asks BoatAirStation).
        public bool HasCompressor => compressorOwned;
        public bool HasWinch => winchOwned;
        /// The boat is inside the Fog Isle's fog bank right now (K.8).
        public bool InFogBank => inFogBank;

        // ------------------------------------------------------------------ IBoat
        public Transform Root => transform;
        public bool PlayerDriving => driving;
        public bool Anchored => anchored;
        /// True while another co-op machine simulates the boat (NetFlags.BoatRemote).
        public bool IsRemote => remote;

        /// Raised when the LOCAL player drops (true) or raises (false) the anchor – the Net module forwards it to the
        /// machine that simulates the boat (SetAnchoredRemote).
        public static event System.Action<bool> AnchorToggledLocally;

        /// The local player is on the deck next to the wheel, but another player is driving (shows "Besetzt").
        public bool HelmOccupied => NetFlags.SeatTakenByOther && !driving && InHelmReach();
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
                return IsOnDeck(r.position);
            }
        }

        /// A diver's feet position is on the deck (between the rails, bow to stern, above the water).
        public bool IsOnDeck(Vector3 wp)
        {
            Vector3 lp = transform.InverseTransformPoint(wp);
            if (lp.y < BoatHull.DeckY - 0.6f || lp.y > BoatHull.DeckY + 3.2f) return false;
            if (lp.z < -4.3f || lp.z > 3.75f) return false;
            float hw = lp.z < -3.74f ? 1.15f : BoatHull.DeckHalfAtZ(lp.z) + 0.15f;
            if (Mathf.Abs(lp.x) > hw) return false;
            return wp.y > Waves.HeightAt(wp.x, wp.z) - 0.2f;
        }

        // ------------------------------------------------------------------ mooring ("Zur Hütte", see BoatHome)
        /// The boat's spot at the dock (where every game starts and every slept morning puts it).
        public Vector3 MooringPosition => WorldInfo.BoatSpawn != Vector3.zero ? WorldInfo.BoatSpawn : spawnPos;
        public float MooringYaw => WorldInfo.BoatSpawn != Vector3.zero ? WorldInfo.BoatSpawnYaw : spawnYaw;

        /// Lying at (or right next to) its mooring already: nothing to bring back.
        public bool AtMooring
        {
            get
            {
                Vector3 d = transform.position - MooringPosition;
                d.y = 0f;
                return d.sqrMagnitude < 8f * 8f;
            }
        }

        /// Back to the mooring at the dock: stopped, anchored at the dock, no tow pending, nobody at the wheel.
        /// Only where this machine simulates the boat (offline, or the co-op host while nobody drives) – BoatHome decides.
        public bool ReturnToMooring()
        {
            if (remote || NetFlags.BoatRemote) return false;
            if (driving) ExitHelm();
            towing = false;
            towToken++;
            beachedTimer = 0f;
            anchored = true;
            PlaceAt(MooringPosition, MooringYaw);   // zeroes the velocities and re-drops the anchor at the dock
            GameEvents.RaiseBoatReturnedHome();
            return true;
        }

        int towToken;

        public void PlaceAt(Vector3 position, float yawDegrees)
        {
            position.y = Waves.HeightAt(position.x, position.z);
            Quaternion rot = Quaternion.Euler(0f, yawDegrees, 0f);
            if (rb != null)
            {
                rb.position = position;
                rb.rotation = rot;
                if (!rb.isKinematic)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
            }
            lastRemoteValid = false;
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

            if (parts.compressor != null)
            {
                chugSrc = BoatSynth.LoopSource(parts.compressor.gameObject, BoatSynth.Chug, 2f, 25f, AudioRolloffMode.Linear);
                chugSrc.dopplerLevel = 0f;
            }
            gameObject.AddComponent<BoatDisembark>().Init(this);

            GameEvents.GameStarted += OnGameStarted;
            GameEvents.ReturnedToMenu += OnReturnedToMenu;
            GameEvents.GearChanged += OnGearChanged;
            GameEvents.DayStarted += OnDayStarted;
            GameEvents.StoryFlagSet += OnStoryFlag;
            Net.StateSynced += OnStateSynced;
            BoatSurface.BoatBody = rb;

            PlaceAt(spawnPos, spawnYaw);
            DropAnchor(true);
        }

        void OnDestroy()
        {
            GameEvents.GameStarted -= OnGameStarted;
            GameEvents.ReturnedToMenu -= OnReturnedToMenu;
            GameEvents.GearChanged -= OnGearChanged;
            GameEvents.DayStarted -= OnDayStarted;
            GameEvents.StoryFlagSet -= OnStoryFlag;
            Net.StateSynced -= OnStateSynced;
            BoatTelemetry.Valid = false;
            ClearDrivePrompt();
            if (rope != null) Destroy(rope.gameObject);
            if (parts != null && parts.anchor != null && parts.anchor.parent != transform) Destroy(parts.anchor.gameObject);
            if (ReferenceEquals(Refs.Boat, this)) Refs.Boat = null;
            if (BoatSurface.BoatBody == rb) BoatSurface.BoatBody = null;
        }

        void OnGameStarted()
        {
            driving = false;
            ClearDrivePrompt();
            towing = false;
            anchorHintShown = false;
            OnGearChanged();
            // a co-op guest takes pose and anchor from the owner's stream (placing it at the dock here would leave the
            // anchor lying at the dock while the hull follows the owner out at sea)
            if (Net.IsClient) return;
            anchored = true;
            PlaceAt(spawnPos, spawnYaw);
        }

        /// After a night's sleep the boat is back at the dock, like the player is back in the hut.
        /// Same "slept" check as the player module: the midnight rollover (clock not at morning) does not move it.
        /// Online the player module teleports everybody to the hut on a slept morning, so a leftover driver seat or a
        /// player still counted as aboard never keeps the boat out at sea (the Net module took the wheel back already).
        void OnDayStarted(int day)
        {
            var gm = GameManager.I;
            if (remote || gm == null || !gm.InGame) return;
            if (Mathf.Abs(gm.State.timeOfDay - GameManager.MorningTime) > 0.01f) return;
            if (Net.IsOnline)
            {
                if (driving)
                {
                    driving = false;
                    ClearDrivePrompt();
                }
            }
            else if (driving || PlayerAboard) return;
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
            var gm = GameManager.I;
            GearStats g = gm != null && gm.State != null ? gm.Gear : default;
            boatSpeedMult = gm != null ? Mathf.Max(0.5f, g.boatSpeed) : 1f;
            // crew equipment shows on the boat as soon as somebody opened its crate
            compressorOwned = gm != null && g.hasBoatCompressor;
            winchOwned = gm != null && g.hasBoatWinch;
            if (parts != null)
            {
                if (parts.compressor != null && parts.compressor.gameObject.activeSelf != compressorOwned) parts.compressor.gameObject.SetActive(compressorOwned);
                if (parts.davit != null && parts.davit.gameObject.activeSelf != winchOwned) parts.davit.gameObject.SetActive(winchOwned);
            }
            fogMap = StoryFlags.Has(StoryKeys.FogMap);
        }

        void OnStoryFlag(string flag)
        {
            if (flag == StoryKeys.FogMap) fogMap = true;
        }

        void OnStateSynced()
        {
            // a guest learns about bought equipment / Toni's map from the host's snapshot
            OnGearChanged();
            flagPoll = 0f;
        }

        // ------------------------------------------------------------------ helm / ladder / anchor (called by interactables)
        static IPlayer LocalPlayer
        {
            get
            {
                var p = Refs.Player;
                if (p == null || (p is Object o && o == null)) return null;
                return p;
            }
        }

        /// The local player stands on the deck (not in the water, not seated) within HelmRange of the driver seat.
        bool InHelmReach()
        {
            var p = LocalPlayer;
            if (p == null || parts == null || parts.seat == null || p.IsInWater || p.IsUnderwater || p.IsSeated || p.IsFainting) return false;
            Transform r = p.Root;
            if (r == null || !PlayerAboard) return false;
            Vector3 d = r.position - parts.seat.position;
            d.y = 0f;
            return d.sqrMagnitude <= HelmRange * HelmRange;
        }

        public bool CanEnterHelm()
        {
            // cooldown: the same E press that gets the driver up must not re-seat them through the interactor
            if (Time.frameCount == exitFrame || Time.time - exitTime < 0.4f) return false;
            if (driving || NetFlags.SeatTakenByOther || GameManager.I == null || !GameManager.I.InGame) return false;
            return InHelmReach();
        }

        public void EnterHelm()
        {
            if (!CanEnterHelm()) return;
            Hands.StowAll();   // both hands on the wheel
            LocalPlayer.SetSeated(parts.seat);
            driving = true;
            seatedFrame = Time.frameCount;
            seatedTime = Time.time;
            throttle = 0f;
            // the controls are shown as the centre prompt while seated – no duplicate toast on the first sit
            if (anchored)
            {
                AnchorHint();
                anchorHintShown = true;
            }
        }

        static float lastNotDriverHint = -10f;

        /// Round 3: somebody who is not at the wheel tried the anchor (key or winch) – one short line, max every 3 s.
        public static void NotDriverHint()
        {
            if (Time.unscaledTime - lastNotDriverHint < 3f) return;
            lastNotDriverHint = Time.unscaledTime;
            GameEvents.RaiseNotify(NotifyKind.Warning, Loc.Tr("Anker nur vom Fahrer", "Anchor: driver only"), Loc.Tr("am Steuer", "at the wheel"));
        }

        /// L.10: "Anker ist unten" + key chip.
        void AnchorHint()
        {
            string q = GameInput.BindingLabel(GameAction.Anchor);
            GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr("Der Anker ist unten", "The anchor is down"), Loc.Tr(q + " – lichten", q + " – raise"));
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
            var p = LocalPlayer;
            Vector3 pos = transform.TransformPoint(parts.ladderDeckLocal);
            p.TeleportTo(pos, transform.eulerAngles.y);
            // no instant refill: the tank is refilled by resting on the deck (Player module, feedback #17)
            if (GameManager.I != null && GameManager.I.InGame) Footage.BankDive();
            BoatSynth.PlayAt(BoatSynth.Splash, pos, 0.35f, 1.2f, 30f);
            // K.3: heavy salvage carried up the ladder is delivered on deck
            SalvageDelivery.DeliverCarried(pos);
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

        /// Local player action (winch or the anchor key). Forwarded to the simulating machine in co-op.
        public void ToggleAnchor()
        {
            if (anchored) RaiseAnchor();
            else
            {
                DropAnchor(false);
                if (driving) throttle = 0f;
            }
            AnchorToggledLocally?.Invoke(anchored);
        }

        /// Co-op: applies the anchor state decided on another machine (no event, short toast only when it changes).
        public void SetAnchoredRemote(bool down) => SetAnchoredRemote(down, false);

        /// Co-op: instant = silent correction from the owner's stream (drop-in join, slept morning, toggle races):
        /// down → the anchor lies under the CURRENT roller; up → stowed on deck. No toast, no animation.
        public void SetAnchoredRemote(bool down, bool instant)
        {
            if (!instant)
            {
                if (down == anchored) return;
                if (down) DropAnchor(false);
                else RaiseAnchor();
                return;
            }
            if (parts == null || parts.anchor == null) return;
            if (down)
            {
                if (anchored && !AnchorFar) return;
                DropAnchor(true);
                return;
            }
            if (!anchored && anchorState == AnchorState.Stowed) return;
            anchored = false;
            parts.anchor.SetParent(transform, false);
            parts.anchor.localPosition = parts.anchorStowLocal;
            parts.anchor.localRotation = Quaternion.identity;
            anchorState = AnchorState.Stowed;
            raiseStowValid = false;
            if (rope != null) rope.enabled = false;
        }

        /// The anchor lies on the seabed far away from the roller (stale state after a remote pose jump).
        public bool AnchorFar
        {
            get
            {
                if (anchorState != AnchorState.Down || parts == null || parts.anchor == null) return false;
                Vector3 d = parts.anchor.position - transform.TransformPoint(parts.rollerLocal);
                d.y = 0f;
                return d.sqrMagnitude > 60f * 60f;
            }
        }

        /// Co-op: throttle / steering of the remote driver for the wheel, outboard, engine sound and smoke on this machine.
        public void SetRemoteControls(float throttleInput, float steerInput)
        {
            remoteThrottle = Mathf.Clamp(throttleInput, -1f, 1f);
            remoteSteer = Mathf.Clamp(steerInput, -1f, 1f);
            remoteControlsTime = Time.time;
        }

        /// Co-op: pose from the simulating machine (alternative to setting Root directly). Keeps the kinematic body in sync.
        public void ApplyRemotePose(Vector3 position, Quaternion rotation)
        {
            Vector3 prev = transform.position;
            if (rb != null && rb.isKinematic)
            {
                rb.position = position;
                rb.rotation = rotation;
            }
            transform.SetPositionAndRotation(position, rotation);
            // the owner teleported the boat (home to the dock via "Zur Hütte", towed free, slept morning): an anchor
            // lying on the old seabed spot is re-dropped under the roller at once (the stream only heals it beyond 60 m)
            if (anchored && anchorState == AnchorState.Down && (position - prev).sqrMagnitude > 6f * 6f) DropAnchor(true);
        }

        /// Follows NetFlags.BoatRemote. The Net module (NetBoatSync) normally switches the rigidbody itself (it also
        /// hands over the last streamed velocity); the body is only touched here when it is not yet in the right mode.
        void SetRemote(bool on)
        {
            remote = on;
            lastRemoteValid = false;
            beachedTimer = 0f;
            flippedTimer = 0f;
            if (rb == null) return;
            if (on)
            {
                if (!rb.isKinematic)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    rb.isKinematic = true;
                    rb.interpolation = RigidbodyInterpolation.None;
                }
            }
            else
            {
                if (rb.isKinematic)
                {
                    rb.isKinematic = false;
                    rb.interpolation = RigidbodyInterpolation.Interpolate;
                    rb.position = transform.position;
                    rb.rotation = transform.rotation;
                    rb.linearVelocity = Vector3.ClampMagnitude(remoteVel, MaxSpeed * 1.5f);
                    rb.angularVelocity = Vector3.zero;
                }
                if (anchored) anchorPoint = transform.position;
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
                GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr("Anker geworfen", "Anchor dropped"), Loc.Meters(depth));
                BoatSynth.PlayAt(BoatSynth.Splash, roller, 0.6f, 0.8f, 50f);
            }
        }

        void RaiseAnchor()
        {
            anchored = false;
            anchorState = AnchorState.Raising;
            raiseStowValid = false;
            anchorHintShown = false;
            GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr("Anker gelichtet", "Anchor raised"));
        }

        // ------------------------------------------------------------------ input + animation
        void Update()
        {
            float dt = Time.deltaTime;
            var p = LocalPlayer;
            bool inGame = GameManager.I != null && GameManager.I.InGame;

            bool wantRemote = NetFlags.BoatRemote;
            if (wantRemote != remote) SetRemote(wantRemote);

            if (driving && Time.time - seatedTime > 0.5f && (p == null || !p.IsSeated)) driving = false;
            // (co-op: a lost race for the seat is resolved by the Net module, which calls ExitHelm)

            bool seatedInput = driving && inGame && !UIState.BlocksGameplay;
            if (seatedInput && Time.frameCount != seatedFrame && Time.time - seatedTime > 0.25f && GameInput.InteractPressed)
            {
                if (Mathf.Abs(forwardSpeed) > ExitMaxSpeed)
                {
                    if (Time.time - lastSpeedHint > 2f)
                    {
                        lastSpeedHint = Time.time;
                        GameEvents.RaiseNotify(NotifyKind.Warning, Loc.Tr("Zu schnell zum Aufstehen", "Too fast to get up"), Loc.Tr("Gas weg", "Ease off"));
                    }
                }
                else
                {
                    ExitHelm();
                    seatedInput = false;
                }
            }
            // round 3 (GDD §19 „Nur der Fahrer kann den Anker legen“): only the driver works the anchor
            if (inGame && !UIState.BlocksGameplay && GameInput.AnchorPressed)
            {
                if (driving) ToggleAnchor();
                else if (PlayerAboard) NotDriverHint();
            }

            // controls hint (UIHud hides prompts while menus block gameplay)
            if (seatedInput) InteractionPrompt.Text = DrivePrompt();
            else if (!driving) ClearDrivePrompt();

            // driver input only when this machine simulates the boat
            bool canControl = seatedInput && !remote;
            float tThr = canControl ? GameInput.Move.y : 0f;
            float tSteer = canControl ? GameInput.Move.x : 0f;
            if (anchored && canControl && Mathf.Abs(tThr) > 0.1f && !anchorHintShown)
            {
                anchorHintShown = true;
                AnchorHint();
            }
            if (anchored) tThr *= 0.15f;
            if (remote && Time.time - remoteControlsTime < 0.75f)
            {
                tThr = remoteThrottle;
                tSteer = remoteSteer;
            }
            else if (remote)
            {
                // no control stream: estimate the throttle from the speed for the engine effects
                tThr = NetFlags.BoatDriverId >= 0 ? Mathf.Clamp(forwardSpeed / (MaxSpeed * boatSpeedMult), -1f, 1f) : 0f;
                tSteer = 0f;
            }
            throttle = Mathf.MoveTowards(throttle, tThr, dt * (Mathf.Abs(tThr) > Mathf.Abs(throttle) ? 1.4f : 2.5f));
            steer = Mathf.MoveTowards(steer, tSteer, dt * 2.2f);

            Animate(dt);
            UpdateFx(dt);
        }

        /// The throttle/steer input of THIS machine's driver (Net module streams it to the others).
        public float DriverThrottle => driving && !remote ? throttle : 0f;
        public float DriverSteer => driving && !remote ? steer : 0f;

        bool EngineOn => driving || (remote && NetFlags.BoatDriverId >= 0);

        string DrivePrompt()
        {
            string f = GameInput.BindingLabel(GameAction.Forward), b = GameInput.BindingLabel(GameAction.Back);
            string l = GameInput.BindingLabel(GameAction.Left), r = GameInput.BindingLabel(GameAction.Right);
            string q = GameInput.BindingLabel(GameAction.Anchor), e = GameInput.BindingLabel(GameAction.Interact);
            string m = GameInput.BindingLabel(GameAction.Map);
            if (drivePrompt == null || drivePromptLang != Loc.Current || !ReferenceEquals(f, dpFwd) || !ReferenceEquals(b, dpBack)
                || !ReferenceEquals(l, dpLeft) || !ReferenceEquals(r, dpRight) || !ReferenceEquals(q, dpAnchor) || !ReferenceEquals(e, dpExit)
                || !ReferenceEquals(m, dpMap))
            {
                drivePromptLang = Loc.Current;
                dpFwd = f; dpBack = b; dpLeft = l; dpRight = r; dpAnchor = q; dpExit = e; dpMap = m;
                // round 3: the map key works while driving (UIMapPanel does not stop the steering)
                drivePrompt = Loc.Tr($"{f}/{b} Gas · {l}/{r} Lenken · {q} Anker · {m} Karte · {e} Aufstehen",
                                     $"{f}/{b} throttle · {l}/{r} steer · {q} anchor · {m} map · {e} get up");
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
            if (EngineOn)
            {
                propAngle = Mathf.Repeat(propAngle + (220f + throttle * 2400f) * dt, 360f);
                radarAngle = Mathf.Repeat(radarAngle + 80f * dt, 360f);
                parts.propeller.localRotation = Quaternion.Euler(0f, 0f, propAngle);
                parts.radar.localRotation = Quaternion.Euler(0f, radarAngle, 0f);
            }
            AnimateCompass(dt);
            AnimateCompressor(dt);
            AnimateFlag(dt);
        }

        /// The dashboard compass points north – and spins wildly in the fog bank without Toni's map (K.8). With the map
        /// a gold course marker points the way to the Fog Isle (the buoy lane start first, then the island).
        void AnimateCompass(float dt)
        {
            if (parts.compassNeedle == null) return;
            float yaw = transform.eulerAngles.y;
            bool lostInFog = inFogBank && !fogMap;
            if (lostInFog)
            {
                compassSpinVel = Mathf.MoveTowards(compassSpinVel, 520f + Mathf.Sin(Time.time * 1.7f) * 260f, 900f * dt);
                compassAngle += compassSpinVel * dt;
            }
            else
            {
                compassSpinVel = 0f;
                float north = -yaw;
                float k = 1f - Mathf.Exp(-5f * dt);
                compassAngle = Mathf.LerpAngle(compassAngle, north + Mathf.Sin(Time.time * 2.1f) * 4f, k);
            }
            parts.compassNeedle.localRotation = Quaternion.Euler(0f, compassAngle, 0f);

            Vector3 target = Vector3.zero;
            bool course = fogMap && FogRoute.HasCourse(transform.position, out target);
            if (parts.compassCourse != null)
            {
                if (parts.compassCourse.gameObject.activeSelf != course) parts.compassCourse.gameObject.SetActive(course);
                if (course)
                {
                    Vector3 d = target - transform.position;
                    float bearing = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                    parts.compassCourse.localRotation = Quaternion.Euler(0f, bearing - yaw, 0f);
                }
            }
        }

        /// E.4: the compressor chugs and its flywheel spins while the local player's tank refills on deck.
        void AnimateCompressor(float dt)
        {
            if (!compressorOwned || parts.compressor == null)
            {
                if (chugSrc != null && chugSrc.isPlaying) chugSrc.Stop();
                return;
            }
            bool running = BoatAirStation.LocalRefillingHere(this);
            compressorRpm = Mathf.MoveTowards(compressorRpm, running ? 1f : 0f, dt * (running ? 1.5f : 0.6f));
            if (parts.compressorWheel != null && compressorRpm > 0.001f) parts.compressorWheel.Rotate(0f, 0f, 900f * compressorRpm * dt, Space.Self);
            // a little shake on its straps
            float shake = compressorRpm * 0.006f;
            parts.compressor.localRotation = Quaternion.Euler(Mathf.Sin(Time.time * 57f) * shake * 60f, 0f, Mathf.Sin(Time.time * 43f) * shake * 60f);
            if (chugSrc != null)
            {
                chugSrc.volume = compressorRpm * 0.45f * Settings.SfxVolume;
                chugSrc.pitch = 0.8f + compressorRpm * 0.25f;
                if (chugSrc.volume > 0.001f) { if (!chugSrc.isPlaying) chugSrc.Play(); }
                else if (chugSrc.isPlaying) chugSrc.Stop();
            }
        }

        /// CosmeticsDatabase ids of the boat flags (Mira's rewards M2 / M3).
        static readonly string FlagWavesId = CosmeticsDatabase.Id(CosmeticsDatabase.BoatFlag, 1), FlagAltriffId = CosmeticsDatabase.Id(CosmeticsDatabase.BoatFlag, 2);

        /// L.7 boat flag reward (highest unlocked "boatflag:n") + K.7 a sponsor pennant while a deal runs.
        void AnimateFlag(float dt)
        {
            if (parts.flag == null) return;
            flagPoll -= dt;
            if (flagPoll <= 0f)
            {
                flagPoll = 2f;
                int want = Cosmetics.IsUnlocked(FlagAltriffId) ? 2 : Cosmetics.IsUnlocked(FlagWavesId) ? 1 : 0;
                int pennant = -1;
                var act = Deals.Active;
                if (act != null && act.Count > 0 && act[0] != null && !string.IsNullOrEmpty(act[0].sponsorId))
                    pennant = (int)(HeliService.HashString(act[0].sponsorId) % 6u);
                if (want != flagShown || pennant != pennantShown)
                {
                    flagShown = want;
                    pennantShown = pennant;
                    if (flagGo != null) Destroy(flagGo);
                    flagGo = BoatFlags.Build(parts.flag, want, pennant);
                    bool show = flagGo != null;
                    var pole = parts.flag.parent;
                    if (pole != null && pole.gameObject.activeSelf != show) pole.gameObject.SetActive(show);
                }
            }
            if (flagGo == null) return;
            // flutter: faster with speed / wind
            float speed = Mathf.Abs(forwardSpeed);
            flagWave += dt * (2.5f + speed * 0.6f);
            parts.flag.localRotation = Quaternion.Euler(0f, 180f + Mathf.Sin(flagWave) * (12f - Mathf.Min(8f, speed)), Mathf.Sin(flagWave * 1.7f) * 4f);
        }

        void UpdateFx(float dt)
        {
            float speed = Mathf.Abs(forwardSpeed);
            float thr = Mathf.Abs(throttle);
            bool engineOn = EngineOn;

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

            float target = engineOn ? (0.22f + 0.4f * thr) * Settings.SfxVolume : 0f;
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
            if (remote && dt > 0f) TrackRemoteMotion(dt);
            PublishTelemetry();
            UpdateFogState();
            UpdateVolcanoGate();
            // wake emitter sits on the water behind the stern
            Vector3 w = transform.TransformPoint(parts.sternWakeLocal);
            w.y = Waves.HeightAt(w.x, w.z) + 0.05f;
            wakeEmitter.position = w;
            UpdateAnchor(dt);
        }

        /// BoatTelemetry (J.3): read by fauna (dolphins at the bow wave from 6 m/s), livestream (signal), wrist computer.
        void PublishTelemetry()
        {
            Vector3 v = remote ? remoteVel : (rb != null && !rb.isKinematic ? rb.linearVelocity : Vector3.zero);
            BoatTelemetry.Valid = GameManager.I != null && GameManager.I.InGame;
            BoatTelemetry.Position = transform.position;
            BoatTelemetry.Speed = new Vector2(v.x, v.z).magnitude;
            BoatTelemetry.Anchored = anchored;
        }

        /// K.8 fog bank: inside FogBankRadius around the Fog Isle. Without Toni's map the boat drifts out (FixedUpdate)
        /// and the compass spins; a short notification when somebody on board enters it.
        void UpdateFogState()
        {
            Vector3 d = transform.position - WorldLayout.FogIsland;
            d.y = 0f;
            bool inside = d.sqrMagnitude < WorldLayout.FogBankRadius * WorldLayout.FogBankRadius;
            if (inside == inFogBank) return;
            inFogBank = inside;
            if (!inside) { fogToastShown = false; return; }
            if (!fogMap && !fogToastShown && (driving || PlayerAboard))
            {
                fogToastShown = true;
                GameEvents.RaiseNotify(NotifyKind.Warning, Loc.Tr("Dichter Nebel – kein Durchkommen", "Thick fog – no way through"), Loc.Tr("Karte fehlt", "No map"));
            }
        }

        /// Round 3 (player answer): the volcano island opens with the wreck unlock. Inside WorldLayout.VolcanoGateRadius a
        /// strong current pushes the boat back out while it is closed (FixedUpdate); entering shows the requirement once.
        void UpdateVolcanoGate()
        {
            if (Time.time >= volcanoGateCheck)
            {
                volcanoGateCheck = Time.time + 1f;
                volcanoOpen = VolcanoGate.Open;
            }
            bool inside = VolcanoGate.InsideRing(transform.position);
            if (inside == inVolcanoRing) return;
            inVolcanoRing = inside;
            if (!inside) { volcanoToastShown = false; return; }
            if (!volcanoOpen && !volcanoToastShown && (driving || PlayerAboard))
            {
                volcanoToastShown = true;
                GameEvents.RaiseNotify(NotifyKind.Warning, Loc.Tr("Starke Strömung – kein Durchkommen", "Strong current – no way through"), VolcanoGate.RequirementText());
            }
        }

        /// Remote-driven boat: estimate the velocity from the pose stream (effects + a smooth hand-over to local physics).
        void TrackRemoteMotion(float dt)
        {
            Vector3 pos = transform.position;
            if (lastRemoteValid)
            {
                Vector3 v = (pos - lastRemotePos) / dt;
                if (v.sqrMagnitude > 40f * 40f) v = Vector3.zero;   // teleport / snap
                remoteVel = Vector3.Lerp(remoteVel, v, 1f - Mathf.Exp(-6f * dt));
            }
            else remoteVel = Vector3.zero;
            lastRemotePos = pos;
            lastRemoteValid = true;
            Vector3 fwd = transform.forward;
            fwd.y = 0f;
            forwardSpeed = fwd.sqrMagnitude > 1e-4f ? Vector3.Dot(remoteVel, fwd.normalized) : 0f;
            Vector3 stern = transform.TransformPoint(new Vector3(0f, -0.3f, -2f));
            submerged01 = Waves.HeightAt(stern.x, stern.z) > stern.y ? 1f : 0f;
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
                    // the boat may drive off faster than the winch pulls (6–8 m/s): the anchor comes along with the
                    // boat and is winched in relative to it, so the rope never stretches back across the deck
                    if (raiseStowValid) a.position += stow - raiseLastStow;
                    raiseLastStow = stow;
                    raiseStowValid = true;
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
            if (rb == null || remote || NetFlags.BoatRemote || rb.isKinematic) return;   // co-op: simulated on another machine
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

            // K.8 fog bank without Toni's map: a current pushes the boat back out (no headway toward the island, at
            // least 2 m/s outward) and the bow is shoved around – you simply cannot find the way through
            if (!fogMap && sub01 > 0f)
            {
                Vector3 fromIsle = pos - WorldLayout.FogIsland;
                fromIsle.y = 0f;
                float r = fromIsle.magnitude;
                if (r < WorldLayout.FogBankRadius && r > 1f)
                {
                    Vector3 outward = fromIsle / r;
                    float vr = Vector3.Dot(v, outward);
                    float deep = Mathf.Clamp01((WorldLayout.FogBankRadius - r) / 60f);   // full strength 60 m into the bank
                    float acc = Mathf.Clamp((FogDriftSpeed - vr) * (0.8f + 2.2f * deep), 0f, 4f + 6f * deep);
                    if (anchored) anchorPoint = Vector3.MoveTowards(anchorPoint, pos + outward * 3f, FogDriftSpeed * dt);   // the anchor drags
                    rb.AddForce(outward * (acc * sub01), ForceMode.Acceleration);
                    rb.AddTorque(Vector3.up * (Mathf.Sin(Time.time * 0.9f + r * 0.05f) * 0.35f * deep * sub01), ForceMode.Acceleration);
                }
            }

            // round 3: the volcano's current ring while the wreck is still locked – same push-back as the fog bank
            if (!volcanoOpen && sub01 > 0f)
            {
                Vector3 fromVol = pos - WorldLayout.VolcanoIsland;
                fromVol.y = 0f;
                float r = fromVol.magnitude;
                if (r < WorldLayout.VolcanoGateRadius && r > 1f)
                {
                    Vector3 outward = fromVol / r;
                    float vr = Vector3.Dot(v, outward);
                    float deep = Mathf.Clamp01((WorldLayout.VolcanoGateRadius - r) / 60f);
                    float acc = Mathf.Clamp((FogDriftSpeed - vr) * (0.8f + 2.2f * deep), 0f, 4f + 6f * deep);
                    if (anchored) anchorPoint = Vector3.MoveTowards(anchorPoint, pos + outward * 3f, FogDriftSpeed * dt);
                    rb.AddForce(outward * (acc * sub01), ForceMode.Acceleration);
                }
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
            int token = ++towToken;
            Fader.FadeThrough(() =>
            {
                if (token != towToken || this == null || rb == null) return;   // brought home in the meantime
                towing = false;
                Vector3 from = rb.position;
                if (FindDeepWater(from, out Vector3 p, out float yaw))
                {
                    PlaceAt(p, yaw);
                    GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr("Festgefahren – freigeschleppt", "Stuck – towed free"), Loc.Tr("Fischer", "Fisherman"));
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
