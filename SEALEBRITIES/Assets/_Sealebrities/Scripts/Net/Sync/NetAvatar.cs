using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// A remote co-op player in this world: bean diver model (DiverModels.Build with the player's full look – suit,
    /// cuffs, pattern, hat, face, mask, fins, visible gear, rank badge; low-poly fallback when the Player module does
    /// not provide one), 100 ms interpolated pose, the L.13 motion set (IDiverMotion when the puppet implements it),
    /// the K.4 selfie pose + expression, a carried quest item / salvage piece, the slumped unconscious pose (F.1),
    /// fishing line + float (D.1), hand items, lamp light + beam, exhale bubbles under water and a world-space
    /// nameplate with a speaking icon. While visible it is registered in Divers (fauna, audio) and as an IFaunaDiver
    /// so the host's animals react to it.
    public sealed class NetAvatar : MonoBehaviour, IFaunaDiver
    {
        /// Player colours of the art bible (P1..P4) used when a player has not picked a suit colour.
        static readonly Color[] DefaultSuits =
        {
            new Color(0.933f, 0.498f, 0.176f), new Color(0.247f, 0.722f, 0.769f),
            new Color(0.878f, 0.376f, 0.494f), new Color(0.949f, 0.788f, 0.298f)
        };

        /// Strap items (Q51) follow the hands only after they were stable this long (no model rebuild per hand switch).
        const float StrapSettleSeconds = 1.5f;
        const int LinePoints = 14;

        public int PlayerId { get; private set; }
        public string PlayerName { get; private set; } = "";
        /// Time.unscaledTime of the last received state.
        public float LastStateTime { get; private set; } = -100f;
        public bool Visible { get; private set; }

        // ------------------------------------------------------------------ IFaunaDiver
        public int NetId => PlayerId;
        public Vector3 HeadPosition => head;
        public Vector3 LookForward => lookFwd;
        public Vector3 Velocity => vel;
        public bool Underwater => (flags & NetAvatarSync.FUnderwater) != 0;
        public bool InWater => (flags & NetAvatarSync.FInWater) != 0;
        public bool LightOn => (flags & NetAvatarSync.FLight) != 0;
        public float CalmUntil => calmUntil;

        // ------------------------------------------------------------------ extra state for UI / other features
        public bool Seated => (flags & NetAvatarSync.FSeated) != 0;
        public bool Fainting => (flags & NetAvatarSync.FFainting) != 0;
        public bool PhoneOpen => (flags & NetAvatarSync.FPhone) != 0;
        public bool Paused => (flags & NetAvatarSync.FPaused) != 0;
        public bool Recording => (flags & NetAvatarSync.FRecording) != 0;
        public bool Aiming => (flags & NetAvatarSync.FAiming) != 0;
        public bool OnBoat => (flags & NetAvatarSync.FOnBoat) != 0;
        public bool InSelfie => (flags & NetAvatarSync.FSelfie) != 0;
        public bool Unconscious => (flags & NetAvatarSync.FUnconscious) != 0;
        public bool Towing => (flags & NetAvatarSync.FTowing) != 0;
        public bool Carrying => (flags & NetAvatarSync.FCarry) != 0;
        public bool Fishing => (flags & NetAvatarSync.FFishing) != 0;
        public HoldItem RightItem { get; private set; }
        public HoldItem LeftItem { get; private set; }
        public float Air01 { get; private set; } = 1f;
        public float Depth { get; private set; }
        /// L.13 motion state of the remote player (and the 0..1 phase of its cycle).
        public MotionState Motion { get; private set; }
        public float MotionPhase { get; private set; }
        /// K.4 selfie expression (SelfieExpression as byte).
        public byte Expression { get; private set; }
        /// Carried item id (null = hands free) – K.3 / L.7.
        public string CarriedItem => carryId;

        readonly SyncSnapBuffer<NetAvatarSample> buffer = new SyncSnapBuffer<NetAvatarSample>(12);
        ushort flags;
        Vector3 head, lookFwd = Vector3.forward, vel;
        float calmUntil;
        Vector3 modelOffset;

        // visuals
        GameObject modelGo;
        IDiverPuppet puppet;
        IDiverMotion motionPuppet;
        DiverLook builtLook;
        bool hasLook;
        DiverLook infoLook;            // the look from the player list (straps = "owns")
        bool infoHasLook;
        HoldItem strapRight, strapLeft;
        float strapStable;
        bool ownLamp = true;
        Transform look;          // eye position, yaw + pitch of the remote camera
        Light lamp;
        GameObject beam;
        NetAvatarNameplate plate;
        float bubbleTimer;
        bool wasUnderwater;
        float occlusionTimer;
        float slump;             // 0..1 unconscious tilt for puppets without the motion set

        // carried item
        GameObject carryGo;
        string carryId;

        // fishing line + float
        LineRenderer fishLine;
        Transform bobber;
        readonly Vector3[] linePts = new Vector3[LinePoints];
        Vector3 bobberShown;
        bool bobberHas;
        byte lastRod;

        static Mesh beamMesh;
        static Material beamMat;
        static Texture2D beamTex;
        static Mesh facetSphere;

        // ------------------------------------------------------------------ setup
        public void Init(int id, NetPlayerInfo info)
        {
            PlayerId = id;
            look = new GameObject("Look").transform;
            look.SetParent(transform, false);
            look.localPosition = new Vector3(0f, PlayerController.EyeHeight, 0f);

            // dive lamp (same light as the local player's, left of the eye)
            var lampGo = new GameObject("Lamp");
            lampGo.transform.SetParent(look, false);
            lampGo.transform.localPosition = new Vector3(-0.16f, -0.12f, 0.35f);
            lamp = lampGo.AddComponent<Light>();
            lamp.type = LightType.Spot;
            lamp.spotAngle = 45f;
            lamp.innerSpotAngle = 25f;
            lamp.color = new Color(1f, 0.9f, 0.7f);   // warm yellow (GDD 15.32)
            lamp.intensity = 5f;
            lamp.range = 14f;
            lamp.shadows = LightShadows.None;
            lamp.enabled = false;

            beam = new GameObject("Beam");
            beam.transform.SetParent(lampGo.transform, false);
            beam.transform.localScale = new Vector3(2.4f, 2.4f, 6.5f);
            beam.AddComponent<MeshFilter>().sharedMesh = BeamMesh();
            var br = beam.AddComponent<MeshRenderer>();
            br.sharedMaterial = BeamMaterial();
            br.shadowCastingMode = ShadowCastingMode.Off;
            br.receiveShadows = false;
            beam.SetActive(false);

            plate = NetAvatarNameplate.Create(transform);
            ApplyInfo(info);
            SetVisible(false);
        }

        /// Name and look from the session's player list (rebuilds the model when the look changed).
        public void ApplyInfo(NetPlayerInfo info)
        {
            string name = info != null && !string.IsNullOrWhiteSpace(info.name) ? info.name.Trim() : Loc.Tr("Taucher", "Diver") + " " + (PlayerId + 1);
            if (name != PlayerName)
            {
                PlayerName = name;
                if (plate != null) plate.SetName(name);
            }
            DiverLook lk;
            if (info != null && info.look.lookVersion >= 1) lk = info.look;
            else
            {
                lk = NetLookCodec.FromLegacy(info != null ? info.suit : DefaultSuits[Mathf.Abs(PlayerId) % DefaultSuits.Length],
                                             info != null ? info.hat : 0, info != null ? info.face : 0);
                lk.lampOnStrap = false;
                lk.cameraOnStrap = false;
            }
            lk.suit.a = 1f;
            // nobody picked a colour (factory default orange): use the art-bible player colour of this slot so the
            // crew can be told apart (host stays orange)
            if (Mathf.Abs(lk.suit.r - 1f) < 0.01f && Mathf.Abs(lk.suit.g - 0.55f) < 0.01f && Mathf.Abs(lk.suit.b - 0.2f) < 0.01f)
                lk.suit = DefaultSuits[Mathf.Abs(PlayerId) % DefaultSuits.Length];
            lk.sitting = false;
            infoLook = lk;
            infoHasLook = true;
            var want = WithStraps(lk, strapRight, strapLeft);
            want.sponsor = DiverSponsor.CurrentIndex;
            if (!hasLook || !NetLookCodec.Same(want, builtLook)) BuildModel(want);
        }

        /// Q51: the lamp hangs on the shoulder strap and the camera on the chest strap while they are not in a hand.
        static DiverLook WithStraps(DiverLook l, HoldItem right, HoldItem left)
        {
            bool lampHeld = right == HoldItem.Flashlight || left == HoldItem.Flashlight;
            bool camHeld = right == HoldItem.Camera || left == HoldItem.Camera;
            l.lampOnStrap = l.lampOnStrap && !lampHeld;
            l.cameraOnStrap = l.cameraOnStrap && !camHeld;
            return l;
        }

        void BuildModel(DiverLook lk)
        {
            builtLook = lk;
            hasLook = true;
            if (modelGo != null) Destroy(modelGo);
            modelGo = null;
            puppet = null;
            motionPuppet = null;

            GameObject go = null;
            var build = DiverModels.Build;
            if (build != null)
            {
                try { go = build(lk); }
                catch (Exception e) { Debug.LogWarning("Sealebrities/Net: DiverModels.Build failed: " + e.Message); }
            }
            if (go == null) go = NetAvatarFallback.Build(lk);
            go.name = "Diver";
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            StripForAvatar(go);
            modelGo = go;
            puppet = go.GetComponentInChildren<IDiverPuppet>(true);
            motionPuppet = puppet as IDiverMotion;
            if (motionPuppet == null) motionPuppet = go.GetComponentInChildren<IDiverMotion>(true);
            ownLamp = go.GetComponent<NetAvatarFallback>() != null;   // the fallback has no lamp light of its own
            modelOffset = Vector3.zero;
            if (!Visible) modelGo.SetActive(false);
            if (carryGo != null) AttachCarry();
        }

        /// Remote avatars never collide, listen or render a camera (no physics fights with the interpolated pose).
        static void StripForAvatar(GameObject go)
        {
            var cols = go.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++) Destroy(cols[i]);
            var bodies = go.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < bodies.Length; i++) Destroy(bodies[i]);
            var cams = go.GetComponentsInChildren<Camera>(true);
            for (int i = 0; i < cams.Length; i++) cams[i].enabled = false;
            var listeners = go.GetComponentsInChildren<AudioListener>(true);
            for (int i = 0; i < listeners.Length; i++) listeners[i].enabled = false;
        }

        public void Shutdown()
        {
            if (Visible) SetVisible(false);
            Divers.Remove(transform);
            FaunaContext.UnregisterRemote(transform);
        }

        void OnDestroy()
        {
            Divers.Remove(transform);
            FaunaContext.UnregisterRemote(transform);
        }

        // ------------------------------------------------------------------ lying on a sleep spot (L.9)
        /// Puppet geometry while lying (DiverPuppet: pivot 0.95 m dropped 0.55 m and pitched −86°, head toward −forward):
        /// the eyes end up ~0.67 m behind the root, the back ~0.12 m above it.
        const float LieEyeBack = 0.67f, LieBackAboveRoot = 0.12f, LieSnapRange = 3f;

        /// Nearest PropInfo.SleepSpots entry within LieSnapRange of the sender's root (it pressed E there – no protocol
        /// change needed). Returns the root position / yaw that lays the puppet on it, head at the spot's head end.
        static bool SnapToSleepSpot(Vector3 root, out Vector3 pos, out float yaw)
        {
            pos = root;
            yaw = 0f;
            var spots = PropInfo.SleepSpots;
            int best = -1;
            float bestD = LieSnapRange * LieSnapRange;
            for (int i = 0; i < spots.Count; i++)
            {
                Vector3 d = spots[i].lie - root;
                d.y *= 0.5f;
                float dd = d.sqrMagnitude;
                if (dd < bestD) { bestD = dd; best = i; }
            }
            if (best < 0) return false;
            var s = spots[best];
            yaw = s.yaw;
            Vector3 fwd = Quaternion.Euler(0f, s.yaw, 0f) * Vector3.forward;
            // the spot's `lie` is the first-person eye just above the cloth / mattress
            float surface = s.lie.y - (s.kind == SleepSpotInfo.Hammock ? 0.27f : 0.09f);
            pos = new Vector3(s.lie.x, surface - LieBackAboveRoot, s.lie.z) + fwd * LieEyeBack;
            return true;
        }

        // ------------------------------------------------------------------ network input
        public void Push(float ts, in NetAvatarSample s)
        {
            LastStateTime = Time.unscaledTime;
            buffer.Add(ts, s);
        }

        // ------------------------------------------------------------------ per frame
        public void Tick(float dt, Camera cam)
        {
            bool show = NetWorldSync.InGame && !UIState.MainMenuOpen && buffer.Count > 0
                        && Time.unscaledTime - LastStateTime < 6f;
            if (show != Visible) SetVisible(show);
            if (!show) return;

            // ---- interpolated pose (100 ms behind the sender)
            var tl = SyncTimelines.Get(PlayerId);
            float rt = tl.RenderTime(tl.Delay(NetAvatarSync.InterpDelay, 0.25f));   // same delay as the boat stream
            if (!buffer.Bracket(rt, out int ia, out int ib, out float f, out float over)) return;
            ref NetAvatarSample a = ref buffer.At(ia);
            ref NetAvatarSample b = ref buffer.At(ib);
            bool aBoat = (a.flags & NetAvatarSync.FOnBoat) != 0, bBoat = (b.flags & NetAvatarSync.FOnBoat) != 0;
            // boarding / leaving the boat (coordinate frame changes) or a teleport (rescue, sleep): no gliding
            if (aBoat != bBoat || (a.pos - b.pos).sqrMagnitude > 100f) { f = f < 0.5f ? 0f : 1f; }

            Vector3 pos = Vector3.LerpUnclamped(a.pos, b.pos, f);
            float yaw = Mathf.LerpAngle(a.yaw, b.yaw, f);
            float pitch = Mathf.Lerp(a.pitch, b.pitch, f);
            float eyeY = Mathf.Lerp(a.eyeY, b.eyeY, f);
            Vector3 v = Vector3.Lerp(a.vel, b.vel, f);
            ref NetAvatarSample d = ref (f < 0.5f ? ref a : ref b);   // discrete values from the nearer sample
            flags = d.flags;
            RightItem = d.right;
            LeftItem = d.left;
            Air01 = d.air01;
            Depth = d.depth;
            Motion = d.motion;
            Expression = d.expression;
            MotionPhase = a.motion == b.motion ? LerpPhase(a.phase, b.phase, f) : d.phase;
            bool onBoat = (flags & NetAvatarSync.FOnBoat) != 0;

            if (onBoat)
            {
                var boat = Refs.Boat;
                if (NetWorldSync.Alive(boat) && boat.Root != null)
                {
                    Transform bt = boat.Root;
                    pos = bt.TransformPoint(pos);
                    yaw += bt.eulerAngles.y;
                }
            }
            else if (over > 0f)
            {
                pos += v * Mathf.Min(over, 0.25f);   // short extrapolation when a packet is late
            }
            // lying on a co-op sleep spot (L.9): the sender's root stays where it pressed E (only its camera lies down) –
            // snap the puppet onto the nearest bed / hammock / bunk so it rests on the mattress instead of the floor
            if (Motion == MotionState.Lie && !onBoat && SnapToSleepSpot(pos, out Vector3 liePos, out float lieYaw))
            {
                pos = liePos;
                yaw = lieYaw;
            }

            transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            vel = v;
            head = pos + Vector3.up * eyeY;
            look.localPosition = new Vector3(0f, eyeY, 0f);
            look.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            lookFwd = look.forward;

            // ---- calm period for the host's predators after this diver fainted / while unconscious
            if (Fainting || Unconscious) calmUntil = Time.time + 30f;

            // ---- Q51 straps follow the hands once they are stable (no rebuild for a quick switch)
            UpdateStraps(dt);

            // ---- character pose
            bool underwater = Underwater;
            bool seated = Seated;
            bool swimming = InWater && !seated;
            float speed = swimming ? v.magnitude : new Vector2(v.x, v.z).magnitude;
            if (onBoat) speed = 0f;
            float talk = NetVoice.Loudness(PlayerId);
            bool unconscious = Unconscious;
            if (NetWorldSync.Alive(puppet))
            {
                try
                {
                    if (NetWorldSync.Alive(motionPuppet))
                    {
                        motionPuppet.SetMotion(Motion, speed, MotionPhase, RightItem, LeftItem, LightOn);
                        motionPuppet.SetExpression(Expression);
                    }
                    else puppet.SetPose(swimming && !unconscious, seated, unconscious ? 0f : speed, RightItem, LeftItem, LightOn);
                    puppet.SetTalking(unconscious ? 0f : talk);
                    AlignHead(swimming, dt);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("Sealebrities/Net: avatar puppet threw: " + e.Message);
                    puppet = null;
                    motionPuppet = null;
                }
            }
            UpdateSlump(unconscious, dt);

            // ---- lamp + visible beam (under water or at night); the Player module's puppet brings its own lamp light
            bool lightOn = LightOn && ownLamp && !unconscious;
            if (lamp.enabled != lightOn) lamp.enabled = lightOn;
            bool showBeam = lightOn && (underwater || EnvState.Night01 > 0.25f) && Settings.Data.quality != GraphicsQuality.Low;
            if (beam.activeSelf != showBeam) beam.SetActive(showBeam);

            // ---- exhale bubbles from the regulator (an unconscious diver's bubbles stop – F.1)
            if (underwater && !unconscious)
            {
                if (!wasUnderwater) bubbleTimer = 0.4f;
                bubbleTimer -= dt;
                if (bubbleTimer <= 0f)
                {
                    bubbleTimer = UnityEngine.Random.Range(2.6f, 3.8f) * (speed > 1.5f ? 0.6f : 1f);
                    FaunaBubbles.Poof(head + lookFwd * 0.18f + Vector3.up * 0.05f, 0.05f, UnityEngine.Random.Range(6, 11));
                }
            }
            wasUnderwater = underwater;

            // ---- carried item + fishing line
            UpdateCarry();
            UpdateFishing(ref d, dt);

            // ---- nameplate (billboard above the head, hidden beyond visibility / when occluded)
            if (plate != null)
            {
                plate.SetStatus(unconscious ? Loc.Tr("bewusstlos", "passed out") : null);
                Vector3 anchor = head + Vector3.up * 0.53f;
                bool camUnder = false;
                var lp = Refs.Player;
                if (NetWorldSync.Alive(lp)) camUnder = lp.IsUnderwater;
                float range = 60f;
                if (camUnder || underwater)
                {
                    float depth = Mathf.Max(Depth, NetWorldSync.Alive(lp) ? lp.Depth : 0f);
                    range = UnderwaterVisibility(depth);
                }
                occlusionTimer -= dt;
                bool checkOcclusion = occlusionTimer <= 0f;
                if (checkOcclusion) occlusionTimer = 0.25f;
                plate.Tick(cam, anchor, range, NetVoice.IsSpeaking(PlayerId), checkOcclusion);
            }
        }

        static float LerpPhase(float a, float b, float f)
        {
            float d = b - a;
            if (d > 0.5f) d -= 1f;
            else if (d < -0.5f) d += 1f;
            return Mathf.Repeat(a + d * f, 1f);
        }

        float sponsorPoll;

        void UpdateStraps(float dt)
        {
            if (!infoHasLook) return;
            // the channel's sponsor patch follows the shared deals (checked every 2 s)
            sponsorPoll -= dt;
            if (sponsorPoll <= 0f)
            {
                sponsorPoll = 2f;
                int sp = DiverSponsor.CurrentIndex;
                if (hasLook && sp != builtLook.sponsor)
                {
                    var w = builtLook;
                    w.sponsor = sp;
                    BuildModel(w);
                    return;
                }
            }
            if (RightItem != strapRight || LeftItem != strapLeft)
            {
                strapRight = RightItem;
                strapLeft = LeftItem;
                strapStable = 0f;
                return;
            }
            if (strapStable >= StrapSettleSeconds) return;
            strapStable += dt;
            if (strapStable < StrapSettleSeconds) return;
            var want = WithStraps(infoLook, strapRight, strapLeft);
            want.sponsor = DiverSponsor.CurrentIndex;
            if (!NetLookCodec.Same(want, builtLook)) BuildModel(want);
        }

        /// F.1 slumped pose for puppets without the motion set: the body rolls onto its side and drifts.
        void UpdateSlump(bool unconscious, float dt)
        {
            if (modelGo == null || NetWorldSync.Alive(motionPuppet)) return;
            float target = unconscious ? 1f : 0f;
            if (Mathf.Approximately(slump, target) && target == 0f) return;
            slump = Mathf.MoveTowards(slump, target, dt * (unconscious ? 0.8f : 2f));
            float sway = Mathf.Sin(Time.time * 0.7f + PlayerId) * 6f * slump;
            modelGo.transform.localRotation = Quaternion.Euler(18f * slump + sway, 0f, 72f * slump);
        }

        /// In the horizontal swim pose a puppet that pivots around its belly moves its head away from where the remote
        /// camera really is. Shift the model so its head sits at the camera (what the friend sees is where they look
        /// from); standing / seated poses stay on the feet.
        void AlignHead(bool swimming, float dt)
        {
            if (modelGo == null) return;
            Transform h = puppet.Head;
            Vector3 target = Vector3.zero;
            if (swimming && h != null)
            {
                target = modelOffset + (head - h.position);
                if (target.sqrMagnitude > 1.44f) target = target.normalized * 1.2f;
            }
            modelOffset = Vector3.Lerp(modelOffset, target, 1f - Mathf.Exp(-6f * dt));
            modelGo.transform.position = transform.position + modelOffset;
        }

        void SetVisible(bool v)
        {
            Visible = v;
            if (modelGo != null && modelGo.activeSelf != v) modelGo.SetActive(v);
            if (plate != null) plate.SetVisible(v);
            if (!v)
            {
                if (lamp != null) lamp.enabled = false;
                if (beam != null && beam.activeSelf) beam.SetActive(false);
                if (carryGo != null && carryGo.activeSelf) carryGo.SetActive(false);
                HideFishing();
                Divers.Remove(transform);
                FaunaContext.UnregisterRemote(transform);
            }
            else
            {
                calmUntil = Mathf.Max(calmUntil, Time.time + 5f);   // a diver that just appeared is not attacked at once
                FaunaContext.RegisterRemote(transform, this);
                Divers.Add(transform);
            }
        }

        // ------------------------------------------------------------------ carried item (K.3, L.7)
        void UpdateCarry()
        {
            string want = NetCrew.ItemOf(PlayerId);
            if (string.IsNullOrEmpty(want) && Carrying) want = carryId ?? "item";   // the flag arrived before CarryState
            if (want == carryId)
            {
                if (carryGo != null && !carryGo.activeSelf) carryGo.SetActive(true);
                return;
            }
            carryId = want;
            if (carryGo != null) Destroy(carryGo);
            carryGo = null;
            if (string.IsNullOrEmpty(want)) return;
            carryGo = BuildCarryProp(want);
            AttachCarry();
        }

        void AttachCarry()
        {
            if (carryGo == null) return;
            // in front of the chest, following the look pitch a little (both hands hold it – the puppet's carry pose)
            carryGo.transform.SetParent(look, false);
            bool heavy = carryId != null && carryId.StartsWith("salv:", StringComparison.Ordinal);
            carryGo.transform.localPosition = heavy ? new Vector3(0f, -0.62f, 0.5f) : new Vector3(0.05f, -0.5f, 0.42f);
            carryGo.transform.localRotation = Quaternion.identity;
        }

        /// Simple How-to-Fish style stand-ins (the Player module's own carry models are first-person only):
        /// heavy salvage = a banded sea chest, coconuts = a faceted brown nut, finds = a small sandy box, quest items =
        /// a woven crate with a yellow band.
        static GameObject BuildCarryProp(string id)
        {
            var root = new GameObject("Carried");
            string lower = id.ToLowerInvariant();
            if (lower.StartsWith("salv:", StringComparison.Ordinal))
            {
                Box(root.transform, Vector3.zero, new Vector3(0.62f, 0.4f, 0.42f), LowPoly.Wood);
                Box(root.transform, new Vector3(0f, 0.21f, 0f), new Vector3(0.64f, 0.06f, 0.44f), LowPoly.WoodDark);
                Box(root.transform, new Vector3(-0.2f, 0f, 0f), new Vector3(0.05f, 0.42f, 0.44f), LowPoly.CoralYellow);
                Box(root.transform, new Vector3(0.2f, 0f, 0f), new Vector3(0.05f, 0.42f, 0.44f), LowPoly.CoralYellow);
            }
            else if (lower.Contains("coconut") || lower.Contains("kokos"))
            {
                var nut = new GameObject("Nut");
                nut.transform.SetParent(root.transform, false);
                nut.transform.localScale = Vector3.one * 0.2f;
                nut.AddComponent<MeshFilter>().sharedMesh = FacetSphere();
                var mr = nut.AddComponent<MeshRenderer>();
                mr.sharedMaterial = LowPoly.Mat(new Color(0.42f, 0.27f, 0.21f));
                mr.shadowCastingMode = ShadowCastingMode.On;
            }
            else if (lower.StartsWith("find:", StringComparison.Ordinal))
            {
                Box(root.transform, Vector3.zero, new Vector3(0.16f, 0.1f, 0.12f), LowPoly.WetSand);
            }
            else
            {
                Box(root.transform, Vector3.zero, new Vector3(0.3f, 0.24f, 0.24f), LowPoly.WoodLight);
                Box(root.transform, new Vector3(0f, 0.04f, 0f), new Vector3(0.31f, 0.05f, 0.25f), LowPoly.CoralYellow);
            }
            return root;
        }

        static void Box(Transform parent, Vector3 pos, Vector3 scale, Color c)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = LowPoly.Mat(c);
            r.shadowCastingMode = ShadowCastingMode.On;
        }

        static Mesh FacetSphere()
        {
            if (facetSphere != null) return facetSphere;
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var src = tmp.GetComponent<MeshFilter>().sharedMesh;
            facetSphere = LowPoly.Flatten(src);
            facetSphere.name = "SB_NetFacetSphere";
            Destroy(tmp);
            return facetSphere;
        }

        // ------------------------------------------------------------------ fishing line + float (D.1)
        void UpdateFishing(ref NetAvatarSample s, float dt)
        {
            bool fishing = Fishing;
            bool hasBobber = fishing && (flags & NetAvatarSync.FBobber) != 0 && s.bobber != Vector3.zero;
            if (!hasBobber)
            {
                HideFishing();
                return;
            }
            EnsureFishingObjects();
            var st = (FishingRodTool.RodState)s.rod;
            Vector3 target = s.bobber;
            // bite: the float is pulled under; reeling: it skims toward the diver (the stream brings the positions)
            if (st == FishingRodTool.RodState.Bite) target.y -= 0.12f;
            if (!bobberHas || (bobberShown - target).sqrMagnitude > 64f) bobberShown = target;
            else bobberShown = Vector3.Lerp(bobberShown, target, 1f - Mathf.Exp(-10f * dt));
            if (lastRod != s.rod && st == FishingRodTool.RodState.Bite) FaunaBubbles.Poof(bobberShown, 0.12f, 8);
            lastRod = s.rod;
            bobberHas = true;
            if (!bobber.gameObject.activeSelf) bobber.gameObject.SetActive(true);
            bobber.position = bobberShown;

            // rod tip: raised in front of the right shoulder
            Vector3 flatFwd = transform.forward;
            Vector3 right = transform.right;
            Vector3 tip = transform.position + Vector3.up * 2.05f + flatFwd * 1.35f + right * 0.32f;
            float tension = s.tension;
            float sag = Mathf.Lerp(0.55f, 0.02f, Mathf.Clamp01(tension * 1.6f)) * Mathf.Clamp01(Vector3.Distance(tip, bobberShown) / 8f);
            for (int i = 0; i < LinePoints; i++)
            {
                float t = i / (float)(LinePoints - 1);
                Vector3 p = Vector3.Lerp(tip, bobberShown, t);
                p.y -= Mathf.Sin(t * Mathf.PI) * sag;
                linePts[i] = p;
            }
            if (!fishLine.enabled) fishLine.enabled = true;
            fishLine.SetPositions(linePts);
        }

        void EnsureFishingObjects()
        {
            if (fishLine == null)
            {
                var lg = new GameObject("FishingLine");
                lg.transform.SetParent(transform, false);
                fishLine = lg.AddComponent<LineRenderer>();
                fishLine.useWorldSpace = true;
                fishLine.positionCount = LinePoints;
                fishLine.widthMultiplier = 0.008f;
                fishLine.numCapVertices = 0;
                fishLine.sharedMaterial = BoatMats.Line;
                fishLine.shadowCastingMode = ShadowCastingMode.Off;
                fishLine.receiveShadows = false;
                fishLine.enabled = false;
            }
            if (bobber == null)
            {
                bobber = FishingVisuals.BuildBobber(transform);
                var cols = bobber.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < cols.Length; i++) Destroy(cols[i]);
                bobber.gameObject.SetActive(false);
            }
        }

        void HideFishing()
        {
            bobberHas = false;
            if (fishLine != null && fishLine.enabled) fishLine.enabled = false;
            if (bobber != null && bobber.gameObject.activeSelf) bobber.gameObject.SetActive(false);
        }

        /// Underwater sight distance by depth (art bible §2.2: 19 m in the shallows → 11 m at ≥ 80 m).
        public static float UnderwaterVisibility(float depth)
        {
            if (depth <= 8f) return 19.4f;
            if (depth <= 15f) return Mathf.Lerp(19.4f, 17.8f, (depth - 8f) / 7f);
            if (depth <= 25f) return Mathf.Lerp(17.8f, 16f, (depth - 15f) / 10f);
            if (depth <= 40f) return Mathf.Lerp(16f, 14.4f, (depth - 25f) / 15f);
            if (depth <= 60f) return Mathf.Lerp(14.4f, 12.6f, (depth - 40f) / 20f);
            if (depth <= 80f) return Mathf.Lerp(12.6f, 10.8f, (depth - 60f) / 20f);
            return 10.8f;
        }

        // ------------------------------------------------------------------ shared beam assets
        static Mesh BeamMesh()
        {
            if (beamMesh != null) return beamMesh;
            // open cone along +Z: apex at the lamp, unit radius at z = 1 (scaled per avatar); v = 0 apex .. 1 end
            const int Seg = 14;
            var verts = new Vector3[Seg * 2];
            var uvs = new Vector2[Seg * 2];
            var tris = new int[Seg * 6];
            for (int i = 0; i < Seg; i++)
            {
                float a = i / (float)Seg * Mathf.PI * 2f;
                float u = i / (float)Seg;
                verts[i] = new Vector3(Mathf.Cos(a) * 0.04f, Mathf.Sin(a) * 0.04f, 0f);
                verts[Seg + i] = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 1f);
                uvs[i] = new Vector2(u, 0.02f);
                uvs[Seg + i] = new Vector2(u, 0.98f);
                int n = (i + 1) % Seg;
                int t = i * 6;
                tris[t] = i; tris[t + 1] = Seg + i; tris[t + 2] = Seg + n;
                tris[t + 3] = i; tris[t + 4] = Seg + n; tris[t + 5] = n;
            }
            beamMesh = new Mesh { name = "SB_NetLampBeam", vertices = verts, uv = uvs, triangles = tris };
            beamMesh.RecalculateNormals();
            beamMesh.RecalculateBounds();
            return beamMesh;
        }

        static Material BeamMaterial()
        {
            if (beamMat != null) return beamMat;
            beamTex = new Texture2D(4, 32, TextureFormat.RGBA32, false) { name = "SB_NetLampBeamRamp", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[4 * 32];
            for (int y = 0; y < 32; y++)
            {
                float t = y / 31f;
                byte alpha = (byte)Mathf.RoundToInt(255f * Mathf.Pow(1f - t, 1.6f) * Mathf.Clamp01(t * 6f));
                for (int x = 0; x < 4; x++) px[y * 4 + x] = new Color32(255, 255, 255, alpha);
            }
            beamTex.SetPixels32(px);
            beamTex.Apply(false, true);
            // own (uncached) material: MaterialLibrary.Unlit is shared by colour and must not get our texture
            beamMat = new Material(MaterialLibrary.UnlitShader) { name = "SB_NetLampBeam" };
            beamMat.SetColor("_BaseColor", new Color(1f, 0.92f, 0.72f, 0.09f));
            beamMat.SetTexture("_BaseMap", beamTex);
            beamMat.SetTexture("_MainTex", beamTex);
            MaterialLibrary.MakeTransparent(beamMat, true);
            return beamMat;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { beamMesh = null; beamMat = null; beamTex = null; facetSphere = null; }
    }
}
