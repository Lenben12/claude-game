using UnityEngine;

namespace Sealebrities
{
    /// Step kinds of a side quest (L.7). Progress lives in QuestState (step index + progress counter / bit mask).
    public enum SqStep : byte
    {
        /// Talk to the giver (hand-in) – also the last step of every quest.
        Talk,
        /// Carry quest items (Carry, "q:<item>") to the giver – or to `spot` when set (W1 drums → hangar).
        Bring,
        /// Collect quest objects of a group with E (bit mask of the object index).
        Collect,
        /// Hold E for `a` seconds on each object of a group (T1 ghost net).
        Hold,
        /// A photo matching `target` (species id or a tag – see SideQuestRunner.PhotoTags).
        Photo,
        /// A clip of `target` species (any of a csv list – bit mask when count > 1).
        Film,
        /// Catch a fish ≥ a kg (target = kind id or null for any), b = 1 → from the boat.
        Catch,
        /// Upload a video: target "honest" / "shark" / species id.
        Upload,
        /// Selfie with an NPC / species in frame.
        Selfie,
        /// Release a lantern on the Lantern Evening (L.8).
        Lantern,
        /// Bring an NPC (target) to a spot after hour a (P2 card night).
        Escort,
        /// Record `a` seconds with the camera while the condition `target` holds (J2 night reef, M3 canoe race).
        Record,
        /// Choose: give back (E) or keep (Q) – B1.
        Choice,
        /// Wait until day + 1, hour a (M3) – then the next step starts.
        Wait
    }

    public sealed class SqStepDef
    {
        public SqStep kind;
        public string target;
        public int count = 1;
        public float a, b;
        /// Quest-line text (≤ 42 characters).
        public string de, en;
        /// QuestSpots id for the distance arrow / map marker (null = the giver).
        public string spot;
        public string Line
        {
            get
            {
                string s = Loc.Tr(de, en);
                return s.IndexOf('{') >= 0 ? DialogueDatabase.ExpandTokens(s) : s;
            }
        }
    }

    public sealed class SideQuestDef
    {
        public string id, giver, titleDe, titleEn;
        public int minDay;
        public long minFollowers;
        public string needFlag, needSite, needQuest, needItem;
        public bool needVillage, needTutorial;
        public SqStepDef[] steps;
        public long money;
        public string cosmetic, flag;
        /// Short extra reward chip ("+2 Express", "Kiosk: Curry") shown with the done notification.
        public string chipDe, chipEn;
        /// Dialogue prefixes (start / end lines).
        public string StartSeq => "sq." + id + ".start";
        public string EndSeq => "sq." + id + ".end";
        public string Title => Loc.Tr(titleDe, titleEn);
        public string Chip => chipDe != null ? Loc.Tr(chipDe, chipEn) : null;
    }

    /// The 24 side quests of L.7 (+ Toni's chain "toni" of K.8). Quest rewards are balance constants (J.5: npc owns them).
    /// Ids match core's references: FoodDatabase.unlockQuest (A1, A2) and CosmeticsDatabase rules (quest:P2, L2, M2, M3, B1).
    public static class SideQuestDatabase
    {
        public const string Toni = "toni";

        static SqStepDef St(SqStep k, string target, int count, string de, string en, string spot = null, float a = 0f, float b = 0f) =>
            new SqStepDef { kind = k, target = target, count = count, de = de, en = en, spot = spot, a = a, b = b };

        static SqStepDef Back(string giverDe, string giverEn) => St(SqStep.Talk, null, 1, "Zurück zu " + giverDe, "Back to " + giverEn);

        public static readonly SideQuestDef[] All =
        {
            // ---------------------------------------------------------------- Horst
            new SideQuestDef { id = "H1", giver = NpcDatabase.Horst, titleDe = "Günthers Raubzug", titleEn = "Günther's Heist", needTutorial = true, money = 80,
                steps = new[] { St(SqStep.Photo, "guenther", 1, "Foto: Günther am Steg, 12–14 Uhr", "Photo: Günther at the dock, 12–2 pm", "dock", 12f, 14f), Back("Horst", "Horst") } },
            new SideQuestDef { id = "H2", giver = NpcDatabase.Horst, titleDe = "Horsts Sonnenbrille", titleEn = "Horst's Sunglasses", needTutorial = true, money = 50,
                steps = new[] { St(SqStep.Bring, "sunglasses", 1, "Sonnenbrille im Hafenbecken finden", "Find the sunglasses in the harbour", "q_sunglasses") } },
            new SideQuestDef { id = "H3", giver = NpcDatabase.Horst, titleDe = "Influencer-Workshop", titleEn = "Influencer Workshop", needTutorial = true, minDay = 5, money = 120,
                steps = new[] { St(SqStep.Selfie, NpcDatabase.Horst, 1, "Selfie mit Horst {K:Selfie}", "Selfie with Horst {K:Selfie}"), St(SqStep.Upload, "honest", 1, "Video mit ehrlichem Titel", "Video with an honest title"), Back("Horst", "Horst") } },
            // ---------------------------------------------------------------- Wanda
            new SideQuestDef { id = "W1", giver = NpcDatabase.Wanda, titleDe = "Treibende Fässer", titleEn = "Drifting Drums", needFlag = NpcKeys.FirstDelivery, money = 150,
                chipDe = "+2 Express", chipEn = "+2 express",
                steps = new[] { St(SqStep.Bring, "drum", 3, "Fässer zum Hangar tragen", "Carry the drums to the hangar", "hangar"), Back("Wanda", "Wanda") } },
            new SideQuestDef { id = "W2", giver = NpcDatabase.Wanda, titleDe = "Prospekt-Fotos", titleEn = "Brochure Photos", minDay = 4, money = 120,
                steps = new[] { St(SqStep.Photo, "lm_lighthouse,lm_hut,lm_village", 3, "Vom Wasser: Leuchtturm, Hütte, Dorf", "From the water: lighthouse, hut, village"), Back("Wanda", "Wanda") } },
            // ---------------------------------------------------------------- Pearl
            new SideQuestDef { id = "P1", giver = NpcDatabase.Pearl, titleDe = "Die Gezeitensteine", titleEn = "The Tide Pegs", needVillage = true, minDay = 3, money = 200,
                flag = StoryKeys.NightOfTheEye,
                steps = new[] { St(SqStep.Collect, "peg", 3, "Gezeitenpflöcke finden", "Find the tide pegs"), Back("Perle", "Pearl") } },
            new SideQuestDef { id = "P2", giver = NpcDatabase.Pearl, titleDe = "Kartenabend", titleEn = "Card Night", needVillage = true, minDay = 5, cosmetic = "hat:13",
                steps = new[] { St(SqStep.Escort, NpcDatabase.Horst, 1, "Horst ab 18 Uhr ins Dorf bringen", "Bring Horst to the village after 6 pm", "v_meeting", 18f), Back("Perle", "Pearl") } },
            new SideQuestDef { id = "P3", giver = NpcDatabase.Pearl, titleDe = "Lichter auf dem Wasser", titleEn = "Lights on the Water", needVillage = true, needFlag = NpcKeys.LanternSeen, money = 150,
                flag = NpcKeys.PearlLightStory,
                steps = new[] { St(SqStep.Lantern, null, 1, "Eine Laterne loslassen", "Release a lantern", "v_lanterns"),
                                St(SqStep.Photo, "lanterns", 1, "Foto vom Wasser: 5 Laternen", "Photo from the water: 5 lanterns", "v_lanterns"), Back("Perle", "Pearl") } },
            // ---------------------------------------------------------------- Mira
            new SideQuestDef { id = "M1", giver = NpcDatabase.Mira, titleDe = "Treibholz", titleEn = "Driftwood", needVillage = true, money = 120,
                steps = new[] { St(SqStep.Collect, "driftwood", 5, "Treibholz an den Stränden sammeln", "Collect driftwood on the beaches"), Back("Mira", "Mira") } },
            new SideQuestDef { id = "M2", giver = NpcDatabase.Mira, titleDe = "Gus' altes Paddel", titleEn = "Gus's Old Paddle", needQuest = "M1", money = 200, cosmetic = "boatflag:1",
                steps = new[] { St(SqStep.Bring, "paddle", 1, "Paddel aus der Buchthöhle holen", "Fetch the paddle from the cove cave", "v_boatshed"), Back("Mira", "Mira") } },
            new SideQuestDef { id = "M3", giver = NpcDatabase.Mira, titleDe = "Das erste Paddeln", titleEn = "First Paddle", needQuest = "M2", money = 300, cosmetic = "boatflag:2",
                steps = new[] { St(SqStep.Wait, null, 1, "Morgen um 10 Uhr am Dorfsteg", "Tomorrow 10 am at the village jetty", "v_jetty", 10f),
                                St(SqStep.Record, "canoe", 1, "Das Kanu-Rennen filmen (8 s)", "Film the canoe race (8 s)", "v_jetty", 8f), Back("Mira", "Mira") } },
            // ---------------------------------------------------------------- Tomas
            new SideQuestDef { id = "T1", giver = NpcDatabase.Tomas, titleDe = "Geisternetz", titleEn = "Ghost Net", needVillage = true, minDay = 2, money = 200, flag = NpcKeys.GhostNetCleared,
                chipDe = "Riff +10 % Tiere", chipEn = "Reef +10 % animals",
                steps = new[] { St(SqStep.Hold, "ghostnet", 3, "Geisternetz lösen ({K:Interact} halten)", "Cut the ghost net (hold {K:Interact})", null, 5f), Back("Tomas", "Tomas") } },
            new SideQuestDef { id = "T2", giver = NpcDatabase.Tomas, titleDe = "Wo wohnt der Zackenbarsch?", titleEn = "Where Does the Grouper Live?", needVillage = true, needSite = "wreck", money = 250,
                chipDe = "3 Tiefen im Sealog", chipEn = "3 Sealog depths",
                steps = new[] { St(SqStep.Photo, "grouper", 1, "Foto: Zackenbarsch unter 25 m", "Photo: grouper below 25 m", null, 25f), Back("Tomas", "Tomas") } },
            new SideQuestDef { id = "T3", giver = NpcDatabase.Tomas, titleDe = "Der große Fang", titleEn = "The Big Catch", needVillage = true, needItem = "rod_t1", money = 150,
                steps = new[] { St(SqStep.Catch, null, 1, "Fisch über 3 kg vom Boot", "Fish over 3 kg from the boat", null, 3f, 1f), Back("Tomas", "Tomas") } },
            // ---------------------------------------------------------------- Ada
            new SideQuestDef { id = "A1", giver = NpcDatabase.Ada, titleDe = "Kokos-Curry", titleEn = "Coconut Curry", needVillage = true, money = 80,
                chipDe = "Kiosk: Curry", chipEn = "Kiosk: curry",
                steps = new[] { St(SqStep.Bring, "coconut", 3, "3 Kokosnüsse (+2 Makrelen in der Kühlbox)", "3 coconuts (+2 mackerel in the cooler)") } },
            new SideQuestDef { id = "A2", giver = NpcDatabase.Ada, titleDe = "Seetrauben", titleEn = "Sea Grapes", needVillage = true, minDay = 4, money = 80,
                chipDe = "Kiosk: Salat", chipEn = "Kiosk: salad",
                steps = new[] { St(SqStep.Collect, "seagrape", 6, "Seetrauben im Seegras pflücken", "Pick sea grapes in the sea grass"), Back("Ada", "Ada") } },
            // ---------------------------------------------------------------- Juno
            new SideQuestDef { id = "J1", giver = NpcDatabase.Juno, titleDe = "Walgesang", titleEn = "Whale Song", needVillage = true, needSite = "blue_drop", money = 400, flag = NpcKeys.OldreefSong,
                chipDe = "Menü-Lied", chipEn = "Menu song",
                steps = new[] { St(SqStep.Film, "humpback", 1, "Big Bertha filmen", "Film Big Bertha"), Back("Juno", "Juno") } },
            new SideQuestDef { id = "J2", giver = NpcDatabase.Juno, titleDe = "Knallkrebs-Konzert", titleEn = "Snapping Shrimp Concert", needVillage = true, minDay = 3, money = 120,
                steps = new[] { St(SqStep.Record, "night_reef", 1, "Nachts am Hausriff 20 s filmen", "Record 20 s at the house reef at night", "house_reef", 20f), Back("Juno", "Juno") } },
            // ---------------------------------------------------------------- Lio
            new SideQuestDef { id = "L1", giver = NpcDatabase.Lio, titleDe = "Mein Drachen!", titleEn = "My Kite!", needVillage = true, money = 50,
                steps = new[] { St(SqStep.Bring, "kite", 1, "Lios Drachen vom Leuchtturm holen", "Get Lio's kite from the lighthouse", "q_kite") } },
            new SideQuestDef { id = "L2", giver = NpcDatabase.Lio, titleDe = "Ein echter Hai!", titleEn = "A Real Shark!", needVillage = true, minFollowers = 300, cosmetic = "hat:14", money = 100,
                steps = new[] { St(SqStep.Upload, "shark", 1, "Video mit Hai hochladen", "Upload a video with a shark"), Back("Lio", "Lio") } },
            new SideQuestDef { id = "L3", giver = NpcDatabase.Lio, titleDe = "Lios Schatzkarte", titleEn = "Lio's Treasure Map", needVillage = true, minDay = 6, money = 200,
                chipDe = "Goldmünzen", chipEn = "Gold coins",
                steps = new[] { St(SqStep.Collect, "liochest", 1, "Dem Buntstift-X folgen", "Follow the crayon X", "q_liochest"), Back("Lio", "Lio") } },
            // ---------------------------------------------------------------- Toni (after K.8)
            new SideQuestDef { id = "N1", giver = NpcDatabase.Toni, titleDe = "Tonaufnahmen", titleEn = "Sound Recordings", needFlag = StoryKeys.FogMap, money = 300,
                steps = new[] { St(SqStep.Film, "dolphin,sea_lion,humpback", 3, "Delfin, Seelöwe, Wal filmen", "Film dolphin, sea lion, whale"), Back("Toni", "Tony") } },
            new SideQuestDef { id = "N2", giver = NpcDatabase.Toni, titleDe = "Das letzte Band", titleEn = "The Last Tape", needFlag = StoryKeys.FogMap, needSite = "deep_trench", money = 2000,
                flag = NpcKeys.LastTape,
                steps = new[] { St(SqStep.Bring, "tape", 1, "Das Band am Antennenmast (92 m)", "The tape at the antenna mast (92 m)", "q_tape") } },
            // ---------------------------------------------------------------- Chad & Brad
            new SideQuestDef { id = "B1", giver = NpcDatabase.Brad, titleDe = "Der goldene Selfie-Stick", titleEn = "The Golden Selfie Stick", needFlag = NpcKeys.RivalR1Done, money = 0,
                steps = new[] { St(SqStep.Bring, "selfie_stick", 1, "Selfie-Stick im Hausriff finden", "Find the selfie stick in the house reef", "q_selfie_stick"),
                                St(SqStep.Choice, null, 1, "Zurückgeben oder behalten?", "Give it back or keep it?") } },
        };

        /// Toni's chain (K.8): T1 talk, T2 coconut + grilled fish, T3 recorder, T4 valve + coil + battery. Steps match
        /// StoryKeys.ToniT1…T4. Not in All (no giver marker logic of the side quests, exempt from the 3-quest limit).
        public static readonly SideQuestDef ToniChain = new SideQuestDef
        {
            id = Toni, giver = NpcDatabase.Toni, titleDe = "Schiffbrüchig!", titleEn = "Castaway!",
            steps = new[]
            {
                St(SqStep.Talk, null, 1, "Mit dem Schiffbrüchigen reden", "Talk to the castaway", "driftwood"),
                St(SqStep.Bring, "coconut,grilled_fish", 2, "Kokosnuss + Grillfisch für Toni", "Coconut + grilled fish for Tony", "driftwood"),
                St(SqStep.Bring, "recorder", 1, "Tonis Tonbandgerät bergen", "Recover Tony's tape recorder", "q_recorder"),
                St(SqStep.Collect, "valve,coil", 2, "Röhre, Spule + Funkbatterie", "Valve, coil + radio battery", null),
            }
        };

        public static SideQuestDef Get(string id)
        {
            if (id == null) return null;
            if (id == Toni) return ToniChain;
            for (int i = 0; i < All.Length; i++) if (All[i].id == id) return All[i];
            return null;
        }

        /// Localized quest title by id (also for the Toni chain and unknown ids).
        public static string TitleOf(string id)
        {
            var d = Get(id);
            return d != null ? d.Title : id;
        }
    }
}
