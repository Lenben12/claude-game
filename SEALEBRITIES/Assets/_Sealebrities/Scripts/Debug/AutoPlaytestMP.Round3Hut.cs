using System.Collections;
using UnityEngine;

namespace Sealebrities
{
    /// Round-3 hut furnishing in co-op (GDD §19 „Haus individueller einrichten“): the hut is SHARED – a guest's paint /
    /// rug / decor goes through the host (CoreCommands.HutKey) and comes back in the snapshot; a host change reaches
    /// the guest. Hooked at the end of StepRound3Core (11h).
    public sealed partial class AutoPlaytestMP
    {
        static bool HutSameColor(Color a, Color b) => Mathf.Abs(a.r - b.r) < 0.01f && Mathf.Abs(a.g - b.g) < 0.01f && Mathf.Abs(a.b - b.b) < 0.01f;

        IEnumerator StepRound3HutDecor()
        {
            yield return Sync("S31a", 60f);
            var before = HutDecor.Copy(S.hut);
            if (!isHost)
            {
                bool ok = HutDecor.SetWall(4) & HutDecor.SetRug(3) & HutDecor.SetPlaced("palm", true);
                Check(ok && HutDecor.Wall == 4 && HutDecor.Rug == 3 && HutDecor.IsPlaced("palm"), "guest: wall " + HutDecor.Walls[4].Name + ", rug " + HutDecor.Rugs[3].Name + ", palm (optimistic)");
                SendVal("hutdecor", "1");
                yield return Wait(2.5f);
                Check(HutDecor.Wall == 4 && HutDecor.Rug == 3 && HutDecor.IsPlaced("palm"), "guest: the choice survived the host's snapshots (the host applied it)");
                var d = PropHutDecor.Instance;
                Check(d != null && HutSameColor(d.WallColor, HutDecor.Walls[4].a) && d.ActiveRug == 3 && d.ItemShown("palm"), "guest: the hut shows it");
                yield return WaitVal("hutfloor", 40f);
                yield return WaitUntil(() => HutDecor.Floor == 1, 8f);
                yield return Wait(0.5f);
                d = PropHutDecor.Instance;
                Check(HutDecor.Floor == 1 && d != null && HutSameColor(d.FloorColor, HutDecor.Floors[1].a), "guest: the host's new floor (" + HutDecor.Floors[1].Name + ") arrived");
            }
            else
            {
                yield return WaitVal("hutdecor", 40f);
                yield return WaitUntil(() => HutDecor.Wall == 4 && HutDecor.Rug == 3 && HutDecor.IsPlaced("palm"), 8f);
                Check(HutDecor.Wall == 4 && HutDecor.Rug == 3 && HutDecor.IsPlaced("palm"), "host: the guest's paint / rug / palm reached the shared hut");
                yield return Wait(0.6f);
                var d = PropHutDecor.Instance;
                Check(d != null && HutSameColor(d.WallColor, HutDecor.Walls[4].a) && d.ActiveRug == 3 && d.ItemShown("palm"), "host: the hut shows the guest's choice");
                if (d != null)
                {
                    yield return Stand(PropHut.ToWorld(new Vector3(3.3f, 0.06f, 0.9f)), 270f);
                    yield return AimAt(PropHut.ToWorld(new Vector3(-3.5f, 0.9f, -0.5f)));
                    yield return Shot("host_S31_hut_decor");
                }
                HutDecor.SetFloor(1);
                SendVal("hutfloor", "1");
            }
            yield return Sync("S31b", 60f);
            if (isHost) HutDecor.DebugReplace(before);   // host-authoritative restore, reaches the guest by snapshot
            yield return Sync("S31c", 60f);
        }
    }
}
