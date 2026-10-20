using System.Collections.Generic;

namespace ModernWestern.UEArchitecture.Tests
{
    // The test subsystems exist in every Play session of a project that includes these tests: they only get created
    // while a test opens the gate.
    public static class TestGate
    {
        public static bool Enabled;

        public static readonly List<string> Log = new();
    }

    public sealed class FirstSubsystem : GameInstanceSubsystem, ITickable
    {
        public int Ticks;

        public float LastDeltaTime;

        protected override bool ShouldCreate() => TestGate.Enabled;

        protected override void Initialize() => TestGate.Log.Add("First.Initialize");

        protected override void Deinitialize() => TestGate.Log.Add("First.Deinitialize");

        protected override void OnLevelBeganPlay() => TestGate.Log.Add("First.LevelBeganPlay");

        protected override void OnLevelEndingPlay() => TestGate.Log.Add("First.LevelEndingPlay");

        public void Tick(float deltaTime)
        {
            Ticks++;

            LastDeltaTime = deltaTime;
        }
    }

    // Sorts before FirstSubsystem, so it is initialized first and has to pull its dependency.
    public sealed class DependentSubsystem : GameInstanceSubsystem
    {
        public FirstSubsystem First;

        protected override bool ShouldCreate() => TestGate.Enabled;

        protected override void Initialize()
        {
            First = GameInstance.Get<FirstSubsystem>();

            TestGate.Log.Add("Dependent.Initialize");
        }

        protected override void Deinitialize() => TestGate.Log.Add("Dependent.Deinitialize");
    }

    // No script mentions this class: the framework finds it by itself.
    public sealed class UnreferencedSubsystem : GameInstanceSubsystem
    {
        protected override bool ShouldCreate() => TestGate.Enabled;
    }

    public sealed class SkippedSubsystem : GameInstanceSubsystem
    {
        protected override bool ShouldCreate() => false;
    }

    public sealed class LevelSubsystem : WorldSubsystem, ITickable
    {
        public int Ticks;

        protected override bool ShouldCreate() => TestGate.Enabled;

        protected override void Initialize() => TestGate.Log.Add("Level.Initialize");

        protected override void OnWorldBeginPlay() => TestGate.Log.Add("Level.BeginPlay");

        protected override void Deinitialize() => TestGate.Log.Add("Level.Deinitialize");

        public void Tick(float deltaTime) => Ticks++;
    }

    public sealed class TestGameState : GameState
    {
        protected override void OnBeginPlay() => TestGate.Log.Add("GameState.BeginPlay");

        protected override void OnEndPlay() => TestGate.Log.Add("GameState.EndPlay");
    }

    public sealed class TestPlayerState : PlayerState
    {
        public int Score;

        protected override void OnBeginPlay() => TestGate.Log.Add($"{PlayerName}.BeginPlay");

        protected override void OnEndPlay() => TestGate.Log.Add($"{PlayerName}.EndPlay");
    }

    public struct TestMessage
    {
        public int Value;
    }

    public struct OtherMessage
    {
        public int Value;
    }

    public struct RemovalMessage
    {
    }

    public sealed class Counter
    {
        public int Calls;

        public int Sum;
    }
}
