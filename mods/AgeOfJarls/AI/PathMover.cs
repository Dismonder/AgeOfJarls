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
        /// <summary>Everyday trips are walked; a far goal is jogged to, walking again for the last stretch.</summary>
        private const float JogAbove = 20f;
        private const float WalkBelow = 12f;
        /// <summary>Not getting this much closer to the current waypoint for this long means stuck, however much it moves.</summary>
        private const float WaypointProgress = 0.3f;
        private const float NoProgressSeconds = 6f;

        private const float SameWaypoint = 0.1f;

        /// <summary>
        /// The navmesh of an area nobody walked lately is built on demand, a tile at a time, from the first path asked
        /// for (a far work zone, a chest across the settlement): a goal counts as unreachable only after it stayed so
        /// for this long, asked again every <see cref="NoPathRepathSeconds"/>.
        /// </summary>
        private const float NoPathGraceSeconds = 4f;
        private const float NoPathRepathSeconds = 1f;
        private float _noPathTimer;

        private Vector3 _waypointPoint;
        private float _waypointBest = float.MaxValue;
        private float _waypointTimer;

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

        private bool _jogging;
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
            float distance = Utils.DistanceXZ(goal, position);
            if (distance <= stopDistance)
            {
                _ai.StopMoving();
                _jogging = false;
                return MoveResult.Arrived;
            }
            _jogging = distance > JogAbove || (_jogging && distance > WalkBelow);
            run = run || _jogging;

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
            if (step == Step.Moving || step == Step.Arrived)
            {
                _noPathTimer = 0f;
                return step == Step.Moving ? MoveResult.Moving : MoveResult.Arrived;
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
            if (TryDoor(position, goal, step == Step.Stuck))
            {
                return MoveResult.Moving;
            }
            if (step == Step.Stuck && _stuckCount < StuckBeforeHop)
            {
                // Not given up at the first stall (another settler in the way, a crowded corner by the table): a fresh
                // path is on its way, and slipping past furniture, then the step over, get their turn (see Unstick).
                return MoveResult.Moving;
            }
            if (step == Step.Blocked)
            {
                // No way yet and no door to open: it waits for the navmesh a moment (standing, see Follow).
                _noPathTimer += dt;
                if (_noPathTimer < NoPathGraceSeconds)
                {
                    return MoveResult.Moving;
                }
            }
            return MoveResult.Blocked;
        }

        /// <summary>The goal changed or the trip ended: forget the route and close a door left open behind.</summary>
        internal void Reset()
        {
            _path.Clear();
            _hasPath = false;
            _repathTimer = 0f;
            _stuckTimer = 0f;
            _noPathTimer = 0f;
            _jogging = false;
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
                // Another goal (or it moved): a path right away, and a fresh wait for one if there is none yet.
                _repathTimer = 0f;
                _noPathTimer = 0f;
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
                if (AiTrace.On && !_pathReaches)
                {
                    AiTrace.Write(_ai, $"no path to a goal {Vector3.Distance(position, target):0} m away ({(_hasPath ? "partial" : "none")})");
                }
            }
            if (!_hasPath || !_pathReaches)
            {
                // Asked again soon: the navmesh there may still be being built (see NoPathGraceSeconds).
                _ai.StopMoving();
                _repathTimer = Mathf.Min(_repathTimer, NoPathRepathSeconds);
                return Step.Blocked;
            }

            // A corner counts as reached once passed: cutting it (see below) may never bring the settler within
            // WaypointReached of it, and it would turn back for it.
            // Corners are walked to, never cut: they lie right at the corners of what the path goes around (a table, a
            // door frame), and cutting them walks the settler into it. The character turns gradually on its own.
            while (_path.Count > 0 && Utils.DistanceXZ(_path[0], position) < WaypointReached)
            {
                _path.RemoveAt(0);
            }
            if (_path.Count == 0)
            {
                _ai.StopMoving();
                return Vector3.Distance(position, target) <= reach ? Step.Arrived : Step.Blocked;
            }

            // Moving is not progress: circling or pacing near a waypoint without getting closer is stuck too. Measured
            // per waypoint; a repath that leads somewhere else starts over.
            if (Utils.DistanceXZ(_path[0], _waypointPoint) > SameWaypoint)
            {
                _waypointPoint = _path[0];
                _waypointBest = float.MaxValue;
                _waypointTimer = 0f;
            }
            float toWaypoint = Utils.DistanceXZ(_path[0], position);
            if (toWaypoint < _waypointBest - WaypointProgress)
            {
                _waypointBest = toWaypoint;
                _waypointTimer = 0f;
            }
            else
            {
                _waypointTimer += dt;
            }

            if (Vector3.Distance(position, _progressPoint) > StuckDistance && _waypointTimer <= NoProgressSeconds)
            {
                _progressPoint = position;
                _stuckTimer = 0f;
            }
            else
            {
                _stuckTimer += dt;
                if (_stuckTimer > StuckSeconds || _waypointTimer > NoProgressSeconds)
                {
                    _waypointBest = float.MaxValue;
                    _waypointTimer = 0f;
                    if (AiTrace.On)
                    {
                        AiTrace.Write(_ai, $"stuck, {Vector3.Distance(position, target):0} m from the goal, next waypoint " +
                                           $"{Utils.DistanceXZ(_path[0], position):0.0} m away{DescribeSurroundings(position)}");
                    }
                    _stuckTimer = 0f;
                    _repathTimer = 0f;
                    if (Unstick(position))
                    {
                        return Step.Moving;
                    }
                    _ai.StopMoving();
                    return Step.Stuck;
                }
            }
            _ai.MoveTowards(_path[0] - position, run);
            return Step.Moving;
        }

        // Stuck again and again on one spot. The second time, a settler wedged against furniture (the bed it got up from,
        // a chest, a bench) slips past what it touches until it is clear of it - walls, floors and roofs stay solid.
        // From the third time, with the way on right next to it, it takes a short step over to that waypoint, which
        // lies on the navmesh: at most MaxHop, so never through a wall and never across the world like a teleport.
        private const int StuckBeforeSlip = 2;
        private const int StuckBeforeHop = 3;
        private const float MaxHop = 2f;
        private const float SameStuckSpot = 1f;
        private Vector3 _stuckAt;
        private int _stuckCount;
        private static int s_pieceMask;

        private bool Unstick(Vector3 position)
        {
            if (Vector3.Distance(position, _stuckAt) < SameStuckSpot)
            {
                _stuckCount++;
            }
            else
            {
                _stuckAt = position;
                _stuckCount = 1;
            }
            if (_stuckCount == StuckBeforeSlip && SlipPastFurniture(position))
            {
                return true;
            }
            if (_stuckCount < StuckBeforeHop || _path.Count == 0 || Utils.DistanceXZ(_path[0], position) > MaxHop)
            {
                return false;
            }
            _stuckCount = 0;
            Vector3 hop = _path[0] + Vector3.up * 0.1f;
            if (AiTrace.On)
            {
                AiTrace.Write(_ai, $"stuck {StuckBeforeHop} times here, steps over to the next waypoint {Utils.DistanceXZ(hop, position):0.0} m away");
            }
            _ai.transform.position = hop;
            Rigidbody body = _ai.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.position = hop;
                body.linearVelocity = Vector3.zero;
            }
            return true;
        }

        private bool SlipPastFurniture(Vector3 position)
        {
            if (!(_ai.GetComponent<Character>() is Settlers.SettlerCharacter body))
            {
                return false;
            }
            if (s_pieceMask == 0)
            {
                s_pieceMask = LayerMask.GetMask("piece", "piece_nonsolid");
            }
            int count = Physics.OverlapSphereNonAlloc(position + Vector3.up * 0.9f, 1f, s_near, s_pieceMask);
            var furniture = new List<Collider>();
            for (int i = 0; i < count; i++)
            {
                Piece piece = s_near[i] != null ? s_near[i].GetComponentInParent<Piece>() : null;
                if (piece != null && IsFurniture(piece))
                {
                    furniture.Add(s_near[i]);
                }
            }
            if (furniture.Count == 0)
            {
                return false;
            }
            body.PassThrough(furniture, position);
            if (AiTrace.On)
            {
                AiTrace.Write(_ai, $"wedged: slips past {furniture.Count} piece(s) of furniture");
            }
            return true;
        }

        private static bool IsFurniture(Piece piece) =>
            piece.m_category == Piece.PieceCategory.Furniture || piece.m_comfort > 0 || piece.GetComponent<Bed>() != null ||
            piece.GetComponent<Container>() != null || piece.GetComponent<CraftingStation>() != null;

        // For aoj_trace: what a stuck settler touches and the nearest door, to tell a wall from a shut door.
        private static readonly Collider[] s_near = new Collider[16];

        private string DescribeSurroundings(Vector3 position)
        {
            int count = Physics.OverlapSphereNonAlloc(position + Vector3.up, 1.2f, s_near);
            var names = new List<string>();
            for (int i = 0; i < count && names.Count < 6; i++)
            {
                Collider collider = s_near[i];
                if (collider == null || collider.attachedRigidbody != null && collider.attachedRigidbody.gameObject == _ai.gameObject)
                {
                    continue;
                }
                string name = Utils.GetPrefabName(collider.transform.root.gameObject) + "/" + LayerMask.LayerToName(collider.gameObject.layer);
                if (!names.Contains(name))
                {
                    names.Add(name);
                }
            }
            s_pieces.Clear();
            Piece.GetAllPiecesInRadius(position, DoorSearchRadius, s_pieces);
            Door nearest = null;
            float nearestDistance = float.MaxValue;
            foreach (Piece piece in s_pieces)
            {
                Door door = piece.GetComponent<Door>();
                float distance = door != null ? Vector3.Distance(door.transform.position, position) : float.MaxValue;
                if (distance < nearestDistance)
                {
                    nearest = door;
                    nearestDistance = distance;
                }
            }
            s_pieces.Clear();
            string doorText = nearest == null ? "no door within 10 m"
                : $"door {nearestDistance:0.0} m {(IsUsable(nearest) ? (IsOpen(nearest) ? "open" : "closed") : "not usable")}";
            return $"; touching: {string.Join(", ", names)}; {doorText}";
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
