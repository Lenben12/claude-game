namespace Sealebrities
{
    /// Story-flag keys owned by the npc+story module (shared GameState.storyFlags via StoryFlags – host authority).
    /// Core keys stay in StoryKeys; these are additional, never spelled inline elsewhere.
    public static class NpcKeys
    {
        // ---- tutorial beats (A.3): "horst_b0" … "horst_b9" played (Beat(n))
        public static string Beat(int n) => n switch
        {
            0 => "horst_b0", 1 => "horst_b1", 2 => "horst_b2", 3 => "horst_b3", 4 => "horst_b4",
            5 => "horst_b5", 6 => "horst_b6", 7 => "horst_b7", 8 => "horst_b8", _ => "horst_b9"
        };
        public const string HorstReactCatch = "horst_r_catch", HorstReactBoot = "horst_r_boot";
        /// Gus' arrival call (B.1 milestone 0, K.5) was heard.
        public const string GusCall0 = "gus_call0";
        public const string WandaMet = "wanda_met";
        /// The first chopper delivery arrived (W1 prerequisite).
        public const string FirstDelivery = "first_delivery";
        public const string FinMet = "fin_met";
        public const string ToniMet = "toni_met";
        public const string PearlMet = "pearl_met";
        // ---- Gus missions (B.2)
        /// M3 done: the lighthouse beam points to the trench at night (props/worldfx may read it).
        public const string LighthouseLit = "lighthouse_lit";
        /// M4 done: the Big Eye's shadow was filmed (finale hint).
        public const string EyeSeen = "eye_seen";
        public const string GusHammockClue = "gus_hammock_clue";
        // ---- rivals (B.3)
        public const string RivalR1Done = "rival_r1";
        public const string RivalR2Started = "rival_r2", RivalR2Done = "rival_r2_done";
        public const string RivalR3Started = "rival_r3", RivalR3Done = "rival_r3_done";
        public const string RivalR4Started = "rival_r4", RivalR4Rescued = "rival_r4_rescued", RivalR4Ignored = "rival_r4_ignored";
        /// Rivals appear as friendly guests in the finale chat (R4 rescued) – the livestream/finale owner may read it.
        public const string RivalsFriends = "rivals_friends";
        // ---- village (L.8) and side quests (L.7)
        public const string LanternSeen = "lantern_seen";
        public const string PearlLightStory = "pearl_light_story";
        /// T1 done: the house reef population +10 % (fauna may read it).
        public const string GhostNetCleared = "ghost_net_cleared";
        /// J1 done: the menu song „Altriff-Lied“ (K.6 music may read it).
        public const string OldreefSong = "oldreef_song";
        /// N2 done: the last 1987 tape (finale hint).
        public const string LastTape = "last_tape";
        /// W1 reward: free express deliveries left ("wanda_express_1", "wanda_express_2").
        public const string FreeExpress1 = "wanda_express_1", FreeExpress2 = "wanda_express_2";
        /// B1 outcome.
        public const string StickReturned = "stick_returned", StickKept = "stick_kept";
        /// T2 reward: depth bands marked in the Sealog ("depthhint:<speciesId>").
        public const string DepthHintPrefix = "depthhint:";
        /// L.6 clue heard ("clue:<secretKey>").
        public const string CluePrefix = "clue:";
    }
}
