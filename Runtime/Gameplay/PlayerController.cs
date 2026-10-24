namespace ModernWestern.UEArchitecture
{
    /// <summary>
    /// The player's will, like Unreal's APlayerController: reads input and drives the Pawn it possesses. It outlives its
    /// Pawns (dying and respawning does not destroy it), owns the player's HUD and holds the player's PlayerState.
    /// The GameMode creates one per player; derive from it for your input and put it on the GameMode's PlayerController prefab.
    /// </summary>
    [UnityEngine.AddComponentMenu("UE Architecture/Player Controller")]
    public class PlayerController : Controller
    {
        private HUD _hud;

        /// <summary>
        /// 0 for the first player, 1 for the second...
        /// </summary>
        public int PlayerIndex { get; private set; }

        /// <summary>
        /// This player's HUD, or null if the GameMode has no HUD prefab.
        /// </summary>
        public HUD HUD => _hud != null ? _hud : null;

        public T GetHUD<T>() where T : HUD
        {
            return HUD as T;
        }

        internal void Init_Internal(PlayerState playerState, int playerIndex)
        {
            SetPlayerState_Internal(playerState);

            PlayerIndex = playerIndex;
        }

        internal void SetHUD_Internal(HUD hud)
        {
            _hud = hud;
        }
    }
}
