using System.Collections;
using UnityEngine;

namespace Sealebrities
{
    /// Round-3 step 3 – landmark islands (own identities) and the volcano island (props, spawn anchors, colliders).
    /// Runs inside RunRound3Steps (-r3only).
    public sealed partial class AutoPlaytest
    {
        IEnumerator StepR3Islands()
        {
            // ---------------------------------------------------------------- the volcano island
            var vol = GameObject.Find("VolcanoIsland");
            if (!Check(vol != null, "volcano props root 'VolcanoIsland' exists")) yield break;
            var vt = vol.transform;
            Vector3 vc = WorldLayout.VolcanoIsland;

            // smoke plume: one particle system, playing, prewarmed
            var smokeT = vt.Find("VolcanoSmoke");
            var smoke = smokeT != null ? smokeT.GetComponent<ParticleSystem>() : null;
            Check(smoke != null, "volcano smoke particle system exists");
            yield return Wait(1f);
            if (smoke != null)
            {
                Check(smoke.isPlaying, "volcano smoke plays");
                Check(smoke.particleCount >= 20, "volcano smoke has a prewarmed plume (" + smoke.particleCount + " particles)");
                var pr = smoke.GetComponent<ParticleSystemRenderer>();
                if (pr != null) Info("smoke plume bounds: top y " + pr.bounds.max.y.ToString("0") + " m, width " + pr.bounds.size.x.ToString("0") + " m");
            }
            var fx = vol.GetComponent<PropVolcanoFX>();
            Check(fx != null && fx.SteamCount >= 4, "volcano FX controller with steam vents (" + (fx != null ? fx.SteamCount : 0) + ")");
            Check(HasRenderers(vt.Find("VolcanoLava")), "glowing lava (crater lake + cracks) built");
            Check(HasRenderers(vt.Find("VolcanoTrees")), "palms / tree ferns / cycads / snags built");
            Check(HasRenderers(vt.Find("VolcanoUnderstory")), "ferns + horsetails built");
            Check(HasRenderers(vt.Find("VolcanoRocksFar")) && HasRenderers(vt.Find("VolcanoDetail")), "volcano rocks + beach dressing built");

            // colliders on the rocks (land, reef, cave arches) – include the under-water decor while it is inactive
            int land = 0, reef = 0, cave = 0, trunks = 0, bad = 0;
            foreach (var c in vol.GetComponentsInChildren<Collider>(true))
            {
                if (c.isTrigger) continue;
                string n = c.gameObject.name;
                if (n == "VolcanoRock") land++;
                else if (n == "VolcanoReefRock") reef++;
                else if (n == "VolcanoCaveRock") cave++;
                else if (n == "Snag" || n == "DeadPalm" || n.StartsWith("Solid")) trunks++;
                if ((n == "VolcanoRock" || n == "VolcanoReefRock" || n == "VolcanoCaveRock") && c is MeshCollider mc && mc.sharedMesh == null) bad++;
            }
            Check(land >= 60, "volcano land rocks have colliders (" + land + ")");
            Check(reef >= 30, "volcano reef rocks have colliders (" + reef + ")");
            Check(cave >= 20, "lava-tube arch rocks have colliders (" + cave + ")");
            Check(bad == 0, "no rock collider without a mesh (" + bad + ")");
            Info("trunk / misc solids on the volcano: " + trunks);

            // named spawn anchors with sane depths / heights
            int spotsOk = 0, spotsAll = 0;
            bool SpotIn(string id, float minY, float maxY, bool ground)
            {
                spotsAll++;
                if (!PropInfo.Spot(id, out var p)) { Check(false, "spot " + id + " exists"); return false; }
                float y = ground ? WorldInfo.GroundHeight(p) : p.y;
                float dist = new Vector2(p.x - vc.x, p.z - vc.z).magnitude;
                bool ok = y >= minY && y <= maxY && dist < WorldLayout.VolcanoZoneRadius;
                if (ok) spotsOk++;
                else Check(false, "spot " + id + " y " + y.ToString("0.0") + " in [" + minY + ", " + maxY + "], " + dist.ToString("0") + " m from the volcano");
                return ok;
            }
            SpotIn("volcano_beach", -0.5f, 3.5f, false);
            SpotIn("volcano_crater", 30f, 40f, false);
            SpotIn("volcano_mooring", -8f, -2f, true);
            for (int i = 0; i < 6; i++) SpotIn("volcano_land_" + i, 0.05f, 30f, false);
            for (int i = 0; i < 6; i++) SpotIn("volcano_reef_" + i, -20.5f, -3.5f, true);
            for (int i = 0; i < 4; i++) SpotIn("volcano_deep_" + i, -82f, -38f, true);
            SpotIn("volcano_cave", -62f, -38f, false);
            Check(spotsOk == spotsAll, "volcano spawn spots sane (" + spotsOk + "/" + spotsAll + ")");

            int shore = 0, bask = 0, meadow = 0, perch = 0, all = 0;
            foreach (var s in PropInfo.LandSpots)
            {
                if (s.area != PropVolcano.Area) continue;
                all++;
                if (s.kind == LandSpotKind.Shore) shore++;
                else if (s.kind == LandSpotKind.Bask) bask++;
                else if (s.kind == LandSpotKind.Meadow) meadow++;
                else if (s.kind == LandSpotKind.Perch) perch++;
            }
            Check(all >= 10 && shore >= 3 && bask >= 1 && meadow >= 1, "volcano land spots (" + all + ": shore " + shore + ", bask " + bask + ", meadow " + meadow + ", perch " + perch + ")");
            int hab = 0, over = 0;
            foreach (var h in PropInfo.Habitats)
            {
                if (h.zone != PropVolcano.Zone) continue;
                hab++;
                if (h.kind == HabitatKind.Overhang) over++;
            }
            Check(hab >= 12 && over >= 2, "volcano habitats (" + hab + ", overhang " + over + ")");

            // standing on the black sand
            if (pc != null && PropInfo.Spot("volcano_beach", out var beach))
            {
                yield return Stand(beach + Vector3.up * 0.3f, YawTo(beach, vc));
                yield return Wait(0.8f);
                Vector3 at = pc.transform.position;
                Check(!pc.IsInWater && Mathf.Abs(at.y - beach.y) < 2f, "player stands on the volcano beach (y " + at.y.ToString("0.0") + ")");
                // near the island the under-water decor switches on: the lava-tube passage must be free
                Check(IsActiveUnderwater(vt), "volcano under-water decor active near the island");
                if (PropInfo.Spot("volcano_cave", out var cavePos))
                {
                    bool blocked = Physics.CheckSphere(cavePos, 0.6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                    Check(!blocked, "the lava-tube passage is open at volcano_cave (no collider in the swim path)");
                    Info("volcano_cave at " + cavePos.ToString("F1") + ", " + (-cavePos.y).ToString("0") + " m deep");
                }
            }

            // ---------------------------------------------------------------- landmark island identities
            string[] ids = { "kelp_forest", "wreck", "blue_drop", "deep_trench", "fog_isle" };
            int rockCols = 0;
            foreach (var id in ids)
            {
                var isle = GameObject.Find("MiniIsland_" + id);
                if (!Check(isle != null, "landmark island " + id + " exists")) continue;
                Check(HasRenderers(isle.transform.Find("Identity_" + id)), id + ": identity props built");
                Check(PropInfo.MiniLandmarks.ContainsKey(id), id + ": landmark spot kept");
                foreach (var c in isle.GetComponentsInChildren<Collider>(true))
                    if (!c.isTrigger && c.gameObject.name == "Rock") rockCols++;
            }
            Check(HasRenderers(FindIn("MiniIsland_kelp_forest", "Identity_kelp_forest_Shallows")), "kelp island: sea-grass shallows");
            Check(PropInfo.Spot("wreck_lifeboat", out _), "wreck island: washed-up lifeboat");
            int nests = 0;
            foreach (var s in PropInfo.LandSpots) if (s.area == "blue_drop" && s.kind == LandSpotKind.Nest) nests++;
            Check(nests >= 2, "blue drop island: seabird nests on basalt columns (" + nests + ")");
            Check(PropInfo.Spot("trench_palm", out _) && HasRenderers(FindIn("MiniIsland_deep_trench", "Identity_deep_trench_Shallows")), "trench atoll: leaning palm + white sandbar ring");
            var mistT = FindIn("MiniIsland_fog_isle", "IslandMist");
            var mist = mistT != null ? mistT.GetComponent<ParticleSystem>() : null;
            Check(mist != null && mist.isPlaying, "Fog Isle: mist plays");
            Check(FindIn("MiniIsland_fog_isle", "FogGlowPlants") != null && PropInfo.Spot("toni_fog_camp", out _), "Fog Isle: glowing plants round Toni's camp (camp kept)");
            Check(rockCols >= 25, "landmark island rocks have colliders (" + rockCols + ")");
            Check(PropInfo.MiniShellSpots.Count >= 8, "golden-shell spots on the islands kept (" + PropInfo.MiniShellSpots.Count + ")");
        }

        static bool HasRenderers(Transform t) => t != null && t.GetComponentsInChildren<Renderer>(true).Length > 0;

        static Transform FindIn(string root, string child)
        {
            var go = GameObject.Find(root);
            if (go == null) return null;
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                if (t.name == child) return t;
            return null;
        }

        static bool IsActiveUnderwater(Transform volcano)
        {
            var uw = volcano.Find("VolcanoUnderwater");
            var decor = uw != null ? uw.Find("Decor") : null;
            return decor != null && decor.gameObject.activeInHierarchy;
        }
    }
}
