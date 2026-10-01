using AgeOfJarls.Core;
using HarmonyLib;
using UnityEngine;

namespace AgeOfJarls.Portals
{
    /// <summary>
    /// The portal module's player side: a shorter jump. The game holds the player two seconds in the dark before it
    /// moves them, and after a portal (a "distant" teleport) waits eight seconds more while the far zone loads, even
    /// when it is loaded already. Both waits are scaled to Portals/PlayerTeleportSeconds and Portals/DistantLoadSeconds
    /// by letting the game's own timer run faster; the game's checks (zone loaded, floor found) stay as they are, so a
    /// jump never lands before the ground is there.
    /// </summary>
    internal static class PortalPatches
    {
        private const float VanillaFadeSeconds = 2f;
        private const float VanillaDistantSeconds = 8f;

        [HarmonyPatch(typeof(Player), nameof(Player.UpdateTeleport))]
        private static class FasterTeleportPatch
        {
            private static void Prefix(Player __instance, float dt)
            {
                if (!__instance.m_teleporting || AoJConfig.PlayerTeleportSeconds == null)
                {
                    return;
                }
                float timer = __instance.m_teleportTimer;
                float speed;
                if (timer < VanillaFadeSeconds)
                {
                    speed = VanillaFadeSeconds / Mathf.Max(0.1f, AoJConfig.PlayerTeleportSeconds.Value);
                }
                else if (__instance.m_distantTeleport && timer < VanillaDistantSeconds)
                {
                    float wait = VanillaDistantSeconds - VanillaFadeSeconds;
                    speed = wait / Mathf.Max(0.1f, AoJConfig.DistantLoadSeconds.Value);
                }
                else
                {
                    return;
                }
                if (speed > 1f)
                {
                    __instance.m_teleportTimer += dt * (speed - 1f);
                }
            }
        }
    }
}
