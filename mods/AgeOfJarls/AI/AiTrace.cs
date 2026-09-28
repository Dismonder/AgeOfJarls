using System;
using AgeOfJarls.Core;
using AgeOfJarls.Settlers;
using UnityEngine;

namespace AgeOfJarls.AI
{
    /// <summary>
    /// aoj_trace: a timeline of what the settlers this machine simulates decide - activity changes, chest trips, loot
    /// targets, stuck and blocked paths - in the BepInEx log, so behaviour can be judged over minutes instead of by eye.
    /// Off by default; callers check <see cref="On"/> before building a message.
    /// </summary>
    internal static class AiTrace
    {
        internal static bool On;

        internal static void Write(Component who, string what)
        {
            if (who == null)
            {
                return;
            }
            Vector3 p = who.transform.position;
            Settler settler = who.GetComponent<Settler>();
            string name = settler != null && settler.Identity != null ? settler.DisplayName : who.name;
            Log.Info("Trace", FormattableString.Invariant($"{Time.time:0.0} {name} @{p.x:0},{p.z:0}: ") + what);
        }
    }
}
