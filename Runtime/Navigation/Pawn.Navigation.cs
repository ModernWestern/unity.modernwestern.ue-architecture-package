#if UEA_NAVIGATION
using UnityEngine.AI;

namespace ModernWestern.UEArchitecture
{
    // Navigation for Pawns. Only with Unity's AI module (com.unity.modules.ai).
    public partial class Pawn
    {
        private PathFollowing _pathFollowing;

        /// <summary>
        /// What moves this Pawn on the NavMesh, or null if it has none yet (its controller's MoveTo adds one when the Pawn
        /// has a NavMeshAgent).
        /// </summary>
        public PathFollowing PathFollowing
        {
            get
            {
                if (_pathFollowing == null)
                {
                    TryGetComponent(out _pathFollowing);
                }

                return _pathFollowing != null ? _pathFollowing : null;
            }
        }

        internal PathFollowing PathFollowingToMove_Internal()
        {
            var pathFollowing = PathFollowing;

            if (pathFollowing != null || !TryGetComponent(out NavMeshAgent _))
            {
                return pathFollowing;
            }

            return _pathFollowing = gameObject.AddComponent<PathFollowing>();
        }

        // A move belongs to whoever controlled the Pawn when it started.
        private void AbortMove_Internal()
        {
            var pathFollowing = PathFollowing;

            if (pathFollowing != null)
            {
                pathFollowing.Abort_Internal(MoveReason.ControllerChanged);
            }
        }
    }
}
#endif
