namespace Sealebrities
{
    /// How a creature moves (FaunaCreature integrates each mode differently).
    public enum FaunaMode : byte { Swim, Ground, Fly }

    /// Scripted control of a creature for a while (animal events, dolphins riding the bow wave, mischief, Gerald's
    /// photobomb). Steer writes the creature's behavior outputs (desiredVel, faceDir, …) and returns true; returning
    /// false hands the tick back to the creature's own behavior.
    public interface IFaunaDirector
    {
        bool Steer(FaunaCreature c, float dt);
    }

    /// Scripted leader of a school (bait ball, a big school passing the diver).
    public interface IFaunaSchoolDirector
    {
        bool SteerLeader(FaunaSchool s, float dt);
    }

    public static class FaunaModes
    {
        public static FaunaMode Of(SpeciesDef s)
        {
            if (s == null) return FaunaMode.Swim;
            if (s.plan == BodyPlan.Bird || s.plan == BodyPlan.Bat) return FaunaMode.Fly;
            if (s.land) return FaunaMode.Ground;
            if (s.plan == BodyPlan.Crab || s.plan == BodyPlan.Starfish || s.behavior == BehaviorType.Crawler) return FaunaMode.Ground;
            return FaunaMode.Swim;
        }

        /// Species that live on the seafloor / on the ground (spawned on the bottom, never in open water).
        public static bool IsBottom(SpeciesDef s)
        {
            if (s == null) return false;
            return Of(s) == FaunaMode.Ground || s.behavior == BehaviorType.BottomDweller || s.behavior == BehaviorType.Ambush
                   || s.plan == BodyPlan.Octopus || s.id == "clownfish" || s.habitat == "anemone" || s.habitat == "crevice" || s.habitat == "seafan";
        }
    }
}
