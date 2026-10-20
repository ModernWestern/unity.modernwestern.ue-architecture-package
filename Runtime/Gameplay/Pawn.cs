using UnityEngine;

namespace ModernWestern.UEArchitecture
{
    /// <summary>
    /// Something a player can control, like Unreal's APawn: a character, a car, a ship. The GameMode spawns the default Pawn
    /// of each player at a PlayerStart and the player's PlayerController possesses it. Data that must survive death belongs
    /// in the PlayerState, not here.
    /// </summary>
    [DisallowMultipleComponent]
    public class Pawn : MonoBehaviour
    {
        private PlayerController _controller;

        /// <summary>
        /// The PlayerController controlling this Pawn, or null.
        /// </summary>
        public PlayerController Controller => _controller != null ? _controller : null;

        /// <summary>
        /// The PlayerState of the player controlling this Pawn, or null.
        /// </summary>
        public PlayerState PlayerState => Controller != null ? Controller.PlayerState : null;

        public bool IsPlayerControlled => Controller != null;

        public T GetController<T>() where T : PlayerController
        {
            return Controller as T;
        }

        public T GetPlayerState<T>() where T : PlayerState
        {
            return PlayerState as T;
        }

        /// <summary>
        /// Called when a player takes control of this Pawn.
        /// </summary>
        protected virtual void OnPossessed(PlayerController controller)
        {
        }

        /// <summary>
        /// Called when the player lets go of this Pawn.
        /// </summary>
        protected virtual void OnUnPossessed()
        {
        }

        /// <summary>
        /// If you need OnDestroy, override it and call base.OnDestroy(): it tells the controller its Pawn is gone.
        /// </summary>
        protected virtual void OnDestroy()
        {
            if (_controller != null)
            {
                _controller.PawnDestroyed_Internal(this);
            }
        }

        internal void PossessedBy_Internal(PlayerController controller)
        {
            _controller = controller;

            OnPossessed(controller);
        }

        internal void UnPossessed_Internal()
        {
            _controller = null;

            OnUnPossessed();
        }
    }
}
