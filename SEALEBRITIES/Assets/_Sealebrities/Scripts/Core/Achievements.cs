using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Achievements for everyone present (F.6). Data only this round (Steam later).
    /// Unlock on the host → GameState.achievements + every connected player's PlayerRecord; each machine shows the
    /// notification and keeps its own list (Settings.Data.achievements – J.3 called it achievements.json).
    /// Clients forward CoreCommands.AchievementKey; they learn about unlocks from the host's snapshot (ApplyHostSnapshot
    /// diff) or the Net module's Achievement message (95) – both end in ApplyLocal, which is idempotent.
    public static class Achievements
    {
        static GameState S => GameManager.I != null ? GameManager.I.State : null;
        /// Automated test runs never write the player's settings: their unlocks live here only.
        static readonly HashSet<string> sessionOnly = new HashSet<string>();

        public static bool IsUnlocked(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            var s = S;
            if (s != null && s.achievements.Contains(id)) return true;
            return Settings.Data.achievements.Contains(id) || sessionOnly.Contains(id);
        }

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
            if (s.achievements.Contains(id)) { ApplyLocal(id); return false; }
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
            var local = PlayerRecords.Local;
            if (local != null && !local.achievements.Contains(id)) local.achievements.Add(id);
            ApplyLocal(id);
            Cosmetics.CheckRules();
            return true;
        }

        /// Shows + remembers an achievement on THIS machine (idempotent). Net calls it for message 95.
        public static void ApplyLocal(string id)
        {
            var def = AchievementDatabase.Get(id);
            if (def == null) return;
            var list = Settings.Data.achievements;
            if (list.Contains(id) || sessionOnly.Contains(id)) return;
            if (GameManager.AutomatedRun) sessionOnly.Add(id);
            else { list.Add(id); Settings.MarkDirty(); }
            var local = PlayerRecords.Local;
            if (local != null && !local.achievements.Contains(id)) local.achievements.Add(id);
            GameEvents.RaiseAchievementUnlocked(def);
            GameEvents.RaiseNotify(NotifyKind.Achievement, Loc.Tr("Erfolg: ", "Achievement: ") + def.Name);
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
        static void ResetStatics() { hooked = false; sessionOnly.Clear(); }
    }
}
