using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

namespace Sealebrities
{
    /// Message types of the session/state range (0–63) owned by net-core.
    /// 64–127 avatars/boat/creatures/world (net-sync), 128–159 voice, 160+ free.
    public static class NetCoreMsg
    {
        /// Transport keep-alive, written by the send thread when idle; never dispatched.
        public const byte Heartbeat = 0;
        /// client → host: protocol, app version, name, look.
        public const byte Hello = 1;
        /// host → client: assigned id, player list, spawn hint, full shared state.
        public const byte Welcome = 2;
        /// host → client: refused (reason code + host version).
        public const byte Reject = 3;
        /// host → all: complete player list (sent on join / leave).
        public const byte PlayerList = 4;
        /// host → all: ping per player (unreliable, every 2 s).
        public const byte PlayerPings = 5;
        public const byte Ping = 6;
        public const byte Pong = 7;
        /// client → host: the client leaves.
        public const byte Bye = 8;
        /// host → all: the session is closed (reason code).
        public const byte SessionEnd = 9;
        /// client → host: NetCommand (shared-state change request).
        public const byte Command = 10;
        /// host → clients: shared GameState snapshot (NetSharedState JSON, gzip when large).
        public const byte State = 11;
        /// host → clients: day + time of day (unreliable, 2×/s).
        public const byte Clock = 12;
        /// host → clients: forwarded gameplay notification (species, fish, delivery, sleep …).
        public const byte Event = 13;

        public const byte LastCoreType = 63;
    }

    /// Reasons carried by Reject / SessionEnd (localized on the receiving machine).
    public enum NetEndReason : byte
    {
        None,
        HostLeft,           // host returned to the menu or quit
        Full,               // 4 players already
        VersionMismatch,    // different game version / protocol
        NotReady,           // host never entered the game
        Timeout,            // no data for 10 s
        Lost,               // socket error
        Kicked,
        LocalLeft,          // we left ourselves (no notice)
        ConnectFailed
    }

    /// Small binary writer for net-core payloads (little-endian, strings UTF-8 length-prefixed).
    /// Begin() returns one shared instance: main thread only, finish one message before starting the next.
    public sealed class NetCoreWriter
    {
        static readonly NetCoreWriter Shared = new NetCoreWriter();

        readonly MemoryStream ms = new MemoryStream(512);
        readonly BinaryWriter bw;

        NetCoreWriter() { bw = new BinaryWriter(ms, new UTF8Encoding(false)); }

        public static NetCoreWriter Begin()
        {
            var w = Shared;
            w.ms.Position = 0;
            w.ms.SetLength(0);
            return w;
        }

        public NetCoreWriter Byte(byte v) { bw.Write(v); return this; }
        public NetCoreWriter Bool(bool v) { bw.Write(v); return this; }
        public NetCoreWriter Int(int v) { bw.Write(v); return this; }
        public NetCoreWriter Long(long v) { bw.Write(v); return this; }
        public NetCoreWriter Float(float v) { bw.Write(v); return this; }
        public NetCoreWriter Double(double v) { bw.Write(v); return this; }
        public NetCoreWriter Str(string s) { bw.Write(s ?? ""); return this; }
        public NetCoreWriter Color(Color c) { bw.Write(c.r); bw.Write(c.g); bw.Write(c.b); return this; }
        public NetCoreWriter Vec3(Vector3 v) { bw.Write(v.x); bw.Write(v.y); bw.Write(v.z); return this; }

        public NetCoreWriter Blob(byte[] b)
        {
            int n = b != null ? b.Length : 0;
            bw.Write(n);
            if (n > 0) bw.Write(b, 0, n);
            return this;
        }

        public byte[] ToArray()
        {
            bw.Flush();
            return ms.ToArray();
        }
    }

    /// Reader matching NetCoreWriter. Throws EndOfStreamException on a short payload (NetBus.Dispatch logs it).
    public sealed class NetCoreReader
    {
        readonly BinaryReader br;
        readonly MemoryStream ms;

        public NetCoreReader(byte[] payload)
        {
            ms = new MemoryStream(payload ?? Array.Empty<byte>(), false);
            br = new BinaryReader(ms, Encoding.UTF8);
        }

        public bool AtEnd => ms.Position >= ms.Length;
        public byte Byte() => br.ReadByte();
        public bool Bool() => br.ReadBoolean();
        public int Int() => br.ReadInt32();
        public long Long() => br.ReadInt64();
        public float Float() => br.ReadSingle();
        public double Double() => br.ReadDouble();
        public string Str() => br.ReadString();
        public Color Color() { float r = br.ReadSingle(), g = br.ReadSingle(), b = br.ReadSingle(); return new Color(r, g, b, 1f); }
        public Vector3 Vec3() { float x = br.ReadSingle(), y = br.ReadSingle(), z = br.ReadSingle(); return new Vector3(x, y, z); }

        public byte[] Blob()
        {
            int n = br.ReadInt32();
            if (n < 0 || n > ms.Length - ms.Position) throw new EndOfStreamException("blob");
            return n == 0 ? Array.Empty<byte>() : br.ReadBytes(n);
        }
    }

    /// Text payload packing: UTF-8, gzip when larger than 1 KB (falls back to raw if compression is unavailable).
    public static class NetCoreZip
    {
        public const int CompressAbove = 1024;
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        /// Returns the packed bytes; zipped tells the receiver whether to Unpack with gzip.
        public static byte[] PackText(string text, out bool zipped)
        {
            byte[] raw = Utf8.GetBytes(text ?? "");
            zipped = false;
            if (raw.Length <= CompressAbove) return raw;
            try
            {
                using (var ms = new MemoryStream(raw.Length / 4 + 64))
                {
                    using (var gz = new GZipStream(ms, System.IO.Compression.CompressionLevel.Fastest, true)) gz.Write(raw, 0, raw.Length);
                    zipped = true;
                    return ms.ToArray();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Net] gzip unavailable, sending raw: " + e.Message);
                zipped = false;
                return raw;
            }
        }

        public static string UnpackText(byte[] data, bool zipped)
        {
            if (data == null || data.Length == 0) return "";
            if (!zipped) return Utf8.GetString(data);
            using (var src = new MemoryStream(data, false))
            using (var gz = new GZipStream(src, CompressionMode.Decompress))
            using (var dst = new MemoryStream(data.Length * 4))
            {
                gz.CopyTo(dst);
                return Utf8.GetString(dst.GetBuffer(), 0, (int)dst.Length);
            }
        }
    }

    /// The shared part of the game sent host → clients (the whole GameState; per-player fields are blanked on the
    /// host and ignored by GameManager.ApplyHostSnapshot on the client).
    [Serializable]
    public class NetSharedState
    {
        public int seq;
        public GameState state;
    }

    /// Live numbers of the co-op connection for tests, the F3 overlay and bug reports.
    public static class NetDiagnostics
    {
        /// Hosting with an open listener, or joined to a host.
        public static bool Connected;
        public static int PlayerCount => Net.PlayerCount;
        public static float BytesInPerSecond, BytesOutPerSecond;
        public static long BytesInTotal, BytesOutTotal;
        /// Seconds since the last state snapshot was sent (host) or applied (client); -1 = none yet.
        public static float LastSnapshotAge => lastSnapshotTime < 0f ? -1f : Time.realtimeSinceStartup - lastSnapshotTime;
        public static int LastSnapshotBytes;
        public static int SnapshotsSent, SnapshotsApplied, CommandsSent, CommandsExecuted, UnreliableDropped;
        /// Client: round trip to the host. Host: worst client.
        public static int PingMs;
        public static string LastError = "";
        /// "TCP host 0.0.0.0:27015" / "TCP client 192.168.1.20:27015" / "offline".
        public static string Transport = "offline";
        public static bool OverlayVisible;

        internal static float lastSnapshotTime = -1f;

        internal static void MarkSnapshot(int bytes)
        {
            lastSnapshotTime = Time.realtimeSinceStartup;
            LastSnapshotBytes = bytes;
        }

        internal static void SetError(string e)
        {
            LastError = e ?? "";
            if (!string.IsNullOrEmpty(e)) Debug.LogWarning("[Net] " + e);
        }

        /// Multi-line summary (allocates – overlay refreshes it twice per second).
        public static string Summary()
        {
            var sb = new StringBuilder(256);
            sb.Append("NET ").Append(Net.Mode).Append("  id ").Append(Net.LocalId).Append("  players ").Append(Net.PlayerCount).Append('/').Append(Net.MaxPlayers).Append('\n');
            sb.Append(Transport).Append(Connected ? "  (connected)" : "  (–)").Append('\n');
            sb.Append("in ").Append((BytesInPerSecond / 1024f).ToString("0.0")).Append(" KB/s  out ").Append((BytesOutPerSecond / 1024f).ToString("0.0")).Append(" KB/s  ping ").Append(PingMs).Append(" ms\n");
            float age = LastSnapshotAge;
            sb.Append("snapshot ").Append(age < 0f ? "–" : age.ToString("0.0") + " s").Append(" ago, ").Append(LastSnapshotBytes).Append(" B  sent ").Append(SnapshotsSent).Append(" applied ").Append(SnapshotsApplied).Append('\n');
            sb.Append("commands sent ").Append(CommandsSent).Append(" executed ").Append(CommandsExecuted).Append("  dropped ").Append(UnreliableDropped);
            for (int i = 0; i < Net.Players.Count; i++)
            {
                var p = Net.Players[i];
                sb.Append('\n').Append(p.IsLocal ? "* " : "  ").Append(p.id).Append(' ').Append(p.name).Append("  ").Append(p.pingMs).Append(" ms");
                if (p.IsLocal) continue;
                // F.5: how this player's voice reaches us right now (air 30 m / water 10 m / radio 200 m)
                sb.Append("  voice ").Append(NetVoice.LinkOf(p.id)).Append(' ').Append((int)NetVoice.RangeOf(p.id)).Append(" m");
                if (NetRescue.IsUnconscious(p.id)) sb.Append("  UNCONSCIOUS");
            }
            if (!string.IsNullOrEmpty(LastError)) sb.Append("\nlast error: ").Append(LastError);
            return sb.ToString();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Connected = false; BytesInPerSecond = 0f; BytesOutPerSecond = 0f; BytesInTotal = 0; BytesOutTotal = 0;
            LastSnapshotBytes = 0; SnapshotsSent = 0; SnapshotsApplied = 0; CommandsSent = 0; CommandsExecuted = 0;
            UnreliableDropped = 0; PingMs = 0; LastError = ""; Transport = "offline"; OverlayVisible = false; lastSnapshotTime = -1f;
        }
    }
}
