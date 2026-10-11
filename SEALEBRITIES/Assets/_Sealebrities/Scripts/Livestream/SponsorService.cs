using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Sealebrities
{
    /// K.7 sponsor deals – the livestream module's half (core keeps the state and the accept/cancel/complete rules in
    /// Core/Deals.cs):
    /// * HOST: creates offers from Data/SponsorDatabase.cs (every morning, when a sponsor tier unlocks), checks the
    ///   conditions, completes (reward) or fails (angry mail) deals. Host-only facts: fish caught, uploads (title style,
    ///   forbidden / required animals, "upload that day"), chopper orders, stream minutes.
    /// * EVERY MACHINE: watches what its own player does (clips, Sealog photos, depth/night seconds, site visits,
    ///   selfies, snacks, filming the chopper) and reports it – clients through NetCommand.Custom "SponsorProgress".
    ///   Progress counts all players.
    /// * Quest line (L.10): the most urgent active deal is the QuestSource.Deal entry.
    public sealed class SponsorService : MonoBehaviour
    {
        public const string ProgressKey = "SponsorProgress";
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static SponsorService Instance { get; private set; }

        /// The stream eye must scan for a deal (crowd clips, filming seconds) even without a livestream.
        public static bool NeedsEye;

        // host: distinct species per deal instance, upload days counted, stream seconds buffer
        readonly Dictionary<string, HashSet<string>> distinct = new Dictionary<string, HashSet<string>>();
        readonly Dictionary<string, int> countedDay = new Dictionary<string, int>();
        readonly Dictionary<string, float> streamBuffer = new Dictionary<string, float>();
        // local: seconds not yet reported, one-shot reports
        readonly Dictionary<string, float> localSeconds = new Dictionary<string, float>();
        readonly HashSet<string> reportedOnce = new HashSet<string>();

        long lastFollowers = -1;
        float slowTimer, chopperTimer, streamFlushTimer;
        string questKey, shownId, shownTemplate;
        int lastDoneCount;
        Language questLang;
        bool questDirty = true;

        static bool Alive(object o) => o != null && (!(o is UnityEngine.Object uo) || uo != null);
        static GameState S => GameManager.I != null && GameManager.I.InGame ? GameManager.I.State : null;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void OnEnable()
        {
            GameEvents.DayStarted += OnDayStarted;
            GameEvents.FollowersChanged += OnFollowersChanged;
            GameEvents.GameStarted += OnGameStarted;
            GameEvents.ReturnedToMenu += OnReturnedToMenu;
            GameEvents.FishCaught += OnFishCaught;
            GameEvents.UploadCreated += OnUploadCreated;
            GameEvents.ClipRecorded += OnClipRecorded;
            GameEvents.SealogPhoto += OnSealogPhoto;
            GameEvents.FoodEaten += OnFoodEaten;
            GameEvents.DealChanged += OnDealChanged;
            GameEvents.LanguageChanged += MarkQuestDirty;
            Deals.Changed += MarkQuestDirty;
            Net.StateSynced += MarkQuestDirty;
        }

        void OnDisable()
        {
            GameEvents.DayStarted -= OnDayStarted;
            GameEvents.FollowersChanged -= OnFollowersChanged;
            GameEvents.GameStarted -= OnGameStarted;
            GameEvents.ReturnedToMenu -= OnReturnedToMenu;
            GameEvents.FishCaught -= OnFishCaught;
            GameEvents.UploadCreated -= OnUploadCreated;
            GameEvents.ClipRecorded -= OnClipRecorded;
            GameEvents.SealogPhoto -= OnSealogPhoto;
            GameEvents.FoodEaten -= OnFoodEaten;
            GameEvents.DealChanged -= OnDealChanged;
            GameEvents.LanguageChanged -= MarkQuestDirty;
            Deals.Changed -= MarkQuestDirty;
            Net.StateSynced -= MarkQuestDirty;
        }

        /// Hooks the core facade (sponsor names / deal titles) and the client command.
        public static void InstallHooks()
        {
            Deals.SponsorName = id => SponsorDatabase.GetSponsor(id)?.Name;
            Deals.DealTitle = id => SponsorDatabase.Get(id)?.Title;
            CoreCommands.RegisterCustom(ProgressKey, (from, a) =>
            {
                var svc = Instance;
                if (svc == null || a == null || a.Length < 3) return false;
                float.TryParse(a[2], NumberStyles.Float, Inv, out float amount);
                if (float.IsNaN(amount) || float.IsInfinity(amount)) return false;
                return svc.HostAddProgress(a[1], Mathf.Clamp(amount, 0f, 600f), a.Length > 3 ? a[3] : null);
            });
        }

        void OnGameStarted()
        {
            distinct.Clear(); countedDay.Clear(); streamBuffer.Clear(); localSeconds.Clear(); reportedOnce.Clear();
            var s = S;
            lastFollowers = s != null ? s.followers : -1;
            lastDeliveryCount = s != null ? s.deliveries.Count : -1;
            lastDoneCount = s != null ? s.sponsor.done.Count : 0;
            shownId = null;
            questKey = null;
            questDirty = true;
        }

        void OnReturnedToMenu()
        {
            OnGameStarted();
            QuestTracker.SetEntry(QuestSource.Deal, null, null, null, null);
        }

        void MarkQuestDirty() => questDirty = true;
        void OnDealChanged(string id) => questDirty = true;

        // ================================================================ helpers
        static DealState FindActive(string id)
        {
            var s = S;
            if (s == null || id == null) return null;
            var a = s.sponsor.active;
            for (int i = 0; i < a.Count; i++) if (a[i] != null && a[i].id == id) return a[i];
            return null;
        }

        static bool HasActive(string dealId)
        {
            var s = S;
            if (s == null) return false;
            var a = s.sponsor.active;
            for (int i = 0; i < a.Count; i++) if (a[i] != null && a[i].dealId == dealId) return true;
            var o = s.sponsor.offers;
            for (int i = 0; i < o.Count; i++) if (o[i] != null && o[i].dealId == dealId) return true;
            return false;
        }

        static bool Reached(DealDef def, DealState d)
        {
            if (def.condition == DealCondition.OrderAndFilm) return PartBits(d.progress) == 3;
            return d.progress >= def.target - 0.001f;
        }

        // ================================================================ host: offers
        void OnDayStarted(int day)
        {
            if (!Net.HasAuthority) return;
            var s = S;
            if (s == null || s.followers < Progression.LocalSponsorFollowers) return;
            if (OfferedToday(s)) return;   // a reload of the same morning does not add more
            int open = s.sponsor.offers.Count;
            int n = open < SponsorDatabase.KeepOpenOffers ? SponsorDatabase.KeepOpenOffers - open : SponsorDatabase.OffersPerDay;
            GenerateOffers(n, null);
        }

        static bool OfferedToday(GameState s)
        {
            var o = s.sponsor.offers;
            for (int i = 0; i < o.Count; i++) if (o[i] != null && o[i].offeredDay == s.day) return true;
            var a = s.sponsor.active;
            for (int i = 0; i < a.Count; i++) if (a[i] != null && a[i].offeredDay == s.day) return true;
            return false;
        }

        void OnFollowersChanged(long followers)
        {
            long prev = lastFollowers;
            lastFollowers = followers;
            if (!Net.HasAuthority || prev < 0 || followers <= prev) return;
            // a sponsor tier unlocks: its first offer comes right away ("Neues Angebot")
            if (prev < Progression.LocalSponsorFollowers && followers >= Progression.LocalSponsorFollowers) GenerateOffers(1, SponsorTier.Local);
            if (prev < Progression.NationalSponsorFollowers && followers >= Progression.NationalSponsorFollowers) GenerateOffers(1, SponsorTier.National);
            if (prev < Progression.BigSponsorFollowers && followers >= Progression.BigSponsorFollowers) GenerateOffers(1, SponsorTier.Big);
        }

        /// Host: adds up to n offers (only from `tier` when given). Weighted toward the newest unlocked tier.
        public int GenerateOffers(int n, SponsorTier? tier)
        {
            var s = S;
            if (s == null || !Net.HasAuthority || n <= 0) return 0;
            long F = s.followers;
            var rnd = new System.Random(unchecked(s.saveSeed * 31 + s.day * 977 + s.sponsor.nextId * 131 + (tier.HasValue ? (int)tier.Value + 7 : 0)));
            int added = 0;
            var all = SponsorDatabase.All;
            for (int k = 0; k < n && s.sponsor.offers.Count < Deals.MaxOffers; k++)
            {
                float total = 0f;
                for (int i = 0; i < all.Count; i++) total += OfferWeight(all[i], F, tier, s);
                if (total <= 0f) break;
                float r = (float)rnd.NextDouble() * total;
                DealDef pick = null;
                for (int i = 0; i < all.Count; i++)
                {
                    float w = OfferWeight(all[i], F, tier, s);
                    if (w <= 0f) continue;
                    if (r < w) { pick = all[i]; break; }
                    r -= w;
                }
                if (pick == null) break;
                if (AddOffer(pick, F)) added++;
                else break;
            }
            return added;
        }

        static float OfferWeight(DealDef d, long F, SponsorTier? onlyTier, GameState s)
        {
            var sp = d.Sponsor;
            if (sp == null) return 0f;
            if (onlyTier.HasValue && sp.tier != onlyTier.Value) return 0f;
            if (F < SponsorDatabase.FollowersFor(sp.tier)) return 0f;
            if (Deals.IsSulking(sp.id) || HasActive(d.id)) return 0f;
            if (!string.IsNullOrEmpty(d.requiresSite) && !Progression.IsSiteUnlocked(d.requiresSite)) return 0f;
            if (d.condition == DealCondition.StreamMinutes && F < StreamRules.UnlockFollowers) return 0f;
            if (!SponsorDatabase.AnyMatchingSpeciesAvailable(d)) return 0f;
            if (d.requireSpecies != null)
            {
                var req = SpeciesDatabase.Get(d.requireSpecies);
                if (req == null || !Progression.IsGateOpen(req)) return 0f;
            }
            float w;
            switch (sp.tier)
            {
                case SponsorTier.Local: w = F < Progression.NationalSponsorFollowers ? 1f : 0.35f; break;
                case SponsorTier.National: w = F < Progression.BigSponsorFollowers ? 1f : 0.6f; break;
                default: w = 1.2f; break;
            }
            if (s.sponsor.done.Contains(d.id)) w *= 0.5f;
            return w;
        }

        bool AddOffer(DealDef def, long F)
        {
            var offer = new DealState
            {
                dealId = def.id,
                sponsorId = def.sponsorId,
                tags = def.tags != null ? (string[])def.tags.Clone() : new string[0],
                reward = SponsorDatabase.Reward(def, F),
                days = Mathf.Max(1, def.days),
                target = def.target,
                progress = 0f,
            };
            return Deals.AddOffer(offer);
        }

        /// Test hook: offers a specific deal right now (ignores tier and gates).
        public static bool DebugOffer(string dealId)
        {
            var def = SponsorDatabase.Get(dealId);
            var s = S;
            var svc = Instance;
            if (def == null || s == null || svc == null || !Net.HasAuthority) return false;
            return svc.AddOffer(def, s.followers);
        }

        // ================================================================ host: progress
        /// Host: adds progress to an active deal (local reports or a client's SponsorProgress command).
        public bool HostAddProgress(string id, float amount, string speciesId)
        {
            if (!Net.HasAuthority) return false;
            var d = FindActive(id);
            var def = d != null ? SponsorDatabase.Get(d.dealId) : null;
            if (def == null) return false;
            float p = d.progress;
            if (def.condition == DealCondition.OrderAndFilm) p = EncodeParts(PartBits(p) | (Mathf.RoundToInt(amount) & 3));
            else if (def.distinct)
            {
                if (string.IsNullOrEmpty(speciesId)) return false;
                if (!distinct.TryGetValue(id, out var set)) { set = new HashSet<string>(); distinct[id] = set; }
                if (!set.Add(speciesId)) return true;
                p += 1f;
            }
            else p += amount;
            if (def.condition != DealCondition.OrderAndFilm) p = Mathf.Min(p, def.target);
            Deals.SetProgress(id, p, def.target);
            if (Reached(def, d) && !def.needsUpload) CompleteDeal(d);
            questDirty = true;
            return true;
        }

        static void CompleteDeal(DealState d)
        {
            QuestTracker.Complete(d.id);
            Deals.Complete(d.id);
        }

        void OnFishCaught(int value)
        {
            if (!Net.HasAuthority) return;
            ForEachActive(DealCondition.CatchFish, (d, def) => HostAddProgress(d.id, 1f, null));
        }

        /// Host: a new chopper order (gear or snack pack – both add a Delivery) counts for Wanda's deal.
        void CheckNewOrders(GameState s)
        {
            int n = s.deliveries.Count;
            if (lastDeliveryCount >= 0 && n > lastDeliveryCount)
                ForEachActive(DealCondition.OrderAndFilm, (d, def) => HostAddProgress(d.id, 1f, null));
            lastDeliveryCount = n;
        }

        int lastDeliveryCount = -1;

        /// Host: every upload (own and guests' – CreateUpload runs on the host).
        void OnUploadCreated(PendingUpload up)
        {
            var s = S;
            if (!Net.HasAuthority || s == null || up == null) return;
            var active = s.sponsor.active;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var d = i < active.Count ? active[i] : null;
                var def = d != null ? SponsorDatabase.Get(d.dealId) : null;
                if (def == null) continue;
                switch (def.condition)
                {
                    case DealCondition.NoTagInUpload:
                        if (UploadHasTag(up, def.forbidTag)) { Deals.Fail(d.id); continue; }
                        if (UploadHasSpecies(up, def.requireSpecies)) { Deals.SetProgress(d.id, 1f, 1f); CompleteDeal(d); }
                        break;
                    case DealCondition.UploadStyle:
                        if (up.titleStyle != (int)def.style) break;
                        if (countedDay.TryGetValue(d.id, out int day) && day == up.dayUploaded) break;
                        countedDay[d.id] = up.dayUploaded;
                        HostAddProgress(d.id, 1f, null);
                        break;
                    case DealCondition.EatFood:
                        if (Reached(def, d)) CompleteDeal(d);
                        break;
                    default:
                        if (def.needsUpload && Reached(def, d) && UploadHasMatch(up, def)) CompleteDeal(d);
                        break;
                }
            }
            questDirty = true;
        }

        static bool UploadHasTag(PendingUpload up, string tag)
        {
            if (string.IsNullOrEmpty(tag) || up.clips == null) return false;
            for (int i = 0; i < up.clips.Count; i++) if (up.clips[i] != null && SponsorDatabase.TagMatches(up.clips[i].Species, tag)) return true;
            return false;
        }

        static bool UploadHasSpecies(PendingUpload up, string id)
        {
            if (string.IsNullOrEmpty(id) || up.clips == null) return false;
            for (int i = 0; i < up.clips.Count; i++) if (up.clips[i] != null && up.clips[i].speciesId == id) return true;
            return false;
        }

        static bool UploadHasMatch(PendingUpload up, DealDef def)
        {
            if (def.species == null && def.plans == null && !def.landOnly) return true;
            if (up.clips == null) return false;
            for (int i = 0; i < up.clips.Count; i++) if (up.clips[i] != null && SponsorDatabase.Matches(def, up.clips[i].Species)) return true;
            return false;
        }

        /// Host, every frame of a stream: seconds with enough viewers (Blubber Energy).
        public static void OnStreamTick(float dt, int viewers)
        {
            var svc = Instance;
            var s = S;
            if (svc == null || s == null || !Net.HasAuthority) return;
            var active = s.sponsor.active;
            for (int i = 0; i < active.Count; i++)
            {
                var d = active[i];
                var def = d != null ? SponsorDatabase.Get(d.dealId) : null;
                if (def == null || def.condition != DealCondition.StreamMinutes || viewers < def.minViewers) continue;
                svc.streamBuffer.TryGetValue(d.id, out float b);
                svc.streamBuffer[d.id] = b + dt;
            }
            svc.streamFlushTimer += dt;
            if (svc.streamFlushTimer >= 2f) svc.FlushStream();
        }

        public static void OnStreamEnded(bool fainted)
        {
            var svc = Instance;
            if (svc != null) svc.FlushStream();
        }

        void FlushStream()
        {
            streamFlushTimer = 0f;
            if (streamBuffer.Count == 0) return;
            foreach (var kv in streamBuffer)
                if (kv.Value > 0f) pendingFlush.Add(kv);
            streamBuffer.Clear();
            for (int i = 0; i < pendingFlush.Count; i++) HostAddProgress(pendingFlush[i].Key, pendingFlush[i].Value, null);
            pendingFlush.Clear();
        }

        readonly List<KeyValuePair<string, float>> pendingFlush = new List<KeyValuePair<string, float>>(2);

        void ForEachActive(DealCondition c, Action<DealState, DealDef> act)
        {
            var s = S;
            if (s == null) return;
            var active = s.sponsor.active;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var d = i < active.Count ? active[i] : null;
                var def = d != null ? SponsorDatabase.Get(d.dealId) : null;
                if (def != null && def.condition == c) act(d, def);
            }
        }

        // ================================================================ local reports (each machine, own player)
        /// Reports what the LOCAL player did to the host (or applies it on the host itself).
        void Report(DealState d, float amount, string speciesId)
        {
            if (d == null) return;
            if (Net.HasAuthority) HostAddProgress(d.id, amount, speciesId);
            else Net.Command(NetCommand.Custom, ProgressKey, d.id, amount.ToString("0.###", Inv), speciesId ?? "");
        }

        static float LocalDepth()
        {
            var p = Refs.Player;
            return Alive(p) ? p.Depth : 0f;
        }

        static string LocalSite()
        {
            var p = Refs.Player;
            if (!Alive(p) || p.Root == null) return null;
            var site = WorldInfo.SiteAt(p.Root.position);
            return site != null ? site.id : null;
        }

        /// A clip of the local player (Footage.AddClip).
        void OnClipRecorded(FootageClip c)
        {
            var s = S;
            if (s == null || c == null) return;
            var sp = c.Species;
            if (sp == null) return;
            var active = s.sponsor.active;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var d = i < active.Count ? active[i] : null;
                var def = d != null ? SponsorDatabase.Get(d.dealId) : null;
                if (def == null || Reached(def, d)) continue;
                switch (def.condition)
                {
                    case DealCondition.FilmSpecies:
                        if (def.seconds || def.minCrowd > 0) break;   // eye-based (see Update)
                        if (!SponsorDatabase.Matches(def, sp)) break;
                        float depth = c.depth > 0f ? c.depth : LocalDepth();
                        if (def.minDepth > 0f && depth < def.minDepth) break;
                        if (def.site != null && c.siteId != def.site && LocalSite() != def.site) break;
                        Report(d, 1f, sp.id);
                        break;
                    case DealCondition.SelfieWith:
                        if (!c.selfie || !SponsorDatabase.Matches(def, sp) || c.crowd < def.minCrowd) break;
                        Report(d, 1f, sp.id);
                        break;
                }
            }
        }

        /// A Sealog photo. On the host a guest's photo arrives through its command – the guest reports it itself.
        void OnSealogPhoto(PhotoEntry photo, SpeciesDef sp)
        {
            var s = S;
            if (s == null || sp == null) return;
            if (Net.IsHost && NetCommands.ScopeFrom > 0) return;
            var active = s.sponsor.active;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var d = i < active.Count ? active[i] : null;
                var def = d != null ? SponsorDatabase.Get(d.dealId) : null;
                if (def == null || Reached(def, d)) continue;
                switch (def.condition)
                {
                    case DealCondition.PhotoTag:
                        if (!SponsorDatabase.Matches(def, sp)) break;
                        if (def.site != null && LocalSite() != def.site) break;
                        if (def.minDepth > 0f && LocalDepth() < def.minDepth) break;
                        if (def.inWreck && !LocalInWreck()) break;
                        Report(d, 1f, sp.id);
                        break;
                    case DealCondition.SelfieWith:
                        if (!SelfieState.Active || !SponsorDatabase.Matches(def, sp)) break;
                        if (def.minCrowd > 0 && ViewfinderInfo.DetectedCount < def.minCrowd + 1) break;
                        Report(d, 1f, sp.id);
                        break;
                }
            }
        }

        static bool LocalInWreck()
        {
            var p = Refs.Player;
            if (!Alive(p)) return false;
            Camera cam = p.View;
            Vector3 pos = cam != null ? cam.transform.position : (p.Root != null ? p.Root.position : Vector3.zero);
            return StreamEye.InsideWreck(pos);
        }

        void OnFoodEaten(FoodDef f)
        {
            if (f == null) return;
            if (Net.IsHost && NetCommands.ScopeFrom > 0) return;
            ForEachActive(DealCondition.EatFood, (d, def) => { if (!Reached(def, d)) Report(d, 1f, null); });
        }

        // ================================================================ per frame: timed conditions, chopper, quest line
        void Update()
        {
            var s = S;
            if (s == null) { NeedsEye = false; return; }
            float dt = Time.deltaTime;
            if (!Net.IsOnline && UIState.PauseOpen) dt = 0f;
            var active = s.sponsor.active;
            bool needsEye = false, needsChopper = false;
            var p = Refs.Player;
            bool alive = Alive(p);
            bool under = alive && p.IsUnderwater;
            float depth = alive ? p.Depth : 0f;
            var svc = StreamService.Instance;
            var eye = svc != null ? svc.Eye : null;
            if (Net.HasAuthority) CheckNewOrders(s);

            for (int i = 0; i < active.Count; i++)
            {
                var d = active[i];
                var def = d != null ? SponsorDatabase.Get(d.dealId) : null;
                if (def == null || Reached(def, d)) continue;
                switch (def.condition)
                {
                    case DealCondition.DiveDepth:
                        if (under && depth >= def.minDepth) AddLocalSeconds(d, def, dt);
                        break;
                    case DealCondition.NightDive:
                        if (under && IsNightDiveTime(s.timeOfDay)) AddLocalSeconds(d, def, dt);
                        break;
                    case DealCondition.VisitSite:
                        if (under && def.site != null && !reportedOnce.Contains(d.id) && slowTimer <= 0f && LocalSite() == def.site)
                        {
                            reportedOnce.Add(d.id);
                            Report(d, 1f, null);
                        }
                        break;
                    case DealCondition.OrderAndFilm:
                        if ((PartBits(d.progress) & 2) == 0) needsChopper = true;
                        break;
                    case DealCondition.FilmSpecies:
                        if (def.minCrowd > 0)
                        {
                            needsEye = true;
                            if (eye != null && eye.Valid && eye.Recording && Mathf.Max(eye.Count, ViewfinderInfo.DetectedCount) >= def.minCrowd
                                && !reportedOnce.Contains(d.id))
                            {
                                reportedOnce.Add(d.id);
                                Report(d, 1f, null);
                            }
                        }
                        else if (def.seconds)
                        {
                            needsEye = true;
                            if (eye != null && eye.Valid && eye.Recording && EyeSees(eye, def)) AddLocalSeconds(d, def, dt);
                        }
                        break;
                }
            }
            NeedsEye = needsEye;

            slowTimer -= Time.unscaledDeltaTime;
            if (slowTimer <= 0f) slowTimer = 1f;

            chopperTimer -= Time.unscaledDeltaTime;
            if (needsChopper && chopperTimer <= 0f)
            {
                chopperTimer = 1f;
                CheckChopper(active);
            }

            if (questDirty) UpdateQuestLine(s);
        }

        static bool IsNightDiveTime(float timeOfDay)
        {
            float h = Mathf.Repeat(timeOfDay, 1f) * 24f;
            return h >= 21f || h < 4f;
        }

        static bool EyeSees(StreamEye eye, DealDef def)
        {
            for (int i = 0; i < eye.InViewCount; i++) if (SponsorDatabase.Matches(def, eye.InView[i])) return true;
            return false;
        }

        /// Seconds-based conditions: accumulate locally, report in 5 s steps (or when the target is reached).
        void AddLocalSeconds(DealState d, DealDef def, float dt)
        {
            if (dt <= 0f) return;
            localSeconds.TryGetValue(d.id, out float acc);
            acc += dt;
            float remaining = def.target - d.progress;
            if (acc >= 5f || acc >= remaining)
            {
                Report(d, acc, null);
                acc = 0f;
            }
            localSeconds[d.id] = acc;
        }

        /// Wanda's deal: the chopper on camera while recording.
        void CheckChopper(List<DealState> active)
        {
            var p = Refs.Player;
            var hc = Refs.HandCamera;
            if (!Alive(p) || hc == null || (hc is UnityEngine.Object ho && ho == null) || !hc.IsRecording) return;
            Camera cam = p.View;
            if (cam == null) return;
            var heli = UnityEngine.Object.FindAnyObjectByType<HeliChopper>();
            if (heli == null) return;
            Vector3 hp = heli.transform.position;
            if ((hp - cam.transform.position).sqrMagnitude > 180f * 180f) return;
            Vector3 vp = cam.WorldToViewportPoint(hp);
            if (vp.z <= 0f || vp.x < 0.05f || vp.x > 0.95f || vp.y < 0.05f || vp.y > 0.95f) return;
            for (int i = 0; i < active.Count; i++)
            {
                var d = active[i];
                var def = d != null ? SponsorDatabase.Get(d.dealId) : null;
                if (def == null || def.condition != DealCondition.OrderAndFilm || (PartBits(d.progress) & 2) != 0) continue;
                if (reportedOnce.Contains(d.id)) continue;
                reportedOnce.Add(d.id);
                Report(d, 2f, null);
                GameEvents.RaiseNotify(NotifyKind.Sponsor, Loc.Tr("Heli im Kasten!", "Chopper on camera!"), d.sponsorId != null ? Deals.NameOf(d.sponsorId) : null);
            }
        }

        // ================================================================ quest line (L.10)
        void UpdateQuestLine(GameState s)
        {
            questDirty = false;
            // co-op guests: the host completed the shown deal → the check mark here too (the host calls Complete itself)
            var done = s.sponsor.done;
            if (!Net.HasAuthority && shownId != null && done.Count > lastDoneCount && done.Count > 0 && done[done.Count - 1] == shownTemplate
                && FindActive(shownId) == null)
                QuestTracker.Complete(shownId);
            lastDoneCount = done.Count;
            DealState show = null;
            var active = s.sponsor.active;
            for (int i = 0; i < active.Count; i++)
            {
                var d = active[i];
                if (d == null) continue;
                if (d.id == s.trackedQuest) { show = d; break; }
                if (show == null || d.deadlineDay < show.deadlineDay) show = d;
            }
            if (show == null)
            {
                questKey = null;
                shownId = null;
                QuestTracker.SetEntry(QuestSource.Deal, null, null, null, null);
                return;
            }
            var def = SponsorDatabase.Get(show.dealId);
            if (def == null) return;
            shownId = show.id;
            shownTemplate = show.dealId;
            int prog = Mathf.FloorToInt(show.progress + 0.001f);
            string key = show.id + "|" + prog;
            if (key == questKey && questLang == Loc.Current) return;
            questKey = key;
            questLang = Loc.Current;
            Vector3? target = null;
            var ds = def.site != null ? DiveSites.Get(def.site) : null;
            if (ds != null) target = ds.center;
            QuestTracker.SetEntry(QuestSource.Deal, show.id, def.Title, ProgressText(def, show), target, NotifyKind.Sponsor);
        }

        /// "2/3", "40/60 s", "Hochladen!" – shared with the Deals app.
        public static string ProgressText(DealDef def, DealState d)
        {
            if (def == null || d == null) return null;
            if (def.condition == DealCondition.OrderAndFilm) return Mathf.FloorToInt(d.progress) + "/2";
            if (def.needsUpload && d.progress >= def.target - 0.001f) return Loc.Tr("Hochladen!", "Upload!");
            if (def.condition == DealCondition.DiveDepth || def.condition == DealCondition.NightDive || def.condition == DealCondition.StreamMinutes
                || (def.condition == DealCondition.FilmSpecies && def.seconds))
            {
                int have = Mathf.FloorToInt(d.progress), need = Mathf.RoundToInt(def.target);
                if (need >= 120) return (have / 60) + ":" + (have % 60).ToString("00") + " / " + (need / 60) + ":" + (need % 60).ToString("00");
                return have + "/" + need + " s";
            }
            return Mathf.FloorToInt(d.progress + 0.001f) + "/" + Mathf.RoundToInt(def.target);
        }

        /// OrderAndFilm keeps both parts in DealState.progress (saved + synced): the whole number = parts done (every UI
        /// shows "1/2" with target 2), the fraction × 4 = which parts (bit 1 = order, bit 2 = filmed the chopper).
        public static int PartBits(float progress)
        {
            if (float.IsNaN(progress) || progress <= 0f) return 0;
            return Mathf.Clamp(Mathf.RoundToInt((progress - Mathf.Floor(progress)) * 4f), 0, 3);
        }

        public static float EncodeParts(int bits)
        {
            bits &= 3;
            return ((bits & 1) + ((bits >> 1) & 1)) + bits * 0.25f;
        }

        /// Days left of an active deal (the last day counts as 1).
        public static int DaysLeft(DealState d)
        {
            var s = S;
            if (s == null || d == null) return 0;
            return Mathf.Max(1, d.deadlineDay - s.day);
        }
    }
}
