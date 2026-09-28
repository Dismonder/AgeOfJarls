using System.Collections.Generic;
using System.Linq;
using System.Text;
using AgeOfJarls.Settlers;
using AgeOfJarls.Work;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace AgeOfJarls.UI
{
    /// <summary>
    /// Window of a Work Totem ([E] on it): its workers and their problems, the zone radius and the working hours.
    /// Everyone may look; the Jarl and the Hersirs of its settlement assign workers and change the settings.
    /// </summary>
    internal sealed class TotemWindow : AojWindow
    {
        private const float Width = 640f;
        private const float Height = 660f;
        private const float MaxDistance = 12f;
        private const int WorkerRows = 6;
        private const float RadiusStep = 5f;

        private static TotemWindow s_instance;

        private readonly Text[] _workerNames = new Text[WorkerRows];
        private readonly GameObject[] _workerRows = new GameObject[WorkerRows];
        private readonly long[] _workerUids = new long[WorkerRows];
        private WorkTotem _totem;
        private Text _title;
        private Text _info;
        private Text _hoursLabel;
        private Text _priorityLabel;
        private Text _replantLabel;
        private GameObject _replantButton;
        private Text _upgradeLabel;
        private GameObject _controls;

        internal static void Open(WorkTotem totem)
        {
            if (GUIManager.CustomGUIFront == null || totem == null)
            {
                return;
            }
            if (s_instance == null)
            {
                s_instance = CreateWindow<TotemWindow>("AoJ_TotemWindow", Width, Height);
            }
            s_instance._totem = totem;
            s_instance.ShowWindow();
        }

        protected override void Build(Transform root)
        {
            Color beige = GUIManager.Instance.ValheimBeige;
            _title = Label(root, new Vector2(0f, 290f), 26, GUIManager.Instance.ValheimOrange, Width - 40f, 40f, TextAnchor.MiddleCenter);
            _info = Label(root, new Vector2(0f, 190f), 16, beige, Width - 60f, 150f, TextAnchor.UpperLeft);

            _controls = Page(root, "Controls").gameObject;
            Transform controls = _controls.transform;
            for (int i = 0; i < WorkerRows; i++)
            {
                int index = i;
                float y = 90f - i * 40f;
                _workerRows[i] = Page(controls, "Worker" + i).gameObject;
                _workerNames[i] = Label(_workerRows[i].transform, new Vector2(-110f, y), 17, GUIManager.Instance.ValheimOrange, 330f, 36f, TextAnchor.MiddleLeft);
                Button(_workerRows[i].transform, "$aoj_btn_release", new Vector2(190f, y), 150f, () => Release(index), RowButtonHeight);
            }
            Button(controls, "$aoj_btn_assign", new Vector2(-200f, -160f), 200f, Assign);
            _priorityLabel = Button(controls, "", new Vector2(8f, -160f), 196f, CyclePriority);
            Button(controls, "$aoj_radius_less", new Vector2(-165f, -208f), 150f, () => ChangeRadius(-RadiusStep), RowButtonHeight);
            Button(controls, "$aoj_radius_more", new Vector2(-5f, -208f), 150f, () => ChangeRadius(RadiusStep), RowButtonHeight);
            _hoursLabel = Button(controls, "", new Vector2(175f, -208f), 170f, ToggleHours, RowButtonHeight);
            // Woodcutters only: replant felled trees, or clear the land for good.
            _replantLabel = Button(controls, "", new Vector2(212f, -160f), 196f, ToggleReplant);
            _replantButton = _replantLabel.GetComponentInParent<Button>().gameObject;
            _upgradeLabel = Button(controls, "", new Vector2(0f, -256f), 560f, Upgrade, RowButtonHeight);
            Button(root, "$aoj_close", new Vector2(0f, -294f), 200f, Hide);
        }

        protected override bool IsTargetValid()
        {
            Player local = Player.m_localPlayer;
            return _totem != null && local != null && !local.IsDead() &&
                   Vector3.Distance(local.transform.position, _totem.transform.position) <= MaxDistance;
        }

        protected override void Refresh()
        {
            _title.text = Localize("$aoj_totem: " + JobInfo.Token(_totem.Job));
            List<Settler> workers = _totem.Workers();
            var info = new StringBuilder();
            info.Append($"$aoj_workers: {workers.Count}/{_totem.Capacity} · $aoj_radius {Mathf.RoundToInt(_totem.Radius)} m · ")
                .Append(_totem.AllDay ? "$aoj_hours_allday" : "$aoj_hours_day");
            int level = _totem.Level;
            info.Append($"\n$aoj_totem_level {level}/{WorkTotem.MaxLevel}");
            if (level > 1)
            {
                string bonus = Localize("$aoj_totem_bonus", Mathf.RoundToInt((_totem.PaceBonus - 1f) * 100f).ToString(), (level - 1).ToString());
                info.Append(" <color=#b0b0b0>(").Append(bonus).Append(")</color>");
            }
            ToolKind tool = JobInfo.RequiredTool(_totem.Job);
            if (tool != ToolKind.None)
            {
                info.Append("\n$aoj_needs ").Append(JobInfo.ToolToken(tool)).Append(" <color=#b0b0b0>($aoj_tool_hint)</color>");
            }
            info.Append("\n<color=#b0b0b0>").Append(JobHint(_totem.Job)).Append("</color>");
            if (_totem.Settlement == null)
            {
                info.Append("\n<color=#ff9060>$aoj_totem_outside</color>");
            }
            else if (!JobInfo.IsUnlocked(_totem.Job, _totem.Settlement.Data?.Tier ?? 0))
            {
                info.Append("\n<color=#ff9060>$aoj_job_locked</color>");
            }
            _info.text = Localize(info.ToString());

            bool manager = _totem.MayManage(Player.m_localPlayer);
            _controls.SetActive(manager);
            for (int i = 0; i < WorkerRows; i++)
            {
                Settler worker = i < workers.Count ? workers[i] : null;
                _workerRows[i].SetActive(worker != null);
                _workerUids[i] = worker != null ? worker.Uid : 0L;
                if (worker != null)
                {
                    string problem = worker.JobProblem;
                    string name = $"{worker.DisplayName} <color=#b0b0b0>({Mathf.FloorToInt(worker.JobSkill(_totem.Job))})</color>";
                    _workerNames[i].text = Localize(problem.Length > 0 ? $"{name} <color=#ff9060>{problem}</color>" : name);
                }
            }
            _hoursLabel.text = Localize(_totem.AllDay ? "$aoj_hours_allday" : "$aoj_hours_day");
            _priorityLabel.text = Localize("$aoj_priority: " + WorkTotem.PriorityToken(_totem.Priority));
            _replantButton.SetActive(_totem.Job == JobType.Woodcutter);
            _replantLabel.text = Localize(_totem.Replants ? "$aoj_replant_on" : "$aoj_replant_off");
            _upgradeLabel.text = Localize(level >= WorkTotem.MaxLevel
                ? "$aoj_totem_max_level"
                : $"$aoj_totem_upgrade {level + 1}: {WorkTotem.CostText(WorkTotem.UpgradeCost(level + 1))}");
        }

        private void Upgrade()
        {
            if (_totem == null || Player.m_localPlayer == null)
            {
                return;
            }
            int target = _totem.Level + 1;
            string problem = _totem.TryUpgrade(Player.m_localPlayer);
            Player.m_localPlayer.Message(MessageHud.MessageType.Center, Localize(problem ??
                Localize("$aoj_msg_totem_upgraded", Localize(JobInfo.Token(_totem.Job)), target.ToString())));
            RefreshNow();
        }

        private void ToggleReplant()
        {
            if (_totem != null)
            {
                _totem.RequestReplant(!_totem.Replants);
            }
        }

        // Low -> normal -> high -> low: free settlers go to higher priorities first.
        private void CyclePriority()
        {
            if (_totem != null)
            {
                _totem.RequestPriority((_totem.Priority + 1) % (WorkTotem.HighPriority + 1));
            }
        }

        private static string JobHint(JobType job) => "$aoj_job_hint_" + job.ToString().ToLowerInvariant();

        protected override void OnHidden()
        {
            _totem = null;
        }

        private void Assign()
        {
            if (_totem == null)
            {
                return;
            }
            Player player = Player.m_localPlayer;
            if (_totem.Workers().Count >= _totem.Capacity)
            {
                player.Message(MessageHud.MessageType.Center, Localize("$aoj_msg_totem_full"));
                return;
            }
            Settler settler = _totem.NearestFreeSettler();
            if (settler == null)
            {
                player.Message(MessageHud.MessageType.Center, Localize("$aoj_msg_no_free_settler"));
                return;
            }
            settler.RequestSetJob(_totem.Id);
            player.Message(MessageHud.MessageType.Center, Localize("$aoj_msg_assigned", settler.DisplayName, Localize(JobInfo.Token(_totem.Job))));
            RefreshNow();
        }

        private void Release(int index)
        {
            Settler settler = Settler.FindByUid(_workerUids[index]);
            if (settler != null)
            {
                settler.RequestSetJob(0L);
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, Localize("$aoj_msg_released", settler.DisplayName));
                RefreshNow();
            }
        }

        private void ChangeRadius(float step)
        {
            if (_totem != null)
            {
                _totem.RequestRadius(_totem.Radius + step);
                _totem.ShowMarker();
            }
        }

        private void ToggleHours()
        {
            if (_totem != null)
            {
                _totem.RequestAllDay(!_totem.AllDay);
            }
        }
    }
}
