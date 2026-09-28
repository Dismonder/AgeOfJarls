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
                return true;
            }
            // Before vanilla: its targeting then sees the ordered target (or none) and keeps it, since the
            // retarget timer is held back while an order stands.
            ApplyOrders();
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
                _home.Update(dt);
            }
            else
            {
                _home.Stop();
                Player leader = calm ? Leader : null;
                if (leader != null)
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
            _loot = new LootCollector(this, character, _mover);
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
