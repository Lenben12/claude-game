using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Sealebrities
{
    /// TCP implementation of INetTransport (System.Net.Sockets, NoDelay).
    /// Frame: [int32 payloadLength][byte type][int32 senderId][payload], little-endian.
    /// Per connection one receive thread (frames → ConcurrentQueue) and one send thread (double-buffered queue,
    /// 64 KB write buffer, heartbeat frame after 1 s of silence). Unreliable frames are merged with a newer frame of
    /// the same type/sender or dropped only while a queue is backed up, so voice/position streams never pile up.
    public sealed class TcpNetTransport : INetTransport
    {
        public const int HeaderSize = 9;
        public const int MaxPayload = 16 * 1024 * 1024;
        /// Queue size from which unreliable frames are merged with newer ones of the same stream.
        const int MergeAboveBytes = 48 * 1024;
        /// Queue size from which new unreliable frames are dropped.
        const int DropAboveBytes = 384 * 1024;
        /// Queue size that counts as a dead peer (reliable data cannot be dropped).
        const int OverflowBytes = 48 * 1024 * 1024;
        const double HeartbeatSeconds = 1.0;

        sealed class Conn
        {
            public int id;
            public TcpClient tcp;
            public NetworkStream stream;
            public readonly object qlock = new object();
            public List<Frame> queue = new List<Frame>(64);
            public List<Frame> spare = new List<Frame>(64);
            public int queuedBytes;
            public readonly AutoResetEvent wake = new AutoResetEvent(false);
            public int closed;                  // Interlocked: 0 open, 1 closed
            public volatile bool closeAfterFlush;
            public volatile string closeReason;
            public long lastReceive;            // Stopwatch timestamp (Interlocked)
        }

        struct Frame
        {
            public byte[] data;
            public byte type;
            public int sender;
            public bool reliable;
        }

        static readonly byte[] HeartbeatFrame = BuildFrame(NetCoreMsg.Heartbeat, 0, Array.Empty<byte>());

        readonly ConcurrentQueue<NetTransportEvent> events = new ConcurrentQueue<NetTransportEvent>();
        readonly Dictionary<int, Conn> conns = new Dictionary<int, Conn>();
        TcpListener listener;
        volatile bool active;
        bool server;
        int nextId = 1;
        long bytesIn, bytesOut;
        int dropped;
        string endpoint = "";

        public bool IsServer => server;
        public bool IsActive => active;
        public string Endpoint => endpoint;
        public long BytesIn => Interlocked.Read(ref bytesIn);
        public long BytesOut => Interlocked.Read(ref bytesOut);
        public int DroppedUnreliable => Volatile.Read(ref dropped);

        // ------------------------------------------------------------------ start
        public void StartServer(IPAddress bind, int port)
        {
            if (active) throw new InvalidOperationException("transport already active");
            server = true;
            var l = new TcpListener(bind ?? IPAddress.Any, port);
            try { l.ExclusiveAddressUse = true; } catch (Exception) { }
            l.Start(16);
            listener = l;
            active = true;
            endpoint = (bind ?? IPAddress.Any) + ":" + port;
            var t = new Thread(AcceptLoop) { IsBackground = true, Name = "SB Net accept" };
            t.Start();
        }

        public void StartClient(string host, int port, int timeoutMs)
        {
            if (active) throw new InvalidOperationException("transport already active");
            server = false;
            active = true;
            endpoint = host + ":" + port;
            var t = new Thread(() => ConnectWorker(host, port, timeoutMs)) { IsBackground = true, Name = "SB Net connect" };
            t.Start();
        }

        void AcceptLoop()
        {
            while (active)
            {
                TcpClient c;
                try
                {
                    c = listener.AcceptTcpClient();
                }
                catch (Exception)
                {
                    if (!active) break;
                    Thread.Sleep(50);
                    continue;
                }
                if (!active)
                {
                    try { c.Close(); } catch (Exception) { }
                    break;
                }
                try
                {
                    Configure(c);
                    int id;
                    lock (conns) id = nextId++;
                    var conn = NewConn(id, c);
                    lock (conns) conns[id] = conn;
                    StartThreads(conn);
                    events.Enqueue(new NetTransportEvent { kind = NetTransportEventKind.Connected, connection = id });
                }
                catch (Exception)
                {
                    try { c.Close(); } catch (Exception) { }
                }
            }
        }

        void ConnectWorker(string host, int port, int timeoutMs)
        {
            TcpClient c = null;
            try
            {
                IPAddress addr = Resolve(host);
                c = new TcpClient(addr.AddressFamily);
                var ar = c.BeginConnect(addr, port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(Math.Max(500, timeoutMs)))
                {
                    try { c.Close(); } catch (Exception) { }
                    Fail("timeout");
                    return;
                }
                c.EndConnect(ar);
                if (!active)
                {
                    c.Close();
                    return;
                }
                Configure(c);
                var conn = NewConn(0, c);
                lock (conns) conns[0] = conn;
                StartThreads(conn);
                events.Enqueue(new NetTransportEvent { kind = NetTransportEventKind.Connected, connection = 0 });
            }
            catch (SocketException e)
            {
                try { c?.Close(); } catch (Exception) { }
                switch (e.SocketErrorCode)
                {
                    case SocketError.ConnectionRefused: Fail("refused"); break;
                    case SocketError.TimedOut: Fail("timeout"); break;
                    case SocketError.HostNotFound:
                    case SocketError.NoData:
                    case SocketError.TryAgain: Fail("dns"); break;
                    case SocketError.HostUnreachable:
                    case SocketError.NetworkUnreachable:
                    case SocketError.NetworkDown: Fail("unreachable"); break;
                    default: Fail(e.SocketErrorCode.ToString()); break;
                }
            }
            catch (Exception e)
            {
                try { c?.Close(); } catch (Exception) { }
                Fail(e is ArgumentException || e is FormatException ? "dns" : e.Message);
            }
        }

        void Fail(string reason)
        {
            if (active) events.Enqueue(new NetTransportEvent { kind = NetTransportEventKind.Failed, connection = 0, reason = reason });
        }

        static IPAddress Resolve(string host)
        {
            if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("empty host");
            host = host.Trim();
            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)) return IPAddress.Loopback;
            if (IPAddress.TryParse(host, out var ip)) return ip;
            var all = Dns.GetHostAddresses(host);
            for (int i = 0; i < all.Length; i++) if (all[i].AddressFamily == AddressFamily.InterNetwork) return all[i];
            if (all.Length > 0) return all[0];
            throw new SocketException((int)SocketError.HostNotFound);
        }

        static void Configure(TcpClient c)
        {
            c.NoDelay = true;
            c.ReceiveBufferSize = 256 * 1024;
            c.SendBufferSize = 256 * 1024;
            c.SendTimeout = 8000;
        }

        static Conn NewConn(int id, TcpClient c) => new Conn
        {
            id = id,
            tcp = c,
            stream = c.GetStream(),
            lastReceive = Stopwatch.GetTimestamp()
        };

        void StartThreads(Conn conn)
        {
            new Thread(() => ReceiveLoop(conn)) { IsBackground = true, Name = "SB Net recv " + conn.id }.Start();
            new Thread(() => SendLoop(conn)) { IsBackground = true, Name = "SB Net send " + conn.id }.Start();
        }

        // ------------------------------------------------------------------ worker threads
        void ReceiveLoop(Conn conn)
        {
            var header = new byte[HeaderSize];
            string reason = "closed";
            try
            {
                var s = conn.stream;
                while (Volatile.Read(ref conn.closed) == 0)
                {
                    if (!ReadExact(s, header, HeaderSize)) break;
                    int len = header[0] | (header[1] << 8) | (header[2] << 16) | (header[3] << 24);
                    byte type = header[4];
                    int sender = header[5] | (header[6] << 8) | (header[7] << 16) | (header[8] << 24);
                    if (len < 0 || len > MaxPayload) { reason = "bad frame"; break; }
                    byte[] payload = len == 0 ? Array.Empty<byte>() : new byte[len];
                    if (len > 0 && !ReadExact(s, payload, len)) break;
                    Interlocked.Exchange(ref conn.lastReceive, Stopwatch.GetTimestamp());
                    Interlocked.Add(ref bytesIn, HeaderSize + len);
                    if (type == NetCoreMsg.Heartbeat) continue;
                    events.Enqueue(new NetTransportEvent
                    {
                        kind = NetTransportEventKind.Data,
                        connection = conn.id,
                        type = type,
                        sender = sender,
                        payload = payload
                    });
                }
            }
            catch (Exception)
            {
                reason = "lost";
            }
            Close(conn, conn.closeReason ?? reason);
        }

        static bool ReadExact(Stream s, byte[] buf, int count)
        {
            int off = 0;
            while (off < count)
            {
                int n = s.Read(buf, off, count - off);
                if (n <= 0) return false;
                off += n;
            }
            return true;
        }

        void SendLoop(Conn conn)
        {
            long lastSend = Stopwatch.GetTimestamp();
            double tick = Stopwatch.Frequency;
            try
            {
                // not disposed on purpose: disposing would close the socket before the graceful shutdown below
                var buffered = new BufferedStream(conn.stream, 64 * 1024);
                while (Volatile.Read(ref conn.closed) == 0)
                {
                    conn.wake.WaitOne(250);
                    List<Frame> batch;
                    bool flushOnly;
                    lock (conn.qlock)
                    {
                        batch = conn.queue;
                        conn.queue = conn.spare;
                        conn.spare = batch;
                        conn.queuedBytes = 0;
                        flushOnly = conn.closeAfterFlush;
                    }
                    if (batch.Count > 0)
                    {
                        long sent = 0;
                        for (int i = 0; i < batch.Count; i++)
                        {
                            var d = batch[i].data;
                            buffered.Write(d, 0, d.Length);
                            sent += d.Length;
                        }
                        batch.Clear();
                        buffered.Flush();
                        Interlocked.Add(ref bytesOut, sent);
                        lastSend = Stopwatch.GetTimestamp();
                    }
                    else if ((Stopwatch.GetTimestamp() - lastSend) / tick >= HeartbeatSeconds && !flushOnly)
                    {
                        buffered.Write(HeartbeatFrame, 0, HeartbeatFrame.Length);
                        buffered.Flush();
                        Interlocked.Add(ref bytesOut, HeartbeatFrame.Length);
                        lastSend = Stopwatch.GetTimestamp();
                    }
                    if (flushOnly)
                    {
                        bool empty;
                        lock (conn.qlock) empty = conn.queue.Count == 0;
                        if (empty) break;
                    }
                }
                if (conn.closeAfterFlush && Volatile.Read(ref conn.closed) == 0)
                {
                    // graceful: send FIN, then give the peer up to 1.5 s to close its side (closing with unread
                    // data would reset the connection and could swallow our last message on the other side)
                    try { conn.tcp.Client.Shutdown(SocketShutdown.Send); } catch (Exception) { }
                    for (int i = 0; i < 30 && Volatile.Read(ref conn.closed) == 0; i++) Thread.Sleep(50);
                }
            }
            catch (Exception)
            {
                if (conn.closeReason == null) conn.closeReason = "lost";
            }
            Close(conn, conn.closeReason ?? "closed");
        }

        void Close(Conn c, string reason)
        {
            if (Interlocked.Exchange(ref c.closed, 1) != 0) return;
            try { c.stream?.Close(); } catch (Exception) { }
            try { c.tcp?.Close(); } catch (Exception) { }
            try { c.wake.Set(); } catch (Exception) { }
            lock (conns)
            {
                if (conns.TryGetValue(c.id, out var cur) && cur == c) conns.Remove(c.id);
            }
            if (active) events.Enqueue(new NetTransportEvent { kind = NetTransportEventKind.Disconnected, connection = c.id, reason = reason });
        }

        // ------------------------------------------------------------------ main thread API
        public void Send(int connection, byte type, int sender, byte[] payload, bool reliable)
        {
            if (!active) return;
            Conn c;
            lock (conns) conns.TryGetValue(connection, out c);
            if (c == null || Volatile.Read(ref c.closed) != 0 || c.closeAfterFlush) return;
            if (payload == null) payload = Array.Empty<byte>();
            if (payload.Length > MaxPayload) return;

            // the payload is copied right away, so callers may reuse their arrays after Send returns
            byte[] frame = BuildFrame(type, sender, payload);

            bool overflow = false;
            lock (c.qlock)
            {
                var q = c.queue;
                if (!reliable && c.queuedBytes > MergeAboveBytes)
                {
                    for (int i = q.Count - 1; i >= 0; i--)
                    {
                        var f = q[i];
                        if (f.reliable || f.type != type || f.sender != sender) continue;
                        c.queuedBytes += frame.Length - f.data.Length;
                        f.data = frame;
                        q[i] = f;
                        Interlocked.Increment(ref dropped);
                        return;
                    }
                    if (c.queuedBytes > DropAboveBytes)
                    {
                        Interlocked.Increment(ref dropped);
                        return;
                    }
                }
                if (c.queuedBytes > OverflowBytes) overflow = true;
                else
                {
                    q.Add(new Frame { data = frame, type = type, sender = sender, reliable = reliable });
                    c.queuedBytes += frame.Length;
                }
            }
            if (overflow)
            {
                c.closeReason = "overflow";
                Close(c, "overflow");
                return;
            }
            c.wake.Set();
        }

        public void Disconnect(int connection, string reason)
        {
            Conn c;
            lock (conns) conns.TryGetValue(connection, out c);
            if (c == null) return;
            if (c.closeReason == null) c.closeReason = reason ?? "closed";
            c.closeAfterFlush = true;
            c.wake.Set();
        }

        public bool Poll(out NetTransportEvent e) => events.TryDequeue(out e);

        public double SecondsSinceReceive(int connection)
        {
            Conn c;
            lock (conns) conns.TryGetValue(connection, out c);
            if (c == null) return 0;
            long last = Interlocked.Read(ref c.lastReceive);
            return (Stopwatch.GetTimestamp() - last) / (double)Stopwatch.Frequency;
        }

        public void Shutdown(int flushMs)
        {
            List<Conn> all;
            lock (conns) all = new List<Conn>(conns.Values);
            if (!active && all.Count == 0) return;
            for (int i = 0; i < all.Count; i++)
            {
                all[i].closeAfterFlush = true;
                all[i].wake.Set();
            }
            if (flushMs > 0 && all.Count > 0)
            {
                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < flushMs)
                {
                    bool open = false;
                    for (int i = 0; i < all.Count; i++) if (Volatile.Read(ref all[i].closed) == 0) { open = true; break; }
                    if (!open) break;
                    Thread.Sleep(5);
                }
            }
            active = false;
            try { listener?.Stop(); } catch (Exception) { }
            listener = null;
            for (int i = 0; i < all.Count; i++) Close(all[i], "shutdown");
            while (events.TryDequeue(out _)) { }
        }

        static byte[] BuildFrame(byte type, int sender, byte[] payload)
        {
            int len = payload.Length;
            var f = new byte[HeaderSize + len];
            f[0] = (byte)len; f[1] = (byte)(len >> 8); f[2] = (byte)(len >> 16); f[3] = (byte)(len >> 24);
            f[4] = type;
            f[5] = (byte)sender; f[6] = (byte)(sender >> 8); f[7] = (byte)(sender >> 16); f[8] = (byte)(sender >> 24);
            if (len > 0) Buffer.BlockCopy(payload, 0, f, HeaderSize, len);
            return f;
        }
    }
}
