using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// L.9 sleeping in co-op – everybody into bed, „Schlafen 1/2 · 2/2“.
    /// Clients: SleepVote.SetLying → SleepVote.SendHandler → SleepVote (99) to the host → SleepVote.HostSet.
    /// Host: counts (core SleepVote.Tick) and broadcasts SleepState (100: lying, total, who lay down last) 2× per second
    /// while anybody lies, immediately on every change, and once more when it drops to 0. Clients apply it
    /// (SleepVote.ApplyState) and show „{name} will schlafen“ once per player who lies down.
    public static class NetSleepSync
    {
        const float Interval = 0.5f;

        static readonly SyncWriter writer = new SyncWriter(8);
        static float timer;
        static int sentLying = -1, sentTotal = -1, lastLier = -1;
        static bool dirty, handlersOn;
        static readonly List<int> knownLying = new List<int>(4);

        internal static void Install()
        {
            if (handlersOn) return;
            handlersOn = true;
            NetBus.Register(NetSyncMsg.SleepVote, OnVote);
            NetBus.Register(NetSyncMsg.SleepState, OnState);
            SleepVote.SendHandler = SendVote;
            SleepVote.Changed += OnChanged;
        }

        internal static void Uninstall()
        {
            if (!handlersOn) return;
            handlersOn = false;
            NetBus.Unregister(NetSyncMsg.SleepVote);
            NetBus.Unregister(NetSyncMsg.SleepState);
            if (SleepVote.SendHandler == (System.Action<bool>)SendVote) SleepVote.SendHandler = null;
            SleepVote.Changed -= OnChanged;
        }

        static void OnChanged() => dirty = true;

        internal static void Reset()
        {
            sentLying = -1;
            sentTotal = -1;
            lastLier = -1;
            knownLying.Clear();
            dirty = false;
            SleepVote.Reset();
        }

        internal static void OnPlayerJoined(NetPlayerInfo p)
        {
            if (Net.IsHost && p != null && !p.IsLocal && SleepVote.Lying > 0) Broadcast(p.id);
        }

        internal static void OnPlayerLeft(NetPlayerInfo p)
        {
            if (p != null) knownLying.Remove(p.id);
            if (Net.IsHost) dirty = true;   // the total drops – recounted by SleepVote.Tick
        }

        // ------------------------------------------------------------------ client → host
        static void SendVote(bool lying)
        {
            if (!Net.IsClient) return;
            writer.Reset();
            writer.Bool(lying);
            NetBus.Send(NetSyncMsg.SleepVote, writer.ToArray(), NetBus.Everyone, true);
        }

        static void OnVote(int fromId, byte[] payload)
        {
            if (!Net.IsHost || fromId == Net.LocalId || !NetSession.IsMember(fromId)) return;
            var r = new SyncReader(payload);
            bool lying = r.Bool();
            if (r.Failed) return;
            if (lying && !knownLying.Contains(fromId)) { knownLying.Add(fromId); lastLier = fromId; }
            else if (!lying) knownLying.Remove(fromId);
            SleepVote.HostSet(fromId, lying);
            dirty = true;
        }

        // ------------------------------------------------------------------ host → all
        internal static void Tick(float dt)
        {
            if (!Net.IsHost || Net.Players.Count <= 1) return;
            // the host's own vote (SetLying → HostSet) has no message: notice it here
            bool hostLying = SleepVote.LocalLying;
            if (hostLying && !knownLying.Contains(Net.LocalId)) { knownLying.Add(Net.LocalId); lastLier = Net.LocalId; dirty = true; }
            else if (!hostLying && knownLying.Remove(Net.LocalId)) dirty = true;
            if (SleepVote.Lying == 0 && knownLying.Count > 0 && !hostLying)
            {
                // the vote was reset (night skipped, wake-up): forget who lay down
                knownLying.Clear();
                dirty = true;
            }
            timer -= dt;
            bool active = SleepVote.Lying > 0;
            bool changed = SleepVote.Lying != sentLying || SleepVote.Total != sentTotal;
            // changes go out at once, a running vote is refreshed 2×/s (unchanged and nobody lying: silence)
            bool due = changed ? (dirty || timer <= 0f) : (active && timer <= 0f);
            if (!due) return;
            timer = Interval;
            dirty = false;
            Broadcast(NetBus.Everyone);
        }

        static void Broadcast(int target)
        {
            writer.Reset();
            writer.Byte((byte)Mathf.Clamp(SleepVote.Lying, 0, 255));
            writer.Byte((byte)Mathf.Clamp(SleepVote.Total, 0, 255));
            writer.SByte((sbyte)Mathf.Clamp(lastLier, -1, 127));
            NetBus.Send(NetSyncMsg.SleepState, writer.ToArray(), target, true);
            if (target == NetBus.Everyone)
            {
                sentLying = SleepVote.Lying;
                sentTotal = SleepVote.Total;
            }
        }

        static void OnState(int fromId, byte[] payload)
        {
            if (Net.IsHost) return;
            var r = new SyncReader(payload);
            int lying = r.Byte();
            int total = r.Byte();
            int lier = r.SByte();
            if (r.Failed) return;
            int before = SleepVote.Lying;
            SleepVote.ApplyState(lying, total);
            // (the local "lying" ends with the night skip – NetState resets the vote when the host's sleep fade starts)
            if (lying > before && lier >= 0 && lier != Net.LocalId)
            {
                var p = Net.Find(lier);
                string name = p != null && !string.IsNullOrEmpty(p.name) ? p.name : Loc.Tr("Jemand", "Someone");
                NetState.NotifyLocal(NotifyKind.Info, Loc.Tr($"{name} will schlafen", $"{name} wants to sleep"), lying + "/" + total);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            timer = 0f; sentLying = -1; sentTotal = -1; lastLier = -1; dirty = false; handlersOn = false;
            knownLying.Clear();
        }
    }

    /// F.6 achievements for everyone present: the host announces every new channel achievement (Achievement 95) the
    /// moment GameState.achievements grows; each machine shows the notification and keeps its own list
    /// (Achievements.ApplyLocal – idempotent, the snapshot diff ends there as well).
    public static class NetAchievementSync
    {
        static readonly SyncWriter writer = new SyncWriter(64);
        static List<string> watched;
        static int sent;
        static bool handlersOn;

        internal static void Install()
        {
            if (handlersOn) return;
            handlersOn = true;
            NetBus.Register(NetSyncMsg.Achievement, OnAchievement);
        }

        internal static void Uninstall()
        {
            if (!handlersOn) return;
            handlersOn = false;
            NetBus.Unregister(NetSyncMsg.Achievement);
        }

        internal static void Reset() { watched = null; sent = 0; }

        internal static void Tick()
        {
            if (!Net.IsHost) { watched = null; return; }
            var gm = GameManager.I;
            if (gm == null || !gm.InGame) { watched = null; return; }
            var list = gm.State.achievements;
            if (!ReferenceEquals(list, watched))
            {
                // new game / loaded save / session started mid-game: only unlocks from now on are news
                watched = list;
                sent = list.Count;
                return;
            }
            if (list.Count <= sent) { sent = list.Count; return; }
            for (int i = sent; i < list.Count; i++)
            {
                if (string.IsNullOrEmpty(list[i])) continue;
                writer.Reset();
                writer.Str(list[i]);
                NetBus.Send(NetSyncMsg.Achievement, writer.ToArray(), NetBus.Everyone, true);
            }
            sent = list.Count;
        }

        static void OnAchievement(int fromId, byte[] payload)
        {
            if (Net.IsHost) return;
            var r = new SyncReader(payload);
            string id = r.Str();
            if (r.Failed || string.IsNullOrEmpty(id)) return;
            var gm = GameManager.I;
            if (gm == null || !gm.InGame) return;
            // keep the shared list in step until the next snapshot (no double notification from the diff)
            if (!gm.State.achievements.Contains(id)) gm.State.achievements.Add(id);
            Achievements.ApplyLocal(id);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { watched = null; sent = 0; handlersOn = false; }
    }

    /// K.3 animal events (feeding frenzy, cleaning station, big school …). The host's fauna module runs them; every
    /// client mirrors them into FaunaEventInfo (FaunaEvent 101) so its clips get the eventId bonus and the UI shows
    /// the notification. FaunaEventInfo changes on the host are broadcast automatically; when the fauna module runs
    /// events in several zones at once (one per diver's zone), it announces the extra ones with Announce / Retire.
    /// Clients keep up to MaxEvents and show the active event nearest to their diver.
    public static class NetFaunaEvents
    {
        public const int MaxEvents = 6;

        struct Ev
        {
            public string id, zone;
            public Vector3 center;
            public float endsAt;   // Time.time on this machine
        }

        static readonly List<Ev> events = new List<Ev>(MaxEvents);
        static readonly SyncWriter writer = new SyncWriter(64);
        static string hostSentId;
        static Vector3 hostSentCenter;
        static float pickTimer;
        static bool handlersOn, applying;

        internal static void Install()
        {
            if (handlersOn) return;
            handlersOn = true;
            NetBus.Register(NetSyncMsg.FaunaEvent, OnEvent);
            FaunaEventInfo.Changed += OnInfoChanged;
        }

        internal static void Uninstall()
        {
            if (!handlersOn) return;
            handlersOn = false;
            NetBus.Unregister(NetSyncMsg.FaunaEvent);
            FaunaEventInfo.Changed -= OnInfoChanged;
        }

        internal static void Reset()
        {
            events.Clear();
            hostSentId = null;
            if (!Net.HasAuthority && !string.IsNullOrEmpty(FaunaEventInfo.ActiveId))
            {
                applying = true;
                FaunaEventInfo.End();
                applying = false;
            }
        }

        // ------------------------------------------------------------------ host API (fauna)
        /// Host: an additional event runs (e.g. in another diver's zone). Clients near it will show it.
        public static void Announce(string id, string zoneId, Vector3 center, float secondsLeft)
        {
            if (!Net.IsHost || string.IsNullOrEmpty(id)) return;
            Store(id, zoneId, center, secondsLeft);
            Send(true, id, zoneId, center, secondsLeft, NetBus.Everyone);
        }

        /// Host: an announced event is over.
        public static void Retire(string id)
        {
            if (!Net.IsHost || string.IsNullOrEmpty(id)) return;
            Remove(id);
            Send(false, id, null, Vector3.zero, 0f, NetBus.Everyone);
        }

        static void OnInfoChanged()
        {
            if (applying || !Net.IsHost) return;
            if (FaunaEventInfo.IsActive)
            {
                float left = FaunaEventInfo.EndsAt - Time.time;
                hostSentId = FaunaEventInfo.ActiveId;
                hostSentCenter = FaunaEventInfo.Center;
                Store(hostSentId, FaunaEventInfo.ZoneId, hostSentCenter, left);
                Send(true, hostSentId, FaunaEventInfo.ZoneId, hostSentCenter, left, NetBus.Everyone);
            }
            else if (!string.IsNullOrEmpty(hostSentId))
            {
                Remove(hostSentId);
                Send(false, hostSentId, null, Vector3.zero, 0f, NetBus.Everyone);
                hostSentId = null;
            }
        }

        internal static void OnPlayerJoined(NetPlayerInfo p)
        {
            if (!Net.IsHost || p == null || p.IsLocal) return;
            float now = Time.time;
            for (int i = 0; i < events.Count; i++)
            {
                var e = events[i];
                if (e.endsAt > now) Send(true, e.id, e.zone, e.center, e.endsAt - now, p.id);
            }
        }

        static void Send(bool start, string id, string zone, Vector3 center, float secondsLeft, int target)
        {
            writer.Reset();
            writer.Bool(start);
            writer.Str(id);
            writer.Str(zone ?? "");
            writer.Vec3(center);
            writer.Float(Mathf.Max(0f, secondsLeft));
            NetBus.Send(NetSyncMsg.FaunaEvent, writer.ToArray(), target, true);
        }

        static void Store(string id, string zone, Vector3 center, float secondsLeft)
        {
            Remove(id);
            if (events.Count >= MaxEvents) events.RemoveAt(0);
            events.Add(new Ev { id = id, zone = zone, center = center, endsAt = Time.time + Mathf.Clamp(secondsLeft, 0f, 600f) });
        }

        static void Remove(string id)
        {
            for (int i = events.Count - 1; i >= 0; i--) if (events[i].id == id) events.RemoveAt(i);
        }

        static void OnEvent(int fromId, byte[] payload)
        {
            if (Net.IsHost) return;
            var r = new SyncReader(payload);
            bool start = r.Bool();
            string id = r.Str();
            string zone = r.Str();
            Vector3 center = r.Vec3();
            float left = r.Float();
            if (r.Failed || string.IsNullOrEmpty(id)) return;
            if (start) Store(id, zone, center, left);
            else Remove(id);
            pickTimer = 0f;
        }

        /// Clients: FaunaEventInfo = the running event nearest to the local diver (2×/s).
        internal static void Tick(float dt)
        {
            if (Net.HasAuthority) return;
            pickTimer -= dt;
            if (pickTimer > 0f) return;
            pickTimer = 0.5f;
            float now = Time.time;
            for (int i = events.Count - 1; i >= 0; i--) if (events[i].endsAt <= now) events.RemoveAt(i);
            Vector3 me = Vector3.zero;
            var p = Refs.Player;
            bool havePos = NetWorldSync.Alive(p) && p.Root != null;
            if (havePos) me = p.Root.position;
            int best = -1;
            float bd = float.MaxValue;
            for (int i = 0; i < events.Count; i++)
            {
                float d = havePos ? (events[i].center - me).sqrMagnitude : i;
                if (d < bd) { bd = d; best = i; }
            }
            applying = true;
            try
            {
                if (best < 0)
                {
                    if (!string.IsNullOrEmpty(FaunaEventInfo.ActiveId)) FaunaEventInfo.End();
                    return;
                }
                var e = events[best];
                if (FaunaEventInfo.ActiveId == e.id && (FaunaEventInfo.Center - e.center).sqrMagnitude < 1f)
                {
                    FaunaEventInfo.EndsAt = e.endsAt;
                    return;
                }
                if (!string.IsNullOrEmpty(FaunaEventInfo.ActiveId)) FaunaEventInfo.End();
                FaunaEventInfo.Start(e.id, e.zone, e.center, e.endsAt - now);
            }
            finally { applying = false; }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            events.Clear(); hostSentId = null; hostSentCenter = Vector3.zero; pickTimer = 0f; handlersOn = false; applying = false;
        }
    }
}
