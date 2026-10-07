using System;
using System.Collections.Generic;
using System.Linq;
using AgeOfJarls.Core;
using AgeOfJarls.Family;
using AgeOfJarls.Settlement;
using AgeOfJarls.Settlers;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace AgeOfJarls.UI
{
    /// <summary>
    /// Window for one settler ([E] on a settler), in four tabs: orders, inventory, family, and information. Everything
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
        private TextArea _status;
        private Text _followLabel;

        // Inventory tab: one row per item, with buttons to take it (all of it, or one of a stack)
        private ScrollList _items;
        private readonly List<ItemRow> _itemRows = new List<ItemRow>();
        private readonly List<ItemDrop.ItemData> _carried = new List<ItemDrop.ItemData>();
        private Text _itemsEmpty;
        private int _itemsRevision = -1;

        private sealed class ItemRow
        {
            internal Image Icon;
            internal Text Name;
            internal Text Count;
            internal GameObject TakeOne;
            internal ItemDrop.ItemData Item;
        }

        private TextArea _info;
        private TextArea _familyDetails;
        private ScrollList _children;
        private readonly List<ChildRow> _childRows = new List<ChildRow>();

        private sealed class ChildRow
        {
            internal Text Name;
            internal Text Stage;
            internal Text Age;
            internal Text Show;
            internal long Uid;
        }

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
            s_instance.TextEntry.text = "";
            s_instance.ShowWindow();
        }

        protected override void Build(Transform root)
        {
            _title = Label(root, new Vector2(0f, 265f), 26, GUIManager.Instance.ValheimOrange, Width - 40f, 40f, TextAnchor.MiddleCenter);
            Transform orders = Page(root, "Orders");
            Transform inventory = Page(root, "Inventory");
            Transform family = Page(root, "Family");
            Transform info = Page(root, "Info");
            BuildOrders(orders);
            BuildInventory(inventory);
            BuildInfo(info);
            BuildFamily(family);
            Button(root, "$aoj_close", new Vector2(0f, -262f), 200f, Hide);
            Tabs(root, 220f, 0f, ("$aoj_tab_orders", orders), ("$aoj_tab_inventory", inventory), ("$aoj_tab_family", family), ("$aoj_tab_info", info));
        }

        private void BuildOrders(Transform page)
        {
            _status = UiKit.TextArea(page, new Vector2(0f, 122f), 600f, 150f);
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
            TextEntry = TextInput(page, "$aoj_new_name", new Vector2(-ColumnOffset, -140f), ButtonWidth, Rename);
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
            _itemsEmpty = Label(page, new Vector2(0f, 182f), 16, GUIManager.Instance.ValheimBeige, Width - 60f, 28f, TextAnchor.MiddleCenter);
            _items = UiKit.List(page, new Vector2(0f, -10f), 600f, 352f, 34f);
            Button(page, "$aoj_takeback", new Vector2(0f, -212f), ButtonWidth, () => Act(s => s.RequestTakeBack(Player.m_localPlayer)));
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
            for (int i = 0; i < _carried.Count; i++)
            {
                if (i == _itemRows.Count)
                {
                    int index = i;
                    RectTransform root = _items.Row(i);
                    var cells = new ItemRow();
                    cells.Icon = _items.Icon(root, 28f);
                    cells.Name = _items.Text(root, 246f);
                    cells.Count = _items.Text(root, 64f, 14);
                    _items.Button(root, "$aoj_take", 110f, () => TakeRow(index, true));
                    cells.TakeOne = _items.Button(root, "$aoj_take_one", 90f, () => TakeRow(index, false)).GetComponentInParent<Button>().gameObject;
                    _itemRows.Add(cells);
                }
                ItemRow row = _itemRows[i];
                row.Item = _carried[i];
                row.Icon.sprite = ItemIcon(row.Item);
                SetText(row.Name, Localize(row.Item.m_shared.m_name + (row.Item.m_equipped ? " " + Palette.Colored("($aoj_equipped)", Palette.Muted) : "")));
                SetText(row.Count, $"Q{row.Item.m_quality} ×{row.Item.m_stack}");
                row.TakeOne.SetActive(row.Item.m_stack > 1);
            }
            _items.SetCount(_carried.Count);
            SetText(_itemsEmpty, _carried.Count == 0 ? Localize("$aoj_inventory_empty") : "");
        }

        private void BuildInfo(Transform page)
        {
            _info = UiKit.TextArea(page, new Vector2(0f, -18f), 600f, 424f);
        }

        /// <summary>Saved variants can outlive a prefab's icon array; render such items without an icon.</summary>
        internal static Sprite ItemIcon(ItemDrop.ItemData item)
        {
            Sprite[] icons = item?.m_shared?.m_icons;
            int variant = item?.m_variant ?? -1;
            return icons != null && variant >= 0 && variant < icons.Length ? icons[variant] : null;
        }

        private void BuildFamily(Transform page)
        {
            _familyDetails = UiKit.TextArea(page, new Vector2(0f, 110f), 600f, 160f);
            Label(page, new Vector2(0f, 10f), 16, GUIManager.Instance.ValheimOrange, 580f, 28f, TextAnchor.MiddleLeft).text = Localize("$aoj_family_children");
            _children = UiKit.List(page, new Vector2(0f, -118f), 600f, 220f, 36f);
        }

        private void RefreshFamily()
        {
            FamilyInfo family = _settler.Family;
            SettlementData data = _settler.HomeTable?.Data;
            Couple couple = data?.FindCouple(_settler.Uid);
            string details = _settler.FamilyDetails();
            // Reuse the backend's minor stage/next-stage line; compose relationships with live full names.
            details = !_settler.IsAdult && details.Length > 0 ? details.Split('\n')[0] :
                FamilyRules.StageToken(_settler.Stage, _settler.Identity?.Female ?? false) +
                (_settler.Born > 0.0 ? ", " + Localize("$aoj_family_age", Days(_settler.Age)) : "");
            if (family.PartnerUid != 0L)
            {
                Settler partner = Settler.FindByUid(family.PartnerUid);
                details += "\n$aoj_family_partner: " + (partner != null ? partner.FullName : family.PartnerName);
                if (couple != null) details += " · " + Localize("$aoj_family_since", ((int)(couple.Since / WorldClock.DayLength) + 1).ToString());
            }
            else if (family.CourtingUid != 0L)
            {
                Settler courting = Settler.FindByUid(family.CourtingUid);
                details += "\n$aoj_family_courting: " + (courting != null ? courting.FullName : family.CourtingName);
            }
            else details += "\n$aoj_family_single";
            if (family.IsExpecting || family.PartnerExpecting)
                details += "\n" + Localize(family.PartnerExpecting ? "$aoj_family_partner_expecting" : "$aoj_family_expecting", Days(family.DueAt - WorldClock.Now));
            // The blob clears dead parents; immutable birth ZDO keys must not bring them back.
            ChildRecord own = family.Own;
            details += "\n$aoj_family_parents: " + ParentName(data, own != null ? own.Mother : _settler.MotherUid) + " / " + ParentName(data, own != null ? own.Father : _settler.FatherUid);
            _familyDetails.SetText(Localize(details));
            for (int i = 0; i < family.Children.Count; i++)
            {
                if (i == _childRows.Count)
                {
                    int index = i;
                    RectTransform root = _children.Row(i);
                    var cells = new ChildRow { Name = _children.Text(root, 225f), Stage = _children.Text(root, 110f), Age = _children.Text(root, 120f) };
                    cells.Show = _children.Button(root, "$aoj_btn_show", 85f, () => ShowChild(index));
                    _childRows.Add(cells);
                }
                ChildRecord child = family.Children[i];
                ChildRow row = _childRows[i];
                row.Uid = child.Uid;
                Settler loaded = Settler.FindByUid(child.Uid);
                SetText(row.Name, loaded != null ? loaded.FullName : child.Name);
                SetText(row.Stage, Localize(FamilyRules.StageToken(FamilyRules.StageAt(child.Born, WorldClock.Now, WorldClock.DayLength, FamilyConfig.Live), child.Female)));
                SetText(row.Age, Localize("$aoj_family_age", Days(WorldClock.Now - child.Born)));
                bool reachable = ChildWithinReach(loaded);
                row.Show.GetComponentInParent<Button>().interactable = reachable;
                row.Show.color = reachable ? GUIManager.Instance.ValheimBeige : Color.gray;
            }
            _children.SetCount(family.Children.Count);
        }

        private static string Days(double seconds) => (Math.Max(0.0, seconds) / WorldClock.DayLength).ToString("0.0");
        private static string ParentName(SettlementData data, long uid)
        {
            string name = data != null ? FamilyInfo.NameOf(data, uid) : "";
            return name.Length > 0 ? name : "—";
        }

        private bool ChildWithinReach(Settler child) => child != null && child.Zdo != null && Player.m_localPlayer != null &&
            Vector3.Distance(child.transform.position, Player.m_localPlayer.transform.position) <= _reach;

        private void ShowChild(int index)
        {
            Settler child = Settler.FindByUid(_childRows[index].Uid);
            if (ChildWithinReach(child)) Open(child, _reach);
        }

        protected override bool IsTargetValid()
        {
            Player local = Player.m_localPlayer;
            return _settler != null && _settler.Zdo != null && local != null && !local.IsDead() &&
                   Vector3.Distance(local.transform.position, _settler.transform.position) <= _reach;
        }

        protected override void Refresh()
        {
            SetText(_title, _settler.FullName);
            switch (ActiveTab)
            {
                case 0:
                    _status.SetText(Localize(FamilyRules.StageToken(_settler.Stage, _settler.Identity?.Female ?? false) +
                        (_settler.Born > 0.0 ? " · " + Localize("$aoj_family_age", Days(_settler.Age)) : "") + "\n" + _settler.BuildDetails()));
                    _jobLabel.GetComponentInParent<Button>().gameObject.SetActive(_settler.IsAdult);
                    _roleLabel.GetComponentInParent<Button>().gameObject.SetActive(_settler.IsAdult);
                    SetText(_followLabel, Localize(_settler.IsFollowing(Player.m_localPlayer) ? "$aoj_stay" : "$aoj_follow"));
                    Work.WorkTotem totem = _settler.JobTotem;
                    SetText(_jobLabel, Localize("$aoj_job: " + (_settler.JobId == 0L ? "$aoj_job_none" : totem != null ? Work.JobInfo.Token(totem.Job) : "$aoj_job_far")));
                    SetText(_roleLabel, Localize("$aoj_role: " + (_settler.Role == Army.CombatRole.None ? "$aoj_role_none" : Army.CombatRoles.Token(_settler.Role))));
                    break;
                case 1:
                    RefreshItems();
                    break;
                case 2:
                    RefreshFamily();
                    break;
                default:
                    _info.SetText(Localize(Palette.Colored("$aoj_traits", Palette.Accent) + "\n" + _settler.TraitDetails() +
                                          "\n\n" + Palette.Colored("$aoj_skills", Palette.Accent) + ": " + _settler.SkillsText() +
                                          "\n\n" + _settler.BuildDetails()));
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
            _carried.Clear();
            _carried.AddRange(_settler.CarriedItemData());
            FillItemRows();
            _itemsRevision = revision;
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
