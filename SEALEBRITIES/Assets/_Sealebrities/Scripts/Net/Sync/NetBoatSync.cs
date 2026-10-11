using UnityEngine;

namespace Sealebrities
{
    /// The shared boat in co-op. Exactly one machine simulates it at a time (the "owner"): the player at the wheel,
    /// or the host while nobody drives. The owner streams pose, velocity and its driver input at 20 Hz (unreliable,
    /// relayed by the host); everybody else sets NetFlags.BoatRemote, turns the rigidbody kinematic and shows the
    /// interpolated pose (same 100 ms timeline as that player's avatar, so the driver sits exactly on the seat), and
    /// hands the driver input to BoatController.SetRemoteControls for wheel, outboard, engine sound and smoke.
    /// The seat: a local player at the wheel claims it (reliable → host decides → NetFlags.BoatDriverId on all);
    /// a refused/late claimer is put back on deck. Leaving the seat or disconnecting releases it.
    /// The anchor: BoatController.AnchorToggledLocally is broadcast; the others apply it with SetAnchoredRemote.
    [DefaultExecutionOrder(20)]   // before the player (50): a carried player moves with this frame's deck
    public sealed class NetBoatSync : MonoBehaviour
    {
        public const float SendInterval = 0.05f;
        const int HostId = 0;

        struct BoatSample
        {
            public Vector3 pos;
            public Quaternion rot;
            public Vector3 vel, angVel;
            public float throttle, steer;
            public bool anchored;    // the owner's anchor state (self-heals drop-in joins, slept mornings, toggle races)
        }

        /// BoatDriver value "stand up": the host ends every seat for a sleep (written directly, never clamped to -1).
        const sbyte StandUp = -2;

        readonly SyncSnapBuffer<BoatSample> buffer = new SyncSnapBuffer<BoatSample>(10);
        readonly SyncWriter writer = new SyncWriter(64);

        IBoat boat;
        Rigidbody rb;
        bool remoteApplied;          // our rigidbody is currently in "remote" (kinematic) mode
        RigidbodyInterpolation savedInterp = RigidbodyInterpolation.Interpolate;
        int streamOwner = -1;        // whose samples are in the buffer
        float sendTimer;
        float kickToastTime = -10f;
        float lastClaimSent = -10f;
        float sleepLockUntil = -10f;           // host: claims are refused while everybody goes to sleep
        float lastAnchorEvent = -10f, nextAnchorCheck;
        bool handlersOn;

        void OnEnable()
        {
            NetBus.Register(NetSyncMsg.BoatClaim, OnClaim);
            NetBus.Register(NetSyncMsg.BoatDriver, OnDriver);
            NetBus.Register(NetSyncMsg.BoatState, OnState);
            NetBus.Register(NetSyncMsg.BoatAnchor, OnAnchor);
            handlersOn = true;
            Net.PlayerJoined += OnPlayerJoined;
            Net.PlayerLeft += OnPlayerLeft;
            Net.SessionEnded += OnSessionEnded;
            BoatController.AnchorToggledLocally += OnLocalAnchor;
            GameEvents.SleepStarted += OnSleepStarted;
        }

        void OnDisable()
        {
            if (handlersOn)
            {
                NetBus.Unregister(NetSyncMsg.BoatClaim);
                NetBus.Unregister(NetSyncMsg.BoatDriver);
                NetBus.Unregister(NetSyncMsg.BoatState);
                NetBus.Unregister(NetSyncMsg.BoatAnchor);
                handlersOn = false;
            }
            Net.PlayerJoined -= OnPlayerJoined;
            Net.PlayerLeft -= OnPlayerLeft;
            Net.SessionEnded -= OnSessionEnded;
            BoatController.AnchorToggledLocally -= OnLocalAnchor;
            GameEvents.SleepStarted -= OnSleepStarted;
            GoOffline();
        }

        // ------------------------------------------------------------------ helpers
        bool ResolveBoat()
        {
            var b = Refs.Boat;
            if (!NetWorldSync.Alive(b) || b.Root == null)
            {
                boat = null;
                rb = null;
                remoteApplied = false;
                return false;
            }
            if (!ReferenceEquals(b, boat))
            {
                boat = b;
                rb = b.Root.GetComponent<Rigidbody>();
                remoteApplied = rb != null && rb.isKinematic;
                buffer.Clear();
                streamOwner = -1;
            }
            return true;
        }

        BoatController Controller => boat as BoatController;

        /// The machine that simulates the boat right now.
        static int OwnerId => NetFlags.BoatDriverId >= 0 ? NetFlags.BoatDriverId : HostId;

        /// The player is in the session (listed, or still streaming its avatar).
        static bool PlayerPresent(int id)
        {
            if (id == Net.LocalId || Net.Find(id) != null) return true;
            return NetAvatarSync.TryGet(id, out var a) && Time.unscaledTime - a.LastStateTime < 5f;
        }

        void OnSessionEnded(string reason) => GoOffline();

        void GoOffline()
        {
            if (remoteApplied && rb != null) RestoreLocal(false);
            remoteApplied = false;
            NetFlags.BoatRemote = false;
            NetFlags.BoatDriverId = -1;
            buffer.Clear();
            streamOwner = -1;
        }

        // ------------------------------------------------------------------ frame
        void Update()
        {
            if (!Net.IsOnline)
            {
                if (remoteApplied || NetFlags.BoatRemote || NetFlags.BoatDriverId >= 0) GoOffline();
                return;
            }
            if (!ResolveBoat()) return;
            bool inGame = NetWorldSync.InGame;

            // ---- the driver seat
            bool localDriving = inGame && boat.PlayerDriving;
            if (localDriving)
            {
                // clients trust the host's verdict; the host itself frees the seat of a player who is gone
                if (NetFlags.SeatTakenByOther && (Net.IsClient || PlayerPresent(NetFlags.BoatDriverId))) Kick();
                else if (NetFlags.BoatDriverId != Net.LocalId) Claim();
            }
            else if (NetFlags.BoatDriverId == Net.LocalId) Release();
            if (Net.IsHost && NetFlags.BoatDriverId >= 0 && !PlayerPresent(NetFlags.BoatDriverId)) SetDriver(-1);

            // ---- ownership
            int owner = OwnerId;
            bool remote = owner != Net.LocalId;
            if (remote && !remoteApplied) EnterRemote();
            else if (!remote && remoteApplied) RestoreLocal(true);
            NetFlags.BoatRemote = remote;

            // ---- stream (owner) or follow (everyone else)
            if (!remote)
            {
                sendTimer -= Time.unscaledDeltaTime;
                if (sendTimer <= 0f)
                {
                    sendTimer = SendInterval;
                    if (inGame) SendState();
                }
            }
            else ApplyRemote(owner);
        }

        // ------------------------------------------------------------------ seat claims
        void Claim()
        {
            if (Net.IsHost)
            {
                if (NetFlags.BoatDriverId < 0 || !PlayerPresent(NetFlags.BoatDriverId)) SetDriver(Net.LocalId);
                else Kick();
                return;
            }
            NetFlags.BoatDriverId = Net.LocalId;   // optimistic: drive at once, the host may still refuse
            if (Time.unscaledTime - lastClaimSent < 0.5f) return;
            lastClaimSent = Time.unscaledTime;
            writer.Reset();
            writer.Byte(1);
            NetBus.Send(NetSyncMsg.BoatClaim, writer.ToArray(), NetBus.Everyone, true);
        }

        void Release()
        {
            if (Net.IsHost) { SetDriver(-1); return; }
            NetFlags.BoatDriverId = -1;
            writer.Reset();
            writer.Byte(0);
            NetBus.Send(NetSyncMsg.BoatClaim, writer.ToArray(), NetBus.Everyone, true);
        }

        /// Someone else holds the wheel: get up again (they won the race for the seat).
        void Kick()
        {
            var bc = Controller;
            if (bc != null && bc.PlayerDriving) bc.ExitHelm();
            if (Time.unscaledTime - kickToastTime > 3f)
            {
                kickToastTime = Time.unscaledTime;
                var d = Net.Find(NetFlags.BoatDriverId);
                string who = d != null && !string.IsNullOrEmpty(d.name) ? d.name : Loc.Tr("Jemand", "Someone");
                GameEvents.RaiseToast(Loc.Tr(who + " steuert schon das Boot", who + " is already steering the boat"));
            }
        }

        /// Host: decides the driver and tells everybody.
        void SetDriver(int id)
        {
            if (!Net.IsHost) return;
            NetFlags.BoatDriverId = id;
            writer.Reset();
            writer.SByte((sbyte)Mathf.Clamp(id, -1, 127));
            NetBus.Send(NetSyncMsg.BoatDriver, writer.ToArray(), NetBus.Everyone, true);
        }

        void OnClaim(int fromId, byte[] payload)
        {
            if (!Net.IsHost || fromId == Net.LocalId) return;
            var r = new SyncReader(payload);
            bool claim = r.Byte() != 0;
            if (r.Failed) return;
            int cur = NetFlags.BoatDriverId;
            if (claim)
            {
                if (Time.unscaledTime < sleepLockUntil)
                {
                    // everybody is going to sleep: refuse explicitly (an ignored optimistic claim would leave the guest
                    // simulating a phantom boat the host never follows)
                    SendStandUp(fromId);
                    return;
                }
                if (cur < 0 || cur == fromId || !PlayerPresent(cur)) SetDriver(fromId);
                else
                {
                    // refused: tell the claimer who is driving (NetBoatSync on its side puts it back on deck)
                    writer.Reset();
                    writer.SByte((sbyte)Mathf.Clamp(cur, -1, 127));
                    NetBus.Send(NetSyncMsg.BoatDriver, writer.ToArray(), fromId, true);
                }
            }
            else if (cur == fromId) SetDriver(-1);
        }

        void OnDriver(int fromId, byte[] payload)
        {
            if (Net.IsHost) return;
            var r = new SyncReader(payload);
            int id = r.SByte();
            if (r.Failed) return;
            if (id == StandUp)
            {
                NetFlags.BoatDriverId = -1;
                var bc = ResolveBoat() ? Controller : null;
                if (bc != null && bc.PlayerDriving) bc.ExitHelm();
                return;
            }
            NetFlags.BoatDriverId = id;
        }

        void SendStandUp(int target)
        {
            writer.Reset();
            writer.SByte(StandUp);
            NetBus.Send(NetSyncMsg.BoatDriver, writer.ToArray(), target, true);
        }

        /// Host: a sleep (own bed or a guest's NetCommand.Sleep) ends every seat BEFORE the fade, so the host owns the
        /// boat again and its slept-morning reset brings it back to the dock while everybody wakes in the hut.
        void OnSleepStarted()
        {
            if (!Net.IsHost || !NetWorldSync.InGame || !ResolveBoat()) return;
            var bc = Controller;
            if (bc != null && bc.PlayerDriving) bc.ExitHelm();
            if (NetFlags.BoatDriverId > 0) SendStandUp(NetBus.Everyone);
            NetFlags.BoatDriverId = -1;
            sleepLockUntil = Time.unscaledTime + 3f;
        }

        void OnPlayerJoined(NetPlayerInfo p)
        {
            if (!Net.IsHost || p == null || p.id == Net.LocalId) return;
            // late joiner: current driver and anchor state
            writer.Reset();
            writer.SByte((sbyte)Mathf.Clamp(NetFlags.BoatDriverId, -1, 127));
            NetBus.Send(NetSyncMsg.BoatDriver, writer.ToArray(), p.id, true);
            if (!ResolveBoat()) return;
            var bc = Controller;
            if (bc != null)
            {
                writer.Reset();
                writer.Bool(bc.Anchored);
                NetBus.Send(NetSyncMsg.BoatAnchor, writer.ToArray(), p.id, true);
            }
        }

        void OnPlayerLeft(NetPlayerInfo p)
        {
            if (p == null) return;
            if (Net.IsHost && NetFlags.BoatDriverId == p.id) SetDriver(-1);
        }

        // ------------------------------------------------------------------ transform stream
        void SendState()
        {
            Transform t = boat.Root;
            var bc = Controller;
            writer.Reset();
            writer.Byte((byte)Mathf.Clamp(Net.LocalId, 0, 255));
            writer.Float(SyncTimelines.Now);
            writer.Vec3(rb != null ? rb.position : t.position);
            writer.Quat(rb != null ? rb.rotation : t.rotation);
            bool dyn = rb != null && !rb.isKinematic;
            writer.Vec3Short(dyn ? rb.linearVelocity : Vector3.zero, 0.01f);
            writer.Vec3Short(dyn ? rb.angularVelocity : Vector3.zero, 0.001f);
            writer.SByte((sbyte)Mathf.RoundToInt(Mathf.Clamp(bc != null ? bc.DriverThrottle : 0f, -1f, 1f) * 127f));
            writer.SByte((sbyte)Mathf.RoundToInt(Mathf.Clamp(bc != null ? bc.DriverSteer : 0f, -1f, 1f) * 127f));
            writer.Bool(bc != null && bc.Anchored);
            NetBus.Send(NetSyncMsg.BoatState, writer.ToArray(), NetBus.Everyone, false);
        }

        void OnState(int fromId, byte[] payload)
        {
            if (payload == null || payload.Length < 2) return;
            if (Net.IsHost && fromId != Net.LocalId) NetBus.SendExcept(NetSyncMsg.BoatState, payload, fromId, false);
            var r = new SyncReader(payload);
            int origin = r.Byte();
            if (Net.IsHost) origin = fromId;
            if (origin == Net.LocalId) return;
            float ts = r.Float();
            BoatSample s;
            s.pos = r.Vec3();
            s.rot = r.Quat();
            s.vel = r.Vec3Short(0.01f);
            s.angVel = r.Vec3Short(0.001f);
            s.throttle = r.SByte() / 127f;
            s.steer = r.SByte() / 127f;
            s.anchored = r.Bool();
            if (r.Failed) return;

            SyncTimelines.Get(origin).Observe(ts);
            if (origin != OwnerId) return;   // a stale stream of the previous owner
            if (origin != streamOwner)
            {
                buffer.Clear();
                streamOwner = origin;
            }
            buffer.Add(ts, s);
        }

        void EnterRemote()
        {
            if (rb != null)
            {
                if (!rb.isKinematic)
                {
                    savedInterp = rb.interpolation;
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    rb.isKinematic = true;
                }
                rb.interpolation = RigidbodyInterpolation.None;
            }
            remoteApplied = true;
            if (streamOwner != OwnerId) { buffer.Clear(); streamOwner = -1; }
        }

        /// Back to local simulation. When we take over from another machine, start from its newest sample
        /// (not the 100 ms old interpolated pose) and keep its velocity, so the hand-over does not jerk.
        void RestoreLocal(bool takeOver)
        {
            if (rb != null)
            {
                bool haveSample = takeOver && buffer.Count > 0;
                if (haveSample)
                {
                    ref BoatSample n = ref buffer.Newest;
                    boat.Root.SetPositionAndRotation(n.pos, n.rot);
                    rb.position = n.pos;
                    rb.rotation = n.rot;
                }
                rb.isKinematic = false;   // the boat is a dynamic body whenever this machine simulates it
                rb.interpolation = savedInterp == RigidbodyInterpolation.None ? RigidbodyInterpolation.Interpolate : savedInterp;
                if (haveSample)
                {
                    ref BoatSample n = ref buffer.Newest;
                    rb.linearVelocity = n.vel;
                    rb.angularVelocity = n.angVel;
                }
            }
            remoteApplied = false;
            buffer.Clear();
            streamOwner = -1;
        }

        void ApplyRemote(int owner)
        {
            if (buffer.Count == 0 || streamOwner != owner) return;   // hold the current pose until the owner streams
            var tl = SyncTimelines.Get(owner);
            float rt = tl.RenderTime(tl.Delay(NetAvatarSync.InterpDelay, 0.25f));   // same delay as the driver avatar
            if (!buffer.Bracket(rt, out int ia, out int ib, out float f, out float over)) return;
            ref BoatSample a = ref buffer.At(ia);
            ref BoatSample b = ref buffer.At(ib);
            Vector3 pos = Vector3.LerpUnclamped(a.pos, b.pos, f);
            Quaternion rot = Quaternion.Slerp(a.rot, b.rot, f);
            if (over > 0f)
            {
                float e = Mathf.Min(over, 0.3f);
                pos += b.vel * e;
                Vector3 w = b.angVel;
                if (w.sqrMagnitude > 1e-6f) rot = Quaternion.AngleAxis(w.magnitude * e * Mathf.Rad2Deg, w.normalized) * rot;
            }
            var bc = Controller;
            if (bc != null)
            {
                bc.ApplyRemotePose(pos, rot);
                if (NetFlags.BoatDriverId >= 0) bc.SetRemoteControls(Mathf.Lerp(a.throttle, b.throttle, f), Mathf.Lerp(a.steer, b.steer, f));
                // self-healing anchor: follow the owner's state (never right after a toggle, which is still travelling)
                float now = Time.unscaledTime;
                if (NetWorldSync.InGame && now >= nextAnchorCheck && now - lastAnchorEvent > 1.5f)
                {
                    nextAnchorCheck = now + 0.5f;
                    bool want = buffer.Newest.anchored;
                    if (bc.Anchored != want || (want && bc.AnchorFar)) bc.SetAnchoredRemote(want, true);
                }
            }
            else
            {
                boat.Root.SetPositionAndRotation(pos, rot);
                if (rb != null && rb.isKinematic)
                {
                    rb.position = pos;
                    rb.rotation = rot;
                }
            }
        }

        // ------------------------------------------------------------------ anchor
        void OnLocalAnchor(bool down)
        {
            if (!Net.IsOnline || !NetWorldSync.InGame) return;
            lastAnchorEvent = Time.unscaledTime;
            writer.Reset();
            writer.Bool(down);
            NetBus.Send(NetSyncMsg.BoatAnchor, writer.ToArray(), NetBus.Everyone, true);
        }

        void OnAnchor(int fromId, byte[] payload)
        {
            if (Net.IsHost && fromId != Net.LocalId) NetBus.SendExcept(NetSyncMsg.BoatAnchor, payload, fromId, true);
            var r = new SyncReader(payload);
            bool down = r.Bool();
            if (r.Failed || !ResolveBoat()) return;
            // during a guest's join fade the boat is still the menu boat: no toast; the stream's anchor bit heals it
            if (!NetWorldSync.InGame) return;
            lastAnchorEvent = Time.unscaledTime;
            var bc = Controller;
            if (bc != null) bc.SetAnchoredRemote(down);
        }
    }
}
