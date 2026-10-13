using UnityEngine;

namespace Sealebrities
{
    /// G.711 µ-law: 16-bit PCM ↔ 8-bit (2:1, telephone quality, ~zero CPU). 16 kHz mono → 16 kB/s per speaker.
    public static class NetVoiceCodec
    {
        public const int SampleRate = 16000;
        public const int FrameSamples = 640;   // 40 ms

        const int Bias = 0x84;
        const int Clip = 32635;

        static readonly byte[] ExpLut = BuildExpLut();
        static readonly float[] DecodeLut = BuildDecodeLut();

        static byte[] BuildExpLut()
        {
            var t = new byte[256];
            for (int i = 0; i < 256; i++)
            {
                int e = 0;
                for (int v = i; v > 1; v >>= 1) e++;
                t[i] = (byte)Mathf.Min(7, e);
            }
            t[0] = 0;
            t[1] = 0;
            return t;
        }

        static float[] BuildDecodeLut()
        {
            var t = new float[256];
            for (int i = 0; i < 256; i++)
            {
                int u = ~i & 0xFF;
                int sign = u & 0x80;
                int exponent = (u >> 4) & 0x07;
                int mantissa = u & 0x0F;
                int sample = (((mantissa << 3) + Bias) << exponent) - Bias;
                t[i] = (sign != 0 ? -sample : sample) / 32768f;
            }
            return t;
        }

        public static byte Encode(float s)
        {
            int pcm = Mathf.RoundToInt(Mathf.Clamp(s, -1f, 1f) * 32767f);
            int sign = 0;
            if (pcm < 0) { sign = 0x80; pcm = -pcm; }
            if (pcm > Clip) pcm = Clip;
            pcm += Bias;
            int exponent = ExpLut[(pcm >> 7) & 0xFF];
            int mantissa = (pcm >> (exponent + 3)) & 0x0F;
            return (byte)~(sign | (exponent << 4) | mantissa);
        }

        public static float Decode(byte b) => DecodeLut[b];

        public static void EncodeFrame(float[] src, byte[] dst, int count)
        {
            for (int i = 0; i < count; i++) dst[i] = Encode(src[i]);
        }

        public static void DecodeFrame(byte[] src, float[] dst, int count)
        {
            for (int i = 0; i < count; i++) dst[i] = DecodeLut[src[i]];
        }
    }
}
