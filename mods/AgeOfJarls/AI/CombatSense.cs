using System.Collections.Generic;
using AgeOfJarls.Core;
using AgeOfJarls.Settlers;
using UnityEngine;

namespace AgeOfJarls.AI
{
    /// <summary>
    /// What vanilla MonsterAI never does for a humanoid: defend itself. On the settler's owner, every frame: the enemies
    /// close enough to strike are watched, and when one of them swings, the settler turns to it and raises its shield
    /// (or its weapon, as a player blocks with a sword) so the blow lands on the block. The game's perfect block needs
    /// the block to have started within a quarter second of the hit, so the moment to raise it is timed from the
    /// attacker's wind-up: measured per kind of creature from the hits that landed, shared by every settler on this
    /// machine (an unknown creature is blocked from the start of its swing, a plain block, and learned from). Between
    /// blocks it fights as before; while it means to block it does not swing (<see cref="CombatPatches"/>), and an
    /// enemy striking it from the side becomes its target - the one hitting it, not the one it walked up to.
    /// </summary>
    internal sealed class CombatSense
    {
        private const float ScanSeconds = 0.1f;
        /// <summary>An enemy whose edge is this close may reach the settler with a melee swing.</summary>
        private const float StrikeReach = 3.2f;
        /// <summary>Wind-up of a creature nobody measured yet: on the long side, so the block is up when the hit comes.</summary>
        private const float DefaultWindup = 0.7f;
        /// <summary>The block goes up this long before the expected hit: inside the perfect-block window, with slack for the swing's drift.</summary>
        private const float PerfectLead = 0.12f;
        /// <summary>Kept up this long after the expected hit, in case the swing is slower than measured.</summary>
        private const float HoldAfterHit = 0.35f;
        private const float MaxBlockSeconds = 2.5f;
        private const float LearnRate = 0.3f;
        /// <summary>A measured wind-up outside this is a different attack or a stale start: not learned.</summary>
        private const float MinWindup = 0.15f;
        private const float MaxWindup = 3f;
        /// <summary>An enemy striking from this close takes over as the target when the current one is farther.</summary>
        private const float RetargetRange = 4f;

        // Wind-up per creature prefab, learned from the hits that landed: the same creatures swing the same way everywhere.
        private static readonly Dictionary<string, float> s_windups = new Dictionary<string, float>();

        private readonly SettlerAI _ai;
        private readonly SettlerCharacter _body;
        private readonly Settler _settler;
        private readonly Dictionary<Character, float> _swingStarts = new Dictionary<Character, float>();
        private readonly List<Character> _stale = new List<Character>();

        private float _scanTimer;
        private Character _threat;
        private float _hitExpectedAt;
        private bool _blocking;
        private float _blockSince;

        internal CombatSense(SettlerAI ai, SettlerCharacter body, Settler settler)
        {
            _ai = ai;
            _body = body;
            _settler = settler;
        }

        /// <summary>The settler means to block right now: no swing of its own until the blow has landed.</summary>
        internal bool HoldingBlock => _blocking;

        internal void Update(float dt)
        {
            if (!AoJConfig.SettlerBlocking.Value || _body.Down || _body.IsDead() || _body.IsLyingDown || _settler.IsCaptive)
            {
                Release();
                _swingStarts.Clear();
                return;
            }
            _scanTimer -= dt;
            if (_scanTimer <= 0f)
            {
                _scanTimer = ScanSeconds;
                Scan();
            }
            if (_threat == null || _threat.IsDead())
            {
                Release();
                return;
            }
            // Face the striker, unless its own swing is under way (turning would drag the swing with it).
            if (!_body.InAttack())
            {
                _ai.LookAt(_threat.transform.position);
            }
            if (!_blocking)
            {
                if (Time.time >= _hitExpectedAt - PerfectLead && CanBlock())
                {
                    _blocking = true;
                    _blockSince = Time.time;
                    _body.SetBlocking(true);
                }
                return;
            }
            bool over = !_threat.InAttack() && Time.time > _hitExpectedAt + HoldAfterHit;
            if (over || Time.time - _blockSince > MaxBlockSeconds)
            {
                Release();
            }
        }

        /// <summary>An enemy this close to an archer with nothing but its bow makes it step back to shoot.</summary>
        private const float KiteRange = 3.5f;
        private const float KiteStep = 6f;
        private Character _nearest;
        private float _nearestDistance;

        /// <summary>
        /// After vanilla moved it: an archer with an enemy at its feet and no melee weapon in hand backs off a few metres
        /// before the next shot, instead of standing in the blows (vanilla walks a bow up to its target like a club).
        /// </summary>
        internal void AfterVanilla(float dt)
        {
            if (_nearest == null || _nearest.IsDead() || _nearestDistance > KiteRange || _body.InAttack() || _body.Down ||
                !AoJConfig.SettlerBlocking.Value)
            {
                return;
            }
            ItemDrop.ItemData weapon = _body.GetCurrentWeapon();
            if (weapon == null || !Army.Posts.IsBow(weapon))
            {
                return;
            }
            Vector3 away = _body.transform.position - _nearest.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f)
            {
                away = -_body.transform.forward;
            }
            _ai.StepBack(dt, _body.transform.position + away.normalized * KiteStep);
        }

        /// <summary>Block down, no striker watched: knocked out, asleep, or a fight that is over.</summary>
        internal void Release()
        {
            if (_blocking)
            {
                _blocking = false;
                _body.SetBlocking(false);
            }
            _threat = null;
        }

        // Shields block; so does a melee weapon, like a player's. A bow or empty hands have nothing to block with.
        private bool CanBlock()
        {
            ItemDrop.ItemData left = _body.m_leftItem;
            if (left != null && left.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield)
            {
                return true;
            }
            ItemDrop.ItemData weapon = _body.GetCurrentWeapon();
            return weapon != null && left == null && weapon.m_shared.m_blockPower > 0f && Army.Posts.IsMeleeWeapon(weapon);
        }

        private void Scan()
        {
            Vector3 position = _body.transform.position;
            Character threat = null;
            float soonest = float.MaxValue;
            _nearest = null;
            _nearestDistance = float.MaxValue;
            _stale.AddRange(_swingStarts.Keys);
            foreach (Character other in Character.GetAllCharacters())
            {
                if (other == null || other == _body || other.IsDead() || !BaseAI.IsEnemy(_body, other))
                {
                    continue;
                }
                // From the edge of the enemy, not its middle: a troll's swing starts a long way from its centre.
                float distance = Vector3.Distance(other.transform.position, position) - other.GetRadius();
                if (distance < _nearestDistance)
                {
                    _nearest = other;
                    _nearestDistance = distance;
                }
                if (distance > StrikeReach)
                {
                    continue;
                }
                _stale.Remove(other);
                if (!other.InAttack())
                {
                    _swingStarts.Remove(other);
                    continue;
                }
                if (!_swingStarts.TryGetValue(other, out float start))
                {
                    start = Time.time;
                    _swingStarts[other] = start;
                }
                // Only a swing aimed this way: an enemy busy with somebody else is no reason to cower.
                Vector3 toUs = position - other.transform.position;
                toUs.y = 0f;
                if (Vector3.Dot(other.transform.forward, toUs.normalized) < 0.3f)
                {
                    continue;
                }
                float hitAt = start + Windup(other);
                if (hitAt < soonest)
                {
                    soonest = hitAt;
                    threat = other;
                }
            }
            foreach (Character gone in _stale)
            {
                _swingStarts.Remove(gone);
            }
            _stale.Clear();

            if (threat != _threat)
            {
                // A new striker, or none: the block for the old one ends with it.
                if (_blocking && threat == null)
                {
                    Release();
                }
                _threat = threat;
            }
            _hitExpectedAt = soonest;
            if (threat != null && !_ai.HasOrderedTarget)
            {
                Character target = _ai.m_targetCreature;
                if (target != threat && (target == null || Vector3.Distance(target.transform.position, position) > RetargetRange))
                {
                    _ai.RetargetTo(threat);
                }
            }
        }

        private static float Windup(Character attacker) =>
            s_windups.TryGetValue(Utils.GetPrefabName(attacker.gameObject), out float windup) ? windup : DefaultWindup;

        /// <summary>A hit from this attacker landed (blocked or not): how long its swing took is learned for its kind.</summary>
        internal void OnHit(Character attacker)
        {
            if (attacker == null || !_swingStarts.TryGetValue(attacker, out float start))
            {
                return;
            }
            float windup = Time.time - start;
            if (windup < MinWindup || windup > MaxWindup)
            {
                return;
            }
            string prefab = Utils.GetPrefabName(attacker.gameObject);
            s_windups[prefab] = s_windups.TryGetValue(prefab, out float known) ? Mathf.Lerp(known, windup, LearnRate) : windup;
            if (AiTrace.On)
            {
                AiTrace.Write(_ai, $"hit by {prefab} {windup:0.00} s into its swing{(_blocking ? " (blocking)" : "")}: wind-up now {s_windups[prefab]:0.00} s");
            }
        }

        /// <summary>For aoj_debug.</summary>
        internal string DebugState() =>
            _threat != null ? $"{(_blocking ? "blocks" : "braces for")} {_threat.GetHoverName()} ({_hitExpectedAt - Time.time:0.00} s)" : "";
    }
}
