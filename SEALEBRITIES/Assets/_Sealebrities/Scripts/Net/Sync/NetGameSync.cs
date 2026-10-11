using UnityEngine;

namespace Sealebrities
{
    /// Round-2 co-op systems on top of avatars / boat / creatures / voice (Design_Runde2 F, J.3, J.3b, K, L):
    /// crew looks + carried items + guest records (NetCrew), the sleep vote (NetSleepSync), achievements for everyone
    /// present (NetAchievementSync), animal events (NetFaunaEvents), rescue + tow (NetRescue) and Dialogue.Say lines +
    /// opt-in NPC poses (NetNpcSync). Shared GameState lists (deals, side quests, finds, challenges, story flags,
    /// village, pantry, delivery mishaps) travel in NetState's snapshot.
    /// Owned elsewhere (not registered here): the KRILL.tv livestream (Livestream/StreamNet.cs, ids 90–92) and the
    /// story NPCs of the npc module (Npc/NpcNet.cs, ids 176–178).
    /// Also keeps the host's save fresh for its guests (F.3): a guest's pushed record is saved within a minute, and
    /// the save is written when a guest leaves.
    [DefaultExecutionOrder(150)]
    public sealed class NetGameSync : MonoBehaviour
    {
        /// F.3: the host saves guest records at most this often (plus once whenever a guest leaves).
        public const float RecordSaveInterval = 60f;

        float rescueTimer, saveTimer;
        bool recordsDirty, saveSoon, installed;

        void OnEnable()
        {
            NetCrew.Install();
            NetSleepSync.Install();
            NetAchievementSync.Install();
            NetFaunaEvents.Install();
            NetRescue.Install();
            NetNpcSync.Install();
            Net.PlayerJoined += OnPlayerJoined;
            Net.PlayerLeft += OnPlayerLeft;
            Net.SessionEnded += OnSessionEnded;
            NetSession.Started += OnSessionStarted;
            NetCommands.Executed += OnCommandExecuted;
            installed = true;
        }

        void OnDisable()
        {
            if (!installed) return;
            installed = false;
            NetCrew.Uninstall();
            NetSleepSync.Uninstall();
            NetAchievementSync.Uninstall();
            NetFaunaEvents.Uninstall();
            NetRescue.Uninstall();
            NetNpcSync.Uninstall();
            Net.PlayerJoined -= OnPlayerJoined;
            Net.PlayerLeft -= OnPlayerLeft;
            Net.SessionEnded -= OnSessionEnded;
            NetSession.Started -= OnSessionStarted;
            NetCommands.Executed -= OnCommandExecuted;
        }

        // ------------------------------------------------------------------ session events
        void OnSessionStarted()
        {
            NetCrew.OnSessionStarted();
            NetAchievementSync.Reset();
            NetSleepSync.Reset();
            recordsDirty = false;
            saveSoon = false;
            saveTimer = RecordSaveInterval;
        }

        void OnSessionEnded(string reason)
        {
            NetRescue.Reset();
            NetCrew.Reset();
            NetSleepSync.Reset();
            NetFaunaEvents.Reset();
            NetNpcSync.Reset();
            NetAchievementSync.Reset();
            recordsDirty = false;
            saveSoon = false;
        }

        void OnPlayerJoined(NetPlayerInfo p)
        {
            NetCrew.OnPlayerJoined(p);
            NetSleepSync.OnPlayerJoined(p);
            NetFaunaEvents.OnPlayerJoined(p);
            NetRescue.OnPlayerJoined(p);
        }

        void OnPlayerLeft(NetPlayerInfo p)
        {
            NetCrew.OnPlayerLeft(p);
            NetSleepSync.OnPlayerLeft(p);
            NetRescue.OnPlayerLeft(p);
            // F.3: the guest's last record arrived before its goodbye – write the host save now
            if (Net.IsHost) saveSoon = true;
        }

        void OnCommandExecuted(int from, NetCommand cmd, string[] args)
        {
            if (cmd == NetCommand.Custom && args != null && args.Length > 0 && args[0] == CoreCommands.PlayerRecordKey) recordsDirty = true;
        }

        // ------------------------------------------------------------------ frame
        void Update()
        {
            if (!Net.IsOnline) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.25f);
            NetCrew.Tick(dt);
            NetSleepSync.Tick(dt);
            NetAchievementSync.Tick();
            NetFaunaEvents.Tick(dt);
            NetNpcSync.Tick(dt);
            rescueTimer -= dt;
            if (rescueTimer <= 0f)
            {
                rescueTimer = 1f;
                NetRescue.Tick();
            }
            if (Net.IsHost) TickHostSave(dt);
        }

        void TickHostSave(float dt)
        {
            saveTimer -= dt;
            bool due = saveSoon || (recordsDirty && saveTimer <= 0f);
            if (!due) return;
            var gm = GameManager.I;
            if (gm == null || !gm.InGame) return;
            saveSoon = false;
            recordsDirty = false;
            saveTimer = RecordSaveInterval;
            if (GameManager.SavingDisabled) return;
            gm.Save();
        }
    }
}
