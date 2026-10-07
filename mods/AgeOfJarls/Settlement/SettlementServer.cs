using System;
using System.Collections;
using System.Collections.Generic;
using AgeOfJarls.Core;
using HarmonyLib;
using UnityEngine;

namespace AgeOfJarls.Settlement
{
    /// <summary>
    /// Server only (dedicated or host). Only the server holds every ZDO of the world, so only it can tell for sure that
    /// a settler on a roster no longer exists, e.g. after dying far from home while nobody had its table loaded.
    /// </summary>
    internal sealed class SettlementServer : MonoBehaviour
    {
        private const string Module = "Settlement";
        private const float StartDelaySeconds = 30f;
        private const float IntervalSeconds = 60f;

        private readonly List<ZDO> _tables = new List<ZDO>();
        private readonly List<ZDO> _settlers = new List<ZDO>();
        private readonly HashSet<long> _livingSettlers = new HashSet<long>();

        private IEnumerator Start()
        {
            yield return new WaitForSeconds(StartDelaySeconds);
            while (true)
            {
                if (ZNet.instance != null && ZNet.instance.IsServer() && ZDOMan.instance != null)
                {
                    yield return Scan(Keys.JarlTablePrefab, _tables);
                    yield return Scan(Keys.SettlerPrefab, _settlers);
                    PruneRosters();
                }
                yield return new WaitForSeconds(IntervalSeconds);
            }
        }

        // The vanilla iterator walks ~400 sectors per call; one call per frame keeps the scan unnoticeable
        // (the game connects portals the same way).
        private static IEnumerator Scan(string prefab, List<ZDO> result)
        {
            result.Clear();
            int index = 0;
            while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(prefab, result, ref index))
            {
                yield return null;
            }
        }

        // Settlers are matched by their stable id: ZDOIDs are renumbered on every world load.
        private void PruneRosters()
        {
            _livingSettlers.Clear();
            foreach (ZDO settler in _settlers)
            {
                long uid = settler.GetLong(Keys.ZdoSettlerUid);
                if (uid != 0L)
                {
                    _livingSettlers.Add(uid);
                }
            }

            foreach (ZDO table in _tables)
            {
                long owner = table.GetOwner();
                // Without an owner nobody has the table loaded to apply the change; the next scan retries.
                SettlementData data = owner != 0L ? SettlementData.Read(table) : null;
                if (data == null)
                {
                    continue;
                }

                foreach (RosterEntry settler in data.Settlers)
                {
                    if (!_livingSettlers.Contains(settler.Uid))
                    {
                        Log.Info(Module, $"{settler.Name} no longer exists; asking the table owner to drop it from the roster");
                        JarlTable.SendRemoveSettler(owner, table.m_uid, settler.Uid);
                    }
                }
            }
            _tables.Clear();
            _settlers.Clear();
        }

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.Awake))]
        private static class AttachPatch
        {
            // High priority: a later ZNet.Awake postfix of another mod that throws (seen in the wild) must not skip this.
            [HarmonyPriority(Priority.High)]
            private static void Postfix(ZNet __instance)
            {
                try
                {
                    if (__instance.GetComponent<SettlementServer>() == null)
                    {
                        __instance.gameObject.AddComponent<SettlementServer>();
                    }
                }
                catch (Exception e)
                {
                    Log.Error(Module, $"Cannot start the settlement server loop: {e.Message}");
                }
            }
        }
    }
}
