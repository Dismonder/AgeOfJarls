using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;

namespace ModMenu
{
    internal enum ModState
    {
        Loaded,
        Disabled,
        Failed,
    }

    internal sealed class ModEntry
    {
        public string Guid;
        public string Name;
        public string Version;
        public string Location;
        public ModState State;
        public string Error;
        /// <summary>Set when the plugin limits itself to other executables (e.g. a server-only mod): not an error.</summary>
        public string OnlyFor;
        public BaseUnityPlugin Instance;
        public string[] HardDependencies = new string[0];
        public bool Protected;

        /// <summary>Where the mod came from and where to look for updates (filled by <see cref="ModSources"/>).</summary>
        public ModSource Source;
        public string LatestVersion;
        public bool UpdateAvailable;
    }

    /// <summary>
    /// Every BepInEx plugin found at startup - loaded, switched off by the patcher, or failed - plus the set of mods the
    /// player wants switched off at the next start (BepInEx/config/ModMenu/disabled.txt, read by ModMenu.Patcher).
    /// </summary>
    internal static class ModCatalog
    {
        private const string DataSkipped = "ModMenu.Patcher.Skipped";
        private const string DataProtected = "ModMenu.Patcher.Protected";
        private const string DataDisabledFile = "ModMenu.Patcher.DisabledFile";
        private const string DataDiscovering = "ModMenu.Discovering";

        private static List<ModEntry> _mods;
        private static HashSet<string> _disabledAtStart;
        private static HashSet<string> _wanted;

        public static string Folder => Path.Combine(Paths.ConfigPath, "ModMenu");
        public static string DisabledFile => Path.Combine(Folder, "disabled.txt");
        public static string ProfilesFolder => Path.Combine(Folder, "profiles");

        /// <summary>False when ModMenu.Patcher is missing from BepInEx/patchers: switching mods off cannot work.</summary>
        public static bool PatcherActive => AppDomain.CurrentDomain.GetData(DataDisabledFile) != null;

        public static List<ModEntry> Mods
        {
            get
            {
                if (_mods == null)
                {
                    Build();
                }
                return _mods;
            }
        }

        public static bool IsWantedDisabled(string guid)
        {
            _ = Mods;
            return _wanted.Contains(guid);
        }

        public static void SetWantedDisabled(string guid, bool disabled)
        {
            _ = Mods;
            ModEntry mod = _mods.FirstOrDefault(m => m.Guid == guid);
            if (mod != null && mod.Protected)
            {
                return;
            }
            if (disabled ? _wanted.Add(guid) : _wanted.Remove(guid))
            {
                SaveWanted();
            }
        }

        public static HashSet<string> WantedDisabled
        {
            get
            {
                _ = Mods;
                return new HashSet<string>(_wanted, StringComparer.OrdinalIgnoreCase);
            }
        }

        public static void ReplaceWanted(IEnumerable<string> guids)
        {
            _ = Mods;
            var protectedGuids = new HashSet<string>(_mods.Where(m => m.Protected).Select(m => m.Guid), StringComparer.OrdinalIgnoreCase);
            _wanted = new HashSet<string>(guids.Where(g => !protectedGuids.Contains(g)), StringComparer.OrdinalIgnoreCase);
            SaveWanted();
        }

        /// <summary>True when the mods switched on/off differ from what was loaded at startup.</summary>
        public static bool RestartNeeded
        {
            get
            {
                _ = Mods;
                return _mods.Any(m => _wanted.Contains(m.Guid) != _disabledAtStart.Contains(m.Guid));
            }
        }

        /// <summary>Loaded mods that hard-depend on <paramref name="guid"/>; they stop loading with it.</summary>
        public static List<ModEntry> Dependents(string guid) =>
            Mods.Where(m => m.HardDependencies.Contains(guid, StringComparer.OrdinalIgnoreCase)).ToList();

        /// <summary>Hard dependencies of <paramref name="mod"/> that are missing or switched off for the next start.</summary>
        public static List<string> MissingDependencies(ModEntry mod)
        {
            var missing = new List<string>();
            foreach (string dependency in mod.HardDependencies)
            {
                ModEntry target = Mods.FirstOrDefault(m => string.Equals(m.Guid, dependency, StringComparison.OrdinalIgnoreCase));
                if (target == null || _wanted.Contains(target.Guid))
                {
                    missing.Add(target?.Name ?? dependency);
                }
            }
            return missing;
        }

        private static void Build()
        {
            var mods = new Dictionary<string, ModEntry>(StringComparer.OrdinalIgnoreCase);
            string[] protectedGuids = AppDomain.CurrentDomain.GetData(DataProtected) as string[] ?? new[] { PluginInfo.Guid };

            foreach (BepInEx.PluginInfo info in Chainloader.PluginInfos.Values)
            {
                mods[info.Metadata.GUID] = FromInfo(info, info.Instance != null ? ModState.Loaded : ModState.Failed);
            }

            _disabledAtStart = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (AppDomain.CurrentDomain.GetData(DataSkipped) is List<BepInEx.PluginInfo> skipped)
            {
                foreach (BepInEx.PluginInfo info in skipped)
                {
                    mods[info.Metadata.GUID] = FromInfo(info, ModState.Disabled);
                    _disabledAtStart.Add(info.Metadata.GUID);
                }
            }

            // Plugins the chainloader refused (missing dependency, incompatible, exception) never reach PluginInfos.
            foreach (BepInEx.PluginInfo info in DiscoverAll())
            {
                if (!mods.ContainsKey(info.Metadata.GUID))
                {
                    ModEntry failed = FromInfo(info, ModState.Failed);
                    failed.Error = Chainloader.DependencyErrors.FirstOrDefault(e => e.Contains("[" + info + "]"));
                    List<string> processes = info.Processes.Select(p => p.ProcessName).ToList();
                    if (processes.Count > 0 && !processes.Any(p => string.Equals(p.Replace(".exe", ""), Paths.ProcessName, StringComparison.OrdinalIgnoreCase)))
                    {
                        failed.OnlyFor = string.Join(", ", processes.ToArray());
                    }
                    mods[info.Metadata.GUID] = failed;
                }
            }

            foreach (ModEntry mod in mods.Values)
            {
                mod.Protected = protectedGuids.Contains(mod.Guid, StringComparer.OrdinalIgnoreCase);
            }

            _mods = mods.Values.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
            _wanted = ReadGuidFile(DisabledFile);
            _wanted.RemoveWhere(g => _mods.Any(m => m.Protected && string.Equals(m.Guid, g, StringComparison.OrdinalIgnoreCase)));
            ModSources.Detect(_mods);
            Plugin.Log.LogInfo($"Found {_mods.Count} mods: {_mods.Count(m => m.State == ModState.Loaded)} loaded, "
                + $"{_mods.Count(m => m.State == ModState.Disabled)} switched off, {_mods.Count(m => m.State == ModState.Failed)} not loaded; "
                + $"sources: {_mods.Count(m => m.Source.Kind == SourceKind.Nexus)} Nexus, {_mods.Count(m => m.Source.Kind == SourceKind.Thunderstore)} Thunderstore");
        }

        /// <summary>Everything in BepInEx/plugins, from BepInEx's own type cache (cheap; the patcher only edits the copy it returns).</summary>
        private static IEnumerable<BepInEx.PluginInfo> DiscoverAll()
        {
            Dictionary<string, List<BepInEx.PluginInfo>> found;
            // Tells ModMenu.Patcher not to filter this call: the menu lists switched-off mods too.
            AppDomain.CurrentDomain.SetData(DataDiscovering, true);
            try
            {
                found = TypeLoader.FindPluginTypes(Paths.PluginPath, Chainloader.ToPluginInfo, null, "chainloader");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not list the plugins folder: {e.Message}");
                yield break;
            }
            finally
            {
                AppDomain.CurrentDomain.SetData(DataDiscovering, false);
            }
            foreach (KeyValuePair<string, List<BepInEx.PluginInfo>> pair in found)
            {
                foreach (BepInEx.PluginInfo info in pair.Value)
                {
                    if (string.IsNullOrEmpty(info.Location))
                    {
                        AccessLocation(info, pair.Key);
                    }
                    yield return info;
                }
            }
        }

        private static void AccessLocation(BepInEx.PluginInfo info, string location) =>
            HarmonyLib.AccessTools.Property(typeof(BepInEx.PluginInfo), nameof(BepInEx.PluginInfo.Location)).SetValue(info, location, null);

        private static ModEntry FromInfo(BepInEx.PluginInfo info, ModState state) => new ModEntry
        {
            Guid = info.Metadata.GUID,
            Name = info.Metadata.Name,
            Version = info.Metadata.Version?.ToString() ?? "?",
            Location = info.Location,
            State = state,
            Instance = info.Instance,
            HardDependencies = info.Dependencies
                .Where(d => (d.Flags & BepInDependency.DependencyFlags.HardDependency) != 0)
                .Select(d => d.DependencyGUID).ToArray(),
        };

        private static void SaveWanted()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                var lines = new List<string>
                {
                    "# Mods switched off by ModMenu (one plugin GUID per line). Delete a line, or this file, to switch a mod back on.",
                };
                lines.AddRange(_wanted.OrderBy(g => g, StringComparer.OrdinalIgnoreCase));
                File.WriteAllLines(DisabledFile, lines.ToArray());
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Could not save {DisabledFile}: {e.Message}");
            }
        }

        internal static HashSet<string> ReadGuidFile(string file)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(file))
                {
                    foreach (string raw in File.ReadAllLines(file))
                    {
                        string line = raw.Trim();
                        if (line.Length > 0 && !line.StartsWith("#"))
                        {
                            set.Add(line);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not read {file}: {e.Message}");
            }
            return set;
        }
    }
}
