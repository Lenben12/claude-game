using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Drives the intro camera, the fish and the captions. Created by PlayerIntro.Play.
    [DefaultExecutionOrder(200)]
    public sealed class PlayerIntroRunner : MonoBehaviour
    {
        static readonly float[] ShotEnd = { 4.5f, 9.0f, 15.0f, 19.0f, 23.5f };

        struct Swimmer
        {
            public Transform t;
            public CreatureRig rig;
            public Vector3 offset;
            public float phase, speed, wiggle;
        }

        Camera cam;
        Transform camT;
        float t;
        int shot = -1;
        float shotStart;
        readonly List<Swimmer> swimmers = new List<Swimmer>(24);
        readonly List<GameObject> spawned = new List<GameObject>(24);
        Light fillLight;

        // per-shot path
        Vector3 a0, a1, a2, look0, look1;
        float fov0, fov1, roll0, roll1;
        Vector3 schoolFrom, schoolTo;
        string caption;
        GUIStyle captionStyle, hintStyle;
        Texture2D black;

        public void Begin(Camera c)
        {
            cam = c;
            camT = c.transform;
            t = 0f;
        }

        void LateUpdate()
        {
            if (camT == null) { PlayerIntro.Finish(); return; }
            var gm = GameManager.I;
            if (gm != null && gm.InGame) { PlayerIntro.Finish(); return; }
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            t += dt;
            if (t > 0.6f && GameInput.AnyKeyDown) { PlayerIntro.Finish(); return; }
            if (t >= ShotEnd[ShotEnd.Length - 1]) { PlayerIntro.Finish(); return; }

            int s = 0;
            while (s < ShotEnd.Length - 1 && t >= ShotEnd[s]) s++;
            if (s != shot)
            {
                ClearSwimmers();
                shot = s;
                shotStart = s == 0 ? 0f : ShotEnd[s - 1];
                SetupShot(s);
                caption = CaptionFor(s);
                PlayerIntro.RaiseCaption(caption);
            }
            float len = ShotEnd[s] - shotStart;
            float u = Mathf.Clamp01((t - shotStart) / len);
            PoseCamera(s, u);
            AnimateSwimmers(dt, u);
        }

        static string CaptionFor(int s)
        {
            switch (s)
            {
                case 0: return Loc.Tr("Irgendwo im Pazifik …", "Somewhere in the Pacific …");
                case 1: return Loc.Tr("Eine Hütte. Ein Steg. Ein rostiges Boot.", "A hut. A dock. A rusty boat.");
                case 2: return Loc.Tr("Darunter: eine Welt voller Stars.", "Below: a world full of stars.");
                case 3: return Loc.Tr("… manche hat noch nie jemand gefilmt.", "… some nobody has ever filmed.");
                default: return Loc.Tr("Deine Crew. Dein Kanal. 1.000.000 Follower.", "Your crew. Your channel. 1,000,000 followers.");
            }
        }

        static float Above(Vector3 p, float h)
        {
            return Mathf.Max(WorldInfo.GroundHeight(p), Waves.SeaLevel) + h;
        }

        void SetupShot(int s)
        {
            roll0 = roll1 = 0f;
            switch (s)
            {
                case 0:
                {
                    // sunrise light over the island, the sun behind it
                    Vector3 toSun = new Vector3(-0.9f, 0f, -0.42f);
                    if (WorldInfo.Sun != null)
                    {
                        Vector3 f = -WorldInfo.Sun.transform.forward;
                        f.y = 0f;
                        if (f.sqrMagnitude > 0.01f) toSun = f;
                    }
                    toSun.Normalize();
                    Vector3 c = WorldLayout.IslandCenter;
                    a0 = c - toSun * 440f + Vector3.up * 16f;
                    a1 = c - toSun * 360f + Vector3.up * 30f;
                    a2 = a1;
                    look0 = c + Vector3.up * 26f;
                    look1 = c + Vector3.up * 16f;
                    fov0 = 48f; fov1 = 44f;
                    roll0 = -2f; roll1 = 1f;
                    break;
                }
                case 1:
                {
                    // swoop along the beach toward the dock and the boat
                    Vector3 p0 = new Vector3(160f, 0f, -62f), p1 = new Vector3(184f, 0f, -26f), p2 = new Vector3(199f, 0f, -5f);
                    a0 = new Vector3(p0.x, Above(p0, 6f), p0.z);
                    a1 = new Vector3(p1.x, Above(p1, 3.6f), p1.z);
                    a2 = new Vector3(p2.x, Above(p2, 2.6f), p2.z);
                    look0 = new Vector3(150f, 3f, 12f);
                    look1 = WorldLayout.BoatSpawn + Vector3.up * 1.5f;
                    fov0 = 64f; fov1 = 58f;
                    roll0 = 7f; roll1 = -3f;
                    break;
                }
                case 2:
                {
                    // plunge into the house reef
                    var site = DiveSites.Get("house_reef");
                    Vector3 r = site != null ? site.center : new Vector3(260f, 0f, 60f);
                    Vector3 p0 = r + new Vector3(-30f, 0f, -22f), p1 = r + new Vector3(-17f, 0f, -11f), p2 = r + new Vector3(5f, 0f, 6f);
                    a0 = new Vector3(p0.x, Waves.SeaLevel + 4.2f, p0.z);
                    a1 = new Vector3(p1.x, Waves.SeaLevel - 3.5f, p1.z);
                    float g = WorldInfo.GroundHeight(p2);
                    float y2 = g < Waves.SeaLevel - 4f ? Mathf.Min(g + 2.6f, Waves.SeaLevel - 1.6f) : (g + Waves.SeaLevel) * 0.5f;
                    a2 = new Vector3(p2.x, y2, p2.z);
                    look0 = a1 + new Vector3(0f, -3f, 0f);
                    look1 = a2 + (a2 - a1).normalized * 8f + Vector3.down * 0.8f;
                    fov0 = 62f; fov1 = 68f;
                    roll0 = -4f; roll1 = 2f;
                    SpawnReef(r, a2);
                    break;
                }
                case 3:
                {
                    // the deep: something with big eyes comes close
                    var site = DiveSites.Get("deep_trench");
                    Vector3 r = site != null ? site.center : new Vector3(760f, 0f, 660f);
                    float g = WorldInfo.GroundHeight(r);
                    float y = Mathf.Max(g + 14f, Waves.SeaLevel - 58f);
                    if (y > Waves.SeaLevel - 25f) y = Waves.SeaLevel - 25f;
                    a0 = new Vector3(r.x, y + 0.6f, r.z);
                    a1 = new Vector3(r.x + 0.8f, y, r.z + 1.2f);
                    a2 = a1;
                    look0 = a0 + new Vector3(0.4f, -0.4f, 6f);
                    look1 = a1 + new Vector3(0.1f, -0.1f, 6f);
                    fov0 = 56f; fov1 = 50f;
                    SpawnDeep(a1);
                    break;
                }
                default:
                {
                    // back up to the crew on the dock: dolly into the menu framing
                    PlayerMenuCamera.ComputeShot(out Vector3 pos, out Quaternion rot);
                    Vector3 back = rot * Vector3.back;
                    back.y = 0f;
                    back.Normalize();
                    a0 = pos + back * 9f + Vector3.down * 0.9f;
                    a0.y = Mathf.Max(a0.y, Waves.SeaLevel + 0.6f);
                    a1 = pos + back * 2.5f + Vector3.down * 0.2f;
                    a2 = pos;
                    look0 = pos + rot * Vector3.forward * 10f;
                    look1 = look0;
                    fov0 = 58f; fov1 = PlayerController.MenuFov;
                    finalRot = rot;
                    break;
                }
            }
        }

        Quaternion finalRot;

        void PoseCamera(int s, float u)
        {
            float e = s == 4 ? 1f - (1f - u) * (1f - u) * (1f - u) : Mathf.SmoothStep(0f, 1f, u);
            Vector3 p = Bezier(a0, a1, a2, e);
            Quaternion rot;
            if (s == 4)
            {
                Vector3 dir = look0 - p;
                Quaternion lookRot = Quaternion.LookRotation(dir.normalized, Vector3.up);
                rot = Quaternion.Slerp(lookRot, finalRot, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u * 1.3f)));
            }
            else
            {
                Vector3 target = Vector3.Lerp(look0, look1, e);
                if (s == 2)
                {
                    // look along the path once under water
                    Vector3 tan = Bezier(a0, a1, a2, Mathf.Min(1f, e + 0.05f)) - p;
                    if (tan.sqrMagnitude > 1e-4f && e > 0.25f) target = p + Vector3.Slerp((target - p).normalized, tan.normalized, 0.5f) * 8f;
                }
                if (s == 3) p += new Vector3(Mathf.Sin(t * 0.7f) * 0.15f, Mathf.Sin(t * 0.5f) * 0.1f, 0f);
                Vector3 dir = target - p;
                if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward;
                rot = Quaternion.LookRotation(dir.normalized, Vector3.up) * Quaternion.Euler(0f, 0f, Mathf.Lerp(roll0, roll1, e));
            }
            camT.SetPositionAndRotation(p, rot);
            if (cam != null) cam.fieldOfView = Mathf.Lerp(fov0, fov1, e);
        }

        static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float u)
        {
            float v = 1f - u;
            return v * v * a + 2f * v * u * b + u * u * c;
        }

        // ------------------------------------------------------------------ fish
        Swimmer Make(string id, float sizeCm, Vector3 offset, float speed, float phase)
        {
            var sw = new Swimmer();
            var sp = SpeciesDatabase.Get(id);
            if (sp == null) return sw;
            GameObject go = null;
            try { go = CreatureFactory.Build(sp); }
            catch (Exception e) { Debug.LogWarning("Intro: could not build " + id + ": " + e.Message); }
            if (go == null) return sw;
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * (sizeCm / 100f);
            spawned.Add(go);
            sw.t = go.transform;
            sw.rig = go.GetComponent<CreatureRig>();
            sw.offset = offset;
            sw.speed = speed;
            sw.phase = phase;
            sw.wiggle = 1f;
            return sw;
        }

        void SpawnReef(Vector3 reef, Vector3 camEnd)
        {
            // a school crossing in front of the camera's end point
            Vector3 fwd = (camEnd - a1);
            fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 0.01f ? fwd.normalized : Vector3.forward;
            Vector3 side = new Vector3(fwd.z, 0f, -fwd.x);
            Vector3 center = camEnd + fwd * 7f;
            schoolFrom = center - side * 9f + Vector3.up * 0.4f;
            schoolTo = center + side * 9f - Vector3.up * 0.2f;
            var rng = new System.Random(2024);
            for (int i = 0; i < 12; i++)
            {
                Vector3 off = new Vector3((float)rng.NextDouble() - 0.5f, ((float)rng.NextDouble() - 0.5f) * 0.6f, (float)rng.NextDouble() - 0.5f) * 2.4f;
                var sw = Make(i % 4 == 3 ? "parrotfish" : "blue_tang", i % 4 == 3 ? 38f : 24f, off, 1f, (float)rng.NextDouble() * 6f);
                if (sw.t != null) swimmers.Add(sw);
            }
            var turtle = Make("green_turtle", 110f, new Vector3(-2f, 1.8f, 3f), 0.5f, 0f);
            if (turtle.t != null) { turtle.wiggle = 0.3f; swimmers.Add(turtle); }
            var clown = Make("clownfish", 11f, new Vector3(1.2f, -0.6f, -1.5f), 0.2f, 1f);
            if (clown.t != null) swimmers.Add(clown);
        }

        void SpawnDeep(Vector3 camPos)
        {
            schoolFrom = camPos + new Vector3(1.5f, -0.4f, 9f);
            schoolTo = camPos + new Vector3(0.2f, -0.1f, 2.6f);
            var fish = Make("anglerfish", 140f, Vector3.zero, 1f, 0f);
            if (fish.t != null)
            {
                fish.wiggle = 0.5f;
                swimmers.Add(fish);
                if (fish.rig != null && fish.rig.lureLight != null) fish.rig.lureLight.enabled = true;
            }
            var lg = new GameObject("IntroFill");
            lg.transform.SetParent(transform, false);
            lg.transform.position = camPos + new Vector3(0.6f, 0.8f, 1.5f);
            fillLight = lg.AddComponent<Light>();
            fillLight.type = LightType.Point;
            fillLight.color = new Color(0.45f, 0.85f, 1f);
            fillLight.intensity = 2.2f;
            fillLight.range = 9f;
            fillLight.shadows = LightShadows.None;
            spawned.Add(lg);
        }

        void AnimateSwimmers(float dt, float u)
        {
            for (int i = 0; i < swimmers.Count; i++)
            {
                var sw = swimmers[i];
                if (sw.t == null) continue;
                float uu = Mathf.Clamp01(u * sw.speed + (1f - sw.speed) * 0.3f);
                Vector3 basePos = Vector3.Lerp(schoolFrom, schoolTo, uu);
                Vector3 wob = new Vector3(Mathf.Sin(t * 1.3f + sw.phase) * 0.25f, Mathf.Sin(t * 0.9f + sw.phase * 1.7f) * 0.15f, Mathf.Cos(t * 1.1f + sw.phase) * 0.25f);
                Vector3 p = basePos + sw.offset + wob;
                float top = Waves.SeaLevel - 0.7f, bottom = WorldInfo.GroundHeight(p) + 0.4f;
                p.y = bottom < top ? Mathf.Clamp(p.y, bottom, top) : top;
                Vector3 dir = schoolTo - schoolFrom;
                if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward;
                sw.t.SetPositionAndRotation(p, Quaternion.LookRotation(dir.normalized, Vector3.up) * Quaternion.Euler(0f, Mathf.Sin(t * 0.8f + sw.phase) * 8f, 0f));
                var rig = sw.rig;
                if (rig == null) continue;
                float f = 9f * sw.wiggle + 2f;
                var spine = rig.spine;
                if (spine != null)
                    for (int k = 0; k < spine.Length; k++)
                        if (spine[k] != null) spine[k].localRotation = Quaternion.Euler(0f, Mathf.Sin(t * f + sw.phase - k * 0.9f) * 9f * sw.wiggle * (k + 1f) / spine.Length, 0f);
                if (rig.tailFin != null) rig.tailFin.localRotation = Quaternion.Euler(0f, Mathf.Sin(t * f + sw.phase - 3f) * 18f * sw.wiggle, 0f);
                var flips = rig.flippers;
                if (flips != null)
                    for (int k = 0; k < flips.Length; k++)
                        if (flips[k] != null) flips[k].localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 1.6f + k * 0.5f) * 28f * (k % 2 == 0 ? 1f : -1f));
                var pecs = rig.pectoralFins;
                if (pecs != null)
                    for (int k = 0; k < pecs.Length; k++)
                        if (pecs[k] != null) pecs[k].localRotation = Quaternion.Euler(0f, Mathf.Sin(t * 5f + k) * 15f, 0f);
            }
        }

        void ClearSwimmers()
        {
            for (int i = 0; i < spawned.Count; i++) if (spawned[i] != null) Destroy(spawned[i]);
            spawned.Clear();
            swimmers.Clear();
            fillLight = null;
        }

        internal void Cleanup()
        {
            ClearSwimmers();
            if (black != null) Destroy(black);
            black = null;
        }

        // ------------------------------------------------------------------ fallback caption + fade-in (only without a UI listener)
        void OnGUI()
        {
            float fade = 1f - Mathf.Clamp01(t / 0.7f);
            if (fade > 0.001f)
            {
                if (black == null)
                {
                    black = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                    black.SetPixel(0, 0, Color.black);
                    black.Apply();
                }
                var old = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, fade);
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), black);
                GUI.color = old;
            }
            if (PlayerIntro.HasCaptionListener || string.IsNullOrEmpty(caption)) return;
            if (captionStyle == null)
            {
                Font f = Resources.Load<Font>("Fonts/Fredoka-Variable");
                captionStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(Screen.height * 0.04f), fontStyle = FontStyle.Bold };
                if (f != null) captionStyle.font = f;
                captionStyle.normal.textColor = Color.white;
                hintStyle = new GUIStyle(captionStyle) { fontSize = Mathf.RoundToInt(Screen.height * 0.018f), fontStyle = FontStyle.Normal };
            }
            float shotT = t - shotStart;
            float a = Mathf.Clamp01(shotT / 0.4f) * Mathf.Clamp01((ShotEnd[Mathf.Max(0, shot)] - t) / 0.4f);
            Rect r = new Rect(0f, Screen.height * 0.78f, Screen.width, Screen.height * 0.08f);
            var c = GUI.color;
            GUI.color = new Color(0.08f, 0.1f, 0.16f, 0.6f * a);
            GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), caption, captionStyle);
            GUI.color = new Color(1f, 1f, 1f, a);
            GUI.Label(r, caption, captionStyle);
            GUI.color = new Color(1f, 1f, 1f, 0.55f);
            GUI.Label(new Rect(0f, Screen.height * 0.93f, Screen.width, Screen.height * 0.04f), Loc.Tr("Beliebige Taste: Überspringen", "Any key: skip"), hintStyle);
            GUI.color = c;
        }

        void OnDestroy()
        {
            ClearSwimmers();
            if (black != null) Destroy(black);
        }
    }
}
