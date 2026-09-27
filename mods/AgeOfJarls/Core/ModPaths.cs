using System.IO;

namespace AgeOfJarls.Core
{
    internal static class ModPaths
    {
        /// <summary>Shipped, read-only files next to the DLL (plugins/AgeOfJarls/Assets).</summary>
        internal static string AssetsDir { get; private set; }

        /// <summary>User-editable files (BepInEx/config/AgeOfJarls).</summary>
        internal static string ConfigDir { get; private set; }

        internal static string DefsDir => Path.Combine(AssetsDir, "Defs");

        internal static string LocalizationDir => Path.Combine(AssetsDir, "Localization");

        internal static void Init(string pluginDir)
        {
            AssetsDir = Path.Combine(pluginDir, "Assets");
            ConfigDir = Path.Combine(BepInEx.Paths.ConfigPath, PluginInfo.Name);
        }
    }
}
