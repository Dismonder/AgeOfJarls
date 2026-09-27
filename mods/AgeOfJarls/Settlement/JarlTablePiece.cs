using System;
using System.Linq;
using AgeOfJarls.Core;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AgeOfJarls.Settlement
{
    /// <summary>
    /// Builds the Jarl's Table from vanilla parts: a table model, the ward's radius circle and the workbench's
    /// "player base" area (so raids and spawn rules treat a settlement as a base), then adds it to the hammer.
    /// </summary>
    internal static class JarlTablePiece
    {
        private const string Module = "Settlement";
        private static readonly string[] BaseCandidates = { "piece_table_oak", "piece_table" };
        private const string MarkerSource = "guard_stone";
        private const string PlayerBaseSource = "piece_workbench";

        internal static void Register()
        {
            PrefabManager.OnVanillaPrefabsAvailable += OnVanillaPrefabsAvailable;
        }

        private static void OnVanillaPrefabsAvailable()
        {
            // Fires on every main-menu load; the piece is registered once per game session.
            PrefabManager.OnVanillaPrefabsAvailable -= OnVanillaPrefabsAvailable;
            try
            {
                GameObject prefab = Build();
                if (prefab == null)
                {
                    return;
                }

                var config = new PieceConfig
                {
                    Name = "$aoj_piece_jarltable",
                    Description = "$aoj_piece_jarltable_desc",
                    PieceTable = "Hammer",
                    Category = "Misc",
                    CraftingStation = "piece_workbench",
                    Requirements = new[]
                    {
                        new RequirementConfig("Wood", 20, 0, true),
                        new RequirementConfig("Stone", 10, 0, true),
                        new RequirementConfig("DeerHide", 4, 0, true),
                    },
                };
                PieceManager.Instance.AddPiece(new CustomPiece(prefab, false, config));
            }
            catch (Exception e)
            {
                Log.Error(Module, $"Building {Keys.JarlTablePrefab} failed: {e}");
            }
        }

        private static GameObject Build()
        {
            string baseName = BaseCandidates.FirstOrDefault(name => PrefabManager.Instance.GetPrefab(name) != null);
            if (baseName == null)
            {
                Log.Error(Module, $"None of the table prefabs exist: {string.Join(", ", BaseCandidates)}");
                return null;
            }

            GameObject prefab = PrefabManager.Instance.CreateClonedPrefab(Keys.JarlTablePrefab, baseName);
            JarlTable table = prefab.AddComponent<JarlTable>();
            table.m_areaMarker = CloneAreaMarker(prefab);
            AddPlayerBaseArea(prefab);
            Log.Info(Module, $"Built {Keys.JarlTablePrefab} from {baseName}");
            return prefab;
        }

        internal static CircleProjector CloneAreaMarker(GameObject prefab)
        {
            GameObject ward = PrefabManager.Instance.GetPrefab(MarkerSource);
            PrivateArea area = ward != null ? ward.GetComponent<PrivateArea>() : null;
            if (area == null || area.m_areaMarker == null)
            {
                Log.Warning(Module, $"No radius marker on '{MarkerSource}'; the settlement area will not be drawn");
                return null;
            }

            GameObject marker = Object.Instantiate(area.m_areaMarker.gameObject, prefab.transform);
            marker.name = "AoJ_AreaMarker";
            marker.transform.localPosition = Vector3.zero;
            // Active in the prefab so the placement ghost shows it; real tables hide it until hovered.
            marker.SetActive(true);
            return marker.GetComponent<CircleProjector>();
        }

        private static void AddPlayerBaseArea(GameObject prefab)
        {
            GameObject workbench = PrefabManager.Instance.GetPrefab(PlayerBaseSource);
            EffectArea source = workbench != null
                ? workbench.GetComponentsInChildren<EffectArea>(true).FirstOrDefault(a => (a.m_type & EffectArea.Type.PlayerBase) != 0)
                : null;
            if (source == null || source.gameObject == workbench)
            {
                Log.Warning(Module, $"No separate player base area on '{PlayerBaseSource}'; raids will not count the table as a base");
                return;
            }

            GameObject area = Object.Instantiate(source.gameObject, prefab.transform);
            area.name = "AoJ_PlayerBase";
            area.transform.localPosition = Vector3.zero;
            area.GetComponent<EffectArea>().m_type = EffectArea.Type.PlayerBase;
        }
    }
}
