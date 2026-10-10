using UnityEngine;

namespace Sealebrities
{
    /// Entry point of the creature AI + spawning module. Called once by GameBootstrap.
    /// Creates the FaunaSpawner (ICreatureSpawner, registered as Refs.Spawner). Creature instances get a
    /// FaunaCreature component (ICreature) that registers itself in CreatureRegistry while active.
    public static class FaunaModule
    {
        public static FaunaSpawner Spawner { get; private set; }

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
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Spawner = null; }
    }
}
