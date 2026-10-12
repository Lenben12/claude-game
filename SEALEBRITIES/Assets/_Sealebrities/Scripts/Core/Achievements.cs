using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Achievements for everyone present (F.6). Data only this round (Steam later – Steam achievements are NOT used).
    /// Round 3 step 6b (GDD §19b „Erfolge je Welt“): achievements belong to the WORLD (save slot), not to the PC or the
    /// account: the truth is GameState.achievements of the world being played (the host's save; guests see the host's
    /// list through the snapshot). The old per-PC list (Settings.Data.achievements) is no longer read or written.
    /// Unlock on the host → GameState.achievements + every connected player's PlayerRecord (in the host's save);
    /// each machine shows the notification once per world – remembered in its own PlayerRecord (PlayerRecords.Local).
    /// Clients forward CoreCommands.AchievementKey; they learn about unlocks from the host's snapshot (ApplyHostSnapshot
    /// diff) or the Net module's Achievement message (95) – both end in ApplyLocal, which is idempotent.
    public static class Achievements
    {
        static GameState S => GameManager.I != null ? GameManager.I.State : null;

        /// Unlocked in the world being played (main menu: false).
        public static bool IsUnlocked(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            var s = S;
            return s != null && s.achievements != null && s.achievements.Contains(id);
        }

        /// Unlocked / total in the world being played (save-slot cards read the same from the file).
        public static int UnlockedCount
        {
            get
            {
                var s = S;
                if (s == null || s.achievements == null) return 0;
                int n = 0;
                for (int i = 0; i < s.achievements.Count; i++) if (AchievementDatabase.Get(s.achievements[i]) != null) n++;
                return n;
            }
        }

        public static int Total => AchievementDatabase.All.Count;

        /// Unlocks an achievement for the channel and everybody present. Returns true when it was new (host/offline).
        public static bool Unlock(string id)
        {
            var def = AchievementDatabase.Get(id);
            var s = S;
            if (def == null || s == null || GameManager.I == null || !GameManager.I.InGame) return false;
            if (Net.IsClient)
            {
                if (!s.achievements.Contains(id)) Net.Command(NetCommand.Custom, CoreCommands.AchievementKey, id);
                return false;
            }
            if (s.achievements.Contains(id)) return false;   // already unlocked in this world
            s.achievements.Add(id);
            // every connected crew member's record (F.6)
            var players = Net.Players;
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (p == null || p.IsLocal || string.IsNullOrEmpty(p.key)) continue;
                var rec = PlayerRecords.Find(p.key);
                if (rec != null && !rec.achievements.Contains(id)) rec.achievements.Add(id);
            }
            ApplyLocal(id);   // the host's own record + the notification
            Cosmetics.CheckRules();
            return true;
        }

        /// Shows an achievement on THIS machine once per world and remembers it in the local player's record
        /// (idempotent). Net calls it for message 95; the snapshot diff ends here too.
        public static void ApplyLocal(string id)
        {
            var def = AchievementDatabase.Get(id);
            if (def == null) return;
            var local = PlayerRecords.Local;
            if (local == null || local.achievements.Contains(id)) return;
            local.achievements.Add(id);
            GameEvents.RaiseAchievementUnlocked(def);
            GameEvents.RaiseNotify(NotifyKind.Achievement, Loc.Tr("Erfolg: ", "Achievement: ") + def.Name);
        }

        /// Adds achievements to the local record WITHOUT a notification (a guest's record from the host on joining).
        public static void RememberSilently(List<string> ids)
        {
            var local = PlayerRecords.Local;
            if (local == null || ids == null) return;
            for (int i = 0; i < ids.Count; i++)
            {
                string id = ids[i];
                if (!string.IsNullOrEmpty(id) && !local.achievements.Contains(id)) local.achievements.Add(id);
            }
        }

        // ---------------------------------------------------------------- automatic hooks (data only)
        static bool hooked;

        /// Subscribes the core-observable conditions once (GameManager.Awake).
        public static void Hook()
        {
            if (hooked) return;
            hooked = true;
            GameEvents.FishCaught += _ => Unlock("first_fish");
            GameEvents.UploadCreated += _ => Unlock("first_upload");
            GameEvents.SpeciesDiscovered += (def, cm, tier) => { if (tier == SizeTier.Legendary) Unlock("legendary"); if (def != null && def.id == "humpback") Unlock("big_bertha"); };
            GameEvents.SizeRecord += (def, cm, tier) => { if (tier == SizeTier.Legendary) Unlock("legendary"); };
            Collectibles.Found += (id, kind) => CheckCollections();
            Net.PlayerJoined += _ => { if (Net.HasAuthority && Net.Players.Count >= Net.MaxPlayers) Unlock("full_crew"); };
            GameEvents.DeliveryMishapHappened += (item, m) => { if (m == DeliveryMishap.HutRoof) Unlock("roof_delivery"); };
            GameEvents.Mischief += kind => { if (kind == "octopus_lamp") Unlock("light_fingers"); };
        }

        /// f100 … f1m from the follower milestones (Progression.CheckMilestones, host).
        public static void OnFollowerMilestone(long followers)
        {
            if (followers >= 100) Unlock("f100");
            if (followers >= 1000) Unlock("f1k");
            if (followers >= 10000) Unlock("f10k");
            if (followers >= 100000) Unlock("f100k");
            if (followers >= 1000000) Unlock("f1m");
        }

        /// honest10 / clickbait10 – after uploads resolved (host).
        public static void CheckTitleCounts()
        {
            var s = S;
            if (s == null || !Net.HasAuthority) return;
            int honest = 0, clickbait = 0;
            for (int i = 0; i < s.videos.Count; i++)
            {
                var v = s.videos[i];
                if (v == null) continue;
                if (v.titleStyle == (int)TitleStyle.Honest) honest++;
                else if (v.titleStyle == (int)TitleStyle.Clickbait) clickbait++;
            }
            if (honest >= 10) Unlock("honest10");
            if (clickbait >= 10) Unlock("clickbait10");
        }

        /// shells_all / pages_all once the props module registered the totals.
        public static void CheckCollections()
        {
            if (!Net.HasAuthority) return;
            if (Full(CollectibleKind.GoldenShell)) Unlock("shells_all");
            if (Full(CollectibleKind.LogbookPage)) Unlock("pages_all");
            Cosmetics.CheckRules();
        }

        static bool Full(CollectibleKind k) { int t = Collectibles.Total(k); return t > 0 && Collectibles.Count(k) >= t; }

        /// Per-player counters (coconut10, grill_master, snap) – the local machine reports, the host unlocks for all.
        public static void OnLocalCounter(PlayerRecord r)
        {
            if (r == null) return;
            if (r.coconuts >= 10) Unlock("coconut10");
            if (r.grilled >= 20) Unlock("grill_master");
            if (r.snaps >= 5) Unlock("snap");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { hooked = false; }
    }
}
