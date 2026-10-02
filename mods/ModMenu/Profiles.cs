using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ModMenu
{
    /// <summary>
    /// Named sets of switched-off mods, one file per profile in BepInEx/config/ModMenu/profiles/&lt;name&gt;.txt.
    /// A profile lists the mods that are OFF, so mods installed later start switched on in every profile.
    /// </summary>
    internal static class Profiles
    {
        public static List<string> Names()
        {
            try
            {
                if (!Directory.Exists(ModCatalog.ProfilesFolder))
                {
                    return new List<string>();
                }
                return Directory.GetFiles(ModCatalog.ProfilesFolder, "*.txt")
                    .Select(Path.GetFileNameWithoutExtension)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not list profiles: {e.Message}");
                return new List<string>();
            }
        }

        public static HashSet<string> Read(string name) => ModCatalog.ReadGuidFile(PathOf(name));

        /// <summary>The profile whose set of switched-off mods equals the current one, if any.</summary>
        public static string Matching(HashSet<string> disabled) =>
            Names().FirstOrDefault(n => Read(n).SetEquals(disabled));

        /// <summary>Returns the name actually used (invalid file-name characters removed), or null on failure.</summary>
        public static string Save(string name, IEnumerable<string> disabledGuids)
        {
            name = Clean(name);
            if (name.Length == 0)
            {
                return null;
            }
            try
            {
                Directory.CreateDirectory(ModCatalog.ProfilesFolder);
                var lines = new List<string> { "# ModMenu profile: mods switched off (one plugin GUID per line)." };
                lines.AddRange(disabledGuids.OrderBy(g => g, StringComparer.OrdinalIgnoreCase));
                File.WriteAllLines(PathOf(name), lines.ToArray());
                return name;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Could not save profile {name}: {e.Message}");
                return null;
            }
        }

        public static void Delete(string name)
        {
            try
            {
                File.Delete(PathOf(name));
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Could not delete profile {name}: {e.Message}");
            }
        }

        private static string PathOf(string name) => Path.Combine(ModCatalog.ProfilesFolder, Clean(name) + ".txt");

        private static string Clean(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            return new string((name ?? "").Where(c => !invalid.Contains(c)).ToArray()).Trim();
        }
    }
}
