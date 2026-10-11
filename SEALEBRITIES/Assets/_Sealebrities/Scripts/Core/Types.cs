namespace Sealebrities
{
    public enum Language { German, English }

    /// Only Snorkeler ("Entspannt"), OpenWater ("Normal") and Advanced ("Schwer") are selectable
    /// (DifficultySettings.Selectable). Technical/Abyss stay for old saves and are treated as Advanced.
    public enum Difficulty { Snorkeler, OpenWater, Advanced, Technical, Abyss }

    public enum SizeTier { Tiny, Normal, Large, Huge, Legendary }

    public enum Rarity { Common, Uncommon, Rare, Epic, Legendary }

    /// Creature body plans (mesh factory + animation). Values are saved/sent as ints: APPEND new values at the end.
    public enum BodyPlan
    {
        Fish, Shark, Ray, Eel, Turtle, Jelly, Octopus, Squid, Cuttlefish,
        Seahorse, SeaLion, Sunfish, Anglerfish, VampireSquid,
        // round 2 (G.1)
        Crab, Starfish, Dolphin, Whale,
        // round 2b (L.11 island animals)
        Bird, Lizard, Quadruped, Primate, Bat
    }

    /// Creature behaviours. APPEND new values at the end.
    public enum BehaviorType
    {
        Schooling, Wanderer, Skittish, Curious, Predator, Ambush, BottomDweller, Drifter,
        // round 2 (G.1)
        Playful, Crawler,
        // round 2b (L.11)
        Perch, Soar, Bask, Climb, Herd, Mischief
    }

    public enum PatternType { Solid, BellyGradient, VerticalBands, HorizontalStripes, Spots, Mottled, Translucent }

    public enum TitleStyle { Honest, Spicy, Clickbait }

    /// Gear slots. APPEND new values at the end (saved as item ids, but UI/net use the int).
    public enum GearSlot
    {
        Tank, Fins, Lamp, Camera, Boat, Submarine, Rod,
        // round 2 (E.4, E.5, Q32)
        Compressor, Radio, CameraMod,
        // round 2b (K.3 boat winch, K.8 radio battery)
        BoatWinch, Part
    }

    /// G.1 spawn zone of a species. SUPERSEDED by SpeciesDef.zones + depth bands (L.1) – kept only so older code compiles.
    public enum SpawnZone { Site, TidePool, Beach, OpenSea, Seafloor }

    /// Food buffs (E.2). Per player, never synced.
    public enum BuffType { AirCapacity, SwimSpeed, SteadyHands, BumpResist, NightEyes }

    /// Max 1 Food + 1 Drink buff at a time (E).
    public enum FoodKind { Food, Drink }

    /// Time-of-day mask of a species (L.1). Day 07:00–17:30, Dusk 17:30–20:30 and 05:30–07:00, Night 20:30–05:30
    /// (see DayPhase.Of).
    [System.Flags]
    public enum DayMask : byte { Day = 1, Dusk = 2, Night = 4, All = 7 }

    /// Notification kinds (L.10): every toast/push banner = one icon + one short line + optional chip. The UI maps each
    /// kind to a glyph and a colour. APPEND new values at the end.
    public enum NotifyKind : byte
    {
        Info, Money, Followers, Sealog, Clip, Delivery, Quest, Sponsor, Story, Collectible, Warning, Event, Achievement
    }

    /// Viewfinder hint (K.2 – replaces the J.3 list). Set by Filming in ViewfinderInfo.Hint, drawn by the UI.
    public enum ShotHint : byte { None, Recording, NoAnimals, TooDark, Closer, SealogReady, New }

    /// Procedural motion states of a bean diver / NPC (L.13). Sent as a byte in the avatar state. APPEND only.
    public enum MotionState : byte
    {
        Idle, Walk, Run, Jump, Fall, Land, Ladder, Swim, Dive, Sit, Lie, Carry, Tow, Selfie, Unconscious, Crouch
    }

    /// Character screen focus presets (1c).
    public enum PreviewFocus { Body, Head, Face, Feet }

    /// Where the character screen was opened from (1c).
    public enum CharacterScreenContext { FirstStart, MainMenu, Lobby, Pause, Locker }

    /// Delivery mishaps (L.12). Rolled deterministically by the Boat module's HeliService from the delivery hash and
    /// stored in Delivery.mishap so every co-op machine agrees. APPEND only.
    public enum DeliveryMishap { None, Water, HutRoof, Palm, WrongPlace, Village, Goat, Boat }

    /// Livestream chat moods (C.6).
    public enum ChatMood : byte { Neutral, Hype, Laugh, Scared, Donation, Hater, System }

    /// Livestream dare states (C.5).
    public enum DareState : byte { None, Offered, Running, Done, Failed, Expired }

    /// Selfie expressions (K.4): hotbar keys 1–6 in the selfie view; 0 = automatic.
    public enum SelfieExpression : byte { Auto, Grin, Wow, Scared, Kissy, Wink, Laugh }
}
