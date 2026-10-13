using UnityEngine;

namespace Sealebrities
{
    /// Horst's seagull „Günther“ (A.1): a low-poly gull (0.45 m span) that circles above Horst, sits on his hat and
    /// steals his fish sandwich (gag 2). Purely cosmetic: every machine runs it locally from Horst's position; the steal
    /// gag is started by the replicated gag event.
    public sealed class NpcGull
    {
        enum Mode { Circle, Perch, Steal, Flee }

        readonly Transform root, body, wingL, wingR;
        Mode mode = Mode.Circle;
        float modeT, angle, flap;
        Vector3 pos, vel;
        bool valid;

        public Transform Root => root;

        public NpcGull(Transform parent)
        {
            var go = new GameObject("Guenther");
            root = go.transform;
            root.SetParent(parent, false);
            Material white = NpcMesh.Mat(LowPoly.Hex("#F4EFE6"), 0.15f), grey = NpcMesh.Mat(LowPoly.Hex("#9AA0A8"), 0.15f),
                     beak = NpcMesh.Mat(LowPoly.Hex("#F2C94C"), 0.25f), ink = NpcMesh.Mat(LowPoly.Hex("#1E1B2B"), 0.4f), red = NpcMesh.Mat(LowPoly.Hex("#D9534F"), 0.25f);
            body = NpcMesh.Node(root, "Body", Vector3.zero);
            NpcMesh.Part(body, "Torso", NpcMesh.Sphere(12, 20), white, Vector3.zero, new Vector3(0.12f, 0.11f, 0.26f));
            NpcMesh.Part(body, "Head", NpcMesh.Sphere(12, 20), white, new Vector3(0f, 0.06f, 0.13f), Vector3.one * 0.095f);
            NpcMesh.Part(body, "Beak", NpcMesh.Cone(0.018f, 0.07f, 10), beak, new Vector3(0f, 0.055f, 0.17f), Vector3.one, new Vector3(90f, 0f, 0f), false);
            NpcMesh.Part(body, "Spot", NpcMesh.Sphere(6, 8), red, new Vector3(0f, 0.045f, 0.205f), Vector3.one * 0.012f, default, false);
            for (int s = -1; s <= 1; s += 2)
                NpcMesh.Part(body, "Eye", NpcMesh.Sphere(6, 10), ink, new Vector3(s * 0.034f, 0.08f, 0.16f), Vector3.one * 0.018f, default, false);
            NpcMesh.Part(body, "Tail", NpcMesh.Plate("gull_tail", new[] { new Vector2(-0.04f, 0f), new Vector2(0.04f, 0f), new Vector2(0.03f, -0.08f), new Vector2(-0.03f, -0.08f) }, 0.01f),
                grey, new Vector3(0f, 0.01f, -0.14f), Vector3.one, new Vector3(90f, 0f, 0f), false);
            var wing = new[] { new Vector2(0f, -0.05f), new Vector2(0f, 0.05f), new Vector2(0.12f, 0.04f), new Vector2(0.23f, -0.01f), new Vector2(0.12f, -0.06f) };
            wingL = NpcMesh.Node(body, "WingL", new Vector3(-0.05f, 0.03f, 0.01f));
            wingR = NpcMesh.Node(body, "WingR", new Vector3(0.05f, 0.03f, 0.01f));
            var wingMesh = NpcMesh.Plate("gull_wing", wing, 0.012f);
            NpcMesh.Part(wingL, "Wing", wingMesh, grey, Vector3.zero, new Vector3(-1f, 1f, 1f), new Vector3(90f, 0f, 0f), false);
            NpcMesh.Part(wingR, "Wing", wingMesh, grey, Vector3.zero, Vector3.one, new Vector3(90f, 0f, 0f), false);
            for (int s = -1; s <= 1; s += 2)
                NpcMesh.Part(body, "Leg", NpcMesh.Cylinder(0.006f, 0.06f, 6), beak, new Vector3(s * 0.03f, -0.1f, 0f), Vector3.one, default, false);
        }

        public void Steal() { mode = Mode.Steal; modeT = 0f; }

        public void Perch() { mode = Mode.Perch; modeT = 0f; }

        /// horst may be null (Horst hidden) – then the gull hides as well.
        public void Tick(float dt, NpcActor horst, Vector3 cameraPos)
        {
            bool show = horst != null && horst.Present && horst.Motion != NpcMotion.Hidden && horst.gameObject.activeSelf
                        && (horst.Position - cameraPos).sqrMagnitude < 120f * 120f;
            if (root.gameObject.activeSelf != show) root.gameObject.SetActive(show);
            if (!show) { valid = false; return; }
            Vector3 hp = horst.Position;
            Vector3 hatTop = hp + Vector3.up * (horst.Def.height + 0.26f);
            if (!valid) { pos = hatTop + new Vector3(3f, 3f, 0f); valid = true; }
            modeT += dt;
            Vector3 target;
            bool flying = true;
            switch (mode)
            {
                case Mode.Perch:
                    target = hatTop;
                    if ((pos - target).sqrMagnitude < 0.04f) { flying = false; pos = target; }
                    if (modeT > 9f || horst.Moving) { mode = Mode.Circle; modeT = 0f; }
                    break;
                case Mode.Steal:
                {
                    Vector3 hand = horst.Body != null && horst.Body.handR != null ? horst.Body.handR.position : hp + Vector3.up;
                    if (modeT < 1.6f) target = hand;
                    else target = hp + new Vector3(Mathf.Sin(modeT) * 8f, 6f, Mathf.Cos(modeT) * 8f);
                    if (modeT > 6f) { mode = Mode.Perch; modeT = 0f; }
                    break;
                }
                default:
                    angle += dt * 0.55f;
                    target = hp + new Vector3(Mathf.Cos(angle) * 3.6f, 3.2f + Mathf.Sin(angle * 2.3f) * 0.6f, Mathf.Sin(angle) * 3.6f);
                    if (modeT > 16f && !horst.Moving) { mode = Mode.Perch; modeT = 0f; }
                    break;
            }
            if (flying)
            {
                Vector3 desired = (target - pos);
                float dist = desired.magnitude;
                Vector3 want = dist > 0.01f ? desired / dist * Mathf.Min(6f, dist * 2.2f + 0.8f) : Vector3.zero;
                vel = Vector3.Lerp(vel, want, 1f - Mathf.Exp(-3f * dt));
                pos += vel * dt;
                flap += dt * (vel.y > 0.3f ? 16f : 9f);
                float a = Mathf.Sin(flap) * (dist < 0.4f ? 10f : 45f);
                wingL.localRotation = Quaternion.Euler(0f, 0f, a);
                wingR.localRotation = Quaternion.Euler(0f, 0f, -a);
                Vector3 look = new Vector3(vel.x, vel.y * 0.4f, vel.z);
                if (look.sqrMagnitude > 0.01f) root.rotation = Quaternion.Slerp(root.rotation, Quaternion.LookRotation(look.normalized, Vector3.up), 1f - Mathf.Exp(-6f * dt));
            }
            else
            {
                wingL.localRotation = Quaternion.Euler(0f, 0f, 70f);
                wingR.localRotation = Quaternion.Euler(0f, 0f, -70f);
                root.rotation = Quaternion.Euler(0f, horst.Yaw + Mathf.Sin(Time.time * 0.8f) * 30f, 0f);
            }
            root.position = pos;
        }
    }
}
