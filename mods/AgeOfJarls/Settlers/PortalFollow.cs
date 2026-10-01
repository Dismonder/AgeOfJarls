using AgeOfJarls.Core;
using HarmonyLib;
using UnityEngine;

namespace AgeOfJarls.Settlers
{
    /// <summary>
    /// A player's followers take the portal with the player. The game moves the player two seconds after the jump
    /// starts; the followers close by are sent to the arrival spot at once, each by its own owner (this machine or
    /// another, asked by RPC), and are there when the player steps out. Knocked-out and captive settlers stay.
    /// </summary>
    internal static class PortalFollow
    {
        private const string Module = "Settlers";
        /// <summary>A follower farther than this was not really with the player.</summary>
        private const float WithinDistance = 20f;

        [HarmonyPatch(typeof(Player), nameof(Player.TeleportTo))]
        private static class TeleportPatch
        {
            private static void Postfix(Player __instance, Vector3 pos, bool __result)
            {
                if (!__result || __instance != Player.m_localPlayer || AoJConfig.FollowThroughPortals == null || !AoJConfig.FollowThroughPortals.Value)
                {
                    return;
                }
                long leaderId = __instance.GetPlayerID();
                Vector3 from = __instance.transform.position;
                int sent = 0;
                foreach (Settler settler in Settler.Loaded)
                {
                    if (settler == null || !settler.IsLoaded || settler.FollowedPlayerId != leaderId || settler.IsCaptive ||
                        Vector3.Distance(settler.transform.position, from) > WithinDistance)
                    {
                        continue;
                    }
                    var body = settler.GetComponent<SettlerCharacter>();
                    if (body == null || body.Down || body.IsDead())
                    {
                        continue;
                    }
                    settler.RequestTeleport(pos);
                    sent++;
                }
                if (sent > 0)
                {
                    Log.Debug(Module, $"{sent} follower(s) sent through the portal");
                }
            }
        }
    }
}
