using System;
using UnityEngine;

namespace Sealebrities
{
    /// Global event bus. Systems raise events here; UI and other systems listen. Never null-check callers: use the Raise* methods.
    public static class GameEvents
    {
        public static event Action<long> MoneyChanged;
        public static event Action<long> FollowersChanged;
        /// First time a species is filmed well enough: (species, sizeCm, tier)
        public static event Action<SpeciesDef, float, SizeTier> SpeciesDiscovered;
        /// A bigger specimen than the journal record was filmed (not raised on first discovery)
        public static event Action<SpeciesDef, float, SizeTier> SizeRecord;
        public static event Action<Milestone> MilestoneReached;
        public static event Action<DiveSite> SiteUnlocked;
        /// Short info line, e.g. "+30 $ Fisch verkauft"
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
        /// Tutorial / objective text (empty string = none)
        public static event Action<string> QuestChanged;
        public static event Action PhoneOpened;
        public static event Action<int> FishCaught;
        public static event Action<string> SponsorUnlocked;

        public static void RaiseMoneyChanged(long v) => MoneyChanged?.Invoke(v);
        public static void RaiseFollowersChanged(long v) => FollowersChanged?.Invoke(v);
        public static void RaiseSpeciesDiscovered(SpeciesDef s, float cm, SizeTier t) => SpeciesDiscovered?.Invoke(s, cm, t);
        public static void RaiseSizeRecord(SpeciesDef s, float cm, SizeTier t) => SizeRecord?.Invoke(s, cm, t);
        public static void RaiseMilestoneReached(Milestone m) => MilestoneReached?.Invoke(m);
        public static void RaiseSiteUnlocked(DiveSite s) => SiteUnlocked?.Invoke(s);
        public static void RaiseToast(string text) => Toast?.Invoke(text);
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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            MoneyChanged = null; FollowersChanged = null; SpeciesDiscovered = null; SizeRecord = null;
            MilestoneReached = null; SiteUnlocked = null; Toast = null; Banner = null; DayStarted = null;
            PlayerFainted = null; GearChanged = null; LanguageChanged = null; DeliveryOrdered = null;
            DeliveryReceived = null; UploadCreated = null; UploadResolved = null; PlayerBumped = null;
            UnderwaterChanged = null; RecordingChanged = null; ClipRecorded = null; GameStarted = null;
            ReturnedToMenu = null; QuestChanged = null; PhoneOpened = null; FishCaught = null; SponsorUnlocked = null;
        }
    }
}
