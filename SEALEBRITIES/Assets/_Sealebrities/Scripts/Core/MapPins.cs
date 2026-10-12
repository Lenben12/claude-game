using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Sealebrities
{
    /// Round 3 (GDD §20 Q31 „Geteilte Koop-Pins“): one marker on the treasure map. Lives in the SHARED GameState (host save,
    /// snapshot) – everybody in the session sees every pin on the map and as a small marker in the HUD.
    [Serializable]
    public class MapPin
    {
        /// Host-assigned id (> 0). A guest's optimistic pin has a negative id until the host's snapshot replaces it.
        public int id;
        public float x, z;
        /// MapPins.Icon* (flag, star, fish, danger, treasure, camera).
        public int icon;
        /// Net id of the player who set it (0 = host / offline).
        public int owner;
        public int day;
    }

    /// Shared co-op map pins. Mutations go through the host (CoreCommands.PinKey: "add", x, z, icon | "remove", id |
    /// "clear"); guests apply them optimistically, the host's snapshot is the truth. Max Max pins (the oldest go first).
    public static class MapPins
    {
        public const int Max = 24;
        public const int IconFlag = 0, IconStar = 1, IconFish = 2, IconDanger = 3, IconTreasure = 4, IconCamera = 5;
        public const int IconCount = 6;

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static int tempId = -1;

        /// Raised on this machine after a local change (snapshots replace the list silently – poll Signature()).
        public static event Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Changed = null; tempId = -1; }

        static List<MapPin> List
        {
            get
            {
                var gm = GameManager.I;
                if (gm == null || gm.State == null) return null;
                if (gm.State.mapPins == null) gm.State.mapPins = new List<MapPin>();
                return gm.State.mapPins;
            }
        }

        /// Current pins (empty list outside a game). Do not modify.
        public static List<MapPin> All => List ?? Empty;
        static readonly List<MapPin> Empty = new List<MapPin>();

        public static int Count => List != null ? List.Count : 0;

        /// Cheap allocation-free signature of the pin list (UI polls it to notice snapshot changes).
        public static int Signature()
        {
            var l = List;
            if (l == null) return 0;
            unchecked
            {
                int h = l.Count * 7919;
                for (int i = 0; i < l.Count; i++)
                {
                    var p = l[i];
                    if (p == null) continue;
                    h = h * 31 + p.id;
                    h = h * 31 + Mathf.RoundToInt(p.x) * 3 + Mathf.RoundToInt(p.z) * 7 + p.icon;
                }
                return h;
            }
        }

        public static MapPin Find(int id)
        {
            var l = List;
            if (l == null) return null;
            for (int i = 0; i < l.Count; i++) if (l[i] != null && l[i].id == id) return l[i];
            return null;
        }

        /// Places a pin at a world position (x/z) for everybody. Returns the (optimistic) pin or null.
        public static MapPin Add(Vector3 world, int icon)
        {
            var l = List;
            var gm = GameManager.I;
            if (l == null || gm == null || !gm.InGame) return null;
            icon = Mathf.Clamp(icon, 0, IconCount - 1);
            float x = Mathf.Clamp(world.x, -WorldLayout.TerrainSize * 0.5f, WorldLayout.TerrainSize * 0.5f);
            float z = Mathf.Clamp(world.z, -WorldLayout.TerrainSize * 0.5f, WorldLayout.TerrainSize * 0.5f);
            if (Net.IsClient)
            {
                Net.Command(NetCommand.Custom, CoreCommands.PinKey, "add", x.ToString("0.0", Inv), z.ToString("0.0", Inv), icon.ToString(Inv));
                var p = new MapPin { id = tempId--, x = x, z = z, icon = icon, owner = Net.LocalId, day = gm.State.day };
                Insert(l, p);
                Changed?.Invoke();
                return p;
            }
            var pin = HostAdd(x, z, icon, Net.LocalId);
            return pin;
        }

        /// Removes a pin for everybody (anyone may remove any pin – it is a shared crew map).
        public static bool Remove(int id)
        {
            var l = List;
            if (l == null) return false;
            int idx = -1;
            for (int i = 0; i < l.Count; i++) if (l[i] != null && l[i].id == id) { idx = i; break; }
            if (idx < 0) return false;
            if (Net.IsClient)
            {
                if (id > 0) Net.Command(NetCommand.Custom, CoreCommands.PinKey, "remove", id.ToString(Inv));
                l.RemoveAt(idx);
                Changed?.Invoke();
                return true;
            }
            l.RemoveAt(idx);
            AfterChange();
            return true;
        }

        /// Removes every pin (tests / a future "clear all" button).
        public static void Clear()
        {
            var l = List;
            if (l == null || l.Count == 0) return;
            if (Net.IsClient) Net.Command(NetCommand.Custom, CoreCommands.PinKey, "clear");
            l.Clear();
            if (Net.IsClient) Changed?.Invoke(); else AfterChange();
        }

        static MapPin HostAdd(float x, float z, int icon, int owner)
        {
            var l = List;
            var gm = GameManager.I;
            if (l == null || gm == null) return null;
            int next = 1;
            for (int i = 0; i < l.Count; i++) if (l[i] != null && l[i].id >= next) next = l[i].id + 1;
            var p = new MapPin { id = next, x = x, z = z, icon = Mathf.Clamp(icon, 0, IconCount - 1), owner = owner, day = gm.State.day };
            Insert(l, p);
            AfterChange();
            return p;
        }

        static void Insert(List<MapPin> l, MapPin p)
        {
            l.Add(p);
            while (l.Count > Max) l.RemoveAt(0);   // the oldest pin goes first
        }

        static void AfterChange()
        {
            Changed?.Invoke();
            if (Net.IsHost) NetState.ForceSnapshot();
        }

        /// Host side of CoreCommands.PinKey (a guest's change). Re-validated here.
        public static bool Execute(int fromId, string op, string a, string b, string c)
        {
            if (!Net.HasAuthority || string.IsNullOrEmpty(op)) return false;
            var l = List;
            if (l == null) return false;
            switch (op)
            {
                case "add":
                {
                    if (!float.TryParse(a, NumberStyles.Float, Inv, out float x) || !float.TryParse(b, NumberStyles.Float, Inv, out float z)) return false;
                    if (float.IsNaN(x) || float.IsNaN(z) || float.IsInfinity(x) || float.IsInfinity(z)) return false;
                    int.TryParse(c, NumberStyles.Integer, Inv, out int icon);
                    float half = WorldLayout.TerrainSize * 0.5f;
                    return HostAdd(Mathf.Clamp(x, -half, half), Mathf.Clamp(z, -half, half), icon, fromId) != null;
                }
                case "remove":
                {
                    if (!int.TryParse(a, NumberStyles.Integer, Inv, out int id)) return false;
                    for (int i = 0; i < l.Count; i++)
                        if (l[i] != null && l[i].id == id) { l.RemoveAt(i); AfterChange(); return true; }
                    return true;
                }
                case "clear":
                    l.Clear();
                    AfterChange();
                    return true;
            }
            return false;
        }

        /// Repairs a loaded / synced list (null entries, NaN positions, out-of-range icons, too many pins).
        public static void Normalize(List<MapPin> l)
        {
            if (l == null) return;
            float half = WorldLayout.TerrainSize * 0.5f;
            for (int i = l.Count - 1; i >= 0; i--)
            {
                var p = l[i];
                if (p == null || float.IsNaN(p.x) || float.IsNaN(p.z) || float.IsInfinity(p.x) || float.IsInfinity(p.z)) { l.RemoveAt(i); continue; }
                p.x = Mathf.Clamp(p.x, -half, half);
                p.z = Mathf.Clamp(p.z, -half, half);
                p.icon = Mathf.Clamp(p.icon, 0, IconCount - 1);
            }
            while (l.Count > Max) l.RemoveAt(0);
        }

        // ------------------------------------------------------------------ icon look (map, HUD)
        public static UIGlyph.DrawFn Glyph(int icon)
        {
            switch (icon)
            {
                case IconStar: return UIGlyphs.Star;
                case IconFish: return UIGlyphs.Fish;
                case IconDanger: return UIGlyphs.SharkFin;
                case IconTreasure: return UIGlyphs.Chest;
                case IconCamera: return UIGlyphs.Camera;
                default: return UIGlyphs.Flag;
            }
        }

        public static Color Color(int icon)
        {
            switch (icon)
            {
                case IconStar: return UIPalette.Rgb(0xE8B43A);
                case IconFish: return UIPalette.Rgb(0x2FA8B5);
                case IconDanger: return UIPalette.Rgb(0xD9483B);
                case IconTreasure: return UIPalette.Rgb(0xB0782E);
                case IconCamera: return UIPalette.Rgb(0x8E6CD9);
                default: return UIPalette.Rgb(0xE8603C);
            }
        }

        public static string Name(int icon)
        {
            switch (icon)
            {
                case IconStar: return Loc.Tr("Stern", "Star");
                case IconFish: return Loc.Tr("Fische", "Fish");
                case IconDanger: return Loc.Tr("Gefahr", "Danger");
                case IconTreasure: return Loc.Tr("Schatz", "Treasure");
                case IconCamera: return Loc.Tr("Filmspot", "Film spot");
                default: return Loc.Tr("Markierung", "Marker");
            }
        }
    }

    /// Round 3 (GDD §20 Q30 „Orte erst nach Entdeckung“): places on the treasure map appear once somebody of the crew was
    /// there (shared story flag "map:&lt;id&gt;", set through StoryFlags – guests route it to the host). The hut, the dock and the
    /// helipad are known from the start; the hermit camp uses StoryKeys.HermitCampFound; the volcano is charted with a
    /// lock until VolcanoGate opens. Ticked by the UI (UIMapOverlay.Tick) twice per second.
    public static class MapDiscovery
    {
        public struct Place
        {
            public string id, de, en;
            /// Prefix + id (cached – polled often).
            public string flag;
            public Vector3 pos;
            public float radius;
            public UIGlyph.DrawFn icon;
            public Color color;
            /// Always on the map (start places).
            public bool known;
        }

        public const string Prefix = "map:";
        static Place[] places;
        static float timer;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { places = null; timer = 0f; }

        static Place P(string id, string de, string en, Vector3 pos, float r, UIGlyph.DrawFn icon, Color c, bool known = false)
            => new Place { id = id, flag = Prefix + id, de = de, en = en, pos = pos, radius = r, icon = icon, color = c, known = known };

        public static Place[] Places
        {
            get
            {
                if (places != null) return places;
                var ink = UIPalette.Rgb(0x4B3423);
                places = new[]
                {
                    P("hut", "Hütte", "Hut", WorldLayout.Hut, 20f, UIGlyphs.House, UIPalette.Rgb(0xC9822B), true),
                    P("dock", "Steg", "Dock", (WorldLayout.DockStart + WorldLayout.DockEnd) * 0.5f, 20f, UIGlyphs.Boat, ink, true),
                    P("helipad", "Heli-Platz", "Helipad", WorldLayout.Helipad, 22f, UIGlyphs.Heli, ink, true),
                    P("lighthouse", "Leuchtturm", "Lighthouse", WorldLayout.Lighthouse, 26f, UIGlyphs.Star, UIPalette.Rgb(0xC0473A)),
                    P("waterfall", "Wasserfall", "Waterfall", WorldLayout.WaterfallPool, 24f, UIGlyphs.Wave, UIPalette.Rgb(0x3C8FA0)),
                    P("cave", "Grotte", "Grotto", WorldLayout.CaveEntrance, 14f, UIGlyphs.Eye, ink),
                    P("tidepools", "Gezeitenbecken", "Tide pools", WorldLayout.TidePools, 24f, UIGlyphs.Shell, UIPalette.Rgb(0xB0782E)),
                    P("radiotower", "Funkturm-Ruine", "Radio tower ruins", WorldLayout.RadioTowerRuins, 24f, UIGlyphs.Broadcast, ink),
                    P("southbeach", "Südstrand", "South beach", WorldLayout.Village, 36f, UIGlyphs.Wave, UIPalette.Rgb(0x3C8FA0)),
                    P("driftwood", "Treibholzstrand", "Driftwood beach", WorldLayout.DriftwoodBeach, 28f, UIGlyphs.Bottle, UIPalette.Rgb(0x8A6A3E)),
                    P("hiddenbay", "Versteckte Bucht", "Hidden bay", WorldLayout.HiddenBay, 28f, UIGlyphs.Question, UIPalette.Rgb(0x3C8FA0)),
                    P("raft", "Badeinsel", "Swim raft", WorldLayout.SwimRaft, 18f, UIGlyphs.Sun, UIPalette.Rgb(0xC9822B)),
                    P("horst", "Horsts Ecke", "Horst's corner", new Vector3(93f, 0f, -22f), 18f, UIGlyphs.Bed, ink),
                };
                return places;
            }
        }

        public static bool IsKnown(in Place p)
        {
            if (p.known) return true;
            return StoryFlags.Has(p.flag ?? Prefix + p.id);
        }

        /// The hermit camp counts as discovered once somebody stood in it (step 3's flag).
        public static bool CampKnown => StoryFlags.Has(StoryKeys.HermitCampFound);

        /// Twice per second: marks every place the local player (or the boat they ride) is close to.
        public static void Tick(float dt)
        {
            timer -= dt;
            if (timer > 0f) return;
            timer = 0.5f;
            var gm = GameManager.I;
            if (gm == null || !gm.InGame || gm.State == null) return;
            var p = Refs.Player;
            if (p == null || (p is UnityEngine.Object o && o == null) || p.Root == null) return;
            Vector3 at = p.Root.position;
            var list = Places;
            for (int i = 0; i < list.Length; i++)
            {
                var pl = list[i];
                if (pl.known) continue;
                float dx = at.x - pl.pos.x, dz = at.z - pl.pos.z;
                if (dx * dx + dz * dz > pl.radius * pl.radius) continue;
                string flag = pl.flag;
                if (!StoryFlags.Has(flag))
                {
                    StoryFlags.Set(flag);
                    GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr("Auf der Karte: ", "On the map: ") + Loc.Tr(pl.de, pl.en), null);
                }
            }
        }
    }
}
