using UnityEngine;

namespace Sealebrities
{
    /// Small procedural sounds of the stream (chat blip, coin, go-live chime, dare jingles, the sad "stream lost").
    /// Synthesized once at install (22 kHz mono), played 2D on one source; volume follows Settings.SfxVolume.
    public static class StreamSfx
    {
        static AudioSource source;
        static AudioClip blip, coin, live, ended, offer, accept, done, fail, lost;
        static float lastBlip;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { source = null; blip = coin = live = ended = offer = accept = done = fail = lost = null; lastBlip = -10f; }

        const int Rate = 22050;
        delegate float Synth(float t);

        public static void Init(GameObject host)
        {
            if (host == null || source != null) return;
            source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.ignoreListenerPause = true;
            source.priority = 40;

            blip = Make("SB_Live_Blip", 0.06f, t => Sine(t, 1480f) * Mathf.Exp(-t * 70f) * 0.35f);
            coin = Make("SB_Live_Coin", 0.42f, t => (Bell(t, 1568f) + (t > 0.07f ? Bell(t - 0.07f, 2093f) : 0f)) * 0.38f);
            live = Make("SB_Live_Go", 0.72f, t => (Note(t, 0f, 1046.5f) + Note(t, 0.11f, 1318.5f) + Note(t, 0.22f, 1568f) * 1.2f) * 0.32f);
            ended = Make("SB_Live_End", 0.6f, t => (Note(t, 0f, 1568f) + Note(t, 0.14f, 1046.5f)) * 0.3f);
            offer = Make("SB_Live_Offer", 0.32f, t => (Note(t, 0f, 784f) + Note(t, 0.1f, 1046.5f)) * 0.34f);
            accept = Make("SB_Live_Accept", 0.28f, t =>
            {
                float f = Mathf.Lerp(380f, 1250f, t / 0.28f);
                return Mathf.Sin(t * 2f * Mathf.PI * f) * Mathf.Sin(Mathf.Clamp01(t / 0.28f) * Mathf.PI) * 0.3f;
            });
            done = Make("SB_Live_Done", 0.8f, t =>
                (Note(t, 0f, 1046.5f) + Note(t, 0.09f, 1318.5f) + Note(t, 0.18f, 1568f) + Note(t, 0.27f, 2093f) * 1.3f) * 0.26f);
            fail = Make("SB_Live_Fail", 0.75f, t => Wah(t, 0f, 392f) + Wah(t, 0.22f, 370f) + Wah(t, 0.44f, 349f, 0.3f));
            lost = Make("SB_Live_Lost", 1.35f, t => Wah(t, 0f, 311f) + Wah(t, 0.28f, 294f) + Wah(t, 0.56f, 277f) + Wah(t, 0.84f, 262f, 0.5f));
        }

        static float Sine(float t, float f) => Mathf.Sin(t * 2f * Mathf.PI * f);

        static float Bell(float t, float f) =>
            (Sine(t, f) + 0.35f * Sine(t, f * 2.01f)) * Mathf.Exp(-t * 9f) * Mathf.Clamp01(t * 400f);

        static float Note(float t, float start, float f)
        {
            float u = t - start;
            if (u < 0f) return 0f;
            return (Sine(u, f) * 0.8f + Sine(u, f * 2f) * 0.2f) * Mathf.Exp(-u * 7f) * Mathf.Clamp01(u * 300f);
        }

        /// Muted-trombone "wah" with a little vibrato.
        static float Wah(float t, float start, float f, float len = 0.22f)
        {
            float u = t - start;
            if (u < 0f || u > len + 0.05f) return 0f;
            float env = Mathf.Clamp01(u * 40f) * Mathf.Clamp01((len + 0.05f - u) * 12f);
            float vib = 1f + 0.012f * Mathf.Sin(u * 2f * Mathf.PI * 6f);
            float ph = u * 2f * Mathf.PI * f * vib;
            float s = Mathf.Sin(ph) * 0.6f + Mathf.Sin(ph * 2f) * 0.25f + Mathf.Sin(ph * 3f) * 0.12f;
            return s * env * 0.22f;
        }

        static AudioClip Make(string name, float seconds, Synth fn)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float fade = Mathf.Clamp01((n - i) / (Rate * 0.01f));
                data[i] = Mathf.Clamp(fn(t) * fade, -1f, 1f);
            }
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static void Play(AudioClip c, float volume)
        {
            if (source == null || c == null) return;
            float v = volume * Settings.Volume01(Settings.SfxVolume, 1f);
            if (v <= 0.001f) return;
            source.PlayOneShot(c, v);
        }

        public static void ChatBlip()
        {
            float now = Time.unscaledTime;
            if (now - lastBlip < 0.35f) return;
            lastBlip = now;
            Play(blip, 0.25f);
        }

        public static void Coin() => Play(coin, 0.7f);
        public static void GoLive() => Play(live, 0.8f);
        public static void StreamEnded() => Play(ended, 0.7f);
        public static void DareOffered() => Play(offer, 0.8f);
        public static void DareAccepted() => Play(accept, 0.8f);
        public static void DareDone() => Play(done, 0.85f);
        public static void DareFailed() => Play(fail, 0.85f);
        public static void StreamLost() => Play(lost, 0.9f);
    }
}
