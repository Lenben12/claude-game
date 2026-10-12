using System;
using UnityEngine;

namespace Sealebrities
{
    /// Story flag keys shared by every module (npc+story sets most of them; core sets the automatic ones).
    /// Flags live in GameState.storyFlags (shared, host authority). Add new keys here – never spell them inline.
    public static class StoryKeys
    {
        // ---- K.5 prologue / tutorial
        /// The pre-game NPC talk (Wanda + Horst + Gus' letter) is over; new games start at WorldLayout.NewGameSpawn until then.
        public const string PrologueDone = "prologue_done";
        public const string GusLetterRead = "gus_letter";
        /// Tutorial finished (Horst's lure hat, A.3) – set by the npc module.
        public const string TutorialDone = "tutorial_done";
        // ---- K.8 Toni + Fog Isle
        /// Set automatically by core on the first morning with day ≥ 10 and ≥ 2,500 followers (fallback day 16).
        public const string ToniArrived = "toni_arrived";
        public const string ToniT1 = "toni_t1", ToniT2 = "toni_t2", ToniT3 = "toni_t3", ToniT4 = "toni_t4";
        /// Toni's step T4 runs – the Chopper Shop lists the radio battery (category „Teile“).
        public const string ToniT4Active = "toni_t4_active";
        /// Toni's map: the Fog Isle appears on the sea chart, the buoy lane clears the fog, its species spawn.
        public const string FogMap = "fog_map";
        // ---- L.8 village
        public const string VillageVisited = "village_visited";
        public const string NightOfTheEye = "pearl_night_of_eye";
        // ---- B / D rewards that change prices
        /// Dr. Fin R1 done: the carbon rod costs half (D.3).
        public const string RodT1Discount = "rod_t1_discount";
        /// Gus mission M2 done: the underwater radio costs half (E.5).
        public const string RadioDiscount = "uw_radio_discount";
        // ---- misc
        public const string HorstOverboardSeen = "horst_overboard";
        public const string WandaThirdDelivery = "wanda_story_1987";
        public const string FinaleDone = "finale_done";
        // ---- round 3 (GDD §19)
        /// The hermits taught the player to sharpen spears at the grinder (npc module sets it in the hermit camp).
        public const string GrindLearned = "hermit_grind";
        /// All map pieces found: the treasure map is complete (MapPieces sets it on the host / finder).
        public const string TreasureMap = "treasure_map";
        /// The secret the treasure map points to was found (world module sets it at MapPieces.SecretSpot).
        public const string TreasureFound = "treasure_found";
        /// Someone of the crew stepped into the hidden hermit camp (world module, PropCamp) – the phone map shows it from now on.
        public const string HermitCampFound = "hermit_camp_found";
        /// Someone set foot on the volcano island (world module) – nice hook for the fauna / story steps.
        public const string VolcanoLanded = "volcano_landed";
    }

    /// Shared story flags (J.3). Set → host authority (clients forward CoreCommands.StoryFlagKey and apply optimistically).
    public static class StoryFlags
    {
        static GameState S => GameManager.I != null ? GameManager.I.State : null;

        public static bool Has(string flag)
        {
            var s = S;
            return s != null && !string.IsNullOrEmpty(flag) && s.storyFlags.Contains(flag);
        }

        /// Sets a flag once (no-op when already set). Raises GameEvents.StoryFlagSet.
        public static void Set(string flag)
        {
            var s = S;
            if (s == null || string.IsNullOrEmpty(flag) || s.storyFlags.Contains(flag)) return;
            if (Net.IsClient) Net.Command(NetCommand.Custom, CoreCommands.StoryFlagKey, flag);
            s.storyFlags.Add(flag);
            GameEvents.RaiseStoryFlagSet(flag);
            if (flag == StoryKeys.PrologueDone) s.prologueDone = true;
            if (Net.HasAuthority)
            {
                if (flag == StoryKeys.FogMap) Progression.CheckUnlocks();
                Cosmetics.CheckRules();
            }
        }

        /// Removes a flag (authority only – e.g. ToniT4Active when the step ends).
        public static void Clear(string flag)
        {
            var s = S;
            if (s == null || string.IsNullOrEmpty(flag) || !s.storyFlags.Contains(flag)) return;
            if (Net.IsClient) { Net.Command(NetCommand.Custom, CoreCommands.StoryFlagKey, "-" + flag); }
            s.storyFlags.Remove(flag);
        }
    }

    /// Automatic story triggers evaluated by the host at the start of every day (K.8 and friends).
    public static class StoryRules
    {
        /// K.8: Toni washes up on the first morning with day ≥ 10 and ≥ 2,500 followers – at the latest on day 16.
        public const int ToniMinDay = 10, ToniFallbackDay = 16;
        public const long ToniMinFollowers = 2500;
        /// L.8: Lantern Evening every 7th day (day % 7 == 4), 19:00–22:00.
        public const int LanternEveryDays = 7, LanternDayRemainder = 4;
        public const float LanternStart = 19f / 24f, LanternEnd = 22f / 24f;

        public static bool IsLanternDay(int day) => day % LanternEveryDays == LanternDayRemainder;

        public static bool IsLanternEvening(int day, float timeOfDay) => IsLanternDay(day) && timeOfDay >= LanternStart && timeOfDay < LanternEnd;

        /// Host, StartNewDay.
        public static void OnNewDay(GameState s)
        {
            if (s == null || !Net.HasAuthority) return;
            if (!StoryFlags.Has(StoryKeys.ToniArrived)
                && ((s.day >= ToniMinDay && s.followers >= ToniMinFollowers) || s.day >= ToniFallbackDay))
            {
                StoryFlags.Set(StoryKeys.ToniArrived);
                Debug.Log("[Story] Toni washed up on the driftwood beach (day " + s.day + ")");
            }
        }
    }
}
