using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    public enum NetMode { Offline, Host, Client }

    [Serializable]
    public class NetPlayerInfo
    {
        public int id;                  // 0 = host
        public string name;
        public Color suit = new Color(1f, 0.55f, 0.2f);
        public int hat;
        public int face;
        public bool ready;
        public int pingMs;
        public bool IsLocal => id == Net.LocalId;
    }

    /// Client → host requests that change the SHARED game state (one channel, one wallet, one journal).
    /// Arguments are plain strings (JsonUtility for objects). The Net module may append new values.
    public enum NetCommand : byte
    {
        BankClips,          // args: JSON list of FootageClip from the client's dive
        RegisterFilmed,     // speciesId, sizeCm, tier, clipValue
        Upload,             // titleStyle, title
        Buy,                // itemId, express
        OpenCrate,          // itemId
        Sleep,              // – one sleeps = everybody sleeps
        AddCatch,           // JSON FishItem
        ListFish,           // index in the fish inventory
        ListAllFish,        //
        Collect,            // collectible id, kind
        AddMoney,           // amount (e.g. rescue fee as negative)
        Faint,              // – the client fainted (rescue fee, footage already dropped locally)
        Custom              // free-form: first arg is a key
    }

    /// Multiplayer facade (co-op 1–4 players sharing one channel). Implemented by the Net module (Scripts/Net).
    /// Offline (Mode == Offline) everything behaves exactly like single player.
    /// Authority rule: shared state (GameState: money, followers, journal, banked footage, uploads, deliveries,
    /// fish inventory/listings, collectibles, owned items, day/time) is only changed where HasAuthority is true.
    /// Core mutation methods start with `if (Net.IsClient) { Net.Command(...); return ...; }`.
    public static class Net
    {
        public const int MaxPlayers = 4;
        public const int DefaultPort = 27015;
        public const int DiscoveryPort = 27016;
        public const int ProtocolVersion = 1;

        public static NetMode Mode;
        public static bool IsOnline => Mode != NetMode.Offline;
        public static bool IsHost => Mode == NetMode.Host;
        public static bool IsClient => Mode == NetMode.Client;
        /// True where shared game state may be changed directly (offline or host).
        public static bool HasAuthority => Mode != NetMode.Client;

        /// Own player id (0 = host or offline).
        public static int LocalId;
        public static string LocalName = "Diver";
        /// Everyone in the session including the local player (empty offline).
        public static readonly List<NetPlayerInfo> Players = new List<NetPlayerInfo>();
        public static int PlayerCount => IsOnline ? Mathf.Max(1, Players.Count) : 1;

        /// Set by the Net module. Client → host command transport.
        public static Action<NetCommand, string[]> CommandHandler;
        /// Set by the Net module. Opens the "Mit Freunden" screen (host / join / LAN list). Called by the main menu.
        public static Action OpenLobby;
        /// Set by the Net module. Leaves the session (client) or closes it (host).
        public static Action Leave;

        public static event Action<NetPlayerInfo> PlayerJoined;
        public static event Action<NetPlayerInfo> PlayerLeft;
        /// Raised when the connection is lost or the session is closed (reason is localized).
        public static event Action<string> SessionEnded;
        /// Raised on clients after a shared-state snapshot from the host was applied.
        public static event Action StateSynced;

        public static void Command(NetCommand cmd, params string[] args)
        {
            if (CommandHandler != null) CommandHandler(cmd, args);
            else Debug.LogWarning("[Net] No command handler for " + cmd);
        }

        public static NetPlayerInfo Find(int id)
        {
            for (int i = 0; i < Players.Count; i++) if (Players[i].id == id) return Players[i];
            return null;
        }

        public static void RaisePlayerJoined(NetPlayerInfo p) => PlayerJoined?.Invoke(p);
        public static void RaisePlayerLeft(NetPlayerInfo p) => PlayerLeft?.Invoke(p);
        public static void RaiseSessionEnded(string reason) => SessionEnded?.Invoke(reason);
        public static void RaiseStateSynced() => StateSynced?.Invoke();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Mode = NetMode.Offline; LocalId = 0; LocalName = "Diver"; Players.Clear();
            CommandHandler = null; OpenLobby = null; Leave = null;
            PlayerJoined = null; PlayerLeft = null; SessionEnded = null; StateSynced = null;
        }
    }

    /// Message bus between the Net module's transport (net-core) and the replication features (net-sync).
    /// A message is (type byte, payload bytes). Type ranges: 0–63 session/state (net-core), 64–127 avatars/boat/
    /// creatures/world (net-sync), 128–159 voice, 160+ free. Handlers run on the Unity main thread.
    public static class NetBus
    {
        public const int Everyone = -1;

        public delegate void Handler(int fromId, byte[] payload);

        static readonly Handler[] Handlers = new Handler[256];

        /// Set by the transport. (type, payload, targetId) – a client always sends to the host (targetId ignored);
        /// the host sends to targetId or to Everyone (all clients). `reliable` false = may be dropped/merged (state streams).
        public static Action<byte, byte[], int, bool> SendImpl;
        /// Set by the transport. Host only: send to every client except one (used to relay a client's stream).
        public static Action<byte, byte[], int, bool> SendExceptImpl;

        public static void Register(byte type, Handler h) => Handlers[type] = h;
        public static void Unregister(byte type) => Handlers[type] = null;

        public static void Send(byte type, byte[] payload, int targetId = Everyone, bool reliable = true)
        {
            if (Net.IsOnline) SendImpl?.Invoke(type, payload, targetId, reliable);
        }

        public static void SendExcept(byte type, byte[] payload, int exceptId, bool reliable = true)
        {
            if (Net.IsHost) SendExceptImpl?.Invoke(type, payload, exceptId, reliable);
        }

        /// Called by the transport on the main thread for every received message.
        public static void Dispatch(byte type, int fromId, byte[] payload)
        {
            var h = Handlers[type];
            if (h == null) return;
            try { h(fromId, payload); }
            catch (Exception e) { Debug.LogException(e); }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Array.Clear(Handlers, 0, Handlers.Length); SendImpl = null; SendExceptImpl = null; }
    }

    /// Replication switches set by the Net module and honored by the simulating modules.
    public static class NetFlags
    {
        /// Another machine drives the boat: the Boat module skips its physics + driver input and lets the Net module
        /// set IBoat.Root position/rotation. The driver seat must refuse interaction while BoatDriverId belongs to someone else.
        public static bool BoatRemote;
        /// Net id of the player at the wheel (-1 = nobody).
        public static int BoatDriverId = -1;
        /// Clients: creatures are spawned/simulated by the host and mirrored as proxies; the Fauna module must not
        /// spawn real (non-hallucination) creatures locally.
        public static bool CreaturesRemote;

        public static bool SeatTakenByOther => BoatDriverId >= 0 && BoatDriverId != Net.LocalId;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { BoatRemote = false; BoatDriverId = -1; CreaturesRemote = false; }
    }

    /// Appearance of a bean diver (menu crew, intro, co-op avatars).
    [Serializable]
    public struct DiverLook
    {
        public Color suit;
        public Color cuff;
        public int hat;        // 0 none, 1 bucket, 2 viking, 3 shark fin, 4 duck ...
        public int face;       // mouth/eye expression variant
        public bool sitting;
    }

    /// Animation surface of a built diver character (implemented by the Player module on the character root).
    public interface IDiverPuppet
    {
        Transform Head { get; }
        /// swimming: horizontal swim pose; speed m/s for walk/swim cycles; held items drive the hand poses/models.
        void SetPose(bool swimming, bool seated, float speed, HoldItem right, HoldItem left, bool lightOn);
        /// Mouth flap / expression while talking (voice chat) – 0..1 loudness.
        void SetTalking(float loudness01);
    }

    /// Character factory published by the Player module (assign in its installer). Returns the character root;
    /// the root (or a child) carries an IDiverPuppet. Null when the Player module is not installed.
    public static class DiverModels
    {
        public static Func<DiverLook, GameObject> Build;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Build = null; }
    }

    /// Every diver in the world: the local player plus remote avatars. Fauna AI, audio and proximity features
    /// use this instead of Refs.Player so creatures react to all players.
    public static class Divers
    {
        public static readonly List<Transform> All = new List<Transform>();

        public static void Add(Transform t) { if (t != null && !All.Contains(t)) All.Add(t); }
        public static void Remove(Transform t) { All.Remove(t); }

        /// Nearest diver to p (null when none). dist = distance in meters.
        public static Transform Nearest(Vector3 p, out float dist)
        {
            Transform best = null;
            dist = float.MaxValue;
            for (int i = All.Count - 1; i >= 0; i--)
            {
                var t = All[i];
                if (t == null) { All.RemoveAt(i); continue; }
                float d = (t.position - p).sqrMagnitude;
                if (d < dist) { dist = d; best = t; }
            }
            dist = best != null ? Mathf.Sqrt(dist) : float.MaxValue;
            return best;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { All.Clear(); }
    }
}
