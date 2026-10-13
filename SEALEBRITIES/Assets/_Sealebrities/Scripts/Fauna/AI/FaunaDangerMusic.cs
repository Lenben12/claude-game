using UnityEngine;

namespace Sealebrities
{
    /// Round 3 (GDD §20 Q16 "angekündigt durch Musik"): the danger cue of a shark / monster encounter – a low,
    /// plucked two-note ostinato (E–F, getting faster within its 4 s loop) with a soft drum hit, synthesized once at
    /// runtime (no assets). Plays on the machine of the hunted diver (FaunaThreat.Cue) on top of the relaxed music,
    /// fades in over 1 s, rises in pitch during the charge, fades out over 2.5 s. Volume = music × master settings.
    public sealed class FaunaDangerMusic : MonoBehaviour
    {
        const int Rate = 22050;
        const float LoopSeconds = 4f;
        static AudioClip clip;

        AudioSource src;
        AudioLowPassFilter lpf;
        float target, level, hitPitch;
        bool playing;

        /// True while the cue is audible (tests).
        public bool Playing => playing && level > 0.01f;
        public static bool AnyPlaying { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { clip = null; AnyPlaying = false; }

        void Ensure()
        {
            if (src != null) return;
            var go = new GameObject("FaunaDangerMusic");
            go.transform.SetParent(transform, false);
            src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = true;
            src.spatialBlend = 0f;
            src.dopplerLevel = 0f;
            src.priority = 24;
            src.volume = 0f;
            lpf = go.AddComponent<AudioLowPassFilter>();
            lpf.cutoffFrequency = 22000f;
            if (clip == null) clip = Synth();
            src.clip = clip;
        }

        public void Play()
        {
            Ensure();
            target = 1f;
            if (!playing)
            {
                playing = true;
                if (!src.isPlaying) { src.time = 0f; src.Play(); }
            }
        }

        public void Stop(bool instant)
        {
            target = 0f;
            if (instant && src != null) { level = 0f; src.volume = 0f; src.Stop(); playing = false; }
        }

        /// A bite / hit: a short pitch jump.
        public void Hit() { hitPitch = 0.25f; }

        void Update()
        {
            if (src == null) return;
            float dt = Time.unscaledDeltaTime;
            level = Mathf.MoveTowards(level, target, dt * (target > level ? 1f : 0.4f));
            hitPitch = Mathf.MoveTowards(hitPitch, 0f, dt * 0.5f);
            var d = Settings.Data;
            float vol = d != null ? Mathf.Clamp01(d.music) * Mathf.Clamp01(d.master) : 0.7f;
            src.volume = level * Mathf.Max(0.25f, vol) * 0.9f;
            bool charge = FaunaThreat.Phase == 3;
            src.pitch = Mathf.MoveTowards(src.pitch, 1f + (charge ? 0.22f : 0f) + hitPitch, dt * 0.6f);
            var p = Refs.Player;
            bool under = p != null && !(p is Object o && o == null) && p.IsUnderwater;
            lpf.cutoffFrequency = Mathf.MoveTowards(lpf.cutoffFrequency, under ? 2600f : 22000f, dt * 20000f);
            if (playing && level <= 0.001f && target <= 0f)
            {
                playing = false;
                src.Stop();
            }
            AnyPlaying = playing;
        }

        /// The cue: plucked low strings (sum of decaying harmonics) + a soft drum, normalized to 0.8.
        static AudioClip Synth()
        {
            int n = Mathf.RoundToInt(Rate * LoopSeconds);
            var data = new float[n];
            // (time, note: 0 = E2, 1 = F2) – the gaps shrink: the classic "it's getting closer" feel
            float[] times = { 0f, 0.42f, 1.4f, 1.78f, 2.55f, 2.83f, 3.18f, 3.38f, 3.58f, 3.76f };
            const float E2 = 82.41f, F2 = 87.31f;
            for (int k = 0; k < times.Length; k++)
            {
                float f = (k % 2 == 0) ? E2 : F2;
                int start = Mathf.RoundToInt(times[k] * Rate);
                int len = Mathf.Min(n - start, Mathf.RoundToInt(0.7f * Rate));
                float amp = 0.55f + 0.06f * k;
                for (int i = 0; i < len; i++)
                {
                    float t = i / (float)Rate;
                    float env = Mathf.Min(1f, t / 0.008f) * Mathf.Exp(-t / 0.16f);
                    float v = 0f;
                    for (int h = 1; h <= 6; h++) v += Mathf.Sin(2f * Mathf.PI * f * h * t) / Mathf.Pow(h, 1.15f);
                    data[start + i] += v * env * amp;
                }
            }
            // drum: a falling 70 → 40 Hz thump at the loop start and before the fast run
            float[] drums = { 0f, 2.55f };
            for (int k = 0; k < drums.Length; k++)
            {
                int start = Mathf.RoundToInt(drums[k] * Rate);
                int len = Mathf.Min(n - start, Mathf.RoundToInt(0.5f * Rate));
                float ph = 0f;
                for (int i = 0; i < len; i++)
                {
                    float t = i / (float)Rate;
                    float fr = Mathf.Lerp(70f, 40f, Mathf.Clamp01(t / 0.25f));
                    ph += 2f * Mathf.PI * fr / Rate;
                    data[start + i] += Mathf.Sin(ph) * Mathf.Exp(-t / 0.12f) * 0.9f;
                }
            }
            float peak = 0.001f;
            for (int i = 0; i < n; i++) peak = Mathf.Max(peak, Mathf.Abs(data[i]));
            float g = 0.8f / peak;
            for (int i = 0; i < n; i++) data[i] *= g;
            // soften the loop seam
            int fade = Rate / 50;
            for (int i = 0; i < fade; i++) data[n - 1 - i] *= i / (float)fade;
            var c = AudioClip.Create("SB_DangerCue", n, 1, Rate, false);
            c.SetData(data, 0);
            return c;
        }
    }
}
