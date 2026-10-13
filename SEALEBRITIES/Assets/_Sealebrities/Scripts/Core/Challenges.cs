using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// K.3 daily photo challenges: 2 per day (shared, seeded from save seed + day), shown in the Tasks app and on the
    /// site buoy signs. The Filming module checks photos/clips (ChallengeDef.kind/args) and calls Complete.
    /// Reward: clamp(0.3 × ExpectedUploadMoney(F), 40, 400) $ + „+10 % Aufrufe heute“ on today's video.
    public static class Challenges
    {
        static GameState S => GameManager.I != null && GameManager.I.InGame ? GameManager.I.State : null;
        static readonly List<ChallengeDef> candidates = new List<ChallengeDef>();
        static readonly List<string> empty = new List<string>();

        public static event Action Changed;

        public static IReadOnlyList<string> Today => S != null ? (IReadOnlyList<string>)S.challengesToday : empty;

        public static bool IsToday(string id) { var s = S; return s != null && s.challengesToday.Contains(id); }
        public static bool IsDone(string id) { var s = S; return s != null && s.challengesDone.Contains(id); }

        public static long Reward(long followers) =>
            (long)Mathf.Clamp(ChallengeDatabase.RewardShare * Progression.ExpectedUploadMoney(followers), ChallengeDatabase.RewardMin, ChallengeDatabase.RewardMax);

        /// Host: picks today's challenges (NewGame, LoadGame without a roll, StartNewDay). Deterministic per save + day.
        public static void RollForDay(GameState s)
        {
            if (s == null || !Net.HasAuthority) return;
            s.challengesToday.Clear();
            s.challengesDone.Clear();
            candidates.Clear();
            bool lantern = StoryRules.IsLanternDay(s.day);
            var all = ChallengeDatabase.All;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c.lanternOnly && !lantern) continue;
                if (!string.IsNullOrEmpty(c.needsSite) && !s.unlockedSites.Contains(c.needsSite)) continue;
                candidates.Add(c);
            }
            var rng = s.SeededRandom(1000 + s.day);
            for (int n = 0; n < ChallengeDatabase.PerDay && candidates.Count > 0; n++)
            {
                int pick = rng.Next(candidates.Count);
                s.challengesToday.Add(candidates[pick].id);
                candidates.RemoveAt(pick);
            }
            Changed?.Invoke();
        }

        /// A challenge was met (Filming). SHARED STATE: clients forward ChallengeKey; the host pays.
        public static bool Complete(string id)
        {
            var s = S;
            var gm = GameManager.I;
            if (s == null || gm == null || !s.challengesToday.Contains(id) || s.challengesDone.Contains(id)) return false;
            if (Net.IsClient) Net.Command(NetCommand.Custom, CoreCommands.ChallengeKey, id);
            s.challengesDone.Add(id);
            if (s.challengeBonusDay != s.day) { s.challengeBonusDay = s.day; s.challengeBonusCount = 0; }
            s.challengeBonusCount++;
            long reward = Reward(s.followers);
            if (Net.HasAuthority) gm.AddMoney(reward);
            var def = ChallengeDatabase.Get(id);
            GameEvents.RaiseNotify(NotifyKind.Event, def != null ? def.Title : Loc.Tr("Challenge geschafft", "Challenge done"), "+" + Loc.Money(reward) + " · +10 %");
            GameEvents.RaiseChallengeCompleted(id);
            Changed?.Invoke();
            return true;
        }

        public static void RaiseChanged() => Changed?.Invoke();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Changed = null; candidates.Clear(); }
    }
}
