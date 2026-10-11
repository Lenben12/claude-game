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
        /// Regulator inhale: valve click and a breathy hiss.
        static float[] Inhale()
        {
            float dur = 1.1f;
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

        /// Regulator exhale: bubbly rumble with individual bubble bloops.
        static float[] Exhale()
        {
            float dur = 1.7f;
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
                float t0 = bubbles.Range(0.02f, 1.35f);
                AddBubble(a, N(t0), bubbles.Range(260f, 1100f), bubbles.Range(0.015f, 0.055f), bubbles.Range(0.08f, 0.3f) * (1f - 0.4f * t0 / 1.35f));
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

        // ------------------------------------------------------------------ music: ukulele beach loop
        public const float MusicBpm = 104f;

        // chord shapes on a re-entrant ukulele (strings G4 C4 E4 A4)
        static readonly float[] ChordC = { 392.00f, 261.63f, 329.63f, 523.25f };    // 0003
        static readonly float[] ChordAm = { 440.00f, 261.63f, 329.63f, 440.00f };   // 2000
        static readonly float[] ChordF = { 440.00f, 261.63f, 349.23f, 440.00f };    // 2010
        static readonly float[] ChordG7 = { 392.00f, 293.66f, 349.23f, 493.88f };   // 0212
        static readonly float[] BassRoot = { 130.81f, 110.00f, 87.31f, 98.00f };    // C3 A2 F2 G2
        static readonly float[] BassFifth = { 98.00f, 82.41f, 130.81f, 146.83f };   // G2 E2 C3 D3

        // melody in 8th notes per bar (MIDI, 0 = rest): C major pentatonic, a lazy beach tune
        static readonly int[] Melody =
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

        /// Seamless 8-bar island loop (C – Am – F – G7, 104 BPM, ~18.5 s): Karplus-Strong ukulele strums with a
        /// laid-back swing, round bass, shaker and a soft marimba melody, a little room reverb. Main menu / intro.
        public static float[] Music()
        {
            float beat = 60f / MusicBpm;
            const int Bars = 8;
            int n = N(Bars * 4 * beat);
            int tail = N(2.5f);
            var uke = new float[n + tail];
            var low = new float[n + tail];
            var perc = new float[n + tail];
            var mel = new float[n + tail];
            float[][] chords = { ChordC, ChordAm, ChordF, ChordG7, ChordC, ChordAm, ChordF, ChordG7 };
            int[] chordIdx = { 0, 1, 2, 3, 0, 1, 2, 3 };

            // strum pattern (8th slots): D . D U . U D U
            int[] slots = { 0, 2, 3, 5, 6, 7 };
            bool[] down = { true, true, false, false, true, false };
            float[] vel = { 1f, 0.72f, 0.5f, 0.56f, 0.8f, 0.48f };
            const float Swing = 0.11f;   // off-beat 8ths a little late
            float eighth = beat * 0.5f;

            // strum times (for each string: ring until the next strum)
            var times = new List<float>();
            var chordOf = new List<int>();
            var downOf = new List<bool>();
            var velOf = new List<float>();
            for (int bar = 0; bar < Bars; bar++)
                for (int s = 0; s < slots.Length; s++)
                {
                    int slot = slots[s];
                    float t = bar * 4f * beat + slot * eighth + ((slot & 1) == 1 ? Swing * beat : 0f);
                    times.Add(t); chordOf.Add(bar); downOf.Add(down[s]); velOf.Add(vel[s]);
                }
            float loopLen = Bars * 4f * beat;
            uint seed = 11;
            for (int e = 0; e < times.Count; e++)
            {
                float t0 = times[e];
                float next = e + 1 < times.Count ? times[e + 1] : loopLen + times[0];
                float[] chord = chords[chordOf[e]];
                bool dn = downOf[e];
                float v = velOf[e];
                for (int k = 0; k < 4; k++)
                {
                    int str = dn ? k : 3 - k;
                    float ts = t0 + k * (dn ? 0.011f : 0.008f);
                    float amp = v * (dn ? 0.32f : 0.24f) * (str == 1 && !dn ? 0.6f : 1f);
                    int start = N(ts);
                    int len = N(Math.Min(1.7f, next - ts + 0.03f));
                    Pluck(uke, start, chord[str], amp, 0.32f + 0.45f * v, len, seed++);
                }
            }

            // bass: root on 1, fifth on 3
            for (int bar = 0; bar < Bars; bar++)
            {
                int c = chordIdx[bar];
                float bt = bar * 4f * beat;
                AddBass(low, N(bt), BassRoot[c], 0.5f, N(beat * 1.9f));
                AddBass(low, N(bt + 2f * beat), BassFifth[c], 0.42f, N(beat * 1.9f));
            }

            // shaker on every 8th, accents off-beat
            var srng = new FXRng(303);
            for (int i8 = 0; i8 < Bars * 8; i8++)
            {
                float t = i8 * eighth + ((i8 & 1) == 1 ? Swing * beat : 0f);
                AddShaker(perc, N(t), (i8 & 1) == 1 ? 0.12f : 0.07f, ref srng);
            }

            // marimba melody
            for (int i8 = 0; i8 < Melody.Length; i8++)
            {
                int midi = Melody[i8];
                if (midi <= 0) continue;
                float t = i8 * eighth + ((i8 & 1) == 1 ? Swing * beat : 0f);
                float f = 440f * MathF.Pow(2f, (midi - 69) / 12f);
                AddMarimba(mel, N(t), f, 0.2f);
            }

            // mix + gentle tone shaping
            var mix = new float[n + tail];
            var ukeLp = FXBiquad.LowPass(5200f, 0.7f);
            var ukeHp = FXBiquad.HighPass(120f, 0.7f);
            var percHp = FXBiquad.HighPass(4000f, 0.7f);
            for (int i = 0; i < mix.Length; i++)
                mix[i] = ukeLp.Process(ukeHp.Process(uke[i])) + low[i] * 0.9f + percHp.Process(perc[i]) + mel[i] * 0.8f;

            // small room (3 combs + allpass), mostly dry
            Reverb(mix, 0.16f);

            // wrap the tail into the start: what rings over the loop end is heard at the start of the repeat
            var o = new float[n];
            Array.Copy(mix, o, n);
            for (int i = 0; i < tail && i < n; i++) o[i] += mix[n + i];
            return NormalizePeak(o, 0.8f);
        }

        /// Karplus-Strong nylon string pluck rendered from start for len samples (short fade-out at the end).
        static void Pluck(float[] buf, int start, float freq, float amp, float bright, int len, uint seed)
        {
            int period = Math.Max(2, (int)MathF.Round(SR / freq - 0.5f));
            var line = new float[period];
            var rng = new FXRng(seed * 2654435761u + 1u);
            float lp = 0f, mean = 0f;
            float a = bright < 0.05f ? 0.05f : bright > 0.95f ? 0.95f : bright;
            for (int i = 0; i < period; i++)
            {
                lp += a * (rng.Signed() - lp);
                line[i] = lp;
                mean += lp;
            }
            mean /= period;
            for (int i = 0; i < period; i++) line[i] -= mean;
            const float T60 = 1.25f;   // nylon: short, warm sustain
            float g = MathF.Pow(10f, -3f / (T60 * freq));
            len = Math.Min(len, buf.Length - start);
            int fade = Math.Min(len, N(0.018f));
            int idx = 0;
            for (int i = 0; i < len; i++)
            {
                float cur = line[idx];
                int nx = idx + 1 == period ? 0 : idx + 1;
                line[idx] = g * 0.5f * (cur + line[nx]);
                idx = nx;
                float env = i < len - fade ? 1f : (len - i) / (float)fade;
                float att = i < 40 ? i / 40f : 1f;
                buf[start + i] += cur * amp * env * att;
            }
        }

        static void AddBass(float[] a, int start, float f, float amp, int len)
        {
            len = Math.Min(len, a.Length - start);
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)SR;
                float env = (1f - MathF.Exp(-t / 0.006f)) * MathF.Exp(-t / 0.55f);
                float rel = i > len - N(0.03f) ? (len - i) / (float)N(0.03f) : 1f;
                float ph = TwoPi * f * t;
                a[start + i] += amp * env * rel * (MathF.Sin(ph) + 0.25f * MathF.Sin(2f * ph) + 0.06f * MathF.Sin(3f * ph));
            }
        }

        static void AddShaker(float[] a, int start, float amp, ref FXRng rng)
        {
            int len = Math.Min(a.Length - start, N(0.07f));
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)SR;
                float env = (1f - MathF.Exp(-t / 0.004f)) * MathF.Exp(-t / 0.022f);
                a[start + i] += amp * env * rng.Signed();
            }
        }

        static void AddMarimba(float[] a, int start, float f, float amp)
        {
            int len = Math.Min(a.Length - start, N(0.9f));
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)SR;
                float att = 1f - MathF.Exp(-t / 0.0015f);
                float s = MathF.Sin(TwoPi * f * t) * MathF.Exp(-t / 0.32f)
                        + 0.35f * MathF.Sin(TwoPi * 3.93f * f * t) * MathF.Exp(-t / 0.045f)
                        + 0.12f * MathF.Sin(TwoPi * 9.9f * f * t) * MathF.Exp(-t / 0.012f);
                a[start + i] += amp * att * s;
            }
        }

        static void Reverb(float[] a, float wet)
        {
            int[] delays = { N(0.0297f), N(0.0371f), N(0.0411f) };
            const float Fb = 0.74f;
            var lines = new float[3][];
            var pos = new int[3];
            for (int k = 0; k < 3; k++) lines[k] = new float[delays[k]];
            int apLen = N(0.005f);
            var ap = new float[apLen];
            int apPos = 0;
            var damp = new float[3];
            for (int i = 0; i < a.Length; i++)
            {
                float x = a[i];
                float sum = 0f;
                for (int k = 0; k < 3; k++)
                {
                    float y = lines[k][pos[k]];
                    damp[k] += 0.35f * (y - damp[k]);   // a little high-frequency damping in the feedback
                    lines[k][pos[k]] = x + damp[k] * Fb;
                    pos[k] = pos[k] + 1 == delays[k] ? 0 : pos[k] + 1;
                    sum += y;
                }
                sum *= 0.33f;
                float d = ap[apPos];
                float v = sum + d * 0.5f;
                ap[apPos] = v;
                apPos = apPos + 1 == apLen ? 0 : apPos + 1;
                float rev = d - v * 0.5f;
                a[i] = x + rev * wet;
            }
        }
    }
}
