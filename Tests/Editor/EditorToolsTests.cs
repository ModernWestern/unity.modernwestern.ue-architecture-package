using System.Linq;
using NUnit.Framework;
using UnityEditor;
using ModernWestern.UEArchitecture.Tests;

namespace ModernWestern.UEArchitecture.EditorTools.Tests
{
    public class EditorToolsTests
    {
        [TestCase("arena game mode.cs", "ArenaGameMode")]
        [TestCase("Inventory", "Inventory")]
        [TestCase("2d level", "_2dLevel")]
        [TestCase("", "New")]
        public void ClassName_IsAValidIdentifier(string typed, string expected)
        {
            Assert.AreEqual(expected, CreateFrameworkScripts.ClassName(typed));
        }

        [TestCase("Arena", "ArenaGameMode")]
        [TestCase("ArenaMode", "ArenaGameMode")]
        [TestCase("ArenaGameMode", "ArenaGameMode")]
        public void GameModeScripts_AlwaysEndInGameMode(string typed, string expected)
        {
            Assert.AreEqual(expected, CreateFrameworkScripts.FileName(CreateFrameworkScripts.Kind.GameMode, typed));
        }

        [Test]
        public void TheGameModeTemplate_DeclaresItsGameStateAndPlayerState()
        {
            var source = CreateFrameworkScripts.Source(CreateFrameworkScripts.Kind.GameMode, "ArenaGameMode", null);

            StringAssert.Contains("public class ArenaGameMode : GameMode<ArenaGameState, ArenaPlayerState>", source);

            StringAssert.Contains("public class ArenaGameState : GameState", source);

            StringAssert.Contains("public class ArenaPlayerState : PlayerState", source);

            StringAssert.DoesNotContain("namespace", source);
        }

        [Test]
        public void Templates_GoIntoTheRootNamespace()
        {
            foreach (CreateFrameworkScripts.Kind kind in System.Enum.GetValues(typeof(CreateFrameworkScripts.Kind)))
            {
                var source = CreateFrameworkScripts.Source(kind, "Thing", "MyGame.Gameplay");

                StringAssert.Contains("namespace MyGame.Gameplay\n{", source, kind.ToString());

                Assert.IsTrue(source.IndexOf("using ModernWestern.UEArchitecture;", System.StringComparison.Ordinal) < source.IndexOf("namespace", System.StringComparison.Ordinal),
                              $"{kind}: usings stay above the namespace.");

                Assert.AreEqual(source.Count(c => c == '{'), source.Count(c => c == '}'), $"{kind}: balanced braces.");
            }
        }

        [Test]
        public void TheLinkXml_KeepsEverySubsystem()
        {
            var xml = FrameworkLinkXml.Build();

            StringAssert.Contains(typeof(FirstSubsystem).FullName, xml);

            StringAssert.Contains(typeof(LevelSubsystem).FullName, xml);

            StringAssert.DoesNotContain("GameInstanceSubsystem\"", xml, "Abstract classes need no rule.");
        }

        [Test]
        public void GameModeInfo_ReadsTheGameStateAndPlayerStateClasses()
        {
            Assert.AreEqual(typeof(TestGameState), GameModeInfo.GameStateType(typeof(TestGameMode)));

            Assert.AreEqual(typeof(TestPlayerState), GameModeInfo.PlayerStateType(typeof(TestGameMode)));

            Assert.AreEqual(typeof(GameState), GameModeInfo.GameStateType(typeof(GameMode)));
        }

        [TestCase("Assets/Resources/UEArchitectureSettings.asset", true)]
        [TestCase("Assets/Game/Settings/Resources/UEArchitectureSettings.asset", true)]
        [TestCase("Assets/Settings/UEArchitectureSettings.asset", false)]
        [TestCase("Assets/Resources/Settings.asset", false)]
        public void TheSettingsAsset_MustBeLoadableByPlayers(string path, bool fine)
        {
            Assert.AreEqual(fine, UEArchitectureSettingsProvider.LocationProblem(path).Length == 0);
        }

        [Test]
        public void BuildScenes_AddsAndEnablesScenes()
        {
            var original = EditorBuildSettings.scenes;

            const string path = "Assets/__UEArchitectureTestScene.unity";

            try
            {
                Assert.AreEqual(BuildScenes.Status.Missing, BuildScenes.Of(path));

                BuildScenes.Include(path);

                Assert.AreEqual(BuildScenes.Status.InBuild, BuildScenes.Of(path));

                EditorBuildSettings.scenes = EditorBuildSettings.scenes.Select(scene => new EditorBuildSettingsScene(scene.path, scene.path != path)).ToArray();

                Assert.AreEqual(BuildScenes.Status.Disabled, BuildScenes.Of(path));

                BuildScenes.Include(path);

                Assert.AreEqual(BuildScenes.Status.InBuild, BuildScenes.Of(path));

                Assert.AreEqual(1, EditorBuildSettings.scenes.Count(scene => scene.path == path), "Enabled, not added twice.");
            }
            finally
            {
                EditorBuildSettings.scenes = original;
            }
        }
    }
}
