using AgeOfJarls.Core;
using HarmonyLib;

namespace AgeOfJarls.Settlers
{
    /// <summary>
    /// Settlers are knocked out instead of dying unless Settlers/PermanentDeath is on (see
    /// <see cref="SettlerCharacter.KnockOut"/>). Character.CheckDeath is where the game decides every death, on the
    /// character's owner; while down, a settler takes no damage and no creature treats it as an enemy, so monsters
    /// drop it at once and turn to someone who can fight back.
    /// </summary>
    internal static class KnockoutPatches
    {
        [HarmonyPatch(typeof(Character), nameof(Character.CheckDeath))]
        private static class CheckDeathPatch
        {
            [HarmonyPrefix]
            private static bool Prefix(Character __instance)
            {
                if (!(__instance is SettlerCharacter settler) || AoJConfig.PermanentDeath.Value || settler.IsDead() || settler.GetHealth() > 0f)
                {
                    return true;
                }
                settler.KnockOut();
                return false;
            }
        }

        /// <summary>Runs on the owner, where <see cref="SettlerCharacter.Down"/> is always current.</summary>
        [HarmonyPatch(typeof(Character), nameof(Character.RPC_Damage))]
        private static class DamagePatch
        {
            [HarmonyPrefix]
            private static bool Prefix(Character __instance) => !(__instance is SettlerCharacter settler) || !settler.Down;
        }

        /// <summary>Called for every target check of every AI: a type test first, the flag only for settlers.</summary>
        [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.IsEnemy), typeof(Character), typeof(Character))]
        private static class EnemyPatch
        {
            [HarmonyPrefix]
            private static bool Prefix(Character a, Character b, ref bool __result)
            {
                if ((b is SettlerCharacter downB && downB.Down) || (a is SettlerCharacter downA && downA.Down))
                {
                    __result = false;
                    return false;
                }
                return true;
            }
        }
    }
}
