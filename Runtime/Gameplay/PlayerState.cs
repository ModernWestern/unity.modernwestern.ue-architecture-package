namespace ModernWestern.UEArchitecture
{
    /// <summary>
    /// The state of one player, like Unreal's APlayerState: name, score, team, kills. It belongs to the player, not to the
    /// Pawn, so it survives the Pawn dying and respawning. Created by the GameMode for every player and gone when the level
    /// ends. Derive from it and pick your class in <c>GameMode&lt;TGameState, TPlayerState&gt;</c>. It is a plain object,
    /// so it can also be the ViewModel of your HUD.
    /// </summary>
    public class PlayerState
    {
        /// <summary>
        /// Unique number of the player in this level (1, 2, 3...).
        /// </summary>
        public int PlayerId { get; private set; }

        public string PlayerName { get; set; }

        /// <summary>
        /// The player's PlayerController.
        /// </summary>
        public PlayerController Controller { get; private set; }

        /// <summary>
        /// The Pawn the player controls right now, or null (e.g. dead and waiting to respawn).
        /// </summary>
        public Pawn Pawn => Controller != null ? Controller.Pawn : null;

        /// <summary>
        /// The player's Pawn as your class, or null.
        /// </summary>
        public T GetPawn<T>() where T : Pawn
        {
            return Pawn as T;
        }

        /// <summary>
        /// Called when the player starts playing: when the level begins, or when the player joins a level already running.
        /// </summary>
        protected virtual void OnBeginPlay()
        {
        }

        /// <summary>
        /// Called when the player leaves or the level ends. Clean up here (stop listening, dispose your ViewModels).
        /// </summary>
        protected virtual void OnEndPlay()
        {
        }

        public override string ToString()
        {
            return PlayerName;
        }

        internal void Init_Internal(PlayerController controller, int playerId, string playerName)
        {
            Controller = controller;

            PlayerId = playerId;

            PlayerName = playerName;
        }

        internal void BeginPlay_Internal()
        {
            OnBeginPlay();
        }

        internal void EndPlay_Internal()
        {
            OnEndPlay();
        }
    }
}
