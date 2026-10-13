using UnityEngine;

namespace Sealebrities
{
    /// How the voice of one player reaches another (F.5).
    public enum VoiceLink : byte
    {
        /// Both above water: 2–30 m, linear, spatial.
        Air,
        /// Speaker or listener under water without a radio link: 2–10 m, muffled (~900 Hz).
        Water,
        /// Underwater radio (Q70/Q71): up to 200 m, band-pass 400–3,000 Hz + light crackle, also to the boat.
        Radio
    }

    /// Co-op rules shared by the Net module and the simulating modules (no state, allocation free).
    ///
    /// F.4 "more players = more danger": the FAUNA module multiplies its predator count / aggression, jelly count and
    /// the bump air loss with these factors (on top of DifficultySettings.predatorAggression). Offline and solo every
    /// factor is exactly 1. Use DangerPlayers (players in the session) – not the divers that are currently in the water,
    /// so a friend on the boat still makes the sea a bit wilder for everybody.
    ///
    /// F.5 voice ranges: Link() decides per speaker/listener pair; NetVoice applies it.
    public static class NetCoopRules
    {
        // ------------------------------------------------------------------ F.4 danger table
        //                                        1      2      3      4 players
        static readonly float[] PredatorCount = { 1f, 1.25f, 1.5f, 1.75f };
        static readonly float[] PredatorAggro = { 1f, 1.1f, 1.2f, 1.3f };
        static readonly float[] JellyCount = { 1f, 1.2f, 1.4f, 1.6f };
        static readonly float[] BumpAir = { 1f, 1f, 1.1f, 1.2f };

        /// L.1: in co-op the open-world spawner keeps at most this many animals per diver (MaxActive 160 stays).
        public const int MaxCreaturesPerDiver = 60;

        /// Players that count for the danger table (1 offline / solo, 2–4 in a session).
        public static int DangerPlayers => Net.IsOnline ? Mathf.Clamp(Net.Players.Count, 1, Net.MaxPlayers) : 1;

        static int Index => DangerPlayers - 1;

        /// Predator spawn count factor (×1.0 / 1.25 / 1.5 / 1.75).
        public static float PredatorCountMultiplier => PredatorCount[Index];
        /// Predator aggression factor (×1.0 / 1.1 / 1.2 / 1.3) – multiply with DifficultySettings.predatorAggression.
        public static float PredatorAggressionMultiplier => PredatorAggro[Index];
        /// Jelly count factor (×1.0 / 1.2 / 1.4 / 1.6).
        public static float JellyCountMultiplier => JellyCount[Index];
        /// Air seconds lost per predator bump (×1.0 / 1.0 / 1.1 / 1.2) – apply once, where the bump is computed (host fauna).
        public static float BumpAirMultiplier => BumpAir[Index];

        /// Same factors for an explicit player count (tests, balancing tables).
        public static void DangerFor(int players, out float predatorCount, out float aggression, out float jellies, out float bumpAir)
        {
            int i = Mathf.Clamp(players, 1, Net.MaxPlayers) - 1;
            predatorCount = PredatorCount[i];
            aggression = PredatorAggro[i];
            jellies = JellyCount[i];
            bumpAir = BumpAir[i];
        }

        // ------------------------------------------------------------------ F.5 voice
        public const float AirMinDistance = 2f, AirMaxDistance = 30f;
        public const float WaterMinDistance = 2f, WaterMaxDistance = 10f;
        public const float RadioRange = 200f;
        public const float WaterMuffleHz = 900f;
        public const float RadioLowPassHz = 3000f, RadioHighPassHz = 400f;

        /// Voice link between a speaker and a listener. Above water both: Air. As soon as one of them is under water:
        /// Radio when every diver involved wears the underwater radio and every party above water is on the boat
        /// (the boat has the receiver), otherwise Water (2–10 m, muffled).
        public static VoiceLink Link(bool speakerUnder, bool speakerRadio, bool speakerOnBoat, bool listenerUnder, bool listenerRadio, bool listenerOnBoat)
        {
            if (!speakerUnder && !listenerUnder) return VoiceLink.Air;
            bool speakerOk = speakerUnder ? speakerRadio : speakerOnBoat;
            bool listenerOk = listenerUnder ? listenerRadio : listenerOnBoat;
            return speakerOk && listenerOk ? VoiceLink.Radio : VoiceLink.Water;
        }

        /// Maximum hearing distance of a link (m).
        public static float MaxDistance(VoiceLink link) =>
            link == VoiceLink.Radio ? RadioRange : link == VoiceLink.Water ? WaterMaxDistance : AirMaxDistance;

        public static float MinDistance(VoiceLink link) => link == VoiceLink.Water ? WaterMinDistance : AirMinDistance;

        // ------------------------------------------------------------------ sleep / NPC
        /// NPC lines (Horst …) are heard within this distance of the NPC (A.2); radio lines everywhere.
        public const float NpcHearRange = 25f;
    }
}
