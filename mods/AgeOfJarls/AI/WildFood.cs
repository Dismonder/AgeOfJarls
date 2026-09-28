using System.Collections.Generic;
using AgeOfJarls.Settlers;
using HarmonyLib;
using UnityEngine;

namespace AgeOfJarls.AI
{
    /// <summary>
    /// Wild plants loaded on this machine whose pick is food (berries, mushrooms...), for a hungry settler whose
    /// settlement has nothing to eat. Crops a player planted are left out: they are the farmers' and the players' own.
    /// Kept as a list rather than found by a physics query, which in a large base fills up with building pieces.
    /// </summary>
    [HarmonyPatch(typeof(Pickable), "Awake")]
    internal static class WildFood
    {
        private const int PruneAbove = 512;
        private static readonly List<Pickable> s_plants = new List<Pickable>();

        private static bool s_failed;

        [HarmonyPostfix]
        private static void Postfix(Pickable __instance)
        {
            try
            {
                if (__instance.m_nview == null || __instance.m_nview.GetZDO() == null)
                {
                    return;
                }
                ItemDrop item = __instance.m_itemPrefab != null ? __instance.m_itemPrefab.GetComponent<ItemDrop>() : null;
                if (item == null || !Needs.IsFood(item.m_itemData) ||
                    Jobs.FarmerJob.Crops().ContainsKey(Utils.GetPrefabName(__instance.gameObject)))
                {
                    return;
                }
                if (s_plants.Count > PruneAbove)
                {
                    // Plants of unloaded zones are destroyed objects by now.
                    s_plants.RemoveAll(plant => plant == null);
                }
                s_plants.Add(__instance);
            }
            catch (System.Exception e)
            {
                // Runs for every plant the game creates: whatever goes wrong here must not break that, nor flood the log.
                if (!s_failed)
                {
                    s_failed = true;
                    Core.Log.Error("AI", $"Wild food list failed, foraging may miss plants: {e}");
                }
            }
        }

        /// <summary>The one nearest to <paramref name="from"/> that is ready to pick, at most <paramref name="range"/> from <paramref name="center"/>.</summary>
        internal static Pickable Nearest(Vector3 from, Vector3 center, float range)
        {
            Pickable best = null;
            float bestSqr = float.MaxValue;
            for (int i = s_plants.Count - 1; i >= 0; i--)
            {
                Pickable plant = s_plants[i];
                if (plant == null)
                {
                    s_plants.RemoveAt(i);
                    continue;
                }
                Vector3 position = plant.transform.position;
                if (Utils.DistanceXZ(position, center) > range || !plant.CanBePicked())
                {
                    continue;
                }
                float sqr = (position - from).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    best = plant;
                    bestSqr = sqr;
                }
            }
            return best;
        }
    }
}
