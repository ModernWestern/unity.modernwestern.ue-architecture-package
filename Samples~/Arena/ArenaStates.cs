using UnityEngine;

namespace ModernWestern.UEArchitecture.Samples.Arena
{
    // What everyone can read about the round. Created by the GameMode for each level.
    public sealed class ArenaGameState : GameState
    {
        public int CoinsToWin { get; set; }

        public int SecondsLeft { get; set; }

        public bool IsOver { get; set; }
    }

    // The state of one player: it outlives the Pawn.
    public sealed class ArenaPlayerState : PlayerState
    {
        public int Coins { get; set; }
    }

    // Messages are structs: broadcasting them allocates nothing.
    public struct CoinCollected
    {
        public PlayerState Player;

        public Vector3 Position;
    }

    public struct RoundEnded
    {
        public bool Won;

        public int Coins;

        public bool NewBest;
    }
}
