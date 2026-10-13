using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Named world spots of the npc module: where NPCs stand, sit and work, where quest objects lie, where vehicles
    /// berth. The props module may publish the exact spot of its own geometry with Register (e.g. "horst_bench",
    /// "v_meeting", "v_jetty", "q_kite"); otherwise a deterministic fallback is computed from WorldLayout anchors and
    /// the terrain (land / water depth search) – fixed seeds, the same on every co-op machine.
    /// Positions are world positions; y is the walkable surface (or the seabed for underwater spots).
    public static class QuestSpots
    {
        static readonly Dictionary<string, Vector3> overrides = new Dictionary<string, Vector3>();
        static readonly Dictionary<string, Vector3> cache = new Dictionary<string, Vector3>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { overrides.Clear(); cache.Clear(); }

        /// Props: publish the real position of a spot (call after the world is built).
        public static void Register(string id, Vector3 world)
        {
            if (string.IsNullOrEmpty(id)) return;
            overrides[id] = world;
            cache.Remove(id);
        }

        public static bool IsRegistered(string id) => id != null && overrides.ContainsKey(id);

        /// Forget the computed fallbacks (new world / terrain changed).
        public static void ClearCache() => cache.Clear();

        public static Vector3 Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return Vector3.zero;
            if (overrides.TryGetValue(id, out var o)) return o;
            if (cache.TryGetValue(id, out var c)) return c;
            c = Compute(id);
            cache[id] = c;
            return c;
        }

        // ---------------------------------------------------------------- fallback geometry
        static Vector3 V(float x, float z) => new Vector3(x, 0f, z);
        static Vector3 Off(Vector3 a, float dx, float dz) => new Vector3(a.x + dx, 0f, a.z + dz);

        static float Deck(Vector3 p) => NpcUtil.SurfaceY(p);

        /// Dock deck height at p (the props deck collider, else PropInfo.DockDeckY).
        static float DockY(Vector3 p)
        {
            float y = NpcUtil.SurfaceY(p);
            return y > 0.5f ? y : PropInfo.DockDeckY;
        }

        /// The nearest dry point (ground ≥ minY) on a spiral around p (fixed sampling – deterministic).
        public static Vector3 Land(Vector3 p, float minY = 0.4f, float maxR = 30f)
        {
            for (float r = 0f; r <= maxR; r += 1.5f)
            {
                int steps = r < 0.1f ? 1 : Mathf.Max(6, Mathf.RoundToInt(r * 2.2f));
                for (int i = 0; i < steps; i++)
                {
                    float a = i / (float)steps * Mathf.PI * 2f + r * 0.37f;
                    var q = new Vector3(p.x + Mathf.Cos(a) * r, 0f, p.z + Mathf.Sin(a) * r);
                    float y = Deck(q);
                    if (y >= minY && y < 60f && Slope(q) < 0.6f) { q.y = y; return q; }
                }
            }
            p.y = Mathf.Max(Deck(p), 0.3f);
            return p;
        }

        static float Slope(Vector3 p)
        {
            float h0 = WorldInfo.GroundHeight(p);
            float hx = WorldInfo.GroundHeight(p + new Vector3(0.8f, 0f, 0f)), hz = WorldInfo.GroundHeight(p + new Vector3(0f, 0f, 0.8f));
            return Mathf.Max(Mathf.Abs(hx - h0), Mathf.Abs(hz - h0)) / 0.8f;
        }

        /// The nearest seabed point with a water depth in [minD, maxD] on a spiral around p (y = seabed + lift).
        public static Vector3 Water(Vector3 p, float minD, float maxD, float maxR = 120f, float lift = 0.15f, int salt = 0)
        {
            float start = (salt % 7) * 0.9f;
            for (float r = 0f; r <= maxR; r += 2f)
            {
                int steps = r < 0.1f ? 1 : Mathf.Max(8, Mathf.RoundToInt(r * 1.4f));
                for (int i = 0; i < steps; i++)
                {
                    float a = i / (float)steps * Mathf.PI * 2f + start + r * 0.21f;
                    var q = new Vector3(p.x + Mathf.Cos(a) * r, 0f, p.z + Mathf.Sin(a) * r);
                    float g = WorldInfo.GroundHeight(q);
                    float d = Waves.SeaLevel - g;
                    if (d >= minD && d <= maxD) { q.y = g + lift; return q; }
                }
            }
            p.y = WorldInfo.GroundHeight(p) + lift;
            return p;
        }

        /// A point floating on the water surface (depth ≥ minD below) near p.
        public static Vector3 Surface(Vector3 p, float minD = 1.2f, float maxR = 60f)
        {
            var q = Water(p, minD, 999f, maxR, 0f);
            q.y = Waves.SeaLevel;
            return q;
        }

        static Vector3 Compute(string id)
        {
            Vector3 hut = WorldLayout.Hut, ds = WorldLayout.DockStart, de = WorldLayout.DockEnd, heli = WorldLayout.Helipad, vil = WorldLayout.Village;
            switch (id)
            {
                // ---- Horst (A.2)
                case "horst_porch": return Land(Off(hut, 11f, 3.5f));
                case "horst_bench": return Land(Off(ds, -3f, -3f));
                case "horst_tent": return Land(Off(hut, 3f, -11f));
                case "horst_campfire":
                    return PropInfo.Campfire != Vector3.zero ? Land(PropInfo.Campfire + new Vector3(1.8f, 0f, 1.2f)) : Land(Off(hut, 13f, -6f));
                // round 3 (Q10): Horst's corner on the beach (PropHorst registers the real spots) + his tutorial stand
                // on the sand beside the dock's landward end – never on the planks
                case "horst_corner": case "horst_hammock": return Land(PropHorst.Corner, 0.3f, 8f);
                case "horst_home": return Land(PropHorst.Corner - new Vector3(1.3f, 0f, 0f), 0.3f, 8f);
                case "horst_dockside": return Land(Off(ds, -3.5f, -4.5f), 0.4f, 10f);
                case "toni_evening": return Land(PropHorst.Corner + new Vector3(-3.2f, 0f, 3.4f), 0.3f, 8f);
                case "horst_dockend": { var p = Off(de, -3f, 0.85f); p.y = DockY(p); return p; }
                case "horst_dockmid": { var p = Off(ds, (de.x - ds.x) * 0.5f, 0.85f); p.y = DockY(p); return p; }
                case "horst_dockedge": { var p = Off(de, -6f, -0.95f); p.y = DockY(p); return p; }
                case "horst_fallwater": { var p = Off(de, -6f, -2.6f); p.y = Waves.SeaLevel - 0.9f; return p; }
                case "horst_helipad": return Land(Off(heli, 7f, 5f));
                case "horst_trail": return Land(Off(heli, 20f, 22f));
                case "dock": { var p = Off(ds, (de.x - ds.x) * 0.5f, 0f); p.y = DockY(p); return p; }
                case "helipad": return Land(heli);
                // ---- Wanda (K.5, L.12)
                case "wanda_pad": return Land(Off(heli, -3.5f, 2.5f));
                case "wanda_padedge": return Land(Off(heli, -6f, -3f));
                case "hangar": return Land(WorldLayout.HangarSpot);
                case "wanda_hangar": return Land(Off(WorldLayout.HangarSpot, -2.5f, 1.5f));
                // ---- berths (B.3, B.4): south side of the dock, vehicles need ≥ 1.8 m of water
                case "berth_a": return Surface(Off(ds, 12f, -7f), 1.6f, 25f);
                case "berth_b": return Surface(Off(de, -12f, -14f), 2.0f, 40f);
                case "fin_stand": { var p = Off(ds, 11f, -0.85f); p.y = DockY(p); return p; }
                case "rival_stand": { var p = Off(de, -9f, -0.85f); p.y = DockY(p); return p; }
                case "rival_stand2": { var p = Off(de, -7.6f, -0.85f); p.y = DockY(p); return p; }
                case "sea_entry": return Surface(Off(de, 140f, -90f), 5f, 80f);
                case "wreck_anchor": return Surface(PropInfo.WreckCenter != Vector3.zero ? PropInfo.WreckCenter + new Vector3(18f, 0f, 10f) : SiteCenter("wreck") + new Vector3(18f, 0f, 10f), 10f, 80f);
                case "trench_ledge": return Water(SiteCenter("deep_trench"), 82f, 88f, 200f, 0.6f, 4);
                case "antenna": return Water(SiteCenter("deep_trench") + new Vector3(-30f, 0f, 20f), 89f, 95f, 220f, 0f, 9);
                // ---- village Altriff (L.8) – terrace centre WorldLayout.Village, beach south, stilt lagoon below z −121
                case "village": return Land(vil);
                case "v_meeting": return Land(Off(vil, 0f, 4f), 1f);
                case "v_pearl": return Land(Off(vil, -2.5f, 5f), 1f);
                case "v_cards_h": return Land(Off(vil, 1.6f, 4.2f), 1f);
                case "v_calendar": return Land(Off(vil, 2.5f, 6.5f), 1f);
                case "v_boatshed": return Land(Off(vil, -16f, -2f), 0.8f);
                case "v_mira": return Land(Off(vil, -14f, -4f), 0.8f);
                case "v_kiosk": return Land(Off(vil, 12f, 2f), 1f);
                case "v_ada": return Land(Off(vil, 12.5f, 3.6f), 1f);
                case "v_stage": return Land(Off(vil, 6f, -6f), 1f);
                case "v_nets": return Land(Off(vil, -8f, -9f), 0.6f);
                case "v_tomas": return Land(Off(vil, -9f, -8f), 0.6f);
                case "v_lio": return Land(Off(vil, 18f, -10f), 0.5f);
                case "v_weaver1": return Land(Off(vil, -6f, 2f), 1f);
                case "v_weaver2": return Land(Off(vil, -4.6f, 1.2f), 1f);
                case "v_fisher1": return Land(Off(vil, -11f, -10f), 0.5f);
                case "v_fisher2": return Land(Off(vil, -12.2f, -9f), 0.5f);
                case "v_teen": return Land(Off(vil, 8f, -3f), 1f);
                case "v_grandpa": return Land(Off(vil, -18f, 6f), 1f);
                case "v_grandpa_carve": return Land(Off(vil, -17.6f, 2.2f), 1f);
                case "v_guesthouse": return Land(Off(vil, 16f, 7f), 1f);
                case "v_jetty": return Land(Off(vil, 6f, -13f), 0.5f);
                case "v_jetty_end": { var p = Off(vil, 6f, -24f); float y = Deck(p); if (y > 0.4f) { p.y = y; return p; } return Land(Off(vil, 6f, -13f), 0.5f); }
                case "v_lanterns": return Surface(Off(vil, 4f, -30f), 0.8f, 30f);
                case "v_canoe_start": return Surface(Off(vil, 2f, -27f), 0.7f, 30f);
                case "v_eve1": return Land(Off(vil, 3f, -11f), 0.5f);
                // ---- Toni (K.8)
                case "driftwood": return Land(WorldLayout.DriftwoodBeach, 0.3f);
                case "toni_lie": return Land(Off(WorldLayout.DriftwoodBeach, -1.5f, -2f), 0.3f);
                case "toni_raft": return Surface(WorldLayout.DriftwoodRaft, 0.3f, 15f);
                case "fog_camp": return Land(Off(WorldLayout.FogIsland, 0f, -20f), 0.4f, 25f);
                // ---- quest objects (L.7, K.8, B.2)
                case "q_sunglasses": return Water(Off(ds, 14f, 2.2f), 2.4f, 3.6f, 25f, 0.08f, 1);
                case "q_recorder": return Water(Off(WorldLayout.DriftwoodBeach, 22f, 18f), 3f, 4.2f, 40f, 0.1f, 2);
                case "q_coil": return Water(Off(hut, 45f, -60f), 2.6f, 3.4f, 80f, 0.1f, 3);
                case "q_valve": return PropInfo.RadioShack != Vector3.zero ? Land(PropInfo.RadioShack + new Vector3(0.8f, 0f, 0.6f), 0.4f, 6f) : Land(Off(WorldLayout.RadioTowerRuins, 3f, 2f));
                case "q_kite": return PropInfo.LighthouseGallery != Vector3.zero ? PropInfo.LighthouseGallery + new Vector3(1.2f, 0.9f, 0.6f) : Land(Off(WorldLayout.Lighthouse, 3f, 3f));
                case "q_paddle": return PropInfo.BayArch != Vector3.zero ? Land(PropInfo.BayArch + new Vector3(0f, 0f, 2f), 0.3f, 8f) : Land(Off(WorldLayout.HiddenBay, 27f, 4f), 0.3f);
                case "q_selfie_stick": return Water(Off(SiteCenter("house_reef"), -48f, -12f), 5.4f, 6.8f, 60f, 0.12f, 5);
                case "q_tape": return Water(Get("antenna") + new Vector3(3f, 0f, 2f), 88f, 96f, 30f, 0.2f, 6);
                case "q_liochest": return Land(Off(WorldLayout.HiddenBay, 24f, -8f), 0.4f);
                case "m1_fin": return Water(WorldLayout.KelpIsland + new Vector3(52f, 0f, -20f), 14f, 18f, 70f, 0.12f, 7);
                case "m2_radio": return PropInfo.WreckCenter != Vector3.zero ? PropInfo.WreckCenter + new Vector3(2f, 1.2f, 3f) : Water(SiteCenter("wreck"), 24f, 34f, 60f, 0.3f, 8);
                case "m3_shard1": return PropInfo.LighthouseGallery != Vector3.zero ? PropInfo.LighthouseGallery + new Vector3(-1f, 0.15f, -0.8f) : Land(Off(WorldLayout.Lighthouse, -2f, -2f));
                case "m3_shard2": return PropInfo.Grotto != Vector3.zero ? Land(PropInfo.Grotto + new Vector3(0.8f, 0f, 0.4f), 0.4f, 5f) : (PropInfo.CaveChamber != Vector3.zero ? Land(PropInfo.CaveChamber, 0.4f, 5f) : Land(WorldLayout.CaveEntrance));
                case "m3_shard3": return PropInfo.RadioShack != Vector3.zero ? Land(PropInfo.RadioShack + new Vector3(-1f, 0f, 0.4f), 0.4f, 6f) : Land(Off(WorldLayout.RadioTowerRuins, -2f, 3f));
                case "m3_lamp": return PropInfo.LighthouseGallery != Vector3.zero ? PropInfo.LighthouseGallery + new Vector3(0f, 0.2f, 0f) : Land(WorldLayout.Lighthouse);
                case "m4_mast": return Get("antenna");
                case "r4_sub": return Get("trench_ledge") + new Vector3(0f, 1.3f, 0f);
                case "house_reef": return SiteCenter("house_reef");
            }
            // generic: "<site id>" dive-site centres, stakes "stake:<site>:<n>", drums "drum:<n>", pegs, driftwood …
            if (id.StartsWith("stake:", System.StringComparison.Ordinal)) return Stake(id);
            if (id.StartsWith("drum:", System.StringComparison.Ordinal)) return Surface(Off(heli, 20f + Idx(id) * 9f, -18f + Idx(id) * 7f), 1.2f, 40f);
            if (id.StartsWith("peg:", System.StringComparison.Ordinal))
            {
                int i = Idx(id);
                if (i == 0) return Land(Off(vil, 14f, -11f), 0.25f);
                if (i == 1) return Land(Off(WorldLayout.TidePools, 3f, -2f), 0.25f);
                return Water(Off(vil, -20f, -26f), 1.6f, 2.4f, 40f, 0.1f, 11);
            }
            if (id.StartsWith("driftwood:", System.StringComparison.Ordinal))
            {
                int i = Idx(id);
                Vector3[] beaches = { WorldLayout.DriftwoodBeach + new Vector3(-6f, 0f, -2f), WorldLayout.DriftwoodBeach + new Vector3(7f, 0f, 1f),
                                      Off(vil, -22f, -12f), Off(WorldLayout.HiddenBay, 27f, -3f), Off(hut, 20f, 10f) };
                return Land(beaches[Mathf.Clamp(i, 0, beaches.Length - 1)], 0.2f);
            }
            if (id.StartsWith("seagrape:", System.StringComparison.Ordinal)) return Water(Off(hut, 30f - Idx(id) * 6f, 32f + Idx(id) * 4f), 1f, 3f, 45f, 0.1f, 20 + Idx(id));
            if (id.StartsWith("ghostnet:", System.StringComparison.Ordinal)) return Water(Off(SiteCenter("house_reef"), -30f + Idx(id) * 14f, 10f - Idx(id) * 9f), 5f, 9f, 40f, 0.6f, 30 + Idx(id));
            if (id.StartsWith("coconut:", System.StringComparison.Ordinal))
            {
                int i = Idx(id);
                Vector3[] palms = { Off(WorldLayout.DriftwoodBeach, -4f, -6f), Off(hut, 6f, 8f), Off(vil, 10f, -8f), Off(hut, -6f, 12f), Off(WorldLayout.DriftwoodBeach, 6f, -5f), Off(vil, -12f, -6f) };
                return Land(palms[Mathf.Abs(i) % palms.Length], 0.3f);
            }
            var site = DiveSites.Get(id);
            if (site != null) return SiteCenter(id);
            return Land(hut);
        }

        static int Idx(string id)
        {
            int c = id.LastIndexOf(':');
            return c >= 0 && int.TryParse(id.Substring(c + 1), out int i) ? i : 0;
        }

        public static Vector3 SiteCenter(string siteId)
        {
            var s = DiveSites.Get(siteId);
            return s != null ? new Vector3(s.center.x, 0f, s.center.z) : Vector3.zero;
        }

        /// Research stakes (K.3): 3 per site, on the seabed inside the site's depth range.
        static Vector3 Stake(string id)
        {
            // stake:<site>:<n>
            int a = id.IndexOf(':'), b = id.LastIndexOf(':');
            string site = b > a ? id.Substring(a + 1, b - a - 1) : id.Substring(a + 1);
            int n = Idx(id);
            var s = DiveSites.Get(site);
            if (s == null) return Vector3.zero;
            float ang = (n * 120f + NpcUtil.StableHash(site) % 90) * Mathf.Deg2Rad;
            var c = new Vector3(s.center.x + Mathf.Cos(ang) * s.radius * 0.35f, 0f, s.center.z + Mathf.Sin(ang) * s.radius * 0.35f);
            float min = Mathf.Max(2f, s.minDepth), max = Mathf.Max(min + 2f, Mathf.Min(s.maxDepth, s.minDepth + 14f));
            return Water(c, min, max, s.radius * 0.6f, 0f, 40 + n);
        }
    }
}
