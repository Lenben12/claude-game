using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Map waypoint shown in the HUD (set from the Seekarte app).
    public static class UIWaypoint
    {
        public static bool Active;
        public static Vector3 Position;
        public static string Label;
        public static Color Color = new Color(1f, 0.48f, 0.36f);

        public static event System.Action Changed;

        public static void Set(Vector3 position, string label, Color color)
        {
            Active = true;
            Position = position;
            Label = label;
            Color = color;
            Changed?.Invoke();
        }

        public static void Clear()
        {
            if (!Active) return;
            Active = false;
            Changed?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Active = false; Label = null; Changed = null; }
    }

    /// How-to-Fish style hotbar at the bottom centre: left hand (offhand) group and right hand group, 3 slots each,
    /// item icons, key labels from GameInput.BindingLabel, highlighted active slot and the item name below.
    /// The two-handed rod dims the other hand and joins both groups with a white bracket.
    public class UIHotbar
    {
        public const float SlotSize = 84f, Gap = 10f, GroupGap = 56f;

        class Slot
        {
            public VisualElement box;
            public UIGlyph icon;
            public Label key;
            public HoldItem item = (HoldItem)(-1);
            public bool owned, active;
            public int keyVersion = -1;
        }

        public readonly VisualElement Root;
        readonly Slot[] left = new Slot[Hands.SlotsPerHand], right = new Slot[Hands.SlotsPerHand];
        readonly Label leftName, rightName;
        readonly VisualElement leftGroup, rightGroup, bracket;
        bool dirty = true;
        int keyVersion;
        bool twoHanded;

        public UIHotbar()
        {
            Root = UIKit.Row();
            Root.pickingMode = PickingMode.Ignore;
            Root.style.alignItems = Align.FlexEnd;

            leftGroup = Group(Hand.Left, left, out leftName);
            Root.Add(leftGroup);
            var mid = UIKit.Box();
            mid.style.width = GroupGap;
            mid.style.height = SlotSize;
            mid.style.flexShrink = 0;
            mid.style.marginBottom = 24f;
            bracket = UIKit.Box();
            bracket.style.position = Position.Absolute;
            bracket.style.left = -SlotSize * 0.5f; bracket.style.right = -SlotSize * 0.5f;
            bracket.style.bottom = -12f;
            bracket.style.height = 10f;
            bracket.style.borderLeftWidth = 3f; bracket.style.borderRightWidth = 3f; bracket.style.borderBottomWidth = 3f;
            UIKit.BorderColor(bracket, Color.white);
            bracket.style.borderBottomLeftRadius = 6f; bracket.style.borderBottomRightRadius = 6f;
            bracket.style.display = DisplayStyle.None;
            mid.Add(bracket);
            Root.Add(mid);
            rightGroup = Group(Hand.Right, right, out rightName);
            Root.Add(rightGroup);

            UIKit.IgnorePicking(Root);
            Hands.Changed += MarkDirty;
            GameEvents.GearChanged += MarkDirty;
            GameInput.BindingsChanged += OnBindings;
        }

        public void Dispose()
        {
            Hands.Changed -= MarkDirty;
            GameEvents.GearChanged -= MarkDirty;
            GameInput.BindingsChanged -= OnBindings;
        }

        void OnBindings() { keyVersion++; dirty = true; }
        public void MarkDirty() { dirty = true; }
        public void RefreshTexts() { keyVersion++; dirty = true; }

        VisualElement Group(Hand hand, Slot[] slots, out Label name)
        {
            var g = UIKit.Col();
            g.style.alignItems = Align.Center;
            var handIcon = new UIHudGlyph(UIGlyphs.Hand, new Color(1f, 1f, 1f, 0.7f), 28f);
            if (hand == Hand.Left) handIcon.style.scale = new Scale(new Vector2(-1f, 1f));
            handIcon.style.marginBottom = 6f;
            g.Add(handIcon);
            var row = UIKit.Row();
            for (int i = 0; i < slots.Length; i++)
            {
                var s = new Slot();
                s.box = UIKit.Box();
                UIKit.Size(s.box, SlotSize, SlotSize);
                UIKit.Radius(s.box, 12f);
                UIKit.Center(s.box);
                if (i > 0) s.box.style.marginLeft = Gap;
                UIKit.Transition(s.box, 0.1f, "scale");
                s.icon = new UIGlyph(null, Color.white, 64f);
                s.box.Add(s.icon);
                s.key = UIKit.HudText("", 13f, Color.white, true);
                s.key.style.position = Position.Absolute;
                s.key.style.left = 7f; s.key.style.top = 4f;
                s.box.Add(s.key);
                ApplySlotStyle(s, false);
                row.Add(s.box);
                slots[i] = s;
            }
            g.Add(row);
            name = UIKit.HudText("", 16f, Color.white, true);
            name.style.marginTop = 6f;
            name.style.height = 20f;
            name.style.unityTextAlign = TextAnchor.MiddleCenter;
            g.Add(name);
            return g;
        }

        static void ApplySlotStyle(Slot s, bool active)
        {
            s.box.style.backgroundColor = active ? new Color(1f, 1f, 1f, 0.16f) : new Color(15f / 255f, 20f / 255f, 35f / 255f, 0.22f);
            UIKit.Border(s.box, active ? 3f : 2f, active ? Color.white : new Color(1f, 1f, 1f, 0.45f));
            s.box.style.scale = new Scale(active ? new Vector2(1.1f, 1.1f) : Vector2.one);
        }

        public void Tick()
        {
            if (!dirty) return;
            dirty = false;
            Refresh(Hand.Left, left, leftName);
            Refresh(Hand.Right, right, rightName);
            bool rod = Hands.IsTwoHanded(Hands.Active(Hand.Right)) || Hands.IsTwoHanded(Hands.Active(Hand.Left));
            if (rod != twoHanded)
            {
                twoHanded = rod;
                UIKit.Show(bracket, rod);
            }
            bool rodRight = Hands.IsTwoHanded(Hands.Active(Hand.Right));
            bool rodLeft = Hands.IsTwoHanded(Hands.Active(Hand.Left));
            leftGroup.style.opacity = rodRight ? 0.45f : 1f;
            rightGroup.style.opacity = rodLeft ? 0.45f : 1f;
            if (rodRight) leftName.text = "";
            if (rodLeft) rightName.text = "";
        }

        void Refresh(Hand hand, Slot[] slots, Label name)
        {
            int active = Hands.ActiveSlot(hand);
            for (int i = 0; i < slots.Length; i++)
            {
                var s = slots[i];
                var item = Hands.SlotItem(hand, i);
                bool owned = item != HoldItem.None && Hands.Owns(item);
                bool act = i == active;
                if (item != s.item || owned != s.owned)
                {
                    s.item = item;
                    s.owned = owned;
                    s.icon.SetDraw(UIGlyphs.ForItem(item));
                    s.icon.SetColor(new Color(1f, 1f, 1f, owned ? 1f : 0.35f));
                }
                if (act != s.active)
                {
                    s.active = act;
                    ApplySlotStyle(s, act);
                }
                if (s.keyVersion != keyVersion)
                {
                    s.keyVersion = keyVersion;
                    s.key.text = GameInput.BindingLabel((GameAction)((hand == Hand.Right ? (int)GameAction.RightSlot1 : (int)GameAction.LeftSlot1) + i));
                }
            }
            var held = active >= 0 ? Hands.SlotItem(hand, active) : HoldItem.None;
            string n = held == HoldItem.None ? "" : Hands.DisplayName(held);
            if (held == HoldItem.FishingRod) n += Loc.Tr(" · beidhändig", " · two-handed");
            if (!string.Equals(name.text, n)) name.text = n;
        }
    }
}
