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
        public QuestIcon icon;
    }

    /// Tutorial chain stored in the SHARED GameState.questIndex (0..8, 9 = finished):
    ///   0 open the phone → 1 catch 5 fish with the rod → 2 list them in the Fischmarkt → 3 buy the first air tank →
    ///   4 same-day helicopter: open the crate → 5 take the camera (hotbar) → 6 dive at the House Reef and film
    ///   3 species while aiming → 7 back on land/boat: upload on FinTube → 8 sleep.
    /// After the tutorial it publishes the next long-term goal (deeper tank, next dive site, 1,000,000 followers).
    ///
    /// Publishing: GameEvents.RaiseQuestChanged("<title>\n<detail>") on every change (first line = toast, both lines
    /// = the "Aufgaben" app), plus the structured TutorialQuests.Current / Changed.
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

        enum TutorialHint { Underwater, Recording, FirstClip, Count }

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
            if (rec) QueueHint(TutorialHint.Recording, Loc.Tr("Tier groß und mittig halten", "Keep the animal big and centred"));
        }

        void OnClipRecorded(FootageClip clip)
        {
            QueueHint(TutorialHint.FirstClip, Loc.Tr("Clip! Sicher erst an Land oder auf dem Boot.", "Clip! It's safe once you're on land or the boat."));
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
                GameEvents.RaiseToast(pendingHints.Dequeue());
                hintCooldown = HintSpacing;
            }

            pollTimer += dt;
            if (pollTimer < PollInterval) return;
            pollTimer = 0f;

            var p = Refs.Player;
            if (p != null && !(p is UnityEngine.Object o && o == null) && p.IsUnderwater)
                QueueHint(TutorialHint.Underwater, Loc.Tr("Luft im Blick behalten!", "Watch your air!"));

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

            string text = string.IsNullOrEmpty(info.detail) ? info.title : info.title + "\n" + info.detail;
            if (!force && text == lastText) return;
            lastText = text;
            GameEvents.RaiseQuestChanged(text);
            Changed?.Invoke(info);
        }

        // ------------------------------------------------------------------ step conditions
        /// Conditions on SHARED state (every player's actions count). Also lets old/advanced saves skip steps.
        static bool IsDoneShared(int step, GameState s)
        {
            switch ((Step)step)
            {
                case Step.Phone: return s.fishCaught > 0 || s.fishListedTotal > 0 || TankOwned(s) || TankOrdered(s) || DiscoveredCount(s) > 0;
                case Step.CatchFish: return s.fishCaught >= FishGoal || TankOwned(s) || TankOrdered(s) || DiscoveredCount(s) > 0;
                case Step.ListFish: return s.fishListedTotal >= ListGoal || s.money >= TankPrice || TankOwned(s) || TankOrdered(s) || DiscoveredCount(s) > 0;
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
                    q.title = Loc.Tr("Pressluftflasche kaufen", "Buy the air tank") + " (" + Loc.Money(price) + ")";
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
                    q.icon = QuestIcon.Dive;
                    q.progress = Mathf.Min(SpeciesGoal, DiscoveredCount(s));
                    q.goal = SpeciesGoal;
                    q.title = Loc.Tr($"{SpeciesGoal} Tierarten filmen", $"Film {SpeciesGoal} species") + Counter(q);
                    q.detail = Loc.Tr("Hausriff: ", "House Reef: ") + Key(GameAction.Down) + Loc.Tr(" abtauchen · ", " dive · ")
                               + Key(GameAction.Aim) + Loc.Tr(" zielen · ", " aim · ")
                               + Key(GameAction.Use) + Loc.Tr(" filmen", " record");
                    if (EnvState.Night01 > 0.3f)
                        q.detail += " · " + SlotKey(HoldItem.Flashlight) + Loc.Tr(" Lampe", " lamp");
                    break;

                case Step.Upload:
                    q.icon = QuestIcon.Upload;
                    q.title = Loc.Tr("Video auf FinTube hochladen", "Upload a video to FinTube");
                    if (s.bankedFootage.Count > 0)
                        q.detail = Loc.Tr("Handy ", "Phone ") + Key(GameAction.Phone) + " → FinTube";
                    else if (s.diveFootage.Count > 0)
                        q.detail = Loc.Tr("Zurück an Land oder aufs Boot – dann sind die Clips sicher.", "Back to land or the boat – then your clips are safe.");
                    else
                        q.detail = Loc.Tr("Erst ein paar Clips filmen.", "Film a few clips first.");
                    break;

                case Step.Sleep:
                    q.icon = QuestIcon.Sleep;
                    q.title = Loc.Tr("In der Hütte schlafen", "Sleep in the hut");
                    q.detail = Loc.Tr("Über Nacht kommen die Aufrufe.", "The views come in overnight.");
                    break;
            }
            return q;
        }

        /// Long-term goal after the tutorial: deeper tank for an unlocked site → next dive site → 1,000,000 followers.
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

            NeededTank(s, out var site, out var tank);
            if (tank != null)
            {
                q.icon = QuestIcon.Gear;
                q.title = Loc.Tr("Tiefere Flasche: ", "Deeper tank: ") + tank.Name;
                if (Shop.IsOrdered(tank))
                    q.detail = Loc.Tr("Unterwegs – Paket am Landeplatz öffnen.", "On its way – open the crate on the helipad.");
                else if (s.followers < tank.followersNeeded)
                    q.detail = Loc.Tr($"{site.Name} · ab {Loc.Number(tank.followersNeeded)} Followern", $"{site.Name} · from {Loc.Number(tank.followersNeeded)} followers");
                else
                    q.detail = Loc.Tr($"{site.Name} · Shop · {Loc.Money(tank.price)}", $"{site.Name} · Shop · {Loc.Money(tank.price)}");
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
                q.title = Loc.Tr("Neuer Tauchplatz: ", "New dive site: ") + next.Name;
                q.detail = Progression.SiteRequirementsMet(next, out string reason) || string.IsNullOrEmpty(reason)
                    ? Loc.Tr("Gleich freigeschaltet!", "Unlocking now!")
                    : reason;
                return q;
            }

            q.icon = QuestIcon.Star;
            q.title = Loc.Tr("Ziel: 1.000.000 Follower", "Goal: 1,000,000 followers");
            q.detail = Loc.Tr("Volle Sealog-Seiten bringen Sponsoren.", "Full Sealog pages bring sponsors.");
            return q;
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
                if (!s.unlockedSites.Contains(sites[i].id)) return sites[i];
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
                    NeededTankSig(s, ref h);
                    var visit = UnvisitedSite(s);
                    h = h * 31 + (visit != null ? visit.id.GetHashCode() : 7);
                    var next = NextLockedSite(s);
                    if (next != null)
                    {
                        h = h * 31 + next.id.GetHashCode();
                        bool prevOk = next.prevSiteId == null || Journal.SiteCompletion(next.prevSiteId) >= next.prevCompletionNeeded;
                        h = h * 31 + (prevOk ? 1 : 0) + (s.followers >= next.followersNeeded ? 2 : 0);
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
            }
        }

        static int SlotSig(HoldItem item) => Hands.Find(item, out Hand hand, out int slot) ? 1 + (int)hand * 3 + slot : 0;

        // ------------------------------------------------------------------ helpers (allocation free)
        static long TankPrice
        {
            get
            {
                var tank = GearDatabase.Get(TankId);
                return tank != null ? tank.price : 130;
            }
        }

        static bool TankOwned(GameState s) => s.ownedItems.Contains(TankId);

        static bool TankOrdered(GameState s)
        {
            for (int i = 0; i < s.deliveries.Count; i++)
                if (s.deliveries[i] != null && s.deliveries[i].itemId == TankId) return true;
            return false;
        }

        static int DiscoveredCount(GameState s)
        {
            int n = 0;
            var j = s.journal;
            for (int i = 0; i < j.Count; i++) if (j[i] != null && j[i].discovered) n++;
            return n;
        }
    }
}
