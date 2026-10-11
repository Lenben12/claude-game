using UnityEngine;

namespace Sealebrities
{
    /// Small procedural sound clips of the props (built once, no assets): the radio buoy's sonar beep, the crackling
    /// radio of the tower ruins, the mannequin's squeaky honk, a heavy door slam in the wreck.
    public static class PropSound
    {
        const int Rate = 22050;
        static AudioClip beep, crackle, honk, slam, hum;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { beep = null; crackle = null; honk = null; slam = null; hum = null; }

        public static AudioClip Beep => beep != null ? beep : (beep = Make("PropBeep", 0.9f, (t, r) =>
        {
            // two short sonar pings per clip (the loop repeats every 0.9 s)
            float a = t < 0.12f ? Mathf.Sin(t / 0.12f * Mathf.PI) : 0f;
            float b = t > 0.2f && t < 0.3f ? Mathf.Sin((t - 0.2f) / 0.1f * Mathf.PI) * 0.6f : 0f;
            return (a + b) * Mathf.Sin(2f * Mathf.PI * 1250f * t) * 0.5f;
        }));

        public static AudioClip Crackle => crackle != null ? crackle : (crackle = Make("PropCrackle", 2.4f, (t, r) =>
        {
            float n = (float)(r.NextDouble() * 2.0 - 1.0);
            float pop = r.NextDouble() < 0.004 ? 1f : 0f;
            float voice = Mathf.Sin(2f * Mathf.PI * 180f * t + 3f * Mathf.Sin(2f * Mathf.PI * 3f * t)) * (0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 0.7f * t));
            return n * 0.18f + pop * 0.6f + voice * 0.12f;
        }));

        public static AudioClip Honk => honk != null ? honk : (honk = Make("PropHonk", 0.45f, (t, r) =>
        {
            float env = Mathf.Clamp01(t / 0.03f) * Mathf.Clamp01((0.45f - t) / 0.12f);
            float f = 420f + 160f * Mathf.Sin(t * 30f);
            float s = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * f * t));
            return s * env * 0.28f;
        }));

        public static AudioClip Slam => slam != null ? slam : (slam = Make("PropSlam", 0.7f, (t, r) =>
        {
            float n = (float)(r.NextDouble() * 2.0 - 1.0);
            return (n * 0.6f + Mathf.Sin(2f * Mathf.PI * 55f * t)) * Mathf.Exp(-t * 9f) * 0.7f;
        }));

        /// Low drone (the lava tube / the deep TV antenna).
        public static AudioClip Hum => hum != null ? hum : (hum = Make("PropHum", 3f, (t, r) =>
            (Mathf.Sin(2f * Mathf.PI * 62f * t) * 0.5f + Mathf.Sin(2f * Mathf.PI * 93f * t) * 0.25f) * (0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * (1f / 3f) * t)) * 0.4f));

        static AudioClip Make(string name, float seconds, System.Func<float, System.Random, float> f)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var d = new float[n];
            var rng = new System.Random(name.Length * 977);
            for (int i = 0; i < n; i++) d[i] = Mathf.Clamp(f(i / (float)Rate, rng), -1f, 1f);
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(d, 0);
            return clip;
        }

        /// 3D one-shot (temporary object), scaled by the SFX volume.
        public static void PlayAt(AudioClip clip, Vector3 pos, float volume, float maxDistance = 30f)
        {
            if (clip == null) return;
            volume *= Settings.SfxVolume;
            if (volume <= 0.001f) return;
            var go = new GameObject("PropOneShot");
            go.transform.position = pos;
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.volume = volume;
            src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 1.5f;
            src.maxDistance = maxDistance;
            src.dopplerLevel = 0f;
            src.Play();
            Object.Destroy(go, clip.length + 0.1f);
        }
    }

    /// A looping 3D sound that only plays while the local player is within range (optionally only under water / only
    /// at night) – the radio crackle of the tower ruins (25 m), the blue-drop radio buoy's beep (30 m under water).
    public class PropSoundLoop : MonoBehaviour
    {
        public float range = 25f;
        public float volume = 0.6f;
        public bool underwaterOnly;
        AudioSource src;
        float next;

        public static PropSoundLoop Attach(GameObject host, AudioClip clip, float range, float volume, bool underwaterOnly)
        {
            var l = host.AddComponent<PropSoundLoop>();
            l.range = range;
            l.volume = volume;
            l.underwaterOnly = underwaterOnly;
            var s = host.AddComponent<AudioSource>();
            s.clip = clip;
            s.loop = true;
            s.playOnAwake = false;
            s.spatialBlend = 1f;
            s.rolloffMode = AudioRolloffMode.Linear;
            s.minDistance = 2f;
            s.maxDistance = range;
            s.dopplerLevel = 0f;
            l.src = s;
            return l;
        }

        void Update()
        {
            if (Time.unscaledTime < next || src == null) return;
            next = Time.unscaledTime + 0.5f;
            var p = Refs.Player;
            bool on = p != null && p.Root != null && GameManager.I != null && GameManager.I.InGame
                      && (p.Root.position - transform.position).sqrMagnitude < range * range
                      && (!underwaterOnly || p.IsUnderwater);
            src.volume = volume * Settings.SfxVolume;
            if (on && !src.isPlaying) src.Play();
            else if (!on && src.isPlaying) src.Stop();
        }

        void OnDisable() { if (src != null && src.isPlaying) src.Stop(); }
    }
}
