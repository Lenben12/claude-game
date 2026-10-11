using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Sealebrities
{
    /// Allocation-free cache of the Sealog's discovered species (the detector asks once per candidate and tick).
    /// Rebuilt at most once a second, or right away after MarkDirty (new discovery, host snapshot, new game).
    public static class FilmKnown
    {
        static readonly HashSet<string> known = new HashSet<string>(StringComparer.Ordinal);
        static GameState cachedState;
        static bool dirty = true;
        static float nextRefresh;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { known.Clear(); cachedState = null; dirty = true; nextRefresh = 0f; }

        public static void MarkDirty() => dirty = true;

        public static bool IsKnown(SpeciesDef sp)
        {
            if (sp == null) return false;
            Refresh();
            return known.Contains(sp.id);
        }

        public static bool IsKnown(string speciesId)
        {
            if (string.IsNullOrEmpty(speciesId)) return false;
            Refresh();
            return known.Contains(speciesId);
        }

        static void Refresh()
        {
            var gm = GameManager.I;
            var s = gm != null ? gm.State : null;
            if (s == null) { if (known.Count > 0) known.Clear(); cachedState = null; return; }
            float now = Time.unscaledTime;
            if (!dirty && ReferenceEquals(s, cachedState) && now < nextRefresh) return;
            dirty = false;
            cachedState = s;
            nextRefresh = now + 1f;
            known.Clear();
            var j = s.journal;
            for (int i = 0; i < j.Count; i++)
                if (j[i] != null && j[i].discovered && !string.IsNullOrEmpty(j[i].speciesId)) known.Add(j[i].speciesId);
        }
    }

    /// One detection pass of the hand camera (K.2): every creature of CreatureRegistry that is visible in the
    /// viewfinder – inside the frame, within visibility, big enough, not occluded – plus the Sealog photo subject.
    /// Fixed arrays (no allocations per tick). Results stay valid until the next Run; the first
    /// min(Count, OcclusionBudget) entries are sorted by priority (Sealog-ready animals first, then weight × value),
    /// so the first 8 are the viewfinder brackets.
    public sealed class FilmDetector
    {
        public const int Capacity = 256;
        const int MaxDistinct = 64;
        /// "Danger close" for the livestream hype and chat (C.2): a dangerous animal within this distance on camera.
        public const float DangerCloseMeters = 5f;

        // ------------------------------------------------------------------ results
        public int Count;
        public readonly ICreature[] Creature = new ICreature[Capacity];
        public readonly SpeciesDef[] Species = new SpeciesDef[Capacity];
        public readonly FilmShot[] Shot = new FilmShot[Capacity];
        /// Species already in the Sealog.
        public readonly bool[] Known = new bool[Capacity];
        /// Index of the Sealog photo subject (−1 = none) and its photo quality 0..1.
        public int Subject = -1;
        public float SubjectQuality;
        /// Index of the most valuable detected animal (weight × base value × size multiplier), −1 = none.
        public int Best = -1;
        public float BestValue;
        /// Distinct species among the detected animals (first DistinctSpecies entries of DistinctList).
        public int DistinctSpecies;
        public readonly SpeciesDef[] DistinctList = new SpeciesDef[MaxDistinct];
        /// Animals whose centre is in the frame (also the ones that are too far, too small or occluded).
        public int InFrame;
        public bool AnyTooFar, AnyTooSmall, AnyOccluded;
        /// An animal fills the Sealog frame but is too dark for a Sealog photo (lamp / night vision!).
        public bool SubjectTooDark;
        /// A dangerous animal (predator / ambush) is detected; DangerIndex = the closest one.
        public bool DangerInFrame;
        public int DangerIndex = -1;
        public float DangerDistance = float.MaxValue;
        /// An undiscovered species or a Huge / Legendary animal is detected ("Wow").
        public bool WowInFrame;
        /// Detection range of this pass (m).
        public float Range;

        // ------------------------------------------------------------------ candidate scratch
        readonly ICreature[] cc = new ICreature[Capacity];
        readonly SpeciesDef[] cs = new SpeciesDef[Capacity];
        readonly FilmShot[] cshot = new FilmShot[Capacity];
        readonly float[] cprio = new float[Capacity];
        readonly bool[] cknown = new bool[Capacity];
        int cand;
        bool warnedCapacity;

        public void Clear()
        {
            if (Count > 0) { Array.Clear(Creature, 0, Count); Array.Clear(Species, 0, Count); }
            if (DistinctSpecies > 0) Array.Clear(DistinctList, 0, DistinctSpecies);
            Count = 0; Subject = -1; SubjectQuality = 0f; Best = -1; BestValue = 0f; DistinctSpecies = 0; InFrame = 0;
            AnyTooFar = AnyTooSmall = AnyOccluded = SubjectTooDark = false;
            DangerInFrame = false; DangerIndex = -1; DangerDistance = float.MaxValue; WowInFrame = false;
        }

        /// Runs one detection pass with the given lens state.
        public void Run(in FilmShotContext ctx)
        {
            Clear();
            Range = ctx.maxRange;
            cand = 0;
            if (ctx.cam == null) return;

            var list = CreatureRegistry.Active;
            float coarse = ctx.maxRange + 15f;
            float coarseSq = coarse * coarse;
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                if (c == null) continue;
                if (c is Object uo && uo == null) continue;   // destroyed but not yet unregistered (Unity fake-null)
                if (c.IsHallucination || !c.IsAlive) continue;
                if (c.Transform == null) continue;
                var sp = c.Species;
                if (sp == null) continue;
                if ((c.Position - ctx.camPos).sqrMagnitude > coarseSq) continue;

                var gate = FilmShotEvaluator.Detect(c, sp, ctx, out var shot);
                if (gate == FilmGate.OffScreen || gate == FilmGate.Hidden) continue;
                InFrame++;
                if (gate == FilmGate.TooFar) { AnyTooFar = true; continue; }
                if (gate == FilmGate.TooSmall) { AnyTooSmall = true; continue; }

                if (cand >= Capacity)
                {
                    if (!warnedCapacity) { warnedCapacity = true; Debug.LogWarning("[Filming] more than " + Capacity + " animals in frame – extra ones ignored"); }
                    continue;
                }
                bool known = FilmKnown.IsKnown(sp);
                float value = Mathf.Max(1, sp.baseValue) * Progression.SizeMultiplier(c.Tier);
                float prio = shot.weight * (1f + 0.25f * Mathf.Log(1f + value));
                if (shot.inSealogFrame && shot.sealogSized) prio += 100f + shot.extent;
                if (!known) prio += 0.5f;
                cc[cand] = c;
                cs[cand] = sp;
                cshot[cand] = shot;
                cprio[cand] = prio;
                cknown[cand] = known;
                cand++;
            }

            // the best candidates first (partial selection sort, only the occlusion budget needs an order)
            int k = Mathf.Min(cand, FilmShotEvaluator.OcclusionBudget);
            for (int a = 0; a < k; a++)
            {
                int m = a;
                float mp = cprio[a];
                for (int b = a + 1; b < cand; b++) if (cprio[b] > mp) { mp = cprio[b]; m = b; }
                if (m != a) Swap(a, m);
            }

            // line of sight: two rays for the best candidates, the rest only when close
            float untested = ctx.maxRange * FilmShotEvaluator.UntestedRangeShare;
            for (int a = 0; a < cand; a++)
            {
                bool ok = a < k ? !FilmShotEvaluator.IsOccluded(ctx, cc[a], cc[a].WorldBounds) : cshot[a].distance <= untested;
                if (!ok) { AnyOccluded = true; continue; }
                int n = Count++;
                Creature[n] = cc[a];
                Species[n] = cs[a];
                Shot[n] = cshot[a];
                Known[n] = cknown[a];
            }
            if (cand > 0) { Array.Clear(cc, 0, cand); Array.Clear(cs, 0, cand); }
            cand = 0;

            Derive(ctx);
        }

        void Swap(int a, int b)
        {
            var c = cc[a]; cc[a] = cc[b]; cc[b] = c;
            var s = cs[a]; cs[a] = cs[b]; cs[b] = s;
            var h = cshot[a]; cshot[a] = cshot[b]; cshot[b] = h;
            float p = cprio[a]; cprio[a] = cprio[b]; cprio[b] = p;
            bool kn = cknown[a]; cknown[a] = cknown[b]; cknown[b] = kn;
        }

        /// Subject, best animal, distinct species, danger and "wow" flags.
        void Derive(in FilmShotContext ctx)
        {
            float subjectExtent = 0f, darkExtent = 0f;
            for (int i = 0; i < Count; i++)
            {
                ref var s = ref Shot[i];
                var sp = Species[i];
                var c = Creature[i];

                if (s.inSealogFrame && s.sealogSized)
                {
                    if (s.light >= FilmShotEvaluator.SealogMinLight)
                    {
                        if (s.extent > subjectExtent) { subjectExtent = s.extent; Subject = i; }
                    }
                    else if (s.extent > darkExtent) darkExtent = s.extent;
                }

                float v = s.weight * Mathf.Max(1, sp.baseValue) * Progression.SizeMultiplier(c.Tier);
                if (v > BestValue || Best < 0) { BestValue = v; Best = i; }

                if (sp.IsDangerous)
                {
                    DangerInFrame = true;
                    if (s.distance < DangerDistance) { DangerDistance = s.distance; DangerIndex = i; }
                }
                if (!Known[i] || c.Tier >= SizeTier.Huge) WowInFrame = true;

                bool seen = false;
                for (int d = 0; d < DistinctSpecies; d++) if (ReferenceEquals(DistinctList[d], sp)) { seen = true; break; }
                if (!seen && DistinctSpecies < MaxDistinct) DistinctList[DistinctSpecies++] = sp;
            }
            SubjectTooDark = Subject < 0 && darkExtent > 0f;
            SubjectQuality = Subject >= 0 ? FilmShotEvaluator.PhotoQuality(Shot[Subject], ctx.steadiness) : 0f;
        }

        public bool DangerClose => DangerInFrame && DangerDistance <= DangerCloseMeters;

        /// Index of the first detected animal of this species (closest when several), −1 = none.
        public int IndexOf(string speciesId)
        {
            int best = -1;
            float bd = float.MaxValue;
            for (int i = 0; i < Count; i++)
            {
                var sp = Species[i];
                if (sp == null || !string.Equals(sp.id, speciesId, StringComparison.Ordinal)) continue;
                if (Shot[i].distance < bd) { bd = Shot[i].distance; best = i; }
            }
            return best;
        }

        public int IndexOf(ICreature c)
        {
            for (int i = 0; i < Count; i++) if (ReferenceEquals(Creature[i], c)) return i;
            return -1;
        }
    }
}
