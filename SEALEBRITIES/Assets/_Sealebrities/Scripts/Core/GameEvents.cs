using System;
using UnityEngine;

namespace Sealebrities
{
    /// Global event bus. Systems raise events here; UI and other systems listen. Never null-check callers: use the Raise* methods.
    ///
    /// Notifications (L.10): RaiseNotify(kind, line, chip) is THE way to show a toast – one icon + one short line (≤ 40
    /// characters) + an optional value chip. Toast is kept as a flattened mirror of every notification ("line · chip")
    /// for legacy listeners (FX click sound, net forwarding, test logs); RaiseToast(text) = RaiseNotify(Info, text).
    /// The UI subscribes to exactly ONE of them (Notify once it draws icons) so nothing shows twice.
    public static class GameEvents
    {
        public static event Action<long> MoneyChanged;
        public static event Action<long> FollowersChanged;
        /// First time a species is registered in the Sealog (round 2: by a Sealog photo, K.2): (species, sizeCm, tier)
        public static event Action<SpeciesDef, float, SizeTier> SpeciesDiscovered;
        /// A bigger specimen than the journal record was registered (not raised on first discovery)
        public static event Action<SpeciesDef, float, SizeTier> SizeRecord;
        public static event Action<Milestone> MilestoneReached;
        public static event Action<DiveSite> SiteUnlocked;
        /// Flattened mirror of every notification ("line · chip"). Prefer Notify.
        public static event Action<string> Toast;
        /// Big centered banner: (text, color)
        public static event Action<string, Color> Banner;
        public static event Action<int> DayStarted;
        public static event Action PlayerFainted;
        public static event Action GearChanged;
        public static event Action LanguageChanged;
        public static event Action<GearItem> DeliveryOrdered;
        /// Crate picked up and item received
        public static event Action<GearItem> DeliveryReceived;
        public static event Action<PendingUpload> UploadCreated;
        public static event Action<VideoRecord> UploadResolved;
        /// A predator bumped the player: air seconds lost
        public static event Action<float> PlayerBumped;
        public static event Action<bool> UnderwaterChanged;
        public static event Action<bool> RecordingChanged;
        public static event Action<FootageClip> ClipRecorded;
        public static event Action GameStarted;
        public static event Action ReturnedToMenu;
        /// Tutorial / objective text (empty string = none). Round 2b: the top-centre quest line reads QuestTracker.
        public static event Action<string> QuestChanged;
        public static event Action PhoneOpened;
        public static event Action<int> FishCaught;
        public static event Action<string> SponsorUnlocked;
        /// The host started sleeping (fade to the next morning). The Net module mirrors the fade on clients.
        public static event Action SleepStarted;

        // ---------------------------------------------------------------- round 2 (J.3)
        /// NPC / radio line for subtitles + babble: (speakerId, localized text, seconds).
        public static event Action<string, string, float> NpcLine;
        /// Radio call started (true) / ended (false): (speakerId, started).
        public static event Action<string, bool> RadioCall;
        public static event Action StreamStarted;
        /// (fainted, money earned, followers delta – negative after a faint).
        public static event Action<bool, long, long> StreamEnded;
        public static event Action<DareInfo> DareChanged;
        public static event Action<FoodDef> FoodEaten;
        public static event Action BuffsChanged;
        public static event Action<AchievementDef> AchievementUnlocked;
        public static event Action<string> CosmeticUnlocked;
        /// Fauna mischief kind id ("octopus_lamp", "fish_nibble", "sealion_bump", "gerald_photobomb", "parrot_poop", "crab_pinch").
        public static event Action<string> Mischief;
        /// Net id of a player who became unconscious (F.1).
        public static event Action<int> PlayerUnconscious;
        /// (revived player id, rescuer id).
        public static event Action<int, int> PlayerRevived;
        public static event Action BoatReturnedHome;
        /// Gus mission id whose state changed.
        public static event Action<string> MissionChanged;
        /// New channel rank index (Progression.Ranks).
        public static event Action<int> RankChanged;

        // ---------------------------------------------------------------- round 2b (J.3b)
        /// (kind, short line, optional chip).
        public static event Action<NotifyKind, string, string> Notify;
        public static event Action<string> FaunaEventStarted;
        public static event Action<string> FaunaEventEnded;
        /// Sponsor deal id whose state changed (offer added/expired, accepted, cancelled, done, failed).
        public static event Action<string> DealChanged;
        public static event Action<string> SideQuestChanged;
        /// Sightings counted for a species (species, count) – L.2.
        public static event Action<SpeciesDef, int> Sighted;
        /// A Sealog photo registered a species (K.2). PhotoEntry may be null on the host for a guest's photo.
        public static event Action<PhotoEntry, SpeciesDef> SealogPhoto;

        // ---------------------------------------------------------------- core extras (round 2b)
        /// A shared story flag was set (StoryFlags.Set) – raised on every machine (clients: after the host snapshot).
        public static event Action<string> StoryFlagSet;
        /// Research task id whose progress changed (Dr. Fin).
        public static event Action<string> ResearchChanged;
        /// Today's find/salvage id was taken (K.3).
        public static event Action<string> FindTaken;
        /// A daily photo challenge was completed (K.3).
        public static event Action<string> ChallengeCompleted;
        /// The local player picked up / put down a carried item (old id, new id; null = empty hands).
        public static event Action<string, string> CarryChanged;
        /// A delivery mishap happened (delivery item id, mishap) – L.12, raised by the Boat module.
        public static event Action<string, DeliveryMishap> DeliveryMishapHappened;
        /// Round 3: a fight moment of the LOCAL player (kind "hit" / "hurt" / "block" / "knockout", intensity 0..1) –
        /// the livestream turns it into a hype spike (viewers love fights). Raised by Combat.
        public static event Action<string, float> Fight;

        public static void RaiseMoneyChanged(long v) => MoneyChanged?.Invoke(v);
        public static void RaiseFollowersChanged(long v) => FollowersChanged?.Invoke(v);
        public static void RaiseSpeciesDiscovered(SpeciesDef s, float cm, SizeTier t) => SpeciesDiscovered?.Invoke(s, cm, t);
        public static void RaiseSizeRecord(SpeciesDef s, float cm, SizeTier t) => SizeRecord?.Invoke(s, cm, t);
        public static void RaiseMilestoneReached(Milestone m) => MilestoneReached?.Invoke(m);
        public static void RaiseSiteUnlocked(DiveSite s) => SiteUnlocked?.Invoke(s);
        /// Legacy toast = an Info notification.
        public static void RaiseToast(string text) => RaiseNotify(NotifyKind.Info, text, null);
        public static void RaiseBanner(string text, Color color) => Banner?.Invoke(text, color);
        public static void RaiseDayStarted(int day) => DayStarted?.Invoke(day);
        public static void RaisePlayerFainted() => PlayerFainted?.Invoke();
        public static void RaiseGearChanged() => GearChanged?.Invoke();
        public static void RaiseLanguageChanged() => LanguageChanged?.Invoke();
        public static void RaiseDeliveryOrdered(GearItem item) => DeliveryOrdered?.Invoke(item);
        public static void RaiseDeliveryReceived(GearItem item) => DeliveryReceived?.Invoke(item);
        public static void RaiseUploadCreated(PendingUpload u) => UploadCreated?.Invoke(u);
        public static void RaiseUploadResolved(VideoRecord v) => UploadResolved?.Invoke(v);
        public static void RaisePlayerBumped(float airLost) => PlayerBumped?.Invoke(airLost);
        public static void RaiseUnderwaterChanged(bool under) => UnderwaterChanged?.Invoke(under);
        public static void RaiseRecordingChanged(bool rec) => RecordingChanged?.Invoke(rec);
        public static void RaiseClipRecorded(FootageClip c) => ClipRecorded?.Invoke(c);
        public static void RaiseGameStarted() => GameStarted?.Invoke();
        public static void RaiseReturnedToMenu() => ReturnedToMenu?.Invoke();
        public static void RaiseQuestChanged(string text) => QuestChanged?.Invoke(text);
        public static void RaisePhoneOpened() => PhoneOpened?.Invoke();
        public static void RaiseFishCaught(int value) => FishCaught?.Invoke(value);
        public static void RaiseSponsorUnlocked(string siteId) => SponsorUnlocked?.Invoke(siteId);
        public static void RaiseSleepStarted() => SleepStarted?.Invoke();

        public static void RaiseNpcLine(string speakerId, string text, float seconds) => NpcLine?.Invoke(speakerId, text, seconds);
        public static void RaiseRadioCall(string speakerId, bool started) => RadioCall?.Invoke(speakerId, started);
        public static void RaiseStreamStarted() => StreamStarted?.Invoke();
        public static void RaiseStreamEnded(bool fainted, long money, long followersDelta) => StreamEnded?.Invoke(fainted, money, followersDelta);
        public static void RaiseDareChanged(DareInfo d) => DareChanged?.Invoke(d);
        public static void RaiseFoodEaten(FoodDef f) => FoodEaten?.Invoke(f);
        public static void RaiseBuffsChanged() => BuffsChanged?.Invoke();
        public static void RaiseAchievementUnlocked(AchievementDef a) => AchievementUnlocked?.Invoke(a);
        public static void RaiseCosmeticUnlocked(string id) => CosmeticUnlocked?.Invoke(id);
        public static void RaiseMischief(string kind) => Mischief?.Invoke(kind);
        public static void RaisePlayerUnconscious(int playerId) => PlayerUnconscious?.Invoke(playerId);
        public static void RaisePlayerRevived(int playerId, int rescuerId) => PlayerRevived?.Invoke(playerId, rescuerId);
        public static void RaiseBoatReturnedHome() => BoatReturnedHome?.Invoke();
        public static void RaiseMissionChanged(string id) => MissionChanged?.Invoke(id);
        public static void RaiseRankChanged(int rank) => RankChanged?.Invoke(rank);

        /// Shows a notification: icon (kind) + one short line + optional chip ("+3", "2.500 F", "7/24").
        /// Also mirrored to Toast as "line · chip" for legacy listeners.
        public static void RaiseNotify(NotifyKind kind, string line, string chip = null)
        {
            if (string.IsNullOrEmpty(line)) return;
            Notify?.Invoke(kind, line, chip);
            Toast?.Invoke(string.IsNullOrEmpty(chip) ? line : line + " · " + chip);
        }

        public static void RaiseFaunaEventStarted(string id) => FaunaEventStarted?.Invoke(id);
        public static void RaiseFaunaEventEnded(string id) => FaunaEventEnded?.Invoke(id);
        public static void RaiseDealChanged(string id) => DealChanged?.Invoke(id);
        public static void RaiseSideQuestChanged(string id) => SideQuestChanged?.Invoke(id);
        public static void RaiseSighted(SpeciesDef s, int count) => Sighted?.Invoke(s, count);
        public static void RaiseSealogPhoto(PhotoEntry photo, SpeciesDef s) => SealogPhoto?.Invoke(photo, s);

        public static void RaiseStoryFlagSet(string flag) => StoryFlagSet?.Invoke(flag);
        public static void RaiseResearchChanged(string id) => ResearchChanged?.Invoke(id);
        public static void RaiseFindTaken(string id) => FindTaken?.Invoke(id);
        public static void RaiseChallengeCompleted(string id) => ChallengeCompleted?.Invoke(id);
        public static void RaiseCarryChanged(string oldId, string newId) => CarryChanged?.Invoke(oldId, newId);
        public static void RaiseDeliveryMishap(string itemId, DeliveryMishap m) => DeliveryMishapHappened?.Invoke(itemId, m);
        public static void RaiseFight(string kind, float intensity) => Fight?.Invoke(kind, intensity);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            MoneyChanged = null; FollowersChanged = null; SpeciesDiscovered = null; SizeRecord = null;
            MilestoneReached = null; SiteUnlocked = null; Toast = null; Banner = null; DayStarted = null;
            PlayerFainted = null; GearChanged = null; LanguageChanged = null; DeliveryOrdered = null;
            DeliveryReceived = null; UploadCreated = null; UploadResolved = null; PlayerBumped = null;
            UnderwaterChanged = null; RecordingChanged = null; ClipRecorded = null; GameStarted = null;
            ReturnedToMenu = null; QuestChanged = null; PhoneOpened = null; FishCaught = null; SponsorUnlocked = null;
            SleepStarted = null;
            NpcLine = null; RadioCall = null; StreamStarted = null; StreamEnded = null; DareChanged = null;
            FoodEaten = null; BuffsChanged = null; AchievementUnlocked = null; CosmeticUnlocked = null; Mischief = null;
            PlayerUnconscious = null; PlayerRevived = null; BoatReturnedHome = null; MissionChanged = null; RankChanged = null;
            Notify = null; FaunaEventStarted = null; FaunaEventEnded = null; DealChanged = null; SideQuestChanged = null;
            Sighted = null; SealogPhoto = null;
            StoryFlagSet = null; ResearchChanged = null; FindTaken = null; ChallengeCompleted = null; CarryChanged = null;
            DeliveryMishapHappened = null; Fight = null;
        }
    }
}
