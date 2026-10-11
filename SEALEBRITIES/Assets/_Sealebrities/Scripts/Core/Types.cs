namespace Sealebrities
{
    public enum Language { German, English }

    /// Only Snorkeler ("Entspannt"), OpenWater ("Normal") and Advanced ("Schwer") are selectable
    /// (DifficultySettings.Selectable). Technical/Abyss stay for old saves and are treated as Advanced.
    public enum Difficulty { Snorkeler, OpenWater, Advanced, Technical, Abyss }

    public enum SizeTier { Tiny, Normal, Large, Huge, Legendary }

    public enum Rarity { Common, Uncommon, Rare, Epic, Legendary }

    public enum BodyPlan
    {
        Fish, Shark, Ray, Eel, Turtle, Jelly, Octopus, Squid, Cuttlefish,
        Seahorse, SeaLion, Sunfish, Anglerfish, VampireSquid
    }

    public enum BehaviorType { Schooling, Wanderer, Skittish, Curious, Predator, Ambush, BottomDweller, Drifter }

    public enum PatternType { Solid, BellyGradient, VerticalBands, HorizontalStripes, Spots, Mottled, Translucent }

    public enum TitleStyle { Honest, Spicy, Clickbait }

    public enum GearSlot { Tank, Fins, Lamp, Camera, Boat, Submarine, Rod }
}
