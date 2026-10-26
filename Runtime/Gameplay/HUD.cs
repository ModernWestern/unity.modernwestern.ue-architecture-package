using System;
using UnityEngine;

namespace ModernWestern.UEArchitecture
{
    /// <summary>
    /// The on-screen interface of one player, like Unreal's AHUD. The GameMode creates it from its HUD prefab (your Canvas)
    /// for every player. With ObserverVM installed, the Widgets inside it are bound by themselves to the player's
    /// PlayerState, Pawn, PlayerController, the GameState or a subsystem, depending on the ViewModel type each one expects.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("UE Architecture/HUD")]
    public class HUD : MonoBehaviour
    {
        private PlayerController _owner;

        internal static event Action<HUD> Initialized_Internal;

        /// <summary>
        /// The player this HUD belongs to.
        /// </summary>
        public PlayerController Owner => _owner != null ? _owner : null;

        public PlayerState PlayerState => Owner != null ? Owner.PlayerState : null;

        public Pawn Pawn => Owner != null ? Owner.Pawn : null;

        public T GetPlayerState<T>() where T : PlayerState
        {
            return PlayerState as T;
        }

        public T GetPawn<T>() where T : Pawn
        {
            return Pawn as T;
        }

        /// <summary>
        /// Called once the HUD knows its player (Owner, PlayerState and Pawn are set).
        /// </summary>
        protected virtual void OnInit()
        {
        }

        internal void Init_Internal(PlayerController owner)
        {
            _owner = owner;

            OnInit();

            try
            {
                Initialized_Internal?.Invoke(this);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }
    }
}
