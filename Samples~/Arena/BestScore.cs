using UnityEngine;

namespace ModernWestern.UEArchitecture.Samples.Arena
{
    // A game subsystem: created by itself, alive across levels. Get it anywhere with GameInstance.Get<BestScore>().
    public sealed class BestScore : GameInstanceSubsystem
    {
        private const string Key = "UEArchitecture.Samples.Arena.BestScore";

        public int Best { get; private set; }

        protected override void Initialize()
        {
            Best = PlayerPrefs.GetInt(Key, 0);
        }

        // True when it is a new best score.
        public bool Submit(int coins)
        {
            if (coins <= Best)
            {
                return false;
            }

            Best = coins;

            PlayerPrefs.SetInt(Key, coins);

            return true;
        }
    }
}
