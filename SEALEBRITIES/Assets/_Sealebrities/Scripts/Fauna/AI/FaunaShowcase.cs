using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Q43 support: a decorative mixed school for cut-scenes (intro under water, menu, photo mode) – by default 40 fish
    /// (20 blue tang, 12 clownfish, 8 parrotfish) circling a point or a camera in loose, species-sorted rings, with the
    /// real swim animation (FaunaSwimAnimator). Pure visuals: no FaunaCreature, no CreatureRegistry, no Sealog, no net,
    /// deterministic layout (fixed seed). Models come from the fauna pool templates (cheap once prewarmed).
    ///
    ///   var school = FaunaShowcase.IntroSchool(parent, cameraTransform);   // follows the camera
    ///   ...
    ///   school.Dispose();
    public sealed class FaunaShowcase : MonoBehaviour
    {
        public static readonly string[] IntroIds = { "blue_tang", "clownfish", "parrotfish" };
        public static readonly int[] IntroCounts = { 20, 12, 8 };
        public static readonly float[] IntroSizesCm = { 24f, 11f, 38f };

        /// Optional: the ring centre follows this transform (e.g. the intro camera); otherwise Center is used.
        public Transform follow;
        /// Offset from the followed transform, in its local space (in front of a camera: +z).
        public Vector3 followOffset = new Vector3(0f, -0.3f, 1.5f);
        public Vector3 Center;
        /// Global multiplier for the ring radius (1 = 2.4–5.2 m) and the swim speed.
        public float radiusMul = 1f, speedMul = 1f;
        /// +1 counter-clockwise seen from above, -1 clockwise.
        public float direction = 1f;

        sealed class Fish
        {
            public Transform t;
            public FaunaSwimAnimator anim;
            public float radius, height, angle, angSpeed, lengthM, bobPhase, bobAmp, radialPhase;
        }

        readonly List<Fish> fish = new List<Fish>(48);
        readonly List<GameObject> owned = new List<GameObject>(48);
        float time;

        /// The Q43 school: 20 blue tang, 12 clownfish, 8 parrotfish around `follow` (or `center` when follow is null).
        public static FaunaShowcase IntroSchool(Transform parent, Transform follow, Vector3 center = default, int seed = 43)
        {
            var s = Create(parent, center, IntroIds, IntroCounts, IntroSizesCm, seed);
            if (s != null) s.follow = follow;
            return s;
        }

        /// Any mixed school: ids[i] × counts[i] at sizesCm[i]. Unknown ids are skipped. Never returns null.
        public static FaunaShowcase Create(Transform parent, Vector3 center, string[] ids, int[] counts, float[] sizesCm, int seed)
        {
            var go = new GameObject("FaunaShowcase");
            if (parent != null) go.transform.SetParent(parent, false);
            var sc = go.AddComponent<FaunaShowcase>();
            sc.Center = center;
            sc.Build(ids, counts, sizesCm, seed);
            return sc;
        }

        void Build(string[] ids, int[] counts, float[] sizesCm, int seed)
        {
            if (ids == null || counts == null) return;
            var rng = new System.Random(seed);
            int groups = Mathf.Min(ids.Length, counts.Length);
            for (int g = 0; g < groups; g++)
            {
                var sp = SpeciesDatabase.Get(ids[g]);
                if (sp == null) continue;
                float cm = sizesCm != null && g < sizesCm.Length && sizesCm[g] > 0f ? sizesCm[g] : Mathf.Max(5f, (sp.minCm + sp.maxCm) * 0.5f);
                // each species keeps its own band: small fish on the inside ring, large ones outside
                float bandR = 2.4f + Mathf.Clamp01((cm - 8f) / 40f) * 2.2f;
                float bandY = g == 1 ? -0.5f : (g == 2 ? 0.45f : 0f);
                float bandA = g * 2.1f;
                for (int i = 0; i < counts[g]; i++)
                {
                    var t = MakeModel(sp);
                    if (t == null) break;
                    float len = cm * (0.9f + 0.2f * (float)rng.NextDouble()) / 100f;
                    t.localScale = Vector3.one * len;
                    var f = new Fish
                    {
                        t = t,
                        lengthM = len,
                        radius = bandR + ((float)rng.NextDouble() - 0.5f) * 1.2f,
                        height = bandY + ((float)rng.NextDouble() - 0.5f) * 1.1f,
                        angle = bandA + ((float)rng.NextDouble() - 0.5f) * 2.6f,
                        bobPhase = (float)rng.NextDouble() * 6.283f,
                        bobAmp = 0.08f + 0.12f * (float)rng.NextDouble(),
                        radialPhase = (float)rng.NextDouble() * 6.283f
                    };
                    // ~1.1 m/s along the ring, small ones a little slower
                    float v = Mathf.Lerp(0.8f, 1.25f, Mathf.Clamp01((cm - 8f) / 30f)) * (0.92f + 0.16f * (float)rng.NextDouble());
                    f.angSpeed = v / Mathf.Max(1f, f.radius);
                    var rig = t.GetComponent<CreatureRig>();
                    if (rig != null)
                    {
                        f.anim = new FaunaSwimAnimator();
                        f.anim.Setup(rig, sp);
                        f.anim.SetLureEnabled(false);
                    }
                    fish.Add(f);
                }
            }
            Place(0f);
            for (int i = 0; i < owned.Count; i++) if (owned[i] != null) owned[i].SetActive(true);
        }

        Transform MakeModel(SpeciesDef sp)
        {
            GameObject go = null;
            try
            {
                var spawner = FaunaModule.Spawner;
                go = spawner != null ? spawner.CloneModel(sp, transform) : CreatureFactory.Build(sp);
            }
            catch (Exception e) { Debug.LogWarning("[Fauna] showcase: could not build " + sp.id + ": " + e.Message); }
            if (go == null) return null;
            go.name = sp.id + "_showcase";
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            var cols = go.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++) if (cols[i] != null) cols[i].enabled = false;
            owned.Add(go);
            return go.transform;
        }

        Vector3 CurrentCenter()
        {
            if (follow != null) return follow.TransformPoint(followOffset);
            return Center;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            time += dt;
            Place(dt);
        }

        void Place(float dt)
        {
            Vector3 c = CurrentCenter();
            float sea = Waves.SeaLevel - 0.6f;
            float dir = direction >= 0f ? 1f : -1f;
            for (int i = 0; i < fish.Count; i++)
            {
                var f = fish[i];
                if (f.t == null) continue;
                f.angle += f.angSpeed * speedMul * dir * dt;
                float r = (f.radius + Mathf.Sin(time * 0.35f + f.radialPhase) * 0.35f) * radiusMul;
                float ca = Mathf.Cos(f.angle), sa = Mathf.Sin(f.angle);
                Vector3 p = c + new Vector3(ca * r, f.height + Mathf.Sin(time * 0.9f + f.bobPhase) * f.bobAmp, sa * r);
                // stay in the water and off the sand
                float ground = WorldInfo.Built ? WorldInfo.GroundHeight(p) + 0.35f : -9999f;
                if (p.y > sea) p.y = sea;
                if (p.y < ground) p.y = ground;
                // tangent of the circle (+ the slight bob) = swim direction
                Vector3 tan = new Vector3(-sa, 0f, ca) * dir;
                tan.y = Mathf.Cos(time * 0.9f + f.bobPhase) * f.bobAmp * 0.6f;
                f.t.SetPositionAndRotation(p, Quaternion.LookRotation(tan.normalized, Vector3.up));
                if (f.anim != null && dt > 0f)
                {
                    float yawRate = f.angSpeed * speedMul * Mathf.Rad2Deg * -dir;   // positive = turning right
                    f.anim.Animate(dt, 1f, f.lengthM, 0.45f, yawRate, 0f, 0f, 1f, time + i * 1.7f);
                }
            }
        }

        /// Destroys the school and its models.
        public void Dispose()
        {
            if (this != null) Destroy(gameObject);
        }

        void OnDestroy()
        {
            for (int i = 0; i < owned.Count; i++) if (owned[i] != null) Destroy(owned[i]);
            owned.Clear();
            fish.Clear();
        }
    }
}
