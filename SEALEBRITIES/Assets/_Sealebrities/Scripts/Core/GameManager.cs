using System;
using System.IO;
using UnityEngine;

namespace Sealebrities
{
    /// Owns the GameState, the clock, saving and the new-day flow. Created by GameBootstrap.
    public class GameManager : MonoBehaviour
    {
        public static GameManager I { get; private set; }

        public const float DayLengthSeconds = 25f * 60f;
        public const float MorningTime = 0.27f;     // 06:29
        public const float NightStart = 0.85f;      // 20:24

        public GameState State { get; private set; } = new GameState();
        public bool InGame { get; private set; }
        public DifficultySettings Difficulty => DifficultySettings.For(State.difficulty);
        public GearStats Gear => GearDatabase.Stats(State);

        static string SavePath => Path.Combine(Application.persistentDataPath, "sealebrities_save.json");
        public static bool HasSave => File.Exists(SavePath);

        /// Set by automated test runs (AutoShot) so they never overwrite the player's save.
        public static bool SavingDisabled;

        void Awake()
        {
            if (I != null && I != this) { Destroy(gameObject); return; }
            I = this;
            LoadSettingsOnly();
        }

        void OnDestroy() { if (I == this) I = null; }

        // ------------------------------------------------------------------ game flow
        public void NewGame(Difficulty difficulty)
        {
            State = new GameState { difficulty = difficulty, language = Loc.Current, day = 1, timeOfDay = MorningTime };
            GearDatabase.GiveStartingGear(State);
            State.unlockedSites.Add("house_reef");
            State.followerHistory.Add(0);
            InGame = true;
            Save();
            RaiseAllChanged();
            GameEvents.RaiseGameStarted();
            GameEvents.RaiseDayStarted(State.day);
        }

        public bool LoadGame()
        {
            if (!HasSave) return false;
            try
            {
                var loaded = JsonUtility.FromJson<GameState>(File.ReadAllText(SavePath));
                if (loaded == null) return false;
                State = loaded;
                GearDatabase.GiveStartingGear(State);
                if (!State.unlockedSites.Contains("house_reef")) State.unlockedSites.Add("house_reef");
                Loc.SetLanguage(State.language);
                State.timeOfDay = MorningTime;   // always wake up in the morning
                State.diveFootage.Clear();
                InGame = true;
                RaiseAllChanged();
                GameEvents.RaiseGameStarted();
                GameEvents.RaiseDayStarted(State.day);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("Sealebrities: could not load save: " + e.Message);
                return false;
            }
        }

        public void Save()
        {
            if (!InGame || SavingDisabled) return;
            try
            {
                State.language = Loc.Current;
                File.WriteAllText(SavePath, JsonUtility.ToJson(State, true));
            }
            catch (Exception e) { Debug.LogWarning("Sealebrities: could not save: " + e.Message); }
        }

        public void DeleteSave()
        {
            try { if (HasSave) File.Delete(SavePath); } catch (Exception) { }
        }

        public void ReturnToMenu()
        {
            Save();
            InGame = false;
            GameEvents.RaiseReturnedToMenu();
        }

        void LoadSettingsOnly()
        {
            if (!HasSave) return;
            try
            {
                var s = JsonUtility.FromJson<GameState>(File.ReadAllText(SavePath));
                if (s != null) Loc.Current = s.language;
            }
            catch (Exception) { }
        }

        void RaiseAllChanged()
        {
            GameEvents.RaiseMoneyChanged(State.money);
            GameEvents.RaiseFollowersChanged(State.followers);
            GameEvents.RaiseGearChanged();
        }

        // ------------------------------------------------------------------ economy
        public void AddMoney(long amount)
        {
            if (amount == 0) return;
            State.money = Math.Max(0, State.money + amount);
            GameEvents.RaiseMoneyChanged(State.money);
        }

        public bool TrySpend(long amount)
        {
            if (State.money < amount) return false;
            State.money -= amount;
            GameEvents.RaiseMoneyChanged(State.money);
            return true;
        }

        public void AddFollowers(long amount)
        {
            if (amount == 0) return;
            State.followers = Math.Max(0, State.followers + amount);
            GameEvents.RaiseFollowersChanged(State.followers);
            Progression.CheckMilestones();
        }

        // ------------------------------------------------------------------ clock
        public bool IsNight => State.timeOfDay >= NightStart || State.timeOfDay < 0.22f;

        public string ClockString
        {
            get
            {
                float h = State.timeOfDay * 24f;
                int hh = Mathf.FloorToInt(h) % 24;
                int mm = Mathf.FloorToInt((h - Mathf.Floor(h)) * 60f);
                return $"{hh:00}:{mm:00}";
            }
        }

        void Update()
        {
            if (!InGame || UIState.PauseOpen) return;
            State.timeOfDay += Time.deltaTime / DayLengthSeconds;
            if (State.timeOfDay >= 1f)
            {
                State.timeOfDay -= 1f;
                StartNewDay(false);
            }
        }

        /// Bed interaction: fade, skip to the next morning, resolve uploads, save.
        public void SleepToMorning()
        {
            Fader.FadeThrough(() =>
            {
                State.timeOfDay = MorningTime;
                StartNewDay(true);
            }, 2f);
        }

        void StartNewDay(bool slept)
        {
            State.day++;
            if (slept) State.timeOfDay = MorningTime;
            Progression.ResolvePendingUploads();
            long sponsor = Progression.DailySponsorIncome();
            if (sponsor > 0)
            {
                AddMoney(sponsor);
                GameEvents.RaiseToast(Loc.Tr($"Sponsoren zahlen {Loc.Money(sponsor)}", $"Sponsors pay {Loc.Money(sponsor)}"));
            }
            State.followerHistory.Add(State.followers);
            GameEvents.RaiseDayStarted(State.day);
            Save();
        }

        /// Called by the player module when air runs out. Applies difficulty rules; the caller handles teleporting.
        public void HandleFaint()
        {
            Footage.LoseDive();
            var d = Difficulty;
            if (d.permadeath)
            {
                GameEvents.RaiseBanner(Loc.Tr("Permadeath: Deine Agentur ist pleite.", "Permadeath: your agency is bankrupt."), new Color(1f, 0.23f, 0.19f));
                DeleteSave();
                InGame = false;
                GameEvents.RaisePlayerFainted();
                GameEvents.RaiseReturnedToMenu();
                return;
            }
            if (d.rescueFee > 0)
            {
                long fee = Math.Min(State.money, d.rescueFee);
                AddMoney(-fee);
                GameEvents.RaiseToast(Loc.Tr($"Rettung: −{Loc.Money(fee)}", $"Rescue: −{Loc.Money(fee)}"));
            }
            GameEvents.RaisePlayerFainted();
            Save();
        }

        void OnApplicationQuit() { Save(); }
    }
}
