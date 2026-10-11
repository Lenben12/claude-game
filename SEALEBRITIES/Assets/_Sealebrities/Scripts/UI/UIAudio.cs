using UnityEngine;

namespace Sealebrities
{
    /// Procedural UI sounds (phone ping for push notifications, soft click for buttons, low "nope" blip).
    /// Clips are synthesized once at startup; volume follows Settings.SfxVolume (master is AudioListener.volume).
    public static class UIAudio
    {
        static AudioSource source;
        static AudioClip ping, click, pop, nope;
        static float lastClick;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { source = null; ping = null; click = null; pop = null; nope = null; lastClick = -10f; }

        public static void Init(GameObject host)
        {
            if (host == null || source != null) return;
            source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.ignoreListenerPause = true;
            source.priority = 32;
            ping = Make("SB_UI_Ping", 0.42f, (t, i) =>
            {
                // two bright bell partials, second note a fifth higher (classic phone "ding-ding")
                float a = t < 0.14f ? Bell(t, 1318.5f) : 0f;
                float b = t >= 0.12f ? Bell(t - 0.12f, 1975.5f) : 0f;
                return (a + b) * 0.42f;
            });
            click = Make("SB_UI_Click", 0.05f, (t, i) => Mathf.Sin(t * 2f * Mathf.PI * 1900f) * Mathf.Exp(-t * 110f) * 0.35f);
            pop = Make("SB_UI_Pop", 0.12f, (t, i) =>
            {
                float f = Mathf.Lerp(420f, 900f, t / 0.12f);
                return Mathf.Sin(t * 2f * Mathf.PI * f) * Mathf.Exp(-t * 32f) * 0.4f;
            });
            nope = Make("SB_UI_Nope", 0.16f, (t, i) => Mathf.Sin(t * 2f * Mathf.PI * (t < 0.07f ? 330f : 247f)) * Mathf.Exp(-t * 14f) * 0.3f);
        }

        static float Bell(float t, float f)
        {
            return (Mathf.Sin(t * 2f * Mathf.PI * f) + 0.35f * Mathf.Sin(t * 2f * Mathf.PI * f * 2.01f)) * Mathf.Exp(-t * 9f) * Mathf.Clamp01(t * 400f);
        }

        delegate float Synth(float t, int i);

        static AudioClip Make(string name, float seconds, Synth fn)
        {
            const int rate = 44100;
            int n = Mathf.CeilToInt(seconds * rate);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float fade = Mathf.Clamp01((n - i) / (rate * 0.01f));
                data[i] = Mathf.Clamp(fn(t, i) * fade, -1f, 1f);
            }
            var clip = AudioClip.Create(name, n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static void Play(AudioClip c, float volume)
        {
            if (source == null || c == null) return;
            float v = volume * Mathf.Clamp01(Settings.SfxVolume);
            if (v <= 0.001f) return;
            source.PlayOneShot(c, v);
        }

        public static void Ping() => Play(ping, 0.7f);
        public static void Pop() => Play(pop, 0.5f);
        public static void Nope() => Play(nope, 0.6f);

        public static void Click()
        {
            float now = Time.unscaledTime;
            if (now - lastClick < 0.05f) return;
            lastClick = now;
            Play(click, 0.35f);
        }
    }
}
