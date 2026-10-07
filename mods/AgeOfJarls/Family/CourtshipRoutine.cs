using System;
using AgeOfJarls.AI;
using AgeOfJarls.Core;
using AgeOfJarls.Settlement;
using AgeOfJarls.Settlers;
using UnityEngine;
using Random = UnityEngine.Random;

namespace AgeOfJarls.Family
{
    /// <summary>Owner-side visits in shared world-time windows, so partners with different owners meet together.</summary>
    internal sealed class CourtshipRoutine
    {
        internal const double VisitPeriod = 300.0;
        internal const double VisitLength = 45.0;
        private readonly SettlerAI _ai;
        private readonly Settler _settler;
        private readonly SettlerCharacter _body;
        private readonly PathMover _mover;
        private long _otherUid;
        private long _cachedUid;
        private Settler _other;
        private bool _couple;
        private float _refreshAt;
        private float _emoteAt;
        private float _retryAt;

        internal CourtshipRoutine(SettlerAI ai, Settler settler, SettlerCharacter body, PathMover mover)
        {
            _ai = ai;
            _settler = settler;
            _body = body;
            _mover = mover;
            _refreshAt = Time.time + Random.Range(0f, 3f);
            _emoteAt = Time.time + Random.Range(0f, 9f);
        }

        /// <summary>The home routine stops the job once on entering a visit.</summary>
        internal bool IsVisiting { get; private set; }

        /// <summary>Cheap outside a window; evaluated before work so even a busy worker can visit.</summary>
        internal bool ShouldVisit(JarlTable table, bool night)
        {
            double now = WorldClock.Now;
            if (now % VisitPeriod >= VisitLength)
            {
                return false;
            }
            if (night || table == null || !_settler.IsAdult || !_settler.IsAtHome || !_ai.IsCalm() ||
                table.AlarmOn || _settler.IsResting || (_settler.Zdo != null && Needs.IsHungry(_settler.Zdo)))
            {
                return false;
            }
            FamilyInfo family = _settler.Family;
            _couple = family.PartnerUid != 0L;
            _otherUid = _couple ? family.PartnerUid : family.CourtingUid;
            return _otherUid != 0L && (!_couple || Math.Floor(now / VisitPeriod) % 3 == 0);
        }

        /// <summary>After ShouldVisit succeeds; HomeRoutine contains exceptions and commits Courting once.</summary>
        internal bool Update(float dt, JarlTable table)
        {
            if (!IsVisiting)
            {
                IsVisiting = true;
                _mover.Reset();
            }
            // A brief alarm may have put it to bed without ending this window.
            _body.GetUp();
            if (Time.time >= _refreshAt)
            {
                _refreshAt = Time.time + 3f;
                _cachedUid = _otherUid;
                _other = Settler.FindByUid(_otherUid);
            }
            Settler other = _cachedUid == _otherUid && _other != null && _other.IsLoaded ? _other : null;
            Vector3 tablePosition = table.transform.position;
            Vector3 meeting = tablePosition + Settler.IdleSpot(Math.Min(_settler.Uid, _otherUid));
            Vector3 towards = (other != null ? other.transform.position : tablePosition) - meeting;
            towards.y = 0f;
            meeting += towards.normalized * 0.8f;
            if (Time.time < _retryAt)
            {
                _ai.Halt();
                return true;
            }
            _settler.SetSitting(false);
            MoveResult move = _mover.MoveTo(dt, meeting, 0.8f, 1.2f, run: false);
            _body.SetRun(false);
            if (move == MoveResult.Blocked)
            {
                _retryAt = Time.time + 30f;
                _mover.Reset();
                _ai.Halt();
                return true;
            }
            if (move != MoveResult.Arrived)
            {
                return true;
            }
            _ai.Halt();
            _ai.AimAt(other != null ? other.transform.position : tablePosition);
            if (Time.time >= _emoteAt)
            {
                _emoteAt = Time.time + Random.Range(6f, 9f);
                int choice = (int)(Math.Floor(WorldClock.Now / 7.0) % (_couple ? 3 : 4));
                string emote = _couple ? (choice == 0 ? "loveyou" : choice == 1 ? "laugh" : "cheer")
                    : (choice == 0 ? "blowkiss" : choice == 1 ? "wave" : choice == 2 ? "laugh" : "loveyou");
                _body.PlayEmote(emote);
            }
            return true;
        }

        /// <summary>No relationship writes: the table remains the authority.</summary>
        internal void Stop() => IsVisiting = false;
    }
}
