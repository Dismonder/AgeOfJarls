using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;

namespace ModMenu.Patcher
{
    /// <summary>
    /// Everything that touches BepInEx.PluginInfo, kept out of <see cref="Patcher"/>: BepInEx reflects over the
    /// patcher class and reads TargetDLLs before it loads its patched UnityEngine.CoreModule, and resolving PluginInfo
    /// (PluginInfo -> BaseUnityPlugin -> MonoBehaviour) at that point loads the unpatched CoreModule, so the
    /// chainloader entry point never runs and the game starts with no mods at all. Only Finish() uses this class.
    /// </summary>
    internal static class ChainloaderFilter
    {
        private static System.Reflection.PropertyInfo _locationProperty;
        private static readonly List<BepInEx.PluginInfo> _seen = new List<BepInEx.PluginInfo>();

        public static void Install()
        {
            var harmony = new Harmony("com.damia.modmenu.patcher");
            var target = AccessTools.Method(typeof(TypeLoader), nameof(TypeLoader.FindPluginTypes)).MakeGenericMethod(typeof(BepInEx.PluginInfo));
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(ChainloaderFilter), nameof(FindPluginTypesPostfix)));
        }

        // Runs after the cache was saved, so editing the result never poisons BepInEx's plugin cache.
        private static void FindPluginTypesPostfix(string cacheName, Dictionary<string, List<BepInEx.PluginInfo>> __result)
        {
            // Every chainloader pass (loaders like MultiFolderLoader run one per folder), except ModMenu's own listing,
            // which needs every plugin and marks itself with DataDiscovering.
            if (cacheName != "chainloader" || __result == null || AppDomain.CurrentDomain.GetData(Patcher.DataDiscovering) is bool listing && listing)
            {
                return;
            }
            try
            {
                HashSet<string> disabled = Patcher.ReadDisabled(Patcher.DisabledFile);
                _seen.AddRange(__result.Values.SelectMany(list => list));
                List<BepInEx.PluginInfo> all = _seen;
                if (disabled.Count > 0 && !all.Any(i => string.Equals(i.Metadata.GUID, Patcher.ModMenuGuid, StringComparison.OrdinalIgnoreCase)))
                {
                    // The menu itself was removed: nothing could switch these mods back on, so load everything.
                    Patcher.Log.LogWarning("ModMenu plugin not found; ignoring disabled.txt and loading every mod.");
                    return;
                }
                HashSet<string> protectedGuids = ProtectedGuids(all);
                var skipped = AppDomain.CurrentDomain.GetData(Patcher.DataSkipped) as List<BepInEx.PluginInfo> ?? new List<BepInEx.PluginInfo>();

                foreach (string location in __result.Keys.ToList())
                {
                    List<BepInEx.PluginInfo> infos = __result[location];
                    foreach (BepInEx.PluginInfo info in infos.ToList())
                    {
                        string guid = info.Metadata.GUID;
                        if (!disabled.Contains(guid) || protectedGuids.Contains(guid))
                        {
                            continue;
                        }
                        // Chainloader.Start sets Location only on what it gets back; the menu still needs the path.
                        if (_locationProperty == null)
                        {
                            _locationProperty = AccessTools.Property(typeof(BepInEx.PluginInfo), nameof(BepInEx.PluginInfo.Location));
                        }
                        _locationProperty.SetValue(info, location, null);
                        infos.Remove(info);
                        skipped.Add(info);
                        Patcher.Log.LogMessage($"Disabled in ModMenu, not loading [{info}]");
                    }
                    if (infos.Count == 0)
                    {
                        __result.Remove(location);
                    }
                }

                AppDomain.CurrentDomain.SetData(Patcher.DataSkipped, skipped);
                AppDomain.CurrentDomain.SetData(Patcher.DataProtected, protectedGuids.ToArray());
            }
            catch (Exception e)
            {
                Patcher.Log.LogError($"Could not filter disabled mods, every mod will load: {e}");
            }
        }

        /// <summary>ModMenu and everything it needs: switching those off would lock the player out of the menu.</summary>
        private static HashSet<string> ProtectedGuids(List<BepInEx.PluginInfo> all)
        {
            var byGuid = new Dictionary<string, BepInEx.PluginInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (BepInEx.PluginInfo info in all)
            {
                byGuid[info.Metadata.GUID] = info;
            }
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Patcher.ModMenuGuid };
            var queue = new Queue<string>(result);
            while (queue.Count > 0)
            {
                if (!byGuid.TryGetValue(queue.Dequeue(), out BepInEx.PluginInfo info))
                {
                    continue;
                }
                foreach (BepInDependency dependency in info.Dependencies)
                {
                    if ((dependency.Flags & BepInDependency.DependencyFlags.HardDependency) != 0 && result.Add(dependency.DependencyGUID))
                    {
                        queue.Enqueue(dependency.DependencyGUID);
                    }
                }
            }
            return result;
        }
    }
}
