using UnityEngine;

namespace Sealebrities
{
    /// Small shared particle effects of the fauna: splashes (dolphin leaps, whale breaches, flying fish), ink puffs
    /// (octopus mischief), sand puffs (parrotfish, digging crabs), scale puffs (a "caught" prey in a hunt – no gore)
    /// and bioluminescent sparkles (Glow Night). Two world-space particle systems (falling / floating), emitted with
    /// EmitParams – no allocations per emit.
    public static class FaunaFx
    {
        static ParticleSystem fall, drift;
        static Texture2D tex;
        static Material matAlpha, matAdd;
        static ParticleSystem glow;

        public static void Install(Transform parent)
        {
            if (fall != null) return;
            fall = Make(parent, "FaunaFxFall", 1.2f, 400, false);
            drift = Make(parent, "FaunaFxDrift", -0.02f, 600, false);
            glow = Make(parent, "FaunaFxGlow", -0.05f, 500, true);
        }

        static ParticleSystem Make(Transform parent, string name, float gravity, int max, bool additive)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.duration = 1f;
            main.playOnAwake = false;
            main.maxParticles = max;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f;
            main.startLifetime = 1f;
            main.startSize = 0.1f;
            main.gravityModifier = gravity;
            var em = ps.emission;
            em.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = gravity > 0.5f ? 0.05f : 0.2f;
            noise.frequency = 0.8f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0.75f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(grad);
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, gravity > 0.5f ? 0.6f : 1.8f)));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sharedMaterial = additive ? MatAdd() : MatAlpha();
            ps.Play();
            return ps;
        }

        static Texture2D Disc()
        {
            if (tex != null) return tex;
            tex = new Texture2D(32, 32, TextureFormat.RGBA32, false) { name = "SB_FaunaFxDisc", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[32 * 32];
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                {
                    float dx = (x + 0.5f) / 16f - 1f, dy = (y + 0.5f) / 16f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01((0.92f - d) / 0.12f);
                    px[y * 32 + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        static Material MatAlpha()
        {
            if (matAlpha != null) return matAlpha;
            matAlpha = new Material(MaterialLibrary.ParticlesUnlit) { name = "SB_FaunaFxAlpha" };
            matAlpha.SetTexture("_BaseMap", Disc());
            matAlpha.SetTexture("_MainTex", Disc());
            matAlpha.SetColor("_BaseColor", Color.white);
            MaterialLibrary.MakeTransparent(matAlpha, true);
            return matAlpha;
        }

        static Material MatAdd()
        {
            if (matAdd != null) return matAdd;
            matAdd = new Material(MaterialLibrary.ParticlesUnlit) { name = "SB_FaunaFxAdd" };
            matAdd.SetTexture("_BaseMap", Disc());
            matAdd.SetTexture("_MainTex", Disc());
            matAdd.SetColor("_BaseColor", Color.white);
            MaterialLibrary.MakeTransparent(matAdd, true);
            matAdd.SetFloat("_Blend", 2f);
            matAdd.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            return matAdd;
        }

        static void Emit(ParticleSystem ps, Vector3 c, float radius, int n, Color col, float size, float life, Vector3 vel, float velJitter)
        {
            if (ps == null) return;
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < n; i++)
            {
                ep.position = c + Random.insideUnitSphere * radius;
                ep.velocity = vel + Random.insideUnitSphere * velJitter;
                ep.startSize = size * Random.Range(0.6f, 1.3f);
                ep.startLifetime = life * Random.Range(0.7f, 1.3f);
                ep.startColor = col;
                ps.Emit(ep, 1);
            }
        }

        /// White droplets thrown up where an animal breaks the surface.
        public static void Splash(Vector3 at, float radius)
        {
            float r = Mathf.Clamp(radius, 0.15f, 4f);
            Emit(fall, at, r * 0.6f, Mathf.Clamp(Mathf.RoundToInt(r * 30f), 12, 90), new Color(0.93f, 0.97f, 1f, 0.9f), Mathf.Clamp(r * 0.12f, 0.05f, 0.4f), 1.1f, Vector3.up * (2.5f + r * 1.2f), 1.6f + r);
            FaunaBubbles.Poof(at - Vector3.up * 0.5f, r * 0.6f, Mathf.Clamp(Mathf.RoundToInt(r * 14f), 8, 50));
        }

        /// Octopus ink: a dark violet cloud that grows and fades.
        public static void Ink(Vector3 at, float radius) =>
            Emit(drift, at, radius * 0.4f, 26, new Color(0.12f, 0.08f, 0.16f, 0.85f), radius * 0.55f, 2.6f, Vector3.zero, 0.25f);

        /// Sand puff (parrotfish, a crab digging in, a find being dug out).
        public static void Sand(Vector3 at, float radius) =>
            Emit(drift, at, radius * 0.3f, 18, new Color(0.92f, 0.85f, 0.66f, 0.75f), radius * 0.45f, 1.4f, Vector3.up * 0.25f, 0.3f);

        /// A puff of silver scales (hunt scene "catch" – prey vanishes, no gore).
        public static void Scales(Vector3 at, float radius) =>
            Emit(drift, at, radius * 0.3f, 22, new Color(0.85f, 0.9f, 0.95f, 0.9f), radius * 0.12f, 1.6f, Vector3.zero, 0.6f);

        /// Bioluminescent sparkles (Glow Night, K.3).
        public static void Sparkle(Vector3 at, float radius, Color col, int n) =>
            Emit(glow, at, radius, n, col, 0.05f, 1.4f, Vector3.up * 0.05f, 0.15f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { fall = null; drift = null; glow = null; tex = null; matAlpha = null; matAdd = null; }
    }
}
