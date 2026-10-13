using UnityEngine;

namespace Sealebrities
{
    /// Plays one remote speaker: 16 kHz samples go into a ring buffer (main thread) and are resampled to the output
    /// rate in OnAudioFilterRead (audio thread) after a ~120 ms jitter buffer has filled. The AudioSource loops a
    /// constant "1.0" clip that the filter multiplies with the voice, so Unity's 3D panning and linear rolloff apply no
    /// matter whether the spatializer runs before or after custom filters. The link (F.5, NetCoopRules) sets range and
    /// colour: Air 2–30 m clean; Water 2–10 m muffled (AudioLowPassFilter ~900 Hz); Radio up to 200 m, non-spatial,
    /// band-pass 400–3,000 Hz (low-pass + AudioHighPassFilter) with a light hiss and the odd crackle pop.
    [RequireComponent(typeof(AudioSource))]
    public sealed class NetVoicePlayer : MonoBehaviour
    {
        const int RingSize = 16384;                                       // ~1 s at 16 kHz
        // jitter buffer: 120 ms on a clean link, growing to 300 ms with the measured arrival jitter (TCP retransmits on
        // a lossy internet link) instead of stuttering; the latency cap keeps >= 150 ms above it
        const int MinStart = NetVoiceCodec.SampleRate * 120 / 1000;
        const int MaxStart = NetVoiceCodec.SampleRate * 300 / 1000;
        const int MaxBufferedBase = NetVoiceCodec.SampleRate * 450 / 1000;  // drop the oldest beyond 450 ms (latency cap)
        const int CapMargin = NetVoiceCodec.SampleRate * 150 / 1000;
        volatile int startThreshold = MinStart;
        float arrivalJitter, lastArrival = -1f;
        int StartThreshold => startThreshold;
        int MaxBuffered => Mathf.Max(MaxBufferedBase, startThreshold + CapMargin);

        static AudioClip dcClip;
        static long samplesPlayed;

        /// 16 kHz voice samples all remote speakers have played so far (written on the audio thread; tests and diagnostics).
        public static long SamplesPlayed => System.Threading.Interlocked.Read(ref samplesPlayed);

        readonly float[] ring = new float[RingSize];
        readonly object sync = new object();
        int readPos, writePos, count;
        double frac;
        bool playing;
        double step = 16000.0 / 48000.0;

        AudioSource src;
        AudioLowPassFilter lowPass;
        AudioHighPassFilter highPass;
        int lastSeq = -1;
        float lastEnqueue = -10f;
        VoiceLink link = VoiceLink.Air;
        // radio crackle (audio thread)
        volatile bool crackle;
        uint noiseState = 0x9E3779B9u;
        float pop;

        public int PlayerId { get; private set; }
        /// The link this speaker is heard with right now (F.5).
        public VoiceLink Link => link;

        public static NetVoicePlayer Create(Transform parent, int playerId, bool spatial)
        {
            var go = new GameObject("Voice_" + playerId);
            go.transform.SetParent(parent, false);
            var src = go.AddComponent<AudioSource>();
            var p = go.AddComponent<NetVoicePlayer>();      // custom filter first …
            p.lowPass = go.AddComponent<AudioLowPassFilter>();   // … then the muffle / radio band-pass, so they filter our output
            p.lowPass.cutoffFrequency = NetCoopRules.WaterMuffleHz;
            p.lowPass.lowpassResonanceQ = 1f;
            p.lowPass.enabled = false;
            p.highPass = go.AddComponent<AudioHighPassFilter>();
            p.highPass.cutoffFrequency = NetCoopRules.RadioHighPassHz;
            p.highPass.highpassResonanceQ = 1f;
            p.highPass.enabled = false;
            p.noiseState ^= (uint)(playerId * 7919 + 1);
            p.Init(src, playerId, spatial);
            return p;
        }

        void Init(AudioSource s, int playerId, bool spatial)
        {
            PlayerId = playerId;
            src = s;
            int rate = AudioSettings.outputSampleRate;
            if (rate <= 0) rate = 48000;
            step = NetVoiceCodec.SampleRate / (double)rate;
            src.clip = DcClip(rate);
            src.loop = true;
            src.playOnAwake = false;
            src.spatialBlend = spatial ? 1f : 0f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 2f;
            src.maxDistance = 30f;
            src.dopplerLevel = 0f;
            src.spread = 0f;
            src.priority = 40;
            src.volume = 0f;
            src.Play();
        }

        static AudioClip DcClip(int rate)
        {
            if (dcClip != null) return dcClip;
            int n = Mathf.Max(1024, rate / 10);
            dcClip = AudioClip.Create("SB_NetVoiceDC", n, 1, rate, false);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = 1f;
            dcClip.SetData(data, 0);
            return dcClip;
        }

        /// Main thread: queue one decoded frame (seq = sender frame counter, wraps at 65536).
        public void Enqueue(float[] samples, int n, int seq)
        {
            if (lastSeq >= 0)
            {
                int diff = (seq - lastSeq) & 0xFFFF;
                if (diff == 0 || diff > 0x8000) return;   // duplicate or late (older) frame
            }
            lastSeq = seq;
            float now = Time.unscaledTime;
            lastEnqueue = now;
            // peak-hold arrival jitter (|inter-arrival − frame length|, decaying); pauses between talk spurts ignored
            if (lastArrival > 0f && n > 0)
            {
                float ia = now - lastArrival;
                if (ia < 0.5f)
                {
                    float dev = Mathf.Abs(ia - n / (float)NetVoiceCodec.SampleRate);
                    arrivalJitter = Mathf.Max(dev, arrivalJitter * 0.995f);
                    startThreshold = Mathf.Clamp(Mathf.RoundToInt(2f * arrivalJitter * NetVoiceCodec.SampleRate), MinStart, MaxStart);
                }
            }
            lastArrival = now;
            lock (sync)
            {
                for (int i = 0; i < n; i++)
                {
                    ring[writePos] = samples[i];
                    writePos = (writePos + 1) & (RingSize - 1);
                    if (count < RingSize) count++;
                    else readPos = (readPos + 1) & (RingSize - 1);
                }
                if (count > MaxBuffered)
                {
                    int drop = count - StartThreshold;
                    readPos = (readPos + drop) & (RingSize - 1);
                    count -= drop;
                }
            }
        }

        /// Main thread: position / volume / range / colour for the current link. distance = speaker ↔ listener (m) when
        /// the speaker's avatar is visible (spatial), else 0.
        public void UpdateOutput(Vector3 position, bool spatial, VoiceLink newLink, float distance)
        {
            link = newLink;
            transform.position = position;
            bool radio = newLink == VoiceLink.Radio;
            // the radio sits in your ear (non-spatial); air and water voices come from the diver's head
            float sb = spatial && !radio ? 1f : 0f;
            if (src.spatialBlend != sb) src.spatialBlend = sb;
            float maxD = NetCoopRules.MaxDistance(newLink), minD = NetCoopRules.MinDistance(newLink);
            if (!radio)
            {
                if (src.maxDistance != maxD) src.maxDistance = maxD;
                if (src.minDistance != minD) src.minDistance = minD;
            }
            // silent while nobody talks (the carrier clip never reaches the speakers, even if the filter were bypassed)
            float v = Time.unscaledTime - lastEnqueue < 1.5f ? Settings.VoiceVolume : 0f;
            // radio: full volume up to 180 m, fading out to 200 m (a non-spatial source has no rolloff of its own)
            if (radio && spatial) v *= 1f - Mathf.Clamp01((distance - (maxD - 20f)) / 20f);
            if (!Mathf.Approximately(src.volume, v)) src.volume = v;
            bool lp = newLink != VoiceLink.Air;
            if (lowPass.enabled != lp) lowPass.enabled = lp;
            float cutoff = radio ? NetCoopRules.RadioLowPassHz : NetCoopRules.WaterMuffleHz;
            if (lp && lowPass.cutoffFrequency != cutoff) lowPass.cutoffFrequency = cutoff;
            if (highPass.enabled != radio) highPass.enabled = radio;
            crackle = radio;
            if (!src.isPlaying) src.Play();
        }

        /// Older call (bool muffled) – kept for callers outside the voice module.
        public void UpdateOutput(Vector3 position, bool spatial, bool muffled) =>
            UpdateOutput(position, spatial, muffled ? VoiceLink.Water : VoiceLink.Air, 0f);

        public void ResetStream()
        {
            lock (sync)
            {
                readPos = writePos = count = 0;
                frac = 0.0;
                playing = false;
            }
            lastSeq = -1;
            lastArrival = -1f;
            arrivalJitter = 0f;
            startThreshold = MinStart;
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            lock (sync)
            {
                int frames = data.Length / channels;
                if (!playing)
                {
                    if (count >= StartThreshold) playing = true;
                    else
                    {
                        System.Array.Clear(data, 0, data.Length);
                        return;
                    }
                }
                int consumed = 0;
                bool hiss = crackle;
                for (int i = 0; i < frames; i++)
                {
                    float s = 0f;
                    if (count >= 2)
                    {
                        float a = ring[readPos];
                        float b = ring[(readPos + 1) & (RingSize - 1)];
                        s = a + (b - a) * (float)frac;
                        frac += step;
                        while (frac >= 1.0 && count >= 2)
                        {
                            frac -= 1.0;
                            readPos = (readPos + 1) & (RingSize - 1);
                            count--;
                            consumed++;
                        }
                        if (hiss)
                        {
                            // underwater radio: soft hiss + a rare crackle pop (xorshift, no allocations)
                            noiseState ^= noiseState << 13;
                            noiseState ^= noiseState >> 17;
                            noiseState ^= noiseState << 5;
                            float n = (noiseState & 0xFFFF) / 32767.5f - 1f;
                            if ((noiseState >> 17) == 0) pop = 0.22f * (n >= 0f ? 1f : -1f);   // ~1.5 pops/s at 48 kHz
                            s = s * 0.94f + n * 0.012f + pop;
                            pop *= 0.92f;
                        }
                    }
                    else
                    {
                        playing = false;   // underrun: wait for the jitter buffer to refill
                    }
                    int o = i * channels;
                    for (int c = 0; c < channels; c++) data[o + c] *= s;
                }
                if (consumed > 0) System.Threading.Interlocked.Add(ref samplesPlayed, consumed);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { dcClip = null; samplesPlayed = 0; }
    }
}
