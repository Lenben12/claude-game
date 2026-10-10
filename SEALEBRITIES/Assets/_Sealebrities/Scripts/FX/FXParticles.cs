using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Camera-following particles: underwater marine snow, regulator exhale bubbles and the surface splash burst.
    sealed class FXParticles
    {
        const float SnowHalfHeight = 6f;
        const float SnowLifetimeAvg = 7.5f;

        readonly GameObject root;
        readonly Texture2D dotTex, bubbleTex;
        readonly Material snowMat, bubbleMat, dropMat;

        readonly ParticleSystem snow, bubbles, drops;
        readonly Transform snowT, dropsT;

        bool snowOn;
        float lastRate = -1f;
        Vector3 lastSnowPos;
        Color lastSnowColor = new Color(-1f, 0f, 0f, 0f);
        float bubbleAccum;
        int bubbleSide = 1;
        Color32 bubbleColor = new Color32(235, 245, 255, 220);
        ParticleSystem.EmitParams emit;

        public FXParticles(Transform parent)
        {
            root = new GameObject("FX_Particles");
            root.transform.SetParent(parent, false);

            dotTex = FXAssets.SoftDot(32);
            bubbleTex = FXAssets.BubbleRing(32);
            snowMat = FXAssets.ParticleMaterial("FX_MarineSnow", dotTex, false, Color.white);
            bubbleMat = FXAssets.ParticleMaterial("FX_Bubbles", bubbleTex, false, Color.white);
            dropMat = FXAssets.ParticleMaterial("FX_Droplets", dotTex, false, Color.white);

            snow = BuildSnow(out snowT);
            bubbles = BuildBubbles();
            drops = BuildDroplets(out dropsT);
        }

        public void Dispose()
        {
            if (root != null) Object.Destroy(root);
            if (snowMat != null) Object.Destroy(snowMat);
            if (bubbleMat != null) Object.Destroy(bubbleMat);
            if (dropMat != null) Object.Destroy(dropMat);
            if (dotTex != null) Object.Destroy(dotTex);
            if (bubbleTex != null) Object.Destroy(bubbleTex);
        }

        // ------------------------------------------------------------------ setup
        ParticleSystem Create(string name, Material mat, out ParticleSystemRenderer r)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            r = go.GetComponent<ParticleSystemRenderer>();
            if (r == null) r = go.AddComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sortMode = ParticleSystemSortMode.None;

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 5f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.startSpeed = 0f;
            main.gravityModifier = 0f;

            var em = ps.emission;
            em.rateOverTime = 0f;

            var shape = ps.shape;
            shape.enabled = false;
            return ps;
        }

        static Gradient FadeGradient(float inEnd, float outStart, float maxAlpha)
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(maxAlpha, inEnd), new GradientAlphaKey(maxAlpha, outStart), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        ParticleSystem BuildSnow(out Transform t)
        {
            var ps = Create("FX_MarineSnow", snowMat, out var r);
            t = ps.transform;
            r.maxParticleSize = 0.012f;
            r.minParticleSize = 0f;

            var main = ps.main;
            main.maxParticles = 3200;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.035f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.85f, 0.88f, 0.9f, 0.6f));
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, FXMath.TwoPi);

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(22f, SnowHalfHeight * 2f, 22f);

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.045f, 0.045f);
            vel.y = new ParticleSystem.MinMaxCurve(-0.05f, 0.015f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.045f, 0.045f);

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(FadeGradient(0.15f, 0.8f, 1f));

            ps.Play();
            return ps;
        }

        ParticleSystem BuildBubbles()
        {
            var ps = Create("FX_Bubbles", bubbleMat, out var r);
            r.maxParticleSize = 0.06f;

            var main = ps.main;
            main.maxParticles = 700;
            main.startLifetime = 4f;
            main.startSize = 0.03f;
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.92f, 0.96f, 1f, 0.85f));

            var em = ps.emission;
            em.enabled = false;

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.24f;
            noise.frequency = 1.3f;
            noise.scrollSpeed = 0.6f;
            noise.damping = true;
            noise.octaveCount = 1;
            noise.quality = ParticleSystemNoiseQuality.Medium;

            // bubbles expand as the pressure drops, then pop
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            var curve = new AnimationCurve(new Keyframe(0f, 0.75f), new Keyframe(0.9f, 1.25f), new Keyframe(1f, 0.15f));
            sol.size = new ParticleSystem.MinMaxCurve(1f, curve);

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(FadeGradient(0.04f, 0.92f, 1f));

            ps.Play();
            return ps;
        }

        ParticleSystem BuildDroplets(out Transform t)
        {
            var ps = Create("FX_SplashDroplets", dropMat, out var r);
            t = ps.transform;
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.035f;
            r.lengthScale = 1.4f;
            r.maxParticleSize = 0.05f;

            var main = ps.main;
            main.maxParticles = 300;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.6f, 4.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.06f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.9f, 0.97f, 1f, 0.85f));
            main.gravityModifier = 1.3f;

            var em = ps.emission;
            em.enabled = false;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 38f;
            shape.radius = 0.5f;
            shape.rotation = new Vector3(-90f, 0f, 0f);   // cone points up

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(FadeGradient(0.02f, 0.75f, 1f));

            ps.Play();
            return ps;
        }

        // ------------------------------------------------------------------ per frame
        public void Tick(FXFrame f, FXBreath breath)
        {
            if (f.camT == null) return;
            TickSnow(f);
            TickBreathBubbles(f, breath);
        }

        void TickSnow(FXFrame f)
        {
            bool on = f.underwater;
            float rate = on ? Mathf.Lerp(70f, 260f, FXMath.Smooth01(f.depth / 60f)) : 0f;
            if (Mathf.Abs(rate - lastRate) > 2f || (rate == 0f && lastRate != 0f))
            {
                var em = snow.emission;
                em.rateOverTime = rate;
                lastRate = rate;
            }

            // Keep the spawn box fully below the surface so no specks float in the air.
            Vector3 c = f.camPos;
            var sp = new Vector3(c.x, Mathf.Min(c.y, Waves.SeaLevel - 0.4f - SnowHalfHeight), c.z);
            bool jumped = (sp - lastSnowPos).sqrMagnitude > 15f * 15f;
            snowT.position = sp;
            lastSnowPos = sp;

            // Specks are only visible where light reaches them: sunlight fades with depth, the flashlight brings them back.
            float light = FXMath.Clamp01(f.day01 * Mathf.Exp(-f.depth / 40f) * (0.35f + 0.65f * f.sun01) + 0.55f * f.flash01 + 0.08f);
            float b = Mathf.Lerp(0.18f, 0.92f, light);
            var sc = new Color(b * 0.95f, b * 0.98f, b, Mathf.Lerp(0.35f, 0.65f, light));
            if (FXMath.Differs(sc, lastSnowColor, 0.02f))
            {
                var main = snow.main;
                main.startColor = new ParticleSystem.MinMaxGradient(sc);
                lastSnowColor = sc;
            }
            byte bb = (byte)(Mathf.Lerp(0.3f, 1f, light) * 255f);
            bubbleColor = new Color32((byte)(bb * 0.93f), (byte)(bb * 0.97f), bb, 215);

            // Fill the box instantly when diving in or after a teleport.
            if (on && (!snowOn || jumped)) snow.Emit((int)(rate * SnowLifetimeAvg * 0.85f));
            snowOn = on;
        }

        void TickBreathBubbles(FXFrame f, FXBreath breath)
        {
            if (!breath.Exhaling || !f.underwater || f.paused)
            {
                bubbleAccum = 0f;
                return;
            }
            float rate = 40f * (1f - 0.6f * breath.ExhaleT) * breath.Pitch;
            bubbleAccum += rate * f.dt;
            int guard = 0;
            while (bubbleAccum >= 1f && guard++ < 20)
            {
                bubbleAccum -= 1f;
                bubbleSide = -bubbleSide;
                Vector3 pos = f.camPos + f.camFwd * 0.13f - f.camUp * 0.17f + f.camRight * (0.07f * bubbleSide);
                float vy = Random.Range(0.55f, 1.15f);
                Vector3 vel = Vector3.up * vy
                              + f.camRight * (bubbleSide * Random.Range(0.12f, 0.35f))
                              + f.camFwd * Random.Range(0f, 0.12f);
                EmitBubble(pos, vel, Random.Range(0.008f, 0.04f), vy);
            }
        }

        void EmitBubble(Vector3 pos, Vector3 vel, float size, float riseSpeed)
        {
            float toSurface = Waves.HeightAt(pos.x, pos.z) - pos.y - 0.04f;
            if (toSurface <= 0.02f) return;
            emit.position = pos;
            emit.velocity = vel;
            emit.startSize = size;
            emit.startLifetime = Mathf.Clamp(toSurface / Mathf.Max(0.2f, riseSpeed), 0.1f, 12f);
            emit.startColor = bubbleColor;
            emit.applyShapeToPosition = false;
            bubbles.Emit(emit, 1);
        }

        /// Surface crossing: droplets spray at the waterline; diving in also wraps the camera in a bubble cloud.
        public void Splash(FXFrame f, bool under, float strength)
        {
            Vector3 c = f.camPos;
            float surface = Waves.HeightAt(c.x, c.z);
            Vector3 flatFwd = new Vector3(f.camFwd.x, 0f, f.camFwd.z);
            if (flatFwd.sqrMagnitude < 0.001f) flatFwd = Vector3.forward;
            flatFwd.Normalize();

            dropsT.position = new Vector3(c.x, surface, c.z) + flatFwd * 0.8f;
            float lightAmt = Mathf.Lerp(0.35f, 1f, f.day01);
            var dm = drops.main;
            dm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.9f * lightAmt, 0.97f * lightAmt, lightAmt, 0.85f));
            drops.Emit((int)((under ? 45 : 70) * strength));

            if (!under) return;
            int count = (int)(55 * strength);
            for (int i = 0; i < count; i++)
            {
                Vector3 off = new Vector3(Random.Range(-0.9f, 0.9f), Random.Range(-1.6f, -0.1f), Random.Range(-0.9f, 0.9f));
                Vector3 pos = c + flatFwd * 0.7f + off;
                float vy = Random.Range(0.5f, 1.7f);
                Vector3 vel = new Vector3(Random.Range(-0.5f, 0.5f), vy, Random.Range(-0.5f, 0.5f));
                EmitBubble(pos, vel, Random.Range(0.01f, 0.06f), vy);
            }
        }
    }
}
