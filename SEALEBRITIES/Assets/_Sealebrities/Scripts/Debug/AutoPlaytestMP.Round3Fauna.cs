using System.Collections;
using UnityEngine;

namespace Sealebrities
{
    /// Round-3 fauna in co-op (GDD §20 Q17/Q18/Q20): the GUEST spears the host's food fish through its proxy (raw fish
    /// lands in the guest's backpack, the host's animal is gone), a host shark hunts the GUEST (the bite arrives as
    /// NetSyncMsg.FaunaCombat: Puste / air on the guest's machine, the danger music too) and a chest opened by the guest
    /// is open for the host (only the opener gets the loot). Hooked at the end of StepRound3Core (S32a–e).
    public sealed partial class AutoPlaytestMP
    {
        IEnumerator StepRound3Fauna()
        {
            yield return Sync("S32a", 90f);
            if (pc == null) yield break;
            var site = DiveSites.Get("house_reef");
            Vector3 floor = FindSeafloor(site.center, site.radius * 0.6f, -11f, -6f);
            Vector3 hostEye = KeepInWater(floor + Vector3.up * 3f, 2.5f, 2f);
            Vector3 guestEye = KeepInWater(floor + new Vector3(4f, 3f, 0f), 2.5f, 2f);
            Vector3 eye = isHost ? hostEye : guestEye;
            yield return DiveTo(eye, eye + Vector3.forward * 8f);
            SendVal("r3fdive", "1");
            yield return WaitVal("r3fdive", 40f);
            yield return Wait(1.5f);

            // ---- 1: the guest spears the host's snapper
            if (isHost)
            {
                var a = PeerAvatar();
                var sp = FaunaModule.Spawner;
                var def = SpeciesDatabase.Get("snapper");
                FaunaCreature fish = null;
                if (a != null && sp != null && def != null)
                {
                    Vector3 ge = a.HeadPosition;
                    fish = sp.SpawnOne(def, KeepInWater(ge + Vector3.forward * 1.8f, 1f, 1.5f), ge, 20f);
                }
                Check(fish != null, "host: a snapper in front of the guest");
                SendVal("r3ffish", fish != null ? fish.netId.ToString(Inv) : "-1");
                yield return WaitVal("r3fcaught", 40f);
                if (fish != null)
                {
                    int id = fish.netId;
                    yield return WaitUntil(() => !fish.alive || fish.pendingDespawn || fish.netId != id, 6f);
                    Check(!fish.alive || fish.pendingDespawn || fish.netId != id, "host: the guest's spear took the fish out of the host's sea");
                }
            }
            else
            {
                yield return WaitVal("r3ffish", 40f);
                int.TryParse(Val("r3ffish") ?? "-1", System.Globalization.NumberStyles.Integer, Inv, out int fid);
                NetCreatureProxy px = null;
                yield return WaitUntil(() => (px = FindProxy(fid)) != null, 10f);
                if (Check(px != null, "guest: the host's snapper arrived as a proxy (" + fid + ")"))
                {
                    Backpack.Add(ItemIds.SpearT1, 1, false);
                    int raw0 = Backpack.Count(ItemIds.RawFish);
                    Vector3 from = px.Position - (px.Position - cam.transform.position).normalized * 1.2f;
                    bool hit = Combat.Strike(from, (px.WorldBounds.center - from).normalized, ItemDatabase.Get(ItemIds.SpearT1), Backpack.First(ItemIds.SpearT1), true, pc.transform, out _);
                    yield return Wait(0.3f);
                    Check(hit && Backpack.Count(ItemIds.RawFish) > raw0, "guest: speared the proxy – raw fish in the GUEST's backpack (" + raw0 + " → " + Backpack.Count(ItemIds.RawFish) + ")");
                    Check(!px.IsAlive, "guest: the caught proxy is hidden right away");
                }
                SendVal("r3fcaught", "1");
            }
            yield return Sync("S32b", 60f);

            // ---- 2: a host shark hunts the guest
            if (isHost)
            {
                var th = FaunaThreat.Instance;
                int bites0 = FaunaThreat.Bites, miss0 = FaunaThreat.Misses;
                bool started = th != null && th.ForceStalkOn(peerId, "whitetip_shark");
                Check(started, "host: a reef shark hunts the guest");
                float t = 0f;
                while (t < 60f && FaunaThreat.Busy && FaunaThreat.Bites == bites0 && FaunaThreat.Misses == miss0) { t += Time.unscaledDeltaTime; yield return null; }
                Check(FaunaThreat.Bites > bites0 || FaunaThreat.Misses > miss0, "host: the charge on the guest " + (FaunaThreat.Bites > bites0 ? "bit" : "missed"));
                SendVal("r3fbite", FaunaThreat.Bites > bites0 ? "1" : "0");
                if (th != null) th.CancelStalk();
            }
            else
            {
                Combat.Reset();
                bool music = false;
                float hurtAt = -1f;
                System.Action<float, Vector3, string> onHurt = (d, f, s) => { if (hurtAt < 0f) hurtAt = Time.unscaledTime; };
                Combat.LocalHurt += onHurt;
                float t = 0f;
                while (t < 70f && Val("r3fbite") == null)
                {
                    t += Time.unscaledDeltaTime;
                    if (FaunaDangerMusic.AnyPlaying) music = true;
                    yield return null;
                }
                Combat.LocalHurt -= onHurt;
                Check(music, "guest: the danger music announced the shark");
                if (Val("r3fbite") == "1") Check(hurtAt > 0f && !pc.IsFainting, "guest: the bite arrived on the guest's machine (Puste " + F(Combat.Health, "0") + "), never death");
                else Info("the charge missed – no bite to check");
                Combat.Reset();
                pc.RefillAir();
            }
            yield return Sync("S32c", 60f);

            // ---- 3: a chest opened by the guest is open for the host (loot only for the opener)
            SeaChest chest = null;
            foreach (var c in SeaContent.Chests) if (c != null && c.chestId == "hr_ledge") chest = c;
            if (chest == null && SeaContent.Chests.Count > 1) chest = SeaContent.Chests[1];
            if (Check(chest != null, Role + ": the same chests on both machines (" + SeaContent.Chests.Count + ")"))
            {
                string cid = chest.chestId;
                long worth0 = SeaTreasure.ValuablesWorth;
                if (!isHost)
                {
                    chest.DebugOpen();
                    Check(SeaTreasure.IsOpened(cid) && SeaTreasure.ValuablesWorth > worth0, "guest: chest '" + cid + "' opened, loot in the guest's backpack");
                    SendVal("r3fchest", cid);
                    yield return Wait(2f);
                    Check(SeaTreasure.IsOpened(cid), "guest: still open after the host's snapshot");
                }
                else
                {
                    yield return WaitVal("r3fchest", 40f);
                    yield return WaitUntil(() => SeaTreasure.IsOpened(cid), 8f);
                    Check(SeaTreasure.IsOpened(cid), "host: the guest's chest is open for the host too");
                    Check(SeaTreasure.ValuablesWorth == worth0, "host: only the opener got the loot");
                }
            }
            pc.enabled = true;
            yield return Sync("S32d", 60f);
        }

        static NetCreatureProxy FindProxy(int netId)
        {
            var list = CreatureRegistry.Active;
            for (int i = 0; i < list.Count; i++)
                if (list[i] is NetCreatureProxy px && px != null && px.NetId == netId && px.IsAlive) return px;
            return null;
        }
    }
}
