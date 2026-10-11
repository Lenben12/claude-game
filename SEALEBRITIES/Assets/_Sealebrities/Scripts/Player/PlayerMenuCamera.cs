using UnityEngine;

namespace Sealebrities
{
    /// Main-menu scene (art bible §1.14): the bean-diver crew sits on the end of the dock with the legs dangling over
    /// the water (one plays the ukulele), the camera floats over the water south-west of them with a gentle sway and
    /// keeps the crew on the right third of the screen (the menu text sits on the left). The world shows its menu
    /// time (golden hour). Built when the menu opens, removed when the game starts. Driven by PlayerController.
    public class PlayerMenuCamera
    {
        const int CrewCount = 3;

        Transform cam;
        Camera camera;
        Vector3 basePos;
        Quaternion baseRot = Quaternion.identity;
        float time, lookCheck;
        GameObject crewRoot;
        readonly DiverPuppet[] crew = new DiverPuppet[CrewCount];
        Color builtSuit;
        int builtHat = -1, builtFace = -1;
        bool active;

        public void Begin(Transform cameraT, Camera cameraC)
        {
            cam = cameraT;
            camera = cameraC;
            active = true;
            EnsureCrew();
            ComputeShot(out basePos, out baseRot);
            time = 0f;
            if (cam != null) cam.SetPositionAndRotation(basePos, baseRot);
            if (camera != null) camera.fieldOfView = PlayerController.MenuFov;
        }

        public void End()
        {
            active = false;
            if (crewRoot != null) Object.Destroy(crewRoot);
            crewRoot = null;
            for (int i = 0; i < CrewCount; i++) crew[i] = null;
            builtHat = -1;
        }

        public void Tick(float dt)
        {
            if (!active || cam == null) return;
            dt = Mathf.Min(dt, 0.1f);
            time += dt;

            // gentle sway like standing in a small boat
            Vector3 off = new Vector3(Mathf.Sin(time * 0.21f) * 0.12f, Mathf.Sin(time * 0.33f + 1f) * 0.07f, Mathf.Sin(time * 0.17f + 2f) * 0.1f);
            Quaternion rot = baseRot * Quaternion.Euler(Mathf.Sin(time * 0.27f) * 0.6f, Mathf.Sin(time * 0.19f + 0.5f) * 0.8f, Mathf.Sin(time * 0.23f + 1.3f) * 0.5f);
            cam.SetPositionAndRotation(basePos + off, rot);

            AnimateCrew();

            // the player's own diver follows the character settings
            lookCheck -= dt;
            if (lookCheck <= 0f)
            {
                lookCheck = 0.5f;
                var d = Settings.Data;
                if (d.hat != builtHat || d.face != builtFace || (d.suitColor - builtSuit).maxColorComponent > 0.01f || (builtSuit - d.suitColor).maxColorComponent > 0.01f)
                    RebuildPlayerDiver();
            }
        }

        // ------------------------------------------------------------------ crew
        /// Where the crew sits: three seats on the south edge near the dock end (between the posts), facing south.
        public static void CrewSeats(out Vector3 center, Vector3[] seats)
        {
            Vector3 end = WorldLayout.DockEnd;
            float z0 = end.z;
            float deckY = DeckHeight(new Vector3(end.x - 1.8f, 0f, z0), PropInfo.DockDeckY);
            // find the south edge of the deck
            float edge = z0 - 1.2f;
            for (float dz = 0.6f; dz <= 3f; dz += 0.1f)
            {
                Vector3 probe = new Vector3(end.x - 1.8f, deckY + 2f, z0 - dz);
                if (!Physics.Raycast(probe, Vector3.down, out RaycastHit hit, 2.4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) || hit.point.y < deckY - 0.25f)
                {
                    edge = z0 - dz + 0.1f;
                    break;
                }
            }
            float[] xs = { end.x - 1.0f, end.x - 1.8f, end.x - 2.6f };
            for (int i = 0; i < seats.Length && i < xs.Length; i++)
                seats[i] = new Vector3(xs[i], DeckHeight(new Vector3(xs[i], 0f, z0), deckY) - 0.36f, edge + 0.12f);
            center = new Vector3(end.x - 1.8f, deckY + 0.55f, edge);
        }

        static float DeckHeight(Vector3 p, float fallback)
        {
            float guess = float.IsNaN(fallback) ? 1.3f : fallback;
            if (Physics.Raycast(new Vector3(p.x, guess + 4f, p.z), Vector3.down, out RaycastHit hit, 8f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && hit.point.y > Waves.SeaLevel + 0.3f && hit.point.y < guess + 1.5f)
                return hit.point.y;
            return guess;
        }

        static readonly Vector3[] seatBuf = new Vector3[CrewCount];

        /// The menu camera pose (also the last shot of the launch intro).
        public static void ComputeShot(out Vector3 pos, out Quaternion rot)
        {
            CrewSeats(out Vector3 c, seatBuf);
            pos = c + new Vector3(-3.4f, 0.25f, -5.4f);
            float sea = Waves.SeaLevel;
            if (pos.y < sea + 1.4f) pos.y = sea + 1.4f;
            // tilted ~5° further down than before: the dock bunting (raised above head height) now runs along the
            // top edge of the frame instead of straight through the logo
            Vector3 dir = (c + new Vector3(0f, -0.75f, 0f)) - pos;
            rot = Quaternion.Euler(0f, -16f, 0f) * Quaternion.LookRotation(dir.normalized, Vector3.up);
        }

        void EnsureCrew()
        {
            if (crewRoot != null) return;
            if (DiverModels.Build == null) DiverBuilder.Install();
            crewRoot = new GameObject("MenuCrew");
            if (Refs.WorldRoot != null) crewRoot.transform.SetParent(Refs.WorldRoot, false);
            CrewSeats(out _, seatBuf);
            for (int i = 0; i < CrewCount; i++) BuildMember(i);
        }

        DiverLook LookFor(int i)
        {
            switch (i)
            {
                case 1:
                {
                    var l = Settings.Look;
                    builtSuit = Settings.Data.suitColor;
                    builtHat = Settings.Data.hat;
                    builtFace = Settings.Data.face;
                    l.sitting = true;
                    return l;
                }
                case 0:
                {
                    var l = DiverStyle.ForPlayer(2, 4, 1);
                    l.sitting = true;
                    return l;
                }
                default:
                {
                    var l = DiverStyle.ForPlayer(1, 1, 5 + 4);
                    l.sitting = true;
                    return l;
                }
            }
        }

        void BuildMember(int i)
        {
            if (crewRoot == null) return;
            GameObject go = null;
            var build = DiverModels.Build ?? DiverBuilder.Build;
            try { go = build(LookFor(i)); }
            catch (System.Exception e) { Debug.LogException(e); }
            if (go == null) return;
            go.name = "CrewDiver" + i;
            go.transform.SetParent(crewRoot.transform, true);
            float yaw = 180f + (i - 1) * -9f;
            go.transform.SetPositionAndRotation(seatBuf[i], Quaternion.Euler(0f, yaw, 0f));
            var p = go.GetComponent<DiverPuppet>();
            if (p != null)
            {
                // one fishes from the dock, one plays the ukulele, the player's own diver just hangs out
                p.SetPose(false, true, 0f, i == 0 ? HoldItem.FishingRod : HoldItem.None, HoldItem.None, false);
                p.Ukulele = i == 2;
                p.LegDangle = i == 1 ? 1f : 0.7f;
                p.Phase = i * 2.1f;
            }
            crew[i] = p;
        }

        void RebuildPlayerDiver()
        {
            if (crewRoot == null) return;
            if (crew[1] != null) Object.Destroy(crew[1].gameObject);
            crew[1] = null;
            CrewSeats(out _, seatBuf);
            BuildMember(1);
        }

        /// Little chats: the outer divers turn their heads to the middle now and then, mouths flap.
        void AnimateCrew()
        {
            float t = time;
            for (int i = 0; i < CrewCount; i++)
            {
                var p = crew[i];
                if (p == null) continue;
                float phase = t * 0.31f + i * 1.7f;
                float look = Mathf.Clamp01(Mathf.Sin(phase) * 2f - 0.6f);
                p.HeadYaw = i == 0 ? -28f * look : i == 2 ? 26f * look : Mathf.Sin(t * 0.23f) * 14f;
                // speaker changes every ~3 s
                int speaker = Mathf.FloorToInt(t / 3.1f) % 4;
                float talk = speaker == i ? Mathf.Clamp01(Mathf.Sin(t * 11f + i) * 0.5f + Mathf.Sin(t * 7.3f) * 0.35f + 0.25f) : 0f;
                p.SetTalking(talk);
            }
        }
    }
}
