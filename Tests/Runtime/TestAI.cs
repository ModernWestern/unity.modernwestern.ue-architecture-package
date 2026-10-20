namespace ModernWestern.UEArchitecture.Tests
{
    public sealed class TestAI : AIController
    {
        public int Possessions;

        public int BeginPlays;

        protected override void OnPossess(Pawn pawn) => Possessions++;

        protected override void OnBeginPlay() => BeginPlays++;

        protected override void OnEndPlay() => TestGate.Log.Add("AI.EndPlay");
    }
}
