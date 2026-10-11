using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Livestream requests from the UI (C.8): offline / host run the livestream module's custom command directly, a co-op
    /// guest forwards it to the host. Returns false when no module handles it (the livestream module is not installed).
    public static class UIStream
    {
        public static bool Request(string key, params string[] extra)
        {
            if (GameManager.I == null || !GameManager.I.InGame) return false;
            var args = new string[1 + (extra != null ? extra.Length : 0)];
            args[0] = key;
            if (extra != null) for (int i = 0; i < extra.Length; i++) args[i + 1] = extra[i];
            if (Net.IsClient)
            {
                Net.Command(NetCommand.Custom, args);
                return true;
            }
            return CoreCommands.Execute(Net.LocalId, NetCommand.Custom, args);
        }

        /// Why the stream cannot start right now (null = it can). C.1 / C.7 rules.
        public static string BlockReason(GameState s)
        {
            if (s == null) return "";
            if (s.followers < LiveState.UnlockFollowers) return Loc.Tr("Ab 1.000 Followern", "From 1,000 followers");
            if (s.lastStreamDay == s.day) return Loc.Tr("Heute schon gestreamt", "Already streamed today");
            bool dockOk = s.followers >= LiveState.DockStreamFollowers;
            var boat = Refs.Boat;
            if (boat is Object bo && bo == null) boat = null;
            bool aboard = boat != null && boat.PlayerAboard;
            if (aboard) return null;
            if (dockOk && OnDock()) return null;
            return dockOk ? Loc.Tr("Nur auf dem Boot oder am Steg", "Only on the boat or the dock") : Loc.Tr("Nur auf dem Boot", "Only on the boat");
        }

        static bool OnDock()
        {
            var p = Refs.Player;
            if (p is Object po && po == null) p = null;
            if (p == null || p.Root == null || p.IsInWater) return false;
            Vector3 a = WorldLayout.DockStart, b = WorldLayout.DockEnd, x = p.Root.position;
            Vector2 ab = new Vector2(b.x - a.x, b.z - a.z), ax = new Vector2(x.x - a.x, x.z - a.z);
            float t = Mathf.Clamp01(Vector2.Dot(ax, ab) / Mathf.Max(0.01f, ab.sqrMagnitude));
            return (ax - ab * t).sqrMagnitude < 4f * 4f;
        }
    }

    /// KRILL.tv (C.7): big „Live gehen“ button (disabled with the reason), live stats + „Stream beenden“ while live, the
    /// last streams. Locked below 1,000 followers.
    public class UIAppKrill : UIPhoneApp
    {
        public override string Id => "krill";
        public override string Title => "KRILL.tv";
        public override Color Color => UIPalette.Rgb(0x8E5CC2);
        public override UIGlyph.DrawFn Icon => UIGlyphs.Broadcast;

        public override bool ShowLock
        {
            get
            {
                var s = UIKit.State;
                return s == null || s.followers < LiveState.UnlockFollowers;
            }
        }

        public override int Badge => LiveState.IsLive ? -1 : 0;

        float tick;

        public override void Tick(float dt)
        {
            if (!LiveState.IsLive) return;
            tick -= dt;
            if (tick > 0f) return;
            tick = 1f;
            Phone.Refresh(true);
        }

        public override void Build(VisualElement body)
        {
            var s = UIKit.State;
            if (s == null) return;
            bool unlocked = s.followers >= LiveState.UnlockFollowers;

            var hero = UIKit.Col();
            hero.style.alignItems = Align.Center;
            hero.style.marginTop = 16f;
            var circle = UIKit.Box();
            UIKit.Size(circle, 180f, 180f);
            UIKit.Radius(circle, 90f);
            circle.style.backgroundColor = UIPalette.A(Color, 0.18f);
            UIKit.Border(circle, 4f, Color);
            UIKit.Center(circle);
            circle.Add(new UIGlyph(unlocked ? UIGlyphs.Broadcast : UIGlyphs.Lock, Color, 100f));
            hero.Add(circle);
            body.Add(hero);

            if (!unlocked)
            {
                var title = PhoneKit.T(Loc.Tr("Ab 1.000 Followern", "From 1,000 followers"), PhoneKit.Big, UIPalette.Ink, true);
                title.style.marginTop = 16f;
                title.style.unityTextAlign = TextAnchor.MiddleCenter;
                body.Add(title);
                var bar = PhoneKit.Bar(s.followers / (float)LiveState.UnlockFollowers, Color, 18f);
                bar.style.marginTop = 20f;
                body.Add(bar);
                var row = UIKit.Row();
                row.style.marginTop = 8f;
                row.Add(new UIGlyph(UIGlyphs.Person, UIPalette.Turquoise, 28f));
                row.Add(UIKit.Text(" " + Loc.Number(s.followers) + " / " + Loc.Number(LiveState.UnlockFollowers), PhoneKit.Body, UIPalette.Ink, true));
                body.Add(row);
                return;
            }

            if (LiveState.IsLive)
            {
                var live = UIKit.Pill("● LIVE", UIPalette.Live, Color.white, PhoneKit.Body);
                live.style.alignSelf = Align.Center;
                live.style.marginTop = 14f;
                body.Add(live);
                var stats = UIKit.Row();
                stats.style.marginTop = 14f;
                stats.Add(PhoneKit.Stat(UIGlyphs.Eye, UIPalette.InkSoft, UIKit.Compact(LiveState.Viewers)));
                stats.Add(UIKit.HSpacer(8f));
                stats.Add(PhoneKit.Stat(UIGlyphs.Clock, UIPalette.InkSoft, UIKit.ClockShort(Mathf.FloorToInt(LiveState.Seconds))));
                body.Add(stats);
                var m = UIKit.Row();
                m.style.marginTop = 8f;
                m.Add(PhoneKit.Stat(UIGlyphs.Coin, UIPalette.Sun, UIKit.SignedMoney(LiveState.MoneyThisStream), UIPalette.LeafInk));
                body.Add(m);
                var end = PhoneKit.IconButton(UIGlyphs.Cross, Loc.Tr("Stream beenden", "End stream"), () =>
                {
                    if (!UIStream.Request(CoreCommands.StreamEndKey)) UIAudio.Nope();
                    Phone.Refresh(true);
                }, UIBtnKind.Secondary, PhoneKit.Big);
                end.style.marginTop = 16f;
                body.Add(end);
            }
            else
            {
                string reason = UIStream.BlockReason(s);
                var go = PhoneKit.IconButton(UIGlyphs.Broadcast, Loc.Tr("Live gehen", "Go live"), () =>
                {
                    if (UIStream.BlockReason(UIKit.State) != null) { UIAudio.Nope(); return; }
                    if (!UIStream.Request(CoreCommands.StreamStartKey))
                    {
                        UIAudio.Nope();
                        GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr("KRILL.tv kommt bald", "KRILL.tv coming soon"));
                    }
                    else UIAudio.Pop();
                    Phone.Refresh(true);
                }, UIBtnKind.Live, PhoneKit.Big);
                go.style.marginTop = 18f;
                UIKit.SetEnabled(go, reason == null);
                body.Add(go);
                if (reason != null)
                {
                    var r = UIWidgets.Notice(reason, UIPalette.Sun, UIGlyphs.Lock, PhoneKit.Small);
                    r.style.marginTop = 10f;
                    body.Add(r);
                }
                var info = UIKit.Row();
                info.style.marginTop = 6f;
                info.style.alignSelf = Align.Center;
                info.Add(new UIGlyph(UIGlyphs.Clock, UIPalette.InkSoft, 22f));
                info.Add(UIKit.Text(" 10 min · " + Loc.Tr("1× pro Tag", "1× per day"), PhoneKit.Tiny, UIPalette.InkSoft, true));
                body.Add(info);
            }

            if (s.streams != null && s.streams.Count > 0)
            {
                body.Add(PhoneKit.Section(Loc.Tr("Letzte Streams", "Last streams"), UIGlyphs.Broadcast));
                int shown = 0;
                for (int i = s.streams.Count - 1; i >= 0 && shown < 6; i--, shown++)
                {
                    var st = s.streams[i];
                    if (st == null) continue;
                    var row = UIKit.Row();
                    row.style.backgroundColor = st.fainted ? UIPalette.A(UIPalette.Coral, 0.15f) : Color.white;
                    UIKit.Radius(row, 18f);
                    UIKit.Pad(row, 10f, 14f, 10f, 14f);
                    row.style.marginBottom = 8f;
                    row.Add(UIKit.Text(Loc.Tr("Tag ", "Day ") + st.day, PhoneKit.Small, UIPalette.Ink, true));
                    row.Add(UIKit.Flex());
                    row.Add(new UIGlyph(UIGlyphs.Eye, UIPalette.InkSoft, 20f));
                    row.Add(UIKit.Text(" " + UIKit.Compact(st.peakViewers) + "  ", PhoneKit.Tiny, UIPalette.InkSoft, true));
                    row.Add(new UIGlyph(UIGlyphs.Coin, UIPalette.Sun, 20f));
                    row.Add(UIKit.Text(" " + UIKit.SignedMoney(st.money) + "  ", PhoneKit.Tiny, UIPalette.LeafInk, true));
                    row.Add(new UIGlyph(UIGlyphs.Person, UIPalette.Turquoise, 20f));
                    row.Add(UIKit.Text(" " + UIKit.Signed(st.followers), PhoneKit.Tiny, st.followers < 0 ? UIPalette.CoralInk : UIPalette.TurquoiseInk, true));
                    body.Add(row);
                }
            }
        }
    }
}
