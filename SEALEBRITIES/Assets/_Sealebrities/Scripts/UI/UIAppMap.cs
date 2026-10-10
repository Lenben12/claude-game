using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Seekarte: top-down map (terrain texture + Painter2D overlay) with dive sites, points of interest,
    /// live player arrow and boat marker, plus a site list (depth vs. gear, distance, unlock requirement).
    public class UIAppMap : UIPhoneApp
    {
        VisualElement map, playerArrow, boatMarker;
        float mapSize = 380f;

        public override string Id => "map";
        public override string Title => Loc.Tr("Seekarte", "Sea Chart");
        public override Color Color => UIPalette.Rgb(0x1E8DAE);
        public override UIGlyph.DrawFn Icon => UIGlyphs.Pin;

        static Vector2 ToMap01(Vector3 world)
        {
            float half = UIMapTexture.WorldHalf;
            return new Vector2((world.x + half) / (2f * half), 1f - (world.z + half) / (2f * half));
        }

        public override void Build(VisualElement body)
        {
            var gm = GameManager.I;
            if (gm == null) return;

            map = UIKit.Box("sb-map");
            map.style.height = mapSize;
            map.style.width = mapSize;
            map.style.alignSelf = Align.Center;
            map.style.flexShrink = 0;
            UIKit.Radius(map, 18f);
            map.style.overflow = Overflow.Hidden;
            map.style.backgroundColor = UIPalette.Rgb(0x0B2E52);
            UIKit.Image(map, UIMapTexture.Get(), false);
            UIKit.Border(map, 1f, new Color(1f, 1f, 1f, 0.12f));
            body.Add(map);

            var overlay = new UIMapOverlay();
            UIKit.FillParent(overlay);
            map.Add(overlay);

            // points of interest
            AddPoi(WorldLayout.Hut, Loc.Tr("Hütte", "Hut"), UIGlyphs.House, UIPalette.Sun);
            AddPoi(WorldLayout.Helipad, "Heli", UIGlyphs.Heli, UIPalette.Text);
            AddPoi(WorldLayout.DockEnd, Loc.Tr("Steg", "Dock"), null, UIPalette.Text);
            AddPoi(WorldLayout.Lighthouse, Loc.Tr("Leuchtturm", "Lighthouse"), null, UIPalette.Coral);
            AddPoi(WorldLayout.HiddenBay, Loc.Tr("Versteckte Bucht", "Hidden bay"), null, UIPalette.Lagoon);

            // site labels
            foreach (var site in DiveSites.All)
            {
                bool unlocked = Progression.IsSiteUnlocked(site);
                var pos = ToMap01(site.center);
                var lbl = UIKit.Text(unlocked ? ShortName(site) : ShortName(site), 11f, unlocked ? Color.white : UIPalette.A(Color.white, 0.55f), true);
                lbl.pickingMode = PickingMode.Ignore;
                lbl.style.position = Position.Absolute;
                lbl.style.left = Length.Percent(pos.x * 100f);
                lbl.style.top = Length.Percent(pos.y * 100f);
                lbl.style.translate = new Translate(Length.Percent(-50f), Length.Percent(-50f));
                lbl.style.backgroundColor = new Color(0f, 0f, 0f, 0.45f);
                UIKit.Radius(lbl, 6f);
                UIKit.Pad(lbl, 2f, 6f, 2f, 6f);
                map.Add(lbl);
            }

            // compass + scale
            var north = UIKit.Text("N", 13f, Color.white, true);
            UIKit.Abs(north, null, 8f, 10f, null);
            north.style.backgroundColor = new Color(0f, 0f, 0f, 0.4f);
            UIKit.Radius(north, 10f);
            UIKit.Size(north, 22f, 22f);
            north.style.unityTextAlign = TextAnchor.MiddleCenter;
            map.Add(north);
            var scale = UIKit.Col();
            UIKit.Abs(scale, 10f, null, null, 8f);
            var sbar = UIKit.Box();
            UIKit.Size(sbar, mapSize * 500f / WorldLayout.TerrainSize, 4f);
            sbar.style.backgroundColor = Color.white;
            UIKit.Radius(sbar, 2f);
            scale.Add(sbar);
            scale.Add(UIKit.Text("500 m", 10f, Color.white, true));
            map.Add(scale);

            boatMarker = new UIGlyph(UIGlyphs.Boat, Color.white, 16f);
            boatMarker.style.position = Position.Absolute;
            boatMarker.style.marginLeft = -8f; boatMarker.style.marginTop = -8f;
            map.Add(boatMarker);
            playerArrow = new UIGlyph(UIGlyphs.Arrow, UIPalette.Coral, 22f);
            playerArrow.style.position = Position.Absolute;
            playerArrow.style.marginLeft = -11f; playerArrow.style.marginTop = -11f;
            map.Add(playerArrow);
            UpdateMarkers();

            // legend row
            var legend = UIKit.Row();
            legend.style.marginTop = 8f;
            legend.style.flexWrap = Wrap.Wrap;
            legend.Add(LegendItem(UIGlyphs.Arrow, UIPalette.Coral, Loc.Tr("Du", "You")));
            legend.Add(LegendItem(UIGlyphs.Boat, Color.white, Loc.Tr("Boot", "Boat")));
            legend.Add(LegendItem(UIGlyphs.House, UIPalette.Sun, Loc.Tr("Hütte", "Hut")));
            body.Add(legend);

            // site list
            body.Add(UIWidgets.SectionTitle(Loc.Tr("Tauchplätze", "Dive sites")));
            float safe = gm.Gear.maxDepth;
            var safeLine = UIKit.Text(Loc.Tr("Deine Ausrüstung: sicher bis ", "Your gear: safe down to ") + Loc.Meters(safe), 13f, UIPalette.Text, true);
            safeLine.style.marginBottom = 8f;
            body.Add(safeLine);
            Vector3 playerPos = Refs.Player != null && Refs.Player.Root != null ? Refs.Player.Root.position : WorldLayout.PlayerSpawn;
            foreach (var site in DiveSites.All) body.Add(SiteRow(site, safe, playerPos));
        }

        static string ShortName(DiveSite s)
        {
            string n = s.Name;
            int q = n.IndexOf(" „", System.StringComparison.Ordinal);
            if (q < 0) q = n.IndexOf(" “", System.StringComparison.Ordinal);
            return q > 0 ? n.Substring(0, q) : n;
        }

        void AddPoi(Vector3 world, string label, UIGlyph.DrawFn icon, Color c)
        {
            var pos = ToMap01(world);
            var e = UIKit.Row();
            e.pickingMode = PickingMode.Ignore;
            e.style.position = Position.Absolute;
            e.style.left = Length.Percent(pos.x * 100f);
            e.style.top = Length.Percent(pos.y * 100f);
            e.style.translate = new Translate(-5f, -5f);
            if (icon != null)
            {
                var g = new UIGlyph(icon, c, 12f);
                e.Add(g);
            }
            else e.Add(UIKit.Dot(c, 7f));
            map.Add(e);
        }

        static VisualElement LegendItem(UIGlyph.DrawFn icon, Color c, string text)
        {
            var row = UIKit.Row();
            row.style.marginRight = 14f;
            var g = new UIGlyph(icon, c, 14f);
            g.style.marginRight = 5f;
            row.Add(g);
            row.Add(UIKit.Text(text, 12f, UIPalette.TextDim));
            return row;
        }

        static VisualElement SiteRow(DiveSite site, float safe, Vector3 playerPos)
        {
            bool unlocked = Progression.IsSiteUnlocked(site);
            var card = UIKit.Card(UIPalette.A(UIPalette.Raised, unlocked ? 0.95f : 0.55f), 14f, 12f);
            card.style.marginBottom = 6f;
            card.style.borderLeftWidth = 4f;
            card.style.borderLeftColor = unlocked ? site.mapColor : UIPalette.A(site.mapColor, 0.4f);

            var head = UIKit.Row();
            var name = UIKit.Text(site.Name, 15f, unlocked ? UIPalette.Text : UIPalette.TextDim, true);
            name.style.flexShrink = 1;
            head.Add(name);
            head.Add(UIKit.Flex());
            float dist = Vector2.Distance(new Vector2(playerPos.x, playerPos.z), new Vector2(site.center.x, site.center.z));
            head.Add(UIKit.Text(UIKit.Distance(dist), 12.5f, UIPalette.TextDim, true));
            card.Add(head);

            var depthRow = UIKit.Row();
            depthRow.style.marginTop = 4f;
            depthRow.Add(UIKit.Text(Loc.Tr("Tiefe ", "Depth ") + UIKit.DepthRange(site.minDepth, site.maxDepth), 12.5f, UIPalette.Text));
            depthRow.Add(UIKit.Flex());
            Color c; string txt;
            if (safe >= site.maxDepth) { c = UIPalette.Money; txt = Loc.Tr("Ausrüstung passt", "Gear OK"); }
            else if (safe >= site.minDepth) { c = UIPalette.Sun; txt = Loc.Tr("nur teilweise", "partly"); }
            else { c = UIPalette.Live; txt = Loc.Tr("zu tief!", "too deep!"); }
            depthRow.Add(UIKit.Pill(txt + " · " + Loc.Tr("du: ", "you: ") + Loc.Meters(safe), UIPalette.A(c, 0.2f), c, 10f));
            card.Add(depthRow);

            if (!unlocked)
            {
                Progression.SiteRequirementsMet(site, out string reason);
                var req = UIKit.Row();
                req.style.marginTop = 6f;
                var g = new UIGlyph(UIGlyphs.Lock, UIPalette.Sun, 13f);
                g.style.marginRight = 6f;
                req.Add(g);
                var r = UIKit.Para(string.IsNullOrEmpty(reason) ? Loc.Tr("Bald verfügbar", "Available soon") : reason, 12f, UIPalette.Sun);
                r.style.flexShrink = 1;
                req.Add(r);
                card.Add(req);
            }
            return card;
        }

        void UpdateMarkers()
        {
            if (map == null || map.panel == null) return;
            float w = mapSize, h = mapSize;
            var player = Refs.Player;
            if (player != null && player.Root != null)
            {
                UIKit.Show(playerArrow, true);
                var p01 = ToMap01(player.Root.position);
                playerArrow.style.left = Mathf.Clamp01(p01.x) * w;
                playerArrow.style.top = Mathf.Clamp01(p01.y) * h;
                Transform yawSource = player.View != null ? player.View.transform : player.Root;
                playerArrow.style.rotate = new Rotate(Angle.Degrees(yawSource.eulerAngles.y));
            }
            else UIKit.Show(playerArrow, false);

            var boat = Refs.Boat;
            if (boat != null && boat.Root != null)
            {
                UIKit.Show(boatMarker, true);
                var b01 = ToMap01(boat.Root.position);
                boatMarker.style.left = Mathf.Clamp01(b01.x) * w;
                boatMarker.style.top = Mathf.Clamp01(b01.y) * h;
                boatMarker.style.rotate = new Rotate(Angle.Degrees(boat.Root.eulerAngles.y));
            }
            else UIKit.Show(boatMarker, false);
        }

        public override void Tick(float dt)
        {
            UpdateMarkers();
        }
    }

    /// Painter2D overlay of the sea chart: grid and dive-site circles (locked sites dimmed and dashed).
    public class UIMapOverlay : VisualElement
    {
        public UIMapOverlay()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            if (r.width < 4f) return;
            var p = ctx.painter2D;
            float half = UIMapTexture.WorldHalf;

            p.strokeColor = new Color(1f, 1f, 1f, 0.06f);
            p.lineWidth = 1f;
            for (int i = 1; i < 8; i++)
            {
                float f = i / 8f;
                p.BeginPath(); p.MoveTo(new Vector2(r.x + r.width * f, r.y)); p.LineTo(new Vector2(r.x + r.width * f, r.yMax)); p.Stroke();
                p.BeginPath(); p.MoveTo(new Vector2(r.x, r.y + r.height * f)); p.LineTo(new Vector2(r.xMax, r.y + r.height * f)); p.Stroke();
            }

            // pass 0: unlocked sites (solid outline), pass 1: locked sites (dimmed, dashed outline)
            for (int pass = 0; pass < 2; pass++)
            {
                if (pass == 1) p.SetDashPattern(5f, 4f);
                foreach (var site in DiveSites.All)
                {
                    bool unlocked = GameManager.I != null && Progression.IsSiteUnlocked(site);
                    if (unlocked != (pass == 0)) continue;
                    var c = new Vector2(r.x + (site.center.x + half) / (2f * half) * r.width, r.y + (1f - (site.center.z + half) / (2f * half)) * r.height);
                    float rad = site.radius / (2f * half) * r.width;
                    p.fillColor = UIPalette.A(site.mapColor, unlocked ? 0.3f : 0.12f);
                    p.BeginPath(); p.Arc(c, rad, Angle.Degrees(0f), Angle.Degrees(360f)); p.ClosePath(); p.Fill();
                    p.strokeColor = unlocked ? UIPalette.Lighten(site.mapColor, 0.25f) : new Color(1f, 1f, 1f, 0.35f);
                    p.lineWidth = unlocked ? 2f : 1.5f;
                    p.BeginPath(); p.Arc(c, rad, Angle.Degrees(0f), Angle.Degrees(360f)); p.ClosePath(); p.Stroke();
                }
            }
            p.SetDashPattern(System.ReadOnlySpan<float>.Empty);
        }
    }
}
