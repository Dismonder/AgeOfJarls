using System.Collections.Generic;
using System.Linq;
using AgeOfJarls.Core;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace AgeOfJarls.Recruitment
{
    /// <summary>
    /// The bars a captive is locked behind: a vanilla iron gate (a wooden one if that is missing) turned into a plain
    /// breakable object - no building piece (nothing to take down for free materials), no door to open, no need for
    /// support - with more health in harder lands. A captive goes free once its guards are dead or its bars broken.
    /// </summary>
    internal sealed class CaptiveCage : MonoBehaviour
    {
        private const string Module = "Recruitment";
        internal const string PrefabName = "AoJ_CaptiveCage";
        /// <summary>A captive counts as behind bars while a cage stands this close.</summary>
        internal const float Range = 3f;
        private static readonly string[] Sources = { "iron_grate", "wood_gate" };

        internal static readonly List<CaptiveCage> Loaded = new List<CaptiveCage>();

        private void Awake()
        {
            ZNetView view = GetComponent<ZNetView>();
            if (view != null && view.IsValid())
            {
                Loaded.Add(this);
            }
        }

        private void OnDestroy()
        {
            Loaded.Remove(this);
        }

        /// <summary>Whether bars stand next to this spot (the cage is loaded wherever its captive is).</summary>
        internal static bool AnyNear(Vector3 position)
        {
            foreach (CaptiveCage cage in Loaded)
            {
                if (cage != null && (cage.transform.position - position).sqrMagnitude <= Range * Range)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Health of the bars: harder lands, stronger locks.</summary>
        internal static float HealthFor(Heightmap.Biome biome)
        {
            switch (biome)
            {
                case Heightmap.Biome.BlackForest:
                    return 300f;
                case Heightmap.Biome.Swamp:
                    return 500f;
                case Heightmap.Biome.Mountain:
                    return 700f;
                case Heightmap.Biome.Plains:
                    return 900f;
                case Heightmap.Biome.Mistlands:
                    return 1200f;
                case Heightmap.Biome.AshLands:
                case Heightmap.Biome.DeepNorth:
                    return 1500f;
                default:
                    return 150f;
            }
        }

        /// <summary>Owner of the camp: bars in front of the captive, facing it. False when there are no bars to place.</summary>
        internal static bool Place(Vector3 captive, Heightmap.Biome biome)
        {
            GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(PrefabName) : null;
            if (prefab == null)
            {
                return false;
            }
            Vector3 front = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
            Vector3 position = captive + front * 1.4f;
            GameObject cage = Instantiate(prefab, position, Quaternion.LookRotation(-front));
            ZNetView view = cage.GetComponent<ZNetView>();
            if (view != null && view.IsValid())
            {
                view.GetZDO().Set(ZDOVars.s_health, HealthFor(biome));
            }
            return true;
        }

        internal static void Register()
        {
            PrefabManager.OnVanillaPrefabsAvailable += Build;
        }

        private static void Build()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= Build;
            string source = Sources.FirstOrDefault(s => PrefabManager.Instance.GetPrefab(s) != null);
            if (source == null)
            {
                Log.Warning(Module, "No gate to build captives' bars from: captives are only guarded");
                return;
            }
            GameObject prefab = PrefabManager.Instance.CreateClonedPrefab(PrefabName, source);
            WearNTear wear = prefab.GetComponent<WearNTear>();
            Destructible bars = prefab.AddComponent<Destructible>();
            bars.m_health = HealthFor(Heightmap.Biome.Meadows);
            bars.m_damages = new HitData.DamageModifiers();
            if (wear != null)
            {
                bars.m_hitEffect = wear.m_hitEffect;
                bars.m_destroyedEffect = wear.m_destroyedEffect;
            }
            // In this order: WearNTear needs the Piece.
            foreach (Component part in new Component[] { prefab.GetComponent<Door>(), wear, prefab.GetComponent<Piece>() })
            {
                if (part != null)
                {
                    DestroyImmediate(part);
                }
            }
            prefab.AddComponent<CaptiveCage>();
            PrefabManager.Instance.AddPrefab(new CustomPrefab(prefab, false));
            Log.Info(Module, $"Built {PrefabName} from {source}");
        }
    }
}
