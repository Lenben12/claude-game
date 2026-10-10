using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Fishing spot at the end of the dock (first money). E casts a bobber 6–9 m out; after 2–7 s a fish bites and
    /// the player has 0.9 s to strike with E. The catch is sold immediately.
    public class FishingSpot : MonoBehaviour, IInteractable
    {
        enum Phase { Idle, Casting, Waiting, Bite, Reeling }

        struct FishKind
        {
            public string de, en;
            public int min, max, tex;
            public float len, hMul, wMul, weight;
        }

        static readonly FishKind[] Kinds =
        {
            new FishKind { de = "Makrele", en = "Mackerel", min = 18, max = 28, tex = 0, len = 0.32f, hMul = 0.75f, wMul = 0.9f, weight = 0.4f },
            new FishKind { de = "Sardine", en = "Sardine", min = 10, max = 16, tex = 1, len = 0.19f, hMul = 0.72f, wMul = 0.8f, weight = 0.4f },
            new FishKind { de = "Brasse", en = "Sea bream", min = 25, max = 40, tex = 2, len = 0.3f, hMul = 1.35f, wMul = 0.85f, weight = 0.2f },
        };

        const float WindUp = 0.2f, FlightTime = 0.75f, BiteWindow = 0.9f, ReelTime = 1.1f, MaxDistance = 6f;
        const int MaxBucketFish = 5;

        Phase phase;
        float phaseTime, timer, nibbleTimer, nibble;
        int lastEFrame = -1;
        Vector3 castFrom, target, fishFrom, facing;
        int fishKind;
        Transform bobber, idleRod, bucketFishRoot, handRod, handTipPivot, handTip;
        GameObject flyingFish;
        LineRenderer line;
        readonly Vector3[] linePts = new Vector3[14];
        ParticleSystem splash;
        readonly List<GameObject> bucketFish = new List<GameObject>();
        static readonly Vector3 HandPos = new Vector3(0.3f, -0.32f, 0.42f);
        static readonly Quaternion HandRot = Quaternion.Euler(-28f, 6f, 0f);

        // ------------------------------------------------------------------ creation
        public static FishingSpot Create(Transform parent)
        {
            Vector3 spot = WorldInfo.FishingSpot;
            if (spot == Vector3.zero) spot = WorldLayout.FishingSpot;
            Vector3 dockEnd = WorldInfo.DockEndPosition;
            if (dockEnd == Vector3.zero) dockEnd = WorldLayout.DockEnd;

            Vector3 facing = spot - dockEnd;
            facing.y = 0f;
            if (facing.sqrMagnitude < 0.25f) facing = dockEnd - WorldLayout.DockStart;
            facing.y = 0f;
            if (facing.sqrMagnitude < 1e-4f) facing = Vector3.right;
            facing.Normalize();

            // find the dock planks under the spot (fall back toward the dock end, then to a fixed height)
            Vector3 pos = spot;
            bool found = false;
            for (int i = 0; i <= 4 && !found; i++)
            {
                Vector3 c = Vector3.Lerp(spot, dockEnd, i * 0.25f);
                float y = BoatSurface.Below(new Vector3(c.x, Mathf.Max(spot.y, 0f) + 6f, c.z), 12f, out bool water);
                if (!water)
                {
                    pos = new Vector3(c.x, y, c.z);
                    found = true;
                }
            }
            if (!found) pos.y = Mathf.Max(spot.y, 1.0f);

            var go = new GameObject("FishingSpot");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(facing, Vector3.up));
            var fs = go.AddComponent<FishingSpot>();
            fs.facing = facing;
            fs.Init();
            return fs;
        }

        void Init()
        {
            FishingVisuals.BuildSpot(transform, out idleRod, out bucketFishRoot);
            var bc = gameObject.AddComponent<BoxCollider>();
            bc.center = new Vector3(0f, 0.45f, -0.15f);
            bc.size = new Vector3(1.1f, 0.9f, 0.75f);

            bobber = FishingVisuals.BuildBobber(transform);
            bobber.gameObject.SetActive(false);

            var lg = new GameObject("FishingLine");
            lg.transform.SetParent(transform, false);
            line = lg.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = linePts.Length;
            line.widthMultiplier = 0.008f;
            line.sharedMaterial = BoatMats.Line;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;

            splash = BoatParticles.Splash(transform);

            GameEvents.GameStarted += OnGameStarted;
            GameEvents.ReturnedToMenu += OnReturnedToMenu;
        }

        void OnDestroy()
        {
            GameEvents.GameStarted -= OnGameStarted;
            GameEvents.ReturnedToMenu -= OnReturnedToMenu;
            if (handRod != null) Destroy(handRod.gameObject);
        }

        void OnGameStarted()
        {
            EndSession(false);
            RefreshBucket();
        }

        void OnReturnedToMenu() => EndSession(false);

        // ------------------------------------------------------------------ IInteractable
        public string GetPrompt()
        {
            switch (phase)
            {
                case Phase.Idle: return CanFish() ? Loc.Tr("E – Angeln", "E – Fish") : null;
                case Phase.Casting:
                case Phase.Waiting: return Loc.Tr("E – Einholen", "E – Reel in");
                case Phase.Bite: return Loc.Tr("E – Anschlagen!", "E – Strike!");
                default: return null;
            }
        }

        public bool CanInteract() => phase == Phase.Idle ? CanFish() : phase != Phase.Reeling;

        /// A cast is in progress (E is handled by the spot itself until the session ends).
        public bool SessionActive => phase != Phase.Idle;

        public void Interact()
        {
            // FishingSpot.Update may already have handled this E press (e.g. reeled in -> Idle); don't recast in the same frame.
            if (lastEFrame == Time.frameCount) return;
            if (phase == Phase.Idle)
            {
                if (CanFish()) StartFishing();
            }
            else HandleE();
        }

        static bool CanFish()
        {
            var gm = GameManager.I;
            var p = Refs.Player;
            if (gm == null || !gm.InGame || p == null) return false;
            return !p.IsInWater && !p.IsUnderwater && !p.IsSeated && !p.IsFainting;
        }

        bool StillValid()
        {
            if (!CanFish()) return false;
            Transform r = Refs.Player.Root;
            if (r == null) return false;
            Vector3 d = r.position - transform.position;
            d.y = 0f;
            return d.sqrMagnitude < MaxDistance * MaxDistance;
        }

        // ------------------------------------------------------------------ session
        void StartFishing()
        {
            var p = Refs.Player;
            lastEFrame = Time.frameCount;
            EnsureHandRod(p.View);
            idleRod.gameObject.SetActive(false);
            target = ChooseTarget(p);
            phase = Phase.Casting;
            phaseTime = 0f;
            castFrom = RodTip();
            bobber.gameObject.SetActive(true);
            bobber.position = castFrom;
            line.enabled = true;
            BoatSynth.PlayAt(BoatSynth.Reel, castFrom, 0.35f, 1.7f, 20f);
        }

        void EnsureHandRod(Camera view)
        {
            if (view == null)
            {
                if (handRod != null) handRod.gameObject.SetActive(false);
                return;
            }
            if (handRod == null) handRod = FishingVisuals.BuildRod(view.transform, false, out handTipPivot, out handTip);
            if (handRod.parent != view.transform) handRod.SetParent(view.transform, false);
            handRod.localPosition = HandPos;
            handRod.localRotation = HandRot;
            handTipPivot.localRotation = Quaternion.identity;
            handRod.gameObject.SetActive(true);
        }

        Vector3 RodTip()
        {
            if (handRod != null && handRod.gameObject.activeInHierarchy) return handTip.position;
            var p = Refs.Player;
            if (p != null && p.Root != null) return p.Root.position + Vector3.up * 1.5f + facing * 0.6f;
            return transform.position + Vector3.up * 1.5f;
        }

        Vector3 ChooseTarget(IPlayer p)
        {
            Vector3 origin = p.Root != null ? p.Root.position : transform.position;
            Vector3 look = p.View != null ? p.View.transform.forward : facing;
            look.y = 0f;
            Vector3 dockAxis = transform.position - WorldLayout.IslandCenter;
            dockAxis.y = 0f;
            Vector3[] dirs =
            {
                look.sqrMagnitude > 0.01f ? look.normalized : facing,
                facing,
                Quaternion.Euler(0f, 35f, 0f) * facing,
                Quaternion.Euler(0f, -35f, 0f) * facing,
                dockAxis.sqrMagnitude > 0.01f ? dockAxis.normalized : facing,
            };
            for (int i = 0; i < dirs.Length; i++)
                for (int k = 0; k < 3; k++)
                {
                    Vector3 c = origin + dirs[i] * Random.Range(6f, 9f);
                    if (IsGoodWater(c)) return new Vector3(c.x, Waves.SeaLevel, c.z);
                }
            Vector3 f = transform.position + facing * 7.5f;
            return new Vector3(f.x, Waves.SeaLevel, f.z);
        }

        static bool IsGoodWater(Vector3 p)
        {
            if (WorldInfo.GroundHeight(p) > Waves.SeaLevel - 0.8f) return false;
            if (BoatSurface.IsBlocked(p)) return false;
            if (Refs.Boat != null && Refs.Boat.Root != null)
            {
                Vector3 d = Refs.Boat.Root.position - p;
                d.y = 0f;
                if (d.sqrMagnitude < 5.5f * 5.5f) return false;
            }
            return true;
        }

        void HandleE()
        {
            if (lastEFrame == Time.frameCount) return;
            lastEFrame = Time.frameCount;
            switch (phase)
            {
                case Phase.Casting:
                case Phase.Waiting:
                    EndSession(false);
                    GameEvents.RaiseToast(Loc.Tr("Angel eingeholt.", "Line reeled in."));
                    break;
                case Phase.Bite:
                    Catch();
                    break;
            }
        }

        int PickKind()
        {
            float total = 0f;
            for (int i = 0; i < Kinds.Length; i++) total += Kinds[i].weight;
            float r = Random.value * total;
            for (int i = 0; i < Kinds.Length; i++)
            {
                r -= Kinds[i].weight;
                if (r <= 0f) return i;
            }
            return 0;
        }

        void Catch()
        {
            var gm = GameManager.I;
            int k = PickKind();
            var fk = Kinds[k];
            int value = Random.Range(fk.min, fk.max + 1);
            // tutorial guarantee: the first 3 catches always pay for the first tank (3 x 27 = 81 $ >= 80 $)
            if (gm.State.fishCaught < 3) value = Mathf.Max(value, 27);
            gm.AddMoney(value);
            gm.State.fishCaught++;
            GameEvents.RaiseFishCaught(value);
            GameEvents.RaiseToast(Loc.Tr($"+{Loc.Money(value)} – {fk.de} verkauft", $"+{Loc.Money(value)} – {fk.en} sold"));

            fishKind = k;
            fishFrom = bobber.position;
            if (flyingFish != null) Destroy(flyingFish);
            flyingFish = FishingVisuals.BuildFish(transform, fk.tex, fk.len * 1.6f, fk.hMul, fk.wMul, true);
            flyingFish.transform.position = fishFrom;
            bobber.gameObject.SetActive(false);
            BoatParticles.EmitAt(splash, fishFrom, 16);
            BoatSynth.PlayAt(BoatSynth.Splash, fishFrom, 0.5f, 1.3f, 30f);
            BoatSynth.PlayAt(BoatSynth.Reel, RodTip(), 0.5f, 1f, 20f);
            phase = Phase.Reeling;
            phaseTime = 0f;
        }

        void EndSession(bool missed)
        {
            if (missed) GameEvents.RaiseToast(Loc.Tr("Weg ist er …", "It got away …"));
            phase = Phase.Idle;
            if (bobber != null) bobber.gameObject.SetActive(false);
            if (line != null) line.enabled = false;
            if (handRod != null) handRod.gameObject.SetActive(false);
            if (idleRod != null) idleRod.gameObject.SetActive(true);
            if (flyingFish != null) Destroy(flyingFish);
            flyingFish = null;
        }

        void RefreshBucket()
        {
            int want = GameManager.I != null ? Mathf.Min(GameManager.I.State.fishCaught, MaxBucketFish) : 0;
            while (bucketFish.Count > want)
            {
                Destroy(bucketFish[bucketFish.Count - 1]);
                bucketFish.RemoveAt(bucketFish.Count - 1);
            }
            while (bucketFish.Count < want)
            {
                int i = bucketFish.Count;
                var fk = Kinds[i % Kinds.Length];
                var f = FishingVisuals.BuildFish(bucketFishRoot, fk.tex, fk.len, fk.hMul, fk.wMul, false);
                float a = (i * 72f + 15f) * Mathf.Deg2Rad;
                f.transform.localPosition = new Vector3(Mathf.Cos(a) * 0.07f, 0.3f + (i % 2) * 0.03f, Mathf.Sin(a) * 0.07f);
                f.transform.localRotation = Quaternion.Euler(-72f - (i % 3) * 5f, i * 72f, 0f);
                bucketFish.Add(f);
            }
        }

        // ------------------------------------------------------------------ update
        void Update()
        {
            if (phase == Phase.Idle) return;
            if (!StillValid())
            {
                EndSession(false);
                return;
            }
            if (UIState.BlocksGameplay)
            {
                UpdateLine();
                return;
            }
            float dt = Time.deltaTime;
            phaseTime += dt;
            if (GameInput.InteractPressed) HandleE();
            if (phase == Phase.Idle) return;

            float rodAngle = 0f, bend = 0f, sag = 0.4f;
            switch (phase)
            {
                case Phase.Casting:
                {
                    if (phaseTime < WindUp)
                    {
                        rodAngle = Mathf.Lerp(0f, -40f, phaseTime / WindUp);
                        bobber.position = RodTip();
                        castFrom = bobber.position;
                    }
                    else
                    {
                        float s = Mathf.Clamp01((phaseTime - WindUp) / FlightTime);
                        rodAngle = Mathf.Lerp(-40f, 12f, Mathf.Clamp01((phaseTime - WindUp) / 0.25f));
                        Vector3 p = Vector3.Lerp(castFrom, target, s) + Vector3.up * (2.5f * 4f * s * (1f - s));
                        bobber.position = p;
                        sag = 0.1f;
                        if (s >= 1f)
                        {
                            Vector3 w = new Vector3(target.x, Waves.HeightAt(target.x, target.z), target.z);
                            BoatParticles.EmitAt(splash, w, 12);
                            BoatSynth.PlayAt(BoatSynth.Plop, w, 0.6f, 1f, 30f);
                            phase = Phase.Waiting;
                            phaseTime = 0f;
                            timer = Random.Range(2f, 7f);
                            nibbleTimer = Random.Range(0.7f, 1.6f);
                        }
                    }
                    break;
                }
                case Phase.Waiting:
                {
                    nibbleTimer -= dt;
                    if (nibbleTimer <= 0f && timer > 0.6f)
                    {
                        nibble = 1f;
                        nibbleTimer = Random.Range(0.8f, 2f);
                    }
                    nibble = Mathf.MoveTowards(nibble, 0f, dt * 4f);
                    float dip = nibble * 0.05f * Mathf.Abs(Mathf.Sin(phaseTime * 30f));
                    bobber.position = new Vector3(target.x, Waves.HeightAt(target.x, target.z) - dip, target.z);
                    bobber.rotation = Quaternion.Euler(Mathf.Sin(Time.time * 1.7f) * 8f, 0f, Mathf.Sin(Time.time * 1.3f) * 8f);
                    rodAngle = Mathf.Sin(Time.time * 1.3f) * 1.5f + nibble * 3f;
                    sag = 0.5f;
                    timer -= dt;
                    if (timer <= 0f)
                    {
                        phase = Phase.Bite;
                        phaseTime = 0f;
                        timer = BiteWindow;
                        GameEvents.RaiseToast(Loc.Tr("Biss! Drück E!", "Bite! Press E!"));
                        Vector3 w = bobber.position;
                        BoatParticles.EmitAt(splash, w, 8);
                        BoatSynth.PlayAt(BoatSynth.Plop, w, 0.8f, 0.8f, 30f);
                    }
                    break;
                }
                case Phase.Bite:
                {
                    float dip = 0.2f + Mathf.Sin(phaseTime * 30f) * 0.04f;
                    bobber.position = new Vector3(target.x + Mathf.Sin(phaseTime * 13f) * 0.05f, Waves.HeightAt(target.x, target.z) - dip, target.z);
                    rodAngle = 12f + Mathf.Sin(phaseTime * 35f) * 3f;
                    bend = 22f;
                    sag = 0.03f;
                    timer -= dt;
                    if (timer <= 0f) EndSession(true);
                    break;
                }
                case Phase.Reeling:
                {
                    float s = Mathf.Clamp01(phaseTime / ReelTime);
                    Vector3 to = bucketFishRoot.position + Vector3.up * 0.45f;
                    Vector3 ctrl = (fishFrom + to) * 0.5f + Vector3.up * 3f;
                    float a = 1f - s;
                    Vector3 p = a * a * fishFrom + 2f * a * s * ctrl + s * s * to;
                    Vector3 tangent = 2f * a * (ctrl - fishFrom) + 2f * s * (to - ctrl);
                    if (flyingFish != null)
                    {
                        flyingFish.transform.position = p;
                        if (tangent.sqrMagnitude > 1e-4f)
                            flyingFish.transform.rotation = Quaternion.LookRotation(tangent.normalized, Vector3.up) * Quaternion.Euler(0f, Mathf.Sin(phaseTime * 28f) * 30f, 0f);
                    }
                    rodAngle = -35f;
                    bend = 10f * (1f - s);
                    sag = 0f;
                    if (s >= 1f)
                    {
                        BoatSynth.PlayAt(BoatSynth.Plop, to, 0.5f, 1.4f, 20f);
                        EndSession(false);
                        RefreshBucket();
                        return;
                    }
                    break;
                }
            }

            if (handRod != null && handRod.gameObject.activeSelf)
            {
                handRod.localRotation = HandRot * Quaternion.Euler(rodAngle, 0f, 0f);
                handTipPivot.localRotation = Quaternion.Euler(bend, 0f, 0f);
            }
            currentSag = sag;
            UpdateLine();
        }

        float currentSag = 0.4f;

        void UpdateLine()
        {
            if (line == null || !line.enabled) return;
            Vector3 a = RodTip();
            Vector3 b;
            if (phase == Phase.Reeling && flyingFish != null) b = flyingFish.transform.position + flyingFish.transform.forward * (flyingFish.transform.localScale.z * 0.5f);
            else b = bobber.position + Vector3.up * 0.1f;
            int n = linePts.Length;
            float dist = Vector3.Distance(a, b);
            float sagM = currentSag * Mathf.Min(1f, dist / 6f);
            for (int i = 0; i < n; i++)
            {
                float s = i / (n - 1f);
                linePts[i] = Vector3.Lerp(a, b, s) + Vector3.down * (sagM * 4f * s * (1f - s) * (1f + 0.3f * (1f - s)));
            }
            line.SetPositions(linePts);
        }
    }
}
