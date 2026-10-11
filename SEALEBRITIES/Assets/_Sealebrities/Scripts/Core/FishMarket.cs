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
        // ---- round 2 (D.2, B.4 Dr. Fin fishing quests, L.7 Tomas T3)
        /// Caught while standing on the boat deck.
        public bool fromBoat;
        /// Rolled on the open-sea table (bobber in ≥ 12 m water, ≥ 60 m from every shore, rod allows it).
        public bool openSea;

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
        /// Catch weight on the SHORE table (0 = never from the shore).
        public float chance;
        /// Catch weight on the OPEN-SEA table (0 = never on the open sea) – D.2.
        public float seaChance;
        public float minKg, maxKg;
        public float pricePerKg;
        public float rareBias;          // > 0: better rods catch it more often, < 0: less often
        public bool junk;               // not a fish (old boot …)
        /// Lowest rod tier that can hook it (marlin „Kapitän Pieks“: rod_t2+).
        public int minRodTier;
        // ---- D.1 reel minigame
        /// Seconds of perfect green-zone reeling (reelPower 1) to land it.
        public float stamina = 3f;
        /// Seconds between two runs (0 = never runs, e.g. the old boot).
        public float runMin, runMax;
        /// Tension gain in %/s while the fish runs.
        public float runStrength;
        /// Jumps out of the water during some runs (tuna, mahi, swordfish, marlin).
        public bool leaps;
        // ---- looks (FishingVisuals)
        /// Fallback model for FishingVisuals.BuildFish: 0 mackerel, 1 sardine, 2 sea bream.
        public int visual;
        public float length = 0.3f;     // body length in m at maxKg
        public float heightMul = 1f, widthMul = 1f;
        /// Beak / sword length as a fraction of the body length (garfish, swordfish, marlin).
        public float bill;
        /// Tall dorsal sail (marlin) – 0 none, 1 marlin sail, 2 swordfish crescent.
        public int dorsal;
        public Color back, flank, belly;

        public string Name => Loc.Tr(nameDe, nameEn);
        /// Body length (m) of a fish of this weight (cube-root scaling).
        public float LengthFor(float kg) => length * Mathf.Pow(Mathf.Clamp(kg / Mathf.Max(0.01f, maxKg), 0.05f, 1.5f), 1f / 3f);
        public bool Runs => runMax > 0f && runStrength > 0f;
    }

    /// Dr. Fin's fishing research tasks (B.4 rod chain R1–R4). Data for the npc module's quest checks: count the
    /// catches with FishMarket.Matches on the host (FishMarket.Caught) – the npc module pays the rewards.
    public sealed class FishQuestDef
    {
        public string id, kindId;
        public float minKg;
        public int count;
        public bool fromBoat, openSea;
        /// Reward: money; rewardItem = gear id given by FishMarket.GrantItem (R4: the research rod).
        public long money;
        public string rewardItem;
        /// Story flag set on completion (R1: StoryKeys.RodT1Discount).
        public string rewardFlag;
        public string titleDe, titleEn;
        public string Title => Loc.Tr(titleDe, titleEn);
    }

    /// Fishing economy: catches go into GameState.fishInventory, are listed in the phone market app and sell by
    /// themselves within ~20–60 real seconds (Tick, authority only).
    ///
    /// Round 2 (Design_Runde2 D + K.1): two catch tables – the SHORE table (dock, beaches, the boat in the lagoon) and
    /// the OPEN-SEA table (bobber in ≥ 12 m of water and ≥ 60 m from every shore – typically from the boat; needs a rod
    /// with GearItem.openSea, the bamboo rod falls back to the shore table). Every kind carries its reel-minigame data
    /// (stamina, runs). Shore prices are the K.1 values (× 1.25 of round 2: the first tank after ≈ 7 minutes of fishing).
    /// Balance constants of fish live HERE (J.5).
    public static class FishMarket
    {
        /// Real seconds until a bite with the starter rod on the shore table (divide by GearStats.fishSpeed).
        public const float BiteDelayMin = 12f, BiteDelayMax = 30f;
        /// D.2: open-sea bites take longer (bigger fish).
        public const float SeaBiteDelayMin = 20f, SeaBiteDelayMax = 45f;
        /// Seconds the bobber stays under water for the strike.
        public const float StrikeWindow = 1.1f;
        /// Real seconds a listing needs to sell.
        public const float SellDelayMin = 20f, SellDelayMax = 60f;
        /// D.2 open-sea rule.
        public const float OpenSeaMinDepth = 12f, OpenSeaShoreDistance = 60f;

        // ---- D.1 reel tension minigame (tension in %)
        public const float ReelTensionUp = 55f;        // %/s while Use is held
        public const float ReelTensionDown = 70f;      // %/s while released
        public const float ReelYellowMax = 90f;        // yellow 75..90, red 90..100 (green from DifficultySettings)
        public const float ReelHookTension = 42f;      // tension right after the strike
        public const float ReelSnapSeconds = 0.35f;    // × rod lineStrength × difficulty snapTimeMultiplier at 100 %
        public const float ReelSlackTension = 5f, ReelSlackSeconds = 2f;
        public const float ReelRunMin = 0.6f, ReelRunMax = 1.4f;
        public const float ReelCounterSteer = 0.4f;    // holding the opposite direction cuts a run's pull by 40 %
        public const float ZoneGreen = 1f, ZoneYellow = 0.5f, ZoneRed = 0.2f, ZoneBelow = 0.3f;

        public static event Action Changed;
        /// Every fish that went into the cooler box on THIS machine (local catches; on the host also the guests'
        /// catches arriving by command). Dr. Fin / Tomas / sponsor checks: listen on the host (Net.HasAuthority).
        public static event Action<FishItem> Caught;

        static readonly System.Random marketRng = new System.Random();
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// "#RRGGBB" → Color (pure C#, safe inside static initializers).
        static Color C(string hex)
        {
            int v = int.Parse(hex.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return new Color(((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f, 1f);
        }

        /// D.1 / D.2 / K.1. Shore table: boot .05, sardine .30, garfish .12, mackerel .28, bream .15, snapper .10.
        /// Open sea: bonito .35, tuna .22, amberjack .18, mahi .12, snapper .08, swordfish .04, marlin .01 (rod_t2+).
        public static readonly List<FishKind> Kinds = new List<FishKind>
        {
            new FishKind { id = "boot", nameDe = "Alter Stiefel", nameEn = "Old Boot", rarity = Rarity.Common, chance = 0.05f,
                minKg = 0.4f, maxKg = 0.9f, pricePerKg = 1.5f, rareBias = -0.8f, junk = true, stamina = 1.5f,
                visual = 0, length = 0.3f, back = C("#5A4632"), flank = C("#6E5640"), belly = C("#3C2E22") },
            new FishKind { id = "sardine", nameDe = "Sardine", nameEn = "Sardine", rarity = Rarity.Common, chance = 0.30f,
                minKg = 0.06f, maxKg = 0.16f, pricePerKg = 50f, rareBias = -0.5f, stamina = 2f, runMin = 3f, runMax = 4f, runStrength = 30f,
                visual = 1, length = 0.2f, heightMul = 0.72f, widthMul = 0.8f,
                back = C("#2F6E8E"), flank = C("#BFD8E2"), belly = C("#F2F6F7") },
            new FishKind { id = "garfish", nameDe = "Hornhecht", nameEn = "Garfish", rarity = Rarity.Common, chance = 0.12f,
                minKg = 0.4f, maxKg = 1.0f, pricePerKg = 12.5f, rareBias = 0f, stamina = 2.5f, runMin = 2f, runMax = 3f, runStrength = 40f,
                visual = 1, length = 0.78f, heightMul = 0.42f, widthMul = 0.62f, bill = 0.22f,
                back = C("#2E7F86"), flank = C("#B8D2D4"), belly = C("#EEF1EC") },
            new FishKind { id = "mackerel", nameDe = "Makrele", nameEn = "Mackerel", rarity = Rarity.Common, chance = 0.28f,
                minKg = 0.3f, maxKg = 0.8f, pricePerKg = 17.5f, rareBias = 0f, stamina = 3f, runMin = 2.5f, runMax = 3.5f, runStrength = 40f,
                visual = 0, length = 0.42f, heightMul = 0.75f, widthMul = 0.9f,
                back = C("#2E7D6B"), flank = C("#A9C9C2"), belly = C("#F1F3EE") },
            new FishKind { id = "bream", nameDe = "Brasse", nameEn = "Sea Bream", rarity = Rarity.Uncommon, chance = 0.15f,
                minKg = 0.5f, maxKg = 1.6f, pricePerKg = 14f, rareBias = 0.5f, stamina = 4f, runMin = 2.5f, runMax = 3.5f, runStrength = 45f,
                visual = 2, length = 0.45f, heightMul = 1.35f, widthMul = 0.85f,
                back = C("#8C9AA6"), flank = C("#D7DEE3"), belly = C("#F4F2EA") },
            new FishKind { id = "snapper", nameDe = "Roter Schnapper", nameEn = "Red Snapper", rarity = Rarity.Uncommon, chance = 0.10f, seaChance = 0.08f,
                minKg = 1f, maxKg = 3f, pricePerKg = 11f, rareBias = 1.2f, stamina = 5f, runMin = 2f, runMax = 3f, runStrength = 55f,
                visual = 2, length = 0.6f, heightMul = 1.2f, widthMul = 0.85f,
                back = C("#C8402E"), flank = C("#EF7A5E"), belly = C("#F8D3C2") },
            // ---- open sea (D.2)
            new FishKind { id = "bonito", nameDe = "Bonito", nameEn = "Bonito", rarity = Rarity.Common, seaChance = 0.35f,
                minKg = 2f, maxKg = 5f, pricePerKg = 8f, rareBias = 0f, stamina = 5.5f, runMin = 2f, runMax = 3f, runStrength = 55f,
                visual = 0, length = 0.7f, heightMul = 0.9f, widthMul = 0.95f,
                back = C("#24406E"), flank = C("#9DB0C4"), belly = C("#EEF0EE") },
            new FishKind { id = "tuna", nameDe = "Gelbflossen-Thun", nameEn = "Yellowfin Tuna", rarity = Rarity.Uncommon, seaChance = 0.22f,
                minKg = 4f, maxKg = 12f, pricePerKg = 7f, rareBias = 1.5f, stamina = 8f, runMin = 1.8f, runMax = 2.6f, runStrength = 70f, leaps = true,
                visual = 0, length = 1.0f, heightMul = 0.95f, widthMul = 1f,
                back = C("#1E3A6E"), flank = C("#9FB3C8"), belly = C("#F2D24B") },
            new FishKind { id = "amberjack", nameDe = "Bernsteinmakrele", nameEn = "Amberjack", rarity = Rarity.Uncommon, seaChance = 0.18f,
                minKg = 5f, maxKg = 15f, pricePerKg = 6f, rareBias = 1.5f, stamina = 9f, runMin = 1.8f, runMax = 2.6f, runStrength = 75f,
                visual = 0, length = 1.15f, heightMul = 0.9f, widthMul = 0.9f,
                back = C("#6A7452"), flank = C("#C9B98C"), belly = C("#EFEADA") },
            new FishKind { id = "mahi", nameDe = "Goldmakrele", nameEn = "Mahi-Mahi", rarity = Rarity.Rare, seaChance = 0.12f,
                minKg = 6f, maxKg = 15f, pricePerKg = 9f, rareBias = 2.5f, stamina = 10f, runMin = 1.5f, runMax = 2.3f, runStrength = 75f, leaps = true,
                visual = 0, length = 1.2f, heightMul = 1.1f, widthMul = 0.7f,
                back = C("#2BA84A"), flank = C("#F2C230"), belly = C("#F7E27A") },
            new FishKind { id = "swordfish", nameDe = "Schwertfisch", nameEn = "Swordfish", rarity = Rarity.Epic, seaChance = 0.04f,
                minKg = 20f, maxKg = 60f, pricePerKg = 5f, rareBias = 4f, stamina = 13f, runMin = 1.5f, runMax = 2.2f, runStrength = 85f, leaps = true,
                visual = 0, length = 1.9f, heightMul = 0.85f, widthMul = 0.85f, bill = 0.42f, dorsal = 2,
                back = C("#2E3550"), flank = C("#7E8AA6"), belly = C("#D9DEE2") },
            new FishKind { id = "marlin", nameDe = "Blauer Marlin „Kapitän Pieks“", nameEn = "Blue Marlin “Captain Poke”", rarity = Rarity.Legendary, seaChance = 0.01f,
                minKg = 80f, maxKg = 120f, pricePerKg = 6f, rareBias = 6f, minRodTier = 2, stamina = 18f, runMin = 1.2f, runMax = 2.0f, runStrength = 95f, leaps = true,
                visual = 0, length = 2.8f, heightMul = 0.85f, widthMul = 0.8f, bill = 0.34f, dorsal = 1,
                back = C("#24357A"), flank = C("#5F86C4"), belly = C("#E6EAEE") },
        };

        /// B.4 rod chain R1 → R4 (always offered in this order by the npc module).
        public static readonly FishQuestDef[] FinFishingQuests =
        {
            new FishQuestDef { id = "fin_fish_bream", kindId = "bream", minKg = 1.2f, count = 3, money = 150, rewardFlag = StoryKeys.RodT1Discount,
                titleDe = "3 Brassen über 1,2 kg", titleEn = "3 sea bream over 1.2 kg" },
            new FishQuestDef { id = "fin_fish_snapper_boat", kindId = "snapper", minKg = 2f, count = 1, fromBoat = true, money = 400,
                titleDe = "Roter Schnapper vom Boot, über 2 kg", titleEn = "Red snapper from the boat, over 2 kg" },
            new FishQuestDef { id = "fin_fish_tuna", kindId = "tuna", minKg = 8f, count = 1, openSea = true, money = 1200,
                titleDe = "Gelbflossen-Thun, über 8 kg", titleEn = "Yellowfin tuna, over 8 kg" },
            new FishQuestDef { id = "fin_fish_sword", kindId = "swordfish", minKg = 0f, count = 1, openSea = true, rewardItem = "rod_t3",
                titleDe = "Schwertfisch für die Forschung", titleEn = "A swordfish for science" },
        };

        static GameState S => GameManager.I != null ? GameManager.I.State : null;

        public static FishKind Kind(string id)
        {
            if (id == null) return null;
            for (int i = 0; i < Kinds.Count; i++) if (Kinds[i].id == id) return Kinds[i];
            return null;
        }

        public static FishQuestDef FinQuest(string id)
        {
            if (id == null) return null;
            for (int i = 0; i < FinFishingQuests.Length; i++) if (FinFishingQuests[i].id == id) return FinFishingQuests[i];
            return null;
        }

        public static void RaiseChanged() => Changed?.Invoke();

        // ------------------------------------------------------------------ open sea (D.2)
        static readonly float[] ShoreRings = { 12f, 24f, 36f, 48f, 60f };
        const int ShoreDirections = 20;

        /// True when a bobber at p is on the open sea: water ≥ 12 m deep and no land (main island, landmark islands,
        /// Fog Isle) within 60 m. ~100 height samples – call it once per cast, not per frame.
        public static bool IsOpenSea(Vector3 p)
        {
            if (WorldInfo.SeafloorDepth(p) < OpenSeaMinDepth) return false;
            float sea = Waves.SeaLevel;
            for (int r = 0; r < ShoreRings.Length; r++)
            {
                float rad = ShoreRings[r];
                for (int k = 0; k < ShoreDirections; k++)
                {
                    float a = (k + (r & 1) * 0.5f) / ShoreDirections * Mathf.PI * 2f;
                    var q = new Vector3(p.x + Mathf.Cos(a) * rad, 0f, p.z + Mathf.Sin(a) * rad);
                    if (WorldInfo.GroundHeight(q) > sea - 0.2f) return false;
                }
            }
            return true;
        }

        /// The local player's rod may use the open-sea table (carbon rod and better).
        public static bool RodAllowsOpenSea(in GearStats g) => g.rodOpenSea;

        // ------------------------------------------------------------------ catching (local player's rod)
        /// Seconds until the next bite with the local player's equipped rod on the shore table.
        public static float BiteDelay(System.Random rng) => BiteDelay(rng, false);

        /// Seconds until the next bite (open sea: 20–45 s, shore: 12–30 s) ÷ the rod's fishSpeed.
        public static float BiteDelay(System.Random rng, bool openSea)
        {
            if (rng == null) rng = marketRng;
            float speed = GameManager.I != null ? Mathf.Max(0.3f, GameManager.I.Gear.fishSpeed) : 1f;
            float lo = openSea ? SeaBiteDelayMin : BiteDelayMin, hi = openSea ? SeaBiteDelayMax : BiteDelayMax;
            return Mathf.Lerp(lo, hi, (float)rng.NextDouble()) / speed;
        }

        /// Rolls a shore catch with the local player's equipped rod. Pass the result to AddCatch.
        public static FishItem RollCatch(System.Random rng) => RollCatch(rng, false);

        /// Rolls a catch with the local player's equipped rod (luck shifts toward rarer, heavier fish) on the shore or
        /// the open-sea table. The first 5 catches of a save are never junk. Pass the result to AddCatch.
        public static FishItem RollCatch(System.Random rng, bool openSea)
        {
            if (rng == null) rng = marketRng;
            var gm = GameManager.I;
            GearStats g = gm != null ? gm.Gear : default;
            float luck = gm != null ? Mathf.Max(0f, g.fishLuck) : 0f;
            int rodTier = gm != null ? g.rodTier : 0;
            bool noJunk = gm != null && gm.State.fishCaught < 5;

            float total = 0f;
            for (int i = 0; i < Kinds.Count; i++) total += Weight(Kinds[i], luck, noJunk, openSea, rodTier);
            float r = (float)rng.NextDouble() * total;
            FishKind pick = openSea ? Kind("bonito") : Kind("mackerel");
            for (int i = 0; i < Kinds.Count; i++)
            {
                float w = Weight(Kinds[i], luck, noJunk, openSea, rodTier);
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
                uid = rng.Next(1, int.MaxValue),
                openSea = openSea
            };
        }

        static float Weight(FishKind k, float luck, bool noJunk, bool openSea, int rodTier)
        {
            if (noJunk && k.junk) return 0f;
            if (rodTier < k.minRodTier) return 0f;
            float c = openSea ? k.seaChance : k.chance;
            if (c <= 0f) return 0f;
            return c * Mathf.Max(0.05f, 1f + luck * k.rareBias);
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

        /// Quest check helper (Dr. Fin R1–R4, Tomas T3 "≥ 3 kg from the boat", sponsor "catch 8 fish").
        /// kindId null = any real fish (no junk).
        public static bool Matches(FishItem f, string kindId, float minKg = 0f, bool needBoat = false, bool needOpenSea = false)
        {
            if (f == null) return false;
            var k = f.Kind;
            if (k == null || (kindId == null && k.junk)) return false;
            if (kindId != null && f.kindId != kindId) return false;
            if (f.weightKg + 1e-4f < minKg) return false;
            if (needBoat && !f.fromBoat) return false;
            if (needOpenSea && !f.openSea) return false;
            return true;
        }

        public static bool Matches(FishItem f, FishQuestDef q) =>
            q != null && Matches(f, q.kindId, q.minKg, q.fromBoat, q.openSea);

        /// Host/offline: gives the crew a gear item as a reward (Dr. Fin R4 → "rod_t3"). Returns false when unknown,
        /// already owned or called on a co-op client.
        public static bool GrantItem(string itemId)
        {
            var s = S;
            var item = GearDatabase.Get(itemId);
            if (s == null || item == null || !Net.HasAuthority || s.ownedItems.Contains(itemId)) return false;
            s.ownedItems.Add(itemId);
            GameEvents.RaiseGearChanged();
            GameEvents.RaiseNotify(NotifyKind.Delivery, item.Name, Loc.Tr("Neu!", "New!"));
            if (GameManager.I != null) GameManager.I.Save();
            return true;
        }

        // ------------------------------------------------------------------ shared-state mutations
        /// Puts a caught fish into the shared cooler box. Raises GameEvents.FishCaught and Caught.
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
            Caught?.Invoke(f);
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
            // L.10: icon + one short line + value chip
            if (count == 1)
                GameEvents.RaiseNotify(NotifyKind.Money, Loc.Tr($"{last.Name} verkauft", $"{last.Name} sold"), "+" + Loc.Money(total));
            else
                GameEvents.RaiseNotify(NotifyKind.Money, Loc.Tr($"{count} Fische verkauft", $"{count} fish sold"), "+" + Loc.Money(total));
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
        static void ResetStatics() { Changed = null; Caught = null; }
    }
}
