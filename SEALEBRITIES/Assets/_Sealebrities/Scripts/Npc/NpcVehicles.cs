using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// A story vehicle (yacht „Clout Nine“, research catamaran „RV Plankton“, gold mini sub „Bottom Line“, Wanda's
    /// chopper, outrigger canoes). Host-driven like the NPCs (NpcNet replicates index, presence, position, yaw).
    /// World objects → faceted like the world (How to Fish boats).
    public sealed class NpcVehicle
    {
        public readonly string Id;
        public readonly int Index;
        public readonly Transform Root;
        public bool Present;
        public bool Floating = true;
        public Vector3 Pos;
        public float Yaw;
        public Transform Rotor, TailRotor;
        public float RotorSpeed;

        Vector3 from, to;
        float moveT = 1f, moveLen = 1f;
        float fromYaw, toYaw;
        Vector3 netPos;
        float netYaw;
        bool netValid, shown = true;
        readonly float bobSeed;

        public NpcVehicle(string id, int index, Transform root)
        {
            Id = id;
            Index = index;
            Root = root;
            bobSeed = (NpcUtil.StableHash(id) & 255) / 40f;
        }

        public bool Moving => moveT < 1f;

        /// Host: teleport.
        public void Place(Vector3 p, float yaw)
        {
            Pos = p;
            Yaw = yaw;
            from = to = p;
            fromYaw = toYaw = yaw;
            moveT = 1f;
        }

        /// Host: glide to p over `seconds` (ease in/out), turning to yaw.
        public void SailTo(Vector3 p, float yaw, float seconds)
        {
            from = Pos;
            to = p;
            fromYaw = Yaw;
            toYaw = yaw;
            moveLen = Mathf.Max(0.5f, seconds);
            moveT = 0f;
        }

        public void HostTick(float dt)
        {
            if (moveT >= 1f) return;
            moveT = Mathf.Min(1f, moveT + dt / moveLen);
            float e = moveT * moveT * (3f - 2f * moveT);
            Pos = Vector3.Lerp(from, to, e);
            Yaw = Mathf.LerpAngle(fromYaw, toYaw, Mathf.Clamp01(e * 1.6f));
        }

        public void ApplyNet(bool present, Vector3 p, float yaw)
        {
            Present = present;
            bool first = !netValid;
            netValid = true;
            netPos = p;
            netYaw = yaw;
            if (first || (p - Pos).sqrMagnitude > 400f) { Pos = p; Yaw = yaw; }
        }

        public void Animate(float dt, Vector3 cam)
        {
            if (!Net.HasAuthority && netValid)
            {
                float k = 1f - Mathf.Exp(-5f * dt);
                Pos = Vector3.Lerp(Pos, netPos, k);
                Yaw = Mathf.LerpAngle(Yaw, netYaw, k);
            }
            bool show = Present && (Pos - cam).sqrMagnitude < 900f * 900f;
            if (show != shown) { shown = show; Root.gameObject.SetActive(show); }
            if (!show) return;
            float t = Time.time + bobSeed;
            Vector3 p = Pos;
            Quaternion rot = Quaternion.Euler(0f, Yaw, 0f);
            if (Floating)
            {
                p.y = Waves.HeightAt(p.x, p.z) + Mathf.Sin(t * 0.9f) * 0.05f;
                rot *= Quaternion.Euler(Mathf.Sin(t * 0.7f) * 1.2f, 0f, Mathf.Sin(t * 0.55f) * 1.6f);
            }
            Root.SetPositionAndRotation(p, rot);
            if (Rotor != null && RotorSpeed > 0f) Rotor.Rotate(0f, RotorSpeed * dt, 0f, Space.Self);
            if (TailRotor != null && RotorSpeed > 0f) TailRotor.Rotate(RotorSpeed * 1.4f * dt, 0f, 0f, Space.Self);
        }
    }

    /// Procedural vehicle models (faceted, How to Fish palette).
    public static class NpcVehicleBuilder
    {
        static Material W(string hex) => NpcMesh.World(LowPoly.Hex(hex));
        static Material W(Color c) => NpcMesh.World(c);

        // ------------------------------------------------------------------ hull loft
        /// Boat hull along +Z (bow at +Z), deck at y = deckY, keel at -depth, beam = full width. Faceted.
        public static Mesh Hull(string key, float length, float beam, float depth, float deckY, float bowFine = 0.3f, int stations = 12)
        {
            return NpcMesh.Cached("hull_" + key, () =>
            {
                var verts = new List<Vector3>();
                var tris = new List<int>();
                // per station: 5 points (deck L, chine L, keel, chine R, deck R)
                int per = 5;
                for (int s = 0; s <= stations; s++)
                {
                    float t = s / (float)stations;                 // 0 stern … 1 bow
                    float z = (t - 0.5f) * length;
                    float wFac = t < 1f - bowFine ? 1f : Mathf.Cos((t - (1f - bowFine)) / bowFine * Mathf.PI * 0.5f);
                    wFac = Mathf.Max(0.02f, wFac);
                    if (t < 0.08f) wFac *= Mathf.Lerp(0.88f, 1f, t / 0.08f);
                    float hw = beam * 0.5f * wFac;
                    float keel = -depth * Mathf.Lerp(0.75f, 1f, Mathf.Sin(t * Mathf.PI));
                    float sheer = deckY + (t > 0.75f ? (t - 0.75f) * 1.2f : 0f);
                    verts.Add(new Vector3(-hw, sheer, z));
                    verts.Add(new Vector3(-hw * 0.82f, keel * 0.45f, z));
                    verts.Add(new Vector3(0f, keel, z));
                    verts.Add(new Vector3(hw * 0.82f, keel * 0.45f, z));
                    verts.Add(new Vector3(hw, sheer, z));
                }
                float midY = (deckY - depth) * 0.5f;
                // sides + bottom: outward = away from the centre line
                for (int s = 0; s < stations; s++)
                    for (int k = 0; k < per - 1; k++)
                    {
                        int a = s * per + k, b = a + 1, c = a + per, d = c + 1;
                        Vector3 mid = (verts[a] + verts[b] + verts[c] + verts[d]) * 0.25f;
                        Vector3 outward = new Vector3(mid.x, mid.y - midY, 0f);
                        if (outward.sqrMagnitude < 1e-6f) outward = Vector3.down;
                        Tri(verts, tris, a, c, d, outward);
                        Tri(verts, tris, a, d, b, outward);
                    }
                // deck
                for (int s = 0; s < stations; s++)
                {
                    int a = s * per, b = s * per + 4, c = (s + 1) * per, d = (s + 1) * per + 4;
                    Tri(verts, tris, a, b, d, Vector3.up);
                    Tri(verts, tris, a, d, c, Vector3.up);
                }
                // transom (stern, −Z)
                Tri(verts, tris, 0, 1, 2, Vector3.back);
                Tri(verts, tris, 0, 2, 4, Vector3.back);
                Tri(verts, tris, 2, 3, 4, Vector3.back);
                var m = new Mesh();
                m.SetVertices(verts);
                m.SetTriangles(tris, 0);
                m.RecalculateNormals();
                m.RecalculateBounds();
                return LowPoly.Flatten(m);
            });
        }

        /// Adds a triangle wound so its normal points along `outward`.
        static void Tri(List<Vector3> v, List<int> t, int a, int b, int c, Vector3 outward)
        {
            Vector3 n = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
            if (Vector3.Dot(n, outward) >= 0f) { t.Add(a); t.Add(b); t.Add(c); }
            else { t.Add(a); t.Add(c); t.Add(b); }
        }

        static Mesh FlatBox(float w, float h, float d, float round = 0.15f) =>
            NpcMesh.Flat(NpcMesh.RoundBox(w, h, d, round, 3), "box" + w + "_" + h + "_" + d + "_" + round);

        static MeshRenderer P(Transform t, string n, Mesh m, Material mat, Vector3 pos, Vector3 euler = default) =>
            NpcMesh.Part(t, n, m, mat, pos, Vector3.one, euler, true);

        // ------------------------------------------------------------------ Clout Nine (B.3): 24 m white yacht
        public static GameObject Yacht()
        {
            var go = new GameObject("CloutNine");
            var t = go.transform;
            Material white = W("#EDE6DA"), gold = W("#E8C65A"), tint = W("#2E3140"), teak = W("#C9B08E"), water = W("#4FB8C8"), pink = W("#F29BBE"), ink = W("#1E1B2B");
            P(t, "Hull", Hull("yacht", 24f, 6.2f, 1.6f, 1.6f, 0.32f, 14), white, Vector3.zero);
            P(t, "Stripe", FlatBox(6.25f, 0.18f, 20f, 0.3f), gold, new Vector3(0f, 1.15f, -1f));
            P(t, "Deck", FlatBox(5.8f, 0.12f, 18f, 0.2f), teak, new Vector3(0f, 1.62f, -1.5f));
            // superstructure: two decks with tinted window bands
            P(t, "Cabin1", FlatBox(4.6f, 1.7f, 11f, 0.35f), white, new Vector3(0f, 2.5f, 0.5f));
            P(t, "Win1", FlatBox(4.7f, 0.55f, 10.4f, 0.4f), tint, new Vector3(0f, 2.7f, 0.5f));
            P(t, "Cabin2", FlatBox(3.6f, 1.3f, 6.5f, 0.35f), white, new Vector3(0f, 3.95f, 1.2f));
            P(t, "Win2", FlatBox(3.7f, 0.45f, 6.0f, 0.4f), tint, new Vector3(0f, 4.05f, 1.2f));
            P(t, "Radar", NpcMesh.Flat(NpcMesh.Cylinder(0.08f, 1.4f, 8), "yacht_mast"), gold, new Vector3(0f, 4.6f, 0.8f));
            P(t, "Dish", NpcMesh.Flat(NpcMesh.RoundBox(1.4f, 0.12f, 0.3f, 0.5f, 3), "yacht_dish"), white, new Vector3(0f, 5.9f, 0.8f));
            // hot tub on the aft deck with turquoise water
            P(t, "Tub", NpcMesh.Flat(NpcMesh.Cylinder(1.25f, 0.65f, 14), "yacht_tub"), white, new Vector3(0f, 1.62f, -7.2f));
            P(t, "TubWater", NpcMesh.Flat(NpcMesh.Cylinder(1.08f, 0.05f, 14), "yacht_tubw"), water, new Vector3(0f, 2.18f, -7.2f));
            // giant inflatable flamingo floating next to the tub
            var fl = NpcMesh.Node(t, "Flamingo", new Vector3(2.0f, 1.75f, -4.3f), new Vector3(0f, -30f, 0f));
            NpcMesh.Part(fl, "Body", NpcMesh.Sphere(10, 16), NpcMesh.Mat(LowPoly.Hex("#F29BBE"), 0.5f), new Vector3(0f, 0.45f, 0f), new Vector3(1.0f, 0.6f, 1.5f));
            var neck = new[] { new Vector3(0f, 0.6f, 0.55f), new Vector3(0f, 1.2f, 0.75f), new Vector3(0f, 1.75f, 0.55f), new Vector3(0f, 1.95f, 0.75f) };
            NpcMesh.Part(fl, "Neck", NpcMesh.Sweep("flamingo_neck", neck, 0.13f, 0.1f, 10), NpcMesh.Mat(LowPoly.Hex("#F29BBE"), 0.5f), Vector3.zero, Vector3.one);
            NpcMesh.Part(fl, "Head", NpcMesh.Sphere(10, 14), NpcMesh.Mat(LowPoly.Hex("#F29BBE"), 0.5f), new Vector3(0f, 2.0f, 0.85f), Vector3.one * 0.3f);
            NpcMesh.Part(fl, "Beak", NpcMesh.Cone(0.08f, 0.28f, 10), NpcMesh.Mat(LowPoly.Hex("#1E1B2B"), 0.4f), new Vector3(0f, 1.95f, 0.98f), Vector3.one, new Vector3(110f, 0f, 0f), false);
            // railing posts + gold name plate
            for (int i = 0; i < 9; i++)
                for (int s = -1; s <= 1; s += 2)
                    P(t, "Rail", NpcMesh.Flat(NpcMesh.Cylinder(0.035f, 0.9f, 6), "yacht_rail"), gold, new Vector3(s * 2.85f, 1.65f, -9.5f + i * 2.1f));
            P(t, "NamePlate", FlatBox(0.1f, 0.5f, 3.2f, 0.2f), gold, new Vector3(3.12f, 1.3f, -6f));
            P(t, "NamePlateL", FlatBox(0.1f, 0.5f, 3.2f, 0.2f), gold, new Vector3(-3.12f, 1.3f, -6f));
            P(t, "Speaker", FlatBox(0.5f, 0.7f, 0.5f, 0.3f), ink, new Vector3(1.9f, 2.0f, 4.5f));
            // round 3 (GDD §19): the old single box ended at 2.0 m – one walked through both cabin decks. Now the hull
            // (full beam aft, narrower toward the fine bow) up to the teak deck, both cabins and the hot tub.
            AddSolid(go, new Vector3(6.2f, 3.3f, 16.4f), new Vector3(0f, 0.03f, -3.8f));
            AddSolid(go, new Vector3(4.4f, 3.5f, 4.6f), new Vector3(0f, 0.13f, 6.6f));
            AddSolid(go, new Vector3(2.0f, 3.7f, 2.6f), new Vector3(0f, 0.23f, 10.2f));
            AddSolid(go, new Vector3(4.6f, 1.75f, 11f), new Vector3(0f, 2.5f, 0.5f));
            AddSolid(go, new Vector3(3.6f, 1.3f, 6.5f), new Vector3(0f, 3.95f, 1.2f));
            AddSolid(go, new Vector3(2.5f, 0.65f, 2.5f), new Vector3(0f, 1.95f, -7.2f));
            return go;
        }

        // ------------------------------------------------------------------ RV Plankton (B.4): grey-blue catamaran
        public static GameObject Catamaran()
        {
            var go = new GameObject("RVPlankton");
            var t = go.transform;
            Material hull = W("#7A8FA6"), white = W("#EDE6DA"), yellow = W("#F2C94C"), deck = W("#9AA0A8"), tint = W("#2E3140"), teal = W("#3FA9B5");
            for (int s = -1; s <= 1; s += 2)
                P(t, "Hull", Hull("cat", 10f, 1.3f, 0.8f, 0.6f, 0.4f, 10), hull, new Vector3(s * 1.7f, 0f, 0f));
            P(t, "Deck", FlatBox(4.6f, 0.2f, 7.5f, 0.15f), deck, new Vector3(0f, 0.85f, -0.5f));
            P(t, "Cabin", FlatBox(2.8f, 1.6f, 3.2f, 0.3f), white, new Vector3(0f, 1.75f, 0.8f));
            P(t, "Window", FlatBox(2.9f, 0.5f, 3.0f, 0.4f), tint, new Vector3(0f, 2.0f, 0.8f));
            P(t, "Stripe", FlatBox(2.85f, 0.18f, 3.25f, 0.2f), teal, new Vector3(0f, 1.25f, 0.8f));
            // A-frame crane at the stern
            for (int s = -1; s <= 1; s += 2)
                NpcMesh.Part(t, "AFrame", NpcMesh.Flat(NpcMesh.Cylinder(0.09f, 3.2f, 6), "aframe_leg"), yellow, new Vector3(s * 1.6f, 0.9f, -3.6f), Vector3.one, new Vector3(-22f, 0f, s * -12f));
            P(t, "ABar", NpcMesh.Flat(NpcMesh.Cylinder(0.1f, 2.4f, 6), "aframe_bar"), yellow, new Vector3(-1.2f, 3.8f, -4.8f), new Vector3(0f, 0f, -90f));
            P(t, "Winch", FlatBox(0.6f, 0.5f, 0.6f, 0.4f), yellow, new Vector3(0f, 1.25f, -2.6f));
            P(t, "Buoy", NpcMesh.Flat(NpcMesh.Sphere(6, 8), "cat_buoy"), W("#EE7F2D"), new Vector3(0f, 3.3f, -5.0f));
            // round 3 (GDD §19): the old box ended at 1.8 m (the cabin reaches 2.55 m) – now both hulls, the deck, the
            // cabin and the winch; between the hulls under the deck stays open water
            AddSolid(go, new Vector3(1.3f, 1.5f, 10f), new Vector3(-1.7f, -0.1f, 0f));
            AddSolid(go, new Vector3(1.3f, 1.5f, 10f), new Vector3(1.7f, -0.1f, 0f));
            AddSolid(go, new Vector3(4.6f, 0.3f, 7.5f), new Vector3(0f, 0.82f, -0.5f));
            AddSolid(go, new Vector3(2.8f, 1.6f, 3.2f), new Vector3(0f, 1.75f, 0.8f));
            AddSolid(go, new Vector3(0.6f, 0.5f, 0.6f), new Vector3(0f, 1.22f, -2.6f));
            return go;
        }

        // ------------------------------------------------------------------ Bottom Line (R4): gold mini sub
        public static GameObject MiniSub()
        {
            var go = new GameObject("BottomLine");
            var t = go.transform;
            Material gold = NpcMesh.Mat(LowPoly.Hex("#E8C65A"), 0.75f), glass = NpcMesh.Glass(new Color(0.62f, 0.89f, 0.93f, 0.45f)), ink = W("#1E1B2B"), red = NpcMesh.Glow(LowPoly.Hex("#FF5A5A"), 2f);
            NpcMesh.Part(t, "Body", NpcMesh.Capsule(1.0f, 4.2f, 24), gold, new Vector3(0f, 0f, -2.1f), Vector3.one, new Vector3(90f, 0f, 0f));
            NpcMesh.Part(t, "Dome", NpcMesh.Sphere(12, 20), glass, new Vector3(0f, 0.55f, 1.3f), new Vector3(1.4f, 1.1f, 1.4f), default, false);
            NpcMesh.Part(t, "Fin", NpcMesh.Plate("sub_fin", new[] { new Vector2(0f, 0f), new Vector2(0.9f, 0f), new Vector2(0.3f, 0.8f) }, 0.08f), gold, new Vector3(0f, 0.8f, -1.8f), Vector3.one, new Vector3(0f, 90f, 0f));
            NpcMesh.Part(t, "Prop", NpcMesh.Torus(0.55f, 0.06f, 18, 6), ink, new Vector3(0f, 0f, -2.35f), Vector3.one, new Vector3(90f, 0f, 0f));
            NpcMesh.Part(t, "Valve", NpcMesh.Cylinder(0.12f, 0.35f, 10), W("#C9CED6"), new Vector3(0.95f, 0.1f, -0.4f), Vector3.one, new Vector3(0f, 0f, -90f));
            NpcMesh.Part(t, "Beacon", NpcMesh.Sphere(8, 10), red, new Vector3(0f, 1.0f, -0.6f), Vector3.one * 0.18f, default, false);
            AddSolid(go, new Vector3(2.2f, 2.2f, 5.2f), Vector3.zero);
            return go;
        }

        // ------------------------------------------------------------------ outrigger canoe (M3, Lantern Evening dressing)
        public static GameObject Canoe(int paddlers)
        {
            var go = new GameObject("Canoe");
            var t = go.transform;
            Material wood = W("#8E6C50"), dark = W("#5E4436"), sail = W("#3FA9A0");
            P(t, "Hull", Hull("canoe", 6.5f, 0.75f, 0.45f, 0.35f, 0.35f, 10), wood, Vector3.zero);
            P(t, "Rim", FlatBox(0.8f, 0.06f, 5.2f, 0.4f), dark, new Vector3(0f, 0.36f, 0f));
            P(t, "Float", NpcMesh.Flat(NpcMesh.Capsule(0.16f, 4.2f, 8), "canoe_float"), wood, new Vector3(-2.1f, 0.0f, -2.1f), new Vector3(90f, 0f, 0f));
            for (int i = -1; i <= 1; i += 2)
                P(t, "Boom", NpcMesh.Flat(NpcMesh.Cylinder(0.05f, 2.3f, 6), "canoe_boom"), dark, new Vector3(0f, 0.38f, i * 1.0f), new Vector3(0f, 0f, 90f));
            // wave-pattern stripe (L.8 palette)
            P(t, "Stripe", FlatBox(0.78f, 0.08f, 3.0f, 0.4f), sail, new Vector3(0f, 0.22f, 0.4f));
            for (int i = 0; i < paddlers; i++) Paddler(t, new Vector3(0f, 0.35f, 1.6f - i * 1.6f), i);
            // round 3 (GDD §19): the canoe hull is solid (it had no collider)
            AddSolid(go, new Vector3(0.75f, 0.8f, 6.2f), new Vector3(0f, -0.05f, 0f));
            return go;
        }

        static readonly string[] PaddlerShirts = { "#EE7F2D", "#3FA9A0", "#F2C94C", "#E0607E" };
        static readonly string[] PaddlerSkins = { "#A8724A", "#E8C08A", "#6B4636", "#D6A574" };

        /// A small seated paddler (cosmetic, animated by NpcVehicleAnim).
        static void Paddler(Transform canoe, Vector3 at, int i)
        {
            var p = NpcMesh.Node(canoe, "Paddler" + i, at);
            Material shirt = NpcMesh.Mat(LowPoly.Hex(PaddlerShirts[i % 4]), 0.14f), skin = NpcMesh.Mat(LowPoly.Hex(PaddlerSkins[i % 4]), 0.1f), wood = W("#C9B08E");
            NpcMesh.Part(p, "Body", NpcMesh.Capsule(0.22f, 0.75f, 20), shirt, Vector3.zero, Vector3.one);
            NpcMesh.Part(p, "Head", NpcMesh.Sphere(12, 20), skin, new Vector3(0f, 0.85f, 0f), Vector3.one * 0.4f);
            for (int s = -1; s <= 1; s += 2)
                NpcMesh.Part(p, "Eye", NpcMesh.Sphere(8, 10), NpcMesh.Mat(LowPoly.Hex("#F4EFE6"), 0.3f), new Vector3(s * 0.07f, 0.9f, 0.17f), Vector3.one * 0.08f, default, false);
            var arms = NpcMesh.Node(p, "Arms", new Vector3(0f, 0.55f, 0.1f));
            NpcMesh.Part(arms, "Paddle", NpcMesh.Cylinder(0.025f, 1.5f, 6), wood, new Vector3(0f, -0.2f, 0.25f), Vector3.one, new Vector3(0f, 0f, 75f), false);
            NpcMesh.Part(arms, "Blade", NpcMesh.RoundBox(0.25f, 0.4f, 0.03f, 0.5f, 4), wood, new Vector3(-0.72f, -0.4f, 0.25f), Vector3.one, new Vector3(0f, 0f, 75f), false);
        }

        // ------------------------------------------------------------------ chopper (K.5 prologue) – the Boat module's model
        public static GameObject Chopper(out Transform rotor, out Transform tail)
        {
            var go = new GameObject("WandaChopper");
            rotor = null;
            tail = null;
            try
            {
                var parts = HeliModel.Build(go.transform);
                if (parts != null) { rotor = parts.mainRotor; tail = parts.tailRotor; }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Npc] HeliModel.Build failed – simple chopper used. " + e.Message);
                NpcMesh.Part(go.transform, "Cab", NpcMesh.Sphere(12, 18), W("#E0607E"), new Vector3(0f, 1.3f, 0f), new Vector3(2.2f, 2f, 3.4f));
                rotor = NpcMesh.Node(go.transform, "Rotor", new Vector3(0f, 2.5f, 0f));
                NpcMesh.Part(rotor, "Blade", FlatBox(8f, 0.06f, 0.3f, 0.2f), W("#2E3140"), Vector3.zero);
            }
            return go;
        }

        // ------------------------------------------------------------------ small story set dressing
        /// Horst's folding chair + cooler box + sandwich wrappers (spot „horst_bench“ – props may register their own bench).
        public static GameObject HorstChair()
        {
            var go = new GameObject("HorstChair");
            var t = go.transform;
            Material cloth = W("#3FB8C4"), frame = W("#C9CED6"), cooler = W("#EDE6DA"), lid = W("#E0607E");
            P(t, "Seat", FlatBox(0.6f, 0.08f, 0.55f, 0.3f), cloth, new Vector3(0f, 0.45f, 0f));
            P(t, "Back", FlatBox(0.6f, 0.6f, 0.06f, 0.3f), cloth, new Vector3(0f, 0.78f, -0.3f), new Vector3(-12f, 0f, 0f));
            for (int s = -1; s <= 1; s += 2)
                for (int f = -1; f <= 1; f += 2)
                    P(t, "Leg", NpcMesh.Flat(NpcMesh.Cylinder(0.02f, 0.5f, 5), "chair_leg"), frame, new Vector3(s * 0.27f, 0f, f * 0.22f), new Vector3(f * 10f, 0f, 0f));
            P(t, "Cooler", FlatBox(0.5f, 0.38f, 0.36f, 0.25f), cooler, new Vector3(0.65f, 0.19f, 0.1f));
            P(t, "Lid", FlatBox(0.52f, 0.08f, 0.38f, 0.3f), lid, new Vector3(0.65f, 0.41f, 0.1f));
            P(t, "Wrapper", FlatBox(0.16f, 0.02f, 0.12f, 0.5f), W("#F4EFE6"), new Vector3(-0.5f, 0.01f, 0.3f), new Vector3(0f, 25f, 0f));
            P(t, "Wrapper2", FlatBox(0.14f, 0.02f, 0.1f, 0.5f), W("#F2C94C"), new Vector3(-0.6f, 0.01f, -0.1f), new Vector3(0f, -40f, 0f));
            return go;
        }

        /// Horst's tent next to the hut (A.2 nights).
        public static GameObject HorstTent()
        {
            var go = new GameObject("HorstTent");
            var t = go.transform;
            var outline = new[] { new Vector2(-1.1f, 0f), new Vector2(1.1f, 0f), new Vector2(0f, 1.3f) };
            Material a = W("#E07B39"), b = W("#A8478F");
            P(t, "Front", NpcMesh.Flat(NpcMesh.Plate("tent_tri", outline, 2.2f), "tent"), a, Vector3.zero);
            P(t, "Flag", FlatBox(0.4f, 0.25f, 0.02f, 0.2f), b, new Vector3(0.25f, 1.5f, 1.1f));
            P(t, "Pole", NpcMesh.Flat(NpcMesh.Cylinder(0.02f, 1.6f, 5), "tent_pole"), W("#5E4436"), new Vector3(0f, 0f, 1.1f));
            AddSolid(go, new Vector3(2.2f, 1.3f, 2.2f), new Vector3(0f, 0.6f, 0f));
            return go;
        }

        /// A floating paper lantern (Lantern Evening, L.8): warm glow, small and light.
        public static GameObject Lantern(int colour)
        {
            var go = new GameObject("Lantern");
            var t = go.transform;
            string[] cols = { "#F2C94C", "#EE7F2D", "#F4EFE6", "#E0607E" };
            Color c = LowPoly.Hex(cols[colour & 3]);
            NpcMesh.Part(t, "Raft", NpcMesh.Flat(NpcMesh.Cylinder(0.18f, 0.04f, 8), "lantern_raft"), W("#8E6C50"), Vector3.zero, Vector3.one, default, false);
            NpcMesh.Part(t, "Paper", NpcMesh.Cylinder(0.12f, 0.26f, 10, 0.15f), NpcMesh.Glow(c, 1.5f), new Vector3(0f, 0.04f, 0f), Vector3.one, default, false);
            return go;
        }

        public static void AddSolid(GameObject go, Vector3 size, Vector3 center)
        {
            var bc = go.AddComponent<BoxCollider>();
            bc.size = size;
            bc.center = center;
            PropSolid.Count++;
        }
    }
}
