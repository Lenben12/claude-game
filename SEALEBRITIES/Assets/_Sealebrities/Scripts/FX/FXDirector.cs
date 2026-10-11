using System;
using UnityEngine;

namespace Sealebrities
{
    /// Runs after gameplay scripts (late execution order) so the camera has its final pose for the frame.
    /// Gathers the shared FXFrame once and ticks every FX subsystem. A failing subsystem is logged once and
    /// switched off; it never takes the rest of the game (or the bootstrap) down with it.
    /// Applies the graphics tier and the audio volumes from Settings (initially and on Settings.Changed).
    [DefaultExecutionOrder(1000)]
    public sealed class FXDirector : MonoBehaviour
    {
        readonly FXFrame f = new FXFrame();
        readonly FXBreath breath = new FXBreath();

        FXPostProcess post;
        FXCaustics caustics;
        FXParticles particles;
        FXGodRays rays;
        FXAudio audio;
        FXGraphics graphics;

        bool initialized, subscribed;
        bool pendingSplash, pendingSplashUnder, pendingSettings;
        float lastSplashTime = -100f;
        bool wasUnderwater;
        Camera configuredCam;

        public void Init()
        {
            if (initialized) return;
            initialized = true;
            try { post = new FXPostProcess(transform); } catch (Exception e) { Fail("post-processing", e); post = null; }
            try { caustics = new FXCaustics(); } catch (Exception e) { Fail("caustics", e); caustics = null; }
            try { particles = new FXParticles(transform); } catch (Exception e) { Fail("particles", e); particles = null; }
            try { rays = new FXGodRays(transform); } catch (Exception e) { Fail("god rays", e); rays = null; }
            try { audio = new FXAudio(transform); } catch (Exception e) { Fail("audio", e); audio = null; }
            graphics = new FXGraphics();
            ApplySettings();
            Subscribe();
        }

        static void Fail(string what, Exception e)
        {
            Debug.LogError("Sealebrities FX: " + what + " disabled: " + e);
        }

        void OnEnable() { if (initialized) Subscribe(); }
        void OnDisable() { Unsubscribe(); }

        void OnDestroy()
        {
            Unsubscribe();
            if (!initialized) return;
            try { if (caustics != null) caustics.Dispose(); } catch (Exception) { }
            try { if (particles != null) particles.Dispose(); } catch (Exception) { }
            try { if (rays != null) rays.Dispose(); } catch (Exception) { }
            try { if (audio != null) audio.Dispose(); } catch (Exception) { }
            try { if (post != null) post.Dispose(); } catch (Exception) { }
            try { if (graphics != null) graphics.RestoreEditorDefaults(); } catch (Exception) { }
        }

        void Subscribe()
        {
            if (subscribed) return;
            subscribed = true;
            GameEvents.UnderwaterChanged += OnUnderwaterChanged;
            Settings.Changed += OnSettingsChanged;
            if (audio != null) audio.Subscribe();
            if (particles != null) particles.Subscribe();
        }

        void Unsubscribe()
        {
            if (!subscribed) return;
            subscribed = false;
            GameEvents.UnderwaterChanged -= OnUnderwaterChanged;
            Settings.Changed -= OnSettingsChanged;
            if (audio != null) audio.Unsubscribe();
            if (particles != null) particles.Unsubscribe();
        }

        void OnUnderwaterChanged(bool under)
        {
            pendingSplash = true;
            pendingSplashUnder = under;
        }

        void OnSettingsChanged() { pendingSettings = true; }

        /// Graphics tier (only when it changed) + audio volumes.
        void ApplySettings()
        {
            var data = Settings.Data;
            GraphicsQuality q = data != null ? data.quality : GraphicsQuality.High;
            if (graphics != null && (q != f.quality || !appliedOnce))
            {
                appliedOnce = true;
                f.quality = q;
                try { graphics.Apply(q); } catch (Exception e) { Debug.LogWarning("Sealebrities FX: graphics tier failed: " + e.Message); }
                try { if (post != null) post.SetTier(q); } catch (Exception) { }
                try { if (particles != null) particles.SetTier(q); } catch (Exception) { }
                try { if (rays != null) rays.SetTier(q); } catch (Exception) { }
                try { if (caustics != null) caustics.SetTier(q); } catch (Exception) { }
            }
            if (audio != null) audio.ApplyVolumes();
        }
        bool appliedOnce;

        void LateUpdate()
        {
            if (!initialized) return;
            if (pendingSettings)
            {
                pendingSettings = false;
                ApplySettings();
            }
            UpdateFrame();
            breath.Tick(f);

            if (f.cam != configuredCam)
            {
                configuredCam = f.cam;
                if (configuredCam != null && graphics != null)
                {
                    try { graphics.ConfigureCamera(configuredCam); } catch (Exception e) { Debug.LogWarning("Sealebrities FX: camera setup failed: " + e.Message); }
                }
            }

            if (pendingSplash)
            {
                pendingSplash = false;
                // returning to the menu raises UnderwaterChanged(false) after InGame is already cleared: no splash then
                if (f.inGame) HandleSplash(pendingSplashUnder);
            }

            if (post != null)
            {
                try { post.Tick(f); } catch (Exception e) { Fail("post-processing", e); post = null; }
            }
            if (graphics != null && f.cam != null)
            {
                try { graphics.TickFarClip(f.cam, f.underwater && f.under01 >= 0.5f, FXVisibility.Meters); }
                catch (Exception e) { Debug.LogWarning("Sealebrities FX: far clip failed: " + e.Message); graphics = null; }
            }
            if (caustics != null)
            {
                try { caustics.Tick(f); }
                catch (Exception e) { Fail("caustics", e); try { caustics.Dispose(); } catch (Exception) { } caustics = null; }
            }
            if (particles != null)
            {
                try { particles.Tick(f, breath); } catch (Exception e) { Fail("particles", e); particles.Unsubscribe(); particles = null; }
            }
            if (rays != null)
            {
                try { rays.Tick(f); } catch (Exception e) { Fail("god rays", e); rays = null; }
            }
            if (audio != null)
            {
                try { audio.Tick(f, breath); }
                catch (Exception e) { Fail("audio", e); audio.Unsubscribe(); audio = null; }
            }
        }

        void HandleSplash(bool under)
        {
            float now = Time.unscaledTime;
            // Bobbing at the surface toggles the state often: only the first crossing gets the full splash.
            float strength = now - lastSplashTime > 1.2f ? 1f : 0.3f;
            lastSplashTime = now;
            if (f.camT == null) return;
            f.splash01 = Mathf.Max(f.splash01, strength);
            try
            {
                if (particles != null) particles.Splash(f, under, strength);
                if (audio != null) audio.PlaySplash(strength);
            }
            catch (Exception e) { Debug.LogWarning("Sealebrities FX: splash failed: " + e.Message); }
        }

        void UpdateFrame()
        {
            float dt = Time.deltaTime;
            if (dt > 0.1f) dt = 0.1f;
            f.dt = dt;
            f.time = Time.time;
            f.unscaledTime = Time.unscaledTime;
            // a real pause stops time (offline pause menu, finale); in co-op the world keeps running
            f.paused = Time.timeScale <= 0.0001f;
            f.inGame = GameManager.I != null && GameManager.I.InGame;

            IPlayer p = Refs.Player;
            if (p is UnityEngine.Object po && po == null) p = null;   // destroyed MonoBehaviour behind the interface
            f.hasPlayer = p != null;

            Camera cam = null;
            if (p != null) cam = p.View;
            if (cam == null) cam = Camera.main;
            f.cam = cam;
            if (cam != null)
            {
                Transform t = cam.transform;
                f.camT = t;
                f.camPos = t.position;
                f.camFwd = t.forward;
                f.camRight = t.right;
                f.camUp = t.up;
            }
            else f.camT = null;

            if (p != null)
            {
                f.underwater = p.IsUnderwater;
                f.inWater = p.IsInWater;
                f.depth = f.underwater ? Mathf.Max(0f, p.Depth) : 0f;
                f.air01 = Mathf.Clamp01(p.Air01);
                f.maxSafeDepth = p.MaxSafeDepth;
                f.flashlight = p.FlashlightOn;
                f.fainting = p.IsFainting;
            }
            else
            {
                f.underwater = false;
                f.inWater = false;
                f.depth = 0f;
                f.air01 = 1f;
                f.maxSafeDepth = 9999f;
                f.flashlight = false;
                f.fainting = false;
            }

            if (f.underwater && !wasUnderwater) f.sinceSubmerge = 0f;
            else if (f.underwater) f.sinceSubmerge += Time.unscaledDeltaTime;
            else f.sinceSubmerge = 100f;
            wasUnderwater = f.underwater;

            f.night01 = Mathf.Clamp01(EnvState.Night01);
            f.sun01 = Mathf.Clamp01(EnvState.SunIntensity01);
            f.day01 = FXMath.SmoothRange(0.04f, 0.35f, f.sun01) * (1f - f.night01 * 0.8f);

            Light sun = WorldInfo.Sun;
            if (sun != null)
            {
                float y = -sun.transform.forward.y;
                f.sunElevation = Mathf.Asin(Mathf.Clamp(y, -1f, 1f)) * Mathf.Rad2Deg;
            }
            else f.sunElevation = Mathf.Lerp(-20f, 45f, f.sun01);
            // golden hour: sun from just below the horizon to ~20° (the menu is at ~8°)
            f.golden01 = FXMath.SmoothRange(-4f, 3f, f.sunElevation) * (1f - FXMath.SmoothRange(10f, 22f, f.sunElevation));

            // Smoothed blends. Entering the water is quick (≤ 0.2 s); leaving lets the blue "drain" off the lens.
            float uTarget = f.underwater ? 1f : 0f;
            f.under01 = Mathf.MoveTowards(f.under01, uTarget, dt / (f.underwater ? 0.16f : 0.35f));
            f.flash01 = Mathf.MoveTowards(f.flash01, f.flashlight ? 1f : 0f, dt / 0.15f);
            f.faint01 = Mathf.MoveTowards(f.faint01, f.fainting ? 1f : 0f, dt / 0.8f);

            bool warn = f.underwater && !f.fainting && (f.depth > f.maxSafeDepth || f.air01 < 0.2f);
            f.warn01 = Mathf.MoveTowards(f.warn01, warn ? 1f : 0f, dt / 0.5f);

            float narcTarget = 0f;
            if (f.underwater && f.inGame && f.depth > 30f && GameManager.I.Difficulty.narcosis)
                narcTarget = FXMath.SmoothRange(30f, 60f, f.depth);
            f.narcosis01 = Mathf.MoveTowards(f.narcosis01, narcTarget, dt / 1.5f);

            f.splash01 = Mathf.Max(0f, f.splash01 - dt / 0.45f);
        }
    }
}
