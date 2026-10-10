using UnityEngine;

namespace Sealebrities
{
    /// Per-evaluation inputs that are the same for every creature (filled once per 0.12 s tick).
    public struct FilmShotContext
    {
        public Camera cam;
        public Vector3 camPos;
        public Vector3 camFwd;
        public float aspect;
        /// Colliders below this transform (the player) never occlude.
        public Transform ignoreRoot;
        /// Product of the creature-independent factors: steadiness x aim x camera quality.
        public float globalFactor;
        public bool lampOn;
        public float lampRange;
        public float night01;
    }

    /// Scores how well a creature is framed by the hand camera. Quality 0..1.6 is the product of
    /// coverage x centering x distance x light x occlusion (per creature) and steadiness x aim x camera (global).
    public static class FilmShotEvaluator
    {
        public const float MaxRange = 30f;
        public const float IdealCoverMin = 0.12f, IdealCoverMax = 0.6f;
        static readonly float LampConeCos = Mathf.Cos(28f * Mathf.Deg2Rad);
        static readonly RaycastHit[] Hits = new RaycastHit[8];

        /// Returns 0 when the creature is not usable at all (out of range, out of frame, occluded).
        public static float Quality(ICreature c, SpeciesDef species, in FilmShotContext ctx)
        {
            Bounds b = c.WorldBounds;
            Vector3 center = b.center;
            Vector3 to = center - ctx.camPos;
            float dist = to.magnitude;
            if (dist > MaxRange || dist < 0.05f) return 0f;

            // in front and inside the safe frame (5 % margin)
            Vector3 vp = ctx.cam.WorldToViewportPoint(center);
            if (vp.z <= 0f || vp.x < 0.05f || vp.x > 0.95f || vp.y < 0.05f || vp.y > 0.95f) return 0f;

            float fDist = DistanceFactor(dist);
            if (fDist <= 0f) return 0f;

            float fCover = CoverageFactor(Coverage(ctx.cam, b, ctx.aspect));

            float dx = vp.x - 0.5f, dy = vp.y - 0.5f;
            float r = Mathf.Sqrt(dx * dx + dy * dy) * 2f;               // 0 center .. ~1.27 corner of the safe frame
            float fCenter = Mathf.Lerp(1f, 0.45f, Mathf.Clamp01((r - 0.2f) / 0.9f));

            Vector3 dir = to / dist;
            float fLight = LightFactor(center, dist, dir, species, ctx);

            float q = fCover * fCenter * fDist * fLight * ctx.globalFactor;
            if (q <= 0.01f) return 0f;

            // most expensive test last
            if (IsOccluded(ctx, center, b.extents.y, dir, dist)) return 0f;
            return Mathf.Clamp(q, 0f, 1.6f);
        }

        /// Projected size of the bounds in units of screen height (width counts 60 % so long animals are not "tiny").
        /// Returns 2 when a corner is behind the lens (the animal overflows the frame).
        public static float Coverage(Camera cam, Bounds b, float aspect)
        {
            Vector3 c = b.center, e = b.extents;
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var p = new Vector3(
                    c.x + ((i & 1) == 0 ? -e.x : e.x),
                    c.y + ((i & 2) == 0 ? -e.y : e.y),
                    c.z + ((i & 4) == 0 ? -e.z : e.z));
                Vector3 v = cam.WorldToViewportPoint(p);
                if (v.z <= 0.01f) return 2f;
                if (v.x < minX) minX = v.x;
                if (v.x > maxX) maxX = v.x;
                if (v.y < minY) minY = v.y;
                if (v.y > maxY) maxY = v.y;
            }
            float h = maxY - minY;
            float w = (maxX - minX) * aspect;
            return Mathf.Max(h, w * 0.6f);
        }

        /// 1 inside the ideal 12–60 % band; tiny animals fade toward 0.3, overflowing ones toward 0.35.
        public static float CoverageFactor(float cover)
        {
            if (cover < IdealCoverMin) return Mathf.Lerp(0.3f, 1f, cover / IdealCoverMin);
            if (cover <= IdealCoverMax) return 1f;
            return Mathf.Max(0.35f, 1f - (cover - IdealCoverMax) * 0.9f);
        }

        /// Best 1–8 m, fading to 0 at 25 m (water visibility); a little worse when the lens almost touches the animal.
        public static float DistanceFactor(float dist)
        {
            if (dist >= 25f) return 0f;
            if (dist > 8f) return 1f - (dist - 8f) / 17f;
            if (dist < 0.4f) return Mathf.Lerp(0.6f, 1f, dist / 0.4f);
            return 1f;
        }

        /// Sunlight falls off with depth (1 at ≤ 8 m → 0.25 at ≥ 60 m) and at night; the flashlight cone brings it back (up to 1.1).
        static float LightFactor(Vector3 center, float dist, Vector3 dir, SpeciesDef species, in FilmShotContext ctx)
        {
            float depth = Mathf.Max(0f, Waves.SeaLevel - center.y);
            float light = Mathf.Lerp(1f, 0.25f, Mathf.InverseLerp(8f, 60f, depth));
            light *= Mathf.Lerp(1f, 0.35f, Mathf.Clamp01(ctx.night01));
            if (species.glows) light = Mathf.Max(light, 0.75f);   // bioluminescent animals light themselves
            if (ctx.lampOn && dist <= ctx.lampRange && Vector3.Dot(ctx.camFwd, dir) >= LampConeCos)
            {
                float boost = Mathf.Clamp01(1.25f - dist / Mathf.Max(0.1f, ctx.lampRange));
                light = Mathf.Lerp(light, 1.1f, boost);
            }
            return light;
        }

        /// Creatures have no colliders, so any non-player hit between lens and body center occludes.
        /// Animals close to the ground get a second chance with a ray to the top of their bounds.
        static bool IsOccluded(in FilmShotContext ctx, Vector3 center, float halfHeight, Vector3 dir, float dist)
        {
            if (!Blocked(ctx, ctx.camPos, dir, dist * 0.98f)) return false;
            Vector3 top = center + Vector3.up * (halfHeight * 0.8f);
            Vector3 to = top - ctx.camPos;
            float d = to.magnitude;
            if (d < 0.01f) return true;
            return Blocked(ctx, ctx.camPos, to / d, d * 0.98f);
        }

        static bool Blocked(in FilmShotContext ctx, Vector3 origin, Vector3 dir, float dist)
        {
            int n = Physics.RaycastNonAlloc(origin, dir, Hits, dist, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var col = Hits[i].collider;
                if (col == null) continue;
                if (ctx.ignoreRoot != null && col.transform.IsChildOf(ctx.ignoreRoot)) continue;
                return true;
            }
            return false;
        }
    }
}
