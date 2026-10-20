using System;

namespace ModernWestern.UEArchitecture.Samples.Arena
{
    // A game subsystem: created by itself, alive across levels. Get it anywhere with GameInstance.Get<BestScore>().
    // The best score lives in a save game, so it survives quitting the game.
    public sealed class BestScore : GameInstanceSubsystem
    {
        private const string Slot = "UE Architecture Arena";

        private ArenaSave _save;

        public int Best => _save.bestScore;

        protected override void Initialize()
        {
            _save = SaveGames.Load<ArenaSave>(Slot) ?? new ArenaSave();
        }

        // True when it is a new best score.
        public bool Submit(int coins)
        {
            if (coins <= _save.bestScore)
            {
                return false;
            }

            _save.bestScore = coins;

            SaveGames.Save(_save, Slot);

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
