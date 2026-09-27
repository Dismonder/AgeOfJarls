using AgeOfJarls.Core;
using AgeOfJarls.Core.Defs;
using HarmonyLib;
using UnityEngine;

namespace AgeOfJarls.Settlers
{
    /// <summary>
    /// Settlers learn to fight: every hit a settler lands raises its combat experience (0-100, fast at first, slow near
    /// the top), and its hits deal up to +50% damage with it, more or less with morale and with traits (Strong: melee,
    /// Hawk-eyed: bows, Cowardly: less of both). Runs on the attacker's machine - the settler's owner - in
    /// Character.Damage, before the hit travels to the target's owner, so both players see the same damage.
    /// Hardened settlers take less damage (<see cref="DamageTakenPatch"/>, on the settler's owner).
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    internal static class CombatSkill
    {
        internal static readonly int SkillKey = (Keys.SkillPrefix + "combat").GetStableHashCode();
        private const float GainPerHit = 0.25f;
        private const float MaxBonus = 0.5f;

        internal static float Level(ZDO zdo) => zdo?.GetFloat(SkillKey) ?? 0f;

        [HarmonyPrefix]
        private static void Prefix(Character __instance, HitData hit)
        {
            if (hit == null || hit.m_attacker.IsNone() || __instance == null || __instance.IsPlayer() || ZNetScene.instance == null)
            {
                return;
            }
            GameObject attacker = ZNetScene.instance.FindInstance(hit.m_attacker);
            Settler settler = attacker != null ? attacker.GetComponent<Settler>() : null;
            ZNetView view = settler != null ? settler.GetComponent<ZNetView>() : null;
            if (view == null || !view.IsValid() || !view.IsOwner())
            {
                return;
            }
            ZDO zdo = view.GetZDO();
            float skill = zdo.GetFloat(SkillKey);
            // Experience up to +50%, morale -15% (miserable) to +15% (high spirits), traits by kind of weapon.
            float morale = Mathf.Lerp(0.85f, 1.15f, Needs.Morale(zdo) / 100f);
            bool ranged = hit.m_skill == Skills.SkillType.Bows || hit.m_skill == Skills.SkillType.Crossbows;
            float berserk = !ranged && settler.Role == Army.CombatRole.Berserker ? Army.CombatRoles.BerserkerDamage : 0f;
            float traits = Mathf.Max(0.1f, 1f + settler.TraitSum(ranged ? TraitStat.RangedDamage : TraitStat.MeleeDamage) + berserk);
            hit.m_damage.Modify((1f + MaxBonus * skill / 100f) * morale * traits);
            zdo.Set(SkillKey, Mathf.Min(100f, skill + GainPerHit * (1f - skill / 110f)));
        }

        /// <summary>A hit on a settler, on its owner (where damage is applied): Hardened settlers shrug some of it off.</summary>
        [HarmonyPatch(typeof(Character), nameof(Character.RPC_Damage))]
        private static class DamageTakenPatch
        {
            [HarmonyPrefix]
            private static void Prefix(Character __instance, HitData hit)
            {
                if (hit == null || !(__instance is SettlerCharacter))
                {
                    return;
                }
                Settler settler = __instance.GetComponent<Settler>();
                float taken = settler != null ? settler.TraitSum(TraitStat.DamageTaken) : 0f;
                if (taken != 0f)
                {
                    hit.m_damage.Modify(Mathf.Max(0.1f, 1f + taken));
                }
            }
        }
    }
}
