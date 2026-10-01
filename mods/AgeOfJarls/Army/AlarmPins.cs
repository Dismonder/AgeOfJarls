using System;
using System.Collections.Generic;
using System.IO;
using AgeOfJarls.Core;
using UnityEngine;

namespace AgeOfJarls.Army
{
    /// <summary>
    /// While a settlement's alarm is on, every player gets a map pin at it - also players far away, where the table is
    /// not loaded: the table's owner tells everybody by a routed RPC when the alarm starts and when it ends. The pins
    /// are temporary: never saved, gone with the session.
    /// </summary>
    internal static class AlarmPins
    {
        private const string Module = "Army";
        private const int MaxNameLength = 40;

        private static readonly Dictionary<long, Minimap.PinData> s_pins = new Dictionary<long, Minimap.PinData>();

        /// <summary>The table's owner: tell every player the alarm started or ended.</summary>
        internal static void Broadcast(long settlementId, Vector3 position, string name, bool on)
        {
            if (ZRoutedRpc.instance == null || settlementId == 0L)
            {
                return;
            }
            var package = new ZPackage();
            package.Write(settlementId);
            package.Write(position);
            package.Write(name ?? "");
            package.Write(on);
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, Keys.RpcAlarmPin, package);
        }

        private static void RPC_AlarmPin(long sender, ZPackage package)
        {
            try
            {
                long id = package.ReadLong();
                Vector3 position = package.ReadVector3();
                string name = TextUtil.SanitizeName(package.ReadString(), MaxNameLength);
                bool on = package.ReadBool();
                Minimap map = Minimap.instance;
                if (map == null)
                {
                    return;
                }
                if (s_pins.TryGetValue(id, out Minimap.PinData old))
                {
                    map.RemovePin(old);
                    s_pins.Remove(id);
                }
                if (on)
                {
                    string label = Localization.instance != null ? Localization.instance.Localize("$aoj_pin_alarm", name) : name;
                    s_pins[id] = map.AddPin(position, Minimap.PinType.Icon0, label, false, false);
                }
            }
            catch (Exception e) when (e is IOException || e is ArgumentException)
            {
                // Network boundary: a malformed package must not break anything else.
                Log.Warning(Module, $"Malformed alarm pin from peer {sender}: {e.Message}");
            }
        }

        /// <summary>A new world session (<see cref="Net.RoutedRpcs"/>): register with its ZRoutedRpc, forget the last session's pins.</summary>
        internal static void OnNewSession(ZRoutedRpc rpc)
        {
            s_pins.Clear();
            Net.RoutedRpcs.Register<ZPackage>(rpc, Keys.RpcAlarmPin, RPC_AlarmPin);
        }
    }
}
