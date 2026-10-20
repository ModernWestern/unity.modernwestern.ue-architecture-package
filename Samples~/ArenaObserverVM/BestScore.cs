using System;
using ModernWestern.UI.ObserverVM;

namespace ModernWestern.UEArchitecture.Samples.ArenaObserverVM
{
    // A game subsystem that is also a ViewModel: a Widget expecting a BestScore is bound to it by itself.
    // The best score lives in a save game, so it survives quitting the game.
    public sealed class BestScore : GameInstanceSubsystem
    {
        private const string Slot = "UE Architecture Arena ObserverVM";

        private ArenaSave _save;

        public Observable<int> Best { get; } = new();

        protected override void Initialize()
        {
            _save = SaveGames.Load<ArenaSave>(Slot) ?? new ArenaSave();

            Best.Value = _save.bestScore;
        }

        // True when it is a new best score.
        public bool Submit(int coins)
        {
            if (coins <= Best.Value)
            {
                return false;
            }

            _save.bestScore = coins;

            SaveGames.Save(_save, Slot);

            Best.Value = coins;

            return true;
        }
    }

    // What the Arena saves (public fields, the way Unity serializes them).
    [Serializable]
    public sealed class ArenaSave : SaveGame
    {
        public int bestScore;
    }
}
