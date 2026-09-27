using AgeOfJarls.Settlement;
using AgeOfJarls.Settlers;
using UnityEngine;

namespace AgeOfJarls.Army
{
    /// <summary>Picks the war banner a soldier stands at, and what gear each role needs.</summary>
    internal static class Posts
    {
        /// <summary>The soldier's current banner if it still fits, otherwise the nearest banner of that kind with room.</summary>
        internal static WarBanner Choose(Settler settler, JarlTable table, BannerKind kind)
        {
            WarBanner current = WarBanner.FindById(settler.PostId);
            if (current != null && current.Kind == kind && current.Settlement == table)
            {
                return current;
            }
            WarBanner best = null;
            float bestSqr = float.MaxValue;
            foreach (WarBanner banner in WarBanner.Loaded)
            {
                if (banner == null || banner.Kind != kind || banner.Settlement != table || banner.Posted().Count >= banner.Places)
                {
                    continue;
                }
                float sqr = (banner.transform.position - settler.transform.position).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    best = banner;
                    bestSqr = sqr;
                }
            }
            return best;
        }

        /// <summary>The nearest Shelter banner of the settlement, where civilians hide during an alarm.</summary>
        internal static WarBanner Shelter(JarlTable table, Vector3 from)
        {
            WarBanner best = null;
            float bestSqr = float.MaxValue;
            foreach (WarBanner banner in WarBanner.Loaded)
            {
                if (banner == null || banner.Kind != BannerKind.Shelter || banner.Settlement != table)
                {
                    continue;
                }
                float sqr = (banner.transform.position - from).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    best = banner;
                    bestSqr = sqr;
                }
            }
            return best;
        }

        internal static bool IsMeleeWeapon(ItemDrop.ItemData item) =>
            item != null && (item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon ||
                             item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.TwoHandedWeapon);

        internal static bool IsOneHanded(ItemDrop.ItemData item) =>
            item != null && item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon;

        internal static bool IsBow(ItemDrop.ItemData item) => item != null && item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Bow;

        internal static bool IsShield(ItemDrop.ItemData item) => item != null && item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield;

        internal static bool IsArmor(ItemDrop.ItemData item)
        {
            if (item == null)
            {
                return false;
            }
            ItemDrop.ItemData.ItemType type = item.m_shared.m_itemType;
            return type == ItemDrop.ItemData.ItemType.Helmet || type == ItemDrop.ItemData.ItemType.Chest ||
                   type == ItemDrop.ItemData.ItemType.Legs || type == ItemDrop.ItemData.ItemType.Shoulder;
        }

        internal static bool IsArrowFor(ItemDrop.ItemData item, ItemDrop.ItemData bow) =>
            item != null && bow != null && item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo &&
            item.m_shared.m_ammoType == bow.m_shared.m_ammoType;
    }
}
