using System.Collections.Generic;
using AgeOfJarls.Core;
using AgeOfJarls.Settlers;
using AgeOfJarls.Work;
using UnityEngine;

namespace AgeOfJarls.AI.Jobs
{
    /// <summary>Everything a job needs from its settler.</summary>
    internal sealed class JobContext
    {
        internal SettlerAI Ai;
        internal Settler Settler;
        internal SettlerCharacter Body;
        internal PathMover Mover;
        internal LootCollector Loot;
    }

    /// <summary>
    /// A job run by a settler's owner while it is at home and it is working time. Each frame it either works (walks,
    /// swings, loads...) and returns true, or has nothing to do right now and returns false, so the settler stores
    /// what it carries or idles. Problems ("no axe", "no trees") go to the settler's ZDO for everyone's hover.
    /// Work pace comes from morale and hunger (<see cref="Needs"/>) and the settler's experience at the job.
    /// </summary>
    internal abstract class JobBase
    {
        protected const string Module = "Work";
        private const float StatsFlushSeconds = 10f;
        private const float SkillPerItem = 0.15f;

        protected readonly JobContext Ctx;
        protected WorkTotem Totem;
        private float _unsavedWork;
        private float _unsavedOutput;
        private float _flushTimer;

        protected JobBase(JobContext context)
        {
            Ctx = context;
        }

        internal abstract JobType Job { get; }

        protected Settler Settler => Ctx.Settler;

        protected SettlerCharacter Body => Ctx.Body;

        protected Vector3 Position => Ctx.Ai.transform.position;

        protected long Uid => Ctx.Settler.Uid;

        /// <summary>One frame of work at this totem; false = nothing to do right now.</summary>
        internal bool Update(float dt, WorkTotem totem)
        {
            Totem = totem;
            bool working = Work(dt);
            if (working)
            {
                // Time at the totem's pace: the measured rate stays the base one, the catch-up applies the level itself.
                _unsavedWork += dt * TotemPace;
            }
            _flushTimer -= dt;
            if (_flushTimer <= 0f)
            {
                _flushTimer = StatsFlushSeconds;
                FlushStats();
            }
            return working;
        }

        protected abstract bool Work(float dt);

        /// <summary>What the worker keeps in its bag instead of storing: its tool, and supplies it works with.</summary>
        internal virtual bool Keeps(ItemDrop.ItemData item) => JobInfo.Fits(item, JobInfo.RequiredTool(Job));

        /// <summary>For aoj_debug: what the job is working on right now.</summary>
        internal virtual string DebugTarget => "";

        protected string Describe(Component target) =>
            target == null ? "" : $"{Utils.GetPrefabName(target.gameObject)} {Vector3.Distance(Position, target.transform.position):0} m";

        /// <summary>Leaving the job (orders, night, a fight): drop reservations.</summary>
        internal virtual void Stop()
        {
            FlushStats();
        }

        /// <summary>
        /// x0.6 - x1.25 from morale and hunger, up to x1.5 more from experience, the traits (Diligent, Lazy) and the
        /// totem's level.
        /// </summary>
        protected float Pace
        {
            get
            {
                ZDO zdo = Settler.Zdo;
                float traits = Mathf.Max(0.1f, 1f + Settler.TraitSum(Core.Defs.TraitStat.WorkSpeed)) * TotemPace;
                return zdo == null ? traits : Needs.WorkPace(zdo) * (1f + Skill / 200f) * traits;
            }
        }

        private float TotemPace => Totem != null ? Totem.PaceBonus : 1f;

        protected float Skill => Settler.Zdo?.GetFloat(Keys.SkillPrefix + Job.ToString().ToLowerInvariant()) ?? 0f;

        protected void Problem(string token) => Settler.SetJobProblem(token);

        protected bool InZone(Vector3 point) => Utils.DistanceXZ(point, Totem.transform.position) <= Totem.Radius;

        /// <summary>Items produced: they count for the settler's live pace and experience.</summary>
        protected void Produced(int items)
        {
            if (items > 0)
            {
                _unsavedOutput += items;
            }
        }

        /// <summary>The best tool of the kind, in hand; null (and a problem) when the settler has none.</summary>
        protected ItemDrop.ItemData TakeTool(ToolKind kind)
        {
            ItemDrop.ItemData tool = JobInfo.BestTool(Body.GetInventory(), kind);
            if (tool == null)
            {
                Problem("$aoj_problem_no_" + kind.ToString().ToLowerInvariant());
                return null;
            }
            if (!tool.m_equipped && kind != ToolKind.Hammer)
            {
                Body.EquipItem(tool, triggerEquipEffects: false);
                Settler.MarkInventoryDirty();
            }
            return tool;
        }

        /// <summary>Faces the point and stops (to repair, feed a station...).</summary>
        protected void Face(Vector3 point)
        {
            Ctx.Ai.StopMoving();
            Vector3 direction = point - Position;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.01f)
            {
                Ctx.Ai.LookTowards(direction.normalized);
            }
        }

        /// <summary>
        /// Stops and looks right at the point, down at a log on the ground too: a swing goes along the look, the way
        /// vanilla aims at a structure it attacks. Level (<see cref="Face"/>) it would pass over a log and hit the ground.
        /// </summary>
        protected void Aim(Vector3 point)
        {
            Ctx.Ai.StopMoving();
            Ctx.Ai.AimAt(point);
        }

        // ---------------------------------------------------------------- shared steps

        internal enum Step
        {
            Busy,
            Done,
            Failed,
        }

        private const float WalkGiveUpSeconds = 40f;
        private const float ChestsRefreshSeconds = 5f;
        private const float AskSeconds = 1f;
        private const float AccessGiveUpSeconds = 5f;

        private Vector3 _walkGoal;
        private float _walkTimer;
        private readonly List<Container> _chests = new List<Container>();
        private float _chestsTimer;
        private const float FetchRetrySeconds = 10f;

        private Container _fetchChest;
        private float _accessTimer;
        private float _askTimer;
        private float _fetchRetryAt;

        /// <summary>Walks until within reach, then stands; Failed when there is no way or it takes too long.</summary>
        protected Step WalkTo(float dt, Vector3 point, float reach)
        {
            if (Vector3.Distance(Position, point) <= reach)
            {
                _walkTimer = 0f;
                // There, and maybe waiting (for a chest's owner, a station): vanilla idle movement must not stroll off
                // meanwhile - out at a far totem it would head back home.
                Ctx.Ai.StopMoving();
                return Step.Done;
            }
            if ((point - _walkGoal).sqrMagnitude > 1f)
            {
                _walkGoal = point;
                _walkTimer = 0f;
                Ctx.Mover.Reset();
            }
            _walkTimer += dt;
            MoveResult move = Ctx.Mover.MoveTo(dt, point, reach * 0.7f, reach, run: false);
            if (move == MoveResult.Arrived)
            {
                return Vector3.Distance(Position, point) <= reach + 0.5f ? Step.Done : Step.Failed;
            }
            if (move == MoveResult.Blocked || _walkTimer > WalkGiveUpSeconds)
            {
                _walkTimer = 0f;
                return Step.Failed;
            }
            return Step.Busy;
        }

        /// <summary>The settlement's chests (refreshed every few seconds), for fetching supplies.</summary>
        protected List<Container> SettlementChests()
        {
            _chestsTimer -= Time.deltaTime;
            if (_chestsTimer <= 0f)
            {
                _chestsTimer = ChestsRefreshSeconds;
                Settlement.JarlTable table = Settler.HomeTable;
                if (table != null && table.Data != null)
                {
                    Settlement.SettlementStorage.CollectChests(table.transform.position, Settlement.JarlTable.RadiusOf(table.Data), _chests);
                }
                else
                {
                    _chests.Clear();
                }
            }
            return _chests;
        }

        /// <summary>
        /// Gets wanted supplies from the settlement's chests (the nearest chest holding some): Done once something was
        /// taken, Failed when no chest has any.
        /// </summary>
        protected Step Fetch(float dt, System.Predicate<ItemDrop.ItemData> wanted, int maxItems)
        {
            if (_fetchChest == null || !Settlement.SettlementStorage.IsUsable(_fetchChest))
            {
                // After an empty search the chests are not searched again for a while.
                if (Time.time < _fetchRetryAt)
                {
                    return Step.Failed;
                }
                _fetchChest = Settlement.SettlementStorage.FindHolding(SettlementChests(), wanted, Position);
                _accessTimer = 0f;
                if (_fetchChest == null)
                {
                    _fetchRetryAt = Time.time + FetchRetrySeconds;
                    return Step.Failed;
                }
            }
            Step walk = WalkTo(dt, _fetchChest.transform.position, 2.5f);
            if (walk != Step.Done)
            {
                if (walk == Step.Failed)
                {
                    _fetchChest = null;
                }
                return walk;
            }
            _askTimer -= dt;
            bool ask = _askTimer <= 0f;
            if (ask)
            {
                _askTimer = AskSeconds;
            }
            if (!Settlement.ChestAccess.Acquire(_fetchChest, ask))
            {
                _accessTimer += dt;
                if (_accessTimer > AccessGiveUpSeconds)
                {
                    _fetchChest = null;
                    return Step.Failed;
                }
                return Step.Busy;
            }
            int taken = Settlement.SettlementStorage.TakeFrom(_fetchChest, Body.GetInventory(), wanted, maxItems);
            _fetchChest = null;
            if (taken > 0)
            {
                Settler.FlushInventory();
            }
            return taken > 0 ? Step.Done : Step.Failed;
        }

        private const float StationsRefreshSeconds = 2f;
        private System.Collections.IList _stations;
        private float _stationsTimer;

        /// <summary>The job's stations in the zone (furnaces, cooking stations...), refreshed every couple of seconds.</summary>
        protected List<T> Stations<T>(float dt) where T : Component
        {
            _stationsTimer -= dt;
            if (_stations is List<T> cached && _stationsTimer > 0f)
            {
                cached.RemoveAll(IsGone);
                return cached;
            }
            _stationsTimer = StationsRefreshSeconds;
            var list = new List<T>();
            foreach (Component component in WorkScanner.Find<T>(Totem))
            {
                if (component is T station && station != null)
                {
                    list.Add(station);
                }
            }
            _stations = list;
            return list;
        }

        private static bool IsGone<T>(T component) where T : Component => component == null;

        /// <summary>Removes one item of this name from the settler's bag (after handing it to a station).</summary>
        protected bool Consume(string itemName)
        {
            Inventory bag = Body.GetInventory();
            ItemDrop.ItemData item = bag.GetAllItems().Find(i => i.m_shared.m_name == itemName && !i.m_equipped);
            if (item == null)
            {
                return false;
            }
            bag.RemoveItem(item, 1);
            return true;
        }

        protected static string PrefabName(ItemDrop.ItemData item) =>
            item.m_dropPrefab != null ? item.m_dropPrefab.name : Utils.GetPrefabName(item.m_shared.m_name);

        private void FlushStats()
        {
            ZDO zdo = Settler.Zdo;
            if (zdo == null || !Settler.GetComponent<ZNetView>().IsOwner() || (_unsavedWork <= 0f && _unsavedOutput <= 0f))
            {
                return;
            }
            zdo.Set(Keys.ZdoSettlerWorkTime, zdo.GetFloat(Keys.ZdoSettlerWorkTime) + _unsavedWork);
            if (_unsavedOutput > 0f)
            {
                zdo.Set(Keys.ZdoSettlerWorkOutput, zdo.GetFloat(Keys.ZdoSettlerWorkOutput) + _unsavedOutput);
                string skillKey = Keys.SkillPrefix + Job.ToString().ToLowerInvariant();
                float skill = zdo.GetFloat(skillKey);
                // Diminishing returns: fast at first, slow near the top.
                zdo.Set(skillKey, Mathf.Min(100f, skill + _unsavedOutput * SkillPerItem * (1f - skill / 110f)));
            }
            _unsavedWork = 0f;
            _unsavedOutput = 0f;
        }
    }
}
