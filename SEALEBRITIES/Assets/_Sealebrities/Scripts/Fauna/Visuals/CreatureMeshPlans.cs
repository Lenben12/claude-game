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
                default: CreatureMeshFish.Build(ctx); break;
            }
            return ctx;
        }
    }
}
