using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Sealebrities
{
    /// Round 3 (GDD §20 Q20 "Schätze & Wracks erkunden"): sunken treasure chests. Opening one ("E – Truhe öffnen",
    /// 2.2 s; 1.2 s when a stick / spear is used as a crowbar) puts its loot into the OPENER's backpack – valuables
    /// (ItemCategory.Valuable, no slot limit, sold in the fish market app's „Funde“ tab), whetstones, snacks, sometimes
    /// a spear. Each chest opens once per save: shared story flag "chest:&lt;id&gt;" (co-op guests forward it through
    /// StoryFlags; every machine shows the lid open). Loot is deterministic per save seed + chest.
    public static class SeaTreasure
    {
        public const string ChestFlagPrefix = "chest:", FoundFlagPrefix = "found:";
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly List<KeyValuePair<string, int>> scratch = new List<KeyValuePair<string, int>>(8);

        public static bool IsOpened(string chestId) => StoryFlags.Has(ChestFlagPrefix + chestId);

        /// Loot of a chest (tier 1 lagoon / house reef … 4 the monster's sinkhole; pirate = the pirate wreck).
        public static void Roll(string chestId, int tier, bool pirate, List<KeyValuePair<string, int>> into)
        {
            into.Clear();
            var gm = GameManager.I;
            int seed = gm != null && gm.State != null ? gm.State.saveSeed : 1;
            var rng = new System.Random(unchecked(seed * 7349 + FindsDatabase.StableHash(chestId)));
            void Add(string id, int n) { if (n > 0) into.Add(new KeyValuePair<string, int>(id, n)); }
            bool Chance(double p) => rng.NextDouble() < p;
            switch (Mathf.Clamp(tier, 1, 4))
            {
                case 1:
                    Add(ItemIds.Coins, 2 + rng.Next(3));
                    if (Chance(0.5)) Add(ItemIds.Pearl, 1);
                    if (Chance(0.45)) Add("krill_bar", 1);
                    if (Chance(0.35)) Add(ItemIds.Stone, 1);
                    break;
                case 2:
                    Add(ItemIds.Coins, 3 + rng.Next(4));
                    Add(Chance(0.5) ? ItemIds.Compass : ItemIds.Spyglass, 1);
                    if (Chance(0.5)) Add(ItemIds.Pearl, 1);
                    Add(ItemIds.Stone, 1 + rng.Next(2));
                    if (Chance(0.4)) Add("glow_soda", 1);
                    break;
                case 3:
                    Add(ItemIds.Coins, 5 + rng.Next(5));
                    Add(ItemIds.Gem, 1);
                    if (Chance(0.5)) Add(ItemIds.Goblet, 1);
                    if (Chance(0.25)) Add(ItemIds.Figurine, 1);
                    if (Chance(0.5)) Add(ItemIds.Spyglass, 1);
                    Add(ItemIds.Stone, 2);
                    if (Chance(0.5)) Add("glow_soda", 1);
                    break;
                default:
                    Add(ItemIds.FossilTooth, 1);
                    Add(ItemIds.Gem, 1 + rng.Next(2));
                    Add(ItemIds.Coins, 6 + rng.Next(5));
                    if (Chance(0.6)) Add(ItemIds.Figurine, 1);
                    break;
            }
            if (pirate)
            {
                Add(ItemIds.Goblet, 1);
                if (Chance(0.35)) Add(ItemIds.SpearT1, 1);
            }
        }

        /// The local player opened a chest: loot into the backpack + one toast listing it.
        public static void GiveLoot(string chestId, int tier, bool pirate)
        {
            Roll(chestId, tier, pirate, scratch);
            var sb = new StringBuilder(64);
            long worth = 0;
            for (int i = 0; i < scratch.Count; i++)
            {
                var d = ItemDatabase.Get(scratch[i].Key);
                if (d == null) continue;
                int got = Backpack.Add(d.id, scratch[i].Value, false);
                if (got <= 0) continue;
                worth += d.value * got;
                if (sb.Length > 0) sb.Append(" · ");
                sb.Append(d.Name);
                if (got > 1) sb.Append(" ×").Append(got.ToString(Inv));
            }
            GameEvents.RaiseNotify(NotifyKind.Collectible, Loc.Tr("Schatz gefunden!", "Treasure found!") + (worth > 0 ? "  " + Loc.Money(worth) : ""), sb.ToString());
            FXSfx.Play(FXSound.Fanfare, 0.6f);
            Opened++;
        }

        /// Chests opened by the local player this session (tests).
        public static int Opened;

        // ------------------------------------------------------------------ selling valuables (fish market app)
        /// Valuables in the local player's backpack: total sale value.
        public static long ValuablesWorth
        {
            get
            {
                long v = 0;
                var l = Backpack.Pouch;
                for (int i = 0; i < l.Count; i++)
                {
                    var st = l[i];
                    var d = st != null ? st.Def : null;
                    if (d != null && d.category == ItemCategory.Valuable && st.count > 0) v += d.value * st.count;
                }
                return v;
            }
        }

        /// Sells n of a valuable (money is shared: co-op guests' sale goes through GameManager.AddMoney → host).
        public static long Sell(string itemId, int n)
        {
            var d = ItemDatabase.Get(itemId);
            var gm = GameManager.I;
            if (d == null || gm == null || d.category != ItemCategory.Valuable || n <= 0) return 0;
            n = Mathf.Min(n, Backpack.Count(itemId));
            if (n <= 0 || !Backpack.Remove(itemId, n)) return 0;
            long money = d.value * n;
            gm.AddMoney(money);
            if (Net.IsClient) { gm.State.money += money; GameEvents.RaiseMoneyChanged(gm.State.money); }   // optimistic
            GameEvents.RaiseNotify(NotifyKind.Money, Loc.Tr("Verkauft: ", "Sold: ") + d.Name, "+" + Loc.Money(money));
            return money;
        }

        public static long SellAll()
        {
            long sum = 0;
            var l = Backpack.Pouch;
            for (int i = l.Count - 1; i >= 0; i--)
            {
                var st = l[i];
                var d = st != null ? st.Def : null;
                if (d == null || d.category != ItemCategory.Valuable || st.count <= 0) continue;
                sum += Sell(d.id, st.count);
                if (i > l.Count) i = l.Count;
            }
            return sum;
        }
    }

    /// One sunken treasure chest (SeaContent places them). Lid opens for everybody once the shared flag is set.
    public sealed class SeaChest : PropHold
    {
        public string chestId;
        public int tier = 1;
        public bool pirate;
        public Transform lid;
        public GameObject loot, glint;
        float lidAngle, poll;
        bool open;

        void Awake() { seconds = 2.2f; range = 3.4f; }

        bool Tool => Hands.IsHeld(HoldItem.Spear) || Hands.IsHeld(HoldItem.Stick);
        protected override string VerbDe => Tool ? "Truhe aufhebeln" : "Truhe öffnen";
        protected override string VerbEn => Tool ? "Pry the chest open" : "Open the chest";
        protected override bool Available() => GameManager.I != null && GameManager.I.InGame && !SeaTreasure.IsOpened(chestId);
        protected override void OnStart() { seconds = Tool ? 1.2f : 2.2f; }

        protected override void Complete()
        {
            if (SeaTreasure.IsOpened(chestId)) return;
            StoryFlags.Set(SeaTreasure.ChestFlagPrefix + chestId);
            SeaTreasure.GiveLoot(chestId, tier, pirate);
            FaunaReact.Startle(transform.position, 5f, 2f);
            open = true;
        }

        /// Tests: open it right now (as the local player).
        public void DebugOpen() { if (Available()) Complete(); }

        protected override void Update()
        {
            base.Update();
            poll -= Time.unscaledDeltaTime;
            if (poll <= 0f)
            {
                poll = 0.5f;
                open = SeaTreasure.IsOpened(chestId);
                if (loot != null && loot.activeSelf == open) loot.SetActive(!open);
                if (glint != null && glint.activeSelf == open) glint.SetActive(!open);
            }
            float want = open ? -105f : 0f;
            if (Mathf.Abs(lidAngle - want) > 0.1f && lid != null)
            {
                lidAngle = Mathf.MoveTowards(lidAngle, want, 160f * Time.deltaTime);
                lid.localRotation = Quaternion.Euler(lidAngle, 0f, 0f);
            }
        }
    }

    /// Wrecks and curiosities announce themselves the first time a diver of the crew gets close ("Entdeckt: …") –
    /// shared flag "found:&lt;id&gt;". One per content root, 2 checks a second, no allocations.
    public sealed class SeaContentDirector : MonoBehaviour
    {
        public struct Poi { public string id, nameDe, nameEn; public Vector3 pos; public float radius; }
        public readonly List<Poi> pois = new List<Poi>(16);
        float timer;

        void Update()
        {
            timer -= Time.unscaledDeltaTime;
            if (timer > 0f) return;
            timer = 0.5f;
            var gm = GameManager.I;
            var p = Refs.Player;
            if (gm == null || !gm.InGame || p == null || (p is Object o && o == null) || !p.IsUnderwater || p.View == null) return;
            Vector3 eye = p.View.transform.position;
            for (int i = 0; i < pois.Count; i++)
            {
                var poi = pois[i];
                if ((eye - poi.pos).sqrMagnitude > poi.radius * poi.radius) continue;
                string flag = SeaTreasure.FoundFlagPrefix + poi.id;
                if (StoryFlags.Has(flag)) continue;
                StoryFlags.Set(flag);
                GameEvents.RaiseNotify(NotifyKind.Story, Loc.Tr("Entdeckt: ", "Discovered: ") + Loc.Tr(poi.nameDe, poi.nameEn), Loc.Tr("Truhe suchen!", "Find the chest!"));
            }
        }
    }
}
