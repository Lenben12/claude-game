using UnityEngine;

namespace Sealebrities
{
    /// Round 3 (GDD §20 Q48 "Wetter beeinflusst Sicht/Wellen und welche Tiere auftauchen"). The day's weather (Weather,
    /// from the save seed – identical on every co-op machine) sets
    ///  * the wave strength (Waves.Strength: calm sunny days 0.8 … breezy days 1.35),
    ///  * the under-water visibility (FXMath.WeatherFogMul: clear sunny water … murky after showers),
    ///  * spawn weights: sunny calm days bring dolphins, turtles, mantas, whale sharks and flying fish to the surface;
    ///    wind and showers send them down, wash jellies in and make the sharks bolder.
    /// Ticked every frame by FaunaThreat (every machine); values glide over ~20 s.
    public static class FaunaWeather
    {
        static float waves = 1f, fog = 1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { waves = 1f; fog = 1f; }

        public static WeatherKind Now
        {
            get
            {
                var gm = GameManager.I;
                if (gm == null || !gm.InGame) return Weather.TestOverride ?? WeatherKind.Fair;
                return Weather.Today;
            }
        }

        public static float WaveTarget(WeatherKind k)
        {
            switch (k)
            {
                case WeatherKind.Sunny: return 0.8f;
                case WeatherKind.Cloudy: return 1.1f;
                case WeatherKind.Breezy: return 1.35f;
                case WeatherKind.Showers: return 1.25f;
                default: return 1f;
            }
        }

        /// Multiplier on the under-water fog density (> 1 = murkier, shorter sight).
        public static float FogTarget(WeatherKind k)
        {
            switch (k)
            {
                case WeatherKind.Sunny: return 0.9f;
                case WeatherKind.Cloudy: return 1.1f;
                case WeatherKind.Breezy: return 1.18f;
                case WeatherKind.Showers: return 1.3f;
                default: return 1f;
            }
        }

        public static void Tick(float dt)
        {
            var k = Now;
            float a = Mathf.Clamp01(dt / 20f);
            waves = Mathf.Lerp(waves, WaveTarget(k), a);
            fog = Mathf.Lerp(fog, FogTarget(k), a);
            Waves.Strength = waves;
            FXMath.WeatherFogMul = fog;
        }

        /// Predators get bolder under grey skies (attack chance, FaunaThreat).
        public static float PredatorMul
        {
            get
            {
                switch (Now)
                {
                    case WeatherKind.Sunny: return 0.9f;
                    case WeatherKind.Cloudy: return 1.15f;
                    case WeatherKind.Breezy: return 1.1f;
                    case WeatherKind.Showers: return 1.3f;
                    default: return 1f;
                }
            }
        }

        /// Spawn weight multiplier of a species under today's weather.
        public static float SpawnMul(SpeciesDef s)
        {
            if (s == null) return 1f;
            var k = Now;
            if (s.plan == BodyPlan.Jelly)
                return k == WeatherKind.Showers ? 1.6f : k == WeatherKind.Breezy ? 1.3f : k == WeatherKind.Sunny ? 0.85f : 1f;
            if (s.IsDangerous || s.hunter)
                return k == WeatherKind.Showers ? 1.3f : k == WeatherKind.Cloudy ? 1.2f : k == WeatherKind.Sunny ? 0.9f : 1f;
            bool surfaceLover = s.plan == BodyPlan.Dolphin || s.plan == BodyPlan.Turtle || s.plan == BodyPlan.Sunfish
                                || s.id == "manta" || s.id == "whale_shark" || s.variant == "flying" || s.id == "mahi_mahi";
            if (surfaceLover)
                return k == WeatherKind.Sunny ? 1.45f : k == WeatherKind.Fair ? 1.15f : k == WeatherKind.Cloudy ? 0.9f : k == WeatherKind.Breezy ? 0.6f : 0.55f;
            return 1f;
        }
    }
}
