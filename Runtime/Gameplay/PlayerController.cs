using System;
using UnityEngine;

namespace ModernWestern.UEArchitecture
{
    /// <summary>
    /// The player's will, like Unreal's APlayerController: reads input and drives the Pawn it possesses. It outlives its
    /// Pawns (dying and respawning does not destroy it), owns the player's HUD and holds the player's PlayerState.
    /// The GameMode creates one per player; derive from it for your input and put it on the GameMode's PlayerController prefab.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerController : MonoBehaviour
    {
        private Pawn _pawn;

        private HUD _hud;

        /// <summary>
        /// Raised when this player starts or stops controlling a Pawn (also when the Pawn is destroyed).
        /// </summary>
        public event Action<PlayerController> PawnChanged;

        internal static event Action<PlayerController> AnyPawnChanged_Internal;

        public PlayerState PlayerState { get; private set; }

        /// <summary>
        /// 0 for the first player, 1 for the second...
        /// </summary>
        public int PlayerIndex { get; private set; }

        /// <summary>
        /// The Pawn this player controls right now, or null.
        /// </summary>
        public Pawn Pawn => _pawn != null ? _pawn : null;

        /// <summary>
        /// This player's HUD, or null if the GameMode has no HUD prefab.
        /// </summary>
        public HUD HUD => _hud != null ? _hud : null;

        public T GetPawn<T>() where T : Pawn
        {
            return Pawn as T;
        }

        public T GetPlayerState<T>() where T : PlayerState
        {
            return PlayerState as T;
        }

        public T GetHUD<T>() where T : HUD
        {
            return HUD as T;
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
        /// Called when the player starts playing (the level began or the player joined), after its first Pawn was spawned.
        /// </summary>
        protected virtual void OnBeginPlay()
        {
        }

        protected virtual void OnEndPlay()
        {
        }

        internal void Init_Internal(PlayerState playerState, int playerIndex)
        {
            PlayerState = playerState;

            PlayerIndex = playerIndex;
        }

        internal void SetHUD_Internal(HUD hud)
        {
            _hud = hud;
        }

        internal void BeginPlay_Internal()
        {
            OnBeginPlay();
        }

        internal void EndPlay_Internal()
        {
            OnEndPlay();
        }

        internal void PawnDestroyed_Internal(Pawn pawn)
        {
            if (!ReferenceEquals(pawn, _pawn))
            {
                return;
            }

            _pawn = null;

            RaisePawnChanged_Internal();
        }

        private void ReleasePawn_Internal()
        {
            var pawn = Pawn;

            if (pawn == null)
            {
                _pawn = null;

                return;
            }

            _pawn = null;

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
