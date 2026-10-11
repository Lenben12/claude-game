using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sealebrities
{
    /// Spawns, pools, ticks and despawns all creatures (ICreatureSpawner, registered as Refs.Spawner).
    /// Keeps a per-site target population within ~80 m of the player while in game, prefers spawning out of view or
    /// in the fog, despawns animals more than 120 m away, caps the active count at 160 and updates far creatures
    /// at a lower rate. No per-frame allocations.
    /// Co-op (host / offline authority): every diver in FaunaContext.DiverList gets its own population, creatures react
    /// to the diver nearest to them and a creature is only despawned when it is far from ALL divers.
    /// Co-op client (NetFlags.CreaturesRemote): no real animals are spawned here (the host's animals arrive as net
    /// proxies); only local narcosis hallucinations exist.
    public sealed class FaunaSpawner : MonoBehaviour, ICreatureSpawner
    {
        public const int MaxActive = 160;
        public const int MaxHallucinations = 6;
        // Population ring tuned for the 15–20 m underwater visibility (art bible §2.2 / fauna checklist): the same
        // per-site counts live in a smaller ring, so about twice as many animals are actually within sight.
        // Previous values: population 110 m, count 120 m, despawn 150 m, nearest spawn 14 m, in-view guard 35 m.
        public const float PopulationRadius = 80f;
        public const float CountRadius = 85f;
        public const float DespawnDistance = 120f;
        public const float MinSpawnDistance = 12f;
        /// Spawn points closer than this must be out of view (beyond it the underwater fog hides the pop-in).
        public const float InViewSpawnGuard = 24f;
        public const float SiteFalloff = 150f;
        public const float NearLodDistance = 60f;
        public const float FarLodDistance = 110f;

        FaunaPool pool;
        readonly int[] nearPer = new int[FaunaContext.MaxDivers];
        int netSerial;
        float remotePrewarmTimer;
        readonly List<FaunaCreature> active = new List<FaunaCreature>(MaxActive + 16);
        readonly List<FaunaSchool> schools = new List<FaunaSchool>(64);
        readonly Stack<FaunaSchool> schoolPool = new Stack<FaunaSchool>(32);
        readonly Dictionary<string, List<SpeciesDef>> siteSpecies = new Dictionary<string, List<SpeciesDef>>();
        readonly Dictionary<SpeciesDef, int> individuals = new Dictionary<SpeciesDef, int>();
        readonly List<SpeciesDef> prewarmQueue = new List<SpeciesDef>(16);

        System.Random rng;
        int frame;
        int serial;
        bool ticking;
        int nearCount;
        int hallucinationCount;
        float siteTimer, despawnTimer;
        DiveSite currentSite;
        float siteWeight;
        DiveSite prewarmSite;
        DiveSite lockHintSite;   // locked site whose "locked" hint was already shown since entering it

        /// Dive site whose fauna is currently being populated (null in open water / on land far from sites).
        public DiveSite CurrentSite => currentSite;
        public int ActiveCount => active.Count;
        /// Live creatures including hallucinations (read-only; used by the Net module to replicate the host's animals).
        public IReadOnlyList<FaunaCreature> ActiveCreatures => active;

        /// A fresh inactive copy of a species model without creature logic (co-op proxies on clients).
        public GameObject CloneModel(SpeciesDef s, Transform parent) => s == null ? null : pool.CloneModel(s, parent);

        // ================================================================== lifecycle

        void Awake()
        {
            rng = new System.Random(System.Environment.TickCount);
            pool = new FaunaPool(transform);
            FaunaBubbles.Install(transform);
            for (int i = 0; i < DiveSites.All.Count; i++)
            {
                var site = DiveSites.All[i];
                siteSpecies[site.id] = SpeciesDatabase.BySite(site.id);
            }
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
            if (!FaunaNav.ClampIntoWater(ref position, 0.3f + half, half)) return null;   // never on land

            FaunaSchool school = null;
            if (!hallucination && species.behavior == BehaviorType.Schooling)
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
            nearCount = 0;
        }

        FaunaSchool TakeSchool()
        {
            var s = schoolPool.Count > 0 ? schoolPool.Pop() : new FaunaSchool();
            schools.Add(s);
            return s;
        }

        void ReleaseSchool(FaunaSchool s)
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
            schoolPool.Push(s);
        }

        // ================================================================== frame loop

        void Update()
        {
            frame++;
            FaunaContext.Capture();
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
            Vector3 pp = FaunaContext.PlayerPos;
            bool remoteFauna = NetFlags.CreaturesRemote;
            if (remoteFauna) DropRealCreatures();   // co-op client: the host's animals arrive as net proxies
            // co-op: more than one diver → every creature reacts to (and is kept alive by) its nearest diver
            bool multi = hasPlayer && FaunaContext.DiverCount > 1;
            if (multi) Array.Clear(nearPer, 0, nearPer.Length);

            // schools first (leaders move, members follow)
            for (int i = schools.Count - 1; i >= 0; i--)
            {
                var s = schools[i];
                if (s.members.Count == 0) { ReleaseSchoolAt(i); continue; }
                if (multi) FaunaContext.SetFocus(FaunaContext.NearestDiver(s.leaderPos, out _));
                s.Tick(dt);
            }
            if (multi) FaunaContext.SetFocus(0);

            despawnTimer -= dt;
            bool despawnCheck = despawnTimer <= 0f;
            if (despawnCheck) despawnTimer = 0.5f;

            int near = 0;
            ticking = true;
            // full-rate ticks only where animals can be seen: under water the fog limit (8–19 m) + a margin
            float nearLodD = FXVisibility.Underwater ? Mathf.Clamp(FXVisibility.Meters + 15f, 25f, NearLodDistance) : NearLodDistance;
            float farLodD = Mathf.Max(nearLodD * 1.8f, nearLodD + 10f);
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
                        MeasureDivers(cp, out d3, out dxz, out dxzAll, out dLocal);
                    }
                    else
                    {
                        // single player (and hallucinations, which only ever exist for the local diver)
                        if (multi) FaunaContext.SetFocus(0);
                        Vector3 lp = multi ? FaunaContext.DiverList[0].head : pp;
                        float dx = cp.x - lp.x, dy = cp.y - lp.y, dz = cp.z - lp.z;
                        dxz = Mathf.Sqrt(dx * dx + dz * dz);
                        d3 = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
                        dxzAll = dxz;
                        dLocal = d3;
                    }
                }
                c.distToPlayer = d3;
                c.distXZ = dxz;
                c.distLocal = dLocal;

                if (!c.hallucination)
                {
                    if (hasPlayer && despawnCheck && dxzAll > DespawnDistance) { c.pendingDespawn = true; continue; }
                    if (!multi && dxz < CountRadius) near++;
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
            nearCount = multi ? nearPer[0] : near;

            if (hasPlayer)
            {
                Populate(dt, pp);
                if (multi && !remoteFauna) PopulateRemote();
            }
        }

        /// Co-op: distances from a creature to every diver. Focuses the nearest one (3D, head) for the behavior tick,
        /// counts the creature for every diver it is near and reports the smallest horizontal distance (despawn).
        void MeasureDivers(Vector3 cp, out float d3, out float dxz, out float dxzAll, out float dLocal)
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
                if (xz < CountRadius) nearPer[k]++;
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

        /// Co-op host: keeps the site population around every remote diver too (the local one is handled by Populate).
        void PopulateRemote()
        {
            var list = FaunaContext.DiverList;
            int n = FaunaContext.DiverCount;
            remotePrewarmTimer -= Time.deltaTime;
            if (remotePrewarmTimer <= 0f)
            {
                // pre-build the models of the sites the other divers are heading to (no hitch when they arrive)
                remotePrewarmTimer = 0.5f;
                for (int k = 1; k < n; k++) QueuePrewarm(list[k].head);
            }
            for (int k = 1; k < n; k++)
            {
                if (active.Count >= MaxActive) break;
                Vector3 hp = list[k].head;
                var site = FindSite(hp, out float w);
                if (site == null) continue;
                if (GameManager.I != null && !Progression.IsSiteUnlocked(site)) continue;
                int target = Mathf.RoundToInt(TargetFor(site.id) * w);
                int have = nearPer[k];
                if (have >= target) continue;

                FaunaContext.SetFocus(k);   // spawn out of THIS diver's view
                int groups = have < target / 2 ? 2 : 1;
                for (int g = 0; g < groups; g++)
                {
                    int got = SpawnGroup(site, hp);
                    if (got <= 0) break;
                    have += got;
                    if (have >= target || active.Count >= MaxActive) break;
                }
                nearPer[k] = have;
                FaunaContext.SetFocus(0);
            }
        }

        void QueuePrewarm(Vector3 p)
        {
            DiveSite near = null;
            float bestOut = float.MaxValue;
            var all = DiveSites.All;
            for (int i = 0; i < all.Count; i++)
            {
                var s = all[i];
                float outside = FaunaNav.DistXZ(p, s.center) - s.radius;
                if (outside < 300f && outside < bestOut) { bestOut = outside; near = s; }
            }
            if (near == null || !siteSpecies.TryGetValue(near.id, out var species)) return;
            for (int i = 0; i < species.Count; i++)
                if (!pool.HasTemplate(species[i]) && !prewarmQueue.Contains(species[i])) prewarmQueue.Add(species[i]);
        }

        // ================================================================== population

        static int TargetFor(string siteId)
        {
            switch (siteId)
            {
                case "house_reef": return 70;
                case "kelp_forest": return 50;
                case "wreck": return 45;
                case "blue_drop": return 20;
                case "deep_trench": return 14;
                default: return 25;
            }
        }

        void Populate(float dt, Vector3 pp)
        {
            siteTimer -= dt;
            if (siteTimer <= 0f)
            {
                siteTimer = 0.5f;
                var site = FindSite(pp, out siteWeight);
                if (site != currentSite) lockHintSite = null;
                currentSite = site;
                UpdatePrewarm(pp);
            }

            // build one missing template per frame for the site we're approaching (avoids a hitch on arrival)
            if (prewarmQueue.Count > 0)
            {
                var s = prewarmQueue[prewarmQueue.Count - 1];
                prewarmQueue.RemoveAt(prewarmQueue.Count - 1);
                if (!pool.HasTemplate(s)) { pool.Template(s); return; }
            }

            if (currentSite == null) return;

            // locked dive sites stay empty until their journal/follower requirements unlock them
            if (GameManager.I != null && !Progression.IsSiteUnlocked(currentSite))
            {
                // hint once per visit, only once actually inside the site (the falloff ring reaches the island shore)
                if (lockHintSite != currentSite && siteWeight >= 0.999f)
                {
                    lockHintSite = currentSite;
                    Progression.CheckUnlocks();   // requirements may be met but not processed yet
                    if (!Progression.IsSiteUnlocked(currentSite))
                    {
                        Progression.SiteRequirementsMet(currentSite, out var why);
                        string n = currentSite.Name;
                        GameEvents.RaiseToast(Loc.Tr($"Tauchplatz „{n}“ gesperrt", $"Dive site “{n}” locked")
                                              + (string.IsNullOrEmpty(why) ? "" : ": " + why));
                    }
                }
                if (!Progression.IsSiteUnlocked(currentSite)) return;
            }
            if (NetFlags.CreaturesRemote) return;   // co-op client: the host populates the reef

            int target = Mathf.RoundToInt(TargetFor(currentSite.id) * siteWeight);
            if (nearCount >= target || active.Count >= MaxActive) return;

            int groups = nearCount < target / 2 ? 2 : 1;
            for (int g = 0; g < groups; g++)
            {
                int n = SpawnGroup(currentSite, pp);
                if (n <= 0) break;
                nearCount += n;
                if (nearCount >= target || active.Count >= MaxActive) break;
            }
        }

        DiveSite FindSite(Vector3 p, out float weight)
        {
            DiveSite best = null;
            float bestScore = float.MaxValue;
            weight = 0f;
            var all = DiveSites.All;
            for (int i = 0; i < all.Count; i++)
            {
                var s = all[i];
                float d = FaunaNav.DistXZ(p, s.center);
                float outside = d - s.radius;           // <= 0 inside the site
                if (outside > SiteFalloff) continue;
                if (outside < bestScore)
                {
                    bestScore = outside;
                    best = s;
                }
            }
            if (best != null) weight = bestScore <= 0f ? 1f : Mathf.Clamp01(1f - bestScore / SiteFalloff);
            return best;
        }

        void UpdatePrewarm(Vector3 p)
        {
            DiveSite near = null;
            float bestOut = float.MaxValue;
            var all = DiveSites.All;
            for (int i = 0; i < all.Count; i++)
            {
                var s = all[i];
                float outside = FaunaNav.DistXZ(p, s.center) - s.radius;
                if (outside < 300f && outside < bestOut) { bestOut = outside; near = s; }
            }
            if (near == null || near == prewarmSite) return;
            prewarmSite = near;
            prewarmQueue.Clear();
            if (!siteSpecies.TryGetValue(near.id, out var list)) return;
            for (int i = 0; i < list.Count; i++)
                if (!pool.HasTemplate(list[i])) prewarmQueue.Add(list[i]);
        }

        void PrewarmStartSite()
        {
            if (!siteSpecies.TryGetValue("house_reef", out var list)) return;
            for (int i = 0; i < list.Count; i++)
            {
                if (pool.HasTemplate(list[i])) continue;
                pool.Template(list[i]);
                return;   // one per call
            }
        }

        /// Named individuals (Gerald, Sir Flapsalot, Lumen) exist only once at a time.
        static bool IsUnique(SpeciesDef s) => s.id == "green_turtle" || s.id == "sea_lion" || s.id == "anglerfish";

        static int MaxGroups(SpeciesDef s)
        {
            if (IsUnique(s)) return 1;
            int cap = 999;
            if (s.rarity == Rarity.Legendary) cap = 1;
            else if (s.rarity == Rarity.Epic) cap = 2;
            if (s.behavior == BehaviorType.Curious) cap = Mathf.Min(cap, 3);
            return cap;
        }

        bool Eligible(SpeciesDef s)
        {
            individuals.TryGetValue(s, out int n);
            return n < (IsUnique(s) ? 1 : MaxGroups(s) * Mathf.Max(1, s.groupMax));
        }

        SpeciesDef PickSpecies(DiveSite site)
        {
            if (!siteSpecies.TryGetValue(site.id, out var list) || list.Count == 0) return null;
            float total = 0f;
            for (int i = 0; i < list.Count; i++)
                if (Eligible(list[i])) total += list[i].SpawnWeight;
            if (total <= 0f) return null;
            float r = (float)rng.NextDouble() * total;
            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i];
                if (!Eligible(s)) continue;
                r -= s.SpawnWeight;
                if (r <= 0f) return s;
            }
            for (int i = list.Count - 1; i >= 0; i--) if (Eligible(list[i])) return list[i];
            return null;
        }

        int SpawnGroup(DiveSite site, Vector3 pp)
        {
            var s = PickSpecies(site);
            if (s == null) return 0;
            int count = rng.Next(s.groupMin, Mathf.Max(s.groupMin, s.groupMax) + 1);
            if (IsUnique(s)) count = 1;
            count = Mathf.Min(count, MaxActive - active.Count);
            if (count <= 0) return 0;

            float typicalLen = (s.minCm + s.maxCm) * 0.005f;
            float half = Mathf.Max(0.04f, s.bodyHeight) * typicalLen * 0.5f;
            bool bottomType = s.behavior == BehaviorType.BottomDweller || s.behavior == BehaviorType.Ambush
                              || s.plan == BodyPlan.Octopus || s.id == "clownfish";
            float clear = (bottomType ? 0.15f : 0.6f) + half;
            if (!FindSpawnPoint(site, s, pp, clear, half, bottomType, out Vector3 center)) return 0;

            Vector3 leashC = site.center;
            float leashR = site.radius + 30f;
            FaunaSchool school = null;
            if (s.behavior == BehaviorType.Schooling)
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
                        Vector2 o = Random.insideUnitCircle * Mathf.Clamp(typicalLen * 3f, 2f, 8f);
                        p = center + new Vector3(o.x, Random.Range(-1f, 1f) * (bottomType ? 0.2f : 1.5f), o.y);
                    }
                    if (!FaunaNav.ClampIntoWater(ref p, clear, half)) p = center;
                }
                else if (school != null && school.anchored) p = school.home;

                float cm = Progression.RollSizeCm(s, Progression.RollTier(rng), rng);
                var c = CreateCreature(s, p, cm, false, school, leashC, leashR);
                if (c == null) break;
                spawned++;
            }
            if (spawned == 0 && school != null) ReleaseSchool(school);
            return spawned;
        }

        bool FindSpawnPoint(DiveSite site, SpeciesDef s, Vector3 pp, float clear, float half, bool bottomType, out Vector3 result)
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                float dist = Mathf.Lerp(MinSpawnDistance, PopulationRadius, (float)rng.NextDouble());   // uniform in distance → denser near the diver
                var p = new Vector3(pp.x + Mathf.Cos(ang) * dist, 0f, pp.z + Mathf.Sin(ang) * dist);
                if (FaunaNav.DistXZ(p, site.center) > site.radius + 60f) continue;

                float g = WorldInfo.GroundHeight(p);
                if (g > FaunaNav.LandGuardY - 0.8f) continue;   // land, beach or ankle-deep water
                float top = FaunaNav.BandTop(s, half, FaunaNav.PlanningSurfaceY);
                float bot = FaunaNav.BandBottom(s, g, clear);
                if (bot > top) continue;

                float y;
                if (bottomType) y = bot + Mathf.Min(0.3f, top - bot) * (float)rng.NextDouble();
                else if (rng.NextDouble() < 0.7)
                    y = Mathf.Clamp(pp.y + ((float)rng.NextDouble() * 2f - 1f) * 20f, bot, top);   // near the diver's depth
                else y = Mathf.Lerp(bot, top, (float)rng.NextDouble());
                p.y = y;

                // spawn in the fog or out of view: never pop in right in front of the camera
                if (dist < InViewSpawnGuard && FaunaContext.InView(p)) continue;
                result = p;
                return true;
            }
            result = default;
            return false;
        }
    }
}
