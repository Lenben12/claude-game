using UnityEngine;

namespace Sealebrities
{
    /// Allocation-free navigation helpers shared by creatures, schools and the spawner.
    public static class FaunaNav
    {
        /// Seafloor shallower than this (world y) counts as land / beach: creatures never go there.
        public const float LandGuardY = -1.2f;
        /// Wave-safe surface height used for planning (actual clamping uses Waves.HeightAt).
        public const float PlanningSurfaceY = Waves.SeaLevel - 0.6f;

        public static float DistXZ(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        public static float DistXZSqr(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        public static float Ground(Vector3 p) => WorldInfo.GroundHeight(p);

        public static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        public static Vector3 FlatDir(Vector3 v)
        {
            v.y = 0f;
            float m = v.magnitude;
            return m > 1e-5f ? v / m : Vector3.zero;
        }

        /// Horizontal direction toward deeper water (zero on flat ground).
        public static Vector3 Downhill(Vector3 p)
        {
            const float e = 3f;
            float gx = Ground(new Vector3(p.x + e, 0f, p.z)) - Ground(new Vector3(p.x - e, 0f, p.z));
            float gz = Ground(new Vector3(p.x, 0f, p.z + e)) - Ground(new Vector3(p.x, 0f, p.z - e));
            var v = new Vector3(-gx, 0f, -gz);
            float m = v.magnitude;
            return m > 1e-4f ? v / m : Vector3.zero;
        }

        /// Upper limit (world y) of a species' preferred band.
        public static float BandTop(SpeciesDef s, float halfHeight, float surfaceY)
            => Mathf.Min(-s.BandMin(FaunaContext.NightNow), surfaceY - 1f - halfHeight);

        /// Lower limit (world y) of a species' preferred band at the given seafloor height (night shift applied, L.1:
        /// lanternfish rise 30 m at night).
        public static float BandBottom(SpeciesDef s, float groundY, float clearance)
            => Mathf.Max(-s.BandMax(FaunaContext.NightNow), groundY + clearance);

        /// Picks a random reachable point for wandering: inside the leash circle, over water deep enough for the
        /// species and inside its depth band. bottomBias 0 = anywhere in the band, 1 = right above the seafloor.
        public static bool TryPickPoint(SpeciesDef s, Vector3 from, float minR, float maxR, Vector3 leashC, float leashR,
                                        float clearance, float halfHeight, float bottomBias, out Vector3 result)
        {
            for (int i = 0; i < 6; i++)
            {
                Vector2 r = Random.insideUnitCircle;
                if (r.sqrMagnitude < 1e-4f) r = Vector2.right;
                r = r.normalized * Random.Range(minR, maxR);
                var p = new Vector3(from.x + r.x, 0f, from.z + r.y);

                if (leashR > 0f)
                {
                    float dx = p.x - leashC.x, dz = p.z - leashC.z;
                    float d = Mathf.Sqrt(dx * dx + dz * dz);
                    if (d > leashR)
                    {
                        float k = leashR * Random.Range(0.45f, 0.95f) / d;
                        p.x = leashC.x + dx * k;
                        p.z = leashC.z + dz * k;
                    }
                }

                float g = Ground(p);
                if (g > LandGuardY - 0.5f) continue;
                float top = BandTop(s, halfHeight, PlanningSurfaceY);
                float bot = BandBottom(s, g, clearance);
                if (bot > top) continue;

                float y = Random.Range(bot, top);
                if (bottomBias > 0f) y = Mathf.Lerp(y, bot + Mathf.Min(0.4f, top - bot) * Random.value, bottomBias);
                else y = Mathf.Lerp(y, Mathf.Clamp(from.y, bot, top), 0.45f);
                p.y = y;
                result = p;
                return true;
            }
            result = from;
            return false;
        }

        /// Clamps a point into water: below the (planning) surface and above the seafloor. False if over land.
        public static bool ClampIntoWater(ref Vector3 p, float clearance, float halfHeight)
        {
            float g = Ground(p);
            if (g > LandGuardY) return false;
            float top = PlanningSurfaceY - 1f - halfHeight;
            float bot = g + clearance;
            if (bot > top) p.y = Mathf.Max(g + halfHeight + 0.05f, Mathf.Min(top, bot));
            else p.y = Mathf.Clamp(p.y, bot, top);
            return true;
        }

        /// Searches straight down for any solid surface (wreck deck, rocks, terrain). Returns its height or the terrain height.
        public static float SurfaceBelow(Vector3 p, float maxDist)
        {
            float g = Ground(p);
            if (Physics.Raycast(p + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, maxDist + 0.5f, FaunaContext.ObstacleMask, QueryTriggerInteraction.Ignore))
            {
                if (!FaunaContext.IsPlayerCollider(hit.collider) && hit.point.y > g) return hit.point.y;
            }
            return g;
        }
    }
}
