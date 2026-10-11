using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// One decoded avatar state (15–20 Hz stream of a remote player).
    public struct NetAvatarSample
    {
        public Vector3 pos;      // feet position: world, or boat-local while OnBoat
        public float yaw;        // look yaw: world, or relative to the boat's yaw while OnBoat
        public float pitch;      // look pitch in degrees (+ = looking down)
        public Vector3 vel;      // world velocity m/s
        public float eyeY;       // camera height above the feet
        public ushort flags;
        public HoldItem right, left;
        public float air01, depth;
        // ---- round 2 (L.13 motion set, K.4 selfie, D.1 fishing)
        public MotionState motion;
        public float phase;      // 0..1 of the current motion cycle
        public byte expression;  // SelfieExpression
        public byte rod;         // FishingRodTool.RodState (FFishing)
        public float tension;    // reel tension 0..1 (FFishing)
        public Vector3 bobber;   // world position of the float (FBobber)
    }

    /// Co-op avatars: streams the local player's state (15 Hz, 20 Hz when fast; unreliable; the host relays it to the
    /// other clients) and keeps one NetAvatar per remote player (spawned on Net.PlayerJoined or the first state,
    /// removed on Net.PlayerLeft / session end). Also routes predator bumps from the host's fauna to the bumped client.
    /// Round 2: the state also carries the L.13 motion state + cycle phase, the K.4 selfie expression (and the body
    /// faces the selfie camera), carry / unconscious / tow / lying flags and – while the rod is out – the fishing rod
    /// state, reel tension and float position (D.1 "fishing visuals of others"). 33–42 bytes per state.
    [DefaultExecutionOrder(200)]   // LateUpdate after the player camera moved (billboards, light cones)
    public sealed class NetAvatarSync : MonoBehaviour
    {
        // state flags
        public const ushort FUnderwater = 1, FInWater = 2, FSeated = 4, FFainting = 8, FPhone = 16, FLight = 32,
                            FRecording = 64, FAiming = 128, FOnBoat = 256, FPaused = 512,
                            // round 2
                            FSelfie = 1024, FUnconscious = 2048, FCarry = 4096, FTowing = 8192, FFishing = 16384, FBobber = 32768;

        public const float SendInterval = 1f / 15f;
        public const float FastSendInterval = 1f / 20f;
        /// Interpolation delay of remote avatars / boat (seconds behind the sender).
        public const float InterpDelay = 0.1f;

        public static NetAvatarSync Instance { get; private set; }

        readonly Dictionary<int, NetAvatar> avatars = new Dictionary<int, NetAvatar>();
        readonly List<NetAvatar> list = new List<NetAvatar>(4);
        readonly List<int> removeIds = new List<int>(4);
        readonly SyncWriter writer = new SyncWriter(64);
        float sendTimer, reconcileTimer;
        Transform avatarRoot;
        bool handlersOn;

        /// Remote avatars currently known (visible or not).
        public IReadOnlyList<NetAvatar> All => list;

        public static bool TryGet(int playerId, out NetAvatar avatar)
        {
            avatar = null;
            var s = Instance;
            return s != null && s.avatars.TryGetValue(playerId, out avatar) && avatar != null;
        }

        void Awake()
        {
            Instance = this;
            var r = new GameObject("NetAvatars");
            r.transform.SetParent(transform, false);
            avatarRoot = r.transform;
        }

        void OnEnable()
        {
            NetBus.Register(NetSyncMsg.AvatarState, OnState);
            NetBus.Register(NetSyncMsg.AvatarBump, OnBump);
            handlersOn = true;
            Net.PlayerJoined += OnPlayerJoined;
            Net.PlayerLeft += OnPlayerLeft;
            Net.SessionEnded += OnSessionEnded;
            NetSession.PlayersChanged += OnPlayersChanged;
            FaunaContext.RemoteBump = SendBump;
        }

        /// A name or look changed in the player list: refresh the avatars on the next frame (not once a second).
        void OnPlayersChanged() => reconcileTimer = 0f;

        void OnDisable()
        {
            if (handlersOn)
            {
                NetBus.Unregister(NetSyncMsg.AvatarState);
                NetBus.Unregister(NetSyncMsg.AvatarBump);
                handlersOn = false;
            }
            Net.PlayerJoined -= OnPlayerJoined;
            Net.PlayerLeft -= OnPlayerLeft;
            Net.SessionEnded -= OnSessionEnded;
            NetSession.PlayersChanged -= OnPlayersChanged;
            if (FaunaContext.RemoteBump == (Action<int, float>)SendBump) FaunaContext.RemoteBump = null;
        }

        void OnDestroy()
        {
            RemoveAll();
            if (Instance == this) Instance = null;
        }

        // ------------------------------------------------------------------ session events
        void OnPlayerJoined(NetPlayerInfo p)
        {
            if (p == null || p.id == Net.LocalId) return;
            Ensure(p.id);
        }

        void OnPlayerLeft(NetPlayerInfo p)
        {
            if (p == null) return;
            Remove(p.id);
            SyncTimelines.Forget(p.id);
        }

        void OnSessionEnded(string reason)
        {
            RemoveAll();
            SyncTimelines.Clear();
        }

        // ------------------------------------------------------------------ frame
        void Update()
        {
            // contract safety net: the local diver belongs in Divers.All (normally added by the Player module)
            var lp = Refs.Player;
            if (NetWorldSync.Alive(lp) && lp.Root != null && !Divers.All.Contains(lp.Root)) Divers.Add(lp.Root);

            if (!Net.IsOnline)
            {
                if (list.Count > 0) RemoveAll();
                return;
            }

            reconcileTimer -= Time.unscaledDeltaTime;
            if (reconcileTimer <= 0f)
            {
                reconcileTimer = 1f;
                Reconcile();
            }

            sendTimer -= Time.unscaledDeltaTime;
            if (sendTimer <= 0f)
            {
                var p = Refs.Player;
                bool fast = NetWorldSync.Alive(p) && p.Velocity.sqrMagnitude > 16f;
                sendTimer = fast ? FastSendInterval : SendInterval;
                if (NetWorldSync.InGame) SendLocal();
            }
        }

        void LateUpdate()
        {
            if (list.Count == 0) return;
            float dt = Time.deltaTime;
            Camera cam = null;
            var p = Refs.Player;
            if (NetWorldSync.Alive(p)) cam = p.View;
            if (cam == null) cam = Camera.main;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var a = list[i];
                if (a == null) { list.RemoveAt(i); continue; }
                a.Tick(dt, cam);
            }
        }

        /// Creates avatars for listed players we don't have yet, drops avatars of players that are gone,
        /// refreshes names and looks.
        void Reconcile()
        {
            var players = Net.Players;
            for (int i = 0; i < players.Count; i++)
            {
                var info = players[i];
                if (info == null || info.id == Net.LocalId) continue;
                var a = Ensure(info.id);
                if (a != null) a.ApplyInfo(info);
            }
            if (players.Count == 0) return;   // the player list may not be filled yet: keep avatars fed by states
            removeIds.Clear();
            for (int i = 0; i < list.Count; i++)
            {
                var a = list[i];
                if (a == null) continue;
                if (Net.Find(a.PlayerId) == null && Time.unscaledTime - a.LastStateTime > 3f) removeIds.Add(a.PlayerId);
            }
            for (int i = 0; i < removeIds.Count; i++) Remove(removeIds[i]);
        }

        NetAvatar Ensure(int id)
        {
            if (id == Net.LocalId || id < 0) return null;
            if (avatars.TryGetValue(id, out var a) && a != null) return a;
            var go = new GameObject("NetAvatar_" + id);
            go.transform.SetParent(avatarRoot, false);
            a = go.AddComponent<NetAvatar>();
            a.Init(id, Net.Find(id));
            avatars[id] = a;
            list.Add(a);
            return a;
        }

        void Remove(int id)
        {
            if (!avatars.TryGetValue(id, out var a)) return;
            avatars.Remove(id);
            list.Remove(a);
            if (a != null)
            {
                a.Shutdown();
                Destroy(a.gameObject);
            }
        }

        void RemoveAll()
        {
            for (int i = 0; i < list.Count; i++)
            {
                var a = list[i];
                if (a == null) continue;
                a.Shutdown();
                Destroy(a.gameObject);
            }
            list.Clear();
            avatars.Clear();
        }

        // ------------------------------------------------------------------ local → network
        void SendLocal()
        {
            var p = Refs.Player;
            if (!NetWorldSync.Alive(p)) return;
            Transform root = p.Root;
            if (root == null) return;
            Camera cam = p.View;
            Transform ct = cam != null ? cam.transform : root;

            Vector3 pos = root.position;
            Vector3 fwd = ct.forward;
            float yaw = ct.eulerAngles.y;
            float pitch = -Mathf.Asin(Mathf.Clamp(fwd.y, -1f, 1f)) * Mathf.Rad2Deg;
            float eyeY = cam != null ? Mathf.Clamp(ct.position.y - pos.y, 0f, 2.5f) : PlayerController.EyeHeight;

            ushort flags = 0;
            if (p.IsUnderwater) flags |= FUnderwater;
            if (p.IsInWater) flags |= FInWater;
            if (p.IsSeated) flags |= FSeated;
            if (p.IsFainting) flags |= FFainting;
            if (UIState.PhoneOpen) flags |= FPhone;
            if (UIState.PauseOpen) flags |= FPaused;
            if (p.FlashlightOn) flags |= FLight;
            var hc = Refs.HandCamera;
            if (NetWorldSync.Alive(hc))
            {
                if (hc.IsRecording) flags |= FRecording;
                if (hc.IsAiming) flags |= FAiming;
            }

            // ---- round 2: motion set, selfie, carry, rescue, fishing
            NetLocalAvatar.Resolve(p, out MotionState motion, out float phase);
            if (SelfieState.Active)
            {
                flags |= FSelfie;
                // K.4: the selfie camera looks back at the diver – everybody else sees the diver face that camera
                if (NetLocalAvatar.SelfieFacing(p, out float sy, out float sp))
                {
                    yaw = sy;
                    pitch = sp;
                    eyeY = PlayerController.EyeHeight;
                }
            }
            if (NetRescue.LocalUnconscious) flags |= FUnconscious;
            if (NetRescue.LocalTowTarget >= 0) flags |= FTowing;
            bool carrying = Carry.IsCarrying;
            if (carrying) flags |= FCarry;
            bool fishing = NetLocalAvatar.Fishing(out byte rod, out Vector3 bobber, out bool bobberValid, out float tension);
            if (fishing) flags |= FFishing;
            if (fishing && bobberValid) flags |= FBobber;
            HoldItem right = Hands.Active(Hand.Right), left = Hands.Active(Hand.Left);
            if (carrying)
            {
                // K.3 / L.7: a carried item fills both hands (the camera hangs on its chest strap)
                right = HoldItem.QuestItem;
                left = HoldItem.None;
            }
            else if (SelfieState.Active && right == HoldItem.None) right = HoldItem.Camera;

            // on the boat: send boat-local coordinates, so the avatar stays glued to the deck on every machine
            var boat = Refs.Boat;
            if (NetWorldSync.Alive(boat) && boat.Root != null && (boat.PlayerAboard || boat.PlayerDriving))
            {
                Transform bt = boat.Root;
                pos = bt.InverseTransformPoint(pos);
                yaw = Mathf.DeltaAngle(bt.eulerAngles.y, yaw);
                flags |= FOnBoat;
            }

            writer.Reset();
            writer.Byte((byte)Mathf.Clamp(Net.LocalId, 0, 255));
            writer.Float(SyncTimelines.Now);
            writer.UShort(flags);
            writer.Vec3(pos);
            writer.UShort(SyncQuant.YawToUShort(yaw));
            writer.SByte((sbyte)Mathf.Clamp(Mathf.RoundToInt(pitch / 90f * 127f), -127, 127));
            writer.Vec3Short(p.Velocity, 0.01f);
            writer.Byte((byte)Mathf.Clamp(Mathf.RoundToInt(eyeY * 100f), 0, 255));
            writer.Byte((byte)right);
            writer.Byte((byte)left);
            writer.Byte(SyncQuant.To01(p.Air01));
            writer.Byte((byte)Mathf.Clamp(Mathf.RoundToInt(p.Depth), 0, 255));
            // round 2 tail: motion state, cycle phase, selfie expression [, rod state, tension [, float position]]
            writer.Byte((byte)motion);
            writer.Byte(SyncQuant.To01(phase));
            writer.Byte((byte)Mathf.Clamp(SelfieState.Expression, 0, 255));
            if (fishing)
            {
                writer.Byte(rod);
                writer.Byte(SyncQuant.To01(tension));
                if (bobberValid) SyncQuant.WritePos(writer, bobber);
            }
            NetBus.Send(NetSyncMsg.AvatarState, writer.ToArray(), NetBus.Everyone, false);
        }

        // ------------------------------------------------------------------ network → avatars
        void OnState(int fromId, byte[] payload)
        {
            if (payload == null || payload.Length < 2) return;
            // the host relays a client's stream to everybody else (the origin id is inside the payload)
            if (Net.IsHost && fromId != Net.LocalId) NetBus.SendExcept(NetSyncMsg.AvatarState, payload, fromId, false);

            var r = new SyncReader(payload);
            int origin = r.Byte();
            if (Net.IsHost) origin = fromId;   // the host trusts its transport over the payload
            if (origin == Net.LocalId) return;

            float ts = r.Float();
            NetAvatarSample s;
            s.flags = r.UShort();
            s.pos = r.Vec3();
            s.yaw = SyncQuant.UShortToYaw(r.UShort());
            s.pitch = r.SByte() / 127f * 90f;
            s.vel = r.Vec3Short(0.01f);
            s.eyeY = r.Byte() / 100f;
            s.right = ToItem(r.Byte());
            s.left = ToItem(r.Byte());
            s.air01 = SyncQuant.From01(r.Byte());
            s.depth = r.Byte();
            if (r.Failed) return;
            s.motion = MotionState.Idle;
            s.phase = 0f;
            s.expression = 0;
            s.rod = 0;
            s.tension = 0f;
            s.bobber = Vector3.zero;
            if (r.Remaining >= 3)
            {
                int m = r.Byte();
                s.motion = m <= (int)MotionState.Crouch ? (MotionState)m : MotionState.Idle;
                s.phase = SyncQuant.From01(r.Byte());
                s.expression = r.Byte();
                if ((s.flags & FFishing) != 0)
                {
                    s.rod = r.Byte();
                    s.tension = SyncQuant.From01(r.Byte());
                    if ((s.flags & FBobber) != 0) s.bobber = SyncQuant.ReadPos(ref r);
                }
                if (r.Failed)
                {
                    // a short tail: keep the base pose
                    s.flags = (ushort)(s.flags & ~(FFishing | FBobber));
                }
            }

            SyncTimelines.Get(origin).Observe(ts);
            var a = Ensure(origin);
            if (a != null) a.Push(ts, s);
        }

        static HoldItem ToItem(byte b) => b <= (byte)HoldItem.QuestItem ? (HoldItem)b : HoldItem.None;

        // ------------------------------------------------------------------ predator bumps (host fauna → client)
        void SendBump(int playerId, float airSeconds)
        {
            if (!Net.IsHost || playerId < 0 || playerId == Net.LocalId) return;
            writer.Reset();
            writer.Float(airSeconds);
            NetBus.Send(NetSyncMsg.AvatarBump, writer.ToArray(), playerId, true);
        }

        void OnBump(int fromId, byte[] payload)
        {
            if (Net.IsHost) return;   // only the host's fauna bumps
            var r = new SyncReader(payload);
            float air = r.Float();
            if (r.Failed || air <= 0f || !NetWorldSync.InGame) return;
            var p = Refs.Player;
            if (!NetWorldSync.Alive(p) || p.IsFainting || !p.IsUnderwater) return;
            GameEvents.RaisePlayerBumped(Mathf.Min(air, 60f));
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Instance = null; }
    }
}
