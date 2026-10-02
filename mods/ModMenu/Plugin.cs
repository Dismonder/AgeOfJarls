using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Utils;

namespace ModMenu
{
    [BepInPlugin(PluginInfo.Guid, "Mod Menu", PluginInfo.Version)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    // Client-side tool only: never forces the other players or the server to have it.
    [NetworkCompatibility(CompatibilityLevel.NotEnforced, VersionStrictness.None)]
    public class Plugin : BaseUnityPlugin
    {
        internal static Plugin Instance;
        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> CheckUpdatesOnOpen;
        internal static ConfigEntry<KeyboardShortcut> OpenKey;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            CheckUpdatesOnOpen = Config.Bind("Updates", "CheckOnOpen", true,
                "Check Nexus Mods and Thunderstore for newer versions the first time the menu is opened in a session.");

            OpenKey = Config.Bind("General", "OpenKey", new KeyboardShortcut(UnityEngine.KeyCode.M, UnityEngine.KeyCode.LeftControl),
                "Opens the Mods window in the main menu - also when another mod reworks the menu and hides the button.");

            _harmony = new Harmony(PluginInfo.Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);

            Log.LogInfo($"{PluginInfo.Name} {PluginInfo.Version} loaded");
        }

        private void Update()
        {
            FejdStartup startup = FejdStartup.instance;
            if (startup != null && !UI.ModMenuWindow.IsOpen && startup.m_menuList != null && startup.m_menuList.activeInHierarchy
                && OpenKey.Value.IsDown())
            {
                UI.MainMenuButton.OpenWindow(startup);
            }
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
