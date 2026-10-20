using UnityEngine;

namespace ModernWestern.UEArchitecture.Samples.Arena
{
    // The rules of the level: collect the coins before the time runs out. Then the level opens again behind the loading
    // screen, and the best score survives because it lives in a game subsystem (BestScore), not in the level.
    public sealed class ArenaGameMode : GameMode<ArenaGameState, ArenaPlayerState>
    {
        [Header("Rules")]
        [SerializeField, Min(1)] private int coinsToWin = 8;

        [SerializeField, Min(5)] private int roundSeconds = 30;

        [SerializeField] private Coin coinPrefab;

        [SerializeField, Tooltip("Shown while the level opens again.")]
        private LoadingScreen loadingScreen;

        private MessageListener _coins;

        private TimerHandle _clock;

        public Coin CoinPrefab => coinPrefab;

        protected override void InitGame()
        {
            GameState.CoinsToWin = coinsToWin;

            GameState.SecondsLeft = roundSeconds;
        }

        protected override void StartPlay()
        {
            // Nobody calls the GameMode: the coins announce themselves with a message.
            _coins = Messages<CoinCollected>.Listen(this, static (mode, message) => mode.OnCoinCollected(message));

            _clock = Timers.Set(this, 1f, static mode => mode.OnSecond(), loop: true);
        }

        protected override void OnEndPlay()
        {
            // Timers are cleared with the level; listeners are not.
            _coins.Dispose();
        }

        private void OnCoinCollected(CoinCollected message)
        {
            if (GameState.IsOver || !(message.Player is ArenaPlayerState player))
            {
                return;
            }

            player.Coins++;

            if (player.Coins >= GameState.CoinsToWin)
            {
                EndRound(true, player);
            }
        }

        private void OnSecond()
        {
            if (GameState.IsOver)
            {
                return;
            }

            GameState.SecondsLeft--;

            if (GameState.SecondsLeft <= 0)
            {
                EndRound(false, World.GetPlayerState<ArenaPlayerState>());
            }
        }

        private void EndRound(bool won, ArenaPlayerState player)
        {
            GameState.IsOver = true;

            Timers.Clear(ref _clock);

            var newBest = GameInstance.Get<BestScore>().Submit(player.Coins);

            Messages.Broadcast(new RoundEnded { Won = won, Coins = player.Coins, NewBest = newBest });

            Timers.Set(this, 3f, static mode => Levels.Open(mode.gameObject.scene.path, mode.loadingScreen));
        }
    }
}
