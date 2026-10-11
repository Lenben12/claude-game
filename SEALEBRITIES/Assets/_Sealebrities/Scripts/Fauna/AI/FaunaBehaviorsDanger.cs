using UnityEngine;

namespace Sealebrities
{
    static class FaunaDanger
    {
        /// 0 (snorkeler) .. 1 (abyss) normalized aggression.
        public static float Aggr01 => Mathf.Clamp01((FaunaContext.Aggression - 0.3f) / 1.7f);

        /// Bumps the focused diver: the local player directly, a remote co-op diver through the Net module.
        public static void Bump(float minAir, float maxAir)
        {
            float air = Mathf.Lerp(minAir, maxAir, Aggr01);
            if (FaunaContext.FocusLocal) GameEvents.RaisePlayerBumped(air);
            else FaunaContext.RemoteBump?.Invoke(FaunaContext.FocusNetId, air);
        }
    }

    /// Predators (barracuda, hammerhead): patrol; when the diver is within 8 m they roll against the difficulty's
    /// predatorAggression and may charge. A hit within 1.6 m bumps the diver (air loss 8–18 s) and the predator retreats.
    /// 25 s cooldown after a bump.
    public sealed class FaunaPredatorBehavior : FaunaBehavior
    {
        enum St { Patrol, Charge, Retreat }
        readonly FaunaWanderer w = new FaunaWanderer();
        St st;
        float timer, cooldown, rollTimer;
        bool wasInside;

        public override void Reset()
        {
            w.Configure(WanderMin(c.lengthM) + 4f, WanderMax(c.lengthM) + 10f, 0.15f, 0f);
            w.Reset();
            st = St.Patrol;
            cooldown = Random.Range(4f, 10f);
            rollTimer = 0f;
            wasInside = false;
        }

        public override void Tick(float dt)
        {
            cooldown -= dt;
            timer -= dt;
            float d = c.distToPlayer;
            bool attackable = FaunaContext.PlayerAttackable;

            switch (st)
            {
                case St.Patrol:
                    {
                        c.desiredVel = w.Tick(c, dt, 0.9f);
                        c.activityTarget = 0.25f;
                        bool inside = attackable && d < 8f;
                        if (attackable && d < 6f && d > 0.01f)
                        {
                            // menacing: keep an eye on the diver while cruising past
                            c.hasFaceDir = true;
                            c.faceDir = Vector3.Lerp(c.velocity.sqrMagnitude > 0.01f ? c.velocity.normalized : c.Transform.forward, c.ToPlayer / d, 0.35f);
                        }
                        if (inside && cooldown <= 0f)
                        {
                            if (!wasInside) rollTimer = 0f;   // roll immediately when the diver enters the 8 m ring
                            rollTimer -= dt;
                            if (rollTimer <= 0f)
                            {
                                rollTimer = 3f;
                                float chance = Mathf.Clamp01(0.3f * FaunaContext.Aggression) * (1f + 0.3f * EnvState.Night01);
                                if (Random.value < chance)
                                {
                                    st = St.Charge;
                                    timer = 4.5f;
                                }
                            }
                        }
                        wasInside = inside;
                        break;
                    }

                case St.Charge:
                    {
                        c.relaxBand = true;
                        Vector3 head = c.HeadPos;
                        Vector3 to = FaunaContext.PlayerPos - head;
                        float hd = to.magnitude;
                        float chargeSpeed = Mathf.Clamp(c.cruise * 3.2f, 3.5f, 6.5f);
                        c.desiredVel = hd > 1e-3f ? to * (chargeSpeed / hd) : c.Transform.forward * chargeSpeed;
                        c.accelMul = 3f;
                        c.turnMul = 2.5f;
                        c.activityTarget = 1f;
                        if (hd < 1.6f)
                        {
                            FaunaDanger.Bump(8f, 18f);
                            Retreat(4f, 25f);
                        }
                        else if (timer <= 0f || d > 16f || !attackable)
                        {
                            Retreat(2.5f, 12f);
                        }
                        break;
                    }

                case St.Retreat:
                    {
                        c.relaxBand = true;
                        c.desiredVel = c.FleeFromPlayer(Mathf.Max(c.cruise * 1.8f, 1.2f), 0.25f);
                        c.accelMul = 2f;
                        c.turnMul = 2f;
                        c.activityTarget = 0.75f;
                        if (timer <= 0f)
                        {
                            st = St.Patrol;
                            w.Pick(c);
                        }
                        break;
                    }
            }
        }

        void Retreat(float seconds, float cool)
        {
            st = St.Retreat;
            timer = seconds;
            cooldown = cool;
            wasInside = false;
        }
    }

    /// Moray eel: lives in a hole (home point on the wreck / rocks), small head movements, stares at close divers,
    /// occasionally shifts a little. Lunges with a small bump when the diver's head comes within 1.8 m.
    public sealed class FaunaMorayBehavior : FaunaBehavior
    {
        enum St { Idle, Shift, Lunge, Return }
        St st;
        float timer, cooldown, shiftTimer, seed, baseYaw;
        bool bumped;
        Vector3 shiftTarget;

        public override void Reset()
        {
            // settle onto the nearest solid surface below the spawn point (wreck deck, rock or seafloor)
            Vector3 p = c.pos;
            float surf = FaunaNav.SurfaceBelow(p, 12f);
            Vector3 h = new Vector3(p.x, surf + c.halfHeight + 0.08f, p.z);
            if (h.y < WorldInfo.GroundHeight(h) + c.hardClearance) h.y = WorldInfo.GroundHeight(h) + c.hardClearance;
            c.home = h;
            c.pos = h;
            c.Transform.position = h;
            st = St.Idle;
            cooldown = Random.Range(3f, 8f);
            shiftTimer = Random.Range(12f, 30f);
            seed = Random.Range(0f, 40f);
            Vector3 f = c.Transform.forward;
            baseYaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
        }

        public override void Tick(float dt)
        {
            cooldown -= dt;
            timer -= dt;
            c.relaxBand = true;
            Vector3 head = c.HeadPos;
            float hd = (FaunaContext.PlayerPos - head).magnitude;

            if ((st == St.Idle || st == St.Shift) && FaunaContext.PlayerAttackable && hd < 1.8f && cooldown <= 0f)
            {
                st = St.Lunge;
                timer = 0.45f;
                bumped = false;
            }

            switch (st)
            {
                case St.Idle:
                    {
                        c.desiredVel = Vector3.ClampMagnitude((c.home - c.pos) * 1.5f, c.cruise);
                        c.hasFaceDir = true;
                        if (FaunaContext.HasPlayer && c.distToPlayer < 6f) c.faceDir = c.ToPlayer;   // the stare
                        else
                        {
                            // small looking-around around its resting heading (the animator adds head sway)
                            float a = baseYaw + (Mathf.PerlinNoise(FaunaContext.Time * 0.12f, seed) - 0.5f) * 100f;
                            c.faceDir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                        }
                        c.turnMul = 0.6f;
                        c.activityTarget = 0.2f;
                        shiftTimer -= dt;
                        if (shiftTimer <= 0f)
                        {
                            Vector2 r = Random.insideUnitCircle * 2.5f;
                            shiftTarget = new Vector3(c.home.x + r.x, 0f, c.home.z + r.y);
                            shiftTarget.y = FaunaNav.SurfaceBelow(new Vector3(shiftTarget.x, c.home.y + 1.5f, shiftTarget.z), 6f) + c.halfHeight + 0.08f;
                            st = St.Shift;
                            timer = 8f;
                        }
                        break;
                    }

                case St.Shift:
                    c.desiredVel = c.Seek(shiftTarget, c.cruise * 0.6f, 0.4f);
                    c.activityTarget = 0.3f;
                    if ((shiftTarget - c.pos).sqrMagnitude < 0.09f || timer <= 0f)
                    {
                        if (FaunaNav.DistXZ(shiftTarget, c.home) < 3f) c.home = c.pos;
                        st = St.Idle;
                        shiftTimer = Random.Range(15f, 35f);
                    }
                    break;

                case St.Lunge:
                    {
                        Vector3 to = FaunaContext.PlayerPos - c.pos;
                        float m = to.magnitude;
                        c.desiredVel = m > 1e-3f ? to * (4.5f / m) : Vector3.zero;
                        c.hasFaceDir = true;
                        c.faceDir = to;
                        c.accelMul = 20f;
                        c.turnMul = 4f;
                        c.activityTarget = 1f;
                        if (!bumped && hd < 1.0f) { bumped = true; FaunaDanger.Bump(3f, 7f); }
                        if (timer <= 0f)
                        {
                            if (!bumped && hd < 2.2f) FaunaDanger.Bump(3f, 7f);
                            st = St.Return;
                            timer = 3f;
                            cooldown = 25f;
                        }
                        break;
                    }

                case St.Return:
                    c.desiredVel = c.Seek(c.home, Mathf.Max(c.cruise * 1.5f, 0.6f), 0.5f);
                    c.activityTarget = 0.6f;
                    if ((c.home - c.pos).sqrMagnitude < 0.04f || timer <= 0f) st = St.Idle;
                    break;
            }
        }
    }

    /// Anglerfish: hovers near the bottom in the dark, flickering lure, stares at the diver, snaps (no bump) when
    /// the diver comes very close.
    public sealed class FaunaAnglerBehavior : FaunaBehavior
    {
        readonly FaunaWanderer w = new FaunaWanderer();
        float snapTimer, cooldown;

        public override void Reset()
        {
            w.Configure(2f, 8f, 0.75f, 0.6f);
            w.pauseRange = new Vector2(4f, 10f);
            w.Reset();
            c.leashCenter = c.pos;
            c.leashRadius = 15f;
            snapTimer = 0f;
            cooldown = Random.Range(2f, 6f);
        }

        public override void Tick(float dt)
        {
            cooldown -= dt;
            c.lureMul = 1f;
            if (snapTimer > 0f)
            {
                snapTimer -= dt;
                Vector3 to = c.ToPlayer;
                float m = to.magnitude;
                c.relaxBand = true;
                c.desiredVel = snapTimer > 0.15f && m > 1e-3f ? to * (3f / m) : -to * (1.2f / Mathf.Max(m, 1e-3f));
                c.hasFaceDir = true;
                c.faceDir = to;
                c.accelMul = 25f;
                c.turnMul = 4f;
                c.activityTarget = 1f;
                c.lureMul = 2.5f;
                return;
            }

            c.desiredVel = w.Tick(c, dt, 0.6f);
            c.activityTarget = 0.2f;
            if (FaunaContext.HasPlayer && c.distToPlayer < 7f)
            {
                c.hasFaceDir = true;
                c.faceDir = c.ToPlayer;
                c.activityTarget = 0.3f;
                c.lureMul = 1.4f;
            }
            if (FaunaContext.HasPlayer && FaunaContext.PlayerUnderwater && cooldown <= 0f && c.distToPlayer < 1.8f + c.lengthM * 0.5f)
            {
                snapTimer = 0.45f;
                cooldown = 10f;
            }
        }
    }
}
