using UnityEngine;

namespace Sealebrities
{
    /// DontDestroyOnLoad pump of the co-op module: drains the transport's event queue on the main thread, dispatches
    /// frames into NetBus, implements NetBus.SendImpl/SendExceptImpl and ticks session, state sync, lobby and
    /// diagnostics. Created by NetBoot (no scene object needed). Runs before the gameplay scripts so a received
    /// snapshot is applied before GameManager/UI read the state in the same frame.
    [DefaultExecutionOrder(-800)]
    public sealed class NetCoreRunner : MonoBehaviour
    {
        public static NetCoreRunner I { get; private set; }

        /// The active transport (null while offline). Owned by NetSession.
        public static INetTransport Transport;

        /// Sender of the frame currently being dispatched (-1 outside of a dispatch). SendExcept uses it to keep the
        /// original client's id when the host relays that client's stream.
        static int dispatchFrom = -1;

        INetTransport diagTransport;
        long diagIn, diagOut;
        float diagTimer;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            I = null;
            Transport = null;
            dispatchFrom = -1;
        }

        public static void Ensure()
        {
            if (I != null) return;
            var go = new GameObject("SB_Net");
            DontDestroyOnLoad(go);
            go.AddComponent<NetCoreRunner>();
        }

        void Awake()
        {
            if (I != null && I != this) { Destroy(gameObject); return; }
            I = this;
            NetBus.SendImpl = SendImpl;
            NetBus.SendExceptImpl = SendExceptImpl;
        }

        void OnDestroy()
        {
            if (I != this) return;
            NetSession.OnQuit();
            I = null;
        }

        void OnApplicationQuit()
        {
            NetSession.OnQuit();
        }

        void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.25f);
            Pump();
            NetSession.Tick(dt);
            NetState.Tick(dt);
            NetBoot.Tick(dt);
            NetLobby.Tick(dt);
            TickDiagnostics(dt);
        }

        // ------------------------------------------------------------------ receive
        static void Pump()
        {
            for (int guard = 0; guard < 4000; guard++)
            {
                var t = Transport;
                if (t == null || !t.Poll(out var e)) return;
                switch (e.kind)
                {
                    case NetTransportEventKind.Connected: NetSession.OnTransportConnected(e.connection); break;
                    case NetTransportEventKind.Disconnected: NetSession.OnTransportDisconnected(e.connection, e.reason); break;
                    case NetTransportEventKind.Failed: NetSession.OnTransportFailed(e.reason); break;
                    default: Deliver(e.connection, e.type, e.sender, e.payload); break;
                }
            }
        }

        static void Deliver(int connection, byte type, int sender, byte[] payload)
        {
            int from;
            if (NetSession.IsHostSide)
            {
                // a client cannot pretend to be someone else: on the host the connection IS the player id
                from = connection;
                if (!NetSession.IsMember(from) && !NetSession.IsPreJoinType(type)) return;
            }
            else from = sender;
            int prev = dispatchFrom;
            dispatchFrom = from;
            NetBus.Dispatch(type, from, payload);
            dispatchFrom = prev;
        }

        // ------------------------------------------------------------------ NetBus implementation
        static void SendImpl(byte type, byte[] payload, int target, bool reliable)
        {
            var t = Transport;
            if (t == null) return;
            if (Net.IsClient)
            {
                t.Send(0, type, Net.LocalId, payload, reliable);
                return;
            }
            if (!Net.IsHost) return;
            var players = Net.Players;
            if (target == NetBus.Everyone)
            {
                for (int i = 0; i < players.Count; i++)
                {
                    int id = players[i].id;
                    if (id != Net.LocalId) t.Send(id, type, Net.LocalId, payload, reliable);
                }
            }
            else if (target != Net.LocalId && NetSession.IsMember(target))
            {
                t.Send(target, type, Net.LocalId, payload, reliable);
            }
        }

        static void SendExceptImpl(byte type, byte[] payload, int exceptId, bool reliable)
        {
            var t = Transport;
            if (t == null || !Net.IsHost) return;
            int sender = exceptId > 0 && exceptId == dispatchFrom ? exceptId : Net.LocalId;
            var players = Net.Players;
            for (int i = 0; i < players.Count; i++)
            {
                int id = players[i].id;
                if (id != Net.LocalId && id != exceptId) t.Send(id, type, sender, payload, reliable);
            }
        }

        // ------------------------------------------------------------------ diagnostics
        void TickDiagnostics(float dt)
        {
            var t = Transport;
            if (t != diagTransport)
            {
                diagTransport = t;
                diagIn = t != null ? t.BytesIn : 0;
                diagOut = t != null ? t.BytesOut : 0;
                diagTimer = 0f;
                if (t == null)
                {
                    NetDiagnostics.BytesInPerSecond = 0f;
                    NetDiagnostics.BytesOutPerSecond = 0f;
                }
            }
            if (t == null) return;
            diagTimer += dt;
            if (diagTimer < 1f) return;
            long inNow = t.BytesIn, outNow = t.BytesOut;
            NetDiagnostics.BytesInPerSecond = (inNow - diagIn) / diagTimer;
            NetDiagnostics.BytesOutPerSecond = (outNow - diagOut) / diagTimer;
            NetDiagnostics.BytesInTotal += inNow - diagIn;
            NetDiagnostics.BytesOutTotal += outNow - diagOut;
            NetDiagnostics.UnreliableDropped = t.DroppedUnreliable;
            diagIn = inNow;
            diagOut = outNow;
            diagTimer = 0f;
        }
    }
}
