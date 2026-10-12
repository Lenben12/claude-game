using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

namespace Sealebrities
{
    /// Round 3 (GDD §20 Q30 „Gezeichnete Schatzkarte, zoom- und verschiebbar, Orte erst nach Entdeckung“): the parchment
    /// art of the treasure map. Sampled from the terrain in the background (a few ms per frame, never on one frame):
    /// one sheet for the whole sea (512²) plus detail patches for the main island, the volcano and the landmark islands
    /// (faded edges, so zooming in stays sharp). Palette: paper, ink coastline with a second ripple line, hatched hill
    /// shadows, a teal wash for the water (lighter in the lagoons), dotted depth contours, burnt sheet edges.
    public static class UITreasureMapArt
    {
        public const float Half = WorldLayout.TerrainSize * 0.5f;
        public const float Size = WorldLayout.TerrainSize;

        public sealed class Sheet
        {
            public float cx, cz, half;
            public int res;
            public bool isBase;
            public Texture2D tex;
            internal float[] h;
            internal int next;
            internal Color32[] px;
            internal int colorRow;
            public bool Done => tex != null;
            /// Normalized rect on the whole map (u0, v0 top, width, height).
            public Rect UV => new Rect((cx - half + Half) / Size, 1f - (cz + half + Half) / Size, 2f * half / Size, 2f * half / Size);
        }

        static Sheet[] sheets;
        static int current;
        static readonly Stopwatch Watch = new Stopwatch();
        /// Incremented whenever a sheet finished (the map element re-attaches the textures).
        public static int Version { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { sheets = null; current = 0; Version = 0; }

        public static Sheet[] Sheets
        {
            get
            {
                if (sheets != null) return sheets;
                sheets = new[]
                {
                    new Sheet { cx = 0f, cz = 0f, half = Half, res = 512, isBase = true },
                    new Sheet { cx = 0f, cz = 0f, half = 230f, res = 640 },
                    new Sheet { cx = WorldLayout.VolcanoIsland.x, cz = WorldLayout.VolcanoIsland.z, half = 150f, res = 320 },
                    new Sheet { cx = WorldLayout.KelpIsland.x, cz = WorldLayout.KelpIsland.z, half = 90f, res = 192 },
                    new Sheet { cx = WorldLayout.WreckIsland.x, cz = WorldLayout.WreckIsland.z, half = 90f, res = 192 },
                    new Sheet { cx = WorldLayout.BlueDropIsland.x, cz = WorldLayout.BlueDropIsland.z, half = 90f, res = 192 },
                    new Sheet { cx = WorldLayout.TrenchIsland.x, cz = WorldLayout.TrenchIsland.z, half = 90f, res = 192 },
                    new Sheet { cx = WorldLayout.FogIsland.x, cz = WorldLayout.FogIsland.z, half = 80f, res = 160 },
                };
                return sheets;
            }
        }

        public static Sheet Base => Sheets[0];
        public static bool AllDone => current >= Sheets.Length;

        static bool TerrainReady => WorldInfo.Terrain != null || WorldInfo.HeightProvider != null;

        /// Called while in game (map overlay tick): builds the sheets within a time budget (ms per frame).
        public static void Pump(float budgetMs)
        {
            if (AllDone || !TerrainReady) return;
            var gm = GameManager.I;
            if (gm == null || !gm.InGame) return;
            var list = Sheets;
            Watch.Restart();
            var terrain = WorldInfo.Terrain;
            float ty = terrain != null ? terrain.transform.position.y : 0f;
            float sea = Waves.SeaLevel;
            while (current < list.Length)
            {
                var s = list[current];
                int n = s.res * s.res;
                if (s.h == null) s.h = new float[n];
                float step = 2f * s.half / s.res;
                float x0 = s.cx - s.half + step * 0.5f, z0 = s.cz - s.half + step * 0.5f;
                while (s.next < n)
                {
                    int end = Mathf.Min(n, s.next + 96);
                    for (int i = s.next; i < end; i++)
                    {
                        int px = i % s.res, py = i / s.res;
                        float wx = x0 + px * step, wz = z0 + py * step;
                        float v;
                        try
                        {
                            if (terrain != null) v = terrain.SampleHeight(new Vector3(wx, 0f, wz)) + ty - sea;
                            else v = WorldInfo.HeightProvider(wx, wz) - sea;
                        }
                        catch (Exception) { v = -40f; }
                        if (float.IsNaN(v)) v = -40f;
                        s.h[i] = v;
                    }
                    s.next = end;
                    if (Watch.Elapsed.TotalMilliseconds > budgetMs) return;
                }
                // colour the sheet a few rows at a time (the 640² island sheet would hitch on one frame)
                bool failed = false;
                try
                {
                    if (s.px == null) { s.px = new Color32[n]; s.colorRow = 0; }
                    while (s.colorRow < s.res)
                    {
                        int r1 = Mathf.Min(s.res, s.colorRow + 16);
                        ColorRows(s, s.colorRow, r1);
                        s.colorRow = r1;
                        if (s.colorRow < s.res && Watch.Elapsed.TotalMilliseconds > budgetMs) return;
                    }
                    var t = new Texture2D(s.res, s.res, TextureFormat.RGBA32, false)
                    {
                        wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
                        name = "SB_UI_TreasureMap_" + s.res + "_" + Mathf.RoundToInt(s.cx) + "_" + Mathf.RoundToInt(s.cz)
                    };
                    t.SetPixels32(s.px);
                    t.Apply(false, true);
                    s.tex = t;
                }
                catch (Exception e) { failed = true; Debug.LogWarning("Sealebrities UI: treasure map sheet failed: " + e.Message); }
                s.h = null;
                s.px = null;
                current++;
                if (!failed) Version++;
                if (Watch.Elapsed.TotalMilliseconds > budgetMs) return;
            }
        }

        /// Tests: builds everything now (blocking).
        public static void BuildAllNow()
        {
            int guard = 0;
            while (!AllDone && TerrainReady && guard++ < 100000) Pump(1000f);
        }

        // ------------------------------------------------------------------ colours
        static readonly Color Paper = UIPalette.Rgb(0xEBDCB4);
        static readonly Color PaperDark = UIPalette.Rgb(0xC9A66B);
        static readonly Color Burnt = UIPalette.Rgb(0x8C6239);
        public static readonly Color Ink = UIPalette.Rgb(0x4B3423);
        static readonly Color Sand = UIPalette.Rgb(0xE6CB92);
        static readonly Color Lowland = UIPalette.Rgb(0xC5C189);
        static readonly Color Green = UIPalette.Rgb(0xA4AD70);
        static readonly Color Upland = UIPalette.Rgb(0x8E9862);
        static readonly Color Rock = UIPalette.Rgb(0x9A8462);
        static readonly Color Ash = UIPalette.Rgb(0x5E5650);
        static readonly Color Lagoon = UIPalette.Rgb(0xB4DCCD);
        static readonly Color Shallow = UIPalette.Rgb(0x9FC9BF);
        static readonly Color Mid = UIPalette.Rgb(0x8AB6B2);
        static readonly Color Deep = UIPalette.Rgb(0x779FA4);

        static Color LandColor(float h)
        {
            if (h < 1.3f) return Sand;
            if (h < 6f) return Color.Lerp(Lowland, Green, (h - 1.3f) / 4.7f);
            if (h < 18f) return Color.Lerp(Green, Upland, (h - 6f) / 12f);
            return Color.Lerp(Upland, Rock, Mathf.Clamp01((h - 18f) / 16f));
        }

        static Color SeaColor(float d)
        {
            if (d < 3f) return Color.Lerp(Lagoon, Shallow, d / 3f);
            if (d < 18f) return Color.Lerp(Shallow, Mid, (d - 3f) / 15f);
            return Color.Lerp(Mid, Deep, Mathf.Clamp01((d - 18f) / 70f));
        }

        static float Hash(int x, int y)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFF) / 65535f;
            }
        }

        static float Noise(float x, float y)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = x - ix, fy = y - iy;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float a = Hash(ix, iy), b = Hash(ix + 1, iy), c = Hash(ix, iy + 1), d = Hash(ix + 1, iy + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        static void ColorRows(Sheet s, int rowStart, int rowEnd)
        {
            int n = s.res;
            var h = s.h;
            var px = s.px;
            float step = 2f * s.half / n;
            float x0 = s.cx - s.half + step * 0.5f, z0 = s.cz - s.half + step * 0.5f;
            Vector3 vol = WorldLayout.VolcanoIsland;
            for (int y = rowStart; y < rowEnd; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    float v = h[i];
                    float hl = h[y * n + Mathf.Max(0, x - 1)], hr = h[y * n + Mathf.Min(n - 1, x + 1)];
                    float hd = h[Mathf.Max(0, y - 1) * n + x], hu = h[Mathf.Min(n - 1, y + 1) * n + x];
                    float wx = x0 + x * step, wz = z0 + y * step;
                    Color c;
                    if (v > 0f)
                    {
                        c = LandColor(v);
                        float dv = Mathf.Sqrt((wx - vol.x) * (wx - vol.x) + (wz - vol.z) * (wz - vol.z));
                        if (dv < WorldLayout.VolcanoShoreRadius + 12f) c = Color.Lerp(c, Ash, 0.6f);
                        // hill shading (light from the north-west) + ink hatching on the shadow side
                        float shade = Mathf.Clamp(((hl - hr) + (hu - hd)) / (2f * step) * 0.32f, -0.3f, 0.3f);
                        c = shade > 0f ? Color.Lerp(c, Paper, shade * 0.9f) : Color.Lerp(c, UIPalette.Rgb(0x6B5A44), -shade * 0.7f);
                        if (shade < -0.05f && ((x + y) & 3) == 0) c = Color.Lerp(c, Ink, Mathf.Min(0.5f, -shade * 2.2f));
                        // ink coastline
                        if (hl <= 0f || hr <= 0f || hd <= 0f || hu <= 0f) c = Color.Lerp(c, Ink, 0.85f);
                    }
                    else
                    {
                        float d = -v;
                        c = Color.Lerp(SeaColor(d), Paper, 0.28f);
                        // the classic second coast line, two texels out into the sea
                        bool near1 = hl > 0f || hr > 0f || hd > 0f || hu > 0f;
                        if (!near1)
                        {
                            float a2 = h[y * n + Mathf.Max(0, x - 2)], b2 = h[y * n + Mathf.Min(n - 1, x + 2)];
                            float c2 = h[Mathf.Max(0, y - 2) * n + x], d2 = h[Mathf.Min(n - 1, y + 2) * n + x];
                            if (a2 > 0f || b2 > 0f || c2 > 0f || d2 > 0f) c = Color.Lerp(c, Ink, 0.32f);
                        }
                        else c = Color.Lerp(c, Paper, 0.45f);   // surf
                        // dotted depth contours every 20 m
                        int band = Mathf.FloorToInt(d / 20f);
                        if (d > 4f && ((x + y) & 1) == 0)
                        {
                            int bl = Mathf.FloorToInt(-hl / 20f), bu = Mathf.FloorToInt(-hu / 20f);
                            if (bl != band || bu != band) c = Color.Lerp(c, Ink, 0.16f);
                        }
                    }
                    // paper grain + a few stains (world based, so the sheets match where they overlap)
                    float grain = Noise(wx / 7f, wz / 7f);
                    c *= 0.955f + 0.07f * grain;
                    float stain = Noise(wx / 230f + 17.3f, wz / 230f - 5.1f);
                    if (stain > 0.6f) c = Color.Lerp(c, PaperDark, (stain - 0.6f) * 0.7f);
                    float alpha = 1f;
                    float ex = Mathf.Min(x, n - 1 - x) / (float)n, ey = Mathf.Min(y, n - 1 - y) / (float)n;
                    float edge = Mathf.Min(ex, ey);
                    if (s.isBase)
                    {
                        // burnt, slightly ragged sheet edge
                        float rag = 0.045f + 0.02f * Noise(x * 0.09f, y * 0.09f);
                        if (edge < rag) c = Color.Lerp(c, Burnt, Mathf.Clamp01((rag - edge) / rag) * 0.6f);
                    }
                    else alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(edge / 0.07f));
                    c.a = alpha;
                    px[i] = c;
                }
            }
        }
    }

    /// Round 3 treasure map element (phone app „Seekarte“ and the M overlay): a clipped viewport with a zoomable /
    /// pannable parchment canvas. Drag = pan, wheel / + − = smooth zoom around the pointer, ◎ = centre on me.
    /// Shows the discovered places, the camp (after visiting) and the volcano (lock until the wreck), dive sites, the
    /// player arrow, the boat, co-op divers, the waypoint, SHARED co-op pins (palette top-left: tap an icon, then tap the
    /// map; tap a pin → waypoint / remove), pins for heard clues („?“ circles), found wrecks and the treasure X.
    /// Tap on empty map = waypoint (snaps to a site / place nearby).
    public class UITreasureMap : VisualElement
    {
        public const float MinZoom = 1f, MaxZoom = 16f;
        static readonly Color Ink = UITreasureMapArt.Ink;
        static readonly Color PaperTok = UIPalette.Rgb(0xF4E7C6);

        /// View state kept across page rebuilds (one per host: phone / overlay).
        public sealed class ViewState
        {
            public Vector2 center = new Vector2(-1f, -1f);
            public float zoom = 4f;
            public bool follow;
        }

        public static readonly ViewState PhoneView = new ViewState { zoom = 4f };
        public static readonly ViewState OverlayView = new ViewState { zoom = 3.5f, follow = true };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetViews()
        {
            PhoneView.center = new Vector2(-1f, -1f); PhoneView.zoom = 4f; PhoneView.follow = false;
            OverlayView.center = new Vector2(-1f, -1f); OverlayView.zoom = 3.5f; OverlayView.follow = true;
        }

        readonly ViewState view;
        readonly bool overlayMode;
        readonly VisualElement canvas, baseImg, sheetLayer, tokenLayer, pinLayer, markerLayer, toolbar, infoBar, hintPill;
        readonly UITreasureMapInk ink;
        readonly List<VisualElement> sheetEls = new List<VisualElement>();
        readonly List<VisualElement> labelEls = new List<VisualElement>();
        readonly List<VisualElement> crewDots = new List<VisualElement>();
        readonly List<VisualElement> paletteBtns = new List<VisualElement>();
        readonly List<PinToken> pinTokens = new List<PinToken>();
        readonly VisualElement playerArrow, boatMarker, waypointMarker;
        readonly Label infoTitle, infoSub, hintLabel;
        readonly VisualElement infoIcon;

        float zoom, zoomTarget;
        Vector2 center;
        bool anchorActive;
        Vector2 anchorUV, anchorLocal;
        bool pressed, dragging;
        int pointerId = -1;
        Vector2 pressLocal, lastLocal;
        int pinMode = -1, selectedPin = int.MinValue;
        int artVersion = -1, pinSig = int.MinValue, contentSig = int.MinValue;
        float sigTimer;
        bool labelsShown = true;
        float lastW, lastH, lastS = -1f;
        Vector2 lastLeftTop = new Vector2(float.NaN, float.NaN);

        class PinToken
        {
            public VisualElement el, ring;
            public UIGlyph glyph;
            public int id, icon;
            public Vector2 uv;
        }

        /// overlayMode = the M overlay (follows the player by default, slightly bigger tokens).
        public UITreasureMap(ViewState state, bool overlay)
        {
            view = state ?? new ViewState();
            overlayMode = overlay;
            name = "sb-treasure-map";
            style.overflow = Overflow.Hidden;
            style.backgroundColor = UIPalette.Rgb(0xB9C9B4);
            UIKit.Radius(this, 22f);
            UIKit.Border(this, 4f, UIPalette.Rgb(0x6B4E33));
            focusable = false;

            canvas = new VisualElement { name = "map-canvas", pickingMode = PickingMode.Ignore };
            canvas.style.position = Position.Absolute;
            Add(canvas);
            baseImg = new VisualElement { pickingMode = PickingMode.Ignore };
            UIKit.FillParent(baseImg);
            canvas.Add(baseImg);
            sheetLayer = Layer(canvas);
            ink = new UITreasureMapInk(this);
            canvas.Add(ink);
            tokenLayer = Layer(canvas);
            pinLayer = Layer(canvas);
            markerLayer = Layer(canvas);

            waypointMarker = new UIGlyph(UIGlyphs.Flag, UIPalette.Coral, 34f);
            Place(waypointMarker, -8f, -32f);
            markerLayer.Add(waypointMarker);
            boatMarker = new UIGlyph(UIGlyphs.Boat, UIPalette.Rgb(0x2D5F7A), 28f);
            Place(boatMarker, -14f, -14f);
            markerLayer.Add(boatMarker);
            playerArrow = new UIGlyph(UIGlyphs.Arrow, UIPalette.Coral, 34f);
            Place(playerArrow, -17f, -17f);
            markerLayer.Add(playerArrow);

            // ---- toolbar: pin palette (left), zoom + centre (right)
            toolbar = UIKit.Row();
            UIKit.Abs(toolbar, 10f, 10f, 10f, null);
            Add(toolbar);
            var palette = UIKit.Row();
            palette.style.backgroundColor = new Color(0.96f, 0.91f, 0.78f, 0.92f);
            UIKit.Radius(palette, 22f);
            UIKit.Pad(palette, 4f);
            UIKit.Border(palette, 2f, UIPalette.A(Ink, 0.5f));
            for (int i = 0; i < MapPins.IconCount; i++)
            {
                int icon = i;
                var b = RoundBtn(MapPins.Glyph(i), Color.white, MapPins.Color(i), 36f, () => TogglePinMode(icon));
                if (i > 0) b.style.marginLeft = 4f;
                palette.Add(b);
                paletteBtns.Add(b);
            }
            toolbar.Add(palette);
            toolbar.Add(UIKit.Flex());
            var zoomBox = UIKit.Row();
            zoomBox.Add(RoundBtn(UIGlyphs.Minus, Ink, PaperTok, 40f, () => ZoomBy(1f / 1.6f)));
            var plus = RoundBtn(UIGlyphs.Plus, Ink, PaperTok, 40f, () => ZoomBy(1.6f));
            plus.style.marginLeft = 6f;
            zoomBox.Add(plus);
            var me = RoundBtn(UIGlyphs.Person, Color.white, UIPalette.Coral, 40f, CenterOnMe);
            me.style.marginLeft = 6f;
            zoomBox.Add(me);
            toolbar.Add(zoomBox);
            // the gap between palette and zoom buttons belongs to the map (drag / tap)
            toolbar.pickingMode = PickingMode.Ignore;
            zoomBox.pickingMode = PickingMode.Ignore;

            // ---- compass (fixed in the corner, not on the canvas)
            var north = UIKit.Text("N", 18f, Ink, true);
            UIKit.Abs(north, null, null, 14f, 14f);
            UIKit.Size(north, 34f, 34f);
            north.style.backgroundColor = PaperTok;
            UIKit.Radius(north, 17f);
            UIKit.Border(north, 2f, Ink);
            north.style.unityTextAlign = TextAnchor.MiddleCenter;
            north.pickingMode = PickingMode.Ignore;
            Add(north);

            // ---- hint pill (pin mode)
            hintPill = UIKit.Row();
            UIKit.Abs(hintPill, 0f, null, 0f, 14f);
            hintPill.style.justifyContent = Justify.Center;
            var hp = UIKit.Row();
            hp.style.backgroundColor = new Color(0.16f, 0.12f, 0.08f, 0.82f);
            UIKit.Radius(hp, 16f);
            UIKit.Pad(hp, 6f, 14f, 6f, 14f);
            hintLabel = UIKit.Text("", 18f, Color.white, true);
            hp.Add(hintLabel);
            hintPill.Add(hp);
            hintPill.pickingMode = PickingMode.Ignore;
            UIKit.IgnorePicking(hintPill);
            UIKit.Show(hintPill, false);
            Add(hintPill);

            // ---- info bar (selected pin)
            infoBar = UIKit.Row();
            UIKit.Abs(infoBar, 10f, null, 10f, 10f);
            infoBar.style.backgroundColor = new Color(0.97f, 0.93f, 0.82f, 0.97f);
            UIKit.Radius(infoBar, 18f);
            UIKit.Border(infoBar, 2f, UIPalette.A(Ink, 0.55f));
            UIKit.Pad(infoBar, 8f, 10f, 8f, 10f);
            infoIcon = UIKit.Box();
            UIKit.Size(infoIcon, 40f, 40f);
            UIKit.Radius(infoIcon, 20f);
            UIKit.Center(infoIcon);
            infoBar.Add(infoIcon);
            var infoTxt = UIKit.Col();
            infoTxt.style.marginLeft = 10f;
            infoTxt.style.flexGrow = 1;
            infoTxt.style.flexShrink = 1;
            infoTitle = UIKit.Text("", 20f, Ink, true);
            infoSub = UIKit.Text("", 16f, UIPalette.Rgb(0x7A6048));
            infoTxt.Add(infoTitle);
            infoTxt.Add(infoSub);
            infoBar.Add(infoTxt);
            infoBar.Add(RoundBtn(UIGlyphs.Flag, Color.white, UIPalette.Coral, 40f, WaypointToSelected));
            var del = RoundBtn(UIGlyphs.Trash, Color.white, UIPalette.Rgb(0xB0473A), 40f, RemoveSelected);
            del.style.marginLeft = 6f;
            infoBar.Add(del);
            var cls = RoundBtn(UIGlyphs.Cross, Ink, PaperTok, 40f, () => Select(int.MinValue));
            cls.style.marginLeft = 6f;
            infoBar.Add(cls);
            UIKit.Show(infoBar, false);
            Add(infoBar);

            zoom = zoomTarget = Mathf.Clamp(view.zoom, MinZoom, MaxZoom);
            center = view.center;

            RegisterCallback<PointerDownEvent>(OnDown);
            RegisterCallback<PointerMoveEvent>(OnMove);
            RegisterCallback<PointerUpEvent>(OnUp);
            RegisterCallback<PointerCaptureOutEvent>(_ => { pressed = false; dragging = false; pointerId = -1; });
            RegisterCallback<WheelEvent>(OnWheel);
            RegisterCallback<GeometryChangedEvent>(_ => lastS = -1f);
        }

        static VisualElement Layer(VisualElement parent)
        {
            var l = new VisualElement { pickingMode = PickingMode.Ignore };
            UIKit.FillParent(l);
            parent.Add(l);
            return l;
        }

        static void Place(VisualElement e, float ml, float mt)
        {
            e.style.position = Position.Absolute;
            e.style.marginLeft = ml;
            e.style.marginTop = mt;
            e.pickingMode = PickingMode.Ignore;
        }

        VisualElement RoundBtn(UIGlyph.DrawFn icon, Color fg, Color bg, float size, Action onClick)
        {
            var b = UIKit.Box();
            UIKit.Size(b, size, size);
            UIKit.Radius(b, size * 0.5f);
            UIKit.Center(b);
            b.style.backgroundColor = bg;
            UIKit.Border(b, 2f, UIPalette.A(Ink, 0.55f));
            var g = new UIGlyph(icon, fg, size * 0.56f);
            b.Add(g);
            b.userData = "map-ui";
            b.RegisterCallback<ClickEvent>(e => { e.StopPropagation(); UIAudio.Click(); onClick?.Invoke(); });
            b.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            b.RegisterCallback<PointerUpEvent>(e => e.StopPropagation());
            return b;
        }

        // ------------------------------------------------------------------ coordinates
        public static Vector2 ToUV(Vector3 w) => new Vector2((w.x + UITreasureMapArt.Half) / UITreasureMapArt.Size, 1f - (w.z + UITreasureMapArt.Half) / UITreasureMapArt.Size);
        public static Vector3 FromUV(Vector2 uv) => new Vector3(uv.x * UITreasureMapArt.Size - UITreasureMapArt.Half, 0f, (1f - uv.y) * UITreasureMapArt.Size - UITreasureMapArt.Half);

        float ViewW => contentRect.width;
        float ViewH => contentRect.height;
        float CanvasSize(float z) => Mathf.Max(ViewW, ViewH) * z;

        /// Map px of the viewport → world.
        public Vector3 ViewToWorld(Vector2 local)
        {
            float s = CanvasSize(zoom);
            if (s < 1f) return Vector3.zero;
            Vector2 uv = center + (local - new Vector2(ViewW, ViewH) * 0.5f) / s;
            return FromUV(uv);
        }

        /// World → viewport px.
        public Vector2 WorldToView(Vector3 w)
        {
            float s = CanvasSize(zoom);
            return (ToUV(w) - center) * s + new Vector2(ViewW, ViewH) * 0.5f;
        }

        /// Panel position (pointer events) → viewport px (inside the border, where the canvas is positioned).
        Vector2 PanelToView(Vector2 panelPos)
        {
            Vector2 p = VisualElementExtensions.WorldToLocal(this, panelPos);
            p.x -= resolvedStyle.borderLeftWidth;
            p.y -= resolvedStyle.borderTopWidth;
            return p;
        }

        // ------------------------------------------------------------------ test / host API
        public float Zoom => zoom;
        public float ZoomTarget => zoomTarget;
        public Vector3 CenterWorld => FromUV(center);
        public int PinMode => pinMode;
        public int SelectedPin => selectedPin;
        public int PinTokenCount => pinTokens.Count;
        public int TokenCount => tokenLayer.childCount;
        public bool Follow => view.follow;

        public void SetView(Vector3 world, float z)
        {
            view.follow = false;
            center = ToUV(world);
            zoom = zoomTarget = Mathf.Clamp(z, MinZoom, MaxZoom);
            anchorActive = false;
            ClampCenter();
            Store();
            ApplyCanvas(true);
        }

        /// Wheel-style zoom around a viewport point (null = the centre).
        public void ZoomBy(float factor, Vector2? local = null)
        {
            float t = Mathf.Clamp(zoomTarget * factor, MinZoom, MaxZoom);
            if (Mathf.Approximately(t, zoomTarget)) return;
            Vector2 at = local ?? new Vector2(ViewW, ViewH) * 0.5f;
            float s = CanvasSize(zoom);
            if (s > 1f)
            {
                anchorLocal = at;
                anchorUV = center + (at - new Vector2(ViewW, ViewH) * 0.5f) / s;
                anchorActive = true;
            }
            zoomTarget = t;
        }

        /// Pans by a pixel delta (drag).
        public void PanBy(Vector2 deltaPx)
        {
            float s = CanvasSize(zoom);
            if (s < 1f) return;
            view.follow = false;
            anchorActive = false;
            center -= deltaPx / s;
            ClampCenter();
            Store();
            ApplyCanvas(false);
        }

        public void CenterOnMe()
        {
            view.follow = overlayMode;
            if (TryPlayer(out Vector3 p)) { center = ToUV(p); anchorActive = false; ClampCenter(); Store(); ApplyCanvas(false); }
        }

        public void TogglePinMode(int icon)
        {
            pinMode = pinMode == icon ? -1 : Mathf.Clamp(icon, 0, MapPins.IconCount - 1);
            Select(int.MinValue);
            RefreshPalette();
        }

        /// Tap at a viewport point (tests call it directly).
        public void Tap(Vector2 local)
        {
            var world = ViewToWorld(local);
            if (pinMode >= 0)
            {
                var p = MapPins.Add(world, pinMode);
                pinMode = -1;
                RefreshPalette();
                if (p != null) UIAudio.Pop();
                pinSig = int.MinValue;
                return;
            }
            int hit = HitPin(local);
            if (hit != int.MinValue) { Select(hit); return; }
            if (selectedPin != int.MinValue) { Select(int.MinValue); return; }
            SetWaypointNear(world, local);
        }

        // ------------------------------------------------------------------ input
        bool FromUi(EventBase e)
        {
            var t = e.target as VisualElement;
            while (t != null && t != this)
            {
                if (t == toolbar || t == infoBar || (t.userData is string s && s == "map-ui")) return true;
                t = t.parent;
            }
            return false;
        }

        void OnDown(PointerDownEvent e)
        {
            if (e.button != 0 || FromUi(e)) return;
            pressed = true;
            dragging = false;
            pointerId = e.pointerId;
            pressLocal = lastLocal = PanelToView(e.position);
            try { this.CapturePointer(e.pointerId); } catch (Exception) { }
            e.StopPropagation();
        }

        void OnMove(PointerMoveEvent e)
        {
            if (!pressed || e.pointerId != pointerId) return;
            if ((e.pressedButtons & 1) == 0) { EndPress(false); return; }
            Vector2 p = PanelToView(e.position);
            if (!dragging && (p - pressLocal).sqrMagnitude > 7f * 7f) dragging = true;
            if (dragging) PanBy(p - lastLocal);
            lastLocal = p;
            e.StopPropagation();
        }

        void OnUp(PointerUpEvent e)
        {
            if (!pressed || e.pointerId != pointerId) return;
            bool tap = !dragging;
            Vector2 p = PanelToView(e.position);
            EndPress(true);
            if (tap) Tap(p);
            e.StopPropagation();
        }

        void EndPress(bool release)
        {
            if (pointerId >= 0 && this.HasPointerCapture(pointerId))
            {
                try { this.ReleasePointer(pointerId); } catch (Exception) { }
            }
            pressed = false;
            dragging = false;
            pointerId = -1;
        }

        void OnWheel(WheelEvent e)
        {
            float d = e.delta.y;
            if (Mathf.Abs(d) > 0.001f) ZoomBy(d < 0f ? 1.3f : 1f / 1.3f, PanelToView(e.mousePosition));
            e.StopPropagation();
        }

        // ------------------------------------------------------------------ waypoint / pins
        void SetWaypointNear(Vector3 world, Vector2 local)
        {
            float s = CanvasSize(zoom);
            float snapPx = 30f;
            DiveSite best = null;
            float bestD = float.MaxValue;
            bool fog = StoryFlags.Has(StoryKeys.FogMap);
            foreach (var site in DiveSites.All)
            {
                if (site.IsStorySite && !fog) continue;
                float dpx = (WorldToView(site.center) - local).magnitude;
                float rpx = site.radius / UITreasureMapArt.Size * s;
                if (dpx < Mathf.Max(snapPx, rpx) && dpx < bestD) { best = site; bestD = dpx; }
            }
            if (best != null) UIWaypoint.Set(best.center, best.Name, best.mapColor);
            else UIWaypoint.Set(world, Loc.Tr("Wegpunkt", "Waypoint"), UIPalette.Coral);
            UIAudio.Click();
        }

        int HitPin(Vector2 local)
        {
            int best = int.MinValue;
            float bestD = 24f * 24f;
            for (int i = 0; i < pinTokens.Count; i++)
            {
                var t = pinTokens[i];
                Vector2 p = WorldToView(FromUV(t.uv));
                float d = (p - local).sqrMagnitude;
                if (d < bestD) { bestD = d; best = t.id; }
            }
            return best;
        }

        void Select(int id)
        {
            selectedPin = id;
            var pin = id != int.MinValue ? MapPins.Find(id) : null;
            if (pin == null) selectedPin = int.MinValue;
            UIKit.Show(infoBar, pin != null);
            if (pin != null)
            {
                infoIcon.Clear();
                infoIcon.style.backgroundColor = MapPins.Color(pin.icon);
                infoIcon.Add(new UIGlyph(MapPins.Glyph(pin.icon), Color.white, 24f));
                infoTitle.text = MapPins.Name(pin.icon);
                string who = pin.owner == Net.LocalId || !Net.IsOnline ? Loc.Tr("von dir", "yours") : Loc.Tr("von der Crew", "by the crew");
                string dist = TryPlayer(out Vector3 me) ? UIKit.Distance(Vector2.Distance(new Vector2(me.x, me.z), new Vector2(pin.x, pin.z))) + " · " : "";
                infoSub.text = dist + who;
            }
            for (int i = 0; i < pinTokens.Count; i++) UIKit.Show(pinTokens[i].ring, pinTokens[i].id == selectedPin);
        }

        void WaypointToSelected()
        {
            var pin = MapPins.Find(selectedPin);
            if (pin == null) return;
            UIWaypoint.Set(new Vector3(pin.x, 0f, pin.z), MapPins.Name(pin.icon), MapPins.Color(pin.icon));
            Select(int.MinValue);
        }

        void RemoveSelected()
        {
            if (selectedPin == int.MinValue) return;
            MapPins.Remove(selectedPin);
            Select(int.MinValue);
            pinSig = int.MinValue;
        }

        void RefreshPalette()
        {
            for (int i = 0; i < paletteBtns.Count; i++)
            {
                bool on = i == pinMode;
                var b = paletteBtns[i];
                b.style.scale = new Scale(on ? new Vector2(1.18f, 1.18f) : Vector2.one);
                UIKit.Border(b, on ? 3f : 2f, on ? Color.white : UIPalette.A(Ink, 0.55f));
            }
            UIKit.Show(hintPill, pinMode >= 0);
            if (pinMode >= 0) hintLabel.text = Loc.Tr("Tippe auf die Karte: ", "Tap the map: ") + MapPins.Name(pinMode);
        }

        // ------------------------------------------------------------------ per frame
        static bool TryPlayer(out Vector3 pos)
        {
            pos = Vector3.zero;
            var p = Refs.Player;
            if (p == null || (p is UnityEngine.Object o && o == null) || p.Root == null) return false;
            pos = p.Root.position;
            return true;
        }

        void ClampCenter()
        {
            float s = CanvasSize(zoom);
            if (s < 1f) return;
            float hx = ViewW * 0.5f / s, hy = ViewH * 0.5f / s;
            center.x = hx >= 0.5f ? 0.5f : Mathf.Clamp(center.x, hx, 1f - hx);
            center.y = hy >= 0.5f ? 0.5f : Mathf.Clamp(center.y, hy, 1f - hy);
        }

        void Store()
        {
            view.center = center;
            view.zoom = zoomTarget;
        }

        void ApplyCanvas(bool force)
        {
            float w = ViewW, h = ViewH;
            if (w < 2f || h < 2f) return;
            float s = CanvasSize(zoom);
            Vector2 lt = new Vector2(w * 0.5f - center.x * s, h * 0.5f - center.y * s);
            if (!force && Mathf.Abs(s - lastS) < 0.01f && (lt - lastLeftTop).sqrMagnitude < 0.0004f && Mathf.Approximately(w, lastW) && Mathf.Approximately(h, lastH)) return;
            bool resized = Mathf.Abs(s - lastS) >= 0.01f;
            lastS = s; lastW = w; lastH = h; lastLeftTop = lt;
            canvas.style.width = s;
            canvas.style.height = s;
            canvas.style.left = lt.x;
            canvas.style.top = lt.y;
            if (resized) ink.MarkDirtyRepaint();
            bool showLabels = zoom >= (overlayMode ? 2.4f : 2.8f);
            if (showLabels != labelsShown)
            {
                labelsShown = showLabels;
                for (int i = 0; i < labelEls.Count; i++) UIKit.Show(labelEls[i], showLabels);
            }
        }

        /// Called every frame by the host (phone app / overlay) while visible.
        public void Tick(float dt)
        {
            if (panel == null) return;
            UITreasureMapArt.Pump(overlayMode ? 2f : 3f);
            if (UITreasureMapArt.Version != artVersion) AttachArt();
            float w = ViewW, h = ViewH;
            if (w < 2f || h < 2f) return;

            if (center.x < 0f)
            {
                // first open: centre on the player (or the hut)
                center = ToUV(TryPlayer(out Vector3 p0) ? p0 : WorldLayout.Hut);
            }
            if (view.follow && !pressed && TryPlayer(out Vector3 pf)) center = ToUV(pf);

            if (!Mathf.Approximately(zoom, zoomTarget))
            {
                zoom = Mathf.Lerp(zoom, zoomTarget, 1f - Mathf.Exp(-12f * Mathf.Max(0.001f, dt)));
                if (Mathf.Abs(zoom - zoomTarget) < 0.004f * zoomTarget) zoom = zoomTarget;
                if (anchorActive && !view.follow)
                {
                    float s = CanvasSize(zoom);
                    if (s > 1f) center = anchorUV - (anchorLocal - new Vector2(w, h) * 0.5f) / s;
                }
                if (zoom == zoomTarget) anchorActive = false;
            }
            ClampCenter();
            Store();
            ApplyCanvas(false);

            sigTimer -= dt;
            if (sigTimer <= 0f)
            {
                sigTimer = 0.4f;
                int ps = MapPins.Signature();
                if (ps != pinSig) { pinSig = ps; RebuildPins(); }
                int cs = ContentSignature();
                if (cs != contentSig) { contentSig = cs; RebuildTokens(); ink.MarkDirtyRepaint(); }
                UpdateQuestMarkers();
            }
            UpdateMarkers();
        }

        // ------------------------------------------------------------------ quest „!“ / NPC „!“ „?“ (L.7, kept from the old chart)
        readonly List<VisualElement> questMarks = new List<VisualElement>();
        readonly List<Label> questMarkText = new List<Label>();

        void UpdateQuestMarkers()
        {
            int n = 0;
            var target = QuestTracker.Target;
            if (target.HasValue) SetQuestMark(n++, target.Value, "!", UIPalette.Sun);
            var act = NpcRegistry.Active;
            for (int i = 0; i < act.Count && n < 12; i++)
            {
                var npc = act[i];
                if (npc == null || npc.Root == null || npc.Marker == 0) continue;
                if (npc is UnityEngine.Object no && no == null) continue;
                SetQuestMark(n++, npc.Root.position, npc.Marker == 2 ? "?" : "!", npc.Marker == 2 ? UIPalette.Leaf : UIPalette.Sun);
            }
            for (int i = n; i < questMarks.Count; i++) UIKit.Show(questMarks[i], false);
        }

        void SetQuestMark(int i, Vector3 world, string text, Color c)
        {
            while (questMarks.Count <= i)
            {
                var e = UIKit.Box();
                e.pickingMode = PickingMode.Ignore;
                e.style.position = Position.Absolute;
                UIKit.Size(e, 26f, 26f);
                e.style.marginLeft = -13f; e.style.marginTop = -30f;
                UIKit.Radius(e, 13f);
                UIKit.Border(e, 2f, Color.white);
                UIKit.Center(e);
                var l = UIKit.Text("", 18f, UIPalette.Ink, true);
                l.pickingMode = PickingMode.Ignore;
                e.Add(l);
                markerLayer.Insert(0, e);
                questMarks.Add(e);
                questMarkText.Add(l);
            }
            var m = questMarks[i];
            UIKit.Show(m, true);
            SetUV(m, ToUV(world));
            m.style.backgroundColor = c;
            if (questMarkText[i].text != text) questMarkText[i].text = text;
        }

        void AttachArt()
        {
            artVersion = UITreasureMapArt.Version;
            var sheets = UITreasureMapArt.Sheets;
            var b = UITreasureMapArt.Base;
            // until the parchment is drawn (a few seconds after the game started) the canvas is plain paper
            if (b.Done) UIKit.Image(baseImg, b.tex, false);
            else baseImg.style.backgroundColor = UIPalette.Rgb(0xD9CFAF);
            for (int i = 1; i < sheets.Length; i++)
            {
                var s = sheets[i];
                if (!s.Done) continue;
                bool have = false;
                for (int k = 0; k < sheetEls.Count; k++) if (sheetEls[k].userData == s) { have = true; break; }
                if (have) continue;
                var e = new VisualElement { pickingMode = PickingMode.Ignore, userData = s };
                var r = s.UV;
                e.style.position = Position.Absolute;
                e.style.left = Length.Percent(r.x * 100f);
                e.style.top = Length.Percent(r.y * 100f);
                e.style.width = Length.Percent(r.width * 100f);
                e.style.height = Length.Percent(r.height * 100f);
                UIKit.Image(e, s.tex, false);
                sheetLayer.Add(e);
                sheetEls.Add(e);
            }
        }

        /// Everything that changes which places / clues / wrecks are on the map (polled, allocation free).
        static int ContentSignature()
        {
            unchecked
            {
                int h = 17;
                var places = MapDiscovery.Places;
                for (int i = 0; i < places.Length; i++) h = h * 31 + (MapDiscovery.IsKnown(places[i]) ? 1 : 0);
                h = h * 31 + (MapDiscovery.CampKnown ? 1 : 0);
                h = h * 31 + (VolcanoGate.Open ? 1 : 0);
                h = h * 31 + (StoryFlags.Has(StoryKeys.FogMap) ? 1 : 0);
                h = h * 31 + (StoryFlags.Has(StoryKeys.ToniArrived) ? 1 : 0);
                h = h * 31 + (MapPieces.Complete ? 1 : 0);
                h = h * 31 + (StoryFlags.Has(StoryKeys.TreasureFound) ? 1 : 0);
                for (int i = 0; i < WreckFlags.Length; i++) h = h * 31 + (StoryFlags.Has(WreckFlags[i]) ? 1 : 0);
                h = h * 31 + MapClues.Signature();
                var gm = GameManager.I;
                if (gm != null && gm.State != null) h = h * 31 + gm.State.unlockedSites.Count;
                h = h * 31 + (int)Loc.Current;
                return h;
            }
        }

        public static readonly string[] WreckIds = { "sailboat", "seaplane", "trawler", "pirates" };
        static readonly string[] WreckFlags = { "found:sailboat", "found:seaplane", "found:trawler", "found:pirates" };
        static readonly string[] WreckPlaced = { "wreck_sailboat", "wreck_seaplane", "wreck_trawler", "wreck_pirates" };

        static string WreckName(string id)
        {
            switch (id)
            {
                case "sailboat": return Loc.Tr("Segelboot-Wrack", "Sailboat wreck");
                case "seaplane": return Loc.Tr("Wasserflugzeug", "Seaplane");
                case "trawler": return Loc.Tr("Fischkutter", "Trawler");
                default: return Loc.Tr("Piratenwrack", "Pirate wreck");
            }
        }

        void RebuildTokens()
        {
            tokenLayer.Clear();
            labelEls.Clear();
            // places (discovered only)
            var places = MapDiscovery.Places;
            for (int i = 0; i < places.Length; i++)
            {
                var pl = places[i];
                if (!MapDiscovery.IsKnown(pl)) continue;
                AddToken(pl.pos, pl.icon, pl.color, Loc.Tr(pl.de, pl.en), false);
            }
            if (MapDiscovery.CampKnown) AddToken(WorldLayout.HermitCamp, UIGlyphs.Hut, UIPalette.Rgb(0xD9762B), Loc.Tr("Lager", "Camp"), false);
            // the volcano island: always charted – locked until the wreck opens the way
            AddToken(WorldLayout.VolcanoIsland, VolcanoGate.Open ? UIGlyphs.Smoke : UIGlyphs.Lock, VolcanoGate.Open ? UIPalette.Rgb(0xC0473A) : UIPalette.SunInk,
                     Loc.Tr("Vulkaninsel", "Volcano Isle"), true);
            if (StoryFlags.Has(StoryKeys.ToniArrived)) AddToken(WorldLayout.DriftwoodBeach, UIGlyphs.Envelope, UIPalette.Rgb(0x4F7FAE), "Toni", false);

            // dive sites (always charted, a lock while closed)
            bool fog = StoryFlags.Has(StoryKeys.FogMap);
            foreach (var site in DiveSites.All)
            {
                if (site.IsStorySite && !fog) continue;
                AddSiteTag(site);
            }
            // found wrecks
            for (int i = 0; i < WreckIds.Length; i++)
            {
                if (!StoryFlags.Has(WreckFlags[i])) continue;
                if (!SeaContent.Placed.TryGetValue(WreckPlaced[i], out Vector3 wp)) continue;
                AddToken(wp, UIGlyphs.BoatSide, UIPalette.Rgb(0x5B4636), WreckName(WreckIds[i]), false);
            }
            // heard clues: a „?“ in a dashed circle (the circle is drawn by the ink layer)
            var clues = MapClues.Current;
            for (int i = 0; i < clues.Count; i++) AddClueToken(clues[i].pos);
            // the treasure X
            if (MapPieces.Complete && !StoryFlags.Has(StoryKeys.TreasureFound))
            {
                var x = UIKit.Text("X", 34f, UIPalette.Rgb(0xB3261E), true);
                x.pickingMode = PickingMode.Ignore;
                SetUV(x, ToUV(MapPieces.SecretSpot));
                x.style.marginLeft = -12f; x.style.marginTop = -22f;
                tokenLayer.Add(x);
            }
            // doodles in the open sea (pure decoration – a treasure map needs a sea monster)
            AddDoodle(new Vector3(-760f, 0f, 60f), UIGlyphs.Whale, 58f);
            AddDoodle(new Vector3(260f, 0f, -780f), UIGlyphs.SharkFin, 40f);
            for (int i = 0; i < labelEls.Count; i++) UIKit.Show(labelEls[i], labelsShown);
        }

        static void SetUV(VisualElement e, Vector2 uv)
        {
            e.style.position = Position.Absolute;
            e.style.left = Length.Percent(Mathf.Clamp01(uv.x) * 100f);
            e.style.top = Length.Percent(Mathf.Clamp01(uv.y) * 100f);
        }

        void AddToken(Vector3 world, UIGlyph.DrawFn icon, Color color, string label, bool alwaysLabel)
        {
            float sz = overlayMode ? 30f : 28f;
            var t = UIKit.Box();
            t.pickingMode = PickingMode.Ignore;
            SetUV(t, ToUV(world));
            UIKit.Size(t, sz, sz);
            t.style.marginLeft = -sz * 0.5f; t.style.marginTop = -sz * 0.5f;
            UIKit.Radius(t, sz * 0.5f);
            t.style.backgroundColor = PaperTok;
            UIKit.Border(t, 2f, Ink);
            UIKit.Center(t);
            t.Add(new UIGlyph(icon, color, sz * 0.62f));
            tokenLayer.Add(t);
            if (string.IsNullOrEmpty(label)) return;
            var l = UIKit.Text(label, overlayMode ? 16f : 15f, Ink, true);
            l.style.unityFontStyleAndWeight = FontStyle.BoldAndItalic;
            l.style.unityTextOutlineWidth = 1.2f;
            l.style.unityTextOutlineColor = new Color(0.96f, 0.91f, 0.78f, 0.95f);
            l.style.whiteSpace = WhiteSpace.NoWrap;
            l.pickingMode = PickingMode.Ignore;
            SetUV(l, ToUV(world));
            l.style.translate = new Translate(Length.Percent(-50f), 0f);
            l.style.marginTop = sz * 0.5f + 1f;
            tokenLayer.Add(l);
            if (!alwaysLabel) labelEls.Add(l);
        }

        void AddSiteTag(DiveSite site)
        {
            bool unlocked = Progression.IsSiteUnlocked(site);
            var lbl = UIKit.Row();
            lbl.pickingMode = PickingMode.Ignore;
            SetUV(lbl, ToUV(site.center));
            lbl.style.translate = new Translate(Length.Percent(-50f), Length.Percent(-50f));
            lbl.style.backgroundColor = unlocked ? new Color(0.98f, 0.95f, 0.86f, 0.95f) : new Color(0.93f, 0.88f, 0.76f, 0.85f);
            UIKit.Radius(lbl, 10f);
            UIKit.Border(lbl, 1.5f, UIPalette.A(Ink, 0.6f));
            UIKit.Pad(lbl, 3f, 8f, 3f, 6f);
            if (!unlocked) lbl.Add(new UIGlyph(UIGlyphs.Lock, UIPalette.SunInk, 16f));
            else lbl.Add(UIKit.Dot(site.mapColor, 10f));
            var t = UIKit.Text(ShortName(site), overlayMode ? 16f : 15f, Ink, true);
            t.style.marginLeft = 4f;
            lbl.Add(t);
            UIKit.IgnorePicking(lbl);
            tokenLayer.Add(lbl);
        }

        void AddClueToken(Vector3 world)
        {
            var q = UIKit.Text("?", 20f, UIPalette.Rgb(0x8E3B2E), true);
            q.pickingMode = PickingMode.Ignore;
            SetUV(q, ToUV(world));
            q.style.marginLeft = -6f; q.style.marginTop = -13f;
            q.style.unityTextOutlineWidth = 1.5f;
            q.style.unityTextOutlineColor = PaperTok;
            tokenLayer.Add(q);
        }

        void AddDoodle(Vector3 world, UIGlyph.DrawFn icon, float size)
        {
            var g = new UIGlyph(icon, UIPalette.A(Ink, 0.38f), size);
            SetUV(g, ToUV(world));
            g.style.marginLeft = -size * 0.5f; g.style.marginTop = -size * 0.5f;
            tokenLayer.Add(g);
        }

        public static string ShortName(DiveSite s)
        {
            string n = s.Name;
            int q = n.IndexOf(" „", StringComparison.Ordinal);
            if (q < 0) q = n.IndexOf(" “", StringComparison.Ordinal);
            if (q < 0) q = n.IndexOf(" \"", StringComparison.Ordinal);
            return q > 0 ? n.Substring(0, q) : n;
        }

        void RebuildPins()
        {
            pinLayer.Clear();
            pinTokens.Clear();
            var pins = MapPins.All;
            float sz = overlayMode ? 34f : 32f;
            for (int i = 0; i < pins.Count; i++)
            {
                var p = pins[i];
                if (p == null) continue;
                var tok = new PinToken { id = p.id, icon = p.icon, uv = ToUV(new Vector3(p.x, 0f, p.z)) };
                tok.el = UIKit.Box();
                tok.el.pickingMode = PickingMode.Ignore;
                SetUV(tok.el, tok.uv);
                UIKit.Size(tok.el, sz, sz);
                tok.el.style.marginLeft = -sz * 0.5f; tok.el.style.marginTop = -sz * 0.5f;
                UIKit.Radius(tok.el, sz * 0.5f);
                tok.el.style.backgroundColor = MapPins.Color(p.icon);
                UIKit.Border(tok.el, 3f, PaperTok);
                tok.el.style.borderBottomColor = UIPalette.Darken(MapPins.Color(p.icon), 0.35f);
                UIKit.Center(tok.el);
                tok.glyph = new UIGlyph(MapPins.Glyph(p.icon), Color.white, sz * 0.55f);
                tok.el.Add(tok.glyph);
                tok.ring = UIKit.Box();
                tok.ring.pickingMode = PickingMode.Ignore;
                tok.ring.style.position = Position.Absolute;
                tok.ring.style.left = -7f; tok.ring.style.top = -7f; tok.ring.style.right = -7f; tok.ring.style.bottom = -7f;
                UIKit.Radius(tok.ring, sz);
                UIKit.Border(tok.ring, 3f, Color.white);
                UIKit.Show(tok.ring, p.id == selectedPin);
                tok.el.Add(tok.ring);
                // own pins get a tiny coral dot, crew pins a turquoise one
                var who = UIKit.Dot(p.owner == Net.LocalId || !Net.IsOnline ? UIPalette.Coral : UIPalette.Turquoise, 10f);
                who.pickingMode = PickingMode.Ignore;
                who.style.position = Position.Absolute;
                who.style.right = -4f; who.style.bottom = -4f;
                UIKit.Border(who, 2f, Color.white);
                tok.el.Add(who);
                pinLayer.Add(tok.el);
                pinTokens.Add(tok);
            }
            if (selectedPin != int.MinValue && MapPins.Find(selectedPin) == null) Select(int.MinValue);
        }

        void UpdateMarkers()
        {
            var player = Refs.Player;
            if (player is UnityEngine.Object po && po == null) player = null;
            Transform local = null;
            if (player != null && player.Root != null)
            {
                local = player.Root;
                UIKit.Show(playerArrow, true);
                SetUV(playerArrow, ToUV(player.Root.position));
                Transform yawSource = player.View != null ? player.View.transform : player.Root;
                playerArrow.style.rotate = new Rotate(Angle.Degrees(yawSource.eulerAngles.y));
            }
            else UIKit.Show(playerArrow, false);

            var boat = Refs.Boat;
            if (boat is UnityEngine.Object bo && bo == null) boat = null;
            if (boat != null && boat.Root != null)
            {
                UIKit.Show(boatMarker, true);
                SetUV(boatMarker, ToUV(boat.Root.position));
                boatMarker.style.rotate = new Rotate(Angle.Degrees(boat.Root.eulerAngles.y));
            }
            else UIKit.Show(boatMarker, false);

            UIKit.Show(waypointMarker, UIWaypoint.Active);
            if (UIWaypoint.Active) SetUV(waypointMarker, ToUV(UIWaypoint.Position));

            int n = 0;
            var all = Divers.All;
            for (int i = 0; i < all.Count; i++)
            {
                var t = all[i];
                if (t == null || t == local) continue;
                if (n >= crewDots.Count)
                {
                    var dot = UIKit.Dot(UIPalette.Turquoise, 18f);
                    UIKit.Border(dot, 3f, Color.white);
                    Place(dot, -9f, -9f);
                    markerLayer.Insert(0, dot);
                    crewDots.Add(dot);
                }
                var d = crewDots[n++];
                UIKit.Show(d, true);
                SetUV(d, ToUV(t.position));
            }
            for (int i = n; i < crewDots.Count; i++) UIKit.Show(crewDots[i], false);
        }
    }

    /// Painter2D ink of the treasure map canvas: rhumb lines + compass rose, dive-site circles (dashed while locked), the
    /// volcano's current ring until the wreck opens it, the fog lane (Toni's map), dashed „somewhere here“ circles for
    /// heard clues. Redrawn only when the canvas size or the content changes.
    public class UITreasureMapInk : VisualElement
    {
        static readonly Vector3 RoseAt = new Vector3(-760f, 0f, 760f);

        public UITreasureMapInk(UITreasureMap owner)
        {
            pickingMode = PickingMode.Ignore;
            UIKit.FillParent(this);
            generateVisualContent += Draw;
        }

        void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            if (r.width < 4f) return;
            var p = ctx.painter2D;
            float S = r.width;
            Vector2 M(Vector3 w) { var uv = UITreasureMap.ToUV(w); return new Vector2(r.x + uv.x * S, r.y + uv.y * S); }
            float Px(float meters) => meters / UITreasureMapArt.Size * S;
            var ink = UITreasureMapArt.Ink;

            // ---- rhumb lines from the compass rose
            Vector2 rose = M(RoseAt);
            p.strokeColor = UIPalette.A(ink, 0.11f);
            p.lineWidth = 1.2f;
            float len = S * 1.6f;
            for (int i = 0; i < 16; i++)
            {
                float a = i * Mathf.PI / 8f;
                p.BeginPath();
                p.MoveTo(rose);
                p.LineTo(rose + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * len);
                p.Stroke();
            }
            // ---- compass rose
            float rr = Mathf.Max(26f, S * 0.05f);
            p.strokeColor = UIPalette.A(ink, 0.55f);
            p.lineWidth = 1.5f;
            p.BeginPath(); p.Arc(rose, rr * 0.62f, Angle.Degrees(0f), Angle.Degrees(360f)); p.ClosePath(); p.Stroke();
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f - Mathf.PI * 0.5f;
                float l = i % 2 == 0 ? rr : rr * 0.55f;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Vector2 side = new Vector2(-dir.y, dir.x) * (rr * 0.13f);
                Vector2 tip = rose + dir * l;
                p.fillColor = i == 0 ? UIPalette.Rgb(0xB3261E) : (i % 2 == 0 ? UIPalette.A(ink, 0.8f) : UIPalette.Rgb(0xF4E7C6));
                p.BeginPath(); p.MoveTo(rose + side); p.LineTo(tip); p.LineTo(rose - side); p.ClosePath(); p.Fill();
                p.strokeColor = UIPalette.A(ink, 0.7f);
                p.lineWidth = 1f;
                p.Stroke();
            }

            // ---- dive sites
            bool fog = StoryFlags.Has(StoryKeys.FogMap);
            for (int pass = 0; pass < 2; pass++)
            {
                if (pass == 1) p.SetDashPattern(7f, 5f);
                foreach (var site in DiveSites.All)
                {
                    if (site.IsStorySite && !fog) continue;
                    bool unlocked = GameManager.I != null && Progression.IsSiteUnlocked(site);
                    if (unlocked != (pass == 0)) continue;
                    Vector2 c = M(site.center);
                    float rad = Mathf.Max(8f, Px(site.radius));
                    p.fillColor = UIPalette.A(site.mapColor, unlocked ? 0.2f : 0.07f);
                    p.BeginPath(); p.Arc(c, rad, Angle.Degrees(0f), Angle.Degrees(360f)); p.ClosePath(); p.Fill();
                    p.strokeColor = UIPalette.A(ink, unlocked ? 0.6f : 0.35f);
                    p.lineWidth = unlocked ? 2f : 1.5f;
                    p.BeginPath(); p.Arc(c, rad, Angle.Degrees(0f), Angle.Degrees(360f)); p.ClosePath(); p.Stroke();
                }
            }
            p.SetDashPattern(ReadOnlySpan<float>.Empty);

            // ---- the volcano's current ring while it is closed
            if (!VolcanoGate.Open)
            {
                p.strokeColor = UIPalette.A(UIPalette.Rgb(0x8E3B2E), 0.55f);
                p.lineWidth = 2f;
                p.SetDashPattern(4f, 6f);
                p.BeginPath(); p.Arc(M(WorldLayout.VolcanoIsland), Px(WorldLayout.VolcanoGateRadius), Angle.Degrees(0f), Angle.Degrees(360f)); p.ClosePath(); p.Stroke();
                p.SetDashPattern(ReadOnlySpan<float>.Empty);
            }

            // ---- the Fog Isle hides in a cloud until Toni's map, then the buoy lane appears
            if (fog)
            {
                Vector2 a = M(WorldLayout.FogLaneStart), b = M(WorldLayout.FogLaneEnd), isle = M(WorldLayout.FogIsland);
                var buoy = UIPalette.Rgb(0xD9A21B);
                p.strokeColor = buoy;
                p.lineWidth = 2.5f;
                p.SetDashPattern(6f, 5f);
                p.BeginPath(); p.MoveTo(a); p.LineTo(b); p.LineTo(isle); p.Stroke();
                p.SetDashPattern(ReadOnlySpan<float>.Empty);
                p.fillColor = buoy;
                for (int i = 0; i < 6; i++)
                {
                    var c = Vector2.Lerp(a, b, i / 5f);
                    p.BeginPath(); p.Arc(c, 3.5f, Angle.Degrees(0f), Angle.Degrees(360f)); p.ClosePath(); p.Fill();
                }
            }
            else
            {
                Vector2 isle = M(WorldLayout.FogIsland);
                float fr = Mathf.Max(18f, Px(110f));
                p.fillColor = new Color(0.97f, 0.95f, 0.9f, 0.9f);
                p.strokeColor = UIPalette.A(ink, 0.45f);
                p.lineWidth = 1.5f;
                for (int i = 0; i < 6; i++)
                {
                    float a = i * Mathf.PI / 3f;
                    Vector2 c = isle + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.6f) * fr * 0.55f;
                    p.BeginPath(); p.Arc(c, fr * 0.55f, Angle.Degrees(0f), Angle.Degrees(360f)); p.ClosePath(); p.Fill();
                }
                p.BeginPath(); p.Arc(isle, fr * 0.7f, Angle.Degrees(0f), Angle.Degrees(360f)); p.ClosePath(); p.Fill();
            }

            // ---- heard clues: dashed „somewhere here“ circles
            var clues = MapClues.Current;
            if (clues.Count > 0)
            {
                p.strokeColor = UIPalette.A(UIPalette.Rgb(0x8E3B2E), 0.75f);
                p.lineWidth = 2f;
                p.SetDashPattern(5f, 4f);
                for (int i = 0; i < clues.Count; i++)
                {
                    float rad = Mathf.Max(14f, Px(clues[i].radius));
                    p.BeginPath(); p.Arc(M(clues[i].pos), rad, Angle.Degrees(0f), Angle.Degrees(360f)); p.ClosePath(); p.Stroke();
                }
                p.SetDashPattern(ReadOnlySpan<float>.Empty);
            }
        }
    }

    /// Heard clues (NpcClues: told + not found yet) as approximate places on the map – a dashed circle with „?“, never the
    /// exact spot. Positions from the props / sea content (map pieces, wrecks, chests, curiosities) or the known places.
    public static class MapClues
    {
        public struct Clue { public string key; public Vector3 pos; public float radius; }

        static readonly List<Clue> current = new List<Clue>();
        static readonly List<string> flags = new List<string>();
        static int sig = int.MinValue;
        static float stamp = -10f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { current.Clear(); flags.Clear(); sig = int.MinValue; stamp = -10f; }

        /// Clue flags + found state (polled every 0.4 s by the map).
        public static int Signature()
        {
            Refresh();
            return sig;
        }

        public static List<Clue> Current
        {
            get { Refresh(); return current; }
        }

        static void Refresh()
        {
            if (Time.unscaledTime - stamp < 0.35f) return;
            stamp = Time.unscaledTime;
            var gm = GameManager.I;
            if (gm == null || gm.State == null) { if (current.Count > 0) { current.Clear(); sig = 0; } return; }
            List<NpcClues.Entry> all;
            try { all = NpcClues.All; }
            catch (Exception) { return; }
            int h = 23;
            int n = 0;
            if (flags.Count != all.Count)
            {
                flags.Clear();
                for (int i = 0; i < all.Count; i++) flags.Add(NpcClues.FlagOf(all[i]));
            }
            for (int i = 0; i < all.Count; i++)
            {
                var e = all[i];
                if (!StoryFlags.Has(flags[i])) continue;
                bool found;
                try { found = e.found(); } catch (Exception) { found = true; }
                if (found) continue;
                if (!Resolve(e.key, out Vector3 pos, out float rad)) continue;
                unchecked { h = h * 31 + e.key.GetHashCode(); }
                if (n < current.Count) current[n] = new Clue { key = e.key, pos = pos, radius = rad };
                else current.Add(new Clue { key = e.key, pos = pos, radius = rad });
                n++;
            }
            if (current.Count > n) current.RemoveRange(n, current.Count - n);
            sig = h;
        }

        /// Approximate place of a clue (offset a little, so the circle does not point at the exact spot).
        static bool Resolve(string key, out Vector3 pos, out float radius)
        {
            pos = Vector3.zero;
            radius = 30f;
            if (string.IsNullOrEmpty(key)) return false;
            if (key.StartsWith("map_p", StringComparison.Ordinal) && key.Length > 5 && int.TryParse(key.Substring(5), out int pi)
                && pi >= 0 && pi < MapPieces.Count && MapPieces.Spots[pi].valid)
            {
                pos = MapPieces.Spots[pi].position;
                radius = 22f;
                return Jitter(key, ref pos, 9f);
            }
            if (SeaContent.Placed.TryGetValue(key, out pos)) return Jitter(key, ref pos, 12f);
            switch (key)
            {
                case "chest_lagoon": return Placed("chest_lagoon_raft", key, out pos);
                case "chest_reef": return Placed("chest_hr_coral", key, out pos, 40f, ref radius);
                case "chest_kelp": return Placed("chest_kelp_a", key, out pos, 50f, ref radius);
                case "treasure":
                    pos = MapPieces.SecretSpot;
                    radius = 26f;
                    return Jitter(key, ref pos, 8f);
                case "lighthouse_top":
                case "lighthouse_cellar": pos = WorldLayout.Lighthouse; radius = 18f; return true;
                case "ravine_piggy": pos = WorldLayout.Ravine; radius = 20f; return true;
                case "waterfall": pos = WorldLayout.WaterfallPool; radius = 20f; return true;
                case "village_loft":
                case "tide_page": pos = WorldLayout.HermitCamp; radius = 18f; return MapDiscovery.CampKnown;
                case "kelp_carving":
                case "meme_dance":
                case "meme_gnome": return Site("kelp_forest", out pos, ref radius);
                case "big_mo":
                case "meme_dino": pos = WorldLayout.VolcanoAbyss; radius = 60f; return true;
                case "fossils": pos = WorldLayout.VolcanoIsland; radius = 90f; return true;
                case "blue_buoy": return Site("blue_drop", out pos, ref radius);
                case "fog_archive": pos = WorldLayout.FogIsland; radius = 50f; return true;
            }
            return false;
        }

        static bool Placed(string id, string key, out Vector3 pos)
        {
            float r = 30f;
            return Placed(id, key, out pos, 30f, ref r);
        }

        static bool Placed(string id, string key, out Vector3 pos, float rad, ref float radius)
        {
            if (!SeaContent.Placed.TryGetValue(id, out pos)) return false;
            radius = rad;
            return Jitter(key, ref pos, rad * 0.35f);
        }

        static bool Site(string id, out Vector3 pos, ref float radius)
        {
            pos = Vector3.zero;
            var s = DiveSites.Get(id);
            if (s == null) return false;
            pos = s.center;
            radius = Mathf.Max(40f, s.radius * 0.8f);
            return true;
        }

        static bool Jitter(string key, ref Vector3 pos, float m)
        {
            int h = key.GetHashCode();
            float a = (h & 1023) / 1023f * Mathf.PI * 2f;
            pos += new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * m * 0.6f;
            return true;
        }
    }

    /// Round 3 (GDD §20 Q31): the shared map pins as small markers in the HUD (icon + distance), like the waypoint.
    /// Pins farther than MaxDistance or behind a menu are hidden; at most MaxShown nearest.
    public class UIHudPins
    {
        const int MaxShown = 8;
        const float MaxDistance = 900f;

        class Marker
        {
            public VisualElement root;
            public UIHudGlyph icon;
            public Label dist;
            public int distKey = -1, icon0 = -1;
        }

        readonly VisualElement layer;
        readonly Marker[] markers = new Marker[MaxShown];
        readonly int[] order = new int[MapPins.Max];
        readonly float[] dists = new float[MapPins.Max];
        int shown;

        public UIHudPins(VisualElement parent)
        {
            layer = new VisualElement { name = "sb-hud-pins", pickingMode = PickingMode.Ignore };
            UIKit.FillParent(layer);
            parent.Add(layer);
            for (int i = 0; i < markers.Length; i++)
            {
                var m = new Marker { root = UIKit.Col() };
                m.root.pickingMode = PickingMode.Ignore;
                m.root.style.position = Position.Absolute;
                m.root.style.alignItems = Align.Center;
                m.root.style.width = 80f;
                m.root.style.marginLeft = -40f;
                m.root.style.marginTop = -14f;
                m.icon = new UIHudGlyph(UIGlyphs.Flag, Color.white, 24f);
                m.root.Add(m.icon);
                m.dist = UIKit.HudText("", 15f);
                m.root.Add(m.dist);
                m.root.style.display = DisplayStyle.None;
                layer.Add(m.root);
                markers[i] = m;
            }
        }

        public int Shown => shown;

        public void Tick(bool visible)
        {
            int n = 0;
            Camera cam = null;
            Vector3 from = Vector3.zero;
            var pins = MapPins.All;
            if (visible && pins.Count > 0)
            {
                var p = Refs.Player;
                if (p is UnityEngine.Object po && po == null) p = null;
                cam = p != null ? p.View : null;
                if (cam == null) cam = Camera.main;
                if (cam != null && p != null && p.Root != null)
                {
                    from = p.Root.position;
                    // nearest first (insertion sort over ≤ 24 pins, no allocations)
                    int c = 0;
                    for (int i = 0; i < pins.Count && c < order.Length; i++)
                    {
                        var pin = pins[i];
                        if (pin == null) continue;
                        float d = Vector2.Distance(new Vector2(from.x, from.z), new Vector2(pin.x, pin.z));
                        if (d > MaxDistance || d < 6f) continue;
                        int k = c++;
                        while (k > 0 && dists[k - 1] > d) { dists[k] = dists[k - 1]; order[k] = order[k - 1]; k--; }
                        dists[k] = d;
                        order[k] = i;
                    }
                    n = Mathf.Min(c, MaxShown);
                }
            }
            float w = layer.layout.width, h = layer.layout.height;
            if (w < 2f || h < 2f || Screen.width < 2 || Screen.height < 2) n = 0;
            for (int i = 0; i < markers.Length; i++)
            {
                var m = markers[i];
                bool on = i < n;
                if (on)
                {
                    var pin = pins[order[i]];
                    Vector3 target = new Vector3(pin.x, Waves.SeaLevel + 1.2f, pin.z);
                    float gy = WorldInfo.GroundHeight(target);
                    if (gy > target.y - 1f) target.y = gy + 1.2f;
                    Vector3 sp = cam.WorldToScreenPoint(target);
                    if (sp.z < 0f) on = false;   // behind: only the waypoint gets an edge marker
                    else
                    {
                        float x = sp.x / Screen.width * w, y = (1f - sp.y / Screen.height) * h;
                        // final test: a pin far below the view (e.g. the sea seen from the lighthouse) vanished – it now sits
                        // at the lower / upper edge of the free band (never over the hotbar)
                        y = Mathf.Clamp(y, 70f, Mathf.Max(70f, h - 200f));
                        if (x < 40f || x > w - 40f) on = false;
                        else
                        {
                            m.root.style.left = x;
                            m.root.style.top = y;
                            if (pin.icon != m.icon0)
                            {
                                m.icon0 = pin.icon;
                                m.icon.SetDraw(MapPins.Glyph(pin.icon));
                                m.icon.SetColor(Color.Lerp(MapPins.Color(pin.icon), Color.white, 0.25f));
                            }
                            float d = dists[i];
                            int key = d >= 1000f ? Mathf.RoundToInt(d / 100f) * 100 : Mathf.RoundToInt(d / 5f) * 5;
                            if (key != m.distKey) { m.distKey = key; m.dist.text = UIKit.Distance(key); }
                        }
                    }
                }
                if (on != (m.root.style.display.value == DisplayStyle.Flex)) m.root.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            }
            shown = 0;
            for (int i = 0; i < markers.Length; i++) if (markers[i].root.style.display.value == DisplayStyle.Flex) shown++;
        }
    }
}
