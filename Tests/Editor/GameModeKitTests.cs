using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using UnityEditor.SceneManagement;
using ModernWestern.UEArchitecture.Tests;

namespace ModernWestern.UEArchitecture.EditorTools.Tests
{
    /// <summary>
    /// The Game Mode Kit: the plan's order and reuse, a set made of existing classes wired at once (prefabs, fields, the
    /// level's GameMode, Player Starts), the step that finishes a set after its compile, and the command line run twice.
    /// </summary>
    public class GameModeKitTests
    {
        private const string Folder = "Assets/__UEATempKit";

        private const string HiddenFolder = "Assets/__UEATempKitScripts~";

        private static readonly GeneratorConventions Conventions = new("Assets/Scripts", "Assets/Prefabs", "Gameplay", "UI", "{Root}/{Area}", "{Class}", "{Root}/Modes/{Scene}");

        private EditorTestKit _kit;

        [SetUp]
        public void SetUp()
        {
            _kit = new EditorTestKit(Folder);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var step in PendingSteps.All().Where(step => step.kind == UEArchitectureActions.GameModeKitKind && step.Arg("name") == "UeaTempKit").ToArray())
            {
                PendingSteps.Cancel(step.id);
            }

            if (Directory.Exists(HiddenFolder))
            {
                Directory.Delete(HiddenFolder, true);
            }

            _kit.Dispose();
        }

        [Test]
        public void ThePlan_WritesNewClassesAndBuildsTheGameModeLast()
        {
            var plan = GameModeKitPlan.Build(new KitRequest { name = "Arena", aiController = new KitChoice(KitChoiceMode.New) }, _ => null, ClassRequestTypes.Find, _ => "Game",
                                             Conventions);

            Assert.IsTrue(plan.Ok, string.Join("\n", plan.Problems));

            CollectionAssert.AreEqual(new[] { "ArenaGameMode", "ArenaPlayerController", "ArenaPawn", "ArenaHUD", "ArenaAIController" },
                                      plan.Scripts.Select(script => CreateFrameworkScripts.ClassName(script.Parent == typeof(GameMode) ? script.TypedName + "GameMode" : script.TypedName))
                                          .ToArray());

            CollectionAssert.AreEqual(new[] { "AI Controller", "Player Controller", "Default Pawn", "HUD", "GameMode" }, plan.PrefabOrder.Select(slot => slot.Row).ToArray(),
                                      "The GameMode points to the others, so it is built last; the AI Controller before the Pawn that points to it.");

            CollectionAssert.AreEqual(new[] { "playerController", "defaultPawn", "hud", "aiController" }, plan.Assignments.Select(item => item.Field).ToArray());

            Assert.AreEqual("Game.ArenaGameState", plan.GameState.NewClass, "The new GameState is written with the GameMode.");

            Assert.IsFalse(plan.Resolved);

            StringAssert.StartsWith("5 scripts, 5 prefabs, 4 fields", plan.Describe(new KitRequest(), 5));
        }

        [Test]
        public void ThePlan_ReusesClassesThatExist()
        {
            var existing = new Dictionary<string, Type> { { "Game.ArenaPawn", typeof(TestPawn) }, { "Game.ArenaGameMode", typeof(TestGameMode) } };

            var plan = GameModeKitPlan.Build(new KitRequest
            {
                name = "Arena",
                playerController = new KitChoice(KitChoiceMode.Existing, typeof(TestController).FullName),
                hud = new KitChoice(KitChoiceMode.Base),
                gameState = new KitChoice(KitChoiceMode.None)
            }, name => existing.TryGetValue(name, out var type) ? type : null, ClassRequestTypes.Find, _ => "Game", Conventions);

            Assert.IsTrue(plan.Ok, string.Join("\n", plan.Problems));

            CollectionAssert.IsEmpty(plan.Scripts, "Every class exists: nothing to write.");

            Assert.IsTrue(plan.Resolved);

            Assert.AreEqual(typeof(TestPawn), plan.Pawn.Type);

            Assert.AreEqual(typeof(TestController), plan.PlayerController.Type);

            Assert.AreEqual(typeof(HUD), plan.HUD.Type, "Base: the framework class.");

            Assert.AreEqual(typeof(TestGameState), plan.GameState.Type, "An existing GameMode keeps its own GameState.");

            Assert.IsTrue(plan.Notes.Any(note => note.Contains("TestGameMode already exists")), string.Join("\n", plan.Notes));

            var wrong = GameModeKitPlan.Build(new KitRequest { name = "Arena", pawn = new KitChoice(KitChoiceMode.Existing, typeof(TestController).FullName) }, _ => null, ClassRequestTypes.Find,
                                              _ => "Game", Conventions);

            StringAssert.Contains("Default Pawn", wrong.Problems.Single(), "A Player Controller is not a Pawn.");

            var noLevel = GameModeKitPlan.Build(new KitRequest { name = "Arena", useAs = KitDestination.ThisLevel }, _ => null, ClassRequestTypes.Find, _ => "Game", Conventions);

            Assert.IsFalse(noLevel.Ok, "This level needs a level.");

            var newState = GameModeKitPlan.Build(new KitRequest { name = "Arena", playerState = new KitChoice(KitChoiceMode.Existing, typeof(TestPlayerState).FullName) }, _ => null,
                                                 ClassRequestTypes.Find, _ => "Game", Conventions);

            Assert.AreEqual(typeof(TestPlayerState), newState.Scripts[0].PlayerState, "The new GameMode names the existing PlayerState.");

            Assert.IsNull(newState.Scripts[0].GameState, "and writes a new GameState.");
        }

        [Test]
        public void AKitOfExistingClasses_IsWiredAtOnce()
        {
            var scene = _kit.CreateLevel("Arena");

            var request = ExistingRequest_Internal(scene.path);

            var result = UEArchitectureActions.CreateGameModeKit(request);

            Assert.IsTrue(result.Ok, string.Join("\n", result.Problems));

            Assert.IsFalse(result.Pending, "No new class: nothing waits.");

            var gameMode = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/Prefabs/Gameplay/TestGameMode.prefab")?.GetComponent<GameMode>();

            Assert.IsNotNull(gameMode, string.Join("\n", result.Created));

            Assert.IsInstanceOf<TestController>(gameMode.PlayerControllerPrefab);

            Assert.IsInstanceOf<TestPawn>(gameMode.DefaultPawnPrefab);

            Assert.IsInstanceOf<TestHUD>(gameMode.HUDPrefab);

            Assert.IsInstanceOf<TestAI>(new SerializedObject(gameMode.DefaultPawnPrefab).FindProperty("aiController").objectReferenceValue, "The Pawn's AI Controller.");

            Assert.IsNotNull(gameMode.DefaultPawnPrefab.transform.Find("Body"), "The capsule preset.");

            Assert.IsNull(gameMode.DefaultPawnPrefab.GetComponentInChildren<Collider>(), "The capsule's collider is removed.");

            if (UiBuilders.Available)
            {
                Assert.IsNotNull(gameMode.HUDPrefab.GetComponent<Canvas>(), "The HUD is a Canvas.");
            }

            Assert.AreEqual(gameMode, GameModeInfo.Find<WorldSettings>(scene).GameModeOverride, "The level uses it.");

            Assert.AreEqual(1, LevelSetup.Starts_Internal(scene).Count, "One Player Start per player.");

            var again = UEArchitectureActions.CreateGameModeKit(request);

            Assert.IsTrue(again.Ok, string.Join("\n", again.Problems));

            CollectionAssert.IsEmpty(again.Created, "Nothing was missing.");

            CollectionAssert.IsEmpty(again.Changed);
        }

        [Test]
        public void TheKit_FillsOnlyEmptySlotsAndCanBeTheProjectDefault()
        {
            var gameModePrefab = _kit.Prefab("TestGameMode", typeof(TestGameMode));

            var otherPawn = _kit.Prefab("OtherPawn", typeof(Pawn));

            UEArchitectureActions.AssignClass(gameModePrefab.GetComponent<GameMode>(), "defaultPawn", null, otherPawn);

            var request = ExistingRequest_Internal(string.Empty);

            request.useAs = KitDestination.ProjectDefault;

            var result = UEArchitectureActions.CreateGameModeKit(request);

            Assert.IsTrue(result.Ok, string.Join("\n", result.Problems));

            var gameMode = gameModePrefab.GetComponent<GameMode>();

            Assert.AreEqual(otherPawn.GetComponent<Pawn>(), gameMode.DefaultPawnPrefab, "A slot that was set keeps its value.");

            Assert.IsTrue(result.Notes.Any(note => note.Contains("keeps OtherPawn")), string.Join("\n", result.Notes));

            Assert.IsInstanceOf<TestController>(gameMode.PlayerControllerPrefab, "Empty slots are filled.");

            Assert.AreEqual(gameMode, FrameworkIndex.Settings.DefaultGameMode, "The project default (the settings asset is created when missing).");
        }

        [Test]
        public void NewGameModeKit_Twice_FromTheCommandLine()
        {
            var scene = _kit.CreateLevel("Arena");

            EditorSceneManager.SaveScene(scene);

            var path = scene.path;

            EditorTestKit.Close(scene);

            var arguments = new[]
            {
                "name=Kit", "gamemode=" + typeof(TestGameMode).FullName, "player-controller=" + typeof(TestController).FullName, "pawn=" + typeof(TestPawn).FullName,
                "hud=" + typeof(TestHUD).FullName, "use=level", "scene=" + path, "pawn-preset=empty"
            };

            var first = UEArchitectureCli.Execute(Parse_Internal("new-gamemode-kit", arguments));

            Assert.IsTrue(first.Ok, string.Join("\n", first.Problems));

            Assert.AreEqual("new-gamemode-kit", first.Verb);

            CollectionAssert.Contains(first.Created, Folder + "/Prefabs/Gameplay/TestGameMode.prefab");

            CollectionAssert.Contains(AssetDatabase.GetDependencies(path, false), Folder + "/Prefabs/Gameplay/TestGameMode.prefab", "The level was saved with the GameMode.");

            var second = UEArchitectureCli.Execute(Parse_Internal("new-gamemode-kit", arguments));

            Assert.IsTrue(second.Ok, string.Join("\n", second.Problems));

            CollectionAssert.IsEmpty(second.Created, "The second run creates nothing.");

            CollectionAssert.IsEmpty(second.Changed, "The second run changes nothing.");

            Assert.IsFalse(UEArchitectureCli.Execute(Parse_Internal("new-gamemode-kit", "name=Kit", "pawn=NoSuchClass")).Ok, "An unknown class is reported.");
        }

        [Test]
        public void NewClasses_AreWrittenOnceAndWaitForTheCompile()
        {
            var arguments = new[] { "name=UeaTempKit", "folder=" + HiddenFolder, "hud=none", "ai-controller=none" };

            var first = UEArchitectureCli.Execute(Parse_Internal("new-gamemode-kit", arguments));

            Assert.IsTrue(first.Ok, string.Join("\n", first.Problems));

            Assert.IsTrue(first.Pending, "The prefabs wait for the compile.");

            CollectionAssert.IsSubsetOf(new[] { HiddenFolder + "/UeaTempKitGameMode.cs", HiddenFolder + "/UeaTempKitPlayerController.cs", HiddenFolder + "/UeaTempKitPawn.cs" },
                                        first.Created.ToArray());

            var step = PendingSteps.All().Single(item => item.kind == UEArchitectureActions.GameModeKitKind && item.Arg("name") == "UeaTempKit");

            Assert.AreEqual(3, step.scriptPaths.Length, "One step waits for every script of the set.");

            Assert.AreEqual("UeaTempKit", JsonUtility.FromJson<KitRequest>(step.Arg("request")).name, "The request survives in the step.");

            var second = UEArchitectureCli.Execute(Parse_Internal("new-gamemode-kit", arguments));

            Assert.IsTrue(second.Ok, string.Join("\n", second.Problems));

            Assert.IsTrue(second.Pending);

            CollectionAssert.IsEmpty(second.Created, "The second run writes nothing.");

            CollectionAssert.IsEmpty(second.Changed);

            Assert.AreEqual(1, PendingSteps.All().Count(item => item.kind == UEArchitectureActions.GameModeKitKind && item.Arg("name") == "UeaTempKit"), "Queued once.");
        }

        [Test]
        public void TheKitStep_WiresTheSetAfterTheCompile()
        {
            var store = new PendingStepStore(Path.Combine("Temp", "UEATest_Kit_" + Guid.NewGuid().ToString("N") + ".json").Replace('\\', '/'));

            try
            {
                var request = ExistingRequest_Internal(string.Empty);

                var step = PendingStep.Create(UEArchitectureActions.GameModeKitKind).WithArg("name", "Kit").WithArg("request", JsonUtility.ToJson(request));

                step.WithScript(Script_Internal(typeof(TestPawn)), typeof(TestPawn).FullName);

                step.CreatedUtc = DateTime.UtcNow.AddHours(-1);

                Assert.IsTrue(PendingSteps.Enqueue(step, new ActionResult("test"), store));

                var environment = new PendingEnvironment(DateTime.UtcNow, false, _ => true, name => name == typeof(TestPawn).FullName ? typeof(TestPawn) : null, _ => typeof(TestPawn), null);

                var result = PendingSteps.Run_Internal(store, "complete-pending", environment);

                Assert.IsTrue(result.Ok, string.Join("\n", result.Problems));

                Assert.IsFalse(store.Exists, "The step finished.");

                var gameMode = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/Prefabs/Gameplay/TestGameMode.prefab").GetComponent<GameMode>();

                Assert.IsInstanceOf<TestPawn>(gameMode.DefaultPawnPrefab, "Wired by the step.");
            }
            finally
            {
                if (File.Exists(store.Path))
                {
                    File.Delete(store.Path);
                }
            }
        }

        private static KitRequest ExistingRequest_Internal(string scenePath)
        {
            return new KitRequest
            {
                name = "Kit",
                gameMode = new KitChoice(KitChoiceMode.Existing, typeof(TestGameMode).FullName),
                playerController = new KitChoice(KitChoiceMode.Existing, typeof(TestController).FullName),
                pawn = new KitChoice(KitChoiceMode.Existing, typeof(TestPawn).FullName),
                hud = new KitChoice(KitChoiceMode.Existing, typeof(TestHUD).FullName),
                aiController = new KitChoice(KitChoiceMode.Existing, typeof(TestAI).FullName),
                pawnPreset = PawnPreset.Capsule,
                useAs = string.IsNullOrEmpty(scenePath) ? KitDestination.Nothing : KitDestination.ThisLevel,
                scenePath = scenePath
            };
        }

        private static string Script_Internal(Type type)
        {
            return AssetDatabase.FindAssets(type.Name + " t:MonoScript").Select(AssetDatabase.GUIDToAssetPath).First(path => path.EndsWith("/" + type.Name + ".cs", StringComparison.Ordinal));
        }

        private static CliArguments Parse_Internal(string verb, params string[] pairs)
        {
            return CliArguments.Parse(new[] { "Unity.exe", "-uea", verb }.Concat(pairs).ToArray());
        }
    }
}
