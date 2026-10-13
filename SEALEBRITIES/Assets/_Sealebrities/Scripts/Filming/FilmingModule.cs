using UnityEngine;

namespace Sealebrities
{
    /// Entry point of the Filming module (hand camera, photos, Sealog photos, detection feed, tutorial quests).
    /// Called once by GameBootstrap.
    ///   • FilmHandCamera – the camera as a hotbar item (Hands: HoldItem.Camera): K.2 auto-recording with automatic
    ///     detection of every animal in frame, clips per individual, smart tape, Sealog photos (Journal.RegisterPhoto),
    ///     sightings (L.2), selfie recording (K.4), zoom lens + night vision (Q32), golden camera (Q52).
    ///     Publishes ViewfinderInfo (ui) and FilmFeed (livestream / sponsors / anyone).
    ///   • FilmChallenges – K.3 daily photo challenge checks; FilmPhotoBook – photo metadata + FinTube collage quality
    ///     (L.4); FilmLegends + FilmLegendSpawner – post-finale legend & record hunt; FilmTrophy – the golden camera
    ///     trophy (props mount it with FilmTrophy.Mount(shelfAnchor)).
    ///   • TutorialQuests – the tutorial chain + long-term goals (owned by the npc+story engineer).
    public static class FilmingModule
    {
        static GameObject host;

        public static FilmHandCamera HandCamera { get; private set; }
        public static TutorialQuests Quests { get; private set; }
        public static FilmLegendSpawner Legends { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { host = null; HandCamera = null; Quests = null; Legends = null; }

        /// Creates the hand camera (registers Refs.HandCamera), the legend spawner and the tutorial quest chain.
        /// The camera model attaches itself to Refs.Player.View as soon as the player exists and is only shown while
        /// the camera is the active item of a hand.
        public static void Install(Transform root)
        {
            if (host != null) return;
            host = new GameObject("Filming");
            if (root != null) host.transform.SetParent(root, false);
            HandCamera = host.AddComponent<FilmHandCamera>();
            Legends = host.AddComponent<FilmLegendSpawner>();
            Quests = host.AddComponent<TutorialQuests>();
            Refs.HandCamera = HandCamera;
        }
    }
}
