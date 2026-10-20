using UnityEngine;

namespace ModernWestern.UEArchitecture.Tests
{
    // A GameInstance of your own, on a prefab, with a value set in the Inspector (like a Blueprint default).
    public sealed class TestGameInstance : GameInstance
    {
        [SerializeField] private int configValue;

        public int ConfigValue => configValue;

        protected override void Init() => TestGate.Log.Add($"Game.Init {configValue}");

        protected override void Shutdown() => TestGate.Log.Add("Game.Shutdown");

        protected override void OnLevelBeganPlay() => TestGate.Log.Add("Game.LevelBeganPlay");

        protected override void OnLevelEndingPlay() => TestGate.Log.Add("Game.LevelEndingPlay");
    }
}
