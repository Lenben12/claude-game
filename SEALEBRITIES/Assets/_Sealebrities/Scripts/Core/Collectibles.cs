using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    public enum CollectibleKind
    {
        LogbookPage,    // Onkel Gus' lost logbook pages (story flavour)
        GoldenShell,    // shiny shells hidden on islands and in shallow water
        BottleMessage,  // message in a bottle (clue to the lost deep-sea documentary / TV studio)
        Treasure,       // chests, old cameras, odd finds
        Secret,         // hidden places (counted when discovered)
        MapPiece        // round 3: buried / hidden treasure-map pieces (MapPieces) – APPEND new kinds after this
    }

    /// Optional exploration collectibles. Not needed for the story; tracked for the phone and achievements.
    /// The props module places them and registers totals; picking one up calls Collect.
    public static class Collectibles
    {
        static readonly Dictionary<CollectibleKind, int> Totals = new Dictionary<CollectibleKind, int>();

        public static event Action<string, CollectibleKind> Found;

        static GameState S => GameManager.I.State;

        /// Highest valid kind (network clamps).
        public const CollectibleKind LastKind = CollectibleKind.MapPiece;

        /// Called by the placing module once per kind after the world is built.
        public static void RegisterTotal(CollectibleKind kind, int total) => Totals[kind] = total;

        public static int Total(CollectibleKind kind) => Totals.TryGetValue(kind, out var t) ? t : 0;

        public static int TotalAll
        {
            get { int n = 0; foreach (var kv in Totals) n += kv.Value; return n; }
        }

        public static bool IsCollected(string id) => GameManager.I != null && S.collected.Contains(id);

        public static int Count(CollectibleKind kind)
        {
            if (GameManager.I == null) return 0;
            string prefix = Prefix(kind);
            int n = 0;
            foreach (var id in S.collected) if (id.StartsWith(prefix, StringComparison.Ordinal)) n++;
            return n;
        }

        public static int CountAll => GameManager.I == null ? 0 : S.collected.Count;

        /// Ids must start with the kind prefix, e.g. Id(CollectibleKind.GoldenShell, "lighthouse_rock").
        public static string Id(CollectibleKind kind, string name) => Prefix(kind) + name;

        static string Prefix(CollectibleKind kind)
        {
            switch (kind)
            {
                case CollectibleKind.LogbookPage: return "log:";
                case CollectibleKind.GoldenShell: return "shell:";
                case CollectibleKind.BottleMessage: return "bottle:";
                case CollectibleKind.Treasure: return "treasure:";
                case CollectibleKind.MapPiece: return "map:";
                default: return "secret:";
            }
        }

        public static string KindName(CollectibleKind kind)
        {
            switch (kind)
            {
                case CollectibleKind.LogbookPage: return Loc.Tr("Logbuch-Seite", "Logbook page");
                case CollectibleKind.GoldenShell: return Loc.Tr("Goldene Muschel", "Golden shell");
                case CollectibleKind.BottleMessage: return Loc.Tr("Flaschenpost", "Message in a bottle");
                case CollectibleKind.Treasure: return Loc.Tr("Fundstück", "Treasure");
                case CollectibleKind.MapPiece: return Loc.Tr("Kartenstück", "Map piece");
                default: return Loc.Tr("Geheimnis", "Secret");
            }
        }

        /// Returns false if already collected.
        /// SHARED STATE: on co-op clients this sends NetCommand.Collect (id, kind) and applies it optimistically.
        public static bool Collect(string id, CollectibleKind kind, string displayText = null)
        {
            if (string.IsNullOrEmpty(id) || GameManager.I == null || !GameManager.I.InGame || S.collected.Contains(id)) return false;
            if (Net.IsClient) Net.Command(NetCommand.Collect, id, ((int)kind).ToString(System.Globalization.CultureInfo.InvariantCulture));
            S.collected.Add(id);
            Found?.Invoke(id, kind);
            string label = displayText ?? KindName(kind);
            int have = Count(kind), total = Total(kind);
            // L.10: icon + one short line + chip ("Logbuch-Seite · 7/24")
            GameEvents.RaiseNotify(NotifyKind.Collectible, label, total > 0 ? have + "/" + total : null);
            if (Net.HasAuthority) Cosmetics.CheckRules();
            GameManager.I.Save();
            return true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Totals.Clear(); Found = null; }
    }
}
