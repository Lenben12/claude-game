using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Opt-in for NPC-like characters that have no replication of their own: the host streams their pose and clients
    /// receive it here (the npc module's story NPCs are replicated by Npc/NpcNet.cs and do NOT implement this).
    public interface INetNpc
    {
        /// Clients, every frame while the host streams this character: world position + yaw (degrees), walking speed
        /// (m/s), talking, quest marker (0 none, 1 "!", 2 "?"). Smooth / animate it yourself.
        void ApplyNetState(Vector3 position, float yaw, float speed, bool talking, int marker);
    }

    /// Generic story bits in co-op that no other module replicates (A.2, B.1):
    /// * StoryLine (98, reliable, host → all): every line spoken with Dialogue.Say on the host (speaker, line id, args)
    ///   – each machine localizes it (Dialogue.PlayLocal). A line of a speaker with a body (NpcRegistry) is heard within
    ///   25 m of it; a line without one is a radio line: everywhere, under water only with the underwater radio,
    ///   otherwise it waits until the diver surfaces. (The npc module sends its NPC lines through Npc/NpcNet.cs and
    ///   radio calls are raised on every machine by their owners, so nothing is doubled.)
    /// * NpcState (97, unreliable, host → all, 4 Hz): pose / speed / talking / marker of every INpc that implements
    ///   INetNpc. Nothing else is touched.
    public static class NetNpcSync
    {
        const float SendInterval = 0.25f;
        const int MaxNpcs = 48;

        /// Clients: host-driven characters (INetNpc) must not walk on their own.
        public static bool Remote => Net.IsClient;

        struct Target
        {
            public Vector3 pos;
            public float yaw, speed, time;
            public bool talking;
            public byte marker;
        }

        struct Queued
        {
            public string speaker, lineId;
            public float seconds;
            public string[] args;
        }

        static readonly SyncWriter writer = new SyncWriter(512);
        static readonly SyncWriter lineWriter = new SyncWriter(256);
        static readonly Dictionary<uint, Vector3> hostLast = new Dictionary<uint, Vector3>(16);
        static readonly Dictionary<uint, Target> targets = new Dictionary<uint, Target>(16);
        static readonly List<Queued> radioQueue = new List<Queued>(16);
        static readonly string[] NoArgs = Array.Empty<string>();
        static float sendTimer, lastSendTime;
        static bool handlersOn, applying;

        // ================================================================== install / session
        internal static void Install()
        {
            if (handlersOn) return;
            handlersOn = true;
            NetBus.Register(NetSyncMsg.NpcState, OnState);
            NetBus.Register(NetSyncMsg.StoryLine, OnLine);
            Dialogue.LineSpoken += OnHostLine;
        }

        internal static void Uninstall()
        {
            if (!handlersOn) return;
            handlersOn = false;
            NetBus.Unregister(NetSyncMsg.NpcState);
            NetBus.Unregister(NetSyncMsg.StoryLine);
            Dialogue.LineSpoken -= OnHostLine;
        }

        internal static void Reset()
        {
            hostLast.Clear();
            targets.Clear();
            radioQueue.Clear();
        }

        static bool CanBroadcast => Net.IsHost && Net.Players.Count > 1;

        /// Stable 32-bit id of an NPC id string (FNV-1a, allocation free).
        public static uint Hash(string id)
        {
            unchecked
            {
                uint h = 2166136261;
                if (id != null) for (int i = 0; i < id.Length; i++) { h ^= id[i]; h *= 16777619; }
                return h;
            }
        }

        // ================================================================== frame
        internal static void Tick(float dt)
        {
            if (!Net.IsOnline) return;
            var gm = GameManager.I;
            if (gm == null || !gm.InGame) return;
            if (Net.IsHost) { if (CanBroadcast) HostTick(dt); return; }
            ClientTick();
        }

        static void HostTick(float dt)
        {
            sendTimer -= dt;
            if (sendTimer > 0f) return;
            sendTimer = SendInterval;
            float now = Time.unscaledTime;
            float interval = Mathf.Max(0.05f, now - lastSendTime);
            lastSendTime = now;
            var list = NpcRegistry.Active;
            writer.Reset();
            writer.Float(SyncTimelines.Now);
            int countPos = writer.Length;
            writer.Byte(0);
            int n = 0;
            for (int i = 0; i < list.Count && n < MaxNpcs; i++)
            {
                var npc = list[i];
                if (!(npc is INetNpc) || !NetWorldSync.Alive(npc)) continue;
                Transform root;
                try { root = npc.Root; }
                catch (Exception) { continue; }
                if (root == null || !root.gameObject.activeInHierarchy) continue;
                uint h = Hash(npc.Id);
                Vector3 p = root.position;
                float speed = 0f;
                if (hostLast.TryGetValue(h, out Vector3 last))
                {
                    Vector3 d = p - last;
                    d.y = 0f;
                    speed = d.magnitude / interval;
                }
                hostLast[h] = p;
                bool talking = false;
                int marker = 0;
                try { talking = npc.IsTalking; marker = npc.Marker; }
                catch (Exception) { }
                writer.UInt(h);
                SyncQuant.WritePos(writer, p);
                writer.UShort(SyncQuant.YawToUShort(root.eulerAngles.y));
                writer.Byte((byte)Mathf.Clamp(Mathf.RoundToInt(speed * 40f), 0, 255));
                writer.Byte(talking ? (byte)1 : (byte)0);
                writer.Byte((byte)Mathf.Clamp(marker, 0, 255));
                n++;
            }
            if (n == 0) return;
            writer.PatchByte(countPos, (byte)n);
            NetBus.Send(NetSyncMsg.NpcState, writer.ToArray(), NetBus.Everyone, false);
        }

        static void OnState(int fromId, byte[] payload)
        {
            if (Net.IsHost) return;
            var r = new SyncReader(payload);
            r.Float();   // host time (INetNpc smooths itself)
            int n = r.Byte();
            float now = Time.unscaledTime;
            for (int i = 0; i < n; i++)
            {
                uint h = r.UInt();
                Vector3 p = SyncQuant.ReadPos(ref r);
                float yaw = SyncQuant.UShortToYaw(r.UShort());
                float speed = r.Byte() / 40f;
                byte flags = r.Byte();
                byte marker = r.Byte();
                if (r.Failed) return;
                targets[h] = new Target { pos = p, yaw = yaw, speed = speed, talking = (flags & 1) != 0, marker = marker, time = now };
            }
        }

        static void ClientTick()
        {
            FlushRadioQueue();
            if (targets.Count == 0) return;
            var list = NpcRegistry.Active;
            float now = Time.unscaledTime;
            for (int i = 0; i < list.Count; i++)
            {
                if (!(list[i] is INetNpc net) || !NetWorldSync.Alive(net)) continue;
                if (!targets.TryGetValue(Hash(list[i].Id), out var t) || now - t.time > 3f) continue;
                try { net.ApplyNetState(t.pos, t.yaw, t.speed, t.talking, t.marker); }
                catch (Exception e) { Debug.LogWarning("[Net] INetNpc threw: " + e.Message); }
            }
        }

        // ================================================================== lines
        static void OnHostLine(string speaker, string lineId, float seconds, string[] args)
        {
            if (applying || !CanBroadcast || string.IsNullOrEmpty(lineId)) return;
            bool hasPos = false;
            Vector3 pos = Vector3.zero;
            var npc = NpcRegistry.Find(speaker);
            if (NetWorldSync.Alive(npc))
            {
                try
                {
                    var root = npc.Root;
                    if (root != null) { hasPos = true; pos = root.position; }
                }
                catch (Exception) { }
            }
            lineWriter.Reset();
            lineWriter.Str(speaker ?? "");
            lineWriter.Str(lineId);
            lineWriter.Float(seconds);
            int n = args != null ? Mathf.Min(args.Length, 8) : 0;
            lineWriter.Byte((byte)n);
            for (int i = 0; i < n; i++) lineWriter.Str(args[i]);
            lineWriter.Bool(hasPos);
            lineWriter.Vec3(pos);
            NetBus.Send(NetSyncMsg.StoryLine, lineWriter.ToArray(), NetBus.Everyone, true);
        }

        static void OnLine(int fromId, byte[] payload)
        {
            if (Net.IsHost) return;
            var r = new SyncReader(payload);
            string speaker = r.Str();
            string lineId = r.Str();
            float seconds = r.Float();
            int n = r.Byte();
            string[] args = n == 0 ? NoArgs : new string[n];
            for (int i = 0; i < n; i++) args[i] = r.Str();
            bool hasPos = r.Bool();
            Vector3 pos = r.Vec3();
            if (r.Failed || string.IsNullOrEmpty(lineId)) return;
            var gm = GameManager.I;
            if (gm == null || !gm.InGame) return;
            if (hasPos)
            {
                // a line of a speaker with a body: only for divers near it (A.2)
                var p = Refs.Player;
                if (NetWorldSync.Alive(p) && p.Root != null && (p.Root.position - pos).sqrMagnitude > NetCoopRules.NpcHearRange * NetCoopRules.NpcHearRange) return;
                Speak(speaker, lineId, seconds, args);
                return;
            }
            var q = new Queued { speaker = speaker, lineId = lineId, seconds = seconds, args = args };
            if (radioQueue.Count > 0 || !RadioAudible())
            {
                if (radioQueue.Count >= 16) radioQueue.RemoveAt(0);
                radioQueue.Add(q);
                return;
            }
            Speak(q.speaker, q.lineId, q.seconds, q.args);
        }

        static void FlushRadioQueue()
        {
            if (radioQueue.Count == 0 || !RadioAudible()) return;
            var q = radioQueue[0];   // one line per frame keeps the order
            radioQueue.RemoveAt(0);
            Speak(q.speaker, q.lineId, q.seconds, q.args);
        }

        /// B.1: radio lines reach a diver under water only with the underwater radio.
        static bool RadioAudible()
        {
            var p = Refs.Player;
            if (!NetWorldSync.Alive(p) || !p.IsUnderwater) return true;
            var gm = GameManager.I;
            return gm != null && gm.InGame && gm.Gear.hasRadio;
        }

        static void Speak(string speaker, string lineId, float seconds, string[] args)
        {
            applying = true;
            try { Dialogue.PlayLocal(speaker, lineId, seconds, args ?? NoArgs); }
            catch (Exception e) { Debug.LogWarning("[Net] story line " + lineId + " failed: " + e.Message); }
            finally { applying = false; }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            hostLast.Clear(); targets.Clear(); radioQueue.Clear();
            sendTimer = 0f; lastSendTime = 0f; handlersOn = false; applying = false;
        }
    }
}
