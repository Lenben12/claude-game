using System.Collections;
using UnityEngine;

namespace Sealebrities
{
    /// Round-3 step 3 screenshots – hut interior and furnishing (Einrichten). Files: r3h_*.png.
    public sealed partial class AutoShot
    {
        IEnumerator ShotsRound3Hut()
        {
            yield return Guard(ShotR3HutInterior(), "r3h_interior");
            yield return Guard(ShotR3HutDecorVariant(), "r3h_decor_variant");
            yield return Guard(ShotR3SurfRack(), "r3h_surfboard_rack");
            if (pc != null) pc.enabled = true;
        }

        static Vector3 HutPt(float x, float y, float z) => PropHut.ToWorld(new Vector3(x, y, z));

        IEnumerator ShotR3HutInterior()
        {
            if (PropHutDecor.Instance == null) Note("WARNING: r3h – no PropHutDecor in the scene");
            SetTime(0.45f);
            yield return EnsureOnLand();
            // wide view from the door, day
            FreeCamera(HutPt(4.05f, 1.62f, 0.9f), HutPt(-3.6f, 0.95f, -0.4f));
            yield return Wait(3f);
            yield return Capture("r3h_interior_day");
            FreeCamera(HutPt(0.5f, 1.5f, 1.5f), HutPt(-1.9f, 0.7f, 4.4f));
            yield return Wait(1f);
            yield return Capture("r3h_bed_corner");
            FreeCamera(HutPt(-0.2f, 1.55f, -0.3f), HutPt(3.2f, 0.8f, -3.8f));
            yield return Wait(1f);
            yield return Capture("r3h_tv_corner");
            FreeCamera(HutPt(-1.3f, 1.6f, -1.1f), HutPt(-4.1f, 0.85f, -3.7f));
            yield return Wait(1f);
            yield return Capture("r3h_workshop");
            FreeCamera(HutPt(1.0f, 1.6f, 1.7f), HutPt(3.2f, 0.75f, 4.6f));
            yield return Wait(1f);
            yield return Capture("r3h_kitchen_filter");
            // night: the hanging lamp, the table lamp and the floor lamp are on
            SetTime(0.9f);
            FreeCamera(HutPt(4.05f, 1.62f, 0.9f), HutPt(-3.6f, 0.95f, -0.4f));
            yield return Wait(4f);
            yield return Capture("r3h_interior_night");
            SetTime(0.45f);
        }

        IEnumerator ShotR3HutDecorVariant()
        {
            var gm = GameManager.I;
            if (gm == null || !gm.InGame) yield break;
            var before = HutDecor.Copy(gm.State.hut);
            SetTime(0.45f);
            yield return EnsureOnLand();
            // a different look through the public API (test grant of the bought items first)
            string[] items = { "palm", "aquarium", "hangchair", "lights", "records", "marlin", "surfboard" };
            foreach (var id in items)
            {
                var it = HutDecor.Item(id);
                if (it != null && it.unlock == HutDecorUnlock.Buy && !gm.State.hut.owned.Contains(id)) gm.State.hut.owned.Add(id);
            }
            bool ok = HutDecor.SetWall(1) & HutDecor.SetFloor(3) & HutDecor.SetRug(1) & HutDecor.SetCurtains(2);
            foreach (var id in items) ok &= HutDecor.SetPlaced(id, true);
            if (!ok) Note("WARNING: r3h_decor_variant – a choice was refused");
            yield return Wait(0.6f);
            FreeCamera(HutPt(4.05f, 1.62f, 0.9f), HutPt(-3.6f, 0.95f, -0.4f));
            yield return Wait(1.5f);
            yield return Capture("r3h_decor_variant");
            FreeCamera(HutPt(-1.6f, 1.6f, 1.6f), HutPt(1.9f, 0.9f, -3.4f));
            yield return Wait(1f);
            yield return Capture("r3h_decor_variant_living");
            HutDecor.DebugReplace(before);
            yield return Wait(0.3f);
        }

        IEnumerator ShotR3SurfRack()
        {
            SetTime(0.4f);
            yield return EnsureOnLand();
            Vector3 c = PropHut.RackCenter;
            if (c == Vector3.zero) { Note("WARNING: r3h – no surfboard rack"); yield break; }
            Vector3 eye = c + new Vector3(2.1f, 1.45f, 2.7f);
            eye.y = Mathf.Max(eye.y, WorldInfo.GroundHeight(eye) + 1.3f);
            FreeCamera(eye, c + new Vector3(0f, 0.85f, 0.2f));
            yield return Wait(2f);
            yield return Capture("r3h_surfboard_rack");
            Vector3 side = c + new Vector3(3.2f, 0.95f, 0.35f);
            side.y = Mathf.Max(side.y, WorldInfo.GroundHeight(side) + 0.9f);
            FreeCamera(side, c + new Vector3(0f, 0.9f, 0.3f));
            yield return Wait(1f);
            yield return Capture("r3h_surfboard_rack_side");
        }
    }
}
