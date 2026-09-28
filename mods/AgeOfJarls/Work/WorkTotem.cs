using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AgeOfJarls.Core;
using AgeOfJarls.Net;
using AgeOfJarls.Settlement;
using AgeOfJarls.Settlers;
using UnityEngine;

namespace AgeOfJarls.Work
{
    /// <summary>
    /// A Work Totem: one job, a work zone around it and the settlers assigned to it. The totem only holds its settings
    /// (stable id, radius, working hours) in its ZDO; a worker keeps the totem's id in its own ZDO, so assignments
    /// survive owner changes and world reloads. Settings change by RPC on the totem's owner, who checks the sender's
    /// role in the settlement the totem stands in.
    /// </summary>
    public class WorkTotem : MonoBehaviour, Hoverable, Interactable
    {
        private const string Module = "Work";
        internal const float MinRadius = 8f;
        internal const float MaxRadius = 30f;
        internal const float DefaultRadius = 15f;
        private const float MarkerSeconds = 0.5f;

        internal static readonly List<WorkTotem> Loaded = new List<WorkTotem>();

        /// <summary>Set on each totem prefab.</summary>
        public JobType m_job;

        /// <summary>Radius circle cloned from the ward; assigned when the prefab is built.</summary>
        public CircleProjector m_areaMarker;

        private ZNetView _nview;
        private Piece _piece;

        private enum TotemAction
        {
            Radius = 1,
            AllDay = 2,
            Priority = 3,
            Replant = 4,
            Upgrade = 5,
        }

        // ---------------------------------------------------------------- level

        internal const int MaxLevel = 3;
        /// <summary>Every level above the first: one more worker place and this much faster work.</summary>
        internal const float PacePerLevel = 0.15f;

        /// <summary>What level 2, then level 3 costs (item prefab, amount): early materials, then the bronze age.</summary>
        private static readonly (string item, int amount)[][] UpgradeCosts =
        {
            new[] { ("Wood", 20), ("Stone", 10), ("Resin", 5) },
            new[] { ("FineWood", 10), ("Bronze", 4) },
        };

        internal int Level => _nview != null && _nview.IsValid()
            ? Mathf.Clamp(_nview.GetZDO().GetInt(Keys.ZdoTotemLevel, 1), 1, MaxLevel)
            : 1;

        /// <summary>Multiplier for its workers' pace, live and in the catch-up.</summary>
        internal float PaceBonus => 1f + PacePerLevel * (Level - 1);

        /// <summary>The materials for <paramref name="level"/> (2 or 3); empty for any other level.</summary>
        internal static (string item, int amount)[] UpgradeCost(int level) =>
            level >= 2 && level <= MaxLevel ? UpgradeCosts[level - 2] : new (string, int)[0];

        // The player who paid for an upgrade here waits for the owner's answer; only then may a refund come in.
        private int _paidFromLevel;
        private float _paidUntil;
        private const float RefundWaitSeconds = 30f;

        /// <summary>A woodcutter totem whose workers replant the trees they fell.</summary>
        internal bool Replants => m_job == JobType.Woodcutter && _nview != null && _nview.IsValid() && _nview.GetZDO().GetBool(Keys.ZdoTotemReplant);

        /// <summary>Which totems free settlers go to first when the settlement assigns work by itself.</summary>
        internal const int LowPriority = 0;
        internal const int NormalPriority = 1;
        internal const int HighPriority = 2;

        internal JobType Job => m_job;

        internal int Priority => _nview != null && _nview.IsValid()
            ? Mathf.Clamp(_nview.GetZDO().GetInt(Keys.ZdoTotemPriority, NormalPriority), LowPriority, HighPriority)
            : NormalPriority;

        internal static string PriorityToken(int priority) =>
            priority == HighPriority ? "$aoj_priority_high" : priority == LowPriority ? "$aoj_priority_low" : "$aoj_priority_normal";

        internal long Id => _nview != null && _nview.IsValid() ? _nview.GetZDO().GetLong(Keys.ZdoTotemId) : 0L;

        internal float Radius => _nview != null && _nview.IsValid()
            ? Mathf.Clamp(_nview.GetZDO().GetFloat(Keys.ZdoTotemRadius, DefaultRadius), MinRadius, MaxRadius)
            : DefaultRadius;

        /// <summary>Work around the clock instead of by day only.</summary>
        internal bool AllDay => _nview != null && _nview.IsValid() && _nview.GetZDO().GetBool(Keys.ZdoTotemAllDay);

        /// <summary>Whether its workers should be at it now (world time, the same on every machine).</summary>
        internal bool IsWorkTime => AllDay || !EnvMan.IsNight();

        /// <summary>The settlement the totem stands in (its table loaded here), or null.</summary>
        internal JarlTable Settlement => JarlTable.FindContaining(transform.position);

        internal static WorkTotem FindById(long id)
        {
            if (id == 0L)
            {
                return null;
            }
            foreach (WorkTotem totem in Loaded)
            {
                if (totem.Id == id)
                {
                    return totem;
                }
            }
            return null;
        }

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            _piece = GetComponent<Piece>();
            SetMarkerRadius(DefaultRadius);
            if (_nview == null || !_nview.IsValid())
            {
                // Placement ghost: the circle shows the default zone while the player chooses the spot.
                return;
            }
            Loaded.Add(this);
            HideMarker();
            _nview.Register<ZPackage>(Keys.RpcTotemConfig, RPC_Config);
            _nview.Register<int>(Keys.RpcTotemRefund, RPC_Refund);
        }

        private void Start()
        {
            if (_nview != null && _nview.IsValid() && _nview.IsOwner() && _nview.GetZDO().GetLong(Keys.ZdoTotemId) == 0L)
            {
                _nview.GetZDO().Set(Keys.ZdoTotemId, Keys.NewId());
            }
        }

        private void OnDestroy()
        {
            Loaded.Remove(this);
        }

        // ---------------------------------------------------------------- workers

        /// <summary>Loaded settlers assigned here (others may be away; they come back to it).</summary>
        internal List<Settler> Workers()
        {
            long id = Id;
            return id == 0L ? new List<Settler>() : Settler.Loaded.Where(s => s.JobId == id).ToList();
        }

        /// <summary>How many loaded settlers work here, without building a list.</summary>
        internal int WorkerCount()
        {
            long id = Id;
            int count = 0;
            if (id != 0L)
            {
                foreach (Settler settler in Settler.Loaded)
                {
                    if (settler != null && settler.JobId == id)
                    {
                        count++;
                    }
                }
            }
            return count;
        }

        /// <summary>
        /// Workers the totem takes: more places as the settlement's tier rises, with every extra member and with every
        /// level of the totem.
        /// </summary>
        internal int Capacity
        {
            get
            {
                JarlTable table = Settlement;
                SettlementData data = table != null ? table.Data : null;
                int tier = data != null ? data.Tier : 0;
                return Mathf.Min(AoJConfig.TotemBaseSlots.Value + tier / 2, AoJConfig.TotemMaxSlots.Value) +
                       AoJConfig.TotemSlotsPerMember.Value * JarlTable.ExtraMembers(data) +
                       (JarlTable.HasUnlock(data, JarlTable.UnlockTotemSlot) ? 1 : 0) +
                       (Level - 1);
            }
        }

        /// <summary>A settler of the totem's settlement with no job, nearest first.</summary>
        internal Settler NearestFreeSettler()
        {
            JarlTable table = Settlement;
            long settlementId = table != null ? table.SettlementId : 0L;
            if (settlementId == 0L)
            {
                return null;
            }
            return Settler.Loaded
                .Where(s => s.Identity != null && s.HomeId == settlementId && s.JobId == 0L)
                .OrderBy(s => Vector3.Distance(s.transform.position, transform.position))
                .FirstOrDefault();
        }

        // ---------------------------------------------------------------- settings

        internal void RequestRadius(float radius) =>
            Send(TotemAction.Radius, p => p.Write(Mathf.Clamp(radius, MinRadius, MaxRadius)));

        internal void RequestAllDay(bool allDay) => Send(TotemAction.AllDay, p => p.Write(allDay));

        internal void RequestPriority(int priority) => Send(TotemAction.Priority, p => p.Write(Mathf.Clamp(priority, LowPriority, HighPriority)));

        internal void RequestReplant(bool replant) => Send(TotemAction.Replant, p => p.Write(replant));

        /// <summary>
        /// Pays for the next level from the player's inventory and asks the owner to raise it. Returns null when sent,
        /// else a localization token saying why not. The owner gives the materials back if it refuses (two players at
        /// once, rights changed meanwhile).
        /// </summary>
        internal string TryUpgrade(Player player)
        {
            int level = Level;
            if (level >= MaxLevel)
            {
                return "$aoj_totem_max_level";
            }
            if (!MayManage(player))
            {
                return Permissions.Denied(SettlementRight.Manage);
            }
            (string item, int amount)[] cost = UpgradeCost(level + 1);
            bool free = player.NoCostCheat();
            if (!free)
            {
                string missing = MissingText(player.GetInventory(), cost);
                if (missing.Length > 0)
                {
                    return "$aoj_msg_totem_missing " + missing;
                }
                foreach ((string item, int amount) in cost)
                {
                    player.GetInventory().RemoveItem(SharedName(item), amount);
                }
                _paidFromLevel = level;
                _paidUntil = Time.time + RefundWaitSeconds;
            }
            Send(TotemAction.Upgrade, p => p.Write(level));
            return null;
        }

        /// <summary>"20 $item_wood, 10 $item_stone" - the materials of a level, for buttons and messages.</summary>
        internal static string CostText((string item, int amount)[] cost) =>
            string.Join(", ", cost.Select(c => $"{c.amount} {SharedName(c.item)}"));

        private static string MissingText(Inventory inventory, (string item, int amount)[] cost) =>
            string.Join(", ", cost.Where(c => inventory.CountItems(SharedName(c.item)) < c.amount)
                .Select(c => $"{c.amount - inventory.CountItems(SharedName(c.item))} {SharedName(c.item)}"));

        private static string SharedName(string prefab)
        {
            GameObject item = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefab) : null;
            ItemDrop drop = item != null ? item.GetComponent<ItemDrop>() : null;
            return drop != null ? drop.m_itemData.m_shared.m_name : prefab;
        }

        // On the player who paid, if the owner refused the upgrade it asked for: the materials come back.
        private void RPC_Refund(long sender, int fromLevel)
        {
            Player player = Player.m_localPlayer;
            if (player == null || fromLevel != _paidFromLevel || Time.time > _paidUntil)
            {
                return;
            }
            _paidUntil = 0f;
            foreach ((string item, int amount) in UpgradeCost(fromLevel + 1))
            {
                player.GetInventory().AddItem(item, amount, 1, 0, 0L, "", false);
            }
            player.Message(MessageHud.MessageType.Center, Localize("$aoj_msg_totem_refund"));
        }

        private void Send(TotemAction action, Action<ZPackage> write)
        {
            var package = new ZPackage();
            package.Write((int)action);
            write(package);
            _nview.InvokeRPC(Keys.RpcTotemConfig, package);
        }

        /// <summary>Hersirs and Jarls of the settlement it stands in; outside any settlement, its builder.</summary>
        internal bool MayManage(Player player)
        {
            if (player == null)
            {
                return false;
            }
            JarlTable table = Settlement;
            if (table != null && table.Data != null)
            {
                return table.RoleOf(player).Allows(SettlementRight.Manage);
            }
            return _piece != null && _piece.GetCreator() == player.GetPlayerID();
        }

        private void RPC_Config(long sender, ZPackage package)
        {
            if (!_nview.IsOwner())
            {
                _nview.InvokeRPC(Keys.RpcTotemConfig, package);
                return;
            }
            try
            {
                var action = (TotemAction)package.ReadInt();
                if (!MayManage(Peers.FindPlayer(sender)))
                {
                    Log.Warning(Module, $"Totem settings from peer {sender} refused: not the Jarl or a Hersir here");
                    if (action == TotemAction.Upgrade)
                    {
                        _nview.InvokeRPC(sender, Keys.RpcTotemRefund, package.ReadInt());
                    }
                    return;
                }
                ZDO zdo = _nview.GetZDO();
                switch (action)
                {
                    case TotemAction.Upgrade:
                        // Raised from the level the player saw and paid for; anything else (two players at once) is refused.
                        int from = package.ReadInt();
                        if (from == Level && from < MaxLevel)
                        {
                            zdo.Set(Keys.ZdoTotemLevel, from + 1);
                        }
                        else
                        {
                            _nview.InvokeRPC(sender, Keys.RpcTotemRefund, from);
                        }
                        break;
                    case TotemAction.Radius:
                        zdo.Set(Keys.ZdoTotemRadius, Mathf.Clamp(package.ReadSingle(), MinRadius, MaxRadius));
                        break;
                    case TotemAction.AllDay:
                        zdo.Set(Keys.ZdoTotemAllDay, package.ReadBool());
                        break;
                    case TotemAction.Priority:
                        zdo.Set(Keys.ZdoTotemPriority, Mathf.Clamp(package.ReadInt(), LowPriority, HighPriority));
                        break;
                    case TotemAction.Replant:
                        zdo.Set(Keys.ZdoTotemReplant, package.ReadBool());
                        break;
                    default:
                        Log.Warning(Module, $"Unknown totem action {(int)action} from peer {sender}");
                        break;
                }
            }
            catch (Exception e) when (e is IOException || e is ArgumentException)
            {
                Log.Error(Module, $"Malformed totem settings from peer {sender}: {e.Message}");
            }
        }

        // ---------------------------------------------------------------- hover, interaction, marker

        public string GetHoverText()
        {
            ShowMarker();
            if (_nview == null || !_nview.IsValid())
            {
                return Localize(JobInfo.Token(m_job));
            }
            List<Settler> workers = Workers();
            var text = new StringBuilder();
            text.Append("<b>$aoj_totem: ").Append(JobInfo.Token(m_job)).Append("</b> · $aoj_totem_level ").Append(Level).Append('/').Append(MaxLevel).Append('\n');
            text.Append("$aoj_workers: ").Append(workers.Count == 0 ? "-" : string.Join(", ", workers.Select(w => w.DisplayName)))
                .Append($" ({workers.Count}/{Capacity})\n");
            text.Append("$aoj_radius ").Append(Mathf.RoundToInt(Radius)).Append(" m · ").Append(AllDay ? "$aoj_hours_allday" : "$aoj_hours_day")
                .Append(" · $aoj_priority: ").Append(PriorityToken(Priority));
            ToolKind tool = JobInfo.RequiredTool(m_job);
            if (tool != ToolKind.None)
            {
                text.Append(" · $aoj_needs ").Append(JobInfo.ToolToken(tool));
            }
            string problem = workers.Select(w => w.JobProblem).FirstOrDefault(p => p.Length > 0);
            if (!string.IsNullOrEmpty(problem))
            {
                text.Append("\n<color=#ff9060>").Append(problem).Append("</color>");
            }
            if (Settlement == null)
            {
                text.Append("\n<color=#ff9060>$aoj_totem_outside</color>");
            }
            text.Append("\n[<color=yellow><b>$KEY_Use</b></color>] $aoj_manage_totem");
            return Localize(text.ToString());
        }

        public string GetHoverName() => Localize(JobInfo.Token(m_job));

        public float GetHoverOffset() => 0f;

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || !(user is Player) || _nview == null || !_nview.IsValid())
            {
                return false;
            }
            UI.TotemWindow.Open(this);
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        internal void ShowMarker()
        {
            if (m_areaMarker == null)
            {
                return;
            }
            SetMarkerRadius(Radius);
            m_areaMarker.gameObject.SetActive(true);
            CancelInvoke(nameof(HideMarker));
            Invoke(nameof(HideMarker), MarkerSeconds);
        }

        private void HideMarker()
        {
            if (m_areaMarker != null)
            {
                m_areaMarker.gameObject.SetActive(false);
            }
        }

        private void SetMarkerRadius(float radius)
        {
            if (m_areaMarker != null)
            {
                m_areaMarker.m_radius = radius;
                m_areaMarker.m_nrOfSegments = Mathf.Max(24, Mathf.RoundToInt(radius * 4f));
            }
        }

        private static string Localize(string text) => Localization.instance != null ? Localization.instance.Localize(text) : text;
    }
}
