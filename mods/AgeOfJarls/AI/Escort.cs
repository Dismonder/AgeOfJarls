using System.Collections.Generic;
using AgeOfJarls.Core;
using AgeOfJarls.Settlers;
using HarmonyLib;
using UnityEngine;

namespace AgeOfJarls.AI
{
    /// <summary>
    /// A settler following a player is that player's guard. Vanilla "follow" walks straight at the player and stops
    /// three metres short - in front of the camera as often as not - and fights whatever it notices, however far away.
    /// The guard keeps to the player's shoulder: a little behind and to one side (each settler its own side), out of
    /// the player's view, and does not fidget while the player stands and looks around - only when it has been in
    /// the way for a moment does it step behind again. It fights for the player, not for itself: the enemy nearest
    /// the player, first of all one going for the player or one that just struck the player, and never one farther
    /// from the player than the leash - it breaks off and comes back rather than chase into the woods. Owner only,
    /// like the AI itself.
    /// </summary>
    internal sealed class Escort
    {
        private const string Module = "AI";
        /// <summary>
        /// With the player standing, it stays put however the player turns - unless it is in the way (see
        /// <see cref="InViewPatience"/>) or has ended up this far from its spot (the player stepped aside, or it was
        /// pushed): a player looking around is not asking for a dance.
        /// </summary>
        private const float StandingSlack = 5f;
        /// <summary>With the player on the move it sets off again after this gap, so it walks in strides, not steps.</summary>
        private const float WalkingSlack = 2f;
        private const float RunDistance = 10f;
        /// <summary>Inside this cone ahead of the player (60 degrees to each side), this close, it is in the view.</summary>
        private const float InViewCos = 0.5f;
        private const float InViewDistance = 4f;
        /// <summary>In the view this long before it steps aside: a player glancing around is not asking for a dance.</summary>
        private const float InViewPatience = 1f;
        private const float LeaderMovingSpeed = 0.5f;
        private const float ScanSeconds = 0.5f;
        /// <summary>An enemy that struck the player is the one to fight for this long.</summary>
        private const float StrikerSeconds = 8f;
        /// <summary>Metres of advantage over a nearer enemy for one going for the player, one that hit the player, the current one.</summary>
        private const float TargetsLeaderBonus = 6f;
        private const float StrikerBonus = 10f;
        private const float KeepTargetBonus = 2f;

        /// <summary>How often a guard looks who else follows its leader, for its place in the formation.</summary>
        private const float SlotSeconds = 0.5f;

        private readonly SettlerAI _ai;
        private readonly Settler _settler;
        private int _slot;
        private float _slotTimer;
        private bool _moving;
        /// <summary>Where the current trip goes: the live spot while the player walks, a fixed one while the player stands.</summary>
        private Vector3 _target;
        private float _inViewSince = -1f;
        private Vector3 _lastLeaderPosition;
        private bool _leaderSeen;
        private float _leaderSpeed;
        private float _scanTimer;
        private Character _striker;
        private float _strikerUntil;

        internal Escort(SettlerAI ai, Settler settler)
        {
            _ai = ai;
            _settler = settler;
        }

        // Its place among the leader's followers, by settler id: the first pair at the shoulders, the next pair a row
        // back and a little farther out (see Formation.EscortSlot), so three or four guards do not fight for two spots.
        private int SlotAmong(Player leader)
        {
            long leaderId = leader.GetPlayerID();
            long me = _settler.Uid;
            int slot = 0;
            foreach (Settler other in Settler.Loaded)
            {
                if (other == null || other == _settler || !other.IsLoaded || other.IsDown || other.FollowedPlayerId != leaderId)
                {
                    continue;
                }
                if (other.Uid < me)
                {
                    slot++;
                }
            }
            return slot;
        }

        /// <summary>Replaces vanilla following (see <see cref="FollowPatch"/>): to the player's shoulder, out of the view.</summary>
        internal void Follow(Player leader, float dt)
        {
            Vector3 me = _ai.transform.position;
            Vector3 at = leader.transform.position;
            if (!_leaderSeen)
            {
                _lastLeaderPosition = at;
                _leaderSeen = true;
            }
            if (dt > 0f)
            {
                _leaderSpeed = Mathf.Lerp(_leaderSpeed, FlatDistance(at, _lastLeaderPosition) / dt, 0.3f);
            }
            _lastLeaderPosition = at;

            Vector3 forward = Flat(leader.transform.forward);
            if (forward.sqrMagnitude < 0.01f)
            {
                forward = Flat(at - me);
            }
            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            _slotTimer -= dt;
            if (_slotTimer <= 0f)
            {
                _slotTimer = SlotSeconds;
                _slot = SlotAmong(leader);
            }
            Formation.EscortSlot(_slot, out float behind, out float sideways);
            Vector3 spot = at - forward * behind + right * sideways;
            float toLeader = FlatDistance(me, at);
            float toSpot = FlatDistance(me, spot);

            Vector3 fromLeader = Flat(me - at);
            bool inView = toLeader < InViewDistance && fromLeader.sqrMagnitude > 0.01f && Vector3.Dot(forward, fromLeader.normalized) > InViewCos;
            if (!inView)
            {
                _inViewSince = -1f;
            }
            else if (_inViewSince < 0f)
            {
                _inViewSince = Time.time;
            }
            bool inTheWay = inView && Time.time - _inViewSince > InViewPatience;
            bool leaderMoving = _leaderSpeed > LeaderMovingSpeed;

            if (!_moving)
            {
                if (toSpot > (leaderMoving ? WalkingSlack : StandingSlack) || inTheWay)
                {
                    _moving = true;
                    _target = spot;
                }
                else
                {
                    _ai.Halt();
                    return;
                }
            }
            else if (leaderMoving || inTheWay)
            {
                // Behind a walking player the spot moves with the player; a standing player's turns do not move the
                // target of a trip under way, or the settler would circle a player who looks around.
                _target = spot;
            }
            // MoveTo stops by itself within half a metre (a metre at a run) or when there is no path.
            if (_ai.MoveToPoint(dt, _target, 0f, toLeader > RunDistance))
            {
                _moving = false;
                _inViewSince = -1f;
            }
        }

        /// <summary>
        /// Owner, before vanilla targeting, while the settler follows and no order stands: the enemy to fight for the
        /// player, if any, and vanilla's own target search held back meanwhile (it would pick the enemy nearest the
        /// settler, however far from the player).
        /// </summary>
        internal void UpdateTargeting(float dt, Player leader)
        {
            if (!_settler.IsAdult)
            {
                _ai.RetargetTo(null);
                return;
            }
            _scanTimer -= dt;
            _ai.HoldVanillaTargeting(ScanSeconds + 0.1f);
            if (_scanTimer > 0f)
            {
                return;
            }
            _scanTimer = ScanSeconds;

            float range = AoJConfig.GuardRange.Value;
            float leash = AoJConfig.GuardLeash.Value;
            Character current = _ai.CurrentTarget;
            Vector3 at = leader.transform.position;
            if (current != null && (current.IsDead() || Vector3.Distance(current.transform.position, at) > leash))
            {
                if (AiTrace.On)
                {
                    AiTrace.Write(_ai, $"guard: lets {current.m_name} go, {(current.IsDead() ? "dead" : "too far from the leader")}");
                }
                _ai.RetargetTo(null);
                current = null;
            }
            if (_striker != null && (_striker.IsDead() || Time.time > _strikerUntil))
            {
                _striker = null;
            }

            Character best = null;
            float bestScore = float.MaxValue;
            List<Character> all = Character.GetAllCharacters();
            for (int i = 0; i < all.Count; i++)
            {
                Character candidate = all[i];
                if (candidate == null || candidate == _ai.Body)
                {
                    continue;
                }
                // Distance before the faction check: the latter is the dear part, and most creatures are far away.
                float distance = Vector3.Distance(candidate.transform.position, at);
                if ((distance > range && candidate != current) || candidate.IsDead() || !BaseAI.IsEnemy(_ai.Body, candidate))
                {
                    continue;
                }
                // A guard does not hunt: a deer or a boar is left alone unless it is the one attacking the player.
                bool animal = candidate.m_faction == Character.Faction.AnimalsVeg;
                if (animal && candidate != _striker && !GoesFor(candidate, leader))
                {
                    continue;
                }
                float score = animal ? distance + range : distance;
                if (candidate == current)
                {
                    score -= KeepTargetBonus;
                }
                if (candidate == _striker)
                {
                    score -= StrikerBonus;
                }
                else if (GoesFor(candidate, leader))
                {
                    score -= TargetsLeaderBonus;
                }
                if (score < bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }
            if (best != null && best != current)
            {
                if (AiTrace.On)
                {
                    AiTrace.Write(_ai, $"guard: {best.m_name} {bestScore:0} m from the leader{(best == _striker ? " (struck the leader)" : GoesFor(best, leader) ? " (going for the leader)" : "")}");
                }
                _ai.RetargetTo(best);
                _ai.SetAlerted(true);
            }
            else if (best == null && current == null)
            {
                // Vanilla's search would have lowered its guard by now; it is held back, so this does.
                _ai.CalmDown();
            }
        }

        /// <summary>The player it follows was struck: that enemy comes first (from the hit, on the player's machine).</summary>
        internal void Defend(Character attacker)
        {
            if (attacker == null || attacker.IsDead() || !BaseAI.IsEnemy(_ai.Body, attacker))
            {
                return;
            }
            _striker = attacker;
            _strikerUntil = Time.time + StrikerSeconds;
            _scanTimer = 0f;
        }

        private static bool GoesFor(Character enemy, Player leader)
        {
            var ai = enemy.GetBaseAI() as MonsterAI;
            return ai != null && ai.GetTargetCreature() == leader;
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        private static float FlatDistance(Vector3 a, Vector3 b) => Flat(a - b).magnitude;

        /// <summary>Vanilla following of a settler goes to the escort; other creatures follow as before.</summary>
        [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.Follow))]
        private static class FollowPatch
        {
            private static bool Prefix(BaseAI __instance, GameObject go, float dt)
            {
                if (!(__instance is SettlerAI ai) || ai.Escort == null || go == null)
                {
                    return true;
                }
                Player leader = go.GetComponent<Player>();
                if (leader == null)
                {
                    return true;
                }
                try
                {
                    ai.Escort.Follow(leader, dt);
                    return false;
                }
                catch (System.Exception e)
                {
                    // Vanilla following instead, this frame; the game's AI loop must not see the exception.
                    if (Time.time >= s_nextErrorLogTime)
                    {
                        s_nextErrorLogTime = Time.time + 30f;
                        Log.Error(Module, $"{ai.name}: guard following failed, vanilla follows instead: {e}");
                    }
                    return true;
                }
            }

            private static float s_nextErrorLogTime;
        }

        /// <summary>A hit on a player (applied on that player's machine): its guards simulated here go for the attacker.</summary>
        [HarmonyPatch(typeof(Character), nameof(Character.RPC_Damage))]
        private static class LeaderHitPatch
        {
            private static void Prefix(Character __instance, HitData hit)
            {
                if (!(__instance is Player leader) || hit == null || hit.m_attacker.IsNone() || ZNetScene.instance == null)
                {
                    return;
                }
                GameObject attackerObject = ZNetScene.instance.FindInstance(hit.m_attacker);
                Character attacker = attackerObject != null ? attackerObject.GetComponent<Character>() : null;
                if (attacker == null)
                {
                    return;
                }
                long leaderId = leader.GetPlayerID();
                foreach (Settler settler in Settler.Loaded)
                {
                    if (settler == null || settler.FollowedPlayerId != leaderId)
                    {
                        continue;
                    }
                    SettlerAI ai = settler.GetComponent<SettlerAI>();
                    ai?.Escort?.Defend(attacker);
                }
            }
        }
    }
}
