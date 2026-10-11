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
        // Balanced with the economy simulation (scratchpad/econsim_core): solo ≈ 1,000 followers after 2–2.5 h of play,
        // 1,000,000 after ~40 h; co-op 2 players ~0.88×, 4 players ~0.78× of that time.
        //   views     = Σ(best clip value per species × novelty) × ViewsPerValue × Reach(followers) × title × difficulty × co-op
        //   followers = views × Conversion(followers) × title conversion
        //   money     = views / 1000 × MoneyPerThousandViews (from MonetizationFollowers on)
        public const float ViewsPerValue = 40f;
        public const float ReachScale = 300f, ReachExponent = 0.8f;
        /// Follower conversion of a brand-new channel (big channels convert a smaller share of their views).
        public const float FollowerConversion = 0.009f;
        public const float ConversionScale = 2000f, ConversionExponent = 0.4f;
        public const float MoneyPerThousandViews = 4f;
        public const long MonetizationFollowers = 100;
        /// Clips of a species uploaded again within this many days only count half.
        public const int NoveltyDays = 2;
        public const float NoveltyPenalty = 0.5f;

        public static float Reach(long followers) => 1f + Mathf.Pow(Mathf.Max(0, followers) / ReachScale, ReachExponent);

        public static float Conversion(long followers) => FollowerConversion / Mathf.Pow(1f + Mathf.Max(0, followers) / ConversionScale, ConversionExponent);

        /// New followers from a video's views.
        public static long FollowersFromViews(float views, long followers, TitleStyle style) =>
            (long)(views * Conversion(followers) * TitleConversionMultiplier(style));

        public static long MoneyFromViews(float views, long followers) =>
            followers >= MonetizationFollowers ? (long)(views / 1000f * MoneyPerThousandViews) : 0;

        // ------------------------------------------------------------------ co-op fairness
        // One shared channel, one wallet, shared gear ownership (each player equips their own choice).
        // More divers find more and bigger animals, so each video counts a bit less and each fish pays a bit less:
        // the crew still progresses faster together (4 players ≈ 1.3× solo speed), but not 4×.
        static readonly float[] CoopUpload = { 1f, 1f, 0.75f, 0.65f, 0.58f };
        static readonly float[] CoopFish = { 1f, 1f, 0.85f, 0.75f, 0.65f };

        /// Multiplier on upload views (→ followers and money) for the number of players in the session.
        public static float CoopUploadFactor(int players) => CoopUpload[Mathf.Clamp(players, 1, 4)];
        /// Multiplier on each caught fish's value for the number of players in the session.
        public static float CoopFishFactor(int players) => CoopFish[Mathf.Clamp(players, 1, 4)];

        // ------------------------------------------------------------------ air
        /// Breath-hold seconds without a tank (snorkel set). Tanks: see GearDatabase (90 s … 220 s at the surface).
        public const float BreathHoldSeconds = 20f;
        /// Air consumption multiplier at a depth (1 at the surface, 2 at 60 m). Sprinting adds ×1.4 (Player module).
        public static float AirRate(float depthMeters) => 1f + Mathf.Max(0f, depthMeters) / 60f;

        // Each title style has one strength: Honest = best for followers, Spicy = balanced, Clickbait = best for money.
        public static float TitleViewMultiplier(TitleStyle t) => t == TitleStyle.Honest ? 1f : t == TitleStyle.Spicy ? 1.3f : 1.8f;
        public static float TitleConversionMultiplier(TitleStyle t) => t == TitleStyle.Honest ? 1.3f : t == TitleStyle.Spicy ? 0.9f : 0.6f;

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
            var e = Journal.Find(c.speciesId);
            return e != null && day - e.lastUploadDay <= NoveltyDays ? NoveltyPenalty : 1f;
        }

        public static string SuggestTitle(List<FootageClip> clips, TitleStyle style)
        {
            if (clips.Count == 0) return "";
            var top = clips[0].Species;
            string name = top != null ? top.Name : "?";
            switch (style)
            {
                case TitleStyle.Honest: return Loc.Tr($"Tauchgang am {GameManager.I.State.day}. Tag: {name} und mehr", $"Dive day {GameManager.I.State.day}: {name} and more");
                case TitleStyle.Spicy: return Loc.Tr($"{name}: Ich war SO nah dran!", $"{name}: I got SO close!");
                default: return Loc.Tr($"{name.ToUpper()} HAT MICH FAST GEFRESSEN (KEIN CLICKBAIT)", $"{name.ToUpper()} ALMOST ATE ME (NOT CLICKBAIT)");
            }
        }

        /// Today's not yet resolved upload (one video per day: further uploads are added to it), null when none.
        public static PendingUpload TodaysUpload(GameState s)
        {
            if (s == null) return null;
            for (int i = s.pendingUploads.Count - 1; i >= 0; i--)
                if (s.pendingUploads[i] != null && s.pendingUploads[i].dayUploaded == s.day) return s.pendingUploads[i];
            return null;
        }

        /// Estimate of views/followers/money for the current banked footage. Used by the FinTube app before uploading.
        /// Includes the clips already in today's video (one video per day – the economy is balanced for that).
        public static (long viewsMin, long viewsMax, long followers, long money) Preview(TitleStyle style)
        {
            var s = GameManager.I.State;
            var today = TodaysUpload(s);
            List<FootageClip> clips;
            if (today != null && today.clips != null && today.clips.Count > 0)
            {
                var merged = new List<FootageClip>(s.bankedFootage);
                merged.AddRange(today.clips);
                clips = SelectBestClips(merged);
            }
            else clips = SelectBestClips(s.bankedFootage);
            float views = RawViews(clips, style, s.followers, s.day);
            long f = FollowersFromViews(views, s.followers, style);
            long money = MoneyFromViews(views, s.followers);
            return ((long)(views * 0.85f), (long)(views * 1.2f), f, money);
        }

        static float RawViews(List<FootageClip> clips, TitleStyle style, long followers, int day)
        {
            float sum = 0f;
            foreach (var c in clips) sum += c.Value * NoveltyFactor(c, day);
            float inc = DifficultySettings.For(GameManager.I.State.difficulty).incomeMultiplier;
            return sum * ViewsPerValue * Reach(followers) * TitleViewMultiplier(style) * inc * CoopUploadFactor(Net.PlayerCount);
        }

        /// Creates an upload from all banked footage. Results arrive the next morning. Returns null if there is no footage.
        /// SHARED STATE: on co-op clients this sends NetCommand.Upload (style, title) and returns an optimistic copy.
        public static PendingUpload CreateUpload(TitleStyle style) => CreateUpload(style, null);

        /// title: custom title (null/empty = suggested title).
        public static PendingUpload CreateUpload(TitleStyle style, string title)
        {
            var gm = GameManager.I;
            if (gm == null) return null;
            var s = gm.State;
            if (s.bankedFootage.Count == 0) return null;
            // one video per day: a second upload on the same day is added to today's video (best clip per species)
            var today = TodaysUpload(s);
            List<FootageClip> clips;
            if (today != null)
            {
                var merged = new List<FootageClip>(today.clips ?? new List<FootageClip>());
                merged.AddRange(s.bankedFootage);
                clips = SelectBestClips(merged);
            }
            else clips = SelectBestClips(s.bankedFootage);
            if (string.IsNullOrEmpty(title)) title = SuggestTitle(clips, style);
            if (Net.IsClient) Net.Command(NetCommand.Upload, ((int)style).ToString(System.Globalization.CultureInfo.InvariantCulture), title);
            PendingUpload up;
            if (today != null)
            {
                today.clips = clips;
                today.title = title;
                today.titleStyle = (int)style;
                up = today;
            }
            else
            {
                up = new PendingUpload { dayUploaded = s.day, titleStyle = (int)style, title = title, clips = clips };
                s.pendingUploads.Add(up);   // clients: optimistic until the next host snapshot
            }
            s.bankedFootage.Clear();
            GameEvents.RaiseUploadCreated(up);
            gm.Save();
            return up;
        }

        /// Called by GameManager at the start of a new day (authority only).
        public static void ResolvePendingUploads()
        {
            if (!Net.HasAuthority || GameManager.I == null) return;
            var s = GameManager.I.State;
            if (s.pendingUploads.Count == 0) return;
            var gm = GameManager.I;
            MergeSameDayUploads(s.pendingUploads);
            foreach (var up in s.pendingUploads)
            {
                var style = (TitleStyle)up.titleStyle;
                float raw = RawViews(up.clips, style, s.followers, up.dayUploaded);
                // deterministic variance per day
                var rng = new System.Random(up.dayUploaded * 7919 + up.clips.Count * 31);
                float variance = 0.85f + (float)rng.NextDouble() * 0.35f;
                long views = (long)(raw * variance);
                long followers = FollowersFromViews(views, s.followers, style);
                long money = MoneyFromViews(views, s.followers);
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

        /// Older saves (or a race in co-op) may hold several uploads of one day: merge them into one video
        /// (best clip per species, the newest title and style), so splitting footage never pays more than one video.
        static void MergeSameDayUploads(List<PendingUpload> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                var a = list[i];
                if (a == null) continue;
                List<FootageClip> merged = null;
                string title = a.title;
                int titleStyle = a.titleStyle;
                for (int k = i + 1; k < list.Count; k++)
                {
                    var b = list[k];
                    if (b == null || b.dayUploaded != a.dayUploaded) continue;
                    if (merged == null) merged = new List<FootageClip>(a.clips ?? new List<FootageClip>());
                    if (b.clips != null) merged.AddRange(b.clips);
                    title = b.title;
                    titleStyle = b.titleStyle;
                    list.RemoveAt(k);
                    k--;
                }
                if (merged == null) continue;
                a.clips = SelectBestClips(merged);
                a.title = title;
                a.titleStyle = titleStyle;
            }
            for (int i = list.Count - 1; i >= 0; i--) if (list[i] == null || list[i].clips == null) list.RemoveAt(i);
        }

        /// Daily sponsor income per completed journal page: SponsorPerPage × (1, 2, 3 …).
        public const long SponsorPerPage = 200;

        /// Daily sponsor income from completed journal pages (only once sponsors are unlocked).
        public static long DailySponsorIncome()
        {
            var s = GameManager.I.State;
            if (s.followers < SponsorFollowers) return 0;
            long sum = 0;
            for (int i = 0; i < s.sponsorSites.Count; i++) sum += SponsorPerPage * (i + 1);
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
                descDe = "KRILL.tv meldet sich – Livestreams kommen bald. Hochsee-Rute im Shop.", descEn = "KRILL.tv gets in touch – livestreams are coming soon. Deep-sea rod in the shop." },
            new Milestone { id = "m2500", followers = 2500, titleDe = "2.500 Follower", titleEn = "2,500 Followers",
                descDe = "Doppelflasche im Shop – das Wrack ruft.", descEn = "Twin tanks in the shop – the wreck is calling." },
            new Milestone { id = "m5000", followers = 5000, titleDe = "5.000 Follower", titleEn = "5,000 Followers",
                descDe = "Sponsoren zahlen jetzt täglich für volle Sealog-Seiten.", descEn = "Sponsors now pay daily for full Sealog pages." },
            new Milestone { id = "m10000", followers = 10000, titleDe = "10.000 Follower", titleEn = "10,000 Followers",
                descDe = "RIB-Umbau und Doppel-Strobes im Shop. Jemand schreibt dir …", descEn = "RIB conversion and twin strobes in the shop. Someone is texting you …" },
            new Milestone { id = "m25000", followers = 25000, titleDe = "25.000 Follower", titleEn = "25,000 Followers",
                descDe = "Tauchscooter und Profi-Kamera im Shop.", descEn = "Dive scooter and pro camera in the shop." },
            new Milestone { id = "m50000", followers = 50000, titleDe = "50.000 Follower", titleEn = "50,000 Followers",
                descDe = "Ein Doku-Sender schaut zu – weiter so!", descEn = "A documentary channel is watching – keep it up!" },
            new Milestone { id = "m100000", followers = 100000, titleDe = "100.000 Follower", titleEn = "100,000 Followers",
                descDe = "Rang „Sealebrity“. Der Tiefseegraben ruft.", descEn = "Rank “Sealebrity”. The deep trench is calling." },
            new Milestone { id = "m250000", followers = 250000, titleDe = "250.000 Follower", titleEn = "250,000 Followers",
                descDe = "Das Kino-Rig 6K ist im Shop.", descEn = "The 6K cinema rig is in the shop." },
            new Milestone { id = "m500000", followers = 500000, titleDe = "500.000 Follower", titleEn = "500,000 Followers",
                descDe = "Halbe Million – Respekt!", descEn = "Half a million – respect!" },
            new Milestone { id = "m1000000", followers = FinaleFollowers, titleDe = "1.000.000 Follower!", titleEn = "1,000,000 Followers!",
                descDe = "Du bist eine echte Sealebrity!", descEn = "You are a true Sealebrity!" },
        };

        public static Milestone NextMilestone(long followers) => Milestones.Find(m => m.followers > followers);

        /// Authority only (clients receive milestones with the host snapshot).
        public static void CheckMilestones()
        {
            if (!Net.HasAuthority || GameManager.I == null) return;
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
                reason = Loc.Tr($"Sealog-Seite „{prev.Name}“ zu {Loc.Percent(site.prevCompletionNeeded)} füllen",
                                $"Fill the “{prev.Name}” Sealog page to {Loc.Percent(site.prevCompletionNeeded)}");
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

        /// Authority only (clients receive unlocks with the host snapshot).
        public static void CheckUnlocks()
        {
            if (GameManager.I == null || !Net.HasAuthority) return;
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
                    GameEvents.RaiseBanner(Loc.Tr($"Sealog-Seite „{site.Name}“ komplett!\nEin Sponsor meldet sich (aktiv ab {Loc.Number(SponsorFollowers)} Followern).",
                                                  $"Sealog page “{site.Name}” complete!\nA sponsor gets in touch (active from {Loc.Number(SponsorFollowers)} followers)."),
                                           new Color(0.24f, 0.86f, 0.52f));
                }
            }
        }
    }
}
