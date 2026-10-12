using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Shared-state replication host → clients.
    /// * Snapshot (NetCoreMsg.State, reliable): the whole GameState as NetSharedState JSON (gzip above 1 KB) with the
    ///   per-player fields blanked. Sent when an allocation-free hash of the shared fields changed (checked 4×/s),
    ///   right after every client command, and as a keyframe every 10 s. Header carries "last executed command seq"
    ///   per client; a client holds a snapshot that does not yet include its own last command (max 1.5 s).
    ///   Clients apply it through GameManager.ApplyHostSnapshot (raises Money/Followers/Gear/DayStarted/Milestone/
    ///   SiteUnlocked/UploadResolved/StateSynced) and derive the remaining popups from the difference (fish sold,
    ///   sponsor page, sponsor income).
    /// * Clock (NetCoreMsg.Clock, unreliable, 2×/s): day + time of day – the clients' clocks follow the host.
    /// * Events (NetCoreMsg.Event): the host's shared-state notifications (new species, size record, fish caught,
    ///   crate opened, collectible found, sleep fade, same-day wake-up, results of a client's Buy/Faint command).
    ///   A notification is never echoed to the player who caused it (they saw it optimistically).
    /// * Round 2 (L.10): host notifications about the SHARED world (money, followers, sponsor deals, quests, story,
    ///   animal events and challenges; everything raised while a new day starts) travel as EvNotify (kind + line +
    ///   chip). Clients drop a forwarded line they showed themselves within the last seconds.
    /// The snapshot never carries the host's per-player data: player records (keys!), the host's own record, its
    /// carried item, dive footage, gallery and equipment choice are blanked before serializing.
    public static class NetState
    {
        const float SnapshotInterval = 0.25f, MinForcedInterval = 0.08f, ClockInterval = 0.5f, KeyframeInterval = 10f;
        const float AckWaitSeconds = 1.5f;
        const float NotifyDedupeSeconds = 4f;

        // event kinds inside NetCoreMsg.Event
        const byte EvToast = 1, EvSpecies = 2, EvRecord = 3, EvFish = 4, EvDelivery = 5, EvFound = 6, EvSleep = 7, EvWake = 8, EvRescue = 9,
                   EvNotify = 10;

        static bool installed, subscribed;

        // host
        static bool hosting;
        static float snapTimer, clockTimer, keyTimer, sinceLastSend;
        static int lastHash, seq, lastHostDay = -1;
        static bool force;
        static readonly NetSharedState wrapper = new NetSharedState();
        static readonly List<FootageClip> NoClips = new List<FootageClip>();
        static readonly List<PhotoEntry> NoPhotos = new List<PhotoEntry>();
        static readonly List<string> NoStrings = new List<string>();
        static readonly List<PlayerRecord> NoRecords = new List<PlayerRecord>();

        // client: notifications shown recently (dedupe of forwarded lines)
        struct RecentNotify { public NotifyKind kind; public string line, chip; public float time; }
        static readonly RecentNotify[] recent = new RecentNotify[10];
        static int recentNext;
        static bool forwarding;

        // client
        static bool client, inWorld;
        static byte[] pendingBlob;
        static bool pendingZipped;
        static int pendingSeq, pendingAck, appliedSeq;
        static float pendingSince;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            installed = false; subscribed = false; hosting = false; client = false; inWorld = false;
            snapTimer = 0f; clockTimer = 0f; keyTimer = 0f; sinceLastSend = 0f; lastHash = 0; seq = 0; lastHostDay = -1; force = false;
            pendingBlob = null; pendingZipped = false; pendingSeq = 0; pendingAck = 0; appliedSeq = 0; pendingSince = 0f;
            wrapper.state = null;
            System.Array.Clear(recent, 0, recent.Length);
            recentNext = 0;
            forwarding = false;
            localOnly = 0;
            faunaEventFrame = -1;
            heldEvents.Clear();
        }

        internal static void Install()
        {
            if (installed) return;
            installed = true;
            NetBus.Register(NetCoreMsg.State, OnState);
            NetBus.Register(NetCoreMsg.Clock, OnClock);
            NetBus.Register(NetCoreMsg.Event, OnEvent);
            GameEvents.Notify += OnAnyNotify;
        }

        internal static void BeginHost()
        {
            hosting = true;
            client = false;
            inWorld = false;
            force = true;
            lastHash = 0;
            snapTimer = 0f;
            keyTimer = KeyframeInterval;
            var gm = GameManager.I;
            lastHostDay = gm != null && gm.InGame ? gm.State.day : -1;
            Subscribe();
        }

        internal static void BeginClient(int welcomeSeq)
        {
            hosting = false;
            client = true;
            inWorld = false;
            pendingBlob = null;
            appliedSeq = welcomeSeq;
            Unsubscribe();
        }

        internal static void ClientEnteredWorld()
        {
            inWorld = true;
            TryApplyPending();
        }

        internal static void End()
        {
            Unsubscribe();
            hosting = false;
            client = false;
            inWorld = false;
            pendingBlob = null;
            wrapper.state = null;
        }

        /// Host: send a snapshot on the next frame (after commands, new day …).
        public static void ForceSnapshot() => force = true;

        internal static void Tick(float dt)
        {
            if (hosting)
            {
                FlushHeldEvents();
                TickHost(dt);
            }
            else if (client) TryApplyPending();
        }

        // =================================================================== host
        static void TickHost(float dt)
        {
            var gm = GameManager.I;
            if (gm == null || !gm.InGame || Net.Players.Count <= 1) return;   // nobody to send to (Welcome carries the state)
            sinceLastSend += dt;
            snapTimer -= dt;
            keyTimer -= dt;
            clockTimer -= dt;
            if (clockTimer <= 0f)
            {
                clockTimer = ClockInterval;
                var cw = NetCoreWriter.Begin().Int(gm.State.day).Float(gm.State.timeOfDay);
                NetBus.Send(NetCoreMsg.Clock, cw.ToArray(), NetBus.Everyone, false);
            }
            bool due = snapTimer <= 0f || (force && sinceLastSend >= MinForcedInterval);
            if (!due) return;
            snapTimer = SnapshotInterval;
            int h = StateHash(gm.State);
            if (!force && h == lastHash && keyTimer > 0f) return;
            lastHash = h;
            force = false;
            keyTimer = KeyframeInterval;
            sinceLastSend = 0f;

            byte[] blob = BuildSnapshot(out bool zipped, out int s);
            var w = NetCoreWriter.Begin().Bool(zipped).Int(s);
            NetCommands.WriteAcks(w);
            w.Blob(blob);
            byte[] payload = w.ToArray();
            NetBus.Send(NetCoreMsg.State, payload, NetBus.Everyone, true);
            NetDiagnostics.SnapshotsSent++;
            NetDiagnostics.MarkSnapshot(payload.Length);
        }

        /// Host: the shared state as packed JSON (per-player fields blanked). Also used for the Welcome message.
        internal static byte[] BuildSnapshot(out bool zipped, out int snapSeq)
        {
            var s = GameManager.I.State;
            snapSeq = ++seq;
            var dive = s.diveFootage;
            var photos = s.photos;
            var equipped = s.equipped;
            var players = s.players;
            var localRecord = s.localRecord;
            string carried = s.carried;
            string json;
            try
            {
                s.diveFootage = NoClips;
                s.photos = NoPhotos;
                s.equipped = NoStrings;
                // F.3: per-player records hold every machine's player key – they stay in the host save only
                s.players = NoRecords;
                s.localRecord = null;
                s.carried = null;
                wrapper.seq = snapSeq;
                wrapper.state = s;
                json = JsonUtility.ToJson(wrapper);
            }
            finally
            {
                s.diveFootage = dive;
                s.photos = photos;
                s.equipped = equipped;
                s.players = players;
                s.localRecord = localRecord;
                s.carried = carried;
                wrapper.state = null;
            }
            return NetCoreZip.PackText(json, out zipped);
        }

        internal static GameState DecodeSnapshot(byte[] blob, bool zipped)
        {
            string json = NetCoreZip.UnpackText(blob, zipped);
            var w = JsonUtility.FromJson<NetSharedState>(json);
            return w != null ? w.state : null;
        }

        /// Allocation-free hash over the shared fields (time of day excluded – it has its own stream).
        internal static int StateHash(GameState s)
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + s.day;
                h = h * 31 + s.money.GetHashCode();
                h = h * 31 + s.followers.GetHashCode();
                h = h * 31 + s.totalViews.GetHashCode();
                h = h * 31 + s.questIndex;
                h = h * 31 + s.fishCaught;
                h = h * 31 + (s.finaleReached ? 1 : 0);
                h = h * 31 + s.fishListedTotal;
                h = h * 31 + s.fishSold;
                h = h * 31 + s.fishMoney.GetHashCode();
                h = h * 31 + (int)s.difficulty;
                h = h * 31 + s.saveSeed;
                h = HashStrings(h, s.ownedItems);
                h = HashStrings(h, s.unlockedSites);
                h = HashStrings(h, s.reachedMilestones);
                h = HashStrings(h, s.sponsorSites);
                h = HashStrings(h, s.collected);
                h = h * 31 + (s.followerHistory != null ? s.followerHistory.Count : 0);
                h = h * 31 + (s.pendingUploads != null ? s.pendingUploads.Count : 0);
                h = h * 31 + (s.videos != null ? s.videos.Count : 0);
                var j = s.journal;
                if (j != null)
                {
                    h = h * 31 + j.Count;
                    for (int i = 0; i < j.Count; i++)
                    {
                        var e = j[i];
                        if (e == null) continue;
                        h = h * 31 + (e.speciesId != null ? e.speciesId.GetHashCode() : 0);
                        h = h * 31 + (e.discovered ? 1 : 0);
                        h = h * 31 + e.recordCm.GetHashCode();
                        h = h * 31 + e.timesFilmed;
                        h = h * 31 + e.lastUploadDay;
                        // L.2 Sealog counters
                        h = h * 31 + e.sightings;
                        h = h * 31 + e.photos;
                        h = h * 31 + e.minDepthSeen.GetHashCode();
                        h = h * 31 + e.maxDepthSeen.GetHashCode();
                        h = h * 31 + e.zoneMask;
                        h = h * 31 + (e.bestPhotoId != null ? e.bestPhotoId.GetHashCode() : 0);
                    }
                }
                h = HashRound2(h, s);
                var b = s.bankedFootage;
                if (b != null)
                {
                    h = h * 31 + b.Count;
                    for (int i = 0; i < b.Count; i++)
                        if (b[i] != null) h = h * 31 + (b[i].speciesId != null ? b[i].speciesId.GetHashCode() : 0) + b[i].quality.GetHashCode();
                }
                var d = s.deliveries;
                if (d != null)
                {
                    h = h * 31 + d.Count;
                    for (int i = 0; i < d.Count; i++)
                    {
                        if (d[i] == null) continue;
                        h = h * 31 + (d[i].itemId != null ? d[i].itemId.GetHashCode() : 0);
                        h = h * 31 + d[i].arrivalDay + d[i].arrivalTime.GetHashCode();
                        h = h * 31 + (d[i].dropped ? 1 : 0) + (d[i].express ? 2 : 0);
                        // L.12 chopper mishaps: where the crate lies and how often the goat was nudged
                        h = h * 31 + d[i].mishap;
                        h = h * 31 + d[i].dropZone;
                        h = h * 31 + d[i].nudges;
                    }
                }
                var fi = s.fishInventory;
                if (fi != null)
                {
                    h = h * 31 + fi.Count;
                    for (int i = 0; i < fi.Count; i++) if (fi[i] != null) h = h * 31 + fi[i].uid;
                }
                var fl = s.fishListings;
                if (fl != null)
                {
                    h = h * 31 + fl.Count;
                    for (int i = 0; i < fl.Count; i++) if (fl[i] != null && fl[i].fish != null) h = h * 31 + fl[i].fish.uid;
                }
                return h;
            }
        }

        /// The round-2 shared lists (J.3 / J.3b): pantry, story flags, missions, research, achievements, cosmetics,
        /// daily use, streams, rivals, bonuses, finds, challenges, sponsor deals, side quests (+ Toni), tracked quest,
        /// prologue, village lantern, Gerald days, mishap day, deal of the day. Allocation free.
        static int HashRound2(int h, GameState s)
        {
            unchecked
            {
                var pantry = s.pantry;
                if (pantry != null)
                {
                    h = h * 31 + pantry.Count;
                    for (int i = 0; i < pantry.Count; i++)
                        if (pantry[i] != null) h = h * 31 + (pantry[i].foodId != null ? pantry[i].foodId.GetHashCode() : 0) + pantry[i].count * 7;
                }
                h = HashStrings(h, s.storyFlags);
                var missions = s.missions;
                if (missions != null)
                {
                    h = h * 31 + missions.Count;
                    for (int i = 0; i < missions.Count; i++)
                        if (missions[i] != null) h = h * 31 + (missions[i].id != null ? missions[i].id.GetHashCode() : 0) + missions[i].state * 13 + missions[i].progress * 101;
                }
                var research = s.research;
                if (research != null)
                {
                    h = h * 31 + research.Count;
                    for (int i = 0; i < research.Count; i++)
                        if (research[i] != null) h = h * 31 + (research[i].id != null ? research[i].id.GetHashCode() : 0) + research[i].progress * 13 + (research[i].done ? 1 : 0);
                }
                h = HashStrings(h, s.achievements);
                h = HashStrings(h, s.cosmetics);
                h = HashStrings(h, s.dailyUsed);
                h = h * 31 + s.lastStreamDay;
                h = h * 31 + (s.streams != null ? s.streams.Count : 0);
                h = h * 31 + s.rivalStage;
                h = h * 31 + s.lastDuelDay;
                h = h * 31 + s.duelWonUntilDay;
                h = h * 31 + (s.firstVideoBonusPaid ? 1 : 0);
                h = h * 31 + s.cozyBonusDay;
                h = h * 31 + s.lastGrillDay;
                var finds = s.finds;
                if (finds != null)
                {
                    h = h * 31 + finds.Count;
                    for (int i = 0; i < finds.Count; i++)
                        if (finds[i] != null) h = h * 31 + (finds[i].id != null ? finds[i].id.GetHashCode() : 0) + (finds[i].taken ? 1 : 0) + (finds[i].delivered ? 2 : 0);
                }
                var found = s.foundItems;
                if (found != null)
                {
                    h = h * 31 + found.Count;
                    for (int i = 0; i < found.Count; i++) if (found[i] != null && found[i].id != null) h = h * 31 + found[i].id.GetHashCode();
                }
                h = HashStrings(h, s.challengesToday);
                h = HashStrings(h, s.challengesDone);
                h = h * 31 + s.challengeBonusDay;
                h = h * 31 + s.challengeBonusCount;
                var sp = s.sponsor;
                if (sp != null)
                {
                    h = HashDeals(h, sp.offers);
                    h = HashDeals(h, sp.active);
                    h = h * 31 + (sp.cooldowns != null ? sp.cooldowns.Count : 0);
                    h = h * 31 + (sp.done != null ? sp.done.Count : 0);
                    h = h * 31 + sp.failed;
                }
                var quests = s.sideQuests;
                if (quests != null)
                {
                    h = h * 31 + quests.Count;
                    for (int i = 0; i < quests.Count; i++)
                    {
                        var q = quests[i];
                        if (q == null) continue;
                        h = h * 31 + (q.id != null ? q.id.GetHashCode() : 0);
                        h = h * 31 + q.step * 17 + q.progress * 131 + (q.done ? 1 : 0);
                    }
                }
                h = h * 31 + (s.trackedQuest != null ? s.trackedQuest.GetHashCode() : 0);
                h = h * 31 + (s.prologueDone ? 1 : 0);
                h = h * 31 + s.lastLanternDay;
                h = h * 31 + s.geraldDays;
                h = h * 31 + s.lastMishapDay;
                h = h * 31 + (s.dealOfDayItem != null ? s.dealOfDayItem.GetHashCode() : 0);
                return h;
            }
        }

        static int HashDeals(int h, List<DealState> list)
        {
            unchecked
            {
                if (list == null) return h * 31;
                h = h * 31 + list.Count;
                for (int i = 0; i < list.Count; i++)
                {
                    var d = list[i];
                    if (d == null) continue;
                    h = h * 31 + (d.id != null ? d.id.GetHashCode() : 0);
                    h = h * 31 + d.progress.GetHashCode() + d.target.GetHashCode() * 7;
                    h = h * 31 + d.deadlineDay + d.expiresDay * 13;
                }
                return h;
            }
        }

        static int HashStrings(int h, List<string> list)
        {
            unchecked
            {
                if (list == null) return h * 31;
                h = h * 31 + list.Count;
                for (int i = 0; i < list.Count; i++) h = h * 31 + (list[i] != null ? list[i].GetHashCode() : 0);
                return h;
            }
        }

        // ------------------------------------------------------------------ host: event forwarding
        // ------------------------------------------------------------------ L.10 shared notifications
        static int faunaEventFrame = -1;

        struct HeldNotify { public NotifyKind kind; public string line, chip; public int frame, actor; }
        /// Event-kind lines wait one frame: an animal event's own notification (raised before or after FaunaEventInfo
        /// starts) stays local.
        static readonly List<HeldNotify> heldEvents = new List<HeldNotify>(4);

        static int localOnly;

        /// The host is executing a guest's command right now (the mutation methods raise the GUEST's own lines on the
        /// host's screen – UINotifications hides the actor-local kinds and names the actor on money / quest lines).
        public static bool HostRunningGuestCommand => Net.IsHost && NetCommands.ScopeFrom > 0;

        /// Shows a notification on THIS machine only (never forwarded to the crew) – for lines every machine already
        /// raises itself (rescue, sleep vote, co-op hints).
        public static void NotifyLocal(NotifyKind kind, string line, string chip = null)
        {
            localOnly++;
            try { GameEvents.RaiseNotify(kind, line, chip); }
            finally { localOnly--; }
        }

        /// Every notification on this machine: clients remember it (dedupe), the host forwards the shared ones.
        /// They go to EVERY client – also the one whose command caused them: consequences only the host computes
        /// („Neue Insel auf der Seekarte!“, „Hausriff komplett!“) reach it too, and the line it already showed
        /// optimistically is dropped there as a duplicate. Buy / Faint results have their own messages (EvToast, EvRescue).
        static void OnAnyNotify(NotifyKind kind, string line, string chip)
        {
            if (forwarding || string.IsNullOrEmpty(line)) return;
            Remember(kind, line, chip);
            if (localOnly > 0 || !CanForward || !ShouldForward(kind)) return;
            if (NetCommands.ScopeFrom > 0 && (NetCommands.ScopeCmd == NetCommand.Faint || NetCommands.ScopeCmd == NetCommand.Buy)) return;
            if (kind == NotifyKind.Event)
            {
                if (heldEvents.Count < 8) heldEvents.Add(new HeldNotify { kind = kind, line = line, chip = chip, frame = Time.frameCount, actor = Actor });
                return;
            }
            ForwardNotify(kind, line, chip, Actor);
        }

        /// The payload names the actor: the guest whose command caused the line drops it when it already showed the
        /// same (kind, line) itself – whatever the chip says (Challenges compute their chip from live followers).
        static void ForwardNotify(NotifyKind kind, string line, string chip, int actor)
        {
            var payload = NetCoreWriter.Begin().Byte(EvNotify).Byte((byte)kind).Str(line).Str(chip ?? "").Int(actor).ToArray();
            NetBus.Send(NetCoreMsg.Event, payload, NetBus.Everyone, true);
        }

        /// Host, every frame: releases held Event-kind lines unless an animal event started in their frame.
        static void FlushHeldEvents()
        {
            if (heldEvents.Count == 0) return;
            int now = Time.frameCount;
            for (int i = 0; i < heldEvents.Count; i++)
            {
                var h = heldEvents[i];
                if (h.frame >= now) continue;
                heldEvents.RemoveAt(i--);
                if (h.frame == faunaEventFrame || !CanForward) continue;
                ForwardNotify(h.kind, h.line, h.chip, h.actor);
            }
        }

        /// Money, followers, sponsor deals, quests, story and shared events concern the whole crew; while a new day
        /// starts (day number already advanced, DayStarted not yet raised) also the warnings (a failed deal …).
        /// Sealog / collectibles / deliveries / achievements have their own events; clips, warnings and hints are local.
        static bool ShouldForward(NotifyKind kind)
        {
            switch (kind)
            {
                case NotifyKind.Money:
                case NotifyKind.Followers:
                case NotifyKind.Sponsor:
                case NotifyKind.Quest:
                case NotifyKind.Story:
                    return true;
                case NotifyKind.Event:
                    // challenges, lanterns … – but an animal event's own line stays with the divers near it (FlushHeldEvents)
                    return true;
                case NotifyKind.Warning:
                case NotifyKind.Info:
                {
                    var gm = GameManager.I;
                    return kind == NotifyKind.Warning && gm != null && gm.InGame && lastHostDay >= 0 && gm.State.day != lastHostDay;
                }
                default:
                    return false;
            }
        }

        static void OnHostFaunaEvent(string id) => faunaEventFrame = Time.frameCount;

        static void Remember(NotifyKind kind, string line, string chip)
        {
            recent[recentNext] = new RecentNotify { kind = kind, line = line, chip = chip ?? "", time = Time.unscaledTime };
            recentNext = (recentNext + 1) % recent.Length;
        }

        /// Raises a notification unless the same line was shown in the last seconds (forwarded or derived twice).
        static void NotifyOnce(NotifyKind kind, string line, string chip)
        {
            if (SeenRecently(kind, line, chip)) return;
            GameEvents.RaiseNotify(kind, line, chip);
        }

        /// Same (kind, line) in the last seconds, any chip.
        static bool SeenRecentlyLine(NotifyKind kind, string line)
        {
            float now = Time.unscaledTime;
            for (int i = 0; i < recent.Length; i++)
            {
                var r = recent[i];
                if (r.line == null || now - r.time > NotifyDedupeSeconds) continue;
                if (r.kind == kind && r.line == line) return true;
            }
            return false;
        }

        static bool SeenRecently(NotifyKind kind, string line, string chip)
        {
            float now = Time.unscaledTime;
            chip = chip ?? "";
            for (int i = 0; i < recent.Length; i++)
            {
                var r = recent[i];
                if (r.line == null || now - r.time > NotifyDedupeSeconds) continue;
                if (r.kind == kind && r.line == line && r.chip == chip) return true;
            }
            return false;
        }

        static void Subscribe()
        {
            if (subscribed) return;
            subscribed = true;
            GameEvents.FaunaEventStarted += OnHostFaunaEvent;
            GameEvents.Toast += OnHostToast;
            GameEvents.SpeciesDiscovered += OnHostSpecies;
            GameEvents.SizeRecord += OnHostRecord;
            GameEvents.FishCaught += OnHostFish;
            GameEvents.DeliveryReceived += OnHostDelivery;
            GameEvents.SleepStarted += OnHostSleep;
            GameEvents.DayStarted += OnHostDay;
            Collectibles.Found += OnHostFound;
        }

        static void Unsubscribe()
        {
            if (!subscribed) return;
            subscribed = false;
            GameEvents.FaunaEventStarted -= OnHostFaunaEvent;
            GameEvents.Toast -= OnHostToast;
            GameEvents.SpeciesDiscovered -= OnHostSpecies;
            GameEvents.SizeRecord -= OnHostRecord;
            GameEvents.FishCaught -= OnHostFish;
            GameEvents.DeliveryReceived -= OnHostDelivery;
            GameEvents.SleepStarted -= OnHostSleep;
            GameEvents.DayStarted -= OnHostDay;
            Collectibles.Found -= OnHostFound;
        }

        static bool CanForward => hosting && Net.IsHost && Net.Players.Count > 1;

        /// The player who caused the current notification (a client inside its command, else the host).
        static int Actor => NetCommands.ScopeFrom > 0 ? NetCommands.ScopeFrom : Net.LocalId;

        /// Sends to everybody except the actor (who already saw it locally).
        static void SendToOthers(byte[] payload)
        {
            int actor = Actor;
            if (actor != Net.LocalId) NetBus.SendExcept(NetCoreMsg.Event, payload, actor, true);
            else NetBus.Send(NetCoreMsg.Event, payload, NetBus.Everyone, true);
        }

        /// Host toasts are local by default (hints, anchor, bite …). Only the results of a client's commands that the
        /// client cannot know itself travel: Buy → the buyer gets "ordered – chopper arrives at 19:30" or the refusal
        /// reason; Faint → the rescue fee goes to that client unchanged and with the name to everybody else.
        static void OnHostToast(string text)
        {
            if (!CanForward || NetCommands.ScopeFrom <= 0 || string.IsNullOrEmpty(text)) return;
            var cmd = NetCommands.ScopeCmd;
            int who = NetCommands.ScopeFrom;
            if (cmd == NetCommand.Buy)
            {
                // only a refusal reaches here (the buyer shows its own localized order confirmation)
                NetBus.Send(NetCoreMsg.Event, NetCoreWriter.Begin().Byte(EvToast).Str(text).ToArray(), who, true);
                return;
            }
            if (cmd != NetCommand.Faint) return;
            // structured: every machine formats the rescue fee in its own language
            int fee = (int)Math.Min(int.MaxValue, Math.Max(0L, GameManager.LastRescueFee));
            NetBus.Send(NetCoreMsg.Event, NetCoreWriter.Begin().Byte(EvRescue).Int(who).Int(fee).ToArray(), NetBus.Everyone, true);
        }

        static void OnHostSpecies(SpeciesDef def, float cm, SizeTier tier)
        {
            if (!CanForward || def == null) return;
            SendToOthers(NetCoreWriter.Begin().Byte(EvSpecies).Str(def.id).Float(cm).Int((int)tier).Int(Actor).ToArray());
        }

        static void OnHostRecord(SpeciesDef def, float cm, SizeTier tier)
        {
            if (!CanForward || def == null) return;
            SendToOthers(NetCoreWriter.Begin().Byte(EvRecord).Str(def.id).Float(cm).Int((int)tier).Int(Actor).ToArray());
        }

        static void OnHostFish(int value)
        {
            if (!CanForward) return;
            SendToOthers(NetCoreWriter.Begin().Byte(EvFish).Int(value).Int(Actor).ToArray());
        }

        static void OnHostDelivery(GearItem item)
        {
            if (!CanForward || item == null) return;
            SendToOthers(NetCoreWriter.Begin().Byte(EvDelivery).Str(item.id).Int(Actor).ToArray());
        }

        static void OnHostFound(string id, CollectibleKind kind)
        {
            if (!CanForward || string.IsNullOrEmpty(id)) return;
            SendToOthers(NetCoreWriter.Begin().Byte(EvFound).Str(id).Int((int)kind).Int(Actor).ToArray());
        }

        static void OnHostSleep()
        {
            ForceSnapshot();
            if (!CanForward) return;
            NetBus.Send(NetCoreMsg.Event, NetCoreWriter.Begin().Byte(EvSleep).ToArray(), NetBus.Everyone, true);
        }

        static void OnHostDay(int day)
        {
            ForceSnapshot();
            bool sameDay = day == lastHostDay;
            lastHostDay = day;
            // a sleep after midnight wakes everybody up without a new day number: tell the clients explicitly
            if (sameDay && CanForward)
                NetBus.Send(NetCoreMsg.Event, NetCoreWriter.Begin().Byte(EvWake).Int(day).ToArray(), NetBus.Everyone, true);
        }

        // =================================================================== client
        static void OnState(int from, byte[] payload)
        {
            if (!client) return;
            var r = new NetCoreReader(payload);
            bool zipped = r.Bool();
            int s = r.Int();
            int ack = NetCommands.ReadAckFor(r, Net.LocalId);
            byte[] blob = r.Blob();
            if (s <= appliedSeq) return;   // older than what we have (welcome)
            if (pendingBlob == null) pendingSince = Time.realtimeSinceStartup;
            pendingBlob = blob;
            pendingZipped = zipped;
            pendingSeq = s;
            pendingAck = ack;
            TryApplyPending();
        }

        static void TryApplyPending()
        {
            if (pendingBlob == null || !inWorld) return;
            if (pendingAck < NetCommands.LastSentSeq && Time.realtimeSinceStartup - pendingSince < AckWaitSeconds) return;
            var blob = pendingBlob;
            bool zipped = pendingZipped;
            int s = pendingSeq;
            pendingBlob = null;
            Apply(blob, zipped, s);
        }

        static void Apply(byte[] blob, bool zipped, int s)
        {
            var gm = GameManager.I;
            if (gm == null || !gm.InGame) return;
            GameState snap;
            try
            {
                snap = DecodeSnapshot(blob, zipped);
            }
            catch (Exception e)
            {
                NetDiagnostics.SetError("snapshot: " + e.Message);
                return;
            }
            if (snap == null) return;
            var old = gm.State;
            snap.EnsureValid();
            PreserveIdentity(old, snap);
            gm.ApplyHostSnapshot(snap);
            appliedSeq = s;
            NotifyDifferences(old, gm.State);
            NetDiagnostics.SnapshotsApplied++;
            NetDiagnostics.MarkSnapshot(blob.Length);
        }

        /// Keeps the client's object references for deliveries and fish (crates on the helipad and open UI rows hold
        /// them) by copying the host's values into the existing objects.
        static void PreserveIdentity(GameState old, GameState snap)
        {
            if (old == null || snap == null) return;
            var od = old.deliveries;
            var nd = snap.deliveries;
            for (int i = 0; i < nd.Count; i++)
            {
                var n = nd[i];
                if (n == null) continue;
                Delivery match = null;
                for (int k = 0; k < od.Count; k++)
                    if (od[k] != null && od[k].itemId == n.itemId) { match = od[k]; break; }
                if (match == null || match == n) continue;
                bool localDrop = match.dropped;
                match.itemId = n.itemId;
                match.arrivalDay = n.arrivalDay;
                match.arrivalTime = n.arrivalTime;
                match.express = n.express;
                match.dropped = n.dropped || localDrop;   // our own chopper may already have dropped it
                // L.12 mishaps: the host's roll, drop zone and goat nudges (the crate on every machine lies in one spot)
                match.mishap = n.mishap;
                match.dropZone = n.dropZone;
                match.nudges = Mathf.Max(match.nudges, n.nudges);
                nd[i] = match;
            }

            var oi = old.fishInventory;
            var ni = snap.fishInventory;
            for (int i = 0; i < ni.Count; i++)
            {
                var n = ni[i];
                if (n == null || n.uid == 0) continue;
                for (int k = 0; k < oi.Count; k++)
                {
                    var o = oi[k];
                    if (o == null || o.uid != n.uid || o == n) continue;
                    CopyFish(n, o);
                    ni[i] = o;
                    break;
                }
            }

            var ol = old.fishListings;
            var nl = snap.fishListings;
            for (int i = 0; i < nl.Count; i++)
            {
                var n = nl[i];
                if (n == null || n.fish == null || n.fish.uid == 0) continue;
                for (int k = 0; k < ol.Count; k++)
                {
                    var o = ol[k];
                    if (o == null || o.fish == null || o.fish.uid != n.fish.uid || o == n) continue;
                    CopyFish(n.fish, o.fish);
                    o.listedDay = n.listedDay;
                    o.listedTime = n.listedTime;
                    o.sellAt = n.sellAt;
                    nl[i] = o;
                    break;
                }
            }
        }

        static void CopyFish(FishItem from, FishItem to)
        {
            to.kindId = from.kindId;
            to.weightKg = from.weightKg;
            to.value = from.value;
            to.dayCaught = from.dayCaught;
            to.uid = from.uid;
        }

        /// Popups the host showed from its automatic processing (fish market, sponsors) – rebuilt here in the client's
        /// own language from the state difference.
        static void NotifyDifferences(GameState old, GameState cur)
        {
            if (old == null || cur == null) return;
            int sold = cur.fishSold - old.fishSold;
            if (sold > 0)
            {
                long money = Math.Max(0, cur.fishMoney - old.fishMoney);
                // L.10 style: icon + one short line + chip
                GameEvents.RaiseNotify(NotifyKind.Money,
                    sold == 1 ? Loc.Tr("Fisch verkauft", "Fish sold") : Loc.Tr($"{sold} Fische verkauft", $"{sold} fish sold"),
                    "+" + Loc.Money(money));
            }
            for (int i = 0; i < cur.sponsorSites.Count; i++)
            {
                string id = cur.sponsorSites[i];
                if (old.sponsorSites.Contains(id)) continue;
                GameEvents.RaiseSponsorUnlocked(id);
                var site = DiveSites.Get(id);
                // same line as the host's (Progression) – whichever arrives second is dropped as a duplicate
                if (site != null) NotifyOnce(NotifyKind.Sponsor, Loc.Tr($"{site.Name} komplett!", $"{site.Name} complete!"), Loc.Tr("Sponsor", "Sponsor"));
            }
            // the daily sponsor income arrives as the host's forwarded "Sponsoren zahlen" notification (new-day lines)
        }

        static void OnClock(int from, byte[] payload)
        {
            if (!client || !inWorld) return;
            var gm = GameManager.I;
            if (gm == null || !gm.InGame) return;
            var r = new NetCoreReader(payload);
            int day = r.Int();
            float t = r.Float();
            var s = gm.State;
            if (day != s.day) return;   // the new day arrives with the next snapshot
            float lag = Mathf.Clamp(NetDiagnostics.PingMs, 0, 2000) * 0.0005f / GameManager.DayLengthSeconds;
            s.timeOfDay = Mathf.Clamp(t + lag, 0f, 0.99999f);
        }

        static void OnEvent(int from, byte[] payload)
        {
            if (!client || !inWorld) return;
            var gm = GameManager.I;
            if (gm == null || !gm.InGame) return;
            var r = new NetCoreReader(payload);
            byte kind = r.Byte();
            switch (kind)
            {
                case EvToast:
                    GameEvents.RaiseToast(r.Str());
                    break;
                case EvRescue:
                {
                    int actor = r.Int();
                    long fee = r.Int();
                    if (fee <= 0) break;
                    string text = Loc.Tr("Rettung", "Rescue");
                    var p = Net.Find(actor);
                    if (actor != Net.LocalId && p != null) text = p.name + ": " + text;
                    GameEvents.RaiseNotify(NotifyKind.Money, text, "−" + Loc.Money(fee));
                    break;
                }
                case EvSpecies:
                case EvRecord:
                {
                    var def = SpeciesDatabase.Get(r.Str());
                    float cm = r.Float();
                    var tier = (SizeTier)Mathf.Clamp(r.Int(), 0, 4);
                    if (def == null) break;
                    if (kind == EvSpecies) GameEvents.RaiseSpeciesDiscovered(def, cm, tier);
                    else GameEvents.RaiseSizeRecord(def, cm, tier);
                    break;
                }
                case EvFish:
                    GameEvents.RaiseFishCaught(r.Int());
                    break;
                case EvDelivery:
                {
                    var item = GearDatabase.Get(r.Str());
                    if (item == null) break;
                    GameEvents.RaiseDeliveryReceived(item);
                    GameEvents.RaiseGearChanged();
                    GameEvents.RaiseBanner(Loc.Tr("Neu ausgerüstet: ", "New gear: ") + item.Name, new Color(1f, 0.55f, 0.1f));
                    break;
                }
                case EvFound:
                {
                    string id = r.Str();
                    var ck = (CollectibleKind)Mathf.Clamp(r.Int(), 0, (int)Collectibles.LastKind);
                    int actor = r.Int();
                    int have = Collectibles.Count(ck) + (Collectibles.IsCollected(id) ? 0 : 1);
                    int total = Collectibles.Total(ck);
                    string label = Collectibles.KindName(ck);
                    string text = label;
                    var p = Net.Find(actor);
                    if (p != null && !p.IsLocal) text = p.name + ": " + text;
                    GameEvents.RaiseNotify(NotifyKind.Collectible, text, total > 0 ? have + "/" + total : null);
                    break;
                }
                case EvSleep:
                    // L.9: the vote is used up – everybody gets up with the new morning
                    SleepVote.Reset();
                    Fader.FadeThrough(null, 2f);   // the new morning arrives with the next snapshot while black
                    Survival.ApplyNight();         // round 3: the guest's own hunger / thirst after the night
                    break;
                case EvNotify:
                {
                    var nk = (NotifyKind)Mathf.Clamp(r.Byte(), 0, (int)NotifyKind.Achievement);
                    string line = r.Str();
                    string chip = r.Str();
                    int actor = r.Int();
                    if (string.IsNullOrEmpty(line) || SeenRecently(nk, line, chip)) break;
                    if (actor == Net.LocalId && SeenRecentlyLine(nk, line)) break;   // my own optimistic line, other chip
                    forwarding = true;
                    try
                    {
                        Remember(nk, line, chip);
                        GameEvents.RaiseNotify(nk, line, string.IsNullOrEmpty(chip) ? null : chip);
                    }
                    finally { forwarding = false; }
                    break;
                }
                case EvWake:
                {
                    int day = r.Int();
                    var s = gm.State;
                    if (s.day != day) break;
                    s.timeOfDay = GameManager.MorningTime;
                    GameEvents.RaiseDayStarted(day);
                    break;
                }
            }
        }
    }
}
