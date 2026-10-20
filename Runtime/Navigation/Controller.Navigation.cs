#if UEA_NAVIGATION
using System;
using UnityEngine;

namespace ModernWestern.UEArchitecture
{
    // Navigation for controllers, like Unreal's AAIController::MoveTo. Only with Unity's AI module (com.unity.modules.ai).
    public abstract partial class Controller
    {
        private bool _warnedCannotMove;

        /// <summary>
        /// What the controlled Pawn's move is doing (Idle when there is no Pawn or no move). Like Unreal's GetMoveStatus.
        /// </summary>
        public MoveStatus MoveStatus
        {
            get
            {
                var pathFollowing = Pawn != null ? Pawn.PathFollowing : null;

                return pathFollowing != null ? pathFollowing.Status : MoveStatus.Idle;
            }
        }

        /// <summary>
        /// Moves the controlled Pawn to a point on the NavMesh. The Pawn needs a NavMeshAgent (its PathFollowing is added
        /// when first needed). Ends with exactly one <see cref="OnMoveCompleted"/>; a new move replaces this one.
        /// </summary>
        /// <param name="acceptanceRadius">How close is close enough; 0 = the agent's stopping distance.</param>
        /// <param name="allowPartial">If the point cannot be reached, go as close as possible (Partial) instead of failing (Invalid).</param>
        public MoveRequest MoveTo(Vector3 destination, float acceptanceRadius = 0f, bool allowPartial = false)
        {
            var pathFollowing = PathFollowingToMove_Internal();

            return pathFollowing != null ? pathFollowing.MoveTo(destination, acceptanceRadius, allowPartial) : MoveRequest.None;
        }

        /// <summary>
        /// Like <see cref="MoveTo(Vector3, float, bool)"/>, also calling <paramref name="onCompleted"/> with
        /// <paramref name="owner"/> when it ends. Use a <c>static</c> lambda so nothing is captured.
        /// </summary>
        public MoveRequest MoveTo<TOwner>(Vector3 destination, TOwner owner, Action<TOwner, MoveResult> onCompleted, float acceptanceRadius = 0f, bool allowPartial = false) where TOwner : class
        {
            var pathFollowing = PathFollowingToMove_Internal();

            return pathFollowing != null ? pathFollowing.MoveTo(destination, owner, onCompleted, acceptanceRadius, allowPartial) : MoveRequest.None;
        }

        /// <summary>
        /// Moves the controlled Pawn to an actor, following it while it moves, until it touches it. Like Unreal's MoveToActor.
        /// </summary>
        public MoveRequest MoveToActor(Component target, float acceptanceRadius = 0f, bool allowPartial = false)
        {
            var pathFollowing = PathFollowingToMove_Internal();

            return pathFollowing != null ? pathFollowing.MoveToActor(target, acceptanceRadius, allowPartial) : MoveRequest.None;
        }

        public MoveRequest MoveToActor<TOwner>(Component target, TOwner owner, Action<TOwner, MoveResult> onCompleted, float acceptanceRadius = 0f, bool allowPartial = false) where TOwner : class
        {
            var pathFollowing = PathFollowingToMove_Internal();

            return pathFollowing != null ? pathFollowing.MoveToActor(target, owner, onCompleted, acceptanceRadius, allowPartial) : MoveRequest.None;
        }

        /// <summary>
        /// Stops the controlled Pawn. Its move, if any, ends as Aborted (Stopped).
        /// </summary>
        public void StopMovement()
        {
            var pathFollowing = Pawn != null ? Pawn.PathFollowing : null;

            if (pathFollowing != null)
            {
                pathFollowing.Stop();
            }
        }

        /// <summary>
        /// Holds the Pawn where it is without ending its move.
        /// </summary>
        public void PauseMove()
        {
            var pathFollowing = Pawn != null ? Pawn.PathFollowing : null;

            if (pathFollowing != null)
            {
                pathFollowing.Pause();
            }
        }

        public void ResumeMove()
        {
            var pathFollowing = Pawn != null ? Pawn.PathFollowing : null;

            if (pathFollowing != null)
            {
                pathFollowing.Resume();
            }
        }

        /// <summary>
        /// Called once for every move of the controlled Pawn, when it ends: Success, Partial, Blocked, Invalid or Aborted.
        /// Like Unreal's OnMoveCompleted. You can request the next move from here.
        /// </summary>
        protected virtual void OnMoveCompleted(MoveResult result)
        {
        }

        internal void MoveCompleted_Internal(MoveResult result)
        {
            OnMoveCompleted(result);
        }

        private PathFollowing PathFollowingToMove_Internal()
        {
            var pawn = Pawn;

            var pathFollowing = pawn != null ? pawn.PathFollowingToMove_Internal() : null;

            if (pathFollowing == null && !_warnedCannotMove)
            {
                _warnedCannotMove = true;

                Debug.LogWarning(pawn == null
                                     ? $"[UEArchitecture] {name} called MoveTo without a Pawn: possess one first."
                                     : $"[UEArchitecture] {name} cannot move {pawn.name}: it needs a NavMeshAgent (and a NavMesh to walk on).", this);
            }

            return pathFollowing;
        }
    }
}
#endif
