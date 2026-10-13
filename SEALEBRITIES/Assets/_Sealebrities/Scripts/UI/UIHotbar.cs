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

    /// Round 3 hotbar (GDD §19 „ein großer Balken statt 3 + 3“): ONE row of Hands.SlotCount slots at the bottom centre
    /// (keys 1–8 from GameInput.BindingLabel). Each slot shows its item icon, a count for stacks, a durability bar for
    /// weapons and a small mitten for offhand slots. The main hand's slot gets a thick white frame, the offhand's a
    /// turquoise one; the names of both held items sit below. Used-up stacks / gear not bought yet are dimmed.
    public class UIHotbar
    {
        public const float SlotSize = 74f, Gap = 8f;
        static readonly Color OffColor = UIPalette.Turquoise;

        class Slot
        {
            public VisualElement box, durTrack, durFill;
            public UIGlyph icon, offHand;
            public Label key, count;
            public string id;
            public int shownCount = -1, state = -1, durKey = -1;
            public bool usable = true;
            public int keyVersion = -1;
            public bool off;
        }

        public readonly VisualElement Root;
        readonly Slot[] slots = new Slot[Hands.SlotCount];
        readonly Label nameLabel;
        bool dirty = true;
        int keyVersion;
        float lastChange;
        string nameMain, nameOff;
        Language nameLang;

        public UIHotbar()
        {
            Root = UIKit.Col();
            Root.pickingMode = PickingMode.Ignore;
            Root.style.alignItems = Align.Center;

            var row = UIKit.Row();
            row.style.alignItems = Align.FlexEnd;
            for (int i = 0; i < slots.Length; i++)
            {
                var s = new Slot();
                s.box = UIKit.Box();
                UIKit.Size(s.box, SlotSize, SlotSize);
                UIKit.Radius(s.box, 12f);
                UIKit.Center(s.box);
                if (i > 0) s.box.style.marginLeft = Gap;
                UIKit.Transition(s.box, 0.1f, "scale");
                s.icon = new UIGlyph(null, Color.white, 56f);
                s.box.Add(s.icon);
                s.key = UIKit.HudText("", 13f, Color.white, true);
                s.key.style.position = Position.Absolute;
                s.key.style.left = 6f; s.key.style.top = 3f;
                s.box.Add(s.key);
                s.count = UIKit.HudText("", 15f, Color.white, true);
                s.count.style.position = Position.Absolute;
                s.count.style.right = 6f; s.count.style.bottom = 2f;
                s.box.Add(s.count);
                // offhand marker: a small mirrored mitten in the lower left corner
                s.offHand = new UIGlyph(UIGlyphs.Hand, OffColor, 18f);
                s.offHand.style.position = Position.Absolute;
                s.offHand.style.left = 4f; s.offHand.style.bottom = 4f;
                s.offHand.style.scale = new Scale(new Vector2(-1f, 1f));
                s.offHand.style.display = DisplayStyle.None;
                s.box.Add(s.offHand);
                // durability bar (weapons)
                s.durTrack = UIKit.Box();
                s.durTrack.style.position = Position.Absolute;
                s.durTrack.style.left = 10f; s.durTrack.style.right = 10f; s.durTrack.style.bottom = 5f;
                s.durTrack.style.height = 4f;
                UIKit.Radius(s.durTrack, 2f);
                s.durTrack.style.backgroundColor = new Color(0f, 0f, 0f, 0.35f);
                s.durFill = UIKit.Box();
                s.durFill.style.height = 4f;
                UIKit.Radius(s.durFill, 2f);
                s.durFill.style.backgroundColor = UIPalette.Leaf;
                s.durTrack.Add(s.durFill);
                s.durTrack.style.display = DisplayStyle.None;
                s.box.Add(s.durTrack);
                ApplySlotStyle(s, 0);
                row.Add(s.box);
                slots[i] = s;
            }
            Root.Add(row);
            nameLabel = UIKit.HudText("", 16f, Color.white, true);
            nameLabel.style.marginTop = 6f;
            nameLabel.style.height = 20f;
            nameLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            Root.Add(nameLabel);

            UIKit.IgnorePicking(Root);
            Hands.Changed += MarkDirty;
            Backpack.Changed += MarkDirty;
            GameEvents.GearChanged += MarkDirty;
            GameInput.BindingsChanged += OnBindings;
        }

        public void Dispose()
        {
            Hands.Changed -= MarkDirty;
            Backpack.Changed -= MarkDirty;
            GameEvents.GearChanged -= MarkDirty;
            GameInput.BindingsChanged -= OnBindings;
        }

        void OnBindings() { keyVersion++; dirty = true; }
        public void MarkDirty() { dirty = true; }
        public void RefreshTexts() { keyVersion++; dirty = true; nameMain = null; }

        /// 0 = idle, 1 = main hand, 2 = offhand.
        static void ApplySlotStyle(Slot s, int state)
        {
            bool held = state != 0;
            s.box.style.backgroundColor = held ? new Color(1f, 1f, 1f, 0.16f) : new Color(15f / 255f, 20f / 255f, 35f / 255f, 0.22f);
            Color border = state == 1 ? Color.white : state == 2 ? OffColor : new Color(1f, 1f, 1f, 0.45f);
            UIKit.Border(s.box, held ? 3f : 2f, border);
            s.box.style.scale = new Scale(held ? new Vector2(1.1f, 1.1f) : Vector2.one);
        }

        /// Seconds since the hotbar last changed (Q39 idle fade).
        public float IdleSeconds => Time.unscaledTime - lastChange;

        public void Tick()
        {
            if (!dirty) return;
            dirty = false;
            lastChange = Time.unscaledTime;
            int main = Hands.ActiveSlot(Hand.Right), off = Hands.ActiveSlot(Hand.Left);
            for (int i = 0; i < slots.Length; i++)
            {
                var s = slots[i];
                string id = Hands.SlotId(i);
                var def = ItemDatabase.Get(id);
                bool usable = Hands.SlotUsable(i);
                if (id != s.id || usable != s.usable)
                {
                    s.id = id;
                    s.usable = usable;
                    s.icon.SetDraw(UIGlyphs.ForItemId(id));
                    Color tint = UIGlyphs.TintFor(id);
                    tint.a = usable ? 1f : 0.35f;
                    s.icon.SetColor(tint);
                }
                int cnt = def != null && !def.isGear && def.maxStack > 1 ? Backpack.Count(id) : -1;
                if (cnt != s.shownCount)
                {
                    s.shownCount = cnt;
                    s.count.text = cnt > 1 ? cnt.ToString() : cnt == 0 ? "0" : "";
                }
                // durability of the best stack of a weapon
                int dk = -1;
                float dur01 = 1f;
                if (def != null && def.HasDurability)
                {
                    var st = Backpack.First(id);
                    dur01 = st != null ? Mathf.Clamp01(st.durability / Mathf.Max(1f, def.durability)) : 0f;
                    dk = Mathf.RoundToInt(dur01 * 20f);
                }
                if (dk != s.durKey)
                {
                    s.durKey = dk;
                    UIKit.Show(s.durTrack, dk >= 0);
                    if (dk >= 0)
                    {
                        s.durFill.style.width = Length.Percent(dur01 * 100f);
                        s.durFill.style.backgroundColor = dur01 < 0.25f ? UIPalette.Danger : dur01 < 0.5f ? UIPalette.Warn : UIPalette.Leaf;
                    }
                }
                bool isOff = Hands.SlotHand(i) == Hand.Left;
                if (isOff != s.off) { s.off = isOff; UIKit.Show(s.offHand, isOff); }
                int state = i == main ? 1 : i == off ? 2 : 0;
                if (state != s.state)
                {
                    s.state = state;
                    ApplySlotStyle(s, state);
                }
                if (s.keyVersion != keyVersion)
                {
                    s.keyVersion = keyVersion;
                    s.key.text = GameInput.BindingLabel(GameInput.SlotAction(i));
                }
            }
            // names of what is held: "Stock · Lampe" / "Angel · beidhändig"
            string m = Hands.ActiveId(Hand.Right), o = Hands.ActiveId(Hand.Left);
            if (m != nameMain || o != nameOff || nameLang != Loc.Current)
            {
                nameMain = m;
                nameOff = o;
                nameLang = Loc.Current;
                string n = Hands.DisplayName(m);
                if (m != null && Hands.IsTwoHandedId(m)) n += Loc.Tr(" · beidhändig", " · two-handed");
                string on = Hands.DisplayName(o);
                if (on.Length > 0) n = n.Length > 0 ? n + "  ·  " + on : on;
                nameLabel.text = n;
            }
        }
    }
}
