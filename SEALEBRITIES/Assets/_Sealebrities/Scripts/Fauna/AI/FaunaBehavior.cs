using UnityEngine;

namespace Sealebrities
{
    /// Base class of a creature's brain. Each tick it writes the creature's behavior outputs
    /// (desiredVel, faceDir, activityTarget, relaxBand, accelMul, turnMul ...). The creature core does the rest.
    public abstract class FaunaBehavior
    {
        protected FaunaCreature c;

        public virtual bool IsHallucination => false;

        public void Bind(FaunaCreature creature) { c = creature; reactT = 0f; temperSet = false; }

        // ------------------------------------------------------------------ round 3: reactions by species (Q21)
        float reactT;
        Vector3 reactFrom;
        FaunaTemper temper;
        bool temperSet;

        /// The species' reaction profile (FaunaReact.TemperOf).
        protected FaunaTemper Temper
        {
            get
            {
                if (!temperSet) { temper = FaunaReact.TemperOf(c.species); temperSet = true; }
                return temper;
            }
        }

        /// Round 3 (GDD §20 Q21 "Flucht je nach Art"): small fish dart away when a diver comes close / rushes in or
        /// something scary happens nearby (FaunaReact.Startle); bottom animals duck down; curious and cool animals do
        /// nothing here. Returns true while fleeing (the outputs are written – the caller skips its own tick).
        protected bool React(float dt)
        {
            var t = Temper;
            if (t != FaunaTemper.Darter && t != FaunaTemper.Hider) return false;
            if (reactT > 0f) reactT -= dt;
            bool trigger = false;
            if (FaunaReact.Startled(c.pos, out Vector3 sf)) { trigger = true; reactFrom = sf; }
            else if (FaunaContext.HasPlayer && FaunaContext.PlayerInWater)
            {
                float r = t == FaunaTemper.Darter ? 2.2f + c.lengthM * 3f : 1.4f + c.lengthM * 1.5f;
                if (FaunaContext.PlayerSpeed > 1.6f) r += 2f;   // rushing in scares everybody
                if (c.distToPlayer < r) { trigger = true; reactFrom = FaunaContext.PlayerPos; }
            }
            if (trigger && reactT <= 0f) reactT = t == FaunaTemper.Darter ? Random.Range(1.2f, 2.2f) : Random.Range(1.5f, 3f);
            if (reactT <= 0f) return false;
            Vector3 away = c.pos - reactFrom;
            if (away.sqrMagnitude < 1e-4f) away = -c.Transform.forward;
            away.Normalize();
            if (t == FaunaTemper.Darter)
            {
                away += new Vector3(Mathf.Sin(FaunaContext.Time * 2.1f + c.slot), Mathf.Sin(FaunaContext.Time * 1.3f + c.slot) * 0.3f, Mathf.Cos(FaunaContext.Time * 1.7f + c.slot)) * 0.25f;
                c.desiredVel = away.normalized * Mathf.Max(c.cruise * 3.2f, 1.5f);
                c.accelMul = 4f;
                c.turnMul = 3f;
                c.activityTarget = 0.95f;
            }
            else
            {
                // hiders back off and press down to the floor (into their crevice)
                away.y = -0.6f;
                c.desiredVel = away.normalized * Mathf.Max(c.cruise * 1.8f, 0.4f);
                c.accelMul = 2.5f;
                c.activityTarget = 0.6f;
            }
            return true;
        }

        /// Called on every (re)spawn after the creature was placed.
        public virtual void Reset() { }

        public abstract void Tick(float dt);

        public static FaunaBehavior Create(SpeciesDef s, bool hallucination)
        {
            if (hallucination) return new FaunaHallucinationBehavior();
            // ---- round 3: the volcano's prehistoric monster (FaunaVolcano)
            if (s.monster) return new FaunaMonsterBehavior();
            // ---- round 2: body plans with their own movement first
            switch (s.plan)
            {
                case BodyPlan.Crab: return new FaunaCrabBehavior();
                case BodyPlan.Starfish: return new FaunaStarfishBehavior();
                case BodyPlan.Dolphin: return new FaunaDolphinBehavior();
                case BodyPlan.Whale: return new FaunaWhaleBehavior();
                case BodyPlan.Bird:
                    return s.behavior == BehaviorType.Soar ? (FaunaBehavior)new FaunaSoarBehavior() : new FaunaPerchBirdBehavior();
                case BodyPlan.Bat: return new FaunaBatBehavior();
                case BodyPlan.Lizard:
                    return s.behavior == BehaviorType.Climb ? (FaunaBehavior)new FaunaClimbBehavior() : new FaunaBaskBehavior();
                case BodyPlan.Quadruped:
                    return s.behavior == BehaviorType.Herd ? (FaunaBehavior)new FaunaHerdBehavior() : new FaunaLandWanderBehavior();
                case BodyPlan.Primate: return new FaunaMonkeyBehavior();
            }
            if (s.variant == "flying") return new FaunaFlyingFishBehavior();
            if (s.variant == "goblin") return new FaunaGoblinBehavior();
            if (s.variant == "gulper") return new FaunaGulperBehavior();
            if (s.variant == "coelacanth") return new FaunaHoverBehavior();
            switch (s.behavior)
            {
                case BehaviorType.Schooling: return new FaunaSchoolingBehavior();
                case BehaviorType.Skittish: return new FaunaSkittishBehavior();
                case BehaviorType.Curious: return new FaunaCuriousBehavior();
                case BehaviorType.Predator: return new FaunaPredatorBehavior();
                case BehaviorType.Crawler: return new FaunaWalkerBehavior();
                case BehaviorType.Playful: return new FaunaDolphinBehavior();
                case BehaviorType.Ambush:
                    if (s.plan == BodyPlan.Eel) return new FaunaMorayBehavior();
                    return new FaunaAnglerBehavior();
                case BehaviorType.BottomDweller:
                    if (s.plan == BodyPlan.Seahorse) return new FaunaSeahorseBehavior();
                    return new FaunaRayBehavior();
                case BehaviorType.Drifter:
                    if (s.plan == BodyPlan.VampireSquid) return new FaunaVampireSquidBehavior();
                    if (s.plan == BodyPlan.Sunfish) return new FaunaSunfishBehavior();
                    if (s.plan == BodyPlan.Jelly) return new FaunaJellyBehavior();
                    return new FaunaHoverBehavior();
                default: return new FaunaWanderBehavior();
            }
        }

        /// Something touched / startled the animal (mischief, a diver right on top of it). Default: nothing.
        public virtual void Poke(Vector3 from) { }

        /// Horizontal distance from the focused diver's feet (land animals: the head is 1.6 m up).
        protected float FeetDistXZ => FaunaNav.DistXZ(c.pos, FaunaContext.PlayerRootPos);

        /// The focused diver stands / walks on land (or on the dock, boat) – land animals react to that.
        protected bool PlayerOnLandNear(float radius) => FaunaContext.HasPlayer && !FaunaContext.PlayerUnderwater && FeetDistXZ < radius
                                                       && Mathf.Abs(FaunaContext.PlayerRootPos.y - c.pos.y) < 4f;

        /// Sets the round-2 action channel (0..1) read by the gait animator (and replicated to co-op proxies).
        protected void Action(float a01) { c.lureMul = 1f + 2f * Mathf.Clamp01(a01); }

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
