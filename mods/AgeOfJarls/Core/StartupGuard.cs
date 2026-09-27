using System;
using System.Linq;
using HarmonyLib;

namespace AgeOfJarls.Core
{
    /// <summary>
    /// Unity only calls ZNet.Start - which loads and generates the world - if ZNet.Awake finished without an exception.
    /// A throwing ZNet.Awake postfix of any mod (seen in the wild: M182 Admin Panel + Companion registering the same RPC
    /// twice) otherwise leaves every player on an endless loading screen. By the time a postfix runs, vanilla ZNet.Awake
    /// has completed, so this finalizer logs such an exception and lets the world load. Anything else is rethrown.
    /// </summary>
    [HarmonyPatch(typeof(ZNet), nameof(ZNet.Awake))]
    internal static class StartupGuard
    {
        private const string Module = "Core";

        private static Exception Finalizer(Exception __exception)
        {
            if (__exception == null || AoJConfig.GuardWorldStartup == null || !AoJConfig.GuardWorldStartup.Value)
            {
                return __exception;
            }

            string culprit = __exception.StackTrace?
                .Split('\n')
                .Select(line => line.Trim())
                .FirstOrDefault(line => line.Contains(".Postfix"));
            if (culprit == null)
            {
                // Vanilla code or a prefix failed: ZNet may be half initialized, so the failure must stay visible.
                return __exception;
            }

            Log.Error(Module, $"A ZNet.Awake patch of another mod threw {__exception.GetType().Name}: {__exception.Message}. " +
                              $"The world keeps loading thanks to General.GuardWorldStartup, but that mod is likely broken - fix or disable it. Culprit: {culprit}");
            return null;
        }
    }
}
