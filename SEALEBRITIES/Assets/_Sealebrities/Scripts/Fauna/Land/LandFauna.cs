using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Island animals (L.11) and shore crabs (G.1): every animal has a deterministic home spot (FaunaAnchors: props
    /// publish perches, walls, pens, beaches …; a terrain fallback exists). An animal is spawned (through the
    /// FaunaSpawner – same ICreature, CreatureRegistry, co-op replication) when its home is within 60 m of any diver
    /// and its time of day fits, and goes again beyond 90 m or out of season. Cap 40 island animals.
    /// Host / offline only (clients see the host's animals as proxies).
    public sealed class LandFauna : MonoBehaviour
    {
        public const float SpawnRange = 60f;
        public const float DespawnDistance = 90f;
        public const int Cap = 52;   // round 3: more island life (birds, crabs, lizards – decoration, Q22/Q23)

        sealed class Slot
        {
            public SpeciesDef s;
            public FaunaLandSpot spot;
            public FaunaCreature c;
            public int netId;
            public float retry;
        }

        static LandFauna instance;
        readonly List<Slot> slots = new List<Slot>(96);
        bool built;
        float timer;
        int activeCount;

        public static LandFauna Instance => instance;
        public int ActiveCount => activeCount;

        void Awake() { instance = this; }
        void OnDestroy() { if (instance == this) instance = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { instance = null; }

        // ------------------------------------------------------------------ population plan (L.11 counts)

        struct Plan
        {
            public string id;
            public int count;
            public FaunaSpotKind a, b;
            public string tag;   // preferred tag (null = any)
            public Plan(string id, int count, FaunaSpotKind a, FaunaSpotKind b, string tag = null) { this.id = id; this.count = count; this.a = a; this.b = b; this.tag = tag; }
        }

        static readonly Plan[] Plans =
        {
            new Plan("seagull", 4, FaunaSpotKind.Perch, FaunaSpotKind.Perch),
            new Plan("seagull", 4, FaunaSpotKind.Beach, FaunaSpotKind.Beach),
            new Plan("frigatebird", 2, FaunaSpotKind.Soar, FaunaSpotKind.Soar),
            new Plan("tropicbird", 3, FaunaSpotKind.Rocks, FaunaSpotKind.Rocks, "cliffs|cliff_north"),
            new Plan("marine_iguana", 6, FaunaSpotKind.TidePool, FaunaSpotKind.Rocks),
            new Plan("gecko", 6, FaunaSpotKind.Wall, FaunaSpotKind.Wall),
            new Plan("coconut_crab", 3, FaunaSpotKind.Jungle, FaunaSpotKind.Jungle),
            new Plan("goat", 3, FaunaSpotKind.Pen, FaunaSpotKind.Pen),
            new Plan("goat", 2, FaunaSpotKind.Meadow, FaunaSpotKind.Meadow, "radio|radio_meadow"),
            new Plan("pig", 1, FaunaSpotKind.Ground, FaunaSpotKind.Ground, "camp"),   // round 3: Truffle lives with the hermits
            new Plan("capuchin", 1, FaunaSpotKind.Perch, FaunaSpotKind.Jungle, "lighthouse"),
            new Plan("fruit_bat", 10, FaunaSpotKind.Roost, FaunaSpotKind.Roost),
            new Plan("ghost_crab", 12, FaunaSpotKind.Beach, FaunaSpotKind.Beach),
            new Plan("sally_crab", 6, FaunaSpotKind.TidePool, FaunaSpotKind.Rocks),
            new Plan("hermit_crab", 4, FaunaSpotKind.TidePool, FaunaSpotKind.TidePool),
            // ---- round 3 (GDD §20 Q22: birds, crabs, lizards – decoration, no Sealog)
            new Plan("red_crab", 8, FaunaSpotKind.Jungle, FaunaSpotKind.Meadow),
            new Plan("skink", 6, FaunaSpotKind.Rocks, FaunaSpotKind.Meadow),
            new Plan("booby", 4, FaunaSpotKind.Rocks, FaunaSpotKind.Perch, "volcano|north_cliffs|cliffs|blue_drop"),
            new Plan("reef_heron", 2, FaunaSpotKind.TidePool, FaunaSpotKind.Rocks, "tide_pools|hidden_bay|tidepools"),
            // the volcano island: iguanas on the black lava, crabs on the black sand, red crabs in the ferns
            new Plan("marine_iguana", 5, FaunaSpotKind.Rocks, FaunaSpotKind.Beach, "volcano"),
            new Plan("sally_crab", 5, FaunaSpotKind.Beach, FaunaSpotKind.Rocks, "volcano"),
            new Plan("red_crab", 4, FaunaSpotKind.Meadow, FaunaSpotKind.Jungle, "volcano"),
        };

        readonly List<int> candidates = new List<int>(256);

        void Build()
        {
            built = true;
            slots.Clear();
            var spots = FaunaAnchors.LandSpots;
            for (int p = 0; p < Plans.Length; p++)
            {
                var plan = Plans[p];
                var s = SpeciesDatabase.Get(plan.id);
                if (s == null) continue;
                candidates.Clear();
                for (int i = 0; i < spots.Count; i++)
                    if ((spots[i].kind == plan.a || spots[i].kind == plan.b) && TagMatch(spots[i].tag, plan.tag)) candidates.Add(i);
                if (candidates.Count == 0 && plan.tag != null)
                    for (int i = 0; i < spots.Count; i++) if (spots[i].kind == plan.a || spots[i].kind == plan.b) candidates.Add(i);
                // fallbacks: walls → village / hut ground, the capuchin → rocks / jungle
                if (candidates.Count == 0 && plan.a == FaunaSpotKind.Wall)
                    for (int i = 0; i < spots.Count; i++) if (spots[i].kind == FaunaSpotKind.Ground) candidates.Add(i);
                if (candidates.Count == 0)
                    for (int i = 0; i < spots.Count; i++) if (spots[i].kind == FaunaSpotKind.Rocks || spots[i].kind == FaunaSpotKind.Jungle || spots[i].kind == FaunaSpotKind.Meadow) candidates.Add(i);
                if (candidates.Count == 0) continue;
                for (int k = 0; k < plan.count; k++)
                {
                    // deterministic, spread over the candidate spots
                    int idx = candidates[(int)(Hash(plan.id, k + p * 31) % (uint)candidates.Count)];
                    slots.Add(new Slot { s = s, spot = spots[idx] });
                    if (s.unique) break;
                }
            }
            Debug.Log("[Fauna] island animals: " + slots.Count + " home spots" + (FaunaAnchors.HasPropSpots ? " (props)" : " (fallback)"));
        }

        /// Plan tags may list alternatives ("cliffs|cliff_north"); null matches any tag.
        static bool TagMatch(string spotTag, string planTag)
        {
            if (planTag == null) return true;
            if (spotTag == null) return false;
            int start = 0;
            while (start <= planTag.Length)
            {
                int bar = planTag.IndexOf('|', start);
                int end = bar < 0 ? planTag.Length : bar;
                if (end - start == spotTag.Length && string.CompareOrdinal(planTag, start, spotTag, 0, spotTag.Length) == 0) return true;
                if (bar < 0) break;
                start = bar + 1;
            }
            return false;
        }

        /// New home spots arrived (props published theirs): start the plan over.
        public void Replan()
        {
            var spawner = FaunaModule.Spawner;
            for (int i = 0; i < slots.Count; i++)
                if (slots[i].c != null && spawner != null && slots[i].c.alive && slots[i].c.netId == slots[i].netId) spawner.Despawn(slots[i].c);
            slots.Clear();
            built = false;
        }

        static uint Hash(string id, int k)
        {
            unchecked
            {
                uint h = 2166136261u;
                for (int i = 0; i < id.Length; i++) h = (h ^ id[i]) * 16777619u;
                h ^= (uint)k * 0x9E3779B1u;
                h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
                return h;
            }
        }

        // ------------------------------------------------------------------ frame

        void Update()
        {
            var gm = GameManager.I;
            var spawner = FaunaModule.Spawner;
            if (gm == null || !gm.InGame || spawner == null) return;
            if (!FaunaAnchors.Built) return;
            if (!built) Build();
            if (NetFlags.CreaturesRemote || !Net.HasAuthority) { ForgetAll(); return; }
            if (UIState.PauseOpen && !Net.IsOnline) return;
            timer -= Time.deltaTime;
            if (timer > 0f) return;
            timer = 0.5f;
            if (!FaunaContext.HasPlayer) return;

            DayMask now = FaunaContext.DayNow;
            int n = FaunaContext.DiverCount;
            activeCount = 0;
            for (int i = 0; i < slots.Count; i++)
            {
                var sl = slots[i];
                if (sl.c != null && (!sl.c.alive || sl.c.netId != sl.netId || sl.c.pendingDespawn)) sl.c = null;
                if (sl.c != null) activeCount++;
            }
            for (int i = 0; i < slots.Count; i++)
            {
                var sl = slots[i];
                float d = NearestDiverXZ(sl.spot.pos, n);
                bool season = sl.s.FitsTime(now) && Extra(sl.s);
                if (sl.c != null)
                {
                    float dc = NearestDiverXZ(sl.c.pos, n);
                    bool leave = dc > DespawnDistance || (!season && dc > 25f && !FaunaContext.InView(sl.c.pos));
                    if (leave) { spawner.Despawn(sl.c); sl.c = null; activeCount--; }
                    continue;
                }
                if (!season || d > SpawnRange || activeCount >= Cap) continue;
                if (Time.time < sl.retry) continue;
                if (sl.s.unique && spawner.Individuals(sl.s) > 0) continue;
                // never pop into view right in front of somebody
                if (d < 22f && FaunaContext.InView(sl.spot.pos)) continue;
                Vector3 p = sl.spot.pos;
                if (FaunaModes.Of(sl.s) == FaunaMode.Fly && sl.spot.kind != FaunaSpotKind.Soar && sl.spot.kind != FaunaSpotKind.Roost) p += Vector3.up * 0.2f;
                var c = spawner.SpawnOne(sl.s, p, sl.spot.pos, 40f);
                if (c == null) { sl.retry = Time.time + 3f; continue; }
                c.SetLandSpot(sl.spot);
                sl.c = c;
                sl.netId = c.netId;
                activeCount++;
            }
        }

        /// Extra season rules beyond the day mask (L.11): fruit bats fly 18:30–21:00 (plus the night), the pig only by day.
        static bool Extra(SpeciesDef s)
        {
            if (s.id != "fruit_bat") return true;
            float tod = GameManager.I != null ? GameManager.I.State.timeOfDay : 0.5f;
            return tod > 18.5f / 24f || tod < 5f / 24f;
        }

        static float NearestDiverXZ(Vector3 p, int n)
        {
            float best = float.MaxValue;
            for (int k = 0; k < n; k++)
            {
                float d = FaunaNav.DistXZ(p, FaunaContext.DiverList[k].head);
                if (d < best) best = d;
            }
            return best;
        }

        void ForgetAll()
        {
            for (int i = 0; i < slots.Count; i++) slots[i].c = null;
            activeCount = 0;
        }

        // ------------------------------------------------------------------ helpers for behaviors / other modules

        /// A random home spot of one of two kinds within range of a point (behaviors: next perch, swimming water …).
        public static bool PickSpot(FaunaSpotKind a, FaunaSpotKind b, Vector3 near, float range, out FaunaLandSpot spot)
        {
            spot = default;
            var spots = FaunaAnchors.LandSpots;
            int seen = 0;
            float r2 = range * range;
            for (int i = 0; i < spots.Count; i++)
            {
                var s = spots[i];
                if (s.kind != a && s.kind != b) continue;
                if (FaunaNav.DistXZSqr(s.pos, near) > r2) continue;
                seen++;
                if (Random.Range(0, seen) == 0) spot = s;   // reservoir sampling
            }
            return seen > 0;
        }

        /// L.12 goat mishap: "Captain Bleat" walks to the parcel and sits on it until nudged (fishboat calls this on
        /// the host). Returns false when no goat could be found / spawned near the point.
        public static bool RequestGoatOn(Vector3 crateTop, float seconds)
        {
            var spawner = FaunaModule.Spawner;
            if (spawner == null || NetFlags.CreaturesRemote) return false;
            var goat = SpeciesDatabase.Get("goat");
            if (goat == null) return false;
            FaunaCreature best = null;
            float bd = 80f;
            var list = spawner.ActiveCreatures;
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                if (c == null || c.species != goat || !c.alive) continue;
                float d = FaunaNav.DistXZ(c.pos, crateTop);
                if (d < bd) { bd = d; best = c; }
            }
            if (best == null)
            {
                Vector3 p = crateTop + new Vector3(6f, 0f, 4f);
                p.y = WorldInfo.GroundHeight(p);
                best = spawner.SpawnOne(goat, p, crateTop, 20f);
                if (best == null) return false;
                best.landSpawned = true;
            }
            best.director = new GoatOnCrateDirector(crateTop, seconds);
            return true;
        }

        /// Ends the goat-on-crate scene (the player nudged him three times).
        public static void ReleaseGoat()
        {
            var spawner = FaunaModule.Spawner;
            if (spawner == null) return;
            var list = spawner.ActiveCreatures;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && list[i].director is GoatOnCrateDirector g) g.Release();
        }

        sealed class GoatOnCrateDirector : IFaunaDirector
        {
            readonly Vector3 top;
            float left;
            bool released;

            public GoatOnCrateDirector(Vector3 crateTop, float seconds) { top = crateTop; left = Mathf.Max(5f, seconds); }

            public void Release() { released = true; }

            public bool Steer(FaunaCreature c, float dt)
            {
                left -= dt;
                if (released || left <= 0f)
                {
                    c.director = null;
                    c.velocity = FaunaNav.FlatDir(c.pos - top) * 1.5f;   // hops off
                    return false;
                }
                Vector3 to = top - c.pos; to.y = 0f;
                float d = to.magnitude;
                if (d > 0.35f)
                {
                    c.desiredVel = to / d * Mathf.Max(0.6f, c.cruise * 1.5f);
                    if (d < 1.5f) { c.hasHeightOverride = true; c.heightOverride = top.y + c.halfHeight; }
                }
                else
                {
                    c.desiredVel = Vector3.zero;
                    c.hasHeightOverride = true;
                    c.heightOverride = top.y + c.halfHeight;
                    c.lureMul = 1f;   // head up, proud
                }
                c.activityTarget = 0.4f;
                return true;
            }
        }
    }
}
