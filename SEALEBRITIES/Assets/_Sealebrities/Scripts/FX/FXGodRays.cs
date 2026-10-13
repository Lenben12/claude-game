using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Additive light shafts below the surface, slanted along the refracted sun direction (art bible §2.3):
    /// flat graphic bands (no gaussian haze), width 1.2–3.5 m, ~18 m long, tint #DFFBFF, alpha ~0.12 in the
    /// shallows fading to 0 at 30 m, swaying at 0.1 Hz. 8 shafts on High, 5 on Medium, 3 on Low.
    /// All shafts live in one dynamic mesh (one draw call); each quad is billboarded around its own axis.
    sealed class FXGodRays
    {
        const int MaxCount = 8;
        const float FadeStart = 4f;
        const float MaxDepth = 30f;
        const float Alpha = 0.13f;
        const float SwayHz = 0.1f;
        const float SwayMeters = 0.45f;

        readonly GameObject go;
        readonly Mesh mesh;
        readonly MeshRenderer mr;
        readonly Material mat;
        readonly Texture2D tex;

        readonly Vector3[] verts = new Vector3[MaxCount * 4];
        readonly Color32[] cols = new Color32[MaxCount * 4];

        readonly Vector2[] anchor = new Vector2[MaxCount];
        readonly Vector2[] drift = new Vector2[MaxCount];
        readonly Vector2[] swayDir = new Vector2[MaxCount];
        readonly float[] swayPhase = new float[MaxCount];
        readonly float[] age = new float[MaxCount];
        readonly float[] life = new float[MaxCount];
        readonly float[] length = new float[MaxCount];
        readonly float[] width = new float[MaxCount];
        readonly float[] gain = new float[MaxCount];

        int count = MaxCount;
        float master;
        bool visible, needRespawn = true;

        static readonly Color RayTint = new Color(0xDF / 255f, 0xFB / 255f, 1f, 1f);   // #DFFBFF

        public FXGodRays(Transform parent)
        {
            go = new GameObject("FX_GodRays");
            go.transform.SetParent(parent, false);

            tex = FXAssets.GodRay(32, 128);
            mat = FXAssets.ParticleMaterial("FX_GodRays", tex, true, RayTint);

            mesh = new Mesh { name = "FX_GodRays" };
            mesh.MarkDynamic();
            var uvs = new Vector2[MaxCount * 4];
            var tris = new int[MaxCount * 6];
            for (int i = 0; i < MaxCount; i++)
            {
                int v = i * 4, t = i * 6;
                uvs[v] = new Vector2(0f, 1f);
                uvs[v + 1] = new Vector2(1f, 1f);
                uvs[v + 2] = new Vector2(1f, 0f);
                uvs[v + 3] = new Vector2(0f, 0f);
                tris[t] = v; tris[t + 1] = v + 1; tris[t + 2] = v + 2;
                tris[t + 3] = v; tris[t + 4] = v + 2; tris[t + 5] = v + 3;
            }
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.colors32 = cols;
            mesh.triangles = tris;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100f);

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            mr.enabled = false;
        }

        public void Dispose()
        {
            if (mesh != null) Object.Destroy(mesh);
            if (mat != null) Object.Destroy(mat);
            if (tex != null) Object.Destroy(tex);
        }

        public void SetTier(GraphicsQuality q)
        {
            int n = q == GraphicsQuality.High ? 8 : q == GraphicsQuality.Medium ? 5 : 3;
            if (n == count) return;
            count = Mathf.Clamp(n, 1, MaxCount);
            // collapse the unused quads (zero area: no raster cost, no triangle-buffer change)
            for (int i = count * 4; i < verts.Length; i++)
            {
                verts[i] = Vector3.zero;
                cols[i] = new Color32(0, 0, 0, 0);
            }
            needRespawn = true;
        }

        public void Tick(FXFrame f)
        {
            Light sun = WorldInfo.Sun;
            float depthFade = 1f - FXMath.SmoothRange(FadeStart, MaxDepth, f.depth);
            float target = (f.underwater && sun != null && f.depth < MaxDepth) ? f.under01 * f.day01 * depthFade : 0f;
            master = Mathf.MoveTowards(master, target, f.dt / 0.6f);

            if (master < 0.004f || f.camT == null || sun == null)
            {
                if (visible) { mr.enabled = false; visible = false; }
                if (master < 0.004f) needRespawn = true;
                return;
            }

            // Refract the sun direction into the water (Snell, n = 1.33): shafts are steeper than the sun.
            Vector3 sunFwd = sun.transform.forward;
            if (sunFwd.y > -0.08f) sunFwd.y = -0.08f;
            Vector2 h = new Vector2(sunFwd.x, sunFwd.z);
            float sinAir = Mathf.Clamp01(h.magnitude / sunFwd.magnitude);
            float sinWater = sinAir / 1.33f;
            Vector2 hd = h.sqrMagnitude > 1e-6f ? h.normalized : Vector2.zero;
            var dir = new Vector3(hd.x * sinWater, -Mathf.Sqrt(1f - sinWater * sinWater), hd.y * sinWater);
            // horizontal shift of a shaft per meter of depth
            Vector2 slope = new Vector2(dir.x, dir.z) / -dir.y;

            Vector3 cam = f.camPos;
            Vector2 camXZ = new Vector2(cam.x, cam.z);
            Vector2 depthShift = slope * f.depth;   // where a shaft passing the camera's depth enters the surface

            if (needRespawn)
            {
                needRespawn = false;
                for (int i = 0; i < count; i++)
                {
                    Respawn(i, f, camXZ - depthShift);
                    age[i] = Random.Range(0f, life[i]);
                }
            }

            if (!visible) { mr.enabled = true; visible = true; }

            Matrix4x4 w2l = go.transform.worldToLocalMatrix;
            float dt = f.dt;
            float swayT = f.time * FXMath.TwoPi * SwayHz;
            for (int i = 0; i < count; i++)
            {
                age[i] += dt;
                anchor[i] += drift[i] * dt;
                Vector2 atCamDepth = anchor[i] + depthShift;
                if (age[i] >= life[i] || (atCamDepth - camXZ).sqrMagnitude > 20f * 20f)
                {
                    Respawn(i, f, camXZ - depthShift);
                    age[i] = 0f;
                }

                float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(age[i] / life[i]));
                float alpha = master * env * gain[i] * Alpha;

                Vector2 a = anchor[i] + swayDir[i] * (Mathf.Sin(swayT + swayPhase[i]) * SwayMeters);
                float ax = a.x, az = a.y;
                var top = new Vector3(ax, Waves.HeightAt(ax, az) - 0.15f, az);
                Vector3 bottom = top + dir * length[i];
                Vector3 mid = (top + bottom) * 0.5f;
                Vector3 right = Vector3.Cross(dir, cam - mid);
                float rm = right.magnitude;
                right = rm > 1e-4f ? right / rm : f.camRight;

                float w0 = width[i] * 0.5f, w1 = width[i] * 0.8f;
                int v = i * 4;
                verts[v] = w2l.MultiplyPoint3x4(top - right * w0);
                verts[v + 1] = w2l.MultiplyPoint3x4(top + right * w0);
                verts[v + 2] = w2l.MultiplyPoint3x4(bottom + right * w1);
                verts[v + 3] = w2l.MultiplyPoint3x4(bottom - right * w1);

                var c = new Color32(255, 255, 255, (byte)(FXMath.Clamp01(alpha) * 255f));
                cols[v] = c; cols[v + 1] = c; cols[v + 2] = c; cols[v + 3] = c;
            }
            mesh.vertices = verts;
            mesh.colors32 = cols;
            mesh.bounds = new Bounds(w2l.MultiplyPoint3x4(cam), Vector3.one * 90f);
        }

        void Respawn(int i, FXFrame f, Vector2 center)
        {
            // mostly in front of the camera so the shafts are actually seen
            float yaw = Mathf.Atan2(f.camFwd.x, f.camFwd.z) + Random.Range(-1.9f, 1.9f);
            float radius = Random.Range(2.5f, 14f);
            anchor[i] = center + new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw)) * radius;
            float da = Random.Range(0f, FXMath.TwoPi);
            drift[i] = new Vector2(Mathf.Cos(da), Mathf.Sin(da)) * Random.Range(0.05f, 0.2f);
            float sa = Random.Range(0f, FXMath.TwoPi);
            swayDir[i] = new Vector2(Mathf.Cos(sa), Mathf.Sin(sa));
            swayPhase[i] = Random.Range(0f, FXMath.TwoPi);
            life[i] = Random.Range(6f, 12f);
            length[i] = Random.Range(15f, 21f);
            width[i] = Random.Range(1.2f, 3.5f);
            gain[i] = Random.Range(0.7f, 1f);
        }
    }
}
