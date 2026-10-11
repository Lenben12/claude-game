using UnityEngine;

namespace Sealebrities
{
    /// Plays one remote speaker: 16 kHz samples go into a ring buffer (main thread) and are resampled to the output
    /// rate in OnAudioFilterRead (audio thread) after a ~120 ms jitter buffer has filled. The AudioSource loops a
    /// constant "1.0" clip that the filter multiplies with the voice, so Unity's 3D panning and linear 2–30 m rolloff
    /// apply no matter whether the spatializer runs before or after custom filters. An AudioLowPassFilter after it
    /// muffles the voice (~900 Hz) while the speaker or the listener is under water.
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
        int lastSeq = -1;
        float lastEnqueue = -10f;

        public int PlayerId { get; private set; }

        public static NetVoicePlayer Create(Transform parent, int playerId, bool spatial)
        {
            var go = new GameObject("Voice_" + playerId);
            go.transform.SetParent(parent, false);
            var src = go.AddComponent<AudioSource>();
            var p = go.AddComponent<NetVoicePlayer>();      // custom filter first …
            p.lowPass = go.AddComponent<AudioLowPassFilter>();   // … then the muffle, so it filters our output
            p.lowPass.cutoffFrequency = 900f;
            p.lowPass.lowpassResonanceQ = 1f;
            p.lowPass.enabled = false;
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

        /// Main thread: position / volume / muffle.
        public void UpdateOutput(Vector3 position, bool spatial, bool muffled)
        {
            transform.position = position;
            float sb = spatial ? 1f : 0f;
            if (src.spatialBlend != sb) src.spatialBlend = sb;
            // silent while nobody talks (the carrier clip never reaches the speakers, even if the filter were bypassed)
            float v = Time.unscaledTime - lastEnqueue < 1.5f ? Settings.VoiceVolume : 0f;
            if (!Mathf.Approximately(src.volume, v)) src.volume = v;
            if (lowPass.enabled != muffled) lowPass.enabled = muffled;
            if (!src.isPlaying) src.Play();
        }

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
