using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using Mono.Cecil;

namespace ModMenu.Patcher
{
    /// <summary>
    /// BepInEx 5 preloader patcher. Patches nothing in the game; it only hooks the chainloader's plugin discovery and
    /// drops the plugins whose GUIDs are listed in BepInEx/config/ModMenu/disabled.txt, so a mod can be switched off
    /// without renaming or moving its files (which would break Vortex / r2modman deployments).
    /// The plugin part of ModMenu reads what happened here through AppDomain data (no assembly reference needed).
    /// </summary>
    public static class Patcher
    {
        public const string ModMenuGuid = "com.damia.modmenu";
        public const string DataSkipped = "ModMenu.Patcher.Skipped";
        public const string DataProtected = "ModMenu.Patcher.Protected";
        public const string DataDisabledFile = "ModMenu.Patcher.DisabledFile";
        public const string DataDiscovering = "ModMenu.Discovering";

        internal static readonly ManualLogSource Log = Logger.CreateLogSource("ModMenu.Patcher");

        // NEVER reference BepInEx.PluginInfo in this class (fields, signatures, TargetDLLs, Patch): see ChainloaderFilter.

        public static IEnumerable<string> TargetDLLs { get; } = new string[0];

        public static string DisabledFile => Path.Combine(Path.Combine(Paths.ConfigPath, "ModMenu"), "disabled.txt");

        public static void Patch(AssemblyDefinition assembly)
        {
        }

        public static void Finish()
        {
            try
            {
                // Finish runs after BepInEx loaded the patched UnityEngine.CoreModule, so PluginInfo is safe from here on.
                ChainloaderFilter.Install();
                AppDomain.CurrentDomain.SetData(DataDisabledFile, DisabledFile);
            }
            catch (Exception e)
            {
                Log.LogError($"Could not hook the chainloader, every mod will load: {e}");
            }
        }

        public static HashSet<string> ReadDisabled(string file)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(file))
            {
                return set;
            }
            foreach (string raw in File.ReadAllLines(file))
            {
                string line = raw.Trim();
                if (line.Length > 0 && !line.StartsWith("#"))
                {
                    set.Add(line);
                }
            }
            return set;
        }
    }
}
