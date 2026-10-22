#if UEA_NAVIGATION
using System;
using UnityEngine;
using System.Runtime.CompilerServices;

namespace ModernWestern.UEArchitecture
{
    /// <summary>
    /// What a <see cref="PathFollowing"/> is doing, like Unreal's EPathFollowingStatus.
    /// </summary>
    public enum MoveStatus
    {
        /// <summary>
        /// No move in progress.
        /// </summary>
        Idle,

        /// <summary>
        /// A move was requested and its path is being computed (or the agent is not on the NavMesh yet).
        /// </summary>
        Waiting,

        /// <summary>
        /// Following its path.
        /// </summary>
        Moving,

        /// <summary>
        /// Paused with <see cref="PathFollowing.Pause"/> (or NavMeshAgent.isStopped).
        /// </summary>
        Paused
    }

    /// <summary>
    /// How a move ended, like Unreal's EPathFollowingResult. Every move ends with exactly one.
    /// </summary>
    public enum MoveOutcome
    {
        /// <summary>
        /// It reached the destination (within the acceptance radius, or the agent's stopping distance).
        /// </summary>
        Success,

        /// <summary>
        /// The destination cannot be reached and it got as close as possible. Only when the move allows partial paths.
        /// </summary>
        Partial,

        /// <summary>
        /// It stopped making progress (stuck against something, pushed back, never finished an off-mesh link).
        /// </summary>
        Blocked,

        /// <summary>
        /// It could not move there: no path, nothing walkable near the destination, or the agent is not on a NavMesh.
        /// </summary>
        Invalid,

        /// <summary>
        /// Something ended it first: a new move, a stop, the agent was disabled or destroyed, the level ended, the Pawn
        /// changed controller or the actor it was moving to was destroyed.
        /// </summary>
        Aborted
    }

    /// <summary>
    /// Why a move ended the way it did. <see cref="None"/> when the outcome says it all.
    /// </summary>
    public enum MoveReason
    {
        None,

        /// <summary>
        /// Success: it was already there when the move was requested.
        /// </summary>
        AlreadyThere,

        /// <summary>
        /// Aborted: another move was requested.
        /// </summary>
        NewRequest,

        /// <summary>
        /// Aborted: <see cref="PathFollowing.Stop"/> or StopMovement was called.
        /// </summary>
        Stopped,

        /// <summary>
        /// Aborted: the Pawn was possessed by another controller or unpossessed.
        /// </summary>
        ControllerChanged,

        /// <summary>
        /// Aborted (or Invalid, when requested like that): the agent, its PathFollowing or its GameObject was disabled or destroyed.
        /// </summary>
        Disabled,

        /// <summary>
        /// Aborted: the level ended.
        /// </summary>
        LevelEnded,

        /// <summary>
        /// Aborted: the actor it was moving to was destroyed.
        /// </summary>
        TargetLost,

        /// <summary>
        /// Invalid: the agent is not on a NavMesh (none was built or loaded where it stands).
        /// </summary>
        AgentNotOnNavMesh,

        /// <summary>
        /// Invalid: there is nothing walkable near the destination.
        /// </summary>
        DestinationNotOnNavMesh,

        /// <summary>
        /// Invalid: there is no path to the destination (and the move does not allow partial paths).
        /// </summary>
        Unreachable,

        /// <summary>
        /// Invalid: the path kept getting lost (the NavMesh changed under the agent, or it was moved by hand) and no new
        /// one could take it any closer.
        /// </summary>
        PathLost,

        /// <summary>
        /// Invalid: the move could not be requested (no Pawn, or no NavMeshAgent). Only seen when waiting on <see cref="MoveRequest.None"/>.
        /// </summary>
        NoAgent,

        /// <summary>
        /// The result is no longer known: asked about long after the move ended and many others followed.
        /// </summary>
        Unknown
    }

    /// <summary>
    /// How a move ended: what, why, where and how long it took.
    /// </summary>
    public readonly struct MoveResult
    {
        internal MoveResult(MoveRequest request, MoveOutcome outcome, MoveReason reason, Vector3 destination, Vector3 position, float duration)
        {
            Request = request;

            Outcome = outcome;

            Reason = reason;

            Destination = destination;

            Position = position;

            Duration = duration;
        }

        public MoveRequest Request { get; }

        public MoveOutcome Outcome { get; }

        public MoveReason Reason { get; }

        /// <summary>
        /// Where it was asked to go (for MoveToActor, where the actor was at the end).
        /// </summary>
        public Vector3 Destination { get; }

        /// <summary>
        /// Where the agent stood on the NavMesh when the move ended.
        /// </summary>
        public Vector3 Position { get; }

        /// <summary>
        /// Seconds from the request to the end (scaled time).
        /// </summary>
        public float Duration { get; }

        public bool IsSuccess => Outcome == MoveOutcome.Success;

        public PathFollowing PathFollowing => Request.PathFollowing;

        /// <summary>
        /// The Pawn that moved, or null if the agent is not a Pawn.
        /// </summary>
        public Pawn Pawn => Request.PathFollowing != null ? Request.PathFollowing.Pawn : null;

        public override string ToString()
        {
            return Reason == MoveReason.None ? Outcome.ToString() : Outcome + " (" + Reason + ")";
        }
    }

    /// <summary>
    /// A move requested with MoveTo. Keep it to know if that move is still running, stop it or wait for it:
    /// <code>
    /// ai.MoveTo(point, this, static (self, result) => self.OnArrived(result));   // callback, allocates nothing
    /// yield return ai.MoveTo(point).Wait();                                       // coroutine
    /// var result = await ai.MoveTo(point);                                        // async method
    /// </code>
    /// </summary>
    public readonly struct MoveRequest : IEquatable<MoveRequest>
    {
        /// <summary>
        /// No move: what MoveTo returns when it cannot request one. The Pawn is missing or has no NavMeshAgent (the reason
        /// was logged), the PathFollowing was destroyed, or the call came from a handler of the move being replaced by
        /// another MoveTo (that other move wins). It never raises MoveCompleted.
        /// </summary>
        public static readonly MoveRequest None = default;

        internal MoveRequest(PathFollowing pathFollowing, int id)
        {
            PathFollowing = pathFollowing;

            Id = id;
        }

        public PathFollowing PathFollowing { get; }

        /// <summary>
        /// Unique per move, 0 for <see cref="None"/>.
        /// </summary>
        public int Id { get; }

        public bool IsValid => Id != 0;

        /// <summary>
        /// True until the move ends (its MoveCompleted is raised then).
        /// </summary>
        public bool IsActive => Id != 0 && PathFollowing != null && PathFollowing.IsCurrent_Internal(Id);

        public MoveStatus Status => IsActive ? PathFollowing.Status : MoveStatus.Idle;

        /// <summary>
        /// How it ended, once it ended.
        /// </summary>
        public bool TryGetResult(out MoveResult result)
        {
            if (Id != 0 && !ReferenceEquals(PathFollowing, null) && !IsActive)
            {
                return PathFollowing.TryGetResult_Internal(Id, out result);
            }

            result = default;

            return false;
        }

        /// <summary>
        /// Stops this move if it is still running (it ends as Aborted, Stopped).
        /// </summary>
        public void Stop()
        {
            if (IsActive)
            {
                PathFollowing.Stop();
            }
        }

        /// <summary>
        /// For coroutines: <c>yield return request.Wait();</c>, then read its Result. Allocates a small object.
        /// </summary>
        public MoveWait Wait()
        {
            return new MoveWait(this);
        }

        public MoveWait.Awaiter GetAwaiter()
        {
            return Wait().GetAwaiter();
        }

        public bool Equals(MoveRequest other)
        {
            return Id == other.Id && ReferenceEquals(PathFollowing, other.PathFollowing);
        }

        public override bool Equals(object obj)
        {
            return obj is MoveRequest other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Id;
        }

        public static bool operator ==(MoveRequest left, MoveRequest right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(MoveRequest left, MoveRequest right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    /// Waits for a move to end: <c>yield return request.Wait();</c> in a coroutine, or <c>await request;</c>.
    /// </summary>
    public sealed class MoveWait : CustomYieldInstruction
    {
        private Action _continuation;

        internal MoveWait(MoveRequest request)
        {
            Request = request;

            if (request.IsActive)
            {
                request.PathFollowing.AddWait_Internal(this);

                return;
            }

            if (request.TryGetResult(out var result))
            {
                Complete_Internal(result);

                return;
            }

            var reason = request.IsValid ? MoveReason.Unknown : MoveReason.NoAgent;

            Complete_Internal(new MoveResult(request, request.IsValid ? MoveOutcome.Aborted : MoveOutcome.Invalid, reason, Vector3.zero, Vector3.zero, 0f));
        }

        public MoveRequest Request { get; }

        public bool IsDone { get; private set; }

        /// <summary>
        /// How the move ended, once <see cref="IsDone"/>.
        /// </summary>
        public MoveResult Result { get; private set; }

        public override bool keepWaiting => !IsDone;

        public Awaiter GetAwaiter()
        {
            return new Awaiter(this);
        }

        internal void Complete_Internal(MoveResult result)
        {
            if (IsDone)
            {
                return;
            }

            Result = result;

            IsDone = true;

            var continuation = _continuation;

            _continuation = null;

            try
            {
                continuation?.Invoke();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        public readonly struct Awaiter : INotifyCompletion
        {
            private readonly MoveWait _wait;

            internal Awaiter(MoveWait wait)
            {
                _wait = wait;
            }

            public bool IsCompleted => _wait.IsDone;

            public MoveResult GetResult()
            {
                return _wait.Result;
            }

            public void OnCompleted(Action continuation)
            {
                if (_wait.IsDone)
                {
                    continuation();
                }
                else
                {
                    _wait._continuation += continuation;
                }
            }
        }
    }
}
#endif
