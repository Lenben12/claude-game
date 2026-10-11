using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Seekarte: drawn top-down chart (main island, landmark islands, dive sites, points of interest) with the live
    /// player arrow, the boat, co-op divers and a waypoint (tap the chart or a site's flag button; shown in the HUD).
    public class UIAppMap : UIPhoneApp
    {
        VisualElement map, playerArrow, boatMarker, waypointMarker;
        readonly List<VisualElement> crewDots = new List<VisualElement>();
        const float MapSize = 496f;

        public override string Id => "map";
        public override string Title => Loc.Tr("Seekarte", "Sea Chart");
        public override Color Color => UIPalette.Rgb(0x6EA6D4);
        public override UIGlyph.DrawFn Icon => UIGlyphs.Pin;

        static Vector2 ToMap01(Vector3 world)
        {
            float half = UIMapTexture.WorldHalf;
            return new Vector2((world.x + half) / (2f * half), 1f - (world.z + half) / (2f * half));
        }

        static Vector3 FromMap01(Vector2 m)
        {
            float half = UIMapTexture.WorldHalf;
            return new Vector3(m.x * 2f * half - half, 0f, (1f - m.y) * 2f * half - half);
        }

        public override void Build(VisualElement body)
        {
            var gm = GameManager.I;
            if (gm == null) return;
            crewDots.Clear();

            map = UIKit.Box("sb-map");
            UIKit.Size(map, MapSize, MapSize);
            map.style.alignSelf = Align.Center;
            UIKit.Radius(map, 24f);
            map.style.overflow = Overflow.Hidden;
            map.style.backgroundColor = UIPalette.Rgb(0x2E5F93);
            UIKit.Image(map, UIMapTexture.Get(), false);
            UIKit.Border(map, 4f, Color.white);
            body.Add(map);

            var overlay = new UIMapOverlay();
            UIKit.FillParent(overlay);
            map.Add(overlay);

            // points of interest
            AddPoi(WorldLayout.Hut, UIGlyphs.House, UIPalette.Sun);
            AddPoi(WorldLayout.Helipad, UIGlyphs.Heli, Color.white);
            AddPoi(WorldLayout.Lighthouse, UIGlyphs.Star, UIPalette.Coral);
            AddPoi(WorldLayout.WaterfallPool, UIGlyphs.Wave, Color.white);
            AddPoi(WorldLayout.TidePools, UIGlyphs.Shell, UIPalette.Sand);
            AddPoi(WorldLayout.RadioTowerRuins, UIGlyphs.Broadcast, Color.white);
            // L.8 the village of Altriff (map label)
            AddPoi(WorldLayout.Village, UIGlyphs.Hut, UIPalette.Rgb(0xEE7F2D));
            AddLabel(WorldLayout.Village, Loc.Tr("Altriff", "Oldreef"), 0f, 18f);
            if (StoryFlags.Has(StoryKeys.ToniArrived)) AddPoi(WorldLayout.DriftwoodBeach, UIGlyphs.Envelope, UIPalette.Rgb(0x4F7FAE));

            // K.8 Toni's map: the Fog Isle and the buoy lane appear once the flag is set (no spoiler before)
            bool fogMap = StoryFlags.Has(StoryKeys.FogMap);
            if (fogMap) map.Add(new UIFogLane());

            // L.7 quest markers: the tracked place task and NPCs with „!“ / „?“
            var target = QuestTracker.Target;
            if (target.HasValue) AddMarker(target.Value, "!", UIPalette.Sun);
            for (int i = 0; i < NpcRegistry.Active.Count; i++)
            {
                var npc = NpcRegistry.Active[i];
                if (npc == null || npc.Root == null || npc.Marker == 0) continue;
                if (npc is Object no && no == null) continue;
                AddMarker(npc.Root.position, npc.Marker == 2 ? "?" : "!", npc.Marker == 2 ? UIPalette.Leaf : UIPalette.Sun);
            }

            // site labels
            foreach (var site in DiveSites.All)
            {
                if (site.IsStorySite && !fogMap) continue;
                bool unlocked = Progression.IsSiteUnlocked(site);
                var pos = ToMap01(site.center);
                var lbl = UIKit.Row();
                lbl.pickingMode = PickingMode.Ignore;
                lbl.style.position = Position.Absolute;
                lbl.style.left = Length.Percent(pos.x * 100f);
                lbl.style.top = Length.Percent(pos.y * 100f);
                lbl.style.translate = new Translate(Length.Percent(-50f), Length.Percent(-50f));
                lbl.style.backgroundColor = unlocked ? Color.white : UIPalette.A(Color.white, 0.75f);
                UIKit.Radius(lbl, 10f);
                UIKit.Pad(lbl, 3f, 8f, 3f, 6f);
                if (!unlocked) lbl.Add(new UIGlyph(UIGlyphs.Lock, UIPalette.SunInk, 16f));
                else lbl.Add(UIKit.Dot(site.mapColor, 10f));
                var t = UIKit.Text(ShortName(site), 16f, UIPalette.Ink, true);
                t.style.marginLeft = 4f;
                lbl.Add(t);
                UIKit.IgnorePicking(lbl);
                map.Add(lbl);
            }

            // compass + scale
            var north = UIKit.Text("N", 18f, UIPalette.Ink, true);
            UIKit.Abs(north, null, 10f, 12f, null);
            north.style.backgroundColor = Color.white;
            UIKit.Radius(north, 15f);
            UIKit.Size(north, 30f, 30f);
            north.style.unityTextAlign = TextAnchor.MiddleCenter;
            north.pickingMode = PickingMode.Ignore;
            map.Add(north);
            var scale = UIKit.Col();
            UIKit.Abs(scale, 12f, null, null, 10f);
            var sbar = UIKit.Box();
            UIKit.Size(sbar, MapSize * 500f / WorldLayout.TerrainSize, 6f);
            sbar.style.backgroundColor = Color.white;
            UIKit.Radius(sbar, 3f);
            scale.Add(sbar);
            var sl = UIKit.Text("500 m", 15f, Color.white, true);
            UIKit.Outline(sl, 1.2f);
            scale.Add(sl);
            UIKit.IgnorePicking(scale);
            map.Add(scale);

            waypointMarker = new UIGlyph(UIGlyphs.Flag, UIPalette.Coral, 34f);
            waypointMarker.style.position = Position.Absolute;
            waypointMarker.style.marginLeft = -8f; waypointMarker.style.marginTop = -32f;
            map.Add(waypointMarker);
            boatMarker = new UIGlyph(UIGlyphs.Boat, Color.white, 26f);
            boatMarker.style.position = Position.Absolute;
            boatMarker.style.marginLeft = -13f; boatMarker.style.marginTop = -13f;
            map.Add(boatMarker);
            playerArrow = new UIGlyph(UIGlyphs.Arrow, UIPalette.Coral, 34f);
            playerArrow.style.position = Position.Absolute;
            playerArrow.style.marginLeft = -17f; playerArrow.style.marginTop = -17f;
            map.Add(playerArrow);
            UpdateMarkers();

            // tap the chart: waypoint at that spot (snaps to a dive site nearby)
            map.RegisterCallback<ClickEvent>(e =>
            {
                Vector2 local = e.localPosition;
                var world = FromMap01(new Vector2(local.x / MapSize, local.y / MapSize));
                DiveSite best = null;
                float bestD = 90f;
                foreach (var s in DiveSites.All)
                {
                    if (s.IsStorySite && !StoryFlags.Has(s.unlockFlag)) continue;
                    float d = Vector2.Distance(new Vector2(s.center.x, s.center.z), new Vector2(world.x, world.z));
                    if (d < Mathf.Max(bestD, s.radius) && (best == null || d < bestD)) { best = s; bestD = d; }
                }
                if (best != null) UIWaypoint.Set(best.center, best.Name, best.mapColor);
                else UIWaypoint.Set(world, Loc.Tr("Wegpunkt", "Waypoint"), UIPalette.Coral);
                UIAudio.Click();
                Phone.Refresh(true);
            });

            // legend
            var legend = UIKit.Row();
            legend.style.marginTop = 10f;
            legend.style.flexWrap = Wrap.Wrap;
            legend.Add(LegendItem(UIGlyphs.Arrow, UIPalette.Coral, Loc.Tr("Du", "You")));
            legend.Add(LegendItem(UIGlyphs.Boat, UIPalette.Ink, Loc.Tr("Boot", "Boat")));
            if (Net.IsOnline) legend.Add(LegendItem(UIGlyphs.Crew, UIPalette.Turquoise, "Crew"));
            legend.Add(LegendItem(UIGlyphs.Flag, UIPalette.Coral, Loc.Tr("Antippen = Wegpunkt", "Tap = waypoint")));
            body.Add(legend);
            if (UIWaypoint.Active)
            {
                var clear = PhoneKit.IconButton(UIGlyphs.Cross, Loc.Tr("Wegpunkt löschen", "Clear waypoint"), () => { UIWaypoint.Clear(); Phone.Refresh(true); }, UIBtnKind.Secondary, PhoneKit.Small);
                clear.style.marginTop = 8f;
                body.Add(clear);
            }

            // the secret island item (K.8) once Toni's map is ours
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

        void AddLabel(Vector3 world, string text, float dx, float dy)
        {
            var pos = ToMap01(world);
            var l = UIKit.Text(text, 15f, Color.white, true);
            UIKit.Outline(l, 1.4f, 0.3f);
            l.pickingMode = PickingMode.Ignore;
            l.style.position = Position.Absolute;
            l.style.left = Length.Percent(pos.x * 100f);
            l.style.top = Length.Percent(pos.y * 100f);
            l.style.marginLeft = dx - 24f;
            l.style.marginTop = dy - 8f;
            map.Add(l);
        }

        void AddMarker(Vector3 world, string text, Color c)
        {
            var pos = ToMap01(world);
            var e = UIKit.Box();
            e.pickingMode = PickingMode.Ignore;
            e.style.position = Position.Absolute;
            e.style.left = Length.Percent(Mathf.Clamp01(pos.x) * 100f);
            e.style.top = Length.Percent(Mathf.Clamp01(pos.y) * 100f);
            UIKit.Size(e, 26f, 26f);
            e.style.marginLeft = -13f; e.style.marginTop = -30f;
            UIKit.Radius(e, 13f);
            e.style.backgroundColor = c;
            UIKit.Border(e, 2f, Color.white);
            UIKit.Center(e);
            e.Add(UIKit.Text(text, 18f, UIPalette.Ink, true));
            map.Add(e);
        }

        static string ShortName(DiveSite s)
        {
            string n = s.Name;
            int q = n.IndexOf(" „", System.StringComparison.Ordinal);
            if (q < 0) q = n.IndexOf(" “", System.StringComparison.Ordinal);
            return q > 0 ? n.Substring(0, q) : n;
        }

        void AddPoi(Vector3 world, UIGlyph.DrawFn icon, Color c)
        {
            var pos = ToMap01(world);
            var e = UIKit.Box();
            e.pickingMode = PickingMode.Ignore;
            e.style.position = Position.Absolute;
            e.style.left = Length.Percent(pos.x * 100f);
            e.style.top = Length.Percent(pos.y * 100f);
            UIKit.Size(e, 22f, 22f);
            e.style.marginLeft = -11f; e.style.marginTop = -11f;
            UIKit.Radius(e, 11f);
            e.style.backgroundColor = UIPalette.A(UIPalette.Ink, 0.55f);
            UIKit.Center(e);
            e.Add(new UIGlyph(icon, c, 15f));
            map.Add(e);
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

        void UpdateMarkers()
        {
            if (map == null || map.panel == null) return;
            var player = Refs.Player;
            if (player is Object po && po == null) player = null;
            Transform local = null;
            if (player != null && player.Root != null)
            {
                local = player.Root;
                UIKit.Show(playerArrow, true);
                var p01 = ToMap01(player.Root.position);
                playerArrow.style.left = Mathf.Clamp01(p01.x) * MapSize;
                playerArrow.style.top = Mathf.Clamp01(p01.y) * MapSize;
                Transform yawSource = player.View != null ? player.View.transform : player.Root;
                playerArrow.style.rotate = new Rotate(Angle.Degrees(yawSource.eulerAngles.y));
            }
            else UIKit.Show(playerArrow, false);

            var boat = Refs.Boat;
            if (boat is Object bo && bo == null) boat = null;
            if (boat != null && boat.Root != null)
            {
                UIKit.Show(boatMarker, true);
                var b01 = ToMap01(boat.Root.position);
                boatMarker.style.left = Mathf.Clamp01(b01.x) * MapSize;
                boatMarker.style.top = Mathf.Clamp01(b01.y) * MapSize;
                boatMarker.style.rotate = new Rotate(Angle.Degrees(boat.Root.eulerAngles.y));
            }
            else UIKit.Show(boatMarker, false);

            UIKit.Show(waypointMarker, UIWaypoint.Active);
            if (UIWaypoint.Active)
            {
                var w01 = ToMap01(UIWaypoint.Position);
                waypointMarker.style.left = Mathf.Clamp01(w01.x) * MapSize;
                waypointMarker.style.top = Mathf.Clamp01(w01.y) * MapSize;
            }

            // co-op divers (everyone in Divers.All except the local player)
            int n = 0;
            var all = Divers.All;
            for (int i = 0; i < all.Count; i++)
            {
                var t = all[i];
                if (t == null || t == local) continue;
                if (n >= crewDots.Count)
                {
                    var dot = UIKit.Dot(UIPalette.Turquoise, 18f);
                    UIKit.Border(dot, 3f, Color.white);
                    dot.style.position = Position.Absolute;
                    dot.style.marginLeft = -9f; dot.style.marginTop = -9f;
                    dot.pickingMode = PickingMode.Ignore;
                    map.Add(dot);
                    crewDots.Add(dot);
                }
                var d = crewDots[n++];
                UIKit.Show(d, true);
                var c01 = ToMap01(t.position);
                d.style.left = Mathf.Clamp01(c01.x) * MapSize;
                d.style.top = Mathf.Clamp01(c01.y) * MapSize;
            }
            for (int i = n; i < crewDots.Count; i++) UIKit.Show(crewDots[i], false);
        }

        public override void Tick(float dt)
        {
            UpdateMarkers();
        }
    }

    /// Painter2D overlay of the sea chart: dive-site circles (locked sites dimmed and dashed).
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
                    if (site.IsStorySite && !StoryFlags.Has(site.unlockFlag)) continue;   // the Fog Isle stays hidden until the map
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

    /// K.8: the yellow buoy lane through the fog to the Fog Isle (dashed line + buoy dots) on the sea chart.
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
            // soft fog ring around the island
            float fr = WorldLayout.FogBankRadius / (2f * half) * r.width;
            p.strokeColor = new Color(1f, 1f, 1f, 0.45f);
            p.lineWidth = 2f;
            p.SetDashPattern(3f, 5f);
            p.BeginPath(); p.Arc(isle, fr, Angle.Degrees(0f), Angle.Degrees(360f)); p.ClosePath(); p.Stroke();
            p.SetDashPattern(System.ReadOnlySpan<float>.Empty);
        }
    }
}
