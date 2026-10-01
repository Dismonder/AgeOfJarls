using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AgeOfJarls.Settlement
{
    /// <summary>
    /// The settlement's map of its chests: for every chest, what it holds (by item and by kind) and how much room it
    /// has, read once per change of the chest instead of by every settler looking for a chest every frame. The game
    /// itself keeps every machine's copy of a chest's inventory current (Container reloads it from the ZDO within a
    /// second of a change, owner or not), so the map is the same on every machine: whoever simulates a settler, it
    /// knows where things lie. Entries follow the chest's own load revision.
    /// </summary>
    internal static class ChestIndex
    {
        private const int MaxEntries = 1024;

        private static readonly Dictionary<ZDOID, ChestSummary> s_summaries = new Dictionary<ZDOID, ChestSummary>();

        internal sealed class ChestSummary
        {
            internal uint Revision = uint.MaxValue;
            /// <summary>Stacks held: item name, quality, world level, size.</summary>
            internal readonly List<Stack> Stacks = new List<Stack>();
            /// <summary>Items held, by shared name.</summary>
            internal readonly HashSet<string> Items = new HashSet<string>();
            /// <summary>Kinds held (see SettlementStorage.KindOf); "" for things of no kind.</summary>
            internal readonly HashSet<string> Kinds = new HashSet<string>();
            internal int EmptySlots;
            internal int UsedSlots;

            internal bool IsEmpty => Stacks.Count == 0;
        }

        internal struct Stack
        {
            internal string Name;
            /// <summary>See SettlementStorage.KindOf; null for things of no kind.</summary>
            internal string Kind;
            internal int Quality;
            internal int WorldLevel;
            internal int Size;
        }

        /// <summary>The chest's summary, rebuilt when its inventory changed since the last look.</summary>
        internal static ChestSummary Of(Container chest)
        {
            ZDOID id = chest.m_nview.GetZDO().m_uid;
            if (!s_summaries.TryGetValue(id, out ChestSummary summary))
            {
                if (s_summaries.Count >= MaxEntries)
                {
                    s_summaries.Clear();
                }
                summary = new ChestSummary();
                s_summaries[id] = summary;
            }
            // The owner changes the inventory directly and saves it (the revision moves with it); other machines
            // load it from the ZDO, and their revision moves when they do - either way the summary follows the copy.
            uint revision = chest.m_lastRevision;
            if (summary.Revision == revision && revision != uint.MaxValue)
            {
                return summary;
            }
            Rebuild(summary, chest.GetInventory(), revision);
            return summary;
        }

        /// <summary>After this machine itself changed the chest (stored into it): the summary is stale at once.</summary>
        internal static void Touch(Container chest)
        {
            if (chest != null && chest.m_nview != null && chest.m_nview.IsValid() && s_summaries.TryGetValue(chest.m_nview.GetZDO().m_uid, out ChestSummary summary))
            {
                summary.Revision = uint.MaxValue;
            }
        }

        private static void Rebuild(ChestSummary summary, Inventory inventory, uint revision)
        {
            summary.Revision = revision;
            summary.Stacks.Clear();
            summary.Items.Clear();
            summary.Kinds.Clear();
            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
            {
                string kind = SettlementStorage.KindOf(item);
                summary.Stacks.Add(new Stack { Name = item.m_shared.m_name, Kind = kind, Quality = item.m_quality, WorldLevel = item.m_worldLevel, Size = item.m_stack });
                summary.Items.Add(item.m_shared.m_name);
                summary.Kinds.Add(kind ?? "");
            }
            summary.EmptySlots = inventory.GetEmptySlots();
            summary.UsedSlots = inventory.NrOfItems();
        }

        /// <summary>For the chest's hover text: the kinds it holds, as localization tokens, most first.</summary>
        internal static List<string> KindTokens(Container chest)
        {
            ChestSummary summary = Of(chest);
            var counts = new Dictionary<string, int>();
            foreach (Stack stack in summary.Stacks)
            {
                string kind = stack.Kind ?? "";
                counts[kind] = (counts.TryGetValue(kind, out int count) ? count : 0) + stack.Size;
            }
            return counts.OrderByDescending(c => c.Value)
                .Select(c => c.Key.Length > 0 ? SettlementStorage.KindToken(c.Key) : "$aoj_kind_other")
                .ToList();
        }
    }
}
