using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// How-to-Fish style co-op nameplate (art bible §1.12): lower-case name in Fredoka, white with a thin dark outline,
    /// no background, billboard above the head, on-screen size clamped to stay readable at distance, hidden beyond
    /// 60 m / the underwater visibility and when a wall is in between. A white speaker icon pulses (2 Hz) left of the
    /// name while the player talks.
    public sealed class NetAvatarNameplate : MonoBehaviour
    {
        const int FontSize = 64;
        const float CharSize = 10f / FontSize;          // ≈ 1 world unit per line at scale 1
        const float NaturalHeight = 0.22f;              // world height of a line when close
        const float MinPx = 17f, MaxPx = 30f;           // line height on screen (cap height ≈ 14–26 px)
        const float OutlineOffset = 0.055f;             // ≈ 0.12 em outline
        static readonly Color TextColor = new Color(1f, 1f, 1f, 0.92f);
        static readonly Color OutlineColor = new Color(0.102f, 0.078f, 0.188f, 0.34f);   // #1A1430, layered to ~60 %
        static readonly Vector2[] OutlineDirs =
        {
            new Vector2(1f, 0f), new Vector2(-1f, 0f), new Vector2(0f, 1f), new Vector2(0f, -1f),
            new Vector2(0.71f, 0.71f), new Vector2(-0.71f, 0.71f), new Vector2(0.71f, -0.71f), new Vector2(-0.71f, -0.71f)
        };

        static Font font;
        static bool fontLoaded;
        static Mesh quad;
        static Material iconMat;
        static Texture2D iconTex;

        Transform root;
        TextMesh main;
        readonly TextMesh[] outline = new TextMesh[8];
        Transform icon;
        float textWidth;
        bool visible = true;
        bool occluded;
        bool shownLast;

        public static NetAvatarNameplate Create(Transform parent)
        {
            var go = new GameObject("Nameplate");
            go.transform.SetParent(parent, false);
            var p = go.AddComponent<NetAvatarNameplate>();
            p.Build();
            return p;
        }

        static Font LoadFont()
        {
            if (fontLoaded) return font;
            fontLoaded = true;
            try { font = Resources.Load<Font>("Fonts/Fredoka-Variable"); }
            catch (System.Exception) { font = null; }
            if (font == null)
            {
                try { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
                catch (System.Exception) { font = null; }
            }
            return font;
        }

        void Build()
        {
            root = transform;
            var f = LoadFont();
            for (int i = 0; i < outline.Length; i++)
            {
                outline[i] = MakeText("Outline" + i, f, OutlineColor, 0);
                outline[i].transform.localPosition = new Vector3(OutlineDirs[i].x * OutlineOffset, OutlineDirs[i].y * OutlineOffset, 0.01f);
            }
            main = MakeText("Name", f, TextColor, 1);

            var ig = new GameObject("Speaking");
            ig.transform.SetParent(root, false);
            ig.AddComponent<MeshFilter>().sharedMesh = Quad();
            var r = ig.AddComponent<MeshRenderer>();
            r.sharedMaterial = IconMaterial();
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortingOrder = 1;
            icon = ig.transform;
            ig.SetActive(false);
        }

        TextMesh MakeText(string name, Font f, Color c, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            var t = go.AddComponent<TextMesh>();
            if (f != null)
            {
                t.font = f;
                var mr = go.GetComponent<MeshRenderer>();
                mr.sharedMaterial = f.material;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.sortingOrder = order;
            }
            t.fontSize = FontSize;
            t.characterSize = CharSize;
            t.anchor = TextAnchor.MiddleCenter;
            t.alignment = TextAlignment.Center;
            t.richText = false;
            t.color = c;
            return t;
        }

        public void SetName(string name)
        {
            string s = string.IsNullOrEmpty(name) ? "" : name.ToLowerInvariant();
            main.text = s;
            for (int i = 0; i < outline.Length; i++) outline[i].text = s;
            textWidth = MeasureWidth(s);
            icon.localPosition = new Vector3(-(textWidth * 0.5f + 0.42f), 0.02f, 0f);
        }

        static float MeasureWidth(string s)
        {
            var f = LoadFont();
            if (f == null || string.IsNullOrEmpty(s)) return s == null ? 0f : s.Length * 0.5f;
            f.RequestCharactersInTexture(s, FontSize);
            float w = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                if (f.GetCharacterInfo(s[i], out CharacterInfo ci, FontSize)) w += ci.advance;
                else w += FontSize * 0.5f;
            }
            return w * CharSize * 0.1f;
        }

        public void SetVisible(bool v)
        {
            visible = v;
            if (!v) Show(false);
        }

        void Show(bool v)
        {
            if (shownLast == v && root.gameObject.activeSelf == v) return;
            shownLast = v;
            root.gameObject.SetActive(v);
        }

        /// anchor = world point 0.5 m above the head; range = maximum distance to show the plate.
        public void Tick(Camera cam, Vector3 anchor, float range, bool speaking, bool checkOcclusion)
        {
            if (!visible || cam == null) { Show(false); return; }
            Transform ct = cam.transform;
            Vector3 cp = ct.position;
            Vector3 toPlate = anchor - cp;
            float dist = toPlate.magnitude;
            if (dist > range || dist < 0.3f || Vector3.Dot(toPlate, ct.forward) <= 0f) { Show(false); return; }

            if (checkOcclusion)
            {
                // walls, terrain, the hull … (avatars have no colliders, the local player is on Ignore Raycast)
                const int Mask = ~((1 << 2) | (1 << 4));
                occluded = Physics.Linecast(cp, anchor, Mask, QueryTriggerInteraction.Ignore);
            }
            if (occluded) { Show(false); return; }
            Show(true);

            // constant-ish screen size: natural size up close, clamped to MinPx..MaxPx line height
            float worldPerPx = 2f * dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, Screen.height);
            float px = Mathf.Clamp(NaturalHeight / Mathf.Max(worldPerPx, 1e-5f), MinPx, MaxPx);
            float s = px * worldPerPx;
            root.SetPositionAndRotation(anchor, ct.rotation);
            root.localScale = new Vector3(s, s, s);

            if (icon.gameObject.activeSelf != speaking) icon.gameObject.SetActive(speaking);
            if (speaking)
            {
                float pulse = 0.7f * (1f + 0.12f * Mathf.Sin(Time.unscaledTime * Mathf.PI * 4f));
                icon.localScale = new Vector3(pulse, pulse, pulse);
            }
        }

        // ------------------------------------------------------------------ speaker icon assets
        static Mesh Quad()
        {
            if (quad != null) return quad;
            quad = new Mesh
            {
                name = "SB_NetIconQuad",
                vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) },
                uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) },
                triangles = new[] { 0, 2, 1, 0, 3, 2 }
            };
            quad.RecalculateNormals();
            quad.RecalculateBounds();
            return quad;
        }

        static Material IconMaterial()
        {
            if (iconMat != null) return iconMat;
            iconTex = DrawSpeaker(64);
            iconMat = new Material(MaterialLibrary.UnlitShader) { name = "SB_NetSpeakerIcon" };
            iconMat.SetColor("_BaseColor", Color.white);
            iconMat.SetTexture("_BaseMap", iconTex);
            iconMat.SetTexture("_MainTex", iconTex);
            MaterialLibrary.MakeTransparent(iconMat, true);
            iconMat.renderQueue = (int)RenderQueue.Transparent + 50;
            return iconMat;
        }

        /// White speaker with two sound waves and a dark outline (anti-aliased by distance fields).
        static Texture2D DrawSpeaker(int n)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "SB_NetSpeaker", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            float aa = 1.5f / n;
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                    float d = SpeakerDist(u, v);              // < 0 inside the white shape
                    float fill = Mathf.Clamp01(0.5f - d / aa);
                    float edge = Mathf.Clamp01(0.5f - (d - 0.045f) / aa);   // outline band around it
                    float a = Mathf.Max(fill, edge * 0.6f);
                    float w = a > 0f ? fill / a : 0f;
                    byte c = (byte)Mathf.RoundToInt(Mathf.Lerp(30f, 255f, w));
                    px[y * n + x] = new Color32(c, c, (byte)Mathf.RoundToInt(Mathf.Lerp(48f, 255f, w)), (byte)Mathf.RoundToInt(a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        static float SpeakerDist(float u, float v)
        {
            // box (magnet) + trapezoid (cone)
            float box = BoxDist(u - 0.22f, v - 0.5f, 0.07f, 0.11f);
            float coneHalf = Mathf.Lerp(0.11f, 0.27f, Mathf.Clamp01((u - 0.29f) / 0.17f));
            float cone = Mathf.Max(Mathf.Abs(v - 0.5f) - coneHalf, Mathf.Max(0.29f - u, u - 0.46f));
            float body = Mathf.Min(box, cone);
            // two arcs (sound waves) to the right
            float cx = u - 0.46f, cy = v - 0.5f;
            float r = Mathf.Sqrt(cx * cx + cy * cy);
            float ang = Mathf.Abs(Mathf.Atan2(cy, cx));
            float arcMask = ang < 0.85f ? 0f : (ang - 0.85f) * 0.2f;
            float wave1 = Mathf.Abs(r - 0.17f) - 0.035f + arcMask;
            float wave2 = Mathf.Abs(r - 0.31f) - 0.035f + arcMask;
            if (cx < 0f) { wave1 = 1f; wave2 = 1f; }
            return Mathf.Min(body, Mathf.Min(wave1, wave2));
        }

        static float BoxDist(float x, float y, float hx, float hy)
        {
            float dx = Mathf.Abs(x) - hx, dy = Mathf.Abs(y) - hy;
            float ox = Mathf.Max(dx, 0f), oy = Mathf.Max(dy, 0f);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(dx, dy), 0f);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { font = null; fontLoaded = false; quad = null; iconMat = null; iconTex = null; }
    }
}
