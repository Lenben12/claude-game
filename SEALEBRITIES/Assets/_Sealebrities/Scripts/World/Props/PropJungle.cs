using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Island vegetation and rocks: palm variants, ferns/shrubs/broad-leaf plants, coastal and inland boulders.
    /// Instances are merged per material into 64 m / 48 m chunks with distance culling.
    public static class PropJungle
    {
        public const int PalmTarget = 540;
        public const int PlantTarget = 440;

        static float G(float x, float z) => PropGround.H(x, z);

        public static IEnumerator Build(Transform island, PropExclusion excl)
        {
            var root = PropUtil.WorldRoot("Jungle", island);
            var rng = new System.Random(4711);
            var variants = new List<PropModel>();

            // ---------------------------------------------------------------- variants
            var palms = new List<PropModel>();
            for (int i = 0; i < 8; i++)
            {
                palms.Add(PropPlants.Palm(1000 + i * 17));
                if (PropRun.Due()) yield return null;
            }
            var plants = new List<PropModel>();
            var plantKind = new List<int>();
            for (int i = 0; i < 4; i++) { plants.Add(PropPlants.Fern(2000 + i)); plantKind.Add(0); }
            for (int i = 0; i < 4; i++) { plants.Add(PropPlants.Shrub(3000 + i)); plantKind.Add(1); }
            for (int i = 0; i < 6; i++) { plants.Add(PropPlants.BroadLeaf(4000 + i, i % 4)); plantKind.Add(2); }
            variants.AddRange(palms);
            variants.AddRange(plants);
            if (PropRun.Due()) yield return null;

            // ---------------------------------------------------------------- palms
            var treeBatch = new PropBatch(64f);
            var palmCols = new GameObject("PalmColliders");
            palmCols.transform.SetParent(root, false);
            palmCols.transform.position = Vector3.zero;
            var spacing = new PropExclusion(8f);
            float ext = WorldLayout.IslandRadius + 35f;
            Vector2 lh = new Vector2(WorldLayout.Lighthouse.x, WorldLayout.Lighthouse.z);
            int placed = 0, attempts = 0;
            while (placed < PalmTarget && attempts < 40000)
            {
                attempts++;
                float x = PropUtil.Range(rng, -ext, ext), z = PropUtil.Range(rng, -ext, ext);
                float g = G(x, z);
                if (g < 1.5f || g > 70f) continue;
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
                float s = PropUtil.Range(rng, 0.82f, 1.2f);
                float y = Mathf.Min(Mathf.Min(G(x + 0.4f, z), G(x - 0.4f, z)), Mathf.Min(G(x, z + 0.4f), G(x, z - 0.4f))) - 0.1f;
                y = Mathf.Min(y, g - 0.05f);
                var model = palms[rng.Next(palms.Count)];
                treeBatch.Add(model, Matrix4x4.TRS(new Vector3(x, y, z), rot, Vector3.one * s));
                PropCol.UprightCapsule(palmCols, new Vector3(x, y, z), 0.27f * s, 3.2f);
                spacing.Add(x, z, PropUtil.Range(rng, 2.6f, 4.4f));
                placed++;
                if (PropRun.Due()) yield return null;
            }
            treeBatch.Flush(root, "Palms", ShadowCastingMode.On, 950f);
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
                // ferns/shrubs like the shade of the inland jungle, broad leaves everywhere
                int idx;
                double k = rng.NextDouble();
                if (g < 4f && k < 0.5) idx = 4 + rng.Next(4);          // shrubs near the beach
                else if (k < 0.38) idx = rng.Next(4);                  // fern
                else if (k < 0.66) idx = 4 + rng.Next(4);              // shrub
                else idx = 8 + rng.Next(6);                            // broad leaf
                float s = PropUtil.Range(rng, 0.7f, 1.35f);
                Vector3 n = PropGround.Normal(x, z, 0.8f);
                Quaternion rot = PropUtil.OnGround(n, 0.6f, PropUtil.Range(rng, 0f, 360f));
                plantBatch.Add(plants[idx], Matrix4x4.TRS(new Vector3(x, g - 0.04f, z), rot, Vector3.one * s));
                pSpacing.Add(x, z, 1.1f * s);
                placed++;
                if (PropRun.Due()) yield return null;
            }
            plantBatch.Flush(root, "Plants", ShadowCastingMode.Off, 150f);
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
                Material mat = isCoast ? PropMats.RockDark : (rng.NextDouble() < 0.6 ? PropMats.Rock : PropMats.RockDark);
                if (s > 1.3f)
                {
                    var mesh = PropRocks.Mesh(sc, rng.Next(), 7);
                    rockBatch.Add(mesh, mat, Matrix4x4.TRS(p, rot, Vector3.one));
                    PropCol.Mesh(rockCols, mesh, p, rot, Vector3.one, "Rock");
                }
                else
                {
                    PropRocks.Build(rockBatch.B(mat, p), p, sc, rot, rng.Next(), 5);
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
                    var mesh = PropRocks.Mesh(sc, rng.Next(), 6);
                    rockBatch.Add(mesh, PropMats.Rock, Matrix4x4.TRS(p, rot, Vector3.one));
                    PropCol.Mesh(rockCols, mesh, p, rot, Vector3.one, "Rock");
                }
                else PropRocks.Build(rockBatch.B(PropMats.Rock, p), p, sc, rot, rng.Next(), 5);
                excl.Add(x, z, s);
            }
            rockBatch.Flush(root, "Rocks", ShadowCastingMode.On, 650f);

            // variant meshes were merged into the chunks and are no longer needed
            foreach (var v in variants)
                foreach (var part in v.parts)
                    if (part.mesh != null) Object.Destroy(part.mesh);
        }
    }
}
