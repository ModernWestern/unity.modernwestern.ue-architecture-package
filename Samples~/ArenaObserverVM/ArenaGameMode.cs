using UnityEngine;

namespace ModernWestern.UEArchitecture.Samples.ArenaObserverVM
{
    // The same rules as the "Arena" sample. The difference is in the states: changing their Observables is all it takes
    // for the HUD to update.
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
            GameState.CoinsToWin.Value = coinsToWin;

            GameState.SecondsLeft.Value = roundSeconds;
        }

        protected override void StartPlay()
        {
            _coins = Messages<CoinCollected>.Listen(this, static (mode, message) => mode.OnCoinCollected(message));

            _clock = Timers.Set(this, 1f, static mode => mode.OnSecond(), loop: true);
        }

        protected override void OnEndPlay()
        {
            _coins.Dispose();
        }

        private void OnCoinCollected(CoinCollected message)
        {
            if (GameState.IsOver.Value || !(message.Player is ArenaPlayerState player))
            {
                return;
            }

            player.Coins.Value++;

            if (player.Coins.Value >= GameState.CoinsToWin.Value)
            {
                EndRound(true, player);
            }
        }

        private void OnSecond()
        {
            if (GameState.IsOver.Value)
            {
                return;
            }

            GameState.SecondsLeft.Value--;

            if (GameState.SecondsLeft.Value <= 0)
            {
                EndRound(false, World.GetPlayerState<ArenaPlayerState>());
            }
        }

        private void EndRound(bool won, ArenaPlayerState player)
        {
            Timers.Clear(ref _clock);

            var newBest = GameInstance.Get<BestScore>().Submit(player.Coins.Value);

            var title = won ? "You win!" : "Time is up!";

            GameState.Banner.Value = newBest ? title + "\nNew best score" : title;

            GameState.IsOver.Value = true;

            Timers.Set(this, 3f, static mode => Levels.Open(mode.gameObject.scene.path, mode.loadingScreen));
        }
    }
}
