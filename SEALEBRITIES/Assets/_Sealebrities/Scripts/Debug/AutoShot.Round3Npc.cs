using System.Collections;
using UnityEngine;

namespace Sealebrities
{
    /// Round-3 step 5 screenshots – the hermit camp and the NPCs in the new proportions. Files: r3n_*.png
    ///   r3n_camp_day / r3n_camp_fire (dusk, four at the fire) / r3n_camp_night (two in the hammocks)
    ///   r3n_hermit_<role> (portrait of each of the six), r3n_hermit_lineup (all six side by side on the camp floor)
    ///   r3n_horst_bench / r3n_horst_hammock, r3n_story_cast (Horst, Wanda, Dr. Fin, Toni, Chad, Brad side by side)
    ///   r3n_gesture_wheel (the wheel open at the cook)
    public sealed partial class AutoShot
    {
        IEnumerator ShotsRound3Npc()
        {
            yield return Guard(ShotR3NpcCamp(), "r3n_camp");
            yield return Guard(ShotR3NpcPortraits(), "r3n_hermits");
            yield return Guard(ShotR3NpcCast(), "r3n_cast");
            yield return Guard(ShotR3NpcHorst(), "r3n_horst");
            yield return Guard(ShotR3NpcWheel(), "r3n_wheel");
            if (pc != null) pc.enabled = true;
        }

        static NpcActor ShotNpc(string id) => NpcRegistry.Find(id) as NpcActor;

        static void FriendlyHermits()
        {
            StoryFlags.Set(StoryKeys.HermitCampFound);
            foreach (var id in NpcDatabase.Hermits) { var h = Hermits.GetOrAdd(id); if (h != null) { h.met = true; h.mood = 50; h.friend = true; } }
        }

        IEnumerator ShotR3NpcCamp()
        {
            if (!QuestSpots.IsRegistered("hermit_camp")) { Note("WARNING: r3n – no hermit camp"); yield break; }
            FriendlyHermits();
            Vector3 c = QuestSpots.Get("hermit_camp");
            SetTime(0.44f);
            yield return EnsureOnLand();
            yield return Wait(4f);
            FreeCamera(c + new Vector3(-9f, 7.5f, -9f), c + Vector3.up * 0.5f);
            yield return Wait(1.5f);
            yield return Capture("r3n_camp_day");
            SetTime(20.2f / 24f);
            yield return Wait(5f);
            if (!PropInfo.Spot("hermit_fire", out Vector3 fire)) fire = c;
            FreeCamera(fire + new Vector3(4.5f, 2.3f, -5f), fire + Vector3.up * 0.6f);
            yield return Wait(1.2f);
            yield return Capture("r3n_camp_fire");
            SetTime(23.5f / 24f);
            yield return Wait(4f);
            FreeCamera(c + new Vector3(-5f, 3.2f, 5f), c + Vector3.up * 0.6f);
            yield return Wait(1.2f);
            yield return Capture("r3n_camp_night");
            SetTime(0.44f);
        }

        IEnumerator ShotR3NpcPortraits()
        {
            FriendlyHermits();
            SetTime(0.44f);
            yield return Wait(3f);
            DiverLod.ForceLod0 = true;
            string[] role = { "leader", "smith", "hunter", "cook", "lookout", "gardener" };
            for (int i = 0; i < NpcDatabase.Hermits.Length; i++)
            {
                var a = ShotNpc(NpcDatabase.Hermits[i]);
                if (a == null || !a.Present) { Note("r3n: " + NpcDatabase.Hermits[i] + " not present"); continue; }
                Vector3 f = a.transform.forward;
                FreeCamera(a.Position + f * 2.3f + Vector3.up * 1.45f + a.transform.right * 0.5f, a.Position + Vector3.up * (a.Def.height * 0.62f));
                yield return Wait(0.8f);
                yield return Capture("r3n_hermit_" + role[i]);
            }
            // line-up: place all six side by side on the camp floor (host / offline – a screenshot run; schedules paused)
            NpcDirector.FreezeSchedules = true;
            Vector3 c = QuestSpots.Get("hermit_camp");
            Vector3 right = Vector3.right, fwd = Vector3.forward;
            for (int i = 0; i < NpcDatabase.Hermits.Length; i++)
            {
                var a = ShotNpc(NpcDatabase.Hermits[i]);
                if (a == null) continue;
                a.SetPresent(true);
                a.Place(c + right * ((i - 2.5f) * 0.95f), 180f);
                a.Motion = NpcMotion.Idle;
            }
            yield return Wait(0.3f);
            FreeCamera(c - fwd * 4.6f + Vector3.up * 1.4f, c + Vector3.up * 0.95f);
            yield return Wait(0.5f);
            yield return Capture("r3n_hermit_lineup");
            NpcDirector.FreezeSchedules = false;
            DiverLod.ForceLod0 = false;
        }

        IEnumerator ShotR3NpcCast()
        {
            // the speaking cast side by side on the beach in front of the hut (proportions next to each other)
            string[] cast = { NpcDatabase.Horst, NpcDatabase.Wanda, NpcDatabase.Fin, NpcDatabase.Toni, NpcDatabase.Chad, NpcDatabase.Brad };
            Vector3 c = QuestSpots.Get("horst_corner") + new Vector3(-6f, 0f, 4f);
            DiverLod.ForceLod0 = true;
            NpcDirector.FreezeSchedules = true;
            for (int i = 0; i < cast.Length; i++)
            {
                var a = ShotNpc(cast[i]);
                if (a == null) continue;
                a.SetPresent(true);
                a.Place(c + Vector3.right * ((i - 2.5f) * 0.95f), 180f);
                a.Motion = NpcMotion.Idle;
            }
            SetTime(0.45f);
            yield return Wait(0.5f);
            FreeCamera(c - Vector3.forward * 4.8f + Vector3.up * 1.4f, c + Vector3.up * 0.95f);
            yield return Wait(0.6f);
            yield return Capture("r3n_story_cast");
            DiverLod.ForceLod0 = false;
            NpcDirector.FreezeSchedules = false;
            for (int i = 0; i < cast.Length; i++)
            {
                // the visitors go back to their own schedules (they appear on their visit days only)
                var a = ShotNpc(cast[i]);
                if (a != null && cast[i] != NpcDatabase.Horst && cast[i] != NpcDatabase.Wanda) a.SetPresent(false);
            }
        }

        IEnumerator ShotR3NpcHorst()
        {
            var h = ShotNpc(NpcDatabase.Horst);
            if (h == null) yield break;
            SetTime(10f / 24f);
            yield return Wait(6f);
            FreeCamera(h.Position + new Vector3(3f, 1.8f, -2.5f), h.Position + Vector3.up * 0.8f);
            yield return Wait(1f);
            yield return Capture("r3n_horst_bench");
            SetTime(13f / 24f);
            yield return Wait(8f);
            FreeCamera(h.Position + new Vector3(2.4f, 1.7f, 1.6f), h.Position + Vector3.up * 0.3f);
            yield return Wait(1f);
            yield return Capture("r3n_horst_hammock");
            SetTime(0.45f);
        }

        IEnumerator ShotR3NpcWheel()
        {
            FriendlyHermits();
            SetTime(0.45f);
            yield return Wait(2f);
            var cook = ShotNpc(NpcDatabase.HCook);
            if (cook == null || !cook.Present || NpcInteract.I == null || pc == null) yield break;
            pc.enabled = true;
            Vector3 p = cook.Position + cook.transform.forward * 2f;
            p.y = WorldInfo.GroundHeight(p);
            pc.TeleportTo(p, Mathf.Atan2(cook.Position.x - p.x, cook.Position.z - p.z) * Mathf.Rad2Deg);
            yield return Wait(1f);
            // the wheel stays open only while G is held (release = choose)
            GameInput.SimPress(GameAction.Gestures);
            yield return Wait(0.3f);
            if (!NpcInteract.WheelOpen) NpcInteract.I.OpenWheel(cook);
            yield return Wait(0.6f);
            yield return Capture("r3n_gesture_wheel");
            NpcInteract.ResetSession();
            GameInput.SimRelease(GameAction.Gestures);
        }
    }
}
