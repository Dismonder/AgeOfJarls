using System.Collections.Generic;
using AgeOfJarls.Core;
using Jotunn.Managers;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AgeOfJarls.Settlement
{
    /// <summary>
    /// Work Totems and War Banners are standards that stand anywhere. The vanilla banners they are made of hang on
    /// walls only (Piece.m_notOnFloor): their beam and cloth are lifted onto a wooden pole taken from the vanilla
    /// 2 m pole, which goes into the ground or onto a floor and gives the piece its support.
    /// </summary>
    internal static class FreeStandingBanner
    {
        private const string Module = "Settlement";
        private const string PoleSource = "wood_pole2";

        /// <summary>The vanilla banner is 3 m of cloth under a 1.4 m beam: a bit smaller suits a standard.</summary>
        private const float BannerScale = 0.75f;
        private const float BeamHeight = 3f;
        private const float PoleTop = 3.2f;
        /// <summary>A little into the ground, where WearNTear looks for what holds the piece up.</summary>
        private const float PoleBottom = -0.05f;
        private const float PoleThickness = 0.22f;
        /// <summary>The cloth hangs in front of the pole instead of through it.</summary>
        private const float ClothOffset = 0.14f;
        private const float MinHealth = 200f;

        /// <summary>Rebuilds a cloned vanilla wall banner as a standard; false (and unchanged) if its parts are missing.</summary>
        internal static bool Apply(GameObject prefab)
        {
            Transform beam = prefab.transform.Find("woodbeam");
            Transform cloth = prefab.transform.Find("default");
            GameObject poleSource = PrefabManager.Instance.GetPrefab(PoleSource);
            Transform poleMesh = poleSource != null ? poleSource.transform.Find("New") : null;
            if (beam == null || cloth == null || poleMesh == null)
            {
                Log.Warning(Module, $"{prefab.name}: banner parts or {PoleSource} not found, it stays a wall banner");
                return false;
            }

            var banner = new GameObject("banner") { layer = prefab.layer };
            banner.transform.SetParent(prefab.transform, false);
            banner.transform.localPosition = new Vector3(ClothOffset, BeamHeight, 0f);
            banner.transform.localScale = Vector3.one * BannerScale;
            beam.SetParent(banner.transform, false);
            cloth.SetParent(banner.transform, false);

            // Something to aim at over the whole cloth, so [E] opens the window; on piece_nonsolid, people walk through.
            MeshFilter clothMesh = cloth.GetComponent<MeshFilter>();
            if (clothMesh != null && clothMesh.sharedMesh != null)
            {
                Bounds bounds = clothMesh.sharedMesh.bounds;
                var aim = new GameObject("cloth_collider") { layer = LayerMask.NameToLayer("piece_nonsolid") };
                aim.transform.SetParent(cloth, false);
                BoxCollider box = aim.AddComponent<BoxCollider>();
                box.center = bounds.center;
                box.size = new Vector3(Mathf.Max(bounds.size.x, 0.05f), bounds.size.y, bounds.size.z);
            }

            // The cube mesh of the vanilla pole lies along its x axis, turned upright; the collider follows the mesh.
            GameObject pole = Object.Instantiate(poleMesh.gameObject, prefab.transform, false);
            pole.name = "pole";
            pole.transform.localPosition = new Vector3(0f, (PoleTop + PoleBottom) / 2f, 0f);
            pole.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            pole.transform.localScale = new Vector3(PoleTop - PoleBottom, PoleThickness, PoleThickness);
            pole.AddComponent<BoxCollider>();

            // Drawn and culled with the banner.
            LODGroup lodGroup = prefab.GetComponent<LODGroup>();
            Renderer poleRenderer = pole.GetComponent<Renderer>();
            if (lodGroup != null && poleRenderer != null)
            {
                LOD[] lods = lodGroup.GetLODs();
                if (lods.Length > 0)
                {
                    lods[0].renderers = new List<Renderer>(lods[0].renderers) { poleRenderer }.ToArray();
                    lodGroup.SetLODs(lods);
                    lodGroup.RecalculateBounds();
                }
            }

            // Stands on the ground or a floor, and anywhere: a totem goes where the work is, far from any workbench.
            Piece piece = prefab.GetComponent<Piece>();
            if (piece != null)
            {
                piece.m_notOnFloor = false;
                piece.m_craftingStation = null;
            }
            // A pole in the ground takes more than a cloth on a wall; sieges go for these.
            WearNTear wear = prefab.GetComponent<WearNTear>();
            if (wear != null)
            {
                wear.m_health = Mathf.Max(wear.m_health, MinHealth);
            }
            return true;
        }
    }
}
