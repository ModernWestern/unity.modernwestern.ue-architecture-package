using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ModernWestern.UEArchitecture.Tests
{
    // Your own GameInstance: a class on a prefab, picked in the settings, like a GameInstance Blueprint in Unreal.
    public class GameInstanceTests : FrameworkTestBase
    {
        private UEArchitectureSettings _settings;

        [SetUp]
        public void UseMyGameInstance()
        {
            var prefab = Template<TestGameInstance>("My Game");

            Set(prefab, "configValue", 42);

            _settings = ScriptableObject.CreateInstance<UEArchitectureSettings>();

            _settings.Set_Internal(prefab, null, null);

            Relaunch(_settings);
        }

        [TearDown]
        public void DropSettings()
        {
            Object.DestroyImmediate(_settings);
        }

        [Test]
        public void TheGameInstanceOfTheSettings_IsCreatedWithItsInspectorValues()
        {
            var game = GameInstance.Current as TestGameInstance;

            Assert.IsNotNull(game, "The prefab picked in the settings, not a plain GameInstance.");

            Assert.AreEqual(42, game.ConfigValue);

            Assert.AreEqual("GameInstance", game.name);

            Assert.AreEqual("DontDestroyOnLoad", game.gameObject.scene.name);

            Assert.Less(IndexOf("First.Initialize"), IndexOf("Game.Init 42"), "Init runs once the subsystems are ready.");
        }

        [UnityTest]
        public IEnumerator ItHears_EveryLevelBeginningAndEnding()
        {
            TestGate.Log.Clear();

            OpenLevel();

            yield return null;

            IndexOf("Game.LevelBeganPlay");

            IndexOf("First.LevelBeganPlay");

            Assert.Less(IndexOf("Level.BeginPlay"), IndexOf("Game.LevelBeganPlay"), "After the level's own subsystems.");

            World.End_Internal();

            Assert.Less(IndexOf("Game.LevelEndingPlay"), IndexOf("Level.Deinitialize"), "While the level is still alive.");

            IndexOf("First.LevelEndingPlay");
        }

        [Test]
        public void Shutdown_RunsBeforeTheSubsystemsGoAway()
        {
            TestGate.Log.Clear();

            GameFramework.Shutdown();

            Assert.Less(IndexOf("Game.Shutdown"), IndexOf("First.Deinitialize"));
        }
    }
}
