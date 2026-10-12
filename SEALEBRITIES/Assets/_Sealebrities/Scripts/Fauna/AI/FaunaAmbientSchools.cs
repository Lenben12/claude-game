using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Round 3 (GDD §20 Q15 "viele Fische, überall Leben, auch im offenen Meer"): purely visual bait-fish clouds that
    /// mill in the open water around the local camera – the sea never looks empty between the dive sites, at no AI or
    /// network cost. 2–4 clouds (more in the open / deep sea and around the volcano), 36–60 fish each, GPU-instanced
    /// static swim poses (like CreatureSwarm), placed 14–26 m away out of view, drifting slowly, parting around the
    /// diver (Q21: small fish dart away). Local to every machine, not filmable (scenery), only drawn under water.
    /// No per-frame allocations.
    public sealed class FaunaAmbientSchools : MonoBehaviour
    {
        public const int MaxClouds = 4;
        public const int MaxFish = 60;
        const int Poses = 4;
        const int Batch = 1023;

        sealed class Kind
        {
            public SpeciesDef s;
            public Mesh[] poses;
            public Material[] mats;
            public int subs;
            public bool failed;
        }

        sealed class Cloud
        {
            public bool live;
            public Kind kind;
            public Vector3 center, drift;
            public float radius, spin, phase, scale, age, life;
            public int count, seed;
        }

        readonly Cloud[] clouds = new Cloud[MaxClouds];
        readonly Matrix4x4[][] mats = new Matrix4x4[Poses][];
        readonly int[] counts = new int[Poses];
        Kind sardine, mackerel, lantern;
        RenderParams rp;
        bool rpInit;
        System.Random rng = new System.Random(2026);
        float zoneTimer;
        int wanted;

        /// Live clouds / fish (tests).
        public static int LiveClouds { get; private set; }
        public static int LiveFish { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { LiveClouds = 0; LiveFish = 0; }

        void Awake()
        {
            for (int i = 0; i < MaxClouds; i++) clouds[i] = new Cloud();
            for (int p = 0; p < Poses; p++) mats[p] = new Matrix4x4[Batch];
        }

        Kind Make(string id)
        {
            var s = SpeciesDatabase.Get(id);
            if (s == null) return null;
            var k = new Kind { s = s };
            try
            {
                var kit = CreatureFactory.Kit(s);
                k.poses = new Mesh[Poses];
                for (int p = 0; p < Poses; p++) k.poses[p] = CreatureMeshPose.Bake(kit, p / (float)Poses, "SB_Ambient_" + id + "_" + p);
                k.mats = kit.Materials;
                k.subs = Mathf.Min(kit.Materials.Length, k.poses[0].subMeshCount);
                for (int i = 0; i < k.mats.Length; i++) if (k.mats[i] != null && k.mats[i].renderQueue < 3000) k.mats[i].enableInstancing = true;
            }
            catch (System.Exception e)
            {
                k.failed = true;
                Debug.LogWarning("[Fauna] ambient school " + id + " unavailable: " + e.Message);
            }
            return k;
        }

        Kind KindFor(float depth, int seed)
        {
            if (depth > 38f)
            {
                if (lantern == null) lantern = Make("lanternfish");
                return lantern != null && !lantern.failed ? lantern : null;
            }
            if ((seed & 1) == 0)
            {
                if (sardine == null) sardine = Make("sardine");
                if (sardine != null && !sardine.failed) return sardine;
            }
            if (mackerel == null) mackerel = Make("mackerel");
            return mackerel != null && !mackerel.failed ? mackerel : null;
        }

        void LateUpdate()
        {
            LiveClouds = 0;
            LiveFish = 0;
            var gm = GameManager.I;
            var p = Refs.Player;
            if (gm == null || !gm.InGame || p == null || (p is Object o && o == null) || !FXVisibility.Underwater) { Clear(); return; }
            var cam = p.View;
            if (cam == null) return;
            Vector3 cp = cam.transform.position;
            Vector3 cf = cam.transform.forward;
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            float time = Time.time;

            zoneTimer -= dt;
            if (zoneTimer <= 0f)
            {
                zoneTimer = 1f;
                string z = FaunaZones.ZoneAt(cp);
                wanted = z == Zones.OpenSea || z == Zones.DeepSea || z == Zones.Volcano || z == Zones.BlueDrop ? 4
                       : z == Zones.Island ? 0 : 2;
            }

            for (int p2 = 0; p2 < Poses; p2++) counts[p2] = 0;
            Kind drawKind = null;
            for (int i = 0; i < MaxClouds; i++)
            {
                var c = clouds[i];
                if (!c.live)
                {
                    // final test: clouds only spawned beside / behind the view and drifted randomly – a still frame of the
                    // open sea showed none. Every other cloud now appears ahead in the fog (growing in) and they drift
                    // past the diver.
                    if (i < wanted) TrySpawn(c, cp, cf, (i & 1) == 0);
                    if (!c.live) continue;
                }
                c.age += dt;
                c.center += c.drift * dt;
                float dist = (c.center - cp).magnitude;
                bool inView = Vector3.Dot(c.center - cp, cf) > 0f;
                // recycle when far, old and not looked at, or more clouds than wanted
                if (dist > 42f || (c.age > c.life && (!inView || dist > 24f)) || i >= wanted + 1)
                {
                    c.live = false;
                    continue;
                }
                // keep it in the water column
                float g = WorldInfo.GroundHeight(c.center);
                float top = Waves.SeaLevel - 1.5f - c.radius * 0.5f;
                float bot = g + 1.5f + c.radius * 0.5f;
                if (bot < top) c.center.y = Mathf.Clamp(c.center.y, bot, top);
                else { c.live = false; continue; }
                Draw(c, cp, time, ref drawKind);
                LiveClouds++;
            }
            Flush(drawKind);
        }

        void Clear()
        {
            for (int i = 0; i < MaxClouds; i++) clouds[i].live = false;
        }

        void TrySpawn(Cloud c, Vector3 cp, Vector3 cf, bool ahead)
        {
            Vector3 fwd = new Vector3(cf.x, 0f, cf.z);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            for (int a = 0; a < 3; a++)
            {
                // ahead: far in the fog (it grows in over 2 s); otherwise beside / behind the view – both drift past the diver
                float ang = ahead ? Mathf.Lerp(-55f, 55f, (float)rng.NextDouble()) : Mathf.Lerp(70f, 290f, (float)rng.NextDouble());
                Vector3 dir = Quaternion.Euler(0f, ang, 0f) * fwd;
                float d = ahead ? Mathf.Lerp(18f, 25f, (float)rng.NextDouble()) : Mathf.Lerp(14f, 26f, (float)rng.NextDouble());
                Vector3 pos = cp + dir * d;
                pos.y = cp.y + Mathf.Lerp(-6f, 4f, (float)rng.NextDouble());
                float g = WorldInfo.GroundHeight(pos);
                float depth = Waves.SeaLevel - g;
                if (g > FaunaNav.LandGuardY - 2f || depth < 6f) continue;
                int seed = rng.Next();
                var k = KindFor(Waves.SeaLevel - pos.y, seed);
                if (k == null) return;
                c.kind = k;
                c.center = pos;
                c.radius = Mathf.Lerp(2.4f, 4.2f, (float)rng.NextDouble());
                c.count = Mathf.Clamp(36 + rng.Next(25), 12, MaxFish);
                // drift past the diver (towards the camera, ±20–50° off), so the cloud crosses the view
                Vector3 toCam = cp - pos;
                toCam.y = 0f;
                if (toCam.sqrMagnitude < 1e-4f) toCam = -dir;
                float side = ((seed & 4) == 0 ? 1f : -1f) * Mathf.Lerp(20f, 50f, (float)rng.NextDouble());
                c.drift = Quaternion.Euler(0f, side, 0f) * toCam.normalized * Mathf.Lerp(0.35f, 0.75f, (float)rng.NextDouble());
                c.spin = ((seed & 2) == 0 ? 1f : -1f) * Mathf.Lerp(0.25f, 0.45f, (float)rng.NextDouble());
                c.phase = (float)rng.NextDouble() * 10f;
                c.scale = (k.s.minCm + k.s.maxCm) * 0.005f;
                c.seed = seed;
                c.age = 0f;
                c.life = Mathf.Lerp(40f, 90f, (float)rng.NextDouble());
                c.live = true;
                return;
            }
        }

        void Draw(Cloud c, Vector3 cp, float time, ref Kind drawKind)
        {
            // one kind per frame keeps the batching simple; a cloud of another kind waits until it is the only one
            if (drawKind != null && drawKind != c.kind) { Flush(drawKind); drawKind = null; for (int p = 0; p < Poses; p++) counts[p] = 0; }
            drawKind = c.kind;
            float rot = c.phase + time * c.spin;
            float sc = c.scale * Mathf.Clamp01(c.age * 0.5f);   // grows in over 2 s (no pop in the fog)
            for (int i = 0; i < c.count; i++)
            {
                // a milling ring: angle around the centre + a little height / radius jitter per fish
                float h1 = Hash(c.seed, i, 1), h2 = Hash(c.seed, i, 2), h3 = Hash(c.seed, i, 3);
                float a = h1 * Mathf.PI * 2f + rot * (0.8f + 0.4f * h2);
                float r = c.radius * (0.45f + 0.55f * h3);
                Vector3 off = new Vector3(Mathf.Cos(a) * r, (h2 - 0.5f) * c.radius * 0.9f + Mathf.Sin(time * 0.8f + h1 * 9f) * 0.25f, Mathf.Sin(a) * r);
                Vector3 pos = c.center + off;
                // part around the diver (Q21)
                Vector3 away = pos - cp;
                float ad = away.magnitude;
                if (ad < 3.2f && ad > 1e-3f) pos += away * ((3.2f - ad) / ad);
                // facing: along the milling direction
                Vector3 tangent = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)) * Mathf.Sign(c.spin);
                Quaternion q = Quaternion.LookRotation(tangent, Vector3.up);
                float s = sc * (0.85f + 0.3f * h2);
                int pose = ((int)((time * 2.4f + h3 * 4f) * Poses)) & (Poses - 1);
                int k = counts[pose];
                if (k >= Batch) continue;
                mats[pose][k] = Matrix4x4.TRS(pos, q, new Vector3(s, s, s));
                counts[pose] = k + 1;
                LiveFish++;
            }
        }

        void Flush(Kind kind)
        {
            if (kind == null || kind.failed || kind.poses == null) return;
            if (!rpInit)
            {
                rpInit = true;
                rp = new RenderParams(kind.mats[0]) { layer = 0, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true, worldBounds = new Bounds(Vector3.zero, Vector3.one * 100000f) };
            }
            for (int pose = 0; pose < Poses; pose++)
            {
                int n = counts[pose];
                if (n == 0) continue;
                var mesh = kind.poses[pose];
                for (int sub = 0; sub < kind.subs; sub++)
                {
                    var mat = kind.mats[sub];
                    if (mat == null || !mat.enableInstancing) continue;
                    rp.material = mat;
                    Graphics.RenderMeshInstanced(rp, mesh, sub, mats[pose], n);
                }
                counts[pose] = 0;
            }
        }

        static float Hash(int a, int b, int c)
        {
            unchecked
            {
                uint h = (uint)a * 0x9E3779B1u ^ (uint)b * 0x85EBCA77u ^ (uint)c * 0xC2B2AE3Du;
                h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
                return (h & 0xFFFF) / 65535f;
            }
        }

        void OnDestroy()
        {
            Kill(sardine); Kill(mackerel); Kill(lantern);
        }

        static void Kill(Kind k)
        {
            if (k == null || k.poses == null) return;
            for (int i = 0; i < k.poses.Length; i++) if (k.poses[i] != null) Destroy(k.poses[i]);
        }
    }
}
