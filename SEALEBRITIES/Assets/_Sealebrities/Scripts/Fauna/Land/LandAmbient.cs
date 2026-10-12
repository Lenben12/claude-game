using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Ambient island life that is NOT in the Sealog (L.11): chickens in the village, small crabs scuttling on the
    /// beaches by day (Q5), butterflies over meadows and jungle paths, dragonflies at the waterfall pool.
    /// Purely visual and local to each machine (not ICreatures, not replicated, not filmable for the Sealog);
    /// home spots are deterministic, so co-op machines show them in the same places. Max 32 actors, only within 50 m
    /// of the local player, no allocations per frame after the actors exist.
    public sealed class LandAmbient : MonoBehaviour
    {
        public const float ShowRange = 50f;
        public const int MaxActors = 32;

        enum Kind { Chicken, Crab, Butterfly, Dragonfly }

        sealed class Actor
        {
            public Kind kind;
            public Vector3 home;
            public Transform t;
            public FaunaSwimAnimator anim;
            public Transform[] wings;
            public Vector3 pos, target, vel;
            public float timer, phase, speed, size;
            public bool built, shown;
            public int seed;
        }

        readonly List<Actor> actors = new List<Actor>(MaxActors);
        bool planned;
        SpeciesDef chickenDef, crabDef;
        static Mesh wingMesh, bodyMesh;

        void Plan()
        {
            planned = true;
            chickenDef = new SpeciesDef
            {
                id = "amb_chicken", nameDe = "Huhn", nameEn = "Chicken", plan = BodyPlan.Bird, variant = "chicken", behavior = BehaviorType.Wanderer,
                minCm = 35, maxCm = 45, minDepth = -60, maxDepth = 0, speed = 1f, land = true,
                colorA = new Color(0.957f, 0.937f, 0.902f), colorB = new Color(0.886f, 0.784f, 0.565f), colorC = new Color(0.949f, 0.788f, 0.298f),
                bodyHeight = 0.42f, bodyWidth = 0.3f, finSize = 1.3f, dayMask = DayMask.All,
            };
            crabDef = new SpeciesDef
            {
                id = "amb_crab", nameDe = "Strandkrabbe", nameEn = "Beach crab", plan = BodyPlan.Crab, variant = "flat", behavior = BehaviorType.Crawler,
                minCm = 3, maxCm = 6, minDepth = -1.5f, maxDepth = 0, speed = 1.5f,
                colorA = new Color(0.79f, 0.69f, 0.53f), colorB = new Color(0.89f, 0.81f, 0.66f), colorC = new Color(0.36f, 0.33f, 0.41f),
                bodyHeight = 0.3f, bodyWidth = 1.1f, finSize = 1f, pattern = PatternType.Mottled,
            };
            var spots = FaunaAnchors.LandSpots;
            var rng = new System.Random(9090);
            int chickens = 0, crabs = 0, flies = 0;
            for (int i = 0; i < spots.Count && actors.Count < MaxActors; i++)
            {
                var s = spots[i];
                if (chickens < 6 && (s.kind == FaunaSpotKind.Pen || (s.kind == FaunaSpotKind.Ground && (s.tag == "camp" || s.tag == "village"))) && rng.NextDouble() < 0.5)
                { Add(Kind.Chicken, s.pos, rng); chickens++; }
                else if (crabs < 12 && s.kind == FaunaSpotKind.Beach && rng.NextDouble() < 0.18)
                { Add(Kind.Crab, s.pos, rng); crabs++; }
                else if (flies < 8 && (s.kind == FaunaSpotKind.Meadow || s.kind == FaunaSpotKind.Jungle) && rng.NextDouble() < 0.25)
                { Add(Kind.Butterfly, s.pos + Vector3.up * 1.2f, rng); flies++; }
            }
            Vector3 pool = PropInfo.PoolCenter != Vector3.zero ? PropInfo.PoolCenter : WorldLayout.WaterfallPool;
            if (pool.y == 0f) pool.y = WorldInfo.GroundHeight(pool) + 1.2f;
            for (int i = 0; i < 5 && actors.Count < MaxActors; i++) Add(Kind.Dragonfly, pool + new Vector3((float)rng.NextDouble() * 8f - 4f, 0.8f, (float)rng.NextDouble() * 8f - 4f), rng);
        }

        void Add(Kind k, Vector3 home, System.Random rng)
        {
            actors.Add(new Actor { kind = k, home = home, pos = home, target = home, seed = rng.Next(), phase = (float)rng.NextDouble() * 10f });
        }

        void Update()
        {
            var gm = GameManager.I;
            var p = Refs.Player;
            if (gm == null || !gm.InGame || p == null || p.Root == null) { HideAll(); return; }
            if (!FaunaAnchors.Built) return;
            if (!planned) Plan();
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            if (dt <= 0f) return;
            Vector3 pp = p.Root.position;
            bool day = FaunaContext.DayNow != DayMask.Night;
            for (int i = 0; i < actors.Count; i++)
            {
                var a = actors[i];
                bool want = FaunaNav.DistXZ(a.home, pp) < ShowRange && (a.kind == Kind.Chicken || day);
                if (want && !a.built) Build(a);
                if (a.t == null) continue;
                if (want != a.shown) { a.shown = want; a.t.gameObject.SetActive(want); }
                if (!want) continue;
                switch (a.kind)
                {
                    case Kind.Chicken: Walker(a, dt, pp, 0.5f, 3f, 0.35f); break;
                    case Kind.Crab: Walker(a, dt, pp, 1.2f, 1.5f, 0.05f); break;
                    default: Flyer(a, dt); break;
                }
            }
        }

        void HideAll()
        {
            for (int i = 0; i < actors.Count; i++)
                if (actors[i].t != null && actors[i].shown) { actors[i].shown = false; actors[i].t.gameObject.SetActive(false); }
        }

        void Build(Actor a)
        {
            a.built = true;
            GameObject go = null;
            try
            {
                switch (a.kind)
                {
                    case Kind.Chicken:
                    case Kind.Crab:
                        {
                            var def = a.kind == Kind.Chicken ? chickenDef : crabDef;
                            go = CreatureFactory.Build(def);
                            a.size = Mathf.Lerp(def.minCm, def.maxCm, Hash01(a.seed)) / 100f;
                            go.transform.localScale = Vector3.one * a.size;
                            var rig = go.GetComponent<CreatureRig>();
                            a.anim = new FaunaSwimAnimator();
                            a.anim.Setup(rig, def);
                            if (rig != null && rig.renderers != null)
                                for (int i = 0; i < rig.renderers.Length; i++) if (rig.renderers[i] != null) rig.renderers[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                            break;
                        }
                    default:
                        go = Insect(a);
                        break;
                }
            }
            catch (System.Exception e) { Debug.LogWarning("[Fauna] ambient " + a.kind + " failed: " + e.Message); }
            if (go == null) return;
            go.name = "Ambient_" + a.kind;
            go.transform.SetParent(transform, false);
            a.t = go.transform;
            a.shown = true;
            a.pos = a.home;
            a.pos.y = WorldInfo.GroundHeight(a.home);
        }

        static float Hash01(int s) { unchecked { uint h = (uint)s * 0x9E3779B1u; h ^= h >> 15; h *= 0x2C1B3C6Du; return (h & 0xFFFF) / 65535f; } }

        /// Chickens and beach crabs: short walks around home, pauses, step away from people.
        void Walker(Actor a, float dt, Vector3 player, float speed, float range, float scared)
        {
            a.timer -= dt;
            Vector3 away = a.pos - player; away.y = 0f;
            bool flee = away.magnitude < 1.6f + scared * 2f;
            if (a.timer <= 0f || flee)
            {
                a.timer = Random.Range(1.5f, 5f);
                if (flee) a.target = a.pos + away.normalized * Random.Range(1.5f, 3f);
                else if (Random.value < 0.45f) a.target = a.pos;   // pause (peck)
                else { Vector2 o = Random.insideUnitCircle * range; a.target = a.home + new Vector3(o.x, 0f, o.y); }
            }
            Vector3 to = a.target - a.pos; to.y = 0f;
            float d = to.magnitude;
            float sp = flee ? speed * 2.5f : speed;
            Vector3 v = d > 0.05f ? to / d * Mathf.Min(sp, d * 4f) : Vector3.zero;
            a.vel = Vector3.MoveTowards(a.vel, v, dt * 6f);
            Vector3 np = a.pos + a.vel * dt;
            float g = WorldInfo.GroundHeight(np);
            if (a.kind == Kind.Crab && (g < -0.4f || g > 1.8f)) { a.target = a.home; a.vel = Vector3.zero; np = a.pos; g = WorldInfo.GroundHeight(np); }
            np.y = g;
            a.pos = np;
            float half = (a.kind == Kind.Chicken ? chickenDef.bodyHeight : crabDef.bodyHeight) * a.size * 0.5f;
            Vector3 face = a.kind == Kind.Crab ? Vector3.Cross(Vector3.up, a.vel) : a.vel;
            Quaternion rot = a.t.rotation;
            if (face.sqrMagnitude > 0.0004f) rot = Quaternion.RotateTowards(rot, Quaternion.LookRotation(face.normalized, Vector3.up), 360f * dt);
            a.t.SetPositionAndRotation(a.pos + Vector3.up * half, rot);
            if (a.anim != null)
            {
                float speed01 = a.vel.magnitude / Mathf.Max(0.05f, speed * 0.6f);

                a.anim.Animate(dt, speed01, a.size, 0.3f, 0f, 0f, 0f, 1f, Time.time + a.phase);
            }
        }

        /// Butterflies and dragonflies: fluttering paths around home (dragonflies dart and hover).
        void Flyer(Actor a, float dt)
        {
            a.phase += dt;
            bool drag = a.kind == Kind.Dragonfly;
            a.timer -= dt;
            if (a.timer <= 0f)
            {
                a.timer = drag ? Random.Range(0.6f, 2f) : Random.Range(1.5f, 3.5f);
                Vector3 o = Random.insideUnitSphere * (drag ? 3f : 2.5f);
                o.y *= 0.4f;
                a.target = a.home + o;
                float g = WorldInfo.GroundHeight(a.target);
                a.target.y = Mathf.Max(a.target.y, Mathf.Max(g, Waves.SeaLevel) + 0.4f);
            }
            Vector3 to = a.target - a.pos;
            float sp = drag ? 3.5f : 0.9f;
            Vector3 v = Vector3.ClampMagnitude(to * (drag ? 3f : 1.2f), sp);
            v.y += drag ? 0f : Mathf.Sin(a.phase * 7f) * 0.4f;
            a.vel = Vector3.MoveTowards(a.vel, v, dt * (drag ? 12f : 3f));
            a.pos += a.vel * dt;
            Quaternion rot = a.t.rotation;
            Vector3 fl = new Vector3(a.vel.x, 0f, a.vel.z);
            if (fl.sqrMagnitude > 0.0004f) rot = Quaternion.RotateTowards(rot, Quaternion.LookRotation(fl.normalized, Vector3.up), (drag ? 720f : 240f) * dt);
            a.t.SetPositionAndRotation(a.pos, rot);
            if (a.wings != null)
            {
                float flap = drag ? 25f * Mathf.Sin(a.phase * 60f) : 70f * Mathf.Abs(Mathf.Sin(a.phase * 9f));
                for (int i = 0; i < a.wings.Length; i++)
                {
                    float side = (i % 2 == 0) ? 1f : -1f;
                    a.wings[i].localRotation = Quaternion.Euler(0f, 0f, side * flap);
                }
            }
        }

        // ------------------------------------------------------------------ tiny insect models

        GameObject Insect(Actor a)
        {
            bool drag = a.kind == Kind.Dragonfly;
            EnsureMeshes();
            var root = new GameObject("Insect");
            Color body = drag ? new Color(0.18f, 0.77f, 0.63f) : new Color(0.17f, 0.15f, 0.2f);
            Color[] wingCols = { new Color(0.93f, 0.5f, 0.18f), new Color(0.95f, 0.79f, 0.3f), new Color(0.25f, 0.66f, 0.71f), new Color(0.91f, 0.44f, 0.63f) };
            Color wing = drag ? new Color(0.81f, 0.91f, 1f) : wingCols[(int)(Hash01(a.seed) * 3.99f)];
            var b = new GameObject("Body");
            b.transform.SetParent(root.transform, false);
            b.transform.localScale = drag ? new Vector3(0.012f, 0.012f, 0.09f) : new Vector3(0.01f, 0.01f, 0.03f);
            b.AddComponent<MeshFilter>().sharedMesh = bodyMesh;
            var br = b.AddComponent<MeshRenderer>();
            br.sharedMaterial = LowPoly.Mat(body);
            br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            int nw = drag ? 4 : 2;
            a.wings = new Transform[nw];
            for (int i = 0; i < nw; i++)
            {
                var piv = new GameObject("Wing" + i).transform;
                piv.SetParent(root.transform, false);
                piv.localPosition = new Vector3(0f, 0.004f, drag ? (i < 2 ? 0.012f : -0.008f) : 0f);
                var w = new GameObject("Blade");
                w.transform.SetParent(piv, false);
                float side = (i % 2 == 0) ? 1f : -1f;
                w.transform.localPosition = new Vector3(side * (drag ? 0.035f : 0.022f), 0f, 0f);
                w.transform.localScale = drag ? new Vector3(0.07f, 1f, 0.018f) : new Vector3(0.045f, 1f, 0.04f);
                w.AddComponent<MeshFilter>().sharedMesh = wingMesh;
                var wr = w.AddComponent<MeshRenderer>();
                wr.sharedMaterial = drag ? MaterialLibrary.Transparent(new Color(wing.r, wing.g, wing.b, 0.55f), 0.6f, true) : LowPoly.Mat(wing);
                wr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                a.wings[i] = piv;
            }
            if (!drag)
            {
                // butterfly wings are double-sided cards: use a second flipped blade
                for (int i = 0; i < nw; i++)
                {
                    var back = Object.Instantiate(a.wings[i].GetChild(0).gameObject, a.wings[i], false);
                    back.transform.localRotation = Quaternion.Euler(180f, 0f, 0f);
                }
            }
            return root;
        }

        static void EnsureMeshes()
        {
            if (bodyMesh == null)
            {
                bodyMesh = new Mesh { name = "SB_AmbientBody" };
                bodyMesh.vertices = new[]
                {
                    new Vector3(0f, 0.5f, 0f), new Vector3(0.5f, 0f, 0f), new Vector3(0f, -0.5f, 0f), new Vector3(-0.5f, 0f, 0f),
                    new Vector3(0f, 0f, 0.5f), new Vector3(0f, 0f, -0.5f),
                };
                bodyMesh.triangles = new[] { 0, 4, 1, 1, 4, 2, 2, 4, 3, 3, 4, 0, 0, 1, 5, 1, 2, 5, 2, 3, 5, 3, 0, 5 };
                bodyMesh.RecalculateNormals();
                bodyMesh.RecalculateBounds();
            }
            if (wingMesh == null)
            {
                wingMesh = new Mesh { name = "SB_AmbientWing" };
                wingMesh.vertices = new[] { new Vector3(-0.5f, 0f, 0.3f), new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.4f), new Vector3(-0.5f, 0f, -0.3f) };
                wingMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                wingMesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
                wingMesh.RecalculateBounds();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { wingMesh = null; bodyMesh = null; }
    }
}
