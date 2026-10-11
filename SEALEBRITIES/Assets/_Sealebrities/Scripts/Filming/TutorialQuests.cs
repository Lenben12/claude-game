using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Icon of a quest entry (the phone app "Aufgaben" / toasts show icons instead of long sentences).
    public enum QuestIcon { None, Phone, Fish, Market, Shop, Heli, Camera, Dive, Upload, Sleep, Site, Gear, Star }

    /// The current task as structured data for the phone app "Aufgaben".
    public struct QuestInfo
    {
        public bool active;         // false outside a running game
        public bool tutorial;       // true during the tutorial chain, false for the long-term goals afterwards
        public int step;            // tutorial step 0..StepCount-1 (StepCount when finished)
        public int stepCount;
        public string title;        // short, ≤ ~6 words, may contain a key hint like "[Tab]"
        public string detail;       // one short line: how to do it
        public int progress, goal;  // counter (goal 0 = no counter)
        public string unit;         // quest-line counter suffix (" $", " %") or null
        public QuestIcon icon;
    }

    /// Tutorial chain stored in the SHARED GameState.questIndex (0..8, 9 = finished):
    ///   0 open the phone → 1 catch 5 fish with the rod → 2 list them in the Fischmarkt → 3 buy the first air tank
    ///   (100 $, K.1) → 4 same-day helicopter: open the crate → 5 take the camera (hotbar) → 6 dive at the House Reef
    ///   and PHOTOGRAPH 3 species for the Sealog (K.2 – the camera records by itself, the Sealog needs a photo) →
    ///   7 back on land/boat: upload on FinTube → 8 sleep (the bed works from 18:00, E.3).
    /// After the tutorial it publishes the next long-term goal (deeper tank, next dive site, 1,000,000 followers).
    /// Horst (npc module) delivers each step as a spoken beat (A.3); this class keeps the short task text.
    ///
    /// Publishing: GameEvents.RaiseQuestChanged("<title>\n<detail>") on every change (first line = toast, both lines
    /// = the "Aufgaben" app), the structured TutorialQuests.Current / Changed, and the always-visible quest line
    /// (L.10): QuestTracker entry of source Tutorial while the tutorial runs, source Other for the long-term goal.
    /// Co-op: progression is evaluated where Net.HasAuthority (host / offline) from shared state, so every player's
    /// fish, listings, purchases, Sealog entries and uploads count. The only local conditions (phone opened, camera
    /// taken) are reported by clients through GameManager.SetQuestIndex (the host only accepts forward moves).
    public class TutorialQuests : MonoBehaviour
    {
        public const int StepCount = 9;
        public const int Finished = StepCount;
        public const int FishGoal = 5, ListGoal = 5, SpeciesGoal = 3;
        const string TankId = "tank_t1";
        const float PollInterval = 0.25f;
        const float HintSpacing = 3.5f;

        enum Step { Phone, CatchFish, ListFish, BuyTank, OpenCrate, TakeCamera, FilmSpecies, Upload, Sleep }

        enum TutorialHint { Underwater, Recording, FirstClip, Photo, Count }

        /// The task shown right now (also outside the tutorial).
        public static QuestInfo Current { get; private set; }
        public static event Action<QuestInfo> Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Current = default; Changed = null; }

        bool phoneOpened;
        float pollTimer, hintCooldown;
        string lastText;
        int lastSignature = int.MinValue;
        int lastStepSeen = -1;
        int lastReported = -1;
        int bindingsVersion;
        readonly bool[] hintShown = new bool[(int)TutorialHint.Count];
        readonly Queue<string> pendingHints = new Queue<string>();

        static readonly Color FinishColor = new Color(1f, 0.76f, 0.2f);

        /// Current tutorial step (0..9) or -1 when no game is running.
        public static int CurrentStep => GameManager.I != null && GameManager.I.InGame ? Mathf.Clamp(GameManager.I.State.questIndex, 0, Finished) : -1;

        public static bool TutorialRunning => CurrentStep >= 0 && CurrentStep < Finished;

        // ------------------------------------------------------------------ lifecycle
        void OnEnable()
        {
            GameEvents.GameStarted += OnGameStarted;
            GameEvents.ReturnedToMenu += OnReturnedToMenu;
            GameEvents.LanguageChanged += OnAnyChange;
            GameEvents.PhoneOpened += OnPhoneOpened;
            GameEvents.FishCaught += OnFishCaught;
            GameEvents.DeliveryOrdered += OnDelivery;
            GameEvents.DeliveryReceived += OnDelivery;
            GameEvents.UploadCreated += OnUploadCreated;
            GameEvents.UploadResolved += OnUploadResolved;
            GameEvents.DayStarted += OnDayStarted;
            GameEvents.SpeciesDiscovered += OnSpeciesDiscovered;
            GameEvents.RecordingChanged += OnRecordingChanged;
            GameEvents.ClipRecorded += OnClipRecorded;
            GameEvents.MoneyChanged += OnMoneyChanged;
            GameEvents.FollowersChanged += OnFollowersChanged;
            GameEvents.SiteUnlocked += OnSiteUnlocked;
            GameEvents.GearChanged += OnAnyChange;
            FishMarket.Changed += OnAnyChange;
            Hands.Changed += OnAnyChange;
            Net.StateSynced += OnAnyChange;
            GameInput.BindingsChanged += OnBindingsChanged;
        }

        void OnDisable()
        {
            GameEvents.GameStarted -= OnGameStarted;
            GameEvents.ReturnedToMenu -= OnReturnedToMenu;
            GameEvents.LanguageChanged -= OnAnyChange;
            GameEvents.PhoneOpened -= OnPhoneOpened;
            GameEvents.FishCaught -= OnFishCaught;
            GameEvents.DeliveryOrdered -= OnDelivery;
            GameEvents.DeliveryReceived -= OnDelivery;
            GameEvents.UploadCreated -= OnUploadCreated;
            GameEvents.UploadResolved -= OnUploadResolved;
            GameEvents.DayStarted -= OnDayStarted;
            GameEvents.SpeciesDiscovered -= OnSpeciesDiscovered;
            GameEvents.RecordingChanged -= OnRecordingChanged;
            GameEvents.ClipRecorded -= OnClipRecorded;
            GameEvents.MoneyChanged -= OnMoneyChanged;
            GameEvents.FollowersChanged -= OnFollowersChanged;
            GameEvents.SiteUnlocked -= OnSiteUnlocked;
            GameEvents.GearChanged -= OnAnyChange;
            FishMarket.Changed -= OnAnyChange;
            Hands.Changed -= OnAnyChange;
            Net.StateSynced -= OnAnyChange;
            GameInput.BindingsChanged -= OnBindingsChanged;
        }

        static bool InGame => GameManager.I != null && GameManager.I.InGame;

        // ------------------------------------------------------------------ events
        void ResetSession()
        {
            phoneOpened = false;
            pendingHints.Clear();
            hintCooldown = 1.5f;
            pollTimer = 0f;
            lastReported = -1;
            for (int i = 0; i < hintShown.Length; i++) hintShown[i] = false;
        }

        void OnGameStarted()
        {
            ResetSession();
            lastText = null;
            lastSignature = int.MinValue;
            lastStepSeen = -1;
            Poll(true);
        }

        void OnReturnedToMenu()
        {
            ResetSession();
            lastText = "";
            lastSignature = int.MinValue;
            lastStepSeen = -1;
            Current = default;
            GameEvents.RaiseQuestChanged("");
            Changed?.Invoke(Current);
        }

        void OnAnyChange() { if (InGame) Poll(false); }
        void OnBindingsChanged() { bindingsVersion++; if (InGame) Poll(false); }
        void OnPhoneOpened() { phoneOpened = true; Poll(false); }
        void OnFishCaught(int value) => Poll(false);
        void OnDelivery(GearItem item) => Poll(false);
        void OnUploadCreated(PendingUpload upload) => Poll(false);
        void OnUploadResolved(VideoRecord video) => Poll(false);
        void OnDayStarted(int day) => Poll(false);
        void OnSpeciesDiscovered(SpeciesDef s, float cm, SizeTier tier) => Poll(false);
        void OnFollowersChanged(long f) { if (InGame && GameManager.I.State.questIndex >= Finished) Poll(false); }
        void OnSiteUnlocked(DiveSite site) => Poll(false);

        void OnMoneyChanged(long money)
        {
            if (!InGame) return;
            int q = GameManager.I.State.questIndex;
            if (q == (int)Step.ListFish || q == (int)Step.BuyTank || q >= Finished) Poll(false);
        }

        void OnRecordingChanged(bool rec)
        {
            // K.2: under water the camera records by itself – every animal in frame counts
            if (rec) QueueHint(TutorialHint.Recording, Loc.Tr("Kamera filmt – alles, was schwimmt", "Camera's rolling – everything counts"));
        }

        void OnClipRecorded(FootageClip clip)
        {
            QueueHint(TutorialHint.FirstClip, Loc.Tr("Clip im Kasten – an Land sichern!", "Clip! Get it to land to keep it"));
            Poll(false);
        }

        // ------------------------------------------------------------------ update / poll
        void Update()
        {
            if (!InGame) return;
            float dt = Time.deltaTime;

            // contextual hints, spaced out so they never pile up
            if (hintCooldown > 0f) hintCooldown -= dt;
            if (hintCooldown <= 0f && pendingHints.Count > 0)
            {
                // L.10: one short line per notification
                GameEvents.RaiseNotify(NotifyKind.Info, pendingHints.Dequeue());
                hintCooldown = HintSpacing;
            }

            pollTimer += dt;
            if (pollTimer < PollInterval) return;
            pollTimer = 0f;

            var p = Refs.Player;
            if (p != null && !(p is UnityEngine.Object o && o == null) && p.IsUnderwater)
            {
                QueueHint(TutorialHint.Underwater, Loc.Tr("Luft im Blick behalten!", "Watch your air!"));
                if (!hintShown[(int)TutorialHint.Photo] && GameManager.I.State.questIndex == (int)Step.FilmSpecies && Hands.IsHeld(HoldItem.Camera))
                    QueueHint(TutorialHint.Photo, Loc.Tr("Sealog: ", "Sealog: ") + Key(GameAction.Aim) + Loc.Tr(" zoomen · ", " zoom · ") + Key(GameAction.Use) + Loc.Tr(" Foto", " photo"));
            }

            Poll(false);
        }

        void QueueHint(TutorialHint hint, string text)
        {
            if (!InGame) return;
            if (GameManager.I.State.questIndex >= Finished) return;   // hints only while the tutorial runs
            int i = (int)hint;
            if (hintShown[i]) return;
            hintShown[i] = true;
            pendingHints.Enqueue(text);
        }

        /// Advances (authority) or reports local progress (co-op client), then publishes the current task.
        void Poll(bool forcePublish)
        {
            var gm = GameManager.I;
            if (gm == null || !gm.InGame) return;
            var s = gm.State;
            int step = Mathf.Clamp(s.questIndex, 0, Finished);

            if (Net.HasAuthority)
            {
                int before = step;
                // the tank order vanished without arriving (should not happen) – go back to ordering it
                if (step == (int)Step.OpenCrate && !TankOwned(s) && !TankOrdered(s)) step = (int)Step.BuyTank;
                while (step < Finished && (IsDoneShared(step, s) || IsDoneLocal(step))) step++;
                if (step != before) gm.SetQuestIndex(step);
            }
            else if (step < Finished && step != lastReported && IsDoneLocal(step))
            {
                // co-op client: phone opened / camera taken happen only here – tell the host (forward only)
                lastReported = step;
                gm.SetQuestIndex(step + 1);
                step = Mathf.Clamp(s.questIndex, 0, Finished);
            }

            if (lastStepSeen >= 0 && lastStepSeen < Finished && step >= Finished)
                GameEvents.RaiseBanner(Loc.Tr("Tutorial geschafft! Ziel: 1.000.000 Follower.", "Tutorial complete! Goal: 1,000,000 followers."), FinishColor);
            // A.3 reward: Horst's lure hat (core's cosmetic rule "tutorial" unlocks hat 10 and shows the notification)
            if (step >= Finished && Net.HasAuthority && !StoryFlags.Has(StoryKeys.TutorialDone)) StoryFlags.Set(StoryKeys.TutorialDone);
            // a finished step: check mark + pop on the quest line (UI is local, so every machine does this)
            if (lastStepSeen >= 0 && step > lastStepSeen && step <= Finished) QuestTracker.Complete("tutorial");
            if (step != lastStepSeen) forcePublish = true;
            lastStepSeen = step;

            Publish(step, s, forcePublish);
        }

        void Publish(int step, GameState s, bool force)
        {
            int sig = Signature(step, s);
            if (!force && sig == lastSignature) return;
            lastSignature = sig;

            var info = step >= Finished ? Goal(s) : StepInfo(step, s);
            info.active = true;
            info.step = step;
            info.stepCount = StepCount;
            Current = info;
            FeedQuestLine(step, s, info);

            string text = string.IsNullOrEmpty(info.detail) ? info.title : info.title + "\n" + info.detail;
            if (!force && text == lastText) return;
            lastText = text;
            GameEvents.RaiseQuestChanged(text);
            Changed?.Invoke(info);
        }

        /// L.10 quest line: the tutorial step (source Tutorial, with its how-to as the second line) or, afterwards, the
        /// long-term goal (source Goal – lowest priority: story, side quests, deals and chopper searches go first).
        /// Only called when the task text changed (signature), never per frame.
        static void FeedQuestLine(int step, GameState s, QuestInfo info)
        {
            string line = info.title;
            string prog = null;
            if (info.goal > 0)
            {
                string counter = Counter(info);
                if (line != null && line.EndsWith(counter, StringComparison.Ordinal)) line = line.Substring(0, line.Length - counter.Length);
                prog = info.goal >= 1000 ? Loc.Number(info.progress) + "/" + Loc.Number(info.goal) : info.progress + "/" + info.goal;
                if (!string.IsNullOrEmpty(info.unit)) prog += info.unit;
            }
            if (step >= Finished)
            {
                QuestTracker.SetEntry(QuestSource.Tutorial, null, null, null, null);
                Vector3? goalTarget = null;
                if (info.icon == QuestIcon.Site)
                {
                    var visit = UnvisitedSite(s);
                    if (VillageGoal(s)) goalTarget = WorldLayout.Village;
                    else if (visit != null) goalTarget = new Vector3(visit.center.x, 0f, visit.center.z);
                }
                QuestTracker.SetEntry(QuestSource.Goal, "goal", line, prog, goalTarget, NotifyKind.Info, info.detail);
                return;
            }
            QuestTracker.SetEntry(QuestSource.Goal, null, null, null, null);
            Vector3? target = null;
            switch ((Step)step)
            {
                case Step.CatchFish:
                    target = WorldInfo.FishingSpot != Vector3.zero ? WorldInfo.FishingSpot : WorldLayout.FishingSpot;
                    break;
                case Step.OpenCrate:
                    target = WorldInfo.HelipadPosition != Vector3.zero ? WorldInfo.HelipadPosition : WorldLayout.Helipad;
                    break;
                case Step.FilmSpecies:
                {
                    var reef = DiveSites.Get("house_reef");
                    if (reef != null) target = new Vector3(reef.center.x, 0f, reef.center.z);
                    break;
                }
                case Step.Sleep:
                    target = WorldInfo.BedPosition != Vector3.zero ? WorldInfo.BedPosition : WorldLayout.Bed;
                    break;
            }
            QuestTracker.SetEntry(QuestSource.Tutorial, "tutorial", line, prog, target, NotifyKind.Quest, info.detail);
        }

        // ------------------------------------------------------------------ step conditions
        /// Conditions on SHARED state (every player's actions count). Also lets old/advanced saves skip steps.
        static bool IsDoneShared(int step, GameState s)
        {
            switch ((Step)step)
            {
                // old/advanced saves: an air tank or a video means the fishing lesson is long done (a gull photo on the
                // dock or one snorkel trip must NOT skip the fishing and market lesson)
                case Step.Phone: return s.fishCaught > 0 || s.fishListedTotal > 0 || TankOwned(s) || TankOrdered(s) || s.videos.Count > 0;
                case Step.CatchFish: return s.fishCaught >= FishGoal || TankOwned(s) || TankOrdered(s) || s.videos.Count > 0;
                case Step.ListFish: return s.fishListedTotal >= ListGoal || s.money >= TankPrice || TankOwned(s) || TankOrdered(s) || s.videos.Count > 0;
                case Step.BuyTank: return TankOwned(s) || TankOrdered(s);
                case Step.OpenCrate: return TankOwned(s);
                case Step.TakeCamera: return DiscoveredCount(s) > 0 || s.diveFootage.Count > 0 || s.bankedFootage.Count > 0 || s.pendingUploads.Count > 0 || s.videos.Count > 0;
                case Step.FilmSpecies: return DiscoveredCount(s) >= SpeciesGoal || s.videos.Count > 0;
                case Step.Upload: return s.pendingUploads.Count > 0 || s.videos.Count > 0;
                case Step.Sleep: return s.videos.Count > 0;   // uploads are resolved at the start of a new day
                default: return true;
            }
        }

        /// Conditions that only the local machine can see.
        bool IsDoneLocal(int step)
        {
            switch ((Step)step)
            {
                case Step.Phone: return phoneOpened || UIState.PhoneOpen;
                case Step.TakeCamera: return Hands.IsHeld(HoldItem.Camera);
                default: return false;
            }
        }

        // ------------------------------------------------------------------ texts
        QuestInfo StepInfo(int step, GameState s)
        {
            var q = new QuestInfo { tutorial = true };
            switch ((Step)step)
            {
                case Step.Phone:
                    q.icon = QuestIcon.Phone;
                    q.title = Loc.Tr("Handy öffnen ", "Open your phone ") + Key(GameAction.Phone);
                    q.detail = Loc.Tr("Shop, Fischmarkt, FinTube und Aufgaben.", "Shop, fish market, FinTube and tasks.");
                    break;

                case Step.CatchFish:
                    q.icon = QuestIcon.Fish;
                    q.progress = Mathf.Min(FishGoal, s.fishCaught);
                    q.goal = FishGoal;
                    q.title = Loc.Tr($"{FishGoal} Fische angeln", $"Catch {FishGoal} fish") + Counter(q);
                    q.detail = Hands.IsHeld(HoldItem.FishingRod)
                        ? Loc.Tr("Am Stegende auswerfen ", "Cast at the end of the dock ") + Key(GameAction.Use)
                        : Loc.Tr("Angel nehmen ", "Take the rod ") + SlotKey(HoldItem.FishingRod) + Loc.Tr(", am Stegende auswerfen.", ", cast at the end of the dock.");
                    break;

                case Step.ListFish:
                    q.icon = QuestIcon.Market;
                    q.progress = Mathf.Min(ListGoal, s.fishListedTotal);
                    q.goal = ListGoal;
                    q.title = Loc.Tr($"{ListGoal} Fische verkaufen", $"Sell {ListGoal} fish") + Counter(q);
                    q.detail = s.fishInventory.Count > 0
                        ? Loc.Tr("Handy ", "Phone ") + Key(GameAction.Phone) + Loc.Tr(" → Fischmarkt → Anbieten", " → Fish Market → List")
                        : Loc.Tr("Erst weiter angeln, dann im Fischmarkt anbieten.", "Catch more fish, then list them in the Fish Market.");
                    break;

                case Step.BuyTank:
                {
                    q.icon = QuestIcon.Shop;
                    long price = TankPrice;
                    // money counter on the quest line ("45/100 $") – the goal right after selling, not a silent price tag
                    q.progress = (int)Math.Min(Math.Max(0L, s.money), price);
                    q.goal = (int)price;
                    q.unit = " $";
                    q.title = Loc.Tr("Pressluftflasche kaufen", "Buy the air tank");
                    if (s.money < price)
                    {
                        string missing = Loc.Money(price - s.money);
                        q.detail = Loc.Tr($"Noch {missing} – weiter angeln und verkaufen.", $"{missing} to go – keep fishing and selling.");
                    }
                    else q.detail = Loc.Tr("Handy ", "Phone ") + Key(GameAction.Phone) + Loc.Tr(" → Shop", " → Shop");
                    break;
                }

                case Step.OpenCrate:
                    q.icon = QuestIcon.Heli;
                    q.title = Loc.Tr("Paket am Landeplatz öffnen", "Open the crate on the helipad");
                    q.detail = DeliveryHint(s);
                    break;

                case Step.TakeCamera:
                    q.icon = QuestIcon.Camera;
                    if (Hands.Find(HoldItem.Camera, out _, out _))
                    {
                        q.title = Loc.Tr("Kamera nehmen ", "Take the camera ") + SlotKey(HoldItem.Camera);
                        // built from the current bindings (only rebuilt when the signature – incl. bindingsVersion – changes)
                        string r = GameInput.BindingLabel(GameAction.RightSlot1) + "–" + GameInput.BindingLabel(GameAction.RightSlot3);
                        string l = GameInput.BindingLabel(GameAction.LeftSlot1) + "–" + GameInput.BindingLabel(GameAction.LeftSlot3);
                        q.detail = Loc.Tr($"Rechte Hand {r}, linke Hand {l}.", $"Right hand {r}, left hand {l}.");
                    }
                    else
                    {
                        q.title = Loc.Tr("Kamera in die Hotbar ziehen ", "Drag the camera to the hotbar ") + Key(GameAction.Inventory);
                        q.detail = Loc.Tr("Dann mit ihrer Taste in die Hand nehmen.", "Then take it with its key.");
                    }
                    break;

                case Step.FilmSpecies:
                    // K.2: the camera records by itself under water; the Sealog needs a PHOTO of each species
                    q.icon = QuestIcon.Dive;
                    q.progress = Mathf.Min(SpeciesGoal, DiscoveredCount(s));
                    q.goal = SpeciesGoal;
                    q.title = Loc.Tr($"{SpeciesGoal} Arten fürs Sealog fotografieren", $"Photograph {SpeciesGoal} species for the Sealog") + Counter(q);
                    q.detail = Key(GameAction.Down) + Loc.Tr(" runter · ", " down · ")
                               + Key(GameAction.Aim) + "+" + Key(GameAction.Use) + Loc.Tr(" Foto", " photo");
                    if (EnvState.Night01 > 0.3f)
                        q.detail += " · " + SlotKey(HoldItem.Flashlight) + Loc.Tr(" Lampe", " lamp");
                    break;

                case Step.Upload:
                    q.icon = QuestIcon.Upload;
                    q.title = Loc.Tr("Video auf FinTube hochladen", "Upload a video to FinTube");
                    if (s.bankedFootage.Count > 0)
                        q.detail = Loc.Tr("Handy ", "Phone ") + Key(GameAction.Phone) + " → FinTube";
                    else if (s.diveFootage.Count > 0)
                        q.detail = Loc.Tr("An Land/Boot: Clips sichern", "Land/boat: keep your clips");
                    else
                        q.detail = Loc.Tr("Erst ein paar Clips filmen.", "Film a few clips first.");
                    break;

                case Step.Sleep:
                    q.icon = QuestIcon.Sleep;
                    q.title = Loc.Tr("In der Hütte schlafen", "Sleep in the hut");
                    // E.3: the bed works from 18:00 – until then grill or explore
                    q.detail = GameManager.I != null && !GameManager.I.CanSleepNow
                        ? Loc.Tr("Ab 18:00 – bis dahin grillen oder erkunden.", "From 6 pm – grill or explore until then.")
                        : Loc.Tr("Über Nacht kommen die Aufrufe.", "The views come in overnight.");
                    break;
            }
            return q;
        }

        /// Long-term goal after the tutorial: visit the village → first livestream → deeper tank for an unlocked site →
        /// next dive site → 1,000,000 followers. A locked goal shows its REQUIREMENT as the quest-line counter
        /// („Kelpwald: Hausriff-Seite 45/60 %“, „Wrack: Follower 1.200/2.500“) – „Neuer Tauchplatz“ only once it is met.
        static QuestInfo Goal(GameState s)
        {
            var q = new QuestInfo { tutorial = false };
            if (s.finaleReached)
            {
                q.icon = QuestIcon.Star;
                q.title = Loc.Tr("Du bist eine Sealebrity!", "You are a Sealebrity!");
                q.detail = Loc.Tr("Fülle die letzten Sealog-Seiten.", "Fill the last Sealog pages.");
                return q;
            }

            // most side quests live in the village (needVillage) – point there once after the tutorial
            if (VillageGoal(s))
            {
                q.icon = QuestIcon.Site;
                q.title = Loc.Tr("Dorf Altriff besuchen", "Visit Oldreef village");
                q.detail = Loc.Tr("Im Süden – dort gibt es Aufgaben.", "In the south – people need help.");
                return q;
            }

            // Q21: going live earns money – make the first stream a goal as soon as it is possible
            if (s.followers >= Progression.LivestreamFollowers && (s.streams == null || s.streams.Count == 0))
            {
                q.icon = QuestIcon.Star;
                q.title = Loc.Tr("Erster Livestream: Boot → KRILL.tv", "First livestream: boat → KRILL.tv");
                q.detail = Loc.Tr("Zuschauer spenden echtes Geld.", "Viewers donate real money.");
                return q;
            }

            NeededTank(s, out var site, out var tank);
            if (tank != null)
            {
                q.icon = QuestIcon.Gear;
                if (Shop.IsOrdered(tank))
                {
                    q.title = Loc.Tr("Tiefere Flasche: ", "Deeper tank: ") + tank.Name;
                    q.detail = Loc.Tr("Unterwegs – Paket am Landeplatz öffnen.", "On its way – open the crate on the helipad.");
                }
                else if (s.followers < tank.followersNeeded)
                {
                    q.title = tank.Name + Loc.Tr(": Follower", ": followers");
                    q.progress = (int)Math.Min(s.followers, tank.followersNeeded);
                    q.goal = (int)Math.Min(tank.followersNeeded, int.MaxValue);
                    q.detail = Loc.Tr($"Für {site.Name}", $"For {site.Name}");
                }
                else
                {
                    q.title = Loc.Tr("Tiefere Flasche: ", "Deeper tank: ") + tank.Name;
                    q.detail = Loc.Tr($"{site.Name} · Shop · {Loc.Money(tank.price)}", $"{site.Name} · Shop · {Loc.Money(tank.price)}");
                }
                return q;
            }

            // an unlocked site nobody has filmed at yet: teach the boat trip (sea chart, helm, anchor)
            var visit = UnvisitedSite(s);
            if (visit != null)
            {
                q.icon = QuestIcon.Site;
                q.title = Loc.Tr("Mit dem Boot: ", "By boat: ") + visit.Name;
                q.detail = Loc.Tr("Seekarte → Flagge · ", "Sea chart → flag · ") + Key(GameAction.Interact) + Loc.Tr(" Steuern · ", " steer · ")
                           + Key(GameAction.Anchor) + Loc.Tr(" Anker", " anchor");
                return q;
            }

            var next = NextLockedSite(s);
            if (next != null)
            {
                q.icon = QuestIcon.Site;
                if (Progression.SiteRequirementsMet(next, out string reason) || string.IsNullOrEmpty(reason))
                {
                    q.title = Loc.Tr("Neuer Tauchplatz: ", "New dive site: ") + next.Name;
                    q.detail = Loc.Tr("Gleich freigeschaltet!", "Unlocking now!");
                }
                else if (next.prevSiteId != null && Journal.SiteCompletion(next.prevSiteId) < next.prevCompletionNeeded)
                {
                    q.title = next.Name + ": " + PageName(next.prevSiteId) + Loc.Tr("-Seite", " page");
                    q.progress = Mathf.RoundToInt(Journal.SiteCompletion(next.prevSiteId) * 100f);
                    q.goal = Mathf.RoundToInt(next.prevCompletionNeeded * 100f);
                    q.unit = " %";
                    q.detail = reason;
                }
                else
                {
                    q.title = next.Name + Loc.Tr(": Follower", ": followers");
                    q.progress = (int)Math.Min(s.followers, next.followersNeeded);
                    q.goal = (int)Math.Min(next.followersNeeded, int.MaxValue);
                    q.detail = reason;
                }
                return q;
            }

            q.icon = QuestIcon.Star;
            q.title = Loc.Tr("Ziel: 1.000.000 Follower", "Goal: 1,000,000 followers");
            q.detail = Loc.Tr("Volle Sealog-Seiten bringen Sponsoren.", "Full Sealog pages bring sponsors.");
            return q;
        }

        /// After the tutorial, until somebody walked into the village (its side-quest givers).
        static bool VillageGoal(GameState s) => StoryFlags.Has(StoryKeys.TutorialDone) && !StoryFlags.Has(StoryKeys.VillageVisited);

        /// Short Sealog page name of a dive site („Hausriff“, „Wrack“) – falls back to the site name.
        static string PageName(string siteId)
        {
            int i = SealogPages.IndexOf(siteId);
            if (i >= 0) return SealogPages.All[i].Name;
            var d = DiveSites.Get(siteId);
            return d != null ? d.Name : siteId;
        }
        /// The deepest unlocked site the best owned tank cannot reach, and the cheapest tank that can (null when fine).
        static void NeededTank(GameState s, out DiveSite site, out GearItem tank)
        {
            site = null;
            tank = null;
            var best = GearDatabase.BestOwned(s, GearSlot.Tank);
            float depth = best != null ? best.maxDepth : 6f;
            var sites = DiveSites.All;
            for (int i = sites.Count - 1; i >= 0; i--)
            {
                var d = sites[i];
                if (!s.unlockedSites.Contains(d.id)) continue;
                // typical working depth of the site (kelp ~20 m, wreck on the sand ~32 m, drop-off plateau ~40 m, trench ~90 m)
                float need = Mathf.Min(d.maxDepth, d.minDepth + 10f);
                if (need <= depth) break;   // the deepest unlocked site is reachable → all shallower ones too
                var items = GearDatabase.All;
                for (int k = 0; k < items.Count; k++)
                {
                    var g = items[k];
                    if (g.slot != GearSlot.Tank || g.comingSoon || g.maxDepth < need) continue;
                    if (tank == null || g.price < tank.price) tank = g;
                }
                if (tank != null) site = d;
                return;
            }
        }

        /// First unlocked site (except the House Reef) without a single Sealog entry yet – null when all were visited.
        static DiveSite UnvisitedSite(GameState s)
        {
            var sites = DiveSites.All;
            for (int i = 0; i < sites.Count; i++)
            {
                var d = sites[i];
                if (d.id == "house_reef" || !s.unlockedSites.Contains(d.id)) continue;
                if (Journal.SiteCompletion(d.id) <= 0f) return d;
            }
            return null;
        }

        static DiveSite NextLockedSite(GameState s)
        {
            var sites = DiveSites.All;
            for (int i = 0; i < sites.Count; i++)
                if (!sites[i].IsStorySite && !s.unlockedSites.Contains(sites[i].id)) return sites[i];   // the Fog Isle stays secret (K.8)
            return null;
        }

        string DeliveryHint(GameState s)
        {
            Delivery d = Shop.FindDelivery(TankId);
            if (d == null) return Loc.Tr("Der Heli bringt es zum Landeplatz.", "The chopper drops it on the helipad.");
            if (d.dropped && HeliService.DriftsAway(d)) return Loc.Tr("Treibt im Wasser beim Landeplatz!", "Floating in the water near the helipad!");
            if (d.dropped) return Loc.Tr("Das Paket ist da!", "The crate is here!");
            if (Shop.IsDue(d)) return Loc.Tr("Der Heli ist im Anflug!", "The chopper is on its way!");
            string clock = GameManager.Clock(d.arrivalTime);
            if (d.arrivalDay > s.day) return Loc.Tr($"Morgen um {clock} – du kannst schlafen.", $"Tomorrow at {clock} – you can sleep.");
            return Loc.Tr($"Heli kommt um {clock}.", $"Chopper arrives at {clock}.");
        }

        static string Counter(in QuestInfo q) => q.goal > 0 ? $" ({q.progress}/{q.goal})" : "";

        static string Key(GameAction a) => "[" + GameInput.BindingLabel(a) + "]";

        /// Key of the hotbar slot that holds the item, or the inventory key when it is not on the hotbar.
        static string SlotKey(HoldItem item)
        {
            if (!Hands.Find(item, out Hand hand, out int slot)) return Key(GameAction.Inventory);
            var a = (GameAction)((hand == Hand.Right ? (int)GameAction.RightSlot1 : (int)GameAction.LeftSlot1) + slot);
            return Key(a);
        }

        // ------------------------------------------------------------------ change detection (allocation free)
        /// Hash of everything the current text depends on – texts are only rebuilt when it changes.
        int Signature(int step, GameState s)
        {
            unchecked
            {
                int h = step * 397 ^ (int)Loc.Current * 7919 ^ bindingsVersion * 104729;
                if (step >= Finished)
                {
                    h = h * 31 + (s.finaleReached ? 1 : 0);
                    h = h * 31 + (VillageGoal(s) ? 1 : 0) + (s.streams != null && s.streams.Count > 0 ? 2 : 0)
                        + (s.followers >= Progression.LivestreamFollowers ? 4 : 0);
                    NeededTankSig(s, ref h);
                    var visit = UnvisitedSite(s);
                    h = h * 31 + (visit != null ? visit.id.GetHashCode() : 7);
                    var next = NextLockedSite(s);
                    if (next != null)
                    {
                        h = h * 31 + next.id.GetHashCode();
                        bool prevOk = next.prevSiteId == null || Journal.SiteCompletion(next.prevSiteId) >= next.prevCompletionNeeded;
                        h = h * 31 + (prevOk ? 1 : 0) + (s.followers >= next.followersNeeded ? 2 : 0);
                        // the requirement counter on the quest line: page percent and followers (in 10s, clamped)
                        if (next.prevSiteId != null) h = h * 31 + Mathf.RoundToInt(Journal.SiteCompletion(next.prevSiteId) * 100f);
                        h = h * 31 + (int)(Math.Min(s.followers, next.followersNeeded) / 10);
                    }
                    return h;
                }
                switch ((Step)step)
                {
                    case Step.CatchFish:
                        h = h * 31 + Mathf.Min(FishGoal, s.fishCaught);
                        h = h * 31 + (Hands.IsHeld(HoldItem.FishingRod) ? 1 : 0) + SlotSig(HoldItem.FishingRod) * 4;
                        break;
                    case Step.ListFish:
                        h = h * 31 + Mathf.Min(ListGoal, s.fishListedTotal);
                        h = h * 31 + (s.fishInventory.Count > 0 ? 1 : 0);
                        break;
                    case Step.BuyTank:
                        h = h * 31 + (int)Math.Max(0, TankPrice - s.money);
                        break;
                    case Step.OpenCrate:
                    {
                        var d = Shop.FindDelivery(TankId);
                        h = h * 31 + (d == null ? 0 : d.dropped ? 1 : Shop.IsDue(d) ? 2 : 3);
                        if (d != null) h = h * 31 + d.arrivalDay * 1440 + Mathf.FloorToInt(d.arrivalTime * 1440f);
                        h = h * 31 + s.day;
                        break;
                    }
                    case Step.TakeCamera:
                        h = h * 31 + SlotSig(HoldItem.Camera);
                        break;
                    case Step.FilmSpecies:
                        h = h * 31 + Mathf.Min(SpeciesGoal, DiscoveredCount(s));
                        h = h * 31 + (EnvState.Night01 > 0.3f ? 1 + SlotSig(HoldItem.Flashlight) : 0);
                        break;
                    case Step.Upload:
                        h = h * 31 + (s.bankedFootage.Count > 0 ? 1 : 0) + (s.diveFootage.Count > 0 ? 2 : 0);
                        break;
                    case Step.Sleep:
                        h = h * 31 + (GameManager.I != null && GameManager.I.CanSleepNow ? 1 : 0);
                        break;
                }
                return h;
            }
        }

        static void NeededTankSig(GameState s, ref int h)
        {
            NeededTank(s, out var site, out var tank);
            unchecked
            {
                if (tank == null) { h = h * 31 + 1; return; }
                h = h * 31 + tank.id.GetHashCode();
                h = h * 31 + (Shop.IsOrdered(tank) ? 1 : 0) + (s.followers >= tank.followersNeeded ? 2 : 0);
                h = h * 31 + (int)(Math.Min(s.followers, tank.followersNeeded) / 10);
            }
        }

        static int SlotSig(HoldItem item) => Hands.Find(item, out Hand hand, out int slot) ? 1 + (int)hand * 3 + slot : 0;

        // ------------------------------------------------------------------ helpers (allocation free)
        static long TankPrice
        {
            get
            {
                var tank = GearDatabase.Get(TankId);
                return tank != null ? tank.price : 100;   // K.1: the first tank costs 100 $
            }
        }

        static bool TankOwned(GameState s) => s.ownedItems.Contains(TankId);

        static bool TankOrdered(GameState s)
        {
            for (int i = 0; i < s.deliveries.Count; i++)
                if (s.deliveries[i] != null && s.deliveries[i].itemId == TankId) return true;
            return false;
        }

        /// Discovered MARINE species (a gull or goat photo on land does not count as the reef dive lesson).
        static int DiscoveredCount(GameState s)
        {
            int n = 0;
            var j = s.journal;
            for (int i = 0; i < j.Count; i++)
            {
                if (j[i] == null || !j[i].discovered) continue;
                var sp = SpeciesDatabase.Get(j[i].speciesId);
                if (sp != null && sp.land) continue;
                n++;
            }
            return n;
        }
    }
}
