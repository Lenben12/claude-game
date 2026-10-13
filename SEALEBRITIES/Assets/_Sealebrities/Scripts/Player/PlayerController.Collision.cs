using UnityEngine;

namespace Sealebrities
{
    /// Round 3 collision audit (GDD §19 „Durch feste Objekte darf man nirgends laufen“): a CharacterController only
    /// collides while it moves itself – a boat hull, a swinging door or a pushed crate that moves INTO the diver, or a
    /// teleport / climb that ends inside something, leaves the capsule overlapping, and from there it walks / swims
    /// straight through. Every frame (controller on) the shrunken capsule is tested against convex colliders (boxes,
    /// capsules, spheres, convex meshes – the boat hull, props, doors) and pushed out along the shortest way (cc.Move, so
    /// it never pushes through a wall behind). Non-convex meshes (terrain, rocks, the wreck) are static: the controller's
    /// overlap recovery handles them. Allocation-free.
    public partial class PlayerController
    {
        static readonly Collider[] overlapBuf = new Collider[16];
        const float DepenMaxStep = 0.25f;

        /// Last frame's push-out distance (tests / debug).
        public float LastDepenetration { get; private set; }

        void Depenetrate()
        {
            LastDepenetration = 0f;
            if (cc == null || !cc.enabled || seated || ladderClimbing || vaulting || lyingCam || menuMode) return;
            float r = cc.radius - 0.02f;
            Vector3 p = transform.position;
            Vector3 c = p + cc.center;
            float half = Mathf.Max(0f, cc.height * 0.5f - cc.radius);
            Vector3 top = c + Vector3.up * half;
            Vector3 bottom = c - Vector3.up * (half - 0.05f);   // a little above the feet: the floor never counts
            int n = Physics.OverlapCapsuleNonAlloc(top, bottom, r, overlapBuf, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (n <= 0) return;
            Vector3 push = Vector3.zero;
            for (int i = 0; i < n; i++)
            {
                var col = overlapBuf[i];
                overlapBuf[i] = null;
                if (col == null || col == cc || col.isTrigger || !col.enabled) continue;
                if (col is TerrainCollider) continue;
                if (col is MeshCollider mc && !mc.convex) continue;
                if (col.transform.IsChildOf(transform)) continue;
                Vector3 best = Vector3.zero;
                float bestLen = 0f;
                bool inside = false;
                for (int k = 0; k < 3; k++)
                {
                    Vector3 s = k == 0 ? top : k == 1 ? (top + bottom) * 0.5f : bottom;
                    Vector3 q = col.ClosestPoint(s);
                    Vector3 d = s - q;
                    float m = d.magnitude;
                    if (m < 1e-4f) { inside = true; continue; }
                    if (m < r && r - m > bestLen) { bestLen = r - m; best = d / m * (r - m); }
                }
                if (bestLen <= 0f && inside)
                {
                    // the axis itself is inside: step out horizontally, away from the collider's centre
                    Vector3 away = c - col.bounds.center;
                    away.y = 0f;
                    if (away.sqrMagnitude < 1e-6f) away = -YawForward();
                    best = away.normalized * 0.12f;
                    bestLen = 0.12f;
                }
                // keep the strongest push per axis direction (several overlaps add up, capped below)
                if (Mathf.Abs(best.x) > Mathf.Abs(push.x)) push.x = best.x;
                if (Mathf.Abs(best.y) > Mathf.Abs(push.y)) push.y = best.y;
                if (Mathf.Abs(best.z) > Mathf.Abs(push.z)) push.z = best.z;
            }
            float len = push.magnitude;
            if (len < 0.002f) return;
            if (len > DepenMaxStep) push *= DepenMaxStep / len;
            // never push down into the floor on land
            if (mode == PlayerMoveMode.Land && push.y < 0f) push.y = 0f;
            cc.Move(push);
            LastDepenetration = push.magnitude;
        }
    }
}
