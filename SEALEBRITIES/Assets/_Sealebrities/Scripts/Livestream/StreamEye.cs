using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Availability bits of the C.5 dares (what the diver could do right now). Computed by StreamEye on the machine of
    /// the player, sent to the host in the stream feed, so the host only offers dares that are possible.
    public static class DareBits
    {
        public const int Starfish = 1 << 0, Three = 1 << 1, Dark = 1 << 2, School = 1 << 3, Moray = 1 << 4, Deep5 = 1 << 5,
            LowAir = 1 << 6, Shark = 1 << 7, Deep15 = 1 << 8, WreckDark = 1 << 9, Angler = 1 << 10;
    }

    /// What the LOCAL camera sees (K.2 rules: every animal in the viewport within visibility, big enough, no
    /// hallucinations) plus the diver's situation (depth, air, signal, boat/dock) and what is around for dares.
    /// Own lightweight scan (5 Hz, allocation free) so the stream does not depend on the Filming module's internals.
    /// Every machine runs one for its own player; the host merges them ("every player camera counts", C.8).
    public sealed class StreamEye
    {
        public const int MaxInView = 8;

        // ---------------------------------------------------------------- situation
        public bool Valid;
        public bool CameraInHand, InWater, Underwater, OnBoat, OnDock, SignalOk, Recording, FlashlightOn, Fainting;
        public float Depth, Air01, AirSeconds, MaxSafeDepth, SeafloorDepth;
        public Vector3 Position, CamPos;

        // ---------------------------------------------------------------- camera
        /// Animals detected in frame (no occlusion test for the count).
        public int Count;
        /// Most valuable visible animal (occlusion-tested).
        public SpeciesDef Best;
        public SizeTier BestTier;
        public float BestCm;
        public bool BestIsNew;
        public SpeciesDef Biggest;
        public SizeTier BiggestTier;
        public float BiggestCm;
        /// A dangerous animal within 5 m and on camera.
        public bool DangerClose;
        public SpeciesDef DangerSpecies;
        public bool Gerald;
        public SpeciesDef LegendarySpecies;
        public float LegendaryCm;
        /// Distinct species in frame.
        public readonly SpeciesDef[] InView = new SpeciesDef[MaxInView];
        public int InViewCount;

        // ---------------------------------------------------------------- around the diver (dares)
        public int NearMask;
        public int NearSpecies;
        public float DistStarfish = float.MaxValue, DistShark = float.MaxValue, DistMoray = float.MaxValue, DistAngler = float.MaxValue;
        public bool MorayInView;
        public int SchoolCount;
        public Vector3 SchoolCenter;
        public float SchoolDist = float.MaxValue;
        public bool InWreck;
        public bool NearWreck;

        // ---------------------------------------------------------------- internals (fixed buffers)
        static Dictionary<SpeciesDef, int> index;
        static SpeciesDef[] bySlot;
        int[] schoolCount;
        Vector3[] schoolSum;
        int[] touched;
        int touchedCount;
        ulong nearA, nearB;

        const int TopK = 3;
        readonly ICreature[] top = new ICreature[TopK];
        readonly float[] topScore = new float[TopK];
        static readonly RaycastHit[] Hits = new RaycastHit[8];

        public static int IndexOf(SpeciesDef s)
        {
            EnsureIndex();
            return s != null && index.TryGetValue(s, out int i) ? i : -1;
        }

        public static SpeciesDef SpeciesAt(int i)
        {
            EnsureIndex();
            return i >= 0 && i < bySlot.Length ? bySlot[i] : null;
        }

        static void EnsureIndex()
        {
            if (index != null) return;
            var all = SpeciesDatabase.All;
            index = new Dictionary<SpeciesDef, int>(all.Count);
            bySlot = new SpeciesDef[all.Count];
            for (int i = 0; i < all.Count; i++) { index[all[i]] = i; bySlot[i] = all[i]; }
        }

        public StreamEye()
        {
            EnsureIndex();
            int n = bySlot.Length;
            schoolCount = new int[n];
            schoolSum = new Vector3[n];
            touched = new int[n];
        }

        static bool Alive(object o) => o != null && (!(o is Object uo) || uo != null);

        /// Scans the local player's view. Allocation free.
        public void Scan()
        {
            ResetResults();
            var p = Refs.Player;
            if (!Alive(p)) return;
            var root = p.Root;
            if (root == null) return;
            Camera cam = p.View;
            if (cam == null || !cam.isActiveAndEnabled) cam = Camera.main;
            Valid = true;

            Position = root.position;
            InWater = p.IsInWater;
            Underwater = p.IsUnderwater;
            Depth = Mathf.Max(0f, p.Depth);
            Air01 = Mathf.Clamp01(p.Air01);
            AirSeconds = p.AirSecondsLeft;
            MaxSafeDepth = p.MaxSafeDepth;
            FlashlightOn = p.FlashlightOn;
            Fainting = p.IsFainting;
            CameraInHand = Hands.IsHeld(HoldItem.Camera) || SelfieState.Active;
            var hc = Refs.HandCamera;
            Recording = hc != null && !(hc is Object ho && ho == null) && hc.IsRecording;
            SeafloorDepth = WorldInfo.SeafloorDepth(Position);

            var boat = Refs.Boat;
            OnBoat = Alive(boat) && boat.PlayerAboard;
            OnDock = !InWater && IsOnDock(Position);
            long followers = GameManager.I != null && GameManager.I.InGame ? GameManager.I.State.followers : 0;
            SignalOk = HasSignal(Position, followers);

            Vector3 wc = PropInfo.WreckCenter;
            NearWreck = Underwater && wc != Vector3.zero && Flat(Position - wc) < 45f;
            if (cam == null) { FinishMask(); return; }

            CamPos = cam.transform.position;
            InWreck = NearWreck && InsideWreck(CamPos);
            ScanCreatures(cam);
            FinishMask();
        }

        void ResetResults()
        {
            Valid = false; CameraInHand = false; InWater = false; Underwater = false; OnBoat = false; OnDock = false; SignalOk = false;
            Recording = false; FlashlightOn = false; Fainting = false;
            Depth = 0f; Air01 = 1f; AirSeconds = 0f; MaxSafeDepth = 0f; SeafloorDepth = 0f;
            Count = 0; Best = null; BestTier = SizeTier.Normal; BestCm = 0f; BestIsNew = false;
            Biggest = null; BiggestTier = SizeTier.Normal; BiggestCm = 0f; DangerClose = false; DangerSpecies = null; Gerald = false;
            LegendarySpecies = null; LegendaryCm = 0f;
            for (int i = 0; i < MaxInView; i++) InView[i] = null;
            InViewCount = 0;
            NearMask = 0; NearSpecies = 0;
            DistStarfish = DistShark = DistMoray = DistAngler = float.MaxValue;
            MorayInView = false; SchoolCount = 0; SchoolDist = float.MaxValue; SchoolCenter = Vector3.zero;
            InWreck = false; NearWreck = false;
        }

        static float Flat(Vector3 d) => Mathf.Sqrt(d.x * d.x + d.z * d.z);

        /// The dock deck (DockStart → DockEnd, ~3 m wide, deck at PropInfo.DockDeckY).
        public static bool IsOnDock(Vector3 p)
        {
            Vector3 a = WorldLayout.DockStart, b = WorldLayout.DockEnd;
            float minX = Mathf.Min(a.x, b.x) - 1.5f, maxX = Mathf.Max(a.x, b.x) + 1.5f;
            if (p.x < minX || p.x > maxX) return false;
            if (Mathf.Abs(p.z - a.z) > 2.6f) return false;
            return p.y > Waves.SeaLevel + 0.6f && p.y < PropInfo.DockDeckY + 3.5f;
        }

        /// Stream signal (C.1): the boat within range (150 m, 400 m from 2,500 followers); from 2,500 the relay buoy at
        /// the dock covers the same range around the dock end.
        public static bool HasSignal(Vector3 p, long followers)
        {
            float range = StreamRules.SignalRange(followers);
            float r2 = range * range;
            if (BoatPosition(out Vector3 boat) && (boat - p).sqrMagnitude <= r2) return true;
            if (followers >= StreamRules.DockFollowers)
            {
                Vector3 dock = WorldInfo.DockEndPosition != Vector3.zero ? WorldInfo.DockEndPosition : WorldLayout.DockEnd;
                Vector3 d = dock - p;
                d.y = 0f;
                if (d.sqrMagnitude <= r2) return true;
            }
            return false;
        }

        public static bool BoatPosition(out Vector3 pos)
        {
            if (BoatTelemetry.Valid) { pos = BoatTelemetry.Position; return true; }
            var boat = Refs.Boat;
            if (Alive(boat) && boat.Root != null) { pos = boat.Root.position; return true; }
            pos = Vector3.zero;
            return false;
        }

        /// Inside the MS Clickbait (no interior volume is published yet – PropInfo has only the wreck centre): close to
        /// the hull and a deck above the camera. Used by the dark-wreck dare and the DeepByte photo deal.
        public static bool InsideWreck(Vector3 p)
        {
            // props publish the exact interior volume of the MS Clickbait (H.1) – the stand-in below is the fallback
            if (PropInfo.WreckInterior.size.sqrMagnitude > 0.01f) return PropInfo.InWreckInterior(p);
            Vector3 wc = PropInfo.WreckCenter;
            if (wc == Vector3.zero) return false;
            if (Flat(p - wc) > 20f || Mathf.Abs(p.y - wc.y) > 14f) return false;
            if (!Waves.IsUnderwater(p)) return false;
            return UnderRoof(p);
        }

        static bool UnderRoof(Vector3 from)
        {
            int n = Physics.RaycastNonAlloc(from, Vector3.up, Hits, 8f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            var player = Refs.Player;
            Transform self = Alive(player) ? player.Root : null;
            for (int i = 0; i < n; i++)
            {
                var col = Hits[i].collider;
                if (col == null) continue;
                if (self != null && col.transform.IsChildOf(self)) continue;
                return true;
            }
            return false;
        }

        void ScanCreatures(Camera cam)
        {
            float range = StreamRules.DetectRange();
            float near = StreamRules.NearRange;
            float coarse = Mathf.Max(range, near) + 12f;
            float coarse2 = coarse * coarse;
            float tanHalf = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float lo = StreamRules.ViewportMargin, hi = 1f - StreamRules.ViewportMargin;
            Vector3 camPos = CamPos;
            nearA = 0UL; nearB = 0UL;
            touchedCount = 0;
            for (int k = 0; k < TopK; k++) { top[k] = null; topScore[k] = 0f; }
            float bestDanger = float.MaxValue;

            var list = CreatureRegistry.Active;
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                if (c == null) continue;
                if (c is Object uo && uo == null) continue;
                if (c.IsHallucination || !c.IsAlive) continue;
                var sp = c.Species;
                if (sp == null) continue;
                Vector3 pos = c.Position;
                Vector3 toC = pos - camPos;
                float d2 = toC.sqrMagnitude;
                if (d2 > coarse2) continue;

                int slot = IndexOf(sp);
                Bounds b = c.WorldBounds;
                float surf = Mathf.Sqrt(b.SqrDistance(camPos));

                // ---- around the diver (dares, availability)
                if (surf <= near && slot >= 0)
                {
                    if (slot < 64) nearA |= 1UL << slot; else nearB |= 1UL << (slot - 64);
                    if (sp.plan == BodyPlan.Starfish && surf < DistStarfish) DistStarfish = surf;
                    else if (sp.plan == BodyPlan.Shark && surf < DistShark) DistShark = surf;
                    else if (sp.plan == BodyPlan.Anglerfish && surf < DistAngler) DistAngler = surf;
                    else if ((sp.id == "moray" || sp.id == "ribbon_eel") && surf < DistMoray) DistMoray = surf;
                    if (sp.behavior == BehaviorType.Schooling)
                    {
                        if (schoolCount[slot] == 0) touched[touchedCount++] = slot;
                        schoolCount[slot]++;
                        schoolSum[slot] += pos;
                    }
                }

                // ---- on camera (K.2 detection)
                if (surf > range) continue;
                Vector3 vp = cam.WorldToViewportPoint(b.center);
                if (vp.z <= 0.05f || vp.x < lo || vp.x > hi || vp.y < lo || vp.y > hi) continue;
                Vector3 size = b.size;
                float extent = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
                float frac = extent / Mathf.Max(0.01f, 2f * vp.z * tanHalf);
                if (frac < (c.SizeCm <= 12f ? StreamRules.MinCoverSmall : StreamRules.MinCover)) continue;

                Count++;
                AddInView(sp);
                var tier = c.Tier;
                if (sp.id == "green_turtle") Gerald = true;
                if ((sp.id == "moray" || sp.id == "ribbon_eel") && surf <= 2.5f) MorayInView = true;
                if (tier == SizeTier.Legendary && c.SizeCm > LegendaryCm) { LegendarySpecies = sp; LegendaryCm = c.SizeCm; }
                if (c.SizeCm > BiggestCm) { Biggest = sp; BiggestCm = c.SizeCm; BiggestTier = tier; }
                if (sp.IsDangerous && surf <= StreamRules.DangerDistance && surf < bestDanger)
                {
                    bestDanger = surf;
                    DangerClose = true;
                    DangerSpecies = sp;
                }
                float score = Mathf.Max(1, sp.baseValue) * Progression.SizeMultiplier(tier) * (Journal.IsDiscovered(sp.id) ? 1f : 1.5f);
                InsertTop(c, score);
            }

            // best visible animal: the top candidates, first one with a clear line of sight
            for (int k = 0; k < TopK; k++)
            {
                var c = top[k];
                if (c == null) break;
                if (k < TopK - 1 && IsOccluded(camPos, c)) continue;
                Best = c.Species;
                BestTier = c.Tier;
                BestCm = c.SizeCm;
                BestIsNew = Best != null && !Journal.IsDiscovered(Best.id);
                break;
            }

            // biggest school around the diver
            for (int t = 0; t < touchedCount; t++)
            {
                int s = touched[t];
                int n = schoolCount[s];
                if (n > SchoolCount)
                {
                    SchoolCount = n;
                    SchoolCenter = schoolSum[s] / n;
                }
                schoolCount[s] = 0;
                schoolSum[s] = Vector3.zero;
            }
            if (SchoolCount > 0) SchoolDist = Vector3.Distance(camPos, SchoolCenter);
            NearSpecies = PopCount(nearA) + PopCount(nearB);
        }

        void AddInView(SpeciesDef sp)
        {
            for (int i = 0; i < InViewCount; i++) if (InView[i] == sp) return;
            if (InViewCount < MaxInView) InView[InViewCount++] = sp;
        }

        void InsertTop(ICreature c, float score)
        {
            for (int k = 0; k < TopK; k++)
            {
                if (top[k] != null && score <= topScore[k]) continue;
                for (int m = TopK - 1; m > k; m--) { top[m] = top[m - 1]; topScore[m] = topScore[m - 1]; }
                top[k] = c;
                topScore[k] = score;
                return;
            }
        }

        static bool IsOccluded(Vector3 camPos, ICreature c)
        {
            Vector3 center = c.WorldBounds.center;
            Vector3 to = center - camPos;
            float dist = to.magnitude;
            if (dist < 0.3f) return false;
            Vector3 dir = to / dist;
            int n = Physics.RaycastNonAlloc(camPos, dir, Hits, dist * 0.97f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            Transform self = c.Transform;
            var player = Refs.Player;
            Transform me = Alive(player) ? player.Root : null;
            for (int i = 0; i < n; i++)
            {
                var col = Hits[i].collider;
                if (col == null) continue;
                var ct = col.transform;
                if (me != null && ct.IsChildOf(me)) continue;
                if (self != null && ct.IsChildOf(self)) continue;
                return true;
            }
            return false;
        }

        static int PopCount(ulong v)
        {
            int n = 0;
            while (v != 0UL) { v &= v - 1UL; n++; }
            return n;
        }

        void FinishMask()
        {
            if (!Valid || !Underwater || Fainting) { NearMask = 0; return; }
            int m = 0;
            if (DistStarfish <= StreamRules.NearRange) m |= DareBits.Starfish;
            if (NearSpecies >= 3) m |= DareBits.Three;
            // "Lampe aus" only means something when the lamp is on and it is really dark (night or deep)
            if (SeafloorDepth > 12f && FlashlightOn && (EnvState.Night01 > 0.5f || Depth > 30f)) m |= DareBits.Dark;
            if (SchoolCount >= 5) m |= DareBits.School;
            if (DistMoray <= StreamRules.NearRange) m |= DareBits.Moray;
            if (SeafloorDepth >= MaxSafeDepth + 6f) m |= DareBits.Deep5;
            // only when 15 % can be reached in time: 25–90 s of air above 15 % at the current rate leaves ~30 s of the
            // 120 s for the ascent (no fixed Air01 cap – tank size and depth change the rate)
            float toLow = AirSeconds * (Air01 - 0.15f) / Mathf.Max(0.01f, Air01);
            if (Air01 >= 0.4f && toLow >= 25f && toLow <= 90f) m |= DareBits.LowAir;
            if (DistShark <= StreamRules.NearRange) m |= DareBits.Shark;
            if (SeafloorDepth >= MaxSafeDepth + 16f) m |= DareBits.Deep15;
            if (NearWreck) m |= DareBits.WreckDark;
            if (DistAngler <= StreamRules.NearRange) m |= DareBits.Angler;
            NearMask = m;
        }

        /// Nearest live creature of a body plan / id to the local camera (bounds distance). Used on a key press only.
        public static ICreature Nearest(Vector3 from, BodyPlan plan, string idA, string idB, out float dist)
        {
            dist = float.MaxValue;
            ICreature best = null;
            var list = CreatureRegistry.Active;
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                if (c == null) continue;
                if (c is Object uo && uo == null) continue;
                if (c.IsHallucination || !c.IsAlive) continue;
                var sp = c.Species;
                if (sp == null) continue;
                bool match = idA != null ? (sp.id == idA || sp.id == idB) : sp.plan == plan;
                if (!match) continue;
                float d = Mathf.Sqrt(c.WorldBounds.SqrDistance(from));
                if (d < dist) { dist = d; best = c; }
            }
            return best;
        }
    }
}
