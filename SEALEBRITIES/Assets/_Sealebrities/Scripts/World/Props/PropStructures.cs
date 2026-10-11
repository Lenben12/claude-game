using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Dock, helipad and lighthouse in the low-poly style (chunky planks, 6-8 sided posts, flat colors).
    public static class PropStructures
    {
        static float G(float x, float z) => PropGround.H(x, z);

        // ================================================================== dock
        /// The dock (built by PropDock: weathered planks with gaps, posts with wet bands, working ladder, lanterns, ...).
        public static IEnumerator BuildDock(Transform island, PropExclusion excl) => PropDock.Build(island, excl);

        // ================================================================== helipad
        public static IEnumerator BuildHelipad(Transform island, PropExclusion excl)
        {
            Vector3 c = WorldLayout.Helipad;
            const float R = 6f;
            const int Sides = 12;
            PropGround.MinMax(c.x, c.z, R + 0.5f, out float gmin, out float gmax);
            float top = Mathf.Max(gmax + 0.2f, 0.9f);
            float bottom = gmin - 0.8f;
            PropInfo.HelipadTopY = top;

            var root = PropUtil.WorldRoot("Helipad", island);
            var cols = new GameObject("Colliders").transform;
            cols.SetParent(root, false);
            var batch = new PropBatch(0f);

            // flat concrete disk: the top carries the crisp "H" block texture, a yellow ring is geometry
            Vector3 ct = new Vector3(c.x, top, c.z);
            batch.B(PropMats.Helipad).Disk(ct, R, Sides, Quaternion.identity, true, 1f, true, 15f);
            // (a +y rotation of -15° turns the ring's vertex angles by +15°, matching the disk's angle offset)
            batch.B(PropMats.Marking).Ring(ct + Vector3.up * 0.012f, R - 0.75f, R - 0.4f, Sides, Quaternion.Euler(0f, -15f, 0f));
            batch.B(PropMats.Concrete).Cylinder(new Vector3(c.x, top - 0.35f, c.z), Quaternion.identity, R + 0.05f, R, 0.35f, Sides, false, false, 1f, 1f, 0f, false, 15f);
            batch.B(PropMats.ConcreteDark).Cylinder(new Vector3(c.x, bottom, c.z), Quaternion.identity, R + 0.15f, R + 0.05f, top - 0.35f - bottom, Sides, false, false, 1f, 1f, 0f, false, 15f);
            var cb = new PropMeshBuilder();
            cb.Cylinder(new Vector3(0f, bottom - top, 0f), Quaternion.identity, R, R, top - bottom, 24, true, true);
            PropCol.Mesh(cols, cb.ToMesh("HelipadCollider", false), ct, Quaternion.identity, Vector3.one, "HelipadCol");

            // edge lights (emissive, switched by a night controller)
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI * 2f / 8f;
                Vector3 p = ct + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (R - 0.2f);
                batch.B(PropMats.DarkMetal).Cylinder(p, Quaternion.identity, 0.1f, 0.09f, 0.06f, 6, false, true);
                batch.B(PropMats.PadBulb).Ico(p + Vector3.up * 0.07f, new Vector3(0.08f, 0.07f, 0.08f), Quaternion.identity, 0);
            }
            var lightsGo = new GameObject("PadLights");
            lightsGo.transform.SetParent(root, false);
            lightsGo.transform.position = ct + Vector3.up * 1.2f;
            var padLight = lightsGo.AddComponent<Light>();
            padLight.type = LightType.Point;
            padLight.color = new Color(1f, 0.85f, 0.55f);
            padLight.range = 11f;
            padLight.intensity = 0.9f;
            padLight.shadows = LightShadows.None;
            PropNightLight.Attach(lightsGo, padLight, PropMats.PadBulb, new Color(3f, 2.4f, 0.8f), 0.15f, 0.4f, false);

            // steps toward the hut when the pad stands above the ground
            Vector3 dir = WorldLayout.Hut - c;
            dir.y = 0f;
            dir.Normalize();
            Quaternion sr = Quaternion.LookRotation(dir);
            for (int i = 0; i < 25; i++)
            {
                float stepTop = top - (i + 1) * 0.2f;
                Vector3 p = c + dir * (R + 0.16f + i * 0.32f);
                float g = G(p.x, p.z);
                if (stepTop < g - 0.02f) break;
                float h = stepTop - (g - 0.3f);
                Vector3 sc = new Vector3(p.x, stepTop - h * 0.5f, p.z);
                batch.B(i % 2 == 0 ? PropMats.Concrete : PropMats.ConcreteDark).Box(sc, new Vector3(2.2f, h, 0.32f), sr);
                PropCol.Box(cols, sc, new Vector3(2.2f, h, 0.32f), sr);
            }

            // windsock
            Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;
            Vector3 sockPos = c + side * (R + 2.2f) - dir * 1.5f;
            sockPos.y = G(sockPos.x, sockPos.z);
            Windsock(root, sockPos, batch);
            PropCol.Capsule(cols, sockPos + Vector3.up * 2.5f, 0.1f, 5f, Quaternion.identity);

            // drums and a pallet
            var rng = new System.Random(55);
            Vector3 dp = c - side * (R + 2.5f) - dir * 1f;
            for (int i = 0; i < 3; i++)
            {
                Vector3 p = dp + new Vector3(PropUtil.Range(rng, -0.9f, 0.9f), 0f, PropUtil.Range(rng, -0.9f, 0.9f));
                p.y = G(p.x, p.z) - 0.04f;
                Drum(batch, p, i == 1 ? PropMats.DrumRed : PropMats.DrumYellow);
                PropCol.Capsule(cols, p + Vector3.up * 0.44f, 0.3f, 0.88f, Quaternion.identity);
            }
            Vector3 pal = dp + dir * 2.2f;
            pal.y = G(pal.x, pal.z);
            for (int k = 0; k < 5; k++)
                batch.B(PropMats.WoodLight).Box(pal + sr * new Vector3(-0.48f + k * 0.24f, 0.13f, 0f), new Vector3(0.18f, 0.04f, 1.2f), sr);
            for (int k = -1; k <= 1; k++)
                batch.B(PropMats.Wood).Box(pal + sr * new Vector3(0f, 0.06f, k * 0.5f), new Vector3(1.1f, 0.11f, 0.11f), sr);

            batch.Flush(root, "HelipadMesh", ShadowCastingMode.On, 0f);
            excl.Add(c.x, c.z, R + 4.5f);
            excl.Add(sockPos.x, sockPos.z, 1.5f);
            excl.Add(dp.x, dp.z, 3f);
            yield break;
        }

        /// Standing oil drum: 8-sided body with two darker rims.
        public static void Drum(PropBatch batch, Vector3 basePos, Material mat)
        {
            const float r = 0.29f, h = 0.88f;
            batch.B(mat).Cylinder(basePos, Quaternion.identity, r, r, h, 8, false, true);
            var rim = batch.B(PropMats.DarkMetal);
            rim.Cylinder(basePos + Vector3.up * 0.28f, Quaternion.identity, r + 0.012f, r + 0.012f, 0.05f, 8);
            rim.Cylinder(basePos + Vector3.up * 0.6f, Quaternion.identity, r + 0.012f, r + 0.012f, 0.05f, 8);
        }

        static void Windsock(Transform root, Vector3 groundPos, PropBatch batch)
        {
            const float poleH = 5f;
            batch.B(PropMats.Metal).Cylinder(groundPos - Vector3.up * 0.3f, Quaternion.identity, 0.08f, 0.06f, poleH + 0.3f, 6, false, true);
            var pivot = new GameObject("Windsock");
            pivot.transform.SetParent(root, false);
            pivot.transform.position = groundPos + Vector3.up * poleH;
            var sockT = new GameObject("Sock").transform;
            sockT.SetParent(pivot.transform, false);

            // four orange / white segments (6-sided frustums), each segment its own color
            var orange = new PropMeshBuilder();
            var white = new PropMeshBuilder();
            Quaternion along = Quaternion.FromToRotation(Vector3.up, Vector3.right);
            const int segs = 4;
            const float len = 2.3f, rA = 0.34f, rB = 0.13f;
            for (int i = 0; i < segs; i++)
            {
                float t0 = (float)i / segs, t1 = (float)(i + 1) / segs;
                var b = i % 2 == 0 ? orange : white;
                b.Cylinder(new Vector3(0.12f + t0 * len, 0f, 0f), along, Mathf.Lerp(rA, rB, t0), Mathf.Lerp(rA, rB, t1), len / segs, 6);
                b.Cylinder(new Vector3(0.12f + t0 * len, 0f, 0f), along, Mathf.Lerp(rA, rB, t0) - 0.01f, Mathf.Lerp(rA, rB, t1) - 0.01f, len / segs, 6, false, false, 1f, 1f, 0f, true);
            }
            PropUtil.MeshObject(sockT, "SockOrange", orange.ToMesh("WindsockOrange"), PropMats.Orange, Vector3.zero, Quaternion.identity);
            PropUtil.MeshObject(sockT, "SockWhite", white.ToMesh("WindsockWhite"), PropMats.White, Vector3.zero, Quaternion.identity);
            var hb = new PropMeshBuilder();
            hb.Torus(new Vector3(0.12f, 0f, 0f), along, 0.34f, 0.025f, 8, 3);
            hb.Rod(Vector3.zero, new Vector3(0.12f, 0f, 0f), 0.03f, 0.03f, 4);
            PropUtil.MeshObject(sockT, "Hoop", hb.ToMesh("WindsockHoop"), PropMats.Metal, Vector3.zero, Quaternion.identity);
            var ws = pivot.AddComponent<PropWindsock>();
            ws.sock = sockT;
            ws.baseYaw = 35f;
        }

        // ================================================================== lighthouse
        public static IEnumerator BuildLighthouse(Transform island, PropExclusion excl)
        {
            Vector3 c = WorldLayout.Lighthouse;
            PropGround.MinMax(c.x, c.z, 3.8f, out float gmin, out float gmax);
            float baseTop = Mathf.Max(gmax + 0.4f, 1.5f);
            float footY = gmin - 1.5f;

            var root = PropUtil.WorldRoot("Lighthouse", island);
            var cols = new GameObject("Colliders").transform;
            cols.SetParent(root, false);
            var batch = new PropBatch(0f);

            // octagonal stone foundation
            batch.B(PropMats.Stone).Cylinder(new Vector3(c.x, footY, c.z), Quaternion.identity, 3.75f, 3.5f, baseTop - footY, 8, false, true, 1f, 1f, 0f, false, 22.5f);
            var fcb = new PropMeshBuilder();
            fcb.Cylinder(Vector3.zero, Quaternion.identity, 3.75f, 3.5f, baseTop - footY, 8, true, true, 1f, 1f, 0f, false, 22.5f);
            PropCol.Mesh(cols, fcb.ToMesh("LighthouseFoundation", false), new Vector3(c.x, footY, c.z), Quaternion.identity, Vector3.one);

            // chunky 8-sided tower with red / white bands
            const float H = 15.5f, r0 = 2.4f, r1 = 1.65f;
            const int bands = 6;
            Vector3 tb = new Vector3(c.x, baseTop, c.z);
            for (int i = 0; i < bands; i++)
            {
                float t0 = (float)i / bands, t1 = (float)(i + 1) / bands;
                var m = i % 2 == 0 ? PropMats.White : PropMats.RedMetal;
                batch.B(m).Cylinder(tb + Vector3.up * (H * t0), Quaternion.identity, Mathf.Lerp(r0, r1, t0), Mathf.Lerp(r0, r1, t1), H / bands + 0.01f, 8, false, false, 1f, 1f, 0f, false, 22.5f);
            }
            var tcb = new PropMeshBuilder();
            // up to the gallery floor only (the lantern room has its own collider, so the gallery ring stays walkable)
            tcb.Cylinder(Vector3.zero, Quaternion.identity, r0, r1, H, 16, true, true);
            PropCol.Mesh(cols, tcb.ToMesh("LighthouseTower", false), tb, Quaternion.identity, Vector3.one);

            // door facing the hut path
            Vector3 toHut = WorldLayout.Hut - c;
            toHut.y = 0f;
            toHut.Normalize();
            Quaternion dr = Quaternion.LookRotation(toHut);
            float apothem = r0 * Mathf.Cos(Mathf.PI / 8f);
            batch.B(PropMats.Shutter).Box(tb + toHut * (apothem - 0.02f) + Vector3.up * 1.05f, new Vector3(1.1f, 2.1f, 0.24f), dr);
            batch.B(PropMats.Wood).Box(tb + toHut * (apothem - 0.06f) + Vector3.up * 1.1f, new Vector3(1.36f, 2.32f, 0.2f), dr);
            batch.B(PropMats.Stone).Box(tb + toHut * (r0 + 0.45f) + Vector3.up * -0.1f, new Vector3(1.6f, 0.2f, 0.9f), dr);
            // small windows spiralling up
            for (int i = 0; i < 4; i++)
            {
                float y = 4.2f + i * 3f;
                float a = 45f * (1 + i * 2) * Mathf.Deg2Rad;   // octagon face centers (vertices sit at 22.5° + k·45°)
                Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                float rr = Mathf.Lerp(r0, r1, y / H) * Mathf.Cos(Mathf.PI / 8f);
                Quaternion wr = Quaternion.LookRotation(d);
                batch.B(PropMats.Trim).Box(tb + d * (rr - 0.02f) + Vector3.up * y, new Vector3(0.7f, 0.95f, 0.16f), wr);
                batch.B(PropMats.LanternRoom).Box(tb + d * (rr + 0.04f) + Vector3.up * y, new Vector3(0.48f, 0.72f, 0.06f), wr);
            }
            if (PropRun.Due()) yield return null;

            // gallery: chunky platform with a simple railing
            float gy = baseTop + H;
            var dm = batch.B(PropMats.DarkMetal);
            dm.Cylinder(new Vector3(c.x, gy - 0.6f, c.z), Quaternion.identity, r1 - 0.05f, 2.8f, 0.6f, 8, false, false, 1f, 1f, 0f, false, 22.5f);
            dm.Cylinder(new Vector3(c.x, gy, c.z), Quaternion.identity, 2.8f, 2.8f, 0.16f, 8, false, true, 1f, 1f, 0f, false, 22.5f);
            const int posts = 8;
            for (int i = 0; i < posts; i++)
            {
                float a0 = (22.5f + i * 45f) * Mathf.Deg2Rad, a1 = (22.5f + (i + 1) * 45f) * Mathf.Deg2Rad;
                Vector3 p0 = new Vector3(c.x + Mathf.Cos(a0) * 2.68f, gy + 0.16f, c.z + Mathf.Sin(a0) * 2.68f);
                Vector3 p1 = new Vector3(c.x + Mathf.Cos(a1) * 2.68f, gy + 0.16f, c.z + Mathf.Sin(a1) * 2.68f);
                dm.Rod(p0, p0 + Vector3.up * 1.0f, 0.045f, 0.045f, 4);
                dm.Beam(p0 + Vector3.up * 1.0f, p1 + Vector3.up * 1.0f, 0.08f, 0.08f);
                dm.Beam(p0 + Vector3.up * 0.5f, p1 + Vector3.up * 0.5f, 0.05f, 0.05f);
                // solid railing segment
                Vector3 seg = p1 - p0;
                PropCol.Box(cols, (p0 + p1) * 0.5f + Vector3.up * 0.55f, new Vector3(0.12f, 1.1f, seg.magnitude), Quaternion.LookRotation(seg.normalized), "GalleryRail");
            }
            // walkable gallery floor
            var gcb = new PropMeshBuilder();
            gcb.Cylinder(Vector3.zero, Quaternion.identity, 2.8f, 2.8f, 0.16f, 8, true, true, 1f, 1f, 0f, false, 22.5f);
            var gmc = PropCol.Mesh(cols, gcb.ToMesh("LighthouseGallery", false), new Vector3(c.x, gy, c.z), Quaternion.identity, Vector3.one, "GalleryFloor");
            gmc.convex = true;
            PropInfo.LighthouseGallery = new Vector3(c.x, gy + 0.16f, c.z);

            // lamp room: dark lantern room, glass, mullions, red cone roof with a ball
            float ly = gy + 0.16f;
            batch.B(PropMats.LanternRoom).Cylinder(new Vector3(c.x, ly, c.z), Quaternion.identity, 1.4f, 1.4f, 0.55f, 8, false, false, 1f, 1f, 0f, false, 22.5f);
            var lrb = new PropMeshBuilder();
            lrb.Cylinder(Vector3.zero, Quaternion.identity, 1.42f, 1.42f, 2.4f, 8, true, true, 1f, 1f, 0f, false, 22.5f);
            var lrc = PropCol.Mesh(cols, lrb.ToMesh("LanternRoomCol", false), new Vector3(c.x, ly, c.z), Quaternion.identity, Vector3.one, "LanternRoom");
            lrc.convex = true;
            batch.B(PropMats.Glass).Cylinder(new Vector3(c.x, ly + 0.55f, c.z), Quaternion.identity, 1.32f, 1.32f, 1.8f, 8, false, false, 1f, 1f, 0f, false, 22.5f);
            for (int i = 0; i < 8; i++)
            {
                float a = (22.5f + i * 45f) * Mathf.Deg2Rad;
                Vector3 p = new Vector3(c.x + Mathf.Cos(a) * 1.33f, ly + 0.55f, c.z + Mathf.Sin(a) * 1.33f);
                dm.Rod(p, p + Vector3.up * 1.8f, 0.06f, 0.06f, 4);
            }
            float roofY = ly + 2.35f;
            batch.B(PropMats.RedMetal).Cylinder(new Vector3(c.x, roofY, c.z), Quaternion.identity, 1.7f, 0f, 1.35f, 8, true, false, 1f, 1f, 0f, false, 22.5f);
            dm.Cylinder(new Vector3(c.x, roofY - 0.1f, c.z), Quaternion.identity, 1.5f, 1.5f, 0.1f, 8, true, false, 1f, 1f, 0f, false, 22.5f);
            dm.Ico(new Vector3(c.x, roofY + 1.45f, c.z), Vector3.one * 0.22f, Quaternion.identity, 0);
            dm.Rod(new Vector3(c.x, roofY + 1.6f, c.z), new Vector3(c.x, roofY + 2.6f, c.z), 0.035f, 0.02f, 4);

            // lamp: lens (static, emissive) + rotating pivot with beams and spot lights
            Vector3 lampC = new Vector3(c.x, ly + 1.35f, c.z);
            batch.B(PropMats.Lens).Cylinder(lampC - Vector3.up * 0.4f, Quaternion.identity, 0.42f, 0.42f, 0.8f, 8, true, true);
            dm.Cylinder(new Vector3(c.x, ly + 0.55f, c.z), Quaternion.identity, 0.26f, 0.2f, 0.42f, 6, false, true);

            var pivot = new GameObject("LighthouseLamp");
            pivot.transform.SetParent(root, false);
            pivot.transform.position = lampC;
            var lamp = pivot.AddComponent<PropLighthouseLamp>();
            lamp.pivot = pivot.transform;
            lamp.lens = PropMats.Lens;

            var beamMat = new Material(MaterialLibrary.UnlitShader) { name = "Prop_LighthouseBeam" };
            beamMat.SetTexture("_BaseMap", PropTex.Beam);
            beamMat.SetColor("_BaseColor", new Color(1f, 0.95f, 0.75f, 0f));
            MaterialLibrary.MakeTransparent(beamMat, true);
            beamMat.SetFloat("_DstBlend", (float)BlendMode.One);
            lamp.beamMat = beamMat;

            var beams = new Renderer[2];
            var spots = new Light[2];
            for (int i = 0; i < 2; i++)
            {
                Vector3 fwd = i == 0 ? Vector3.forward : Vector3.back;
                // pitch sign flips for the backward beam so both tilt slightly downward
                Quaternion tiltDown = Quaternion.Euler(i == 0 ? 2.5f : -2.5f, 0f, 0f);
                var bb = new PropMeshBuilder();
                bb.Cylinder(Vector3.zero, Quaternion.FromToRotation(Vector3.up, fwd), 0.3f, 8f, 110f, 10, false, false, 1f, 1f / 110f);
                var mr = PropUtil.MeshObject(pivot.transform, "Beam" + i, bb.ToMesh("LighthouseBeam", false), beamMat, Vector3.zero, tiltDown, false);
                mr.receiveShadows = false;
                mr.enabled = false;
                beams[i] = mr;

                var sgo = new GameObject("Spot" + i);
                sgo.transform.SetParent(pivot.transform, false);
                sgo.transform.localRotation = Quaternion.LookRotation(tiltDown * fwd);
                var sl = sgo.AddComponent<Light>();
                sl.type = LightType.Spot;
                sl.color = new Color(1f, 0.93f, 0.75f);
                sl.range = 180f;
                sl.spotAngle = 10f;
                sl.innerSpotAngle = 4f;
                sl.intensity = 0f;
                sl.shadows = LightShadows.None;
                sl.enabled = false;
                spots[i] = sl;
            }
            lamp.beams = beams;
            lamp.spots = spots;
            lamp.spotIntensity = 60f;
            var glow = PropUtil.AddPointLight(pivot.transform, lampC, new Color(1f, 0.85f, 0.6f), 14f, 0f, "LampGlow");
            glow.enabled = false;
            lamp.glow = glow;

            LighthouseArea(root, batch, cols, c, baseTop, toHut, gy, H, r0, r1);

            batch.Flush(root, "LighthouseMesh", ShadowCastingMode.On, 0f);
            excl.Add(c.x, c.z, 8f);
            excl.AddLine(c, c + toHut * 6f, 2f);
        }

        static void FenceCol(Transform cols, Vector3 a, Vector3 b)
        {
            Vector3 seg = b - a;
            seg.y = 0f;
            if (seg.sqrMagnitude < 0.01f) return;
            PropCol.Box(cols, (a + b) * 0.5f + Vector3.up * 0.5f, new Vector3(0.1f, 1.1f, seg.magnitude + 0.1f), Quaternion.LookRotation(seg.normalized), "Fence");
        }

        /// Lighthouse surroundings: an exterior ladder up to the gallery (PropLadder), a white picket fence around the
        /// plateau with a gate toward the path, a bench facing the sea and a coin telescope.
        static void LighthouseArea(Transform root, PropBatch batch, Transform cols, Vector3 c, float baseTop, Vector3 toHut, float gy, float H, float r0, float r1)
        {
            var rng = new System.Random(808);
            // ---- ladder on the sea side (opposite the door)
            Vector3 outDir = -toHut;
            Vector3 side = Vector3.Cross(Vector3.up, outDir).normalized;
            float lr = 2.95f;
            Vector3 foot = new Vector3(c.x, baseTop, c.z) + outDir * lr;
            float topY = gy + 1.25f;
            var dm = batch.B(PropMats.DarkMetal);
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 rb = foot + side * (s * 0.3f);
                dm.Beam(rb - Vector3.up * 0.1f, new Vector3(rb.x, topY, rb.z), 0.06f, 0.06f);
                dm.Beam(new Vector3(rb.x, topY, rb.z), new Vector3(rb.x, topY, rb.z) - outDir * 0.35f, 0.05f, 0.05f);
            }
            for (float y = baseTop + 0.3f; y < topY - 0.1f; y += 0.32f)
                dm.Rod(foot - side * 0.3f + Vector3.up * (y - baseTop), foot + side * 0.3f + Vector3.up * (y - baseTop), 0.025f, 0.025f, 4);
            // brackets to the tower wall
            for (float y = 1.5f; y < H; y += 3.5f)
            {
                float wr = Mathf.Lerp(r0, r1, y / H) * Mathf.Cos(Mathf.PI / 8f);
                Vector3 a = new Vector3(c.x, baseTop + y, c.z) + outDir * wr;
                Vector3 b = new Vector3(c.x, baseTop + y, c.z) + outDir * lr;
                dm.Beam(a + side * 0.3f, b + side * 0.3f, 0.05f, 0.05f);
                dm.Beam(a - side * 0.3f, b - side * 0.3f, 0.05f, 0.05f);
            }
            var lgo = new GameObject("LighthouseLadder");
            lgo.transform.SetParent(root, false);
            lgo.transform.SetPositionAndRotation(new Vector3(foot.x, (baseTop + topY) * 0.5f, foot.z) + outDir * 0.15f, Quaternion.LookRotation(outDir));
            var bc = lgo.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.size = new Vector3(0.9f, topY - baseTop, 0.7f);
            var ladder = lgo.AddComponent<PropLadder>();
            float yawOut = Mathf.Atan2(outDir.x, outDir.z) * Mathf.Rad2Deg;
            ladder.bottomStand = new Vector3(foot.x, baseTop + 0.05f, foot.z) + outDir * 0.9f;
            ladder.bottomYaw = yawOut + 180f;
            ladder.topStand = new Vector3(c.x, gy + 0.2f, c.z) + outDir * 2.15f;
            ladder.topYaw = yawOut;
            ladder.foot = foot;
            ladder.proximityTop = true;      // the gallery railing may block the ray to the ladder top
            ladder.proximityBottom = true;
            PropInfo.LighthouseLadderFoot = foot;

            // ---- picket fence around the plateau, gate toward the path
            float fr = 8.6f;
            int n = 72;
            var picket = batch.B(PropMats.Trim);
            Vector3 prev = Vector3.zero, colStart = Vector3.zero;
            bool havePrev = false, haveCol = false;
            int colCount = 0;
            float gateA = Mathf.Atan2(toHut.z, toHut.x);
            for (int i = 0; i <= n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                float da = Mathf.DeltaAngle(a * Mathf.Rad2Deg, gateA * Mathf.Rad2Deg);
                bool gate = Mathf.Abs(da) < 11f;
                Vector3 p = new Vector3(c.x + Mathf.Cos(a) * fr, 0f, c.z + Mathf.Sin(a) * fr);
                p.y = PropGround.H(p.x, p.z);
                if (gate)
                {
                    if (haveCol && colCount > 0) FenceCol(cols, colStart, prev);
                    havePrev = false;
                    haveCol = false;
                    continue;
                }
                Quaternion pr = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f);
                picket.Box(p + Vector3.up * 0.42f, new Vector3(0.08f, 0.95f, 0.035f), pr);
                picket.Face3(p + Vector3.up * 0.9f + pr * new Vector3(-0.04f, 0f, 0.018f), p + Vector3.up * 0.9f + pr * new Vector3(0.04f, 0f, 0.018f), p + Vector3.up * 1.0f + pr * new Vector3(0f, 0f, 0.018f), pr * Vector3.forward);
                if (havePrev)
                {
                    picket.Beam(prev + Vector3.up * 0.3f, p + Vector3.up * 0.3f, 0.05f, 0.07f);
                    picket.Beam(prev + Vector3.up * 0.72f, p + Vector3.up * 0.72f, 0.05f, 0.07f);
                }
                if (!haveCol) { colStart = p; haveCol = true; colCount = 0; }
                else if (++colCount >= 3) { FenceCol(cols, colStart, p); colStart = p; colCount = 0; }
                prev = p;
                havePrev = true;
            }
            if (haveCol && colCount > 0) FenceCol(cols, colStart, prev);

            // ---- bench facing the sea and a coin telescope
            Vector3 benchDir = Quaternion.Euler(0f, 35f, 0f) * outDir;
            Vector3 bp = new Vector3(c.x, 0f, c.z) + benchDir * 6.4f;
            bp.y = PropGround.H(bp.x, bp.z);
            Quaternion br = Quaternion.LookRotation(-benchDir);
            var w = batch.B(PropMats.Wood3);
            for (int i = 0; i < 3; i++) w.Box(bp + br * new Vector3(0f, 0.45f, 0.12f - i * 0.13f), new Vector3(1.5f, 0.05f, 0.11f), br);
            for (int i = 0; i < 2; i++) w.Box(bp + br * new Vector3(0f, 0.7f + i * 0.17f, 0.24f), new Vector3(1.5f, 0.12f, 0.04f), br * Quaternion.Euler(10f, 0f, 0f));
            var d = batch.B(PropMats.WoodDark);
            for (int s = -1; s <= 1; s += 2) d.Box(bp + br * new Vector3(s * 0.62f, 0.22f, 0f), new Vector3(0.07f, 0.44f, 0.36f), br);
            PropCol.Box(cols, bp + br * new Vector3(0f, 0.3f, 0.05f), new Vector3(1.55f, 0.6f, 0.5f), br, "Bench");
            PropInfo.LighthouseBench = bp + Vector3.up * 0.5f;

            Vector3 telDir = Quaternion.Euler(0f, -40f, 0f) * outDir;
            Vector3 tp = new Vector3(c.x, 0f, c.z) + telDir * 7.2f;
            tp.y = PropGround.H(tp.x, tp.z);
            var met = batch.B(PropMats.Flat(PropPal.HullBlue));
            met.Cylinder(tp - Vector3.up * 0.1f, Quaternion.identity, 0.16f, 0.1f, 1.15f, 6, false, true);
            met.Cylinder(tp + Vector3.up * 1.05f, Quaternion.identity, 0.08f, 0.08f, 0.12f, 6, false, true);
            Quaternion tr = Quaternion.LookRotation(telDir) * Quaternion.Euler(-12f, 0f, 0f);
            Vector3 tc = tp + Vector3.up * 1.28f;
            batch.B(PropMats.Flat(PropPal.Sun)).Cylinder(tc - tr * Vector3.forward * 0.35f, tr * Quaternion.Euler(90f, 0f, 0f), 0.11f, 0.13f, 0.7f, 8, true, true);
            batch.B(PropMats.Ink).Cylinder(tc + tr * Vector3.forward * 0.35f, tr * Quaternion.Euler(90f, 0f, 0f), 0.135f, 0.135f, 0.04f, 8, true, true);
            met.Box(tc - Vector3.up * 0.14f, new Vector3(0.18f, 0.16f, 0.22f), Quaternion.LookRotation(telDir));
            PropCol.Capsule(cols, tp + Vector3.up * 0.7f, 0.2f, 1.4f, Quaternion.identity, "Telescope");
        }
    }
}
