using System;

namespace Sealebrities
{
    /// The relaxed background tracks of K.6 (all 32 bars in 4/4, rendered once per use on a worker thread):
    ///   Menu „Strandhütte“      104 BPM  C – Am – F – G7          ukulele strum, marimba melody (2 variations), round bass, shaker
    ///   Day „Inseltag“           92 BPM  F – Dm – B♭ – C          fingerpicked ukulele, soft marimba, upright bass, shaker, birds
    ///   Evening „Goldene Stunde“ 80 BPM  Dmaj7 – Bm7 – Gmaj7 – A6 slow ukulele strum, warm pad, vibraphone
    ///   Night „Mondlagune“       66 BPM  Am9 – Fmaj7 – Cmaj7 – G6 soft pad, kalimba / music-box notes, low bass hum
    ///   Water „Blaues Schweben“  72 BPM  E♭maj7 – Fadd9 (lydian)  airy pad with a slow filter sweep, low-passed marimba, bells
    ///   Deep „Tiefenstille“      54 BPM  drone on D               low pad, distant chimes, no rhythm
    ///   Oldreef „Altriff-Lied“  104 BPM  menu chords              marimba + whale motif (Juno's quest J1 unlocks it)
    /// 22,050 Hz mono, returned as 16-bit samples. Pure C#, no Unity API: safe on a worker thread. Deterministic (fixed
    /// seeds per track). Chill only: no drums except a soft shaker, nothing reacts to danger.
    static class FXMusicSynth
    {
        public const int SR = 22050;
        public const int Bars = 32;
        const int SineN = 4096, SineMask = SineN - 1;
        static readonly float[] SineT = BuildSine();

        static float[] BuildSine()
        {
            var t = new float[SineN];
            for (int i = 0; i < SineN; i++) t[i] = (float)Math.Sin(2.0 * Math.PI * i / SineN);
            return t;
        }

        /// sin(2π · phase) for a phase in cycles (any sign / size).
        static float Sn(float phase)
        {
            int i = (int)(phase * SineN);
            return SineT[i & SineMask];
        }

        static int N(float seconds) => (int)(seconds * SR);
        static float Hz(int midi) => 440f * MathF.Pow(2f, (midi - 69) / 12f);

        public static float BpmOf(FXMusicTrack t)
        {
            switch (t)
            {
                case FXMusicTrack.Day: return 92f;
                case FXMusicTrack.Evening: return 80f;
                case FXMusicTrack.Night: return 66f;
                case FXMusicTrack.Water: return 72f;
                case FXMusicTrack.Deep: return 54f;
                default: return 104f;   // Menu, Oldreef
            }
        }

        /// Length of one loop in seconds.
        public static float LoopSeconds(FXMusicTrack t) => Bars * 4f * 60f / BpmOf(t);

        /// Renders one seamless loop of the track (16-bit, SR Hz, mono).
        public static short[] Render(FXMusicTrack t)
        {
            float beat = 60f / BpmOf(t);
            int n = N(Bars * 4f * beat);
            int tail = N(5f);
            var mix = new float[n + tail];
            float wet;
            switch (t)
            {
                case FXMusicTrack.Day: Day(mix, beat); wet = 0.2f; break;
                case FXMusicTrack.Evening: Evening(mix, beat); wet = 0.32f; break;
                case FXMusicTrack.Night: Night(mix, beat); wet = 0.4f; break;
                case FXMusicTrack.Water: Water(mix, beat); wet = 0.5f; break;
                case FXMusicTrack.Deep: Deep(mix, beat); wet = 0.6f; break;
                case FXMusicTrack.Oldreef: Menu(mix, beat, true); wet = 0.24f; break;
                default: Menu(mix, beat, false); wet = 0.18f; break;
            }
            Reverb(mix, wet, t == FXMusicTrack.Deep || t == FXMusicTrack.Water ? 0.86f : 0.78f);

            // what rings over the loop end is heard at the start of the repeat (seamless loop)
            var o = new float[n];
            Array.Copy(mix, o, n);
            for (int i = 0; i < tail && i < n; i++) o[i] += mix[n + i];
            // glide the last 6 ms onto the first sample: no click at the loop seam
            int g = Math.Min(128, n / 4);
            float delta = o[0] - o[n - 1];
            for (int j = 0; j < g; j++) o[n - g + j] += delta * (j + 1) / g;
            return Master(o, t == FXMusicTrack.Deep ? 0.12f : 0.15f);
        }

        /// RMS-normalise, soft-limit the peaks and convert to 16 bit.
        static short[] Master(float[] a, float rmsTarget)
        {
            double sum = 0;
            for (int i = 0; i < a.Length; i++) sum += a[i] * a[i];
            float rms = (float)Math.Sqrt(sum / Math.Max(1, a.Length));
            float k = rms > 1e-9f ? rmsTarget / rms : 1f;
            var o = new short[a.Length];
            for (int i = 0; i < a.Length; i++)
            {
                float x = a[i] * k;
                x = x / (1f + 0.45f * MathF.Abs(x));              // gentle soft clip
                if (x > 0.98f) x = 0.98f; else if (x < -0.98f) x = -0.98f;
                o[i] = (short)(x * 32767f);
            }
            return o;
        }

        // ------------------------------------------------------------------ instruments
        /// Karplus-Strong nylon string (ukulele / upright bass), rendered from start for len samples.
        static void Pluck(float[] buf, int start, float freq, float amp, float bright, int len, float t60, uint seed)
        {
            if (start < 0 || start >= buf.Length) return;
            int period = Math.Max(2, (int)MathF.Round(SR / freq - 0.5f));
            var line = new float[period];
            var rng = new FXRng(seed * 2654435761u + 7u);
            float lp = 0f, mean = 0f;
            float a = bright < 0.05f ? 0.05f : bright > 0.95f ? 0.95f : bright;
            for (int i = 0; i < period; i++) { lp += a * (rng.Signed() - lp); line[i] = lp; mean += lp; }
            mean /= period;
            for (int i = 0; i < period; i++) line[i] -= mean;
            float g = MathF.Pow(10f, -3f / (t60 * freq));
            len = Math.Min(len, buf.Length - start);
            int fade = Math.Min(len, N(0.02f));
            int idx = 0;
            for (int i = 0; i < len; i++)
            {
                float cur = line[idx];
                int nx = idx + 1 == period ? 0 : idx + 1;
                line[idx] = g * 0.5f * (cur + line[nx]);
                idx = nx;
                float env = i < len - fade ? 1f : (len - i) / (float)fade;
                float att = i < 20 ? i / 20f : 1f;
                buf[start + i] += cur * amp * env * att;
            }
        }

        /// Marimba bar: fundamental + the 3.93× and 9.9× partials with fast decays.
        static void Marimba(float[] a, int start, float f, float amp, float decay = 0.32f)
        {
            if (start < 0) return;
            int len = Math.Min(a.Length - start, N(decay * 3.2f));
            float p1 = 0f, p2 = 0f, p3 = 0f, i1 = f / SR, i2 = 3.93f * f / SR, i3 = 9.9f * f / SR;
            float k1 = MathF.Exp(-1f / (decay * SR)), k2 = MathF.Exp(-1f / (0.045f * SR)), k3 = MathF.Exp(-1f / (0.012f * SR));
            float e1 = 1f, e2 = 0.35f, e3 = 0.12f;
            if (f * 9.9f > SR * 0.45f) e3 = 0f;
            for (int i = 0; i < len; i++)
            {
                float att = i < 30 ? i / 30f : 1f;
                a[start + i] += amp * att * (e1 * Sn(p1) + e2 * Sn(p2) + e3 * Sn(p3));
                p1 += i1; p2 += i2; p3 += i3;
                if (p1 > 1f) p1 -= 1f; if (p2 > 1f) p2 -= 1f; if (p3 > 1f) p3 -= 1f;
                e1 *= k1; e2 *= k2; e3 *= k3;
            }
        }

        /// Round bass: sine + soft 2nd/3rd harmonic, plucked envelope.
        static void Bass(float[] a, int start, float f, float amp, int len, float decay = 0.55f)
        {
            if (start < 0) return;
            len = Math.Min(len, a.Length - start);
            int rel = Math.Max(1, N(0.04f));
            float p = 0f, inc = f / SR, k = MathF.Exp(-1f / (decay * SR)), e = 1f;
            for (int i = 0; i < len; i++)
            {
                float att = i < 60 ? i / 60f : 1f;
                float r = i > len - rel ? (len - i) / (float)rel : 1f;
                a[start + i] += amp * att * r * e * (Sn(p) + 0.25f * Sn(2f * p) + 0.06f * Sn(3f * p));
                p += inc; if (p > 1f) p -= 1f;
                e *= k;
            }
        }

        /// Bell / chime: slightly inharmonic partials.
        static void Bell(float[] a, int start, float f, float amp, float decay)
        {
            if (start < 0) return;
            int len = Math.Min(a.Length - start, N(decay * 5f));
            float p1 = 0f, p2 = 0f, p3 = 0f, i1 = f / SR, i2 = 2f * f / SR, i3 = 3.01f * f / SR;
            float k1 = MathF.Exp(-1f / (decay * SR)), k2 = MathF.Exp(-1f / (decay * 0.55f * SR)), k3 = MathF.Exp(-1f / (decay * 0.3f * SR));
            float e1 = 1f, e2 = 0.45f, e3 = 0.2f;
            if (f * 3.01f > SR * 0.45f) e3 = 0f;
            if (f * 2f > SR * 0.45f) e2 = 0f;
            for (int i = 0; i < len; i++)
            {
                float att = i < 40 ? i / 40f : 1f;
                a[start + i] += amp * att * (e1 * Sn(p1) + e2 * Sn(p2) + e3 * Sn(p3));
                p1 += i1; p2 += i2; p3 += i3;
                if (p1 > 1f) p1 -= 1f; if (p2 > 1f) p2 -= 1f; if (p3 > 1f) p3 -= 1f;
                e1 *= k1; e2 *= k2; e3 *= k3;
            }
        }

        /// Vibraphone: round tone with a 5.5 Hz tremolo and a long decay.
        static void Vibes(float[] a, int start, float f, float amp)
        {
            if (start < 0) return;
            int len = Math.Min(a.Length - start, N(3.2f));
            float p1 = 0f, p4 = 0f, pt = 0f, i1 = f / SR, i4 = 4f * f / SR, it = 5.5f / SR;
            float k1 = MathF.Exp(-1f / (1.25f * SR)), k4 = MathF.Exp(-1f / (0.12f * SR));
            float e1 = 1f, e4 = f * 4f > SR * 0.45f ? 0f : 0.22f;
            for (int i = 0; i < len; i++)
            {
                float att = i < 50 ? i / 50f : 1f;
                float trem = 0.82f + 0.18f * Sn(pt);
                a[start + i] += amp * att * trem * (e1 * Sn(p1) + e4 * Sn(p4));
                p1 += i1; p4 += i4; pt += it;
                if (p1 > 1f) p1 -= 1f; if (p4 > 1f) p4 -= 1f; if (pt > 1f) pt -= 1f;
                e1 *= k1; e4 *= k4;
            }
        }

        /// Kalimba tine / music-box note: pure tone with a short inharmonic "plink".
        static void Kalimba(float[] a, int start, float f, float amp, bool musicBox)
        {
            if (start < 0) return;
            int len = Math.Min(a.Length - start, N(musicBox ? 2.4f : 1.8f));
            float p1 = 0f, p2 = 0f, i1 = f / SR, i2 = (musicBox ? 3.0f : 5.4f) * f / SR;
            float k1 = MathF.Exp(-1f / ((musicBox ? 0.7f : 0.55f) * SR)), k2 = MathF.Exp(-1f / (0.03f * SR));
            float e1 = 1f, e2 = (musicBox ? 0.3f : 0.45f);
            if (i2 > 0.45f) e2 = 0f;
            for (int i = 0; i < len; i++)
            {
                float att = i < 25 ? i / 25f : 1f;
                a[start + i] += amp * att * (e1 * Sn(p1) + e2 * Sn(p2));
                p1 += i1; p2 += i2;
                if (p1 > 1f) p1 -= 1f; if (p2 > 1f) p2 -= 1f;
                e1 *= k1; e2 *= k2;
            }
        }

        static void Shaker(float[] a, int start, float amp, ref FXRng rng)
        {
            if (start < 0) return;
            int len = Math.Min(a.Length - start, N(0.07f));
            float hp = 0f, last = 0f;
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)SR;
                float env = (1f - MathF.Exp(-t / 0.004f)) * MathF.Exp(-t / 0.022f);
                float w = rng.Signed();
                hp = 0.55f * (hp + w - last);   // crude high-pass: the shaker is only "tss"
                last = w;
                a[start + i] += amp * env * hp;
            }
        }

        /// Swept bird chirp (day track).
        static void Chirp(float[] a, int start, float dur, float fA, float fB, float amp)
        {
            if (start < 0) return;
            int len = Math.Min(a.Length - start, N(dur));
            float p = 0f;
            for (int i = 0; i < len; i++)
            {
                float x = i / (float)len;
                p += (fA + (fB - fA) * x) / SR;
                if (p > 1f) p -= 1f;
                float env = Sn(0.5f * x);
                a[start + i] += amp * env * env * Sn(p);
            }
        }

        /// Pad chord: per note 3 slightly detuned voices (sine + soft 2nd/3rd), slow attack and release, written into
        /// a layer that the caller low-passes. len = sustain length; the release rings on after it.
        static void PadChord(float[] layer, int start, int len, int[] midis, float amp, float attack, float release, float bright)
        {
            if (start < 0) return;
            int att = Math.Max(1, N(attack)), rel = Math.Max(1, N(release));
            int total = Math.Min(layer.Length - start, len + rel);
            float h2 = 0.35f * bright, h3 = 0.15f * bright;
            for (int nIdx = 0; nIdx < midis.Length; nIdx++)
            {
                float f = Hz(midis[nIdx]);
                float a1 = 0f, a2 = 0.33f, a3 = 0.67f;
                float d1 = f / SR, d2 = f * 1.0045f / SR, d3 = f * 0.9957f / SR;
                float noteAmp = amp / midis.Length;
                for (int i = 0; i < total; i++)
                {
                    float env = i < att ? i / (float)att : 1f;
                    if (i > len) env *= 1f - (i - len) / (float)rel;
                    env = env * env * (3f - 2f * env);
                    float v = Sn(a1) + Sn(a2) + Sn(a3) + h2 * (Sn(2f * a1) + Sn(2f * a2)) + h3 * Sn(3f * a3);
                    layer[start + i] += noteAmp * env * v * 0.33f;
                    a1 += d1; a2 += d2; a3 += d3;
                    if (a1 > 1f) a1 -= 1f; if (a2 > 1f) a2 -= 1f; if (a3 > 1f) a3 -= 1f;
                }
            }
        }

        /// One-pole low-pass over a layer with a cutoff that can sweep (cutoff(t) = c0 + (c1 − c0) · (0.5 − 0.5 cos(2π t / period))).
        static void SweepLowPass(float[] layer, float c0, float c1, float periodSec)
        {
            float y = 0f;
            int step = 64;
            float k = 0f;
            for (int i = 0; i < layer.Length; i++)
            {
                if (i % step == 0)
                {
                    float t = i / (float)SR;
                    float c = periodSec > 0f ? c0 + (c1 - c0) * (0.5f - 0.5f * MathF.Cos(6.2831853f * t / periodSec)) : c0;
                    k = 1f - MathF.Exp(-6.2831853f * c / SR);
                }
                y += k * (layer[i] - y);
                layer[i] = y;
            }
        }

        static void Add(float[] dst, float[] src, float gain)
        {
            for (int i = 0; i < dst.Length && i < src.Length; i++) dst[i] += src[i] * gain;
        }

        /// Ukulele voicing on the re-entrant G4 C4 E4 A4 strings: each string takes the nearest chord tone at or above
        /// its open note (max +6 semitones).
        static int[] UkeVoicing(int[] pitchClasses)
        {
            int[] open = { 67, 60, 64, 69 };
            var o = new int[4];
            for (int s = 0; s < 4; s++)
            {
                int best = open[s];
                for (int up = 0; up <= 6; up++)
                {
                    int m = open[s] + up;
                    bool hit = false;
                    for (int k = 0; k < pitchClasses.Length; k++) if ((m - pitchClasses[k]) % 12 == 0) hit = true;
                    if (hit) { best = m; break; }
                }
                o[s] = best;
            }
            return o;
        }

        // ------------------------------------------------------------------ menu „Strandhütte“ (and Oldreef)
        // the original 8-bar beach tune (8th notes, MIDI, 0 = rest)
        static readonly int[] MenuMelody =
        {
            76, 0, 79, 0, 81, 79, 76, 0,
            72, 0, 76, 0, 74, 72, 69, 0,
            69, 0, 72, 0, 74, 0, 76, 0,
            74, 0, 0, 79, 0, 0, 0, 0,
            79, 0, 81, 0, 84, 0, 81, 79,
            76, 0, 79, 0, 76, 74, 72, 0,
            74, 0, 76, 0, 72, 0, 69, 0,
            67, 0, 71, 0, 74, 0, 0, 0,
        };
        static readonly int[] CPenta = { 60, 62, 64, 67, 69, 72, 74, 76, 79, 81, 84, 86 };

        static void Menu(float[] mix, float beat, bool oldreef)
        {
            int[][] uke =
            {
                new[] { 67, 60, 64, 72 },   // C   0003
                new[] { 69, 60, 64, 69 },   // Am  2000
                new[] { 69, 60, 65, 69 },   // F   2010
                new[] { 67, 62, 65, 71 },   // G7  0212
            };
            int[] root = { 48, 45, 41, 43 }, fifth = { 43, 40, 48, 50 };
            float eighth = beat * 0.5f, swing = 0.11f * beat;
            int[] slots = { 0, 2, 3, 5, 6, 7 };
            bool[] down = { true, true, false, false, true, false };
            float[] vel = { 1f, 0.72f, 0.5f, 0.56f, 0.8f, 0.48f };
            var ukeL = new float[mix.Length];
            uint seed = oldreef ? 900u : 11u;
            var rng = new FXRng(oldreef ? 4040u : 303u);
            for (int bar = 0; bar < Bars; bar++)
            {
                int c = bar & 3;
                bool bSection = bar >= 16 && bar < 24;
                float barT = bar * 4f * beat;
                float ukeGain = (oldreef ? 0.6f : 1f) * (bSection ? 0.7f : 1f);
                for (int s = 0; s < slots.Length; s++)
                {
                    if (bSection && (slots[s] & 1) == 1) continue;   // the bridge strums lighter
                    float t0 = barT + slots[s] * eighth + ((slots[s] & 1) == 1 ? swing : 0f);
                    float next = s + 1 < slots.Length ? barT + slots[s + 1] * eighth : barT + 4f * beat;
                    for (int k = 0; k < 4; k++)
                    {
                        int str = down[s] ? k : 3 - k;
                        float ts = t0 + k * (down[s] ? 0.011f : 0.008f);
                        float amp = vel[s] * (down[s] ? 0.32f : 0.24f) * ukeGain;
                        Pluck(ukeL, N(ts), Hz(uke[c][str]), amp, 0.32f + 0.45f * vel[s], N(Math.Min(1.6f, next - ts + 0.05f)), 1.25f, seed++);
                    }
                }
                // bass: root on 1, fifth on 3 (a little walk-up into every 4th bar)
                Bass(mix, N(barT), Hz(root[c]), 0.5f, N(beat * 1.9f));
                Bass(mix, N(barT + 2f * beat), Hz(fifth[c]), 0.42f, N(beat * (c == 3 ? 0.9f : 1.9f)));
                if (c == 3) Bass(mix, N(barT + 3f * beat), Hz(fifth[c] + 2), 0.36f, N(beat * 0.9f));
                // shaker on the 8ths (accents off-beat), resting in the bridge's first half
                if (!(bSection && bar < 20))
                    for (int i8 = 0; i8 < 8; i8++)
                        Shaker(mix, N(barT + i8 * eighth + ((i8 & 1) == 1 ? swing : 0f)), (i8 & 1) == 1 ? 0.1f : 0.06f, ref rng);
            }
            Add(mix, ukeL, 1f);

            // melody: A (original) – A' (variation 1) – bridge (arpeggios + a high answer) – A'' (variation 2)
            var mrng = new FXRng(oldreef ? 77u : 1234u);
            for (int section = 0; section < 4; section++)
            {
                float secT = section * 8 * 4f * beat;
                if (section == 2)
                {
                    for (int bar = 0; bar < 8; bar++)
                    {
                        int c = bar & 3;
                        float barT = secT + bar * 4f * beat;
                        int[] chord = uke[c];
                        for (int q = 0; q < 4; q++)
                            Marimba(mix, N(barT + q * beat + (q == 3 ? swing : 0f)), Hz(chord[(q + bar) & 3] + 12), 0.1f);
                        if ((bar & 1) == 1) Marimba(mix, N(barT + 2.5f * beat + swing), Hz(CPenta[8 + mrng.Int(4)]), 0.12f);
                    }
                    continue;
                }
                for (int i8 = 0; i8 < MenuMelody.Length; i8++)
                {
                    int m = MenuMelody[i8];
                    if (m <= 0) continue;
                    if (section == 1 && (i8 & 3) == 2) m = Neighbour(m, mrng.Next01() < 0.5f ? 1 : -1);
                    if (section == 3 && (i8 / 16) % 2 == 1) m = Neighbour(m, 1);
                    if (section == 3 && (i8 & 7) == 6 && mrng.Next01() < 0.5f) continue;
                    float t = secT + i8 * eighth + ((i8 & 1) == 1 ? swing : 0f);
                    float amp = oldreef ? 0.26f : 0.2f;
                    Marimba(mix, N(t), Hz(m), amp);
                    if (oldreef) Marimba(mix, N(t), Hz(m - 12), 0.1f);   // octave doubling, rounder
                }
            }

            // Oldreef: a slow whale motif drifts over bars 9, 17 and 25 (soft gliding sine)
            if (oldreef)
                for (int k = 0; k < 3; k++)
                    WhaleMotif(mix, N((8 + 8 * k) * 4f * beat + beat), 8f * beat, 0.16f);
        }

        /// Step to the neighbouring note of the C pentatonic scale (dir +1 / −1).
        static int Neighbour(int m, int dir)
        {
            int best = 0;
            for (int i = 0; i < CPenta.Length; i++) if (CPenta[i] == m) best = i;
            int j = Math.Max(0, Math.Min(CPenta.Length - 1, best + dir));
            return CPenta[j];
        }

        static void WhaleMotif(float[] a, int start, float dur, float amp)
        {
            int len = Math.Min(a.Length - start, N(dur));
            if (start < 0 || len <= 0) return;
            float p = 0f;
            for (int i = 0; i < len; i++)
            {
                float x = i / (float)len;
                // oooo – ooo – oo: three gliding calls
                float f = x < 0.4f ? 330f + 160f * Smooth(x / 0.4f)
                        : x < 0.7f ? 490f - 98f * Smooth((x - 0.4f) / 0.3f)
                        : 392f - 62f * Smooth((x - 0.7f) / 0.3f);
                f *= 1f + 0.008f * Sn(4.5f * i / SR);
                p += f / SR;
                if (p > 1f) p -= 1f;
                float seg = x < 0.4f ? x / 0.4f : x < 0.7f ? (x - 0.4f) / 0.3f : (x - 0.7f) / 0.3f;
                float env = Sn(0.5f * seg);
                a[start + i] += amp * env * (Sn(p) + 0.2f * Sn(2f * p));
            }
        }

        static float Smooth(float x) { x = x < 0f ? 0f : x > 1f ? 1f : x; return x * x * (3f - 2f * x); }

        // ------------------------------------------------------------------ day „Inseltag“
        static void Day(float[] mix, float beat)
        {
            int[][] pcs = { new[] { 5, 9, 0 }, new[] { 2, 5, 9 }, new[] { 10, 2, 5 }, new[] { 0, 4, 7 } };   // F Dm Bb C
            int[] root = { 41, 38, 46, 48 }, fifth = { 48, 45, 41, 43 };
            int[] fPenta = { 65, 67, 69, 72, 74, 77, 79, 81, 84 };
            float eighth = beat * 0.5f, swing = 0.07f * beat;
            int[] pick = { 1, 2, 3, 0, 1, 2, 3, 2 };              // fingerpicking string order per 8th
            var ukeL = new float[mix.Length];
            var rng = new FXRng(9292);
            uint seed = 500;
            for (int bar = 0; bar < Bars; bar++)
            {
                int c = bar & 3;
                int[] v = UkeVoicing(pcs[c]);
                float barT = bar * 4f * beat;
                for (int i8 = 0; i8 < 8; i8++)
                {
                    float t = barT + i8 * eighth + ((i8 & 1) == 1 ? swing : 0f);
                    float vel = (i8 == 0 ? 1f : (i8 & 1) == 0 ? 0.75f : 0.55f) * (0.9f + 0.2f * rng.Next01());
                    Pluck(ukeL, N(t), Hz(v[pick[i8]]), 0.26f * vel, 0.4f + 0.3f * vel, N(eighth * 2.6f), 1.1f, seed++);
                }
                // upright bass: root on 1, fifth on 3, a walking pickup into the next chord every second bar
                Bass(mix, N(barT), Hz(root[c]), 0.5f, N(beat * 1.9f), 0.6f);
                Pluck(mix, N(barT), Hz(root[c]), 0.1f, 0.1f, N(beat * 1.2f), 1.2f, seed++);
                Bass(mix, N(barT + 2f * beat), Hz(fifth[c]), 0.42f, N(beat * ((bar & 1) == 1 ? 1.4f : 1.9f)), 0.6f);
                if ((bar & 1) == 1)
                {
                    int nextRoot = root[(c + 1) & 3];
                    Bass(mix, N(barT + 3.5f * beat), Hz(nextRoot + (nextRoot > fifth[c] ? -1 : 1)), 0.36f, N(beat * 0.5f), 0.4f);
                }
                // soft shaker
                for (int i8 = 0; i8 < 8; i8++)
                    Shaker(mix, N(barT + i8 * eighth + ((i8 & 1) == 1 ? swing : 0f)), (i8 & 1) == 1 ? 0.06f : 0.035f, ref rng);
                // a bird now and then
                if (bar % 3 == 1)
                {
                    float bt = barT + beat * (1f + rng.Next01() * 2f);
                    float f0 = rng.Range(2600f, 3800f);
                    Chirp(mix, N(bt), 0.09f, f0, f0 + rng.Range(-900f, 1100f), 0.05f);
                    Chirp(mix, N(bt + 0.14f), 0.08f, f0 + 300f, f0 - rng.Range(200f, 700f), 0.04f);
                }
            }
            Add(mix, ukeL, 1f);

            // soft marimba: a 4-bar phrase, varied each time, resting in bars 1–4 and 17–20
            var mrng = new FXRng(4141);
            int[] motif = { 2, -1, 3, 4, -1, 3, 2, -1, 1, 2, -1, -1, 0, -1, -1, -1 };   // quarter notes over 4 bars (−1 rest)
            for (int phrase = 0; phrase < Bars / 4; phrase++)
            {
                if (phrase == 0 || phrase == 4) continue;
                float pt = phrase * 16f * beat;
                int shift = phrase % 3 == 2 ? 2 : 0;
                for (int q = 0; q < motif.Length; q++)
                {
                    int idx = motif[q];
                    if (idx < 0) continue;
                    if (phrase >= 5 && mrng.Next01() < 0.25f) idx += 1;
                    int m = fPenta[Math.Min(fPenta.Length - 1, idx + shift + (phrase >= 5 ? 1 : 0))];
                    Marimba(mix, N(pt + q * beat + (q & 1) * swing * 0.5f), Hz(m), 0.13f, 0.28f);
                }
            }
        }

        // ------------------------------------------------------------------ evening „Goldene Stunde“
        static void Evening(float[] mix, float beat)
        {
            int[][] chords = { new[] { 50, 54, 57, 61 }, new[] { 47, 50, 54, 57 }, new[] { 43, 47, 50, 54 }, new[] { 45, 49, 52, 54 } };   // Dmaj7 Bm7 Gmaj7 A6
            int[] dPenta = { 74, 76, 78, 81, 83, 86, 88 };
            var pad = new float[mix.Length];
            var ukeL = new float[mix.Length];
            uint seed = 700;
            var rng = new FXRng(5151);
            for (int bar = 0; bar < Bars; bar++)
            {
                int c = bar & 3;
                float barT = bar * 4f * beat;
                var padNotes = new[] { chords[c][0] + 12, chords[c][1] + 12, chords[c][2] + 12, chords[c][3] + 12 };
                PadChord(pad, N(barT), N(4f * beat), padNotes, 0.5f, 1.1f, 1.4f, 0.6f);
                var pcsC = new int[4];
                for (int k = 0; k < 4; k++) pcsC[k] = chords[c][k] % 12;
                int[] v = UkeVoicing(pcsC);
                // slow, soft down-strums on 1 and 3 (a lazy arpeggio)
                for (int hit = 0; hit < 2; hit++)
                {
                    float t0 = barT + hit * 2f * beat;
                    for (int k = 0; k < 4; k++)
                        Pluck(ukeL, N(t0 + k * 0.035f), Hz(v[k]), 0.22f * (hit == 0 ? 1f : 0.75f), 0.3f, N(2f * beat + 0.2f), 1.4f, seed++);
                }
                Bass(mix, N(barT), Hz(chords[c][0] - 12), 0.32f, N(beat * 3.8f), 1.4f);
            }
            SweepLowPass(pad, 1100f, 1100f, 0f);
            Add(mix, pad, 1f);
            Add(mix, ukeL, 0.9f);

            // vibraphone: half and quarter notes, sparse, from bar 5 on
            for (int bar = 4; bar < Bars; bar++)
            {
                float barT = bar * 4f * beat;
                if (bar % 8 == 7) continue;   // breathe
                int idx = (bar * 3 + rng.Int(3)) % dPenta.Length;
                Vibes(mix, N(barT), Hz(dPenta[idx]), 0.14f);
                if (rng.Next01() < 0.6f) Vibes(mix, N(barT + 2f * beat), Hz(dPenta[(idx + (rng.Next01() < 0.5f ? 1 : 2)) % dPenta.Length]), 0.11f);
                if (rng.Next01() < 0.3f) Vibes(mix, N(barT + 3f * beat), Hz(dPenta[(idx + 4) % dPenta.Length]), 0.08f);
            }
        }

        // ------------------------------------------------------------------ night „Mondlagune“
        static void Night(float[] mix, float beat)
        {
            int[][] chords = { new[] { 57, 60, 64, 67, 71 }, new[] { 53, 57, 60, 64 }, new[] { 48, 52, 55, 59 }, new[] { 55, 59, 62, 64 } };   // Am9 Fmaj7 Cmaj7 G6
            var pad = new float[mix.Length];
            var rng = new FXRng(6363);
            float eighth = beat * 0.5f;
            for (int bar = 0; bar < Bars; bar++)
            {
                int c = bar & 3;
                float barT = bar * 4f * beat;
                PadChord(pad, N(barT), N(4f * beat), chords[c], 0.55f, 1.6f, 1.8f, 0.4f);
                // low bass hum: the root two octaves down with a slowly beating partner
                int rootM = chords[c][0] - 24;
                Hum(mix, N(barT), N(4f * beat), Hz(rootM), 0.22f);
                // kalimba (A sections) / music box (bridge) broken chords with gaps
                bool box = bar >= 16 && bar < 24;
                for (int i8 = 0; i8 < 8; i8++)
                {
                    if (rng.Next01() < (box ? 0.45f : 0.55f)) continue;
                    int[] ch = chords[c];
                    int m = ch[(i8 * 2 + bar) % ch.Length] + (box ? 24 : 12);
                    float t = barT + i8 * eighth + rng.Range(-0.01f, 0.01f);
                    Kalimba(mix, N(t), Hz(m), (box ? 0.08f : 0.11f) * (0.75f + 0.5f * rng.Next01()), box);
                }
            }
            SweepLowPass(pad, 650f, 1000f, 16f * 4f * beat);
            Add(mix, pad, 1f);
        }

        /// Sustained low hum (sine + a partner 0.5 Hz above → slow beating), soft attack and release.
        static void Hum(float[] a, int start, int len, float f, float amp)
        {
            int rel = N(1.2f), att = N(1.0f);
            int total = Math.Min(a.Length - start, len + rel);
            float p1 = 0f, p2 = 0f, i1 = f / SR, i2 = (f + 0.5f) / SR;
            for (int i = 0; i < total; i++)
            {
                float env = i < att ? i / (float)att : 1f;
                if (i > len) env *= 1f - (i - len) / (float)rel;
                a[start + i] += amp * env * 0.5f * (Sn(p1) + Sn(p2));
                p1 += i1; p2 += i2;
                if (p1 > 1f) p1 -= 1f; if (p2 > 1f) p2 -= 1f;
            }
        }

        // ------------------------------------------------------------------ water „Blaues Schweben“
        static void Water(float[] mix, float beat)
        {
            int[][] chords = { new[] { 63, 67, 70, 74 }, new[] { 65, 69, 72, 67 + 12 } };   // Ebmaj7, Fadd9 (lydian colour: the A natural)
            var pad = new float[mix.Length];
            var mar = new float[mix.Length];
            var rng = new FXRng(7171);
            for (int bar = 0; bar < Bars; bar += 2)
            {
                int c = (bar / 2) & 1;
                float barT = bar * 4f * beat;
                PadChord(pad, N(barT), N(8f * beat), chords[c], 0.6f, 2.2f, 2.4f, 0.8f);
                // a slow, low-passed marimba arpeggio in most bars
                for (int b = 0; b < 2; b++)
                {
                    if ((bar + b) % 8 == 7) continue;
                    float bt = barT + b * 4f * beat;
                    for (int q = 0; q < 4; q++)
                    {
                        if (rng.Next01() < 0.2f) continue;
                        Marimba(mar, N(bt + q * beat), Hz(chords[c][(q + b) & 3] - 12), 0.16f, 0.45f);
                    }
                }
                // sparse high bells
                if (rng.Next01() < 0.6f)
                {
                    float t = barT + beat * (1f + rng.Next01() * 5f);
                    Bell(mix, N(t), Hz(chords[c][rng.Int(4)] + 12), 0.07f, 1.4f);
                }
            }
            SweepLowPass(pad, 450f, 2400f, 8f * 4f * beat);
            SweepLowPass(mar, 1000f, 1000f, 0f);
            Add(mix, pad, 1f);
            Add(mix, mar, 1f);
        }

        // ------------------------------------------------------------------ deep „Tiefenstille“
        static void Deep(float[] mix, float beat)
        {
            var pad = new float[mix.Length];
            var rng = new FXRng(8181);
            int[] colours = { 53, 52, 55, 57 };   // F3, E3, G3, A3 shimmer over the D drone
            int seg = N(8f * 4f * beat);           // 8 bars per segment
            for (int s = 0; s < Bars / 8; s++)
            {
                int start = s * seg;
                PadChord(pad, start, seg, new[] { 38, 45, 50 }, 0.75f, 3.5f, 3.5f, 0.3f);
                PadChord(pad, start + N(4f * beat), seg - N(4f * beat), new[] { colours[s & 3], colours[s & 3] + 12 }, 0.22f, 4f, 3.5f, 0.2f);
            }
            SweepLowPass(pad, 300f, 750f, 12f * 4f * beat);
            Add(mix, pad, 1f);

            // distant chimes every 4–9 s (D minor pentatonic, high and soft)
            int[] chime = { 86, 89, 91, 93, 96 };
            float loop = Bars * 4f * beat;
            float t = 2f;
            while (t < loop - 2f)
            {
                Bell(mix, N(t), Hz(chime[rng.Int(chime.Length)]), 0.045f, 2.2f);
                if (rng.Next01() < 0.3f) Bell(mix, N(t + 0.6f), Hz(chime[rng.Int(chime.Length)]), 0.03f, 2.2f);
                t += rng.Range(4f, 9f);
            }
        }

        // ------------------------------------------------------------------ reverb
        /// Small Schroeder room (4 damped combs + 2 allpasses), wet mixed in.
        static void Reverb(float[] a, float wet, float feedback)
        {
            int[] delays = { 557, 593, 641, 677 };
            var lines = new float[4][];
            var pos = new int[4];
            var damp = new float[4];
            for (int k = 0; k < 4; k++) lines[k] = new float[delays[k]];
            var ap1 = new float[278];
            var ap2 = new float[220];
            int a1 = 0, a2 = 0;
            for (int i = 0; i < a.Length; i++)
            {
                float x = a[i];
                float sum = 0f;
                for (int k = 0; k < 4; k++)
                {
                    float y = lines[k][pos[k]];
                    damp[k] += 0.4f * (y - damp[k]);
                    lines[k][pos[k]] = x + damp[k] * feedback;
                    pos[k] = pos[k] + 1 == delays[k] ? 0 : pos[k] + 1;
                    sum += y;
                }
                sum *= 0.25f;
                float d = ap1[a1]; float v = sum + d * 0.5f; ap1[a1] = v; a1 = a1 + 1 == ap1.Length ? 0 : a1 + 1; sum = d - v * 0.5f;
                d = ap2[a2]; v = sum + d * 0.5f; ap2[a2] = v; a2 = a2 + 1 == ap2.Length ? 0 : a2 + 1; sum = d - v * 0.5f;
                a[i] = x + sum * wet;
            }
        }
    }
}
