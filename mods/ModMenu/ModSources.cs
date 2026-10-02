using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BepInEx;
using Newtonsoft.Json.Linq;

namespace ModMenu
{
    internal enum SourceKind
    {
        Unknown,
        Nexus,
        Thunderstore,
    }

    internal sealed class ModSource
    {
        public SourceKind Kind;
        public int NexusId;
        public string ThunderstoreNamespace;
        public string ThunderstoreName;
        /// <summary>The version of the downloaded file (Vortex archive / Thunderstore manifest); may differ from the plugin's own.</summary>
        public string InstalledVersion;
        /// <summary>True when the Thunderstore package was only guessed from the plugin's name and GUID.</summary>
        public bool Guessed;

        public string PageUrl =>
            Kind == SourceKind.Nexus ? $"https://www.nexusmods.com/valheim/mods/{NexusId}"
            : Kind == SourceKind.Thunderstore && ThunderstoreNamespace != null ? $"https://thunderstore.io/c/valheim/p/{ThunderstoreNamespace}/{ThunderstoreName}/"
            : null;
    }

    /// <summary>
    /// Works out where each mod came from without any account or API key: Vortex's deployment manifest names the
    /// Nexus archive each file came from ("Auto Store 174 0.7.1 2026-..." = mod 174, version 0.7.1), and Thunderstore /
    /// r2modman installs keep the package's manifest.json next to the DLL.
    /// </summary>
    internal static class ModSources
    {
        // Vortex archive names: "Name 174 0.7.1 2026-09-11T02-59Z xyz" (current) and "Name-241-1-0-0-1614726408" (older).
        private static readonly Regex VortexNew = new Regex(@"^(?<name>.+?) (?<id>\d+) (?<ver>\S+) \d{4}-\d{2}-\d{2}T", RegexOptions.Compiled);
        private static readonly Regex VortexOld = new Regex(@"^(?<name>.+?)-(?<id>\d+)-(?<ver>\d+(?:-\d+)*?)-\d{9,}$", RegexOptions.Compiled);
        // Thunderstore dependency strings: "Namespace-Name-1.2.3".
        private static readonly Regex Dependency = new Regex(@"^(?<ns>[^-]+)-(?<name>.+)-\d+\.\d+\.\d+$", RegexOptions.Compiled);

        public static void Detect(List<ModEntry> mods)
        {
            Dictionary<string, string> vortex = ReadVortexManifests();
            var namespaces = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var manifests = new Dictionary<ModEntry, JObject>();

            foreach (ModEntry mod in mods)
            {
                if (string.IsNullOrEmpty(mod.Location))
                {
                    mod.Source = new ModSource();
                    continue;
                }
                mod.Source = FromVortex(mod, vortex) ?? new ModSource();
                if (mod.Source.Kind == SourceKind.Unknown && FindManifest(mod.Location) is JObject manifest)
                {
                    manifests[mod] = manifest;
                    foreach (JToken dependency in manifest["dependencies"] as JArray ?? new JArray())
                    {
                        Match m = Dependency.Match((string)dependency ?? "");
                        if (m.Success)
                        {
                            namespaces[m.Groups["name"].Value] = m.Groups["ns"].Value;
                        }
                    }
                }
            }

            foreach (KeyValuePair<ModEntry, JObject> pair in manifests)
            {
                string name = (string)pair.Value["name"];
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }
                string folder = new DirectoryInfo(Path.GetDirectoryName(pair.Key.Location)).Name;
                string ns = folder.EndsWith("-" + name, StringComparison.OrdinalIgnoreCase)
                    ? folder.Substring(0, folder.Length - name.Length - 1)
                    : namespaces.TryGetValue(name, out string known) ? known : null;
                pair.Key.Source = new ModSource
                {
                    Kind = SourceKind.Thunderstore,
                    ThunderstoreName = name,
                    ThunderstoreNamespace = ns,
                    InstalledVersion = (string)pair.Value["version_number"],
                };
            }

            // Hand-built or unknown installs: try a Thunderstore package named like the plugin (checked online later).
            foreach (ModEntry mod in mods.Where(m => m.Source.Kind == SourceKind.Unknown && !string.IsNullOrEmpty(m.Location)))
            {
                mod.Source = new ModSource
                {
                    Kind = SourceKind.Thunderstore,
                    ThunderstoreName = mod.Name.Replace(" ", ""),
                    Guessed = true,
                };
            }
        }

        private static ModSource FromVortex(ModEntry mod, Dictionary<string, string> vortex)
        {
            if (!vortex.TryGetValue(Normalize(mod.Location), out string archive) || !ParseVortexArchive(archive, out int id, out string version))
            {
                return null;
            }
            return new ModSource { Kind = SourceKind.Nexus, NexusId = id, InstalledVersion = version };
        }

        /// <summary>Nexus mod id and file version from a Vortex archive name; false for names in neither known format.</summary>
        internal static bool ParseVortexArchive(string archive, out int id, out string version)
        {
            id = 0;
            version = null;
            Match m = VortexNew.Match(archive ?? "");
            if (m.Success)
            {
                version = m.Groups["ver"].Value;
            }
            else
            {
                m = VortexOld.Match(archive ?? "");
                if (!m.Success)
                {
                    return false;
                }
                version = m.Groups["ver"].Value.Replace('-', '.');
            }
            return int.TryParse(m.Groups["id"].Value, out id);
        }

        /// <summary>Full path of each deployed file -> the Vortex archive ("source") it was deployed from.</summary>
        private static Dictionary<string, string> ReadVortexManifests()
        {
            var result = new Dictionary<string, string>();
            try
            {
                foreach (string file in Directory.GetFiles(Paths.PluginPath, "vortex.deployment*.json"))
                {
                    JObject manifest = JObject.Parse(File.ReadAllText(file));
                    string target = (string)manifest["targetPath"] ?? Paths.PluginPath;
                    foreach (JToken entry in manifest["files"] as JArray ?? new JArray())
                    {
                        string relPath = (string)entry["relPath"];
                        string source = (string)entry["source"];
                        if (!string.IsNullOrEmpty(relPath) && !string.IsNullOrEmpty(source))
                        {
                            result[Normalize(Path.Combine(target, relPath))] = source;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not read the Vortex deployment manifest: {e.Message}");
            }
            return result;
        }

        /// <summary>manifest.json in the DLL's folder or any parent folder up to BepInEx/plugins.</summary>
        private static JObject FindManifest(string dll)
        {
            string root = Normalize(Paths.PluginPath);
            for (string dir = Path.GetDirectoryName(dll); !string.IsNullOrEmpty(dir) && Normalize(dir).StartsWith(root) && Normalize(dir) != root; dir = Path.GetDirectoryName(dir))
            {
                string file = Path.Combine(dir, "manifest.json");
                if (!File.Exists(file))
                {
                    continue;
                }
                try
                {
                    return JObject.Parse(File.ReadAllText(file));
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"Could not read {file}: {e.Message}");
                    return null;
                }
            }
            return null;
        }

        private static string Normalize(string path) => Path.GetFullPath(path).TrimEnd('\\', '/').ToLowerInvariant();

        /// <summary>Numeric parts of a version ("v1.4.5b" -> 1,4,5); null when it has none.</summary>
        public static int[] ParseVersion(string version)
        {
            if (string.IsNullOrEmpty(version))
            {
                return null;
            }
            int[] parts = Regex.Matches(version, @"\d+").Cast<Match>().Select(m => int.TryParse(m.Value, out int v) ? v : 0).ToArray();
            return parts.Length == 0 ? null : parts;
        }

        public static bool IsNewer(string latest, string installed)
        {
            int[] a = ParseVersion(latest);
            int[] b = ParseVersion(installed);
            if (a == null || b == null)
            {
                return false;
            }
            for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
            {
                int x = i < a.Length ? a[i] : 0;
                int y = i < b.Length ? b[i] : 0;
                if (x != y)
                {
                    return x > y;
                }
            }
            return false;
        }
    }
}
