using System;
using UnityEngine;

namespace Sealebrities
{
    /// One map-piece spot: where it lies, whether it is buried (dig with the stick or the hands at a marked spot).
    public struct MapPieceSpot
    {
        public Vector3 position;
        public bool buried;
        public bool valid;
    }

    /// Round 3 (GDD §19 „Kartenstücke überall auf der Insel, teils vergraben. Erst mit allen Teilen öffnet sich die
    /// Karte zu einem Geheimnis“): Count pieces, shared like every collectible (Collectibles kind MapPiece, ids
    /// "map:p01" …, host authority through NetCommand.Collect). The finder also gets a "map_piece" into the pouch.
    /// When the last piece is found the shared flag StoryKeys.TreasureMap is set: the treasure map opens (UI hook
    /// OpenMapHandler) and points to SecretSpot.
    ///
    /// WORLD MODULE (step 3): placement API –
    ///  * the props build places DEFAULT placeholder pieces (PropSurvival.BuildMapPieces) unless UseDefaultPlacement is
    ///    false; to place them yourself set UseDefaultPlacement = false before IslandProps.Build and create one
    ///    PropMapPiece (lying) or PropDigSpot (buried) per index 0..Count-1 (PropSurvival.CreatePiece does both);
    ///  * set SecretSpot (+ SecretSpotValid) to the place the finished map points to and put the secret there (set
    ///    StoryKeys.TreasureFound when it is found).
    public static class MapPieces
    {
        public const int Count = 8;

        /// Where the pieces are (filled by whoever placed them – the HUD / map app may show the found ones).
        public static readonly MapPieceSpot[] Spots = new MapPieceSpot[Count];
        /// False = the world module places the pieces itself (see the class comment).
        public static bool UseDefaultPlacement = true;
        /// Where the treasure map's X is.
        public static Vector3 SecretSpot = new Vector3(-115f, 0f, -19f);
        public static bool SecretSpotValid;

        /// UI: shows the treasure map card (pieces found, the X once complete).
        public static Action OpenMapHandler;

        public static event Action Changed;

        public static string Id(int i) => Collectibles.Id(CollectibleKind.MapPiece, "p" + (Mathf.Clamp(i, 0, Count - 1) + 1).ToString("00"));

        public static int IndexOf(string id)
        {
            for (int i = 0; i < Count; i++) if (Id(i) == id) return i;
            return -1;
        }

        public static bool IsFound(int i) => i >= 0 && i < Count && Collectibles.IsCollected(Id(i));

        public static int Found => GameManager.I == null ? 0 : Collectibles.Count(CollectibleKind.MapPiece);

        public static bool Complete => Found >= Count || StoryFlags.Has(StoryKeys.TreasureMap);

        /// The LOCAL player found piece i (pickup or dig). Shared count + the finder's pouch copy; the last piece
        /// completes the treasure map for everybody. False when it was already found.
        public static bool Collect(int i)
        {
            if (i < 0 || i >= Count) return false;
            if (!Collectibles.Collect(Id(i), CollectibleKind.MapPiece, Loc.Tr("Kartenstück", "Map piece"))) return false;
            Backpack.Add(ItemIds.MapPiece, 1, false);
            if (Found >= Count && !StoryFlags.Has(StoryKeys.TreasureMap))
            {
                StoryFlags.Set(StoryKeys.TreasureMap);
                GameEvents.RaiseBanner(Loc.Tr("Die Schatzkarte ist komplett!", "The treasure map is complete!"), new Color(0.96f, 0.82f, 0.4f));
                if (SecretSpotValid) UIWaypoint.Set(SecretSpot, Loc.Tr("Schatz", "Treasure"), new Color(0.96f, 0.82f, 0.4f));
            }
            Changed?.Invoke();
            return true;
        }

        /// Opens the treasure map (backpack „Benutzen“ on a map piece / the map).
        public static void OpenMap()
        {
            if (OpenMapHandler != null) { OpenMapHandler(); return; }
            if (Complete)
            {
                if (SecretSpotValid) UIWaypoint.Set(SecretSpot, Loc.Tr("Schatz", "Treasure"), new Color(0.96f, 0.82f, 0.4f));
                GameEvents.RaiseNotify(NotifyKind.Story, Loc.Tr("Schatzkarte", "Treasure map"), Loc.Tr("X markiert", "X marked"));
            }
            else GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr("Kartenstücke", "Map pieces"), Found + "/" + Count);
        }

        /// Points the HUD waypoint at the secret (treasure map card button).
        public static bool MarkSecret()
        {
            if (!Complete) return false;
            UIWaypoint.Set(SecretSpot, Loc.Tr("Schatz", "Treasure"), new Color(0.96f, 0.82f, 0.4f));
            return true;
        }

        public static void RaiseChanged() => Changed?.Invoke();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            OpenMapHandler = null;
            Changed = null;
            UseDefaultPlacement = true;
            SecretSpotValid = false;
            SecretSpot = new Vector3(-115f, 0f, -19f);
            Array.Clear(Spots, 0, Spots.Length);
        }
    }
}
