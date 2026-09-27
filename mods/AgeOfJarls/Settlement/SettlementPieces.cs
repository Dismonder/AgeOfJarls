using System;
using System.Linq;
using AgeOfJarls.Army;
using AgeOfJarls.Core;
using AgeOfJarls.Work;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AgeOfJarls.Settlement
{
    /// <summary>
    /// The mod's other buildings, all made from vanilla parts like the Jarl's Table: a Work Totem per job (a banner
    /// with the zone circle), the Settlement Cauldron (the cauldron model holding food instead of being a crafting
    /// station), the Armory (a chest the troop takes gear from) and the War Banner (a post for the troop).
    /// </summary>
    internal static class SettlementPieces
    {
        private const string Module = "Settlement";
        private const string FallbackBanner = "piece_banner01";
        private const string ChestSource = "piece_chest_wood";

        internal static void Register()
        {
            PrefabManager.OnVanillaPrefabsAvailable += OnVanillaPrefabsAvailable;
        }

        private static void OnVanillaPrefabsAvailable()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= OnVanillaPrefabsAvailable;
            int banner = 1;
            foreach (JobType job in JobInfo.All)
            {
                string source = $"piece_banner{banner++:00}";
                TryAdd(Keys.TotemPrefabPrefix + job, () => BuildTotem(job, source), JobInfo.Token(job) + "_totem",
                    "$aoj_totem_desc", ("Wood", 10), ("Stone", 2));
            }
            TryAdd(Keys.CauldronPrefab, BuildCauldron, "$aoj_piece_cauldron", "$aoj_piece_cauldron_desc", ("Wood", 10), ("Stone", 10));
            TryAdd(Keys.ArmoryPrefab, BuildArmory, "$aoj_piece_armory", "$aoj_piece_armory_desc", ("Wood", 15), ("Copper", 2));
            TryAdd(Keys.BannerPrefab, BuildBanner, "$aoj_piece_banner", "$aoj_piece_banner_desc", ("Wood", 6), ("LeatherScraps", 4));
        }

        private static void TryAdd(string name, Func<GameObject> build, string displayName, string description, params (string item, int amount)[] cost)
        {
            try
            {
                GameObject prefab = build();
                if (prefab == null)
                {
                    return;
                }
                var config = new PieceConfig
                {
                    Name = displayName,
                    Description = description,
                    PieceTable = "Hammer",
                    Category = "Misc",
                    CraftingStation = "piece_workbench",
                    Requirements = cost.Select(c => new RequirementConfig(c.item, c.amount, 0, true)).ToArray(),
                };
                PieceManager.Instance.AddPiece(new CustomPiece(prefab, false, config));
                Log.Info(Module, $"Built {name}");
            }
            catch (Exception e)
            {
                Log.Error(Module, $"Building {name} failed: {e}");
            }
        }

        private static GameObject Clone(string name, params string[] sources)
        {
            string source = sources.FirstOrDefault(s => PrefabManager.Instance.GetPrefab(s) != null);
            if (source == null)
            {
                Log.Error(Module, $"None of {string.Join(", ", sources)} exist; {name} is not available");
                return null;
            }
            return PrefabManager.Instance.CreateClonedPrefab(name, source);
        }

        private static GameObject BuildTotem(JobType job, string banner)
        {
            GameObject prefab = Clone(Keys.TotemPrefabPrefix + job, banner, FallbackBanner);
            if (prefab == null)
            {
                return null;
            }
            WorkTotem totem = prefab.AddComponent<WorkTotem>();
            totem.m_job = job;
            totem.m_areaMarker = JarlTablePiece.CloneAreaMarker(prefab);
            return prefab;
        }

        // The cauldron model without its crafting station: a food store settlers eat from.
        private static GameObject BuildCauldron()
        {
            GameObject prefab = Clone(Keys.CauldronPrefab, "piece_cauldron");
            if (prefab == null)
            {
                return null;
            }
            foreach (CraftingStation station in prefab.GetComponentsInChildren<CraftingStation>(true))
            {
                Object.DestroyImmediate(station);
            }
            AddContainer(prefab, "$aoj_piece_cauldron", 4, 2);
            prefab.AddComponent<SettlementCauldron>();
            return prefab;
        }

        private static GameObject BuildArmory()
        {
            GameObject prefab = Clone(Keys.ArmoryPrefab, "piece_chest", ChestSource);
            if (prefab == null)
            {
                return null;
            }
            Container container = prefab.GetComponent<Container>();
            if (container != null)
            {
                container.m_name = "$aoj_piece_armory";
            }
            prefab.AddComponent<Armory>();
            return prefab;
        }

        private static GameObject BuildBanner()
        {
            GameObject prefab = Clone(Keys.BannerPrefab, "piece_banner10", FallbackBanner);
            if (prefab == null)
            {
                return null;
            }
            WarBanner banner = prefab.AddComponent<WarBanner>();
            banner.m_areaMarker = JarlTablePiece.CloneAreaMarker(prefab);
            return prefab;
        }

        private static void AddContainer(GameObject prefab, string name, int width, int height)
        {
            Container template = PrefabManager.Instance.GetPrefab(ChestSource)?.GetComponent<Container>();
            Container container = prefab.GetComponent<Container>() ?? prefab.AddComponent<Container>();
            container.m_name = name;
            container.m_width = width;
            container.m_height = height;
            container.m_privacy = Container.PrivacySetting.Public;
            if (template != null)
            {
                container.m_bkg = template.m_bkg;
                container.m_openEffects = template.m_openEffects;
                container.m_closeEffects = template.m_closeEffects;
            }
        }
    }

    /// <summary>Marks the Settlement Cauldron: settlers eat from it and never store anything else in it.</summary>
    public class SettlementCauldron : MonoBehaviour
    {
        internal static readonly System.Collections.Generic.List<SettlementCauldron> Loaded = new System.Collections.Generic.List<SettlementCauldron>();

        internal Container Container { get; private set; }

        private void Awake()
        {
            Container = GetComponent<Container>();
            ZNetView view = GetComponent<ZNetView>();
            if (view != null && view.GetZDO() != null)
            {
                Loaded.Add(this);
            }
        }

        private void OnDestroy()
        {
            Loaded.Remove(this);
        }

        /// <summary>Servings of food in it.</summary>
        internal int Servings() =>
            Container == null ? 0 : Container.GetInventory().GetAllItems().Where(Settlers.Needs.IsFood).Sum(i => i.m_stack);
    }
}
