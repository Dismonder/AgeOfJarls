using System;
using System.Collections.Generic;
using System.Linq;
using AgeOfJarls.Core;
using AgeOfJarls.Core.Defs;
using UnityEngine;

namespace AgeOfJarls.Settlement
{
    /// <summary>
    /// Where settlers put things away. Chests stay sorted: an item goes where the same item already lies, then where
    /// its kind lies (wood with wood, hides with hides), then into an empty chest; never into a chest that holds
    /// other things only. Chests that only hold the item or its kind win over mixed ones, the nearest over the rest.
    /// A member can give a chest a kind ([Shift+E] on it, <see cref="ChestKinds"/>): it then takes that kind only, and
    /// before any other chest but the Settlement Cauldron.
    /// </summary>
    internal static class SettlementStorage
    {
        private const string Module = "Storage";

        private const int RankCauldron = 0;
        private const int RankAssigned = 1;
        private const int RankSameItem = 2;
        private const int RankSameItemMixed = 3;
        private const int RankSameKind = 4;
        private const int RankSameKindMixed = 5;
        private const int RankEmpty = 6;
        private const int RankNone = -1;

        /// <summary>Kinds without a list in storage.json: told by the item type (see <see cref="KindOf"/>).</summary>
        private static readonly string[] BuiltInKinds = { "food", "trophy", "consumable", "gear" };

        /// <summary>The kinds a chest can be given, storage.json's first; "" = anything (sorted by what it holds).</summary>
        internal static List<string> ChestKinds()
        {
            var kinds = new List<string>(DefsRegistry.Current.Storage.Kinds.Keys);
            kinds.AddRange(BuiltInKinds.Where(k => !kinds.Contains(k)));
            return kinds;
        }

        /// <summary>The kind a member gave this chest, or "" (sorted by what it holds).</summary>
        internal static string AssignedKind(Container chest) =>
            chest != null && chest.m_nview != null && chest.m_nview.IsValid() ? chest.m_nview.GetZDO().GetString(Keys.ZdoChestKind) : "";

        /// <summary>"" -> the first kind -> ... -> the last kind -> "".</summary>
        internal static string NextKind(string current)
        {
            List<string> kinds = ChestKinds();
            int index = kinds.IndexOf(current ?? "");
            return index + 1 < kinds.Count ? kinds[index + 1] : "";
        }

        /// <summary>Localization token of a chest kind ("" = anything).</summary>
        internal static string KindToken(string kind) => string.IsNullOrEmpty(kind) ? "$aoj_kind_auto" : "$aoj_kind_" + kind;

        private static readonly List<Piece> s_pieces = new List<Piece>();

        // Item prefab → kind, from storage.json (the server's copy in multiplayer, so both machines sort alike).
        private static readonly Dictionary<string, string> s_kinds = new Dictionary<string, string>();
        private static DefsBundle s_kindsSource;

        private static Dictionary<string, string> KindTable()
        {
            DefsBundle defs = DefsRegistry.Current;
            if (s_kindsSource != defs)
            {
                s_kindsSource = defs;
                s_kinds.Clear();
                foreach (KeyValuePair<string, List<string>> kind in defs.Storage.Kinds)
                {
                    foreach (string item in kind.Value)
                    {
                        s_kinds[item] = kind.Key;
                    }
                }
            }
            return s_kinds;
        }

        /// <summary>What a settler puts away: everything but the gear it uses and its ammunition.</summary>
        internal static bool IsStorable(ItemDrop.ItemData item) =>
            item != null && !item.m_equipped &&
            item.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Ammo &&
            item.m_shared.m_itemType != ItemDrop.ItemData.ItemType.AmmoNonEquipable;

        /// <summary>A chest this close to a Hauler's totem is its input chest: haulers empty it, nobody fills it.</summary>
        internal const float InputChestRange = 4f;

        /// <summary>
        /// The settlement's public chests: no carts, ships, personal chests, the Obliterator, the Armory or a Hauler's
        /// input chest. Settlement Cauldrons are in the list but only ever take food.
        /// </summary>
        internal static void CollectChests(Vector3 center, float radius, List<Container> chests)
        {
            chests.Clear();
            AddChests(center, radius, chests);
        }

        /// <summary>The settlement's chests: inside the table's radius and inside every area marked on its map.</summary>
        internal static void CollectChests(Vector3 center, float radius, SettlementData data, List<Container> chests)
        {
            chests.Clear();
            AddChests(center, radius, chests);
            if (data == null)
            {
                return;
            }
            foreach (SettlementZone zone in data.Zones)
            {
                AddChests(zone.Center, zone.Radius, chests);
            }
        }

        private static void AddChests(Vector3 center, float radius, List<Container> chests)
        {
            s_pieces.Clear();
            Piece.GetAllPiecesInRadius(center, radius, s_pieces);
            foreach (Piece piece in s_pieces)
            {
                // Carts and ships keep their container on a child object, so only root containers count.
                Container chest = piece.GetComponent<Container>();
                if (chest != null && chest.m_privacy == Container.PrivacySetting.Public &&
                    piece.GetComponent<Incinerator>() == null && piece.GetComponent<Army.Armory>() == null &&
                    chest.m_nview != null && chest.m_nview.IsValid() && !IsInputChest(chest) && !chests.Contains(chest))
                {
                    chests.Add(chest);
                }
            }
            s_pieces.Clear();
        }

        /// <summary>
        /// The Obliterator, an Armory, a Settlement Cauldron: chests with a purpose of their own, never a Hauler's input
        /// chest (a hauler would empty the troop's gear or the settlement's food into the chests).
        /// </summary>
        internal static bool HasOwnPurpose(Container chest) =>
            chest.GetComponent<Incinerator>() != null || chest.GetComponent<Army.Armory>() != null || IsCauldron(chest);

        internal static bool IsInputChest(Container chest)
        {
            if (HasOwnPurpose(chest))
            {
                return false;
            }
            foreach (Work.WorkTotem totem in Work.WorkTotem.Loaded)
            {
                if (totem.Job == Work.JobType.Hauler && Vector3.Distance(totem.transform.position, chest.transform.position) <= InputChestRange)
                {
                    return true;
                }
            }
            return false;
        }

        internal static bool IsCauldron(Container chest)
        {
            foreach (SettlementCauldron cauldron in SettlementCauldron.Loaded)
            {
                if (cauldron != null && cauldron.Container == chest)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Owner of the chest only (see ChestAccess): moves wanted items from the chest into the settler's bag, as much as
        /// fits, at most <paramref name="maxItems"/>. Returns the number of items moved.
        /// </summary>
        internal static int TakeFrom(Container chest, Inventory bag, Predicate<ItemDrop.ItemData> wanted, int maxItems)
        {
            if (!IsUsable(chest) || !chest.m_nview.IsOwner())
            {
                return 0;
            }
            chest.Load();
            Inventory contents = chest.GetInventory();
            int moved = 0;
            foreach (ItemDrop.ItemData item in contents.GetAllItems().ToArray())
            {
                if (moved >= maxItems || !wanted(item))
                {
                    continue;
                }
                int amount = Mathf.Min(item.m_stack, FreeSpace(bag, item), maxItems - moved);
                if (amount <= 0)
                {
                    continue;
                }
                ItemDrop.ItemData copy = item.Clone();
                copy.m_stack = amount;
                copy.m_equipped = false;
                if (!bag.AddItem(copy))
                {
                    continue;
                }
                contents.RemoveItem(item, amount);
                moved += amount;
            }
            return moved;
        }

        /// <summary>Nearest usable chest among these holding a wanted item, or null.</summary>
        internal static Container FindHolding(List<Container> chests, Predicate<ItemDrop.ItemData> wanted, Vector3 from)
        {
            Container best = null;
            float bestSqr = float.MaxValue;
            foreach (Container chest in chests)
            {
                if (!IsUsable(chest) || IsCauldron(chest) || !chest.GetInventory().GetAllItems().Exists(wanted))
                {
                    continue;
                }
                float sqr = (chest.transform.position - from).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    best = chest;
                    bestSqr = sqr;
                }
            }
            return best;
        }

        /// <summary>Existing, and nobody has it open (the flag in the ZDO covers the other player).</summary>
        internal static bool IsUsable(Container chest) =>
            chest != null && chest.m_nview != null && chest.m_nview.IsValid() && !chest.IsInUse() &&
            chest.m_nview.GetZDO().GetInt(ZDOVars.s_inUse) == 0;

        /// <summary>The chest to walk to next, or null; <paramref name="carrying"/> tells whether anything waits to be stored.</summary>
        internal static Container NextDestination(Inventory carried, List<Container> chests, Vector3 from, out bool carrying, Predicate<ItemDrop.ItemData> keep = null)
        {
            carrying = false;
            foreach (ItemDrop.ItemData item in carried.GetAllItems())
            {
                if (!IsStorable(item) || (keep != null && keep(item)))
                {
                    continue;
                }
                carrying = true;
                Container chest = BestChestFor(item, chests, from);
                if (chest != null)
                {
                    return chest;
                }
            }
            return null;
        }

        internal static bool HasDestination(ItemDrop.ItemData item, List<Container> chests, Vector3 from) =>
            BestChestFor(item, chests, from) != null;

        internal static Container BestChestFor(ItemDrop.ItemData item, List<Container> chests, Vector3 from)
        {
            Container best = null;
            int bestRank = int.MaxValue;
            float bestDistance = float.MaxValue;
            string kind = KindOf(item);
            foreach (Container chest in chests)
            {
                if (!IsUsable(chest))
                {
                    continue;
                }
                ChestIndex.ChestSummary contents = ChestIndex.Of(chest);
                // Food goes to the Settlement Cauldron first; nothing else ever goes there. A chest given a kind takes
                // that kind only, before any sorted chest.
                string assigned = AssignedKind(chest);
                int rank = IsCauldron(chest) ? (Settlers.Needs.IsFood(item) ? RankCauldron : RankNone)
                    : assigned.Length > 0 ? (assigned == kind ? RankAssigned : RankNone)
                    : Rank(contents, item, kind);
                if (rank == RankNone || rank > bestRank || FreeSpace(contents, item) <= 0)
                {
                    continue;
                }
                float distance = (chest.transform.position - from).sqrMagnitude;
                if (rank < bestRank || distance < bestDistance)
                {
                    best = chest;
                    bestRank = rank;
                    bestDistance = distance;
                }
            }
            return best;
        }

        /// <summary>
        /// Moves into the chest everything carried whose best chest it is, re-ranking after every item: an empty chest
        /// takes one kind of thing, the next kind then looks for another chest. Returns the number of items moved.
        /// </summary>
        internal static int StoreInto(Container chest, Inventory carried, List<Container> chests, Vector3 from, string who, Predicate<ItemDrop.ItemData> keep = null)
        {
            // The chest's owner writes its inventory (see ChestAccess); catch up with what the previous owner saved.
            if (!IsUsable(chest) || !chest.m_nview.IsOwner())
            {
                return 0;
            }
            chest.Load();

            Inventory contents = chest.GetInventory();
            int moved = 0;
            foreach (ItemDrop.ItemData item in carried.GetAllItems().ToArray())
            {
                if (!IsStorable(item) || (keep != null && keep(item)) || BestChestFor(item, chests, from) != chest)
                {
                    continue;
                }
                int amount = Mathf.Min(item.m_stack, FreeSpace(contents, item));
                if (amount <= 0)
                {
                    continue;
                }

                ItemDrop.ItemData copy = item.Clone();
                copy.m_stack = amount;
                if (!contents.AddItem(copy))
                {
                    // Cannot happen with the space counted like AddItem fills it; never remove what did not arrive.
                    Log.Warning(Module, $"{who} could not put {amount}x {item.m_shared.m_name} into a chest");
                    continue;
                }
                carried.RemoveItem(item, amount);
                moved += amount;
                Log.Info(Module, $"{who} stored {amount}x {item.m_shared.m_name}");
                // The next item is ranked against what the chest holds now.
                ChestIndex.Touch(chest);
            }
            return moved;
        }

        // Same kind: an explicit group above, or the item type for things that need no list (trophies, food, gear).
        internal static string KindOf(ItemDrop.ItemData item)
        {
            string prefab = item.m_dropPrefab != null ? item.m_dropPrefab.name : null;
            if (prefab != null && KindTable().TryGetValue(prefab, out string kind))
            {
                return kind;
            }

            ItemDrop.ItemData.SharedData shared = item.m_shared;
            switch (shared.m_itemType)
            {
                case ItemDrop.ItemData.ItemType.Trophy:
                    return "trophy";
                case ItemDrop.ItemData.ItemType.Consumable:
                    return shared.m_food > 0f || shared.m_foodStamina > 0f || shared.m_foodEitr > 0f ? "food" : "consumable";
                default:
                    return item.IsEquipable() ? "gear" : null;
            }
        }

        private static int Rank(ChestIndex.ChestSummary contents, ItemDrop.ItemData item, string kind)
        {
            bool sameItem = false;
            bool sameKind = false;
            bool other = false;
            foreach (ChestIndex.Stack held in contents.Stacks)
            {
                if (held.Name == item.m_shared.m_name)
                {
                    sameItem = true;
                }
                else if (kind != null && held.Kind == kind)
                {
                    sameKind = true;
                }
                else
                {
                    other = true;
                }
            }

            if (sameItem)
            {
                return other ? RankSameItemMixed : RankSameItem;
            }
            if (sameKind)
            {
                return other ? RankSameKindMixed : RankSameKind;
            }
            return other ? RankNone : RankEmpty;
        }

        // Counted the way Inventory.AddItem fills a chest: stacks of the same item, quality and world level, then
        // empty slots. A settler's stack never exceeds the stack size, so what is left after topping up fits one slot.
        internal static int FreeSpace(Inventory contents, ItemDrop.ItemData item)
        {
            int maxStack = item.m_shared.m_maxStackSize;
            int space = contents.GetEmptySlots() * maxStack;
            if (maxStack <= 1)
            {
                return space;
            }
            foreach (ItemDrop.ItemData held in contents.GetAllItems())
            {
                if (held.m_shared.m_name == item.m_shared.m_name && held.m_quality == item.m_quality && held.m_worldLevel == item.m_worldLevel)
                {
                    space += Mathf.Max(0, maxStack - held.m_stack);
                }
            }
            return space;
        }

        internal static int FreeSpace(ChestIndex.ChestSummary contents, ItemDrop.ItemData item)
        {
            int maxStack = item.m_shared.m_maxStackSize;
            int space = contents.EmptySlots * maxStack;
            if (maxStack <= 1)
            {
                return space;
            }
            foreach (ChestIndex.Stack held in contents.Stacks)
            {
                if (held.Name == item.m_shared.m_name && held.Quality == item.m_quality && held.WorldLevel == item.m_worldLevel)
                {
                    space += Mathf.Max(0, maxStack - held.Size);
                }
            }
            return space;
        }
    }
}
