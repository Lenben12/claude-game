using System;
using System.Globalization;
using UnityEngine;

namespace Sealebrities
{
    /// Host side of the co-op command channel: applies a client's NetCommand to the shared GameState by calling the
    /// same public mutation methods the host uses itself (see the list in GameManager). The Net module's host handler
    /// only needs: `CoreCommands.Execute(fromId, cmd, args);`. Numbers are sent with the invariant culture.
    public static class CoreCommands
    {
        // ---- NetCommand.Custom keys (first argument) handled by core
        public const string AddFollowersKey = "AddFollowers";
        public const string SetQuestKey = "SetQuest";
        // round 2 (J.3)
        /// Handled by the Boat module (Boat/BoatHome.cs registers it with RegisterCustom) – not a core key.
        public const string BoatHomeKey = "BoatHome";
        public const string EatKey = "Eat";                     // "eat"|"add", foodId, n  → Pantry
        public const string GrillKey = "Grill";                 // fish uid                → Pantry.HostGrill
        public const string CoconutKey = "Coconut";             // palm id                 → GameState.dailyUsed
        public const string StoryFlagKey = "StoryFlag";         // flag ("-flag" clears)   → StoryFlags
        public const string MissionKey = "Mission";             // id, state, progress     → Missions.Set
        public const string ResearchKey = "Research";           // "add"|"set"|"done", id, value → Research
        public const string StreamStartKey = "StreamStart";     // livestream module (RegisterCustom)
        public const string StreamEndKey = "StreamEnd";         // livestream module (RegisterCustom)
        public const string DareKey = "Dare";                   // livestream module (RegisterCustom)
        public const string AchievementKey = "Achievement";     // id                      → Achievements.Unlock
        public const string CosmeticKey = "Cosmetic";           // id                      → Cosmetics.Unlock
        public const string PlayerRecordKey = "PlayerRecord";   // JSON PlayerRecord       → PlayerRecords.StoreFromClient
        // round 2b (J.3b)
        public const string SightingsKey = "Sightings";         // "id:n:min:max:zoneMask;…" → Journal.ApplySightingsBatch
        public const string DealKey = "Deal";                   // "accept"|"cancel", id   → Deals
        public const string FindKey = "Find";                   // "take"|"deliver"|"sell"|"sellall", id → Finds
        public const string ChallengeKey = "Challenge";         // id                      → Challenges.Complete
        public const string QuestKey = "Quest";                 // op, id, step, progress  → Quests
        public const string CarryKey = "Carry";                 // item id ("" = none)     → the sender's PlayerRecord.carried
        public const string LanternKey = "Lantern";             // –                       → Village.ReleaseLantern
        public const string ForceSleepKey = "ForceSleep";       // –                       → SleepVote.ForceSleep (host decides)
        /// Fallback sleep vote while the Net module has no SleepVote message (L.9): "1" lying / "0" up.
        public const string SleepVoteKey = "SleepVote";
        public const string KioskKey = "Kiosk";                 // foodId                  → Pantry.HostBuyAtKiosk (spend + add, atomic)

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// NetCommand.Custom keys handled by other modules (e.g. the livestream module's "StreamStart"): key →
        /// (fromId, args) → applied. Registered once at module install; run on the host only (Execute checks
        /// Net.HasAuthority). Core keys cannot be overridden.
        static readonly System.Collections.Generic.Dictionary<string, Func<int, string[], bool>> CustomHandlers =
            new System.Collections.Generic.Dictionary<string, Func<int, string[], bool>>();

        static bool IsCoreKey(string key)
        {
            switch (key)
            {
                case AddFollowersKey: case SetQuestKey: case EatKey: case GrillKey: case CoconutKey:
                case StoryFlagKey: case MissionKey: case ResearchKey: case AchievementKey: case CosmeticKey: case PlayerRecordKey:
                case SightingsKey: case DealKey: case FindKey: case ChallengeKey: case QuestKey: case CarryKey: case LanternKey:
                case ForceSleepKey: case SleepVoteKey: case KioskKey:
                    return true;
                default: return false;
            }
        }

        public static void RegisterCustom(string key, Func<int, string[], bool> handler)
        {
            if (string.IsNullOrEmpty(key) || IsCoreKey(key)) return;
            if (handler == null) CustomHandlers.Remove(key);
            else CustomHandlers[key] = handler;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { CustomHandlers.Clear(); }

        /// Returns true when the command was understood and applied.
        public static bool Execute(int fromId, NetCommand cmd, string[] args)
        {
            var gm = GameManager.I;
            if (gm == null || !gm.InGame || !Net.HasAuthority) return false;
            if (args == null) args = Array.Empty<string>();
            try
            {
                switch (cmd)
                {
                    case NetCommand.BankClips:
                    {
                        var batch = JsonUtility.FromJson<ClipBatch>(Arg(args, 0));
                        if (batch == null || batch.clips == null) return false;
                        Footage.BankClips(batch.clips);
                        return true;
                    }
                    case NetCommand.RegisterFilmed:
                    {
                        var def = SpeciesDatabase.Get(Arg(args, 0));
                        if (def == null) return false;
                        var tier = (SizeTier)Mathf.Clamp(I(Arg(args, 2)), 0, 4);
                        float cm = Mathf.Clamp(F(Arg(args, 1)), 0f, 5000f);
                        if (args.Length >= 6)
                        {
                            // K.2 Sealog photo: speciesId, sizeCm, tier, photoQuality, photoId, depth [, zoneBit]
                            Journal.RegisterPhoto(def, cm, tier, Mathf.Clamp01(F(Arg(args, 3))), Arg(args, 4), Mathf.Clamp(F(Arg(args, 5)), 0f, 200f), I(Arg(args, 6)));
                            return true;
                        }
                        Journal.RegisterFilmed(def, cm, tier, Mathf.Clamp(F(Arg(args, 3)), 0f, 100000f));
                        return true;
                    }
                    case NetCommand.Upload:
                    {
                        var style = (TitleStyle)Mathf.Clamp(I(Arg(args, 0)), 0, 2);
                        string[] thumbs = null;
                        string csv = Arg(args, 2);
                        if (!string.IsNullOrEmpty(csv)) thumbs = csv.Split(',');
                        bool custom = Arg(args, 3) == "1";
                        float tq = Mathf.Clamp01(F(Arg(args, 4)));
                        return Progression.CreateUpload(style, Arg(args, 1), thumbs, null, custom, tq) != null;
                    }
                    case NetCommand.Buy:
                    {
                        string id = Arg(args, 0);
                        string ex = Arg(args, 1);
                        bool express = ex == "1" || ex == "true" || ex == "True";
                        var food = FoodDatabase.FromDeliveryId(id);
                        if (food != null) return Shop.BuyFood(food, express);
                        var item = GearDatabase.Get(id);
                        if (item == null) return false;
                        return Shop.Buy(item, express);
                    }
                    case NetCommand.OpenCrate:
                    {
                        // the crate's arrival day + time pick the exact crate (older clients: the first crate of the item)
                        Delivery d = null;
                        if (args.Length >= 3) d = Shop.FindDelivery(Arg(args, 0), I(Arg(args, 1), -1), F(Arg(args, 2), -1f));
                        if (d == null) d = Shop.FindDelivery(Arg(args, 0));
                        if (d == null) return false;
                        Shop.ReceiveForRemote(d);
                        return true;
                    }
                    case NetCommand.Sleep:
                        gm.SleepToMorning();
                        return true;
                    case NetCommand.AddCatch:
                    {
                        var f = JsonUtility.FromJson<FishItem>(Arg(args, 0));
                        if (f == null || FishMarket.Kind(f.kindId) == null) return false;
                        f.value = Mathf.Clamp(f.value, 0, 10000);
                        f.weightKg = Mathf.Clamp(f.weightKg, 0f, 200f);
                        FishMarket.AddCatch(f);
                        return true;
                    }
                    case NetCommand.ListFish:
                        return FishMarket.ListByUid(I(Arg(args, 1)), I(Arg(args, 0), -1));
                    case NetCommand.ListAllFish:
                        return FishMarket.ListAll() > 0;
                    case NetCommand.Collect:
                    {
                        string id = Arg(args, 0);
                        if (string.IsNullOrEmpty(id)) return false;
                        return Collectibles.Collect(id, (CollectibleKind)Mathf.Clamp(I(Arg(args, 1)), 0, (int)CollectibleKind.Secret));
                    }
                    case NetCommand.AddMoney:
                        gm.AddMoney(L(Arg(args, 0)));
                        return true;
                    case NetCommand.Faint:
                        gm.ApplyRescueFee();
                        return true;
                    case NetCommand.Custom:
                        return ExecuteCustom(fromId, Arg(args, 0), args);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Net] command " + cmd + " from " + fromId + " failed: " + e.Message);
            }
            return false;
        }

        static bool ExecuteCustom(int fromId, string key, string[] args)
        {
            var gm = GameManager.I;
            switch (key)
            {
                case AddFollowersKey:
                    gm.AddFollowers(L(Arg(args, 1)));
                    return true;
                case SetQuestKey:
                {
                    int q = I(Arg(args, 1), -1);
                    if (q < 0 || q < gm.State.questIndex) return false;   // quests only move forward
                    gm.SetQuestIndex(q);
                    return true;
                }
                case EatKey:
                {
                    string op = Arg(args, 1), food = Arg(args, 2);
                    if (FoodDatabase.Get(food) == null) return false;
                    if (op == "eat") return Pantry.RemoveLocal(food);
                    if (op == "add") { Pantry.AddLocal(food, Mathf.Clamp(I(Arg(args, 3), 1), 1, 9)); return true; }
                    return false;
                }
                case GrillKey:
                    Pantry.HostGrill(I(Arg(args, 1), -1));
                    return true;
                case CoconutKey:
                {
                    string palm = Arg(args, 1);
                    if (string.IsNullOrEmpty(palm)) return false;
                    return Pantry.TryShakePalm(palm);
                }
                case StoryFlagKey:
                {
                    string flag = Arg(args, 1);
                    if (string.IsNullOrEmpty(flag)) return false;
                    if (flag[0] == '-') StoryFlags.Clear(flag.Substring(1));
                    else StoryFlags.Set(flag);
                    return true;
                }
                case MissionKey:
                    Missions.Set(Arg(args, 1), I(Arg(args, 2)), I(Arg(args, 3)));
                    return true;
                case ResearchKey:
                    return Research.Execute(Arg(args, 1), Arg(args, 2), Arg(args, 3));
                case AchievementKey:
                    Achievements.Unlock(Arg(args, 1));
                    return true;
                case CosmeticKey:
                    Cosmetics.Unlock(Arg(args, 1));
                    return true;
                case PlayerRecordKey:
                    return PlayerRecords.StoreFromClient(fromId, Arg(args, 1));
                case SightingsKey:
                    Journal.ApplySightingsBatch(Arg(args, 1));
                    return true;
                case DealKey:
                    return Deals.Execute(Arg(args, 1), Arg(args, 2));
                case FindKey:
                    return Finds.Execute(Arg(args, 1), Arg(args, 2));
                case ChallengeKey:
                    return Challenges.Complete(Arg(args, 1));
                case QuestKey:
                    return Quests.Execute(Arg(args, 1), Arg(args, 2), Arg(args, 3), Arg(args, 4));
                case CarryKey:
                {
                    var p = Net.Find(fromId);
                    var rec = p != null ? PlayerRecords.Find(p.key) : null;
                    string item = Arg(args, 1);
                    if (rec != null) rec.carried = string.IsNullOrEmpty(item) ? null : item;
                    return true;
                }
                case LanternKey:
                    return Village.ReleaseLantern();
                case ForceSleepKey:
                    return SleepVote.ForceSleep();
                case SleepVoteKey:
                    SleepVote.HostSet(fromId, Arg(args, 1) == "1");
                    return true;
                case KioskKey:
                    return Pantry.HostBuyAtKiosk(Arg(args, 1));
            }
            if (key != null && CustomHandlers.TryGetValue(key, out var handler) && handler != null) return handler(fromId, args);
            return false;
        }

        static string Arg(string[] a, int i) => i < a.Length ? a[i] : null;

        static float F(string s, float def = 0f) => s != null && float.TryParse(s, NumberStyles.Float, Inv, out var v) && !float.IsNaN(v) && !float.IsInfinity(v) ? v : def;
        static int I(string s, int def = 0) => s != null && int.TryParse(s, NumberStyles.Integer, Inv, out var v) ? v : def;
        static long L(string s, long def = 0) => s != null && long.TryParse(s, NumberStyles.Integer, Inv, out var v) ? v : def;
    }
}
