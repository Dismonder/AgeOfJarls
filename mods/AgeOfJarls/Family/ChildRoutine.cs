using System.Runtime.CompilerServices;
using AgeOfJarls.AI;
using AgeOfJarls.Core;
using AgeOfJarls.Settlement;
using AgeOfJarls.Settlers;
using UnityEngine;

namespace AgeOfJarls.Family
{
    /// <summary>Owner-side play near home; parent references are also shared with the all-client home anchor.</summary>
    internal sealed class ChildRoutine
    {
        private const float ParentSeconds = 3f;
        private const float RetrySeconds = 30f;
        private readonly SettlerAI _ai;
        private readonly Settler _settler;
        private readonly SettlerCharacter _body;
        private readonly PathMover _mover;
        private float _pickAt;
        private float _emoteAt;
        private float _retryAt;
        private Vector3 _spot;
        private bool _hasSpot;
        private bool _arrived;
        private bool _byParent;

        // Weak keys release unloaded children; no scans or new cache entries on the usual frame path.
        private static readonly ConditionalWeakTable<Settler, Parents> s_parents = new ConditionalWeakTable<Settler, Parents>();
        private static readonly ConditionalWeakTable<Settler, Parents>.CreateValueCallback s_createParents = CreateParents;

        private sealed class Parents
        {
            internal Settler Mother;
            internal Settler Father;
            internal float RefreshAt = Time.time + Random.Range(0f, ParentSeconds);
        }

        private static Parents CreateParents(Settler settler) => new Parents();

        internal ChildRoutine(SettlerAI ai, Settler settler, SettlerCharacter body, PathMover mover)
        {
            _ai = ai;
            _settler = settler;
            _body = body;
            _mover = mover;
            _pickAt = Time.time + Random.Range(0f, 20f);
            _emoteAt = Time.time + Random.Range(0f, 40f);
        }

        /// <summary>Stationary play can use HomeRoutine's shared sitting timer.</summary>
        internal bool Idle { get; private set; }

        /// <summary>Infants stay beside a parent at home; children have their own smaller ring slot.</summary>
        internal static Vector3 Anchor(Settler settler, JarlTable table)
        {
            if (settler.Stage == LifeStage.Infant)
            {
                Settler parent = FindParent(settler, float.MaxValue, atHome: true);
                if (parent != null)
                {
                    return parent.transform.position;
                }
            }
            return table.transform.position + Settler.IdleSpot(settler.Uid) * 0.7f;
        }

        /// <summary>Mother first, then father, with at most one lookup of each every three seconds.</summary>
        internal static Settler FindParent(Settler settler, float range, bool atHome, bool idleOrWorking = false)
        {
            Parents parents = s_parents.GetValue(settler, s_createParents);
            if (Time.time >= parents.RefreshAt)
            {
                parents.RefreshAt = Time.time + ParentSeconds;
                parents.Mother = Settler.FindByUid(settler.MotherUid);
                parents.Father = Settler.FindByUid(settler.FatherUid);
            }
            if (EligibleParent(parents.Mother, settler, range, atHome, idleOrWorking))
            {
                return parents.Mother;
            }
            return EligibleParent(parents.Father, settler, range, atHome, idleOrWorking) ? parents.Father : null;
        }

        private static bool EligibleParent(Settler parent, Settler child, float range, bool atHome, bool idleOrWorking)
        {
            if (parent == null || !parent.IsLoaded || parent.IsDown ||
                (atHome && (!parent.IsAtHome || parent.HomeId != child.HomeId)) ||
                Vector3.Distance(parent.transform.position, child.transform.position) > range)
            {
                return false;
            }
            if (!idleOrWorking)
            {
                return true;
            }
            SettlerActivity activity = (SettlerActivity)(parent.Zdo?.GetInt(Keys.ZdoSettlerActivity) ?? 0);
            return activity == SettlerActivity.Idle || activity == SettlerActivity.Working;
        }

        /// <summary>One calm owner frame; HomeRoutine contains exceptions and commits Playing or NoFood once.</summary>
        internal void Update(float dt, JarlTable table)
        {
            Idle = false;
            Vector3 anchor = Anchor(_settler, table);
            if (_settler.Stage == LifeStage.Infant)
            {
                Infant(dt, anchor);
                return;
            }
            if (Time.time >= _retryAt && Time.time >= _pickAt)
            {
                PickSpot(anchor);
            }
            if (!_hasSpot || _arrived || Time.time < _retryAt)
            {
                _ai.Halt();
                Idle = true;
                return;
            }
            _settler.SetSitting(false);
            MoveResult move = _mover.MoveTo(dt, _spot, 0.6f, 1f, run: true);
            if (move == MoveResult.Blocked)
            {
                GiveUp();
            }
            else if (move == MoveResult.Arrived)
            {
                _arrived = true;
                _ai.Halt();
                Idle = true;
                if (_byParent)
                {
                    _pickAt = Time.time + 10f;
                }
                if (Random.value < 0.3f)
                {
                    int emote = Random.Range(0, 4);
                    _body.PlayEmote(emote == 0 ? "dance" : emote == 1 ? "cheer" : emote == 2 ? "laugh" : "wave");
                }
            }
        }

        private void Infant(float dt, Vector3 anchor)
        {
            if (Time.time >= _retryAt && Utils.DistanceXZ(_ai.transform.position, anchor) > 2.5f)
            {
                _settler.SetSitting(false);
                Vector3 offset = Settler.IdleSpot(_settler.Uid).normalized * 1.2f;
                MoveResult move = _mover.MoveTo(dt, anchor + offset, 1f, 2f, run: false);
                // PathMover normally jogs on long trips; infants only run during alarms.
                _body.SetRun(false);
                if (move == MoveResult.Blocked)
                {
                    GiveUp();
                }
                if (move == MoveResult.Moving)
                {
                    return;
                }
            }
            _ai.Halt();
            Idle = true;
            if (Time.time >= _emoteAt)
            {
                _emoteAt = Time.time + Random.Range(20f, 40f);
                _body.PlayEmote(Random.value < 0.5f ? "wave" : "laugh");
            }
        }

        private void PickSpot(Vector3 anchor)
        {
            _pickAt = Time.time + Random.Range(8f, 20f);
            Settler parent = Random.value < 0.25f ? FindParent(_settler, 25f, atHome: true, idleOrWorking: true) : null;
            _byParent = parent != null;
            if (_byParent)
            {
                Vector3 side = Settler.IdleSpot(_settler.Uid).normalized;
                _spot = parent.transform.position + side * 1.5f;
            }
            else
            {
                Vector2 offset = Random.insideUnitCircle * 6f;
                _spot = anchor + new Vector3(offset.x, 0f, offset.y);
                if (Pathfinding.instance != null &&
                    Pathfinding.instance.FindValidPoint(out Vector3 snapped, _spot, 1f, _ai.m_pathAgentType) &&
                    Utils.DistanceXZ(snapped, anchor) <= 6f)
                {
                    _spot = snapped;
                }
                else
                {
                    GiveUp();
                    return;
                }
            }
            _hasSpot = true;
            _arrived = false;
            _mover.Reset();
        }

        private void GiveUp()
        {
            _retryAt = Time.time + RetrySeconds;
            _pickAt = _retryAt;
            _hasSpot = false;
            _mover.Reset();
            _ai.Halt();
        }

        /// <summary>Higher priorities interrupt a play trip without losing its blocked-spot cooldown.</summary>
        internal void Stop()
        {
            _hasSpot = false;
            _arrived = false;
            Idle = false;
        }
    }
}
