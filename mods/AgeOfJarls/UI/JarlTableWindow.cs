using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
    /// The settlement window ([E] on the Jarl's Table), in tabs: the settlement (summary, tier, name, feast), its
    /// members (ranks, admitting players at the table), its settlers (one row each, with orders), work, storage,
    /// defence, the chronicle and families. Everyone may look; each control shows for the ranks that may use it
    /// (<see cref="Permissions"/>). Every change is an RPC that the table's or the settler's owner checks again.
    /// </summary>
    internal sealed class JarlTableWindow : AojWindow
    {
        private const float Width = 840f;
        private const float Height = 680f;
        private const float MaxDistance = 12f;
        private const float CandidateRange = 20f;
        private static JarlTableWindow s_instance;
        private readonly List<Container> _chests = new List<Container>();
        private readonly List<Row> _rows = new List<Row>();
        private readonly List<MemberRow> _memberRows = new List<MemberRow>();
        private readonly List<CandidateRow> _candidateRows = new List<CandidateRow>();
        private JarlTable _table;
        private Text _title;
        private TextArea _summary;
        private Text _hint;
        private GameObject _liveControls;
        private GameObject _manageControls;
        private Text _membersHeader;
        private Text _membersBonus;
        private Text _candidatesLabel;
        private Text _rosterHeader;
        private ScrollList _members;
        private ScrollList _candidates;
        private ScrollList _settlers;
        private TextArea _storage;
        private float _storageAt;
        private uint _storageRevision;

        private GameObject _familyControls;
        private Text _noCouples;
        private Text _noBirths;
        private Text _matchHint;
        private ScrollList _couples;
        private ScrollList _courtships;
        private ScrollList _children;
        private ScrollList _singles;
        private SettlementData _familySource;
        private uint _familyRevision;
        private double _familyNextStage;
        private long _matchUid;
        private readonly Dictionary<long, string> _familyNames = new Dictionary<long, string>();
        private readonly List<Couple> _coupleModels = new List<Couple>();
        private readonly List<Courtship> _courtshipModels = new List<Courtship>();
        private readonly List<ChildRecord> _childModels = new List<ChildRecord>();
        private readonly List<RosterEntry> _singleModels = new List<RosterEntry>();
        private readonly List<FamilyRow> _coupleRows = new List<FamilyRow>();
        private readonly List<FamilyRow> _courtshipRows = new List<FamilyRow>();
        private readonly List<FamilyRow> _familyChildRows = new List<FamilyRow>();
        private readonly List<FamilyRow> _singleRows = new List<FamilyRow>();

        private sealed class FamilyRow
        {
            internal Text Description;
            internal Text Match;
            internal ConfirmButton Separate;
            internal long Uid;
        }

        private sealed class MemberRow
        {
            internal Text Name;
            internal Text Rank;
            internal GameObject Up;
            internal GameObject Down;
            internal ConfirmButton Jarl;
            internal ConfirmButton Remove;
            internal long PlayerId;
        }

        private sealed class CandidateRow
        {
            internal Text Name;
            internal GameObject Admit;
            internal GameObject CoJarl;
            internal Player Player;
        }

        private sealed class Row
        {
            internal Text Name;
            internal Text Status;
            internal GameObject Follow;
            internal GameObject Home;
            internal ConfirmButton Dismiss;
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
            s_instance._storageAt = 0f;
            s_instance._familySource = null;
            s_instance._matchUid = 0L;
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
            Transform families = Page(root, "Families");
            BuildOverview(overview);
            BuildMembers(members);
            BuildSettlers(settlers);
            BuildStorage(storage);
            _work = UiKit.TextArea(work, new Vector2(0f, 0f), 780f, 430f);
            _workControls = Page(work, "WorkControls").gameObject;
            _manualWorkLabel = Button(_workControls.transform, "", new Vector2(0f, -245f), 420f, ToggleManualWork, RowButtonHeight);
            BuildDefense(defense);
            _chronicle = UiKit.TextArea(chronicle, new Vector2(0f, -20f), 780f, 470f);
            BuildFamilies(families);
            Button(root, "$aoj_close", new Vector2(0f, -300f), 200f, Hide);
            Tabs(root, 252f, 0f, ("$aoj_tab_overview", overview), ("$aoj_tab_members", members), ("$aoj_tab_settlers", settlers),
                ("$aoj_tab_work", work), ("$aoj_tab_storage", storage), ("$aoj_tab_defense", defense), ("$aoj_tab_chronicle", chronicle), ("$aoj_tab_families", families));
        }

        private TextArea _work;
        private GameObject _workControls;
        private Text _manualWorkLabel;
        private TextArea _defense;
        private Text _alarmLabel;
        private GameObject _defenseControls;
        private TextArea _chronicle;

        private void BuildDefense(Transform page)
        {
            _defense = UiKit.TextArea(page, new Vector2(0f, 0f), 780f, 420f);
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
            _summary = UiKit.TextArea(page, new Vector2(-170f, 5f), 440f, 410f);
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
            TextEntry = TextInput(manage, "$aoj_settlement_name", new Vector2(x, 50f), width, Rename);
            Button(manage, "$aoj_rename_settlement", new Vector2(x, 2f), width, Rename);
            Button(live, "$aoj_feast", new Vector2(x, -48f), width, () => Act(t => t.TryFeast(Player.m_localPlayer)));
        }

        private void BuildMembers(Transform page)
        {
            _membersHeader = Label(page, new Vector2(0f, 205f), 16, GUIManager.Instance.ValheimOrange, 780f, 30f, TextAnchor.MiddleLeft);
            _membersBonus = Label(page, new Vector2(0f, 176f), 14, GUIManager.Instance.ValheimBeige, 780f, 28f, TextAnchor.MiddleLeft);
            _members = UiKit.List(page, new Vector2(0f, 45f), 780f, 230f, 40f);
            _candidatesLabel = Label(page, new Vector2(0f, -88f), 15, GUIManager.Instance.ValheimOrange, 780f, 28f, TextAnchor.MiddleLeft);
            _candidates = UiKit.List(page, new Vector2(0f, -150f), 780f, 90f, 36f);
            UiKit.TextArea(page, new Vector2(0f, -232f), 780f, 56f).SetText(Localize("$aoj_ranks_help"));
        }

        private static GameObject ButtonObject(Text label) => label.GetComponentInParent<Button>().gameObject;

        private void BuildSettlers(Transform page)
        {
            _rosterHeader = Label(page, new Vector2(0f, 205f), 16, GUIManager.Instance.ValheimOrange, 780f, 30f, TextAnchor.MiddleLeft);
            _settlers = UiKit.List(page, new Vector2(0f, -30f), 800f, 430f, 46f);
        }

        private void BuildStorage(Transform page)
        {
            _storage = UiKit.TextArea(page, new Vector2(0f, -20f), 780f, 470f);
        }

        protected override bool IsTargetValid()
        {
            Player local = Player.m_localPlayer;
            return _table != null && _table.Data != null && local != null && !local.IsDead() &&
                   Vector3.Distance(local.transform.position, _table.transform.position) <= MaxDistance;
        }

        protected override void Refresh()
        {
            if (ActiveTab != 7) _matchUid = 0L;
            SettlementData data = _table.Data;
            SetText(_title, JarlTable.DisplayName(data));
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
                    _work.SetText(Localize(WorkText(data)));
                    _workControls.SetActive(May(SettlementRight.Manage));
                    SetText(_manualWorkLabel, Localize(_table.ManualWork ? "$aoj_work_manual" : "$aoj_work_auto"));
                    break;
                case 4:
                    RefreshStorage(data);
                    break;
                case 5:
                    RefreshDefense(data);
                    break;
                case 6:
                    _chronicle.SetText(Settlement.Chronicle.Describe(_table.NetView.GetZDO(), int.MaxValue));
                    break;
                default:
                    RefreshFamilies(data);
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
            int noBed = data.SettlersNeedingBed - data.SettlersWithBed;
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
            if (data.PregnancyCount() > 0) alerts.Add(Localize("$aoj_alert_expecting", data.PregnancyCount().ToString()));
            string feast = _table.FeastUntil > Core.WorldClock.Now ? "  ·  $aoj_feast_on" : "";
            _summary.SetText(Localize(_table.BuildSummary(data) + "\n\n$aoj_settlers: " + JarlTable.PopulationText(data) +
                                     $"\n$aoj_at_home: {atHome}/{data.Settlers.Count}  ·  $aoj_morale: {Mathf.RoundToInt(morale)}{feast}" +
                                     $"\n$aoj_food: {servings} ($aoj_food_days {foodDays:0.0})" +
                                     "\n$aoj_families: " + Localize("$aoj_family_summary", data.Couples.Count(c => c.A != 0L && c.B != 0L).ToString(), data.MinorCount(WorldClock.Now, WorldClock.DayLength, FamilyConfig.Live).ToString()) +
                                     (alerts.Count > 0 ? "\n<color=" + Palette.Warn + ">" + string.Join("\n", alerts) + "</color>" : "")));
            bool live = role.Allows(SettlementRight.Live);
            bool manage = role.Allows(SettlementRight.Manage);
            _liveControls.SetActive(live);
            _manageControls.SetActive(manage);
            SetText(_hint, !live ? Localize("$aoj_read_only")
                : !manage ? Localize("$aoj_rank_hint", Localize(Permissions.Token(role)))
                : "");
        }

        // ---------------------------------------------------------------- members tab

        private void RefreshMembers(SettlementData data)
        {
            Player me = Player.m_localPlayer;
            long myId = me != null ? me.GetPlayerID() : 0L;
            int extra = JarlTable.ExtraMembers(data);
            SetText(_membersHeader, Localize("$aoj_members_header", data.Members.Count.ToString(), data.JarlCount.ToString(),
                AoJConfig.MaxJarls.Value.ToString()));
            SetText(_membersBonus, Localize("$aoj_members_bonus", JarlTable.CapacityOf(data).ToString(), JarlTable.CapacityFor(data.Tier).ToString(),
                Mathf.RoundToInt(JarlTable.RadiusOf(data)).ToString(), Mathf.RoundToInt(JarlTable.RadiusFor(data.Tier)).ToString(),
                (AoJConfig.TotemSlotsPerMember.Value * extra).ToString()));

            SettlementMember senior = data.Jarl;
            for (int i = 0; i < data.Members.Count; i++)
            {
                if (i == _memberRows.Count)
                {
                    int index = i;
                    RectTransform root = _members.Row(i);
                    var cells = new MemberRow { Name = _members.Text(root, 216f), Rank = _members.Text(root, 120f, 15) };
                    cells.Up = ButtonObject(_members.Button(root, "$aoj_btn_promote", 94f, () => ChangeRank(index, 1)));
                    cells.Down = ButtonObject(_members.Button(root, "$aoj_btn_demote", 94f, () => ChangeRank(index, -1)));
                    cells.Jarl = _members.Confirm(root, "$aoj_btn_cojarl", 94f, () => MemberJarl(index));
                    cells.Remove = _members.Confirm(root, "$aoj_btn_remove", 94f, () => MemberRemove(index));
                    _memberRows.Add(cells);
                }
                MemberRow row = _memberRows[i];
                SettlementMember member = data.Members[i];
                row.PlayerId = member.PlayerId;
                bool self = member.PlayerId == myId;
                SetText(row.Name, self ? member.Name + Localize(" ($aoj_you)") : member.Name);
                SetText(row.Rank, Localize(Permissions.Token(member.Role)) + (member == senior && data.JarlCount > 1 ? " *" : ""));
                row.Up.SetActive(member.Role < SettlementRole.Hersir && _table.RankProblem(me, member.PlayerId, member.Role + 1) == null);
                row.Down.SetActive(member.Role > SettlementRole.Karl && _table.RankProblem(me, member.PlayerId, member.Role - 1) == null);
                bool coJarl = _table.RankProblem(me, member.PlayerId, SettlementRole.Jarl) == null;
                bool handOver = !coJarl && !self && member.Role != SettlementRole.Jarl && data.RoleOf(myId) == SettlementRole.Jarl;
                row.Jarl.Bind(handOver ? "$aoj_btn_handover" : "$aoj_btn_cojarl", member.PlayerId, handOver);
                row.Jarl.gameObject.SetActive(coJarl || handOver);
                row.Remove.Bind(self ? "$aoj_btn_leave" : "$aoj_btn_remove", member.PlayerId);
                row.Remove.gameObject.SetActive(_table.RankProblem(me, member.PlayerId, SettlementRole.Guest) == null);
            }
            _members.SetCount(data.Members.Count);

            // Players standing at the table who are not members yet: a Hersir admits them as Karls, a Jarl as co-Jarls.
            List<Player> candidates = me == null ? new List<Player>() : Player.GetAllPlayers()
                .Where(p => p != null && p != me && data.RoleOf(p.GetPlayerID()) == SettlementRole.Guest &&
                            Vector3.Distance(p.transform.position, _table.transform.position) <= CandidateRange)
                .OrderBy(p => Vector3.Distance(p.transform.position, _table.transform.position))
                .ToList();
            SetText(_candidatesLabel, Localize(candidates.Count > 0 ? "$aoj_members_candidates" : "$aoj_members_no_candidates"));
            for (int i = 0; i < candidates.Count; i++)
            {
                if (i == _candidateRows.Count)
                {
                    int index = i;
                    RectTransform root = _candidates.Row(i);
                    var cells = new CandidateRow { Name = _candidates.Text(root, 360f) };
                    cells.Admit = ButtonObject(_candidates.Button(root, "$aoj_btn_admit", 174f, () => Admit(index, SettlementRole.Karl)));
                    cells.CoJarl = ButtonObject(_candidates.Button(root, "$aoj_btn_admit_jarl", 174f, () => Admit(index, SettlementRole.Jarl)));
                    _candidateRows.Add(cells);
                }
                CandidateRow row = _candidateRows[i];
                row.Player = candidates[i];
                if (row.Player == null)
                {
                    continue;
                }
                long id = row.Player.GetPlayerID();
                SetText(row.Name, row.Player.GetPlayerName());
                row.Admit.SetActive(_table.RankProblem(me, id, SettlementRole.Karl) == null);
                row.CoJarl.SetActive(_table.RankProblem(me, id, SettlementRole.Jarl) == null);
            }
            _candidates.SetCount(candidates.Count);
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
            _table.RequestHandOver(member.PlayerId, member.Name);
            me.Message(MessageHud.MessageType.Center, Localize("$aoj_msg_new_jarl", member.Name));
            RefreshNow();
        }

        // Two clicks: off the list (or, on your own row, leaving the settlement).
        private void MemberRemove(int index)
        {
            SettlementMember member = RowMember(index);
            if (member != null)
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
            SetText(_rosterHeader, Localize($"$aoj_settlers: {JarlTable.PopulationText(data)} · $aoj_beds: {data.SettlersWithBed}/{data.SettlersNeedingBed}" +
                (data.Settlers.Count == 0 ? "   ($aoj_no_settlers)" : "")));
            bool member = May(SettlementRight.Live);
            bool manager = May(SettlementRight.Manage);
            for (int i = 0; i < data.Settlers.Count; i++)
            {
                if (i == _rows.Count)
                {
                    int index = i;
                    RectTransform root = _settlers.Row(i);
                    var cells = new Row { Name = _settlers.Text(root, 224f, 17), Status = _settlers.Text(root, 238f, 14) };
                    cells.Follow = ButtonObject(_settlers.Button(root, "$aoj_btn_follow", 94f, () => RowFollow(index)));
                    cells.Home = ButtonObject(_settlers.Button(root, "$aoj_btn_home", 94f, () => RowGoHome(index)));
                    cells.Dismiss = _settlers.Confirm(root, "$aoj_btn_dismiss", 94f, () => RowDismiss(index));
                    _rows.Add(cells);
                }
                Row row = _rows[i];
                RosterEntry entry = data.Settlers[i];
                Settler settler = Settler.FindByUid(entry.Uid);
                row.Uid = entry.Uid;
                SetText(row.Name, settler != null ? settler.FullName : JarlTable.SettlerName(entry));
                ChildRecord child = data.FindChild(entry.Uid);
                LifeStage stage = settler != null ? settler.Stage : child != null ? FamilyRules.StageAt(child.Born, WorldClock.Now, WorldClock.DayLength, FamilyConfig.Live) : LifeStage.Adult;
                string status = stage != LifeStage.Adult ? FamilyRules.StageToken(stage, settler?.Identity?.Female ?? child?.Female ?? false) + " · " + Localize("$aoj_family_age", Days(settler != null ? settler.Age : WorldClock.Now - child.Born))
                    : settler != null ? settler.ShortStatus() : "$aoj_far_away";
                string bed = !FamilyRules.NeedsOwnBed(stage) ? "" : entry.HasBed ? "$aoj_bed_short_yes" : "$aoj_bed_short_no";
                SetText(row.Status, Localize(status + (bed.Length > 0 ? "\n" + bed : "")));
                row.Follow.SetActive(member && stage >= LifeStage.Youth);
                row.Follow.GetComponent<Button>().interactable = settler != null;
                row.Home.SetActive(member);
                row.Home.GetComponent<Button>().interactable = settler != null;
                row.Dismiss.Bind("$aoj_btn_dismiss", entry.Uid);
                row.Dismiss.gameObject.SetActive(manager);
            }
            _settlers.SetCount(data.Settlers.Count);
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
                text.Append($"<color={Palette.Accent}>{Work.JobInfo.Token(totem.Job)}</color> ({distance} m, {workers.Count}/{totem.Capacity}, {Work.WorkTotem.PriorityToken(totem.Priority)}): ");
                text.Append(workers.Count == 0 ? "-" : string.Join(", ", workers.Select(w => w.JobProblem.Length > 0 ? $"{w.DisplayName} <color={Palette.Warn}>({w.JobProblem})</color>" : w.DisplayName)));
                if (!Work.JobInfo.IsUnlocked(totem.Job, data.Tier))
                {
                    text.Append(" <color=" + Palette.Warn + ">($aoj_job_locked)</color>");
                }
                text.Append('\n');
            }
            List<Settler> idle = data.Settlers.Select(s => Settler.FindByUid(s.Uid)).Where(s => s != null && s.JobId == 0L && s.Role == Army.CombatRole.None).ToList();
            text.Append("\n<color=" + Palette.Accent + ">$aoj_unemployed</color>: ").Append(idle.Count == 0 ? "-" : string.Join(", ", idle.Select(s => s.DisplayName)));
            text.Append("\n\n<color=" + Palette.Muted + ">$aoj_work_hint</color>");
            text.Append("\n<color=" + Palette.Muted + ">").Append(_table.ManualWork ? "$aoj_work_manual_hint" : "$aoj_work_auto_hint").Append("</color>");
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
            text.Append("$aoj_alarm: ").Append(_table.AlarmOn ? "<color=" + Palette.Danger + ">$aoj_alarm_on</color>" : "$aoj_alarm_off");
            text.Append("  ·  $aoj_fame: ").Append(_table.Fame);
            if (besieged)
            {
                text.Append("  ·  <color=" + Palette.Danger + ">$aoj_besieged</color>");
            }
            text.Append('\n');

            List<Settler> soldiers = data.Settlers.Select(s => Settler.FindByUid(s.Uid)).Where(s => s != null && s.Role != Army.CombatRole.None).ToList();
            text.Append("\n<color=" + Palette.Accent + ">$aoj_troop</color> (").Append(soldiers.Count).Append("):\n");
            foreach (Settler soldier in soldiers)
            {
                Army.WarBanner post = Army.WarBanner.FindById(soldier.PostId);
                text.Append($"  {soldier.DisplayName} - {Army.CombatRoles.Token(soldier.Role)}")
                    .Append(post != null ? $" @ {Army.WarBanner.KindToken(post.Kind)}" : " ($aoj_no_post)").Append('\n');
            }
            List<Army.WarBanner> banners = Army.WarBanner.Loaded.Where(b => b != null && b.Settlement == _table).ToList();
            text.Append("\n<color=" + Palette.Accent + ">$aoj_banners</color> (").Append(banners.Count).Append("): ");
            text.Append(banners.Count == 0 ? "$aoj_banners_none" :
                string.Join(", ", banners.Select(b => $"{Army.WarBanner.KindToken(b.Kind)} {b.Posted().Count}/{b.Places}")));
            int armories = Army.Armory.Loaded.Count(a => a != null && Vector3.Distance(a.transform.position, _table.transform.position) <= JarlTable.RadiusOf(data));
            text.Append("\n$aoj_armories: ").Append(armories);
            int breaches = _table.BreachList.Count;
            if (breaches > 0)
            {
                text.Append("\n<color=" + Palette.Warn + ">").Append(Localize("$aoj_breaches_waiting", breaches.ToString())).Append("</color>");
            }
            text.Append("\n\n<color=" + Palette.Muted + ">$aoj_defense_hint</color>");
            _defense.SetText(Localize(text.ToString()));
            _defenseControls.SetActive(May(SettlementRight.Military));
            SetText(_alarmLabel, Localize(_table.AlarmOn ? "$aoj_alarm_end" : "$aoj_alarm_sound"));
        }

        // ---------------------------------------------------------------- storage tab

        private void BuildFamilies(Transform page)
        {
            _familyControls = Page(page, "FamilyControls").gameObject;
            _noCouples = Button(_familyControls.transform, "", new Vector2(-200f, 202f), 380f, () => ToggleFamilyFlag(FamilyFlags.NoNewCouples), 30f);
            _noBirths = Button(_familyControls.transform, "", new Vector2(200f, 202f), 380f, () => ToggleFamilyFlag(FamilyFlags.NoBirths), 30f);
            _matchHint = Label(page, new Vector2(0f, 173f), 14, GUIManager.Instance.ValheimBeige, 780f, 24f, TextAnchor.MiddleLeft);
            FamilyHeading(page, "$aoj_family_couples", -200f, 145f);
            FamilyHeading(page, "$aoj_family_courtships", 200f, 145f);
            _couples = UiKit.List(page, new Vector2(-200f, 48f), 380f, 166f, 40f);
            _courtships = UiKit.List(page, new Vector2(200f, 48f), 380f, 166f, 40f);
            FamilyHeading(page, "$aoj_children", -200f, -54f);
            FamilyHeading(page, "$aoj_family_singles", 200f, -54f);
            _children = UiKit.List(page, new Vector2(-200f, -160f), 380f, 178f, 40f);
            _singles = UiKit.List(page, new Vector2(200f, -160f), 380f, 178f, 36f);
        }

        private static void FamilyHeading(Transform page, string token, float x, float y) =>
            SetText(Label(page, new Vector2(x, y), 16, GUIManager.Instance.ValheimOrange, 380f, 24f, TextAnchor.MiddleLeft), Localize(token));

        private void ToggleFamilyFlag(byte bit)
        {
            if (_table != null && May(SettlementRight.Manage))
                _table.RequestSetFamilyFlags(bit, !_table.Data.HasFamilyFlag(bit));
        }

        // Cache membership and names alongside the table's cached blob, including time-derived promotions.
        private void CacheFamilies(SettlementData data, double now)
        {
            uint revision = _table.NetView.GetZDO().DataRevision;
            if (_familySource == data && revision == _familyRevision && now < _familyNextStage) return;
            _familySource = data;
            _familyRevision = revision;
            _familyNextStage = double.MaxValue;
            _familyNames.Clear();
            _coupleModels.Clear();
            _coupleModels.AddRange(data.Couples);
            _courtshipModels.Clear();
            _courtshipModels.AddRange(data.Courtships);
            _childModels.Clear();
            _childModels.AddRange(data.Children);
            _singleModels.Clear();
            FamilyConfig cfg = FamilyConfig.Live;
            foreach (RosterEntry entry in data.Settlers)
            {
                _familyNames[entry.Uid] = entry.Name;
                ChildRecord child = data.FindChild(entry.Uid);
                bool minor = child != null && FamilyRules.StageAt(child.Born, now, WorldClock.DayLength, cfg) != LifeStage.Adult;
                if (minor)
                    _familyNextStage = Math.Min(_familyNextStage, now + FamilyRules.SecondsToNextStage(now - child.Born, WorldClock.DayLength, cfg));
                if (!minor && data.FindCouple(entry.Uid) == null && data.FindCourtship(entry.Uid) == null)
                    _singleModels.Add(entry);
            }
            if (!_singleModels.Exists(s => s.Uid == _matchUid)) _matchUid = 0L;
        }

        private string FamilyName(long uid)
        {
            Settler live = uid != 0L ? Settler.FindByUid(uid) : null;
            return live != null ? live.FullName : _familyNames.TryGetValue(uid, out string name) ? name : "—";
        }

        private static string Days(double seconds) => (Math.Max(0.0, seconds) / WorldClock.DayLength).ToString("0.0");

        private void RefreshFamilies(SettlementData data)
        {
            double now = WorldClock.Now;
            CacheFamilies(data, now);
            bool manage = May(SettlementRight.Manage);
            _familyControls.SetActive(manage);
            if (!manage) _matchUid = 0L;
            SetText(_noCouples, Localize(data.HasFamilyFlag(FamilyFlags.NoNewCouples) ? "$aoj_btn_no_couples_on" : "$aoj_btn_no_couples_off"));
            SetText(_noBirths, Localize(data.HasFamilyFlag(FamilyFlags.NoBirths) ? "$aoj_btn_no_births_on" : "$aoj_btn_no_births_off"));
            SetText(_matchHint, Localize(manage ? "$aoj_hint_match" : "$aoj_read_only"));
            for (int i = 0; i < _coupleModels.Count; i++)
            {
                if (i == _coupleRows.Count)
                {
                    int index = i;
                    RectTransform root = _couples.Row(i);
                    var row = new FamilyRow { Description = _couples.Text(root, 248f, 15) };
                    row.Separate = _couples.Confirm(root, "$aoj_btn_separate", 96f, () => Separate(index));
                    _coupleRows.Add(row);
                }
                Couple couple = _coupleModels[i];
                FamilyRow cells = _coupleRows[i];
                cells.Uid = couple.A != 0L ? couple.A : couple.B;
                bool paired = couple.A != 0L && couple.B != 0L;
                string line = paired
                    ? FamilyName(couple.A) + Palette.Colored(" ♥ ", Palette.Love) + FamilyName(couple.B) + "\n" +
                        Localize("$aoj_family_since", ((int)(couple.Since / WorldClock.DayLength) + 1).ToString())
                    : FamilyName(cells.Uid);
                line += " · $aoj_family_children: " + couple.ChildrenBorn;
                if (couple.IsExpecting) line += "\n" + Localize("$aoj_family_expecting", Days(couple.DueAt - now));
                SetText(cells.Description, Localize(line));
                cells.Separate.Bind("$aoj_btn_separate", unchecked(couple.A * 397L ^ couple.B));
                cells.Separate.gameObject.SetActive(manage && paired);
            }
            _couples.SetCount(_coupleModels.Count);
            for (int i = 0; i < _courtshipModels.Count; i++)
            {
                if (i == _courtshipRows.Count)
                    _courtshipRows.Add(new FamilyRow { Description = _courtships.Text(_courtships.Row(i), 348f, 15) });
                Courtship courtship = _courtshipModels[i];
                SetText(_courtshipRows[i].Description, Localize(FamilyName(courtship.A) + Palette.Colored(" ♥? ", Palette.Love) + FamilyName(courtship.B) + "\n" +
                    Localize("$aoj_family_courtship_left", Days(courtship.StartedAt + FamilyConfig.Live.CourtshipDays * WorldClock.DayLength - now))));
            }
            _courtships.SetCount(_courtshipModels.Count);
            for (int i = 0; i < _childModels.Count; i++)
            {
                if (i == _familyChildRows.Count)
                    _familyChildRows.Add(new FamilyRow { Description = _children.Text(_children.Row(i), 348f, 15) });
                ChildRecord child = _childModels[i];
                LifeStage stage = FamilyRules.StageAt(child.Born, now, WorldClock.DayLength, FamilyConfig.Live);
                SetText(_familyChildRows[i].Description, Localize(FamilyName(child.Uid) + " · " + FamilyRules.StageToken(stage, child.Female) + " · " +
                    Localize("$aoj_family_age", Days(now - child.Born)) + "\n$aoj_family_parents: " + FamilyName(child.Mother) + " / " + FamilyName(child.Father)));
            }
            _children.SetCount(_childModels.Count);
            for (int i = 0; i < _singleModels.Count; i++)
            {
                if (i == _singleRows.Count)
                {
                    int index = i;
                    RectTransform root = _singles.Row(i);
                    var row = new FamilyRow { Description = _singles.Text(root, 248f, 15) };
                    row.Match = _singles.Button(root, "$aoj_btn_match", 96f, () => Match(index));
                    _singleRows.Add(row);
                }
                FamilyRow cells = _singleRows[i];
                cells.Uid = _singleModels[i].Uid;
                SetText(cells.Description, FamilyName(cells.Uid));
                cells.Description.color = cells.Uid == _matchUid ? GUIManager.Instance.ValheimOrange : GUIManager.Instance.ValheimBeige;
                ButtonObject(cells.Match).SetActive(manage);
            }
            _singles.SetCount(_singleModels.Count);
        }

        private void Separate(int index)
        {
            if (_table != null && May(SettlementRight.Manage)) _table.RequestSeparate(_coupleRows[index].Uid);
        }

        private void Match(int index)
        {
            if (_table == null || !May(SettlementRight.Manage)) return;
            long uid = _singleRows[index].Uid;
            if (_matchUid == 0L || _matchUid == uid)
            {
                _matchUid = _matchUid == uid ? 0L : uid;
                RefreshNow();
                return;
            }
            string problem = FamilySim.PairProblem(_table.Data, _matchUid, uid, WorldClock.Now);
            if (problem != null)
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, Localize("$aoj_msg_match_bad", Localize(problem)));
            else
            {
                _table.RequestArrangeCouple(_matchUid, uid);
                _matchUid = 0L;
            }
            RefreshNow();
        }

        // Read-only: every machine has the chests of a loaded settlement with their current contents.
        private void RefreshStorage(SettlementData data)
        {
            uint revision = _table.NetView.GetZDO().DataRevision;
            if (Time.unscaledTime < _storageAt && revision == _storageRevision) return;
            _storageAt = Time.unscaledTime + 2f;
            _storageRevision = revision;
            SettlementStorage.CollectChests(_table, _chests);
            if (_chests.Count == 0)
            {
                _storage.SetText(Localize("$aoj_storage_none"));
                return;
            }
            var totals = new Dictionary<string, int>();
            var lines = new StringBuilder();
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
                int distance = Mathf.RoundToInt(Vector3.Distance(chest.transform.position, _table.transform.position));
                string held = contents.Count == 0 ? "$aoj_storage_empty_chest" : string.Join(", ", contents.OrderByDescending(c => c.Value).Select(c => $"{c.Key} {c.Value}"));
                int used = inventory.NrOfItems();
                int size = used + inventory.GetEmptySlots();
                string assigned = SettlementStorage.AssignedKind(chest);
                List<string> autoKinds = assigned.Length > 0 ? null : ChestIndex.KindTokens(chest);
                string kind = assigned.Length > 0 ? $" [{SettlementStorage.KindToken(assigned)}]"
                    : autoKinds.Count > 0 ? $" [{string.Join(", ", autoKinds)}]" : "";
                lines.Append(Palette.Colored(chest.m_name + kind, Palette.Accent)).Append($" ({distance} m, {used}/{size}): {held}\n");
            }
            lines.Append("\n").Append(Palette.Colored("$aoj_storage_hint", Palette.Muted));
            string sums = totals.Count == 0 ? "—" : string.Join(" · ", totals.OrderByDescending(t => t.Value).Select(t => $"{t.Key} {t.Value}"));
            _storage.SetText(Localize(Palette.Colored("$aoj_storage_total", Palette.Accent) + $" ({_chests.Count}): {sums}\n\n" + lines));
        }
    }
}
