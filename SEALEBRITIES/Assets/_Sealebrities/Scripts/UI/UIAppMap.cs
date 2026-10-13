using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Seekarte (round 3, GDD §20 Q30/Q31): the hand-drawn treasure map (UITreasureMap – drag, wheel / + −, ◎), places
    /// after discovery, shared co-op pins (palette on the map), clue circles, found wrecks, the treasure X; below a short
    /// legend with the M key (the same map as an overlay while driving the boat) and the dive-site list.
    /// The page is not rebuilt on every state change (LiveUpdates = false) – the map updates itself, so zoom, pan and the
    /// selected pin survive; the site list refreshes when the page is opened.
    public class UIAppMap : UIPhoneApp
    {
        public const float MapW = 496f, MapH = 640f;
        UITreasureMap map;

        public override string Id => "map";
        public override string Title => Loc.Tr("Seekarte", "Sea Chart");
        public override Color Color => UIPalette.Rgb(0xB0782E);
        public override UIGlyph.DrawFn Icon => UIGlyphs.Pin;
        public override bool LiveUpdates => false;

        /// The map element of the open page (null when the page is not shown) – tests drive it directly.
        public UITreasureMap Map => map != null && map.panel != null ? map : null;

        public override void Build(VisualElement body)
        {
            var gm = GameManager.I;
            if (gm == null) return;

            map = new UITreasureMap(UITreasureMap.PhoneView, false);
            UIKit.Size(map, MapW, MapH);
            map.style.alignSelf = Align.Center;
            body.Add(map);

            // legend + the overlay key
            var legend = UIKit.Row();
            legend.style.marginTop = 10f;
            legend.style.flexWrap = Wrap.Wrap;
            legend.Add(LegendItem(UIGlyphs.Arrow, UIPalette.Coral, Loc.Tr("Du", "You")));
            legend.Add(LegendItem(UIGlyphs.Boat, UIPalette.Rgb(0x2D5F7A), Loc.Tr("Boot", "Boat")));
            if (Net.IsOnline) legend.Add(LegendItem(UIGlyphs.Crew, UIPalette.Turquoise, "Crew"));
            legend.Add(LegendItem(UIGlyphs.Question, UIPalette.Rgb(0x8E3B2E), Loc.Tr("Hinweis", "Clue")));
            body.Add(legend);
            var keyRow = UIKit.Row();
            keyRow.style.marginTop = 4f;
            keyRow.Add(UIKit.KeyCap(GameInput.BindingLabel(GameAction.Map), PhoneKit.Small));
            var kt = PhoneKit.T(Loc.Tr(" Karte beim Fahren", " map while driving"), PhoneKit.Small, UIPalette.InkSoft, true);
            kt.style.marginLeft = 8f;
            keyRow.Add(kt);
            body.Add(keyRow);

            var actions = UIKit.Row();
            actions.style.marginTop = 10f;
            actions.style.flexWrap = Wrap.Wrap;
            if (UIWaypoint.Active)
            {
                var clear = PhoneKit.IconButton(UIGlyphs.Cross, Loc.Tr("Wegpunkt", "Waypoint"), () => { UIWaypoint.Clear(); Phone.Refresh(true); }, UIBtnKind.Secondary, PhoneKit.Small);
                clear.style.marginRight = 8f;
                actions.Add(clear);
            }
            if (MapPins.Count > 0)
            {
                var clearPins = PhoneKit.IconButton(UIGlyphs.Trash, Loc.Tr("Alle Pins", "All pins") + " (" + MapPins.Count + ")", () => { MapPins.Clear(); Phone.Refresh(true); }, UIBtnKind.Secondary, PhoneKit.Small);
                actions.Add(clearPins);
            }
            if (actions.childCount > 0) body.Add(actions);

            // the secret island item (K.8) once Toni's map is ours
            bool fogMap = StoryFlags.Has(StoryKeys.FogMap);
            if (fogMap)
            {
                var fog = DiveSites.Get(Zones.FogIsle);
                var card = PhoneKit.Card(16f);
                card.style.backgroundColor = UIPalette.Rgb(0xF6ECD6);
                card.style.marginTop = 12f;
                var r = UIKit.Row();
                r.Add(UIWidgets.IconTile(UIGlyphs.Cloud, UIPalette.Rgb(0xF28CBE), 60f));
                var c = UIKit.Col();
                c.style.marginLeft = 12f;
                c.style.flexGrow = 1;
                c.style.flexShrink = 1;
                c.Add(UIKit.Text(Loc.Tr("Tonis Karte", "Tony's Map"), PhoneKit.Tiny, UIPalette.Rgb(0x8E6C50), true));
                c.Add(PhoneKit.T(fog != null ? fog.Name : Loc.Tr("Nebelinsel", "Fog Isle"), PhoneKit.Body, UIPalette.Ink, true));
                c.Add(PhoneKit.T(Loc.Tr("Folge den gelben Bojen", "Follow the yellow buoys"), PhoneKit.Tiny, UIPalette.InkSoft));
                r.Add(c);
                var flagBtn = UIWidgets.RoundIconButton(UIGlyphs.Flag, () =>
                {
                    UIWaypoint.Set(WorldLayout.FogLaneStart, Loc.Tr("Bojen", "Buoys"), UIPalette.Rgb(0xF2C94C));
                    Phone.Refresh(true);
                }, 52f, UIPalette.CoralInk);
                r.Add(flagBtn);
                card.Add(r);
                body.Add(card);
            }

            // site list
            body.Add(PhoneKit.Section(Loc.Tr("Tauchplätze", "Dive sites"), UIGlyphs.Pin));
            float safe = gm.Gear.maxDepth;
            var safeRow = UIKit.Row();
            safeRow.style.marginBottom = 10f;
            safeRow.Add(new UIGlyph(UIGlyphs.Tank, UIPalette.Turquoise, 30f));
            safeRow.Add(UIKit.Text(" " + Loc.Tr("sicher bis ", "safe to ") + Loc.Meters(safe), PhoneKit.Body, UIPalette.Ink, true));
            body.Add(safeRow);
            Vector3 playerPos = WorldLayout.PlayerSpawn;
            var pl = Refs.Player;
            if (pl != null && !(pl is Object po && po == null) && pl.Root != null) playerPos = pl.Root.position;
            foreach (var site in DiveSites.All)
                if (!site.IsStorySite || fogMap) body.Add(SiteRow(site, safe, playerPos));
        }

        static VisualElement LegendItem(UIGlyph.DrawFn icon, Color c, string text)
        {
            var row = UIKit.Row();
            row.style.marginRight = 16f;
            row.style.marginBottom = 4f;
            var g = new UIGlyph(icon, c, 24f);
            g.style.marginRight = 6f;
            row.Add(g);
            row.Add(UIKit.Text(text, PhoneKit.Tiny, UIPalette.InkSoft, true));
            return row;
        }

        VisualElement SiteRow(DiveSite site, float safe, Vector3 playerPos)
        {
            bool unlocked = Progression.IsSiteUnlocked(site);
            var card = PhoneKit.Card(14f);
            card.style.borderLeftWidth = 8f;
            card.style.borderLeftColor = unlocked ? site.mapColor : UIPalette.A(site.mapColor, 0.4f);
            if (!unlocked) card.style.opacity = 0.85f;

            var head = UIKit.Row();
            var name = PhoneKit.T(site.Name, PhoneKit.Body, unlocked ? UIPalette.Ink : UIPalette.InkSoft, true);
            name.style.flexShrink = 1;
            name.style.flexGrow = 1;
            head.Add(name);
            float dist = Vector2.Distance(new Vector2(playerPos.x, playerPos.z), new Vector2(site.center.x, site.center.z));
            head.Add(UIKit.Text(UIKit.Distance(dist), PhoneKit.Small, UIPalette.InkSoft, true));
            bool isWp = UIWaypoint.Active && (UIWaypoint.Position - site.center).sqrMagnitude < 1f;
            var flag = UIWidgets.RoundIconButton(UIGlyphs.Flag, () =>
            {
                if (isWp) UIWaypoint.Clear();
                else UIWaypoint.Set(site.center, site.Name, site.mapColor);
                Phone.Refresh(true);
            }, 52f, isWp ? UIPalette.CoralInk : UIPalette.InkSoft);
            flag.style.marginLeft = 10f;
            if (isWp) flag.style.backgroundColor = UIPalette.A(UIPalette.Coral, 0.25f);
            head.Add(flag);
            var show = UIWidgets.RoundIconButton(UIGlyphs.Eye, () => { if (map != null) map.SetView(site.center, 5f); }, 52f, UIPalette.InkSoft);
            show.style.marginLeft = 6f;
            head.Add(show);
            card.Add(head);

            var depthRow = UIKit.Row();
            depthRow.style.marginTop = 6f;
            depthRow.Add(new UIGlyph(UIGlyphs.Depth, UIPalette.InkSoft, 24f));
            depthRow.Add(UIKit.Text(" " + UIKit.DepthRange(site.minDepth, site.maxDepth), PhoneKit.Small, UIPalette.Ink));
            depthRow.Add(UIKit.Flex());
            Color c; UIGlyph.DrawFn icon;
            if (safe >= site.maxDepth) { c = UIPalette.LeafInk; icon = UIGlyphs.Check; }
            else if (safe >= site.minDepth) { c = UIPalette.SunInk; icon = UIGlyphs.Warning; }
            else { c = UIPalette.CoralInk; icon = UIGlyphs.Warning; }
            depthRow.Add(new UIGlyph(icon, c, 26f));
            card.Add(depthRow);

            if (!unlocked)
            {
                Progression.SiteRequirementsMet(site, out string reason);
                var req = UIKit.Row();
                req.style.marginTop = 6f;
                req.Add(new UIGlyph(UIGlyphs.Lock, UIPalette.SunInk, 24f));
                var r = PhoneKit.T(string.IsNullOrEmpty(reason) ? Loc.Tr("Bald verfügbar", "Available soon") : reason, PhoneKit.Tiny, UIPalette.SunInk, true);
                r.style.marginLeft = 6f;
                r.style.flexShrink = 1;
                req.Add(r);
                card.Add(req);
            }
            return card;
        }

        public override void OnClose() { }

        public override void Tick(float dt)
        {
            if (map != null && map.panel != null) map.Tick(dt);
        }
    }

    /// Legacy painter overlay of the old sea chart (kept for other callers): dive-site circles (locked sites dashed).
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
            for (int pass = 0; pass < 2; pass++)
            {
                if (pass == 1) p.SetDashPattern(8f, 6f);
                foreach (var site in DiveSites.All)
                {
                    if (site.IsStorySite && !StoryFlags.Has(site.unlockFlag)) continue;
                    bool unlocked = GameManager.I != null && Progression.IsSiteUnlocked(site);
                    if (unlocked != (pass == 0)) continue;
                    var c = new Vector2(r.x + (site.center.x + half) / (2f * half) * r.width, r.y + (1f - (site.center.z + half) / (2f * half)) * r.height);
                    float rad = site.radius / (2f * half) * r.width;
                    p.fillColor = UIPalette.A(site.mapColor, unlocked ? 0.28f : 0.10f);
                    p.BeginPath(); p.Arc(c, rad, Angle.Degrees(0f), Angle.Degrees(360f)); p.ClosePath(); p.Fill();
                    p.strokeColor = unlocked ? Color.white : new Color(1f, 1f, 1f, 0.55f);
                    p.lineWidth = unlocked ? 3f : 2f;
                    p.BeginPath(); p.Arc(c, rad, Angle.Degrees(0f), Angle.Degrees(360f)); p.ClosePath(); p.Stroke();
                }
            }
            p.SetDashPattern(System.ReadOnlySpan<float>.Empty);
        }
    }

    /// K.8: the yellow buoy lane through the fog to the Fog Isle (dashed line + buoy dots) – legacy (the treasure map
    /// draws the lane in its ink layer).
    public class UIFogLane : VisualElement
    {
        public UIFogLane()
        {
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.left = 0; style.top = 0; style.right = 0; style.bottom = 0;
            generateVisualContent += Draw;
        }

        void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            if (r.width < 4f) return;
            var p = ctx.painter2D;
            float half = UIMapTexture.WorldHalf;
            Vector2 M(Vector3 w) => new Vector2(r.x + (w.x + half) / (2f * half) * r.width, r.y + (1f - (w.z + half) / (2f * half)) * r.height);
            Vector2 a = M(WorldLayout.FogLaneStart), b = M(WorldLayout.FogLaneEnd), isle = M(WorldLayout.FogIsland);
            var buoy = UIPalette.Rgb(0xF2C94C);
            p.strokeColor = buoy;
            p.lineWidth = 3f;
            p.SetDashPattern(6f, 5f);
            p.BeginPath(); p.MoveTo(a); p.LineTo(b); p.LineTo(isle); p.Stroke();
            p.SetDashPattern(System.ReadOnlySpan<float>.Empty);
            for (int i = 0; i < 6; i++)
            {
                var c = Vector2.Lerp(a, b, i / 5f);
                p.fillColor = buoy;
                p.BeginPath(); p.Arc(c, 4f, Angle.Degrees(0f), Angle.Degrees(360f)); p.ClosePath(); p.Fill();
            }
        }
    }
}
