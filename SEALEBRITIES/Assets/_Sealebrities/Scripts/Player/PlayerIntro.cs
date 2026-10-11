using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Launch intro ("trailer", ~23 s, any key skips): a camera flight over the island at sunrise light, a swoop along the
    /// beach and the dock, a plunge into the house reef with a school of fish and a turtle, a dark deep-sea shot with an
    /// anglerfish eye, and back up to the crew on the dock – which is exactly where the main-menu camera starts.
    /// Localized captions are published through Caption (the UI shows them; without a listener a simple fallback is
    /// drawn). Skipped automatically with -host, -join, -autoshot, -autoplaytest or -autoplaytest-mp.
    public static class PlayerIntro
    {
        /// True while the intro owns the camera.
        public static bool Playing { get; private set; }
        /// Caption text for the current shot ("" = clear).
        public static event Action<string> Caption;

        static Action onDone;
        static PlayerIntroRunner runner;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Playing = false; Caption = null; onDone = null; runner = null; }

        /// Plays the intro and calls onDone afterwards (also when skipped or impossible – then immediately).
        public static void Play(Action onDone)
        {
            if (Playing)
            {
                var prev = PlayerIntro.onDone;
                PlayerIntro.onDone = () => { prev?.Invoke(); onDone?.Invoke(); };
                return;
            }
            Camera cam = null;
            var pc = Refs.Player as PlayerController;
            if (pc != null) cam = pc.View;
            var gm = GameManager.I;
            if (ShouldSkip() || cam == null || !WorldInfo.Built || (gm != null && gm.InGame))
            {
                SafeInvoke(onDone);
                return;
            }
            PlayerIntro.onDone = onDone;
            Playing = true;
            var go = new GameObject("PlayerIntro");
            runner = go.AddComponent<PlayerIntroRunner>();
            runner.Begin(cam);
        }

        /// Stops the intro right away (as if skipped).
        public static void Skip()
        {
            if (Playing) Finish();
        }

        internal static void Finish()
        {
            if (!Playing) return;
            Playing = false;
            if (runner != null)
            {
                runner.Cleanup();
                UnityEngine.Object.Destroy(runner.gameObject);
            }
            runner = null;
            RaiseCaption("");
            var d = onDone;
            onDone = null;
            SafeInvoke(d);
        }

        internal static void RaiseCaption(string text) => Caption?.Invoke(text);
        internal static bool HasCaptionListener => Caption != null;

        static void SafeInvoke(Action a)
        {
            if (a == null) return;
            try { a(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        static bool ShouldSkip()
        {
            if (Application.isBatchMode) return true;
            string[] args;
            try { args = Environment.GetCommandLineArgs(); }
            catch (Exception) { return false; }
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (string.Equals(a, "-host", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "-join", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(a, "-autoshot", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "-autoplaytest", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(a, "-autoplaytest-mp", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "-nointro", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }

}
