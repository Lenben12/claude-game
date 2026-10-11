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
        /// NetCommand.Custom keys (first argument).
        public const string AddFollowersKey = "AddFollowers";
        public const string SetQuestKey = "SetQuest";

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

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
                        Journal.RegisterFilmed(def, F(Arg(args, 1)), (SizeTier)Mathf.Clamp(I(Arg(args, 2)), 0, 4), Mathf.Clamp(F(Arg(args, 3)), 0f, 100000f));
                        return true;
                    }
                    case NetCommand.Upload:
                    {
                        var style = (TitleStyle)Mathf.Clamp(I(Arg(args, 0)), 0, 2);
                        return Progression.CreateUpload(style, Arg(args, 1)) != null;
                    }
                    case NetCommand.Buy:
                    {
                        var item = GearDatabase.Get(Arg(args, 0));
                        if (item == null) return false;
                        string ex = Arg(args, 1);
                        return Shop.Buy(item, ex == "1" || ex == "true" || ex == "True");
                    }
                    case NetCommand.OpenCrate:
                    {
                        var d = Shop.FindDelivery(Arg(args, 0));
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
                        f.weightKg = Mathf.Clamp(f.weightKg, 0f, 100f);
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
                        return ExecuteCustom(Arg(args, 0), args);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Net] command " + cmd + " from " + fromId + " failed: " + e.Message);
            }
            return false;
        }

        static bool ExecuteCustom(string key, string[] args)
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
            }
            return false;
        }

        static string Arg(string[] a, int i) => i < a.Length ? a[i] : null;

        static float F(string s, float def = 0f) => s != null && float.TryParse(s, NumberStyles.Float, Inv, out var v) && !float.IsNaN(v) ? v : def;
        static int I(string s, int def = 0) => s != null && int.TryParse(s, NumberStyles.Integer, Inv, out var v) ? v : def;
        static long L(string s, long def = 0) => s != null && long.TryParse(s, NumberStyles.Integer, Inv, out var v) ? v : def;
    }
}
