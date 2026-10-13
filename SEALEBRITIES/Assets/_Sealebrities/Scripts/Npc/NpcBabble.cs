using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Babble voices (Q60, A.1, B.1): every NPC / radio line is spoken as cartoon gibberish – short procedural syllables
    /// (glottal pulse train through two vowel formants) at the speaker's pitch and rate, +30 % pitch on "!" lines, a
    /// short "hm-hm" before questions. Radio speakers (Gus, radio calls) go through a 300–3,000 Hz band-pass with crackle
    /// and end with a click. Listens to GameEvents.NpcLine / RadioCall, so lines raised by any module get a voice.
    /// Syllable banks are rendered once per voice on first use (≈ 30 ms, never per frame); playback allocates nothing.
    public sealed class NpcBabble : MonoBehaviour
    {
        const int SR = 22050;
        const int Syllables = 12;

        sealed class Bank
        {
            public AudioClip[] syl = new AudioClip[Syllables];
            public AudioClip hm, click;
        }

        sealed class Voice
        {
            public string speaker;
            public NpcDef def;
            public Bank bank;
            public float timer, left, pitchMul;
            public int remaining, hmLeft;
            public bool radio;
            public AudioSource src;
            public NpcActor actor;
            public System.Random rng;
        }

        readonly Dictionary<string, Bank> banks = new Dictionary<string, Bank>();
        readonly List<Voice> voices = new List<Voice>(4);
        readonly List<Voice> pool = new List<Voice>(4);
        AudioSource radioSrc;
        readonly AudioSource[] worldSrc = new AudioSource[3];
        string radioSpeaker;
        float radioUntil;

        public static NpcBabble I { get; private set; }

        public static NpcBabble Create(Transform parent)
        {
            var go = new GameObject("SB_NpcBabble");
            go.transform.SetParent(parent, false);
            var b = go.AddComponent<NpcBabble>();
            return b;
        }

        void Awake()
        {
            I = this;
            radioSrc = MakeSource("Radio", false);
            for (int i = 0; i < worldSrc.Length; i++) worldSrc[i] = MakeSource("World" + i, true);
            for (int i = 0; i < 4; i++) pool.Add(new Voice { rng = new System.Random(1234 + i) });
            GameEvents.NpcLine += OnLine;
            GameEvents.RadioCall += OnRadio;
        }

        void OnDestroy()
        {
            GameEvents.NpcLine -= OnLine;
            GameEvents.RadioCall -= OnRadio;
            if (I == this) I = null;
        }

        AudioSource MakeSource(string name, bool spatial)
        {
            var go = new GameObject("Babble" + name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = false;
            s.spatialBlend = spatial ? 1f : 0f;
            s.rolloffMode = AudioRolloffMode.Linear;
            s.minDistance = 3f;
            s.maxDistance = 30f;
            s.dopplerLevel = 0f;
            s.priority = 64;
            return s;
        }

        void OnRadio(string speaker, bool started)
        {
            if (started) { radioSpeaker = speaker; radioUntil = Time.unscaledTime + 60f; }
            else if (radioSpeaker == speaker)
            {
                var b = BankFor(NpcDatabase.Get(speaker), true);
                if (b != null && b.click != null) radioSrc.PlayOneShot(b.click, Volume * 0.8f);
                radioSpeaker = null;
            }
        }

        static float Volume => Mathf.Clamp01(Settings.SfxVolume) * 0.75f;

        void OnLine(string speaker, string text, float seconds)
        {
            var def = NpcDatabase.Get(speaker);
            if (def == null || string.IsNullOrEmpty(text)) return;
            // during a radio call every line comes out of the radio (Gus, Wanda's mishap calls, the brothers' SOS)
            bool radio = def.radio || (radioSpeaker != null && Time.unscaledTime < radioUntil);
            // one voice per speaker: a new line replaces the old one
            for (int i = voices.Count - 1; i >= 0; i--)
                if (voices[i].speaker == speaker) { pool.Add(voices[i]); voices.RemoveAt(i); }
            if (pool.Count == 0) { pool.Add(voices[0]); voices.RemoveAt(0); }
            var v = pool[pool.Count - 1];
            pool.RemoveAt(pool.Count - 1);
            v.speaker = speaker;
            v.def = def;
            v.radio = radio;
            v.bank = BankFor(def, radio);
            v.actor = radio ? null : NpcRegistry.Find(speaker) as NpcActor;
            if (!radio && v.actor == null) radio = v.radio = true;   // speaker without a body (e.g. a voice from the boat radio)
            if (v.bank == null) { pool.Add(v); return; }
            int syl = CountSyllables(text);
            float maxSyl = Mathf.Max(2f, seconds * 0.85f * def.rate);
            v.remaining = Mathf.Clamp(syl, 2, (int)maxSyl);
            v.timer = 0.05f;
            v.left = seconds;
            v.pitchMul = text.IndexOf('!') >= 0 ? 1.3f : 1f;
            v.hmLeft = text.IndexOf('?') >= 0 ? 2 : 0;
            v.src = radio ? radioSrc : FreeWorldSource();
            if (v.actor != null) v.actor.BeginTalking(seconds);
            voices.Add(v);
        }

        AudioSource FreeWorldSource()
        {
            for (int i = 0; i < worldSrc.Length; i++)
            {
                bool used = false;
                for (int k = 0; k < voices.Count; k++) if (voices[k].src == worldSrc[i]) { used = true; break; }
                if (!used) return worldSrc[i];
            }
            return worldSrc[0];
        }

        static int CountSyllables(string text)
        {
            int n = 0;
            bool prevVowel = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = char.ToLowerInvariant(text[i]);
                bool vowel = c == 'a' || c == 'e' || c == 'i' || c == 'o' || c == 'u' || c == 'y' || c == 'ä' || c == 'ö' || c == 'ü';
                if (vowel && !prevVowel) n++;
                prevVowel = vowel;
            }
            return n;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            float vol = Volume;
            for (int i = voices.Count - 1; i >= 0; i--)
            {
                var v = voices[i];
                v.left -= dt;
                if (v.src != null && !v.radio && v.actor != null)
                    v.src.transform.position = v.actor.transform.position + Vector3.up * (v.def.height * 0.85f);
                v.timer -= dt;
                if (v.timer <= 0f)
                {
                    if (v.hmLeft > 0)
                    {
                        v.hmLeft--;
                        Emit(v, v.bank.hm, vol * 0.7f, 0.3f);
                        v.timer = 0.17f;
                    }
                    else if (v.remaining > 0)
                    {
                        v.remaining--;
                        var clip = v.bank.syl[v.rng.Next(Syllables)];
                        Emit(v, clip, vol, 0.65f + (float)v.rng.NextDouble() * 0.35f);
                        float gap = 1f / Mathf.Max(3f, v.def.rate);
                        // small pauses like word breaks
                        if (v.rng.NextDouble() < 0.18) gap += 0.08f;
                        v.timer = gap * (0.85f + (float)v.rng.NextDouble() * 0.3f);
                    }
                    else v.timer = 1f;
                }
                if ((v.remaining <= 0 && v.hmLeft <= 0 && v.timer > 0.5f) || v.left <= -0.5f)
                {
                    voices.RemoveAt(i);
                    pool.Add(v);
                }
            }
        }

        void Emit(Voice v, AudioClip clip, float vol, float open)
        {
            if (clip == null || v.src == null) return;
            v.src.pitch = v.pitchMul * (0.94f + (float)v.rng.NextDouble() * 0.12f);
            v.src.PlayOneShot(clip, vol);
            if (v.actor != null) v.actor.TalkPulse(open);
        }

        // ---------------------------------------------------------------- synthesis
        Bank BankFor(NpcDef def, bool radio)
        {
            if (def == null) return null;
            string key = def.id + (radio ? "#r" : "");
            if (banks.TryGetValue(key, out var b)) return b;
            b = Render(def, radio);
            banks[key] = b;
            return b;
        }

        static readonly float[,] Formants =
        {
            { 800f, 1200f }, // a
            { 450f, 800f },  // o
            { 330f, 750f },  // u
            { 420f, 2100f }, // e
            { 300f, 2600f }, // i
            { 600f, 1700f }, // ä
        };

        static int[] VowelSet(int set)
        {
            switch (set)
            {
                case 0: return new[] { 1, 2, 0, 1, 2, 0 };          // bubbly o, u, a
                case 1: return new[] { 0, 3, 4, 3, 0, 5 };          // bright a, e, i
                case 3: return new[] { 2, 1, 2, 1, 0, 2 };          // soft, hum-heavy
                default: return new[] { 0, 1, 3, 2, 4, 5 };         // mixed
            }
        }

        static Bank Render(NpcDef def, bool radio)
        {
            var b = new Bank();
            var rng = new System.Random(NpcUtil.StableHash(def.id) & 0x7fffffff);
            int[] vs = VowelSet(def.vowels);
            float formantShift = Mathf.Clamp(def.pitch / 150f, 0.85f, 1.35f);
            for (int i = 0; i < Syllables; i++)
            {
                int vowel = vs[i % vs.Length];
                float len = 0.07f + (float)rng.NextDouble() * 0.06f;
                bool fric = rng.NextDouble() < 0.35;
                float f0 = def.pitch * (0.9f + (float)rng.NextDouble() * 0.24f);
                b.syl[i] = MakeClip(def.id + "_s" + i, Synth(f0, Formants[vowel, 0] * formantShift, Formants[vowel, 1] * formantShift, len, fric, radio, rng, false));
            }
            b.hm = MakeClip(def.id + "_hm", Synth(def.pitch * 0.92f, 260f, 1900f, 0.12f, false, radio, rng, true));
            if (radio) b.click = MakeClip(def.id + "_click", Click(rng));
            return b;
        }

        static AudioClip MakeClip(string name, float[] data)
        {
            var c = AudioClip.Create("SB_Babble_" + name, data.Length, 1, SR, false);
            c.SetData(data, 0);
            return c;
        }

        struct Biquad
        {
            float b0, b1, b2, a1, a2, z1, z2;

            public static Biquad BandPass(float fc, float q)
            {
                float w = 2f * Mathf.PI * fc / SR, c = Mathf.Cos(w), al = Mathf.Sin(w) / (2f * q), a0 = 1f + al;
                return new Biquad { b0 = al / a0, b1 = 0f, b2 = -al / a0, a1 = -2f * c / a0, a2 = (1f - al) / a0 };
            }

            public static Biquad LowPass(float fc, float q)
            {
                float w = 2f * Mathf.PI * fc / SR, c = Mathf.Cos(w), al = Mathf.Sin(w) / (2f * q), a0 = 1f + al;
                return new Biquad { b0 = (1f - c) * 0.5f / a0, b1 = (1f - c) / a0, b2 = (1f - c) * 0.5f / a0, a1 = -2f * c / a0, a2 = (1f - al) / a0 };
            }

            public static Biquad HighPass(float fc, float q)
            {
                float w = 2f * Mathf.PI * fc / SR, c = Mathf.Cos(w), al = Mathf.Sin(w) / (2f * q), a0 = 1f + al;
                return new Biquad { b0 = (1f + c) * 0.5f / a0, b1 = -(1f + c) / a0, b2 = (1f + c) * 0.5f / a0, a1 = -2f * c / a0, a2 = (1f - al) / a0 };
            }

            public float Process(float x)
            {
                float y = b0 * x + z1;
                z1 = b1 * x - a1 * y + z2;
                z2 = b2 * x - a2 * y;
                return y;
            }
        }

        static float[] Synth(float f0, float f1, float f2, float seconds, bool fricative, bool radio, System.Random rng, bool hum)
        {
            int n = Mathf.Max(64, (int)(seconds * SR));
            var d = new float[n];
            var bp1 = Biquad.BandPass(f1, 5f);
            var bp2 = Biquad.BandPass(f2, 7f);
            var lp = Biquad.LowPass(hum ? 900f : 4200f, 0.7f);
            var nb = Biquad.BandPass(3800f, 1.4f);
            var rHp = Biquad.HighPass(300f, 0.7f);
            var rLp = Biquad.LowPass(3000f, 0.7f);
            float phase = 0f;
            int fricLen = fricative ? (int)(0.02f * SR) : 0;
            float peak = 0.0001f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SR;
                float u = i / (float)n;
                // pitch glides slightly (cartoon intonation) + vibrato
                float f = f0 * (1f + 0.06f * (0.5f - u) + 0.015f * Mathf.Sin(t * 2f * Mathf.PI * 6f));
                phase += f / SR;
                if (phase >= 1f) phase -= 1f;
                float saw = 2f * phase - 1f;
                float pulse = saw - saw * saw * saw * 0.33f;           // softened sawtooth (glottal-ish)
                float voiced = bp1.Process(pulse) * 1.0f + bp2.Process(pulse) * (hum ? 0.15f : 0.55f);
                voiced = lp.Process(voiced + pulse * 0.08f);
                float x = voiced;
                if (i < fricLen)
                {
                    float noise = (float)rng.NextDouble() * 2f - 1f;
                    x = Mathf.Lerp(nb.Process(noise) * 0.6f, x, i / (float)fricLen);
                }
                // envelope: 8 ms attack, 35 ms release
                float att = Mathf.Clamp01(t / 0.008f), rel = Mathf.Clamp01((n - i) / (0.035f * SR));
                x *= att * rel * (hum ? 0.6f : 1f);
                if (radio)
                {
                    x = rLp.Process(rHp.Process(x));
                    if (rng.NextDouble() < 0.004) x += ((float)rng.NextDouble() * 2f - 1f) * 0.3f;   // crackle
                    x = Mathf.Clamp(x * 1.6f, -0.9f, 0.9f);
                }
                d[i] = x;
                float a = Mathf.Abs(x);
                if (a > peak) peak = a;
            }
            float norm = 0.55f / peak;
            for (int i = 0; i < n; i++) d[i] *= norm;
            return d;
        }

        static float[] Click(System.Random rng)
        {
            int n = (int)(0.06f * SR);
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float env = Mathf.Exp(-i / (0.004f * SR));
                d[i] = ((float)rng.NextDouble() * 2f - 1f) * env * 0.6f + (i < 40 ? 0.5f : 0f) * env;
            }
            return d;
        }
    }
}
