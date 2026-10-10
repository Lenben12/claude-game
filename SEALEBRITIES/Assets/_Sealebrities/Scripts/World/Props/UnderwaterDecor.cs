using System.Collections;
using UnityEngine;

namespace Sealebrities
{
    /// Entry point (props engineer): underwater decor for every dive site plus floating site buoys.
    /// Called by WorldBuilder after the terrain exists: "yield return UnderwaterDecor.Build(root)".
    public static class UnderwaterDecor
    {
        /// Decor of a site is only active while the main camera is within this horizontal distance of its center.
        public const float ActivationRange = 550f;

        public static IEnumerator Build(Transform root)
        {
            PropInfo.AnemoneSpots.Clear();
            PropInfo.WreckHoles.Clear();
            PropInfo.WreckHoleNormals.Clear();
            PropInfo.VentPositions.Clear();

            var uw = PropUtil.WorldRoot("UnderwaterDecor", root);
            yield return PropRun.Safe(PropMats.PrewarmUnderwater(), "underwater materials");
            yield return PropRun.Safe(PropCoralKit.Prepare(), "coral variants");

            foreach (var site in DiveSites.All)
            {
                var siteRoot = PropUtil.WorldRoot("Site_" + site.id, uw);
                var decor = PropUtil.WorldRoot("Decor", siteRoot);
                IEnumerator work;
                switch (site.id)
                {
                    case "house_reef": work = PropReef.Build(decor, site); break;
                    case "kelp_forest": work = PropKelp.Build(decor, site); break;
                    case "wreck": work = PropWreck.Build(decor, site); break;
                    case "blue_drop": work = PropDeep.BuildDropOff(decor, site); break;
                    case "deep_trench": work = PropDeep.BuildTrench(decor, site); break;
                    default: work = PropDeep.BuildGeneric(decor, site); break;
                }
                yield return PropRun.Safe(work, "site " + site.id);
                siteRoot.gameObject.AddComponent<PropSiteActivation>().Init(decor.gameObject, site.center, ActivationRange);
                try { CreateBuoy(uw, site); }
                catch (System.Exception e) { Debug.LogError("Sealebrities props: buoy " + site.id + " failed: " + e); }
                if (PropRun.Due()) yield return null;
            }

            // coral variant meshes have been merged into the site chunks; free them
            foreach (var t in new[] { PropCoralType.Branching, PropCoralType.Brain, PropCoralType.Table, PropCoralType.Fan, PropCoralType.Sponge, PropCoralType.Anemone, PropCoralType.Whip })
                foreach (var m in PropCoralKit.Of(t))
                    foreach (var part in m.parts)
                        if (part.mesh != null) Object.Destroy(part.mesh);
            PropCoralKit.Of(PropCoralType.Branching).Clear();
            PropCoralKit.Of(PropCoralType.Brain).Clear();
            PropCoralKit.Of(PropCoralType.Table).Clear();
            PropCoralKit.Of(PropCoralType.Fan).Clear();
            PropCoralKit.Of(PropCoralType.Sponge).Clear();
            PropCoralKit.Of(PropCoralType.Anemone).Clear();
            PropCoralKit.Of(PropCoralType.Whip).Clear();
            PropCoralKit.Ready = false;

            PropInfo.UnderwaterBuilt = true;
        }

        /// Chunky low-poly marker buoy at the site center: orange 8-sided body with a white band, pole, a faceted flag
        /// in the site's map color and a night blinker.
        static void CreateBuoy(Transform parent, DiveSite site)
        {
            var go = new GameObject("Buoy_" + site.id);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(site.center.x, 0f, site.center.z);
            var t = go.transform;

            var body = new PropMeshBuilder();
            body.Lathe(Vector3.zero, Quaternion.identity, new[]
            {
                new Vector2(0f, -0.62f), new Vector2(0.3f, -0.55f), new Vector2(0.5f, -0.2f),
                new Vector2(0.5f, 0.3f), new Vector2(0.28f, 0.56f), new Vector2(0f, 0.64f),
            }, 8, 1f, 1f);
            PropUtil.MeshObject(t, "Body", body.ToMesh("BuoyBody"), PropMats.BuoyOrange, Vector3.zero, Quaternion.identity);

            var band = new PropMeshBuilder();
            band.Cylinder(new Vector3(0f, -0.02f, 0f), Quaternion.identity, 0.515f, 0.515f, 0.24f, 8);
            PropUtil.MeshObject(t, "Band", band.ToMesh("BuoyBand"), PropMats.BuoyWhite, Vector3.zero, Quaternion.identity);

            var pole = new PropMeshBuilder();
            pole.Rod(new Vector3(0f, 0.55f, 0f), new Vector3(0f, 2.15f, 0f), 0.05f, 0.045f, 6, true);
            pole.Torus(new Vector3(0f, 1.75f, 0f), Quaternion.identity, 0.17f, 0.025f, 8, 3);
            pole.Rod(new Vector3(0f, -0.62f, 0f), new Vector3(0f, -5.5f, 0f), 0.025f, 0.025f, 3);
            PropUtil.MeshObject(t, "Pole", pole.ToMesh("BuoyPole"), PropMats.Metal, Vector3.zero, Quaternion.identity);

            var blink = PropMats.EmissiveInstance(new Color(1f, 0.92f, 0.6f), Color.black);
            var bulb = new PropMeshBuilder();
            bulb.Ico(new Vector3(0f, 2.22f, 0f), Vector3.one * 0.09f, Quaternion.identity, 0);
            PropUtil.MeshObject(t, "Blinker", bulb.ToMesh("BuoyBlinker"), blink, Vector3.zero, Quaternion.identity, false);

            // flag: two kinked flat segments, two-sided
            var flagPivot = new GameObject("FlagPivot").transform;
            flagPivot.SetParent(t, false);
            flagPivot.localPosition = new Vector3(0f, 1.98f, 0f);
            var flag = new PropMeshBuilder();
            Vector3 a0 = new Vector3(0.04f, 0f, 0f), a1 = new Vector3(0.04f, -0.46f, 0f);
            Vector3 m0 = new Vector3(0.42f, -0.01f, 0.06f), m1 = new Vector3(0.42f, -0.45f, 0.06f);
            Vector3 e0 = new Vector3(0.78f, -0.03f, 0f), e1 = new Vector3(0.78f, -0.43f, 0f);
            flag.Quad2(a0, m0, m1, a1, Vector3.forward);
            flag.Quad2(m0, e0, e1, m1, Vector3.forward);
            PropUtil.MeshObject(flagPivot, "Flag", flag.ToMesh("BuoyFlag"), PropMats.Flag(site.mapColor), Vector3.zero, Quaternion.identity);

            var b = go.AddComponent<PropBuoy>();
            b.x = site.center.x;
            b.z = site.center.z;
            b.yaw = Mathf.Abs(site.id.GetHashCode() % 360);
            b.flag = flagPivot;
            b.blinker = blink;
        }
    }
}
