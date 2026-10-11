using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Sealebrities
{
    /// A game found on the local network by its UDP beacon.
    public sealed class NetLanGame
    {
        public string token;
        public string name;
        public string address;
        public int port;
        public int players;
        public int maxPlayers;
        public int protocol;
        public string version;
        public float lastSeen;

        public bool Compatible => protocol == Net.ProtocolVersion && version == Application.version;
        public bool Full => players >= maxPlayers;
    }

    /// Own IPv4 addresses (shown to the host so friends know where to connect).
    public static class NetAddresses
    {
        public struct Entry
        {
            public string address;
            public string adapter;
            public bool vpn;
            /// 0 = LAN with a gateway, 1 = VPN adapter, 2 = other / virtual (VirtualBox, Hyper-V …).
            public int rank;
            public IPAddress broadcast;
        }

        static readonly string[] VpnHints = { "radmin", "zerotier", "hamachi", "tailscale", "wireguard", "openvpn", "tap-windows", "vpn" };
        static readonly string[] VirtualHints = { "virtualbox", "vmware", "hyper-v", "docker", "wsl", "host-only" };

        /// Non-loopback IPv4 addresses of interfaces that are up: real LAN first, then VPN adapters, virtual adapters last.
        public static List<Entry> LocalIPv4()
        {
            var list = new List<Entry>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    string desc = ((ni.Name ?? "") + " " + (ni.Description ?? "")).ToLowerInvariant();
                    bool vpn = false;
                    for (int i = 0; i < VpnHints.Length; i++) if (desc.Contains(VpnHints[i])) { vpn = true; break; }
                    bool virt = false;
                    for (int i = 0; i < VirtualHints.Length; i++) if (desc.Contains(VirtualHints[i])) { virt = true; break; }
                    IPInterfaceProperties props;
                    try { props = ni.GetIPProperties(); } catch (Exception) { continue; }
                    bool gateway = false;
                    try
                    {
                        foreach (var g in props.GatewayAddresses)
                            if (g.Address != null && g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)) { gateway = true; break; }
                    }
                    catch (Exception) { }
                    int rank = vpn ? 1 : gateway && !virt ? 0 : gateway ? 1 : 2;
                    foreach (var ua in props.UnicastAddresses)
                    {
                        var a = ua.Address;
                        if (a == null || a.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(a)) continue;
                        var b = a.GetAddressBytes();
                        if (b[0] == 169 && b[1] == 254) continue;   // APIPA (no DHCP)
                        IPAddress bc = null;
                        try
                        {
                            var mask = ua.IPv4Mask;
                            if (mask != null && !mask.Equals(IPAddress.Any))
                            {
                                var m = mask.GetAddressBytes();
                                var r = new byte[4];
                                for (int i = 0; i < 4; i++) r[i] = (byte)(b[i] | ~m[i]);
                                bc = new IPAddress(r);
                            }
                        }
                        catch (Exception) { }
                        list.Add(new Entry { address = a.ToString(), adapter = ni.Name, vpn = vpn, rank = rank, broadcast = bc });
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Net] could not list network interfaces: " + e.Message);
            }
            if (list.Count == 0)
            {
                try
                {
                    foreach (var a in Dns.GetHostAddresses(Dns.GetHostName()))
                        if (a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
                            list.Add(new Entry { address = a.ToString(), adapter = "", vpn = false, rank = 0 });
                }
                catch (Exception) { }
            }
            // stable sort by rank (List.Sort is not stable)
            for (int i = 1; i < list.Count; i++)
            {
                var e = list[i];
                int j = i - 1;
                while (j >= 0 && list[j].rank > e.rank) { list[j + 1] = list[j]; j--; }
                list[j + 1] = e;
            }
            return list;
        }

        // enumerating the adapters costs 10–40 ms: the primary address is cached for 30 s (also refreshed by the beacon's
        // background enumeration); TickCount is thread-safe, unlike Unity's clocks
        static volatile string primaryCache;
        static int primaryTick;

        /// The address most likely reachable by LAN friends (real LAN first), or "" when none.
        public static string PrimaryLan()
        {
            string c = primaryCache;
            if (c != null && unchecked(Environment.TickCount - primaryTick) < 30000) return c;
            return RememberPrimary(LocalIPv4());
        }

        internal static string RememberPrimary(List<Entry> l)
        {
            string r = l != null && l.Count > 0 ? l[0].address : "";
            primaryTick = Environment.TickCount;
            primaryCache = r;
            return r;
        }
    }

    /// Host side: broadcasts "SBLAN|1|protocol|version|port|players|max|token|name" on Net.DiscoveryPort every second
    /// (255.255.255.255, every interface's subnet broadcast and 127.0.0.1 for a second window on the same PC).
    public sealed class NetLanBeacon : IDisposable
    {
        public const string Magic = "SBLAN";

        readonly UdpClient udp;
        // published target list: built on a pool thread and swapped in whole, never mutated after publishing
        volatile List<IPEndPoint> targets;
        int refreshing;
        readonly string token;
        float timer;
        float refreshTargets;

        public NetLanBeacon()
        {
            udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
            token = Guid.NewGuid().ToString("N").Substring(0, 12);
            targets = BuildTargets();   // once at host start (synchronous)
        }

        static List<IPEndPoint> BuildTargets()
        {
            var l = new List<IPEndPoint>();
            l.Add(new IPEndPoint(IPAddress.Broadcast, Net.DiscoveryPort));
            l.Add(new IPEndPoint(IPAddress.Loopback, Net.DiscoveryPort));
            var adapters = NetAddresses.LocalIPv4();
            NetAddresses.RememberPrimary(adapters);
            foreach (var e in adapters)
                if (e.broadcast != null && !e.broadcast.Equals(IPAddress.Broadcast))
                    l.Add(new IPEndPoint(e.broadcast, Net.DiscoveryPort));
            return l;
        }

        /// Re-enumerates the adapters off the main thread (10–40 ms, more with VPN / Hyper-V adapters).
        void RefreshTargetsAsync()
        {
            if (Interlocked.CompareExchange(ref refreshing, 1, 0) != 0) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { targets = BuildTargets(); }
                catch (Exception) { }
                finally { Interlocked.Exchange(ref refreshing, 0); }
            });
        }

        /// Call every frame on the host.
        public void Tick(float dt, string hostName, int players, int port)
        {
            timer -= dt;
            if (timer > 0f) return;
            timer = 1f;
            refreshTargets -= 1f;
            if (refreshTargets <= 0f)
            {
                refreshTargets = 30f;   // adapters may come up later (VPN)
                RefreshTargetsAsync();
            }
            string name = (hostName ?? "").Replace('|', '/');
            if (name.Length > 24) name = name.Substring(0, 24);
            var sb = new StringBuilder(96);
            sb.Append(Magic).Append("|1|").Append(Net.ProtocolVersion).Append('|').Append(Application.version.Replace('|', '/')).Append('|')
              .Append(port).Append('|').Append(players).Append('|').Append(Net.MaxPlayers).Append('|').Append(token).Append('|').Append(name);
            byte[] data = Encoding.UTF8.GetBytes(sb.ToString());
            var list = targets;
            for (int i = 0; list != null && i < list.Count; i++)
            {
                try { udp.Send(data, data.Length, list[i]); }
                catch (Exception) { }   // an adapter without broadcast permission – ignore
            }
        }

        public void Dispose()
        {
            try { udp.Close(); } catch (Exception) { }
        }
    }

    /// Lobby side: listens for beacons on Net.DiscoveryPort (shared socket, so several windows on one PC can listen).
    public sealed class NetLanListener : IDisposable
    {
        struct Packet { public string text; public string from; }

        public readonly List<NetLanGame> Games = new List<NetLanGame>();
        public bool Available { get; private set; }
        public string Error { get; private set; }

        readonly ConcurrentQueue<Packet> inbox = new ConcurrentQueue<Packet>();
        UdpClient udp;
        volatile bool running;

        public void Start()
        {
            if (running) return;
            try
            {
                var u = new UdpClient(AddressFamily.InterNetwork);
                u.ExclusiveAddressUse = false;
                u.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                u.EnableBroadcast = true;
                u.Client.Bind(new IPEndPoint(IPAddress.Any, Net.DiscoveryPort));
                udp = u;
                running = true;
                Available = true;
                Error = null;
                new Thread(ReceiveLoop) { IsBackground = true, Name = "SB Net LAN" }.Start();
            }
            catch (Exception e)
            {
                Available = false;
                Error = e.Message;
                Debug.LogWarning("[Net] LAN search unavailable: " + e.Message);
            }
        }

        void ReceiveLoop()
        {
            var u = udp;
            while (running)
            {
                try
                {
                    var ep = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = u.Receive(ref ep);
                    if (data == null || data.Length < 8 || data.Length > 512) continue;
                    inbox.Enqueue(new Packet { text = Encoding.UTF8.GetString(data), from = ep.Address.ToString() });
                }
                catch (Exception)
                {
                    if (!running) break;
                    Thread.Sleep(20);
                }
            }
        }

        /// Main thread: applies received beacons and drops games not heard for 4 s. Returns true when the list changed.
        public bool Tick()
        {
            bool changed = false;
            float now = Time.realtimeSinceStartup;
            while (inbox.TryDequeue(out var p))
            {
                var parts = p.text.Split(new[] { '|' }, 9);
                if (parts.Length < 9 || parts[0] != NetLanBeacon.Magic || parts[1] != "1") continue;
                if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int proto)) continue;
                if (!int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int port)) continue;
                int.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int players);
                int.TryParse(parts[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out int max);
                string token = parts[7];
                NetLanGame g = null;
                for (int i = 0; i < Games.Count; i++) if (Games[i].token == token) { g = Games[i]; break; }
                if (g == null)
                {
                    g = new NetLanGame { token = token };
                    Games.Add(g);
                    changed = true;
                }
                // the same host arrives via loopback and via the LAN broadcast: prefer 127.0.0.1 (same PC, no firewall)
                bool loop = p.from == "127.0.0.1";
                if (g.address == null || loop || (g.address != "127.0.0.1" && g.address != p.from && now - g.lastSeen > 3f))
                {
                    if (g.address != p.from) changed = true;
                    g.address = p.from;
                }
                if (g.players != players || g.port != port || g.name != parts[8]) changed = true;
                g.protocol = proto;
                g.version = parts[3];
                g.port = port;
                g.players = players;
                g.maxPlayers = max > 0 ? max : Net.MaxPlayers;
                g.name = parts[8];
                g.lastSeen = now;
            }
            for (int i = Games.Count - 1; i >= 0; i--)
            {
                if (now - Games[i].lastSeen > 4f)
                {
                    Games.RemoveAt(i);
                    changed = true;
                }
            }
            return changed;
        }

        public void Dispose()
        {
            running = false;
            try { udp?.Close(); } catch (Exception) { }
            udp = null;
            Games.Clear();
            while (inbox.TryDequeue(out _)) { }
        }
    }
}
