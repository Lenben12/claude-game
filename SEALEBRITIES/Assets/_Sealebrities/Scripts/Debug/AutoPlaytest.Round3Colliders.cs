using System.Collections;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Sealebrities
{
    /// Round-3 step 3 – solid prop colliders (reef rocks, palms, rocks, buoys, wreck parts, helicopter). Runs inside RunRound3Steps (-r3only).
    public sealed partial class AutoPlaytest
    {
        const int SolidMask = Physics.DefaultRaycastLayers;

        /// Nearest enabled, non-trigger collider whose object name starts with `name` (null = none within maxDist).
        static Collider R3FindCol(string name, Vector3 near, float maxDist)
        {
            Collider best = null;
            float bd = maxDist * maxDist;
            foreach (var c in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
            {
                if (c == null || !c.enabled || c.isTrigger || !c.gameObject.activeInHierarchy) continue;
                if (!c.gameObject.name.StartsWith(name, System.StringComparison.Ordinal)) continue;
                float d = (c.bounds.center - near).sqrMagnitude;
                if (d < bd) { bd = d; best = c; }
            }
            return best;
        }

        static bool R3Solid(Vector3 p, float r = 0.08f) => Physics.CheckSphere(p, r, SolidMask, QueryTriggerInteraction.Ignore);

        IEnumerator StepR3Colliders()
        {
            if (pc == null) yield break;
            pc.enabled = true;
            Info("solid prop colliders added this world build: " + PropSolid.Count + " (PropSolid helpers + vehicle boxes)");
            Check(Physics.GetIgnoreLayerCollision(0, PropSolid.BoatPassLayer) && !Physics.GetIgnoreLayerCollision(PlayerController.PlayerLayer, PropSolid.BoatPassLayer),
                  "boat-pass layer " + PropSolid.BoatPassLayer + ": the boat (Default) passes, the diver (layer " + PlayerController.PlayerLayer + ") collides");

            // ---- palms on the hut beach: capsules along the leaning trunk
            Vector3 hut = WorldInfo.PlayerSpawn != Vector3.zero ? WorldInfo.PlayerSpawn : WorldLayout.PlayerSpawn;
            yield return Stand(SnapDown(hut + Vector3.up * 2f), 90f);
            var palm = R3FindCol("PalmTrunk", hut, 120f) as CapsuleCollider;
            if (Check(palm != null, "a palm trunk collider near the hut"))
            {
                float tilt = Vector3.Angle(palm.transform.up, Vector3.up);
                Vector3 top = palm.transform.position + palm.transform.up * (palm.height * 0.5f - palm.radius);
                Check(tilt > 1.5f && R3Solid(top), "the trunk capsule follows the lean (" + F(tilt, "0.0") + "° off vertical, solid at " + V(top) + ")");
            }

            // ---- beach: the grill is solid (trigger kept), the surfboards stand in the sand
            var grill = Object.FindFirstObjectByType<PropGrill>();
            if (grill != null)
            {
                Vector3 gp = grill.transform.position;
                Check(R3Solid(gp - Vector3.up * 0.3f) && grill.GetComponent<Collider>() != null && grill.GetComponent<Collider>().isTrigger,
                      "the grill grate is solid and keeps its interaction trigger (" + V(gp) + ")");
            }
            int boards = 0, buried = 0;
            foreach (var c in Object.FindObjectsByType<BoxCollider>(FindObjectsSortMode.None))
            {
                if (c == null || c.gameObject.name != "Surfboard") continue;
                boards++;
                Vector3 tail = c.transform.position + c.transform.rotation * new Vector3(0f, 0f, -1f);
                float g = WorldInfo.GroundHeight(tail);
                if (tail.y < g - 0.12f) buried++;
                else Info("surfboard tail " + F(tail.y - g, "0.00") + " m above the ground at " + V(tail));
            }
            Check(boards > 0 && buried == boards, "stuck surfboards: " + buried + "/" + boards + " tails buried in the sand");

            // ---- dock: lantern posts and bollards
            var post = R3FindCol("LanternPost", WorldLayout.DockStart, 200f);
            var bollard = R3FindCol("Bollard", WorldLayout.DockEnd, 200f);
            Check(post != null && bollard != null && R3Solid(post.bounds.center) && R3Solid(bollard.bounds.center), "dock lantern posts and bollards are solid");

            // ---- lagoon: sandbar to stand on, buoys solid + moving with the waves
            var bar = R3FindCol("SandbarCol", hut, 400f);
            if (bar != null)
            {
                Vector3 bc = bar.bounds.center;
                bool hit = Physics.Raycast(new Vector3(bc.x, Waves.SeaLevel + 3f, bc.z), Vector3.down, out RaycastHit h, 6f, SolidMask, QueryTriggerInteraction.Ignore);
                Check(hit && h.collider == bar && h.point.y > Waves.SeaLevel - 0.05f, "one can stand on a sandbar (top " + (hit ? F(h.point.y - Waves.SeaLevel, "0.00") : "–") + " m above sea level)");
            }
            else Info("no sandbar in this layout");
            var buoy = R3FindCol("BuoySolid", WorldLayout.DockEnd, 600f);
            if (Check(buoy != null, "a solid buoy near the dock"))
            {
                var rb = buoy.attachedRigidbody;
                Vector3 b0 = buoy.bounds.center;
                yield return Wait(1.2f);
                Vector3 b1 = buoy.bounds.center;
                Check(rb != null && rb.isKinematic && buoy.gameObject.layer == PropSolid.BoatPassLayer,
                      "the buoy collider rides a kinematic body (moved " + F((b1 - b0).magnitude, "0.000") + " m with the waves) on the boat-pass layer");
            }

            // ---- house reef: reef rocks and coral colonies are solid, sea grass stays free
            var reef = DiveSites.Get("house_reef");
            if (reef != null)
            {
                Vector3 rc = reef.center;
                Vector3 floor = FindSeafloor(rc, reef.radius * 0.6f, -9f, -3.5f);
                yield return DiveTo(floor + Vector3.up * 2.5f, floor);
                pc.enabled = true;
                yield return Wait(1.2f);
                var rock = R3FindCol("ReefRock", floor, 80f);
                if (Check(rock != null, "reef rock colliders exist (nearest " + (rock != null ? V(rock.bounds.center) : "none") + ")"))
                {
                    Check(rock is MeshCollider mc && mc.convex, "the reef rock is a convex hull");
                    // a diver-sized capsule swept at the rock's upper half hits it (it used to pass straight through)
                    Vector3 rcen = rock.bounds.center;
                    float yTest = Mathf.Max(rcen.y, WorldInfo.GroundHeight(rcen) + 0.15f);
                    Vector3 from = new Vector3(rcen.x - rock.bounds.extents.x - 2f, yTest, rcen.z);
                    bool hitRock = Physics.CapsuleCast(from, from + Vector3.up * 0.6f, 0.3f, Vector3.right, out RaycastHit rh, rock.bounds.size.x + 4f, SolidMask, QueryTriggerInteraction.Ignore);
                    Check(hitRock && rh.collider != null && !(rh.collider is TerrainCollider),
                          "a diver capsule swept into the reef rock is stopped (" + (hitRock ? rh.collider.gameObject.name + " after " + F(rh.distance, "0.0") + " m" : "no hit") + ")");
                }
                int lumps = 0;
                SphereCollider oneLump = null;
                foreach (var h in Object.FindObjectsByType<SphereCollider>(FindObjectsSortMode.None))
                    if (h != null && h.gameObject.name.StartsWith("Solid", System.StringComparison.Ordinal) && FlatDist(h.transform.TransformPoint(h.center), rc) < reef.radius) { lumps++; oneLump = h; }
                Check(lumps > 50, "coral colonies with a rounded collider in the house reef: " + lumps);
                if (oneLump != null)
                {
                    Vector3 lc = oneLump.transform.TransformPoint(oneLump.center);
                    Check(R3Solid(lc + Vector3.up * (oneLump.radius * 0.5f)) && (oneLump.gameObject.layer == PropSolid.BoatPassLayer) == (lc.y + oneLump.radius > Waves.SeaLevel - PropSolid.BoatDraft),
                          "a coral lump is solid, on the " + (oneLump.gameObject.layer == PropSolid.BoatPassLayer ? "boat-pass" : "default") + " layer for its top at " + F(lc.y + oneLump.radius, "0.0") + " m");
                }
                int grass = 0, grassFree = 0;
                foreach (var hab in PropInfo.Habitats)
                {
                    if (hab.kind != HabitatKind.SeaGrass || hab.zone != "house_reef") continue;
                    grass++;
                    if (!Physics.CheckSphere(hab.position + Vector3.up * 0.45f, 0.3f, SolidMask, QueryTriggerInteraction.Ignore)) grassFree++;
                    if (grass >= 20) break;
                }
                Check(grass == 0 || grassFree >= grass * 0.8f, "sea grass stays passable: " + grassFree + "/" + grass + " meadows free at diver height");
                yield return Shot("61_reef_collision");
            }

            // ---- kelp forest: stalks and fronds stay passable (mid-water free), boulders solid
            var kelp = DiveSites.Get("kelp_forest");
            if (kelp != null)
            {
                Vector3 kc = kelp.center;
                float g = WorldInfo.GroundHeight(kc);
                yield return DiveTo(new Vector3(kc.x, Mathf.Min(g + 6f, Waves.SeaLevel - 3f), kc.z), kc + Vector3.forward * 5f + Vector3.up * g);
                pc.enabled = true;
                yield return Wait(1.5f);
                int free = 0;
                for (int i = 0; i < 8; i++)
                {
                    float a = i * Mathf.PI / 4f;
                    Vector3 p = kc + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 7f;
                    float pg = WorldInfo.GroundHeight(p);
                    Vector3 lo = new Vector3(p.x, pg + 4.6f, p.z), hi = new Vector3(p.x, Mathf.Min(pg + 8f, Waves.SeaLevel - 2.5f), p.z);
                    if (hi.y <= lo.y) hi = lo + Vector3.up * 0.1f;
                    if (!Physics.CheckCapsule(lo, hi, 0.35f, SolidMask, QueryTriggerInteraction.Ignore)) free++;
                }
                Check(free >= 7, "kelp stays passable: " + free + "/8 mid-water columns in the dense stand are free");
                var boulder = R3FindCol("KelpBoulder", kc, 200f);
                var stone = R3FindCol("KelpStone", kc, 200f);
                Check(boulder != null && stone != null, "kelp boulders and the bigger stones are solid");
                // swim into the boulder: the diver stops at its surface
                if (boulder != null)
                {
                    Vector3 bc = boulder.bounds.center;
                    float bg = WorldInfo.GroundHeight(bc);
                    Vector3 target = new Vector3(bc.x, Mathf.Max(bc.y, bg + 1.2f), bc.z);
                    // final test: in the late-game run a kelp current once carried the diver 5 m away – a second try from
                    // the other side (the current only decides whether the diver reaches the rock, never the collision)
                    bool crossed = false;
                    float moved = 0f;
                    for (int attempt = 0; attempt < 2; attempt++)
                    {
                        Vector3 side = attempt == 0 ? Vector3.right : Vector3.left;
                        Vector3 start = target - side * (boulder.bounds.extents.x + 2.5f);
                        pc.TeleportTo(start - Vector3.up * 0.9f, attempt == 0 ? 90f : -90f);
                        pc.RefillAir();
                        yield return Wait(0.6f);
                        yield return AimAt(target);
                        float d0 = FlatDist(pc.transform.position, target);
                        Vector3 body0 = pc.transform.position + Vector3.up * 0.9f;
                        GameInput.SimPress(GameAction.Forward);
                        // closest approach (with the late-game fins the diver hits the rock and slides along it – the end
                        // point can lie farther away than the start without ever passing through)
                        float closest = d0, tw = 0f;
                        while (tw < 3.0f) { tw += Time.deltaTime; closest = Mathf.Min(closest, FlatDist(pc.transform.position, target)); yield return null; }
                        GameInput.SimRelease(GameAction.Forward);
                        yield return Wait(0.3f);
                        Vector3 body = pc.transform.position + Vector3.up * 0.9f;
                        Vector3 path = body - body0;
                        // the way from the start to where the diver ended must not cross the boulder's surface
                        crossed |= path.sqrMagnitude > 1e-4f && boulder.Raycast(new Ray(body0, path.normalized), out RaycastHit bh, path.magnitude);
                        moved = d0 - closest;
                        if (moved > 0.5f) break;
                    }
                    Check(!crossed && moved > 0.5f,
                          "swimming into a boulder stops at its surface (moved " + F(moved, "0.0") + " m, crossed " + crossed + ")");
                    yield return Shot("61_kelp_boulder");
                }
            }

            // ---- wreck: barrels, the lifeboat and the slam door leaf
            var wreck = DiveSites.Get("wreck");
            Vector3 wc = PropInfo.WreckCenter != Vector3.zero ? PropInfo.WreckCenter : (wreck != null ? wreck.center : Vector3.zero);
            if (wc != Vector3.zero)
            {
                yield return DiveTo(wc + Vector3.up * 6f, wc);
                pc.enabled = true;
                yield return Wait(1.5f);
                var barrel = R3FindCol("Barrel", wc, 80f);
                var life = R3FindCol("RowingBoatCol", wc, 80f);
                Check(barrel != null && life != null, "wreck barrels and the lifeboat are solid");
                var slam = Object.FindFirstObjectByType<PropSlamDoor>();
                Check(slam != null && slam.leaf != null && slam.leaf.GetComponent<BoxCollider>() != null && !slam.leaf.GetComponent<BoxCollider>().isTrigger,
                      "the slamming store door leaf is solid");
            }

            // ---- vehicles built off-world: chopper, yacht, catamaran
            Vector3 lab = new Vector3(2600f, 260f, 2600f);
            var heli = new GameObject("R3HeliTest");
            heli.transform.position = lab;
            HeliModel.Build(heli.transform);
            var yacht = NpcVehicleBuilder.Yacht();
            yacht.transform.position = lab + new Vector3(30f, 0f, 0f);
            var cat = NpcVehicleBuilder.Catamaran();
            cat.transform.position = lab + new Vector3(60f, 0f, 0f);
            Physics.SyncTransforms();
            Check(R3Solid(lab + new Vector3(0f, 0.1f, 0.6f)) && R3Solid(lab + new Vector3(0.95f, -1.35f, 0f), 0.03f) && R3Solid(lab + new Vector3(0f, 0.75f, -5f)),
                  "the parked chopper is solid: fuselage, skids, tail boom");
            Check(!R3Solid(lab + new Vector3(3.5f, 1.75f, 0.1f), 0.1f), "the main rotor (3.1 m above the pad) has no collider");
            Check(R3Solid(lab + new Vector3(30f, 2.6f, 0.5f)) && R3Solid(lab + new Vector3(30f, 4.1f, 1.2f)), "the yacht's cabin decks are solid (old box ended at 2.0 m)");
            Check(R3Solid(lab + new Vector3(60f, 2.2f, 0.8f)) && !R3Solid(lab + new Vector3(60f, -0.4f, 0f), 0.2f), "the catamaran cabin is solid, the gap between the hulls stays open");
            Object.Destroy(heli);
            Object.Destroy(yacht);
            Object.Destroy(cat);
            yield return Wait(0.2f);
            GameInput.SimClear();
        }
    }
}
