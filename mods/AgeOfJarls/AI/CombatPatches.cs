using AgeOfJarls.Settlers;
using HarmonyLib;
using UnityEngine;

namespace AgeOfJarls.AI
{
    /// <summary>
    /// What a settler needs from the game to fight with a player's weapons: no swing of its own while it means to block
    /// (<see cref="CombatSense"/>), a bow drawn fully (a creature has no draw time, so its arrows would fly powerless),
    /// the hits that land on it timed for the block, and each weapon's AI reach and pace set for a humanoid - player
    /// weapons carry a creature's defaults, so an archer would walk up to two metres and a swordsman swing every two seconds.
    /// </summary>
    internal static class CombatPatches
    {
        [HarmonyPatch(typeof(MonsterAI), "DoAttack")]
        private static class NoSwingWhileBlockingPatch
        {
            [HarmonyPrefix]
            private static bool Prefix(MonsterAI __instance, ref bool __result)
            {
                if (__instance is SettlerAI ai && ai.HoldingBlock)
                {
                    __result = false;
                    return false;
                }
                return true;
            }
        }

        /// <summary>
        /// Vanilla draws one of every weapon that fits at random, every second of a fight - an archer with a sword kept
        /// swapping. A settler fights with the weapon of its role: a bow from afar (a melee weapon in its bag when the
        /// enemy is in its face), a melee weapon otherwise; a bow only when it has nothing else.
        /// </summary>
        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.EquipBestWeapon))]
        private static class WeaponChoicePatch
        {
            private const float BowTooClose = 3.5f;

            [HarmonyPrefix]
            private static bool Prefix(Humanoid __instance, Character targetCreature, StaticTarget targetStatic)
            {
                if (!(__instance is SettlerCharacter body) || body.InAttack())
                {
                    return true;
                }
                Settler settler = body.GetComponent<Settler>();
                if (settler == null)
                {
                    return true;
                }
                ItemDrop.ItemData best = Choose(body, settler, targetCreature, targetStatic);
                if (best != null && !best.m_equipped)
                {
                    body.EquipItem(best, triggerEquipEffects: false);
                    settler.MarkInventoryDirty();
                }
                return false;
            }

            private static ItemDrop.ItemData Choose(SettlerCharacter body, Settler settler, Character targetCreature, StaticTarget targetStatic)
            {
                System.Collections.Generic.List<ItemDrop.ItemData> bag = body.GetInventory().GetAllItems();
                ItemDrop.ItemData melee = null;
                ItemDrop.ItemData bow = null;
                System.Func<ItemDrop.ItemData, bool> roleWeapon = Army.Posts.WeaponFor(settler.Role);
                foreach (ItemDrop.ItemData item in bag)
                {
                    if (!item.IsWeapon() || Army.Posts.IsBroken(item) || item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Tool)
                    {
                        continue;
                    }
                    if (Army.Posts.IsBow(item))
                    {
                        if (bow == null || Better(item, bow))
                        {
                            bow = item;
                        }
                    }
                    else if (Army.Posts.IsMeleeWeapon(item) && (melee == null || Prefer(item, melee, roleWeapon)))
                    {
                        melee = item;
                    }
                }
                float distance = targetCreature != null ? Vector3.Distance(targetCreature.transform.position, body.transform.position) - targetCreature.GetRadius()
                    : targetStatic != null ? Vector3.Distance(targetStatic.transform.position, body.transform.position)
                    : float.MaxValue;
                bool archer = settler.Role == Army.CombatRole.Archer && bow != null && HasArrows(bag, bow);
                if (archer && (distance > BowTooClose || melee == null))
                {
                    return bow;
                }
                return melee ?? (bow != null && HasArrows(bag, bow) ? bow : null);
            }

            private static bool HasArrows(System.Collections.Generic.List<ItemDrop.ItemData> bag, ItemDrop.ItemData bow) =>
                bag.Exists(i => Army.Posts.IsArrowFor(i, bow));

            // The role's kind of weapon first (a shieldbearer's one-hander over a two-hander), then the harder hitter.
            private static bool Prefer(ItemDrop.ItemData item, ItemDrop.ItemData current, System.Func<ItemDrop.ItemData, bool> roleWeapon)
            {
                bool fits = roleWeapon(item);
                bool currentFits = roleWeapon(current);
                return fits != currentFits ? fits : Better(item, current);
            }

            private static bool Better(ItemDrop.ItemData item, ItemDrop.ItemData current) =>
                item.GetDamage().GetTotalDamage() > current.GetDamage().GetTotalDamage();
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.GetAttackDrawPercentage))]
        private static class BowDrawPatch
        {
            [HarmonyPostfix]
            private static void Postfix(Humanoid __instance, ref float __result)
            {
                if (__instance is SettlerCharacter body)
                {
                    // Drawn a little short without experience, fully by a veteran.
                    ZNetView view = body.GetComponent<ZNetView>();
                    float skill = view != null && view.IsValid() ? CombatSkill.Level(view.GetZDO()) : 0f;
                    __result = Mathf.Lerp(0.75f, 1f, skill / 100f);
                }
            }
        }

        /// <summary>On the settler's owner, where the hit is applied (before the knockout patch decides it is down).</summary>
        [HarmonyPatch(typeof(Character), nameof(Character.RPC_Damage))]
        private static class HitTimingPatch
        {
            [HarmonyPrefix]
            [HarmonyPriority(Priority.High)]
            private static void Prefix(Character __instance, HitData hit)
            {
                if (!(__instance is SettlerCharacter body) || hit == null || hit.m_attacker.IsNone() || ZNetScene.instance == null)
                {
                    return;
                }
                SettlerAI ai = body.GetComponent<SettlerAI>();
                if (ai == null || ai.Sense == null)
                {
                    return;
                }
                GameObject attacker = ZNetScene.instance.FindInstance(hit.m_attacker);
                Character character = attacker != null ? attacker.GetComponent<Character>() : null;
                if (character != null)
                {
                    ai.Sense.OnHit(character);
                }
            }
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.EquipItem))]
        private static class WeaponTuningPatch
        {
            [HarmonyPostfix]
            private static void Postfix(Humanoid __instance, ItemDrop.ItemData item, bool __result)
            {
                if (__result && __instance is SettlerCharacter)
                {
                    Tune(item);
                }
            }
        }

        private const float BowRange = 22f;
        private const float BowInterval = 1.2f;
        private const float MeleeInterval = 0.9f;
        private const float MeleeReachSlack = 0.7f;
        private const float MeleeMaxAngle = 12f;

        /// <summary>
        /// The AI fields of a player's weapon, which only creatures read: a bow is shot from afar, a melee weapon from
        /// its own reach, and both as often as the swing allows. Shared data, so set once per kind of weapon; players
        /// never read these fields.
        /// </summary>
        internal static void Tune(ItemDrop.ItemData item)
        {
            if (item == null || !item.IsWeapon() || item.m_shared.m_attack == null)
            {
                return;
            }
            ItemDrop.ItemData.SharedData shared = item.m_shared;
            if (shared.m_skillType == Skills.SkillType.Bows || shared.m_skillType == Skills.SkillType.Crossbows)
            {
                shared.m_aiAttackRange = BowRange;
                shared.m_aiAttackRangeMin = 0f;
                shared.m_aiAttackInterval = BowInterval;
                shared.m_aiAttackMaxAngle = 5f;
            }
            else if (Army.Posts.IsMeleeWeapon(item))
            {
                shared.m_aiAttackRange = Mathf.Max(2f, shared.m_attack.m_attackRange + MeleeReachSlack);
                shared.m_aiAttackRangeMin = 0f;
                shared.m_aiAttackInterval = MeleeInterval;
                shared.m_aiAttackMaxAngle = MeleeMaxAngle;
            }
        }
    }
}
