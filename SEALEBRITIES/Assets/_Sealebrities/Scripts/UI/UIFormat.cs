using UnityEngine;

namespace Sealebrities
{
    /// One formatter per slider kind (1e): every value label is clamped and formatted here, so a NaN / ∞ / 1e9 that
    /// slipped through a misclick can never print "-2147483648 %" or a number in the billions again. All results are
    /// cached strings (no per-frame allocations while a slider is dragged).
    public static class UIFormat
    {
        static readonly string[] PercentCache = new string[101];
        static readonly string[] DegreeCache = new string[181];
        static readonly string[] MultCacheDe = new string[501], MultCacheEn = new string[501];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            System.Array.Clear(PercentCache, 0, PercentCache.Length);
            System.Array.Clear(DegreeCache, 0, DegreeCache.Length);
            System.Array.Clear(MultCacheDe, 0, MultCacheDe.Length);
            System.Array.Clear(MultCacheEn, 0, MultCacheEn.Length);
        }

        static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        /// 0..1 → „0 %“ … „100 %“ (NaN → 0 %, values outside are clamped).
        public static string Percent(float v01)
        {
            if (!Finite(v01)) v01 = 0f;
            int p = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(v01) * 100f), 0, 100);
            return PercentCache[p] ?? (PercentCache[p] = p + " %");
        }

        /// Multiplier „0,20×“ … „5,00×“ (culture aware, NaN → 1,00×, clamped to [min, max]).
        public static string Multiplier(float v, float min = 0.1f, float max = 5f)
        {
            if (!Finite(v)) v = 1f;
            v = Mathf.Clamp(v, Mathf.Max(0f, min), Mathf.Min(5f, max));
            int k = Mathf.Clamp(Mathf.RoundToInt(v * 100f), 0, 500);
            bool de = Loc.Current == Language.German;
            var cache = de ? MultCacheDe : MultCacheEn;
            return cache[k] ?? (cache[k] = (k / 100f).ToString("0.00", Loc.Culture) + "×");
        }

        /// Degrees „60°“ … „100°“ (NaN → min, clamped).
        public static string Degrees(float v, float min = 0f, float max = 180f)
        {
            if (!Finite(v)) v = min;
            int d = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp(v, min, max)), 0, 180);
            return DegreeCache[d] ?? (DegreeCache[d] = d + "°");
        }

        /// A finite value inside [min, max] (NaN / ∞ → fallback).
        public static float Clamp(float v, float min, float max, float fallback)
        {
            if (!Finite(v)) v = fallback;
            return Mathf.Clamp(v, min, max);
        }

        /// „8 min“ / „45 s“ for buff timers and ETAs (whole numbers, never negative).
        public static string ShortDuration(float seconds)
        {
            if (!Finite(seconds) || seconds < 0f) seconds = 0f;
            if (seconds >= 60f) return Mathf.CeilToInt(seconds / 60f) + " min";
            return Mathf.CeilToInt(seconds) + " s";
        }
    }
}
