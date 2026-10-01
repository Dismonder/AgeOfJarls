using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AgeOfJarls.Core;
using AgeOfJarls.Settlers;
using UnityEngine;

namespace AgeOfJarls.Net
{
    /// <summary>
    /// Items out of a settler's bag for a player. On the settler's own machine they go straight into the bag; for a
    /// player elsewhere the owner sends them with an RPC on the settler itself, aimed at that peer, whose player puts
    /// them in its bag (what does not fit lands at its feet) and answers that they arrived. Until that answer the
    /// owner keeps the parcel: a peer that never answers (gone, a parcel its mod could not read) gets nothing, and the
    /// items go back into the settler's bag instead of vanishing. The parcel is an inventory package, like a gift to
    /// a settler, so quality, durability and crafter travel with the item.
    /// </summary>
    internal static class ItemDelivery
    {
        private const string Module = "Net";
        /// <summary>How long the owner waits for the answer. A routed RPC takes well under a second.</summary>
        private const float AnswerSeconds = 6f;
        private const int MaxNamesInMessage = 6;

        private sealed class Pending
        {
            internal long Id;
            internal long Peer;
            internal Settler Settler;
            internal string From;
            internal Vector3 Position;
            internal Inventory Parcel;
            internal float Deadline;
        }

        private static readonly List<Pending> s_pending = new List<Pending>();
        private static long s_nextId;

        /// <summary>The settler's owner: hands the parcel to the peer's player.</summary>
        internal static void Send(Settler settler, long peer, Inventory parcel)
        {
            if (settler == null || parcel == null || parcel.NrOfItems() == 0)
            {
                return;
            }
            var package = new ZPackage();
            package.Write(settler.DisplayName ?? "");
            parcel.Save(package);
            if (peer == ZDOMan.GetSessionID())
            {
                // The same package is read back: a ZPackage reads from where its writing stopped.
                package.SetPos(0);
                if (!Receive(package))
                {
                    settler.TakeBackParcel(parcel);
                }
                return;
            }
            long id = ++s_nextId;
            s_pending.Add(new Pending
            {
                Id = id,
                Peer = peer,
                Settler = settler,
                From = settler.DisplayName,
                Position = settler.transform.position,
                Parcel = parcel,
                Deadline = Time.time + AnswerSeconds,
            });
            settler.SendParcel(peer, id, package);
        }

        /// <summary>The asking peer: the parcel into the local player's bag. False when nothing was taken out of it.</summary>
        internal static bool Receive(ZPackage package)
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                return false;
            }
            var parcel = new Inventory("aoj_delivery", null, 8, 8);
            string from;
            try
            {
                from = TextUtil.SanitizeName(package.ReadString(), 40);
                parcel.Load(package);
            }
            catch (Exception e) when (e is IOException || e is ArgumentException)
            {
                Log.Warning(Module, $"Malformed item parcel: {e.Message}");
                return false;
            }
            List<ItemDrop.ItemData> items = parcel.GetAllItems().ToList();
            if (items.Count == 0)
            {
                return false;
            }
            var names = new List<string>();
            bool dropped = false;
            foreach (ItemDrop.ItemData item in items)
            {
                item.m_equipped = false;
                if (names.Count < MaxNamesInMessage)
                {
                    names.Add(item.m_stack > 1 ? $"{item.m_shared.m_name} x{item.m_stack}" : item.m_shared.m_name);
                }
                if (!player.GetInventory().AddItem(item))
                {
                    ItemDrop.DropItem(item, item.m_stack, player.transform.position + Vector3.up * 0.5f + player.transform.forward * 0.3f, Quaternion.identity);
                    dropped = true;
                }
            }
            if (Localization.instance != null)
            {
                string list = string.Join(", ", names) + (items.Count > names.Count ? ", ..." : "");
                player.Message(MessageHud.MessageType.TopLeft,
                    Localization.instance.Localize(dropped ? "$aoj_msg_took_dropped" : "$aoj_msg_took", from, list));
            }
            return true;
        }

        /// <summary>The owner: the peer's player has the parcel, forget it.</summary>
        internal static void Acknowledged(long id, long peer)
        {
            int at = s_pending.FindIndex(p => p.Id == id && p.Peer == peer);
            if (at >= 0)
            {
                s_pending.RemoveAt(at);
            }
        }

        /// <summary>Every frame (Plugin): parcels nobody confirmed in time go back where they came from.</summary>
        internal static void Update()
        {
            if (s_pending.Count == 0)
            {
                return;
            }
            float now = Time.time;
            for (int i = s_pending.Count - 1; i >= 0; i--)
            {
                Pending pending = s_pending[i];
                if (now < pending.Deadline)
                {
                    continue;
                }
                s_pending.RemoveAt(i);
                Return(pending);
            }
        }

        // Back into the settler's bag; on the ground where it stood when the settler is gone from this machine (zone
        // unloaded) or owned elsewhere by now.
        private static void Return(Pending pending)
        {
            string where;
            if (pending.Settler != null && pending.Settler.TakeBackParcel(pending.Parcel))
            {
                where = "back in its bag";
            }
            else if (ZNetScene.instance != null)
            {
                foreach (ItemDrop.ItemData item in pending.Parcel.GetAllItems().ToList())
                {
                    ItemDrop.DropItem(item, item.m_stack, pending.Position + Vector3.up, Quaternion.identity);
                }
                where = "dropped where it stood";
            }
            else
            {
                where = "lost, the world is gone";
            }
            Log.Warning(Module, $"Peer {pending.Peer} did not confirm the {pending.Parcel.NrOfItems()} items from {pending.From} within {AnswerSeconds:0} s: {where}");
        }
    }
}
