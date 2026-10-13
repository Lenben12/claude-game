using UnityEngine;

namespace Sealebrities
{
    /// Root of the co-op replication features (net-sync): remote avatars, the shared boat, the host's creatures,
    /// proximity voice chat and the round-2 shared systems (NetGameSync: looks, carry, sleep vote, achievements, animal
    /// events, rescue / tow, livestream, story NPCs). Installs itself once after the first scene load (no boot step
    /// needed); Install() may also be called explicitly and is idempotent. Everything idles while Net.Mode == Offline
    /// (except the voice loopback test).
    public sealed class NetWorldSync : MonoBehaviour
    {
        public static NetWorldSync Instance { get; private set; }

        public NetAvatarSync Avatars { get; private set; }
        public NetBoatSync Boat { get; private set; }
        public NetCreatureSync Creatures { get; private set; }
        public NetVoice Voice { get; private set; }
        public NetGameSync Game { get; private set; }

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
            s.Game = go.AddComponent<NetGameSync>();
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
                   + "  mic " + (!NetVoice.MicAvailable ? "none" : NetVoice.LocalTransmitting ? "TX" : "idle")
                   + "\nsleep " + SleepVote.Lying + "/" + SleepVote.Total
                   + "  live " + (LiveState.IsLive ? LiveState.Viewers.ToString() : "-")
                   + "  event " + (FaunaEventInfo.IsActive ? FaunaEventInfo.ActiveId : "-")
                   + "  tow " + (NetRescue.LocalTowTarget >= 0 ? "→" + NetRescue.LocalTowTarget : NetRescue.LocalTowedBy >= 0 ? "←" + NetRescue.LocalTowedBy : "-")
                   + "  danger ×" + NetCoopRules.PredatorCountMultiplier.ToString("0.00");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Instance = null; }
    }
}
