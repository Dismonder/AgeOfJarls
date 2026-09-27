using System;
using System.Linq;
using AgeOfJarls.Core;
using AgeOfJarls.Settlers;
using HarmonyLib;
using UnityEngine;
using Random = UnityEngine.Random;

namespace AgeOfJarls.Recruitment
{
    /// <summary>
    /// Captives in enemy camps. When a vanilla location loads (the same moment for new and long-explored worlds), its
    /// owner decides once - saved in the location's ZDO - whether a camp from the configured list holds a captive
    /// settler. The captive is tied up among a couple of fresh guards from the biome, so freeing it always takes a
    /// fight: with no guard alive nearby, [E] frees it and it follows its rescuer home.
    /// </summary>
    [HarmonyPatch(typeof(LocationProxy), "SpawnLocation")]
    internal static class CaptiveCamps
    {
        private const string Module = "Recruitment";
        private const int Guards = 2;
        private const float GuardSpread = 5f;

        [HarmonyPostfix]
        private static void Postfix(LocationProxy __instance, bool __result)
        {
            try
            {
                if (__result)
                {
                    Decide(__instance);
                }
            }
            catch (Exception e)
            {
                Log.Error(Module, $"Captive camp check failed: {e.Message}");
            }
        }

        private static void Decide(LocationProxy proxy)
        {
            ZNetView view = proxy.m_nview;
            if (view == null || !view.IsValid() || !view.IsOwner() || proxy.m_instance == null || ZNetScene.instance == null)
            {
                return;
            }
            ZDO zdo = view.GetZDO();
            if (zdo.GetInt(Keys.ZdoCampDecided) != 0)
            {
                return;
            }
            string location = Utils.GetPrefabName(proxy.m_instance);
            string[] camps = AoJConfig.CaptiveCampLocations.Value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
            bool isCamp = camps.Any(c => location.IndexOf(c, StringComparison.OrdinalIgnoreCase) >= 0);
            if (!isCamp || Random.value > AoJConfig.CaptiveCampChance.Value)
            {
                zdo.Set(Keys.ZdoCampDecided, 2);
                return;
            }
            zdo.Set(Keys.ZdoCampDecided, 1);

            Vector3 center = proxy.transform.position;
            GameObject settlerPrefab = ZNetScene.instance.GetPrefab(Keys.SettlerPrefab);
            if (settlerPrefab == null)
            {
                return;
            }
            Vector3 spot = Ground(center + new Vector3(Random.Range(-2f, 2f), 0f, Random.Range(-2f, 2f)));
            GameObject captive = UnityEngine.Object.Instantiate(settlerPrefab, spot, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            captive.GetComponent<Settler>()?.MakeCaptive();

            string[] guards = GuardsFor(WorldGenerator.instance != null ? WorldGenerator.instance.GetBiome(center) : Heightmap.Biome.Meadows);
            for (int i = 0; i < Guards; i++)
            {
                GameObject guard = ZNetScene.instance.GetPrefab(guards[i % guards.Length]);
                if (guard != null)
                {
                    Vector2 offset = Random.insideUnitCircle * GuardSpread;
                    UnityEngine.Object.Instantiate(guard, Ground(spot + new Vector3(offset.x, 0f, offset.y)), Quaternion.identity);
                }
            }
            Log.Info(Module, $"A captive waits in {location} at {spot:F0}");
        }

        // From raids.json ("campGuards" by biome name), else the built-in line-up.
        private static string[] GuardsFor(Heightmap.Biome biome)
        {
            if (Core.Defs.DefsRegistry.Current.Raids.CampGuards.TryGetValue(biome.ToString(), out System.Collections.Generic.List<string> configured) &&
                configured.Count > 0)
            {
                return configured.ToArray();
            }
            switch (biome)
            {
                case Heightmap.Biome.BlackForest:
                    return new[] { "Greydwarf", "Greydwarf_Elite" };
                case Heightmap.Biome.Swamp:
                    return new[] { "Draugr", "Draugr_Elite" };
                case Heightmap.Biome.Mountain:
                    return new[] { "Wolf", "Fenring" };
                case Heightmap.Biome.Plains:
                    return new[] { "Goblin", "GoblinBrute" };
                case Heightmap.Biome.Mistlands:
                    return new[] { "Seeker", "SeekerSoldier" };
                case Heightmap.Biome.AshLands:
                    return new[] { "Charred_Melee", "Charred_Archer" };
                default:
                    return new[] { "Greyling", "Greydwarf" };
            }
        }

        private static Vector3 Ground(Vector3 point)
        {
            if (ZoneSystem.instance != null && ZoneSystem.instance.GetGroundHeight(point, out float height))
            {
                point.y = height + 0.2f;
            }
            return point;
        }
    }
}
