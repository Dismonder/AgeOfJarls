using System.Collections.Generic;
using AgeOfJarls.Core;
using HarmonyLib;
using UnityEngine;

namespace AgeOfJarls.Settlers
{
    /// <summary>
    /// Whether settlers bump into players and into each other. The game has every character collide with every other;
    /// with Settlers/CollideWithPlayers off, players and settlers walk through each other (no more being shoved in a
    /// doorway or pushed off a ledge by a follower), with Settlers/CollideWithSettlers off settlers walk through each
    /// other (a crowd at a chest or a gate no longer jams). Done per pair of colliders (Physics.IgnoreCollision), so
    /// everything else - walls, furniture, enemies - stays solid, and hits (sphere casts, not contacts) land as before.
    /// Applied when a settler or a player loads and again when the setting changes; the settings are synced, so every
    /// machine does the same.
    /// </summary>
    internal static class SettlerCollision
    {
        private const string Module = "Settlers";
        private static readonly List<Collider> s_a = new List<Collider>();
        private static readonly List<Collider> s_b = new List<Collider>();

        internal static void Init()
        {
            AoJConfig.CollideWithPlayers.SettingChanged += (_, __) => ApplyAll();
            AoJConfig.CollideWithSettlers.SettingChanged += (_, __) => ApplyAll();
        }

        /// <summary>A settler loaded (its colliders exist): its pairs with every player and settler here.</summary>
        internal static void OnSettlerLoaded(Settler settler)
        {
            if (settler == null)
            {
                return;
            }
            bool ignorePlayers = !AoJConfig.CollideWithPlayers.Value;
            bool ignoreSettlers = !AoJConfig.CollideWithSettlers.Value;
            foreach (Player player in Player.GetAllPlayers())
            {
                if (player != null)
                {
                    Pair(settler.gameObject, player.gameObject, ignorePlayers);
                }
            }
            foreach (Settler other in Settler.Loaded)
            {
                if (other != null && other != settler)
                {
                    Pair(settler.gameObject, other.gameObject, ignoreSettlers);
                }
            }
        }

        private static void ApplyAll()
        {
            foreach (Settler settler in Settler.Loaded)
            {
                OnSettlerLoaded(settler);
            }
            Log.Info(Module, $"Settlers {(AoJConfig.CollideWithPlayers.Value ? "collide with" : "walk through")} players and {(AoJConfig.CollideWithSettlers.Value ? "collide with" : "walk through")} each other");
        }

        // Every solid collider of one against every solid collider of the other (a character has one capsule; ragdolls
        // and attached items are left alone: they come and go).
        private static void Pair(GameObject a, GameObject b, bool ignore)
        {
            if (a == null || b == null)
            {
                return;
            }
            Collect(a, s_a);
            Collect(b, s_b);
            for (int i = 0; i < s_a.Count; i++)
            {
                for (int j = 0; j < s_b.Count; j++)
                {
                    // Unity refuses a pair with a collider switched off (a body mid-destruction): that pair is skipped,
                    // the next load of either side pairs them again.
                    if (s_a[i].enabled && s_b[j].enabled && s_a[i].gameObject.activeInHierarchy && s_b[j].gameObject.activeInHierarchy)
                    {
                        Physics.IgnoreCollision(s_a[i], s_b[j], ignore);
                    }
                }
            }
            s_a.Clear();
            s_b.Clear();
        }

        private static void Collect(GameObject owner, List<Collider> into)
        {
            into.Clear();
            Character character = owner.GetComponent<Character>();
            if (character != null && character.m_collider != null)
            {
                into.Add(character.m_collider);
                return;
            }
            foreach (Collider collider in owner.GetComponents<Collider>())
            {
                if (collider != null && collider.enabled && !collider.isTrigger)
                {
                    into.Add(collider);
                }
            }
        }

        /// <summary>A player appeared (yours or another's): its pairs with the settlers loaded here.</summary>
        [HarmonyPatch(typeof(Player), "Awake")]
        private static class PlayerPatch
        {
            private static void Postfix(Player __instance)
            {
                if (__instance == null || AoJConfig.CollideWithPlayers == null || AoJConfig.CollideWithPlayers.Value)
                {
                    return;
                }
                foreach (Settler settler in Settler.Loaded)
                {
                    if (settler != null)
                    {
                        Pair(settler.gameObject, __instance.gameObject, true);
                    }
                }
            }
        }
    }
}
