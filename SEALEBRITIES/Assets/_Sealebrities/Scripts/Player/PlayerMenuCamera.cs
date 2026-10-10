using UnityEngine;

namespace Sealebrities
{
    /// Slow cinematic orbit around WorldInfo.MenuCameraLookAt while the main menu is open.
    /// Driven by PlayerController (the camera is detached from the head while orbiting).
    public class PlayerMenuCamera
    {
        Transform cam;
        Vector3 center;
        float radius, height, angle, time, smoothY;

        public void Begin(Transform camera)
        {
            cam = camera;
            Vector3 look = WorldInfo.MenuCameraLookAt;
            Vector3 pos = WorldInfo.MenuCameraPosition;
            if ((pos - look).sqrMagnitude < 1f)
            {
                // world did not publish a menu shot: frame the hut side of the island from the sea
                look = new Vector3(60f, 8f, 0f);
                pos = new Vector3(290f, 38f, -160f);
            }
            center = look;
            Vector3 d = pos - look;
            radius = Mathf.Max(15f, new Vector2(d.x, d.z).magnitude);
            height = d.y;
            angle = Mathf.Atan2(d.z, d.x);
            time = 0f;
            smoothY = pos.y;
            cam.SetPositionAndRotation(pos, Quaternion.LookRotation((look - pos).normalized, Vector3.up));
        }

        public void Tick(float dt)
        {
            if (cam == null) return;
            dt = Mathf.Min(dt, 0.1f);
            time += dt;
            angle += dt * 0.022f;     // ~1.3 degrees per second

            Vector3 p = center + new Vector3(Mathf.Cos(angle) * radius, height + Mathf.Sin(time * 0.15f) * 2f, Mathf.Sin(angle) * radius);
            float floor = Mathf.Max(WorldInfo.GroundHeight(p), Waves.SeaLevel) + 6f;
            smoothY = Mathf.Lerp(smoothY, Mathf.Max(p.y, floor), 1f - Mathf.Exp(-1.5f * dt));
            p.y = smoothY;

            Vector3 target = center + new Vector3(Mathf.Sin(time * 0.11f) * 3f, Mathf.Sin(time * 0.07f) * 1.5f, 0f);
            Vector3 dir = target - p;
            if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward;
            cam.SetPositionAndRotation(p, Quaternion.LookRotation(dir.normalized, Vector3.up));
        }
    }
}
