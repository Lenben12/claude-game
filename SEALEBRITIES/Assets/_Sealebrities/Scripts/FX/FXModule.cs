using UnityEngine;

namespace Sealebrities
{
    /// FX module entry point: global post-processing volume, fog and camera background (above/underwater, the Fog Isle's
    /// fog bank), graphics tiers (Settings Low/Medium/High, no SSAO), animated caustics (sun light cookie),
    /// camera-following particles (marine snow, exhale bubbles, splash + bubble burst on diving in, bioluminescence and
    /// glowing plankton, current streaks, god rays), the view-model fill light and fully procedural audio (ambience,
    /// breathing, heartbeat, warnings, jingles, the underwater soundscape and the relaxed background music, K.6).
    /// Art direction: "How to Fish" (docs/ArtBible_HowToFish.md) – ACES filmic grading with violet shadows and a
    /// dark-violet vignette, light blue haze above water, bright turquoise shallows with ~19 m sight that darken to
    /// near black in the deep, clean graphic particles.
    /// Public surface for other modules: FXVisibility, FXQuality, FXSfx, FXMusic, FXEffects, FXFogBank, FXCurrents (FXApi.cs).
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
