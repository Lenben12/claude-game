using System;
using System.Globalization;
using UnityEngine;

namespace Sealebrities
{
    /// Side quests (L.7) and Toni's chain (K.8, id "toni") – shared state in GameState.sideQuests, host authority.
    /// The npc module owns the quest data (Data/SideQuestDatabase.cs), the flow, the lines and the rewards; core only
    /// keeps the state and the rules (max 3 active side quests; story chains do not count).
    /// Clients forward CoreCommands.QuestKey ("start" / "progress" / "done" / "track" / "drop") and apply optimistically.
    public static class Quests
    {
        /// L.7: at most 3 active side quests (+ tutorial, story mission, sponsor deals).
        public const int MaxActiveSide = 3;
        /// Story chains that do not count toward the side-quest limit.
        public const string ToniChain = "toni";

        static GameState S => GameManager.I != null && GameManager.I.InGame ? GameManager.I.State : null;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static bool IsStoryChain(string id) => id == ToniChain;

        public static QuestState Get(string id)
        {
            var s = S;
            if (s == null || string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < s.sideQuests.Count; i++) if (s.sideQuests[i] != null && s.sideQuests[i].id == id) return s.sideQuests[i];
            return null;
        }

        public static bool IsActive(string id) { var q = Get(id); return q != null && !q.done; }
        public static bool IsDone(string id) { var q = Get(id); return q != null && q.done; }

        public static int ActiveSideCount
        {
            get
            {
                var s = S;
                if (s == null) return 0;
                int n = 0;
                for (int i = 0; i < s.sideQuests.Count; i++)
                {
                    var q = s.sideQuests[i];
                    if (q != null && !q.done && !IsStoryChain(q.id)) n++;
                }
                return n;
            }
        }

        public static bool CanStart(string id, out string reason)
        {
            reason = null;
            if (S == null || string.IsNullOrEmpty(id)) { reason = "?"; return false; }
            var q = Get(id);
            if (q != null) { reason = q.done ? Loc.Tr("Schon erledigt", "Already done") : Loc.Tr("Läuft schon", "Already running"); return false; }
            if (!IsStoryChain(id) && ActiveSideCount >= MaxActiveSide)
            {
                reason = Loc.Tr("Schon 3 Nebenaufgaben aktiv", "Already 3 side quests active");
                return false;
            }
            return true;
        }

        /// Starts a quest (giver dialog „Annehmen“). Returns false when the limit is reached (the reason is notified).
        public static bool Start(string id)
        {
            var s = S;
            if (!CanStart(id, out string reason))
            {
                if (reason != null && s != null && Get(id) == null) GameEvents.RaiseNotify(NotifyKind.Warning, reason);
                return false;
            }
            if (Net.IsClient) Net.Command(NetCommand.Custom, CoreCommands.QuestKey, "start", id);
            s.sideQuests.Add(new QuestState { id = id, startedDay = s.day });
            // the pin is per player: the host running a guest's "start" must not pin it on the host's quest line
            if (string.IsNullOrEmpty(s.trackedQuest) && !(Net.IsHost && NetCommands.ScopeFrom > 0)) s.trackedQuest = id;
            GameEvents.RaiseSideQuestChanged(id);
            QuestTracker.Resolve();
            return true;
        }

        /// Sets step + progress of a running quest (progress counts all players – the host is the truth).
        public static void SetProgress(string id, int step, int progress)
        {
            var q = Get(id);
            if (q == null || q.done || (q.step == step && q.progress == progress)) return;
            if (Net.IsClient) Net.Command(NetCommand.Custom, CoreCommands.QuestKey, "progress", id, step.ToString(Inv), progress.ToString(Inv));
            q.step = Mathf.Max(0, step);
            q.progress = Mathf.Max(0, progress);
            GameEvents.RaiseSideQuestChanged(id);
        }

        /// Adds to the progress counter of the current step.
        public static void AddProgress(string id, int amount)
        {
            var q = Get(id);
            if (q == null || q.done || amount == 0) return;
            SetProgress(id, q.step, q.progress + amount);
        }

        /// Finishes a quest (the npc module pays the reward with GameManager.AddMoney / Cosmetics.Unlock …).
        public static void Complete(string id)
        {
            var s = S;
            var q = Get(id);
            if (s == null || q == null || q.done) return;
            if (Net.IsClient) Net.Command(NetCommand.Custom, CoreCommands.QuestKey, "done", id);
            q.done = true;
            if (s.trackedQuest == id) s.trackedQuest = null;
            QuestTracker.Complete(id);
            GameEvents.RaiseSideQuestChanged(id);
            QuestTracker.Resolve();
            if (Net.HasAuthority) Cosmetics.CheckRules();
        }

        /// Gives a running side quest up (frees a slot).
        public static void Drop(string id)
        {
            var s = S;
            var q = Get(id);
            if (s == null || q == null || q.done || IsStoryChain(id)) return;
            if (Net.IsClient) Net.Command(NetCommand.Custom, CoreCommands.QuestKey, "drop", id);
            s.sideQuests.Remove(q);
            if (s.trackedQuest == id) s.trackedQuest = null;
            GameEvents.RaiseSideQuestChanged(id);
            QuestTracker.Resolve();
        }

        /// Pins a task in THIS player's quest line (Tasks app „Verfolgen“; any id the QuestTracker knows, null =
        /// automatic). PER PLAYER (GameState.CopyPerPlayerFrom keeps it): one player's pin never moves the other's line.
        public static void Track(string id)
        {
            var s = S;
            if (s == null || s.trackedQuest == id) return;
            s.trackedQuest = string.IsNullOrEmpty(id) ? null : id;
            QuestTracker.Resolve();
        }

        /// Host side of QuestKey.
        public static bool Execute(string op, string id, string a, string b)
        {
            switch (op)
            {
                case "start": return Start(id);
                case "progress":
                    int.TryParse(a, NumberStyles.Integer, Inv, out int step);
                    int.TryParse(b, NumberStyles.Integer, Inv, out int prog);
                    SetProgress(id, step, prog);
                    return true;
                case "done": Complete(id); return true;
                case "drop": Drop(id); return true;
                case "track": return true;   // older clients: the pin is per player now – ignore remote pins
            }
            return false;
        }
    }

    /// Gus missions M1–M4 (B.2) in GameState.missions – state 0 locked, 1 active, 2 done. Host authority (MissionKey).
    public static class Missions
    {
        public const int Locked = 0, Active = 1, Done = 2;
        static GameState S => GameManager.I != null && GameManager.I.InGame ? GameManager.I.State : null;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static MissionState Get(string id)
        {
            var s = S;
            if (s == null || string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < s.missions.Count; i++) if (s.missions[i] != null && s.missions[i].id == id) return s.missions[i];
            return null;
        }

        public static int StateOf(string id) { var m = Get(id); return m != null ? m.state : Locked; }
        public static bool IsDone(string id) => StateOf(id) == Done;

        /// The one active Gus mission (max 1) or null.
        public static MissionState Current
        {
            get
            {
                var s = S;
                if (s == null) return null;
                for (int i = 0; i < s.missions.Count; i++) if (s.missions[i] != null && s.missions[i].state == Active) return s.missions[i];
                return null;
            }
        }

        public static void Set(string id, int state, int progress)
        {
            var s = S;
            if (s == null || string.IsNullOrEmpty(id)) return;
            var m = Get(id);
            if (m != null && m.state == state && m.progress == progress) return;
            if (Net.IsClient) Net.Command(NetCommand.Custom, CoreCommands.MissionKey, id, state.ToString(Inv), progress.ToString(Inv));
            if (m == null) { m = new MissionState { id = id }; s.missions.Add(m); }
            if (m.state == Done && state != Done) return;   // done stays done
            m.state = Mathf.Clamp(state, Locked, Done);
            m.progress = Mathf.Max(0, progress);
            if (m.state == Done && id == "M2") StoryFlags.Set(StoryKeys.RadioDiscount);
            GameEvents.RaiseMissionChanged(id);
            if (Net.HasAuthority && m.state == Done) Cosmetics.CheckRules();
        }
    }

    /// Dr. Fin research tasks (B.4, K.3) in GameState.research. Host authority (ResearchKey "add" / "set" / "done").
    public static class Research
    {
        static GameState S => GameManager.I != null && GameManager.I.InGame ? GameManager.I.State : null;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static ResearchState Get(string id)
        {
            var s = S;
            if (s == null || string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < s.research.Count; i++) if (s.research[i] != null && s.research[i].id == id) return s.research[i];
            return null;
        }

        static ResearchState GetOrAdd(string id)
        {
            var r = Get(id);
            if (r != null || S == null) return r;
            r = new ResearchState { id = id };
            S.research.Add(r);
            return r;
        }

        public static int ProgressOf(string id) { var r = Get(id); return r != null ? r.progress : 0; }
        public static bool IsDone(string id) { var r = Get(id); return r != null && r.done; }

        public static void AddProgress(string id, int amount)
        {
            if (S == null || string.IsNullOrEmpty(id) || amount == 0 || IsDone(id)) return;
            if (Net.IsClient) Net.Command(NetCommand.Custom, CoreCommands.ResearchKey, "add", id, amount.ToString(Inv));
            GetOrAdd(id).progress += amount;
            GameEvents.RaiseResearchChanged(id);
        }

        public static void SetProgress(string id, int progress)
        {
            if (S == null || string.IsNullOrEmpty(id) || IsDone(id) || ProgressOf(id) == progress) return;
            if (Net.IsClient) Net.Command(NetCommand.Custom, CoreCommands.ResearchKey, "set", id, progress.ToString(Inv));
            GetOrAdd(id).progress = Mathf.Max(0, progress);
            GameEvents.RaiseResearchChanged(id);
        }

        public static void Complete(string id)
        {
            if (S == null || string.IsNullOrEmpty(id) || IsDone(id)) return;
            if (Net.IsClient) Net.Command(NetCommand.Custom, CoreCommands.ResearchKey, "done", id);
            GetOrAdd(id).done = true;
            GameEvents.RaiseResearchChanged(id);
        }

        public static bool Execute(string op, string id, string value)
        {
            int.TryParse(value, NumberStyles.Integer, Inv, out int v);
            switch (op)
            {
                case "add": AddProgress(id, v); return true;
                case "set": SetProgress(id, v); return true;
                case "done": Complete(id); return true;
            }
            return false;
        }
    }

    /// Carried quest items and heavy salvage of the LOCAL player (K.3, L.7): one item in both hands
    /// (HoldItem.QuestItem; the camera goes to the chest strap and pauses recording). Per player (GameState.carried,
    /// mirrored into the player's record); the host learns it through CoreCommands.CarryKey for the other avatars.
    public static class Carry
    {
        /// K.3 heavy salvage: swim speed × 0.55, sinks 0.15 m/s unless swimming up, air use × 1.2; two carriers × 0.85 each.
        public const float HeavySwimMultiplier = 0.55f, HeavySinkSpeed = 0.15f, HeavyAirMultiplier = 1.2f, TwoCarrierMultiplier = 0.85f;
        /// The boat winch hooks salvage up to this far from the boat (K.3).
        public const float WinchRange = 40f;

        static GameState S => GameManager.I != null && GameManager.I.InGame ? GameManager.I.State : null;

        public static string Current => S != null ? S.carried : null;
        public static bool IsCarrying => !string.IsNullOrEmpty(Current);
        /// Heavy salvage ("salv:…") – slow swimming, both hands.
        public static bool IsHeavy => IsCarrying && Current.StartsWith("salv:", StringComparison.Ordinal);

        /// Picks an item up (quest item id, coconut, find/salvage id). Returns false when the hands are full.
        public static bool Pick(string itemId)
        {
            var s = S;
            if (s == null || string.IsNullOrEmpty(itemId) || IsCarrying) return false;
            Set(s, itemId);
            return true;
        }

        /// Puts the item down / hands it over / loses it when fainting. Returns the old id (null when empty).
        public static string Drop()
        {
            var s = S;
            if (s == null || !IsCarrying) return null;
            string old = s.carried;
            Set(s, null);
            return old;
        }

        static void Set(GameState s, string id)
        {
            string old = s.carried;
            s.carried = id;
            var rec = PlayerRecords.Local;
            if (rec != null) rec.carried = id;
            if (Net.IsClient) Net.Command(NetCommand.Custom, CoreCommands.CarryKey, id ?? "");
            GameEvents.RaiseCarryChanged(old, id);
        }
    }

    /// The village Altriff (L.8) – shared customs state.
    public static class Village
    {
        static GameState S => GameManager.I != null && GameManager.I.InGame ? GameManager.I.State : null;

        public static bool IsLanternEvening
        {
            get { var s = S; return s != null && StoryRules.IsLanternEvening(s.day, s.timeOfDay); }
        }

        public static bool LanternReleasedToday { get { var s = S; return s != null && s.lastLanternDay == s.day; } }

        /// "E – Laterne loslassen" on Lantern Evening: +5 % views on today's video (not stacking with the cozy bonus).
        public static bool ReleaseLantern()
        {
            var s = S;
            if (s == null || !IsLanternEvening || LanternReleasedToday) return false;
            if (Net.IsClient) Net.Command(NetCommand.Custom, CoreCommands.LanternKey);
            s.lastLanternDay = s.day;
            GameEvents.RaiseNotify(NotifyKind.Event, Loc.Tr("Laterne fliegt", "Lantern released"), "+5 %");
            return true;
        }
    }
}
