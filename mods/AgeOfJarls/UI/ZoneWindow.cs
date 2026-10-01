using AgeOfJarls.Core;
using AgeOfJarls.Settlement;
using HarmonyLib;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace AgeOfJarls.UI
{
    /// <summary>
    /// Marking a settlement's areas on the big map: the zone key (Commands/ZoneKey, Z) with the cursor over the map
    /// opens this window for the area under the cursor, or for a new one there, in the nearest loaded settlement the
    /// player may manage (a Hersir or the Jarl). Name, kind (a warehouse, whose chests the settlers fill and empty
    /// like the ones at home, or any other named area), radius; saved on the table by its owner (JarlTable.RPC_Action).
    /// </summary>
    internal sealed class ZoneWindow : AojWindow
    {
        private const float Width = 520f;
        private const float Height = 400f;
        private const float RadiusStep = 5f;

        private static ZoneWindow s_instance;

        private JarlTable _table;
        private SettlementZone _zone;
        private bool _existing;
        private Text _title;
        private Text _where;
        private Text _kindLabel;
        private Text _radiusLabel;
        private GameObject _deleteButton;

        internal static void Open(JarlTable table, SettlementZone zone, bool existing)
        {
            if (GUIManager.CustomGUIFront == null || table == null || zone == null)
            {
                return;
            }
            if (s_instance == null)
            {
                s_instance = CreateWindow<ZoneWindow>("AoJ_ZoneWindow", Width, Height);
            }
            s_instance._table = table;
            s_instance._zone = zone;
            s_instance._existing = existing;
            s_instance.TextEntry.text = zone.Name ?? "";
            s_instance.ShowWindow();
        }

        protected override void Build(Transform root)
        {
            Color beige = GUIManager.Instance.ValheimBeige;
            _title = Label(root, new Vector2(0f, 160f), 24, GUIManager.Instance.ValheimOrange, Width - 40f, 40f, TextAnchor.MiddleCenter);
            _where = Label(root, new Vector2(0f, 120f), 15, beige, Width - 60f, 30f, TextAnchor.MiddleCenter);
            Label(root, new Vector2(-150f, 70f), 17, beige, 180f, 36f, TextAnchor.MiddleLeft).text = Localize("$aoj_zone_name");
            TextEntry = TextInput(root, "$aoj_zone_name_hint", new Vector2(70f, 70f), 300f);
            TextEntry.characterLimit = SettlementZone.MaxNameLength;
            Label(root, new Vector2(-150f, 20f), 17, beige, 180f, 36f, TextAnchor.MiddleLeft).text = Localize("$aoj_zone_kind");
            _kindLabel = Button(root, "", new Vector2(70f, 20f), 300f, CycleKind);
            Label(root, new Vector2(-150f, -30f), 17, beige, 180f, 36f, TextAnchor.MiddleLeft).text = Localize("$aoj_zone_radius");
            Button(root, "-", new Vector2(-30f, -30f), 50f, () => ChangeRadius(-RadiusStep));
            _radiusLabel = Label(root, new Vector2(70f, -30f), 18, GUIManager.Instance.ValheimOrange, 120f, 36f, TextAnchor.MiddleCenter);
            Button(root, "+", new Vector2(170f, -30f), 50f, () => ChangeRadius(RadiusStep));
            Button(root, "$aoj_btn_save", new Vector2(-150f, -120f), 150f, Save);
            _deleteButton = Button(root, "$aoj_btn_remove", new Vector2(10f, -120f), 150f, Delete).GetComponentInParent<Button>().gameObject;
            Button(root, "$aoj_btn_cancel", new Vector2(170f, -120f), 150f, Hide);
        }

        protected override bool IsTargetValid() => _table != null && _zone != null && Player.m_localPlayer != null;

        protected override void Refresh()
        {
            SettlementData data = _table.Data;
            _title.text = Localize(_existing ? "$aoj_zone_title_edit" : "$aoj_zone_title_new");
            _where.text = Localize("$aoj_zone_where", JarlTable.DisplayName(data), Utils.DistanceXZ(_zone.Center, _table.transform.position).ToString("0"));
            _kindLabel.text = Localize(_zone.Kind == SettlementZone.KindWarehouse ? "$aoj_zone_kind_warehouse" : "$aoj_zone_kind_other");
            _radiusLabel.text = $"{_zone.Radius:0} m";
            _deleteButton.SetActive(_existing);
        }

        private void CycleKind()
        {
            _zone.Kind = _zone.Kind == SettlementZone.KindWarehouse ? SettlementZone.KindOther : SettlementZone.KindWarehouse;
            RefreshNow();
        }

        private void ChangeRadius(float step)
        {
            _zone.Radius = Mathf.Clamp(_zone.Radius + step, SettlementZone.MinRadius, SettlementZone.MaxRadius);
            RefreshNow();
        }

        private void Save()
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                return;
            }
            if (!_table.RoleOf(player).Allows(SettlementRight.Manage))
            {
                player.Message(MessageHud.MessageType.Center, Permissions.Denied(SettlementRight.Manage));
                return;
            }
            _zone.Name = TextUtil.SanitizeName(TextEntry.text, SettlementZone.MaxNameLength);
            if (_zone.Name.Length == 0)
            {
                _zone.Name = Localize(_zone.Kind == SettlementZone.KindWarehouse ? "$aoj_zone_kind_warehouse" : "$aoj_zone_kind_other");
            }
            _table.RequestSetZone(_zone);
            player.Message(MessageHud.MessageType.Center, Localize("$aoj_msg_zone_saved", _zone.Name));
            Hide();
        }

        private void Delete()
        {
            Player player = Player.m_localPlayer;
            if (player == null || !_existing)
            {
                return;
            }
            if (!_table.RoleOf(player).Allows(SettlementRight.Manage))
            {
                player.Message(MessageHud.MessageType.Center, Permissions.Denied(SettlementRight.Manage));
                return;
            }
            _table.RequestRemoveZone(_zone.Id);
            player.Message(MessageHud.MessageType.Center, Localize("$aoj_msg_zone_removed", _zone.Name));
            Hide();
        }

        /// <summary>
        /// The zone key on the big map: the area under the cursor to edit, else a new one there. Only while the map
        /// takes input (no pin name being typed) and no window of the mod is open.
        /// </summary>
        [HarmonyPatch(typeof(Minimap), nameof(Minimap.UpdateMap))]
        private static class MapKeyPatch
        {
            private static void Postfix(Minimap __instance, Player player, bool takeInput)
            {
                if (!takeInput || player == null || __instance.m_mode != Minimap.MapMode.Large || AoJConfig.ZoneKey == null ||
                    !ZInput.GetKeyDown(AoJConfig.ZoneKey.Value) || Minimap.InTextInput() ||
                    (s_instance != null && s_instance.gameObject.activeSelf))
                {
                    return;
                }
                Vector3 point = __instance.ScreenToWorldPoint(ZInput.pointerPosition);
                JarlTable table = null;
                SettlementZone zone = null;
                foreach (JarlTable candidate in JarlTable.Loaded)
                {
                    SettlementData data = candidate != null ? candidate.Data : null;
                    if (data == null)
                    {
                        continue;
                    }
                    SettlementZone hit = data.ZoneAt(point);
                    if (hit != null)
                    {
                        table = candidate;
                        zone = hit.Clone();
                        break;
                    }
                }
                if (table == null)
                {
                    float nearest = JarlTable.MaxZoneDistance;
                    foreach (JarlTable candidate in JarlTable.Loaded)
                    {
                        if (candidate == null || candidate.Data == null)
                        {
                            continue;
                        }
                        float distance = Utils.DistanceXZ(candidate.transform.position, point);
                        if (distance <= nearest)
                        {
                            nearest = distance;
                            table = candidate;
                        }
                    }
                    if (table == null)
                    {
                        player.Message(MessageHud.MessageType.Center, Localize("$aoj_msg_zone_no_settlement", JarlTable.MaxZoneDistance.ToString("0")));
                        return;
                    }
                    if (!table.RoleOf(player).Allows(SettlementRight.Manage))
                    {
                        player.Message(MessageHud.MessageType.Center, Permissions.Denied(SettlementRight.Manage));
                        return;
                    }
                    zone = new SettlementZone { Center = point, Radius = 15f, Kind = SettlementZone.KindWarehouse };
                }
                Open(table, zone, zone.Id != 0L);
            }
        }
    }
}
