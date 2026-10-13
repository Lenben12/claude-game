using UnityEngine;

namespace Sealebrities
{
    /// Round 3 (GDD §20 Q19 "Memes: lustige Fundstücke + Tier-Memes"): the curiosities of the Sealog page „Kuriositäten“.
    /// Each one is a SeaMemeActor: a deterministic, locally simulated show piece (identical on every co-op machine) that
    /// is an ICreature in CreatureRegistry – so the hand camera films it like an animal (bonus views via the species'
    /// baseValue) and a Sealog photo registers it. Animal memes use the real creature models + props (a grouper with
    /// "deal with it" sunglasses, a dancing disco octopus, a "stonks" ray climbing a zig-zag chart, a crab rave round a
    /// boombox); funny finds are props (throne with a crowned hermit crab, a sunken gaming PC with RGB, a shopping cart
    /// with a fish inside, a scuba garden gnome, a rubber duck flotilla at the surface, a rubber T-rex at the volcano's
    /// sinkhole). Never hittable, never spawned by the fauna.
    public sealed class SeaMemeActor : MonoBehaviour, ICreature
    {
        // ------------------------------------------------------------------ ICreature
        public SpeciesDef Species => species;
        public float SizeCm => sizeCm;
        public SizeTier Tier => SizeTier.Normal;
        public Transform Transform => tr;
        public Vector3 Position => bounds.center;
        public Bounds WorldBounds => bounds;
        public float Activity01 => activity;
        public bool IsHallucination => false;
        public bool IsAlive => alive && this != null && isActiveAndEnabled;

        SpeciesDef species;
        float sizeCm, activity = 0.4f;
        bool alive;
        Transform tr;
        Bounds bounds;
        float boundsTimer;
        Renderer[] renderers;

        // per kind
        string kind;
        Vector3 home;
        float yaw0, t;
        Transform model, ball, arrow, fishInCart;
        Transform[] crabs, ducks;
        FaunaSwimAnimator anim;
        FaunaSwimAnimator[] crabAnims;
        Material rgb, screen, eq;
        Light discoLight;
        CreatureRig rig;

        static readonly Color[] Rgb = { new Color(1.6f, 0.2f, 1.6f), new Color(0.2f, 1.4f, 2f), new Color(0.3f, 1.8f, 0.4f), new Color(2f, 1.2f, 0.2f), new Color(2f, 0.25f, 0.3f) };

        /// Builds the meme's model under `parent` at its world spot (or for an icon when parent is null).
        public static SeaMemeActor Create(SpeciesDef s, Transform parent, Vector3 pos, float yaw)
        {
            if (s == null) return null;
            var go = new GameObject("Meme_" + s.id);
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            var a = go.AddComponent<SeaMemeActor>();
            a.Build(s, true);
            return a;
        }

        /// Icon model of a meme (CreatureIcons.CustomModel hook): the same parts without colliders / behaviour.
        public static GameObject IconModel(SpeciesDef s)
        {
            if (s == null || !s.meme) return null;
            var go = new GameObject("MemeIcon_" + s.id);
            var a = go.AddComponent<SeaMemeActor>();
            a.Build(s, false);
            a.enabled = false;
            Object.DestroyImmediate(a);
            return go;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void InstallIconHook() { CreatureIcons.CustomModel = IconModel; }

        void Build(SpeciesDef s, bool solid)
        {
            tr = transform;
            species = s;
            kind = s.id;
            sizeCm = (s.minCm + s.maxCm) * 0.5f;
            home = tr.position;
            yaw0 = tr.eulerAngles.y;
            switch (kind)
            {
                case "meme_shades":
                    {
                        model = Creature(s, 0.7f);
                        // the grouper's head: eyes at ~42 % toward the snout, a third above the centre line
                        var head = rig != null && rig.spine != null && rig.spine.Length > 0 && rig.spine[0] != null ? rig.spine[0] : model;
                        var holder = new GameObject("Shades").transform;
                        holder.SetParent(model, false);
                        float hw = Mathf.Max(0.06f, s.bodyWidth * 0.36f);
                        SeaModels.Sunglasses(holder, 0.41f, s.bodyHeight * 0.17f, hw);
                        holder.SetParent(head, true);
                        break;
                    }
                case "meme_dance":
                    {
                        model = Creature(s, 0.8f);
                        ball = SeaModels.DiscoBall();
                        ball.SetParent(tr, false);
                        ball.localPosition = new Vector3(0f, 1.6f, 0f);
                        if (solid)
                        {
                            discoLight = PropUtil.AddPointLight(tr, tr.TransformPoint(new Vector3(0f, 1.4f, 0f)), new Color(1f, 0.4f, 0.9f), 5f, 1.8f, "DiscoLight");
                            if (discoLight != null) discoLight.shadows = LightShadows.None;
                        }
                        break;
                    }
                case "meme_stonks":
                    {
                        model = Creature(s, 1.8f);
                        arrow = SeaModels.StonksArrow();
                        arrow.SetParent(tr, false);
                        arrow.localPosition = new Vector3(0f, 1.2f, 0f);
                        arrow.localRotation = Quaternion.Euler(-40f, 0f, 0f);
                        break;
                    }
                case "meme_crabrave":
                    {
                        var box = SeaModels.Boombox(out eq);
                        box.SetParent(tr, false);
                        crabs = new Transform[8];
                        crabAnims = new FaunaSwimAnimator[8];
                        for (int i = 0; i < crabs.Length; i++)
                        {
                            var go = SafeBuild(s);
                            if (go == null) continue;
                            var ct = go.transform;
                            ct.SetParent(tr, false);
                            ct.localScale = Vector3.one * 0.16f;
                            crabs[i] = ct;
                            var cr = go.GetComponent<CreatureRig>();
                            if (cr != null) { var an = new FaunaSwimAnimator(); an.Setup(cr, s); crabAnims[i] = an; }
                        }
                        break;
                    }
                case "meme_throne":
                    {
                        model = SeaModels.Throne(solid);
                        model.SetParent(tr, false);
                        // the king: a hermit crab on the cushion
                        var king = SpeciesDatabase.Get("hermit_crab");
                        var go = SafeBuild(king);
                        if (go != null)
                        {
                            go.transform.SetParent(model, false);
                            go.transform.localPosition = new Vector3(0f, 0.74f, 0.12f);
                            go.transform.localScale = Vector3.one * 0.22f;
                            var cr = go.GetComponent<CreatureRig>();
                            if (cr != null) { anim = new FaunaSwimAnimator(); anim.Setup(cr, king); }
                        }
                        break;
                    }
                case "meme_gamingpc":
                    model = SeaModels.GamingPc(solid, out rgb, out screen);
                    model.SetParent(tr, false);
                    break;
                case "meme_ducks":
                    {
                        ducks = new Transform[24];
                        for (int i = 0; i < ducks.Length; i++)
                        {
                            var d = SeaModels.Duck();
                            d.SetParent(tr, false);
                            float a = i * 2.39996f;
                            float rr = 0.6f + Mathf.Sqrt(i) * 0.75f;
                            d.localPosition = new Vector3(Mathf.Cos(a) * rr, 0f, Mathf.Sin(a) * rr);
                            d.localRotation = Quaternion.Euler(0f, i * 47f, 0f);
                            ducks[i] = d;
                        }
                        break;
                    }
                case "meme_cart":
                    {
                        model = SeaModels.ShoppingCart(solid);
                        model.SetParent(tr, false);
                        var fishDef = SpeciesDatabase.Get("parrotfish");
                        var go = SafeBuild(fishDef);
                        if (go != null)
                        {
                            fishInCart = go.transform;
                            fishInCart.SetParent(model, false);
                            fishInCart.localPosition = new Vector3(0f, 0.62f, 0f);
                            fishInCart.localScale = Vector3.one * 0.45f;
                            var cr = go.GetComponent<CreatureRig>();
                            if (cr != null) { anim = new FaunaSwimAnimator(); anim.Setup(cr, fishDef); }
                        }
                        break;
                    }
                case "meme_gnome":
                    model = SeaModels.Gnome(solid);
                    model.SetParent(tr, false);
                    break;
                case "meme_dino":
                    model = SeaModels.RubberDino(solid);
                    model.SetParent(tr, false);
                    break;
                default:
                    model = Creature(s, sizeCm / 100f);
                    break;
            }
            renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) renderers[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            alive = true;
            t = (Mathf.Abs(FindsDatabase.StableHash(s.id)) % 100) * 0.1f;
            UpdateBounds();
            if (solid && isActiveAndEnabled) CreatureRegistry.Register(this);   // OnEnable ran before the build
        }

        static GameObject SafeBuild(SpeciesDef s)
        {
            if (s == null) return null;
            try { return CreatureFactory.Build(s); }
            catch (System.Exception e) { Debug.LogWarning("[Fauna] meme model " + s.id + " failed: " + e.Message); return null; }
        }

        Transform Creature(SpeciesDef s, float lengthM)
        {
            var go = SafeBuild(s);
            if (go == null) { go = new GameObject("Missing"); }
            var ct = go.transform;
            ct.SetParent(tr, false);
            ct.localScale = Vector3.one * lengthM;
            rig = go.GetComponent<CreatureRig>();
            if (rig != null) { anim = new FaunaSwimAnimator(); anim.Setup(rig, s); }
            return ct;
        }

        void OnEnable()
        {
            if (alive && enabled) CreatureRegistry.Register(this);
        }

        void OnDisable() { CreatureRegistry.Unregister(this); }
        void OnDestroy() { CreatureRegistry.Unregister(this); alive = false; }

        // ------------------------------------------------------------------ show
        void Update()
        {
            if (!alive) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            t += dt;
            var cam = FaunaContext.HasPlayer ? FaunaContext.DiverList[0].head : tr.position + Vector3.forward * 100f;
            float camD = (cam - tr.position).magnitude;
            bool near = camD < 45f;
            switch (kind)
            {
                case "meme_shades":
                    {
                        // hovers, then slowly turns to look cool into the camera
                        Vector3 p = home + new Vector3(0f, 0.6f + Mathf.Sin(t * 0.7f) * 0.12f, 0f);
                        Vector3 to = cam - p; to.y = 0f;
                        float want = camD < 12f && to.sqrMagnitude > 0.01f ? Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg : yaw0 + Mathf.Sin(t * 0.15f) * 40f;
                        float yaw = Mathf.MoveTowardsAngle(tr.eulerAngles.y, want, 25f * dt);
                        tr.SetPositionAndRotation(p, Quaternion.Euler(0f, yaw, 0f));
                        if (near && anim != null) anim.Animate(dt, 0.2f, 0.7f, 0.25f, 0f, 0f, 0f, 1f, t);
                        activity = 0.35f;
                        break;
                    }
                case "meme_dance":
                    {
                        float beat = t * Mathf.PI * 2f * 1.05f;
                        Vector3 p = home + new Vector3(Mathf.Sin(beat * 0.5f) * 0.25f, 0.45f + Mathf.Abs(Mathf.Sin(beat)) * 0.3f, 0f);
                        tr.SetPositionAndRotation(p, Quaternion.Euler(Mathf.Sin(beat) * 10f, yaw0 + Mathf.Sin(beat * 0.25f) * 70f, Mathf.Sin(beat * 0.5f) * 14f));
                        if (near && anim != null) anim.Animate(dt, 2.2f, 0.8f, 1f, Mathf.Sin(beat * 0.25f) * 120f, 0f, 0f, 1f, t * 1.6f);
                        if (ball != null) ball.localRotation = Quaternion.Euler(0f, t * 60f, 0f);
                        if (discoLight != null)
                        {
                            discoLight.enabled = near;
                            int k = (int)(t * 2f) % Rgb.Length;
                            discoLight.color = Rgb[k] * 0.5f;
                        }
                        activity = 1f;
                        break;
                    }
                case "meme_stonks":
                    {
                        // a 16 s chart: three steep climbs with little dips, then a slow glide back down
                        float cyc = Mathf.Repeat(t, 16f);
                        float x, y;
                        if (cyc < 11f)
                        {
                            float u = cyc / 11f;
                            x = Mathf.Lerp(-9f, 9f, u);
                            float steps = u * 3f;
                            float f = steps - Mathf.Floor(steps);
                            y = Mathf.Floor(steps) * 2.2f + (f < 0.75f ? f / 0.75f * 2.8f : 2.8f - (f - 0.75f) / 0.25f * 0.6f);
                        }
                        else
                        {
                            float u = (cyc - 11f) / 5f;
                            x = Mathf.Lerp(9f, -9f, u);
                            y = Mathf.Lerp(8.8f, 0f, u * u * (3f - 2f * u));
                        }
                        Quaternion yawQ = Quaternion.Euler(0f, yaw0, 0f);
                        Vector3 p = home + yawQ * new Vector3(0f, y, x);
                        Vector3 v = (p - tr.position) / dt;
                        tr.position = p;
                        if (v.sqrMagnitude > 0.01f) tr.rotation = Quaternion.Slerp(tr.rotation, Quaternion.LookRotation(v.normalized, Vector3.up), Mathf.Clamp01(dt * 4f));
                        if (arrow != null) arrow.localRotation = Quaternion.Euler(cyc < 11f ? -40f : 35f, 0f, 0f);
                        if (near && anim != null) anim.Animate(dt, 1.2f, 1.8f, 0.6f, 0f, 0f, 0f, 1f, t);
                        activity = 0.7f;
                        break;
                    }
                case "meme_crabrave":
                    {
                        float beat = t * Mathf.PI * 2f * 2f;   // 120 bpm
                        if (crabs != null)
                            for (int i = 0; i < crabs.Length; i++)
                            {
                                var c = crabs[i];
                                if (c == null) continue;
                                float a = i * Mathf.PI * 2f / crabs.Length + t * 0.35f;
                                c.localPosition = new Vector3(Mathf.Cos(a) * 0.85f, 0.04f + Mathf.Abs(Mathf.Sin(beat + i * 0.1f)) * 0.08f, Mathf.Sin(a) * 0.85f);
                                c.localRotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg + Mathf.Sin(beat * 0.5f) * 20f, 0f);
                                if (near && crabAnims[i] != null) crabAnims[i].Animate(dt, 1f, 0.16f, 1f, 0f, 0f, 0f, 3f, t + i);
                            }
                        if (eq != null && near) PropMats.SetEmission(eq, Rgb[(int)(t * 4f) % Rgb.Length]);
                        activity = 1f;
                        break;
                    }
                case "meme_throne":
                    if (near && anim != null) anim.Animate(dt, 0f, 0.22f, 0.6f, 0f, 0f, 0f, 1f, t);   // action 0: the king never hides in his shell
                    activity = 0.3f;
                    break;
                case "meme_gamingpc":
                    if (near)
                    {
                        if (rgb != null) PropMats.SetEmission(rgb, Color.Lerp(Rgb[(int)t % Rgb.Length], Rgb[((int)t + 1) % Rgb.Length], t - Mathf.Floor(t)));
                        if (screen != null) PropMats.SetEmission(screen, new Color(0.25f, 0.45f, 1.4f) * (0.85f + 0.15f * Mathf.Sin(t * 9f)));
                    }
                    activity = 0.5f;
                    break;
                case "meme_ducks":
                    if (ducks != null && camD < 140f)
                        for (int i = 0; i < ducks.Length; i++)
                        {
                            var d = ducks[i];
                            if (d == null) continue;
                            Vector3 wp = d.position;
                            float h = Waves.HeightAt(wp.x, wp.z);
                            d.position = new Vector3(wp.x, h - 0.03f, wp.z);
                            d.localRotation = Quaternion.Euler(Mathf.Sin(t * 1.3f + i) * 8f, i * 47f + t * 6f, Mathf.Cos(t * 1.1f + i) * 8f);
                        }
                    activity = 0.4f;
                    break;
                case "meme_cart":
                    if (fishInCart != null)
                    {
                        fishInCart.localRotation = Quaternion.Euler(0f, Mathf.Sin(t * 0.5f) * 30f, 0f);
                        if (near && anim != null) anim.Animate(dt, 0.3f, 0.3f, 0.3f, 0f, 0f, 0f, 1f, t);
                    }
                    activity = 0.3f;
                    break;
                case "meme_gnome":
                case "meme_dino":
                    if (model != null) model.localRotation = Quaternion.Euler(Mathf.Sin(t * 0.8f) * 2f, 0f, Mathf.Cos(t * 0.6f) * 2f);
                    activity = 0.25f;
                    break;
            }
            boundsTimer -= dt;
            if (boundsTimer <= 0f) { boundsTimer = 0.25f; UpdateBounds(); }
        }

        void UpdateBounds()
        {
            bool any = false;
            Bounds b = default;
            if (renderers != null)
                for (int i = 0; i < renderers.Length; i++)
                {
                    var r = renderers[i];
                    if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                    if (r is SkinnedMeshRenderer || r is MeshRenderer)
                    {
                        if (!any) { b = r.bounds; any = true; }
                        else b.Encapsulate(r.bounds);
                    }
                }
            if (!any) b = new Bounds(tr.position, Vector3.one * Mathf.Max(0.2f, sizeCm / 100f));
            bounds = b;
        }
    }
}
