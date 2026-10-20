using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace ModernWestern.UEArchitecture.Tests
{
    // What runs every frame or on every gameplay event must not allocate. Each test warms up first: Mono creates static
    // lambdas, and arrays grow, the first time.
    public class ZeroGcTests : FrameworkTestBase
    {
        private readonly Counter _counter = new();

        [Test]
        public void GettingAGameSubsystem_DoesNotAllocate()
        {
            Relaunch();

            Get_Internal();

            Assert.That(Get_Internal, Is.Not.AllocatingGCMemory());
        }

        private static void Get_Internal()
        {
            GameInstance.Get<FirstSubsystem>();

            GameInstance.Get<DependentSubsystem>();
        }

        [UnityTest]
        public IEnumerator GettingTheLevelAndItsSubsystems_DoesNotAllocate()
        {
            OpenLevel();

            yield return null;

            Read_Internal();

            Assert.That(Read_Internal, Is.Not.AllocatingGCMemory());
        }

        private static void Read_Internal()
        {
            World.Get<LevelSubsystem>();

            World.GetGameMode<TestGameMode>();

            World.GetPlayerState<TestPlayerState>();

            World.GetPawn<TestPawn>();

            _ = World.TimeSeconds;
        }

        [UnityTest]
        public IEnumerator LoopingOverPlayersAndStarts_DoesNotAllocate()
        {
            var mode = Template<TestGameMode>("Mode");

            Set(mode, "defaultPawn", Template<TestPawn>("Pawn"));

            var worldSettings = Create<WorldSettings>("World Settings");

            Set(worldSettings, "gameModeOverride", mode);

            OpenLevel(worldSettings, Create<PlayerStart>("Start"));

            yield return null;

            Loop_Internal();

            Assert.That(Loop_Internal, Is.Not.AllocatingGCMemory());
        }

        private static void Loop_Internal()
        {
            foreach (var player in World.Players)
            {
                _ = player.Pawn;
            }

            foreach (var playerState in World.GameState.Players)
            {
                _ = playerState.PlayerId;
            }

            foreach (var start in PlayerStart.All)
            {
                _ = start.StartTag;
            }

            foreach (var subsystem in World.AllSubsystems)
            {
                _ = subsystem.IsInitialized;
            }
        }

        [Test]
        public void Broadcasting_DoesNotAllocate()
        {
            var first = Messages<TestMessage>.Listen(_counter, static (c, m) => c.Sum += m.Value);

            var second = Messages<TestMessage>.Listen(static m => _ = m.Value);

            Broadcast_Internal();

            Assert.That(Broadcast_Internal, Is.Not.AllocatingGCMemory());

            first.Dispose();

            second.Dispose();
        }

        private static void Broadcast_Internal()
        {
            Messages.Broadcast(new TestMessage { Value = 1 });
        }

        [Test]
        public void ListeningAndStopping_DoesNotAllocate_AfterWarmUp()
        {
            ListenAndStop_Internal();

            Assert.That(ListenAndStop_Internal, Is.Not.AllocatingGCMemory());
        }

        private void ListenAndStop_Internal()
        {
            var listener = Messages<OtherMessage>.Listen(_counter, static (c, m) => c.Calls++);

            listener.Dispose();
        }

        [Test]
        public void SettingFiringAndClearingTimers_DoesNotAllocate_AfterWarmUp()
        {
            Timers.Reset_Internal();

            Cycle_Internal();

            Assert.That(Cycle_Internal, Is.Not.AllocatingGCMemory());

            Timers.Reset_Internal();
        }

        private int _frame = 1_000_000;

        private double _now = 1_000_000d;

        private void Cycle_Internal()
        {
            var once = Timers.Set(_counter, 0.5f, static c => c.Calls++, flags: TimerFlags.Persistent);

            var loop = Timers.Set(_counter, 0.25f, static c => c.Sum++, loop: true, flags: TimerFlags.Persistent);

            _ = once.Remaining;

            Timers.Pause(loop);

            Timers.Resume(loop);

            _now += 1d;

            Timers.Tick_Internal(_now + 1e9, _now + 1e9, ++_frame);

            Timers.Clear(ref loop);
        }

        [UnityTest]
        public IEnumerator AFrameOfTheFramework_DoesNotAllocate()
        {
            Relaunch();

            OpenLevel();

            Timers.Set(_counter, 0.1f, static c => c.Calls++, loop: true);

            yield return null;

            FrameworkLoop.Tick_Internal();

            Assert.That(FrameworkLoop.Tick_Internal, Is.Not.AllocatingGCMemory());
        }
    }
}
