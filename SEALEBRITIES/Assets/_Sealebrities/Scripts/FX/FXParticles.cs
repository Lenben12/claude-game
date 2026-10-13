using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Camera-following particles (art bible §2.3): marine snow (~0.5 flakes/m³ of flat 2–4 cm specks, white α 0.35),
    /// faceted regulator bubbles (white α 0.6), the surface splash, colourful bioluminescent sparks in the deep
    /// (> 60 m, GDD 15.31; a few also in the shallows at night) and exhale bubbles of co-op divers.
    /// Counts follow the graphics tier. World-space splashes/bubbles for other modules via FXEffects.
    sealed class FXParticles
    {
        const float SnowHalfHeight = 5f;
        const float SnowBoxXZ = 16f;
        const float SnowLifetimeAvg = 7.5f;
        const float SnowDensity = 0.5f;         // flakes per m³ on High
        const float SparkBoxXZ = 13f, SparkHalfHeight = 4.5f;
        const int SparkMax = 260;

        static readonly Color SparkCyan = FXMath.Hex(0x46E0FF);
        static readonly Color SparkGreen = FXMath.Hex(0x9BFF6E);
        static readonly Color SparkPink = FXMath.Hex(0xFF6EC7);

        readonly GameObject root;
        readonly Texture2D dotTex, bubbleTex, sparkTex;
        readonly Material snowMat, bubbleMat, dropMat, sparkMat;

        readonly ParticleSystem snow, bubbles, drops, sparks, streams;
        readonly Transform snowT, dropsT, sparksT;
        readonly Material streamMat;
        float streamAccum, glowAccum;
        Vector3 lastCamPos;
        bool lastCamValid;

        float tierScale = 1f;
        float snowRateFull;
        bool snowOn, sparksOn;
        float lastRate = -1f, lastSparkRate = -1f;
        Vector3 lastSnowPos;
        Color lastSnowColor = new Color(-1f, 0f, 0f, 0f);
        float bubbleAccum;
        int bubbleSide = 1;
        Color32 bubbleColor = new Color32(235, 245, 255, 155);
        ParticleSystem.EmitParams emit;

        // co-op divers: cached head transform per remote avatar root (no per-frame lookups after the first)
        readonly Dictionary<Transform, Transform> heads = new Dictionary<Transform, Transform>();
        float lastRemoteTime;

        public FXParticles(Transform parent)
        {
            root = new GameObject("FX_Particles");
            root.transform.SetParent(parent, false);

            dotTex = FXAssets.CleanDot(32);
            bubbleTex = FXAssets.FacetBubble(64);
            sparkTex = FXAssets.Spark(32);
            snowMat = FXAssets.ParticleMaterial("FX_MarineSnow", dotTex, false, Color.white);
            bubbleMat = FXAssets.ParticleMaterial("FX_Bubbles", bubbleTex, false, Color.white);
            dropMat = FXAssets.ParticleMaterial("FX_Droplets", dotTex, false, Color.white);
            // HDR tint: the sparks feed the bloom a little
            sparkMat = FXAssets.ParticleMaterial("FX_Biolum", sparkTex, true, new Color(1.8f, 1.8f, 1.8f, 1f));

            streamMat = FXAssets.ParticleMaterial("FX_CurrentStreams", dotTex, false, Color.white);

            snow = BuildSnow(out snowT);
            bubbles = BuildBubbles();
            drops = BuildDroplets(out dropsT);
            sparks = BuildSparks(out sparksT);
            streams = BuildStreams();
            SetTier(GraphicsQuality.High);
        }

        /// K.3 current streaks: thin stretched specks drifting along the registered current lanes (FXCurrents).
        ParticleSystem BuildStreams()
        {
            var ps = Create("FX_CurrentStreams", streamMat, out var r);
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.18f;
            r.lengthScale = 2.2f;
            r.maxParticleSize = 0.02f;
            var main = ps.main;
            main.maxParticles = 360;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.03f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.92f, 0.97f, 1f, 0.32f));
            var em = ps.emission;
            em.enabled = false;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(FadeGradient(0.15f, 0.7f, 1f));
            ps.Play();
            return ps;
        }

        public void Dispose()
        {
            if (root != null) Object.Destroy(root);
            if (snowMat != null) Object.Destroy(snowMat);
            if (bubbleMat != null) Object.Destroy(bubbleMat);
            if (dropMat != null) Object.Destroy(dropMat);
            if (sparkMat != null) Object.Destroy(sparkMat);
            if (streamMat != null) Object.Destroy(streamMat);
            if (dotTex != null) Object.Destroy(dotTex);
            if (bubbleTex != null) Object.Destroy(bubbleTex);
            if (sparkTex != null) Object.Destroy(sparkTex);
        }

        public void Subscribe()
        {
            FXEffects.SplashHandler = SplashAt;
            FXEffects.BubblesHandler = BubblesAt;
        }

        public void Unsubscribe()
        {
            if (FXEffects.SplashHandler == (System.Action<Vector3, float>)SplashAt) FXEffects.SplashHandler = null;
            if (FXEffects.BubblesHandler == (System.Action<Vector3, int>)BubblesAt) FXEffects.BubblesHandler = null;
        }

        public void SetTier(GraphicsQuality q)
        {
            tierScale = q == GraphicsQuality.High ? 1f : q == GraphicsQuality.Medium ? 0.6f : 0.35f;
            float volume = SnowBoxXZ * SnowBoxXZ * SnowHalfHeight * 2f;
            int snowCount = Mathf.Max(60, Mathf.RoundToInt(volume * SnowDensity * tierScale));
            snowRateFull = snowCount / SnowLifetimeAvg;
            var sm = snow.main;
            sm.maxParticles = snowCount + snowCount / 6;
            var pm = sparks.main;
            pm.maxParticles = Mathf.Max(30, Mathf.RoundToInt(SparkMax * tierScale));
            lastRate = -1f;
            lastSparkRate = -1f;
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
            r.maxParticleSize = 0.01f;
            r.minParticleSize = 0f;

            var main = ps.main;
            main.maxParticles = 1500;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.04f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.92f, 0.97f, 1f, 0.35f));
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, FXMath.TwoPi);

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(SnowBoxXZ, SnowHalfHeight * 2f, SnowBoxXZ);

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
            r.maxParticleSize = 0.08f;

            var main = ps.main;
            main.maxParticles = 500;
            main.startLifetime = 4f;
            main.startSize = 0.03f;
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.95f, 0.98f, 1f, 0.6f));
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);

            var em = ps.emission;
            em.enabled = false;

            // calm, readable wobble (cartoon bubbles rise in gentle S-curves)
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.16f;
            noise.frequency = 0.9f;
            noise.scrollSpeed = 0.45f;
            noise.damping = true;
            noise.octaveCount = 1;
            noise.quality = ParticleSystemNoiseQuality.Low;

            // bubbles grow a little on the way up, then pop
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            var curve = new AnimationCurve(new Keyframe(0f, 0.8f), new Keyframe(0.92f, 1.2f), new Keyframe(1f, 0.2f));
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
            main.maxParticles = 400;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.75f);   // Q9: ~0.6 s
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.6f, 4.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.06f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.93f, 0.96f, 0.97f, 0.9f));
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

        ParticleSystem BuildSparks(out Transform t)
        {
            var ps = Create("FX_Bioluminescence", sparkMat, out var r);
            t = ps.transform;
            r.maxParticleSize = 0.012f;

            var main = ps.main;
            main.maxParticles = SparkMax;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 5.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.065f);
            // three fixed colours, picked at random per spark
            var g = new Gradient { mode = GradientMode.Fixed };
            g.SetKeys(
                new[] { new GradientColorKey(SparkCyan, 0.34f), new GradientColorKey(SparkGreen, 0.67f), new GradientColorKey(SparkPink, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            main.startColor = new ParticleSystem.MinMaxGradient(g) { mode = ParticleSystemGradientMode.RandomColor };

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(SparkBoxXZ, SparkHalfHeight * 2f, SparkBoxXZ);

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.06f, 0.06f);
            vel.y = new ParticleSystem.MinMaxCurve(-0.03f, 0.05f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.06f, 0.06f);

            // twinkle: two soft flashes over the lifetime
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            var curve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.18f, 1f), new Keyframe(0.42f, 0.45f),
                                           new Keyframe(0.62f, 1f), new Keyframe(1f, 0f));
            sol.size = new ParticleSystem.MinMaxCurve(1f, curve);

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(FadeGradient(0.12f, 0.75f, 1f));

            ps.Play();
            return ps;
        }

        // ------------------------------------------------------------------ per frame
        public void Tick(FXFrame f, FXBreath breath)
        {
            if (f.camT == null) return;
            TickSnow(f);
            TickSparks(f);
            TickBreathBubbles(f, breath);
            TickRemoteDivers(f);
            TickCurrents(f);
            TickNightGlow(f);
            lastCamPos = f.camPos;
            lastCamValid = true;
        }

        /// K.3: streaks drift along every current lane near the camera (the lanes come from FXCurrents).
        void TickCurrents(FXFrame f)
        {
            var lanes = FXCurrents.Lanes;
            if (!f.underwater || f.paused || lanes.Count == 0 || streams == null) { streamAccum = 0f; return; }
            Vector3 c = f.camPos;
            for (int li = 0; li < lanes.Count; li++)
            {
                var lane = lanes[li];
                Vector3 ab = lane.end - lane.start;
                float len = ab.magnitude;
                if (len < 1f) continue;
                Vector3 dir = ab / len;
                float t = Mathf.Clamp(Vector3.Dot(c - lane.start, dir), 0f, len);
                Vector3 closest = lane.start + dir * t;
                float d = Vector3.Distance(c, closest);
                if (d > lane.radius + 18f) continue;
                streamAccum += 40f * tierScale * f.dt * Mathf.Lerp(0.6f, 1.4f, Mathf.InverseLerp(0.3f, 1.4f, lane.speed));
                int guard = 0;
                // a basis across the lane for the offsets
                Vector3 side = Vector3.Cross(dir, Vector3.up);
                if (side.sqrMagnitude < 0.01f) side = Vector3.Cross(dir, Vector3.right);
                side.Normalize();
                Vector3 up = Vector3.Cross(side, dir);
                var ep = new ParticleSystem.EmitParams { applyShapeToPosition = false };
                while (streamAccum >= 1f && guard++ < 12)
                {
                    streamAccum -= 1f;
                    float tt = Mathf.Clamp(t + Random.Range(-16f, 10f), 0f, len);
                    Vector2 o = Random.insideUnitCircle * lane.radius * 0.9f;
                    Vector3 p = lane.start + dir * tt + side * o.x + up * o.y;
                    if ((p - c).sqrMagnitude > 20f * 20f) continue;
                    if (p.y > Waves.HeightAt(p.x, p.z) - 0.3f) continue;
                    ep.position = p;
                    ep.velocity = dir * lane.speed * Random.Range(0.8f, 1.25f) + Random.insideUnitSphere * 0.05f;
                    ep.startSize = Random.Range(0.012f, 0.03f);
                    ep.startLifetime = Random.Range(1.6f, 2.8f);
                    streams.Emit(ep, 1);
                }
            }
        }

        /// K.3 "Leuchtnacht": at night in the lagoon, on the house reef and at the Fog Isle every movement of the diver
        /// stirs up blue-green sparkles (much stronger while the night_glow event runs nearby).
        void TickNightGlow(FXFrame f)
        {
            if (!f.underwater || f.paused || !lastCamValid || f.dt <= 0f) { glowAccum = 0f; return; }
            Vector3 c = f.camPos;
            float amount = 0f;
            if (f.night01 > 0.3f && f.depth < 16f)
            {
                float r = Mathf.Sqrt(c.x * c.x + c.z * c.z);
                var fog = WorldLayout.FogIsland;
                float dFog = Mathf.Sqrt((c.x - fog.x) * (c.x - fog.x) + (c.z - fog.z) * (c.z - fog.z));
                bool zone = r < WorldLayout.IslandRadius + 190f || dFog < 160f;
                if (zone) amount = 0.35f * Mathf.InverseLerp(0.3f, 0.8f, f.night01);
            }
            if (FaunaEventInfo.IsActive && FaunaEventInfo.ActiveId == "night_glow" && (c - FaunaEventInfo.Center).sqrMagnitude < 90f * 90f)
                amount = 1f;
            if (amount <= 0f) { glowAccum = 0f; return; }

            float speed = Mathf.Min(3f, (c - lastCamPos).magnitude / f.dt);
            glowAccum += (6f + 34f * speed) * amount * tierScale * f.dt;
            var ep = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            int guard = 0;
            while (glowAccum >= 1f && guard++ < 16)
            {
                glowAccum -= 1f;
                // around the hands and around the kicking fins
                bool fins = Random.value < 0.45f;
                Vector3 p = fins
                    ? c - f.camUp * Random.Range(0.9f, 1.5f) - f.camFwd * Random.Range(0.4f, 1.2f) + Random.insideUnitSphere * 0.5f
                    : c + f.camFwd * Random.Range(0.6f, 2.2f) + Random.insideUnitSphere * 0.7f;
                if (p.y > Waves.HeightAt(p.x, p.z) - 0.2f) continue;
                ep.position = p;
                ep.velocity = Random.insideUnitSphere * 0.12f;
                ep.startSize = Random.Range(0.025f, 0.06f);
                ep.startLifetime = Random.Range(0.8f, 1.7f);
                ep.startColor = Random.value < 0.6f ? (Color32)SparkCyan : (Color32)SparkGreen;
                sparks.Emit(ep, 1);
            }
        }

        void TickSnow(FXFrame f)
        {
            bool on = f.underwater;
            float rate = on ? snowRateFull * Mathf.Lerp(0.8f, 1f, FXMath.Smooth01(f.depth / 40f)) : 0f;
            if (Mathf.Abs(rate - lastRate) > 1f || (rate == 0f && lastRate != 0f))
            {
                var em = snow.emission;
                em.rateOverTime = rate;
                lastRate = rate;
            }

            // Keep the spawn box fully below the surface so no specks float in the air.
            Vector3 c = f.camPos;
            var sp = new Vector3(c.x, Mathf.Min(c.y, Waves.SeaLevel - 0.4f - SnowHalfHeight), c.z);
            bool jumped = (sp - lastSnowPos).sqrMagnitude > 12f * 12f;
            snowT.position = sp;
            lastSnowPos = sp;

            // Specks are only visible where light reaches them: sunlight fades with depth, the flashlight brings them back.
            float light = FXMath.Clamp01(f.day01 * Mathf.Exp(-f.depth / 40f) * (0.35f + 0.65f * f.sun01) + 0.55f * f.flash01 + 0.08f);
            float b = Mathf.Lerp(0.25f, 0.97f, light);
            var sc = new Color(b * 0.95f, b * 0.98f, b, Mathf.Lerp(0.22f, 0.35f, light));
            if (FXMath.Differs(sc, lastSnowColor, 0.02f))
            {
                var main = snow.main;
                main.startColor = new ParticleSystem.MinMaxGradient(sc);
                lastSnowColor = sc;
            }
            byte bb = (byte)(Mathf.Lerp(0.35f, 1f, light) * 255f);
            bubbleColor = new Color32((byte)(bb * 0.96f), (byte)(bb * 0.98f), bb, 155);   // white α 0.6

            // Fill the box instantly when diving in or after a teleport.
            if (on && (!snowOn || jumped)) snow.Emit(Mathf.Min((int)(rate * SnowLifetimeAvg * 0.85f), 1200));
            snowOn = on;
        }

        void TickSparks(FXFrame f)
        {
            // deep-sea glow from ~45 m, full at 70 m; at night a few sparks also in the shallows
            float amount = 0f;
            if (f.underwater)
                amount = Mathf.Max(FXMath.SmoothRange(45f, 70f, f.depth), 0.25f * f.night01 * FXMath.SmoothRange(2f, 8f, f.depth));
            float rate = amount * SparkMax * tierScale / 4f;
            if (Mathf.Abs(rate - lastSparkRate) > 0.5f || (rate == 0f && lastSparkRate != 0f))
            {
                var em = sparks.emission;
                em.rateOverTime = rate;
                lastSparkRate = rate;
            }
            if (rate <= 0f) { sparksOn = false; return; }
            Vector3 c = f.camPos;
            sparksT.position = new Vector3(c.x, Mathf.Min(c.y, Waves.SeaLevel - 0.5f - SparkHalfHeight), c.z);
            if (!sparksOn) sparks.Emit(Mathf.Min((int)(rate * 3f), 200));
            sparksOn = true;
        }

        void TickBreathBubbles(FXFrame f, FXBreath breath)
        {
            if (!breath.Exhaling || !f.underwater || f.paused)
            {
                bubbleAccum = 0f;
                return;
            }
            // fewer, chunkier bubbles read better in the clean low-poly look than a dense fizz
            float rate = 24f * (1f - 0.6f * breath.ExhaleT) * breath.Pitch;
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
                EmitBubble(pos, vel, Random.Range(0.012f, 0.045f), vy);
            }
        }

        /// Co-op: every remote diver below the surface exhales a bubble burst on a regular rhythm (stateless:
        /// the phase comes from the avatar's instance id, so no per-diver bookkeeping is needed).
        void TickRemoteDivers(FXFrame f)
        {
            var all = Divers.All;
            float now = f.time;
            float prev = lastRemoteTime;
            lastRemoteTime = now;
            if (all.Count <= 1 || f.paused || now <= prev) return;

            Transform local = null;
            var p = Refs.Player;
            if (p != null && !(p is Object po && po == null)) local = p.Root;

            const float Period = 3.6f, ExStart = 1.4f, ExLen = 1.5f, Rate = 14f;
            for (int i = 0; i < all.Count; i++)
            {
                Transform t = all[i];
                if (t == null || t == local) continue;
                Vector3 rp = t.position;
                if ((rp - f.camPos).sqrMagnitude > 28f * 28f) continue;
                Vector3 head = HeadOf(t, rp);
                if (head.y > Waves.HeightAt(head.x, head.z) - 0.4f) continue;

                float off = (t.GetEntityId().GetHashCode() & 1023) * 0.0137f;
                float a = (prev + off) % Period, b = (now + off) % Period;
                if (b < a) a -= Period;   // wrapped this frame
                float s0 = Mathf.Clamp(a, ExStart, ExStart + ExLen), s1 = Mathf.Clamp(b, ExStart, ExStart + ExLen);
                int n = Mathf.FloorToInt((s1 - ExStart) * Rate) - Mathf.FloorToInt((s0 - ExStart) * Rate);
                for (int k = 0; k < n && k < 6; k++)
                {
                    float vy = Random.Range(0.55f, 1.1f);
                    var vel = new Vector3(Random.Range(-0.25f, 0.25f), vy, Random.Range(-0.25f, 0.25f));
                    EmitBubble(head + new Vector3(Random.Range(-0.06f, 0.06f), 0f, Random.Range(-0.06f, 0.06f)), vel, Random.Range(0.015f, 0.05f), vy);
                }
            }
        }

        Vector3 HeadOf(Transform avatar, Vector3 rootPos)
        {
            if (!heads.TryGetValue(avatar, out Transform head))
            {
                head = null;
                var puppet = avatar.GetComponentInChildren<IDiverPuppet>();
                if (puppet != null && !(puppet is Object o && o == null)) head = puppet.Head;
                if (heads.Count > 32) heads.Clear();
                heads[avatar] = head;
            }
            if (head != null) return head.position + head.forward * 0.12f;
            return rootPos + avatar.up * 1.55f;
        }

        void EmitBubble(Vector3 pos, Vector3 vel, float size, float riseSpeed, float maxLife = 12f)
        {
            float toSurface = Waves.HeightAt(pos.x, pos.z) - pos.y - 0.04f;
            if (toSurface <= 0.02f) return;
            emit.position = pos;
            emit.velocity = vel;
            emit.startSize = size;
            emit.startLifetime = Mathf.Clamp(toSurface / Mathf.Max(0.2f, riseSpeed), 0.1f, maxLife);
            emit.startColor = bubbleColor;
            emit.applyShapeToPosition = false;
            bubbles.Emit(emit, 1);
        }

        Color DropColor(FXFrame f)
        {
            float lightAmt = Mathf.Lerp(0.35f, 1f, f.day01);
            return new Color(0.93f * lightAmt, 0.96f * lightAmt, 0.97f * lightAmt, 0.9f);
        }

        /// Surface crossing of the lens (Q9). Diving in: a burst of 40 faceted bubbles rushing up past the mask for
        /// ~1.2 s. Coming up: water drips off the mask (a few droplets).
        public void Splash(FXFrame f, bool under, float strength)
        {
            Vector3 c = f.camPos;
            float surface = Waves.HeightAt(c.x, c.z);
            Vector3 flatFwd = new Vector3(f.camFwd.x, 0f, f.camFwd.z);
            if (flatFwd.sqrMagnitude < 0.001f) flatFwd = Vector3.forward;
            flatFwd.Normalize();

            if (!under)
            {
                dropsT.position = new Vector3(c.x, surface, c.z) + flatFwd * 0.8f;
                var dm = drops.main;
                dm.startColor = new ParticleSystem.MinMaxGradient(DropColor(f));
                drops.Emit((int)(20 * strength * Mathf.Lerp(0.6f, 1f, tierScale)));
                return;
            }
            int count = (int)(40 * strength);
            for (int i = 0; i < count; i++)
            {
                Vector3 off = new Vector3(Random.Range(-0.8f, 0.8f), Random.Range(-1.5f, -0.1f), Random.Range(-0.8f, 0.8f));
                Vector3 pos = c + flatFwd * 0.7f + off;
                float vy = Random.Range(0.7f, 1.9f);
                Vector3 vel = new Vector3(Random.Range(-0.45f, 0.45f), vy, Random.Range(-0.45f, 0.45f));
                EmitBubble(pos, vel, Random.Range(0.015f, 0.07f), vy, Random.Range(1.0f, 1.4f));
            }
        }

        /// The body hits the water from a jump or a dive (Q9): 32 droplets thrown up in a crown for ~0.6 s plus a few
        /// bubbles under the entry point.
        public void BodySplash(FXFrame f, Vector3 pos, float strength)
        {
            if (drops == null) return;
            float surface = Waves.HeightAt(pos.x, pos.z);
            var p = new Vector3(pos.x, surface, pos.z);
            var ep = new ParticleSystem.EmitParams { applyShapeToPosition = false, startColor = DropColor(f) };
            int n = Mathf.Clamp((int)(32f * Mathf.Lerp(0.6f, 1f, strength) * Mathf.Lerp(0.7f, 1f, tierScale)), 8, 40);
            for (int i = 0; i < n; i++)
            {
                float a = (i + Random.value * 0.6f) / n * FXMath.TwoPi;
                var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                float r = Random.Range(0.15f, 0.45f);
                ep.position = p + new Vector3(d.x * r, 0.03f, d.y * r);
                float up = Random.Range(2.2f, 4.4f) * (0.7f + 0.3f * strength);
                ep.velocity = new Vector3(d.x * Random.Range(0.8f, 1.8f), up, d.y * Random.Range(0.8f, 1.8f));
                ep.startSize = Random.Range(0.02f, 0.06f);
                ep.startLifetime = Random.Range(0.45f, 0.75f);
                drops.Emit(ep, 1);
            }
            for (int i = 0; i < 10; i++)
            {
                float vy = Random.Range(0.6f, 1.4f);
                EmitBubble(p + new Vector3(Random.Range(-0.4f, 0.4f), Random.Range(-1.2f, -0.2f), Random.Range(-0.4f, 0.4f)),
                           new Vector3(Random.Range(-0.3f, 0.3f), vy, Random.Range(-0.3f, 0.3f)), Random.Range(0.02f, 0.06f), vy, 1.4f);
            }
        }

        /// FXEffects.Splash: droplets thrown up at a world position (fishing float, a fish, a co-op diver …).
        void SplashAt(Vector3 pos, float strength)
        {
            if (drops == null) return;
            float surface = Waves.HeightAt(pos.x, pos.z);
            var p = new Vector3(pos.x, surface, pos.z);
            int n = Mathf.Clamp((int)(30f * strength * Mathf.Lerp(0.6f, 1f, tierScale)), 2, 60);
            var ep = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < n; i++)
            {
                Vector2 d = Random.insideUnitCircle;
                ep.position = p + new Vector3(d.x * 0.25f, 0.02f, d.y * 0.25f);
                ep.velocity = new Vector3(d.x * 1.4f, Random.Range(1.5f, 3.6f) * (0.6f + 0.4f * strength), d.y * 1.4f);
                ep.startSize = Random.Range(0.015f, 0.05f);
                ep.startLifetime = Random.Range(0.45f, 0.9f);
                drops.Emit(ep, 1);
            }
        }

        /// FXEffects.Bubbles: a few rising bubbles at a world position (ignored above the surface).
        void BubblesAt(Vector3 pos, int count)
        {
            if (bubbles == null) return;
            for (int i = 0; i < count; i++)
            {
                float vy = Random.Range(0.5f, 1.2f);
                var vel = new Vector3(Random.Range(-0.2f, 0.2f), vy, Random.Range(-0.2f, 0.2f));
                EmitBubble(pos + Random.insideUnitSphere * 0.12f, vel, Random.Range(0.012f, 0.05f), vy);
            }
        }
    }
}
