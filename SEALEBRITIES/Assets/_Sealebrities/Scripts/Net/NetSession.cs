using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using UnityEngine;

namespace Sealebrities
{
    /// Co-op session (1–4 players, one shared channel): hosting, joining, the player list, pings, timeouts and the
    /// clean end of a session. The host is player 0; a client's id is its transport connection id (1, 2, …).
    ///
    /// Flow: host StartHost → listener + LAN beacon → host starts/continues a game. Client Join → TCP connect →
    /// Hello {protocol, version, name, look} → host Welcome {id, players, spawn hint, full state} (or Reject full/version)
    /// → client fades into the host's world (GameManager.JoinAsClient, saving disabled). Drop-in join works any time the
    /// host is in game. The host leaving ends the session for everybody (clients return to the menu with a notice).
    ///
    /// Events: Net.PlayerJoined / Net.PlayerLeft are raised for REMOTE players only (the local entry is in Net.Players
    /// from the start). NetSession.Started fires once Net.Mode/Players are set; NetSession.Ended after the reset.
    public static class NetSession
    {
        public enum Phase { Idle, Hosting, Connecting, Joining, Joined }

        public static Phase Current { get; private set; }
        /// True on the hosting machine (also before the host entered the game).
        public static bool IsHostSide => Current == Phase.Hosting;
        public static bool IsConnecting => Current == Phase.Connecting;
        /// Port the host actually listens on (may be a fallback port, see StartHost).
        public static int HostPort { get; private set; } = Net.DefaultPort;
        /// Port the player asked for when hosting (the lobby's port field shows this one).
        public static int RequestedPort { get; private set; } = Net.DefaultPort;

        /// Ports tried after Net.DefaultPort when it cannot be used on this PC (another program holds it – e.g. Apple's
        /// "Mobile Device Service" listens on 127.0.0.1:27015 on every PC with iTunes / Apple Devices – or Windows reserved
        /// it for Hyper-V). A same-PC join of "127.0.0.1" without a port finds the host on one of them automatically.
        public static readonly int[] FallbackPorts = { 27115, 27116, 27117, 27118, 27119 };
        /// "ip:port" of the last join attempt.
        public static string JoinTarget { get; private set; } = "";
        public static string HostName { get; private set; } = "";

        /// Lobby status line (localized) and whether it is an error.
        public static string Status { get; private set; } = "";
        public static bool StatusIsError { get; private set; }
        public static event Action StatusChanged;
        /// Hosting started or the client was welcomed (Net.Mode, LocalId and Players are set).
        public static event Action Started;
        /// The session is over and everything is offline again. Argument: localized notice ("" when we left ourselves).
        public static event Action<string> Ended;

        /// Default suit colours P1–P4 (art bible §1.12) – used when two players picked the same colour.
        public static readonly Color[] PlayerColors =
        {
            new Color(0.933f, 0.498f, 0.176f), new Color(0.247f, 0.722f, 0.769f),
            new Color(0.878f, 0.376f, 0.494f), new Color(0.949f, 0.788f, 0.298f)
        };

        public const float TimeoutSeconds = 10f;
        const float HelloTimeout = 10f, NotReadyTimeout = 90f, WelcomeTimeout = 20f, RetryWelcomeTimeout = 8f;

        sealed class Pending
        {
            public int conn;
            public float since;
            public bool hello;
            public NetPlayerInfo info;
        }

        static readonly List<Pending> pending = new List<Pending>();
        static readonly List<NetPlayerInfo> tmpPlayers = new List<NetPlayerInfo>();
        static NetLanBeacon beacon;
        static float pingTimer, pingListTimer, connectStarted;
        static bool ending, quitDone, installed;
        static bool savingOverridden, savingPrev;
        static float hostToastDelay = -1f, clientToastDelay = -1f;

        // -join auto retry (command line: both windows start at the same time)
        static bool retryEnabled;
        static float retryUntil, retryAt;
        static string retryHost;
        static int retryPort;
        static bool retryExplicitPort = true;

        static float Now => Time.realtimeSinceStartup;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Current = Phase.Idle; HostPort = Net.DefaultPort; RequestedPort = Net.DefaultPort; JoinTarget = ""; HostName = "";
            retryExplicitPort = true;
            Status = ""; StatusIsError = false; StatusChanged = null; Started = null; Ended = null;
            pending.Clear(); tmpPlayers.Clear(); beacon = null;
            pingTimer = 0f; pingListTimer = 0f; connectStarted = 0f;
            ending = false; quitDone = false; installed = false; savingOverridden = false; savingPrev = false;
            hostToastDelay = -1f; clientToastDelay = -1f;
            retryEnabled = false; retryUntil = 0f; retryAt = 0f; retryHost = null; retryPort = 0;
        }

        internal static void Install()
        {
            if (installed) return;
            installed = true;
            NetBus.Register(NetCoreMsg.Hello, OnHello);
            NetBus.Register(NetCoreMsg.Welcome, OnWelcome);
            NetBus.Register(NetCoreMsg.Reject, OnReject);
            NetBus.Register(NetCoreMsg.PlayerList, OnPlayerList);
            NetBus.Register(NetCoreMsg.PlayerPings, OnPlayerPings);
            NetBus.Register(NetCoreMsg.Ping, OnPing);
            NetBus.Register(NetCoreMsg.Pong, OnPong);
            NetBus.Register(NetCoreMsg.Bye, OnBye);
            NetBus.Register(NetCoreMsg.SessionEnd, OnSessionEnd);
            GameEvents.ReturnedToMenu += OnReturnedToMenu;
            GameEvents.GameStarted += OnGameStarted;
        }

        /// Members may send anything; unknown connections only these.
        internal static bool IsPreJoinType(byte type) =>
            type == NetCoreMsg.Hello || type == NetCoreMsg.Ping || type == NetCoreMsg.Pong || type == NetCoreMsg.Bye;

        /// A remote player that completed the handshake (host side: a client; client side: anyone in the list).
        public static bool IsMember(int id)
        {
            if (id == Net.LocalId) return false;
            var p = Net.Find(id);
            return p != null;
        }

        /// Name this machine uses in co-op (-netname wins over the settings).
        public static string LocalName()
        {
            string n = NetBoot.NameOverride;
            return CleanName(string.IsNullOrWhiteSpace(n) ? Settings.DisplayName : n);
        }

        static NetPlayerInfo LocalInfo(int id) => new NetPlayerInfo
        {
            id = id,
            name = LocalName(),
            suit = Settings.Data.suitColor,
            hat = Settings.Data.hat,
            face = Settings.Data.face,
            ready = true
        };

        static string CleanName(string n)
        {
            if (string.IsNullOrWhiteSpace(n)) return Loc.Tr("Taucher", "Diver");
            n = n.Trim().Replace('\n', ' ').Replace('\r', ' ').Replace('|', '/');
            if (n.Length > Settings.MaxNameLength) n = n.Substring(0, Settings.MaxNameLength);
            return n;
        }

        static void SetStatus(string text, bool error)
        {
            Status = text ?? "";
            StatusIsError = error;
            if (error && !string.IsNullOrEmpty(text)) NetDiagnostics.SetError(text);
            StatusChanged?.Invoke();
        }

        public static void ClearStatus()
        {
            if (Current == Phase.Idle && !retryEnabled) SetStatus("", false);
        }

        /// Shows a message in the lobby status line (lobby-side validation errors).
        public static void ReportStatus(string text, bool error) => SetStatus(text, error);

        /// A command line -join is waiting to retry.
        public static bool IsRetrying => retryEnabled && Current == Phase.Idle;

        /// Turns off saving for this session (command line -host test runs); restored when the session ends.
        public static void SuppressSaving()
        {
            if (!savingOverridden)
            {
                savingPrev = GameManager.SavingDisabled;
                savingOverridden = true;
            }
            GameManager.SavingDisabled = true;
        }

        static void ResetFlags()
        {
            NetFlags.BoatRemote = false;
            NetFlags.BoatDriverId = -1;
            NetFlags.CreaturesRemote = false;
        }

        // =================================================================== host
        /// Opens the listener and the LAN beacon. The caller then starts or loads the game (NewGame/LoadGame).
        public static bool StartHost(int port, string bindIp, out string error)
        {
            error = null;
            if (Current != Phase.Idle) EndSession(NetEndReason.LocalLeft, null, false);
            if (port < 1 || port > 65535)
            {
                error = Loc.Tr("Ungültiger Port (1–65535).", "Invalid port (1–65535).");
                return false;
            }
            IPAddress bind = IPAddress.Any;
            if (!string.IsNullOrWhiteSpace(bindIp) && !IPAddress.TryParse(bindIp.Trim(), out bind))
            {
                error = Loc.Tr("Ungültige Bind-Adresse: ", "Invalid bind address: ") + bindIp;
                return false;
            }

            RequestedPort = port;
            // the default port may be held by another program on this PC: then move to a fallback port (and say so)
            int tries = port == Net.DefaultPort ? 1 + FallbackPorts.Length : 1;
            TcpNetTransport t = null;
            int requested = port;
            for (int k = 0; k < tries; k++)
            {
                int p = k == 0 ? requested : FallbackPorts[k - 1];
                var candidate = new TcpNetTransport();
                try
                {
                    candidate.StartServer(bind, p);
                    t = candidate;
                    port = p;
                    break;
                }
                catch (SocketException e)
                {
                    candidate.Shutdown(0);
                    bool busy = e.SocketErrorCode == SocketError.AddressAlreadyInUse || e.SocketErrorCode == SocketError.AccessDenied;
                    if (busy && k + 1 < tries)
                    {
                        Debug.Log("[Net] port " + p + " on " + bind + " is not available (" + e.SocketErrorCode + ") – trying " + FallbackPorts[k]);
                        continue;
                    }
                    error = busy
                        ? Loc.Tr($"Port {p} ist schon belegt – läuft auf diesem PC schon ein Host? Nimm einen anderen Port.",
                                 $"Port {p} is already in use – is another host running on this PC? Pick another port.")
                        : Loc.Tr("Hosten nicht möglich: ", "Cannot host: ") + e.Message;
                    NetDiagnostics.SetError(error);
                    return false;
                }
                catch (Exception e)
                {
                    candidate.Shutdown(0);
                    error = Loc.Tr("Hosten nicht möglich: ", "Cannot host: ") + e.Message;
                    NetDiagnostics.SetError(error);
                    return false;
                }
            }
            if (t == null)
            {
                error = Loc.Tr("Hosten nicht möglich: kein freier Port.", "Cannot host: no free port.");
                NetDiagnostics.SetError(error);
                return false;
            }
            if (port != requested)
                Debug.LogWarning("[Net] port " + requested + " is used by another program on this PC – hosting on port " + port + " instead");
            else if (bind.Equals(IPAddress.Any) && LoopbackShadowed(port))
                Debug.LogWarning("[Net] 127.0.0.1:" + port + " belongs to another program (e.g. Apple Mobile Device Service) – guests on this PC connect via the LAN address");

            NetCoreRunner.Transport = t;
            HostPort = port;
            Net.Mode = NetMode.Host;
            Net.LocalId = 0;
            Net.LocalName = LocalName();
            Net.Players.Clear();
            Net.Players.Add(LocalInfo(0));
            HostName = Net.LocalName;
            ResetFlags();
            pending.Clear();
            try { beacon = new NetLanBeacon(); }
            catch (Exception e) { beacon = null; Debug.LogWarning("[Net] LAN beacon unavailable: " + e.Message); }
            Current = Phase.Hosting;
            quitDone = false;
            Application.runInBackground = true;
            NetDiagnostics.Connected = true;
            NetDiagnostics.Transport = "TCP host " + t.Endpoint;
            NetDiagnostics.LastError = "";
            NetCommands.BeginHost();
            NetState.BeginHost();
            SetStatus(port != requested
                ? Loc.Tr($"Host läuft auf Port {port} (Port {requested} ist auf diesem PC belegt)", $"Hosting on port {port} (port {requested} is taken on this PC)")
                : Loc.Tr($"Host läuft (Port {port})", $"Hosting (port {port})"), false);
            Debug.Log("[Net] hosting on " + t.Endpoint);
            Started?.Invoke();
            return true;
        }

        static Pending FindPending(int conn)
        {
            for (int i = 0; i < pending.Count; i++) if (pending[i].conn == conn) return pending[i];
            return null;
        }

        static void OnHello(int from, byte[] payload)
        {
            if (Current != Phase.Hosting) return;
            var p = FindPending(from);
            if (p == null || p.hello) return;
            var r = new NetCoreReader(payload);
            int proto = r.Int();
            string version = r.Str();
            string name = r.Str();
            Color suit = r.Color();
            int hat = r.Int();
            int face = r.Int();
            if (proto != Net.ProtocolVersion || version != Application.version)
            {
                Reject(p, NetEndReason.VersionMismatch);
                return;
            }
            if (Net.Players.Count >= Net.MaxPlayers)
            {
                Reject(p, NetEndReason.Full);
                return;
            }
            p.hello = true;
            p.since = Now;
            p.info = new NetPlayerInfo
            {
                id = from,
                name = UniqueName(CleanName(name)),
                suit = UniqueSuit(suit),
                hat = Mathf.Clamp(hat, 0, 16),
                face = Mathf.Clamp(face, 0, 16),
                ready = true
            };
            var gm = GameManager.I;
            if (gm != null && gm.InGame) Accept(p);
            // otherwise Tick accepts as soon as the host is in game
        }

        static void Reject(Pending p, NetEndReason reason)
        {
            var t = NetCoreRunner.Transport;
            if (t != null)
            {
                var w = NetCoreWriter.Begin().Byte((byte)reason).Str(Application.version);
                t.Send(p.conn, NetCoreMsg.Reject, 0, w.ToArray(), true);
                t.Disconnect(p.conn, "rejected");
            }
            pending.Remove(p);
            Debug.Log("[Net] rejected connection " + p.conn + ": " + reason);
        }

        static void Accept(Pending p)
        {
            var gm = GameManager.I;
            var t = NetCoreRunner.Transport;
            if (gm == null || t == null) return;
            if (Net.Players.Count >= Net.MaxPlayers)
            {
                Reject(p, NetEndReason.Full);
                return;
            }
            var info = p.info;
            pending.Remove(p);
            Net.Players.Add(info);

            // build everything that does not use the shared writer first
            Vector3 spawn = HostSpawnHint(out bool near, out float yaw);
            byte[] state = NetState.BuildSnapshot(out bool zipped, out int seq);

            var w = NetCoreWriter.Begin().Int(info.id);
            WritePlayers(w);
            w.Bool(near).Vec3(spawn).Float(yaw);
            w.Int((int)gm.State.difficulty);
            w.Bool(zipped).Int(seq).Blob(state);
            t.Send(info.id, NetCoreMsg.Welcome, 0, w.ToArray(), true);

            BroadcastPlayerList();
            Debug.Log("[Net] player " + info.id + " '" + info.name + "' joined (" + Net.Players.Count + "/" + Net.MaxPlayers + ")");
            Net.RaisePlayerJoined(info);
            GameEvents.RaiseToast(Loc.Tr($"{info.name} ist an Bord!", $"{info.name} joined the crew!"));
        }

        static string UniqueName(string n)
        {
            string baseName = n;
            int k = 2;
            while (NameTaken(n) && k < 20) n = baseName + " " + k++;
            return n;
        }

        static bool NameTaken(string n)
        {
            for (int i = 0; i < Net.Players.Count; i++)
                if (string.Equals(Net.Players[i].name, n, StringComparison.OrdinalIgnoreCase)) return true;
            for (int i = 0; i < pending.Count; i++)
                if (pending[i].info != null && string.Equals(pending[i].info.name, n, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static Color UniqueSuit(Color c)
        {
            c.a = 1f;
            if (!SuitTaken(c)) return c;
            for (int i = 0; i < PlayerColors.Length; i++) if (!SuitTaken(PlayerColors[i])) return PlayerColors[i];
            return c;
        }

        static bool SuitTaken(Color c)
        {
            for (int i = 0; i < Net.Players.Count; i++)
            {
                var s = Net.Players[i].suit;
                if (Mathf.Abs(s.r - c.r) + Mathf.Abs(s.g - c.g) + Mathf.Abs(s.b - c.b) < 0.15f) return true;
            }
            for (int i = 0; i < pending.Count; i++)
            {
                if (pending[i].info == null) continue;
                var s = pending[i].info.suit;
                if (Mathf.Abs(s.r - c.r) + Mathf.Abs(s.g - c.g) + Mathf.Abs(s.b - c.b) < 0.15f) return true;
            }
            return false;
        }

        /// Where a joining client should appear: next to the host when the host stands on land, otherwise the hut.
        static Vector3 HostSpawnHint(out bool near, out float yaw)
        {
            near = false;
            yaw = 0f;
            var p = Refs.Player;
            if (p == null || p.Root == null) return Vector3.zero;
            bool aboard = Refs.Boat != null && Refs.Boat.Root != null && Refs.Boat.PlayerAboard;
            if (p.IsInWater || p.IsSeated || p.IsFainting || aboard) return Vector3.zero;
            near = true;
            yaw = p.View != null ? p.View.transform.eulerAngles.y : p.Root.eulerAngles.y;
            return p.Root.position;
        }

        static void RemovePlayer(NetPlayerInfo p, NetEndReason reason)
        {
            if (p == null || p.IsLocal) return;
            Net.Players.Remove(p);
            if (NetFlags.BoatDriverId == p.id) NetFlags.BoatDriverId = -1;
            NetCommands.Forget(p.id);
            BroadcastPlayerList();
            Debug.Log("[Net] player " + p.id + " '" + p.name + "' left (" + reason + ")");
            Net.RaisePlayerLeft(p);
            GameEvents.RaiseToast(reason == NetEndReason.Timeout || reason == NetEndReason.Lost
                ? Loc.Tr($"{p.name} hat die Verbindung verloren", $"{p.name} lost the connection")
                : Loc.Tr($"{p.name} ist von Bord gegangen", $"{p.name} left the crew"));
        }

        static void OnBye(int from, byte[] payload)
        {
            if (Current != Phase.Hosting) return;
            var pend = FindPending(from);
            if (pend != null) pending.Remove(pend);
            var p = Net.Find(from);
            if (p != null) RemovePlayer(p, NetEndReason.LocalLeft);
            NetCoreRunner.Transport?.Disconnect(from, "bye");
        }

        static void WritePlayers(NetCoreWriter w)
        {
            var list = Net.Players;
            w.Byte((byte)Mathf.Min(list.Count, 255));
            for (int i = 0; i < list.Count && i < 255; i++)
            {
                var p = list[i];
                w.Int(p.id).Str(p.name).Color(p.suit).Int(p.hat).Int(p.face).Int(p.pingMs);
            }
        }

        static void ReadPlayers(NetCoreReader r, List<NetPlayerInfo> into)
        {
            into.Clear();
            int n = r.Byte();
            for (int i = 0; i < n; i++)
            {
                var p = new NetPlayerInfo { id = r.Int(), name = r.Str(), suit = r.Color(), hat = r.Int(), face = r.Int(), pingMs = r.Int(), ready = true };
                into.Add(p);
            }
        }

        static void BroadcastPlayerList()
        {
            if (!Net.IsHost) return;
            var w = NetCoreWriter.Begin();
            WritePlayers(w);
            NetBus.Send(NetCoreMsg.PlayerList, w.ToArray(), NetBus.Everyone, true);
        }

        // =================================================================== client
        /// Parses "ip", "ip:port", "[v6]:port" or "host:port".
        public static bool TryParseAddress(string text, out string host, out int port, out string error) =>
            TryParseAddress(text, out host, out port, out _, out error);

        /// Same, and tells whether the text contained a port (otherwise port = Net.DefaultPort).
        public static bool TryParseAddress(string text, out string host, out int port, out bool explicitPort, out string error)
        {
            host = null;
            port = Net.DefaultPort;
            explicitPort = false;
            error = null;
            text = (text ?? "").Trim();
            if (text.Length == 0)
            {
                error = Loc.Tr("Bitte die IP-Adresse des Hosts eingeben.", "Please enter the host's IP address.");
                return false;
            }
            string portPart = null;
            if (text.StartsWith("[", StringComparison.Ordinal))
            {
                int close = text.IndexOf(']');
                if (close < 0) { error = Loc.Tr("Adresse ungültig.", "Invalid address."); return false; }
                host = text.Substring(1, close - 1);
                if (close + 1 < text.Length && text[close + 1] == ':') portPart = text.Substring(close + 2);
            }
            else
            {
                int first = text.IndexOf(':'), last = text.LastIndexOf(':');
                if (first >= 0 && first == last)
                {
                    host = text.Substring(0, first);
                    portPart = text.Substring(first + 1);
                }
                else host = text;   // no port, or a bare IPv6 address
            }
            host = host.Trim();
            if (host.Length == 0) { error = Loc.Tr("Adresse ungültig.", "Invalid address."); return false; }
            if (!string.IsNullOrEmpty(portPart))
            {
                if (!int.TryParse(portPart.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out port) || port < 1 || port > 65535)
                {
                    error = Loc.Tr("Port ungültig (1–65535).", "Invalid port (1–65535).");
                    return false;
                }
                explicitPort = true;
            }
            return true;
        }

        /// Joins "ip[:port]". Returns false (with a localized error) when the address cannot be parsed.
        public static bool Join(string address, out string error)
        {
            if (!TryParseAddress(address, out string host, out int port, out bool explicitPort, out error))
            {
                SetStatus(error, true);
                return false;
            }
            JoinHostPort(host, port, explicitPort);
            return true;
        }

        public static void JoinHostPort(string host, int port) => JoinHostPort(host, port, true);

        /// explicitPort false: the player typed no port – a host on this PC may sit on a fallback port (see FallbackPorts).
        public static void JoinHostPort(string host, int port, bool explicitPort)
        {
            if (Current != Phase.Idle) EndSession(NetEndReason.LocalLeft, null, false);
            var gm = GameManager.I;
            if (gm != null && gm.InGame)
            {
                SetStatus(Loc.Tr("Erst zurück ins Hauptmenü.", "Return to the main menu first."), true);
                return;
            }
            string connectHost = host;
            int connectPort = port;
            ResolveSamePcTarget(ref connectHost, ref connectPort, explicitPort);
            var t = new TcpNetTransport();
            NetCoreRunner.Transport = t;
            Current = Phase.Connecting;
            quitDone = false;
            connectStarted = Now;
            JoinTarget = connectHost + ":" + connectPort;
            NetDiagnostics.Transport = "TCP client " + JoinTarget;
            SetStatus(Loc.Tr($"Verbinde mit {JoinTarget} …", $"Connecting to {JoinTarget} …"), false);
            Debug.Log("[Net] connecting to " + JoinTarget + (connectHost != host || connectPort != port ? " (asked for " + host + ":" + port + ")" : ""));
            try
            {
                t.StartClient(connectHost, connectPort, 6000);
            }
            catch (Exception e)
            {
                FailJoin(Loc.Tr("Verbindung fehlgeschlagen: ", "Connection failed: ") + e.Message);
            }
        }

        /// Keeps retrying a refused/timed-out join for a while (command line -join while the host is still booting).
        public static void EnableAutoRetry(string host, int port, float seconds) => EnableAutoRetry(host, port, true, seconds);

        public static void EnableAutoRetry(string host, int port, bool explicitPort, float seconds)
        {
            retryEnabled = true;
            retryHost = host;
            retryPort = port;
            retryExplicitPort = explicitPort;
            retryUntil = Now + seconds;
        }

        // ------------------------------------------------------------------ same-PC targets
        static bool IsLoopbackHost(string h) =>
            h == "127.0.0.1" || h == "::1" || string.Equals(h, "localhost", StringComparison.OrdinalIgnoreCase);

        static IPEndPoint[] ActiveListeners()
        {
            try { return IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners(); }
            catch (Exception e)
            {
                Debug.LogWarning("[Net] cannot list TCP listeners: " + e.Message);
                return null;
            }
        }

        static void ListenerKinds(IPEndPoint[] listeners, int port, out bool loopback, out bool wildcard)
        {
            loopback = false;
            wildcard = false;
            if (listeners == null) return;
            for (int i = 0; i < listeners.Length; i++)
            {
                var ep = listeners[i];
                if (ep == null || ep.Port != port) continue;
                if (IPAddress.IsLoopback(ep.Address)) loopback = true;
                else if (ep.Address.Equals(IPAddress.Any) || ep.Address.Equals(IPAddress.IPv6Any)) wildcard = true;
            }
        }

        /// True when another socket listens on 127.0.0.1:port (it would take the same-PC connections to that port).
        static bool LoopbackShadowed(int port)
        {
            ListenerKinds(ActiveListeners(), port, out bool loop, out _);
            return loop;
        }

        /// A join of 127.0.0.1 / localhost: (1) no port typed and the default port is not a game here, but a host listens
        /// on a fallback port → use that port; (2) another program (e.g. Apple's mobile device service) holds
        /// 127.0.0.1:port while the game host listens on all addresses → connect through this PC's LAN address instead
        /// (Windows hands 127.0.0.1 connections to the more specific listener).
        static void ResolveSamePcTarget(ref string host, ref int port, bool explicitPort)
        {
            if (!IsLoopbackHost(host)) return;
            var listeners = ActiveListeners();
            if (listeners == null) return;
            if (!explicitPort && port == Net.DefaultPort)
            {
                // a host on all addresses keeps the default port; a lone 127.0.0.1 listener on it is usually Apple's
                // service, and nothing there means the host is still starting or moved to a fallback port
                ListenerKinds(listeners, port, out _, out bool defWild);
                if (!defWild)
                {
                    for (int i = 0; i < FallbackPorts.Length; i++)
                    {
                        ListenerKinds(listeners, FallbackPorts[i], out bool l, out bool w);
                        if (!l && !w) continue;
                        Debug.Log("[Net] same-PC join: the host listens on fallback port " + FallbackPorts[i]);
                        port = FallbackPorts[i];
                        break;
                    }
                }
            }
            ListenerKinds(listeners, port, out bool loop, out bool wild);
            if (loop && wild)
            {
                string lan = NetAddresses.PrimaryLan();
                if (!string.IsNullOrEmpty(lan))
                {
                    Debug.Log("[Net] 127.0.0.1:" + port + " belongs to another program – connecting via " + lan + ":" + port);
                    host = lan;
                }
            }
        }

        public static void CancelJoin()
        {
            retryEnabled = false;
            if (Current == Phase.Connecting) FailJoin(Loc.Tr("Abgebrochen.", "Cancelled."), false);
        }

        static void FailJoin(string text, bool error = true)
        {
            var t = NetCoreRunner.Transport;
            NetCoreRunner.Transport = null;
            t?.Shutdown(0);
            Current = Phase.Idle;
            NetDiagnostics.Connected = false;
            NetDiagnostics.Transport = "offline";
            SetStatus(text, error);
        }

        internal static void OnTransportConnected(int conn)
        {
            if (Current == Phase.Hosting)
            {
                pending.Add(new Pending { conn = conn, since = Now });
                return;
            }
            if (Current != Phase.Connecting || conn != 0) return;
            var t = NetCoreRunner.Transport;
            if (t == null) return;
            SetStatus(Loc.Tr("Verbunden – warte auf den Host …", "Connected – waiting for the host …"), false);
            var w = NetCoreWriter.Begin().Int(Net.ProtocolVersion).Str(Application.version).Str(LocalName())
                .Color(Settings.Data.suitColor).Int(Settings.Data.hat).Int(Settings.Data.face);
            t.Send(0, NetCoreMsg.Hello, 0, w.ToArray(), true);
        }

        internal static void OnTransportFailed(string reason)
        {
            if (Current != Phase.Connecting) return;
            if ((reason == "refused" || reason == "timeout") && TryScheduleRetry()) return;
            retryEnabled = false;
            FailJoin(ConnectErrorText(reason));
        }

        /// Auto-retry (command line -join, the automated test): drop this attempt and try again in 2 s. The address is
        /// resolved again then (a host on this PC may have moved to a fallback port meanwhile).
        static bool TryScheduleRetry()
        {
            if (!retryEnabled || Now >= retryUntil) return false;
            var t = NetCoreRunner.Transport;
            NetCoreRunner.Transport = null;
            t?.Shutdown(0);
            Current = Phase.Idle;
            retryAt = Now + 2f;
            SetStatus(Loc.Tr($"Host ({JoinTarget}) noch nicht bereit – neuer Versuch …", $"Host ({JoinTarget}) not ready yet – retrying …"), false);
            return true;
        }

        internal static void OnTransportDisconnected(int conn, string reason)
        {
            if (Current == Phase.Hosting)
            {
                var pend = FindPending(conn);
                if (pend != null) pending.Remove(pend);
                var p = Net.Find(conn);
                if (p != null && !p.IsLocal) RemovePlayer(p, reason == "timeout" ? NetEndReason.Timeout : NetEndReason.Lost);
                return;
            }
            if (conn != 0) return;
            if (Current == Phase.Connecting)
            {
                // e.g. another program on that port hung up on our Hello
                if (TryScheduleRetry()) return;
                retryEnabled = false;
                FailJoin(Loc.Tr("Der Host hat die Verbindung getrennt.", "The host closed the connection."));
                return;
            }
            if (Current == Phase.Joining || Current == Phase.Joined)
                EndSession(NetEndReason.Lost, ReasonText(reason == "timeout" ? NetEndReason.Timeout : NetEndReason.Lost, null), true);
        }

        static void OnReject(int from, byte[] payload)
        {
            if (Current != Phase.Connecting) return;
            var r = new NetCoreReader(payload);
            var reason = (NetEndReason)r.Byte();
            string hostVersion = r.AtEnd ? "?" : r.Str();
            retryEnabled = false;
            FailJoin(ReasonText(reason, hostVersion));
        }

        static void OnWelcome(int from, byte[] payload)
        {
            if (Current != Phase.Connecting) return;
            var gm = GameManager.I;
            if (gm == null) { FailJoin("GameManager missing"); return; }
            var r = new NetCoreReader(payload);
            int myId = r.Int();
            ReadPlayers(r, tmpPlayers);
            bool near = r.Bool();
            Vector3 spawn = r.Vec3();
            float yaw = r.Float();
            r.Int();   // difficulty (also inside the state)
            bool zipped = r.Bool();
            int seq = r.Int();
            byte[] blob = r.Blob();

            GameState state;
            try { state = NetState.DecodeSnapshot(blob, zipped); }
            catch (Exception e) { state = null; NetDiagnostics.SetError("welcome state: " + e.Message); }
            if (state == null)
            {
                FailJoin(Loc.Tr("Der Spielstand des Hosts konnte nicht gelesen werden.", "Could not read the host's game state."));
                return;
            }

            Net.Mode = NetMode.Client;
            Net.LocalId = myId;
            Net.LocalName = LocalName();
            Net.Players.Clear();
            Net.Players.AddRange(tmpPlayers);
            if (Net.Find(myId) == null) Net.Players.Add(LocalInfo(myId));
            var host = Net.Find(0);
            HostName = host != null ? host.name : "Host";
            ResetFlags();
            if (!savingOverridden)
            {
                savingPrev = GameManager.SavingDisabled;
                savingOverridden = true;
            }
            GameManager.SavingDisabled = true;   // only the host saves
            Current = Phase.Joining;
            retryEnabled = false;
            Application.runInBackground = true;
            NetDiagnostics.Connected = true;
            NetDiagnostics.LastError = "";
            NetCommands.BeginClient();
            NetState.BeginClient(seq);
            SetStatus(Loc.Tr("Verbunden!", "Connected!"), false);
            Debug.Log("[Net] joined as player " + myId + " (" + Net.Players.Count + " players)");
            Started?.Invoke();
            for (int i = 0; i < Net.Players.Count; i++) if (!Net.Players[i].IsLocal) Net.RaisePlayerJoined(Net.Players[i]);
            NetLobby.Close();
            Fader.FadeThrough(() => EnterWorld(state, near, spawn, yaw), 1.4f);
        }

        static void EnterWorld(GameState state, bool near, Vector3 spawn, float yaw)
        {
            if (Current != Phase.Joining) return;   // the session ended during the fade
            var gm = GameManager.I;
            if (gm == null) return;
            gm.JoinAsClient(state);
            Current = Phase.Joined;
            NetState.ClientEnteredWorld();
            if (near) PlaceNear(spawn, yaw);
            clientToastDelay = 1.2f;
        }

        static void PlaceNear(Vector3 hostPos, float yaw)
        {
            var p = Refs.Player;
            if (p == null || p.Root == null) return;
            const float r = PlayerController.Radius, h = PlayerController.Height;
            int mask = Physics.DefaultRaycastLayers;
            for (int i = 0; i < 8; i++)
            {
                float a = (yaw + 90f + i * 45f) * Mathf.Deg2Rad;
                Vector3 c = hostPos + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 1.8f;
                // no spot behind a wall / hut post
                if (Physics.Linecast(hostPos + Vector3.up, c + Vector3.up, mask, QueryTriggerInteraction.Ignore)) continue;
                if (!Physics.Raycast(c + Vector3.up * 2.2f, Vector3.down, out RaycastHit hit, 5f, mask, QueryTriggerInteraction.Ignore)) continue;
                if (hit.normal.y <= 0.6f || hit.point.y <= Waves.SeaLevel + 0.15f) continue;
                // the same floor level as the host (never on a roof eave or down a slope) …
                if (Mathf.Abs(hit.point.y - hostPos.y) >= 1f) continue;
                // … and room to stand (a ray started inside a rock or trunk misses it and hits the ground below)
                if (Physics.CheckCapsule(hit.point + Vector3.up * (r + 0.05f), hit.point + Vector3.up * (h - r), r * 0.9f, mask, QueryTriggerInteraction.Ignore)) continue;
                p.TeleportTo(hit.point + Vector3.up * 0.05f, yaw);
                return;
            }
            // no free spot: keep the hut spawn that JoinAsClient → OnGameStarted already set
        }

        static void OnPlayerList(int from, byte[] payload)
        {
            if (!Net.IsClient) return;
            var r = new NetCoreReader(payload);
            ReadPlayers(r, tmpPlayers);
            bool announce = Current == Phase.Joined;
            for (int i = Net.Players.Count - 1; i >= 0; i--)
            {
                var old = Net.Players[i];
                bool still = false;
                for (int k = 0; k < tmpPlayers.Count; k++) if (tmpPlayers[k].id == old.id) { still = true; break; }
                if (still || old.IsLocal) continue;
                Net.Players.RemoveAt(i);
                if (NetFlags.BoatDriverId == old.id) NetFlags.BoatDriverId = -1;
                Net.RaisePlayerLeft(old);
                if (announce) GameEvents.RaiseToast(Loc.Tr($"{old.name} ist von Bord gegangen", $"{old.name} left the crew"));
            }
            for (int k = 0; k < tmpPlayers.Count; k++)
            {
                var np = tmpPlayers[k];
                var cur = Net.Find(np.id);
                if (cur == null)
                {
                    Net.Players.Add(np);
                    if (!np.IsLocal)
                    {
                        Net.RaisePlayerJoined(np);
                        if (announce) GameEvents.RaiseToast(Loc.Tr($"{np.name} ist an Bord!", $"{np.name} joined the crew!"));
                    }
                }
                else
                {
                    cur.name = np.name;
                    cur.suit = np.suit;
                    cur.hat = np.hat;
                    cur.face = np.face;
                    cur.pingMs = np.pingMs;
                }
            }
        }

        static void OnPlayerPings(int from, byte[] payload)
        {
            if (!Net.IsClient) return;
            var r = new NetCoreReader(payload);
            int n = r.Byte();
            for (int i = 0; i < n; i++)
            {
                int id = r.Int();
                int ping = r.Int();
                var p = Net.Find(id);
                if (p != null && !p.IsLocal) p.pingMs = ping;
            }
        }

        static void OnPing(int from, byte[] payload)
        {
            if (!Net.IsOnline) return;
            if (Net.IsHost && !IsMember(from)) return;
            NetBus.Send(NetCoreMsg.Pong, payload, Net.IsHost ? from : NetBus.Everyone, true);
        }

        static void OnPong(int from, byte[] payload)
        {
            if (!Net.IsOnline || payload == null || payload.Length < 8) return;
            double sent = new NetCoreReader(payload).Double();
            int ms = Mathf.Clamp((int)((Time.realtimeSinceStartupAsDouble - sent) * 1000.0), 0, 99999);
            if (Net.IsHost)
            {
                var p = Net.Find(from);
                if (p != null) p.pingMs = ms;
            }
            else
            {
                NetDiagnostics.PingMs = ms;
                var me = Net.Find(Net.LocalId);
                if (me != null) me.pingMs = ms;
            }
        }

        static void OnSessionEnd(int from, byte[] payload)
        {
            if (!Net.IsClient) return;
            var reason = payload != null && payload.Length > 0 ? (NetEndReason)payload[0] : NetEndReason.HostLeft;
            EndSession(reason, ReasonText(reason, null), true);
        }

        // =================================================================== frame
        internal static void Tick(float dt)
        {
            switch (Current)
            {
                case Phase.Hosting: TickHost(dt); break;
                case Phase.Connecting:
                    // auto-retrying joins give up on a silent attempt sooner (something else may answer on that port)
                    if (Now - connectStarted > (retryEnabled ? RetryWelcomeTimeout : WelcomeTimeout))
                    {
                        if (TryScheduleRetry()) break;
                        retryEnabled = false;
                        FailJoin(Loc.Tr("Der Host antwortet nicht (Zeitüberschreitung).", "The host does not answer (timeout)."));
                    }
                    break;
                case Phase.Joining:
                case Phase.Joined: TickClient(dt); break;
                default:
                    if (retryEnabled && Now >= retryAt)
                    {
                        if (Now > retryUntil)
                        {
                            retryEnabled = false;
                            SetStatus(ConnectErrorText("refused"), true);
                        }
                        else JoinHostPort(retryHost, retryPort, retryExplicitPort);
                    }
                    break;
            }
        }

        static void TickHost(float dt)
        {
            var t = NetCoreRunner.Transport;
            if (t == null) return;
            var gm = GameManager.I;
            bool inGame = gm != null && gm.InGame;

            for (int i = pending.Count - 1; i >= 0 && i < pending.Count; i--)
            {
                var p = pending[i];
                if (!p.hello)
                {
                    if (Now - p.since > HelloTimeout) { t.Disconnect(p.conn, "no hello"); pending.RemoveAt(i); }
                    continue;
                }
                if (inGame) Accept(p);
                else if (Now - p.since > NotReadyTimeout) Reject(p, NetEndReason.NotReady);
            }

            for (int i = Net.Players.Count - 1; i >= 0 && i < Net.Players.Count; i--)
            {
                var p = Net.Players[i];
                if (p.IsLocal) continue;
                if (t.SecondsSinceReceive(p.id) > TimeoutSeconds)
                {
                    t.Disconnect(p.id, "timeout");
                    RemovePlayer(p, NetEndReason.Timeout);
                }
            }

            pingTimer -= dt;
            if (pingTimer <= 0f)
            {
                pingTimer = 1f;
                if (Net.Players.Count > 1) SendPing();
            }
            pingListTimer -= dt;
            if (pingListTimer <= 0f)
            {
                pingListTimer = 2f;
                int worst = 0;
                var w = NetCoreWriter.Begin().Byte((byte)Net.Players.Count);
                for (int i = 0; i < Net.Players.Count; i++)
                {
                    var p = Net.Players[i];
                    w.Int(p.id).Int(p.pingMs);
                    if (!p.IsLocal && p.pingMs > worst) worst = p.pingMs;
                }
                NetDiagnostics.PingMs = worst;
                if (Net.Players.Count > 1) NetBus.Send(NetCoreMsg.PlayerPings, w.ToArray(), NetBus.Everyone, false);
            }

            if (beacon != null) beacon.Tick(dt, HostName, Net.Players.Count, HostPort);

            if (hostToastDelay > 0f)
            {
                hostToastDelay -= dt;
                if (hostToastDelay <= 0f && inGame)
                {
                    string ip = NetAddresses.PrimaryLan();
                    if (!string.IsNullOrEmpty(ip) && HostPort != Net.DefaultPort) ip += ":" + HostPort;
                    GameEvents.RaiseToast(string.IsNullOrEmpty(ip)
                        ? Loc.Tr($"Koop offen – Port {HostPort}", $"Co-op open – port {HostPort}")
                        : Loc.Tr($"Koop offen – Freunde verbinden sich mit {ip}", $"Co-op open – friends connect to {ip}"));
                    if (GameManager.SavingDisabled)
                        GameEvents.RaiseToast(Loc.Tr("Testsitzung: wird nicht gespeichert", "Test session: not saved"));
                }
            }
        }

        static void TickClient(float dt)
        {
            var t = NetCoreRunner.Transport;
            if (t == null) return;
            if (t.SecondsSinceReceive(0) > TimeoutSeconds)
            {
                EndSession(NetEndReason.Timeout, ReasonText(NetEndReason.Timeout, null), true);
                return;
            }
            pingTimer -= dt;
            if (pingTimer <= 0f)
            {
                pingTimer = 1f;
                SendPing();
            }
            if (clientToastDelay > 0f)
            {
                clientToastDelay -= dt;
                if (clientToastDelay <= 0f && Current == Phase.Joined)
                    GameEvents.RaiseToast(Loc.Tr($"Du bist in {HostName}s Spiel", $"You joined {HostName}'s game"));
            }
        }

        static void SendPing()
        {
            var w = NetCoreWriter.Begin().Double(Time.realtimeSinceStartupAsDouble);
            NetBus.Send(NetCoreMsg.Ping, w.ToArray(), NetBus.Everyone, true);
        }

        static void OnGameStarted()
        {
            if (Current == Phase.Hosting) hostToastDelay = 1.5f;
        }

        static void OnReturnedToMenu()
        {
            if (ending || Current == Phase.Idle) return;
            EndSession(NetEndReason.LocalLeft, null, false);
        }

        // =================================================================== leaving
        /// Net.Leave: client → back to the menu (no save); host → save, back to the menu, session closed for everyone.
        public static void LeaveFromUI()
        {
            if (Current == Phase.Idle) return;
            if (Current == Phase.Connecting) { CancelJoin(); return; }
            var gm = GameManager.I;
            if (gm != null && gm.InGame) gm.ReturnToMenu();   // ReturnedToMenu → EndSession
            if (Current != Phase.Idle) EndSession(NetEndReason.LocalLeft, null, false);
        }

        /// Tears the session down. notice: localized text shown over the main menu (null = none).
        /// toMenu: leave the world first (a client never saves – Net.Mode is still Client during ReturnToMenu).
        public static void EndSession(NetEndReason reason, string notice, bool toMenu)
        {
            if (ending || Current == Phase.Idle) return;
            ending = true;
            try
            {
                var t = NetCoreRunner.Transport;
                var phase = Current;
                bool wasHost = phase == Phase.Hosting;

                if (t != null)
                {
                    if (wasHost)
                    {
                        var bye = new[] { (byte)NetEndReason.HostLeft };
                        for (int i = 0; i < Net.Players.Count; i++)
                            if (!Net.Players[i].IsLocal) t.Send(Net.Players[i].id, NetCoreMsg.SessionEnd, 0, bye, true);
                        for (int i = 0; i < pending.Count; i++) t.Send(pending[i].conn, NetCoreMsg.SessionEnd, 0, bye, true);
                    }
                    else if (reason == NetEndReason.LocalLeft && (phase == Phase.Joining || phase == Phase.Joined))
                    {
                        t.Send(0, NetCoreMsg.Bye, Net.LocalId, Array.Empty<byte>(), true);
                    }
                }

                var gm = GameManager.I;
                if (toMenu && gm != null && gm.InGame) gm.ReturnToMenu();

                if (Net.IsOnline)
                {
                    tmpPlayers.Clear();
                    for (int i = 0; i < Net.Players.Count; i++) if (!Net.Players[i].IsLocal) tmpPlayers.Add(Net.Players[i]);
                    for (int i = 0; i < tmpPlayers.Count; i++) Net.RaisePlayerLeft(tmpPlayers[i]);
                    tmpPlayers.Clear();
                    Net.RaiseSessionEnded(notice ?? "");
                }

                Net.Mode = NetMode.Offline;
                Net.LocalId = 0;
                Net.Players.Clear();
                Net.CommandHandler = null;
                Net.LocalName = Settings.DisplayName;
                ResetFlags();
                if (savingOverridden)
                {
                    GameManager.SavingDisabled = savingPrev;
                    savingOverridden = false;
                }
                NetCommands.End();
                NetState.End();
                beacon?.Dispose();
                beacon = null;
                pending.Clear();
                NetCoreRunner.Transport = null;
                t?.Shutdown(wasHost ? 300 : 150);
                Current = Phase.Idle;
                hostToastDelay = -1f;
                clientToastDelay = -1f;
                retryEnabled = false;
                NetDiagnostics.Connected = false;
                NetDiagnostics.Transport = "offline";
                NetDiagnostics.PingMs = 0;
                SetStatus(notice ?? "", !string.IsNullOrEmpty(notice) && reason != NetEndReason.LocalLeft);
                Debug.Log("[Net] session ended: " + reason);
                Ended?.Invoke(notice ?? "");
                if (!string.IsNullOrEmpty(notice)) NetLobby.ShowNotice(Loc.Tr("Koop beendet", "Co-op ended"), notice);
            }
            finally
            {
                ending = false;
            }
        }

        /// Application quit: say goodbye and close the sockets, but keep Net.Mode (a client must not save on quit).
        internal static void OnQuit()
        {
            if (quitDone) return;
            quitDone = true;
            var t = NetCoreRunner.Transport;
            if (t == null) return;
            try
            {
                if (Current == Phase.Hosting)
                {
                    var bye = new[] { (byte)NetEndReason.HostLeft };
                    for (int i = 0; i < Net.Players.Count; i++)
                        if (!Net.Players[i].IsLocal) t.Send(Net.Players[i].id, NetCoreMsg.SessionEnd, 0, bye, true);
                }
                else if (Current == Phase.Joining || Current == Phase.Joined)
                {
                    t.Send(0, NetCoreMsg.Bye, Net.LocalId, Array.Empty<byte>(), true);
                }
                beacon?.Dispose();
                beacon = null;
                t.Shutdown(200);
            }
            catch (Exception) { }
        }

        // =================================================================== texts
        public static string ReasonText(NetEndReason reason, string hostVersion)
        {
            switch (reason)
            {
                case NetEndReason.HostLeft: return Loc.Tr("Der Host hat das Spiel beendet.", "The host ended the game.");
                case NetEndReason.Full: return Loc.Tr("Das Spiel ist voll (4 von 4 Tauchern).", "The game is full (4 of 4 divers).");
                case NetEndReason.VersionMismatch:
                    return Loc.Tr($"Andere Spielversion als der Host ({hostVersion ?? "?"} / du: {Application.version}). Bitte beide dieselbe Version starten.",
                                  $"Different game version than the host ({hostVersion ?? "?"} / you: {Application.version}). Both need the same version.");
                case NetEndReason.NotReady: return Loc.Tr("Der Host ist nicht ins Spiel gegangen.", "The host never entered the game.");
                case NetEndReason.Timeout: return Loc.Tr("Zeitüberschreitung – keine Antwort vom Host.", "Timed out – no answer from the host.");
                case NetEndReason.Kicked: return Loc.Tr("Du wurdest aus der Sitzung entfernt.", "You were removed from the session.");
                case NetEndReason.LocalLeft: return "";
                default: return Loc.Tr("Verbindung zum Host verloren.", "Lost the connection to the host.");
            }
        }

        static string ConnectErrorText(string reason)
        {
            switch (reason)
            {
                case "refused":
                    return Loc.Tr($"Unter {JoinTarget} läuft kein Spiel. Hat der Host auf „Spiel hosten“ gedrückt? Stimmt der Port?",
                                  $"No game running at {JoinTarget}. Did the host press “Host game”? Is the port right?");
                case "timeout":
                    return Loc.Tr($"{JoinTarget} antwortet nicht. Firewall beim Host, Portweiterleitung oder VPN prüfen.",
                                  $"{JoinTarget} does not answer. Check the host's firewall, port forwarding or VPN.");
                case "dns":
                    return Loc.Tr("Adresse nicht gefunden – bitte die IP prüfen (z. B. 192.168.1.20).", "Address not found – check the IP (e.g. 192.168.1.20).");
                case "unreachable":
                    return Loc.Tr("Netzwerk nicht erreichbar – seid ihr im selben WLAN oder VPN?", "Network unreachable – are you on the same Wi-Fi or VPN?");
                default:
                    return Loc.Tr("Verbindung fehlgeschlagen: ", "Connection failed: ") + reason;
            }
        }
    }
}
