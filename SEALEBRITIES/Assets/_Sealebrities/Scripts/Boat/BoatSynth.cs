using UnityEngine;

namespace Sealebrities
{
    /// Procedural audio clips of the Boat module (no imported assets). Loops are exactly 1 s long and contain
    /// only integer frequencies so they repeat seamlessly.
    public static class BoatSynth
    {
        const int Rate = 22050;
        static AudioClip engine, rotor, splash, thunk, plop, reel, backfire;
        static AudioClip snap, drag, sparkle, bleat, chug, hiss, ratchet;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            engine = rotor = splash = thunk = plop = reel = backfire = null;
            snap = drag = sparkle = bleat = chug = hiss = ratchet = null;
        }

        public static AudioClip Engine { get { if (engine == null) engine = MakeEngine(); return engine; } }
        public static AudioClip Rotor { get { if (rotor == null) rotor = MakeRotor(); return rotor; } }
        public static AudioClip Splash { get { if (splash == null) splash = MakeSplash(); return splash; } }
        public static AudioClip Thunk { get { if (thunk == null) thunk = MakeThunk(); return thunk; } }
        public static AudioClip Plop { get { if (plop == null) plop = MakePlop(); return plop; } }
        public static AudioClip Reel { get { if (reel == null) reel = MakeReel(); return reel; } }
        public static AudioClip Backfire { get { if (backfire == null) backfire = MakeBackfire(); return backfire; } }
        /// D.1: the line snaps – a dry twang with a falling pitch.
        public static AudioClip Snap { get { if (snap == null) snap = MakeSnap(); return snap; } }
        /// D.1: the reel's drag buzzing while the fish takes line (1 s seamless loop).
        public static AudioClip Drag { get { if (drag == null) drag = MakeDrag(); return drag; } }
        /// D.3: the research rod's "rare bite" ping (bright two-note chime).
        public static AudioClip Sparkle { get { if (sparkle == null) sparkle = MakeSparkle(); return sparkle; } }
        /// L.12: Kapitän Meck on the crate – „Määäh“.
        public static AudioClip Bleat { get { if (bleat == null) bleat = MakeBleat(); return bleat; } }
        /// E.4: the boat compressor chugging (1 s seamless loop).
        public static AudioClip Chug { get { if (chug == null) chug = MakeChug(); return chug; } }
        /// L.12: the smoke beacon ignites.
        public static AudioClip Hiss { get { if (hiss == null) hiss = MakeHiss(); return hiss; } }
        /// K.3: the boat winch cranking (1 s seamless loop).
        public static AudioClip Ratchet { get { if (ratchet == null) ratchet = MakeRatchet(); return ratchet; } }

        const float TwoPi = Mathf.PI * 2f;

        static AudioClip Make(string name, float[] d, float peak)
        {
            float mean = 0f;
            for (int i = 0; i < d.Length; i++) mean += d[i];
            mean /= d.Length;
            float max = 1e-5f;
            for (int i = 0; i < d.Length; i++) { d[i] -= mean; max = Mathf.Max(max, Mathf.Abs(d[i])); }
            float k = peak / max;
            for (int i = 0; i < d.Length; i++) d[i] *= k;
            var clip = AudioClip.Create(name, d.Length, 1, Rate, false);
            clip.SetData(d, 0);
            return clip;
        }

        /// Old two-stroke outboard: 42 Hz combustion pulses, harmonics, rumble and a periodic weak cylinder.
        static AudioClip MakeEngine()
        {
            var rng = new System.Random(11);
            var d = new float[Rate];
            float lp = 0f, lp2 = 0f;
            const float f = 42f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float phase = t * f;
                int cyc = (int)phase;
                float frac = phase - cyc;
                float misfire = (cyc % 7 == 3) ? 0.35f : 1f;
                float pulse = Mathf.Exp(-frac * 9f) * misfire;
                float harm = 0.5f * Mathf.Sin(TwoPi * f * t) + 0.3f * Mathf.Sin(TwoPi * 2f * f * t + 0.5f)
                           + 0.15f * Mathf.Sin(TwoPi * 3f * f * t + 1.3f) + 0.08f * Mathf.Sin(TwoPi * 5f * f * t + 0.2f);
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += (noise - lp) * 0.08f;
                lp2 += (noise - lp2) * 0.3f;
                d[i] = pulse * 0.8f + harm * 0.35f + lp * 0.9f + lp2 * pulse * 0.25f;
            }
            return Make("BoatEngine", d, 0.8f);
        }

        /// Helicopter: 13 Hz blade slap, low thump, turbine whine and hiss.
        static AudioClip MakeRotor()
        {
            var rng = new System.Random(21);
            var d = new float[Rate];
            float lp1 = 0f, lp2 = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float bp = t * 13f;
                float fr = bp - Mathf.Floor(bp);
                float env = Mathf.Exp(-fr * 7f);
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp1 += (noise - lp1) * 0.25f;
                lp2 += (noise - lp2) * 0.05f;
                float slap = lp1 * env * 1.1f;
                float thump = Mathf.Sin(TwoPi * 55f * t) * env * 0.6f;
                float whine = Mathf.Sin(TwoPi * 1100f * t) * 0.035f + Mathf.Sin(TwoPi * 2200f * t) * 0.012f;
                d[i] = slap + thump + whine + lp2 * 0.5f;
            }
            return Make("HeliRotor", d, 0.85f);
        }

        static AudioClip MakeSplash()
        {
            var rng = new System.Random(31);
            int n = (int)(Rate * 0.7f);
            var d = new float[n];
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float env = Mathf.Min(1f, t / 0.008f) * Mathf.Exp(-t * 6f);
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                float cut = Mathf.Lerp(0.6f, 0.05f, Mathf.Clamp01(t / 0.5f));
                lp += (noise - lp) * cut;
                float bloop = Mathf.Sin(TwoPi * (300f + 900f * t) * t) * Mathf.Exp(-t * 14f) * 0.4f;
                d[i] = lp * env + bloop;
            }
            return Make("BoatSplash", d, 0.7f);
        }

        static AudioClip MakeThunk()
        {
            var rng = new System.Random(41);
            int n = (int)(Rate * 0.4f);
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                d[i] = Mathf.Sin(TwoPi * 85f * t) * Mathf.Exp(-t * 14f)
                     + Mathf.Sin(TwoPi * 340f * t) * Mathf.Exp(-t * 30f) * 0.35f
                     + noise * Mathf.Exp(-t * 60f) * 0.5f;
            }
            return Make("CrateThunk", d, 0.8f);
        }

        static AudioClip MakePlop()
        {
            var rng = new System.Random(51);
            int n = (int)(Rate * 0.3f);
            var d = new float[n];
            float ph = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float freq = Mathf.Lerp(650f, 220f, Mathf.Clamp01(t / 0.12f));
                ph += TwoPi * freq / Rate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                d[i] = Mathf.Sin(ph) * Mathf.Exp(-t * 22f) + noise * Mathf.Exp(-t * 40f) * 0.25f;
            }
            return Make("BobberPlop", d, 0.6f);
        }

        static AudioClip MakeReel()
        {
            var rng = new System.Random(61);
            int n = (int)(Rate * 0.7f);
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float clickRate = Mathf.Lerp(45f, 25f, t / 0.7f);
                float ph = t * clickRate;
                float fr = ph - Mathf.Floor(ph);
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                d[i] = noise * Mathf.Exp(-fr * 40f) * (1f - t / 0.7f);
            }
            return Make("FishingReel", d, 0.5f);
        }

        static AudioClip MakeBackfire()
        {
            var rng = new System.Random(71);
            int n = (int)(Rate * 0.5f);
            var d = new float[n];
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += (noise - lp) * 0.2f;
                d[i] = lp * Mathf.Exp(-t * 12f) * 1.5f + Mathf.Sin(TwoPi * 60f * t) * Mathf.Exp(-t * 18f);
            }
            return Make("BoatBackfire", d, 0.9f);
        }

        static AudioClip MakeSnap()
        {
            var rng = new System.Random(81);
            int n = (int)(Rate * 0.55f);
            var d = new float[n];
            float ph = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float f = Mathf.Lerp(420f, 140f, Mathf.Clamp01(t / 0.25f));
                ph += TwoPi * f / Rate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                float twang = (Mathf.Sin(ph) + 0.4f * Mathf.Sin(ph * 2.01f)) * Mathf.Exp(-t * 9f);
                d[i] = twang + noise * Mathf.Exp(-t * 90f) * 1.2f;
            }
            return Make("LineSnap", d, 0.85f);
        }

        static AudioClip MakeDrag()
        {
            var rng = new System.Random(91);
            var d = new float[Rate];
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float ph = t * 38f;   // 38 clicks per second, integer → seamless loop
                float fr = ph - Mathf.Floor(ph);
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                d[i] = noise * Mathf.Exp(-fr * 28f) + Mathf.Sin(TwoPi * 1140f * t) * Mathf.Exp(-fr * 18f) * 0.35f;
            }
            return Make("ReelDrag", d, 0.55f);
        }

        static AudioClip MakeSparkle()
        {
            int n = (int)(Rate * 0.9f);
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float a = Mathf.Sin(TwoPi * 1318.5f * t) + 0.5f * Mathf.Sin(TwoPi * 2637f * t);
                float b = t > 0.11f ? (Mathf.Sin(TwoPi * 1975.5f * (t - 0.11f)) + 0.4f * Mathf.Sin(TwoPi * 3951f * (t - 0.11f))) * Mathf.Exp(-(t - 0.11f) * 5f) : 0f;
                d[i] = a * Mathf.Exp(-t * 7f) * 0.8f + b;
            }
            return Make("RareBite", d, 0.6f);
        }

        static AudioClip MakeBleat()
        {
            int n = (int)(Rate * 0.95f);
            var d = new float[n];
            float ph = 0f, bp = 0f, bp2 = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float vib = 1f + 0.06f * Mathf.Sin(TwoPi * 7.5f * t);
                float f = Mathf.Lerp(470f, 390f, t / 0.95f) * vib;
                ph += f / Rate;
                float saw = 2f * (ph - Mathf.Floor(ph)) - 1f;
                // two crude formants ("ä") with one-pole smoothing
                bp += (saw - bp) * 0.35f;
                bp2 += (bp - bp2) * 0.6f;
                float trem = 0.75f + 0.25f * Mathf.Sin(TwoPi * 7.5f * t + 1f);
                float env = Mathf.Min(1f, t / 0.05f) * Mathf.Clamp01((0.95f - t) / 0.25f);
                d[i] = (bp2 * 0.8f + saw * 0.2f) * trem * env;
            }
            return Make("GoatBleat", d, 0.7f);
        }

        static AudioClip MakeChug()
        {
            var rng = new System.Random(101);
            var d = new float[Rate];
            float lp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float ph = t * 9f;   // piston strokes
                float fr = ph - Mathf.Floor(ph);
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += (noise - lp) * 0.12f;
                d[i] = Mathf.Sin(TwoPi * 60f * t) * Mathf.Exp(-fr * 7f) * 0.9f + Mathf.Sin(TwoPi * 120f * t) * 0.12f + lp * (0.25f + 0.6f * Mathf.Exp(-fr * 12f));
            }
            return Make("Compressor", d, 0.6f);
        }

        static AudioClip MakeHiss()
        {
            var rng = new System.Random(111);
            int n = (int)(Rate * 1.3f);
            var d = new float[n];
            float hp = 0f, prev = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                hp = 0.92f * (hp + noise - prev);
                prev = noise;
                float env = Mathf.Min(1f, t / 0.03f) * Mathf.Exp(-t * 1.6f);
                float pop = t < 0.06f ? Mathf.Sin(TwoPi * 90f * t) * (1f - t / 0.06f) : 0f;
                d[i] = hp * env * 0.7f + pop;
            }
            return Make("FlareHiss", d, 0.6f);
        }

        static AudioClip MakeRatchet()
        {
            var rng = new System.Random(121);
            var d = new float[Rate];
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float ph = t * 14f;
                float fr = ph - Mathf.Floor(ph);
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                d[i] = noise * Mathf.Exp(-fr * 45f) + Mathf.Sin(TwoPi * 520f * t) * Mathf.Exp(-fr * 30f) * 0.5f + Mathf.Sin(TwoPi * 70f * t) * 0.08f;
            }
            return Make("WinchRatchet", d, 0.5f);
        }

        /// 3D one-shot at a world position (temporary AudioSource object). Scaled by the SFX volume setting.
        public static void PlayAt(AudioClip clip, Vector3 pos, float volume, float pitch = 1f, float maxDistance = 60f)
        {
            if (clip == null) return;
            volume *= Settings.SfxVolume;
            if (volume <= 0.001f) return;
            var go = new GameObject("BoatOneShot");
            go.transform.position = pos;
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.volume = volume;
            src.pitch = pitch;
            src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 2f;
            src.maxDistance = maxDistance;
            src.dopplerLevel = 0f;
            src.Play();
            Object.Destroy(go, clip.length / Mathf.Max(0.1f, pitch) + 0.1f);
        }

        public static AudioSource LoopSource(GameObject host, AudioClip clip, float minDist, float maxDist, AudioRolloffMode mode)
        {
            var src = host.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.playOnAwake = false;
            src.spatialBlend = 1f;
            src.rolloffMode = mode;
            src.minDistance = minDist;
            src.maxDistance = maxDist;
            src.dopplerLevel = 0.4f;
            src.volume = 0f;
            return src;
        }
    }
}
