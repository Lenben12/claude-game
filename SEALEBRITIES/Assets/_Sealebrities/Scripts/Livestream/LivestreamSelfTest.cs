using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Sealebrities
{
    /// Automated check of the livestream + sponsor deal rules (Design_Runde2 J.6 "Livestream" and "(m) sponsors"), run
    /// only with the command line flag `-livestreamtest <folder>` (never touches the player's save or settings):
    ///   1. go-live rules (locked below 1,000 followers, only on the boat, once per day);
    ///   2. a real stream on the boat: viewers, chat, one dare accepted and completed (scripted success), money and
    ///      followers credited at the end, end summary, stream record;
    ///   3. a second stream ended by fainting: pending followers lost, the C.4 loss applied;
    ///   4. sponsor deals: two non-overlapping deals accepted, a third with a shared tag refused with the reason,
    ///      cancel makes the sponsor sulk, completing one pays the reward, the deal line shows in the quest tracker.
    /// Writes <folder>/livestream_selftest.txt and quits (exit code 0 = all passed).
    public sealed class LivestreamSelfTest : MonoBehaviour
    {
        string outDir;
        readonly StringBuilder log = new StringBuilder();
        int failed, passed, chatCount, donations;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Activate()
        {
            var args = Environment.GetCommandLineArgs();
            string dir = null;
            for (int i = 0; i < args.Length; i++)
                if (string.Equals(args[i], "-livestreamtest", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                    dir = args[i + 1].Trim().Trim('"');
            if (dir == null) return;
            GameManager.SavingDisabled = true;
            GameManager.AutomatedRun = true;
            Application.runInBackground = true;
            var go = new GameObject("SB_LivestreamSelfTest");
            DontDestroyOnLoad(go);
            go.AddComponent<LivestreamSelfTest>().outDir = dir;
        }

        void Start() { StartCoroutine(Run()); }

        void Note(string s)
        {
            Debug.Log("LivestreamSelfTest: " + s);
            log.AppendLine(DateTime.Now.ToString("HH:mm:ss", Inv) + "  " + s);
        }

        bool Check(bool ok, string what)
        {
            if (ok) passed++; else failed++;
            Note((ok ? "ok   " : "FAIL ") + what);
            return ok;
        }

        static IEnumerator Wait(float seconds)
        {
            float t = 0f;
            while (t < seconds) { t += Time.unscaledDeltaTime; yield return null; }
        }

        void OnChat(ChatMessage m) { chatCount++; if (m.donation > 0) donations++; }

        IEnumerator Run()
        {
            float waited = 0f;
            while (!BootState.Ready && waited < 180f) { waited += Time.unscaledDeltaTime; yield return null; }
            if (!BootState.Ready) { Note("boot did not finish"); Finish(3); yield break; }
            if (PlayerIntro.Playing) PlayerIntro.Skip();
            var ui = UIModule.Controller;
            if (ui != null && ui.Intro != null && ui.Intro.Active) ui.Intro.Skip();
            yield return Wait(1.5f);

            var gm = GameManager.I;
            gm.NewGame(Difficulty.OpenWater);
            yield return Wait(2f);
            var s = gm.State;
            LiveState.Chat += OnChat;
            try
            {
                Check(LivestreamModule.Stream != null && StreamService.Instance != null, "livestream module installed");
                Check(!KrillTv.CanGoLive(out string r0) && r0 == Loc.Tr("Ab 1.000 Followern", "From 1,000 followers"), "locked below 1,000 followers (" + r0 + ")");
            }
            catch (Exception e) { Check(false, "rules: " + e.Message); }

            gm.AddFollowers(1000 - s.followers);
            yield return null;
            Check(!KrillTv.CanGoLive(out string r1), "not on the boat → refused (" + r1 + ")");

            // onto the boat
            var player = Refs.Player;
            var boat = Refs.Boat;
            if (player != null && boat != null)
            {
                player.TeleportTo(boat.RescuePoint, boat.Root != null ? boat.Root.eulerAngles.y : 0f);
                yield return Wait(1f);
            }
            Check(KrillTv.StartPlace() == "boat", "standing on the boat (start place " + (KrillTv.StartPlace() ?? "none") + ")");
            long money0 = s.money, f0 = s.followers;

            // ---------------- 2. a real stream
            bool started = KrillTv.RequestStart();
            if (!started) started = KrillTv.DebugStart(true);
            Check(started && LiveState.IsLive, "stream started (live " + LiveState.IsLive + ")");
            Check(s.lastStreamDay == s.day, "once-per-day marker set");
            Hands.Select(Hand.Right, 0);   // camera in hand
            yield return Wait(20f);
            Check(LiveState.Viewers > 0, "viewers " + LiveState.Viewers + " (base " + StreamRules.BaseViewers(s.followers).ToString("0", Inv) + "), hype " + LiveState.Hype.ToString("0.00", Inv));
            Check(chatCount > 0, chatCount + " chat bubbles so far");
            bool offered = KrillTv.DebugOfferDare("dare_three");
            Check(offered && LiveState.Dare != null && LiveState.Dare.state == DareState.Offered, "dare offered to the local player");
            Check(KrillTv.LocalIsDareTarget || LiveState.Dare != null && LiveState.Dare.targetPlayerId == Net.LocalId, "local player is the target");
            yield return null;
            bool accepted = KrillTv.AcceptDare();
            Check(accepted && LiveState.Dare != null && LiveState.Dare.state == DareState.Running, "[T] accepted → running");
            long reward = LiveState.Dare != null ? LiveState.Dare.reward : 0;
            long beforeDare = s.money;
            Check(KrillTv.DebugCompleteDare(), "scripted success reported");
            Check(LiveState.Dare != null && LiveState.Dare.state == DareState.Done, "dare done");
            Check(s.money >= beforeDare + reward, "dare reward paid (+" + reward + " $)");
            yield return Wait(40f);
            long pending = LiveState.PendingFollowers;
            Check(pending > 0, "pending followers " + pending);
            KrillTv.RequestEnd();
            yield return null;
            var sum = KrillTv.LastSummary;
            Check(!LiveState.IsLive && sum != null && !sum.fainted, "stream ended normally, summary ready");
            Check(s.money > money0, "money earned: +" + (s.money - money0) + " $ (summary " + (sum != null ? sum.money : 0) + " $)");
            Check(s.followers > f0, "followers credited: +" + (s.followers - f0));
            Check(s.streams.Count == 1 && !s.streams[0].fainted, "stream record stored");
            Check(sum != null && !string.IsNullOrEmpty(sum.BestLine()), "best moment: " + (sum != null ? sum.BestLine() : ""));
            Check(!KrillTv.CanGoLive(out string r2) && r2 == Loc.Tr("Heute schon gestreamt", "Already streamed today"), "second stream today refused (" + r2 + ")");
            Note("chat bubbles " + chatCount + ", donations " + donations);

            // ---------------- 3. fainting ends the stream
            Check(KrillTv.DebugStart(true), "test stream 2 started");
            yield return Wait(5f);
            long fBefore = s.followers;
            long loss = Math.Min(Progression.StreamFaintLoss(fBefore, s.difficulty), fBefore);
            KrillTv.DebugFaint();
            yield return null;
            Check(!LiveState.IsLive && KrillTv.LastSummary != null && KrillTv.LastSummary.fainted, "faint ended the stream");
            Check(s.followers == fBefore - loss, "faint loss applied: −" + loss + " (" + fBefore + " → " + s.followers + ")");

            // ---------------- 4. sponsor deals
            yield return DealsTest(gm);

            LiveState.Chat -= OnChat;
            Finish(failed == 0 ? 0 : 1);
        }

        IEnumerator DealsTest(GameManager gm)
        {
            var s = gm.State;
            s.sponsor.offers.Clear();
            s.sponsor.active.Clear();
            s.sponsor.cooldowns.Clear();
            Check(SponsorService.DebugOffer("d_shark3"), "offer d_shark3 (shark)");
            Check(SponsorService.DebugOffer("d_noshark"), "offer d_noshark (turtle, shark)");
            Check(SponsorService.DebugOffer("d_fishroll"), "offer d_fishroll (fishing)");
            yield return null;
            DealState shark = Find(s, "d_shark3"), noshark = Find(s, "d_noshark"), fish = Find(s, "d_fishroll");
            Check(shark != null && Deals.Accept(shark.id), "accept the shark deal");
            bool can = Deals.CanAccept(noshark, out string reason, out var conflict, out string tag);
            Check(!can && conflict == shark && tag == "shark", "second shark deal refused: " + reason);
            Check(fish != null && Deals.Accept(fish.id), "accept a non-overlapping deal (2/2)");
            yield return null;
            Check(QuestTracker.Line != null, "quest line shows a deal: " + QuestTracker.Line + " " + QuestTracker.Progress);
            Check(Deals.Cancel(shark.id) && Deals.IsSulking("sharksoda"), "cancel → SharkSoda sulks 3 days");
            long m0 = s.money;
            var svc = SponsorService.Instance;
            for (int i = 0; i < 8; i++) GameEvents.RaiseFishCaught(5);
            yield return null;
            bool done = Find(s, "d_fishroll") == null && s.sponsor.done.Contains("d_fishroll");
            if (!done && svc != null && fish != null) { svc.HostAddProgress(fish.id, 8f, null); done = s.sponsor.done.Contains("d_fishroll"); }
            Check(done, "8 fish caught → deal done");
            Check(s.money >= m0 + (fish != null ? fish.reward : 0), "reward paid (+" + (fish != null ? fish.reward : 0) + " $)");
        }

        static DealState Find(GameState s, string dealId)
        {
            for (int i = 0; i < s.sponsor.offers.Count; i++) if (s.sponsor.offers[i] != null && s.sponsor.offers[i].dealId == dealId) return s.sponsor.offers[i];
            for (int i = 0; i < s.sponsor.active.Count; i++) if (s.sponsor.active[i] != null && s.sponsor.active[i].dealId == dealId) return s.sponsor.active[i];
            return null;
        }

        void Finish(int code)
        {
            Note("RESULT " + passed + " passed, " + failed + " failed");
            try
            {
                Directory.CreateDirectory(outDir);
                File.WriteAllText(Path.Combine(outDir, "livestream_selftest.txt"), log.ToString());
            }
            catch (Exception e) { Debug.LogError("LivestreamSelfTest: cannot write the report: " + e.Message); }
            Application.Quit(code);
        }
    }
}
