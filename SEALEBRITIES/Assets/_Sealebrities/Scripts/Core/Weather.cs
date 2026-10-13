using UnityEngine;

namespace Sealebrities
{
    /// Daily weather kinds (round 3 weather app). APPEND only.
    public enum WeatherKind { Sunny, Fair, Cloudy, Breezy, Showers }

    /// Round 3 (GDD §19 „Wetter-App: zusätzlich Temperaturen“): a deterministic forecast per save + day (identical on
    /// every co-op machine) with air temperature by time of day and weather, and water temperature by depth. The FX /
    /// world modules may read Cloudiness / Wind01 for clouds and waves (purely cosmetic, nothing depends on it yet).
    public static class Weather
    {
        static GameState S => GameManager.I != null ? GameManager.I.State : null;

        /// Weather of a day (seeded by the save: the same on every co-op machine).
        public static WeatherKind KindOf(int day)
        {
            var s = S;
            int seed = s != null ? s.saveSeed : 1;
            if (day <= 1) return WeatherKind.Sunny;   // the first day is always sunny
            int r;
            unchecked
            {
                uint h = (uint)(seed * 73856093) ^ (uint)(day * 19349663);
                h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                r = (int)(h % 100u);
            }
            if (r < 34) return WeatherKind.Sunny;
            if (r < 60) return WeatherKind.Fair;
            if (r < 78) return WeatherKind.Cloudy;
            if (r < 92) return WeatherKind.Breezy;
            return WeatherKind.Showers;
        }

        /// Test hook (AutoShot / AutoPlaytest only): forces today's weather on this machine (null = the forecast).
        public static WeatherKind? TestOverride;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { TestOverride = null; }

        public static WeatherKind Today => TestOverride ?? KindOf(S != null ? S.day : 1);
        public static WeatherKind Tomorrow => KindOf(S != null ? S.day + 1 : 2);

        /// 0 clear … 1 overcast.
        public static float Cloudiness(WeatherKind k)
        {
            switch (k)
            {
                case WeatherKind.Sunny: return 0.1f;
                case WeatherKind.Fair: return 0.35f;
                case WeatherKind.Cloudy: return 0.75f;
                case WeatherKind.Breezy: return 0.45f;
                default: return 0.9f;
            }
        }

        /// 0 calm … 1 windy.
        public static float Wind01(WeatherKind k) => k == WeatherKind.Breezy ? 0.8f : k == WeatherKind.Showers ? 0.6f : k == WeatherKind.Cloudy ? 0.35f : 0.15f;

        /// Air temperature in °C at a time of day: 24 °C before sunrise, peak ~31 °C at 14:00 on a sunny day; clouds and
        /// showers take up to 4 °C off the peak.
        public static float AirTemp(int day, float timeOfDay)
        {
            var k = KindOf(day);
            float t = Mathf.Repeat(timeOfDay, 1f);
            // cosine curve: minimum at 05:00, maximum at 14:00
            float phase = (t - 14f / 24f) * Mathf.PI * 2f;
            float curve = (Mathf.Cos(phase) + 1f) * 0.5f;
            float min = 23.5f, max = 31f - Cloudiness(k) * 4f - (k == WeatherKind.Breezy ? 1f : 0f);
            float temp = Mathf.Lerp(min, max, curve);
            // a little day-to-day variation (± 1 °C)
            temp += ((day * 37) % 7 - 3) * 0.3f;
            return temp;
        }

        public static float AirTempNow => S != null ? AirTemp(S.day, S.timeOfDay) : 28f;

        /// Water temperature in °C at a depth: 27 °C at the surface (+0.5 in the afternoon), thermocline from 20 m,
        /// ~22 °C at 40 m, ~12 °C at 100 m, 4 °C in the deep sea.
        public static float WaterTemp(float depth, float timeOfDay = 0.5f)
        {
            depth = Mathf.Max(0f, float.IsNaN(depth) ? 0f : depth);
            float surface = 27f + Mathf.Clamp01(1f - Mathf.Abs(Mathf.Repeat(timeOfDay, 1f) - 0.62f) * 4f) * 0.5f;
            if (depth <= 20f) return surface - depth * 0.05f;
            if (depth <= 100f) return Mathf.Lerp(surface - 1f, 12f, (depth - 20f) / 80f);
            return Mathf.Lerp(12f, 4f, Mathf.Clamp01((depth - 100f) / 900f));
        }

        public static float WaterTempNow => WaterTemp(0f, S != null ? S.timeOfDay : 0.5f);

        public static string Name(WeatherKind k)
        {
            switch (k)
            {
                case WeatherKind.Sunny: return Loc.Tr("Sonnig", "Sunny");
                case WeatherKind.Fair: return Loc.Tr("Heiter", "Fair");
                case WeatherKind.Cloudy: return Loc.Tr("Bewölkt", "Cloudy");
                case WeatherKind.Breezy: return Loc.Tr("Windig", "Breezy");
                default: return Loc.Tr("Schauer", "Showers");
            }
        }

        /// Sea state text for the app.
        public static string Sea(WeatherKind k)
        {
            switch (k)
            {
                case WeatherKind.Breezy: return Loc.Tr("Leichter Seegang", "Light swell");
                case WeatherKind.Showers: return Loc.Tr("Etwas Seegang", "Some swell");
                default: return Loc.Tr("Ruhige See", "Calm sea");
            }
        }

        /// "28 °C" (rounded, culture-safe).
        public static string Format(float c) => Mathf.RoundToInt(c) + " °C";
    }
}
