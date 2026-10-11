using UnityEngine;

namespace Sealebrities
{
    /// Why the camera does (not) register the animal in front of it. Published by FilmHandCamera.Hint for the
    /// viewfinder UI (icons/short words instead of sentences).
    public enum FilmHint
    {
        None,       // camera not in use
        NoAnimal,   // nothing in the frame
        OffCenter,  // an animal is visible but not inside the central 70 % of the frame
        TooFar,     // farther than the scan range (min(12 m, underwater visibility))
        TooSmall,   // too small in the frame – get closer or zoom in
        Blocked,    // something is in the way
        TooDark,    // framed, but too dark to count – lamp!
        Shaky,      // framed, but the camera moves too fast
        Weak,       // framed, but the shot is too weak for another reason
        Good,       // counts (recording → footage)
        AimToLog,   // recording from the hip: footage counts, but only aimed footage goes into the Sealog
        Logged      // this animal is in the Sealog for this dive
    }

    /// Per-evaluation inputs that are the same for every creature (filled once per 0.12 s tick).
    public struct FilmShotContext
    {
        public Camera cam;
        public Vector3 camPos;
        public Vector3 camFwd;
        public float aspect;
        /// Colliders below this transform (the player) never occlude.
        public Transform ignoreRoot;
        /// 0..1 from the look speed (1 = perfectly still).
        public float steadiness;
        public bool lampOn;
        public float lampRange;
        public float night01;
        /// Current scan range in meters (ScanRange()).
        public float maxRange;
    }

    /// Result of a framing check.
    public struct FilmShot
    {
        /// 0..~1.1: coverage × centering × distance × light × steadiness. Counts as footage from FilmHandCamera.MinFraming.
        public float framing;
        public float extent;      // projected size in screen heights (long axis)
        public float fill;        // fraction of the frame filled (max of height/width fraction)
        public float offCenter;   // 0 center .. 0.5 frame edge (Chebyshev distance in viewport units)
        public float distance;    // meters from the lens to the animal's bounds
        public float light;
    }

    /// Scores how well a creature is framed by the hand camera (playtest feedback #16: no more "scanning" of animals
    /// that are not properly in frame or too far away). Hard gates – all must pass for an animal to be a target:
    ///   • bounds center inside the central 70 % of the frame,
    ///   • projected size ≥ 8 % of the screen height (relaxed to 5 % for animals ≤ 12 cm),
    ///   • distance ≤ min(12 m, current underwater visibility from the fog),
    ///   • clear line of sight.
    /// The framing score then grades coverage, centering, distance, light and steadiness.
    public static class FilmShotEvaluator
    {
        public const float FrameMargin = 0.15f;
        public const float MinCover = 0.08f, MinCoverSmall = 0.05f;
        public const float MaxScanRange = 12f;
        /// Legacy name: the coarse range limit (scan range can never exceed this).
        public const float MaxRange = MaxScanRange;
        public const float IdealFillMin = 0.12f, IdealFillMax = 0.65f;

        static readonly float LampConeCos = Mathf.Cos(28f * Mathf.Deg2Rad);
        static readonly RaycastHit[] Hits = new RaycastHit[8];

        // ------------------------------------------------------------------ range / visibility
        /// Distance (m) at which only 10 % of an object's own color is left in the current fog (the FX module drives
        /// RenderSettings fog; under water ExponentialSquared k 0.078 → ≈ 19 m in the shallows, ≈ 11 m deep down).
        public static float Visibility()
        {
            if (!RenderSettings.fog) return 60f;
            float d = RenderSettings.fogDensity;
            switch (RenderSettings.fogMode)
            {
                case FogMode.Linear:
                    return Mathf.Max(1f, RenderSettings.fogEndDistance);
                case FogMode.Exponential:
                    return d > 1e-5f ? 2.302585f / d : 1000f;
                default:   // ExponentialSquared: exp(-(k d)²) = 0.1
                    return d > 1e-5f ? 1.517427f / d : 1000f;
            }
        }

        /// Scan range now: min(12 m, current visibility), never below 3 m. Visibility comes from the FX module
        /// (FXVisibility: the 10 % colour distance of the fog it applies, ≈ 19 m in the shallows, ≈ 8 m deep down,
        /// less at night; ScanMeters = its "clearly recognisable" range), with the raw RenderSettings fog as a fallback.
        public static float ScanRange()
        {
            float vis = Mathf.Min(Visibility(), FXVisibility.Meters);
            if (FXVisibility.Underwater) vis = Mathf.Min(vis, FXVisibility.ScanMeters * 1.2f);
            return Mathf.Clamp(vis, 3f, MaxScanRange);
        }

        /// Minimum projected size (screen heights) for an animal of this body length.
        public static float MinCoverFor(float sizeCm) => Mathf.Lerp(MinCoverSmall, MinCover, Mathf.InverseLerp(12f, 40f, sizeCm));

        // ------------------------------------------------------------------ framing
        /// Returns FilmHint.None when every gate except the line of sight passes (check IsOccluded next),
        /// NoAnimal when the creature is not on screen at all, otherwise the failed gate.
        public static FilmHint Check(ICreature c, SpeciesDef species, in FilmShotContext ctx, out FilmShot shot)
        {
            shot = default;
            Bounds b = c.WorldBounds;
            Vector3 center = b.center;
            Vector3 vp = ctx.cam.WorldToViewportPoint(center);
            if (vp.z <= 0.05f) return FilmHint.NoAnimal;
            float dx = vp.x - 0.5f, dy = vp.y - 0.5f;
            float off = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
            shot.offCenter = off;
            if (off > 0.5f) return FilmHint.NoAnimal;

            float surf = Mathf.Sqrt(b.SqrDistance(ctx.camPos));
            shot.distance = surf;
            if (off > 0.5f - FrameMargin) return FilmHint.OffCenter;
            if (surf > ctx.maxRange) return FilmHint.TooFar;

            Measure(ctx.cam, b, ctx.aspect, out float extent, out float fill);
            shot.extent = extent;
            shot.fill = fill;
            if (extent < MinCoverFor(c.SizeCm)) return FilmHint.TooSmall;

            float fCover = CoverageFactor(fill);
            float r = off / (0.5f - FrameMargin);                                  // 0 center .. 1 edge of the 70 % box
            float fCenter = Mathf.Lerp(1f, 0.7f, Mathf.Clamp01((r - 0.3f) / 0.7f));
            float fDist = DistanceFactor(surf, ctx.maxRange);
            Vector3 to = center - ctx.camPos;
            float cd = to.magnitude;
            Vector3 dir = cd > 1e-4f ? to / cd : ctx.camFwd;
            float fLight = LightFactor(center, cd, dir, species, ctx);
            shot.light = fLight;
            shot.framing = Mathf.Clamp(fCover * fCenter * fDist * fLight * Mathf.Clamp01(ctx.steadiness), 0f, 1.2f);
            return FilmHint.None;
        }

        /// extent = long-axis size in screen heights; fill = max(height fraction, width fraction) of the frame.
        /// A corner behind the lens means the animal overflows the frame (extent/fill 1.5).
        public static void Measure(Camera cam, Bounds b, float aspect, out float extent, out float fill)
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
                if (v.z <= 0.01f) { extent = 1.5f; fill = 1.5f; return; }
                if (v.x < minX) minX = v.x;
                if (v.x > maxX) maxX = v.x;
                if (v.y < minY) minY = v.y;
                if (v.y > maxY) maxY = v.y;
            }
            float h = maxY - minY;
            float w = maxX - minX;
            extent = Mathf.Max(h, w * aspect);
            fill = Mathf.Max(h, w);
        }

        /// Legacy helper: projected size in screen heights (long axis).
        public static float Coverage(Camera cam, Bounds b, float aspect)
        {
            Measure(cam, b, aspect, out float extent, out _);
            return extent;
        }

        /// 1 while the animal fills 12–65 % of the frame; smaller fades to 0.6, overflowing to 0.45.
        public static float CoverageFactor(float fill)
        {
            if (fill < IdealFillMin) return Mathf.Lerp(0.6f, 1f, fill / IdealFillMin);
            if (fill <= IdealFillMax) return 1f;
            return Mathf.Max(0.45f, 1f - (fill - IdealFillMax) * 0.9f);
        }

        /// 1 up to half the scan range, 0.55 at the scan range; a little worse when the lens almost touches the animal.
        public static float DistanceFactor(float dist, float maxRange)
        {
            float half = Mathf.Max(0.5f, maxRange * 0.5f);
            if (dist < 0.3f) return Mathf.Lerp(0.75f, 1f, dist / 0.3f);
            if (dist <= half) return 1f;
            return Mathf.Lerp(1f, 0.55f, Mathf.Clamp01((dist - half) / Mathf.Max(0.1f, maxRange - half)));
        }

        /// Sunlight falls off with depth (1 at ≤ 8 m → 0.25 at ≥ 60 m) and at night; the flashlight cone brings it back (up to 1.1).
        static float LightFactor(Vector3 center, float dist, Vector3 dir, SpeciesDef species, in FilmShotContext ctx)
        {
            float depth = Mathf.Max(0f, Waves.SeaLevel - center.y);
            float light = Mathf.Lerp(1f, 0.25f, Mathf.InverseLerp(8f, 60f, depth));
            light *= Mathf.Lerp(1f, 0.35f, Mathf.Clamp01(ctx.night01));
            if (species != null && species.glows) light = Mathf.Max(light, 0.75f);   // bioluminescent animals light themselves
            if (ctx.lampOn && dist <= ctx.lampRange && Vector3.Dot(ctx.camFwd, dir) >= LampConeCos)
            {
                float boost = Mathf.Clamp01(1.25f - dist / Mathf.Max(0.1f, ctx.lampRange));
                light = Mathf.Lerp(light, 1.1f, boost);
            }
            return light;
        }

        // ------------------------------------------------------------------ line of sight
        /// Any collider between lens and animal (except the player and the animal itself) occludes. Animals close to
        /// the ground get a second chance with a ray to the top of their bounds.
        public static bool IsOccluded(in FilmShotContext ctx, ICreature c, Bounds b)
        {
            Transform self = c.Transform;
            Vector3 center = b.center;
            Vector3 to = center - ctx.camPos;
            float dist = to.magnitude;
            if (dist < 0.05f) return false;
            if (!Blocked(ctx, self, to / dist, dist * 0.98f)) return false;
            Vector3 top = center + Vector3.up * (b.extents.y * 0.8f);
            to = top - ctx.camPos;
            float d = to.magnitude;
            if (d < 0.01f) return true;
            return Blocked(ctx, self, to / d, d * 0.98f);
        }

        static bool Blocked(in FilmShotContext ctx, Transform self, Vector3 dir, float dist)
        {
            int n = Physics.RaycastNonAlloc(ctx.camPos, dir, Hits, dist, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var col = Hits[i].collider;
                if (col == null) continue;
                var ct = col.transform;
                if (ctx.ignoreRoot != null && ct.IsChildOf(ctx.ignoreRoot)) continue;
                if (self != null && ct.IsChildOf(self)) continue;
                return true;
            }
            return false;
        }
    }
}
