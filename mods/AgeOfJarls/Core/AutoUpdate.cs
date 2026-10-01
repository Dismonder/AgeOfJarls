using System;
using System.Collections;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;

namespace AgeOfJarls.Core
{
    /// <summary>
    /// Keeps the mod current from its update page (Updates/Url, a Cloudflare Pages site published by
    /// tools/publish-update.ps1). In the main menu - at start and after leaving a world - it reads the page's
    /// manifest (version, zip, SHA-256); when a newer version is listed it downloads the zip, checks the hash and
    /// puts the files in place for the next start: the game keeps the loaded DLL locked, but Windows lets it be
    /// renamed, so the old one becomes AgeOfJarls.dll.old (deleted at the next start) and the new one takes its name.
    /// Nothing from the download runs in this session; the player is told to restart. Every player on a server gets
    /// the same version this way at their next start (the game refuses mismatched versions anyway).
    /// </summary>
    internal static class AutoUpdate
    {
        private const string Module = "Update";
        private const int ManifestTimeoutSeconds = 15;
        private const int DownloadTimeoutSeconds = 180;
        private const int MaxZipBytes = 50 * 1024 * 1024;
        private const float RecheckSeconds = 600f;
        private const string OldSuffix = ".old";
        private const string ZipFolder = "plugins/AgeOfJarls/";

        // Filled by JsonUtility from the page's manifest.json.
#pragma warning disable 649
        [Serializable]
        private sealed class Manifest
        {
            public string version;
            public string url;
            public string sha256;
            public string notes;
        }
#pragma warning restore 649

        private static MonoBehaviour s_runner;
        private static string s_pluginDir;
        private static bool s_checking;
        private static float s_nextCheck;

        /// <summary>The version downloaded and put in place this session, waiting for a restart; null if none.</summary>
        internal static string PendingVersion { get; private set; }

        internal static void Init(MonoBehaviour runner, string pluginDir)
        {
            s_runner = runner;
            s_pluginDir = pluginDir;
            CleanOld(pluginDir);
        }

        /// <summary>From the main menu: one check at a time, not more often than every ten minutes.</summary>
        internal static void CheckNow()
        {
            if (s_runner == null || s_checking || PendingVersion != null || Time.realtimeSinceStartup < s_nextCheck ||
                AoJConfig.AutoUpdate == null || !AoJConfig.AutoUpdate.Value)
            {
                return;
            }
            s_nextCheck = Time.realtimeSinceStartup + RecheckSeconds;
            s_runner.StartCoroutine(Check());
        }

        // Files the last update left behind: the DLL of that time is no longer loaded, so it can go now.
        private static void CleanOld(string dir)
        {
            try
            {
                foreach (string file in Directory.GetFiles(dir, "*" + OldSuffix, SearchOption.AllDirectories))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch (IOException)
                    {
                        // Still locked: next time.
                    }
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Log.Warning(Module, $"Cannot clean up old files: {e.Message}");
            }
        }

        private static IEnumerator Check()
        {
            s_checking = true;
            try
            {
                string url = (AoJConfig.UpdateUrl.Value ?? "").Trim();
                if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    Log.Warning(Module, "Updates/Url is not an https address; no update check");
                    yield break;
                }

                string text;
                using (UnityWebRequest request = UnityWebRequest.Get(url))
                {
                    request.timeout = ManifestTimeoutSeconds;
                    yield return request.SendWebRequest();
                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        Log.Info(Module, $"Update check skipped: {request.error}");
                        yield break;
                    }
                    text = request.downloadHandler.text;
                }

                Manifest manifest = Parse(text);
                if (manifest == null)
                {
                    yield break;
                }
                // System.Version: the game has a Version class of its own in the global namespace.
                System.Version remote;
                System.Version local;
                if (!System.Version.TryParse(manifest.version, out remote) || !System.Version.TryParse(PluginInfo.Version, out local))
                {
                    Log.Warning(Module, $"Unreadable version on the update page: '{manifest.version}'");
                    yield break;
                }
                if (remote <= local)
                {
                    Log.Info(Module, $"{PluginInfo.Name} {PluginInfo.Version} is current (the update page lists {manifest.version})");
                    yield break;
                }
                if (string.IsNullOrEmpty(manifest.url) || !manifest.url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrEmpty(manifest.sha256))
                {
                    Log.Warning(Module, "The update page lists a newer version without an https address or a checksum; ignored");
                    yield break;
                }

                Log.Info(Module, $"{PluginInfo.Name} {manifest.version} is available, downloading {manifest.url}");
                byte[] zip;
                using (UnityWebRequest request = UnityWebRequest.Get(manifest.url))
                {
                    request.timeout = DownloadTimeoutSeconds;
                    yield return request.SendWebRequest();
                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        Log.Warning(Module, $"Download failed: {request.error}");
                        yield break;
                    }
                    zip = request.downloadHandler.data;
                }
                if (zip == null || zip.Length == 0 || zip.Length > MaxZipBytes)
                {
                    Log.Warning(Module, $"Download of an unexpected size ({zip?.Length ?? 0} bytes); ignored");
                    yield break;
                }
                string hash = Sha256(zip);
                if (!string.Equals(hash, manifest.sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    Log.Error(Module, $"The downloaded file does not match the checksum on the update page ({hash} vs {manifest.sha256}); discarded");
                    yield break;
                }

                int files;
                try
                {
                    files = Install(zip);
                }
                catch (Exception e)
                {
                    Log.Error(Module, $"Installing {manifest.version} failed: {e.Message}");
                    yield break;
                }
                PendingVersion = manifest.version;
                Log.Info(Module, $"{PluginInfo.Name} {manifest.version} installed ({files} files); it runs from the next start of the game");
                Tell();
            }
            finally
            {
                s_checking = false;
            }
        }

        private static Manifest Parse(string text)
        {
            try
            {
                Manifest manifest = JsonUtility.FromJson<Manifest>(text);
                if (manifest == null || string.IsNullOrEmpty(manifest.version))
                {
                    Log.Warning(Module, "The update page's manifest has no version");
                    return null;
                }
                return manifest;
            }
            catch (ArgumentException e)
            {
                Log.Warning(Module, $"Unreadable manifest on the update page: {e.Message}");
                return null;
            }
        }

        // The zip is the Thunderstore package: the plugin's files under plugins/AgeOfJarls/. Each one is written next
        // to its target and then takes its place; a target in use (the DLL) is renamed first.
        private static int Install(byte[] zip)
        {
            int files = 0;
            using (var stream = new MemoryStream(zip))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string name = entry.FullName.Replace('\\', '/');
                    if (!name.StartsWith(ZipFolder, StringComparison.Ordinal) || name.EndsWith("/", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    string relative = name.Substring(ZipFolder.Length);
                    if (relative.Length == 0 || relative.Contains("..") || Path.IsPathRooted(relative))
                    {
                        continue;
                    }
                    string target = Path.Combine(s_pluginDir, relative.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    string fresh = target + ".new";
                    using (Stream from = entry.Open())
                    using (FileStream to = File.Create(fresh))
                    {
                        from.CopyTo(to);
                    }
                    if (File.Exists(target))
                    {
                        string old = target + OldSuffix;
                        if (File.Exists(old))
                        {
                            File.Delete(old);
                        }
                        File.Move(target, old);
                    }
                    File.Move(fresh, target);
                    files++;
                }
            }
            return files;
        }

        private static string Sha256(byte[] data)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(data);
                var text = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash)
                {
                    text.Append(b.ToString("x2"));
                }
                return text.ToString();
            }
        }

        private static void Tell()
        {
            if (PendingVersion != null && Player.m_localPlayer != null && Localization.instance != null)
            {
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, Localization.instance.Localize("$aoj_msg_update_ready", PendingVersion));
            }
        }

        /// <summary>The main menu is up (game start, or back from a world): look for an update.</summary>
        [HarmonyPatch(typeof(FejdStartup), nameof(FejdStartup.Start))]
        private static class MenuPatch
        {
            private static void Postfix() => CheckNow();
        }

        /// <summary>Into a world with an update waiting: say so, where the player can see it.</summary>
        [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
        private static class SpawnPatch
        {
            private static void Postfix(Player __instance)
            {
                if (__instance == Player.m_localPlayer)
                {
                    Tell();
                }
            }
        }
    }
}
