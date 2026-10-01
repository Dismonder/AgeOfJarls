using System.Collections.Generic;
using UnityEngine;

namespace AgeOfJarls.Portals
{
    /// <summary>
    /// The portal module's map: which loaded portal takes a settler close to where it wants to be. A portal's other end
    /// is known from its connection (the game asks the server for that ZDO for every loaded portal, so clients know
    /// it too). The exit spot is computed as the game does for a player: a metre in front of the far portal.
    /// </summary>
    internal static class PortalRoutes
    {
        /// <summary>A portal farther than this from the settler is not worth the walk.</summary>
        internal const float SearchRange = 80f;
        private const float RescanSeconds = 5f;
        private const float ExitDistance = 1f;

        private static readonly List<TeleportWorld> s_portals = new List<TeleportWorld>();
        private static float s_scannedAt = float.MinValue;

        internal sealed class Route
        {
            internal TeleportWorld Portal;
            internal Vector3 Entrance;
            internal Vector3 Exit;
            internal string Tag;
        }

        /// <summary>
        /// The nearest portal within reach of <paramref name="from"/> whose other end lies within
        /// <paramref name="radius"/> of <paramref name="destination"/>; null when there is none.
        /// </summary>
        internal static Route FindRoute(Vector3 from, Vector3 destination, float radius)
        {
            Route best = null;
            float bestDistance = float.MaxValue;
            foreach (TeleportWorld portal in LoadedPortals())
            {
                if (portal == null || portal.m_nview == null || !portal.m_nview.IsValid())
                {
                    continue;
                }
                float distance = Vector3.Distance(portal.transform.position, from);
                if (distance > SearchRange || distance >= bestDistance || !TryGetExit(portal, out Vector3 exit))
                {
                    continue;
                }
                if (Utils.DistanceXZ(exit, destination) > radius)
                {
                    continue;
                }
                best = new Route
                {
                    Portal = portal,
                    Entrance = portal.transform.position + portal.transform.forward * ExitDistance,
                    Exit = exit,
                    Tag = portal.GetText(),
                };
                bestDistance = distance;
            }
            return best;
        }

        /// <summary>
        /// A portal within reach whose far end is so much nearer the goal that the walk to the portal plus the walk
        /// from its far end saves at least <paramref name="minSaving"/> metres; null when none does.
        /// </summary>
        internal static Route FindShortcut(Vector3 from, Vector3 goal, float minSaving)
        {
            float direct = Utils.DistanceXZ(from, goal);
            Route best = null;
            float bestTotal = direct - minSaving;
            foreach (TeleportWorld portal in LoadedPortals())
            {
                if (portal == null || portal.m_nview == null || !portal.m_nview.IsValid())
                {
                    continue;
                }
                float toPortal = Utils.DistanceXZ(portal.transform.position, from);
                if (toPortal > SearchRange || !TryGetExit(portal, out Vector3 exit))
                {
                    continue;
                }
                float total = toPortal + Utils.DistanceXZ(exit, goal);
                if (total >= bestTotal)
                {
                    continue;
                }
                best = new Route
                {
                    Portal = portal,
                    Entrance = portal.transform.position + portal.transform.forward * ExitDistance,
                    Exit = exit,
                    Tag = portal.GetText(),
                };
                bestTotal = total;
            }
            return best;
        }

        /// <summary>Where a jump through this portal lands, as the game computes it for a player; false without a target.</summary>
        internal static bool TryGetExit(TeleportWorld portal, out Vector3 exit)
        {
            exit = Vector3.zero;
            if (portal == null || portal.m_nview == null || !portal.m_nview.IsValid() || ZDOMan.instance == null)
            {
                return false;
            }
            ZDOID targetId = portal.m_nview.GetZDO().GetConnectionZDOID(ZDOExtraData.ConnectionType.Portal);
            if (targetId.IsNone())
            {
                return false;
            }
            ZDO target = ZDOMan.instance.GetZDO(targetId);
            if (target == null)
            {
                return false;
            }
            Vector3 forward = target.GetRotation() * Vector3.forward;
            exit = target.GetPosition() + forward * ExitDistance + Vector3.up;
            return true;
        }

        private static List<TeleportWorld> LoadedPortals()
        {
            if (Time.time - s_scannedAt > RescanSeconds)
            {
                s_scannedAt = Time.time;
                s_portals.Clear();
                s_portals.AddRange(Object.FindObjectsByType<TeleportWorld>(FindObjectsSortMode.None));
            }
            return s_portals;
        }
    }
}
