using System;
using UnityEngine;

namespace ModernWestern.UEArchitecture
{
    /// <summary>
    /// What possesses a Pawn, like Unreal's AController: a player (<see cref="PlayerController"/>) or an AI
    /// (<see cref="AIController"/>). It outlives its Pawns and can move from one to another with <see cref="Possess"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public abstract class Controller : MonoBehaviour
    {
        private Pawn _pawn;

        /// <summary>
        /// Raised when this controller starts or stops controlling a Pawn (also when the Pawn is destroyed).
        /// </summary>
        public event Action<Controller> PawnChanged;

        internal static event Action<Controller> AnyPawnChanged_Internal;

        /// <summary>
        /// The Pawn controlled right now, or null.
        /// </summary>
        public Pawn Pawn => _pawn != null ? _pawn : null;

        /// <summary>
        /// The PlayerState: always for players; for AIs only when they ask for one (bots on a scoreboard).
        /// </summary>
        public PlayerState PlayerState { get; private set; }

        public bool IsPlayerController => this is PlayerController;

        public T GetPawn<T>() where T : Pawn
        {
            return Pawn as T;
        }

        public T GetPlayerState<T>() where T : PlayerState
        {
            return PlayerState as T;
        }

        /// <summary>
        /// Takes control of a Pawn (taking it from its previous controller, if any) and lets go of the current one.
        /// </summary>
        public void Possess(Pawn pawn)
        {
            if (pawn == Pawn)
            {
                return;
            }

            ReleasePawn_Internal();

            if (pawn != null)
            {
                if (pawn.Controller != null)
                {
                    pawn.Controller.UnPossess();
                }

                _pawn = pawn;

                pawn.PossessedBy_Internal(this);

                OnPossess(pawn);

                Possessed_Internal();
            }

            RaisePawnChanged_Internal();
        }

        /// <summary>
        /// Lets go of the current Pawn (it stays in the level).
        /// </summary>
        public void UnPossess()
        {
            if (Pawn == null)
            {
                return;
            }

            ReleasePawn_Internal();

            RaisePawnChanged_Internal();
        }

        protected virtual void OnPossess(Pawn pawn)
        {
        }

        protected virtual void OnUnPossess(Pawn pawn)
        {
        }

        /// <summary>
        /// Called when it starts playing (the level began, or it joined a level already running).
        /// </summary>
        protected virtual void OnBeginPlay()
        {
        }

        /// <summary>
        /// Called when it leaves or the level ends.
        /// </summary>
        protected virtual void OnEndPlay()
        {
        }

        /// <summary>
        /// If you need OnDestroy, override it and call base.OnDestroy().
        /// </summary>
        protected virtual void OnDestroy()
        {
            Destroyed_Internal();
        }

        internal void SetPlayerState_Internal(PlayerState playerState)
        {
            PlayerState = playerState;
        }

        internal void BeginPlay_Internal()
        {
            OnBeginPlay();
        }

        internal void EndPlay_Internal()
        {
            OnEndPlay();
        }

        internal virtual void Possessed_Internal()
        {
        }

        internal virtual void Destroyed_Internal()
        {
        }

        // True when the destroyed Pawn was this controller's.
        internal virtual bool PawnDestroyed_Internal(Pawn pawn)
        {
            if (!ReferenceEquals(pawn, _pawn))
            {
                return false;
            }

            _pawn = null;

            RaisePawnChanged_Internal();

            return true;
        }

        private void ReleasePawn_Internal()
        {
            var pawn = Pawn;

            _pawn = null;

            if (pawn == null)
            {
                return;
            }

            pawn.UnPossessed_Internal();

            OnUnPossess(pawn);
        }

        private void RaisePawnChanged_Internal()
        {
            try
            {
                PawnChanged?.Invoke(this);

                AnyPawnChanged_Internal?.Invoke(this);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }
    }
}
