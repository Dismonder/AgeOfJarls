using System;
using System.Collections.Generic;
using System.Linq;
using AgeOfJarls.Core;
using AgeOfJarls.Settlement;
using AgeOfJarls.Settlers;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace AgeOfJarls.UI
{
    /// <summary>
    /// Window for one settler ([E] on a settler), in three tabs: orders, what it carries, and who it is. Everything
    /// shown comes from the settler's ZDO and every order is an RPC, so it looks and works the same for both players
    /// whichever of them simulates the settler.
    /// </summary>
    internal sealed class SettlerWindow : AojWindow
    {
        private const float Width = 640f;
        private const float Height = 600f;
        private const float ButtonWidth = 250f;
        private const float ColumnOffset = 135f;
        private const float MaxDistance = 15f;

        private static SettlerWindow s_instance;

        private Settler _settler;
        private float _reach = MaxDistance;
        private Text _title;

        // Orders tab
        private Text _status;
        private Text _followLabel;

        // Inventory tab
        private Text _items;
        private int _itemsRevision = -1;

        // Info tab
        private Text _info;

        /// <summary>Opens the window; opened from afar (the command wheel), it stays open up to <paramref name="reach"/> metres away.</summary>
        internal static void Open(Settler settler, float reach = MaxDistance)
        {
            if (GUIManager.CustomGUIFront == null || settler == null)
            {
                return;
            }
            if (s_instance == null)
            {
                s_instance = CreateWindow<SettlerWindow>("AoJ_SettlerWindow", Width, Height);
            }
            s_instance._settler = settler;
            s_instance._reach = Mathf.Max(MaxDistance, reach);
            s_instance._itemsRevision = -1;
            s_instance.TextEntry.text = "";
            s_instance.ShowWindow();
        }

        protected override void Build(Transform root)
        {
            _title = Label(root, new Vector2(0f, 265f), 26, GUIManager.Instance.ValheimOrange, Width - 40f, 40f, TextAnchor.MiddleCenter);
            Transform orders = Page(root, "Orders");
            Transform inventory = Page(root, "Inventory");
            Transform info = Page(root, "Info");
            BuildOrders(orders);
            BuildInventory(inventory);
            BuildInfo(info);
            Button(root, "$aoj_close", new Vector2(0f, -262f), 200f, Hide);
            Tabs(root, 220f, 190f, ("$aoj_tab_orders", orders), ("$aoj_tab_inventory", inventory), ("$aoj_tab_info", info));
        }

        private void BuildOrders(Transform page)
        {
            _status = Label(page, new Vector2(0f, 122f), 15, GUIManager.Instance.ValheimBeige, Width - 60f, 150f, TextAnchor.UpperLeft);
            _followLabel = Button(page, "", new Vector2(-ColumnOffset, 15f), ButtonWidth, () => Act(s => s.ToggleFollow(Player.m_localPlayer)));
            Button(page, "$aoj_go_home", new Vector2(ColumnOffset, 15f), ButtonWidth, () => Act(s => s.RequestGoHome(Player.m_localPlayer)));
            Button(page, "$aoj_cmd_wait", new Vector2(-ColumnOffset, -35f), ButtonWidth, () => Act(s =>
            {
                s.SendWait();
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, Localize("$aoj_msg_wait", s.DisplayName));
            }));
            Button(page, "$aoj_takeback", new Vector2(ColumnOffset, -35f), ButtonWidth, () => Act(s => s.RequestTakeBack(Player.m_localPlayer)));
            _jobLabel = Button(page, "", new Vector2(-ColumnOffset, -85f), ButtonWidth, () => Act(NextJob));
            _roleLabel = Button(page, "", new Vector2(ColumnOffset, -85f), ButtonWidth, () => Act(NextRole));
            TextEntry = TextInput(page, "$aoj_new_name", new Vector2(-ColumnOffset, -140f), ButtonWidth);
            Button(page, "$aoj_rename", new Vector2(ColumnOffset, -140f), ButtonWidth, Rename);
            Label(page, new Vector2(0f, -195f), 14, GUIManager.Instance.ValheimBeige, Width - 60f, 36f, TextAnchor.MiddleCenter).text =
                Localize("$aoj_wheel_hint", CommandWheel.KeyName);
        }

        private Text _jobLabel;
        private Text _roleLabel;

        // The next totem of its settlement with a free place (after the current one), then "no job", and around again.
        private static void NextJob(Settler settler)
        {
            JarlTable table = settler.HomeTable;
            if (table == null)
            {
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, Localize("$aoj_msg_no_home", settler.DisplayName));
                return;
            }
            if (!Allowed(settler, SettlementRight.Manage))
            {
                return;
            }
            List<Work.WorkTotem> totems = Work.WorkTotem.Loaded
                .Where(t => t != null && t.Settlement == table && t.Id != 0L)
                .OrderBy(t => Vector3.Distance(t.transform.position, table.transform.position))
                .ToList();
            int current = totems.FindIndex(t => t.Id == settler.JobId);
            for (int step = 1; step <= totems.Count; step++)
            {
                Work.WorkTotem totem = totems[(current + step) % totems.Count];
                if (current + step == totems.Count)
                {
                    break;
                }
                if (totem.Workers().Count < totem.Capacity)
                {
                    settler.RequestSetJob(totem.Id);
                    Player.m_localPlayer.Message(MessageHud.MessageType.Center,
                        Localize("$aoj_msg_assigned", settler.DisplayName, Localize(Work.JobInfo.Token(totem.Job))));
                    return;
                }
            }
            if (settler.JobId != 0L)
            {
                settler.RequestSetJob(0L);
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, Localize("$aoj_msg_released", settler.DisplayName));
            }
            else if (totems.Count == 0)
            {
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, Localize("$aoj_msg_no_totems"));
            }
        }

        // None -> warrior -> archer -> shieldbearer -> none, skipping roles the settlement has not unlocked yet.
        private static void NextRole(Settler settler)
        {
            if (!Allowed(settler, SettlementRight.Military))
            {
                return;
            }
            JarlTable table = settler.HomeTable;
            int tier = table != null && table.Data != null ? table.Data.Tier : 0;
            Army.CombatRole role = settler.Role;
            for (int step = 0; step < 4; step++)
            {
                role = (Army.CombatRole)(((int)role + 1) % 4);
                if (role == Army.CombatRole.None || Army.CombatRoles.IsUnlocked(role, tier))
                {
                    break;
                }
            }
            settler.RequestSetRole(role);
            Player.m_localPlayer.Message(MessageHud.MessageType.Center,
                Localize("$aoj_msg_role", settler.DisplayName, Localize(role == Army.CombatRole.None ? "$aoj_role_none" : Army.CombatRoles.Token(role))));
        }

        private void BuildInventory(Transform page)
        {
            _items = Label(page, new Vector2(0f, 20f), 16, GUIManager.Instance.ValheimBeige, Width - 60f, 330f, TextAnchor.UpperLeft);
            Button(page, "$aoj_takeback", new Vector2(0f, -190f), ButtonWidth, () => Act(s => s.RequestTakeBack(Player.m_localPlayer)));
        }

        private void BuildInfo(Transform page)
        {
            _info = Label(page, new Vector2(0f, -10f), 16, GUIManager.Instance.ValheimBeige, Width - 60f, 400f, TextAnchor.UpperLeft);
        }

        protected override bool IsTargetValid()
        {
            Player local = Player.m_localPlayer;
            return _settler != null && local != null && !local.IsDead() &&
                   Vector3.Distance(local.transform.position, _settler.transform.position) <= _reach;
        }

        protected override void Refresh()
        {
            _title.text = _settler.DisplayName;
            switch (ActiveTab)
            {
                case 0:
                    _status.text = Localize(_settler.BuildDetails());
                    _followLabel.text = Localize(_settler.IsFollowing(Player.m_localPlayer) ? "$aoj_stay" : "$aoj_follow");
                    Work.WorkTotem totem = _settler.JobTotem;
                    _jobLabel.text = Localize("$aoj_job: " + (_settler.JobId == 0L ? "$aoj_job_none" : totem != null ? Work.JobInfo.Token(totem.Job) : "$aoj_job_far"));
                    _roleLabel.text = Localize("$aoj_role: " + (_settler.Role == Army.CombatRole.None ? "$aoj_role_none" : Army.CombatRoles.Token(_settler.Role)));
                    break;
                case 1:
                    RefreshItems();
                    break;
                default:
                    _info.text = Localize("<color=#e0c080>$aoj_traits</color>\n" + _settler.TraitDetails() +
                                          "\n\n<color=#e0c080>$aoj_skills</color>: " + _settler.SkillsText() +
                                          "\n\n" + _settler.BuildDetails());
                    break;
            }
        }

        // The saved inventory is parsed again only after it changed.
        private void RefreshItems()
        {
            ZNetView view = _settler.GetComponent<ZNetView>();
            int revision = view != null && view.IsValid() ? view.GetZDO().GetInt(Keys.ZdoSettlerInventoryRevision) : -1;
            if (revision == _itemsRevision && _items.text.Length > 0)
            {
                return;
            }
            _itemsRevision = revision;
            List<string> items = _settler.CarriedItems();
            _items.text = Localize(items.Count == 0 ? "$aoj_inventory_empty" : "<color=#e0c080>$aoj_items</color>\n" + string.Join("\n", items));
        }

        protected override void OnHidden()
        {
            _settler = null;
        }

        private void Rename()
        {
            string name = TextEntry.text;
            if (_settler != null && !string.IsNullOrWhiteSpace(name) && Allowed(_settler, SettlementRight.Live))
            {
                _settler.RequestRename(name);
                TextEntry.text = "";
            }
        }

        // The owner checks again; this only spares a silent refusal.
        private static bool Allowed(Settler settler, SettlementRight right)
        {
            if (settler.MayBeOrderedBy(Player.m_localPlayer, right))
            {
                return true;
            }
            Player.m_localPlayer?.Message(MessageHud.MessageType.Center, Permissions.Denied(right));
            return false;
        }

        private void Act(Action<Settler> order)
        {
            if (_settler != null && Player.m_localPlayer != null)
            {
                order(_settler);
                _itemsRevision = -1;
                RefreshNow();
            }
        }
    }
}
