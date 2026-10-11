using UnityEngine;

namespace Sealebrities
{
    /// Small reusable world-space builders for the round-2b detail pass, the village and the islands (How to Fish
    /// style: chunky boxes and 6–8 sided cylinders in flat palette colors). Everything writes into a PropBatch
    /// (merged per cell) and optional colliders; positions are world space with y = ground.
    public static class PropKit
    {
        static float G(float x, float z) => PropGround.H(x, z);
        static Quaternion Yaw(float deg) => Quaternion.Euler(0f, deg, 0f);

        // ------------------------------------------------------------------ signs
        /// A post sign with a painted text label ("|" = second line). Board faces yaw (+z of the yaw rotation).
        public static void Sign(PropBatch b, Transform cols, Vector3 ground, float yaw, string text, Color board, Color ink, float height = 1.35f, float boardH = 0.34f)
        {
            Quaternion r = Yaw(yaw);
            float w = boardH * PropTex.LabelAspect(text);
            var post = b.W(PropMats.WoodDark, ground);
            post.Cylinder(ground - Vector3.up * 0.35f, Quaternion.Euler(0f, 0f, 2f), 0.05f, 0.045f, height + 0.35f, 6, false, true);
            if (w > 0.9f)
            {
                // wide boards get two posts
                post.Cylinder(ground + r * new Vector3(-w * 0.42f, -0.35f, -0.03f), Quaternion.identity, 0.045f, 0.04f, height + 0.3f, 6, false, true);
                post.Cylinder(ground + r * new Vector3(w * 0.42f, -0.35f, -0.03f), Quaternion.identity, 0.045f, 0.04f, height + 0.3f, 6, false, true);
            }
            Vector3 c = ground + Vector3.up * height + r * new Vector3(0f, 0f, 0.05f);
            Board(b, c, r, w, boardH, text, board, ink);
            if (cols != null) PropCol.Box(cols, c - Vector3.up * height * 0.5f, new Vector3(Mathf.Max(0.2f, w), height + boardH, 0.2f), r, "Sign");
        }

        /// A flat label board (backing + painted face) centered at c, facing r * forward.
        public static void Board(PropBatch b, Vector3 c, Quaternion r, float w, float h, string text, Color board, Color ink)
        {
            b.W(PropMats.WoodDark, c).Box(c - r * Vector3.forward * 0.025f, new Vector3(w + 0.06f, h + 0.06f, 0.04f), r);
            Vector3 f = c + r * Vector3.forward * 0.0f;
            Vector3 u = r * Vector3.right * (w * 0.5f), v = Vector3.up * (h * 0.5f);
            v = r * new Vector3(0f, h * 0.5f, 0f);
            // seen from the front (the viewer looks along -forward) local +x is on the viewer's LEFT: the text starts
            // at +x, otherwise it reads mirrored (playtest round 2: „WANDAS WIRBEL“ at the hangar was mirrored)
            b.W(PropMats.Label(text, board, ink), c).QuadAuto(f - u - v, f - u + v, f + u + v, f + u - v,
                new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(0f, 0f), r * Vector3.forward);
        }

        // ------------------------------------------------------------------ crates, barrels, drums
        public static void Crate(PropBatch b, Transform cols, Vector3 basePos, float size, float yaw, Material body = null)
        {
            Quaternion r = Yaw(yaw);
            Vector3 c = basePos + Vector3.up * (size * 0.5f);
            b.W(body ?? PropMats.Wood3, c).Box(c, Vector3.one * size * 0.94f, r);
            var d = b.W(PropMats.WoodDark, c);
            float e = size * 0.5f;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    d.Box(c + r * new Vector3(sx * (e - 0.035f), 0f, sz * (e - 0.035f)), new Vector3(0.08f, size, 0.08f), r);
            d.Box(c + r * new Vector3(0f, e - 0.03f, 0f), new Vector3(size * 0.98f, 0.06f, 0.1f), r);
            if (cols != null && size > 0.35f) PropCol.Box(cols, c, Vector3.one * size, r, "Crate");
        }

        /// A little stack of 2–4 crates (deterministic from seed).
        public static void CrateStack(PropBatch b, Transform cols, Vector3 ground, float yaw, int seed)
        {
            var rng = new System.Random(seed);
            int n = 2 + rng.Next(3);
            Quaternion r = Yaw(yaw);
            for (int i = 0; i < n; i++)
            {
                float s = PropUtil.Range(rng, 0.45f, 0.62f);
                Vector3 off = i < 2 ? r * new Vector3(i * 0.62f, 0f, PropUtil.Range(rng, -0.05f, 0.05f)) : r * new Vector3(0.3f, 0.6f, 0f);
                Vector3 p = ground + off;
                if (i < 2) p.y = G(p.x, p.z) - 0.02f;
                Crate(b, cols, p, s, yaw + PropUtil.Range(rng, -12f, 12f), i == 1 ? PropMats.Wood2 : PropMats.Wood3);
            }
        }

        public static void Barrel(PropBatch b, Transform cols, Vector3 ground, Material mat, bool lying = false, float yaw = 0f)
        {
            if (!lying)
            {
                PropStructures.Drum(b, ground - Vector3.up * 0.03f, mat);
                if (cols != null) PropCol.Capsule(cols, ground + Vector3.up * 0.44f, 0.29f, 0.88f, Quaternion.identity, "Barrel");
                return;
            }
            Quaternion q = Quaternion.Euler(0f, yaw, 90f);
            Vector3 axis = q * Vector3.up;
            Vector3 c = ground + Vector3.up * 0.27f;
            b.W(mat, c).Cylinder(c - axis * 0.44f, q, 0.29f, 0.29f, 0.88f, 8, true, true);
            b.W(PropMats.DarkMetal, c).Cylinder(c - axis * 0.2f, q, 0.302f, 0.302f, 0.05f, 8);
            b.W(PropMats.DarkMetal, c).Cylinder(c + axis * 0.15f, q, 0.302f, 0.302f, 0.05f, 8);
            if (cols != null) PropCol.Capsule(cols, c, 0.29f, 0.88f, q, "Barrel");
        }

        /// Wooden bucket / tub.
        public static void Bucket(PropBatch b, Vector3 ground, Material mat, float s = 1f)
        {
            b.W(mat, ground).Cylinder(ground, Quaternion.identity, 0.15f * s, 0.19f * s, 0.32f * s, 7, true, false);
            b.W(PropMats.DarkMetal, ground).Cylinder(ground + Vector3.up * 0.005f, Quaternion.identity, 0.145f * s, 0.185f * s, 0.31f * s, 7, false, false, 1f, 1f, 0f, true);
            b.W(PropMats.DarkMetal, ground).Torus(ground + Vector3.up * 0.22f * s, Quaternion.identity, 0.178f * s, 0.012f, 8, 3);
        }

        /// Cooler box (bait / drinks): colored body, white lid, handle.
        public static void Cooler(PropBatch b, Transform cols, Vector3 ground, float yaw, Material body)
        {
            Quaternion r = Yaw(yaw);
            b.W(body, ground).Box(ground + r * new Vector3(0f, 0.2f, 0f), new Vector3(0.7f, 0.36f, 0.42f), r);
            b.W(PropMats.White, ground).Box(ground + r * new Vector3(0f, 0.41f, 0f), new Vector3(0.74f, 0.07f, 0.46f), r);
            b.W(PropMats.DarkMetal, ground).Box(ground + r * new Vector3(0f, 0.47f, 0f), new Vector3(0.36f, 0.04f, 0.05f), r);
            if (cols != null) PropCol.Box(cols, ground + r * new Vector3(0f, 0.22f, 0f), new Vector3(0.74f, 0.45f, 0.46f), r, "Cooler");
        }

        /// Wire-and-wood crab trap (pot): a box frame with netting.
        public static void CrabTrap(PropBatch b, Vector3 ground, float yaw, float s = 1f)
        {
            Quaternion r = Yaw(yaw);
            Vector3 c = ground + Vector3.up * 0.2f * s;
            var fr = b.W(PropMats.WoodDark, c);
            Vector3 h = new Vector3(0.4f, 0.2f, 0.3f) * s;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    fr.Box(c + r * new Vector3(sx * h.x, 0f, sz * h.z), new Vector3(0.04f, h.y * 2f, 0.04f) * s, r);
            for (int sy = -1; sy <= 1; sy += 2)
            {
                fr.Box(c + r * new Vector3(0f, sy * h.y, h.z), new Vector3(h.x * 2f, 0.035f, 0.035f), r);
                fr.Box(c + r * new Vector3(0f, sy * h.y, -h.z), new Vector3(h.x * 2f, 0.035f, 0.035f), r);
            }
            var net = b.W(PropMats.Rope, c);
            for (int i = 1; i < 5; i++)
            {
                float x = -h.x + i * h.x * 0.4f;
                net.Box(c + r * new Vector3(x, 0f, h.z), new Vector3(0.008f, h.y * 2f, 0.008f), r);
                net.Box(c + r * new Vector3(x, 0f, -h.z), new Vector3(0.008f, h.y * 2f, 0.008f), r);
            }
            for (int i = 1; i < 3; i++)
            {
                float y = -h.y + i * h.y * 0.67f;
                net.Box(c + r * new Vector3(0f, y, h.z), new Vector3(h.x * 2f, 0.008f, 0.008f), r);
                net.Box(c + r * new Vector3(0f, y, -h.z), new Vector3(h.x * 2f, 0.008f, 0.008f), r);
            }
            b.W(PropMats.BuoyOrange, c).Ico(c + r * new Vector3(h.x + 0.12f, -h.y + 0.08f, 0f), Vector3.one * 0.09f * s, Quaternion.identity, 0);
        }

        public static void RopeCoil(PropBatch b, Vector3 ground, float r0 = 0.3f)
        {
            for (int k = 0; k < 3; k++)
                b.W(PropMats.Rope, ground).Torus(ground + Vector3.up * (0.045f + k * 0.075f), Yaw(k * 25f), r0 - k * 0.05f, 0.045f, 8, 3);
        }

        /// Orange / white life ring hanging on a short post.
        public static void LifebuoyPost(PropBatch b, Transform cols, Vector3 ground, float yaw)
        {
            Quaternion r = Yaw(yaw);
            b.W(PropMats.WoodDark, ground).Box(ground + Vector3.up * 0.6f, new Vector3(0.1f, 1.5f, 0.1f), r);
            Vector3 lr = ground + Vector3.up * 1.05f + r * new Vector3(0f, 0f, 0.1f);
            const int segs = 8;
            for (int i = 0; i < segs; i++)
            {
                float a0 = i * Mathf.PI * 2f / segs, a1 = (i + 1) * Mathf.PI * 2f / segs;
                Vector3 p0 = lr + r * new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f) * 0.28f;
                Vector3 p1 = lr + r * new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f) * 0.28f;
                b.W(i % 2 == 0 ? PropMats.Orange : PropMats.White, ground).Rod(p0, p1, 0.07f, 0.07f, 5, true);
            }
            if (cols != null) PropCol.Capsule(cols, ground + Vector3.up * 0.7f, 0.1f, 1.4f, Quaternion.identity, "Post");
        }

        // ------------------------------------------------------------------ seating
        /// Wooden bench (slats + backrest); facing = yaw (people sit looking along +z of yaw). Optional PropSeat.
        public static void Bench(PropBatch b, Transform cols, Transform parent, Vector3 ground, float yaw, bool seat = true, bool rest = false, bool refill = false)
        {
            Quaternion r = Yaw(yaw);
            var w = b.W(PropMats.Wood3, ground);
            for (int i = 0; i < 3; i++) w.Box(ground + r * new Vector3(0f, 0.45f, 0.12f - i * 0.13f), new Vector3(1.5f, 0.05f, 0.11f), r);
            for (int i = 0; i < 2; i++) w.Box(ground + r * new Vector3(0f, 0.7f + i * 0.17f, -0.24f), new Vector3(1.5f, 0.12f, 0.04f), r * Quaternion.Euler(-10f, 0f, 0f));
            var d = b.W(PropMats.WoodDark, ground);
            for (int s = -1; s <= 1; s += 2)
            {
                d.Box(ground + r * new Vector3(s * 0.62f, 0.22f, 0f), new Vector3(0.07f, 0.44f, 0.36f), r);
                d.Box(ground + r * new Vector3(s * 0.62f, 0.66f, -0.25f), new Vector3(0.06f, 0.5f, 0.06f), r * Quaternion.Euler(-10f, 0f, 0f));
            }
            if (cols != null) PropCol.Box(cols, ground + r * new Vector3(0f, 0.3f, -0.05f), new Vector3(1.55f, 0.6f, 0.5f), r, "Bench");
            if (seat && parent != null) Seat(parent, ground + r * new Vector3(0f, 0.47f, 0.02f), yaw, ground + r * new Vector3(0f, 0f, 0.95f), rest, refill);
        }

        /// Interactable seat point: seat transform at the hip, eye = seat + PlayerController.SeatedEyeHeight.
        public static PropSeat Seat(Transform parent, Vector3 sitTop, float yaw, Vector3 stand, bool rest = false, bool refill = false)
        {
            var go = new GameObject(rest ? "RestSeat" : "Seat");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(sitTop, Yaw(yaw));
            var bc = go.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.size = new Vector3(1.1f, 0.45f, 0.55f);
            var st = new GameObject("SeatPoint").transform;
            st.SetParent(go.transform, false);
            st.localPosition = new Vector3(0f, -0.2f, 0.05f);
            var s = go.AddComponent<PropSeat>();
            s.seat = st;
            s.standPos = stand + Vector3.up * 0.05f;
            s.restSpot = rest;
            s.refillAir = refill;
            return s;
        }

        public static void Stool(PropBatch b, Vector3 ground, Material top = null)
        {
            b.W(top ?? PropMats.Wood, ground).Cylinder(ground + Vector3.up * 0.42f, Quaternion.identity, 0.2f, 0.2f, 0.06f, 8, true, true);
            var d = b.W(PropMats.WoodDark, ground);
            for (int i = 0; i < 3; i++)
            {
                float a = i * Mathf.PI * 2f / 3f;
                d.Rod(ground + new Vector3(Mathf.Cos(a) * 0.17f, 0f, Mathf.Sin(a) * 0.17f), ground + new Vector3(Mathf.Cos(a) * 0.1f, 0.43f, Mathf.Sin(a) * 0.1f), 0.025f, 0.02f, 4);
            }
        }

        public static void Table(PropBatch b, Transform cols, Vector3 ground, float yaw, float w = 1.2f, float d = 0.7f, Material top = null)
        {
            Quaternion r = Yaw(yaw);
            var t = b.W(top ?? PropMats.Wood2, ground);
            t.Box(ground + Vector3.up * 0.74f, new Vector3(w, 0.06f, d), r);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    t.Box(ground + r * new Vector3(sx * (w * 0.5f - 0.06f), 0.37f, sz * (d * 0.5f - 0.06f)), new Vector3(0.06f, 0.74f, 0.06f), r);
            if (cols != null) PropCol.Box(cols, ground + Vector3.up * 0.38f, new Vector3(w, 0.76f, d), r, "Table");
        }

        /// Picnic table with two attached benches.
        public static void PicnicTable(PropBatch b, Transform cols, Vector3 ground, float yaw)
        {
            Quaternion r = Yaw(yaw);
            var t = b.W(PropMats.Wood3, ground);
            for (int i = -2; i <= 2; i++) t.Box(ground + r * new Vector3(0f, 0.74f, i * 0.16f), new Vector3(1.7f, 0.05f, 0.14f), r);
            for (int s = -1; s <= 1; s += 2)
            {
                for (int i = 0; i < 2; i++) t.Box(ground + r * new Vector3(0f, 0.44f, s * (0.62f + i * 0.13f)), new Vector3(1.7f, 0.05f, 0.12f), r);
                b.W(PropMats.WoodDark, ground).Beam(ground + r * new Vector3(s * 0.65f, 0f, -0.85f), ground + r * new Vector3(s * 0.65f, 0f, 0.85f), 0.07f, 0.07f);
                b.W(PropMats.WoodDark, ground).Beam(ground + r * new Vector3(s * 0.65f, 0f, -0.75f), ground + r * new Vector3(s * 0.65f, 0.72f, 0.1f), 0.06f, 0.06f);
                b.W(PropMats.WoodDark, ground).Beam(ground + r * new Vector3(s * 0.65f, 0f, 0.75f), ground + r * new Vector3(s * 0.65f, 0.72f, -0.1f), 0.06f, 0.06f);
            }
            if (cols != null) PropCol.Box(cols, ground + Vector3.up * 0.4f, new Vector3(1.75f, 0.8f, 1.7f), r, "PicnicTable");
        }

        // ------------------------------------------------------------------ lights
        /// Lantern on a wooden post (switches on at night).
        public static void LanternPost(PropBatch b, Transform cols, Transform parent, Vector3 ground, float yaw, float height = 1.9f, bool light = true)
        {
            Quaternion r = Yaw(yaw);
            var d = b.W(PropMats.WoodDark, ground);
            d.Box(ground + Vector3.up * (height * 0.5f - 0.2f), new Vector3(0.1f, height + 0.4f, 0.1f), r);
            d.Box(ground + Vector3.up * (height - 0.12f) + r * new Vector3(0f, 0f, 0.17f), new Vector3(0.06f, 0.06f, 0.4f), r);
            Vector3 lan = ground + Vector3.up * (height - 0.42f) + r * new Vector3(0f, 0f, 0.34f);
            b.W(PropMats.DarkMetal, lan).Cylinder(lan + new Vector3(0f, 0.2f, 0f), Quaternion.identity, 0.12f, 0f, 0.1f, 6, true, false);
            b.W(PropMats.DarkMetal, lan).Cylinder(lan - new Vector3(0f, 0.03f, 0f), Quaternion.identity, 0.1f, 0.1f, 0.04f, 6, true, true);
            b.W(PropMats.Glass, lan).Cylinder(lan, Quaternion.identity, 0.085f, 0.085f, 0.2f, 6);
            b.W(PropMats.DockBulb, lan).Ico(lan + new Vector3(0f, 0.1f, 0f), new Vector3(0.05f, 0.07f, 0.05f), Quaternion.identity, 0);
            b.W(PropMats.Rope, lan).Rod(lan + new Vector3(0f, 0.3f, 0f), ground + Vector3.up * (height - 0.15f) + r * new Vector3(0f, 0f, 0.34f), 0.012f, 0.012f, 3);
            if (cols != null) PropCol.Capsule(cols, ground + Vector3.up * (height * 0.5f), 0.08f, height, Quaternion.identity, "LanternPost");
            if (light && parent != null)
            {
                var l = PropUtil.AddPointLight(parent, lan + new Vector3(0f, 0.1f, 0f), new Color(1f, 0.74f, 0.42f), 8f, 1.5f, "Lantern");
                var nl = PropNightLight.Attach(l.gameObject, l, null, Color.black, 0.15f, 0.5f, true);
                nl.flickerAmount = 0.1f;
                nl.cullDistance = 90f;
            }
        }

        /// String of little bulbs / paper lanterns between two points (party lights).
        public static void LightString(PropBatch b, Vector3 a, Vector3 c, float sag, Material bulb)
        {
            float len = (c - a).magnitude;
            int n = Mathf.Max(3, Mathf.RoundToInt(len / 0.7f));
            Vector3 prev = a;
            var rope = b.W(PropMats.Ink, a);
            for (int k = 1; k <= n; k++)
            {
                float t = k / (float)n;
                Vector3 p = Vector3.Lerp(a, c, t) - Vector3.up * (Mathf.Sin(t * Mathf.PI) * sag);
                rope.Rod(prev, p, 0.008f, 0.008f, 3);
                if (k < n) b.W(bulb, p).Ico(p - Vector3.up * 0.07f, new Vector3(0.05f, 0.07f, 0.05f), Quaternion.identity, 0);
                prev = p;
            }
        }

        /// Triangle bunting between two points.
        public static void Bunting(PropBatch b, Vector3 a, Vector3 c, float sag, int seed)
        {
            Material[] flags = { PropMats.Flat(PropPal.Coral), PropMats.Flat(PropPal.Sun), PropMats.Flat(PropPal.Turquoise), PropMats.Trim, PropMats.Flat(LowPoly.Hex("#3FA9A0")) };
            float len = (c - a).magnitude;
            int n = Mathf.Max(2, Mathf.RoundToInt(len / 0.5f));
            Vector3 prev = a;
            var rope = b.W(PropMats.Rope, a);
            for (int k = 1; k <= n; k++)
            {
                float t = k / (float)n;
                Vector3 p = Vector3.Lerp(a, c, t) - Vector3.up * (Mathf.Sin(t * Mathf.PI) * sag);
                rope.Rod(prev, p, 0.01f, 0.01f, 3);
                if (k < n)
                {
                    Vector3 along = (p - prev).normalized;
                    Vector3 nrm = Vector3.Cross(along, Vector3.up);
                    if (nrm.sqrMagnitude < 1e-6f) nrm = Vector3.forward;
                    b.W(flags[(k + seed) % flags.Length], p).Tri2(p - along * 0.12f, p + along * 0.12f, p - Vector3.up * 0.24f, nrm.normalized);
                }
                prev = p;
            }
        }

        // ------------------------------------------------------------------ fishing / village craft
        /// A drying rack with a fishing net hanging over it (two A-frames, a pole, the net as a sagging grid).
        public static void NetRack(PropBatch b, Transform cols, Vector3 ground, float yaw, float len = 2.6f)
        {
            Quaternion r = Yaw(yaw);
            var w = b.W(PropMats.WoodDark, ground);
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 e = ground + r * new Vector3(s * len * 0.5f, 0f, 0f);
                e.y = G(e.x, e.z);
                w.Beam(e + r * new Vector3(0f, -0.2f, -0.45f), e + r * new Vector3(0f, 1.7f, 0f), 0.07f, 0.07f);
                w.Beam(e + r * new Vector3(0f, -0.2f, 0.45f), e + r * new Vector3(0f, 1.7f, 0f), 0.07f, 0.07f);
            }
            Vector3 p0 = ground + r * new Vector3(-len * 0.5f, 1.68f, 0f), p1 = ground + r * new Vector3(len * 0.5f, 1.68f, 0f);
            w.Beam(p0, p1, 0.06f, 0.06f);
            var net = b.W(PropMats.Flat(LowPoly.Hex("#5F7F86")), ground);
            const int nx = 8, ny = 5;
            for (int s = -1; s <= 1; s += 2)
            {
                for (int i = 0; i <= nx; i++)
                {
                    float t = i / (float)nx;
                    Vector3 top = Vector3.Lerp(p0, p1, t);
                    Vector3 bot = top + r * new Vector3(0f, -1.25f + 0.12f * Mathf.Sin(t * 7f), s * 0.42f);
                    net.Rod(top, bot, 0.008f, 0.008f, 3);
                }
                for (int j = 1; j <= ny; j++)
                {
                    float v = j / (float)ny;
                    Vector3 a = p0 + r * new Vector3(0f, -1.25f * v, s * 0.42f * v), c = p1 + r * new Vector3(0f, -1.25f * v, s * 0.42f * v);
                    net.Rod(a, c, 0.008f, 0.008f, 3);
                }
            }
            for (int i = 0; i < 4; i++)
                b.W(i % 2 == 0 ? PropMats.BuoyOrange : PropMats.White, ground).Ico(Vector3.Lerp(p0, p1, 0.15f + i * 0.23f) + r * new Vector3(0f, -1.0f, 0.45f), Vector3.one * 0.08f, Quaternion.identity, 0);
            if (cols != null) PropCol.Box(cols, ground + Vector3.up * 0.9f, new Vector3(len, 1.8f, 0.9f), r, "NetRack");
        }

        /// Outrigger canoe (village / lagoon): slim dugout hull, outrigger float on two booms, a paddle.
        public static void Canoe(PropBatch b, Matrix4x4 trs, Material hull, Material trim)
        {
            var hb = new PropMeshBuilder();
            const int secs = 9;
            const float L = 5.2f;
            var prev = new Vector3[5];
            for (int k = 0; k <= secs; k++)
            {
                float t = k / (float)secs;
                float z = (t - 0.5f) * L;
                float st = Mathf.Max(0f, Mathf.Sin(Mathf.PI * t));
                float hw = 0.34f * Mathf.Pow(st, 0.55f) + 0.02f;
                float depth = 0.36f * Mathf.Pow(st, 0.35f) + 0.04f;
                float sheer = 0.12f * (1f - st);
                var ring = new[]
                {
                    new Vector3(-hw, depth + sheer, z), new Vector3(-hw * 0.9f, depth * 0.35f, z), new Vector3(0f, 0f, z),
                    new Vector3(hw * 0.9f, depth * 0.35f, z), new Vector3(hw, depth + sheer, z),
                };
                if (k > 0)
                    for (int j = 0; j < 4; j++)
                        hb.QuadAuto(prev[j], prev[j + 1], ring[j + 1], ring[j], Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero,
                            (prev[j] + ring[j + 1]) * 0.5f - new Vector3(0f, depth, z));
                for (int j = 0; j < 5; j++) prev[j] = ring[j];
            }

            hb.DoubleSide(0, 0);
            Vector3 cell = trs.GetColumn(3);
            b.B(hull, cell).Append(hb, trs);
            var tb = new PropMeshBuilder();
            tb.Box(new Vector3(0f, 0.36f, 0f), new Vector3(0.62f, 0.05f, L * 0.82f));
            // outrigger: two booms + float
            for (int s = -1; s <= 1; s += 2) tb.Beam(new Vector3(0f, 0.42f, s * 0.9f), new Vector3(1.5f, 0.3f, s * 0.9f), 0.06f, 0.06f);
            tb.Cylinder(new Vector3(1.55f, 0.18f, -1.3f), Quaternion.Euler(90f, 0f, 0f), 0.11f, 0.11f, 2.6f, 6, true, true);
            b.B(trim, cell).Append(tb, trs);
            var pb = new PropMeshBuilder();
            pb.Rod(new Vector3(-0.15f, 0.45f, -0.6f), new Vector3(0.2f, 0.5f, 0.9f), 0.025f, 0.025f, 4);
            pb.Box(new Vector3(0.24f, 0.5f, 1.1f), new Vector3(0.16f, 0.03f, 0.4f), Quaternion.Euler(0f, 8f, 0f));
            b.B(PropMats.WoodLight, cell).Append(pb, trs);
        }

        /// A rack holding canoes / surfboards (village beach, boat shed).
        public static void CanoeRack(PropBatch b, Transform cols, Vector3 ground, float yaw, int canoes, int seed)
        {
            Quaternion r = Yaw(yaw);
            var w = b.W(PropMats.WoodDark, ground);
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 e = ground + r * new Vector3(0f, 0f, s * 1.6f);
                w.Box(e + Vector3.up * 0.6f, new Vector3(0.1f, 1.6f, 0.1f), r);
                w.Box(e + Vector3.up * 0.6f + r * new Vector3(0.9f, 0f, 0f), new Vector3(0.1f, 1.6f, 0.1f), r);
                w.Box(e + Vector3.up * 0.55f + r * new Vector3(0.45f, 0f, 0f), new Vector3(1.0f, 0.06f, 0.08f), r);
                w.Box(e + Vector3.up * 1.25f + r * new Vector3(0.45f, 0f, 0f), new Vector3(1.0f, 0.06f, 0.08f), r);
            }
            Material[] hulls = { PropMats.Flat(LowPoly.Hex("#3FA9A0")), PropMats.Flat(LowPoly.Hex("#EE7F2D")), PropMats.Wood3 };
            for (int i = 0; i < canoes; i++)
            {
                Vector3 c = ground + r * new Vector3(0.45f, 0.62f + i * 0.7f, 0f);
                Canoe(b, Matrix4x4.TRS(c, r * Quaternion.Euler(0f, 0f, 180f), new Vector3(0.75f, 0.75f, 0.68f)), hulls[(i + seed) % hulls.Length], PropMats.WoodLight);
            }
            if (cols != null) PropCol.Box(cols, ground + Vector3.up * 0.8f + r * new Vector3(0.45f, 0f, 0f), new Vector3(1.1f, 1.6f, 3.4f), r, "CanoeRack");
        }

        // ------------------------------------------------------------------ camp
        /// A-frame tent (canvas in a color) with an open flap; opening faces yaw.
        public static void Tent(PropBatch b, Transform cols, Vector3 ground, float yaw, Color cloth, float len = 2.2f, float w = 1.8f, float h = 1.4f)
        {
            Quaternion r = Yaw(yaw);
            var c = b.W(PropMats.Flat(cloth), ground);
            var dk = b.W(PropMats.Flat(PropMats.Value(cloth, 0.78f)), ground);
            Vector3 P(float x, float y, float z) => ground + r * new Vector3(x, y, z);
            Vector3 fl = P(-w * 0.5f, 0.02f, len * 0.5f), fr = P(w * 0.5f, 0.02f, len * 0.5f), ft = P(0f, h, len * 0.5f);
            Vector3 bl = P(-w * 0.5f, 0.02f, -len * 0.5f), br = P(w * 0.5f, 0.02f, -len * 0.5f), bt = P(0f, h, -len * 0.5f);
            c.QuadAuto(fl, ft, bt, bl, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, r * new Vector3(-1f, 0.7f, 0f));
            c.QuadAuto(fr, ft, bt, br, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, r * new Vector3(1f, 0.7f, 0f));
            dk.QuadAuto(fl, ft, bt, bl, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, r * new Vector3(1f, -0.7f, 0f));
            dk.QuadAuto(fr, ft, bt, br, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, r * new Vector3(-1f, -0.7f, 0f));
            c.Face3(bl, br, bt, r * Vector3.back);
            dk.Face3(bl, br, bt, r * Vector3.forward);
            // half-open front flap
            c.Tri2(fl, ft, P(-w * 0.55f, 0.02f, len * 0.5f + 0.5f), r * Vector3.forward);
            b.W(PropMats.WoodDark, ground).Rod(P(0f, 0f, len * 0.5f + 0.1f), P(0f, h + 0.1f, len * 0.5f + 0.05f), 0.025f, 0.025f, 4);
            b.W(PropMats.Rope, ground).Rod(P(0f, h + 0.08f, len * 0.5f + 0.05f), P(0f, 0f, len * 0.5f + 1.1f), 0.01f, 0.01f, 3);
            b.W(PropMats.Blanket, ground).Box(P(0f, 0.06f, -0.2f), new Vector3(w * 0.7f, 0.08f, len * 0.7f), r);
            if (cols != null) PropCol.Box(cols, P(0f, h * 0.45f, 0f), new Vector3(w * 0.85f, h * 0.9f, len), r, "Tent");
        }

        /// Ring of stones with ash and charred logs (cold or with a FX fire).
        public static void FirePit(PropBatch b, Transform parent, Vector3 ground, int seed, bool lit)
        {
            var rng = new System.Random(seed);
            b.W(PropMats.Ash, ground).Disk(ground + Vector3.up * 0.03f, 0.5f, 8, Quaternion.identity, true);
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI * 2f / 8f + PropUtil.Range(rng, -0.1f, 0.1f);
                Vector3 sp = ground + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.68f;
                sp.y = G(sp.x, sp.z) + 0.05f;
                float s = PropUtil.Range(rng, 0.14f, 0.22f);
                PropRocks.Build(b.W(i % 3 == 0 ? PropMats.Rock : PropMats.RockDark, sp), sp, new Vector3(s, s * 0.8f, s), Yaw(PropUtil.Range(rng, 0f, 360f)), rng.Next(), 0, 0.2f);
            }
            for (int i = 0; i < 3; i++)
            {
                float a = i * 2.1f + 0.3f;
                Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                b.W(PropMats.WoodDark, ground).Rod(ground + d * 0.38f + Vector3.up * 0.04f, ground + Vector3.up * 0.3f - d * 0.05f, 0.055f, 0.045f, 5, true);
            }
            if (!lit || parent == null) return;
            var fire = PropFX.Fire(parent, ground + Vector3.up * 0.1f);
            fire.transform.localScale = Vector3.one * 0.7f;
            var go = new GameObject("FirePit");
            go.transform.SetParent(parent, false);
            go.transform.position = ground + Vector3.up * 0.6f;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.55f, 0.22f);
            l.range = 7f;
            l.intensity = 2f;
            l.shadows = LightShadows.None;
            var nl = PropNightLight.Attach(go, l, null, Color.black, 0.1f, 0.35f, true);
            nl.flickerAmount = 0.35f;
            nl.particles = fire;
            nl.cullDistance = 90f;
        }

        /// Hammock between two points (striped cloth, ropes), optionally registered as a sleep spot.
        public static void Hammock(PropBatch b, Transform cols, Vector3 a, Vector3 c, int stripes, bool solid = true)
        {
            Vector3 ab = c - a;
            float len = ab.magnitude;
            Vector3 dir = ab / len;
            Vector3 lat = Vector3.Cross(Vector3.up, dir).normalized;
            const int n = 8;
            float sag = Mathf.Min(0.7f, len * 0.22f);
            Vector3 P(float t, float w) => Vector3.Lerp(a, c, t) - Vector3.up * (sag * Mathf.Sin(t * Mathf.PI)) + lat * (w * Mathf.Sin(Mathf.Clamp01(t * 1.15f - 0.075f) * Mathf.PI) * 0.5f) - Vector3.up * (Mathf.Abs(w) * 0.12f * Mathf.Sin(t * Mathf.PI));
            Material[][] sets =
            {
                new[] { PropMats.Flat(PropPal.Coral), PropMats.Trim, PropMats.Flat(PropPal.Turquoise), PropMats.Trim },
                new[] { PropMats.Flat(LowPoly.Hex("#3FA9A0")), PropMats.Flat(LowPoly.Hex("#F2C94C")), PropMats.Flat(LowPoly.Hex("#EE7F2D")), PropMats.Trim },
                new[] { PropMats.Flat(LowPoly.Hex("#8E6CD9")), PropMats.Trim, PropMats.Flat(LowPoly.Hex("#F29BBE")), PropMats.Trim },
            };
            var set = sets[Mathf.Abs(stripes) % sets.Length];
            for (int s = 0; s < 4; s++)
            {
                float w0 = -1f + s * 0.5f, w1 = w0 + 0.5f;
                var m = b.W(set[s], a);
                for (int i = 0; i < n; i++)
                {
                    float t0 = i / (float)n, t1 = (i + 1) / (float)n;
                    m.Quad2(P(t0, w0), P(t1, w0), P(t1, w1), P(t0, w1), Vector3.up);
                }
            }
            var rope = b.W(PropMats.Rope, a);
            rope.Rod(a - dir * 0.3f, P(0.05f, 0f), 0.02f, 0.02f, 3);
            rope.Rod(c + dir * 0.3f, P(0.95f, 0f), 0.02f, 0.02f, 3);
            if (solid && cols != null) PropCol.Box(cols, P(0.5f, 0f) + Vector3.up * 0.05f, new Vector3(1.0f, 0.25f, len * 0.6f), Quaternion.LookRotation(dir), "Hammock");
        }

        /// Registers a hammock (built with Hammock) as a sleep spot / bed interactable.
        public static void HammockBed(Transform parent, string id, Vector3 a, Vector3 c)
        {
            Vector3 mid = (a + c) * 0.5f - Vector3.up * Mathf.Min(0.7f, (c - a).magnitude * 0.22f);
            Vector3 dir = (c - a); dir.y = 0f; dir.Normalize();
            Vector3 lat = Vector3.Cross(Vector3.up, dir);
            Vector3 stand = mid + lat * 1.0f;
            stand.y = G(stand.x, stand.z);
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            int idx = PropBed.Register(id, mid + Vector3.up * 0.3f - dir * 0.8f, yaw, stand, yaw + 90f, SleepSpotInfo.Hammock);
            var go = new GameObject("HammockBed_" + id);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(mid, Quaternion.LookRotation(dir));
            var bc = go.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.size = new Vector3(0.9f, 0.4f, 2f);
            var bed = go.AddComponent<PropBed>();
            bed.spotIndex = idx;
            bed.kind = SleepSpotInfo.Hammock;
        }

        // ------------------------------------------------------------------ nature bits
        /// A fallen log lying along yaw (driftwood or jungle), optionally walkable (collider).
        public static void Log(PropBatch b, Transform cols, Vector3 center, float yaw, float len, float r, Material mat, bool collider = true)
        {
            Quaternion q = Quaternion.Euler(0f, yaw, 90f);
            Vector3 axis = q * Vector3.up;
            b.W(mat, center).Cylinder(center - axis * len * 0.5f, q, r, r * 0.8f, len, 7, true, true);
            // a stub branch
            b.W(mat, center).Rod(center + axis * len * 0.18f, center + axis * len * 0.25f + Vector3.up * r * 2.2f, r * 0.35f, r * 0.2f, 5, true);
            if (collider && cols != null) PropCol.Capsule(cols, center, r, len, q, "Log");
        }

        /// A small cluster of rocks with moss (detail filler).
        public static void RockCluster(PropBatch b, Transform cols, Vector3 ground, int seed, float size = 1f, Material main = null)
        {
            var rng = new System.Random(seed);
            int n = 2 + rng.Next(3);
            for (int i = 0; i < n; i++)
            {
                Vector2 o = i == 0 ? Vector2.zero : PropUtil.InDisc(rng, 1.1f * size);
                Vector3 p = new Vector3(ground.x + o.x, 0f, ground.z + o.y);
                float s = (i == 0 ? PropUtil.Range(rng, 0.5f, 0.85f) : PropUtil.Range(rng, 0.2f, 0.45f)) * size;
                Vector3 sc = new Vector3(s * PropUtil.Range(rng, 1.0f, 1.4f), s * PropUtil.Range(rng, 0.55f, 0.9f), s);
                p.y = G(p.x, p.z) - sc.y * 0.3f;
                Quaternion q = Quaternion.Euler(PropUtil.Range(rng, -8f, 8f), PropUtil.Range(rng, 0f, 360f), 0f);
                int sd = rng.Next();
                PropRocks.BuildSplit(b, p, p, sc, q, sd, s > 0.4f ? 1 : 0, 0.15f, main ?? (i % 2 == 0 ? PropMats.Rock : PropMats.RockMid), PropMats.Moss, PropMats.Crevice);
                if (cols != null && s > 0.6f) PropCol.Mesh(cols, PropRocks.ColliderMesh(sc, sd, 1, 0.15f), p, q, Vector3.one, "Rock");
            }
        }

        /// A mushroom ring (jungle detail): 9–14 little red / white mushrooms in a circle.
        public static void MushroomRing(PropBatch b, Vector3 center, float radius, int seed)
        {
            var rng = new System.Random(seed);
            int n = 9 + rng.Next(6);
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n + PropUtil.Range(rng, -0.15f, 0.15f);
                Vector3 p = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius * PropUtil.Range(rng, 0.9f, 1.1f);
                p.y = G(p.x, p.z);
                float h = PropUtil.Range(rng, 0.08f, 0.2f);
                b.W(PropMats.Paper, p).Cylinder(p, Quaternion.identity, 0.025f, 0.02f, h, 5);
                float cr = PropUtil.Range(rng, 0.06f, 0.11f);
                b.W(i % 4 == 0 ? PropMats.FlowerYellow : PropMats.LighthouseRed, p).Cylinder(p + Vector3.up * h, Quaternion.identity, cr, 0.012f, cr * 0.75f, 6, true, false);
                if (i % 4 != 0) b.W(PropMats.White, p).Octa(p + Vector3.up * (h + cr * 0.45f) + new Vector3(cr * 0.3f, 0f, 0f), 0.015f);
            }
        }

        /// A sandbar mound in the lagoon (just breaking the surface) – birds rest here.
        public static void Sandbar(PropBatch b, Vector3 center, float len, float wid, float yaw, int seed)
        {
            var mb = new PropMeshBuilder();
            var rng = new System.Random(seed);
            const int A = 12;
            Quaternion r = Yaw(yaw);
            Vector3 top = center + Vector3.up * 0.18f;
            var ring = new Vector3[A];
            var ring2 = new Vector3[A];
            for (int i = 0; i < A; i++)
            {
                float a = i * Mathf.PI * 2f / A;
                float j = PropUtil.Range(rng, 0.85f, 1.1f);
                ring[i] = center + r * new Vector3(Mathf.Cos(a) * len * 0.5f * j, 0.08f, Mathf.Sin(a) * wid * 0.5f * j);
                ring2[i] = center + r * new Vector3(Mathf.Cos(a) * len * 0.75f * j, -1.2f, Mathf.Sin(a) * wid * 0.85f * j);
            }
            for (int i = 0; i < A; i++)
            {
                int k = (i + 1) % A;
                mb.Face3(top, ring[i], ring[k], Vector3.up);
                mb.QuadAuto(ring[i], ring[k], ring2[k], ring2[i], Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, ((ring[i] + ring[k]) * 0.5f - center));
            }
            b.B(PropMats.Flat(PropPal.Sand), center).Append(mb, Matrix4x4.identity);
        }

        /// Tide-line debris: a strip of dark seaweed, small shells and a bit of rope along the shore.
        public static void TideLine(PropBatch b, Vector3 a, Vector3 c, int seed)
        {
            var rng = new System.Random(seed);
            Vector3 d = c - a;
            float len = d.magnitude;
            if (len < 0.5f) return;
            Vector3 dir = d / len;
            Vector3 lat = Vector3.Cross(Vector3.up, dir);
            int n = Mathf.Max(3, Mathf.RoundToInt(len / 0.6f));
            var weed = b.W(PropMats.Flat(LowPoly.Hex("#5B5A3A")), a);
            for (int i = 0; i < n; i++)
            {
                Vector3 p = a + dir * (len * i / n) + lat * PropUtil.Range(rng, -0.35f, 0.35f);
                p.y = G(p.x, p.z) + 0.02f;
                float s = PropUtil.Range(rng, 0.12f, 0.3f);
                weed.Box(p, new Vector3(s * 2f, 0.025f, s), Yaw(PropUtil.Range(rng, 0f, 360f)));
                if (rng.NextDouble() < 0.35)
                    b.W(rng.NextDouble() < 0.5 ? PropMats.FlowerWhite : PropMats.FlowerPink, p).Ico(p + lat * 0.25f + Vector3.up * 0.02f, new Vector3(0.04f, 0.025f, 0.035f), Yaw(PropUtil.Range(rng, 0f, 360f)), 0);
            }
            if (rng.NextDouble() < 0.6)
            {
                Vector3 p = a + dir * len * 0.5f;
                p.y = G(p.x, p.z) + 0.03f;
                b.W(PropMats.Rope, p).Rod(p - dir * 0.5f, p + dir * 0.4f + lat * 0.2f, 0.02f, 0.02f, 3);
            }
        }

        /// A crab burrow: a small dark hole with a sand rim.
        public static void Burrow(PropBatch b, Vector3 ground)
        {
            b.W(PropMats.Flat(PropMats.Value(PropPal.Sand, 0.86f)), ground).Cylinder(ground - Vector3.up * 0.02f, Quaternion.identity, 0.16f, 0.1f, 0.06f, 7, false, true);
            b.W(PropMats.Ink, ground).Disk(ground + Vector3.up * 0.045f, 0.07f, 7, Quaternion.identity, true);
        }

        // ------------------------------------------------------------------ water
        /// A small floating mooring buoy (animated on the waves by PropBuoy).
        public static void MooringBuoy(Transform parent, Vector3 pos, Material body, float scale = 0.7f)
        {
            var go = new GameObject("MooringBuoy");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(pos.x, 0f, pos.z);
            var m = new PropMeshBuilder();
            m.Ico(Vector3.zero, Vector3.one * 0.42f * scale, Quaternion.identity, 1);
            m.Rod(new Vector3(0f, 0.3f * scale, 0f), new Vector3(0f, 0.75f * scale, 0f), 0.04f, 0.035f, 5, true);
            PropUtil.MeshObject(go.transform, "Body", m.ToMesh("MooringBuoy"), body, Vector3.zero, Quaternion.identity);
            var band = new PropMeshBuilder();
            band.Cylinder(new Vector3(0f, -0.05f * scale, 0f), Quaternion.identity, 0.43f * scale, 0.43f * scale, 0.1f * scale, 8);
            PropUtil.MeshObject(go.transform, "Band", band.ToMesh("MooringBand"), PropMats.White, Vector3.zero, Quaternion.identity);
            var bu = go.AddComponent<PropBuoy>();
            bu.x = pos.x;
            bu.z = pos.z;
            bu.floatOffset = -0.1f;
            bu.yaw = (pos.x * 13.7f + pos.z * 7.1f) % 360f;
        }

        /// The swim raft with a diving board (L.5 spot 3): floats on 4 drums in the lagoon; the board is 2 m above the
        /// water. Builds geometry + colliders around the anchor (world, y ignored) and returns the deck height.
        public static float SwimRaft(PropBatch b, Transform cols, Transform parent, Vector3 anchor, float yaw)
        {
            Quaternion r = Yaw(yaw);
            float deck = Waves.SeaLevel + 0.45f;
            Vector3 c = new Vector3(anchor.x, deck, anchor.z);
            Material[] planks = { PropMats.Wood, PropMats.Wood2, PropMats.Wood3 };
            for (int i = -6; i <= 6; i++)
                b.W(planks[(i + 9) % 3], c).Box(c + r * new Vector3(i * 0.26f, -0.03f, 0f), new Vector3(0.24f, 0.06f, 3.3f), r);
            for (int s = -1; s <= 1; s += 2)
            {
                b.W(PropMats.WoodDark, c).Box(c + r * new Vector3(0f, -0.12f, s * 1.3f), new Vector3(3.4f, 0.14f, 0.16f), r);
                for (int t = -1; t <= 1; t += 2)
                    b.W(t * s > 0 ? PropMats.DrumBlue : PropMats.DrumYellow, c).Cylinder(c + r * new Vector3(t * 1.1f, -0.85f, s * 1.0f), r * Quaternion.Euler(90f, 0f, 0f), 0.29f, 0.29f, 0.88f, 8, true, true);
            }
            PropCol.Box(cols, c + Vector3.up * -0.1f, new Vector3(3.4f, 0.24f, 3.4f), r, "SwimRaft");
            // ladder on the shore side and the diving board tower on the sea side
            Vector3 lad = c + r * new Vector3(-1.75f, 0f, 0f);
            var lw = b.W(PropMats.Metal, c);
            for (int s = -1; s <= 1; s += 2)
            {
                lw.Rod(lad + r * new Vector3(0f, -1.4f, s * 0.25f), lad + r * new Vector3(0f, 0.7f, s * 0.25f), 0.03f, 0.03f, 6);
                lw.Rod(lad + r * new Vector3(0f, 0.7f, s * 0.25f), lad + r * new Vector3(0.3f, 0.7f, s * 0.25f), 0.03f, 0.03f, 6);
            }
            for (float y = -1.2f; y < 0.0f; y += 0.3f) lw.Rod(lad + r * new Vector3(0f, y, -0.25f), lad + r * new Vector3(0f, y, 0.25f), 0.022f, 0.022f, 5);
            PropLadder.Create(parent, "SwimRaftLadder", lad + Vector3.up * -1.4f, deck + 0.7f, c + r * new Vector3(-1.2f, 0.02f, 0f), r * Vector3.left, LadderInfo.Metal, true);
            // diving board: two posts, a springy plank 2 m above the water, steps up
            Vector3 tw = c + r * new Vector3(1.0f, 0f, 0f);
            var tb = b.W(PropMats.WoodDark, c);
            for (int s = -1; s <= 1; s += 2) tb.Box(tw + r * new Vector3(0f, 0.8f, s * 0.35f), new Vector3(0.12f, 1.6f, 0.12f), r);
            b.W(PropMats.White, c).Box(tw + r * new Vector3(0.9f, 1.55f, 0f), new Vector3(2.4f, 0.08f, 0.55f), r * Quaternion.Euler(0f, 0f, 3f));
            b.W(PropMats.Flat(PropPal.Turquoise), c).Box(tw + r * new Vector3(1.9f, 1.6f, 0f), new Vector3(0.4f, 0.09f, 0.56f), r * Quaternion.Euler(0f, 0f, 3f));
            for (int k = 0; k < 4; k++)
                b.W(PropMats.Wood3, c).Box(tw + r * new Vector3(-0.55f + k * 0.18f, 0.2f + k * 0.36f, 0f), new Vector3(0.18f, 0.06f, 0.5f), r);
            PropCol.Box(cols, tw + r * new Vector3(0.9f, 1.55f, 0f), new Vector3(2.4f, 0.12f, 0.55f), r * Quaternion.Euler(0f, 0f, 3f), "DivingBoard");
            for (int k = 0; k < 4; k++) PropCol.Box(cols, tw + r * new Vector3(-0.55f + k * 0.18f, 0.2f + k * 0.36f, 0f), new Vector3(0.2f, 0.08f, 0.5f), r, "BoardStep");
            for (int s = -1; s <= 1; s += 2) PropCol.Box(cols, tw + r * new Vector3(0f, 0.8f, s * 0.35f), new Vector3(0.14f, 1.6f, 0.14f), r, "BoardPost");
            // a striped flag and a towel left on the deck
            b.W(PropMats.WoodDark, c).Rod(c + r * new Vector3(-1.5f, 0f, -1.5f), c + r * new Vector3(-1.5f, 2.2f, -1.5f), 0.03f, 0.025f, 5);
            b.W(PropMats.Flat(PropPal.Coral), c).Tri2(c + r * new Vector3(-1.5f, 2.2f, -1.5f), c + r * new Vector3(-1.5f, 1.8f, -1.5f), c + r * new Vector3(-0.9f, 2.0f, -1.5f), r * Vector3.forward);
            PropBeach.Towel(b, c + r * new Vector3(-0.3f, 0.01f, -0.6f), yaw + 20f);
            return deck;
        }
    }
}
