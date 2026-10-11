using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Per-player crew data in co-op:
    /// * LOOK (1c, Q51, Q50): name + full DiverLook. Sent when the character screen saved ("Fertig" →
    ///   CharacterScreen.LookSaved) and whenever the visible gear (tank / fins tier, lamp / camera on the strap) or the
    ///   rank badge changed (checked every second). Clients send LookChanged (96) to the host; the host validates it,
    ///   updates its player list and broadcasts the list – every machine rebuilds that avatar.
    /// * CARRY (K.3, L.7): the carried quest item / salvage id of every player (CarryState 102, any → host → all), so the
    ///   avatars hold it. ItemOf / CarriersOf for the player module's "two divers carry together ×0.85" rule.
    /// * RECORDS (F.3): guests push their PlayerRecord to the host whenever it changed (checked every 10 s) on top of
    ///   core's 60 s push – the host's save is never more than a few seconds behind when the host suddenly ends.
    public static class NetCrew
    {
        const float LookPoll = 1f, RecordPoll = 10f;

        static readonly SyncWriter writer = new SyncWriter(96);
        static readonly Dictionary<int, string> carried = new Dictionary<int, string>(4);
        static int lastLookHash;
        static float lookTimer, recordTimer;
        static int lastRecordHash;
        static bool lookForce, handlersOn;

        /// Raised when the carried item of any player changed (avatars, UI).
        public static event Action<int> CarryChanged;

        // ================================================================== carry API
        /// Item id the player carries (null = hands free). Works for the local player too.
        public static string ItemOf(int playerId)
        {
            if (playerId == Net.LocalId) return Carry.Current;
            return carried.TryGetValue(playerId, out string s) ? s : null;
        }

        /// Players (incl. the local one) carrying exactly this item id right now – K.3: two divers carrying one salvage
        /// piece swim ×0.85 each instead of ×0.55. Returns the count, fills `into` when given.
        public static int CarriersOf(string itemId, List<int> into = null)
        {
            into?.Clear();
            if (string.IsNullOrEmpty(itemId)) return 0;
            int n = 0;
            if (Carry.Current == itemId) { n++; into?.Add(Net.LocalId); }
            foreach (var kv in carried)
            {
                if (kv.Key == Net.LocalId || kv.Value != itemId) continue;
                n++;
                into?.Add(kv.Key);
            }
            return n;
        }

        // ================================================================== install
        internal static void Install()
        {
            if (handlersOn) return;
            handlersOn = true;
            NetBus.Register(NetSyncMsg.LookChanged, OnLook);
            NetBus.Register(NetSyncMsg.CarryState, OnCarry);
            CharacterScreen.LookSaved += OnLookSaved;
            GameEvents.CarryChanged += OnLocalCarry;
            GameEvents.GearChanged += OnGearChanged;
            GameEvents.RankChanged += OnRankChanged;
        }

        internal static void Uninstall()
        {
            if (!handlersOn) return;
            handlersOn = false;
            NetBus.Unregister(NetSyncMsg.LookChanged);
            NetBus.Unregister(NetSyncMsg.CarryState);
            CharacterScreen.LookSaved -= OnLookSaved;
            GameEvents.CarryChanged -= OnLocalCarry;
            GameEvents.GearChanged -= OnGearChanged;
            GameEvents.RankChanged -= OnRankChanged;
        }

        static void OnLookSaved() { lookForce = true; lookTimer = 0f; }
        static void OnGearChanged() { lookTimer = Mathf.Min(lookTimer, 0.2f); }
        static void OnRankChanged(int rank) { lookTimer = Mathf.Min(lookTimer, 0.2f); }

        internal static void OnSessionStarted()
        {
            lastLookHash = 0;
            lookForce = true;
            lookTimer = 0.5f;
            recordTimer = RecordPoll;
            lastRecordHash = 0;
            carried.Clear();
        }

        internal static void Reset()
        {
            carried.Clear();
            lastLookHash = 0;
            lastRecordHash = 0;
        }

        internal static void OnPlayerJoined(NetPlayerInfo p)
        {
            if (p == null || p.IsLocal) return;
            if (Net.IsHost)
            {
                // late joiner: everybody's carried item
                foreach (var kv in carried) SendCarryTo(kv.Key, kv.Value, p.id);
                string mine = Carry.Current;
                if (!string.IsNullOrEmpty(mine)) SendCarryTo(Net.LocalId, mine, p.id);
            }
        }

        internal static void OnPlayerLeft(NetPlayerInfo p)
        {
            if (p == null) return;
            if (carried.Remove(p.id)) CarryChanged?.Invoke(p.id);
        }

        /// Client entered the host's world: tell everybody what we carry (normally nothing) and push our record soon.
        internal static void OnClientEnteredWorld(bool recordKnownByHost)
        {
            string mine = Carry.Current;
            if (!string.IsNullOrEmpty(mine)) SendCarryTo(Net.LocalId, mine, NetBus.Everyone);
            // a host that did not know this machine yet gets our record right away (hotbar, stats, achievements)
            recordTimer = recordKnownByHost ? RecordPoll : 3f;
            lastRecordHash = recordKnownByHost ? RecordHash(PlayerRecords.CaptureLocal()) : 0;
        }

        // ================================================================== frame
        internal static void Tick(float dt)
        {
            if (!Net.IsOnline) return;
            var gm = GameManager.I;
            lookTimer -= dt;
            if (lookTimer <= 0f)
            {
                lookTimer = LookPoll;
                PollLook();
            }
            if (Net.IsClient && gm != null && gm.InGame)
            {
                recordTimer -= dt;
                if (recordTimer <= 0f)
                {
                    recordTimer = RecordPoll;
                    var rec = PlayerRecords.CaptureLocal();
                    int h = RecordHash(rec);
                    if (h != lastRecordHash)
                    {
                        lastRecordHash = h;
                        PlayerRecords.SendLocal();
                    }
                }
            }
        }

        /// Hash of the record fields worth a push (play minutes excluded – they always tick).
        static int RecordHash(PlayerRecord r)
        {
            if (r == null) return 0;
            unchecked
            {
                int h = 17;
                if (r.hotbarRight != null) for (int i = 0; i < r.hotbarRight.Length; i++) h = h * 31 + (r.hotbarRight[i] != null ? r.hotbarRight[i].GetHashCode() : 0);
                if (r.hotbarLeft != null) for (int i = 0; i < r.hotbarLeft.Length; i++) h = h * 31 + (r.hotbarLeft[i] != null ? r.hotbarLeft[i].GetHashCode() : 0);
                if (r.equipped != null) for (int i = 0; i < r.equipped.Count; i++) h = h * 31 + (r.equipped[i] != null ? r.equipped[i].GetHashCode() : 0);
                h = h * 31 + (r.achievements != null ? r.achievements.Count : 0);
                h = h * 31 + r.fishCaught; h = h * 31 + r.speciesFilmed; h = h * 31 + r.rescues; h = h * 31 + r.dives; h = h * 31 + r.faints;
                h = h * 31 + r.coconuts; h = h * 31 + r.grilled; h = h * 31 + r.snaps; h = h * 31 + r.photos;
                h = h * 31 + (r.name != null ? r.name.GetHashCode() : 0);
                h = h * 31 + NetLookCodec.Hash(r.look);
                return h;
            }
        }

        // ================================================================== look
        /// The local look as it travels: lampOnStrap / cameraOnStrap mean "owns a lamp / camera" on the wire – every
        /// machine hides the strap item itself while the avatar holds it (no avatar rebuild for every hand switch).
        public static DiverLook WireLook()
        {
            var l = Settings.Look;
            var gm = GameManager.I;
            bool inGame = gm != null && gm.InGame;
            l.lampOnStrap = inGame && gm.Gear.hasLamp;
            l.cameraOnStrap = inGame && Hands.Owns(HoldItem.Camera);
            return l;
        }

        static void PollLook()
        {
            var me = Net.Find(Net.LocalId);
            if (me == null) return;
            var look = WireLook();
            string name = NetSession.LocalName();
            int h = NetLookCodec.Hash(look) * 31 + (name != null ? name.GetHashCode() : 0);
            if (!lookForce && h == lastLookHash) return;
            lookForce = false;
            lastLookHash = h;
            if (Net.IsHost)
            {
                if (ApplyLook(me, name, look, true)) NetSession.BroadcastPlayerListNow();
                return;
            }
            // optimistic: the own entry shows the new look at once (the host's list confirms it)
            me.look = look;
            me.suit = look.suit;
            me.hat = look.hat;
            me.face = look.face;
            writer.Reset();
            writer.Byte((byte)Mathf.Clamp(Net.LocalId, 0, 255));
            writer.Str(name);
            NetLookCodec.Write(writer, look);
            NetBus.Send(NetSyncMsg.LookChanged, writer.ToArray(), NetBus.Everyone, true);
        }

        static void OnLook(int fromId, byte[] payload)
        {
            if (!Net.IsHost || fromId == Net.LocalId) return;
            var r = new SyncReader(payload);
            r.Byte();   // origin (the host trusts its transport)
            string name = r.Str();
            var look = NetLookCodec.Read(ref r);
            if (r.Failed) return;
            var p = Net.Find(fromId);
            if (p == null) return;
            if (ApplyLook(p, name, look, false)) NetSession.BroadcastPlayerListNow();
        }

        /// Host: validates and stores a player's name + look (also in its PlayerRecord). True when anything changed.
        static bool ApplyLook(NetPlayerInfo p, string name, DiverLook look, bool local)
        {
            look = NetLookCodec.Sanitize(look);
            bool changed = !NetLookCodec.Same(p.look, look);
            if (!local)
            {
                string clean = NetSession.CleanPlayerName(name);
                if (!string.Equals(clean, p.name, StringComparison.Ordinal))
                {
                    clean = NetSession.UniqueNameFor(clean, p.id);
                    if (clean != p.name) { p.name = clean; changed = true; }
                }
            }
            else if (!string.IsNullOrEmpty(name) && name != p.name) { p.name = name; Net.LocalName = name; changed = true; }
            p.look = look;
            p.suit = look.suit;
            p.hat = look.hat;
            p.face = look.face;
            if (!local && !string.IsNullOrEmpty(p.key))
            {
                var rec = PlayerRecords.Find(p.key);
                if (rec != null) { rec.look = look; rec.name = p.name; }
            }
            return changed;
        }

        // ================================================================== carry
        static void OnLocalCarry(string oldId, string newId)
        {
            if (!Net.IsOnline) return;
            SendCarryTo(Net.LocalId, newId, NetBus.Everyone);
        }

        static void SendCarryTo(int who, string item, int target)
        {
            writer.Reset();
            writer.Byte((byte)Mathf.Clamp(who, 0, 255));
            writer.Str(item ?? "");
            NetBus.Send(NetSyncMsg.CarryState, writer.ToArray(), target, true);
        }

        static void OnCarry(int fromId, byte[] payload)
        {
            var r = new SyncReader(payload);
            int origin = r.Byte();
            string item = r.Str();
            if (r.Failed) return;
            if (Net.IsHost)
            {
                if (fromId == Net.LocalId) return;
                origin = fromId;
                NetBus.SendExcept(NetSyncMsg.CarryState, payload, fromId, true);
                // the host save keeps it in the player's record too (core's CarryKey does the same for older clients)
                var p = Net.Find(fromId);
                var rec = p != null ? PlayerRecords.Find(p.key) : null;
                if (rec != null) rec.carried = string.IsNullOrEmpty(item) ? null : item;
            }
            if (origin == Net.LocalId) return;
            if (item != null && item.Length > 96) item = item.Substring(0, 96);
            if (string.IsNullOrEmpty(item)) carried.Remove(origin);
            else carried[origin] = item;
            CarryChanged?.Invoke(origin);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            carried.Clear();
            lastLookHash = 0; lookTimer = 0f; recordTimer = 0f; lastRecordHash = 0; lookForce = false; handlersOn = false;
            CarryChanged = null;
        }
    }

    /// Compact DiverLook wire format (≈ 30 bytes) + validation shared by the player list (net-core) and LookChanged.
    public static class NetLookCodec
    {
        const byte FlagSnorkel = 1, FlagLamp = 2, FlagCamera = 4, FlagSitting = 8;

        public static void Write(SyncWriter w, in DiverLook l)
        {
            w.Byte((byte)Mathf.Clamp(l.lookVersion, 0, 255));
            w.Color32(Opaque(l.suit));
            w.Color32(l.cuff);
            w.Byte(ClampByte(l.hat)); w.Byte(ClampByte(l.face)); w.Byte(ClampByte(l.pattern)); w.Byte(ClampByte(l.eyes));
            w.Byte(ClampByte(l.brows)); w.Byte(ClampByte(l.skin)); w.Byte(ClampByte(l.mouth));
            w.Color32(l.mask);
            w.Color32(l.fins);
            w.Byte(ClampByte(l.tankTier)); w.Byte(ClampByte(l.finTier)); w.Byte(ClampByte(l.rank));
            byte f = 0;
            if (l.snorkel) f |= FlagSnorkel;
            if (l.lampOnStrap) f |= FlagLamp;
            if (l.cameraOnStrap) f |= FlagCamera;
            if (l.sitting) f |= FlagSitting;
            w.Byte(f);
        }

        public static DiverLook Read(ref SyncReader r)
        {
            var l = new DiverLook();
            l.lookVersion = r.Byte();
            l.suit = r.Color32();
            l.cuff = r.Color32();
            l.hat = r.Byte(); l.face = r.Byte(); l.pattern = r.Byte(); l.eyes = r.Byte();
            l.brows = r.Byte(); l.skin = r.Byte(); l.mouth = r.Byte();
            l.mask = r.Color32();
            l.fins = r.Color32();
            l.tankTier = r.Byte(); l.finTier = r.Byte(); l.rank = r.Byte();
            byte f = r.Byte();
            l.snorkel = (f & FlagSnorkel) != 0;
            l.lampOnStrap = (f & FlagLamp) != 0;
            l.cameraOnStrap = (f & FlagCamera) != 0;
            l.sitting = (f & FlagSitting) != 0;
            return l;
        }

        /// Same format through the net-core writer (player list, Hello).
        public static void Write(NetCoreWriter w, in DiverLook l)
        {
            w.Byte((byte)Mathf.Clamp(l.lookVersion, 0, 255));
            C(w, Opaque(l.suit)); C(w, l.cuff);
            w.Byte(ClampByte(l.hat)).Byte(ClampByte(l.face)).Byte(ClampByte(l.pattern)).Byte(ClampByte(l.eyes));
            w.Byte(ClampByte(l.brows)).Byte(ClampByte(l.skin)).Byte(ClampByte(l.mouth));
            C(w, l.mask); C(w, l.fins);
            w.Byte(ClampByte(l.tankTier)).Byte(ClampByte(l.finTier)).Byte(ClampByte(l.rank));
            byte f = 0;
            if (l.snorkel) f |= FlagSnorkel;
            if (l.lampOnStrap) f |= FlagLamp;
            if (l.cameraOnStrap) f |= FlagCamera;
            if (l.sitting) f |= FlagSitting;
            w.Byte(f);
        }

        public static DiverLook Read(NetCoreReader r)
        {
            var l = new DiverLook();
            l.lookVersion = r.Byte();
            l.suit = C(r); l.cuff = C(r);
            l.hat = r.Byte(); l.face = r.Byte(); l.pattern = r.Byte(); l.eyes = r.Byte();
            l.brows = r.Byte(); l.skin = r.Byte(); l.mouth = r.Byte();
            l.mask = C(r); l.fins = C(r);
            l.tankTier = r.Byte(); l.finTier = r.Byte(); l.rank = r.Byte();
            byte f = r.Byte();
            l.snorkel = (f & FlagSnorkel) != 0;
            l.lampOnStrap = (f & FlagLamp) != 0;
            l.cameraOnStrap = (f & FlagCamera) != 0;
            l.sitting = (f & FlagSitting) != 0;
            return l;
        }

        static void C(NetCoreWriter w, Color c) =>
            w.Byte(SyncQuant.To01(c.r)).Byte(SyncQuant.To01(c.g)).Byte(SyncQuant.To01(c.b)).Byte(SyncQuant.To01(c.a));

        static Color C(NetCoreReader r)
        {
            float cr = r.Byte() / 255f, cg = r.Byte() / 255f, cb = r.Byte() / 255f, ca = r.Byte() / 255f;
            return new Color(cr, cg, cb, ca);
        }

        static byte ClampByte(int v) => (byte)Mathf.Clamp(v, 0, 255);
        static Color Opaque(Color c) { c.a = 1f; return c; }

        /// Indices into the cosmetics ranges, colours finite; legacy looks get skin / mouth from face.
        public static DiverLook Sanitize(DiverLook l)
        {
            if (l.lookVersion < 1 && l.face == 0 && l.skin == 0 && l.mouth == 0 && l.suit.r + l.suit.g + l.suit.b <= 0f) l.suit = new Color(1f, 0.55f, 0.2f);
            l = l.Normalized();
            l.hat = Mathf.Clamp(l.hat, 0, CosmeticsDatabase.HatCount - 1);
            l.pattern = Mathf.Clamp(l.pattern, 0, CosmeticsDatabase.PatternCount - 1);
            l.eyes = Mathf.Clamp(l.eyes, 0, CosmeticsDatabase.EyesCount - 1);
            l.brows = Mathf.Clamp(l.brows, 0, CosmeticsDatabase.BrowsCount - 1);
            l.skin = Mathf.Clamp(l.skin, 0, CosmeticsDatabase.SkinTones.Length - 1);
            l.mouth = Mathf.Clamp(l.mouth, 0, CosmeticsDatabase.MouthCount - 1);
            l.face = l.skin * 5 + l.mouth;
            l.tankTier = Mathf.Clamp(l.tankTier, 0, 5);
            l.finTier = Mathf.Clamp(l.finTier, 0, 3);
            l.rank = Mathf.Clamp(l.rank, 0, 16);
            l.suit = Finite(l.suit, new Color(1f, 0.55f, 0.2f));
            l.suit.a = 1f;
            l.cuff = Finite(l.cuff, new Color(0f, 0f, 0f, 0f));
            l.mask = Finite(l.mask, new Color(0f, 0f, 0f, 0f));
            l.fins = Finite(l.fins, new Color(0f, 0f, 0f, 0f));
            return l;
        }

        static Color Finite(Color c, Color fallback)
        {
            if (float.IsNaN(c.r) || float.IsNaN(c.g) || float.IsNaN(c.b) || float.IsNaN(c.a)) return fallback;
            return new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), Mathf.Clamp01(c.a));
        }

        /// A look from the legacy fields (older peers / NetPlayerInfo without a full look).
        public static DiverLook FromLegacy(Color suit, int hat, int face)
        {
            var l = new DiverLook { suit = suit, hat = hat, face = face, lookVersion = 0, snorkel = true };
            l.cuff = new Color(0f, 0f, 0f, 0f);
            return Sanitize(l);
        }

        /// Allocation-free hash over everything visible (quantized like the wire format).
        public static int Hash(in DiverLook l)
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + l.lookVersion;
                h = h * 31 + Q(l.suit); h = h * 31 + Q(l.cuff); h = h * 31 + Q(l.mask); h = h * 31 + Q(l.fins);
                h = h * 31 + l.hat; h = h * 31 + l.face; h = h * 31 + l.pattern; h = h * 31 + l.eyes; h = h * 31 + l.brows;
                h = h * 31 + l.skin; h = h * 31 + l.mouth; h = h * 31 + l.tankTier; h = h * 31 + l.finTier; h = h * 31 + l.rank;
                h = h * 31 + (l.snorkel ? 1 : 0) + (l.lampOnStrap ? 2 : 0) + (l.cameraOnStrap ? 4 : 0) + (l.sitting ? 8 : 0);
                h = h * 31 + l.sponsor;
                return h;
            }
        }

        public static bool Same(in DiverLook a, in DiverLook b) => Hash(a) == Hash(b);

        static int Q(Color c) => SyncQuant.To01(c.r) | (SyncQuant.To01(c.g) << 8) | (SyncQuant.To01(c.b) << 16) | (SyncQuant.To01(c.a) << 24);
    }
}
