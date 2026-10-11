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

        /// Index of the channel rank reached exactly at this milestone (−1 = none).
        public int RankIndex
        {
            get
            {
                var r = Progression.Ranks;
                for (int i = 1; i < r.Length; i++) if (r[i].followers == followers) return i;
                return -1;
            }
        }

        /// The one ribbon for this milestone („Symbol + eine kurze Zeile“): a milestone that is also a rank threshold
        /// shows the rank („Neuer Rang: Garnele!“) instead of a second rank ribbon; the band is the short description.
        public string BannerText
        {
            get
            {
                int r = RankIndex;
                string head = r > 0 ? Loc.Tr("Neuer Rang: ", "New rank: ") + Progression.RankName(r) + "!" : Title;
                return head + "\n" + Description;
            }
        }
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

        public static float SizeMultiplier(SizeTier t) => TierMultiplier[Mathf.Clamp((int)t, 0, TierMultiplier.Length - 1)];

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
        /// K.2: crowd bonus per other animal detected at the same time (max 8 → +32 %), event and selfie multipliers.
        public const float CrowdBonus = 0.04f, EventClipMultiplier = 1.25f, SelfieMultiplier = 1.3f, SelfieMatchMultiplier = 1.15f;
        public const int MaxCrowd = 8;

        /// Value of one clip = species base value × fixed size multiplier × clip quality (× record/action bonus)
        /// × crowd (1 + 0.04 × crowd) × event 1.25 × selfie 1.3 (× 1.15 when the expression fits) – K.2/K.4.
        /// The upload keeps the best clip per species (max 12), so the video value grows with every species and size tier.
        public static float ClipValue(FootageClip c)
        {
            if (c == null) return 0f;
            var s = SpeciesDatabase.Get(c.speciesId);
            if (s == null) return 0f;
            float q = float.IsNaN(c.quality) ? 0.1f : Mathf.Clamp(c.quality, 0.1f, 1.6f);
            float action = 1f + Mathf.Clamp01(float.IsNaN(c.action) ? 0f : c.action) * 0.4f;
            float record = c.isRecord ? 1.5f : 1f;
            float crowd = 1f + CrowdBonus * Mathf.Clamp(c.crowd, 0, MaxCrowd);
            float ev = string.IsNullOrEmpty(c.eventId) ? 1f : EventClipMultiplier;
            float selfie = c.selfie ? SelfieMultiplier * (c.selfieMatch ? SelfieMatchMultiplier : 1f) : 1f;
            return s.baseValue * SizeMultiplier(c.Tier) * q * action * record * crowd * ev * selfie;
        }

        // ------------------------------------------------------------------ uploads
        // Balanced with the economy simulation scratchpad/econsim_k (section K, 40 seeds, 16 active min per day):
        // first tank after ≈ 7 min of fishing, ≈ 258 $/day before 1,000 followers, camera t1 ≈ day 4, fins t1 ≈ day 5;
        // 1,000,000 followers at ≈ 38 h solo incl. a daily livestream (2 players 34.8 h, 4 players 31.3 h).
        //   views     = Σ(best clip value per species × novelty) × ViewsPerValue × Reach(followers) × title × difficulty
        //               × co-op × (1 + bonuses: challenge, cozy/lantern, duel, thumbnail)
        //   followers = views × Conversion(followers) × title conversion
        //   money     = views / 1000 × MoneyPerThousandViews × StarterBoost(followers)   (from video 1, K.1)
        public const float ViewsPerValue = 40f;
        public const float ReachScale = 300f, ReachExponent = 0.8f;
        /// Follower conversion of a brand-new channel (big channels convert a smaller share of their views).
        /// K.1: 0.009 → 0.0058 (compensates the K.2 filming rework and the daily livestream).
        public const float FollowerConversion = 0.0058f;
        public const float ConversionScale = 2000f, ConversionExponent = 0.4f;
        /// 4 $ per 1,000 views from video 1 (K.1 – D.4's 2 $ starter rate is dropped).
        public const float MoneyPerThousandViews = 4f;
        /// FinTube pays from the first video (Q45, K.1).
        public const long MonetizationFollowers = 0;
        /// K.1 starter boost: upload money × (1 + StarterBoostMax × max(0, 1 − F / StarterBoostEnd)) – ×2.5 at 0 followers.
        public const float StarterBoostMax = 1.5f;
        public const float StarterBoostEnd = 5000f;
        /// One-time "Creator-Startbonus" with the first resolved video (K.1: 50 $).
        public const long StarterBonus = 50;
        /// Notifications show the boost only while it is above this.
        public const float StarterBoostShowAbove = 1.05f;
        /// Clips of a species uploaded again within this many days only count half.
        public const int NoveltyDays = 2;
        public const float NoveltyPenalty = 0.5f;
        /// Q31/L.4: views × (1 + ThumbnailBonus × thumbQuality).
        public const float ThumbnailBonus = 0.15f;
        /// B.3: a won weekly follower duel gives +5 % views on next week's videos.
        public const float DuelViewsBonus = 0.05f;
        /// L.8: releasing a lantern on Lantern Evening → +5 % views (does not stack with the cozy bonus).
        public const float LanternViewsBonus = 0.05f;
        /// L.4: max title length (the host cuts it).
        public const int MaxTitleLength = 60;

        public static float Reach(long followers) => 1f + Mathf.Pow(Mathf.Max(0, followers) / ReachScale, ReachExponent);

        public static float Conversion(long followers) => FollowerConversion / Mathf.Pow(1f + Mathf.Max(0, followers) / ConversionScale, ConversionExponent);

        /// New followers from a video's views.
        public static long FollowersFromViews(float views, long followers, TitleStyle style) =>
            (long)(views * Conversion(followers) * TitleConversionMultiplier(style));

        /// K.1 starter boost on upload money (2.5 at 0 followers → 1.0 from 5,000).
        public static float StarterBoost(long followers) => 1f + StarterBoostMax * Mathf.Max(0f, 1f - Mathf.Max(0, followers) / StarterBoostEnd);

        public static long MoneyFromViews(float views, long followers) =>
            followers >= MonetizationFollowers ? (long)(views / 1000f * MoneyPerThousandViews * StarterBoost(followers)) : 0;

        // ------------------------------------------------------------------ expected upload (C.3 livestream, K.3 rewards, K.7 deals)
        /// Livestream factors (C.3, tuned in econsim_k): a good daily stream ≈ +80 % money, +25 % followers of an upload.
        public const float StreamMoneyFactor = 0.8f;
        public const float StreamFollowerFactor = 0.25f;
        /// C.4: followers lost when the stream ends by fainting: clamp(round(share × F), min, max) (share per difficulty).
        public const long StreamFaintLossMin = 10, StreamFaintLossMax = 25000;

        /// Typical day's upload clip value (econsim): clamp(150 + 135 × log10(F / 1000), 150, 700).
        public static float Vref(long followers) =>
            Mathf.Clamp(150f + 135f * Mathf.Log10(Mathf.Max(1f, followers) / 1000f), 150f, 700f);

        public static float ExpectedUploadViews(long followers) => Vref(followers) * ViewsPerValue * Reach(followers);

        public static long ExpectedUploadMoney(long followers) => MoneyFromViews(ExpectedUploadViews(followers), followers);

        public static long ExpectedUploadFollowers(long followers) => FollowersFromViews(ExpectedUploadViews(followers), followers, TitleStyle.Honest);

        /// C.4 follower loss when a stream ends by fainting.
        public static long StreamFaintLoss(long followers, Difficulty d)
        {
            float share = DifficultySettings.For(d).streamFaintLoss;
            return (long)Mathf.Clamp(Mathf.Round(share * Mathf.Max(0, followers)), StreamFaintLossMin, StreamFaintLossMax);
        }

        // ------------------------------------------------------------------ channel ranks (Q50)
        public static readonly (long followers, string de, string en)[] Ranks =
        {
            (0, "Plankton", "Plankton"),
            (100, "Garnele", "Shrimp"),
            (1000, "Clownfisch", "Clownfish"),
            (5000, "Seepferdchen", "Seahorse"),
            (25000, "Schildkröte", "Turtle"),
            (100000, "Sealebrity", "Sealebrity"),
            (500000, "Hai-Legende", "Shark Legend"),
            (1000000, "Wal-Ikone", "Whale Icon"),
        };

        public static int RankIndex(long followers)
        {
            int r = 0;
            for (int i = 0; i < Ranks.Length; i++) if (followers >= Ranks[i].followers) r = i;
            return r;
        }

        public static string RankName(int index)
        {
            index = Mathf.Clamp(index, 0, Ranks.Length - 1);
            return Loc.Tr(Ranks[index].de, Ranks[index].en);
        }

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
            if (clips == null || clips.Count == 0) return "";
            var top = clips[0].Species;
            string name = top != null ? top.Name : "?";
            switch (style)
            {
                case TitleStyle.Honest: return Loc.Tr($"Tauchgang am {GameManager.I.State.day}. Tag: {name} und mehr", $"Dive day {GameManager.I.State.day}: {name} and more");
                case TitleStyle.Spicy: return Loc.Tr($"{name}: Ich war SO nah dran!", $"{name}: I got SO close!");
                default: return Loc.Tr($"{name.ToUpper()} HAT MICH FAST GEFRESSEN (KEIN CLICKBAIT)", $"{name.ToUpper()} ALMOST ATE ME (NOT CLICKBAIT)");
            }
        }

        /// L.4: three suggestion chips for the selected title style, built from the video (top species, number of
        /// animals, a record, an event, the site). Fills `into` (cleared first). The first one is the placeholder.
        public static void SuggestTitles(List<FootageClip> clips, TitleStyle style, List<string> into)
        {
            into.Clear();
            if (clips == null || clips.Count == 0 || GameManager.I == null) return;
            int day = GameManager.I.State.day;
            var top = clips[0].Species;
            string a = top != null ? top.Name : "?";
            // distinct species (clips can repeat a species) and the first species different from the top one
            int n = 0;
            string b = null;
            for (int i = 0; i < clips.Count; i++)
            {
                string id = clips[i].speciesId;
                bool dup = false;
                for (int k = 0; k < i; k++) if (clips[k].speciesId == id) { dup = true; break; }
                if (dup) continue;
                n++;
                if (b == null && i > 0 && clips[i].Species != null && id != clips[0].speciesId) b = clips[i].Species.Name;
            }
            n = Mathf.Max(1, n);
            int more = Mathf.Max(0, n - (b != null ? 2 : 1));
            string species = n == 1 ? Loc.Tr("1 Art", "1 species") : Loc.Tr($"{n} Arten", $"{n} species");
            int divers = Mathf.Max(1, Net.PlayerCount);
            string diverText = divers == 1 ? Loc.Tr("1 Taucher", "1 diver") : Loc.Tr($"{divers} Taucher", $"{divers} divers");
            FootageClip record = null, ev = null, biggest = clips[0];
            for (int i = 0; i < clips.Count; i++)
            {
                var c = clips[i];
                if (record == null && c.isRecord) record = c;
                if (ev == null && !string.IsNullOrEmpty(c.eventId)) ev = c;
                if (c.sizeCm > biggest.sizeCm) biggest = c;
            }
            var site = DiveSites.Get(clips[0].siteId);
            string siteName = site != null ? site.Name : Loc.Tr("Rostbucht", "Rust Cove");
            string big = biggest.Species != null ? biggest.Species.Name : a;
            string size = Loc.Length(biggest.sizeCm);
            switch (style)
            {
                case TitleStyle.Honest:
                    if (b == null) into.Add(Loc.Tr($"Tauchtag {day}: {a}", $"Dive day {day}: {a}"));
                    else if (more == 0) into.Add(Loc.Tr($"Tauchtag {day}: {a} und {b}", $"Dive day {day}: {a} and {b}"));
                    else if (more == 1) into.Add(Loc.Tr($"Tauchtag {day}: {a}, {b} und 1 weiteres Tier", $"Dive day {day}: {a}, {b} and 1 more animal"));
                    else into.Add(Loc.Tr($"Tauchtag {day}: {a}, {b} und {more} weitere Tiere", $"Dive day {day}: {a}, {b} and {more} more animals"));
                    into.Add(Loc.Tr($"{siteName}: {species} an einem Tag", $"{siteName}: {species} in one day"));
                    into.Add(record != null && record.Species != null
                        ? Loc.Tr($"Neuer Rekord: {record.Species.Name} ({Loc.Length(record.sizeCm)})", $"New record: {record.Species.Name} ({Loc.Length(record.sizeCm)})")
                        : Loc.Tr($"Ein ruhiger Tag mit {a}", $"A calm day with {a}"));
                    break;
                case TitleStyle.Spicy:
                    // German: no article before the species name (gender/case would not fit every name – „an einem
                    // 2,20 m-Großer Tümmler“ in playtest round 2)
                    into.Add(Loc.Tr($"Ich war SO nah dran: {big}, {size}!", $"I got SO close to a {size} {big}!"));
                    into.Add(ev != null
                        ? Loc.Tr("Mitten im Tier-Chaos – das musst du sehen!", "Right in the middle of the chaos – you have to see this!")
                        : Loc.Tr($"{a} hat mich angeschaut. Lange.", $"{a} looked at me. For a long time."));
                    into.Add(Loc.Tr($"{species}, {diverText}, 0 Plan", $"{species}, {diverText}, 0 plan"));
                    break;
                default:
                    bool shark = false;
                    for (int i = 0; i < clips.Count; i++) { var sp = clips[i].Species; if (sp != null && sp.plan == BodyPlan.Shark) { shark = true; break; } }
                    into.Add(shark ? Loc.Tr("HAI GREIFT AN?! (nein)", "SHARK ATTACK?! (no)") : Loc.Tr($"{a.ToUpper()} GREIFT AN?! (nein)", $"{a.ToUpper()} ATTACKS?! (no)"));
                    into.Add(Loc.Tr($"Das SCHLIMMSTE, was mir je passiert ist ({big})", $"The WORST thing that ever happened to me ({big})"));
                    into.Add(Loc.Tr("99 % schaffen es nicht bis zum Ende!!", "99% don't make it to the end!!"));
                    break;
            }
            for (int i = 0; i < into.Count; i++) into[i] = SanitizeTitle(into[i]);
        }

        /// L.4 title rules: no control characters, trimmed, max 60 characters (the host cuts guest titles too).
        public static string SanitizeTitle(string title)
        {
            if (string.IsNullOrEmpty(title)) return "";
            var sb = new System.Text.StringBuilder(Mathf.Min(title.Length, MaxTitleLength));
            for (int i = 0; i < title.Length && sb.Length < MaxTitleLength; i++)
            {
                char ch = title[i];
                if (char.IsControl(ch)) continue;
                if (char.IsSurrogate(ch)) continue;   // Fredoka has no emoji glyphs
                sb.Append(ch);
            }
            return sb.ToString().Trim();
        }

        /// Extra views on an upload of `day` (challenges, cozy/lantern, duel) – 0 = none.
        public static float ViewsBonus(GameState s, int day)
        {
            if (s == null) return 0f;
            float b = 0f;
            if (s.challengeBonusDay == day) b += ChallengeDatabase.ViewsBonus * Mathf.Max(0, s.challengeBonusCount);
            if (s.cozyBonusDay == day || s.lastLanternDay == day) b += Mathf.Max(FoodDatabase.CozyViewsBonus, LanternViewsBonus);
            if (s.duelWonUntilDay >= day) b += DuelViewsBonus;
            return b;
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
            float views = RawViews(clips, style, s.followers, s.day, today != null ? today.thumbQuality : 0f);
            long f = FollowersFromViews(views, s.followers, style);
            long money = MoneyFromViews(views, s.followers);
            return ((long)(views * 0.85f), (long)(views * 1.2f), f, money);
        }

        static float RawViews(List<FootageClip> clips, TitleStyle style, long followers, int day, float thumbQuality)
        {
            float sum = 0f;
            foreach (var c in clips) sum += c.Value * NoveltyFactor(c, day);
            var s = GameManager.I.State;
            float inc = DifficultySettings.For(s.difficulty).incomeMultiplier;
            float thumb = 1f + ThumbnailBonus * Mathf.Clamp01(float.IsNaN(thumbQuality) ? 0f : thumbQuality);
            float bonus = 1f + ViewsBonus(s, day);
            return sum * ViewsPerValue * Reach(followers) * TitleViewMultiplier(style) * inc * CoopUploadFactor(Net.PlayerCount) * thumb * bonus;
        }

        /// Creates an upload from all banked footage. Results arrive the next morning. Returns null if there is no footage.
        /// SHARED STATE: on co-op clients this sends NetCommand.Upload (style, title …) and returns an optimistic copy.
        public static PendingUpload CreateUpload(TitleStyle style) => CreateUpload(style, null);

        /// title: custom title (null/empty = suggested title).
        public static PendingUpload CreateUpload(TitleStyle style, string title) => CreateUpload(style, title, null, null, false, 0f);

        /// L.4: own title (cut to 60 characters, empty → suggestion), collage species (max 5), the local collage PNG
        /// (thumbs/day_{n}.png – stays on this machine) and the thumbnail quality (Q31: views × (1 + 0.15 × q)).
        /// Co-op: NetCommand.Upload args = style, title, thumbSpecies csv, customTitle 0/1, thumbQuality.
        public static PendingUpload CreateUpload(TitleStyle style, string title, string[] thumbSpecies, string thumbPath, bool customTitle, float thumbQuality)
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
            title = SanitizeTitle(title);
            if (string.IsNullOrEmpty(title)) { title = SanitizeTitle(SuggestTitle(clips, style)); customTitle = false; }
            thumbSpecies = CleanThumbSpecies(thumbSpecies, clips);
            thumbQuality = float.IsNaN(thumbQuality) ? 0f : Mathf.Clamp01(thumbQuality);
            if (Net.IsClient)
            {
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                Net.Command(NetCommand.Upload, ((int)style).ToString(inv), title, string.Join(",", thumbSpecies),
                    customTitle ? "1" : "0", thumbQuality.ToString("R", inv));
            }
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
            up.thumbSpecies = thumbSpecies;
            if (!string.IsNullOrEmpty(thumbPath)) up.thumbPath = thumbPath;
            up.customTitle = customTitle;
            up.thumbQuality = Mathf.Max(up.thumbQuality, thumbQuality);
            s.bankedFootage.Clear();
            GameEvents.RaiseUploadCreated(up);
            gm.Save();
            return up;
        }

        /// Max 5 known species, defaulting to the species with the best clips (L.4 collage).
        static string[] CleanThumbSpecies(string[] wanted, List<FootageClip> clips)
        {
            var list = new List<string>(5);
            if (wanted != null)
                for (int i = 0; i < wanted.Length && list.Count < 5; i++)
                    if (SpeciesDatabase.Get(wanted[i]) != null && !list.Contains(wanted[i])) list.Add(wanted[i]);
            if (list.Count == 0)
                for (int i = 0; i < clips.Count && list.Count < 5; i++)
                    if (!list.Contains(clips[i].speciesId)) list.Add(clips[i].speciesId);
            return list.ToArray();
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
                float raw = RawViews(up.clips, style, s.followers, up.dayUploaded, up.thumbQuality);
                // deterministic variance per day
                var rng = new System.Random(up.dayUploaded * 7919 + up.clips.Count * 31);
                float variance = 0.85f + (float)rng.NextDouble() * 0.35f;
                long views = (long)(raw * variance);
                long followers = FollowersFromViews(views, s.followers, style);
                float boost = StarterBoost(s.followers);
                long money = MoneyFromViews(views, s.followers);
                long bonus = 0;
                if (!s.firstVideoBonusPaid)
                {
                    s.firstVideoBonusPaid = true;
                    bonus = StarterBonus;
                    money += bonus;
                }
                foreach (var c in up.clips) Journal.Get(c.speciesId).lastUploadDay = up.dayUploaded;

                var rec = new VideoRecord
                {
                    day = up.dayUploaded, title = up.title, titleStyle = up.titleStyle, views = views,
                    followersGained = followers, money = money, clipCount = up.clips.Count,
                    topSpeciesId = up.clips.Count > 0 ? up.clips[0].speciesId : null,
                    thumbSpecies = up.thumbSpecies, thumbPath = up.thumbPath, customTitle = up.customTitle,
                    starterBoost = boost, starterBonus = bonus
                };
                s.videos.Add(rec);
                s.totalViews += views;
                gm.AddFollowers(followers);
                if (money > 0)
                {
                    gm.AddMoney(money);
                    // L.10: "FinTube zahlt · +86 $" (+ "Starthilfe ×2,3" while the K.1 boost is noticeable)
                    string chip = "+" + Loc.Money(money);
                    if (boost > StarterBoostShowAbove)
                        chip += Loc.Tr(" · Starthilfe ×", " · starter boost ×") + boost.ToString("0.0", Loc.Culture);
                    GameEvents.RaiseNotify(NotifyKind.Money, Loc.Tr("FinTube zahlt", "FinTube pays"), chip);
                }
                GameEvents.RaiseUploadResolved(rec);
            }
            s.pendingUploads.Clear();
            Achievements.CheckTitleCounts();
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
                    if (b.thumbSpecies != null && b.thumbSpecies.Length > 0) a.thumbSpecies = b.thumbSpecies;
                    if (!string.IsNullOrEmpty(b.thumbPath)) a.thumbPath = b.thumbPath;
                    a.customTitle = b.customTitle;
                    a.thumbQuality = Mathf.Max(a.thumbQuality, b.thumbQuality);
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

        /// Daily sponsor income per completed journal page: SponsorPerPage × (1, 2, 3 …) – the base pay (Q89) that stays
        /// next to the K.7 sponsor deals.
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
        /// National sponsors and the daily page money (GDD).
        public const long SponsorFollowers = 5000;
        /// K.7: local sponsor deals from 250 followers, national from 5,000, big brands from 50,000.
        public const long LocalSponsorFollowers = 250;
        public const long NationalSponsorFollowers = 5000;
        public const long BigSponsorFollowers = 50000;
        public const long GuestFollowers = 25000;
        public const long FinaleFollowers = 1000000;

        public static readonly List<Milestone> Milestones = new List<Milestone>
        {
            new Milestone { id = "m100", followers = 100, titleDe = "100 Follower", titleEn = "100 Followers",
                descDe = "100 Follower · Gus funkt!", descEn = "100 followers · Gus calls!" },
            new Milestone { id = "m1000", followers = 1000, titleDe = "1.000 Follower", titleEn = "1,000 Followers",
                descDe = "KRILL.tv frei · Hochsee-Rute", descEn = "KRILL.tv live · deep-sea rod" },
            new Milestone { id = "m2500", followers = 2500, titleDe = "2.500 Follower", titleEn = "2,500 Followers",
                descDe = "Doppelflasche · Wrack ruft", descEn = "Twin tanks · wreck calling" },
            new Milestone { id = "m5000", followers = 5000, titleDe = "5.000 Follower", titleEn = "5,000 Followers",
                descDe = "Sponsoren zahlen täglich", descEn = "Sponsors pay daily" },
            new Milestone { id = "m10000", followers = 10000, titleDe = "10.000 Follower", titleEn = "10,000 Followers",
                descDe = "RIB + Doppel-Strobes im Shop", descEn = "RIB + twin strobes in shop" },
            new Milestone { id = "m25000", followers = 25000, titleDe = "25.000 Follower", titleEn = "25,000 Followers",
                descDe = "Tauchscooter + Profi-Kamera", descEn = "Dive scooter + pro camera" },
            new Milestone { id = "m50000", followers = 50000, titleDe = "50.000 Follower", titleEn = "50,000 Followers",
                descDe = "Ein Doku-Sender schaut zu", descEn = "A doc channel is watching" },
            new Milestone { id = "m100000", followers = 100000, titleDe = "100.000 Follower", titleEn = "100,000 Followers",
                descDe = "Der Tiefseegraben ruft", descEn = "The deep trench is calling" },
            new Milestone { id = "m250000", followers = 250000, titleDe = "250.000 Follower", titleEn = "250,000 Followers",
                descDe = "Kino-Rig 6K im Shop", descEn = "6K cinema rig in the shop" },
            new Milestone { id = "m500000", followers = 500000, titleDe = "500.000 Follower", titleEn = "500,000 Followers",
                descDe = "Halbe Million – Respekt!", descEn = "Half a million – respect!" },
            new Milestone { id = "m1000000", followers = FinaleFollowers, titleDe = "1.000.000 Follower!", titleEn = "1,000,000 Followers!",
                descDe = "Eine echte Sealebrity!", descEn = "A true Sealebrity!" },
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
                    GameEvents.RaiseBanner(m.BannerText, new Color(1f, 0.76f, 0.2f));
                    if (m.followers >= FinaleFollowers) s.finaleReached = true;
                    Achievements.OnFollowerMilestone(m.followers);
                }
            }
            CheckUnlocks();
            Cosmetics.CheckRules();
        }

        // ------------------------------------------------------------------ dive sites and sponsors
        /// L.1 spawn gate of a species: its homeSite is unlocked (null = always) and its story flag is set.
        public static bool IsGateOpen(SpeciesDef def)
        {
            if (def == null || GameManager.I == null) return false;
            if (!string.IsNullOrEmpty(def.unlockFlag) && !StoryFlags.Has(def.unlockFlag)) return false;
            if (string.IsNullOrEmpty(def.homeSite) || def.homeSite == "house_reef") return true;
            return GameManager.I.State.unlockedSites.Contains(def.homeSite);
        }

        public static bool SiteRequirementsMet(DiveSite site, out string reason)
        {
            var s = GameManager.I.State;
            reason = null;
            if (s.unlockedSites.Contains(site.id)) return true;
            if (site.IsStorySite)
            {
                // K.8: the Fog Isle opens only with Toni's map (no Sealog/follower rule)
                if (StoryFlags.Has(site.unlockFlag)) return true;
                reason = Loc.Tr("Dichter Nebel – ohne Karte kommst du nicht durch.", "Thick fog – you won't get through without a map.");
                return false;
            }
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

        public static bool IsSiteUnlocked(DiveSite site) => site != null && GameManager.I != null && GameManager.I.State.unlockedSites.Contains(site.id);

        public static bool IsSiteUnlocked(string siteId) => GameManager.I != null && siteId != null && GameManager.I.State.unlockedSites.Contains(siteId);

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
                    if (site.IsStorySite)
                        GameEvents.RaiseNotify(NotifyKind.Story, Loc.Tr("Neue Insel auf der Seekarte!", "New island on the sea chart!"), site.Name);
                    else if (site.prevSiteId != null)
                        GameEvents.RaiseBanner(Loc.Tr("Neuer Tauchplatz: ", "New dive site: ") + site.Name, new Color(0.18f, 0.77f, 0.75f));
                }
                if (Journal.PageCompletion(site.id) >= 1f && !s.sponsorSites.Contains(site.id))
                {
                    s.sponsorSites.Add(site.id);
                    GameEvents.RaiseSponsorUnlocked(site.id);
                    // L.10: icon + one short line + chip
                    GameEvents.RaiseNotify(NotifyKind.Sponsor, Loc.Tr($"{site.Name} komplett!", $"{site.Name} complete!"), Loc.Tr("Sponsor", "Sponsor"));
                }
            }
            if (Journal.IsComplete) Achievements.Unlock("golden_camera");
        }
    }
}
