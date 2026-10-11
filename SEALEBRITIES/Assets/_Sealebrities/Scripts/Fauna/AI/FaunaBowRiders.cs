using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// G.1 dolphins at the bow: when the boat runs faster than 6 m/s over water deeper than 20 m (by day, dolphins
    /// unlocked), a pod of 3–6 appears beside it and rides the bow wave for 20–40 s – racing just ahead of the bow, a
    /// little below the surface, leaping now and then – before peeling off. Host / offline only (BoatTelemetry from the
    /// Boat module). Unlocks the achievement "bow_riders".
    public sealed class FaunaBowRiders : MonoBehaviour
    {
        public const float MinSpeed = 6f;
        public const float MinDepth = 20f;
        public const float Cooldown = 150f;

        /// True while a pod rides the bow (filming challenge "ch_dolphin_bow", livestream, tests).
        public static bool Riding { get; private set; }
        public static Vector3 BoatHeading { get; private set; } = Vector3.forward;

        readonly List<FaunaCreature> pod = new List<FaunaCreature>(8);
        Vector3 lastBoat, heading = Vector3.forward;
        bool hasLast;
        float cooldown = 40f, rideLeft, fastTime, rideTime;
        bool achieved;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Riding = false; BoatHeading = Vector3.forward; }

        void Update()
        {
            var gm = GameManager.I;
            if (gm == null || !gm.InGame || !BoatTelemetry.Valid) { hasLast = false; Riding = false; return; }
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            Vector3 bp = BoatTelemetry.Position;
            if (hasLast)
            {
                Vector3 d = bp - lastBoat;
                d.y = 0f;
                if (d.sqrMagnitude > 1e-4f) heading = Vector3.Slerp(heading, d.normalized, Mathf.Clamp01(dt * 3f));
            }
            lastBoat = bp;
            hasLast = true;
            BoatHeading = heading;
            if (!Net.HasAuthority || NetFlags.CreaturesRemote) { Riding = false; return; }

            for (int i = pod.Count - 1; i >= 0; i--)
                if (pod[i] == null || !pod[i].alive || pod[i].pendingDespawn || !(pod[i].director is Ride)) pod.RemoveAt(i);
            Riding = pod.Count > 0;

            if (Riding)
            {
                rideLeft -= dt;
                rideTime += dt;
                if (!achieved && rideTime > 5f && BoatTelemetry.Speed > MinSpeed * 0.7f) { achieved = true; Achievements.Unlock("bow_riders"); }
                if (rideLeft <= 0f || BoatTelemetry.Speed < 2.5f) Release();
                return;
            }

            cooldown -= dt;
            bool fast = BoatTelemetry.Speed > MinSpeed && WorldInfo.SeafloorDepth(bp) > MinDepth;
            fastTime = fast ? fastTime + dt : 0f;
            if (cooldown > 0f || fastTime < 3f) return;
            var s = SpeciesDatabase.Get("dolphin");
            if (s == null || !Progression.IsGateOpen(s) || !s.FitsTime(FaunaContext.DayNow)) return;
            var spawner = FaunaModule.Spawner;
            if (spawner == null) return;
            // they come in from the side, 30 m off, slightly behind
            Vector3 side = Vector3.Cross(Vector3.up, heading) * (Random.value < 0.5f ? -1f : 1f);
            Vector3 at = bp + side * 28f - heading * 8f;
            at.y = Waves.SeaLevel - 2.5f;
            int n = Random.Range(3, 7);
            int before = pod.Count;
            spawner.SpawnAt(s, at, n, bp, 400f, false, pod);
            for (int i = before; i < pod.Count; i++) pod[i].director = new Ride(this, i - before);
            if (pod.Count == 0) { cooldown = 20f; return; }
            rideLeft = Random.Range(20f, 40f);
            rideTime = 0f;
            cooldown = Cooldown;
            Debug.Log("[Fauna] dolphins at the bow (" + pod.Count + ")");
        }

        void Release()
        {
            for (int i = 0; i < pod.Count; i++) if (pod[i] != null && pod[i].director is Ride) pod[i].director = null;
            pod.Clear();
            Riding = false;
        }

        /// One pod member racing at the bow: a slot ahead / beside the bow, a little below the surface, leaps.
        sealed class Ride : IFaunaDirector
        {
            readonly FaunaBowRiders owner;
            readonly Vector3 slot;
            float jumpTimer, airT;
            bool jumping;

            public Ride(FaunaBowRiders o, int index)
            {
                owner = o;
                float side = (index % 2 == 0 ? 1f : -1f) * (1.2f + (index / 2) * 1.4f);
                slot = new Vector3(side, -(1.1f + (index % 3) * 0.5f), 4.5f + (index % 3) * 1.6f);
                jumpTimer = Random.Range(3f, 9f);
            }

            public bool Steer(FaunaCreature c, float dt)
            {
                if (!BoatTelemetry.Valid) return false;
                if (jumping)
                {
                    if (FaunaJump.Airborne(c, ref airT, dt, 2.5f)) return true;
                    jumping = false;
                    jumpTimer = Random.Range(4f, 9f);
                }
                Vector3 h = owner.heading;
                Vector3 right = Vector3.Cross(Vector3.up, h);
                Vector3 target = BoatTelemetry.Position + h * slot.z + right * slot.x;
                target.y = Waves.SeaLevel + slot.y;
                Vector3 boatVel = h * BoatTelemetry.Speed;
                c.relaxBand = true;
                // catch up from the side at most 5 m/s faster than the boat (no rubber-band sprint)
                c.desiredVel = boatVel + Vector3.ClampMagnitude((target - c.pos) * 1.4f, 5f);
                c.accelMul = 5f;
                c.turnMul = 2.5f;
                c.activityTarget = 0.95f;
                jumpTimer -= dt;
                if (jumpTimer <= 0f && (target - c.pos).sqrMagnitude < 9f)
                {
                    jumping = true;
                    airT = 0f;
                    FaunaJump.Launch(c, h, BoatTelemetry.Speed + 1f, Random.Range(4f, 5.5f));
                }
                return true;
            }
        }
    }
}
