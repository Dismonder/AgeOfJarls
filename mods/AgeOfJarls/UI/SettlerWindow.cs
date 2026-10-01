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

        // Inventory tab: one row per item, with buttons to take it (all of it, or one of a stack)
        private const int ItemRows = 10;
        private const float ItemRowTop = 178f;
        private const float ItemRowStep = 34f;
        private readonly List<ItemRow> _itemRows = new List<ItemRow>();
        private readonly List<ItemDrop.ItemData> _carried = new List<ItemDrop.ItemData>();
        private Text _itemsEmpty;
        private Text _itemPageLabel;
        private GameObject _itemPager;
        private int _itemPage;
        private int _itemsRevision = -1;

        private sealed class ItemRow
        {
            internal GameObject Root;
            internal Text Name;
            internal GameObject TakeOne;
            internal ItemDrop.ItemData Item;
        }

        // Info tab
        private Text _info;

        /// <summary>Opens the window; opened from afar (the command wheel), it stays open up to <paramref name="reach"/> metres away.</summary>
        /// <summary>This settler's window is open here: it stops and faces the player talking to it.</summary>
        internal static bool IsOpenFor(Settler settler) =>
            s_instance != null && s_instance.gameObject.activeSelf && s_instance._settler == settler;

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
            s_instance._itemPage = 0;
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

        // None -> warrior -> archer -> shieldbearer -> spearman -> berserker -> none, skipping roles the settlement has
        // not unlocked yet.
        private static void NextRole(Settler settler)
        {
            if (!Allowed(settler, SettlementRight.Military))
            {
                return;
            }
            JarlTable table = settler.HomeTable;
            int tier = table != null && table.Data != null ? table.Data.Tier : 0;
            Army.CombatRole role = settler.Role;
            int count = Army.CombatRoles.All.Length + 1;
            for (int step = 0; step < count; step++)
            {
                role = (Army.CombatRole)(((int)role + 1) % count);
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
            _itemsEmpty = Label(page, new Vector2(0f, 120f), 18, GUIManager.Instance.ValheimBeige, Width - 60f, 40f, TextAnchor.MiddleCenter);
            for (int i = 0; i < ItemRows; i++)
            {
                int index = i;
                float y = ItemRowTop - i * ItemRowStep;
                var row = new ItemRow { Root = Page(page, "Item" + i).gameObject };
                row.Name = Label(row.Root.transform, new Vector2(-120f, y), 17, GUIManager.Instance.ValheimBeige, 340f, RowButtonHeight, TextAnchor.MiddleLeft);
                Button(row.Root.transform, "$aoj_take", new Vector2(150f, y), 110f, () => TakeRow(index, all: true), RowButtonHeight);
                row.TakeOne = Button(row.Root.transform, "$aoj_take_one", new Vector2(255f, y), 90f, () => TakeRow(index, all: false), RowButtonHeight)
                    .GetComponentInParent<Button>().gameObject;
                _itemRows.Add(row);
            }
            _itemPager = Page(page, "ItemPager").gameObject;
            Button(_itemPager.transform, "<", new Vector2(-90f, -170f), 60f, () => TurnItemPage(-1), RowButtonHeight);
            _itemPageLabel = Label(_itemPager.transform, new Vector2(0f, -170f), 16, GUIManager.Instance.ValheimBeige, 120f, RowButtonHeight, TextAnchor.MiddleCenter);
            Button(_itemPager.transform, ">", new Vector2(90f, -170f), 60f, () => TurnItemPage(1), RowButtonHeight);
            Button(page, "$aoj_takeback", new Vector2(0f, -212f), ButtonWidth, () => Act(s => s.RequestTakeBack(Player.m_localPlayer)));
        }

        private int ItemPages => Mathf.Max(1, (_carried.Count + ItemRows - 1) / ItemRows);

        private void TurnItemPage(int step)
        {
            _itemPage = (_itemPage + step + ItemPages) % ItemPages;
            FillItemRows();
        }

        private void TakeRow(int index, bool all)
        {
            ItemDrop.ItemData item = index < _itemRows.Count ? _itemRows[index].Item : null;
            if (item != null)
            {
                Act(s => s.RequestTakeItem(Player.m_localPlayer, item, all ? item.m_stack : 1));
            }
        }

        private void FillItemRows()
        {
            _itemPage = Mathf.Clamp(_itemPage, 0, ItemPages - 1);
            for (int i = 0; i < _itemRows.Count; i++)
            {
                ItemRow row = _itemRows[i];
                int at = _itemPage * ItemRows + i;
                row.Item = at < _carried.Count ? _carried[at] : null;
                row.Root.SetActive(row.Item != null);
                if (row.Item == null)
                {
                    continue;
                }
                string line = row.Item.m_stack > 1 ? $"{row.Item.m_shared.m_name} ×{row.Item.m_stack}" : row.Item.m_shared.m_name;
                row.Name.text = Localize(row.Item.m_equipped ? line + " <color=#b0b0b0>($aoj_equipped)</color>" : line);
                row.TakeOne.SetActive(row.Item.m_stack > 1);
            }
            _itemsEmpty.text = _carried.Count == 0 ? Localize("$aoj_inventory_empty") : "";
            _itemPager.SetActive(ItemPages > 1);
            _itemPageLabel.text = $"{_itemPage + 1} / {ItemPages}";
        }

        private void BuildInfo(Transform page)
        {
            _info = Label(page, new Vector2(0f, -10f), 16, GUIManager.Instance.ValheimBeige, Width - 60f, 400f, TextAnchor.UpperLeft);
        }

        protected override bool IsTargetValid()
        {
            Player local = Player.m_localPlayer;
            return _settler != null && _settler.Zdo != null && local != null && !local.IsDead() &&
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
            if (revision == _itemsRevision)
            {
                return;
            }
            _itemsRevision = revision;
            _carried.Clear();
            _carried.AddRange(_settler.CarriedItemData());
            FillItemRows();
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
