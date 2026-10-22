namespace ModernWestern.UEArchitecture.Samples.Patrol
{
    // What everyone can read about the level. Created by the GameMode for each level.
    public sealed class PatrolGameState : GameState
    {
        public int TimesCaught { get; set; }
    }

    // Sent by the guards and the player every time one of their moves ends, so the HUD can show it.
    public struct MoveEnded
    {
        public string Who;

        public string Goal;

        public MoveResult Result;
    }

    public struct PlayerCaught
    {
        public GuardController Guard;
    }
}
