using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Sealebrities
{
    /// Round-3 step 2 (Player + characters, GDD §19): sleeping IN the hammock / bed (first person + avatar), the dock
    /// ladder climbed by the player, the new characters (every option builds, triangle budget, line-up shots) and the
    /// first-person hands with the new items (stick swing, spear thrust, speargun aim, bottle + drinking).
    /// Runs inside RunRound3Steps (-r3only).
    public sealed partial class AutoPlaytest
    {
        readonly List<GameObject> r3Temp = new List<GameObject>();

        void ClearR3Temp()
        {
            for (int i = 0; i < r3Temp.Count; i++) if (r3Temp[i] != null) Object.Destroy(r3Temp[i]);
            r3Temp.Clear();
        }

        DiverPuppet R3Diver(DiverLook look, Vector3 feet, float yaw)
        {
            var build = DiverModels.Build;
            if (build == null) return null;
            GameObject go = null;
            try { go = build(look); }
            catch (Exception e) { Info("diver build threw: " + e.Message); }
            if (go == null) return null;
            go.transform.SetPositionAndRotation(feet, Quaternion.Euler(0f, yaw, 0f));
            if (Refs.WorldRoot != null) go.transform.SetParent(Refs.WorldRoot, true);
            DiverLod.Force(go, true);
            r3Temp.Add(go);
            return go.GetComponent<DiverPuppet>();
        }

        static DiverLook R3Look(Color suit, int hat, int hair, int hairColor, int beard, int nose, int build, int height, int acc, int skin, int eyes = 0, int brows = 0, int pattern = 0)
        {
            return new DiverLook
            {
                suit = suit, cuff = new Color(0f, 0f, 0f, 0f), hat = hat, hair = hair, hairColor = hairColor, beard = beard, nose = nose,
                build = build, height = height, accessory = acc, skin = skin, eyes = eyes, brows = brows, pattern = pattern, mouth = 0,
                snorkel = true, lookVersion = DiverLook.CurrentVersion, tankTier = 2, finTier = 1
            };
        }

        /// LOD0 bounds of a built diver (the LOD proxies are excluded).
        static Bounds R3Bounds(GameObject go)
        {
            var b = new Bounds(go.transform.position, Vector3.zero);
            bool any = false;
            var lod = go.GetComponentInChildren<LODGroup>(true);
            Renderer[] rs = lod != null && lod.lodCount > 0 ? lod.GetLODs()[0].renderers : go.GetComponentsInChildren<Renderer>(false);
            for (int i = 0; i < rs.Length; i++)
            {
                var r = rs[i];
                if (r == null || !r.gameObject.activeInHierarchy || !r.enabled) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return b;
        }

        // ================================================================== 56 sleeping IN the hammock / bed
        IEnumerator StepR3SleepPose()
        {
            if (pc == null || cam == null) yield break;
            pc.enabled = true;
            SetTime(0.8f);
            var spots = PropInfo.SleepSpots;
            int bed = -1, hammock = -1;
            for (int i = 0; i < spots.Count; i++)
            {
                if (spots[i].kind == SleepSpotInfo.Bed && bed < 0) bed = i;
                if (spots[i].kind != SleepSpotInfo.Hammock) continue;
                bool porch = spots[i].id != null && spots[i].id.StartsWith("porch", StringComparison.Ordinal);
                bool havePorch = hammock >= 0 && spots[hammock].id != null && spots[hammock].id.StartsWith("porch", StringComparison.Ordinal);
                if (hammock < 0 || (porch && !havePorch)) hammock = i;
            }
            Check(bed >= 0 && hammock >= 0, spots.Count + " sleep spots (bed #" + bed + ", hammock #" + hammock + ")");
            if (bed < 0 || hammock < 0) yield break;
            Check(spots[bed].hip != Vector3.zero && spots[hammock].hip != Vector3.zero, "bed + hammock carry the exact pelvis point (PropBed.RegisterBed / RegisterHammock)");
            var ccomp = pc.GetComponent<CharacterController>();
            int[] which = { hammock, bed };
            for (int w = 0; w < which.Length; w++)
            {
                int idx = which[w];
                var s = spots[idx];
                string name = s.kind == SleepSpotInfo.Hammock ? "hammock" : "bed";
                DiverPuppet.LiePose(s, out Vector3 root, out float yaw, out float hr, out float fr, out bool ham);
                Vector3 stand = s.stand != Vector3.zero ? s.stand : root + Vector3.right;
                yield return Stand(stand, s.standYaw);

                // ---- first person: the diver's root on the spot, the eye above the mattress / cloth, looking up
                pc.DebugLie(idx);
                yield return Wait(0.9f);
                float dRoot = Vector3.Distance(pc.Root.position, root);
                Check(pc.IsLyingDown && dRoot < 0.05f && ccomp != null && !ccomp.enabled,
                      name + ": lying down puts the diver's root on the spot's pelvis point (" + F(dRoot, "0.00") + " m), controller off");
                Vector3 camP = cam.transform.position;
                float above = camP.y - root.y;
                bool free = !Physics.CheckSphere(camP, 0.035f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                Check(above > 0.15f && above < (ham ? 0.8f : 0.55f) && free, name + ": first-person eye " + F(above, "0.00") + " m above the surface, not inside anything (" + free + ")");
                Check(cam.transform.forward.y > 0.45f, name + ": looking up while lying (forward.y " + F(cam.transform.forward.y, "0.00") + ")");
                Metric("sleep." + name, "headRise " + F(hr, "0") + "°, feetRise " + F(fr, "0") + "°, eye +" + F(above, "0.00") + " m");
                yield return Shot("56_sleep_fp_" + name);

                // ---- the co-op avatar on the same spot (what a friend sees – images 4 / 5 of the feedback)
                var look = Settings.Look;
                look.tankTier = 2;
                var pup = R3Diver(look, root, yaw);
                if (pup != null)
                {
                    pup.SetMotion(MotionState.Lie, 0f, 0f, HoldItem.None, HoldItem.None, false);
                    pup.SetLiePose(hr, fr, ham);
                    yield return Wait(1.6f);
                    Bounds b = R3Bounds(pup.gameObject);
                    Vector3 fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                    float along = Vector3.Dot(pup.Head.position - root, fwd);
                    Check(b.min.y > root.y - (ham ? 0.14f : 0.1f), name + ": nothing sinks through (lowest point " + F(b.min.y - root.y, "0.00") + " m vs the surface)");
                    Check(b.max.y < root.y + (ham ? 0.75f : 0.55f), name + ": the body lies (highest point +" + F(b.max.y - root.y, "0.00") + " m)");
                    Check(along < -0.35f && pup.Head.position.y > root.y + 0.05f, name + ": the head rests at the pillow end, higher than the pelvis (" + F(along, "0.00") + " m along, +" + F(pup.Head.position.y - root.y, "0.00") + " m)");
                    // side view from the free side
                    Vector3 side = Quaternion.Euler(0f, yaw + 90f, 0f) * Vector3.forward;
                    Vector3 mid = root + Vector3.up * 0.35f;
                    if (Physics.Raycast(mid, side, 2.2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) side = -side;
                    pc.enabled = false;
                    Pose(mid + side * 2.0f + Vector3.up * 0.55f - fwd * 0.1f, root + Vector3.up * 0.25f);
                    yield return Wait(0.3f);
                    yield return Shot("56_sleep_avatar_" + name);
                    pc.enabled = true;
                    ClearR3Temp();
                }

                // ---- getting up: back on the floor at the stand point
                pc.DebugLie(-1);
                yield return Wait(0.8f);
                float dStand = FlatDist(pc.Root.position, stand);
                Check(!pc.IsLyingDown && ccomp != null && ccomp.enabled && dStand < 0.5f, name + ": getting up stands the diver next to it (" + F(dStand, "0.00") + " m from the stand point, controller on)");
            }
        }

        // ================================================================== 57 the dock ladder (climbed by the player)
        IEnumerator StepR3DockLadder()
        {
            if (pc == null) yield break;
            pc.enabled = true;
            SetTime(0.5f);
            Hands.StowAll();
            Vector3 db = PropInfo.DockLadderBottom;
            if (db == Vector3.zero) { Info("no dock ladder published – skipped"); yield break; }
            int li = -1;
            for (int i = 0; i < PropInfo.Ladders.Count; i++)
                if (FlatDist(PropInfo.Ladders[i].bottom, db) < 0.8f) { li = i; break; }
            Check(li >= 0, "the dock ladder is in PropInfo.Ladders (#" + li + ")");
            if (li < 0) yield break;
            var lad = PropInfo.Ladders[li];
            Check(FlatDist(lad.bottom, db) > 0.3f, "its bottom is the rail foot (" + F(FlatDist(lad.bottom, db), "0.00") + " m inside DockLadderBottom) – the hands grip the rungs");
            Vector3 o = lad.outward;
            // swim next to it, looking AWAY from the dock
            Vector3 swim = new Vector3(db.x + o.x * 0.3f + 0.35f, Waves.SeaLevel - PlayerController.EyeHeight + 0.15f, db.z + o.z * 0.3f + 0.35f);
            pc.TeleportTo(swim, Mathf.Atan2(o.x, o.z) * Mathf.Rad2Deg);
            pc.RefillAir();
            yield return Wait(1.3f);
            Check(pc.IsInWater, "swimming beside the dock ladder");
            GameInput.SimTap(GameAction.Jump);
            yield return Wait(0.7f);
            Check(pc.OnGenericLadder, "Space beside the dock ladder grabs it – even looking away (no scripted auto-climb any more)");
            if (!pc.OnGenericLadder) yield break;
            float y0 = pc.LadderRootY;
            yield return Wait(1.0f);
            Check(pc.OnGenericLadder && pc.LadderRootY - y0 < 0.05f, "nothing climbs by itself (moved " + F(pc.LadderRootY - y0, "0.00") + " m)");
            Vector3 rail = lad.bottom + o * 0.4f;
            float off = FlatDist(pc.Root.position, rail);
            Check(off < 0.08f, "the climber hangs 0.4 m in front of the rungs (" + F(off, "0.00") + " m off)");
            GameInput.SimPress(GameAction.Forward);
            float t0 = Time.realtimeSinceStartup, maxJump = 0f;
            Vector3 lastP = pc.Root.position;
            bool shot = false;
            while (pc.OnGenericLadder && Time.realtimeSinceStartup - t0 < 20f)
            {
                Vector3 pNow = pc.Root.position;
                float dtf = Mathf.Max(Time.deltaTime, 0.004f);
                maxJump = Mathf.Max(maxJump, Vector3.Distance(pNow, lastP) / dtf);
                lastP = pNow;
                if (!shot && pc.LadderRootY > y0 + 0.9f && !pc.LadderMantling) { shot = true; yield return Shot("57_dock_ladder_fp"); }
                yield return null;
            }
            GameInput.SimRelease(GameAction.Forward);
            yield return Wait(0.4f);
            float dTop = Vector3.Distance(pc.Root.position, lad.top);
            Check(!pc.OnGenericLadder && !pc.IsInWater && dTop < 1.2f, "W climbs up and steps off onto the planks (" + F(dTop, "0.00") + " m from the top stand, " + F(Time.realtimeSinceStartup - t0) + " s)");
            Check(maxJump < 8f, "smooth: the fastest root speed while climbing was " + F(maxJump, "0.0") + " m/s (no teleport)");

            // the co-op avatar on that ladder (Ladder motion state with alternating hands and feet)
            var pup = R3Diver(R3Look(DiverStyle.PlayerColors[1], 16, 0, 2, 0, 1, 0, 0, 0, 1), rail + Vector3.up * 0.8f, Mathf.Atan2(-o.x, -o.z) * Mathf.Rad2Deg);
            if (pup != null)
            {
                float ph = 0f, t = 0f;
                bool s2 = false;
                Vector3 side = Vector3.Cross(Vector3.up, o).normalized;
                pc.enabled = false;
                while (t < 1.6f)
                {
                    t += Time.deltaTime;
                    ph = Mathf.Repeat(ph + Time.deltaTime * 0.9f, 1f);
                    pup.transform.position = rail + Vector3.up * (0.8f + t * 0.25f);
                    pup.SetMotion(MotionState.Ladder, 0.5f, ph, HoldItem.None, HoldItem.None, false);
                    Pose(rail + o * 2.2f + side * 1.4f + Vector3.up * (1.9f + t * 0.25f), rail + Vector3.up * (1.6f + t * 0.25f));
                    if (!s2 && t > 1.1f) { s2 = true; yield return Shot("57_ladder_avatar"); }
                    yield return null;
                }
                pc.enabled = true;
                ClearR3Temp();
            }
        }

        // ================================================================== 58 characters
        IEnumerator StepR3Characters()
        {
            var build = DiverModels.Build;
            Check(build != null, "DiverModels.Build installed");
            if (build == null) yield break;
            var baseLook = Settings.Look;
            baseLook.lookVersion = DiverLook.CurrentVersion;
            baseLook.tankTier = 5; baseLook.finTier = 3; baseLook.snorkel = true; baseLook.lampOnStrap = true; baseLook.cameraOnStrap = true;
            string[] kinds = { "build", "height", "hair", "hairColor", "beard", "nose", "accessory", "eyes", "brows", "hat", "pattern", "skin" };
            int[] counts = { CosmeticsDatabase.BuildCount, CosmeticsDatabase.HeightCount, CosmeticsDatabase.HairCount, CosmeticsDatabase.HairColors.Length,
                             CosmeticsDatabase.BeardCount, CosmeticsDatabase.NoseCount, CosmeticsDatabase.AccessoryCount, CosmeticsDatabase.EyesCount,
                             CosmeticsDatabase.BrowsCount, CosmeticsDatabase.HatCount, CosmeticsDatabase.PatternCount, CosmeticsDatabase.SkinTones.Length };
            int built = 0, failed = 0, maxTris = 0, minTris = int.MaxValue, expected = 0;
            string worst = "", worstParts = "";
            for (int k = 0; k < kinds.Length; k++)
            {
                expected += counts[k];
                for (int i = 0; i < counts[k]; i++)
                {
                    var l = baseLook;
                    switch (k)
                    {
                        case 0: l.build = i; break;
                        case 1: l.height = i - 2; break;
                        case 2: l.hair = i; break;
                        case 3: l.hairColor = i; break;
                        case 4: l.beard = i; break;
                        case 5: l.nose = i; break;
                        case 6: l.accessory = i; break;
                        case 7: l.eyes = i; break;
                        case 8: l.brows = i; break;
                        case 9: l.hat = i; break;
                        case 10: l.pattern = i; break;
                        default: l.skin = i; break;
                    }
                    GameObject go = null;
                    try { go = build(l); }
                    catch (Exception e) { failed++; Info("look build threw (" + kinds[k] + " " + i + "): " + e.Message); }
                    if (go == null) continue;
                    built++;
                    int tris = Triangles(go);
                    if (tris > maxTris) { maxTris = tris; worst = kinds[k] + " " + i; worstParts = TopParts(go, 8); }
                    if (tris < minTris) minTris = tris;
                    Object.Destroy(go);
                    if ((built & 7) == 0) yield return null;
                }
            }
            // the heaviest combination: crown + curls + full beard + necklace + trimix twin tanks + scooter fins + both strap items
            {
                var l = baseLook;
                l.hat = 12; l.hair = 2; l.beard = 3; l.accessory = 1; l.eyes = 5; l.brows = 1; l.nose = 1;
                var go = build(l);
                if (go != null)
                {
                    int tris = Triangles(go);
                    Check(tris <= 10500, "the heaviest combination (crown, curls, full beard, necklace, lashes, trimix, both strap items): " + tris + " triangles (≤ 10,500)");
                    Metric("diver3.heaviest", tris + " – " + TopParts(go, 8));
                    var g = go.GetComponentInChildren<LODGroup>(true);
                    Check(g != null && g.lodCount >= 3, "LODGroup on the round-3 diver (" + (g != null ? g.lodCount : 0) + " levels: " + LastLodInfo + ")");
                    Metric("diver3.lods", LastLodInfo);
                    Object.Destroy(go);
                }
            }
            Check(failed == 0 && built == expected, built + "/" + expected + " round-3 looks built (body, height, hair, hair colour, beard, nose, extras, eyes, brows, hats, patterns, skins), " + failed + " failed");
            Check(maxTris <= 9000, "LOD0 budget: " + minTris + "–" + maxTris + " triangles incl. trimix tanks + strap items (≤ 9,000; worst " + worst + ")");
            Metric("diver3.triangles", minTris + "–" + maxTris);
            Metric("diver3.trianglesByPart", worstParts);

            // line-up on the dock: six different divers, then a close-up of the faces
            if (pc == null || cam == null) yield break;
            SetTime(0.47f);
            Vector3 end = DockEnd();
            Vector3 deck = SnapDown(new Vector3(end.x - 5f, end.y + 1f, end.z), 2f, 4f);
            var looks = new[]
            {
                R3Look(DiverStyle.PlayerColors[0], 0, 0, 0, 0, 0, 0, 0, 0, 0),
                R3Look(DiverStyle.PlayerColors[1], 15, 2, 4, 0, 4, 1, -1, 3, 4, 5, 4),
                R3Look(DiverStyle.PlayerColors[2], 0, 5, 5, 0, 1, 0, 1, 4, 6, 0, 0, 11),
                R3Look(DiverStyle.PlayerColors[3], 5, 6, 1, 3, 2, 2, 2, 2, 3, 0, 1),
                R3Look(CosmeticsDatabase.SuitColors[15], 17, 1, 9, 0, 3, 3, 0, 6, 8, 0, 2, 8),
                R3Look(CosmeticsDatabase.SuitColors[13], 19, 3, 6, 4, 0, 0, -2, 7, 7, 0, 0, 9),
            };
            Vector3 camPos = deck + new Vector3(0f, 1.55f, -3.3f);
            for (int i = 0; i < looks.Length; i++)
            {
                Vector3 feet = deck + new Vector3((i - 2.5f) * 0.78f, 0f, 0f);
                float yaw = Mathf.Atan2(camPos.x - feet.x, camPos.z - feet.z) * Mathf.Rad2Deg;
                var p = R3Diver(looks[i], feet, yaw);
                if (p == null) continue;
                p.SetMotion(MotionState.Idle, 0f, i * 0.2f, i == 3 ? HoldItem.Spear : HoldItem.None, i == 1 ? HoldItem.Flashlight : HoldItem.None, false);
                p.SetExpression(i % 3 == 0 ? (int)SelfieExpression.Grin : 0);
                p.Phase = i * 1.1f;
            }
            pc.enabled = false;
            Pose(camPos, deck + Vector3.up * 1.15f);
            yield return Wait(1.6f);
            yield return Shot("58_characters_lineup");
            Pose(deck + new Vector3(-0.4f, 1.72f, -1.25f), deck + new Vector3(-0.4f, 1.62f, 0f));
            yield return Wait(0.5f);
            yield return Shot("58_characters_faces");
            ClearR3Temp();

            // a running and a diving avatar (walk / run / swim motion set)
            var runner = R3Diver(looks[1], deck + new Vector3(-9f, 0f, 0f), 90f);
            if (runner != null)
            {
                float t = 0f, ph = 0f;
                bool s1 = false;
                while (t < 1.5f)
                {
                    t += Time.deltaTime;
                    ph = Mathf.Repeat(ph + Time.deltaTime * 2.1f, 1f);
                    Vector3 pos = deck + new Vector3(-9f + t * 6f, 0f, 0f);
                    runner.transform.position = pos;
                    runner.SetMotion(MotionState.Run, 6f, ph, HoldItem.None, HoldItem.None, false);
                    Pose(pos + new Vector3(0f, 1.1f, -3.3f), pos + Vector3.up * 1.0f);
                    if (!s1 && t > 1.0f) { s1 = true; yield return Shot("58_run_avatar"); }
                    yield return null;
                }
                ClearR3Temp();
            }
            pc.enabled = true;

            // the character screen with the new tab layout (live preview)
            if (UIModule.DebugOpenCharacter(15))
            {
                yield return Wait(1.8f);
                yield return Shot("58_character_screen");
                UIModule.DebugOpenCharacter(-2);
                yield return Wait(0.6f);
            }
        }

        // ================================================================== 59 hands + held items
        IEnumerator StepR3HeldItems()
        {
            if (pc == null) yield break;
            pc.enabled = true;
            SetTime(0.5f);
            Vector3 end = DockEnd();
            yield return Stand(SnapDown(new Vector3(end.x - 8f, end.y + 1f, end.z), 2f, 4f), 90f);
            Hands.StowAll();
            Backpack.Add(ItemIds.SpearT1, 1, false);
            Backpack.Add(ItemIds.SpearT2, 1, false);
            Backpack.Add(ItemIds.Speargun, 1, false);
            Backpack.Add(ItemIds.BottleFresh, 1, false);
            var hands = Object.FindFirstObjectByType<PlayerHands>();
            Check(hands != null, "first-person hands present");
            if (hands == null) yield break;

            // stick: rest + a swing caught mid-strike
            yield return R3Hold(0, ItemIds.Stick);
            Check(R3Shown(hands, "Stick"), "the stick is in the main hand (rest pose)");
            yield return Shot("59_hands_stick");
            GameInput.SimTap(GameAction.Use);
            yield return Wait(0.14f);
            yield return Shot("59_hands_stick_swing");
            Check(Time.time - PlayerTools.LastSwingTime < 1.5f, "LMB swings the stick");
            yield return Wait(0.6f);

            // spears: wooden point vs steel blade + a thrust
            yield return R3Hold(1, ItemIds.SpearT1);
            Check(R3Shown(hands, "Spear") && R3Shown(hands, "WoodTip") && !R3Shown(hands, "SteelTip"), "wooden spear: fire-hardened point");
            yield return Shot("59_hands_spear_wood");
            yield return R3Hold(1, ItemIds.SpearT2);
            Check(R3Shown(hands, "SteelTip") && !R3Shown(hands, "WoodTip"), "steel spear: steel blade");
            GameInput.SimTap(GameAction.Use);
            yield return Wait(0.17f);
            yield return Shot("59_hands_spear_thrust");
            yield return Wait(0.6f);

            // speargun: two hands, aimed with RMB
            yield return R3Hold(2, ItemIds.Speargun);
            Check(R3Shown(hands, "Speargun") && R3MittenCount(hands) >= 2, "speargun: both hands on it (grip + barrel)");
            yield return Shot("59_hands_speargun");
            GameInput.SimPress(GameAction.Aim);
            yield return Wait(0.45f);
            yield return Shot("59_hands_speargun_aim");
            GameInput.SimRelease(GameAction.Aim);
            yield return Wait(0.3f);

            // a bottle upright in the fist, then drinking it
            yield return R3Hold(3, ItemIds.BottleFresh);
            yield return Shot("59_hands_bottle");
            Survival.Set(70f, 30f);
            GameInput.SimTap(GameAction.Use);
            yield return Wait(0.95f);
            yield return Shot("59_hands_drink");
            yield return Wait(1.6f);
            Hands.StowAll();
        }

        IEnumerator R3Hold(int slot, string id)
        {
            Hands.Assign(slot, id, Hand.Right);
            yield return Wait(0.1f);
            if (Hands.ActiveSlot(Hand.Right) != slot) Hands.Select(slot);
            yield return Wait(0.6f);
        }

        static bool R3Shown(PlayerHands hands, string node)
        {
            var all = hands.GetComponentsInChildren<Transform>(false);
            for (int i = 0; i < all.Length; i++) if (all[i].name == node && all[i].gameObject.activeInHierarchy) return true;
            return false;
        }

        static int R3MittenCount(PlayerHands hands)
        {
            int n = 0;
            var rs = hands.GetComponentsInChildren<Renderer>(false);
            for (int i = 0; i < rs.Length; i++) if (rs[i] != null && rs[i].name == "Mitten" && rs[i].enabled) n++;
            return n;
        }
    }
}
