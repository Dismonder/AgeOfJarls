using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AgeOfJarls.Core;
using HarmonyLib;
using UnityEngine;

namespace AgeOfJarls.Net
{
    /// <summary>
    /// Items for a player on another machine: a settler's owner takes them out of the settler's bag and sends them to
    /// the asking peer, whose player puts them in its own bag (what does not fit lands at its feet). The parcel is an
    /// inventory package like a gift to a settler, so quality, durability and crafter travel with the item.
    /// </summary>
    internal static class ItemDelivery
    {
        private const string Module = "Net";

        /// <summary>Hands the parcel to the peer's player: straight into the bag on this machine, by a routed RPC elsewhere.</summary>
        internal static void Send(long peer, Inventory parcel, string from)
        {
            var package = new ZPackage();
            package.Write(from ?? "");
            parcel.Save(package);
            if (peer == ZDOMan.GetSessionID())
            {
                Receive(package);
            }
            else if (ZRoutedRpc.instance != null)
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(peer, Keys.RpcReceiveItems, package);
            }
        }

        private static void RPC_ReceiveItems(long sender, ZPackage package) => Receive(package);

        private static void Receive(ZPackage package)
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                return;
            }
            var parcel = new Inventory("aoj_delivery", null, 8, 4);
            string from;
            try
            {
                from = TextUtil.SanitizeName(package.ReadString(), 40);
                parcel.Load(package);
            }
            catch (Exception e) when (e is IOException || e is ArgumentException)
            {
                Log.Warning(Module, $"Malformed item parcel: {e.Message}");
                return;
            }
            var names = new List<string>();
            bool dropped = false;
            foreach (ItemDrop.ItemData item in parcel.GetAllItems().ToList())
            {
                item.m_equipped = false;
                names.Add(item.m_stack > 1 ? $"{item.m_shared.m_name} x{item.m_stack}" : item.m_shared.m_name);
                if (!player.GetInventory().AddItem(item))
                {
                    ItemDrop.DropItem(item, item.m_stack, player.transform.position + Vector3.up * 0.5f + player.transform.forward * 0.3f, Quaternion.identity);
                    dropped = true;
                }
            }
            if (names.Count > 0 && Localization.instance != null)
            {
                player.Message(MessageHud.MessageType.TopLeft,
                    Localization.instance.Localize(dropped ? "$aoj_msg_took_dropped" : "$aoj_msg_took", from, string.Join(", ", names)));
            }
        }

        // Every world session gets a new ZRoutedRpc: register with it.
        [HarmonyPatch(typeof(ZNet), nameof(ZNet.Awake))]
        private static class RegisterPatch
        {
            [HarmonyPriority(Priority.High)]
            private static void Postfix()
            {
                ZRoutedRpc.instance?.Register<ZPackage>(Keys.RpcReceiveItems, RPC_ReceiveItems);
            }
        }
    }
}
