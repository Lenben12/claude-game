using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// F.3 guests keep everything: per-player data in the HOST save, keyed by Settings.PlayerKey (random per machine,
    /// never the Steam id, never logged).
    ///
    /// Net module flow:
    /// * Hello carries Settings.PlayerKey → the host stores it in NetPlayerInfo.key.
    /// * Welcome: host sends JsonUtility.ToJson(PlayerRecords.ForGuest(key, name)) → guest calls ApplyToLocal(record).
    /// * Guests send SendLocal() every 60 s and on leaving (CoreCommands.PlayerRecordKey); the host stores it (Store).
    /// The host's own record is refreshed on every save (GameManager.Save → CaptureLocal + Store).
    /// The guest's coop_player.json stays as offline fallback.
    public static class PlayerRecords
    {
        /// Guests push their record this often (seconds).
        public const float SendInterval = 60f;

        static GameState S => GameManager.I != null ? GameManager.I.State : null;
        static float sendTimer;

        /// The LOCAL player's record (stats, achievements, carried). Created on demand; per player (CopyPerPlayerFrom).
        public static PlayerRecord Local
        {
            get
            {
                var s = S;
                if (s == null) return null;
                if (s.localRecord == null) s.localRecord = new PlayerRecord { key = Settings.PlayerKey, name = Settings.DisplayName };
                if (s.localRecord.equipped == null) s.localRecord.equipped = new List<string>();
                if (s.localRecord.achievements == null) s.localRecord.achievements = new List<string>();
                return s.localRecord;
            }
        }

        /// Host: the stored record of a player key (null = unknown).
        public static PlayerRecord Find(string key)
        {
            var s = S;
            if (s == null || string.IsNullOrEmpty(key)) return null;
            for (int i = 0; i < s.players.Count; i++) if (s.players[i] != null && s.players[i].key == key) return s.players[i];
            return null;
        }

        /// Host: record for a joining guest (a fresh one with the starter hotbar when the key is new).
        public static PlayerRecord ForGuest(string key, string name)
        {
            var r = Find(key);
            if (r != null) return r;
            r = new PlayerRecord
            {
                key = key, name = name,
                hotbarRight = new[] { "Camera", "FishingRod", "None" }, hotbarLeft = new[] { "Flashlight", "None", "None" },
            };
            if (S != null && Net.HasAuthority) S.players.Add(r);
            return r;
        }

        /// Host: stores (adds or replaces by key) a record.
        public static void Store(PlayerRecord r)
        {
            var s = S;
            if (s == null || r == null || string.IsNullOrEmpty(r.key) || !Net.HasAuthority) return;
            if (r.equipped == null) r.equipped = new List<string>();
            if (r.achievements == null) r.achievements = new List<string>();
            r.lastSeenDay = s.day;
            for (int i = 0; i < s.players.Count; i++)
            {
                if (s.players[i] == null || s.players[i].key != r.key) continue;
                // achievements unlocked for this player while it was away stay
                var old = s.players[i].achievements;
                if (old != null) for (int k = 0; k < old.Count; k++) if (!r.achievements.Contains(old[k])) r.achievements.Add(old[k]);
                s.players[i] = r;
                return;
            }
            s.players.Add(r);
        }

        /// Host side of PlayerRecordKey: a guest's record (the key must match the sender's NetPlayerInfo.key when known).
        public static bool StoreFromClient(int fromId, string json)
        {
            if (string.IsNullOrEmpty(json) || !Net.HasAuthority) return false;
            PlayerRecord r;
            try { r = JsonUtility.FromJson<PlayerRecord>(json); }
            catch (Exception) { return false; }
            if (r == null || string.IsNullOrEmpty(r.key)) return false;
            var p = Net.Find(fromId);
            if (p != null && !string.IsNullOrEmpty(p.key) && p.key != r.key) return false;
            if (p != null && string.IsNullOrEmpty(p.key)) p.key = r.key;
            if (r.name != null && r.name.Length > Settings.MaxNameLength) r.name = r.name.Substring(0, Settings.MaxNameLength);
            Store(r);
            return true;
        }

        /// Fills the local record from the current per-player state (hotbar, equipment choice, look, name, carried).
        public static PlayerRecord CaptureLocal()
        {
            var s = S;
            var r = Local;
            if (s == null || r == null) return r;
            r.key = Settings.PlayerKey;
            r.name = Settings.DisplayName;
            r.look = Settings.Look;
            r.hotbarRight = (string[])s.hotbarRight.Clone();
            r.hotbarLeft = (string[])s.hotbarLeft.Clone();
            r.equipped = new List<string>(s.equipped);
            r.carried = s.carried;
            r.lastSeenDay = s.day;
            return r;
        }

        /// Guest: applies the record the host kept for this machine (hotbar, equipment choice, stats, achievements).
        public static void ApplyToLocal(PlayerRecord r)
        {
            var s = S;
            if (s == null || r == null) return;
            if (r.hotbarRight != null && r.hotbarRight.Length == Hands.SlotsPerHand) s.hotbarRight = (string[])r.hotbarRight.Clone();
            if (r.hotbarLeft != null && r.hotbarLeft.Length == Hands.SlotsPerHand) s.hotbarLeft = (string[])r.hotbarLeft.Clone();
            if (r.equipped != null) s.equipped = new List<string>(r.equipped);
            s.EnsureHotbar();
            var local = Local;
            local.fishCaught = r.fishCaught; local.speciesFilmed = r.speciesFilmed; local.rescues = r.rescues;
            local.dives = r.dives; local.faints = r.faints; local.playMinutes = r.playMinutes;
            local.coconuts = r.coconuts; local.grilled = r.grilled; local.snaps = r.snaps; local.photos = r.photos;
            if (r.achievements != null)
                for (int i = 0; i < r.achievements.Count; i++) Achievements.ApplyLocal(r.achievements[i]);
            Hands.NotifyStateReplaced();
            GameEvents.RaiseGearChanged();
        }

        /// Guest: sends the own record to the host.
        public static void SendLocal()
        {
            if (!Net.IsClient || S == null) return;
            var r = CaptureLocal();
            if (r == null) return;
            Net.Command(NetCommand.Custom, CoreCommands.PlayerRecordKey, JsonUtility.ToJson(r));
        }

        /// GameManager.Update: play time + the 60 s guest push.
        public static void Tick(float dt)
        {
            var gm = GameManager.I;
            if (gm == null || !gm.InGame) return;
            var r = Local;
            if (r != null) r.playMinutes += dt / 60f;
            if (!Net.IsClient) { sendTimer = 0f; return; }
            sendTimer += dt;
            if (sendTimer < SendInterval) return;
            sendTimer = 0f;
            SendLocal();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { sendTimer = 0f; }
    }
}
