using System;
using AgeOfJarls.Core;
using AgeOfJarls.Settlers;
using UnityEngine;

namespace AgeOfJarls.AI
{
    /// <summary>
    /// The settler's AI: vanilla MonsterAI (targets, fighting, fleeing, following, idling) plus settler behaviours.
    /// Vanilla decides first; a settler behaviour replaces that frame's movement only while the settler is calm.
    /// Like every BaseAI it only runs on the ZDO owner.
    /// </summary>
    public class SettlerAI : MonsterAI
    {
        private const string Module = "AI";
        private const float ErrorLogSeconds = 30f;

        private float _nextErrorLogTime;
        private Settler _settler;
        private PathMover _mover;
        private LootCollector _loot;
        private HomeRoutine _home;

        /// <summary>A player's "attack!" holds for this long, or until the target dies or runs out of range.</summary>
        private const float AttackOrderSeconds = 30f;
        private const float OrderedTargetRange = 50f;
        /// <summary>A "fall back!" keeps settlers from picking fights for this long, so they really disengage.</summary>
        private const float CeaseFireSeconds = 8f;

        private Character _orderedTarget;
        private float _orderedUntil;
        private float _ceaseFireUntil;
        private CombatSense _sense;

        /// <summary>Blocks and the striker as target (owner only; null before the first AI frame).</summary>
        internal CombatSense Sense => _sense;

        /// <summary>Means to block a blow right now: vanilla must not start a swing (see CombatPatches).</summary>
        internal bool HoldingBlock => _sense != null && _sense.HoldingBlock;

        internal bool HasOrderedTarget => _orderedTarget != null;

        /// <summary>A few running steps to a point (an archer backing away): vanilla's own path following.</summary>
        internal void StepBack(float dt, Vector3 point) => MoveTo(dt, point, 0.5f, true);

        private Escort _escort;
        private PortalTravel _portals;
        private RescueDuty _rescue;
        private SquadTactics _squad;
        private Courtesy _courtesy;
        private bool _travelling;

        /// <summary>Following as a player's guard (owner only; null before the first AI frame).</summary>
        internal Escort Escort => _escort;

        internal Character Body => m_character;

        internal Character CurrentTarget => m_targetCreature;

        /// <summary>Vanilla's path following to a point; true once there (within half a metre, one at a run) or with no path.</summary>
        internal bool MoveToPoint(float dt, Vector3 point, float dist, bool run) => MoveTo(dt, point, dist, run);

        internal void Halt() => StopMoving();

        internal void CalmDown() => SetAlerted(false);

        /// <summary>Keeps vanilla from looking for a target of its own for this long (the guard picks them).</summary>
        internal void HoldVanillaTargeting(float seconds) => m_updateTargetTimer = Mathf.Max(m_updateTargetTimer, seconds);

        /// <summary>The enemy striking it becomes its target, like vanilla's own retarget on damage - before the hit.</summary>
        internal void RetargetTo(Character target)
        {
            m_targetCreature = target;
            m_targetStatic = null;
            m_updateTargetTimer = 1f;
        }

        /// <summary>Owner only (via RPC): fight this creature, whatever vanilla targeting would prefer.</summary>
        internal void OrderAttack(Character target)
        {
            _orderedTarget = target;
            _orderedUntil = Time.time + AttackOrderSeconds;
            _ceaseFireUntil = 0f;
            SetAlerted(true);
        }

        /// <summary>Owner only (via RPC): drop every target for a while.</summary>
        internal void OrderCeaseFire()
        {
            _orderedTarget = null;
            _ceaseFireUntil = Time.time + CeaseFireSeconds;
            m_targetCreature = null;
            m_targetStatic = null;
        }

        public override bool UpdateAI(float dt)
        {
            if (m_character is SettlerCharacter body && body.Down)
            {
                // Knocked out: lies still until it gets up, with no targets and no chores.
                StopMoving();
                m_targetCreature = null;
                m_targetStatic = null;
                _home?.Stop();
                _sense?.Release();
                return true;
            }
            // Before vanilla, guarded like the behaviours after it: an exception here would take the game's own AI
            // down with it every frame (BaseAI.FixedUpdate does not catch), and the settler would stand frozen.
            try
            {
                // Its targeting then sees the ordered target (or none) and keeps it, since the retarget timer is
                // held back while an order stands.
                ApplyOrders();
                // A guard fights for the player it follows: its target comes from the escort, not from vanilla's
                // search. A soldier at home fights with the troop: the enemy its comrades are on (SquadTactics).
                if (_escort != null && _orderedTarget == null && Time.time >= _ceaseFireUntil)
                {
                    Player leader = Leader;
                    if (leader != null)
                    {
                        _escort.UpdateTargeting(dt, leader);
                    }
                    else if (_settler.HasHome)
                    {
                        _squad.UpdateTargeting(dt);
                    }
                }
                // Blocks and the striker as target before vanilla swings: a swing of its own would cancel the block.
                _sense?.Update(dt);
            }
            catch (Exception e)
            {
                if (Time.time >= _nextErrorLogTime)
                {
                    _nextErrorLogTime = Time.time + ErrorLogSeconds;
                    Log.Error(Module, $"{name}: settler targeting failed, vanilla AI carries on: {e}");
                }
            }
            long perf = Perf.Start();
            bool running = base.UpdateAI(dt);
            Perf.Stop(Perf.Section.VanillaAi, perf);
            if (!running)
            {
                return false;
            }
            perf = Perf.Start();
            try
            {
                if (!IsCalm())
                {
                    _sense?.AfterVanilla(dt);
                    _squad?.AfterVanilla(dt);
                }
                UpdateSettlerBehaviours(dt);
            }
            catch (Exception e)
            {
                // A failing settler behaviour must not take the vanilla AI down with it, nor log every frame.
                if (Time.time >= _nextErrorLogTime)
                {
                    _nextErrorLogTime = Time.time + ErrorLogSeconds;
                    Log.Error(Module, $"{name}: settler behaviour failed, vanilla AI carries on: {e}");
                }
            }
            Perf.Stop(Perf.Section.SettlerAi, perf);
            return true;
        }

        private void ApplyOrders()
        {
            if (Time.time < _ceaseFireUntil)
            {
                m_targetCreature = null;
                m_targetStatic = null;
                m_updateTargetTimer = 1f;
                return;
            }
            if (_orderedTarget == null)
            {
                return;
            }
            if (_orderedTarget.IsDead() || Time.time > _orderedUntil ||
                Vector3.Distance(_orderedTarget.transform.position, transform.position) > OrderedTargetRange)
            {
                _orderedTarget = null;
                return;
            }
            // Vanilla still drops targets that are not enemies of a tamed creature (players, other tamed ones).
            m_targetCreature = _orderedTarget;
            m_targetStatic = null;
            m_updateTargetTimer = 1f;
        }

        private void UpdateSettlerBehaviours(float dt)
        {
            // Created lazily: MonsterAI.Awake is protected in the game but public in the publicized reference, so
            // overriding it would not match at runtime.
            if (_home == null && !Init())
            {
                return;
            }

            if (_settler.IsCaptive)
            {
                // Tied up until freed: no fights, no chores.
                OrderCeaseFire();
                _home.Stop();
                return;
            }

            // Talked to (its window open on this machine): it stops and faces the player, and takes up its chores
            // where it left them once the window closes. A swing under way finishes where it was aimed.
            Player talker = Player.m_localPlayer;
            if (talker != null && IsCalm() && UI.SettlerWindow.IsOpenFor(_settler))
            {
                StopMoving();
                if (!m_character.InAttack())
                {
                    LookAt(talker.transform.position);
                }
                _settler.FlushInventory();
                return;
            }

            bool calm = IsCalm();
            if (calm && _settler.IsAtHome)
            {
                // Far from home with a portal into the settlement nearby: the portal, otherwise the home routine.
                // The routine is stopped once, when the trip begins: Stop() each frame would flip the activity
                // (and its ZDO value) between idle and returning every frame.
                // A knocked-out neighbour comes first (Realistic mode), then the portal home, then the routine.
                bool travelling = _rescue.Update(dt) || _portals.Update(dt);
                if (travelling && !_travelling)
                {
                    _home.Stop();
                }
                _travelling = travelling;
                if (!travelling)
                {
                    _home.Update(dt);
                }
            }
            else
            {
                _travelling = false;
                _home.Stop();
                Player leader = calm ? Leader : null;
                // Out of a player's way first (a guard in a doorway); otherwise the loot around the leader.
                if (leader != null && !_rescue.Update(dt) && !_courtesy.Update(dt))
                {
                    // A settler with a home keeps its loot for the chests there.
                    _loot.Follow(dt, leader, handOver: !_settler.HasHome);
                }
            }
            long perf = Perf.Start();
            _mover.Update();
            Perf.Stop(Perf.Section.Paths, perf);
            // Picked up, stored or handed over this frame: in the ZDO before ownership can move.
            _settler.FlushInventory();
        }

        private bool Init()
        {
            _settler = GetComponent<Settler>();
            var character = GetComponent<SettlerCharacter>();
            if (_settler == null || character == null)
            {
                return false;
            }
            _mover = new PathMover(this);
            _sense = new CombatSense(this, character, _settler);
            _loot = new LootCollector(this, character, _mover);
            _escort = new Escort(this, _settler);
            _portals = new PortalTravel(this, _settler, _mover);
            _rescue = new RescueDuty(this, _settler, _mover);
            _squad = new SquadTactics(this, _settler, character);
            _courtesy = new Courtesy(this);
            var context = new Jobs.JobContext { Ai = this, Settler = _settler, Body = character, Mover = _mover, Loot = _loot };
            var duty = new SoldierDuty(this, _settler, character, _mover);
            _home = new HomeRoutine(this, _settler, character, _mover, _loot, duty, job => CreateJob(job, context));
            return true;
        }

        private static Jobs.JobBase CreateJob(Work.JobType job, Jobs.JobContext context)
        {
            switch (job)
            {
                case Work.JobType.Woodcutter:
                    return new Jobs.WoodcutterJob(context);
                case Work.JobType.Miner:
                    return new Jobs.MinerJob(context);
                case Work.JobType.Hauler:
                    return new Jobs.HaulerJob(context);
                case Work.JobType.Builder:
                    return new Jobs.BuilderJob(context);
                case Work.JobType.Farmer:
                    return new Jobs.FarmerJob(context);
                case Work.JobType.Smelter:
                    return new Jobs.SmelterJob(context);
                case Work.JobType.Cook:
                    return new Jobs.CookJob(context);
                default:
                    return null;
            }
        }

        /// <summary>For aoj_debug, on the owner: fight, orders, home routine, path and loot.</summary>
        internal string DebugState()
        {
            string fight = m_targetCreature != null ? $"fights {m_targetCreature.GetHoverName()}"
                : m_targetStatic != null ? $"attacks {Utils.GetPrefabName(m_targetStatic.gameObject)}"
                : Time.time < _ceaseFireUntil ? "cease-fire"
                : "calm";
            string ordered = _orderedTarget != null ? $" (ordered: {_orderedTarget.GetHoverName()})" : "";
            string guard = _sense != null ? _sense.DebugState() : "";
            if (guard.Length > 0)
            {
                ordered += " · " + guard;
            }
            string home = _home != null ? _home.DebugState() : "-";
            string path = _mover != null ? _mover.DebugState() : "-";
            string loot = _loot != null ? _loot.DebugState() : "-";
            return $"{fight}{ordered}\nhome: {home}\npath: {path} · loot: {loot}";
        }

        /// <summary>
        /// No enemy or structure targeted: free to do settler things. A swing is no fight - at work (an axe at a tree)
        /// it is the work itself, and the job holds the settler still until it lands.
        /// </summary>
        internal bool IsCalm() => m_targetCreature == null && m_targetStatic == null && !m_character.IsDead();

        /// <summary>Looks at the point from its eyes, up or down too - a swing goes where the settler looks.</summary>
        internal void AimAt(Vector3 point) => LookAt(point);

        /// <summary>The player this settler follows, if any.</summary>
        internal Player Leader
        {
            get
            {
                GameObject target = GetFollowTarget();
                return target != null ? target.GetComponent<Player>() : null;
            }
        }
    }
}
