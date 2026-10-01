using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AgeOfJarls.Core;
using AgeOfJarls.Settlement;
using AgeOfJarls.Settlers;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace AgeOfJarls.UI
{
    /// <summary>
    /// The settlement window ([E] on the Jarl's Table), in tabs: the settlement (summary, tier, name, feast), its
    /// members (ranks, admitting players at the table), its settlers (one row each, with orders), work, storage,
    /// defence and the chronicle. Everyone may look; each control shows for the ranks that may use it
    /// (<see cref="Permissions"/>). Every change is an RPC that the table's or the settler's owner checks again.
    /// </summary>
    internal sealed class JarlTableWindow : AojWindow
    {
        private const float Width = 840f;
        private const float Height = 680f;
        private const float MaxDistance = 12f;
        private const float CandidateRange = 20f;
        private const int RowsPerPage = 8;
        private const float RowHeight = 46f;
        private const float FirstRowY = 160f;
        private const float ConfirmSeconds = 3f;
        private const int MaxChestLines = 12;
        private const int MaxTotals = 24;

        private static JarlTableWindow s_instance;

        private readonly List<Container> _chests = new List<Container>();
        private readonly Row[] _rows = new Row[RowsPerPage];
        private JarlTable _table;
        private Text _title;

        // Settlement tab
        private Text _summary;
        private Text _hint;
        private GameObject _liveControls;
        private GameObject _manageControls;

        // Members tab
        private const int MemberRows = 6;
        private const float MemberRowHeight = 40f;
        private const float FirstMemberY = 135f;
        private const int MaxCandidates = 2;
        private readonly MemberRow[] _memberRows = new MemberRow[MemberRows];
        private readonly CandidateRow[] _candidateRows = new CandidateRow[MaxCandidates];
        private Text _membersHeader;
        private Text _membersBonus;
        private Text _candidatesLabel;
        private GameObject _memberPager;
        private Text _memberPageLabel;
        private int _memberPage;
        private long _confirmMember;
        private string _confirmAction = "";
        private float _confirmMemberUntil;

        private sealed class MemberRow
        {
            internal GameObject Root;
            internal Text Name;
            internal Text Rank;
            internal GameObject Up;
            internal GameObject Down;
            internal GameObject Jarl;
            internal Text JarlLabel;
            internal GameObject Remove;
            internal Text RemoveLabel;
            internal long PlayerId;
        }

        private sealed class CandidateRow
        {
            internal GameObject Root;
            internal Text Name;
            internal GameObject Admit;
            internal GameObject CoJarl;
            internal Player Player;
        }

        // Settlers tab
        private Text _rosterHeader;
        private Text _pageLabel;
        private int _page;
        private long _confirmUid;
        private float _confirmUntil;

        // Storage tab
        private Text _totals;
        private Text _chestList;

        private sealed class Row
        {
            internal GameObject Root;
            internal Text Name;
            internal Text Status;
            internal GameObject Buttons;
            internal GameObject Dismiss;
            internal Text DismissLabel;
            internal long Uid;
        }

        internal static void Open(JarlTable table)
        {
            if (GUIManager.CustomGUIFront == null || table == null)
            {
                return;
            }
            if (s_instance == null)
            {
                s_instance = CreateWindow<JarlTableWindow>("AoJ_JarlTableWindow", Width, Height);
            }
            s_instance._table = table;
            s_instance._page = 0;
            s_instance._memberPage = 0;
            s_instance.TextEntry.text = "";
            s_instance.ShowWindow();
        }

        protected override void Build(Transform root)
        {
            _title = Label(root, new Vector2(0f, 300f), 26, GUIManager.Instance.ValheimOrange, Width - 40f, 40f, TextAnchor.MiddleCenter);
            Transform overview = Page(root, "Overview");
            Transform members = Page(root, "Members");
            Transform settlers = Page(root, "Settlers");
            Transform work = Page(root, "Work");
            Transform storage = Page(root, "Storage");
            Transform defense = Page(root, "Defense");
            Transform chronicle = Page(root, "Chronicle");
            BuildOverview(overview);
            BuildMembers(members);
            BuildSettlers(settlers);
            BuildStorage(storage);
            _work = Label(work, new Vector2(0f, 0f), 15, GUIManager.Instance.ValheimBeige, Width - 60f, 430f, TextAnchor.UpperLeft);
            _workControls = Page(work, "WorkControls").gameObject;
            _manualWorkLabel = Button(_workControls.transform, "", new Vector2(0f, -245f), 420f, ToggleManualWork, RowButtonHeight);
            BuildDefense(defense);
            _chronicle = Label(chronicle, new Vector2(0f, -20f), 15, GUIManager.Instance.ValheimBeige, Width - 60f, 470f, TextAnchor.UpperLeft);
            Button(root, "$aoj_close", new Vector2(0f, -300f), 200f, Hide);
            Tabs(root, 252f, 106f, ("$aoj_tab_overview", overview), ("$aoj_tab_members", members), ("$aoj_tab_settlers", settlers),
                ("$aoj_tab_work", work), ("$aoj_tab_storage", storage), ("$aoj_tab_defense", defense), ("$aoj_tab_chronicle", chronicle));
        }

        private Text _work;
        private GameObject _workControls;
        private Text _manualWorkLabel;
        private Text _defense;
        private Text _alarmLabel;
        private GameObject _defenseControls;
        private Text _chronicle;

        private void BuildDefense(Transform page)
        {
            _defense = Label(page, new Vector2(0f, -10f), 15, GUIManager.Instance.ValheimBeige, Width - 60f, 420f, TextAnchor.UpperLeft);
            _defenseControls = Page(page, "DefenseControls").gameObject;
            _alarmLabel = Button(_defenseControls.transform, "", new Vector2(0f, -240f), 300f, ToggleAlarm);
        }

        private void ToggleAlarm()
        {
            if (_table != null)
            {
                _table.RequestAlarm(!_table.AlarmOn);
            }
        }

        private void BuildOverview(Transform page)
        {
            Color beige = GUIManager.Instance.ValheimBeige;
            _summary = Label(page, new Vector2(-170f, 20f), 17, beige, 440f, 360f, TextAnchor.UpperLeft);
            _hint = Label(page, new Vector2(0f, -240f), 15, beige, Width - 60f, 30f, TextAnchor.MiddleCenter);

            // Members (Karl and up) bring their followers in and hold feasts; Hersirs and Jarls also expand and rename.
            _liveControls = Page(page, "Live").gameObject;
            _manageControls = Page(page, "Manage").gameObject;
            Transform live = _liveControls.transform;
            Transform manage = _manageControls.transform;
            const float x = 235f;
            const float width = 300f;
            Button(live, "$aoj_accept_followers_short", new Vector2(x, 170f), width, () => Act(t => t.AcceptFollowers(Player.m_localPlayer)));
            Button(manage, "$aoj_upgrade", new Vector2(x, 120f), width, () => Act(t => t.TryUpgrade(Player.m_localPlayer)));
            TextEntry = TextInput(manage, "$aoj_settlement_name", new Vector2(x, 50f), width);
            Button(manage, "$aoj_rename_settlement", new Vector2(x, 2f), width, Rename);
            Button(live, "$aoj_feast", new Vector2(x, -48f), width, () => Act(t => t.TryFeast(Player.m_localPlayer)));
        }

        private void BuildMembers(Transform page)
        {
            Color beige = GUIManager.Instance.ValheimBeige;
            Color orange = GUIManager.Instance.ValheimOrange;
            _membersHeader = Label(page, new Vector2(0f, 205f), 16, orange, Width - 60f, 30f, TextAnchor.MiddleLeft);
            _membersBonus = Label(page, new Vector2(0f, 176f), 14, beige, Width - 60f, 28f, TextAnchor.MiddleLeft);
            for (int i = 0; i < MemberRows; i++)
            {
                int index = i;
                float y = FirstMemberY - i * MemberRowHeight;
                var row = new MemberRow { Root = Page(page, "Member" + i).gameObject };
                Transform parent = row.Root.transform;
                row.Name = Label(parent, new Vector2(-290f, y), 17, orange, 220f, MemberRowHeight, TextAnchor.MiddleLeft);
                row.Rank = Label(parent, new Vector2(-115f, y), 15, beige, 120f, MemberRowHeight, TextAnchor.MiddleLeft);
                row.Up = ButtonObject(Button(parent, "$aoj_btn_promote", new Vector2(20f, y), 84f, () => ChangeRank(index, 1), RowButtonHeight));
                row.Down = ButtonObject(Button(parent, "$aoj_btn_demote", new Vector2(110f, y), 84f, () => ChangeRank(index, -1), RowButtonHeight));
                row.JarlLabel = Button(parent, "$aoj_btn_cojarl", new Vector2(200f, y), 84f, () => MemberJarl(index), RowButtonHeight);
                row.Jarl = ButtonObject(row.JarlLabel);
                row.RemoveLabel = Button(parent, "$aoj_btn_remove", new Vector2(290f, y), 84f, () => MemberRemove(index), RowButtonHeight);
                row.Remove = ButtonObject(row.RemoveLabel);
                _memberRows[i] = row;
            }

            _candidatesLabel = Label(page, new Vector2(-190f, -100f), 15, orange, 400f, 28f, TextAnchor.MiddleLeft);
            _memberPager = Page(page, "Pager").gameObject;
            Button(_memberPager.transform, "<", new Vector2(250f, -100f), 50f, () => TurnMemberPage(-1), RowButtonHeight);
            _memberPageLabel = Label(_memberPager.transform, new Vector2(315f, -100f), 16, beige, 70f, 28f, TextAnchor.MiddleCenter);
            Button(_memberPager.transform, ">", new Vector2(380f, -100f), 50f, () => TurnMemberPage(1), RowButtonHeight);
            for (int i = 0; i < MaxCandidates; i++)
            {
                int index = i;
                float y = -135f - i * MemberRowHeight;
                var row = new CandidateRow { Root = Page(page, "Candidate" + i).gameObject };
                Transform parent = row.Root.transform;
                row.Name = Label(parent, new Vector2(-290f, y), 16, beige, 220f, MemberRowHeight, TextAnchor.MiddleLeft);
                row.Admit = ButtonObject(Button(parent, "$aoj_btn_admit", new Vector2(65f, y), 174f, () => Admit(index, SettlementRole.Karl), RowButtonHeight));
                row.CoJarl = ButtonObject(Button(parent, "$aoj_btn_admit_jarl", new Vector2(245f, y), 174f, () => Admit(index, SettlementRole.Jarl), RowButtonHeight));
                _candidateRows[i] = row;
            }
            Label(page, new Vector2(0f, -228f), 13, beige, Width - 60f, 56f, TextAnchor.UpperLeft).text = Localize("$aoj_ranks_help");
        }

        private static GameObject ButtonObject(Text label) => label.GetComponentInParent<Button>().gameObject;

        private void BuildSettlers(Transform page)
        {
            Color beige = GUIManager.Instance.ValheimBeige;
            _rosterHeader = Label(page, new Vector2(0f, 205f), 16, GUIManager.Instance.ValheimOrange, Width - 60f, 30f, TextAnchor.MiddleLeft);
            for (int i = 0; i < RowsPerPage; i++)
            {
                int index = i;
                float y = FirstRowY - i * RowHeight;
                var row = new Row { Root = Page(page, "Row" + i).gameObject };
                row.Name = Label(row.Root.transform, new Vector2(-265f, y), 17, GUIManager.Instance.ValheimOrange, 250f, RowHeight, TextAnchor.MiddleLeft);
                row.Status = Label(row.Root.transform, new Vector2(-15f, y), 14, beige, 250f, RowHeight, TextAnchor.MiddleLeft);
                row.Buttons = Page(row.Root.transform, "Buttons").gameObject;
                Button(row.Buttons.transform, "$aoj_btn_follow", new Vector2(175f, y), 84f, () => RowFollow(index), RowButtonHeight);
                Button(row.Buttons.transform, "$aoj_btn_home", new Vector2(265f, y), 84f, () => RowGoHome(index), RowButtonHeight);
                row.DismissLabel = Button(row.Buttons.transform, "$aoj_btn_dismiss", new Vector2(355f, y), 84f, () => RowDismiss(index), RowButtonHeight);
                row.Dismiss = ButtonObject(row.DismissLabel);
                _rows[i] = row;
            }
            Button(page, "<", new Vector2(-90f, -230f), 60f, () => TurnPage(-1), RowButtonHeight);
            _pageLabel = Label(page, new Vector2(0f, -230f), 16, beige, 120f, 30f, TextAnchor.MiddleCenter);
            Button(page, ">", new Vector2(90f, -230f), 60f, () => TurnPage(1), RowButtonHeight);
        }

        private void BuildStorage(Transform page)
        {
            Color beige = GUIManager.Instance.ValheimBeige;
            // The totals take two or three lines; the chest list starts right under them.
            _totals = Label(page, new Vector2(0f, 175f), 16, beige, Width - 60f, 70f, TextAnchor.UpperLeft);
            _chestList = Label(page, new Vector2(0f, -40f), 14, beige, Width - 60f, 340f, TextAnchor.UpperLeft);
        }

        protected override bool IsTargetValid()
        {
            Player local = Player.m_localPlayer;
            return _table != null && _table.Data != null && local != null && !local.IsDead() &&
                   Vector3.Distance(local.transform.position, _table.transform.position) <= MaxDistance;
        }

        protected override void Refresh()
        {
            SettlementData data = _table.Data;
            _title.text = JarlTable.DisplayName(data);
            switch (ActiveTab)
            {
                case 0:
                    RefreshOverview(data);
                    break;
                case 1:
                    RefreshMembers(data);
                    break;
                case 2:
                    RefreshSettlers(data);
                    break;
                case 3:
                    _work.text = Localize(WorkText(data));
                    _workControls.SetActive(May(SettlementRight.Manage));
                    _manualWorkLabel.text = Localize(_table.ManualWork ? "$aoj_work_manual" : "$aoj_work_auto");
                    break;
                case 4:
                    RefreshStorage(data);
                    break;
                case 5:
                    RefreshDefense(data);
                    break;
                default:
                    _chronicle.text = Settlement.Chronicle.Describe(_table.NetView.GetZDO(), 22);
                    break;
            }
        }

        protected override void OnHidden()
        {
            _table = null;
            _chests.Clear();
            foreach (CandidateRow row in _candidateRows)
            {
                row.Player = null;
            }
        }

        private bool May(SettlementRight right) => _table != null && _table.RoleOf(Player.m_localPlayer).Allows(right);

        // ---------------------------------------------------------------- settlement tab

        private void RefreshOverview(SettlementData data)
        {
            SettlementRole role = _table.RoleOf(Player.m_localPlayer);
            List<Settler> residents = data.Settlers.Select(s => Settler.FindByUid(s.Uid)).Where(s => s != null && s.Zdo != null).ToList();
            int atHome = residents.Count(s => s.IsAtHome);
            float morale = residents.Count > 0 ? residents.Average(s => Needs.Morale(s.Zdo)) : 0f;
            float radius = JarlTable.RadiusOf(data);
            int servings = SettlementCauldron.Loaded
                .Where(c => c != null && Vector3.Distance(c.transform.position, _table.transform.position) <= radius)
                .Sum(c => c.Servings());
            float foodDays = data.Settlers.Count > 0 ? servings / (data.Settlers.Count * 2f) : 0f;
            var alerts = new List<string>();
            int noBed = data.Settlers.Count - data.SettlersWithBed;
            if (noBed > 0)
            {
                alerts.Add(Localize("$aoj_alert_beds", noBed.ToString()));
            }
            if (data.Settlers.Count > 0 && servings == 0)
            {
                alerts.Add(Localize("$aoj_alert_food"));
            }
            if (residents.Any(s => s.JobProblem.Length > 0))
            {
                alerts.Add(Localize("$aoj_alert_work"));
            }
            string feast = _table.FeastUntil > Core.WorldClock.Now ? "  ·  $aoj_feast_on" : "";
            _summary.text = Localize(_table.BuildSummary(data) + "\n\n$aoj_settlers: " + JarlTable.BuildRosterText(data) +
                                     $"\n$aoj_at_home: {atHome}/{data.Settlers.Count}  ·  $aoj_morale: {Mathf.RoundToInt(morale)}{feast}" +
                                     $"\n$aoj_food: {servings} ($aoj_food_days {foodDays:0.0})" +
                                     (alerts.Count > 0 ? "\n<color=#ff9060>" + string.Join("\n", alerts) + "</color>" : ""));
            bool live = role.Allows(SettlementRight.Live);
            bool manage = role.Allows(SettlementRight.Manage);
            _liveControls.SetActive(live);
            _manageControls.SetActive(manage);
            _hint.text = !live ? Localize("$aoj_read_only")
                : !manage ? Localize("$aoj_rank_hint", Localize(Permissions.Token(role)))
                : "";
        }

        // ---------------------------------------------------------------- members tab

        private void RefreshMembers(SettlementData data)
        {
            Player me = Player.m_localPlayer;
            long myId = me != null ? me.GetPlayerID() : 0L;
            int extra = JarlTable.ExtraMembers(data);
            _membersHeader.text = Localize("$aoj_members_header", data.Members.Count.ToString(), data.JarlCount.ToString(),
                AoJConfig.MaxJarls.Value.ToString());
            _membersBonus.text = Localize("$aoj_members_bonus", JarlTable.CapacityOf(data).ToString(), JarlTable.CapacityFor(data.Tier).ToString(),
                Mathf.RoundToInt(JarlTable.RadiusOf(data)).ToString(), Mathf.RoundToInt(JarlTable.RadiusFor(data.Tier)).ToString(),
                (AoJConfig.TotemSlotsPerMember.Value * extra).ToString());

            int pages = Mathf.Max(1, Mathf.CeilToInt(data.Members.Count / (float)MemberRows));
            _memberPage = Mathf.Clamp(_memberPage, 0, pages - 1);
            _memberPager.SetActive(pages > 1);
            _memberPageLabel.text = $"{_memberPage + 1}/{pages}";
            SettlementMember senior = data.Jarl;
            for (int i = 0; i < MemberRows; i++)
            {
                int index = _memberPage * MemberRows + i;
                MemberRow row = _memberRows[i];
                SettlementMember member = index < data.Members.Count ? data.Members[index] : null;
                row.Root.SetActive(member != null);
                row.PlayerId = member?.PlayerId ?? 0L;
                if (member == null)
                {
                    continue;
                }
                bool self = member.PlayerId == myId;
                row.Name.text = self ? member.Name + Localize(" ($aoj_you)") : member.Name;
                row.Rank.text = Localize(Permissions.Token(member.Role)) + (member == senior && data.JarlCount > 1 ? " *" : "");

                // Karl -> Huskarl -> Hersir step by step; Jarl has its own button (co-Jarl, or handing the title over).
                row.Up.SetActive(member.Role < SettlementRole.Hersir && _table.RankProblem(me, member.PlayerId, member.Role + 1) == null);
                row.Down.SetActive(member.Role > SettlementRole.Karl && _table.RankProblem(me, member.PlayerId, member.Role - 1) == null);
                bool coJarl = _table.RankProblem(me, member.PlayerId, SettlementRole.Jarl) == null;
                bool handOver = !coJarl && !self && member.Role != SettlementRole.Jarl && data.RoleOf(myId) == SettlementRole.Jarl;
                row.Jarl.SetActive(coJarl || handOver);
                row.JarlLabel.text = Localize(handOver ? (Confirming(member.PlayerId, "handover") ? "$aoj_btn_confirm" : "$aoj_btn_handover") : "$aoj_btn_cojarl");
                row.Remove.SetActive(_table.RankProblem(me, member.PlayerId, SettlementRole.Guest) == null);
                row.RemoveLabel.text = Localize(Confirming(member.PlayerId, "remove") ? "$aoj_btn_confirm" : self ? "$aoj_btn_leave" : "$aoj_btn_remove");
            }

            // Players standing at the table who are not members yet: a Hersir admits them as Karls, a Jarl as co-Jarls.
            List<Player> candidates = me == null ? new List<Player>() : Player.GetAllPlayers()
                .Where(p => p != null && p != me && data.RoleOf(p.GetPlayerID()) == SettlementRole.Guest &&
                            Vector3.Distance(p.transform.position, _table.transform.position) <= CandidateRange)
                .OrderBy(p => Vector3.Distance(p.transform.position, _table.transform.position))
                .Take(MaxCandidates)
                .ToList();
            _candidatesLabel.text = Localize(candidates.Count > 0 ? "$aoj_members_candidates" : "$aoj_members_no_candidates");
            for (int i = 0; i < MaxCandidates; i++)
            {
                CandidateRow row = _candidateRows[i];
                row.Player = i < candidates.Count ? candidates[i] : null;
                row.Root.SetActive(row.Player != null);
                if (row.Player == null)
                {
                    continue;
                }
                long id = row.Player.GetPlayerID();
                row.Name.text = row.Player.GetPlayerName();
                row.Admit.SetActive(_table.RankProblem(me, id, SettlementRole.Karl) == null);
                row.CoJarl.SetActive(_table.RankProblem(me, id, SettlementRole.Jarl) == null);
            }
        }

        private SettlementMember RowMember(int index)
        {
            SettlementData data = _table != null ? _table.Data : null;
            long id = _memberRows[index].PlayerId;
            return data != null && id != 0L ? data.Members.Find(m => m.PlayerId == id) : null;
        }

        private void ChangeRank(int index, int step)
        {
            SettlementMember member = RowMember(index);
            if (member != null)
            {
                RequestRank(member.PlayerId, member.Name, member.Role + step);
            }
        }

        // A co-Jarl while there is room; otherwise the title itself changes hands (two clicks).
        private void MemberJarl(int index)
        {
            SettlementMember member = RowMember(index);
            Player me = Player.m_localPlayer;
            if (member == null || me == null)
            {
                return;
            }
            if (_table.RankProblem(me, member.PlayerId, SettlementRole.Jarl) == null)
            {
                RequestRank(member.PlayerId, member.Name, SettlementRole.Jarl);
                return;
            }
            if (!Confirm(member.PlayerId, "handover"))
            {
                return;
            }
            _table.RequestHandOver(member.PlayerId, member.Name);
            me.Message(MessageHud.MessageType.Center, Localize("$aoj_msg_new_jarl", member.Name));
            RefreshNow();
        }

        // Two clicks: off the list (or, on your own row, leaving the settlement).
        private void MemberRemove(int index)
        {
            SettlementMember member = RowMember(index);
            if (member != null && Confirm(member.PlayerId, "remove"))
            {
                RequestRank(member.PlayerId, member.Name, SettlementRole.Guest);
            }
        }

        private void Admit(int index, SettlementRole rank)
        {
            Player player = _candidateRows[index].Player;
            if (player != null)
            {
                RequestRank(player.GetPlayerID(), player.GetPlayerName(), rank);
            }
        }

        private void RequestRank(long playerId, string name, SettlementRole rank)
        {
            Player me = Player.m_localPlayer;
            if (_table == null || me == null)
            {
                return;
            }
            string problem = _table.RankProblem(me, playerId, rank);
            if (problem != null)
            {
                me.Message(MessageHud.MessageType.Center, Localize(problem, AoJConfig.MaxJarls.Value.ToString()));
                return;
            }
            _table.RequestSetRank(playerId, name, rank);
            string message = rank != SettlementRole.Guest ? Localize("$aoj_msg_rank_set", name, Localize(Permissions.Token(rank)))
                : playerId == me.GetPlayerID() ? Localize("$aoj_msg_rank_left")
                : Localize("$aoj_msg_rank_removed", name);
            me.Message(MessageHud.MessageType.Center, message);
            RefreshNow();
        }

        private bool Confirming(long playerId, string action) =>
            _confirmMember == playerId && _confirmAction == action && Time.time < _confirmMemberUntil;

        private bool Confirm(long playerId, string action)
        {
            if (Confirming(playerId, action))
            {
                _confirmMember = 0L;
                return true;
            }
            _confirmMember = playerId;
            _confirmAction = action;
            _confirmMemberUntil = Time.time + ConfirmSeconds;
            RefreshNow();
            return false;
        }

        private void TurnMemberPage(int step)
        {
            _memberPage += step;
            RefreshNow();
        }

        private void Rename()
        {
            if (_table != null)
            {
                _table.RequestRename(TextEntry.text);
                TextEntry.text = "";
            }
        }

        private void Act(Action<JarlTable> action)
        {
            if (_table != null && Player.m_localPlayer != null)
            {
                action(_table);
                RefreshNow();
            }
        }

        // ---------------------------------------------------------------- settlers tab

        private void RefreshSettlers(SettlementData data)
        {
            int pages = Mathf.Max(1, Mathf.CeilToInt(data.Settlers.Count / (float)RowsPerPage));
            _page = Mathf.Clamp(_page, 0, pages - 1);
            _pageLabel.text = $"{_page + 1}/{pages}";
            _rosterHeader.text = Localize($"$aoj_settlers: {data.Settlers.Count}/{JarlTable.CapacityOf(data)} · $aoj_beds: {data.SettlersWithBed}" +
                                          (data.Settlers.Count == 0 ? "   ($aoj_no_settlers)" : ""));
            bool member = May(SettlementRight.Live);
            bool manager = May(SettlementRight.Manage);
            for (int i = 0; i < RowsPerPage; i++)
            {
                int index = _page * RowsPerPage + i;
                Row row = _rows[i];
                RosterEntry entry = index < data.Settlers.Count ? data.Settlers[index] : null;
                row.Root.SetActive(entry != null);
                if (entry == null)
                {
                    row.Uid = 0L;
                    continue;
                }

                Settler settler = Settler.FindByUid(entry.Uid);
                row.Uid = entry.Uid;
                row.Name.text = JarlTable.SettlerName(entry);
                string bed = entry.HasBed ? "$aoj_bed_short_yes" : "$aoj_bed_short_no";
                row.Status.text = Localize(settler != null ? settler.ShortStatus() + "\n" + bed : "$aoj_far_away\n" + bed);
                // Orders need the settler loaded here; sending one away needs only the roster (and a Hersir).
                row.Buttons.SetActive(member);
                row.Dismiss.SetActive(manager);
                bool confirming = _confirmUid == entry.Uid && Time.time < _confirmUntil;
                row.DismissLabel.text = Localize(confirming ? "$aoj_btn_confirm" : "$aoj_btn_dismiss");
            }
        }

        private void TurnPage(int step)
        {
            _page += step;
            RefreshNow();
        }

        private Settler RowSettler(int index)
        {
            Settler settler = Settler.FindByUid(_rows[index].Uid);
            if (settler == null && Player.m_localPlayer != null)
            {
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, Localize("$aoj_far_away"));
            }
            return settler;
        }

        private void RowFollow(int index)
        {
            Settler settler = RowSettler(index);
            if (settler != null)
            {
                settler.SendFollow(Player.m_localPlayer);
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, Localize("$aoj_msg_follow", settler.DisplayName));
            }
        }

        private void RowGoHome(int index)
        {
            Settler settler = RowSettler(index);
            if (settler != null)
            {
                settler.RequestGoHome(Player.m_localPlayer);
            }
        }

        // Two clicks within a few seconds: a settler sent away has to be brought back and accepted again.
        private void RowDismiss(int index)
        {
            long uid = _rows[index].Uid;
            if (uid == 0L || _table == null)
            {
                return;
            }
            if (_confirmUid != uid || Time.time > _confirmUntil)
            {
                _confirmUid = uid;
                _confirmUntil = Time.time + ConfirmSeconds;
                RefreshNow();
                return;
            }
            _confirmUid = 0L;
            _table.RequestDismiss(uid);
            Player.m_localPlayer.Message(MessageHud.MessageType.Center, Localize("$aoj_msg_dismissed", _rows[index].Name.text));
            RefreshNow();
        }

        // ---------------------------------------------------------------- work tab

        private string WorkText(SettlementData data)
        {
            var text = new StringBuilder();
            List<Work.WorkTotem> totems = Work.WorkTotem.Loaded.Where(t => t != null && t.Settlement == _table).ToList();
            if (totems.Count == 0)
            {
                text.Append("$aoj_work_none\n");
            }
            foreach (Work.WorkTotem totem in totems.OrderByDescending(t => t.Priority).ThenBy(t => t.Job))
            {
                List<Settler> workers = totem.Workers();
                int distance = Mathf.RoundToInt(Vector3.Distance(totem.transform.position, _table.transform.position));
                text.Append($"<color=#e0c080>{Work.JobInfo.Token(totem.Job)}</color> ({distance} m, {workers.Count}/{totem.Capacity}, {Work.WorkTotem.PriorityToken(totem.Priority)}): ");
                text.Append(workers.Count == 0 ? "-" : string.Join(", ", workers.Select(w => w.JobProblem.Length > 0 ? $"{w.DisplayName} <color=#ff9060>({w.JobProblem})</color>" : w.DisplayName)));
                if (!Work.JobInfo.IsUnlocked(totem.Job, data.Tier))
                {
                    text.Append(" <color=#ff9060>($aoj_job_locked)</color>");
                }
                text.Append('\n');
            }
            List<Settler> idle = data.Settlers.Select(s => Settler.FindByUid(s.Uid)).Where(s => s != null && s.JobId == 0L && s.Role == Army.CombatRole.None).ToList();
            text.Append("\n<color=#e0c080>$aoj_unemployed</color>: ").Append(idle.Count == 0 ? "-" : string.Join(", ", idle.Select(s => s.DisplayName)));
            text.Append("\n\n<color=#b0b0b0>$aoj_work_hint</color>");
            text.Append("\n<color=#b0b0b0>").Append(_table.ManualWork ? "$aoj_work_manual_hint" : "$aoj_work_auto_hint").Append("</color>");
            return text.ToString();
        }

        private void ToggleManualWork()
        {
            if (_table != null)
            {
                _table.RequestManualWork(!_table.ManualWork);
            }
        }

        // ---------------------------------------------------------------- defense tab

        private void RefreshDefense(SettlementData data)
        {
            var text = new StringBuilder();
            bool besieged = _table.UnderSiege;
            text.Append("$aoj_alarm: ").Append(_table.AlarmOn ? "<color=#ff6040>$aoj_alarm_on</color>" : "$aoj_alarm_off");
            text.Append("  ·  $aoj_fame: ").Append(_table.Fame);
            if (besieged)
            {
                text.Append("  ·  <color=#ff6040>$aoj_besieged</color>");
            }
            text.Append('\n');

            List<Settler> soldiers = data.Settlers.Select(s => Settler.FindByUid(s.Uid)).Where(s => s != null && s.Role != Army.CombatRole.None).ToList();
            text.Append("\n<color=#e0c080>$aoj_troop</color> (").Append(soldiers.Count).Append("):\n");
            foreach (Settler soldier in soldiers)
            {
                Army.WarBanner post = Army.WarBanner.FindById(soldier.PostId);
                text.Append($"  {soldier.DisplayName} - {Army.CombatRoles.Token(soldier.Role)}")
                    .Append(post != null ? $" @ {Army.WarBanner.KindToken(post.Kind)}" : " ($aoj_no_post)").Append('\n');
            }
            List<Army.WarBanner> banners = Army.WarBanner.Loaded.Where(b => b != null && b.Settlement == _table).ToList();
            text.Append("\n<color=#e0c080>$aoj_banners</color> (").Append(banners.Count).Append("): ");
            text.Append(banners.Count == 0 ? "$aoj_banners_none" :
                string.Join(", ", banners.Select(b => $"{Army.WarBanner.KindToken(b.Kind)} {b.Posted().Count}/{b.Places}")));
            int armories = Army.Armory.Loaded.Count(a => a != null && Vector3.Distance(a.transform.position, _table.transform.position) <= JarlTable.RadiusOf(data));
            text.Append("\n$aoj_armories: ").Append(armories);
            int breaches = _table.BreachList.Count;
            if (breaches > 0)
            {
                text.Append("\n<color=#ff9060>").Append(Localize("$aoj_breaches_waiting", breaches.ToString())).Append("</color>");
            }
            text.Append("\n\n<color=#b0b0b0>$aoj_defense_hint</color>");
            _defense.text = Localize(text.ToString());
            _defenseControls.SetActive(May(SettlementRight.Military));
            _alarmLabel.text = Localize(_table.AlarmOn ? "$aoj_alarm_end" : "$aoj_alarm_sound");
        }

        // ---------------------------------------------------------------- storage tab

        // Read-only: every machine has the chests of a loaded settlement with their current contents.
        private void RefreshStorage(SettlementData data)
        {
            SettlementStorage.CollectChests(_table, _chests);
            if (_chests.Count == 0)
            {
                _totals.text = Localize("$aoj_storage_none");
                _chestList.text = "";
                return;
            }

            var totals = new Dictionary<string, int>();
            var lines = new StringBuilder();
            int shown = 0;
            foreach (Container chest in _chests.OrderBy(c => Vector3.Distance(c.transform.position, _table.transform.position)))
            {
                Inventory inventory = chest.GetInventory();
                var contents = new Dictionary<string, int>();
                foreach (ItemDrop.ItemData item in inventory.GetAllItems())
                {
                    string name = item.m_shared.m_name;
                    contents[name] = (contents.TryGetValue(name, out int count) ? count : 0) + item.m_stack;
                    totals[name] = (totals.TryGetValue(name, out int total) ? total : 0) + item.m_stack;
                }
                if (shown++ >= MaxChestLines)
                {
                    continue;
                }
                int distance = Mathf.RoundToInt(Vector3.Distance(chest.transform.position, _table.transform.position));
                string held = contents.Count == 0
                    ? "$aoj_storage_empty_chest"
                    : string.Join(", ", contents.OrderByDescending(c => c.Value).Take(4).Select(c => $"{c.Key} {c.Value}")) +
                      (contents.Count > 4 ? $" (+{contents.Count - 4})" : "");
                int used = inventory.NrOfItems();
                int size = used + inventory.GetEmptySlots();
                string assigned = SettlementStorage.AssignedKind(chest);
                // A chest's kind: the one a member gave it, or what the settlers take it for from what it holds.
                List<string> autoKinds = assigned.Length > 0 ? null : ChestIndex.KindTokens(chest);
                string kind = assigned.Length > 0 ? $" [{SettlementStorage.KindToken(assigned)}]"
                    : autoKinds.Count > 0 ? $" [{string.Join(", ", autoKinds.Take(2))}]" : "";
                lines.Append($"<color=#e0c080>{chest.m_name}{kind}</color> ({distance} m, {used}/{size}): {held}\n");
            }
            if (_chests.Count > MaxChestLines)
            {
                lines.Append($"... (+{_chests.Count - MaxChestLines})\n");
            }
            lines.Append("\n<color=#b0b0b0>$aoj_storage_hint</color>");

            string sums = totals.Count == 0
                ? "-"
                : string.Join(" · ", totals.OrderByDescending(t => t.Value).Take(MaxTotals).Select(t => $"{t.Key} {t.Value}"));
            _totals.text = Localize($"<color=#e0c080>$aoj_storage_total</color> ({_chests.Count}): {sums}");
            _chestList.text = Localize(lines.ToString());
        }
    }
}
