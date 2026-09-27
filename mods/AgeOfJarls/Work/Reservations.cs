using System.Collections.Generic;
using UnityEngine;

namespace AgeOfJarls.Work
{
    /// <summary>
    /// Who is working on what: two workers never pick the same tree, rock, drop or crop. Kept in memory on the machine
    /// that simulates the workers (settlers of one settlement are usually simulated by one player), with an expiry,
    /// so a reservation of a worker that vanished frees itself.
    /// </summary>
    internal static class Reservations
    {
        private const float DefaultSeconds = 30f;

        private static readonly Dictionary<int, (long owner, float until)> s_taken = new Dictionary<int, (long, float)>();
        private static readonly List<int> s_expired = new List<int>();

        internal static bool IsFree(Object target, long worker)
        {
            if (target == null)
            {
                return false;
            }
            return !s_taken.TryGetValue(target.GetInstanceID(), out (long owner, float until) taken) || taken.owner == worker ||
                   Time.time > taken.until;
        }

        /// <summary>Takes or renews the target for the worker; false when another worker has it.</summary>
        internal static bool Take(Object target, long worker, float seconds = DefaultSeconds)
        {
            if (!IsFree(target, worker))
            {
                return false;
            }
            s_taken[target.GetInstanceID()] = (worker, Time.time + seconds);
            if (s_taken.Count > 256)
            {
                Prune();
            }
            return true;
        }

        internal static void Release(Object target, long worker)
        {
            if (target != null && s_taken.TryGetValue(target.GetInstanceID(), out (long owner, float until) taken) && taken.owner == worker)
            {
                s_taken.Remove(target.GetInstanceID());
            }
        }

        private static void Prune()
        {
            s_expired.Clear();
            foreach (KeyValuePair<int, (long owner, float until)> pair in s_taken)
            {
                if (Time.time > pair.Value.until)
                {
                    s_expired.Add(pair.Key);
                }
            }
            foreach (int key in s_expired)
            {
                s_taken.Remove(key);
            }
        }
    }
}
