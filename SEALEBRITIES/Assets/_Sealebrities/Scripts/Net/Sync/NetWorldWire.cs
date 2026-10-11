using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Sealebrities
{
    /// Message type ids of the replication features (NetBus ranges: 64–127 avatars/boat/creatures/world, 128–159 voice).
    public static class NetSyncMsg
    {
        // avatars
        public const byte AvatarState = 64;      // unreliable, 15 Hz: originId, time, flags, pose, items (host relays)
        public const byte AvatarBump = 65;       // reliable host → client: a predator bumped you (air seconds)
        // boat
        public const byte BoatClaim = 70;        // reliable client → host: 1 claim / 0 release the driver seat
        public const byte BoatDriver = 71;       // reliable host → all: current driver id (-1 nobody)
        public const byte BoatState = 72;        // unreliable 20 Hz owner → host → all: transform + velocity
        public const byte BoatAnchor = 73;       // reliable any → host → all: anchor down/up
        // creatures (host → one client)
        public const byte CreatureSpawn = 80;    // reliable: id, species, size, tier, pose
        public const byte CreatureDespawn = 81;  // reliable: ids
        public const byte CreatureSnap = 82;     // unreliable 10 Hz: quantized poses of the creatures near that client
        public const byte CreatureResync = 83;   // reliable client → host: forget what I know, send everything again
        // round 2 (Design_Runde2 J.3 / J.3b) – shared game systems (NetGameSync)
        // 90–92: reserved for the KRILL.tv livestream – registered by the livestream module itself (Livestream/StreamNet.cs),
        // never by the Net module (one handler per id). 176–178: the npc module's NPC sync (Npc/NpcNet.cs).
        public const byte StreamState = 90;      // (livestream) reliable host → all: LiveState 2 Hz + stream summary (C.8)
        public const byte StreamFeed = 91;       // (livestream) unreliable client → host 2 Hz: that player's camera (C.8)
        public const byte StreamChat = 92;       // (livestream) reliable host → all: chat bubble line id + args
        public const byte TowState = 93;         // reliable client → host (start/stop) and host → all (every tow pair) – F.1
        public const byte Unconscious = 94;      // reliable any → host → all: unconscious on/off (+ rescuer on revive) – F.1
        public const byte Achievement = 95;      // reliable host → all: achievement id unlocked for everybody present (F.6)
        public const byte LookChanged = 96;      // reliable any → host (validates) → all via the player list: name + DiverLook (1c)
        public const byte NpcState = 97;         // unreliable host → all 4 Hz: poses of NPC-like characters that opt in (INetNpc)
        public const byte StoryLine = 98;        // reliable host → all: a Dialogue.Say line (id + args, + position of a bodied speaker)
        public const byte SleepVote = 99;        // reliable client → host: lying 1 / 0 (L.9)
        public const byte SleepState = 100;      // reliable host → all: lying, total, last player who lay down (L.9)
        public const byte FaunaEvent = 101;      // reliable host → all: animal event start / end (K.3)
        public const byte CarryState = 102;      // reliable any → host → all: carried quest item / salvage id (K.3, L.7)
        // voice
        public const byte VoiceFrame = 128;      // unreliable: originId, seq, flags, 40 ms µ-law @ 16 kHz
    }

    [StructLayout(LayoutKind.Explicit)]
    struct SyncFloatBits
    {
        [FieldOffset(0)] public float f;
        [FieldOffset(0)] public uint u;
    }

    /// Growable little-endian byte writer. Reused per sender; ToArray() copies the written bytes into a fresh array
    /// (the transport may keep a reliable payload until it is acknowledged, so payload arrays are never reused).
    public sealed class SyncWriter
    {
        byte[] buf;
        int len;

        public SyncWriter(int capacity = 256) { buf = new byte[Mathf.Max(16, capacity)]; }

        public int Length => len;
        public void Reset() { len = 0; }

        void Ensure(int extra)
        {
            if (len + extra <= buf.Length) return;
            int n = buf.Length * 2;
            while (n < len + extra) n *= 2;
            Array.Resize(ref buf, n);
        }

        public void Byte(byte v) { Ensure(1); buf[len++] = v; }
        public void SByte(sbyte v) { Ensure(1); buf[len++] = (byte)v; }
        public void Bool(bool v) { Byte(v ? (byte)1 : (byte)0); }

        public void UShort(ushort v)
        {
            Ensure(2);
            buf[len++] = (byte)v;
            buf[len++] = (byte)(v >> 8);
        }

        public void Short(short v) => UShort((ushort)v);

        public void UInt(uint v)
        {
            Ensure(4);
            buf[len++] = (byte)v;
            buf[len++] = (byte)(v >> 8);
            buf[len++] = (byte)(v >> 16);
            buf[len++] = (byte)(v >> 24);
        }

        public void Float(float v)
        {
            var b = new SyncFloatBits { f = v };
            UInt(b.u);
        }

        public void Long(long v)
        {
            UInt(unchecked((uint)v));
            UInt(unchecked((uint)(v >> 32)));
        }

        public void Vec3(Vector3 v) { Float(v.x); Float(v.y); Float(v.z); }
        public void Quat(Quaternion q) { Float(q.x); Float(q.y); Float(q.z); Float(q.w); }

        /// Velocity-like vector as 3 × int16 with the given step (e.g. 0.01 m/s).
        public void Vec3Short(Vector3 v, float step)
        {
            Short(SyncQuant.ToShort(v.x, step));
            Short(SyncQuant.ToShort(v.y, step));
            Short(SyncQuant.ToShort(v.z, step));
        }

        public void Bytes(byte[] src, int offset, int count)
        {
            Ensure(count);
            Buffer.BlockCopy(src, offset, buf, len, count);
            len += count;
        }

        /// Overwrites one already written byte (e.g. a count placeholder).
        public void PatchByte(int at, byte v) { if (at >= 0 && at < len) buf[at] = v; }

        /// UTF-8 string with a ushort byte length (max 4 KB – longer strings are cut). For rare reliable messages only.
        public void Str(string s)
        {
            if (string.IsNullOrEmpty(s)) { UShort(0); return; }
            int max = System.Text.Encoding.UTF8.GetMaxByteCount(s.Length);
            if (max > 4096) { s = s.Substring(0, 1024); max = System.Text.Encoding.UTF8.GetMaxByteCount(s.Length); }
            Ensure(2 + max);
            int n = System.Text.Encoding.UTF8.GetBytes(s, 0, s.Length, buf, len + 2);
            buf[len] = (byte)n;
            buf[len + 1] = (byte)(n >> 8);
            len += 2 + n;
        }

        /// Colour as 4 bytes (alpha kept: DiverLook uses α 0 for "default").
        public void Color32(Color c)
        {
            Byte(SyncQuant.To01(c.r)); Byte(SyncQuant.To01(c.g)); Byte(SyncQuant.To01(c.b)); Byte(SyncQuant.To01(c.a));
        }

        public byte[] ToArray()
        {
            var a = new byte[len];
            Buffer.BlockCopy(buf, 0, a, 0, len);
            return a;
        }
    }

    /// Allocation-free little-endian reader over a received payload. Reads past the end return 0 and set Failed.
    public struct SyncReader
    {
        readonly byte[] buf;
        int pos;
        public bool Failed;

        public SyncReader(byte[] payload, int start = 0)
        {
            buf = payload;
            pos = start;
            Failed = payload == null;
        }

        public int Position => pos;
        public int Remaining => buf == null ? 0 : buf.Length - pos;

        bool Need(int n)
        {
            if (buf != null && pos + n <= buf.Length) return true;
            Failed = true;
            return false;
        }

        public byte Byte() => Need(1) ? buf[pos++] : (byte)0;
        public sbyte SByte() => (sbyte)Byte();
        public bool Bool() => Byte() != 0;

        public ushort UShort()
        {
            if (!Need(2)) return 0;
            ushort v = (ushort)(buf[pos] | (buf[pos + 1] << 8));
            pos += 2;
            return v;
        }

        public short Short() => (short)UShort();

        public uint UInt()
        {
            if (!Need(4)) return 0;
            uint v = (uint)(buf[pos] | (buf[pos + 1] << 8) | (buf[pos + 2] << 16) | (buf[pos + 3] << 24));
            pos += 4;
            return v;
        }

        public float Float()
        {
            var b = new SyncFloatBits { u = UInt() };
            float f = b.f;
            return float.IsNaN(f) || float.IsInfinity(f) ? 0f : f;
        }

        public long Long()
        {
            ulong lo = UInt();
            ulong hi = UInt();
            return unchecked((long)(lo | (hi << 32)));
        }

        public Vector3 Vec3() => new Vector3(Float(), Float(), Float());

        public Quaternion Quat()
        {
            var q = new Quaternion(Float(), Float(), Float(), Float());
            float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            if (m < 1e-4f) return Quaternion.identity;
            float inv = 1f / m;
            return new Quaternion(q.x * inv, q.y * inv, q.z * inv, q.w * inv);
        }

        public Vector3 Vec3Short(float step) => new Vector3(Short() * step, Short() * step, Short() * step);

        /// Copies count bytes into dst (false when the payload is too short).
        public bool Bytes(byte[] dst, int count)
        {
            if (dst == null || count > dst.Length || !Need(count)) return false;
            Buffer.BlockCopy(buf, pos, dst, 0, count);
            pos += count;
            return true;
        }

        /// String written by SyncWriter.Str (allocates – rare reliable messages only). "" when short / empty.
        public string Str()
        {
            int n = UShort();
            if (n == 0 || !Need(n)) return "";
            string s = System.Text.Encoding.UTF8.GetString(buf, pos, n);
            pos += n;
            return s;
        }

        public Color Color32()
        {
            float r = SyncQuant.From01(Byte()), g = SyncQuant.From01(Byte()), b = SyncQuant.From01(Byte()), a = SyncQuant.From01(Byte());
            return new Color(r, g, b, a);
        }
    }

    /// Quantization helpers for compact world poses.
    public static class SyncQuant
    {
        // horizontal world range (terrain ±1024 m plus margin), vertical range (deepest seafloor .. hill tops)
        const float XZMin = -1100f, XZRange = 2200f;
        const float YMin = -200f, YRange = 270f;

        public static short ToShort(float v, float step)
        {
            float q = v / step;
            if (q > 32767f) q = 32767f;
            else if (q < -32767f) q = -32767f;
            return (short)Mathf.RoundToInt(q);
        }

        static ushort Pack(float v, float min, float range)
        {
            float t = (v - min) / range;
            if (t < 0f) t = 0f;
            else if (t > 1f) t = 1f;
            return (ushort)Mathf.RoundToInt(t * 65535f);
        }

        static float Unpack(ushort v, float min, float range) => min + v * (range / 65535f);

        /// 6 bytes: x/z ≈ 3.4 cm, y ≈ 0.4 cm resolution.
        public static void WritePos(SyncWriter w, Vector3 p)
        {
            w.UShort(Pack(p.x, XZMin, XZRange));
            w.UShort(Pack(p.y, YMin, YRange));
            w.UShort(Pack(p.z, XZMin, XZRange));
        }

        /// The quantized values WritePos would send (for change detection before sending).
        public static void PackPos(Vector3 p, out ushort x, out ushort y, out ushort z)
        {
            x = Pack(p.x, XZMin, XZRange);
            y = Pack(p.y, YMin, YRange);
            z = Pack(p.z, XZMin, XZRange);
        }

        public static void WritePackedPos(SyncWriter w, ushort x, ushort y, ushort z) { w.UShort(x); w.UShort(y); w.UShort(z); }

        /// The 4 bytes WriteRot would send, packed into one uint (yaw | pitch << 16 | roll << 24).
        public static uint PackRot(Quaternion q)
        {
            Vector3 e = q.eulerAngles;
            float pitch = e.x > 180f ? e.x - 360f : e.x;
            float roll = e.z > 180f ? e.z - 360f : e.z;
            uint yaw = (ushort)Mathf.RoundToInt(Mathf.Repeat(e.y, 360f) / 360f * 65535f);
            uint p = (byte)(sbyte)Mathf.Clamp(Mathf.RoundToInt(pitch / 90f * 127f), -127, 127);
            uint r = (byte)(sbyte)Mathf.Clamp(Mathf.RoundToInt(roll / 180f * 127f), -127, 127);
            return yaw | (p << 16) | (r << 24);
        }

        public static void WritePackedRot(SyncWriter w, uint packed)
        {
            w.UShort((ushort)(packed & 0xFFFF));
            w.Byte((byte)((packed >> 16) & 0xFF));
            w.Byte((byte)(packed >> 24));
        }

        /// True when two packed rotations differ by more than ~1° of yaw or one pitch / roll step.
        public static bool RotChanged(uint a, uint b)
        {
            if (a == b) return false;
            int dy = Mathf.Abs((int)(a & 0xFFFF) - (int)(b & 0xFFFF));
            if (dy > 32767) dy = 65536 - dy;
            return dy > 180 || ((a >> 16) & 0xFF) != ((b >> 16) & 0xFF) || (a >> 24) != (b >> 24);
        }

        public static Vector3 ReadPos(ref SyncReader r)
        {
            float x = Unpack(r.UShort(), XZMin, XZRange);
            float y = Unpack(r.UShort(), YMin, YRange);
            float z = Unpack(r.UShort(), XZMin, XZRange);
            return new Vector3(x, y, z);
        }

        /// 4 bytes: yaw (16 bit), pitch ±90° and roll ±180° (8 bit each) – covers every orientation.
        public static void WriteRot(SyncWriter w, Quaternion q)
        {
            Vector3 e = q.eulerAngles;
            float pitch = e.x > 180f ? e.x - 360f : e.x;
            float roll = e.z > 180f ? e.z - 360f : e.z;
            w.UShort((ushort)Mathf.RoundToInt(Mathf.Repeat(e.y, 360f) / 360f * 65535f));
            w.SByte((sbyte)Mathf.Clamp(Mathf.RoundToInt(pitch / 90f * 127f), -127, 127));
            w.SByte((sbyte)Mathf.Clamp(Mathf.RoundToInt(roll / 180f * 127f), -127, 127));
        }

        public static Quaternion ReadRot(ref SyncReader r)
        {
            float yaw = r.UShort() / 65535f * 360f;
            float pitch = r.SByte() / 127f * 90f;
            float roll = r.SByte() / 127f * 180f;
            return Quaternion.Euler(pitch, yaw, roll);
        }

        public static byte To01(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
        public static float From01(byte b) => b / 255f;

        public static ushort YawToUShort(float yawDeg) => (ushort)Mathf.RoundToInt(Mathf.Repeat(yawDeg, 360f) / 360f * 65535f);
        public static float UShortToYaw(ushort v) => v / 65535f * 360f;
    }
}
