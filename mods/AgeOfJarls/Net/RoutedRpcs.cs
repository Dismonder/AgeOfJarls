using System;
using AgeOfJarls.Core;
using HarmonyLib;

namespace AgeOfJarls.Net
{
    /// <summary>
    /// Registration of the mod's routed RPCs (the ones aimed at a peer or at everybody, not at an object) with the
    /// session's ZRoutedRpc, which the game creates anew in ZNet.Awake. Done the moment it is created, and again after
    /// ZNet.Awake. A ZNet.Awake postfix alone is not safe: a postfix of another mod that throws there (M182 Admin Panel
    /// registering an RPC twice, see <see cref="StartupGuard"/>) stops every postfix after it, and a machine whose
    /// registration was skipped silently ignores what the others send it. Registering twice is harmless here.
    /// </summary>
    internal static class RoutedRpcs
    {
        private const string Module = "Net";

        /// <summary>Registers a handler unless one with that name is registered already (the game throws on a repeat).</summary>
        internal static void Register<T>(ZRoutedRpc rpc, string name, Action<long, T> handler)
        {
            if (rpc != null && !rpc.m_functions.ContainsKey(name.GetStableHashCode()))
            {
                rpc.Register(name, handler);
            }
        }

        private static void RegisterAll(ZRoutedRpc rpc)
        {
            if (rpc == null)
            {
                return;
            }
            Army.AlarmPins.OnNewSession(rpc);
            Recruitment.RecruitPins.Register(rpc);
            Log.Debug(Module, "Routed RPCs registered");
        }

        [HarmonyPatch(typeof(ZRoutedRpc), MethodType.Constructor, typeof(bool))]
        private static class ConstructorPatch
        {
            private static void Postfix(ZRoutedRpc __instance) => RegisterAll(__instance);
        }

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.Awake))]
        private static class AwakePatch
        {
            [HarmonyPriority(Priority.First)]
            private static void Postfix() => RegisterAll(ZRoutedRpc.instance);
        }
    }
}
