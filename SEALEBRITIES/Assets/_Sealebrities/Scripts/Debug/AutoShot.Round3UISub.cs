using System.Collections;

namespace Sealebrities
{
    /// Round-3 step 6 – screenshots of the shop / loading screen / studio / cookbook parts (own partial files:
    /// AutoShot.Round3UIShop.cs, .Round3UILoading.cs, .Round3UIStudio.cs).
    public sealed partial class AutoShot
    {
        IEnumerator ShotsRound3UISub()
        {
            yield return Guard(ShotsRound3UIShop(), "round-3 ui shop shots");
            yield return Guard(ShotsRound3UILoading(), "round-3 loading screen shots");
            yield return Guard(ShotsRound3UIStudio(), "round-3 ui studio shots");
        }
    }
}
