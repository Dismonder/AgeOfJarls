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

        /// <summary>The solid colliders of a work object, fetched once when the object is chosen.</summary>
        internal static Collider[] SolidColliders(Component target) =>
            target == null ? new Collider[0] : System.Array.FindAll(target.GetComponentsInChildren<Collider>(), c => !c.isTrigger);

        /// <summary>The point of these colliders nearest to <paramref name="from"/> (bounds-based, cheap and safe).</summary>
        internal static Vector3 NearestPoint(Collider[] colliders, Vector3 from, Vector3 fallback)
        {
            Collider best = null;
            float bestSqr = float.MaxValue;
            foreach (Collider collider in colliders)
            {
                if (collider == null || !collider.enabled)
                {
                    continue;
                }
                float sqr = (collider.bounds.ClosestPoint(from) - from).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    best = collider;
                    bestSqr = sqr;
                }
            }
            return best != null ? best.bounds.ClosestPoint(from) : fallback;
        }

        /// <summary>The point of the object's colliders nearest to <paramref name="from"/> (bounds-based, cheap and safe).</summary>
        internal static Vector3 NearestPoint(Component target, Vector3 from)
        {
            Collider best = null;
            float bestSqr = float.MaxValue;
            foreach (Collider collider in target.GetComponentsInChildren<Collider>())
            {
                if (collider.isTrigger || !collider.enabled)
                {
                    continue;
                }
                float sqr = (collider.bounds.ClosestPoint(from) - from).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    best = collider;
                    bestSqr = sqr;
                }
            }
            return best != null ? best.bounds.ClosestPoint(from) : target.transform.position;
        }
    }
}
