using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// K.3 animal events – "something to watch every 2–3 minutes of diving". Host-authoritative: the host (or the
    /// offline game) schedules them per diver (first 60–120 s after entering a zone at ≥ 3 m, then every 150–300 s of
    /// dive time), runs one at a time (60–120 s), spawns the actors 25–40 m away out of view and steers them in with
    /// directors. FaunaEventInfo carries the running event (the net module mirrors it, message FaunaEvent 101); every
    /// machine shows the short notification when its player is close. Actors are ordinary creatures, so co-op clients
    /// see them through the normal creature replication.
    ///   feeding_frenzy  kelp / wreck / blue drop / open sea – a sardine bait ball, hunters dart through it
    ///   cleaning_station house reef (Gerald) / wreck (grouper) / blue drop (manta) / Fog Isle – cleaner wrasse at work
    ///   big_school      house reef (blue tang) / wreck (barracuda tornado) / open sea (sardines) / blue drop (hammerheads)
    ///   encounter       house reef (Gerald or a ray) / lagoon (eagle rays in formation) / blue drop (manta circles you)
    ///   predator_hunt   from day 3: kelp (leopard sharks) / wreck (barracuda) / blue drop (hammerhead) / house reef
    ///                   ("octopus vs crab", cute) – 2–3 lunges, 50 % "catch" = the prey vanishes in a puff of scales
    ///   night_glow      lagoon / house reef / Fog Isle, 21:00–04:00 – bioluminescent sparkles, jellies rise
    ///   whale_song      blue drop – Big Bertha passes singing (guaranteed once while she is not in the Sealog)
    ///   deep_parade     trench – lanternfish and vampire squid rise in a column
    public sealed class FaunaEvents : MonoBehaviour
    {
        public const float FirstMin = 60f, FirstMax = 120f, NextMin = 150f, NextMax = 300f;
        public const float DurationMin = 60f, DurationMax = 120f;
        public const float SpawnMin = 25f, SpawnMax = 40f;
        public const int HuntFromDay = 3;
        /// Notification range of the local player around the event centre.
        public const float NotifyRange = 70f;

        static FaunaEvents instance;
        public static FaunaEvents Instance => instance;

        readonly float[] diveTime = new float[FaunaContext.MaxDivers];
        readonly float[] nextAt = new float[FaunaContext.MaxDivers];
        readonly string[] lastZone = new string[FaunaContext.MaxDivers];
        readonly List<FaunaCreature> actors = new List<FaunaCreature>(64);
        readonly List<FaunaCreature> prey = new List<FaunaCreature>(64);
        readonly List<string> candidates = new List<string>(8);
        FaunaSchool eventSchool;
        string activeId;
        Vector3 center;
        float endsAt, sparkleTimer, catchTimer;
        int catches;
        bool whaleSongDone;
        System.Random rng = new System.Random(1234);

        void Awake() { instance = this; }

        void OnEnable()
        {
            GameEvents.FaunaEventStarted += OnStarted;
            GameEvents.GameStarted += OnGameStarted;
            GameEvents.ReturnedToMenu += OnGameStarted;
        }

        void OnDisable()
        {
            GameEvents.FaunaEventStarted -= OnStarted;
            GameEvents.GameStarted -= OnGameStarted;
            GameEvents.ReturnedToMenu -= OnGameStarted;
        }

        void OnDestroy() { if (instance == this) instance = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { instance = null; }

        void OnGameStarted()
        {
            StopEvent(false);
            for (int k = 0; k < diveTime.Length; k++) { diveTime[k] = 0f; nextAt[k] = 0f; lastZone[k] = null; }
        }

        /// Every machine: a short notification (L.10) when the event happens near the local player.
        void OnStarted(string id)
        {
            var p = Refs.Player;
            if (p == null || p.Root == null) return;
            if ((p.Root.position - FaunaEventInfo.Center).sqrMagnitude > NotifyRange * NotifyRange) return;
            GameEvents.RaiseNotify(NotifyKind.Event, Title(id), null);
        }

        public static string Title(string id)
        {
            switch (id)
            {
                case "feeding_frenzy": return Loc.Tr("Fressrausch!", "Feeding frenzy!");
                case "cleaning_station": return Loc.Tr("Putzerstation", "Cleaning station");
                case "big_school": return Loc.Tr("Riesenschwarm!", "Big school!");
                case "encounter": return Loc.Tr("Begegnung", "Encounter");
                case "predator_hunt": return Loc.Tr("Jagdszene!", "Hunt!");
                case "night_glow": return Loc.Tr("Leuchtnacht", "Glow night");
                case "whale_song": return Loc.Tr("Walgesang", "Whale song");
                case "deep_parade": return Loc.Tr("Tiefenparade", "Deep parade");
                default: return id;
            }
        }

        // ================================================================== frame

        void Update()
        {
            float t = Time.time;
            // visuals of a running event on every machine (sparkles of the Glow Night)
            if (FaunaEventInfo.IsActive && FaunaEventInfo.ActiveId == "night_glow") GlowSparkles(Time.deltaTime);

            var gm = GameManager.I;
            if (gm == null || !gm.InGame || !Net.HasAuthority || NetFlags.CreaturesRemote)
            {
                if (activeId != null && !Net.HasAuthority) { activeId = null; actors.Clear(); prey.Clear(); eventSchool = null; }
                return;
            }
            if (UIState.PauseOpen && !Net.IsOnline) return;
            var spawner = FaunaModule.Spawner;
            if (spawner == null || !FaunaContext.HasPlayer) return;
            float dt = Time.deltaTime;

            if (activeId != null)
            {
                TickEvent(dt);
                if (t >= endsAt || NearestDiver(center) > 140f) StopEvent(true);
                return;
            }

            int n = FaunaContext.DiverCount;
            for (int k = 0; k < n; k++)
            {
                ref FaunaDiver d = ref FaunaContext.DiverList[k];
                float depth = Waves.SeaLevel - d.head.y;
                if (!d.underwater || depth < 3f) continue;
                string zone = spawner.CurrentZone;
                if (k > 0) zone = FaunaZones.ZoneAt(d.head);
                if (zone != lastZone[k])
                {
                    // a new zone: the first event 60–120 s after arriving
                    lastZone[k] = zone;
                    diveTime[k] = 0f;
                    nextAt[k] = Rand(FirstMin, FirstMax);
                }
                diveTime[k] += dt;
                if (diveTime[k] < nextAt[k]) continue;
                if (TryStart(k, zone)) nextAt[k] = diveTime[k] + Rand(NextMin, NextMax);
                else nextAt[k] = diveTime[k] + 20f;
                break;
            }
        }

        float Rand(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        static float NearestDiver(Vector3 p)
        {
            float best = float.MaxValue;
            for (int k = 0; k < FaunaContext.DiverCount; k++) best = Mathf.Min(best, Vector3.Distance(p, FaunaContext.DiverList[k].head));
            return best;
        }

        // ================================================================== start

        static SpeciesDef S(string id) => SpeciesDatabase.Get(id);
        static bool Open(string id) { var s = S(id); return s != null && (GameManager.I == null || Progression.IsGateOpen(s)); }

        bool TryStart(int k, string zone)
        {
            var site = DiveSites.Get(zone);
            if (site != null && !Progression.IsSiteUnlocked(site)) return false;   // a locked site has no show
            var gm = GameManager.I;
            DayMask now = FaunaContext.DayNow;
            bool day = now == DayMask.Day;
            float tod = gm.State.timeOfDay;
            bool glowNight = tod >= 21f / 24f || tod < 4f / 24f;
            candidates.Clear();
            switch (zone)
            {
                case Zones.HouseReef:
                    if (day) { candidates.Add("cleaning_station"); candidates.Add("big_school"); candidates.Add("encounter"); }
                    if (gm.State.day >= HuntFromDay) candidates.Add("predator_hunt");
                    if (glowNight) { candidates.Add("night_glow"); candidates.Add("night_glow"); }
                    break;
                case Zones.Lagoon:
                    if (day && Open("spotted_eagle_ray")) candidates.Add("encounter");
                    if (glowNight) candidates.Add("night_glow");
                    break;
                case Zones.Kelp:
                    if (day) candidates.Add("feeding_frenzy");
                    if (gm.State.day >= HuntFromDay) candidates.Add("predator_hunt");
                    break;
                case Zones.Wreck:
                    if (day) { candidates.Add("feeding_frenzy"); candidates.Add("cleaning_station"); }
                    candidates.Add("big_school");
                    if (gm.State.day >= HuntFromDay) candidates.Add("predator_hunt");
                    break;
                case Zones.BlueDrop:
                    if (day) { candidates.Add("feeding_frenzy"); candidates.Add("cleaning_station"); candidates.Add("encounter"); }
                    if (rng.NextDouble() < 0.3) candidates.Add("big_school");   // hammerhead school: rare
                    if (gm.State.day >= HuntFromDay) candidates.Add("predator_hunt");
                    if (!whaleSongDone || rng.NextDouble() < 0.3) candidates.Add("whale_song");
                    if (Open("humpback") && !Journal.IsDiscovered("humpback") && !whaleSongDone) { candidates.Clear(); candidates.Add("whale_song"); }
                    break;
                case Zones.Trench:
                    candidates.Add("deep_parade");
                    break;
                case Zones.FogIsle:
                    if (day) candidates.Add("cleaning_station");
                    if (glowNight) candidates.Add("night_glow");
                    break;
                case Zones.OpenSea:
                    if (day) { candidates.Add("feeding_frenzy"); candidates.Add("big_school"); }
                    if (glowNight) candidates.Add("night_glow");   // round 3: open-water nights glow too
                    break;
                case Zones.Volcano:
                    // round 3: the volcano waters (only once they are open – before that they are plain open sea)
                    if (!VolcanoGate.Open) { if (day) candidates.Add("big_school"); break; }
                    if (day) { candidates.Add("feeding_frenzy"); candidates.Add("big_school"); }
                    if (glowNight) candidates.Add("night_glow");
                    break;
                case Zones.DeepSea:
                    if (day) candidates.Add("big_school");
                    break;
            }
            while (candidates.Count > 0)
            {
                int i = rng.Next(candidates.Count);
                string id = candidates[i];
                candidates.RemoveAt(i);
                if (Begin(id, zone, k)) return true;
            }
            return false;
        }

        bool Begin(string id, string zone, int k)
        {
            var spawner = FaunaModule.Spawner;
            ref FaunaDiver d = ref FaunaContext.DiverList[k];
            Vector3 fwd = FaunaNav.FlatDir(d.forward);
            if (fwd == Vector3.zero) fwd = Vector3.forward;
            // the show happens in front of the diver, a little below the eyes
            center = d.head + fwd * 11f + Vector3.down * 0.5f;
            if (!FaunaNav.ClampIntoWater(ref center, 2.5f, 0.5f)) center = d.head + fwd * 4f;
            actors.Clear();
            prey.Clear();
            eventSchool = null;
            catches = 0;
            catchTimer = 2f;
            bool ok = false;
            switch (id)
            {
                case "feeding_frenzy": ok = FeedingFrenzy(zone, d); break;
                case "cleaning_station": ok = CleaningStation(zone, d); break;
                case "big_school": ok = BigSchool(zone, d); break;
                case "encounter": ok = Encounter(zone, d); break;
                case "predator_hunt": ok = Hunt(zone, d); break;
                case "night_glow": ok = NightGlow(d); break;
                case "whale_song": ok = WhaleSong(d); break;
                case "deep_parade": ok = DeepParade(d); break;
            }
            if (!ok)
            {
                for (int i = 0; i < actors.Count; i++) if (actors[i] != null) actors[i].director = null;
                if (eventSchool != null) { eventSchool.director = null; eventSchool.spreadMul = 1f; }
                actors.Clear(); prey.Clear(); eventSchool = null;
                return false;
            }
            activeId = id;
            float dur = Rand(DurationMin, DurationMax);
            if (id == "cleaning_station") dur = 90f;
            if (id == "encounter") dur = 45f;
            endsAt = Time.time + dur;
            FaunaEventInfo.Start(id, zone, center, dur);
            Debug.Log("[Fauna] event " + id + " in " + zone);
            return true;
        }

        /// Spawn point 25–40 m from the centre, away from the diver's view.
        Vector3 Offstage(in FaunaDiver d, float depthY)
        {
            Vector3 fwd = FaunaNav.FlatDir(d.forward);
            for (int i = 0; i < 6; i++)
            {
                float ang = Rand(100f, 260f) * Mathf.Deg2Rad;   // behind / to the sides of the view
                Vector3 dir = Quaternion.Euler(0f, ang * Mathf.Rad2Deg, 0f) * fwd;
                Vector3 p = center + dir * Rand(SpawnMin, SpawnMax);
                p.y = depthY;
                if (FaunaNav.ClampIntoWater(ref p, 1.5f, 0.5f)) return p;
            }
            Vector3 q = center - fwd * SpawnMin;
            q.y = depthY;
            FaunaNav.ClampIntoWater(ref q, 1.5f, 0.5f);
            return q;
        }

        int Spawn(string id, Vector3 at, int count, IFaunaDirector dir, List<FaunaCreature> into = null)
        {
            var s = S(id);
            if (s == null || !Open(id)) return 0;
            var spawner = FaunaModule.Spawner;
            int before = actors.Count;
            int got = spawner.SpawnAt(s, at, count, center, 60f, false, actors);
            for (int i = before; i < actors.Count; i++)
            {
                if (dir != null) actors[i].director = dir;
                into?.Add(actors[i]);
            }
            return got;
        }

        int SpawnSchool(string id, Vector3 at, int count, IFaunaSchoolDirector dir, float spreadMul)
        {
            var s = S(id);
            if (s == null || !Open(id)) return 0;
            var spawner = FaunaModule.Spawner;
            var sc = spawner.TakeSchool();
            float len = (s.minCm + s.maxCm) * 0.005f;
            sc.Init(s, at, center, 80f, len, count);
            sc.director = dir;
            sc.spreadMul = spreadMul;
            int before = actors.Count;
            int got = spawner.SpawnAt(s, at, count, center, 80f, false, actors, sc);
            if (got == 0) { spawner.ReleaseSchool(sc); return 0; }
            for (int i = before; i < actors.Count; i++) prey.Add(actors[i]);
            eventSchool = sc;
            return got;
        }

        FaunaCreature Existing(string id, float range)
        {
            var spawner = FaunaModule.Spawner;
            var s = S(id);
            var list = spawner.ActiveCreatures;
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                if (c != null && c.species == s && c.alive && !c.hallucination && (c.pos - center).sqrMagnitude < range * range) return c;
            }
            return null;
        }

        // ------------------------------------------------------------------ event recipes

        bool FeedingFrenzy(string zone, in FaunaDiver d)
        {
            if (!Open("sardine")) return false;
            Vector3 ballAt = Offstage(d, center.y);
            if (SpawnSchool("sardine", ballAt, 40, new BaitBall(this), 0.45f) < 10) return false;
            int hunters = 0;
            switch (zone)
            {
                case Zones.Kelp:
                    hunters += Spawn("sea_lion", Offstage(d, center.y), 1, new Hunter(this, 1.1f));
                    hunters += Spawn("leopard_shark", Offstage(d, center.y - 2f), 2, new Hunter(this, 0.8f));
                    break;
                case Zones.Wreck:
                    hunters += Spawn("barracuda", Offstage(d, center.y), 2 + rng.Next(3), new Hunter(this, 1f));
                    break;
                case Zones.BlueDrop:
                    hunters += Spawn("yellowfin", Offstage(d, center.y), 3, new Hunter(this, 1.2f));
                    hunters += Spawn("dolphin", Offstage(d, center.y), 2, new Hunter(this, 1.3f));
                    break;
                default:
                    hunters += Spawn(Open("yellowfin") ? "yellowfin" : (Open("dolphin") ? "dolphin" : "barracuda"), Offstage(d, center.y), 3, new Hunter(this, 1.1f));
                    break;
            }
            return true;   // a bait ball alone is still a sight
        }

        bool CleaningStation(string zone, in FaunaDiver d)
        {
            // the station: a cleaning anchor near the diver (or right in front)
            if (FaunaAnchors.FindHabitat(center, 25f, FaunaHabitat.Cleaning, 3, out Vector3 st))
            {
                st.y = WorldInfo.GroundHeight(st) + 1.4f;
                if (FaunaNav.ClampIntoWater(ref st, 1.2f, 0.6f)) center = st;
            }
            string big;
            switch (zone)
            {
                case Zones.HouseReef: big = "green_turtle"; break;
                case Zones.Wreck: big = "grouper"; break;
                case Zones.BlueDrop: big = "manta"; break;
                default: big = Open("coelacanth") && rng.NextDouble() < 0.2 ? "emperor_angelfish" : "parrotfish"; break;
            }
            var hold = new Hold(this);
            var existing = Existing(big, 50f);
            if (existing != null) { existing.director = hold; actors.Add(existing); }
            else if (Spawn(big, Offstage(d, center.y), 1, hold) == 0) return false;
            var guest = actors[actors.Count - 1];
            return SpawnSchool("cleaner_wrasse", Offstage(d, center.y), 6 + rng.Next(5), new Orbit(guest, 0.6f), 0.35f) > 0;
        }

        bool BigSchool(string zone, in FaunaDiver d)
        {
            switch (zone)
            {
                case Zones.HouseReef: return SpawnSchool("blue_tang", Offstage(d, center.y), 30, new Pass(this, d.head, false), 0.8f) > 0;
                case Zones.Wreck: return SpawnSchool("barracuda", Offstage(d, center.y), 24, new Pass(this, d.head, true), 0.7f) > 0;
                case Zones.BlueDrop: return Spawn("hammerhead", Offstage(d, center.y - 4f), 6, new EncounterDir(this, 10f, 0.6f)) > 0;
                default: return SpawnSchool("sardine", Offstage(d, center.y), 40, new Pass(this, d.head, rng.NextDouble() < 0.5), 0.9f) > 0;
            }
        }

        bool Encounter(string zone, in FaunaDiver d)
        {
            switch (zone)
            {
                case Zones.HouseReef:
                    {
                        var gerald = Existing("green_turtle", 80f);
                        var enc = new EncounterDir(this, 3.5f, 0.8f);
                        if (gerald != null) { gerald.director = enc; actors.Add(gerald); return true; }
                        if (Open("green_turtle") && FaunaModule.Spawner.Individuals(S("green_turtle")) == 0 && rng.NextDouble() < 0.6)
                            return Spawn("green_turtle", Offstage(d, center.y), 1, enc) > 0;
                        return Spawn("bluespotted_ray", Offstage(d, center.y - 1f), 1, enc) > 0;
                    }
                case Zones.Lagoon:
                    return Spawn("spotted_eagle_ray", Offstage(d, center.y), 4 + rng.Next(3), new EncounterDir(this, 6f, 0.7f)) > 0;
                case Zones.BlueDrop:
                    return Spawn("manta", Offstage(d, center.y), 1, new EncounterDir(this, 4.5f, 0.6f)) > 0;
            }
            return false;
        }

        bool Hunt(string zone, in FaunaDiver d)
        {
            string hunter, preyId;
            switch (zone)
            {
                case Zones.Kelp: hunter = "leopard_shark"; preyId = "sardine"; break;
                case Zones.Wreck: hunter = "barracuda"; preyId = "sardine"; break;
                case Zones.BlueDrop: hunter = "hammerhead"; preyId = "yellowfin"; break;
                default: hunter = "octopus"; preyId = "hermit_crab"; break;   // house reef: octopus vs crab (cute)
            }
            if (!Open(hunter) || !Open(preyId)) return false;
            int preyCount;
            if (preyId == "hermit_crab")
            {
                Vector3 at = center;
                at.y = WorldInfo.GroundHeight(at);
                preyCount = FaunaModule.Spawner.SpawnAt(S(preyId), at, 1, center, 20f, true, prey);
                for (int i = 0; i < prey.Count; i++) actors.Add(prey[i]);
            }
            else preyCount = SpawnSchool(preyId, Offstage(d, center.y), preyId == "sardine" ? 24 : 6, new BaitBall(this), 0.6f);
            if (preyCount == 0) return false;
            return Spawn(hunter, Offstage(d, center.y), hunter == "octopus" ? 1 : (hunter == "hammerhead" ? 1 : 2), new Hunter(this, hunter == "octopus" ? 0.6f : 1.2f)) > 0;
        }

        bool NightGlow(in FaunaDiver d)
        {
            if (!Open("moon_jelly")) return true;
            Vector3 deep = center;
            deep.y = Mathf.Max(WorldInfo.GroundHeight(center) + 1.5f, center.y - 4f);
            Spawn("moon_jelly", deep, 6 + rng.Next(5), new Rise(this, 0.12f));
            return true;
        }

        bool WhaleSong(in FaunaDiver d)
        {
            if (!Open("humpback")) return false;
            var song = new EncounterDir(this, 20f, 0.35f);
            var bertha = Existing("humpback", 300f);
            if (bertha != null) { bertha.director = song; actors.Add(bertha); }
            else if (Spawn("humpback", Offstage(d, center.y - 4f), 1, song) == 0) return false;
            whaleSongDone = true;
            return true;
        }

        bool DeepParade(in FaunaDiver d)
        {
            Vector3 below = center + Vector3.down * 12f;
            FaunaNav.ClampIntoWater(ref below, 2f, 0.5f);
            int got = 0;
            if (Open("lanternfish")) got += SpawnSchool("lanternfish", below, 24, new RiseSchool(this), 0.9f);
            got += Spawn("vampire_squid", below + Vector3.right * 3f, 2, new Rise(this, 0.25f));
            return got > 0;
        }

        // ------------------------------------------------------------------ running / stopping

        void TickEvent(float dt)
        {
            for (int i = actors.Count - 1; i >= 0; i--)
                if (actors[i] == null || !actors[i].alive || actors[i].pendingDespawn) actors.RemoveAt(i);
            for (int i = prey.Count - 1; i >= 0; i--)
                if (prey[i] == null || !prey[i].alive || prey[i].pendingDespawn) prey.RemoveAt(i);
            if (actors.Count == 0 && activeId != "night_glow") { StopEvent(true); return; }
            if (eventSchool != null && eventSchool.members.Count > 0) center = Vector3.Lerp(center, eventSchool.avgPos, dt * 0.5f);
        }

        /// A hunter got close to the prey: 50 % (max 3) one prey vanishes in a puff of scales – no gore.
        internal void TryCatch(Vector3 at)
        {
            catchTimer -= 1f;
            if (catches >= 3 || prey.Count <= 1 || catchTimer > 0f) return;
            catchTimer = 2f;
            if (rng.NextDouble() >= 0.5) return;
            int best = -1;
            float bd = 4f;
            for (int i = 0; i < prey.Count; i++)
            {
                float dd = (prey[i].pos - at).sqrMagnitude;
                if (dd < bd) { bd = dd; best = i; }
            }
            if (best < 0) return;
            var p = prey[best];
            FaunaFx.Scales(p.pos, Mathf.Max(0.3f, p.lengthM * 2f));
            prey.RemoveAt(best);
            catches++;
            p.RequestDespawn();
        }

        void StopEvent(bool raiseEnd)
        {
            for (int i = 0; i < actors.Count; i++) if (actors[i] != null && actors[i].director != null) actors[i].director = null;
            if (eventSchool != null) { eventSchool.director = null; eventSchool.spreadMul = 1f; }
            actors.Clear();
            prey.Clear();
            eventSchool = null;
            if (activeId != null && raiseEnd && Net.HasAuthority) FaunaEventInfo.End();
            activeId = null;
        }

        void GlowSparkles(float dt)
        {
            sparkleTimer -= dt;
            if (sparkleTimer > 0f) return;
            sparkleTimer = 0.12f;
            var p = Refs.Player;
            if (p == null || p.Root == null || !p.IsUnderwater) return;
            if ((p.Root.position - FaunaEventInfo.Center).sqrMagnitude > 90f * 90f) return;
            // around the moving diver (fins) and around animals nearby
            float sp = p.Velocity.magnitude;
            if (sp > 0.2f) FaunaFx.Sparkle(p.Root.position + Vector3.up * 0.5f - p.Velocity.normalized * 0.6f, 0.6f, new Color(0.27f, 0.88f, 1f, 1f), Mathf.Clamp((int)(sp * 6f), 2, 10));
            var list = CreatureRegistry.Active;
            int n = 0;
            for (int i = 0; i < list.Count && n < 6; i++)
            {
                var c = list[i];
                if (c == null || !c.IsAlive) continue;
                if ((c.Position - p.Root.position).sqrMagnitude > 15f * 15f) continue;
                n++;
                FaunaFx.Sparkle(c.Position, 0.3f, i % 3 == 0 ? new Color(0.6f, 1f, 0.43f, 1f) : new Color(0.27f, 0.88f, 1f, 1f), 2);
            }
        }

        // ================================================================== directors

        /// Bait ball: the school's leader circles the event centre slowly, the members pack tight (spreadMul).
        sealed class BaitBall : IFaunaSchoolDirector
        {
            readonly FaunaEvents e;
            float a;
            public BaitBall(FaunaEvents ev) { e = ev; a = Random.value * 6f; }
            public bool SteerLeader(FaunaSchool s, float dt)
            {
                a += dt * 0.5f;
                Vector3 tgt = e.center + new Vector3(Mathf.Sin(a), Mathf.Sin(a * 0.7f) * 0.4f, Mathf.Cos(a)) * 1.6f;
                float d = (tgt - s.leaderPos).magnitude;
                s.LeaderTo(tgt, d > 5f ? s.cruise * 2.5f : s.cruise * 0.9f, dt);
                return true;
            }
        }

        /// Big school passing: from where it spawned across in front of the diver (or a barracuda tornado around them).
        sealed class Pass : IFaunaSchoolDirector
        {
            readonly FaunaEvents e;
            readonly bool tornado;
            Vector3 end;
            bool hasEnd;
            float a;
            public Pass(FaunaEvents ev, Vector3 diver, bool tornado) { e = ev; this.tornado = tornado; }
            public bool SteerLeader(FaunaSchool s, float dt)
            {
                if (tornado)
                {
                    a += dt * 0.35f;
                    Vector3 tgt = e.center + new Vector3(Mathf.Sin(a) * 5f, Mathf.Sin(a * 0.5f) * 1.5f, Mathf.Cos(a) * 5f);
                    s.LeaderTo(tgt, s.cruise * 1.4f, dt);
                    return true;
                }
                if (!hasEnd)
                {
                    hasEnd = true;
                    Vector3 through = e.center - s.leaderPos;
                    through.y = 0f;
                    end = e.center + through.normalized * 35f;
                    end.y = e.center.y;
                }
                s.LeaderTo((s.leaderPos - e.center).sqrMagnitude < 9f || Vector3.Dot(end - s.leaderPos, end - e.center) < 0f ? end : e.center, s.cruise * 1.2f, dt);
                return true;
            }
        }

        /// Cleaner wrasse swarm tight around their guest.
        sealed class Orbit : IFaunaSchoolDirector
        {
            readonly FaunaCreature guest;
            readonly float r;
            float a;
            public Orbit(FaunaCreature guest, float radius) { this.guest = guest; r = radius; }
            public bool SteerLeader(FaunaSchool s, float dt)
            {
                if (guest == null || !guest.alive) return false;
                a += dt * 1.3f;
                float rad = r + guest.lengthM * 0.4f;
                Vector3 tgt = guest.pos + new Vector3(Mathf.Sin(a) * rad, Mathf.Sin(a * 1.7f) * rad * 0.3f, Mathf.Cos(a) * rad);
                s.LeaderTo(tgt, s.cruise * 3f + 0.4f, dt);
                return true;
            }
        }

        /// The cleaning guest holds still above the station (slowly turning), after arriving.
        sealed class Hold : IFaunaDirector
        {
            readonly FaunaEvents e;
            float yaw;
            public Hold(FaunaEvents ev) { e = ev; yaw = Random.value * 360f; }
            public bool Steer(FaunaCreature c, float dt)
            {
                Vector3 tgt = e.center;
                float d = (tgt - c.pos).magnitude;
                c.relaxBand = true;
                if (d > 1.5f)
                {
                    c.desiredVel = c.Seek(tgt, Mathf.Max(c.cruise, 0.5f), 3f);
                    c.activityTarget = 0.4f;
                    return true;
                }
                yaw += dt * 6f;
                c.desiredVel = (tgt - c.pos) * 0.5f;
                c.hasFaceDir = true;
                c.faceDir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                c.turnMul = 0.2f;
                c.activityTarget = 0.5f;
                return true;
            }
        }

        /// A hunter darts through the prey (centre / nearest prey), turns away, comes back – and sometimes catches one.
        sealed class Hunter : IFaunaDirector
        {
            readonly FaunaEvents e;
            readonly float speedMul;
            float passT = -1f, cool;
            Vector3 exitDir;
            public Hunter(FaunaEvents ev, float speedMul) { e = ev; this.speedMul = speedMul; cool = Random.Range(0f, 3f); }
            public bool Steer(FaunaCreature c, float dt)
            {
                c.relaxBand = true;
                Vector3 target = e.center;
                if (passT >= 0f)
                {
                    passT += dt;
                    c.desiredVel = exitDir * Mathf.Max(2.5f, c.cruise * 3f) * speedMul;
                    c.accelMul = 3f;
                    c.activityTarget = 1f;
                    if (passT > 1.6f) { passT = -1f; cool = Random.Range(1.5f, 4f); }
                    return true;
                }
                cool -= dt;
                Vector3 to = target - c.pos;
                float d = to.magnitude;
                if (cool > 0f && d < 9f)
                {
                    // circle at the edge before the next run
                    Vector3 tangent = Vector3.Cross(Vector3.up, to.normalized);
                    c.desiredVel = tangent * Mathf.Max(1f, c.cruise * 1.2f) + to * 0.05f;
                    c.activityTarget = 0.6f;
                    return true;
                }
                c.desiredVel = d > 1e-3f ? to / d * Mathf.Max(3f, c.cruise * 3.2f) * speedMul : c.Transform.forward;
                c.accelMul = 3f;
                c.turnMul = 2f;
                c.activityTarget = 1f;
                if (d < 1.5f + c.lengthM * 0.5f)
                {
                    e.TryCatch(c.HeadPos);
                    exitDir = d > 1e-3f ? to / d : c.Transform.forward;
                    passT = 0f;
                }
                return true;
            }
        }

        /// A calm close meeting: approach the diver and circle at a distance (Gerald, rays, manta, Bertha singing).
        sealed class EncounterDir : IFaunaDirector
        {
            readonly FaunaEvents e;
            readonly float radius, speedMul;
            float a, dir;
            bool init;
            public EncounterDir(FaunaEvents ev, float radius, float speedMul) { e = ev; this.radius = radius; this.speedMul = speedMul; dir = Random.value < 0.5f ? -1f : 1f; }
            public bool Steer(FaunaCreature c, float dt)
            {
                if (!FaunaContext.HasPlayer) return false;
                Vector3 center = FaunaContext.PlayerPos;
                Vector3 off = c.pos - center;
                if (!init) { init = true; a = Mathf.Atan2(off.x, off.z); }
                float r = radius + c.lengthM * 0.6f;
                a += dt * dir * Mathf.Max(0.3f, c.cruise * speedMul) / r;
                Vector3 tgt = center + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * r;
                tgt.y = Mathf.Lerp(center.y - 0.5f, c.pos.y, 0.5f);
                c.relaxBand = true;
                c.desiredVel = c.Seek(tgt, Mathf.Max(0.5f, c.cruise * 1.4f * speedMul), 3f);
                if (off.magnitude < r + 3f) { c.hasFaceDir = true; c.faceDir = Vector3.Lerp(c.velocity.normalized, -off.normalized, 0.3f); }
                c.activityTarget = 0.7f;
                return true;
            }
        }

        /// Rise slowly in a column above the event centre (jellies at the Glow Night, vampire squid at the Deep Parade).
        sealed class Rise : IFaunaDirector
        {
            readonly FaunaEvents e;
            readonly float speed;
            public Rise(FaunaEvents ev, float speed) { e = ev; this.speed = speed; }
            public bool Steer(FaunaCreature c, float dt)
            {
                c.relaxBand = true;
                Vector3 to = e.center - c.pos;
                to.y = 0f;
                c.desiredVel = to * 0.1f + Vector3.up * speed;
                if (c.pos.y > e.center.y + 6f) c.desiredVel.y = 0f;
                c.activityTarget = 0.5f;
                if (c.species.plan == BodyPlan.Jelly) return false;   // jellies pulse upward on their own; keep their pulse
                return true;
            }
        }

        sealed class RiseSchool : IFaunaSchoolDirector
        {
            readonly FaunaEvents e;
            float t;
            public RiseSchool(FaunaEvents ev) { e = ev; }
            public bool SteerLeader(FaunaSchool s, float dt)
            {
                t += dt;
                float y = e.center.y - 10f + Mathf.Min(14f, t * 0.35f);
                Vector3 tgt = new Vector3(e.center.x + Mathf.Sin(t * 0.3f) * 2f, y, e.center.z + Mathf.Cos(t * 0.3f) * 2f);
                s.LeaderTo(tgt, s.cruise * 1.2f + 0.3f, dt);
                return true;
            }
        }
    }
}
