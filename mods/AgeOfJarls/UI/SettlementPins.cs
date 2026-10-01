using System.Collections.Generic;
using AgeOfJarls.Core;
using AgeOfJarls.Settlement;
using HarmonyLib;
using UnityEngine;

namespace AgeOfJarls.UI
{
    /// <summary>
    /// Every settlement loaded on this machine is drawn on the map: a circle of its radius around the table and one
    /// for each area marked on the map (a warehouse in blue, any other area in green, with its name), the way the
    /// game draws an event's area - pins with a world size, so they scale with the map. Tinted after the game has
    /// painted them; gone with the session, never saved. Only loaded tables: no other settlement's data is here.
    /// </summary>
    internal static class SettlementPins
    {
        private const float RefreshSeconds = 1f;
        private static readonly Color WarehouseColor = new Color(0.4f, 0.7f, 1f, 0.45f);
        private static readonly Color OtherColor = new Color(0.55f, 0.9f, 0.5f, 0.45f);

        private sealed class AreaPin
        {
            internal Minimap.PinData Pin;
            internal Color Color;
            internal bool Seen;
        }

        private static readonly Dictionary<JarlTable, AreaPin> s_tables = new Dictionary<JarlTable, AreaPin>();
        private static readonly Dictionary<long, AreaPin> s_zones = new Dictionary<long, AreaPin>();
        private static readonly List<JarlTable> s_goneTables = new List<JarlTable>();
        private static readonly List<long> s_goneZones = new List<long>();
        private static Minimap s_map;
        private static float s_nextRefresh;
        private static Color s_color = new Color(1f, 0.78f, 0.34f, 0.4f);
        private static string s_colorText;

        private static void Refresh(Minimap map)
        {
            if (map != s_map)
            {
                s_tables.Clear();
                s_zones.Clear();
                s_map = map;
            }
            if (Time.time < s_nextRefresh)
            {
                return;
            }
            s_nextRefresh = Time.time + RefreshSeconds;
            if (!AoJConfig.SettlementAreas.Value)
            {
                RemoveAll(map);
                return;
            }
            bool mayAdd = map.m_visibleIconTypes == null || map.m_visibleIconTypes[(int)Minimap.PinType.EventArea];
            foreach (AreaPin area in s_zones.Values)
            {
                area.Seen = false;
            }

            foreach (JarlTable table in JarlTable.Loaded)
            {
                SettlementData data = table != null ? table.Data : null;
                if (data == null)
                {
                    continue;
                }
                Place(map, s_tables, table, table.transform.position, table.Radius * 2f, "", AreaColor(), mayAdd);
                foreach (SettlementZone zone in data.Zones)
                {
                    Color color = zone.Kind == SettlementZone.KindWarehouse ? WarehouseColor : OtherColor;
                    AreaPin area = Place(map, s_zones, zone.Id, zone.Center, zone.Radius * 2f, zone.Name, color, mayAdd);
                    if (area != null)
                    {
                        area.Seen = true;
                    }
                }
            }

            foreach (KeyValuePair<JarlTable, AreaPin> entry in s_tables)
            {
                if (entry.Key == null)
                {
                    s_goneTables.Add(entry.Key);
                }
            }
            foreach (JarlTable table in s_goneTables)
            {
                map.RemovePin(s_tables[table].Pin);
                s_tables.Remove(table);
            }
            s_goneTables.Clear();
            foreach (KeyValuePair<long, AreaPin> entry in s_zones)
            {
                if (!entry.Value.Seen)
                {
                    s_goneZones.Add(entry.Key);
                }
            }
            foreach (long id in s_goneZones)
            {
                map.RemovePin(s_zones[id].Pin);
                s_zones.Remove(id);
            }
            s_goneZones.Clear();
        }

        private static AreaPin Place<TKey>(Minimap map, Dictionary<TKey, AreaPin> pins, TKey key, Vector3 position, float size, string name, Color color, bool mayAdd)
        {
            if (pins.TryGetValue(key, out AreaPin area))
            {
                Minimap.PinData pin = area.Pin;
                if (pin.m_worldSize != size || pin.m_pos != position)
                {
                    pin.m_worldSize = size;
                    pin.m_pos = position;
                    map.m_pinUpdateRequired = true;
                }
                if (pin.m_name != (name ?? ""))
                {
                    pin.m_name = name ?? "";
                    map.m_pinUpdateRequired = true;
                }
                area.Color = color;
                return area;
            }
            if (!mayAdd)
            {
                return null;
            }
            // AddPin switches a hidden icon type back on: no new pins while the map's filter hides this kind.
            Minimap.PinData fresh = map.AddPin(position, Minimap.PinType.EventArea, name ?? "", save: false, isChecked: false);
            fresh.m_worldSize = size;
            area = new AreaPin { Pin = fresh, Color = color };
            pins[key] = area;
            return area;
        }

        private static void RemoveAll(Minimap map)
        {
            foreach (AreaPin area in s_tables.Values)
            {
                map.RemovePin(area.Pin);
            }
            foreach (AreaPin area in s_zones.Values)
            {
                map.RemovePin(area.Pin);
            }
            s_tables.Clear();
            s_zones.Clear();
        }

        private static Color AreaColor()
        {
            string text = AoJConfig.SettlementAreaColor.Value;
            if (text != s_colorText)
            {
                s_colorText = text;
                if (!ColorUtility.TryParseHtmlString(text, out s_color))
                {
                    s_color = new Color(1f, 0.78f, 0.34f, 0.4f);
                }
            }
            return s_color;
        }

        private static void Tint(AreaPin area)
        {
            if (area.Pin.m_iconElement != null)
            {
                area.Pin.m_iconElement.color = area.Color;
            }
            if (area.Pin.m_NamePinData != null && area.Pin.m_NamePinData.PinNameText != null)
            {
                Color text = area.Color;
                text.a = 1f;
                area.Pin.m_NamePinData.PinNameText.color = text;
            }
        }

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.UpdateMap))]
        private static class RefreshPatch
        {
            private static void Postfix(Minimap __instance)
            {
                if (JarlTable.Loaded.Count > 0 || s_tables.Count > 0 || s_zones.Count > 0)
                {
                    Refresh(__instance);
                }
            }
        }

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.UpdatePins))]
        private static class ColorPatch
        {
            private static void Postfix()
            {
                foreach (AreaPin area in s_tables.Values)
                {
                    Tint(area);
                }
                foreach (AreaPin area in s_zones.Values)
                {
                    Tint(area);
                }
            }
        }
    }
}
