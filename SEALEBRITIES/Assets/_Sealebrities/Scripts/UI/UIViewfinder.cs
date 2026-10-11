using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Camcorder viewfinder (K.2 "just keep recording"): drawn from ViewfinderInfo (published by the Filming module):
    ///  - small corner brackets around every detected animal (max 8), colour = size tier; a species not yet in the
    ///    Sealog gets a pulsing yellow bracket with „?“;
    ///  - top-left „● REC 0:42 · 🐟 3“ (animals in frame), top-right tape + battery, bottom-left zoom + date stamp;
    ///  - the Sealog frame (central 40 %, thin white corners) turns green with „Foto fürs Sealog!“ (+ „NEU!“) when a
    ///    valid photo subject is inside;
    ///  - one icon + 1–2 words hint (ShotHint): „Keine Tiere“, „Licht!“, „Näher“, „Neue Art!“.
    /// Recording from the hip: a mini line „● REC · 3“ above the hotbar. The selfie view (K.4) gets a rounded frame
    /// instead of the camcorder frame (UISelfieFrame). Only core contracts are read (IHandCamera, ViewfinderInfo,
    /// SelfieState) – no Filming internals.
    public class UIViewfinder
    {
        class Bracket
        {
            public VisualElement box;
            public VisualElement[] corners = new VisualElement[4];
            public Label tag;
            public int key = int.MinValue;
            public int colorKey = -1;
            public bool shown;
        }

        static readonly Color W = new Color(1f, 1f, 1f, 0.92f);
        static readonly Color NewColor = UIPalette.Rgb(0xF2C94C);
        static readonly Color SealogGreen = UIPalette.Rgb(0x6FE38A);

        readonly VisualElement root, frame, recDot, batteryFill, smallRec, smallRecDot, cross, sealogFrame, sealogInfo, hintRow, bracketLayer;
        readonly VisualElement[] sealogCorners = new VisualElement[4];
        readonly Label recLabel, recCount, tapeLabel, zoomLabel, stampLabel, smallRecLabel, sealogTitle, sealogName, sealogNew, hintLabel, legacyLabel;
        readonly UIHudGlyph recFish, smallFish, hintIcon;
        readonly UIBar zoomBar;
        readonly Bracket[] brackets = new Bracket[ViewfinderInfo.MaxBrackets];
        public readonly UISelfieFrame Selfie;

        bool shown, smallShown, wasRecording, sealogOn;
        float recSeconds, blink;
        int recSec = -1, tapeSec = -1, zoomKey = -1, stampKey = -1, batteryKey = -1, smallSec = -1, countKey = -1, hintKey = -1, smallCount = -1;
        SpeciesDef sealogSubject;
        bool sealogNewShown;
        int textVersion = -1;
        SpeciesDef legacyTarget;

        public UIViewfinder(VisualElement layer)
        {
            root = new VisualElement { name = "sb-viewfinder" };
            UIKit.FillParent(root);
            layer.Add(root);

            // brackets around the detected animals (under everything else)
            bracketLayer = new VisualElement { pickingMode = PickingMode.Ignore };
            UIKit.FillParent(bracketLayer);
            root.Add(bracketLayer);
            for (int i = 0; i < brackets.Length; i++) brackets[i] = MakeBracket();

            // camcorder frame (4 % margin)
            frame = new VisualElement();
            frame.style.position = Position.Absolute;
            frame.style.left = Length.Percent(4f); frame.style.right = Length.Percent(4f);
            frame.style.top = Length.Percent(5f); frame.style.bottom = Length.Percent(5f);
            root.Add(frame);
            AddCorner(frame, true, true, W, 64f, 5f); AddCorner(frame, false, true, W, 64f, 5f);
            AddCorner(frame, true, false, W, 64f, 5f); AddCorner(frame, false, false, W, 64f, 5f);

            // top-left: ● REC 0:42 · 🐟 3 – right of the dive HUD block (air ring, depth, site), which it covered
            // when it sat in the corner (playtest round 2)
            var recRow = UIKit.Row();
            UIKit.Abs(recRow, 210f, 22f, null, null);
            recDot = UIKit.Dot(UIPalette.Danger, 18f);
            UIKit.Border(recDot, 2f, Color.white);
            recDot.style.marginRight = 10f;
            recRow.Add(recDot);
            recLabel = Mono("STBY", 28f);
            recRow.Add(recLabel);
            recFish = new UIHudGlyph(UIGlyphs.Fish, Color.white, 28f);
            recFish.style.marginLeft = 16f;
            recRow.Add(recFish);
            recCount = Mono("0", 28f);
            recCount.style.marginLeft = 6f;
            recRow.Add(recCount);
            frame.Add(recRow);

            // top-right: tape + battery
            var tr = UIKit.Row();
            UIKit.Abs(tr, null, 22f, 28f, null);
            tapeLabel = Mono("", 22f);
            tapeLabel.style.marginRight = 16f;
            tr.Add(tapeLabel);
            var battery = UIKit.Row();
            var body = UIKit.Box();
            UIKit.Size(body, 40f, 20f);
            UIKit.Border(body, 2f, W);
            UIKit.Radius(body, 3f);
            UIKit.Pad(body, 2f);
            batteryFill = UIKit.Box();
            batteryFill.style.height = Length.Percent(100f);
            batteryFill.style.width = Length.Percent(100f);
            batteryFill.style.backgroundColor = W;
            body.Add(batteryFill);
            battery.Add(body);
            var nub = UIKit.Box();
            UIKit.Size(nub, 4f, 8f);
            nub.style.backgroundColor = W;
            battery.Add(nub);
            tr.Add(battery);
            frame.Add(tr);

            // bottom-left: zoom + date stamp
            var bl = UIKit.Col();
            UIKit.Abs(bl, 28f, null, null, 22f);
            zoomLabel = Mono("x1.0", 26f);
            bl.Add(zoomLabel);
            zoomBar = new UIBar(5f, W, new Color(1f, 1f, 1f, 0.28f));
            zoomBar.style.width = 140f;
            zoomBar.style.marginTop = 6f;
            bl.Add(zoomBar);
            stampLabel = Mono("", 19f);
            stampLabel.style.marginTop = 12f;
            bl.Add(stampLabel);
            frame.Add(bl);

            // Sealog frame: central 40 %
            sealogFrame = new VisualElement { pickingMode = PickingMode.Ignore };
            sealogFrame.style.position = Position.Absolute;
            sealogFrame.style.left = Length.Percent(30f); sealogFrame.style.right = Length.Percent(30f);
            sealogFrame.style.top = Length.Percent(30f); sealogFrame.style.bottom = Length.Percent(30f);
            root.Add(sealogFrame);
            sealogCorners[0] = AddCorner(sealogFrame, true, true, W, 34f, 2.5f);
            sealogCorners[1] = AddCorner(sealogFrame, false, true, W, 34f, 2.5f);
            sealogCorners[2] = AddCorner(sealogFrame, true, false, W, 34f, 2.5f);
            sealogCorners[3] = AddCorner(sealogFrame, false, false, W, 34f, 2.5f);
            // above the frame: below it the aimed camera model (art bible: up to 36 % of the screen height) covered
            // „Foto fürs Sealog!“ (playtest round 2)
            sealogInfo = UIKit.Col();
            UIKit.Abs(sealogInfo, 0f, null, 0f, null);
            sealogInfo.style.bottom = Length.Percent(100f);
            sealogInfo.style.marginBottom = 12f;
            sealogInfo.style.alignItems = Align.Center;
            sealogFrame.Add(sealogInfo);
            var st = UIKit.Row();
            st.Add(new UIHudGlyph(UIGlyphs.Book, SealogGreen, 26f));
            sealogTitle = UIKit.HudText("", 24f, SealogGreen);
            sealogTitle.style.marginLeft = 8f;
            st.Add(sealogTitle);
            sealogNew = UIKit.Pill("", NewColor, UIPalette.Ink, 17f);
            sealogNew.style.marginLeft = 10f;
            st.Add(sealogNew);
            sealogInfo.Add(st);
            sealogName = UIKit.HudText("", 22f);
            sealogName.style.marginTop = 2f;
            sealogInfo.Add(sealogName);
            sealogInfo.style.display = DisplayStyle.None;

            // crosshair (small)
            cross = UIKit.Box();
            cross.style.position = Position.Absolute;
            cross.style.left = Length.Percent(50f); cross.style.top = Length.Percent(50f);
            UIKit.Size(cross, 0f, 0f);
            root.Add(cross);
            AddLine(cross, -22f, -1.5f, 14f, 3f, W); AddLine(cross, 8f, -1.5f, 14f, 3f, W);
            AddLine(cross, -1.5f, -22f, 3f, 14f, W); AddLine(cross, -1.5f, 8f, 3f, 14f, W);

            // hint: icon + 1–2 words, upper centre above the Sealog label (the lower centre belongs to the aimed
            // camera model, which hid the hint – playtest round 2)
            hintRow = UIKit.Row();
            hintRow.style.position = Position.Absolute;
            hintRow.style.left = 0f; hintRow.style.right = 0f;
            hintRow.style.top = Length.Percent(13f);
            hintRow.style.justifyContent = Justify.Center;
            hintIcon = new UIHudGlyph(UIGlyphs.Fish, Color.white, 30f);
            hintIcon.style.marginRight = 10f;
            hintRow.Add(hintIcon);
            hintLabel = UIKit.HudText("", 26f);
            hintRow.Add(hintLabel);
            root.Add(hintRow);
            legacyLabel = UIKit.HudText("", 22f);
            legacyLabel.style.position = Position.Absolute;
            legacyLabel.style.left = 0f; legacyLabel.style.right = 0f;
            legacyLabel.style.bottom = Length.Percent(25.5f);
            legacyLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            root.Add(legacyLabel);

            // hip recording: mini line above the hotbar
            smallRec = UIKit.Row();
            UIKit.Abs(smallRec, 0f, null, 0f, 214f);
            smallRec.style.justifyContent = Justify.Center;
            smallRecDot = UIKit.Dot(UIPalette.Danger, 14f);
            UIKit.Border(smallRecDot, 2f, Color.white);
            smallRecDot.style.marginRight = 8f;
            smallRec.Add(smallRecDot);
            smallRecLabel = UIKit.HudText("REC", 20f);
            smallRec.Add(smallRecLabel);
            smallFish = new UIHudGlyph(UIGlyphs.Fish, Color.white, 22f);
            smallFish.style.marginLeft = 10f;
            smallRec.Add(smallFish);
            smallRec.style.display = DisplayStyle.None;
            root.Add(smallRec);

            Selfie = new UISelfieFrame(root);

            UIKit.IgnorePicking(root);
            root.style.display = DisplayStyle.None;
            ShowFrameParts(false);
            RefreshTexts();
        }

        static Label Mono(string text, float size)
        {
            var l = UIKit.HudText(text, size, new Color(1f, 1f, 1f, 0.95f));
            l.style.letterSpacing = 2f;
            return l;
        }

        static VisualElement AddCorner(VisualElement parent, bool left, bool top, Color c, float size, float width)
        {
            var b = UIKit.Box();
            b.pickingMode = PickingMode.Ignore;
            b.style.position = Position.Absolute;
            UIKit.Size(b, size, size);
            if (left) b.style.left = 0; else b.style.right = 0;
            if (top) b.style.top = 0; else b.style.bottom = 0;
            if (left) { b.style.borderLeftWidth = width; b.style.borderLeftColor = c; } else { b.style.borderRightWidth = width; b.style.borderRightColor = c; }
            if (top) { b.style.borderTopWidth = width; b.style.borderTopColor = c; } else { b.style.borderBottomWidth = width; b.style.borderBottomColor = c; }
            parent.Add(b);
            return b;
        }

        static void SetCornerColor(VisualElement b, Color c)
        {
            b.style.borderLeftColor = c; b.style.borderRightColor = c; b.style.borderTopColor = c; b.style.borderBottomColor = c;
        }

        Bracket MakeBracket()
        {
            var br = new Bracket { box = new VisualElement { pickingMode = PickingMode.Ignore } };
            br.box.style.position = Position.Absolute;
            br.corners[0] = AddCorner(br.box, true, true, W, 16f, 3f);
            br.corners[1] = AddCorner(br.box, false, true, W, 16f, 3f);
            br.corners[2] = AddCorner(br.box, true, false, W, 16f, 3f);
            br.corners[3] = AddCorner(br.box, false, false, W, 16f, 3f);
            br.tag = UIKit.HudText("?", 22f, NewColor);
            br.tag.style.position = Position.Absolute;
            br.tag.style.right = -18f; br.tag.style.top = -22f;
            br.box.Add(br.tag);
            br.box.style.display = DisplayStyle.None;
            bracketLayer.Add(br.box);
            return br;
        }

        static void AddLine(VisualElement parent, float x, float y, float w, float h, Color c)
        {
            var l = UIKit.Box();
            l.style.position = Position.Absolute;
            l.style.left = x; l.style.top = y;
            UIKit.Size(l, w, h);
            l.style.backgroundColor = c;
            parent.Add(l);
        }

        void ShowFrameParts(bool show)
        {
            UIKit.Show(frame, show);
            UIKit.Show(cross, show);
            UIKit.Show(sealogFrame, show);
            // the hint („Neue Art!“, „Näher“ …) belongs to the viewfinder: it stayed on screen after leaving the aim
            // while recording from the hip, on top of the REC line and the subtitles (playtest round 2)
            if (!show) UIKit.Show(hintRow, false);
        }

        public void RefreshTexts()
        {
            recSec = -1; tapeSec = -1; zoomKey = -1; stampKey = -1; smallSec = -1; countKey = -1; hintKey = -1; smallCount = -1;
            textVersion = -1;
            sealogSubject = null;
            legacyTarget = null;
            Selfie.RefreshTexts();
        }

        public void OnRecordingChanged(bool recording)
        {
            if (recording && !wasRecording) recSeconds = 0f;
            wasRecording = recording;
        }

        public bool IsAiming
        {
            get
            {
                var cam = Refs.HandCamera;
                if (cam is Object o && o == null) return false;
                return cam != null && cam.IsAiming && !UIState.BlocksGameplay && UIKit.InGame;
            }
        }

        public void Tick(float dt)
        {
            var cam = Refs.HandCamera;
            if (cam is Object co && co == null) cam = null;
            bool inGame = UIKit.InGame && !UIState.BlocksGameplay;
            bool recording = cam != null && cam.IsRecording && inGame;
            if (recording && !wasRecording) recSeconds = 0f;
            wasRecording = cam != null && cam.IsRecording;
            if (recording) recSeconds += Time.deltaTime;
            blink += dt;
            bool blinkOn = Mathf.Repeat(blink, 1f) < 0.6f;

            bool selfie = inGame && (SelfieState.Active || UIState.SelfieOpen);
            bool aim = cam != null && cam.IsAiming && inGame && !selfie;
            bool small = recording && !aim && !selfie;
            bool any = aim || small || selfie;
            UIKit.Show(root, any);
            if (aim != shown)
            {
                shown = aim;
                ShowFrameParts(aim);
                recSec = -1; tapeSec = -1; zoomKey = -1; stampKey = -1; batteryKey = -1; countKey = -1; hintKey = -1;
                sealogSubject = null; sealogOn = false;
                SetSealog(null);
                legacyTarget = null;
                legacyLabel.text = "";
            }
            if (small != smallShown)
            {
                smallShown = small;
                UIKit.Show(smallRec, small);
                smallSec = -1; smallCount = -1;
            }
            Selfie.Tick(dt, selfie, recording, recSeconds, blinkOn);
            TickBrackets(aim || selfie);
            if (small)
            {
                smallRecDot.style.opacity = blinkOn ? 1f : 0.15f;
                int s = Mathf.FloorToInt(recSeconds);
                int n = Mathf.Clamp(ViewfinderInfo.DetectedCount, 0, 99);
                if (s != smallSec || n != smallCount)
                {
                    smallSec = s; smallCount = n;
                    smallRecLabel.text = n > 0 ? "REC " + UIKit.Clock(s) + "  · " + n : "REC " + UIKit.Clock(s);
                    UIKit.Show(smallFish, n > 0);
                }
            }
            if (!aim) return;

            // REC / standby + animals in frame
            recDot.style.opacity = recording ? (blinkOn ? 1f : 0.15f) : 0f;
            int rs = recording ? Mathf.FloorToInt(recSeconds) : -2;
            if (rs != recSec)
            {
                recSec = rs;
                recLabel.text = recording ? "REC " + UIKit.Clock(rs) : "STBY";
                recLabel.style.color = recording ? UIPalette.Danger : new Color(1f, 1f, 1f, 0.95f);
            }
            int cnt = Mathf.Clamp(ViewfinderInfo.DetectedCount, 0, 99);
            if (cnt != countKey)
            {
                countKey = cnt;
                recCount.text = cnt.ToString();
                recFish.SetColor(cnt > 0 ? Color.white : new Color(1f, 1f, 1f, 0.45f));
                recCount.style.color = cnt > 0 ? Color.white : new Color(1f, 1f, 1f, 0.45f);
            }

            // tape + battery
            float total = Mathf.Max(1f, cam.TapeSecondsTotal);
            float left = Mathf.Max(0f, cam.TapeSecondsLeft);
            if (float.IsNaN(left)) left = 0f;
            int ts = Mathf.CeilToInt(left);
            if (ts != tapeSec)
            {
                tapeSec = ts;
                tapeLabel.text = Loc.Tr("BAND ", "TAPE ") + UIKit.Clock(ts);
                tapeLabel.style.color = left / total < 0.15f ? UIPalette.Danger : new Color(1f, 1f, 1f, 0.95f);
            }
            int bk = Mathf.CeilToInt(left / total * 4f);
            if (bk != batteryKey)
            {
                batteryKey = bk;
                batteryFill.style.width = Length.Percent(Mathf.Clamp(bk, 0, 4) * 25f);
                batteryFill.style.backgroundColor = bk <= 1 ? UIPalette.Danger : new Color(1f, 1f, 1f, 0.88f);
            }

            // zoom
            float z = float.IsNaN(cam.Zoom) ? 1f : cam.Zoom;
            int zk = Mathf.RoundToInt(z * 10f);
            if (zk != zoomKey)
            {
                zoomKey = zk;
                zoomLabel.text = "x" + (zk / 10f).ToString("0.0", Loc.Culture);
                float maxZoom = GameManager.I != null ? Mathf.Max(1.01f, GameManager.I.Gear.maxZoom) : 2f;
                zoomBar.SetValue(Mathf.InverseLerp(1f, maxZoom, z));
            }

            // camcorder date stamp
            var gm = GameManager.I;
            if (gm != null)
            {
                int sk = gm.State.day * 2000 + Mathf.FloorToInt(Mathf.Repeat(gm.State.timeOfDay, 1f) * 1440f);
                if (sk != stampKey)
                {
                    stampKey = sk;
                    stampLabel.text = Loc.Tr("TAG ", "DAY ") + gm.State.day.ToString("00") + "  " + gm.ClockString;
                }
            }

            TickSealog(blinkOn);
            TickHint(cam);
        }

        // ------------------------------------------------------------------ brackets
        void TickBrackets(bool active)
        {
            int n = active ? Mathf.Clamp(ViewfinderInfo.BracketCount, 0, brackets.Length) : 0;
            float w = root.layout.width, h = root.layout.height;
            if (!(w > 2f) || !(h > 2f)) n = 0;
            float pulse = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 4f));
            for (int i = 0; i < brackets.Length; i++)
            {
                var b = brackets[i];
                bool on = i < n;
                if (on != b.shown) { b.shown = on; b.box.style.display = on ? DisplayStyle.Flex : DisplayStyle.None; b.key = int.MinValue; }
                if (!on) continue;
                var r = ViewfinderInfo.Brackets[i];
                if (float.IsNaN(r.x) || float.IsNaN(r.y) || float.IsNaN(r.z) || float.IsNaN(r.w)) { b.box.style.display = DisplayStyle.None; b.shown = false; continue; }
                float x0 = Mathf.Clamp01(Mathf.Min(r.x, r.z)) * w, x1 = Mathf.Clamp01(Mathf.Max(r.x, r.z)) * w;
                float y0 = (1f - Mathf.Clamp01(Mathf.Max(r.y, r.w))) * h, y1 = (1f - Mathf.Clamp01(Mathf.Min(r.y, r.w))) * h;
                // a little air around the animal, never smaller than 36 px
                float pad = 6f;
                float bw = Mathf.Max(36f, x1 - x0 + pad * 2f), bh = Mathf.Max(36f, y1 - y0 + pad * 2f);
                float cx = (x0 + x1) * 0.5f, cy = (y0 + y1) * 0.5f;
                int key = Mathf.RoundToInt(cx) * 7919 ^ Mathf.RoundToInt(cy) * 104729 ^ Mathf.RoundToInt(bw) * 31 ^ Mathf.RoundToInt(bh);
                if (key != b.key)
                {
                    b.key = key;
                    b.box.style.left = cx - bw * 0.5f;
                    b.box.style.top = cy - bh * 0.5f;
                    b.box.style.width = bw;
                    b.box.style.height = bh;
                }
                bool isNew = ViewfinderInfo.BracketNew[i];
                int tier = ViewfinderInfo.BracketTier[i];
                int ck = isNew ? 100 : tier;
                if (ck != b.colorKey)
                {
                    b.colorKey = ck;
                    Color c = isNew ? NewColor : tier == (int)SizeTier.Normal || tier < 0 || tier > 4 ? W : Progression.TierColor((SizeTier)tier);
                    for (int k = 0; k < 4; k++) SetCornerColor(b.corners[k], c);
                    UIKit.Show(b.tag, isNew);
                }
                b.box.style.opacity = isNew ? pulse : 1f;
            }
        }

        // ------------------------------------------------------------------ Sealog frame
        void SetSealog(SpeciesDef s)
        {
            bool on = s != null;
            if (on != sealogOn || s != sealogSubject)
            {
                sealogOn = on;
                sealogSubject = s;
                Color c = on ? SealogGreen : W;
                for (int i = 0; i < 4; i++)
                {
                    SetCornerColor(sealogCorners[i], c);
                    float wdt = on ? 4f : 2.5f;
                    var e = sealogCorners[i];
                    if (e.style.borderLeftWidth.value > 0f) e.style.borderLeftWidth = wdt;
                    if (e.style.borderRightWidth.value > 0f) e.style.borderRightWidth = wdt;
                    if (e.style.borderTopWidth.value > 0f) e.style.borderTopWidth = wdt;
                    if (e.style.borderBottomWidth.value > 0f) e.style.borderBottomWidth = wdt;
                }
                UIKit.Show(sealogInfo, on);
                if (on)
                {
                    bool known = Journal.IsDiscovered(s.id);
                    sealogNewShown = !known;
                    sealogTitle.text = Loc.Tr("Foto fürs Sealog!", "Photo for the Sealog!");
                    sealogNew.text = Loc.Tr("NEU!", "NEW!");
                    UIKit.Show(sealogNew, !known);
                    sealogName.text = known ? s.Name : "?";
                }
            }
        }

        void TickSealog(bool blinkOn)
        {
            SetSealog(ViewfinderInfo.SealogSubject);
            if (sealogOn && sealogNewShown) sealogNew.style.opacity = blinkOn ? 1f : 0.55f;
        }

        // ------------------------------------------------------------------ hint (icon + 1–2 words)
        void TickHint(IHandCamera cam)
        {
            var h = ViewfinderInfo.Hint;
            int ver = GameInput.BindingsVersion * 31 + (int)Loc.Current;
            int key = (int)h + 100 * ver;
            if (key != hintKey)
            {
                hintKey = key;
                string text = "";
                UIGlyph.DrawFn icon = null;
                Color c = Color.white;
                switch (h)
                {
                    case ShotHint.NoAnimals: icon = UIGlyphs.Fish; c = new Color(1f, 1f, 1f, 0.6f); text = Loc.Tr("Keine Tiere", "No animals"); break;
                    case ShotHint.TooDark: icon = UIGlyphs.Lamp; c = UIPalette.Sun; text = "[" + GameInput.BindingLabel(GameAction.LightToggle) + "] " + Loc.Tr("Licht!", "Light!"); break;
                    case ShotHint.Closer: icon = UIGlyphs.Arrow; c = UIPalette.Sun; text = Loc.Tr("Näher", "Closer"); break;
                    case ShotHint.New: icon = UIGlyphs.Sparkle; c = NewColor; text = Loc.Tr("Neue Art!", "New species!"); break;
                }
                hintLabel.text = text;
                hintLabel.style.color = c;
                if (icon != null) { hintIcon.SetDraw(icon); hintIcon.SetColor(c); }
                UIKit.Show(hintRow, text.Length > 0);
            }
            // legacy filming (before K.2): no detection data → the old single target name
            var target = ViewfinderInfo.BracketCount == 0 && ViewfinderInfo.SealogSubject == null ? cam.CurrentTarget : null;
            if (target != legacyTarget)
            {
                legacyTarget = target;
                legacyLabel.text = target != null ? (Journal.IsDiscovered(target.id) ? target.Name : "???") : "";
            }
        }
    }

    /// Selfie view frame (K.4): a rounded white frame instead of the camcorder frame, REC + animals in frame, the chosen
    /// expression and the hint line „H – zurück · 1–6 Gesicht“ (keys from the current bindings).
    public class UISelfieFrame
    {
        readonly VisualElement root, recDot;
        readonly Label recLabel, exprLabel, backKey, faceKey, backText, faceText;
        int recSec = -1, count = -1, expr = -1, bindVersion = -1;
        bool shown;

        public UISelfieFrame(VisualElement parent)
        {
            root = new VisualElement { name = "sb-selfie", pickingMode = PickingMode.Ignore };
            UIKit.FillParent(root);
            parent.Add(root);
            var frame = new VisualElement { pickingMode = PickingMode.Ignore };
            frame.style.position = Position.Absolute;
            frame.style.left = Length.Percent(6f); frame.style.right = Length.Percent(6f);
            frame.style.top = Length.Percent(7f); frame.style.bottom = Length.Percent(17f);
            UIKit.Border(frame, 6f, new Color(1f, 1f, 1f, 0.9f));
            UIKit.Radius(frame, 46f);
            root.Add(frame);

            // top centre of the frame (the quest line is hidden in the selfie view); top-left collided with the
            // air ring / depth / site HUD (playtest round 2)
            var top = UIKit.Row();
            UIKit.Abs(top, 0f, 26f, 0f, null);
            top.style.justifyContent = Justify.Center;
            recDot = UIKit.Dot(UIPalette.Danger, 16f);
            UIKit.Border(recDot, 2f, Color.white);
            recDot.style.marginRight = 8f;
            top.Add(recDot);
            recLabel = UIKit.HudText("", 24f);
            top.Add(recLabel);
            frame.Add(top);

            exprLabel = UIKit.Pill("", new Color(1f, 1f, 1f, 0.92f), UIPalette.Ink, 20f);
            exprLabel.style.position = Position.Absolute;
            exprLabel.style.right = 30f; exprLabel.style.top = 24f;
            frame.Add(exprLabel);

            var hint = UIKit.Row();
            UIKit.Abs(hint, 0f, null, 0f, 140f);
            hint.style.justifyContent = Justify.Center;
            backKey = UIKit.HudKeyCap("H", 32f);
            hint.Add(backKey);
            backText = UIKit.HudText("", 22f);
            backText.style.marginLeft = 8f;
            backText.style.marginRight = 22f;
            hint.Add(backText);
            faceKey = UIKit.HudKeyCap("1–6", 32f);
            hint.Add(faceKey);
            faceText = UIKit.HudText("", 22f);
            faceText.style.marginLeft = 8f;
            hint.Add(faceText);
            root.Add(hint);

            UIKit.IgnorePicking(root);
            root.style.display = DisplayStyle.None;
        }

        public void RefreshTexts() { recSec = -1; count = -1; expr = -1; bindVersion = -1; }

        public static string ExpressionName(int e)
        {
            switch ((SelfieExpression)e)
            {
                case SelfieExpression.Grin: return Loc.Tr("Grinsen", "Grin");
                case SelfieExpression.Wow: return Loc.Tr("Staunen", "Wow");
                case SelfieExpression.Scared: return Loc.Tr("Schreck", "Scared");
                case SelfieExpression.Kissy: return Loc.Tr("Kussmund", "Kissy");
                case SelfieExpression.Wink: return Loc.Tr("Zwinkern", "Wink");
                case SelfieExpression.Laugh: return Loc.Tr("Lachen", "Laugh");
                default: return Loc.Tr("Auto", "Auto");
            }
        }

        public void Tick(float dt, bool active, bool recording, float recSeconds, bool blinkOn)
        {
            if (active != shown) { shown = active; UIKit.Show(root, active); RefreshTexts(); }
            if (!active) return;
            int bv = GameInput.BindingsVersion * 31 + (int)Loc.Current;
            if (bv != bindVersion)
            {
                bindVersion = bv;
                backKey.text = GameInput.BindingLabel(GameAction.Selfie);
                faceKey.text = GameInput.BindingLabel(GameAction.RightSlot1) + "–" + GameInput.BindingLabel(GameAction.LeftSlot3);
                backText.text = Loc.Tr("zurück", "back");
                faceText.text = Loc.Tr("Gesicht", "face");
            }
            recDot.style.opacity = recording ? (blinkOn ? 1f : 0.15f) : 0f;
            int s = recording ? Mathf.FloorToInt(recSeconds) : -2;
            int n = Mathf.Clamp(ViewfinderInfo.DetectedCount, 0, 99);
            if (s != recSec || n != count)
            {
                recSec = s; count = n;
                recLabel.text = (recording ? "REC " + UIKit.Clock(s) : "SELFIE") + (n > 0 ? "  ·  " + n + Loc.Tr(" Tiere", " animals") : "");
            }
            int e = SelfieState.Expression;
            if (e != expr)
            {
                expr = e;
                exprLabel.text = ExpressionName(e);
            }
        }
    }
}
