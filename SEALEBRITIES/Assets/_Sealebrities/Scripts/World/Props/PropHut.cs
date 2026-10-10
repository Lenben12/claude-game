using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// The "Sealebrity Dive Co." hut: plank walls, rusty corrugated roof, porch with steps, interior with bed, TV, lamp.
    /// Local frame: origin = WorldLayout.Hut at floor-top height, +x east (door side), +z north. Interior x [-4.5, 4.5], z [-5, 5]
    /// (sized so that WorldLayout.Bed / TV / PlayerSpawn are inside).
    public static class PropHut
    {
        const float HX = 4.5f, HZ = 5f, WallH = 2.7f, PitchDeg = 18f, T = 0.14f;

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
            // floor + joists
            B(PropMats.Wood).Box(new Vector3(0f, -0.1f, 0f), new Vector3(2f * HX + 0.3f, 0.2f, 2f * HZ + 0.3f));
            Col(new Vector3(0f, -0.15f, 0f), new Vector3(2f * HX + 0.3f, 0.3f, 2f * HZ + 0.3f));
            for (int i = -2; i <= 2; i++)
                B(PropMats.WoodDark).Box(new Vector3(i * 2.2f, -0.3f, 0f), new Vector3(0.16f, 0.2f, 2f * HZ + 0.3f));

            // stilts
            float[] xs = { -HX + 0.1f, 0f, HX - 0.1f };
            float[] zs = { -HZ + 0.1f, 0f, HZ - 0.1f };
            foreach (float x in xs) foreach (float z in zs) Stilt(x, z);

            // walls with openings (s0, s1, v0, v1)
            var west = new List<Vector4> { new Vector4(-1.9f, -0.7f, 1.0f, 1.9f), new Vector4(1.0f, 2.0f, 1.0f, 1.9f) };
            var east = new List<Vector4> { new Vector4(-3.2f, -2.0f, 1.0f, 1.9f), new Vector4(0.2f, 1.6f, 0f, 2.15f) };
            var south = new List<Vector4> { new Vector4(-1.6f, -0.4f, 1.0f, 1.9f), new Vector4(1.2f, 2.2f, 1.0f, 1.9f) };
            var north = new List<Vector4> { new Vector4(0.0f, 1.2f, 1.0f, 1.9f) };
            Wall(true, -HX, -HZ - T * 0.5f, HZ + T * 0.5f, west);
            Wall(true, HX, -HZ - T * 0.5f, HZ + T * 0.5f, east);
            Wall(false, -HZ, -HX + T * 0.5f, HX - T * 0.5f, south);
            Wall(false, HZ, -HX + T * 0.5f, HX - T * 0.5f, north);

            // corner posts and top plates
            var dark = B(PropMats.WoodDark);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    dark.Box(new Vector3(sx * (HX + 0.02f), WallH * 0.5f, sz * (HZ + 0.02f)), new Vector3(0.2f, WallH, 0.2f));
            dark.Box(new Vector3(0f, WallH - 0.06f, -HZ), new Vector3(2f * HX, 0.12f, T + 0.06f));
            dark.Box(new Vector3(0f, WallH - 0.06f, HZ), new Vector3(2f * HX, 0.12f, T + 0.06f));

            // gables (east / west)
            float rise = (HZ + T * 0.5f) * Mathf.Tan(PitchDeg * Mathf.Deg2Rad);
            var wood = B(PropMats.Wood);
            for (int s = -1; s <= 1; s += 2)
            {
                float x = s * HX;
                for (int f = -1; f <= 1; f += 2)
                {
                    float xf = x + f * T * 0.5f;
                    Vector3 a = new Vector3(xf, WallH, -HZ - T * 0.5f), b = new Vector3(xf, WallH, HZ + T * 0.5f), t = new Vector3(xf, WallH + rise, 0f);
                    wood.TriAuto(a, b, t, new Vector2(0f, WallH), new Vector2(2f * HZ + T, WallH), new Vector2(HZ, WallH + rise), new Vector3(f, 0f, 0f));
                }
            }
        }

        static void Stilt(float x, float z)
        {
            float g = GL(x, z);
            float bottom = g - 0.4f, top = -0.2f;
            if (top - bottom < 0.08f) return;
            B(PropMats.WoodDark).Cylinder(new Vector3(x, bottom, z), Quaternion.identity, 0.12f, 0.11f, top - bottom, 6, false, false, 0.75f, 1f);
            if (top - bottom > 0.6f) PropCol.Capsule(cols, O + new Vector3(x, (top + bottom) * 0.5f, z), 0.13f, top - bottom, Quaternion.identity);
        }

        /// Plank wall with rectangular openings. alongZ: wall at x = fixed running along z; else wall at z = fixed along x.
        static void Wall(bool alongZ, float fixedCoord, float s0, float s1, List<Vector4> holes)
        {
            holes.Sort((a, b) => a.x.CompareTo(b.x));
            float cur = s0;
            foreach (var h in holes)
            {
                if (h.x > cur) WallPiece(alongZ, fixedCoord, s0, cur, h.x, 0f, WallH);
                if (h.z > 0f) WallPiece(alongZ, fixedCoord, s0, h.x, h.y, 0f, h.z);
                if (h.w < WallH) WallPiece(alongZ, fixedCoord, s0, h.x, h.y, h.w, WallH);
                cur = h.y;
                Frame(alongZ, fixedCoord, h);
            }
            if (cur < s1) WallPiece(alongZ, fixedCoord, s0, cur, s1, 0f, WallH);
        }

        static void WallPiece(bool alongZ, float f, float s0, float a0, float a1, float v0, float v1)
        {
            float s = (a0 + a1) * 0.5f, y = (v0 + v1) * 0.5f;
            Vector3 center = alongZ ? new Vector3(f, y, s) : new Vector3(s, y, f);
            Vector3 size = alongZ ? new Vector3(T, v1 - v0, a1 - a0) : new Vector3(a1 - a0, v1 - v0, T);
            B(PropMats.Wood).Box(center, size, Quaternion.identity, 1f, new Vector2(a0 - s0 + f * 0.37f, v0));
            Col(center, size);
        }

        static void Frame(bool alongZ, float f, Vector4 h)
        {
            var d = B(PropMats.WoodDark);
            float depth = T + 0.08f, w = 0.07f;
            bool isDoor = h.z <= 0.001f;
            Vector3 P(float s, float y) => alongZ ? new Vector3(f, y, s) : new Vector3(s, y, f);
            Vector3 Sz(float alongS, float alongY) => alongZ ? new Vector3(depth, alongY, alongS) : new Vector3(alongS, alongY, depth);
            d.Box(P(h.x - w * 0.5f, (h.z + h.w) * 0.5f), Sz(w, h.w - h.z + w));
            d.Box(P(h.y + w * 0.5f, (h.z + h.w) * 0.5f), Sz(w, h.w - h.z + w));
            d.Box(P((h.x + h.y) * 0.5f, h.w + w * 0.5f), Sz(h.y - h.x + 2f * w, w));
            if (!isDoor)
            {
                Vector3 sill = alongZ ? new Vector3(depth + 0.1f, w, h.y - h.x + 2f * w) : new Vector3(h.y - h.x + 2f * w, w, depth + 0.1f);
                d.Box(P((h.x + h.y) * 0.5f, h.z - w * 0.5f), sill);
                // weathered shutter left open beside the window (outside)
                float outSide = Mathf.Sign(f);
                Vector3 sp = alongZ
                    ? new Vector3(f + outSide * (T * 0.5f + 0.04f), (h.z + h.w) * 0.5f, h.x - w - (h.y - h.x) * 0.25f)
                    : new Vector3(h.x - w - (h.y - h.x) * 0.25f, (h.z + h.w) * 0.5f, f + outSide * (T * 0.5f + 0.04f));
                Vector3 ss = alongZ ? new Vector3(0.04f, h.w - h.z, (h.y - h.x) * 0.5f) : new Vector3((h.y - h.x) * 0.5f, h.w - h.z, 0.04f);
                B(PropMats.WoodPale).Box(sp, ss, Quaternion.identity, 1f, default, PropUV.Default);
            }
        }

        // ------------------------------------------------------------------ roof
        static void BuildRoof()
        {
            float p = PitchDeg * Mathf.Deg2Rad;
            float ridgeY = WallH + HZ * Mathf.Tan(p);
            float over = 0.6f;
            float run = HZ + over;
            float slopeLen = run / Mathf.Cos(p);
            var roof = B(PropMats.Roof);
            for (int s = -1; s <= 1; s += 2)
            {
                Quaternion rot = Quaternion.Euler(s * PitchDeg, 0f, 0f);
                float zc = s * run * 0.5f;
                float yc = ridgeY - run * 0.5f * Mathf.Tan(p) + 0.05f;
                Vector3 center = new Vector3(0f, yc, zc);
                roof.Box(center, new Vector3(2f * HX + 2f * over, 0.05f, slopeLen), rot);
                Col(center + rot * new Vector3(0f, 0.08f, 0f), new Vector3(2f * HX + 2f * over, 0.22f, slopeLen), rot);
            }
            roof.Box(new Vector3(0f, ridgeY + 0.08f, 0f), new Vector3(2f * HX + 2f * over + 0.05f, 0.08f, 0.36f));

            // rafters, ridge beam, ties
            var dark = B(PropMats.WoodDark);
            dark.Box(new Vector3(0f, ridgeY - 0.12f, 0f), new Vector3(2f * HX, 0.18f, 0.14f));
            for (int i = -2; i <= 2; i++)
            {
                float x = i * 1.8f;
                dark.Beam(new Vector3(x, WallH, -HZ), new Vector3(x, ridgeY - 0.05f, 0f), 0.08f, 0.12f);
                dark.Beam(new Vector3(x, WallH, HZ), new Vector3(x, ridgeY - 0.05f, 0f), 0.08f, 0.12f);
            }
            dark.Box(new Vector3(-1.8f, WallH + 0.05f, 0f), new Vector3(0.1f, 0.12f, 2f * HZ));
            dark.Box(new Vector3(1.8f, WallH + 0.05f, 0f), new Vector3(0.1f, 0.12f, 2f * HZ));
        }

        // ------------------------------------------------------------------ porch, awning, steps
        static void BuildPorch(PropExclusion excl)
        {
            float px0 = HX + 0.15f, px1 = 7.2f, pz0 = -1.6f, pz1 = 3.4f;
            Vector3 pc = new Vector3((px0 + px1) * 0.5f, -0.1f, (pz0 + pz1) * 0.5f);
            Vector3 ps = new Vector3(px1 - px0, 0.2f, pz1 - pz0);
            B(PropMats.Wood).Box(pc, ps, Quaternion.identity, 1f, default, PropUV.LongU);
            Col(pc - new Vector3(0f, 0.05f, 0f), ps + new Vector3(0f, 0.1f, 0f));
            Stilt(px1 - 0.15f, pz0 + 0.1f);
            Stilt(px1 - 0.15f, pz1 - 0.1f);
            Stilt(px0 + 1.2f, pz0 + 0.1f);
            Stilt(px0 + 1.2f, pz1 - 0.1f);

            // awning on posts
            var dark = B(PropMats.WoodDark);
            float aTop = 2.55f, aLow = 2.25f, ax1 = 7.45f;
            float ang = Mathf.Atan2(aTop - aLow, ax1 - HX) * Mathf.Rad2Deg;
            float alen = (ax1 - HX) / Mathf.Cos(ang * Mathf.Deg2Rad);
            Quaternion arot = Quaternion.Euler(0f, 0f, -ang);
            B(PropMats.Roof).Box(new Vector3((HX + ax1) * 0.5f, (aTop + aLow) * 0.5f + 0.04f, 0.9f), new Vector3(alen, 0.04f, 5.6f), arot);
            dark.Box(new Vector3(7.1f, 2.2f, 0.9f), new Vector3(0.12f, 0.12f, 5.3f));
            dark.Box(new Vector3(7.1f, 1.1f, pz0 + 0.1f), new Vector3(0.12f, 2.2f, 0.12f));
            dark.Box(new Vector3(7.1f, 1.1f, pz1 - 0.1f), new Vector3(0.12f, 2.2f, 0.12f));
            // porch railing on the north/south edges
            for (int s = 0; s < 2; s++)
            {
                float z = s == 0 ? pz0 + 0.05f : pz1 - 0.05f;
                dark.Box(new Vector3((px0 + px1) * 0.5f, 0.95f, z), new Vector3(px1 - px0, 0.07f, 0.07f));
                for (int i = 0; i < 4; i++) dark.Box(new Vector3(px0 + 0.3f + i * 0.7f, 0.47f, z), new Vector3(0.06f, 0.94f, 0.06f));
                Col(new Vector3((px0 + px1) * 0.5f, 0.5f, z), new Vector3(px1 - px0, 1f, 0.1f));
            }

            // steps down to the beach
            float rise = 0.19f, runStep = 0.3f, sz = 0.9f, sw = 1.5f;
            float lastTop = 0f, lastX = px1;
            int count = 0;
            for (int i = 0; i < 40; i++)
            {
                float top = -(i + 1) * rise;
                float x = px1 + i * runStep + runStep * 0.5f;
                float g = GL(x, sz);
                if (top < g - 0.02f) break;
                B(PropMats.Wood).Box(new Vector3(x, top - 0.025f, sz), new Vector3(runStep + 0.02f, 0.05f, sw), Quaternion.identity, 1f, default, PropUV.LongU);
                float blockH = top - 0.05f - (g - 0.3f);
                if (blockH > 0.02f) dark.Box(new Vector3(x, top - 0.05f - blockH * 0.5f, sz), new Vector3(runStep, blockH, sw - 0.1f));
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
                Col(mid - r * new Vector3(0f, 0.06f, 0f), new Vector3(d.magnitude + 0.1f, 0.12f, sw), r);
                excl.AddLine(O + new Vector3(px1, 0f, sz), O + new Vector3(lastX + 1.5f, 0f, sz), 2.5f);
            }
        }

        // ------------------------------------------------------------------ interior
        static void BuildInterior(Transform root)
        {
            // bed at WorldLayout.Bed, head at the north wall
            Vector3 bed = Local(WorldLayout.Bed);
            float bx = bed.x, bz = bed.z;
            var dark = B(PropMats.WoodDark);
            dark.Box(new Vector3(bx, 0.22f, bz), new Vector3(1.0f, 0.18f, 1.9f));
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    dark.Box(new Vector3(bx + sx * 0.45f, 0.1f, bz + sz * 0.88f), new Vector3(0.08f, 0.2f, 0.08f));
            dark.Box(new Vector3(bx, 0.55f, bz + 0.93f), new Vector3(1.04f, 0.75f, 0.06f));
            B(PropMats.FabricMattress).Box(new Vector3(bx, 0.39f, bz), new Vector3(0.92f, 0.16f, 1.84f));
            B(PropMats.PlasticWhite).Box(new Vector3(bx, 0.52f, bz + 0.68f), new Vector3(0.62f, 0.11f, 0.36f), Quaternion.Euler(-8f, 3f, 0f));
            B(PropMats.FabricBlanket).Box(new Vector3(bx, 0.49f, bz - 0.25f), new Vector3(0.98f, 0.05f, 1.3f), Quaternion.Euler(0f, 1.5f, 0f));
            B(PropMats.FabricBlanket).Box(new Vector3(bx + 0.5f, 0.3f, bz - 0.25f), new Vector3(0.03f, 0.36f, 1.25f));
            var bedGo = new GameObject("Bed");
            bedGo.transform.SetParent(root, false);
            bedGo.transform.position = O + new Vector3(bx, 0.33f, bz);
            var bc = bedGo.AddComponent<BoxCollider>();
            bc.size = new Vector3(1.05f, 0.66f, 1.95f);
            bedGo.AddComponent<PropBed>();
            // nightstand crate (east of the bed, away from the player spawn)
            Crate(new Vector3(bx + 1.0f, 0f, bz + 0.6f), 0.5f, 12f);

            // table + chair + clutter (west wall)
            Vector3 tp = new Vector3(-3.6f, 0f, -0.5f);
            var pale = B(PropMats.WoodPale);
            pale.Box(tp + new Vector3(0f, 0.745f, 0f), new Vector3(0.8f, 0.05f, 1.3f));
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    pale.Box(tp + new Vector3(sx * 0.33f, 0.36f, sz * 0.58f), new Vector3(0.06f, 0.72f, 0.06f));
            Col(tp + new Vector3(0f, 0.39f, 0f), new Vector3(0.8f, 0.78f, 1.3f));
            // laptop (editing station)
            B(PropMats.DarkMetal).Box(tp + new Vector3(0.05f, 0.785f, 0.1f), new Vector3(0.24f, 0.02f, 0.34f));
            Quaternion lid = Quaternion.Euler(0f, 0f, -14f);
            Vector3 lidC = tp + new Vector3(-0.08f, 0.9f, 0.1f);
            B(PropMats.DarkMetal).Box(lidC, new Vector3(0.015f, 0.23f, 0.34f), lid);
            B(PropMats.LaptopScreen).Box(lidC + lid * new Vector3(0.01f, 0f, 0f), new Vector3(0.004f, 0.19f, 0.3f), lid);
            B(PropMats.PlasticWhite).Cylinder(tp + new Vector3(0.2f, 0.77f, -0.35f), Quaternion.identity, 0.045f, 0.045f, 0.1f, 10, false, true);
            B(PropMats.PlasticWhite).Box(tp + new Vector3(0.1f, 0.775f, -0.15f), new Vector3(0.22f, 0.006f, 0.3f), Quaternion.Euler(0f, 12f, 0f));
            // chair facing west
            Vector3 cp = new Vector3(-2.75f, 0f, -0.5f);
            var dw = B(PropMats.WoodDark);
            dw.Box(cp + new Vector3(0f, 0.45f, 0f), new Vector3(0.45f, 0.04f, 0.45f));
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    dw.Box(cp + new Vector3(sx * 0.19f, 0.22f, sz * 0.19f), new Vector3(0.04f, 0.44f, 0.04f));
            dw.Box(cp + new Vector3(0.21f, 0.72f, 0f), new Vector3(0.04f, 0.5f, 0.45f));
            Col(cp + new Vector3(0f, 0.45f, 0f), new Vector3(0.45f, 0.9f, 0.45f));

            // shelves
            Shelf(new Vector3(-3.5f, 0f, -HZ + 0.25f), 0f, 1);
            Shelf(new Vector3(2.6f, 0f, HZ - 0.25f), 180f, 2);

            // CRT TV on a crate, facing the armchair
            Vector3 tv = Local(WorldLayout.TV);
            Vector3 chair = new Vector3(0.8f, 0f, -1.4f);
            Vector3 toChair = chair - tv;
            float tvYaw = Mathf.Atan2(toChair.x, toChair.z) * Mathf.Rad2Deg;
            Quaternion tr = Quaternion.Euler(0f, tvYaw, 0f);
            Crate(new Vector3(tv.x, 0f, tv.z), 0.62f, tvYaw);
            Vector3 tvc = new Vector3(tv.x, 0.62f + 0.29f, tv.z);
            B(PropMats.PlasticBeige).Box(tvc, new Vector3(0.72f, 0.56f, 0.5f), tr);
            B(PropMats.PlasticBeige).Box(tvc + tr * new Vector3(0f, -0.02f, -0.38f), new Vector3(0.5f, 0.42f, 0.32f), tr);
            B(PropMats.Screen).Box(tvc + tr * new Vector3(-0.06f, 0.02f, 0.255f), new Vector3(0.5f, 0.4f, 0.02f), tr);
            B(PropMats.DarkMetal).Cylinder(tvc + tr * new Vector3(0.28f, 0.1f, 0.25f), tr * Quaternion.Euler(90f, 0f, 0f), 0.03f, 0.03f, 0.03f, 8, false, true);
            B(PropMats.DarkMetal).Cylinder(tvc + tr * new Vector3(0.28f, -0.05f, 0.25f), tr * Quaternion.Euler(90f, 0f, 0f), 0.03f, 0.03f, 0.03f, 8, false, true);
            B(PropMats.Metal).Rod(tvc + new Vector3(0f, 0.28f, 0f), tvc + tr * new Vector3(-0.25f, 0.75f, -0.05f), 0.006f, 0.004f, 4);
            B(PropMats.Metal).Rod(tvc + new Vector3(0f, 0.28f, 0f), tvc + tr * new Vector3(0.22f, 0.72f, -0.1f), 0.006f, 0.004f, 4);
            Col(new Vector3(tv.x, 0.6f, tv.z), new Vector3(0.75f, 1.2f, 0.75f), tr);

            // armchair facing the TV
            Vector3 toTv = tv - chair;
            Quaternion cr = Quaternion.Euler(0f, Mathf.Atan2(toTv.x, toTv.z) * Mathf.Rad2Deg, 0f);
            var fab = B(PropMats.FabricChair);
            fab.Box(chair + cr * new Vector3(0f, 0.24f, 0f), new Vector3(0.86f, 0.4f, 0.8f), cr);
            fab.Box(chair + cr * new Vector3(0f, 0.66f, -0.34f), new Vector3(0.86f, 0.8f, 0.18f), cr * Quaternion.Euler(-8f, 0f, 0f));
            fab.Box(chair + cr * new Vector3(-0.38f, 0.52f, 0.02f), new Vector3(0.13f, 0.28f, 0.78f), cr);
            fab.Box(chair + cr * new Vector3(0.38f, 0.52f, 0.02f), new Vector3(0.13f, 0.28f, 0.78f), cr);
            Col(chair + new Vector3(0f, 0.45f, 0f), new Vector3(0.9f, 0.9f, 0.85f), cr);

            // rug, crates by the door
            B(PropMats.FabricRug).Box(new Vector3(0.5f, 0.006f, -0.6f), new Vector3(2.4f, 0.012f, 1.8f));
            Crate(new Vector3(3.8f, 0f, 3.5f), 0.6f, 5f);
            Crate(new Vector3(3.75f, 0.6f, 3.45f), 0.5f, 28f);
            Crate(new Vector3(3.9f, 0f, 2.8f), 0.55f, -10f);
            Col(new Vector3(3.8f, 0.55f, 3.2f), new Vector3(0.75f, 1.1f, 1.3f));

            // FinTube poster on the west wall
            float wx = -HX + T * 0.5f + 0.012f;
            B(PropMats.Poster).QuadAuto(new Vector3(wx, 1.2f, 2.5f), new Vector3(wx, 1.95f, 2.5f), new Vector3(wx, 1.95f, 3.25f), new Vector3(wx, 1.2f, 3.25f),
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f), Vector3.right);
            // fishing rod hanging on the south wall
            B(PropMats.WoodDark).Rod(new Vector3(-0.2f, 1.4f, -HZ + T * 0.5f + 0.05f), new Vector3(1.0f, 2.3f, -HZ + T * 0.5f + 0.05f), 0.012f, 0.005f, 4);

            // hanging lamp under the ridge
            float ridgeY = WallH + HZ * Mathf.Tan(PitchDeg * Mathf.Deg2Rad);
            Vector3 lamp = new Vector3(0.3f, 2.3f, 0f);
            B(PropMats.Rubber).Rod(new Vector3(lamp.x, ridgeY - 0.2f, 0f), lamp + new Vector3(0f, 0.15f, 0f), 0.008f, 0.008f, 4);
            B(PropMats.DarkMetal).Cylinder(lamp - new Vector3(0f, 0.05f, 0f), Quaternion.identity, 0.26f, 0.06f, 0.2f, 14, false, true);
            B(PropMats.PlasticWhite).Cylinder(lamp - new Vector3(0f, 0.05f, 0f), Quaternion.identity, 0.255f, 0.055f, 0.195f, 14, false, false, 1f, 1f, 0f, true);
            B(PropMats.HutBulb).Blob(lamp - new Vector3(0f, 0.04f, 0f), new Vector3(0.06f, 0.07f, 0.06f), Quaternion.identity, 3, null);
            var light = PropUtil.AddPointLight(root, O + lamp - new Vector3(0f, 0.15f, 0f), new Color(1f, 0.76f, 0.48f), 10f, 2.4f, "HutLamp");
            PropNightLight.Attach(light.gameObject, light, PropMats.HutBulb, new Color(4f, 2.9f, 1.6f), 0.12f, 0.45f, false);
        }

        static void Crate(Vector3 basePos, float size, float yaw)
        {
            Quaternion r = Quaternion.Euler(0f, yaw, 0f);
            Vector3 c = basePos + new Vector3(0f, size * 0.5f, 0f);
            B(PropMats.Wood).Box(c, new Vector3(size, size, size) * 0.96f, r, 1f, new Vector2(yaw * 0.01f, 0.25f));
            var d = B(PropMats.WoodDark);
            float e = size * 0.5f;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    d.Box(c + r * new Vector3(sx * (e - 0.03f), 0f, sz * (e - 0.03f)), new Vector3(0.07f, size, 0.07f), r);
        }

        static void Shelf(Vector3 basePos, float yaw, int seed)
        {
            var rng = new System.Random(seed * 31 + 7);
            Quaternion r = Quaternion.Euler(0f, yaw, 0f);
            float w = 1.6f, dpt = 0.4f, h = 1.9f;
            var d = B(PropMats.WoodDark);
            d.Box(basePos + r * new Vector3(-w * 0.5f, h * 0.5f, 0f), new Vector3(0.05f, h, dpt), r);
            d.Box(basePos + r * new Vector3(w * 0.5f, h * 0.5f, 0f), new Vector3(0.05f, h, dpt), r);
            for (int i = 0; i < 4; i++)
            {
                float y = 0.15f + i * 0.55f;
                B(PropMats.WoodPale).Box(basePos + r * new Vector3(0f, y, 0f), new Vector3(w, 0.03f, dpt), r);
                float x = -w * 0.5f + 0.12f;
                while (x < w * 0.5f - 0.15f)
                {
                    int kind = rng.Next(4);
                    float s = PropUtil.Range(rng, 0.18f, 0.3f);
                    Vector3 p = basePos + r * new Vector3(x + s * 0.5f, y + 0.015f, PropUtil.Range(rng, -0.05f, 0.05f));
                    if (kind == 0) B(PropMats.Wood).Box(p + new Vector3(0f, s * 0.4f, 0f), new Vector3(s, s * 0.8f, s * 0.9f), r * Quaternion.Euler(0f, PropUtil.Range(rng, -8f, 8f), 0f));
                    else if (kind == 1) B(PropMats.DrumRed).Cylinder(p, Quaternion.identity, s * 0.3f, s * 0.3f, s * 0.9f, 8, false, true);
                    else if (kind == 2) B(PropMats.Rope).Torus(p + new Vector3(0f, 0.04f, 0f), Quaternion.identity, s * 0.32f, 0.035f, 10, 5, 3f, 1f);
                    else B(PropMats.Glass).Cylinder(p, Quaternion.identity, s * 0.22f, s * 0.2f, s * 0.8f, 8, false, true);
                    x += s + PropUtil.Range(rng, 0.03f, 0.12f);
                }
            }
            Col(basePos + r * new Vector3(0f, h * 0.5f, 0f), new Vector3(w + 0.05f, h, dpt), r);
        }

        // ------------------------------------------------------------------ outside details
        static void BuildExteriorDetails()
        {
            // hand painted sign on the east gable, above the awning
            float sx = HX + T * 0.5f + 0.05f;
            B(PropMats.WoodDark).Box(new Vector3(sx, 3.2f, 0f), new Vector3(0.06f, 0.95f, 3.5f));
            float fx = sx + 0.045f;
            B(PropMats.Sign).QuadAuto(new Vector3(fx, 2.75f, -1.7f), new Vector3(fx, 3.65f, -1.7f), new Vector3(fx, 3.65f, 1.7f), new Vector3(fx, 2.75f, 1.7f),
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f), Vector3.right);

            // door leaf swung open against the outer wall
            B(PropMats.WoodDark).Box(new Vector3(HX + T * 0.5f + 0.04f, 1.06f, 1.6f + 0.67f), new Vector3(0.05f, 2.05f, 1.3f), Quaternion.identity, 1f, default, PropUV.Default);
            // life ring next to the door
            B(PropMats.FabricSock).Torus(new Vector3(HX + T * 0.5f + 0.1f, 1.55f, -0.75f), Quaternion.FromToRotation(Vector3.up, Vector3.right), 0.3f, 0.07f, 16, 8, 1f, 1f);
            // rain barrel at the north west corner
            float g = GL(-HX - 0.6f, HZ - 0.6f);
            B(PropMats.DrumBlue).Cylinder(new Vector3(-HX - 0.6f, g - 0.05f, HZ - 0.6f), Quaternion.identity, 0.32f, 0.32f, 0.95f, 14, false, true, 2f, 1f);
            PropCol.Capsule(cols, O + new Vector3(-HX - 0.6f, g + 0.45f, HZ - 0.6f), 0.33f, 0.95f, Quaternion.identity);
        }
    }
}
