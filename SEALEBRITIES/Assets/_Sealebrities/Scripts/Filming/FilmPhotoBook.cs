using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Sealebrities
{
    /// What the camera knew about one gallery photo (the gallery index itself only stores file, species, site, day).
    [Serializable]
    public class FilmPhotoMeta
    {
        public string file;
        public int day;
        /// Species id of the Sealog subject ("" = none).
        public string subject = "";
        /// 0..1 photo quality of the subject (fill × centring × light × steadiness) – 0 without a subject.
        public float quality;
        /// Distinct species detected in the photo (max 8, subject first).
        public string[] species;
        public int animals;
        public bool selfie;
        public float sizeCm;
        public int tier;
        public float depth;
    }

    /// Per-machine photo metadata next to the gallery (gallery/film_photos.json) and the FinTube thumbnail helpers
    /// (Q31 → L.4): the upload collage uses "today's best photo of each species" and its quality
    /// thumbQuality = average tile quality (photo quality, an icon tile counts 0.4) × (0.8 + 0.05 × tiles), max 1 →
    /// views × (1 + 0.15 × thumbQuality) (Progression.ThumbnailBonus). The UI composes the collage; filming knows the
    /// photos. Automated test runs keep the metadata in memory only (like Gallery).
    public static class FilmPhotoBook
    {
        [Serializable]
        class Index
        {
            public int version = 1;
            public List<FilmPhotoMeta> photos = new List<FilmPhotoMeta>();
        }

        public const int MaxEntries = 600;
        /// Quality of a collage tile without a photo (the species' 3D icon on a depth gradient – L.4).
        public const float IconTileQuality = 0.4f;
        /// A photo where the species is only in the background counts this much of its quality.
        public const float BackgroundShare = 0.6f;
        public const int MaxTiles = 5;
        /// Today's photos offered by the FinTube upload card (Q31).
        public const int MaxPickerPhotos = 8;

        static readonly List<FilmPhotoMeta> list = new List<FilmPhotoMeta>();
        static bool loaded, hooked;
        /// Round 3 step 6b: the gallery folder belongs to the world (save slot / host world) – another world reloads.
        static string loadedFolder;

        /// Raised after a photo was analysed and stored (photo entry + meta).
        public static event Action<PhotoEntry, FilmPhotoMeta> Recorded;

        static string FilePath => Path.Combine(Gallery.Folder, "film_photos.json");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { list.Clear(); loaded = false; hooked = false; Recorded = null; loadedFolder = null; memory.Clear(); }

        // ------------------------------------------------------------------ recording (FilmHandCamera)
        internal static FilmPhotoMeta Record(PhotoEntry e, FilmPhotoReport r)
        {
            if (e == null || string.IsNullOrEmpty(e.file)) return null;
            EnsureLoaded();
            var m = new FilmPhotoMeta
            {
                file = e.file,
                day = e.day,
                subject = r.subject != null ? r.subject.id : "",
                quality = r.subject != null ? Mathf.Clamp01(r.quality) : 0f,
                animals = r.animalCount,
                selfie = r.selfie,
                sizeCm = r.subjectCm,
                tier = (int)r.subjectTier,
                depth = r.cameraDepth,
            };
            int n = Mathf.Min(r.speciesCount, 8);
            m.species = new string[n];
            for (int i = 0; i < n; i++) m.species[i] = r.species[i] != null ? r.species[i].id : "";
            int old = IndexOf(e.file);
            if (old >= 0) list[old] = m; else list.Add(m);
            if (list.Count > MaxEntries) list.RemoveRange(0, list.Count - MaxEntries);
            Write();
            Recorded?.Invoke(e, m);
            return m;
        }

        // ------------------------------------------------------------------ queries
        public static FilmPhotoMeta Find(PhotoEntry e) => e != null ? Find(e.file) : null;

        public static FilmPhotoMeta Find(string file)
        {
            if (string.IsNullOrEmpty(file)) return null;
            EnsureLoaded();
            int i = IndexOf(file);
            return i >= 0 ? list[i] : null;
        }

        /// 0..1 quality of a photo as a thumbnail of its subject. Unknown photos fall back to the Sealog's best photo
        /// quality (the photo was taken before this file existed / in another session) or 0.5 for a tagged photo.
        public static float Quality(PhotoEntry e)
        {
            if (e == null) return 0f;
            var m = Find(e.file);
            if (m != null && !string.IsNullOrEmpty(m.subject)) return m.quality;
            if (m != null) return 0f;
            if (string.IsNullOrEmpty(e.speciesId)) return 0f;
            var j = Journal.Find(e.speciesId);
            if (j != null && j.bestPhotoId == e.file && j.bestPhotoQuality > 0f) return Mathf.Clamp01(j.bestPhotoQuality);
            return 0.5f;
        }

        /// Quality of a photo as a collage tile for one species: its own Sealog photo = full quality, a photo where the
        /// species is only in the frame = BackgroundShare × max(quality, 0.5), not in the photo = 0.
        public static float QualityFor(PhotoEntry e, string speciesId)
        {
            if (e == null || string.IsNullOrEmpty(speciesId)) return 0f;
            var m = Find(e.file);
            if (m != null)
            {
                if (m.subject == speciesId) return m.quality;
                if (m.species != null)
                    for (int i = 0; i < m.species.Length; i++)
                        if (m.species[i] == speciesId) return BackgroundShare * Mathf.Max(m.quality, 0.5f);
                return 0f;
            }
            return e.speciesId == speciesId ? Quality(e) : 0f;
        }

        /// The best photo of a species taken on `day` (subject photos first), null when none.
        public static PhotoEntry BestOnDay(string speciesId, int day)
        {
            var photos = Gallery.Photos;
            PhotoEntry best = null;
            float bq = 0f;
            for (int i = photos.Count - 1; i >= 0; i--)
            {
                var p = photos[i];
                if (p == null || p.day != day) continue;
                float q = QualityFor(p, speciesId);
                if (q > bq) { bq = q; best = p; }
            }
            return best;
        }

        /// The best photo of a species ever taken on this PC (Sealog detail page), null when none.
        public static PhotoEntry BestEver(string speciesId)
        {
            var photos = Gallery.Photos;
            PhotoEntry best = null;
            float bq = 0f;
            for (int i = photos.Count - 1; i >= 0; i--)
            {
                float q = QualityFor(photos[i], speciesId);
                if (q > bq) { bq = q; best = photos[i]; }
            }
            return best;
        }

        /// Today's photos for the FinTube picker (max 8): Sealog photos first, best quality first.
        public static void PhotosOfDay(int day, List<PhotoEntry> into, int max = MaxPickerPhotos)
        {
            into.Clear();
            var photos = Gallery.Photos;
            for (int i = photos.Count - 1; i >= 0; i--)
                if (photos[i] != null && photos[i].day == day) into.Add(photos[i]);
            into.Sort(ByQualityDesc);
            if (into.Count > max) into.RemoveRange(max, into.Count - max);
        }

        static readonly Comparison<PhotoEntry> ByQualityDesc = (a, b) => Quality(b).CompareTo(Quality(a));

        /// Collage tile quality for a species on `day`: its best photo of that day, else the icon tile (0.4).
        public static float TileQuality(string speciesId, int day)
        {
            var p = BestOnDay(speciesId, day);
            float q = p != null ? QualityFor(p, speciesId) : 0f;
            return Mathf.Max(q, IconTileQuality);
        }

        /// L.4 thumbQuality of a collage of these species (max 5 tiles) for an upload of `day`:
        /// average tile quality × (0.8 + 0.05 × tiles), clamped to 0..1. Empty → 0.
        public static float ThumbQuality(IList<string> species, int day)
        {
            if (species == null) return 0f;
            int tiles = 0;
            float sum = 0f;
            for (int i = 0; i < species.Count && tiles < MaxTiles; i++)
            {
                if (SpeciesDatabase.Get(species[i]) == null) continue;
                sum += TileQuality(species[i], day);
                tiles++;
            }
            if (tiles == 0) return 0f;
            return Mathf.Clamp01(sum / tiles * (0.8f + 0.05f * tiles));
        }

        /// Default collage species: the species with the best clip values (max 5) – fills `into`.
        public static void DefaultCollage(List<FootageClip> clips, List<string> into)
        {
            into.Clear();
            if (clips == null) return;
            // clips from Progression.SelectBestClips are already one per species, best first
            for (int i = 0; i < clips.Count && into.Count < MaxTiles; i++)
            {
                var c = clips[i];
                if (c == null || string.IsNullOrEmpty(c.speciesId) || into.Contains(c.speciesId)) continue;
                into.Add(c.speciesId);
            }
        }

        // ------------------------------------------------------------------ file
        static int IndexOf(string file)
        {
            for (int i = list.Count - 1; i >= 0; i--) if (list[i] != null && list[i].file == file) return i;
            return -1;
        }

        static void EnsureLoaded()
        {
            if (!hooked)
            {
                hooked = true;
                Gallery.Removed += OnPhotoRemoved;
            }
            string folder = Gallery.Folder;
            if (loaded && ReferenceEquals(folder, loadedFolder)) return;
            // automated runs keep the metadata in memory only: remember it per world folder
            bool memOnly = GameManager.AutomatedRun && string.IsNullOrEmpty(SaveSlots.RootOverride);
            if (loaded && loadedFolder != null && memOnly) memory[loadedFolder] = new List<FilmPhotoMeta>(list);
            loaded = true;
            loadedFolder = folder;
            if (memOnly && memory.TryGetValue(folder, out var mem)) { list.Clear(); list.AddRange(mem); }
            else Read(list);
        }

        static readonly Dictionary<string, List<FilmPhotoMeta>> memory = new Dictionary<string, List<FilmPhotoMeta>>();

        static void OnPhotoRemoved(PhotoEntry e)
        {
            if (e == null) return;
            int i = IndexOf(e.file);
            if (i < 0) return;
            list.RemoveAt(i);
            Write();
        }

        static void Read(List<FilmPhotoMeta> into)
        {
            into.Clear();
            try
            {
                string p = FilePath;
                if (!File.Exists(p)) return;
                var idx = JsonUtility.FromJson<Index>(File.ReadAllText(p));
                if (idx == null || idx.photos == null) return;
                for (int i = 0; i < idx.photos.Count; i++)
                {
                    var m = idx.photos[i];
                    if (m == null || string.IsNullOrEmpty(m.file)) continue;
                    if (m.subject == null) m.subject = "";
                    into.Add(m);
                }
            }
            catch (Exception e) { Debug.LogWarning("Sealebrities: photo metadata could not be read: " + e.Message); }
        }

        static void Write()
        {
            if (GameManager.AutomatedRun && string.IsNullOrEmpty(SaveSlots.RootOverride)) return;   // never touch the player's files from a test run (sandbox ok)
            try
            {
                // another game window on this PC (co-op test) may have added photos meanwhile: merge them
                var disk = new List<FilmPhotoMeta>();
                Read(disk);
                for (int i = 0; i < disk.Count; i++)
                    if (IndexOf(disk[i].file) < 0 && File.Exists(Path.Combine(Gallery.Folder, disk[i].file))) list.Insert(0, disk[i]);
                if (list.Count > MaxEntries) list.RemoveRange(0, list.Count - MaxEntries);

                Directory.CreateDirectory(Gallery.Folder);
                var idx = new Index();
                idx.photos.AddRange(list);
                string p = FilePath, tmp = p + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(idx));
                if (!File.Exists(p)) File.Move(tmp, p);
                else
                {
                    try { File.Replace(tmp, p, null); }
                    catch (Exception)
                    {
                        File.Copy(tmp, p, true);
                        File.Delete(tmp);
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning("Sealebrities: photo metadata could not be saved: " + e.Message); }
        }
    }
}
