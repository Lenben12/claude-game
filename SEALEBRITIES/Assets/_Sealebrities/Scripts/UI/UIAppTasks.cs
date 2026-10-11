using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// "Aufgaben": the current tutorial quest (NEW badge until opened), quick to-dos derived from the game state
    /// (upload clips, sell fish, pick up a crate), the next milestone, the next dive-site unlock and the collectibles.
    /// Quests are no longer on screen permanently – a short push notification announces each new one.
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

        /// Called by the UI controller on GameEvents.QuestChanged. isNew = the task itself changed (arms the badge);
        /// progress updates of the same task ("(2/5)", "Noch 40 $") refresh the app silently.
        public void SetQuest(string text, bool isNew)
        {
            text = text ?? "";
            if (text == quest) return;
            quest = text;
            if (text.Length == 0) questNew = false;
            else if (isNew) questNew = !(UIState.PhoneOpen && Phone != null && Phone.Current == this);
            Phone?.MarkDirty();
        }

        public void SetQuest(string text) => SetQuest(text, true);

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

            // current quest
            var qc = PhoneKit.Card(20f);
            qc.style.borderLeftWidth = 8f;
            qc.style.borderLeftColor = UIPalette.Leaf;
            var qh = UIKit.Row();
            qh.Add(new UIGlyph(UIGlyphs.Flag, UIPalette.LeafInk, 34f));
            var qt = UIKit.Text(quest.Length > 0 ? Loc.Tr("Aktuell", "Current") : Loc.Tr("Freies Spiel", "Free play"), PhoneKit.Small, UIPalette.LeafInk, true);
            qt.style.marginLeft = 10f;
            qh.Add(qt);
            qc.Add(qh);
            var qtext = PhoneKit.T(quest.Length > 0 ? quest : Loc.Tr("Filme Tiere, sammle Follower – Ziel: 1.000.000!", "Film animals, grow your followers – goal: 1,000,000!"),
                                   PhoneKit.Big, UIPalette.Ink, true);
            qtext.style.marginTop = 8f;
            qc.Add(qtext);
            body.Add(qc);

            // to-dos from the state
            int todo = 0;
            var todos = UIKit.Col();
            int banked = s.bankedFootage.Count;
            if (banked > 0) { todos.Add(Todo(UIGlyphs.Play, UIPalette.Danger, Loc.Tr("Clips hochladen", "Upload clips"), banked.ToString(), Phone.FinTube)); todo++; }
            int dive = Footage.DiveCount;
            if (dive > 0) { todos.Add(Todo(UIGlyphs.Camera, UIPalette.Warn, Loc.Tr("Clips sichern: an Land / aufs Boot", "Save clips: get to land / the boat"), dive.ToString(), null)); todo++; }
            int fish = s.fishInventory.Count;
            if (fish > 0) { todos.Add(Todo(UIGlyphs.Market, UIPalette.Sun, Loc.Tr("Fische verkaufen", "Sell fish"), fish.ToString(), Phone.Market)); todo++; }
            for (int i = 0; i < s.deliveries.Count; i++)
            {
                var d = s.deliveries[i];
                if (d == null) continue;
                var item = GearDatabase.Get(d.itemId);
                string name = item != null ? item.Name : d.itemId;
                if (d.dropped) { todos.Add(Todo(UIGlyphs.Heli, UIPalette.Coral, Loc.Tr("Kiste abholen: ", "Pick up crate: ") + name, "", Phone.Map)); todo++; }
                else if (todo < 6) { todos.Add(Todo(UIGlyphs.Clock, UIPalette.Sky, name, UIKit.TimeOfDay(d.arrivalTime), Phone.Shop)); todo++; }
            }
            if (s.pendingUploads.Count > 0) { todos.Add(Todo(UIGlyphs.House, UIPalette.Violet, Loc.Tr("Schlafen → Video-Ergebnisse", "Sleep → video results"), "", null)); todo++; }
            if (todo > 0)
            {
                body.Add(PhoneKit.Section(Loc.Tr("Zu tun", "To do"), UIGlyphs.Check));
                body.Add(todos);
            }

            // next milestone
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
                mr.Add(mt);
                mc.Add(mr);
                var md = PhoneKit.T(next.Description, PhoneKit.Small, UIPalette.InkSoft);
                md.style.marginTop = 4f;
                mc.Add(md);
                var bar = PhoneKit.Bar((s.followers - prev) / (float)System.Math.Max(1L, next.followers - prev), UIPalette.Turquoise, 16f);
                bar.style.marginTop = 12f;
                mc.Add(bar);
                var left = UIKit.Text(UIKit.Compact(s.followers) + " / " + UIKit.Compact(next.followers), PhoneKit.Small, UIPalette.TurquoiseInk, true);
                left.style.marginTop = 6f;
                left.style.alignSelf = Align.FlexEnd;
                mc.Add(left);
            }
            else mc.Add(PhoneKit.T(Loc.Tr("Alle Meilensteine erreicht!", "All milestones reached!"), PhoneKit.Big, UIPalette.LeafInk, true));
            body.Add(mc);

            // next dive site
            DiveSite nextSite = null;
            foreach (var site in DiveSites.All) if (!Progression.IsSiteUnlocked(site)) { nextSite = site; break; }
            if (nextSite != null)
            {
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
                    var prev = DiveSites.Get(nextSite.prevSiteId);
                    float comp = Journal.SiteCompletion(nextSite.prevSiteId);
                    sc.Add(Requirement(UIGlyphs.Book, (prev != null ? prev.Name : "") + " " + Loc.Percent(nextSite.prevCompletionNeeded),
                                       comp / Mathf.Max(0.01f, nextSite.prevCompletionNeeded), Loc.Percent(comp)));
                }
                if (nextSite.followersNeeded > 0)
                    sc.Add(Requirement(UIGlyphs.Person, UIKit.Compact(nextSite.followersNeeded) + Loc.Tr(" Follower", " followers"),
                                       s.followers / (float)nextSite.followersNeeded, UIKit.Compact(s.followers)));
                body.Add(sc);
            }

            // collectibles
            body.Add(PhoneKit.Section(Loc.Tr("Sammelstücke", "Collectibles"), UIGlyphs.Shell));
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
