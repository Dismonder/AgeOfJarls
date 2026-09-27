using System;
using System.Collections;
using AgeOfJarls.Core;
using AgeOfJarls.Core.Defs;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;

namespace AgeOfJarls.Net
{
    /// <summary>
    /// Server-authoritative definitions: Jotunn delivers the server's set to each client during login, before the
    /// player enters the world, and <see cref="BroadcastToPeers"/> re-sends it after the host reloads the files.
    /// </summary>
    internal static class DefsSync
    {
        private const string Module = "Net";
        private static CustomRPC _rpc;

        internal static void Register()
        {
            _rpc = NetworkManager.Instance.AddRPC(Keys.RpcDefsSync, OnServerReceive, OnClientReceive);
            SynchronizationManager.Instance.AddInitialSynchronization(_rpc, CreatePackage);
        }

        /// <summary>Server or host only. Returns how many connected players were sent the definitions.</summary>
        internal static int BroadcastToPeers()
        {
            if (_rpc == null || ZNet.instance == null || !ZNet.instance.IsServer())
            {
                return 0;
            }

            var peers = ZNet.instance.GetPeers();
            if (peers.Count > 0)
            {
                _rpc.SendPackage(peers, CreatePackage());
            }
            return peers.Count;
        }

        private static ZPackage CreatePackage()
        {
            var package = new ZPackage();
            package.Write(DefsRegistry.ProtocolVersion);
            package.Write(DefsRegistry.Serialize());
            return package;
        }

        private static IEnumerator OnServerReceive(long sender, ZPackage package)
        {
            // Definitions only ever flow server -> client.
            Log.Warning(Module, $"Ignored definitions sent by peer {sender}");
            yield break;
        }

        private static IEnumerator OnClientReceive(long sender, ZPackage package)
        {
            Apply(package);
            yield break;
        }

        private static void Apply(ZPackage package)
        {
            try
            {
                int protocol = package.ReadInt();
                if (protocol != DefsRegistry.ProtocolVersion)
                {
                    Log.Error(Module, $"Server definitions use protocol {protocol}, this client expects {DefsRegistry.ProtocolVersion}");
                    return;
                }
                if (DefsRegistry.ApplyFromServer(package.ReadString()))
                {
                    Log.Info(Module, "Definitions received from the server");
                }
            }
            catch (Exception e)
            {
                // Network boundary: a malformed package must never break the login.
                Log.Error(Module, $"Malformed definitions package from the server: {e}");
            }
        }

        // Every world session starts from the local files; when joining a server they are replaced during login.
        [HarmonyPatch(typeof(ZNet), nameof(ZNet.Awake))]
        private static class ZNetAwakePatch
        {
            // High priority: a later ZNet.Awake postfix of another mod that throws (seen in the wild) must not skip this.
            [HarmonyPriority(Priority.High)]
            private static void Postfix()
            {
                try
                {
                    DefsRegistry.LoadLocal();
                }
                catch (Exception e)
                {
                    Log.Error(Module, $"Reloading local definitions failed: {e}");
                }
            }
        }
    }
}
