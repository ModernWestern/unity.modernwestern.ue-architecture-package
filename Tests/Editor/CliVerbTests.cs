using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using ModernWestern.UEArchitecture.Tests;

namespace ModernWestern.UEArchitecture.EditorTools.Tests
{
    /// <summary>
    /// Every command-line verb of the create-and-wire tools, run twice: the second run must report nothing created.
    /// </summary>
    public class CliVerbTests
    {
        private const string Folder = "Assets/__UEATempCliVerbs";

        private EditorTestKit _kit;

        [SetUp]
        public void SetUp()
        {
            _kit = new EditorTestKit(Folder);
        }

        [TearDown]
        public void TearDown()
        {
            _kit.Dispose();
        }

        [Test]
        public void CreateClassPrefab_Twice()
        {
            var first = Twice_Internal("create-class-prefab", "class=" + typeof(TestController).FullName);

            CollectionAssert.Contains(first.Created, Folder + "/Prefabs/Gameplay/TestController.prefab");
        }

        [Test]
        public void AssignClass_Twice()
        {
            var gameModePath = AssetDatabase.GetAssetPath(_kit.Prefab("ArenaGameMode", typeof(TestGameMode)));

            var first = Twice_Internal("assign-class", "target=" + gameModePath, "property=hud", "class=TestHUD");

            CollectionAssert.Contains(first.Created, Folder + "/Prefabs/UI/TestHUD.prefab");

            Assert.AreEqual(typeof(TestHUD), AssetDatabase.LoadAssetAtPath<GameObject>(gameModePath).GetComponent<GameMode>().HUDPrefab.GetType());

            var cleared = UEArchitectureCli.Execute(Parse_Internal("assign-class", "target=" + gameModePath, "property=hud", "none=true"));

            Assert.IsTrue(cleared.Ok, string.Join("\n", cleared.Problems));

            Assert.IsNull(AssetDatabase.LoadAssetAtPath<GameObject>(gameModePath).GetComponent<GameMode>().HUDPrefab);

            Assert.IsFalse(UEArchitectureCli.Execute(Parse_Internal("assign-class", "target=" + gameModePath, "property=hud")).Ok, "class, prefab or none is needed.");
        }

        [Test]
        public void OverrideGameMode_Twice_OnALevelThatIsNotOpen()
        {
            var scene = _kit.CreateLevel("Boss");

            var arena = _kit.Prefab("ArenaGameMode", typeof(TestGameMode));

            UEArchitectureActions.UseGameModeInLevel(arena, scene);

            EditorSceneManager.SaveScene(scene);

            var path = scene.path;

            EditorTestKit.Close(scene);

            var first = Twice_Internal("override-gamemode", "scene=" + path);

            var variantPath = _kit.LevelFolder("Boss") + "/TestGameMode.prefab";

            CollectionAssert.Contains(first.Created, variantPath);

            CollectionAssert.Contains(AssetDatabase.GetDependencies(path, false), variantPath, "The level was saved with its own GameMode.");

            Assert.IsFalse(SceneManager.GetSceneByPath(path).isLoaded, "The level the command opened is closed again.");
        }

        [Test]
        public void SetDefaultGameMode_Twice()
        {
            var arena = AssetDatabase.GetAssetPath(_kit.Prefab("ArenaGameMode", typeof(TestGameMode)));

            Twice_Internal("set-default-gamemode", "prefab=" + arena);

            Assert.AreEqual(arena, AssetDatabase.GetAssetPath(FrameworkIndex.Settings.DefaultGameMode));
        }

        [Test]
        public void UseGameModeAndClearOverride_Twice()
        {
            var scene = _kit.CreateLevel("Arena");

            var arena = AssetDatabase.GetAssetPath(_kit.Prefab("ArenaGameMode", typeof(TestGameMode)));

            var used = Twice_Internal("use-gamemode", "prefab=" + arena, "scene=" + scene.path);

            Assert.IsTrue(used.Created.Any(created => created.EndsWith("#World Settings")), "World Settings was added the first time.");

            Assert.AreEqual(arena, AssetDatabase.GetAssetPath(GameModeInfo.Find<WorldSettings>(scene).GameModeOverride));

            Twice_Internal("clear-override", "scene=" + scene.path);

            Assert.IsNull(GameModeInfo.Find<WorldSettings>(scene).GameModeOverride);
        }

        [Test]
        public void CreateMissingDefaultClasses_Twice()
        {
            var gameModePath = AssetDatabase.GetAssetPath(_kit.Prefab("ArenaGameMode", typeof(TestGameMode)));

            var first = Twice_Internal("create-missing-default-classes", "target=" + gameModePath, "folder=" + Folder + "/Defaults");

            Assert.AreEqual(3, first.Created.Count(created => created.EndsWith(".prefab")), "Player Controller, Pawn and HUD.");
        }

        [Test]
        public void FillSceneReferences_Twice()
        {
            var scene = _kit.CreateLevel("Gameplay");

            var holder = new GameObject("GameplayGameMode");

            SceneManager.MoveGameObjectToScene(holder, scene);

            var gameMode = holder.AddComponent<TestLevelGameMode>();

            var player = new GameObject("Player");

            SceneManager.MoveGameObjectToScene(player, scene);

            player.AddComponent<TestPawn>();

            EditorSceneManager.SaveScene(scene);

            var first = UEArchitectureCli.Execute(Parse_Internal("fill-scene-references", "target=" + scene.path + "#GameplayGameMode"));

            Assert.IsTrue(first.Ok, string.Join("\n", first.Problems));

            Assert.IsTrue(first.Changed.Any(changed => changed.EndsWith("playerCharacter")));

            Assert.AreEqual(player, gameMode.PlayerCharacter.gameObject);

            var second = UEArchitectureCli.Execute(Parse_Internal("fill-scene-references", "target=" + scene.path + "#GameplayGameMode"));

            Assert.IsTrue(second.Ok);

            CollectionAssert.IsEmpty(second.Created);

            CollectionAssert.IsEmpty(second.Changed, "The second run changes nothing.");
        }

        [Test]
        public void AssignProjectClass_Twice_CreatesTheSettingsWhenMissing()
        {
            var first = Twice_Internal("assign-project-class", "property=gameInstance", "class=" + typeof(TestGameInstance).FullName);

            Assert.IsNotNull(FrameworkIndex.Settings, "The settings asset exists after the first run.");

            Assert.IsInstanceOf<TestGameInstance>(FrameworkIndex.Settings.GameInstancePrefab);

            Assert.IsTrue(first.Created.Any(created => created.EndsWith("TestGameInstance.prefab")), string.Join("\n", first.Created));

            Assert.IsFalse(UEArchitectureCli.Execute(Parse_Internal("assign-project-class", "property=gameInstance")).Ok, "class, prefab or none is needed.");

            var unknown = UEArchitectureCli.Execute(Parse_Internal("assign-project-class", "property=nothing", "none=true"));

            StringAssert.Contains("no field \"nothing\"", unknown.Problems.Single());
        }

        [Test]
        public void CreateVariant_ReusesTheLevelsVariant()
        {
            var basePawn = _kit.Prefab("ArenaPawn", typeof(TestPawn));

            var gameModePath = AssetDatabase.GetAssetPath(_kit.Prefab("ArenaGameMode", typeof(TestGameMode)));

            var gameMode = AssetDatabase.LoadAssetAtPath<GameObject>(gameModePath).GetComponent<GameMode>();

            UEArchitectureActions.AssignClass(gameMode, "defaultPawn", null, basePawn);

            var first = UEArchitectureCli.Execute(Parse_Internal("create-variant", "target=" + gameModePath, "property=defaultPawn", "level=Boss"));

            Assert.IsTrue(first.Ok, string.Join("\n", first.Problems));

            Assert.AreEqual("create-variant", first.Verb);

            var variantPath = _kit.LevelFolder("Boss") + "/TestPawn.prefab";

            CollectionAssert.Contains(first.Created, variantPath);

            Assert.AreEqual(variantPath, AssetDatabase.GetAssetPath(gameMode.DefaultPawnPrefab));

            UEArchitectureActions.AssignClass(gameMode, "defaultPawn", null, basePawn);

            var second = UEArchitectureCli.Execute(Parse_Internal("create-variant", "target=" + gameModePath, "property=defaultPawn", "level=Boss"));

            Assert.IsTrue(second.Ok, string.Join("\n", second.Problems));

            CollectionAssert.IsEmpty(second.Created, "The level's variant is reused.");

            Assert.AreEqual(variantPath, AssetDatabase.GetAssetPath(gameMode.DefaultPawnPrefab));
        }

        [Test]
        public void SaveAsPrefab_Twice()
        {
            var scene = _kit.CreateLevel("Placed");

            var holder = new GameObject("GameplayGameMode");

            SceneManager.MoveGameObjectToScene(holder, scene);

            var gameMode = holder.AddComponent<TestGameMode>();

            var controller = new GameObject("Player Input");

            SceneManager.MoveGameObjectToScene(controller, scene);

            UEArchitectureActions.Assign(gameMode, "playerController", controller.AddComponent<TestController>());

            EditorSceneManager.SaveScene(scene);

            var arguments = new[] { "target=" + scene.path + "#GameplayGameMode", "property=playerController", "path=" + Folder + "/PlayerInput.prefab" };

            var first = UEArchitectureCli.Execute(Parse_Internal("save-as-prefab", arguments));

            Assert.IsTrue(first.Ok, string.Join("\n", first.Problems));

            CollectionAssert.Contains(first.Created, Folder + "/PlayerInput.prefab");

            Assert.AreEqual(Folder + "/PlayerInput.prefab", AssetDatabase.GetAssetPath(gameMode.PlayerControllerPrefab));

            var second = UEArchitectureCli.Execute(Parse_Internal("save-as-prefab", arguments));

            Assert.IsTrue(second.Ok, string.Join("\n", second.Problems));

            CollectionAssert.IsEmpty(second.Created, "Already a prefab: nothing new.");
        }

        [Test]
        public void RevertOverride_Twice()
        {
            var basePrefab = _kit.Prefab("ArenaGameMode", typeof(TestGameMode));

            GameObject variant;

            using (var op = Scaffold.Begin("Variant"))
            {
                variant = op.CreateVariant(basePrefab, Folder + "/BossGameMode.prefab");
            }

            UEArchitectureActions.Assign(variant.GetComponent<GameMode>(), "playersAtStart", 3);

            var first = Twice_Internal("revert-override", "target=" + Folder + "/BossGameMode.prefab", "property=playersAtStart");

            Assert.AreEqual(1, first.Changed.Count);

            Assert.AreEqual(1, AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/BossGameMode.prefab").GetComponent<GameMode>().PlayersAtStart, "Back to the base's value.");
        }

        [Test]
        public void CreateGameModeVariant_Twice()
        {
            var scene = _kit.CreateLevel("Boss");

            var arena = AssetDatabase.GetAssetPath(_kit.Prefab("ArenaGameMode", typeof(TestGameMode)));

            var first = Twice_Internal("create-gamemode-variant", "prefab=" + arena, "scene=" + scene.path);

            var variantPath = _kit.LevelFolder("Boss") + "/TestGameMode.prefab";

            CollectionAssert.Contains(first.Created, variantPath);

            Assert.AreEqual(variantPath, AssetDatabase.GetAssetPath(GameModeInfo.Find<WorldSettings>(scene).GameModeOverride));
        }

        [Test]
        public void CompletePending_WithNothingQueued()
        {
            Assume.That(PendingSteps.Count == 0, "Steps are queued in this project.");

            var result = UEArchitectureCli.Execute(Parse_Internal("complete-pending"));

            Assert.IsTrue(result.Ok);

            Assert.IsFalse(result.Pending);

            CollectionAssert.IsEmpty(result.Created);

            StringAssert.Contains("Nothing pending", result.Message);
        }

        [Test]
        public void Help_ListsEveryVerb()
        {
            var help = UEArchitectureCli.Execute(Parse_Internal("help")).Message;

            foreach (var verb in new[]
                     {
                         "assign-class", "create-class-prefab", "override-gamemode", "set-default-gamemode", "use-gamemode", "clear-override", "create-missing-default-classes",
                         "fill-scene-references", "complete-pending", "assign-project-class", "create-variant", "save-as-prefab", "revert-override", "create-gamemode-variant",
                         "create-class", "new-gamemode-kit", "create-level", "set-up-level", "add-player-starts", "create-hud-prefab", "create-loading-screen", "find-children",
                         "validate", "fix-all"
                     })
            {
                StringAssert.Contains(verb, help);
            }
        }

        private static ActionResult Twice_Internal(string verb, params string[] pairs)
        {
            var first = UEArchitectureCli.Execute(Parse_Internal(verb, pairs));

            Assert.IsTrue(first.Ok, $"{verb}: {string.Join("\n", first.Problems)}");

            Assert.AreEqual(verb, first.Verb);

            var second = UEArchitectureCli.Execute(Parse_Internal(verb, pairs));

            Assert.IsTrue(second.Ok, $"{verb} again: {string.Join("\n", second.Problems)}");

            CollectionAssert.IsEmpty(second.Created, $"{verb}: the second run creates nothing.");

            CollectionAssert.IsEmpty(second.Changed, $"{verb}: the second run changes nothing.");

            return first;
        }

        private static CliArguments Parse_Internal(string verb, params string[] pairs)
        {
            return CliArguments.Parse(new[] { "Unity.exe", "-uea", verb }.Concat(pairs).ToArray());
        }
    }
}
