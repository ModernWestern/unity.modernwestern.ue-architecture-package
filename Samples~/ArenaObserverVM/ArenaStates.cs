using UnityEngine;
using ModernWestern.UI.ObserverVM;

namespace ModernWestern.UEArchitecture.Samples.ArenaObserverVM
{
    // What everyone can read about the round. With ObserverVM its values are Observables, so the HUD Widgets that expect
    // an ArenaGameState are bound to it by themselves and update without any HUD code.
    public sealed class ArenaGameState : GameState
    {
        public Observable<int> CoinsToWin { get; } = new();

        public Observable<int> SecondsLeft { get; } = new();

        public Observable<bool> IsOver { get; } = new();

        public Observable<string> Banner { get; } = new(string.Empty);
    }

    // The state of one player: it outlives the Pawn. Widgets expecting an ArenaPlayerState get the player's own.
    public sealed class ArenaPlayerState : PlayerState
    {
        public Observable<int> Coins { get; } = new();
    }

    // Messages are structs: broadcasting them allocates nothing.
    public struct CoinCollected
    {
        public PlayerState Player;

        public Vector3 Position;
    }
}
