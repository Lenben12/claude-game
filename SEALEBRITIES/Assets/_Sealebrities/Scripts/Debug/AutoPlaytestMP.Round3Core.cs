using System.Collections;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Sealebrities
{
    /// Round-3 core co-op check (Testversion 5, GDD §19): every player has an OWN backpack, hotbar and hunger / thirst;
    /// a campfire lit by the guest burns for the host too; a map piece found by the host is gone for the guest (and only
    /// the finder gets the pouch copy); a daily pickup taken by the guest is gone on the host; the avatar shows the
    /// guest's stick.
    public sealed partial class AutoPlaytestMP
    {
        IEnumerator StepRound3Core()
        {
            yield return Sync("S30a", 120f);
            if (pc == null) yield break;
            pc.enabled = true;
            yield return Stand(isHost ? HostSpot : ClientSpot, 90f);

            // ---- own backpacks
            int stones = isHost ? 2 : 5;
            int stone0 = Backpack.Count(ItemIds.Stone);
            Backpack.Add(ItemIds.Stone, stones, false);
            SendVal("stones", Backpack.Count(ItemIds.Stone).ToString(Inv));
            yield return WaitVal("stones", 30f);
            yield return Wait(1.5f);   // a host snapshot passes
            Check(Backpack.Count(ItemIds.Stone) == stone0 + stones, Role + ": own backpack untouched by the other player (" + Backpack.Count(ItemIds.Stone) + " stones, the other has " + (Val("stones") ?? "-") + ")");

            // ---- hunger / thirst per player
            if (isHost) Survival.Set(30f, 30f);
            yield return Wait(1.5f);
            if (!isHost) Check(Survival.Hunger > 90f && Survival.Thirst > 90f, "the host's hunger is not the guest's (guest " + F(Survival.Hunger, "0") + " / " + F(Survival.Thirst, "0") + ")");
            else Check(Survival.Hunger < 35f, "host hungry (" + F(Survival.Hunger, "0") + ")");
            yield return Sync("S30b", 60f);

            // ---- shared campfire: the guest lights it
            PropCampfire fire = null;
            float best = float.MaxValue;
            foreach (var f in Object.FindObjectsByType<PropCampfire>(FindObjectsSortMode.None)) { float d = FlatDist(f.transform.position, WorldLayout.Hut); if (d < best) { best = d; fire = f; } }
            Check(fire != null, "a campfire spot on both machines");
            if (fire != null)
            {
                if (!isHost)
                {
                    Backpack.Add(ItemIds.Wood, Survival.CampfireWood, false);
                    bool lit = Survival.TryLightCampfire(fire.id);
                    Check(lit && Survival.CampfireLit(fire.id), "guest lit '" + fire.id + "' (optimistic)");
                    SendVal("fire", fire.id);
                    yield return Wait(2f);
                    Check(Survival.CampfireLit(fire.id), "still burning after the host's snapshot");
                }
                else
                {
                    yield return WaitVal("fire", 40f);
                    yield return WaitUntil(() => Survival.CampfireLit(fire.id), 8f);
                    Check(Survival.CampfireLit(fire.id), "the guest's campfire burns on the host too (" + F(Survival.CampfireHoursLeft(fire.id), "0.0") + " h)");
                }
            }
            yield return Sync("S30c", 60f);

            // ---- a map piece found by the host
            int piece = 2;
            int pouch0 = Backpack.Count(ItemIds.MapPiece);
            if (isHost)
            {
                bool got = MapPieces.Collect(piece);
                Check(got && Backpack.Count(ItemIds.MapPiece) == pouch0 + 1, "host found map piece " + (piece + 1) + " (pouch " + Backpack.Count(ItemIds.MapPiece) + ")");
                SendVal("piece", "1");
            }
            else
            {
                yield return WaitVal("piece", 40f);
                yield return WaitUntil(() => MapPieces.IsFound(piece), 8f);
                Check(MapPieces.IsFound(piece), "the host's map piece is found for the guest too (" + MapPieces.Found + "/" + MapPieces.Count + ")");
                Check(Backpack.Count(ItemIds.MapPiece) == pouch0, "only the finder got the pouch copy");
            }
            yield return Sync("S30d", 60f);

            // ---- a daily pickup taken by the guest
            PropLoot loot = null;
            foreach (var l in Object.FindObjectsByType<PropLoot>(FindObjectsSortMode.None))
                if (l != null && l.dropped == null && !string.IsNullOrEmpty(l.lootId) && Loot.Available(l.lootId)) { loot = l; break; }
            if (loot == null) Info("no daily pickup left");
            else if (!isHost)
            {
                string id = loot.lootId;
                int have = Backpack.Count(loot.itemId);
                bool took = Loot.TryTake(id, loot.itemId, loot.count);
                Check(took && Backpack.Count(loot.itemId) == have + loot.count, "guest took '" + id + "' into its backpack");
                SendVal("loot", id);
            }
            else
            {
                yield return WaitVal("loot", 40f);
                string id = Val("loot");
                yield return WaitUntil(() => !Loot.Available(id), 8f);
                Check(!Loot.Available(id), "the guest's pickup '" + id + "' is gone on the host until tomorrow");
            }
            yield return Sync("S30e", 60f);

            // ---- the guest holds its stick: the host sees it on the avatar
            if (!isHost)
            {
                Hands.Hold(ItemIds.Stick, Hand.Right);
                yield return Wait(0.5f);
                Check(Hands.Active(Hand.Right) == HoldItem.Stick, "guest holds the stick");
                SendVal("stick", "1");
                yield return Wait(3f);
                Hands.StowAll();
            }
            else
            {
                yield return WaitVal("stick", 30f);
                var a = PeerAvatar();
                yield return WaitUntil(() => { var pa = PeerAvatar(); return pa != null && pa.RightItem == HoldItem.Stick; }, 6f);
                a = PeerAvatar();
                Check(a != null && a.RightItem == HoldItem.Stick, "the guest's avatar holds the stick (" + (a != null ? a.RightItem.ToString() : "no avatar") + ")");
                if (a != null) { yield return AimAt(a.HeadPosition); yield return Shot("host_S30_guest_stick"); }
            }
            Survival.Set(Survival.Max, Survival.Max);
            yield return Sync("S30f", 60f);
            yield return StepRound3HutDecor();   // round 3 step 3: shared hut furnishing (AutoPlaytestMP.Round3Hut.cs)
            yield return StepRound3Fauna();      // round 3 step 4: guest spear fishing, shark bite on the guest, shared chest (AutoPlaytestMP.Round3Fauna.cs)
            yield return StepRound3Npc();        // round 3 step 5: hermit moods, the tie-up of a guest, a guest backpack hand-in (AutoPlaytestMP.Round3Npc.cs)
            yield return StepRound3UI();         // round 3 step 6: shared map pins, the passenger bench, anchor only by the driver (AutoPlaytestMP.Round3UI.cs)
            yield return StepRound3Slots();      // round 3 step 6b: only the host invites / saves, guest photos per world (AutoPlaytestMP.Round3Slots.cs)
        }
    }
}
