using UnityEngine;

namespace Sealebrities
{
    /// Small wooden shack in the How to Fish forest-hut style (art bible §1.11, reference 3.png): vertical boards
    /// #8B6B5A / #7A5E50 with dark gaps, door with a light frame, a 4-pane window with off-white frame, gable roof with
    /// dark shingles (0.4 m overhang), short stilts. Solid walls, gables and roof. Used for the boathouse, the stilt
    /// fishing hut on the kelp island and the ruined radio shack (concrete variant).
    public static class PropShack
    {
        public struct Spec
        {
            public float width, depth, wallH, pitchDeg;
            public float doorX;          // door center on the front wall (+z), NaN = no door
            public float doorW;
            public bool windowLeft, windowRight, windowBack;
            public bool concrete;        // concrete walls, no roof (ruin)
            public bool noRoof;
            public int seed;
        }

        public static Spec Default(float w, float d, int seed) => new Spec
        {
            width = w, depth = d, wallH = 2.3f, pitchDeg = 32f, doorX = 0f, doorW = 1.0f, windowLeft = true, windowRight = false, windowBack = false, seed = seed
        };

        static PropBatch batch;
        static Transform cols;
        static Matrix4x4 M;
        static Quaternion R;
        static Vector3 cell;

        static PropMeshBuilder B(Material m)
        {
            var b = batch.B(m, cell);
            b.M = M;
            return b;
        }

        static void Col(Vector3 local, Vector3 size, Quaternion localRot) => PropCol.Box(cols, M.MultiplyPoint3x4(local), size, R * localRot, "ShackCol");

        static uint Hash(int a, int b, int c)
        {
            unchecked
            {
                uint h = (uint)(a * 374761393 + b * 668265263 + c * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                return h ^ (h >> 16);
            }
        }

        /// Builds the shack. floorY = world height of the floor top; groundCenter.y is ignored.
        /// Returns the world position of the door threshold (outside).
        public static Vector3 Build(PropBatch b, Transform colliders, Vector3 groundCenter, float yawDeg, float floorY, Spec s,
            System.Func<float, float, float> ground = null)
        {
            if (ground == null) ground = PropGround.H;
            batch = b;
            cols = colliders;
            R = Quaternion.Euler(0f, yawDeg, 0f);
            Vector3 origin = new Vector3(groundCenter.x, floorY, groundCenter.z);
            M = Matrix4x4.TRS(origin, R, Vector3.one);
            cell = origin;
            float hw = s.width * 0.5f, hd = s.depth * 0.5f;
            const float T = 0.12f;

            // floor and stilts
            Material[] fl = { PropMats.Wood, PropMats.Wood2, PropMats.Wood3 };
            int k = 0;
            for (float x = -hw + 0.11f; x < hw; x += 0.22f, k++)
                B(fl[(int)(Hash(k, s.seed, 1) % 3)]).Box(new Vector3(x, -0.05f, 0f), new Vector3(0.2f, 0.1f, s.depth));
            B(PropMats.PlankGap).Box(new Vector3(0f, -0.13f, 0f), new Vector3(s.width - 0.05f, 0.08f, s.depth - 0.05f));
            Col(new Vector3(0f, -0.12f, 0f), new Vector3(s.width, 0.24f, s.depth), Quaternion.identity);
            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    Vector3 lp = new Vector3(sx * (hw - 0.1f), 0f, sz * (hd - 0.1f));
                    Vector3 wp = M.MultiplyPoint3x4(lp);
                    float g = ground(wp.x, wp.z);
                    float h = floorY - 0.15f - (g - 0.5f);
                    if (h > 0.05f) B(PropMats.WoodDark).Box(new Vector3(lp.x, -0.15f - h * 0.5f, lp.z), new Vector3(0.16f, h, 0.16f));
                }
            }

            Material boardA = s.concrete ? PropMats.Concrete : PropMats.ShackBoard;
            Material boardB = s.concrete ? PropMats.ConcreteDark : PropMats.ShackBoard2;

            // walls: front (+z, door), back (-z), left (-x), right (+x)
            float doorH = 2.0f;
            bool hasDoor = !float.IsNaN(s.doorX);
            WallV(boardA, boardB, s, 0, new Vector3(0f, 0f, hd), Vector3.right, s.width, hasDoor ? s.doorX : float.NaN, s.doorW, 0f, doorH, T);
            WallV(boardA, boardB, s, 1, new Vector3(0f, 0f, -hd), Vector3.right, s.width, s.windowBack ? 0f : float.NaN, 0.9f, 1.0f, 1.8f, T);
            WallV(boardA, boardB, s, 2, new Vector3(-hw, 0f, 0f), Vector3.forward, s.depth, s.windowLeft ? 0f : float.NaN, 0.9f, 1.0f, 1.8f, T);
            WallV(boardA, boardB, s, 3, new Vector3(hw, 0f, 0f), Vector3.forward, s.depth, s.windowRight ? 0f : float.NaN, 0.9f, 1.0f, 1.8f, T);

            // corner posts
            var post = B(s.concrete ? PropMats.ConcreteDark : PropMats.WoodDark);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    post.Box(new Vector3(sx * hw, s.wallH * 0.5f, sz * hd), new Vector3(0.16f, s.wallH, 0.16f));

            // door frame + leaf (swung open inward)
            if (hasDoor && !s.concrete)
            {
                var fr = B(PropMats.WoodLight);
                fr.Box(new Vector3(s.doorX - s.doorW * 0.5f - 0.05f, doorH * 0.5f, hd), new Vector3(0.1f, doorH, T + 0.06f));
                fr.Box(new Vector3(s.doorX + s.doorW * 0.5f + 0.05f, doorH * 0.5f, hd), new Vector3(0.1f, doorH, T + 0.06f));
                fr.Box(new Vector3(s.doorX, doorH + 0.05f, hd), new Vector3(s.doorW + 0.2f, 0.1f, T + 0.06f));
                Quaternion leaf = Quaternion.Euler(0f, 70f, 0f);
                Vector3 hinge = new Vector3(s.doorX - s.doorW * 0.5f, 0f, hd - T * 0.5f);
                for (int i = 0; i < 4; i++)
                {
                    Vector3 c = hinge + leaf * new Vector3(0.12f + i * 0.24f, doorH * 0.5f, -0.03f);
                    B(i % 2 == 0 ? PropMats.ShackBoard2 : PropMats.ShackBoard).Box(c, new Vector3(0.22f, doorH - 0.05f, 0.05f), leaf);
                }
            }

            if (!s.noRoof && !s.concrete) Roof(s, T);
            batch = null;
            cols = null;
            return M.MultiplyPoint3x4(new Vector3(hasDoor ? s.doorX : 0f, 0f, hd + 0.8f));
        }

        /// Vertical-board wall centered at c, running along 'along' for 'len'. Optional opening (center offset ox along
        /// the wall, width ow, from y0 to y1).
        static void WallV(Material a, Material bm, Spec s, int wall, Vector3 c, Vector3 along, float len, float ox, float ow, float y0, float y1, float T)
        {
            Vector3 n = Vector3.Cross(along, Vector3.up);   // outward or inward – boards are full thickness anyway
            bool opening = !float.IsNaN(ox);
            float o0 = opening ? ox - ow * 0.5f : 0f, o1 = opening ? ox + ow * 0.5f : 0f;
            const float pitch = 0.2f, gap = 0.025f;
            float half = len * 0.5f;
            // backing panel (dark gaps)
            Quaternion rot = Quaternion.LookRotation(n, Vector3.up);
            Vector3 bsize = new Vector3(len, s.wallH, T * 0.4f);
            if (!opening) B(PropMats.PlankGap).Box(c + Vector3.up * (s.wallH * 0.5f), bsize, rot);
            int i = 0;
            for (float t = -half; t < half - 0.02f; t += pitch, i++)
            {
                float b0 = t, b1 = Mathf.Min(half, t + pitch - gap);
                float mid = (b0 + b1) * 0.5f;
                uint h = Hash(i, wall, s.seed);
                Material m = (h & 1) == 0 ? a : bm;
                float off = ((h >> 4 & 255) / 255f - 0.5f) * 0.02f;
                bool cut = opening && b1 > o0 && b0 < o1;
                if (!cut)
                {
                    Vector3 p = c + along * mid + n * off + Vector3.up * (s.wallH * 0.5f);
                    B(m).Box(p, new Vector3(b1 - b0, s.wallH, T), rot);
                }
                else
                {
                    if (y0 > 0.02f)
                    {
                        Vector3 p = c + along * mid + n * off + Vector3.up * (y0 * 0.5f);
                        B(m).Box(p, new Vector3(b1 - b0, y0, T), rot);
                    }
                    if (y1 < s.wallH - 0.02f)
                    {
                        Vector3 p = c + along * mid + n * off + Vector3.up * ((y1 + s.wallH) * 0.5f);
                        B(m).Box(p, new Vector3(b1 - b0, s.wallH - y1, T), rot);
                    }
                }
            }
            // colliders
            if (!opening) Col(c + Vector3.up * (s.wallH * 0.5f), new Vector3(len, s.wallH, T), rot);
            else
            {
                float l0 = o0 + half, l1 = half - o1;
                if (l0 > 0.05f) Col(c + along * (-half + l0 * 0.5f) + Vector3.up * (s.wallH * 0.5f), new Vector3(l0, s.wallH, T), rot);
                if (l1 > 0.05f) Col(c + along * (o1 + l1 * 0.5f) + Vector3.up * (s.wallH * 0.5f), new Vector3(l1, s.wallH, T), rot);
                if (y0 > 0.05f) Col(c + along * ox + Vector3.up * (y0 * 0.5f), new Vector3(ow, y0, T), rot);
                if (y1 < s.wallH - 0.05f) Col(c + along * ox + Vector3.up * ((y1 + s.wallH) * 0.5f), new Vector3(ow, s.wallH - y1, T), rot);
                // backing beside the opening
                if (l0 > 0.05f) B(PropMats.PlankGap).Box(c + along * (-half + l0 * 0.5f) + Vector3.up * (s.wallH * 0.5f), new Vector3(l0, s.wallH, T * 0.4f), rot);
                if (l1 > 0.05f) B(PropMats.PlankGap).Box(c + along * (o1 + l1 * 0.5f) + Vector3.up * (s.wallH * 0.5f), new Vector3(l1, s.wallH, T * 0.4f), rot);
                // window: glass + off-white frame and cross (not for the door, y0 == 0)
                if (y0 > 0.05f && !s.concrete)
                {
                    Vector3 wc = c + along * ox + Vector3.up * ((y0 + y1) * 0.5f);
                    B(PropMats.WindowGlass).Box(wc, new Vector3(ow, y1 - y0, 0.03f), rot);
                    var tr = B(PropMats.Trim);
                    tr.Box(wc + Vector3.up * ((y1 - y0) * 0.5f + 0.04f), new Vector3(ow + 0.16f, 0.08f, T + 0.06f), rot);
                    tr.Box(wc - Vector3.up * ((y1 - y0) * 0.5f + 0.04f), new Vector3(ow + 0.2f, 0.08f, T + 0.1f), rot);
                    tr.Box(wc - along * (ow * 0.5f + 0.04f), new Vector3(0.08f, y1 - y0 + 0.16f, T + 0.06f), rot);
                    tr.Box(wc + along * (ow * 0.5f + 0.04f), new Vector3(0.08f, y1 - y0 + 0.16f, T + 0.06f), rot);
                    tr.Box(wc, new Vector3(0.05f, y1 - y0, 0.06f), rot);
                    tr.Box(wc, new Vector3(ow, 0.05f, 0.06f), rot);
                }
            }
        }

        /// Gable roof with the ridge along z (gables front and back), dark shingle rows, 0.4 m overhang.
        static void Roof(Spec s, float T)
        {
            float hw = s.width * 0.5f, hd = s.depth * 0.5f;
            float p = s.pitchDeg * Mathf.Deg2Rad;
            float rise = hw * Mathf.Tan(p);
            float ridgeY = s.wallH + rise;
            const float over = 0.4f;
            float run = hw + over;
            float slopeLen = run / Mathf.Cos(p);
            float len = s.depth + 2f * over;
            const int rows = 4;
            float seg = slopeLen / rows;
            for (int side = -1; side <= 1; side += 2)
            {
                Quaternion rot = Quaternion.Euler(0f, 0f, -side * s.pitchDeg);
                for (int r = 0; r < rows; r++)
                {
                    float d = (r + 0.5f) * seg;
                    Vector3 c = new Vector3(side * d * Mathf.Cos(p), ridgeY + 0.06f - d * Mathf.Sin(p), 0f);
                    B(r % 2 == 0 ? PropMats.Shingle : PropMats.ShingleDark).Box(c, new Vector3(seg * 1.12f, 0.08f, len + (r % 2) * 0.05f), Quaternion.Euler(0f, 0f, -side * (s.pitchDeg + 3f)));
                }
                Vector3 cc = new Vector3(side * run * 0.5f, ridgeY - run * 0.5f * Mathf.Tan(p), 0f);
                Col(cc + rot * new Vector3(0f, 0.05f, 0f), new Vector3(slopeLen + 0.2f, 0.36f, len), rot);
            }
            B(PropMats.WoodDark).Box(new Vector3(0f, ridgeY + 0.08f, 0f), new Vector3(0.22f, 0.22f, len + 0.1f), Quaternion.Euler(0f, 0f, 45f));
            // gables: vertical boards cut by the roof line, dark backing
            for (int f = -1; f <= 1; f += 2)
            {
                float z = f * hd;
                Vector3 n = new Vector3(0f, 0f, f);
                B(PropMats.PlankGap).Face3(new Vector3(-hw, s.wallH, z + f * 0.02f), new Vector3(hw, s.wallH, z + f * 0.02f), new Vector3(0f, ridgeY, z + f * 0.02f), n);
                B(PropMats.PlankGap).Face3(new Vector3(-hw, s.wallH, z - f * 0.02f), new Vector3(hw, s.wallH, z - f * 0.02f), new Vector3(0f, ridgeY, z - f * 0.02f), -n);
                int i = 0;
                for (float x = -hw; x < hw - 0.05f; x += 0.2f, i++)
                {
                    float xm = x + 0.0875f;
                    float top = s.wallH + (hw - Mathf.Abs(xm) - 0.09f) * Mathf.Tan(p);
                    if (top <= s.wallH + 0.04f) continue;
                    var m = (Hash(i, 9 + f, s.seed) & 1) == 0 ? PropMats.ShackBoard : PropMats.ShackBoard2;
                    B(m).Box(new Vector3(xm, (s.wallH + top) * 0.5f, z), new Vector3(0.175f, top - s.wallH, T));
                }
                // convex prism collider
                var v = new[]
                {
                    new Vector3(-hw, s.wallH - 0.05f, z - T), new Vector3(hw, s.wallH - 0.05f, z - T), new Vector3(0f, ridgeY + 0.05f, z - T),
                    new Vector3(-hw, s.wallH - 0.05f, z + T), new Vector3(hw, s.wallH - 0.05f, z + T), new Vector3(0f, ridgeY + 0.05f, z + T),
                };
                var tris = new[] { 0, 2, 1, 3, 4, 5, 0, 1, 4, 0, 4, 3, 1, 2, 5, 1, 5, 4, 0, 3, 5, 0, 5, 2 };
                var mesh = new Mesh { name = "ShackGableCol", vertices = v, triangles = tris };
                mesh.RecalculateBounds();
                var mc = PropCol.Mesh(cols, mesh, M.MultiplyPoint3x4(Vector3.zero), R, Vector3.one, "ShackGable");
                mc.convex = true;
            }
        }
    }
}
