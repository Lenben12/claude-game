using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Creature replication. The HOST simulates every animal (Fauna module, unchanged AI) and, per client, keeps an
    /// interest set of the animals within ~90 m of that client's diver (110 m to leave again): new ones are sent as a
    /// reliable spawn (net id, species, size, tier, pose), vanished/far ones as a reliable despawn, and the poses of all
    /// interesting ones as 10 Hz unreliable snapshots (14 bytes per animal).
    /// CLIENTS set NetFlags.CreaturesRemote (their Fauna spawner then only makes local hallucinations) and show
    /// NetCreatureProxy stand-ins that are registered as ICreature, so filming works on every machine.
    public sealed class NetCreatureSync : MonoBehaviour
    {
        public const float SnapInterval = 0.1f;
        public const float InterestEnter = 90f;
        public const float InterestLeave = 110f;
        /// Proxies are shown this far behind the host (10 Hz snapshots need > 1 interval of buffer).
        public const float ProxyDelay = 0.15f;
        const int HostId = 0;
        // MaxSnapPerMsg: ONE unreliable snapshot per guest and tick (the count is a byte; FaunaSpawner.MaxActive = 160
        // fits). Split frames of the same type would replace each other in a congested TCP send queue (TcpNetTransport
        // merges the newest queued frame of a type), so only the last part of every tick would ever arrive.
        const int MaxSpawnPerMsg = 40, MaxSnapPerMsg = 255, MaxDespawnPerMsg = 200;

        // ------------------------------------------------------------------ host side
        sealed class Interest
        {
            public int playerId;
            public int tick;
            public readonly Dictionary<int, int> known = new Dictionary<int, int>(128);
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
            float inR2 = InterestEnter * InterestEnter, outR2 = InterestLeave * InterestLeave;
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                if (ReferenceEquals(c, null) || c == null) continue;
                if (c.hallucination || !c.alive || c.pendingDespawn || c.netId <= 0 || c.species == null) continue;
                float d2 = (c.pos - center).sqrMagnitude;
                bool known = it.known.ContainsKey(c.netId);
                if (d2 > (known ? outR2 : inR2)) continue;
                if (!known && SpeciesIndex(c.species) < 0) continue;   // not replicable (unknown to the database)
                it.known[c.netId] = tick;
                if (known) AddSnap(it.playerId, c);
                else AddSpawn(it.playerId, c);
            }
            tmpIds.Clear();
            foreach (var kv in it.known) if (kv.Value != tick) tmpIds.Add(kv.Key);
            for (int i = 0; i < tmpIds.Count; i++)
            {
                it.known.Remove(tmpIds[i]);
                AddDespawn(it.playerId, tmpIds[i]);
            }
            FlushSpawn(it.playerId);
            FlushDespawn(it.playerId);
            FlushSnap(it.playerId);
        }

        void BeginSpawn(float ts)
        {
            spawnW.Reset();
            spawnW.Float(ts);
            spawnCountPos = spawnW.Length;
            spawnW.Byte(0);
            spawnCount = 0;
        }

        void AddSpawn(int target, FaunaCreature c)
        {
            int idx = SpeciesIndex(c.species);
            if (idx < 0) return;
            spawnW.UShort((ushort)c.netId);
            spawnW.UShort((ushort)idx);
            spawnW.Float(c.sizeCm);
            spawnW.Byte((byte)c.tier);
            SyncQuant.WritePos(spawnW, c.pos);
            SyncQuant.WriteRot(spawnW, c.tr != null ? c.tr.rotation : Quaternion.identity);
            spawnW.Byte(SyncQuant.To01(c.Activity01));
            spawnW.Byte(SyncQuant.To01(NetCreatureProxy.AnimChannel(c)));
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

        void AddSnap(int target, FaunaCreature c)
        {
            snapW.UShort((ushort)c.netId);
            SyncQuant.WritePos(snapW, c.pos);
            SyncQuant.WriteRot(snapW, c.tr != null ? c.tr.rotation : Quaternion.identity);
            snapW.Byte(SyncQuant.To01(c.Activity01));
            snapW.Byte(SyncQuant.To01(NetCreatureProxy.AnimChannel(c)));
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
        static void ResetStatics() { speciesIndex = null; }
    }
}
