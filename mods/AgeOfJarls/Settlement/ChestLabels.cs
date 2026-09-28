using AgeOfJarls.Core;
using AgeOfJarls.Net;
using HarmonyLib;

namespace AgeOfJarls.Settlement
{
    /// <summary>
    /// [Shift+E] on a chest in a settlement gives it a kind - wood, ores, food... - and settlers then put only that
    /// kind in it (see SettlementStorage); again cycles to the next kind, and back to "anything". Members (Karl and up)
    /// may change it; the chest's owner checks the sender's rank and writes it to the chest's ZDO, so every machine
    /// sorts alike. Vanilla uses [E] and holding [E] on chests, not [Shift+E].
    /// </summary>
    internal static class ChestLabels
    {
        private const string Module = "Storage";

        /// <summary>
        /// The settlement whose settlers store into this chest: a public chest piece inside one, not the Settlement
        /// Cauldron (food only), an Armory, the Obliterator or a Hauler's input chest. Null otherwise.
        /// </summary>
        internal static JarlTable SettlementOf(Container chest)
        {
            if (chest == null || chest.m_piece == null || chest.m_privacy != Container.PrivacySetting.Public ||
                chest.GetComponent<Incinerator>() != null || chest.GetComponent<Army.Armory>() != null ||
                SettlementStorage.IsCauldron(chest) || SettlementStorage.IsInputChest(chest))
            {
                return null;
            }
            return JarlTable.FindContaining(chest.transform.position);
        }

        /// <summary>On the chest's owner (the RPC goes there; passed on if ownership moved meanwhile).</summary>
        internal static void RPC_SetKind(Container chest, long sender, string kind)
        {
            ZNetView view = chest.m_nview;
            if (view == null || !view.IsValid())
            {
                return;
            }
            if (!view.IsOwner())
            {
                view.InvokeRPC(Keys.RpcChestKind, kind);
                return;
            }
            JarlTable table = SettlementOf(chest);
            Player player = Peers.FindPlayer(sender);
            kind = kind ?? "";
            if (table == null || !table.RoleOf(player).Allows(SettlementRight.Live) ||
                (kind.Length > 0 && !SettlementStorage.ChestKinds().Contains(kind)))
            {
                Log.Warning(Module, $"Chest kind '{kind}' from peer {sender} refused");
                return;
            }
            view.GetZDO().Set(Keys.ZdoChestKind, kind);
        }

        private static bool MayLabel(Container chest, out JarlTable table)
        {
            table = SettlementOf(chest);
            return table != null && Player.m_localPlayer != null && table.RoleOf(Player.m_localPlayer).Allows(SettlementRight.Live);
        }

        [HarmonyPatch(typeof(Container), nameof(Container.GetHoverText))]
        private static class HoverPatch
        {
            [HarmonyPostfix]
            private static void Postfix(Container __instance, ref string __result)
            {
                if (Localization.instance == null || !MayLabel(__instance, out _))
                {
                    return;
                }
                string kind = SettlementStorage.KindToken(SettlementStorage.AssignedKind(__instance));
                __result += Localization.instance.Localize($"\n[<color=yellow><b>$KEY_AltPlace + $KEY_Use</b></color>] $aoj_chest_for: {kind}");
            }
        }

        [HarmonyPatch(typeof(Container), nameof(Container.Interact))]
        private static class InteractPatch
        {
            [HarmonyPrefix]
            private static bool Prefix(Container __instance, Humanoid character, bool hold, bool alt, ref bool __result)
            {
                if (!alt || hold || !(character is Player player) || player != Player.m_localPlayer ||
                    __instance.m_nview == null || !__instance.m_nview.IsValid() || SettlementOf(__instance) == null)
                {
                    return true;
                }
                __result = true;
                if (!MayLabel(__instance, out _))
                {
                    player.Message(MessageHud.MessageType.Center, Permissions.Denied(SettlementRight.Live));
                    return false;
                }
                string next = SettlementStorage.NextKind(SettlementStorage.AssignedKind(__instance));
                __instance.m_nview.InvokeRPC(Keys.RpcChestKind, next);
                player.Message(MessageHud.MessageType.Center,
                    Localization.instance.Localize("$aoj_msg_chest_for", Localization.instance.Localize(SettlementStorage.KindToken(next))));
                return false;
            }
        }
    }
}
