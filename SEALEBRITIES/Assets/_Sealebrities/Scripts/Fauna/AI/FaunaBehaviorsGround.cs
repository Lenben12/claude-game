using UnityEngine;

namespace Sealebrities
{
    /// Crabs (G.1, L.11): scuttle sideways between short targets around their home, pause with claw play, and react:
    ///   ghost crab – sprints away sideways and digs into the sand when someone comes within 3 m (pops out later),
    ///   hermit crab – pulls into its shell when a diver stays within 0.8 m for 3 s (or touches it),
    ///   Sally Lightfoot – skittish, hops from rock to rock,
    ///   coconut crab – slow night walker, raises its claws when someone comes close.
    /// The action channel drives the claws (threat) or the hermit's hiding (gait animator).
    public sealed class FaunaCrabBehavior : FaunaBehavior
    {
        enum St { Walk, Pause, Flee, Hide, Threat }
        St st;
        float timer, closeTime, hideDepth, hopT;
        Vector3 target, fleeDir;
        float sideSign = 1f;
        bool ghost, hermit, flat, coconut;
        float walkSpeed, fleeSpeed;

        public override void Reset()
        {
            string v = c.species.variant ?? "";
            ghost = v == "ghost"; hermit = v == "hermit"; flat = v == "flat"; coconut = v == "coconut";
            c.home = c.pos;
            walkSpeed = Mathf.Max(0.04f, c.cruise) * (hermit ? 1f : 1.6f);
            fleeSpeed = ghost ? Mathf.Clamp(c.lengthM * 22f, 0.9f, 2.4f) : flat ? Mathf.Clamp(c.lengthM * 14f, 0.6f, 1.4f) : Mathf.Max(walkSpeed * 3f, 0.12f);
            st = Random.value < 0.5f ? St.Pause : St.Walk;
            timer = Random.Range(1f, 4f);
            closeTime = 0f;
            hideDepth = 0f;
            c.hasHeightOverride = false;
            PickTarget();
        }

        void PickTarget()
        {
            float r = Mathf.Clamp(c.lengthM * 25f, 0.5f, 4f) * (coconut ? 1.5f : 1f);
            for (int i = 0; i < 4; i++)
            {
                Vector2 o = Random.insideUnitCircle * r;
                Vector3 t = c.home + new Vector3(o.x, 0f, o.y);
                if (c.GroundOk(WorldInfo.GroundHeight(t))) { target = t; return; }
            }
            target = c.home;
        }

        float ThreatDistance()
        {
            if (!FaunaContext.HasPlayer) return 999f;
            // above water: someone walking on the beach (feet); under water: the diver's head
            bool crabDry = c.pos.y > c.surfaceY - 0.2f;
            return crabDry ? (FaunaContext.PlayerUnderwater ? 999f : FeetDistXZ) : (FaunaContext.PlayerInWater ? c.distToPlayer : 999f);
        }

        public override void Poke(Vector3 from)
        {
            fleeDir = FaunaNav.FlatDir(c.pos - from);
            if (hermit) { st = St.Hide; timer = Random.Range(4f, 6f); }
            else { st = St.Flee; timer = ghost ? 1.2f : 1.6f; }
        }

        public override void Tick(float dt)
        {
            timer -= dt;
            float threat = ThreatDistance();
            c.hasHeightOverride = false;

            // reactions
            if (st != St.Hide && st != St.Flee)
            {
                if (hermit)
                {
                    if (threat < 0.8f + c.lengthM) closeTime += dt; else closeTime = Mathf.Max(0f, closeTime - dt);
                    if (closeTime > 3f) { st = St.Hide; timer = Random.Range(4f, 6f); closeTime = 0f; }
                }
                else if ((ghost || flat) && threat < 3f + c.lengthM)
                {
                    fleeDir = FaunaNav.FlatDir(c.pos - FaunaContext.PlayerRootPos);
                    if (fleeDir == Vector3.zero) fleeDir = c.Transform.right;
                    st = St.Flee;
                    timer = ghost ? 1.2f : 1.4f;
                    hopT = 0f;
                }
                else if (coconut && threat < 2.2f + c.lengthM)
                {
                    st = St.Threat;
                    timer = 2.5f;
                }
            }

            switch (st)
            {
                case St.Walk:
                    {
                        Vector3 to = target - c.pos;
                        to.y = 0f;
                        float d = to.magnitude;
                        if (d < 0.08f + c.lengthM * 0.3f || timer <= 0f)
                        {
                            st = St.Pause;
                            timer = Random.Range(1.5f, hermit ? 6f : 4f);
                            break;
                        }
                        Vector3 dir = to / d;
                        Move(dir, walkSpeed);
                        c.activityTarget = 0.3f;
                        Action(0f);
                        break;
                    }
                case St.Pause:
                    c.desiredVel = Vector3.zero;
                    c.activityTarget = 0.2f;
                    Action(0f);
                    if (timer <= 0f) { PickTarget(); st = St.Walk; timer = Random.Range(3f, 7f); }
                    break;
                case St.Flee:
                    {
                        Move(fleeDir, fleeSpeed);
                        c.accelMul = 6f;
                        c.activityTarget = 0.95f;
                        Action(ghost ? 0.6f : 0f);
                        if (flat)
                        {
                            // Sally Lightfoot hops: a quick arc over the rocks
                            hopT += dt;
                            float hop = Mathf.Abs(Mathf.Sin(hopT * 9f)) * Mathf.Clamp(c.lengthM * 3f, 0.05f, 0.25f);
                            c.hasHeightOverride = true;
                            c.heightOverride = WorldInfo.GroundHeight(c.pos) + c.halfHeight + hop;
                        }
                        if (timer <= 0f)
                        {
                            if (ghost) { st = St.Hide; timer = Random.Range(5f, 8f); }
                            else { st = St.Pause; timer = Random.Range(2f, 4f); c.home = c.pos; }
                        }
                        break;
                    }
                case St.Hide:
                    c.desiredVel = Vector3.zero;
                    c.activityTarget = 0.1f;
                    if (ghost)
                    {
                        // dig in: sink into the sand, peek out again
                        hideDepth = Mathf.MoveTowards(hideDepth, timer > 0.6f ? 1f : 0f, dt * 2.5f);
                        c.hasHeightOverride = true;
                        c.heightOverride = WorldInfo.GroundHeight(c.pos) + c.halfHeight * (1f - 2.2f * hideDepth);
                        Action(0f);
                    }
                    else Action(timer > 0.5f ? 1f : 0f);   // hermit: into the shell
                    if (timer <= 0f && threat > 2f) { st = St.Pause; timer = Random.Range(1f, 3f); c.home = c.pos; hideDepth = 0f; }
                    else if (timer <= 0f) timer = 1.5f;
                    break;
                case St.Threat:
                    c.desiredVel = FaunaNav.FlatDir(c.pos - FaunaContext.PlayerRootPos) * (walkSpeed * 0.6f);
                    c.hasFaceDir = true;
                    c.faceDir = FaunaNav.FlatDir(FaunaContext.PlayerRootPos - c.pos);
                    c.activityTarget = 0.7f;
                    Action(1f);
                    if (timer <= 0f) { st = St.Pause; timer = 2f; }
                    break;
            }
        }

        /// Crabs walk sideways: the body faces 90° from the direction of travel.
        void Move(Vector3 dir, float speed)
        {
            c.desiredVel = dir * speed;
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            Vector3 fwd = c.Transform.forward;
            // keep the side that needs the smaller turn
            if (Vector3.Dot(side, fwd) < 0f) sideSign = -1f; else sideSign = 1f;
            c.hasFaceDir = true;
            c.faceDir = side * sideSign;
            c.turnMul = 1.5f;
        }
    }

    /// Starfish: lie on the seafloor, glide a few centimetres now and then, curl their arms when touched.
    public sealed class FaunaStarfishBehavior : FaunaBehavior
    {
        float timer, curl;
        Vector3 dir;

        public override void Reset()
        {
            timer = Random.Range(5f, 30f);
            dir = Vector3.zero;
            curl = 0f;
        }

        public override void Poke(Vector3 from) { curl = 1f; }

        public override void Tick(float dt)
        {
            timer -= dt;
            if (timer <= 0f)
            {
                if (dir == Vector3.zero)
                {
                    Vector2 r = Random.insideUnitCircle.normalized;
                    dir = new Vector3(r.x, 0f, r.y);
                    timer = Random.Range(6f, 14f);
                }
                else { dir = Vector3.zero; timer = Random.Range(15f, 45f); }
            }
            c.desiredVel = dir * Mathf.Max(0.01f, c.cruise);
            c.hasFaceDir = true;
            c.faceDir = c.Transform.forward;
            c.turnMul = 0.1f;
            curl = Mathf.MoveTowards(curl, 0f, dt * 0.2f);
            c.activityTarget = 0.15f + curl * 0.5f;
        }
    }

    /// Slow walkers on the sand (flamboyant cuttlefish): short walks with pauses, stop and "flash" when a diver comes.
    public sealed class FaunaWalkerBehavior : FaunaBehavior
    {
        readonly FaunaWanderer w = new FaunaWanderer();

        public override void Reset()
        {
            w.Configure(0.6f, 3f, 1f, 0.6f);
            w.pauseRange = new Vector2(2f, 7f);
            w.Reset();
            c.home = c.pos;
            c.leashCenter = c.pos;
            c.leashRadius = 6f;
        }

        public override void Tick(float dt)
        {
            if (PlayerNearby(2.5f + c.lengthM))
            {
                // displays its colours facing the diver
                c.desiredVel = Vector3.zero;
                c.hasFaceDir = true;
                c.faceDir = FaunaNav.FlatDir(c.ToPlayer);
                c.activityTarget = 0.9f;
                return;
            }
            Vector3 v = w.Tick(c, dt, 1f);
            v.y = 0f;
            c.desiredVel = v;
            c.activityTarget = w.Paused ? 0.2f : 0.35f;
        }
    }
}
