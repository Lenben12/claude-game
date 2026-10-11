using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Small particle systems owned by props (campfire, vent bubbles and smoke, bioluminescent drift).
    public static class PropFX
    {
        static readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Mats.Clear(); }

        static Material ParticleMat(string key, Texture2D tex, Color color, bool additive)
        {
            if (Mats.TryGetValue(key, out var m) && m != null) return m;
            Shader sh = MaterialLibrary.ParticlesUnlit;
            if (sh == null) sh = MaterialLibrary.UnlitShader;
            m = new Material(sh) { name = "PropFX_" + key };
            m.SetTexture("_BaseMap", tex);
            m.SetTexture("_MainTex", tex);
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            Mats[key] = m;
            return m;
        }

        static ParticleSystem Create(Transform parent, string name, Vector3 pos, Quaternion rot, Material mat, int maxParticles)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, rot);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.duration = 5f;
            main.maxParticles = maxParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.playOnAwake = true;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        static Gradient Grad(Color c0, float a0, Color c1, float a1, Color c2, float a2)
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(c0, 0f), new GradientColorKey(c1, 0.5f), new GradientColorKey(c2, 1f) },
                new[] { new GradientAlphaKey(a0, 0f), new GradientAlphaKey(a1, 0.5f), new GradientAlphaKey(a2, 1f) });
            return g;
        }

        public static ParticleSystem Fire(Transform parent, Vector3 pos)
        {
            var ps = Create(parent, "Fire", pos, Quaternion.Euler(-90f, 0f, 0f), ParticleMat("fire", PropTex.SoftDot, Color.white, true), 80);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.05f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.45f, 1.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.28f, 0.58f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.82f, 0.45f, 0.9f), new Color(1f, 0.55f, 0.18f, 0.8f));
            main.gravityModifier = -0.08f;
            var em = ps.emission;
            em.rateOverTime = 30f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 10f;
            sh.radius = 0.22f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(Grad(new Color(1f, 0.9f, 0.6f), 0.95f, new Color(1f, 0.42f, 0.1f), 0.6f, new Color(0.35f, 0.08f, 0.03f), 0f));
            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.25f));
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.35f;
            noise.frequency = 1.4f;
            return ps;
        }

        public static ParticleSystem Bubbles(Transform parent, Vector3 pos, float rate, float radius)
        {
            var ps = Create(parent, "Bubbles", pos, Quaternion.Euler(-90f, 0f, 0f), ParticleMat("bubble", PropTex.Bubble, new Color(0.85f, 0.95f, 1f, 0.75f), false), 160);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(9f, 15f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.7f, 1.3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.15f);
            main.gravityModifier = -0.03f;
            var em = ps.emission;
            em.rateOverTime = rate;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 7f;
            sh.radius = radius;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(Grad(Color.white, 0f, Color.white, 1f, Color.white, 0f));
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.25f;
            noise.frequency = 0.6f;
            ps.Play(true);
            return ps;
        }

        /// Dark mineral plume of a black smoker.
        public static ParticleSystem Smoke(Transform parent, Vector3 pos)
        {
            var ps = Create(parent, "Plume", pos, Quaternion.Euler(-90f, 0f, 0f), ParticleMat("smoke", PropTex.SoftDot, new Color(0.08f, 0.07f, 0.065f, 1f), false), 60);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.7f, 1.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.0f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, 0.55f));
            var em = ps.emission;
            em.rateOverTime = 6f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 6f;
            sh.radius = 0.15f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(Grad(Color.white, 0.6f, Color.white, 0.35f, Color.white, 0f));
            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 3.2f));
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.3f;
            noise.frequency = 0.3f;
            ps.Play(true);
            return ps;
        }

        /// Light grey grill smoke (E.1) – stopped until a fish sizzles.
        public static ParticleSystem GrillSmoke(Transform parent, Vector3 pos)
        {
            var ps = Create(parent, "GrillSmoke", pos, Quaternion.Euler(-90f, 0f, 0f), ParticleMat("grill_smoke", PropTex.SoftDot, new Color(0.92f, 0.9f, 0.86f, 1f), false), 50);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.2f, 3.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, 0.5f));
            main.playOnAwake = false;
            var em = ps.emission;
            em.rateOverTime = 9f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 12f;
            sh.radius = 0.22f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(Grad(Color.white, 0.55f, Color.white, 0.3f, Color.white, 0f));
            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.6f));
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.25f;
            noise.frequency = 0.4f;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return ps;
        }

        /// Hissing air at an old air station (white puffs, stopped until it refills).
        public static ParticleSystem Hiss(Transform parent, Vector3 pos)
        {
            var ps = Create(parent, "Hiss", pos, Quaternion.Euler(0f, 0f, 0f), ParticleMat("hiss", PropTex.SoftDot, new Color(1f, 1f, 1f, 0.6f), false), 30);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.18f);
            main.playOnAwake = false;
            var em = ps.emission;
            em.rateOverTime = 30f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 20f;
            sh.radius = 0.03f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(Grad(Color.white, 0.6f, Color.white, 0.3f, Color.white, 0f));
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return ps;
        }

        /// Orange smoke column (wrong-place chopper delivery beacon, L.12 – the Boat module may reuse it).
        public static ParticleSystem SmokeBeacon(Transform parent, Vector3 pos)
        {
            var ps = Create(parent, "SmokeBeacon", pos, Quaternion.Euler(-90f, 0f, 0f), ParticleMat("beacon_smoke", PropTex.SoftDot, new Color(1f, 0.55f, 0.18f, 1f), false), 140);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.4f, 3.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, 0.7f));
            var em = ps.emission;
            em.rateOverTime = 14f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 6f;
            sh.radius = 0.2f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(Grad(Color.white, 0.75f, Color.white, 0.45f, Color.white, 0f));
            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 3.4f));
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.6f;
            noise.frequency = 0.15f;
            ps.Play(true);
            return ps;
        }

        /// Subtle golden glints around a collectible (a few particles per second, started/stopped by distance).
        public static ParticleSystem Sparkle(Transform parent, Vector3 pos, float radius)
        {
            var ps = Create(parent, "Sparkle", pos, Quaternion.identity, ParticleMat("sparkle", PropTex.Star, new Color(1f, 0.92f, 0.62f, 0.9f), true), 12);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.12f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.16f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI);
            main.gravityModifier = -0.02f;
            main.playOnAwake = false;
            var em = ps.emission;
            em.rateOverTime = 3.5f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = radius;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(Grad(Color.white, 0f, Color.white, 1f, Color.white, 0f));
            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.2f), new Keyframe(0.4f, 1f), new Keyframe(1f, 0f)));
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return ps;
        }

        /// White spray at the foot of a waterfall.
        public static ParticleSystem Spray(Transform parent, Vector3 pos, float width)
        {
            var ps = Create(parent, "Spray", pos, Quaternion.Euler(-90f, 0f, 0f), ParticleMat("spray", PropTex.SoftDot, new Color(0.95f, 0.98f, 1f, 0.55f), false), 70);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.9f);
            main.gravityModifier = 0.12f;
            var em = ps.emission;
            em.rateOverTime = 26f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = new Vector3(width, 0.3f, 0.3f);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(Grad(Color.white, 0.7f, Color.white, 0.4f, Color.white, 0f));
            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.6f));
            ps.Play(true);
            return ps;
        }

        /// Slowly drifting glowing specks (deep sea bioluminescence).
        public static ParticleSystem Specks(Transform parent, Vector3 center, float radius, int count, Color color)
        {
            var ps = Create(parent, "BioSpecks", center, Quaternion.identity, ParticleMat("speck_" + ColorUtility.ToHtmlStringRGB(color), PropTex.SoftDot, color, true), count);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(12f, 22f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.01f, 0.08f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
            main.prewarm = true;
            var em = ps.emission;
            em.rateOverTime = count / 17f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = radius;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(Grad(Color.white, 0f, Color.white, 1f, Color.white, 0f));
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.12f;
            noise.frequency = 0.15f;
            ps.Play(true);
            return ps;
        }
    }
}
