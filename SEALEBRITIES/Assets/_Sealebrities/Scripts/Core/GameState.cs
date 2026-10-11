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
        public int timesFilmed;
        public float bestClipValue;
        public int firstDay;
        public int lastUploadDay = -99; // for the novelty penalty on repeated uploads
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
    }

    [Serializable]
    public class Delivery
    {
        public string itemId;
        public int arrivalDay;
        public float arrivalTime;       // time of day 0..1
        public bool express;
        public bool dropped;            // crate is on the helipad, waiting for pickup
    }

    /// The save game. In co-op the host's GameState is the shared truth; the fields marked PER PLAYER are local
    /// (kept by GameManager.ApplyHostSnapshot via CopyPerPlayerFrom and never taken from the host).
    [Serializable]
    public class GameState
    {
        public const int CurrentVersion = 2;

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

        // ---- PER PLAYER: equipment choice, hotbar, gallery
        /// Chosen item per gear slot (item ids). Missing slot → best owned item (GearDatabase.Equipped).
        public List<string> equipped = new List<string>();
        public string[] hotbarRight = { "Camera", "FishingRod", "None" };
        public string[] hotbarLeft = { "Flashlight", "None", "None" };
        public int activeRight = -1;    // -1 = empty hand
        public int activeLeft = -1;
        public List<PhotoEntry> photos = new List<PhotoEntry>();

        /// Moved to Settings (per machine). Kept as a forwarding property for old callers; not serialized.
        public float mouseSensitivity
        {
            get => Settings.Data.mouseSensitivity;
            set { Settings.Data.mouseSensitivity = Mathf.Clamp(value, 0.1f, 5f); Settings.MarkDirty(); }
        }

        /// Deterministic random stream for this save (same on every co-op machine once synced).
        public System.Random SeededRandom(int salt) => new System.Random(unchecked(saveSeed * 486187739 + salt * 16777619));

        /// Repairs missing lists / wrong array sizes after loading an older save or a snapshot.
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
            EnsureHotbar();
            if (money < 0) money = 0;
            if (followers < 0) followers = 0;
            version = CurrentVersion;
        }

        public void EnsureHotbar()
        {
            if (hotbarRight == null || hotbarRight.Length != Hands.SlotsPerHand) hotbarRight = Resize(hotbarRight, "Camera", "FishingRod", "None");
            if (hotbarLeft == null || hotbarLeft.Length != Hands.SlotsPerHand) hotbarLeft = Resize(hotbarLeft, "Flashlight", "None", "None");
            if (activeRight >= Hands.SlotsPerHand || activeRight < -1) activeRight = -1;
            if (activeLeft >= Hands.SlotsPerHand || activeLeft < -1) activeLeft = -1;
        }

        static string[] Resize(string[] src, string a, string b, string c)
        {
            var r = new[] { a, b, c };
            if (src != null) for (int i = 0; i < r.Length && i < src.Length; i++) r[i] = src[i] ?? "None";
            return r;
        }

        /// Copies the per-player fields (hotbar, equipment choice, current dive footage, gallery) from another state.
        /// Used when a host snapshot replaces the shared state on a client.
        public void CopyPerPlayerFrom(GameState src)
        {
            if (src == null) return;
            hotbarRight = src.hotbarRight;
            hotbarLeft = src.hotbarLeft;
            activeRight = src.activeRight;
            activeLeft = src.activeLeft;
            equipped = src.equipped ?? new List<string>();
            diveFootage = src.diveFootage ?? new List<FootageClip>();
            photos = src.photos ?? new List<PhotoEntry>();
        }
    }

    /// Journal (Sealog) access helpers.
    public static class Journal
    {
        static GameState S => GameManager.I.State;

        public static JournalEntry Get(string speciesId)
        {
            var j = S.journal;
            for (int i = 0; i < j.Count; i++) if (j[i] != null && j[i].speciesId == speciesId) return j[i];
            var e = new JournalEntry { speciesId = speciesId };
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
                for (int i = 0; i < j.Count; i++) if (j[i] != null && j[i].discovered) n++;
                return n;
            }
        }

        public static float SiteCompletion(string siteId)
        {
            var all = SpeciesDatabase.All;
            int n = 0, found = 0;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].siteId != siteId) continue;
                n++;
                if (IsDiscovered(all[i].id)) found++;
            }
            return n == 0 ? 0f : found / (float)n;
        }

        /// Registers a good clip of an animal. Raises SpeciesDiscovered or SizeRecord. Returns (isNew, isRecord).
        /// SHARED STATE: on co-op clients this sends NetCommand.RegisterFilmed and applies the result optimistically.
        public static (bool isNew, bool isRecord) RegisterFilmed(SpeciesDef def, float sizeCm, SizeTier tier, float clipValue)
        {
            if (def == null || GameManager.I == null) return (false, false);
            if (Net.IsClient)
            {
                var inv = CultureInfo.InvariantCulture;
                Net.Command(NetCommand.RegisterFilmed, def.id, sizeCm.ToString("R", inv), ((int)tier).ToString(inv), clipValue.ToString("R", inv));
                return Apply(def, sizeCm, tier, clipValue);
            }
            var r = Apply(def, sizeCm, tier, clipValue);
            Progression.CheckUnlocks();
            return r;
        }

        static (bool isNew, bool isRecord) Apply(SpeciesDef def, float sizeCm, SizeTier tier, float clipValue)
        {
            var e = Get(def.id);
            bool isNew = !e.discovered;
            bool isRecord = false;
            e.timesFilmed++;
            if (clipValue > e.bestClipValue) e.bestClipValue = clipValue;
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
            GameEvents.RaiseToast(n == 1
                ? Loc.Tr("1 Clip gesichert – lade ihn im Handy auf FinTube hoch.", "1 clip saved – upload it on FinTube in your phone.")
                : Loc.Tr($"{n} Clips gesichert – lade sie im Handy auf FinTube hoch.", $"{n} clips saved – upload them on FinTube in your phone."));
        }

        /// Host side of NetCommand.BankClips: adds another player's banked clips to the shared pool.
        public static void BankClips(List<FootageClip> clips)
        {
            if (clips == null || clips.Count == 0 || GameManager.I == null || !Net.HasAuthority) return;
            for (int i = 0; i < clips.Count; i++) if (clips[i] != null && SpeciesDatabase.Get(clips[i].speciesId) != null) S.bankedFootage.Add(clips[i]);
        }

        /// Call when the player faints: this dive's footage is lost.
        public static void LoseDive()
        {
            if (GameManager.I == null) return;
            int n = S.diveFootage.Count;
            S.diveFootage.Clear();
            if (n > 0) GameEvents.RaiseToast(n == 1 ? Loc.Tr("1 Clip verloren.", "1 clip lost.") : Loc.Tr($"{n} Clips verloren.", $"{n} clips lost."));
        }

        public static int DiveCount => GameManager.I != null ? S.diveFootage.Count : 0;
        public static int BankedCount => GameManager.I != null ? S.bankedFootage.Count : 0;
    }
}
