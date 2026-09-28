using System.Collections.Generic;
using AgeOfJarls.Work;
using UnityEngine;

namespace AgeOfJarls.AI.Jobs
{
    /// <summary>
    /// Woodcutter: fells trees in the zone with its axe, chops the fallen logs, clears stumps and small trees and gathers
    /// the wood; the home routine carries the load to the sorted chests. Swings are real attacks with the axe in hand, so
    /// the game's own rules apply (tool tier against the tree, damage, effects, drops). A felled tree is finished - log,
    /// halves, stump - before the next one falls, so nothing is left lying.
    /// At a totem set to replant, every tree it fells is followed by a sapling of the same kind a few metres away,
    /// from the seeds in the chests (pine cones, beech seeds...), so the forest grows back.
    /// </summary>
    internal sealed class WoodcutterJob : HarvestJob
    {
        /// <summary>Logs and wood first: a felled tree is replanted a little later.</summary>
        private const float PlantDelay = 20f;
        private const float PlantReach = 2f;
        private const float SpotDistance = 3f;
        private const float SpotClearance = 1.2f;
        private const int MaxQueued = 8;
        private const int SeedBatch = 5;

        private static Dictionary<string, (GameObject sapling, string seed)> s_saplings;
        private static readonly Collider[] s_hits = new Collider[8];
        private static int s_blockMask;

        private readonly List<(Vector3 stump, GameObject sapling, string seed, float readyAt)> _toPlant =
            new List<(Vector3, GameObject, string, float)>();
        private Vector3? _spot;

        internal WoodcutterJob(JobContext context) : base(context)
        {
        }

        internal override JobType Job => JobType.Woodcutter;

        protected override ToolKind Tool => ToolKind.Axe;

        protected override string NothingToDo => "$aoj_problem_no_trees";

        protected override void OnBroughtDown(string prefab, Vector3 position)
        {
            if (Totem != null && Totem.Replants && _toPlant.Count < MaxQueued && Saplings().TryGetValue(prefab, out (GameObject sapling, string seed) kind))
            {
                _toPlant.Add((position, kind.sapling, kind.seed, Time.time + PlantDelay));
            }
        }

        protected override bool Work(float dt)
        {
            // Never mid-swing: the swing is finished (see HarvestJob) before anything else.
            if (_toPlant.Count > 0 && Time.time >= _toPlant[0].readyAt && Totem.Replants && !Body.InAttack() && Plant(dt))
            {
                return true;
            }
            return base.Work(dt);
        }

        internal override void Stop()
        {
            _spot = null;
            base.Stop();
        }

        private bool Plant(float dt)
        {
            (Vector3 stump, GameObject sapling, string seed, float readyAt) next = _toPlant[0];
            Inventory bag = Body.GetInventory();
            if (!bag.GetAllItems().Exists(i => PrefabName(i) == next.seed))
            {
                Step fetch = Fetch(dt, i => PrefabName(i) == next.seed, SeedBatch);
                if (fetch == Step.Failed)
                {
                    // No seeds of this kind anywhere: the tree is not replanted.
                    Problem("$aoj_problem_no_tree_seeds");
                    Done();
                    return false;
                }
                return true;
            }
            if (_spot == null)
            {
                _spot = FreeSpot(next.stump);
                if (_spot == null)
                {
                    Done();
                    return false;
                }
            }
            Step walk = WalkTo(dt, _spot.Value, PlantReach);
            if (walk != Step.Done)
            {
                if (walk == Step.Failed)
                {
                    Done();
                }
                return walk == Step.Busy;
            }
            Face(_spot.Value);
            ItemDrop.ItemData seed = bag.GetAllItems().Find(i => PrefabName(i) == next.seed);
            if (seed != null)
            {
                Object.Instantiate(next.sapling, _spot.Value, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                bag.RemoveItem(seed, 1);
                Settler.FlushInventory();
            }
            Done();
            return true;
        }

        private void Done()
        {
            _toPlant.RemoveAt(0);
            _spot = null;
        }

        // A few metres from the stump, inside the zone, on dry ground with nothing solid in the way.
        private Vector3? FreeSpot(Vector3 stump)
        {
            if (s_blockMask == 0)
            {
                s_blockMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid");
            }
            float start = Random.Range(0f, 360f);
            for (int i = 0; i < 8; i++)
            {
                Vector3 spot = stump + Quaternion.Euler(0f, start + i * 45f, 0f) * Vector3.forward * SpotDistance;
                if (!InZone(spot) || ZoneSystem.instance == null)
                {
                    continue;
                }
                spot.y = ZoneSystem.instance.GetGroundHeight(spot);
                if (spot.y < ZoneSystem.instance.m_waterLevel + 0.2f)
                {
                    continue;
                }
                if (Physics.OverlapSphereNonAlloc(spot + Vector3.up * (SpotClearance + 0.2f), SpotClearance, s_hits, s_blockMask) == 0)
                {
                    return spot;
                }
            }
            return null;
        }

        // Grown tree prefab name -> (the sapling that grows into it, the seed it is planted from).
        private static Dictionary<string, (GameObject sapling, string seed)> Saplings()
        {
            if (s_saplings != null || ZNetScene.instance == null)
            {
                return s_saplings ?? new Dictionary<string, (GameObject, string)>();
            }
            s_saplings = new Dictionary<string, (GameObject, string)>();
            foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
            {
                Plant plant = prefab != null ? prefab.GetComponent<Plant>() : null;
                Piece piece = prefab != null ? prefab.GetComponent<Piece>() : null;
                if (plant == null || plant.m_grownPrefabs == null || piece == null || piece.m_resources == null ||
                    piece.m_resources.Length == 0 || piece.m_resources[0].m_resItem == null)
                {
                    continue;
                }
                string seed = piece.m_resources[0].m_resItem.gameObject.name;
                foreach (GameObject grown in plant.m_grownPrefabs)
                {
                    if (grown != null && grown.GetComponent<TreeBase>() != null && !s_saplings.ContainsKey(grown.name))
                    {
                        s_saplings[grown.name] = (prefab, seed);
                    }
                }
            }
            return s_saplings;
        }

        protected override IEnumerable<Component> Candidates()
        {
            // Fallen logs, also those a tree felled at the edge threw past it.
            foreach (Component log in WorkScanner.Find<TreeLog>(Totem, WorkTotem.Overreach))
            {
                yield return log;
            }
            foreach (Component tree in WorkScanner.Find<TreeBase>(Totem))
            {
                yield return tree;
            }
            // Stumps and small trees are no TreeBase but tree-like Destructibles an axe clears - never a sapling or
            // anything else a player planted or built.
            foreach (Component small in WorkScanner.Find<Destructible>(Totem))
            {
                if (small is Destructible d && d.m_destructibleType == DestructibleType.Tree &&
                    d.m_damages.m_chop != HitData.DamageModifier.Immune && d.GetComponent<Plant>() == null && d.GetComponent<Piece>() == null)
                {
                    yield return small;
                }
            }
        }

        // A felled tree is finished before the next one falls: its log, then the halves, the wood gathered, then its
        // stump (and small trees) - nothing is left lying about.
        protected override int Rank(Component target) => target is TreeLog ? 0 : target is Destructible ? 1 : 2;

        protected override float Margin(Component target) => target is TreeLog ? WorkTotem.Overreach : 0f;

        protected override int MinToolTier(Component target)
        {
            switch (target)
            {
                case TreeLog log:
                    return log.m_minToolTier;
                case TreeBase tree:
                    return tree.m_minToolTier;
                case Destructible small:
                    return small.m_minToolTier;
                default:
                    return int.MaxValue;
            }
        }
    }

    /// <summary>
    /// Miner: breaks rocks and ore deposits in the zone with its pickaxe (respecting the pickaxe's tier) and gathers
    /// the stone and ore.
    /// </summary>
    internal sealed class MinerJob : HarvestJob
    {
        internal MinerJob(JobContext context) : base(context)
        {
        }

        internal override JobType Job => JobType.Miner;

        protected override ToolKind Tool => ToolKind.Pickaxe;

        protected override string NothingToDo => "$aoj_problem_no_rocks";

        protected override IEnumerable<Component> Candidates()
        {
            foreach (Component rock in WorkScanner.Find<MineRock5>(Totem))
            {
                yield return rock;
            }
            foreach (Component rock in WorkScanner.Find<MineRock>(Totem))
            {
                yield return rock;
            }
            foreach (Component destructible in WorkScanner.Find<Destructible>(Totem))
            {
                // Plain Destructibles are also bushes, stumps and pots: only the ones a pickaxe is for.
                if (destructible is Destructible d && d.m_damages.m_pickaxe != HitData.DamageModifier.Immune &&
                    d.m_damages.m_chop == HitData.DamageModifier.Immune)
                {
                    yield return destructible;
                }
            }
        }

        protected override int MinToolTier(Component target)
        {
            switch (target)
            {
                case MineRock5 rock5:
                    return rock5.m_minToolTier;
                case MineRock rock:
                    return rock.m_minToolTier;
                case Destructible destructible:
                    return destructible.m_minToolTier;
                default:
                    return int.MaxValue;
            }
        }
    }

    /// <summary>
    /// Shared by the woodcutter and the miner: gather what lies in the zone, otherwise pick the next object its tool
    /// can break - the most urgent kind first (a felled tree's log before a new tree), then the nearest; reserved, so
    /// two workers never share one - walk up to it and swing with the tool in hand, aimed at it. What it brings down
    /// (a log, split halves) is worked next; a target its blows never reach is given up.
    /// </summary>
    internal abstract class HarvestJob : JobBase
    {
        private const float StrikeDistance = 1.2f;
        /// <summary>Where the way ends short of the strike distance, a blow still reaches this much farther.</summary>
        private const float ReachSlack = 0.6f;
        /// <summary>About where a swing starts: blows are aimed from this height - down at a log, level at a trunk.</summary>
        private const float ChestHeight = 1f;
        private const float BaseSwingSeconds = 1.5f;
        private const float GiveUpSeconds = 40f;
        private const float IgnoreSeconds = 120f;
        private const float ScanSeconds = 3f;
        /// <summary>
        /// After bringing something down it watches it fall, counted from its last swing: the blow lands, the tree falls,
        /// its log comes to rest. (Gathering the drops first may well take longer.)
        /// </summary>
        private const float SettleSeconds = 2.5f;
        /// <summary>Swings in a row that leave no mark on the target: it is out of reach (on a roof, behind something).</summary>
        private const int MissesBeforeGivingUp = 3;
        private const int SlotsToKeepFree = 1;
        private const float FacingDegrees = 20f;

        private readonly Dictionary<int, float> _ignored = new Dictionary<int, float>();
        private Component _target;
        private Collider[] _targetColliders = new Collider[0];
        private ZNetView _targetView;
        private float _scanTimer;
        private float _swingTimer;
        private float _tripTimer;
        private float _swungAt;
        private float _settleUntil;

        // What the current target was, to tell when the swings brought it down (it is destroyed, not just released).
        private string _targetPrefab;
        private Vector3 _targetPosition;
        private bool _struck;
        // Its ZDO's data revision at the last swing: every hit writes the target's health, so an unchanged one is a miss.
        private uint _targetRevision;
        private int _misses;

        /// <summary>The target this worker struck is gone: felled, broken or split.</summary>
        protected virtual void OnBroughtDown(string prefab, Vector3 position)
        {
        }

        protected HarvestJob(JobContext context) : base(context)
        {
        }

        internal override string DebugTarget => Describe(_target);

        protected abstract ToolKind Tool { get; }

        protected abstract string NothingToDo { get; }

        protected abstract IEnumerable<Component> Candidates();

        protected abstract int MinToolTier(Component target);

        /// <summary>Which kind of target comes first, lowest first; the nearest among equals.</summary>
        protected virtual int Rank(Component target) => 0;

        /// <summary>How far past the zone's edge such a target is still worked (see <see cref="WorkTotem.Overreach"/>).</summary>
        protected virtual float Margin(Component target) => 0f;

        protected override bool Work(float dt)
        {
            if (Body.InAttack())
            {
                // Mid-swing it holds still, aimed where the blow lands: turning now - to a drop, to the next target -
                // would send the blade into whatever lies that way. A target felled by this very blow is let go after.
                // The pause between swings runs on meanwhile: it is counted from the start of a swing.
                _swingTimer -= dt;
                if (_target != null)
                {
                    Aim(StrikePoint());
                }
                else
                {
                    Ctx.Ai.StopMoving();
                }
                return true;
            }
            ItemDrop.ItemData tool = TakeTool(Tool);
            if (tool == null)
            {
                Release();
                return false;
            }
            if (Body.GetInventory().GetEmptySlots() <= SlotsToKeepFree)
            {
                // A full bag goes to the chests first (the home routine does that when the job pauses).
                Release();
                Problem("");
                return false;
            }

            // What the last swings dropped (wood, stone, ore, resin, seeds...) comes first - also where a tree felled at
            // the edge took the work past it.
            if (Ctx.Loot.CollectInZone(dt, Totem.transform.position, Totem.Radius + WorkTotem.Overreach, item => true))
            {
                Produced(Ctx.Loot.TakePicked());
                Problem("");
                return true;
            }
            Produced(Ctx.Loot.TakePicked());

            if (!IsWorkable(_target, tool))
            {
                if (_target == null && _struck && _targetPrefab != null)
                {
                    // Unity's null: the object this worker was striking has been destroyed - felled, broken or split.
                    OnBroughtDown(_targetPrefab, _targetPosition);
                    if (AiTrace.On)
                    {
                        AiTrace.Write(Ctx.Ai, $"brought down {_targetPrefab}");
                    }
                    // What it turned into (a log, two halves) is worked next: looked for afresh once it lies there.
                    WorkScanner.Forget(Totem);
                    _settleUntil = _swungAt + SettleSeconds;
                }
                Release();
                if (Time.time < _settleUntil)
                {
                    Ctx.Ai.StopMoving();
                    return true;
                }
                // The next target is looked for at once when the last one is done - a pause here would drop the worker
                // into its chores between two trees. Only a scan that found nothing waits before the next.
                _scanTimer -= dt;
                if (_scanTimer > 0f)
                {
                    return false;
                }
                _target = FindTarget(tool);
                _tripTimer = 0f;
                _targetColliders = WorkScanner.SolidColliders(_target);
                if (_target == null)
                {
                    _scanTimer = ScanSeconds;
                    Problem(_toolTooWeak ? "$aoj_problem_tool_too_weak" : NothingToDo);
                    return false;
                }
                _targetPrefab = Utils.GetPrefabName(_target.gameObject);
                _targetPosition = _target.transform.position;
                _targetView = _target.GetComponent<ZNetView>();
                _misses = 0;
                Ctx.Mover.Reset();
            }
            Problem("");

            Vector3 point = StrikePoint();
            float distance = Utils.DistanceXZ(point, Position);
            if (distance > StrikeDistance)
            {
                _tripTimer += dt;
                // Walked to below the aim point, at the target's own height: the path measures its ends in 3D.
                Vector3 ground = new Vector3(point.x, _target.transform.position.y, point.z);
                MoveResult move = Ctx.Mover.MoveTo(dt, ground, StrikeDistance * 0.8f, StrikeDistance + ReachSlack, run: false);
                if (move == MoveResult.Moving && _tripTimer <= GiveUpSeconds)
                {
                    return true;
                }
                if (move != MoveResult.Arrived || distance > StrikeDistance + ReachSlack)
                {
                    // No way there, too long, or the way ends out of reach: another target.
                    Ignore(_target);
                    Release();
                    return true;
                }
                // As close as the ground allows (a log lying against something) and within reach: it swings from here.
            }

            Reservations.Take(_target, Uid);
            Aim(point);
            _swingTimer -= dt;
            // The swing goes where the settler faces: wait until it has turned to the target.
            Vector3 toTarget = point - Position;
            toTarget.y = 0f;
            bool facing = toTarget.sqrMagnitude < 0.01f || Ctx.Ai.IsLookingTowards(toTarget.normalized, FacingDegrees);
            if (!facing || _swingTimer > 0f)
            {
                return true;
            }
            // Every hit writes the target's health into its ZDO: a swing that left no mark missed. One whose blows keep
            // missing is out of reach (on a roof, behind a wall) and is given up for another, not swung at forever.
            uint revision = Revision();
            bool landed = !_struck || _targetView == null || revision != _targetRevision;
            if (!landed && _misses + 1 >= MissesBeforeGivingUp)
            {
                if (AiTrace.On)
                {
                    AiTrace.Write(Ctx.Ai, $"its blows do not reach {Describe(_target)}: gives it up");
                }
                Ignore(_target);
                Release();
                return true;
            }
            if (Body.StartAttack(null, false))
            {
                _misses = landed ? 0 : _misses + 1;
                _targetRevision = revision;
                _swungAt = Time.time;
                _swingTimer = BaseSwingSeconds / Mathf.Max(0.1f, Pace);
                _struck = true;
                if (AiTrace.On)
                {
                    AiTrace.Write(Ctx.Ai, $"swings at {Describe(_target)} (strike point {Utils.DistanceXZ(point, Position):0.0} m, " +
                                          $"{point.y - Position.y:0.0} m up{(landed ? "" : $", {_misses} miss(es)")})");
                }
            }
            return true;
        }

        internal override void Stop()
        {
            Release();
            base.Stop();
        }

        /// <summary>Where on the target to aim, seen from about where a swing starts.</summary>
        private Vector3 StrikePoint() => WorkScanner.StrikePoint(_target, _targetColliders, Position + Vector3.up * ChestHeight);

        private uint Revision() => _targetView != null && _targetView.IsValid() ? _targetView.GetZDO().DataRevision : 0u;

        private bool IsWorkable(Component target, ItemDrop.ItemData tool) =>
            target != null && MinToolTier(target) <= tool.m_shared.m_toolTier && Reservations.IsFree(target, Uid) &&
            InReach(target);

        private bool InReach(Component target) =>
            Utils.DistanceXZ(target.transform.position, Totem.transform.position) <= Totem.Radius + Margin(target);

        /// <summary>At the last scan the zone held work, but only for a better tool (birch for a stone axe...).</summary>
        private bool _toolTooWeak;

        private Component FindTarget(ItemDrop.ItemData tool)
        {
            Component best = null;
            int bestRank = int.MaxValue;
            float bestSqr = float.MaxValue;
            float now = Time.time;
            _toolTooWeak = false;
            foreach (Component candidate in Candidates())
            {
                if (candidate == null || (_ignored.TryGetValue(candidate.GetInstanceID(), out float until) && now < until))
                {
                    continue;
                }
                if (!IsWorkable(candidate, tool))
                {
                    _toolTooWeak |= MinToolTier(candidate) > tool.m_shared.m_toolTier && InReach(candidate);
                    continue;
                }
                int rank = Rank(candidate);
                float sqr = (candidate.transform.position - Position).sqrMagnitude;
                if (rank < bestRank || (rank == bestRank && sqr < bestSqr))
                {
                    best = candidate;
                    bestRank = rank;
                    bestSqr = sqr;
                }
            }
            if (_ignored.Count > 64)
            {
                _ignored.Clear();
            }
            if (best != null)
            {
                Reservations.Take(best, Uid);
            }
            return best;
        }

        private void Ignore(Component target)
        {
            if (target != null)
            {
                _ignored[target.GetInstanceID()] = Time.time + IgnoreSeconds;
            }
        }

        private void Release()
        {
            if (_target != null)
            {
                Reservations.Release(_target, Uid);
            }
            _target = null;
            // Letting go of a target is not bringing it down.
            _struck = false;
            _targetPrefab = null;
        }
    }
}
