using UnityEngine;

namespace Sealebrities
{
    /// Step kinds of a side quest (L.7). Progress lives in QuestState (step index + progress counter / bit mask).
    public enum SqStep : byte
    {
        /// Talk to the giver (hand-in) – also the last step of most quests.
        Talk,
        /// Carry quest items (Carry, "q:<item>") to the giver – or to `spot` when set (W1 drums → hangar).
        Bring,
        /// Collect quest objects of a group with E (bit mask of the object index).
        Collect,
        /// Hold E for `a` seconds on each object of a group (T1 ghost net).
        Hold,
        /// A photo matching `target` (species id or a tag – see NpcDirector.HostPhoto); a csv list with count n = any n
        /// of them (landmarks, curiosities).
        Photo,
        /// A clip of `target` species (any of a csv list – bit mask when count > 1).
        Film,
        /// Catch a fish ≥ a kg (target = kind id or null for any), b = 1 → from the boat.
        Catch,
        /// Upload a video: target "honest" / "shark" / species id.
        Upload,
        /// Selfie with an NPC (target = npc id) or an animal (target = species id) in frame.
        Selfie,
        /// Release a lantern on Lantern Evening (the hermits' ritual at the old jetty).
        Lantern,
        /// Bring an NPC (target) to a spot after hour a – unused since round 3 (kept for old saves).
        Escort,
        /// Record `a` seconds with the camera while the condition `target` holds (night reef, canoe race, Horst asleep,
        /// the camp goats, Kokos, the sunset ritual, the delivery chopper).
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
        /// needVillage: unused since round 3 (the village is gone); hermit givers need the camp found + a friendly mood.
        public bool needVillage, needTutorial;
        public SqStepDef[] steps;
        public long money;
        public string cosmetic, flag;
        /// Short extra reward chip ("+2 Express", "Rezept: Curry") shown with the done notification.
        public string chipDe, chipEn;
        /// Round 3: a funny filming job (GDD §20 Q11) – the Tasks app marks it with a camera.
        public bool filming;
        /// Dialogue prefixes (start / end lines).
        public string StartSeq => "sq." + id + ".start";
        public string EndSeq => "sq." + id + ".end";
        public string Title => Loc.Tr(titleDe, titleEn);
        public string Chip => chipDe != null ? Loc.Tr(chipDe, chipEn) : null;
    }

    /// The side quests (L.7, round 3: the village Altriff is gone – Pearl's, Mira's, Tomas', Ada's, Juno's and Lio's jobs
    /// moved to Horst, Wanda, Dr. Fin, Toni and the hermits; GDD §20 Q11 adds funny filming jobs) + Toni's chain.
    /// Quest ids stay (saves, cosmetics rules quest:P2/L2/M2/M3/B1, FoodDatabase.unlockQuest A1/A2).
    public static class SideQuestDatabase
    {
        public const string Toni = "toni";

        static SqStepDef St(SqStep k, string target, int count, string de, string en, string spot = null, float a = 0f, float b = 0f) =>
            new SqStepDef { kind = k, target = target, count = count, de = de, en = en, spot = spot, a = a, b = b };

        static SqStepDef Back(string giverDe, string giverEn) => St(SqStep.Talk, null, 1, "Zurück zu " + giverDe, "Back to " + giverEn);

        /// All curiosities of the Sealog page „Kuriositäten“ (H5: photograph any two).
        const string Memes = "meme_shades,meme_cart,meme_crabrave,meme_dance,meme_gnome,meme_throne,meme_gamingpc,meme_stonks,meme_ducks,meme_dino";

        public static readonly SideQuestDef[] All =
        {
            // ---------------------------------------------------------------- Horst (his beach hammock)
            new SideQuestDef { id = "H1", giver = NpcDatabase.Horst, titleDe = "Günthers Raubzug", titleEn = "Günther's Heist", needTutorial = true, money = 80, filming = true,
                steps = new[] { St(SqStep.Photo, "guenther", 1, "Foto: Günther mit Beute, 12–14 Uhr", "Photo: Günther with loot, 12–2 pm", "horst_corner", 12f, 14f), Back("Horst", "Horst") } },
            new SideQuestDef { id = "H2", giver = NpcDatabase.Horst, titleDe = "Horsts Sonnenbrille", titleEn = "Horst's Sunglasses", needTutorial = true, money = 50,
                steps = new[] { St(SqStep.Bring, "sunglasses", 1, "Sonnenbrille am Steg im Wasser", "Sunglasses in the water by the dock", "q_sunglasses") } },
            new SideQuestDef { id = "H3", giver = NpcDatabase.Horst, titleDe = "Influencer-Workshop", titleEn = "Influencer Workshop", needTutorial = true, minDay = 5, money = 120, filming = true,
                steps = new[] { St(SqStep.Selfie, NpcDatabase.Horst, 1, "Selfie mit Horst {K:Selfie}", "Selfie with Horst {K:Selfie}"), St(SqStep.Upload, "honest", 1, "Video mit ehrlichem Titel", "Video with an honest title"), Back("Horst", "Horst") } },
            new SideQuestDef { id = "H4", giver = NpcDatabase.Horst, titleDe = "ASMR mit Horst", titleEn = "ASMR with Horst", needTutorial = true, minDay = 3, money = 110, filming = true,
                steps = new[] { St(SqStep.Record, "npc_sleep:" + NpcDatabase.Horst, 1, "Horst schlafend filmen (10 s)", "Film Horst asleep (10 s)", "horst_hammock", 10f), Back("Horst", "Horst") } },
            new SideQuestDef { id = "H5", giver = NpcDatabase.Horst, titleDe = "Meme-Jagd", titleEn = "Meme Hunt", needTutorial = true, minDay = 2, money = 140, filming = true,
                steps = new[] { St(SqStep.Photo, Memes, 2, "2 Kuriositäten im Meer fotografieren", "Photograph 2 curiosities in the sea", "house_reef"), Back("Horst", "Horst") } },
            new SideQuestDef { id = "T3", giver = NpcDatabase.Horst, titleDe = "Der große Fang", titleEn = "The Big Catch", needTutorial = true, needItem = "rod_t1", money = 150,
                steps = new[] { St(SqStep.Catch, null, 1, "Fisch über 3 kg vom Boot", "Fish over 3 kg from the boat", null, 3f, 1f), Back("Horst", "Horst") } },
            new SideQuestDef { id = "L1", giver = NpcDatabase.Horst, titleDe = "Horsts Drachen", titleEn = "Horst's Kite", needTutorial = true, minDay = 2, money = 60,
                steps = new[] { St(SqStep.Bring, "kite", 1, "Drachen oben am Leuchtturm holen", "Get the kite from the lighthouse top", "q_kite") } },
            new SideQuestDef { id = "L3", giver = NpcDatabase.Horst, titleDe = "Horsts Schatzkarte", titleEn = "Horst's Treasure Map", needTutorial = true, minDay = 6, money = 200,
                chipDe = "Horsts Notgroschen", chipEn = "Horst's savings",
                steps = new[] { St(SqStep.Collect, "liochest", 1, "Dem Buntstift-X folgen", "Follow the crayon X", "q_liochest"), Back("Horst", "Horst") } },
            new SideQuestDef { id = "M2", giver = NpcDatabase.Horst, titleDe = "Gus' altes Paddel", titleEn = "Gus's Old Paddle", needTutorial = true, minDay = 4, money = 200, cosmetic = "boatflag:1",
                steps = new[] { St(SqStep.Bring, "paddle", 1, "Paddel aus der Buchthöhle holen", "Fetch the paddle from the cove cave", "q_paddle") } },
            // ---------------------------------------------------------------- Wanda (hangar)
            new SideQuestDef { id = "W1", giver = NpcDatabase.Wanda, titleDe = "Treibende Fässer", titleEn = "Drifting Drums", needFlag = NpcKeys.FirstDelivery, money = 150,
                chipDe = "+2 Express", chipEn = "+2 express",
                steps = new[] { St(SqStep.Bring, "drum", 3, "Fässer zum Hangar tragen", "Carry the drums to the hangar", "hangar"), Back("Wanda", "Wanda") } },
            new SideQuestDef { id = "W2", giver = NpcDatabase.Wanda, titleDe = "Prospekt-Fotos", titleEn = "Brochure Photos", minDay = 4, money = 120, filming = true,
                steps = new[] { St(SqStep.Photo, "lm_lighthouse,lm_hut,lm_helipad", 3, "Vom Wasser: Leuchtturm, Hütte, Landeplatz", "From the water: lighthouse, hut, helipad"), Back("Wanda", "Wanda") } },
            new SideQuestDef { id = "L2", giver = NpcDatabase.Wanda, titleDe = "Ein echter Hai!", titleEn = "A Real Shark!", minFollowers = 300, cosmetic = "hat:14", money = 100,
                steps = new[] { St(SqStep.Upload, "shark", 1, "Video mit Hai hochladen", "Upload a video with a shark"), Back("Wanda", "Wanda") } },
            new SideQuestDef { id = "W3", giver = NpcDatabase.Wanda, titleDe = "Heli-Stunt", titleEn = "Chopper Stunt", needFlag = NpcKeys.FirstDelivery, minDay = 3, money = 130, filming = true,
                steps = new[] { St(SqStep.Record, "chopper", 1, "Den Liefer-Heli filmen (6 s)", "Film the delivery chopper (6 s)", "helipad", 6f), Back("Wanda", "Wanda") } },
            // ---------------------------------------------------------------- Dr. Fin (visit days – her jobs finish on the spot)
            new SideQuestDef { id = "T1", giver = NpcDatabase.Fin, titleDe = "Geisternetz", titleEn = "Ghost Net", minDay = 2, money = 200, flag = NpcKeys.GhostNetCleared,
                chipDe = "Riff +10 % Tiere", chipEn = "Reef +10 % animals",
                steps = new[] { St(SqStep.Hold, "ghostnet", 3, "Geisternetz lösen ({K:Interact} halten)", "Cut the ghost net (hold {K:Interact})", "house_reef", 5f) } },
            new SideQuestDef { id = "T2", giver = NpcDatabase.Fin, titleDe = "Wo wohnt der Zackenbarsch?", titleEn = "Where Does the Grouper Live?", needSite = "wreck", money = 250,
                chipDe = "3 Tiefen im Sealog", chipEn = "3 Sealog depths",
                steps = new[] { St(SqStep.Photo, "grouper", 1, "Foto: Zackenbarsch unter 25 m", "Photo: grouper below 25 m", "wreck", 25f) } },
            new SideQuestDef { id = "J1", giver = NpcDatabase.Fin, titleDe = "Walgesang", titleEn = "Whale Song", needSite = "blue_drop", money = 400, flag = NpcKeys.OldreefSong, filming = true,
                chipDe = "Menü-Lied", chipEn = "Menu song",
                steps = new[] { St(SqStep.Film, "humpback", 1, "Big Bertha filmen", "Film Big Bertha", "blue_drop") } },
            new SideQuestDef { id = "F1", giver = NpcDatabase.Fin, titleDe = "Schildkröten-Selfie", titleEn = "Turtle Selfie", money = 150, filming = true,
                steps = new[] { St(SqStep.Selfie, "green_turtle", 1, "Selfie mit Gerald {K:Selfie}", "Selfie with Gerald {K:Selfie}", "house_reef") } },
            // ---------------------------------------------------------------- Toni (after his map)
            new SideQuestDef { id = "N1", giver = NpcDatabase.Toni, titleDe = "Tonaufnahmen", titleEn = "Sound Recordings", needFlag = StoryKeys.FogMap, money = 300, filming = true,
                steps = new[] { St(SqStep.Film, "dolphin,sea_lion,humpback", 3, "Delfin, Seelöwe, Wal filmen", "Film dolphin, sea lion, whale"), Back("Toni", "Tony") } },
            new SideQuestDef { id = "N2", giver = NpcDatabase.Toni, titleDe = "Das letzte Band", titleEn = "The Last Tape", needFlag = StoryKeys.FogMap, needSite = "deep_trench", money = 2000,
                flag = NpcKeys.LastTape,
                steps = new[] { St(SqStep.Bring, "tape", 1, "Das Band am Antennenmast (92 m)", "The tape at the antenna mast (92 m)", "q_tape") } },
            new SideQuestDef { id = "J2", giver = NpcDatabase.Toni, titleDe = "Knallkrebs-Konzert", titleEn = "Snapping Shrimp Concert", needFlag = StoryKeys.FogMap, money = 160, filming = true,
                steps = new[] { St(SqStep.Record, "night_reef", 1, "Nachts am Hausriff 20 s filmen", "Record 20 s at the house reef at night", "house_reef", 20f), Back("Toni", "Tony") } },
            // ---------------------------------------------------------------- Chad & Brad
            new SideQuestDef { id = "B1", giver = NpcDatabase.Brad, titleDe = "Der goldene Selfie-Stick", titleEn = "The Golden Selfie Stick", needFlag = NpcKeys.RivalR1Done, money = 0,
                steps = new[] { St(SqStep.Bring, "selfie_stick", 1, "Selfie-Stick im Hausriff finden", "Find the selfie stick in the house reef", "q_selfie_stick"),
                                St(SqStep.Choice, null, 1, "Zurückgeben oder behalten?", "Give it back or keep it?") } },
            // ---------------------------------------------------------------- the hermits (camp found + the giver friendly)
            new SideQuestDef { id = "M1", giver = NpcDatabase.HSmith, titleDe = "Holz für die Schleifbank", titleEn = "Wood for the Grinding Bench", money = 120,
                flag = StoryKeys.GrindLearned, chipDe = "Lektion: Speere schärfen", chipEn = "Lesson: sharpening spears",
                steps = new[] { St(SqStep.Collect, "driftwood", 5, "Treibholz an den Stränden sammeln", "Collect driftwood on the beaches"), Back("Amboss", "Anvil") } },
            new SideQuestDef { id = "A1", giver = NpcDatabase.HCook, titleDe = "Kokos-Curry", titleEn = "Coconut Curry", money = 80,
                flag = NpcKeys.RecipeCurry, chipDe = "Rezept: Kokos-Curry", chipEn = "Recipe: coconut curry",
                steps = new[] { St(SqStep.Bring, "coconut", 3, "3 Kokosnüsse (+2 Makrelen in der Kühlbox)", "3 coconuts (+2 mackerel in the cooler)") } },
            new SideQuestDef { id = "A2", giver = NpcDatabase.HGardener, titleDe = "Seetrauben", titleEn = "Sea Grapes", minDay = 2, money = 80,
                flag = NpcKeys.HarvestLearned, chipDe = "Obst jeden Morgen", chipEn = "Fruit every morning",
                steps = new[] { St(SqStep.Collect, "seagrape", 6, "Seetrauben im Seegras pflücken", "Pick sea grapes in the sea grass"), Back("Wurzel", "Root") } },
            new SideQuestDef { id = "P2", giver = NpcDatabase.HGardener, titleDe = "Ziegen-Yoga", titleEn = "Goat Yoga", money = 120, cosmetic = "hat:13", filming = true,
                steps = new[] { St(SqStep.Record, "goats", 1, "Die Lager-Ziegen filmen (8 s)", "Film the camp goats (8 s)", "hermit_goats", 8f), Back("Wurzel", "Root") } },
            new SideQuestDef { id = "C1", giver = NpcDatabase.HCook, titleDe = "Kokos' Porträt", titleEn = "Kokos' Portrait", money = 90, filming = true,
                steps = new[] { St(SqStep.Record, "kokos", 1, "Kokos filmen (6 s)", "Film Kokos (6 s)", "camp_map_piece", 6f), Back("Topf", "Pot") } },
            new SideQuestDef { id = "M3", giver = NpcDatabase.HHunter, titleDe = "Das Kanu-Rennen", titleEn = "The Canoe Race", money = 300, cosmetic = "boatflag:2", filming = true,
                flag = NpcKeys.TrapLearned, chipDe = "Fischfalle", chipEn = "Fish trap",
                steps = new[] { St(SqStep.Wait, null, 1, "Morgen um 10 Uhr am alten Steg", "Tomorrow 10 am at the old jetty", "v_jetty", 10f),
                                St(SqStep.Record, "canoe", 1, "Das Kanu-Rennen filmen (8 s)", "Film the canoe race (8 s)", "v_jetty", 8f), Back("Harpo", "Harpo") } },
            new SideQuestDef { id = "P1", giver = NpcDatabase.HLeader, titleDe = "Die Gezeitenpflöcke", titleEn = "The Tide Pegs", money = 200,
                flag = StoryKeys.NightOfTheEye,
                steps = new[] { St(SqStep.Collect, "peg", 3, "Gezeitenpflöcke finden", "Find the tide pegs"), Back("Ebbe", "Ebb") } },
            new SideQuestDef { id = "P3", giver = NpcDatabase.HLeader, titleDe = "Lichter auf dem Wasser", titleEn = "Lights on the Water", needQuest = "P1", money = 150, filming = true,
                flag = NpcKeys.PearlLightStory,
                steps = new[] { St(SqStep.Lantern, null, 1, "Am alten Steg eine Laterne loslassen", "Release a lantern at the old jetty", "v_lanterns"),
                                St(SqStep.Photo, "lanterns", 1, "Foto vom Wasser: 5 Laternen", "Photo from the water: 5 lanterns", "v_lanterns"), Back("Ebbe", "Ebb") } },
            new SideQuestDef { id = "C2", giver = NpcDatabase.HLeader, titleDe = "Der Gruß ans Meer", titleEn = "The Greeting to the Sea", needQuest = "P1", money = 250, filming = true,
                steps = new[] { St(SqStep.Record, "ritual", 1, "Das Abendritual am Schrein filmen (8 s)", "Film the evening ritual at the shrine (8 s)", "hermit_shrine", 8f), Back("Ebbe", "Ebb") } },
            new SideQuestDef { id = "S1", giver = NpcDatabase.HLookout, titleDe = "Beweis für den Schrein", titleEn = "Proof for the Shrine", needSite = "wreck", money = 600, filming = true,
                steps = new[] { St(SqStep.Film, "mosasaurus", 1, "Big Mo am Vulkan filmen", "Film Big Mo at the volcano", "volcano_abyss"), Back("Fernglas", "Spyglass") } },
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

        /// The giver is one of the hermits (offered once the camp was found and he is friendly).
        public static bool HermitGiver(SideQuestDef d) => d != null && NpcDatabase.IsHermit(d.giver);
    }
}
