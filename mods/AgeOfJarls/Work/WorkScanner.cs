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

        private static readonly Collider[] s_hits = new Collider[1024];
        private static readonly Dictionary<(long, Type), (float time, List<Component> found)> s_cache =
            new Dictionary<(long, Type), (float, List<Component>)>();
        private static readonly HashSet<Component> s_seen = new HashSet<Component>();
        private static int s_mask;

        internal static List<Component> Find<T>(WorkTotem totem) where T : Component
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
            int count = Physics.OverlapSphereNonAlloc(totem.transform.position, totem.Radius, s_hits, s_mask);
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

        /// <summary>A standing tree is struck at its trunk, whose radius this is about.</summary>
        private const float TrunkRadius = 0.5f;

        /// <summary>
        /// Where to swing at a work object from <paramref name="from"/>: a standing tree at the side of its trunk facing
        /// the worker (its colliders include branches metres wide), anything else at the nearest point of its colliders.
        /// </summary>
        internal static Vector3 StrikePoint(Component target, Collider[] colliders, Vector3 from)
        {
            if (target is TreeBase)
            {
                Vector3 trunk = target.transform.position;
                Vector3 away = from - trunk;
                away.y = 0f;
                return away.sqrMagnitude < 0.01f ? trunk : trunk + away.normalized * TrunkRadius;
            }
            return NearestPoint(colliders, from, target.transform.position);
        }

        /// <summary>The point of these colliders nearest to <paramref name="from"/>.</summary>
        internal static Vector3 NearestPoint(Collider[] colliders, Vector3 from, Vector3 fallback)
        {
            Vector3 best = fallback;
            float bestSqr = float.MaxValue;
            foreach (Collider collider in colliders)
            {
                if (collider == null || !collider.enabled)
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
        // lying at an angle is mostly empty air. Other meshes fall back to their bounds.
        private static Vector3 ClosestOn(Collider collider, Vector3 from) =>
            collider is MeshCollider mesh && !mesh.convex ? collider.bounds.ClosestPoint(from) : collider.ClosestPoint(from);

    }
}
