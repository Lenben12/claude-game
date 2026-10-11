using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Procedural toy-like delivery helicopter (~10 m) in the low-poly style: chunky octagonal yellow body with a
    /// glass bubble and red stripes, red tail boom with fins, spinning main and tail rotor, skids, nav lights and a
    /// blinking beacon. Flat-shaded solid colors.
    public static class HeliModel
    {
        public class Parts
        {
            public Transform mainRotor, tailRotor, hook;
            public Renderer beacon;
        }

        public static Parts Build(Transform root)
        {
            var parts = new Parts();
            var set = new BoatMeshSet();

            // fuselage: octagonal loft (flat faces on top, bottom and sides)
            float[] z = { 3.0f, 2.62f, 1.95f, 1.0f, 0.0f, -1.1f, -1.95f, -2.6f };
            float[] cy = { 0.04f, 0.0f, 0.05f, 0.14f, 0.2f, 0.25f, 0.42f, 0.6f };
            float[] hw = { 0.22f, 0.62f, 0.9f, 1.02f, 1.04f, 0.94f, 0.58f, 0.3f };
            float[] hh = { 0.2f, 0.6f, 0.86f, 0.98f, 1.0f, 0.92f, 0.56f, 0.3f };
            const int seg = 8;
            int rows = z.Length;
            var P = new Vector3[rows, seg];
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < seg; c++)
                {
                    float a = (c + 0.5f) / seg * Mathf.PI * 2f;
                    P[r, c] = new Vector3(hw[r] * Mathf.Cos(a) * 1.08f, cy[r] + hh[r] * Mathf.Sin(a) * 1.08f, z[r]);
                }
            for (int r = 0; r < rows - 1; r++)
                for (int c = 0; c < seg; c++)
                {
                    int c1 = (c + 1) % seg;
                    float zc = (z[r] + z[r + 1]) * 0.5f;
                    float am = (c + 1f) / seg * Mathf.PI * 2f;   // mid angle of this face
                    float s = Mathf.Sin(am);
                    Material m = BoatMats.HeliYellow;
                    if (zc > 1.0f && s > -0.3f) m = BoatMats.HeliGlass;                       // bubble canopy
                    else if (zc > -0.6f && zc < 1.0f && s > 0.3f && s < 0.9f) m = BoatMats.HeliGlass;   // side windows
                    else if (s < -0.3f && s > -0.9f) m = BoatMats.HeliRed;                     // red belly stripes
                    Vector3 mid = (P[r, c] + P[r + 1, c] + P[r + 1, c1] + P[r, c1]) * 0.25f;
                    Vector3 axis = new Vector3(0f, (cy[r] + cy[r + 1]) * 0.5f, zc);
                    set[m].QuadOut(P[r, c], P[r + 1, c], P[r + 1, c1], P[r, c1], mid - axis);
                }
            // nose and tail caps
            {
                var cap = set[BoatMats.HeliGlass];
                Vector3 tip = new Vector3(0f, cy[0], z[0] + 0.12f);
                for (int c = 0; c < seg; c++) cap.TriOut(tip, P[0, c], P[0, (c + 1) % seg], Vector3.forward);
                var back = set[BoatMats.HeliYellow];
                Vector3 tailCenter = new Vector3(0f, cy[rows - 1], z[rows - 1]);
                for (int c = 0; c < seg; c++) back.TriOut(tailCenter, P[rows - 1, c], P[rows - 1, (c + 1) % seg], Vector3.back);
            }

            // dark inner skin following the loft (facing inward) + pilot, so the glass never looks into a hollow shell
            {
                var inner = set[BoatMats.HeliDark];
                Vector3 In(int r, int c) { var ax = new Vector3(0f, cy[r], z[r]); return ax + (P[r, c] - ax) * 0.95f; }
                for (int r = 0; r < rows - 1; r++)
                    for (int c = 0; c < seg; c++)
                    {
                        int c1 = (c + 1) % seg;
                        Vector3 a = In(r, c), b = In(r + 1, c), cc = In(r + 1, c1), d = In(r, c1);
                        Vector3 axis = new Vector3(0f, (cy[r] + cy[r + 1]) * 0.5f, (z[r] + z[r + 1]) * 0.5f);
                        inner.QuadOut(a, b, cc, d, axis - (a + b + cc + d) * 0.25f);
                    }
                Vector3 noseIn = new Vector3(0f, cy[0], z[0]), tailIn = new Vector3(0f, cy[rows - 1], z[rows - 1]);
                for (int c = 0; c < seg; c++)
                {
                    inner.TriOut(noseIn, In(0, c), In(0, (c + 1) % seg), Vector3.back);
                    inner.TriOut(tailIn, In(rows - 1, c), In(rows - 1, (c + 1) % seg), Vector3.forward);
                }
            }
            set[BoatMats.Pilot].TaperBox(new Vector3(0.42f, 0.05f, 1.15f), new Vector2(0.4f, 0.34f), new Vector2(0.32f, 0.26f), 0.55f, Quaternion.identity);
            set[BoatMats.Helmet].Ellipsoid(new Vector3(0.42f, 0.55f, 1.18f), new Vector3(0.17f, 0.17f, 0.18f), Quaternion.identity, 6, 4);
            set[BoatMats.HeliDark].Box(new Vector3(0.42f, 0.54f, 1.34f), new Vector3(0.22f, 0.09f, 0.05f));
            set[BoatMats.HeliDark].Box(new Vector3(0f, -0.25f, 2.15f), new Vector3(1.2f, 0.35f, 0.35f));

            // engine housing, exhausts, mast
            set[BoatMats.HeliYellow].TaperBox(new Vector3(0f, 1.2f, -0.7f), new Vector2(1.0f, 2.0f), new Vector2(0.68f, 1.5f), 0.44f, Quaternion.identity);
            set[BoatMats.HeliRed].Box(new Vector3(0f, 1.43f, -0.75f), new Vector3(0.5f, 0.03f, 1.2f));
            set[BoatMats.HeliDark].Cylinder(new Vector3(-0.25f, 1.15f, -1.6f), new Vector3(-0.3f, 1.2f, -1.95f), 0.09f, 0.1f, 6, false, true);
            set[BoatMats.HeliDark].Cylinder(new Vector3(0.25f, 1.15f, -1.6f), new Vector3(0.3f, 1.2f, -1.95f), 0.09f, 0.1f, 6, false, true);
            set[BoatMats.HeliDark].Cylinder(new Vector3(0f, 1.3f, 0.1f), new Vector3(0f, 1.72f, 0.1f), 0.1f, 0.09f, 6);

            // tail boom, fins, stabilizer
            set[BoatMats.HeliRed].Cylinder(new Vector3(0f, 0.62f, -2.5f), new Vector3(0f, 0.85f, -7.0f), 0.32f, 0.14f, 6, false, true);
            set[BoatMats.HeliYellow].Cylinder(new Vector3(0f, 0.717f, -4.4f), new Vector3(0f, 0.735f, -4.75f), 0.26f, 0.245f, 6, false, false);
            set[BoatMats.HeliRed].TaperBox(new Vector3(0f, 1.35f, -7.05f), new Vector2(0.08f, 0.8f), new Vector2(0.06f, 0.5f), 1.1f, Quaternion.Euler(-20f, 0f, 0f));
            set[BoatMats.HeliRed].Box(new Vector3(0f, 0.45f, -6.9f), new Vector3(0.07f, 0.5f, 0.4f), Quaternion.Euler(25f, 0f, 0f));
            set[BoatMats.HeliYellow].Box(new Vector3(0f, 0.82f, -5.9f), new Vector3(1.8f, 0.07f, 0.45f));
            set[BoatMats.HeliRed].Box(new Vector3(-0.9f, 0.82f, -5.9f), new Vector3(0.06f, 0.32f, 0.36f));
            set[BoatMats.HeliRed].Box(new Vector3(0.9f, 0.82f, -5.9f), new Vector3(0.06f, 0.32f, 0.36f));

            // skids
            var dk = set[BoatMats.HeliDark];
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * 0.95f;
                dk.Cylinder(new Vector3(x, -1.35f, -1.6f), new Vector3(x, -1.35f, 1.7f), 0.06f, 0.06f, 6);
                dk.Cylinder(new Vector3(x, -1.35f, 1.7f), new Vector3(x, -1.12f, 2.05f), 0.06f, 0.05f, 6);
                dk.Cylinder(new Vector3(side * 0.45f, -0.75f, 0.9f), new Vector3(x, -1.35f, 0.9f), 0.05f, 0.05f, 6, false, false);
                dk.Cylinder(new Vector3(side * 0.45f, -0.75f, -0.8f), new Vector3(x, -1.35f, -0.8f), 0.05f, 0.05f, 6, false, false);
            }

            // nav lights + landing light
            set[BoatMats.NavRed].Sphere(new Vector3(-0.95f, 0.82f, -5.9f), 0.07f, 5, 3);
            set[BoatMats.NavGreen].Sphere(new Vector3(0.95f, 0.82f, -5.9f), 0.07f, 5, 3);
            set[BoatMats.NavWhite].Sphere(new Vector3(0f, 0.92f, -7.12f), 0.06f, 5, 3);
            set[BoatMats.NavWhite].Disc(new Vector3(0f, -0.66f, 2.25f), new Vector3(0f, -0.8f, 0.6f).normalized, 0.1f, 6);
            set[BoatMats.HeliDark].Box(new Vector3(0f, -0.9f, 0.2f), new Vector3(0.2f, 0.14f, 0.2f));

            BoatOwnedMesh.Own(set.Build("Body", root, true));

            // beacon (blinks)
            var bs = new BoatMeshSet();
            bs[BoatMats.NavRed].Sphere(Vector3.zero, 0.09f, 6, 3);
            var beacon = BoatOwnedMesh.Own(bs.Build("Beacon", root, new Vector3(0f, 1.4f, -1.4f), Quaternion.identity, false));
            parts.beacon = beacon.GetComponent<Renderer>();

            // main rotor: chunky hub, four flat blades, a faint blur disc
            var ms = new BoatMeshSet();
            ms[BoatMats.HeliDark].Cylinder(new Vector3(0f, -0.06f, 0f), new Vector3(0f, 0.1f, 0f), 0.18f, 0.15f, 6);
            ms[BoatMats.HeliRed].Cylinder(new Vector3(0f, 0.1f, 0f), new Vector3(0f, 0.16f, 0f), 0.08f, 0.05f, 6);
            for (int k = 0; k < 4; k++)
            {
                Quaternion r = Quaternion.Euler(0f, k * 90f, 0f);
                ms[BoatMats.HeliDark].Box(r * new Vector3(0f, 0f, 2.6f), new Vector3(0.3f, 0.06f, 5.0f), r * Quaternion.Euler(-1.2f, 0f, 3f));
                ms[BoatMats.HeliYellow].Box(r * new Vector3(0f, 0.002f, 4.9f), new Vector3(0.31f, 0.062f, 0.4f), r * Quaternion.Euler(-1.2f, 0f, 3f));
            }
            ms[BoatMats.RotorBlur].Disc(new Vector3(0f, 0.01f, 0f), Vector3.up, 5.1f, 16);
            var main = BoatOwnedMesh.Own(ms.Build("MainRotor", root, new Vector3(0f, 1.75f, 0.1f), Quaternion.identity, true));
            parts.mainRotor = main.transform;

            // tail rotor (spins around local X)
            var ts = new BoatMeshSet();
            ts[BoatMats.HeliDark].Cylinder(new Vector3(-0.06f, 0f, 0f), new Vector3(0.08f, 0f, 0f), 0.07f, 0.07f, 6);
            ts[BoatMats.HeliDark].Box(Vector3.zero, new Vector3(0.04f, 1.4f, 0.14f), Quaternion.Euler(8f, 0f, 0f));
            ts[BoatMats.RotorBlur].Disc(new Vector3(0.02f, 0f, 0f), Vector3.right, 0.72f, 10);
            var tail = BoatOwnedMesh.Own(ts.Build("TailRotor", root, new Vector3(0.12f, 1.45f, -7.15f), Quaternion.identity, false));
            parts.tailRotor = tail.transform;

            var hook = new GameObject("CargoHook").transform;
            hook.SetParent(root, false);
            hook.localPosition = new Vector3(0f, -0.95f, 0.2f);
            parts.hook = hook;
            return parts;
        }
    }

    /// One delivery flight: inbound from ~900 m at 70 m altitude, descend over the helipad, hover, drop crates on
    /// parachutes, climb out and despawn.
    public class HeliChopper : MonoBehaviour
    {
        enum Phase { Inbound, Descend, Hover, Climb, Outbound }

        const float CruiseAlt = 70f, CruiseSpeed = 42f, HoverAGL = 24f, SpawnDist = 900f, DespawnDist = 1050f;

        HeliService service;
        HeliModel.Parts parts;
        readonly List<HeliCrate> cargo = new List<HeliCrate>();
        Phase phase;
        Vector3 vel, prevVel, accS, exitDir;
        float yaw, pitch, roll, timer, padY;
        int dropIndex;
        AudioSource rotorSrc;
        ParticleSystem downwash;
        Light beaconLight;
        bool washOnWater = true;

        public static HeliChopper Spawn(HeliService svc, Transform parent, List<Delivery> deliveries)
        {
            Vector3 pad = svc.Pad;
            Vector3 outward = pad - WorldLayout.IslandCenter;
            outward.y = 0f;
            if (outward.sqrMagnitude < 1f) outward = Vector3.forward;
            outward.Normalize();
            Vector3 dir = Quaternion.Euler(0f, Random.Range(-55f, 55f), 0f) * outward;
            Vector3 start = pad + dir * SpawnDist;
            start.y = CruiseAlt;

            var go = new GameObject("DeliveryChopper");
            go.transform.SetParent(parent, false);
            go.transform.position = start;
            var h = go.AddComponent<HeliChopper>();
            h.Init(svc, deliveries, dir);
            return h;
        }

        void Init(HeliService svc, List<Delivery> deliveries, Vector3 fromDir)
        {
            service = svc;
            parts = HeliModel.Build(transform);
            yaw = Mathf.Atan2(-fromDir.x, -fromDir.z) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            vel = -fromDir * CruiseSpeed;
            prevVel = vel;
            exitDir = Quaternion.Euler(0f, Random.Range(-40f, 40f), 0f) * fromDir;

            int n = deliveries.Count;
            for (int i = 0; i < n; i++)
            {
                float zOff = (i - (n - 1) * 0.5f) * 1.0f;
                cargo.Add(HeliCrate.CreateHanging(svc, parts.hook, deliveries[i], zOff));
            }

            rotorSrc = BoatSynth.LoopSource(gameObject, BoatSynth.Rotor, 15f, 900f, AudioRolloffMode.Linear);
            rotorSrc.volume = 0.9f;
            rotorSrc.dopplerLevel = 0.6f;
            rotorSrc.Play();

            downwash = BoatParticles.Downwash(transform.parent);
            BoatParticles.SetRate(downwash, 0f);

            var lgo = new GameObject("BeaconLight");
            lgo.transform.SetParent(parts.beacon.transform, false);
            beaconLight = lgo.AddComponent<Light>();
            beaconLight.type = LightType.Point;
            beaconLight.color = new Color(1f, 0.15f, 0.1f);
            beaconLight.range = 14f;
            beaconLight.shadows = LightShadows.None;
            beaconLight.enabled = false;

            GameEvents.RaiseToast(Loc.Tr("Der Liefer-Heli ist im Anflug!", "The delivery chopper is on its way!"));
        }

        void OnDestroy()
        {
            if (downwash != null) Destroy(downwash.gameObject);
        }

        Vector3 HoldOver(Vector3 pad, Vector3 pos)
        {
            Vector3 h = new Vector3(pad.x - pos.x, 0f, pad.z - pos.z) * 0.8f;
            return Vector3.ClampMagnitude(h, 3f);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || service == null) return;
            Vector3 pos = transform.position;
            Vector3 pad = service.Pad;

            switch (phase)
            {
                case Phase.Inbound:
                {
                    Vector3 to = new Vector3(pad.x - pos.x, 0f, pad.z - pos.z);
                    float dist = to.magnitude;
                    float spd = Mathf.Min(CruiseSpeed, Mathf.Sqrt(2f * 3.2f * dist) + 0.3f);
                    Vector3 desired = dist > 0.01f ? to / dist * spd : Vector3.zero;
                    desired.y = Mathf.Clamp((CruiseAlt - pos.y) * 0.5f, -6f, 6f);
                    vel = Vector3.MoveTowards(vel, desired, 7f * dt);
                    if (dist < 3f && new Vector2(vel.x, vel.z).magnitude < 2.5f)
                    {
                        phase = Phase.Descend;
                        padY = service.PadSurfaceY();
                    }
                    break;
                }
                case Phase.Descend:
                {
                    Vector3 hold = HoldOver(pad, pos);
                    float targetY = padY + HoverAGL;
                    hold.y = Mathf.Clamp((targetY - pos.y) * 0.7f, -7f, 4f);
                    vel = Vector3.MoveTowards(vel, hold, 6f * dt);
                    if (Mathf.Abs(targetY - pos.y) < 0.6f)
                    {
                        phase = Phase.Hover;
                        timer = 1.5f;
                    }
                    break;
                }
                case Phase.Hover:
                {
                    Vector3 hold = HoldOver(pad, pos);
                    hold.y = (padY + HoverAGL - pos.y) * 0.8f;
                    vel = Vector3.MoveTowards(vel, hold, 4f * dt);
                    timer -= dt;
                    if (timer <= 0f)
                    {
                        if (dropIndex < cargo.Count)
                        {
                            DropNext();
                            timer = dropIndex < cargo.Count ? 1.1f : 2f;
                        }
                        else phase = Phase.Climb;
                    }
                    break;
                }
                case Phase.Climb:
                {
                    Vector3 desired = new Vector3(exitDir.x * 12f, Mathf.Clamp((CruiseAlt - pos.y) * 0.6f, 0f, 7f), exitDir.z * 12f);
                    vel = Vector3.MoveTowards(vel, desired, 5f * dt);
                    if (pos.y > CruiseAlt - 3f) phase = Phase.Outbound;
                    break;
                }
                case Phase.Outbound:
                {
                    Vector3 desired = exitDir * (CruiseSpeed * 1.1f);
                    desired.y = Mathf.Clamp((CruiseAlt + 10f - pos.y) * 0.5f, -4f, 6f);
                    vel = Vector3.MoveTowards(vel, desired, 6f * dt);
                    if (new Vector2(pos.x - pad.x, pos.z - pad.z).magnitude > DespawnDist)
                    {
                        Destroy(gameObject);
                        return;
                    }
                    break;
                }
            }

            pos += vel * dt;
            transform.position = pos;
            Orient(dt);
            parts.mainRotor.Rotate(0f, 1300f * dt, 0f, Space.Self);
            parts.tailRotor.Rotate(2600f * dt, 0f, 0f, Space.Self);
            UpdateDownwash(pos);
            UpdateBeacon();
            rotorSrc.pitch = 1f + new Vector2(vel.x, vel.z).magnitude / CruiseSpeed * 0.07f + (phase == Phase.Climb ? 0.05f : 0f);
            rotorSrc.volume = 0.9f * Settings.SfxVolume;
        }

        void Orient(float dt)
        {
            Vector3 hv = new Vector3(vel.x, 0f, vel.z);
            float targetYaw = yaw;
            if (phase == Phase.Climb || phase == Phase.Outbound) targetYaw = Mathf.Atan2(exitDir.x, exitDir.z) * Mathf.Rad2Deg;
            else if (hv.magnitude > 4f) targetYaw = Mathf.Atan2(hv.x, hv.z) * Mathf.Rad2Deg;
            float newYaw = Mathf.MoveTowardsAngle(yaw, targetYaw, 40f * dt);
            float yawRate = Mathf.DeltaAngle(yaw, newYaw) / dt;
            yaw = newYaw;

            Vector3 acc = (vel - prevVel) / dt;
            prevVel = vel;
            accS = Vector3.Lerp(accS, acc, 1f - Mathf.Exp(-dt * 3f));
            Quaternion inv = Quaternion.Inverse(Quaternion.Euler(0f, yaw, 0f));
            Vector3 lv = inv * vel, la = inv * accS;
            float wob = (phase == Phase.Hover || phase == Phase.Descend) ? 1f : 0.3f;
            float tPitch = Mathf.Clamp(lv.z * 0.22f + la.z * 1.5f, -14f, 16f) + Mathf.Sin(Time.time * 1.3f) * wob;
            float tRoll = Mathf.Clamp(-yawRate * 0.4f - la.x * 1.5f, -22f, 22f) + Mathf.Sin(Time.time * 0.9f + 1f) * wob;
            float k = 1f - Mathf.Exp(-dt * 2.5f);
            pitch = Mathf.Lerp(pitch, tPitch, k);
            roll = Mathf.Lerp(roll, tRoll, k);
            transform.rotation = Quaternion.Euler(pitch, yaw, roll);
        }

        void UpdateDownwash(Vector3 pos)
        {
            if (downwash == null) return;
            if (pos.y - padY > 60f && phase != Phase.Descend && phase != Phase.Hover)
            {
                BoatParticles.SetRate(downwash, 0f);
                return;
            }
            float surf = BoatSurface.Below(pos + Vector3.down * 2f, 60f, out bool water);
            float h = pos.y - surf;
            if (h > 40f)
            {
                BoatParticles.SetRate(downwash, 0f);
                return;
            }
            downwash.transform.position = new Vector3(pos.x, surf + 0.15f, pos.z);
            if (water != washOnWater)
            {
                washOnWater = water;
                var main = downwash.main;
                main.startColor = water ? new ParticleSystem.MinMaxGradient(new Color(0.9f, 0.95f, 1f, 0.45f))
                                        : new ParticleSystem.MinMaxGradient(new Color(0.72f, 0.66f, 0.55f, 0.4f));
            }
            BoatParticles.SetRate(downwash, (1f - h / 40f) * 70f);
        }

        void UpdateBeacon()
        {
            bool on = Mathf.Repeat(Time.time, 1.2f) < 0.15f;
            parts.beacon.enabled = on;
            float night = EnvState.Night01;
            beaconLight.enabled = on && night > 0.15f;
            beaconLight.intensity = 3f * night;
        }

        void DropNext()
        {
            var crate = cargo[dropIndex++];
            if (crate == null) return;   // opened elsewhere meanwhile (co-op) – nothing to drop
            int slot = service.FreeSlot();
            Vector3 target = service.SlotPosition(slot);
            bool drift = HeliService.DriftsAway(crate.Delivery);   // same result on every co-op machine
            if (drift)
            {
                if (service.FindDriftTarget(out Vector3 water)) target = water;
                else drift = false;
            }
            crate.Release(slot, target, drift);
        }
    }
}
