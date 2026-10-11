using System;
using System.Collections.Generic;

namespace Sealebrities
{
    /// Raw mono samples (44.1 kHz) produced by FXAudioSynth; turned into AudioClips on the main thread.
    sealed class FXSynthBank
    {
        public float[] waves, jungle, drone, groan;
        public float[] inhale, exhale, heartbeat, splash, bump;
        public float[] blip, recOn, recOff, discover, fanfare, coin;
        public float[] warn, shutter, equip, cast, reel, catchFish, sparkle, click, ping, error;
        // round 2 (Q9, Q12): diving in, and the underwater soundscape
        public float[] plunge, dolphin, whale, crabs, shrimp, garibaldi, seaLion, angler;
    }

    /// xorshift32 noise source (thread-safe as a value owned by one generator).
    struct FXRng
    {
        uint s;
        public FXRng(uint seed) { s = seed == 0 ? 0x9E3779B9u : seed; }

        public float Next01()
        {
            s ^= s << 13;
            s ^= s >> 17;
            s ^= s << 5;
            return (s & 0xFFFFFF) / 16777216f;
        }

        public float Signed() => Next01() * 2f - 1f;
        public float Range(float a, float b) => a + (b - a) * Next01();
        public int Int(int maxExclusive) => (int)(Next01() * maxExclusive) % Math.Max(1, maxExclusive);
    }

    /// RBJ biquad filter (transposed direct form II).
    struct FXBiquad
    {
        float b0, b1, b2, a1, a2, z1, z2;

        static FXBiquad Make(float b0, float b1, float b2, float a0, float a1, float a2)
        {
            return new FXBiquad { b0 = b0 / a0, b1 = b1 / a0, b2 = b2 / a0, a1 = a1 / a0, a2 = a2 / a0 };
        }

        public static FXBiquad LowPass(float fc, float q)
        {
            float w = FXMath.TwoPi * fc / FXAudioSynth.SR, c = MathF.Cos(w), al = MathF.Sin(w) / (2f * q);
            return Make((1f - c) * 0.5f, 1f - c, (1f - c) * 0.5f, 1f + al, -2f * c, 1f - al);
        }

        public static FXBiquad HighPass(float fc, float q)
        {
            float w = FXMath.TwoPi * fc / FXAudioSynth.SR, c = MathF.Cos(w), al = MathF.Sin(w) / (2f * q);
            return Make((1f + c) * 0.5f, -(1f + c), (1f + c) * 0.5f, 1f + al, -2f * c, 1f - al);
        }

        public static FXBiquad BandPass(float fc, float q)
        {
            float w = FXMath.TwoPi * fc / FXAudioSynth.SR, c = MathF.Cos(w), al = MathF.Sin(w) / (2f * q);
            return Make(al, 0f, -al, 1f + al, -2f * c, 1f - al);
        }

        public float Process(float x)
        {
            float y = b0 * x + z1;
            z1 = b1 * x - a1 * y + z2;
            z2 = b2 * x - a2 * y;
            return y;
        }
    }

    /// Generates every FX sound from scratch (filtered noise, sines, envelopes). No Unity API: safe on a worker thread.
    static class FXAudioSynth
    {
        public const int SR = 44100;
        const float TwoPi = FXMath.TwoPi;

        public static FXSynthBank Generate()
        {
            return new FXSynthBank
            {
                waves = Waves(),
                jungle = Jungle(),
                drone = Drone(),
                groan = Groan(),
                inhale = Inhale(),
                exhale = Exhale(),
                heartbeat = Heartbeat(),
                splash = Splash(),
                bump = Bump(),
                blip = Blip(),
                recOn = RecBeeps(true),
                recOff = RecBeeps(false),
                discover = Discover(),
                fanfare = Fanfare(),
                coin = Coin(),
                warn = WarnBeep(),
                shutter = Shutter(),
                equip = Equip(),
                cast = Cast(),
                reel = Reel(),
                catchFish = CatchFish(),
                sparkle = Sparkle(),
                click = Click(),
                ping = Ping(),
                error = ErrorBoop(),
                plunge = Plunge(),
                dolphin = Dolphin(),
                whale = WhaleSong(),
                crabs = CrabClicks(),
                shrimp = ShrimpCrackle(),
                garibaldi = GaribaldiKnocks(),
                seaLion = SeaLionBarks(),
                angler = AnglerHum(),
            };
        }

        // ------------------------------------------------------------------ helpers
        static int N(float seconds) => (int)(seconds * SR);

        static float OnePole(float fc) => 1f - MathF.Exp(-TwoPi * fc / SR);

        /// buf holds n + xf samples; the head is equal-power crossfaded with the tail so the loop is seamless.
        static float[] MakeLoop(float[] buf, int n, int xf)
        {
            var o = new float[n];
            Array.Copy(buf, o, n);
            for (int i = 0; i < xf; i++)
            {
                float t = i / (float)xf;
                o[i] = buf[i] * MathF.Sqrt(t) + buf[n + i] * MathF.Sqrt(1f - t);
            }
            return o;
        }

        static void NormalizeRms(float[] a, float target)
        {
            double sum = 0;
            for (int i = 0; i < a.Length; i++) sum += a[i] * a[i];
            float rms = (float)Math.Sqrt(sum / Math.Max(1, a.Length));
            if (rms < 1e-9f) return;
            float k = target / rms;
            for (int i = 0; i < a.Length; i++) a[i] *= k;
        }

        static float[] NormalizePeak(float[] a, float peak)
        {
            float m = 0f;
            for (int i = 0; i < a.Length; i++) { float v = MathF.Abs(a[i]); if (v > m) m = v; }
            if (m < 1e-9f) return a;
            float k = peak / m;
            for (int i = 0; i < a.Length; i++) a[i] *= k;
            return a;
        }

        /// Short fade in/out to avoid clicks at the clip borders.
        static float[] Declick(float[] a, float fadeIn = 0.002f, float fadeOut = 0.01f)
        {
            int fi = Math.Min(a.Length / 2, N(fadeIn)), fo = Math.Min(a.Length / 2, N(fadeOut));
            for (int i = 0; i < fi; i++) a[i] *= i / (float)fi;
            for (int i = 0; i < fo; i++) a[a.Length - 1 - i] *= i / (float)fo;
            return a;
        }

        static float Swell(float ph)
        {
            float s1 = 0.5f + 0.5f * MathF.Sin(TwoPi * 2f * ph);
            float s2 = 0.5f + 0.5f * MathF.Sin(TwoPi * 3f * ph + 1.7f);
            float s3 = 0.5f + 0.5f * MathF.Sin(TwoPi * 5f * ph + 0.4f);
            return 0.55f * s1 + 0.3f * s2 + 0.15f * s3;
        }

        /// Rising "bloop" of a single air bubble.
        static void AddBubble(float[] a, int start, float f0, float dur, float amp)
        {
            int len = N(dur * 2.5f);
            float phase = 0f;
            for (int i = 0; i < len; i++)
            {
                int k = start + i;
                if (k < 0) continue;
                if (k >= a.Length) break;
                float t = i / (float)SR;
                float fr = f0 * (1f + 2.2f * t / dur);
                phase += TwoPi * fr / SR;
                if (phase > TwoPi) phase -= TwoPi;
                float env = (1f - MathF.Exp(-t / 0.0015f)) * MathF.Exp(-t / (dur * 0.35f));
                a[k] += amp * env * MathF.Sin(phase);
            }
        }

        /// Pitch-swept bird note with a soft sin^2 envelope.
        static void AddChirp(float[] a, int start, float dur, float fA, float fB, float amp)
        {
            int len = N(dur);
            float phase = 0f;
            for (int i = 0; i < len; i++)
            {
                int k = start + i;
                if (k < 0) continue;
                if (k >= a.Length) break;
                float x = i / (float)len;
                float fr = fA + (fB - fA) * x;
                phase += TwoPi * fr / SR;
                if (phase > TwoPi) phase -= TwoPi;
                float env = MathF.Sin(MathF.PI * x);
                env *= env;
                a[k] += amp * env * (MathF.Sin(phase) + 0.18f * MathF.Sin(2f * phase));
            }
        }

        /// Bell-like tone (slightly inharmonic partials).
        static void AddBell(float[] a, int start, float f, float amp, float decay)
        {
            int len = Math.Min(a.Length - start, N(decay * 6f));
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)SR;
                float att = 1f - MathF.Exp(-t / 0.003f);
                float s = MathF.Sin(TwoPi * f * t) * MathF.Exp(-t / decay)
                        + 0.45f * MathF.Sin(TwoPi * 2f * f * t) * MathF.Exp(-t / (decay * 0.55f))
                        + 0.2f * MathF.Sin(TwoPi * 3.01f * f * t) * MathF.Exp(-t / (decay * 0.3f));
                a[start + i] += amp * att * s;
            }
        }

        /// Square-ish beep (odd harmonics) with tiny attack/release ramps.
        static void AddBeep(float[] a, int start, float f, float dur, float amp)
        {
            int len = Math.Min(a.Length - start, N(dur));
            int ramp = N(0.004f);
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)SR;
                float env = Math.Min(1f, Math.Min(i / (float)ramp, (len - i) / (float)ramp));
                float s = MathF.Sin(TwoPi * f * t) + 0.22f * MathF.Sin(TwoPi * 3f * f * t) + 0.08f * MathF.Sin(TwoPi * 5f * f * t);
                a[start + i] += amp * env * s;
            }
        }

        /// Brass-like note: harmonic stack whose brightness follows the attack, with delayed vibrato.
        static void AddBrass(float[] a, int start, float f, float dur, float amp)
        {
            int len = Math.Min(a.Length - start, N(dur + 0.25f));
            float phase = 0f;
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)SR;
                float vib = t > 0.2f ? 1f + 0.006f * MathF.Sin(TwoPi * 5.5f * t) * Math.Min(1f, (t - 0.2f) * 4f) : 1f;
                phase += TwoPi * f * vib / SR;
                if (phase > TwoPi * 64f) phase -= TwoPi * 64f;
                float bright = 0.35f + 0.65f * MathF.Exp(-t / 0.22f);
                float s = 0f, g = 1f;
                for (int h = 1; h <= 9; h++)
                {
                    s += g / h * MathF.Sin(h * phase);
                    g *= bright;
                }
                float env = Math.Min(1f, t / 0.025f);
                if (t > dur) env *= MathF.Max(0f, 1f - (t - dur) / 0.25f);
                a[start + i] += amp * env * s;
            }
        }

        // ------------------------------------------------------------------ ambience loops
        /// Ocean waves: low rumble plus a wash whose brightness rises with each swell (16 s seamless loop).
        static float[] Waves()
        {
            const float L = 16f;
            int n = N(L), xf = SR, total = n + xf;
            var rumble = new float[total];
            var wash = new float[total];
            var rng = new FXRng(1234);
            var lp1 = FXBiquad.LowPass(320f, 0.7f);
            var lp2 = FXBiquad.LowPass(320f, 0.7f);
            float brown = 0f, w1 = 0f, w2 = 0f;
            for (int i = 0; i < total; i++)
            {
                float ph = i / (float)SR / L;
                float swell = Swell(ph);
                float crash = swell * swell * swell;
                float w = rng.Signed();
                brown = brown * 0.996f + w * 0.05f;
                rumble[i] = lp2.Process(lp1.Process(brown));
                float a = OnePole(350f + 3200f * crash);
                w1 += a * (w - w1);
                w2 += a * (w1 - w2);
                wash[i] = w2;
            }
            NormalizeRms(rumble, 1f);
            NormalizeRms(wash, 1f);
            var mix = new float[total];
            for (int i = 0; i < total; i++)
            {
                float swell = Swell(i / (float)SR / L);
                float crash = swell * swell * swell;
                mix[i] = rumble[i] * (0.5f + 0.35f * swell) + wash[i] * (0.12f + 0.95f * crash);
            }
            return NormalizePeak(MakeLoop(mix, n, xf), 0.9f);
        }

        /// Jungle: gusty wind, rustling leaves, cicadas and occasional bird calls (20 s seamless loop).
        static float[] Jungle()
        {
            const float L = 20f;
            int n = N(L), xf = SR, total = n + xf;
            var wind = new float[total];
            var leaves = new float[total];
            var rng = new FXRng(777);
            var windLp = FXBiquad.LowPass(700f, 0.6f);
            var leafHp = FXBiquad.HighPass(2800f, 0.7f);
            var leafLp = FXBiquad.LowPass(7000f, 0.7f);
            float b0 = 0f, b1 = 0f, b2 = 0f;
            for (int i = 0; i < total; i++)
            {
                float w = rng.Signed();
                // Paul Kellet's economy pink noise
                b0 = 0.99765f * b0 + w * 0.0990460f;
                b1 = 0.96300f * b1 + w * 0.2965164f;
                b2 = 0.57000f * b2 + w * 1.0526913f;
                float pink = b0 + b1 + b2 + w * 0.1848f;
                wind[i] = windLp.Process(pink);
                leaves[i] = leafLp.Process(leafHp.Process(rng.Signed()));
            }
            NormalizeRms(wind, 1f);
            NormalizeRms(leaves, 1f);
            var mix = new float[total];
            for (int i = 0; i < total; i++)
            {
                float ph = i / (float)SR / L;
                float gust = 0.55f + 0.25f * MathF.Sin(TwoPi * ph + 0.3f) + 0.12f * MathF.Sin(TwoPi * 3f * ph + 2f) + 0.08f * MathF.Sin(TwoPi * 7f * ph);
                float rustle = MathF.Max(0f, gust - 0.55f) * 3f;
                mix[i] = wind[i] * 0.16f * gust + leaves[i] * 0.05f * rustle * rustle;
            }
            var loop = MakeLoop(mix, n, xf);

            // cicadas: exactly periodic over the loop (integer cycles), added after the crossfade
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)SR;
                float ph = (float)(t / L);
                float swell = 0.5f - 0.5f * MathF.Cos(TwoPi * 2f * ph);
                float am = 0.5f + 0.5f * (float)Math.Sin(2.0 * Math.PI * 55.0 * t);
                am *= am; am *= am;
                float c = (float)(Math.Sin(2.0 * Math.PI * 4600.0 * t) * 0.6 + Math.Sin(2.0 * Math.PI * 5300.0 * t) * 0.4);
                loop[i] += c * am * swell * swell * 0.035f;
            }

            // birds, placed away from the loop seam
            var brng = new FXRng(4242);
            int calls = 9;
            for (int k = 0; k < calls; k++)
            {
                int start = N(0.4f + (L - 2.4f) * (k + brng.Next01() * 0.8f) / calls);
                float amp = brng.Range(0.06f, 0.16f);
                int type = brng.Int(3);
                if (type == 0)
                {
                    int notes = 2 + brng.Int(4);
                    float f0 = brng.Range(2400f, 4200f);
                    int pos = start;
                    for (int j = 0; j < notes; j++)
                    {
                        float dur = brng.Range(0.05f, 0.12f);
                        AddChirp(loop, pos, dur, f0, f0 + brng.Range(-1400f, 1600f), amp * brng.Range(0.7f, 1f));
                        pos += N(dur + brng.Range(0.025f, 0.08f));
                        f0 = Math.Max(1800f, f0 + brng.Range(-300f, 300f));
                    }
                }
                else if (type == 1)
                {
                    float fa = brng.Range(1300f, 1800f);
                    AddChirp(loop, start, 0.35f, fa, fa + brng.Range(500f, 900f), amp);
                    AddChirp(loop, start + N(0.45f), 0.3f, fa + 700f, fa + brng.Range(-100f, 200f), amp * 0.8f);
                }
                else
                {
                    float fa = brng.Range(2800f, 3600f);
                    int pos = start;
                    int notes = 6 + brng.Int(6);
                    for (int j = 0; j < notes; j++)
                    {
                        AddChirp(loop, pos, 0.035f, fa, fa - 600f, amp * 0.8f);
                        pos += N(0.05f);
                    }
                }
            }
            return NormalizePeak(loop, 0.85f);
        }

        /// Underwater drone: dark filtered rumble, slow water movement and a soft 55 Hz hum (12 s seamless loop).
        static float[] Drone()
        {
            const float L = 12f;
            int n = N(L), xf = SR, total = n + xf;
            var low = new float[total];
            var mid = new float[total];
            var rng = new FXRng(9001);
            var lp1 = FXBiquad.LowPass(180f, 0.7f);
            var lp2 = FXBiquad.LowPass(220f, 0.7f);
            var bp = FXBiquad.BandPass(600f, 0.8f);
            float brown = 0f;
            for (int i = 0; i < total; i++)
            {
                float w = rng.Signed();
                brown = brown * 0.997f + w * 0.05f;
                low[i] = lp2.Process(lp1.Process(brown));
                mid[i] = bp.Process(rng.Signed());
            }
            NormalizeRms(low, 1f);
            NormalizeRms(mid, 1f);
            var mix = new float[total];
            for (int i = 0; i < total; i++)
            {
                float ph = i / (float)SR / L;
                float lfo = 0.75f + 0.25f * MathF.Sin(TwoPi * 2f * ph);
                float move = 0.5f + 0.5f * MathF.Sin(TwoPi * ph + 1f);
                mix[i] = low[i] * 0.3f * lfo + mid[i] * 0.04f * move;
            }
            var loop = MakeLoop(mix, n, xf);
            // 55 Hz x 12 s = 660 whole cycles: periodic, added after the crossfade
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)SR;
                float ph = (float)(t / L);
                double w = 2.0 * Math.PI * 55.0 * t;
                float hum = (float)(0.16 * Math.Sin(w) + 0.05 * Math.Sin(2.0 * w + 0.3) + 0.02 * Math.Sin(3.0 * w + 1.1));
                loop[i] += hum * (0.8f + 0.2f * MathF.Sin(TwoPi * 3f * ph));
            }
            return NormalizePeak(loop, 0.8f);
        }

        /// Distant creepy groan from the deep (one-shot, ~7 s).
        static float[] Groan()
        {
            float dur = 7f;
            int n = N(dur);
            var a = new float[n];
            var rng = new FXRng(66);
            var formant = FXBiquad.BandPass(320f, 2.2f);
            var nlp = FXBiquad.LowPass(150f, 0.7f);
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SR;
                float glide = 62f * MathF.Pow(41f / 62f, t / dur);
                float fr = glide + 1.5f * MathF.Sin(TwoPi * 0.6f * t);
                phase += TwoPi * fr / SR;
                if (phase > TwoPi * 32f) phase -= TwoPi * 32f;
                float tone = MathF.Sin(phase) + 0.5f * MathF.Sin(2f * phase) + 0.3f * MathF.Sin(3f * phase) + 0.12f * MathF.Sin(5f * phase);
                float voiced = formant.Process(tone) * 2.5f + tone * 0.25f;
                float env = FXMath.Smooth01(t / 1.8f) * FXMath.Smooth01((dur - t) / 3f);
                a[i] = (voiced + nlp.Process(rng.Signed()) * 1.5f) * env;
            }
            return Declick(NormalizePeak(a, 0.85f));
        }

        // ------------------------------------------------------------------ diver
        /// Regulator inhale: valve click and a breathy hiss (1.2 s, Q12).
        static float[] Inhale()
        {
            float dur = 1.2f;
            int n = N(dur);
            var a = new float[n];
            var rng = new FXRng(31);
            var bp = FXBiquad.BandPass(3400f, 0.7f);
            var hp = FXBiquad.HighPass(1200f, 0.7f);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SR;
                float env = FXMath.Smooth01(t / 0.12f) * (0.8f + 0.2f * t / dur) * FXMath.Smooth01((dur - t) / 0.25f);
                float hiss = hp.Process(bp.Process(rng.Signed()));
                float whistle = 0.03f * MathF.Sin(TwoPi * 2400f * t);
                float click = t < 0.005f ? rng.Signed() * 0.6f * (1f - t / 0.005f) : 0f;
                a[i] = (hiss + whistle) * env + click;
            }
            return Declick(NormalizePeak(a, 0.8f));
        }

        /// Regulator exhale: bubbly rumble with individual bubble bloops (1.6 s, Q12: exhale + bubble burst).
        static float[] Exhale()
        {
            float dur = 1.6f;
            int n = N(dur);
            var a = new float[n];
            var rng = new FXRng(57);
            var lp = FXBiquad.LowPass(480f, 0.8f);
            var burbleBp = FXBiquad.BandPass(260f, 1.4f);
            float slow = 0f, brown = 0f;
            float aSlow = OnePole(14f);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SR;
                float w = rng.Signed();
                brown = brown * 0.995f + w * 0.06f;
                slow += aSlow * (rng.Signed() * 20f - slow);   // ~14 Hz random burble modulation (rms ~0.35)
                float am = MathF.Max(0f, 0.45f + slow);
                float env = FXMath.Smooth01(t / 0.06f) * (1f - 0.35f * t / dur) * FXMath.Smooth01((dur - t) / 0.35f);
                a[i] = (lp.Process(brown) * 1.6f * (0.4f + 1.2f * am) + burbleBp.Process(w) * 0.5f * am) * env;
            }
            var bubbles = new FXRng(58);
            for (int k = 0; k < 42; k++)
            {
                float t0 = bubbles.Range(0.02f, 1.25f);
                AddBubble(a, N(t0), bubbles.Range(260f, 1100f), bubbles.Range(0.015f, 0.055f), bubbles.Range(0.08f, 0.3f) * (1f - 0.4f * t0 / 1.25f));
            }
            return Declick(NormalizePeak(a, 0.85f));
        }

        /// One "lub-dub".
        static float[] Heartbeat()
        {
            int n = N(0.75f);
            var a = new float[n];
            Thump(a, 0, 58f, 38f, 0.07f, 1f);
            Thump(a, N(0.26f), 70f, 45f, 0.055f, 0.65f);
            var lp = FXBiquad.LowPass(160f, 0.7f);
            for (int i = 0; i < n; i++) a[i] = lp.Process(a[i]);
            return Declick(NormalizePeak(a, 0.9f));
        }

        static void Thump(float[] a, int start, float fA, float fB, float decay, float amp)
        {
            float phase = 0f;
            int len = Math.Min(a.Length - start, N(decay * 7f));
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)SR;
                float fr = fB + (fA - fB) * MathF.Exp(-t / 0.05f);
                phase += TwoPi * fr / SR;
                float env = (1f - MathF.Exp(-t / 0.004f)) * MathF.Exp(-t / decay);
                a[start + i] += amp * env * (MathF.Sin(phase) + 0.25f * MathF.Sin(2f * phase));
            }
        }

        /// Surface splash: bright noise burst sweeping darker, a plunge thump and a few bubbles.
        static float[] Splash()
        {
            float dur = 1.1f;
            int n = N(dur);
            var a = new float[n];
            var rng = new FXRng(808);
            float y1 = 0f, y2 = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SR;
                float fc = 600f + 6400f * MathF.Exp(-t / 0.25f);
                float c = OnePole(fc);
                float w = rng.Signed();
                y1 += c * (w - y1);
                y2 += c * (y1 - y2);
                float env = (1f - MathF.Exp(-t / 0.003f)) * MathF.Exp(-t / 0.22f);
                a[i] = (y2 * 2.2f + (w - y1) * 0.25f * MathF.Exp(-t / 0.06f)) * env;
            }
            Thump(a, 0, 80f, 50f, 0.09f, 0.6f);
            var b = new FXRng(809);
            for (int k = 0; k < 14; k++)
                AddBubble(a, N(b.Range(0.12f, 0.85f)), b.Range(300f, 900f), b.Range(0.02f, 0.05f), b.Range(0.05f, 0.15f));
            return Declick(NormalizePeak(a, 0.9f), 0.001f, 0.05f);
        }

        /// Dull underwater thud when a predator bumps the diver.
        static float[] Bump()
        {
            int n = N(0.6f);
            var a = new float[n];
            Thump(a, 0, 60f, 34f, 0.12f, 1f);
            var rng = new FXRng(4);
            var lp = FXBiquad.LowPass(300f, 0.7f);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SR;
                a[i] += lp.Process(rng.Signed()) * 1.2f * MathF.Exp(-t / 0.08f);
            }
            return Declick(NormalizePeak(a, 0.9f));
        }

        // ------------------------------------------------------------------ UI / rewards
        static float[] Blip()
        {
            int n = N(0.09f);
            var a = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SR;
                float env = (1f - MathF.Exp(-t / 0.002f)) * MathF.Exp(-t / 0.025f);
                a[i] = env * (MathF.Sin(TwoPi * 1046.5f * t) + 0.25f * MathF.Sin(TwoPi * 2093f * t));
            }
            return Declick(NormalizePeak(a, 0.7f));
        }

        static float[] RecBeeps(bool on)
        {
            var a = new float[N(0.3f)];
            float f1 = on ? 880f : 1318.5f, f2 = on ? 1318.5f : 880f;
            AddBeep(a, 0, f1, 0.08f, 0.5f);
            AddBeep(a, N(0.13f), f2, on ? 0.11f : 0.14f, 0.5f);
            return Declick(NormalizePeak(a, 0.7f));
        }

        /// New species: sparkly E-major arpeggio.
        static float[] Discover()
        {
            var a = new float[N(2.1f)];
            float[] notes = { 659.25f, 830.61f, 987.77f, 1318.51f };
            for (int k = 0; k < notes.Length; k++) AddBell(a, N(0.1f * k + (k == 3 ? 0.03f : 0f)), notes[k], k == 3 ? 0.9f : 0.6f, k == 3 ? 0.6f : 0.35f);
            var rng = new FXRng(2024);
            for (int k = 0; k < 7; k++) AddBell(a, N(rng.Range(0.36f, 0.95f)), rng.Range(2600f, 4200f), 0.09f, 0.08f);
            return Declick(NormalizePeak(a, 0.85f), 0.002f, 0.05f);
        }

        /// New size record: little brass fanfare with a cymbal swell.
        static float[] Fanfare()
        {
            var a = new float[N(2.6f)];
            AddBrass(a, 0, 392f, 0.13f, 0.5f);
            AddBrass(a, N(0.15f), 523.25f, 0.13f, 0.5f);
            AddBrass(a, N(0.3f), 659.25f, 0.13f, 0.5f);
            int hit = N(0.45f);
            AddBrass(a, hit, 783.99f, 1.6f, 0.6f);
            AddBrass(a, hit, 523.25f, 1.6f, 0.3f);
            AddBrass(a, hit, 659.25f, 1.6f, 0.3f);
            Thump(a, hit, 110f, 70f, 0.18f, 0.6f);
            var rng = new FXRng(321);
            var hp = FXBiquad.HighPass(5000f, 0.7f);
            for (int i = hit; i < a.Length; i++)
            {
                float t = (i - hit) / (float)SR;
                a[i] += hp.Process(rng.Signed()) * 0.35f * (1f - MathF.Exp(-t / 0.004f)) * MathF.Exp(-t / 0.5f);
            }
            return Declick(NormalizePeak(a, 0.85f), 0.002f, 0.1f);
        }

        /// Money: classic two-tone coin.
        static float[] Coin()
        {
            int n = N(0.6f);
            var a = new float[n];
            int split = N(0.075f);
            int glide = N(0.003f);
            float phase = 0f, env = 0.8f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SR;
                // phase-continuous oscillator with a 3 ms pitch glide: no click at the note change
                float k = FXMath.Clamp01((i - split) / (float)glide);
                float f = 987.77f + (1318.51f - 987.77f) * k;
                phase += TwoPi * f / SR;
                if (phase > TwoPi) phase -= TwoPi;
                float target = i < split ? 0.8f : MathF.Exp(-(t - 0.075f) / 0.16f);
                env += (target - env) * 0.02f;   // ~1 ms smoothing of the envelope step
                a[i] = env * (MathF.Sin(phase) + 0.3f * MathF.Sin(3f * phase) + 0.12f * MathF.Sin(5f * phase));
            }
            return Declick(NormalizePeak(a, 0.6f));
        }

        // ------------------------------------------------------------------ extra one-shots (FXSfx)
        /// Warning "piep piep" (low air / too deep), soft so it can repeat.
        static float[] WarnBeep()
        {
            var a = new float[N(0.32f)];
            AddSoftBeep(a, 0, 1318.5f, 0.075f, 0.6f);
            AddSoftBeep(a, N(0.16f), 1318.5f, 0.075f, 0.6f);
            return Declick(NormalizePeak(a, 0.6f));
        }

        static void AddSoftBeep(float[] a, int start, float f, float dur, float amp)
        {
            int len = Math.Min(a.Length - start, N(dur + 0.06f));
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)SR;
                float env = (1f - MathF.Exp(-t / 0.004f)) * (t < dur ? 1f : MathF.Exp(-(t - dur) / 0.015f));
                a[start + i] += amp * env * (MathF.Sin(TwoPi * f * t) + 0.12f * MathF.Sin(TwoPi * 2f * f * t));
            }
        }

        /// Camera shutter: sharp click, mechanical clack, short motor whirr.
        static float[] Shutter()
        {
            int n = N(0.28f);
            var a = new float[n];
            var rng = new FXRng(1701);
            var hp = FXBiquad.HighPass(2500f, 0.7f);
            var bp = FXBiquad.BandPass(1400f, 1.2f);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SR;
                float w = rng.Signed();
                float t1 = t - 0.003f;   // after the declick fade-in
                float click = t1 > 0f ? hp.Process(w) * MathF.Exp(-t1 / 0.004f) : 0f;
                float t2 = t - 0.045f;
                float clack = t2 > 0f ? bp.Process(w) * 1.6f * MathF.Exp(-t2 / 0.012f) : 0f;
                float t3 = t - 0.08f;
                float whirr = t3 > 0f && t3 < 0.16f ? 0.12f * MathF.Sin(TwoPi * 420f * t3) * MathF.Sin(MathF.PI * t3 / 0.16f) * (0.6f + 0.4f * MathF.Sin(TwoPi * 60f * t3)) : 0f;
                a[i] = click + clack + whirr;
            }
            return Declick(NormalizePeak(a, 0.75f));
        }

        /// Hotbar equip: a soft cloth "fwip" and a tiny click.
        static float[] Equip()
        {
            int n = N(0.16f);
            var a = new float[n];
            var rng = new FXRng(88);
            float y1 = 0f, y2 = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SR;
                float x = t / 0.13f;
                float fc = 700f + 2600f * x;
                float c = OnePole(fc);
                float w = rng.Signed();
                y1 += c * (w - y1);
                y2 += c * (y1 - y2);
                float env = x < 1f ? MathF.Sin(MathF.PI * x) : 0f;
                a[i] = (y1 - y2) * 2.2f * env * env;
                float tc = t - 0.12f;
                if (tc > 0f) a[i] += 0.25f * MathF.Sin(TwoPi * 2200f * tc) * MathF.Exp(-tc / 0.006f);
            }
            return Declick(NormalizePeak(a, 0.5f));
        }

        /// Fishing rod cast: a whoosh through the air, then the line zipping off the reel.
        static float[] Cast()
        {
            int n = N(0.95f);
            var a = new float[n];
            var rng = new FXRng(4040);
            var bp = FXBiquad.BandPass(800f, 1.1f);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SR;
                float x = t / 0.32f;
                if (x < 1f)
                {
                    // the band-pass is recreated on a coarse grid (sweep 500 -> 2400 -> 900 Hz)
                    if ((i & 255) == 0) bp = FXBiquad.BandPass(500f + 1900f * MathF.Sin(MathF.PI * MathF.Min(1f, x * 1.3f)), 1.1f);
                    float env = MathF.Sin(MathF.PI * x);
                    a[i] += bp.Process(rng.Signed()) * 2.4f * env * env;
                }
            }
            // reel zip: clicks from 70 Hz slowing to 20 Hz
            float ph = 0f;
            int start = N(0.22f);
            for (int i = start; i < n; i++)
            {
                float t = (i - start) / (float)SR;
                float rate = 20f + 50f * MathF.Exp(-t / 0.25f);
                ph += rate / SR;
                if (ph >= 1f)
                {
                    ph -= 1f;
                    AddTick(a, i, 3200f, 0.18f * MathF.Exp(-t / 0.5f));
                }
            }
            return Declick(NormalizePeak(a, 0.7f));
        }

        static void AddTick(float[] a, int start, float f, float amp)
        {
            int len = Math.Min(a.Length - start, N(0.012f));
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)SR;
                a[start + i] += amp * MathF.Sin(TwoPi * f * t) * MathF.Exp(-t / 0.0025f);
            }
        }

        /// Reel ratchet (winding in).
        static float[] Reel()
        {
            int n = N(0.7f);
            var a = new float[n];
            int step = N(1f / 17f);
            var rng = new FXRng(77);
            for (int i = 0, k = 0; i < n; i += step, k++)
                AddTick(a, i, 2600f + 400f * rng.Next01(), 0.5f * (0.7f + 0.3f * (k & 1)));
            return Declick(NormalizePeak(a, 0.55f));
        }

        /// A fish pulled out of the water: small splash + two bloops.
        static float[] CatchFish()
        {
            float dur = 0.55f;
            int n = N(dur);
            var a = new float[n];
            var rng = new FXRng(919);
            float y1 = 0f, y2 = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SR;
                float c = OnePole(900f + 5200f * MathF.Exp(-t / 0.12f));
                float w = rng.Signed();
                y1 += c * (w - y1);
                y2 += c * (y1 - y2);
                a[i] = y2 * 2f * (1f - MathF.Exp(-t / 0.002f)) * MathF.Exp(-t / 0.11f);
            }
            AddBubble(a, N(0.05f), 520f, 0.035f, 0.25f);
            AddBubble(a, N(0.14f), 760f, 0.03f, 0.18f);
            return Declick(NormalizePeak(a, 0.7f), 0.001f, 0.04f);
        }

        /// Collectible found: quick rising pentatonic sparkle.
        static float[] Sparkle()
        {
            var a = new float[N(1.2f)];
            float[] notes = { 1046.5f, 1174.7f, 1318.5f, 1568f, 1760f };
            for (int k = 0; k < notes.Length; k++) AddBell(a, N(0.055f * k), notes[k], 0.35f, 0.18f + 0.04f * k);
            return Declick(NormalizePeak(a, 0.6f), 0.002f, 0.05f);
        }

        /// UI click.
        static float[] Click()
        {
            int n = N(0.03f);
            var a = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SR;
                a[i] = MathF.Sin(TwoPi * 1900f * t) * MathF.Exp(-t / 0.005f) + 0.3f * MathF.Sin(TwoPi * 3800f * t) * MathF.Exp(-t / 0.002f);
            }
            return Declick(NormalizePeak(a, 0.45f), 0.0005f, 0.005f);
        }

        /// Notification "ding" (phone banner).
        static float[] Ping()
        {
            var a = new float[N(0.9f)];
            AddBell(a, 0, 1568f, 0.55f, 0.22f);
            AddBell(a, N(0.08f), 2093f, 0.4f, 0.25f);
            return Declick(NormalizePeak(a, 0.55f), 0.002f, 0.05f);
        }

        /// Not possible / denied: two low descending boops.
        static float[] ErrorBoop()
        {
            var a = new float[N(0.32f)];
            AddBeep(a, 0, 392f, 0.1f, 0.5f);
            AddBeep(a, N(0.13f), 294f, 0.14f, 0.5f);
            var lp = FXBiquad.LowPass(1800f, 0.7f);
            for (int i = 0; i < a.Length; i++) a[i] = lp.Process(a[i]);
            return Declick(NormalizePeak(a, 0.5f));
        }

        // ------------------------------------------------------------------ round 2: diving in + underwater soundscape (Q9, Q12)
        /// Diving in (Q9 "plunge"): a deep whoomp as the head goes under, then a rush of rising bubbles past the mask.
        static float[] Plunge()
        {
            float dur = 1.3f;
            int n = N(dur);
            var a = new float[n];
            var rng = new FXRng(1212);
            var lp = FXBiquad.LowPass(420f, 0.8f);
            var bp = FXBiquad.BandPass(900f, 0.9f);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SR;
                float w = rng.Signed();
                // muffled water rush: low-passed noise swelling in and fading out
                float rush = lp.Process(w) * 2.2f * (1f - MathF.Exp(-t / 0.03f)) * MathF.Exp(-t / 0.35f);
                float fizz = bp.Process(w) * 0.35f * FXMath.Smooth01(t / 0.08f) * MathF.Exp(-t / 0.5f);
                a[i] = rush + fizz;
            }
            Thump(a, 0, 75f, 38f, 0.16f, 1.1f);
            var b = new FXRng(1213);
            for (int k = 0; k < 34; k++)
            {
                float t0 = b.Range(0.03f, 0.95f);
                AddBubble(a, N(t0), b.Range(220f, 900f), b.Range(0.02f, 0.06f), b.Range(0.06f, 0.2f) * (1f - 0.6f * t0));
            }
            return Declick(NormalizePeak(a, 0.9f), 0.001f, 0.08f);
        }

        /// Dolphins (heard through water): two or three swooping whistles and a burst of echolocation clicks.
        static float[] Dolphin()
        {
            float dur = 1.9f;
            int n = N(dur);
            var a = new float[n];
            var rng = new FXRng(4711);
            float[] starts = { 0.02f, 0.62f, 1.18f };
            float[] lens = { 0.48f, 0.4f, 0.55f };
            float[] f0s = { 5200f, 7400f, 4600f };
            float[] sweeps = { 2600f, -2200f, 3400f };
            for (int w = 0; w < starts.Length; w++)
            {
                int s0 = N(starts[w]), len = N(lens[w]);
                float phase = 0f;
                for (int i = 0; i < len && s0 + i < n; i++)
                {
                    float x = i / (float)len;
                    float fr = f0s[w] + sweeps[w] * x + 900f * MathF.Sin(MathF.PI * 2f * 1.6f * x + w);
                    phase += TwoPi * fr / SR;
                    if (phase > TwoPi) phase -= TwoPi;
                    float env = MathF.Sin(MathF.PI * x);
                    env *= env;
                    a[s0 + i] += 0.5f * env * (MathF.Sin(phase) + 0.25f * MathF.Sin(2f * phase));
                }
            }
            // click train ("creak"): short broadband ticks, rate rising from 30 to 120 per second
            var hp = FXBiquad.HighPass(2500f, 0.7f);
            float ct = 0.95f;
            while (ct < 1.15f)
            {
                int s = N(ct);
                for (int i = 0; i < N(0.0012f) && s + i < n; i++) a[s + i] += hp.Process(rng.Signed()) * 0.9f * (1f - i / (float)N(0.0012f));
                float rate = 30f + 90f * (ct - 0.95f) / 0.2f;
                ct += 1f / rate;
            }
            // heard through water: the very top is gone
            var lp = FXBiquad.LowPass(7000f, 0.7f);
            for (int i = 0; i < n; i++) a[i] = lp.Process(a[i]);
            return Declick(NormalizePeak(a, 0.7f), 0.003f, 0.05f);
        }

        /// Whale song (Big Bertha, Q12): long, slow moans gliding through ~0.1 Hz swells, with a distant echo.
        static float[] WhaleSong()
        {
            float dur = 8f;
            int n = N(dur);
            var dry = new float[n];
            var formant = FXBiquad.BandPass(380f, 1.6f);
            var formant2 = FXBiquad.BandPass(820f, 2.2f);
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SR;
                // three phrases: a low moan, a rising "whoop", a falling sigh
                float fr;
                if (t < 3.2f) fr = 118f - 26f * MathF.Sin(MathF.PI * t / 3.2f);
                else if (t < 5.0f) fr = 95f + 140f * FXMath.Smooth01((t - 3.2f) / 1.8f);
                else fr = 235f - 120f * FXMath.Smooth01((t - 5f) / 2.6f);
                fr *= 1f + 0.012f * MathF.Sin(TwoPi * 0.15f * t);          // slow 0.15 Hz waver
                phase += TwoPi * fr / SR;
                if (phase > TwoPi * 64f) phase -= TwoPi * 64f;
                float tone = MathF.Sin(phase) + 0.55f * MathF.Sin(2f * phase) + 0.3f * MathF.Sin(3f * phase) + 0.15f * MathF.Sin(4f * phase);
                float env;
                if (t < 3.2f) env = MathF.Sin(MathF.PI * t / 3.2f);
                else if (t < 5f) env = 0.9f * MathF.Sin(MathF.PI * (t - 3.2f) / 1.8f);
                else env = 0.8f * MathF.Sin(MathF.PI * MathF.Min(1f, (t - 5f) / 2.9f));
                dry[i] = (formant.Process(tone) * 2.4f + formant2.Process(tone) * 0.8f + tone * 0.18f) * env;
            }
            // far away: two echoes and a soft low-pass
            var a = new float[n];
            int e1 = N(0.42f), e2 = N(0.97f);
            var lp = FXBiquad.LowPass(1300f, 0.7f);
            for (int i = 0; i < n; i++)
            {
                float v = dry[i] + (i >= e1 ? 0.45f * dry[i - e1] : 0f) + (i >= e2 ? 0.25f * dry[i - e2] : 0f);
                a[i] = lp.Process(v);
            }
            return Declick(NormalizePeak(a, 0.85f), 0.05f, 0.4f);
        }

        /// Crabs in the tide pools: irregular dry clicks and little clicking runs.
        static float[] CrabClicks()
        {
            float dur = 1.5f;
            var a = new float[N(dur)];
            var rng = new FXRng(808080);
            float t = 0.02f;
            while (t < dur - 0.05f)
            {
                int start = N(t);
                float f = rng.Range(3200f, 5200f);
                AddClick(a, start, f, rng.Range(0.4f, 1f), ref rng);
                if (rng.Next01() < 0.35f) AddClick(a, start + N(rng.Range(0.012f, 0.025f)), f * 1.1f, rng.Range(0.3f, 0.7f), ref rng);
                t += rng.Range(0.05f, 0.24f);
            }
            return Declick(NormalizePeak(a, 0.6f));
        }

        static void AddClick(float[] a, int start, float f, float amp, ref FXRng rng)
        {
            int len = Math.Min(a.Length - start, N(0.006f));
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)SR;
                float env = MathF.Exp(-t / 0.0011f);
                a[start + i] += amp * env * (0.7f * MathF.Sin(TwoPi * f * t) + 0.5f * rng.Signed());
            }
        }

        /// Snapping shrimp on the reef bed: a dense, crackling "frying pan" (6 s seamless loop).
        static float[] ShrimpCrackle()
        {
            float dur = 6f;
            int n = N(dur);
            var a = new float[n];
            var rng = new FXRng(77777);
            int pops = (int)(dur * 230f);
            for (int k = 0; k < pops; k++)
            {
                int start = (int)(rng.Next01() * n);
                float r = rng.Next01();
                float amp = 0.08f + 0.9f * r * r * r * r;      // mostly faint, a few loud snaps
                int len = N(rng.Range(0.0004f, 0.0016f));
                for (int i = 0; i < len; i++)
                {
                    int idx = start + i;
                    if (idx >= n) idx -= n;                   // wrap: the loop seam is seamless
                    a[idx] += amp * rng.Signed() * (1f - i / (float)len);
                }
            }
            var hp = FXBiquad.HighPass(1800f, 0.7f);
            var lp = FXBiquad.LowPass(9000f, 0.7f);
            // run the filters twice over the loop so the seam starts with a settled filter state
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < n; i++)
                {
                    float v = lp.Process(hp.Process(a[i]));
                    if (pass == 1) a[i] = v;
                }
            return NormalizePeak(a, 0.7f);
        }

        /// Garibaldi defending their nest: a few low knocks ("thump-thump").
        static float[] GaribaldiKnocks()
        {
            var a = new float[N(1.1f)];
            var rng = new FXRng(2525);
            float t = 0.01f;
            for (int k = 0; k < 4; k++)
            {
                float f = rng.Range(320f, 480f);
                Knock(a, N(t), f, rng.Range(0.6f, 1f));
                t += rng.Range(0.11f, 0.26f);
            }
            return Declick(NormalizePeak(a, 0.6f));
        }

        static void Knock(float[] a, int start, float f, float amp)
        {
            int len = Math.Min(a.Length - start, N(0.12f));
            float phase = 0f;
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)SR;
                float fr = f * (1f + 0.6f * MathF.Exp(-t / 0.008f));
                phase += TwoPi * fr / SR;
                float env = (1f - MathF.Exp(-t / 0.0008f)) * MathF.Exp(-t / 0.028f);
                a[start + i] += amp * env * (MathF.Sin(phase) + 0.3f * MathF.Sin(2.7f * phase));
            }
        }

        /// Sea lion (Sir Flapsalot): honking barks, muffled by the water.
        static float[] SeaLionBarks()
        {
            float dur = 1.6f;
            var a = new float[N(dur)];
            var rng = new FXRng(3131);
            float[] at = { 0.02f, 0.42f, 0.86f };
            var f1 = FXBiquad.BandPass(720f, 2.5f);
            var f2 = FXBiquad.BandPass(1350f, 3f);
            var noise = FXBiquad.BandPass(1100f, 1.2f);
            for (int b = 0; b < at.Length; b++)
            {
                int s0 = N(at[b]), len = N(0.24f);
                float phase = 0f;
                float pitch = 1f + 0.06f * b;
                for (int i = 0; i < len && s0 + i < a.Length; i++)
                {
                    float t = i / (float)SR;
                    float fr = (250f - 70f * t / 0.24f) * pitch;
                    phase += TwoPi * fr / SR;
                    if (phase > TwoPi * 32f) phase -= TwoPi * 32f;
                    // buzzy source (many harmonics) through two vocal formants
                    float src = 0f;
                    for (int h = 1; h <= 8; h++) src += MathF.Sin(h * phase) / h;
                    float env = (1f - MathF.Exp(-t / 0.01f)) * MathF.Exp(-t / 0.09f);
                    a[s0 + i] += env * (f1.Process(src) * 2.2f + f2.Process(src) * 1.4f + noise.Process(rng.Signed()) * 0.35f);
                }
            }
            var lp = FXBiquad.LowPass(1900f, 0.7f);
            for (int i = 0; i < a.Length; i++) a[i] = lp.Process(a[i]);
            return Declick(NormalizePeak(a, 0.7f), 0.002f, 0.05f);
        }

        /// Anglerfish "Lumen" in the trench: an eerie low hum with a slow beat.
        static float[] AnglerHum()
        {
            float dur = 4f;
            int n = N(dur);
            var a = new float[n];
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)SR;
                float env = FXMath.Smooth01((float)t / 0.9f) * FXMath.Smooth01((dur - (float)t) / 1.2f);
                double w1 = 2.0 * Math.PI * 61.0 * t, w2 = 2.0 * Math.PI * 63.4 * t;
                float hum = (float)(0.5 * Math.Sin(w1) + 0.45 * Math.Sin(w2) + 0.18 * Math.Sin(2.0 * w1 + 0.4) + 0.08 * Math.Sin(3.0 * w2));
                // a faint glassy overtone that comes and goes
                float glass = 0.05f * MathF.Sin((float)(2.0 * Math.PI * 732.0 * t)) * (0.5f + 0.5f * MathF.Sin((float)(2.0 * Math.PI * 0.7 * t)));
                a[i] = (hum + glass) * env;
            }
            return Declick(NormalizePeak(a, 0.75f), 0.01f, 0.2f);
        }
    }
}
