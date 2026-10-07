using System;
using System.Linq;
using System.Reflection;
using AgeOfJarls.AI;
using AgeOfJarls.Core;
using Jotunn.Managers;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AgeOfJarls.Settlers
{
    /// <summary>
    /// Builds AoJ_Settler from the vanilla Player prefab: same model, animations and equipment slots, but driven by
    /// Humanoid + MonsterAI instead of Player + PlayerController.
    /// </summary>
    internal static class SettlerPrefab
    {
        private const string Module = "Settlers";
        private const string BasePrefab = "Player";

        internal static void Register()
        {
            PrefabManager.OnVanillaPrefabsAvailable += OnVanillaPrefabsAvailable;
        }

        private static void OnVanillaPrefabsAvailable()
        {
            // Fires on every main-menu load; the prefab is built once per game session.
            PrefabManager.OnVanillaPrefabsAvailable -= OnVanillaPrefabsAvailable;
            AppearancePalette.Capture();
            Family.LoveEffects.Capture();
            try
            {
                GameObject prefab = Build();
                if (prefab != null)
                {
                    PrefabManager.Instance.AddPrefab(prefab);
                }
            }
            catch (Exception e)
            {
                Log.Error(Module, $"Building {Keys.SettlerPrefab} failed: {e}");
            }
        }

        private static GameObject Build()
        {
            GameObject prefab = PrefabManager.Instance.CreateClonedPrefab(Keys.SettlerPrefab, BasePrefab);
            Player player = prefab != null ? prefab.GetComponent<Player>() : null;
            if (player == null)
            {
                Log.Error(Module, $"Vanilla prefab '{BasePrefab}' not found or has no Player component");
                return null;
            }

            Humanoid humanoid = prefab.AddComponent<SettlerCharacter>();
            CopySerializedFields(player, humanoid);
            DestroyIfPresent<PlayerController>(prefab);
            DestroyIfPresent<Skills>(prefab);
            DestroyIfPresent<Talker>(prefab);
            Object.DestroyImmediate(player);

            humanoid.m_name = "$aoj_settler";
            humanoid.m_faction = Character.Faction.Players;
            // Humanoid.Start hands these to every non-player on every load (seen in game: the Player prefab's torch),
            // so a settler would carry free items; settlers only get what players give them.
            humanoid.m_defaultItems = new GameObject[0];
            humanoid.m_randomWeapon = new GameObject[0];
            humanoid.m_randomArmor = new GameObject[0];
            humanoid.m_randomShield = new GameObject[0];
            humanoid.m_randomSets = new Humanoid.ItemSet[0];
            humanoid.m_randomItems = new Humanoid.RandomItem[0];
            // Players live in their profiles; settlers must be saved in the world.
            prefab.GetComponent<ZNetView>().m_persistent = true;
            MoveOffPlayerLayer(prefab);

            // No m_avoidFire: settlers are tamed and vanilla skips fire avoidance for tamed creatures (BaseAI.AvoidFire).
            MonsterAI ai = prefab.AddComponent<SettlerAI>();
            ai.m_attackPlayerObjects = false;
            ai.m_viewRange = 30f;
            ai.m_randomMoveRange = AoJConfig.SettlerWanderRange.Value;
            ai.m_randomMoveInterval = 8f;

            prefab.AddComponent<Settler>();
            Log.Info(Module, $"Built {Keys.SettlerPrefab}: {string.Join(", ", prefab.GetComponents<Component>().Select(c => c.GetType().Name))}");
            return prefab;
        }

        // Instances only receive serialized fields (public or [SerializeField]), so those are the ones worth copying:
        // model references, speeds, hit and death effects, the unarmed weapon...
        private static void CopySerializedFields(Humanoid from, Humanoid to)
        {
            for (Type type = typeof(Humanoid); type != null && type != typeof(MonoBehaviour); type = type.BaseType)
            {
                foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if ((field.IsPublic && !field.IsNotSerialized) || field.IsDefined(typeof(SerializeField), false))
                    {
                        field.SetValue(to, field.GetValue(from));
                    }
                }
            }
        }

        private static void DestroyIfPresent<T>(GameObject prefab) where T : Component
        {
            T component = prefab.GetComponent<T>();
            if (component != null)
            {
                Object.DestroyImmediate(component);
            }
        }

        // Player.m_interactMask contains "character" but not "player": on the player layer nobody could hover a settler.
        private static void MoveOffPlayerLayer(GameObject prefab)
        {
            int playerLayer = LayerMask.NameToLayer("player");
            int characterLayer = LayerMask.NameToLayer("character");
            if (playerLayer < 0 || characterLayer < 0)
            {
                return;
            }

            int moved = 0;
            foreach (Transform child in prefab.GetComponentsInChildren<Transform>(true))
            {
                if (child.gameObject.layer == playerLayer)
                {
                    child.gameObject.layer = characterLayer;
                    moved++;
                }
            }
            Log.Debug(Module, $"Moved {moved} object(s) from layer 'player' to 'character'");
        }
    }
}
