using AgeOfJarls.Core;
using HarmonyLib;
using UnityEngine;

namespace AgeOfJarls.Settlers
{
    /// <summary>
    /// Settlers learn to fight: every hit a settler lands raises its combat experience (0-100, fast at first, slow near
    /// the top), and its hits deal up to +50% damage with it. Runs on the attacker's machine - the settler's owner - in
    /// Character.Damage, before the hit travels to the target's owner, so both players see the same damage.
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
            // Experience up to +50%, morale -15% (miserable) to +15% (high spirits).
            float morale = Mathf.Lerp(0.85f, 1.15f, Needs.Morale(zdo) / 100f);
            hit.m_damage.Modify((1f + MaxBonus * skill / 100f) * morale);
            zdo.Set(SkillKey, Mathf.Min(100f, skill + GainPerHit * (1f - skill / 110f)));
        }
    }
}
