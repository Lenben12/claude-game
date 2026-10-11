using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sealebrities
{
    /// Spawns, pools, ticks and despawns all creatures (ICreatureSpawner, registered as Refs.Spawner).
    ///
    /// Round 2b (L.1 "fish everywhere"): open-world spawning. Around every diver the spawner keeps the target of the
    /// zone the diver is in (FaunaZones: site zones, lagoon, open sea, deep sea) inside an 80 m ring. Each spawn picks a
    /// point around the diver, takes the zone AT THAT POINT, chooses the spawn depth first (70 % within ±12 m of the
    /// diver, 30 % anywhere in the water column) and weights the zone's species by rarity × depth preference (1.0 at
    /// prefDepth → 0.35 at the band edges → 0 after a 10 % margin). A species needs its zone, an open gate
    /// (Progression.IsGateOpen: home site unlocked / story flag), a fitting time of day and its depth band; bottom
    /// species use the seafloor depth and sit at a matching habitat anchor (FaunaAnchors) within 30 m.
    /// Locked site: its own pool is off, open-water species spawn at 50 % density. Open sea: + 1 school. At the surface
    /// (diver < 2 m deep or on the boat) in open sea / lagoon ≥ 6 surface animals stay within 40 m.
    /// Co-op: per-diver populations (max 60 per diver), F.4 danger multipliers, creatures react to the nearest diver and
    /// are only despawned when far from ALL divers. Client (NetFlags.CreaturesRemote): only hallucinations locally.
    /// No per-frame allocations (zone species lists are built once).
    public sealed class FaunaSpawner : MonoBehaviour, ICreatureSpawner
    {
        public const int MaxActive = 160;
        public const int MaxHallucinations = 6;
        public const float PopulationRadius = 80f;
        public const float CountRadius = 85f;
        public const float DespawnDistance = 120f;
        public const float MinSpawnDistance = 12f;
        /// Spawn points closer than this must be out of view (beyond it the underwater fog hides the pop-in).
        public const float InViewSpawnGuard = 24f;
        /// Share of the population spawns that go into the visible band around the diver (sight + 8 m).
        public const float NearSpawnShare = 0.6f;
        public const float NearLodDistance = 60f;
        public const float FarLodDistance = 110f;
        /// Habitat anchors are used within this distance of a bottom species' spawn point (L.1).
        public const float HabitatRange = 30f;

        FaunaPool pool;
        readonly int[] nearPer = new int[FaunaContext.MaxDivers];
        readonly int[] schoolsPer = new int[FaunaContext.MaxDivers];
        readonly int[] surfacePer = new int[FaunaContext.MaxDivers];
        /// Animals inside the visible band (sight + 8 m) per diver – topped up separately so swimming along does
        /// not leave the water in view empty while the 80 m ring still counts as full (playtest round 2).
        readonly int[] sightPer = new int[FaunaContext.MaxDivers];
        float sightBand = 30f, sightTimer;
        /// Share of the zone target that should be inside the visible band (at most SightMax animals).
        public const float SightShare = 0.45f;
        public const int SightMax = 12;
        /// Open water (open sea, Fog Isle) never looks empty: at least this many animals in the visible band.
        public const int OpenWaterSightMin = 11;
        /// T1 reward „Hausriff +10 % Tiere“ (Tomas: "Jetzt kommen sie zurück") once the ghost net is cut.
        public const float GhostNetBonus = 1.1f;
        bool ghostNetCleared;
        /// Animals per visible-band top-up (a school arrives as a small group; it never adds a whole school).
        public const int TopUpGroup = 4;
        readonly string[] diverZone = new string[FaunaContext.MaxDivers];
        int netSerial;
        float remotePrewarmTimer;
        readonly List<FaunaCreature> active = new List<FaunaCreature>(MaxActive + 16);
        readonly List<FaunaSchool> schools = new List<FaunaSchool>(64);
        readonly Stack<FaunaSchool> schoolPool = new Stack<FaunaSchool>(32);
        readonly Dictionary<string, List<SpeciesDef>> zoneSpecies = new Dictionary<string, List<SpeciesDef>>();
        readonly Dictionary<SpeciesDef, int> individuals = new Dictionary<SpeciesDef, int>();
        readonly List<SpeciesDef> prewarmQueue = new List<SpeciesDef>(24);
        readonly List<SpeciesDef> pickList = new List<SpeciesDef>(48);
        readonly List<float> pickWeight = new List<float>(48);
        static readonly List<SpeciesDef> Empty = new List<SpeciesDef>(0);

        System.Random rng;
        int frame;
        int serial;
        bool ticking;
        int hallucinationCount;
        float zoneTimer, despawnTimer;
        DiveSite currentSite;
        string currentZone;
        string prewarmZone;
        DiveSite lockHintSite;   // locked site whose "locked" hint was already shown since entering it

        /// Dive-site zone the local diver is in (null in the lagoon / open water / on land).
        public DiveSite CurrentSite => currentSite;
        /// L.1 zone of the local diver (site id, "lagoon", "open_sea", "deep_sea", "island").
        public string CurrentZone => currentZone;
        public int ActiveCount => active.Count;
        /// Live creatures including hallucinations (read-only; used by the Net module to replicate the host's animals).
        public IReadOnlyList<FaunaCreature> ActiveCreatures => active;

        /// A fresh inactive copy of a species model without creature logic (co-op proxies on clients).
        public GameObject CloneModel(SpeciesDef s, Transform parent) => s == null ? null : pool.CloneModel(s, parent);

        /// Real animals within a horizontal radius of a point (tests, census).
        public int CountNear(Vector3 p, float radius, bool includeLand = false)
        {
            int n = 0;
            float r2 = radius * radius;
            for (int i = 0; i < active.Count; i++)
            {
                var c = active[i];
                if (ReferenceEquals(c, null) || c == null || c.hallucination || !c.alive) continue;
                if (!includeLand && (c.landSpawned || c.mode == FaunaMode.Fly)) continue;
                if (FaunaNav.DistXZSqr(c.pos, p) <= r2) n++;
            }
            return n;
        }

        // ================================================================== lifecycle

        void Awake()
        {
            rng = new System.Random(System.Environment.TickCount);
            pool = new FaunaPool(transform);
            FaunaBubbles.Install(transform);
            FaunaFx.Install(transform);
            // L.1: species lists per zone, built once (water animals only – island / beach / tide-pool life is LandFauna's)
            string[] zones = { Zones.HouseReef, Zones.Kelp, Zones.Wreck, Zones.BlueDrop, Zones.Trench, Zones.FogIsle, Zones.Lagoon, Zones.OpenSea, Zones.DeepSea };
            for (int i = 0; i < zones.Length; i++)
            {
                var l = new List<SpeciesDef>();
                SpeciesDatabase.ByZone(zones[i], l);
                for (int j = l.Count - 1; j >= 0; j--) if (l[j].land || FaunaModes.Of(l[j]) == FaunaMode.Fly) l.RemoveAt(j);
                zoneSpecies[zones[i]] = l;
            }
            for (int i = 0; i < DiveSites.All.Count; i++)
                if (!zoneSpecies.ContainsKey(DiveSites.All[i].id)) zoneSpecies[DiveSites.All[i].id] = new List<SpeciesDef>();
        }

        void OnEnable()
        {
            GameEvents.ReturnedToMenu += OnReturnedToMenu;
            GameEvents.GameStarted += OnGameStarted;
            GameEvents.PlayerFainted += OnPlayerFainted;
            GameEvents.DayStarted += OnDayStarted;
        }

        void OnDisable()
        {
            GameEvents.ReturnedToMenu -= OnReturnedToMenu;
            GameEvents.GameStarted -= OnGameStarted;
            GameEvents.PlayerFainted -= OnPlayerFainted;
            GameEvents.DayStarted -= OnDayStarted;
        }

        void OnDestroy()
        {
            if (ReferenceEquals(Refs.Spawner, this)) Refs.Spawner = null;
        }

        void OnReturnedToMenu() { DespawnAll(); lockHintSite = null; }
        void OnGameStarted()
        {
            DespawnAll();
            lockHintSite = null;
            FaunaContext.CalmUntil = Time.time + 5f;
            FaunaContext.SessionCalmUntil = Time.time + 5f;
        }
        void OnPlayerFainted() { FaunaContext.CalmUntil = Time.time + 30f; }

        /// After a night's sleep (screen is black, clock set to morning) the reef gets fresh animals and sizes.
        /// The midnight rollover also raises DayStarted, but mid-dive: there nothing may pop out of existence.
        void OnDayStarted(int day)
        {
            var gm = GameManager.I;
            if (gm == null || !gm.InGame) return;
            if (Mathf.Abs(gm.State.timeOfDay - GameManager.MorningTime) > 0.01f) return;
            DespawnAll();
        }

        // ================================================================== ICreatureSpawner

        public ICreature Spawn(SpeciesDef species, Vector3 position, float sizeCm, bool hallucination)
        {
            if (species == null) return null;
            if (!hallucination && NetFlags.CreaturesRemote) return null;   // co-op client: the host owns the real animals
            FaunaContext.Capture();

            if (hallucination)
            {
                // nitrogen narcosis only exists on the harder difficulties (Advanced, Technical, Abyss)
                var gm = GameManager.I;
                if (gm != null && !gm.Difficulty.narcosis) return null;
                if (hallucinationCount >= MaxHallucinations) return null;
                if (sizeCm <= 0f)
                {
                    var t = rng.NextDouble() < 0.6 ? SizeTier.Huge : SizeTier.Legendary;
                    sizeCm = Progression.RollSizeCm(species, t, rng);
                }
                if (FaunaContext.HasPlayer)
                {
                    float d = Vector3.Distance(position, FaunaContext.CamPos);
                    if (d < 6.5f || d > 40f || position == Vector3.zero)
                    {
                        position = FaunaContext.CamPos + FaunaContext.CamForward * Random.Range(8f, 12f)
                                   + FaunaContext.CamRight * Random.Range(-3f, 3f) + Vector3.up * Random.Range(-1.5f, 1f);
                    }
                }
            }
            else
            {
                if (active.Count >= MaxActive) return null;
                if (sizeCm <= 0f) sizeCm = Progression.RollSizeCm(species, Progression.RollTier(rng), rng);
            }

            float lenM = sizeCm / 100f;
            float half = Mathf.Max(0.04f, species.bodyHeight) * lenM * 0.5f;
            var mode = FaunaModes.Of(species);
            if (mode == FaunaMode.Swim || hallucination)
            {
                if (!FaunaNav.ClampIntoWater(ref position, 0.3f + half, half)) return null;   // never on land
            }
            else if (mode == FaunaMode.Ground)
            {
                position.y = FaunaNav.SurfaceBelow(position + Vector3.up * 1f, 3f) + half;
            }

            FaunaSchool school = null;
            if (!hallucination && UsesSchool(species))
            {
                school = TakeSchool();
                school.Init(species, position, position, 30f, lenM, 1);
            }
            var c = CreateCreature(species, position, sizeCm, hallucination, school, position, 30f);
            if (c == null && school != null) ReleaseSchool(school);
            return c;
        }

        public void Despawn(ICreature creature)
        {
            var fc = creature as FaunaCreature;
            if (ReferenceEquals(fc, null)) return;
            // hallucinations dissolve quickly instead of popping out of existence
            if (fc != null && fc.hallucination && fc.alive && !fc.pendingDespawn && fc.behavior is FaunaHallucinationBehavior hb)
            {
                hb.FadeOut(0.6f);
                return;
            }
            if (ticking) { fc.pendingDespawn = true; return; }
            int i = fc.listIndex;
            if (i >= 0 && i < active.Count && ReferenceEquals(active[i], fc)) DespawnAt(i);
            else if (fc != null && fc.gameObject.activeSelf) fc.Deactivate();
        }

        // ================================================================== creation / removal

        FaunaCreature CreateCreature(SpeciesDef s, Vector3 p, float cm, bool halluc, FaunaSchool school, Vector3 leashC, float leashR)
        {
            FaunaCreature c = pool.Take(s, halluc);
            if (c == null) return null;
            c.slot = serial++ % 6;
            c.listIndex = active.Count;
            active.Add(c);
            if (halluc)
            {
                hallucinationCount++;
                c.netId = 0;
            }
            else
            {
                individuals.TryGetValue(s, out int n);
                individuals[s] = n + 1;
                netSerial = netSerial >= 65535 ? 1 : netSerial + 1;
                c.netId = netSerial;
            }
            if (school != null) school.members.Add(c);
            if (FaunaContext.HasPlayer)
            {
                c.distToPlayer = Vector3.Distance(p, FaunaContext.PlayerPos);
                c.distXZ = FaunaNav.DistXZ(p, FaunaContext.PlayerPos);
                c.distLocal = FaunaContext.DiverCount > 1 ? Vector3.Distance(p, FaunaContext.DiverList[0].head) : c.distToPlayer;
            }
            c.Activate(this, s, cm, halluc, p, school, leashC, leashR);
            return c;
        }

        void DespawnAt(int i) => RemoveAt(i, false);

        void RemoveAt(int i, bool destroyed)
        {
            var c = active[i];
            int last = active.Count - 1;
            if (i != last)
            {
                var moved = active[last];
                active[i] = moved;
                moved.listIndex = i;
            }
            active.RemoveAt(last);
            if (ReferenceEquals(c, null)) return;
            c.listIndex = -1;

            if (c.hallucination) hallucinationCount = Mathf.Max(0, hallucinationCount - 1);
            else if (c.species != null && individuals.TryGetValue(c.species, out int n)) individuals[c.species] = Mathf.Max(0, n - 1);

            if (c.school != null) { c.school.Remove(c); c.school = null; }
            if (destroyed || c == null) return;   // destroyed externally: nothing to pool
            bool halluc = c.hallucination;
            c.director = null;
            c.Deactivate();
            if (halluc) Destroy(c.gameObject);
            else pool.Return(c);
        }

        /// Called from FaunaCreature.OnDestroy when an instance gets destroyed by someone else.
        internal void NotifyDestroyed(FaunaCreature c)
        {
            if (ticking) { c.pendingDespawn = true; return; }   // swept by the update loop
            int i = c.listIndex;
            if (i >= 0 && i < active.Count && ReferenceEquals(active[i], c)) RemoveAt(i, true);
        }

        public void DespawnAll()
        {
            if (ticking)
            {
                // called from inside a creature tick (event listener): sweep at the end of this frame
                for (int i = 0; i < active.Count; i++) if (!ReferenceEquals(active[i], null)) active[i].pendingDespawn = true;
                return;
            }
            for (int i = active.Count - 1; i >= 0; i--) DespawnAt(i);
            for (int i = schools.Count - 1; i >= 0; i--) ReleaseSchoolAt(i);
        }

        internal FaunaSchool TakeSchool()
        {
            var s = schoolPool.Count > 0 ? schoolPool.Pop() : new FaunaSchool();
            schools.Add(s);
            return s;
        }

        internal void ReleaseSchool(FaunaSchool s)
        {
            int i = schools.IndexOf(s);
            if (i >= 0) ReleaseSchoolAt(i);
        }

        void ReleaseSchoolAt(int i)
        {
            var s = schools[i];
            int last = schools.Count - 1;
            schools[i] = schools[last];
            schools.RemoveAt(last);
            s.members.Clear();
            s.species = null;
            s.director = null;
            schoolPool.Push(s);
        }

        // ================================================================== frame loop

        void Update()
        {
            frame++;
            FaunaContext.Capture();
            CreatureGlow.Tick(Time.time);
            FaunaAnchors.Ensure();
            var gm = GameManager.I;
            if (gm == null || !gm.InGame)
            {
                if (active.Count > 0) DespawnAll();
                // while the menu is up, quietly pre-build the house reef models (the player always wakes up there)
                if (WorldInfo.Built && (frame % 8) == 0) PrewarmStartSite();
                return;
            }
            // frozen while the pause menu is open (offline only: in co-op the world keeps running for the others)
            if (UIState.PauseOpen && !Net.IsOnline) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            dt = Mathf.Min(dt, 0.1f);

            bool hasPlayer = FaunaContext.HasPlayer;
            bool remoteFauna = NetFlags.CreaturesRemote;
            if (remoteFauna) DropRealCreatures();   // co-op client: the host's animals arrive as net proxies
            bool multi = hasPlayer && FaunaContext.DiverCount > 1;
            int nd = hasPlayer ? FaunaContext.DiverCount : 0;
            Array.Clear(nearPer, 0, nearPer.Length);
            Array.Clear(schoolsPer, 0, schoolsPer.Length);
            Array.Clear(surfacePer, 0, surfacePer.Length);
            Array.Clear(sightPer, 0, sightPer.Length);
            // counted band = what the diver can see (+2 m); the top-up spawns up to sight + 8 m (SpawnGroup)
            sightBand = Mathf.Clamp((FXVisibility.Underwater ? FXVisibility.Meters : 25f) + 2f, MinSpawnDistance + 2f, PopulationRadius);

            // schools first (leaders move, members follow)
            for (int i = schools.Count - 1; i >= 0; i--)
            {
                var s = schools[i];
                if (s.members.Count == 0) { ReleaseSchoolAt(i); continue; }
                if (multi) FaunaContext.SetFocus(FaunaContext.NearestDiver(s.leaderPos, out _));
                s.Tick(dt);
                if (hasPlayer && s.members.Count >= 6)
                    for (int k = 0; k < nd; k++)
                        if (FaunaNav.DistXZ(s.leaderPos, FaunaContext.DiverList[k].head) < CountRadius) { schoolsPer[k]++; nearPer[k] += 6; }   // a big school counts as 6
            }
            if (multi) FaunaContext.SetFocus(0);

            despawnTimer -= dt;
            bool despawnCheck = despawnTimer <= 0f;
            if (despawnCheck) despawnTimer = 0.5f;

            ticking = true;
            // full-rate ticks only where animals can be seen: under water the fog limit (8–19 m) + a margin
            float nearLodD = FXVisibility.Underwater ? Mathf.Clamp(FXVisibility.Meters + 15f, 25f, NearLodDistance) : NearLodDistance;
            float farLodD = Mathf.Max(nearLodD * 1.8f, nearLodD + 10f);
            DayMask now = FaunaContext.DayNow;
            for (int i = 0; i < active.Count; i++)
            {
                var c = active[i];
                if (c == null) { if (!ReferenceEquals(c, null)) c.pendingDespawn = true; continue; }
                if (c.pendingDespawn) continue;

                float d3 = 0f, dxz = 0f, dxzAll = 0f, dLocal = 0f;
                if (hasPlayer)
                {
                    Vector3 cp = c.pos;
                    if (multi && !c.hallucination)
                    {
                        MeasureDivers(c, cp, out d3, out dxz, out dxzAll, out dLocal);
                    }
                    else
                    {
                        // single player (and hallucinations, which only ever exist for the local diver)
                        if (multi) FaunaContext.SetFocus(0);
                        Vector3 lp = multi ? FaunaContext.DiverList[0].head : FaunaContext.PlayerPos;
                        float dx = cp.x - lp.x, dy = cp.y - lp.y, dz = cp.z - lp.z;
                        dxz = Mathf.Sqrt(dx * dx + dz * dz);
                        d3 = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
                        dxzAll = dxz;
                        dLocal = d3;
                        if (!c.hallucination) CountFor(0, c, dxz);
                    }
                }
                c.distToPlayer = d3;
                c.distXZ = dxz;
                c.distLocal = dLocal;

                if (!c.hallucination && hasPlayer && despawnCheck)
                {
                    float limit = c.landSpawned ? LandFauna.DespawnDistance : DespawnDistance;
                    if (dxzAll > limit && c.director == null) { c.pendingDespawn = true; continue; }
                    // out of season: day species leave at night (only far away and out of view)
                    if (!c.species.FitsTime(now) && !c.landSpawned && c.director == null && dxzAll > 35f && !FaunaContext.InView(c.pos))
                    { c.pendingDespawn = true; continue; }
                }

                c.accumDt += dt;
                int interval = d3 < nearLodD ? 1 : (d3 < farLodD ? 3 : 6);
                if (interval == 1 || ((frame + c.slot) % interval) == 0)
                {
                    float step = Mathf.Min(c.accumDt, 0.3f);
                    c.accumDt = 0f;
                    c.Tick(step, interval == 1);
                }
            }
            ticking = false;
            if (multi) FaunaContext.SetFocus(0);

            // sweep despawns (swap-remove, iterate backwards)
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var c = active[i];
                if (ReferenceEquals(c, null) || c == null || c.pendingDespawn) DespawnAt(i);
            }

            if (hasPlayer) Populate(dt);
        }

        void CountFor(int k, FaunaCreature c, float dxz)
        {
            if (c.landSpawned || c.mode == FaunaMode.Fly) return;
            if (dxz < CountRadius && (c.school == null || c.school.members.Count < 6)) nearPer[k]++;
            if (dxz < sightBand) sightPer[k]++;
            if (dxz < FaunaZones.SurfaceRange && c.pos.y > c.surfaceY - 3.5f) surfacePer[k]++;
        }

        /// Co-op: distances from a creature to every diver. Focuses the nearest one (3D, head) for the behavior tick,
        /// counts the creature for every diver it is near and reports the smallest horizontal distance (despawn).
        void MeasureDivers(FaunaCreature c, Vector3 cp, out float d3, out float dxz, out float dxzAll, out float dLocal)
        {
            var list = FaunaContext.DiverList;
            int n = FaunaContext.DiverCount;
            int best = 0;
            float best3 = float.MaxValue, bestXZ = 0f, minXZ = float.MaxValue;
            dLocal = 0f;
            for (int k = 0; k < n; k++)
            {
                Vector3 h = list[k].head;
                float dx = cp.x - h.x, dy = cp.y - h.y, dz = cp.z - h.z;
                float xz2 = dx * dx + dz * dz;
                float s3 = xz2 + dy * dy;
                float xz = Mathf.Sqrt(xz2);
                CountFor(k, c, xz);
                if (xz < minXZ) minXZ = xz;
                if (s3 < best3) { best3 = s3; best = k; bestXZ = xz; }
                if (k == 0) dLocal = Mathf.Sqrt(s3);
            }
            FaunaContext.SetFocus(best);
            d3 = Mathf.Sqrt(best3);
            dxz = bestXZ;
            dxzAll = minXZ;
        }

        /// Co-op client: real animals that existed before the session switched to remote fauna are removed.
        void DropRealCreatures()
        {
            if (active.Count <= hallucinationCount || ticking) return;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var c = active[i];
                if (ReferenceEquals(c, null) || c == null || !c.hallucination) DespawnAt(i);
            }
        }

        // ================================================================== population (L.1)

        void Populate(float dt)
        {
            int nd = FaunaContext.DiverCount;
            bool multi = nd > 1;
            sightTimer -= dt;
            zoneTimer -= dt;
            if (zoneTimer <= 0f)
            {
                zoneTimer = 0.5f;
                ghostNetCleared = StoryFlags.Has(NpcKeys.GhostNetCleared);
                for (int k = 0; k < nd; k++) diverZone[k] = ZoneOfDiver(k);
                currentZone = diverZone[0];
                var site = DiveSites.Get(currentZone);
                if (site != currentSite) lockHintSite = null;
                currentSite = site;
                UpdatePrewarm(currentZone);
                LockedHint();
                if (multi)
                {
                    remotePrewarmTimer -= 0.5f;
                    if (remotePrewarmTimer <= 0f)
                    {
                        remotePrewarmTimer = 1f;
                        for (int k = 1; k < nd; k++) QueuePrewarm(diverZone[k]);
                    }
                }
            }

            // build one missing template per frame for the zone we are in / approaching (avoids a hitch on arrival)
            if (prewarmQueue.Count > 0)
            {
                var s = prewarmQueue[prewarmQueue.Count - 1];
                prewarmQueue.RemoveAt(prewarmQueue.Count - 1);
                if (!pool.HasTemplate(s)) { pool.Template(s); return; }
            }
            if (NetFlags.CreaturesRemote) return;   // co-op client: the host populates the sea
            if (!FaunaAnchors.Built) FaunaAnchors.Ensure();

            int budget = 2;   // groups per frame over all divers
            for (int k = 0; k < nd && budget > 0; k++)
            {
                if (active.Count >= MaxActive) break;
                string zone = diverZone[k];
                if (zone == null || zone == Zones.Island) continue;   // on land: the island fauna is LandFauna's
                FaunaContext.SetFocus(k);   // spawn out of THIS diver's view
                budget -= PopulateDiver(k, zone, multi, budget);
            }
            FaunaContext.SetFocus(0);
        }

        /// Zone of diver k: the zone at the head; a diver standing on land near the water (dock, beach) keeps the reef
        /// alive below the dock, a diver far inland gets none.
        string ZoneOfDiver(int k)
        {
            ref FaunaDiver d = ref FaunaContext.DiverList[k];
            string z = FaunaZones.ZoneAt(d.head);
            if (z != Zones.Island) return z;
            // on land: look 12 m ahead / around for water (dock, boat at the jetty, beach)
            Vector3 f = d.forward; f.y = 0f;
            if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
            f.Normalize();
            string ahead = FaunaZones.ZoneAt(d.rootPos + f * 12f);
            return ahead;
        }

        int PopulateDiver(int k, string zone, bool multi, int budget)
        {
            ref FaunaDiver d = ref FaunaContext.DiverList[k];
            Vector3 hp = d.head;
            var site = DiveSites.Get(zone);
            bool locked = site != null && GameManager.I != null && !Progression.IsSiteUnlocked(site);
            float target = FaunaZones.Target(zone) * (locked ? FaunaZones.LockedDensity : 1f);
            if (locked) target = Mathf.Max(target, FaunaZones.Target(Zones.OpenSea) * FaunaZones.LockedDensity);
            if (FaunaContext.NightNow) target *= FaunaZones.NightFactor;
            if (ghostNetCleared && zone == Zones.HouseReef) target *= GhostNetBonus;
            if (multi) target = Mathf.Min(target, FaunaZones.MaxPerDiverCoop);
            int have = nearPer[k];
            int used = 0;

            // the surface is alive too (open sea / lagoon, diver at the surface or on the boat)
            bool atSurface = !d.underwater || (Waves.SeaLevel - hp.y) < 2f;
            if (atSurface && (zone == Zones.OpenSea || zone == Zones.Lagoon || zone == Zones.DeepSea) && surfacePer[k] < FaunaZones.SurfaceTarget && used < budget)
            {
                int got = SpawnGroup(zone, locked, hp, d.head.y, true, false);
                if (got > 0) { used++; surfacePer[k] += got; have += got; }
            }
            // open sea: + 1 school
            if (FaunaZones.ExtraSchools(zone) > schoolsPer[k] && used < budget)
            {
                int got = SpawnGroup(zone, locked, hp, hp.y, false, true);
                if (got > 0) { used++; schoolsPer[k]++; }
            }
            if (have >= target)
            {
                // the ring is full – but is the water the diver can see populated? (swimming along leaves the
                // animals behind in the ring while the band in front of the mask empties)
                // capped (a big zone target must not pull 30+ fully ticked animals into view – iteration 3 of the
                // playtest halved the FPS that way), small groups only, and the ring never grows: every top-up first
                // frees far slots (≥ 55 m, out of view); nothing far to free → the population stays as it is
                int sightTarget = Mathf.Min(Mathf.CeilToInt(target * SightShare), SightMax);
                if (locked || zone == Zones.OpenSea || zone == Zones.FogIsle) sightTarget = Mathf.Max(sightTarget, OpenWaterSightMin);   // a locked site is open water too
                if (sightPer[k] >= sightTarget || sightTimer > 0f || used >= budget) return used;
                sightTimer = 0.4f;
                if (have >= target || active.Count >= MaxActive - 8)
                {
                    if (TrimFar(TopUpGroup) == 0) return used;
                }
                if (active.Count >= MaxActive - 4) return used;   // the trimmed ones leave next frame
                int got = SpawnGroup(zone, locked, hp, hp.y, false, false, true, TopUpGroup);
                if (got > 0) { sightPer[k] += got; nearPer[k] = have + got; }
                return used + 1;
            }
            // the ring is still filling: when the visible band is empty (zones of big schools – big schools do not count
            // for `have`, so the ring could stay "unfilled" with every school far away), the first group of this frame
            // spawns right at the fog edge (census: Fog Isle 104 animals within 80 m, 0 within sight)
            int fillSight = Mathf.Min(Mathf.CeilToInt(target * SightShare), SightMax);
            if (locked || zone == Zones.OpenSea || zone == Zones.FogIsle) fillSight = Mathf.Max(fillSight, OpenWaterSightMin);
            int groups = have < target / 2 ? 2 : 1;
            for (int g = 0; g < groups && used < budget; g++)
            {
                if (active.Count >= MaxActive) break;
                bool near = g == 0 && sightPer[k] < fillSight;
                int got = SpawnGroup(zone, locked, hp, hp.y, false, false, near, near ? TopUpGroup * 2 : int.MaxValue);
                used++;
                if (got <= 0) continue;
                if (near) sightPer[k] += got;
                have += got;
                if (have >= target) break;
            }
            nearPer[k] = have;
            return used;
        }

        void LockedHint()
        {
            var site = currentSite;
            if (site == null || GameManager.I == null) return;
            if (Progression.IsSiteUnlocked(site)) return;
            if (lockHintSite == site) return;
            // only once actually inside the site (the zone margin reaches the island shore)
            if (FaunaNav.DistXZ(FaunaContext.DiverList[0].head, site.center) > site.radius) return;
            lockHintSite = site;
            Progression.CheckUnlocks();   // requirements may be met but not processed yet
            if (Progression.IsSiteUnlocked(site)) return;
            // L.10: icon + one short line + chip
            GameEvents.RaiseNotify(NotifyKind.Warning, Loc.Tr($"{site.Name} gesperrt", $"{site.Name} locked"), LockChip(site));
        }

        static string LockChip(DiveSite site)
        {
            if (site.IsStorySite) return Loc.Tr("Karte fehlt", "Map needed");
            var s = GameManager.I.State;
            if (site.prevSiteId != null && Journal.SiteCompletion(site.prevSiteId) < site.prevCompletionNeeded)
            {
                // short page name („Wrack 60 %“): „Sealog Wrack „MS Clickbait“ 60 %“ ran across the held camera
                return Zones.Name(site.prevSiteId) + " " + Loc.Percent(site.prevCompletionNeeded);
            }
            if (s.followers < site.followersNeeded) return Loc.Number(site.followersNeeded) + " F";
            return null;
        }

        // ------------------------------------------------------------------ prewarm

        void UpdatePrewarm(string zone)
        {
            if (zone == null || zone == prewarmZone) return;
            prewarmZone = zone;
            prewarmQueue.Clear();
            QueuePrewarm(zone);
            // the sites we are approaching
            var all = DiveSites.All;
            Vector3 p = FaunaContext.DiverList[0].head;
            for (int i = 0; i < all.Count; i++)
                if (FaunaNav.DistXZ(p, all[i].center) - all[i].radius < 300f) QueuePrewarm(all[i].id);
        }

        void QueuePrewarm(string zone)
        {
            if (zone == null || !zoneSpecies.TryGetValue(zone, out var list)) return;
            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i];
                if (pool.HasTemplate(s) || prewarmQueue.Contains(s)) continue;
                if (GameManager.I != null && !Progression.IsGateOpen(s)) continue;
                prewarmQueue.Add(s);
            }
        }

        void PrewarmStartSite()
        {
            if (!zoneSpecies.TryGetValue(Zones.HouseReef, out var list)) return;
            for (int i = 0; i < list.Count; i++)
            {
                if (pool.HasTemplate(list[i])) continue;
                pool.Template(list[i]);
                return;   // one per call
            }
        }

        // ------------------------------------------------------------------ species choice

        static bool UsesSchool(SpeciesDef s) => s.behavior == BehaviorType.Schooling || s.behavior == BehaviorType.Playful;

        /// Real members of one school (big schools get visual buddies from CreatureSwarm).
        public static int SchoolCap(SpeciesDef s)
        {
            if (s.groupMax <= 12) return s.groupMax;
            return Mathf.Clamp(s.groupMax / 5, 12, 28);
        }

        int MaxGroups(SpeciesDef s)
        {
            if (s.unique) return 1;
            int cap = 999;
            if (s.rarity == Rarity.Legendary) cap = 1;
            else if (s.rarity == Rarity.Epic) cap = 2;
            if (s.behavior == BehaviorType.Curious) cap = Mathf.Min(cap, 3);
            return cap;
        }

        bool Eligible(SpeciesDef s)
        {
            individuals.TryGetValue(s, out int n);
            int per = UsesSchool(s) ? SchoolCap(s) : Mathf.Max(1, s.groupMax);
            return n < (s.unique ? 1 : MaxGroups(s) * per);
        }

        /// L.1 spawn weight of a species at a point (0 = not eligible). depth = spawn depth for open-water animals,
        /// seafloor = water depth at the point (bottom animals live on the seafloor).
        float Weight(SpeciesDef s, Vector3 point, float depth, float seafloor, bool surfaceOnly, bool schoolOnly)
        {
            if (!Eligible(s)) return 0f;
            if (GameManager.I != null && !Progression.IsGateOpen(s)) return 0f;
            if (!s.FitsTime(FaunaContext.DayNow)) return 0f;
            if (schoolOnly && !(UsesSchool(s) && s.groupMax >= 4)) return 0f;
            if (s.homeRange > 0f && FaunaZones.HomeAnchor(s, out Vector3 anchor, out float baseR) && FaunaNav.DistXZ(point, anchor) > baseR + s.homeRange) return 0f;
            bool night = FaunaContext.NightNow;
            bool bottom = FaunaModes.IsBottom(s);
            if (surfaceOnly && bottom) return 0f;
            float dw;
            if (bottom)
            {
                dw = s.DepthWeight(seafloor, night);
                if (dw <= 0f) return 0f;
                var hab = FaunaAnchors.HabitatOf(s);
                // anemone / sea-fan dwellers only live at their anchor
                if ((hab == FaunaHabitat.Anemone || hab == FaunaHabitat.SeaFan) && FaunaAnchors.Habitats.Count > 0
                    && !FaunaAnchors.FindHabitat(point, HabitatRange, hab, 0, out _)) return 0f;
            }
            else
            {
                dw = s.DepthWeight(depth, night);
                if (dw <= 0f) return 0f;
                if (s.BandMin(night) > seafloor - 0.5f) return 0f;   // water too shallow for the band
            }
            float w = s.SpawnWeight * dw;
            if (FaunaContext.DayNow != DayMask.Day) w *= s.nightWeight;
            if (s.IsDangerous) w *= FaunaContext.PredatorCountMul;
            if (s.plan == BodyPlan.Jelly) w *= FaunaContext.JellyCountMul;
            if (surfaceOnly && s.BandMin(night) <= 1f && s.minDepth <= 2f) w *= 2f;   // flying fish, dolphins, turtles up top
            return w;
        }

        SpeciesDef Pick(List<SpeciesDef> list, Vector3 point, float depth, float seafloor, bool surfaceOnly, bool schoolOnly)
        {
            pickList.Clear();
            pickWeight.Clear();
            float total = 0f;
            for (int i = 0; i < list.Count; i++)
            {
                float w = Weight(list[i], point, depth, seafloor, surfaceOnly, schoolOnly);
                if (w <= 0f) continue;
                pickList.Add(list[i]);
                pickWeight.Add(w);
                total += w;
            }
            if (total <= 0f) return null;
            float r = (float)rng.NextDouble() * total;
            for (int i = 0; i < pickList.Count; i++)
            {
                r -= pickWeight[i];
                if (r <= 0f) return pickList[i];
            }
            return pickList[pickList.Count - 1];
        }

        List<SpeciesDef> ListFor(string zone, bool lockedSite, float seafloor)
        {
            if (lockedSite) return zoneSpecies.TryGetValue(seafloor >= FaunaZones.DeepSeaDepth ? Zones.DeepSea : Zones.OpenSea, out var o) ? o : Empty;
            return zoneSpecies.TryGetValue(zone, out var l) ? l : Empty;
        }

        /// One spawn attempt around a diver: point → zone at the point → depth → species → group. Returns the count.
        /// Marks up to `count` of the farthest animals (no event director, no unique, out of view, ≥ 55 m away – school
        /// members included) for despawn so the visible-band top-up never runs into MaxActive and never grows the
        /// population without limit.
        int TrimFar(int count)
        {
            int freed = 0;
            for (int n = 0; n < count; n++)
            {
                int best = -1;
                float bestD = 55f;
                for (int i = 0; i < active.Count; i++)
                {
                    var c = active[i];
                    if (ReferenceEquals(c, null) || c == null || c.pendingDespawn || c.hallucination || c.landSpawned) continue;
                    if (c.director != null || (c.species != null && c.species.unique)) continue;
                    if (c.distXZ <= bestD || FaunaContext.InView(c.pos)) continue;
                    bestD = c.distXZ;
                    best = i;
                }
                if (best < 0) return freed;
                active[best].pendingDespawn = true;
                freed++;
            }
            return freed;
        }

        int SpawnGroup(string diverZoneId, bool diverZoneLocked, Vector3 diverHead, float diverY, bool surfaceOnly, bool schoolOnly, bool forceNear = false, int maxCount = int.MaxValue)
        {
            // playtest round 2 ("überall Fische"): most groups spawn inside the band the diver can actually see
            // (MinSpawnDistance … sight + 8 m), the rest anywhere in the 80 m ring – the ring target stays, but the
            // animals are no longer spread so thin that the 19 m of underwater sight show empty water
            float sight = FXVisibility.Underwater ? FXVisibility.Meters : 25f;
            float nearMax = Mathf.Clamp(sight + 8f, MinSpawnDistance + 8f, PopulationRadius);
            // in-view spawns are fine once they are inside the fog (they fade in while they swim closer)
            float viewGuard = Mathf.Clamp(sight, MinSpawnDistance, InViewSpawnGuard);
            for (int attempt = 0; attempt < 5; attempt++)
            {
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                float maxR = surfaceOnly ? FaunaZones.SurfaceRange : PopulationRadius;
                if (forceNear && !surfaceOnly && !schoolOnly) maxR = Mathf.Clamp(sight + 2f, MinSpawnDistance + 4f, PopulationRadius);   // top-up: right at the fog edge
                else if (!surfaceOnly && !schoolOnly && rng.NextDouble() < NearSpawnShare) maxR = nearMax;
                float dist = Mathf.Lerp(MinSpawnDistance, maxR, (float)rng.NextDouble());
                var p = new Vector3(diverHead.x + Mathf.Cos(ang) * dist, 0f, diverHead.z + Mathf.Sin(ang) * dist);
                float g = WorldInfo.GroundHeight(p);
                if (g > FaunaNav.LandGuardY - 0.8f) continue;   // land, beach or ankle-deep water
                float seafloor = Waves.SeaLevel - g;

                string zone = FaunaZones.ZoneAt(p);
                if (zone == Zones.Island) continue;
                var site = DiveSites.Get(zone);
                bool locked = site != null && GameManager.I != null && !Progression.IsSiteUnlocked(site);

                // spawn depth first: 70 % near the diver's depth, 30 % anywhere in the water column (surface: top 2 m)
                float diverDepth = Mathf.Max(0.5f, Waves.SeaLevel - diverY);
                float depth;
                if (surfaceOnly) depth = 0.4f + (float)rng.NextDouble() * 1.8f;
                else if (rng.NextDouble() < 0.7) depth = diverDepth + ((float)rng.NextDouble() * 2f - 1f) * 12f;
                else depth = (float)rng.NextDouble() * seafloor;
                depth = Mathf.Clamp(depth, 0.6f, Mathf.Max(0.6f, seafloor - 0.5f));

                var list = ListFor(zone, locked, seafloor);
                var s = Pick(list, p, depth, seafloor, surfaceOnly, schoolOnly);
                if (s == null && (zone == Zones.DeepSea || zone == Zones.Trench) && !locked)
                    s = Pick(zoneSpecies[Zones.OpenSea], p, Mathf.Min(depth, 30f), seafloor, surfaceOnly, schoolOnly);   // never empty water
                if (s == null) continue;

                bool bottom = FaunaModes.IsBottom(s);
                float y = bottom ? g : Waves.SeaLevel - depth;
                // L.3: bottom species sit at a matching habitat anchor within 30 m
                if (bottom)
                {
                    var hab = FaunaAnchors.HabitatOf(s);
                    if (hab != FaunaHabitat.None && FaunaAnchors.FindHabitat(p, HabitatRange, hab, serial, out Vector3 hpnt))
                    {
                        float hg = WorldInfo.GroundHeight(hpnt);
                        if (s.DepthWeight(Waves.SeaLevel - hg, FaunaContext.NightNow) > 0f) { p = hpnt; g = Mathf.Max(hg, hpnt.y); y = g; }
                    }
                }
                p.y = y;
                // spawn in the fog or out of view: never pop in right in front of the camera
                if (FaunaNav.DistXZ(p, diverHead) < viewGuard && FaunaContext.InView(p)) continue;

                Vector3 leashC;
                float leashR;
                if (site != null && !locked) { leashC = site.center; leashR = site.radius + FaunaZones.ZoneMargin; }
                else { leashC = p; leashR = zone == Zones.Lagoon ? 45f : 70f; }
                if (s.homeRange > 0f && FaunaZones.HomeAnchor(s, out Vector3 anchor, out float baseR)) { leashC = anchor; leashR = baseR + s.homeRange; }
                int count = rng.Next(s.groupMin, Mathf.Max(s.groupMin, s.groupMax) + 1);
                if (UsesSchool(s)) count = Mathf.Min(count, SchoolCap(s));
                count = Mathf.Max(1, Mathf.Min(count, maxCount));
                return SpawnAt(s, p, count, leashC, leashR, bottom, null);
            }
            return 0;
        }

        /// Spawns a group of a species around center (bottom = on the seafloor / ground). Schools / pods get a shared
        /// FaunaSchool. Used by the population, LandFauna and FaunaEvents. Returns how many were created; created
        /// creatures are appended to outList when given.
        internal int SpawnAt(SpeciesDef s, Vector3 center, int count, Vector3 leashC, float leashR, bool bottom, List<FaunaCreature> outList, FaunaSchool useSchool = null)
        {
            if (s == null || NetFlags.CreaturesRemote) return 0;
            if (s.unique) count = Mathf.Min(count, 1);
            count = Mathf.Min(count, MaxActive - active.Count);
            if (count <= 0) return 0;
            float typicalLen = (s.minCm + s.maxCm) * 0.005f;
            float half = Mathf.Max(0.04f, s.bodyHeight) * typicalLen * 0.5f;
            var mode = FaunaModes.Of(s);
            float clear = (bottom ? 0.15f : 0.6f) + half;
            if (mode == FaunaMode.Swim)
            {
                if (bottom) center.y = WorldInfo.GroundHeight(center) + clear;
                if (!FaunaNav.ClampIntoWater(ref center, clear, half)) return 0;
            }
            else if (mode == FaunaMode.Ground) center.y = FaunaNav.SurfaceBelow(center + Vector3.up * 0.8f, 2.5f) + half;

            FaunaSchool school = useSchool;
            if (school == null && UsesSchool(s) && mode == FaunaMode.Swim)
            {
                school = TakeSchool();
                school.Init(s, center, leashC, leashR, typicalLen, count);
            }

            int spawned = 0;
            for (int i = 0; i < count; i++)
            {
                Vector3 p = center;
                if (i > 0)
                {
                    if (school != null)
                    {
                        Vector3 o = Random.insideUnitSphere * school.spread;
                        o.y *= 0.45f;
                        p = (school.anchored ? school.home : center) + o;
                    }
                    else
                    {
                        Vector2 o = Random.insideUnitCircle * Mathf.Clamp(typicalLen * 3f, 1f, 8f);
                        p = center + new Vector3(o.x, Random.Range(-1f, 1f) * (bottom ? 0.2f : 1.5f), o.y);
                    }
                    if (mode == FaunaMode.Swim) { if (!FaunaNav.ClampIntoWater(ref p, clear, half)) p = center; }
                    else if (mode == FaunaMode.Ground) p.y = FaunaNav.SurfaceBelow(p + Vector3.up * 0.8f, 2.5f) + half;
                }
                else if (school != null && school.anchored) p = school.home;

                float cm = Progression.RollSizeCm(s, Progression.RollTier(rng), rng);
                var c = CreateCreature(s, p, cm, false, school, leashC, leashR);
                if (c == null) break;
                spawned++;
                outList?.Add(c);
            }
            if (spawned == 0 && school != null && useSchool == null) ReleaseSchool(school);
            return spawned;
        }

        /// Creates one creature of a species exactly at p (LandFauna home spots, events). Null when full / remote.
        internal FaunaCreature SpawnOne(SpeciesDef s, Vector3 p, Vector3 leashC, float leashR)
        {
            if (s == null || NetFlags.CreaturesRemote || active.Count >= MaxActive) return null;
            float cm = Progression.RollSizeCm(s, Progression.RollTier(rng), rng);
            return CreateCreature(s, p, cm, false, null, leashC, leashR);
        }

        /// How many real individuals of a species are alive (uniques, land caps).
        public int Individuals(SpeciesDef s) => s != null && individuals.TryGetValue(s, out int n) ? n : 0;

        /// Random number source of the spawner (host-side gameplay randomness).
        internal System.Random Rng => rng;
    }
}
