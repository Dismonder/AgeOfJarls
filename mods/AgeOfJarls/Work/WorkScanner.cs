using System;
using System.Collections.Generic;
using UnityEngine;

namespace AgeOfJarls.Work
{
    /// <summary>
    /// Finds work objects (trees, logs, rocks, crops...) in a totem's zone with one physics query, cached for a few
    /// seconds per totem and type, so workers of the same totem share the scan and nothing searches every frame.
    /// </summary>
    internal static class WorkScanner
    {
        private const float CacheSeconds = 5f;

        private const int MaxHits = 32768;
        private static Collider[] s_hits = new Collider[1024];
        private static readonly Dictionary<(long, Type), (float time, List<Component> found)> s_cache =
            new Dictionary<(long, Type), (float, List<Component>)>();
        private static readonly HashSet<Component> s_seen = new HashSet<Component>();
        private static readonly List<(long, Type)> s_stale = new List<(long, Type)>();
        private static int s_mask;

        /// <summary>
        /// What lies in the zone, and up to <paramref name="margin"/> past its edge (one margin per type: the cache is
        /// kept by totem and type).
        /// </summary>
        internal static List<Component> Find<T>(WorkTotem totem, float margin = 0f) where T : Component
        {
            var key = (totem.Id, typeof(T));
            if (s_cache.TryGetValue(key, out (float time, List<Component> found) cached) && Time.time - cached.time < CacheSeconds)
            {
                cached.found.RemoveAll(c => c == null);
                return cached.found;
            }

            if (s_mask == 0)
            {
                s_mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "vehicle");
            }
            var found = new List<Component>();
            s_seen.Clear();
            int count;
            // A full buffer means some were left out - by a big base in the zone, the walls and floors could crowd out
            // every tree: it grows and the query runs again.
            while ((count = Physics.OverlapSphereNonAlloc(totem.transform.position, totem.Radius + margin, s_hits, s_mask)) == s_hits.Length &&
                   s_hits.Length < MaxHits)
            {
                s_hits = new Collider[s_hits.Length * 2];
            }
            for (int i = 0; i < count; i++)
            {
                T component = s_hits[i].GetComponentInParent<T>();
                if (component != null && s_seen.Add(component))
                {
                    found.Add(component);
                }
            }
            s_seen.Clear();
            s_cache[key] = (Time.time, found);
            if (s_cache.Count > 128)
            {
                s_cache.Clear();
            }
            return found;
        }

        /// <summary>Something in the zone just fell or broke apart (a tree into a log...): the next search looks afresh.</summary>
        internal static void Forget(WorkTotem totem)
        {
            long id = totem.Id;
            s_stale.Clear();
            foreach ((long, Type) key in s_cache.Keys)
            {
                if (key.Item1 == id)
                {
                    s_stale.Add(key);
                }
            }
            foreach ((long, Type) key in s_stale)
            {
                s_cache.Remove(key);
            }
            s_stale.Clear();
        }

        private static int s_solidMask;

        /// <summary>
        /// The colliders of a work object a swing can hit, fetched once when the object is chosen: not triggers, and
        /// not the "viewblock" canopy a tree carries to block the view - its box reaches some ten metres around.
        /// </summary>
        internal static Collider[] SolidColliders(Component target)
        {
            if (target == null)
            {
                return new Collider[0];
            }
            if (s_solidMask == 0)
            {
                s_solidMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "terrain", "vehicle");
            }
            return System.Array.FindAll(target.GetComponentsInChildren<Collider>(),
                c => !c.isTrigger && (s_solidMask & (1 << c.gameObject.layer)) != 0);
        }

        /// <summary>A standing tree is struck at its trunk, whose radius this is about...</summary>
        private const float TrunkRadius = 0.5f;
        /// <summary>...and at least this high above its foot, when the worker stands below it on a slope.</summary>
        private const float TrunkMinHeight = 0.4f;

        /// <summary>
        /// Where to swing at a work object from <paramref name="chest"/> (about where a swing starts): a standing tree
        /// at the side of its trunk facing the worker, at that height (its colliders include branches metres wide);
        /// anything else - a log on the ground, a rock - at the point of its colliders nearest to it, so the blow is
        /// aimed down at a log rather than over it into the ground.
        /// </summary>
        internal static Vector3 StrikePoint(Component target, Collider[] colliders, Vector3 chest)
        {
            if (target is TreeBase)
            {
                Vector3 trunk = target.transform.position;
                Vector3 away = chest - trunk;
                away.y = 0f;
                Vector3 point = away.sqrMagnitude < 0.01f ? trunk : trunk + away.normalized * TrunkRadius;
                point.y = Mathf.Max(chest.y, trunk.y + TrunkMinHeight);
                return point;
            }
            return NearestPoint(colliders, chest, target.transform.position);
        }

        /// <summary>The point of these colliders nearest to <paramref name="from"/>.</summary>
        internal static Vector3 NearestPoint(Collider[] colliders, Vector3 from, Vector3 fallback)
        {
            Vector3 best = fallback;
            float bestSqr = float.MaxValue;
            foreach (Collider collider in colliders)
            {
                // A broken piece of a rock is switched off with its game object, not the collider.
                if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy)
                {
                    continue;
                }
                Vector3 point = ClosestOn(collider, from);
                float sqr = (point - from).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    best = point;
                    bestSqr = sqr;
                }
            }
            return best;
        }

        // On the collider itself where Unity can tell (boxes, spheres, capsules, convex meshes): the box around a log
        // lying at an angle is mostly empty air. Unity cannot for other meshes (a big rock): there it is where a line from
        // the worker to the mesh's middle meets its surface - the corner of its bounds may hang in the air, out of reach.
        private static Vector3 ClosestOn(Collider collider, Vector3 from)
        {
            if (!(collider is MeshCollider mesh) || mesh.convex)
            {
                return collider.ClosestPoint(from);
            }
            Bounds bounds = collider.bounds;
            Vector3 toCenter = bounds.center - from;
            float length = toCenter.magnitude;
            if (length > 0.01f && collider.Raycast(new Ray(from, toCenter / length), out RaycastHit hit, length))
            {
                return hit.point;
            }
            return bounds.ClosestPoint(from);
        }

        private const int ApproachSides = 8;
        /// <summary>How far from an object's side a worker stands: about its own radius, clear of the object.</summary>
        private const float StandOff = 0.8f;
        private static readonly List<Vector3> s_approachPath = new List<Vector3>();

        /// <summary>
        /// Where to stand to reach an object: beside it on the navmesh, within <paramref name="reach"/> of it, with a
        /// full path from <paramref name="from"/> - the nearest by path of the spots around it. The side facing the
        /// worker may be walled in (a chest between the table and a bench), where a path only ever gets part-way while
        /// another side is open. False when no side can be reached, or the navmesh there is not built yet.
        /// </summary>
        internal static bool FindApproach(Vector3 from, Collider[] colliders, Vector3 fallback, float reach, Pathfinding.AgentType agent, out Vector3 spot)
        {
            spot = fallback;
            if (Pathfinding.instance == null)
            {
                return false;
            }
            var bounds = new Bounds(fallback, Vector3.zero);
            foreach (Collider collider in colliders)
            {
                if (collider != null && collider.enabled && collider.gameObject.activeInHierarchy)
                {
                    bounds.Encapsulate(collider.bounds);
                }
            }
            Vector3 center = bounds.center;
            // Far enough out that each direction meets the side facing it, not a corner around from it.
            float radius = new Vector2(bounds.extents.x, bounds.extents.z).magnitude + 2f;
            float shortest = float.MaxValue;
            for (int i = 0; i < ApproachSides; i++)
            {
                Vector3 outside = center + Quaternion.Euler(0f, i * 360f / ApproachSides, 0f) * Vector3.forward * radius;
                Vector3 side = NearestPoint(colliders, outside, fallback);
                Vector3 away = outside - side;
                away.y = 0f;
                Vector3 stand = away.sqrMagnitude > 0.0001f ? side + away.normalized * StandOff : outside;
                // At the object's foot; for a wall of a house on stilts or a roof also on the ground below it - the
                // navmesh nearest its foot may be the floor inside, with no way in.
                stand.y = bounds.min.y + 0.2f;
                TryApproach(from, stand, colliders, fallback, reach, agent, ref shortest, ref spot);
                if (ZoneSystem.instance != null && ZoneSystem.instance.GetGroundHeight(stand, out float ground) && ground < bounds.min.y - 1f)
                {
                    stand.y = ground;
                    TryApproach(from, stand, colliders, fallback, reach, agent, ref shortest, ref spot);
                }
            }
            s_approachPath.Clear();
            return shortest < float.MaxValue;
        }

        private static void TryApproach(Vector3 from, Vector3 stand, Collider[] colliders, Vector3 fallback, float reach,
                                        Pathfinding.AgentType agent, ref float shortest, ref Vector3 spot)
        {
            if (!Pathfinding.instance.GetPath(from, stand, s_approachPath, agent, requireFullPath: true, cleanup: false) || s_approachPath.Count == 0)
            {
                return;
            }
            // The game moves the goal to the nearest navmesh, maybe onto a floor above: the end must reach the object.
            Vector3 end = s_approachPath[s_approachPath.Count - 1];
            if (Vector3.Distance(end, NearestPoint(colliders, end, fallback)) > reach)
            {
                return;
            }
            float length = 0f;
            for (int k = 1; k < s_approachPath.Count; k++)
            {
                length += Vector3.Distance(s_approachPath[k - 1], s_approachPath[k]);
            }
            if (length < shortest)
            {
                shortest = length;
                spot = end;
            }
        }
    }
}
