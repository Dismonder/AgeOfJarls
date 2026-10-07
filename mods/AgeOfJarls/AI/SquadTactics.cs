using System.Collections.Generic;
using AgeOfJarls.Army;
using AgeOfJarls.Settlers;
using UnityEngine;

namespace AgeOfJarls.AI
{
    /// <summary>
    /// How the settlement's soldiers fight together, on top of vanilla's "attack what you see". Focus fire: a soldier
    /// at home goes for the enemy its comrades nearby are already fighting - the most wounded one first - instead of
    /// each picking its own, so enemies go down one after another. An archer with a comrade or a player in its line
    /// of fire steps to the side before it shoots. Owner only, like the AI; players' orders (attack, fall back) come
    /// first and are not touched here.
    /// </summary>
    internal sealed class SquadTactics
    {
        private const float ScanSeconds = 0.5f;
        /// <summary>A comrade's enemy this far from the soldier is a fight worth joining.</summary>
        private const float FocusRange = 12f;
        /// <summary>The target in hand keeps this much of an edge (in health fraction) over another, so soldiers do not flit between two.</summary>
        private const float StickBonus = 0.1f;
        /// <summary>Every comrade already on an enemy makes it this much more attractive: the troop piles on.</summary>
        private const float AllyWeight = 0.03f;
        private const float LineWidth = 0.9f;
        private const float LineRange = 14f;
        private const float SidestepDistance = 2f;
        private const float SidestepSeconds = 0.8f;
        private const float SidestepCooldownSeconds = 2f;

        private readonly SettlerAI _ai;
        private readonly Settler _settler;
        private readonly SettlerCharacter _body;
        private readonly Dictionary<Character, int> _onTarget = new Dictionary<Character, int>();
        private float _scanTimer;
        /// <summary>Comrades are fighting (at the last scan): the troop picks the targets, not vanilla.</summary>
        private bool _squadFight;
        private float _sidestepUntil;
        private float _sidestepCooldownUntil;
        private Vector3 _sidestepSpot;

        internal SquadTactics(SettlerAI ai, Settler settler, SettlerCharacter body)
        {
            _ai = ai;
            _settler = settler;
            _body = body;
            _scanTimer = Random.Range(0f, ScanSeconds);
        }

        /// <summary>Before vanilla, for a soldier at home (no leader, no order): the troop's target, if there is one to join.</summary>
        internal void UpdateTargeting(float dt)
        {
            if (!_settler.IsAdult)
            {
                _squadFight = false;
                return;
            }
            // While the troop fights, vanilla's own search is held back: it would pick the nearest enemy every second
            // and the soldier would run back and forth between its choice and the troop's.
            if (_squadFight)
            {
                _ai.HoldVanillaTargeting(ScanSeconds + 0.1f);
            }
            _scanTimer -= dt;
            if (_scanTimer > 0f)
            {
                return;
            }
            _scanTimer = ScanSeconds;
            if (_settler.Role == CombatRole.None || _body.InAttack())
            {
                _squadFight = false;
                return;
            }
            long home = _settler.HomeId;
            if (home == 0L)
            {
                _squadFight = false;
                return;
            }

            Vector3 me = _ai.transform.position;
            Character current = _ai.CurrentTarget;
            _onTarget.Clear();
            foreach (Settler other in Settler.Loaded)
            {
                if (other == null || other == _settler || !other.IsLoaded || !other.IsAdult || other.HomeId != home || other.Role == CombatRole.None || other.IsDown)
                {
                    continue;
                }
                SettlerAI ai = other.GetComponent<SettlerAI>();
                Character target = ai != null ? ai.CurrentTarget : null;
                if (target == null || target.IsDead())
                {
                    continue;
                }
                _onTarget.TryGetValue(target, out int count);
                _onTarget[target] = count + 1;
            }
            _squadFight = _onTarget.Count > 0;
            if (!_squadFight)
            {
                return;
            }

            Character best = null;
            float bestScore = float.MaxValue;
            if (current != null && !current.IsDead())
            {
                _onTarget.TryGetValue(current, out int onCurrent);
                best = current;
                bestScore = current.GetHealthPercentage() - StickBonus - onCurrent * AllyWeight;
            }
            foreach (KeyValuePair<Character, int> entry in _onTarget)
            {
                Character candidate = entry.Key;
                if (candidate == current || Vector3.Distance(candidate.transform.position, me) > FocusRange || !BaseAI.IsEnemy(_body, candidate))
                {
                    continue;
                }
                float score = candidate.GetHealthPercentage() - entry.Value * AllyWeight;
                if (score < bestScore)
                {
                    best = candidate;
                    bestScore = score;
                }
            }
            if (best != null && best != current)
            {
                if (AiTrace.On)
                {
                    AiTrace.Write(_ai, $"focus: {best.m_name} at {best.GetHealthPercentage():P0}, {_onTarget[best]} comrade(s) on it");
                }
                _ai.RetargetTo(best);
                _ai.SetAlerted(true);
            }
        }

        /// <summary>After vanilla moved it, in a fight: an archer about to shoot past a comrade steps aside first.</summary>
        internal void AfterVanilla(float dt)
        {
            if (!_settler.IsAdult)
            {
                return;
            }
            if (Time.time < _sidestepUntil)
            {
                if (!_body.InAttack())
                {
                    _ai.StepBack(dt, _sidestepSpot);
                }
                return;
            }
            Character target = _ai.CurrentTarget;
            if (target == null || Time.time < _sidestepCooldownUntil || _body.InAttack() || !Posts.IsBow(_body.GetCurrentWeapon()))
            {
                return;
            }
            Vector3 me = _body.transform.position;
            Vector3 at = target.transform.position;
            if (Vector3.Distance(me, at) > LineRange)
            {
                return;
            }
            List<Character> all = Character.GetAllCharacters();
            for (int i = 0; i < all.Count; i++)
            {
                Character other = all[i];
                if (other == null || other == _body || other == target || other.IsDead() || !(other.IsPlayer() || other is SettlerCharacter))
                {
                    continue;
                }
                Vector3 where = other.transform.position;
                if (!Formation.InLineOfFire(me, at, where, LineWidth))
                {
                    continue;
                }
                Vector3 line = at - me;
                line.y = 0f;
                Vector3 right = Vector3.Cross(Vector3.up, line.normalized);
                _sidestepSpot = me + right * (Formation.SidestepSide(me, at, where) * SidestepDistance);
                _sidestepUntil = Time.time + SidestepSeconds;
                _sidestepCooldownUntil = Time.time + SidestepCooldownSeconds;
                if (AiTrace.On)
                {
                    AiTrace.Write(_ai, $"{other.m_name} in the line of fire: steps aside");
                }
                _ai.StepBack(dt, _sidestepSpot);
                return;
            }
        }
    }
}
