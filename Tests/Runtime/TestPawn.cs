namespace ModernWestern.UEArchitecture.Tests
{
    public sealed class TestPawn : Pawn
    {
        public int Possessions;

        protected override void OnPossessed(PlayerController controller) => Possessions++;
    }
}
