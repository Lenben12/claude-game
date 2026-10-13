using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Sealebrities
{
    /// Unlockable cosmetics (1c). Channel unlocks are shared (GameState.cosmetics, host authority); every machine also
    /// remembers what it has seen unlocked (Settings.Data.unlockedCosmetics) so the character screen in the main menu
    /// and a co-op guest's own games show them. Free options are always unlocked.
    public static class Cosmetics
    {
        static GameState S => GameManager.I != null && GameManager.I.InGame ? GameManager.I.State : null;
        static readonly HashSet<string> sessionOnly = new HashSet<string>();

        public static bool IsUnlocked(string id)
        {
            var def = CosmeticsDatabase.Get(id);
            if (def == null) return false;
            if (def.free) return true;
            var s = S;
            if (s != null && s.cosmetics.Contains(id)) return true;
            return Settings.Data.unlockedCosmetics.Contains(id) || sessionOnly.Contains(id);
        }

        public static bool IsUnlocked(string kind, int index) => IsUnlocked(CosmeticsDatabase.Id(kind, index));

        /// Unlocks a cosmetic for the channel (story rewards: npc module; automatic rules: CheckRules).
        /// SHARED STATE: clients forward CoreCommands.CosmeticKey and remember it locally at once.
        public static void Unlock(string id)
        {
            var def = CosmeticsDatabase.Get(id);
            if (def == null || def.free) return;
            var s = S;
            if (s == null) { RememberLocal(id, true); return; }
            if (s.cosmetics.Contains(id)) { RememberLocal(id, false); return; }
            if (Net.IsClient) Net.Command(NetCommand.Custom, CoreCommands.CosmeticKey, id);
            s.cosmetics.Add(id);
            RememberLocal(id, true);
        }

        /// Remembers an unlock on this machine and tells the player once (also called for host snapshot diffs).
        public static void RememberLocal(string id, bool notify)
        {
            var def = CosmeticsDatabase.Get(id);
            if (def == null || def.free) return;
            var list = Settings.Data.unlockedCosmetics;
            bool known = list.Contains(id) || sessionOnly.Contains(id);
            if (!known)
            {
                if (GameManager.AutomatedRun) sessionOnly.Add(id);
                else { list.Add(id); Settings.MarkDirty(); }
            }
            if (known || !notify) return;
            GameEvents.RaiseCosmeticUnlocked(id);
            string line;
            switch (def.kind)
            {
                case CosmeticsDatabase.Hat: line = Loc.Tr("Neuer Hut", "New hat"); break;
                case CosmeticsDatabase.Pattern: line = Loc.Tr("Neues Muster", "New pattern"); break;
                case CosmeticsDatabase.Eyes: line = Loc.Tr("Neue Augen", "New eyes"); break;
                case CosmeticsDatabase.Mask: line = Loc.Tr("Neuer Maskenrahmen", "New mask frame"); break;
                case CosmeticsDatabase.Fins: line = Loc.Tr("Neue Flossen", "New fins"); break;
                case CosmeticsDatabase.BoatFlag: line = Loc.Tr("Neue Bootsflagge", "New boat flag"); break;
                default: line = Loc.Tr("Neu freigeschaltet", "Unlocked"); break;
            }
            GameEvents.RaiseNotify(NotifyKind.Collectible, line, def.Name);
        }

        /// Host: evaluates every automatic unlock rule (cheap – called after milestones, collectibles, Sealog photos,
        /// achievements, story flags and at the start of a day; never per frame).
        public static void CheckRules()
        {
            var s = S;
            if (s == null || !Net.HasAuthority) return;
            var all = CosmeticsDatabase.All;
            for (int i = 0; i < all.Count; i++)
            {
                var d = all[i];
                if (d.free || string.IsNullOrEmpty(d.rule) || s.cosmetics.Contains(d.id)) continue;
                if (RuleMet(d.rule, s)) Unlock(d.id);
            }
        }

        static bool RuleMet(string rule, GameState s)
        {
            switch (rule)
            {
                case "shells_all": return Full(CollectibleKind.GoldenShell);
                case "pages_all": return Full(CollectibleKind.LogbookPage);
                case "treasures_all": return Full(CollectibleKind.Treasure);
                case "secrets_all": return Full(CollectibleKind.Secret);
                case "sealog_full": return Journal.IsComplete;
                case "tutorial": return s.storyFlags.Contains(StoryKeys.TutorialDone);
            }
            int colon = rule.IndexOf(':');
            if (colon <= 0) return false;
            string head = rule.Substring(0, colon), arg = rule.Substring(colon + 1);
            switch (head)
            {
                case "followers": return long.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out long f) && s.followers >= f;
                case "achievement": return Achievements.IsUnlocked(arg);
                case "gerald_days": return int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && s.geraldDays >= n;
                case "secret": return s.collected.Contains("secret:" + arg);
                default: return false;   // mission:/quest: – unlocked by the npc module
            }
        }

        static bool Full(CollectibleKind k) { int t = Collectibles.Total(k); return t > 0 && Collectibles.Count(k) >= t; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { sessionOnly.Clear(); }
    }
}
