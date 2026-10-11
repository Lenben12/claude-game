using System;
using System.Net;

namespace Sealebrities
{
    public enum NetTransportEventKind : byte
    {
        /// Server: a client socket was accepted. Client: the connection to the host is up.
        Connected,
        /// A connection closed (reason = short code: "closed", "lost", "timeout", "overflow", "shutdown" …).
        Disconnected,
        /// A frame arrived.
        Data,
        /// Client only: connecting failed (reason = "refused", "timeout", "dns", "unreachable" or a message).
        Failed
    }

    public struct NetTransportEvent
    {
        public NetTransportEventKind kind;
        /// Server: the client's connection id (1, 2, …). Client: always 0 (the host).
        public int connection;
        public byte type;
        /// Player id written by the sending machine (host relays keep the original client's id).
        public int sender;
        public byte[] payload;
        public string reason;
    }

    /// Byte transport between the host and up to 3 clients. TCP today (TcpNetTransport); a Steam transport
    /// (SteamNetworkingSockets / P2P relay) can implement the same interface later without touching the session code.
    /// Threading: every member is called from the Unity main thread; events are produced on worker threads and
    /// collected with Poll on the main thread.
    public interface INetTransport
    {
        bool IsServer { get; }
        bool IsActive { get; }
        /// Human readable endpoint for diagnostics ("0.0.0.0:27015").
        string Endpoint { get; }
        long BytesIn { get; }
        long BytesOut { get; }
        /// Unreliable frames dropped because a send queue was backed up.
        int DroppedUnreliable { get; }

        /// Starts listening. Throws (SocketException …) when the port cannot be opened.
        void StartServer(IPAddress bind, int port);
        /// Starts connecting in the background; result arrives as a Connected or Failed event.
        void StartClient(string host, int port, int timeoutMs);
        /// Queues one frame for a connection. reliable = false: may be merged with a newer frame of the same
        /// type/sender or dropped while that connection's send queue is backed up.
        void Send(int connection, byte type, int sender, byte[] payload, bool reliable);
        /// Flushes what is queued for the connection, then closes it.
        void Disconnect(int connection, string reason);
        /// Main thread: next pending event.
        bool Poll(out NetTransportEvent e);
        /// Seconds since anything (including heartbeats) arrived on a connection (0 when unknown).
        double SecondsSinceReceive(int connection);
        /// Flushes queues for up to flushMs, then closes everything. Safe to call twice.
        void Shutdown(int flushMs);
    }
}
