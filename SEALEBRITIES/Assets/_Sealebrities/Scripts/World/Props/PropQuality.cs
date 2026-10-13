using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Graphics-quality dependent render distances of the props (art bible §1.5 tiers). Only RENDERING changes with the
    /// tier – placement and colliders are identical on every machine (co-op determinism).
    ///   High: grass 100 % / 60 m, props 140 m · Medium: 60 % / 40 m, 100 m · Low: 30 % / 25 m, 70 m.
    public static class PropQuality
    {
        struct Entry
        {
            public LODGroup group;
            public Renderer[] lod0, lod1;
            public float d0, d1;
        }

        static readonly List<Entry> Entries = new List<Entry>();
        static int applied = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Entries.Clear(); applied = -1; }

        public static GraphicsQuality Tier => Settings.Data != null ? Settings.Data.quality : GraphicsQuality.High;

        /// Multiplier for near-detail cull distances (trees/props: 140 / 100 / 70 m).
        public static float CullScale
        {
            get
            {
                switch (Tier)
                {
                    case GraphicsQuality.Low: return 0.5f;
                    case GraphicsQuality.Medium: return 0.72f;
                    default: return 1f;
                }
            }
        }

        /// Multiplier for the far silhouettes (island skyline seen from the sea).
        public static float FarScale
        {
            get
            {
                switch (Tier)
                {
                    case GraphicsQuality.Low: return 0.65f;
                    case GraphicsQuality.Medium: return 0.85f;
                    default: return 1f;
                }
            }
        }

        public static float GrassDensity
        {
            get
            {
                switch (Tier)
                {
                    case GraphicsQuality.Low: return 0.3f;
                    case GraphicsQuality.Medium: return 0.6f;
                    default: return 1f;
                }
            }
        }

        public static float GrassDistance
        {
            get
            {
                switch (Tier)
                {
                    case GraphicsQuality.Low: return 25f;
                    case GraphicsQuality.Medium: return 40f;
                    default: return 60f;
                }
            }
        }

        /// Distance below which grass uses the full 12–24 blade clumps (8 blades beyond).
        public const float GrassNearLod = 20f;

        public static void RegisterCull(GameObject holder, List<Renderer> renderers, float cullDistance)
        {
            if (holder == null || renderers == null || renderers.Count == 0) return;
            var lod = holder.GetComponent<LODGroup>();
            if (lod == null) lod = holder.AddComponent<LODGroup>();
            lod.fadeMode = LODFadeMode.None;
            var e = new Entry { group = lod, lod0 = renderers.ToArray(), lod1 = null, d0 = cullDistance, d1 = 0f };
            Entries.Add(e);
            Apply(e, true);
        }

        public static void RegisterLod(GameObject holder, List<Renderer> lod0, List<Renderer> lod1, float nearDistance, float farDistance)
        {
            if (holder == null) return;
            if ((lod0 == null || lod0.Count == 0) && (lod1 == null || lod1.Count == 0)) return;
            var lod = holder.GetComponent<LODGroup>();
            if (lod == null) lod = holder.AddComponent<LODGroup>();
            lod.fadeMode = LODFadeMode.None;
            var e = new Entry
            {
                group = lod,
                lod0 = lod0 != null ? lod0.ToArray() : new Renderer[0],
                lod1 = lod1 != null ? lod1.ToArray() : new Renderer[0],
                d0 = nearDistance,
                d1 = farDistance
            };
            Entries.Add(e);
            Apply(e, true);
        }

        /// Re-applies the distances when the quality tier changed (cheap to call every frame).
        public static void Poll()
        {
            int t = (int)Tier;
            if (t == applied) return;
            applied = t;
            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                if (Entries[i].group == null) { Entries.RemoveAt(i); continue; }
                Apply(Entries[i], false);
            }
        }

        // the default vertical FOV is 70° (art bible §1.13)
        static float Height(float size, float distance) => Mathf.Clamp(size / (Mathf.Max(1f, distance) * 2f * Mathf.Tan(35f * Mathf.Deg2Rad)), 0.0002f, 0.95f);

        static void Apply(Entry e, bool recalcBounds)
        {
            var lod = e.group;
            if (lod == null) return;
            if (recalcBounds)
            {
                var all = new List<Renderer>();
                if (e.lod0 != null) all.AddRange(e.lod0);
                if (e.lod1 != null) all.AddRange(e.lod1);
                lod.SetLODs(new[] { new LOD(0.01f, all.ToArray()) });
                lod.RecalculateBounds();
            }
            float size = Mathf.Max(1f, lod.size);
            if (e.lod1 == null)
            {
                lod.SetLODs(new[] { new LOD(Height(size, e.d0 * CullScale), e.lod0) });
                return;
            }
            float h0 = Height(size, e.d0 * CullScale);
            float h1 = Height(size, e.d1 * FarScale);
            if (h1 >= h0) h1 = h0 * 0.5f;
            if (e.lod0.Length == 0) lod.SetLODs(new[] { new LOD(h1, e.lod1) });
            else if (e.lod1.Length == 0) lod.SetLODs(new[] { new LOD(h0, e.lod0) });
            else lod.SetLODs(new[] { new LOD(h0, e.lod0), new LOD(h1, e.lod1) });
        }
    }
}
