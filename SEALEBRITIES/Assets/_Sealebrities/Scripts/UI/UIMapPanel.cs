using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Round 3 (GDD §19 „Während der Fahrt hat man Zugriff auf die Karte und die Markierungen“, Q36 „Beifahrer: Filmen +
    /// Karte ansehen“): the treasure map as an overlay on the right side of the screen (key M, rebindable). It does NOT
    /// block gameplay – the driver keeps steering with WASD, the passenger keeps sitting, on land you can keep walking –
    /// it only frees the cursor for dragging / zooming / pins (UIState.MapOverlayOpen). Esc, M or ✕ close it; the phone and
    /// the backpack close it too. Follows the player by default (◎ turns following back on after a drag).
    /// Also ticks MapDiscovery (places on the map) and warms up the parchment art in the background while in game.
    public class UIMapPanel
    {
        public const float CardW = 560f;
        readonly VisualElement root, card;
        readonly UITreasureMap map;
        readonly Label keyCap, title;
        int keyVersion = -1;
        float warmDelay = 3f;

        public UIMapPanel(VisualElement layer)
        {
            root = new VisualElement { name = "sb-map-panel", pickingMode = PickingMode.Ignore };
            UIKit.FillParent(root);
            layer.Add(root);

            card = UIKit.Col();
            UIKit.Abs(card, null, 160f, 36f, null);
            card.style.width = CardW;
            card.style.backgroundColor = new Color(0.96f, 0.91f, 0.78f, 0.97f);
            UIKit.Radius(card, 26f);
            UIKit.Pad(card, 12f);
            UIKit.Border(card, 3f, UIPalette.Rgb(0x6B4E33));
            card.style.borderBottomWidth = 6f;
            root.Add(card);

            var head = UIKit.Row();
            head.style.marginBottom = 10f;
            head.Add(UIWidgets.IconTile(UIGlyphs.Pin, UIPalette.Rgb(0xB0782E), 40f));
            title = UIKit.Text(Loc.Tr("Karte", "Map"), 26f, UITreasureMapArt.Ink, true);
            title.style.marginLeft = 10f;
            head.Add(title);
            head.Add(UIKit.Flex());
            keyCap = UIKit.KeyCap(GameInput.BindingLabel(GameAction.Map), 18f);
            head.Add(keyCap);
            var close = UIWidgets.RoundIconButton(UIGlyphs.Cross, Close, 44f, UITreasureMapArt.Ink);
            close.style.marginLeft = 10f;
            head.Add(close);
            card.Add(head);

            map = new UITreasureMap(UITreasureMap.OverlayView, true);
            UIKit.Size(map, CardW - 24f - 6f, CardW - 24f - 6f);
            card.Add(map);

            root.style.display = DisplayStyle.None;
        }

        public bool IsOpen => UIState.MapOverlayOpen;
        public UITreasureMap Map => map;

        public static bool CanOpen
        {
            get
            {
                var gm = GameManager.I;
                return gm != null && gm.InGame && !UIState.MainMenuOpen && !UIState.LoadingOpen && !UIState.PauseOpen && !UIState.CharacterOpen;
            }
        }

        public void Open()
        {
            if (!CanOpen) return;
            UIState.MapOverlayOpen = true;
            root.style.display = DisplayStyle.Flex;
            int v = GameInput.BindingsVersion * 31 + (int)Loc.Current;
            if (v != keyVersion)
            {
                keyVersion = v;
                keyCap.text = GameInput.BindingLabel(GameAction.Map);
                title.text = Loc.Tr("Karte", "Map");
            }
            UIAudio.Click();
        }

        public void Close()
        {
            if (!UIState.MapOverlayOpen && root.style.display.value == DisplayStyle.None) return;
            UIState.MapOverlayOpen = false;
            root.style.display = DisplayStyle.None;
        }

        public void Toggle()
        {
            if (UIState.MapOverlayOpen) Close(); else Open();
        }

        public void Tick(float dt)
        {
            var gm = GameManager.I;
            bool inGame = gm != null && gm.InGame && !UIState.MainMenuOpen && !UIState.LoadingOpen;
            if (!inGame)
            {
                if (UIState.MapOverlayOpen) Close();
                warmDelay = 3f;
                return;
            }
            MapDiscovery.Tick(dt);
            if (UIState.MapOverlayOpen)
            {
                // a menu on top (phone, backpack, pause, NPC wheel …) takes over
                if (UIState.BlocksGameplay) { Close(); return; }
                map.Tick(dt);
            }
            else
            {
                // warm the parchment art up in the background (≈1 ms per frame) so the first map open is instant
                if (warmDelay > 0f) warmDelay -= dt;
                else UITreasureMapArt.Pump(1f);
            }
        }
    }
}
