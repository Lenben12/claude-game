using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Sealog journal (L.2 / K.2): pages (dive sites, open sea, island, the Fog Isle once unlocked) with species cards –
    /// „×23“ sightings badge, depth chip „2–12 m“, silhouettes until the Sealog PHOTO registers a species („Gesichtet ×3 –
    /// Foto fehlt!“). Detail page: record bar, counters (found / photos / lives at / you saw it at / found at), a vertical
    /// 0–100 m depth bar (band teal, your range yellow), time-of-day icons, size tiers and a fact. Extra tab „Tiefen“: a
    /// ruler with every species at its preferred depth – "which fish at which depth". Filter chip „Nach Tiefe sortieren“.
    public class UIAppSealog : UIPhoneApp
    {
        const int DepthTab = -1;
        /// Post-finale tabs (Design_Runde2 I, Q79): FilmLegends – the 5 named legends and the record hunt gold badges.
        const int LegendsTab = -2, RecordTab = -3;

        int pageIndex;
        bool sortByDepth;
        SpeciesDef detail;
        Texture2D photo;
        string photoFile;
        readonly List<SpeciesDef> list = new List<SpeciesDef>();

        static readonly Color BandTeal = UIPalette.Rgb(0x3FA9B5);
        static readonly Color SeenYellow = UIPalette.Rgb(0xF2C94C);

        public override string Id => "sealog";
        public override string Title => "Sealog";
        public override Color Color => UIPalette.Turquoise;
        public override UIGlyph.DrawFn Icon => UIGlyphs.Book;

        public override string HeaderTitle
        {
            get
            {
                if (detail == null) return "Sealog";
                return IsDiscovered(detail.id) ? detail.Name : "???";
            }
        }

        public override void OnOpen()
        {
            detail = null;
            var p = Refs.Player;
            if (p != null && !(p is Object o && o == null) && p.Root != null)
            {
                var site = WorldInfo.SiteAt(p.Root.position);
                int i = site != null ? SealogPages.IndexOf(site.id) : -1;
                if (i >= 0 && PageVisible(i)) pageIndex = i;
            }
        }

        public override void OnClose() { FreePhoto(); }

        public override bool Back()
        {
            if (detail != null) { detail = null; FreePhoto(); return true; }
            return false;
        }

        public void ShowSpecies(SpeciesDef s)
        {
            detail = s;
            if (s != null)
            {
                int i = SealogPages.IndexOf(s.Page);
                if (i >= 0) pageIndex = i;
            }
            Phone.Refresh(false);
        }

        void FreePhoto()
        {
            if (photo != null) Object.Destroy(photo);
            photo = null;
            photoFile = null;
        }

        public override void Build(VisualElement body)
        {
            if (GameManager.I == null) return;
            if (detail != null) BuildDetail(body, detail);
            else BuildIndex(body);
        }

        // ------------------------------------------------------------------ helpers
        static JournalEntry Entry(string id) => Journal.Find(id);

        static bool IsDiscovered(string id)
        {
            var e = GameManager.I != null ? Entry(id) : null;
            return e != null && e.discovered;
        }

        static string Band(SpeciesDef s)
        {
            if (s.land) return Loc.Tr("an Land", "on land");
            float a = Mathf.Max(0f, s.minDepth), b = Mathf.Max(a, s.maxDepth);
            return UIKit.DepthRange(a, b);
        }

        /// The Fog Isle page stays a locked „???“ tab until Toni's map (no spoiler); every other page is listed.
        static bool PageVisible(int i) => i >= 0 && i < SealogPages.All.Length;

        static bool PageLocked(SealogPages.Page p, out DiveSite site)
        {
            site = p.site ? DiveSites.Get(p.id) : null;
            return site != null && !Progression.IsSiteUnlocked(site);
        }

        static string PageName(SealogPages.Page p)
        {
            if (p.id == Zones.FogIsle && !StoryFlags.Has(StoryKeys.FogMap)) return "???";
            return p.Name;
        }

        /// Smallest size (cm) that classifies as the given tier (binary search over Progression.ClassifySize).
        static float TierStartCm(SpeciesDef s, int tier)
        {
            if (tier <= 0) return s.minCm;
            float lo = s.minCm, hi = s.maxCm * 1.3f + 1f;
            if ((int)Progression.ClassifySize(s, hi) < tier) return -1f;
            for (int i = 0; i < 32; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if ((int)Progression.ClassifySize(s, mid) >= tier) hi = mid; else lo = mid;
            }
            return hi;
        }

        // ------------------------------------------------------------------ index
        void BuildIndex(VisualElement body)
        {
            int total = SpeciesDatabase.Count, found = Journal.DiscoveredCount;
            bool complete = total > 0 && found >= total;

            var head = PhoneKit.Card();
            var hr = UIKit.Row();
            hr.Add(new UIGlyph(complete ? UIGlyphs.Trophy : UIGlyphs.Book, complete ? UIPalette.Rgb(0xE8C65A) : UIPalette.Turquoise, 40f));
            var cnt = UIKit.Text(found + " / " + total, PhoneKit.Title, UIPalette.Ink, true);
            cnt.style.marginLeft = 12f;
            hr.Add(cnt);
            hr.Add(UIKit.Flex());
            hr.Add(UIKit.Text(Loc.Percent(total > 0 ? found / (float)total : 0f), PhoneKit.Body, complete ? UIPalette.SunInk : UIPalette.TurquoiseInk, true));
            head.Add(hr);
            var hb = PhoneKit.Bar(total > 0 ? found / (float)total : 0f, complete ? UIPalette.Rgb(0xE8C65A) : UIPalette.Turquoise, 14f);
            hb.style.marginTop = 12f;
            head.Add(hb);
            // K.2 flow hint: the Sealog needs a photo
            var hint = UIKit.Row();
            hint.style.marginTop = 10f;
            hint.Add(new UIGlyph(UIGlyphs.Camera, UIPalette.InkSoft, 24f));
            // the same combo the tutorial / Horst teach: Aim (zoom) + Use (photo)
            hint.Add(UIKit.Text(" " + GameInput.BindingLabel(GameAction.Aim) + " + " + GameInput.BindingLabel(GameAction.Use) + " · " + Loc.Tr("Foto = Eintrag", "photo = entry"), PhoneKit.Tiny, UIPalette.InkSoft, true));
            head.Add(hint);
            body.Add(head);

            // page tabs + the depth ruler tab
            var tabs = UIKit.Row();
            tabs.style.flexWrap = Wrap.Wrap;
            tabs.style.marginTop = 4f;
            tabs.style.marginBottom = 4f;
            var pages = SealogPages.All;
            bool hunt = FilmLegends.IsHuntActive;
            pageIndex = Mathf.Clamp(pageIndex, hunt ? RecordTab : DepthTab, pages.Length - 1);
            for (int i = 0; i < pages.Length; i++) tabs.Add(PageTab(pages[i], i));
            tabs.Add(DepthTabButton());
            if (hunt)
            {
                tabs.Add(ExtraTab(LegendsTab, UIGlyphs.Trophy, UIPalette.Rgb(0xE8C65A), Loc.Tr("Legenden", "Legends")));
                tabs.Add(ExtraTab(RecordTab, UIGlyphs.Star, UIPalette.Rgb(0xE8C65A), Loc.Tr("Rekordjagd", "Record hunt")));
            }
            body.Add(tabs);

            if (pageIndex == DepthTab) { BuildDepthRuler(body); return; }
            if (pageIndex == LegendsTab) { BuildLegends(body); return; }
            if (pageIndex == RecordTab) { BuildRecordHunt(body); return; }

            var page = pages[pageIndex];
            body.Add(PageCard(page));

            var sortRow = UIKit.Row();
            sortRow.style.marginTop = 2f;
            sortRow.style.marginBottom = 6f;
            var chip = UIKit.Row();
            UIKit.Radius(chip, 999f);
            UIKit.Pad(chip, 8f, 14f, 8f, 12f);
            UIKit.Border(chip, 2f, sortByDepth ? UIPalette.TurquoiseInk : UIPalette.A(UIPalette.Ink, 0.1f));
            chip.Add(new UIGlyph(UIGlyphs.Sort, sortByDepth ? UIPalette.TurquoiseInk : UIPalette.InkSoft, 24f));
            chip.Add(UIKit.Text(" " + Loc.Tr("Nach Tiefe sortieren", "Sort by depth"), PhoneKit.Tiny, sortByDepth ? UIPalette.TurquoiseInk : UIPalette.InkSoft, true));
            UIKit.IgnorePicking(chip);
            chip.pickingMode = PickingMode.Position;
            UIKit.Clickable(chip, () => { sortByDepth = !sortByDepth; Phone.Refresh(true); }, sortByDepth ? UIPalette.A(UIPalette.Turquoise, 0.18f) : Color.white, UIPalette.CardAlt);
            sortRow.Add(chip);
            body.Add(sortRow);

            SpeciesDatabase.ByPage(page.id, list);
            if (sortByDepth) list.Sort((a, b) => a.PrefDepth.CompareTo(b.PrefDepth));
            var grid = UIKit.Row();
            grid.style.flexWrap = Wrap.Wrap;
            grid.style.alignItems = Align.FlexStart;
            grid.style.justifyContent = Justify.SpaceBetween;
            for (int i = 0; i < list.Count; i++) grid.Add(SpeciesCard(list[i]));
            body.Add(grid);
        }

        VisualElement PageTab(SealogPages.Page page, int index)
        {
            bool sel = index == pageIndex;
            bool locked = PageLocked(page, out var site);
            Color c = site != null ? site.mapColor : page.id == "island" ? UIPalette.Leaf : UIPalette.Sky;
            var tab = UIKit.Row();
            UIKit.Radius(tab, 999f);
            UIKit.Pad(tab, 10f, 16f, 10f, 14f);
            tab.style.marginRight = 8f; tab.style.marginBottom = 8f;
            UIKit.Border(tab, 2f, sel ? c : UIPalette.A(UIPalette.Ink, 0.08f));
            Color bg = sel ? UIPalette.A(c, 0.22f) : Color.white;
            UIKit.Clickable(tab, () => { pageIndex = index; Phone.Refresh(false); }, bg, UIPalette.A(c, 0.16f));
            if (locked)
            {
                var g = new UIGlyph(UIGlyphs.Lock, UIPalette.Silhouette, 22f);
                g.style.marginRight = 6f;
                tab.Add(g);
            }
            else tab.Add(UIKit.Dot(c, 14f));
            var name = UIKit.Text(PageName(page), PhoneKit.Small, locked ? UIPalette.Silhouette : UIPalette.Ink, true);
            name.style.marginLeft = locked ? 0f : 8f;
            tab.Add(name);
            UIKit.IgnorePicking(tab);
            tab.pickingMode = PickingMode.Position;
            return tab;
        }

        VisualElement ExtraTab(int index, UIGlyph.DrawFn icon, Color c, string label)
        {
            bool sel = pageIndex == index;
            var tab = UIKit.Row();
            UIKit.Radius(tab, 999f);
            UIKit.Pad(tab, 10f, 16f, 10f, 14f);
            tab.style.marginRight = 8f; tab.style.marginBottom = 8f;
            UIKit.Border(tab, 2f, sel ? UIWidgets.Ink(c) : UIPalette.A(UIPalette.Ink, 0.08f));
            UIKit.Clickable(tab, () => { pageIndex = index; Phone.Refresh(false); }, sel ? UIPalette.A(c, 0.22f) : Color.white, UIPalette.A(c, 0.16f));
            tab.Add(new UIGlyph(icon, UIWidgets.Ink(c), 24f));
            var l = UIKit.Text(label, PhoneKit.Small, UIPalette.Ink, true);
            l.style.marginLeft = 6f;
            tab.Add(l);
            UIKit.IgnorePicking(tab);
            tab.pickingMode = PickingMode.Position;
            return tab;
        }

        // ------------------------------------------------------------------ post-finale: legends + record hunt
        void BuildLegends(VisualElement body)
        {
            var all = FilmLegends.All;
            body.Add(PhoneKit.Section(Loc.Tr("Legenden", "Legends") + "  " + FilmLegends.CaughtCount + "/" + all.Length, UIGlyphs.Trophy));
            for (int i = 0; i < all.Length; i++)
            {
                var l = all[i];
                bool caught = FilmLegends.IsCaught(l);
                var site = DiveSites.Get(l.siteId);
                var sp = l.Species;
                var card = PhoneKit.Card();
                var r = UIKit.Row();
                r.Add(UIWidgets.IconTile(caught ? UIGlyphs.Trophy : UIGlyphs.Camera, caught ? UIPalette.Rgb(0xE8C65A) : UIPalette.Silhouette, 52f));
                var col = UIKit.Col();
                col.style.marginLeft = 12f;
                col.style.flexGrow = 1;
                col.style.flexShrink = 1;
                col.Add(UIKit.Text(l.Name, PhoneKit.Body, UIPalette.Ink, true));
                string what = (sp != null && IsDiscovered(sp.id) ? sp.Name : "???") + " · " + Loc.Length(l.sizeCm);
                col.Add(UIKit.Text((site != null ? site.Name : l.siteId) + " · " + what, PhoneKit.Tiny, UIPalette.InkSoft, true));
                r.Add(col);
                r.Add(UIKit.Text(caught ? Loc.Tr("Gefilmt!", "Caught!") : Loc.Tr("Offen", "Open"), PhoneKit.Small, caught ? UIPalette.SunInk : UIPalette.InkSoft, true));
                card.Add(r);
                body.Add(card);
            }
        }

        void BuildRecordHunt(VisualElement body)
        {
            var all = SpeciesDatabase.All;
            body.Add(PhoneKit.Section(Loc.Tr("Rekordjagd", "Record hunt") + "  " + FilmLegends.GoldCount + " " + Loc.Tr("Gold", "gold"), UIGlyphs.Star));
            int shown = 0;
            for (int i = 0; i < all.Count; i++)
            {
                var sp = all[i];
                var e = Entry(sp.id);
                if (e == null || !e.discovered) continue;
                shown++;
                bool gold = FilmLegends.HasGold(sp.id);
                float target = FilmLegends.GoldTarget(sp);
                var row = UIKit.Row();
                UIKit.Pad(row, 10f, 12f, 10f, 12f);
                UIKit.Radius(row, 16f);
                row.style.marginBottom = 6f;
                row.style.backgroundColor = gold ? UIPalette.A(UIPalette.Rgb(0xE8C65A), 0.22f) : Color.white;
                row.Add(new UIGlyph(gold ? UIGlyphs.Trophy : UIGlyphs.Star, gold ? UIPalette.Rgb(0xE8C65A) : UIPalette.Silhouette, 30f));
                var name = UIKit.Text(" " + sp.Name, PhoneKit.Small, UIPalette.Ink, true);
                name.style.flexGrow = 1;
                name.style.flexShrink = 1;
                row.Add(name);
                row.Add(UIKit.Text(Loc.Length(e.recordCm) + " / " + Loc.Length(target), PhoneKit.Tiny, gold ? UIPalette.SunInk : UIPalette.InkSoft, true));
                body.Add(row);
            }
            if (shown == 0) body.Add(PhoneKit.Empty(UIGlyphs.Star, Loc.Tr("Erst Arten fürs Sealog fotografieren.", "Photograph species for the Sealog first.")));
        }

        VisualElement DepthTabButton()
        {
            bool sel = pageIndex == DepthTab;
            var tab = UIKit.Row();
            UIKit.Radius(tab, 999f);
            UIKit.Pad(tab, 10f, 16f, 10f, 14f);
            tab.style.marginRight = 8f; tab.style.marginBottom = 8f;
            UIKit.Border(tab, 2f, sel ? UIPalette.TurquoiseInk : UIPalette.A(UIPalette.Ink, 0.08f));
            UIKit.Clickable(tab, () => { pageIndex = DepthTab; Phone.Refresh(false); }, sel ? UIPalette.A(UIPalette.Turquoise, 0.22f) : Color.white, UIPalette.A(UIPalette.Turquoise, 0.16f));
            tab.Add(new UIGlyph(UIGlyphs.Depth, UIPalette.TurquoiseInk, 24f));
            var l = UIKit.Text(Loc.Tr("Tiefen", "Depths"), PhoneKit.Small, UIPalette.Ink, true);
            l.style.marginLeft = 6f;
            tab.Add(l);
            UIKit.IgnorePicking(tab);
            tab.pickingMode = PickingMode.Position;
            return tab;
        }

        VisualElement PageCard(SealogPages.Page page)
        {
            bool locked = PageLocked(page, out var site);
            Color c = site != null ? site.mapColor : UIPalette.Sky;
            var card = PhoneKit.Card();
            card.style.borderLeftWidth = 8f;
            card.style.borderLeftColor = c;

            var r = UIKit.Row();
            var name = UIKit.Text(site != null && !locked ? site.Name : PageName(page), PhoneKit.Big, UIPalette.Ink, true);
            name.style.flexShrink = 1;
            name.style.whiteSpace = WhiteSpace.Normal;
            r.Add(name);
            r.Add(UIKit.Flex());
            if (site != null)
            {
                var depth = UIKit.Row();
                depth.Add(new UIGlyph(UIGlyphs.Depth, UIPalette.InkSoft, 26f));
                depth.Add(UIKit.Text(UIKit.DepthRange(site.minDepth, site.maxDepth), PhoneKit.Small, UIPalette.InkSoft, true));
                r.Add(depth);
            }
            card.Add(r);

            if (site != null && locked && !Progression.SiteRequirementsMet(site, out string reason))
            {
                var n = UIWidgets.Notice(site.IsStorySite && !StoryFlags.Has(StoryKeys.FogMap) ? Loc.Tr("Nur mit Karte zu finden", "Only found with a map") : reason,
                                         UIPalette.Sun, UIGlyphs.Lock, PhoneKit.Small);
                n.style.marginTop = 10f;
                n.style.marginBottom = 0f;
                card.Add(n);
            }

            SpeciesDatabase.ByPage(page.id, list);
            int found = 0;
            for (int i = 0; i < list.Count; i++) if (IsDiscovered(list[i].id)) found++;
            float comp = list.Count > 0 ? found / (float)list.Count : 0f;
            var pr = UIKit.Row();
            pr.style.marginTop = 12f;
            pr.Add(new UIGlyph(UIGlyphs.Fish, UIPalette.InkSoft, 28f));
            var cn = UIKit.Text(found + " / " + list.Count, PhoneKit.Body, UIPalette.Ink, true);
            cn.style.marginLeft = 8f;
            pr.Add(cn);
            pr.Add(UIKit.Flex());
            pr.Add(UIKit.Text(Loc.Percent(comp), PhoneKit.Body, UIPalette.Ink, true));
            card.Add(pr);
            var bar = new UIBar(14f, comp >= 1f ? UIPalette.LeafInk : c);
            bar.style.marginTop = 8f;
            bar.SetValue(comp);
            DiveSite next = null;
            if (site != null) foreach (var s in DiveSites.All) if (s.prevSiteId == site.id) { next = s; break; }
            if (next != null) bar.AddMarker(next.prevCompletionNeeded, UIPalette.Ink);
            card.Add(bar);

            var gm = GameManager.I;
            if (next != null)
            {
                bool done = Progression.IsSiteUnlocked(next);
                card.Add(RewardLine(UIGlyphs.Pin, Loc.Percent(next.prevCompletionNeeded) + " → " + next.Name, done || comp >= next.prevCompletionNeeded, done));
            }
            if (site != null)
            {
                bool sponsor = gm.State.sponsorSites.Contains(site.id);
                card.Add(RewardLine(UIGlyphs.Coin, "100 % → " + Loc.Tr("Sponsor", "Sponsor"), sponsor, sponsor));
            }
            return card;
        }

        static VisualElement RewardLine(UIGlyph.DrawFn icon, string text, bool reached, bool done)
        {
            var row = UIKit.Row();
            row.style.marginTop = 10f;
            var box = UIKit.Box();
            UIKit.Size(box, 32f, 32f);
            UIKit.Radius(box, 16f);
            UIKit.Border(box, 2f, reached ? UIPalette.LeafInk : UIPalette.Silhouette);
            box.style.backgroundColor = done ? UIPalette.LeafInk : Color.clear;
            UIKit.Center(box);
            box.Add(done ? new UIGlyph(UIGlyphs.Check, Color.white, 22f) : new UIGlyph(icon, reached ? UIPalette.LeafInk : UIPalette.Silhouette, 20f));
            row.Add(box);
            var l = PhoneKit.T(text, PhoneKit.Small, reached ? UIPalette.Ink : UIPalette.InkSoft, true);
            l.style.marginLeft = 10f;
            l.style.flexShrink = 1;
            row.Add(l);
            return row;
        }

        VisualElement SpeciesCard(SpeciesDef s)
        {
            var e = Entry(s.id);
            bool disc = e != null && e.discovered;
            int seen = e != null ? Mathf.Max(e.sightings, e.timesFilmed) : 0;
            var card = UIKit.Col();
            card.style.width = Length.Percent(48.5f);
            card.style.marginBottom = 14f;
            card.style.alignItems = Align.Center;
            UIKit.Radius(card, 22f);
            UIKit.Pad(card, 14f, 10f, 44f, 10f);
            card.style.minHeight = 250f;
            UIKit.Clickable(card, () => ShowSpecies(s), disc ? Color.white : UIPalette.CardAlt, disc ? UIPalette.Raised2 : UIPalette.A(UIPalette.CardAlt, 0.7f));
            if (disc) UIKit.Lift(card, 0.8f);

            var rarity = UIKit.Dot(UIPalette.RarityColor(s.rarity), 14f);
            rarity.style.position = Position.Absolute;
            rarity.style.right = 14f; rarity.style.top = 14f;
            card.Add(rarity);

            card.Add(UIWidgets.SpeciesIcon(s, disc, 200f, 120f));
            var name = UIKit.Text(disc ? s.Name : "???", PhoneKit.Small, disc ? UIPalette.Ink : UIPalette.Silhouette, true);
            name.style.marginTop = 8f;
            name.style.whiteSpace = WhiteSpace.Normal;
            name.style.unityTextAlign = TextAnchor.MiddleCenter;
            card.Add(name);
            if (disc)
            {
                var tier = (SizeTier)e.recordTier;
                var c = tier == SizeTier.Normal ? UIPalette.CardAlt : Progression.TierColor(tier);
                var pill = UIKit.Pill(Loc.Length(e.recordCm), c, UIPalette.Ink, PhoneKit.Tiny);
                pill.style.alignSelf = Align.Center;
                pill.style.marginTop = 6f;
                card.Add(pill);
            }
            else if (seen > 0)
            {
                // sighted while filming, but no Sealog photo yet (K.2)
                var miss = UIKit.Row();
                miss.style.marginTop = 6f;
                miss.Add(new UIGlyph(UIGlyphs.Camera, UIPalette.CoralInk, 22f));
                var mt = UIKit.Text(" " + Loc.Tr("Foto fehlt!", "Photo missing!"), PhoneKit.Tiny, UIPalette.CoralInk, true);
                miss.Add(mt);
                card.Add(miss);
            }
            else
            {
                var lives = UIKit.Text(Loc.Tr("Lebt in ", "Lives at ") + Band(s), PhoneKit.Tiny, UIPalette.InkSoft, true);
                lives.style.marginTop = 6f;
                card.Add(lives);
            }

            // bottom-left depth chip, bottom-right sightings badge
            var depth = UIKit.Pill(Band(s), UIPalette.A(BandTeal, 0.2f), UIWidgets.Ink(BandTeal), PhoneKit.Tiny);
            depth.style.position = Position.Absolute;
            depth.style.left = 10f; depth.style.bottom = 10f;
            card.Add(depth);
            if (seen > 0)
            {
                var badge = UIKit.Pill("×" + seen, disc ? UIPalette.Turquoise : UIPalette.Silhouette, Color.white, PhoneKit.Tiny);
                badge.style.position = Position.Absolute;
                badge.style.right = 10f; badge.style.bottom = 10f;
                card.Add(badge);
            }
            UIKit.IgnorePicking(card);
            card.pickingMode = PickingMode.Position;
            return card;
        }

        // ------------------------------------------------------------------ depth ruler ("Tiefen")
        /// y position (px) of a depth on the ruler: 40 px per metre down to 30 m, then 12 px per metre.
        static float DepthY(float d)
        {
            d = Mathf.Clamp(d, 0f, 100f);
            return d <= 30f ? d * 40f : 1200f + (d - 30f) * 12f;
        }

        void BuildDepthRuler(VisualElement body)
        {
            var intro = PhoneKit.T(Loc.Tr("Welcher Fisch lebt in welcher Tiefe?", "Which fish lives at which depth?"), PhoneKit.Small, UIPalette.InkSoft, true);
            intro.style.marginBottom = 8f;
            body.Add(intro);

            var all = SpeciesDatabase.All;
            list.Clear();
            for (int i = 0; i < all.Count; i++) if (!all[i].land) list.Add(all[i]);
            list.Sort((a, b) => a.PrefDepth.CompareTo(b.PrefDepth));

            const float RulerW = 74f, IconW = 96f, IconH = 70f, Lanes = 4f;
            float height = DepthY(100f) + IconH + 20f;
            var host = UIKit.Box();
            host.style.height = height;
            host.style.backgroundColor = Color.white;
            UIKit.Radius(host, 22f);
            UIKit.Image(host, UIKit.VGradient(UIPalette.Rgb(0x284F80), UIPalette.Rgb(0x8FD3E0)), false);
            host.style.overflow = Overflow.Hidden;
            body.Add(host);

            // ruler ticks
            for (int d = 0; d <= 100; d += 10)
            {
                if (d > 30 && d % 20 != 0 && d != 100) continue;
                float y = DepthY(d) + 10f;
                var line = UIKit.Box();
                line.style.position = Position.Absolute;
                line.style.left = RulerW - 14f; line.style.right = 0f; line.style.top = y;
                line.style.height = 1f;
                line.style.backgroundColor = new Color(1f, 1f, 1f, 0.25f);
                host.Add(line);
                var l = UIKit.Text(d + " m", PhoneKit.Tiny, Color.white, true);
                UIKit.Outline(l, 1.2f, 0.2f);
                l.style.position = Position.Absolute;
                l.style.left = 10f; l.style.top = y - 12f;
                host.Add(l);
            }

            // icons in lanes (no overlaps): each lane remembers its lowest used y
            var laneBottom = new float[(int)Lanes];
            for (int i = 0; i < laneBottom.Length; i++) laneBottom[i] = -999f;
            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i];
                float y = DepthY(s.PrefDepth) + 10f - IconH * 0.5f;
                int lane = -1;
                for (int k = 0; k < laneBottom.Length; k++) if (laneBottom[k] <= y) { lane = k; break; }
                if (lane < 0)
                {
                    lane = 0;
                    for (int k = 1; k < laneBottom.Length; k++) if (laneBottom[k] < laneBottom[lane]) lane = k;
                    y = laneBottom[lane];
                }
                laneBottom[lane] = y + IconH;
                bool disc = IsDiscovered(s.id);
                var cell = UIKit.Col();
                cell.style.position = Position.Absolute;
                cell.style.left = RulerW + lane * (IconW + 8f);
                cell.style.top = Mathf.Max(0f, y);
                cell.style.width = IconW;
                cell.style.alignItems = Align.Center;
                cell.Add(UIWidgets.SpeciesIcon(s, disc, IconW, IconH - 20f));
                var dl = UIKit.Text(Mathf.RoundToInt(s.PrefDepth) + " m", 16f, Color.white, true);
                UIKit.Outline(dl, 1.2f, 0.2f);
                cell.Add(dl);
                UIKit.IgnorePicking(cell);
                cell.pickingMode = PickingMode.Position;
                var sp = s;
                cell.RegisterCallback<ClickEvent>(_ => { UIAudio.Click(); ShowSpecies(sp); });
                host.Add(cell);
            }
        }

        // ------------------------------------------------------------------ detail page
        void BuildDetail(VisualElement body, SpeciesDef s)
        {
            var e = Entry(s.id);
            bool disc = e != null && e.discovered;
            int seen = e != null ? Mathf.Max(e.sightings, e.timesFilmed) : 0;
            var site = DiveSites.Get(s.homeSite ?? s.siteId);
            SizeTier recTier = disc ? (SizeTier)e.recordTier : SizeTier.Normal;
            Color tierColor = Progression.TierColor(recTier);

            var hero = UIKit.Col();
            hero.style.height = 300f;
            UIKit.Center(hero);
            UIKit.Radius(hero, 26f);
            UIKit.Image(hero, UIKit.VGradient(UIPalette.Rgb(0x8FD3E0), UIPalette.Rgb(0xEAF4F6)), false);
            if (disc) { hero.style.borderBottomWidth = 6f; hero.style.borderBottomColor = tierColor; }
            hero.Add(UIWidgets.SpeciesIcon(s, disc, 440f, 270f));
            body.Add(hero);

            var name = UIKit.Text(disc ? s.Name : "???", PhoneKit.Title, UIPalette.Ink, true);
            name.style.marginTop = 14f;
            name.style.whiteSpace = WhiteSpace.Normal;
            body.Add(name);
            if (disc)
            {
                var latin = UIKit.Text(s.latin, PhoneKit.Small, UIPalette.InkSoft);
                latin.style.unityFontStyleAndWeight = FontStyle.Italic;
                body.Add(latin);
            }

            var pills = UIKit.Row();
            pills.style.flexWrap = Wrap.Wrap;
            pills.style.marginTop = 10f;
            AddPill(pills, Loc.RarityName(s.rarity), UIPalette.RarityColor(s.rarity));
            int pi = SealogPages.IndexOf(s.Page);
            if (pi >= 0) AddPill(pills, PageName(SealogPages.All[pi]), site != null ? site.mapColor : UIPalette.Sky);
            if (s.IsDangerous) AddPill(pills, Loc.Tr("Gefährlich", "Dangerous"), UIPalette.Coral);
            if (s.glows) AddPill(pills, Loc.Tr("Leuchtet", "Glows"), UIPalette.Violet);
            body.Add(pills);

            // record bar
            var rec = PhoneKit.Card();
            rec.style.marginTop = 12f;
            var rr = UIKit.Row();
            rr.Add(new UIGlyph(UIGlyphs.Star, disc ? UIWidgets.Ink(tierColor) : UIPalette.Silhouette, 34f));
            var big = UIKit.Text(disc ? Loc.Length(e.recordCm) + " · " + Loc.TierName(recTier)
                                      : seen > 0 ? Loc.Tr($"Gesichtet ×{seen} – Foto fehlt!", $"Sighted ×{seen} – photo missing!") : Loc.Tr("Noch kein Foto", "No photo yet"),
                                 PhoneKit.Big, disc ? UIPalette.Ink : UIPalette.InkSoft, true);
            big.style.marginLeft = 10f;
            big.style.whiteSpace = WhiteSpace.Normal;
            big.style.flexShrink = 1;
            rr.Add(big);
            rec.Add(rr);
            rec.Add(SizeScale(s, disc ? e.recordCm : -1f));
            body.Add(rec);

            // L.2 counters + depth
            body.Add(PhoneKit.Section(Loc.Tr("Fundbuch", "Sightings"), UIGlyphs.Depth));
            var cc = PhoneKit.Card(14f);
            var row = UIKit.Row();
            row.style.alignItems = Align.FlexStart;
            var left = UIKit.Col();
            left.style.flexGrow = 1;
            left.style.flexShrink = 1;
            left.Add(UIWidgets.InfoLine(Loc.Tr("Gefunden", "Found"), seen + "×", null, PhoneKit.Small));
            left.Add(UIWidgets.InfoLine(Loc.Tr("Fotos", "Photos"), (e != null ? e.photos : 0).ToString(), null, PhoneKit.Small));
            left.Add(UIWidgets.InfoLine(Loc.Tr("Lebt in", "Lives at"), Band(s), UIWidgets.Ink(BandTeal), PhoneKit.Small));
            left.Add(UIWidgets.InfoLine(Loc.Tr("Von dir gesehen", "You saw it at"),
                e != null && e.HasDepthSeen ? UIKit.DepthRange(e.minDepthSeen, Mathf.Max(e.minDepthSeen, e.maxDepthSeen)) : "–", UIPalette.SunInk, PhoneKit.Small));
            // time of day
            var tod = UIKit.Row();
            tod.style.marginTop = 10f;
            tod.Add(DayIcon(UIGlyphs.Sun, UIPalette.SunInk, (s.dayMask & DayMask.Day) != 0, Loc.Tr("Tag", "Day")));
            tod.Add(DayIcon(UIGlyphs.Sun, UIPalette.CoralInk, (s.dayMask & DayMask.Dusk) != 0, Loc.Tr("Dämmerung", "Dusk")));
            tod.Add(DayIcon(UIGlyphs.Moon, UIPalette.Violet, (s.dayMask & DayMask.Night) != 0, Loc.Tr("Nacht", "Night")));
            left.Add(tod);
            row.Add(left);
            if (!s.land)
            {
                var bar = new UIDepthBar(s.BandMin(false), s.BandMax(false),
                    e != null && e.HasDepthSeen ? e.minDepthSeen : -1f, e != null && e.HasDepthSeen ? e.maxDepthSeen : -1f, BandTeal, SeenYellow);
                bar.style.width = 92f;
                bar.style.height = 270f;
                bar.style.marginLeft = 10f;
                row.Add(bar);
            }
            cc.Add(row);
            // found at: zone chips
            if (e != null && e.zoneMask != 0)
            {
                var zl = UIKit.Text(Loc.Tr("Fundorte", "Found at"), PhoneKit.Tiny, UIPalette.InkSoft, true);
                zl.style.marginTop = 10f;
                cc.Add(zl);
                var chips = UIKit.Row();
                chips.style.flexWrap = Wrap.Wrap;
                chips.style.marginTop = 6f;
                for (int i = 0; i < Zones.All.Length && i < 31; i++)
                    if ((e.zoneMask & (1 << i)) != 0) AddPill(chips, Zones.Name(Zones.All[i]), BandTeal);
                cc.Add(chips);
            }
            body.Add(cc);

            // best own photo
            if (disc)
            {
                PhotoEntry pe = null;
                if (!string.IsNullOrEmpty(e.bestPhotoId))
                {
                    var photos = global::Sealebrities.Gallery.Photos;
                    for (int i = photos.Count - 1; i >= 0; i--) if (photos[i] != null && photos[i].file == e.bestPhotoId) { pe = photos[i]; break; }
                }
                if (pe == null) pe = global::Sealebrities.Gallery.LatestFor(s.id);
                if (pe != null)
                {
                    if (photoFile != pe.file)
                    {
                        FreePhoto();
                        photo = global::Sealebrities.Gallery.Load(pe);
                        photoFile = pe.file;
                    }
                    if (photo != null)
                    {
                        body.Add(PhoneKit.Section(Loc.Tr("Dein bestes Foto", "Your best photo"), UIGlyphs.Image));
                        var img = UIKit.Box();
                        img.style.height = 280f;
                        UIKit.Radius(img, 22f);
                        UIKit.ImageCover(img, photo);
                        body.Add(img);
                    }
                }
            }

            // profile
            body.Add(PhoneKit.Section(Loc.Tr("Steckbrief", "Profile"), UIGlyphs.Book));
            var prof = PhoneKit.Card(14f);
            prof.Add(UIWidgets.InfoLine(Loc.Tr("Größe", "Size"), Loc.Length(s.minCm) + " – " + Loc.Length(s.maxCm), null, PhoneKit.Small));
            prof.Add(UIWidgets.InfoLine(Loc.Tr("Basiswert", "Base value"), Loc.Number(s.baseValue), null, PhoneKit.Small));
            if (disc)
            {
                prof.Add(UIWidgets.InfoLine(Loc.Tr("Bester Clip", "Best clip"), Mathf.RoundToInt(e.bestClipValue).ToString(Loc.Culture), UIPalette.SunInk, PhoneKit.Small));
                prof.Add(UIWidgets.InfoLine(Loc.Tr("Entdeckt", "Discovered"), Loc.Tr("Tag ", "Day ") + e.firstDay, null, PhoneKit.Small));
            }
            body.Add(prof);

            // size tiers
            body.Add(PhoneKit.Section(Loc.Tr("Größenstufen", "Size tiers"), UIGlyphs.Chart));
            for (int i = 0; i < Progression.TierMultiplier.Length; i++) body.Add(TierRow(s, i, disc && i == (int)recTier));

            // fact
            body.Add(PhoneKit.Section(Loc.Tr("Wusstest du?", "Did you know?"), UIGlyphs.Question));
            if (disc) body.Add(UIWidgets.Notice(s.Fact, UIPalette.Turquoise, null, PhoneKit.Small));
            else body.Add(UIWidgets.Notice(Loc.Tr("Fotografiere es fürs Sealog – dann erfährst du mehr.", "Photograph it for the Sealog to learn more."),
                                           UIPalette.Silhouette, UIGlyphs.Camera, PhoneKit.Small));
        }

        static VisualElement DayIcon(UIGlyph.DrawFn icon, Color c, bool on, string label)
        {
            var col = UIKit.Col();
            col.style.alignItems = Align.Center;
            col.style.marginRight = 14f;
            col.style.opacity = on ? 1f : 0.3f;
            col.Add(new UIGlyph(icon, c, 34f));
            col.Add(UIKit.Text(label, 16f, on ? UIPalette.Ink : UIPalette.InkSoft, true));
            return col;
        }

        static void AddPill(VisualElement row, string text, Color c)
        {
            var p = UIKit.Pill(text, UIPalette.A(c, 0.22f), UIWidgets.Ink(c), PhoneKit.Tiny);
            UIKit.Pad(p, 6f, 14f, 6f, 14f);
            p.style.marginRight = 8f; p.style.marginBottom = 6f;
            row.Add(p);
        }

        VisualElement TierRow(SpeciesDef s, int tier, bool isRecord)
        {
            var t = (SizeTier)tier;
            Color c = Progression.TierColor(t);
            var row = UIKit.Row();
            UIKit.Pad(row, 12f, 14f, 12f, 14f);
            UIKit.Radius(row, 16f);
            row.style.marginBottom = 6f;
            row.style.backgroundColor = isRecord ? UIPalette.A(c, 0.22f) : Color.white;
            if (isRecord) UIKit.Border(row, 2f, UIWidgets.Ink(c));

            var dot = UIKit.Dot(t == SizeTier.Normal ? UIPalette.Silhouette : c, 18f);
            row.Add(dot);
            var n = UIKit.Text(Loc.TierName(t), PhoneKit.Small, UIPalette.Ink, true);
            n.style.marginLeft = 10f;
            n.style.width = 140f;
            row.Add(n);

            float a = TierStartCm(s, tier), b = tier + 1 < Progression.TierMultiplier.Length ? TierStartCm(s, tier + 1) : -1f;
            string range = a < 0f ? "—" : b > 0f ? Loc.Length(a) + "–" + Loc.Length(b) : "≥ " + Loc.Length(a);
            var r = UIKit.Text(range, PhoneKit.Tiny, UIPalette.InkSoft);
            r.style.flexGrow = 1;
            row.Add(r);
            if (isRecord)
            {
                var star = new UIGlyph(UIGlyphs.Star, UIWidgets.Ink(c), 24f);
                star.style.marginRight = 6f;
                row.Add(star);
            }
            var m = UIKit.Text(UIKit.Mult(Progression.TierMultiplier[tier]), PhoneKit.Body, UIPalette.Ink, true);
            m.style.width = 80f;
            m.style.unityTextAlign = TextAnchor.MiddleRight;
            row.Add(m);
            return row;
        }

        /// Horizontal band of the 5 size tiers with a marker at the record size.
        static VisualElement SizeScale(SpeciesDef s, float recordCm)
        {
            var wrap = UIKit.Col();
            wrap.style.marginTop = 16f;
            float start = s.minCm;
            float legendStart = TierStartCm(s, 4);
            float end = Mathf.Max(s.maxCm * 1.1f, legendStart > 0f ? legendStart * 1.06f : s.maxCm, recordCm * 1.02f);
            float span = Mathf.Max(0.01f, end - start);

            var band = UIKit.Row();
            band.style.height = 22f;
            band.style.alignItems = Align.Stretch;
            UIKit.Radius(band, 11f);
            band.style.overflow = Overflow.Hidden;
            float prev = start;
            for (int i = 0; i < 5; i++)
            {
                float nextStart = i < 4 ? TierStartCm(s, i + 1) : end;
                if (nextStart < 0f) nextStart = end;
                float w = Mathf.Max(0f, nextStart - prev) / span;
                var seg = UIKit.Box();
                seg.style.width = Length.Percent(w * 100f);
                var tc = Progression.TierColor((SizeTier)i);
                seg.style.backgroundColor = i == 1 ? UIPalette.CardAlt : UIPalette.A(tc, 0.9f);
                if (i > 0) { seg.style.borderLeftWidth = 2f; seg.style.borderLeftColor = Color.white; }
                band.Add(seg);
                prev = nextStart;
            }
            var bandWrap = UIKit.Box();
            bandWrap.Add(band);
            if (recordCm > 0f)
            {
                float f = Mathf.Clamp01((recordCm - start) / span);
                var marker = UIKit.Box();
                marker.style.position = Position.Absolute;
                marker.style.left = Length.Percent(f * 100f);
                marker.style.top = -8f;
                marker.style.marginLeft = -6f;
                UIKit.Size(marker, 12f, 38f);
                UIKit.Radius(marker, 6f);
                marker.style.backgroundColor = Color.white;
                UIKit.Border(marker, 3f, UIPalette.Ink);
                bandWrap.Add(marker);
            }
            wrap.Add(bandWrap);
            var labels = UIKit.Row();
            labels.style.marginTop = 8f;
            labels.Add(UIKit.Text(Loc.Length(start), PhoneKit.Tiny, UIPalette.InkSoft));
            labels.Add(UIKit.Flex());
            labels.Add(UIKit.Text(Loc.Length(end), PhoneKit.Tiny, UIPalette.InkSoft));
            wrap.Add(labels);
            return wrap;
        }
    }

    /// Small vertical 0–100 m depth bar (L.2): the species band in teal, your own seen range in yellow, labels every 25 m.
    public class UIDepthBar : VisualElement
    {
        readonly float bandMin, bandMax, seenMin, seenMax;
        readonly Color band, seen;

        public UIDepthBar(float bandMin, float bandMax, float seenMin, float seenMax, Color band, Color seen)
        {
            this.bandMin = Clamp(bandMin); this.bandMax = Clamp(Mathf.Max(bandMin, bandMax));
            this.seenMin = seenMin; this.seenMax = seenMax;
            this.band = band; this.seen = seen;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            for (int d = 0; d <= 100; d += 25)
            {
                var l = UIKit.Text(d.ToString(), 14f, UIPalette.InkSoft, true);
                l.style.position = Position.Absolute;
                l.style.left = 0f;
                l.style.top = Length.Percent(d);
                l.style.marginTop = -9f;
                Add(l);
            }
        }

        static float Clamp(float v) => float.IsNaN(v) ? 0f : Mathf.Clamp(v, 0f, 100f);

        void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            if (r.height < 10f) return;
            var p = ctx.painter2D;
            float x0 = r.x + 34f, w = Mathf.Max(10f, r.width - 40f);
            float Y(float d) => r.y + r.height * Mathf.Clamp01(d / 100f);
            // water column
            p.fillColor = UIPalette.A(UIPalette.Rgb(0x2E5F93), 0.18f);
            p.BeginPath();
            p.MoveTo(new Vector2(x0, r.y)); p.LineTo(new Vector2(x0 + w, r.y)); p.LineTo(new Vector2(x0 + w, r.yMax)); p.LineTo(new Vector2(x0, r.yMax));
            p.ClosePath(); p.Fill();
            // band
            p.fillColor = band;
            float b0 = Y(bandMin), b1 = Mathf.Max(b0 + 4f, Y(bandMax));
            p.BeginPath();
            p.MoveTo(new Vector2(x0, b0)); p.LineTo(new Vector2(x0 + w, b0)); p.LineTo(new Vector2(x0 + w, b1)); p.LineTo(new Vector2(x0, b1));
            p.ClosePath(); p.Fill();
            // own range
            if (seenMin >= 0f)
            {
                p.fillColor = seen;
                float s0 = Y(Clamp(seenMin)), s1 = Mathf.Max(s0 + 4f, Y(Clamp(Mathf.Max(seenMin, seenMax))));
                float sx = x0 + w * 0.3f, sw = w * 0.4f;
                p.BeginPath();
                p.MoveTo(new Vector2(sx, s0)); p.LineTo(new Vector2(sx + sw, s0)); p.LineTo(new Vector2(sx + sw, s1)); p.LineTo(new Vector2(sx, s1));
                p.ClosePath(); p.Fill();
            }
        }
    }
}
