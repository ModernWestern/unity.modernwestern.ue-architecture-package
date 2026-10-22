namespace ModernWestern.UEArchitecture.Samples.Patrol
{
    // The rules of the level: a guard that catches the player sends them back to the start.
    public sealed class PatrolGameMode : GameMode<PatrolGameState, PlayerState>
    {
        private MessageListener _caught;

        protected override void StartPlay()
        {
            _caught = Messages<PlayerCaught>.Listen(this, static (mode, _) => mode.OnCaught());
        }

        protected override void OnEndPlay()
        {
            _caught.Dispose();
        }

        private void OnCaught()
        {
            GameState.TimesCaught++;

            // Stops the Pawn's move and warps its NavMeshAgent to the PlayerStart.
            RestartPlayer(World.GetPlayer());
        }
    }
}
