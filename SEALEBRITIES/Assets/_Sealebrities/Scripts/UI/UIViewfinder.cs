using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Camcorder viewfinder shown while aiming the hand camera: corner brackets, REC timer, tape, zoom, battery,
    /// crosshair, date stamp and the target info (species, size tier, capture progress, framing quality) as outlined
    /// HUD text without boxes. Also shows a small "● REC" indicator while recording without aiming.
    public class UIViewfinder
    {
        readonly VisualElement root, frame, recDot, targetPanel, qualityRow, batteryFill, smallRec, smallRecDot, cross;
        readonly Label recLabel, tapeLabel, zoomLabel, stampLabel, targetName, targetSize, progressLabel, qualityLabel, noTarget, smallRecLabel, progCap, qCap, reasonLabel, aimHint;
        // hint texts cached per FilmHint value (rebuilt on language/binding/range change – nothing allocated per frame)
        readonly string[] hintText = new string[(int)FilmHint.Logged + 1];
        int hintTextVersion = -1, hintRangeKey = -1;
        bool aimHintShown;
        int hasState = -1;
        readonly UIBar progressBar, zoomBar;
        readonly VisualElement[] qualitySegs = new VisualElement[5];
        static readonly Color W = new Color(1f, 1f, 1f, 0.92f);
        static readonly Color SegOff = new Color(1f, 1f, 1f, 0.28f);

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

            // the brackets mark exactly the area in which an animal counts (FilmShotEvaluator: central 70 %)
            frame = new VisualElement();
            frame.style.position = Position.Absolute;
            float m = FilmShotEvaluator.FrameMargin * 100f;
            frame.style.left = Length.Percent(m); frame.style.right = Length.Percent(m);
            frame.style.top = Length.Percent(m); frame.style.bottom = Length.Percent(m);
            root.Add(frame);

            AddBracket(true, true, W); AddBracket(false, true, W); AddBracket(true, false, W); AddBracket(false, false, W);

            // top-left: REC
            var recRow = UIKit.Row();
            UIKit.Abs(recRow, 28f, 22f, null, null);
            recDot = UIKit.Dot(UIPalette.Danger, 18f);
            UIKit.Border(recDot, 2f, Color.white);
            recDot.style.marginRight = 10f;
            recRow.Add(recDot);
            recLabel = Mono("STBY", 28f);
            recRow.Add(recLabel);
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

            // crosshair
            cross = UIKit.Box();
            cross.style.position = Position.Absolute;
            cross.style.left = Length.Percent(50f); cross.style.top = Length.Percent(50f);
            UIKit.Size(cross, 0f, 0f);
            root.Add(cross);
            AddLine(cross, -34f, -1.5f, 22f, 3f, W); AddLine(cross, 12f, -1.5f, 22f, 3f, W);
            AddLine(cross, -1.5f, -34f, 3f, 22f, W); AddLine(cross, -1.5f, 12f, 3f, 22f, W);
            var dot = UIKit.Dot(W, 5f);
            dot.style.position = Position.Absolute;
            dot.style.left = -2.5f; dot.style.top = -2.5f;
            cross.Add(dot);

            // target info (lower centre, no box) – raised so it does not sit on the held camera model
            var tWrap = UIKit.Col();
            UIKit.Abs(tWrap, 0f, null, 0f, null);
            tWrap.style.bottom = Length.Percent(24f);
            tWrap.style.alignItems = Align.Center;
            frame.Add(tWrap);
            targetPanel = UIKit.Col();
            targetPanel.style.width = 520f;
            tWrap.Add(targetPanel);
            // name and size on separate lines: long names ("Suppenschildkröte „Gerald“") never overprint the size
            targetName = UIKit.HudText("", 28f);
            targetName.style.whiteSpace = WhiteSpace.NoWrap;
            targetName.style.overflow = Overflow.Hidden;
            targetName.style.textOverflow = TextOverflow.Ellipsis;
            targetPanel.Add(targetName);
            targetSize = UIKit.HudText("", 20f);
            targetSize.style.marginTop = 2f;
            targetPanel.Add(targetSize);
            var progRow = UIKit.Row();
            progRow.style.marginTop = 10f;
            progCap = UIKit.HudText("", 14f, new Color(1f, 1f, 1f, 0.85f));
            progCap.style.width = 92f;
            progRow.Add(progCap);
            progressBar = new UIBar(10f, UIPalette.Turquoise, SegOff);
            progressBar.style.flexGrow = 1;
            progRow.Add(progressBar);
            progressLabel = UIKit.HudText("", 16f);
            progressLabel.style.width = 52f;
            progressLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            progRow.Add(progressLabel);
            targetPanel.Add(progRow);
            qualityRow = UIKit.Row();
            qualityRow.style.marginTop = 8f;
            qCap = UIKit.HudText("", 14f, new Color(1f, 1f, 1f, 0.85f));
            qCap.style.width = 92f;
            qualityRow.Add(qCap);
            var segs = UIKit.Row();
            segs.style.flexGrow = 1;
            for (int i = 0; i < qualitySegs.Length; i++)
            {
                var sgm = UIKit.Box();
                sgm.style.flexGrow = 1;
                sgm.style.height = 10f;
                if (i > 0) sgm.style.marginLeft = 4f;
                UIKit.Radius(sgm, 3f);
                sgm.style.backgroundColor = SegOff;
                qualitySegs[i] = sgm;
                segs.Add(sgm);
            }
            qualityRow.Add(segs);
            qualityLabel = UIKit.HudText("", 16f);
            qualityLabel.style.width = 100f;
            qualityLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            qualityRow.Add(qualityLabel);
            targetPanel.Add(qualityRow);
            // one-word reason why the framed animal does not count yet (or a check when it is logged)
            reasonLabel = UIKit.HudText("", 19f, UIPalette.Sun, true);
            reasonLabel.style.marginTop = 6f;
            reasonLabel.style.alignSelf = Align.Center;
            targetPanel.Add(reasonLabel);
            noTarget = UIKit.HudText("", 21f, new Color(1f, 1f, 1f, 0.92f), true);
            noTarget.style.unityTextAlign = TextAnchor.MiddleCenter;
            tWrap.Add(noTarget);
            // hip recording: only aimed footage goes into the Sealog → "[RMB] aim" under the REC timer
            aimHint = UIKit.HudText("", 18f, UIPalette.Sun, true);
            aimHint.style.marginTop = 4f;
            aimHint.style.display = DisplayStyle.None;

            // small REC indicator while recording without aiming (top-right, under the clock)
            smallRec = UIKit.Row();
            UIKit.Abs(smallRec, null, 84f, 32f, null);
            smallRecDot = UIKit.Dot(UIPalette.Danger, 16f);
            UIKit.Border(smallRecDot, 2f, Color.white);
            smallRecDot.style.marginRight = 8f;
            smallRec.Add(smallRecDot);
            smallRecLabel = UIKit.HudText("REC", 22f);
            smallRec.Add(smallRecLabel);
            smallRec.style.display = DisplayStyle.None;
            root.Add(smallRec);
            UIKit.Abs(aimHint, null, 116f, 32f, null);
            root.Add(aimHint);

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

        void AddBracket(bool left, bool top, Color c)
        {
            var b = UIKit.Box();
            b.style.position = Position.Absolute;
            UIKit.Size(b, 64f, 64f);
            if (left) b.style.left = 0; else b.style.right = 0;
            if (top) b.style.top = 0; else b.style.bottom = 0;
            if (left) { b.style.borderLeftWidth = 5f; b.style.borderLeftColor = c; } else { b.style.borderRightWidth = 5f; b.style.borderRightColor = c; }
            if (top) { b.style.borderTopWidth = 5f; b.style.borderTopColor = c; } else { b.style.borderBottomWidth = 5f; b.style.borderBottomColor = c; }
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
            progCap.text = Loc.Tr("AUFNAHME", "CAPTURE");
            qCap.text = Loc.Tr("QUALITÄT", "QUALITY");
            recSec = -1; tapeSec = -1; zoomKey = -1; stampKey = -1; progressKey = -1; qualityKey = -1; smallSec = -1;
            lastTargetKnown = false; lastTarget = null; targetCm = -1f; targetState = -1; hasState = -1;
            hintTextVersion = -1;
            aimHintShown = false;
            if (aimHint != null) aimHint.style.display = DisplayStyle.None;
        }

        /// Short icon-like hint for the current camera state (cached; TooFar includes the current scan range).
        string HintText(FilmHint h, FilmHandCamera fhc)
        {
            int rk = fhc != null ? Mathf.RoundToInt(fhc.ScanRange) : 12;
            int ver = GameInput.BindingsVersion * 31 + (int)Loc.Current;
            if (ver != hintTextVersion || rk != hintRangeKey)
            {
                hintTextVersion = ver;
                hintRangeKey = rk;
                string lamp = "[" + GameInput.BindingLabel(GameAction.LightToggle) + "] ";
                for (int i = 0; i < hintText.Length; i++) hintText[i] = "";
                hintText[(int)FilmHint.None] = Loc.Tr("Kein Tier im Bild", "No animal in frame");
                hintText[(int)FilmHint.NoAnimal] = Loc.Tr("Kein Tier im Bild", "No animal in frame");
                hintText[(int)FilmHint.TooFar] = Loc.Tr($"Näher ran (max {rk} m)", $"Get closer (max {rk} m)");
                hintText[(int)FilmHint.OffCenter] = Loc.Tr("Mittig halten", "Keep it centred");
                hintText[(int)FilmHint.TooSmall] = Loc.Tr("Näher ran / Zoom", "Closer / zoom");
                hintText[(int)FilmHint.Blocked] = Loc.Tr("Verdeckt", "Blocked");
                hintText[(int)FilmHint.Shaky] = Loc.Tr("Ruhig halten", "Hold steady");
                hintText[(int)FilmHint.TooDark] = lamp + Loc.Tr("Lampe", "Lamp");
                hintText[(int)FilmHint.Weak] = Loc.Tr("Näher / mittiger", "Closer / more centred");
                hintText[(int)FilmHint.Logged] = Loc.Tr("Im Sealog!", "In the Sealog!");
                hintText[(int)FilmHint.AimToLog] = "[" + GameInput.BindingLabel(GameAction.Aim) + "] " + Loc.Tr("zielen", "aim");
            }
            int k = (int)h;
            return k >= 0 && k < hintText.Length ? hintText[k] : "";
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

            bool aim = cam != null && cam.IsAiming && inGame;
            if (aim != shown)
            {
                shown = aim;
                UIKit.Show(root, aim || smallShown);
                ShowFrameParts(aim);
                recSec = -1; tapeSec = -1; zoomKey = -1; stampKey = -1; progressKey = -1; qualityKey = -1; batteryKey = -1;
                lastTargetKnown = false; lastTarget = null; targetState = -1; hasState = -1;
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
            // recording from the hip: only aimed footage goes into the Sealog
            var fh = cam as FilmHandCamera;
            bool wantAim = small && fh != null && fh.Hint == FilmHint.AimToLog;
            if (wantAim != aimHintShown)
            {
                aimHintShown = wantAim;
                if (wantAim) aimHint.text = HintText(FilmHint.AimToLog, fh);
                aimHint.style.display = wantAim ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (!aim) return;

            // REC / standby
            recDot.style.opacity = recording ? (blinkOn ? 1f : 0.15f) : 0f;
            int rs = recording ? Mathf.FloorToInt(recSeconds) : -2;
            if (rs != recSec)
            {
                recSec = rs;
                recLabel.text = recording ? "REC " + UIKit.Clock(rs) : "STBY";
                recLabel.style.color = recording ? UIPalette.Danger : new Color(1f, 1f, 1f, 0.95f);
            }

            // tape + battery
            float total = Mathf.Max(1f, cam.TapeSecondsTotal);
            float left = Mathf.Max(0f, cam.TapeSecondsLeft);
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
            var fhc = cam as FilmHandCamera;
            FilmHint hint = fhc != null ? fhc.Hint : (has ? FilmHint.Good : FilmHint.NoAnimal);
            if ((has ? 1 : 0) != hasState)
            {
                hasState = has ? 1 : 0;
                UIKit.Show(targetPanel, has);
                UIKit.Show(noTarget, !has);
                lastTargetKnown = false;
            }
            // why it does (not) count: no target → near-miss reason; with a target → one word under the quality bar
            int st = (has ? 100 : 0) + (int)hint + (hint == FilmHint.TooFar && fhc != null ? 1000 * Mathf.RoundToInt(fhc.ScanRange) : 0);
            if (st != targetState)
            {
                targetState = st;
                if (!has) noTarget.text = HintText(hint == FilmHint.None ? FilmHint.NoAnimal : hint, fhc);
                else
                {
                    bool show = hint == FilmHint.Shaky || hint == FilmHint.TooDark || hint == FilmHint.Weak || hint == FilmHint.Logged;
                    reasonLabel.text = show ? HintText(hint, fhc) : "";
                    reasonLabel.style.color = hint == FilmHint.Logged ? UIPalette.Leaf : UIPalette.Sun;
                    UIKit.Show(reasonLabel, show);
                }
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
                        targetSize.style.color = tier == SizeTier.Normal ? Color.white : Progression.TierColor(tier);
                    }
                    else targetSize.text = "";
                }
            }

            int pk = Mathf.RoundToInt(progress * 100f);
            if (pk != progressKey)
            {
                progressKey = pk;
                progressBar.SetValue(progress);
                progressBar.SetColor(progress >= 1f ? UIPalette.Leaf : UIPalette.Turquoise);
                progressLabel.text = progress >= 1f ? "OK" : pk + "%";
            }

            float q = Mathf.Clamp(cam.CurrentTargetQuality, 0f, 1.6f);
            int qk = Mathf.RoundToInt(q * 20f);
            if (qk != qualityKey)
            {
                qualityKey = qk;
                float q01 = q / 1.6f;
                int lit = Mathf.Clamp(Mathf.CeilToInt(q01 * qualitySegs.Length - 0.001f), 0, qualitySegs.Length);
                Color c = q < 0.5f ? UIPalette.Danger : q < 0.9f ? UIPalette.Sun : q < 1.25f ? UIPalette.Turquoise : UIPalette.Leaf;
                for (int i = 0; i < qualitySegs.Length; i++)
                    qualitySegs[i].style.backgroundColor = i < lit ? c : SegOff;
                qualityLabel.text = q < 0.5f ? Loc.Tr("schlecht", "poor") : q < 0.9f ? Loc.Tr("okay", "okay") : q < 1.25f ? Loc.Tr("gut", "good") : Loc.Tr("perfekt", "perfect");
                qualityLabel.style.color = c;
            }
        }

        /// Allocation-free variant of Journal.IsDiscovered.
        static bool Discovered(string id)
        {
            var l = GameManager.I.State.journal;
            for (int i = 0; i < l.Count; i++)
                if (l[i] != null && l[i].speciesId == id) return l[i].discovered;
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
                if (dist < 0.01f || dist > 40f) continue;
                float cos = Vector3.Dot(d / dist, f);
                if (cos < 0.6f) continue;
                float score = (1f - cos) * 40f + dist * 0.05f;
                if (score < bestScore) { bestScore = score; best = c; }
            }
            return best;
        }
    }
}
