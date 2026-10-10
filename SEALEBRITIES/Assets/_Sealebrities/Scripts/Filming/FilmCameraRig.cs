using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Per-frame inputs for the camera model animation.
    public struct FilmRigInput
    {
        public bool visible;
        public bool aiming;
        public bool recording;
        public bool lampOn;
        public bool underwater;
        public bool hasTarget;
        public float zoom;           // current smoothed zoom (1..maxZoom)
        public float zoom01;         // 0..1 within the camera's zoom range (lens travel)
        public float fieldOfView;    // current view FOV in degrees
        public float tape01;
        public float quality;
        public float progress01;
        public Vector2 lookDelta;    // GameInput.Look this frame
        public float moveSpeed;      // player speed m/s
    }

    /// One built camera model (one gear tier) attached to the player's view camera.
    /// Owns its combined meshes; shared primitive meshes/materials live in FilmMesh / FilmMaterials.
    public sealed class FilmCameraRig
    {
        public GameObject root;
        public int tier;

        // dynamic parts
        public MeshRenderer screen;
        public GameObject recGroup;
        public MeshRenderer tally;
        public MeshRenderer tapeBar;
        public Transform tapePivot;
        public Transform zoomPart;
        public Vector3 zoomPartBase;
        public float zoomTravel;
        public readonly List<MeshRenderer> lampGlows = new List<MeshRenderer>();

        // hold poses in view space (meters, relative to the view camera at FOV 72)
        public Vector3 hipPos, aimPos;
        public Quaternion hipRot = Quaternion.identity, aimRot = Quaternion.identity;

        public readonly List<Object> owned = new List<Object>();

        // animation state
        float visBlend, aimBlend, blink, bobPhase, idleTime;
        Vector2 sway;
        int screenState = -1, tapeState = -1, lampState = -1;
        bool recShown = true, tallyShown = true;

        const float BaseHalfTan = 0.7265425f; // tan(36°) – the model is laid out for a 72° view

        /// Snap animation state (used right after building so the model does not fly in from nowhere).
        public void ResetPose(bool visible)
        {
            visBlend = visible ? 1f : 0f;
            aimBlend = 0f;
            sway = Vector2.zero;
        }

        public void Animate(in FilmRigInput input, float dt)
        {
            if (root == null) return;

            // ---------------- show / hide (slide down out of view, then deactivate)
            visBlend = Mathf.MoveTowards(visBlend, input.visible ? 1f : 0f, dt * 4f);
            if (!input.visible && visBlend <= 0f)
            {
                if (root.activeSelf) root.SetActive(false);
                return;
            }
            if (!root.activeSelf) root.SetActive(true);

            // ---------------- pose
            aimBlend = Mathf.MoveTowards(aimBlend, input.aiming ? 1f : 0f, dt * 5f);
            float a = Mathf.SmoothStep(0f, 1f, aimBlend);
            Vector3 pos = Vector3.Lerp(hipPos, aimPos, a);
            Quaternion rot = Quaternion.Slerp(hipRot, aimRot, a);

            // sway: the model lags behind the look direction (frame-rate independent via angular rate)
            float invDt = dt > 0.0001f ? 1f / dt : 0f;
            Vector2 rate = input.lookDelta * invDt;                          // ~deg/s
            float swayAmount = Mathf.Lerp(1f, 0.35f, a);
            Vector2 targetSway = new Vector2(Mathf.Clamp(rate.x * 0.018f, -6f, 6f), Mathf.Clamp(rate.y * 0.018f, -5f, 5f)) * swayAmount;
            sway = Vector2.Lerp(sway, targetSway, 1f - Mathf.Exp(-dt * 9f));

            // bob: walking bob on land, slow floating drift in water
            idleTime += dt;
            float speed = Mathf.Min(input.moveSpeed, 6f);
            Vector3 bob;
            if (input.underwater)
            {
                bob = new Vector3(Mathf.Sin(idleTime * 0.9f) * 0.004f, Mathf.Sin(idleTime * 1.3f) * 0.005f, 0f);
                bobPhase += dt * (1.5f + speed * 0.8f);
                bob.y += Mathf.Sin(bobPhase) * 0.003f * Mathf.Clamp01(speed / 3f);
            }
            else
            {
                bobPhase += dt * speed * 2.1f;
                float k = Mathf.Clamp01(speed / 4f);
                bob = new Vector3(Mathf.Cos(bobPhase) * 0.006f * k, -Mathf.Abs(Mathf.Sin(bobPhase)) * 0.008f * k, 0f);
                bob.y += Mathf.Sin(idleTime * 1.6f) * 0.0015f;   // breathing
            }
            bob *= Mathf.Lerp(1f, 0.4f, a);

            pos += bob + new Vector3(-sway.x * 0.0018f, -sway.y * 0.0018f, 0f);
            float hide = 1f - Mathf.SmoothStep(0f, 1f, visBlend);
            pos += new Vector3(0.04f, -0.3f, -0.06f) * hide;

            // keep the apparent on-screen size constant while the view zooms (model is a child of the camera)
            float fov = Mathf.Clamp(input.fieldOfView, 1f, 170f);
            float scale = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) / BaseHalfTan;
            var t = root.transform;
            t.localPosition = new Vector3(pos.x * scale, pos.y * scale, pos.z);
            t.localRotation = rot * Quaternion.Euler(sway.y, -sway.x, -sway.x * 0.5f);
            t.localScale = new Vector3(scale, scale, scale);

            // ---------------- REC dot + tally light (blink while recording)
            if (input.recording) blink += dt; else blink = 0f;
            bool dotOn = input.recording && (blink % 1f) < 0.62f;
            if (recGroup != null && recShown != dotOn) { recGroup.SetActive(dotOn); recShown = dotOn; }
            bool tallyOn = input.recording;
            if (tally != null && tallyShown != tallyOn) { tally.enabled = tallyOn; tallyShown = tallyOn; }

            // ---------------- screen state (swap cached materials only on change)
            int st;
            if (!input.recording && !input.aiming) st = FilmMaterials.ScreenStandby;
            else if (!input.hasTarget) st = FilmMaterials.ScreenLive;
            else if (input.progress01 >= 1f) st = FilmMaterials.ScreenCaptured;
            else if (input.quality >= FilmHandCamera.MinQuality) st = FilmMaterials.ScreenGood;
            else st = FilmMaterials.ScreenWeak;
            if (screen != null && st != screenState) { screen.sharedMaterial = FilmMaterials.Screen(st); screenState = st; }

            // ---------------- storage bar
            if (tapePivot != null)
            {
                float f = Mathf.Clamp01(input.tape01);
                tapePivot.localScale = new Vector3(Mathf.Max(0.001f, f), 1f, 1f);
                int ts = f > 0.3f ? 0 : f > 0.12f ? 1 : 2;
                if (tapeBar != null && ts != tapeState) { tapeBar.sharedMaterial = FilmMaterials.Tape(ts); tapeState = ts; }
            }

            // ---------------- lens travel with zoom
            if (zoomPart != null)
                zoomPart.localPosition = zoomPartBase + Vector3.forward * (zoomTravel * Mathf.Clamp01(input.zoom01));

            // ---------------- strobes / LED panels glow while the flashlight is on
            int ls = input.lampOn ? 1 : 0;
            if (ls != lampState)
            {
                lampState = ls;
                var mat = FilmMaterials.LampDiffuser(input.lampOn);
                for (int i = 0; i < lampGlows.Count; i++) if (lampGlows[i] != null) lampGlows[i].sharedMaterial = mat;
            }
        }

        public void Destroy()
        {
            if (root != null) Object.Destroy(root);
            root = null;
            for (int i = 0; i < owned.Count; i++) if (owned[i] != null) Object.Destroy(owned[i]);
            owned.Clear();
            lampGlows.Clear();
        }
    }
}
