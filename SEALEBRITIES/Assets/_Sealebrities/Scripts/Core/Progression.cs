using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    public class Milestone
    {
        public string id;
        public long followers;
        public string titleDe, titleEn, descDe, descEn;
        public string Title => Loc.Tr(titleDe, titleEn);
        public string Description => Loc.Tr(descDe, descEn);
    }

    /// Balancing: sizes, clip values, uploads, followers, money, milestones and unlocks.
    public static class Progression
    {
        // ------------------------------------------------------------------ sizes
        public static readonly float[] TierMultiplier = { 0.5f, 1f, 1.6f, 2.5f, 4f };
        static readonly float[] TierChance = { 0.25f, 0.50f, 0.17f, 0.06f, 0.02f };
        // Fraction bands inside a species' [minCm, maxCm] range; Legendary may exceed max by 10 %.
        static readonly float[] TierBandMin = { 0.00f, 0.20f, 0.60f, 0.80f, 0.95f };
        static readonly float[] TierBandMax = { 0.20f, 0.60f, 0.80f, 0.95f, 1.10f };

        public static float SizeMultiplier(SizeTier t) => TierMultiplier[(int)t];

        public static SizeTier RollTier(System.Random rng)
        {
            double r = rng.NextDouble(), acc = 0;
            for (int i = 0; i < TierChance.Length; i++) { acc += TierChance[i]; if (r <= acc) return (SizeTier)i; }
            return SizeTier.Normal;
        }

        public static float RollSizeCm(SpeciesDef s, SizeTier t, System.Random rng)
        {
            float f = Mathf.Lerp(TierBandMin[(int)t], TierBandMax[(int)t], (float)rng.NextDouble());
            return s.minCm + (s.maxCm - s.minCm) * f;
        }

        public static SizeTier ClassifySize(SpeciesDef s, float cm)
        {
            float f = (cm - s.minCm) / Mathf.Max(0.01f, s.maxCm - s.minCm);
            for (int i = TierBandMin.Length - 1; i >= 0; i--) if (f >= TierBandMin[i]) return (SizeTier)i;
            return SizeTier.Tiny;
        }

        public static Color TierColor(SizeTier t)
        {
            switch (t)
            {
                case SizeTier.Tiny: return new Color(0.62f, 0.72f, 0.78f);
                case SizeTier.Normal: return new Color(0.92f, 0.96f, 0.97f);
                case SizeTier.Large: return new Color(0.24f, 0.86f, 0.52f);
                case SizeTier.Huge: return new Color(0.62f, 0.42f, 1f);
                default: return new Color(1f, 0.76f, 0.2f);
            }
        }

        // ------------------------------------------------------------------ clip value
        /// Value of one clip = species base value x fixed size multiplier x clip quality (x small record/action bonus).
        public static float ClipValue(FootageClip c)
        {
            var s = SpeciesDatabase.Get(c.speciesId);
            if (s == null) return 0f;
            float q = Mathf.Clamp(c.quality, 0.1f, 1.6f);
            float action = 1f + Mathf.Clamp01(c.action) * 0.4f;
            float record = c.isRecord ? 1.5f : 1f;
            return s.baseValue * SizeMultiplier(c.Tier) * q * action * record;
        }

        // ------------------------------------------------------------------ uploads
        public const float ViewsPerValue = 45f;
        public const float FollowerConversion = 0.03f;
        public const float MoneyPerThousandViews = 8f;
        public const long MonetizationFollowers = 100;

        public static float Reach(long followers) => 1f + Mathf.Pow(followers / 400f, 0.85f);

        public static float TitleViewMultiplier(TitleStyle t) => t == TitleStyle.Honest ? 1f : t == TitleStyle.Spicy ? 1.3f : 1.8f;
        public static float TitleConversionMultiplier(TitleStyle t) => t == TitleStyle.Honest ? 1f : t == TitleStyle.Spicy ? 0.95f : 0.6f;

        /// Best clip per species from the banked footage, sorted by value (max 12).
        public static List<FootageClip> SelectBestClips(List<FootageClip> source)
        {
            var best = new Dictionary<string, FootageClip>();
            foreach (var c in source)
                if (!best.TryGetValue(c.speciesId, out var b) || c.Value > b.Value) best[c.speciesId] = c;
            var list = new List<FootageClip>(best.Values);
            list.Sort((a, b) => b.Value.CompareTo(a.Value));
            if (list.Count > 12) list.RemoveRange(12, list.Count - 12);
            return list;
        }

        static float NoveltyFactor(FootageClip c, int day)
        {
            var e = Journal.Get(c.speciesId);
            return day - e.lastUploadDay <= 2 ? 0.5f : 1f;
        }

        public static string SuggestTitle(List<FootageClip> clips, TitleStyle style)
        {
            if (clips.Count == 0) return "";
            var top = clips[0].Species;
            string name = top != null ? top.Name : "?";
            switch (style)
            {
                case TitleStyle.Honest: return Loc.Tr($"Tauchgang am {GameManager.I.State.day}. Tag: {name} und mehr", $"Dive day {GameManager.I.State.day}: {name} and more");
                case TitleStyle.Spicy: return Loc.Tr($"Ich war SO nah an einem {name}!", $"I got SO close to a {name}!");
                default: return Loc.Tr($"{name.ToUpper()} HAT MICH FAST GEFRESSEN (KEIN CLICKBAIT)", $"{name.ToUpper()} ALMOST ATE ME (NOT CLICKBAIT)");
            }
        }

        /// Estimate of views/followers/money for the current banked footage. Used by the FinTube app before uploading.
        public static (long viewsMin, long viewsMax, long followers, long money) Preview(TitleStyle style)
        {
            var s = GameManager.I.State;
            var clips = SelectBestClips(s.bankedFootage);
            float views = RawViews(clips, style, s.followers, s.day);
            long f = (long)(views * FollowerConversion * TitleConversionMultiplier(style));
            long money = s.followers >= MonetizationFollowers ? (long)(views / 1000f * MoneyPerThousandViews) : 0;
            return ((long)(views * 0.85f), (long)(views * 1.2f), f, money);
        }

        static float RawViews(List<FootageClip> clips, TitleStyle style, long followers, int day)
        {
            float sum = 0f;
            foreach (var c in clips) sum += c.Value * NoveltyFactor(c, day);
            float inc = DifficultySettings.For(GameManager.I.State.difficulty).incomeMultiplier;
            return sum * ViewsPerValue * Reach(followers) * TitleViewMultiplier(style) * inc;
        }

        /// Creates an upload from all banked footage. Results arrive the next morning. Returns null if there is no footage.
        public static PendingUpload CreateUpload(TitleStyle style)
        {
            var s = GameManager.I.State;
            if (s.bankedFootage.Count == 0) return null;
            var clips = SelectBestClips(s.bankedFootage);
            var up = new PendingUpload { dayUploaded = s.day, titleStyle = (int)style, title = SuggestTitle(clips, style), clips = clips };
            s.pendingUploads.Add(up);
            s.bankedFootage.Clear();
            GameEvents.RaiseUploadCreated(up);
            GameManager.I.Save();
            return up;
        }

        /// Called by GameManager at the start of a new day.
        public static void ResolvePendingUploads()
        {
            var s = GameManager.I.State;
            if (s.pendingUploads.Count == 0) return;
            var gm = GameManager.I;
            foreach (var up in s.pendingUploads)
            {
                var style = (TitleStyle)up.titleStyle;
                float raw = RawViews(up.clips, style, s.followers, up.dayUploaded);
                // deterministic variance per day
                var rng = new System.Random(up.dayUploaded * 7919 + up.clips.Count * 31);
                float variance = 0.85f + (float)rng.NextDouble() * 0.35f;
                long views = (long)(raw * variance);
                long followers = (long)(views * FollowerConversion * TitleConversionMultiplier(style));
                long money = s.followers >= MonetizationFollowers ? (long)(views / 1000f * MoneyPerThousandViews) : 0;
                foreach (var c in up.clips) Journal.Get(c.speciesId).lastUploadDay = up.dayUploaded;

                var rec = new VideoRecord
                {
                    day = up.dayUploaded, title = up.title, titleStyle = up.titleStyle, views = views,
                    followersGained = followers, money = money, clipCount = up.clips.Count,
                    topSpeciesId = up.clips.Count > 0 ? up.clips[0].speciesId : null
                };
                s.videos.Add(rec);
                s.totalViews += views;
                gm.AddFollowers(followers);
                if (money > 0) gm.AddMoney(money);
                GameEvents.RaiseUploadResolved(rec);
            }
            s.pendingUploads.Clear();
        }

        /// Daily sponsor income from completed journal pages (only once sponsors are unlocked).
        public static long DailySponsorIncome()
        {
            var s = GameManager.I.State;
            if (s.followers < SponsorFollowers) return 0;
            long sum = 0;
            for (int i = 0; i < s.sponsorSites.Count; i++) sum += 150 * (i + 1);
            return sum;
        }

        // ------------------------------------------------------------------ milestones (balanced to 1,000,000)
        public const long LivestreamFollowers = 1000;
        public const long SponsorFollowers = 5000;
        public const long GuestFollowers = 25000;
        public const long FinaleFollowers = 1000000;

        public static readonly List<Milestone> Milestones = new List<Milestone>
        {
            new Milestone { id = "m100", followers = 100, titleDe = "100 Follower", titleEn = "100 Followers",
                descDe = "FinTube zahlt dir jetzt Geld pro Aufruf.", descEn = "FinTube now pays you per view." },
            new Milestone { id = "m1000", followers = 1000, titleDe = "1.000 Follower", titleEn = "1,000 Followers",
                descDe = "KRILL.tv freigeschaltet: Du kannst jetzt live streamen.", descEn = "KRILL.tv unlocked: you can now go live." },
            new Milestone { id = "m2500", followers = 2500, titleDe = "2.500 Follower", titleEn = "2,500 Followers",
                descDe = "Livestream aus dem Wasser mit Relay-Boje.", descEn = "Livestream from underwater with a relay buoy." },
            new Milestone { id = "m5000", followers = 5000, titleDe = "5.000 Follower", titleEn = "5,000 Followers",
                descDe = "Erste Sponsoren-Deals: Werbung auf deinem Boot.", descEn = "First sponsor deals: ads on your boat." },
            new Milestone { id = "m10000", followers = 10000, titleDe = "10.000 Follower", titleEn = "10,000 Followers",
                descDe = "Sponsoren auf deiner Ausrüstung. Die Murk-App meldet sich …", descEn = "Sponsors on your gear. The Murk app gets in touch …" },
            new Milestone { id = "m25000", followers = 25000, titleDe = "25.000 Follower", titleEn = "25,000 Followers",
                descDe = "TourTrap: Zahlende Gast-Taucher buchen dich.", descEn = "TourTrap: paying guest divers book you." },
            new Milestone { id = "m50000", followers = 50000, titleDe = "50.000 Follower", titleEn = "50,000 Followers",
                descDe = "Mini-U-Boot im Shop. Der Tiefseegraben ruft.", descEn = "Mini sub in the shop. The deep trench is calling." },
            new Milestone { id = "m100000", followers = 100000, titleDe = "100.000 Follower", titleEn = "100,000 Followers",
                descDe = "Rang „Sealebrity“. VIP-Gäste und Promi-Anfragen.", descEn = "Rank “Sealebrity”. VIP guests and celebrity requests." },
            new Milestone { id = "m250000", followers = 250000, titleDe = "250.000 Follower", titleEn = "250,000 Followers",
                descDe = "Tiefsee-U-Boot im Shop.", descEn = "Deep sea submarine in the shop." },
            new Milestone { id = "m500000", followers = 500000, titleDe = "500.000 Follower", titleEn = "500,000 Followers",
                descDe = "Ein Streamingdienst will deine Doku-Serie.", descEn = "A streaming service wants your docuseries." },
            new Milestone { id = "m1000000", followers = FinaleFollowers, titleDe = "1.000.000 Follower!", titleEn = "1,000,000 Followers!",
                descDe = "Du bist eine echte Sealebrity. Finale: Livestream aus dem Abgrund.", descEn = "You are a true Sealebrity. Finale: livestream from the abyss." },
        };

        public static Milestone NextMilestone(long followers) => Milestones.Find(m => m.followers > followers);

        public static void CheckMilestones()
        {
            var s = GameManager.I.State;
            foreach (var m in Milestones)
            {
                if (s.followers >= m.followers && !s.reachedMilestones.Contains(m.id))
                {
                    s.reachedMilestones.Add(m.id);
                    GameEvents.RaiseMilestoneReached(m);
                    GameEvents.RaiseBanner(m.Title + "\n" + m.Description, new Color(1f, 0.76f, 0.2f));
                    if (m.followers >= FinaleFollowers) s.finaleReached = true;
                }
            }
            CheckUnlocks();
        }

        // ------------------------------------------------------------------ dive sites and sponsors
        public static bool SiteRequirementsMet(DiveSite site, out string reason)
        {
            var s = GameManager.I.State;
            reason = null;
            if (s.unlockedSites.Contains(site.id)) return true;
            if (site.prevSiteId != null && Journal.SiteCompletion(site.prevSiteId) < site.prevCompletionNeeded)
            {
                var prev = DiveSites.Get(site.prevSiteId);
                reason = Loc.Tr($"Tagebuch-Seite „{prev.Name}“ zu {Loc.Percent(site.prevCompletionNeeded)} füllen",
                                $"Fill the “{prev.Name}” journal page to {Loc.Percent(site.prevCompletionNeeded)}");
                return false;
            }
            if (s.followers < site.followersNeeded)
            {
                reason = Loc.Tr($"{Loc.Number(site.followersNeeded)} Follower nötig", $"{Loc.Number(site.followersNeeded)} followers needed");
                return false;
            }
            return true;
        }

        public static bool IsSiteUnlocked(DiveSite site) => GameManager.I.State.unlockedSites.Contains(site.id);

        public static void CheckUnlocks()
        {
            if (GameManager.I == null) return;
            var s = GameManager.I.State;
            foreach (var site in DiveSites.All)
            {
                if (!s.unlockedSites.Contains(site.id) && SiteRequirementsMet(site, out _))
                {
                    s.unlockedSites.Add(site.id);
                    GameEvents.RaiseSiteUnlocked(site);
                    if (site.prevSiteId != null)
                        GameEvents.RaiseBanner(Loc.Tr("Neuer Tauchplatz: ", "New dive site: ") + site.Name, new Color(0.18f, 0.77f, 0.75f));
                }
                if (Journal.SiteCompletion(site.id) >= 1f && !s.sponsorSites.Contains(site.id))
                {
                    s.sponsorSites.Add(site.id);
                    GameEvents.RaiseSponsorUnlocked(site.id);
                    GameEvents.RaiseBanner(Loc.Tr($"Tagebuch-Seite „{site.Name}“ komplett!\nEin Sponsor meldet sich (aktiv ab {Loc.Number(SponsorFollowers)} Followern).",
                                                  $"Journal page “{site.Name}” complete!\nA sponsor gets in touch (active from {Loc.Number(SponsorFollowers)} followers)."),
                                           new Color(0.24f, 0.86f, 0.52f));
                }
            }
        }
    }
}
