using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Round 3 (GDD §20 Q12 „Geheimnisse mittel mit Hinweisen – Tipps aus Logbuch und von NPCs“): who knows which clue.
    /// Each entry: a key (shared flag "clue:<key>" once told), the line per NPC who knows it, when it is relevant and when
    /// its target counts as found. Pick returns the first relevant, unfound clue an NPC knows that was not told yet –
    /// else one that was told (a reminder) – so every ask brings something useful. Map pieces + the treasure (step 3),
    /// wrecks / chests / curiosities / Big Mo (step 4) and the old L.6 secrets re-homed after the village was removed.
    public static class NpcClues
    {
        public sealed class Entry
        {
            public string key;
            /// (npc id, line id) pairs.
            public string[] who;
            public Func<bool> relevant, found;
            public string Line(string npc)
            {
                for (int i = 0; i + 1 < who.Length; i += 2) if (who[i] == npc) return who[i + 1];
                return null;
            }
        }

        const string H = NpcDatabase.Horst, W = NpcDatabase.Wanda, F = NpcDatabase.Fin, T = NpcDatabase.Toni,
            HL = NpcDatabase.HLeader, HS = NpcDatabase.HSmith, HH = NpcDatabase.HHunter, HC = NpcDatabase.HCook, HO = NpcDatabase.HLookout, HG = NpcDatabase.HGardener;

        static List<Entry> all;

        static Entry E(string key, Func<bool> relevant, Func<bool> found, params string[] who) => new Entry { key = key, who = who, relevant = relevant, found = found };

        static bool Flag(string f) => StoryFlags.Has(f);
        static bool Secret(string k) => Collectibles.IsCollected("secret:" + k);
        static bool Seen(string sp) => Journal.IsDiscovered(sp);
        static bool Site(string s) => Progression.IsSiteUnlocked(s);
        static bool Piece(int i) => MapPieces.IsFound(i);

        public static List<Entry> All
        {
            get
            {
                if (all != null) return all;
                all = new List<Entry>
                {
                    // ---- the treasure map (step 3: 8 pieces, then the cove under the radio tower cliffs)
                    E("maps", () => MapPieces.Found == 0, () => MapPieces.Found > 0, H, "hint.maps.horst"),
                    E("map_p0", () => !MapPieces.Complete, () => Piece(0), H, "hint.map_p0.horst"),
                    E("chest_lagoon", () => true, () => Flag("chest:lagoon_raft"), H, "hint.chest_lagoon.horst", HC, "hint.chest_lagoon.h_cook"),
                    E("map_p1", () => !MapPieces.Complete, () => Piece(1), HH, "hint.map_p1.h_hunter", H, "hint.map_p1.horst"),
                    E("meme_shades", () => true, () => Seen("meme_shades"), H, "hint.meme_shades.horst"),
                    E("map_p2", () => !MapPieces.Complete, () => Piece(2), HG, "hint.map_p2.h_gardener", H, "hint.map_p2.horst"),
                    E("lighthouse_top", () => true, () => Secret("lighthouse_top"), H, "horst.clue.top"),
                    E("map_p3", () => !MapPieces.Complete, () => Piece(3), H, "hint.map_p3.horst"),
                    E("wreck_sailboat", () => true, () => Flag("found:sailboat"), W, "hint.wreck_sailboat.wanda"),
                    E("chest_reef", () => true, () => Flag("chest:hr_coral") && Flag("chest:hr_ledge"), H, "hint.chest_reef.horst"),
                    E("map_p4", () => !MapPieces.Complete, () => Piece(4), HG, "hint.map_p4.h_gardener", H, "hint.map_p4.horst"),
                    E("meme_crabrave", () => true, () => Seen("meme_crabrave"), T, "hint.meme_crabrave.toni"),
                    E("meme_cart", () => true, () => Seen("meme_cart"), H, "hint.meme_cart.horst"),
                    E("map_p5", () => !MapPieces.Complete, () => Piece(5), HH, "hint.map_p5.h_hunter", H, "hint.map_p5.horst"),
                    E("ravine_piggy", () => true, () => Secret("ravine_piggy"), H, "horst.clue.ravine"),
                    E("map_p6", () => !MapPieces.Complete, () => Piece(6), HS, "hint.map_p6.h_smith", H, "hint.map_p6.horst"),
                    E("map_p7", () => !MapPieces.Complete, () => Piece(7), H, "hint.map_p7.horst"),
                    E("treasure", () => MapPieces.Complete, () => Flag(StoryKeys.TreasureFound), HO, "hint.treasure.h_lookout", H, "hint.treasure.horst"),
                    E("wreck_seaplane", () => true, () => Flag("found:seaplane"), W, "hint.wreck_seaplane.wanda"),
                    E("meme_ducks", () => true, () => Seen("meme_ducks"), W, "hint.meme_ducks.wanda"),
                    E("wreck_trawler", () => true, () => Flag("found:trawler"), HO, "hint.wreck_trawler.h_lookout", F, "hint.wreck_trawler.fin"),
                    E("meme_stonks", () => true, () => Seen("meme_stonks"), T, "hint.meme_stonks.toni"),
                    E("meme_gamingpc", () => true, () => Seen("meme_gamingpc"), T, "hint.meme_gamingpc.toni"),
                    E("wreck_pirates", () => true, () => Flag("found:pirates"), HO, "hint.wreck_pirates.h_lookout", W, "hint.wreck_pirates.wanda"),
                    E("kelp_carving", () => Site("kelp_forest"), () => Secret("kelp_carving"), H, "horst.clue.kelp"),
                    E("chest_kelp", () => Site("kelp_forest"), () => Flag("chest:kelp_a") && Flag("chest:kelp_b"), F, "hint.chest_kelp.fin"),
                    E("meme_dance", () => Site("kelp_forest"), () => Seen("meme_dance"), T, "hint.meme_dance.toni"),
                    E("meme_gnome", () => Site("kelp_forest"), () => Seen("meme_gnome"), H, "hint.meme_gnome.horst"),
                    E("lighthouse_cellar", () => true, () => Secret("lighthouse_cellar"), H, "horst.clue.cellar"),
                    E("meme_throne", () => Site("wreck"), () => Seen("meme_throne"), F, "hint.meme_throne.fin"),
                    E("big_mo", () => VolcanoGate.Open && Site("wreck"), () => Seen("mosasaurus") || Flag(FaunaVolcano.MonsterRepelledFlag),
                        HO, "hint.big_mo.h_lookout", F, "hint.big_mo.fin", H, "hint.big_mo.horst"),
                    E("fossils", () => VolcanoGate.Open && Site("wreck"), () => false, F, "hint.fossils.fin"),
                    E("meme_dino", () => VolcanoGate.Open && Site("wreck"), () => Seen("meme_dino"), HO, "hint.meme_dino.h_lookout"),
                    // ---- the old L.6 secrets that were the villagers' (Collection app shows the heard clue)
                    E("waterfall", () => true, () => Secret("waterfall"), HG, "hm.clue.falls"),
                    E("village_loft", () => true, () => Secret("village_loft"), HL, "hm.clue.loft"),
                    E("tide_page", () => true, () => false, HL, "hint.tide_page.h_leader"),
                    E("eye", () => true, () => false, HL, "hint.eye.h_leader"),
                    E("blue_buoy", () => Site("blue_drop") && Flag(StoryKeys.FogMap), () => Secret("blue_buoy"), T, "toni.clue.buoy"),
                    E("fog_archive", () => Flag(StoryKeys.FogMap), () => Secret("fog_archive"), T, "toni.clue.archive"),
                };
                return all;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { all = null; }

        public static string FlagOf(Entry e) => NpcKeys.CluePrefix + e.key;

        /// HOST: the clue line this NPC tells next (mark = remember it as told). Null when the NPC knows nothing useful.
        public static string Pick(string npc, bool mark)
        {
            var list = All;
            Entry again = null;
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                string line = e.Line(npc);
                if (line == null) continue;
                bool rel, fnd;
                try { rel = e.relevant(); fnd = e.found(); }
                catch (Exception) { continue; }
                if (!rel || fnd) continue;
                if (StoryFlags.Has(FlagOf(e))) { if (again == null) again = e; continue; }
                if (mark && Net.HasAuthority) StoryFlags.Set(FlagOf(e));
                return line;
            }
            return again != null ? again.Line(npc) : null;
        }

        /// "Nothing new" line of an NPC who speaks (null for hermits – they put a finger to the lips).
        public static string NoneLine(string npc)
        {
            switch (npc)
            {
                case NpcDatabase.Horst: return "hint.none.horst";
                case NpcDatabase.Wanda: return "hint.none.wanda";
                case NpcDatabase.Fin: return "hint.none.fin";
                case NpcDatabase.Toni: return "hint.none.toni";
                case NpcDatabase.Chad: case NpcDatabase.Brad: return "hint.none.rival";
            }
            return null;
        }

        /// Told and still unfound clues (Tasks app „Hinweise“): text + who said it.
        public static void Heard(List<KeyValuePair<string, string>> into)
        {
            into.Clear();
            var list = All;
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (!StoryFlags.Has(FlagOf(e))) continue;
                bool fnd;
                try { fnd = e.found(); } catch (Exception) { fnd = true; }
                if (fnd || e.key == "eye" || e.key == "maps") continue;
                string line = null, who = null;
                for (int k = 0; k + 1 < e.who.Length; k += 2) { line = e.who[k + 1]; who = e.who[k]; break; }
                if (line == null) continue;
                into.Add(new KeyValuePair<string, string>(DialogueDatabase.Resolve(line, null), NpcDatabase.NameOf(DialogueDatabase.SpeakerOf(line) ?? who)));
            }
        }
    }
}
