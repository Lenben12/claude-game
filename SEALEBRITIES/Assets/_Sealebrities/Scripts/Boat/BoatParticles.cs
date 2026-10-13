using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Particle system factory for the Boat module (engine smoke, bow spray, wake foam, splashes, puffs, rotor downwash).
    /// Sprites are crisp flat cartoon discs/blobs (BoatTextures) to match the low-poly look.
    public static class BoatParticles
    {
        public static ParticleSystem Create(string name, Transform parent, Vector3 localPos, Quaternion localRot, Material mat, bool worldSpace)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 5f;
            main.simulationSpace = worldSpace ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var em = ps.emission;
            em.rateOverTime = 0f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            return ps;
        }

        static ParticleSystem.MinMaxGradient FadeGradient(Color c0, Color c1, float a0, float aPeak, float peakAt)
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(c0, 0f), new GradientColorKey(c1, 1f) },
                new[] { new GradientAlphaKey(a0, 0f), new GradientAlphaKey(aPeak, peakAt), new GradientAlphaKey(0f, 1f) });
            return new ParticleSystem.MinMaxGradient(g);
        }

        static ParticleSystem.MinMaxCurve Grow(float from, float to)
        {
            return new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, from, 1f, to));
        }

        /// Grey-blue exhaust smoke rising from the outboard. Rate is driven by the boat.
        public static ParticleSystem EngineSmoke(Transform parent, Vector3 localPos)
        {
            var ps = Create("EngineSmoke", parent, localPos, Quaternion.Euler(-80f, 0f, 0f), BoatMats.Smoke, true);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 3.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            // light cartoon puffs (round flat sprites) rather than realistic dark exhaust
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.78f, 0.79f, 0.82f, 0.6f), new Color(0.62f, 0.63f, 0.67f, 0.7f));
            main.gravityModifier = -0.04f;
            main.maxParticles = 160;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 12f;
            sh.radius = 0.04f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = FadeGradient(Color.white, new Color(0.8f, 0.82f, 0.85f), 0f, 1f, 0.15f);
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = Grow(0.6f, 3.2f);
            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.35f;
            noise.frequency = 0.6f;
            ps.Play();
            return ps;
        }

        /// White droplets thrown sideways from the bow.
        public static ParticleSystem BowSpray(Transform parent, Vector3 localPos, float side)
        {
            var ps = Create("BowSpray", parent, localPos, Quaternion.LookRotation(new Vector3(side * 0.8f, 0.55f, 0.25f)), BoatMats.Spray, true);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 5.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.35f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.92f, 0.96f, 1f, 0.75f), new Color(0.8f, 0.9f, 0.95f, 0.5f));
            main.gravityModifier = 1f;
            main.maxParticles = 300;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 18f;
            sh.radius = 0.15f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = FadeGradient(Color.white, Color.white, 0.9f, 0.8f, 0.3f);
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = Grow(0.7f, 1.8f);
            ps.Play();
            return ps;
        }

        /// Flat foam patches left behind the stern (world space, horizontal billboards).
        public static ParticleSystem Wake(Transform parent)
        {
            var ps = Create("Wake", parent, Vector3.zero, Quaternion.identity, BoatMats.Foam, true);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 4.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.9f, 1.6f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, 0.7f), new Color(0.9f, 0.95f, 1f, 0.5f));
            main.maxParticles = 400;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = 0.5f;
            sh.scale = new Vector3(1f, 0.01f, 1f);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = FadeGradient(Color.white, Color.white, 0.6f, 0.75f, 0.15f);
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = Grow(0.8f, 3.2f);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            r.sortingFudge = 10f;
            ps.Play();
            return ps;
        }

        /// One-shot capable splash emitter (world space). Use Emit to splash at any position.
        public static ParticleSystem Splash(Transform parent)
        {
            var ps = Create("Splash", parent, Vector3.zero, Quaternion.Euler(-90f, 0f, 0f), BoatMats.Spray, true);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.12f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.95f, 0.98f, 1f, 0.9f));
            main.gravityModifier = 1f;
            main.maxParticles = 200;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 30f;
            sh.radius = 0.05f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = FadeGradient(Color.white, Color.white, 1f, 0.9f, 0.5f);
            ps.Play();
            return ps;
        }

        public static void EmitAt(ParticleSystem ps, Vector3 worldPos, int count, float sizeScale = 1f)
        {
            if (ps == null) return;
            var ep = new ParticleSystem.EmitParams { position = worldPos, applyShapeToPosition = true };
            if (sizeScale != 1f) ep.startSize = 0.09f * sizeScale;
            ps.Emit(ep, count);
        }

        /// Self-destroying burst (dust + wood splinters), e.g. when a crate is opened.
        public static void Puff(Vector3 pos, Color color, int count, float size = 0.5f)
        {
            var go = new GameObject("Puff");
            go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 3.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(color, color * 0.75f);
            main.gravityModifier = 0.25f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.Destroy;
            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = 0.35f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = FadeGradient(Color.white, Color.white, 1f, 0.8f, 0.2f);
            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = Grow(0.6f, 1.8f);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = BoatMats.Dust;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
        }

        /// Ring of dust / spray under a hovering helicopter. Position and rate are driven by the helicopter.
        public static ParticleSystem Downwash(Transform parent)
        {
            var ps = Create("Downwash", parent, Vector3.zero, Quaternion.identity, BoatMats.Dust, true);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(5f, 9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.8f, 1.8f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.72f, 0.66f, 0.55f, 0.4f));
            main.gravityModifier = -0.02f;
            main.maxParticles = 300;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Circle;
            sh.radius = 2f;
            sh.radiusThickness = 0.2f;
            sh.rotation = new Vector3(90f, 0f, 0f);
            sh.randomDirectionAmount = 0.15f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = FadeGradient(Color.white, Color.white, 0f, 1f, 0.2f);
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = Grow(0.7f, 2.5f);
            var lv = ps.limitVelocityOverLifetime;
            lv.enabled = true;
            lv.limit = 2f;
            lv.dampen = 0.08f;
            ps.Play();
            return ps;
        }

        public static void SetRate(ParticleSystem ps, float rate)
        {
            if (ps == null) return;
            var em = ps.emission;
            em.rateOverTime = rate;
        }
    }
}
