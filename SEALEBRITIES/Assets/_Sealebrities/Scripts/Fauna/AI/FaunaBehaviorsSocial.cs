using UnityEngine;

namespace Sealebrities
{
    /// School member: boids (separation / alignment / cohesion) around the school's wandering virtual leader.
    /// Clownfish stay within 2 m of their anemone (the school home) and bravely charge a diver that comes too close.
    public sealed class FaunaSchoolingBehavior : FaunaBehavior
    {
        readonly FaunaWanderer fallback = new FaunaWanderer();
        Vector3 slot;
        float slotTimer;

        public override void Reset()
        {
            fallback.Configure(WanderMin(c.lengthM), WanderMax(c.lengthM), 0f, 0.1f);
            fallback.Reset();
            NewSlot();
        }

        void NewSlot()
        {
            float spread = c.school != null ? c.school.spread * c.school.spreadMul : 0.6f;
            slot = Random.insideUnitSphere * spread;
            slot.y *= 0.45f;
            slotTimer = Random.Range(2.5f, 6f);
        }

        public override void Tick(float dt)
        {
            var sc = c.school;
            if (sc == null)
            {
                c.desiredVel = fallback.Tick(c, dt, 1f);
                KeepPersonalSpace();
                return;
            }

            slotTimer -= dt;
            if (slotTimer <= 0f) NewSlot();

            // cohesion toward the leader formation slot + follow the leader's velocity
            Vector3 tgt = sc.leaderPos + slot;
            Vector3 desired = sc.leaderVel + (tgt - c.pos) * 0.9f;

            // separation
            float sepR = c.lengthM * 1.5f + 0.05f;
            float sepR2 = sepR * sepR;
            Vector3 sep = Vector3.zero;
            var list = sc.members;
            for (int i = 0; i < list.Count; i++)
            {
                var m = list[i];
                if (ReferenceEquals(m, c)) continue;
                Vector3 d = c.pos - m.pos;
                float dd = d.sqrMagnitude;
                if (dd < sepR2 && dd > 1e-8f) sep += d * ((sepR2 - dd) / (sepR2 * Mathf.Sqrt(dd)));
            }
            desired += sep * (c.cruise * 2f + 0.2f);

            // alignment
            desired += (sc.avgVel - c.velocity) * 0.3f;

            // scatter from the diver
            float scareR = (sc.anchored ? 0.9f : 2.5f) + c.lengthM * 3f + (FaunaContext.PlayerSpeed > 2f ? 1.5f : 0f);
            bool scared = PlayerNearby(scareR);
            if (scared)
            {
                desired += c.FleeFromPlayer(c.cruise * 3f + 0.4f, 0.3f);
                if (!sc.anchored) sc.scare = 2f;
            }

            if (sc.anchored)
            {
                // clownfish: never more than ~2 m from the anemone
                c.relaxBand = true;
                Vector3 toHome = sc.home - c.pos;
                float dh = toHome.magnitude;
                if (dh > 1.6f) desired += toHome * ((dh - 1.6f) * 3f / dh);
                if (dh > 2f) desired += toHome * (1.5f / dh);
            }

            float maxS = c.cruise * (sc.scare > 0f || scared ? 3.2f : 1.8f) + (sc.anchored ? 0.25f : 0f);
            float mag = desired.magnitude;
            if (mag > maxS) desired *= maxS / mag;

            c.desiredVel = desired;
            c.accelMul = 2.5f;
            c.turnMul = 1.5f;
            c.activityTarget = sc.scare > 0f || scared ? 0.85f : (sc.defending ? 0.55f : 0.25f);
        }
    }

    /// Curious animals (turtle, octopus, cuttlefish, sea lion, giant sea bass): approach a diver within 15 m to
    /// 2.5–4 m, hover/circle while facing the camera, then leave. Sea lions do fast playful loops instead.
    public sealed class FaunaCuriousBehavior : FaunaBehavior
    {
        enum St { Wander, Approach, Display, Leave }
        readonly FaunaWanderer w = new FaunaWanderer();
        St st;
        float timer, cooldown, hoverDist, orbitAngle, orbitDir, loopPhase;
        bool sealion, octopus, verticalLoop;

        public override void Reset()
        {
            sealion = c.species.plan == BodyPlan.SeaLion;
            octopus = c.species.plan == BodyPlan.Octopus;
            float bottom = octopus ? 1f : c.species.plan == BodyPlan.Cuttlefish ? 0.4f : 0.1f;
            w.Configure(WanderMin(c.lengthM), WanderMax(c.lengthM), bottom, octopus ? 0.5f : 0.15f);
            w.Reset();
            st = St.Wander;
            cooldown = Random.Range(2f, 10f);
            timer = 0f;
        }

        public override void Tick(float dt)
        {
            cooldown -= dt;
            timer -= dt;
            bool ok = FaunaContext.HasPlayer && FaunaContext.PlayerUnderwater;
            float d = c.distToPlayer;

            switch (st)
            {
                case St.Wander:
                    c.desiredVel = w.Tick(c, dt, sealion ? 1.2f : 1f);
                    c.activityTarget = sealion ? 0.35f : 0.25f;
                    if (ok && cooldown <= 0f && d < 15f)
                    {
                        st = St.Approach;
                        timer = 14f;
                        hoverDist = Random.Range(2.5f, 4f) + c.lengthM * 0.5f;
                        orbitDir = Random.value < 0.5f ? -1f : 1f;
                        Vector3 off = c.pos - FaunaContext.PlayerPos;
                        orbitAngle = Mathf.Atan2(off.x, off.z);
                        loopPhase = 0f;
                        verticalLoop = false;
                    }
                    break;

                case St.Approach:
                    {
                        c.relaxBand = true;
                        Vector3 dir = c.pos - FaunaContext.PlayerPos;
                        if (dir.sqrMagnitude < 1e-4f) dir = -FaunaContext.CamForward;
                        dir = (dir.normalized + FaunaContext.CamForward * 0.8f).normalized;   // come into view
                        Vector3 tgt = FaunaContext.PlayerPos + dir * hoverDist;
                        if (octopus) tgt.y = Mathf.Max(FaunaContext.PlayerPos.y - 1.2f, c.groundY + c.clearance);
                        float spd = sealion ? 3.2f : Mathf.Max(c.cruise * 1.6f, 0.5f);
                        c.desiredVel = c.Seek(tgt, spd, 1.5f);
                        c.hasFaceDir = d < hoverDist + 3f;
                        c.faceDir = c.ToPlayer;
                        c.activityTarget = sealion ? 0.7f : 0.45f;
                        if (d < hoverDist + 0.8f)
                        {
                            st = St.Display;
                            timer = sealion ? Random.Range(8f, 14f) : Random.Range(7f, 13f);
                        }
                        else if (timer <= 0f || !ok || d > 25f) Leave();
                        break;
                    }

                case St.Display:
                    c.relaxBand = true;
                    if (sealion) DisplayLoops(dt);
                    else DisplayHover(dt);
                    if (timer <= 0f || !ok || d > 14f) Leave();
                    break;

                case St.Leave:
                    {
                        Vector3 away = c.FleeFromPlayer(Mathf.Max(c.cruise * 1.2f, 0.4f), 0.2f);
                        away.y *= 0.3f;
                        c.desiredVel = away;
                        c.activityTarget = 0.3f;
                        if (timer <= 0f)
                        {
                            st = St.Wander;
                            w.Pick(c);
                        }
                        break;
                    }
            }
        }

        void DisplayHover(float dt)
        {
            orbitAngle += dt * orbitDir * (0.28f / Mathf.Max(1f, c.lengthM * 0.6f));
            Vector3 off = new Vector3(Mathf.Sin(orbitAngle), 0f, Mathf.Cos(orbitAngle)) * hoverDist;
            Vector3 tgt = FaunaContext.PlayerPos + off;
            float wantY = octopus ? Mathf.Max(FaunaContext.PlayerPos.y - 1.2f, c.groundY + c.clearance) : FaunaContext.PlayerPos.y - 0.3f;
            tgt.y = Mathf.Lerp(c.pos.y, wantY, 0.5f);
            c.desiredVel = c.Seek(tgt, Mathf.Max(c.cruise * 0.8f, 0.3f), 1.5f);
            c.hasFaceDir = true;
            c.faceDir = c.ToPlayer;
            c.turnMul = 1.3f;
            c.activityTarget = 0.6f;
        }

        void DisplayLoops(float dt)
        {
            // fast playful loops right in front of the camera, alternating horizontal circles and vertical rolls
            float r = Mathf.Max(1.6f, hoverDist * 0.75f);
            loopPhase += dt * 3.4f / r;
            if (loopPhase > Mathf.PI * 2f)
            {
                loopPhase -= Mathf.PI * 2f;
                verticalLoop = !verticalLoop;
            }
            Vector3 fwd = FaunaNav.FlatDir(FaunaContext.CamForward);
            if (fwd == Vector3.zero) fwd = Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 center = FaunaContext.PlayerPos + fwd * (hoverDist + 0.6f);
            Vector3 axis2 = verticalLoop ? Vector3.up : fwd;
            Vector3 offset = (right * Mathf.Cos(loopPhase) + axis2 * Mathf.Sin(loopPhase)) * r;
            Vector3 tangent = -right * Mathf.Sin(loopPhase) + axis2 * Mathf.Cos(loopPhase);
            Vector3 tgt = center + offset;
            c.desiredVel = c.Seek(tgt, 3.6f, 0.4f) + tangent * 2.2f;
            c.freePitch = verticalLoop;
            c.accelMul = 3.5f;
            c.turnMul = 3f;
            c.activityTarget = 0.95f;
        }

        void Leave()
        {
            st = St.Leave;
            timer = Random.Range(5f, 8f);
            cooldown = Random.Range(35f, 70f);
        }
    }

    /// Nitrogen-narcosis hallucination: appears in front of the diver, swims around for 6–10 s, then fades away
    /// (or vanishes in a puff of bubbles when the diver gets within 4 m). Never filmable.
    public sealed class FaunaHallucinationBehavior : FaunaBehavior
    {
        float age, life, fadeT, fadeFrom, fadeDur, retarget, alpha;
        bool fading, poofed;
        Vector3 target;

        public override bool IsHallucination => true;
        public bool Fading => fading;

        public override void Reset()
        {
            age = 0f;
            life = Random.Range(6f, 10f);
            fading = false;
            poofed = false;
            fadeT = 0f;
            fadeDur = 1.4f;
            retarget = 0f;
            alpha = 0f;
            c.SetGhostAlpha(0f);
        }

        /// Starts fading out now (used when someone else despawns the hallucination).
        public void FadeOut(float duration)
        {
            if (fading) return;
            fading = true;
            fadeT = 0f;
            fadeFrom = alpha;
            fadeDur = Mathf.Max(0.05f, duration);
        }

        public override void Tick(float dt)
        {
            age += dt;
            if (!FaunaContext.HasPlayer) { c.SetGhostAlpha(0f); c.RequestDespawn(); return; }

            retarget -= dt;
            if (retarget <= 0f)
            {
                retarget = Random.Range(1.5f, 3f);
                target = FaunaContext.CamPos + FaunaContext.CamForward * Random.Range(6.5f, 10f)
                         + FaunaContext.CamRight * Random.Range(-4f, 4f) + Vector3.up * Random.Range(-2f, 1.5f);
            }
            c.desiredVel = c.Seek(target, Mathf.Max(c.cruise * 1.4f, 0.9f), 1.5f);
            c.relaxBand = true;
            c.turnMul = 1.5f;
            c.activityTarget = 0.5f;

            float poofDist = 4f + c.lengthM * 0.5f;
            if (!poofed && c.distToPlayer < poofDist)
            {
                // "poof": a burst of bubbles and it's gone
                poofed = true;
                fading = true;
                fadeT = 0f;
                fadeFrom = alpha;
                fadeDur = 0.25f;
                FaunaBubbles.Poof(c.pos, Mathf.Max(0.3f, c.lengthM * 0.45f), Mathf.Clamp(Mathf.RoundToInt(18f + c.lengthM * 12f), 18, 60));
            }
            else if (!fading && age >= life) FadeOut(1.4f);

            if (fading)
            {
                fadeT += dt;
                alpha = fadeFrom * (1f - fadeT / fadeDur);
                if (alpha <= 0f)
                {
                    c.SetGhostAlpha(0f);
                    c.RequestDespawn();
                    return;
                }
            }
            else alpha = Mathf.Clamp01(age / 0.8f) * 0.9f;   // never fully solid: eerie
            c.SetGhostAlpha(alpha);
        }
    }
}
