using HarmonyLib;

namespace AgeOfJarls.Settlement
{
    /// <summary>
    /// A bed the settlement gave to a settler says whose it is. It stays unclaimed in vanilla terms, so a player can
    /// still claim it; the settler then gets another free bed at the next reconcile of the Jarl's Table.
    /// </summary>
    [HarmonyPatch(typeof(Bed), nameof(Bed.GetHoverText))]
    internal static class BedHoverPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Bed __instance, ref string __result)
        {
            ZNetView view = __instance.m_nview;
            if (view == null || !view.IsValid() || view.GetZDO().GetLong(ZDOVars.s_owner) != 0L)
            {
                return;
            }
            string settler = JarlTable.SettlerInBed(__instance.transform.position);
            if (settler != null)
            {
                __result = Localization.instance.Localize("$aoj_bed_of\n[<color=yellow><b>$KEY_Use</b></color>] $piece_bed_claim", settler);
            }
        }
    }
}
