using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Sealebrities
{
    /// A caught fish in the (shared) cooler box.
    [Serializable]
    public class FishItem
    {
        public string kindId;
        public float weightKg;
        public int value;           // sale price in $ (already includes difficulty and co-op factors)
        public int dayCaught;
        public int uid;             // random id so co-op commands can find the same fish on the host

        public FishKind Kind => FishMarket.Kind(kindId);
        public string Name { get { var k = Kind; return k != null ? k.Name : kindId; } }
    }

    /// A fish offered in the phone's market app; sells by itself at sellAt.
    [Serializable]
    public class FishListing
    {
        public FishItem fish;
        public int listedDay;
        public float listedTime;    // time of day 0..1
        public float sellAt;        // absolute game time (day + timeOfDay) when it sells
    }

    public class FishKind
    {
        public string id, nameDe, nameEn;
        public Rarity rarity;
        public float chance;            // base catch weight
        public float minKg, maxKg;
        public float pricePerKg;
        public float rareBias;          // > 0: better rods catch it more often, < 0: less often
        public bool junk;               // not a fish (old boot …)
        /// Fallback model for FishingVisuals.BuildFish: 0 mackerel, 1 sardine, 2 sea bream.
        public int visual;
        public float length = 0.3f;     // body length in m at maxKg
        public float heightMul = 1f, widthMul = 1f;
        public Color back, flank, belly;

        public string Name => Loc.Tr(nameDe, nameEn);
        /// Body length (m) of a fish of this weight (cube-root scaling).
        public float LengthFor(float kg) => length * Mathf.Pow(Mathf.Clamp(kg / Mathf.Max(0.01f, maxKg), 0.05f, 1.5f), 1f / 3f);
    }

    /// Fishing economy: catches go into GameState.fishInventory, are listed in the phone market app and sell by
    /// themselves within ~20–60 real seconds (Tick, authority only). Balance target: the first air tank (130 $)
    /// takes ~13 fish ≈ 9 real minutes with the starter rod.
    public static class FishMarket
    {
        /// Real seconds until a bite with the starter rod (divide by GearStats.fishSpeed for better rods).
        public const float BiteDelayMin = 12f, BiteDelayMax = 30f;
        /// Seconds the bobber stays under water for the strike.
        public const float StrikeWindow = 1.1f;
        /// Real seconds a listing needs to sell.
        public const float SellDelayMin = 20f, SellDelayMax = 60f;

        public static event Action Changed;

        static readonly System.Random marketRng = new System.Random();
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// "#RRGGBB" → Color (pure C#, safe inside static initializers).
        static Color C(string hex)
        {
            int v = int.Parse(hex.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return new Color(((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f, 1f);
        }

        public static readonly List<FishKind> Kinds = new List<FishKind>
        {
            new FishKind { id = "boot", nameDe = "Alter Stiefel", nameEn = "Old Boot", rarity = Rarity.Common, chance = 0.05f,
                minKg = 0.4f, maxKg = 0.9f, pricePerKg = 1.5f, rareBias = -0.8f, junk = true, visual = 0, length = 0.3f, heightMul = 1f, widthMul = 1f,
                back = C("#5A4632"), flank = C("#6E5640"), belly = C("#3C2E22") },
            new FishKind { id = "sardine", nameDe = "Sardine", nameEn = "Sardine", rarity = Rarity.Common, chance = 0.30f,
                minKg = 0.06f, maxKg = 0.16f, pricePerKg = 40f, rareBias = -0.5f, visual = 1, length = 0.2f, heightMul = 0.72f, widthMul = 0.8f,
                back = C("#2F6E8E"), flank = C("#BFD8E2"), belly = C("#F2F6F7") },
            new FishKind { id = "mackerel", nameDe = "Makrele", nameEn = "Mackerel", rarity = Rarity.Common, chance = 0.30f,
                minKg = 0.3f, maxKg = 0.8f, pricePerKg = 14f, rareBias = 0f, visual = 0, length = 0.42f, heightMul = 0.75f, widthMul = 0.9f,
                back = C("#2E7D6B"), flank = C("#A9C9C2"), belly = C("#F1F3EE") },
            new FishKind { id = "bream", nameDe = "Brasse", nameEn = "Sea Bream", rarity = Rarity.Uncommon, chance = 0.18f,
                minKg = 0.5f, maxKg = 1.6f, pricePerKg = 11f, rareBias = 0.5f, visual = 2, length = 0.45f, heightMul = 1.35f, widthMul = 0.85f,
                back = C("#8C9AA6"), flank = C("#D7DEE3"), belly = C("#F4F2EA") },
            new FishKind { id = "snapper", nameDe = "Roter Schnapper", nameEn = "Red Snapper", rarity = Rarity.Uncommon, chance = 0.12f,
                minKg = 1f, maxKg = 3f, pricePerKg = 9f, rareBias = 1.2f, visual = 2, length = 0.6f, heightMul = 1.2f, widthMul = 0.85f,
                back = C("#C8402E"), flank = C("#EF7A5E"), belly = C("#F8D3C2") },
            new FishKind { id = "tuna", nameDe = "Gelbflossen-Thun", nameEn = "Yellowfin Tuna", rarity = Rarity.Rare, chance = 0.04f,
                minKg = 4f, maxKg = 12f, pricePerKg = 7f, rareBias = 2.5f, visual = 0, length = 1.0f, heightMul = 0.9f, widthMul = 1f,
                back = C("#1E3A6E"), flank = C("#9FB3C8"), belly = C("#F2D24B") },
            new FishKind { id = "mahi", nameDe = "Goldmakrele", nameEn = "Mahi-Mahi", rarity = Rarity.Epic, chance = 0.01f,
                minKg = 6f, maxKg = 15f, pricePerKg = 9f, rareBias = 4f, visual = 0, length = 1.2f, heightMul = 1.1f, widthMul = 0.7f,
                back = C("#2BA84A"), flank = C("#F2C230"), belly = C("#F7E27A") },
        };

        static GameState S => GameManager.I != null ? GameManager.I.State : null;

        public static FishKind Kind(string id)
        {
            if (id == null) return null;
            for (int i = 0; i < Kinds.Count; i++) if (Kinds[i].id == id) return Kinds[i];
            return null;
        }

        public static void RaiseChanged() => Changed?.Invoke();

        // ------------------------------------------------------------------ catching (local player's rod)
        /// Seconds until the next bite with the local player's equipped rod.
        public static float BiteDelay(System.Random rng)
        {
            float speed = GameManager.I != null ? Mathf.Max(0.3f, GameManager.I.Gear.fishSpeed) : 1f;
            return Mathf.Lerp(BiteDelayMin, BiteDelayMax, (float)rng.NextDouble()) / speed;
        }

        /// Rolls a catch with the local player's equipped rod (luck shifts toward rarer, heavier fish).
        /// The first 5 catches of a save are never junk. Pass the result to AddCatch.
        public static FishItem RollCatch(System.Random rng)
        {
            if (rng == null) rng = marketRng;
            var gm = GameManager.I;
            float luck = gm != null ? Mathf.Max(0f, gm.Gear.fishLuck) : 0f;
            bool noJunk = gm != null && gm.State.fishCaught < 5;

            float total = 0f;
            for (int i = 0; i < Kinds.Count; i++) total += Weight(Kinds[i], luck, noJunk);
            float r = (float)rng.NextDouble() * total;
            FishKind pick = Kinds[2];
            for (int i = 0; i < Kinds.Count; i++)
            {
                float w = Weight(Kinds[i], luck, noJunk);
                if (w <= 0f) continue;
                r -= w;
                if (r <= 0f) { pick = Kinds[i]; break; }
            }

            float t = Mathf.Pow((float)rng.NextDouble(), 1f / (1f + luck));   // luck → heavier
            float kg = Mathf.Round(Mathf.Lerp(pick.minKg, pick.maxKg, t) * 100f) / 100f;
            return new FishItem
            {
                kindId = pick.id,
                weightKg = kg,
                value = ValueOf(pick, kg),
                dayCaught = gm != null ? gm.State.day : 1,
                uid = rng.Next(1, int.MaxValue)
            };
        }

        static float Weight(FishKind k, float luck, bool noJunk)
        {
            if (noJunk && k.junk) return 0f;
            return k.chance * Mathf.Max(0.05f, 1f + luck * k.rareBias);
        }

        /// Sale value of a fish: weight × price/kg × difficulty income × co-op factor (min 1 $).
        public static int ValueOf(FishKind k, float kg)
        {
            float mul = 1f;
            var gm = GameManager.I;
            if (gm != null) mul *= gm.Difficulty.incomeMultiplier;
            mul *= Progression.CoopFishFactor(Net.PlayerCount);
            return Mathf.Max(1, Mathf.RoundToInt(kg * k.pricePerKg * mul));
        }

        // ------------------------------------------------------------------ shared-state mutations
        /// Puts a caught fish into the shared cooler box. Raises GameEvents.FishCaught.
        public static void AddCatch(FishItem f)
        {
            var s = S;
            if (f == null || s == null) return;
            if (f.uid == 0) f.uid = marketRng.Next(1, int.MaxValue);
            if (f.dayCaught <= 0) f.dayCaught = s.day;
            if (Net.IsClient)
            {
                Net.Command(NetCommand.AddCatch, JsonUtility.ToJson(f));
                // optimistic local copy – the host's snapshot replaces it
            }
            s.fishInventory.Add(f);
            s.fishCaught++;
            GameEvents.RaiseFishCaught(f.value);
            Changed?.Invoke();
        }

        /// Lists one fish of the cooler box on the market. Returns false when it is not in the box.
        public static bool List(FishItem f)
        {
            var s = S;
            if (s == null || f == null) return false;
            int idx = s.fishInventory.IndexOf(f);
            return idx >= 0 && ListAt(idx);
        }

        public static bool ListAt(int index)
        {
            var s = S;
            if (s == null || index < 0 || index >= s.fishInventory.Count) return false;
            var f = s.fishInventory[index];
            if (Net.IsClient)
                Net.Command(NetCommand.ListFish, index.ToString(Inv), f != null ? f.uid.ToString(Inv) : "0");
            s.fishInventory.RemoveAt(index);
            if (f != null)
            {
                s.fishListings.Add(NewListing(s, f));
                s.fishListedTotal++;
            }
            Changed?.Invoke();
            return true;
        }

        /// Host side of a client's ListFish command: finds the fish by uid. A miss means another player already listed
        /// or sold that fish – never list a different one by index (indexes differ between machines). The index is
        /// only used for legacy fish without a uid.
        public static bool ListByUid(int uid, int fallbackIndex)
        {
            var s = S;
            if (s == null) return false;
            if (uid != 0)
            {
                for (int i = 0; i < s.fishInventory.Count; i++)
                    if (s.fishInventory[i] != null && s.fishInventory[i].uid == uid) return ListAt(i);
                return false;
            }
            return ListAt(fallbackIndex);
        }

        /// Lists every fish in the cooler box. Returns the number listed.
        public static int ListAll()
        {
            var s = S;
            if (s == null || s.fishInventory.Count == 0) return 0;
            if (Net.IsClient) Net.Command(NetCommand.ListAllFish);
            int n = 0;
            for (int i = 0; i < s.fishInventory.Count; i++)
            {
                var f = s.fishInventory[i];
                if (f == null) continue;
                s.fishListings.Add(NewListing(s, f));
                n++;
            }
            s.fishInventory.Clear();
            s.fishListedTotal += n;
            Changed?.Invoke();
            return n;
        }

        static FishListing NewListing(GameState s, FishItem f)
        {
            float delay;
            lock (marketRng) delay = Mathf.Lerp(SellDelayMin, SellDelayMax, (float)marketRng.NextDouble());
            return new FishListing
            {
                fish = f,
                listedDay = s.day,
                listedTime = s.timeOfDay,
                sellAt = s.day + s.timeOfDay + delay / GameManager.DayLengthSeconds
            };
        }

        /// Sells due listings (called every frame by GameManager.Update where Net.HasAuthority).
        public static void Tick()
        {
            if (!Net.HasAuthority) return;
            var gm = GameManager.I;
            if (gm == null || !gm.InGame) return;
            var s = gm.State;
            var list = s.fishListings;
            if (list.Count == 0) return;
            float now = s.day + s.timeOfDay;
            long total = 0;
            int count = 0;
            FishItem last = null;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var l = list[i];
                if (l == null || l.fish == null) { list.RemoveAt(i); continue; }
                if (now < l.sellAt) continue;
                total += Math.Max(0, l.fish.value);
                count++;
                last = l.fish;
                list.RemoveAt(i);
            }
            if (count == 0) return;
            s.fishSold += count;
            s.fishMoney += total;
            gm.AddMoney(total);
            if (count == 1)
                GameEvents.RaiseToast(Loc.Tr($"+{Loc.Money(total)} – {last.Name} verkauft", $"+{Loc.Money(total)} – {last.Name} sold"));
            else
                GameEvents.RaiseToast(Loc.Tr($"+{Loc.Money(total)} – {count} Fische verkauft", $"+{Loc.Money(total)} – {count} fish sold"));
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ queries (allocation free)
        public static int InventoryCount => S != null ? S.fishInventory.Count : 0;
        public static int ListingCount => S != null ? S.fishListings.Count : 0;

        public static long InventoryValue
        {
            get
            {
                var s = S;
                if (s == null) return 0;
                long v = 0;
                for (int i = 0; i < s.fishInventory.Count; i++) if (s.fishInventory[i] != null) v += s.fishInventory[i].value;
                return v;
            }
        }

        public static long ListedValue
        {
            get
            {
                var s = S;
                if (s == null) return 0;
                long v = 0;
                for (int i = 0; i < s.fishListings.Count; i++) if (s.fishListings[i]?.fish != null) v += s.fishListings[i].fish.value;
                return v;
            }
        }

        /// Real seconds until a listing sells (0 = any moment now).
        public static float SecondsLeft(FishListing l)
        {
            var s = S;
            if (s == null || l == null) return 0f;
            return Mathf.Max(0f, (l.sellAt - (s.day + s.timeOfDay)) * GameManager.DayLengthSeconds);
        }

        /// 0..1 progress of a listing toward its sale (for a progress bar).
        public static float SaleProgress01(FishListing l)
        {
            var s = S;
            if (s == null || l == null) return 0f;
            float start = l.listedDay + l.listedTime;
            float span = Mathf.Max(1e-5f, l.sellAt - start);
            return Mathf.Clamp01((s.day + s.timeOfDay - start) / span);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Changed = null; }
    }
}
