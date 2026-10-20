using UnityEngine;
using ModernWestern.UI.ObserverVM;

namespace ModernWestern.UEArchitecture.Samples.ArenaObserverVM
{
    // A game subsystem that is also a ViewModel: a Widget expecting a BestScore is bound to it by itself.
    public sealed class BestScore : GameInstanceSubsystem
    {
        private const string Key = "UEArchitecture.Samples.ArenaObserverVM.BestScore";

        public Observable<int> Best { get; } = new();

        protected override void Initialize()
        {
            Best.Value = PlayerPrefs.GetInt(Key, 0);
        }

        // True when it is a new best score.
        public bool Submit(int coins)
        {
            if (coins <= Best.Value)
            {
                return false;
            }

            Best.Value = coins;

            PlayerPrefs.SetInt(Key, coins);

            return true;
        }
    }
}
