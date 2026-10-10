using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Sealebrities
{
    /// Managed (thread-safe) result of building one species: geometry + painted pixel data.
    /// Unity objects (Mesh, Texture2D, Material) are created from it on the main thread by CreatureFactory.
    internal sealed class CreatureMeshBake
    {
        public CreatureMeshContext Ctx;
        public int SkinW, SkinH, FinW, FinH, ShellW, ShellH;
        public Color32[] Skin, SkinNormal, Fins, FinNormal, Shell, ShellNormal;
        public bool SkinHasAlpha;

        public bool HasPixels => Skin != null && Fins != null;

        /// Drops the pixel arrays once the GPU textures exist (geometry is kept for icons / rebuilds).
        public void ReleasePixels()
        {
            Skin = SkinNormal = Fins = FinNormal = Shell = ShellNormal = null;
        }

        public static CreatureMeshBake Make(SpeciesDef s)
        {
            var ctx = CreatureMeshPlans.Build(s);
            var bake = new CreatureMeshBake { Ctx = ctx };
            bool fineStripes = s.pattern == PatternType.HorizontalStripes || (s.pattern == PatternType.VerticalBands && (s.variant == "lionfish" || s.variant == "barracuda"));
            bool big = s.maxCm >= 300f || fineStripes;

            CreatureMeshImage skin;
            if (ctx.Layout == CreatureMeshLayout.Bell) { skin = CreatureMeshPaint.Bell(ctx, 256, 128); bake.SkinHasAlpha = true; }
            else if (ctx.Layout == CreatureMeshLayout.Disc) skin = CreatureMeshPaint.Skin(ctx, big ? 512 : 256, big ? 512 : 256);
            else skin = CreatureMeshPaint.Skin(ctx, big ? 512 : 256, big ? 256 : 128);
            bake.SkinW = skin.W; bake.SkinH = skin.H;
            bake.Skin = skin.ToColor32();
            CreatureMeshPaint.SkinTexelSize(ctx, skin.W, skin.H, out float tu, out float tv);
            if (ctx.Layout == CreatureMeshLayout.Bell) { tu = 3.1f / skin.W; tv = 0.6f / skin.H; }
            bake.SkinNormal = skin.NormalMap(SkinSlope(s.plan), tu, tv);

            int fw = big ? 256 : 128;
            var fins = CreatureMeshPaint.Fins(ctx, fw, fw);
            bake.FinW = fins.W; bake.FinH = fins.H;
            bake.Fins = fins.ToColor32();
            bake.FinNormal = fins.NormalMap(0.45f, 0.25f / fw, 0.4f / (fw * 0.25f));

            if (s.plan == BodyPlan.Turtle)
            {
                var shell = CreatureMeshPaint.Shell(ctx, 256, 256);
                bake.ShellW = shell.W; bake.ShellH = shell.H;
                bake.Shell = shell.ToColor32();
                bake.ShellNormal = shell.NormalMap(0.6f, 0.5f / 256f, 0.64f / 128f);
            }
            return bake;
        }

        static float SkinSlope(BodyPlan p)
        {
            switch (p)
            {
                case BodyPlan.Fish: return 0.55f;
                case BodyPlan.Shark: return 0.3f;
                case BodyPlan.Ray: return 0.35f;
                case BodyPlan.Eel: return 0.38f;
                case BodyPlan.Turtle: return 0.75f;
                case BodyPlan.Jelly: return 0.25f;
                case BodyPlan.Octopus: return 0.75f;
                case BodyPlan.Squid:
                case BodyPlan.Cuttlefish: return 0.45f;
                case BodyPlan.VampireSquid: return 0.35f;
                case BodyPlan.Seahorse: return 0.5f;
                case BodyPlan.SeaLion: return 0.3f;
                case BodyPlan.Sunfish: return 0.6f;
                case BodyPlan.Anglerfish: return 0.65f;
                default: return 0.45f;
            }
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
