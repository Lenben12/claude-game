using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Round 3 (GDD §20 Q19/Q20 + coordinator: "the most important thing in Testversion 5 is more content in the sea"):
    /// places the new under-water content – deterministic (fixed seeds + the terrain), so every co-op machine builds
    /// the same sea:
    ///  * 4 small wrecks between the dive sites (sunken sailboat, crashed seaplane, fishing trawler with an enterable
    ///    wheelhouse, a pirate ship) – each announces itself when found and hides a treasure chest,
    ///  * 13 more treasure chests (house reef, lagoon, kelp, the MS Clickbait's hold + sand, blue drop, trench, Fog Isle,
    ///    the volcano's lava tube and the monster's sinkhole) – loot by depth / danger (SeaTreasure),
    ///  * the 10 curiosities / memes of the Sealog page „Kuriositäten“ (SeaMemeActor).
    /// Spots are searched around anchor points for a seabed of the right depth, gentle slope and free space; content is
    /// switched off beyond its activation range (PropSiteActivation). Called by UnderwaterDecor after the island props.
    public static class SeaContent
    {
        /// What was placed (tests / AutoShot): id → world position.
        public static readonly Dictionary<string, Vector3> Placed = new Dictionary<string, Vector3>();
        public static readonly List<SeaChest> Chests = new List<SeaChest>();
        public static readonly List<SeaMemeActor> Memes = new List<SeaMemeActor>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Placed.Clear(); Chests.Clear(); Memes.Clear(); }

        static readonly Vector3 HR = new Vector3(188f, 0f, 38f), KF = new Vector3(-340f, 0f, 300f), BD = new Vector3(-620f, 0f, -540f),
            DT = new Vector3(760f, 0f, 660f), FI = new Vector3(-60f, 0f, 640f);

        public static IEnumerator Build(Transform parent)
        {
            Placed.Clear();
            Chests.Clear();
            Memes.Clear();
            var root = PropUtil.WorldRoot("SeaContent", parent);
            var dir = root.gameObject.AddComponent<SeaContentDirector>();

            Vector3 wreckC = PropInfo.WreckCenter != Vector3.zero ? PropInfo.WreckCenter : DiveSites.Get("wreck").center;
            wreckC.y = 0f;

            // ---------------------------------------------------------------- the four wrecks
            Vector3 sail = Wreck(root, dir, "sailboat", "Versunkenes Segelboot", "Sunken sailboat", new Vector3(40f, 0f, 250f), 80f, 12f, 26f, 11,
                (solid) => { var t = SeaModels.Sailboat(solid, out var c); return (t, c, Vector3.zero); }, 2, false);
            if (PropRun.Due()) yield return null;
            Vector3 plane = Wreck(root, dir, "seaplane", "Abgestürztes Wasserflugzeug", "Crashed seaplane", new Vector3(320f, 0f, -150f), 90f, 14f, 30f, 22,
                (solid) => { var t = SeaModels.Seaplane(solid, out var c); return (t, c, Vector3.zero); }, 2, false);
            if (PropRun.Due()) yield return null;
            Vector3 trawler = Wreck(root, dir, "trawler", "Gesunkener Fischkutter", "Sunken trawler", new Vector3(-280f, 0f, -260f), 90f, 16f, 34f, 33,
                (solid) => { var t = SeaModels.Trawler(solid, out var c, out var room); return (t, c, room); }, 3, false);
            if (PropRun.Due()) yield return null;
            Vector3 pirates = Wreck(root, dir, "pirates", "Piratenwrack", "Pirate wreck", new Vector3(500f, 0f, 330f), 90f, 14f, 30f, 44,
                (solid) => { var t = SeaModels.PirateWreck(solid, out var c); return (t, c, Vector3.zero); }, 3, true);
            if (PropRun.Due()) yield return null;

            // ---------------------------------------------------------------- chests
            Chest(root, "hr_coral", HR + new Vector3(30f, 0f, -35f), 35f, 4f, 11f, 1, 101);
            Chest(root, "hr_ledge", HR + new Vector3(-45f, 0f, 40f), 35f, 4f, 11f, 1, 102);
            Chest(root, "lagoon_raft", WorldLayout.SwimRaft + new Vector3(8f, 0f, 14f), 25f, 2.5f, 7f, 1, 103);
            Chest(root, "lagoon_bay", WorldLayout.HiddenBay + new Vector3(-25f, 0f, -10f), 35f, 2f, 7f, 1, 104);
            Chest(root, "kelp_a", KF + new Vector3(-25f, 0f, -20f), 40f, 10f, 24f, 2, 105);
            Chest(root, "kelp_b", KF + new Vector3(45f, 0f, 35f), 40f, 10f, 24f, 2, 106);
            if (PropRun.Due()) yield return null;
            Chest(root, "wreck_sand", wreckC + new Vector3(-28f, 0f, 16f), 30f, 18f, 42f, 2, 107);
            WreckHoldChest(root);
            Chest(root, "blue_ledge", BD + new Vector3(40f, 0f, 40f), 50f, 28f, 50f, 3, 108);
            Chest(root, "trench_vent", DT + new Vector3(30f, 0f, -40f), 60f, 60f, 110f, 3, 109);
            Chest(root, "fog_reef", FI + new Vector3(25f, 0f, -20f), 30f, 6f, 22f, 2, 110);
            if (PropInfo.Spot("volcano_cave", out var cave)) Chest(root, "volcano_cave", cave, 12f, 30f, 70f, 3, 111);
            Vector3 abyss = WorldLayout.VolcanoAbyss;
            Vector3 toVolcano = WorldLayout.VolcanoIsland - abyss; toVolcano.y = 0f; toVolcano.Normalize();
            Chest(root, "volcano_abyss", abyss + toVolcano * (WorldLayout.VolcanoAbyssRadius - 4f), 22f, 30f, 75f, 4, 112);
            if (PropRun.Due()) yield return null;

            // ---------------------------------------------------------------- curiosities / memes
            Meme(root, "meme_shades", HR + new Vector3(15f, 0f, -10f), 30f, 5f, 10f, 0.6f, 201);
            Meme(root, "meme_cart", HR + new Vector3(-30f, 0f, -20f), 30f, 5f, 11f, 0f, 202);
            Meme(root, "meme_crabrave", WorldLayout.SwimRaft + new Vector3(-6f, 0f, 22f), 30f, 3f, 7f, 0f, 203);
            Meme(root, "meme_dance", KF + new Vector3(15f, 0f, 10f), 30f, 9f, 20f, 0f, 204, true);
            Meme(root, "meme_gnome", KF + new Vector3(-35f, 0f, 25f), 30f, 8f, 18f, 0f, 205, true);
            Meme(root, "meme_throne", wreckC + new Vector3(24f, 0f, -14f), 30f, 18f, 40f, 0f, 206);
            if (plane != Vector3.zero) Meme(root, "meme_gamingpc", plane + new Vector3(4.5f, 0f, -3f), 10f, 10f, 34f, 0f, 207);
            if (trawler != Vector3.zero) Meme(root, "meme_stonks", trawler + new Vector3(0f, 0f, 24f), 30f, 16f, 40f, 2f, 208);
            Meme(root, "meme_dino", abyss + toVolcano * (WorldLayout.VolcanoAbyssRadius + 4f), 22f, 22f, 50f, 0f, 209, true);
            Ducks(root, new Vector3(420f, 0f, 70f));
            if (PropRun.Due()) yield return null;
            Debug.Log("[Fauna] sea content: " + Chests.Count + " chests, " + Memes.Count + " curiosities, wrecks at "
                      + Placed.Count + " spots (sailboat " + (sail != Vector3.zero) + ", plane " + (plane != Vector3.zero) + ", trawler " + (trawler != Vector3.zero) + ", pirates " + (pirates != Vector3.zero) + ")");
        }

        // ================================================================== spot search
        /// A seabed point near `seed` with the depth in [minD, maxD], slope ≤ maxSlope and clearR of free water above.
        public static bool FindSpot(Vector3 seed, float searchR, float minD, float maxD, float maxSlope, float clearR, int salt, out Vector3 spot)
        {
            var rng = new System.Random(salt * 7919 + 17);
            float best = float.MaxValue;
            spot = Vector3.zero;
            Vector3 bestP = Vector3.zero;
            float midD = (minD + maxD) * 0.5f;
            for (float r = 0f; r <= searchR; r += 6f)
            {
                int n = r < 1f ? 1 : Mathf.Max(6, Mathf.RoundToInt(r * 0.6f));
                float off = (float)rng.NextDouble() * Mathf.PI * 2f;
                for (int i = 0; i < n; i++)
                {
                    float a = off + i * Mathf.PI * 2f / n;
                    var p = new Vector3(seed.x + Mathf.Cos(a) * r, 0f, seed.z + Mathf.Sin(a) * r);
                    float g = WorldInfo.GroundHeight(p);
                    float depth = Waves.SeaLevel - g;
                    if (depth < minD || depth > maxD)
                    {
                        float miss = depth < minD ? minD - depth : depth - maxD;
                        if (miss < best && depth > 1.5f) { best = miss; bestP = new Vector3(p.x, g, p.z); }
                        continue;
                    }
                    if (Slope(p) > maxSlope) continue;
                    if (Physics.CheckSphere(new Vector3(p.x, g + clearR + 0.25f, p.z), clearR, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                    spot = new Vector3(p.x, g, p.z);
                    return true;
                }
            }
            spot = bestP;
            return false;
        }

        static float Slope(Vector3 p)
        {
            const float e = 1.5f;
            float gx = WorldInfo.GroundHeight(new Vector3(p.x + e, 0f, p.z)) - WorldInfo.GroundHeight(new Vector3(p.x - e, 0f, p.z));
            float gz = WorldInfo.GroundHeight(new Vector3(p.x, 0f, p.z + e)) - WorldInfo.GroundHeight(new Vector3(p.x, 0f, p.z - e));
            return Mathf.Sqrt(gx * gx + gz * gz) / (2f * e);
        }

        static Quaternion OnBed(Vector3 p, float yaw, float follow)
        {
            const float e = 1.5f;
            float gx = WorldInfo.GroundHeight(new Vector3(p.x + e, 0f, p.z)) - WorldInfo.GroundHeight(new Vector3(p.x - e, 0f, p.z));
            float gz = WorldInfo.GroundHeight(new Vector3(p.x, 0f, p.z + e)) - WorldInfo.GroundHeight(new Vector3(p.x, 0f, p.z - e));
            Vector3 n = new Vector3(-gx, 2f * e, -gz).normalized;
            return PropUtil.OnGround(n, follow, yaw);
        }

        static Transform Holder(Transform root, string name, Vector3 center, float range)
        {
            var h = new GameObject(name).transform;
            h.SetParent(root, false);
            var content = new GameObject("Content").transform;
            content.SetParent(h, false);
            h.gameObject.AddComponent<PropSiteActivation>().Init(content.gameObject, center, range);
            return content;
        }

        /// Shallow solids go on the boat-pass layer (the diver bumps into them, the boat never snags).
        static void FixLayers(Transform t)
        {
            foreach (var c in t.GetComponentsInChildren<Collider>(true))
            {
                if (c.isTrigger) continue;
                float top = c.bounds.max.y;
                c.gameObject.layer = PropSolid.WaterLayer(top);
            }
        }

        // ================================================================== builders
        delegate (Transform model, Vector3 chestLocal, Vector3 roomLocal) WreckBuilder(bool solid);

        static Vector3 Wreck(Transform root, SeaContentDirector dir, string id, string de, string en, Vector3 seed, float searchR, float minD, float maxD,
                             int salt, WreckBuilder build, int chestTier, bool pirate)
        {
            try
            {
                if (!FindSpot(seed, searchR, minD, maxD, 0.3f, 4f, salt, out var spot) && spot == Vector3.zero) return Vector3.zero;
                float yaw = (Mathf.Abs(FindsDatabase.StableHash(id)) % 360);
                var content = Holder(root, "Wreck_" + id, spot, 180f);
                var (model, chestLocal, roomLocal) = build(true);
                model.SetParent(content, false);
                model.SetPositionAndRotation(spot + Vector3.down * 0.35f, OnBed(spot, yaw, 0.5f));
                FixLayers(model);
                Placed["wreck_" + id] = spot;
                dir.pois.Add(new SeaContentDirector.Poi { id = "wreck_" + id, nameDe = de, nameEn = en, pos = spot + Vector3.up * 1.5f, radius = 16f });
                // the chest: in the wheelhouse / hold / beside the wreck (snapped to whatever is below)
                Vector3 cp = model.TransformPoint(chestLocal);
                cp = SnapDown(cp + Vector3.up * 0.6f, 3f);
                var ch = MakeChest(content, "wreck_" + id, cp, yaw + 90f, chestTier, pirate);
                if (ch != null) Chests.Add(ch);
                if (roomLocal != Vector3.zero) Placed["room_" + id] = model.TransformPoint(roomLocal);
                return spot;
            }
            catch (System.Exception e)
            {
                Debug.LogError("Sealebrities sea content: wreck " + id + " failed: " + e);
                return Vector3.zero;
            }
        }

        static Vector3 SnapDown(Vector3 from, float maxDrop)
        {
            if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, maxDrop + 0.6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return hit.point;
            float g = WorldInfo.GroundHeight(from);
            return new Vector3(from.x, Mathf.Max(g, from.y - maxDrop), from.z);
        }

        static void Chest(Transform root, string id, Vector3 seed, float searchR, float minD, float maxD, int tier, int salt)
        {
            try
            {
                if (!FindSpot(seed, searchR, minD, maxD, 0.45f, 0.8f, salt, out var spot))
                {
                    if (spot == Vector3.zero) { Debug.LogWarning("[Fauna] no spot for chest " + id); return; }
                }
                var content = Holder(root, "Chest_" + id, spot, 140f);
                var ch = MakeChest(content, id, spot, Mathf.Abs(FindsDatabase.StableHash(id)) % 360, tier, false);
                if (ch != null) Chests.Add(ch);
            }
            catch (System.Exception e) { Debug.LogError("Sealebrities sea content: chest " + id + " failed: " + e); }
        }

        /// A chest inside the MS Clickbait's hold (next to the cargo) – the "sunken room" of the old wreck.
        static void WreckHoldChest(Transform root)
        {
            try
            {
                if (!PropInfo.Spot("wreck_hold", out var hold)) return;
                Vector3[] tries = { new Vector3(2.2f, 0f, -2.5f), new Vector3(-2.2f, 0f, -2.5f), new Vector3(2.2f, 0f, 2.5f), new Vector3(-2.4f, 0f, 1f), new Vector3(0f, 0f, -4f) };
                for (int i = 0; i < tries.Length; i++)
                {
                    Vector3 p = SnapDown(hold + tries[i] + Vector3.up * 1.2f, 2.5f);
                    if (Physics.CheckBox(p + Vector3.up * 0.4f, new Vector3(0.5f, 0.3f, 0.35f), Quaternion.identity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                    var content = Holder(root, "Chest_wreck_hold", p, 140f);
                    var ch = MakeChest(content, "wreck_hold", p, 90f, 3, false);
                    if (ch != null) Chests.Add(ch);
                    return;
                }
                Debug.LogWarning("[Fauna] no free floor for the wreck-hold chest");
            }
            catch (System.Exception e) { Debug.LogError("Sealebrities sea content: hold chest failed: " + e); }
        }

        static SeaChest MakeChest(Transform parent, string id, Vector3 pos, float yaw, int tier, bool pirate)
        {
            var t = SeaModels.Chest(true, pirate);
            t.SetParent(parent, false);
            t.SetPositionAndRotation(pos + Vector3.down * 0.06f, OnBed(pos, yaw, 0.6f));
            FixLayers(t);
            var use = new GameObject("ChestUse");
            use.transform.SetParent(t, false);
            use.transform.localPosition = new Vector3(0f, 0.4f, 0f);
            var sc = use.AddComponent<SphereCollider>();
            sc.isTrigger = true;
            sc.radius = 1.2f;
            var ch = t.gameObject.AddComponent<SeaChest>();
            ch.chestId = id;
            ch.tier = tier;
            ch.pirate = pirate;
            ch.lid = t.Find("LidPivot");
            var loot = t.Find("Loot");
            ch.loot = loot != null ? loot.gameObject : null;
            var glint = t.Find("Glint");
            ch.glint = glint != null ? glint.gameObject : null;
            Placed["chest_" + id] = pos;
            return ch;
        }

        static void Meme(Transform root, string id, Vector3 seed, float searchR, float minD, float maxD, float lift, int salt, bool rock = false)
        {
            try
            {
                var s = SpeciesDatabase.Get(id);
                if (s == null) return;
                if (!FindSpot(seed, searchR, minD, maxD, 0.4f, 1.2f, salt, out var spot) && spot == Vector3.zero) { Debug.LogWarning("[Fauna] no spot for " + id); return; }
                var content = Holder(root, "Meme_" + id, spot, id == "meme_stonks" ? 160f : 130f);
                if (rock)
                {
                    var b = new PropMeshBuilder();
                    b.Ico(spot + Vector3.up * 0.15f, new Vector3(0.9f, 0.35f, 0.8f), Quaternion.Euler(0f, salt * 13f, 0f), 1);
                    PropUtil.MeshObject(content, "MemeRock", b.ToMesh("SB_MemeRock"), PropMats.ReefRock, Vector3.zero, Quaternion.identity, false);
                    PropSolid.AABox(content, spot + Vector3.up * 0.15f, new Vector3(1.6f, 0.5f, 1.4f), true);
                    spot += Vector3.up * 0.45f;
                }
                float yaw = Mathf.Abs(FindsDatabase.StableHash(id)) % 360;
                var a = SeaMemeActor.Create(s, content, spot + Vector3.up * lift, yaw);
                if (a != null)
                {
                    FixLayers(a.transform);
                    Memes.Add(a);
                    Placed[id] = spot;
                }
            }
            catch (System.Exception e) { Debug.LogError("Sealebrities sea content: meme " + id + " failed: " + e); }
        }

        static void Ducks(Transform root, Vector3 at)
        {
            try
            {
                var s = SpeciesDatabase.Get("meme_ducks");
                if (s == null) return;
                float g = WorldInfo.GroundHeight(at);
                if (g > -4f) at += new Vector3(40f, 0f, 0f);
                var content = Holder(root, "Meme_ducks", at, 300f);
                var a = SeaMemeActor.Create(s, content, new Vector3(at.x, Waves.SeaLevel, at.z), 0f);
                if (a != null) { Memes.Add(a); Placed["meme_ducks"] = new Vector3(at.x, 0f, at.z); }
            }
            catch (System.Exception e) { Debug.LogError("Sealebrities sea content: ducks failed: " + e); }
        }
    }
}
