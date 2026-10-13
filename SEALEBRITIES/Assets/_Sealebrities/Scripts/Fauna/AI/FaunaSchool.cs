using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// A school of fish: a virtual leader wanders the site (or circles the anemone for clownfish);
    /// the members (FaunaSchoolingBehavior) flock around it with boids rules. Pooled by the spawner.
    public sealed class FaunaSchool
    {
        public SpeciesDef species;
        public readonly List<FaunaCreature> members = new List<FaunaCreature>(12);
        public Vector3 leaderPos, leaderVel, avgPos, avgVel;
        public Vector3 home;
        public bool anchored;
        public Vector3 leashCenter;
        public float leashRadius;
        public float cruise, lengthM, spread, clearance, halfHeight;
        /// > 0 while the school is scattering away from the diver.
        public float scare;
        /// Clownfish currently charging the diver to defend their anemone.
        public bool defending;
        /// Optional scripted leader (animal events: bait ball, big school passing); returns true when it moved the leader.
        public IFaunaSchoolDirector director;
        /// Members stay this tight (0.4 = bait ball) – 1 = species default.
        public float spreadMul = 1f;

        Vector3 target;
        bool hasTarget;
        float targetTimer, orbit;

        public void Init(SpeciesDef s, Vector3 center, Vector3 leashC, float leashR, float typicalLenM, int expectedCount)
        {
            species = s;
            members.Clear();
            lengthM = Mathf.Max(0.03f, typicalLenM);
            cruise = Mathf.Clamp(s.speed * lengthM, 0.14f, 1.8f);
            halfHeight = Mathf.Max(0.04f, s.bodyHeight) * lengthM * 0.5f;
            clearance = 0.6f + halfHeight;
            leashCenter = leashC;
            leashRadius = leashR;
            scare = 0f;
            director = null;
            spreadMul = 1f;
            defending = false;
            hasTarget = false;
            targetTimer = 0f;
            orbit = Random.Range(0f, 10f);
            anchored = s.id == "clownfish";

            leaderPos = center;
            leaderVel = Vector3.zero;
            if (anchored)
            {
                // the anemone sits on the seafloor (or a rock); the fish hover just above it
                float g = FaunaNav.SurfaceBelow(center, 15f);
                clearance = 0.25f + halfHeight;
                home = new Vector3(center.x, g + 0.45f + halfHeight, center.z);
                leaderPos = home;
            }
            else home = center;
            avgPos = leaderPos;
            avgVel = Vector3.zero;
            spread = Mathf.Max(anchored ? 0.5f : 0.35f, lengthM * (1.2f + Mathf.Sqrt(Mathf.Max(1, expectedCount)) * 0.9f));
        }

        public void Remove(FaunaCreature c) { members.Remove(c); }

        public void Tick(float dt)
        {
            int n = members.Count;
            if (n == 0) return;
            Vector3 sp = Vector3.zero, sv = Vector3.zero;
            for (int i = 0; i < n; i++)
            {
                var m = members[i];
                sp += m.pos;
                sv += m.velocity;
            }
            avgPos = sp / n;
            avgVel = sv / n;
            if (scare > 0f) scare -= dt;
            defending = false;

            if (director != null && director.SteerLeader(this, dt)) return;
            if (anchored) TickAnchored(dt);
            else TickRoaming(dt);
        }

        /// Moves the virtual leader toward a point (directors), keeping it in water.
        public void LeaderTo(Vector3 target, float speed, float dt)
        {
            Vector3 to = target - leaderPos;
            Vector3 desired = to.sqrMagnitude > 1e-4f ? to.normalized * speed : Vector3.zero;
            leaderVel = Vector3.MoveTowards(leaderVel, desired, (cruise * 2f + 0.5f) * dt);
            Vector3 np = leaderPos + leaderVel * dt;
            float g = FaunaNav.Ground(np);
            if (g > FaunaNav.LandGuardY) { np.x = leaderPos.x; np.z = leaderPos.z; leaderVel *= -0.3f; g = FaunaNav.Ground(np); }
            float top = Waves.HeightAt(np.x, np.z) - 1f - halfHeight;
            float bot = g + clearance;
            np.y = bot <= top ? Mathf.Clamp(np.y, bot, top) : Mathf.Max(g + halfHeight + 0.3f, top);
            leaderPos = np;
        }

        void TickAnchored(float dt)
        {
            orbit += dt * 0.6f;
            Vector3 tgt = home + new Vector3(Mathf.Sin(orbit) * 0.5f, Mathf.Sin(orbit * 1.7f) * 0.15f, Mathf.Cos(orbit * 0.8f) * 0.5f);
            if (FaunaContext.HasPlayer && FaunaContext.PlayerInWater)
            {
                Vector3 toDiver = FaunaContext.PlayerPos - home;
                float d = toDiver.magnitude;
                if (d < 3f && d > 0.01f)
                {
                    defending = true;
                    tgt = home + toDiver * (Mathf.Min(1.5f, d * 0.5f) / d);
                }
            }
            Vector3 v = (tgt - leaderPos) * 1.5f;
            leaderVel = Vector3.ClampMagnitude(v, cruise * 2.5f + 0.2f);
            leaderPos += leaderVel * dt;
        }

        void TickRoaming(float dt)
        {
            targetTimer -= dt;

            // don't let the leader run away from the school
            float lag = (leaderPos - avgPos).magnitude;
            if (lag > spread * 3f + 2f)
            {
                leaderVel = Vector3.MoveTowards(leaderVel, Vector3.zero, cruise * 2f * dt);
                leaderPos += leaderVel * dt;
                return;
            }

            if (!hasTarget || targetTimer <= 0f || (target - leaderPos).sqrMagnitude < 4f) PickTarget();

            float spd = cruise * (scare > 0f ? 2.5f : 1f);
            Vector3 to = target - leaderPos;
            Vector3 desired = to.sqrMagnitude > 1e-4f ? to.normalized * spd : Vector3.zero;
            if (scare > 0f && FaunaContext.HasPlayer)
            {
                Vector3 away = FaunaNav.FlatDir(leaderPos - FaunaContext.PlayerPos);
                desired = (away + desired.normalized * 0.3f).normalized * spd;
            }

            // land / too shallow ahead → turn to deeper water and pick a new goal
            Vector3 fwd = FaunaNav.FlatDir(leaderVel.sqrMagnitude > 1e-4f ? leaderVel : desired);
            if (fwd != Vector3.zero)
            {
                Vector3 ahead = leaderPos + fwd * Mathf.Max(3f, spread + 2f);
                float g = FaunaNav.Ground(ahead);
                if (g > FaunaNav.LandGuardY - 0.5f || g + clearance > -species.minDepth)
                {
                    Vector3 dh = FaunaNav.Downhill(ahead);
                    if (dh == Vector3.zero) dh = -fwd;
                    desired = dh * spd;
                    if (targetTimer > 1f) targetTimer = 1f;
                }
            }

            leaderVel = Vector3.MoveTowards(leaderVel, desired, (cruise * 1.5f + 0.2f) * dt);
            Vector3 np = leaderPos + leaderVel * dt;

            float gl = FaunaNav.Ground(np);
            if (gl > FaunaNav.LandGuardY)
            {
                np.x = leaderPos.x; np.z = leaderPos.z;
                leaderVel = -leaderVel * 0.3f;
                gl = FaunaNav.Ground(np);
                hasTarget = false;
            }
            float top = FaunaNav.BandTop(species, halfHeight, Waves.HeightAt(np.x, np.z));
            float bot = FaunaNav.BandBottom(species, gl, clearance);
            if (bot <= top) np.y = Mathf.Clamp(np.y, bot, top);
            else np.y = Mathf.Max(gl + halfHeight + 0.3f, Mathf.Min(np.y, Waves.SeaLevel - 1.5f - halfHeight));
            leaderPos = np;
        }

        void PickTarget()
        {
            float minR = Mathf.Clamp(lengthM * 30f, 6f, 15f);
            float maxR = Mathf.Clamp(lengthM * 120f, 15f, 45f);
            if (!FaunaNav.TryPickPoint(species, leaderPos, minR, maxR, leashCenter, leashRadius, clearance, halfHeight, 0f, out target))
            {
                Vector3 toLeash = FaunaNav.FlatDir(leashCenter - leaderPos);
                if (toLeash == Vector3.zero) toLeash = Vector3.forward;
                target = leaderPos + toLeash * minR;
            }
            hasTarget = true;
            targetTimer = Random.Range(8f, 16f);
        }
    }
}
