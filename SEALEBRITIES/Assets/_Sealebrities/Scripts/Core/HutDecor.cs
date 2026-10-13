using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Sealebrities
{
    /// Round 3 (GDD §19 „Das Haus innen schöner, und man kann es individueller einrichten“): the hut's furnishing –
    /// SHARED state (the hut belongs to the whole crew). Index 0 of every list is the standard look, so an empty /
    /// zeroed state (old saves) shows today's hut.
    [Serializable]
    public class HutDecorState
    {
        /// HutDecor.Walls index (interior wall paint).
        public int wall;
        /// HutDecor.Floors index (floor boards).
        public int floor;
        /// HutDecor.Rugs index (pattern + colours; HutDecor.RugNone = no rug).
        public int rug;
        /// HutDecor.Curtains index (HutDecor.CurtainsNone = no curtains).
        public int curtains;
        /// Bought decor ids (free and unlocked items are never listed here).
        public List<string> owned = new List<string>();
        /// Decor ids shown in the hut (each item has its fixed place).
        public List<string> placed = new List<string>();
    }

    /// How a decor item becomes available.
    public enum HutDecorUnlock { Free, Buy, Shells, Pages, Bottles, Achievement }

    public sealed class HutDecorItem
    {
        public string id, nameDe, nameEn;
        public HutDecorUnlock unlock;
        /// Price in $ (Buy) or the number of collectibles needed (Shells / Pages / Bottles).
        public long price;
        public int need;
        /// Achievement id (Achievement).
        public string achievement;
        /// Icon tint for the UI.
        public Color tint;
        public string Name => Loc.Tr(nameDe, nameEn);
    }

    /// One colour choice (wall paint, floor stain, rug, curtains). b / c = second / third colour (floor tones, rug
    /// pattern) – a == b == c for single-colour options.
    public struct HutSwatch
    {
        public string nameDe, nameEn;
        public Color a, b, c;
        public string Name => Loc.Tr(nameDe, nameEn);
        public HutSwatch(string de, string en, string hexA, string hexB = null, string hexC = null)
        {
            nameDe = de; nameEn = en;
            a = LowPoly.Hex(hexA);
            b = hexB != null ? LowPoly.Hex(hexB) : a;
            c = hexC != null ? LowPoly.Hex(hexC) : b;
        }
    }

    /// Catalogue + the public API of the hut furnishing. Every change goes through the host:
    ///   HutDecor.SetWall / SetFloor / SetRug / SetCurtains / SetPlaced / Toggle / Buy
    /// → clients send NetCommand.Custom CoreCommands.HutKey (op, value) and apply it optimistically; the host applies
    /// it (Execute), forces a snapshot, and the snapshot (GameState.hut is shared) brings every guest to the same hut.
    /// The World module's PropHutDecor shows the state (it polls Signature, so loads and snapshots apply too).
    public static class HutDecor
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static GameState S => GameManager.I != null ? GameManager.I.State : null;

        // ------------------------------------------------------------------ catalogue
        public static readonly HutSwatch[] Walls =
        {
            new HutSwatch("Kalkweiß", "Whitewash", "#DCCFBE", "#CFC2B2", "#E3D8C9"),
            new HutSwatch("Lagune", "Lagoon", "#8CC4C0", "#80B8B4", "#97CDC9"),
            new HutSwatch("Koralle", "Coral", "#E3A08C", "#D99482", "#EAAB98"),
            new HutSwatch("Sonnengelb", "Sunshine", "#E8CF8A", "#DDC37F", "#EFD898"),
            new HutSwatch("Salbei", "Sage", "#A9BC94", "#9DB088", "#B3C59F"),
            new HutSwatch("Himmelblau", "Sky blue", "#9DB7D4", "#91ABC8", "#A9C2DD"),
            new HutSwatch("Flieder", "Lilac", "#B8A6CC", "#AD9AC1", "#C2B1D5"),
            new HutSwatch("Treibholz", "Driftwood", "#9A7B60", "#8C6F56", "#A6876B"),
        };

        public static readonly HutSwatch[] Floors =
        {
            new HutSwatch("Honigholz", "Honey wood", "#8E6C50", "#7F6149", "#9A795D"),
            new HutSwatch("Dunkle Eiche", "Dark oak", "#5E4436", "#553D31", "#684C3D"),
            new HutSwatch("Treibholz hell", "Bleached driftwood", "#C9B08E", "#BCA382", "#D3BC9C"),
            new HutSwatch("Weiß lackiert", "Painted white", "#E3DBCD", "#D8CFC0", "#ECE5D8"),
            new HutSwatch("Bootsblau", "Boat blue", "#5F8FA8", "#56849C", "#6A9AB3"),
            new HutSwatch("Schiffsrot", "Ship red", "#9A5650", "#8E4D48", "#A6615A"),
        };

        /// Rug patterns: 0 stripes (today's), 1 round, 2 checks, 3 zigzag kilim, 4 straw mat, 5 none.
        public static readonly HutSwatch[] Rugs =
        {
            new HutSwatch("Streifen", "Stripes", "#8E5CC2", "#F2C94C"),
            new HutSwatch("Rund", "Round", "#E07A62", "#F1E6CC", "#3FA9B5"),
            new HutSwatch("Karo", "Checks", "#3FA9B5", "#EDE6DA"),
            new HutSwatch("Zickzack", "Zigzag", "#B8454A", "#F28C4A", "#1E1B2B"),
            new HutSwatch("Bastmatte", "Straw mat", "#C9B48A", "#A88763"),
            new HutSwatch("Kein Teppich", "No rug", "#5A6478"),
        };
        public const int RugNone = 5;

        public static readonly HutSwatch[] Curtains =
        {
            new HutSwatch("Leinen", "Linen", "#E8DCC0"),
            new HutSwatch("Türkis", "Turquoise", "#3FA9B5"),
            new HutSwatch("Koralle", "Coral", "#E07A62"),
            new HutSwatch("Sonnengelb", "Sunshine", "#E8C25A"),
            new HutSwatch("Marine", "Navy", "#3E5A8A"),
            new HutSwatch("Rosa", "Pink", "#E86FA0"),
            new HutSwatch("Keine", "None", "#5A6478"),
        };
        public const int CurtainsNone = 6;

        public static readonly HutDecorItem[] Items =
        {
            new HutDecorItem { id = "palm", nameDe = "Topfpalme", nameEn = "Potted palm", unlock = HutDecorUnlock.Free, tint = LowPoly.Hex("#7FA243") },
            new HutDecorItem { id = "lights", nameDe = "Lichterkette", nameEn = "String lights", unlock = HutDecorUnlock.Free, tint = LowPoly.Hex("#F2C94C") },
            new HutDecorItem { id = "surfboard", nameDe = "Surfbrett an der Wand", nameEn = "Wall surfboard", unlock = HutDecorUnlock.Buy, price = 450, tint = LowPoly.Hex("#F28C4A") },
            new HutDecorItem { id = "records", nameDe = "Plattenspieler", nameEn = "Record player", unlock = HutDecorUnlock.Buy, price = 600, tint = LowPoly.Hex("#B8454A") },
            new HutDecorItem { id = "hangchair", nameDe = "Hängesessel", nameEn = "Hanging chair", unlock = HutDecorUnlock.Buy, price = 1200, tint = LowPoly.Hex("#C9B48A") },
            new HutDecorItem { id = "marlin", nameDe = "Marlin-Trophäe", nameEn = "Mounted marlin", unlock = HutDecorUnlock.Buy, price = 1800, tint = LowPoly.Hex("#3E5A8A") },
            new HutDecorItem { id = "aquarium", nameDe = "Aquarium", nameEn = "Aquarium", unlock = HutDecorUnlock.Buy, price = 3000, tint = LowPoly.Hex("#3FA9B5") },
            new HutDecorItem { id = "shells", nameDe = "Muschelgirlande", nameEn = "Shell garland", unlock = HutDecorUnlock.Shells, need = 3, tint = LowPoly.Hex("#F2C94C") },
            new HutDecorItem { id = "logframe", nameDe = "Logbuch-Bilderrahmen", nameEn = "Framed logbook pages", unlock = HutDecorUnlock.Pages, need = 3, tint = LowPoly.Hex("#F1E6CC") },
            new HutDecorItem { id = "bottles", nameDe = "Flaschenpost-Regal", nameEn = "Bottle-post shelf", unlock = HutDecorUnlock.Bottles, need = 1, tint = LowPoly.Hex("#7FB89A") },
            new HutDecorItem { id = "finbutton", nameDe = "Silberner Fin-Button", nameEn = "Silver Fin button", unlock = HutDecorUnlock.Achievement, achievement = "f10k", tint = LowPoly.Hex("#C8CDD3") },
        };

        public static HutDecorItem Item(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < Items.Length; i++) if (Items[i].id == id) return Items[i];
            return null;
        }

        public static int ItemIndex(string id)
        {
            if (string.IsNullOrEmpty(id)) return -1;
            for (int i = 0; i < Items.Length; i++) if (Items[i].id == id) return i;
            return -1;
        }

        // ------------------------------------------------------------------ events / hooks
        /// Raised on THIS machine after a local (or optimistic) change. Snapshots / loads are picked up by polling
        /// Signature (PropHutDecor).
        public static event Action Changed;
        /// UI: opens the furnishing screen (set by UIHutDecor).
        public static Action OpenHandler;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Changed = null; OpenHandler = null; }

        public static void OpenUI()
        {
            if (OpenHandler != null) OpenHandler();
            else GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr("Einrichten", "Furnish"), "–");
        }

        // ------------------------------------------------------------------ state
        public static HutDecorState Default() => new HutDecorState();

        public static HutDecorState State
        {
            get
            {
                var s = S;
                if (s == null) return null;
                if (s.hut == null) s.hut = Default();
                return s.hut;
            }
        }

        public static int Wall => State != null ? State.wall : 0;
        public static int Floor => State != null ? State.floor : 0;
        public static int Rug => State != null ? State.rug : 0;
        public static int CurtainChoice => State != null ? State.curtains : 0;

        /// Repairs a loaded / received state: indices in range, unknown or duplicate ids removed.
        public static void Normalize(HutDecorState h)
        {
            if (h == null) return;
            h.wall = Clamp(h.wall, Walls.Length);
            h.floor = Clamp(h.floor, Floors.Length);
            h.rug = Clamp(h.rug, Rugs.Length);
            h.curtains = Clamp(h.curtains, Curtains.Length);
            if (h.owned == null) h.owned = new List<string>();
            if (h.placed == null) h.placed = new List<string>();
            CleanIds(h.owned, true);
            CleanIds(h.placed, false);
            // a bought item can only stand in the hut when it was bought
            for (int i = h.placed.Count - 1; i >= 0; i--)
            {
                var it = Item(h.placed[i]);
                if (it != null && it.unlock == HutDecorUnlock.Buy && !h.owned.Contains(it.id)) h.placed.RemoveAt(i);
            }
        }

        static int Clamp(int v, int n) => v < 0 || v >= n ? 0 : v;

        static void CleanIds(List<string> list, bool buyOnly)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var it = Item(list[i]);
                bool bad = it == null || (buyOnly && it.unlock != HutDecorUnlock.Buy);
                if (!bad)
                    for (int k = 0; k < i; k++) if (list[k] == list[i]) { bad = true; break; }
                if (bad) list.RemoveAt(i);
            }
        }

        /// Allocation-free hash of everything that is visible (PropHutDecor re-applies when it changes).
        public static int Signature(GameState s)
        {
            if (s == null || s.hut == null) return 17;
            var h = s.hut;
            unchecked
            {
                int x = 17;
                x = x * 31 + h.wall;
                x = x * 31 + h.floor * 7;
                x = x * 31 + h.rug * 13;
                x = x * 31 + h.curtains * 19;
                var p = h.placed;
                if (p != null) for (int i = 0; i < p.Count; i++) x = x * 31 + (p[i] != null ? p[i].GetHashCode() : 0);
                return x;
            }
        }

        public static bool IsPlaced(string id)
        {
            var h = State;
            return h != null && h.placed.Contains(id);
        }

        public static bool IsOwned(string id)
        {
            var h = State;
            return h != null && h.owned.Contains(id);
        }

        /// Collectible progress toward an unlock (shells / pages / bottles), else 0.
        public static int Progress(HutDecorItem it)
        {
            if (it == null || GameManager.I == null) return 0;
            switch (it.unlock)
            {
                case HutDecorUnlock.Shells: return Collectibles.Count(CollectibleKind.GoldenShell);
                case HutDecorUnlock.Pages: return Collectibles.Count(CollectibleKind.LogbookPage);
                case HutDecorUnlock.Bottles: return Collectibles.Count(CollectibleKind.BottleMessage);
            }
            return 0;
        }

        /// True when the item may be placed: free, bought, or its find / achievement condition holds (shared state, so
        /// every machine decides the same).
        public static bool IsAvailable(string id)
        {
            var it = Item(id);
            var s = S;
            if (it == null || s == null) return false;
            switch (it.unlock)
            {
                case HutDecorUnlock.Free: return true;
                case HutDecorUnlock.Buy: return IsOwned(id);
                case HutDecorUnlock.Achievement: return s.achievements != null && s.achievements.Contains(it.achievement);
                default: return Progress(it) >= it.need;
            }
        }

        /// Short hint for a locked item (UI), e.g. „2/3 Muscheln“, „600 $“, „10.000 Follower“.
        public static string LockHint(HutDecorItem it)
        {
            if (it == null) return "";
            switch (it.unlock)
            {
                case HutDecorUnlock.Buy: return Loc.Money(it.price);
                case HutDecorUnlock.Shells: return Progress(it) + "/" + it.need + Loc.Tr(" Muscheln", " shells");
                case HutDecorUnlock.Pages: return Progress(it) + "/" + it.need + Loc.Tr(" Logbuchseiten", " logbook pages");
                case HutDecorUnlock.Bottles: return Progress(it) + "/" + it.need + Loc.Tr(" Flaschenpost", " bottle post");
                case HutDecorUnlock.Achievement:
                    var a = AchievementDatabase.Get(it.achievement);
                    return a != null ? a.Name : it.achievement;
            }
            return "";
        }

        // ------------------------------------------------------------------ mutations (routed through the host)
        public static bool SetWall(int i) => Set("wall", i, Walls.Length);
        public static bool SetFloor(int i) => Set("floor", i, Floors.Length);
        public static bool SetRug(int i) => Set("rug", i, Rugs.Length);
        public static bool SetCurtains(int i) => Set("curtains", i, Curtains.Length);

        static bool Set(string op, int i, int n)
        {
            var h = State;
            if (h == null || GameManager.I == null || !GameManager.I.InGame || i < 0 || i >= n) return false;
            if (Get(h, op) == i) return true;
            if (Net.IsClient) Net.Command(NetCommand.Custom, CoreCommands.HutKey, op, i.ToString(Inv));
            Apply(h, op, i);
            AfterChange();
            return true;
        }

        static int Get(HutDecorState h, string op)
        {
            switch (op)
            {
                case "wall": return h.wall;
                case "floor": return h.floor;
                case "rug": return h.rug;
                default: return h.curtains;
            }
        }

        static void Apply(HutDecorState h, string op, int i)
        {
            switch (op)
            {
                case "wall": h.wall = i; break;
                case "floor": h.floor = i; break;
                case "rug": h.rug = i; break;
                default: h.curtains = i; break;
            }
        }

        /// Shows / hides a decor item. Locked (not bought / not unlocked) items cannot be placed → false.
        public static bool SetPlaced(string id, bool on)
        {
            var h = State;
            var it = Item(id);
            if (h == null || it == null || GameManager.I == null || !GameManager.I.InGame) return false;
            if (on && !IsAvailable(id)) return false;
            if (h.placed.Contains(id) == on) return true;
            if (Net.IsClient) Net.Command(NetCommand.Custom, CoreCommands.HutKey, on ? "place" : "remove", id);
            if (on) h.placed.Add(id); else h.placed.Remove(id);
            AfterChange();
            return true;
        }

        public static bool Toggle(string id) => SetPlaced(id, !IsPlaced(id));

        /// Buys a decor item from the crew money (shared) and puts it up. False when it is not for sale, already owned
        /// or the money is short. Clients: the host spends + adds it (atomic), the client shows it optimistically.
        public static bool Buy(string id)
        {
            var h = State;
            var it = Item(id);
            var gm = GameManager.I;
            if (h == null || it == null || gm == null || !gm.InGame || it.unlock != HutDecorUnlock.Buy) return false;
            if (h.owned.Contains(id)) return SetPlaced(id, true);
            if (gm.State.money < it.price)
            {
                GameEvents.RaiseNotify(NotifyKind.Warning, Loc.Tr("Zu wenig Geld", "Not enough money"), Loc.Money(it.price));
                return false;
            }
            if (Net.IsClient)
            {
                Net.Command(NetCommand.Custom, CoreCommands.HutKey, "buy", id);
                gm.State.money -= it.price;              // optimistic until the host's snapshot
                GameEvents.RaiseMoneyChanged(gm.State.money);
                h.owned.Add(id);
                if (!h.placed.Contains(id)) h.placed.Add(id);
                Changed?.Invoke();
            }
            else if (!HostBuy(id)) return false;
            GameEvents.RaiseNotify(NotifyKind.Info, it.Name, "−" + Loc.Money(it.price));
            return true;
        }

        static bool HostBuy(string id)
        {
            var h = State;
            var it = Item(id);
            var gm = GameManager.I;
            if (h == null || it == null || gm == null || it.unlock != HutDecorUnlock.Buy) return false;
            if (h.owned.Contains(id)) { if (!h.placed.Contains(id)) { h.placed.Add(id); AfterChange(); } return true; }
            if (!gm.TrySpend(it.price)) return false;
            h.owned.Add(id);
            if (!h.placed.Contains(id)) h.placed.Add(id);
            AfterChange();
            gm.Save();
            return true;
        }

        static void AfterChange()
        {
            Changed?.Invoke();
            if (Net.IsHost) NetState.ForceSnapshot();
        }

        /// Host side of CoreCommands.HutKey (a guest's change). Re-validated against the host's state.
        public static bool Execute(string op, string value)
        {
            if (!Net.HasAuthority || string.IsNullOrEmpty(op)) return false;
            var h = State;
            if (h == null) return false;
            switch (op)
            {
                case "wall":
                case "floor":
                case "rug":
                case "curtains":
                {
                    if (!int.TryParse(value, NumberStyles.Integer, Inv, out int i)) return false;
                    int n = op == "wall" ? Walls.Length : op == "floor" ? Floors.Length : op == "rug" ? Rugs.Length : Curtains.Length;
                    if (i < 0 || i >= n) return false;
                    Apply(h, op, i);
                    AfterChange();
                    return true;
                }
                case "place":
                    if (Item(value) == null || !IsAvailable(value)) return false;
                    if (!h.placed.Contains(value)) { h.placed.Add(value); AfterChange(); }
                    return true;
                case "remove":
                    if (h.placed.Remove(value)) AfterChange();
                    return true;
                case "buy":
                    return HostBuy(value);
            }
            return false;
        }

        /// Debug / tests: copies the furnishing (no routing).
        public static HutDecorState Copy(HutDecorState h)
        {
            if (h == null) return Default();
            return new HutDecorState
            {
                wall = h.wall, floor = h.floor, rug = h.rug, curtains = h.curtains,
                owned = new List<string>(h.owned ?? new List<string>()),
                placed = new List<string>(h.placed ?? new List<string>()),
            };
        }

        /// Debug / tests: replaces the local furnishing without routing (the host's snapshot wins on clients).
        public static void DebugReplace(HutDecorState h)
        {
            var s = S;
            if (s == null) return;
            s.hut = Copy(h);
            Normalize(s.hut);
            AfterChange();
        }
    }
}
