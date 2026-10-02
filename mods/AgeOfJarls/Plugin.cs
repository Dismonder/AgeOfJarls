using System.IO;
using AgeOfJarls.Commands;
using AgeOfJarls.Core;
using AgeOfJarls.Core.Defs;
using AgeOfJarls.Net;
using AgeOfJarls.Settlement;
using AgeOfJarls.Settlers;
using BepInEx;
using HarmonyLib;
using Jotunn.Utils;

namespace AgeOfJarls
{
    [BepInPlugin(PluginInfo.Guid, PluginInfo.Name, PluginInfo.Version)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    // Patch strictness: the settlers' network messages change between patch versions too (0.7.0 -> 0.7.1 changed the
    // portal request), and a machine reading a message in the old format would throw inside the game's RPC loop.
    // The auto-updater brings everyone to the same version at the next start.
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Patch)]
    public class Plugin : BaseUnityPlugin
    {
        private Harmony _harmony;

        private void Awake()
        {
            Log.Init(Logger);
            AoJConfig.Bind(Config);
            string pluginDir = Path.GetDirectoryName(Info.Location);
            ModPaths.Init(pluginDir);
            AutoUpdate.Init(this, pluginDir);

            TranslationLoader.Load();
            DefsRegistry.LoadLocal();
            DefsSync.Register();
            SettlerPrefab.Register();
            JarlTablePiece.Register();
            SettlementPieces.Register();
            Recruitment.CaptiveCage.Register();
            ConsoleCommands.Register();
            UI.CommandWheel.RegisterKey();
            SettlerCollision.Init();

            _harmony = new Harmony(PluginInfo.Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);

            Log.Info("Core", $"{PluginInfo.Name} {PluginInfo.Version} loaded");
        }

        private void Update()
        {
            ItemDelivery.Update();
            UI.HelpWindow.Poll();
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
