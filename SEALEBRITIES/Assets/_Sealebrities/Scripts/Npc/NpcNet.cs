using System.Globalization;
using System.Text;
using UnityEngine;

namespace Sealebrities
{
    /// Co-op replication of the npc module (A.2 "one Horst for everyone", L.8 villagers, K.5/K.8 story NPCs).
    /// Host-authoritative: the host simulates every NPC, conversation and quest; clients mirror.
    ///   176 NpcState  host → all, unreliable 4 Hz: every actor (presence, motion, gesture, marker, flags, position, yaw)
    ///                 + every story vehicle (presence, position, yaw).
    ///   177 NpcLine   host → all, reliable: one spoken line (speaker, line id, seconds, anchor actor, args) – each machine
    ///                 localizes it and shows it only within 25 m of the anchor (radio lines are local per machine).
    ///   178 NpcEvent  host → all / one, reliable: letter card, drop the carried item, gull gag, conversation hint.
    ///   client → host: NetCommand.Custom "Npc" (talk / next / skip / accept / decline / choice / evt …).
    /// The ids come from the free NetBus range (160+). The Net module's planned NpcState 97 / StoryLine 98 are not
    /// needed – NPC lines are sent here, not through Dialogue.LineSpoken.
    public static class NpcNet
    {
        public const byte MsgState = 176, MsgLine = 177, MsgEvent = 178;
        public const string CommandKey = "Npc";
        public const byte EvLetter = 1, EvDropCarry = 2, EvGullSteal = 3, EvConversation = 4, EvNotify = 5, EvPickCarry = 6;

        static readonly SyncWriter w = new SyncWriter(512);
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static bool registered;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { registered = false; }

        public static void Register()
        {
            if (registered) return;
            registered = true;
            NetBus.Register(MsgState, OnState);
            NetBus.Register(MsgLine, OnLine);
            NetBus.Register(MsgEvent, OnEvent);
            CoreCommands.RegisterCustom(CommandKey, OnCommand);
        }

        // ---------------------------------------------------------------- client → host
        /// Sends a request to the host (clients) or runs it directly (host / offline).
        public static void Request(string op, string a = null, string b = null, string c = null, string d = null)
        {
            if (Net.IsClient)
            {
                Net.Command(NetCommand.Custom, CommandKey, op ?? "", a ?? "", b ?? "", c ?? "", d ?? "");
                return;
            }
            NpcDirector.HostRequest(Net.LocalId, op, a, b, c, d);
        }

        static bool OnCommand(int fromId, string[] args)
        {
            if (args == null || args.Length < 2) return false;
            NpcDirector.HostRequest(fromId, Arg(args, 1), Arg(args, 2), Arg(args, 3), Arg(args, 4), Arg(args, 5));
            return true;
        }

        static string Arg(string[] a, int i) => i < a.Length ? a[i] : null;

        // ---------------------------------------------------------------- host → clients
        public static void SendState(NpcActor[] actors, NpcVehicle[] vehicles)
        {
            if (!Net.IsHost) return;
            w.Reset();
            w.Float(SyncTimelines.Now);
            int n = 0;
            for (int i = 0; i < actors.Length; i++) if (actors[i] != null) n++;
            w.Byte((byte)n);
            for (int i = 0; i < actors.Length; i++)
            {
                var a = actors[i];
                if (a == null) continue;
                w.Byte((byte)a.Index);
                w.Byte((byte)((a.Present ? 1 : 0) | (a.MarkerValue & 3) << 1));
                w.Byte((byte)a.Motion);
                w.Byte((byte)a.Gesture);
                w.Byte(a.GestureSeq);
                w.Byte(a.Flags);
                SyncQuant.WritePos(w, a.Position);
                w.UShort(SyncQuant.YawToUShort(a.Yaw));
            }
            int vn = vehicles != null ? vehicles.Length : 0;
            w.Byte((byte)vn);
            for (int i = 0; i < vn; i++)
            {
                var v = vehicles[i];
                w.Byte((byte)v.Index);
                w.Byte((byte)((v.Present ? 1 : 0) | (v.RotorSpeed > 1f ? 2 : 0)));
                SyncQuant.WritePos(w, v.Pos);
                w.UShort(SyncQuant.YawToUShort(v.Yaw));
            }
            NetBus.Send(MsgState, w.ToArray(), NetBus.Everyone, false);
        }

        static void OnState(int fromId, byte[] payload)
        {
            if (!Net.IsClient) return;
            var dir = NpcDirector.I;
            if (dir == null) return;
            var r = new SyncReader(payload);
            r.Float();
            int n = r.Byte();
            for (int i = 0; i < n && !r.Failed; i++)
            {
                int idx = r.Byte();
                int f = r.Byte();
                var motion = (NpcMotion)r.Byte();
                var gesture = (NpcGesture)r.Byte();
                byte seq = r.Byte();
                byte flags = r.Byte();
                Vector3 p = SyncQuant.ReadPos(ref r);
                float yaw = SyncQuant.UShortToYaw(r.UShort());
                if (r.Failed) break;
                var a = dir.ActorAt(idx);
                if (a != null) a.ApplyNet(p, yaw, (f & 1) != 0, motion, gesture, seq, (f >> 1) & 3, flags);
            }
            int vn = r.Byte();
            for (int i = 0; i < vn && !r.Failed; i++)
            {
                int idx = r.Byte();
                int f = r.Byte();
                Vector3 p = SyncQuant.ReadPos(ref r);
                float yaw = SyncQuant.UShortToYaw(r.UShort());
                if (r.Failed) break;
                var v = dir.VehicleAt(idx);
                if (v == null) continue;
                v.ApplyNet((f & 1) != 0, p, yaw);
                v.RotorSpeed = (f & 2) != 0 ? 900f : 0f;
            }
        }

        public static void SendLine(string speaker, string lineId, float seconds, int anchor, string[] args)
        {
            if (!Net.IsHost) return;
            w.Reset();
            Str(w, speaker);
            Str(w, lineId);
            w.Float(seconds);
            w.Byte((byte)(anchor < 0 ? 255 : anchor));
            int argc = args != null ? Mathf.Min(args.Length, 8) : 0;
            w.Byte((byte)argc);
            for (int i = 0; i < argc; i++) Str(w, args[i]);
            NetBus.Send(MsgLine, w.ToArray(), NetBus.Everyone, true);
        }

        static void OnLine(int fromId, byte[] payload)
        {
            if (!Net.IsClient) return;
            var r = new SyncReader(payload);
            string speaker = ReadStr(ref r), id = ReadStr(ref r);
            float seconds = r.Float();
            int anchor = r.Byte();
            int argc = r.Byte();
            string[] args = null;
            if (argc > 0)
            {
                args = new string[argc];
                for (int i = 0; i < argc; i++) args[i] = ReadStr(ref r);
            }
            if (r.Failed) return;
            NpcDirector.ClientLine(speaker, id, seconds, anchor == 255 ? -1 : anchor, args);
        }

        public static void SendEvent(byte ev, string a, string b, int target = NetBus.Everyone)
        {
            if (!Net.IsHost) return;
            w.Reset();
            w.Byte(ev);
            Str(w, a);
            Str(w, b);
            NetBus.Send(MsgEvent, w.ToArray(), target, true);
        }

        static void OnEvent(int fromId, byte[] payload)
        {
            if (!Net.IsClient) return;
            var r = new SyncReader(payload);
            byte ev = r.Byte();
            string a = ReadStr(ref r), b = ReadStr(ref r);
            if (r.Failed) return;
            NpcDirector.ClientEvent(ev, a, b);
        }

        // ---------------------------------------------------------------- strings
        static void Str(SyncWriter sw, string s)
        {
            if (string.IsNullOrEmpty(s)) { sw.UShort(0); return; }
            var bytes = Encoding.UTF8.GetBytes(s);
            int n = Mathf.Min(bytes.Length, 4000);
            sw.UShort((ushort)n);
            sw.Bytes(bytes, 0, n);
        }

        static string ReadStr(ref SyncReader r)
        {
            int n = r.UShort();
            if (n == 0 || r.Failed) return "";
            var buf = new byte[n];
            if (!r.Bytes(buf, n)) return "";
            return Encoding.UTF8.GetString(buf);
        }

        public static string I(int v) => v.ToString(Inv);
        public static string F(float v) => v.ToString("0.##", Inv);
        public static int ParseI(string s, int def = 0) => s != null && int.TryParse(s, NumberStyles.Integer, Inv, out int v) ? v : def;
        public static float ParseF(string s, float def = 0f) => s != null && float.TryParse(s, NumberStyles.Float, Inv, out float v) && !float.IsNaN(v) && !float.IsInfinity(v) ? v : def;
    }
}
