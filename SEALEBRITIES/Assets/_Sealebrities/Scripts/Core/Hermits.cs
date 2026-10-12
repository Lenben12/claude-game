using System;
using UnityEngine;

namespace Sealebrities
{
    /// Round 3 (GDD §19 „verstecktes Lager“, §20 Q2–Q8, Q47): the shared state of one hermit of the jungle camp.
    /// Lives in GameState.hermits (host save, replicated with the snapshot). Mood −100 (furious) … +100 (friend).
    [Serializable]
    public class HermitState
    {
        public string id;
        /// −100 … +100 – gifts and the right signs raise it, wrong signs / hits lower it (Hermits.AddMood).
        public int mood;
        /// Fights this hermit lost against the crew (Q7: strength earns respect).
        public int respect;
        /// The crew met him at least once (first sign / talk / fight).
        public bool met;
        /// He was a friend once (mood ≥ Friendly) – his daily baseline never falls below neutral again.
        public bool friend;
        /// Day of the signs below and the bit mask of the signs he already counted today (full value once per day).
        public int signDay = -1;
        public int signMask;
        /// Gifts today (the first three count in full).
        public int giftDay = -1;
        public int gifts;
        /// Day + count of trades (lines / mood bonus only for the first two per day).
        public int tradeDay = -1;
        public int trades;
    }

    /// Mood bands + host-side mutations of the hermits (npc module drives the behaviour, the UI reads the bands).
    /// Every mutation is host / offline only; clients send NpcNet requests and receive the state with the snapshot
    /// (ForceSnapshot after each change – mood changes are rare events).
    public static class Hermits
    {
        /// Band thresholds: ≤ Hostile attacks strangers who come close, < Grumpy shoos, ≥ Friendly teaches + gives jobs.
        public const int Hostile = -50, Grumpy = -10, Friendly = 30, Min = -100, Max = 100;
        /// Fighting stamina of a hermit (the stick does 10 per bonk, a wooden spear ~16).
        public const float Stamina = 50f;

        public static event Action<string> Changed;

        static GameState S => GameManager.I != null ? GameManager.I.State : null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Changed = null; }

        public static HermitState Get(string id)
        {
            var s = S;
            if (s == null || s.hermits == null || string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < s.hermits.Count; i++) if (s.hermits[i] != null && s.hermits[i].id == id) return s.hermits[i];
            return null;
        }

        /// Current mood (the start mood of the hermit's definition before the crew ever met him).
        public static int Mood(string id)
        {
            var h = Get(id);
            if (h != null) return h.mood;
            var d = HermitDatabase.Get(id);
            return d != null ? d.startMood : 0;
        }

        public static int Respect(string id) { var h = Get(id); return h != null ? h.respect : 0; }

        public static bool IsHostile(string id) => Mood(id) <= Hostile;
        public static bool IsGrumpy(string id) => Mood(id) < Grumpy;
        public static bool IsFriendly(string id) => Mood(id) >= Friendly;

        /// 0 hostile, 1 grumpy, 2 neutral, 3 friendly.
        public static int Band(int mood) => mood <= Hostile ? 0 : mood < Grumpy ? 1 : mood < Friendly ? 2 : 3;

        public static string BandName(int band)
        {
            switch (band)
            {
                case 0: return Loc.Tr("Wütend", "Furious");
                case 1: return Loc.Tr("Misstrauisch", "Wary");
                case 2: return Loc.Tr("Ruhig", "Calm");
                default: return Loc.Tr("Freundlich", "Friendly");
            }
        }

        public static Color BandColor(int band)
        {
            switch (band)
            {
                case 0: return new Color(0.86f, 0.3f, 0.26f);
                case 1: return new Color(0.95f, 0.62f, 0.25f);
                case 2: return new Color(0.95f, 0.85f, 0.45f);
                default: return new Color(0.45f, 0.8f, 0.4f);
            }
        }

        /// HOST: the hermit's state (created with his start mood).
        public static HermitState GetOrAdd(string id)
        {
            var s = S;
            if (s == null || string.IsNullOrEmpty(id)) return null;
            if (s.hermits == null) s.hermits = new System.Collections.Generic.List<HermitState>();
            var h = Get(id);
            if (h != null) return h;
            var d = HermitDatabase.Get(id);
            h = new HermitState { id = id, mood = d != null ? d.startMood : 0 };
            s.hermits.Add(h);
            return h;
        }

        /// HOST: changes the mood (clamped) – returns the new mood.
        public static int AddMood(string id, int delta)
        {
            if (!Net.HasAuthority) return Mood(id);
            var h = GetOrAdd(id);
            if (h == null) return 0;
            h.met = true;
            int old = h.mood;
            h.mood = Mathf.Clamp(h.mood + delta, Min, Max);
            if (h.mood >= Friendly) h.friend = true;
            if (h.mood != old) Raise(id);
            return h.mood;
        }

        /// HOST: sets the mood to at least `floor`.
        public static void Raise(string id, int floor)
        {
            var h = GetOrAdd(id);
            if (h == null || h.mood >= floor) return;
            AddMood(id, floor - h.mood);
        }

        /// HOST: another fight lost against the crew.
        public static void AddRespect(string id)
        {
            if (!Net.HasAuthority) return;
            var h = GetOrAdd(id);
            if (h == null) return;
            h.respect++;
            h.met = true;
            Raise(id);
        }

        /// HOST: the sign counts in full (first time today for this hermit) – marks it.
        public static bool FirstSignToday(string id, int sign)
        {
            var h = GetOrAdd(id);
            if (h == null) return false;
            int day = GameManager.I != null ? GameManager.I.State.day : 1;
            if (h.signDay != day) { h.signDay = day; h.signMask = 0; }
            int bit = 1 << Mathf.Clamp(sign, 0, 30);
            if ((h.signMask & bit) != 0) return false;
            h.signMask |= bit;
            return true;
        }

        /// HOST: counts a gift – true while it is one of today's first three.
        public static bool CountGift(string id)
        {
            var h = GetOrAdd(id);
            if (h == null) return false;
            int day = GameManager.I != null ? GameManager.I.State.day : 1;
            if (h.giftDay != day) { h.giftDay = day; h.gifts = 0; }
            h.gifts++;
            return h.gifts <= 3;
        }

        /// HOST: counts a trade – true for today's first two.
        public static bool CountTrade(string id)
        {
            var h = GetOrAdd(id);
            if (h == null) return false;
            int day = GameManager.I != null ? GameManager.I.State.day : 1;
            if (h.tradeDay != day) { h.tradeDay = day; h.trades = 0; }
            h.trades++;
            return h.trades <= 2;
        }

        /// HOST, new day: moods drift a quarter of the way back to each hermit's baseline (respect + friendship
        /// raise it), so a bad day is forgiven and a good one fades slowly.
        public static void NewDay()
        {
            var s = S;
            if (s == null || s.hermits == null || !Net.HasAuthority) return;
            for (int i = 0; i < s.hermits.Count; i++)
            {
                var h = s.hermits[i];
                if (h == null) continue;
                int b = Baseline(h);
                int d = b - h.mood;
                if (d == 0) continue;
                int step = d > 0 ? Mathf.Max(1, d / 4) : Mathf.Min(-1, d / 4);
                h.mood = Mathf.Clamp(h.mood + step, Min, Max);
            }
            Raise(null);
        }

        public static int Baseline(HermitState h)
        {
            var d = HermitDatabase.Get(h.id);
            int b = d != null ? d.startMood : 0;
            b += h.respect * 20;
            if (h.friend) b = Mathf.Max(b, Friendly - 5);
            return Mathf.Clamp(b, Min, Max);
        }

        /// Normalizes a loaded list (unknown ids, NaN-free ints are fine).
        public static void Normalize(System.Collections.Generic.List<HermitState> l)
        {
            if (l == null) return;
            for (int i = l.Count - 1; i >= 0; i--)
            {
                var h = l[i];
                if (h == null || string.IsNullOrEmpty(h.id)) { l.RemoveAt(i); continue; }
                h.mood = Mathf.Clamp(h.mood, Min, Max);
                h.respect = Mathf.Clamp(h.respect, 0, 99);
                for (int k = 0; k < i; k++) if (l[k] != null && l[k].id == h.id) { l.RemoveAt(i); break; }
            }
        }

        static void Raise(string id)
        {
            try { Changed?.Invoke(id); }
            catch (Exception e) { Debug.LogException(e); }
            if (Net.IsHost) NetState.ForceSnapshot();
        }
    }
}
