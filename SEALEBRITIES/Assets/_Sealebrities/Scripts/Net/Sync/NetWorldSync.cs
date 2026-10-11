using UnityEngine;

namespace Sealebrities
{
    /// Root of the co-op replication features (net-sync): remote avatars, the shared boat, the host's creatures and
    /// proximity voice chat. Installs itself once after the first scene load (no boot step needed); Install() may also
    /// be called explicitly and is idempotent. Everything idles while Net.Mode == Offline (except the voice loopback test).
    /// Only the Core/Net.cs contract is used (NetBus, Net, NetFlags, Divers, DiverModels, IDiverPuppet).
    public sealed class NetWorldSync : MonoBehaviour
    {
        public static NetWorldSync Instance { get; private set; }

        public NetAvatarSync Avatars { get; private set; }
        public NetBoatSync Boat { get; private set; }
        public NetCreatureSync Creatures { get; private set; }
        public NetVoice Voice { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoInstall() => Install();

        public static NetWorldSync Install()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("NetSync");
            DontDestroyOnLoad(go);
            var s = go.AddComponent<NetWorldSync>();
            s.Avatars = go.AddComponent<NetAvatarSync>();
            s.Boat = go.AddComponent<NetBoatSync>();
            s.Creatures = go.AddComponent<NetCreatureSync>();
            s.Voice = go.AddComponent<NetVoice>();
            return s;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// True while the local game is running (not the main menu / loading).
        public static bool InGame
        {
            get
            {
                var gm = GameManager.I;
                return gm != null && gm.InGame;
            }
        }

        /// Unity-aware null check for interface references that may point at destroyed objects.
        public static bool Alive(object o) => o != null && !(o is Object uo && uo == null);

        /// One-line replication status for the net overlay / bug reports (allocates – call a few times per second).
        public static string Summary()
        {
            var s = Instance;
            int avatars = 0, visible = 0;
            if (s != null && s.Avatars != null)
            {
                var all = s.Avatars.All;
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i] == null) continue;
                    avatars++;
                    if (all[i].Visible) visible++;
                }
            }
            int proxies = s != null && s.Creatures != null ? s.Creatures.ProxyCount : 0;
            var spawner = FaunaModule.Spawner;
            int real = spawner != null ? spawner.ActiveCount : 0;
            return "sync avatars " + visible + "/" + avatars
                   + "  boat driver " + NetFlags.BoatDriverId + (NetFlags.BoatRemote ? " (remote)" : " (local)")
                   + "  creatures " + (NetFlags.CreaturesRemote ? proxies + " proxies" : real + " local")
                   + "  mic " + (!NetVoice.MicAvailable ? "none" : NetVoice.LocalTransmitting ? "TX" : "idle");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Instance = null; }
    }
}
