using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// The always-visible quest line (L.10, player feedback x): top centre, 18 px from the top, max 640 px wide, no box.
    /// Type glyph + short text (≤ 42 characters) + progress „2/3“ + a distance arrow for place tasks („↑ 34 m“), an
    /// optional smaller second line for the next task. Pops on progress (scale 1.06, tick), shows a check mark for
    /// 1.5 s on completion. Hidden while the phone, menus, cutscenes, the viewfinder or the selfie view are open; moves
    /// 44 px down while live (the LIVE pill sits there). Settings → Spiel „Aufgaben-Anzeige“: Normal / Klein / Aus.
    /// Reads QuestTracker; until a provider fills it, the legacy tutorial text (GameEvents.QuestChanged) is shown.
    public class UIQuestLine
    {
        public const int MaxChars = 42;
        /// The second line (how-to / next task) is smaller and may be a bit longer (max width 560 px).
        public const int NextMaxChars = 56;

        readonly VisualElement root, column, lineRow, distRow;
        readonly UIHudGlyph glyph, arrow;
        readonly Label text, progress, next, dist;
        public readonly VisualElement Below;   // the sleep counter sits here (L.9)

        string legacy = "";
        bool dirty = true, shown, rootShown, columnShown = true;
        string shownLine, shownProgress;
        float pop, check = -1f, distTimer;
        int distKey = int.MinValue, angleKey = int.MinValue;
        int mode = -1;
        bool live;

        public UIQuestLine(VisualElement layer)
        {
            root = new VisualElement { name = "sb-questline", pickingMode = PickingMode.Ignore };
            UIKit.Abs(root, 0f, 18f, 0f, null);
            root.style.alignItems = Align.Center;
            layer.Add(root);

            column = UIKit.Col();
            column.style.alignItems = Align.Center;
            column.style.maxWidth = 640f;
            root.Add(column);

            lineRow = UIKit.Row();
            lineRow.style.justifyContent = Justify.Center;
            column.Add(lineRow);
            glyph = new UIHudGlyph(UIGlyphs.Tasks, UIPalette.Leaf, 26f);
            glyph.style.marginRight = 10f;
            lineRow.Add(glyph);
            text = UIKit.HudText("", 22f);
            text.style.whiteSpace = WhiteSpace.NoWrap;
            text.style.overflow = Overflow.Hidden;
            text.style.textOverflow = TextOverflow.Ellipsis;
            text.style.flexShrink = 1;
            lineRow.Add(text);
            progress = UIKit.HudText("", 22f, UIPalette.Sun);
            progress.style.marginLeft = 10f;
            lineRow.Add(progress);
            distRow = UIKit.Row();
            distRow.style.marginLeft = 12f;
            arrow = new UIHudGlyph(UIGlyphs.ArrowUp, Color.white, 20f);
            distRow.Add(arrow);
            dist = UIKit.HudText("", 20f, UIPalette.DepthSub);
            dist.style.marginLeft = 4f;
            distRow.Add(dist);
            lineRow.Add(distRow);
            UIKit.Transition(lineRow, 0.15f, "scale");

            // bold + full white with the HUD outline: the thin 78 % line was unreadable over a bright sky
            next = UIKit.HudText("", 19f, Color.white);
            UIKit.Outline(next, 1.8f);
            next.style.marginTop = 2f;
            next.style.whiteSpace = WhiteSpace.NoWrap;
            next.style.overflow = Overflow.Hidden;
            next.style.textOverflow = TextOverflow.Ellipsis;
            next.style.maxWidth = 560f;
            column.Add(next);

            Below = UIKit.Col();
            Below.style.alignItems = Align.Center;
            Below.style.marginTop = 6f;
            root.Add(Below);

            UIKit.IgnorePicking(root);
            root.style.display = DisplayStyle.None;
            QuestTracker.Changed += OnChanged;
            QuestTracker.Completed += OnCompleted;
        }

        public void Dispose()
        {
            QuestTracker.Changed -= OnChanged;
            QuestTracker.Completed -= OnCompleted;
        }

        void OnChanged() => dirty = true;

        void OnCompleted(string id)
        {
            check = 1.5f;
            UIAudio.Pop();
            dirty = true;
        }

        /// Legacy tutorial text („Titel\nDetail (2/5)“) from GameEvents.QuestChanged.
        public void SetLegacy(string questText)
        {
            questText = questText ?? "";
            if (questText == legacy) return;
            legacy = questText;
            SplitLegacy(legacy, out legacyLine, out legacyProg);   // parsed once here, never per frame
            dirty = true;
        }

        string legacyLine, legacyProg;

        public void RefreshTexts() { dirty = true; distKey = int.MinValue; }

        public void ResetSession()
        {
            legacy = "";
            legacyLine = null; legacyProg = null;
            shownLine = null; shownProgress = null;
            check = -1f;
            dirty = true;
        }

        static void SplitLegacy(string s, out string line, out string prog)
        {
            line = null; prog = null;
            if (string.IsNullOrEmpty(s)) return;
            int nl = s.IndexOf('\n');
            string title = nl >= 0 ? s.Substring(0, nl) : s;
            string detail = nl >= 0 ? s.Substring(nl + 1) : "";
            line = title.Trim();
            // "(2/5)" at the end of either line → progress
            ExtractCount(ref line, out prog);
            if (prog == null && detail.Length > 0)
            {
                string d = detail.Trim();
                ExtractCount(ref d, out prog);
            }
        }

        static void ExtractCount(ref string s, out string prog)
        {
            prog = null;
            if (string.IsNullOrEmpty(s) || !s.EndsWith(")", System.StringComparison.Ordinal)) return;
            int open = s.LastIndexOf('(');
            if (open < 0 || s.IndexOf('/', open) < 0) return;
            prog = s.Substring(open + 1, s.Length - open - 2).Trim();
            s = s.Substring(0, open).TrimEnd();
        }

        static string Cut(string s, int max = MaxChars)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int nl = s.IndexOf('\n');
            if (nl >= 0) s = s.Substring(0, nl);
            return s.Length <= max ? s : s.Substring(0, max - 1).TrimEnd() + "…";
        }

        public void Tick(float dt, bool aiming)
        {
            var gm = GameManager.I;
            int m = Mathf.Clamp(Settings.Data.questDisplay, 0, 2);
            bool show = gm != null && gm.InGame && m != 2 && !UIState.BlocksGameplay && !UIState.LoadingOpen
                        && !UIState.MainMenuOpen && !aiming && !UIState.SelfieOpen && !SelfieState.Active && !PlayerIntro.Playing;
            string line = QuestTracker.Line, prog = QuestTracker.Progress, nextLine = QuestTracker.NextLine;
            NotifyKind kind = QuestTracker.Kind;
            if (string.IsNullOrEmpty(line)) { line = legacyLine; prog = legacyProg; nextLine = null; kind = NotifyKind.Quest; }
            bool has = !string.IsNullOrEmpty(line) || check > 0f;
            show &= has;
            // the co-op sleep counter (Below) must stay visible even when the task part is hidden (no task, display
            // "Aus", aiming, selfie) – the root shows for either, the column only for the task line
            bool vote = Net.IsOnline && SleepVote.Total > 1 && SleepVote.Lying > 0 && gm != null && gm.InGame && !UIState.MainMenuOpen;
            if (show != shown) { shown = show; dirty = true; }
            if (show != columnShown) { columnShown = show; UIKit.Show(column, show); }
            bool rootOn = show || vote;
            if (rootOn != rootShown) { rootShown = rootOn; UIKit.Show(root, rootOn); }
            bool isLive = LiveState.IsLive;
            // below the stream's LIVE pill + data bar (they end ~84 px down – at 62 the timer ran across the line)
            if (isLive != live) { live = isLive; root.style.top = isLive ? 96f : 18f; }
            if (!show) return;

            if (m != mode) { mode = m; dirty = true; }
            if (dirty)
            {
                dirty = false;
                bool small = m == 1;
                if (check > 0f)
                {
                    glyph.SetDraw(UIGlyphs.Check);
                    glyph.SetColor(UIPalette.Leaf);
                }
                else
                {
                    glyph.SetDraw(UIGlyphs.ForNotify(kind));
                    glyph.SetColor(UIGlyphs.NotifyColor(kind));
                }
                UIKit.Show(glyph, !small);
                text.style.fontSize = small ? 18f : 22f;
                progress.style.fontSize = small ? 18f : 22f;
                string l = Cut(line);
                if (!string.Equals(l, text.text)) text.text = l;
                string p = prog ?? "";
                if (!string.Equals(p, progress.text)) progress.text = p;
                UIKit.Show(progress, p.Length > 0);
                string n = small ? "" : Cut(nextLine, NextMaxChars);
                if (!string.Equals(n, next.text)) next.text = n;
                UIKit.Show(next, n.Length > 0);
                // progress or a new task → pop
                if (shownLine != null && (!string.Equals(shownProgress, prog) || !string.Equals(shownLine, line)))
                {
                    pop = 0.3f;
                    if (string.Equals(shownLine, line)) UIAudio.Click();
                }
                shownLine = line; shownProgress = prog;
                distKey = int.MinValue;
            }
            if (pop > 0f)
            {
                pop -= dt;
                float s = pop > 0f ? 1.06f : 1f;
                lineRow.style.scale = new Scale(new Vector2(s, s));
            }
            if (check > 0f)
            {
                check -= dt;
                if (check <= 0f) dirty = true;
            }
            TickDistance(dt, m == 1);
        }

        void TickDistance(float dt, bool small)
        {
            var target = QuestTracker.Target;
            var p = Refs.Player;
            if (p is Object po && po == null) p = null;
            bool has = !small && target.HasValue && p != null && p.Root != null && !string.IsNullOrEmpty(QuestTracker.Line);
            UIKit.Show(distRow, has);
            if (!has) return;
            distTimer -= dt;
            if (distTimer > 0f) return;
            distTimer = 0.2f;
            Vector3 from = p.Root.position, to = target.Value;
            Vector2 d = new Vector2(to.x - from.x, to.z - from.z);
            int meters = Mathf.RoundToInt(d.magnitude);
            int key = meters >= 1000 ? meters / 100 * 100 : meters;
            if (key != distKey)
            {
                distKey = key;
                dist.text = UIKit.Distance(key);
            }
            Camera cam = p.View != null ? p.View : Camera.main;
            float yaw = cam != null ? cam.transform.eulerAngles.y : p.Root.eulerAngles.y;
            float bearing = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
            int ak = Mathf.RoundToInt(Mathf.DeltaAngle(yaw, bearing) / 5f) * 5;
            if (ak != angleKey)
            {
                angleKey = ak;
                arrow.style.rotate = new Rotate(Angle.Degrees(ak));
            }
        }
    }

    /// Co-op sleep counter under the quest line (L.9): moon glyph + „Schlafen 1/2“ as soon as one player lies down;
    /// „2/2 – Gute Nacht!“ when everybody lies. The "{Name} will schlafen" push comes from the host (SleepVote).
    public class UISleepCounter
    {
        readonly VisualElement row;
        readonly Label label;
        int lying = -1, total = -1;
        bool shown;

        public UISleepCounter(VisualElement parent)
        {
            row = UIKit.Row();
            row.pickingMode = PickingMode.Ignore;
            var g = new UIHudGlyph(UIGlyphs.Moon, UIPalette.Rgb(0xDCE6FF), 24f);
            g.style.marginRight = 8f;
            row.Add(g);
            label = UIKit.HudText("", 22f);
            row.Add(label);
            row.style.display = DisplayStyle.None;
            UIKit.IgnorePicking(row);
            parent.Add(row);
        }

        public void Tick(UINotifications notes)
        {
            int l = SleepVote.Lying, t = SleepVote.Total;
            bool show = Net.IsOnline && t > 1 && l > 0 && UIKit.InGame && !UIState.MainMenuOpen;
            if (show != shown) { shown = show; UIKit.Show(row, show); }
            if (!show)
            {
                lying = -1;
                return;
            }
            if (l != lying || t != total)
            {
                lying = l; total = t;
                string count = l + "/" + t;
                label.text = l >= t ? count + Loc.Tr(" – Gute Nacht!", " – Good night!") : Loc.Tr("Schlafen ", "Sleep ") + count;
                label.style.color = l >= t ? UIPalette.Sun : Color.white;
                // no own "Crew will schlafen" push any more: the host already sends "{Name} will schlafen 1/2" to
                // everybody – the co-op test showed both pushes stacked for the same vote
            }
        }
    }
}
