using AgeOfJarls.Core;
using HarmonyLib;

namespace AgeOfJarls.Settlement
{
    /// <summary>
    /// A settler takes a chest over the way a player opening it does: only the chest's owner writes its inventory, so
    /// the settler's machine asks the owner, who hands the ZDO over unless someone has the chest open. Two machines
    /// never write the same chest at once, which a plain ownership grab could not promise.
    /// </summary>
    [HarmonyPatch(typeof(Container), "Awake")]
    internal static class ChestAccess
    {
        [HarmonyPostfix]
        private static void Postfix(Container __instance)
        {
            // Chests built as pieces only (m_piece is set by Awake for networked containers): carts, ships and
            // tombstones never get the RPC, and a container sharing its vehicle's ZNetView is never registered twice.
            ZNetView view = __instance.m_nview;
            if (view != null && view.GetZDO() != null && __instance.m_piece != null)
            {
                view.Register(Keys.RpcChestRequest, sender => RPC_Request(__instance, sender));
            }
        }

        /// <summary>True when this machine may write the chest now; otherwise asks its owner (call again later).</summary>
        internal static bool Acquire(Container chest, bool ask)
        {
            if (chest.m_nview.IsOwner())
            {
                return true;
            }
            if (ask)
            {
                chest.m_nview.InvokeRPC(Keys.RpcChestRequest);
            }
            return false;
        }

        // On the chest's owner. Refused silently like a busy chest in vanilla: the settler asks again or gives up.
        // Only chests built as pieces: a cart's or ship's container shares its vehicle's ZDO, which must not move.
        private static void RPC_Request(Container chest, long sender)
        {
            ZNetView view = chest.m_nview;
            if (view == null || !view.IsValid() || !view.IsOwner() || sender == ZDOMan.GetSessionID() || chest.m_piece == null ||
                chest.m_privacy != Container.PrivacySetting.Public || !SettlementStorage.IsUsable(chest))
            {
                return;
            }
            ZDOMan.instance.ForceSendZDO(sender, view.GetZDO().m_uid);
            view.GetZDO().SetOwner(sender);
        }
    }
}
