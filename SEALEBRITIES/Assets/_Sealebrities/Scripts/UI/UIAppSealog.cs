using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Sealog journal: one page per dive site with completion and rewards, a grid of species (silhouettes until filmed)
    /// and a detail page with the biggest specimen, the size-tier table and facts.
    public class UIAppSealog : UIPhoneApp
    {
        int siteIndex;
        SpeciesDef detail;

        public override string Id => "sealog";
        public override string Title => "Sealog";
        public override Color Color => UIPalette.Lagoon;
        public override UIGlyph.DrawFn Icon => UIGlyphs.Book;

        public override string HeaderTitle
        {
            get
            {
                if (detail == null) return Loc.Tr("Sealog · Tagebuch", "Sealog · Journal");
                return IsDiscovered(detail.id) ? detail.Name : "???";
            }
        }

        public override void OnOpen()
        {
            detail = null;
            var p = Refs.Player;
            if (p != null && p.Root != null)
            {
                var site = WorldInfo.SiteAt(p.Root.position);
                int i = site != null ? DiveSites.All.IndexOf(site) : -1;
                if (i >= 0) siteIndex = i;
            }
        }

        public override bool Back()
        {
            if (detail != null) { detail = null; return true; }
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
            for (int i = 0; i < list.Count; i++) if (list[i].speciesId == id) return list[i];
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

        // ------------------------------------------------------------------ index
        void BuildIndex(VisualElement body)
        {
            var sites = DiveSites.All;
            siteIndex = Mathf.Clamp(siteIndex, 0, sites.Count - 1);
            int total = SpeciesDatabase.All.Count, found = Journal.DiscoveredCount;

            var head = UIKit.Card(new Color(1f, 1f, 1f, 0.06f), 16f, 14f);
            var hr = UIKit.Row();
            hr.Add(UIKit.Text(Loc.Tr("Arten entdeckt", "Species discovered"), 15f, UIPalette.TextDim, true));
            hr.Add(UIKit.Flex());
            hr.Add(UIKit.Text(found + " / " + total, 18f, UIPalette.Text, true));
            head.Add(hr);
            var hb = new UIBar(6f, UIPalette.Lagoon);
            hb.style.marginTop = 8f;
            hb.SetValue(total > 0 ? found / (float)total : 0f);
            head.Add(hb);
            body.Add(head);

            // site tabs
            var tabs = UIKit.Row();
            tabs.style.flexWrap = Wrap.Wrap;
            tabs.style.marginTop = 12f;
            for (int i = 0; i < sites.Count; i++) tabs.Add(SiteTab(sites[i], i));
            body.Add(tabs);

            var site = sites[siteIndex];
            body.Add(SiteCard(site));

            var list = SpeciesDatabase.BySite(site.id);
            var grid = UIKit.Row();
            grid.style.flexWrap = Wrap.Wrap;
            grid.style.alignItems = Align.FlexStart;
            grid.style.justifyContent = Justify.FlexStart;
            grid.style.marginTop = 12f;
            for (int i = 0; i < list.Count; i++) grid.Add(SpeciesCard(list[i], i % 3 != 2));
            body.Add(grid);
        }

        VisualElement SiteTab(DiveSite site, int index)
        {
            bool sel = index == siteIndex;
            bool unlocked = Progression.IsSiteUnlocked(site);
            var tab = UIKit.Row();
            UIKit.Radius(tab, 999f);
            UIKit.Pad(tab, 6f, 12f, 6f, 10f);
            tab.style.marginRight = 6f; tab.style.marginBottom = 6f;
            UIKit.Border(tab, 1f, sel ? site.mapColor : new Color(1f, 1f, 1f, 0.1f));
            Color bg = sel ? UIPalette.A(site.mapColor, 0.35f) : new Color(1f, 1f, 1f, 0.04f);
            UIKit.Clickable(tab, () => { siteIndex = index; Phone.Refresh(false); }, bg, UIPalette.A(site.mapColor, 0.25f));
            if (!unlocked)
            {
                var g = new UIGlyph(UIGlyphs.Lock, UIPalette.TextFaint, 13f);
                g.style.marginRight = 5f;
                tab.Add(g);
            }
            else tab.Add(UIKit.Dot(site.mapColor, 8f));
            var name = UIKit.Text(ShortName(site), 12.5f, unlocked ? UIPalette.Text : UIPalette.TextFaint, true);
            name.style.marginLeft = unlocked ? 6f : 0f;
            tab.Add(name);
            var pc = UIKit.Text(Mathf.RoundToInt(Journal.SiteCompletion(site.id) * 100f) + "%", 11f, UIPalette.TextDim);
            pc.style.marginLeft = 6f;
            tab.Add(pc);
            UIKit.IgnorePicking(tab);
            tab.pickingMode = PickingMode.Position;
            return tab;
        }

        static string ShortName(DiveSite s)
        {
            string n = s.Name;
            int q = n.IndexOf(" „", System.StringComparison.Ordinal);
            if (q < 0) q = n.IndexOf(" “", System.StringComparison.Ordinal);
            return q > 0 ? n.Substring(0, q) : n;
        }

        VisualElement SiteCard(DiveSite site)
        {
            var card = UIKit.Card(UIPalette.A(UIPalette.Raised, 0.9f), 18f, 14f);
            card.style.marginTop = 6f;
            card.style.borderLeftWidth = 4f;
            card.style.borderLeftColor = site.mapColor;

            var r = UIKit.Row();
            r.Add(UIKit.Text(site.Name, 18f, UIPalette.Text, true));
            r.Add(UIKit.Flex());
            r.Add(UIKit.Text(UIKit.DepthRange(site.minDepth, site.maxDepth), 13f, UIPalette.TextDim, true));
            card.Add(r);
            var d = UIKit.Para(site.Description, 13f, UIPalette.TextDim);
            d.style.marginTop = 4f;
            card.Add(d);

            if (!Progression.SiteRequirementsMet(site, out string reason) && !Progression.IsSiteUnlocked(site))
            {
                var n = UIWidgets.Notice(Loc.Tr("Gesperrt: ", "Locked: ") + reason, UIPalette.Sun, UIGlyphs.Lock);
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
            pr.Add(UIKit.Text(Loc.Tr($"{found} / {list.Count} Arten", $"{found} / {list.Count} species"), 13f, UIPalette.Text, true));
            pr.Add(UIKit.Flex());
            pr.Add(UIKit.Text(Loc.Percent(comp), 13f, UIPalette.Text, true));
            card.Add(pr);
            var bar = new UIBar(8f, comp >= 1f ? UIPalette.Money : site.mapColor);
            bar.style.marginTop = 6f;
            bar.SetValue(comp);
            DiveSite next = null;
            foreach (var s in DiveSites.All) if (s.prevSiteId == site.id) { next = s; break; }
            if (next != null) bar.AddMarker(next.prevCompletionNeeded, UIPalette.A(Color.white, 0.8f));
            card.Add(bar);

            var gm = GameManager.I;
            if (next != null)
            {
                bool done = Progression.IsSiteUnlocked(next);
                string txt = Loc.Tr($"{Loc.Percent(next.prevCompletionNeeded)} → Neuer Tauchplatz: {next.Name}", $"{Loc.Percent(next.prevCompletionNeeded)} → New dive site: {next.Name}");
                if (next.followersNeeded > 0) txt += Loc.Tr($" (ab {Loc.Number(next.followersNeeded)} Followern)", $" (from {Loc.Number(next.followersNeeded)} followers)");
                card.Add(RewardLine(txt, done || comp >= next.prevCompletionNeeded, done));
            }
            bool sponsor = gm.State.sponsorSites.Contains(site.id);
            card.Add(RewardLine(Loc.Tr("100 % → Sponsor-Deal", "100 % → sponsor deal") +
                                (sponsor && gm.State.followers < Progression.SponsorFollowers ? Loc.Tr($" (aktiv ab {Loc.Number(Progression.SponsorFollowers)} Followern)", $" (active from {Loc.Number(Progression.SponsorFollowers)} followers)") : ""),
                                sponsor, sponsor));
            return card;
        }

        static VisualElement RewardLine(string text, bool reached, bool done)
        {
            var row = UIKit.Row();
            row.style.marginTop = 8f;
            var box = UIKit.Box();
            UIKit.Size(box, 18f, 18f);
            UIKit.Radius(box, 9f);
            UIKit.Border(box, 1.5f, reached ? UIPalette.Money : UIPalette.TextFaint);
            box.style.backgroundColor = done ? UIPalette.Money : Color.clear;
            box.style.alignItems = Align.Center;
            box.style.justifyContent = Justify.Center;
            if (done) box.Add(new UIGlyph(UIGlyphs.Check, UIPalette.Deep, 14f));
            row.Add(box);
            var l = UIKit.Para(text, 12.5f, reached ? UIPalette.Text : UIPalette.TextDim);
            l.style.marginLeft = 8f;
            l.style.flexShrink = 1;
            row.Add(l);
            return row;
        }

        VisualElement SpeciesCard(SpeciesDef s, bool gapRight)
        {
            var e = Entry(s.id);
            bool disc = e != null && e.discovered;
            var card = UIKit.Col();
            card.style.width = Length.Percent(31.5f);
            if (gapRight) card.style.marginRight = Length.Percent(2.75f);
            card.style.marginBottom = 8f;
            card.style.alignItems = Align.Center;
            UIKit.Radius(card, 14f);
            UIKit.Pad(card, 8f, 6f, 10f, 6f);
            card.style.minHeight = 136f;
            UIKit.Clickable(card, () => ShowSpecies(s), disc ? UIPalette.Raised : UIPalette.A(UIPalette.Raised, 0.55f), UIPalette.Raised2);
            if (disc) UIKit.Border(card, 1f, UIPalette.A(Progression.TierColor((SizeTier)e.recordTier), 0.35f));

            var rarity = UIKit.Dot(UIPalette.RarityColor(s.rarity), 7f);
            rarity.style.position = Position.Absolute;
            rarity.style.right = 8f; rarity.style.top = 8f;
            card.Add(rarity);

            card.Add(UIWidgets.SpeciesIcon(s, disc, 104f, 66f));
            var name = UIKit.Text(disc ? s.Name : "???", 12.5f, disc ? UIPalette.Text : UIPalette.TextFaint, true);
            name.style.marginTop = 6f;
            name.style.whiteSpace = WhiteSpace.Normal;
            name.style.unityTextAlign = TextAnchor.MiddleCenter;
            card.Add(name);
            if (disc)
            {
                var tier = (SizeTier)e.recordTier;
                var pill = UIKit.Pill(Loc.Length(e.recordCm) + " · " + Loc.TierName(tier), Progression.TierColor(tier), UIPalette.Deep, 10f);
                pill.style.alignSelf = Align.Center;
                pill.style.marginTop = 5f;
                card.Add(pill);
            }
            else
            {
                var hint = UIKit.Text(Loc.Tr("nicht gefilmt", "not filmed"), 10.5f, UIPalette.TextFaint);
                hint.style.marginTop = 5f;
                card.Add(hint);
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
            hero.style.height = 180f;
            hero.style.alignItems = Align.Center;
            hero.style.justifyContent = Justify.Center;
            UIKit.Radius(hero, 18f);
            UIKit.Image(hero, UIKit.VGradient(UIPalette.Rgb(0x0A2236), UIPalette.Rgb(0x1B5878)), false);
            if (disc) { hero.style.borderBottomWidth = 3f; hero.style.borderBottomColor = tierColor; }
            hero.Add(UIWidgets.SpeciesIcon(s, disc, 300f, 160f));
            body.Add(hero);

            var name = UIKit.Text(disc ? s.Name : "???", 26f, UIPalette.Text, true);
            name.style.marginTop = 12f;
            name.style.whiteSpace = WhiteSpace.Normal;
            body.Add(name);
            if (disc)
            {
                var latin = UIKit.Text(s.latin, 14f, UIPalette.TextDim);
                latin.style.unityFontStyleAndWeight = FontStyle.Italic;
                body.Add(latin);
            }

            var pills = UIKit.Row();
            pills.style.flexWrap = Wrap.Wrap;
            pills.style.marginTop = 8f;
            AddPill(pills, Loc.RarityName(s.rarity), UIPalette.RarityColor(s.rarity));
            if (site != null) AddPill(pills, site.Name, site.mapColor);
            if (s.IsDangerous) AddPill(pills, Loc.Tr("Gefährlich", "Dangerous"), UIPalette.Coral);
            if (s.glows) AddPill(pills, Loc.Tr("Leuchtet", "Glows"), UIPalette.Violet);
            body.Add(pills);

            // biggest specimen
            var rec = UIKit.Card(UIPalette.A(UIPalette.Raised, 0.95f), 16f, 14f);
            rec.style.marginTop = 12f;
            rec.Add(UIKit.Caption(Loc.Tr("Größtes Exemplar", "Biggest specimen"), UIPalette.TextFaint, 11f));
            if (disc)
            {
                var big = UIKit.Text(Loc.Tr("Größtes Exemplar: ", "Biggest specimen: ") + Loc.Length(e.recordCm) + " (" + Loc.TierName(recTier) + ")", 18f, tierColor, true);
                big.style.marginTop = 4f;
                big.style.whiteSpace = WhiteSpace.Normal;
                rec.Add(big);
            }
            else
            {
                var none = UIKit.Text(Loc.Tr("Noch nicht gefilmt", "Not filmed yet"), 18f, UIPalette.TextDim, true);
                none.style.marginTop = 4f;
                rec.Add(none);
            }
            rec.Add(SizeScale(s, disc ? e.recordCm : -1f));
            body.Add(rec);

            // info
            body.Add(UIWidgets.SectionTitle(Loc.Tr("Steckbrief", "Profile")));
            body.Add(UIWidgets.InfoLine(Loc.Tr("Tauchplatz", "Dive site"), site != null ? site.Name : "—"));
            body.Add(UIWidgets.InfoLine(Loc.Tr("Tiefe", "Depth"), UIKit.DepthRange(s.minDepth, s.maxDepth)));
            body.Add(UIWidgets.InfoLine(Loc.Tr("Größe", "Size"), Loc.Length(s.minCm) + " – " + Loc.Length(s.maxCm)));
            body.Add(UIWidgets.InfoLine(Loc.Tr("Seltenheit", "Rarity"), Loc.RarityName(s.rarity), UIPalette.RarityColor(s.rarity)));
            body.Add(UIWidgets.InfoLine(Loc.Tr("Basiswert", "Base value"), Loc.Number(s.baseValue)));
            if (disc)
            {
                body.Add(UIWidgets.InfoLine(Loc.Tr("Gefilmt", "Filmed"), e.timesFilmed + "×"));
                body.Add(UIWidgets.InfoLine(Loc.Tr("Bester Clip", "Best clip"), Loc.Tr("Wert ", "Value ") + Mathf.RoundToInt(e.bestClipValue).ToString(Loc.Culture), UIPalette.Sun));
                body.Add(UIWidgets.InfoLine(Loc.Tr("Entdeckt", "Discovered"), Loc.Tr("Tag ", "Day ") + e.firstDay));
            }

            // size tiers
            body.Add(UIWidgets.SectionTitle(Loc.Tr("Größenstufen · Wert-Multiplikator", "Size tiers · value multiplier")));
            var summary = UIKit.Text(TierSummary(), 12.5f, UIPalette.TextDim);
            summary.style.whiteSpace = WhiteSpace.Normal;
            summary.style.marginBottom = 8f;
            body.Add(summary);
            for (int i = 0; i < Progression.TierMultiplier.Length; i++) body.Add(TierRow(s, i, disc && i == (int)recTier, disc));

            // fact
            body.Add(UIWidgets.SectionTitle(Loc.Tr("Wusstest du?", "Did you know?")));
            if (disc) body.Add(UIWidgets.Notice(s.Fact, UIPalette.Lagoon));
            else body.Add(UIWidgets.Notice(Loc.Tr($"Noch nicht gefilmt. Halte zwischen {s.minDepth:0} und {s.maxDepth:0} m Tiefe Ausschau – {(site != null ? site.Name : "")}.",
                                                   $"Not filmed yet. Look out between {s.minDepth:0} and {s.maxDepth:0} m depth – {(site != null ? site.Name : "")}."), UIPalette.TextDim));
        }

        static string TierSummary()
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < Progression.TierMultiplier.Length; i++)
            {
                if (i > 0) sb.Append(" · ");
                sb.Append(Loc.TierName((SizeTier)i)).Append(' ').Append(UIKit.Mult(Progression.TierMultiplier[i]));
            }
            return sb.ToString();
        }

        static void AddPill(VisualElement row, string text, Color c)
        {
            var p = UIKit.Pill(text, UIPalette.A(c, 0.2f), c, 11f);
            p.style.marginRight = 6f; p.style.marginBottom = 4f;
            row.Add(p);
        }

        VisualElement TierRow(SpeciesDef s, int tier, bool isRecord, bool disc)
        {
            var t = (SizeTier)tier;
            Color c = Progression.TierColor(t);
            var row = UIKit.Row();
            UIKit.Pad(row, 8f, 10f, 8f, 10f);
            UIKit.Radius(row, 10f);
            row.style.marginBottom = 4f;
            row.style.backgroundColor = isRecord ? UIPalette.A(c, 0.16f) : new Color(1f, 1f, 1f, 0.04f);
            if (isRecord) UIKit.Border(row, 1f, UIPalette.A(c, 0.7f));

            var badge = UIWidgets.TierBadge(t, 11f);
            badge.style.width = 84f;
            row.Add(badge);

            float a = TierStartCm(s, tier), b = tier + 1 < Progression.TierMultiplier.Length ? TierStartCm(s, tier + 1) : -1f;
            string range = a < 0f ? "—" : b > 0f ? Loc.Length(a) + " – " + Loc.Length(b) : Loc.Tr("ab ", "from ") + Loc.Length(a);
            var r = UIKit.Text(range, 12.5f, UIPalette.Text);
            r.style.marginLeft = 10f;
            r.style.flexGrow = 1;
            row.Add(r);
            if (isRecord)
            {
                var star = new UIGlyph(UIGlyphs.Star, c, 14f);
                star.style.marginRight = 6f;
                row.Add(star);
            }
            var m = UIKit.Text(UIKit.Mult(Progression.TierMultiplier[tier]), 15f, c, true);
            m.style.width = 50f;
            m.style.unityTextAlign = TextAnchor.MiddleRight;
            row.Add(m);
            return row;
        }

        /// Horizontal band of the 5 size tiers with a marker at the record size.
        static VisualElement SizeScale(SpeciesDef s, float recordCm)
        {
            var wrap = UIKit.Col();
            wrap.style.marginTop = 12f;
            float start = s.minCm;
            float legendStart = TierStartCm(s, 4);
            float end = Mathf.Max(s.maxCm * 1.1f, legendStart > 0f ? legendStart * 1.06f : s.maxCm, recordCm * 1.02f);
            float span = Mathf.Max(0.01f, end - start);

            var band = UIKit.Row();
            band.style.height = 12f;
            band.style.alignItems = Align.Stretch;
            UIKit.Radius(band, 6f);
            band.style.overflow = Overflow.Hidden;
            float prev = start;
            for (int i = 0; i < 5; i++)
            {
                float nextStart = i < 4 ? TierStartCm(s, i + 1) : end;
                if (nextStart < 0f) nextStart = end;
                float w = Mathf.Max(0f, nextStart - prev) / span;
                var seg = UIKit.Box();
                seg.style.width = Length.Percent(w * 100f);
                seg.style.backgroundColor = UIPalette.A(Progression.TierColor((SizeTier)i), 0.75f);
                if (i > 0) { seg.style.borderLeftWidth = 1f; seg.style.borderLeftColor = UIPalette.A(UIPalette.Deep, 0.8f); }
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
                marker.style.top = -4f;
                marker.style.marginLeft = -3f;
                UIKit.Size(marker, 6f, 20f);
                UIKit.Radius(marker, 3f);
                marker.style.backgroundColor = Color.white;
                UIKit.Border(marker, 1.5f, UIPalette.Deep);
                bandWrap.Add(marker);
            }
            wrap.Add(bandWrap);
            var labels = UIKit.Row();
            labels.style.marginTop = 6f;
            labels.Add(UIKit.Text(Loc.Length(start), 11f, UIPalette.TextFaint));
            labels.Add(UIKit.Flex());
            labels.Add(UIKit.Text(Loc.Length(end), 11f, UIPalette.TextFaint));
            wrap.Add(labels);
            return wrap;
        }
    }
}
