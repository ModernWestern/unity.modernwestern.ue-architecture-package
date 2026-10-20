using System.Collections.Generic;

namespace ModernWestern.UEArchitecture
{
    /// <summary>
    /// The state of the match everyone can read, like Unreal's AGameStateBase: score to win, time left, the players.
    /// Created by the GameMode when the level begins and gone when it ends. Derive from it for your own data and pick your
    /// class in <c>GameMode&lt;TGameState, TPlayerState&gt;</c>. It is a plain object (not a component), so it can also be
    /// the ViewModel of your HUD.
    /// </summary>
    public class GameState
    {
        private readonly List<PlayerState> _players = new();

        /// <summary>
        /// The GameMode that created it.
        /// </summary>
        public GameMode GameMode { get; private set; }

        /// <summary>
        /// Every player of the match, in the order they joined.
        /// </summary>
        public IReadOnlyList<PlayerState> Players => _players;

        /// <summary>
        /// Seconds since the level began (scaled by Time.timeScale).
        /// </summary>
        public float TimeSeconds => World.TimeSeconds;

        /// <summary>
        /// Called when the level begins play, after the first players joined.
        /// </summary>
        protected virtual void OnBeginPlay()
        {
        }

        /// <summary>
        /// Called when the level ends. Clean up here (stop listening, dispose your ViewModels).
        /// </summary>
        protected virtual void OnEndPlay()
        {
        }

        protected virtual void OnPlayerAdded(PlayerState player)
        {
        }

        protected virtual void OnPlayerRemoved(PlayerState player)
        {
        }

        internal void Init_Internal(GameMode gameMode)
        {
            GameMode = gameMode;
        }

        internal void BeginPlay_Internal()
        {
            OnBeginPlay();
        }

        internal void EndPlay_Internal()
        {
            OnEndPlay();
        }

        internal void AddPlayer_Internal(PlayerState player)
        {
            _players.Add(player);

            OnPlayerAdded(player);
        }

        internal void RemovePlayer_Internal(PlayerState player)
        {
            if (_players.Remove(player))
            {
                OnPlayerRemoved(player);
            }
        }
    }
}
