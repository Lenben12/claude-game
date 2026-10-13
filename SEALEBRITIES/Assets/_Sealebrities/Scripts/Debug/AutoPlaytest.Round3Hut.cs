using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Sealebrities
{
    /// Round-3 step 3 – hut interior + furnishing persists in the save and syncs in co-op. Runs inside RunRound3Steps (-r3only).
    public sealed partial class AutoPlaytest
    {
        static bool SameColor(Color a, Color b) => Mathf.Abs(a.r - b.r) < 0.01f && Mathf.Abs(a.g - b.g) < 0.01f && Mathf.Abs(a.b - b.b) < 0.01f;

        static int CountNear<T>(Vector3 p, float r) where T : Component
        {
            int n = 0;
            foreach (var c in Object.FindObjectsByType<T>(FindObjectsSortMode.None))
                if (c != null && FlatDist(c.transform.position, p) < r) n++;
            return n;
        }

        IEnumerator StepR3HutDecor()
        {
            var gm = GameManager.I;
            if (pc == null || gm == null) yield break;
            pc.enabled = true;
            var d = PropHutDecor.Instance;
            Check(d != null, "the hut furnishing parts exist (PropHutDecor: wall paint, floor, rugs, curtains, decor)");
            if (d == null) yield break;
            Vector3 hut = PropHut.Origin;
            long money0 = S.money;
            var before = HutDecor.Copy(S.hut);

            // ---- interior built + key spots / interactables still there
            string[] spots = { "fridge", "locker", "golden_camera", "gus_letter", "water_filter", "grinder", "hut_roof", "hut_decor", "compressor" };
            var missing = new List<string>();
            foreach (var s in spots) if (!PropInfo.Spot(s, out _)) missing.Add(s);
            Check(missing.Count == 0, "hut spots present: " + string.Join(", ", spots) + (missing.Count > 0 ? " – MISSING " + string.Join(", ", missing) : ""));
            string[] sleep = { "hut_bed", "hut_sofa_bunk", "porch_hammock_south", "porch_hammock_north" };
            int foundSleep = 0;
            foreach (var s in sleep) foreach (var sp in PropInfo.SleepSpots) if (sp.id == s) { foundSleep++; break; }
            Check(foundSleep == sleep.Length, "bed, sofa bunk and both porch hammocks registered as sleep spots (" + foundSleep + "/" + sleep.Length + ")");
            int catalog = CountNear<PropHutCatalog>(hut, 8f), locker = CountNear<PropLocker>(hut, 8f), food = CountNear<PropFoodSpot>(hut, 8f),
                trophy = CountNear<PropTrophy>(hut, 8f), filter = CountNear<PropWaterFilter>(hut, 8f), grinder = CountNear<PropGrinder>(hut, 8f), readable = CountNear<PropReadable>(hut, 8f);
            Check(catalog == 1 && locker >= 1 && food >= 1 && trophy >= 1 && filter >= 1 && grinder >= 1 && readable >= 1,
                  "interactables in the hut: Einrichten " + catalog + ", locker " + locker + ", fridge " + food + ", trophy " + trophy + ", filter " + filter + ", grinder " + grinder + ", letter " + readable);
            int decorBuilt = 0;
            foreach (var it in HutDecor.Items) if (d.ItemBuilt(it.id)) decorBuilt++;
            Check(decorBuilt == HutDecor.Items.Length && d.wall[0] != null && d.floor[0] != null && d.curtain != null,
                  "customizable parts built: " + decorBuilt + "/" + HutDecor.Items.Length + " decor items, wall + floor tone renderers, curtains");

            // the player spawn stays free (≥ 0.6 m), the nightstand is solid now
            Vector3 spawn = new Vector3(WorldLayout.PlayerSpawn.x, PropInfo.HutFloorY, WorldLayout.PlayerSpawn.z);
            var hits = Physics.OverlapCapsule(spawn + Vector3.up * 0.65f, spawn + Vector3.up * 1.6f, 0.6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            var blocking = new List<string>();
            foreach (var c in hits)
                if (c != null && c.GetComponentInParent<PlayerController>() == null && !(c is CharacterController)) blocking.Add(c.name);
            Check(blocking.Count == 0, "PlayerSpawn has ≥ 0.6 m clearance" + (blocking.Count > 0 ? " – blocked by " + string.Join(", ", blocking) : ""));
            Vector3 ns = new Vector3(WorldLayout.Bed.x + 0.88f, PropInfo.HutFloorY + 0.3f, hut.z + 4.685f);
            Check(Physics.CheckSphere(ns, 0.08f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), "the nightstand beside the bed has a collider");
            Vector3 helmet = PropInfo.Spot("golden_camera", out var gc) ? gc : hut;
            Check(Physics.Raycast(helmet + Vector3.up * 0.05f, Vector3.down, 0.2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore),
                  "the golden camera stands on the trophy shelf (shelf collider right below it)");

            // ---- surfboard rack: tails on the ground north of the rack, decks against the upper bar
            Check(PropHut.RackBoardTails.Count == 3, "3 boards on the surfboard rack");
            for (int i = 0; i < PropHut.RackBoardTails.Count; i++)
            {
                Vector3 t = PropHut.RackBoardTails[i], c = PropHut.RackBoardContacts[i];
                float g = WorldInfo.GroundHeight(t);
                Check(Mathf.Abs(t.y - g) < 0.15f && t.z > PropHut.RackCenter.z + 0.15f && c.y > t.y + 1.0f,
                      "rack board " + (i + 1) + ": tail on the ground (" + F(t.y - g, "0.00") + " m), " + F(t.z - PropHut.RackCenter.z, "0.00") + " m north of the bars, leaning on the bar at " + F(c.y - PropHut.RackCenter.y, "0.00") + " m");
            }

            // ---- apply choices through the public API → materials / objects change live
            bool ok = HutDecor.SetWall(3) & HutDecor.SetFloor(2) & HutDecor.SetRug(2) & HutDecor.SetCurtains(1)
                      & HutDecor.SetPlaced("palm", true) & HutDecor.SetPlaced("lights", true);
            yield return Wait(0.5f);
            Check(ok, "wall 3, floor 2, rug 2, curtains 1, palm + string lights accepted");
            Check(SameColor(d.WallColor, HutDecor.Walls[3].a), "walls painted " + HutDecor.Walls[3].Name + " (#" + ColorUtility.ToHtmlStringRGB(d.WallColor) + ")");
            Check(SameColor(d.FloorColor, HutDecor.Floors[2].a), "floor " + HutDecor.Floors[2].Name + " (#" + ColorUtility.ToHtmlStringRGB(d.FloorColor) + ")");
            Check(d.ActiveRug == 2, "rug " + HutDecor.Rugs[2].Name + " shown (active " + d.ActiveRug + ")");
            Check(d.CurtainsShown && SameColor(d.CurtainColor, HutDecor.Curtains[1].a), "curtains " + HutDecor.Curtains[1].Name);
            Check(d.ItemShown("palm") && d.ItemShown("lights") && !d.ItemShown("aquarium"), "palm + string lights placed, the aquarium is not");
            HutDecor.SetRug(HutDecor.RugNone);
            HutDecor.SetCurtains(HutDecor.CurtainsNone);
            HutDecor.SetPlaced("lights", false);
            yield return Wait(0.4f);
            Check(d.ActiveRug == HutDecor.RugNone && !d.CurtainsShown && !d.ItemShown("lights"), "no rug, no curtains, lights put away");
            HutDecor.SetRug(2);
            HutDecor.SetCurtains(1);
            yield return Stand(PropHut.ToWorld(new Vector3(3.3f, 0.06f, 0.9f)), 270f);
            yield return AimAt(PropHut.ToWorld(new Vector3(-3.5f, 0.9f, -0.5f)));
            yield return Wait(0.6f);
            yield return Shot("63_hut_decor");

            // ---- the „Einrichten“ screen opens at the catalogue and blocks gameplay
            PropHutCatalog cat = null;
            foreach (var c in Object.FindObjectsByType<PropHutCatalog>(FindObjectsSortMode.None)) if (c != null) cat = c;
            if (cat != null)
            {
                Check(!string.IsNullOrEmpty(cat.GetPrompt()), "catalogue prompt: " + cat.GetPrompt());
                cat.Interact();
                yield return Wait(0.6f);
                Check(UIState.DecorOpen && UIState.BlocksGameplay, "E at the catalogue opens „Einrichten“ (blocks gameplay)");
                yield return Shot("63_einrichten_walls");
                UIHutDecor.DebugOpen(4);
                yield return Wait(0.4f);
                yield return Shot("63_einrichten_deko");
                UIHutDecor.Close();
                yield return Wait(0.2f);
                Check(!UIState.DecorOpen && !UIState.BlocksGameplay, "the screen closes again");
            }
            else Check(false, "no PropHutCatalog found");

            // ---- save → load round trip (the JSON a save writes, kept in memory)
            string json = gm.SnapshotJson();
            HutDecor.DebugReplace(HutDecor.Default());
            yield return Wait(0.4f);
            Check(SameColor(d.WallColor, HutDecor.Walls[0].a) && d.ActiveRug == 0 && !d.ItemShown("palm"), "reset to the standard look before loading");
            bool loaded = gm.LoadFromJson(json);
            yield return Wait(1.2f);
            d = PropHutDecor.Instance;
            var h = S.hut;
            Check(loaded && h != null && h.wall == 3 && h.floor == 2 && h.rug == 2 && h.curtains == 1 && h.placed.Contains("palm") && !h.placed.Contains("lights"),
                  "save → load keeps the furnishing (wall " + (h != null ? h.wall : -1) + ", floor " + (h != null ? h.floor : -1) + ", rug " + (h != null ? h.rug : -1) + ", curtains " + (h != null ? h.curtains : -1) + ", placed " + (h != null ? string.Join(",", h.placed) : "-") + ")");
            Check(d != null && SameColor(d.WallColor, HutDecor.Walls[3].a) && d.ActiveRug == 2 && d.ItemShown("palm"), "… and the hut shows it after loading");

            // ---- old saves: the standard hut; broken values are repaired
            GameState v5 = null, broken = null;
            try
            {
                v5 = JsonUtility.FromJson<GameState>("{\"version\":5,\"saveSeed\":77,\"day\":3}");
                v5.EnsureValid();
                broken = JsonUtility.FromJson<GameState>("{\"version\":6,\"saveSeed\":78,\"hut\":{\"wall\":99,\"floor\":-3,\"rug\":42,\"curtains\":7,\"owned\":[\"palm\",\"marlin\",\"marlin\",\"bogus\"],\"placed\":[\"bogus\",\"palm\",\"palm\"]}}");
                broken.EnsureValid();
            }
            catch (System.Exception e) { Check(false, "normalisation threw " + e.Message); }
            if (v5 != null)
                Check(v5.version == GameState.CurrentVersion && GameState.CurrentVersion >= 6 && v5.hut != null && v5.hut.wall == 0 && v5.hut.floor == 0 && v5.hut.rug == 0 && v5.hut.curtains == 0
                      && v5.hut.owned.Count == 0 && v5.hut.placed.Count == 0, "a v5 save loads as v" + v5.version + " with the standard hut");
            if (broken != null)
                Check(broken.hut.wall == 0 && broken.hut.floor == 0 && broken.hut.rug == 0 && broken.hut.curtains == 0 && broken.hut.owned.Count == 1 && broken.hut.owned[0] == "marlin"
                      && broken.hut.placed.Count == 1 && broken.hut.placed[0] == "palm", "broken furnishing values repaired (owned " + string.Join(",", broken.hut.owned) + ", placed " + string.Join(",", broken.hut.placed) + ")");

            // ---- buying decor costs crew money; too little money / locked items cannot be placed
            var noBuy = HutDecor.Copy(S.hut);
            noBuy.owned.Remove("marlin"); noBuy.placed.Remove("marlin");
            noBuy.owned.Remove("aquarium"); noBuy.placed.Remove("aquarium");
            noBuy.placed.Remove("shells");
            HutDecor.DebugReplace(noBuy);
            var marlin = HutDecor.Item("marlin");
            S.money = 5000;
            bool bought = HutDecor.Buy("marlin");
            yield return Wait(0.4f);
            Check(bought && S.money == 5000 - marlin.price && HutDecor.IsOwned("marlin") && HutDecor.IsPlaced("marlin") && d.ItemShown("marlin"),
                  "bought the marlin for " + Loc.Money(marlin.price) + " (money " + S.money + "), it hangs on the wall");
            S.money = 100;
            bool cheap = HutDecor.Buy("aquarium");
            Check(!cheap && S.money == 100 && !HutDecor.IsOwned("aquarium"), "100 $ do not buy the aquarium (" + Loc.Money(HutDecor.Item("aquarium").price) + ")");
            Check(!HutDecor.SetPlaced("aquarium", true) && !HutDecor.IsPlaced("aquarium"), "a decor item that was not bought cannot be placed");
            // find-locked: the shell garland needs 3 golden shells
            var shells = S.collected.FindAll(id => id != null && id.StartsWith(Collectibles.Id(CollectibleKind.GoldenShell, ""), System.StringComparison.Ordinal));
            foreach (var id in shells) S.collected.Remove(id);
            Check(!HutDecor.IsAvailable("shells") && !HutDecor.SetPlaced("shells", true), "the shell garland is locked with 0/3 shells (" + HutDecor.LockHint(HutDecor.Item("shells")) + ")");
            string[] test = { Collectibles.Id(CollectibleKind.GoldenShell, "r3h_a"), Collectibles.Id(CollectibleKind.GoldenShell, "r3h_b"), Collectibles.Id(CollectibleKind.GoldenShell, "r3h_c") };
            S.collected.AddRange(test);
            Check(HutDecor.IsAvailable("shells") && HutDecor.SetPlaced("shells", true), "3 shells found → the garland can be put up");
            foreach (var id in test) S.collected.Remove(id);
            S.collected.AddRange(shells);
            if (!S.achievements.Contains("f10k")) Check(!HutDecor.SetPlaced("finbutton", true), "the Fin button needs the 10.000-follower achievement");

            // ---- restore
            S.money = money0;
            GameEvents.RaiseMoneyChanged(S.money);
            HutDecor.DebugReplace(before);
            yield return Wait(0.3f);
        }
    }
}
