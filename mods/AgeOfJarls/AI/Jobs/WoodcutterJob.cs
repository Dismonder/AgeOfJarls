using System.Collections.Generic;
using AgeOfJarls.Work;
using UnityEngine;

namespace AgeOfJarls.AI.Jobs
{
    /// <summary>
    /// Woodcutter: fells trees in the zone with its axe, chops the fallen logs and gathers the wood; the home routine
    /// carries the load to the sorted chests. Swings are real attacks with the axe in hand, so the game's own rules
    /// apply (tool tier against the tree, damage, effects, drops). Logs come before new trees, so nothing is left lying.
    /// </summary>
    internal sealed class WoodcutterJob : HarvestJob
    {
        internal WoodcutterJob(JobContext context) : base(context)
        {
        }

        internal override JobType Job => JobType.Woodcutter;

        protected override ToolKind Tool => ToolKind.Axe;

        protected override string NothingToDo => "$aoj_problem_no_trees";

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
                Release();
                _scanTimer -= dt;
                if (_scanTimer > 0f)
                {
                    return false;
                }
                _scanTimer = ScanSeconds;
                _target = FindTarget(tool);
                _tripTimer = 0f;
                _targetColliders = WorkScanner.SolidColliders(_target);
                if (_target == null)
                {
                    Problem(NothingToDo);
                    return false;
                }
                Ctx.Mover.Reset();
            }
            Problem("");

            Vector3 point = WorkScanner.NearestPoint(_targetColliders, Position, _target.transform.position);
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

        private Component FindTarget(ItemDrop.ItemData tool)
        {
            Component best = null;
            float bestSqr = float.MaxValue;
            float now = Time.time;
            foreach (Component candidate in Candidates())
            {
                if (candidate == null || (_ignored.TryGetValue(candidate.GetInstanceID(), out float until) && now < until) ||
                    !IsWorkable(candidate, tool))
                {
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
        }
    }
}
