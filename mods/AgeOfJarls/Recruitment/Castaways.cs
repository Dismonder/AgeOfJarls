using System.Collections;
using AgeOfJarls.Core;
using AgeOfJarls.Settlement;
using UnityEngine;

namespace AgeOfJarls.Recruitment
{
    /// <summary>
    /// The first settlers: once per world, shortly after the first Jarl's Table is built, castaways reach the nearest
    /// shore (a gentle start with no fight). Needs no new location, so it works in existing worlds; a world-wide
    /// global key makes sure it happens only once, whoever builds the table.
    /// </summary>
    internal static class Castaways
    {
        private const string Module = "Recruitment";
        private const float DelaySeconds = 30f;
        private const int Count = 2;
        private const float SearchFrom = 20f;
        private const float SearchTo = 200f;
        private const float SearchStep = 10f;
        private const float AngleStep = 15f;

        /// <summary>On the machine that founded the settlement (the table's owner).</summary>
        internal static void OnSettlementFounded(JarlTable table)
        {
            if (!AoJConfig.Castaways.Value || ZoneSystem.instance == null || ZoneSystem.instance.GetGlobalKey(Keys.CastawaysKey))
            {
                return;
            }
            ZoneSystem.instance.SetGlobalKey(Keys.CastawaysKey);
            table.StartCoroutine(Arrive(table));
        }

        private static IEnumerator Arrive(JarlTable table)
        {
            yield return new WaitForSeconds(DelaySeconds);
            if (table == null || ZNetScene.instance == null)
            {
                yield break;
            }
            GameObject prefab = ZNetScene.instance.GetPrefab(Keys.SettlerPrefab);
            if (prefab == null)
            {
                yield break;
            }

            Vector3 shore = FindShore(table.transform.position) ?? table.transform.position + table.transform.forward * table.Radius;
            if (ZoneSystem.instance.GetGroundHeight(shore, out float ground))
            {
                shore.y = ground;
            }
            for (int i = 0; i < Count; i++)
            {
                Vector3 spot = shore + new Vector3(Random.Range(-2f, 2f), 0.3f, Random.Range(-2f, 2f));
                Object.Instantiate(prefab, spot, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            }
            Log.Info(Module, $"{Count} castaways reached the shore at {shore:F0}");
            if (table.NetView != null && table.NetView.IsValid())
            {
                Chronicle.Add(table.NetView, "$aoj_chr_castaways", Count.ToString());
            }
            if (MessageHud.instance != null)
            {
                MessageHud.instance.MessageAll(MessageHud.MessageType.Center, "$aoj_msg_castaways");
            }
            if (Minimap.instance != null)
            {
                Minimap.instance.AddPin(shore, Minimap.PinType.Icon3, Localization.instance.Localize("$aoj_pin_castaways"), true, false);
            }
        }

        // The nearest dry spot next to the sea: ground just above the water level, searched in rings around the table.
        private static Vector3? FindShore(Vector3 center)
        {
            float water = ZoneSystem.instance.m_waterLevel;
            for (float distance = SearchFrom; distance <= SearchTo; distance += SearchStep)
            {
                for (float angle = 0f; angle < 360f; angle += AngleStep)
                {
                    Vector3 point = center + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * distance;
                    if (!ZoneSystem.instance.GetGroundHeight(point, out float height))
                    {
                        continue;
                    }
                    if (height > water + 0.3f && height < water + 2f)
                    {
                        point.y = height;
                        return point;
                    }
                }
            }
            return null;
        }
    }
}
