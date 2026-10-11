using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Sealebrities
{
    /// Test shortcuts for play-testing the late game (all 5 dive sites, expensive gear, the 1,000,000 finale), which
    /// otherwise takes 15–40 hours. Off by default. Switch on with Ctrl+Shift+F12 in game, or start the game with
    /// -cheats. Keys while on (only in a running game, not while a menu or the phone is open):
    ///   F5  +10,000 $ (Shift: +250,000 $)          F6  followers up to the next milestone
    ///   F7  unlock every dive site                  F8  own every buyable item
    ///   F9  +1 in-game hour                         F10 finish the tutorial
    /// Co-op: money and followers also work for guests (they go through the host like every shared change);
    /// the other keys only work for the host (or offline) and show a hint otherwise.
    public sealed class DebugCheats : MonoBehaviour
    {
        public static bool Enabled { get; private set; }

        static DebugCheats instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Enabled = false; instance = null; }

        /// Called by GameBootstrap (idempotent).
        public static void Install()
        {
            if (instance != null) return;
            var go = new GameObject("SB_DebugCheats");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<DebugCheats>();
            Enabled = HasArg("-cheats");
            if (Enabled) Debug.Log("Sealebrities: test cheats enabled (-cheats).");
        }

        void OnDestroy() { if (instance == this) instance = null; }

        static bool HasArg(string flag)
        {
            try
            {
                var args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length; i++)
                    if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch (Exception) { }
            return false;
        }

        void Update()
        {
            var gm = GameManager.I;
            if (gm == null || !gm.InGame || UIState.MainMenuOpen || UIState.LoadingOpen) return;

            bool ctrl = GameInput.KeyHeld(Key.LeftCtrl) || GameInput.KeyHeld(Key.RightCtrl);
            bool shift = GameInput.KeyHeld(Key.LeftShift) || GameInput.KeyHeld(Key.RightShift);
            if (ctrl && shift && GameInput.KeyDown(Key.F12))
            {
                Enabled = !Enabled;
                GameEvents.RaiseToast(Enabled
                    ? Loc.Tr("Testmodus an: F5 Geld · F6 Follower · F7 Tauchplätze · F8 Ausrüstung · F9 +1 Std · F10 Tutorial fertig",
                             "Test mode on: F5 money · F6 followers · F7 dive sites · F8 gear · F9 +1 h · F10 finish tutorial")
                    : Loc.Tr("Testmodus aus", "Test mode off"));
                return;
            }
            if (!Enabled || UIState.BlocksGameplay) return;

            if (GameInput.KeyDown(Key.F5)) AddMoney(gm, shift ? 250000 : 10000);
            else if (GameInput.KeyDown(Key.F6)) NextMilestone(gm);
            else if (GameInput.KeyDown(Key.F7)) { if (HostOnly()) UnlockAllSites(gm); }
            else if (GameInput.KeyDown(Key.F8)) { if (HostOnly()) GiveAllGear(gm); }
            else if (GameInput.KeyDown(Key.F9)) { if (HostOnly()) SkipHour(gm); }
            else if (GameInput.KeyDown(Key.F10)) { if (HostOnly()) FinishTutorial(gm); }
        }

        static bool HostOnly()
        {
            if (Net.HasAuthority) return true;
            GameEvents.RaiseToast(Loc.Tr("Test: nur der Host kann das", "Test: only the host can do that"));
            return false;
        }

        static void AddMoney(GameManager gm, long amount)
        {
            gm.AddMoney(amount);   // co-op guests: sent to the host
            GameEvents.RaiseToast("Test: +" + Loc.Money(amount));
        }

        static void NextMilestone(GameManager gm)
        {
            long f = gm.State.followers;
            var m = Progression.NextMilestone(f);
            long target = m != null ? m.followers : f + 100000;
            gm.AddFollowers(Math.Max(1, target - f));   // co-op guests: sent to the host; milestones/unlocks follow
            GameEvents.RaiseToast(Loc.Tr("Test: Follower → ", "Test: followers → ") + Loc.Number(target));
        }

        static void UnlockAllSites(GameManager gm)
        {
            var s = gm.State;
            int n = 0;
            foreach (var site in DiveSites.All)
            {
                if (site == null || s.unlockedSites.Contains(site.id)) continue;
                s.unlockedSites.Add(site.id);
                GameEvents.RaiseSiteUnlocked(site);
                n++;
            }
            gm.Save();
            GameEvents.RaiseToast(Loc.Tr($"Test: {n} Tauchplätze freigeschaltet", $"Test: {n} dive sites unlocked"));
        }

        static void GiveAllGear(GameManager gm)
        {
            var s = gm.State;
            int n = 0;
            foreach (var g in GearDatabase.All)
            {
                if (g == null || g.comingSoon || s.ownedItems.Contains(g.id)) continue;
                s.ownedItems.Add(g.id);
                n++;
            }
            GameEvents.RaiseGearChanged();
            gm.Save();
            GameEvents.RaiseToast(Loc.Tr($"Test: {n} Ausrüstungsteile erhalten", $"Test: {n} gear items added"));
        }

        static void SkipHour(GameManager gm)
        {
            // GameManager.Update rolls a value ≥ 1 over into a new day (host / offline)
            gm.State.timeOfDay += 1f / 24f;
            GameEvents.RaiseToast("Test: +1 h");
        }

        static void FinishTutorial(GameManager gm)
        {
            gm.SetQuestIndex(TutorialQuests.Finished);
            GameEvents.RaiseToast(Loc.Tr("Test: Tutorial erledigt", "Test: tutorial finished"));
        }
    }
}
