using UnityEngine;

namespace Sealebrities
{
    /// How an animal reacts to a diver coming close (round 3, GDD §20 Q21 "Flucht je nach Art").
    public enum FaunaTemper : byte
    {
        /// Small and medium fish: dart away fast when a diver comes close or rushes in.
        Darter,
        /// Bottom animals (octopus, eels, crabs, seahorses, hagfish): duck down / back off slowly.
        Hider,
        /// Turtles, sea lions, dolphins, cuttlefish, big groupers: come closer (their Curious behaviour) – no flight.
        Curious,
        /// Sharks, barracuda, whales, mantas, sunfish, the monster: stay cool, only a slow turn when you bump into them.
        Cool,
        /// Jellies, sea lilies, starfish: nothing to react with.
        Drifter,
    }

    /// Round 3: per-species reaction profile + "startle" spots (a spear hit, a shark charge, the monster) that make
    /// every small fish nearby dart away for a moment. Allocation free; host-side (the animals live there) – co-op
    /// guests see the result through the proxies.
    public static class FaunaReact
    {
        const int MaxStartles = 6;
        static readonly Vector3[] sPos = new Vector3[MaxStartles];
        static readonly float[] sRad = new float[MaxStartles];
        static readonly float[] sUntil = new float[MaxStartles];
        static int next;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { for (int i = 0; i < MaxStartles; i++) sUntil[i] = 0f; next = 0; }

        public static FaunaTemper TemperOf(SpeciesDef s)
        {
            if (s == null) return FaunaTemper.Drifter;
            if (s.Hostile || s.monster) return FaunaTemper.Cool;
            switch (s.plan)
            {
                case BodyPlan.Shark: case BodyPlan.Whale: case BodyPlan.Sunfish: return FaunaTemper.Cool;
                case BodyPlan.Ray: return s.maxCm >= 250f ? FaunaTemper.Cool : FaunaTemper.Darter;
                case BodyPlan.Turtle: case BodyPlan.SeaLion: case BodyPlan.Dolphin: case BodyPlan.Cuttlefish: return FaunaTemper.Curious;
                case BodyPlan.Jelly: case BodyPlan.Starfish: return FaunaTemper.Drifter;
                case BodyPlan.Octopus: case BodyPlan.Eel: case BodyPlan.Crab: case BodyPlan.Seahorse: return FaunaTemper.Hider;
                case BodyPlan.Squid: case BodyPlan.VampireSquid: case BodyPlan.Anglerfish: return FaunaTemper.Hider;
            }
            if (s.behavior == BehaviorType.Curious) return FaunaTemper.Curious;
            if (s.variant == "lionfish") return FaunaTemper.Cool;   // venomous – it knows
            if (s.maxCm >= 160f) return FaunaTemper.Cool;           // big groupers, tuna
            return FaunaTemper.Darter;
        }

        /// Something scary happened at p (spear hit, shark charge): small fish within radius flee for `seconds`.
        public static void Startle(Vector3 p, float radius, float seconds)
        {
            float now = Time.time;
            sPos[next] = p;
            sRad[next] = radius;
            sUntil[next] = now + seconds;
            next = (next + 1) % MaxStartles;
        }

        /// The nearest active startle spot that reaches pos (false = none).
        public static bool Startled(Vector3 pos, out Vector3 from)
        {
            from = pos;
            float now = Time.time;
            for (int i = 0; i < MaxStartles; i++)
            {
                if (sUntil[i] <= now) continue;
                float r = sRad[i];
                if ((pos - sPos[i]).sqrMagnitude > r * r) continue;
                from = sPos[i];
                return true;
            }
            return false;
        }
    }
}
