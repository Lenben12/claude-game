using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Sealebrities
{
    /// Filming progress of one creature INDIVIDUAL during the current dive (pooled, no per-frame allocations). K.2:
    /// every detected individual keeps its own track while recording; after FilmShotEvaluator.CaptureSeconds (1.0 s) of
    /// detection it counts as a sighting and becomes a FootageClip that keeps improving up to MaxClipSeconds (8 s).
    public sealed class FilmInstanceTrack
    {
        public string speciesId;
        public float sizeCm;
        /// Seconds of detected footage while recording (capped at FilmShotEvaluator.MaxClipSeconds).
        public float seconds;
        /// Legacy (round 1: aimed seconds) – no longer used for the Sealog.
        public float aimedSeconds;
        public float qualitySum;   // frame weight × seconds
        public float actionSum;    // Activity01 × seconds
        /// The clip of this individual (null before the capture time, or when the per-species clip cap gave its slot to
        /// a better individual).
        public FootageClip clip;
        /// Legacy name: the individual was counted (sighting + clip decision) – K.2 no longer registers by filming.
        public bool registered;
        /// Sighting already counted for this individual this dive (clip or photo, L.2).
        public bool sighted;
        /// Most other animals detected at the same time (0..8, K.2 crowd bonus).
        public int crowd;
        /// Animal event id the individual was filmed in (K.3, × 1.25).
        public string eventId;
        /// Filmed in the selfie view with the diver in frame (K.4, × 1.3) / with a fitting expression (× 1.15).
        public bool selfie, selfieMatch;
        /// Last depth (m) where it was detected.
        public float depth;
        /// A Sealog photo of this individual discovered the species / set a size record (the clip carries the flags).
        public bool photoNew, photoRecord;
        /// Time.time of the last detection (old tracks are pruned).
        public float lastSeen;
        /// Whole seconds already reported through FilmFeed.ClipImproved.
        public int reportedSeconds;

        public float AvgQuality => seconds > 0f ? qualitySum / seconds : 0f;
        public float AvgAction => seconds > 0f ? actionSum / seconds : 0f;

        public void Reset()
        {
            speciesId = null; sizeCm = 0f; seconds = 0f; aimedSeconds = 0f; qualitySum = 0f; actionSum = 0f;
            clip = null; registered = false; sighted = false; crowd = 0; eventId = null; selfie = false; selfieMatch = false;
            depth = 0f; photoNew = false; photoRecord = false; lastSeen = 0f; reportedSeconds = 0;
        }
    }

    /// Reference identity for creature keys (creatures may be pooled MonoBehaviours or co-op proxies; never use
    /// Unity's Equals here).
    public sealed class FilmRefComparer : IEqualityComparer<ICreature>
    {
        public static readonly FilmRefComparer Instance = new FilmRefComparer();
        public bool Equals(ICreature a, ICreature b) => ReferenceEquals(a, b);
        public int GetHashCode(ICreature c) => RuntimeHelpers.GetHashCode(c);
    }
}
