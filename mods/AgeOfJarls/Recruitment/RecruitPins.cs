using System;
using System.IO;
using AgeOfJarls.Core;
using UnityEngine;

namespace AgeOfJarls.Recruitment
{
    /// <summary>
    /// Where settlers can be found, on everybody's map: the shore the castaways reached and every camp a captive was
    /// put in. Saved pins (they stay in the player's map between sessions, like the game's own), told to all players
    /// by a routed RPC when they come into being and taken off again when the captive is freed. A machine that joins
    /// later gets the captives' pins from the captives it loads (<see cref="UI.SettlerPins"/>). Pins of the same name
    /// at the same spot are never doubled, whichever way they arrive.
    /// </summary>
    internal static class RecruitPins
    {
        private const string Module = "Recruitment";
        internal const string CastawaysToken = "$aoj_pin_castaways";
        internal const string CaptiveToken = "$aoj_pin_captive";
        /// <summary>Two pins of one name this close are the same place.</summary>
        private const float SameSpot = 6f;

        internal static void Register(ZRoutedRpc rpc) => Net.RoutedRpcs.Register<ZPackage>(rpc, Keys.RpcRecruitPin, RPC_RecruitPin);

        /// <summary>Everybody's map, this machine's included (the game hands a broadcast to its sender too): a pin at the spot, or the one there removed.</summary>
        internal static void Broadcast(string token, Vector3 position, bool on)
        {
            if (ZRoutedRpc.instance == null)
            {
                return;
            }
            var package = new ZPackage();
            package.Write(token);
            package.Write(position);
            package.Write(on);
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, Keys.RpcRecruitPin, package);
        }

        private static void RPC_RecruitPin(long sender, ZPackage package)
        {
            try
            {
                string token = package.ReadString();
                Vector3 position = package.ReadVector3();
                bool on = package.ReadBool();
                // Network boundary: only the mod's own two pins.
                if (token != CastawaysToken && token != CaptiveToken)
                {
                    return;
                }
                Apply(token, position, on);
            }
            catch (Exception e) when (e is IOException || e is ArgumentException)
            {
                Log.Warning(Module, $"Malformed recruit pin from peer {sender}: {e.Message}");
            }
        }

        /// <summary>This machine's map only: the saved pin at the spot, if there is none yet; or the one there removed.</summary>
        internal static void Apply(string token, Vector3 position, bool on)
        {
            Minimap map = Minimap.instance;
            if (map == null || Localization.instance == null)
            {
                return;
            }
            string label = Localization.instance.Localize(token);
            Minimap.PinData existing = Find(map, label, position);
            if (on && existing == null)
            {
                map.AddPin(position, token == CaptiveToken ? Minimap.PinType.Icon0 : Minimap.PinType.Icon3, label, save: true, isChecked: false);
            }
            else if (!on && existing != null)
            {
                map.RemovePin(existing);
            }
        }

        private static Minimap.PinData Find(Minimap map, string label, Vector3 position)
        {
            foreach (Minimap.PinData pin in map.m_pins)
            {
                if (pin != null && pin.m_name == label && Utils.DistanceXZ(pin.m_pos, position) <= SameSpot)
                {
                    return pin;
                }
            }
            return null;
        }
    }
}
