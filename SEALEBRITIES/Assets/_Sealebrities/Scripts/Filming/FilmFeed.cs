using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Sealebrities
{
    /// What the LOCAL hand camera sees right now – the read-only hook for the livestream (C.1 "the stream uses the hand
    /// camera whenever it is in hand", C.2 hype targets, C.5 dares, C.6 chat triggers, C.8 multi-cam StreamFeed), the
    /// sponsor conditions (K.7) and anyone else who wants to react to the camera. Updated by FilmHandCamera on every
    /// detection tick (0.12 s) while the camera is in hand – recording is NOT needed – and cleared when it is put away.
    /// Everything here is allocation free; the indexed accessors are valid until the next Ticked event.
    ///
    /// Typical use (livestream):
    ///   hype target  = FilmFeed.Detected > 0 ? 1 + 0.004 × FilmFeed.BestBaseValue × tierMult : (SecondsWithoutAnimal > 15 ? 0.6 : 1)
    ///   danger spike = FilmFeed.DangerClose
    ///   dare_three   = count distinct ids from SpeciesAt(i) over 30 s; dare_moray = InFrame("moray", 1.5f) for 3 s
    ///   StreamFeed   = FilmFeed.GetStreamFeed(out id, out quality, out tier, out danger, out inWater)
    public static class FilmFeed
    {
        // ------------------------------------------------------------------ camera state
        /// The camera is the active item of a hand (local player).
        public static bool CameraHeld;
        /// Held, gameplay not blocked, not seated/fainting – detection runs.
        public static bool Usable;
        public static bool Recording, Aiming, Selfie, Underwater, NightVision;
        /// Meters below the surface at the lens (0 above water) and the L.1 zone id at the lens.
        public static float Depth;
        public static string Zone;

        // ------------------------------------------------------------------ what is in frame
        /// Animals detected in frame right now (K.2 rules) and their distinct species.
        public static int Detected, Species;
        /// The most valuable detected animal (weight × base value × size multiplier).
        public static SpeciesDef BestSpecies;
        public static ICreature BestCreature;
        public static float BestQuality, BestDistance;
        public static SizeTier BestTier;
        public static int BestBaseValue;
        /// The Sealog photo subject (null = none) and its photo quality 0..1.
        public static SpeciesDef Subject;
        public static float SubjectQuality;
        /// An undiscovered species / a Huge or Legendary animal is detected.
        public static bool NewSpeciesInFrame, WowInFrame;
        /// A dangerous animal (predator / ambush) is detected, and within 5 m (C.2 hype ×1.4, chat "behind you!!!").
        public static bool DangerInFrame, DangerClose;
        public static SpeciesDef DangerSpecies;
        public static float DangerDistance = float.MaxValue;
        /// Time.time of the last tick with at least one animal detected (−1000 = never).
        public static float LastAnimalTime = -1000f;
        /// Most animals detected at once while recording during the current dive (sponsor "clip with ≥ 20 fish").
        public static int PeakDetectedWhileRecording;

        /// Raised after every detection tick (also once with Detected = 0 when the camera is put away).
        public static event Action Ticked;
        /// A clip of this dive got longer or better (seconds cross a whole second, quality improved) – sponsor
        /// conditions like "film an octopus for 8 s" (K.7). Clip creation itself is GameEvents.ClipRecorded.
        public static event Action<FootageClip> ClipImproved;

        static FilmDetector source;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            source = null;
            Ticked = null;
            ClipImproved = null;
            ResetValues();
            LastAnimalTime = -1000f;
            PeakDetectedWhileRecording = 0;
        }

        static void ResetValues()
        {
            CameraHeld = Usable = Recording = Aiming = Selfie = Underwater = NightVision = false;
            Depth = 0f; Zone = null;
            Detected = 0; Species = 0;
            BestSpecies = null; BestCreature = null; BestQuality = 0f; BestDistance = 0f; BestTier = SizeTier.Normal; BestBaseValue = 0;
            Subject = null; SubjectQuality = 0f;
            NewSpeciesInFrame = WowInFrame = DangerInFrame = DangerClose = false;
            DangerSpecies = null; DangerDistance = float.MaxValue;
        }

        /// Seconds since the camera last showed an animal (C.2 "nothing on camera for > 15 s" = boring).
        public static float SecondsWithoutAnimal => Mathf.Max(0f, Time.time - LastAnimalTime);

        // ------------------------------------------------------------------ indexed access (valid until the next tick)
        public static int Count => source != null && Usable ? source.Count : 0;

        public static ICreature CreatureAt(int i) => Valid(i) ? source.Creature[i] : null;
        public static SpeciesDef SpeciesAt(int i) => Valid(i) ? source.Species[i] : null;
        public static float DistanceAt(int i) => Valid(i) ? source.Shot[i].distance : float.MaxValue;
        /// K.2 frame weight of that animal (its clip quality contribution).
        public static float WeightAt(int i) => Valid(i) ? source.Shot[i].weight : 0f;
        /// Projected size in screen heights.
        public static float ExtentAt(int i) => Valid(i) ? source.Shot[i].extent : 0f;
        public static SizeTier TierAt(int i)
        {
            var c = CreatureAt(i);
            return c != null ? c.Tier : SizeTier.Normal;
        }

        static bool Valid(int i) => source != null && Usable && i >= 0 && i < source.Count;

        /// A detected animal of this species within maxDistance meters (dare "moray from 1.5 m").
        public static bool InFrame(string speciesId, float maxDistance = float.MaxValue)
        {
            int n = Count;
            for (int i = 0; i < n; i++)
            {
                var sp = source.Species[i];
                if (sp != null && string.Equals(sp.id, speciesId, StringComparison.Ordinal) && source.Shot[i].distance <= maxDistance) return true;
            }
            return false;
        }

        /// This individual is detected in frame right now.
        public static bool InFrame(ICreature c)
        {
            if (c == null) return false;
            int n = Count;
            for (int i = 0; i < n; i++) if (ReferenceEquals(source.Creature[i], c)) return true;
            return false;
        }

        /// A detected animal matching a topic tag (FilmTags: "shark", "ray", "turtle", "octopus" …) within maxDistance.
        public static bool InFrameTag(string tag, float maxDistance = float.MaxValue)
        {
            int n = Count;
            for (int i = 0; i < n; i++)
                if (source.Shot[i].distance <= maxDistance && FilmTags.Matches(source.Species[i], tag)) return true;
            return false;
        }

        /// Number of detected animals matching a topic tag.
        public static int CountTag(string tag)
        {
            int n = Count, k = 0;
            for (int i = 0; i < n; i++) if (FilmTags.Matches(source.Species[i], tag)) k++;
            return k;
        }

        /// Number of detected animals of one species.
        public static int CountSpecies(string speciesId)
        {
            int n = Count, k = 0;
            for (int i = 0; i < n; i++)
            {
                var sp = source.Species[i];
                if (sp != null && string.Equals(sp.id, speciesId, StringComparison.Ordinal)) k++;
            }
            return k;
        }

        /// C.8 multi-cam feed of this machine (2 Hz by the livestream/net engineers): best detected species id (null =
        /// none), its frame weight, size tier, a dangerous animal within 5 m on camera, and whether the lens is in water.
        public static void GetStreamFeed(out string speciesId, out float quality, out int tier, out bool danger, out bool inWater)
        {
            bool any = Usable && Detected > 0 && BestSpecies != null;
            speciesId = any ? BestSpecies.id : null;
            quality = any ? BestQuality : 0f;
            tier = any ? (int)BestTier : 0;
            danger = Usable && DangerClose;
            inWater = Underwater;
        }

        // ------------------------------------------------------------------ publishing (FilmHandCamera)
        internal static void Publish(FilmHandCamera cam, FilmDetector det, string zone, float depth)
        {
            source = det;
            CameraHeld = cam.IsHeld;
            Usable = true;
            Recording = cam.IsRecording;
            Aiming = cam.IsAiming;
            Selfie = cam.IsSelfie;
            NightVision = cam.NightVisionOn;
            Underwater = depth > 0f;
            Depth = depth;
            Zone = zone;
            Detected = det.Count;
            Species = det.DistinctSpecies;
            if (det.Best >= 0)
            {
                var c = det.Creature[det.Best];
                BestCreature = c;
                BestSpecies = det.Species[det.Best];
                BestQuality = det.Shot[det.Best].weight;
                BestDistance = det.Shot[det.Best].distance;
                BestTier = c != null && !(c is Object o && o == null) ? c.Tier : SizeTier.Normal;
                BestBaseValue = BestSpecies != null ? BestSpecies.baseValue : 0;
            }
            else
            {
                BestCreature = null; BestSpecies = null; BestQuality = 0f; BestDistance = 0f; BestTier = SizeTier.Normal; BestBaseValue = 0;
            }
            Subject = det.Subject >= 0 ? det.Species[det.Subject] : null;
            SubjectQuality = det.SubjectQuality;
            NewSpeciesInFrame = false;
            for (int i = 0; i < det.Count; i++) if (!det.Known[i]) { NewSpeciesInFrame = true; break; }
            WowInFrame = det.WowInFrame;
            DangerInFrame = det.DangerInFrame;
            DangerClose = det.DangerClose;
            DangerSpecies = det.DangerIndex >= 0 ? det.Species[det.DangerIndex] : null;
            DangerDistance = det.DangerDistance;
            if (det.Count > 0) LastAnimalTime = Time.time;
            if (Recording && det.Count > PeakDetectedWhileRecording) PeakDetectedWhileRecording = det.Count;
            Ticked?.Invoke();
        }

        /// The camera was put away / the game left: nothing is on camera any more.
        internal static void Clear(bool raise)
        {
            bool was = Usable;
            source = null;
            ResetValues();
            if (raise && was) Ticked?.Invoke();
        }

        internal static void ResetDive() => PeakDetectedWhileRecording = 0;

        internal static void RaiseClipImproved(FootageClip c) => ClipImproved?.Invoke(c);
    }
}
