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
    ///   Journal.RegisterFilmed / RegisterPhoto / AddSightings · Footage.BankDive (host: Footage.BankClips) · Progression.CreateUpload
    ///   Shop.Buy / BuyFood · Shop.Receive (host: Shop.ReceiveForRemote) · FishMarket.AddCatch / List / ListAt / ListAll
    ///   Collectibles.Collect · BoatHome.Request · StoryFlags.Set/Clear · Missions.Set · Research.* · Achievements.Unlock
    ///   Cosmetics.Unlock · Pantry.Add / TryEat / TryGrill / TryShakePalm / BuyAtKiosk · Finds.Take / Deliver / Sell / SellAll
    ///   Challenges.Complete · Quests.Start / SetProgress / Complete / Drop / Track · Deals.Accept / Cancel
    ///   Village.ReleaseLantern · SleepVote.SetLying (host evaluates) · PlayerRecords.SendLocal
    /// Host-only automatic changes: StartNewDay (+ challenges, deal of the day, finds reset, sponsor deals, Toni trigger),
    /// Progression.ResolvePendingUploads / CheckMilestones / CheckUnlocks, FishMarket.Tick, sponsor income,
    /// Deals.AddOffer / Complete / Fail / SetProgress, Cosmetics.CheckRules.
    /// Per-player (never routed): Hands, Gear.Equip, Gallery, Footage.AddClip/LoseDive, Settings, Buffs, Carry (the host
    /// only mirrors it), SelfieState.
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

        /// Round 3 step 6b (GDD §19b): four save slots (SaveSlots) – the running world saves into SaveSlots.Active
        /// (save_slot{n}.json); null = nothing to save (no slot / co-op guest).
        static string SavePath => SaveSlots.ActiveFile;
        /// At least one save slot is used.
        public static bool HasSave => SaveSlots.AnyUsed;

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
            // round 3 step 6b: the old single save becomes save slot 1 (never from a test run)
            if (!AutomatedRun && !SavingDisabled)
            {
                try { SaveSlots.MigrateLegacy(); }
                catch (Exception e) { Debug.LogWarning("Sealebrities: save migration failed: " + e.Message); }
            }
            Achievements.Hook();
            if (!hooked)
            {
                hooked = true;
                GameEvents.MilestoneReached += OnMilestone;
            }
        }

        static bool hooked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStaticHooks() { hooked = false; }

        void OnDestroy() { if (I == this) I = null; }

        /// The 1,000,000 milestone starts the finale (Q79: on Normal/Hard this PC unlocks Schwer – also via CompleteFinale).
        static void OnMilestone(Milestone m)
        {
            var gm = I;
            if (m == null || gm == null || m.followers < Progression.FinaleFollowers) return;
            MarkFinaleOnThisPc(gm.State.difficulty);
        }

        static void MarkFinaleOnThisPc(Difficulty d)
        {
            if (DifficultySettings.Normalize(d) == Sealebrities.Difficulty.Snorkeler || Settings.Data.normalCompleted || AutomatedRun) return;
            Settings.Data.normalCompleted = true;
            Settings.Save();
        }

        /// The finale "Live aus der Tiefe" was finished (npc module). Host: shared flag + achievement for everyone.
        public void CompleteFinale()
        {
            if (!InGame) return;
            State.finaleReached = true;
            StoryFlags.Set(StoryKeys.FinaleDone);
            Achievements.Unlock("live_from_the_deep");
            MarkFinaleOnThisPc(State.difficulty);
        }

        /// K.5: a new game starts with the prologue at the helipad (WorldLayout.NewGameSpawn) until StoryKeys.PrologueDone.
        public bool NeedsPrologue => InGame && !State.prologueDone;

        // ------------------------------------------------------------------ game flow
        /// New world without choosing a slot: the first free slot (automated runs / saving disabled: no slot at all, the
        /// world is never saved). The menu and the host pages pass the slot themselves.
        public void NewGame(Difficulty difficulty) => NewGame(difficulty, AutomatedRun || SavingDisabled ? 0 : SaveSlots.FirstFree());

        /// New world in save slot `slot` (1..4; 0 = an unsaved world). An old world in that slot (file + photos) is
        /// removed first – the menu only offers empty slots, so this only clears leftovers.
        public void NewGame(Difficulty difficulty, int slot)
        {
            if (!SaveSlots.Valid(slot)) slot = 0;
            if (slot > 0 && !SavingDisabled) SaveSlots.PrepareNew(slot);
            SaveSlots.SetActive(slot);
            if (slot > 0 && !SavingDisabled) SaveSlots.SetLastUsed(slot);
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
            ResetRuntimeState();
            InGame = true;
            Challenges.RollForDay(State);
            Shop.RollDealOfTheDay(State);
            Save();
            RaiseAllChanged();
            GameEvents.RaiseGameStarted();
            GameEvents.RaiseDayStarted(State.day);
        }

        /// Per-session runtime facades (buffs, stream, sleep vote, quest line, viewfinder, selfie, animal event).
        static void ResetRuntimeState()
        {
            Buffs.Clear();
            LiveState.Reset();
            SleepVote.Reset();
            QuestTracker.Clear();
            ViewfinderInfo.Clear();
            SelfieState.Active = false;
            FaunaEventInfo.End();
            Combat.Reset();
        }

        /// „Fortsetzen“: loads the last played slot (SaveSlots.ContinueSlot).
        public bool LoadGame() => LoadSlot(SaveSlots.ContinueSlot);

        /// Loads save slot `slot` (1..4) – this world's progress, achievements, map pins, hut, photos. The slot becomes
        /// the one this world saves into and the one „Fortsetzen“ continues.
        public bool LoadSlot(int slot)
        {
            if (!SaveSlots.Exists(slot)) return false;
            string json;
            try { json = File.ReadAllText(SaveSlots.FileOf(slot)); }
            catch (Exception e)
            {
                Debug.LogWarning("Sealebrities: could not read save slot " + slot + ": " + e.Message);
                return false;
            }
            int prev = SaveSlots.Active;
            SaveSlots.SetActive(slot);   // before loading: the gallery / photo book of THIS world are read from now on
            if (!LoadFromJson(json))
            {
                SaveSlots.SetActive(prev);
                return false;
            }
            SaveSlots.SetLastUsed(slot);
            return true;
        }

        /// Loads a save from its JSON text – LoadGame reads the file; the automated co-op test reloads the host's world
        /// from the same JSON in memory (it must never touch the player's save file).
        public bool LoadFromJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return false;
            try
            {
                var loaded = JsonUtility.FromJson<GameState>(json);
                if (loaded == null) return false;
                loaded.EnsureValid();
                loaded.difficulty = DifficultySettings.Normalize(loaded.difficulty);
                if (loaded.saveSeed == 0) loaded.saveSeed = NewSeed();
                // heavy salvage hooked on the winch / carried when the game was saved is not in anyone's hands after
                // loading: put it back on the seabed (small finds go straight to foundItems and are never "delivered")
                for (int i = 0; i < loaded.finds.Count; i++)
                {
                    var f = loaded.finds[i];
                    if (f != null && f.taken && !f.delivered && FindsDatabase.TryResolve(f.id, loaded.saveSeed, out var sp) && sp.heavy) f.taken = false;
                }
                RebaseTimersForMorning(loaded);
                // the gallery is per machine now (gallery/index.json): migrate photos stored in older saves
                Gallery.ImportLegacy(loaded.photos);
                loaded.photos.Clear();
                State = loaded;
                // the world's achievements are the host's own (no notification burst when one fires again)
                Achievements.RememberSilently(State.achievements);
                GearDatabase.GiveStartingGear(State);
                if (!State.unlockedSites.Contains("house_reef")) State.unlockedSites.Add("house_reef");
                // a story site in the list without its flag (hand-edited / old test saves) is locked again
                for (int i = State.unlockedSites.Count - 1; i >= 0; i--)
                {
                    var site = DiveSites.Get(State.unlockedSites[i]);
                    if (site != null && site.IsStorySite && !State.storyFlags.Contains(site.unlockFlag)) State.unlockedSites.RemoveAt(i);
                }
                State.timeOfDay = MorningTime;   // always wake up in the morning
                State.diveFootage.Clear();
                State.activeRight = -1;          // both hands empty after loading
                State.activeLeft = -1;
                State.activeMain = -1;
                State.activeOff = -1;
                State.carried = null;            // carried items are not saved in the hands
                Survival.FinishFilterAfterLoad(State);   // the night passed: the water filter is done
                ResetRuntimeState();
                InGame = true;
                if (State.challengesToday.Count == 0) Challenges.RollForDay(State);
                if (string.IsNullOrEmpty(State.dealOfDayItem)) Shop.RollDealOfTheDay(State);
                RaiseAllChanged();
                GameEvents.RaiseGameStarted();
                GameEvents.RaiseDayStarted(State.day);
                if (State.justGiftedCompressor)
                {
                    State.justGiftedCompressor = false;
                    GameEvents.RaiseNotify(NotifyKind.Delivery, Loc.Tr("Geschenk von Gus", "A gift from Gus"), Loc.Tr("Boot-Kompressor", "Boat compressor"));
                    Save();
                }
                Progression.CheckUnlocks();
                Cosmetics.CheckRules();
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

        /// Writes the save into the active slot (compact JSON, atomically through a temp file so a crash mid-write never
        /// corrupts it). Skipped on co-op clients (only the host saves – into the host's slot, with every guest's
        /// PlayerRecord), in automated runs and for worlds without a slot.
        public void Save()
        {
            if (!InGame || SavingDisabled || Net.IsClient) return;
            string path = SavePath;
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                State.language = Loc.Current;
                State.lastPlayedUtc = DateTime.UtcNow.Ticks;
                PlayerRecords.Store(PlayerRecords.CaptureLocal());   // F.3: the host's own record next to the guests'
                string json = JsonUtility.ToJson(State);
                string tmp = path + ".tmp";
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
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
                SaveSlots.NotifyChanged(SaveSlots.Active);
            }
            catch (Exception e) { Debug.LogWarning("Sealebrities: could not save: " + e.Message); }
        }

        /// Deletes the running world's slot (permadeath): save file + its photos.
        public void DeleteSave()
        {
            if (SavingDisabled || Net.IsClient || !SaveSlots.Valid(SaveSlots.Active)) return;
            try { SaveSlots.Delete(SaveSlots.Active); } catch (Exception) { }
        }

        public void ReturnToMenu()
        {
            SaveCoopPlayer(false);
            Journal.FlushAllSightings();  // guests: the last seconds of Sealog counts / depths still reach the host
            PlayerRecords.SendLocal();   // guests: the host keeps this machine's record (F.3)
            Save();
            InGame = false;
            ResetRuntimeState();
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
            string path = SaveSlots.LegacyPath;
            if (!File.Exists(path)) path = SaveSlots.Exists(SaveSlots.ContinueSlot) ? SaveSlots.FileOf(SaveSlots.ContinueSlot) : null;
            if (path == null) return;
            try
            {
                var old = JsonUtility.FromJson<LegacySettings>(File.ReadAllText(path));
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
            // from the menu: the guest's own hotbar and equipment choice of its last session in THIS host world
            // (coop/w{seed}/coop_player.json – per world, GDD §19b)
            snapshot.CopyPerPlayerFrom(InGame ? State : LoadCoopPlayer(snapshot.saveSeed));
            snapshot.activeRight = -1;
            snapshot.activeLeft = -1;
            snapshot.activeMain = -1;
            snapshot.activeOff = -1;
            snapshot.EnsurePerPlayer();   // round 3: a first-time guest gets the starter backpack
            snapshot.diveFootage.Clear();
            snapshot.carried = null;
            // the host's world is none of this PC's save slots: nothing of it can ever be written into one
            SaveSlots.SetActive(0);
            State = snapshot;
            GearDatabase.GiveStartingGear(State);
            ResetRuntimeState();
            InGame = true;
            // channel cosmetics become visible on this machine's character screen; achievements are only those earned
            // while present (ApplyHostSnapshot diff / Net message 95 / the stored PlayerRecord)
            for (int i = 0; i < State.cosmetics.Count; i++) Cosmetics.RememberLocal(State.cosmetics[i], false);
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
            Journal.ReapplyPendingSightings();   // optimistic "×n" counts not yet batched to the host stay visible

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
                GameEvents.RaiseBanner(m.BannerText, new Color(1f, 0.76f, 0.2f));
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
            if (Progression.RankIndex(snapshot.followers) != Progression.RankIndex(old.followers))
                GameEvents.RaiseRankChanged(Progression.RankIndex(snapshot.followers));

            // round 2: shared lists the client may have missed
            for (int i = 0; i < snapshot.storyFlags.Count; i++)
                if (!old.storyFlags.Contains(snapshot.storyFlags[i])) GameEvents.RaiseStoryFlagSet(snapshot.storyFlags[i]);
            for (int i = 0; i < snapshot.achievements.Count; i++)
                if (!old.achievements.Contains(snapshot.achievements[i])) Achievements.ApplyLocal(snapshot.achievements[i]);
            for (int i = 0; i < snapshot.cosmetics.Count; i++)
                Cosmetics.RememberLocal(snapshot.cosmetics[i], !old.cosmetics.Contains(snapshot.cosmetics[i]));
            if (!SamePantry(old, snapshot)) Pantry.RaiseChanged();
            if (snapshot.finds.Count != old.finds.Count || snapshot.foundItems.Count != old.foundItems.Count) Finds.RaiseChanged();
            if (snapshot.sponsor.offers.Count != old.sponsor.offers.Count || snapshot.sponsor.active.Count != old.sponsor.active.Count) Deals.RaiseChanged();
            if (snapshot.challengesToday.Count != old.challengesToday.Count || snapshot.challengesDone.Count != old.challengesDone.Count) Challenges.RaiseChanged();
            for (int i = 0; i < snapshot.sideQuests.Count; i++)
            {
                var q = snapshot.sideQuests[i];
                if (q == null) continue;
                QuestState o = null;
                for (int k = 0; k < old.sideQuests.Count; k++) if (old.sideQuests[k] != null && old.sideQuests[k].id == q.id) { o = old.sideQuests[k]; break; }
                if (o == null || o.step != q.step || o.progress != q.progress || o.done != q.done) GameEvents.RaiseSideQuestChanged(q.id);
            }
            if (snapshot.trackedQuest != old.trackedQuest) QuestTracker.Resolve();
            // no SleepVote.Reset here: players lying down before midnight stay in bed over the rollover; the sleep paths
            // reset every machine through EvSleep
            if (snapshot.day != old.day) GameEvents.RaiseDayStarted(snapshot.day);
            Net.RaiseStateSynced();
        }

        static bool SamePantry(GameState a, GameState b)
        {
            if (a.pantry.Count != b.pantry.Count) return false;
            for (int i = 0; i < a.pantry.Count; i++)
            {
                var x = a.pantry[i];
                var y = b.pantry[i];
                if (x == null || y == null || x.foodId != y.foodId || x.count != y.count) return false;
            }
            return true;
        }

        /// Host: JSON of the shared state for clients (per-player fields are included but ignored by clients).
        public string SnapshotJson() => JsonUtility.ToJson(State);

        // ------------------------------------------------------------------ co-op guest: own per-player fields
        /// A guest never saves (the host owns the save). Its hotbar layout and equipment choice are kept per HOST WORLD in
        /// coop/w{saveSeed}/coop_player.json so they survive leaving and re-joining that world (round 3 step 6b: nothing
        /// carries over into another world; the photos of that world lie next to it in coop/w{saveSeed}/gallery).
        [Serializable]
        class CoopPlayerFile
        {
            public string[] hotbarRight;
            public string[] hotbarLeft;
            public List<string> equipped = new List<string>();
            // round 3: one hotbar, backpack, survival meters
            public string[] hotbar;
            public int hotbarOffMask = GameState.DefaultOffMask;
            public List<InvStack> backpack = new List<InvStack>();
            public List<InvStack> pouch = new List<InvStack>();
            public bool backpackInit;
            public float hunger = 100f, thirst = 100f;
        }

        static string CoopPlayerPath(int seed) => Path.Combine(SaveSlots.GuestFolder(seed), "coop_player.json");
        int coopPlayerHash;
        float coopPlayerTimer;

        GameState LoadCoopPlayer(int seed)
        {
            var st = new GameState();
            if (AutomatedRun) return st;    // tests expect the default hotbar
            try
            {
                string path = CoopPlayerPath(seed);
                if (File.Exists(path))
                {
                    var f = JsonUtility.FromJson<CoopPlayerFile>(File.ReadAllText(path));
                    if (f != null)
                    {
                        if (f.hotbarRight != null) st.hotbarRight = f.hotbarRight;
                        if (f.hotbarLeft != null) st.hotbarLeft = f.hotbarLeft;
                        if (f.equipped != null) st.equipped = f.equipped;
                        if (f.hotbar != null && f.hotbar.Length == Hands.SlotCount) { st.hotbar = f.hotbar; st.hotbarOffMask = f.hotbarOffMask; }
                        if (f.backpackInit)
                        {
                            st.backpack = f.backpack ?? new List<InvStack>();
                            st.pouch = f.pouch ?? new List<InvStack>();
                            st.backpackInit = true;
                            st.hunger = f.hunger;
                            st.thirst = f.thirst;
                        }
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning("Sealebrities: coop_player.json could not be read: " + e.Message); }
            st.EnsureValid();
            coopPlayerHash = CoopPlayerHash(st);
            return st;
        }

        static int CoopPlayerHash(GameState s)
        {
            unchecked
            {
                int h = 17;
                if (s.hotbar != null) for (int i = 0; i < s.hotbar.Length; i++) h = h * 31 + (s.hotbar[i] != null ? s.hotbar[i].GetHashCode() : 0);
                h = h * 31 + s.hotbarOffMask;
                if (s.equipped != null) for (int i = 0; i < s.equipped.Count; i++) h = h * 31 + (s.equipped[i] != null ? s.equipped[i].GetHashCode() : 0);
                if (s.backpack != null)
                    for (int i = 0; i < s.backpack.Count; i++)
                    {
                        var st = s.backpack[i];
                        h = h * 31 + (st != null && st.id != null ? st.id.GetHashCode() * 7 + st.count * 131 + Mathf.RoundToInt(st.durability) * 17 + Mathf.RoundToInt(st.sharpness * 100f) : 0);
                    }
                if (s.pouch != null) for (int i = 0; i < s.pouch.Count; i++) h = h * 31 + (s.pouch[i] != null ? s.pouch[i].count : 0);
                h = h * 31 + Mathf.RoundToInt(s.hunger * 0.2f) * 101 + Mathf.RoundToInt(s.thirst * 0.2f);
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
                var f = new CoopPlayerFile
                {
                    hotbarRight = State.hotbarRight, hotbarLeft = State.hotbarLeft, equipped = State.equipped,
                    hotbar = State.hotbar, hotbarOffMask = State.hotbarOffMask, backpack = State.backpack, pouch = State.pouch,
                    backpackInit = State.backpackInit, hunger = State.hunger, thirst = State.thirst,
                };
                string path = CoopPlayerPath(State.saveSeed);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonUtility.ToJson(f));
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
            int rank = Progression.RankIndex(State.followers);
            State.followers = Math.Max(0, State.followers + amount);
            GameEvents.RaiseFollowersChanged(State.followers);
            int newRank = Progression.RankIndex(State.followers);
            if (newRank != rank) GameEvents.RaiseRankChanged(newRank);
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
            Journal.TickSightings(Time.unscaledDeltaTime);
            SleepVote.Tick(Time.unscaledDeltaTime);
            if (UIState.PauseOpen && !Net.IsOnline) return;   // offline the pause menu stops the clock; online the world runs on
            // round 3 step 6b: play time of this world (slot card); the host counts, guests get it with the snapshot
            if (!Net.IsClient) State.playSeconds += Mathf.Min(Time.unscaledDeltaTime, 1f);
            Buffs.Tick(Time.deltaTime);
            Survival.Tick(Time.deltaTime);
            Combat.Tick(Time.deltaTime);
            PlayerRecords.Tick(Time.deltaTime);
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

        /// E.3: the bed works from 18:00 (and after midnight until the morning).
        public const float SleepFromTime = 0.75f;

        /// E.3 rule for the bed / sleep spots: true from 18:00 until MorningTime. Otherwise show the reason
        /// „Schlafen ab 18:00“ / "Sleep from 6 pm" (SleepRuleHint).
        public bool CanSleepNow => InGame && (State.timeOfDay >= SleepFromTime || State.timeOfDay < MorningTime);

        public static string SleepRuleHint => Loc.Tr("Schlafen ab 18:00", "Sleep from 6 pm");

        /// Bed interaction: fade, skip to the next morning, resolve uploads, save. Co-op beds go through SleepVote (L.9:
        /// everybody must lie down – the host calls this when the vote is complete); the Net module mirrors
        /// SleepStarted as a fade on the clients. After midnight the clock rollover already started the new day, so only
        /// the morning is skipped to (uploads still resolve, same day number). Programmatic callers (tests, the
        /// vote) are not bound to the 18:00 rule – the bed checks CanSleepNow.
        public void SleepToMorning()
        {
            if (Net.IsClient) { Net.Command(NetCommand.Sleep); return; }
            if (!InGame) return;
            if (Time.unscaledTime - lastSleepStart < SleepGuardSeconds && lastSleepStart > 0f) return;
            lastSleepStart = Time.unscaledTime;
            SleepVote.Reset();
            // E.2 "Gemütlich": grilled a fish today (before midnight: this day; after midnight: yesterday) + slept in bed
            bool afterMidnight = State.timeOfDay < MorningTime;
            int sleepDay = afterMidnight ? State.day - 1 : State.day;
            bool cozy = State.lastGrillDay >= 0 && State.lastGrillDay >= sleepDay;
            GameEvents.RaiseSleepStarted();
            Fader.FadeThrough(() =>
            {
                if (!InGame) return;
                if (State.timeOfDay < MorningTime)
                {
                    State.timeOfDay = MorningTime;
                    if (cozy) GrantCozy();
                    Progression.ResolvePendingUploads();
                    GameEvents.RaiseDayStarted(State.day);
                    Save();
                    return;
                }
                State.timeOfDay = MorningTime;
                StartNewDay(true);
                if (cozy) GrantCozy();
            }, 2f);
        }

        void GrantCozy()
        {
            State.cozyBonusDay = State.day;
            GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr("Gut gegessen, gut geschlafen", "Ate well, slept well"), Loc.Tr("+5 % Aufrufe heute", "+5 % views today"));
        }

        void StartNewDay(bool slept)
        {
            State.day++;
            if (slept) State.timeOfDay = MorningTime;
            // SleepVote is reset by SleepToMorning / ForceSleep only – the midnight rollover keeps lying players in bed
            Progression.ResolvePendingUploads();
            long sponsor = Progression.DailySponsorIncome();
            if (sponsor > 0)
            {
                AddMoney(sponsor);
                GameEvents.RaiseNotify(NotifyKind.Sponsor, Loc.Tr("Sponsoren zahlen", "Sponsors pay"), "+" + Loc.Money(sponsor));
            }
            State.followerHistory.Add(State.followers);
            // round 2 daily resets (host)
            State.dailyUsed.Clear();
            Finds.OnNewDay(State);
            Challenges.RollForDay(State);
            Shop.RollDealOfTheDay(State);
            Deals.OnNewDay(State);
            StoryRules.OnNewDay(State);
            Cosmetics.CheckRules();
            FishMarket.Tick();   // listings sell overnight
            GameEvents.RaiseDayStarted(State.day);
            Save();
        }

        /// Called by the player module when the LOCAL player runs out of air. Loses this dive's footage and charges the
        /// rescue fee (shared wallet, via the host in co-op). The caller handles teleporting.
        public void HandleFaint()
        {
            Footage.LoseDive();
            var rec = PlayerRecords.Local;
            if (rec != null) rec.faints++;
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
            GameEvents.RaiseNotify(NotifyKind.Money, Loc.Tr("Rettung", "Rescue"), "−" + Loc.Money(fee));
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
