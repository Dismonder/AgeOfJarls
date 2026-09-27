using System;
using AgeOfJarls.Core;
using HarmonyLib;

namespace AgeOfJarls.Settlers
{
    /// <summary>
    /// Players cannot hurt settlers unless Settlers.FriendlyFire is on. Checked in RPC_Damage, i.e. on the ZDO owner
    /// where damage is applied, so the rule is the same whichever player hits (arrows, fire and bombs included).
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.RPC_Damage))]
    internal static class FriendlyFirePatch
    {
        private static bool Prefix(Character __instance, HitData hit)
        {
            // Runs for every hit in the game: the cheap type test comes first.
            if (!(__instance is SettlerCharacter) || hit == null || AoJConfig.SettlerFriendlyFire.Value)
            {
                return true;
            }

            try
            {
                return !(hit.GetAttacker() is Player);
            }
            catch (Exception e)
            {
                Log.Error("Settlers", $"Friendly fire check failed, damage applied: {e.Message}");
                return true;
            }
        }
    }
}
