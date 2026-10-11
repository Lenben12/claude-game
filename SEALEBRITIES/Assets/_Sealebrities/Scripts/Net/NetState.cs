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
    public static class NetState
    {
        const float SnapshotInterval = 0.25f, MinForcedInterval = 0.08f, ClockInterval = 0.5f, KeyframeInterval = 10f;
        const float AckWaitSeconds = 1.5f;

        // event kinds inside NetCoreMsg.Event
        const byte EvToast = 1, EvSpecies = 2, EvRecord = 3, EvFish = 4, EvDelivery = 5, EvFound = 6, EvSleep = 7, EvWake = 8, EvRescue = 9;

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
        }

        internal static void Install()
        {
            if (installed) return;
            installed = true;
            NetBus.Register(NetCoreMsg.State, OnState);
            NetBus.Register(NetCoreMsg.Clock, OnClock);
            NetBus.Register(NetCoreMsg.Event, OnEvent);
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
            if (hosting) TickHost(dt);
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
            string json;
            try
            {
                s.diveFootage = NoClips;
                s.photos = NoPhotos;
                s.equipped = NoStrings;
                wrapper.seq = snapSeq;
                wrapper.state = s;
                json = JsonUtility.ToJson(wrapper);
            }
            finally
            {
                s.diveFootage = dive;
                s.photos = photos;
                s.equipped = equipped;
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
                    }
                }
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
        static void Subscribe()
        {
            if (subscribed) return;
            subscribed = true;
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
                GameEvents.RaiseToast(sold == 1
                    ? Loc.Tr($"+{Loc.Money(money)} – Fisch verkauft", $"+{Loc.Money(money)} – fish sold")
                    : Loc.Tr($"+{Loc.Money(money)} – {sold} Fische verkauft", $"+{Loc.Money(money)} – {sold} fish sold"));
            }
            for (int i = 0; i < cur.sponsorSites.Count; i++)
            {
                string id = cur.sponsorSites[i];
                if (old.sponsorSites.Contains(id)) continue;
                GameEvents.RaiseSponsorUnlocked(id);
                var site = DiveSites.Get(id);
                if (site != null)
                    GameEvents.RaiseBanner(Loc.Tr($"Sealog-Seite „{site.Name}“ komplett!\nEin Sponsor meldet sich (aktiv ab {Loc.Number(Progression.SponsorFollowers)} Followern).",
                                                  $"Sealog page “{site.Name}” complete!\nA sponsor gets in touch (active from {Loc.Number(Progression.SponsorFollowers)} followers)."),
                                           new Color(0.24f, 0.86f, 0.52f));
            }
            if (cur.day > old.day)
            {
                long sponsor = Progression.DailySponsorIncome();
                if (sponsor > 0) GameEvents.RaiseToast(Loc.Tr($"Sponsoren zahlen {Loc.Money(sponsor)}", $"Sponsors pay {Loc.Money(sponsor)}"));
            }
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
                    string text = Loc.Tr($"Rettung: −{Loc.Money(fee)}", $"Rescue: −{Loc.Money(fee)}");
                    var p = Net.Find(actor);
                    if (actor != Net.LocalId && p != null) text = p.name + ": " + text;
                    GameEvents.RaiseToast(text);
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
                    var ck = (CollectibleKind)Mathf.Clamp(r.Int(), 0, (int)CollectibleKind.Secret);
                    int actor = r.Int();
                    int have = Collectibles.Count(ck) + (Collectibles.IsCollected(id) ? 0 : 1);
                    int total = Collectibles.Total(ck);
                    string label = Collectibles.KindName(ck);
                    string text = total > 0 ? label + " (" + have + "/" + total + ")" : label;
                    var p = Net.Find(actor);
                    if (p != null && !p.IsLocal) text = p.name + ": " + text;
                    GameEvents.RaiseToast(text);
                    break;
                }
                case EvSleep:
                    Fader.FadeThrough(null, 2f);   // the new morning arrives with the next snapshot while black
                    break;
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
