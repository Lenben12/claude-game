using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// The "Sealebrity Dive Co." hut in the low-poly style: white clapboard walls (separate flat boards), wooden trim,
    /// turquoise shutters, red shingle roof, striped porch awning; chunky interior with bed, TV, desk and shelves.
    /// Local frame: origin = WorldLayout.Hut (shifted west) at floor-top height, +x east (door side), +z north.
    /// Interior x [-4.5, 4.5], z [-5, 5] (sized so that WorldLayout.Bed / TV / PlayerSpawn are inside).
    public static class PropHut
    {
        const float HX = 4.5f, HZ = 5f, WallH = 2.7f, PitchDeg = 20f, T = 0.14f, BoardH = 0.3f;

        static PropBatch batch;
        static Matrix4x4 TO;
        static Vector3 O;
        static Transform cols;

        static PropMeshBuilder B(Material m)
        {
            var b = batch.B(m);
            b.M = TO;
            return b;
        }

        static void Col(Vector3 localCenter, Vector3 size, Quaternion rot) => PropCol.Box(cols, O + localCenter, size, rot);
        static void Col(Vector3 localCenter, Vector3 size) => PropCol.Box(cols, O + localCenter, size, Quaternion.identity);

        static float GL(float lx, float lz) => PropGround.H(O.x + lx, O.z + lz) - O.y;

        /// World XZ position converted into the hut's local frame (y = 0).
        static Vector3 Local(Vector3 world) => new Vector3(world.x - O.x, 0f, world.z - O.z);

        /// The hut is shifted slightly west of WorldLayout.Hut so WorldLayout.PlayerSpawn has ~0.8 m clearance to the west wall.
        public const float WestShift = 0.4f;

        static float RidgeY => WallH + HZ * Mathf.Tan(PitchDeg * Mathf.Deg2Rad);

        public static IEnumerator Build(Transform island, PropExclusion excl)
        {
            Vector3 c = WorldLayout.Hut - new Vector3(WestShift, 0f, 0f);
            PropGround.MinMaxRect(new Vector3(c.x + 1.3f, 0f, c.z), Quaternion.identity, 6.3f, 5.6f, 1f, out float gmin, out float gmax);
            float floorY = Mathf.Max(gmax + 0.3f, 1.1f);
            O = new Vector3(c.x, floorY, c.z);
            TO = Matrix4x4.Translate(O);
            PropInfo.HutFloorY = floorY;
            PropInfo.HutDoor = O + new Vector3(HX + 1.2f, 0f, 0.9f);

            var root = PropUtil.WorldRoot("Hut", island);
            cols = new GameObject("Colliders").transform;
            cols.SetParent(root, false);
            batch = new PropBatch(0f);

            BuildShell();
            if (PropRun.Due()) yield return null;
            BuildRoof();
            BuildPorch(excl);
            if (PropRun.Due()) yield return null;
            BuildInterior(root);
            BuildExteriorDetails();
            if (PropRun.Due()) yield return null;

            batch.Flush(root, "HutMesh", ShadowCastingMode.On, 0f);
            batch = null;
            excl.Add(c.x + 1.3f, c.z, 9.5f);
            excl.Add(WorldLayout.PlayerSpawn.x, WorldLayout.PlayerSpawn.z, 2f);
        }

        // ------------------------------------------------------------------ shell
        static void BuildShell()
        {
            // floor: separate boards along z with small gaps over a dark skirt (plank look without textures)
            int k = 0;
            for (float x = -HX; x < HX + 0.16f; x += 0.3f, k++)
                B(k % 3 == 1 ? PropMats.WoodLight : PropMats.Wood).Box(new Vector3(x, -0.06f, 0f), new Vector3(0.28f, 0.12f, 2f * HZ + 0.3f));
            B(PropMats.WoodDark).Box(new Vector3(0f, -0.17f, 0f), new Vector3(2f * HX + 0.2f, 0.12f, 2f * HZ + 0.2f));
            Col(new Vector3(0f, -0.15f, 0f), new Vector3(2f * HX + 0.3f, 0.3f, 2f * HZ + 0.3f));
            for (int i = -2; i <= 2; i++)
                B(PropMats.WoodDark).Box(new Vector3(i * 2.2f, -0.32f, 0f), new Vector3(0.18f, 0.2f, 2f * HZ + 0.3f));

            // stilts
            float[] xs = { -HX + 0.1f, 0f, HX - 0.1f };
            float[] zs = { -HZ + 0.1f, 0f, HZ - 0.1f };
            foreach (float x in xs) foreach (float z in zs) Stilt(x, z);

            // clapboard walls with openings (s0, s1, v0, v1)
            var west = new List<Vector4> { new Vector4(-1.9f, -0.7f, 1.0f, 1.9f), new Vector4(1.0f, 2.0f, 1.0f, 1.9f) };
            var east = new List<Vector4> { new Vector4(-3.2f, -2.0f, 1.0f, 1.9f), new Vector4(0.2f, 1.6f, 0f, 2.15f) };
            var south = new List<Vector4> { new Vector4(-1.6f, -0.4f, 1.0f, 1.9f), new Vector4(1.2f, 2.2f, 1.0f, 1.9f) };
            var north = new List<Vector4> { new Vector4(0.0f, 1.2f, 1.0f, 1.9f) };
            Wall(true, -HX, -HZ - T * 0.5f, HZ + T * 0.5f, west);
            Wall(true, HX, -HZ - T * 0.5f, HZ + T * 0.5f, east);
            Wall(false, -HZ, -HX + T * 0.5f, HX - T * 0.5f, south);
            Wall(false, HZ, -HX + T * 0.5f, HX - T * 0.5f, north);

            // wooden corner posts and top plates
            var trim = B(PropMats.Wood);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    trim.Box(new Vector3(sx * (HX + 0.02f), WallH * 0.5f, sz * (HZ + 0.02f)), new Vector3(0.22f, WallH, 0.22f));
            trim.Box(new Vector3(0f, WallH - 0.06f, -HZ), new Vector3(2f * HX, 0.14f, T + 0.08f));
            trim.Box(new Vector3(0f, WallH - 0.06f, HZ), new Vector3(2f * HX, 0.14f, T + 0.08f));

            // gables (east / west), white
            float rise = (HZ + T * 0.5f) * Mathf.Tan(PitchDeg * Mathf.Deg2Rad);
            var wall = B(PropMats.Wall);
            for (int s = -1; s <= 1; s += 2)
            {
                float x = s * HX;
                for (int f = -1; f <= 1; f += 2)
                {
                    float xf = x + f * T * 0.5f;
                    Vector3 a = new Vector3(xf, WallH, -HZ - T * 0.5f), b = new Vector3(xf, WallH, HZ + T * 0.5f), t = new Vector3(xf, WallH + rise, 0f);
                    wall.Face3(a, b, t, new Vector3(f, 0f, 0f));
                }
                // a little round attic vent in the west gable (the east gable carries the sign)
                if (s > 0) continue;
                B(PropMats.Wood).Cylinder(new Vector3(x + s * (T * 0.5f), WallH + rise * 0.45f, 0f), Quaternion.Euler(0f, 0f, -90f * s), 0.24f, 0.24f, 0.04f, 8, false, true);
                B(PropMats.Ink).Cylinder(new Vector3(x + s * (T * 0.5f + 0.04f), WallH + rise * 0.45f, 0f), Quaternion.Euler(0f, 0f, -90f * s), 0.15f, 0.15f, 0.005f, 8, false, true);
            }
        }

        static void Stilt(float x, float z)
        {
            float g = GL(x, z);
            float bottom = g - 0.4f, top = -0.2f;
            if (top - bottom < 0.08f) return;
            B(PropMats.WoodDark).Cylinder(new Vector3(x, bottom, z), Quaternion.identity, 0.13f, 0.12f, top - bottom, 6);
            if (top - bottom > 0.6f) PropCol.Capsule(cols, O + new Vector3(x, (top + bottom) * 0.5f, z), 0.13f, top - bottom, Quaternion.identity);
        }

        /// Clapboard wall with rectangular openings. alongZ: wall at x = fixed running along z; else wall at z = fixed along x.
        static void Wall(bool alongZ, float fixedCoord, float s0, float s1, List<Vector4> holes)
        {
            holes.Sort((a, b) => a.x.CompareTo(b.x));
            float cur = s0;
            foreach (var h in holes)
            {
                if (h.x > cur) WallPiece(alongZ, fixedCoord, cur, h.x, 0f, WallH);
                if (h.z > 0f) WallPiece(alongZ, fixedCoord, h.x, h.y, 0f, h.z);
                if (h.w < WallH) WallPiece(alongZ, fixedCoord, h.x, h.y, h.w, WallH);
                cur = h.y;
                Frame(alongZ, fixedCoord, h);
            }
            if (cur < s1) WallPiece(alongZ, fixedCoord, cur, s1, 0f, WallH);
        }

        /// One wall rectangle as horizontal boards on a global 0.3 m grid; every second board sticks out a little.
        static void WallPiece(bool alongZ, float f, float a0, float a1, float v0, float v1)
        {
            float outSign = Mathf.Sign(f);
            int k0 = Mathf.FloorToInt(v0 / BoardH + 1e-3f), k1 = Mathf.CeilToInt(v1 / BoardH - 1e-3f);
            float s = (a0 + a1) * 0.5f;
            for (int k = k0; k < k1; k++)
            {
                float y0 = Mathf.Max(v0, k * BoardH), y1 = Mathf.Min(v1, (k + 1) * BoardH);
                if (y1 - y0 < 0.01f) continue;
                float push = (k % 2 == 0) ? 0f : 0.02f * outSign;
                float y = (y0 + y1) * 0.5f;
                Vector3 center = alongZ ? new Vector3(f + push, y, s) : new Vector3(s, y, f + push);
                Vector3 size = alongZ ? new Vector3(T, y1 - y0, a1 - a0) : new Vector3(a1 - a0, y1 - y0, T);
                B(k % 2 == 0 ? PropMats.Wall : PropMats.Wall2).Box(center, size);
            }
            float yc = (v0 + v1) * 0.5f;
            Vector3 cc = alongZ ? new Vector3(f, yc, s) : new Vector3(s, yc, f);
            Vector3 cs = alongZ ? new Vector3(T, v1 - v0, a1 - a0) : new Vector3(a1 - a0, v1 - v0, T);
            Col(cc, cs);
        }

        static void Frame(bool alongZ, float f, Vector4 h)
        {
            var d = B(PropMats.Wood);
            float depth = T + 0.1f, w = 0.09f;
            bool isDoor = h.z <= 0.001f;
            Vector3 P(float s, float y) => alongZ ? new Vector3(f, y, s) : new Vector3(s, y, f);
            Vector3 Sz(float alongS, float alongY) => alongZ ? new Vector3(depth, alongY, alongS) : new Vector3(alongS, alongY, depth);
            d.Box(P(h.x - w * 0.5f, (h.z + h.w) * 0.5f), Sz(w, h.w - h.z + w));
            d.Box(P(h.y + w * 0.5f, (h.z + h.w) * 0.5f), Sz(w, h.w - h.z + w));
            d.Box(P((h.x + h.y) * 0.5f, h.w + w * 0.5f), Sz(h.y - h.x + 2f * w, w));
            if (!isDoor)
            {
                Vector3 sill = alongZ ? new Vector3(depth + 0.12f, w, h.y - h.x + 3f * w) : new Vector3(h.y - h.x + 3f * w, w, depth + 0.12f);
                d.Box(P((h.x + h.y) * 0.5f, h.z - w * 0.5f), sill);
                // turquoise shutters left open on both sides (outside)
                float outSide = Mathf.Sign(f);
                float sw = (h.y - h.x) * 0.5f;
                for (int side = 0; side < 2; side++)
                {
                    float sc = side == 0 ? h.x - w - sw * 0.5f - 0.02f : h.y + w + sw * 0.5f + 0.02f;
                    Vector3 sp = alongZ
                        ? new Vector3(f + outSide * (T * 0.5f + 0.05f), (h.z + h.w) * 0.5f, sc)
                        : new Vector3(sc, (h.z + h.w) * 0.5f, f + outSide * (T * 0.5f + 0.05f));
                    Vector3 ss = alongZ ? new Vector3(0.05f, h.w - h.z, sw) : new Vector3(sw, h.w - h.z, 0.05f);
                    B(PropMats.Shutter).Box(sp, ss);
                }
            }
        }

        // ------------------------------------------------------------------ roof: stepped red shingle rows
        static void BuildRoof()
        {
            float p = PitchDeg * Mathf.Deg2Rad;
            float ridgeY = RidgeY;
            float over = 0.55f;
            float run = HZ + over;
            float slopeLen = run / Mathf.Cos(p);
            const int rows = 5;
            float seg = slopeLen / rows;
            float width = 2f * HX + 2f * over;
            for (int s = -1; s <= 1; s += 2)
            {
                for (int r = 0; r < rows; r++)
                {
                    float d = (r + 0.5f) * seg;
                    Vector3 c = new Vector3(0f, ridgeY + 0.07f - d * Mathf.Sin(p), s * d * Mathf.Cos(p));
                    Quaternion rot = Quaternion.Euler(s * (PitchDeg + 4f), 0f, 0f);
                    B(r % 2 == 0 ? PropMats.Roof : PropMats.RoofDark).Box(c, new Vector3(width + (r % 2) * 0.04f, 0.1f, seg * 1.14f), rot);
                }
                Quaternion prot = Quaternion.Euler(s * PitchDeg, 0f, 0f);
                float zc = s * run * 0.5f;
                float yc = ridgeY - run * 0.5f * Mathf.Tan(p) + 0.05f;
                Vector3 center = new Vector3(0f, yc, zc);
                Col(center + prot * new Vector3(0f, 0.08f, 0f), new Vector3(width, 0.22f, slopeLen), prot);
            }
            // chunky ridge cap
            B(PropMats.RoofDark).Box(new Vector3(0f, ridgeY + 0.1f, 0f), new Vector3(width + 0.1f, 0.3f, 0.3f), Quaternion.Euler(45f, 0f, 0f));

            // rafters, ridge beam, ties (visible from inside)
            var dark = B(PropMats.WoodDark);
            dark.Box(new Vector3(0f, ridgeY - 0.12f, 0f), new Vector3(2f * HX, 0.2f, 0.16f));
            for (int i = -2; i <= 2; i++)
            {
                float x = i * 1.8f;
                dark.Beam(new Vector3(x, WallH, -HZ), new Vector3(x, ridgeY - 0.05f, 0f), 0.1f, 0.14f);
                dark.Beam(new Vector3(x, WallH, HZ), new Vector3(x, ridgeY - 0.05f, 0f), 0.1f, 0.14f);
            }
            dark.Box(new Vector3(-1.8f, WallH + 0.05f, 0f), new Vector3(0.12f, 0.14f, 2f * HZ));
            dark.Box(new Vector3(1.8f, WallH + 0.05f, 0f), new Vector3(0.12f, 0.14f, 2f * HZ));
        }

        // ------------------------------------------------------------------ porch, awning, steps
        static void BuildPorch(PropExclusion excl)
        {
            float px0 = HX + 0.15f, px1 = 7.2f, pz0 = -1.6f, pz1 = 3.4f;
            int k = 0;
            for (float x = px0 + 0.15f; x < px1; x += 0.3f, k++)
                B(k % 3 == 2 ? PropMats.WoodLight : PropMats.Wood).Box(new Vector3(x, -0.06f, (pz0 + pz1) * 0.5f), new Vector3(0.28f, 0.12f, pz1 - pz0));
            B(PropMats.WoodDark).Box(new Vector3((px0 + px1) * 0.5f, -0.17f, (pz0 + pz1) * 0.5f), new Vector3(px1 - px0, 0.12f, pz1 - pz0 - 0.05f));
            Vector3 pc = new Vector3((px0 + px1) * 0.5f, -0.1f, (pz0 + pz1) * 0.5f);
            Vector3 ps = new Vector3(px1 - px0, 0.2f, pz1 - pz0);
            Col(pc - new Vector3(0f, 0.05f, 0f), ps + new Vector3(0f, 0.1f, 0f));
            Stilt(px1 - 0.15f, pz0 + 0.1f);
            Stilt(px1 - 0.15f, pz1 - 0.1f);
            Stilt(px0 + 1.2f, pz0 + 0.1f);
            Stilt(px0 + 1.2f, pz1 - 0.1f);

            // striped awning on posts with a zig-zag valance
            var wood = B(PropMats.Wood);
            float aTop = 2.55f, aLow = 2.25f, ax1 = 7.45f;
            float ang = Mathf.Atan2(aTop - aLow, ax1 - HX) * Mathf.Rad2Deg;
            float alen = (ax1 - HX) / Mathf.Cos(ang * Mathf.Deg2Rad);
            Quaternion arot = Quaternion.Euler(0f, 0f, -ang);
            const int stripes = 7;
            float az0 = 0.9f - 2.8f, sw = 5.6f / stripes;
            for (int i = 0; i < stripes; i++)
            {
                float zc = az0 + (i + 0.5f) * sw;
                var m = i % 2 == 0 ? PropMats.Roof : PropMats.White;
                B(m).Box(new Vector3((HX + ax1) * 0.5f, (aTop + aLow) * 0.5f + 0.04f, zc), new Vector3(alen, 0.05f, sw), arot);
                Vector3 e0 = new Vector3(ax1, aLow + 0.02f, zc - sw * 0.5f), e1 = new Vector3(ax1, aLow + 0.02f, zc + sw * 0.5f);
                B(m).Tri2(e0, e1, new Vector3(ax1, aLow - 0.24f, zc), Vector3.right);
            }
            wood.Box(new Vector3(7.1f, 2.2f, 0.9f), new Vector3(0.14f, 0.14f, 5.3f));
            wood.Box(new Vector3(7.1f, 1.1f, pz0 + 0.1f), new Vector3(0.14f, 2.2f, 0.14f));
            wood.Box(new Vector3(7.1f, 1.1f, pz1 - 0.1f), new Vector3(0.14f, 2.2f, 0.14f));
            // porch railing on the north/south edges
            for (int s = 0; s < 2; s++)
            {
                float z = s == 0 ? pz0 + 0.05f : pz1 - 0.05f;
                wood.Box(new Vector3((px0 + px1) * 0.5f, 0.95f, z), new Vector3(px1 - px0, 0.09f, 0.09f));
                for (int i = 0; i < 4; i++) wood.Box(new Vector3(px0 + 0.3f + i * 0.7f, 0.47f, z), new Vector3(0.08f, 0.94f, 0.08f));
                Col(new Vector3((px0 + px1) * 0.5f, 0.5f, z), new Vector3(px1 - px0, 1f, 0.1f));
            }
            // flower boxes on the railings
            for (int s = 0; s < 2; s++)
            {
                float z = s == 0 ? pz0 + 0.05f : pz1 - 0.05f;
                Vector3 fb = new Vector3(px0 + 1.4f, 1.06f, z);
                B(PropMats.Shutter).Box(fb, new Vector3(0.8f, 0.16f, 0.22f));
                var rng = new System.Random(31 + s);
                for (int i = 0; i < 3; i++)
                {
                    Vector3 bp = fb + new Vector3(-0.25f + i * 0.25f, 0.14f, 0f);
                    B(PropMats.GrassDark).Ico(bp, new Vector3(0.14f, 0.12f, 0.12f), Quaternion.Euler(0f, i * 40f, 0f), 0);
                    B(i == 1 ? PropMats.FlowerYellow : PropMats.FlowerPink).Octa(bp + new Vector3(PropUtil.Range(rng, -0.05f, 0.05f), 0.12f, 0.04f), 0.05f);
                }
            }

            // steps down to the beach
            float rise = 0.19f, runStep = 0.3f, sz = 0.9f, swidth = 1.5f;
            float lastTop = 0f, lastX = px1;
            int count = 0;
            var dark = B(PropMats.WoodDark);
            for (int i = 0; i < 40; i++)
            {
                float top = -(i + 1) * rise;
                float x = px1 + i * runStep + runStep * 0.5f;
                float g = GL(x, sz);
                if (top < g - 0.02f) break;
                B(PropMats.WoodLight).Box(new Vector3(x, top - 0.04f, sz), new Vector3(runStep + 0.02f, 0.08f, swidth));
                float blockH = top - 0.08f - (g - 0.3f);
                if (blockH > 0.02f) dark.Box(new Vector3(x, top - 0.08f - blockH * 0.5f, sz), new Vector3(runStep, blockH, swidth - 0.1f));
                lastTop = top;
                lastX = x + runStep * 0.5f;
                count++;
            }
            if (count > 0)
            {
                Vector2 A = new Vector2(px1, 0f), Bp = new Vector2(lastX, lastTop);
                Vector2 d = Bp - A;
                float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                Quaternion r = Quaternion.Euler(0f, 0f, angle);
                Vector3 mid = new Vector3((A.x + Bp.x) * 0.5f, (A.y + Bp.y) * 0.5f, sz);
                Col(mid - r * new Vector3(0f, 0.06f, 0f), new Vector3(d.magnitude + 0.1f, 0.12f, swidth), r);
                excl.AddLine(O + new Vector3(px1, 0f, sz), O + new Vector3(lastX + 1.5f, 0f, sz), 2.5f);
            }
        }

        // ------------------------------------------------------------------ interior
        static void BuildInterior(Transform root)
        {
            // bed at WorldLayout.Bed, head at the north wall
            Vector3 bed = Local(WorldLayout.Bed);
            float bx = bed.x, bz = bed.z;
            var frame = B(PropMats.Wood);
            frame.Box(new Vector3(bx, 0.22f, bz), new Vector3(1.04f, 0.2f, 1.94f));
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    frame.Box(new Vector3(bx + sx * 0.45f, 0.1f, bz + sz * 0.88f), new Vector3(0.1f, 0.2f, 0.1f));
            frame.Box(new Vector3(bx, 0.6f, bz + 0.93f), new Vector3(1.08f, 0.85f, 0.08f));
            frame.Box(new Vector3(bx, 1.05f, bz + 0.93f), new Vector3(1.16f, 0.08f, 0.12f));
            B(PropMats.White).Box(new Vector3(bx, 0.39f, bz), new Vector3(0.94f, 0.16f, 1.86f));
            B(PropMats.Wall2).Box(new Vector3(bx, 0.53f, bz + 0.66f), new Vector3(0.64f, 0.13f, 0.36f), Quaternion.Euler(-8f, 3f, 0f));
            B(PropMats.Blanket).Box(new Vector3(bx, 0.49f, bz - 0.25f), new Vector3(1.0f, 0.06f, 1.32f), Quaternion.Euler(0f, 1.5f, 0f));
            B(PropMats.Blanket).Box(new Vector3(bx + 0.51f, 0.32f, bz - 0.25f), new Vector3(0.04f, 0.36f, 1.26f));
            var bedGo = new GameObject("Bed");
            bedGo.transform.SetParent(root, false);
            bedGo.transform.position = O + new Vector3(bx, 0.33f, bz);
            var bc = bedGo.AddComponent<BoxCollider>();
            bc.size = new Vector3(1.05f, 0.66f, 1.95f);
            bedGo.AddComponent<PropBed>();
            // nightstand crate (east of the bed, away from the player spawn)
            Crate(new Vector3(bx + 1.0f, 0f, bz + 0.6f), 0.5f, 12f);

            // desk + chair + laptop (west wall)
            Vector3 tp = new Vector3(-3.6f, 0f, -0.5f);
            var light = B(PropMats.WoodLight);
            light.Box(tp + new Vector3(0f, 0.74f, 0f), new Vector3(0.84f, 0.07f, 1.34f));
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    light.Box(tp + new Vector3(sx * 0.33f, 0.36f, sz * 0.58f), new Vector3(0.08f, 0.72f, 0.08f));
            Col(tp + new Vector3(0f, 0.39f, 0f), new Vector3(0.84f, 0.78f, 1.34f));
            B(PropMats.Ink).Box(tp + new Vector3(0.05f, 0.79f, 0.1f), new Vector3(0.26f, 0.03f, 0.36f));
            Quaternion lid = Quaternion.Euler(0f, 0f, -14f);
            Vector3 lidC = tp + new Vector3(-0.08f, 0.91f, 0.1f);
            B(PropMats.Ink).Box(lidC, new Vector3(0.02f, 0.24f, 0.36f), lid);
            B(PropMats.LaptopScreen).Box(lidC + lid * new Vector3(0.012f, 0f, 0f), new Vector3(0.005f, 0.2f, 0.31f), lid);
            B(PropMats.CushionB).Cylinder(tp + new Vector3(0.2f, 0.775f, -0.35f), Quaternion.identity, 0.05f, 0.05f, 0.11f, 6, false, true);
            B(PropMats.White).Box(tp + new Vector3(0.1f, 0.78f, -0.15f), new Vector3(0.22f, 0.01f, 0.3f), Quaternion.Euler(0f, 12f, 0f));
            // chair facing west
            Vector3 cp = new Vector3(-2.75f, 0f, -0.5f);
            var cw = B(PropMats.Wood);
            cw.Box(cp + new Vector3(0f, 0.45f, 0f), new Vector3(0.46f, 0.06f, 0.46f));
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    cw.Box(cp + new Vector3(sx * 0.19f, 0.22f, sz * 0.19f), new Vector3(0.06f, 0.44f, 0.06f));
            cw.Box(cp + new Vector3(0.21f, 0.74f, 0f), new Vector3(0.06f, 0.5f, 0.46f));
            Col(cp + new Vector3(0f, 0.45f, 0f), new Vector3(0.46f, 0.9f, 0.46f));

            // shelves
            Shelf(new Vector3(-3.5f, 0f, -HZ + 0.25f), 0f, 1);
            Shelf(new Vector3(2.6f, 0f, HZ - 0.25f), 180f, 2);

            // chunky CRT TV on a crate, facing the armchair
            Vector3 tv = Local(WorldLayout.TV);
            Vector3 chair = new Vector3(0.8f, 0f, -1.4f);
            Vector3 toChair = chair - tv;
            float tvYaw = Mathf.Atan2(toChair.x, toChair.z) * Mathf.Rad2Deg;
            Quaternion tr = Quaternion.Euler(0f, tvYaw, 0f);
            Crate(new Vector3(tv.x, 0f, tv.z), 0.62f, tvYaw);
            Vector3 tvc = new Vector3(tv.x, 0.62f + 0.29f, tv.z);
            B(PropMats.TvBody).Box(tvc, new Vector3(0.74f, 0.58f, 0.52f), tr);
            B(PropMats.TvBody).Box(tvc + tr * new Vector3(0f, -0.02f, -0.38f), new Vector3(0.5f, 0.42f, 0.32f), tr);
            B(PropMats.Screen).Box(tvc + tr * new Vector3(-0.07f, 0.02f, 0.26f), new Vector3(0.5f, 0.42f, 0.02f), tr);
            B(PropMats.Ink).Box(tvc + tr * new Vector3(0.28f, 0.1f, 0.265f), new Vector3(0.07f, 0.07f, 0.03f), tr);
            B(PropMats.CushionB).Box(tvc + tr * new Vector3(0.28f, -0.06f, 0.265f), new Vector3(0.07f, 0.07f, 0.03f), tr);
            B(PropMats.Metal).Rod(tvc + new Vector3(0f, 0.29f, 0f), tvc + tr * new Vector3(-0.25f, 0.76f, -0.05f), 0.012f, 0.008f, 4);
            B(PropMats.Metal).Rod(tvc + new Vector3(0f, 0.29f, 0f), tvc + tr * new Vector3(0.22f, 0.73f, -0.1f), 0.012f, 0.008f, 4);
            Col(new Vector3(tv.x, 0.6f, tv.z), new Vector3(0.75f, 1.2f, 0.75f), tr);

            // armchair facing the TV
            Vector3 toTv = tv - chair;
            Quaternion cr = Quaternion.Euler(0f, Mathf.Atan2(toTv.x, toTv.z) * Mathf.Rad2Deg, 0f);
            var fab = B(PropMats.Armchair);
            fab.Box(chair + cr * new Vector3(0f, 0.24f, 0f), new Vector3(0.88f, 0.42f, 0.82f), cr);
            fab.Box(chair + cr * new Vector3(0f, 0.68f, -0.34f), new Vector3(0.88f, 0.82f, 0.2f), cr * Quaternion.Euler(-8f, 0f, 0f));
            fab.Box(chair + cr * new Vector3(-0.39f, 0.54f, 0.02f), new Vector3(0.14f, 0.3f, 0.8f), cr);
            fab.Box(chair + cr * new Vector3(0.39f, 0.54f, 0.02f), new Vector3(0.14f, 0.3f, 0.8f), cr);
            B(PropMats.Wall2).Box(chair + cr * new Vector3(0f, 0.47f, 0.04f), new Vector3(0.62f, 0.08f, 0.66f), cr);
            Col(chair + new Vector3(0f, 0.45f, 0f), new Vector3(0.9f, 0.9f, 0.85f), cr);

            // striped rug, crates by the door
            B(PropMats.RugA).Box(new Vector3(0.5f, 0.008f, -0.6f), new Vector3(2.4f, 0.016f, 1.8f));
            for (int i = -1; i <= 1; i++)
                B(PropMats.RugB).Box(new Vector3(0.5f + i * 0.7f, 0.018f, -0.6f), new Vector3(0.2f, 0.006f, 1.8f));
            Crate(new Vector3(3.8f, 0f, 3.5f), 0.6f, 5f);
            Crate(new Vector3(3.75f, 0.6f, 3.45f), 0.5f, 28f);
            Crate(new Vector3(3.9f, 0f, 2.8f), 0.55f, -10f);
            Col(new Vector3(3.8f, 0.55f, 3.2f), new Vector3(0.75f, 1.1f, 1.3f));

            // FinTube poster on the west wall
            float wx = -HX + T * 0.5f + 0.03f;
            B(PropMats.Poster).QuadAuto(new Vector3(wx, 1.2f, 2.45f), new Vector3(wx, 1.9f, 2.45f), new Vector3(wx, 1.9f, 3.29f), new Vector3(wx, 1.2f, 3.29f),
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f), Vector3.right);
            // fishing rod hanging on the south wall
            B(PropMats.WoodDark).Rod(new Vector3(-0.2f, 1.4f, -HZ + T * 0.5f + 0.07f), new Vector3(1.0f, 2.3f, -HZ + T * 0.5f + 0.07f), 0.02f, 0.01f, 4);

            // hanging lamp under the ridge: cone shade + glowing bulb
            Vector3 lamp = new Vector3(0.3f, 2.3f, 0f);
            B(PropMats.Ink).Rod(new Vector3(lamp.x, RidgeY - 0.2f, 0f), lamp + new Vector3(0f, 0.15f, 0f), 0.012f, 0.012f, 4);
            B(PropMats.FlowerYellow).Cylinder(lamp - new Vector3(0f, 0.05f, 0f), Quaternion.identity, 0.28f, 0.07f, 0.22f, 6, false, true);
            B(PropMats.Wall2).Cylinder(lamp - new Vector3(0f, 0.05f, 0f), Quaternion.identity, 0.27f, 0.065f, 0.215f, 6, false, false, 1f, 1f, 0f, true);
            B(PropMats.HutBulb).Ico(lamp - new Vector3(0f, 0.04f, 0f), new Vector3(0.07f, 0.08f, 0.07f), Quaternion.identity, 0);
            var lt = PropUtil.AddPointLight(root, O + lamp - new Vector3(0f, 0.15f, 0f), new Color(1f, 0.78f, 0.52f), 10f, 2.4f, "HutLamp");
            PropNightLight.Attach(lt.gameObject, lt, PropMats.HutBulb, new Color(4f, 2.9f, 1.6f), 0.12f, 0.45f, false);
        }

        static void Crate(Vector3 basePos, float size, float yaw)
        {
            Quaternion r = Quaternion.Euler(0f, yaw, 0f);
            Vector3 c = basePos + new Vector3(0f, size * 0.5f, 0f);
            B(PropMats.Wood).Box(c, new Vector3(size, size, size) * 0.94f, r);
            var d = B(PropMats.WoodDark);
            float e = size * 0.5f;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    d.Box(c + r * new Vector3(sx * (e - 0.035f), 0f, sz * (e - 0.035f)), new Vector3(0.08f, size, 0.08f), r);
            d.Box(c + r * new Vector3(0f, e - 0.03f, 0f), new Vector3(size * 0.98f, 0.06f, 0.1f), r);
        }

        static void Shelf(Vector3 basePos, float yaw, int seed)
        {
            var rng = new System.Random(seed * 31 + 7);
            Quaternion r = Quaternion.Euler(0f, yaw, 0f);
            float w = 1.6f, dpt = 0.4f, h = 1.9f;
            var d = B(PropMats.Wood);
            d.Box(basePos + r * new Vector3(-w * 0.5f, h * 0.5f, 0f), new Vector3(0.07f, h, dpt), r);
            d.Box(basePos + r * new Vector3(w * 0.5f, h * 0.5f, 0f), new Vector3(0.07f, h, dpt), r);
            Material[] goods = { PropMats.FlowerYellow, PropMats.CushionA, PropMats.CushionB, PropMats.Orange, PropMats.White };
            for (int i = 0; i < 4; i++)
            {
                float y = 0.15f + i * 0.55f;
                B(PropMats.WoodLight).Box(basePos + r * new Vector3(0f, y, 0f), new Vector3(w, 0.05f, dpt), r);
                if (i == 3) continue;
                float x = -w * 0.5f + 0.12f;
                while (x < w * 0.5f - 0.15f)
                {
                    int kind = rng.Next(4);
                    float s = PropUtil.Range(rng, 0.18f, 0.3f);
                    Vector3 p = basePos + r * new Vector3(x + s * 0.5f, y + 0.025f, PropUtil.Range(rng, -0.05f, 0.05f));
                    if (kind == 0) B(goods[rng.Next(goods.Length)]).Box(p + new Vector3(0f, s * 0.4f, 0f), new Vector3(s, s * 0.8f, s * 0.9f), r * Quaternion.Euler(0f, PropUtil.Range(rng, -8f, 8f), 0f));
                    else if (kind == 1) B(PropMats.DrumRed).Cylinder(p, Quaternion.identity, s * 0.3f, s * 0.3f, s * 0.9f, 6, false, true);
                    else if (kind == 2) B(PropMats.Rope).Torus(p + new Vector3(0f, 0.05f, 0f), Quaternion.identity, s * 0.32f, 0.045f, 8, 3);
                    else B(PropMats.Glass).Cylinder(p, Quaternion.identity, s * 0.22f, s * 0.2f, s * 0.8f, 6, false, true);
                    x += s + PropUtil.Range(rng, 0.03f, 0.12f);
                }
            }
            Col(basePos + r * new Vector3(0f, h * 0.5f, 0f), new Vector3(w + 0.05f, h, dpt), r);
        }

        // ------------------------------------------------------------------ outside details
        static void BuildExteriorDetails()
        {
            // painted sign on the east gable, above the awning
            float sx = HX + T * 0.5f + 0.05f;
            B(PropMats.WoodDark).Box(new Vector3(sx, 3.2f, 0f), new Vector3(0.07f, 1.05f, 3.6f));
            float fx = sx + 0.04f;
            B(PropMats.Sign).QuadAuto(new Vector3(fx, 2.73f, -1.73f), new Vector3(fx, 3.67f, -1.73f), new Vector3(fx, 3.67f, 1.73f), new Vector3(fx, 2.73f, 1.73f),
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f), Vector3.right);

            // turquoise door leaf swung open against the outer wall
            Vector3 door = new Vector3(HX + T * 0.5f + 0.05f, 1.06f, 1.6f + 0.67f);
            B(PropMats.Shutter).Box(door, new Vector3(0.06f, 2.05f, 1.3f));
            B(PropMats.Wood).Box(door + new Vector3(0.04f, 0f, 0f), new Vector3(0.03f, 0.1f, 1.2f));
            B(PropMats.Metal).Box(door + new Vector3(0.05f, 0f, -0.5f), new Vector3(0.04f, 0.06f, 0.1f));

            // life ring next to the door (orange / white segments)
            Vector3 lr = new Vector3(HX + T * 0.5f + 0.12f, 1.55f, -0.75f);
            const int segs = 8;
            for (int i = 0; i < segs; i++)
            {
                float a0 = i * Mathf.PI * 2f / segs, a1 = (i + 1) * Mathf.PI * 2f / segs;
                Vector3 p0 = lr + new Vector3(0f, Mathf.Sin(a0), Mathf.Cos(a0)) * 0.3f;
                Vector3 p1 = lr + new Vector3(0f, Mathf.Sin(a1), Mathf.Cos(a1)) * 0.3f;
                B(i % 2 == 0 ? PropMats.Orange : PropMats.White).Rod(p0, p1, 0.075f, 0.075f, 5, true);
            }
            // rain barrel at the north west corner
            float g = GL(-HX - 0.6f, HZ - 0.6f);
            Vector3 rb = new Vector3(-HX - 0.6f, g - 0.05f, HZ - 0.6f);
            B(PropMats.DrumBlue).Cylinder(rb, Quaternion.identity, 0.34f, 0.34f, 0.95f, 8, false, true);
            B(PropMats.DarkMetal).Cylinder(rb + Vector3.up * 0.2f, Quaternion.identity, 0.355f, 0.355f, 0.07f, 8);
            B(PropMats.DarkMetal).Cylinder(rb + Vector3.up * 0.75f, Quaternion.identity, 0.355f, 0.355f, 0.07f, 8);
            PropCol.Capsule(cols, O + new Vector3(-HX - 0.6f, g + 0.45f, HZ - 0.6f), 0.35f, 0.95f, Quaternion.identity);
        }
    }
}
