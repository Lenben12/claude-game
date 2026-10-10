using UnityEngine;

namespace Sealebrities
{
    /// FX module entry point: global post-processing volume, fog and camera background (above/underwater),
    /// animated caustics (sun light cookie), camera-following particles (marine snow, exhale bubbles, splash,
    /// god rays) and fully procedural audio (ambience, breathing, heartbeat, jingles).
    public static class FXModule
    {
        static FXDirector director;

        /// Creates the FX root object under <paramref name="root"/>. Safe to call more than once.
        public static void Install(Transform root)
        {
            if (director != null) return;
            var go = new GameObject("FX");
            if (root != null) go.transform.SetParent(root, false);
            director = go.AddComponent<FXDirector>();
            director.Init();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { director = null; }
    }
}
