using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Creature replication. The HOST simulates every animal (Fauna module, unchanged AI) and, per client, keeps an
    /// interest set of the animals within ~90 m of that client's diver (110 m to leave again): new ones are sent as a
    /// reliable spawn (net id, species, size, tier, pose), vanished/far ones as a reliable despawn, and the poses of
    /// the interesting ones as 10 Hz unreliable snapshots (14 bytes per animal).
    /// Round 2b (L.1 "fish everywhere"): a pose is only sent when it changed (crawling starfish, perched birds, a
    /// manta at a cleaning station cost nothing) with a keep-alive every 0.8 s, and animals beyond 45 m from that
    /// client go out at 5 Hz – the open-world density costs about the same bandwidth as the old per-site spawning.
    /// Other creature lists (land fauna, L.11) join through RegisterSource.
    /// CLIENTS set NetFlags.CreaturesRemote (their Fauna spawner then only makes local hallucinations) and show
    /// NetCreatureProxy stand-ins that are registered as ICreature, so filming works on every machine.
    public sealed class NetCreatureSync : MonoBehaviour
    {
        public const float SnapInterval = 0.1f;
        public const float InterestEnter = 90f;
        public const float InterestLeave = 110f;
        /// Beyond this distance from the client's diver poses go out every second tick (5 Hz).
        public const float FarDistance = 45f;
        /// An unchanged pose is repeated after this many ticks (proxies drop animals after 3 s of silence).
        const int KeepAliveTicks = 8;

        static readonly List<IReadOnlyList<FaunaCreature>> extraSources = new List<IReadOnlyList<FaunaCreature>>(2);

        /// Host: replicate the animals of another list too (e.g. the land fauna of L.11). Its creatures need unique
        /// FaunaCreature.netId values from the same serial as the spawner's (FaunaSpawner hands them out).
        public static void RegisterSource(IReadOnlyList<FaunaCreature> list)
        {
            if (list != null && !extraSources.Contains(list)) extraSources.Add(list);
        }

        public static void UnregisterSource(IReadOnlyList<FaunaCreature> list) => extraSources.Remove(list);
        /// Proxies are shown this far behind the host (10 Hz snapshots need > 1 interval of buffer).
        public const float ProxyDelay = 0.15f;
        const int HostId = 0;
        // MaxSnapPerMsg: ONE unreliable snapshot per guest and tick (the count is a byte; FaunaSpawner.MaxActive = 160
        // fits). Split frames of the same type would replace each other in a congested TCP send queue (TcpNetTransport
        // merges the newest queued frame of a type), so only the last part of every tick would ever arrive.
        const int MaxSpawnPerMsg = 40, MaxSnapPerMsg = 255, MaxDespawnPerMsg = 200;

        // ------------------------------------------------------------------ host side
        /// What a client last got for one animal (change detection).
        struct Known
        {
            public int seenTick, sentTick;
            public ushort px, py, pz;
            public uint rot;
            public byte act, anim;
        }

        sealed class Interest
        {
            public int playerId;
            public int tick;
            public readonly Dictionary<int, Known> known = new Dictionary<int, Known>(160);
        }

        readonly Dictionary<int, Interest> interests = new Dictionary<int, Interest>();
        readonly List<Interest> interestList = new List<Interest>(4);
        readonly List<int> tmpIds = new List<int>(64);
        readonly SyncWriter spawnW = new SyncWriter(1024);
        readonly SyncWriter snapW = new SyncWriter(1024);
        readonly SyncWriter despawnW = new SyncWriter(512);
        readonly SyncWriter smallW = new SyncWriter(16);
        int spawnCount, snapCount, despawnCount;
        int spawnCountPos, snapCountPos, despawnCountPos;
        float snapTimer;

        // ------------------------------------------------------------------ client side
        readonly Dictionary<int, NetCreatureProxy> proxies = new Dictionary<int, NetCreatureProxy>(256);
        readonly List<NetCreatureProxy> proxyList = new List<NetCreatureProxy>(256);
        readonly Dictionary<SpeciesDef, Stack<NetCreatureProxy>> pool = new Dictionary<SpeciesDef, Stack<NetCreatureProxy>>();
        Transform proxyRoot;
        bool needResync = true;
        int unknownStreak;
        float lastResync = -100f;
        bool handlersOn;

        static Dictionary<SpeciesDef, int> speciesIndex;

        public int ProxyCount => proxyList.Count;

        void Awake()
        {
            var r = new GameObject("NetCreatureProxies");
            r.transform.SetParent(transform, false);
            proxyRoot = r.transform;
        }

        void OnEnable()
        {
            NetBus.Register(NetSyncMsg.CreatureSpawn, OnSpawn);
            NetBus.Register(NetSyncMsg.CreatureDespawn, OnDespawn);
            NetBus.Register(NetSyncMsg.CreatureSnap, OnSnap);
            NetBus.Register(NetSyncMsg.CreatureResync, OnResync);
            handlersOn = true;
            Net.PlayerLeft += OnPlayerLeft;
            Net.SessionEnded += OnSessionEnded;
        }

        void OnDisable()
        {
            if (handlersOn)
            {
                NetBus.Unregister(NetSyncMsg.CreatureSpawn);
                NetBus.Unregister(NetSyncMsg.CreatureDespawn);
                NetBus.Unregister(NetSyncMsg.CreatureSnap);
                NetBus.Unregister(NetSyncMsg.CreatureResync);
                handlersOn = false;
            }
            Net.PlayerLeft -= OnPlayerLeft;
            Net.SessionEnded -= OnSessionEnded;
            NetFlags.CreaturesRemote = false;
            ReleaseAll();
        }

        void OnPlayerLeft(NetPlayerInfo p)
        {
            if (p == null || !interests.TryGetValue(p.id, out var it)) return;
            interests.Remove(p.id);
            interestList.Remove(it);
        }

        void OnSessionEnded(string reason)
        {
            interests.Clear();
            interestList.Clear();
            ReleaseAll();
            needResync = true;
        }

        static int SpeciesIndex(SpeciesDef s)
        {
            if (speciesIndex == null)
            {
                speciesIndex = new Dictionary<SpeciesDef, int>();
                var all = SpeciesDatabase.All;
                for (int i = 0; i < all.Count; i++) if (all[i] != null) speciesIndex[all[i]] = i;
            }
            return s != null && speciesIndex.TryGetValue(s, out int idx) ? idx : -1;
        }

        static SpeciesDef SpeciesAt(int idx)
        {
            var all = SpeciesDatabase.All;
            return idx >= 0 && idx < all.Count ? all[idx] : null;
        }

        // ------------------------------------------------------------------ frame
        void Update()
        {
            NetFlags.CreaturesRemote = Net.IsClient;
            if (Net.IsHost) HostUpdate();
            else
            {
                if (interestList.Count > 0) { interests.Clear(); interestList.Clear(); }
                if (Net.IsClient) ClientUpdate();
                else if (proxyList.Count > 0) ReleaseAll();
            }
        }

        // ================================================================== HOST
        void HostUpdate()
        {
            snapTimer -= Time.unscaledDeltaTime;
            if (snapTimer > 0f) return;
            snapTimer = SnapInterval;
            if (!NetWorldSync.InGame) return;
            var spawner = FaunaModule.Spawner;
            var avatars = NetAvatarSync.Instance;
            if (spawner == null || avatars == null) return;

            // one interest per remote diver that is in the world
            var all = avatars.All;
            for (int i = 0; i < all.Count; i++)
            {
                var a = all[i];
                if (a == null) continue;
                if (!a.Visible)
                {
                    // not in the world right now (loading, menu, timed out): it drops its proxies, so start over later
                    if (interests.TryGetValue(a.PlayerId, out var gone)) gone.known.Clear();
                    continue;
                }
                if (!interests.TryGetValue(a.PlayerId, out var it))
                {
                    it = new Interest { playerId = a.PlayerId };
                    interests[a.PlayerId] = it;
                    interestList.Add(it);
                }
                Process(it, a.HeadPosition, spawner.ActiveCreatures);
            }
        }

        void Process(Interest it, Vector3 center, IReadOnlyList<FaunaCreature> list)
        {
            int tick = ++it.tick;
            float ts = SyncTimelines.Now;
            BeginSpawn(ts);
            BeginSnap(ts);
            BeginDespawn();
            ProcessList(it, center, list, tick);
            for (int s = 0; s < extraSources.Count; s++)
            {
                var extra = extraSources[s];
                if (extra != null && !ReferenceEquals(extra, list)) ProcessList(it, center, extra, tick);
            }
            tmpIds.Clear();
            foreach (var kv in it.known) if (kv.Value.seenTick != tick) tmpIds.Add(kv.Key);
            for (int i = 0; i < tmpIds.Count; i++)
            {
                it.known.Remove(tmpIds[i]);
                AddDespawn(it.playerId, tmpIds[i]);
            }
            FlushSpawn(it.playerId);
            FlushDespawn(it.playerId);
            FlushSnap(it.playerId);
        }

        void ProcessList(Interest it, Vector3 center, IReadOnlyList<FaunaCreature> list, int tick)
        {
            float inR2 = InterestEnter * InterestEnter, outR2 = InterestLeave * InterestLeave, farR2 = FarDistance * FarDistance;
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                if (ReferenceEquals(c, null) || c == null) continue;
                if (c.hallucination || !c.alive || c.pendingDespawn || c.netId <= 0 || c.species == null) continue;
                float d2 = (c.pos - center).sqrMagnitude;
                bool known = it.known.TryGetValue(c.netId, out var k);
                if (d2 > (known ? outR2 : inR2)) continue;
                if (!known && SpeciesIndex(c.species) < 0) continue;   // not replicable (unknown to the database)
                SyncQuant.PackPos(c.pos, out ushort px, out ushort py, out ushort pz);
                uint rot = SyncQuant.PackRot(c.tr != null ? c.tr.rotation : Quaternion.identity);
                byte act = SyncQuant.To01(c.Activity01);
                byte anim = SyncQuant.To01(NetCreatureProxy.AnimChannel(c));
                if (!known)
                {
                    AddSpawn(it.playerId, c, px, py, pz, rot, act, anim);
                    it.known[c.netId] = new Known { seenTick = tick, sentTick = tick, px = px, py = py, pz = pz, rot = rot, act = act, anim = anim };
                    continue;
                }
                k.seenTick = tick;
                int since = tick - k.sentTick;
                bool far = d2 > farR2;
                bool changed = Mathf.Abs(px - k.px) > 0 || Mathf.Abs(pz - k.pz) > 0 || Mathf.Abs(py - k.py) > 3
                               || SyncQuant.RotChanged(rot, k.rot) || Mathf.Abs(act - k.act) > 6 || Mathf.Abs(anim - k.anim) > 6;
                // near: every tick when it moved; far: every second tick (5 Hz, staggered by id); always a keep-alive
                bool due = since >= KeepAliveTicks || (changed && (!far || ((tick + c.netId) & 1) == 0));
                if (due)
                {
                    AddSnap(it.playerId, c.netId, px, py, pz, rot, act, anim);
                    k.sentTick = tick;
                    k.px = px; k.py = py; k.pz = pz; k.rot = rot; k.act = act; k.anim = anim;
                }
                it.known[c.netId] = k;
            }
        }

        void BeginSpawn(float ts)
        {
            spawnW.Reset();
            spawnW.Float(ts);
            spawnCountPos = spawnW.Length;
            spawnW.Byte(0);
            spawnCount = 0;
        }

        void AddSpawn(int target, FaunaCreature c, ushort px, ushort py, ushort pz, uint rot, byte act, byte anim)
        {
            int idx = SpeciesIndex(c.species);
            if (idx < 0) return;
            spawnW.UShort((ushort)c.netId);
            spawnW.UShort((ushort)idx);
            spawnW.Float(c.sizeCm);
            spawnW.Byte((byte)c.tier);
            SyncQuant.WritePackedPos(spawnW, px, py, pz);
            SyncQuant.WritePackedRot(spawnW, rot);
            spawnW.Byte(act);
            spawnW.Byte(anim);
            if (++spawnCount >= MaxSpawnPerMsg)
            {
                float ts = SyncTimelines.Now;
                FlushSpawn(target);
                BeginSpawn(ts);
            }
        }

        void FlushSpawn(int target)
        {
            if (spawnCount == 0) return;
            spawnW.PatchByte(spawnCountPos, (byte)spawnCount);
            NetBus.Send(NetSyncMsg.CreatureSpawn, spawnW.ToArray(), target, true);
            spawnCount = 0;
        }

        void BeginSnap(float ts)
        {
            snapW.Reset();
            snapW.Float(ts);
            snapCountPos = snapW.Length;
            snapW.Byte(0);
            snapCount = 0;
        }

        void AddSnap(int target, int netId, ushort px, ushort py, ushort pz, uint rot, byte act, byte anim)
        {
            snapW.UShort((ushort)netId);
            SyncQuant.WritePackedPos(snapW, px, py, pz);
            SyncQuant.WritePackedRot(snapW, rot);
            snapW.Byte(act);
            snapW.Byte(anim);
            if (++snapCount >= MaxSnapPerMsg)
            {
                float ts = SyncTimelines.Now;
                FlushSnap(target);
                BeginSnap(ts);
            }
        }

        void FlushSnap(int target)
        {
            if (snapCount == 0) return;
            snapW.PatchByte(snapCountPos, (byte)snapCount);
            NetBus.Send(NetSyncMsg.CreatureSnap, snapW.ToArray(), target, false);
            snapCount = 0;
        }

        void BeginDespawn()
        {
            despawnW.Reset();
            despawnCountPos = despawnW.Length;
            despawnW.Byte(0);
            despawnCount = 0;
        }

        void AddDespawn(int target, int id)
        {
            despawnW.UShort((ushort)id);
            if (++despawnCount >= MaxDespawnPerMsg)
            {
                FlushDespawn(target);
                BeginDespawn();
            }
        }

        void FlushDespawn(int target)
        {
            if (despawnCount == 0) return;
            despawnW.PatchByte(despawnCountPos, (byte)despawnCount);
            NetBus.Send(NetSyncMsg.CreatureDespawn, despawnW.ToArray(), target, true);
            despawnCount = 0;
        }

        void OnResync(int fromId, byte[] payload)
        {
            if (!Net.IsHost) return;
            if (interests.TryGetValue(fromId, out var it)) it.known.Clear();   // everything is sent as a spawn again
        }

        // ================================================================== CLIENT
        void ClientUpdate()
        {
            if (!NetWorldSync.InGame)
            {
                if (proxyList.Count > 0) ReleaseAll();
                needResync = true;
                return;
            }
            if (needResync && Time.unscaledTime - lastResync > 1f) SendResync();

            var tl = SyncTimelines.Get(HostId);
            float rt = tl.RenderTime(tl.Delay(ProxyDelay, 0.3f));
            Vector3 camPos = Vector3.zero;
            var p = Refs.Player;
            if (NetWorldSync.Alive(p) && p.View != null) camPos = p.View.transform.position;
            else if (Camera.main != null) camPos = Camera.main.transform.position;
            float dt = Time.deltaTime;
            float now = Time.unscaledTime;
            float time = Time.time;
            for (int i = proxyList.Count - 1; i >= 0; i--)
            {
                var px = proxyList[i];
                if (px == null) { proxyList.RemoveAt(i); continue; }
                // nothing heard for a while (lost despawn / left the host's interest): drop it
                if (now - px.LastHeard > 3f) { Release(px, i); continue; }
                px.Tick(dt, rt, camPos, time);
            }
        }

        void SendResync()
        {
            needResync = false;
            lastResync = Time.unscaledTime;
            unknownStreak = 0;
            smallW.Reset();
            smallW.Byte(1);
            NetBus.Send(NetSyncMsg.CreatureResync, smallW.ToArray(), NetBus.Everyone, true);
        }

        void OnSpawn(int fromId, byte[] payload)
        {
            if (!Net.IsClient) return;
            if (!NetWorldSync.InGame) { needResync = true; return; }
            var r = new SyncReader(payload);
            float ts = r.Float();
            int n = r.Byte();
            if (r.Failed) return;
            SyncTimelines.Get(HostId).Observe(ts);
            for (int i = 0; i < n; i++)
            {
                int id = r.UShort();
                int idx = r.UShort();
                float cm = r.Float();
                int tier = r.Byte();
                Vector3 pos = SyncQuant.ReadPos(ref r);
                Quaternion rot = SyncQuant.ReadRot(ref r);
                float act = SyncQuant.From01(r.Byte());
                float an = SyncQuant.From01(r.Byte());
                if (r.Failed) return;
                var s = SpeciesAt(idx);
                if (s == null) continue;
                var t = (SizeTier)Mathf.Clamp(tier, 0, (int)SizeTier.Legendary);
                if (proxies.TryGetValue(id, out var existing) && existing != null)
                {
                    if (existing.Species == s && Mathf.Abs(existing.SizeCm - cm) < 0.01f)
                    {
                        existing.Push(ts, pos, rot, act, an);
                        continue;
                    }
                    Release(existing, proxyList.IndexOf(existing));
                }
                var px = Take(s);
                if (px == null) continue;
                px.Activate(id, cm, t, ts, pos, rot, act, an);
                proxies[id] = px;
                proxyList.Add(px);
            }
        }

        void OnDespawn(int fromId, byte[] payload)
        {
            if (!Net.IsClient) return;
            var r = new SyncReader(payload);
            int n = r.Byte();
            for (int i = 0; i < n; i++)
            {
                int id = r.UShort();
                if (r.Failed) return;
                if (proxies.TryGetValue(id, out var px)) Release(px, proxyList.IndexOf(px));
            }
        }

        void OnSnap(int fromId, byte[] payload)
        {
            if (!Net.IsClient || !NetWorldSync.InGame) return;
            var r = new SyncReader(payload);
            float ts = r.Float();
            int n = r.Byte();
            if (r.Failed) return;
            SyncTimelines.Get(HostId).Observe(ts);
            int unknown = 0;
            for (int i = 0; i < n; i++)
            {
                int id = r.UShort();
                Vector3 pos = SyncQuant.ReadPos(ref r);
                Quaternion rot = SyncQuant.ReadRot(ref r);
                float act = SyncQuant.From01(r.Byte());
                float an = SyncQuant.From01(r.Byte());
                if (r.Failed) return;
                if (proxies.TryGetValue(id, out var px) && px != null) px.Push(ts, pos, rot, act, an);
                else unknown++;
            }
            // animals we never got a spawn for (missed while loading): ask for a fresh start after a few snapshots
            if (unknown > 0)
            {
                if (++unknownStreak >= 5 && Time.unscaledTime - lastResync > 3f) needResync = true;
            }
            else unknownStreak = 0;
        }

        NetCreatureProxy Take(SpeciesDef s)
        {
            if (pool.TryGetValue(s, out var st))
            {
                while (st.Count > 0)
                {
                    var p = st.Pop();
                    if (p != null) return p;
                }
            }
            GameObject go = null;
            var spawner = FaunaModule.Spawner;
            try
            {
                if (spawner != null) go = spawner.CloneModel(s, proxyRoot);
                else
                {
                    go = CreatureFactory.Build(s);
                    if (go != null)
                    {
                        go.SetActive(false);
                        go.transform.SetParent(proxyRoot, false);
                        var cols = go.GetComponentsInChildren<Collider>(true);
                        for (int i = 0; i < cols.Length; i++) Destroy(cols[i]);
                    }
                }
            }
            catch (System.Exception e) { Debug.LogWarning("Sealebrities/Net: proxy model for " + s.id + " failed: " + e.Message); }
            if (go == null) return null;
            go.name = s.id + "_net";
            var px = go.AddComponent<NetCreatureProxy>();
            px.Setup(s);
            return px;
        }

        void Release(NetCreatureProxy px, int listIndex)
        {
            if (ReferenceEquals(px, null)) return;
            proxies.Remove(px.NetId);
            if (listIndex >= 0 && listIndex < proxyList.Count && ReferenceEquals(proxyList[listIndex], px))
            {
                int last = proxyList.Count - 1;
                proxyList[listIndex] = proxyList[last];
                proxyList.RemoveAt(last);
            }
            else proxyList.Remove(px);
            if (px == null) return;
            px.Deactivate();
            var s = px.Species;
            if (s == null) { Destroy(px.gameObject); return; }
            if (!pool.TryGetValue(s, out var st))
            {
                st = new Stack<NetCreatureProxy>(8);
                pool[s] = st;
            }
            st.Push(px);
        }

        void ReleaseAll()
        {
            for (int i = proxyList.Count - 1; i >= 0; i--) Release(proxyList[i], i);
            proxyList.Clear();
            proxies.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { speciesIndex = null; extraSources.Clear(); }
    }
}
