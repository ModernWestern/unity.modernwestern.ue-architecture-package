namespace ModernWestern.UEArchitecture.Tests
{
    public sealed class TestPawn : Pawn
    {
        public int Possessions;

        protected override void OnPossessed(Controller controller)
        {
            Possessions++;

            TestGate.Log.Add($"{name}.Possessed");
        }
    }
}
