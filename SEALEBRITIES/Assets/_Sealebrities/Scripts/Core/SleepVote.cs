using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// L.9 sleeping in co-op: everybody must lie in a bed / hammock / bunk – "Schlafen 1/2 · 2/2". Solo: E at the bed
    /// from 18:00 sleeps at once (E.3, unchanged).
    ///
    /// Flow: the sleep spot (props) / player calls SetLying(true/false). Clients send the vote to the host – through the
    /// Net module's SleepVote message (99) when it set SendHandler, otherwise through CoreCommands.SleepVoteKey. The
    /// host counts every connected player (HostSet / Tick); when all lie for AllLyingDelay seconds it runs
    /// GameManager.SleepToMorning. The Net module broadcasts Lying/Total (SleepState 100, 2 Hz while > 0) and clients
    /// call ApplyState. Unconscious players and GUESTS in the pause menu must call SetLying(false) (player module / bed).
    /// The host stays lying while its pause menu is open – that is where „Schlafen erzwingen“ lives (ForceSleep).
    public static class SleepVote
    {
        /// Seconds "n/n – Gute Nacht!" is shown before the host skips the night.
        public const float AllLyingDelay = 2f;
        /// Host safety valve: after this many seconds at "n−1/n" the host's pause menu offers „Schlafen erzwingen“.
        public const float ForceSleepAfter = 60f;

        public static int Lying, Total;
        public static bool LocalLying;
        public static event Action Changed;

        /// Set by the Net module: client → host vote (SleepVote 99). Null → CoreCommands.SleepVoteKey fallback.
        public static Action<bool> SendHandler;

        // host bookkeeping
        static readonly List<int> lyingIds = new List<int>(4);
        static float allLyingTimer = -1f, almostTimer;

        /// True on the host while the "Force sleep" button may be shown.
        public static bool ForceSleepAvailable => Net.HasAuthority && Total > 1 && Lying == Total - 1 && almostTimer >= ForceSleepAfter;

        /// Local player lies down / gets up.
        public static void SetLying(bool lying)
        {
            var gm = GameManager.I;
            if (gm == null || !gm.InGame) return;
            if (!Net.IsOnline || Net.PlayerCount <= 1)
            {
                // solo: unchanged – lying down in the bed sleeps (E.3)
                LocalLying = false;
                if (lying) gm.SleepToMorning();
                return;
            }
            if (LocalLying == lying) return;
            LocalLying = lying;
            if (Net.IsClient)
            {
                if (SendHandler != null) SendHandler(lying);
                else Net.Command(NetCommand.Custom, CoreCommands.SleepVoteKey, lying ? "1" : "0");
                Changed?.Invoke();
                return;
            }
            HostSet(Net.LocalId, lying);
        }

        /// Host: a player's vote (own or from a client).
        public static void HostSet(int playerId, bool lying)
        {
            if (!Net.HasAuthority) return;
            int i = lyingIds.IndexOf(playerId);
            if (lying && i < 0) lyingIds.Add(playerId);
            else if (!lying && i >= 0) lyingIds.RemoveAt(i);
            else return;
            if (lying && playerId != Net.LocalId)
            {
                var p = Net.Find(playerId);
                string name = p != null && !string.IsNullOrEmpty(p.name) ? p.name : Loc.Tr("Jemand", "Someone");
                Recount();
                GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr($"{name} will schlafen", $"{name} wants to sleep"), Lying + "/" + Total);
                return;
            }
            Recount();
        }

        /// Clients: the host's SleepState (100).
        public static void ApplyState(int lying, int total)
        {
            if (lying == Lying && total == Total) return;
            Lying = Mathf.Max(0, lying);
            Total = Mathf.Max(0, total);
            Changed?.Invoke();
        }

        /// Host: forces the night skip (pause-menu button after 60 s at n−1/n, or CoreCommands.ForceSleepKey).
        public static bool ForceSleep()
        {
            var gm = GameManager.I;
            if (gm == null || !Net.HasAuthority || !ForceSleepAvailable) return false;
            Reset();
            gm.SleepToMorning();
            return true;
        }

        /// Called every frame by GameManager.
        public static void Tick(float dt)
        {
            if (!Net.HasAuthority || !Net.IsOnline) return;
            if (lyingIds.Count == 0 && Lying == 0) { allLyingTimer = -1f; almostTimer = 0f; return; }
            Recount();
            if (Total > 0 && Lying >= Total)
            {
                if (allLyingTimer < 0f) allLyingTimer = 0f;
                allLyingTimer += dt;
                if (allLyingTimer >= AllLyingDelay)
                {
                    Reset();
                    GameManager.I?.SleepToMorning();
                }
            }
            else allLyingTimer = -1f;
            if (Total > 1 && Lying == Total - 1) almostTimer += dt;
            else almostTimer = 0f;
        }

        static void Recount()
        {
            // drop players who left
            for (int i = lyingIds.Count - 1; i >= 0; i--) if (Net.Find(lyingIds[i]) == null && lyingIds[i] != Net.LocalId) lyingIds.RemoveAt(i);
            int total = Net.PlayerCount, lying = Mathf.Min(lyingIds.Count, total);
            if (lying == Lying && total == Total) return;
            Lying = lying;
            Total = total;
            Changed?.Invoke();
        }

        /// Everybody up (wake-up, session end, back to the menu).
        public static void Reset()
        {
            lyingIds.Clear();
            allLyingTimer = -1f;
            almostTimer = 0f;
            bool changed = Lying != 0 || LocalLying;
            Lying = 0;
            LocalLying = false;
            if (changed) Changed?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Lying = 0; Total = 0; LocalLying = false; Changed = null; SendHandler = null;
            lyingIds.Clear(); allLyingTimer = -1f; almostTimer = 0f;
        }
    }
}
