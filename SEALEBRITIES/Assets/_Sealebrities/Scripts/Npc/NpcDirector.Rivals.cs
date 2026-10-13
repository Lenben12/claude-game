using System;
using UnityEngine;

namespace Sealebrities
{
    /// Round 3 (GDD §20 Q43 „Gegenspieler mit Sabotage – Motive klauen, Streams stören, Follower-Duelle“): the Bottom
    /// Feeders become real antagonists between their story visits (R1–R4, Visitors part):
    ///  • stolen subjects: some mornings they publish a copy of a species the crew filmed recently – film it better
    ///    today (quality ≥ 0.9) and the original wins (follower bonus, they delete their video);
    ///  • stream raids: once in a while they raid a running KRILL.tv stream (haters, hype drops) – a fight in the
    ///    next 45 s turns the raid around (big hype);
    ///  • the weekly follower duel (Visitors part) gets announced on the radio before duel day.
    /// Everything stops once the brothers became friends (R4 rescued). HOST only; machines hear radio / notifications.
    public sealed partial class NpcDirector
    {
        int raidSerialDone = -1;
        float raidUntil = -1f, liveSince = -1f, lastHostFight = -100f;
        bool raidAnswered, fightHooked;

        void ResetRivalSession()
        {
            raidSerialDone = -1;
            raidUntil = -1f;
            liveSince = -1f;
            raidAnswered = false;
            if (!fightHooked) { fightHooked = true; GameEvents.Fight += OnHostFight; }
        }

        void OnHostFight(string kind, float intensity)
        {
            if (Net.HasAuthority && (kind == "hit" || kind == "hurt" || kind == "block")) lastHostFight = Time.time;
        }

        static bool SabotageOn => StoryFlags.Has(NpcKeys.RivalR1Done) && !StoryFlags.Has(NpcKeys.RivalsFriends);

        // ================================================================ stolen subjects
        void SabotageNewDay()
        {
            var s = S;
            if (s == null || !SabotageOn) return;
            // the duel is announced the day before duel day (Monday = day % 7 == 1)
            if (s.day % 7 == 0) RadioAll(NpcDatabase.Chad, "rival.duelweek");
            // about every fourth morning, never on duel day
            if (s.day % 7 == 1 || NpcUtil.Hash01(s.day, 61) > 0.27f) return;
            string sp = RecentSpecies(s);
            if (sp == null) return;
            StoryFlags.Set(NpcKeys.RivalStealPrefix + sp + ":" + s.day.ToString(System.Globalization.CultureInfo.InvariantCulture));
            RadioAll(NpcDatabase.Chad, "rival.steal");
            var def = SpeciesDatabase.Get(sp);
            GameEvents.RaiseNotify(NotifyKind.Warning, Loc.Tr("Bottom Feeders kopieren: ", "Bottom Feeders copy: ") + (def != null ? def.Name : sp),
                Loc.Tr("Heute besser filmen!", "Film it better today!"));
            trackerDirty = true;
        }

        /// The species of the last video (else the newest banked clip) – what they copy.
        static string RecentSpecies(GameState s)
        {
            for (int i = s.videos.Count - 1; i >= 0; i--)
            {
                var v = s.videos[i];
                if (v != null && !string.IsNullOrEmpty(v.topSpeciesId)) { var d = SpeciesDatabase.Get(v.topSpeciesId); if (d != null && !d.deco && !d.meme) return v.topSpeciesId; }
            }
            for (int i = s.bankedFootage.Count - 1; i >= 0; i--)
                if (s.bankedFootage[i] != null && !string.IsNullOrEmpty(s.bankedFootage[i].speciesId)) return s.bankedFootage[i].speciesId;
            return null;
        }

        /// Today's stolen species (null = none / already won back).
        public static string StolenToday()
        {
            var s = S;
            if (s == null) return null;
            string dayS = ":" + s.day.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (StoryFlags.Has(NpcKeys.RivalStealWonPrefix + s.day.ToString(System.Globalization.CultureInfo.InvariantCulture))) return null;
            for (int i = s.storyFlags.Count - 1; i >= 0; i--)
            {
                string f = s.storyFlags[i];
                if (f == null || !f.StartsWith(NpcKeys.RivalStealPrefix, StringComparison.Ordinal) || !f.EndsWith(dayS, StringComparison.Ordinal)) continue;
                return f.Substring(NpcKeys.RivalStealPrefix.Length, f.Length - NpcKeys.RivalStealPrefix.Length - dayS.Length);
            }
            return null;
        }

        /// HOST: a clip of the copied species today, good enough → the original wins.
        void RivalStealClip(int fromId, string species, float quality)
        {
            string stolen = StolenToday();
            if (stolen == null || species != stolen || quality < 0.9f) return;
            var s = S;
            StoryFlags.Set(NpcKeys.RivalStealWonPrefix + s.day.ToString(System.Globalization.CultureInfo.InvariantCulture));
            long gain = (long)Mathf.Clamp(s.followers * 0.02f, 25f, 2500f);
            GameManager.I.AddFollowers(gain);
            GameEvents.RaiseNotify(NotifyKind.Followers, Loc.Tr("Original schlägt Kopie!", "The original wins!"), "+" + Loc.Number(gain));
            RadioAll(NpcDatabase.Brad, "rival.stealwon");
            trackerDirty = true;
        }

        // ================================================================ stream raids
        void ThinkSabotage()
        {
            if (!LiveState.IsLive) { liveSince = -1f; raidUntil = -1f; return; }
            float now = Time.time;
            if (liveSince < 0f) liveSince = now;
            if (raidUntil > 0f)
            {
                if (!raidAnswered && lastHostFight > raidUntil - 45f)
                {
                    raidAnswered = true;
                    var ss = StreamService.Instance;
                    if (ss != null) ss.ReportFightWon(false);
                    GameEvents.RaiseNotify(NotifyKind.Followers, Loc.Tr("Raid abgewehrt!", "Raid repelled!"), Loc.Tr("Hype ↑", "Hype ↑"));
                    RadioAll(NpcDatabase.Brad, "rival.raidlost");
                }
                if (now > raidUntil) raidUntil = -1f;
                return;
            }
            if (!SabotageOn || raidSerialDone == KrillTv.StreamSerial) return;
            float since = now - liveSince;
            if (since < 60f) return;
            raidSerialDone = KrillTv.StreamSerial;
            if (NpcUtil.Hash01(KrillTv.StreamSerial, NpcUtil.Day, 77) > 0.45f) return;   // not every stream
            raidUntil = now + 45f;
            raidAnswered = false;
            var svc = StreamService.Instance;
            if (svc != null) { svc.ReportFoul(2); svc.ReportFoul(1); }
            RadioAll(NpcDatabase.Chad, "rival.raid");
            GameEvents.RaiseNotify(NotifyKind.Warning, Loc.Tr("Stream-Raid der Bottom Feeders!", "Bottom Feeders raid your stream!"),
                Loc.Tr("Zeig einen Kampf!", "Show a fight!"));
        }
    }
}
