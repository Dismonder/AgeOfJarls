using AgeOfJarls.Core;
using AgeOfJarls.Settlers;
using HarmonyLib;
using UnityEngine;

namespace AgeOfJarls.Army
{
    /// <summary>
    /// Soldiers wear out their weapons like players do, at Army/GearWear of a player's rate: a little with every swing
    /// or shot (the game itself only wears players' gear). Runs on the settler's owner, where attacks start. A weapon
    /// worn out is put away - the game would not equip it again - and the soldier fetches a working one from the
    /// Armory, leaving the old one there for a player to repair (<see cref="AI.SoldierDuty"/>). Workers' tools are left alone.
    /// </summary>
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
    internal static class WeaponWear
    {
        [HarmonyPostfix]
        private static void Postfix(Humanoid __instance, bool __result)
        {
            float rate = AoJConfig.GearWear.Value;
            if (!__result || rate <= 0f || !(__instance is SettlerCharacter))
            {
                return;
            }
            Settler settler = __instance.GetComponent<Settler>();
            ItemDrop.ItemData weapon = __instance.GetCurrentWeapon();
            if (settler == null || settler.Role == CombatRole.None || weapon == null || !weapon.m_shared.m_useDurability ||
                !Posts.WeaponFor(settler.Role)(weapon))
            {
                return;
            }
            weapon.m_durability = Mathf.Max(0f, weapon.m_durability - weapon.m_shared.m_useDurabilityDrain * Game.m_durabilityRate * rate);
            if (weapon.m_durability <= 0f)
            {
                __instance.UnequipItem(weapon, triggerEquipEffects: false);
            }
            settler.MarkInventoryDirty();
        }
    }
}
