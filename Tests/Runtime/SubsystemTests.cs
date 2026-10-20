using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace ModernWestern.UEArchitecture.Tests
{
    public class SubsystemTests : FrameworkTestBase
    {
        [Test]
        public void TheGameInstance_StartsByItself_AndSurvivesLevelChanges()
        {
            Assert.IsTrue(GameInstance.IsRunning);

            Assert.IsNotNull(GameInstance.Current);

            Assert.AreEqual("DontDestroyOnLoad", GameInstance.Current.gameObject.scene.name);
        }

        [Test]
        public void Subsystems_AreCreated_AndInitializedAfterTheirDependencies()
        {
            Relaunch();

            var first = GameInstance.Get<FirstSubsystem>();

            var dependent = GameInstance.Get<DependentSubsystem>();

            Assert.IsNotNull(first);

            Assert.IsNotNull(dependent);

            Assert.AreSame(first, dependent.First, "Get inside Initialize returns the initialized dependency.");

            Assert.Less(IndexOf("First.Initialize"), IndexOf("Dependent.Initialize"));

            Assert.IsTrue(first.IsInitialized);
        }

        [Test]
        public void ASubsystemNoScriptMentions_IsCreatedToo()
        {
            Relaunch();

            var found = false;

            foreach (var subsystem in GameInstance.Subsystems)
            {
                found |= subsystem.GetType().Name == "UnreferencedSubsystem";
            }

            Assert.IsTrue(found, "Found by itself (and kept by the link.xml in stripped IL2CPP builds).");
        }

        [Test]
        public void ShouldCreateFalse_SkipsTheSubsystem()
        {
            Relaunch();

            Assert.IsNull(GameInstance.Get<SkippedSubsystem>());
        }

        [Test]
        public void Shutdown_DeinitializesInReverseOrder()
        {
            Relaunch();

            TestGate.Log.Clear();

            GameFramework.Shutdown();

            Assert.Less(IndexOf("Dependent.Deinitialize"), IndexOf("First.Deinitialize"));

            Assert.IsNotNull(GameInstance.Get<FirstSubsystem>(), "Still reachable while the game quits, so OnDestroy code does not hit nulls.");
        }

        [UnityTest]
        public IEnumerator TickableSubsystems_TickEveryFrame()
        {
            Relaunch();

            var first = GameInstance.Get<FirstSubsystem>();

            var ticks = first.Ticks;

            yield return null;

            yield return null;

            yield return null;

            Assert.GreaterOrEqual(first.Ticks - ticks, 2);
        }

        [UnityTest]
        public IEnumerator WorldSubsystems_LiveAsLongAsTheLevel()
        {
            OpenLevel();

            var level = World.Get<LevelSubsystem>();

            Assert.IsNotNull(level);

            Assert.IsTrue(World.HasBegunPlay);

            Assert.Less(IndexOf("Level.Initialize"), IndexOf("Level.BeginPlay"));

            yield return null;

            yield return null;

            Assert.Greater(level.Ticks, 0);

            World.End_Internal();

            Assert.Greater(IndexOf("Level.Deinitialize"), IndexOf("Level.BeginPlay"));

            Assert.IsNull(World.Get<LevelSubsystem>());

            Assert.IsFalse(World.HasBegunPlay);
        }

        [UnityTest]
        public IEnumerator EachLevel_GetsNewSubsystems()
        {
            OpenLevel();

            var first = World.Get<LevelSubsystem>();

            World.Begin_Internal(World.Scene);

            yield return null;

            Assert.IsNotNull(World.Get<LevelSubsystem>());

            Assert.AreNotSame(first, World.Get<LevelSubsystem>());

            Assert.IsFalse(first.IsInitialized);
        }
    }
}
