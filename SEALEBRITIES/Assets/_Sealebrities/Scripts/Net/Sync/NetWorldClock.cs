using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Maps a remote sender's timestamps onto the local clock. Every stream message carries the sender's
    /// Time.unscaledTime; the offset keeps the fastest delivery seen (minimum filter that slowly relaxes upward),
    /// so RenderTime(delay) lags the sender by `delay` plus the base latency, and jitter is absorbed by the delay.
    public sealed class SyncTimeline
    {
        float offset;      // local - remote
        float jitter;      // peak-hold (slowly decaying) lateness of samples over the fastest route
        bool has;

        public bool Valid => has;

        public void Observe(float remoteTime)
        {
            float o = Time.unscaledTime - remoteTime;
            if (!has || o < offset - 0.0001f || o > offset + 1.5f)
            {
                // first sample, a faster delivery, or the sender restarted its clock (new session / reconnect)
                if (!has || o > offset + 1.5f) jitter = 0f;
                offset = o;
                has = true;
                jitter *= 0.995f;
                return;
            }
            jitter = Mathf.Max(o - offset, jitter * 0.995f);
            offset += (o - offset) * 0.01f;   // creep toward a slower route instead of jumping
        }

        /// Interpolation delay that adapts to this link: the measured jitter + 50 ms, within [min, max]. On a LAN it
        /// stays at min; a lossy internet link (TCP retransmits) gets a longer buffer instead of snapping.
        public float Delay(float min, float max) => Mathf.Clamp(jitter + 0.05f, min, max);

        /// The sender's clock now (as far as we can tell).
        public float RemoteNow => Time.unscaledTime - offset;

        /// The sender time to display now: `delay` seconds behind the newest possible sample.
        public float RenderTime(float delay) => Time.unscaledTime - offset - delay;

        public void Reset() { has = false; offset = 0f; jitter = 0f; }
    }

    /// One timeline per sending player id (shared by that player's avatar, boat and creature streams, so things
    /// streamed by the same machine stay perfectly in step with each other).
    public static class SyncTimelines
    {
        static readonly Dictionary<int, SyncTimeline> map = new Dictionary<int, SyncTimeline>();

        /// Local send timestamp for outgoing streams.
        public static float Now => Time.unscaledTime;

        public static SyncTimeline Get(int playerId)
        {
            if (!map.TryGetValue(playerId, out var t))
            {
                t = new SyncTimeline();
                map[playerId] = t;
            }
            return t;
        }

        public static void Forget(int playerId) => map.Remove(playerId);
        public static void Clear() => map.Clear();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { map.Clear(); }
    }

    /// Small ring buffer of timestamped snapshots (newest last) with bracketing for interpolation.
    /// Out-of-order or duplicate samples (timestamp not newer than the newest) are dropped.
    public sealed class SyncSnapBuffer<T> where T : struct
    {
        readonly T[] items;
        readonly float[] times;
        int start, count;

        public SyncSnapBuffer(int capacity = 8)
        {
            items = new T[Mathf.Max(2, capacity)];
            times = new float[items.Length];
        }

        public int Count => count;

        public void Clear() { start = 0; count = 0; }

        int Index(int i) => (start + i) % items.Length;

        public float NewestTime => count > 0 ? times[Index(count - 1)] : float.NegativeInfinity;
        public float OldestTime => count > 0 ? times[Index(0)] : float.NegativeInfinity;
        public ref T Newest => ref items[Index(count - 1)];
        public ref T Oldest => ref items[Index(0)];
        public ref T At(int i) => ref items[Index(i)];
        public float TimeAt(int i) => times[Index(i)];

        /// Adds a sample; false when it was older than (or as old as) the newest one.
        public bool Add(float time, in T item)
        {
            if (count > 0 && time <= NewestTime) return false;
            if (count == items.Length)
            {
                start = (start + 1) % items.Length;
                count--;
            }
            int i = Index(count);
            items[i] = item;
            times[i] = time;
            count++;
            return true;
        }

        /// Finds the samples around time t: a = older, b = newer, f = blend 0..1. When t is past the newest sample,
        /// a == b == newest and `over` holds how far past it is (for extrapolation); before the oldest, a == b == oldest.
        public bool Bracket(float t, out int a, out int b, out float f, out float over)
        {
            a = b = 0;
            f = 0f;
            over = 0f;
            if (count == 0) return false;
            if (t <= times[Index(0)]) return true;
            int last = count - 1;
            float tn = times[Index(last)];
            if (t >= tn)
            {
                a = b = last;
                over = t - tn;
                return true;
            }
            for (int i = last - 1; i >= 0; i--)
            {
                float ti = times[Index(i)];
                if (ti <= t)
                {
                    a = i;
                    b = i + 1;
                    float tb = times[Index(b)];
                    f = tb > ti ? (t - ti) / (tb - ti) : 1f;
                    return true;
                }
            }
            return true;
        }
    }
}
