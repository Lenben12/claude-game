using UnityEngine;

namespace Sealebrities
{
    /// Chopper Shop delivery crate. Hangs under the helicopter, floats down on a parachute, lands on the helipad
    /// (or wherever a mishap sends it – L.12). Opening it receives the item (Shop.Receive); snack packs ("food:…",
    /// a white cooler with a blue lid) go to the shared pantry.
    ///
    /// Mishaps: Water (floats), HutRoof (on the roof – climb up, or shake the wall from below: it slides down), Palm
    /// (the chute tangles in a crown – shake the palm at its trunk), WrongPlace (orange smoke column for 3 minutes,
    /// then a blinking light), Village (Lio guards it – a selfie with him pays), Goat (Kapitän Meck sits on it – nudge
    /// him 3×), Boat (lands on the foredeck and rides along). Shared crate progress is Delivery.nudges (HeliService).
    public class HeliCrate : MonoBehaviour, IInteractable
    {
        enum State { Hanging, Falling, Landed, Floating, Tangled, Sliding }

        const float Size = 0.9f, Height = 0.75f, ChuteHeight = 3.6f;
        const float SnackSize = 0.66f, SnackHeight = 0.5f;

        public Delivery Delivery { get; private set; }
        public int Slot { get; private set; } = -1;
        /// The mishap as it happened on this machine (palm/roof/boat may fall back when the place is missing).
        public DeliveryMishap Mishap { get; private set; }
        /// The delivery's rolled mishap when it was dropped (HeliService moves the crate once if the host's differs).
        public DeliveryMishap RawMishap { get; private set; }
        /// True for a snack pack (pantry delivery).
        public bool IsSnack { get; private set; }
        /// On the ground, the water, in a crown or sliding – no longer under the helicopter.
        public bool IsDown => state != State.Hanging && state != State.Falling;

        HeliService service;
        State state;
        Transform chute, sling, pack, body;
        BoxCollider col;
        Vector3 vel, target;
        float t, open01, collapse01, descent = 2.8f, yaw, landTime;
        bool drifting, chuteGone, mishapRaised;
        float size = Size, height = Height;
        string promptCache, promptLabel, promptItem;
        Language promptLang;
        int promptState = -1;

        // mishap extras
        Transform goat, goatHead;
        float goatT, goatLeaveT = -1f, nextBleat;
        Vector3 goatAway;
        ParticleSystem smoke;
        float smokeUntil;
        Transform blinker;
        Light blinkLight;
        Vector3 trunkBase;
        Vector3 slideFrom, slideTo;
        float slideT;
        string proxWritten;
        int proxMode;      // 0 none, 1 palm shake, 2 roof rattle (decided in Update, prompted in LateUpdate)
        bool parentedToBoat;
        readonly KeyPrompt promptShake = new KeyPrompt(GameAction.Interact, "Palme schütteln", "Shake the palm");
        readonly KeyPrompt promptRattle = new KeyPrompt(GameAction.Interact, "Am Dach rütteln", "Rattle the roof");

        // ------------------------------------------------------------------ creation
        public static HeliCrate CreateHanging(HeliService svc, Transform hook, Delivery d, float zOff)
        {
            var go = new GameObject("DeliveryCrate");
            go.transform.SetParent(hook, false);
            go.transform.localPosition = new Vector3(0f, -1.0f, zOff);
            go.transform.localRotation = Quaternion.identity;
            var c = go.AddComponent<HeliCrate>();
            c.Setup(svc, d, true, new Vector3(0f, 1.0f, -zOff));
            c.state = State.Hanging;
            c.col.enabled = false;
            return c;
        }

        /// A crate that is already down (loaded save, a guest joining later, the host's helicopter dropped it): placed
        /// directly where its mishap put it (HeliService.LandedPlacement decides the spot).
        public static HeliCrate CreateLanded(HeliService svc, Delivery d, int slot, Vector3 pos)
        {
            var go = new GameObject("DeliveryCrate");
            go.transform.SetParent(svc.transform, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, HeliService.Hash(d) % 360u, 0f);
            var c = go.AddComponent<HeliCrate>();
            c.Setup(svc, d, false, Vector3.zero);
            c.Slot = slot;
            c.state = State.Landed;
            c.chute.gameObject.SetActive(false);
            c.chuteGone = true;
            c.pack.gameObject.SetActive(false);
            c.col.enabled = true;
            c.yaw = go.transform.eulerAngles.y;
            return c;
        }

        void Setup(HeliService svc, Delivery d, bool hanging, Vector3 slingApex)
        {
            service = svc;
            Delivery = d;
            IsSnack = d != null && FoodDatabase.FromDeliveryId(d.itemId) != null;
            if (IsSnack) { size = SnackSize; height = SnackHeight; }
            svc.Register(this);

            var bodySet = new BoatMeshSet();
            if (IsSnack) BuildSnackBox(bodySet);
            else BuildWoodCrate(bodySet);
            body = BoatOwnedMesh.Own(bodySet.Build("Body", transform, true)).transform;

            var ps = new BoatMeshSet();
            ps[BoatMats.ChuteRed].TaperBox(Vector3.zero, new Vector2(0.44f, 0.34f), new Vector2(0.36f, 0.26f), 0.17f, Quaternion.identity);
            ps[BoatMats.Strap].Box(Vector3.zero, new Vector3(0.46f, 0.05f, 0.07f));
            pack = BoatOwnedMesh.Own(ps.Build("ChutePack", transform, new Vector3(0f, height + 0.085f, 0f), Quaternion.identity, true)).transform;

            if (hanging)
            {
                var ss = new BoatMeshSet();
                float hs = size * 0.45f;
                for (int sx = -1; sx <= 1; sx += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                        ss[BoatMats.Rope].Cylinder(new Vector3(sx * hs, height, sz * hs), slingApex, 0.016f, 0.016f, 4, false, false);
                sling = BoatOwnedMesh.Own(ss.Build("Sling", transform, false)).transform;
            }

            // parachute: faceted dome of red/white gores (pivot at the crate top) + suspension lines
            var cs = new BoatMeshSet();
            BuildCanopy(cs, new Vector3(0f, ChuteHeight, 0f), 2.1f, 1.1f, 12, 3);
            for (int k = 0; k < 8; k++)
            {
                float a = k / 8f * Mathf.PI * 2f;
                Vector3 rim = new Vector3(Mathf.Cos(a) * 2.02f, ChuteHeight, Mathf.Sin(a) * 2.02f);
                Vector3 attach = new Vector3(Mathf.Sign(Mathf.Cos(a)) * size * 0.33f, 0.05f, Mathf.Sign(Mathf.Sin(a) + 0.001f) * size * 0.33f);
                cs[BoatMats.Rope].Cylinder(attach, rim, 0.012f, 0.012f, 3, false, false);
            }
            // owned mesh is attached while the canopy is still active so its OnDestroy is guaranteed to run
            var chuteGo = BoatOwnedMesh.Own(cs.Build("Parachute", transform, new Vector3(0f, height, 0f), Quaternion.identity, true));
            chute = chuteGo.transform;
            chute.localScale = Vector3.one * 0.05f;
            chuteGo.SetActive(false);

            col = gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, height * 0.5f, 0f);
            col.size = new Vector3(size, height, size);
        }

        /// Wooden shop crate: plank panels, dark corner frame, orange "Chopper Shop" band.
        void BuildWoodCrate(BoatMeshSet set)
        {
            set[BoatMats.Crate].Box(new Vector3(0f, Height * 0.5f, 0f), new Vector3(Size, Height, Size));
            var fr = set[BoatMats.CrateDark];
            float h = Size * 0.5f + 0.008f;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    fr.Box(new Vector3(sx * (h - 0.025f), Height * 0.5f, sz * (h - 0.025f)), new Vector3(0.07f, Height + 0.01f, 0.07f));
            for (int k = 0; k < 2; k++)
            {
                float y = k == 0 ? 0.035f : Height - 0.035f;
                fr.Box(new Vector3(0f, y, h - 0.025f), new Vector3(Size + 0.01f, 0.07f, 0.07f));
                fr.Box(new Vector3(0f, y, -(h - 0.025f)), new Vector3(Size + 0.01f, 0.07f, 0.07f));
                fr.Box(new Vector3(h - 0.025f, y, 0f), new Vector3(0.07f, 0.07f, Size + 0.01f));
                fr.Box(new Vector3(-(h - 0.025f), y, 0f), new Vector3(0.07f, 0.07f, Size + 0.01f));
            }
            for (int k = 1; k < 3; k++)
            {
                float y = Height * k / 3f;
                fr.Box(new Vector3(0f, y, 0f), new Vector3(Size + 0.006f, 0.012f, Size + 0.006f));
            }
            set[BoatMats.CrateBand].Box(new Vector3(0f, Height * 0.5f, 0f), new Vector3(Size + 0.014f, 0.13f, Size + 0.014f));
        }

        /// E.1 snack pack: a white cool box with a blue lid, an orange band and a little fish sticker.
        void BuildSnackBox(BoatMeshSet set)
        {
            float w = SnackSize, h = SnackHeight;
            set[BoatMats.Cooler].Box(new Vector3(0f, h * 0.42f, 0f), new Vector3(w, h * 0.84f, w * 0.78f));
            set[BoatMats.CoolerLid].TaperBox(new Vector3(0f, h * 0.9f, 0f), new Vector2(w + 0.03f, w * 0.78f + 0.03f), new Vector2(w - 0.03f, w * 0.78f - 0.03f), h * 0.18f, Quaternion.identity);
            set[BoatMats.CrateBand].Box(new Vector3(0f, h * 0.45f, 0f), new Vector3(w + 0.012f, 0.08f, w * 0.78f + 0.012f));
            set[BoatMats.Engine].Box(new Vector3(0f, h + 0.03f, 0f), new Vector3(w * 0.5f, 0.035f, 0.05f));
            // fish sticker on the front
            var sticker = set[BoatMats.Pal("#3FB8C4")];
            float fz = w * 0.39f + 0.008f;
            sticker.QuadOut(new Vector3(-0.1f, h * 0.24f, fz), new Vector3(0.06f, h * 0.24f, fz), new Vector3(0.06f, h * 0.36f, fz), new Vector3(-0.1f, h * 0.36f, fz), Vector3.forward);
            sticker.TriOut(new Vector3(0.06f, h * 0.3f, fz), new Vector3(0.13f, h * 0.22f, fz), new Vector3(0.13f, h * 0.38f, fz), Vector3.forward);
        }

        /// Faceted hemisphere canopy with alternating red/white gores. Inner faces are separate geometry (so the shared
        /// one-sided matte materials work from below too).
        static void BuildCanopy(BoatMeshSet set, Vector3 c, float r, float h, int gores, int rings)
        {
            for (int k = 0; k < gores; k++)
            {
                var b = set[k % 2 == 0 ? BoatMats.ChuteRed : BoatMats.ChuteWhite];
                float a0 = k / (float)gores * Mathf.PI * 2f, a1 = (k + 1) / (float)gores * Mathf.PI * 2f;
                for (int ri = 0; ri < rings; ri++)
                {
                    float l0 = ri / (float)rings * Mathf.PI * 0.5f, l1 = (ri + 1) / (float)rings * Mathf.PI * 0.5f;
                    Vector3 p00 = c + Dome(a0, l0, r, h), p10 = c + Dome(a1, l0, r, h);
                    Vector3 p11 = c + Dome(a1, l1, r, h), p01 = c + Dome(a0, l1, r, h);
                    Vector3 outward = (p00 + p10 + p11 + p01) * 0.25f - (c - Vector3.up * h * 0.3f);
                    b.QuadOut(p00, p10, p11, p01, outward);
                    // inner side, pulled in by 1 % so it never shares depth with the outer side
                    Vector3 i00 = c + (p00 - c) * 0.99f, i10 = c + (p10 - c) * 0.99f, i11 = c + (p11 - c) * 0.99f, i01 = c + (p01 - c) * 0.99f;
                    b.QuadOut(i00, i10, i11, i01, -outward);
                }
            }
        }

        static Vector3 Dome(float lon, float lat, float r, float h)
            => new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon) * r, Mathf.Sin(lat) * h, Mathf.Cos(lat) * Mathf.Sin(lon) * r);

        void OnDestroy()
        {
            if (service != null) service.Unregister(this);
            ReleaseProx();
        }

        /// Points the crate at the current Delivery instance (co-op snapshots replace the objects).
        public void Rebind(Delivery current)
        {
            if (current != null) Delivery = current;
        }

        /// The delivery was received elsewhere (another player / machine): disappear with a little puff.
        public void Vanish()
        {
            if (state != State.Hanging && state != State.Falling)
                BoatParticles.Puff(transform.position + Vector3.up * 0.4f, new Color(0.75f, 0.62f, 0.45f, 0.85f), 14, 0.4f);
            Destroy(gameObject);
        }

        string ItemName() => Delivery != null ? Shop.DeliveryName(Delivery) : "?";

        /// The flight keeps a pad slot free for this crate before it is released.
        public void ReserveSlot(int s) { Slot = s; }

        int Nudges
        {
            get
            {
                var cur = service != null ? service.Resolve(Delivery) : null;
                return cur != null ? cur.nudges : (Delivery != null ? Delivery.nudges : 0);
            }
        }

        // ------------------------------------------------------------------ drop
        /// The helicopter lets go. mishap decides how it comes down (drift onto the water, tangle in a palm …).
        public void Release(int slot, Vector3 landingTarget, DeliveryMishap mishap, DeliveryMishap raw, Vector3 palmTrunk)
        {
            Slot = slot;
            target = landingTarget;
            Mishap = mishap;
            RawMishap = raw;
            trunkBase = palmTrunk;
            drifting = mishap == DeliveryMishap.Water;
            transform.SetParent(service.transform, true);
            yaw = transform.eulerAngles.y;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (sling != null) Destroy(sling.gameObject);
            sling = null;
            state = State.Falling;
            t = 0f;
            vel = Vector3.zero;

            float hgt = Mathf.Max(1f, transform.position.y - target.y);
            float dist = new Vector2(target.x - transform.position.x, target.z - transform.position.z).magnitude;
            descent = 2.8f;
            if (dist > 0.1f && dist / (hgt / descent) > 10f) descent = Mathf.Max(0.9f, hgt * 10f / dist);

            if (Delivery != null)
            {
                // host/offline: shared state; co-op client: optimistic until the next host snapshot (saves are skipped there)
                var cur = service.Resolve(Delivery) ?? Delivery;
                cur.dropped = true;
                Delivery.dropped = true;
                if (GameManager.I != null) GameManager.I.Save();
            }
            string n = ItemName();
            if (mishap == DeliveryMishap.None)
                GameEvents.RaiseNotify(NotifyKind.Delivery, n, Loc.Tr("Landeplatz", "Helipad"));
            else if (mishap == DeliveryMishap.WrongPlace)
                GameEvents.RaiseNotify(NotifyKind.Delivery, HeliMishaps.NotifyLine(mishap), HeliMishaps.ZoneName(HeliMishaps.ZoneOf(Delivery)));
            else
                GameEvents.RaiseNotify(NotifyKind.Delivery, HeliMishaps.NotifyLine(mishap), n);
        }

        /// Restored crate (no flight): applies its mishap state right away.
        public void SetLandedMishap(DeliveryMishap m, DeliveryMishap raw, Vector3 palmTrunk, bool tangled, bool floating)
        {
            Mishap = m;
            RawMishap = raw;
            trunkBase = palmTrunk;
            mishapRaised = true;   // the event already happened in an earlier session / on the host
            if (floating)
            {
                state = State.Floating;
                Vector3 p = transform.position;
                p.y = Waves.HeightAt(p.x, p.z) - 0.3f;
                transform.position = p;
            }
            if (tangled)
            {
                state = State.Tangled;
                chute.gameObject.SetActive(true);
                chute.localScale = new Vector3(0.9f, 0.55f, 0.9f);
                chute.localPosition = new Vector3(0f, height - 0.2f, 0f);
                chuteGone = false;
                col.enabled = false;
            }
            AfterLanding(false);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            proxMode = 0;
            switch (state)
            {
                case State.Falling: UpdateFalling(dt); break;
                case State.Floating: UpdateFloating(dt); break;
                case State.Tangled: UpdateTangled(dt); break;
                case State.Sliding: UpdateSliding(dt); break;
                case State.Landed: UpdateLandedMishap(dt); break;
            }
            if (state == State.Landed || state == State.Floating) UpdateCollapse(dt);
            UpdateBeacon(dt);
        }

        /// The shake / rattle prompt runs after the player's interactor wrote its own prompt this frame (like DockLadder).
        void LateUpdate()
        {
            if (proxMode == 0) ReleaseProx();
            else UpdateProximityPrompt(proxMode == 1);
        }

        void UpdateFalling(float dt)
        {
            t += dt;
            if (t > 0.45f && !chute.gameObject.activeSelf)
            {
                chute.gameObject.SetActive(true);
                pack.gameObject.SetActive(false);
            }
            if (chute.gameObject.activeSelf)
            {
                open01 = Mathf.MoveTowards(open01, 1f, dt * 1.8f);
                float e = 1f - (1f - open01) * (1f - open01);
                float flutter = 1f + Mathf.Sin(t * 9f) * 0.04f * (1f - open01);
                chute.localScale = new Vector3(Mathf.Lerp(0.05f, 1f, e) * flutter, Mathf.Lerp(0.3f, 1f, e), Mathf.Lerp(0.05f, 1f, e) * flutter);
            }

            Vector3 pos = transform.position;
            float vy = vel.y;
            if (open01 < 0.25f) vy -= 9.81f * dt;
            else vy = Mathf.MoveTowards(vy, -descent, dt * 14f);

            float remainT = Mathf.Max(0.6f, (pos.y - target.y) / descent);
            Vector3 toT = new Vector3(target.x - pos.x, 0f, target.z - pos.z);
            Vector3 desired = Vector3.ClampMagnitude(toT / remainT, drifting ? 12f : 3f);
            Vector3 hv = Vector3.MoveTowards(new Vector3(vel.x, 0f, vel.z), desired, dt * (drifting ? 4f : 2f));
            vel = new Vector3(hv.x, vy, hv.z);
            pos += vel * dt;

            // L.12 palm: the canopy catches in the crown and the crate dangles there
            if (Mishap == DeliveryMishap.Palm && pos.y <= target.y + 0.4f)
            {
                transform.position = new Vector3(target.x, target.y, target.z);
                state = State.Tangled;
                landTime = Time.time;
                col.enabled = false;
                BoatSynth.PlayAt(BoatSynth.Thunk, pos, 0.4f, 1.5f, 40f);
                BoatParticles.Puff(pos + Vector3.up * height, new Color(0.5f, 0.66f, 0.3f, 0.8f), 18, 0.35f);   // leaves
                AfterLanding(true);
                return;
            }

            // a drifting crate only considers terrain and water, so it can't get stuck on a palm tree on its way out
            float surf;
            bool water;
            if (drifting)
            {
                float g = WorldInfo.GroundHeight(pos), w = Waves.HeightAt(pos.x, pos.z);
                water = w > g;
                surf = water ? w : g;
            }
            else if (Mishap == DeliveryMishap.Boat) surf = BoatSurface.BelowIncludingBoat(pos + Vector3.up * 2.5f, 6f, out water);
            else surf = BoatSurface.Below(pos + Vector3.up * 2.5f, 6f, out water);
            if (pos.y <= surf)
            {
                Land(pos, surf, water);
                return;
            }
            transform.position = pos;
            float sw = open01;
            transform.rotation = Quaternion.Euler(Mathf.Sin(t * 1.4f) * 6f * sw, yaw + Mathf.Sin(t * 0.5f) * 10f, Mathf.Sin(t * 1.1f + 1f) * 5f * sw);
        }

        void Land(Vector3 pos, float surf, bool water)
        {
            state = water ? State.Floating : State.Landed;
            pos.y = water ? surf - 0.3f : surf;
            transform.position = pos;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            col.enabled = true;
            landTime = Time.time;
            collapse01 = 0f;
            if (!chuteGone && !chute.gameObject.activeSelf)
            {
                chute.gameObject.SetActive(true);
                chute.localScale = Vector3.one;
                pack.gameObject.SetActive(false);
            }
            if (water)
            {
                BoatSynth.PlayAt(BoatSynth.Splash, pos, 0.8f, 0.9f, 60f);
                BoatParticles.Puff(pos + Vector3.up * 0.3f, new Color(0.92f, 0.96f, 1f, 0.8f), 18, 0.5f);
            }
            else
            {
                BoatSynth.PlayAt(BoatSynth.Thunk, pos, 0.7f, 0.8f, 50f);
                BoatParticles.Puff(pos + Vector3.up * 0.1f, new Color(0.7f, 0.64f, 0.52f, 0.6f), 14, 0.6f);
            }
            AfterLanding(true);
        }

        /// Mishap extras once the crate is down (fresh = a real landing now, not a restored crate).
        void AfterLanding(bool fresh)
        {
            if (fresh && !mishapRaised && Mishap != DeliveryMishap.None && Delivery != null)
            {
                mishapRaised = true;
                GameEvents.RaiseDeliveryMishap(Delivery.itemId, Mishap);
            }
            switch (Mishap)
            {
                case DeliveryMishap.Goat:
                    if (Nudges < HeliMishaps.GoatNudges && goat == null)
                    {
                        goat = HeliMishaps.BuildGoat(transform, out goatHead).transform;
                        goat.localPosition = new Vector3(0f, height, -0.05f);
                        goat.localRotation = Quaternion.Euler(0f, 30f, 0f);
                        nextBleat = Time.time + 2f;
                    }
                    break;
                case DeliveryMishap.WrongPlace:
                    smoke = HeliMishaps.SmokeColumn(transform);
                    smokeUntil = Time.time + (fresh ? HeliMishaps.SmokeSeconds : 30f);
                    if (fresh) BoatSynth.PlayAt(BoatSynth.Hiss, transform.position, 0.8f, 1f, 60f);
                    BuildBlinker();
                    break;
                case DeliveryMishap.Village:
                    BuildBlinker();
                    break;
                case DeliveryMishap.Boat:
                    AttachToBoat();
                    break;
            }
        }

        void AttachToBoat()
        {
            var boat = Refs.Boat as BoatController;
            if (boat == null || parentedToBoat) return;
            Vector3 lp = boat.transform.InverseTransformPoint(transform.position);
            if (Mathf.Abs(lp.x) > 1.6f || lp.z < -5f || lp.z > 4.5f) return;   // missed the boat after all
            transform.SetParent(boat.transform, true);
            parentedToBoat = true;
        }

        void BuildBlinker()
        {
            if (blinker != null) return;
            var s = new BoatMeshSet();
            s[BoatMats.Metal].Cylinder(new Vector3(0f, height, 0f), new Vector3(0f, height + 0.45f, 0f), 0.015f, 0.012f, 5);
            s[BoatMats.BeaconOrange].Sphere(new Vector3(0f, height + 0.5f, 0f), 0.06f, 6, 4);
            blinker = BoatOwnedMesh.Own(s.Build("Beacon", transform, false)).transform;
            var lg = new GameObject("BeaconLight");
            lg.transform.SetParent(blinker, false);
            lg.transform.localPosition = new Vector3(0f, height + 0.5f, 0f);
            blinkLight = lg.AddComponent<Light>();
            blinkLight.type = LightType.Point;
            blinkLight.color = new Color(1f, 0.5f, 0.15f);
            blinkLight.range = 9f;
            blinkLight.shadows = LightShadows.None;
            blinkLight.enabled = false;
        }

        void UpdateBeacon(float dt)
        {
            if (smoke != null && Time.time > smokeUntil)
            {
                BoatParticles.SetRate(smoke, 0f);
                if (smoke.particleCount == 0) { Destroy(smoke.gameObject); smoke = null; }
            }
            if (blinker == null) return;
            bool smoking = smoke != null && Time.time <= smokeUntil;
            bool on = !smoking && Mathf.Repeat(Time.time, 1.1f) < 0.18f;
            if (blinker.gameObject.activeSelf != (on || smoking)) blinker.gameObject.SetActive(on || smoking);
            if (blinkLight != null)
            {
                float night = EnvState.Night01;
                bool lit = on && night > 0.1f;
                if (blinkLight.enabled != lit) blinkLight.enabled = lit;
                if (lit) blinkLight.intensity = 2.5f * night;
            }
        }

        void UpdateFloating(float dt)
        {
            Vector3 p = transform.position;
            p.y = Waves.HeightAt(p.x, p.z) - 0.3f;
            transform.position = p;
            Quaternion targetRot = Quaternion.FromToRotation(Vector3.up, Waves.NormalAt(p.x, p.z)) * Quaternion.Euler(0f, yaw, 0f);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 1f - Mathf.Exp(-dt * 3f));
            yaw += dt * 3f;
        }

        void UpdateCollapse(float dt)
        {
            if (chuteGone) return;
            // the canopy sinks down beside the crate and flattens out on the ground / water
            collapse01 = Mathf.MoveTowards(collapse01, 1f, dt * 0.7f);
            float e = collapse01 * collapse01 * (3f - 2f * collapse01);
            float s = Mathf.Lerp(1f, 0.05f, e);
            float groundY = state == State.Floating ? 0.33f : 0.04f;
            chute.localRotation = Quaternion.identity;
            chute.localScale = new Vector3(1f + e * 0.15f, s, 1f + e * 0.15f);
            chute.localPosition = new Vector3(-2.6f * e, Mathf.Lerp(height, groundY - ChuteHeight * s, e), 0f);
            float keep = state == State.Floating ? 5f : 25f;
            if (collapse01 >= 1f && Time.time - landTime > keep)
            {
                chute.gameObject.SetActive(false);
                chuteGone = true;
            }
        }

        // ------------------------------------------------------------------ palm / roof
        /// Dangling in the crown: the canopy drapes over it; it falls as soon as somebody shook the palm (nudges ≥ 1).
        void UpdateTangled(float dt)
        {
            float k = Mathf.Sin(Time.time * 1.3f);
            transform.rotation = Quaternion.Euler(k * 6f, yaw + Mathf.Sin(Time.time * 0.7f) * 8f, Mathf.Sin(Time.time * 1.1f) * 5f);
            if (chute.gameObject.activeSelf)
            {
                chute.localScale = Vector3.Lerp(chute.localScale, new Vector3(0.9f, 0.55f, 0.9f), 1f - Mathf.Exp(-dt * 3f));
                chute.localPosition = Vector3.Lerp(chute.localPosition, new Vector3(0f, height - 0.2f, 0f), 1f - Mathf.Exp(-dt * 3f));
            }
            if (Nudges >= 1) StartSlide(GroundUnder(trunkBase, 1.6f));
            else proxMode = 1;
        }

        /// Where a crate coming down next to `around` lands: the ground ~1.6 m beside the trunk / wall.
        Vector3 GroundUnder(Vector3 around, float offset)
        {
            Vector3 flat = transform.position - around;
            flat.y = 0f;
            Vector3 dir = flat.sqrMagnitude > 0.01f ? flat.normalized : Vector3.forward;
            Vector3 p = around + dir * offset;
            float y = BoatSurface.Below(new Vector3(p.x, transform.position.y + 1f, p.z), 40f, out _);
            return new Vector3(p.x, y, p.z);
        }

        void StartSlide(Vector3 to)
        {
            ReleaseProx();
            state = State.Sliding;
            slideFrom = transform.position;
            slideTo = to;
            slideT = 0f;
            bool keepChute = Mishap == DeliveryMishap.Palm;
            chute.gameObject.SetActive(keepChute);
            if (!keepChute) chuteGone = true;
            BoatSynth.PlayAt(BoatSynth.Thunk, slideFrom, 0.5f, 1.3f, 30f);
            if (Mishap == DeliveryMishap.Palm)
            {
                // coconuts come down too (a bonus drink for whoever shook it)
                BoatParticles.Puff(slideFrom + Vector3.up * height, new Color(0.5f, 0.66f, 0.3f, 0.8f), 20, 0.35f);
                Coconuts.Drop(slideFrom + Vector3.up * 0.5f, transform.parent);
            }
        }

        void UpdateSliding(float dt)
        {
            slideT += dt / 0.9f;
            float a = Mathf.Clamp01(slideT);
            Vector3 p = Vector3.Lerp(slideFrom, slideTo, a);
            p.y = Mathf.Lerp(slideFrom.y, slideTo.y, a * a) + Mathf.Sin(a * Mathf.PI) * 0.4f;
            transform.position = p;
            transform.rotation = Quaternion.Euler(Mathf.Sin(a * 9f) * 14f * (1f - a), yaw + a * 50f, 0f);
            if (a >= 1f)
            {
                yaw += 50f;
                Land(slideTo, slideTo.y, false);
                if (chute.gameObject.activeSelf) { chute.localScale = Vector3.one * 0.6f; collapse01 = 0.6f; }
            }
        }

        // ------------------------------------------------------------------ landed mishaps: roof, goat, village
        void UpdateLandedMishap(float dt)
        {
            switch (Mishap)
            {
                case DeliveryMishap.HutRoof:
                    if (OnRoof)
                    {
                        if (Nudges >= 1) StartSlide(RoofSlideTarget());
                        else proxMode = 2;
                    }
                    break;
                case DeliveryMishap.Goat: UpdateGoat(dt); break;
                case DeliveryMishap.Village: UpdateVillageGuard(); break;
            }
        }

        bool OnRoof => Mishap == DeliveryMishap.HutRoof && transform.position.y > WorldInfo.GroundHeight(transform.position) + 1.8f;

        Vector3 RoofSlideTarget()
        {
            Vector3 hut = WorldLayout.Hut;
            Vector3 door = PropInfo.HutDoor != Vector3.zero ? PropInfo.HutDoor : hut + new Vector3(9f, 0f, 0f);
            Vector3 dir = door - hut;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.right;
            // slide off over the eave nearest to the door side, landing ~1.2 m out from the wall
            Vector3 p = transform.position + dir * 4.5f;
            for (int i = 0; i < 8; i++)
            {
                float y = BoatSurface.Below(new Vector3(p.x, transform.position.y + 1f, p.z), 40f, out bool water);
                if (!water && y < transform.position.y - 1.5f) return new Vector3(p.x, y, p.z);
                p += dir;
            }
            return new Vector3(p.x, WorldInfo.GroundHeight(p), p.z);
        }

        /// Proximity prompt (the interactor ray cannot reach a crate in a crown / on a roof): near the trunk / under
        /// the crate → "E – Palme schütteln" / "E – Am Dach rütteln" → nudges = 1 (shared, everybody sees it fall).
        void UpdateProximityPrompt(bool palm)
        {
            var p = Refs.Player;
            var gm = GameManager.I;
            bool near = false;
            if (gm != null && gm.InGame && p != null && !(p is Object o && o == null) && p.Root != null && !UIState.BlocksGameplay
                && !p.IsInWater && !p.IsSeated && !p.IsFainting)
            {
                Vector3 feet = p.Root.position;
                if (palm)
                {
                    Vector3 d = feet - trunkBase;
                    d.y = 0f;
                    near = d.sqrMagnitude < 2.6f * 2.6f && Mathf.Abs(feet.y - trunkBase.y) < 2.5f;
                }
                else
                {
                    Vector3 d = feet - transform.position;
                    float dy = transform.position.y - feet.y;
                    d.y = 0f;
                    near = dy > 1.5f && d.sqrMagnitude < 7f * 7f;
                }
            }
            string cur = InteractionPrompt.Text;
            if (!near || (cur != null && !ReferenceEquals(cur, proxWritten)))
            {
                ReleaseProx();
                return;
            }
            if (proxWritten != null && GameInput.InteractPressed)
            {
                ReleaseProx();
                BoatSynth.PlayAt(BoatSynth.Thunk, palm ? trunkBase + Vector3.up : transform.position, 0.5f, palm ? 0.7f : 0.9f, 20f);
                service.SetNudges(Delivery, 1);
                return;
            }
            proxWritten = palm ? promptShake.Text : promptRattle.Text;
            InteractionPrompt.Text = proxWritten;
        }

        void ReleaseProx()
        {
            if (proxWritten != null && ReferenceEquals(InteractionPrompt.Text, proxWritten)) InteractionPrompt.Text = null;
            proxWritten = null;
        }

        /// Kapitän Meck chews, looks around and bleats; after the 3rd nudge he hops off and trots away.
        void UpdateGoat(float dt)
        {
            if (goat == null) return;
            goatT += dt;
            if (goatLeaveT < 0f)
            {
                if (goatHead != null)
                    goatHead.localRotation = Quaternion.Euler(Mathf.Sin(goatT * 9f) * 4f, Mathf.Sin(goatT * 0.6f) * 35f, Mathf.Sin(goatT * 0.9f) * 6f);
                if (Time.time > nextBleat)
                {
                    nextBleat = Time.time + Random.Range(6f, 12f);
                    var p = Refs.Player;
                    if (p != null && !(p is Object o && o == null) && p.Root != null && (p.Root.position - transform.position).sqrMagnitude < 18f * 18f)
                        BoatSynth.PlayAt(BoatSynth.Bleat, goat.position + Vector3.up * 0.5f, 0.7f, Random.Range(0.9f, 1.15f), 30f);
                }
                if (Nudges >= HeliMishaps.GoatNudges)
                {
                    goatLeaveT = 0f;
                    Vector3 a = Random.insideUnitSphere;
                    a.y = 0f;
                    goatAway = (a.sqrMagnitude > 0.01f ? a.normalized : Vector3.right);
                    goat.SetParent(transform.parent, true);
                    BoatSynth.PlayAt(BoatSynth.Bleat, goat.position + Vector3.up * 0.5f, 0.8f, 1.25f, 30f);
                    promptCache = null;
                }
                return;
            }
            // hop off, trot away, vanish in a little dust puff
            goatLeaveT += dt;
            Vector3 pos = goat.position + goatAway * (2.2f * dt);
            float g = BoatSurface.Below(new Vector3(pos.x, pos.y + 2f, pos.z), 6f, out _);
            float hop = Mathf.Abs(Mathf.Sin(goatLeaveT * 9f)) * 0.18f;
            pos.y = Mathf.Lerp(pos.y, g + hop, 1f - Mathf.Exp(-dt * 8f));
            goat.position = pos;
            goat.rotation = Quaternion.Slerp(goat.rotation, Quaternion.LookRotation(goatAway), 1f - Mathf.Exp(-dt * 6f));
            if (goatLeaveT > 3.5f)
            {
                BoatParticles.Puff(goat.position + Vector3.up * 0.3f, new Color(0.7f, 0.64f, 0.52f, 0.6f), 10, 0.4f);
                Destroy(goat.gameObject);
                goat = null;
                goatHead = null;
            }
        }

        /// L.12 village: Lio guards the crate – a selfie with him (selfie view with Lio close by) pays the "fee".
        void UpdateVillageGuard()
        {
            if (Nudges >= HeliMishaps.PaidMarker) return;
            if (!SelfieState.Active) return;
            var p = Refs.Player;
            if (p == null || (p is Object o && o == null) || p.Root == null) return;
            Vector3 me = p.Root.position;
            if ((me - transform.position).sqrMagnitude > 12f * 12f) return;
            var lio = NpcRegistry.Find("lio");
            if (lio != null && lio.Root != null && (lio.Root.position - me).sqrMagnitude > 4.5f * 4.5f) return;
            service.SetNudges(Delivery, HeliMishaps.PaidMarker);
            GameEvents.RaiseNpcLine("lio", Loc.Tr("Cooles Selfie! Dein Paket, bitte sehr.", "Cool selfie! Your parcel, here you go."), 3.5f);
            promptCache = null;
        }

        // ------------------------------------------------------------------ IInteractable
        /// 0 open, 1 goat, 2 village guard, 3 out of reach (roof / crown).
        int PromptState()
        {
            switch (Mishap)
            {
                case DeliveryMishap.Goat: return Nudges < HeliMishaps.GoatNudges && goatLeaveT < 0f ? 1 : 0;
                case DeliveryMishap.Village: return Nudges < HeliMishaps.PaidMarker ? 2 : 0;
            }
            return 0;
        }

        public string GetPrompt()
        {
            if (!CanInteract()) return null;
            string label = GameInput.BindingLabel(GameAction.Interact);
            string item = Delivery != null ? Delivery.itemId : null;
            int ps = PromptState();
            int extra = ps == 1 ? Nudges : 0;
            if (promptCache == null || promptLang != Loc.Current || !ReferenceEquals(label, promptLabel) || item != promptItem || ps * 10 + extra != promptState)
            {
                promptLang = Loc.Current;
                promptLabel = label;
                promptItem = item;
                promptState = ps * 10 + extra;
                switch (ps)
                {
                    case 1:
                        promptCache = label + Loc.Tr(" – Ziege schubsen (" + (extra + 1) + "/" + HeliMishaps.GoatNudges + ")",
                                                     " – Nudge the goat (" + (extra + 1) + "/" + HeliMishaps.GoatNudges + ")");
                        break;
                    case 2:
                        promptCache = Loc.Tr("Lio bewacht es – Selfie mit ihm! ", "Lio guards it – take a selfie with him! ")
                                      + GameInput.BindingLabel(GameAction.Selfie);
                        break;
                    default:
                        promptCache = label + Loc.Tr(" – Öffnen: ", " – Open: ") + ItemName();
                        break;
                }
            }
            return promptCache;
        }

        public bool CanInteract()
        {
            if (state != State.Landed && state != State.Floating) return false;
            var gm = GameManager.I;
            if (gm == null || !gm.InGame || Delivery == null || service == null) return false;
            var cur = service.Resolve(Delivery);
            return cur != null && !service.WasOpened(cur);
        }

        public void Interact()
        {
            if (!CanInteract()) return;
            var cur = service.Resolve(Delivery);
            if (cur == null) return;
            switch (PromptState())
            {
                case 1:
                    // nudge Kapitän Meck (shared counter – everybody sees him wobble and finally leave)
                    service.SetNudges(Delivery, Mathf.Min(HeliMishaps.GoatNudges, cur.nudges + 1));
                    if (goat != null) goat.localRotation = Quaternion.Euler(0f, goat.localEulerAngles.y + 35f, 0f);
                    BoatSynth.PlayAt(BoatSynth.Bleat, transform.position + Vector3.up, 0.6f, Random.Range(1f, 1.3f), 25f);
                    promptCache = null;
                    return;
                case 2:
                    GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr("Selfie mit Lio machen", "Take a selfie with Lio"), GameInput.BindingLabel(GameAction.Selfie));
                    return;
            }
            Vector3 p = transform.position + Vector3.up * 0.4f;
            service.MarkOpened(cur);
            Shop.Receive(cur);   // co-op clients: forwarded to the host (NetCommand.OpenCrate), applied optimistically
            BoatParticles.Puff(p, new Color(0.75f, 0.62f, 0.45f, 0.85f), 26, 0.45f);
            BoatSynth.PlayAt(BoatSynth.Thunk, p, 0.8f);
            if (goat != null) Destroy(goat.gameObject);
            Destroy(gameObject);
        }
    }

    /// Two coconuts that tumble out of a shaken palm (L.12 palm mishap) – a free drink each (E – Trinken).
    public sealed class Coconuts : MonoBehaviour, IInteractable
    {
        Vector3 vel;
        bool resting;
        float life;
        readonly KeyPrompt prompt = new KeyPrompt(GameAction.Interact, "Trinken", "Drink");

        public static void Drop(Vector3 at, Transform parent)
        {
            for (int i = 0; i < 2; i++)
            {
                var go = new GameObject("Coconut");
                go.transform.SetParent(parent, false);
                go.transform.position = at + new Vector3((i - 0.5f) * 0.5f, 0.3f, 0.2f);
                var s = new BoatMeshSet();
                s[BoatMats.Pal("#6B4636")].Sphere(Vector3.zero, 0.13f, 7, 5);
                s[BoatMats.Pal("#4A3428")].Sphere(new Vector3(0f, 0.08f, 0.08f), 0.035f, 5, 3);
                BoatOwnedMesh.Own(s.Build("Nut", go.transform, true));
                var sc = go.AddComponent<SphereCollider>();
                sc.radius = 0.16f;
                sc.isTrigger = true;
                var c = go.AddComponent<Coconuts>();
                c.vel = new Vector3((i - 0.5f) * 2.4f, 1.5f, 1.2f);
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            life += dt;
            if (life > 90f) { Destroy(gameObject); return; }
            if (resting) return;
            vel.y -= 9.81f * dt;
            Vector3 p = transform.position + vel * dt;
            float g = BoatSurface.Below(p + Vector3.up * 1.5f, 4f, out bool water);
            if (p.y - 0.12f <= g)
            {
                p.y = g + (water ? 0f : 0.12f);
                if (vel.y < -2f) { vel = new Vector3(vel.x * 0.5f, -vel.y * 0.35f, vel.z * 0.5f); BoatSynth.PlayAt(BoatSynth.Thunk, p, 0.25f, 1.9f, 15f); }
                else resting = true;
            }
            transform.position = p;
            transform.Rotate(vel.z * 60f * dt, 0f, -vel.x * 60f * dt, Space.World);
        }

        public string GetPrompt() => CanInteract() ? prompt.Text : null;

        public bool CanInteract()
        {
            var p = Refs.Player;
            return resting && GameManager.I != null && GameManager.I.InGame && p != null && !(p is Object o && o == null) && !p.IsInWater;
        }

        public void Interact()
        {
            if (!CanInteract()) return;
            Pantry.DrinkCoconut();
            GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr("Kokosnuss", "Coconut"), FoodSpots.BuffChip(FoodDatabase.Get(FoodDatabase.Coconut)));
            BoatParticles.Puff(transform.position, new Color(0.95f, 0.95f, 0.9f, 0.8f), 8, 0.2f);
            Destroy(gameObject);
        }
    }
}
