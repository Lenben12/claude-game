using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Sealebrities
{
    /// KRILL.tv livestream simulation (Design_Runde2 C). Runs on every machine:
    /// * HOST / OFFLINE: the stream itself – viewers from followers × hype, hype toward the best camera's target,
    ///   money (60 % ad ticker paid every 5 s + 40 % donations in 1–8 % chunks), pending followers (credited at a normal
    ///   end), signal around the boat, 10-minute data plan, chat director, dares (offer → [T] accept → target checks →
    ///   reward), faint loss, end summary, stream records. Replicated with StreamNet (90/92).
    /// * EVERY MACHINE: its own camera (StreamEye, 5 Hz) – the host merges all cameras ("multi-cam"); clients send it as
    ///   StreamFeed (91). Local faint / drone pull-up ends the stream; the local player's dare is checked here.
    [DefaultExecutionOrder(40)]
    public sealed class StreamService : MonoBehaviour
    {
        public static StreamService Instance { get; private set; }

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        readonly StreamEye eye = new StreamEye();
        readonly DareChecker checker = new DareChecker();
        readonly ChatDirector chat = new ChatDirector();
        public StreamEye Eye => eye;

        /// One camera feeding the stream (the host's own or a client's StreamFeed) + its chat-reaction memory.
        sealed class Cam
        {
            public int id;
            public float time;
            public StreamWireFeed f;
            public int lastBumps = -1, lastBest = -1;
            public float dangerCd, airCd, cameraCd, selfieCd, camGoneSince = -1f;
            public bool wasDanger, wasLowAir, wasSelfie;

            public void ResetMemory()
            {
                lastBumps = -1; lastBest = -1; dangerCd = 0f; airCd = 0f; cameraCd = 0f; selfieCd = 0f; camGoneSince = -1f;
                wasDanger = false; wasLowAir = false; wasSelfie = false;
            }
        }

        readonly Cam localCam = new Cam();
        readonly List<Cam> remote = new List<Cam>(4);
        const float FeedFreshSeconds = 2.5f;

        // ---------------------------------------------------------------- host simulation state
        bool hostLive;
        float lastAnimalTime, noSignalSeconds, signalFactor = 1f, payoutTimer, adAcc, donationPool, nextDonation, pendingAcc, noiseSeed;
        long unpaid;
        /// Dare money paid out this stream (StreamRules.DareMoneyCap).
        long dareMoneyPaid;
        float stateTimer, changedTimer, nextDareAt, dareResultTimer, geraldCd;
        int daresOffered, dareTargetCursor;
        bool forceState;
        int usedDares;
        ulong legendaryA, legendaryB, newA, newB, bigA, bigB;
        BestMoment bestKind;
        SpeciesDef bestSpecies;
        float bestCm, bestScore;
        int bestDare = -1;
        SpeciesDef biggestSeen;
        float biggestSeenCm;
        System.Random rng = new System.Random(Environment.TickCount);

        // ---------------------------------------------------------------- local / client state
        float eyeTimer, feedTimer;
        int localBumps;
        int faintReportedSerial = -1;
        bool faintLatch, droneLatch;
        int checkerSerial = -1;
        string pokeWritten;   // „E – Anstupsen“ written by a running poke dare (cleared with the ReferenceEquals pattern)
        long lastMoneySeen;
        int predictedSerial = -1;
        float predictUntil;

        static bool Alive(object o) => o != null && (!(o is UnityEngine.Object uo) || uo != null);

        // ================================================================ lifecycle
        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            noiseSeed = UnityEngine.Random.Range(0f, 100f);
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void OnEnable()
        {
            GameEvents.PlayerBumped += OnLocalBump;
            GameEvents.PlayerFainted += OnLocalFainted;
            GameEvents.ReturnedToMenu += ResetAll;
            GameEvents.GameStarted += ResetAll;
            GameEvents.SpeciesDiscovered += OnDiscovered;
            GameEvents.SizeRecord += OnRecord;
            GameEvents.SleepStarted += OnSleep;
            GameEvents.PlayerUnconscious += OnUnconscious;
            Net.PlayerLeft += OnPlayerLeft;
            Net.SessionEnded += OnSessionEnded;
        }

        void OnDisable()
        {
            GameEvents.PlayerBumped -= OnLocalBump;
            GameEvents.PlayerFainted -= OnLocalFainted;
            GameEvents.ReturnedToMenu -= ResetAll;
            GameEvents.GameStarted -= ResetAll;
            GameEvents.SpeciesDiscovered -= OnDiscovered;
            GameEvents.SizeRecord -= OnRecord;
            GameEvents.SleepStarted -= OnSleep;
            GameEvents.PlayerUnconscious -= OnUnconscious;
            Net.PlayerLeft -= OnPlayerLeft;
            Net.SessionEnded -= OnSessionEnded;
        }

        /// Back to a clean slate (menu, new game, session end). LiveState itself is reset by GameManager.
        void ResetAll()
        {
            hostLive = false;
            checker.Stop();
            checkerSerial = -1;
            remote.Clear();
            localCam.ResetMemory();
            faintLatch = false;
            droneLatch = false;
            predictedSerial = -1;
            KrillTv.DareIndex = -1;
            KrillTv.LocalIsDareTarget = false;
            KrillTv.DareProgress01 = -1f;
            if (LiveState.IsLive) LiveState.Reset();
        }

        void OnSessionEnded(string reason)
        {
            if (hostLive) return;   // the host keeps streaming offline
            ResetAll();
            LiveState.Reset();
            LiveState.RaiseChanged();
        }

        // ================================================================ per frame
        void Update()
        {
            var gm = GameManager.I;
            if (gm == null || !gm.InGame)
            {
                if (hostLive || LiveState.IsLive) ResetAll();
                return;
            }
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.25f);
            float now = Time.unscaledTime;

            eyeTimer -= dt;
            if ((LiveState.IsLive || SponsorService.NeedsEye) && eyeTimer <= 0f)
            {
                eyeTimer = 0.2f;
                eye.Scan();
            }

            DetectLocalFaint();
            UpdateLocalDare(dt);

            if (Net.HasAuthority)
            {
                bool paused = !Net.IsOnline && UIState.PauseOpen;   // offline the pause menu stops the stream too
                if (hostLive && !paused) HostTick(dt, now);
            }
            else ClientTick(dt);
        }

        // ================================================================ local: faint, drone, dares
        void OnLocalBump(float airLost) { localBumps = (localBumps + 1) & 0xFF; }

        void OnLocalFainted() => ReportFaint(false);

        void DetectLocalFaint()
        {
            if (!LiveState.IsLive) { faintLatch = false; droneLatch = false; return; }
            var p = Refs.Player;
            if (!Alive(p)) return;
            if (p.IsFainting)
            {
                if (!faintLatch) { faintLatch = true; ReportFaint(false); }
            }
            else faintLatch = false;
            // Relaxed: the rescue drone pulls the diver up at 8 % air – the stream ends the same way (loss 1 %, C.4)
            var gm = GameManager.I;
            bool drone = gm != null && !gm.Difficulty.canFaint && p.IsUnderwater && p.Air01 <= 0.08f;
            if (drone)
            {
                if (!droneLatch) { droneLatch = true; ReportFaint(true); }
            }
            else if (!p.IsUnderwater) droneLatch = false;
        }

        void ReportFaint(bool drone)
        {
            if (!LiveState.IsLive || faintReportedSerial == KrillTv.StreamSerial) return;
            faintReportedSerial = KrillTv.StreamSerial;
            if (Net.HasAuthority) HostEnd(true, drone);
            else Net.Command(NetCommand.Custom, CoreCommands.StreamEndKey, drone ? "drone" : "faint");
        }

        void UpdateLocalDare(float dt)
        {
            checker.TickAftermath();
            var d = LiveState.Dare;
            bool mine = d != null && d.targetPlayerId == Net.LocalId;
            KrillTv.LocalIsDareTarget = mine && (d.state == DareState.Offered || d.state == DareState.Running);
            if (mine && d.state == DareState.Offered && !UIState.BlocksGameplay && GameInput.StreamDarePressed) KrillTv.AcceptDare();

            if (mine && d.state == DareState.Running)
            {
                if (checkerSerial != KrillTv.DareSerial)
                {
                    checkerSerial = KrillTv.DareSerial;
                    checker.Begin(KrillTv.DareSerial, StreamDares.At(KrillTv.DareIndex));
                }
                var res = checker.Tick(dt, eye);
                KrillTv.DareProgress01 = checker.Progress01;
                if (res != DareChecker.Result.None) { ReleasePokePrompt(); ReportDare(res == DareChecker.Result.Done); }
                else UpdatePokePrompt(StreamDares.At(KrillTv.DareIndex));
            }
            else
            {
                ReleasePokePrompt();
                if (checker.Active) checker.Stop();
                if (d == null) checkerSerial = -1;
                KrillTv.DareProgress01 = mine && d.state == DareState.Done ? 1f : -1f;
            }
        }

        /// Poke dares: „E – Anstupsen“ while the starfish / shark is in reach (5 Hz eye distances, no per-frame search).
        void UpdatePokePrompt(DareDef def)
        {
            bool near = false;
            if (StreamDares.IsPoke(def) && eye != null && eye.Valid && !UIState.BlocksGameplay)
                near = def.id == "dare_shark" ? eye.DistShark <= 1.6f : eye.DistStarfish <= 1.2f;
            if (!near) { ReleasePokePrompt(); return; }
            string text = PropPrompt.Interact("Anstupsen", "Poke");
            string cur = InteractionPrompt.Text;
            if (cur == null || (pokeWritten != null && ReferenceEquals(cur, pokeWritten)))
            {
                InteractionPrompt.Text = text;
                pokeWritten = text;
            }
        }

        void ReleasePokePrompt()
        {
            if (pokeWritten != null && ReferenceEquals(InteractionPrompt.Text, pokeWritten)) InteractionPrompt.Text = null;
            pokeWritten = null;
        }

        void ReportDare(bool ok)
        {
            if (Net.HasAuthority) HostDareResult(Net.LocalId, KrillTv.DareSerial, ok);
            else Net.Command(NetCommand.Custom, CoreCommands.DareKey, ok ? "done" : "fail", KrillTv.DareSerial.ToString(Inv));
        }

        /// Client: show "running" at once after [T]; the host's next state confirms it.
        public void PredictAccept()
        {
            var d = LiveState.Dare;
            var def = StreamDares.At(KrillTv.DareIndex);
            if (d == null || def == null || d.state != DareState.Offered) return;
            d.state = DareState.Running;
            d.secondsLeft = def.seconds;
            predictedSerial = KrillTv.DareSerial;
            predictUntil = Time.unscaledTime + 1.5f;
            GameEvents.RaiseDareChanged(d);
            StreamSfx.DareAccepted();
        }

        // ================================================================ host: start / end
        /// Starts a stream (host/offline; clients come in through CoreCommands.StreamStartKey). force = tests only.
        public bool HostStart(int fromId, string place, bool force)
        {
            var gm = GameManager.I;
            if (gm == null || !gm.InGame || !Net.HasAuthority || LiveState.IsLive || hostLive) return false;
            var s = gm.State;
            if (!force)
            {
                if (s.followers < StreamRules.UnlockFollowers || s.lastStreamDay == s.day) return false;
                if (place == "dock" && s.followers < StreamRules.DockFollowers) return false;
            }
            s.lastStreamDay = s.day;
            float now = Time.unscaledTime;

            hostLive = true;
            LiveState.Reset();
            LiveState.IsLive = true;
            LiveState.StreamerId = fromId;
            LiveState.Hype = StreamRules.HypeStart;
            LiveState.MaxSeconds = StreamRules.MaxSeconds;
            KrillTv.StreamSerial = (KrillTv.StreamSerial + 1) & 0xFFFF;
            KrillTv.DareIndex = -1;
            KrillTv.LastSummary = null;

            lastAnimalTime = now;
            noSignalSeconds = 0f;
            signalFactor = 1f;
            payoutTimer = 0f;
            adAcc = 0f;
            unpaid = 0;
            donationPool = 0f;
            nextDonation = NextDonationChunk(s.followers);
            pendingAcc = 0f;
            noiseSeed = (float)rng.NextDouble() * 100f;
            daresOffered = 0;
            dareMoneyPaid = 0;
            usedDares = 0;
            dareTargetCursor = 0;
            nextDareAt = now + Range(StreamRules.DareFirstMin, StreamRules.DareFirstMax);
            dareResultTimer = 0f;
            geraldCd = 0f;
            legendaryA = legendaryB = newA = newB = bigA = bigB = 0UL;
            bestKind = BestMoment.None;
            bestSpecies = null;
            bestCm = 0f;
            bestScore = 0f;
            bestDare = -1;
            biggestSeen = null;
            biggestSeenCm = 0f;
            localCam.ResetMemory();
            for (int i = 0; i < remote.Count; i++) remote[i].ResetMemory();

            chat.Reset(rng.Next());
            chat.Push(ChatCat.Hello, -1, 2, now, true);
            chat.Push(ChatCat.Hello, -1, 2, now, false);
            LiveState.Viewers = Mathf.RoundToInt(StreamRules.BaseViewers(s.followers) * LiveState.Hype * 0.4f);

            GameEvents.RaiseStreamStarted();
            KrillTv.RaiseStarted();
            StreamSfx.GoLive();
            forceState = true;
            BroadcastState();
            LiveState.RaiseChanged();
            return true;
        }

        /// Ends the stream for everybody. fainted: pending followers are lost and the C.4 loss applies (drone = the
        /// Relaxed pull-up, same rule with its 1 % share). Money already paid is kept.
        public void HostEnd(bool fainted, bool drone)
        {
            var gm = GameManager.I;
            if (!hostLive || gm == null || !Net.HasAuthority) return;
            var s = gm.State;
            float now = Time.unscaledTime;

            long rest = (long)adAcc;
            if (rest > 0) Pay(rest);
            adAcc = 0f;
            FlushPay();
            long money = LiveState.MoneyThisStream;
            long followers;
            if (fainted)
            {
                long loss = Math.Min(Progression.StreamFaintLoss(s.followers, s.difficulty), s.followers);
                followers = -loss;
                // the "F" wall – sent while the stream is still live so every machine shows it
                var ctx = BuildContext(now);
                for (int i = 0; i < 3; i++) if (chat.Say(ChatCat.Lost, -1, in ctx, now, out var e)) EmitChat(in e);
                if (loss > 0) gm.AddFollowers(-loss);
                Achievements.Unlock("live_fail");
            }
            else
            {
                followers = Math.Max(0L, LiveState.PendingFollowers);
                if (followers > 0) gm.AddFollowers(followers);
            }

            var sum = new StreamSummary
            {
                serial = KrillTv.StreamSerial,
                fainted = fainted,
                drone = drone,
                seconds = LiveState.Seconds,
                peakViewers = LiveState.PeakViewers,
                money = money,
                followersDelta = followers,
                best = bestKind,
                bestSpecies = bestSpecies,
                bestCm = bestCm,
                bestDare = bestDare,
            };
            if (sum.best == BestMoment.None && biggestSeen != null)
            {
                sum.best = BestMoment.Biggest;
                sum.bestSpecies = biggestSeen;
                sum.bestCm = biggestSeenCm;
            }
            s.streams.Add(new StreamRecord
            {
                day = s.day, seconds = sum.seconds, peakViewers = sum.peakViewers, money = money, followers = followers, fainted = fainted
            });
            while (s.streams.Count > 30) s.streams.RemoveAt(0);

            bool hadDare = LiveState.Dare != null;
            hostLive = false;
            LiveState.IsLive = false;
            LiveState.NoSignal = false;
            LiveState.Dare = null;
            KrillTv.DareIndex = -1;
            KrillTv.LastSummary = sum;
            checker.Stop();
            forceState = true;
            BroadcastState();
            StreamNet.SendSummary(sum);

            if (hadDare) GameEvents.RaiseDareChanged(null);
            if (fainted) StreamSfx.StreamLost(); else StreamSfx.StreamEnded();
            GameEvents.RaiseStreamEnded(fainted, money, followers);
            KrillTv.RaiseEnded(sum);
            LiveState.RaiseChanged();
            SponsorService.OnStreamEnded(fainted);
            gm.Save();
        }

        // ================================================================ host: the simulation
        void HostTick(float dt, float now)
        {
            var gm = GameManager.I;
            var s = gm.State;
            long F = s.followers;

            LiveState.Seconds += dt;
            geraldCd -= dt;
            FillLocalCam(now);

            // ---- merge every camera (C.8 multi-cam)
            bool anyCam = false, anySignal = false, danger = false, lowAir = false, deep = false, night = false, selfie = false;
            int camsWithAnimal = 0;
            float bestTarget = 0f;
            int bestSp = -1;
            MergeCam(localCam, now, dt, ref anyCam, ref anySignal, ref danger, ref lowAir, ref deep, ref selfie, ref camsWithAnimal, ref bestTarget, ref bestSp);
            for (int i = 0; i < remote.Count; i++)
                MergeCam(remote[i], now, dt, ref anyCam, ref anySignal, ref danger, ref lowAir, ref deep, ref selfie, ref camsWithAnimal, ref bestTarget, ref bestSp);
            night = EnvState.Night01 > 0.5f;

            // ---- hype (C.2)
            float target;
            if (camsWithAnimal > 0)
            {
                lastAnimalTime = now;
                target = bestTarget + Mathf.Min(StreamRules.MultiCamCap, StreamRules.MultiCamBonus * (camsWithAnimal - 1));
            }
            else if (!anyCam) target = StreamRules.HypeBoring;
            else target = now - lastAnimalTime > StreamRules.BoredAfterSeconds ? StreamRules.HypeBoring : StreamRules.HypeAnimal;
            if (danger) target *= StreamRules.DangerMultiplier;
            var dare = LiveState.Dare;
            if (dare != null && dare.state == DareState.Running) target *= StreamRules.DareMultiplier;
            target = Mathf.Clamp(target, StreamRules.HypeMin, StreamRules.HypeMax);
            float k = 1f - Mathf.Exp(-dt / StreamRules.HypeTau);
            LiveState.Hype = Mathf.Clamp(LiveState.Hype + (target - LiveState.Hype) * k, StreamRules.HypeMin, StreamRules.HypeMax);

            // ---- signal (C.1)
            LiveState.NoSignal = !anySignal;
            if (!anySignal)
            {
                noSignalSeconds += dt;
                signalFactor = Mathf.Max(0.02f, signalFactor - signalFactor * StreamRules.NoSignalDrainPerSecond * dt);
            }
            else
            {
                noSignalSeconds = 0f;
                signalFactor = Mathf.Min(1f, signalFactor + StreamRules.SignalRecoverPerSecond * dt);
            }

            // ---- viewers
            float viewers = StreamRules.BaseViewers(F) * LiveState.Hype * StreamRules.ViewerNoise(LiveState.Seconds, noiseSeed) * signalFactor;
            LiveState.Viewers = Mathf.Max(0, Mathf.RoundToInt(viewers));
            if (LiveState.Viewers > LiveState.PeakViewers) LiveState.PeakViewers = LiveState.Viewers;

            // ---- money + followers (C.3)
            float mps = StreamRules.MoneyPerSecond(F, LiveState.Hype) * signalFactor;
            adAcc += mps * StreamRules.AdShare * dt;
            donationPool += mps * (1f - StreamRules.AdShare) * dt;
            if (donationPool >= nextDonation)
            {
                long amount = Math.Max(1L, (long)Mathf.Round(nextDonation));
                donationPool = Mathf.Max(0f, donationPool - amount);
                nextDonation = NextDonationChunk(F);
                Pay(amount);
                EmitChat(chat.Donation(amount, now));
                StreamSfx.Coin();
            }
            payoutTimer += dt;
            if (payoutTimer >= StreamRules.PayoutSeconds)
            {
                // host AddMoney once per 5 s, batched (ads + the donations of that window)
                payoutTimer = 0f;
                long pay = (long)adAcc;
                if (pay > 0) { adAcc -= pay; Pay(pay); }
                FlushPay();
            }
            pendingAcc += StreamRules.FollowersPerSecond(F, LiveState.Hype) * signalFactor * dt;
            LiveState.PendingFollowers = (long)pendingAcc;

            // ---- chat (C.6)
            var ctx = BuildContext(now);
            ctx.anyCamera = anyCam;
            ctx.species = bestSp;
            ctx.bored = anyCam && camsWithAnimal == 0 && now - lastAnimalTime > StreamRules.BoredAfterSeconds;
            ctx.lowAir = lowAir;
            ctx.deep = deep;
            ctx.night = night;
            ctx.selfie = selfie;
            ctx.noSignal = !anySignal;
            if (chat.Tick(dt, now, in ctx, out var line)) EmitChat(in line);

            // ---- dares (C.5)
            HostDareTick(dt, now, F);

            SponsorService.OnStreamTick(dt, LiveState.Viewers);

            // ---- end conditions
            if (LiveState.Seconds >= LiveState.MaxSeconds) { HostEnd(false, false); return; }
            if (noSignalSeconds >= StreamRules.NoSignalEndSeconds) { HostEnd(false, false); return; }

            // ---- replication + UI events
            stateTimer -= dt;
            if (stateTimer <= 0f || forceState) BroadcastState();
            changedTimer -= dt;
            if (changedTimer <= 0f) { changedTimer = 0.25f; LiveState.RaiseChanged(); }
        }

        ChatDirector.Context BuildContext(float now)
        {
            var ctx = new ChatDirector.Context
            {
                hype = LiveState.Hype,
                viewers = LiveState.Viewers,
                anyCamera = true,
                species = -1,
                coop = Net.IsOnline && Net.Players.Count > 1,
                coopPlayer = -1,
                hater = HaterChannel(),
            };
            if (ctx.coop)
            {
                var ps = Net.Players;
                if (ps.Count > 0) ctx.coopPlayer = ps[rng.Next(ps.Count)].id;
            }
            return ctx;
        }

        /// Clickbait history (2 of the last 5 videos) brings haters (GDD 14.40).
        static bool HaterChannel()
        {
            var gm = GameManager.I;
            if (gm == null) return false;
            var v = gm.State.videos;
            int n = 0, clickbait = 0;
            for (int i = v.Count - 1; i >= 0 && n < 5; i--, n++) if (v[i] != null && v[i].titleStyle == (int)TitleStyle.Clickbait) clickbait++;
            return clickbait >= 2;
        }

        void FillLocalCam(float now)
        {
            localCam.id = Net.LocalId;
            localCam.time = now;
            ref var f = ref localCam.f;
            f.inWater = eye.InWater;
            f.underwater = eye.Underwater;
            f.cameraInHand = eye.CameraInHand;
            f.signalOk = eye.SignalOk;
            f.onBoatOrDock = eye.OnBoat || eye.OnDock;
            f.danger = eye.DangerClose;
            f.gerald = eye.Gerald;
            f.bestNew = eye.BestIsNew;
            f.selfie = SelfieState.Active;
            f.recording = eye.Recording;
            f.fainting = eye.Fainting;
            f.best = StreamEye.IndexOf(eye.Best);
            f.tier = (int)eye.BestTier;
            f.cm = eye.BestCm;
            f.count = eye.Count;
            f.biggest = StreamEye.IndexOf(eye.Biggest);
            f.biggestTier = (int)eye.BiggestTier;
            f.biggestCm = eye.BiggestCm;
            f.dangerSpecies = StreamEye.IndexOf(eye.DangerSpecies);
            f.legendary = StreamEye.IndexOf(eye.LegendarySpecies);
            f.legendaryCm = eye.LegendaryCm;
            f.depth = eye.Depth;
            f.air = eye.Air01;
            f.maxSafe = eye.MaxSafeDepth;
            f.bumps = localBumps;
            f.mask = eye.NearMask;
        }

        void MergeCam(Cam c, float now, float dt, ref bool anyCam, ref bool anySignal, ref bool danger, ref bool lowAir, ref bool deep,
                      ref bool selfie, ref int camsWithAnimal, ref float bestTarget, ref int bestSp)
        {
            if (now - c.time > FeedFreshSeconds) return;
            ref var f = ref c.f;
            c.dangerCd -= dt; c.airCd -= dt; c.cameraCd -= dt; c.selfieCd -= dt;
            if (f.signalOk) anySignal = true;
            if (!f.signalOk) return;   // a camera without signal shows nothing
            bool lowNow = f.underwater && f.air < 0.25f;
            if (lowNow) lowAir = true;
            if (f.underwater && f.depth > 30f) deep = true;
            if (f.selfie) selfie = true;

            // bumps (any player hit by a predator: +0.5 spike, laughing chat)
            if (c.lastBumps >= 0 && f.bumps != c.lastBumps)
            {
                AddHype(StreamRules.SpikeBump);
                chat.Push(ChatCat.Bump, f.dangerSpecies >= 0 ? f.dangerSpecies : f.best, 4, now, true);
            }
            c.lastBumps = f.bumps;

            if (lowNow && !c.wasLowAir && c.airCd <= 0f) { chat.Push(ChatCat.LowAir, -1, 3, now, true); c.airCd = 20f; }
            c.wasLowAir = lowNow;

            if (!f.cameraInHand)
            {
                if (c.camGoneSince < 0f) c.camGoneSince = now;
                if (now - c.camGoneSince > 4f && c.cameraCd <= 0f) { chat.Push(ChatCat.NoCamera, -1, 2, now, false); c.cameraCd = 25f; }
                c.wasDanger = false;
                return;
            }
            c.camGoneSince = -1f;
            anyCam = true;

            if (f.selfie && !c.wasSelfie && c.selfieCd <= 0f) { chat.Push(ChatCat.Selfie, -1, 2, now, true); c.selfieCd = 30f; }
            c.wasSelfie = f.selfie;

            if (f.danger)
            {
                danger = true;
                if (!c.wasDanger && c.dangerCd <= 0f) { chat.Push(ChatCat.Danger, f.dangerSpecies, 3, now, true); c.dangerCd = 12f; }
            }
            c.wasDanger = f.danger;

            if (f.count <= 0 || f.best < 0) return;
            camsWithAnimal++;
            var sp = StreamEye.SpeciesAt(f.best);
            var tier = (SizeTier)Mathf.Clamp(f.tier, 0, 4);
            float t = StreamRules.AnimalHype(sp, tier);
            if (t > bestTarget) { bestTarget = t; bestSp = f.best; }

            if (f.best != c.lastBest)
            {
                c.lastBest = f.best;
                if (sp != null && sp.id == "green_turtle" && geraldCd <= 0f) { chat.Push(ChatCat.Gerald, f.best, 3, now, true); geraldCd = 45f; }
                else chat.Push(ChatCat.Species, f.best, 1, now, false);
            }
            if (f.bestNew && Mark(ref newA, ref newB, f.best)) chat.Push(ChatCat.New, f.best, 3, now, true);
            if (tier >= SizeTier.Huge && Mark(ref bigA, ref bigB, f.best)) chat.Push(ChatCat.Big, f.best, 3, now, true);
            if (f.legendary >= 0 && Mark(ref legendaryA, ref legendaryB, f.legendary))
            {
                AddHype(StreamRules.SpikeLegendary);
                chat.Push(ChatCat.Legendary, f.legendary, 4, now, true);
                ConsiderBest(BestMoment.Legendary, StreamEye.SpeciesAt(f.legendary), f.legendaryCm);
            }
            if (f.biggest >= 0 && f.biggestCm > biggestSeenCm)
            {
                biggestSeenCm = f.biggestCm;
                biggestSeen = StreamEye.SpeciesAt(f.biggest);
            }
        }

        static bool Mark(ref ulong a, ref ulong b, int slot)
        {
            if (slot < 0) return false;
            if (slot < 64)
            {
                ulong bit = 1UL << slot;
                if ((a & bit) != 0) return false;
                a |= bit;
                return true;
            }
            if (slot >= 128) return false;
            ulong b2 = 1UL << (slot - 64);
            if ((b & b2) != 0) return false;
            b |= b2;
            return true;
        }

        void AddHype(float delta) => LiveState.Hype = Mathf.Clamp(LiveState.Hype + delta, StreamRules.HypeMin, StreamRules.HypeMax);

        float NextDonationChunk(long followers)
        {
            float chunk = StreamRules.ExpectedStreamMoney(followers) * Range(StreamRules.DonationChunkMin, StreamRules.DonationChunkMax);
            return Mathf.Max(1f, chunk);
        }

        float Range(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        /// Queues stream money (paid with the next 5 s batch – FlushPay).
        void Pay(long amount)
        {
            if (amount > 0) unpaid += amount;
        }

        /// Pays the queued money into the shared wallet (host) – the "+x $ live" ticker on every machine.
        void FlushPay()
        {
            long amount = unpaid;
            unpaid = 0;
            if (amount <= 0) return;
            var gm = GameManager.I;
            if (gm == null) return;
            gm.AddMoney(amount);
            LiveState.MoneyThisStream += amount;
            KrillTv.RaiseMoneyTicked(amount);
        }

        void EmitChat(in ChatEmit e)
        {
            var m = StreamChat.Format(in e);
            if (string.IsNullOrEmpty(m.text)) return;
            StreamNet.SendChat(in e);
            LiveState.RaiseChat(m);
            if (m.donation <= 0) StreamSfx.ChatBlip();
        }

        void ConsiderBest(BestMoment kind, SpeciesDef sp, float cm)
        {
            float score = Priority(kind) * 10000f + (sp != null ? sp.baseValue : 0f) + cm * 0.01f;
            if (score <= bestScore) return;
            bestScore = score;
            bestKind = kind;
            bestSpecies = sp;
            bestCm = cm;
        }

        static int Priority(BestMoment k)
        {
            switch (k)
            {
                case BestMoment.Record: return 5;
                case BestMoment.NewSpecies: return 4;
                case BestMoment.Dare: return 3;
                case BestMoment.Legendary: return 2;
                case BestMoment.Biggest: return 1;
                default: return 0;
            }
        }

        void BroadcastState()
        {
            forceState = false;
            stateTimer = 0.5f;
            var d = LiveState.Dare;
            var s = new StreamWireState
            {
                live = LiveState.IsLive,
                streamer = LiveState.StreamerId,
                viewers = LiveState.Viewers,
                peak = LiveState.PeakViewers,
                hype = LiveState.Hype,
                seconds = LiveState.Seconds,
                maxSeconds = LiveState.MaxSeconds,
                money = LiveState.MoneyThisStream,
                pending = LiveState.PendingFollowers,
                noSignal = LiveState.NoSignal,
                streamSerial = KrillTv.StreamSerial,
                dareSerial = KrillTv.DareSerial,
                dareIndex = d != null ? KrillTv.DareIndex : -1,
                dareState = d != null ? (int)d.state : 0,
                dareTarget = d != null ? d.targetPlayerId : 0,
                dareUser = KrillTv.DareUser,
                dareSecondsLeft = d != null ? d.secondsLeft : 0f,
                dareReward = d != null ? d.reward : 0,
            };
            StreamNet.SendState(in s);
        }

        // ================================================================ host: dares
        void HostDareTick(float dt, float now, long followers)
        {
            var d = LiveState.Dare;
            if (d == null)
            {
                if (daresOffered < StreamRules.MaxDares && now >= nextDareAt && LiveState.Hype >= StreamRules.DareMinHype && !LiveState.NoSignal)
                    TryOfferDare(now);
                return;
            }
            switch (d.state)
            {
                case DareState.Offered:
                    d.secondsLeft -= dt;
                    if (d.secondsLeft <= 0f)
                    {
                        d.state = DareState.Expired;
                        d.secondsLeft = 0f;
                        AddHype(StreamRules.SpikeDareExpired);
                        FinishDare(now);
                    }
                    break;
                case DareState.Running:
                    d.secondsLeft -= dt;
                    if (d.secondsLeft <= 0f)
                    {
                        d.state = DareState.Failed;
                        d.secondsLeft = 0f;
                        AddHype(StreamRules.SpikeDareFailed);
                        chat.Push(ChatCat.DareFail, -1, 4, now, true);
                        StreamSfx.DareFailed();
                        FinishDare(now);
                    }
                    break;
                default:
                    dareResultTimer -= dt;
                    if (dareResultTimer <= 0f)
                    {
                        LiveState.Dare = null;
                        KrillTv.DareIndex = -1;
                        forceState = true;
                        GameEvents.RaiseDareChanged(null);
                    }
                    break;
            }
        }

        void FinishDare(float now)
        {
            dareResultTimer = StreamRules.DareResultSeconds;
            nextDareAt = now + Range(StreamRules.DareIntervalMin, StreamRules.DareIntervalMax);
            forceState = true;
            GameEvents.RaiseDareChanged(LiveState.Dare);
        }

        /// Rotates through the crew: the first player whose machine reports a possible dare gets one.
        bool TryOfferDare(float now)
        {
            int n = 1 + remote.Count;
            for (int k = 0; k < n; k++)
            {
                int idx = (dareTargetCursor + k) % n;
                var c = idx == 0 ? localCam : remote[idx - 1];
                if (now - c.time > FeedFreshSeconds || c.f.fainting) continue;
                int mask = c.f.mask;
                if (mask == 0) continue;
                int dare = PickDare(mask);
                if (dare < 0) continue;
                dareTargetCursor = idx + 1;
                return HostOfferDare(dare, c.id, false);
            }
            nextDareAt = now + 15f;
            return false;
        }

        int PickDare(int mask)
        {
            var all = StreamDares.All;
            float total = 0f;
            for (int i = 0; i < all.Length; i++) total += DareWeight(i, mask);
            if (total <= 0f) return -1;
            float r = (float)rng.NextDouble() * total;
            for (int i = 0; i < all.Length; i++)
            {
                float w = DareWeight(i, mask);
                if (w <= 0f) continue;
                if (r < w) return i;
                r -= w;
            }
            return -1;
        }

        /// Early dares lean safe, the third one leans really risky (Q83).
        float DareWeight(int i, int mask)
        {
            var d = StreamDares.All[i];
            if ((mask & d.bit) == 0 || (usedDares & (1 << i)) != 0) return 0f;
            switch (daresOffered)
            {
                case 0: return d.risk == StreamDares.Low ? 3f : d.risk == StreamDares.Medium ? 2f : 1f;
                case 1: return d.risk == StreamDares.Low ? 1f : d.risk == StreamDares.Medium ? 3f : 2f;
                default: return d.risk == StreamDares.Low ? 0.5f : d.risk == StreamDares.Medium ? 2f : 3f;
            }
        }

        /// Offers a dare to a player (index into StreamDares.All).
        public bool HostOfferDare(int index, int targetId, bool force)
        {
            var def = StreamDares.At(index);
            var gm = GameManager.I;
            if (def == null || !hostLive || gm == null) return false;
            if (!force && LiveState.Dare != null) return false;
            float now = Time.unscaledTime;
            daresOffered++;
            usedDares |= 1 << index;
            KrillTv.DareSerial = (KrillTv.DareSerial + 1) & 0xFFFF;
            KrillTv.DareIndex = index;
            KrillTv.DareUser = rng.Next(StreamChat.Users.Length);
            var d = new DareInfo
            {
                id = def.id,
                text = StreamDares.Text(def, targetId),
                targetPlayerId = targetId,
                secondsLeft = StreamRules.DareAcceptSeconds,
                reward = System.Math.Max(5L, System.Math.Min(StreamRules.DareReward(def.k, gm.State.followers),
                                         StreamRules.DareMoneyCap(gm.State.followers) - dareMoneyPaid)),
                state = DareState.Offered,
            };
            LiveState.Dare = d;
            chat.Push(ChatCat.DareOffer, -1, 3, now, true);
            forceState = true;
            StreamSfx.DareOffered();
            GameEvents.RaiseDareChanged(d);
            return true;
        }

        public bool HostAcceptDare(int fromId, int serial)
        {
            var d = LiveState.Dare;
            var def = StreamDares.At(KrillTv.DareIndex);
            if (!hostLive || d == null || def == null || d.state != DareState.Offered) return false;
            if (serial != KrillTv.DareSerial || fromId != d.targetPlayerId) return false;
            d.state = DareState.Running;
            d.secondsLeft = def.seconds;
            forceState = true;
            if (fromId == Net.LocalId) StreamSfx.DareAccepted();
            chat.Push(ChatCat.DareOffer, -1, 2, Time.unscaledTime, true);
            GameEvents.RaiseDareChanged(d);
            return true;
        }

        /// The target's machine reports done / failed.
        public bool HostDareResult(int fromId, int serial, bool ok)
        {
            var d = LiveState.Dare;
            var def = StreamDares.At(KrillTv.DareIndex);
            var gm = GameManager.I;
            if (!hostLive || d == null || def == null || gm == null || d.state != DareState.Running) return false;
            if (serial != KrillTv.DareSerial || fromId != d.targetPlayerId) return false;
            float now = Time.unscaledTime;
            if (ok)
            {
                d.state = DareState.Done;
                Pay(d.reward);
                dareMoneyPaid += d.reward;
                FlushPay();
                AddHype(StreamRules.SpikeDareDone);
                pendingAcc += StreamRules.DareFollowerBonus * pendingAcc;
                LiveState.PendingFollowers = (long)pendingAcc;
                bestDare = KrillTv.DareIndex;
                ConsiderBest(BestMoment.Dare, null, 0f);
                chat.Push(ChatCat.DareDone, -1, 5, now, true);
                chat.Push(ChatCat.DareDone, -1, 4, now, false);
                if (def.id == "dare_shark" || def.id == "dare_starfish") chat.Push(ChatCat.Hater, -1, 1, now, false);
                if (def.id == "dare_shark") Achievements.Unlock("hai_five");
                StreamSfx.DareDone();
            }
            else
            {
                d.state = DareState.Failed;
                AddHype(StreamRules.SpikeDareFailed);
                chat.Push(ChatCat.DareFail, -1, 4, now, true);
                StreamSfx.DareFailed();
            }
            d.secondsLeft = 0f;
            FinishDare(now);
            return true;
        }

        // ================================================================ host: command handlers (CoreCommands custom keys)
        public bool HostEndCommand(int fromId, string reason)
        {
            if (!hostLive) return false;
            switch (reason)
            {
                case "faint": HostEnd(true, false); return true;
                case "drone": HostEnd(true, true); return true;
                default: HostEnd(false, false); return true;
            }
        }

        public bool HostDareCommand(int fromId, string op, string serialText)
        {
            int.TryParse(serialText, NumberStyles.Integer, Inv, out int serial);
            switch (op)
            {
                case "accept": return HostAcceptDare(fromId, serial);
                case "done": return HostDareResult(fromId, serial, true);
                case "fail": return HostDareResult(fromId, serial, false);
            }
            return false;
        }

        // ================================================================ host: game events
        void OnDiscovered(SpeciesDef def, float cm, SizeTier tier)
        {
            if (!hostLive || def == null) return;
            AddHype(StreamRules.SpikeNewSpecies);
            int idx = StreamEye.IndexOf(def);
            Mark(ref newA, ref newB, idx);
            chat.Push(ChatCat.New, idx, 4, Time.unscaledTime, true);
            ConsiderBest(BestMoment.NewSpecies, def, cm);
        }

        void OnRecord(SpeciesDef def, float cm, SizeTier tier)
        {
            if (!hostLive || def == null) return;
            AddHype(StreamRules.SpikeRecord);
            int idx = StreamEye.IndexOf(def);
            chat.Push(ChatCat.Record, idx, 5, Time.unscaledTime, true);
            chat.Push(ChatCat.Big, idx, 3, Time.unscaledTime, false);
            ConsiderBest(BestMoment.Record, def, cm);
        }

        void OnSleep() { if (hostLive) HostEnd(false, false); }

        /// F.1: a diver blacked out (unconscious phase before the faint) – the stream is lost for everybody (C.4).
        void OnUnconscious(int playerId)
        {
            if (hostLive) HostEnd(true, false);
            else if (LiveState.IsLive) ReportFaint(false);
        }

        void OnPlayerLeft(NetPlayerInfo p)
        {
            if (p == null) return;
            for (int i = remote.Count - 1; i >= 0; i--) if (remote[i].id == p.id) remote.RemoveAt(i);
            var d = LiveState.Dare;
            if (!hostLive || d == null || d.targetPlayerId != p.id) return;
            if (d.state == DareState.Offered || d.state == DareState.Running)
            {
                d.state = DareState.Expired;
                d.secondsLeft = 0f;
                FinishDare(Time.unscaledTime);
            }
        }

        public void OnRemoteFeed(int fromId, in StreamWireFeed f)
        {
            if (!Net.HasAuthority) return;
            Cam c = null;
            for (int i = 0; i < remote.Count; i++) if (remote[i].id == fromId) { c = remote[i]; break; }
            if (c == null)
            {
                if (remote.Count >= Net.MaxPlayers) return;
                c = new Cam { id = fromId };
                remote.Add(c);
            }
            c.f = f;
            c.time = Time.unscaledTime;
            if (f.fainting && hostLive && LiveState.IsLive) HostEnd(true, false);
        }

        // ================================================================ client mirror
        void ClientTick(float dt)
        {
            if (!LiveState.IsLive) return;
            feedTimer -= dt;
            if (feedTimer <= 0f)
            {
                feedTimer = 0.5f;
                StreamNet.SendFeed(eye, localBumps);
            }
            // smooth between the 2 Hz states
            LiveState.Seconds = Mathf.Min(LiveState.MaxSeconds, LiveState.Seconds + dt);
            var d = LiveState.Dare;
            if (d != null && (d.state == DareState.Offered || d.state == DareState.Running)) d.secondsLeft = Mathf.Max(0f, d.secondsLeft - dt);
        }

        public void OnRemoteState(in StreamWireState s)
        {
            bool wasLive = LiveState.IsLive;
            LiveState.IsLive = s.live;
            LiveState.StreamerId = s.streamer;
            LiveState.Viewers = s.viewers;
            LiveState.PeakViewers = s.peak;
            LiveState.Hype = s.hype;
            LiveState.Seconds = s.seconds;
            LiveState.MaxSeconds = s.maxSeconds > 0f ? s.maxSeconds : StreamRules.MaxSeconds;
            LiveState.PendingFollowers = s.pending;
            LiveState.NoSignal = s.noSignal;
            KrillTv.StreamSerial = s.streamSerial;

            if (s.live && !wasLive)
            {
                lastMoneySeen = s.money;
                LiveState.MoneyThisStream = s.money;
                KrillTv.LastSummary = null;
                faintLatch = false;
                droneLatch = false;
                GameEvents.RaiseStreamStarted();
                KrillTv.RaiseStarted();
                StreamSfx.GoLive();
            }
            else if (s.money > lastMoneySeen)
            {
                long delta = s.money - lastMoneySeen;
                lastMoneySeen = s.money;
                LiveState.MoneyThisStream = s.money;
                KrillTv.RaiseMoneyTicked(delta);
            }
            else LiveState.MoneyThisStream = s.money;

            // dare
            if (!s.live || s.dareIndex < 0 || StreamDares.At(s.dareIndex) == null)
            {
                if (LiveState.Dare != null)
                {
                    LiveState.Dare = null;
                    KrillTv.DareIndex = -1;
                    GameEvents.RaiseDareChanged(null);
                }
            }
            else
            {
                var def = StreamDares.At(s.dareIndex);
                var d = LiveState.Dare;
                bool fresh = d == null || KrillTv.DareSerial != s.dareSerial;
                var state = (DareState)s.dareState;
                // keep the locally predicted "running" until the host confirms (or 1.5 s pass)
                if (!fresh && predictedSerial == s.dareSerial && state == DareState.Offered && Time.unscaledTime < predictUntil) state = DareState.Running;
                bool changed = fresh || d.state != state;
                if (fresh)
                {
                    d = new DareInfo();
                    LiveState.Dare = d;
                    if (state == DareState.Offered) StreamSfx.DareOffered();
                }
                d.id = def.id;
                d.text = StreamDares.Text(def, s.dareTarget);
                d.targetPlayerId = s.dareTarget;
                if (state != DareState.Running || predictedSerial != s.dareSerial || Time.unscaledTime >= predictUntil) d.secondsLeft = s.dareSecondsLeft;
                d.reward = s.dareReward;
                if (changed && d.targetPlayerId == Net.LocalId)
                {
                    if (state == DareState.Done) StreamSfx.DareDone();
                    else if (state == DareState.Failed) StreamSfx.DareFailed();
                }
                d.state = state;
                KrillTv.DareSerial = s.dareSerial;
                KrillTv.DareIndex = s.dareIndex;
                KrillTv.DareUser = s.dareUser;
                if (changed) GameEvents.RaiseDareChanged(d);
            }
            LiveState.RaiseChanged();
        }

        public void OnRemoteSummary(StreamSummary sum)
        {
            if (sum == null) return;
            KrillTv.LastSummary = sum;
            LiveState.IsLive = false;
            LiveState.NoSignal = false;
            if (LiveState.Dare != null)
            {
                LiveState.Dare = null;
                KrillTv.DareIndex = -1;
                GameEvents.RaiseDareChanged(null);
            }
            checker.Stop();
            if (sum.fainted) StreamSfx.StreamLost(); else StreamSfx.StreamEnded();
            GameEvents.RaiseStreamEnded(sum.fainted, sum.money, sum.followersDelta);
            KrillTv.RaiseEnded(sum);
            LiveState.RaiseChanged();
        }

        public void OnRemoteChat(in ChatEmit e)
        {
            var m = StreamChat.Format(in e);
            if (string.IsNullOrEmpty(m.text)) return;
            LiveState.RaiseChat(m);
            if (m.donation > 0) StreamSfx.Coin(); else StreamSfx.ChatBlip();
        }
    }
}
