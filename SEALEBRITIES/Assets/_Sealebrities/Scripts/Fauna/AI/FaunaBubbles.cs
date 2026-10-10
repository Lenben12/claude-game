using UnityEngine;

namespace Sealebrities
{
    /// One shared world-space particle system for bubble bursts ("poof" of a vanishing hallucination).
    public static class FaunaBubbles
    {
        static ParticleSystem ps;
        static Texture2D tex;
        static Material mat;

        public static void Install(Transform parent)
        {
            if (ps != null) return;
            var go = new GameObject("FaunaBubbles");
            go.transform.SetParent(parent, false);
            ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.duration = 1f;
            main.playOnAwake = false;
            main.maxParticles = 600;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f;
            main.startLifetime = 2f;
            main.startSize = 0.08f;
            main.gravityModifier = -0.12f;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            var em = ps.emission;
            em.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.25f;
            noise.frequency = 1.2f;
            noise.scrollSpeed = 0.4f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.85f, 0.08f), new GradientAlphaKey(0.7f, 0.75f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 1.3f)));

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sharedMaterial = BubbleMaterial();

            ps.Play();
        }

        static Material BubbleMaterial()
        {
            if (mat != null) return mat;
            // cartoon bubble (low-poly look, matches the FX bubbles): crisp white outline, faint body, small highlight
            tex = new Texture2D(32, 32, TextureFormat.RGBA32, false) { name = "SB_FaunaBubble", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[32 * 32];
            const float Aa = 1.1f / 16f, Outer = 0.92f, Inner = 0.74f;
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                {
                    float dx = (x + 0.5f) / 16f - 1f, dy = (y + 0.5f) / 16f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float inside = Mathf.Clamp01((Outer - d) / Aa + 0.5f);
                    float rim = inside * Mathf.Clamp01((d - Inner) / Aa + 0.5f);
                    float hx = dx + 0.3f, hy = dy - 0.3f;
                    float hl = Mathf.Clamp01((0.17f - Mathf.Sqrt(hx * hx + hy * hy)) / Aa + 0.5f);
                    float a = Mathf.Clamp01(Mathf.Max(Mathf.Max(rim * 0.95f, hl), inside * 0.2f));
                    px[y * 32 + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);

            mat = new Material(MaterialLibrary.ParticlesUnlit) { name = "SB_FaunaBubbles" };
            mat.SetTexture("_BaseMap", tex);
            mat.SetTexture("_MainTex", tex);
            mat.SetColor("_BaseColor", new Color(0.85f, 0.95f, 1f, 0.9f));
            MaterialLibrary.MakeTransparent(mat, true);
            return mat;
        }

        /// Emits a burst of rising bubbles around a point.
        public static void Poof(Vector3 center, float radius, int count)
        {
            if (ps == null) return;
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < count; i++)
            {
                ep.position = center + Random.insideUnitSphere * radius;
                ep.velocity = Random.insideUnitSphere * 0.7f + Vector3.up * Random.Range(0.4f, 1.1f);
                ep.startSize = Random.Range(0.03f, 0.14f);
                ep.startLifetime = Random.Range(1.2f, 2.6f);
                ps.Emit(ep, 1);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { ps = null; tex = null; mat = null; }
    }
}
