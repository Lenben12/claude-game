using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Sealog journal: one page per dive site (completion, unlock marker), a grid of species (flat grey silhouettes
    /// until filmed) and a detail page with the record bar, the best own photo, the size tiers and a fact.
    public class UIAppSealog : UIPhoneApp
    {
        int siteIndex;
        SpeciesDef detail;
        Texture2D photo;
        string photoFile;

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
                int i = site != null ? DiveSites.All.IndexOf(site) : -1;
                if (i >= 0) siteIndex = i;
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
                var site = DiveSites.Get(s.siteId);
                int i = site != null ? DiveSites.All.IndexOf(site) : -1;
                if (i >= 0) siteIndex = i;
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
        static JournalEntry Entry(string id)
        {
            var list = GameManager.I.State.journal;
            for (int i = 0; i < list.Count; i++) if (list[i] != null && list[i].speciesId == id) return list[i];
            return null;
        }

        static bool IsDiscovered(string id)
        {
            var e = GameManager.I != null ? Entry(id) : null;
            return e != null && e.discovered;
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

        static string ShortName(DiveSite s)
        {
            string n = s.Name;
            int q = n.IndexOf(" „", System.StringComparison.Ordinal);
            if (q < 0) q = n.IndexOf(" “", System.StringComparison.Ordinal);
            return q > 0 ? n.Substring(0, q) : n;
        }

        // ------------------------------------------------------------------ index
        void BuildIndex(VisualElement body)
        {
            var sites = DiveSites.All;
            siteIndex = Mathf.Clamp(siteIndex, 0, sites.Count - 1);
            int total = SpeciesDatabase.All.Count, found = Journal.DiscoveredCount;

            var head = PhoneKit.Card();
            var hr = UIKit.Row();
            hr.Add(new UIGlyph(UIGlyphs.Book, UIPalette.Turquoise, 40f));
            var cnt = UIKit.Text(found + " / " + total, PhoneKit.Title, UIPalette.Ink, true);
            cnt.style.marginLeft = 12f;
            hr.Add(cnt);
            hr.Add(UIKit.Flex());
            hr.Add(UIKit.Text(Loc.Percent(total > 0 ? found / (float)total : 0f), PhoneKit.Body, UIPalette.TurquoiseInk, true));
            head.Add(hr);
            var hb = PhoneKit.Bar(total > 0 ? found / (float)total : 0f, UIPalette.Turquoise, 14f);
            hb.style.marginTop = 12f;
            head.Add(hb);
            body.Add(head);

            // site tabs
            var tabs = UIKit.Row();
            tabs.style.flexWrap = Wrap.Wrap;
            tabs.style.marginTop = 4f;
            tabs.style.marginBottom = 4f;
            for (int i = 0; i < sites.Count; i++) tabs.Add(SiteTab(sites[i], i));
            body.Add(tabs);

            var site = sites[siteIndex];
            body.Add(SiteCard(site));

            var list = SpeciesDatabase.BySite(site.id);
            var grid = UIKit.Row();
            grid.style.flexWrap = Wrap.Wrap;
            grid.style.alignItems = Align.FlexStart;
            grid.style.justifyContent = Justify.SpaceBetween;
            grid.style.marginTop = 6f;
            for (int i = 0; i < list.Count; i++) grid.Add(SpeciesCard(list[i]));
            body.Add(grid);
        }

        VisualElement SiteTab(DiveSite site, int index)
        {
            bool sel = index == siteIndex;
            bool unlocked = Progression.IsSiteUnlocked(site);
            var tab = UIKit.Row();
            UIKit.Radius(tab, 999f);
            UIKit.Pad(tab, 10f, 16f, 10f, 14f);
            tab.style.marginRight = 8f; tab.style.marginBottom = 8f;
            UIKit.Border(tab, 2f, sel ? site.mapColor : UIPalette.A(UIPalette.Ink, 0.08f));
            Color bg = sel ? UIPalette.A(site.mapColor, 0.22f) : Color.white;
            UIKit.Clickable(tab, () => { siteIndex = index; Phone.Refresh(false); }, bg, UIPalette.A(site.mapColor, 0.16f));
            if (!unlocked)
            {
                var g = new UIGlyph(UIGlyphs.Lock, UIPalette.Silhouette, 22f);
                g.style.marginRight = 6f;
                tab.Add(g);
            }
            else tab.Add(UIKit.Dot(site.mapColor, 14f));
            var name = UIKit.Text(ShortName(site), PhoneKit.Small, unlocked ? UIPalette.Ink : UIPalette.Silhouette, true);
            name.style.marginLeft = unlocked ? 8f : 0f;
            tab.Add(name);
            UIKit.IgnorePicking(tab);
            tab.pickingMode = PickingMode.Position;
            return tab;
        }

        VisualElement SiteCard(DiveSite site)
        {
            var card = PhoneKit.Card();
            card.style.borderLeftWidth = 8f;
            card.style.borderLeftColor = site.mapColor;

            var r = UIKit.Row();
            var name = UIKit.Text(site.Name, PhoneKit.Big, UIPalette.Ink, true);
            name.style.flexShrink = 1;
            name.style.whiteSpace = WhiteSpace.Normal;
            r.Add(name);
            r.Add(UIKit.Flex());
            var depth = UIKit.Row();
            depth.Add(new UIGlyph(UIGlyphs.Depth, UIPalette.InkSoft, 26f));
            depth.Add(UIKit.Text(UIKit.DepthRange(site.minDepth, site.maxDepth), PhoneKit.Small, UIPalette.InkSoft, true));
            r.Add(depth);
            card.Add(r);

            if (!Progression.IsSiteUnlocked(site) && !Progression.SiteRequirementsMet(site, out string reason))
            {
                var n = UIWidgets.Notice(reason, UIPalette.Sun, UIGlyphs.Lock, PhoneKit.Small);
                n.style.marginTop = 10f;
                n.style.marginBottom = 0f;
                card.Add(n);
            }

            var list = SpeciesDatabase.BySite(site.id);
            int found = 0;
            foreach (var s in list) if (IsDiscovered(s.id)) found++;
            float comp = list.Count > 0 ? found / (float)list.Count : 0f;
            var pr = UIKit.Row();
            pr.style.marginTop = 12f;
            pr.Add(new UIGlyph(UIGlyphs.Fish, UIPalette.InkSoft, 28f));
            var c = UIKit.Text(found + " / " + list.Count, PhoneKit.Body, UIPalette.Ink, true);
            c.style.marginLeft = 8f;
            pr.Add(c);
            pr.Add(UIKit.Flex());
            pr.Add(UIKit.Text(Loc.Percent(comp), PhoneKit.Body, UIPalette.Ink, true));
            card.Add(pr);
            var bar = new UIBar(14f, comp >= 1f ? UIPalette.LeafInk : site.mapColor);
            bar.style.marginTop = 8f;
            bar.SetValue(comp);
            DiveSite next = null;
            foreach (var s in DiveSites.All) if (s.prevSiteId == site.id) { next = s; break; }
            if (next != null) bar.AddMarker(next.prevCompletionNeeded, UIPalette.Ink);
            card.Add(bar);

            var gm = GameManager.I;
            if (next != null)
            {
                bool done = Progression.IsSiteUnlocked(next);
                card.Add(RewardLine(UIGlyphs.Pin, Loc.Percent(next.prevCompletionNeeded) + " → " + ShortName(next), done || comp >= next.prevCompletionNeeded, done));
            }
            bool sponsor = gm.State.sponsorSites.Contains(site.id);
            card.Add(RewardLine(UIGlyphs.Coin, "100 % → " + Loc.Tr("Sponsor", "Sponsor"), sponsor, sponsor));
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
            var card = UIKit.Col();
            card.style.width = Length.Percent(48.5f);
            card.style.marginBottom = 14f;
            card.style.alignItems = Align.Center;
            UIKit.Radius(card, 22f);
            UIKit.Pad(card, 14f, 10f, 14f, 10f);
            card.style.minHeight = 230f;
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
            UIKit.IgnorePicking(card);
            card.pickingMode = PickingMode.Position;
            return card;
        }

        // ------------------------------------------------------------------ detail page
        void BuildDetail(VisualElement body, SpeciesDef s)
        {
            var e = Entry(s.id);
            bool disc = e != null && e.discovered;
            var site = DiveSites.Get(s.siteId);
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
            if (site != null) AddPill(pills, ShortName(site), site.mapColor);
            if (s.IsDangerous) AddPill(pills, Loc.Tr("Gefährlich", "Dangerous"), UIPalette.Coral);
            if (s.glows) AddPill(pills, Loc.Tr("Leuchtet", "Glows"), UIPalette.Violet);
            body.Add(pills);

            // record bar
            var rec = PhoneKit.Card();
            rec.style.marginTop = 12f;
            var rr = UIKit.Row();
            rr.Add(new UIGlyph(UIGlyphs.Star, disc ? UIWidgets.Ink(tierColor) : UIPalette.Silhouette, 34f));
            var big = UIKit.Text(disc ? Loc.Length(e.recordCm) + " · " + Loc.TierName(recTier) : Loc.Tr("Noch nicht gefilmt", "Not filmed yet"),
                                 PhoneKit.Big, disc ? UIPalette.Ink : UIPalette.InkSoft, true);
            big.style.marginLeft = 10f;
            big.style.whiteSpace = WhiteSpace.Normal;
            big.style.flexShrink = 1;
            rr.Add(big);
            rec.Add(rr);
            rec.Add(SizeScale(s, disc ? e.recordCm : -1f));
            body.Add(rec);

            // best own photo
            if (disc)
            {
                var pe = global::Sealebrities.Gallery.LatestFor(s.id);
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
            prof.Add(UIWidgets.InfoLine(Loc.Tr("Tiefe", "Depth"), UIKit.DepthRange(s.minDepth, s.maxDepth), null, PhoneKit.Small));
            prof.Add(UIWidgets.InfoLine(Loc.Tr("Größe", "Size"), Loc.Length(s.minCm) + " – " + Loc.Length(s.maxCm), null, PhoneKit.Small));
            prof.Add(UIWidgets.InfoLine(Loc.Tr("Basiswert", "Base value"), Loc.Number(s.baseValue), null, PhoneKit.Small));
            if (disc)
            {
                prof.Add(UIWidgets.InfoLine(Loc.Tr("Gefilmt", "Filmed"), e.timesFilmed + "×", null, PhoneKit.Small));
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
            else body.Add(UIWidgets.Notice(Loc.Tr($"Zwischen {s.minDepth:0} und {s.maxDepth:0} m – {(site != null ? ShortName(site) : "")}.",
                                                   $"Between {s.minDepth:0} and {s.maxDepth:0} m – {(site != null ? ShortName(site) : "")}."), UIPalette.Silhouette, UIGlyphs.Eye, PhoneKit.Small));
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
}
