using UnityEngine;

namespace Sealebrities
{
    /// DontDestroyOnLoad pump of SteamService: SteamAPI.RunCallbacks + the Steam lobby tick every frame (before the co-op
    /// pump NetCoreRunner at -800, so connection status callbacks are queued before the transport is polled), and the
    /// clean Steam shutdown on quit. Created by SteamService.Init when Steam is available.
    [DefaultExecutionOrder(-950)]
    public sealed class SteamServiceRunner : MonoBehaviour
    {
        static SteamServiceRunner instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { instance = null; }

        internal static void Ensure()
        {
            if (instance != null) return;
            var go = new GameObject("SB_Steam");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<SteamServiceRunner>();
        }

        void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
        }

        void Update()
        {
            SteamService.RunCallbacks();
            SteamLobby.Tick(Mathf.Min(Time.unscaledDeltaTime, 0.25f));
        }

        void OnApplicationQuit()
        {
            SteamService.Shutdown();
        }

        void OnDestroy()
        {
            if (instance != this) return;
            SteamService.Shutdown();
            instance = null;
        }
    }
}
