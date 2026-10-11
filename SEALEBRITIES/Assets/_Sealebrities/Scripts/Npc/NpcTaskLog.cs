using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Read-only story/quest data for the phone apps (Tasks „Aufgaben“, Dr. Fin, Collection): main goals (K.5), Gus'
    /// job (B.2), story chains (Toni, the brothers), side quests (L.7), research (B.4) and secret clues (L.6).
    /// Everything is derived from the shared GameState, so clients see the host's progress.
    public static class StoryLog
    {
        public struct Entry
        {
            public string id;          // quest id / "gus:M1" / research id – Quests.Track(id) for side quests
            public string title;       // quest title
            public string line;        // current step (≤ 42 characters)
            public string progress;    // "2/3" or null
            public float progress01;
            public string giver;       // NPC display name (null for research / main goals)
            public Color color;        // giver colour
            public bool trackable, tracked;
        }

        static GameState S => GameManager.I != null && GameManager.I.InGame ? GameManager.I.State : null;

        /// K.5: the three main goals (1,000,000 followers, a full Sealog, the documentary live from the deep).
        public static void MainGoals(List<Entry> into)
        {
            into.Clear();
            var s = S;
            if (s == null) return;
            long f = s.followers;
            into.Add(new Entry { id = "goal_followers", title = Loc.Tr("1.000.000 Follower", "1,000,000 followers"),
                progress = Compact(f) + " / 1 Mio.", progress01 = Mathf.Clamp01(f / 1000000f), line = Loc.Tr("Gus' Kanal groß machen", "Grow Gus's channel") });
            int found = Journal.DiscoveredCount, all = Mathf.Max(1, SpeciesDatabase.Count);
            into.Add(new Entry { id = "goal_sealog", title = Loc.Tr("Das Sealog füllen", "Fill the Sealog"),
                progress = found + "/" + all, progress01 = found / (float)all, line = Loc.Tr("Jede Art einmal fotografieren", "Photograph every species once") });
            bool done = s.finaleReached;
            into.Add(new Entry { id = "goal_live", title = Loc.Tr("Live aus der Tiefe", "Live from the Deep"),
                progress = done ? "100 %" : null, progress01 = done ? 1f : 0f,
                line = done ? Loc.Tr("Gus' Doku ist fertig!", "Gus's documentary is finished!") : Loc.Tr("Gus' Doku zu Ende drehen – live", "Finish Gus's documentary – live") });
        }

        static string Compact(long n)
        {
            if (n >= 1000000) return (n / 1000000f).ToString("0.#", Loc.Culture) + Loc.Tr(" Mio.", "M");
            if (n >= 10000) return (n / 1000f).ToString("0.#", Loc.Culture) + "k";
            return Loc.Number(n);
        }

        /// Gus' active job + story chains (Toni, rivals).
        public static void Story(List<Entry> into)
        {
            into.Clear();
            var s = S;
            if (s == null) return;
            var m = Missions.Current;
            if (m != null)
            {
                string line = MissionLineFor(m.id, m.progress);
                into.Add(new Entry { id = "gus:" + m.id, title = NpcDirector.MissionTitle(m.id), line = line, giver = NpcDatabase.NameOf(NpcDatabase.Gus),
                    color = NpcDatabase.ColorOf(NpcDatabase.Gus), progress = m.id == "M3" ? Bits(m.progress & 7) + "/3" : null });
            }
            if (StoryFlags.Has(StoryKeys.ToniArrived) && !StoryFlags.Has(StoryKeys.FogMap))
            {
                var q = Quests.Get(Quests.ToniChain);
                int step = q != null ? Mathf.Clamp(q.step, 0, 3) : 0;
                into.Add(new Entry { id = Quests.ToniChain, title = SideQuestDatabase.ToniChain.Title, line = SideQuestDatabase.ToniChain.steps[step].Line,
                    giver = NpcDatabase.NameOf(NpcDatabase.Toni), color = NpcDatabase.ColorOf(NpcDatabase.Toni), progress = (step + 1) + "/4", progress01 = step / 4f });
            }
            if (StoryFlags.Has(NpcKeys.RivalR2Started) && !StoryFlags.Has(NpcKeys.RivalR2Done))
                into.Add(new Entry { id = "rival_r2", title = Loc.Tr("„Gerald ist CGI!“", "'Gerald is CGI!'"), line = Loc.Tr("Gerald 20 s filmen + ehrlich hochladen", "Film Gerald 20 s + honest upload"),
                    giver = "Bottom Feeders", color = NpcDatabase.ColorOf(NpcDatabase.Chad) });
            if (StoryFlags.Has(NpcKeys.RivalR3Started) && !StoryFlags.Has(NpcKeys.RivalR3Done))
                into.Add(new Entry { id = "rival_r3", title = Loc.Tr("Köder-Skandal", "Chum Scandal"), line = Loc.Tr("Foto: Yacht beim Ködern am Wrack", "Photo: the yacht chumming at the wreck"),
                    giver = "Bottom Feeders", color = NpcDatabase.ColorOf(NpcDatabase.Chad) });
            if (StoryFlags.Has(NpcKeys.RivalR4Started) && !StoryFlags.Has(NpcKeys.RivalR4Rescued) && !StoryFlags.Has(NpcKeys.RivalR4Ignored))
                into.Add(new Entry { id = "rival_r4", title = Loc.Tr("Wettlauf zum Auge", "Race to the Eye"), line = Loc.Tr("Den Brüdern Luft bringen (85 m)", "Bring the brothers air (85 m)"),
                    giver = "Bottom Feeders", color = NpcDatabase.ColorOf(NpcDatabase.Chad) });
        }

        static string MissionLineFor(string id, int progress)
        {
            switch (id)
            {
                case "M1": return Loc.Tr("Glücksflosse beim Kelp-Inselchen (14–18 m)", "Lucky fin near the kelp islet (14–18 m)");
                case "M2": return Loc.Tr("Gus' Funkgerät in der Kombüse der MS Clickbait", "Gus's radio in the MS Clickbait's galley");
                case "M3": return Bits(progress & 7) >= 3 ? Loc.Tr("Scherben oben am Leuchtturm einsetzen", "Fit the shards at the lighthouse top")
                                                         : Loc.Tr("Scherben: Leuchtturm, Grotte, Funkturm", "Shards: lighthouse, grotto, radio tower");
                case "M4": return Loc.Tr("Antennenmast im Graben filmen (92 m)", "Film the antenna mast in the trench (92 m)");
            }
            return "";
        }

        /// Running side quests (L.7) – tracked one first.
        public static void Side(List<Entry> into)
        {
            into.Clear();
            var s = S;
            if (s == null) return;
            for (int i = 0; i < s.sideQuests.Count; i++)
            {
                var q = s.sideQuests[i];
                if (q == null || q.done || q.id == Quests.ToniChain) continue;
                var d = SideQuestDatabase.Get(q.id);
                if (d == null || q.step >= d.steps.Length) continue;
                var st = d.steps[q.step];
                string prog = null;
                float p01 = q.step / (float)d.steps.Length;
                if (st.count > 1)
                {
                    int have = Mathf.Clamp(q.progress, 0, st.count);
                    prog = have + "/" + st.count;
                    p01 = (q.step + have / (float)st.count) / d.steps.Length;
                }
                var e = new Entry { id = d.id, title = d.Title, line = st.Line, progress = prog, progress01 = p01, giver = NpcDatabase.NameOf(d.giver),
                    color = NpcDatabase.ColorOf(d.giver), trackable = true, tracked = s.trackedQuest == d.id };
                if (e.tracked) into.Insert(0, e); else into.Add(e);
            }
        }

        /// Side quests that a giver offers right now (marker "!").
        public static void Offered(List<Entry> into)
        {
            into.Clear();
            var all = SideQuestDatabase.All;
            for (int i = 0; i < all.Length; i++)
            {
                var d = all[i];
                if (!NpcDirector.IsOffered(d)) continue;
                into.Add(new Entry { id = d.id, title = d.Title, giver = NpcDatabase.NameOf(d.giver), color = NpcDatabase.ColorOf(d.giver),
                    line = Loc.Tr("Bei ", "Ask ") + NpcDatabase.NameOf(d.giver) + Loc.Tr(" nachfragen", "") });
            }
        }

        /// Dr. Fin's open research tasks (B.4).
        public static void Research(List<Entry> into)
        {
            into.Clear();
            var all = ResearchDatabase.All;
            for (int i = 0; i < all.Length; i++)
            {
                var d = all[i];
                if (!NpcDirector.IsResearchOpen(d.id)) continue;
                int p = NpcDirector.ResearchProgress(d.id);
                int have = d.kind == ResearchKind.Samples ? Bits(p) : p;
                string prog = d.kind == ResearchKind.Listen ? Mathf.Min(p, (int)d.a) + "/" + (int)d.a + " s" : d.count > 1 ? Mathf.Min(have, d.count) + "/" + d.count : null;
                string reward = d.money > 0 ? Loc.Money(d.money) : d.rod == 4 ? Loc.Tr("Forschungsrute", "Research rod") : null;
                into.Add(new Entry { id = d.id, title = d.Title, line = reward, progress = prog, giver = NpcDatabase.NameOf(NpcDatabase.Fin), color = NpcDatabase.ColorOf(NpcDatabase.Fin),
                    progress01 = d.count > 1 ? Mathf.Clamp01(have / (float)d.count) : 0f });
            }
        }

        // ---------------------------------------------------------------- L.6 secret clues (Collection app)
        static readonly string[,] Clues =
        {
            { "cave_grotto", null }, { "waterfall", "juno.clue.falls" }, { "lighthouse_top", "pearl.clue.top" }, { "radio_tower", null },
            { "ravine_piggy", "lio.clue.ravine" }, { "wreck_cabin", null }, { "kelp_carving", "pearl.clue.kelp" }, { "wreck_hammock", "gus.hammock.2" },
            { "blue_buoy", "toni.clue.buoy" }, { "trench_tv", "gus.m100000.3" }, { "lighthouse_cellar", "pearl.clue.cellar" }, { "village_loft", "pearl.clue.loft" },
            { "fog_archive", "toni.clue.archive" },
        };

        /// Secrets whose clue is a page of Gus' logbook (PropLore index): the cave behind the root curtain (page 6),
        /// the crackling radio up the tower (page 15), the cabin key fed to the morays (page 20).
        static int CluePage(string secretKey)
        {
            switch (secretKey)
            {
                case "cave_grotto": return 5;
                case "radio_tower": return 14;
                case "wreck_cabin": return 19;
                default: return -1;
            }
        }

        static bool PageCollected(string secretKey)
        {
            int p = CluePage(secretKey);
            return p >= 0 && Collectibles.IsCollected(PropLore.PageId(p));
        }

        /// The clue of a secret was heard from an NPC / radio (StoryFlags "clue:&lt;key&gt;") or read in Gus' logbook.
        public static bool ClueHeard(string secretKey) => StoryFlags.Has(NpcKeys.CluePrefix + secretKey) || PageCollected(secretKey);

        /// Secret key by index in L.6 order (0 cave_grotto … 12 fog_archive), null outside.
        public static string SecretKey(int index) => index >= 0 && index < Clues.GetLength(0) ? Clues[index, 0] : null;

        /// UITaskInfo.SecretClue: the clue of a still missing secret once an NPC told it (null otherwise).
        public static string SecretClueByIndex(int index)
        {
            string key = SecretKey(index);
            if (key == null || Collectibles.IsCollected("secret:" + key) || !ClueHeard(key)) return null;
            return ClueText(key);
        }

        /// Text of a clue in the current language: the NPC line, or the logbook page for the page clues.
        public static string ClueText(string secretKey)
        {
            for (int i = 0; i < Clues.GetLength(0); i++)
            {
                if (Clues[i, 0] != secretKey) continue;
                if (Clues[i, 1] != null) return DialogueDatabase.Resolve(Clues[i, 1], null);
                int p = CluePage(secretKey);
                return p >= 0 ? PropLore.Page(p) : null;
            }
            return null;
        }

        /// Who gave the clue (display name) – for "Hinweis von Perle".
        public static string ClueSource(string secretKey)
        {
            for (int i = 0; i < Clues.GetLength(0); i++)
                if (Clues[i, 0] == secretKey && Clues[i, 1] != null) return NpcDatabase.NameOf(DialogueDatabase.SpeakerOf(Clues[i, 1]));
            if (CluePage(secretKey) >= 0) return Loc.Tr("Gus' Logbuch", "Gus's logbook");
            return null;
        }

        /// T2 reward: Tomas marked the depth band of this species in the Sealog.
        public static bool HasDepthHint(string speciesId) => StoryFlags.Has(NpcKeys.DepthHintPrefix + speciesId);

        static int Bits(int v) { int n = 0; while (v != 0) { n += v & 1; v >>= 1; } return n; }
    }

}
