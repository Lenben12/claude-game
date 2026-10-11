using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Sealebrities
{
    /// One named post-finale legend (Design_Runde2 I "post-finale").
    public sealed class LegendDef
    {
        public string id, nameDe, nameEn, speciesId, siteId;
        public float sizeCm;
        public string Name => Loc.Tr(nameDe, nameEn);
        public SpeciesDef Species => SpeciesDatabase.Get(speciesId);
        string flag;
        /// Shared story flag set when the legend was photographed ("legend:<id>").
        public string Flag => flag ?? (flag = FilmLegends.LegendFlagPrefix + id);
    }

    /// Legend & record hunt after the finale (I "post-finale": Sealog tabs „Legenden“ / „Rekordjagd“, drawn by the UI).
    ///  • LEGENDS: one named giant per dive site (Tante Tentakel, Opa Barsch, Hammer-Harry, Oma Bertha, Kalmar-König).
    ///    FilmLegendSpawner (host) puts it into its site while a diver is there; a Sealog photo of it (subject of that
    ///    species and ≥ 97 % of the legend size) catches it for the channel (StoryFlags "legend:<id>").
    ///  • RECORD HUNT: gold target per species = maxCm × 1.05; a Sealog photo of a bigger animal earns the species' gold
    ///    badge (StoryFlags "gold:<speciesId>").
    /// Both live in the shared story flags (host authority, clients forward) – no extra save fields needed.
    public static class FilmLegends
    {
        public const string LegendFlagPrefix = "legend:";
        public const string GoldFlagPrefix = "gold:";
        /// Record hunt gold target = maxCm × GoldFactor.
        public const float GoldFactor = 1.05f;
        /// A photographed animal is the legend when it has at least this share of the legend's size.
        public const float LegendMatch = 0.97f;

        public static readonly LegendDef[] All =
        {
            new LegendDef { id = "aunt_tentacle", nameDe = "Tante Tentakel", nameEn = "Auntie Tentacle", speciesId = "octopus", siteId = "house_reef", sizeCm = 220f },
            new LegendDef { id = "grandpa_bass", nameDe = "Opa Barsch", nameEn = "Grandpa Bass", speciesId = "giant_sea_bass", siteId = "kelp_forest", sizeCm = 280f },
            new LegendDef { id = "hammer_harry", nameDe = "Hammer-Harry", nameEn = "Hammer Harry", speciesId = "hammerhead", siteId = "wreck", sizeCm = 520f },
            new LegendDef { id = "granny_bertha", nameDe = "Oma Bertha", nameEn = "Granny Bertha", speciesId = "humpback", siteId = "blue_drop", sizeCm = 1700f },
            new LegendDef { id = "squid_king", nameDe = "Kalmar-König", nameEn = "Squid King", speciesId = "giant_squid", siteId = "deep_trench", sizeCm = 1400f },
        };

        /// The fauna module may set this to false when it spawns the legends itself.
        public static bool SpawnEnabled = true;

        public static event Action<LegendDef> LegendPhotographed;
        public static event Action<SpeciesDef, float> GoldBadgeEarned;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { SpawnEnabled = true; LegendPhotographed = null; GoldBadgeEarned = null; }

        /// The hunt runs after the finale (StoryKeys.FinaleDone) – or once 1,000,000 followers were reached.
        public static bool IsHuntActive
        {
            get
            {
                var gm = GameManager.I;
                if (gm == null || !gm.InGame || gm.State == null) return false;
                return gm.State.finaleReached || StoryFlags.Has(StoryKeys.FinaleDone);
            }
        }

        public static LegendDef Get(string id)
        {
            for (int i = 0; i < All.Length; i++) if (All[i].id == id) return All[i];
            return null;
        }

        public static LegendDef ForSite(string siteId)
        {
            for (int i = 0; i < All.Length; i++) if (All[i].siteId == siteId) return All[i];
            return null;
        }

        public static bool IsCaught(LegendDef l) => l != null && StoryFlags.Has(l.Flag);

        public static int CaughtCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < All.Length; i++) if (IsCaught(All[i])) n++;
                return n;
            }
        }

        /// Gold target of the record hunt (cm).
        public static float GoldTarget(SpeciesDef sp) => sp != null ? sp.maxCm * GoldFactor : float.MaxValue;

        public static bool HasGold(string speciesId) => !string.IsNullOrEmpty(speciesId) && StoryFlags.Has(GoldFlagPrefix + speciesId);

        public static int GoldCount
        {
            get
            {
                var all = SpeciesDatabase.All;
                int n = 0;
                for (int i = 0; i < all.Count; i++) if (HasGold(all[i].id)) n++;
                return n;
            }
        }

        /// The legend this animal is (null = none).
        public static LegendDef Match(SpeciesDef sp, float sizeCm)
        {
            if (sp == null) return null;
            for (int i = 0; i < All.Length; i++)
                if (All[i].speciesId == sp.id && sizeCm >= All[i].sizeCm * LegendMatch) return All[i];
            return null;
        }

        /// Called by the hand camera for every photo with a Sealog subject (local player).
        internal static void OnPhoto(FilmPhotoReport r)
        {
            if (r == null || r.subject == null || !IsHuntActive) return;
            var sp = r.subject;
            var legend = Match(sp, r.subjectCm);
            if (legend != null && !IsCaught(legend))
            {
                StoryFlags.Set(legend.Flag);
                r.legendId = legend.id;
                GameEvents.RaiseNotify(NotifyKind.Achievement, Loc.Tr("Legende: ", "Legend: ") + legend.Name, Loc.Length(r.subjectCm));
                Achievements.Unlock("legendary");
                LegendPhotographed?.Invoke(legend);
            }
            if (r.subjectCm >= GoldTarget(sp) && !HasGold(sp.id))
            {
                StoryFlags.Set(GoldFlagPrefix + sp.id);
                r.goldRecord = true;
                if (legend == null)
                    GameEvents.RaiseNotify(NotifyKind.Sealog, Loc.Tr("Goldrekord: ", "Gold record: ") + sp.Name, Loc.Length(r.subjectCm));
                GoldBadgeEarned?.Invoke(sp, r.subjectCm);
            }
        }
    }

    /// Host side of the legend hunt: while a diver is at a dive site after the finale, the site's legend swims there
    /// (spawned through Refs.Spawner, so co-op clients see it as a normal creature proxy). It leaves again when nobody
    /// is near the site; once photographed it is not spawned any more. Checks every 2 s, allocation free.
    public sealed class FilmLegendSpawner : MonoBehaviour
    {
        const float CheckInterval = 2f;
        const float SpawnMargin = 30f, DespawnMargin = 160f, RespawnCooldown = 90f;

        readonly ICreature[] spawned = new ICreature[FilmLegends.All.Length];
        readonly float[] cooldown = new float[FilmLegends.All.Length];
        float timer;

        void OnDisable() => DespawnAll();

        void Update()
        {
            timer -= Time.deltaTime;
            if (timer > 0f) return;
            timer = CheckInterval;

            var spawner = Refs.Spawner;
            bool active = FilmLegends.SpawnEnabled && Net.HasAuthority && FilmLegends.IsHuntActive && spawner != null
                          && !(spawner is Object so && so == null);
            if (!active) { DespawnAll(); return; }

            float now = Time.time;
            var legends = FilmLegends.All;
            for (int i = 0; i < legends.Length; i++)
            {
                var l = legends[i];
                var site = DiveSites.Get(l.siteId);
                var sp = l.Species;
                if (site == null || sp == null) continue;
                bool alive = IsAlive(i, sp, l);
                float dist = NearestDiver(site.center);
                if (alive)
                {
                    if (dist > site.radius + DespawnMargin || FilmLegends.IsCaught(l) && dist > site.radius + SpawnMargin) Despawn(i);
                    continue;
                }
                spawned[i] = null;
                if (FilmLegends.IsCaught(l) || !Progression.IsSiteUnlocked(site) || now < cooldown[i]) continue;
                if (dist > site.radius + SpawnMargin) continue;
                Spawn(i, l, sp, site, spawner);
            }
        }

        bool IsAlive(int i, SpeciesDef sp, LegendDef l)
        {
            var c = spawned[i];
            if (c == null) return false;
            if (c is Object o && o == null) return false;
            // pooled creature objects get reused for other animals: check it is still our legend
            return c.IsAlive && ReferenceEquals(c.Species, sp) && Mathf.Abs(c.SizeCm - l.sizeCm) < 1f;
        }

        static float NearestDiver(Vector3 site)
        {
            float best = float.MaxValue;
            var p = Refs.Player;
            if (p != null && !(p is Object po && po == null) && p.Root != null) best = Flat(p.Root.position - site);
            var all = Divers.All;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                var t = all[i];
                if (t == null) continue;
                float d = Flat(t.position - site);
                if (d < best) best = d;
            }
            return best;
        }

        static float Flat(Vector3 d) => Mathf.Sqrt(d.x * d.x + d.z * d.z);

        void Spawn(int i, LegendDef l, SpeciesDef sp, DiveSite site, ICreatureSpawner spawner)
        {
            var gm = GameManager.I;
            int seed = gm != null && gm.State != null ? gm.State.saveSeed : 1;
            var rng = new System.Random(unchecked(seed * 31 + i * 7919 + (gm != null ? gm.State.day : 0)));
            float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
            float rad = site.radius * (0.2f + 0.25f * (float)rng.NextDouble());
            Vector3 p = new Vector3(site.center.x + Mathf.Cos(ang) * rad, 0f, site.center.z + Mathf.Sin(ang) * rad);

            float floor = Mathf.Max(0f, Waves.SeaLevel - WorldInfo.GroundHeight(p));
            float len = l.sizeCm / 100f;
            float want = Mathf.Clamp(sp.PrefDepth, sp.minDepth, sp.maxDepth);
            float top = Mathf.Max(1.5f, len * 0.25f + 1f);
            float bottom = floor - Mathf.Max(1.5f, len * 0.35f);
            if (bottom < top) return;   // too shallow for this giant here – try again next check
            p.y = Waves.SeaLevel - Mathf.Clamp(want, top, bottom);

            ICreature c = null;
            try { c = spawner.Spawn(sp, p, l.sizeCm, false); }
            catch (Exception e) { Debug.LogWarning("[Filming] legend " + l.id + " could not be spawned: " + e.Message); }
            spawned[i] = c;
            cooldown[i] = Time.time + RespawnCooldown;
            if (c != null) Debug.Log("[Filming] legend " + l.id + " swims at " + site.id + " (" + Loc.Length(l.sizeCm) + ")");
        }

        void Despawn(int i)
        {
            var c = spawned[i];
            spawned[i] = null;
            if (c == null || c is Object o && o == null) return;
            var spawner = Refs.Spawner;
            if (spawner == null || spawner is Object so && so == null) return;
            try { spawner.Despawn(c); }
            catch (Exception e) { Debug.LogWarning("[Filming] legend despawn failed: " + e.Message); }
        }

        void DespawnAll()
        {
            for (int i = 0; i < spawned.Length; i++) if (spawned[i] != null) Despawn(i);
        }
    }
}
