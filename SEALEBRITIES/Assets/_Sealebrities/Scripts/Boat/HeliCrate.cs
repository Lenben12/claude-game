using UnityEngine;

namespace Sealebrities
{
    /// Chopper Shop delivery crate. Hangs under the helicopter, floats down on a parachute, lands on the helipad
    /// (or, rarely, drifts onto the water and floats). Opening it receives the item (Shop.Receive).
    public class HeliCrate : MonoBehaviour, IInteractable
    {
        enum State { Hanging, Falling, Landed, Floating }

        const float Size = 0.9f, Height = 0.75f, ChuteHeight = 3.6f;

        public Delivery Delivery { get; private set; }
        public int Slot { get; private set; } = -1;

        HeliService service;
        State state;
        Transform chute, sling, pack;
        BoxCollider col;
        Vector3 vel, target;
        float t, open01, collapse01, descent = 2.8f, yaw, landTime;
        bool drifting, chuteGone;
        string promptCache;
        Language promptLang;

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

        public static HeliCrate CreateLanded(HeliService svc, Delivery d, int slot, Vector3 pos)
        {
            var go = new GameObject("DeliveryCrate");
            go.transform.SetParent(svc.transform, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            var c = go.AddComponent<HeliCrate>();
            c.Setup(svc, d, false, Vector3.zero);
            c.Slot = slot;
            c.state = State.Landed;
            c.chute.gameObject.SetActive(false);
            c.chuteGone = true;
            c.pack.gameObject.SetActive(false);
            c.col.enabled = true;
            return c;
        }

        void Setup(HeliService svc, Delivery d, bool hanging, Vector3 slingApex)
        {
            service = svc;
            Delivery = d;
            svc.Register(this);

            // wooden shop crate: plank panels, dark corner frame, orange "Chopper Shop" band
            var body = new BoatMeshSet();
            body[BoatMats.Crate].Box(new Vector3(0f, Height * 0.5f, 0f), new Vector3(Size, Height, Size));
            var fr = body[BoatMats.CrateDark];
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
            // plank seams on the side panels (thin dark strips)
            for (int k = 1; k < 3; k++)
            {
                float y = Height * k / 3f;
                fr.Box(new Vector3(0f, y, 0f), new Vector3(Size + 0.006f, 0.012f, Size + 0.006f));
            }
            body[BoatMats.CrateBand].Box(new Vector3(0f, Height * 0.5f, 0f), new Vector3(Size + 0.014f, 0.13f, Size + 0.014f));
            BoatOwnedMesh.Own(body.Build("Body", transform, true));

            var ps = new BoatMeshSet();
            ps[BoatMats.ChuteRed].TaperBox(Vector3.zero, new Vector2(0.44f, 0.34f), new Vector2(0.36f, 0.26f), 0.17f, Quaternion.identity);
            ps[BoatMats.Strap].Box(Vector3.zero, new Vector3(0.46f, 0.05f, 0.07f));
            pack = BoatOwnedMesh.Own(ps.Build("ChutePack", transform, new Vector3(0f, Height + 0.085f, 0f), Quaternion.identity, true)).transform;

            if (hanging)
            {
                var ss = new BoatMeshSet();
                for (int sx = -1; sx <= 1; sx += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                        ss[BoatMats.Rope].Cylinder(new Vector3(sx * 0.4f, Height, sz * 0.4f), slingApex, 0.016f, 0.016f, 4, false, false);
                sling = BoatOwnedMesh.Own(ss.Build("Sling", transform, false)).transform;
            }

            // parachute: faceted dome of red/white gores (pivot at the crate top) + suspension lines
            var cs = new BoatMeshSet();
            BuildCanopy(cs, new Vector3(0f, ChuteHeight, 0f), 2.1f, 1.1f, 12, 3);
            for (int k = 0; k < 8; k++)
            {
                float a = k / 8f * Mathf.PI * 2f;
                Vector3 rim = new Vector3(Mathf.Cos(a) * 2.02f, ChuteHeight, Mathf.Sin(a) * 2.02f);
                Vector3 attach = new Vector3(Mathf.Sign(Mathf.Cos(a)) * 0.3f, 0.05f, Mathf.Sign(Mathf.Sin(a) + 0.001f) * 0.3f);
                cs[BoatMats.Rope].Cylinder(attach, rim, 0.012f, 0.012f, 3, false, false);
            }
            // owned mesh is attached while the canopy is still active so its OnDestroy is guaranteed to run
            var chuteGo = BoatOwnedMesh.Own(cs.Build("Parachute", transform, new Vector3(0f, Height, 0f), Quaternion.identity, true));
            chute = chuteGo.transform;
            chute.localScale = Vector3.one * 0.05f;
            chuteGo.SetActive(false);

            col = gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, Height * 0.5f, 0f);
            col.size = new Vector3(Size, Height, Size);
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
        }

        string ItemName()
        {
            var item = Delivery != null ? GearDatabase.Get(Delivery.itemId) : null;
            return item != null ? item.Name : (Delivery != null ? Delivery.itemId : "?");
        }

        // ------------------------------------------------------------------ drop
        public void Release(int slot, Vector3 landingTarget, bool drift)
        {
            Slot = slot;
            target = landingTarget;
            drifting = drift;
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
                Delivery.dropped = true;
                if (GameManager.I != null) GameManager.I.Save();
            }
            string n = ItemName();
            if (drift)
                GameEvents.RaiseToast(Loc.Tr($"Windböe! Dein Paket „{n}“ treibt aufs Wasser …", $"Gust of wind! Your package “{n}” drifts onto the water …"));
            else
                GameEvents.RaiseToast(Loc.Tr($"Paket abgeworfen: {n} – hol es am Heli-Landeplatz ab", $"Package dropped: {n} – pick it up at the helipad"));
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            switch (state)
            {
                case State.Falling: UpdateFalling(dt); break;
                case State.Floating: UpdateFloating(dt); break;
            }
            if (state == State.Landed || state == State.Floating) UpdateCollapse(dt);
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

            // a drifting crate only considers terrain and water, so it can't get stuck on a palm tree on its way out
            float surf;
            bool water;
            if (drifting)
            {
                float g = WorldInfo.GroundHeight(pos), w = Waves.HeightAt(pos.x, pos.z);
                water = w > g;
                surf = water ? w : g;
            }
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
            if (!chute.gameObject.activeSelf)
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
            chute.localPosition = new Vector3(-2.6f * e, Mathf.Lerp(Height, groundY - ChuteHeight * s, e), 0f);
            float keep = state == State.Floating ? 5f : 25f;
            if (collapse01 >= 1f && Time.time - landTime > keep)
            {
                chute.gameObject.SetActive(false);
                chuteGone = true;
            }
        }

        // ------------------------------------------------------------------ IInteractable
        public string GetPrompt()
        {
            if (!CanInteract()) return null;
            if (promptCache == null || promptLang != Loc.Current)
            {
                promptLang = Loc.Current;
                promptCache = Loc.Tr("E – Paket öffnen: ", "E – Open package: ") + ItemName();
            }
            return promptCache;
        }

        public bool CanInteract()
        {
            if (state != State.Landed && state != State.Floating) return false;
            var gm = GameManager.I;
            return gm != null && gm.InGame && Delivery != null && gm.State.deliveries.Contains(Delivery);
        }

        public void Interact()
        {
            if (!CanInteract()) return;
            Vector3 p = transform.position + Vector3.up * 0.4f;
            Shop.Receive(Delivery);
            BoatParticles.Puff(p, new Color(0.75f, 0.62f, 0.45f, 0.85f), 26, 0.45f);
            BoatSynth.PlayAt(BoatSynth.Thunk, p, 0.8f);
            Destroy(gameObject);
        }
    }
}
