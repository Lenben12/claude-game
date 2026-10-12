using System.Collections;
using UnityEngine;

namespace Sealebrities
{
    /// Round-3 step 2 screenshots (Player + characters, GDD §19): the new divers (line-up + faces), divers lying IN the
    /// porch hammock and the hut bed, a diver on the dock ladder, first person with the stick and the aimed speargun.
    /// Files: r3_*.png.
    public sealed partial class AutoShot
    {
        static DiverLook R3Look(Color suit, int hat, int hair, int hairColor, int beard, int nose, int build, int height, int acc, int skin, int brows = 0, int pattern = 0)
        {
            return new DiverLook
            {
                suit = suit, cuff = new Color(0f, 0f, 0f, 0f), hat = hat, hair = hair, hairColor = hairColor, beard = beard, nose = nose,
                build = build, height = height, accessory = acc, skin = skin, brows = brows, pattern = pattern,
                snorkel = true, lookVersion = DiverLook.CurrentVersion, tankTier = 2, finTier = 1
            };
        }

        IEnumerator ShotsRound3Player()
        {
            yield return Guard(ShotR3Lineup(), "r3_characters");
            yield return Guard(ShotR3Sleep(), "r3_sleep");
            yield return Guard(ShotR3LadderAvatar(), "r3_ladder_avatar");
            yield return Guard(ShotR3Hands(), "r3_hands");
            ClearTemp();
            if (pc != null) pc.enabled = true;
        }

        IEnumerator ShotR3Lineup()
        {
            SetTime(0.47f);
            yield return EnsureOnLand();
            Vector3 end = WorldInfo.DockEndPosition != Vector3.zero ? WorldInfo.DockEndPosition : WorldLayout.DockEnd + Vector3.up * 1.3f;
            Vector3 deck = new Vector3(end.x - 5f, end.y - 0.05f, end.z);
            Vector3 camPos = deck + new Vector3(0f, 1.55f, -3.3f);
            var looks = new[]
            {
                R3Look(DiverStyle.PlayerColors[0], 0, 0, 0, 0, 0, 0, 0, 0, 0),
                R3Look(DiverStyle.PlayerColors[1], 15, 2, 4, 0, 4, 1, -1, 3, 4, 4),
                R3Look(DiverStyle.PlayerColors[2], 0, 5, 5, 0, 1, 0, 1, 4, 6, 0, 11),
                R3Look(DiverStyle.PlayerColors[3], 5, 6, 1, 3, 2, 2, 2, 2, 3, 1),
                R3Look(CosmeticsDatabase.SuitColors[15], 17, 1, 9, 0, 3, 3, 0, 6, 8, 2, 8),
                R3Look(CosmeticsDatabase.SuitColors[13], 19, 3, 6, 4, 0, 0, -2, 7, 7, 0, 9),
            };
            for (int i = 0; i < looks.Length; i++)
            {
                Vector3 feet = deck + new Vector3((i - 2.5f) * 0.78f, 0f, 0f);
                float yaw = Mathf.Atan2(camPos.x - feet.x, camPos.z - feet.z) * Mathf.Rad2Deg;
                var p = BuildDiver(looks[i], feet, yaw);
                if (p == null) continue;
                DiverLod.Force(p.gameObject, true);
                p.SetMotion(MotionState.Idle, 0f, i * 0.2f, i == 3 ? HoldItem.Spear : HoldItem.None, i == 1 ? HoldItem.Flashlight : HoldItem.None, false);
                p.SetExpression(i % 3 == 0 ? (int)SelfieExpression.Grin : 0);
                p.Phase = i * 1.1f;
            }
            FreeCamera(camPos, deck + Vector3.up * 1.15f);
            yield return Wait(1.8f);
            yield return Capture("r3_characters_lineup");
            Pose(deck + new Vector3(-0.4f, 1.72f, -1.25f), deck + new Vector3(-0.4f, 1.62f, 0f));
            yield return Wait(0.5f);
            yield return Capture("r3_characters_faces");
            ClearTemp();
        }

        IEnumerator ShotR3Sleep()
        {
            SetTime(0.8f);
            yield return EnsureOnLand();
            var spots = PropInfo.SleepSpots;
            int shots = 0;
            for (int i = 0; i < spots.Count && shots < 2; i++)
            {
                var s = spots[i];
                bool wantHammock = shots == 0;
                if ((s.kind == SleepSpotInfo.Hammock) != wantHammock) continue;
                if (wantHammock && (s.id == null || !s.id.StartsWith("porch", System.StringComparison.Ordinal))) continue;
                DiverPuppet.LiePose(s, out Vector3 root, out float yaw, out float hr, out float fr, out bool ham);
                var p = BuildDiver(R3Look(DiverStyle.PlayerColors[1], 0, 3, 5, 0, 1, 0, 0, 0, 2), root, yaw);
                if (p == null) continue;
                DiverLod.Force(p.gameObject, true);
                p.SetMotion(MotionState.Lie, 0f, 0f, HoldItem.None, HoldItem.None, false);
                p.SetLiePose(hr, fr, ham);
                Vector3 fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                Vector3 side = Quaternion.Euler(0f, yaw + 90f, 0f) * Vector3.forward;
                Vector3 mid = root + Vector3.up * 0.35f;
                if (Physics.Raycast(mid, side, 2.2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) side = -side;
                FreeCamera(mid + side * 2f + Vector3.up * 0.6f - fwd * 0.1f, root + Vector3.up * 0.25f);
                yield return Wait(1.8f);
                yield return Capture(ham ? "r3_sleep_hammock" : "r3_sleep_bed");
                ClearTemp();
                shots++;
                i = -1;   // look for the bed next
            }
            if (shots < 2) Note("WARNING: r3 sleep shots: only " + shots + " of 2 spots found");
        }

        IEnumerator ShotR3LadderAvatar()
        {
            SetTime(0.5f);
            yield return EnsureOnLand();
            Vector3 db = PropInfo.DockLadderBottom;
            int li = -1;
            for (int i = 0; i < PropInfo.Ladders.Count; i++)
            {
                Vector3 b = PropInfo.Ladders[i].bottom;
                if (db != Vector3.zero && new Vector2(b.x - db.x, b.z - db.z).sqrMagnitude < 0.64f) { li = i; break; }
            }
            if (li < 0) { Note("WARNING: no dock ladder for r3_ladder_avatar"); yield break; }
            var lad = PropInfo.Ladders[li];
            Vector3 o = lad.outward;
            Vector3 rail = lad.bottom + o * 0.4f;
            var p = BuildDiver(R3Look(DiverStyle.PlayerColors[0], 16, 0, 2, 0, 1, 0, 0, 0, 1), rail + Vector3.up * 1.0f, Mathf.Atan2(-o.x, -o.z) * Mathf.Rad2Deg);
            if (p == null) yield break;
            DiverLod.Force(p.gameObject, true);
            Vector3 side = Vector3.Cross(Vector3.up, o).normalized;
            float t = 0f, ph = 0f;
            bool shot = false;
            while (t < 1.6f)
            {
                t += Time.deltaTime;
                ph = Mathf.Repeat(ph + Time.deltaTime * 0.9f, 1f);
                p.transform.position = rail + Vector3.up * (1.0f + t * 0.25f);
                p.SetMotion(MotionState.Ladder, 0.5f, ph, HoldItem.None, HoldItem.None, false);
                FreeCamera(rail + o * 2.2f + side * 1.4f + Vector3.up * (2.1f + t * 0.25f), rail + Vector3.up * (1.8f + t * 0.25f));
                if (!shot && t > 1.1f) { shot = true; yield return Capture("r3_ladder_avatar"); }
                yield return null;
            }
            ClearTemp();
        }

        IEnumerator ShotR3Hands()
        {
            SetTime(0.5f);
            if (pc == null) yield break;
            yield return EnsureOnLand();
            Vector3 end = WorldInfo.DockEndPosition != Vector3.zero ? WorldInfo.DockEndPosition : WorldLayout.DockEnd + Vector3.up * 1.3f;
            pc.enabled = true;
            pc.TeleportTo(new Vector3(end.x - 8f, end.y + 0.05f, end.z), 90f);
            yield return Wait(0.8f);
            Backpack.Add(ItemIds.Speargun, 1, false);
            Hands.StowAll();
            Hands.Assign(0, ItemIds.Stick, Hand.Right);
            Hands.Select(0);
            yield return Wait(0.8f);
            yield return Capture("r3_hands_stick");
            Hands.Assign(2, ItemIds.Speargun, Hand.Right);
            Hands.Select(2);
            yield return Wait(0.6f);
            GameInput.SimPress(GameAction.Aim);
            yield return Wait(0.5f);
            yield return Capture("r3_hands_speargun_aim");
            GameInput.SimRelease(GameAction.Aim);
            Hands.StowAll();
        }
    }
}
