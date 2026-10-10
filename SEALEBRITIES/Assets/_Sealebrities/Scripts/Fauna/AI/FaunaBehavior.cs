using UnityEngine;

namespace Sealebrities
{
    /// Base class of a creature's brain. Each tick it writes the creature's behavior outputs
    /// (desiredVel, faceDir, activityTarget, relaxBand, accelMul, turnMul ...). The creature core does the rest.
    public abstract class FaunaBehavior
    {
        protected FaunaCreature c;

        public virtual bool IsHallucination => false;

        public void Bind(FaunaCreature creature) { c = creature; }

        /// Called on every (re)spawn after the creature was placed.
        public virtual void Reset() { }

        public abstract void Tick(float dt);

        public static FaunaBehavior Create(SpeciesDef s, bool hallucination)
        {
            if (hallucination) return new FaunaHallucinationBehavior();
            switch (s.behavior)
            {
                case BehaviorType.Schooling: return new FaunaSchoolingBehavior();
                case BehaviorType.Skittish: return new FaunaSkittishBehavior();
                case BehaviorType.Curious: return new FaunaCuriousBehavior();
                case BehaviorType.Predator: return new FaunaPredatorBehavior();
                case BehaviorType.Ambush:
                    if (s.plan == BodyPlan.Eel) return new FaunaMorayBehavior();
                    return new FaunaAnglerBehavior();
                case BehaviorType.BottomDweller:
                    if (s.plan == BodyPlan.Seahorse) return new FaunaSeahorseBehavior();
                    return new FaunaRayBehavior();
                case BehaviorType.Drifter:
                    if (s.plan == BodyPlan.VampireSquid) return new FaunaVampireSquidBehavior();
                    if (s.plan == BodyPlan.Sunfish) return new FaunaSunfishBehavior();
                    return new FaunaJellyBehavior();
                default: return new FaunaWanderBehavior();
            }
        }

        // ------------------------------------------------------------------ shared helpers
        protected bool PlayerNearby(float radius)
            => FaunaContext.HasPlayer && FaunaContext.PlayerInWater && c.distToPlayer < radius;

        /// Gentle "personal space": non-aggressive animals slide out of the diver's way.
        protected void KeepPersonalSpace(float extra = 0f)
        {
            float r = 1.4f + c.lengthM * 0.5f + extra;
            if (!PlayerNearby(r)) return;
            float k = 1f - c.distToPlayer / r;
            c.desiredVel += c.FleeFromPlayer(Mathf.Max(c.cruise, 0.4f) * (0.8f + k), 0.2f);
            c.activityTarget = Mathf.Max(c.activityTarget, 0.35f);
        }

        protected static float WanderMin(float len) => Mathf.Clamp(len * 4f, 4f, 20f);
        protected static float WanderMax(float len) => Mathf.Clamp(len * 12f, 10f, 60f);
    }

    /// Reusable target-based wandering inside the leash area and the species' depth band.
    public sealed class FaunaWanderer
    {
        public float minR = 5f, maxR = 25f, bottomBias, pauseChance;
        public Vector2 pauseRange = new Vector2(2f, 6f);

        Vector3 target;
        bool hasTarget;
        float timer, pauseTimer, speedPhase;

        public Vector3 Target => target;
        public bool Paused => pauseTimer > 0f;

        public void Configure(float min, float max, float bottom, float pause)
        {
            minR = min; maxR = Mathf.Max(min + 0.5f, max); bottomBias = bottom; pauseChance = pause;
        }

        public void Reset()
        {
            hasTarget = false;
            timer = 0f;
            pauseTimer = 0f;
            speedPhase = Random.value * 10f;
        }

        public Vector3 Tick(FaunaCreature c, float dt, float speedMul)
        {
            speedPhase += dt * 0.7f;
            if (pauseTimer > 0f)
            {
                pauseTimer -= dt;
                return Vector3.zero;
            }
            timer -= dt;
            float arrive = Mathf.Max(1f, c.lengthM * 1.5f);
            if (!hasTarget || timer <= 0f || (target - c.pos).sqrMagnitude < arrive * arrive)
            {
                if (hasTarget && pauseChance > 0f && Random.value < pauseChance)
                {
                    hasTarget = false;
                    pauseTimer = Random.Range(pauseRange.x, pauseRange.y);
                    return Vector3.zero;
                }
                Pick(c);
            }
            float spd = c.cruise * speedMul * (0.85f + 0.2f * Mathf.Sin(speedPhase));
            return c.Seek(target, spd, arrive);
        }

        public void Pick(FaunaCreature c)
        {
            if (!c.PickWanderTarget(minR, maxR, bottomBias, out target))
            {
                Vector3 toLeash = FaunaNav.FlatDir(c.leashCenter - c.pos);
                if (toLeash == Vector3.zero) toLeash = FaunaNav.FlatDir(c.Transform.forward);
                target = c.pos + toLeash * minR;
            }
            hasTarget = true;
            pauseTimer = 0f;
            timer = Random.Range(8f, 18f) + c.lengthM;
        }

        public void SetTarget(Vector3 t, float seconds)
        {
            target = t;
            hasTarget = true;
            pauseTimer = 0f;
            timer = seconds;
        }
    }
}
