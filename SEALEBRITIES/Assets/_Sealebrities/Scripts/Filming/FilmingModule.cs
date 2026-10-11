using UnityEngine;

namespace Sealebrities
{
    /// Entry point of the Filming module (hand camera + photos + tutorial quests). Called once by GameBootstrap.
    ///   • FilmHandCamera – the camera as a hotbar item (Hands: HoldItem.Camera), filming, Sealog registration while
    ///     aiming, photos into the phone gallery (Gallery) and automatic discovery photos.
    ///   • TutorialQuests – the tutorial chain + long-term goals for the phone app "Aufgaben" (GameEvents.QuestChanged,
    ///     TutorialQuests.Current); progression is evaluated where Net.HasAuthority.
    public static class FilmingModule
    {
        static GameObject host;

        public static FilmHandCamera HandCamera { get; private set; }
        public static TutorialQuests Quests { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { host = null; HandCamera = null; Quests = null; }

        /// Creates the hand camera (registers Refs.HandCamera) and the tutorial quest chain.
        /// The camera model attaches itself to Refs.Player.View as soon as the player exists and is only shown while
        /// the camera is the active item of a hand.
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
