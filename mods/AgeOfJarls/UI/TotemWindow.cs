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
        private const float RadiusStep = 5f;

        private static TotemWindow s_instance;

        private ScrollList _workers;
        private readonly List<WorkerRow> _workerRows = new List<WorkerRow>();
        private sealed class WorkerRow
        {
            internal Text Name;
            internal GameObject Release;
            internal long Uid;
        }
        private WorkTotem _totem;
        private Text _title;
        private TextArea _info;
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
            _info = UiKit.TextArea(root, new Vector2(0f, 190f), 600f, 150f);
            _workers = UiKit.List(root, new Vector2(0f, -15f), 600f, 240f, 36f);

            _controls = Page(root, "Controls").gameObject;
            Transform controls = _controls.transform;
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
            SetText(_title, Localize("$aoj_totem: " + JobInfo.Token(_totem.Job)));
            List<Settler> workers = _totem.Workers();
            var info = new StringBuilder();
            info.Append($"$aoj_workers: {workers.Count}/{_totem.Capacity} · $aoj_radius {Mathf.RoundToInt(_totem.Radius)} m · ")
                .Append(_totem.AllDay ? "$aoj_hours_allday" : "$aoj_hours_day");
            int level = _totem.Level;
            info.Append($"\n$aoj_totem_level {level}/{WorkTotem.MaxLevel}");
            if (level > 1)
            {
                string bonus = Localize("$aoj_totem_bonus", Mathf.RoundToInt((_totem.PaceBonus - 1f) * 100f).ToString(), _totem.ExtraPlaces.ToString());
                info.Append(" <color=" + Palette.Muted + ">(").Append(bonus).Append(")</color>");
            }
            ToolKind tool = JobInfo.RequiredTool(_totem.Job);
            if (tool != ToolKind.None)
            {
                info.Append("\n$aoj_needs ").Append(JobInfo.ToolToken(tool)).Append(" <color=" + Palette.Muted + ">($aoj_tool_hint)</color>");
            }
            info.Append("\n<color=" + Palette.Muted + ">").Append(JobHint(_totem.Job)).Append("</color>");
            if (_totem.Settlement == null)
            {
                info.Append("\n<color=" + Palette.Warn + ">$aoj_totem_outside</color>");
            }
            else if (!JobInfo.IsUnlocked(_totem.Job, _totem.Settlement.Data?.Tier ?? 0))
            {
                info.Append("\n<color=" + Palette.Warn + ">$aoj_job_locked</color>");
            }
            _info.SetText(Localize(info.ToString()));

            bool manager = _totem.MayManage(Player.m_localPlayer);
            _controls.SetActive(manager);
            for (int i = 0; i < workers.Count; i++)
            {
                if (i == _workerRows.Count)
                {
                    int index = i;
                    RectTransform root = _workers.Row(i);
                    _workerRows.Add(new WorkerRow
                    {
                        Name = _workers.Text(root, 418f, 17),
                        Release = _workers.Button(root, "$aoj_btn_release", 150f, () => Release(index)).GetComponentInParent<Button>().gameObject,
                    });
                }
                WorkerRow row = _workerRows[i];
                Settler worker = workers[i];
                row.Uid = worker.Uid;
                row.Release.SetActive(manager);
                string name = worker.FullName + " " + Palette.Colored("(" + Mathf.FloorToInt(worker.JobSkill(_totem.Job)) + ")", Palette.Muted);
                SetText(row.Name, Localize(name + (worker.JobProblem.Length > 0 ? " " + Palette.Colored(worker.JobProblem, Palette.Warn) : "")));
            }
            _workers.SetCount(workers.Count);
            SetText(_hoursLabel, Localize(_totem.AllDay ? "$aoj_hours_allday" : "$aoj_hours_day"));
            SetText(_priorityLabel, Localize("$aoj_priority: " + WorkTotem.PriorityToken(_totem.Priority)));
            _replantButton.SetActive(_totem.Job == JobType.Woodcutter);
            SetText(_replantLabel, Localize(_totem.Replants ? "$aoj_replant_on" : "$aoj_replant_off"));
            SetText(_upgradeLabel, Localize(level >= WorkTotem.MaxLevel
                ? "$aoj_totem_max_level"
                : $"$aoj_totem_upgrade {level + 1}: {WorkTotem.CostText(WorkTotem.UpgradeCost(level + 1))}"));
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
            Settler settler = Settler.FindByUid(_workerRows[index].Uid);
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
