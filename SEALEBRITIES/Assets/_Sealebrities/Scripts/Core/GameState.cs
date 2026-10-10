using System;
using System.Collections.Generic;
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

    [Serializable]
    public class GameState
    {
        public int version = 1;
        public Difficulty difficulty = Difficulty.OpenWater;
        public Language language = Language.German;
        public int day = 1;
        public float timeOfDay = 0.29f;     // 0..1 (0.25 = 06:00, 0.5 = 12:00)
        public long money;
        public long followers;
        public long totalViews;
        public List<string> ownedItems = new List<string>();
        public List<JournalEntry> journal = new List<JournalEntry>();
        public List<FootageClip> diveFootage = new List<FootageClip>();     // current dive, lost when fainting
        public List<FootageClip> bankedFootage = new List<FootageClip>();   // safe on land/boat, not uploaded yet
        public List<PendingUpload> pendingUploads = new List<PendingUpload>();
        public List<VideoRecord> videos = new List<VideoRecord>();
        public List<Delivery> deliveries = new List<Delivery>();
        public List<string> unlockedSites = new List<string>();
        public List<string> reachedMilestones = new List<string>();
        public List<string> sponsorSites = new List<string>();             // journal pages completed 100 %
        public List<long> followerHistory = new List<long>();               // followers at the start of each day
        public int questIndex;
        public float mouseSensitivity = 1f;
        public int fishCaught;
        public bool finaleReached;
    }

    /// Journal (Sealog) access helpers.
    public static class Journal
    {
        static GameState S => GameManager.I.State;

        public static JournalEntry Get(string speciesId)
        {
            var e = S.journal.Find(j => j.speciesId == speciesId);
            if (e == null)
            {
                e = new JournalEntry { speciesId = speciesId };
                S.journal.Add(e);
            }
            return e;
        }

        public static bool IsDiscovered(string speciesId)
        {
            var e = S.journal.Find(j => j.speciesId == speciesId);
            return e != null && e.discovered;
        }

        public static int DiscoveredCount => S.journal.FindAll(j => j.discovered).Count;

        public static float SiteCompletion(string siteId)
        {
            var list = SpeciesDatabase.BySite(siteId);
            if (list.Count == 0) return 0f;
            int n = 0;
            foreach (var s in list) if (IsDiscovered(s.id)) n++;
            return n / (float)list.Count;
        }

        /// Registers a good clip of an animal. Raises SpeciesDiscovered or SizeRecord. Returns (isNew, isRecord).
        public static (bool isNew, bool isRecord) RegisterFilmed(SpeciesDef def, float sizeCm, SizeTier tier, float clipValue)
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
            Progression.CheckUnlocks();
            return (isNew, isRecord);
        }
    }

    /// Footage bookkeeping: clips filmed during a dive are only safe once the player is back on land or the boat.
    public static class Footage
    {
        static GameState S => GameManager.I.State;

        public static void AddClip(FootageClip c)
        {
            c.day = S.day;
            S.diveFootage.Add(c);
            GameEvents.RaiseClipRecorded(c);
        }

        /// Call when the player safely leaves the water (boat deck, ladder, beach).
        public static void BankDive()
        {
            if (S.diveFootage.Count == 0) return;
            int n = S.diveFootage.Count;
            S.bankedFootage.AddRange(S.diveFootage);
            S.diveFootage.Clear();
            GameEvents.RaiseToast(Loc.Tr($"{n} Clips gesichert – lade sie im Handy auf FinTube hoch.",
                                         $"{n} clips saved – upload them on FinTube in your phone."));
        }

        /// Call when the player faints: this dive's footage is lost.
        public static void LoseDive()
        {
            int n = S.diveFootage.Count;
            S.diveFootage.Clear();
            if (n > 0) GameEvents.RaiseToast(Loc.Tr($"{n} Clips verloren.", $"{n} clips lost."));
        }

        public static int DiveCount => S.diveFootage.Count;
        public static int BankedCount => S.bankedFootage.Count;
    }
}
