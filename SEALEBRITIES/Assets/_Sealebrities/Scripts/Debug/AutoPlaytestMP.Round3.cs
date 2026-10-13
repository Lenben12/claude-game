using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Sealebrities
{
    /// Round-3 co-op checks (after the first real two-player session): open-world animals around every diver – the same
    /// animals at the same places for both, island animals, the new species as proxies; the KRILL.tv livestream started
    /// by the guest with a dare for the guest; NPC dialogue heard by both + a side quest accepted by the guest and
    /// advanced by both; the shared pantry with personal buffs + the grill; a chopper mishap crate found and opened by the
    /// guest; the host saving, going to the menu and continuing while the guest is away (the guest gets its own hotbar
    /// and stats back). Both windows run the same code, synchronized through the test channel.
    public sealed partial class AutoPlaytestMP
    {
        int npcLineCount, chatCount, streamStarts, streamEnds;
        readonly List<string> npcTexts = new List<string>();
        readonly List<ICreature> testAnimals = new List<ICreature>();

        void SubscribeRound3()
        {
            GameEvents.NpcLine += OnR3NpcLine;
            LiveState.Chat += OnR3Chat;
            KrillTv.Started += OnR3StreamStarted;
            KrillTv.Ended += OnR3StreamEnded;
        }

        void UnsubscribeRound3()
        {
            GameEvents.NpcLine -= OnR3NpcLine;
            LiveState.Chat -= OnR3Chat;
            KrillTv.Started -= OnR3StreamStarted;
            KrillTv.Ended -= OnR3StreamEnded;
        }

        void OnR3NpcLine(string speaker, string text, float seconds)
        {
            npcLineCount++;
            if (text != null && npcTexts.Count < 64) npcTexts.Add((speaker ?? "?") + ":" + text);
        }

        void OnR3Chat(ChatMessage m) => chatCount++;
        void OnR3StreamStarted() => streamStarts++;
        void OnR3StreamEnded(StreamSummary s) => streamEnds++;

        // ------------------------------------------------------------------ helpers
        static Vector3 OpenWater(Vector3 center, float depth)
        {
            for (float r = 0f; r <= 200f; r += 5f)
            {
                int steps = r < 1f ? 1 : Mathf.Max(8, Mathf.RoundToInt(r / 3f));
                for (int i = 0; i < steps; i++)
                {
                    float a = i * Mathf.PI * 2f / steps;
                    var p = new Vector3(center.x + Mathf.Cos(a) * r, Waves.SeaLevel, center.z + Mathf.Sin(a) * r);
                    if (WorldInfo.GroundHeight(p) < Waves.SeaLevel - depth) return p;
                }
            }
            return new Vector3(center.x, Waves.SeaLevel, center.z);
        }

        /// An eye point `eyeDepth` below the surface near `center` with free water around it (and around eye + other)
        /// and a clear way up – no coral head, rock or overhang in the way.
        static Vector3 OpenWaterEye(Vector3 center, float radius, float eyeDepth, Vector3 other)
        {
            for (float r = 0f; r <= radius; r += 4f)
            {
                int steps = r < 1f ? 1 : Mathf.Max(8, Mathf.RoundToInt(r / 3f));
                for (int i = 0; i < steps; i++)
                {
                    float a = i * Mathf.PI * 2f / steps + r * 0.11f;
                    var p = new Vector3(center.x + Mathf.Cos(a) * r, 0f, center.z + Mathf.Sin(a) * r);
                    if (WorldInfo.GroundHeight(p) > Waves.SeaLevel - eyeDepth - 2.5f) continue;
                    Vector3 eye = new Vector3(p.x, Waves.SeaLevel - eyeDepth, p.z);
                    if (!FreeColumn(eye, eyeDepth) || !FreeColumn(eye + other, eyeDepth)) continue;
                    return eye;
                }
            }
            Vector3 f = FindSeafloor(center, radius * 0.6f, -10f, -6.5f);
            return KeepInWater(new Vector3(f.x, Waves.SeaLevel - eyeDepth, f.z), 1.5f, 3f);
        }

        static bool FreeColumn(Vector3 eye, float toSurface)
        {
            if (Physics.CheckSphere(eye, 1.4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return false;
            if (Physics.SphereCast(eye, 0.6f, Vector3.up, out _, toSurface + 0.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return false;
            return true;
        }

        /// Feet next to a standing NPC / object: on ground at its own height (never on a roof above it), free capsule,
        /// and a clear view to its chest.
        static Vector3 FeetNear(Vector3 target, float radius, float startDeg)
        {
            for (int k = 0; k < 15; k++)
            {
                float a = (startDeg + k * 24f) * Mathf.Deg2Rad;
                Vector3 c = target + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
                if (!Physics.Raycast(new Vector3(c.x, target.y + 1.4f, c.z), Vector3.down, out RaycastHit hit, 3.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                if (hit.normal.y < 0.6f || Mathf.Abs(hit.point.y - target.y) > 0.9f || hit.point.y < Waves.SeaLevel) continue;
                Vector3 f = hit.point + Vector3.up * 0.05f;
                if (Physics.CheckCapsule(f + Vector3.up * 0.4f, f + Vector3.up * 1.5f, 0.3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                Vector3 eye = f + Vector3.up * 1.6f, chest = target + Vector3.up * 1.1f;
                if (Physics.Linecast(eye, chest, out RaycastHit lh, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    && lh.collider.GetComponentInParent<NpcActor>() == null && (lh.point - chest).sqrMagnitude > 0.36f) continue;
                return f;
            }
            return SnapDown(target + new Vector3(radius, 1.2f, 0f), 1.2f, 4f);
        }

        /// Waits while keeping the (frozen, diving) player's tank full.
        IEnumerator WaitUnder(float seconds)
        {
            float t = 0f, refill = 0f;
            while (t < seconds)
            {
                float dt = Time.unscaledDeltaTime;
                t += dt;
                refill -= dt;
                if (refill <= 0f && pc != null) { refill = 1f; pc.RefillAir(); }
                yield return null;
            }
        }

        /// Host: the spawner's real animal with this net id (null = none).
        static FaunaCreature HostCreature(int netId)
        {
            var sp = FaunaModule.Spawner;
            if (sp == null || netId <= 0) return null;
            var list = sp.ActiveCreatures;
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                if (ReferenceEquals(c, null) || c == null || !c.IsAlive || c.IsHallucination) continue;
                if (c.NetId == netId) return c;
            }
            return null;
        }

        /// Host: real (replicated) animals near a point – sea animals or island animals.
        static int HostAnimalsNear(Vector3 p, float r, bool land, StringBuilder species = null)
        {
            var sp = FaunaModule.Spawner;
            if (sp == null) return 0;
            var list = sp.ActiveCreatures;
            float r2 = r * r;
            int n = 0;
            var seen = species != null ? new HashSet<string>() : null;
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                if (ReferenceEquals(c, null) || c == null || !c.IsAlive || c.IsHallucination || c.NetId <= 0 || c.Species == null) continue;
                if (c.Species.land != land) continue;
                if ((c.Position - p).sqrMagnitude > r2) continue;
                n++;
                if (seen != null && seen.Add(c.Species.id)) species.Append(species.Length > 0 ? "," : "").Append(c.Species.id);
            }
            return n;
        }

        /// Client: the host's animals shown here (proxies) near a point, nearest first.
        static List<NetCreatureProxy> ProxiesNear(Vector3 p, float r, bool land)
        {
            var res = new List<NetCreatureProxy>();
            var reg = CreatureRegistry.Active;
            float r2 = r * r;
            for (int i = 0; i < reg.Count; i++)
            {
                if (!(reg[i] is NetCreatureProxy px) || px == null || !px.IsAlive || px.Species == null) continue;
                if (px.Species.land != land) continue;
                if ((px.Position - p).sqrMagnitude > r2) continue;
                res.Add(px);
            }
            res.Sort((a, b) => (a.Position - p).sqrMagnitude.CompareTo((b.Position - p).sqrMagnitude));
            return res;
        }

        static string ProxyList(List<NetCreatureProxy> list, int max)
        {
            var sb = new StringBuilder(256);
            for (int i = 0; i < list.Count && i < max; i++)
                sb.Append(list[i].NetId.ToString(Inv)).Append(':').Append(Vec(list[i].Position)).Append(';');
            return sb.ToString();
        }

        /// Host: every animal the guest reported (net id + position) is one of the host's animals at that place.
        void CheckSameAnimals(string list, string what)
        {
            if (string.IsNullOrEmpty(list)) { Check(false, what + ": no animal list from the guest"); return; }
            int total = 0, matched = 0, missing = 0;
            float worst = 0f;
            foreach (var part in list.Split(';'))
            {
                if (string.IsNullOrEmpty(part)) continue;
                int c = part.IndexOf(':');
                if (c <= 0 || !int.TryParse(part.Substring(0, c), NumberStyles.Integer, Inv, out int id)) continue;
                Vector3 p = ParseVec(part.Substring(c + 1));
                total++;
                var fc = HostCreature(id);
                if (fc == null) { missing++; continue; }
                float d = Vector3.Distance(fc.Position, p);
                worst = Mathf.Max(worst, d);
                if (d < 4f) matched++;
            }
            Check(total >= 3 && matched >= Mathf.CeilToInt(total * 0.8f),
                  what + ": " + matched + "/" + total + " of the guest's nearest animals are the host's animals at the same place (≤ 4 m; "
                  + missing + " unknown, worst " + F(worst) + " m)");
        }

        // ================================================================== 15 open world for both (L.1, L.11, G.1)
        IEnumerator StepOpenWorld()
        {
            yield return Sync("S15a", 120f);
            if (pc == null) yield break;
            pc.enabled = true;
            SetTime(0.45f);
            Vector3 open = OpenWater(Vector3.Lerp(WorldLayout.IslandCenter, WorldLayout.KelpIsland, 0.5f), 16f);
            float floorY = WorldInfo.GroundHeight(open);
            float eyeDepth = Mathf.Clamp((Waves.SeaLevel - floorY) * 0.5f, 1.5f, 9f);
            Vector3 eye = KeepInWater(new Vector3(open.x, Waves.SeaLevel - eyeDepth, open.z), 1.2f, 1.2f);
            if (isHost)
            {
                yield return Stand(HostSpot, 270f);
                // ---- island animals around the guest (the host is far away on the dock)
                yield return WaitVal("penpos", 60f);
                if (Val("penpos") != null)
                {
                    Vector3 pen = ParseVec(Val("penpos"));
                    yield return Wait(0.5f);
                    var sp = new StringBuilder();
                    int n = HostAnimalsNear(pen, 40f, true, sp);
                    Info("host: " + n + " island animals within 40 m of the guest at the pen (" + sp + "), host " + F(FlatDist(pc.Root.position, pen), "0") + " m away");
                    SendVal("penhost", n.ToString(Inv) + "|" + sp);
                }
                // ---- open sea around the guest
                yield return WaitVal("owready", 90f);
                float apart = FlatDist(pc.Root.position, eye);
                var ssp = new StringBuilder();
                int hostN = HostAnimalsNear(eye, 80f, false, ssp);
                Check(hostN >= 15, "host spawner filled the sea around the guest: " + hostN + " animals within 80 m of the guest (" + F(apart, "0") + " m from the host) – " + ssp);
                SendVal("owhost", hostN.ToString(Inv));
                yield return WaitVal("owlist", 30f);
                CheckSameAnimals(Val("owlist"), "guest alone at the open-sea spot");
                yield return Sync("S15b", 40f);
                // ---- the host swims over: both see the same animals
                Vector3 myEye = KeepInWater(eye + new Vector3(2.5f, 0f, -1.5f), 1.2f, 1.2f);
                yield return DiveTo(myEye, eye);
                yield return WaitUnder(3f);
                var a = PeerAvatar();
                Check(a != null && a.Visible && Vector3.Distance(a.HeadPosition, eye) < 3.5f, "host sees the guest's diver at the open-sea spot (" + (a != null ? F(Vector3.Distance(a.HeadPosition, eye)) + " m off" : "none") + ")");
                Pose(myEye, eye + Vector3.forward * 4f);
                SendVal("owthere", "1");
                yield return WaitVal("owlist2", 30f);
                CheckSameAnimals(Val("owlist2"), "both divers at the spot");
                yield return Shot("host_S15_open_sea_both");
                // ---- the new species of round 2 as proxies on the guest
                var spawner = Refs.Spawner;
                var list = new List<string> { "spotted_eagle_ray", "hermit_crab" };
                var all = SpeciesDatabase.All;
                for (int i = 0; i < all.Count && list.Count < 7; i++)
                    if (all[i] != null && all[i].homeSite == "fog_isle" && !all[i].land && !list.Contains(all[i].id)) list.Add(all[i].id);
                var sb = new StringBuilder();
                testAnimals.Clear();
                var fs = FaunaModule.Spawner;
                if (fs != null && fs.ActiveCount > 120) Info("spawner busy (" + fs.ActiveCount + " animals)");
                for (int k = 0; k < list.Count; k++)
                {
                    var def = SpeciesDatabase.Get(list[k]);
                    if (def == null || spawner == null) { Info("species " + list[k] + " missing"); continue; }
                    Vector3 at = KeepInWater(eye + Vector3.forward * (3f + k * 0.9f) + Vector3.right * ((k & 1) == 0 ? -1.3f : 1.3f), 0.8f, 1.2f);
                    ICreature c = null;
                    try { c = spawner.Spawn(def, at, Progression.RollSizeCm(def, SizeTier.Normal, rng), false); }
                    catch (Exception e) { Info("spawning " + list[k] + " threw " + e.Message); }
                    var fc = c as FaunaCreature;
                    if (fc == null || fc.NetId <= 0) { Info("could not spawn " + list[k]); continue; }
                    testAnimals.Add(c);
                    sb.Append(list[k]).Append(':').Append(fc.NetId.ToString(Inv)).Append(';');
                }
                Check(testAnimals.Count >= 4, "host spawned " + testAnimals.Count + " new-species animals in front of the guest (" + sb + ")");
                SendVal("newsp", sb.ToString());
                yield return WaitVal("newspok", 40f);
                Info("guest: " + (Val("newspok") ?? "-"));
                for (int i = 0; i < testAnimals.Count; i++)
                    if (testAnimals[i] != null && testAnimals[i].IsAlive && spawner != null) spawner.Despawn(testAnimals[i]);
                testAnimals.Clear();
                yield return Sync("S15c", 40f);
                pc.enabled = true;
                yield return Stand(HostSpot, 270f);
            }
            else
            {
                // ---- island animals (L.11): the guest at the goat pen, the host far away
                Vector3 pen = Vector3.zero;
                var spots = FaunaAnchors.LandSpots;
                for (int i = 0; i < spots.Count; i++) if (spots[i].kind == FaunaSpotKind.Pen) { pen = spots[i].pos; break; }
                if (pen == Vector3.zero) pen = WorldLayout.Village;
                // walk up like a player: first ~35 m away looking elsewhere (island animals never pop into view within
                // 22 m), then up to the pen
                Vector3 far = Vector3.zero;
                for (int k = 0; k < 16 && far == Vector3.zero; k++)
                {
                    float a = k * 22.5f * Mathf.Deg2Rad;
                    Vector3 c = pen + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 35f;
                    if (WorldInfo.GroundHeight(c) < Waves.SeaLevel + 0.4f) continue;
                    Vector3 f = SnapDown(c + Vector3.up * 30f, 2f, 60f);
                    if (f.y < Waves.SeaLevel + 0.3f) continue;
                    far = f;
                }
                if (far != Vector3.zero)
                {
                    yield return Stand(far, YawTo(pen, far));
                    yield return Wait(6f);
                }
                Vector3 feet = FeetNear(pen, 6f, 0f);
                yield return Stand(feet, YawTo(feet, pen));
                yield return Wait(2f);
                SendVal("penpos", Vec(pen));
                yield return WaitVal("penhost", 30f);
                var land = ProxiesNear(pen, 40f, true);
                var ls = new HashSet<string>();
                foreach (var px in land) ls.Add(px.Species.id);
                string ph = Val("penhost") ?? "0|";
                int hostLand = int.TryParse(ph.Split('|')[0], NumberStyles.Integer, Inv, out int hl) ? hl : 0;
                Check(land.Count >= 1 && land.Count >= Mathf.FloorToInt(hostLand * 0.7f),
                      "guest sees " + land.Count + " island animals at the goat pen as proxies (" + string.Join(",", ls) + "; host has " + hostLand + ")");
                yield return AimAt(pen + Vector3.up * 0.6f);
                yield return Shot("client_S15_island_animals");
                // ---- open sea: dive alone, far from the host
                yield return DiveTo(eye, eye + Vector3.forward * 10f);
                Check(pc.IsUnderwater, "guest under water in the open sea (" + F(pc.Depth) + " m, sea floor " + F(Waves.SeaLevel - floorY, "0") + " m)");
                yield return WaitUnder(9f);
                SendVal("owready", Vec(eye));
                yield return WaitVal("owhost", 30f);
                int hostN = Val("owhost") != null ? int.Parse(Val("owhost"), Inv) : 0;
                var near = ProxiesNear(eye, 80f, false);
                int inSight = 0, bandOut = 0, locked = 0, real = 0;
                float sight = FXVisibility.Underwater ? FXVisibility.Meters : 20f;
                bool night = DayPhase.Now == DayMask.Night;
                string outIds = "", lockedIds = "";
                var seen = new HashSet<string>();
                foreach (var px in near)
                {
                    var s = px.Species;
                    seen.Add(s.id);
                    if ((px.Position - eye).sqrMagnitude < sight * sight) inSight++;
                    float depth = Waves.SeaLevel - px.Position.y;
                    float lo = s.BandMin(night), hi = s.BandMax(night);
                    float margin = 2f + (hi - lo) * 0.12f;
                    if (depth < lo - margin || depth > hi + margin) { bandOut++; if (outIds.Length < 160) outIds += s.id + "@" + F(depth, "0") + " "; }
                    if (!Progression.IsGateOpen(s)) { locked++; if (lockedIds.Length < 160) lockedIds += s.id + " "; }
                }
                var reg = CreatureRegistry.Active;
                for (int i = 0; i < reg.Count; i++)
                    if (!(reg[i] is NetCreatureProxy) && reg[i] != null && !reg[i].IsHallucination && reg[i].IsAlive) real++;
                Check(near.Count >= 15 && near.Count >= Mathf.FloorToInt(hostN * 0.75f),
                      "guest sees " + near.Count + " of the host's animals within 80 m (host counts " + hostN + "; " + seen.Count + " species: " + string.Join(",", seen) + ")");
                if (inSight >= 5) Check(true, inSight + " animals within the " + F(sight, "0") + " m sight");
                else Info("only " + inSight + " animals within the " + F(sight, "0") + " m sight");
                Check(bandOut == 0, "every animal the guest sees is inside its depth band" + (bandOut > 0 ? " – outside: " + outIds : ""));
                Check(locked == 0, "no species of a locked site / story gate around the guest" + (locked > 0 ? " – " + lockedIds : ""));
                Check(real == 0, "the guest spawns no animals of its own (" + real + " non-proxy animals)");
                SendVal("owlist", ProxyList(ProxiesNear(eye, 30f, false), 12));
                yield return Shot("client_S15_open_sea");
                yield return Sync("S15b", 40f);
                yield return WaitVal("owthere", 40f);
                yield return WaitUnder(1.0f);
                SendVal("owlist2", ProxyList(ProxiesNear(eye, 30f, false), 12));
                var ha = PeerAvatar();
                if (ha != null) Pose(eye, ha.HeadPosition);
                yield return Shot("client_S15_host_diving_over");
                Pose(eye, eye + Vector3.forward * 10f);
                // ---- new species as proxies
                yield return WaitVal("newsp", 40f);
                string ns = Val("newsp") ?? "";
                int want = 0, got = 0;
                string missing = "";
                var pairs = new List<KeyValuePair<string, int>>();
                foreach (var part in ns.Split(';'))
                {
                    int c = part.IndexOf(':');
                    if (c <= 0 || !int.TryParse(part.Substring(c + 1), NumberStyles.Integer, Inv, out int id)) continue;
                    pairs.Add(new KeyValuePair<string, int>(part.Substring(0, c), id));
                }
                want = pairs.Count;
                float t0 = Time.realtimeSinceStartup;
                var found = new Dictionary<int, NetCreatureProxy>();
                while (found.Count < want && Time.realtimeSinceStartup - t0 < 10f)
                {
                    for (int i = 0; i < reg.Count; i++)
                        if (reg[i] is NetCreatureProxy px && px != null && px.IsAlive && !found.ContainsKey(px.NetId))
                            foreach (var kv in pairs) if (kv.Value == px.NetId) found[px.NetId] = px;
                    yield return WaitUnder(0.3f);
                }
                foreach (var kv in pairs)
                {
                    bool ok = found.TryGetValue(kv.Value, out var px) && px != null && px.Species != null && px.Species.id == kv.Key;
                    if (ok)
                    {
                        bool drawn = false;
                        foreach (var r in px.GetComponentsInChildren<Renderer>(false)) if (r != null && r.enabled) { drawn = true; break; }
                        ok = drawn;
                    }
                    if (ok) got++;
                    else missing += kv.Key + (found.ContainsKey(kv.Value) ? "(as " + (found[kv.Value].Species != null ? found[kv.Value].Species.id : "?") + ")" : "") + " ";
                }
                Check(want > 0 && got == want, got + "/" + want + " new species arrived as proxies with the right model" + (missing.Length > 0 ? " – missing " + missing : ""));
                NetCreatureProxy firstNew = null;
                foreach (var kv in found) if (kv.Value != null && kv.Value.IsAlive) { firstNew = kv.Value; break; }
                if (firstNew != null) Pose(eye, firstNew.Position);
                yield return Shot("client_S15_new_species");   // before the host despawns them
                SendVal("newspok", got + "/" + want + (missing.Length > 0 ? " missing " + missing : ""));
                yield return Sync("S15c", 40f);
                pc.enabled = true;
                yield return Stand(ClientSpot, 90f);
            }
        }

        // ================================================================== 16 livestream in co-op (C.8)
        IEnumerator StepLivestream()
        {
            yield return Sync("S16a", 120f);
            if (pc == null) yield break;
            pc.enabled = true;
            var boat = Refs.Boat as BoatController;
            Vector3 mooring = WorldInfo.BoatSpawn != Vector3.zero ? WorldInfo.BoatSpawn : WorldLayout.BoatSpawn;
            float mooringYaw = WorldInfo.BoatSpawn != Vector3.zero ? WorldInfo.BoatSpawnYaw : WorldLayout.BoatSpawnYaw;
            if (isHost)
            {
                var gm = GameManager.I;
                SetTime(0.45f);
                if (boat != null && NetFlags.BoatDriverId < 0 && FlatDist(boat.transform.position, mooring) > 3f) boat.PlaceAt(mooring, mooringYaw);
                if (boat != null && !boat.Anchored) boat.ToggleAnchor();
                if (S.followers < 1200) gm.AddFollowers(1200 - S.followers);
                S.lastStreamDay = -1;
                if (LiveState.IsLive) KrillTv.RequestEnd();
                yield return Stand(HostSpot, 270f);
                long money0 = S.money, f0 = S.followers;
                SendVal("lsready", S.followers.ToString(Inv));
                yield return WaitPeer(() => LiveState.IsLive, 40f);
                Check(LiveState.IsLive && LiveState.StreamerId == peerId, "the guest went live – the host runs the stream (streamer id " + LiveState.StreamerId + ")");
                if (!LiveState.IsLive) yield break;
                yield return Wait(10f);
                SendVal("lsviewers", LiveState.Viewers.ToString(Inv));
                var svc = StreamService.Instance;
                long before = S.money;
                bool offered = svc != null && svc.HostOfferDare(StreamDares.IndexOf("dare_three"), peerId, true);
                Check(offered && LiveState.Dare != null && LiveState.Dare.targetPlayerId == peerId, "a dare for the guest ('" + (LiveState.Dare != null ? LiveState.Dare.text : "-") + "')");
                long reward = LiveState.Dare != null ? LiveState.Dare.reward : 0;
                SendVal("lsdare", offered ? "1" : "0");
                // the guest's camera may finish "3 species in 30 s" by itself at once (fish below the boat): Running and
                // Done can then arrive in the same host frame
                yield return WaitPeer(() => LiveState.Dare != null && (LiveState.Dare.state == DareState.Running || LiveState.Dare.state == DareState.Done), 20f);
                var ds = LiveState.Dare != null ? LiveState.Dare.state : DareState.Expired;
                Check(ds == DareState.Running || ds == DareState.Done, "the guest accepted with [T] – " + (ds == DareState.Done ? "its own camera already finished the dare" : "the dare runs on the host") + " (" + ds + ")");
                bool done = ds == DareState.Done || KrillTv.DebugCompleteDare();
                Check(done && S.money >= before + reward, "dare done (" + (ds == DareState.Done ? "by the guest's camera" : "scripted success") + ") → +" + reward + " $ in the shared wallet");
                SendVal("lsdaredone", S.money.ToString(Inv));
                yield return Shot("host_S16_stream");
                yield return WaitPeer(() => !LiveState.IsLive, 40f);
                var sum = KrillTv.LastSummary;
                Check(!LiveState.IsLive && sum != null && !sum.fainted, "the guest ended the stream (" + (sum != null ? F(sum.seconds, "0") + " s, peak " + sum.peakViewers + " viewers, +" + sum.money + " $" : "-") + ")");
                yield return Wait(0.6f);
                Check(S.money > money0 && S.followers >= f0, "stream money +" + (S.money - money0) + " $, followers +" + (S.followers - f0)
                      + " (end card " + (sum != null ? (sum.followersDelta >= 0 ? "+" : "") + sum.followersDelta : "-") + "; a short stream earns few followers)");
                SendVal("lsend", S.money.ToString(Inv) + "|" + S.followers.ToString(Inv) + "|" + (sum != null ? sum.peakViewers : 0).ToString(Inv));
                yield return Sync("S16b", 40f);
            }
            else
            {
                yield return WaitVal("lsready", 30f);
                yield return WaitUntil(() => S.followers >= StreamRules.UnlockFollowers, 8f);
                yield return Stand(SnapDown(DockEnd() + new Vector3(-3.0f, 0.4f, 0f)), 0f);
                if (boat != null && !boat.PlayerAboard) boat.BoardFromDock();
                yield return Wait(0.8f);
                if (boat != null) pc.TeleportTo(boat.RescuePoint, boat.transform.eulerAngles.y);
                yield return Wait(1.0f);
                bool can = KrillTv.CanGoLive(out string why);
                Check(can, "KRILL.tv 'Live gehen' is enabled for the guest (" + (KrillTv.StartPlace() ?? "-") + (can ? "" : ": " + why) + ", " + S.followers + " followers)");
                if (Hands.Find(HoldItem.Camera, out Hand ch, out int cs) && !Hands.IsHeld(HoldItem.Camera)) Hands.Select(cs);
                int chat0 = chatCount, st0 = streamStarts;
                bool req = KrillTv.RequestStart();
                yield return WaitUntil(() => LiveState.IsLive, 15f);
                Check(req && LiveState.IsLive && streamStarts > st0, "the guest pressed 'Live gehen' → live on the guest too");
                if (!LiveState.IsLive) yield break;
                yield return WaitVal("lsviewers", 30f);
                int hv = Val("lsviewers") != null ? int.Parse(Val("lsviewers"), Inv) : -1;
                yield return Wait(0.6f);
                Check(LiveState.Viewers > 0 && Mathf.Abs(LiveState.Viewers - hv) <= Mathf.Max(8, hv / 4), "guest sees " + LiveState.Viewers + " viewers (host " + hv + ")");
                Check(chatCount > chat0, (chatCount - chat0) + " chat lines arrived on the guest");
                yield return WaitVal("lsdare", 20f);
                yield return WaitUntil(() => KrillTv.LocalIsDareTarget, 6f);
                Check(KrillTv.LocalIsDareTarget, "the dare card names the guest ('" + (LiveState.Dare != null ? LiveState.Dare.text : "-") + "')");
                yield return Shot("client_S16_dare");
                GameInput.SimTap(GameAction.StreamDare);
                yield return Wait(0.8f);
                if (LiveState.Dare != null && LiveState.Dare.state == DareState.Offered)
                {
                    Info("[T] did not reach the dare – KrillTv.AcceptDare");
                    KrillTv.AcceptDare();
                }
                yield return WaitVal("lsdaredone", 30f);
                long hm = Val("lsdaredone") != null ? long.Parse(Val("lsdaredone"), Inv) : -1;
                yield return WaitUntil(() => S.money == hm, 6f);
                Check(S.money == hm, "the dare reward is in the guest's wallet too (" + S.money + " $)");
                yield return Wait(2f);
                int end0 = streamEnds;
                KrillTv.RequestEnd();
                yield return WaitUntil(() => !LiveState.IsLive && streamEnds > end0, 12f);
                var sum = KrillTv.LastSummary;
                Check(!LiveState.IsLive && sum != null && !sum.fainted, "'Stream beenden' on the guest → end card on the guest (" + (sum != null ? "peak " + sum.peakViewers + ", +" + sum.money + " $" : "-") + ")");
                yield return Wait(1.2f);
                yield return Shot("client_S16_end_card");
                yield return WaitVal("lsend", 30f);
                string[] e = (Val("lsend") ?? "0|0|0").Split('|');
                long em = long.Parse(e[0], Inv), ef = long.Parse(e[1], Inv);
                yield return WaitUntil(() => S.money == em && S.followers == ef, 6f);
                Check(S.money == em && S.followers == ef, "guest wallet + followers equal the host's after the stream (" + S.money + " $, " + S.followers + ")");
                Check(sum == null || sum.peakViewers.ToString(Inv) == e[2], "same end card on both (peak " + (sum != null ? sum.peakViewers : -1) + " / host " + e[2] + ")");
                yield return Sync("S16b", 40f);
                yield return Stand(ClientSpot, 90f);
            }
        }

        // ================================================================== 17 NPC dialogue + side quest for both (A.2, L.7, L.10)
        IEnumerator StepNpcQuest()
        {
            const string qid = "M1";
            yield return Sync("S17a", 120f);
            if (pc == null) yield break;
            pc.enabled = true;
            Hands.StowAll();
            if (isHost)
            {
                SetTime(0.45f);
                // round 3: M1 is the hermit smith's job now – the camp is known and he is a friend (no first-contact stare)
                StoryFlags.Set(StoryKeys.HermitCampFound);
                foreach (var hid in NpcDatabase.Hermits) { Hermits.Raise(hid, 50); var hm = Hermits.GetOrAdd(hid); if (hm != null) hm.met = true; }   // (the guard must not bonk the testers)
                var hs = Hermits.GetOrAdd(NpcDatabase.HSmith);
                if (hs != null) hs.met = true;
                NetState.ForceSnapshot();
                if (Quests.Get(qid) != null && !Quests.IsDone(qid)) Quests.Drop(qid);
                // free a slot when the test game already has 3 side quests
                var s = S;
                for (int i = s.sideQuests.Count - 1; i >= 0 && Quests.ActiveSideCount >= Quests.MaxActiveSide; i--)
                    if (s.sideQuests[i] != null && !s.sideQuests[i].done && !Quests.IsStoryChain(s.sideQuests[i].id)) Quests.Drop(s.sideQuests[i].id);
            }
            yield return Sync("S17b", 30f);
            NpcActor mira = null;
            float tw = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - tw < 12f)
            {
                mira = NpcRegistry.Find(NpcDatabase.HSmith) as NpcActor;
                if (mira != null && mira.Present) break;
                yield return Wait(0.5f);
            }
            Check(mira != null && mira.Present, Role + ": the smith (Amboss) is in the hermit camp (" + (mira != null ? V(mira.Position) : "none") + ")");
            if (mira == null) yield break;
            Vector3 mp = mira.Position;
            Vector3 at = FeetNear(mp, isHost ? 2.6f : 2.0f, isHost ? 20f : 200f);
            yield return Stand(at, YawTo(at, mp));
            yield return AimAt(mira.Position + Vector3.up * 1.2f);
            yield return Sync("S17c", 30f);
            int lines0 = npcLineCount;
            npcTexts.Clear();
            if (!isHost)
            {
                string pm = InteractionPrompt.Text;
                Check(!string.IsNullOrEmpty(pm), "the guest's crosshair prompt at the smith: '" + pm + "' (" + F(FlatDist(pc.Root.position, mira.Position), "0.0") + " m)");
                GameInput.SimTap(GameAction.Interact);
                yield return Wait(1.2f);
                if (npcLineCount == lines0 && !mira.InConversation)
                {
                    Info("E did not reach the smith – NpcDirector.LocalInteract");
                    mira.Interact();
                }
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 70f && !mira.HasOffer && Quests.Get(qid) == null) yield return Wait(0.25f);
                Check(mira.HasOffer || Quests.Get(qid) != null, "after the signs the smith offers 'Holz für die Schleifbank' to the guest (" + (npcLineCount - lines0) + " lines heard, " + F(Time.realtimeSinceStartup - t0, "0") + " s)");
                yield return AimAt(mira.Position + Vector3.up * 1.2f);
                Info("prompt at the offer: '" + InteractionPrompt.Text + "'");
                yield return Shot("client_S17_offer");
                GameInput.SimTap(GameAction.Interact);
                yield return WaitUntil(() => Quests.Get(qid) != null, 4f);
                if (Quests.Get(qid) == null && mira.HasOffer)
                {
                    Info("E did not accept – NpcDirector.LocalInteract");
                    mira.Interact();
                    yield return WaitUntil(() => Quests.Get(qid) != null, 6f);
                }
                Check(Quests.Get(qid) != null, "the guest accepted the smith's job (M1) – the quest is in the guest's state");
                Quests.Track(qid);   // the quest-line pin is per player now – the guest pins its own line
                yield return Wait(2.5f);   // the accept line
                SendVal("npclines", (npcLineCount - lines0).ToString(Inv) + "|" + string.Join("¦", npcTexts));
            }
            else
            {
                yield return WaitPeer(() => Quests.Get(qid) != null, 90f);
                Check(Quests.IsActive(qid), "M1 accepted by the guest is active in the host state");
                yield return WaitVal("npclines", 20f);
                string[] cl = (Val("npclines") ?? "0|").Split(new[] { '|' }, 2);
                int clientLines = int.Parse(cl[0], Inv);
                var theirs = new HashSet<string>(cl.Length > 1 ? cl[1].Split('¦') : new string[0]);
                int same = 0;
                foreach (var t in npcTexts) if (theirs.Contains(t)) same++;
                Check(npcLineCount - lines0 >= 2 && clientLines >= 2 && same >= Mathf.Min(npcTexts.Count, theirs.Count) - 1,
                      "both heard Mira: host " + (npcLineCount - lines0) + " lines, guest " + clientLines + ", " + same + " identical (first: '" + (npcTexts.Count > 0 ? Trim(npcTexts[0], 80) : "-") + "')");
                Quests.Track(qid);
            }
            yield return Sync("S17d", 40f);
            yield return Wait(1.6f);   // the quest line refreshes once a second
            Check(QuestTracker.ActiveId == qid && QuestTracker.Progress == "0/5", Role + " quest line (top centre): '" + QuestTracker.Line + "' " + QuestTracker.Progress + " (" + QuestTracker.ActiveId + ")");
            if (!isHost) yield return Shot("client_S17_quest_line");
            // ---- progress: the guest collects one driftwood, then the host another one
            if (!isHost)
            {
                string used = null;
                yield return CollectQuestObject(qid, "driftwood", null, id => used = id);
                yield return WaitUntil(() => Quests.Get(qid) != null && Quests.Get(qid).progress >= 1, 6f);
                var q = Quests.Get(qid);
                Check(q != null && q.progress == 1, "guest picked up driftwood (" + (used ?? "-") + ") → progress " + (q != null ? q.progress : -1) + "/5");
                SendVal("sq1", used ?? "");
                yield return WaitVal("sq2", 60f);
                yield return WaitUntil(() => Quests.Get(qid) != null && Quests.Get(qid).progress >= 2, 6f);
                yield return Wait(1.2f);
                q = Quests.Get(qid);
                Check(q != null && q.progress == 2 && QuestTracker.Progress == "2/5", "the host's driftwood counts for the guest too: " + (q != null ? q.progress : -1) + "/5, quest line '" + QuestTracker.Progress + "'");
                yield return Shot("client_S17_progress_2of5");
            }
            else
            {
                yield return WaitVal("sq1", 60f);
                yield return WaitUntil(() => Quests.Get(qid) != null && Quests.Get(qid).progress >= 1, 6f);
                yield return Wait(1.2f);
                var q = Quests.Get(qid);
                Check(q != null && q.progress == 1 && QuestTracker.Progress == "1/5", "the guest's driftwood counts on the host: " + (q != null ? q.progress : -1) + "/5, quest line '" + QuestTracker.Progress + "'");
                string skip = Val("sq1");
                string used = null;
                yield return CollectQuestObject(qid, "driftwood", skip, id => used = id);
                yield return WaitUntil(() => Quests.Get(qid) != null && Quests.Get(qid).progress >= 2, 4f);
                q = Quests.Get(qid);
                Check(q != null && q.progress == 2, "host picked up driftwood (" + (used ?? "-") + ") → " + (q != null ? q.progress : -1) + "/5");
                SendVal("sq2", used ?? "");
            }
            yield return Sync("S17e", 40f);
            if (isHost) Quests.Drop(qid);   // keep later steps free of this quest
        }

        /// Collects one quest object of a side quest with E: the props module's „quest:Q:…“ item or the npc's own
        /// „group:n“ object (skip = an id another player already took).
        IEnumerator CollectQuestObject(string quest, string group, string skip, Action<string> done)
        {
            Component target = null;
            string id = null;
            Vector3 pos = Vector3.zero;
            float best = float.MaxValue;
            foreach (var p in Object.FindObjectsByType<PropQuestItem>(FindObjectsSortMode.None))
            {
                if (p == null || p.id == null || !p.id.StartsWith("quest:" + quest + ":", StringComparison.Ordinal) || p.id == skip || !p.CanInteract()) continue;
                float d = FlatDist(p.transform.position, pc.Root.position);
                if (d < best) { best = d; target = p; id = p.id; pos = p.trigger != null ? p.trigger.bounds.center : p.transform.position; }
            }
            if (target == null)
                foreach (var q in Object.FindObjectsByType<QuestObject>(FindObjectsSortMode.None))
                {
                    if (q == null || q.Id == null || !q.Id.StartsWith(group + ":", StringComparison.Ordinal) || q.Id == skip || !q.CanInteract()) continue;
                    float d = FlatDist(q.transform.position, pc.Root.position);
                    if (d < best) { best = d; target = q; id = q.Id; pos = q.transform.position + Vector3.up * 0.15f; }
                }
            if (target == null) { Check(false, Role + ": no " + group + " object of " + quest + " to collect"); done?.Invoke(null); yield break; }
            Vector3 feet = Vector3.zero;
            for (int k = 0; k < 12 && feet == Vector3.zero; k++)
            {
                float a = k * 30f * Mathf.Deg2Rad;
                Vector3 c = pos + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 1.5f;
                Vector3 f = SnapDown(c + Vector3.up * 2f, 2f, 6f);
                if (f.y < Waves.SeaLevel - 0.3f || Mathf.Abs(f.y - pos.y) > 1.6f) continue;
                if (Physics.CheckCapsule(f + Vector3.up * 0.4f, f + Vector3.up * 1.5f, 0.3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                feet = f;
            }
            if (feet == Vector3.zero) feet = SnapDown(pos + Vector3.up * 1.5f);
            yield return Stand(feet, YawTo(feet, pos));
            yield return AimAt(pos);
            Info(Role + " at " + id + " (" + F(best, "0") + " m away before): prompt '" + InteractionPrompt.Text + "'");
            int p0 = Quests.Get(quest) != null ? Quests.Get(quest).progress : -1;
            GameInput.SimTap(GameAction.Interact);
            yield return Wait(0.8f);
            var st = Quests.Get(quest);
            bool usedNow = target is PropQuestItem pq ? !pq.CanInteract() : target is QuestObject qo && !qo.CanInteract();
            if (!usedNow && (st == null || st.progress == p0))
            {
                Info("E did not take it from here – using the object directly");
                if (target is IInteractable ia) ia.Interact();
                yield return Wait(0.5f);
            }
            done?.Invoke(id);
        }

        // ================================================================== 18 food: shared pantry, own buffs, the grill (E.1, E.2)
        IEnumerator StepFood()
        {
            yield return Sync("S18a", 120f);
            if (pc == null) yield break;
            pc.enabled = true;
            // a pantry snack (not the grilled fish – the grill gives that one)
            FoodDef air = null;
            foreach (var f in FoodDatabase.All)
                if (f.buff == BuffType.AirCapacity && f.kind == FoodKind.Food && f.id != FoodDatabase.GrilledFish) { air = f; break; }
            var grill = FoodDatabase.Get(FoodDatabase.GrilledFish);
            if (air == null) { Check(false, "no AirCapacity food in the database"); yield break; }
            if (isHost)
            {
                Buffs.Clear();
                int have = Pantry.Count(air.id);
                Pantry.Add(air.id, 2);
                FishMarket.AddCatch(FishMarket.RollCatch(rng));
                var fish = Pantry.BestGrillFish();
                SendVal("food", air.id + "|" + (have + 2).ToString(Inv) + "|" + (fish != null ? fish.uid : 0).ToString(Inv));
                yield return WaitVal("ate", 40f);
                yield return WaitUntil(() => Pantry.Count(air.id) == have + 1, 6f);
                Check(Pantry.Count(air.id) == have + 1, "the guest's snack left the shared pantry on the host (" + Pantry.Count(air.id) + " left)");
                Check(!Buffs.Has(air.buff), "the buff is the guest's only – the host has none (" + Buffs.Active.Count + " active)");
                yield return WaitVal("grilled", 60f);
                yield return WaitUntil(() => fish == null || !HasFish(fish.uid), 6f);
                Check(fish != null && !HasFish(fish.uid), "the grill took the fish from the host's shared cooler (uid " + (fish != null ? fish.uid : 0) + ")");
                Check(grill == null || !Buffs.Has(grill.buff), "no grilled-fish buff on the host");
                yield return Sync("S18b", 40f);
            }
            else
            {
                Buffs.Clear();
                yield return WaitVal("food", 30f);
                string[] fv = (Val("food") ?? "||0").Split('|');
                int expect = fv.Length > 1 ? int.Parse(fv[1], Inv) : 2;
                int uid = fv.Length > 2 ? int.Parse(fv[2], Inv) : 0;
                yield return WaitUntil(() => Pantry.Count(air.id) == expect && HasFish(uid), 8f);
                Check(Pantry.Count(air.id) == expect, "the host's snacks are in the guest's pantry view (" + Pantry.Count(air.id) + " × " + air.id + ")");
                pc.RefillAir();
                yield return null;
                float cap0 = pc.AirSecondsLeft;
                bool ate = Pantry.TryEat(air.id);
                yield return Wait(1.5f);
                pc.RefillAir();
                yield return null;
                float cap1 = pc.AirSecondsLeft;
                Check(ate && Buffs.Has(air.buff), "guest ate " + air.id + " → own " + air.buff + " buff");
                Check(cap1 > cap0 * (1f + air.value * 0.8f), "guest's tank +" + F((cap1 / Mathf.Max(0.01f, cap0) - 1f) * 100f, "0") + " % (" + F(cap0, "0") + " → " + F(cap1, "0") + " s)");
                SendVal("ate", "1");
                yield return Wait(1.5f);
                Check(Pantry.Count(air.id) == expect - 1, "after the host's snapshot the guest's pantry still says " + Pantry.Count(air.id));
                // the grill at the hut (hold E 6 s)
                PropGrill g = null;
                float best = float.MaxValue;
                foreach (var pg in Object.FindObjectsByType<PropGrill>(FindObjectsSortMode.None))
                {
                    if (pg == null) continue;
                    float d = FlatDist(pg.transform.position, WorldLayout.Hut);
                    if (d < best) { best = d; g = pg; }
                }
                Check(g != null, "a grill near the hut (" + (g != null ? F(best, "0") + " m from the hut" : "none") + ")");
                Buffs.Clear();   // the grill's own buff must show up by itself
                yield return null;
                int grilled0 = Backpack.Count(FoodDatabase.GrilledFish);
                if (g != null)
                {
                    Vector3 gp = g.transform.position;
                    Vector3 feet = Vector3.zero;
                    for (int k = 0; k < 12 && feet == Vector3.zero; k++)
                    {
                        float a = k * 30f * Mathf.Deg2Rad;
                        Vector3 f = SnapDown(gp + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 1.8f + Vector3.up * 2f, 2f, 6f);
                        if (Mathf.Abs(f.y - gp.y) > 1.5f) continue;
                        if (Physics.CheckCapsule(f + Vector3.up * 0.4f, f + Vector3.up * 1.5f, 0.3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                        feet = f;
                    }
                    if (feet == Vector3.zero) feet = SnapDown(gp + new Vector3(1.8f, 2f, 0f));
                    yield return Stand(feet, YawTo(feet, gp));
                    var col = g.GetComponentInChildren<Collider>();
                    yield return AimAt(col != null ? col.bounds.center : gp + Vector3.up * 0.4f);
                    Info("prompt at the grill: '" + InteractionPrompt.Text + "'");
                    GameInput.SimTap(GameAction.Interact);
                    yield return Wait(0.5f);
                    if (!g.Running)
                    {
                        Info("E did not start the grill – PropGrill.Interact");
                        g.Interact();
                    }
                    yield return Wait(2.5f);
                    yield return Shot("client_S18_grill");
                    yield return WaitUntil(() => !g.Running, 6f);
                    yield return Wait(0.3f);
                }
                // round 3: the grilled fish goes into the guest's own backpack – eating it gives the buff
                Check(Backpack.Count(FoodDatabase.GrilledFish) == grilled0 + 1, "6 s at the grill → a grilled fish in the guest's backpack (" + Backpack.Count(FoodDatabase.GrilledFish) + ")");
                Backpack.UseId(FoodDatabase.GrilledFish);
                yield return Wait(0.3f);
                Check(grill != null && Buffs.Has(grill.buff), "eaten from the backpack → the guest's grilled-fish buff (" + (grill != null ? grill.buff.ToString() : "-") + ")");
                SendVal("grilled", "1");
                yield return Wait(1.2f);
                Check(!HasFish(uid), "the grilled fish is gone from the guest's cooler view too");
                Buffs.Clear();
                yield return Sync("S18b", 40f);
                yield return Stand(ClientSpot, 90f);
            }
        }

        // ================================================================== 19 chopper mishap: the guest finds the crate (L.12)
        IEnumerator StepMishap()
        {
            yield return Sync("S19a", 120f);
            if (pc == null) yield break;
            pc.enabled = true;
            var food = FoodDatabase.Get("krill_bar");
            if (food == null) { Info("no krill_bar food – skipped"); yield break; }
            string itemId = FoodDatabase.DeliveryId(food);
            Vector3 pad = WorldInfo.HelipadPosition != Vector3.zero ? WorldInfo.HelipadPosition : WorldLayout.Helipad;
            if (isHost)
            {
                var gm = GameManager.I;
                SetTime(0.40f);
                if (S.money < 2000) gm.AddMoney(2000 - S.money);
                if (S.followers < food.followersNeeded) gm.AddFollowers(food.followersNeeded - S.followers);
                HeliMishaps.ForceNext = DeliveryMishap.WrongPlace;
                int pantry0 = Pantry.Count(food.id);
                Vector3 me = SnapDown(pad + new Vector3(9f, 3f, 9f));
                yield return Stand(me, YawTo(me, pad));
                SendVal("mready", S.money.ToString(Inv));
                yield return WaitPeer(() => Shop.FindDelivery(itemId) != null, 40f);
                var d = Shop.FindDelivery(itemId);
                Check(d != null, "the guest's snack order is a delivery in the host state");
                if (d == null) yield break;
                yield return WaitUntil(() => d.Mishap == DeliveryMishap.WrongPlace, 3f);
                Check(d.Mishap == DeliveryMishap.WrongPlace, "the host rolled the mishap: " + d.Mishap + " (forced 'wrong place' for the test)");
                SendVal("marrival", d.arrivalDay.ToString(Inv) + "|" + d.arrivalTime.ToString("R", Inv));
                yield return Sync("S19b", 40f);
                S.timeOfDay = d.arrivalTime + 0.0015f;
                HeliCrate crate = null;
                yield return WaitCrateDown(itemId, c => crate = c);
                Check(crate != null && crate.Mishap == DeliveryMishap.WrongPlace, "host: the crate fell at the wrong place (" + (crate != null ? F(FlatDist(crate.transform.position, pad), "0") + " m from the pad" : "none") + ")");
                yield return WaitUntil(() => HeliService.SearchActive, 2f);
                Check(HeliService.SearchActive, "host: search line '" + QuestTracker.Line + "'");
                if (crate != null)
                {
                    Vector3 cp = crate.transform.position;
                    pc.enabled = false;
                    Pose(cp + new Vector3(-12f, 8f, -10f), cp + Vector3.up * 4f);
                    yield return Wait(1.2f);
                    yield return Shot("host_S19_mishap_smoke");
                    pc.enabled = true;
                    SendVal("mcrate", Vec(cp));
                }
                yield return WaitVal("mopened", 90f);
                // round 3: crate snacks go into the OPENER's backpack (only what does not fit lands in the shared fridge)
                yield return WaitUntil(() => Shop.FindDelivery(itemId) == null, 6f);
                Check(Shop.FindDelivery(itemId) == null, "the guest opened the lost crate: delivery gone (guest's backpack: " + Val("mopened") + " × " + food.id + ", shared pantry " + (Pantry.Count(food.id) - pantry0) + " new)");
                yield return WaitUntil(() => FindCrate(itemId) == null && !HeliService.SearchActive, 4f);
                Check(FindCrate(itemId) == null && !HeliService.SearchActive, "host: crate gone, search line cleared");
                yield return Sync("S19c", 40f);
                yield return Stand(HostSpot, 270f);
            }
            else
            {
                yield return WaitVal("mready", 30f);
                yield return WaitUntil(() => Shop.CanBuyFood(food, out _), 6f);
                bool ordered = Shop.BuyFood(food, true);
                Check(ordered, "the guest ordered " + food.id + " in the Chopper Shop");
                yield return WaitVal("marrival", 40f);
                yield return WaitUntil(() => { var dd = Shop.FindDelivery(itemId); return dd != null && dd.Mishap == DeliveryMishap.WrongPlace; }, 6f);
                var d = Shop.FindDelivery(itemId);
                Check(d != null && d.Mishap == DeliveryMishap.WrongPlace, "the host's mishap roll reached the guest (" + (d != null ? d.Mishap.ToString() : "-") + ")");
                yield return Sync("S19b", 40f);
                yield return Stand(SnapDown(pad + new Vector3(-9f, 3f, 9f)), YawTo(pad + new Vector3(-9f, 0f, 9f), pad));
                HeliCrate crate = null;
                yield return WaitCrateDown(itemId, c => crate = c);
                Check(crate != null && crate.Mishap == DeliveryMishap.WrongPlace, "guest: the crate fell at the wrong place (" + (crate != null ? F(FlatDist(crate.transform.position, pad), "0") + " m from the pad" : "none") + ")");
                yield return WaitUntil(() => HeliService.SearchActive, 2f);
                // the search entry exists; a running tutorial line keeps priority on the quest line (L.10 order)
                Check(HeliService.SearchActive && (QuestTracker.ActiveId == "heli_search" || QuestTracker.ActiveId == "tutorial"),
                      "guest: search active, quest line '" + QuestTracker.Line + "' (" + QuestTracker.ActiveId + ")");
                yield return WaitVal("mcrate", 40f);
                if (crate == null) yield break;
                Vector3 cp = crate.transform.position;
                if (Val("mcrate") != null)
                {
                    float off = Vector3.Distance(ParseVec(Val("mcrate")), cp);
                    Check(off < 2f, "the crate lies at the same place on both machines (" + F(off, "0.00") + " m apart)");
                }
                int pantry0 = Pantry.Count(food.id);
                int bp0 = Backpack.Count(food.id);
                Vector3 dir = Flat(pad - cp);
                Vector3 feet = SnapDown(cp + dir * 1.9f + Vector3.up * 2f);
                yield return Stand(feet, YawTo(feet, cp));
                yield return AimAt(cp + Vector3.up * 0.35f);
                Info("prompt at the lost crate: '" + InteractionPrompt.Text + "'");
                yield return Shot("client_S19_found_crate");
                GameInput.SimTap(GameAction.Interact);
                yield return Wait(0.8f);
                if (Pantry.Count(food.id) <= pantry0 && Backpack.Count(food.id) <= bp0 && crate != null && crate.CanInteract())
                {
                    Info("E did not open the crate from here – opened via the crate");
                    crate.Interact();
                    yield return Wait(0.4f);
                }
                yield return WaitUntil(() => Backpack.Count(food.id) > bp0 || Pantry.Count(food.id) > pantry0, 3f);
                int gotBp = Backpack.Count(food.id) - bp0;
                Check(gotBp > 0 || Pantry.Count(food.id) > pantry0, "the guest opened the crate → snacks in the guest's backpack (" + gotBp + ", round 3) / pantry (" + (Pantry.Count(food.id) - pantry0) + ")");
                SendVal("mopened", gotBp.ToString(Inv));
                yield return Wait(1.5f);
                Check(!HeliService.SearchActive && QuestTracker.ActiveId != "heli_search", "guest: search line cleared");
                yield return Sync("S19c", 40f);
                yield return Stand(ClientSpot, 90f);
            }
        }

        IEnumerator WaitCrateDown(string itemId, Action<HeliCrate> found)
        {
            float tw = Time.realtimeSinceStartup;
            HeliCrate crate = null;
            while (Time.realtimeSinceStartup - tw < 160f)
            {
                foreach (var c in Object.FindObjectsByType<HeliCrate>(FindObjectsSortMode.None))
                    if (c != null && c.Delivery != null && c.Delivery.itemId == itemId && c.IsDown && c.CanInteract()) { crate = c; break; }
                if (crate != null) break;
                yield return Wait(0.5f);
            }
            Info(Role + ": crate down after " + F(Time.realtimeSinceStartup - tw, "0") + " s");
            found(crate);
        }

        // ================================================================== 21 host reloads its save → the guest's data come back (F.3)
        IEnumerator StepHostReload()
        {
            var gm = GameManager.I;
            yield return Sync("S21a", 120f);
            if (pc == null) yield break;
            if (isHost)
            {
                int guest = peerId;
                string key = Net.Find(guest) != null ? Net.Find(guest).key : null;
                yield return WaitVal("rlrec", 30f);
                string[] rv = (Val("rlrec") ?? "|").Split('|');
                yield return WaitUntil(() => Net.Find(guest) == null, 20f);
                Check(Net.Find(guest) == null, "the guest left (its record is pushed on leaving)");
                yield return WaitUntil(() => { var r = PlayerRecords.Find(key); return r != null && HotbarCsv(r) == rv[0]; }, 8f);
                var rec = PlayerRecords.Find(key);
                Check(rec != null && HotbarCsv(rec) == rv[0], "host keeps the guest's new hotbar " + (rec != null ? HotbarCsv(rec) : "-") + " (guest sent " + rv[0] + ")");
                // "Speichern & Hauptmenü" → "Fortsetzen": the same JSON a save writes, kept in memory (the player's
                // save file is never touched by the test)
                PlayerRecords.Store(PlayerRecords.CaptureLocal());
                string json = gm.SnapshotJson();
                string before = StateLine();
                int records = S.players.Count;
                Net.Leave?.Invoke();
                yield return WaitUntil(() => UIState.MainMenuOpen && !gm.InGame && !Net.IsOnline, 15f);
                Check(UIState.MainMenuOpen && !gm.InGame && !Net.IsOnline, "host saved and went to the main menu (" + F(json.Length / 1024f, "0") + " KB save JSON, " + records + " player records)");
                yield return Wait(2f);
                string bind = string.IsNullOrWhiteSpace(NetBoot.BindOverride) ? HostIp : NetBoot.BindOverride;
                bool ok = NetSession.StartHost(port, bind, out string err);
                Check(ok, "host opens a new session (" + (ok ? NetDiagnostics.Transport : err) + ")");
                if (!ok) yield break;
                NetSession.SuppressSaving();
                bool loaded = gm.LoadFromJson(json);
                yield return WaitUntil(() => gm.InGame, 10f);
                Check(loaded && gm.InGame, "host continued the saved game");
                yield return Wait(1f);
                BindPlayer();
                var rec2 = PlayerRecords.Find(key);
                Check(S.players.Count == records && rec2 != null && HotbarCsv(rec2) == rv[0], "the guest's record survived the save/load (" + S.players.Count + " records, hotbar " + (rec2 != null ? HotbarCsv(rec2) : "-") + ")");
                string after = StateLine();
                Check(after == before, "same shared state after loading: " + after + (after == before ? "" : " (before " + before + ")"));
                peerId = -1;
                float t0 = Time.realtimeSinceStartup;
                yield return WaitUntil(() => FindPeerId() >= 0, 120f);
                peerId = FindPeerId();
                Check(peerId >= 0, "the guest joined the reloaded game after " + F(Time.realtimeSinceStartup - t0, "0") + " s");
                if (peerId < 0) yield break;
                yield return Sync("S21b", 60f);
                yield return WaitVal("rlback", 30f);
                Info("guest after the reload: " + (Val("rlback") ?? "-"));
                yield return Sync("S21c", 40f);
            }
            else
            {
                // a personal change the host must keep for this machine: rod into left slot 2
                Hands.Assign(4, ItemIds.FishingRod, Hand.Left);
                yield return Frames(2);
                var r0 = PlayerRecords.CaptureLocal();
                string hb = HotbarCsv(r0);
                string stats = StatsLine(r0);
                SendVal("rlrec", hb + "|" + stats);
                yield return Wait(1.5f);
                Net.Leave?.Invoke();   // ReturnToMenu pushes the record to the host (F.3)
                yield return WaitUntil(() => UIState.MainMenuOpen && !GameManager.I.InGame && !Net.IsOnline, 15f);
                Check(UIState.MainMenuOpen && !Net.IsOnline, "guest left to the main menu (hotbar " + hb + ")");
                yield return Wait(3f);
                StartJoin(120f);
                float t0 = Time.realtimeSinceStartup;
                yield return WaitUntil(() => NetSession.Current == NetSession.Phase.Joined && gm.InGame, 130f);
                bool joined = NetSession.Current == NetSession.Phase.Joined && gm.InGame && Net.IsClient;
                Check(joined, "rejoined the reloaded host after " + F(Time.realtimeSinceStartup - t0, "0") + " s as player " + Net.LocalId);
                if (!joined) yield break;
                peerId = 0;
                sessionEnded = false;
                sessionEndReason = null;
                BindPlayer();
                yield return Sync("S21b", 60f);
                yield return WaitUntil(() => HotbarCsv(PlayerRecords.CaptureLocal()) == hb, 8f);
                var r1 = PlayerRecords.CaptureLocal();
                Check(HotbarCsv(r1) == hb, "F.3 after the host reloaded: the guest's own hotbar is back (" + HotbarCsv(r1) + ")");
                Check(StatsLine(r1) == stats, "and its own statistics (" + StatsLine(r1) + (StatsLine(r1) == stats ? "" : " vs " + stats) + ")");
                Check(Journal.IsDiscovered("green_turtle") && S.ownedItems.Contains("tank_t1"), "the shared Sealog entry and the bought tank are still there");
                SendVal("rlback", HotbarCsv(r1) + " · " + StatsLine(r1));
                yield return Wait(1f);
                var a = PeerAvatar();
                if (a != null) yield return AimAt(a.HeadPosition);
                yield return Shot("client_S21_after_host_reload");
                yield return Sync("S21c", 40f);
            }
        }

        static string HotbarCsv(PlayerRecord r)
        {
            if (r == null) return "-";
            return (r.hotbar != null ? string.Join(",", r.hotbar) : "") + "/" + r.hotbarOffMask;
        }

        static string StatsLine(PlayerRecord r)
        {
            if (r == null) return "-";
            return "fish " + r.fishCaught + ", species " + r.speciesFilmed + ", dives " + r.dives + ", photos " + r.photos + ", rescues " + r.rescues + ", grilled " + r.grilled;
        }

        // ================================================================== sleep counter (L.9) on the HUD
        /// The co-op sleep counter under the quest line: its text, or "(hidden)" when the row is not shown.
        static string SleepCounterText()
        {
            var ui = UIModule.Controller;
            if (ui == null || ui.SleepCounter == null) return "(no ui)";
            var t = typeof(UISleepCounter);
            var lf = t.GetField("label", BindingFlags.NonPublic | BindingFlags.Instance);
            var rf = t.GetField("row", BindingFlags.NonPublic | BindingFlags.Instance);
            var label = lf != null ? lf.GetValue(ui.SleepCounter) as Label : null;
            var row = rf != null ? rf.GetValue(ui.SleepCounter) as VisualElement : null;
            if (label == null) return "(no label)";
            bool shown = row == null || row.style.display.value != DisplayStyle.None;
            return shown ? label.text : "(hidden)";
        }

        /// A sleep spot other than `exclude` (index in PropInfo.SleepSpots) – hammock first.
        static PropBed OtherBed(int exclude)
        {
            PropBed pick = null;
            foreach (var b in Object.FindObjectsByType<PropBed>(FindObjectsSortMode.None))
            {
                if (b == null || b.spotIndex == exclude) continue;
                if (pick == null || (b.kind == SleepSpotInfo.Hammock && pick.kind != SleepSpotInfo.Hammock)) pick = b;
            }
            return pick;
        }
    }
}
