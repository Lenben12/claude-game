using UnityEngine;

namespace Sealebrities
{
    /// KRILL.tv balance and rules (Design_Runde2 C.1–C.5). Economy factors live in Core/Progression (core is the
    /// constants owner for StreamMoneyFactor / StreamFollowerFactor / StreamFaintLoss); everything stream-specific is here.
    public static class StreamRules
    {
        // ---------------------------------------------------------------- C.1 rules
        /// Unlock, dock start and the signal ranges are core constants (LiveState).
        public static long UnlockFollowers => LiveState.UnlockFollowers;
        public static long DockFollowers => LiveState.DockStreamFollowers;
        public static float MaxSeconds => LiveState.MaxStreamSeconds;

        /// "Kein Signal!": viewers drain 4 %/s, after 20 s the stream ends normally.
        public const float NoSignalDrainPerSecond = 0.04f;
        public const float NoSignalEndSeconds = 20f;
        /// How fast the viewers come back once the signal returns (share per second).
        public const float SignalRecoverPerSecond = 0.25f;

        /// Signal range around the boat (150 m, 400 m from 2,500 followers – the relay buoy, which also covers the dock).
        public static float SignalRange(long followers) => followers >= DockFollowers ? LiveState.SignalRangeRelay : LiveState.SignalRange;

        // ---------------------------------------------------------------- C.2 viewers and hype
        public const float HypeMin = 0.3f, HypeMax = 3.0f, HypeStart = 1.0f;
        /// Hype moves toward its target with this time constant (s).
        public const float HypeTau = 12f;
        /// Nothing on camera for this long → "boring" target 0.6.
        public const float BoredAfterSeconds = 15f;
        public const float HypeBoring = 0.6f, HypeAnimal = 1.0f, HypeAnimalCap = 2.2f, HypePerValue = 0.004f;
        public const float DangerMultiplier = 1.4f, DareMultiplier = 1.3f;
        /// Danger counts within this distance (and on camera).
        public const float DangerDistance = 5f;
        /// Multi-cam (C.8): +0.1 target per extra camera showing an animal, cap +0.3.
        public const float MultiCamBonus = 0.1f, MultiCamCap = 0.3f;

        // spikes (added instantly)
        public const float SpikeNewSpecies = 0.8f, SpikeRecord = 1.0f, SpikeBump = 0.5f, SpikeLegendary = 0.4f,
            SpikeDareDone = 1.0f, SpikeDareFailed = -0.3f, SpikeDareExpired = -0.1f;

        /// baseViewers(F) = 10 + 0.55 × F^0.75 (1k → 108, 10k → 560, 100k → 3,100, 1M → 17,400).
        public static float BaseViewers(long followers) => 10f + 0.55f * Mathf.Pow(Mathf.Max(0, followers), 0.75f);

        /// Target hype of the best camera: 1.0 + 0.004 × baseValue × tierMult (cap 2.2).
        public static float AnimalHype(SpeciesDef s, SizeTier tier)
        {
            if (s == null) return HypeAnimal;
            return Mathf.Min(HypeAnimalCap, HypeAnimal + HypePerValue * s.baseValue * Progression.SizeMultiplier(tier));
        }

        /// Viewer noise: 0.9 + 0.2 × smooth noise over 8 s.
        public static float ViewerNoise(float seconds, float seed) => 0.9f + 0.2f * Mathf.PerlinNoise(seconds / 8f, seed);

        // ---------------------------------------------------------------- C.3 money and followers
        /// Average hype the econsim used for the factors (money/followers are scaled by hype / 1.3).
        public const float AverageHype = 1.3f;
        /// Share of the stream money paid as ticking ad revenue; the rest arrives as donations in 1–8 % chunks.
        public const float AdShare = 0.6f;
        public const float DonationChunkMin = 0.01f, DonationChunkMax = 0.08f;
        /// Money is paid in batches (host AddMoney once per this many seconds).
        public const float PayoutSeconds = 5f;

        /// Session multiplier: co-op upload factor × difficulty income (C.3).
        public static float SessionMultiplier()
        {
            var gm = GameManager.I;
            float inc = gm != null ? gm.Difficulty.incomeMultiplier : 1f;
            return Progression.CoopUploadFactor(Net.PlayerCount) * inc;
        }

        /// Money one full 10-minute stream at average hype brings (reference for donation chunks and the app).
        public static float ExpectedStreamMoney(long followers) =>
            Progression.StreamMoneyFactor * Progression.ExpectedUploadMoney(followers) * SessionMultiplier();

        /// $/s at this hype.
        public static float MoneyPerSecond(long followers, float hype) =>
            ExpectedStreamMoney(followers) / MaxSeconds * hype / AverageHype;

        /// Pending followers per second at this hype (credited only when the stream ends normally).
        public static float FollowersPerSecond(long followers, float hype) =>
            Progression.StreamFollowerFactor * Progression.ExpectedUploadFollowers(followers) * SessionMultiplier() / MaxSeconds * hype / AverageHype;

        // ---------------------------------------------------------------- C.5 dares
        /// A dare is offered every 75–120 s when hype ≥ 0.8 and none runs; max 3 per stream; 8 s to accept.
        public const float DareIntervalMin = 75f, DareIntervalMax = 120f, DareFirstMin = 75f, DareFirstMax = 120f;
        public const float DareMinHype = 0.8f;
        public const int MaxDares = 3;
        public const float DareAcceptSeconds = 8f;
        /// Result cards stay this long.
        public const float DareResultSeconds = 4f;
        /// Followers bonus on success: + this share of the stream's pending followers so far.
        public const float DareFollowerBonus = 0.5f;
        /// "Poke the shark": the shark turns aggressive – bump chance in the next 10 s.
        public const float SharkBumpChance = 0.7f, SharkBumpWindow = 10f;

        /// Dare money per stream is capped at this × ExpectedUploadMoney(F) (difficulty income applies): 3 lucky high-risk
        /// dares (k up to 1.8 each) would otherwise out-earn several days of uploads – the economy sim does not model them.
        public const float DareMoneyCapFactor = 1.5f;

        public static long DareMoneyCap(long followers)
        {
            var gm = GameManager.I;
            float inc = gm != null ? gm.Difficulty.incomeMultiplier : 1f;
            return (long)Mathf.Max(5f, Mathf.Round(DareMoneyCapFactor * Progression.ExpectedUploadMoney(followers) * inc));
        }

        /// Dare reward = k × ExpectedUploadMoney(F) (difficulty income applies, like every stream income).
        public static long DareReward(float k, long followers)
        {
            var gm = GameManager.I;
            float inc = gm != null ? gm.Difficulty.incomeMultiplier : 1f;
            return (long)Mathf.Max(5f, Mathf.Round(k * Progression.ExpectedUploadMoney(followers) * inc));
        }

        // ---------------------------------------------------------------- C.6 chat
        /// One message every clamp(4.0 − 0.35 × hype − 0.25 × log10(viewers), 1.2, 4.0) s.
        public static float ChatInterval(float hype, int viewers) =>
            Mathf.Clamp(4.0f - 0.35f * hype - 0.25f * Mathf.Log10(Mathf.Max(1, viewers)), 1.2f, 4.0f);
        /// Max visible bubbles, seconds each, no repeat within this many seconds.
        public const int ChatVisible = 4;
        public const float ChatSeconds = 6f, ChatNoRepeatSeconds = 60f;

        // ---------------------------------------------------------------- detection (K.2 rules, own lightweight scan)
        /// Detect range = clamp(visibility × 0.9, 4, 25) m.
        public static float DetectRange()
        {
            float vis = FXVisibility.Meters;
            if (float.IsNaN(vis) || vis <= 0f) vis = 20f;
            return Mathf.Clamp(vis * 0.9f, 4f, 25f);
        }
        /// Projected size ≥ 2.5 % of the screen height (1.5 % for animals ≤ 12 cm); 5 % viewport margin.
        public const float MinCover = 0.025f, MinCoverSmall = 0.015f, ViewportMargin = 0.05f;
        /// Dare availability looks this far around the diver.
        public const float NearRange = 25f;
    }
}
