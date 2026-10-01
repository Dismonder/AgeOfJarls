using System.Collections.Generic;
using AgeOfJarls.Core;
using AgeOfJarls.Settlers;
using HarmonyLib;
using UnityEngine;

namespace AgeOfJarls.UI
{
    /// <summary>
    /// Every settler loaded on this machine is on the map like another player - the player icon with its name under
    /// it, in the mod's own colour (UI/SettlerPinColor) so it is not taken for a player. The pins are temporary, like
    /// the players' own: never saved, gone with the session, moved every half second, and hidden with the players
    /// by the map's icon filter. Settlers farther than the loaded zones are not on the map: nothing on this machine
    /// knows where they are, unlike the players, whose positions the server tells everybody.
    /// </summary>
    internal static class SettlerPins
    {
        private const float RefreshSeconds = 0.5f;

        private static readonly Dictionary<Settler, Minimap.PinData> s_pins = new Dictionary<Settler, Minimap.PinData>();
        private static readonly List<Settler> s_gone = new List<Settler>();
        private static Minimap s_map;
        private static float s_nextRefresh;
        private static Color s_color = Color.white;
        private static string s_colorText;

        private static void Refresh(Minimap map)
        {
            if (map != s_map)
            {
                // A new session has a new map: the last session's pins went with its map.
                s_pins.Clear();
                s_map = map;
            }
            if (Time.time < s_nextRefresh)
            {
                return;
            }
            s_nextRefresh = Time.time + RefreshSeconds;

            if (!AoJConfig.SettlerPins.Value)
            {
                RemoveAll(map);
                return;
            }
            foreach (Settler settler in Settler.Loaded)
            {
                if (settler == null || settler.Identity == null || !settler.IsLoaded)
                {
                    continue;
                }
                string name = settler.DisplayName;
                Vector3 position = settler.transform.position;
                if (s_pins.TryGetValue(settler, out Minimap.PinData pin))
                {
                    if (pin.m_pos != position)
                    {
                        pin.m_pos = position;
                        map.m_pinUpdateRequired = true;
                    }
                    if (pin.m_name != name)
                    {
                        pin.m_name = name;
                        map.m_pinUpdateRequired = true;
                    }
                    continue;
                }
                // AddPin switches a hidden icon type back on: no new pins while the map's player filter is off.
                if (map.m_visibleIconTypes != null && !map.m_visibleIconTypes[(int)Minimap.PinType.Player])
                {
                    continue;
                }
                s_pins[settler] = map.AddPin(position, Minimap.PinType.Player, name, save: false, isChecked: false);
            }
            foreach (KeyValuePair<Settler, Minimap.PinData> entry in s_pins)
            {
                if (entry.Key == null || !entry.Key.IsLoaded)
                {
                    s_gone.Add(entry.Key);
                }
            }
            foreach (Settler settler in s_gone)
            {
                map.RemovePin(s_pins[settler]);
                s_pins.Remove(settler);
            }
            s_gone.Clear();
        }

        private static void RemoveAll(Minimap map)
        {
            foreach (Minimap.PinData pin in s_pins.Values)
            {
                map.RemovePin(pin);
            }
            s_pins.Clear();
        }

        private static Color PinColor()
        {
            string text = AoJConfig.SettlerPinColor.Value;
            if (text != s_colorText)
            {
                s_colorText = text;
                if (!ColorUtility.TryParseHtmlString(text, out s_color))
                {
                    s_color = new Color(1f, 0.78f, 0.34f);
                }
            }
            return s_color;
        }

        // The map walks every pin each frame it redraws them; the settlers' are tinted after it.
        [HarmonyPatch(typeof(Minimap), nameof(Minimap.UpdateMap))]
        private static class RefreshPatch
        {
            private static void Postfix(Minimap __instance)
            {
                if (Settler.Loaded.Count > 0 || s_pins.Count > 0)
                {
                    Refresh(__instance);
                }
            }
        }

        /// <summary>The game paints every pin white (grey when shared) on each redraw: ours get their colour back.</summary>
        [HarmonyPatch(typeof(Minimap), nameof(Minimap.UpdatePins))]
        private static class ColorPatch
        {
            private static void Postfix()
            {
                if (s_pins.Count == 0)
                {
                    return;
                }
                Color color = PinColor();
                foreach (Minimap.PinData pin in s_pins.Values)
                {
                    if (pin.m_iconElement != null)
                    {
                        pin.m_iconElement.color = color;
                    }
                    if (pin.m_NamePinData != null && pin.m_NamePinData.PinNameText != null)
                    {
                        pin.m_NamePinData.PinNameText.color = color;
                    }
                }
            }
        }
    }
}
