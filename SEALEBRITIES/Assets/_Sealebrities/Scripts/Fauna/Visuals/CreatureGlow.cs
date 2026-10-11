using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// G.2 "deep creatures glow a little" + art bible §2.5 emission floor. Every opaque creature material carries an
    /// emission channel = albedo × k, where k follows the local camera depth (the animals you can see live within the
    /// ~20 m visibility around you):
    ///   0–20 m: 0.10 → 0.18 at 40 m, scaled by daylight (art bible §2.5, kept above 35 m)
    ///   ≥ 35 m: lerp(0.18, 0.32, (depth − 35) / 60), independent of daylight (G.2; blended over 30–40 m)
    /// Glowing species additionally pulse their glow slot (photophores, lure, tail tips) ±15 % at 0.5 Hz.
    /// Land animals keep k = 0 (normal daylight look). Ticked once per frame by the FaunaSpawner; the floor is
    /// quantized so the ~300 creature materials are only touched when it actually changes.
    public static class CreatureGlow
    {
        struct Entry { public Material m; public Color albedo; public bool solid; public bool land; }
        struct Pulse { public Material m; public Color e; }

        static readonly List<Entry> Entries = new List<Entry>(256);
        static readonly List<Pulse> Pulses = new List<Pulse>(32);
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        static float lastK = -1f;

        /// Current emission floor (0..0.32) – for tests and the swarm renderer.
        public static float Floor { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Entries.Clear(); Pulses.Clear(); lastK = -1f; Floor = 0f; }

        /// solid: the material has no emission map, emission = albedo × k. textured: emission map = base map, colour = k.
        internal static void Register(Material m, Color albedo, bool land, bool solid, float unused)
        {
            if (m == null) return;
            Entries.Add(new Entry { m = m, albedo = albedo, solid = solid, land = land });
            if (lastK >= 0f) Apply(Entries[Entries.Count - 1], lastK);
        }

        internal static void RegisterPulse(Material m, Color baseEmission)
        {
            if (m == null) return;
            Pulses.Add(new Pulse { m = m, e = baseEmission });
        }

        /// Emission floor at a camera depth (m, 0 above water) and daylight 0..1.
        public static float FloorAt(bool underwater, float depth, float sun01)
        {
            if (!underwater) return 0.03f * Mathf.Clamp01(sun01);
            float shallow = Mathf.Lerp(0.10f, 0.18f, Mathf.Clamp01((depth - 20f) / 20f)) * Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(sun01));
            float deep = Mathf.Lerp(0.18f, 0.32f, Mathf.Clamp01((depth - 35f) / 60f));
            return Mathf.Lerp(shallow, deep, Mathf.Clamp01((depth - 30f) / 10f));
        }

        public static void Tick(float time)
        {
            var p = Refs.Player;
            bool under = false;
            float depth = 0f;
            if (p != null)
            {
                under = p.IsUnderwater;
                depth = under ? Mathf.Max(0f, p.Depth) : 0f;
            }
            float k = FloorAt(under, depth, EnvState.SunIntensity01);
            k = Mathf.Round(k * 200f) / 200f;
            Floor = k;
            if (Mathf.Abs(k - lastK) > 1e-4f)
            {
                lastK = k;
                for (int i = Entries.Count - 1; i >= 0; i--)
                {
                    var e = Entries[i];
                    if (e.m == null) { Entries.RemoveAt(i); continue; }
                    Apply(e, k);
                }
            }
            if (Pulses.Count > 0)
            {
                // brighter in the dark: the pulse rides on top of the base glow
                float f = 1f + 0.15f * Mathf.Sin(time * Mathf.PI) + (under ? Mathf.Clamp01((depth - 35f) / 60f) * 0.25f : 0f);
                for (int i = Pulses.Count - 1; i >= 0; i--)
                {
                    var q = Pulses[i];
                    if (q.m == null) { Pulses.RemoveAt(i); continue; }
                    q.m.SetColor(EmissionColor, q.e * f);
                }
            }
        }

        static void Apply(Entry e, float k)
        {
            if (e.land) k = 0f;
            Color c = e.solid ? new Color(e.albedo.r * k, e.albedo.g * k, e.albedo.b * k, 1f) : new Color(k, k, k, 1f);
            e.m.SetColor(EmissionColor, c);
        }
    }
}
