using System;
using UnityEngine;

namespace Sealebrities
{
    /// The "best moment" line of the end card (C.7: „Rekord: Riesen-Muräne 2,31 m“).
    public enum BestMoment : byte { None, Record, NewSpecies, Dare, Legendary, Biggest }

    /// Result of a finished stream (end card, KRILL.tv app). Built on the host, sent to every client (StreamState 90).
    public sealed class StreamSummary
    {
        public int serial;
        public bool fainted, drone;
        public float seconds;
        public int peakViewers;
        public long money;
        /// Followers credited (normal end) or lost (negative, fainted).
        public long followersDelta;
        public BestMoment best;
        public SpeciesDef bestSpecies;
        public float bestCm;
        public int bestDare = -1;

        /// Localized best-moment line ("" = none).
        public string BestLine()
        {
            string s = bestSpecies != null ? bestSpecies.Name : "";
            string cm = bestCm > 0f ? " " + Loc.Length(bestCm) : "";
            switch (best)
            {
                case BestMoment.Record: return Loc.Tr("Rekord: ", "Record: ") + s + cm;
                case BestMoment.NewSpecies: return Loc.Tr("Neu im Sealog: ", "New in the Sealog: ") + s;
                case BestMoment.Dare:
                    var d = StreamDares.At(bestDare);
                    return d != null ? Loc.Tr("Mutprobe: ", "Dare: ") + d.Done : "";
                case BestMoment.Legendary: return Loc.Tr("Legendär: ", "Legendary: ") + s + cm;
                case BestMoment.Biggest: return Loc.Tr("Größtes Tier: ", "Biggest animal: ") + s + cm;
                default: return "";
            }
        }
    }

    /// Public facade of the KRILL.tv livestream (C). The simulation runs in StreamService (host / offline); clients
    /// mirror it through StreamNet (90 StreamState, 91 StreamFeed, 92 StreamChat). Core's LiveState carries the
    /// numbers every module reads (IsLive, Viewers, Hype, Dare …); this class adds the start/end/dare requests, the
    /// go-live rules for the app and the extra overlay facts (dare user, local dare progress, end summary).
    public static class KrillTv
    {
        // ---------------------------------------------------------------- extra runtime state (host + mirrored)
        /// Serial of the current dare (changes with every new offer).
        public static int DareSerial;
        /// StreamDares index of the current dare (-1 = none).
        public static int DareIndex = -1;
        /// Chat user who proposed the dare (StreamChat.Users index).
        public static int DareUser;
        /// Local player has to do the current dare.
        public static bool LocalIsDareTarget;
        /// 0..1 progress of the local player's running dare (-1 = none / instant action).
        public static float DareProgress01 = -1f;
        /// Running stream number (changes with every start).
        public static int StreamSerial;
        /// Last finished stream (end card, app).
        public static StreamSummary LastSummary;
        /// Set by LivestreamModule: false when the UI module ships its own C.7 overlay/app (UIStreamOverlay).
        public static bool BuiltInUI = true;

        /// Definition of the current dare (risk, k, time limit, "done" name) – null when none.
        public static DareDef CurrentDare => LiveState.Dare != null ? StreamDares.At(DareIndex) : null;
        /// Name of the chat user who proposed the current dare (for the dare card).
        public static string DareUserName => DareUser >= 0 && DareUser < StreamChat.Users.Length ? StreamChat.Users[DareUser] : "krill";

        /// Stream started (every machine).
        public static event Action Started;
        /// Stream ended (every machine) – the end card.
        public static event Action<StreamSummary> Ended;
        /// Money arrived during the stream (ads batch, donation or dare reward) – the "+x $ live" ticker.
        public static event Action<long> MoneyTicked;

        internal static void RaiseStarted() => Started?.Invoke();
        internal static void RaiseEnded(StreamSummary s) => Ended?.Invoke(s);
        internal static void RaiseMoneyTicked(long v) { if (v > 0) MoneyTicked?.Invoke(v); }

        // ---------------------------------------------------------------- rules for the app (C.1, C.7)
        public static long Followers => GameManager.I != null && GameManager.I.InGame ? GameManager.I.State.followers : 0;
        public static bool Unlocked => Followers >= StreamRules.UnlockFollowers;

        public static bool StreamedToday
        {
            get
            {
                var gm = GameManager.I;
                return gm != null && gm.InGame && gm.State.lastStreamDay == gm.State.day && !LiveState.IsLive;
            }
        }

        /// Where the local player could start a stream right now ("boat", "dock" or null).
        public static string StartPlace()
        {
            var boat = Refs.Boat;
            if (boat != null && !(boat is UnityEngine.Object o && o == null) && boat.PlayerAboard) return "boat";
            var p = Refs.Player;
            if (Followers >= StreamRules.DockFollowers && p != null && !(p is UnityEngine.Object po && po == null)
                && !p.IsInWater && p.Root != null && StreamEye.IsOnDock(p.Root.position)) return "dock";
            return null;
        }

        /// The KRILL.tv app's „Live gehen“ button: enabled, or the reason why not.
        public static bool CanGoLive(out string reason)
        {
            reason = null;
            var gm = GameManager.I;
            if (gm == null || !gm.InGame) { reason = "–"; return false; }
            if (LiveState.IsLive) { reason = Loc.Tr("Du bist schon live", "You are already live"); return false; }
            if (!Unlocked) { reason = Loc.Tr("Ab 1.000 Followern", "From 1,000 followers"); return false; }
            if (gm.State.lastStreamDay == gm.State.day) { reason = Loc.Tr("Heute schon gestreamt", "Already streamed today"); return false; }
            if (StartPlace() == null)
            {
                reason = Followers >= StreamRules.DockFollowers
                    ? Loc.Tr("Nur auf dem Boot oder am Steg", "Only on the boat or the dock")
                    : Loc.Tr("Nur auf dem Boot", "Only on the boat");
                return false;
            }
            return true;
        }

        /// „Live gehen“. Host/offline starts at once; a client asks the host (CoreCommands.StreamStartKey).
        public static bool RequestStart()
        {
            if (!CanGoLive(out string reason))
            {
                GameEvents.RaiseNotify(NotifyKind.Warning, reason, "KRILL.tv");
                return false;
            }
            string place = StartPlace() ?? "boat";
            if (Net.IsClient)
            {
                Net.Command(NetCommand.Custom, CoreCommands.StreamStartKey, place);
                return true;
            }
            var svc = StreamService.Instance;
            return svc != null && svc.HostStart(Net.LocalId, place, false);
        }

        /// „Stream beenden“.
        public static void RequestEnd()
        {
            if (!LiveState.IsLive) return;
            if (Net.IsClient) { Net.Command(NetCommand.Custom, CoreCommands.StreamEndKey, "user"); return; }
            var svc = StreamService.Instance;
            if (svc != null) svc.HostEnd(false, false);
        }

        /// [T] accept the offered dare (only the named player can).
        public static bool AcceptDare()
        {
            var d = LiveState.Dare;
            if (d == null || d.state != DareState.Offered || !LocalIsDareTarget) return false;
            var svc = StreamService.Instance;
            if (svc == null) return false;
            if (Net.IsClient)
            {
                Net.Command(NetCommand.Custom, CoreCommands.DareKey, "accept", DareSerial.ToString(System.Globalization.CultureInfo.InvariantCulture));
                svc.PredictAccept();
                return true;
            }
            return svc.HostAcceptDare(Net.LocalId, DareSerial);
        }

        /// Money a full 10-minute stream at average hype brings right now (app hint).
        public static long ExpectedMoney() => (long)StreamRules.ExpectedStreamMoney(Followers);

        // ---------------------------------------------------------------- test hooks (AutoPlaytest – core schedules them)
        /// Starts a stream ignoring place and the once-per-day rule (followers must be ≥ 1,000 unless force).
        public static bool DebugStart(bool force = true)
        {
            var svc = StreamService.Instance;
            return svc != null && Net.HasAuthority && svc.HostStart(Net.LocalId, "boat", force);
        }

        /// Offers a dare by id to the local player right now (bypasses availability).
        public static bool DebugOfferDare(string id)
        {
            var svc = StreamService.Instance;
            return svc != null && Net.HasAuthority && svc.HostOfferDare(StreamDares.IndexOf(id), Net.LocalId, true);
        }

        /// Accepts and completes the current dare as if the target had done it (scripted success).
        public static bool DebugCompleteDare()
        {
            var svc = StreamService.Instance;
            if (svc == null || !Net.HasAuthority || LiveState.Dare == null) return false;
            if (LiveState.Dare.state == DareState.Offered) svc.HostAcceptDare(LiveState.Dare.targetPlayerId, DareSerial);
            return svc.HostDareResult(LiveState.Dare.targetPlayerId, DareSerial, true);
        }

        /// Ends the stream as if the streamer fainted (loss applied).
        public static void DebugFaint()
        {
            var svc = StreamService.Instance;
            if (svc != null && Net.HasAuthority) svc.HostEnd(true, false);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            DareSerial = 0; DareIndex = -1; DareUser = 0; LocalIsDareTarget = false; DareProgress01 = -1f; StreamSerial = 0;
            LastSummary = null; BuiltInUI = true; Started = null; Ended = null; MoneyTicked = null;
        }
    }
}
