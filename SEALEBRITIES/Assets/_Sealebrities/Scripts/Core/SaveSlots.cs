using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Sealebrities
{
    /// What the main menu / the host page show about one save slot (read from the slot's file without loading it).
    public sealed class SlotInfo
    {
        public int slot;
        /// The slot has a save file.
        public bool used;
        /// The file exists but could not be read (the card offers „Löschen“ only).
        public bool broken;
        public int day;
        public long followers, money;
        public Difficulty difficulty;
        /// Real seconds played in this world (GameState.playSeconds).
        public double playSeconds;
        /// Achievements unlocked in this world (GameState.achievements).
        public int achievements;
        /// Local time of the last save (GameState.lastPlayedUtc, else the file time).
        public DateTime lastPlayed;
        public int saveSeed;
        /// Photos in the slot's gallery and the newest one (full path, null = none).
        public int photos;
        public string latestPhoto;
    }

    /// Round 3 step 6b (GDD §19b): FOUR save slots – each one a separate world.
    ///   save_slot{n}.json     the world (GameState: progress, Sealog, achievements, play time, map pins, hut, hermits …)
    ///   slot{n}/gallery/      the photos of that world (PNG + index.json + film_photos.json) – never the PC's Pictures
    ///                         folder; „Exportieren“ in the gallery app is the only way out
    ///   slots.json            which slot was played last („Fortsetzen“)
    /// Co-op guests never save; their photos of a host's world live in coop/w{seed}/ (the host's saveSeed) together with
    /// coop_player.json (the guest's own hotbar / backpack fallback for that world). Settings and the character look stay
    /// global (settings.json). The old single save (sealebrities_save.json + gallery/) moves into slot 1 once
    /// (MigrateLegacy; the old file is kept as sealebrities_save.json.migrated).
    /// Automated test runs never touch these files: they either play without a slot (Active 0 → autotest_world/) or
    /// point RootOverride at a sandbox folder.
    public static class SaveSlots
    {
        public const int Count = 4;
        public const string LegacyFileName = "sealebrities_save.json";

        /// Test sandbox: every slot file / slot folder / slots.json lives below this folder instead of persistentDataPath.
        public static string RootOverride;

        /// The slot the running world belongs to (1..4), 0 = an unsaved world (tests, -host without -save).
        public static int Active { get; private set; }

        /// Slot files changed (saved / deleted / migrated) – menus rebuild their cards.
        public static event Action Changed;

        static string defaultRoot;
        static int lastUsedCache = -1;
        static string lastUsedRoot;
        static readonly SlotInfo[] infoCache = new SlotInfo[Count + 1];
        static readonly long[] infoStamp = new long[Count + 1];
        static readonly List<PhotoEntry> tmpPhotos = new List<PhotoEntry>();

        // cached world folder (Gallery.Folder is read often – no string building per call)
        static string cachedWorld, cachedGallery, cachedRoot;
        static int cachedKind = -1, cachedKey;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            RootOverride = null; Active = 0; Changed = null; defaultRoot = null;
            lastUsedCache = -1; lastUsedRoot = null;
            for (int i = 0; i < infoCache.Length; i++) { infoCache[i] = null; infoStamp[i] = 0; }
            tmpPhotos.Clear();
            cachedWorld = null; cachedGallery = null; cachedRoot = null; cachedKind = -1; cachedKey = 0;
        }

        // ------------------------------------------------------------------ paths
        public static string Root
        {
            get
            {
                if (!string.IsNullOrEmpty(RootOverride)) return RootOverride;
                if (defaultRoot == null) defaultRoot = Application.persistentDataPath;
                return defaultRoot;
            }
        }

        public static bool Valid(int slot) => slot >= 1 && slot <= Count;
        public static string LegacyPath => Path.Combine(Root, LegacyFileName);
        public static string FileOf(int slot) => Path.Combine(Root, "save_slot" + slot.ToString(CultureInfo.InvariantCulture) + ".json");
        public static string FolderOf(int slot) => Path.Combine(Root, "slot" + slot.ToString(CultureInfo.InvariantCulture));
        public static string GalleryOf(int slot) => Path.Combine(FolderOf(slot), "gallery");
        /// A co-op guest's per-world folder for the host world with this saveSeed (automated runs outside a sandbox:
        /// autotest_coop/ – the player's own coop/ folder stays clean).
        public static string GuestFolder(int seed) =>
            Path.Combine(Root, GameManager.AutomatedRun && string.IsNullOrEmpty(RootOverride) ? "autotest_coop" : "coop",
                         "w" + seed.ToString("x8", CultureInfo.InvariantCulture));
        static string MetaPath => Path.Combine(Root, "slots.json");

        /// The save file of the running world (null = nothing to save: no slot, or a co-op guest).
        public static string ActiveFile => Valid(Active) && !Net.IsClient ? FileOf(Active) : null;

        /// The data folder of the world being played: the guest folder of the host's world, the active slot's folder,
        /// or (no slot) world_unsaved/ – autotest_world/ for automated runs.
        public static string WorldFolder
        {
            get
            {
                Refresh();
                return cachedWorld;
            }
        }

        /// WorldFolder/gallery – Gallery.Folder.
        public static string GalleryFolder
        {
            get
            {
                Refresh();
                return cachedGallery;
            }
        }

        static void Refresh()
        {
            var gm = GameManager.I;
            int kind, key;
            if (Net.IsClient && gm != null && gm.State != null) { kind = 2; key = gm.State.saveSeed; }
            else if (Valid(Active)) { kind = 1; key = Active; }
            else { kind = GameManager.AutomatedRun ? 3 : 0; key = 0; }
            string root = Root;
            if (cachedWorld != null && kind == cachedKind && key == cachedKey && ReferenceEquals(root, cachedRoot)) return;
            cachedKind = kind;
            cachedKey = key;
            cachedRoot = root;
            switch (kind)
            {
                case 2: cachedWorld = GuestFolder(key); break;
                case 1: cachedWorld = FolderOf(key); break;
                case 3: cachedWorld = Path.Combine(root, "autotest_world"); break;
                default: cachedWorld = Path.Combine(root, "world_unsaved"); break;
            }
            cachedGallery = Path.Combine(cachedWorld, "gallery");
        }

        // ------------------------------------------------------------------ state
        public static bool Exists(int slot) => Valid(slot) && File.Exists(FileOf(slot));

        public static bool AnyUsed
        {
            get
            {
                for (int i = 1; i <= Count; i++) if (Exists(i)) return true;
                return false;
            }
        }

        public static int UsedCount
        {
            get
            {
                int n = 0;
                for (int i = 1; i <= Count; i++) if (Exists(i)) n++;
                return n;
            }
        }

        /// The first empty slot, 0 when all four are used.
        public static int FirstFree()
        {
            for (int i = 1; i <= Count; i++) if (!Exists(i)) return i;
            return 0;
        }

        /// GameManager: the world now belongs to this slot (0 = unsaved). The gallery switches with it.
        internal static void SetActive(int slot)
        {
            Active = Valid(slot) ? slot : 0;
        }

        /// The slot played last (slots.json), 0 = unknown.
        public static int LastUsed
        {
            get
            {
                string root = Root;
                if (lastUsedCache >= 0 && ReferenceEquals(root, lastUsedRoot)) return lastUsedCache;
                lastUsedRoot = root;
                lastUsedCache = 0;
                try
                {
                    string p = MetaPath;
                    if (File.Exists(p))
                    {
                        var m = JsonUtility.FromJson<SlotMeta>(File.ReadAllText(p));
                        if (m != null && Valid(m.lastSlot)) lastUsedCache = m.lastSlot;
                    }
                }
                catch (Exception e) { Debug.LogWarning("Sealebrities: slots.json could not be read: " + e.Message); }
                return lastUsedCache;
            }
        }

        /// „Fortsetzen“: the last played slot, else the most recently saved one, 0 = no save at all.
        public static int ContinueSlot
        {
            get
            {
                int last = LastUsed;
                if (Exists(last)) return last;
                int best = 0;
                DateTime bestTime = DateTime.MinValue;
                for (int i = 1; i <= Count; i++)
                {
                    if (!Exists(i)) continue;
                    DateTime t;
                    try { t = File.GetLastWriteTimeUtc(FileOf(i)); } catch (Exception) { t = DateTime.MinValue; }
                    if (best == 0 || t > bestTime) { best = i; bestTime = t; }
                }
                return best;
            }
        }

        [Serializable]
        class SlotMeta
        {
            public int version = 1;
            public int lastSlot;
        }

        /// Remembers the slot for „Fortsetzen“. Automated runs outside a sandbox only keep it in memory.
        public static void SetLastUsed(int slot)
        {
            if (!Valid(slot)) return;
            lastUsedRoot = Root;
            lastUsedCache = slot;
            if (GameManager.AutomatedRun && string.IsNullOrEmpty(RootOverride)) return;
            try
            {
                Directory.CreateDirectory(Root);
                File.WriteAllText(MetaPath, JsonUtility.ToJson(new SlotMeta { lastSlot = slot }));
            }
            catch (Exception e) { Debug.LogWarning("Sealebrities: slots.json could not be written: " + e.Message); }
        }

        /// Forget the cached slot summaries (after a save / delete / migration) and tell the menus.
        public static void NotifyChanged(int slot = 0)
        {
            if (Valid(slot)) { infoCache[slot] = null; infoStamp[slot] = 0; }
            else for (int i = 0; i < infoCache.Length; i++) { infoCache[i] = null; infoStamp[i] = 0; }
            Changed?.Invoke();
        }

        /// Drops every cached value (tests switching RootOverride).
        public static void InvalidateAll()
        {
            lastUsedCache = -1;
            lastUsedRoot = null;
            cachedWorld = null;
            for (int i = 0; i < infoCache.Length; i++) { infoCache[i] = null; infoStamp[i] = 0; }
        }

        // ------------------------------------------------------------------ summaries
        /// Light view of a save file (JsonUtility skips every other field).
        [Serializable]
        class SlotHeader
        {
            public int version;
            public int saveSeed;
            public Difficulty difficulty = Difficulty.OpenWater;
            public int day = 1;
            public long money, followers;
            public List<string> achievements = new List<string>();
            public double playSeconds;
            public long lastPlayedUtc;
            public PlayerRecord localRecord;
        }

        /// Summary of a slot for the cards (cached until the file changes). Never null.
        public static SlotInfo Info(int slot)
        {
            if (!Valid(slot)) return new SlotInfo { slot = slot };
            string path = FileOf(slot);
            long stamp = 0;
            bool exists = false;
            try
            {
                var fi = new FileInfo(path);
                exists = fi.Exists;
                if (exists) stamp = fi.LastWriteTimeUtc.Ticks ^ (fi.Length * 7919L);
            }
            catch (Exception) { exists = false; }
            if (!exists)
            {
                infoCache[slot] = null;
                return new SlotInfo { slot = slot };
            }
            if (infoCache[slot] != null && infoStamp[slot] == stamp) return infoCache[slot];

            var info = new SlotInfo { slot = slot, used = true };
            try
            {
                var h = JsonUtility.FromJson<SlotHeader>(File.ReadAllText(path));
                if (h == null) info.broken = true;
                else
                {
                    info.day = Mathf.Max(1, h.day);
                    info.money = Math.Max(0, h.money);
                    info.followers = Math.Max(0, h.followers);
                    info.difficulty = DifficultySettings.Normalize(h.difficulty);
                    info.saveSeed = h.saveSeed;
                    // saves before v9 counted the time only in the host's own record
                    double secs = h.playSeconds;
                    if ((double.IsNaN(secs) || secs <= 0.0) && h.localRecord != null && h.localRecord.playMinutes > 0f) secs = h.localRecord.playMinutes * 60.0;
                    info.playSeconds = double.IsNaN(secs) || double.IsInfinity(secs) ? 0.0 : Math.Max(0.0, secs);
                    int n = 0;
                    if (h.achievements != null)
                        for (int i = 0; i < h.achievements.Count; i++)
                            if (AchievementDatabase.Get(h.achievements[i]) != null && h.achievements.IndexOf(h.achievements[i]) == i) n++;
                    info.achievements = n;
                    info.lastPlayed = h.lastPlayedUtc > 0 ? SafeLocal(h.lastPlayedUtc) : File.GetLastWriteTime(path);
                }
            }
            catch (Exception e)
            {
                info.broken = true;
                Debug.LogWarning("Sealebrities: save slot " + slot + " could not be read: " + e.Message);
            }
            if (info.broken)
            {
                try { info.lastPlayed = File.GetLastWriteTime(path); } catch (Exception) { }
            }
            // the gallery of this world: count + newest photo (the card's thumbnail)
            try
            {
                Gallery.ReadIndexAt(GalleryOf(slot), tmpPhotos);
                string newest = null;
                for (int i = 0; i < tmpPhotos.Count; i++)
                {
                    var e = tmpPhotos[i];
                    if (e == null || string.IsNullOrEmpty(e.file)) continue;
                    string p = Path.Combine(GalleryOf(slot), e.file);
                    if (!File.Exists(p)) continue;
                    info.photos++;
                    if (newest == null || string.CompareOrdinal(e.file, Path.GetFileName(newest)) > 0) newest = p;
                }
                info.latestPhoto = newest;
            }
            catch (Exception) { }
            tmpPhotos.Clear();
            infoCache[slot] = info;
            infoStamp[slot] = stamp;
            return info;
        }

        static DateTime SafeLocal(long utcTicks)
        {
            try { return new DateTime(utcTicks, DateTimeKind.Utc).ToLocalTime(); }
            catch (Exception) { return DateTime.Now; }
        }

        /// „3 h 05 min“ / „42 min“ / „< 1 min“.
        public static string FormatPlayTime(double seconds)
        {
            if (double.IsNaN(seconds) || seconds < 60.0) return Loc.Tr("< 1 min", "< 1 min");
            long mins = (long)(seconds / 60.0);
            long h = mins / 60, m = mins % 60;
            if (h <= 0) return m.ToString(CultureInfo.InvariantCulture) + " min";
            return h.ToString(CultureInfo.InvariantCulture) + " h " + m.ToString("00", CultureInfo.InvariantCulture) + " min";
        }

        /// „12.10.2026 14:30“ (de) / „10/12/2026 2:30 PM“ (en).
        public static string FormatDate(DateTime t) => t == DateTime.MinValue ? "–" : t.ToString("g", Loc.Culture);

        // ------------------------------------------------------------------ delete / new
        /// Deletes the slot for good: its save file, its data folder (photos!) and the upload collages of that world.
        /// Returns false when something could not be removed.
        public static bool Delete(int slot)
        {
            if (!Valid(slot)) return false;
            int seed = Info(slot).saveSeed;
            bool ok = WipeSlot(slot);
            if (seed != 0) DeleteCollages(seed);
            if (LastUsed == slot)
            {
                lastUsedCache = 0;
                lastUsedRoot = Root;
                if (!(GameManager.AutomatedRun && string.IsNullOrEmpty(RootOverride)))
                {
                    try { if (File.Exists(MetaPath)) File.Delete(MetaPath); } catch (Exception) { }
                }
            }
            if (Active == slot) Active = 0;
            Gallery.Reload();   // a cached photo list of that world is gone with it
            NotifyChanged(slot);
            Debug.Log("Sealebrities: save slot " + slot + " deleted" + (ok ? "" : " (some files could not be removed)"));
            return ok;
        }

        /// A new world goes into this slot: leftovers of an earlier world there (file, photos) are removed first.
        internal static void PrepareNew(int slot)
        {
            if (!Valid(slot)) return;
            int seed = Exists(slot) ? Info(slot).saveSeed : 0;
            WipeSlot(slot);
            if (seed != 0) DeleteCollages(seed);
            Gallery.Reload();
            NotifyChanged(slot);
        }

        static bool WipeSlot(int slot)
        {
            bool ok = true;
            string file = FileOf(slot);
            try { if (File.Exists(file)) File.Delete(file); } catch (Exception) { ok = false; }
            try { if (File.Exists(file + ".tmp")) File.Delete(file + ".tmp"); } catch (Exception) { }
            string folder = FolderOf(slot);
            try { if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch (Exception) { ok = false; }
            return ok;
        }

        /// FinTube collages of that world (thumbs/s{seed}…_day_N.png – UIThumbnailComposer). Real data folder only.
        static void DeleteCollages(int seed)
        {
            if (!string.IsNullOrEmpty(RootOverride) || GameManager.AutomatedRun) return;
            try
            {
                string thumbs = Path.Combine(Root, "thumbs");
                if (!Directory.Exists(thumbs)) return;
                var files = Directory.GetFiles(thumbs, "s" + seed.ToString("x8", CultureInfo.InvariantCulture) + "*_day_*.png");
                for (int i = 0; i < files.Length; i++)
                {
                    try { File.Delete(files[i]); } catch (Exception) { }
                }
            }
            catch (Exception) { }
        }

        // ------------------------------------------------------------------ migration of the single save
        /// Round 3 step 6b: the old single save (sealebrities_save.json) becomes slot 1 – together with the old per-PC
        /// gallery (gallery/ → slot1/gallery/). The old file stays as sealebrities_save.json.migrated. Returns the
        /// slot it went into (0 = nothing to migrate / failed). GameManager.Awake calls it once per start (never in
        /// automated runs); tests call it inside a RootOverride sandbox.
        public static int MigrateLegacy()
        {
            string legacy = LegacyPath;
            if (!File.Exists(legacy)) return 0;
            int target = Exists(1) ? FirstFree() : 1;
            if (target == 0)
            {
                Debug.LogWarning("Sealebrities: the old single save was not migrated – all four slots are in use.");
                return 0;
            }
            try
            {
                Directory.CreateDirectory(Root);
                File.Copy(legacy, FileOf(target), false);
                // the per-PC gallery of older versions belongs to that world now
                string oldGallery = Path.Combine(Root, "gallery");
                string dst = GalleryOf(target);
                if (Directory.Exists(oldGallery) && !Directory.Exists(dst))
                {
                    Directory.CreateDirectory(FolderOf(target));
                    Directory.Move(oldGallery, dst);
                }
                string bak = legacy + ".migrated";
                if (File.Exists(bak)) File.Delete(bak);
                File.Move(legacy, bak);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Sealebrities: the old save could not be moved into slot " + target + ": " + e.Message);
                // a half-done copy must not hide the old save: the old file is still there, the slot file goes again
                try { if (File.Exists(legacy) && File.Exists(FileOf(target))) File.Delete(FileOf(target)); } catch (Exception) { }
                return 0;
            }
            if (!Valid(LastUsed) || !Exists(LastUsed)) SetLastUsed(target);
            NotifyChanged(target);
            Debug.Log("Sealebrities: the old save is now save slot " + target);
            return target;
        }
    }
}
