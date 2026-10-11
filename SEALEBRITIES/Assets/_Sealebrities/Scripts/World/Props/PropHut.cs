using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// The "Sealebrity Dive Co." hut in the How to Fish beach-shop style (art bible §1.11): weathered whitewashed
    /// clapboard (Whitewash #DCCFBE in three ±6 % variants) with 2–3 cm gaps showing a dark backing, red-brown accent
    /// boards at the base, off-white trim, turquoise shutters, 4-pane windows, a red shingle roof and a striped porch
    /// awning; chunky interior with bed, sofa bunk, TV, desk, shelves, fridge and locker. Walls, gables, roof and awning
    /// are solid (feedback #10).
    /// Round 2b: a wide veranda with two hammocks (sleep spots, L.9), the sofa bunk, the hut fridge (E.1), the locker
    /// (1c), the golden camera trophy (Q52), Gus' letter (K.5), a roof ladder (L.12) and the hut yard (L.5 spot 1):
    /// washing line with wetsuits, Gus' upturned rowboat „Rostlaube“ as a bench, surfboard rack, Gus' old compressor
    /// with a hose to the dock (E.4).
    /// Local frame: origin = WorldLayout.Hut (shifted west) at floor-top height, +x east (door side), +z north.
    /// Interior x [-4.5, 4.5], z [-5, 5] (sized so that WorldLayout.Bed / TV / PlayerSpawn are inside).
    public static class PropHut
    {
        const float HX = 4.5f, HZ = 5f, WallH = 2.7f, PitchDeg = 20f, T = 0.14f, BoardH = 0.22f, BoardGap = 0.025f;
        /// Veranda: x from the east wall to PorchX1, z ±PorchZ; the steps leave it at z = StepsZ.
        const float PorchX1 = 7.6f, PorchZ = 4.75f, StepsZ = 0.9f;
        static int wallId;

        static PropBatch batch;
        static Matrix4x4 TO;
        static Vector3 O;
        static Transform cols, root;

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
        static Vector3 W(Vector3 local) => O + local;

        /// The hut is shifted slightly west of WorldLayout.Hut so WorldLayout.PlayerSpawn has ~0.8 m clearance to the west wall.
        public const float WestShift = 0.4f;

        static float RidgeY => WallH + HZ * Mathf.Tan(PitchDeg * Mathf.Deg2Rad);

        public static IEnumerator Build(Transform island, PropExclusion excl)
        {
            Vector3 c = WorldLayout.Hut - new Vector3(WestShift, 0f, 0f);
            PropGround.MinMaxRect(new Vector3(c.x + 1.6f, 0f, c.z), Quaternion.identity, 6.6f, 5.8f, 1f, out float gmin, out float gmax);
            float floorY = Mathf.Max(gmax + 0.3f, 1.1f);
            O = new Vector3(c.x, floorY, c.z);
            TO = Matrix4x4.Translate(O);
            PropInfo.HutFloorY = floorY;
            PropInfo.HutDoor = O + new Vector3(HX + 1.2f, 0f, 0.9f);

            root = PropUtil.WorldRoot("Hut", island);
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
            BuildYard(excl);
            if (PropRun.Due()) yield return null;

            batch.Flush(root, "HutMesh", ShadowCastingMode.On, 0f);
            batch = null;
            excl.Add(c.x + 1.6f, c.z, 10f);
            excl.Add(WorldLayout.PlayerSpawn.x, WorldLayout.PlayerSpawn.z, 2f);
            PropInfo.AddLandSpot(W(new Vector3(0f, RidgeY + 0.25f, 0f)), LandSpotKind.Perch, 4f, "hut");
            PropInfo.AddLandSpot(W(new Vector3(-HX - 0.1f, 1.4f, -2.5f)), LandSpotKind.Wall, 1.5f, "hut", Vector3.left);
            PropInfo.AddLandSpot(W(new Vector3(0.5f, 1.6f, HZ + 0.1f)), LandSpotKind.Wall, 1.5f, "hut", Vector3.forward);
            PropInfo.AddLandSpot(W(new Vector3(PorchX1 - 0.1f, 2.4f, -PorchZ)), LandSpotKind.Perch, 1f, "hut");
        }

        // ------------------------------------------------------------------ shell
        static void BuildShell()
        {
            // floor: separate boards along z with small gaps over a dark skirt (plank look without textures)
            int k = 0;
            Material[] floorWood = { PropMats.Wood, PropMats.Wood2, PropMats.Wood3 };
            for (float x = -HX; x < HX + 0.16f; x += 0.25f, k++)
                B(floorWood[(k * 7 + 3) % 3]).Box(new Vector3(x, -0.06f, 0f), new Vector3(0.22f, 0.12f, 2f * HZ + 0.3f));
            B(PropMats.PlankGap).Box(new Vector3(0f, -0.15f, 0f), new Vector3(2f * HX + 0.2f, 0.1f, 2f * HZ + 0.2f));
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
            var south = new List<Vector4> { new Vector4(-1.6f, -0.4f, 1.15f, 1.95f), new Vector4(1.2f, 2.2f, 1.0f, 1.9f) };
            var north = new List<Vector4> { new Vector4(-0.7f, 0.3f, 1.0f, 1.9f) };
            wallId = 1; Wall(true, -HX, -HZ - T * 0.5f, HZ + T * 0.5f, west);
            wallId = 2; Wall(true, HX, -HZ - T * 0.5f, HZ + T * 0.5f, east);
            wallId = 3; Wall(false, -HZ, -HX + T * 0.5f, HX - T * 0.5f, south);
            wallId = 4; Wall(false, HZ, -HX + T * 0.5f, HX - T * 0.5f, north);

            // off-white corner posts and top plates
            var trim = B(PropMats.Trim);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    trim.Box(new Vector3(sx * (HX + 0.02f), WallH * 0.5f, sz * (HZ + 0.02f)), new Vector3(0.22f, WallH, 0.22f));
            trim.Box(new Vector3(0f, WallH - 0.06f, -HZ), new Vector3(2f * HX, 0.14f, T + 0.08f));
            trim.Box(new Vector3(0f, WallH - 0.06f, HZ), new Vector3(2f * HX, 0.14f, T + 0.08f));

            // gables (east / west): horizontal whitewashed boards shortened under the roof line, dark backing, solid prism
            float rise = (HZ + T * 0.5f) * Mathf.Tan(PitchDeg * Mathf.Deg2Rad);
            for (int s = -1; s <= 1; s += 2)
            {
                float x = s * HX;
                var gap = B(PropMats.PlankGap);
                for (int f = -1; f <= 1; f += 2)
                {
                    float xf = x + f * T * 0.2f;
                    Vector3 a = new Vector3(xf, WallH, -HZ - T * 0.5f), b = new Vector3(xf, WallH, HZ + T * 0.5f), t = new Vector3(xf, WallH + rise, 0f);
                    gap.Face3(a, b, t, new Vector3(f, 0f, 0f));
                }
                int row = 0;
                for (float y = WallH; y < WallH + rise - 0.05f; y += BoardH, row++)
                {
                    float yTop = Mathf.Min(y + BoardH - BoardGap, WallH + rise);
                    float half = (HZ + T * 0.5f) * (1f - (yTop - WallH) / rise);
                    if (half < 0.08f) break;
                    uint h = Hash(row, 20 + s, 7);
                    float off = ((h & 255) / 255f - 0.5f) * 0.02f;
                    B(PropMats.Whitewash((int)((h >> 8) % 3))).Box(new Vector3(x + s * off, (y + yTop) * 0.5f, 0f), new Vector3(T, yTop - y, 2f * half));
                }
                GablePrism(x, rise);
                // a little round attic vent in the west gable (the east gable carries the sign)
                if (s > 0) continue;
                B(PropMats.Trim).Cylinder(new Vector3(x + s * (T * 0.5f), WallH + rise * 0.45f, 0f), Quaternion.Euler(0f, 0f, -90f * s), 0.24f, 0.24f, 0.04f, 8, false, true);
                B(PropMats.Ink).Cylinder(new Vector3(x + s * (T * 0.5f + 0.04f), WallH + rise * 0.45f, 0f), Quaternion.Euler(0f, 0f, -90f * s), 0.15f, 0.15f, 0.005f, 8, false, true);
            }
        }

        static uint Hash(int a, int b, int c)
        {
            unchecked
            {
                uint h = (uint)(a * 374761393 + b * 668265263 + c * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                return h ^ (h >> 16);
            }
        }

        /// Convex triangular prism collider closing a gable (the space between the wall top and the roof).
        static void GablePrism(float x, float rise)
        {
            float z = HZ + T * 0.5f;
            var v = new[]
            {
                new Vector3(x - T, WallH - 0.05f, -z), new Vector3(x - T, WallH - 0.05f, z), new Vector3(x - T, WallH + rise + 0.05f, 0f),
                new Vector3(x + T, WallH - 0.05f, -z), new Vector3(x + T, WallH - 0.05f, z), new Vector3(x + T, WallH + rise + 0.05f, 0f),
            };
            var t = new[] { 0, 2, 1, 3, 4, 5, 0, 1, 4, 0, 4, 3, 1, 2, 5, 1, 5, 4, 0, 3, 5, 0, 5, 2 };
            var m = new Mesh { name = "HutGableCol" };
            m.vertices = v;
            m.triangles = t;
            m.RecalculateBounds();
            var mc = PropCol.Mesh(cols, m, O, Quaternion.identity, Vector3.one, "GableCol");
            mc.convex = true;
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

        /// One wall rectangle as horizontal boards (0.22 m pitch, 2.5 cm gaps) over a dark backing; three whitewash
        /// variants, a random 0–1 cm out-of-plane offset per board, red-brown accent boards in the two lowest rows.
        static void WallPiece(bool alongZ, float f, float a0, float a1, float v0, float v1)
        {
            float outSign = Mathf.Sign(f);
            float s = (a0 + a1) * 0.5f;
            float yc = (v0 + v1) * 0.5f;
            Vector3 bc = alongZ ? new Vector3(f, yc, s) : new Vector3(s, yc, f);
            Vector3 bs = alongZ ? new Vector3(T * 0.45f, v1 - v0, a1 - a0) : new Vector3(a1 - a0, v1 - v0, T * 0.45f);
            B(PropMats.PlankGap).Box(bc, bs);
            int k0 = Mathf.FloorToInt(v0 / BoardH + 1e-3f), k1 = Mathf.CeilToInt(v1 / BoardH - 1e-3f);
            for (int k = k0; k < k1; k++)
            {
                float y0 = Mathf.Max(v0, k * BoardH), y1 = Mathf.Min(v1, (k + 1) * BoardH - BoardGap);
                if (y1 - y0 < 0.02f) continue;
                uint h = Hash(k, wallId, Mathf.RoundToInt(a0 * 10f));
                float off = ((h & 255) / 255f - 0.5f) * 0.02f * outSign;
                float y = (y0 + y1) * 0.5f;
                Vector3 center = alongZ ? new Vector3(f + off, y, s) : new Vector3(s, y, f + off);
                Vector3 size = alongZ ? new Vector3(T, y1 - y0, a1 - a0) : new Vector3(a1 - a0, y1 - y0, T);
                Material m = k < 2 ? PropMats.AccentBoard : PropMats.Whitewash((int)((h >> 8) % 3));
                B(m).Box(center, size);
            }
            Vector3 cs = alongZ ? new Vector3(T, v1 - v0, a1 - a0) : new Vector3(a1 - a0, v1 - v0, T);
            Col(bc, cs);
        }

        static void Frame(bool alongZ, float f, Vector4 h)
        {
            var d = B(PropMats.Trim);
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
                // 4-pane window: light glass and a cross of off-white mullions
                float cs = (h.x + h.y) * 0.5f, cy = (h.z + h.w) * 0.5f;
                Vector3 gs = alongZ ? new Vector3(0.02f, h.w - h.z, h.y - h.x) : new Vector3(h.y - h.x, h.w - h.z, 0.02f);
                B(PropMats.Glass).Box(P(cs, cy), gs);
                Vector3 mv = alongZ ? new Vector3(0.05f, h.w - h.z, 0.05f) : new Vector3(0.05f, h.w - h.z, 0.05f);
                Vector3 mh = alongZ ? new Vector3(0.05f, 0.05f, h.y - h.x) : new Vector3(h.y - h.x, 0.05f, 0.05f);
                d.Box(P(cs, cy), mv);
                d.Box(P(cs, cy), mh);
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
                // thick slabs (no tunnelling when jumping against them), overlapping at the ridge
                Col(center + prot * new Vector3(0f, 0.02f, -s * 0.15f), new Vector3(width + 0.1f, 0.4f, slopeLen + 0.3f), prot);
            }
            Col(new Vector3(0f, ridgeY + 0.05f, 0f), new Vector3(width + 0.1f, 0.45f, 0.6f));
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

            // L.12: a wooden ladder at the north-west corner leaning on the eave (the chopper sometimes delivers up there)
            float lx = -HX + 0.9f;
            float eaveZ = HZ + over;
            float eave = WallH - over * Mathf.Tan(p) - 0.05f;
            float footZ = eaveZ + 0.75f, topZ = eaveZ + 0.08f;
            float footY = GL(lx, footZ);
            float railTop = eave + 1.0f;
            var lw = B(PropMats.Wood);
            for (int s = -1; s <= 1; s += 2)
                lw.Beam(new Vector3(lx + s * 0.25f, footY - 0.05f, footZ), new Vector3(lx + s * 0.25f, railTop, topZ), 0.06f, 0.06f);
            for (float y = footY + 0.3f; y < railTop - 0.15f; y += 0.3f)
            {
                float t = (y - footY) / (railTop - footY);
                float z = Mathf.Lerp(footZ, topZ, t);
                lw.Rod(new Vector3(lx - 0.25f, y, z), new Vector3(lx + 0.25f, y, z), 0.025f, 0.025f, 5);
            }
            Vector3 roofTop = new Vector3(lx, ridgeY - (HZ - 0.4f) * Mathf.Tan(p) + 0.14f, HZ - 0.4f);
            PropLadder.Create(root, "HutRoofLadder", W(new Vector3(lx, footY, footZ)), O.y + railTop, W(roofTop), Vector3.forward, LadderInfo.Wood);
            PropInfo.SetSpot("hut_roof", W(new Vector3(0f, RidgeY - 0.6f, 1.6f)));
        }

        // ------------------------------------------------------------------ porch (veranda), awning, steps, hammocks
        static void BuildPorch(PropExclusion excl)
        {
            float px0 = HX + 0.15f, px1 = PorchX1, pz0 = -PorchZ, pz1 = PorchZ;
            int k = 0;
            for (float x = px0 + 0.15f; x < px1; x += 0.3f, k++)
                B(k % 3 == 2 ? PropMats.WoodLight : PropMats.Wood).Box(new Vector3(x, -0.06f, (pz0 + pz1) * 0.5f), new Vector3(0.28f, 0.12f, pz1 - pz0));
            B(PropMats.WoodDark).Box(new Vector3((px0 + px1) * 0.5f, -0.17f, (pz0 + pz1) * 0.5f), new Vector3(px1 - px0, 0.12f, pz1 - pz0 - 0.05f));
            Vector3 pc = new Vector3((px0 + px1) * 0.5f, -0.1f, (pz0 + pz1) * 0.5f);
            Vector3 ps = new Vector3(px1 - px0, 0.2f, pz1 - pz0);
            Col(pc - new Vector3(0f, 0.05f, 0f), ps + new Vector3(0f, 0.1f, 0f));
            for (int s = -1; s <= 1; s += 2)
            {
                Stilt(px1 - 0.15f, s * (PorchZ - 0.1f));
                Stilt(px0 + 1.2f, s * (PorchZ - 0.1f));
            }
            Stilt(px1 - 0.15f, StepsZ - 1.0f);
            Stilt(px1 - 0.15f, StepsZ + 1.0f);

            // striped awning over the whole veranda on four posts, zig-zag valance
            var wood = B(PropMats.Wood);
            float aTop = 2.55f, aLow = 2.25f, ax1 = PorchX1 + 0.25f;
            float ang = Mathf.Atan2(aTop - aLow, ax1 - HX) * Mathf.Rad2Deg;
            float alen = (ax1 - HX) / Mathf.Cos(ang * Mathf.Deg2Rad);
            Quaternion arot = Quaternion.Euler(0f, 0f, -ang);
            const int stripes = 10;
            float az0 = -PorchZ - 0.25f, sw = (2f * PorchZ + 0.5f) / stripes;
            for (int i = 0; i < stripes; i++)
            {
                float zc = az0 + (i + 0.5f) * sw;
                var m = i % 2 == 0 ? PropMats.Roof : PropMats.White;
                B(m).Box(new Vector3((HX + ax1) * 0.5f, (aTop + aLow) * 0.5f + 0.04f, zc), new Vector3(alen, 0.05f, sw), arot);
                Vector3 e0 = new Vector3(ax1, aLow + 0.02f, zc - sw * 0.5f), e1 = new Vector3(ax1, aLow + 0.02f, zc + sw * 0.5f);
                B(m).Tri2(e0, e1, new Vector3(ax1, aLow - 0.24f, zc), Vector3.right);
            }
            float postX = PorchX1 - 0.1f;
            wood.Box(new Vector3(postX, 2.2f, 0f), new Vector3(0.14f, 0.14f, 2f * PorchZ + 0.3f));
            float[] postZ = { -PorchZ + 0.1f, StepsZ - 0.85f, StepsZ + 0.85f, PorchZ - 0.1f };
            foreach (float z in postZ)
            {
                wood.Box(new Vector3(postX, 1.1f, z), new Vector3(0.14f, 2.2f, 0.14f));
                Col(new Vector3(postX, 1.1f, z), new Vector3(0.16f, 2.2f, 0.16f));
            }
            // the awning is solid: nobody climbs through it into the gable
            Col(new Vector3((HX + ax1) * 0.5f, (aTop + aLow) * 0.5f + 0.06f, az0 + stripes * sw * 0.5f), new Vector3(alen + 0.1f, 0.16f, stripes * sw + 0.1f), arot);
            // railings: north / south edges and the east edge beside the steps
            for (int s = 0; s < 2; s++)
            {
                float z = s == 0 ? pz0 + 0.05f : pz1 - 0.05f;
                wood.Box(new Vector3((px0 + px1) * 0.5f, 0.95f, z), new Vector3(px1 - px0, 0.09f, 0.09f));
                for (int i = 0; i < 4; i++) wood.Box(new Vector3(px0 + 0.3f + i * 0.85f, 0.47f, z), new Vector3(0.08f, 0.94f, 0.08f));
                Col(new Vector3((px0 + px1) * 0.5f, 0.5f, z), new Vector3(px1 - px0, 1f, 0.1f));
            }
            for (int s = -1; s <= 1; s += 2)
            {
                float zA = s < 0 ? -PorchZ : StepsZ + 0.85f, zB = s < 0 ? StepsZ - 0.85f : PorchZ;
                wood.Box(new Vector3(postX, 0.95f, (zA + zB) * 0.5f), new Vector3(0.09f, 0.09f, zB - zA));
                for (float z = zA + 0.6f; z < zB - 0.2f; z += 0.7f) wood.Box(new Vector3(postX, 0.47f, z), new Vector3(0.08f, 0.94f, 0.08f));
                Col(new Vector3(postX, 0.5f, (zA + zB) * 0.5f), new Vector3(0.1f, 1f, zB - zA));
            }
            // flower boxes on the east railing
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 fb = new Vector3(postX, 1.06f, StepsZ + s * 2.2f);
                B(PropMats.Shutter).Box(fb, new Vector3(0.22f, 0.16f, 0.8f));
                var rng = new System.Random(31 + s);
                for (int i = 0; i < 3; i++)
                {
                    Vector3 bp = fb + new Vector3(0f, 0.14f, -0.25f + i * 0.25f);
                    B(PropMats.GrassDark).Ico(bp, new Vector3(0.12f, 0.12f, 0.14f), Quaternion.Euler(0f, i * 40f, 0f), 0);
                    B(i == 1 ? PropMats.FlowerYellow : PropMats.FlowerPink).Octa(bp + new Vector3(0.04f, 0.12f, PropUtil.Range(rng, -0.05f, 0.05f)), 0.05f);
                }
            }

            // L.9: two porch hammocks (north and south end of the veranda) – sleep spots for co-op guests
            for (int s = -1; s <= 1; s += 2)
            {
                float z = s * (PorchZ - 0.85f);
                Vector3 a = new Vector3(HX + 0.2f, 1.72f, z), b = new Vector3(PorchX1 - 0.2f, 1.72f, z);
                PorchHammock(a, b, s);
                Vector3 mid = W(new Vector3((a.x + b.x) * 0.5f, 1.05f, z));
                Vector3 stand = W(new Vector3((a.x + b.x) * 0.5f, 0.02f, z - s * 0.95f));
                int idx = PropBed.Register(s < 0 ? "porch_hammock_south" : "porch_hammock_north", mid + Vector3.up * 0.25f + Vector3.left * 0.9f, 90f, stand, s < 0 ? 0f : 180f, SleepSpotInfo.Hammock);
                var go = new GameObject(s < 0 ? "PorchHammockSouth" : "PorchHammockNorth");
                go.transform.SetParent(root, false);
                go.transform.position = mid;
                var bc = go.AddComponent<BoxCollider>();
                bc.size = new Vector3(2.4f, 0.35f, 0.9f);
                var bed = go.AddComponent<PropBed>();
                bed.spotIndex = idx;
                bed.kind = SleepSpotInfo.Hammock;
            }

            // steps down to the beach
            float rise = 0.19f, runStep = 0.3f, sz = StepsZ, swidth = 1.5f;
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

        /// A striped hammock hanging along x between the wall hook a and the porch post hook b (local).
        static void PorchHammock(Vector3 a, Vector3 b, int side)
        {
            Vector3 dir = (b - a).normalized;
            Vector3 lat = Vector3.Cross(Vector3.up, dir).normalized;
            const int n = 8;
            const float sag = 0.62f;
            Vector3 P(float t, float w) => Vector3.Lerp(a, b, t) - Vector3.up * (sag * Mathf.Sin(t * Mathf.PI)) + lat * (w * Mathf.Sin(Mathf.Clamp01(t * 1.15f - 0.075f) * Mathf.PI) * 0.45f) - Vector3.up * (Mathf.Abs(w) * 0.1f * Mathf.Sin(t * Mathf.PI));
            Material[] stripes = side < 0
                ? new[] { PropMats.Flat(PropPal.Turquoise), PropMats.Trim, PropMats.Flat(PropPal.Sun), PropMats.Trim }
                : new[] { PropMats.Flat(PropPal.Coral), PropMats.Trim, PropMats.Flat(PropPal.Turquoise), PropMats.Trim };
            for (int s = 0; s < 4; s++)
            {
                float w0 = -1f + s * 0.5f, w1 = w0 + 0.5f;
                var m = B(stripes[s]);
                for (int i = 0; i < n; i++)
                {
                    float t0 = i / (float)n, t1 = (i + 1) / (float)n;
                    m.Quad2(P(t0, w0), P(t1, w0), P(t1, w1), P(t0, w1), Vector3.up);
                }
            }
            var rope = B(PropMats.Rope);
            rope.Rod(a, P(0.06f, 0f), 0.018f, 0.018f, 3);
            rope.Rod(b, P(0.94f, 0f), 0.018f, 0.018f, 3);
            B(PropMats.DarkMetal).Torus(a, Quaternion.Euler(0f, 0f, 90f), 0.05f, 0.012f, 6, 3);
            B(PropMats.DarkMetal).Torus(b, Quaternion.Euler(0f, 0f, 90f), 0.05f, 0.012f, 6, 3);
            // a pillow at the head end
            B(PropMats.White).Box(P(0.22f, 0f) + Vector3.up * 0.06f, new Vector3(0.45f, 0.1f, 0.3f), Quaternion.Euler(0f, 0f, 12f));
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
            var pb = bedGo.AddComponent<PropBed>();
            pb.kind = SleepSpotInfo.Bed;
            pb.spotIndex = PropBed.Register("hut_bed", W(new Vector3(bx, 0.75f, bz + 0.65f)), 180f, W(new Vector3(bx + 0.95f, 0.02f, bz - 0.2f)), 270f, SleepSpotInfo.Bed);
            // nightstand crate (east of the bed, away from the player spawn)
            Crate(new Vector3(bx + 1.0f, 0f, bz + 0.6f), 0.5f, 12f);

            // desk + chair + laptop + Gus' letter (west wall)
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
            // K.5: Gus' letter (envelope + folded sheet) – „E – Gus' Brief lesen“
            Vector3 letter = tp + new Vector3(0.12f, 0.785f, -0.18f);
            B(PropMats.Paper).Box(letter, new Vector3(0.22f, 0.012f, 0.3f), Quaternion.Euler(0f, 12f, 0f));
            B(PropMats.Flat(PropMats.Value(PropPal.Paper, 0.9f))).Box(letter + new Vector3(0.02f, 0.012f, -0.04f), new Vector3(0.16f, 0.01f, 0.24f), Quaternion.Euler(0f, -6f, 0f));
            B(PropMats.LighthouseRed).Cylinder(letter + new Vector3(0.0f, 0.016f, 0.08f), Quaternion.identity, 0.025f, 0.025f, 0.008f, 6, true, true);
            var lgo = new GameObject("GusLetter");
            lgo.transform.SetParent(root, false);
            lgo.transform.position = W(letter + Vector3.up * 0.05f);
            var lsc = lgo.AddComponent<SphereCollider>();
            lsc.isTrigger = true;
            lsc.radius = 0.28f;
            var rd = lgo.AddComponent<PropReadable>();
            rd.verbDe = "Gus' Brief lesen"; rd.verbEn = "Read Gus's letter";
            rd.titleDe = "Brief von Onkel Gus"; rd.titleEn = "Letter from Uncle Gus";
            rd.textDe = PropReadable.LetterDe; rd.textEn = PropReadable.LetterEn;
            rd.gusLetter = true;
            PropInfo.SetSpot("gus_letter", W(letter));
            // chair facing west
            Vector3 cp = new Vector3(-2.75f, 0f, -0.5f);
            var cw = B(PropMats.Wood);
            cw.Box(cp + new Vector3(0f, 0.45f, 0f), new Vector3(0.46f, 0.06f, 0.46f));
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    cw.Box(cp + new Vector3(sx * 0.19f, 0.22f, sz * 0.19f), new Vector3(0.06f, 0.44f, 0.06f));
            cw.Box(cp + new Vector3(0.21f, 0.74f, 0f), new Vector3(0.06f, 0.5f, 0.46f));
            Col(cp + new Vector3(0f, 0.45f, 0f), new Vector3(0.46f, 0.9f, 0.46f));

            // shelves (north wall west, north wall east – the east one carries the golden camera trophy)
            Shelf(new Vector3(-3.35f, 0f, HZ - 0.25f), 180f, 1);
            Vector3 shelf2 = new Vector3(2.6f, 0f, HZ - 0.25f);
            Shelf(shelf2, 180f, 2);
            Trophy(root, shelf2 + new Vector3(0f, 1.88f, 0f));

            // L.9: sofa bunk under the south windows (sleep spot)
            SofaBunk(root, new Vector3(-1.05f, 0f, -HZ + 0.5f));

            // E.1: the hut fridge (north wall, between the bed and the shelf)
            Fridge(root, new Vector3(0.78f, 0f, HZ - 0.42f));

            // 1c: the locker (west wall, north of the window)
            Locker(root, new Vector3(-HX + 0.36f, 0f, 3.45f));

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
            B(PropMats.Poster).QuadAuto(new Vector3(wx, 1.2f, 2.0f), new Vector3(wx, 1.9f, 2.0f), new Vector3(wx, 1.9f, 2.84f), new Vector3(wx, 1.2f, 2.84f),
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f), Vector3.right);
            // Gus' 1987 documentary poster on the north wall above the bed
            float nz = HZ - T * 0.5f - 0.03f;
            B(PropMats.GusPoster).QuadAuto(new Vector3(-2.2f, 1.35f, nz), new Vector3(-2.2f, 2.25f, nz), new Vector3(-1.2f, 2.25f, nz), new Vector3(-1.2f, 1.35f, nz),
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f), Vector3.back);
            // fishing rod hanging on the south wall (east of the windows)
            B(PropMats.WoodDark).Rod(new Vector3(2.45f, 2.3f, -HZ + T * 0.5f + 0.07f), new Vector3(4.05f, 1.5f, -HZ + T * 0.5f + 0.07f), 0.02f, 0.01f, 4);

            // hanging lamp under the ridge: cone shade + glowing bulb
            Vector3 lamp = new Vector3(0.3f, 2.3f, 0f);
            B(PropMats.Ink).Rod(new Vector3(lamp.x, RidgeY - 0.2f, 0f), lamp + new Vector3(0f, 0.15f, 0f), 0.012f, 0.012f, 4);
            B(PropMats.FlowerYellow).Cylinder(lamp - new Vector3(0f, 0.05f, 0f), Quaternion.identity, 0.28f, 0.07f, 0.22f, 6, false, true);
            B(PropMats.Wall2).Cylinder(lamp - new Vector3(0f, 0.05f, 0f), Quaternion.identity, 0.27f, 0.065f, 0.215f, 6, false, false, 1f, 1f, 0f, true);
            B(PropMats.HutBulb).Ico(lamp - new Vector3(0f, 0.04f, 0f), new Vector3(0.07f, 0.08f, 0.07f), Quaternion.identity, 0);
            var lt = PropUtil.AddPointLight(root, O + lamp - new Vector3(0f, 0.15f, 0f), new Color(1f, 0.78f, 0.52f), 10f, 2.4f, "HutLamp");
            PropNightLight.Attach(lt.gameObject, lt, PropMats.HutBulb, new Color(4f, 2.9f, 1.6f), 0.12f, 0.45f, false);
        }

        static void SofaBunk(Transform root, Vector3 c)
        {
            // a low wooden bunk with a turquoise mattress and cushions along the south wall (2.1 × 0.85 m)
            var w = B(PropMats.Wood2);
            w.Box(c + new Vector3(0f, 0.2f, 0f), new Vector3(2.1f, 0.16f, 0.86f));
            for (int sx = -1; sx <= 1; sx += 2) w.Box(c + new Vector3(sx * 1.0f, 0.36f, 0f), new Vector3(0.1f, 0.72f, 0.86f));
            w.Box(c + new Vector3(0f, 0.52f, -0.4f), new Vector3(2.1f, 0.64f, 0.08f));
            B(PropMats.CushionA).Box(c + new Vector3(0f, 0.36f, 0.03f), new Vector3(1.9f, 0.16f, 0.78f));
            B(PropMats.CushionB).Box(c + new Vector3(-0.62f, 0.56f, -0.24f), new Vector3(0.55f, 0.32f, 0.16f), Quaternion.Euler(-12f, 4f, 0f));
            B(PropMats.FlowerYellow).Box(c + new Vector3(0.1f, 0.56f, -0.25f), new Vector3(0.5f, 0.3f, 0.15f), Quaternion.Euler(-14f, -6f, 0f));
            B(PropMats.Blanket).Box(c + new Vector3(0.62f, 0.46f, 0.05f), new Vector3(0.6f, 0.06f, 0.8f), Quaternion.Euler(0f, 8f, 0f));
            var go = new GameObject("SofaBunk");
            go.transform.SetParent(root, false);
            go.transform.position = W(c + new Vector3(0f, 0.3f, 0f));
            var bc = go.AddComponent<BoxCollider>();
            bc.size = new Vector3(2.1f, 0.6f, 0.86f);
            var pb = go.AddComponent<PropBed>();
            pb.kind = SleepSpotInfo.Bunk;
            pb.spotIndex = PropBed.Register("hut_sofa_bunk", W(c + new Vector3(-0.75f, 0.75f, 0.05f)), 90f, W(c + new Vector3(0f, 0.02f, 1.05f)), 180f, SleepSpotInfo.Bunk);
        }

        static void Fridge(Transform root, Vector3 c)
        {
            // chunky retro fridge (rounded corners read as bevelled boxes), mint body, chrome handle, Horst magnet
            Color mint = LowPoly.Hex("#BFE3D6");
            B(PropMats.Flat(mint)).Box(c + new Vector3(0f, 0.86f, 0f), new Vector3(0.74f, 1.62f, 0.62f));
            B(PropMats.Flat(PropMats.Value(mint, 0.9f))).Box(c + new Vector3(0f, 1.69f, 0f), new Vector3(0.7f, 0.08f, 0.58f));
            B(PropMats.Flat(PropMats.Value(mint, 0.85f))).Box(c + new Vector3(0f, 1.18f, -0.315f), new Vector3(0.7f, 0.02f, 0.02f));
            B(PropMats.Metal).Box(c + new Vector3(0.28f, 1.38f, -0.34f), new Vector3(0.04f, 0.28f, 0.05f));
            B(PropMats.Metal).Box(c + new Vector3(0.28f, 0.82f, -0.34f), new Vector3(0.04f, 0.22f, 0.05f));
            B(PropMats.DarkMetal).Box(c + new Vector3(0f, 0.04f, 0f), new Vector3(0.7f, 0.08f, 0.58f));
            B(PropMats.Starfish).Box(c + new Vector3(-0.15f, 1.45f, -0.315f), new Vector3(0.08f, 0.08f, 0.01f), Quaternion.Euler(0f, 0f, 45f));
            B(PropMats.Paper).Box(c + new Vector3(-0.05f, 1.05f, -0.315f), new Vector3(0.16f, 0.2f, 0.005f), Quaternion.Euler(0f, 0f, -6f));
            Col(c + new Vector3(0f, 0.85f, 0f), new Vector3(0.76f, 1.7f, 0.64f));
            var go = new GameObject("Fridge");
            go.transform.SetParent(root, false);
            go.transform.position = W(c + new Vector3(0f, 1.0f, -0.33f));
            var sc = go.AddComponent<BoxCollider>();
            sc.isTrigger = true;
            sc.size = new Vector3(0.8f, 1.6f, 0.15f);
            go.AddComponent<PropFoodSpot>().kind = "fridge";
            PropInfo.SetSpot("fridge", W(c), 180f);
        }

        static void Locker(Transform root, Vector3 c)
        {
            // tall turquoise metal locker facing east, slats + a sticker
            Quaternion r = Quaternion.Euler(0f, 90f, 0f);
            B(PropMats.Shutter).Box(c + new Vector3(0f, 0.95f, 0f), new Vector3(0.55f, 1.9f, 0.85f));
            var dk = B(PropMats.DarkMetal);
            for (int i = 0; i < 3; i++) dk.Box(c + new Vector3(0.28f, 1.55f + i * 0.08f, 0f), new Vector3(0.01f, 0.03f, 0.5f));
            dk.Box(c + new Vector3(0.29f, 0.95f, 0.36f), new Vector3(0.03f, 0.18f, 0.04f));
            B(PropMats.Flat(PropPal.Sun)).Box(c + new Vector3(0.281f, 1.15f, -0.15f), new Vector3(0.005f, 0.16f, 0.16f));
            Col(c + new Vector3(0f, 0.95f, 0f), new Vector3(0.56f, 1.9f, 0.86f));
            var go = new GameObject("Locker");
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(W(c + new Vector3(0.3f, 1.0f, 0f)), r);
            var bc = go.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.size = new Vector3(0.85f, 1.8f, 0.12f);
            go.AddComponent<PropLocker>();
            PropInfo.SetSpot("locker", W(c), 90f);
        }

        /// Q52: the golden camera trophy on the shelf – shown when the Sealog is 100 % (achievement golden_camera).
        static void Trophy(Transform root, Vector3 c)
        {
            var b = new PropMeshBuilder();
            b.Cylinder(Vector3.zero, Quaternion.identity, 0.12f, 0.1f, 0.06f, 8, false, true);
            b.Cylinder(new Vector3(0f, 0.06f, 0f), Quaternion.identity, 0.03f, 0.03f, 0.12f, 6);
            b.Box(new Vector3(0f, 0.27f, 0f), new Vector3(0.24f, 0.17f, 0.15f), Quaternion.identity);
            b.Cylinder(new Vector3(0f, 0.27f, 0.07f), Quaternion.Euler(90f, 0f, 0f), 0.06f, 0.07f, 0.1f, 10, true, true);
            b.Cylinder(new Vector3(-0.05f, 0.4f, 0f), Quaternion.Euler(0f, 0f, 90f), 0.07f, 0.07f, 0.04f, 10, true, true);
            b.Cylinder(new Vector3(0.08f, 0.4f, 0f), Quaternion.Euler(0f, 0f, 90f), 0.06f, 0.06f, 0.04f, 10, true, true);
            var mesh = b.ToMesh("GoldenCamera");
            var go = new GameObject("GoldenCameraTrophy");
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(W(c), Quaternion.Euler(0f, 180f, 0f));
            var mat = PropMats.EmissiveInstance(LowPoly.Hex("#E8C65A"), LowPoly.Hex("#E8C65A") * 0.2f);
            PropUtil.MeshObject(go.transform, "Body", mesh, mat, Vector3.zero, Quaternion.identity);
            var t = go.AddComponent<PropTrophy>();
            t.model = go.transform.GetChild(0).gameObject;
            PropInfo.SetSpot("golden_camera", W(c));
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
            // rain barrel at the south west corner
            float g = GL(-HX - 0.6f, -HZ + 0.6f);
            Vector3 rb = new Vector3(-HX - 0.6f, g - 0.05f, -HZ + 0.6f);
            B(PropMats.DrumBlue).Cylinder(rb, Quaternion.identity, 0.34f, 0.34f, 0.95f, 8, false, true);
            B(PropMats.DarkMetal).Cylinder(rb + Vector3.up * 0.2f, Quaternion.identity, 0.355f, 0.355f, 0.07f, 8);
            B(PropMats.DarkMetal).Cylinder(rb + Vector3.up * 0.75f, Quaternion.identity, 0.355f, 0.355f, 0.07f, 8);
            PropCol.Capsule(cols, O + new Vector3(-HX - 0.6f, g + 0.45f, -HZ + 0.6f), 0.35f, 0.95f, Quaternion.identity);
        }

        // ------------------------------------------------------------------ hut yard (L.5 spot 1)
        static void BuildYard(PropExclusion excl)
        {
            // washing line with three little wetsuits and a towel behind the hut (south side)
            float lz = -HZ - 2.4f;
            Vector3 la = new Vector3(-4.4f, 0f, lz), lb = new Vector3(2.0f, 0f, lz);
            la.y = GL(la.x, la.z); lb.y = GL(lb.x, lb.z);
            var post = B(PropMats.WoodDark);
            post.Cylinder(la - Vector3.up * 0.4f, Quaternion.identity, 0.06f, 0.05f, 2.4f, 6, false, true);
            post.Cylinder(lb - Vector3.up * 0.4f, Quaternion.identity, 0.06f, 0.05f, 2.4f, 6, false, true);
            PropCol.Capsule(cols, W(la + Vector3.up * 0.9f), 0.08f, 1.9f, Quaternion.identity, "LinePost");
            PropCol.Capsule(cols, W(lb + Vector3.up * 0.9f), 0.08f, 1.9f, Quaternion.identity, "LinePost");
            Vector3 ta = la + Vector3.up * 1.9f, tb = lb + Vector3.up * 1.9f;
            var rope = B(PropMats.Rope);
            Vector3 prev = ta;
            for (int i = 1; i <= 8; i++)
            {
                float t = i / 8f;
                Vector3 p = Vector3.Lerp(ta, tb, t) - Vector3.up * (0.18f * Mathf.Sin(t * Mathf.PI));
                rope.Rod(prev, p, 0.012f, 0.012f, 3);
                prev = p;
            }
            Color[] suits = { LowPoly.Hex("#EE7F2D"), LowPoly.Hex("#3FB8C4"), LowPoly.Hex("#E0607E") };
            for (int i = 0; i < 3; i++)
            {
                float t = 0.22f + i * 0.24f;
                Vector3 hang = Vector3.Lerp(ta, tb, t) - Vector3.up * (0.18f * Mathf.Sin(t * Mathf.PI));
                Wetsuit(hang, suits[i], i);
            }
            Vector3 towel = Vector3.Lerp(ta, tb, 0.92f) - Vector3.up * 0.06f;
            B(PropMats.CushionB).Box(towel - Vector3.up * 0.35f, new Vector3(0.45f, 0.7f, 0.02f));
            B(PropMats.White).Box(towel - Vector3.up * 0.35f + new Vector3(0f, 0.1f, -0.012f), new Vector3(0.45f, 0.08f, 0.005f));

            // Gus' upturned rowboat „Rostlaube“ as a bench by the beach campfire, facing the fire
            Vector3 fire = Local(PropBeach.CampfireSpot);
            Vector3 rbp = fire + new Vector3(0.3f, 0f, 2.7f);
            rbp.y = GL(rbp.x, rbp.z) + 0.02f;
            Quaternion rbr = Quaternion.Euler(0f, 90f, 180f);
            PropBeach.RowingBoat(batch, TO * Matrix4x4.TRS(rbp + new Vector3(0f, 0.55f, 0f), rbr, new Vector3(0.95f, 0.95f, 0.85f)), PropMats.Flat(PropPal.Rust), false);
            Vector3 sign = rbp + new Vector3(1.75f, 0f, -0.7f);
            sign.y = GL(sign.x, sign.z);
            B(PropMats.WoodDark).Cylinder(sign - Vector3.up * 0.3f, Quaternion.Euler(0f, 0f, 4f), 0.035f, 0.03f, 1.1f, 5, false, true);
            B(PropMats.Trim).Box(sign + new Vector3(0f, 0.72f, -0.03f), new Vector3(0.72f, 0.16f, 0.03f), Quaternion.Euler(0f, 0f, -3f));
            NameBoard(sign + new Vector3(0f, 0.72f, -0.05f));
            Col(rbp + new Vector3(0f, 0.27f, 0f), new Vector3(3.0f, 0.55f, 1.05f));
            AddSeat(rbp + new Vector3(-0.5f, 0.52f, 0f), 180f, rbp + new Vector3(-0.5f, 0f, -1.2f));
            excl.Add(O.x + rbp.x, O.z + rbp.z, 2.2f);

            // surfboard rack against the north wall
            Vector3 rk = new Vector3(1.6f, 0f, HZ + 0.55f);
            rk.y = GL(rk.x, rk.z);
            var rw = B(PropMats.WoodDark);
            for (int s = -1; s <= 1; s += 2) rw.Box(rk + new Vector3(s * 0.8f, 0.7f, 0f), new Vector3(0.08f, 1.5f, 0.08f));
            rw.Box(rk + new Vector3(0f, 1.2f, 0f), new Vector3(1.7f, 0.07f, 0.12f));
            rw.Box(rk + new Vector3(0f, 0.45f, 0f), new Vector3(1.7f, 0.07f, 0.12f));
            Material[] boards = { PropMats.Surfboard, PropMats.Flat(PropPal.Turquoise), PropMats.Flat(PropPal.Sun) };
            for (int i = 0; i < 3; i++)
                PropBeach.Surfboard(batch, W(rk + new Vector3(-0.5f + i * 0.5f, 1.05f, 0.14f + i * 0.03f)), Quaternion.Euler(-80f + i * 2f, 0f, 0f), boards[i]);
            Col(rk + new Vector3(0f, 0.8f, 0.15f), new Vector3(1.8f, 1.6f, 0.45f));

            // Gus' old compressor (E.4: the hut zone refills air) at the north-east corner, a hose down to the dock
            Vector3 cp = new Vector3(HX + 1.6f, 0f, HZ + 1.5f);
            cp.y = GL(cp.x, cp.z);
            Compressor(cp);
            Vector3 hoseA = W(cp + new Vector3(0.45f, 0.25f, 0f));
            Vector3 dock = new Vector3(WorldLayout.DockStart.x - 6f, 0f, WorldLayout.DockStart.z + 1.6f);
            Hose(hoseA, dock);
            PropInfo.SetSpot("compressor", W(cp));
            excl.Add(O.x + cp.x, O.z + cp.z, 1.6f);
        }

        static void Wetsuit(Vector3 hang, Color c, int i)
        {
            var m = B(PropMats.Flat(c));
            var dk = B(PropMats.Flat(PropMats.Value(c, 0.6f)));
            Quaternion r = Quaternion.Euler(0f, 0f, (i - 1) * 4f);
            Vector3 top = hang - Vector3.up * 0.05f;
            m.Box(top - r * new Vector3(0f, 0.32f, 0f), new Vector3(0.36f, 0.55f, 0.04f), r);
            m.Box(top - r * new Vector3(0.24f, 0.28f, 0f), new Vector3(0.1f, 0.45f, 0.035f), r * Quaternion.Euler(0f, 0f, 12f));
            m.Box(top - r * new Vector3(-0.24f, 0.28f, 0f), new Vector3(0.1f, 0.45f, 0.035f), r * Quaternion.Euler(0f, 0f, -12f));
            m.Box(top - r * new Vector3(0.09f, 0.86f, 0f), new Vector3(0.14f, 0.55f, 0.035f), r);
            m.Box(top - r * new Vector3(-0.09f, 0.86f, 0f), new Vector3(0.14f, 0.55f, 0.035f), r);
            dk.Box(top - r * new Vector3(0f, 0.1f, 0f), new Vector3(0.38f, 0.05f, 0.045f), r);
            B(PropMats.WoodLight).Box(top + new Vector3(0f, 0.02f, 0f), new Vector3(0.04f, 0.09f, 0.03f));
        }

        /// „ROSTLAUBE“ in pixel letters (tiny boxes) on a board facing −z (local).
        static void NameBoard(Vector3 c)
        {
            const string text = "ROSTLAUBE";
            float cell = 0.0125f;
            float w = PropFont.Width(text, 1f) * cell;
            var ink = B(PropMats.LighthouseRed);
            for (int k = 0; k < text.Length; k++)
                for (int row = 0; row < 7; row++)
                    for (int col = 0; col < 5; col++)
                    {
                        if (!PropFont.Bit(text[k], col, row)) continue;
                        float x = -w * 0.5f + (k * 6 + col + 0.5f) * cell;
                        float y = (3.5f - row) * cell;
                        ink.Box(c + new Vector3(x, y, -0.004f), new Vector3(cell * 0.95f, cell * 0.95f, 0.006f));
                    }
        }

        static void Compressor(Vector3 c)
        {
            var body = B(PropMats.Flat(PropPal.LighthouseRed));
            body.Box(c + new Vector3(0f, 0.42f, 0f), new Vector3(0.9f, 0.5f, 0.55f));
            B(PropMats.Metal).Cylinder(c + new Vector3(-0.15f, 0.72f, 0f), Quaternion.Euler(0f, 0f, 90f), 0.17f, 0.17f, 0.7f, 8, true, true);
            B(PropMats.DarkMetal).Cylinder(c + new Vector3(0.28f, 0.68f, 0.14f), Quaternion.identity, 0.06f, 0.06f, 0.2f, 6, false, true);
            B(PropMats.Ink).Cylinder(c + new Vector3(0.36f, 0.12f, 0.3f), Quaternion.Euler(90f, 0f, 0f), 0.12f, 0.12f, 0.06f, 8, true, true);
            B(PropMats.Ink).Cylinder(c + new Vector3(-0.36f, 0.12f, 0.3f), Quaternion.Euler(90f, 0f, 0f), 0.12f, 0.12f, 0.06f, 8, true, true);
            B(PropMats.Ink).Cylinder(c + new Vector3(0.36f, 0.12f, -0.36f), Quaternion.Euler(90f, 0f, 0f), 0.12f, 0.12f, 0.06f, 8, true, true);
            B(PropMats.Ink).Cylinder(c + new Vector3(-0.36f, 0.12f, -0.36f), Quaternion.Euler(90f, 0f, 0f), 0.12f, 0.12f, 0.06f, 8, true, true);
            B(PropMats.White).Cylinder(c + new Vector3(0.1f, 0.68f, 0.28f), Quaternion.Euler(90f, 0f, 0f), 0.07f, 0.07f, 0.02f, 8, true, true);
            B(PropMats.Ink).Box(c + new Vector3(0.1f, 0.69f, 0.295f), new Vector3(0.01f, 0.05f, 0.005f), Quaternion.Euler(0f, 0f, 35f));
            Col(c + new Vector3(0f, 0.45f, 0f), new Vector3(0.95f, 0.9f, 0.7f));
        }

        static void Hose(Vector3 a, Vector3 dockEnd)
        {
            // a yellow air hose lying on the ground in gentle curves
            dockEnd.y = PropGround.H(dockEnd.x, dockEnd.z) + 0.04f;
            float len = Vector2.Distance(new Vector2(a.x, a.z), new Vector2(dockEnd.x, dockEnd.z));
            int n = Mathf.Max(4, Mathf.CeilToInt(len / 1.5f));
            Vector3 side = Vector3.Cross(Vector3.up, (dockEnd - a).normalized);
            Vector3 prev = a;
            var hose = batch.W(PropMats.Flat(PropPal.Sun), dockEnd);
            for (int i = 1; i <= n; i++)
            {
                float t = i / (float)n;
                Vector3 p = Vector3.Lerp(a, dockEnd, t) + side * (Mathf.Sin(t * 9f) * 0.6f * Mathf.Sin(t * Mathf.PI));
                p.y = PropGround.H(p.x, p.z) + 0.05f;
                if (i == 1) p.y = Mathf.Max(p.y, a.y - 0.2f);
                hose.Rod(prev, p, 0.035f, 0.035f, 4);
                prev = p;
            }
        }

        static void AddSeat(Vector3 local, float yaw, Vector3 standLocal)
        {
            var go = new GameObject("Seat");
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(W(local), Quaternion.Euler(0f, yaw, 0f));
            var bc = go.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.size = new Vector3(1.0f, 0.5f, 0.6f);
            var seat = new GameObject("SeatPoint").transform;
            seat.SetParent(go.transform, false);
            seat.localPosition = new Vector3(0f, -0.62f, 0f);
            var s = go.AddComponent<PropSeat>();
            s.seat = seat;
            s.standPos = W(standLocal) + Vector3.up * 0.05f;
        }
    }

    /// The golden camera trophy (Q52): visible once the Sealog is complete (achievement golden_camera).
    public class PropTrophy : MonoBehaviour
    {
        public GameObject model;
        float next;

        void Update()
        {
            if (model == null || Time.unscaledTime < next) return;
            next = Time.unscaledTime + 1f;
            bool on = GameManager.I != null && GameManager.I.InGame && Achievements.IsUnlocked("golden_camera");
            if (model.activeSelf != on) model.SetActive(on);
        }
    }
}
