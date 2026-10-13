using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Display names of tasks the npc / livestream modules own (they set these hooks; the id is shown otherwise).
    public static class UITaskInfo
    {
        /// Side quest id ("H1", "toni" …) → localized title.
        public static Func<string, string> SideQuestTitle;
        /// Gus mission id ("M1" …) → localized title.
        public static Func<string, string> MissionTitle;
        /// Secret index (0-based, CollectibleKind.Secret) → clue text once heard, or null (L.6 Collection „?“).
        public static Func<int, string> SecretClue;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { SideQuestTitle = null; MissionTitle = null; SecretClue = null; }

        public static string Side(string id)
        {
            string t = SideQuestTitle != null ? SideQuestTitle(id) : null;
            if (string.IsNullOrEmpty(t)) t = SideQuestDatabase.TitleOf(id);
            return string.IsNullOrEmpty(t) ? id : t;
        }

        /// The current step's short line (+ "2/3" when it counts), from the npc module's SideQuestDatabase.
        public static string SideStep(QuestState q)
        {
            if (q == null) return "";
            var def = SideQuestDatabase.Get(q.id);
            if (def == null || def.steps == null || def.steps.Length == 0) return Loc.Tr("Schritt ", "Step ") + (q.step + 1);
            var st = def.steps[Mathf.Clamp(q.step, 0, def.steps.Length - 1)];
            string line = st.Line;
            if (st.count > 1) line += "  " + Mathf.Clamp(q.progress, 0, st.count) + "/" + st.count;
            return line;
        }

        public static string Mission(string id)
        {
            string t = MissionTitle != null ? MissionTitle(id) : null;
            if (!string.IsNullOrEmpty(t)) return t;
            switch (id)
            {
                case "M1": return Loc.Tr("Die Glücksflosse", "The Lucky Fin");
                case "M2": return Loc.Tr("Funkstille", "Radio Silence");
                case "M3": return Loc.Tr("Das Leuchtfeuer", "The Beacon");
                case "M4": return Loc.Tr("Signal aus der Tiefe", "Signal from the Deep");
                default: return id;
            }
        }
    }

    /// "Aufgaben" (K.5, L.7, L.10, K.3): the three main goals of Gus' letter on top, the tracked task, every running task
    /// with a pin „Verfolgen“ (tutorial → story mission → side quests → sponsor deals), today's photo challenges, quick
    /// to-dos from the game state, the next milestone / dive site and the collection (missing secrets as „?“).
    public class UIAppTasks : UIPhoneApp
    {
        string quest = "";
        bool questNew;

        public override string Id => "tasks";
        public override string Title => Loc.Tr("Aufgaben", "Tasks");
        public override Color Color => UIPalette.Leaf;
        public override UIGlyph.DrawFn Icon => UIGlyphs.Tasks;
        public override int Badge => questNew ? -1 : 0;

        public string CurrentQuest => quest;

        /// Called by the UI controller on GameEvents.QuestChanged. isNew arms the badge (unless the app is open).
        public void SetQuest(string text, bool isNew)
        {
            text = text ?? "";
            if (text == quest) return;
            bool sameTask = FirstLine(text) == FirstLine(quest);
            quest = text;
            if (text.Length == 0) questNew = false;
            else if (isNew && !sameTask) questNew = !(UIState.PhoneOpen && Phone != null && Phone.Current == this);
            Phone?.MarkDirty();
        }

        public void SetQuest(string text) => SetQuest(text, true);

        static string FirstLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int nl = s.IndexOf('\n');
            return nl >= 0 ? s.Substring(0, nl) : s;
        }

        public void ResetSession()
        {
            quest = "";
            questNew = false;
        }

        public override void OnOpen() { questNew = false; }

        public override void Build(VisualElement body)
        {
            var gm = GameManager.I;
            if (gm == null) return;
            var s = gm.State;
            questNew = false;

            BuildMainGoals(body, s);
            BuildRunning(body, s);
            BuildSideLog(body, s);
            BuildChallenges(body, s);
            BuildTodos(body, s);
            BuildNext(body, s);
            BuildHermits(body, s);
            BuildHints(body);
            BuildCollection(body);
        }

        // ------------------------------------------------------------------ round 3: side-quest log (GDD §20 Q11)
        readonly System.Collections.Generic.List<StoryLog.Entry> offered = new System.Collections.Generic.List<StoryLog.Entry>(8);

        /// Jobs on offer right now (who to ask) + how many are done – the funny filming jobs carry a camera.
        void BuildSideLog(VisualElement body, GameState s)
        {
            StoryLog.Offered(offered);
            int done = 0, total = SideQuestDatabase.All.Length;
            for (int i = 0; i < total; i++) if (Quests.IsDone(SideQuestDatabase.All[i].id)) done++;
            if (offered.Count == 0 && done == 0) return;
            body.Add(PhoneKit.Section(Loc.Tr("Aufträge", "Jobs") + "  " + done + "/" + total, UIGlyphs.Camera));
            for (int i = 0; i < offered.Count && i < 6; i++)
            {
                var e = offered[i];
                var d = SideQuestDatabase.Get(e.id);
                bool film = d != null && d.filming;
                var row = UIKit.Row();
                UIKit.Pad(row, 10f, 12f, 10f, 12f);
                UIKit.Radius(row, 18f);
                row.style.marginBottom = 6f;
                row.style.backgroundColor = Color.white;
                row.Add(UIWidgets.IconTile(film ? UIGlyphs.Camera : UIGlyphs.Star, film ? UIPalette.Rgb(0x4F7FAE) : UIPalette.Sun, 46f));
                var col = UIKit.Col();
                col.style.marginLeft = 10f;
                col.style.flexShrink = 1;
                col.style.flexGrow = 1;
                col.Add(PhoneKit.T(e.title, PhoneKit.Small, UIPalette.Ink, true));
                col.Add(PhoneKit.T(Loc.Tr("Neu bei ", "New from ") + e.giver, PhoneKit.Tiny, UIPalette.InkSoft));
                row.Add(col);
                row.Add(new UIGlyph(UIGlyphs.Bubble, UIWidgets.Ink(e.color), 28f));
                body.Add(row);
            }
            if (offered.Count == 0)
                body.Add(PhoneKit.Empty(UIGlyphs.Camera, Loc.Tr("Gerade keine neuen Aufträge – frag Horst in seiner Hängematte.", "No new jobs right now – ask Horst in his hammock.")));
        }

        // ------------------------------------------------------------------ round 3: the hermits (mood, respect, crafts)
        void BuildHermits(VisualElement body, GameState s)
        {
            if (!StoryFlags.Has(StoryKeys.HermitCampFound)) return;
            body.Add(PhoneKit.Section(Loc.Tr("Einsiedler", "Hermits") + "  · " + GameInput.BindingLabel(GameAction.Gestures) + Loc.Tr(" halten = Zeichen", " hold = signs"), UIGlyphs.Hand));
            var card = PhoneKit.Card(12f);
            for (int i = 0; i < NpcDatabase.Hermits.Length; i++)
            {
                string id = NpcDatabase.Hermits[i];
                var nd = NpcDatabase.Get(id);
                var hd = HermitDatabase.Get(id);
                if (nd == null || hd == null) continue;
                var hs = Hermits.Get(id);
                bool met = hs != null && hs.met;
                int mood = Hermits.Mood(id), band = Hermits.Band(mood);
                var row = UIKit.Row();
                row.style.marginTop = i == 0 ? 0f : 8f;
                row.style.alignItems = Align.Center;
                var name = UIKit.Col();
                name.style.width = 150f;
                name.Add(PhoneKit.T(met ? nd.Name : "?", PhoneKit.Small, UIPalette.Ink, true));
                name.Add(PhoneKit.T(nd.Role, PhoneKit.Tiny, UIPalette.InkSoft));
                row.Add(name);
                var barCol = UIKit.Col();
                barCol.style.flexGrow = 1;
                barCol.style.flexShrink = 1;
                var bar = PhoneKit.Bar(Mathf.Clamp01((mood + 100) / 200f), Hermits.BandColor(band), 10f);
                barCol.Add(bar);
                string info = Hermits.BandName(band);
                if (hs != null && hs.respect > 0) info += Loc.Tr("  · Respekt ", "  · respect ") + hs.respect;
                if (!string.IsNullOrEmpty(hd.craftFlag)) info += "  · " + hd.Craft + (StoryFlags.Has(hd.craftFlag) ? Loc.Tr(" (gelernt)", " (learned)") : "");
                barCol.Add(PhoneKit.T(info, PhoneKit.Tiny, UIPalette.InkSoft));
                row.Add(barCol);
                card.Add(row);
            }
            card.Add(PhoneKit.T(Loc.Tr("Geschenke + richtige Zeichen heben die Laune. „Das Meer ehren“ mögen alle.", "Gifts + the right signs lift the mood. Everyone likes 'Honour the sea'."), PhoneKit.Tiny, UIPalette.InkSoft));
            body.Add(card);
        }

        // ------------------------------------------------------------------ round 3: hints heard from NPCs (Q12)
        readonly System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, string>> hints = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, string>>(16);

        void BuildHints(VisualElement body)
        {
            NpcClues.Heard(hints);
            if (hints.Count == 0) return;
            body.Add(PhoneKit.Section(Loc.Tr("Hinweise", "Hints"), UIGlyphs.Question));
            for (int i = 0; i < hints.Count && i < 8; i++)
            {
                var c = UIWidgets.Notice("„" + hints[i].Key + "“ – " + hints[i].Value, UIPalette.Violet, UIGlyphs.Question, PhoneKit.Tiny);
                c.style.marginBottom = 6f;
                body.Add(c);
            }
        }

        // ------------------------------------------------------------------ K.5 main goals
        void BuildMainGoals(VisualElement body, GameState s)
        {
            var card = PhoneKit.Card(18f);
            card.style.backgroundColor = UIPalette.Rgb(0xF6ECD6);
            var head = UIKit.Row();
            head.Add(new UIGlyph(UIGlyphs.Envelope, UIPalette.Rgb(0xB8861B), 30f));
            head.Add(UIKit.Text(" " + Loc.Tr("Hauptziele", "Main goals"), PhoneKit.Body, UIPalette.Rgb(0x8E6C50), true));
            card.Add(head);
            card.Add(Goal(UIGlyphs.Person, UIPalette.Turquoise, Loc.Tr("1.000.000 Follower", "1,000,000 followers"),
                          s.followers / (float)Progression.FinaleFollowers, UIKit.Compact(s.followers)));
            int total = SpeciesDatabase.Count, found = Journal.DiscoveredCount;
            card.Add(Goal(UIGlyphs.Book, UIPalette.TurquoiseInk, Loc.Tr("Das Sealog füllen", "Fill the Sealog"),
                          total > 0 ? found / (float)total : 0f, found + "/" + total));
            bool live = StoryFlags.Has(StoryKeys.FinaleDone);
            card.Add(Goal(UIGlyphs.Broadcast, UIPalette.Rgb(0x8E5CC2), Loc.Tr("Live aus der Tiefe", "Live from the Deep"),
                          live ? 1f : Mathf.Clamp01(s.followers / (float)Progression.FinaleFollowers) * 0.9f, live ? "100 %" : "?"));
            body.Add(card);
        }

        static VisualElement Goal(UIGlyph.DrawFn icon, Color c, string text, float progress, string value)
        {
            var col = UIKit.Col();
            col.style.marginTop = 10f;
            var r = UIKit.Row();
            r.Add(new UIGlyph(icon, UIWidgets.Ink(c), 26f));
            var l = PhoneKit.T(" " + text, PhoneKit.Small, UIPalette.Ink, true);
            l.style.flexGrow = 1;
            l.style.flexShrink = 1;
            r.Add(l);
            r.Add(UIKit.Text(value, PhoneKit.Small, UIPalette.InkSoft, true));
            col.Add(r);
            var bar = PhoneKit.Bar(Mathf.Clamp01(progress), c, 10f);
            bar.style.marginTop = 5f;
            col.Add(bar);
            return col;
        }

        // ------------------------------------------------------------------ running tasks with pins (L.10)
        void BuildRunning(VisualElement body, GameState s)
        {
            body.Add(PhoneKit.Section(Loc.Tr("Läuft gerade", "Running"), UIGlyphs.Flag));
            int n = 0;
            string tracked = s.trackedQuest;
            string activeId = QuestTracker.ActiveId;

            // tutorial (legacy text)
            if (quest.Length > 0)
            {
                body.Add(TaskRow(UIGlyphs.Tasks, UIPalette.Leaf, FirstLine(quest), QuestDetail(quest), "tutorial", tracked, activeId));
                n++;
            }
            // Gus mission
            var m = Missions.Current;
            if (m != null)
            {
                body.Add(TaskRow(UIGlyphs.Radio, UIPalette.Rgb(0x3FA9B5), UITaskInfo.Mission(m.id), Loc.Tr("Gus' Auftrag", "Gus's job"), m.id, tracked, activeId));
                n++;
            }
            // side quests (+ Toni's chain)
            for (int i = 0; i < s.sideQuests.Count; i++)
            {
                var q = s.sideQuests[i];
                if (q == null || q.done) continue;
                bool toni = Quests.IsStoryChain(q.id);
                body.Add(TaskRow(toni ? UIGlyphs.Envelope : UIGlyphs.Star, toni ? UIPalette.Rgb(0x4F7FAE) : UIPalette.Sun,
                                 UITaskInfo.Side(q.id), UITaskInfo.SideStep(q), q.id, tracked, activeId));
                n++;
            }
            // sponsor deals
            var deals = Deals.Active;
            for (int i = 0; i < deals.Count; i++)
            {
                var d = deals[i];
                if (d == null) continue;
                string prog = d.target > 0f ? Mathf.FloorToInt(d.progress) + "/" + Mathf.CeilToInt(d.target) : Deals.NameOf(d.sponsorId);
                body.Add(TaskRow(UIGlyphs.Handshake, UIPalette.Rgb(0x3FA9A0), Deals.TitleOf(d), prog, d.id, tracked, activeId));
                n++;
            }
            if (n == 0)
                body.Add(PhoneKit.Empty(UIGlyphs.Flag, Loc.Tr("Freies Spiel – filme Tiere, sammle Follower!", "Free play – film animals, grow your followers!")));
        }

        static string QuestDetail(string q)
        {
            int nl = q.IndexOf('\n');
            return nl >= 0 ? q.Substring(nl + 1).Trim() : "";
        }

        VisualElement TaskRow(UIGlyph.DrawFn icon, Color color, string title, string sub, string id, string tracked, string shownId)
        {
            bool isTracked = !string.IsNullOrEmpty(tracked) ? tracked == id : shownId == id;
            var row = UIKit.Row();
            UIKit.Pad(row, 12f, 12f, 12f, 12f);
            UIKit.Radius(row, 20f);
            row.style.marginBottom = 8f;
            row.style.backgroundColor = isTracked ? UIPalette.A(color, 0.18f) : Color.white;
            if (isTracked) UIKit.Border(row, 2f, UIWidgets.Ink(color));
            row.Add(UIWidgets.IconTile(icon, color, 52f));
            var col = UIKit.Col();
            col.style.marginLeft = 12f;
            col.style.flexGrow = 1;
            col.style.flexShrink = 1;
            col.Add(PhoneKit.T(title, PhoneKit.Body, UIPalette.Ink, true));
            if (!string.IsNullOrEmpty(sub)) col.Add(PhoneKit.T(sub, PhoneKit.Tiny, UIPalette.InkSoft));
            row.Add(col);
            // pin „Verfolgen“
            var pin = UIKit.Col();
            pin.style.alignItems = Align.Center;
            pin.style.marginLeft = 8f;
            pin.Add(new UIGlyph(UIGlyphs.Pin, isTracked ? UIWidgets.Ink(color) : UIPalette.Silhouette, 32f));
            pin.Add(UIKit.Text(isTracked ? Loc.Tr("Verfolgt", "Tracked") : Loc.Tr("Verfolgen", "Track"), 15f, isTracked ? UIWidgets.Ink(color) : UIPalette.InkSoft, true));
            UIKit.IgnorePicking(pin);
            pin.pickingMode = PickingMode.Position;
            UIKit.Radius(pin, 14f);
            UIKit.Pad(pin, 4f, 6f, 4f, 6f);
            UIKit.Clickable(pin, () => { QuestTracker.Track(isTracked ? null : id); Phone.Refresh(true); }, Color.clear, UIPalette.A(UIPalette.Ink, 0.06f));
            row.Add(pin);
            // „Aufgeben“: a stuck side quest must never block one of the 3 slots for good (not the story chain, not
            // while carrying its item)
            if (CanGiveUp(id))
            {
                var drop = UIKit.Col();
                drop.style.alignItems = Align.Center;
                drop.style.marginLeft = 4f;
                drop.Add(new UIGlyph(UIGlyphs.Cross, UIPalette.Silhouette, 28f));
                drop.Add(UIKit.Text(Loc.Tr("Aufgeben", "Give up"), 15f, UIPalette.InkSoft, true));
                UIKit.IgnorePicking(drop);
                drop.pickingMode = PickingMode.Position;
                UIKit.Radius(drop, 14f);
                UIKit.Pad(drop, 4f, 6f, 4f, 6f);
                string qid = id, qtitle = title;
                UIKit.Clickable(drop, () => UIDialog.Show(UIGlyphs.Cross, UIPalette.Danger, Loc.Tr("Aufgabe aufgeben?", "Give up this task?"), qtitle,
                    Loc.Tr("Abbrechen", "Cancel"), Loc.Tr("Aufgeben", "Give up"), () => { Quests.Drop(qid); Phone.Refresh(true); }, null, true),
                    Color.clear, UIPalette.A(UIPalette.Ink, 0.06f));
                row.Add(drop);
            }
            return row;
        }

        static bool CanGiveUp(string id)
        {
            if (string.IsNullOrEmpty(id) || Quests.IsStoryChain(id)) return false;
            var d = SideQuestDatabase.Get(id);
            var q = Quests.Get(id);
            if (d == null || q == null || q.done) return false;
            return !CarriesItemOf(d);
        }

        /// The local player carries an item of this quest ("q:&lt;item&gt;:&lt;n&gt;" in a Bring step target).
        static bool CarriesItemOf(SideQuestDef d)
        {
            string c = Carry.Current;
            if (string.IsNullOrEmpty(c) || !c.StartsWith("q:", System.StringComparison.Ordinal)) return false;
            int b = c.LastIndexOf(':');
            if (b <= 2) return false;
            string item = c.Substring(2, b - 2);
            for (int i = 0; i < d.steps.Length; i++)
            {
                var st = d.steps[i];
                if (st.kind != SqStep.Bring || string.IsNullOrEmpty(st.target)) continue;
                var parts = st.target.Split(',');
                for (int k = 0; k < parts.Length; k++) if (parts[k] == item) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ K.3 photo challenges
        void BuildChallenges(VisualElement body, GameState s)
        {
            var today = Challenges.Today;
            if (today == null || today.Count == 0) return;
            body.Add(PhoneKit.Section(Loc.Tr("Foto-Challenges heute", "Photo challenges today"), UIGlyphs.Camera));
            long reward = Challenges.Reward(s.followers);
            for (int i = 0; i < today.Count; i++)
            {
                var def = ChallengeDatabase.Get(today[i]);
                if (def == null) continue;
                bool done = Challenges.IsDone(def.id);
                var row = UIKit.Row();
                UIKit.Pad(row, 12f, 14f, 12f, 12f);
                UIKit.Radius(row, 20f);
                row.style.marginBottom = 8f;
                row.style.backgroundColor = done ? UIPalette.A(UIPalette.Leaf, 0.22f) : Color.white;
                row.Add(UIWidgets.IconTile(done ? UIGlyphs.Check : UIGlyphs.Camera, done ? UIPalette.LeafInk : UIPalette.Sun, 52f));
                var t = PhoneKit.T(def.Title, PhoneKit.Small, UIPalette.Ink, true);
                t.style.marginLeft = 12f;
                t.style.flexGrow = 1;
                t.style.flexShrink = 1;
                row.Add(t);
                var rw = UIKit.Col();
                rw.style.alignItems = Align.FlexEnd;
                rw.Add(UIKit.Text("+" + Loc.Money(reward), PhoneKit.Tiny, UIPalette.LeafInk, true));
                rw.Add(UIKit.Text("+10 % " + Loc.Tr("Aufrufe", "views"), 15f, UIPalette.InkSoft, true));
                row.Add(rw);
                body.Add(row);
            }
        }

        // ------------------------------------------------------------------ to-dos
        void BuildTodos(VisualElement body, GameState s)
        {
            int todo = 0;
            var todos = UIKit.Col();
            int banked = s.bankedFootage.Count;
            if (banked > 0) { todos.Add(Todo(UIGlyphs.Play, UIPalette.Danger, Loc.Tr("Clips hochladen", "Upload clips"), banked.ToString(), Phone.FinTube)); todo++; }
            int dive = Footage.DiveCount;
            if (dive > 0) { todos.Add(Todo(UIGlyphs.Camera, UIPalette.Warn, Loc.Tr("Clips sichern: an Land / aufs Boot", "Save clips: land / boat"), dive.ToString(), null)); todo++; }
            int fish = s.fishInventory.Count;
            if (fish > 0) { todos.Add(Todo(UIGlyphs.Market, UIPalette.Sun, Loc.Tr("Fische verkaufen", "Sell fish"), fish.ToString(), Phone.Market)); todo++; }
            for (int i = 0; i < s.deliveries.Count; i++)
            {
                var d = s.deliveries[i];
                if (d == null) continue;
                string name = Shop.DeliveryName(d);
                if (d.dropped)
                {
                    bool lost = d.Mishap != DeliveryMishap.None;
                    todos.Add(Todo(lost ? UIGlyphs.Smoke : UIGlyphs.Heli, lost ? UIPalette.Warn : UIPalette.Coral,
                                   (lost ? Loc.Tr("Paket suchen: ", "Find the parcel: ") : Loc.Tr("Kiste abholen: ", "Pick up crate: ")) + name, "", Phone.Map));
                    todo++;
                }
                else if (todo < 6) { todos.Add(Todo(UIGlyphs.Clock, UIPalette.Sky, name, UIKit.TimeOfDay(d.arrivalTime), Phone.Shop)); todo++; }
            }
            if (s.pendingUploads.Count > 0) { todos.Add(Todo(UIGlyphs.Bed, UIPalette.Violet, Loc.Tr("Schlafen → Video-Ergebnisse", "Sleep → video results"), "", null)); todo++; }
            if (todo > 0)
            {
                body.Add(PhoneKit.Section(Loc.Tr("Zu tun", "To do"), UIGlyphs.Check));
                body.Add(todos);
            }
        }

        // ------------------------------------------------------------------ next milestone / dive site
        void BuildNext(VisualElement body, GameState s)
        {
            var next = Progression.NextMilestone(s.followers);
            body.Add(PhoneKit.Section(Loc.Tr("Nächstes Ziel", "Next goal"), UIGlyphs.Star));
            var mc = PhoneKit.Card();
            if (next != null)
            {
                long prev = 0;
                foreach (var m in Progression.Milestones) if (m.followers <= s.followers && m.followers > prev) prev = m.followers;
                var mr = UIKit.Row();
                mr.Add(new UIGlyph(UIGlyphs.Person, UIPalette.Turquoise, 34f));
                var mt = UIKit.Text(next.Title, PhoneKit.Big, UIPalette.Ink, true);
                mt.style.marginLeft = 10f;
                mt.style.flexShrink = 1;
                mt.style.whiteSpace = WhiteSpace.Normal;
                mr.Add(mt);
                mc.Add(mr);
                var bar = PhoneKit.Bar((s.followers - prev) / (float)Math.Max(1L, next.followers - prev), UIPalette.Turquoise, 16f);
                bar.style.marginTop = 12f;
                mc.Add(bar);
                var left = UIKit.Text(UIKit.Compact(s.followers) + " / " + UIKit.Compact(next.followers), PhoneKit.Small, UIPalette.TurquoiseInk, true);
                left.style.marginTop = 6f;
                left.style.alignSelf = Align.FlexEnd;
                mc.Add(left);
            }
            else mc.Add(PhoneKit.T(Loc.Tr("Alle Meilensteine erreicht!", "All milestones reached!"), PhoneKit.Big, UIPalette.LeafInk, true));
            body.Add(mc);

            DiveSite nextSite = null;
            foreach (var site in DiveSites.All) if (!site.IsStorySite && !Progression.IsSiteUnlocked(site)) { nextSite = site; break; }
            if (nextSite == null) return;
            body.Add(PhoneKit.Section(Loc.Tr("Nächster Tauchplatz", "Next dive site"), UIGlyphs.Pin));
            var sc = PhoneKit.Card();
            sc.style.borderLeftWidth = 8f;
            sc.style.borderLeftColor = nextSite.mapColor;
            var sr = UIKit.Row();
            sr.Add(new UIGlyph(UIGlyphs.Lock, UIPalette.SunInk, 30f));
            var sn = UIKit.Text(nextSite.Name, PhoneKit.Big, UIPalette.Ink, true);
            sn.style.marginLeft = 10f;
            sn.style.flexShrink = 1;
            sn.style.whiteSpace = WhiteSpace.Normal;
            sr.Add(sn);
            sc.Add(sr);
            if (nextSite.prevSiteId != null)
            {
                var prevSite = DiveSites.Get(nextSite.prevSiteId);
                float comp = Journal.SiteCompletion(nextSite.prevSiteId);
                sc.Add(Requirement(UIGlyphs.Book, (prevSite != null ? prevSite.Name : "") + " " + Loc.Percent(nextSite.prevCompletionNeeded),
                                   comp / Mathf.Max(0.01f, nextSite.prevCompletionNeeded), Loc.Percent(comp)));
            }
            if (nextSite.followersNeeded > 0)
                sc.Add(Requirement(UIGlyphs.Person, UIKit.Compact(nextSite.followersNeeded) + Loc.Tr(" Follower", " followers"),
                                   s.followers / (float)nextSite.followersNeeded, UIKit.Compact(s.followers)));
            body.Add(sc);
        }

        // ------------------------------------------------------------------ collection (L.6)
        void BuildCollection(VisualElement body)
        {
            body.Add(PhoneKit.Section(Loc.Tr("Sammlung", "Collection"), UIGlyphs.Shell));
            var grid = UIKit.Row();
            grid.style.flexWrap = Wrap.Wrap;
            grid.style.justifyContent = Justify.SpaceBetween;
            for (int k = 0; k <= (int)CollectibleKind.Secret; k++)
            {
                var kind = (CollectibleKind)k;
                int have = Collectibles.Count(kind), total = Collectibles.Total(kind);
                var tile = UIKit.Col();
                tile.style.width = Length.Percent(31.5f);
                tile.style.alignItems = Align.Center;
                tile.style.backgroundColor = Color.white;
                UIKit.Radius(tile, 20f);
                UIKit.Pad(tile, 14f, 6f, 12f, 6f);
                tile.style.marginBottom = 10f;
                bool any = have > 0;
                tile.Add(new UIGlyph(UIGlyphs.ForCollectible(kind), any ? UIPalette.SunInk : UIPalette.Silhouette, 48f));
                var cnt = UIKit.Text(total > 0 ? have + "/" + total : have.ToString(), PhoneKit.Body, any ? UIPalette.Ink : UIPalette.Silhouette, true);
                cnt.style.marginTop = 6f;
                tile.Add(cnt);
                var kn = UIKit.Text(Collectibles.KindName(kind), PhoneKit.Tiny, UIPalette.InkSoft);
                kn.style.whiteSpace = WhiteSpace.Normal;
                kn.style.unityTextAlign = TextAnchor.MiddleCenter;
                tile.Add(kn);
                grid.Add(tile);
            }
            body.Add(grid);

            // missing secrets as „?“ – the clue text once it was heard (npc: UITaskInfo.SecretClue)
            int secretsTotal = Collectibles.Total(CollectibleKind.Secret), secretsHave = Collectibles.Count(CollectibleKind.Secret);
            if (secretsTotal <= 0) return;
            var sec = PhoneKit.Card(14f);
            var head = UIKit.Row();
            head.Add(new UIGlyph(UIGlyphs.Question, UIPalette.Violet, 28f));
            head.Add(UIKit.Text(" " + Loc.Tr("Geheimnisse", "Secrets") + " · " + secretsHave + "/" + secretsTotal, PhoneKit.Small, UIPalette.Ink, true));
            sec.Add(head);
            var dots = UIKit.Row();
            dots.style.flexWrap = Wrap.Wrap;
            dots.style.marginTop = 8f;
            for (int i = 0; i < secretsTotal; i++)
            {
                bool found = i < secretsHave;
                var d = UIKit.Box();
                UIKit.Size(d, 40f, 40f);
                UIKit.Radius(d, 12f);
                d.style.marginRight = 6f; d.style.marginBottom = 6f;
                d.style.backgroundColor = found ? UIPalette.A(UIPalette.Violet, 0.25f) : UIPalette.CardAlt;
                UIKit.Center(d);
                d.Add(found ? new UIGlyph(UIGlyphs.Star, UIPalette.Violet, 24f) : (VisualElement)UIKit.Text("?", 22f, UIPalette.Silhouette, true));
                dots.Add(d);
            }
            sec.Add(dots);
            if (UITaskInfo.SecretClue != null)
            {
                for (int i = 0; i < secretsTotal; i++)
                {
                    string clue = null;
                    try { clue = UITaskInfo.SecretClue(i); } catch (Exception) { clue = null; }
                    if (string.IsNullOrEmpty(clue)) continue;
                    var c = UIWidgets.Notice("„" + clue + "“", UIPalette.Violet, UIGlyphs.Question, PhoneKit.Tiny);
                    c.style.marginTop = 6f;
                    c.style.marginBottom = 0f;
                    sec.Add(c);
                }
            }
            body.Add(sec);
        }

        VisualElement Todo(UIGlyph.DrawFn icon, Color color, string text, string value, UIPhoneApp target)
        {
            var row = UIKit.Row();
            UIKit.Pad(row, 12f, 16f, 12f, 12f);
            UIKit.Radius(row, 20f);
            row.style.marginBottom = 8f;
            row.Add(UIWidgets.IconTile(icon, color, 52f));
            var t = PhoneKit.T(text, PhoneKit.Body, UIPalette.Ink, true);
            t.style.marginLeft = 12f;
            t.style.flexShrink = 1;
            t.style.flexGrow = 1;
            row.Add(t);
            if (!string.IsNullOrEmpty(value)) row.Add(UIKit.Pill(value, UIPalette.CardAlt, UIPalette.Ink, PhoneKit.Small));
            if (target != null)
            {
                var chev = new UIGlyph(UIGlyphs.ChevronRight, UIPalette.Silhouette, 28f);
                chev.style.marginLeft = 6f;
                row.Add(chev);
                UIKit.Clickable(row, () => Phone.OpenApp(target), Color.white, UIPalette.CardAlt);
            }
            else row.style.backgroundColor = Color.white;
            UIKit.IgnorePicking(row);
            row.pickingMode = PickingMode.Position;
            return row;
        }

        static VisualElement Requirement(UIGlyph.DrawFn icon, string label, float progress01, string value)
        {
            var col = UIKit.Col();
            col.style.marginTop = 12f;
            var r = UIKit.Row();
            bool done = progress01 >= 1f;
            r.Add(new UIGlyph(done ? UIGlyphs.Check : icon, done ? UIPalette.LeafInk : UIPalette.InkSoft, 26f));
            var l = PhoneKit.T(label, PhoneKit.Small, UIPalette.Ink, true);
            l.style.marginLeft = 8f;
            l.style.flexShrink = 1;
            l.style.flexGrow = 1;
            r.Add(l);
            r.Add(UIKit.Text(value, PhoneKit.Small, done ? UIPalette.LeafInk : UIPalette.InkSoft, true));
            col.Add(r);
            var bar = PhoneKit.Bar(Mathf.Clamp01(progress01), done ? UIPalette.LeafInk : UIPalette.Sun, 12f);
            bar.style.marginTop = 6f;
            col.Add(bar);
            return col;
        }
    }
}
