using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Sealebrities
{
    /// Managed (thread-safe) result of building one species: faceted low-poly geometry + tiny color-block images.
    /// Unity objects (Mesh, Texture2D, Material) are created from it on the main thread by CreatureFactory.
    internal sealed class CreatureMeshBake
    {
        public CreatureMeshContext Ctx;
        public CreatureMeshFlat Flat;
        public CreatureMeshImage Skin, Fins, Shell;   // Shell only for turtles

        public static CreatureMeshBake Make(SpeciesDef s)
        {
            var ctx = CreatureMeshPlans.Build(s);
            var bake = new CreatureMeshBake { Ctx = ctx };
            bake.Skin = CreatureMeshPaint.Skin(ctx);
            bake.Fins = CreatureMeshPaint.Fins(ctx);
            if (s.plan == BodyPlan.Turtle) bake.Shell = CreatureMeshPaint.Shell(ctx);
            bake.Flat = CreatureMeshFlat.From(ctx.B, ctx.FacetUv, ctx.Smooth, ctx.Crease);
            return bake;
        }
    }

    /// Per-species lazy bakes. A background thread pre-bakes every species after startup so the first
    /// CreatureFactory.Build / CreatureIcons.Get call on the main thread is cheap.
    internal static class CreatureMeshBakery
    {
        static readonly object Gate = new object();
        static Dictionary<string, Lazy<CreatureMeshBake>> bakes = new Dictionary<string, Lazy<CreatureMeshBake>>();
        static int generation;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            lock (Gate)
            {
                bakes = new Dictionary<string, Lazy<CreatureMeshBake>>();
                generation++;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void StartPrewarm()
        {
            // touch the database on the main thread (it uses Unity color parsing), then bake in the background
            var all = new List<SpeciesDef>(SpeciesDatabase.All);
            int gen = generation;
            try
            {
                var th = new Thread(() =>
                {
                    for (int i = 0; i < all.Count; i++)
                    {
                        if (gen != generation) return;
                        try { Get(all[i]); }
                        catch (Exception) { /* reported when the main thread asks for it */ }
                    }
                }) { IsBackground = true, Name = "SB_CreatureBake", Priority = System.Threading.ThreadPriority.BelowNormal };
                th.Start();
            }
            catch (Exception e)
            {
                Debug.LogWarning("Sealebrities: creature prewarm thread not started: " + e.Message);
            }
        }

        public static CreatureMeshBake Get(SpeciesDef s)
        {
            Lazy<CreatureMeshBake> lazy;
            lock (Gate)
            {
                if (!bakes.TryGetValue(s.id, out lazy))
                {
                    lazy = new Lazy<CreatureMeshBake>(() => CreatureMeshBake.Make(s), LazyThreadSafetyMode.ExecutionAndPublication);
                    bakes[s.id] = lazy;
                }
            }
            return lazy.Value;
        }

        /// Drops a cached bake (e.g. after it threw) so it can be retried.
        public static void Forget(string id)
        {
            lock (Gate) bakes.Remove(id);
        }
    }
}
