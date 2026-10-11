using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Sealebrities
{
    /// Owns the GameState, the clock, saving and the new-day flow. Created by GameBootstrap.
    ///
    /// SHARED-STATE MUTATIONS (co-op authority routing – every change of shared state goes through one of these;
    /// each begins with `if (Net.IsClient) { Net.Command(...); ... }` and CoreCommands.Execute applies them on the host):
    ///   GameManager.AddMoney / TrySpend / AddFollowers / SleepToMorning / HandleFaint (ApplyRescueFee) / SetQuestIndex
    ///   Journal.RegisterFilmed · Footage.BankDive (host: Footage.BankClips) · Progression.CreateUpload
    ///   Shop.Buy · Shop.Receive (host: Shop.ReceiveForRemote) · FishMarket.AddCatch / List / ListAt / ListAll
    ///   Collectibles.Collect
    /// Host-only automatic changes: StartNewDay, Progression.ResolvePendingUploads / CheckMilestones / CheckUnlocks,
    /// FishMarket.Tick, sponsor income. Per-player (never routed): Hands, Gear.Equip, Gallery, Footage.AddClip/LoseDive, Settings.
    public class GameManager : MonoBehaviour
    {
        public static GameManager I { get; private set; }

        /// One in-game day = 40 real minutes (the tutorial's ~18 fish + market + shop then end around 13:30–15:00, so the
        /// first dive happens in daylight; market listings and shop timers convert through this constant).
        public const float DayLengthSeconds = 40f * 60f;
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

        /// Set by AutoPlaytest / AutoPlaytestMP / AutoShot: unfocused windows keep running at full speed (no background
        /// frame cap, no pause menu on focus loss).
        public static bool AutomatedRun;

        float settingsSaveTimer;

        void Awake()
        {
            if (I != null && I != this) { Destroy(gameObject); return; }
            I = this;
            Settings.Load();
            if (!Settings.FileExisted) MigrateLegacySettings();
            Settings.Apply();
        }

        void OnDestroy() { if (I == this) I = null; }

        // ------------------------------------------------------------------ game flow
        public void NewGame(Difficulty difficulty)
        {
            State = new GameState
            {
                difficulty = DifficultySettings.Normalize(difficulty),
                language = Loc.Current,
                day = 1,
                timeOfDay = MorningTime,
                saveSeed = NewSeed()
            };
            GearDatabase.GiveStartingGear(State);
            State.unlockedSites.Add("house_reef");
            State.followerHistory.Add(0);
            State.EnsureValid();
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
                loaded.EnsureValid();
                loaded.difficulty = DifficultySettings.Normalize(loaded.difficulty);
                if (loaded.saveSeed == 0) loaded.saveSeed = NewSeed();
                RebaseTimersForMorning(loaded);
                // the gallery is per machine now (gallery/index.json): migrate photos stored in older saves
                Gallery.ImportLegacy(loaded.photos);
                loaded.photos.Clear();
                State = loaded;
                GearDatabase.GiveStartingGear(State);
                if (!State.unlockedSites.Contains("house_reef")) State.unlockedSites.Add("house_reef");
                State.timeOfDay = MorningTime;   // always wake up in the morning
                State.diveFootage.Clear();
                State.activeRight = -1;          // both hands empty after loading
                State.activeLeft = -1;
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

        /// Loading rewinds the clock to the morning of the saved day: shift every pending timer (fish listings, undelivered
        /// orders) back by the same amount so each keeps its remaining wait (no 12-15 real-minute stall after Continue).
        /// Saves made after midnight (before MorningTime) only move the clock forward and need no change.
        static void RebaseTimersForMorning(GameState loaded)
        {
            float savedNow = loaded.day + loaded.timeOfDay, newNow = loaded.day + MorningTime;
            float shift = savedNow - newNow;
            if (shift <= 0f) return;
            var listings = loaded.fishListings;
            for (int i = 0; i < listings.Count; i++)
            {
                var l = listings[i];
                if (l == null) continue;
                l.sellAt = Mathf.Max(newNow, l.sellAt - shift);
                float st = l.listedDay + l.listedTime - shift;      // shift the start too: the progress bar keeps its progress
                l.listedDay = Mathf.FloorToInt(st);
                l.listedTime = st - l.listedDay;
            }
            var deliveries = loaded.deliveries;
            for (int i = 0; i < deliveries.Count; i++)
            {
                var d = deliveries[i];
                if (d == null || d.dropped) continue;
                float at = Mathf.Max(newNow, d.arrivalDay + d.arrivalTime - shift);
                d.arrivalDay = Mathf.FloorToInt(at);
                d.arrivalTime = at - d.arrivalDay;
            }
        }

        /// Writes the save (compact JSON, atomically through a temp file so a crash mid-write never corrupts it).
        /// Skipped on co-op clients (they play in the host's world) and in automated runs.
        public void Save()
        {
            if (!InGame || SavingDisabled || Net.IsClient) return;
            try
            {
                State.language = Loc.Current;
                string json = JsonUtility.ToJson(State);
                string path = SavePath, tmp = path + ".tmp";
                File.WriteAllText(tmp, json);
                if (!File.Exists(path)) File.Move(tmp, path);
                else
                {
                    try { File.Replace(tmp, path, null); }
                    catch (Exception)
                    {
                        // filesystems without an atomic replace: plain overwrite (the temp file still held the full json)
                        File.Copy(tmp, path, true);
                        File.Delete(tmp);
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning("Sealebrities: could not save: " + e.Message); }
        }

        public void DeleteSave()
        {
            try { if (HasSave) File.Delete(SavePath); } catch (Exception) { }
        }

        public void ReturnToMenu()
        {
            SaveCoopPlayer(false);
            Save();
            InGame = false;
            GameEvents.RaiseReturnedToMenu();
        }

        static int NewSeed()
        {
            int s = new System.Random().Next(1, int.MaxValue);
            return s != 0 ? s : 1;
        }

        [Serializable]
        class LegacySettings
        {
            public Language language = Language.German;
            public float mouseSensitivity = 1f;
        }

        /// First start of this version: take language and mouse sensitivity from the old save.
        void MigrateLegacySettings()
        {
            if (!HasSave) return;
            try
            {
                var old = JsonUtility.FromJson<LegacySettings>(File.ReadAllText(SavePath));
                if (old == null) return;
                Settings.Data.language = old.language;
                if (old.mouseSensitivity > 0.05f) Settings.Data.mouseSensitivity = old.mouseSensitivity;
                Loc.Current = old.language;
                Settings.Save();
            }
            catch (Exception) { }
        }

        void RaiseAllChanged()
        {
            GameEvents.RaiseMoneyChanged(State.money);
            GameEvents.RaiseFollowersChanged(State.followers);
            GameEvents.RaiseGearChanged();
            Hands.NotifyStateReplaced();
            FishMarket.RaiseChanged();
        }

        // ------------------------------------------------------------------ co-op (called by the Net module)
        /// Client: enter the host's world with its shared-state snapshot (keeps the local per-player fields).
        public void JoinAsClient(GameState snapshot)
        {
            if (snapshot == null) return;
            snapshot.EnsureValid();
            // from the menu: the guest's own hotbar and equipment choice of its last co-op session (coop_player.json)
            snapshot.CopyPerPlayerFrom(InGame ? State : LoadCoopPlayer());
            snapshot.activeRight = -1;
            snapshot.activeLeft = -1;
            snapshot.diveFootage.Clear();
            State = snapshot;
            GearDatabase.GiveStartingGear(State);
            InGame = true;
            RaiseAllChanged();
            GameEvents.RaiseGameStarted();
            GameEvents.RaiseDayStarted(State.day);
            Net.RaiseStateSynced();
        }

        /// Client: replaces the shared state with a newer host snapshot. Keeps the local per-player fields and raises
        /// the events a client would otherwise miss (money/followers/gear changes, new day, milestones, site unlocks,
        /// resolved videos, fish market). Ends with Net.RaiseStateSynced().
        public void ApplyHostSnapshot(GameState snapshot)
        {
            if (snapshot == null) return;
            if (!InGame) { JoinAsClient(snapshot); return; }
            var old = State;
            snapshot.EnsureValid();
            snapshot.CopyPerPlayerFrom(old);
            State = snapshot;

            if (snapshot.money != old.money) GameEvents.RaiseMoneyChanged(snapshot.money);
            if (snapshot.followers != old.followers) GameEvents.RaiseFollowersChanged(snapshot.followers);
            if (snapshot.ownedItems.Count != old.ownedItems.Count) GameEvents.RaiseGearChanged();
            if (snapshot.fishInventory.Count != old.fishInventory.Count || snapshot.fishListings.Count != old.fishListings.Count || snapshot.fishSold != old.fishSold)
                FishMarket.RaiseChanged();

            for (int i = 0; i < snapshot.reachedMilestones.Count; i++)
            {
                string id = snapshot.reachedMilestones[i];
                if (old.reachedMilestones.Contains(id)) continue;
                var m = Progression.Milestones.Find(x => x.id == id);
                if (m == null) continue;
                GameEvents.RaiseMilestoneReached(m);
                GameEvents.RaiseBanner(m.Title + "\n" + m.Description, new Color(1f, 0.76f, 0.2f));
            }
            for (int i = 0; i < snapshot.unlockedSites.Count; i++)
            {
                string id = snapshot.unlockedSites[i];
                if (old.unlockedSites.Contains(id)) continue;
                var site = DiveSites.Get(id);
                if (site == null) continue;
                GameEvents.RaiseSiteUnlocked(site);
                if (site.prevSiteId != null)
                    GameEvents.RaiseBanner(Loc.Tr("Neuer Tauchplatz: ", "New dive site: ") + site.Name, new Color(0.18f, 0.77f, 0.75f));
            }
            for (int i = old.videos.Count; i < snapshot.videos.Count; i++) GameEvents.RaiseUploadResolved(snapshot.videos[i]);
            if (snapshot.day != old.day) GameEvents.RaiseDayStarted(snapshot.day);
            Net.RaiseStateSynced();
        }

        /// Host: JSON of the shared state for clients (per-player fields are included but ignored by clients).
        public string SnapshotJson() => JsonUtility.ToJson(State);

        // ------------------------------------------------------------------ co-op guest: own per-player fields
        /// A guest never saves (the host owns the save). Its hotbar layout and equipment choice are kept per machine in
        /// coop_player.json so they survive leaving and re-joining (the gallery has its own per-machine index).
        [Serializable]
        class CoopPlayerFile
        {
            public string[] hotbarRight;
            public string[] hotbarLeft;
            public List<string> equipped = new List<string>();
        }

        static string CoopPlayerPath => Path.Combine(Application.persistentDataPath, "coop_player.json");
        int coopPlayerHash;
        float coopPlayerTimer;

        GameState LoadCoopPlayer()
        {
            var st = new GameState();
            if (AutomatedRun) return st;    // tests expect the default hotbar
            try
            {
                if (File.Exists(CoopPlayerPath))
                {
                    var f = JsonUtility.FromJson<CoopPlayerFile>(File.ReadAllText(CoopPlayerPath));
                    if (f != null)
                    {
                        if (f.hotbarRight != null) st.hotbarRight = f.hotbarRight;
                        if (f.hotbarLeft != null) st.hotbarLeft = f.hotbarLeft;
                        if (f.equipped != null) st.equipped = f.equipped;
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning("Sealebrities: coop_player.json could not be read: " + e.Message); }
            st.EnsureHotbar();
            coopPlayerHash = CoopPlayerHash(st);
            return st;
        }

        static int CoopPlayerHash(GameState s)
        {
            unchecked
            {
                int h = 17;
                if (s.hotbarRight != null) for (int i = 0; i < s.hotbarRight.Length; i++) h = h * 31 + (s.hotbarRight[i] != null ? s.hotbarRight[i].GetHashCode() : 0);
                if (s.hotbarLeft != null) for (int i = 0; i < s.hotbarLeft.Length; i++) h = h * 31 + (s.hotbarLeft[i] != null ? s.hotbarLeft[i].GetHashCode() : 0);
                if (s.equipped != null) for (int i = 0; i < s.equipped.Count; i++) h = h * 31 + (s.equipped[i] != null ? s.equipped[i].GetHashCode() : 0);
                return h;
            }
        }

        /// Guest: writes coop_player.json when the hotbar or the equipment choice changed (checked every 2 s).
        void SaveCoopPlayer(bool force)
        {
            if (!Net.IsClient || !InGame || AutomatedRun) return;
            int h = CoopPlayerHash(State);
            if (!force && h == coopPlayerHash) return;
            coopPlayerHash = h;
            try
            {
                var f = new CoopPlayerFile { hotbarRight = State.hotbarRight, hotbarLeft = State.hotbarLeft, equipped = State.equipped };
                File.WriteAllText(CoopPlayerPath, JsonUtility.ToJson(f));
            }
            catch (Exception e) { Debug.LogWarning("Sealebrities: coop_player.json could not be written: " + e.Message); }
        }

        // ------------------------------------------------------------------ economy (shared state)
        public void AddMoney(long amount)
        {
            if (amount == 0) return;
            if (Net.IsClient) { Net.Command(NetCommand.AddMoney, amount.ToString(CultureInfo.InvariantCulture)); return; }
            State.money = Math.Max(0, State.money + amount);
            GameEvents.RaiseMoneyChanged(State.money);
        }

        public bool TrySpend(long amount)
        {
            if (State.money < amount) return false;
            if (Net.IsClient) { Net.Command(NetCommand.AddMoney, (-amount).ToString(CultureInfo.InvariantCulture)); return true; }
            State.money -= amount;
            GameEvents.RaiseMoneyChanged(State.money);
            return true;
        }

        public void AddFollowers(long amount)
        {
            if (amount == 0) return;
            if (Net.IsClient) { Net.Command(NetCommand.Custom, CoreCommands.AddFollowersKey, amount.ToString(CultureInfo.InvariantCulture)); return; }
            State.followers = Math.Max(0, State.followers + amount);
            GameEvents.RaiseFollowersChanged(State.followers);
            Progression.CheckMilestones();
        }

        /// Tutorial / quest progress (shared). Saves on the host.
        public void SetQuestIndex(int index)
        {
            if (State.questIndex == index) return;
            if (Net.IsClient) { Net.Command(NetCommand.Custom, CoreCommands.SetQuestKey, index.ToString(CultureInfo.InvariantCulture)); }
            State.questIndex = index;
            Save();
        }

        // ------------------------------------------------------------------ clock
        public bool IsNight => State.timeOfDay >= NightStart || State.timeOfDay < 0.22f;

        public string ClockString => Clock(State.timeOfDay);

        /// "HH:MM" for a time of day 0..1.
        public static string Clock(float timeOfDay)
        {
            float h = Mathf.Repeat(timeOfDay, 1f) * 24f;
            int hh = Mathf.FloorToInt(h) % 24;
            int mm = Mathf.FloorToInt((h - Mathf.Floor(h)) * 60f);
            return $"{hh:00}:{mm:00}";
        }

        void Update()
        {
            settingsSaveTimer += Time.unscaledDeltaTime;
            if (settingsSaveTimer >= 2f)
            {
                settingsSaveTimer = 0f;
                Settings.SaveIfDirty();
            }

            if (!InGame) return;
            if (Net.IsClient)
            {
                coopPlayerTimer += Time.unscaledDeltaTime;
                if (coopPlayerTimer >= 2f) { coopPlayerTimer = 0f; SaveCoopPlayer(false); }
            }
            if (UIState.PauseOpen && !Net.IsOnline) return;   // offline the pause menu stops the clock; online the world runs on
            State.timeOfDay += Time.deltaTime / DayLengthSeconds;
            if (State.timeOfDay >= 1f)
            {
                if (Net.HasAuthority)
                {
                    State.timeOfDay -= 1f;
                    StartNewDay(false);
                }
                else State.timeOfDay = 0.99999f;   // clients wait for the host's new day
            }
            if (Net.HasAuthority) FishMarket.Tick();
        }

        /// Seconds after a sleep started in which further sleep requests are ignored (co-op: two players at the bed, or a
        /// client's Sleep command arriving during the fade, must not skip two days).
        public const float SleepGuardSeconds = 8f;
        float lastSleepStart = -100f;

        /// Bed interaction: fade, skip to the next morning, resolve uploads, save. In co-op one sleeps = everybody sleeps
        /// (the Net module mirrors SleepStarted as a fade on the clients). After midnight the clock rollover already
        /// started the new day, so only the morning is skipped to (uploads still resolve, same day number).
        public void SleepToMorning()
        {
            if (Net.IsClient) { Net.Command(NetCommand.Sleep); return; }
            if (!InGame) return;
            if (Time.unscaledTime - lastSleepStart < SleepGuardSeconds && lastSleepStart > 0f) return;
            lastSleepStart = Time.unscaledTime;
            GameEvents.RaiseSleepStarted();
            Fader.FadeThrough(() =>
            {
                if (!InGame) return;
                if (State.timeOfDay < MorningTime)
                {
                    State.timeOfDay = MorningTime;
                    Progression.ResolvePendingUploads();
                    GameEvents.RaiseDayStarted(State.day);
                    Save();
                    return;
                }
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
            FishMarket.Tick();   // listings sell overnight
            GameEvents.RaiseDayStarted(State.day);
            Save();
        }

        /// Called by the player module when the LOCAL player runs out of air. Loses this dive's footage and charges the
        /// rescue fee (shared wallet, via the host in co-op). The caller handles teleporting.
        public void HandleFaint()
        {
            Footage.LoseDive();
            if (Net.IsClient)
            {
                Net.Command(NetCommand.Faint);
                GameEvents.RaisePlayerFainted();
                return;
            }
            var d = Difficulty;
            if (d.permadeath && !Net.IsOnline)
            {
                GameEvents.RaiseBanner(Loc.Tr("Permadeath: Deine Agentur ist pleite.", "Permadeath: your agency is bankrupt."), new Color(1f, 0.23f, 0.19f));
                DeleteSave();
                InGame = false;
                GameEvents.RaisePlayerFainted();
                GameEvents.RaiseReturnedToMenu();
                return;
            }
            ApplyRescueFee();
            GameEvents.RaisePlayerFainted();
            Save();
        }

        /// The fee charged by the last ApplyRescueFee (the Net module formats it in every guest's own language).
        public static long LastRescueFee { get; private set; }

        /// Host: charges the difficulty's rescue fee (own faint or a client's NetCommand.Faint).
        public void ApplyRescueFee()
        {
            if (!Net.HasAuthority) return;
            long fee = Math.Min(State.money, Difficulty.rescueFee);
            if (fee <= 0) return;
            LastRescueFee = fee;
            AddMoney(-fee);
            GameEvents.RaiseToast(Loc.Tr($"Rettung: −{Loc.Money(fee)}", $"Rescue: −{Loc.Money(fee)}"));
        }

        /// An alt-tabbed window does not need full frame rate: cap it (30 online so the session stays smooth, 15 offline)
        /// – important for the same-PC two-window co-op test on a shared iGPU. Focus back → the player's settings.
        void OnApplicationFocus(bool focus)
        {
            if (AutomatedRun) return;
            if (focus)
            {
                Settings.Apply();
                return;
            }
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = Net.IsOnline ? 30 : 15;
        }

        void OnApplicationQuit()
        {
            SaveCoopPlayer(false);
            Save();
            Settings.SaveIfDirty();
        }
    }
}
