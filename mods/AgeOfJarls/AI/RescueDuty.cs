using AgeOfJarls.Core;
using AgeOfJarls.Settlers;
using UnityEngine;

namespace AgeOfJarls.AI
{
    /// <summary>
    /// Realistic mode: a settler that sees one of its own knocked out nearby - of its settlement, or following the
    /// same player - walks over and helps it up, before any chore. The downed one's owner does the getting up
    /// (Settler.RequestRescue); the helper only has to reach it. Owner only, like the AI; the search runs every few
    /// seconds, never while fighting.
    /// </summary>
    internal sealed class RescueDuty
    {
        private const string Module = "AI";
        private const float SearchRange = 40f;
        private const float SearchSeconds = 3f;
        private const float Reach = 2.5f;
        private const float GiveUpSeconds = 60f;

        private readonly SettlerAI _ai;
        private readonly Settler _settler;
        private readonly PathMover _mover;
        private Settler _patient;
        private float _nextSearch;
        private float _since;

        internal RescueDuty(SettlerAI ai, Settler settler, PathMover mover)
        {
            _ai = ai;
            _settler = settler;
            _mover = mover;
        }

        /// <summary>True while on the way to a knocked-out settler (the frame's movement is taken).</summary>
        internal bool Update(float dt)
        {
            if (!AoJConfig.Realistic || _settler.IsDown || _settler.IsCaptive || !_settler.IsAdult || _settler.IsResting)
            {
                if (_patient != null)
                {
                    _patient = null;
                    _mover.Reset();
                }
                return false;
            }
            if (_patient == null)
            {
                if (Time.time < _nextSearch)
                {
                    return false;
                }
                _nextSearch = Time.time + SearchSeconds;
                _patient = FindPatient();
                if (_patient == null)
                {
                    return false;
                }
                _since = Time.time;
                _mover.Reset();
                Log.Info(Module, $"{_settler.DisplayName} goes to help {_patient.DisplayName} up");
            }
            if (_patient == null || !_patient.IsLoaded || !_patient.NeedsRescue || Time.time - _since > GiveUpSeconds)
            {
                _patient = null;
                _mover.Reset();
                return false;
            }
            Vector3 at = _patient.transform.position;
            if (Vector3.Distance(_ai.transform.position, at) <= Reach)
            {
                _patient.RequestRescue(null);
                _patient = null;
                _mover.Reset();
                _ai.Halt();
                return true;
            }
            MoveResult move = _mover.MoveTo(dt, at, Reach * 0.6f, Reach, run: true);
            if (move == MoveResult.Blocked)
            {
                Log.Info(Module, $"{_settler.DisplayName} cannot reach {_patient.DisplayName}");
                _patient = null;
                _nextSearch = Time.time + GiveUpSeconds;
                _mover.Reset();
                return false;
            }
            _settler.SetActivity(SettlerActivity.Returning);
            return true;
        }

        private Settler FindPatient()
        {
            Settler best = null;
            float bestDistance = SearchRange;
            long home = _settler.HomeId;
            long leader = _settler.FollowedPlayerId;
            Vector3 me = _ai.transform.position;
            foreach (Settler other in Settler.Loaded)
            {
                if (other == null || other == _settler || !other.IsLoaded || !other.NeedsRescue)
                {
                    continue;
                }
                bool ours = (home != 0L && other.HomeId == home) || (leader != 0L && other.FollowedPlayerId == leader) || (home == 0L && leader == 0L);
                if (!ours)
                {
                    continue;
                }
                float distance = Vector3.Distance(other.transform.position, me);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = other;
                }
            }
            return best;
        }
    }
}
