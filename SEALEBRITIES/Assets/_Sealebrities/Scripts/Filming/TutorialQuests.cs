using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Linear tutorial chain stored in GameState.questIndex (0..7, 8 = finished). Publishes the objective text via
    /// GameEvents.QuestChanged and shows a few one-time contextual hints as toasts while the tutorial runs.
    /// Steps are checked both on events and by a light poll, so loaded saves and event order never get stuck.
    public class TutorialQuests : MonoBehaviour
    {
        public const int Finished = 8;
        const string TankId = "tank_t1";
        const string HouseReefId = "house_reef";
        const float PollInterval = 0.25f;
        const float HintSpacing = 3.5f;

        enum TutorialHint { Underwater, Recording, FirstClip, FirstSpecies, Count }

        bool phoneOpened, uploadSeen;
        int fishCaughtSession;
        float pollTimer, hintCooldown;
        string lastText;
        readonly bool[] hintShown = new bool[(int)TutorialHint.Count];
        readonly Queue<string> pendingHints = new Queue<string>();

        static readonly Color FinishColor = new Color(1f, 0.76f, 0.2f);

        /// Current step (0..8) or -1 when no game is running.
        public static int CurrentStep => GameManager.I != null && GameManager.I.InGame ? Mathf.Clamp(GameManager.I.State.questIndex, 0, Finished) : -1;

        // ------------------------------------------------------------------ lifecycle
        void OnEnable()
        {
            GameEvents.GameStarted += OnGameStarted;
            GameEvents.ReturnedToMenu += OnReturnedToMenu;
            GameEvents.LanguageChanged += OnLanguageChanged;
            GameEvents.PhoneOpened += OnPhoneOpened;
            GameEvents.FishCaught += OnFishCaught;
            GameEvents.DeliveryOrdered += OnDelivery;
            GameEvents.DeliveryReceived += OnDelivery;
            GameEvents.UploadCreated += OnUploadCreated;
            GameEvents.DayStarted += OnDayStarted;
            GameEvents.SpeciesDiscovered += OnSpeciesDiscovered;
            GameEvents.RecordingChanged += OnRecordingChanged;
            GameEvents.ClipRecorded += OnClipRecorded;
            GameEvents.MoneyChanged += OnMoneyChanged;
        }

        void OnDisable()
        {
            GameEvents.GameStarted -= OnGameStarted;
            GameEvents.ReturnedToMenu -= OnReturnedToMenu;
            GameEvents.LanguageChanged -= OnLanguageChanged;
            GameEvents.PhoneOpened -= OnPhoneOpened;
            GameEvents.FishCaught -= OnFishCaught;
            GameEvents.DeliveryOrdered -= OnDelivery;
            GameEvents.DeliveryReceived -= OnDelivery;
            GameEvents.UploadCreated -= OnUploadCreated;
            GameEvents.DayStarted -= OnDayStarted;
            GameEvents.SpeciesDiscovered -= OnSpeciesDiscovered;
            GameEvents.RecordingChanged -= OnRecordingChanged;
            GameEvents.ClipRecorded -= OnClipRecorded;
            GameEvents.MoneyChanged -= OnMoneyChanged;
        }

        static bool InGame => GameManager.I != null && GameManager.I.InGame;

        // ------------------------------------------------------------------ events
        void ResetSession()
        {
            phoneOpened = false;
            uploadSeen = false;
            fishCaughtSession = 0;
            pendingHints.Clear();
            hintCooldown = 1.5f;
            pollTimer = 0f;
            for (int i = 0; i < hintShown.Length; i++) hintShown[i] = false;
        }

        void OnGameStarted()
        {
            ResetSession();
            lastText = null;
            Poll(true);
        }

        void OnReturnedToMenu()
        {
            ResetSession();
            lastText = "";
            GameEvents.RaiseQuestChanged("");
        }

        void OnLanguageChanged() { if (InGame) Poll(true); }
        void OnPhoneOpened() { phoneOpened = true; Poll(false); }
        void OnFishCaught(int value) { fishCaughtSession++; Poll(false); }
        void OnDelivery(GearItem item) { Poll(false); }
        void OnUploadCreated(PendingUpload upload) { uploadSeen = true; Poll(false); }
        void OnDayStarted(int day) { Poll(false); }
        void OnMoneyChanged(long money) { if (InGame && GameManager.I.State.questIndex == 2) Poll(false); }

        void OnSpeciesDiscovered(SpeciesDef s, float cm, SizeTier tier)
        {
            QueueHint(TutorialHint.FirstSpecies, Loc.Tr("Neu im Sealog (J)! Je größer das Tier, desto mehr Aufrufe und Geld.",
                                                        "New in the Sealog (J)! The bigger the animal, the more views and money."));
            Poll(false);
        }

        void OnRecordingChanged(bool rec)
        {
            if (rec) QueueHint(TutorialHint.Recording, Loc.Tr("Halte das Tier 1,5 Sekunden gut im Bild", "Keep the animal well framed for 1.5 seconds"));
        }

        void OnClipRecorded(FootageClip clip)
        {
            QueueHint(TutorialHint.FirstClip, Loc.Tr("Clip im Kasten! Sicher ist er erst an Land oder auf dem Boot.",
                                                     "Clip captured! It's only safe once you're back on land or the boat."));
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
            if (p != null && p.IsUnderwater)
                QueueHint(TutorialHint.Underwater, Loc.Tr("Behalte deine Luft im Blick!", "Keep an eye on your air!"));

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

        /// Advances through every satisfied step (several at once for loaded saves), saves on change and publishes the text.
        void Poll(bool forcePublish)
        {
            var gm = GameManager.I;
            if (gm == null || !gm.InGame) return;
            var s = gm.State;
            int before = Mathf.Clamp(s.questIndex, 0, Finished);
            int step = before;

            // the tank order vanished without arriving (should not happen) – go back to ordering it
            if (step == 3 && !TankOwned(s) && !TankOrdered(s)) step = 2;
            while (step < Finished && IsDone(step, s)) step++;

            if (step != s.questIndex)
            {
                s.questIndex = step;
                if (step >= Finished && before < Finished)
                    GameEvents.RaiseBanner(Loc.Tr("Tutorial geschafft! Dein Ziel: 1.000.000 Follower.", "Tutorial complete! Your goal: 1,000,000 followers."), FinishColor);
                gm.Save();
                forcePublish = true;
            }
            Publish(step, s, forcePublish);
        }

        void Publish(int step, GameState s, bool force)
        {
            string text = step >= Finished ? "" : Text(step, s);
            if (!force && text == lastText) return;
            lastText = text;
            GameEvents.RaiseQuestChanged(text);
        }

        // ------------------------------------------------------------------ steps
        bool IsDone(int step, GameState s)
        {
            switch (step)
            {
                case 0: return phoneOpened || UIState.PhoneOpen;
                case 1: return FishCount(s) >= 3 || TankOwned(s) || TankOrdered(s);
                case 2: return TankOwned(s) || TankOrdered(s);
                case 3: return TankOwned(s);
                case 4: return UnderwaterAtHouseReef() || DiscoveredCount(s) >= 3;
                case 5: return DiscoveredCount(s) >= 3;
                case 6: return uploadSeen || s.pendingUploads.Count > 0 || s.videos.Count > 0;
                case 7: return s.videos.Count > 0;   // uploads are resolved at the start of a new day
                default: return true;
            }
        }

        string Text(int step, GameState s)
        {
            switch (step)
            {
                case 0:
                    return Loc.Tr("Willkommen auf Rust Cove! Öffne dein Handy mit Tab.", "Welcome to Rust Cove! Open your phone with Tab.");
                case 1:
                {
                    int n = Mathf.Min(3, FishCount(s));
                    return Loc.Tr("Geh ans Ende des Stegs und angle 3 Fische (E).", "Walk to the end of the dock and catch 3 fish (E).") + $" ({n}/3)";
                }
                case 2:
                {
                    var tank = GearDatabase.Get(TankId);
                    long price = tank != null ? tank.price : 80;
                    string t = Loc.Tr("Bestell im Chopper Shop die Schrott-Pressluftflasche.", "Order the Scrap Air Tank in the Chopper Shop.") + " (" + Loc.Money(price) + ")";
                    if (s.money < price)
                    {
                        string missing = Loc.Money(price - s.money);
                        t += " " + Loc.Tr($"Dir fehlen noch {missing} – angle weiter.", $"You still need {missing} – keep fishing.");
                    }
                    return t;
                }
                case 3:
                {
                    string t = Loc.Tr("Warte auf den Helikopter und öffne das Paket am Landeplatz.", "Wait for the helicopter and open the crate on the helipad.");
                    string hint = DeliveryHint(s);
                    return string.IsNullOrEmpty(hint) ? t : t + " " + hint;
                }
                case 4:
                    return Loc.Tr("Schwimm oder fahr mit dem Boot zum Hausriff (Boje) und tauch ab.", "Swim or take the boat to the House Reef (buoy) and dive down.");
                case 5:
                {
                    int n = Mathf.Min(3, DiscoveredCount(s));
                    return Loc.Tr("Filme 3 verschiedene Tierarten: rechte Maustaste zielen, linke Maustaste aufnehmen.",
                                  "Film 3 different species: right mouse button to aim, left mouse button to record.") + $" ({n}/3)";
                }
                case 6:
                    return Loc.Tr("Komm zurück an Land oder aufs Boot und lade dein Video im Handy auf FinTube hoch.",
                                  "Get back on land or onto the boat and upload your video to FinTube on your phone.");
                case 7:
                    return Loc.Tr("Geh in der Hütte schlafen. Über Nacht kommen die Aufrufe.", "Go to sleep in the hut. The views come in overnight.");
                default:
                    return "";
            }
        }

        static string DeliveryHint(GameState s)
        {
            Delivery d = null;
            for (int i = 0; i < s.deliveries.Count; i++)
                if (s.deliveries[i] != null && s.deliveries[i].itemId == TankId) { d = s.deliveries[i]; break; }
            if (d == null) return "";
            if (d.dropped) return Loc.Tr("Das Paket liegt am Landeplatz!", "The crate is waiting on the helipad!");
            if (Shop.IsDue(d)) return Loc.Tr("Der Helikopter ist im Anflug!", "The chopper is on its way!");
            if (d.arrivalDay > s.day) return Loc.Tr("Lieferung morgen früh – du kannst in der Hütte schlafen.", "Delivery tomorrow morning – you can sleep in the hut.");
            string clock = Clock(d.arrivalTime);
            return Loc.Tr($"Ankunft gegen {clock} Uhr.", $"Arriving around {clock}.");
        }

        static string Clock(float timeOfDay)
        {
            float h = Mathf.Repeat(timeOfDay, 1f) * 24f;
            int hh = Mathf.FloorToInt(h) % 24;
            int mm = Mathf.FloorToInt((h - Mathf.Floor(h)) * 60f);
            return $"{hh:00}:{mm:00}";
        }

        // ------------------------------------------------------------------ helpers (allocation free)
        int FishCount(GameState s) => Mathf.Max(fishCaughtSession, s.fishCaught);

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

        static bool UnderwaterAtHouseReef()
        {
            var p = Refs.Player;
            if (p == null || !p.IsUnderwater) return false;
            Transform root = p.Root;
            if (root == null) return false;
            DiveSite site = WorldInfo.SiteAt(root.position);
            return site != null && site.id == HouseReefId;
        }
    }
}
