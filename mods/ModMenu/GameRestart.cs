using System;
using System.Diagnostics;
using UnityEngine;

namespace ModMenu
{
    internal static class GameRestart
    {
        private const string SteamAppId = "892970";

        /// <summary>
        /// Quits and starts the game again through Steam once this process has exited (Steam ignores a launch while
        /// the game still runs). A hidden PowerShell waits for the exit, so nothing is left behind.
        /// </summary>
        public static void Restart()
        {
            try
            {
                int pid = Process.GetCurrentProcess().Id;
                var start = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -WindowStyle Hidden -Command \"Wait-Process -Id {pid} -ErrorAction SilentlyContinue; Start-Process 'steam://rungameid/{SteamAppId}'\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                Process.Start(start);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Could not schedule the restart, the game will only quit: {e.Message}");
            }
            Application.Quit();
        }
    }
}
