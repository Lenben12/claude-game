using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Sealebrities
{
    /// One photo of the phone gallery. The PNG lives in persistentDataPath/gallery/<file>.
    [Serializable]
    public class PhotoEntry
    {
        public string file;
        public string speciesId;    // "" = no animal in focus
        public string siteId;       // "" = not at a dive site
        public int day;
        public float time;          // time of day 0..1
    }

    /// Local photo gallery (per MACHINE, never synced, independent of the save): PNG files in
    /// persistentDataPath/gallery, the list in gallery/index.json. A co-op guest (who never saves) keeps its photos, and
    /// its single-player photos stay visible during co-op. Old saves are migrated (GameState.photos → index, ImportLegacy).
    /// Add/Delete re-read the index before writing it: two game windows on one PC (co-op test) share the folder.
    /// Automated test runs (GameManager.AutomatedRun) keep the index in memory only and never write the player's file.
    /// Photos are taken by the camera's photo action (GameInput.PhotoPressed) and shown in the phone's gallery app.
    public static class Gallery
    {
        [Serializable]
        class GalleryIndex
        {
            public int version = 1;
            public List<PhotoEntry> photos = new List<PhotoEntry>();
        }

        static readonly List<PhotoEntry> list = new List<PhotoEntry>();
        static readonly List<PhotoEntry> scratch = new List<PhotoEntry>();
        static bool loaded;

        public static event Action<PhotoEntry> Added;
        public static event Action<PhotoEntry> Removed;

        public static string Folder => Path.Combine(Application.persistentDataPath, "gallery");
        static string IndexPath => Path.Combine(Folder, "index.json");

        /// All photos of this PC, oldest first.
        public static List<PhotoEntry> Photos
        {
            get
            {
                EnsureLoaded();
                return list;
            }
        }

        public static int Count => Photos.Count;

        /// Saves a readable RGBA32/RGB24 texture as PNG and adds it to the gallery. Returns null on failure.
        /// The caller still owns (and destroys) the texture.
        public static PhotoEntry Add(Texture2D tex, string speciesId, string siteId)
        {
            var gm = GameManager.I;
            if (tex == null || gm == null || gm.State == null) return null;
            byte[] png;
            try { png = tex.EncodeToPNG(); }
            catch (Exception e)
            {
                Debug.LogWarning("Sealebrities: photo could not be encoded: " + e.Message);
                return null;
            }
            if (png == null || png.Length == 0) return null;

            var s = gm.State;
            string tag = string.IsNullOrEmpty(speciesId) ? "scene" : speciesId;
            string name = "sealog_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", System.Globalization.CultureInfo.InvariantCulture) + "_" + tag + ".png";
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllBytes(Path.Combine(Folder, name), png);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Sealebrities: photo could not be saved: " + e.Message);
                return null;
            }
            var entry = new PhotoEntry { file = name, speciesId = speciesId ?? "", siteId = siteId ?? "", day = s.day, time = s.timeOfDay };
            EnsureLoaded();
            RefreshFromDisk();
            if (IndexOf(list, name) < 0) list.Add(entry);
            SortList();
            WriteIndex();
            Added?.Invoke(entry);
            return entry;
        }

        public static string PathOf(PhotoEntry e) => e == null || string.IsNullOrEmpty(e.file) ? null : Path.Combine(Folder, e.file);

        public static bool Exists(PhotoEntry e)
        {
            string p = PathOf(e);
            return p != null && File.Exists(p);
        }

        /// Loads the PNG into a new texture (the caller destroys it). Null when the file is missing or broken.
        public static Texture2D Load(PhotoEntry e)
        {
            string p = PathOf(e);
            if (p == null || !File.Exists(p)) return null;
            try
            {
                var bytes = File.ReadAllBytes(p);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(bytes))
                {
                    UnityEngine.Object.Destroy(tex);
                    return null;
                }
                tex.name = e.file;
                tex.wrapMode = TextureWrapMode.Clamp;
                return tex;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Sealebrities: photo could not be loaded: " + ex.Message);
                return null;
            }
        }

        /// Removes the entry and deletes the PNG.
        public static void Delete(PhotoEntry e)
        {
            if (e == null) return;
            EnsureLoaded();
            RefreshFromDisk();
            bool removed = list.Remove(e);
            int i = IndexOf(list, e.file);
            if (i >= 0) { list.RemoveAt(i); removed = true; }
            try
            {
                string p = PathOf(e);
                if (p != null && File.Exists(p)) File.Delete(p);
            }
            catch (Exception ex) { Debug.LogWarning("Sealebrities: photo could not be deleted: " + ex.Message); }
            if (removed)
            {
                WriteIndex();
                Removed?.Invoke(e);
            }
        }

        /// Newest photo of a species (Sealog "best own photo"), null when none.
        public static PhotoEntry LatestFor(string speciesId)
        {
            if (string.IsNullOrEmpty(speciesId)) return null;
            var l = Photos;
            for (int i = l.Count - 1; i >= 0; i--)
                if (l[i] != null && l[i].speciesId == speciesId) return l[i];
            return null;
        }

        /// Migration of saves from before the per-machine index: their GameState.photos join the index (only entries
        /// whose PNG still exists). Called by GameManager.LoadGame; the caller then clears the save's list.
        public static void ImportLegacy(List<PhotoEntry> fromSave)
        {
            if (fromSave == null || fromSave.Count == 0) return;
            EnsureLoaded();
            RefreshFromDisk();
            bool changed = false;
            for (int i = 0; i < fromSave.Count; i++)
            {
                var e = fromSave[i];
                if (e == null || string.IsNullOrEmpty(e.file) || IndexOf(list, e.file) >= 0 || !Exists(e)) continue;
                list.Add(e);
                changed = true;
            }
            if (!changed) return;
            SortList();
            WriteIndex();
        }

        /// Opens the gallery folder in the OS file browser (settings / gallery app button).
        public static void OpenFolder()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                Application.OpenURL("file:///" + Folder.Replace('\\', '/'));
            }
            catch (Exception) { }
        }

        // ------------------------------------------------------------------ index file
        static void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;
            ReadIndex(list);
            // entries whose PNG was removed by hand
            for (int i = list.Count - 1; i >= 0; i--) if (!Exists(list[i])) list.RemoveAt(i);
            SortList();
        }

        static void ReadIndex(List<PhotoEntry> into)
        {
            into.Clear();
            try
            {
                string p = IndexPath;
                if (!File.Exists(p)) return;
                var idx = JsonUtility.FromJson<GalleryIndex>(File.ReadAllText(p));
                if (idx == null || idx.photos == null) return;
                for (int i = 0; i < idx.photos.Count; i++)
                {
                    var e = idx.photos[i];
                    if (e != null && !string.IsNullOrEmpty(e.file) && IndexOf(into, e.file) < 0) into.Add(e);
                }
            }
            catch (Exception e) { Debug.LogWarning("Sealebrities: gallery index could not be read: " + e.Message); }
        }

        /// Merges what another game window on this PC wrote meanwhile (keeps our entry objects – the UI caches them).
        static void RefreshFromDisk()
        {
            if (GameManager.AutomatedRun) return;
            ReadIndex(scratch);
            for (int i = 0; i < scratch.Count; i++)
            {
                var e = scratch[i];
                if (IndexOf(list, e.file) < 0 && Exists(e)) list.Add(e);
            }
            // deleted by the other window (its PNG is gone)
            for (int i = list.Count - 1; i >= 0; i--)
                if (IndexOf(scratch, list[i].file) < 0 && !Exists(list[i])) list.RemoveAt(i);
            scratch.Clear();
        }

        static void WriteIndex()
        {
            if (GameManager.AutomatedRun) return;   // never touch the player's gallery index from a test run
            try
            {
                Directory.CreateDirectory(Folder);
                var idx = new GalleryIndex();
                idx.photos.AddRange(list);
                string p = IndexPath, tmp = p + ".tmp";
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
            catch (Exception e) { Debug.LogWarning("Sealebrities: gallery index could not be saved: " + e.Message); }
        }

        static int IndexOf(List<PhotoEntry> l, string file)
        {
            if (string.IsNullOrEmpty(file)) return -1;
            for (int i = 0; i < l.Count; i++) if (l[i] != null && l[i].file == file) return i;
            return -1;
        }

        /// File names carry the timestamp (sealog_yyyyMMdd_HHmmss_fff_…): ordinal order = oldest first.
        static readonly Comparison<PhotoEntry> ByFile = (a, b) => string.CompareOrdinal(a != null ? a.file : "", b != null ? b.file : "");
        static void SortList() => list.Sort(ByFile);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Added = null; Removed = null; list.Clear(); scratch.Clear(); loaded = false; }
    }
}
