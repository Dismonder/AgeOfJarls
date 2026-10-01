using AgeOfJarls.Core;
using AgeOfJarls.Portals;
using AgeOfJarls.Settlement;
using AgeOfJarls.Settlers;
using UnityEngine;

namespace AgeOfJarls.AI
{
    /// <summary>
    /// A settler far from its settlement - left behind after a trip with a player, or sent home from afar - takes a
    /// portal home when one near it leads into the settlement: it walks to the portal and jumps, as a player would,
    /// and goes on from the far side. Without such a portal it walks, as before. Owner only, like the AI; checked
    /// every few seconds while the settler is far from home and free (no orders, not following).
    /// </summary>
    internal sealed class PortalTravel
    {
        private const string Module = "AI";
        /// <summary>Closer than this to home it walks; a portal is for the long way.</summary>
        private const float MinDistance = 100f;
        /// <summary>The far end counts as "home" this far outside the settlement's radius (the portal by the gate).</summary>
        private const float HomeSlack = 40f;
        /// <summary>Radius assumed for a settlement whose table is not loaded here (its real one is unknown then).</summary>
        private const float DefaultRadius = 30f;
        private const float CheckSeconds = 4f;
        private const float RetrySeconds = 60f;
        private const float JumpDistance = 1.6f;
        private const float Reach = 2.5f;

        private readonly SettlerAI _ai;
        private readonly Settler _settler;
        private readonly PathMover _mover;
        private PortalRoutes.Route _route;
        private float _nextCheck;

        internal PortalTravel(SettlerAI ai, Settler settler, PathMover mover)
        {
            _ai = ai;
            _settler = settler;
            _mover = mover;
        }

        /// <summary>True while on its way to a portal (the frame's movement is taken).</summary>
        internal bool Update(float dt)
        {
            if (AoJConfig.SettlerPortals == null || !AoJConfig.SettlerPortals.Value)
            {
                return false;
            }
            if (_route == null)
            {
                if (Time.time < _nextCheck)
                {
                    return false;
                }
                _nextCheck = Time.time + CheckSeconds;
                _route = FindRouteHome();
                if (_route == null)
                {
                    return false;
                }
                _mover.Reset();
                Log.Info(Module, $"{_settler.DisplayName} heads for the portal '{_route.Tag}' to get home");
                if (AiTrace.On)
                {
                    AiTrace.Write(_ai, $"portal '{_route.Tag}' home, {Vector3.Distance(_ai.transform.position, _route.Entrance):0} m away");
                }
            }

            if (_route.Portal == null || !PortalRoutes.TryGetExit(_route.Portal, out Vector3 exit))
            {
                // Unloaded, or its tag changed and the far end is gone.
                GiveUp("the portal is gone");
                return false;
            }
            _route.Exit = exit;
            if (Vector3.Distance(_ai.transform.position, _route.Portal.transform.position) <= JumpDistance)
            {
                _settler.JumpTo(_route.Exit, $"the portal '{_route.Tag}'");
                _route = null;
                _nextCheck = Time.time + CheckSeconds;
                _mover.Reset();
                return true;
            }
            MoveResult move = _mover.MoveTo(dt, _route.Entrance, JumpDistance * 0.5f, Reach, run: true);
            if (move == MoveResult.Blocked)
            {
                GiveUp("no way to it");
                return false;
            }
            if (move == MoveResult.Arrived)
            {
                // At the entrance spot but not on the portal itself (its collider): the last step straight at it.
                _ai.MoveToPoint(dt, _route.Portal.transform.position, 0f, false);
            }
            _settler.SetActivity(SettlerActivity.Returning);
            return true;
        }

        private PortalRoutes.Route FindRouteHome()
        {
            ZNetView nview = _settler.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid() || !_settler.HasHome)
            {
                return null;
            }
            Vector3 me = _ai.transform.position;
            Vector3 home = nview.GetZDO().GetVec3(Keys.ZdoSettlerHomePosition, me);
            if (Utils.DistanceXZ(home, me) < MinDistance)
            {
                return null;
            }
            JarlTable table = _settler.HomeTable;
            float radius = (table != null ? table.Radius : DefaultRadius) + HomeSlack;
            return PortalRoutes.FindRoute(me, home, radius);
        }

        private void GiveUp(string why)
        {
            Log.Info(Module, $"{_settler.DisplayName} gives up the portal: {why}");
            _route = null;
            _nextCheck = Time.time + RetrySeconds;
            _mover.Reset();
        }
    }
}
