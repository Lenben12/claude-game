using UnityEngine;

namespace Sealebrities
{
    /// Models of the npc module's quest items and story objects (smooth, ≤ ~1,500 triangles each, L.13 item look).
    /// Carried items use the carry id "q:&lt;item&gt;:&lt;n&gt;" (Carry.Current); CarryModels.Build(Carry.Current) returns the
    /// model for the player's hands / co-op avatars (player / net modules may call it). Origin = grip point, +Z forward.
    public static class CarryModels
    {
        /// Set true by the Player module once it draws carried quest items itself (the npc module's fallback view hides).
        public static bool PlayerDrawsCarry;

        static Material M(string hex, float s = 0.25f) => NpcMesh.Mat(LowPoly.Hex(hex), s);

        /// Item part of a carry id ("q:drum:2" → "drum"); null for other ids (salvage, finds).
        public static string ItemOf(string carryId)
        {
            if (string.IsNullOrEmpty(carryId) || !carryId.StartsWith("q:", System.StringComparison.Ordinal)) return null;
            int last = carryId.LastIndexOf(':');
            return last > 2 ? carryId.Substring(2, last - 2) : carryId.Substring(2);
        }

        /// Localized name of a carried quest item (for prompts / notifications).
        public static string NameOf(string item)
        {
            switch (item)
            {
                case "sunglasses": return Loc.Tr("Horsts Sonnenbrille", "Horst's sunglasses");
                case "drum": return Loc.Tr("Fass", "Drum");
                case "paddle": return Loc.Tr("Gus' Paddel", "Gus's paddle");
                case "kite": return Loc.Tr("Horsts Drachen", "Horst's kite");
                case "tape": return Loc.Tr("Das letzte Band", "The last tape");
                case "selfie_stick": return Loc.Tr("Goldener Selfie-Stick", "Golden selfie stick");
                case "coconut": return Loc.Tr("Kokosnuss", "Coconut");
                case "grilled_fish": return Loc.Tr("Grillfisch", "Grilled fish");
                case "recorder": return Loc.Tr("Tonbandgerät", "Tape recorder");
                default: return item ?? "";
            }
        }

        public static GameObject Build(string carryIdOrItem)
        {
            string item = ItemOf(carryIdOrItem) ?? carryIdOrItem ?? "";
            var go = new GameObject("QuestItem_" + item);
            var t = go.transform;
            switch (item)
            {
                case "sunglasses":
                    for (int s = -1; s <= 1; s += 2)
                    {
                        NpcMesh.Part(t, "Lens", NpcMesh.Plate("sg_lens", NpcMesh.Ellipse(0.032f, 0.026f, 18), 0.008f), M("#3FA9B5", 0.7f), new Vector3(s * 0.04f, 0f, 0f));
                        NpcMesh.Part(t, "Rim", NpcMesh.Torus(0.03f, 0.005f, 18, 5), M("#1E1B2B", 0.5f), new Vector3(s * 0.04f, 0f, 0f), Vector3.one, new Vector3(90f, 0f, 0f));
                        NpcMesh.Part(t, "Arm", NpcMesh.Capsule(0.004f, 0.11f, 6), M("#1E1B2B", 0.5f), new Vector3(s * 0.07f, 0f, 0f), Vector3.one, new Vector3(90f, 0f, 0f));
                    }
                    break;
                case "drum":
                    NpcMesh.Part(t, "Drum", NpcMesh.Cylinder(0.28f, 0.85f, 20), M("#C94F3D", 0.35f), new Vector3(0f, -0.42f, 0f));
                    for (int i = 0; i < 2; i++) NpcMesh.Part(t, "Ring", NpcMesh.Torus(0.285f, 0.018f, 20, 5), M("#7A4448", 0.3f), new Vector3(0f, -0.2f - i * 0.45f, 0f));
                    NpcMesh.Part(t, "Cap", NpcMesh.Cylinder(0.05f, 0.04f, 10), M("#EDE6DA", 0.3f), new Vector3(0.12f, 0.43f, 0f));
                    break;
                case "peg":
                    NpcMesh.Part(t, "Peg", NpcMesh.Cylinder(0.05f, 0.55f, 14, 0.06f), M("#8E6C50", 0.2f), new Vector3(0f, -0.2f, 0f));
                    NpcMesh.Part(t, "Tip", NpcMesh.Cone(0.05f, 0.12f, 14), M("#8E6C50", 0.2f), new Vector3(0f, -0.2f, 0f), Vector3.one, new Vector3(180f, 0f, 0f));
                    for (int i = 0; i < 3; i++) NpcMesh.Part(t, "Carve", NpcMesh.Torus(0.052f, 0.008f, 14, 4), M(i % 2 == 0 ? "#3FA9A0" : "#EE7F2D", 0.2f), new Vector3(0f, 0.05f + i * 0.08f, 0f));
                    break;
                case "driftwood":
                {
                    var path = new[] { new Vector3(-0.45f, 0f, 0f), new Vector3(-0.15f, 0.04f, 0.06f), new Vector3(0.15f, -0.02f, -0.04f), new Vector3(0.42f, 0.05f, 0.08f) };
                    NpcMesh.Part(t, "Wood", NpcMesh.Sweep("driftwood", path, 0.06f, 0.04f, 9), M("#B9B2A8", 0.1f), Vector3.zero);
                    NpcMesh.Part(t, "Branch", NpcMesh.Capsule(0.025f, 0.22f, 8), M("#B9B2A8", 0.1f), new Vector3(0.05f, 0.02f, 0f), Vector3.one, new Vector3(0f, 0f, -55f));
                    break;
                }
                case "paddle":
                    NpcMesh.Part(t, "Shaft", NpcMesh.Cylinder(0.022f, 1.5f, 10), M("#8E6C50", 0.25f), new Vector3(0f, -0.75f, 0f));
                    NpcMesh.Part(t, "Blade", NpcMesh.RoundBox(0.2f, 0.42f, 0.03f, 0.6f, 6), M("#C9B08E", 0.25f), new Vector3(0f, -0.9f, 0f));
                    NpcMesh.Part(t, "Band", NpcMesh.Torus(0.025f, 0.008f, 10, 4), M("#3FA9A0", 0.2f), new Vector3(0f, 0.3f, 0f));
                    break;
                case "ghostnet":
                    for (int i = 0; i < 6; i++)
                    {
                        float a = i * 1.05f;
                        var p = new[] { new Vector3(Mathf.Cos(a) * 0.5f, 0f, Mathf.Sin(a) * 0.5f), new Vector3(Mathf.Cos(a + 1f) * 0.3f, 0.35f, Mathf.Sin(a + 1f) * 0.3f), new Vector3(Mathf.Cos(a + 2.1f) * 0.55f, 0.15f, Mathf.Sin(a + 2.1f) * 0.55f) };
                        NpcMesh.Part(t, "Strand", NpcMesh.Sweep("ghostnet" + i, p, 0.02f, 0.015f, 6), M("#6E9445", 0.1f), Vector3.zero);
                    }
                    NpcMesh.Part(t, "Float", NpcMesh.Sphere(8, 12), M("#EE7F2D", 0.3f), new Vector3(0.2f, 0.4f, 0f), Vector3.one * 0.14f);
                    break;
                case "seagrape":
                    for (int i = 0; i < 9; i++)
                        NpcMesh.Part(t, "Grape", NpcMesh.Sphere(8, 12), M("#7FA243", 0.45f), new Vector3(Mathf.Sin(i * 2.4f) * 0.04f, i * 0.025f, Mathf.Cos(i * 2.4f) * 0.04f), Vector3.one * 0.035f);
                    NpcMesh.Part(t, "Stem", NpcMesh.Cylinder(0.006f, 0.25f, 6), M("#4E6F2E", 0.2f), Vector3.zero);
                    break;
                case "kite":
                    NpcMesh.Part(t, "Sail", NpcMesh.Plate("kite", new[] { new Vector2(0f, 0.32f), new Vector2(0.2f, 0f), new Vector2(0f, -0.4f), new Vector2(-0.2f, 0f) }, 0.01f), M("#E0607E", 0.2f), Vector3.zero);
                    NpcMesh.Part(t, "Cross", NpcMesh.Capsule(0.008f, 0.4f, 6), M("#8E6C50", 0.2f), new Vector3(-0.2f, 0f, 0.008f), Vector3.one, new Vector3(0f, 0f, -90f));
                    for (int i = 0; i < 3; i++) NpcMesh.Part(t, "Bow", NpcMesh.Sphere(6, 8), M(i % 2 == 0 ? "#F2C94C" : "#3FB8C4", 0.2f), new Vector3(0f, -0.48f - i * 0.1f, 0f), new Vector3(0.05f, 0.03f, 0.02f));
                    break;
                case "liochest":
                case "chest":
                    NpcMesh.Part(t, "Box", NpcMesh.RoundBox(0.6f, 0.36f, 0.4f, 0.2f, 6), M("#8E6C50", 0.2f), new Vector3(0f, 0.18f, 0f));
                    NpcMesh.Part(t, "Lid", NpcMesh.RoundBox(0.62f, 0.14f, 0.42f, 0.5f, 6), M("#6B4A36", 0.2f), new Vector3(0f, 0.42f, 0f));
                    NpcMesh.Part(t, "Lock", NpcMesh.RoundBox(0.08f, 0.1f, 0.03f, 0.4f, 4), M("#E8C65A", 0.8f), new Vector3(0f, 0.32f, 0.21f));
                    NpcMesh.Part(t, "X", NpcMesh.Plate("crayon_x", new[] { new Vector2(-0.1f, -0.08f), new Vector2(0.1f, 0.08f), new Vector2(0.08f, 0.1f), new Vector2(-0.12f, -0.06f) }, 0.01f), M("#C94F3D", 0.1f), new Vector3(0f, 0.5f, 0f), Vector3.one, new Vector3(90f, 0f, 0f));
                    break;
                case "tape":
                    NpcMesh.Part(t, "Cassette", NpcMesh.RoundBox(0.11f, 0.07f, 0.015f, 0.3f, 5), M("#2E3140", 0.4f), Vector3.zero);
                    for (int s = -1; s <= 1; s += 2) NpcMesh.Part(t, "Reel", NpcMesh.Cylinder(0.014f, 0.004f, 14), M("#F4EFE6", 0.3f), new Vector3(s * 0.027f, 0f, 0.008f), Vector3.one, new Vector3(90f, 0f, 0f));
                    NpcMesh.Part(t, "Label", NpcMesh.RoundBox(0.08f, 0.02f, 0.002f, 0.2f, 3), M("#F2C94C", 0.2f), new Vector3(0f, 0.022f, 0.009f));
                    break;
                case "selfie_stick":
                    NpcMesh.Part(t, "Pole", NpcMesh.Cylinder(0.012f, 0.8f, 10), M("#E8C65A", 0.85f), new Vector3(0f, -0.3f, 0f));
                    NpcMesh.Part(t, "Phone", NpcMesh.RoundBox(0.075f, 0.14f, 0.012f, 0.4f, 5), M("#1E1B2B", 0.6f), new Vector3(0f, 0.55f, 0f));
                    break;
                case "coconut":
                    NpcMesh.Part(t, "Nut", NpcMesh.Sphere(12, 18), M("#6B4A36", 0.15f), Vector3.zero, new Vector3(0.18f, 0.2f, 0.18f));
                    for (int i = 0; i < 3; i++) NpcMesh.Part(t, "Eye", NpcMesh.Sphere(6, 8), M("#3A2C2A", 0.1f), new Vector3(Mathf.Cos(i * 2.1f) * 0.025f, 0.095f, Mathf.Sin(i * 2.1f) * 0.025f), Vector3.one * 0.018f);
                    break;
                case "grilled_fish":
                    NpcMesh.Part(t, "Fish", NpcMesh.Sphere(12, 18), M("#C9A36E", 0.35f), Vector3.zero, new Vector3(0.07f, 0.09f, 0.28f));
                    NpcMesh.Part(t, "Tail", NpcMesh.Plate("gf_tail", new[] { new Vector2(0f, 0f), new Vector2(0.06f, 0.06f), new Vector2(0.06f, -0.06f) }, 0.01f), M("#A8724A", 0.3f), new Vector3(0f, 0f, -0.15f), Vector3.one, new Vector3(0f, 90f, 0f));
                    for (int i = 0; i < 3; i++) NpcMesh.Part(t, "Grill", NpcMesh.Capsule(0.006f, 0.1f, 6), M("#3A2C2A", 0.1f), new Vector3(0.033f, 0f, -0.06f + i * 0.06f), Vector3.one, new Vector3(0f, 0f, 90f));
                    NpcMesh.Part(t, "Stick", NpcMesh.Cylinder(0.006f, 0.3f, 6), M("#C9B08E", 0.2f), new Vector3(0f, -0.25f, 0f));
                    break;
                case "recorder":
                    NpcMesh.Part(t, "Box", NpcMesh.RoundBox(0.24f, 0.14f, 0.08f, 0.35f, 6), M("#2E3140", 0.35f), Vector3.zero);
                    for (int s = -1; s <= 1; s += 2) NpcMesh.Part(t, "Reel", NpcMesh.Cylinder(0.028f, 0.008f, 14), M("#F4EFE6", 0.3f), new Vector3(s * 0.055f, 0.01f, 0.041f), Vector3.one, new Vector3(90f, 0f, 0f));
                    NpcMesh.Part(t, "Weed", NpcMesh.Sweep("rec_weed", new[] { new Vector3(-0.1f, 0.07f, 0f), new Vector3(0f, 0.11f, 0.02f), new Vector3(0.1f, 0.06f, -0.02f) }, 0.01f, 0.006f, 6), M("#7E9A3A", 0.2f), Vector3.zero);
                    break;
                case "valve":
                    NpcMesh.Part(t, "Base", NpcMesh.Cylinder(0.03f, 0.03f, 12), M("#2E3140", 0.4f), Vector3.zero);
                    NpcMesh.Part(t, "Glass", NpcMesh.Capsule(0.025f, 0.09f, 12), NpcMesh.Glass(new Color(0.85f, 0.92f, 0.95f, 0.55f)), new Vector3(0f, 0.03f, 0f));
                    NpcMesh.Part(t, "Glow", NpcMesh.Sphere(6, 8), NpcMesh.Glow(LowPoly.Hex("#EE7F2D"), 1.2f), new Vector3(0f, 0.07f, 0f), Vector3.one * 0.015f);
                    break;
                case "coil":
                    for (int i = 0; i < 4; i++) NpcMesh.Part(t, "Turn", NpcMesh.Torus(0.05f, 0.01f, 18, 6), M("#C46A3A", 0.7f), new Vector3(0f, i * 0.022f, 0f));
                    break;
                case "stake":
                    NpcMesh.Part(t, "Pole", NpcMesh.Cylinder(0.035f, 1.2f, 10), M("#F2C94C", 0.4f), Vector3.zero);
                    NpcMesh.Part(t, "Flag", NpcMesh.RoundBox(0.25f, 0.16f, 0.01f, 0.2f, 3), M("#3FA9B5", 0.3f), new Vector3(0.13f, 1.1f, 0f));
                    NpcMesh.Part(t, "Bottle", NpcMesh.Capsule(0.03f, 0.12f, 10), NpcMesh.Glass(new Color(0.75f, 0.92f, 0.95f, 0.5f)), new Vector3(0f, 0.75f, 0.05f));
                    break;
                case "fin":
                    NpcMesh.Part(t, "Fin", NpcMesh.Plate("gold_fin", new[] { new Vector2(-0.12f, -0.1f), new Vector2(0.12f, -0.1f), new Vector2(0.2f, 0.45f), new Vector2(0.05f, 0.42f), new Vector2(0f, 0.46f), new Vector2(-0.05f, 0.42f), new Vector2(-0.2f, 0.45f) }, 0.035f),
                        NpcMesh.Mat(LowPoly.Hex("#E8C65A"), 0.85f), Vector3.zero, Vector3.one, new Vector3(-70f, 0f, 0f));
                    NpcMesh.Part(t, "Pocket", NpcMesh.RoundBox(0.2f, 0.08f, 0.22f, 0.5f, 5), NpcMesh.Mat(LowPoly.Hex("#C9A84A"), 0.8f), new Vector3(0f, 0.03f, -0.08f));
                    break;
                case "radio":
                    NpcMesh.Part(t, "Box", NpcMesh.RoundBox(0.42f, 0.26f, 0.2f, 0.25f, 6), M("#6E6F7E", 0.3f), new Vector3(0f, 0.13f, 0f));
                    NpcMesh.Part(t, "Grill", NpcMesh.RoundBox(0.18f, 0.16f, 0.01f, 0.3f, 4), M("#2E3140", 0.2f), new Vector3(-0.09f, 0.14f, 0.1f));
                    for (int i = 0; i < 2; i++) NpcMesh.Part(t, "Knob", NpcMesh.Cylinder(0.025f, 0.02f, 12), M("#EDE6DA", 0.3f), new Vector3(0.09f + i * 0.07f, 0.14f, 0.1f), Vector3.one, new Vector3(90f, 0f, 0f));
                    NpcMesh.Part(t, "Antenna", NpcMesh.Cylinder(0.006f, 0.4f, 6), M("#C9CED6", 0.6f), new Vector3(0.17f, 0.25f, -0.05f), Vector3.one, new Vector3(0f, 0f, -20f));
                    NpcMesh.Part(t, "Barnacle", NpcMesh.Sphere(6, 8), M("#EDE6DA", 0.1f), new Vector3(-0.15f, 0.26f, 0.04f), Vector3.one * 0.04f);
                    break;
                case "shard":
                    NpcMesh.Part(t, "Shard", NpcMesh.Plate("lens_shard", new[] { new Vector2(0f, 0.16f), new Vector2(0.09f, 0.02f), new Vector2(0.03f, -0.12f), new Vector2(-0.08f, -0.03f) }, 0.02f),
                        NpcMesh.Glow(LowPoly.Hex("#9FE4EC"), 0.6f), new Vector3(0f, 0.08f, 0f), Vector3.one, new Vector3(-15f, 20f, 0f));
                    break;
                case "lamp":
                    NpcMesh.Part(t, "Ring", NpcMesh.Torus(0.35f, 0.05f, 24, 8), M("#C9CED6", 0.7f), Vector3.zero, Vector3.one, new Vector3(90f, 0f, 0f));
                    NpcMesh.Part(t, "Holder", NpcMesh.Cylinder(0.06f, 0.4f, 10), M("#2E3140", 0.5f), new Vector3(0f, -0.75f, 0f));
                    break;
                case "lantern_stand":
                    NpcMesh.Part(t, "Table", NpcMesh.Flat(NpcMesh.RoundBox(0.9f, 0.08f, 0.5f, 0.2f, 2), "lstand_top"), NpcMesh.World(LowPoly.Hex("#8E6C50")), new Vector3(0f, 0.75f, 0f));
                    NpcMesh.Part(t, "Leg", NpcMesh.Flat(NpcMesh.Cylinder(0.06f, 0.75f, 6), "lstand_leg"), NpcMesh.World(LowPoly.Hex("#5E4436")), Vector3.zero);
                    for (int i = 0; i < 3; i++)
                        NpcMesh.Part(t, "Lantern", NpcMesh.Cylinder(0.1f, 0.22f, 10, 0.13f), NpcMesh.Glow(LowPoly.Hex(i == 1 ? "#EE7F2D" : "#F2C94C"), 1.3f), new Vector3(-0.28f + i * 0.28f, 0.8f, 0f));
                    break;
                case "food:coconut_curry":
                    NpcMesh.Part(t, "Bowl", NpcMesh.Dome(8, 18), M("#EDE6DA", 0.3f), Vector3.zero, Vector3.one * 0.22f, new Vector3(180f, 0f, 0f));
                    NpcMesh.Part(t, "Curry", NpcMesh.Cylinder(0.1f, 0.01f, 16), M("#E3A13A", 0.3f), new Vector3(0f, -0.005f, 0f));
                    break;
                case "food:sea_grape_salad":
                    NpcMesh.Part(t, "Bowl", NpcMesh.Dome(8, 18), M("#3FA9A0", 0.3f), Vector3.zero, Vector3.one * 0.22f, new Vector3(180f, 0f, 0f));
                    for (int i = 0; i < 7; i++) NpcMesh.Part(t, "Grape", NpcMesh.Sphere(6, 8), M("#7FA243", 0.5f), new Vector3(Mathf.Sin(i * 2.4f) * 0.05f, 0.01f, Mathf.Cos(i * 2.4f) * 0.05f), Vector3.one * 0.03f);
                    break;
                case "food:ginger_fizz":
                    NpcMesh.Part(t, "Bottle", NpcMesh.Capsule(0.035f, 0.2f, 12), NpcMesh.Glass(new Color(0.95f, 0.85f, 0.45f, 0.75f)), new Vector3(0f, -0.1f, 0f));
                    NpcMesh.Part(t, "Cap", NpcMesh.Cylinder(0.018f, 0.02f, 10), M("#E0607E", 0.4f), new Vector3(0f, 0.1f, 0f));
                    break;
                case "pallet":
                    NpcMesh.Part(t, "Pallet", NpcMesh.Flat(NpcMesh.RoundBox(1.2f, 0.14f, 0.9f, 0.1f, 2), "pallet"), NpcMesh.World(LowPoly.Hex("#C9B08E")), new Vector3(0f, 0.07f, 0f));
                    NpcMesh.Part(t, "Sign", NpcMesh.Flat(NpcMesh.RoundBox(0.5f, 0.3f, 0.04f, 0.2f, 2), "pallet_sign"), NpcMesh.World(LowPoly.Hex("#F2C94C")), new Vector3(0f, 0.9f, -0.4f));
                    NpcMesh.Part(t, "Post", NpcMesh.Flat(NpcMesh.Cylinder(0.03f, 0.8f, 5), "pallet_post"), NpcMesh.World(LowPoly.Hex("#5E4436")), new Vector3(0f, 0f, -0.42f));
                    break;
                case "valve_sub":
                    NpcMesh.Part(t, "Wheel", NpcMesh.Torus(0.12f, 0.025f, 18, 6), M("#E0607E", 0.5f), Vector3.zero, Vector3.one, new Vector3(0f, 0f, 90f));
                    break;
                default:
                    NpcMesh.Part(t, "Thing", NpcMesh.RoundBox(0.2f, 0.2f, 0.2f, 0.4f, 5), M("#F2C94C", 0.4f), Vector3.zero);
                    break;
            }
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        /// The tall 1987 TV-station antenna mast in the trench (B.2 M4) with a blinking red light.
        public static GameObject Mast()
        {
            var go = new GameObject("AntennaMast");
            var t = go.transform;
            Material steel = NpcMesh.World(LowPoly.Hex("#6E6F86")), rust = NpcMesh.World(LowPoly.Hex("#A0583A"));
            for (int i = 0; i < 3; i++)
            {
                float a = i * 120f * Mathf.Deg2Rad;
                NpcMesh.Part(t, "Leg", NpcMesh.Flat(NpcMesh.Cylinder(0.12f, 11f, 6, 0.18f), "mast_leg"), i == 1 ? rust : steel, new Vector3(Mathf.Cos(a) * 0.9f, 0f, Mathf.Sin(a) * 0.9f), Vector3.one, new Vector3(Mathf.Sin(a) * -4f, 0f, Mathf.Cos(a) * 4f));
            }
            for (int k = 0; k < 5; k++)
                NpcMesh.Part(t, "Ring", NpcMesh.Torus(0.85f - k * 0.1f, 0.06f, 12, 4), steel, new Vector3(0f, 1.5f + k * 2f, 0f));
            NpcMesh.Part(t, "Dish", NpcMesh.Flat(NpcMesh.Dome(6, 14), "mast_dish"), NpcMesh.World(LowPoly.Hex("#C9CED6")), new Vector3(0.4f, 9.5f, 0.6f), Vector3.one * 1.8f, new Vector3(-60f, 30f, 0f));
            NpcMesh.Part(t, "Light", NpcMesh.Sphere(8, 10), NpcMesh.Glow(LowPoly.Hex("#FF5A5A"), 3f), new Vector3(0f, 11.2f, 0f), Vector3.one * 0.35f);
            NpcMesh.Part(t, "Sign", NpcMesh.Flat(NpcMesh.RoundBox(1.2f, 0.5f, 0.06f, 0.2f, 2), "mast_sign"), NpcMesh.World(LowPoly.Hex("#EDE6DA")), new Vector3(0f, 3.2f, 0.95f));
            return go;
        }
    }
}
