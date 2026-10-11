using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Sealebrities
{
    /// Filming progress of one creature instance during the current dive (pooled, no per-frame allocations).
    public sealed class FilmInstanceTrack
    {
        public string speciesId;
        public float sizeCm;
        /// Seconds of usable footage (properly framed while recording, aimed or from the hip), capped at
        /// FilmHandCamera.MaxClipSeconds.
        public float seconds;
        /// Seconds of usable footage filmed while AIMING (only aimed footage registers the animal in the Sealog).
        public float aimedSeconds;
        public float qualitySum;   // quality x seconds
        public float actionSum;    // Activity01 x seconds
        /// The clip created once the instance reached the capture time (null before).
        public FootageClip clip;
        /// Registered in the journal (Sealog) for this dive – needs FilmHandCamera.CaptureSeconds of aimed footage.
        public bool registered;

        public float AvgQuality => seconds > 0f ? qualitySum / seconds : 0f;
        public float AvgAction => seconds > 0f ? actionSum / seconds : 0f;

        public void Reset()
        {
            speciesId = null; sizeCm = 0f; seconds = 0f; aimedSeconds = 0f; qualitySum = 0f; actionSum = 0f;
            clip = null; registered = false;
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
