using System.Collections;
using UnityEngine;

namespace Sealebrities
{
    /// Round-3 hermits in co-op (GDD §19 + §20 Q4–Q6, Q47; moods / fights / hand-ins replicate through the host):
    ///  S33a the GUEST gives the cook the sea sign + a gift → the host's mood state changes, the guest sees it back;
    ///  S33b the host's hunter attacks the GUEST → the bonks arrive on the guest's machine, the guest is tied up, wriggles
    ///       free; the host saw the hermit stand guard and let go;
    ///  S33c a guest backpack hand-in (A1 coconuts) counts on the host.
    /// Hooked at the end of StepRound3Core after the fauna checks.
    public sealed partial class AutoPlaytestMP
    {
        IEnumerator StepRound3Npc()
        {
            yield return Sync("S33a", 90f);
            if (pc == null) yield break;
            pc.enabled = true;
            Hands.StowAll();
            if (isHost)
            {
                SetTime(0.46f);
                StoryFlags.Set(StoryKeys.HermitCampFound);
                foreach (var hid in NpcDatabase.Hermits) { var h = Hermits.GetOrAdd(hid); if (h != null) { h.met = true; h.mood = 40; h.friend = true; } }
                var cookS = Hermits.GetOrAdd(NpcDatabase.HCook);
                if (cookS != null) { cookS.mood = -15; cookS.friend = false; cookS.signDay = -1; cookS.giftDay = -1; }
                NetState.ForceSnapshot();
            }
            yield return Sync("S33b", 30f);
            var cook = NpcRegistry.Find(NpcDatabase.HCook) as NpcActor;
            yield return WaitUntil(() => cook != null && cook.Present, 12f);
            if (!Check(cook != null && cook.Present, Role + ": the cook is in the camp")) yield break;
            Vector3 at = FeetNear(cook.Position, isHost ? 2.8f : 2.2f, isHost ? 30f : 210f);
            yield return Stand(at, YawTo(at, cook.Position));
            yield return AimAt(cook.Position + Vector3.up * 1.3f);
            yield return Sync("S33c", 30f);

            // ---- S33a: guest signs + gift → host mood
            if (!isHost)
            {
                yield return WaitUntil(() => Hermits.Mood(NpcDatabase.HCook) == -15, 6f);
                // final test: a sign while the cook still talks (his greeting bark when the guest walks up) is ignored by
                // design – wait for the end of his line (the conversation flag travels with the actor band)
                yield return WaitUntil(() => (cook.Flags & NpcActor.FlagConversation) == 0, 12f);
                yield return Wait(0.5f);
                int m0 = Hermits.Mood(NpcDatabase.HCook);
                NpcInteract.Perform(cook, HermitSign.Sea);
                yield return Wait(1.5f);
                Backpack.Add(ItemIds.RawFish, 1, false);
                bool gave = NpcDirector.LocalGift(cook, ItemIds.RawFish);
                Check(gave, "guest: gave the cook a raw fish from the GUEST's backpack");
                yield return WaitUntil(() => Hermits.Mood(NpcDatabase.HCook) > m0 + 20, 8f);
                Check(Hermits.Mood(NpcDatabase.HCook) > m0 + 20, "guest: the host's mood state came back with the snapshot (" + m0 + " → " + Hermits.Mood(NpcDatabase.HCook) + ")");
                SendVal("r3nsign", "1");
            }
            else
            {
                yield return WaitVal("r3nsign", 40f);
                Check(Hermits.Mood(NpcDatabase.HCook) > -15 + 20, "host: the guest's sea sign + gift raised the cook's mood (" + Hermits.Mood(NpcDatabase.HCook) + ")");
            }
            yield return Sync("S33d", 40f);

            // ---- S33b: the host's hunter attacks the guest → tied up on the guest's machine
            var hunter = NpcRegistry.Find(NpcDatabase.HHunter) as NpcActor;
            if (isHost)
            {
                var dir = NpcDirector.I;
                var hs = Hermits.GetOrAdd(NpcDatabase.HHunter);
                if (hs != null) hs.mood = -70;
                NpcDirector.TestFightsAllowed = true;
                bool started = dir != null && dir.DebugStartFight(NpcDatabase.HHunter, peerId);
                Check(started, "host: the hunter goes for the guest (" + peerId + ")");
                bool guarded = false;
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 60f && Val("r3nfree") == null)
                {
                    if (dir != null) { dir.DebugHermitState(NpcDatabase.HHunter, out _, out _, out int g, out _); if (g == peerId) guarded = true; }
                    yield return Wait(0.25f);
                }
                Check(guarded, "host: the hunter stood guard over the tied-up guest");
                yield return Wait(1.5f);
                if (dir != null) { dir.DebugHermitState(NpcDatabase.HHunter, out int ft, out _, out int g2, out _); Check(ft < 0 && g2 < 0, "host: after the wriggle the hunter let the guest go"); }
                NpcDirector.TestFightsAllowed = false;
                if (hs != null) hs.mood = 40;
                NetState.ForceSnapshot();
            }
            else
            {
                Combat.Reset();
                float hurt = 0f;
                System.Action<float, Vector3, string> onHurt = (d, f, s) => { if (s == NpcDatabase.HHunter) hurt += d; };
                Combat.LocalHurt += onHurt;
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 50f && !NpcInteract.TiedUp)
                {
                    if (hunter != null && FlatDist(pc.Root.position, hunter.Position) > 3.5f)
                    {
                        Vector3 f = FeetNear(hunter.Position, 1.4f, 0f);
                        yield return Stand(f, YawTo(f, hunter.Position));
                    }
                    yield return Wait(0.3f);
                }
                Combat.LocalHurt -= onHurt;
                Check(hurt > 0f, "guest: the host's hermit hits arrive on the guest's machine (Puste −" + F(hurt, "0") + ")");
                Check(NpcInteract.TiedUp && PlayerController.Restrained, "guest: knocked out → tied up, walking blocked");
                if (NpcInteract.TiedUp) yield return Shot("client_S33_tied_up");
                for (int i = 0; i < 24 && NpcInteract.TiedUp; i++) { GameInput.SimTap(i % 2 == 0 ? GameAction.Left : GameAction.Right); yield return Wait(0.05f); }
                for (int i = 0; i < 24 && NpcInteract.TiedUp; i++) NpcInteract.DebugWriggle(i % 2 == 0 ? -1 : 1);
                Check(!NpcInteract.TiedUp && !PlayerController.Restrained, "guest: wriggled free with A / D");
                Combat.Reset();
                SendVal("r3nfree", "1");
            }
            yield return Sync("S33e", 60f);

            // ---- S33c: a guest backpack hand-in (A1: coconuts for the cook) counts on the host
            const string qid = "A1";
            if (isHost)
            {
                var s = S;
                for (int i = s.sideQuests.Count - 1; i >= 0 && Quests.ActiveSideCount >= Quests.MaxActiveSide; i--)
                    if (s.sideQuests[i] != null && !s.sideQuests[i].done && !Quests.IsStoryChain(s.sideQuests[i].id)) Quests.Drop(s.sideQuests[i].id);
                if (Quests.Get(qid) != null) { s.sideQuests.Remove(Quests.Get(qid)); }
                Quests.Start(qid);
                FishMarket.AddCatch(new FishItem { kindId = "mackerel", weightKg = 1f, value = 30 });
                FishMarket.AddCatch(new FishItem { kindId = "mackerel", weightKg = 1f, value = 30 });
                NetState.ForceSnapshot();
                SendVal("r3nq", "1");
                yield return WaitPeer(() => { var q = Quests.Get(qid); return q != null && (q.done || q.progress >= 2); }, 40f);
                var qq = Quests.Get(qid);
                Check(qq != null && (qq.done || qq.progress >= 2), "host: the guest's coconuts from the backpack count (" + (qq != null ? (qq.done ? "done" : qq.progress + "/3") : "-") + ")");
            }
            else
            {
                yield return WaitVal("r3nq", 30f);
                yield return WaitUntil(() => Quests.Get(qid) != null, 6f);
                Backpack.Add(FoodDatabase.Coconut, 2, false);
                int c0 = Backpack.Count(FoodDatabase.Coconut);
                yield return AimAt(cook.Position + Vector3.up * 1.3f);
                Info("guest prompt at the cook: '" + InteractionPrompt.Text + "'");
                if (cook.CanInteract()) cook.Interact();
                yield return WaitUntil(() => Backpack.Count(FoodDatabase.Coconut) < c0, 4f);
                Check(Backpack.Count(FoodDatabase.Coconut) <= c0 - 2, "guest: E on the cook handed 2 coconuts from the backpack (" + c0 + " → " + Backpack.Count(FoodDatabase.Coconut) + ")");
            }
            yield return Sync("S33f", 40f);
            if (isHost && Quests.IsActive(qid)) Quests.Drop(qid);
            pc.enabled = true;
        }
    }
}
