using System.Collections;

namespace Sealebrities
{
    /// Round-3 step 6 – the steps of the shop / loading screen / studio / cookbook parts (80–83, own partial files:
    /// AutoPlaytest.Round3UIShop.cs, .Round3UILoading.cs, .Round3UIStudio.cs).
    public sealed partial class AutoPlaytest
    {
        IEnumerator RunRound3UISubSteps()
        {
            yield return RunStep("80 Chopper-Shop catalogue (Q32): category chips, a big unique 3D picture per item, stat bars / price / ETA / order on every card, detail page + Back, deal of the day, order button = old buy path", StepR3UIShop, 150f);
            yield return RunStep("81 Loading screen (Q35): animated 3D reef with the own diver, fish, light rays, bubbles, tips + ocean facts, torn down on hide", StepR3UILoading, 60f);
            yield return RunStep("82 FinTube Studio (Q33): pick + order the clips (the intro leads title / collage / top species), statistics (views per day, follower growth, best videos), top comments", StepR3UIStudio, 120f);
            yield return RunStep("83 Cookbook app (Q34): every real recipe from catch / fruit / coconuts incl. the hermits' curry (locked until learned), have / missing, where to cook", StepR3UICookbook, 90f);
        }
    }
}
