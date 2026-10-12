using UnityEngine;

namespace Sealebrities
{
    static class FaunaJump
    {
        /// Launches a swimmer out of the water (dolphin leap, whale breach, flying-fish glide).
        public static void Launch(FaunaCreature c, Vector3 horizontalDir, float forward, float up)
        {
            Vector3 f = FaunaNav.FlatDir(horizontalDir);
            if (f == Vector3.zero) f = FaunaNav.FlatDir(c.Transform.forward);
            c.velocity = f * forward + Vector3.up * up;
        }

        /// Keeps the creature ballistic while it is in the air; returns false once it is back under water.
        public static bool Airborne(FaunaCreature c, ref float t, float dt, float maxTime)
        {
            t += dt;
            c.allowAir = true;
            c.desiredVel = c.velocity;
            c.accelMul = 0f;
            c.hasFaceDir = true;
            c.faceDir = c.velocity.sqrMagnitude > 0.01f ? c.velocity : c.Transform.forward;
            c.freePitch = true;
            c.turnMul = 2.5f;
            c.activityTarget = 1f;
            bool under = c.pos.y < c.surfaceY - 0.4f && c.velocity.y < 0f;
            return !(under && t > 0.3f) && t < maxTime;
        }
    }

    /// Bottlenose dolphins (G.1): pods (FaunaSchool) that travel fast near the surface, leap out of the water now and
    /// then and come to circle divers for a while, clicking (activity). Bow riding is a director (FaunaBowRiders).
    public sealed class FaunaDolphinBehavior : FaunaBehavior
    {
        readonly FaunaWanderer w = new FaunaWanderer();
        enum St { Travel, Circle, Leave }
        St st;
        Vector3 slot;
        float timer, cooldown, jumpTimer, airT, orbit, orbitDir, radius;
        bool jumping;

        public override void Reset()
        {
            w.Configure(15f, 45f, 0f, 0f);
            w.Reset();
            slot = Random.insideUnitSphere * 3.5f;
            slot.y *= 0.3f;
            st = St.Travel;
            cooldown = Random.Range(3f, 12f);
            jumpTimer = Random.Range(4f, 14f);
            jumping = false;
        }

        public override void Tick(float dt)
        {
            cooldown -= dt;
            timer -= dt;
            jumpTimer -= dt;
            if (jumping)
            {
                if (FaunaJump.Airborne(c, ref airT, dt, 3f)) return;
                jumping = false;
                jumpTimer = Random.Range(5f, 16f);
            }

            // a leap whenever we are fast and close to the surface
            if (st != St.Circle && jumpTimer <= 0f && c.pos.y > c.surfaceY - 2.5f && c.velocity.magnitude > c.cruise * 0.6f)
            {
                jumping = true;
                airT = 0f;
                FaunaJump.Launch(c, c.velocity, Mathf.Max(4f, c.velocity.magnitude * 1.4f), Random.Range(4.5f, 6.5f));
                return;
            }

            float d = c.distToPlayer;
            bool ok = FaunaContext.HasPlayer && FaunaContext.PlayerUnderwater;
            switch (st)
            {
                case St.Travel:
                    {
                        Vector3 desired;
                        var sc = c.school;
                        if (sc != null)
                        {
                            Vector3 tgt = sc.leaderPos + slot;
                            desired = sc.leaderVel * 1.1f + (tgt - c.pos) * 0.8f;
                        }
                        else desired = w.Tick(c, dt, 1.3f);
                        // stay near the surface: dolphins breathe
                        desired.y += Mathf.Clamp((c.surfaceY - 2f - c.pos.y) * 0.3f, -1f, 1f);
                        float max = c.cruise * 2.2f;
                        if (desired.magnitude > max) desired = desired.normalized * max;
                        c.desiredVel = desired;
                        c.accelMul = 2f;
                        c.activityTarget = 0.45f;
                        if (ok && cooldown <= 0f && d < 18f)
                        {
                            st = St.Circle;
                            timer = Random.Range(14f, 24f);
                            orbitDir = Random.value < 0.5f ? -1f : 1f;
                            Vector3 off = c.pos - FaunaContext.PlayerPos;
                            orbit = Mathf.Atan2(off.x, off.z);
                            radius = Random.Range(3.5f, 6f);
                        }
                        break;
                    }
                case St.Circle:
                    {
                        c.relaxBand = true;
                        orbit += dt * orbitDir * Mathf.Max(0.6f, c.cruise * 1.2f) / radius;
                        Vector3 center = FaunaContext.PlayerPos;
                        Vector3 tgt = center + new Vector3(Mathf.Sin(orbit), 0f, Mathf.Cos(orbit)) * radius + Vector3.up * (Mathf.Sin(orbit * 2f + c.slot) * 1.2f);
                        Vector3 tangent = new Vector3(Mathf.Cos(orbit), 0f, -Mathf.Sin(orbit)) * orbitDir;
                        c.desiredVel = c.Seek(tgt, c.cruise * 1.8f, 1.5f) + tangent * c.cruise;
                        c.accelMul = 3f;
                        c.turnMul = 2f;
                        c.activityTarget = 0.9f;
                        if (timer <= 0f || !ok || d > 30f) { st = St.Leave; timer = 6f; cooldown = Random.Range(40f, 80f); }
                        break;
                    }
                case St.Leave:
                    c.desiredVel = c.FleeFromPlayer(c.cruise * 1.5f, 0.2f);
                    c.activityTarget = 0.5f;
                    if (timer <= 0f) st = St.Travel;
                    break;
            }
        }
    }

    /// Humpback "Big Bertha" (G.1): slow cruise in her home area, comes up to breathe, passes curious divers at a
    /// respectful distance and breaches about once per in-game hour when a diver or the boat is within 160 m.
    public sealed class FaunaWhaleBehavior : FaunaBehavior
    {
        readonly FaunaWanderer w = new FaunaWanderer();
        enum St { Cruise, Breathe, BreachDive, BreachRise, Air, Pass }
        St st;
        float timer, breatheTimer, breachTimer, airT;
        Vector3 passTarget;

        /// Seconds between breaches (one in-game hour = DayLengthSeconds / 24).
        static float BreachInterval => GameManager.DayLengthSeconds / 24f;

        public override void Reset()
        {
            w.Configure(30f, 90f, 0f, 0f);
            w.Reset();
            st = St.Cruise;
            breatheTimer = Random.Range(30f, 70f);
            breachTimer = Random.Range(20f, BreachInterval);
        }

        bool SomeoneNear(float r)
        {
            if (FaunaContext.HasPlayer && c.distXZ < r) return true;
            return BoatTelemetry.Valid && FaunaNav.DistXZ(BoatTelemetry.Position, c.pos) < r;
        }

        public override void Tick(float dt)
        {
            timer -= dt;
            breatheTimer -= dt;
            breachTimer -= dt;
            switch (st)
            {
                case St.Cruise:
                    {
                        Vector3 v = w.Tick(c, dt, 1f);
                        float wantY = c.surfaceY - Mathf.Lerp(8f, 18f, Mathf.PerlinNoise(FaunaContext.Time * 0.02f, c.slot));
                        v.y += Mathf.Clamp((wantY - c.pos.y) * 0.15f, -0.5f, 0.5f);
                        c.desiredVel = v;
                        c.activityTarget = 0.3f;
                        if (breatheTimer <= 0f) { st = St.Breathe; timer = 14f; }
                        else if (breachTimer <= 0f && SomeoneNear(160f)) { st = St.BreachDive; timer = 10f; }
                        else if (FaunaContext.HasPlayer && FaunaContext.PlayerUnderwater && c.distToPlayer < 35f && c.distToPlayer > 12f && Random.value < dt * 0.05f)
                        {
                            // a slow, close pass in front of the diver (8 m off)
                            Vector3 side = Vector3.Cross(Vector3.up, FaunaNav.FlatDir(FaunaContext.CamForward));
                            passTarget = FaunaContext.PlayerPos + FaunaNav.FlatDir(FaunaContext.CamForward) * 9f + side * (c.slot % 2 == 0 ? 25f : -25f);
                            st = St.Pass;
                            timer = 25f;
                        }
                        break;
                    }
                case St.Breathe:
                    {
                        // up to the surface, a few seconds blowing, back down
                        Vector3 fwd = FaunaNav.FlatDir(c.Transform.forward);
                        float wantY = c.surfaceY - c.halfHeight * 0.6f;
                        c.desiredVel = fwd * (c.cruise * 0.6f) + Vector3.up * Mathf.Clamp((wantY - c.pos.y) * 0.4f, -0.8f, 1.2f);
                        c.relaxBand = true;
                        c.activityTarget = c.pos.y > wantY - 1f ? 0.6f : 0.35f;
                        if (timer <= 0f) { st = St.Cruise; breatheTimer = Random.Range(45f, 90f); }
                        break;
                    }
                case St.BreachDive:
                    {
                        Vector3 fwd = FaunaNav.FlatDir(c.Transform.forward);
                        float wantY = Mathf.Max(c.groundY + c.halfHeight + 3f, c.surfaceY - 22f);
                        c.desiredVel = fwd * (c.cruise * 0.6f) + Vector3.up * Mathf.Clamp((wantY - c.pos.y) * 0.4f, -1.5f, 0.5f);
                        c.relaxBand = true;
                        if (c.pos.y < wantY + 2f || timer <= 0f) { st = St.BreachRise; timer = 12f; }
                        break;
                    }
                case St.BreachRise:
                    {
                        Vector3 fwd = FaunaNav.FlatDir(c.Transform.forward);
                        c.desiredVel = fwd * 2f + Vector3.up * 8f;
                        c.accelMul = 3f;
                        c.relaxBand = true;
                        c.allowAir = true;
                        c.hasFaceDir = true;
                        c.faceDir = c.desiredVel;
                        c.freePitch = true;
                        c.activityTarget = 1f;
                        if (c.pos.y > c.surfaceY - c.lengthM * 0.25f)
                        {
                            st = St.Air;
                            airT = 0f;
                            FaunaJump.Launch(c, fwd, 2.5f, Mathf.Max(7f, c.velocity.y));
                            FaunaFx.Splash(new Vector3(c.pos.x, c.surfaceY, c.pos.z), c.lengthM * 0.25f);
                        }
                        else if (timer <= 0f) st = St.Cruise;
                        break;
                    }
                case St.Air:
                    if (!FaunaJump.Airborne(c, ref airT, dt, 4f))
                    {
                        st = St.Cruise;
                        breachTimer = BreachInterval * Random.Range(0.85f, 1.15f);
                        FaunaFx.Splash(new Vector3(c.pos.x, c.surfaceY, c.pos.z), c.lengthM * 0.35f);
                    }
                    break;
                case St.Pass:
                    c.desiredVel = c.Seek(passTarget, c.cruise * 0.8f, 6f);
                    c.relaxBand = true;
                    c.activityTarget = 0.5f;
                    if (timer <= 0f || (passTarget - c.pos).sqrMagnitude < 25f) { st = St.Cruise; w.Pick(c); }
                    break;
            }
        }
    }

    /// Goblin shark (G.1): hovers in the dark near the trench floor, stares, and shoots its jaw out at a diver who
    /// comes within 3 m (scare + small bump), then backs off.
    public sealed class FaunaGoblinBehavior : FaunaBehavior
    {
        readonly FaunaWanderer w = new FaunaWanderer();
        float lungeT = -1f, cooldown, retreatT;
        bool bumped;

        public override void Reset()
        {
            w.Configure(3f, 12f, 0.7f, 0.45f);
            w.pauseRange = new Vector2(3f, 8f);
            w.Reset();
            cooldown = Random.Range(4f, 9f);
            lungeT = -1f;
            retreatT = 0f;
        }

        public override void Tick(float dt)
        {
            cooldown -= dt;
            if (lungeT >= 0f)
            {
                lungeT += dt;
                Vector3 to = c.ToPlayer;
                float m = to.magnitude;
                c.relaxBand = true;
                c.desiredVel = lungeT < 0.35f && m > 1e-3f ? to * (2.5f / m) : Vector3.zero;
                c.hasFaceDir = true;
                c.faceDir = to;
                c.accelMul = 12f;
                c.turnMul = 3f;
                c.activityTarget = 1f;
                Action(lungeT < 0.9f ? Mathf.Clamp01(lungeT / 0.18f) : Mathf.Clamp01(1f - (lungeT - 0.9f) / 0.4f));
                if (!bumped && lungeT < 0.6f && (FaunaContext.PlayerPos - c.HeadPos).magnitude < 1.4f + c.lengthM * 0.2f)
                {
                    bumped = true;
                    FaunaDanger.Bump(2f, 5f);
                }
                if (lungeT > 1.3f) { lungeT = -1f; retreatT = 3f; cooldown = 18f; }
                return;
            }
            Action(0f);
            if (retreatT > 0f)
            {
                retreatT -= dt;
                c.desiredVel = c.FleeFromPlayer(Mathf.Max(c.cruise * 1.5f, 0.8f), 0.2f);
                c.relaxBand = true;
                c.activityTarget = 0.6f;
                return;
            }
            c.desiredVel = w.Tick(c, dt, 0.7f);
            c.activityTarget = 0.2f;
            if (FaunaContext.HasPlayer && c.distToPlayer < 9f)
            {
                c.hasFaceDir = true;
                c.faceDir = c.ToPlayer;
                c.activityTarget = 0.35f;
            }
            if (FaunaContext.PlayerAttackable && cooldown <= 0f && c.distToPlayer < 3f + c.lengthM * 0.3f)
            {
                lungeT = 0f;
                bumped = false;
            }
        }
    }

    /// Gulper eel (G.1): drifts in the deep with its glowing tail tip; when a diver comes within 4 m its mouth pouch
    /// balloons to 3× and slowly deflates again.
    public sealed class FaunaGulperBehavior : FaunaBehavior
    {
        readonly FaunaWanderer w = new FaunaWanderer();
        float mouth, holdT, cooldown;

        public override void Reset()
        {
            w.Configure(3f, 10f, 0.2f, 0.4f);
            w.pauseRange = new Vector2(2f, 6f);
            w.Reset();
            mouth = 0f;
            holdT = 0f;
            cooldown = Random.Range(1f, 4f);
        }

        public override void Tick(float dt)
        {
            cooldown -= dt;
            if (holdT <= 0f && cooldown <= 0f && FaunaContext.HasPlayer && c.distToPlayer < 4f + c.lengthM)
            {
                holdT = 2.2f;
                cooldown = 6f;
            }
            if (holdT > 0f)
            {
                holdT -= dt;
                mouth = Mathf.MoveTowards(mouth, 1f, dt * 1.8f);
                c.hasFaceDir = true;
                c.faceDir = c.ToPlayer;
                c.desiredVel = Vector3.zero;
                c.activityTarget = 0.8f;
            }
            else
            {
                mouth = Mathf.MoveTowards(mouth, 0.08f + 0.05f * Mathf.Sin(FaunaContext.Time * 0.7f + c.slot), dt * 0.5f);
                c.desiredVel = w.Tick(c, dt, 0.6f);
                c.activityTarget = 0.2f;
            }
            Action(mouth);
        }
    }

    /// Flying fish (L.1): skittish near the surface; when something comes close (diver, boat) or just for fun they
    /// burst out of the water and glide 20–40 m on their wing fins.
    public sealed class FaunaFlyingFishBehavior : FaunaBehavior
    {
        readonly FaunaWanderer w = new FaunaWanderer();
        float glideTimer, airT;
        bool gliding;

        public override void Reset()
        {
            w.Configure(5f, 18f, 0f, 0.1f);
            w.Reset();
            glideTimer = Random.Range(6f, 25f);
            gliding = false;
        }

        public override void Tick(float dt)
        {
            glideTimer -= dt;
            if (gliding)
            {
                if (FaunaJump.Airborne(c, ref airT, dt, 3.2f))
                {
                    // the glide: hold the height a little above the waves
                    if (c.pos.y > c.surfaceY + 0.3f && c.velocity.y < 0f) c.velocity.y *= 0.92f;
                    return;
                }
                gliding = false;
                glideTimer = Random.Range(8f, 25f);
            }
            bool boatNear = BoatTelemetry.Valid && BoatTelemetry.Speed > 2f && FaunaNav.DistXZ(BoatTelemetry.Position, c.pos) < 14f;
            bool diverNear = FaunaContext.HasPlayer && FaunaContext.PlayerInWater && c.distToPlayer < 5f;
            if ((glideTimer <= 0f || boatNear || diverNear) && c.pos.y > c.surfaceY - 1.6f)
            {
                gliding = true;
                airT = 0f;
                Vector3 dir = boatNear ? c.pos - BoatTelemetry.Position : (diverNear ? c.pos - FaunaContext.PlayerPos : c.Transform.forward);
                FaunaJump.Launch(c, dir, Random.Range(7f, 10f), Random.Range(2.5f, 3.5f));
                return;
            }
            Vector3 v = w.Tick(c, dt, 1.2f);
            v.y += Mathf.Clamp((c.surfaceY - 0.8f - c.pos.y) * 0.8f, -0.6f, 0.6f);
            c.desiredVel = v;
            c.activityTarget = 0.3f;
        }
    }

    /// Slow hoverers (coelacanth "Grandpa Fossil", other drifting fish): drift around their home with long pauses,
    /// turn calmly toward a diver who comes close.
    public sealed class FaunaHoverBehavior : FaunaBehavior
    {
        readonly FaunaWanderer w = new FaunaWanderer();

        public override void Reset()
        {
            w.Configure(2f, 9f, 0.3f, 0.55f);
            w.pauseRange = new Vector2(5f, 12f);
            w.Reset();
            c.home = c.pos;
            if (c.species.unique) { c.leashCenter = c.pos; c.leashRadius = 14f; }
        }

        public override void Tick(float dt)
        {
            if (React(dt)) return;   // round 3: the nautilus / frilled shark back off into the dark
            c.desiredVel = w.Tick(c, dt, 0.6f);
            c.activityTarget = 0.2f;
            if (FaunaContext.HasPlayer && c.distToPlayer < 6f + c.lengthM)
            {
                c.hasFaceDir = true;
                c.faceDir = Vector3.Lerp(c.Transform.forward, c.ToPlayer.normalized, 0.4f);
                c.turnMul = 0.4f;
                c.activityTarget = 0.35f;
            }
            KeepPersonalSpace();
        }
    }
}
