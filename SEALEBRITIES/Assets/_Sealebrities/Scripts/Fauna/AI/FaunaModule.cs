using UnityEngine;

namespace Sealebrities
{
    /// Entry point of the creature AI + spawning module. Called once by GameBootstrap.
    /// Creates the FaunaSpawner (ICreatureSpawner, registered as Refs.Spawner). Creature instances get a
    /// FaunaCreature component (ICreature) that registers itself in CreatureRegistry while active.
    /// Round 2: next to the spawner live the island animals (LandFauna, LandAmbient), the animal events (FaunaEvents),
    /// mischief (FaunaMischief) and the school buddies (CreatureSwarm).
    public static class FaunaModule
    {
        public static FaunaSpawner Spawner { get; private set; }
        public static LandFauna Land { get; private set; }
        public static FaunaEvents Events { get; private set; }

        public static void Install(Transform root)
        {
            if (Spawner != null)
            {
                Refs.Spawner = Spawner;
                return;
            }

            var go = new GameObject("Fauna");
            if (root != null) go.transform.SetParent(root, false);
            Spawner = go.AddComponent<FaunaSpawner>();
            Refs.Spawner = Spawner;
            Land = go.AddComponent<LandFauna>();
            Events = go.AddComponent<FaunaEvents>();
            go.AddComponent<FaunaMischief>();
            go.AddComponent<CreatureSwarm>();
            go.AddComponent<LandAmbient>();
            go.AddComponent<FaunaBowRiders>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Spawner = null; Land = null; Events = null; }
    }
}
