using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Island vegetation and rocks in the low-poly style: palms (mostly along the coast), round jungle trees inland,
    /// bushes / broad-leaf plants / ferns, thousands of grass tufts on the grassy areas and chunky rocks.
    /// Instances are merged per material into grid chunks with distance culling (one draw call per chunk and color).
    public static class PropJungle
    {
        public const int PalmTarget = 500;
        public const int TreeTarget = 120;
        public const int PlantTarget = 460;
        public const float GrassStep = 2.3f;

        static float G(float x, float z) => PropGround.H(x, z);

        public static IEnumerator Build(Transform island, PropExclusion excl)
        {
            var root = PropUtil.WorldRoot("Jungle", island);
            var rng = new System.Random(4711);
            var variants = new List<PropModel>();

            // ---------------------------------------------------------------- variants
            var palms = new List<PropModel>();
            for (int i = 0; i < 10; i++)
            {
                palms.Add(PropPlants.Palm(1000 + i * 17));
                if (PropRun.Due()) yield return null;
            }
            var trees = new List<PropModel>();
            for (int i = 0; i < 6; i++) trees.Add(PropPlants.Tree(1500 + i * 13));
            var plants = new List<PropModel>();
            for (int i = 0; i < 4; i++) plants.Add(PropPlants.Fern(2000 + i));        // 0..3
            for (int i = 0; i < 6; i++) plants.Add(PropPlants.Bush(3000 + i));        // 4..9
            for (int i = 0; i < 6; i++) plants.Add(PropPlants.BroadLeaf(4000 + i, i % 4)); // 10..15
            if (PropRun.Due()) yield return null;
            var tufts = new List<PropModel>();
            Material[] blades = { PropMats.Grass, PropMats.GrassDark, PropMats.GrassLight };
            for (int i = 0; i < 9; i++) tufts.Add(PropPlants.GrassTuft(5000 + i, blades[i % 3], null));
            tufts.Add(PropPlants.GrassTuft(5100, PropMats.Grass, PropMats.FlowerYellow));
            tufts.Add(PropPlants.GrassTuft(5101, PropMats.GrassDark, PropMats.FlowerWhite));
            tufts.Add(PropPlants.GrassTuft(5102, PropMats.GrassLight, PropMats.FlowerPink));
            var dryTufts = new List<PropModel>
            {
                PropPlants.GrassTuft(5200, PropMats.PalmLeafDry, null),
                PropPlants.GrassTuft(5201, PropMats.GrassLight, null),
            };
            variants.AddRange(palms);
            variants.AddRange(trees);
            variants.AddRange(plants);
            variants.AddRange(tufts);
            variants.AddRange(dryTufts);
            if (PropRun.Due()) yield return null;

            float ext = WorldLayout.IslandRadius + 35f;

            // ---------------------------------------------------------------- palms (coast first, thinner inland)
            var treeBatch = new PropBatch(96f);
            var treeCols = new GameObject("TreeColliders");
            treeCols.transform.SetParent(root, false);
            treeCols.transform.position = Vector3.zero;
            var spacing = new PropExclusion(8f);
            Vector2 lh = new Vector2(WorldLayout.Lighthouse.x, WorldLayout.Lighthouse.z);
            int placed = 0, attempts = 0;
            while (placed < PalmTarget && attempts < 40000)
            {
                attempts++;
                float x = PropUtil.Range(rng, -ext, ext), z = PropUtil.Range(rng, -ext, ext);
                float g = G(x, z);
                if (g < 1.5f || g > 70f) continue;
                if (g > 12f && rng.NextDouble() < 0.6) continue;
                if (excl.Blocked(x, z, 0.5f) || spacing.Blocked(x, z)) continue;
                if (PropGround.Slope(x, z, 1.5f) > 1.0f) continue;
                if (Vector2.Distance(new Vector2(x, z), lh) < 22f && rng.NextDouble() < 0.75) continue;

                Vector3 outward = new Vector3(x, 0f, z);
                outward = outward.sqrMagnitude > 0.01f ? outward.normalized : Vector3.forward;
                float ra = PropUtil.Range(rng, 0f, Mathf.PI * 2f);
                Vector3 randDir = new Vector3(Mathf.Cos(ra), 0f, Mathf.Sin(ra));
                float coast = Mathf.Clamp01((7f - g) / 5f);
                Vector3 lean = Vector3.Slerp(randDir, outward, coast * 0.85f);
                lean.y = 0f;
                if (lean.sqrMagnitude < 0.001f) lean = randDir;
                Quaternion rot = Quaternion.LookRotation(lean.normalized) * Quaternion.Euler(0f, -90f, 0f);
                float s = PropUtil.Range(rng, 0.85f, 1.2f);
                float y = Mathf.Min(Mathf.Min(G(x + 0.4f, z), G(x - 0.4f, z)), Mathf.Min(G(x, z + 0.4f), G(x, z - 0.4f))) - 0.1f;
                y = Mathf.Min(y, g - 0.05f);
                var model = palms[rng.Next(palms.Count)];
                treeBatch.Add(model, Matrix4x4.TRS(new Vector3(x, y, z), rot, Vector3.one * s));
                PropCol.UprightCapsule(treeCols, new Vector3(x, y, z), 0.27f * s, 3.2f);
                spacing.Add(x, z, PropUtil.Range(rng, 2.6f, 4.4f));
                placed++;
                if (PropRun.Due()) yield return null;
            }

            // ---------------------------------------------------------------- round jungle trees (inland)
            placed = 0; attempts = 0;
            while (placed < TreeTarget && attempts < 20000)
            {
                attempts++;
                float x = PropUtil.Range(rng, -ext, ext), z = PropUtil.Range(rng, -ext, ext);
                float g = G(x, z);
                if (g < 5f || g > 70f) continue;
                if (excl.Blocked(x, z, 2f) || spacing.Blocked(x, z, 1.5f)) continue;
                if (PropGround.Slope(x, z, 1.5f) > 0.9f) continue;
                if (Vector2.Distance(new Vector2(x, z), lh) < 20f) continue;
                float s = PropUtil.Range(rng, 0.8f, 1.25f);
                Quaternion rot = Quaternion.Euler(0f, PropUtil.Range(rng, 0f, 360f), 0f);
                Vector3 p = new Vector3(x, g - 0.05f, z);
                treeBatch.Add(trees[rng.Next(trees.Count)], Matrix4x4.TRS(p, rot, Vector3.one * s));
                PropCol.UprightCapsule(treeCols, p, 0.3f * s, 3f);
                spacing.Add(x, z, PropUtil.Range(rng, 3.5f, 5f) * s);
                placed++;
                if (PropRun.Due()) yield return null;
            }
            treeBatch.Flush(root, "Trees", ShadowCastingMode.On, 950f);
            if (PropRun.Due()) yield return null;

            // ---------------------------------------------------------------- ground plants
            var plantBatch = new PropBatch(48f);
            var pSpacing = new PropExclusion(8f);
            placed = 0; attempts = 0;
            while (placed < PlantTarget && attempts < 40000)
            {
                attempts++;
                float x = PropUtil.Range(rng, -ext, ext), z = PropUtil.Range(rng, -ext, ext);
                float g = G(x, z);
                if (g < 1.6f || g > 70f) continue;
                if (excl.Blocked(x, z) || pSpacing.Blocked(x, z)) continue;
                if (PropGround.Slope(x, z, 1f) > 1.2f) continue;
                int idx;
                double k = rng.NextDouble();
                if (g < 4f && k < 0.55) idx = 4 + rng.Next(6);          // bushes near the beach
                else if (k < 0.3) idx = rng.Next(4);                     // fern
                else if (k < 0.65) idx = 4 + rng.Next(6);                // bush
                else idx = 10 + rng.Next(6);                             // broad leaf
                float s = PropUtil.Range(rng, 0.75f, 1.35f);
                Vector3 n = PropGround.Normal(x, z, 0.8f);
                Quaternion rot = PropUtil.OnGround(n, 0.6f, PropUtil.Range(rng, 0f, 360f));
                plantBatch.Add(plants[idx], Matrix4x4.TRS(new Vector3(x, g - 0.05f, z), rot, Vector3.one * s));
                pSpacing.Add(x, z, 1.1f * s);
                placed++;
                if (PropRun.Due()) yield return null;
            }
            plantBatch.Flush(root, "Plants", ShadowCastingMode.On, 170f);
            if (PropRun.Due()) yield return null;

            // ---------------------------------------------------------------- rocks
            var rockBatch = new PropBatch(64f);
            var rockCols = new GameObject("RockColliders").transform;
            rockCols.SetParent(root, false);
            int inland = 0, coastal = 0;
            attempts = 0;
            while ((inland < 45 || coastal < 42) && attempts < 30000)
            {
                attempts++;
                float x = PropUtil.Range(rng, -ext - 10f, ext + 10f), z = PropUtil.Range(rng, -ext - 10f, ext + 10f);
                float g = G(x, z);
                bool isCoast = g > -1.4f && g < 1.3f;
                bool isInland = g >= 2f && g < 70f;
                if (isCoast && coastal >= 42) continue;
                if (isInland && inland >= 45) continue;
                if (!isCoast && !isInland) continue;
                if (excl.Blocked(x, z, 1.5f)) continue;
                float s = isCoast ? PropUtil.Range(rng, 0.7f, 2.6f) : PropUtil.Range(rng, 0.5f, 2.8f);
                Vector3 sc = new Vector3(s * PropUtil.Range(rng, 0.9f, 1.5f), s * PropUtil.Range(rng, 0.55f, 1f), s * PropUtil.Range(rng, 0.8f, 1.2f));
                Quaternion rot = Quaternion.Euler(PropUtil.Range(rng, -10f, 10f), PropUtil.Range(rng, 0f, 360f), PropUtil.Range(rng, -10f, 10f));
                Vector3 p = new Vector3(x, g - sc.y * 0.35f, z);
                Material mat = isCoast ? PropMats.RockDark : (rng.NextDouble() < 0.6 ? PropMats.Rock : PropMats.RockLight);
                if (s > 1.3f)
                {
                    var mesh = PropRocks.Mesh(sc, rng.Next(), 1);
                    rockBatch.Add(mesh, mat, Matrix4x4.TRS(p, rot, Vector3.one));
                    PropCol.Mesh(rockCols, mesh, p, rot, Vector3.one, "Rock");
                }
                else
                {
                    PropRocks.Build(rockBatch.B(mat, p), p, sc, rot, rng.Next(), s > 0.8f ? 1 : 0);
                }
                // a couple of pebbles around bigger rocks
                if (s > 1f)
                {
                    int pebbles = 1 + rng.Next(3);
                    for (int k = 0; k < pebbles; k++)
                    {
                        float a = PropUtil.Range(rng, 0f, Mathf.PI * 2f), d = sc.x * PropUtil.Range(rng, 1.1f, 1.6f);
                        float px = x + Mathf.Cos(a) * d, pz = z + Mathf.Sin(a) * d;
                        float ps = PropUtil.Range(rng, 0.15f, 0.35f);
                        Vector3 pp = new Vector3(px, G(px, pz) - ps * 0.2f, pz);
                        PropRocks.Build(rockBatch.B(mat, pp), pp, new Vector3(ps * 1.2f, ps * 0.7f, ps), Quaternion.Euler(0f, PropUtil.Range(rng, 0f, 360f), 0f), rng.Next(), 0);
                    }
                }
                excl.Add(x, z, s * 1.2f);
                if (isCoast) coastal++; else inland++;
                if (PropRun.Due()) yield return null;
            }
            // rocky outcrop around the lighthouse cliff
            for (int i = 0; i < 14; i++)
            {
                float a = PropUtil.Range(rng, 0f, Mathf.PI * 2f);
                float d = PropUtil.Range(rng, 9f, 26f);
                float x = WorldLayout.Lighthouse.x + Mathf.Cos(a) * d, z = WorldLayout.Lighthouse.z + Mathf.Sin(a) * d;
                if (excl.Blocked(x, z)) continue;
                float g = G(x, z);
                if (g < 0.5f) continue;
                float s = PropUtil.Range(rng, 0.8f, 2.4f);
                Vector3 sc = new Vector3(s * 1.2f, s * 0.7f, s);
                Vector3 p = new Vector3(x, g - sc.y * 0.3f, z);
                Quaternion rot = Quaternion.Euler(0f, PropUtil.Range(rng, 0f, 360f), 0f);
                if (s > 1.3f)
                {
                    var mesh = PropRocks.Mesh(sc, rng.Next(), 1);
                    rockBatch.Add(mesh, PropMats.Rock, Matrix4x4.TRS(p, rot, Vector3.one));
                    PropCol.Mesh(rockCols, mesh, p, rot, Vector3.one, "Rock");
                }
                else PropRocks.Build(rockBatch.B(PropMats.Rock, p), p, sc, rot, rng.Next(), 1);
                excl.Add(x, z, s);
            }
            rockBatch.Flush(root, "Rocks", ShadowCastingMode.On, 650f);

            if (PropRun.Due()) yield return null;

            // ---------------------------------------------------------------- grass tufts (jittered grid on the grassy areas)
            var grassBatch = new PropBatch(40f);
            int tuftCount = 0;
            for (float gx = -ext; gx <= ext; gx += GrassStep)
            {
                for (float gz = -ext; gz <= ext; gz += GrassStep)
                {
                    if (gx * gx + gz * gz > ext * ext) continue;
                    float x = gx + PropUtil.Range(rng, -0.45f, 0.45f) * GrassStep;
                    float z = gz + PropUtil.Range(rng, -0.45f, 0.45f) * GrassStep;
                    double roll = rng.NextDouble();
                    float g = G(x, z);
                    if (g < 1.6f || g > 70f) continue;
                    // same sand/grass line as the terrain colors (WorldTerrainData): green tufts only where the ground
                    // is painted grass, dry tufts only on sand-colored dunes, nothing on the dithered border or dirt
                    float grassAmt = WorldTerrainData.GrassAmount(x, z, g);
                    bool dune = grassAmt < 0.2f;
                    if (!dune && grassAmt < 0.8f) continue;
                    if (WorldTerrainData.IsBareSoil(x, z)) continue;
                    // patchy density: meadows and bare spots
                    float dens = Mathf.SmoothStep(0.2f, 0.8f, PropNoise.Value(x * 0.045f, z * 0.045f, 77));
                    float p = dune ? 0.1f : 0.3f + 0.7f * dens;
                    if (roll > p) continue;
                    if (excl.Blocked(x, z)) continue;
                    if (PropGround.Slope(x, z, 1f) > 0.6f) continue;
                    int clump = dune ? 1 : 1 + (dens > 0.6f ? rng.Next(3) : rng.Next(2));
                    for (int c = 0; c < clump; c++)
                    {
                        float cx = x + (c == 0 ? 0f : PropUtil.Range(rng, -0.6f, 0.6f));
                        float cz = z + (c == 0 ? 0f : PropUtil.Range(rng, -0.6f, 0.6f));
                        float cg = c == 0 ? g : G(cx, cz);
                        PropModel m;
                        if (dune) m = dryTufts[rng.Next(dryTufts.Count)];
                        else m = rng.NextDouble() < 0.07 ? tufts[9 + rng.Next(3)] : tufts[rng.Next(9)];
                        float s = PropUtil.Range(rng, 0.8f, 1.35f);
                        Quaternion rot = Quaternion.Euler(PropUtil.Range(rng, -6f, 6f), PropUtil.Range(rng, 0f, 360f), PropUtil.Range(rng, -6f, 6f));
                        grassBatch.Add(m, Matrix4x4.TRS(new Vector3(cx, cg, cz), rot, Vector3.one * s));
                        tuftCount++;
                    }
                    if (PropRun.Due()) yield return null;
                }
                if (PropRun.Due()) yield return null;
            }
            if (tuftCount == 0) Debug.Log("Sealebrities props: no grassy ground found for grass tufts");
            grassBatch.Flush(root, "Grass", ShadowCastingMode.Off, 95f);
            if (PropRun.Due()) yield return null;

            // variant meshes were merged into the chunks and are no longer needed
            foreach (var v in variants)
                foreach (var part in v.parts)
                    if (part.mesh != null) Object.Destroy(part.mesh);
        }
    }
}
