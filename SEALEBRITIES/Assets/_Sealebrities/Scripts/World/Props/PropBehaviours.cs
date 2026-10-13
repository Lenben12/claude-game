using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Cached main camera position (player camera, or the menu camera before the player exists).
    public static class PropCam
    {
        static Camera cached;
        static int frame = -1;
        static Vector3 pos;
        static bool valid;

        public static Vector3 Position { get { Refresh(); return pos; } }
        public static bool Valid { get { Refresh(); return valid; } }

        static void Refresh()
        {
            int f = Time.frameCount;
            if (f == frame) return;
            frame = f;
            Camera c = cached;
            if (c == null || !c.isActiveAndEnabled)
            {
                c = Camera.main;
                if (c == null && Refs.Player != null)
                {
                    var v = Refs.Player.View;
                    if (v != null) c = v;
                }
                cached = c;
            }
            valid = c != null;
            if (valid) pos = c.transform.position;
        }

        // C#-side frustum test for manual draw calls (grass): planes of the main camera once per frame, with a 20°
        // wider field of view – Update runs before the player turns the camera this frame, so fast turns never pop
        static readonly Plane[] planes = new Plane[6];
        static int planesFrame = -1;
        static bool planesValid;

        /// True when the bounds may be visible to the main camera (always true without a camera).
        public static bool InView(Bounds b)
        {
            int f = Time.frameCount;
            if (f != planesFrame)
            {
                planesFrame = f;
                Refresh();
                Camera c = cached;
                planesValid = false;
                if (c != null && !c.orthographic)
                {
                    float fov = Mathf.Min(c.fieldOfView + 20f, 170f);
                    Matrix4x4 proj = Matrix4x4.Perspective(fov, Mathf.Max(0.1f, c.aspect), c.nearClipPlane, c.farClipPlane);
                    GeometryUtility.CalculateFrustumPlanes(proj * c.worldToCameraMatrix, planes);
                    planesValid = true;
                }
            }
            return !planesValid || GeometryUtility.TestPlanesAABB(planes, b);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { cached = null; frame = -1; valid = false; planesFrame = -1; planesValid = false; }
    }

    /// A sleep spot (E.3 / L.9): the hut bed, the two porch hammocks and the sofa bunk (one per possible player).
    /// From 18:00 (GameManager.CanSleepNow) „E – Hinlegen“ lies the local player down: SleepVote.SetLying(true) – solo
    /// sleeps at once, in co-op everybody must lie down („1/2 · 2/2“, the host skips the night). Moving away, jumping,
    /// E again or – for guests – opening the pause menu gets up (SetLying(false)); the host stays lying in its pause menu
    /// so „Schlafen erzwingen“ keeps working. PropInfo.LocalSleepSpot tells the Player module
    /// which spot to lie the camera down on.
    public class PropBed : MonoBehaviour, IInteractable
    {
        /// Index into PropInfo.SleepSpots (−1 = not registered).
        public int spotIndex = -1;
        public byte kind;
        float lastUse = -100f;
        bool lyingHere;
        Vector3 anchor;

        /// Registers a sleep spot and returns its index (legacy: only the head-end eye position – the lying pose is
        /// derived; prefer RegisterBed / RegisterHammock).
        public static int Register(string id, Vector3 lie, float yaw, Vector3 stand, float standYaw, byte kind)
        {
            PropInfo.SleepSpots.Add(new SleepSpotInfo { id = id, lie = lie, yaw = yaw, stand = stand, standYaw = standYaw, kind = kind });
            return PropInfo.SleepSpots.Count - 1;
        }

        /// Round 3: a flat bed / bunk. pillow = top centre of the pillow (world), feetDir = horizontal direction from the
        /// head end to the foot end, mattressTop = height of the lying surface (blanket top). The diver's pelvis rests
        /// 0.88 m from the crown (crown at the far edge of the pillow), the head is raised by the pillow.
        public static int RegisterBed(string id, Vector3 pillow, float pillowLength, Vector3 feetDir, float mattressTop, Vector3 stand, float standYaw, byte kind = SleepSpotInfo.Bed)
        {
            feetDir.y = 0f;
            feetDir = feetDir.sqrMagnitude > 1e-6f ? feetDir.normalized : Vector3.forward;
            float yaw = Mathf.Atan2(feetDir.x, feetDir.z) * Mathf.Rad2Deg;
            Vector3 crown = pillow - feetDir * (pillowLength * 0.5f - 0.02f);
            Vector3 hip = crown + feetDir * 0.88f;
            hip.y = mattressTop - 0.015f;   // sinks in a little
            float pillowRise = Mathf.Max(0f, pillow.y - mattressTop);
            float headRise = Mathf.Clamp(Mathf.Atan2(pillowRise * 0.8f, 0.72f) * Mathf.Rad2Deg, 0f, 14f);
            var s = new SleepSpotInfo { id = id, yaw = yaw, stand = stand, standYaw = standYaw, kind = kind, hip = hip, headRise = headRise, feetRise = 0f };
            s.lie = EyeOf(s);
            PropInfo.SleepSpots.Add(s);
            return PropInfo.SleepSpots.Count - 1;
        }

        /// Round 3: a hammock hanging between the hook at the head end and the hook at the foot end with the given sag
        /// (centre line y(t) = lerp(a, b, t) − sag · sin(πt)). The pelvis lies in the lowest point, torso and legs follow
        /// the curve up.
        public static int RegisterHammock(string id, Vector3 hookHead, Vector3 hookFeet, float sag, Vector3 stand, float standYaw, float pillow = 0f)
        {
            Vector3 d = hookFeet - hookHead;
            Vector3 flat = new Vector3(d.x, 0f, d.z);
            float len = Mathf.Max(0.5f, flat.magnitude);
            Vector3 dir = flat / len;
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            Vector3 Curve(float t) => Vector3.Lerp(hookHead, hookFeet, t) - Vector3.up * (sag * Mathf.Sin(t * Mathf.PI));
            Vector3 hip = Curve(0.5f);
            Vector3 head = Curve(Mathf.Clamp01(0.5f - 0.7f / len));
            Vector3 feet = Curve(Mathf.Clamp01(0.5f + 0.85f / len));
            // the head rests on the cloth (+ a pillow when there is one, porch hammocks: 0.11 m)
            float headRise = Mathf.Clamp(Mathf.Atan2(head.y + Mathf.Max(0f, pillow) - hip.y, 0.7f) * Mathf.Rad2Deg + 2f, 4f, 34f);
            float feetRise = Mathf.Clamp(Mathf.Atan2(feet.y - hip.y, 0.85f) * Mathf.Rad2Deg, 0f, 28f);
            hip.y -= 0.01f;
            var s = new SleepSpotInfo { id = id, yaw = yaw, stand = stand, standYaw = standYaw, kind = SleepSpotInfo.Hammock, hip = hip, headRise = headRise, feetRise = feetRise };
            s.lie = EyeOf(s);
            PropInfo.SleepSpots.Add(s);
            return PropInfo.SleepSpots.Count - 1;
        }

        /// Eye position of a diver lying on the spot (same maths as DiverPuppet.LieEye at height scale 1).
        static Vector3 EyeOf(in SleepSpotInfo s) => DiverPuppet.LieEye(s.hip, s.yaw, s.headRise, s.kind == SleepSpotInfo.Hammock, 1f, out _);

        bool Lying => lyingHere && SleepVote.LocalLying;

        public string GetPrompt()
        {
            if (!CanInteract()) return null;
            if (Lying) return PropPrompt.Interact("Aufstehen", "Get up");
            if (!GameManager.I.CanSleepNow) return GameManager.SleepRuleHint;
            switch (kind)
            {
                case SleepSpotInfo.Hammock: return PropPrompt.Interact("In die Hängematte legen", "Lie in the hammock");
                case SleepSpotInfo.Bunk: return PropPrompt.Interact("Aufs Sofa legen", "Lie down on the sofa");
                default: return PropPrompt.Interact("Hinlegen", "Lie down");
            }
        }

        public bool CanInteract() => GameManager.I != null && GameManager.I.InGame && Time.unscaledTime - lastUse > 0.6f;

        public void Interact()
        {
            if (!CanInteract()) return;
            lastUse = Time.unscaledTime;
            if (Lying) { GetUp(); return; }
            var gm = GameManager.I;
            if (!gm.CanSleepNow)
            {
                GameEvents.RaiseNotify(NotifyKind.Info, GameManager.SleepRuleHint);
                return;
            }
            if (!Net.IsOnline || Net.PlayerCount <= 1)
            {
                // solo: lying down sleeps until the morning (E.3); round 3: the player lies down on this spot through
                // the fade (the Player module reads LocalSleepSpot on GameEvents.SleepStarted)
                PropInfo.LocalSleepSpot = spotIndex;
                SleepVote.SetLying(true);
                return;
            }
            var p = Refs.Player;
            anchor = p != null && p.Root != null ? p.Root.position : transform.position;
            lyingHere = true;
            PropInfo.LocalSleepSpot = spotIndex;
            SleepVote.SetLying(true);
        }

        void GetUp()
        {
            lyingHere = false;
            if (PropInfo.LocalSleepSpot == spotIndex) PropInfo.LocalSleepSpot = -1;
            if (SleepVote.LocalLying) SleepVote.SetLying(false);
        }

        void Update()
        {
            if (!lyingHere) return;
            if (!SleepVote.LocalLying)
            {
                // the night was skipped (vote reset) or the vote was cancelled elsewhere
                lyingHere = false;
                if (PropInfo.LocalSleepSpot == spotIndex) PropInfo.LocalSleepSpot = -1;
                return;
            }
            var p = Refs.Player;
            // the host stays in bed with the pause menu open: that is where „Schlafen erzwingen“ (ForceSleep) lives
            bool up = p == null || p.Root == null || p.IsFainting || (UIState.PauseOpen && !Net.HasAuthority) || GameInput.JumpPressed;
            if (!up)
            {
                // round 3: the Player module moves the diver onto the mattress / into the hammock – follow it there
                if (p is PlayerController pcl && pcl.IsLyingDown) anchor = p.Root.position;
                Vector3 d = p.Root.position - anchor;
                d.y = 0f;
                up = d.sqrMagnitude > 1.3f * 1.3f;
            }
            if (up) GetUp();
        }

        void OnDisable()
        {
            if (lyingHere) GetUp();
        }
    }

    /// Lights (and an optional emissive material / particle system) that switch on at night via EnvState.Night01.
    public class PropNightLight : MonoBehaviour
    {
        public Light[] lights = new Light[0];
        public float[] baseIntensity = new float[0];
        public Material emissive;
        public Color emissiveColor = Color.white;
        public float onAt = 0.15f, fullAt = 0.5f;
        public bool flicker;
        public float flickerAmount = 0.3f;
        public ParticleSystem particles;
        public float cullDistance = 180f;

        float seed, lastEmis = -1f, nextCheck;
        bool near = true, psOn;

        public static PropNightLight Attach(GameObject go, Light light, Material emissive, Color emissiveColor, float onAt, float fullAt, bool flicker)
        {
            var nl = go.AddComponent<PropNightLight>();
            if (light != null)
            {
                nl.lights = new[] { light };
                nl.baseIntensity = new[] { light.intensity };
                light.enabled = false;
            }
            nl.emissive = emissive;
            nl.emissiveColor = emissiveColor;
            nl.onAt = onAt;
            nl.fullAt = fullAt;
            nl.flicker = flicker;
            return nl;
        }

        void Awake() { seed = Random.value * 100f; }

        void Update()
        {
            float n = EnvState.Night01;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((n - onAt) / Mathf.Max(0.01f, fullAt - onAt)));
            if (Time.time >= nextCheck)
            {
                nextCheck = Time.time + 0.5f + seed * 0.001f;
                near = !PropCam.Valid || (PropCam.Position - transform.position).sqrMagnitude < cullDistance * cullDistance;
            }
            float f = 1f;
            if (flicker && k > 0f)
            {
                float t = Time.time;
                f = 1f - flickerAmount + flickerAmount * (Mathf.PerlinNoise(seed, t * 7f) * 1.4f + Mathf.PerlinNoise(seed + 9f, t * 17f) * 0.6f);
            }
            for (int i = 0; i < lights.Length; i++)
            {
                var l = lights[i];
                if (l == null) continue;
                bool on = near && k > 0.01f;
                if (l.enabled != on) l.enabled = on;
                if (on) l.intensity = baseIntensity[i] * k * f;
            }
            if (emissive != null)
            {
                float e = k * (flicker ? f : 1f);
                if (Mathf.Abs(e - lastEmis) > 0.02f || (e == 0f && lastEmis != 0f))
                {
                    lastEmis = e;
                    PropMats.SetEmission(emissive, emissiveColor * e);
                }
            }
            if (particles != null)
            {
                bool want = k > 0.05f && near;
                if (want != psOn)
                {
                    psOn = want;
                    if (want) particles.Play(true);
                    else particles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
            }
        }
    }

    /// Rotating lighthouse lamp with two spot lights and visible beams, active at night.
    public class PropLighthouseLamp : MonoBehaviour
    {
        public Transform pivot;
        public Light[] spots = new Light[0];
        public float spotIntensity = 40f;
        public Renderer[] beams = new Renderer[0];
        public Material beamMat;
        public Color beamColor = new Color(1f, 0.95f, 0.75f, 0.14f);
        public Material lens;
        public Color lensColor = new Color(3f, 2.6f, 1.6f);
        public Light glow;
        public float glowIntensity = 3f;
        public float speed = 26f;

        float lastK = -1f;

        void Update()
        {
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((EnvState.Night01 - 0.3f) / 0.15f));
            if (k > 0.001f && pivot != null) pivot.Rotate(0f, speed * Time.deltaTime, 0f, Space.Self);
            if (Mathf.Abs(k - lastK) < 0.01f) return;
            lastK = k;
            bool on = k > 0.01f;
            for (int i = 0; i < spots.Length; i++)
            {
                if (spots[i] == null) continue;
                spots[i].enabled = on;
                spots[i].intensity = spotIntensity * k;
            }
            for (int i = 0; i < beams.Length; i++) if (beams[i] != null) beams[i].enabled = on;
            if (beamMat != null)
            {
                var c = beamColor;
                c.a *= k;
                beamMat.SetColor("_BaseColor", c);
            }
            PropMats.SetEmission(lens, lensColor * k);
            if (glow != null)
            {
                glow.enabled = on;
                glow.intensity = glowIntensity * k;
            }
        }
    }

    /// Wind sock swinging with a slowly changing wind.
    public class PropWindsock : MonoBehaviour
    {
        public Transform sock;
        public float baseYaw;
        float seed, nextCheck;
        bool near = true;

        void Awake() { seed = Random.value * 50f; }

        void Update()
        {
            if (sock == null) return;
            if (Time.time >= nextCheck)
            {
                nextCheck = Time.time + 0.5f;
                near = !PropCam.Valid || (PropCam.Position - transform.position).sqrMagnitude < 300f * 300f;
            }
            if (!near) return;
            float t = Time.time;
            float wind = 0.4f + 0.6f * Mathf.PerlinNoise(seed, t * 0.12f);
            float yaw = baseYaw + (Mathf.PerlinNoise(seed + 3f, t * 0.05f) - 0.5f) * 80f + Mathf.Sin(t * 2.3f + seed) * 4f * wind;
            float droop = Mathf.Lerp(62f, 6f, wind) + Mathf.Sin(t * 3.1f + seed) * 2.5f;
            sock.localRotation = Quaternion.Euler(0f, yaw, -droop);
        }
    }

    /// Keeps an always-on light (cave glow, grotto lantern) enabled only while the camera is near (cheap per-frame check).
    public class PropNearLight : MonoBehaviour
    {
        public Light lightRef;
        public float range = 45f;
        float nextCheck;

        public static void Attach(Light l, float range)
        {
            if (l == null) return;
            var n = l.gameObject.AddComponent<PropNearLight>();
            n.lightRef = l;
            n.range = range;
            l.enabled = false;
        }

        void Update()
        {
            if (lightRef == null || Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 0.4f;
            bool near = PropCam.Valid && (PropCam.Position - transform.position).sqrMagnitude < range * range;
            if (lightRef.enabled != near) lightRef.enabled = near;
        }
    }

    /// Gentle pendulum (tire swing).
    public class PropSwing : MonoBehaviour
    {
        public float amplitude = 7f;
        float seed;
        void Awake() { seed = Random.value * 10f; }
        void Update()
        {
            float t = Time.time + seed;
            transform.localRotation = Quaternion.Euler(Mathf.Sin(t * 1.25f) * amplitude, 0f, Mathf.Sin(t * 0.61f) * amplitude * 0.4f);
        }
    }

    /// Dive-site marker buoy floating on the shared wave function, with a fluttering flag and a night blinker.
    public class PropBuoy : MonoBehaviour
    {
        public float x, z, yaw;
        public float floatOffset = -0.15f;
        public Transform flag;
        public Material blinker;
        public Color blinkColor = new Color(3f, 2.2f, 0.6f);

        float phase, nextCheck;
        bool near = true, blinkOn;
        Quaternion tilt = Quaternion.identity;

        void Awake() { phase = Random.value * 10f; }

        void Update()
        {
            if (Time.time >= nextCheck)
            {
                nextCheck = Time.time + 1f;
                near = !PropCam.Valid || (PropCam.Position - transform.position).sqrMagnitude < 900f * 900f;
            }
            float t = Time.time;
            bool wantBlink = EnvState.Night01 > 0.2f && Mathf.Repeat(t + phase, 2f) < 0.25f;
            if (wantBlink != blinkOn)
            {
                blinkOn = wantBlink;
                PropMats.SetEmission(blinker, wantBlink ? blinkColor : Color.black);
            }
            if (!near) return;
            float h = Waves.HeightAt(x, z);
            Vector3 n = Waves.NormalAt(x, z);
            Quaternion target = Quaternion.FromToRotation(Vector3.up, Vector3.SlerpUnclamped(Vector3.up, n, 1.6f));
            tilt = Quaternion.Slerp(tilt, target, Mathf.Clamp01(Time.deltaTime * 3f));
            transform.SetPositionAndRotation(new Vector3(x, h + floatOffset + Mathf.Sin(t * 1.7f + phase) * 0.03f, z), tilt * Quaternion.Euler(0f, yaw, 0f));
            if (flag != null) flag.localRotation = Quaternion.Euler(0f, Mathf.Sin(t * 3.3f + phase) * 14f + Mathf.Sin(t * 7.1f) * 4f, 0f);
        }
    }

    /// Enables a dive site's decor only while the main camera is within range of the site center (horizontal distance).
    public class PropSiteActivation : MonoBehaviour
    {
        public GameObject target;
        public Vector2 center;
        public float radius = 550f;
        float nextCheck;

        public void Init(GameObject decor, Vector3 siteCenter, float range)
        {
            target = decor;
            center = new Vector2(siteCenter.x, siteCenter.z);
            radius = range;
            nextCheck = 0f;
        }

        void Update()
        {
            if (target == null || Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 0.25f;
            if (!PropCam.Valid) return;
            Vector3 p = PropCam.Position;
            float d = Vector2.Distance(new Vector2(p.x, p.z), center);
            bool active = target.activeSelf;
            if (active && d > radius + 25f) target.SetActive(false);
            else if (!active && d < radius) target.SetActive(true);
        }
    }

    /// Single manager animating all kelp clusters of the kelp forest (bone chains of skinned meshes), distance culled.
    public class PropKelpSway : MonoBehaviour
    {
        sealed class Cluster
        {
            public Vector3 center;
            public Renderer rend;
            public Transform[] bones;
            public Quaternion[] baseRot;
            public float[] phase;
            public float[] amp;
            public bool visible = true;
        }

        readonly List<Cluster> clusters = new List<Cluster>();
        public float cullDistance = 175f;
        public float animDistance = 85f;
        int frameIndex;

        public void Register(Vector3 center, Renderer rend, Transform[] bones, float[] phase, float[] amp)
        {
            var c = new Cluster { center = center, rend = rend, bones = bones, phase = phase, amp = amp, baseRot = new Quaternion[bones.Length] };
            for (int i = 0; i < bones.Length; i++) c.baseRot[i] = bones[i].localRotation;
            clusters.Add(c);
        }

        void Update()
        {
            frameIndex++;
            Vector3 cam = PropCam.Position;
            bool camValid = PropCam.Valid;
            float t = Time.time;
            // full-rate sway only within what can be seen under water (fog limit × 2.4)
            float animD = FXVisibility.Underwater ? Mathf.Min(animDistance, FXVisibility.Meters * 2.4f) : animDistance;
            float cull2 = cullDistance * cullDistance, anim2 = animD * animD;
            for (int i = 0; i < clusters.Count; i++)
            {
                var c = clusters[i];
                float d2 = camValid ? (c.center - cam).sqrMagnitude : 0f;
                bool vis = d2 < cull2;
                if (vis != c.visible)
                {
                    c.visible = vis;
                    if (c.rend != null) c.rend.enabled = vis;
                }
                if (!vis) continue;
                // off-screen clusters beyond 15 m are skipped (the pose is recomputed from Time.time, so they resume
                // correctly; the SMR has fixed expanded bounds, so isVisible does not depend on the pose)
                if (c.rend != null && !c.rend.isVisible && d2 > 225f) continue;
                if (d2 > anim2 && ((frameIndex + i) % 3) != 0) continue;
                var bones = c.bones;
                for (int b = 0; b < bones.Length; b++)
                {
                    float ph = c.phase[b], a = c.amp[b];
                    if (a <= 0f) continue;
                    float ax = Mathf.Sin(t * 0.85f + ph) * a + Mathf.Sin(t * 0.33f + ph * 0.5f) * a * 0.6f;
                    float az = Mathf.Cos(t * 0.63f + ph * 1.3f) * a * 0.7f;
                    bones[b].localRotation = c.baseRot[b] * Quaternion.Euler(ax, 0f, az);
                }
            }
        }
    }
}
