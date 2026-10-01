using UnityEngine;

namespace AgeOfJarls.AI
{
    /// <summary>
    /// A settler standing where a player walks steps out of the way: in a doorway, in a passage, at the table. A
    /// player walking at it - close, fast enough to mean it, straight at it - sends it a step and a half to the side
    /// it already stands on, for under a second; then it goes on with what it was doing. Not while it fights. Owner
    /// only, like the AI; asked by the home routine and for followers, before their own movement.
    /// </summary>
    internal sealed class Courtesy
    {
        private const float NoticeRange = 2.6f;
        private const float MinSpeed = 0.8f;
        private const float ApproachCos = 0.6f;
        private const float StepDistance = 1.6f;
        private const float StepSeconds = 0.8f;
        private const float CooldownSeconds = 2f;
        private const float CheckSeconds = 0.2f;
        private const float Done = 0.3f;

        private readonly SettlerAI _ai;
        private float _checkTimer;
        private float _until;
        private float _cooldownUntil;
        private Vector3 _spot;

        internal Courtesy(SettlerAI ai)
        {
            _ai = ai;
        }

        /// <summary>True while stepping aside (the frame's movement is taken).</summary>
        internal bool Update(float dt)
        {
            Vector3 me = _ai.transform.position;
            if (Time.time < _until)
            {
                Vector3 to = _spot - me;
                to.y = 0f;
                if (to.magnitude > Done)
                {
                    // A step, not a trip: straight there, no path.
                    _ai.MoveTowards(to.normalized, false);
                    return true;
                }
                _until = 0f;
                _ai.Halt();
                return false;
            }
            _checkTimer -= dt;
            if (_checkTimer > 0f || Time.time < _cooldownUntil || _ai.Body == null || _ai.Body.InAttack())
            {
                return false;
            }
            _checkTimer = CheckSeconds;
            foreach (Player player in Player.GetAllPlayers())
            {
                if (player == null || player.IsDead())
                {
                    continue;
                }
                Vector3 velocity = player.GetVelocity();
                if (!Formation.WalksInto(me, player.transform.position, velocity, NoticeRange, MinSpeed, ApproachCos))
                {
                    continue;
                }
                _spot = Formation.StepAside(me, player.transform.position, velocity, StepDistance);
                _until = Time.time + StepSeconds;
                _cooldownUntil = Time.time + CooldownSeconds;
                if (AiTrace.On)
                {
                    AiTrace.Write(_ai, $"steps aside for {player.GetPlayerName()}");
                }
                return Update(dt);
            }
            return false;
        }
    }
}
