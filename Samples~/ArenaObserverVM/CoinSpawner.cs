using UnityEngine;
using System.Collections.Generic;

namespace ModernWestern.UEArchitecture.Samples.ArenaObserverVM
{
    // A level subsystem: created when a level begins and gone when it ends, with its timers. Subsystems exist in every
    // level of the project, so this one only works where the GameMode is the Arena one.
    public sealed class CoinSpawner : WorldSubsystem
    {
        private const int MaxCoins = 4;

        private readonly List<Coin> _coins = new();

        private ArenaGameMode _gameMode;

        public int Count => _coins.Count;

        protected override void OnWorldBeginPlay()
        {
            _gameMode = World.GetGameMode<ArenaGameMode>();

            if (_gameMode == null || _gameMode.CoinPrefab == null)
            {
                return;
            }

            Timers.SetLooping(this, 0.3f, 1.2f, static spawner => spawner.Spawn());
        }

        private void Spawn()
        {
            _coins.RemoveAll(static coin => coin == null);

            if (_coins.Count >= MaxCoins || World.GetGameState<ArenaGameState>().IsOver.Value)
            {
                return;
            }

            var position = new Vector3(Random.Range(-7f, 7f), 0f, Random.Range(-4f, 4f));

            _coins.Add(Object.Instantiate(_gameMode.CoinPrefab, position, Quaternion.identity));
        }
    }
}
