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
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public class Plugin : BaseUnityPlugin
    {
        private Harmony _harmony;

        private void Awake()
        {
            Log.Init(Logger);
            AoJConfig.Bind(Config);
            ModPaths.Init(Path.GetDirectoryName(Info.Location));

            TranslationLoader.Load();
            DefsRegistry.LoadLocal();
            DefsSync.Register();
            SettlerPrefab.Register();
            JarlTablePiece.Register();
            SettlementPieces.Register();
            Recruitment.CaptiveCage.Register();
            ConsoleCommands.Register();
            UI.CommandWheel.RegisterKey();

            _harmony = new Harmony(PluginInfo.Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);

            Log.Info("Core", $"{PluginInfo.Name} {PluginInfo.Version} loaded");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
