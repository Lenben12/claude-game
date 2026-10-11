using System;
using UnityEngine;

namespace Sealebrities
{
    /// One live-chat bubble (C.6). Generated on the host, sent as message id + args by the Net module (each machine
    /// localizes); `text` is already localized when it reaches LiveState.RaiseChat.
    public struct ChatMessage
    {
        public string user;
        public string text;
        public ChatMood mood;
        /// > 0 for a donation bubble (gold, coin sound).
        public long donation;
    }

    /// A livestream dare (C.5). targetPlayerId = the player who has to do it (co-op rotates).
    public sealed class DareInfo
    {
        public string id;
        public string text;
        public int targetPlayerId;
        public float secondsLeft;
        public long reward;
        public DareState state;
    }

    /// Runtime state of the KRILL.tv livestream (C). Simulated by the livestream module on the host (offline: locally),
    /// mirrored on clients by the Net module (StreamState 90). Persistent results live in GameState (lastStreamDay,
    /// streams). Values are plain fields so the overlay can read them every frame without allocations.
    public static class LiveState
    {
        /// Unlock + rules (C.1): from 1,000 followers on the boat; from 2,500 also from the dock. One stream per day,
        /// max 10 real minutes. Signal range 150 m around the boat (400 m from 2,500 followers).
        public const long UnlockFollowers = 1000;
        public const long DockStreamFollowers = 2500;
        public const float MaxStreamSeconds = 600f;
        public const float SignalRange = 150f, SignalRangeRelay = 400f;

        public static bool IsLive;
        /// Net id of the player who started the stream.
        public static int StreamerId;
        public static int Viewers;
        public static int PeakViewers;
        /// 0.3 .. 3.0 (C.2).
        public static float Hype = 1f;
        public static float Seconds, MaxSeconds = MaxStreamSeconds;
        public static long MoneyThisStream, PendingFollowers;
        /// Current dare (null = none).
        public static DareInfo Dare;
        /// "Kein Signal!" – viewers drain while true.
        public static bool NoSignal;

        public static event Action<ChatMessage> Chat;
        public static event Action Changed;

        public static void RaiseChat(ChatMessage m) => Chat?.Invoke(m);
        public static void RaiseChanged() => Changed?.Invoke();

        /// Resets the runtime fields (stream ended / back to the menu).
        public static void Reset()
        {
            IsLive = false; StreamerId = 0; Viewers = 0; PeakViewers = 0; Hype = 1f; Seconds = 0f; MaxSeconds = MaxStreamSeconds;
            MoneyThisStream = 0; PendingFollowers = 0; Dare = null; NoSignal = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Reset(); Chat = null; Changed = null; }
    }
}
