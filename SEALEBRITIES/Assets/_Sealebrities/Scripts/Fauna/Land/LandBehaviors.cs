using UnityEngine;

namespace Sealebrities
{
    /// Perching sea birds (seagull "Günther" and friends, tropicbird): sit on dock posts, roofs, rocks and the beach,
    /// look around, take off when someone comes within 4 m, circle over the shore and land on another perch.
    /// Action channel: 0 perched (wings folded), 0.5 glide, 1 flap.
    public sealed class FaunaPerchBirdBehavior : FaunaBehavior
    {
        enum St { Perched, Fly, Circle }
        St st;
        float timer, heading, headTimer, flapBurst;
        Vector3 orbitC;
        float orbitR, orbitH, orbitA, orbitDir;
        FaunaLandSpot perch, next;

        public override void Reset()
        {
            perch = c.landSpot;
            heading = Random.Range(0f, 360f);
            orbitDir = Random.value < 0.5f ? -1f : 1f;
            if (perch.valid && IsSitSpot(perch.kind)) Sit(perch);
            else StartCircle(c.home, 12f);
        }

        static bool IsSitSpot(FaunaSpotKind k) => k == FaunaSpotKind.Perch || k == FaunaSpotKind.Beach || k == FaunaSpotKind.Rocks || k == FaunaSpotKind.Ground || k == FaunaSpotKind.Meadow;

        void Sit(FaunaLandSpot s)
        {
            perch = s;
            st = St.Perched;
            c.perched = true;
            c.perchPos = s.pos;
            timer = Random.Range(12f, 40f);
        }

        void StartCircle(Vector3 center, float minH)
        {
            st = St.Circle;
            c.perched = false;
            orbitC = new Vector3(center.x, Mathf.Max(WorldInfo.GroundHeight(center), Waves.SeaLevel), center.z);
            orbitR = Random.Range(10f, 22f);
            orbitH = Random.Range(minH, minH + 18f);
            Vector3 off = c.pos - orbitC;
            orbitA = Mathf.Atan2(off.x, off.z);
            timer = Random.Range(10f, 25f);
        }

        void TakeOff()
        {
            c.perched = false;
            c.velocity = Vector3.up * 3f + c.Transform.forward * 2f;
            if (LandFauna.PickSpot(FaunaSpotKind.Perch, FaunaSpotKind.Beach, c.home, 55f, out next) && Random.value < 0.6f)
            {
                st = St.Fly;
                timer = 25f;
            }
            else StartCircle(c.pos, 12f);
        }

        public override void Poke(Vector3 from) { if (st == St.Perched) TakeOff(); }

        public override void Tick(float dt)
        {
            timer -= dt;
            switch (st)
            {
                case St.Perched:
                    {
                        c.perched = true;
                        c.perchPos = perch.pos;
                        headTimer -= dt;
                        if (headTimer <= 0f) { headTimer = Random.Range(2f, 6f); heading += Random.Range(-70f, 70f); }
                        c.hasFaceDir = true;
                        c.faceDir = Quaternion.Euler(0f, heading, 0f) * Vector3.forward;
                        c.turnMul = 0.6f;
                        c.activityTarget = 0.15f;
                        Action(0f);
                        bool scared = PlayerOnLandNear(4f + c.lengthM) || (FaunaContext.HasPlayer && FaunaContext.PlayerInWater && c.distToPlayer < 3.5f);
                        if (scared || timer <= 0f) TakeOff();
                        break;
                    }
                case St.Fly:
                    {
                        Vector3 tgt = next.pos + Vector3.up * c.halfHeight;
                        Vector3 to = tgt - c.pos;
                        float d = to.magnitude;
                        // approach from above, glide in, flap to brake
                        Vector3 aim = d > 8f ? tgt + Vector3.up * Mathf.Min(6f, d * 0.25f) : tgt;
                        c.desiredVel = c.Seek(aim, c.cruise * (d > 6f ? 1f : 0.5f), 4f);
                        c.accelMul = 1.5f;
                        c.activityTarget = 0.5f;
                        Action(d < 6f || c.velocity.y > 0.5f ? 1f : 0.5f);
                        if (d < Mathf.Max(0.6f, c.lengthM)) Sit(next);
                        else if (timer <= 0f) StartCircle(c.pos, 10f);
                        break;
                    }
                case St.Circle:
                    {
                        orbitA += dt * orbitDir * c.cruise / orbitR;
                        Vector3 tgt = orbitC + new Vector3(Mathf.Sin(orbitA), 0f, Mathf.Cos(orbitA)) * orbitR + Vector3.up * orbitH;
                        Vector3 tangent = new Vector3(Mathf.Cos(orbitA), 0f, -Mathf.Sin(orbitA)) * orbitDir;
                        c.desiredVel = tangent * c.cruise + (tgt - c.pos) * 0.6f;
                        c.activityTarget = 0.4f;
                        flapBurst -= dt;
                        if (flapBurst < -Random.Range(3f, 7f)) flapBurst = Random.Range(0.6f, 1.4f);
                        Action(flapBurst > 0f || c.velocity.y > 0.6f ? 1f : 0.5f);
                        if (timer <= 0f)
                        {
                            if (LandFauna.PickSpot(FaunaSpotKind.Perch, FaunaSpotKind.Beach, c.home, 55f, out next)) { st = St.Fly; timer = 25f; }
                            else timer = Random.Range(10f, 20f);
                        }
                        break;
                    }
            }
        }
    }

    /// Frigatebirds: soar in wide circles 30–60 m above the lighthouse / the village, hardly ever flapping.
    public sealed class FaunaSoarBehavior : FaunaBehavior
    {
        float orbitA, orbitR, orbitH, orbitDir, flapT;
        Vector3 center;

        public override void Reset()
        {
            var s = c.landSpot;
            center = s.valid ? s.pos : c.home + Vector3.up * 40f;
            float baseY = Mathf.Max(WorldInfo.GroundHeight(center), Waves.SeaLevel);
            center.y = Mathf.Max(center.y, baseY + 30f);
            orbitR = Random.Range(24f, 40f);
            orbitH = Random.Range(0f, 25f);
            orbitDir = Random.value < 0.5f ? -1f : 1f;
            Vector3 off = c.pos - center;
            orbitA = Mathf.Atan2(off.x, off.z);
            flapT = Random.Range(10f, 25f);
        }

        public override void Tick(float dt)
        {
            float speed = c.cruise * 0.8f;
            orbitA += dt * orbitDir * speed / orbitR;
            float h = orbitH + 6f * Mathf.Sin(FaunaContext.Time * 0.05f + c.slot);
            Vector3 tgt = center + new Vector3(Mathf.Sin(orbitA), 0f, Mathf.Cos(orbitA)) * orbitR + Vector3.up * h;
            Vector3 tangent = new Vector3(Mathf.Cos(orbitA), 0f, -Mathf.Sin(orbitA)) * orbitDir;
            c.desiredVel = tangent * speed + (tgt - c.pos) * 0.4f;
            c.activityTarget = 0.3f;
            flapT -= dt;
            if (flapT < 0f && flapT > -1.2f) Action(1f);
            else
            {
                Action(0.5f);
                if (flapT <= -1.2f) flapT = Random.Range(15f, 30f);
            }
        }
    }

    /// Fruit bats: at dusk they leave the village palms and loop around them, fluttering.
    public sealed class FaunaBatBehavior : FaunaBehavior
    {
        float a, r, h, dir, seed;
        Vector3 center;

        public override void Reset()
        {
            var s = c.landSpot;
            center = s.valid ? s.pos : c.home + Vector3.up * 6f;
            r = Random.Range(5f, 13f);
            h = Random.Range(-2f, 4f);
            dir = Random.value < 0.5f ? -1f : 1f;
            seed = Random.Range(0f, 50f);
            Vector3 off = c.pos - center;
            a = Mathf.Atan2(off.x, off.z);
        }

        public override void Tick(float dt)
        {
            float t = FaunaContext.Time;
            float speed = c.cruise * 0.7f;
            a += dt * dir * speed / r;
            float rr = r * (0.8f + 0.4f * Mathf.PerlinNoise(t * 0.3f, seed));
            Vector3 tgt = center + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * rr + Vector3.up * (h + 2.5f * Mathf.Sin(t * 0.7f + seed));
            Vector3 tangent = new Vector3(Mathf.Cos(a), 0f, -Mathf.Sin(a)) * dir;
            c.desiredVel = tangent * speed + (tgt - c.pos) * 1.2f;
            c.accelMul = 2f;
            c.activityTarget = 0.6f;
            Action(Mathf.PerlinNoise(t * 0.8f, seed) > 0.75f ? 0.5f : 1f);
            if (PlayerOnLandNear(2.5f)) dir = -dir;
        }
    }

    /// Marine iguanas: bask on the warm tide-pool and bay rocks, now and then walk into the lagoon to graze
    /// (they walk along the bottom), and scuttle away from people.
    public sealed class FaunaBaskBehavior : FaunaBehavior
    {
        enum St { Bask, Walk, Graze, Flee }
        St st;
        float timer;
        Vector3 target;

        public override void Reset()
        {
            c.home = c.pos;
            st = St.Bask;
            timer = Random.Range(8f, 30f);
        }

        public override void Tick(float dt)
        {
            timer -= dt;
            if (st != St.Flee && (PlayerOnLandNear(2.5f + c.lengthM) || (FaunaContext.PlayerInWater && c.distToPlayer < 2f + c.lengthM)))
            {
                st = St.Flee;
                timer = 1.5f;
                target = c.pos + FaunaNav.FlatDir(c.pos - FaunaContext.PlayerRootPos) * 3f;
            }
            switch (st)
            {
                case St.Bask:
                    c.desiredVel = Vector3.zero;
                    c.activityTarget = 0.1f;
                    if (timer <= 0f)
                    {
                        // 30 %: a swim in the lagoon, otherwise a short walk on the rocks
                        if (Random.value < 0.3f && FindWater(out target)) { st = St.Graze; timer = Random.Range(20f, 40f); }
                        else { target = c.home + Rand2(4f); st = St.Walk; timer = Random.Range(5f, 10f); }
                    }
                    break;
                case St.Walk:
                case St.Graze:
                    {
                        Vector3 to = target - c.pos; to.y = 0f;
                        float d = to.magnitude;
                        if (d < 0.4f || timer <= 0f)
                        {
                            if (st == St.Graze && timer > 0f) { target = target + Rand2(2f); break; }
                            st = St.Bask; timer = Random.Range(15f, 40f);
                            if (st == St.Bask && FaunaNav.DistXZ(c.pos, c.home) > 6f) { target = c.home; st = St.Walk; timer = 12f; }
                            break;
                        }
                        c.desiredVel = to / d * Mathf.Max(0.2f, c.cruise);
                        c.activityTarget = 0.35f;
                        break;
                    }
                case St.Flee:
                    {
                        Vector3 to = target - c.pos; to.y = 0f;
                        c.desiredVel = to.sqrMagnitude > 0.01f ? to.normalized * Mathf.Max(0.9f, c.cruise * 3f) : Vector3.zero;
                        c.accelMul = 4f;
                        c.activityTarget = 0.9f;
                        if (timer <= 0f) { st = St.Bask; timer = Random.Range(10f, 25f); c.home = c.pos; }
                        break;
                    }
            }
        }

        bool FindWater(out Vector3 p)
        {
            for (int i = 0; i < 6; i++)
            {
                Vector3 q = c.home + Rand2(9f);
                float g = WorldInfo.GroundHeight(q);
                if (g < -0.6f && g > -c.species.maxDepth) { p = q; return true; }
            }
            p = c.home;
            return false;
        }

        static Vector3 Rand2(float r) { Vector2 o = Random.insideUnitCircle * r; return new Vector3(o.x, 0f, o.y); }
    }

    /// Geckos: cling to house walls (and scurry on the ground), dart in short bursts, flee upward from hands.
    public sealed class FaunaClimbBehavior : FaunaBehavior
    {
        bool onWall;
        Vector3 wallP, wallN, wallT;
        float u, v, tu, tv, timer, fleeT;

        public override void Reset()
        {
            var s = c.landSpot;
            onWall = s.valid && s.kind == FaunaSpotKind.Wall;
            c.ignoreObstacles = onWall;
            if (onWall)
            {
                wallP = s.pos;
                wallN = s.normal.sqrMagnitude > 0.5f ? s.normal : Vector3.forward;
                wallT = Vector3.Cross(Vector3.up, wallN).normalized;
                u = Random.Range(-0.8f, 0.8f);
                v = Random.Range(0f, 1.2f);
                tu = u; tv = v;
            }
            timer = Random.Range(1f, 4f);
            fleeT = 0f;
        }

        public override void Tick(float dt)
        {
            timer -= dt;
            bool threat = PlayerOnLandNear(1.6f) || (FaunaContext.HasPlayer && !FaunaContext.PlayerInWater && c.distToPlayer < 1.2f);
            if (threat && fleeT <= 0f) { fleeT = 1f; timer = 0f; }
            if (fleeT > 0f) fleeT -= dt;
            if (!onWall)
            {
                // ground gecko: dart and freeze
                if (timer <= 0f) { Vector2 r = Random.insideUnitCircle; c.home = c.pos + new Vector3(r.x, 0f, r.y) * 1.5f; timer = Random.Range(1f, 3.5f); }
                Vector3 to = c.home - c.pos; to.y = 0f;
                c.desiredVel = to.sqrMagnitude > 0.04f ? to.normalized * Mathf.Max(0.3f, c.cruise * (fleeT > 0f ? 6f : 2f)) : Vector3.zero;
                c.accelMul = 6f;
                c.activityTarget = fleeT > 0f ? 0.9f : 0.3f;
                return;
            }
            if (timer <= 0f)
            {
                // a quick dash to a new spot on the wall (up when fleeing)
                tu = Mathf.Clamp(u + Random.Range(-0.6f, 0.6f), -1f, 1f);
                tv = fleeT > 0f ? Mathf.Clamp(v + 0.6f, 0f, 1.8f) : Mathf.Clamp(v + Random.Range(-0.5f, 0.5f), 0f, 1.8f);
                timer = Random.Range(1.5f, 5f);
            }
            float sp = fleeT > 0f ? 1.6f : 0.6f;
            float du = tu - u, dv = tv - v;
            float m = Mathf.Sqrt(du * du + dv * dv);
            float step = Mathf.Min(m, sp * dt);
            if (m > 1e-4f) { u += du / m * step; v += dv / m * step; }
            Vector3 target = wallP + wallT * u + Vector3.up * v + wallN * (c.halfHeight * 0.6f);
            Vector3 hv = (target - c.pos) / Mathf.Max(dt, 1e-3f);
            hv.y = 0f;
            c.desiredVel = Vector3.ClampMagnitude(hv, 3f);
            c.accelMul = 20f;
            c.hasHeightOverride = true;
            c.heightOverride = target.y;
            // body flat on the wall, head pointing along the motion (or up)
            Vector3 headDir = m > 0.01f ? (wallT * du + Vector3.up * dv).normalized : Vector3.up;
            c.hasRotOverride = true;
            c.rotOverride = Quaternion.LookRotation(headDir, wallN);
            c.activityTarget = m > 0.01f ? 0.6f : 0.15f;
        }
    }

    /// Goats ("Captain Bleat" and his herd): graze around the pen / meadow (head down = action), wander together,
    /// step aside when someone comes close.
    public sealed class FaunaHerdBehavior : FaunaBehavior
    {
        enum St { Graze, Walk, Watch }
        St st;
        float timer;
        Vector3 target;

        public override void Reset()
        {
            st = St.Graze;
            timer = Random.Range(3f, 10f);
        }

        public override void Tick(float dt)
        {
            timer -= dt;
            bool near = PlayerOnLandNear(2.2f);
            bool watching = PlayerOnLandNear(7f);
            if (near)
            {
                Vector3 away = FaunaNav.FlatDir(c.pos - FaunaContext.PlayerRootPos);
                c.desiredVel = away * Mathf.Max(0.6f, c.cruise * 1.5f);
                c.activityTarget = 0.6f;
                Action(0f);
                return;
            }
            switch (st)
            {
                case St.Graze:
                    c.desiredVel = Vector3.zero;
                    c.activityTarget = 0.2f;
                    Action(1f);
                    if (watching && Random.value < dt * 0.4f) { st = St.Watch; timer = Random.Range(2f, 5f); }
                    else if (timer <= 0f) { PickTarget(); st = St.Walk; timer = Random.Range(6f, 12f); }
                    break;
                case St.Walk:
                    {
                        Action(0f);
                        Vector3 to = target - c.pos; to.y = 0f;
                        float d = to.magnitude;
                        if (d < 0.5f || timer <= 0f) { st = St.Graze; timer = Random.Range(5f, 14f); break; }
                        c.desiredVel = to / d * Mathf.Max(0.35f, c.cruise);
                        c.activityTarget = 0.3f;
                        break;
                    }
                case St.Watch:
                    c.desiredVel = Vector3.zero;
                    c.hasFaceDir = true;
                    c.faceDir = FaunaNav.FlatDir(FaunaContext.PlayerRootPos - c.pos);
                    c.turnMul = 0.5f;
                    Action(0f);
                    if (timer <= 0f) { st = St.Graze; timer = Random.Range(4f, 9f); }
                    break;
            }
        }

        void PickTarget()
        {
            for (int i = 0; i < 5; i++)
            {
                Vector2 o = Random.insideUnitCircle * 8f;
                Vector3 t = c.home + new Vector3(o.x, 0f, o.y);
                if (c.GroundOk(WorldInfo.GroundHeight(t))) { target = t; return; }
            }
            target = c.home;
        }
    }

    /// Wandering land animals (the pig "Truffle"): potters around the village, swims at the village beach between
    /// 10:00 and 12:00, comes over to look at people.
    public sealed class FaunaLandWanderBehavior : FaunaBehavior
    {
        float timer;
        Vector3 target;
        bool swimTime;

        public override void Reset()
        {
            timer = 0f;
            target = c.home;
        }

        public override void Tick(float dt)
        {
            timer -= dt;
            float tod = GameManager.I != null ? GameManager.I.State.timeOfDay : 0.5f;
            bool swim = c.species.id == "pig" && tod > 10f / 24f && tod < 12f / 24f;
            if (swim != swimTime) { swimTime = swim; timer = 0f; }
            if (timer <= 0f)
            {
                timer = Random.Range(6f, 14f);
                if (swimTime && LandFauna.PickSpot(FaunaSpotKind.Water, FaunaSpotKind.Water, c.home, 45f, out var w))
                {
                    Vector2 o = Random.insideUnitCircle * 3f;
                    target = w.pos + new Vector3(o.x, 0f, o.y);
                }
                else
                {
                    Vector2 o = Random.insideUnitCircle * 10f;
                    target = c.home + new Vector3(o.x, 0f, o.y);
                }
            }
            if (PlayerOnLandNear(5f) && !PlayerOnLandNear(1.6f))
            {
                // curious: come and have a look
                c.desiredVel = FaunaNav.FlatDir(FaunaContext.PlayerRootPos - c.pos) * Mathf.Max(0.3f, c.cruise);
                c.activityTarget = 0.4f;
                return;
            }
            Vector3 to = target - c.pos; to.y = 0f;
            float d = to.magnitude;
            c.desiredVel = d > 0.6f ? to / d * Mathf.Max(0.3f, c.cruise) : Vector3.zero;
            c.activityTarget = d > 0.6f ? 0.3f : 0.15f;
        }
    }

    /// The capuchin "Clapper": hops between railings, rocks and jungle spots, sits and claps when people are near.
    /// His hat theft is a mischief (FaunaMischief directs him).
    public sealed class FaunaMonkeyBehavior : FaunaBehavior
    {
        enum St { Sit, Walk, Climb }
        St st;
        float timer, climbT;
        FaunaLandSpot spot;

        public override void Reset()
        {
            spot = c.landSpot;
            st = St.Sit;
            timer = Random.Range(10f, 25f);
            climbT = 1f;
        }

        public override void Tick(float dt)
        {
            timer -= dt;
            switch (st)
            {
                case St.Sit:
                    {
                        c.desiredVel = Vector3.zero;
                        SitHeight(1f);
                        bool fans = PlayerOnLandNear(6f);
                        Action(fans ? 1f : 0f);
                        c.activityTarget = fans ? 0.8f : 0.2f;
                        if (fans)
                        {
                            c.hasFaceDir = true;
                            c.faceDir = FaunaNav.FlatDir(FaunaContext.PlayerRootPos - c.pos);
                        }
                        if (timer <= 0f && !fans)
                        {
                            if (LandFauna.PickSpot(FaunaSpotKind.Perch, FaunaSpotKind.Jungle, c.home, 45f, out var next)) spot = next;
                            st = St.Walk;
                            timer = 25f;
                        }
                        break;
                    }
                case St.Walk:
                    {
                        Action(0f);
                        climbT = Mathf.MoveTowards(climbT, 0f, dt * 2f);
                        SitHeight(climbT);
                        Vector3 to = spot.pos - c.pos; to.y = 0f;
                        float d = to.magnitude;
                        if (d < 0.5f || timer <= 0f) { st = St.Climb; break; }
                        c.desiredVel = to / d * Mathf.Max(0.8f, c.cruise * 2f);
                        c.activityTarget = 0.5f;
                        break;
                    }
                case St.Climb:
                    climbT = Mathf.MoveTowards(climbT, 1f, dt * 1.5f);
                    SitHeight(climbT);
                    c.desiredVel = Vector3.zero;
                    if (climbT >= 1f) { st = St.Sit; timer = Random.Range(15f, 40f); }
                    break;
            }
        }

        /// Sits on the spot's height (railing, rock) blended with the ground.
        void SitHeight(float k)
        {
            if (!spot.valid) return;
            float g = WorldInfo.GroundHeight(c.pos);
            float spotY = spot.pos.y;
            if (spotY <= g + 0.3f || k <= 0.001f) return;
            c.hasHeightOverride = true;
            c.heightOverride = Mathf.Lerp(g, spotY, k) + c.halfHeight;
        }
    }
}
