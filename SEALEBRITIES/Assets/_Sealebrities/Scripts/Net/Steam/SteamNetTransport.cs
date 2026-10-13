#if (UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || STEAMWORKS_WIN || STEAMWORKS_LIN_OSX || !UNITY_5_3_OR_NEWER) && !DISABLESTEAMWORKS
#define SB_STEAM
#endif

#if SB_STEAM
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using Steamworks;
using Debug = UnityEngine.Debug;

namespace Sealebrities
{
    /// INetTransport over ISteamNetworkingSockets P2P (Steam relay network: no IP, no port forwarding, works behind
    /// NAT / CGNAT). Carries exactly the frames of the TCP transport – (type, sender id, payload) – so the handshake
    /// (Hello → Welcome / Reject), NetBus, state sync, avatars and voice run unchanged.
    ///   Host:   CreateListenSocketP2P(virtual port 0) + one poll group; incoming connections are accepted in the
    ///           connection status callback and get the connection ids 1, 2, … (= the player ids, like TCP).
    ///   Client: ConnectP2P(host SteamID, virtual port 0) – connection id 0 is the host.
    /// Steam message layout (little-endian):
    ///   whole message  [0][type][int32 sender][payload]                       (≤ 256 KB payload)
    ///   first fragment [1][type][int32 sender][int32 total length][chunk]     bigger payloads (Welcome / state
    ///   next fragment  [2][chunk]                                              snapshots) as 256 KB reliable chunks
    /// Steam limits one message to 512 KB; reliable messages arrive in order per connection, so the fragments of one
    /// payload can never interleave with another reliable message. reliable = false → k_nSteamNetworkingSend_Unreliable
    /// (may be dropped, never queued locally); reliable → k_nSteamNetworkingSend_Reliable. When Steam's 8 MB send buffer
    /// is full, reliable frames wait in a local backlog (sent first, order kept) – like the TCP send queue.
    /// Liveness: Steam keeps the link alive on its own thread and reports a dead peer through the status callback
    /// (Disconnected "timeout"/"lost"), so SecondsSinceReceive only reports peers that stay silent for 45 s.
    /// Threading: main thread only (Steam callbacks are dispatched by SteamAPI.RunCallbacks in SteamServiceRunner).
    public sealed class SteamNetTransport : INetTransport
    {
        public const int VirtualPort = 0;
        public const int MaxPayload = TcpNetTransport.MaxPayload;
        /// Payload bytes per fragment (Steam's per-message limit is k_cbMaxSteamNetworkingSocketsMessageSizeSend = 512 KB).
        const int ChunkBytes = 256 * 1024;
        const int WholeHeader = 6, FirstHeader = 10, MoreHeader = 1;
        const byte KindWhole = 0, KindFirst = 1, KindMore = 2;
        const int SendBufferBytes = 8 * 1024 * 1024;
        const int SendRateMaxBytes = 2 * 1024 * 1024;
        const int ConnectedTimeoutMs = 15000;
        const int ListenInitialTimeoutMs = 20000;
        const int BacklogOverflowBytes = 48 * 1024 * 1024;
        /// More simultaneous connections than this are refused right away (the session itself allows 4 players).
        const int MaxConnections = 8;
        const double StuckPeerSeconds = 45.0;
        const int ReliableFlags = Constants.k_nSteamNetworkingSend_Reliable;
        const int UnreliableFlags = Constants.k_nSteamNetworkingSend_Unreliable;
        const int AppCloseReason = (int)ESteamNetConnectionEnd.k_ESteamNetConnectionEnd_App_Generic;

        sealed class Conn
        {
            public int id;
            public HSteamNetConnection h;
            public bool connected;
            public bool closing;
            public string closeReason;
            public long lastReceive;                 // Stopwatch timestamp
            public readonly Queue<byte[]> backlog = new Queue<byte[]>();
            public int backlogBytes;
            // reassembly of a fragmented payload
            public byte[] asm;
            public int asmFilled;
            public byte asmType;
            public int asmSender;
        }

        static int offData = -1, offSize, offConn;

        readonly Queue<NetTransportEvent> events = new Queue<NetTransportEvent>(64);
        readonly Dictionary<uint, Conn> byHandle = new Dictionary<uint, Conn>();
        readonly Dictionary<int, Conn> byId = new Dictionary<int, Conn>();
        readonly List<Conn> scratch = new List<Conn>(8);
        /// Incoming P2P requests from a Steam id that is not (yet) in our lobby: kept unaccepted and re-checked for
        /// PendingSeconds (the member list can update after the connect request), then closed. Ids are never logged.
        struct PendingConn { public HSteamNetConnection h; public ulong steamId; public long since; }
        readonly List<PendingConn> pending = new List<PendingConn>(4);
        const double PendingSeconds = 5.0;
        readonly IntPtr[] msgs = new IntPtr[64];
        Callback<SteamNetConnectionStatusChangedCallback_t> statusCb;
        HSteamListenSocket listen = HSteamListenSocket.Invalid;
        HSteamNetPollGroup pollGroup = HSteamNetPollGroup.Invalid;
        Conn clientConn;
        IntPtr sendBuf = IntPtr.Zero;
        int sendCap;
        bool active, server, shutDown;
        int nextId = 1;
        long bytesIn, bytesOut;
        int dropped;
        string endpoint = "";

        public bool IsServer => server;
        public bool IsActive => active;
        public string Endpoint => endpoint;
        public long BytesIn => bytesIn;
        public long BytesOut => bytesOut;
        public int DroppedUnreliable => dropped;
        /// Connections currently open (diagnostics / self test).
        public int ConnectionCount => byId.Count;

        static long Now => Stopwatch.GetTimestamp();

        // ------------------------------------------------------------------ start
        /// INetTransport: bind/port are meaningless for Steam – listens on P2P virtual port 0.
        public void StartServer(IPAddress bind, int port) => StartSteamServer();

        /// Opens the P2P listen socket. Throws when Steam is not running or the socket cannot be created.
        public void StartSteamServer()
        {
            if (active || shutDown) throw new InvalidOperationException("transport already used");
            if (!SteamService.Initialized) throw new InvalidOperationException("Steam is not running");
            server = true;
            EnsureCallback();
            pollGroup = SteamNetworkingSockets.CreatePollGroup();
            if (pollGroup == HSteamNetPollGroup.Invalid)
            {
                ReleaseNative();
                throw new InvalidOperationException("CreatePollGroup failed");
            }
            var opts = Options(ListenInitialTimeoutMs);
            listen = SteamNetworkingSockets.CreateListenSocketP2P(VirtualPort, opts.Length, opts);
            if (listen == HSteamListenSocket.Invalid)
            {
                ReleaseNative();
                throw new InvalidOperationException("CreateListenSocketP2P failed");
            }
            active = true;
            endpoint = "Steam P2P host";
        }

        /// INetTransport: host = the host's SteamID64 as decimal text (port ignored).
        public void StartClient(string host, int port, int timeoutMs)
        {
            if (!ulong.TryParse((host ?? "").Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out ulong id) || id == 0)
                throw new ArgumentException("not a Steam id");
            StartSteamClient(id, timeoutMs);
        }

        /// Starts connecting to the host over the relay network; the result arrives as a Connected or Failed event.
        public void StartSteamClient(ulong hostSteamId, int timeoutMs)
        {
            if (active || shutDown) throw new InvalidOperationException("transport already used");
            if (!SteamService.Initialized) throw new InvalidOperationException("Steam is not running");
            server = false;
            EnsureCallback();
            active = true;
            endpoint = "Steam P2P client";
            var identity = new SteamNetworkingIdentity();
            identity.SetSteamID(new CSteamID(hostSteamId));
            var opts = Options(Math.Max(5000, timeoutMs));
            var h = SteamNetworkingSockets.ConnectP2P(ref identity, VirtualPort, opts.Length, opts);
            if (h == HSteamNetConnection.Invalid)
            {
                events.Enqueue(new NetTransportEvent { kind = NetTransportEventKind.Failed, connection = 0, reason = "unreachable" });
                return;
            }
            var c = new Conn { id = 0, h = h, lastReceive = Now };
            Register(c);
            clientConn = c;
        }

        /// Self test (Debug/SteamSelfTest): wraps one end of an in-process socket pair (CreateSocketPair) as host
        /// connection 1 or as the client's host connection 0, so the framing and fragmentation run over real Steam
        /// networking sockets without a second PC.
        internal void AttachForSelfTest(HSteamNetConnection h, bool asServer)
        {
            if (active || shutDown) throw new InvalidOperationException("transport already used");
            server = asServer;
            EnsureCallback();
            if (asServer)
            {
                pollGroup = SteamNetworkingSockets.CreatePollGroup();
                if (pollGroup == HSteamNetPollGroup.Invalid) throw new InvalidOperationException("CreatePollGroup failed");
            }
            active = true;
            endpoint = asServer ? "Steam loopback host" : "Steam loopback client";
            var c = new Conn { id = asServer ? nextId++ : 0, h = h, lastReceive = Now };
            if (asServer) SteamNetworkingSockets.SetConnectionPollGroup(h, pollGroup);
            Register(c);
            if (!asServer) clientConn = c;
            MarkConnected(c);
        }

        static SteamNetworkingConfigValue_t[] Options(int initialTimeoutMs)
        {
            return new[]
            {
                Int32Option(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendBufferSize, SendBufferBytes),
                Int32Option(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMax, SendRateMaxBytes),
                Int32Option(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_TimeoutInitial, initialTimeoutMs),
                Int32Option(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_TimeoutConnected, ConnectedTimeoutMs),
            };
        }

        static SteamNetworkingConfigValue_t Int32Option(ESteamNetworkingConfigValue key, int value)
        {
            var o = new SteamNetworkingConfigValue_t
            {
                m_eValue = key,
                m_eDataType = ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32
            };
            o.m_val.m_int32 = value;
            return o;
        }

        void EnsureCallback()
        {
            if (statusCb == null) statusCb = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnStatusChanged);
            if (offData < 0) ComputeOffsets();
        }

        static void ComputeOffsets()
        {
            try
            {
                offSize = Marshal.OffsetOf(typeof(SteamNetworkingMessage_t), "m_cbSize").ToInt32();
                offConn = Marshal.OffsetOf(typeof(SteamNetworkingMessage_t), "m_conn").ToInt32();
                offData = Marshal.OffsetOf(typeof(SteamNetworkingMessage_t), "m_pData").ToInt32();
            }
            catch (Exception)
            {
                // native layout: void* m_pData; int m_cbSize; HSteamNetConnection m_conn;
                offData = 0;
                offSize = IntPtr.Size;
                offConn = IntPtr.Size + 4;
            }
        }

        void Register(Conn c)
        {
            byHandle[c.h.m_HSteamNetConnection] = c;
            byId[c.id] = c;
        }

        void Forget(Conn c)
        {
            if (byHandle.TryGetValue(c.h.m_HSteamNetConnection, out var cur) && cur == c) byHandle.Remove(c.h.m_HSteamNetConnection);
            if (byId.TryGetValue(c.id, out var cur2) && cur2 == c) byId.Remove(c.id);
            if (clientConn == c) clientConn = null;
            c.asm = null;
            c.backlog.Clear();
            c.backlogBytes = 0;
        }

        // ------------------------------------------------------------------ connection status (Steam callback)
        void OnStatusChanged(SteamNetConnectionStatusChangedCallback_t cb)
        {
            if (!active) return;
            uint key = cb.m_hConn.m_HSteamNetConnection;
            var state = cb.m_info.m_eState;
            byHandle.TryGetValue(key, out var c);
            if (c == null)
            {
                if (server && state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting
                    && listen != HSteamListenSocket.Invalid && cb.m_info.m_hListenSocket == listen)
                {
                    ulong remote = 0;
                    try { remote = cb.m_info.m_identityRemote.GetSteamID().m_SteamID; } catch (Exception) { remote = 0; }
                    if (IsAllowedPeer(remote)) AcceptIncoming(cb.m_hConn);
                    else if (PendingIndex(cb.m_hConn) < 0) pending.Add(new PendingConn { h = cb.m_hConn, steamId = remote, since = Now });
                }
                else if (state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer
                         || state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally)
                {
                    int pi = PendingIndex(cb.m_hConn);
                    if (pi >= 0)
                    {
                        pending.RemoveAt(pi);
                        try { SteamNetworkingSockets.CloseConnection(cb.m_hConn, AppCloseReason, "closed", false); } catch (Exception) { }
                    }
                }
                return;
            }
            switch (state)
            {
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:
                    MarkConnected(c);
                    break;
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
                {
                    string reason = EndReason(cb.m_info.m_eEndReason, state);
                    bool wasConnected = c.connected;
                    // messages that arrived before the close (SessionEnd, Reject …) are delivered before the Disconnected event
                    if (wasConnected) Drain(c);
                    Forget(c);
                    try { SteamNetworkingSockets.CloseConnection(cb.m_hConn, AppCloseReason, "closed", false); }
                    catch (Exception) { }
                    if (wasConnected) events.Enqueue(new NetTransportEvent { kind = NetTransportEventKind.Disconnected, connection = c.id, reason = reason });
                    else if (!server) events.Enqueue(new NetTransportEvent { kind = NetTransportEventKind.Failed, connection = 0, reason = reason });
                    break;
                }
            }
        }

        static string EndReason(int end, ESteamNetworkingConnectionState state)
        {
            switch ((ESteamNetConnectionEnd)end)
            {
                case ESteamNetConnectionEnd.k_ESteamNetConnectionEnd_Misc_Timeout:
                case ESteamNetConnectionEnd.k_ESteamNetConnectionEnd_Remote_Timeout:
                    return "timeout";
                case ESteamNetConnectionEnd.k_ESteamNetConnectionEnd_Local_OfflineMode:
                case ESteamNetConnectionEnd.k_ESteamNetConnectionEnd_Misc_SteamConnectivity:
                case ESteamNetConnectionEnd.k_ESteamNetConnectionEnd_Local_ManyRelayConnectivity:
                case ESteamNetConnectionEnd.k_ESteamNetConnectionEnd_Misc_NoRelaySessionsToClient:
                    return "steam";
                case ESteamNetConnectionEnd.k_ESteamNetConnectionEnd_Misc_P2P_Rendezvous:
                case ESteamNetConnectionEnd.k_ESteamNetConnectionEnd_Misc_P2P_NAT_Firewall:
                case ESteamNetConnectionEnd.k_ESteamNetConnectionEnd_Local_P2P_ICE_NoPublicAddresses:
                case ESteamNetConnectionEnd.k_ESteamNetConnectionEnd_Remote_P2P_ICE_NoPublicAddresses:
                    return "unreachable";
                case ESteamNetConnectionEnd.k_ESteamNetConnectionEnd_Misc_PeerSentNoConnection:
                    return "refused";
            }
            if (end >= (int)ESteamNetConnectionEnd.k_ESteamNetConnectionEnd_App_Min && end <= (int)ESteamNetConnectionEnd.k_ESteamNetConnectionEnd_AppException_Max)
                return "closed";
            return state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer ? "closed" : "lost";
        }

        /// Only members of our Steam lobby may connect (an incoming P2P request on virtual port 0 from anybody else is
        /// held, re-checked and closed). Without a lobby (debug paths) everyone is accepted, as before.
        static bool IsAllowedPeer(ulong steamId)
        {
            ulong lobby = SteamLobby.CurrentLobby;
            if (lobby == 0) return true;
            return SteamLobby.IsLobbyMember(new CSteamID(lobby), steamId);
        }

        int PendingIndex(HSteamNetConnection h)
        {
            for (int i = 0; i < pending.Count; i++) if (pending[i].h.m_HSteamNetConnection == h.m_HSteamNetConnection) return i;
            return -1;
        }

        /// Re-checks held connection requests: accept once the peer shows up in the lobby, close after PendingSeconds.
        void ServicePending()
        {
            if (pending.Count == 0) return;
            long now = Now;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var p = pending[i];
                if (IsAllowedPeer(p.steamId))
                {
                    pending.RemoveAt(i);
                    AcceptIncoming(p.h);
                    continue;
                }
                if ((now - p.since) / (double)Stopwatch.Frequency < PendingSeconds) continue;
                pending.RemoveAt(i);
                try { SteamNetworkingSockets.CloseConnection(p.h, AppCloseReason, "not in lobby", false); } catch (Exception) { }
                Debug.LogWarning("[Net] Steam: refused a connection from outside the lobby");
            }
        }

        void AcceptIncoming(HSteamNetConnection h)
        {
            if (byId.Count >= MaxConnections)
            {
                try { SteamNetworkingSockets.CloseConnection(h, AppCloseReason, "full", false); } catch (Exception) { }
                return;
            }
            EResult r;
            try { r = SteamNetworkingSockets.AcceptConnection(h); }
            catch (Exception) { r = EResult.k_EResultFail; }
            if (r != EResult.k_EResultOK)
            {
                try { SteamNetworkingSockets.CloseConnection(h, AppCloseReason, "accept failed", false); } catch (Exception) { }
                Debug.LogWarning("[Net] Steam: could not accept an incoming connection (" + r + ")");
                return;
            }
            var c = new Conn { id = nextId++, h = h, lastReceive = Now };
            SteamNetworkingSockets.SetConnectionPollGroup(h, pollGroup);
            SteamNetworkingSockets.SetConnectionUserData(h, c.id);
            Register(c);
            // the Connected event follows with the k_ESteamNetworkingConnectionState_Connected callback
        }

        void MarkConnected(Conn c)
        {
            if (c.connected) return;
            c.connected = true;
            c.lastReceive = Now;
            events.Enqueue(new NetTransportEvent { kind = NetTransportEventKind.Connected, connection = c.id });
        }

        // ------------------------------------------------------------------ receive
        void Receive()
        {
            for (int round = 0; round < 16; round++)
            {
                int n;
                if (server)
                {
                    if (pollGroup == HSteamNetPollGroup.Invalid) return;
                    n = SteamNetworkingSockets.ReceiveMessagesOnPollGroup(pollGroup, msgs, msgs.Length);
                }
                else
                {
                    if (clientConn == null) return;
                    n = SteamNetworkingSockets.ReceiveMessagesOnConnection(clientConn.h, msgs, msgs.Length);
                }
                if (n <= 0) return;
                Consume(n);
                if (n < msgs.Length) return;
            }
        }

        /// Everything still waiting on one connection (its status changed to closed).
        void Drain(Conn c)
        {
            for (int round = 0; round < 64; round++)
            {
                int n;
                try { n = SteamNetworkingSockets.ReceiveMessagesOnConnection(c.h, msgs, msgs.Length); }
                catch (Exception) { return; }
                if (n <= 0) return;
                Consume(n);
                if (n < msgs.Length) return;
            }
        }

        void Consume(int n)
        {
            for (int i = 0; i < n && i < msgs.Length; i++)
            {
                IntPtr m = msgs[i];
                msgs[i] = IntPtr.Zero;
                if (m == IntPtr.Zero) continue;
                try
                {
                    IntPtr data = Marshal.ReadIntPtr(m, offData);
                    int size = Marshal.ReadInt32(m, offSize);
                    uint conn = unchecked((uint)Marshal.ReadInt32(m, offConn));
                    if (byHandle.TryGetValue(conn, out var c)) HandleMessage(c, data, size);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[Net] Steam: unreadable message (" + e.GetType().Name + ")");
                }
                finally
                {
                    SteamNetworkingMessage_t.Release(m);
                }
            }
        }

        void HandleMessage(Conn c, IntPtr data, int size)
        {
            if (size <= 0 || data == IntPtr.Zero || c.closing) return;
            bytesIn += size;
            c.lastReceive = Now;
            // data can only flow on an established connection: never let a Hello overtake the Connected event
            if (!c.connected) MarkConnected(c);
            byte kind = Marshal.ReadByte(data, 0);
            switch (kind)
            {
                case KindWhole:
                {
                    if (size < WholeHeader) { BadFrame(c); return; }
                    byte type = Marshal.ReadByte(data, 1);
                    int sender = Marshal.ReadInt32(data, 2);
                    if (type == NetCoreMsg.Heartbeat) return;
                    int len = size - WholeHeader;
                    byte[] payload = len == 0 ? Array.Empty<byte>() : new byte[len];
                    if (len > 0) Marshal.Copy(IntPtr.Add(data, WholeHeader), payload, 0, len);
                    Deliver(c, type, sender, payload);
                    return;
                }
                case KindFirst:
                {
                    if (size < FirstHeader) { BadFrame(c); return; }
                    int total = Marshal.ReadInt32(data, 6);
                    if (total < 0 || total > MaxPayload) { BadFrame(c); return; }
                    c.asmType = Marshal.ReadByte(data, 1);
                    c.asmSender = Marshal.ReadInt32(data, 2);
                    c.asm = total == 0 ? Array.Empty<byte>() : new byte[total];
                    c.asmFilled = 0;
                    AppendChunk(c, data, FirstHeader, size);
                    return;
                }
                case KindMore:
                    if (c.asm == null) { BadFrame(c); return; }
                    AppendChunk(c, data, MoreHeader, size);
                    return;
                default:
                    return;   // unknown kind (newer build): ignored – the Hello version check rejects real mismatches
            }
        }

        void AppendChunk(Conn c, IntPtr data, int offset, int size)
        {
            int len = size - offset;
            if (len > c.asm.Length - c.asmFilled) { BadFrame(c); return; }
            if (len > 0) Marshal.Copy(IntPtr.Add(data, offset), c.asm, c.asmFilled, len);
            c.asmFilled += len;
            if (c.asmFilled < c.asm.Length) return;
            byte[] payload = c.asm;
            c.asm = null;
            c.asmFilled = 0;
            if (c.asmType == NetCoreMsg.Heartbeat) return;
            Deliver(c, c.asmType, c.asmSender, payload);
        }

        void Deliver(Conn c, byte type, int sender, byte[] payload)
        {
            events.Enqueue(new NetTransportEvent
            {
                kind = NetTransportEventKind.Data,
                connection = c.id,
                type = type,
                sender = sender,
                payload = payload
            });
        }

        void BadFrame(Conn c)
        {
            c.asm = null;
            Disconnect(c.id, "bad frame");
        }

        // ------------------------------------------------------------------ send
        void EnsureSendBuf(int bytes)
        {
            if (sendBuf != IntPtr.Zero && sendCap >= bytes) return;
            int cap = Math.Max(bytes, 64 * 1024);
            if (sendBuf != IntPtr.Zero) Marshal.FreeHGlobal(sendBuf);
            sendBuf = Marshal.AllocHGlobal(cap);
            sendCap = cap;
        }

        public void Send(int connection, byte type, int sender, byte[] payload, bool reliable)
        {
            if (!active) return;
            if (!byId.TryGetValue(connection, out var c) || c.closing || !c.connected) return;
            if (payload == null) payload = Array.Empty<byte>();
            int len = payload.Length;
            if (len > MaxPayload) return;

            if (len <= ChunkBytes)
            {
                // the payload is copied into the native buffer right away, so callers may reuse their arrays
                int size = WholeHeader + len;
                EnsureSendBuf(size);
                Marshal.WriteByte(sendBuf, 0, KindWhole);
                Marshal.WriteByte(sendBuf, 1, type);
                Marshal.WriteInt32(sendBuf, 2, sender);
                if (len > 0) Marshal.Copy(payload, 0, IntPtr.Add(sendBuf, WholeHeader), len);
                if (reliable) SendReliable(c, size);
                else SendUnreliable(c, size);
                return;
            }

            // big payload (Welcome / state snapshot): reliable fragments, also when the caller asked for unreliable
            int off = 0;
            bool first = true;
            while (off < len)
            {
                int header = first ? FirstHeader : MoreHeader;
                int chunk = Math.Min(ChunkBytes, len - off);
                EnsureSendBuf(header + chunk);
                Marshal.WriteByte(sendBuf, 0, first ? KindFirst : KindMore);
                if (first)
                {
                    Marshal.WriteByte(sendBuf, 1, type);
                    Marshal.WriteInt32(sendBuf, 2, sender);
                    Marshal.WriteInt32(sendBuf, 6, len);
                }
                Marshal.Copy(payload, off, IntPtr.Add(sendBuf, header), chunk);
                SendReliable(c, header + chunk);
                if (c.closing) return;   // backlog overflow closed it
                off += chunk;
                first = false;
            }
        }

        void SendUnreliable(Conn c, int size)
        {
            EResult r;
            try { r = SteamNetworkingSockets.SendMessageToConnection(c.h, sendBuf, (uint)size, UnreliableFlags, out long _); }
            catch (Exception) { r = EResult.k_EResultFail; }
            if (r == EResult.k_EResultOK) bytesOut += size;
            else dropped++;   // send buffer full / connection going down: unreliable frames are simply lost
        }

        void SendReliable(Conn c, int size)
        {
            if (c.backlog.Count == 0)
            {
                EResult r;
                try { r = SteamNetworkingSockets.SendMessageToConnection(c.h, sendBuf, (uint)size, ReliableFlags, out long _); }
                catch (Exception) { r = EResult.k_EResultFail; }
                if (r == EResult.k_EResultOK) { bytesOut += size; return; }
                if (r != EResult.k_EResultLimitExceeded) return;   // connection gone: the status callback reports it
            }
            // Steam's send buffer is full: keep the frame (order kept) and retry every frame
            var copy = new byte[size];
            Marshal.Copy(sendBuf, copy, 0, size);
            c.backlog.Enqueue(copy);
            c.backlogBytes += size;
            if (c.backlogBytes > BacklogOverflowBytes) Close(c, "overflow", false);
        }

        static EResult SendManaged(HSteamNetConnection h, byte[] data, int flags)
        {
            var pin = GCHandle.Alloc(data, GCHandleType.Pinned);
            try { return SteamNetworkingSockets.SendMessageToConnection(h, pin.AddrOfPinnedObject(), (uint)data.Length, flags, out long _); }
            catch (Exception) { return EResult.k_EResultFail; }
            finally { pin.Free(); }
        }

        /// Sends what the backlog holds; false while Steam's buffer is still full.
        bool FlushBacklog(Conn c)
        {
            while (c.backlog.Count > 0)
            {
                var f = c.backlog.Peek();
                var r = SendManaged(c.h, f, ReliableFlags);
                if (r == EResult.k_EResultLimitExceeded) return false;
                c.backlog.Dequeue();
                c.backlogBytes -= f.Length;
                if (r == EResult.k_EResultOK) bytesOut += f.Length;
            }
            return true;
        }

        /// Once per poll round: backlogs out, pending closes done, new messages in.
        void Service()
        {
            if (server) ServicePending();
            scratch.Clear();
            foreach (var kv in byId) scratch.Add(kv.Value);
            for (int i = 0; i < scratch.Count; i++)
            {
                var c = scratch[i];
                bool empty = c.backlog.Count == 0 || FlushBacklog(c);
                if (c.closing && empty) Close(c, c.closeReason ?? "closed", true);
            }
            scratch.Clear();
            Receive();
        }

        // ------------------------------------------------------------------ main thread API
        public bool Poll(out NetTransportEvent e)
        {
            if (events.Count == 0 && active) Service();
            if (events.Count > 0)
            {
                e = events.Dequeue();
                return true;
            }
            e = default;
            return false;
        }

        public void Disconnect(int connection, string reason)
        {
            if (!byId.TryGetValue(connection, out var c) || c.closing) return;
            c.closing = true;
            c.closeReason = reason ?? "closed";
            if (c.backlog.Count == 0) Close(c, c.closeReason, true);
            // otherwise Service() closes it as soon as the backlog is out
        }

        /// linger: Steam keeps sending what is queued (e.g. a Reject / SessionEnd) after the handle is closed.
        void Close(Conn c, string reason, bool linger)
        {
            if (!byHandle.TryGetValue(c.h.m_HSteamNetConnection, out var cur) || cur != c) return;
            c.closing = true;
            Forget(c);
            if (SteamService.Initialized)
            {
                try
                {
                    if (linger) SteamNetworkingSockets.FlushMessagesOnConnection(c.h);
                    SteamNetworkingSockets.CloseConnection(c.h, AppCloseReason, reason ?? "closed", linger);
                }
                catch (Exception) { }
            }
            if (active) events.Enqueue(new NetTransportEvent { kind = NetTransportEventKind.Disconnected, connection = c.id, reason = reason });
        }

        public double SecondsSinceReceive(int connection)
        {
            if (!byId.TryGetValue(connection, out var c)) return 0;
            double s = (Now - c.lastReceive) / (double)Stopwatch.Frequency;
            return s >= StuckPeerSeconds ? s : 0;
        }

        public void Shutdown(int flushMs)
        {
            if (shutDown) return;
            shutDown = true;
            bool steamUp = SteamService.Initialized;
            scratch.Clear();
            foreach (var kv in byId) scratch.Add(kv.Value);
            if (steamUp)
            {
                try
                {
                    for (int i = 0; i < scratch.Count; i++)
                    {
                        FlushBacklog(scratch[i]);
                        SteamNetworkingSockets.FlushMessagesOnConnection(scratch[i].h);
                    }
                    if (flushMs > 0 && scratch.Count > 0) WaitForAcks(flushMs);
                    for (int i = 0; i < scratch.Count; i++)
                        SteamNetworkingSockets.CloseConnection(scratch[i].h, AppCloseReason, "shutdown", true);
                    for (int i = 0; i < pending.Count; i++)
                        SteamNetworkingSockets.CloseConnection(pending[i].h, AppCloseReason, "shutdown", false);
                }
                catch (Exception e) { Debug.LogWarning("[Net] Steam transport shutdown: " + e.Message); }
            }
            pending.Clear();
            for (int i = 0; i < scratch.Count; i++) Forget(scratch[i]);
            scratch.Clear();
            active = false;
            ReleaseNative();
            events.Clear();
        }

        /// Gives Steam up to flushMs to get the queued reliable data (goodbye messages) acknowledged.
        void WaitForAcks(int flushMs)
        {
            var sw = Stopwatch.StartNew();
            var status = new SteamNetConnectionRealTimeStatus_t { reserved = new uint[15] };
            var lanes = new SteamNetConnectionRealTimeLaneStatus_t { reserved = new uint[10] };
            while (sw.ElapsedMilliseconds < flushMs)
            {
                bool busy = false;
                for (int i = 0; i < scratch.Count && !busy; i++)
                {
                    var r = SteamNetworkingSockets.GetConnectionRealTimeStatus(scratch[i].h, ref status, 0, ref lanes);
                    if (r == EResult.k_EResultOK && status.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected
                        && (status.m_cbPendingReliable > 0 || status.m_cbSentUnackedReliable > 0)) busy = true;
                }
                if (!busy) break;
                Thread.Sleep(5);
            }
        }

        void ReleaseNative()
        {
            if (SteamService.Initialized)
            {
                try
                {
                    if (listen != HSteamListenSocket.Invalid) SteamNetworkingSockets.CloseListenSocket(listen);
                    if (pollGroup != HSteamNetPollGroup.Invalid) SteamNetworkingSockets.DestroyPollGroup(pollGroup);
                }
                catch (Exception) { }
            }
            listen = HSteamListenSocket.Invalid;
            pollGroup = HSteamNetPollGroup.Invalid;
            if (statusCb != null)
            {
                try { statusCb.Dispose(); } catch (Exception) { }
                statusCb = null;
            }
            if (sendBuf != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(sendBuf);
                sendBuf = IntPtr.Zero;
                sendCap = 0;
            }
        }
    }
}
#endif
