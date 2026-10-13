using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Sealebrities
{
    [Serializable]
    public class JournalEntry
    {
        public string speciesId;
        public bool discovered;
        public float recordCm;
        public int recordTier;          // (int)SizeTier of the record
        public int timesFilmed;         // legacy (round 1 registrations) – kept for old saves
        public float bestClipValue;
        public int firstDay;
        public int lastUploadDay = -99; // for the novelty penalty on repeated uploads

        // ---- round 2b (L.2 Sealog counters)
        /// Individuals detected while recording (became a clip) or photographed.
        public int sightings;
        /// Sealog photos of this species.
        public int photos;
        /// Depth range where you saw it (−1 = never seen).
        public float minDepthSeen = -1f, maxDepthSeen;
        /// Bit per zone (Zones.Bit) where it was seen.
        public int zoneMask;
        public int lastSeenDay;
        /// Gallery file name of the best Sealog photo (PhotoEntry.file on the machine that took it).
        public string bestPhotoId;
        public float bestPhotoQuality;

        public bool HasDepthSeen => minDepthSeen >= 0f;
    }

    [Serializable]
    public class FootageClip
    {
        public string speciesId;
        public float sizeCm;
        public int tier;                // (int)SizeTier
        public float quality;           // 0.1 .. 1.6 (framing x light x steadiness x camera)
        public float seconds;           // usable seconds captured
        public float action;            // 0..1 average activity during the clip
        public bool isRecord;           // was a new size record when filmed
        public bool isNewSpecies;       // first time this species was filmed
        public int day;
        public string siteId;

        // ---- round 2b (K.2 / K.3 / K.4)
        /// Most other animals detected at the same time (0..8): value × (1 + 0.04 × crowd).
        public int crowd;
        /// Animal event id (K.3) when filmed during an event within 20 m of its centre: value × 1.25.
        public string eventId;
        /// Filmed in the selfie view with the diver in frame: value × 1.3 (K.4).
        public bool selfie;
        /// The selfie expression fitted the scene (Scared + predator, Wow + new/Huge/Legendary): another × 1.15.
        public bool selfieMatch;
        /// Depth in meters where it was filmed.
        public float depth;

        public SizeTier Tier => (SizeTier)tier;
        public SpeciesDef Species => SpeciesDatabase.Get(speciesId);
        public float Value => Progression.ClipValue(this);
    }

    /// JSON wrapper for sending a list of clips (JsonUtility cannot serialize a bare list).
    [Serializable]
    public class ClipBatch
    {
        public List<FootageClip> clips = new List<FootageClip>();
    }

    [Serializable]
    public class PendingUpload
    {
        public int dayUploaded;
        public int titleStyle;          // (int)TitleStyle
        public string title;
        public List<FootageClip> clips = new List<FootageClip>();
        // ---- L.4
        /// Species shown in the collage thumbnail (max 5).
        public string[] thumbSpecies;
        /// Local PNG of the collage (thumbs/day_{n}.png – only valid on the machine that composed it).
        public string thumbPath;
        /// The player typed the title (not a suggestion chip).
        public bool customTitle;
        /// 0..1 thumbnail quality (Q31/L.4): views × (1 + 0.15 × thumbQuality).
        public float thumbQuality;
    }

    [Serializable]
    public class VideoRecord
    {
        public int day;
        public string title;
        public int titleStyle;
        public long views;
        public long followersGained;
        public long money;
        public int clipCount;
        public string topSpeciesId;
        // ---- L.4 / K.1
        public string[] thumbSpecies;
        public string thumbPath;
        public bool customTitle;
        /// Starter boost multiplier applied to this video's money (K.1, 1 = none).
        public float starterBoost = 1f;
        /// One-time creator starter bonus paid with this video.
        public long starterBonus;
    }

    [Serializable]
    public class Delivery
    {
        public string itemId;           // gear id, or "food:<id>" for a snack pack (FoodDatabase.DeliveryPrefix)
        public int arrivalDay;
        public float arrivalTime;       // time of day 0..1
        public bool express;
        public bool dropped;            // crate is on the helipad, waiting for pickup
        // ---- L.12 mishaps (rolled deterministically by the Boat module's HeliService)
        public int mishap;              // (int)DeliveryMishap
        /// Index into PropInfo.DropZones for DeliveryMishap.WrongPlace (−1 = none).
        public int dropZone = -1;
        /// Goat mishap: nudges so far (3 needed).
        public int nudges;

        public DeliveryMishap Mishap => (DeliveryMishap)mishap;
    }

    // ------------------------------------------------------------------ round 2 shared state (J.3 / J.3b)

    /// Per-player data kept in the HOST save for co-op guests (F.3) – and the local player's own counters.
    [Serializable]
    public class PlayerRecord
    {
        /// Settings.PlayerKey of that machine (random, never the Steam id, never logged).
        public string key;
        public string name;
        public DiverLook look;
        /// Legacy 3 + 3 hotbar (records of round-2 saves; migrated on ApplyToLocal).
        public string[] hotbarRight, hotbarLeft;
        // ---- round 3 (v5): per-player hotbar, backpack, survival
        public string[] hotbar;
        public int hotbarOffMask;
        public List<InvStack> backpack = new List<InvStack>();
        public List<InvStack> pouch = new List<InvStack>();
        public bool backpackInit;
        public float hunger = 100f, thirst = 100f;
        public List<string> equipped = new List<string>();
        public List<string> achievements = new List<string>();
        public int fishCaught, speciesFilmed, rescues, dives, faints;
        public float playMinutes;
        public int lastSeenDay;
        // ---- round 2b counters for achievements (coconut10, grill_master, snap, clickbait10 …) and carry
        public int coconuts, grilled, snaps, photos;
        /// Carried quest item / salvage id (null = hands free) – K.3, L.7.
        public string carried;
    }

    [Serializable] public class PantryItem { public string foodId; public int count; }

    /// Gus missions M1–M4 (B.2): state 0 locked, 1 offered/active, 2 done.
    [Serializable] public class MissionState { public string id; public int state; public int progress; }

    /// Dr. Fin research tasks (B.4, K.3).
    [Serializable] public class ResearchState { public string id; public int progress; public bool done; }

    /// Today's underwater finds (K.3). Cleared every new day (ids carry the day anyway).
    [Serializable] public class FindState { public string id; public bool taken; public bool delivered; }

    /// A find waiting in the Fish Market app's „Funde“ tab.
    [Serializable] public class FoundItem { public string id; public string kindId; public long value; public int day; }

    /// Side quest state (L.7) – also used for Toni's chain T1–T4 ("toni").
    [Serializable] public class QuestState { public string id; public int step; public int progress; public bool done; public int startedDay; }

    /// A sponsor deal offer or an active deal (K.7). Templates live in Data/SponsorDatabase.cs (livestream).
    [Serializable]
    public class DealState
    {
        /// Instance id (unique in this save, e.g. "d_shark3#17").
        public string id;
        /// Template id (SponsorDatabase).
        public string dealId;
        public string sponsorId;
        /// Topic tags ("shark", "turtle", "ray", "octopus", "school", "wreck", "night", "depth", "fishing", "livestream",
        /// "island", "coral", "whale") – two ACTIVE deals may not share one.
        public string[] tags;
        public long reward;
        public int days;
        public int offeredDay;
        /// Offers expire at the start of this day.
        public int expiresDay;
        public int acceptedDay;
        /// Active deals fail when this day starts unfinished.
        public int deadlineDay;
        /// Free progress counters for SponsorService (e.g. clips counted, seconds below 30 m).
        public float progress, target;
    }

    [Serializable] public class SponsorCooldown { public string sponsorId; public int untilDay; }

    [Serializable]
    public class SponsorState
    {
        /// Open offers (max Deals.MaxOffers).
        public List<DealState> offers = new List<DealState>();
        /// Running deals (max Deals.MaxActive).
        public List<DealState> active = new List<DealState>();
        /// Sponsors sulking after a cancel (3 days).
        public List<SponsorCooldown> cooldowns = new List<SponsorCooldown>();
        /// Finished deal template ids (history).
        public List<string> done = new List<string>();
        public int failed;
        /// Counter for unique instance ids.
        public int nextId;
    }

    /// One finished livestream (KRILL.tv app list).
    [Serializable]
    public class StreamRecord { public int day; public float seconds; public int peakViewers; public long money; public long followers; public bool fainted; }

    /// The save game. In co-op the host's GameState is the shared truth; the fields marked PER PLAYER are local
    /// (kept by GameManager.ApplyHostSnapshot via CopyPerPlayerFrom and never taken from the host).
    [Serializable]
    public class GameState
    {
        /// 4 = round 2b (K/L). 5 = round 3 (backpack, one 8-slot hotbar, hunger / thirst, campfires, map pieces).
        /// 6 = round 3 hut furnishing (GameState.hut). 7 = round 3 hermits (GameState.hermits). 8 = round 3 shared
        /// treasure-map pins (GameState.mapPins). 9 = round 3 save slots (GDD §19b): play time + last played of the
        /// world (playSeconds, lastPlayedUtc); achievements count per world only (GameState.achievements – no longer
        /// mixed with a per-PC list).
        /// Older saves are migrated in EnsureValid (boat compressor gift, Sealog counters, 3 + 3 hotbar → 8 slots,
        /// starter backpack + the finder's copies of the collectibles, standard hut furnishing, no hermit met yet,
        /// no map pins, play time from the host's own record).
        public const int CurrentVersion = 9;

        public int version = CurrentVersion;
        /// Random per new game (never 0). Use SeededRandom(salt) for per-save randomness (bottle spot …).
        public int saveSeed;
        public Difficulty difficulty = Difficulty.OpenWater;
        public Language language = Language.German;     // legacy: the language now lives in Settings
        public int day = 1;
        public float timeOfDay = 0.29f;     // 0..1 (0.25 = 06:00, 0.5 = 12:00)
        public long money;
        public long followers;
        public long totalViews;
        public List<string> ownedItems = new List<string>();
        public List<JournalEntry> journal = new List<JournalEntry>();
        public List<FootageClip> diveFootage = new List<FootageClip>();     // PER PLAYER: current dive, lost when fainting
        public List<FootageClip> bankedFootage = new List<FootageClip>();   // safe on land/boat, not uploaded yet
        public List<PendingUpload> pendingUploads = new List<PendingUpload>();
        public List<VideoRecord> videos = new List<VideoRecord>();
        public List<Delivery> deliveries = new List<Delivery>();
        public List<string> unlockedSites = new List<string>();
        public List<string> reachedMilestones = new List<string>();
        public List<string> sponsorSites = new List<string>();             // journal pages completed 100 %
        public List<long> followerHistory = new List<long>();               // followers at the start of each day
        public int questIndex;
        public int fishCaught;
        public bool finaleReached;
        public List<string> collected = new List<string>();     // collectible ids (see Collectibles)

        // ---- fishing market (shared)
        public List<FishItem> fishInventory = new List<FishItem>();
        public List<FishListing> fishListings = new List<FishListing>();
        public int fishListedTotal;     // fish ever listed (tutorial: "list 5 fish")
        public int fishSold;
        public long fishMoney;          // money ever earned with fish

        // ---- round 2 (v3, shared)
        public List<PlayerRecord> players = new List<PlayerRecord>();      // F.3 per-player data of every crew member (host save)
        public List<PantryItem> pantry = new List<PantryItem>();           // E.1 snacks (shared fridge / boat cooler)
        public List<string> storyFlags = new List<string>();               // StoryFlags / StoryKeys
        public List<MissionState> missions = new List<MissionState>();     // Gus M1–M4
        public List<ResearchState> research = new List<ResearchState>();   // Dr. Fin
        public List<string> achievements = new List<string>();             // unlocked for the channel (F.6)
        public List<string> cosmetics = new List<string>();                // unlocked cosmetic ids (CosmeticsDatabase)
        public List<string> dailyUsed = new List<string>();                // "palm:<id>" … cleared every new day
        public int lastStreamDay = -1;
        public List<StreamRecord> streams = new List<StreamRecord>();
        public int rivalStage;                                             // Bottom brothers R1–R4 progress
        public int lastDuelDay;
        /// Views +5 % on uploads until (and including) this day after a won weekly duel (B.3).
        public int duelWonUntilDay = -1;
        public bool firstVideoBonusPaid;
        /// Uploads made on this day get the "Gemütlich" +5 % (E.2: grilled + slept in the bed).
        public int cozyBonusDay = -1;
        public int lastGrillDay = -1;

        // ---- round 2b (v4, shared)
        public List<FindState> finds = new List<FindState>();              // K.3 today's finds/salvage
        public List<FoundItem> foundItems = new List<FoundItem>();         // finds waiting to be sold („Funde“ tab)
        public List<string> challengesToday = new List<string>();          // K.3 today's 2 photo challenges
        public List<string> challengesDone = new List<string>();           // completed today ("id") – cleared every new day
        public int challengeBonusDay = -1;                                 // +10 % views per completed challenge on this day
        public int challengeBonusCount;
        public SponsorState sponsor = new SponsorState();                  // K.7
        public List<QuestState> sideQuests = new List<QuestState>();       // L.7 (+ Toni "toni")
        public string trackedQuest;                                        // L.10 tracked task id (PER PLAYER)
        public bool prologueDone;                                          // K.5 pre-game NPC talk finished
        public int lastLanternDay = -1;                                    // L.8 a lantern was released this day (+5 % views)
        public int geraldDays;                                             // days Gerald was filmed (eyes "Herzchen")
        public int lastGeraldDay = -1;
        public int lastMishapDay = -1;                                     // L.12 at most one delivery mishap per day
        public string dealOfDayItem;                                       // L.12 "Angebot des Tages" (−20 %), rolled by the host each day
        public int giftedCompressorVersion;                                // E.4 migration done

        // ---- round 3 (v5, shared)
        /// Campfires lit at the fixed campfire spots (Survival).
        public List<CampfireState> campfires = new List<CampfireState>();
        /// v6: the hut's furnishing (wall paint, floor, rug, curtains, bought / placed decor) – HutDecor.
        public HutDecorState hut = new HutDecorState();
        /// v7: the hermits of the jungle camp (mood, respect, today's signs / gifts / trades) – Hermits.
        public List<HermitState> hermits = new List<HermitState>();
        /// v8: shared co-op pins on the treasure map (GDD §20 Q31) – MapPins.
        public List<MapPin> mapPins = new List<MapPin>();
        /// v9 (save slots, GDD §19b): real seconds played in THIS world (host / single player; the slot card shows it).
        public double playSeconds;
        /// v9: DateTime.UtcNow.Ticks of the last save (slot card „zuletzt gespielt“; 0 = unknown → file time).
        public long lastPlayedUtc;

        // ---- PER PLAYER: equipment choice, hotbar, gallery
        /// Chosen item per gear slot (item ids). Missing slot → best owned item (GearDatabase.Equipped).
        public List<string> equipped = new List<string>();
        /// LEGACY 3 + 3 hotbar (saves before v5) – only read by EnsureHotbar's migration.
        public string[] hotbarRight = { "Camera", "FishingRod", "None" };
        public string[] hotbarLeft = { "Flashlight", "None", "None" };
        public int activeRight = -1;
        public int activeLeft = -1;
        /// Round 3: one hotbar of Hands.SlotCount item ids ("None" = empty) – null until EnsureHotbar migrated/created it.
        public string[] hotbar;
        /// Bit i set = slot i goes into the OFFHAND (else the main hand).
        public int hotbarOffMask = DefaultOffMask;
        /// Held slot of the main hand / offhand (−1 = empty hand).
        public int activeMain = -1;
        public int activeOff = -1;
        /// Round 3 backpack (Backpack) and the collectible pouch (collectibles + map pieces, no slot limit).
        public List<InvStack> backpack = new List<InvStack>();
        public List<InvStack> pouch = new List<InvStack>();
        /// Starter items were given (stick, bottle).
        public bool backpackInit;
        /// Survival meters 0..100 (100 = full / not thirsty).
        public float hunger = 100f, thirst = 100f;
        /// Seawater bottles in the hut's water filter.
        public List<FilterJob> filterJobs = new List<FilterJob>();
        public List<PhotoEntry> photos = new List<PhotoEntry>();
        /// PER PLAYER: carried quest item / salvage id (null = none) – Carry.
        public string carried;
        /// PER PLAYER: the local player's own record (stats, achievements, carried) – synced to the host's players list.
        public PlayerRecord localRecord;

        /// Set by EnsureValid when a v2 save received the free boat compressor (GameManager shows the toast).
        [NonSerialized] public bool justGiftedCompressor;

        /// Moved to Settings (per machine). Kept as a forwarding property for old callers; not serialized.
        public float mouseSensitivity
        {
            get => Settings.Data.mouseSensitivity;
            set { Settings.Data.mouseSensitivity = Mathf.Clamp(value, 0.1f, 5f); Settings.MarkDirty(); }
        }

        /// Deterministic random stream for this save (same on every co-op machine once synced).
        public System.Random SeededRandom(int salt) => new System.Random(unchecked(saveSeed * 486187739 + salt * 16777619));

        /// Repairs missing lists / wrong array sizes after loading an older save or a snapshot, and migrates old versions.
        public void EnsureValid()
        {
            if (ownedItems == null) ownedItems = new List<string>();
            if (journal == null) journal = new List<JournalEntry>();
            if (diveFootage == null) diveFootage = new List<FootageClip>();
            if (bankedFootage == null) bankedFootage = new List<FootageClip>();
            if (pendingUploads == null) pendingUploads = new List<PendingUpload>();
            if (videos == null) videos = new List<VideoRecord>();
            if (deliveries == null) deliveries = new List<Delivery>();
            if (unlockedSites == null) unlockedSites = new List<string>();
            if (reachedMilestones == null) reachedMilestones = new List<string>();
            if (sponsorSites == null) sponsorSites = new List<string>();
            if (followerHistory == null) followerHistory = new List<long>();
            if (collected == null) collected = new List<string>();
            if (fishInventory == null) fishInventory = new List<FishItem>();
            if (fishListings == null) fishListings = new List<FishListing>();
            if (equipped == null) equipped = new List<string>();
            if (photos == null) photos = new List<PhotoEntry>();
            // round 2 / 2b
            if (players == null) players = new List<PlayerRecord>();
            if (pantry == null) pantry = new List<PantryItem>();
            if (storyFlags == null) storyFlags = new List<string>();
            if (missions == null) missions = new List<MissionState>();
            if (research == null) research = new List<ResearchState>();
            if (achievements == null) achievements = new List<string>();
            if (cosmetics == null) cosmetics = new List<string>();
            if (dailyUsed == null) dailyUsed = new List<string>();
            if (streams == null) streams = new List<StreamRecord>();
            if (finds == null) finds = new List<FindState>();
            if (foundItems == null) foundItems = new List<FoundItem>();
            if (challengesToday == null) challengesToday = new List<string>();
            if (challengesDone == null) challengesDone = new List<string>();
            if (sponsor == null) sponsor = new SponsorState();
            if (sponsor.offers == null) sponsor.offers = new List<DealState>();
            if (sponsor.active == null) sponsor.active = new List<DealState>();
            if (sponsor.cooldowns == null) sponsor.cooldowns = new List<SponsorCooldown>();
            if (sponsor.done == null) sponsor.done = new List<string>();
            if (sideQuests == null) sideQuests = new List<QuestState>();
            // round 3
            if (campfires == null) campfires = new List<CampfireState>();
            for (int i = campfires.Count - 1; i >= 0; i--) if (campfires[i] == null || string.IsNullOrEmpty(campfires[i].id) || float.IsNaN(campfires[i].until)) campfires.RemoveAt(i);
            if (backpack == null) backpack = new List<InvStack>();
            if (pouch == null) pouch = new List<InvStack>();
            if (filterJobs == null) filterJobs = new List<FilterJob>();
            for (int i = filterJobs.Count - 1; i >= 0; i--) if (filterJobs[i] == null || filterJobs[i].count <= 0 || float.IsNaN(filterJobs[i].readyAt)) filterJobs.RemoveAt(i);
            Backpack.Normalize(backpack);
            Backpack.Normalize(pouch);
            if (hut == null || version < 6) hut = HutDecor.Default();   // v6: saves before the furnishing get the standard hut
            HutDecor.Normalize(hut);
            if (hermits == null) hermits = new List<HermitState>();   // v7: saves before the hermits – nobody met yet
            Hermits.Normalize(hermits);
            if (mapPins == null) mapPins = new List<MapPin>();       // v8: saves before the map pins – none set
            MapPins.Normalize(mapPins);
            if (double.IsNaN(playSeconds) || double.IsInfinity(playSeconds) || playSeconds < 0.0) playSeconds = 0.0;   // v9
            if (lastPlayedUtc < 0) lastPlayedUtc = 0;
            // achievements of this world: no empty ids, no duplicates
            for (int i = achievements.Count - 1; i >= 0; i--)
                if (string.IsNullOrEmpty(achievements[i]) || achievements.IndexOf(achievements[i]) != i) achievements.RemoveAt(i);
            hunger =float.IsNaN(hunger) || float.IsInfinity(hunger) ? Survival.Max : Mathf.Clamp(hunger, 0f, Survival.Max);
            thirst = float.IsNaN(thirst) || float.IsInfinity(thirst) ? Survival.Max : Mathf.Clamp(thirst, 0f, Survival.Max);
            for (int i = journal.Count - 1; i >= 0; i--) if (journal[i] == null || string.IsNullOrEmpty(journal[i].speciesId)) journal.RemoveAt(i);
            for (int i = players.Count - 1; i >= 0; i--)
            {
                var p = players[i];
                if (p == null || string.IsNullOrEmpty(p.key)) { players.RemoveAt(i); continue; }
                if (p.equipped == null) p.equipped = new List<string>();
                if (p.achievements == null) p.achievements = new List<string>();
            }
            for (int i = 0; i < deliveries.Count; i++) if (deliveries[i] != null && deliveries[i].mishap == 0 && deliveries[i].dropZone == 0) deliveries[i].dropZone = -1;
            EnsureHotbar();
            if (money < 0) money = 0;
            if (followers < 0) followers = 0;
            if (version < CurrentVersion) Migrate(version);
            version = CurrentVersion;
            EnsurePerPlayer();
        }

        /// Round 3: the per-player starter kit (stick + bottle) for a player that never had a backpack (new game, old
        /// save, first co-op join). Safe to call again.
        public void EnsurePerPlayer()
        {
            EnsureHotbar();
            if (backpack == null) backpack = new List<InvStack>();
            if (pouch == null) pouch = new List<InvStack>();
            if (backpackInit) return;
            backpackInit = true;
            Backpack.GiveStarterItems(this);
        }

        /// Old saves (v2 = Testversion 3). Snapshots from a host always carry the current version.
        void Migrate(int from)
        {
            if (from < 3)
            {
                // E.4: the boat refills air only with the new compressor – nobody loses the boat refills they had before
                if (followers >= 250 && giftedCompressorVersion < 3 && !ownedItems.Contains("boat_compressor"))
                {
                    ownedItems.Add("boat_compressor");
                    justGiftedCompressor = true;
                }
                giftedCompressorVersion = 3;
                // the tutorial ran before the prologue existed
                if (day > 1 || questIndex > 0) prologueDone = true;
            }
            if (from < 4)
            {
                // L.2 counters for v2/v3 saves: sightings = timesFilmed, photos = discovered ? 1 : 0
                for (int i = 0; i < journal.Count; i++)
                {
                    var e = journal[i];
                    if (e.sightings <= 0) e.sightings = e.timesFilmed;
                    if (e.photos <= 0 && e.discovered) e.photos = 1;
                    if (e.maxDepthSeen <= 0f) e.minDepthSeen = -1f;
                    if (e.lastSeenDay <= 0) e.lastSeenDay = e.firstDay;
                }
                for (int i = 0; i < deliveries.Count; i++) if (deliveries[i] != null) { deliveries[i].mishap = 0; deliveries[i].dropZone = -1; }
            }
            if (from < 5)
            {
                // round 3: the finder's copies of the channel's collectibles go into the pouch (old saves had no backpack)
                if (pouch == null) pouch = new List<InvStack>();
                for (int i = 0; i < collected.Count; i++)
                {
                    string id = collected[i];
                    if (string.IsNullOrEmpty(id)) continue;
                    string item = null;
                    if (id.StartsWith("shell:", StringComparison.Ordinal)) item = ItemIds.Shell;
                    else if (id.StartsWith("log:", StringComparison.Ordinal)) item = ItemIds.Page;
                    else if (id.StartsWith("bottle:", StringComparison.Ordinal)) item = ItemIds.Message;
                    else if (id.StartsWith("treasure:", StringComparison.Ordinal)) item = ItemIds.Treasure;
                    if (item == null) continue;
                    InvStack st = null;
                    for (int k = 0; k < pouch.Count; k++) if (pouch[k] != null && pouch[k].id == item) { st = pouch[k]; break; }
                    if (st == null) pouch.Add(st = new InvStack { id = item, count = 0 });
                    st.count = Mathf.Min(99, st.count + 1);
                }
            }
            if (from < 9)
            {
                // save slots (GDD §19b): the world's play time was only counted in the host's own record
                float mins = localRecord != null ? localRecord.playMinutes : 0f;
                if (playSeconds <= 0.0 && mins > 0f && !float.IsNaN(mins) && !float.IsInfinity(mins)) playSeconds = mins * 60.0;
                // the world's achievements are the host's own – nothing pops up again after loading
                if (localRecord != null)
                {
                    if (localRecord.achievements == null) localRecord.achievements = new List<string>();
                    for (int i = 0; i < achievements.Count; i++)
                        if (!localRecord.achievements.Contains(achievements[i])) localRecord.achievements.Add(achievements[i]);
                }
            }
        }

        /// Default round-3 hotbar: camera, rod, stick (main hand, keys 1–3), lamp on 4 (offhand), 5–8 free.
        public static string[] DefaultHotbar() => new[]
        {
            ItemIds.Camera, ItemIds.FishingRod, ItemIds.Stick, ItemIds.Flashlight, ItemIds.None, ItemIds.None, ItemIds.None, ItemIds.None
        };

        /// Slots 4–6 belong to the offhand by default (they were the left hand's 1–3 in the old 3 + 3 hotbar).
        public const int DefaultOffMask = (1 << 3) | (1 << 4) | (1 << 5);

        /// Old 3 + 3 hotbar → one 8-slot row: right 1–3 = slots 1–3 (main), left 1–3 = slots 4–6 (offhand). The new
        /// stick takes the first free main-hand slot.
        public static string[] MigrateLegacyHotbar(string[] right, string[] left)
        {
            var r = new string[Hands.SlotCount];
            for (int i = 0; i < r.Length; i++) r[i] = ItemIds.None;
            for (int i = 0; i < Hands.SlotsPerHand; i++)
            {
                r[i] = right != null && i < right.Length && !string.IsNullOrEmpty(right[i]) ? right[i] : (right == null ? DefaultHotbar()[i] : ItemIds.None);
                r[Hands.SlotsPerHand + i] = left != null && i < left.Length && !string.IsNullOrEmpty(left[i]) ? left[i] : (left == null && i == 0 ? ItemIds.Flashlight : ItemIds.None);
            }
            bool hasStick = false;
            for (int i = 0; i < r.Length; i++) if (r[i] == ItemIds.Stick) hasStick = true;
            if (!hasStick)
            {
                int[] order = { 2, 1, 0, 6, 7 };
                for (int k = 0; k < order.Length; k++) if (r[order[k]] == ItemIds.None) { r[order[k]] = ItemIds.Stick; break; }
            }
            return r;
        }

        public void EnsureHotbar()
        {
            if (hotbar == null || hotbar.Length != Hands.SlotCount)
            {
                if (hotbar != null && hotbar.Length > 0)
                {
                    var r = new string[Hands.SlotCount];
                    for (int i = 0; i < r.Length; i++) r[i] = i < hotbar.Length ? hotbar[i] : ItemIds.None;
                    hotbar = r;
                }
                else
                {
                    hotbar = MigrateLegacyHotbar(hotbarRight, hotbarLeft);
                    hotbarOffMask = DefaultOffMask;
                    activeMain = -1;
                    activeOff = -1;
                }
            }
            for (int i = 0; i < hotbar.Length; i++)
                if (string.IsNullOrEmpty(hotbar[i]) || (hotbar[i] != ItemIds.None && ItemDatabase.Get(hotbar[i]) == null)) hotbar[i] = ItemIds.None;
            hotbarOffMask &= (1 << Hands.SlotCount) - 1;
            if (activeMain >= Hands.SlotCount || activeMain < -1) activeMain = -1;
            if (activeOff >= Hands.SlotCount || activeOff < -1) activeOff = -1;
        }

        /// Copies the per-player fields (hotbar, backpack, survival meters, equipment choice, current dive footage,
        /// gallery, carried item, own record, quest-line pin) from another state. Used when a host snapshot replaces the
        /// shared state on a client.
        public void CopyPerPlayerFrom(GameState src)
        {
            if (src == null) return;
            hotbarRight = src.hotbarRight;
            hotbarLeft = src.hotbarLeft;
            activeRight = src.activeRight;
            activeLeft = src.activeLeft;
            hotbar = src.hotbar;
            hotbarOffMask = src.hotbarOffMask;
            activeMain = src.activeMain;
            activeOff = src.activeOff;
            backpack = src.backpack ?? new List<InvStack>();
            pouch = src.pouch ?? new List<InvStack>();
            backpackInit = src.backpackInit;
            hunger = src.hunger;
            thirst = src.thirst;
            filterJobs = src.filterJobs ?? new List<FilterJob>();
            equipped = src.equipped ?? new List<string>();
            diveFootage = src.diveFootage ?? new List<FootageClip>();
            photos = src.photos ?? new List<PhotoEntry>();
            carried = src.carried;
            localRecord = src.localRecord;
            trackedQuest = src.trackedQuest;   // the quest-line pin is per player
        }
    }

    /// Sealog pages (L.1/L.2): site pages unlock the next dive site at 60 %; Hochsee unlocks nothing.
    /// Round 3 (Q23): the island animals are decoration now – the „Insel“ page is gone; new pages: the volcano island's
    /// living fossils (Q14, hidden until VolcanoGate.Open) and the curiosities / memes (Q19).
    public static class SealogPages
    {
        public struct Page
        {
            public string id, nameDe, nameEn;
            /// True for dive-site pages (unlock rule, sponsor page money).
            public bool site;
            public string Name => Loc.Tr(nameDe, nameEn);
        }

        public static readonly Page[] All =
        {
            new Page { id = "house_reef", nameDe = "Hausriff", nameEn = "House Reef", site = true },
            new Page { id = "kelp_forest", nameDe = "Kelpwald", nameEn = "Kelp Forest", site = true },
            new Page { id = "wreck", nameDe = "Wrack", nameEn = "Wreck", site = true },
            new Page { id = "blue_drop", nameDe = "Blauwasser", nameEn = "Blue Water", site = true },
            new Page { id = "deep_trench", nameDe = "Tiefseegraben", nameEn = "Deep Trench", site = true },
            new Page { id = "open_sea", nameDe = "Hochsee", nameEn = "Open Sea", site = false },
            new Page { id = "fog_isle", nameDe = "Nebelinsel", nameEn = "Fog Isle", site = true },
            new Page { id = "volcano_isle", nameDe = "Vulkaninsel", nameEn = "Volcano Isle", site = false },
            new Page { id = "memes", nameDe = "Kuriositäten", nameEn = "Curiosities", site = false },
        };

        public static int IndexOf(string id)
        {
            for (int i = 0; i < All.Length; i++) if (All[i].id == id) return i;
            return -1;
        }
    }

    /// Journal (Sealog) access helpers.
    public static class Journal
    {
        static GameState S => GameManager.I.State;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static JournalEntry Get(string speciesId)
        {
            var j = S.journal;
            for (int i = 0; i < j.Count; i++) if (j[i] != null && j[i].speciesId == speciesId) return j[i];
            var e = new JournalEntry { speciesId = speciesId, minDepthSeen = -1f };
            j.Add(e);
            return e;
        }

        /// Entry or null (never creates one).
        public static JournalEntry Find(string speciesId)
        {
            if (GameManager.I == null) return null;
            var j = S.journal;
            for (int i = 0; i < j.Count; i++) if (j[i] != null && j[i].speciesId == speciesId) return j[i];
            return null;
        }

        public static bool IsDiscovered(string speciesId)
        {
            var e = Find(speciesId);
            return e != null && e.discovered;
        }

        public static int DiscoveredCount
        {
            get
            {
                if (GameManager.I == null) return 0;
                var j = S.journal;
                int n = 0;
                for (int i = 0; i < j.Count; i++)
                {
                    if (j[i] == null || !j[i].discovered) continue;
                    var sp = SpeciesDatabase.Get(j[i].speciesId);
                    if (sp != null && sp.InSealog) n++;   // round 3: decoration animals (old island page) do not count
                }
                return n;
            }
        }

        /// Sealog 100 % – every species (55) discovered (golden camera, Q52).
        public static bool IsComplete => DiscoveredCount >= SpeciesDatabase.Count;

        /// Share of a Sealog page that is discovered (dive-site unlocks use the site pages).
        public static float PageCompletion(string pageId)
        {
            var all = SpeciesDatabase.All;
            int n = 0, found = 0;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].Page != pageId) continue;
                n++;
                if (IsDiscovered(all[i].id)) found++;
            }
            return n == 0 ? 0f : found / (float)n;
        }

        /// Completion of a dive site's Sealog page (= PageCompletion(siteId)).
        public static float SiteCompletion(string siteId) => PageCompletion(siteId);

        /// LEGACY wrapper (round 1): registers a good clip in the Sealog. Round 2b registers the Sealog only by a
        /// photo (RegisterPhoto); the Filming module stops calling this once K.2 is in – until then it keeps working.
        /// SHARED STATE: on co-op clients this sends NetCommand.RegisterFilmed and applies the result optimistically.
        public static (bool isNew, bool isRecord) RegisterFilmed(SpeciesDef def, float sizeCm, SizeTier tier, float clipValue)
        {
            if (def == null || GameManager.I == null || !def.InSealog) return (false, false);
            if (Net.IsClient)
            {
                Net.Command(NetCommand.RegisterFilmed, def.id, sizeCm.ToString("R", Inv), ((int)tier).ToString(Inv), clipValue.ToString("R", Inv));
                return Apply(def, sizeCm, tier, clipValue);
            }
            var r = Apply(def, sizeCm, tier, clipValue);
            Progression.CheckUnlocks();
            Cosmetics.CheckRules();
            return r;
        }

        /// K.2: a Sealog photo of an animal registers the species (discovery / size record) and counts as one sighting
        /// (the Filming module must not add the photo subject to AddSightings again). photoQuality 0..1, photoId = the
        /// gallery file name (PhotoEntry.file), depth in meters, zoneBit = Zones.Bit(zone) or 0.
        /// SHARED STATE: clients send NetCommand.RegisterFilmed (speciesId, sizeCm, tier, photoQuality, photoId, depth,
        /// zoneBit) and apply it optimistically.
        public static (bool isNew, bool isRecord) RegisterPhoto(SpeciesDef def, float sizeCm, SizeTier tier, float photoQuality, string photoId, float depth, int zoneBit = 0)
        {
            // round 3 (Q23): decoration animals (island birds, crabs, lizards …) are no Sealog entries
            if (def == null || GameManager.I == null || !def.InSealog) return (false, false);
            photoQuality = float.IsNaN(photoQuality) ? 0f : Mathf.Clamp01(photoQuality);
            if (float.IsNaN(depth) || float.IsInfinity(depth)) depth = 0f;
            if (Net.IsClient)
            {
                Net.Command(NetCommand.RegisterFilmed, def.id, sizeCm.ToString("R", Inv), ((int)tier).ToString(Inv), photoQuality.ToString("R", Inv),
                    photoId ?? "", depth.ToString("R", Inv), zoneBit.ToString(Inv));
                return ApplyPhoto(def, sizeCm, tier, photoQuality, photoId, depth, zoneBit);
            }
            var r = ApplyPhoto(def, sizeCm, tier, photoQuality, photoId, depth, zoneBit);
            Progression.CheckUnlocks();
            Cosmetics.CheckRules();
            return r;
        }

        static (bool isNew, bool isRecord) Apply(SpeciesDef def, float sizeCm, SizeTier tier, float clipValue)
        {
            var e = Get(def.id);
            e.timesFilmed++;
            if (clipValue > e.bestClipValue) e.bestClipValue = clipValue;
            return Register(def, e, sizeCm, tier);
        }

        static (bool isNew, bool isRecord) ApplyPhoto(SpeciesDef def, float sizeCm, SizeTier tier, float photoQuality, string photoId, float depth, int zoneBit)
        {
            var e = Get(def.id);
            e.photos++;
            if (!string.IsNullOrEmpty(photoId) && (photoQuality >= e.bestPhotoQuality || string.IsNullOrEmpty(e.bestPhotoId)))
            {
                e.bestPhotoId = photoId;
                e.bestPhotoQuality = photoQuality;
            }
            SeenAt(e, 1, depth, depth, zoneBit);
            var r = Register(def, e, sizeCm, tier);
            PhotoEntry photo = null;
            if (!string.IsNullOrEmpty(photoId))
            {
                var list = Gallery.Photos;
                for (int i = list.Count - 1; i >= 0; i--) if (list[i] != null && list[i].file == photoId) { photo = list[i]; break; }
            }
            GameEvents.RaiseSealogPhoto(photo, def);
            GameEvents.RaiseSighted(def, 1);
            return r;
        }

        static (bool isNew, bool isRecord) Register(SpeciesDef def, JournalEntry e, float sizeCm, SizeTier tier)
        {
            bool isNew = !e.discovered;
            bool isRecord = false;
            if (isNew)
            {
                e.discovered = true;
                e.firstDay = S.day;
                e.recordCm = sizeCm;
                e.recordTier = (int)tier;
                GameEvents.RaiseSpeciesDiscovered(def, sizeCm, tier);
            }
            else if (sizeCm > e.recordCm + 0.05f)
            {
                e.recordCm = sizeCm;
                e.recordTier = (int)tier;
                isRecord = true;
                GameEvents.RaiseSizeRecord(def, sizeCm, tier);
            }
            return (isNew, isRecord);
        }

        static void SeenAt(JournalEntry e, int count, float minDepth, float maxDepth, int zoneBit)
        {
            e.sightings += count;
            if (minDepth > maxDepth) { float t = minDepth; minDepth = maxDepth; maxDepth = t; }
            minDepth = Mathf.Max(0f, minDepth);
            maxDepth = Mathf.Max(0f, maxDepth);
            if (e.minDepthSeen < 0f || minDepth < e.minDepthSeen) e.minDepthSeen = minDepth;
            if (maxDepth > e.maxDepthSeen) e.maxDepthSeen = maxDepth;
            e.zoneMask |= zoneBit;
            int day = S.day;
            if (e.speciesId == "green_turtle" && day != S.lastGeraldDay)
            {
                S.lastGeraldDay = day;
                S.geraldDays++;
            }
            e.lastSeenDay = day;
        }

        // ---------------------------------------------------------------- sightings (L.2)
        struct PendingSighting { public string id; public int n; public float min, max; public int zones; }
        static readonly List<PendingSighting> pending = new List<PendingSighting>();
        static float flushTimer;
        /// Clients send their batched sightings every this many seconds (K.2).
        public const float SightingsFlushSeconds = 10f;

        /// Adds sightings of a species (individuals that became a clip, plus animals in a photo that were not its
        /// subject). AUTHORITY applies directly; clients apply optimistically and batch to the host every 10 s
        /// (CoreCommands.SightingsKey "id:n:minDepth:maxDepth:zoneMask;…").
        public static void AddSightings(string speciesId, int count, float minDepth, float maxDepth, int zoneBit)
        {
            var def = SpeciesDatabase.Get(speciesId);
            if (def == null || count <= 0 || GameManager.I == null || !GameManager.I.InGame) return;
            if (float.IsNaN(minDepth) || float.IsInfinity(minDepth)) minDepth = 0f;
            if (float.IsNaN(maxDepth) || float.IsInfinity(maxDepth)) maxDepth = minDepth;
            count = Mathf.Min(count, 500);
            SeenAt(Get(speciesId), count, minDepth, maxDepth, zoneBit);
            GameEvents.RaiseSighted(def, count);
            if (!Net.IsClient) return;
            for (int i = 0; i < pending.Count; i++)
            {
                var p = pending[i];
                if (p.id != speciesId) continue;
                p.n += count; p.min = Mathf.Min(p.min, minDepth); p.max = Mathf.Max(p.max, maxDepth); p.zones |= zoneBit;
                pending[i] = p;
                return;
            }
            pending.Add(new PendingSighting { id = speciesId, n = count, min = minDepth, max = maxDepth, zones = zoneBit });
        }

        /// Host side of SightingsKey: applies one batch from a client.
        public static void ApplySightingsBatch(string batch)
        {
            if (string.IsNullOrEmpty(batch) || !Net.HasAuthority) return;
            var entries = batch.Split(';');
            for (int i = 0; i < entries.Length && i < 128; i++)
            {
                var f = entries[i].Split(':');
                if (f.Length < 4) continue;
                var def = SpeciesDatabase.Get(f[0]);
                if (def == null) continue;
                if (!int.TryParse(f[1], NumberStyles.Integer, Inv, out int n) || n <= 0) continue;
                float.TryParse(f[2], NumberStyles.Float, Inv, out float mn);
                float.TryParse(f[3], NumberStyles.Float, Inv, out float mx);
                int z = 0;
                if (f.Length >= 5) int.TryParse(f[4], NumberStyles.Integer, Inv, out z);
                AddSightings(def.id, Mathf.Min(n, 500), mn, mx, z);
            }
        }

        /// Called every frame by GameManager (clients only): sends the pending sightings every 10 s.
        public static void TickSightings(float dt)
        {
            if (!Net.IsClient) { pending.Clear(); flushTimer = 0f; return; }
            flushTimer += dt;
            if (flushTimer < SightingsFlushSeconds || pending.Count == 0) return;
            FlushSightings();
        }

        /// Raised by FlushAllSightings before the batch is sent: modules with their own sighting buffers (the hand
        /// camera's 2 s buffer) hand them to AddSightings first.
        public static event System.Action FlushRequested;

        /// Leaving a co-op session (guest): flush every buffer (hand camera → Journal pending → host) while still online.
        public static void FlushAllSightings()
        {
            if (!Net.IsClient) return;
            FlushRequested?.Invoke();
            FlushSightings();
        }

        /// Clients: sends the pending sightings batch to the host right now (leaving the session must not lose the
        /// last seconds of "Gefunden ×n" / depth data). No-op offline, on the host or when nothing is pending.
        public static void FlushSightings()
        {
            if (!Net.IsClient || pending.Count == 0) return;
            flushTimer = 0f;
            var sb = new System.Text.StringBuilder(pending.Count * 24);
            for (int i = 0; i < pending.Count; i++)
            {
                var p = pending[i];
                if (i > 0) sb.Append(';');
                sb.Append(p.id).Append(':').Append(p.n.ToString(Inv)).Append(':').Append(p.min.ToString("0.#", Inv)).Append(':')
                  .Append(p.max.ToString("0.#", Inv)).Append(':').Append(p.zones.ToString(Inv));
            }
            pending.Clear();
            Net.Command(NetCommand.Custom, CoreCommands.SightingsKey, sb.ToString());
        }

        /// Clients, after a host snapshot replaced the journal: re-applies the sightings that are still waiting for the
        /// next batch, so the optimistic "×n" counter does not jump back (they stay queued, nothing is sent twice).
        public static void ReapplyPendingSightings()
        {
            if (!Net.IsClient || pending.Count == 0 || GameManager.I == null || !GameManager.I.InGame) return;
            for (int i = 0; i < pending.Count; i++)
            {
                var p = pending[i];
                if (SpeciesDatabase.Get(p.id) == null) continue;
                SeenAt(Get(p.id), p.n, p.min, p.max, p.zones);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { pending.Clear(); flushTimer = 0f; FlushRequested = null; }
    }

    /// Footage bookkeeping: clips filmed during a dive are only safe once the player is back on land or the boat.
    /// diveFootage is per player; bankedFootage is the shared channel's pool for the next upload.
    public static class Footage
    {
        static GameState S => GameManager.I.State;

        public static void AddClip(FootageClip c)
        {
            if (c == null || GameManager.I == null) return;
            c.day = S.day;
            S.diveFootage.Add(c);
            GameEvents.RaiseClipRecorded(c);
        }

        /// Call when the player safely leaves the water (boat deck, ladder, beach).
        /// SHARED STATE: on co-op clients the clips are sent to the host (NetCommand.BankClips).
        public static void BankDive()
        {
            if (GameManager.I == null || S.diveFootage.Count == 0) return;
            int n = S.diveFootage.Count;
            if (Net.IsClient)
            {
                var batch = new ClipBatch { clips = new List<FootageClip>(S.diveFootage) };
                Net.Command(NetCommand.BankClips, JsonUtility.ToJson(batch));
            }
            S.bankedFootage.AddRange(S.diveFootage);   // clients: optimistic until the next host snapshot
            S.diveFootage.Clear();
            GameEvents.RaiseNotify(NotifyKind.Clip, Loc.Tr("Clips gesichert", "Clips saved"), "+" + n.ToString(CultureInfo.InvariantCulture));
        }

        /// Host side of NetCommand.BankClips: adds another player's banked clips to the shared pool.
        public static void BankClips(List<FootageClip> clips)
        {
            if (clips == null || clips.Count == 0 || GameManager.I == null || !Net.HasAuthority) return;
            for (int i = 0; i < clips.Count; i++)
            {
                var c = clips[i];
                if (c == null || SpeciesDatabase.Get(c.speciesId) == null) continue;
                c.quality = float.IsNaN(c.quality) ? 0.1f : Mathf.Clamp(c.quality, 0.1f, 1.6f);
                c.crowd = Mathf.Clamp(c.crowd, 0, Progression.MaxCrowd);
                c.tier = Mathf.Clamp(c.tier, 0, Progression.TierMultiplier.Length - 1);
                if (float.IsNaN(c.action)) c.action = 0f;
                c.action = Mathf.Clamp01(c.action);
                if (float.IsNaN(c.seconds) || float.IsInfinity(c.seconds) || c.seconds < 0f) c.seconds = 0f;
                S.bankedFootage.Add(c);
            }
        }

        /// Call when the player faints: this dive's footage is lost.
        public static void LoseDive()
        {
            if (GameManager.I == null) return;
            int n = S.diveFootage.Count;
            S.diveFootage.Clear();
            if (n > 0) GameEvents.RaiseNotify(NotifyKind.Warning, Loc.Tr("Clips verloren", "Clips lost"), "−" + n.ToString(CultureInfo.InvariantCulture));
        }

        public static int DiveCount => GameManager.I != null ? S.diveFootage.Count : 0;
        public static int BankedCount => GameManager.I != null ? S.bankedFootage.Count : 0;
    }
}
