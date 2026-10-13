using UnityEngine;

namespace Sealebrities
{
    /// Free-standing rock grottos built from props (the terrain is a height field and cannot have overhangs): a tube
    /// of rings (local x offset, z, half width, height) with a dark inner shell, a floor, a chunky outer rock shell
    /// with moss / crevice faces and colliders (inner both-sided + outer). Used for the hidden-bay cave, the wreck
    /// island's sea cave, the sea-lion grotto, the Nebelgrotte, the Blue Chamber and the trench lava tube.
    /// Local frame: origin = mouth floor center, +z into the cave, +y up.
    public static class PropGrotto
    {
        public struct Result
        {
            public Matrix4x4 M;
            /// Local ring centres (floor) – handy for placing things inside.
            public Vector3[] centers;
            public Vector4[] rings;
            public Vector3 W(Vector3 local) => M.MultiplyPoint3x4(local);
        }

        /// A short grotto: entrance 2.2 m wide, a bend, a round chamber.
        public static readonly Vector4[] Small =
        {
            new Vector4(0f, -0.4f, 1.3f, 2.4f), new Vector4(0f, 1.2f, 1.6f, 2.7f), new Vector4(0.4f, 3.2f, 1.7f, 2.8f),
            new Vector4(1.0f, 5.0f, 2.2f, 3.0f), new Vector4(1.3f, 6.8f, 2.4f, 3.1f), new Vector4(1.1f, 8.3f, 1.6f, 2.6f),
        };

        /// A longer tube (lava tube / swim-through) with a chamber at the end.
        public static readonly Vector4[] Tube =
        {
            new Vector4(0f, -0.4f, 1.6f, 2.8f), new Vector4(0f, 2f, 1.8f, 3.0f), new Vector4(0.6f, 5f, 1.7f, 2.9f),
            new Vector4(1.2f, 8f, 1.9f, 3.1f), new Vector4(0.8f, 11f, 2.6f, 3.6f), new Vector4(0.2f, 13.5f, 3.0f, 3.9f),
            new Vector4(0f, 15.5f, 2.2f, 3.0f),
        };

        /// Floor height that keeps the whole inner tube above the terrain (max ground under every ring + 0.08 m).
        public static float FitFloor(Vector3 mouth, float yaw, Vector4[] rings)
        {
            var R = Quaternion.Euler(0f, yaw, 0f);
            float mx = float.MinValue;
            for (int k = 0; k < rings.Length; k++)
            {
                var r = rings[k];
                for (int s = -1; s <= 1; s++)
                {
                    Vector3 p = mouth + R * new Vector3(r.x + s * r.z * 0.9f, 0f, r.y);
                    mx = Mathf.Max(mx, PropGround.H(p.x, p.z));
                }
            }
            return mx + 0.08f;
        }

        public static Result Build(PropBatch batch, Transform cols, Vector3 mouthFloor, float yaw, Vector4[] rings, int seed,
            Material outer, Material moss, float skirt = 2.5f, float thick = 1.6f, bool floor = true)
        {
            var R = Quaternion.Euler(0f, yaw, 0f);
            var M = Matrix4x4.TRS(mouthFloor, R, Vector3.one);
            Vector3 W(Vector3 local) => M.MultiplyPoint3x4(local);
            int nr = rings.Length;
            const int A = 8;
            var inner = new Vector3[nr, A + 1];
            var outerV = new Vector3[nr, A + 3];
            var centers = new Vector3[nr];
            for (int k = 0; k < nr; k++)
            {
                var r = rings[k];
                Vector3 p = new Vector3(r.x, 0f, r.y);
                Vector3 prev = k > 0 ? new Vector3(rings[k - 1].x, 0f, rings[k - 1].y) : p - Vector3.forward;
                Vector3 next = k < nr - 1 ? new Vector3(rings[k + 1].x, 0f, rings[k + 1].y) : p + (p - prev);
                Vector3 t = (next - prev).normalized;
                Vector3 lat = Vector3.Cross(Vector3.up, t).normalized;
                centers[k] = p;
                float th = k == 0 ? thick * 0.8f : thick;
                for (int j = 0; j <= A; j++)
                {
                    float a = Mathf.PI * j / A;
                    Vector3 v = p + lat * (Mathf.Cos(a) * r.z) + Vector3.up * (Mathf.Sin(a) * r.w);
                    if (j > 0 && j < A && k > 0)
                    {
                        float nz = PropNoise.Fbm3(W(v) * 0.45f, 2, seed & 0xFFFF) - 0.5f;
                        Vector3 inward = (p + Vector3.up * (r.w * 0.4f) - v).normalized;
                        v += inward * (nz * 0.45f * Mathf.Min(1f, r.z / 2f));
                    }
                    inner[k, j] = v;
                    float tt = th + 0.8f * PropNoise.Value(k * 1.7f, j * 1.3f, seed);
                    outerV[k, j + 1] = p + lat * (Mathf.Cos(a) * (r.z + tt)) + Vector3.up * (Mathf.Sin(a) * (r.w + tt) + Mathf.Sin(a) * 0.7f);
                }
                outerV[k, 0] = p + lat * (r.z + th + 0.6f) - Vector3.up * skirt;
                outerV[k, A + 2] = p - lat * (r.z + th + 0.6f) - Vector3.up * skirt;
            }
            var innerB = new PropMeshBuilder { M = M };
            var floorB = new PropMeshBuilder { M = M };
            var outerB = new PropMeshBuilder { M = M };
            for (int k = 0; k < nr - 1; k++)
            {
                Vector3 ax = (centers[k] + centers[k + 1]) * 0.5f + Vector3.up * ((rings[k].w + rings[k + 1].w) * 0.2f);
                for (int j = 0; j < A; j++)
                {
                    Vector3 a = inner[k, j], b = inner[k, j + 1], c = inner[k + 1, j + 1], d = inner[k + 1, j];
                    innerB.QuadAuto(a, b, c, d, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, ax - (a + b + c + d) * 0.25f);
                }
                for (int j = 0; j < A + 2; j++)
                {
                    Vector3 a = outerV[k, j], b = outerV[k, j + 1], c = outerV[k + 1, j + 1], d = outerV[k + 1, j];
                    outerB.QuadAuto(a, b, c, d, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, (a + b + c + d) * 0.25f - (centers[k] + centers[k + 1]) * 0.5f);
                }
                if (floor) floorB.QuadAuto(inner[k, 0], inner[k, A], inner[k + 1, A], inner[k + 1, 0], Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector3.up);
            }
            // mouth rim and the closed back
            for (int j = 0; j < A; j++)
                outerB.QuadAuto(inner[0, j], inner[0, j + 1], outerV[0, j + 2], outerV[0, j + 1], Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector3.back);
            outerB.QuadAuto(inner[0, 0], outerV[0, 1], outerV[0, 0], inner[0, 0] - Vector3.up * skirt, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector3.back);
            outerB.QuadAuto(inner[0, A], outerV[0, A + 1], outerV[0, A + 2], inner[0, A] - Vector3.up * skirt, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector3.back);
            {
                int k = nr - 1;
                Vector3 t = (centers[k] - centers[k - 1]).normalized;
                Vector3 ci = centers[k] + Vector3.up * (rings[k].w * 0.45f) + t * 0.4f;
                for (int j = 0; j < A; j++) innerB.Face3(inner[k, j], inner[k, j + 1], ci, -t);
                innerB.Face3(inner[k, 0], inner[k, A], ci, -t);
                Vector3 co = centers[k] + Vector3.up * (rings[k].w * 0.6f) + t * 1.6f;
                for (int j = 0; j < A + 2; j++) outerB.Face3(outerV[k, j], outerV[k, j + 1], co, t);
                outerB.Face3(outerV[k, 0], outerV[k, A + 2], co, t);
            }
            if (floor)
            {
                // apron ramp from the mouth floor down to the outside ground (never steeper than ~25°)
                Vector3 mL = inner[0, A], mR = inner[0, 0];
                float outside = Mathf.Min(0f, PropGround.H(W(new Vector3(0f, 0f, -2.4f)).x, W(new Vector3(0f, 0f, -2.4f)).z) - mouthFloor.y);
                float apron = Mathf.Max(1.6f, -outside * 2.2f);
                float outY = Mathf.Min(0f, PropGround.H(W(new Vector3(0f, 0f, -0.6f - apron)).x, W(new Vector3(0f, 0f, -0.6f - apron)).z) - mouthFloor.y) - 0.05f;
                Vector3 aL = mL + new Vector3(-0.3f, outY, -apron), aR = mR + new Vector3(0.3f, outY, -apron);
                floorB.QuadAuto(aL, aR, mR, mL, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector3.up);
                floorB.QuadAuto(aR, aR + Vector3.down * 1.5f, mR + Vector3.down * 1.5f, mR, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector3.right);
                floorB.QuadAuto(aL, aL + Vector3.down * 1.5f, mL + Vector3.down * 1.5f, mL, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector3.left);
            }
            batch.B(PropMats.CaveInside, mouthFloor).Append(innerB, Matrix4x4.identity);
            if (floor) batch.B(PropMats.CaveFloor, mouthFloor).Append(floorB, Matrix4x4.identity);
            PropRocks.Route(outerB, Matrix4x4.identity, batch, mouthFloor, outer, moss, PropMats.Crevice, 0.62f, -0.5f);
            var colB = new PropMeshBuilder();
            colB.Append(innerB, Matrix4x4.identity);
            if (floor) colB.Append(floorB, Matrix4x4.identity);
            colB.DoubleSide(0, 0);
            PropCol.Mesh(cols, colB.ToMesh("GrottoInnerCol", false), Vector3.zero, Quaternion.identity, Vector3.one, "GrottoInner");
            PropCol.Mesh(cols, outerB.ToMesh("GrottoOuterCol", false), Vector3.zero, Quaternion.identity, Vector3.one, "GrottoOuter");
            return new Result { M = M, centers = centers, rings = rings };
        }

        /// The white guideline rope (GDD 14.6) along the cave floor from the mouth to the last ring.
        public static void Guideline(PropBatch batch, Result g, float height = 0.35f)
        {
            Vector3 prev = g.W(g.centers[0] + new Vector3(0f, height, -1.2f));
            for (int k = 0; k < g.centers.Length; k++)
            {
                Vector3 p = g.W(g.centers[k] + new Vector3(g.rings[k].z * 0.55f, height, 0f));
                batch.W(PropMats.White, p).Rod(prev, p, 0.012f, 0.012f, 3);
                batch.W(PropMats.DarkMetal, p).Rod(p, p - Vector3.up * height, 0.02f, 0.02f, 4);
                prev = p;
            }
        }
    }
}
