using System;
using UnityEngine;

namespace Sealebrities
{
    /// D.1 reel tension minigame – the state of the LOCAL player's fight, published every frame by FishingRodTool
    /// for the UI gauge (UIReelGauge: thin semicircle 220 × 110 px above the hotbar centre, needle = Tension, zones
    /// green GreenMin..GreenMax / yellow ..YellowMax / red ..100, stamina bar = 1 − Progress01, fish icon wiggling
    /// toward RunDir). Plain static fields – reading them never allocates.
    ///
    /// Contract for other modules:
    ///  * UI: set GaugeByUI = true while your gauge is installed – the rod then shows only a short control hint
    ///    instead of its text fallback bar in the interaction prompt.
    ///  * Player: while LockStrafe is true, A/D (GameAction.Left/Right) are the counter-steer input – please do not
    ///    strafe the diver with them (W/S, look and jump stay free).
    ///  * npc (Horst's first-snap line), achievements, sponsor/quest checks: LineSnapped / FishEscaped / Landed.
    public static class FishingReel
    {
        /// A fish is on the line and the fight runs (the gauge is visible).
        public static bool Active;
        /// Line tension 0..100 %.
        public static float Tension;
        /// Green zone in % (difficulty + the rod's greenBonus on each side).
        public static float GreenMin = 35f, GreenMax = 75f;
        /// Yellow up to this, red above (D.1: 90 %).
        public const float YellowMax = FishMarket.ReelYellowMax;
        /// 0 = just hooked … 1 = landed. The gauge's stamina bar shows 1 − Progress01.
        public static float Progress01;
        public static float FishStamina01 => 1f - Progress01;
        /// −1 the fish runs to the left, +1 to the right, 0 no run.
        public static int RunDir;
        public static bool Running;
        /// The player holds the opposite direction during a run (−40 % pull).
        public static bool Countering;
        /// Use is held (the reel turns).
        public static bool Reeling;
        /// 0..1 how close the line is to snapping (time at 100 %) / to the fish getting away (slack time).
        public static float Snap01, Slack01;
        /// The hooked fish (show only a silhouette – the name appears when it is landed).
        public static FishKind Kind;
        /// Zone the needle is in: 0 below green, 1 green, 2 yellow, 3 red.
        public static int Zone;

        /// UI: set true while a gauge draws this state (the rod skips its text bar).
        public static bool GaugeByUI;
        /// Perfect reeling (tension held in the middle of the green zone, no fails): automated test runs
        /// (GameManager.AutomatedRun) – could become an accessibility option later.
        public static bool Assist;
        /// Player module: A/D counter-steer the fish right now – do not strafe with them (see the class comment).
        public static bool LockStrafe;

        /// The line snapped (tension at 100 % too long) – the fish is gone.
        public static event Action LineSnapped;
        /// The fish got away (line too slack, or the rod was put away mid-fight).
        public static event Action FishEscaped;
        /// A fish was landed by the local player (it is already in the shared cooler box).
        public static event Action<FishItem> Landed;

        public static void RaiseSnapped() => LineSnapped?.Invoke();
        public static void RaiseEscaped() => FishEscaped?.Invoke();
        public static void RaiseLanded(FishItem f) => Landed?.Invoke(f);

        public static void Clear()
        {
            Active = false; Tension = 0f; Progress01 = 0f; RunDir = 0; Running = false; Countering = false; Reeling = false;
            Snap01 = 0f; Slack01 = 0f; Kind = null; Zone = 0; LockStrafe = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Clear();
            GreenMin = 35f; GreenMax = 75f; GaugeByUI = false; Assist = false;
            LineSnapped = null; FishEscaped = null; Landed = null;
        }
    }

    /// One fight of the reel minigame (D.1) – pure simulation, no Unity objects (FishingRodTool owns one and reuses it).
    ///  * Hold Use = reel: tension +55 %/s; release: −70 %/s.
    ///  * Runs: every runMin..runMax s the fish pulls for 0.6–1.4 s with +runStrength %/s toward a side; holding the
    ///    opposite direction (A/D, or dragging the view against it) cuts that pull by 40 %.
    ///  * Progress (only while reeling): reelPower × zoneFactor × dt / stamina – green 1.0, yellow 0.5, red 0.2,
    ///    below green 0.3. Landed at 1.
    ///  * Fails: 100 % for 0.35 s × lineStrength × difficulty → the line snaps; ≤ 5 % for 2 s → the fish gets away.
    public sealed class ReelFight
    {
        public enum Result { Fighting, Landed, Snapped, Escaped }

        public FishKind Kind { get; private set; }
        public float Tension { get; private set; }
        public float Progress { get; private set; }
        public float GreenMin { get; private set; }
        public float GreenMax { get; private set; }
        public int RunDir { get; private set; }
        public bool Running => runLeft > 0f;
        public bool Countering { get; private set; }
        public bool Reeling { get; private set; }
        public float Snap01 => snapLimit > 0f ? Mathf.Clamp01(snapT / snapLimit) : 0f;
        public float Slack01 => Mathf.Clamp01(slackT / FishMarket.ReelSlackSeconds);
        /// Seconds since the current run started (visuals: leap timing); -1 when no run.
        public float RunAge { get; private set; } = -1f;
        /// The current run is a leap out of the water (leaping kinds, ~40 % of their runs).
        public bool Leap { get; private set; }
        public float RunDuration { get; private set; }
        public float Elapsed { get; private set; }

        float reelPower = 1f, snapLimit = 0.35f, stamina = 3f;
        float snapT, slackT, nextRun, runLeft;
        bool assist;
        System.Random rng;

        /// beginner: the first catches of a save (FishMarket "never junk" rule) forgive a pinned line twice as long.
        public void Begin(FishKind kind, in GearStats gear, in DifficultySettings diff, bool assistMode, System.Random random, bool beginner = false)
        {
            Kind = kind;
            rng = random ?? new System.Random();
            assist = assistMode;
            reelPower = Mathf.Max(0.3f, gear.reelPower > 0f ? gear.reelPower : 1f);
            float lineStrength = Mathf.Max(0.3f, gear.lineStrength > 0f ? gear.lineStrength : 1f);
            float snapMul = diff.snapTimeMultiplier > 0f ? diff.snapTimeMultiplier : 1f;
            snapLimit = FishMarket.ReelSnapSeconds * lineStrength * snapMul * (beginner ? 2f : 1f);
            float gMin = diff.reelGreenMin > 0f ? diff.reelGreenMin : 35f;
            float gMax = diff.reelGreenMax > 0f ? diff.reelGreenMax : 75f;
            float bonus = Mathf.Max(0f, gear.greenBonus);
            GreenMin = Mathf.Clamp(gMin - bonus, 10f, 60f);
            GreenMax = Mathf.Clamp(gMax + bonus, GreenMin + 15f, FishMarket.ReelYellowMax - 2f);
            stamina = Mathf.Max(0.5f, kind != null ? kind.stamina : 3f);
            Tension = FishMarket.ReelHookTension;
            Progress = 0f;
            snapT = slackT = 0f;
            runLeft = 0f;
            RunDir = 0;
            RunAge = -1f;
            Leap = false;
            Countering = false;
            Reeling = false;
            Elapsed = 0f;
            nextRun = NextRunDelay() * 0.6f;   // the first run comes a little sooner
        }

        float NextRunDelay()
        {
            if (Kind == null || !Kind.Runs) return float.MaxValue;
            return Mathf.Lerp(Kind.runMin, Kind.runMax, (float)rng.NextDouble());
        }

        public static float ZoneFactor(float tension, float greenMin, float greenMax)
        {
            if (tension < greenMin) return FishMarket.ZoneBelow;
            if (tension <= greenMax) return FishMarket.ZoneGreen;
            if (tension <= FishMarket.ReelYellowMax) return FishMarket.ZoneYellow;
            return FishMarket.ZoneRed;
        }

        public int Zone
        {
            get
            {
                if (Tension < GreenMin) return 0;
                if (Tension <= GreenMax) return 1;
                if (Tension <= FishMarket.ReelYellowMax) return 2;
                return 3;
            }
        }

        /// One step. hold = Use held; steer = A/D axis (−1..1); lookYawSpeed = camera yaw speed in °/s (+ = right).
        public Result Step(float dt, bool hold, float steer, float lookYawSpeed)
        {
            if (dt <= 0f) return Result.Fighting;
            Elapsed += dt;

            // ---- runs
            if (runLeft > 0f)
            {
                runLeft -= dt;
                RunAge += dt;
                if (runLeft <= 0f) { runLeft = 0f; RunAge = -1f; Leap = false; nextRun = NextRunDelay(); }
            }
            else
            {
                nextRun -= dt;
                if (nextRun <= 0f && Kind != null && Kind.Runs)
                {
                    RunDuration = Mathf.Lerp(FishMarket.ReelRunMin, FishMarket.ReelRunMax, (float)rng.NextDouble());
                    runLeft = RunDuration;
                    RunAge = 0f;
                    RunDir = rng.NextDouble() < 0.5 ? -1 : 1;
                    Leap = Kind.leaps && rng.NextDouble() < 0.4;
                }
            }
            if (runLeft <= 0f && RunAge < 0f) RunDir = 0;

            if (assist)
            {
                // test runs: perfect reeling, never fails
                Reeling = true;
                Countering = runLeft > 0f;
                float target = (GreenMin + GreenMax) * 0.5f;
                Tension = Mathf.MoveTowards(Tension, target, 80f * dt);
                Progress = Mathf.Min(1f, Progress + reelPower * dt / stamina);
                snapT = slackT = 0f;
                return Progress >= 1f ? Result.Landed : Result.Fighting;
            }

            Reeling = hold;
            Countering = runLeft > 0f && RunDir != 0 && (steer * RunDir < -0.5f || lookYawSpeed * RunDir < -25f);

            float d = hold ? FishMarket.ReelTensionUp : -FishMarket.ReelTensionDown;
            if (runLeft > 0f && Kind != null) d += Kind.runStrength * (Countering ? 1f - FishMarket.ReelCounterSteer : 1f);
            Tension = Mathf.Clamp(Tension + d * dt, 0f, 100f);

            if (hold) Progress = Mathf.Min(1f, Progress + reelPower * ZoneFactor(Tension, GreenMin, GreenMax) * dt / stamina);
            if (Progress >= 1f) return Result.Landed;

            // snap: pinned at 100 % (still pulling) – slack: the line hangs loose
            if (Tension >= 99.9f && d > 0f) snapT += dt;
            else snapT = Mathf.Max(0f, snapT - dt * 2f);
            if (snapT >= snapLimit) return Result.Snapped;

            if (Tension <= FishMarket.ReelSlackTension) slackT += dt;
            else slackT = 0f;
            if (slackT >= FishMarket.ReelSlackSeconds) return Result.Escaped;
            return Result.Fighting;
        }
    }
}
