using System;
using System.IO;
using Jotunn.Managers;

namespace AgeOfJarls.Core
{
    /// <summary>
    /// Loads Assets/Localization/&lt;Language&gt;/*.json (keys without "$", e.g. "aoj_tier_0").
    /// The folder is deliberately not called "Translations", so Jotunn's automatic loader does not add it twice.
    /// </summary>
    internal static class TranslationLoader
    {
        private const string Module = "Core";

        internal static void Load()
        {
            string root = ModPaths.LocalizationDir;
            if (!Directory.Exists(root))
            {
                Log.Warning(Module, $"Localization folder missing: {root}");
                return;
            }

            var localization = LocalizationManager.Instance.GetLocalization();
            foreach (string languageDir in Directory.GetDirectories(root))
            {
                string language = Path.GetFileName(languageDir);
                foreach (string file in Directory.GetFiles(languageDir, "*.json"))
                {
                    try
                    {
                        localization.AddJsonFile(language, File.ReadAllText(file));
                        Log.Debug(Module, $"Loaded {language}/{Path.GetFileName(file)}");
                    }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                    {
                        Log.Error(Module, $"Cannot read {file}: {e.Message}");
                    }
                }
            }
        }
    }
}
