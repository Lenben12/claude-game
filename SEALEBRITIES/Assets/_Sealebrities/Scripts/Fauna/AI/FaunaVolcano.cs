using UnityEngine;

namespace Sealebrities
{
    /// Round 3 (GDD §20 Q13/Q14): the volcano island's fauna. The living fossils (nautilus, horseshoe crab, sixgill and
    /// frilled shark, sea lily, hagfish) come from the normal population of the zone Zones.Volcano (FaunaZones,
    /// FaunaSpawner – only once VolcanoGate.Open, i.e. the wreck is unlocked). This director owns the ONE big
    /// prehistoric monster, the mosasaurus "Big Mo": it lives in the Urzeit-Schlund (WorldLayout.VolcanoAbyss, ~128 m)
    /// and is spawned (host / offline) while a diver is within MonsterRange of the sinkhole. Its behaviour is
    /// FaunaMonsterBehavior (lurk deep → rise and circle a diver who comes over the sinkhole, announced by the danger
    /// music → sometimes one lunge → back down; enough weapon hits drive it away).
    public sealed class FaunaVolcano : MonoBehaviour
    {
        public const string MonsterId = "mosasaurus";
        /// Horizontal distance from the sinkhole within which Big Mo is around.
        public const float MonsterRange = 105f;
        /// Story flag (shared) once the crew drove Big Mo away for the first time.
        public const string MonsterRepelledFlag = "big_mo_repelled";

        float timer;

        public static Vector3 AbyssHome
        {
            get
            {
                Vector3 a = PropInfo.Spot("volcano_abyss", out var s) ? s : WorldLayout.VolcanoAbyss;
                float floor = WorldInfo.GroundHeight(a);
                float y = Mathf.Max(floor + 14f, Waves.SeaLevel - 100f);
                return new Vector3(a.x, Mathf.Min(y, Waves.SeaLevel - 30f), a.z);
            }
        }

        /// The monster's creature (host / offline), or null.
        public static FaunaCreature Monster
        {
            get
            {
                var sp = FaunaModule.Spawner;
                var def = SpeciesDatabase.Get(MonsterId);
                if (sp == null || def == null) return null;
                var list = sp.ActiveCreatures;
                for (int i = 0; i < list.Count; i++)
                    if (list[i] != null && list[i].species == def && list[i].alive && !list[i].pendingDespawn) return list[i];
                return null;
            }
        }

        void Update()
        {
            timer -= Time.deltaTime;
            if (timer > 0f) return;
            timer = 1f;
            var gm = GameManager.I;
            var spawner = FaunaModule.Spawner;
            if (gm == null || !gm.InGame || spawner == null || !Net.HasAuthority || NetFlags.CreaturesRemote || !FaunaContext.HasPlayer) return;
            if (!VolcanoGate.Open) return;
            var def = SpeciesDatabase.Get(MonsterId);
            if (def == null || spawner.Individuals(def) > 0) return;
            Vector3 home = AbyssHome;
            bool near = false;
            for (int k = 0; k < FaunaContext.DiverCount; k++)
            {
                ref FaunaDiver d = ref FaunaContext.DiverList[k];
                if (d.inWater && FaunaNav.DistXZ(d.head, home) < MonsterRange) { near = true; break; }
            }
            if (!near) return;
            SpawnMonster(home);
        }

        /// Spawns Big Mo deep in the sinkhole (also used by tests). Null when the spawner refuses.
        public static FaunaCreature SpawnMonster(Vector3 home)
        {
            var spawner = FaunaModule.Spawner;
            var def = SpeciesDatabase.Get(MonsterId);
            if (spawner == null || def == null) return null;
            var existing = Monster;
            if (existing != null) return existing;
            Vector3 p = home;
            FaunaNav.ClampIntoWater(ref p, 4f, 2f);
            var c = spawner.SpawnOne(def, p, home, WorldLayout.VolcanoAbyssRadius + 25f);
            if (c != null) Debug.Log("[Fauna] Big Mo rises in the Urzeit-Schlund at " + p.ToString("F0"));
            return c;
        }
    }

    /// The mosasaurus "Big Mo" (round 3, Q14/Q16/Q45). States:
    ///  Lurk    slow laps deep in the sinkhole (70–115 m).
    ///  Rise    a diver comes over the sinkhole (or within 45 m): it comes up to 14 m from them, a little below.
    ///  Circle  big closing circles (16 → 9 m) for 12–16 s – the danger music + "Etwas Riesiges kommt …" (FaunaThreat.Cue).
    ///          The first visit of a session is only an inspection (a close pass, no lunge) – spectacular and filmable.
    ///  Lunge   one charge; a hit = bite (55 "Puste", 20 s air, likely a lost clip) – never death.
    ///  Retreat back down for 30 s; then a 60–90 s rest before it notices anyone again.
    ///  Flee    enough weapon hits (scare ≥ 60) drive it to the bottom for 90 s – "Abgewehrt!" + stream hype + flag.
    public sealed class FaunaMonsterBehavior : FaunaBehavior
    {
        enum St { Lurk, Rise, Circle, Pass, Lunge, Retreat, Flee }
        St st;
        float timer, cooldown, orbit, orbitDir, radius, lapAngle, startle;
        int victimId;
        bool victimLocal, cueOn, inspected;
        Vector3 home, passTarget;

        public int State => (int)st;

        public override void Reset()
        {
            home = FaunaVolcano.AbyssHome;
            c.home = home;
            c.leashCenter = home;
            c.leashRadius = WorldLayout.VolcanoAbyssRadius + 25f;
            st = St.Lurk;
            cooldown = 6f;
            lapAngle = Random.value * Mathf.PI * 2f;
            orbitDir = Random.value < 0.5f ? -1f : 1f;
            cueOn = false;
            c.scare = 0f;
        }

        /// A weapon hit (host side). Returns true when the hit drove it off ("won").
        public bool OnWeaponHit(float damage)
        {
            bool hunting = st == St.Circle || st == St.Lunge || st == St.Pass || st == St.Rise;
            if (c.scare >= 60f || st == St.Lunge)
            {
                // a hit right into the lunge or enough hits: Big Mo gives up and dives to the bottom for a while
                CueOff();
                st = St.Flee;
                timer = 90f;
                c.scare = 0f;
                if (!StoryFlags.Has(FaunaVolcano.MonsterRepelledFlag)) StoryFlags.Set(FaunaVolcano.MonsterRepelledFlag);
                FaunaReact.Startle(c.pos, 20f, 3f);
                return true;
            }
            // any other hit during the hunt drives it back down for a moment (still "repelled")
            if (hunting) { CueOff(); st = St.Retreat; timer = 12f; cooldown = 25f; return true; }
            return false;
        }

        void CueOff()
        {
            if (!cueOn) return;
            cueOn = false;
            FaunaThreat.Cue(victimId, victimLocal, false, c.species);
        }

        bool Victim(out Vector3 head)
        {
            head = Vector3.zero;
            if (!FaunaThreat.TryVictim(victimId, out var d) || !d.underwater) return false;
            head = d.head;
            return true;
        }

        public override void Tick(float dt)
        {
            timer -= dt;
            cooldown -= dt;
            c.activityTarget = 0.3f;
            switch (st)
            {
                case St.Lurk:
                    {
                        lapAngle += dt * orbitDir * Mathf.Max(0.4f, c.cruise * 0.5f) / 26f;
                        Vector3 tgt = home + new Vector3(Mathf.Sin(lapAngle), 0f, Mathf.Cos(lapAngle)) * 26f;
                        tgt.y = home.y + Mathf.Sin(lapAngle * 0.7f) * 10f;
                        c.desiredVel = c.Seek(tgt, c.cruise * 0.55f, 6f);
                        // a diver over the sinkhole (or close by): come up and have a look
                        if (cooldown <= 0f && FaunaContext.HasPlayer && FaunaContext.PlayerUnderwater && (Waves.SeaLevel - FaunaContext.PlayerPos.y) > 6f)
                        {
                            float dh = FaunaNav.DistXZ(FaunaContext.PlayerPos, home);
                            if (dh < WorldLayout.VolcanoAbyssRadius + 12f || c.distToPlayer < 45f)
                            {
                                victimId = FaunaContext.FocusNetId;
                                victimLocal = FaunaContext.FocusLocal;
                                st = St.Rise;
                                timer = 45f;   // ~90 m up from the bottom of the sinkhole
                            }
                        }
                        break;
                    }
                case St.Rise:
                    {
                        c.relaxBand = true;
                        if (!Victim(out var head) || FaunaNav.DistXZ(head, home) > 95f) { st = St.Retreat; timer = 20f; break; }
                        Vector3 off = c.pos - head;
                        off.y = 0f;
                        if (off.sqrMagnitude < 1e-3f) off = Vector3.forward;
                        Vector3 tgt = head + off.normalized * 14f + Vector3.down * 4f;
                        // final test: at 2.2 m/s it never made the ~90 m from the bottom of the sinkhole within the 30 s
                        // and went back down without ever circling – it surges up now
                        c.desiredVel = c.Seek(tgt, Mathf.Max(c.cruise * 1.8f, 5f), 8f);
                        c.accelMul = 2.5f;
                        c.activityTarget = 0.6f;
                        if ((c.pos - head).sqrMagnitude < 19f * 19f)
                        {
                            st = St.Circle;
                            timer = Random.Range(12f, 16f);
                            radius = 16f;
                            orbit = Mathf.Atan2(off.x, off.z);
                            cueOn = true;
                            FaunaThreat.Cue(victimId, victimLocal, true, c.species);
                        }
                        else if (timer <= 0f) { st = St.Retreat; timer = 20f; }
                        break;
                    }
                case St.Circle:
                    {
                        c.relaxBand = true;
                        if (!Victim(out var head)) { CueOff(); st = St.Retreat; timer = 20f; break; }
                        radius = Mathf.MoveTowards(radius, 9f, dt * 0.55f);
                        orbit += dt * orbitDir * Mathf.Max(1.2f, c.cruise) / radius;
                        Vector3 tgt = head + new Vector3(Mathf.Sin(orbit), 0f, Mathf.Cos(orbit)) * radius;
                        tgt.y = head.y - 3f + Mathf.Sin(orbit * 1.3f) * 1.5f;
                        Vector3 tangent = new Vector3(Mathf.Cos(orbit), 0f, -Mathf.Sin(orbit)) * orbitDir;
                        c.desiredVel = c.Seek(tgt, Mathf.Max(c.cruise * 1.5f, 2.4f), 4f) + tangent * 1.2f;
                        c.hasFaceDir = true;
                        c.faceDir = Vector3.Lerp(tangent, (head - c.pos).normalized, 0.3f);
                        c.turnMul = 2.5f;
                        c.activityTarget = 0.75f;
                        startle -= dt;
                        if (startle <= 0f) { startle = 1f; FaunaReact.Startle(head, 16f, 1.5f); }
                        if (timer <= 0f)
                        {
                            bool lunge = inspected && Random.value < 0.6f;
                            inspected = true;
                            if (lunge) { st = St.Lunge; timer = 4f; }
                            else
                            {
                                // the inspection pass: right past the diver's mask
                                Vector3 side = Vector3.Cross(Vector3.up, (head - c.pos).normalized);
                                passTarget = head + side * 5f + (head - c.pos).normalized * 25f;
                                passTarget.y = head.y - 1f;
                                st = St.Pass;
                                timer = 9f;
                            }
                        }
                        break;
                    }
                case St.Pass:
                    c.relaxBand = true;
                    c.desiredVel = c.Seek(passTarget, Mathf.Max(c.cruise * 1.6f, 3f), 4f);
                    c.activityTarget = 0.8f;
                    if (timer <= 0f || (passTarget - c.pos).sqrMagnitude < 16f) { CueOff(); st = St.Retreat; timer = 25f; cooldown = 60f; }
                    break;
                case St.Lunge:
                    {
                        c.relaxBand = true;
                        if (!Victim(out var head)) { CueOff(); st = St.Retreat; timer = 20f; break; }
                        Vector3 to = head - c.HeadPos;
                        float hd = to.magnitude;
                        c.desiredVel = hd > 1e-3f ? to * (6.5f / hd) : c.Transform.forward * 6.5f;
                        c.accelMul = 3f;
                        c.turnMul = 3f;
                        c.activityTarget = 1f;
                        Action(1f);   // jaw open
                        if (hd < 3.2f)
                        {
                            FaunaThreat.Bite(victimId, victimLocal, c.HeadPos, c.species, 55f, 20f, 0.7f);
                            CueOff();
                            st = St.Retreat;
                            timer = 30f;
                            cooldown = Random.Range(60f, 90f);
                        }
                        else if (timer <= 0f) { CueOff(); st = St.Retreat; timer = 25f; cooldown = Random.Range(40f, 70f); }
                        break;
                    }
                case St.Retreat:
                    {
                        Action(0f);
                        c.relaxBand = true;
                        Vector3 tgt = home + Vector3.down * 5f;
                        c.desiredVel = c.Seek(tgt, Mathf.Max(c.cruise, 2f), 8f);
                        c.activityTarget = 0.5f;
                        if (timer <= 0f) { st = St.Lurk; if (cooldown < 20f) cooldown = 20f; }
                        break;
                    }
                case St.Flee:
                    {
                        Action(0f);
                        c.relaxBand = true;
                        Vector3 tgt = home + Vector3.down * 12f;
                        c.desiredVel = c.Seek(tgt, Mathf.Max(c.cruise * 2f, 3f), 6f);
                        c.activityTarget = 0.7f;
                        if (timer <= 0f) { st = St.Lurk; cooldown = 30f; }
                        break;
                    }
            }
            if (st != St.Lunge && st != St.Retreat && st != St.Flee) Action(0.15f + 0.1f * Mathf.Sin(FaunaContext.Time * 0.6f));
        }
    }
}
