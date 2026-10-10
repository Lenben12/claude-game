using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Camcorder viewfinder shown while aiming the hand camera: corner brackets, REC timer, tape, zoom, battery,
    /// crosshair, date stamp and the target panel (species, size tier, capture progress, framing quality).
    /// Also shows a small "● REC" indicator while recording without aiming.
    public class UIViewfinder
    {
        readonly VisualElement root, frame, recDot, targetPanel, qualityRow, batteryFill, smallRec, smallRecDot, crossFocus, cross;
        readonly Label recLabel, tapeLabel, zoomLabel, stampLabel, targetName, targetSize, progressLabel, qualityLabel, noTarget, smallRecLabel;
        readonly UIBar progressBar, zoomBar;
        readonly VisualElement[] qualitySegs = new VisualElement[5];

        bool shown, smallShown, wasRecording;
        int targetState = -1;
        float recSeconds, blink, targetTimer;
        int recSec = -1, tapeSec = -1, zoomKey = -1, stampKey = -1, progressKey = -1, qualityKey = -1, batteryKey = -1, smallSec = -1;
        SpeciesDef lastTarget;
        bool lastTargetKnown;
        ICreature targetCreature;
        float targetCm = -1f;

        public UIViewfinder(VisualElement layer)
        {
            root = new VisualElement { name = "sb-viewfinder" };
            UIKit.FillParent(root);
            layer.Add(root);

            frame = new VisualElement();
            frame.style.position = Position.Absolute;
            frame.style.left = Length.Percent(20f); frame.style.right = Length.Percent(20f);
            frame.style.top = Length.Percent(14f); frame.style.bottom = Length.Percent(13f);
            root.Add(frame);

            Color w = new Color(1f, 1f, 1f, 0.88f);
            AddBracket(true, true, w); AddBracket(false, true, w); AddBracket(true, false, w); AddBracket(false, false, w);

            // top-left: REC
            var recRow = UIKit.Row();
            UIKit.Abs(recRow, 28f, 22f, null, null);
            recDot = UIKit.Dot(UIPalette.Live, 16f);
            recDot.style.marginRight = 10f;
            recRow.Add(recDot);
            recLabel = Mono("STBY", 26f);
            recRow.Add(recLabel);
            frame.Add(recRow);

            // top-right: battery + tape
            var tr = UIKit.Row();
            UIKit.Abs(tr, null, 22f, 28f, null);
            tapeLabel = Mono("", 20f);
            tapeLabel.style.marginRight = 16f;
            tr.Add(tapeLabel);
            var battery = UIKit.Row();
            var body = UIKit.Box();
            UIKit.Size(body, 40f, 20f);
            UIKit.Border(body, 2f, w);
            UIKit.Radius(body, 3f);
            UIKit.Pad(body, 2f);
            batteryFill = UIKit.Box();
            batteryFill.style.height = Length.Percent(100f);
            batteryFill.style.width = Length.Percent(100f);
            batteryFill.style.backgroundColor = w;
            body.Add(batteryFill);
            battery.Add(body);
            var nub = UIKit.Box();
            UIKit.Size(nub, 4f, 8f);
            nub.style.backgroundColor = w;
            battery.Add(nub);
            tr.Add(battery);
            frame.Add(tr);

            // bottom-left: zoom + date stamp
            var bl = UIKit.Col();
            UIKit.Abs(bl, 28f, null, null, 22f);
            zoomLabel = Mono("x1.0", 24f);
            bl.Add(zoomLabel);
            zoomBar = new UIBar(4f, w, new Color(1f, 1f, 1f, 0.2f));
            zoomBar.style.width = 140f;
            zoomBar.style.marginTop = 6f;
            bl.Add(zoomBar);
            stampLabel = Mono("", 18f);
            stampLabel.style.marginTop = 12f;
            bl.Add(stampLabel);
            frame.Add(bl);

            // crosshair
            cross = UIKit.Box();
            cross.style.position = Position.Absolute;
            cross.style.left = Length.Percent(50f); cross.style.top = Length.Percent(50f);
            UIKit.Size(cross, 0f, 0f);
            root.Add(cross);
            AddLine(cross, -34f, -1f, 22f, 2f, w); AddLine(cross, 12f, -1f, 22f, 2f, w);
            AddLine(cross, -1f, -34f, 2f, 22f, w); AddLine(cross, -1f, 12f, 2f, 22f, w);
            var dot = UIKit.Dot(w, 4f);
            dot.style.position = Position.Absolute;
            dot.style.left = -2f; dot.style.top = -2f;
            cross.Add(dot);
            crossFocus = UIKit.Box();
            crossFocus.style.position = Position.Absolute;
            crossFocus.style.left = -70f; crossFocus.style.top = -50f;
            UIKit.Size(crossFocus, 140f, 100f);
            UIKit.Border(crossFocus, 2f, UIPalette.A(UIPalette.Lagoon, 0.9f));
            UIKit.Radius(crossFocus, 6f);
            crossFocus.style.display = DisplayStyle.None;
            cross.Add(crossFocus);

            // target panel (bottom-center)
            var tWrap = UIKit.Col();
            UIKit.Abs(tWrap, 0f, null, 0f, 22f);
            tWrap.style.alignItems = Align.Center;
            frame.Add(tWrap);
            targetPanel = UIKit.Card(UIPalette.A(UIPalette.Deep, 0.62f), 14f, 0f);
            UIKit.Pad(targetPanel, 12f, 18f, 14f, 18f);
            targetPanel.style.width = 420f;
            tWrap.Add(targetPanel);
            var nameRow = UIKit.Row();
            targetName = UIKit.Text("", 22f, UIPalette.Text, true);
            targetName.style.flexShrink = 1;
            nameRow.Add(targetName);
            nameRow.Add(UIKit.Flex());
            targetSize = UIKit.Text("", 17f, UIPalette.Text, true);
            nameRow.Add(targetSize);
            targetPanel.Add(nameRow);
            var progRow = UIKit.Row();
            progRow.style.marginTop = 10f;
            var progCap = UIKit.Caption(Loc.Tr("Aufnahme", "Capture"), UIPalette.TextDim, 10f);
            progCap.name = "cap-progress";
            progCap.style.width = 74f;
            progRow.Add(progCap);
            progressBar = new UIBar(8f, UIPalette.Lagoon);
            progressBar.style.flexGrow = 1;
            progRow.Add(progressBar);
            progressLabel = UIKit.Text("", 13f, UIPalette.Text, true);
            progressLabel.style.width = 46f;
            progressLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            progRow.Add(progressLabel);
            targetPanel.Add(progRow);
            qualityRow = UIKit.Row();
            qualityRow.style.marginTop = 8f;
            var qCap = UIKit.Caption(Loc.Tr("Qualität", "Quality"), UIPalette.TextDim, 10f);
            qCap.name = "cap-quality";
            qCap.style.width = 74f;
            qualityRow.Add(qCap);
            var segs = UIKit.Row();
            segs.style.flexGrow = 1;
            for (int i = 0; i < qualitySegs.Length; i++)
            {
                var sgm = UIKit.Box();
                sgm.style.flexGrow = 1;
                sgm.style.height = 8f;
                if (i > 0) sgm.style.marginLeft = 4f;
                UIKit.Radius(sgm, 2f);
                sgm.style.backgroundColor = new Color(1f, 1f, 1f, 0.12f);
                qualitySegs[i] = sgm;
                segs.Add(sgm);
            }
            qualityRow.Add(segs);
            qualityLabel = UIKit.Text("", 13f, UIPalette.Text, true);
            qualityLabel.style.width = 90f;
            qualityLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            qualityRow.Add(qualityLabel);
            targetPanel.Add(qualityRow);
            noTarget = UIKit.Text("", 15f, UIPalette.TextDim);
            noTarget.style.unityTextAlign = TextAnchor.MiddleCenter;
            UIKit.Shadow(noTarget, 4f, 0.8f);
            tWrap.Add(noTarget);

            // small REC indicator while recording without aiming
            smallRec = UIKit.Row();
            UIKit.Abs(smallRec, null, 96f, 28f, null);
            smallRec.style.backgroundColor = UIPalette.Panel;
            UIKit.Radius(smallRec, 999f);
            UIKit.Pad(smallRec, 7f, 14f, 7f, 12f);
            smallRecDot = UIKit.Dot(UIPalette.Live, 12f);
            smallRecDot.style.marginRight = 8f;
            smallRec.Add(smallRecDot);
            smallRecLabel = UIKit.Text("REC", 16f, UIPalette.Text, true);
            smallRec.Add(smallRecLabel);
            smallRec.style.display = DisplayStyle.None;
            root.Add(smallRec);

            UIKit.IgnorePicking(root);
            root.style.display = DisplayStyle.None;
            ShowFrameParts(false);
        }

        static Label Mono(string text, float size)
        {
            var l = UIKit.Text(text, size, new Color(1f, 1f, 1f, 0.92f), true);
            l.style.letterSpacing = 2f;
            UIKit.Shadow(l, 3f, 0.7f);
            return l;
        }

        void AddBracket(bool left, bool top, Color c)
        {
            var b = UIKit.Box();
            b.style.position = Position.Absolute;
            UIKit.Size(b, 64f, 64f);
            if (left) b.style.left = 0; else b.style.right = 0;
            if (top) b.style.top = 0; else b.style.bottom = 0;
            if (left) { b.style.borderLeftWidth = 4f; b.style.borderLeftColor = c; } else { b.style.borderRightWidth = 4f; b.style.borderRightColor = c; }
            if (top) { b.style.borderTopWidth = 4f; b.style.borderTopColor = c; } else { b.style.borderBottomWidth = 4f; b.style.borderBottomColor = c; }
            frame.Add(b);
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
        }

        public void RefreshTexts()
        {
            var cap = targetPanel.Q<Label>("cap-progress");
            if (cap != null) cap.text = Loc.Tr("AUFNAHME", "CAPTURE");
            var q = targetPanel.Q<Label>("cap-quality");
            if (q != null) q.text = Loc.Tr("QUALITÄT", "QUALITY");
            recSec = -1; tapeSec = -1; zoomKey = -1; stampKey = -1; progressKey = -1; qualityKey = -1; smallSec = -1;
            lastTargetKnown = false; lastTarget = null; targetCm = -1f; targetState = -1;
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
                return cam != null && cam.IsAiming && !UIState.BlocksGameplay && UIKit.InGame;
            }
        }

        public void Tick(float dt)
        {
            var cam = Refs.HandCamera;
            bool inGame = UIKit.InGame && !UIState.BlocksGameplay;
            bool recording = cam != null && cam.IsRecording && inGame;
            if (recording && !wasRecording) recSeconds = 0f;
            wasRecording = cam != null && cam.IsRecording;
            if (recording) recSeconds += Time.deltaTime;
            blink += dt;
            bool blinkOn = Mathf.Repeat(blink, 1f) < 0.6f;

            bool aim = cam != null && cam.IsAiming && inGame;
            if (aim != shown)
            {
                shown = aim;
                UIKit.Show(root, aim || smallShown);
                ShowFrameParts(aim);
                recSec = -1; tapeSec = -1; zoomKey = -1; stampKey = -1; progressKey = -1; qualityKey = -1; batteryKey = -1;
                lastTargetKnown = false; lastTarget = null; targetState = -1;
            }
            bool small = recording && !aim;
            if (small != smallShown)
            {
                smallShown = small;
                UIKit.Show(smallRec, small);
                UIKit.Show(root, aim || small);
                smallSec = -1;
            }
            if (small)
            {
                smallRecDot.style.opacity = blinkOn ? 1f : 0.15f;
                int s = Mathf.FloorToInt(recSeconds);
                if (s != smallSec) { smallSec = s; smallRecLabel.text = "REC " + UIKit.Clock(s); }
            }
            if (!aim) return;

            // REC / standby
            recDot.style.opacity = recording ? (blinkOn ? 1f : 0.15f) : 0f;
            int rs = recording ? Mathf.FloorToInt(recSeconds) : -2;
            if (rs != recSec)
            {
                recSec = rs;
                recLabel.text = recording ? "REC " + UIKit.Clock(rs) : "STBY";
                recLabel.style.color = recording ? UIPalette.Live : new Color(1f, 1f, 1f, 0.92f);
            }

            // tape + battery
            float total = Mathf.Max(1f, cam.TapeSecondsTotal);
            float left = Mathf.Max(0f, cam.TapeSecondsLeft);
            int ts = Mathf.CeilToInt(left);
            if (ts != tapeSec)
            {
                tapeSec = ts;
                tapeLabel.text = Loc.Tr("BAND ", "TAPE ") + UIKit.Clock(ts);
                tapeLabel.style.color = left / total < 0.15f ? UIPalette.Live : new Color(1f, 1f, 1f, 0.92f);
            }
            int bk = Mathf.CeilToInt(left / total * 4f);
            if (bk != batteryKey)
            {
                batteryKey = bk;
                batteryFill.style.width = Length.Percent(Mathf.Clamp(bk, 0, 4) * 25f);
                batteryFill.style.backgroundColor = bk <= 1 ? UIPalette.Live : new Color(1f, 1f, 1f, 0.88f);
            }

            // zoom
            int zk = Mathf.RoundToInt(cam.Zoom * 10f);
            if (zk != zoomKey)
            {
                zoomKey = zk;
                zoomLabel.text = "x" + (zk / 10f).ToString("0.0", Loc.Culture);
                float maxZoom = GameManager.I != null ? Mathf.Max(1.01f, GameManager.I.Gear.maxZoom) : 2f;
                zoomBar.SetValue(Mathf.InverseLerp(1f, maxZoom, cam.Zoom));
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

            TickTarget(dt, cam);
        }

        void TickTarget(float dt, IHandCamera cam)
        {
            var target = cam.CurrentTarget;
            bool has = target != null;
            int st = has ? 1 : 0;
            if (st != targetState)
            {
                targetState = st;
                UIKit.Show(targetPanel, has);
                UIKit.Show(crossFocus, has);
                UIKit.Show(noTarget, !has);
                if (!has) noTarget.text = Loc.Tr("Kein Tier im Bild – richte die Kamera auf ein Tier", "No animal in frame – point the camera at an animal");
                lastTargetKnown = false;
            }
            if (!has) { targetCreature = null; return; }

            float progress = Mathf.Clamp01(cam.CurrentTargetProgress01);
            // once known for this target it stays known (cache resets on aim/target change); journal lookup is allocation-free
            bool known = GameManager.I != null && GameManager.I.InGame
                         && ((ReferenceEquals(target, lastTarget) && lastTargetKnown) || progress >= 1f || Discovered(target.id));
            if (target != lastTarget || known != lastTargetKnown)
            {
                lastTarget = target;
                lastTargetKnown = known;
                targetName.text = known ? target.Name : "???";
                targetCreature = null;
                targetCm = -1f;
                targetTimer = 0f;
            }

            // size of the filmed specimen: the closest matching creature near the view axis
            targetTimer -= dt;
            if (targetTimer <= 0f)
            {
                targetTimer = 0.15f;
                targetCreature = FindCreature(target);
                float cm = targetCreature != null ? targetCreature.SizeCm : -1f;
                if (Mathf.Abs(cm - targetCm) > 0.5f)
                {
                    targetCm = cm;
                    if (targetCreature != null)
                    {
                        var tier = targetCreature.Tier;
                        targetSize.text = Loc.Length(cm) + " · " + Loc.TierName(tier) + " " + UIKit.Mult(Progression.SizeMultiplier(tier));
                        targetSize.style.color = Progression.TierColor(tier);
                    }
                    else targetSize.text = "";
                }
            }

            int pk = Mathf.RoundToInt(progress * 100f);
            if (pk != progressKey)
            {
                progressKey = pk;
                progressBar.SetValue(progress);
                progressBar.SetColor(progress >= 1f ? UIPalette.Money : UIPalette.Lagoon);
                progressLabel.text = progress >= 1f ? "OK" : pk + "%";
                UIKit.BorderColor(crossFocus, progress >= 1f ? UIPalette.A(UIPalette.Money, 0.95f) : UIPalette.A(UIPalette.Lagoon, 0.9f));
            }

            float q = Mathf.Clamp(cam.CurrentTargetQuality, 0f, 1.6f);
            int qk = Mathf.RoundToInt(q * 20f);
            if (qk != qualityKey)
            {
                qualityKey = qk;
                float q01 = q / 1.6f;
                int lit = Mathf.Clamp(Mathf.CeilToInt(q01 * qualitySegs.Length - 0.001f), 0, qualitySegs.Length);
                Color c = q < 0.5f ? UIPalette.Live : q < 0.9f ? UIPalette.Sun : q < 1.25f ? UIPalette.Lagoon : UIPalette.Money;
                for (int i = 0; i < qualitySegs.Length; i++)
                    qualitySegs[i].style.backgroundColor = i < lit ? c : new Color(1f, 1f, 1f, 0.12f);
                qualityLabel.text = q < 0.5f ? Loc.Tr("schlecht", "poor") : q < 0.9f ? Loc.Tr("okay", "okay") : q < 1.25f ? Loc.Tr("gut", "good") : Loc.Tr("perfekt", "perfect");
                qualityLabel.style.color = c;
            }
        }

        /// Allocation-free variant of Journal.IsDiscovered (that one allocates a closure per call).
        static bool Discovered(string id)
        {
            var l = GameManager.I.State.journal;
            for (int i = 0; i < l.Count; i++)
                if (l[i].speciesId == id) return l[i].discovered;
            return false;
        }

        static ICreature FindCreature(SpeciesDef species)
        {
            Camera view = Refs.Player != null ? Refs.Player.View : null;
            if (view == null) view = Camera.main;
            if (view == null) return null;
            var t = view.transform;
            Vector3 o = t.position, f = t.forward;
            var list = CreatureRegistry.Active;
            ICreature best = null;
            float bestScore = float.MaxValue;
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                if (c == null) continue;
                if (c is Object uo && uo == null) continue;
                // the hand camera never records hallucinations, so never show their (fake) size either
                if (c.Species != species || !c.IsAlive || c.IsHallucination) continue;
                Vector3 d = c.Position - o;
                float dist = d.magnitude;
                if (dist < 0.01f || dist > 120f) continue;
                float cos = Vector3.Dot(d / dist, f);
                if (cos < 0.6f) continue;
                float score = (1f - cos) * 40f + dist * 0.05f;
                if (score < bestScore) { bestScore = score; best = c; }
            }
            return best;
        }
    }
}
