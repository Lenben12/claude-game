using UnityEngine;

namespace Sealebrities
{
    /// Wanderer: smooth target wandering in the site / depth band. Species flavour: lionfish and groupers hover
    /// a lot, garibaldi guard a small territory, leopard sharks cruise near the bottom.
    public sealed class FaunaWanderBehavior : FaunaBehavior
    {
        readonly FaunaWanderer w = new FaunaWanderer();
        bool territorial;
        float defendTimer;

        public override void Reset()
        {
            var s = c.species;
            float len = c.lengthM;
            float bottom = 0f, pause = 0f;
            float minR = WanderMin(len), maxR = WanderMax(len);
            territorial = false;
            if (s.variant == "lionfish") { pause = 0.6f; minR = 2f; maxR = 7f; bottom = 0.5f; }
            else if (s.variant == "grouper") { pause = 0.35f; bottom = 0.35f; }
            else if (s.variant == "garibaldi") { territorial = true; pause = 0.3f; minR = 2f; maxR = 7f; c.leashCenter = c.pos; c.leashRadius = 10f; }
            else if (s.plan == BodyPlan.Shark && string.IsNullOrEmpty(s.variant)) bottom = 0.55f;
            else if (s.plan == BodyPlan.Squid) { minR = 15f; maxR = 50f; }
            else if (s.plan == BodyPlan.Fish) pause = 0.12f;
            w.Configure(minR, maxR, bottom, pause);
            w.Reset();
            defendTimer = 0f;
        }

        public override void Tick(float dt)
        {
            if (!territorial && React(dt)) return;   // round 3 (Q21): small fish dart away
            c.desiredVel = w.Tick(c, dt, 1f);
            c.activityTarget = w.Paused ? 0.2f : 0.25f;

            if (territorial)
            {
                // garibaldi: dart at the diver when they come close, then back off
                if (defendTimer > 0f) defendTimer -= dt;
                if (PlayerNearby(3.5f) && defendTimer <= 0f) defendTimer = 1.6f;
                if (defendTimer > 0.8f)
                {
                    Vector3 to = c.ToPlayer;
                    float d = to.magnitude;
                    if (d > 0.9f) c.desiredVel = to * (Mathf.Max(c.cruise * 2.5f, 0.8f) / Mathf.Max(d, 1e-3f));
                    c.hasFaceDir = true;
                    c.faceDir = to;
                    c.accelMul = 3f;
                    c.activityTarget = 0.6f;
                    return;
                }
            }
            KeepPersonalSpace();
        }
    }

    /// Skittish: wander, flee when the diver gets within 3 m + body size (wider when the diver rushes in).
    public sealed class FaunaSkittishBehavior : FaunaBehavior
    {
        readonly FaunaWanderer w = new FaunaWanderer();
        float fleeTimer;

        public override void Reset()
        {
            w.Configure(WanderMin(c.lengthM), WanderMax(c.lengthM), 0.3f, 0.25f);
            w.Reset();
            fleeTimer = 0f;
        }

        public override void Tick(float dt)
        {
            float r = 3f + c.lengthM + (FaunaContext.PlayerSpeed > 2f ? 2f : 0f);
            if (PlayerNearby(r) || FaunaReact.Startled(c.pos, out _)) fleeTimer = Mathf.Max(fleeTimer, Random.Range(2f, 3.5f));

            if (fleeTimer > 0f)
            {
                fleeTimer -= dt;
                c.desiredVel = c.FleeFromPlayer(Mathf.Max(c.cruise * 3f, 1.6f), 0.35f);
                c.accelMul = 3.5f;
                c.turnMul = 2.5f;
                c.activityTarget = 0.9f;
                if (fleeTimer <= 0f) w.Pick(c);
                return;
            }
            c.desiredVel = w.Tick(c, dt, 1f);
            c.activityTarget = 0.25f;
        }
    }

    /// Rays: glide just above the seafloor, rest in the sand now and then, take off when approached.
    public sealed class FaunaRayBehavior : FaunaBehavior
    {
        enum St { Glide, Rest, TakeOff }
        readonly FaunaWanderer w = new FaunaWanderer();
        St st;
        float timer;

        public override void Reset()
        {
            w.Configure(Mathf.Max(4f, c.lengthM * 6f), Mathf.Max(14f, c.lengthM * 20f), 1f, 0f);
            w.Reset();
            st = Random.value < 0.4f ? St.Rest : St.Glide;
            timer = st == St.Rest ? Random.Range(4f, 12f) : Random.Range(10f, 25f);
        }

        public override void Tick(float dt)
        {
            timer -= dt;
            if (st != St.TakeOff && (PlayerNearby(3.5f + c.lengthM) || (c.lengthM < 2.5f && FaunaReact.Startled(c.pos, out _))))
            {
                st = St.TakeOff;
                timer = Random.Range(2.5f, 3.5f);
            }

            float floorY = c.groundY + c.clearance;
            switch (st)
            {
                case St.Glide:
                    c.desiredVel = w.Tick(c, dt, 1f);
                    c.desiredVel.y += Mathf.Clamp((floorY - c.pos.y) * 0.8f, -0.5f, 0.5f);
                    c.activityTarget = 0.25f;
                    if (timer <= 0f) { st = St.Rest; timer = Random.Range(5f, 15f); }
                    break;

                case St.Rest:
                    {
                        float restY = c.groundY + c.halfHeight + 0.04f;
                        c.desiredVel = new Vector3(0f, Mathf.Clamp((restY - c.pos.y) * 0.8f, -0.3f, 0.3f), 0f);
                        c.relaxBand = true;
                        c.activityTarget = 0.15f;
                        if (timer <= 0f) { st = St.Glide; timer = Random.Range(10f, 25f); w.Pick(c); }
                        break;
                    }

                case St.TakeOff:
                    {
                        Vector3 away = c.FleeFromPlayer(Mathf.Max(c.cruise * 3f, 1.5f), 0.2f);
                        away.y = Mathf.Abs(away.y) * 0.3f + 0.6f;
                        c.desiredVel = away;
                        c.relaxBand = true;
                        c.accelMul = 3f;
                        c.turnMul = 2f;
                        c.activityTarget = 0.85f;
                        if (timer <= 0f) { st = St.Glide; timer = Random.Range(8f, 20f); w.Pick(c); }
                        break;
                    }
            }
        }
    }

    /// Seahorse: almost static next to its seagrass home, gentle sway, slowly turns away from a close diver.
    public sealed class FaunaSeahorseBehavior : FaunaBehavior
    {
        float t, seed;

        public override void Reset()
        {
            t = 0f;
            seed = Random.Range(0f, 50f);
            c.home = c.pos;
        }

        public override void Tick(float dt)
        {
            t += dt;
            Vector3 sway = new Vector3(Mathf.Sin(t * 0.3f + seed) * 0.25f, Mathf.Sin(t * 0.5f + seed) * 0.08f, Mathf.Cos(t * 0.27f + seed) * 0.25f);
            Vector3 target = c.home + sway;
            target.y = Mathf.Max(target.y, c.groundY + c.hardClearance + 0.05f);
            c.desiredVel = c.Seek(target, c.cruise, 0.4f);
            c.relaxBand = true;
            c.hasFaceDir = true;
            c.faceDir = new Vector3(Mathf.Sin(t * 0.08f + seed), 0f, Mathf.Cos(t * 0.08f + seed));
            c.activityTarget = 0.2f;
            if (PlayerNearby(1.5f + c.lengthM))
            {
                Vector3 away = FaunaNav.FlatDir(c.pos - FaunaContext.PlayerPos);
                if (away != Vector3.zero) c.faceDir = away;
                c.turnMul = 0.5f;
                c.activityTarget = 0.3f;
            }
        }
    }

    /// Jellyfish: slow drift with the current, rhythmic bell pulses push it upward, it sinks in between.
    public sealed class FaunaJellyBehavior : FaunaBehavior
    {
        float pulseT, period, driftTimer;
        Vector3 drift;

        public override void Reset()
        {
            period = Mathf.Lerp(1.4f, 3.2f, Mathf.Clamp01(c.lengthM / 0.5f)) * Random.Range(0.85f, 1.15f);
            pulseT = Random.value;
            NewDrift();
        }

        void NewDrift()
        {
            Vector2 r = Random.insideUnitCircle.normalized;
            drift = new Vector3(r.x, 0f, r.y) * (c.cruise * Random.Range(0.4f, 0.8f));
            driftTimer = Random.Range(8f, 16f);
        }

        public override void Tick(float dt)
        {
            float p = period;
            if (PlayerNearby(2f + c.lengthM)) p *= 0.7f;     // nervous pulsing near the diver
            pulseT += dt / p;
            if (pulseT >= 1f) pulseT -= 1f;
            c.bellPulse = pulseT < 0.22f ? Mathf.Sin(pulseT / 0.22f * Mathf.PI * 0.5f) : 1f - Mathf.Clamp01((pulseT - 0.22f) / 0.55f);
            float thrust = pulseT < 0.3f ? 1f : 0f;

            driftTimer -= dt;
            if (driftTimer <= 0f) NewDrift();

            c.desiredVel = drift + Vector3.up * (thrust * c.cruise * 2.4f - 0.035f);
            c.accelMul = 2f;
            c.hasFaceDir = true;
            c.faceDir = drift;
            c.turnMul = 0.15f;
            c.activityTarget = 0.2f;
        }
    }

    /// Ocean sunfish: slow wandering with sculling fins; near the surface it sometimes lies on its side to bask.
    public sealed class FaunaSunfishBehavior : FaunaBehavior
    {
        readonly FaunaWanderer w = new FaunaWanderer();
        float baskTimer, nextBask;

        public override void Reset()
        {
            w.Configure(10f, 35f, 0f, 0.2f);
            w.Reset();
            baskTimer = 0f;
            nextBask = Random.Range(15f, 45f);
        }

        public override void Tick(float dt)
        {
            nextBask -= dt;
            if (baskTimer <= 0f && nextBask <= 0f && c.pos.y > -18f)
            {
                baskTimer = Random.Range(10f, 20f);
                nextBask = Random.Range(40f, 90f);
            }

            if (baskTimer > 0f)
            {
                baskTimer -= dt;
                c.extraRoll = Mathf.MoveTowards(c.extraRoll, 65f, 15f * dt);
                c.desiredVel = w.Tick(c, dt, 0.3f);
            }
            else
            {
                c.extraRoll = Mathf.MoveTowards(c.extraRoll, 0f, 20f * dt);
                c.desiredVel = w.Tick(c, dt, 0.8f);
            }
            c.activityTarget = 0.2f;
            KeepPersonalSpace();
        }
    }

    /// Vampire squid: drifts in the dark, now and then flares its cloak (more often when the diver comes close).
    public sealed class FaunaVampireSquidBehavior : FaunaBehavior
    {
        readonly FaunaWanderer w = new FaunaWanderer();
        float nextFlare, flareT = -1f;

        public override void Reset()
        {
            w.Configure(3f, 10f, 0f, 0.4f);
            w.Reset();
            nextFlare = Random.Range(4f, 12f);
            flareT = -1f;
        }

        public override void Tick(float dt)
        {
            nextFlare -= dt;
            if (flareT < 0f && (nextFlare <= 0f || (PlayerNearby(3f + c.lengthM) && nextFlare < 6f)))
            {
                flareT = 0f;
                nextFlare = Random.Range(8f, 15f);
            }

            if (flareT >= 0f)
            {
                flareT += dt;
                // 0.4 s open, 1.2 s hold, 0.8 s close
                if (flareT < 0.4f) c.cloak = flareT / 0.4f;
                else if (flareT < 1.6f) c.cloak = 1f;
                else if (flareT < 2.4f) c.cloak = 1f - (flareT - 1.6f) / 0.8f;
                else { c.cloak = 0f; flareT = -1f; }
                c.desiredVel = flareT >= 0f && flareT < 0.5f ? -c.Transform.forward * (c.cruise * 2.5f) : Vector3.zero;
                c.activityTarget = 0.7f;
                return;
            }
            c.desiredVel = w.Tick(c, dt, 0.7f);
            c.desiredVel.y += Mathf.Sin(FaunaContext.Time * 0.6f + c.slot) * 0.03f;
            c.activityTarget = 0.2f;
        }
    }
}
