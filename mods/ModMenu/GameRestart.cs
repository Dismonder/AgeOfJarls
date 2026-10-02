using System;
using System.Diagnostics;
using System.Linq;
using UnityEngine;

namespace ModMenu
{
    internal static class GameRestart
    {
        private const string SteamAppId = "892970";

        /// <summary>
        /// Quits and starts the game again once this process has exited (Steam ignores a launch while the game still
        /// runs). Steam installs go through steam:// so Steam's launch options stay; any other install (Game Pass, a
        /// copied folder) restarts the same executable with the same arguments. A hidden PowerShell waits for the exit.
        /// </summary>
        /// <summary>Linux / macOS: a detached shell waits for the exit, then asks Steam (or the same binary) to start again.</summary>
        private static ProcessStartInfo UnixRestart(int pid, bool steam, string exe)
        {
            string opener = Application.platform == RuntimePlatform.OSXPlayer ? "open" : "xdg-open";
            string launch = steam ? $"{opener} steam://rungameid/{SteamAppId}" : $"\"{exe.Replace("\"", "")}\"";
            return new ProcessStartInfo
            {
                FileName = "/bin/sh",
                Arguments = $"-c 'while kill -0 {pid} 2>/dev/null; do sleep 1; done; {launch} >/dev/null 2>&1 &'",
                UseShellExecute = false,
                CreateNoWindow = true,
            };
        }

        public static void Restart()
        {
            try
            {
                Process self = Process.GetCurrentProcess();
                int pid = self.Id;
                string exe = self.MainModule?.FileName ?? "";
                bool steam = exe.IndexOf("steamapps", StringComparison.OrdinalIgnoreCase) >= 0;
                string[] args = Environment.GetCommandLineArgs();
                string argList = string.Join(",", args.Skip(1).Select(a => "'" + a.Replace("\"", "").Replace("'", "''") + "'").ToArray());
                string launch = steam
                    ? $"Start-Process 'steam://rungameid/{SteamAppId}'"
                    : $"Start-Process -FilePath '{exe.Replace("'", "''")}'" + (argList.Length > 0 ? $" -ArgumentList {argList}" : "");
                Process.Start(Application.platform == RuntimePlatform.WindowsPlayer
                    ? new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -WindowStyle Hidden -Command \"Wait-Process -Id {pid} -ErrorAction SilentlyContinue; {launch}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    }
                    : UnixRestart(pid, steam, exe));
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Could not schedule the restart, the game will only quit: {e.Message}");
            }
            Application.Quit();
        }
    }
}
