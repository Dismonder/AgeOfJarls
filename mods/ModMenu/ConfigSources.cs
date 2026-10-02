using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;

namespace ModMenu
{
    /// <summary>
    /// Every config file a loaded mod uses: its own Config, plus extra ConfigFile objects some mods create (one file per
    /// feature, a shared library file...). Found in the plugin's fields and in static fields of its assembly - a static
    /// ConfigFile, or a static ConfigEntry whose file is not the plugin's. Types with a static constructor are skipped:
    /// reading their fields could run mod code that has not run yet.
    /// </summary>
    internal static class ConfigSources
    {
        private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags StaticFields = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private static readonly Dictionary<BaseUnityPlugin, List<ConfigFile>> Cache = new Dictionary<BaseUnityPlugin, List<ConfigFile>>();

        public static List<ConfigFile> For(BaseUnityPlugin plugin)
        {
            if (plugin == null)
            {
                return new List<ConfigFile>();
            }
            if (Cache.TryGetValue(plugin, out List<ConfigFile> cached))
            {
                return cached;
            }

            var files = new List<ConfigFile>();
            Add(files, plugin.Config);
            try
            {
                for (Type type = plugin.GetType(); type != null && type != typeof(BaseUnityPlugin); type = type.BaseType)
                {
                    foreach (FieldInfo field in type.GetFields(InstanceFields | BindingFlags.DeclaredOnly))
                    {
                        AddFromValue(files, field.FieldType, () => field.GetValue(plugin));
                    }
                }
                foreach (Type type in LoadableTypes(plugin.GetType().Assembly))
                {
                    if (type.ContainsGenericParameters || type.TypeInitializer != null)
                    {
                        continue;
                    }
                    foreach (FieldInfo field in type.GetFields(StaticFields))
                    {
                        if (!field.IsLiteral)
                        {
                            AddFromValue(files, field.FieldType, () => field.GetValue(null));
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not look for extra config files of {plugin.Info?.Metadata?.Name}: {e.Message}");
            }

            Cache[plugin] = files;
            return files;
        }

        private static void AddFromValue(List<ConfigFile> files, Type fieldType, Func<object> read)
        {
            if (!typeof(ConfigFile).IsAssignableFrom(fieldType) && !typeof(ConfigEntryBase).IsAssignableFrom(fieldType))
            {
                return;
            }
            try
            {
                object value = read();
                Add(files, value as ConfigFile ?? (value as ConfigEntryBase)?.ConfigFile);
            }
            catch (Exception)
            {
                // A field we cannot read is simply not a source.
            }
        }

        private static void Add(List<ConfigFile> files, ConfigFile file)
        {
            // BepInEx's own BepInEx.cfg is not a mod's setting.
            if (file != null && !files.Contains(file)
                && !string.Equals(file.ConfigFilePath, Paths.BepInExConfigPath, StringComparison.OrdinalIgnoreCase))
            {
                files.Add(file);
            }
        }

        private static IEnumerable<Type> LoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                // A mod referencing an optional, missing assembly still has its other types.
                return e.Types.Where(t => t != null);
            }
        }
    }
}
