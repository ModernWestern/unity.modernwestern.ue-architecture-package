namespace ModernWestern.UEArchitecture.Tests
{
    public sealed class TestGameMode : GameMode<TestGameState, TestPlayerState>
    {
        protected override void InitGame() => TestGate.Log.Add("GameMode.InitGame");

        protected override void StartPlay() => TestGate.Log.Add("GameMode.StartPlay");

        protected override void OnPostLogin(PlayerController player) => TestGate.Log.Add($"GameMode.PostLogin {player.PlayerState.PlayerName}");

        protected override void OnLogout(PlayerController player) => TestGate.Log.Add($"GameMode.Logout {player.PlayerState.PlayerName}");

        protected override void OnEndPlay() => TestGate.Log.Add("GameMode.EndPlay");
    }
}
