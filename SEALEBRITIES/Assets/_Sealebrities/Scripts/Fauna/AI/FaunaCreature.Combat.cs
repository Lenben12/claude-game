using UnityEngine;

namespace Sealebrities
{
    /// Round 3 (GDD §19/§20 Q17/Q18/Q45): every real animal can be hit by the player's weapons (Combat.Strike finds it
    /// through FaunaCombat.Query – animals have no colliders). What a hit means is decided by FaunaCombat.Classify:
    /// food fish are caught (spear), sharks / the monster are repelled, protected animals refuse the hit (haters), the
    /// rest just flees. This part holds the per-animal reaction state (knock-back + flight after a hit).
    public sealed partial class FaunaCreature : IHittable
    {
        /// Seconds left of the flight after a hit / startle, and where to flee to.
        internal float hitFleeT;
        internal Vector3 hitFleeDir;
        /// Accumulated "scare" of a hostile animal (the monster leaves its hunt above a threshold).
        internal float scare;

        void ResetCombat()
        {
            hitFleeT = 0f;
            hitFleeDir = Vector3.zero;
            scare = 0f;
        }

        // ------------------------------------------------------------------ IHittable
        public bool CanBeHit => alive && !pendingDespawn && !hallucination && species != null && !species.meme && gameObject.activeInHierarchy;

        /// Runs on the attacker's machine: on the host / offline for the local player's weapon (guests reach the host
        /// through FaunaCombat's hit command, their proxies implement IHittable themselves).
        public void OnHit(in HitInfo hit)
        {
            FaunaCombat.LocalHitOnCreature(this, hit);
        }

        /// Makes the animal flee for a while (knock-back impulse along dir). Used after hits and by FaunaCombat.
        internal void StartHitFlee(Vector3 dir, float seconds, float impulse)
        {
            dir.y *= 0.4f;
            if (dir.sqrMagnitude < 1e-4f) dir = -tr.forward;
            hitFleeDir = dir.normalized;
            hitFleeT = Mathf.Max(hitFleeT, seconds);
            if (impulse > 0f && mode != FaunaMode.Fly) velocity += hitFleeDir * impulse;
        }

        /// The flight after a hit replaces the behavior tick (directors still win). Returns true while fleeing.
        bool TickHitFlee(float dt)
        {
            if (hitFleeT <= 0f) return false;
            if (mode == FaunaMode.Fly) { hitFleeT = 0f; return false; }   // birds take off through Poke instead
            hitFleeT -= dt;
            float speed = Mathf.Max(cruise * 3.2f, mode == FaunaMode.Ground ? 0.5f : 1.4f);
            if (species != null && species.monster) speed = Mathf.Max(cruise * 2f, 3f);
            Vector3 d = hitFleeDir;
            if (mode == FaunaMode.Swim)
            {
                // away and a little down (into the depth / the reef), never through the surface
                d.y = Mathf.Min(d.y, 0.1f) - 0.15f;
                relaxBand = true;
            }
            desiredVel = d.normalized * speed;
            accelMul = 3.5f;
            turnMul = 3f;
            activityTarget = 1f;
            return true;
        }
    }
}
