using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Sealebrities
{
    /// Filming progress of one creature instance during the current dive (pooled, no per-frame allocations).
    public sealed class FilmInstanceTrack
    {
        public string speciesId;
        public float sizeCm;
        /// Seconds filmed with quality >= FilmHandCamera.MinQuality (capped at FilmHandCamera.MaxClipSeconds).
        public float seconds;
        public float qualitySum;   // quality x seconds
        public float actionSum;    // Activity01 x seconds
        /// The clip created once the instance reached the capture time (null before).
        public FootageClip clip;
        public JournalEntry entry;

        public float AvgQuality => seconds > 0f ? qualitySum / seconds : 0f;
        public float AvgAction => seconds > 0f ? actionSum / seconds : 0f;

        public void Reset()
        {
            speciesId = null; sizeCm = 0f; seconds = 0f; qualitySum = 0f; actionSum = 0f; clip = null; entry = null;
        }
    }

    /// Reference identity for creature keys (creatures may be pooled MonoBehaviours; never use Unity's Equals here).
    public sealed class FilmRefComparer : IEqualityComparer<ICreature>
    {
        public static readonly FilmRefComparer Instance = new FilmRefComparer();
        public bool Equals(ICreature a, ICreature b) => ReferenceEquals(a, b);
        public int GetHashCode(ICreature c) => RuntimeHelpers.GetHashCode(c);
    }
}
