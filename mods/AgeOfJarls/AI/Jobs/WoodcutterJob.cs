using System.Collections.Generic;
using AgeOfJarls.Work;
using UnityEngine;

namespace AgeOfJarls.AI.Jobs
{
    /// <summary>
    /// Woodcutter: fells trees in the zone with its axe, chops the fallen logs and gathers the wood; the home routine
    /// carries the load to the sorted chests. Swings are real attacks with the axe in hand, so the game's own rules
    /// apply (tool tier against the tree, damage, effects, drops). Logs come before new trees, so nothing is left lying.
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
            // Fallen logs first: they are in the way and already half the work.
            foreach (Component log in WorkScanner.Find<TreeLog>(Totem))
            {
                yield return log;
            }
            foreach (Component tree in WorkScanner.Find<TreeBase>(Totem))
            {
                yield return tree;
            }
        }

        protected override int MinToolTier(Component target) =>
            target is TreeLog log ? log.m_minToolTier : target is TreeBase tree ? tree.m_minToolTier : int.MaxValue;
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
    /// Shared by the woodcutter and the miner: gather what lies in the zone, otherwise pick the nearest object its
    /// tool can break (reserved, so two workers never share one), walk up to it and swing with the tool in hand.
    /// </summary>
    internal abstract class HarvestJob : JobBase
    {
        private const float StrikeDistance = 1.4f;
        private const float BaseSwingSeconds = 1.5f;
        private const float GiveUpSeconds = 40f;
        private const float IgnoreSeconds = 120f;
        private const float ScanSeconds = 3f;
        private const int SlotsToKeepFree = 1;
        private const float FacingDegrees = 20f;

        private readonly Dictionary<int, float> _ignored = new Dictionary<int, float>();
        private Component _target;
        private Collider[] _targetColliders = new Collider[0];
        private float _scanTimer;
        private float _swingTimer;
        private float _tripTimer;

        // What the current target was, to tell when the swings brought it down (it is destroyed, not just released).
        private string _targetPrefab;
        private Vector3 _targetPosition;
        private bool _struck;

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

        protected override bool Work(float dt)
        {
            if (Body.InAttack())
            {
                // Mid-swing it holds still, facing where the blow lands: turning now - to a drop, to the next target -
                // would send the blade into whatever lies that way. A target felled by this very blow is let go after.
                // The pause between swings runs on meanwhile: it is counted from the start of a swing.
                _swingTimer -= dt;
                if (_target != null)
                {
                    Face(WorkScanner.StrikePoint(_target, _targetColliders, Position));
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

            // What the last swings dropped (wood, stone, ore, resin, seeds...) comes first.
            if (Ctx.Loot.CollectInZone(dt, Totem.transform.position, Totem.Radius, item => true))
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
                    // Unity's null: the object this worker was striking has been destroyed.
                    OnBroughtDown(_targetPrefab, _targetPosition);
                }
                Release();
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
                Ctx.Mover.Reset();
            }
            Problem("");

            Vector3 point = WorkScanner.StrikePoint(_target, _targetColliders, Position);
            if (Utils.DistanceXZ(point, Position) > StrikeDistance)
            {
                _tripTimer += dt;
                MoveResult move = Ctx.Mover.MoveTo(dt, point, StrikeDistance * 0.8f, StrikeDistance + 0.6f, run: false);
                if (move == MoveResult.Blocked || _tripTimer > GiveUpSeconds)
                {
                    Ignore(_target);
                    Release();
                }
                return true;
            }

            Reservations.Take(_target, Uid);
            Face(point);
            _swingTimer -= dt;
            // The swing goes where the settler faces: wait until it has turned to the target.
            Vector3 toTarget = point - Position;
            toTarget.y = 0f;
            bool facing = toTarget.sqrMagnitude < 0.01f || Ctx.Ai.IsLookingTowards(toTarget.normalized, FacingDegrees);
            if (facing && _swingTimer <= 0f && !Body.InAttack() && Body.StartAttack(null, false))
            {
                _swingTimer = BaseSwingSeconds / Mathf.Max(0.1f, Pace);
                _struck = true;
                if (AiTrace.On)
                {
                    AiTrace.Write(Ctx.Ai, $"swings at {Describe(_target)} (strike point {Utils.DistanceXZ(point, Position):0.0} m)");
                }
            }
            return true;
        }

        internal override void Stop()
        {
            Release();
            base.Stop();
        }

        private bool IsWorkable(Component target, ItemDrop.ItemData tool) =>
            target != null && MinToolTier(target) <= tool.m_shared.m_toolTier && Reservations.IsFree(target, Uid) &&
            InZone(target.transform.position);

        /// <summary>At the last scan the zone held work, but only for a better tool (birch for a stone axe...).</summary>
        private bool _toolTooWeak;

        private Component FindTarget(ItemDrop.ItemData tool)
        {
            Component best = null;
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
                    _toolTooWeak |= MinToolTier(candidate) > tool.m_shared.m_toolTier && InZone(candidate.transform.position);
                    continue;
                }
                float sqr = (candidate.transform.position - Position).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    best = candidate;
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
