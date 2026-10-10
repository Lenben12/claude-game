using UnityEngine;

namespace Sealebrities
{
    /// Entry point of the Filming module (hand camera + tutorial quests). Called once by GameBootstrap.
    public static class FilmingModule
    {
        static GameObject host;

        public static FilmHandCamera HandCamera { get; private set; }
        public static TutorialQuests Quests { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { host = null; HandCamera = null; Quests = null; }

        /// Creates the hand camera (registers Refs.HandCamera) and the tutorial quest chain.
        /// The camera model attaches itself to Refs.Player.View as soon as the player exists.
        public static void Install(Transform root)
        {
            if (host != null) return;
            host = new GameObject("Filming");
            if (root != null) host.transform.SetParent(root, false);
            HandCamera = host.AddComponent<FilmHandCamera>();
            Quests = host.AddComponent<TutorialQuests>();
            Refs.HandCamera = HandCamera;
        }
    }
}
