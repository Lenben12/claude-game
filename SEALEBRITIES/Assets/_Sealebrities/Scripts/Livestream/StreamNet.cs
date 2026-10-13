using UnityEngine;

namespace Sealebrities
{
    /// Decoded StreamState (host → all).
    public struct StreamWireState
    {
        public bool live, noSignal;
        public int streamer, viewers, peak, streamSerial;
        public float hype, seconds, maxSeconds;
        public long money, pending;
        public int dareSerial, dareIndex, dareState, dareTarget, dareUser;
        public float dareSecondsLeft;
        public long dareReward;
    }

    /// Decoded StreamFeed (client → host): what that player's camera sees (C.8 "every player camera counts").
    public struct StreamWireFeed
    {
        public bool inWater, underwater, cameraInHand, signalOk, onBoatOrDock, danger, gerald, bestNew, selfie, recording, fainting;
        public int best, tier, count, biggest, biggestTier, dangerSpecies, legendary, bumps, mask;
        public float cm, biggestCm, legendaryCm, depth, air, maxSafe;
    }

    /// Livestream replication (Design_Runde2 J.3 net ids): 90 StreamState (host → all, 2 Hz reliable + summary),
    /// 91 StreamFeed (client → host, 2 Hz unreliable), 92 StreamChat (host → all, reliable: line id + arguments, each
    /// machine localizes). Owned by the livestream module – the Net module must not register these ids.
    public static class StreamNet
    {
        public const byte MsgState = 90, MsgFeed = 91, MsgChat = 92;
        const byte KindState = 1, KindSummary = 2;
        const short NoSpecies = -1;

        static readonly SyncWriter W = new SyncWriter(96);
        static bool installed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { installed = false; }

        public static void Install()
        {
            if (installed) return;
            installed = true;
            NetBus.Register(MsgState, OnState);
            NetBus.Register(MsgFeed, OnFeed);
            NetBus.Register(MsgChat, OnChat);
        }

        static bool OnlineWithGuests => Net.IsHost && Net.Players.Count > 1;

        static short Sp(SpeciesDef s) { int i = StreamEye.IndexOf(s); return i < 0 ? NoSpecies : (short)i; }
        static uint U(long v) => unchecked((uint)(int)(v > int.MaxValue ? int.MaxValue : v < int.MinValue ? int.MinValue : v));
        static long L(uint v) => unchecked((int)v);

        // ================================================================ host → all
        public static void SendState(in StreamWireState s)
        {
            if (!OnlineWithGuests) return;
            W.Reset();
            W.Byte(KindState);
            W.Bool(s.live);
            W.Byte((byte)Mathf.Clamp(s.streamer, 0, 255));
            W.UInt(U(s.viewers));
            W.UInt(U(s.peak));
            W.UShort((ushort)Mathf.Clamp(Mathf.RoundToInt(s.hype * 1000f), 0, 65535));
            W.Float(s.seconds);
            W.UShort((ushort)Mathf.Clamp(Mathf.RoundToInt(s.maxSeconds), 0, 65535));
            W.UInt(U(s.money));
            W.UInt(U(s.pending));
            W.Bool(s.noSignal);
            W.UShort((ushort)(s.streamSerial & 0xFFFF));
            W.UShort((ushort)(s.dareSerial & 0xFFFF));
            W.SByte((sbyte)Mathf.Clamp(s.dareIndex, -1, 127));
            W.Byte((byte)s.dareState);
            W.Byte((byte)Mathf.Clamp(s.dareTarget, 0, 255));
            W.Byte((byte)Mathf.Clamp(s.dareUser, 0, 255));
            W.Float(s.dareSecondsLeft);
            W.UInt(U(s.dareReward));
            NetBus.Send(MsgState, W.ToArray(), NetBus.Everyone, true);
        }

        public static void SendSummary(StreamSummary s)
        {
            if (!OnlineWithGuests || s == null) return;
            W.Reset();
            W.Byte(KindSummary);
            W.UShort((ushort)(s.serial & 0xFFFF));
            W.Bool(s.fainted);
            W.Bool(s.drone);
            W.Float(s.seconds);
            W.UInt(U(s.peakViewers));
            W.UInt(U(s.money));
            W.UInt(U(s.followersDelta));
            W.Byte((byte)s.best);
            W.Short(Sp(s.bestSpecies));
            W.Float(s.bestCm);
            W.SByte((sbyte)Mathf.Clamp(s.bestDare, -1, 127));
            NetBus.Send(MsgState, W.ToArray(), NetBus.Everyone, true);
        }

        public static void SendChat(in ChatEmit e)
        {
            if (!OnlineWithGuests) return;
            W.Reset();
            W.UShort((ushort)Mathf.Clamp(e.line, 0, 65535));
            W.Byte((byte)Mathf.Clamp(e.user, 0, 255));
            W.UInt(U(e.donation));
            W.Short((short)Mathf.Clamp(e.species, -1, 32767));
            W.Short((short)Mathf.Clamp(e.player, -1, 32767));
            NetBus.Send(MsgChat, W.ToArray(), NetBus.Everyone, true);
        }

        // ================================================================ client → host
        public static void SendFeed(StreamEye eye, int bumps)
        {
            if (!Net.IsClient || eye == null) return;
            int flags = 0;
            if (eye.InWater) flags |= 1;
            if (eye.Underwater) flags |= 2;
            if (eye.CameraInHand) flags |= 4;
            if (eye.SignalOk) flags |= 8;
            if (eye.OnBoat || eye.OnDock) flags |= 16;
            if (eye.DangerClose) flags |= 32;
            if (eye.Gerald) flags |= 64;
            if (eye.BestIsNew) flags |= 128;
            if (SelfieState.Active) flags |= 256;
            if (eye.Recording) flags |= 512;
            if (eye.Fainting) flags |= 1024;
            W.Reset();
            W.UShort((ushort)flags);
            W.Short(Sp(eye.Best));
            W.Byte((byte)eye.BestTier);
            W.Float(eye.BestCm);
            W.Byte((byte)Mathf.Clamp(eye.Count, 0, 255));
            W.Short(Sp(eye.Biggest));
            W.Byte((byte)eye.BiggestTier);
            W.Float(eye.BiggestCm);
            W.Short(Sp(eye.DangerSpecies));
            W.Short(Sp(eye.LegendarySpecies));
            W.Float(eye.LegendaryCm);
            W.UShort((ushort)Mathf.Clamp(Mathf.RoundToInt(eye.Depth * 10f), 0, 65535));
            W.Byte((byte)Mathf.Clamp(Mathf.RoundToInt(eye.Air01 * 255f), 0, 255));
            W.Byte((byte)Mathf.Clamp(Mathf.RoundToInt(eye.MaxSafeDepth), 0, 255));
            W.Byte((byte)(bumps & 0xFF));
            W.UShort((ushort)(eye.NearMask & 0xFFFF));
            NetBus.Send(MsgFeed, W.ToArray(), NetBus.Everyone, false);
        }

        // ================================================================ receive
        static void OnState(int from, byte[] payload)
        {
            if (!Net.IsClient) return;
            var svc = StreamService.Instance;
            if (svc == null) return;
            var r = new SyncReader(payload);
            byte kind = r.Byte();
            if (kind == KindState)
            {
                var s = new StreamWireState
                {
                    live = r.Bool(),
                    streamer = r.Byte(),
                    viewers = (int)L(r.UInt()),
                    peak = (int)L(r.UInt()),
                    hype = r.UShort() / 1000f,
                    seconds = r.Float(),
                    maxSeconds = r.UShort(),
                    money = L(r.UInt()),
                    pending = L(r.UInt()),
                    noSignal = r.Bool(),
                    streamSerial = r.UShort(),
                    dareSerial = r.UShort(),
                    dareIndex = r.SByte(),
                    dareState = r.Byte(),
                    dareTarget = r.Byte(),
                    dareUser = r.Byte(),
                    dareSecondsLeft = r.Float(),
                    dareReward = L(r.UInt()),
                };
                if (!r.Failed) svc.OnRemoteState(in s);
            }
            else if (kind == KindSummary)
            {
                var sum = new StreamSummary
                {
                    serial = r.UShort(),
                    fainted = r.Bool(),
                    drone = r.Bool(),
                    seconds = r.Float(),
                    peakViewers = (int)L(r.UInt()),
                    money = L(r.UInt()),
                    followersDelta = L(r.UInt()),
                    best = (BestMoment)r.Byte(),
                    bestSpecies = StreamEye.SpeciesAt(r.Short()),
                    bestCm = r.Float(),
                    bestDare = r.SByte(),
                };
                if (!r.Failed) svc.OnRemoteSummary(sum);
            }
        }

        static void OnFeed(int from, byte[] payload)
        {
            if (!Net.IsHost) return;
            var svc = StreamService.Instance;
            if (svc == null) return;
            var r = new SyncReader(payload);
            int flags = r.UShort();
            var f = new StreamWireFeed
            {
                inWater = (flags & 1) != 0,
                underwater = (flags & 2) != 0,
                cameraInHand = (flags & 4) != 0,
                signalOk = (flags & 8) != 0,
                onBoatOrDock = (flags & 16) != 0,
                danger = (flags & 32) != 0,
                gerald = (flags & 64) != 0,
                bestNew = (flags & 128) != 0,
                selfie = (flags & 256) != 0,
                recording = (flags & 512) != 0,
                fainting = (flags & 1024) != 0,
                best = r.Short(),
                tier = r.Byte(),
                cm = r.Float(),
                count = r.Byte(),
                biggest = r.Short(),
                biggestTier = r.Byte(),
                biggestCm = r.Float(),
                dangerSpecies = r.Short(),
                legendary = r.Short(),
                legendaryCm = r.Float(),
                depth = r.UShort() / 10f,
                air = r.Byte() / 255f,
                maxSafe = r.Byte(),
                bumps = r.Byte(),
                mask = r.UShort(),
            };
            if (!r.Failed) svc.OnRemoteFeed(from, in f);
        }

        static void OnChat(int from, byte[] payload)
        {
            if (!Net.IsClient) return;
            var r = new SyncReader(payload);
            var e = new ChatEmit
            {
                line = r.UShort(),
                user = r.Byte(),
                donation = L(r.UInt()),
                species = r.Short(),
                player = r.Short(),
            };
            if (r.Failed) return;
            var svc = StreamService.Instance;
            if (svc != null) svc.OnRemoteChat(in e);
        }
    }
}
