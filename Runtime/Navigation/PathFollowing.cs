#if UEA_NAVIGATION
using System;
using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;
using Object = UnityEngine.Object;

namespace ModernWestern.UEArchitecture
{
    /// <summary>
    /// Moves a NavMeshAgent to a destination and always tells you how the move ended, like Unreal's UPathFollowingComponent.
    /// Every move ends with exactly one MoveCompleted: <see cref="MoveOutcome.Success"/>, Partial, Blocked, Invalid or Aborted.
    /// <code>
    /// pathFollowing.MoveTo(point, this, static (self, result) => self.OnArrived(result));
    /// </code>
    /// <para>Arrival is not "distance below a threshold". A move succeeds when the path to the destination is known to be
    /// complete and the agent stands within the acceptance radius of where that path ends, or cannot get any closer while
    /// already touching it. Paths still being computed, lost (Warp, NavMesh changes), partial (unreachable destinations) or
    /// crossing off-mesh links are handled, and a move that stops making progress ends as Blocked: no move waits forever.</para>
    /// <para>On a Pawn, its controller's MoveTo uses this component (adding it if the Pawn has a NavMeshAgent) and gets
    /// OnMoveCompleted. Moves are ticked by the level's <see cref="NavigationSystem"/>, without allocating.</para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NavMeshAgent))]
    [AddComponentMenu("UE Architecture/Path Following")]
    public sealed class PathFollowing : MonoBehaviour
    {
        private delegate void Invoker(object owner, Delegate callback, MoveResult result);

        private static class Thunk<TOwner> where TOwner : class
        {
            public static readonly Invoker Invoke = static (owner, callback, result) => ((Action<TOwner, MoveResult>)callback)((TOwner)owner, result);
        }

        private enum Phase
        {
            None,
            WaitingForNavMesh,
            WaitingForPath,
            Following
        }

        // Unity drops a finished path within a few millimetres of its end.
        private const float ArrivalTolerance = 0.01f;

        // How long an agent must be almost still before "it can get no closer" counts.
        private const float SettleTime = 0.5f;

        private const float RetargetInterval = 0.1f;

        // Wandering inside a small area for this many Block Times also ends a move as Blocked (agents dancing around each other).
        private const float AreaTimeFactor = 3f;

        private const int MaxRepaths = 3;

        private const int HistorySize = 8;

        internal static readonly List<PathFollowing> Active = new();

        private static int _lastId;

        [SerializeField, Min(0.1f), Tooltip("Seconds without moving Block Distance before a move ends as Blocked.")]
        private float blockTime = 3f;

        [SerializeField, Min(0.01f), Tooltip("How far the agent must move, at least once every Block Time, to count as making progress.")]
        private float blockDistance = 0.1f;

        private readonly MoveResult[] _history = new MoveResult[HistorySize];

        private NavMeshAgent _agent;

        private Pawn _pawn;

        private bool _pawnResolved;

        private int _activeIndex = -1;

        private int _tickFrame = -1;

        private int _historyNext;

        private List<MoveWait> _waits;

        private bool _destroyed;

        private bool _replacing;

        // The current move. _id is 0 when there is none.
        private int _id;

        private Phase _phase;

        private Vector3 _destination;

        private Transform _target;

        private NavMeshAgent _targetAgent;

        private bool _hasTarget;

        private float _targetRadius;

        private float _acceptance;

        private bool _allowPartial;

        private bool _paused;

        private object _callbackOwner;

        private Delegate _callback;

        private Invoker _invoke;

        private bool _callbackOwnerIsUnityObject;

        private double _startTime;

        private int _requestFrame;

        private Vector3 _goal;

        private Vector3 _pathEnd;

        private NavMeshPathStatus _pathStatus;

        private bool _hasPathEnd;

        private bool _confirmingPartial;

        private int _repaths;

        private Vector3 _anchor;

        private float _stillTime;

        private Vector3 _areaAnchor;

        private float _areaTime;

        private float _offMeshTime;

        private Vector3 _lastPosition;

        private Vector3 _lastTargetPoint;

        private float _sinceRetarget;

        // A result decided outside the tick (in MoveTo): delivered on the next tick, never inside MoveTo.
        private bool _hasPending;

        private MoveOutcome _pendingOutcome;

        private MoveReason _pendingReason;

        /// <summary>
        /// Raised once per move, when it ends. Also see the controller's OnMoveCompleted and the callback of MoveTo.
        /// </summary>
        public event Action<MoveResult> MoveCompleted;

        public NavMeshAgent Agent => _agent != null ? _agent : _agent = GetComponent<NavMeshAgent>();

        /// <summary>
        /// The Pawn this component moves, or null if it is on a plain GameObject.
        /// </summary>
        public Pawn Pawn
        {
            get
            {
                if (!_pawnResolved)
                {
                    TryGetComponent(out _pawn);

                    _pawnResolved = true;
                }

                return _pawn != null ? _pawn : null;
            }
        }

        public MoveStatus Status
        {
            get
            {
                if (_id == 0)
                {
                    return MoveStatus.Idle;
                }

                if (_hasPending || _phase != Phase.Following)
                {
                    return MoveStatus.Waiting;
                }

                if (_paused)
                {
                    return MoveStatus.Paused;
                }

                var agent = _agent;

                return agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh && agent.isStopped ? MoveStatus.Paused : MoveStatus.Moving;
            }
        }

        /// <summary>
        /// True from MoveTo until the move ends.
        /// </summary>
        public bool IsMoving => _id != 0;

        /// <summary>
        /// The move in progress, or <see cref="MoveRequest.None"/>.
        /// </summary>
        public MoveRequest CurrentMove => _id != 0 ? new MoveRequest(this, _id) : MoveRequest.None;

        /// <summary>
        /// Where the current move goes (the actor's position for MoveToActor).
        /// </summary>
        public Vector3 Destination => _hasTarget && _target != null ? TargetPoint_Internal() : _destination;

        /// <summary>
        /// The actor the current move goes to, or null.
        /// </summary>
        public Transform Target => _hasTarget && _target != null ? _target : null;

        /// <summary>
        /// How close the current move must get: its acceptance radius or the agent's stopping distance, whichever is larger.
        /// </summary>
        public float AcceptanceRadius => _id != 0 && Agent != null ? EffectiveRadius_Internal(Agent) : 0f;

        /// <summary>
        /// Seconds the current move has gone without making progress (it ends as Blocked at <see cref="BlockTime"/>).
        /// </summary>
        public float SecondsWithoutProgress => _id != 0 ? Mathf.Max(_stillTime, _areaTime / AreaTimeFactor) : 0f;

        /// <summary>
        /// How the last move ended.
        /// </summary>
        public MoveResult LastResult { get; private set; }

        public float BlockTime
        {
            get => blockTime;
            set => blockTime = Mathf.Max(0.1f, value);
        }

        public float BlockDistance
        {
            get => blockDistance;
            set => blockDistance = Mathf.Max(0.01f, value);
        }

        /// <summary>
        /// Moves to a point. It ends when the agent gets within <paramref name="acceptanceRadius"/> (or its stopping
        /// distance, whichever is larger) of the closest walkable point. Replaces the move in progress, which ends as
        /// Aborted (NewRequest) right away; MoveTo calls its handlers make at that moment are ignored, so this move wins.
        /// </summary>
        /// <param name="allowPartial">If the point cannot be reached, go as close as possible (ends as Partial) instead of
        /// failing at once (Invalid, Unreachable).</param>
        public MoveRequest MoveTo(Vector3 destination, float acceptanceRadius = 0f, bool allowPartial = false)
        {
            return Request_Internal(destination, null, acceptanceRadius, allowPartial, null, null, null);
        }

        /// <summary>
        /// Like <see cref="MoveTo(Vector3, float, bool)"/>, calling <paramref name="onCompleted"/> with
        /// <paramref name="owner"/> when it ends. Use a <c>static</c> lambda so nothing is captured. If the owner is a Unity
        /// object that was destroyed, the callback is skipped.
        /// </summary>
        public MoveRequest MoveTo<TOwner>(Vector3 destination, TOwner owner, Action<TOwner, MoveResult> onCompleted, float acceptanceRadius = 0f, bool allowPartial = false) where TOwner : class
        {
            if (onCompleted == null)
            {
                throw new ArgumentNullException(nameof(onCompleted));
            }

            return Request_Internal(destination, null, acceptanceRadius, allowPartial, owner, onCompleted, Thunk<TOwner>.Invoke);
        }

        /// <summary>
        /// Moves to an actor and follows it while it moves. It ends when the agent touches it: within
        /// <paramref name="acceptanceRadius"/> plus both radii. If the actor is destroyed, it ends as Aborted (TargetLost).
        /// </summary>
        public MoveRequest MoveToActor(Component target, float acceptanceRadius = 0f, bool allowPartial = false)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            return Request_Internal(target.transform.position, target.transform, acceptanceRadius, allowPartial, null, null, null);
        }

        /// <summary>
        /// Like <see cref="MoveToActor(Component, float, bool)"/>, calling <paramref name="onCompleted"/> with
        /// <paramref name="owner"/> when it ends.
        /// </summary>
        public MoveRequest MoveToActor<TOwner>(Component target, TOwner owner, Action<TOwner, MoveResult> onCompleted, float acceptanceRadius = 0f, bool allowPartial = false) where TOwner : class
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (onCompleted == null)
            {
                throw new ArgumentNullException(nameof(onCompleted));
            }

            return Request_Internal(target.transform.position, target.transform, acceptanceRadius, allowPartial, owner, onCompleted, Thunk<TOwner>.Invoke);
        }

        /// <summary>
        /// Stops the agent. The move in progress, if any, ends as Aborted (Stopped). Use this, not NavMeshAgent.ResetPath:
        /// a path lost without it is found again.
        /// </summary>
        public void Stop()
        {
            if (_id != 0)
            {
                Finish_Internal(_hasPending ? _pendingOutcome : MoveOutcome.Aborted, _hasPending ? _pendingReason : MoveReason.Stopped, true);

                return;
            }

            var agent = Agent;

            if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh)
            {
                agent.ResetPath();
            }
        }

        /// <summary>
        /// Holds the agent where it is without ending the move (a paused move is never Blocked). Like Unreal's PauseMove.
        /// </summary>
        public void Pause()
        {
            if (_id == 0 || _paused)
            {
                return;
            }

            _paused = true;

            var agent = _agent;

            if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh)
            {
                agent.isStopped = true;
            }
        }

        public void Resume()
        {
            if (!_paused)
            {
                return;
            }

            _paused = false;

            var agent = _agent;

            if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh)
            {
                agent.isStopped = false;
            }
        }

        private void OnDisable()
        {
            Abort_Internal(MoveReason.Disabled);
        }

        private void OnDestroy()
        {
            // From here on MoveTo requests nothing, so no move is left behind that could never end.
            _destroyed = true;

            Abort_Internal(MoveReason.Disabled);

            Unregister_Internal();
        }

        /// <summary>
        /// Where arrival is measured right now (the end of the path, or the actor). For the editor's gizmos.
        /// </summary>
        internal Vector3 ArrivalPoint_Internal => _hasTarget && _target != null ? TargetPoint_Internal() : _hasPathEnd ? _pathEnd : _goal;

        internal bool IsCurrent_Internal(int id)
        {
            return id != 0 && _id == id;
        }

        internal bool TryGetResult_Internal(int id, out MoveResult result)
        {
            foreach (var each in _history)
            {
                if (each.Request.Id == id)
                {
                    result = each;

                    return true;
                }
            }

            result = default;

            return false;
        }

        internal void AddWait_Internal(MoveWait wait)
        {
            (_waits ??= new List<MoveWait>()).Add(wait);
        }

        /// <summary>
        /// Ends the move in progress (if any) as Aborted for this reason, or with the result it already had.
        /// </summary>
        internal void Abort_Internal(MoveReason reason)
        {
            if (_id != 0)
            {
                Finish_Internal(_hasPending ? _pendingOutcome : MoveOutcome.Aborted, _hasPending ? _pendingReason : reason, true);
            }
        }

        internal static void AbortAll_Internal(MoveReason reason)
        {
            for (var i = Active.Count - 1; i >= 0; i--)
            {
                if (i < Active.Count)
                {
                    Active[i].Abort_Internal(reason);
                }
            }
        }

        internal static void Reset_Internal()
        {
            foreach (var pathFollowing in Active)
            {
                pathFollowing._activeIndex = -1;
            }

            Active.Clear();

            _lastId = 0;
        }

        /// <summary>
        /// One frame of the move. Called by the NavigationSystem after every LateUpdate, once the agents moved.
        /// </summary>
        internal void Tick_Internal(float deltaTime, int frame)
        {
            if (_tickFrame == frame)
            {
                return;
            }

            _tickFrame = frame;

            if (_id == 0)
            {
                Unregister_Internal();

                return;
            }

            if (_hasPending)
            {
                Finish_Internal(_pendingOutcome, _pendingReason, true);

                return;
            }

            var agent = _agent;

            if (this == null || agent == null || !isActiveAndEnabled || !agent.isActiveAndEnabled)
            {
                Finish_Internal(MoveOutcome.Aborted, MoveReason.Disabled, true);

                return;
            }

            if (_hasTarget && _target == null)
            {
                Finish_Internal(MoveOutcome.Aborted, MoveReason.TargetLost, true);

                return;
            }

            if (!agent.isOnNavMesh)
            {
                // Maybe the NavMesh is still being built or loaded: give it as long as a blocked move gets.
                _offMeshTime += deltaTime;

                if (_offMeshTime >= blockTime)
                {
                    Finish_Internal(MoveOutcome.Invalid, MoveReason.AgentNotOnNavMesh, false);
                }

                return;
            }

            _offMeshTime = 0f;

            if (_phase == Phase.WaitingForNavMesh)
            {
                StartPath_Internal(agent);

                return;
            }

            var foot = Foot_Internal(agent);

            _lastPosition = foot;

            if (_paused || agent.isStopped)
            {
                Still_Internal(foot);

                return;
            }

            if (_phase == Phase.WaitingForPath)
            {
                // The path is computed on a later frame than SetDestination; until then the agent's path data is stale.
                if (frame == _requestFrame || agent.pathPending)
                {
                    Still_Internal(foot);

                    return;
                }

                if (!PathReady_Internal(agent, foot))
                {
                    return;
                }
            }

            Follow_Internal(agent, foot, deltaTime);
        }

        private MoveRequest Request_Internal(Vector3 destination, Transform target, float acceptance, bool allowPartial, object owner, Delegate callback, Invoker invoke)
        {
            // Being destroyed, or a handler of the move this request is replacing: the request that is replacing it wins.
            if (_destroyed || _replacing)
            {
                return MoveRequest.None;
            }

            if (this == null)
            {
                Debug.LogWarning("[UEArchitecture] MoveTo was called on a destroyed PathFollowing: no move was requested.");

                return MoveRequest.None;
            }

            if (_id != 0)
            {
                _replacing = true;

                try
                {
                    Finish_Internal(_hasPending ? _pendingOutcome : MoveOutcome.Aborted, _hasPending ? _pendingReason : MoveReason.NewRequest, false);
                }
                finally
                {
                    _replacing = false;
                }

                if (_destroyed || this == null)
                {
                    return MoveRequest.None;
                }
            }

            if (_lastId == int.MaxValue)
            {
                _lastId = 0;
            }

            _id = ++_lastId;

            _phase = Phase.None;

            _destination = destination;

            _target = target;

            _hasTarget = target != null;

            _targetAgent = null;

            _targetRadius = _hasTarget ? ActorRadius_Internal(target, out _targetAgent) : 0f;

            _acceptance = Mathf.Max(0f, acceptance);

            _allowPartial = allowPartial;

            _paused = false;

            _callbackOwner = owner;

            _callback = callback;

            _invoke = invoke;

            _callbackOwnerIsUnityObject = owner is Object;

            _startTime = Time.timeAsDouble;

            _offMeshTime = 0f;

            _lastPosition = transform.position;

            _lastTargetPoint = destination;

            var request = new MoveRequest(this, _id);

            Register_Internal();

            var agent = Agent;

            if (!isActiveAndEnabled || agent == null || !agent.isActiveAndEnabled)
            {
                SetPending_Internal(MoveOutcome.Invalid, MoveReason.Disabled);
            }
            else if (World.IsEnding)
            {
                SetPending_Internal(MoveOutcome.Aborted, MoveReason.LevelEnded);
            }
            else if (!IsFinite_Internal(destination))
            {
                SetPending_Internal(MoveOutcome.Invalid, MoveReason.DestinationNotOnNavMesh);
            }
            else if (agent.isOnNavMesh)
            {
                StartPath_Internal(agent);
            }
            else
            {
                _phase = Phase.WaitingForNavMesh;
            }

            return request;
        }

        private void StartPath_Internal(NavMeshAgent agent)
        {
            var foot = Foot_Internal(agent);

            var destination = _hasTarget ? TargetPoint_Internal() : _destination;

            agent.isStopped = _paused;

            _lastPosition = foot;

            _lastTargetPoint = destination;

            _sinceRetarget = 0f;

            _repaths = 0;

            _hasPathEnd = false;

            _confirmingPartial = false;

            Still_Internal(foot);

            if (!agent.SetDestination(destination))
            {
                agent.ResetPath();

                SetPending_Internal(MoveOutcome.Invalid, MoveReason.DestinationNotOnNavMesh);

                return;
            }

            // Right after SetDestination this is the destination projected on the NavMesh (even if it is unreachable).
            _goal = agent.destination;

            _requestFrame = Time.frameCount;

            _phase = Phase.WaitingForPath;

            var radius = EffectiveRadius_Internal(agent);

            var alreadyThere = _hasTarget
                                   ? Within_Internal(agent, foot, destination, radius + agent.radius + _targetRadius, TargetHeight_Internal(agent))
                                   : Within_Internal(agent, foot, _goal, radius, PointHeight_Internal(agent)) && !NavMesh.Raycast(foot, _goal, out _, Filter_Internal(agent));

            if (alreadyThere)
            {
                agent.ResetPath();

                SetPending_Internal(MoveOutcome.Success, MoveReason.AlreadyThere);
            }
        }

        // The path just finished computing. True to go on following it in this same tick.
        private bool PathReady_Internal(NavMeshAgent agent, Vector3 foot)
        {
            var status = agent.pathStatus;

            var radius = EffectiveRadius_Internal(agent);

            if (!agent.hasPath)
            {
                // Nothing to walk: it is already there, it can get no closer (partial), or nothing is reachable.
                var goal = _hasTarget ? TargetPoint_Internal() : _goal;

                var near = _hasTarget ? radius + agent.radius + _targetRadius : Mathf.Max(radius, agent.radius);

                var height = _hasTarget ? TargetHeight_Internal(agent) : PointHeight_Internal(agent);

                if (status != NavMeshPathStatus.PathInvalid && Within_Internal(agent, foot, goal, near, height))
                {
                    Finish_Internal(MoveOutcome.Success, MoveReason.None, true);
                }
                else if (status == NavMeshPathStatus.PathPartial || _confirmingPartial)
                {
                    if (_hasTarget && Within_Internal(agent, foot, goal, near + agent.radius, height))
                    {
                        Finish_Internal(MoveOutcome.Success, MoveReason.None, true);
                    }
                    else
                    {
                        FinishPartial_Internal();
                    }
                }
                else
                {
                    Finish_Internal(MoveOutcome.Invalid, MoveReason.Unreachable, true);
                }

                return false;
            }

            if (status == NavMeshPathStatus.PathInvalid)
            {
                Finish_Internal(MoveOutcome.Invalid, MoveReason.Unreachable, true);

                return false;
            }

            _pathEnd = agent.pathEndPosition;

            _pathStatus = status;

            _hasPathEnd = true;

            if (status == NavMeshPathStatus.PathPartial)
            {
                // An actor is often an obstacle itself (a chest, a door), so its path is partial: it ends by touching it.
                if (!_allowPartial && !_hasTarget)
                {
                    Finish_Internal(MoveOutcome.Invalid, MoveReason.Unreachable, true);

                    return false;
                }

                if (_hasTarget && Within_Internal(agent, foot, TargetPoint_Internal(), radius + agent.radius + _targetRadius, TargetHeight_Internal(agent)))
                {
                    Finish_Internal(MoveOutcome.Success, MoveReason.None, true);

                    return false;
                }

                // Asked again from the end of a partial path, and the end did not move: this is as close as it gets.
                if (_confirmingPartial && Within_Internal(agent, foot, _pathEnd, radius + agent.radius, PointHeight_Internal(agent)))
                {
                    // Next to an actor that is an obstacle, the NavMesh's edge is a bit more than a radius away from it.
                    if (_hasTarget && Within_Internal(agent, foot, TargetPoint_Internal(), radius + 2f * agent.radius + _targetRadius, TargetHeight_Internal(agent)))
                    {
                        Finish_Internal(MoveOutcome.Success, MoveReason.None, true);
                    }
                    else
                    {
                        FinishPartial_Internal();
                    }

                    return false;
                }
            }

            _confirmingPartial = false;

            _phase = Phase.Following;

            return true;
        }

        private void Follow_Internal(NavMeshAgent agent, Vector3 foot, float deltaTime)
        {
            var pending = agent.pathPending;

            var hasPath = agent.hasPath;

            if (!pending && hasPath)
            {
                // Unity may repath by itself (stale paths): keep the end and status of the path being followed.
                _pathEnd = agent.pathEndPosition;

                _pathStatus = agent.pathStatus;

                _hasPathEnd = true;

                if (_pathStatus == NavMeshPathStatus.PathPartial && !_allowPartial && !_hasTarget)
                {
                    Finish_Internal(MoveOutcome.Invalid, MoveReason.Unreachable, true);

                    return;
                }
            }

            if (agent.isOnOffMeshLink)
            {
                // Mid-link positions say nothing about arrival. A link that is never finished still ends as Blocked.
                Progress_Internal(agent, foot, deltaTime);

                if (_stillTime >= blockTime || _areaTime >= blockTime * AreaTimeFactor)
                {
                    Finish_Internal(MoveOutcome.Blocked, MoveReason.None, true);
                }

                return;
            }

            var radius = EffectiveRadius_Internal(agent);

            var height = PointHeight_Internal(agent);

            var targetPoint = Vector3.zero;

            var reach = radius;

            if (_hasTarget)
            {
                targetPoint = TargetPoint_Internal();

                reach = radius + agent.radius + _targetRadius;

                if (Within_Internal(agent, foot, targetPoint, reach, TargetHeight_Internal(agent)))
                {
                    Finish_Internal(MoveOutcome.Success, MoveReason.None, true);

                    return;
                }

                _sinceRetarget += deltaTime;

                var retargetDistance = Mathf.Max(0.1f, reach * 0.5f);

                if (!pending && _sinceRetarget >= RetargetInterval && (targetPoint - _lastTargetPoint).sqrMagnitude > retargetDistance * retargetDistance)
                {
                    // Keeps walking the current path while the new one is computed. If the actor left the NavMesh, keep it.
                    _sinceRetarget = 0f;

                    _lastTargetPoint = targetPoint;

                    if (agent.SetDestination(targetPoint))
                    {
                        _goal = agent.destination;
                    }

                    return;
                }
            }
            else if (!pending && _hasPathEnd && _pathStatus == NavMeshPathStatus.PathComplete && Within_Internal(agent, foot, _pathEnd, radius, height))
            {
                Finish_Internal(MoveOutcome.Success, MoveReason.None, true);

                return;
            }

            if (!pending && _hasPathEnd && _pathStatus == NavMeshPathStatus.PathPartial && Within_Internal(agent, foot, _pathEnd, radius, height))
            {
                ConfirmPartial_Internal(agent);

                return;
            }

            if (!pending && !hasPath)
            {
                // Unity drops a path a few millimetres before its end, but also when the agent is warped or the NavMesh changes.
                if (_hasPathEnd && Within_Internal(agent, foot, _pathEnd, Mathf.Max(radius, agent.radius), height))
                {
                    if (_pathStatus == NavMeshPathStatus.PathPartial)
                    {
                        ConfirmPartial_Internal(agent);

                        return;
                    }

                    if (!_hasTarget)
                    {
                        Finish_Internal(MoveOutcome.Success, MoveReason.None, true);

                        return;
                    }
                }

                Repath_Internal(agent);

                return;
            }

            var still = Progress_Internal(agent, foot, deltaTime);

            // Moving, but never leaving a small area: dancing with another agent, circling. No progress either.
            if (_areaTime >= blockTime * AreaTimeFactor)
            {
                Finish_Internal(MoveOutcome.Blocked, MoveReason.None, true);

                return;
            }

            if (!still)
            {
                return;
            }

            // It can get no closer, but its body already covers the end (another agent standing there, a crowd pushing).
            if (_stillTime >= SettleTime && !pending && _hasPathEnd && IsAlmostStill_Internal(agent, foot) && IsOnLastSegment_Internal(agent, hasPath))
            {
                if (_hasTarget)
                {
                    if (Within_Internal(agent, foot, targetPoint, reach + agent.radius, TargetHeight_Internal(agent)))
                    {
                        Finish_Internal(MoveOutcome.Success, MoveReason.None, true);

                        return;
                    }
                }
                else if (Within_Internal(agent, foot, _pathEnd, radius + agent.radius, height))
                {
                    if (_pathStatus == NavMeshPathStatus.PathPartial)
                    {
                        ConfirmPartial_Internal(agent);
                    }
                    else
                    {
                        Finish_Internal(MoveOutcome.Success, MoveReason.None, true);
                    }

                    return;
                }
            }

            if (_stillTime >= blockTime)
            {
                Finish_Internal(MoveOutcome.Blocked, MoveReason.None, true);
            }
        }

        // True while it has not moved Block Distance from the last anchor (then _stillTime grows). Also measures how long
        // it has stayed inside a small area (_areaTime), however much it moves in there.
        private bool Progress_Internal(NavMeshAgent agent, Vector3 foot, float deltaTime)
        {
            var area = Mathf.Max(blockDistance * 5f, agent.radius * 2f);

            if ((foot - _areaAnchor).sqrMagnitude >= area * area)
            {
                _areaAnchor = foot;

                _areaTime = 0f;
            }
            else
            {
                _areaTime += deltaTime;
            }

            if ((foot - _anchor).sqrMagnitude >= blockDistance * blockDistance)
            {
                _anchor = foot;

                _stillTime = 0f;

                _repaths = 0;

                return false;
            }

            _stillTime += deltaTime;

            return true;
        }

        private void Still_Internal(Vector3 foot)
        {
            _anchor = foot;

            _stillTime = 0f;

            _areaAnchor = foot;

            _areaTime = 0f;
        }

        // Moving at under a tenth of its speed since the last anchor: stopped or jittering, not just walking slowly.
        private bool IsAlmostStill_Internal(NavMeshAgent agent, Vector3 foot)
        {
            return _stillTime > 0f && (foot - _anchor).magnitude / _stillTime < agent.speed * 0.1f;
        }

        private bool IsOnLastSegment_Internal(NavMeshAgent agent, bool hasPath)
        {
            return !hasPath || (agent.steeringTarget - _pathEnd).sqrMagnitude < 0.0001f;
        }

        // At the end of a partial path: ask again once. If the end stays put, it ends as Partial (see PathReady_Internal).
        private void ConfirmPartial_Internal(NavMeshAgent agent)
        {
            if (!SetDestination_Internal(agent))
            {
                return;
            }

            _confirmingPartial = true;
        }

        // The path was lost (Warp, NavMesh changed): find a new one, a few times at most without progress.
        private void Repath_Internal(NavMeshAgent agent)
        {
            if (++_repaths > MaxRepaths)
            {
                Finish_Internal(MoveOutcome.Invalid, MoveReason.PathLost, true);

                return;
            }

            if (SetDestination_Internal(agent))
            {
                _confirmingPartial = false;
            }
        }

        private bool SetDestination_Internal(NavMeshAgent agent)
        {
            var destination = _hasTarget ? TargetPoint_Internal() : _destination;

            if (!agent.SetDestination(destination))
            {
                Finish_Internal(MoveOutcome.Invalid, MoveReason.DestinationNotOnNavMesh, true);

                return false;
            }

            _goal = agent.destination;

            _lastTargetPoint = destination;

            _sinceRetarget = 0f;

            _requestFrame = Time.frameCount;

            _phase = Phase.WaitingForPath;

            return true;
        }

        // As close as it gets to a destination it cannot reach.
        private void FinishPartial_Internal()
        {
            Finish_Internal(_allowPartial ? MoveOutcome.Partial : MoveOutcome.Invalid, _allowPartial ? MoveReason.None : MoveReason.Unreachable, true);
        }

        private void SetPending_Internal(MoveOutcome outcome, MoveReason reason)
        {
            _hasPending = true;

            _pendingOutcome = outcome;

            _pendingReason = reason;
        }

        private void Finish_Internal(MoveOutcome outcome, MoveReason reason, bool stopAgent)
        {
            if (_id == 0)
            {
                return;
            }

            var alive = this != null;

            var agent = alive ? _agent : null;

            var usable = agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh;

            var position = usable ? Foot_Internal(agent) : _lastPosition;

            var destination = _hasTarget ? (_target != null ? TargetPoint_Internal() : _lastTargetPoint) : _destination;

            var result = new MoveResult(new MoveRequest(this, _id), outcome, reason, destination, position, (float)(Time.timeAsDouble - _startTime));

            var owner = _callbackOwner;

            var callback = _callback;

            var invoke = _invoke;

            var ownerIsUnityObject = _callbackOwnerIsUnityObject;

            var wasPaused = _paused;

            // The move is over before anyone hears about it, so a handler can request the next one right away.
            Clear_Internal();

            _history[_historyNext] = result;

            _historyNext = (_historyNext + 1) % HistorySize;

            LastResult = result;

            Unregister_Internal();

            if (usable)
            {
                if (stopAgent)
                {
                    agent.ResetPath();
                }

                if (wasPaused)
                {
                    agent.isStopped = false;
                }
            }

            // Who asked, then the controller, then everyone listening, then whoever waits.
            if (callback != null && !(ownerIsUnityObject && (Object)owner == null))
            {
                try
                {
                    invoke(owner, callback, result);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }

            var pawn = alive ? Pawn : null;

            var controller = pawn != null ? pawn.Controller : null;

            if (controller != null)
            {
                try
                {
                    controller.MoveCompleted_Internal(result);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, controller);
                }
            }

            try
            {
                MoveCompleted?.Invoke(result);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }

            CompleteWaits_Internal(result);
        }

        private void CompleteWaits_Internal(MoveResult result)
        {
            if (_waits == null)
            {
                return;
            }

            for (var i = _waits.Count - 1; i >= 0; i--)
            {
                if (i >= _waits.Count || _waits[i].Request.Id != result.Request.Id)
                {
                    continue;
                }

                var wait = _waits[i];

                _waits.RemoveAt(i);

                wait.Complete_Internal(result);
            }
        }

        private void Clear_Internal()
        {
            _id = 0;

            _phase = Phase.None;

            _hasPending = false;

            _target = null;

            _targetAgent = null;

            _hasTarget = false;

            _paused = false;

            _confirmingPartial = false;

            _callbackOwner = null;

            _callback = null;

            _invoke = null;

            _callbackOwnerIsUnityObject = false;
        }

        private void Register_Internal()
        {
            if (_activeIndex >= 0)
            {
                return;
            }

            _activeIndex = Active.Count;

            Active.Add(this);
        }

        private void Unregister_Internal()
        {
            if (_activeIndex < 0)
            {
                return;
            }

            if (_activeIndex >= Active.Count || !ReferenceEquals(Active[_activeIndex], this))
            {
                _activeIndex = -1;

                return;
            }

            var last = Active.Count - 1;

            var moved = Active[last];

            Active[_activeIndex] = moved;

            moved._activeIndex = _activeIndex;

            Active.RemoveAt(last);

            _activeIndex = -1;
        }

        private float EffectiveRadius_Internal(NavMeshAgent agent)
        {
            return Mathf.Max(_acceptance, agent.stoppingDistance) + ArrivalTolerance;
        }

        private Vector3 TargetPoint_Internal()
        {
            var targetAgent = _targetAgent;

            if (targetAgent != null && targetAgent.isActiveAndEnabled)
            {
                return Foot_Internal(targetAgent);
            }

            return _target.position;
        }

        // How far from its pivot an actor can be touched: its agent's radius, its obstacle's or its renderer's half width.
        private static float ActorRadius_Internal(Transform target, out NavMeshAgent targetAgent)
        {
            if (target.TryGetComponent(out targetAgent))
            {
                return targetAgent.radius;
            }

            var scale = target.lossyScale;

            if (target.TryGetComponent(out NavMeshObstacle obstacle))
            {
                return obstacle.shape == NavMeshObstacleShape.Capsule
                           ? obstacle.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z))
                           : Mathf.Max(obstacle.size.x * Mathf.Abs(scale.x), obstacle.size.z * Mathf.Abs(scale.z)) * 0.5f;
            }

            if (target.TryGetComponent(out Renderer renderer))
            {
                var extents = renderer.bounds.extents;

                return Mathf.Max(extents.x, extents.z);
            }

            return 0f;
        }

        // Where the agent stands on the NavMesh: its simulated position (it may differ from the Transform with root motion)
        // without the base offset.
        private static Vector3 Foot_Internal(NavMeshAgent agent)
        {
            return agent.nextPosition - agent.transform.up * agent.baseOffset;
        }

        private static bool Within_Internal(NavMeshAgent agent, Vector3 foot, Vector3 point, float radius, float height)
        {
            var up = agent.transform.up;

            var delta = point - foot;

            var vertical = Vector3.Dot(delta, up);

            var horizontal = delta - up * vertical;

            return horizontal.sqrMagnitude <= radius * radius && Mathf.Abs(vertical) <= height;
        }

        private static float PointHeight_Internal(NavMeshAgent agent)
        {
            return Mathf.Max(agent.height * 0.5f, 0.3f);
        }

        // An actor's pivot may be anywhere on it (a crate on a table, a character's chest).
        private static float TargetHeight_Internal(NavMeshAgent agent)
        {
            return Mathf.Max(agent.height, 1f);
        }

        private static NavMeshQueryFilter Filter_Internal(NavMeshAgent agent)
        {
            return new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
        }

        private static bool IsFinite_Internal(Vector3 point)
        {
            return !float.IsNaN(point.x) && !float.IsNaN(point.y) && !float.IsNaN(point.z) && !float.IsInfinity(point.x) && !float.IsInfinity(point.y) && !float.IsInfinity(point.z);
        }
    }
}
#endif
