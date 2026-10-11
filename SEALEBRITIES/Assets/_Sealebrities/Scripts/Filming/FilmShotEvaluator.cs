using UnityEngine;

namespace Sealebrities
{
    /// LEGACY viewfinder reason (rounds 1–2a). Round 2b publishes ViewfinderInfo.Hint (ShotHint, K.2) for the UI;
    /// FilmHandCamera still maps its state onto this enum for older readers (UIViewfinder until the ui switch, test logs):
    /// NoAnimal = nothing in frame, TooFar/TooSmall = animals in frame but too far / too small to count, TooDark = the
    /// Sealog subject is too dark, Good = animals detected (a valid Sealog photo subject of a NEW species when aiming),
    /// Logged = valid Sealog photo subject of a species already in the Sealog. OffCenter, Blocked, Shaky, Weak and
    /// AimToLog are no longer produced (no focus requirements any more – K.2).
    public enum FilmHint
    {
        None, NoAnimal, OffCenter, TooFar, TooSmall, Blocked, TooDark, Shaky, Weak, Good, AimToLog, Logged
    }

    /// Why one creature is (not) detected this tick.
    public enum FilmGate : byte
    {
        Detected,   // inside the frame, within range, big enough (occlusion is checked by FilmDetector)
        OffScreen,  // centre outside the viewport (5 % margin) or behind the lens
        Hidden,     // on the other side of the sea surface (camera above water, animal deep below – or the reverse)
        TooFar,     // in frame but beyond DetectRange
        TooSmall    // in frame but smaller than 2.5 % (1.5 % for ≤ 12 cm) of the screen height
    }

    /// Per-tick inputs that are the same for every creature (filled once per 0.12 s tick).
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
        /// Detection range in meters (DetectRange()).
        public float maxRange;
        /// Hand factor: 1.0 aiming (and in the selfie view), 0.9 from the hip (K.2).
        public bool aimed;
        /// Camera gear quality (0.6 … 1.6) – part of the frame weight.
        public float cameraQuality;
        /// Q32 night-vision module switched on: darkness counts as full light.
        public bool nightVision;
        /// The lens is below the sea surface.
        public bool camUnderwater;
        /// Food buff NightEyes (Glow Soda): multiplier on the residual light (1 = none, max 1 light).
        public float ambientBoost;
    }

    /// Result of the per-creature check.
    public struct FilmShot
    {
        /// K.2 frame weight w = Size × Light × Steady × Centre × Hand × cameraQuality (0 … ~1.8).
        public float weight;
        /// Legacy alias of weight (round-1 "framing").
        public float framing;
        /// Projected size in screen heights (long axis).
        public float extent;
        /// Fraction of the frame filled (max of height / width fraction).
        public float fill;
        /// 0 centre .. 0.5 frame edge (Chebyshev distance in viewport units).
        public float offCenter;
        /// Meters from the lens to the animal's bounds.
        public float distance;
        public float light;
        /// Bounds centre in viewport units (0..1, origin bottom-left).
        public Vector2 viewport;
        /// Viewport rect of the bounds (xMin, yMin, xMax, yMax), clamped to 0..1.
        public Vector4 rect;
        /// Centre inside the central 40 % Sealog frame.
        public bool inSealogFrame;
        /// Big enough for a Sealog photo (extent ≥ SealogMinExtent).
        public bool sealogSized;
        /// Meters below the sea surface (0 on land / in the air).
        public float depth;
    }

    /// K.2 "just keep recording": every animal visible in the viewfinder counts – no target lock, no central box.
    /// An animal is DETECTED when (1) its bounds centre is inside the viewport (5 % margin), (2) the distance to its
    /// bounds is ≤ DetectRange = clamp(visibility × 0.9, 4, 25) m, (3) its projected size is ≥ 2.5 % of the screen
    /// height (1.5 % for animals ≤ 12 cm), (4) it is not occluded (FilmDetector: 2-ray test for the 16 best candidates).
    /// Its frame weight w = Size × Light × Steady × Centre × Hand × cameraQuality is the clip quality it adds.
    /// The SEALOG PHOTO subject is the largest detected animal whose centre lies in the central 40 % frame, with an extent
    /// ≥ 10 % of the screen height and Light ≥ 0.35 (lamp at night / below 40 m, or the night-vision module).
    /// This class is the constants owner for every detection / photo threshold (Design_Runde2 J.5).
    public static class FilmShotEvaluator
    {
        // ------------------------------------------------------------------ K.2 detection
        public const float DetectInterval = 0.12f;
        public const float ViewportMargin = 0.05f;
        public const float DetectRangeMin = 4f, DetectRangeMax = 25f, DetectVisibilityShare = 0.9f;
        public const float MinExtent = 0.025f, MinExtentSmall = 0.015f, SmallAnimalCm = 12f;
        /// Occlusion rays are cast for this many best candidates per tick (fixed arrays, no allocations).
        public const int OcclusionBudget = 16;
        /// Candidates beyond the occlusion budget count only when they are this close (× range) – schools stay countable.
        public const float UntestedRangeShare = 0.6f;
        /// Size factor: extent / SizeFullExtent, clamped to SizeMin..1.
        public const float SizeFullExtent = 0.12f, SizeMin = 0.35f;
        /// Steady factor: lerp(SteadyMin, 1, steadiness).
        public const float SteadyMin = 0.7f;
        /// Centre factor: 1.0 inside the central 50 % (half size 0.25), else CentreOutside.
        public const float CentreHalf = 0.25f, CentreOutside = 0.85f;
        /// Hand factor (aiming / from the hip).
        public const float HandAimed = 1f, HandHip = 0.9f;
        /// A detected individual becomes a clip after this many seconds; the clip improves up to MaxClipSeconds.
        public const float CaptureSeconds = 1f, MaxClipSeconds = 8f;
        public const float MinQuality = 0.1f, MaxQuality = 1.6f;
        /// The camera above the water does not see animals deeper than this (and vice versa: nothing above the surface
        /// is seen from below it) – the sea surface is nearly opaque in the low-poly look.
        public const float SurfaceHideDepth = 1.5f;

        // ------------------------------------------------------------------ Sealog photo (K.2)
        /// The Sealog frame is the central 40 % box (half size 0.20).
        public const float SealogFrameHalf = 0.20f;
        public const float SealogMinExtent = 0.10f;
        /// Tiny animals (≤ SmallAnimalCm: mandarinfish, cleaner wrasse, lanternfish, geckos …) need 6 %: at 10 % a 5 cm
        /// fish had to be ≤ 0.36 m from the starter camera (playtest round 2: the mandarinfish never registered).
        public const float SealogMinExtentSmall = 0.06f;

        /// Minimum projected size of a Sealog photo subject for an animal of this body length.
        public static float SealogMinExtentFor(float sizeCm) => sizeCm <= SmallAnimalCm ? SealogMinExtentSmall : SealogMinExtent;
        public const float SealogMinLight = 0.35f;
        /// Photo quality: fill factor 1.0 while the subject fills 25–60 % of the frame.
        public const float PhotoFillIdealMin = 0.25f, PhotoFillIdealMax = 0.60f;
        public const float PhotoCooldown = 0.6f;

        // ------------------------------------------------------------------ legacy (still referenced elsewhere)
        /// Legacy: margin of the viewfinder bracket box. Now the Sealog frame (central 40 %): old viewfinders that place
        /// their corner brackets with this margin mark exactly the Sealog photo frame.
        public const float FrameMargin = 0.5f - SealogFrameHalf;
        /// Legacy round-1 scan cap (12 m). Still used by the fauna module to cull deep animals seen from above the water;
        /// detection itself uses DetectRange (up to 25 m).
        public const float MaxScanRange = 12f;
        public const float MaxRange = MaxScanRange;
        public const float MinCover = 0.08f, MinCoverSmall = 0.05f;
        public const float IdealFillMin = 0.12f, IdealFillMax = 0.65f;

        static readonly float LampConeCos = Mathf.Cos(28f * Mathf.Deg2Rad);
        static readonly RaycastHit[] Hits = new RaycastHit[8];

        // ------------------------------------------------------------------ range / visibility
        /// Distance (m) at which only 10 % of an object's own color is left in the current RenderSettings fog
        /// (fallback when the FX module has not published FXVisibility yet).
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

        /// K.2 detection range: clamp(visibility × 0.9, 4, 25) m ("within visibility"). Above water 25 m.
        public static float DetectRange()
        {
            float vis = FXVisibility.Meters;
            if (!(vis > 0f) || float.IsInfinity(vis)) vis = Visibility();
            return Mathf.Clamp(vis * DetectVisibilityShare, DetectRangeMin, DetectRangeMax);
        }

        /// LEGACY round-1 scan range: min(12 m, visibility), never below 3 m (test logs / metrics only).
        public static float ScanRange()
        {
            float vis = Mathf.Min(Visibility(), FXVisibility.Meters);
            if (FXVisibility.Underwater) vis = Mathf.Min(vis, FXVisibility.ScanMeters * 1.2f);
            return Mathf.Clamp(vis, 3f, MaxScanRange);
        }

        /// Minimum projected size (screen heights) for an animal of this body length (K.2: 2.5 %, 1.5 % for ≤ 12 cm).
        public static float MinExtentFor(float sizeCm) => sizeCm <= SmallAnimalCm ? MinExtentSmall : MinExtent;

        /// Legacy name of MinExtentFor.
        public static float MinCoverFor(float sizeCm) => MinExtentFor(sizeCm);

        // ------------------------------------------------------------------ detection
        /// Checks every gate except the line of sight (FilmDetector tests occlusion for the best candidates) and fills
        /// the shot (weight, extent, fill, rect, light, Sealog-frame flags). Allocation free.
        public static FilmGate Detect(ICreature c, SpeciesDef species, in FilmShotContext ctx, out FilmShot shot)
        {
            shot = default;
            Bounds b = c.WorldBounds;
            Vector3 center = b.center;
            Vector3 vp = ctx.cam.WorldToViewportPoint(center);
            if (!(vp.z > 0.05f)) return FilmGate.OffScreen;
            if (vp.x < ViewportMargin || vp.x > 1f - ViewportMargin || vp.y < ViewportMargin || vp.y > 1f - ViewportMargin)
                return FilmGate.OffScreen;

            // the sea surface separates the two worlds (fish under water are not filmable from the dock and birds are
            // not filmable from below the surface)
            float depth = Waves.SeaLevel - center.y;
            if (!ctx.camUnderwater && depth > SurfaceHideDepth) return FilmGate.Hidden;
            if (ctx.camUnderwater && depth < -0.3f) return FilmGate.Hidden;

            float dx = vp.x - 0.5f, dy = vp.y - 0.5f;
            float off = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
            shot.offCenter = off;
            shot.viewport = new Vector2(vp.x, vp.y);
            shot.depth = Mathf.Max(0f, depth);

            float surf = Mathf.Sqrt(b.SqrDistance(ctx.camPos));
            shot.distance = surf;
            if (surf > ctx.maxRange) return FilmGate.TooFar;

            Measure(ctx.cam, b, ctx.aspect, out float extent, out float fill, out Vector4 rect);
            shot.extent = extent;
            shot.fill = fill;
            shot.rect = rect;
            if (extent < MinExtentFor(c.SizeCm)) return FilmGate.TooSmall;

            Vector3 to = center - ctx.camPos;
            float cd = to.magnitude;
            Vector3 dir = cd > 1e-4f ? to / cd : ctx.camFwd;
            float light = LightFactor(center, cd, dir, species, ctx);
            shot.light = light;
            shot.weight = FrameWeight(extent, light, ctx.steadiness, dx, dy, ctx.aimed, ctx.cameraQuality);
            shot.framing = shot.weight;
            shot.inSealogFrame = off <= SealogFrameHalf;
            shot.sealogSized = extent >= SealogMinExtentFor(c.SizeCm);
            return FilmGate.Detected;
        }

        /// K.2 frame weight of one detected animal.
        public static float FrameWeight(float extent, float light, float steadiness, float dx, float dy, bool aimed, float cameraQuality)
        {
            float size = Mathf.Clamp(extent / SizeFullExtent, SizeMin, 1f);
            float steady = Mathf.Lerp(SteadyMin, 1f, Mathf.Clamp01(steadiness));
            float centre = Mathf.Abs(dx) <= CentreHalf && Mathf.Abs(dy) <= CentreHalf ? 1f : CentreOutside;
            float hand = aimed ? HandAimed : HandHip;
            float q = Mathf.Max(0.1f, float.IsNaN(cameraQuality) ? 0.6f : cameraQuality);
            float w = size * Mathf.Max(0f, light) * steady * centre * hand * q;
            return float.IsNaN(w) ? 0f : w;
        }

        /// Photo quality 0..1 of a Sealog subject = fill factor (ideal 25–60 % of the frame) × centring × light × steadiness.
        public static float PhotoQuality(in FilmShot s, float steadiness)
        {
            float fill = PhotoFillFactor(s.fill);
            float r = Mathf.Clamp01(s.offCenter / SealogFrameHalf);
            float centre = Mathf.Lerp(1f, 0.75f, r * r);
            float light = Mathf.Clamp01(s.light);
            float steady = Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(steadiness));
            float q = fill * centre * light * steady;
            return float.IsNaN(q) ? 0f : Mathf.Clamp01(q);
        }

        /// 1 while the subject fills 25–60 % of the frame; smaller fades to 0.45 (at 5 %), overflowing to 0.6.
        public static float PhotoFillFactor(float fill)
        {
            if (fill < PhotoFillIdealMin) return Mathf.Lerp(0.45f, 1f, Mathf.InverseLerp(0.05f, PhotoFillIdealMin, fill));
            if (fill <= PhotoFillIdealMax) return 1f;
            return Mathf.Lerp(1f, 0.6f, Mathf.InverseLerp(PhotoFillIdealMax, 1.2f, fill));
        }

        /// extent = long-axis size in screen heights; fill = max(height fraction, width fraction) of the frame;
        /// rect = clamped viewport rect. A corner behind the lens means the animal overflows the frame (extent/fill 1.5).
        public static void Measure(Camera cam, Bounds b, float aspect, out float extent, out float fill, out Vector4 rect)
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
                if (v.z <= 0.01f)
                {
                    extent = 1.5f; fill = 1.5f;
                    rect = new Vector4(0f, 0f, 1f, 1f);
                    return;
                }
                if (v.x < minX) minX = v.x;
                if (v.x > maxX) maxX = v.x;
                if (v.y < minY) minY = v.y;
                if (v.y > maxY) maxY = v.y;
            }
            float h = maxY - minY;
            float w = maxX - minX;
            extent = Mathf.Max(h, w * aspect);
            fill = Mathf.Max(h, w);
            rect = new Vector4(Mathf.Clamp01(minX), Mathf.Clamp01(minY), Mathf.Clamp01(maxX), Mathf.Clamp01(maxY));
        }

        /// Legacy overload without the rect.
        public static void Measure(Camera cam, Bounds b, float aspect, out float extent, out float fill) =>
            Measure(cam, b, aspect, out extent, out fill, out _);

        /// Viewport rect of a bounds (for brackets refreshed every frame). False when a corner is behind the lens.
        public static bool ViewportRect(Camera cam, Bounds b, out Vector4 rect)
        {
            Measure(cam, b, cam.aspect, out float extent, out _, out rect);
            return extent < 1.4f;
        }

        /// Legacy helper: projected size in screen heights (long axis).
        public static float Coverage(Camera cam, Bounds b, float aspect)
        {
            Measure(cam, b, aspect, out float extent, out _, out _);
            return extent;
        }

        /// Legacy round-1 coverage grade (kept for older callers).
        public static float CoverageFactor(float fill)
        {
            if (fill < IdealFillMin) return Mathf.Lerp(0.6f, 1f, fill / IdealFillMin);
            if (fill <= IdealFillMax) return 1f;
            return Mathf.Max(0.45f, 1f - (fill - IdealFillMax) * 0.9f);
        }

        /// Legacy round-1 distance grade (kept for older callers).
        public static float DistanceFactor(float dist, float maxRange)
        {
            float half = Mathf.Max(0.5f, maxRange * 0.5f);
            if (dist < 0.3f) return Mathf.Lerp(0.75f, 1f, dist / 0.3f);
            if (dist <= half) return 1f;
            return Mathf.Lerp(1f, 0.55f, Mathf.Clamp01((dist - half) / Mathf.Max(0.1f, maxRange - half)));
        }

        /// Sunlight falls off with depth (1 at ≤ 8 m → 0.25 at ≥ 60 m) and at night; the flashlight cone brings it back
        /// (up to 1.1); glowing animals light themselves; the night-vision module counts as full light (Q32).
        public static float LightFactor(Vector3 center, float dist, Vector3 dir, SpeciesDef species, in FilmShotContext ctx)
        {
            float depth = Mathf.Max(0f, Waves.SeaLevel - center.y);
            float light = Mathf.Lerp(1f, 0.25f, Mathf.InverseLerp(8f, 60f, depth));
            light *= Mathf.Lerp(1f, 0.35f, Mathf.Clamp01(ctx.night01));
            if (ctx.ambientBoost > 1f) light = Mathf.Min(1f, light * ctx.ambientBoost);
            if (species != null && species.glows) light = Mathf.Max(light, 0.75f);
            if (ctx.lampOn && dist <= ctx.lampRange && Vector3.Dot(ctx.camFwd, dir) >= LampConeCos)
            {
                float boost = Mathf.Clamp01(1.25f - dist / Mathf.Max(0.1f, ctx.lampRange));
                light = Mathf.Lerp(light, 1.1f, boost);
            }
            if (ctx.nightVision) light = Mathf.Max(light, 1f);
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
