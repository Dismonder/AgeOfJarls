using HarmonyLib;

namespace AgeOfJarls.Settlers
{
    /// <summary>
    /// Settlers never damage buildings: a woodcutter's swing next to a wall or a fight inside the hall would otherwise
    /// chip the settlement apart (NPC melee hits every destructible in its arc). Runs on the building's owner, where
    /// the RPC lands; the attacker is found by its ZDOID, which is valid for the current session.
    /// </summary>
    [HarmonyPatch(typeof(WearNTear), "RPC_Damage")]
    internal static class StructureGuard
    {
        [HarmonyPrefix]
        private static bool Prefix(HitData hit)
        {
            if (hit == null || hit.m_attacker.IsNone() || ZNetScene.instance == null)
            {
                return true;
            }
            UnityEngine.GameObject attacker = ZNetScene.instance.FindInstance(hit.m_attacker);
            return attacker == null || attacker.GetComponent<Settler>() == null;
        }
    }
}
