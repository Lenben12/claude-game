using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Per-frame inputs for the camera model animation.
    public struct FilmRigInput
    {
        public bool visible;         // the camera is the active item of a hand and gameplay is not blocked (not in the selfie view)
        public bool leftHand;        // held in the left (offhand) hand → mirrored pose
        public bool aiming;
        public bool recording;
        public bool lampOn;
        public bool underwater;
        public bool hasTarget;       // legacy: an animal is detected
        public bool framingOk;       // legacy: a Sealog photo subject is in the frame
        /// FilmMaterials.Screen* state of the device screen (−1 = derive from the legacy flags).
        public int screenState;
        public float zoom;           // current smoothed zoom (1..maxZoom)
        public float zoom01;         // 0..1 within the camera's zoom range (lens travel)
        public float fieldOfView;    // current view FOV in degrees
        public float baseFov;        // the unzoomed gameplay FOV (the model keeps its apparent size while zooming)
        public float tape01;
        public float progress01;
        public Vector2 lookDelta;    // GameInput.Look this frame
        public float moveSpeed;      // player speed m/s
        /// 1d pull-back (m): a wall / plank right in front of the eye pushes the camera back and down.
        public float pullBack;
    }

    /// One built camera model (one gear tier) attached to the player's view camera.
    /// Owns its combined meshes; shared primitive meshes/materials live in FilmMesh / FilmMaterials.
    /// Poses follow the art bible §1.13 (view space, vertical FOV 70°): right-hand item (0.20, −0.20, 0.46),
    /// left-hand item mirrored, aimed (0, −0.11, 0.30); equip = rise 0.25 m in 0.25 s, stow 0.20 s;
    /// idle sway ±4 mm at 0.35 Hz, walk bob 10 mm / 6 mm at step rate, look lag 0.06 s.
    public sealed class FilmCameraRig
    {
        public GameObject root;
        public int tier;
        /// Golden camera (Sealog 100 %).
        public bool gold;
        /// Triangles of the camera model (without the mitten) – L.13 budget statistics.
        public int triangles;

        // dynamic parts
        public MeshRenderer screen;
        public GameObject recGroup;
        public MeshRenderer tally;
        public MeshRenderer tapeBar;
        public MeshRenderer cuff;
        public Transform tapePivot;
        public Transform zoomPart;
        public Vector3 zoomPartBase;
        public float zoomTravel;
        public readonly List<MeshRenderer> lampGlows = new List<MeshRenderer>();

        /// Scale applied to the camera model (≤ FilmRigBuilder.ModelScale) and the extra hold distance (≥ 1) used
        /// for big pro rigs so they stay inside the on-screen size budget.
        public float modelScale = FilmRigBuilder.ModelScale;
        public float distance = 1f;

        public readonly List<Object> owned = new List<Object>();

        // ---- poses (art bible §1.13)
        public static readonly Vector3 HoldPos = new Vector3(0.20f, -0.20f, 0.46f);
        public static readonly Vector3 HoldEuler = new Vector3(2f, -6f, -4f);
        public static readonly Vector3 AimPos = new Vector3(0f, -0.11f, 0.30f);
        public static readonly Vector3 AimEuler = new Vector3(2f, -4f, 0f);
        public const float EquipSeconds = 0.25f, StowSeconds = 0.20f, EquipDrop = 0.25f;
        public const float LookLag = 0.06f;
        const float LeftDrop = 0.01f;                 // offhand sits 1 cm lower (−0.21)

        // animation state
        float visBlend, aimBlend, blink, bobPhase, idleTime, kick;
        Vector2 sway;
        int screenState = -1, tapeState = -1, lampState = -1;
        bool recShown = true, tallyShown = true;
        float side = 1f;

        /// Snap animation state (used right after building so the model does not fly in from nowhere).
        public void ResetPose(bool visible)
        {
            visBlend = visible ? 1f : 0f;
            aimBlend = 0f;
            sway = Vector2.zero;
            kick = 0f;
        }

        /// Small recoil + flash of the screen when a photo is taken.
        public void Shutter() => kick = 1f;

        public void SetCuffColor(Color c)
        {
            if (cuff != null) cuff.sharedMaterial = FilmMaterials.Flat(c);
        }

        public bool IsShown => root != null && root.activeSelf;

        public void Animate(in FilmRigInput input, float dt)
        {
            if (root == null) return;

            // ---------------- show / hide (drop out of view, then deactivate)
            if (input.visible) visBlend = Mathf.MoveTowards(visBlend, 1f, dt / EquipSeconds);
            else visBlend = Mathf.MoveTowards(visBlend, 0f, dt / StowSeconds);
            if (!input.visible && visBlend <= 0f)
            {
                if (root.activeSelf) root.SetActive(false);
                return;
            }
            if (!root.activeSelf) root.SetActive(true);

            // the hand only switches while the model is (almost) out of view
            float wantSide = input.leftHand ? -1f : 1f;
            if (wantSide != side && visBlend < 0.35f) side = wantSide;

            // ---------------- pose
            aimBlend = Mathf.MoveTowards(aimBlend, input.aiming ? 1f : 0f, dt * 5f);
            float a = Mathf.SmoothStep(0f, 1f, aimBlend);
            Vector3 hold = new Vector3(HoldPos.x * side, HoldPos.y - (side < 0f ? LeftDrop : 0f), HoldPos.z);
            Vector3 aim = new Vector3(AimPos.x * side, AimPos.y, AimPos.z);
            Quaternion holdRot = Quaternion.Euler(HoldEuler.x, HoldEuler.y * side, HoldEuler.z * side);
            Quaternion aimRot = Quaternion.Euler(AimEuler.x, AimEuler.y * side, AimEuler.z * side);
            Vector3 pos = Vector3.Lerp(hold, aim, a) * distance;
            Quaternion rot = Quaternion.Slerp(holdRot, aimRot, a);

            // look lag: the model trails the look direction (time constant 0.06 s)
            float invDt = dt > 0.0001f ? 1f / dt : 0f;
            Vector2 rate = input.lookDelta * invDt;                          // ~deg/s
            float swayAmount = Mathf.Lerp(1f, 0.35f, a);
            Vector2 targetSway = new Vector2(Mathf.Clamp(rate.x * 0.018f, -6f, 6f), Mathf.Clamp(rate.y * 0.018f, -5f, 5f)) * swayAmount;
            sway = Vector2.Lerp(sway, targetSway, 1f - Mathf.Exp(-dt / LookLag));

            // idle sway (±4 mm at 0.35 Hz) + walk bob (10 mm vertical / 6 mm lateral per step) or a slow float in water
            idleTime += dt;
            float speed = Mathf.Min(input.moveSpeed, 6f);
            float w = idleTime * Mathf.PI * 2f * 0.35f;
            Vector3 bob = new Vector3(Mathf.Sin(w) * 0.004f, Mathf.Sin(w * 0.8f + 1.3f) * 0.003f, 0f);
            if (input.underwater)
            {
                bobPhase += dt * (1.5f + speed * 0.8f);
                bob.y += Mathf.Sin(bobPhase) * 0.004f * Mathf.Clamp01(speed / 3f);
            }
            else
            {
                float stepRate = Mathf.Clamp(1.2f + speed * 0.45f, 0f, 3.2f);   // steps per second
                bobPhase += dt * stepRate * Mathf.PI;                              // one step = half a cycle
                float k = Mathf.Clamp01(speed / 3f);
                bob.x += Mathf.Sin(bobPhase) * 0.006f * k;
                bob.y += -Mathf.Abs(Mathf.Sin(bobPhase)) * 0.010f * k;
            }
            bob *= Mathf.Lerp(1f, 0.4f, a);
            bob.x *= side;

            // shutter recoil
            kick = Mathf.MoveTowards(kick, 0f, dt * 7f);
            float kk = kick * kick;

            pos += bob + new Vector3(-sway.x * 0.0018f, -sway.y * 0.0018f, -0.012f * kk);
            float hide = 1f - Mathf.SmoothStep(0f, 1f, visBlend);
            pos += new Vector3(0.02f * side, -EquipDrop, -0.03f) * hide;
            // 1d: something right in front of the eye pushes the camera back and down (never into the wall)
            float pull = Mathf.Max(0f, input.pullBack);
            if (pull > 0f) pos += new Vector3(0f, -0.5f * pull, -pull);

            // keep the apparent on-screen size constant while the view zooms (model is a child of the camera)
            float fov = Mathf.Clamp(input.fieldOfView, 1f, 170f);
            float baseFov = Mathf.Clamp(input.baseFov > 1f ? input.baseFov : fov, 1f, 170f);
            float scale = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) / Mathf.Tan(baseFov * 0.5f * Mathf.Deg2Rad);
            var t = root.transform;
            t.localPosition = new Vector3(pos.x * scale, pos.y * scale, Mathf.Max(0.12f, pos.z));
            t.localRotation = rot * Quaternion.Euler(sway.y - 3f * kk + hide * 12f + pull * 25f, -sway.x, -sway.x * 0.5f);
            t.localScale = new Vector3(scale * side, scale, scale);   // negative X mirrors the model into the left hand

            // ---------------- REC dot + tally light (blink while recording)
            if (input.recording) blink += dt; else blink = 0f;
            bool dotOn = input.recording && (blink % 1f) < 0.62f;
            if (recGroup != null && recShown != dotOn) { recGroup.SetActive(dotOn); recShown = dotOn; }
            bool tallyOn = input.recording;
            if (tally != null && tallyShown != tallyOn) { tally.enabled = tallyOn; tallyShown = tallyOn; }

            // ---------------- screen state (swap cached materials only on change)
            int st;
            if (kick > 0.5f) st = FilmMaterials.ScreenCaptured;
            else if (input.screenState >= 0) st = input.screenState;
            else if (!input.recording && !input.aiming) st = FilmMaterials.ScreenStandby;
            else if (!input.hasTarget) st = FilmMaterials.ScreenLive;
            else if (input.framingOk) st = FilmMaterials.ScreenCaptured;
            else st = FilmMaterials.ScreenGood;
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
