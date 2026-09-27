using System;
using AgeOfJarls.Core;
using HarmonyLib;
using UnityEngine;

namespace AgeOfJarls.Settlement
{
    internal static class JarlTablePatches
    {
        private const string Module = "Settlement";

        // Settlements may not overlap: a new table needs Settlement.MinDistance to every loaded one, and room for its
        // own area next to one that grew with its members.
        [HarmonyPatch(typeof(Player), nameof(Player.UpdatePlacementGhost))]
        private static class PlacementDistance
        {
            private static void Postfix(Player __instance, GameObject ___m_placementGhost, ref Player.PlacementStatus ___m_placementStatus)
            {
                // Runs every frame in build mode: leave before any lookup unless a valid table ghost is shown.
                if (___m_placementStatus != Player.PlacementStatus.Valid || ___m_placementGhost == null || JarlTable.Loaded.Count == 0)
                {
                    return;
                }

                try
                {
                    Vector3 spot = ___m_placementGhost.transform.position;
                    float minimum = AoJConfig.SettlementMinDistance.Value;
                    if (___m_placementGhost.GetComponent<JarlTable>() != null &&
                        JarlTable.Loaded.Exists(t => Utils.DistanceXZ(t.transform.position, spot) < Mathf.Max(minimum, t.Radius + JarlTable.RadiusFor(0))))
                    {
                        ___m_placementStatus = Player.PlacementStatus.MoreSpace;
                        __instance.SetPlacementGhostValid(false);
                    }
                }
                catch (Exception e)
                {
                    Log.Error(Module, $"Placement check failed: {e.Message}");
                }
            }
        }

        // Only a Jarl may take the table down, and with it the whole settlement.
        [HarmonyPatch(typeof(Piece), nameof(Piece.CanBeRemoved))]
        private static class RemovalGuard
        {
            private static void Postfix(Piece __instance, ref bool __result)
            {
                if (!__result || Player.m_localPlayer == null)
                {
                    return;
                }

                try
                {
                    JarlTable table = __instance.GetComponent<JarlTable>();
                    SettlementData data = table != null ? table.Data : null;
                    if (data != null && !data.RoleOf(Player.m_localPlayer.GetPlayerID()).Allows(SettlementRight.Rule))
                    {
                        __result = false;
                        // Top left: vanilla shows its own "can't remove" message in the center right after.
                        Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, Localization.instance.Localize("$aoj_msg_cannot_remove"));
                    }
                }
                catch (Exception e)
                {
                    Log.Error(Module, $"Removal check failed: {e.Message}");
                }
            }
        }
    }
}
