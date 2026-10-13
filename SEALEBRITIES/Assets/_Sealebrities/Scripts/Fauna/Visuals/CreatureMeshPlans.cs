using System;
using UnityEngine;

namespace Sealebrities
{
    /// Dispatches a species to its body-plan builder and returns the filled context (managed data only).
    internal static class CreatureMeshPlans
    {
        public static CreatureMeshContext Build(SpeciesDef s)
        {
            var ctx = new CreatureMeshContext(s);
            switch (s.plan)
            {
                case BodyPlan.Fish: CreatureMeshFish.Build(ctx); break;
                case BodyPlan.Shark: CreatureMeshShark.Build(ctx); break;
                case BodyPlan.Ray: CreatureMeshRay.Build(ctx); break;
                case BodyPlan.Eel: CreatureMeshEel.Build(ctx); break;
                case BodyPlan.Turtle: CreatureMeshTurtle.Build(ctx); break;
                case BodyPlan.Jelly: CreatureMeshJelly.Build(ctx); break;
                case BodyPlan.Octopus: CreatureMeshCephalopods.Octopus(ctx); break;
                case BodyPlan.Squid: CreatureMeshCephalopods.Squid(ctx); break;
                case BodyPlan.Cuttlefish: CreatureMeshCephalopods.Cuttlefish(ctx); break;
                case BodyPlan.VampireSquid: CreatureMeshCephalopods.VampireSquid(ctx); break;
                case BodyPlan.Seahorse: CreatureMeshOddballs.Seahorse(ctx); break;
                case BodyPlan.SeaLion: CreatureMeshOddballs.SeaLion(ctx); break;
                case BodyPlan.Sunfish: CreatureMeshOddballs.Sunfish(ctx); break;
                case BodyPlan.Anglerfish: CreatureMeshOddballs.Anglerfish(ctx); break;
                // round 2 (G.1) and round 2b (L.11)
                case BodyPlan.Crab: CreatureMeshCrab.Build(ctx); break;
                case BodyPlan.Starfish: CreatureMeshStarfish.Build(ctx); break;
                case BodyPlan.Dolphin: CreatureMeshMammals.Dolphin(ctx); break;
                case BodyPlan.Whale: CreatureMeshMammals.Whale(ctx); break;
                case BodyPlan.Bird: CreatureMeshBird.Bird(ctx); break;
                case BodyPlan.Bat: CreatureMeshBird.Bat(ctx); break;
                case BodyPlan.Lizard: CreatureMeshLand.Lizard(ctx); break;
                case BodyPlan.Quadruped: CreatureMeshLand.Quadruped(ctx); break;
                case BodyPlan.Primate: CreatureMeshLand.Primate(ctx); break;
                default: CreatureMeshFish.Build(ctx); break;
            }
            return ctx;
        }

        /// True for body plans that never swim (they walk on the seafloor / the ground or fly).
        public static bool IsWalker(BodyPlan p) => p == BodyPlan.Crab || p == BodyPlan.Starfish || p == BodyPlan.Lizard
                                                   || p == BodyPlan.Quadruped || p == BodyPlan.Primate;

        public static bool IsFlyer(BodyPlan p) => p == BodyPlan.Bird || p == BodyPlan.Bat;
    }
}
