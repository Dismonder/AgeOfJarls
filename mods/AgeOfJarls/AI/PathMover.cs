using System.Collections.Generic;
using UnityEngine;

namespace AgeOfJarls.AI
{
    internal enum MoveResult
    {
        Moving,
        /// <summary>Within the stop distance, or at the end of the path and within reach of the goal.</summary>
        Arrived,
        /// <summary>No way to the goal, not even through a door.</summary>
        Blocked,
    }

    /// <summary>
    /// Walks a settler along its own navmesh path: BaseAI keeps a single cached path that vanilla idle and follow
    /// movement recompute every frame, so settler behaviours sharing it would keep resetting each other's route.
    /// A closed door in the way is opened the way a player opens it and closed again once the settler is through.
    /// Doors needing a key, doors that cannot be closed and warded doors the simulating player may not use stay shut.
    /// </summary>
    internal sealed class PathMover
    {
        private const float RepathSeconds = 2f;
        private const float WaypointReached = 0.6f;
        private const float TargetMoved = 1f;
        private const float StuckSeconds = 3f;
        private const float StuckDistance = 0.5f;

        private const float DoorSearchRadius = 10f;
        /// <summary>A settler stuck on its path looks for a closed door this close.</summary>
        private const float StuckDoorRadius = 2.5f;
        private const float DoorReach = 2.2f;
        private const float DoorSwingSeconds = 1.5f;
        /// <summary>After opening, how long to wait for a path: Pathfinding rebuilds navmesh tiles every 5 s.</summary>
        private const float NavmeshWaitSeconds = 7f;
        private const float ThroughRepathSeconds = 1f;
        /// <summary>Past the door by this much, the settler closes it behind itself.</summary>
        private const float DoorClearance = 1.5f;
        private const float DoorRetrySeconds = 60f;
        private const float DoorSearchCooldown = 2f;
        /// <summary>A door opened for a trip that ended just inside is closed once the settler stops walking for this long.</summary>
        private const float AbandonedDoorSeconds = 4f;

        private enum Step
        {
            Moving,
            Arrived,
            Blocked,
            Stuck,
        }

        private enum DoorPhase
        {
            None,
            Approach,
            Swing,
            Through,
        }

        private static readonly List<Piece> s_pieces = new List<Piece>();

        private readonly BaseAI _ai;
        private readonly List<Vector3> _path = new List<Vector3>();
        private readonly Dictionary<Door, float> _doorRetryAt = new Dictionary<Door, float>();

        private Vector3 _target;
        private float _repathTimer;
        private bool _hasPath;
        private bool _pathReaches;
        private Vector3 _progressPoint;
        private float _stuckTimer;

        private Door _door;
        private DoorPhase _doorPhase;
        private float _doorTimer;
        private float _doorSide;
        private bool _openedByUs;
        private float _doorSearchAt;
        private float _lastMoveTime;

        internal PathMover(BaseAI ai)
        {
            _ai = ai;
        }

        /// <summary>For aoj_debug: the current trip, its path and any door on the way.</summary>
        internal string DebugState()
        {
            if (Time.time - _lastMoveTime > 1f)
            {
                return "-";
            }
            string path = !_hasPath ? "no path" : _pathReaches ? $"{_path.Count} pts" : $"{_path.Count} pts, partial";
            string stuck = _stuckTimer > 0.5f ? $", stuck {_stuckTimer:0.0}s" : "";
            string door = _doorPhase != DoorPhase.None ? $", door {_doorPhase}" : "";
            return $"{Vector3.Distance(_ai.transform.position, _target):0} m ({path}{stuck}{door})";
        }

        /// <summary>Every AI frame: closes a door this settler left open once it is through or has stopped using it.</summary>
        internal void Update()
        {
            if (_door == null)
            {
                return;
            }
            Vector3 position = _ai.transform.position;
            CloseDoorBehind(position);
            if (_door != null && Time.time - _lastMoveTime > AbandonedDoorSeconds &&
                Vector3.Distance(position, _door.transform.position) > DoorClearance)
            {
                CloseDoor();
                ForgetDoor();
            }
        }

        internal MoveResult MoveTo(float dt, Vector3 goal, float stopDistance, float reach, bool run)
        {
            _lastMoveTime = Time.time;
            Vector3 position = _ai.transform.position;
            CloseDoorBehind(position);
            if (Utils.DistanceXZ(goal, position) <= stopDistance)
            {
                _ai.StopMoving();
                return MoveResult.Arrived;
            }

            if (_doorPhase == DoorPhase.Approach && ApproachDoor(dt, position, run))
            {
                return MoveResult.Moving;
            }
            if (_doorPhase == DoorPhase.Swing)
            {
                _ai.StopMoving();
                _doorTimer += dt;
                if (_doorTimer < DoorSwingSeconds)
                {
                    return MoveResult.Moving;
                }
                _doorPhase = DoorPhase.Through;
                _doorTimer = 0f;
                _repathTimer = 0f;
            }

            Step step = Follow(dt, position, goal, reach, run, _doorPhase == DoorPhase.Through ? ThroughRepathSeconds : RepathSeconds);
            if (step == Step.Moving)
            {
                return MoveResult.Moving;
            }
            if (step == Step.Arrived)
            {
                return MoveResult.Arrived;
            }

            if (_doorPhase == DoorPhase.Through)
            {
                _doorTimer += dt;
                if (step == Step.Blocked && _doorTimer < NavmeshWaitSeconds)
                {
                    // Opened; the navmesh around the doorway catches up within a few seconds.
                    return MoveResult.Moving;
                }
                GiveUpDoor();
            }
            return TryDoor(position, goal, step == Step.Stuck) ? MoveResult.Moving : MoveResult.Blocked;
        }

        /// <summary>The goal changed or the trip ended: forget the route and close a door left open behind.</summary>
        internal void Reset()
        {
            _path.Clear();
            _hasPath = false;
            _repathTimer = 0f;
            _stuckTimer = 0f;
            if (_door != null && Vector3.Distance(_ai.transform.position, _door.transform.position) > DoorClearance)
            {
                CloseDoor();
            }
            ForgetDoor();
        }

        // ---------------------------------------------------------------- path following

        private Step Follow(float dt, Vector3 position, Vector3 target, float reach, bool run, float repathSeconds)
        {
            if (Utils.DistanceXZ(target, _target) > TargetMoved)
            {
                _repathTimer = 0f;
            }
            _repathTimer -= dt;
            if (_repathTimer <= 0f)
            {
                _repathTimer = repathSeconds;
                _target = target;
                _hasPath = Pathfinding.instance != null && Pathfinding.instance.GetPath(position, target, _path, _ai.m_pathAgentType);
                // A partial path (the goal is behind a wall or a closed door) ends too far from the goal.
                _pathReaches = _hasPath && _path.Count > 0 && Vector3.Distance(_path[_path.Count - 1], target) <= reach;
                _progressPoint = position;
                _stuckTimer = 0f;
            }
            if (!_hasPath || !_pathReaches)
            {
                _ai.StopMoving();
                return Step.Blocked;
            }

            while (_path.Count > 0 && Utils.DistanceXZ(_path[0], position) < WaypointReached)
            {
                _path.RemoveAt(0);
            }
            if (_path.Count == 0)
            {
                _ai.StopMoving();
                return Vector3.Distance(position, target) <= reach ? Step.Arrived : Step.Blocked;
            }

            if (Vector3.Distance(position, _progressPoint) > StuckDistance)
            {
                _progressPoint = position;
                _stuckTimer = 0f;
            }
            else
            {
                _stuckTimer += dt;
                if (_stuckTimer > StuckSeconds)
                {
                    _stuckTimer = 0f;
                    _repathTimer = 0f;
                    _ai.StopMoving();
                    return Step.Stuck;
                }
            }
            _ai.MoveTowards(_path[0] - position, run);
            return Step.Moving;
        }

        // ---------------------------------------------------------------- doors

        private bool TryDoor(Vector3 position, Vector3 goal, bool stuck)
        {
            if (Time.time < _doorSearchAt)
            {
                return false;
            }
            _doorSearchAt = Time.time + DoorSearchCooldown;

            Door door = stuck ? FindDoor(position, position, StuckDoorRadius) : FindDoor(position, goal, DoorSearchRadius);
            if (door == null)
            {
                return false;
            }
            _door = door;
            _openedByUs = false;
            _doorTimer = 0f;
            _doorPhase = DoorPhase.Approach;
            return true;
        }

        // Best closed door around the settler or the goal: the smallest detour through it.
        private Door FindDoor(Vector3 position, Vector3 goal, float radius)
        {
            s_pieces.Clear();
            Piece.GetAllPiecesInRadius(goal, radius, s_pieces);
            if (Vector3.Distance(position, goal) > radius)
            {
                Piece.GetAllPiecesInRadius(position, radius, s_pieces);
            }

            Door best = null;
            float bestCost = float.MaxValue;
            float now = Time.time;
            foreach (Piece piece in s_pieces)
            {
                Door door = piece.GetComponent<Door>();
                if (!IsUsable(door) || IsOpen(door) || (_doorRetryAt.TryGetValue(door, out float retryAt) && now < retryAt))
                {
                    continue;
                }
                Vector3 doorPosition = door.transform.position;
                float cost = Vector3.Distance(position, doorPosition) + Vector3.Distance(doorPosition, goal);
                if (cost < bestCost)
                {
                    best = door;
                    bestCost = cost;
                }
            }
            s_pieces.Clear();
            if (_doorRetryAt.Count > 32)
            {
                _doorRetryAt.Clear();
            }
            return best;
        }

        // True while walking to the door or opening it.
        private bool ApproachDoor(float dt, Vector3 position, bool run)
        {
            if (!IsUsable(_door))
            {
                ForgetDoor();
                return false;
            }
            if (IsOpen(_door))
            {
                // Somebody else opened it meanwhile: go through, but leave closing it to them.
                _doorPhase = DoorPhase.Through;
                _doorTimer = 0f;
                _repathTimer = 0f;
                return false;
            }

            Vector3 doorPosition = _door.transform.position;
            Step step = Follow(dt, position, doorPosition, DoorReach, run, RepathSeconds);
            if (step == Step.Moving)
            {
                return true;
            }
            if (Vector3.Distance(position, doorPosition) > DoorReach)
            {
                GiveUpDoor();
                return false;
            }

            // Door.Interact opens away from the user; the RPC runs on the door's owner like a player's click.
            Vector3 toSettler = position - doorPosition;
            _door.m_nview.InvokeRPC("UseDoor", Vector3.Dot(_door.transform.forward, toSettler) < 0f);
            _doorSide = Mathf.Sign(Vector3.Dot(_door.transform.forward, toSettler));
            _openedByUs = true;
            _doorPhase = DoorPhase.Swing;
            _doorTimer = 0f;
            _ai.StopMoving();
            return true;
        }

        private void CloseDoorBehind(Vector3 position)
        {
            if (_doorPhase != DoorPhase.Through || _door == null)
            {
                return;
            }
            Vector3 fromDoor = position - _door.transform.position;
            if (Mathf.Sign(Vector3.Dot(_door.transform.forward, fromDoor)) != _doorSide && fromDoor.magnitude > DoorClearance)
            {
                CloseDoor();
                ForgetDoor();
            }
        }

        private void GiveUpDoor()
        {
            if (_door != null)
            {
                _doorRetryAt[_door] = Time.time + DoorRetrySeconds;
                CloseDoor();
            }
            ForgetDoor();
        }

        // Only a door this settler opened, and only if it is still open.
        private void CloseDoor()
        {
            if (_openedByUs && IsUsable(_door) && IsOpen(_door))
            {
                _door.m_nview.InvokeRPC("UseDoor", false);
            }
        }

        private void ForgetDoor()
        {
            _door = null;
            _doorPhase = DoorPhase.None;
            _openedByUs = false;
        }

        private static bool IsUsable(Door door) =>
            door != null && door.m_nview != null && door.m_nview.IsValid() && door.m_keyItem == null && !door.m_canNotBeClosed &&
            (!door.m_checkGuardStone || PrivateArea.CheckAccess(door.transform.position, 0f, flash: false));

        private static bool IsOpen(Door door) => door.m_nview.GetZDO().GetInt(ZDOVars.s_state) != 0;
    }
}
