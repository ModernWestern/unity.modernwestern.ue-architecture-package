using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using ModernWestern.UEArchitecture.Tests;

namespace ModernWestern.UEArchitecture.EditorTools.Tests
{
    /// <summary>
    /// New Framework Class: the script of every kind of class, the GameMode's chosen GameState and PlayerState, name
    /// clashes, the asmdef reference fix, and the command line run twice (the second run writes nothing).
    /// </summary>
    public class NewClassTests
    {
        private static readonly GeneratorConventions Conventions = new("Assets/Scripts", "Assets/Prefabs", "Gameplay", "UI", "{Root}/{Area}", "{Class}", "{Root}/Modes/{Scene}");

        // Unity does not import folders whose name ends in "~": scripts written there never compile during the test run.
        private const string HiddenFolder = "Assets/__UEATempNewClass~";

        [TearDown]
        public void TearDown()
        {
            foreach (var step in PendingSteps.All().Where(step => step.scriptPaths.Any(path => path.StartsWith(HiddenFolder, StringComparison.Ordinal))).ToArray())
            {
                PendingSteps.Cancel(step.id);
            }

            if (Directory.Exists(HiddenFolder))
            {
                Directory.Delete(HiddenFolder, true);
            }
        }

        [TestCase("Game Mode", "ModernWestern.UEArchitecture.GameMode", false, "ArenaGameMode : GameMode<ArenaGameState, ArenaPlayerState>")]
        [TestCase("Player Controller", "ModernWestern.UEArchitecture.PlayerController", false, "Arena : PlayerController")]
        [TestCase("AI Controller", "ModernWestern.UEArchitecture.AIController", false, "Arena : AIController")]
        [TestCase("Pawn", "ModernWestern.UEArchitecture.Pawn", false, "Arena : Pawn")]
        [TestCase("HUD", "ModernWestern.UEArchitecture.HUD", false, "Arena : HUD")]
        [TestCase("Game Instance", "ModernWestern.UEArchitecture.GameInstance", false, "Arena : GameInstance")]
        [TestCase("Game Subsystem", "ModernWestern.UEArchitecture.GameInstanceSubsystem", false, "Arena : GameInstanceSubsystem")]
        [TestCase("World Subsystem", "ModernWestern.UEArchitecture.WorldSubsystem", false, "Arena : WorldSubsystem")]
        [TestCase("Loading Screen", "ModernWestern.UEArchitecture.LoadingScreen", false, "Arena : LoadingScreen")]
        [TestCase("Save Game", "ModernWestern.UEArchitecture.SaveGame", false, "Arena : SaveGame")]
        [TestCase("Message", "", true, "public struct Arena")]
        [TestCase("Player Start", "ModernWestern.UEArchitecture.PlayerStart", false, "Arena : PlayerStart")]
        public void EveryCommonClass_WritesItsTemplate(string label, string parent, bool message, string declaration)
        {
            var request = new ClassRequest { className = "Arena", parentClass = parent, message = message, folder = "Assets/Game" };

            var plan = Plan_Internal(request);

            Assert.IsTrue(plan.Ok, label + ": " + string.Join("\n", plan.Problems));

            var source = plan.Scripts[0].Source;

            StringAssert.Contains(declaration, source, label);

            StringAssert.Contains("namespace Game", source, "The folder's root namespace.");

            Assert.AreEqual(source.Count(c => c == '{'), source.Count(c => c == '}'), label + ": balanced braces.");
        }

        [Test]
        public void ATickableSubsystem_WritesItsTickAttribute()
        {
            var request = new ClassRequest
            {
                className = "CameraRig",
                parentClass = typeof(WorldSubsystem).FullName,
                tickable = true,
                tickGroup = TickGroup.LateUpdate,
                tickOrder = -10,
                unscaled = true,
                folder = "Assets/Game"
            };

            var source = Plan_Internal(request).Scripts.Single().Source;

            StringAssert.Contains("[Tick(TickGroup.LateUpdate, Order = -10, Unscaled = true)]", source);

            StringAssert.Contains("public sealed class CameraRig : WorldSubsystem, ITickable", source);

            var refused = UEArchitectureActions.ScriptRequestOf_Internal(new ClassRequest { className = "X", parentClass = typeof(Pawn).FullName, tickable = true }, out var problem);

            Assert.IsNull(refused, "Only subsystems tick by themselves.");

            StringAssert.Contains("Game Instance Subsystem or a World Subsystem", problem);
        }

        [Test]
        public void AGameMode_UsesExistingStatesAndWritesOnlyTheNewOnes()
        {
            var request = new ClassRequest
            {
                className = "Arena",
                parentClass = typeof(GameMode).FullName,
                gameStateClass = typeof(TestGameState).FullName,
                stateFiles = StateFiles.OwnFiles,
                folder = "Assets/Game"
            };

            var plan = Plan_Internal(request);

            Assert.IsTrue(plan.Ok, string.Join("\n", plan.Problems));

            CollectionAssert.AreEqual(new[] { "Assets/Game/ArenaGameMode.cs", "Assets/Game/ArenaPlayerState.cs" }, plan.Scripts.Select(script => script.Path).ToArray(),
                                      "The existing GameState drops its file.");

            var source = plan.Scripts[0].Source;

            StringAssert.Contains("public class ArenaGameMode : GameMode<TestGameState, ArenaPlayerState>", source);

            StringAssert.Contains("using " + typeof(TestGameState).Namespace + ";", source);

            StringAssert.Contains("ArenaPlayerState.cs", source, "Says where the other class is.");

            Assert.AreEqual(source.Count(c => c == '{'), source.Count(c => c == '}'));

            var sameFile = Plan_Internal(new ClassRequest
            {
                className = "Arena",
                parentClass = typeof(GameMode).FullName,
                playerStateClass = typeof(TestPlayerState).FullName,
                stateFiles = StateFiles.SameFile,
                folder = "Assets/Game"
            });

            Assert.AreEqual(1, sameFile.Scripts.Count);

            CollectionAssert.AreEqual(new[] { "Game.ArenaGameMode", "Game.ArenaGameState" }, sameFile.Scripts[0].DeclaredClasses);

            StringAssert.Contains("public class ArenaGameState : GameState", sameFile.Scripts[0].Source);

            Assert.IsNull(UEArchitectureActions.ScriptRequestOf_Internal(new ClassRequest { className = "Arena", parentClass = typeof(GameMode).FullName, gameStateClass = "Nothing.Here" },
                                                                         out var problem));

            StringAssert.Contains("Nothing.Here", problem);
        }

        [Test]
        public void AClassThatExists_IsAClash()
        {
            var request = UEArchitectureActions.ScriptRequestOf_Internal(new ClassRequest { className = "Boss", parentClass = typeof(Pawn).FullName, folder = "Assets/Game" }, out _);

            var plan = ScriptWriter.Plan(new[] { request }, Context_Internal(name => name == "Game.Boss" ? typeof(TestPawn) : null), Conventions);

            Assert.IsFalse(plan.Ok);

            StringAssert.Contains("already exists", plan.Problems.Single());
        }

        [Test]
        public void AMissingAsmdefReference_IsFixableAndThePatchKeepsTheStyle()
        {
            var request = UEArchitectureActions.ScriptRequestOf_Internal(new ClassRequest { className = "Boss", parentClass = typeof(Pawn).FullName, folder = "Assets/Game" }, out _);

            var assembly = new ScriptAssembly("Game", "Assets/Game/Game.asmdef", new[] { "Unity.InputSystem" }, true);

            var plan = ScriptWriter.Plan(new[] { request }, new ScriptPlanContext(_ => null, _ => false, _ => "Game", _ => assembly), Conventions);

            Assert.AreEqual(1, plan.MissingReferences.Count);

            Assert.AreEqual(ScriptWriter.RuntimeAssembly, plan.MissingReferences[0].Assembly);

            Assert.AreEqual("Assets/Game/Game.asmdef", plan.MissingReferences[0].AsmdefPath);

            Func<string, string> guidOf = name => name == ScriptWriter.RuntimeAssembly ? "abc123" : string.Empty;

            var guids = "{\n    \"name\": \"Game\",\n    \"references\": [\n        \"GUID:fff\"\n    ],\n    \"autoReferenced\": true\n}\n";

            var patched = AsmdefReferences.Add(guids, new[] { ScriptWriter.RuntimeAssembly }, guidOf);

            StringAssert.Contains("\"GUID:fff\",\n        \"GUID:abc123\"\n    ]", patched, "GUID style kept.");

            StringAssert.Contains("\"autoReferenced\": true", patched, "Nothing else changes.");

            Assert.AreEqual(patched, AsmdefReferences.Add(patched, new[] { ScriptWriter.RuntimeAssembly }, guidOf), "Already there: unchanged.");

            var names = AsmdefReferences.Add("{\n    \"name\": \"Game\",\n    \"references\": [\"Unity.InputSystem\"]\n}", new[] { ScriptWriter.RuntimeAssembly }, guidOf);

            StringAssert.Contains("\"Unity.InputSystem\",\n        \"UEArchitecture.Runtime\"", names, "Name style kept.");

            var none = AsmdefReferences.Add("{\n    \"name\": \"Game\"\n}", new[] { ScriptWriter.RuntimeAssembly }, guidOf);

            StringAssert.Contains("\"references\": [", none);

            StringAssert.Contains("\"GUID:abc123\"", none, "A new list uses Unity's default GUID style.");
        }

        [Test]
        public void TheAsmdefFix_WritesTheFileOnce()
        {
            Directory.CreateDirectory(HiddenFolder);

            var path = HiddenFolder + "/Game.asmdef";

            File.WriteAllText(path, "{\n    \"name\": \"Game\",\n    \"references\": []\n}\n");

            var missing = new[] { new MissingReference(path, ScriptWriter.RuntimeAssembly, "problem") };

            var first = AsmdefReferences.Apply(missing);

            Assert.IsTrue(first.Ok, string.Join("\n", first.Problems));

            Assert.AreEqual(1, first.Changed.Count);

            StringAssert.Contains("GUID:", File.ReadAllText(path), "UEArchitecture.Runtime by its asmdef's GUID.");

            var second = AsmdefReferences.Apply(missing);

            CollectionAssert.IsEmpty(second.Changed, "Already there.");
        }

        [Test]
        public void CreateClass_Twice_FromTheCommandLine()
        {
            var arguments = new[] { "class=UeaTempBossPawn", "parent=Pawn", "folder=" + HiddenFolder, "prefab=true" };

            var first = UEArchitectureCli.Execute(Parse_Internal("create-class", arguments));

            Assert.IsTrue(first.Ok, string.Join("\n", first.Problems));

            Assert.IsTrue(first.Pending, "The prefab waits for the compile.");

            CollectionAssert.Contains(first.Created, HiddenFolder + "/UeaTempBossPawn.cs");

            Assert.AreEqual(UEArchitectureCli.PendingExitCode, UEArchitectureCli.ExitCodeOf_Internal(first));

            StringAssert.Contains("public class UeaTempBossPawn : Pawn", File.ReadAllText(HiddenFolder + "/UeaTempBossPawn.cs"));

            Assert.AreEqual(1, PendingSteps.All().Count(step => step.scriptPaths.Contains(HiddenFolder + "/UeaTempBossPawn.cs")));

            var second = UEArchitectureCli.Execute(Parse_Internal("create-class", arguments));

            Assert.IsTrue(second.Ok, string.Join("\n", second.Problems));

            CollectionAssert.IsEmpty(second.Created, "The second run writes nothing.");

            CollectionAssert.IsEmpty(second.Changed);

            Assert.AreEqual(1, PendingSteps.All().Count(step => step.scriptPaths.Contains(HiddenFolder + "/UeaTempBossPawn.cs")), "Queued once.");

            var noParent = UEArchitectureCli.Execute(Parse_Internal("create-class", "class=Nobody"));

            Assert.IsFalse(noParent.Ok, "A parent (or message=true) is needed.");
        }

        [Test]
        public void CreateClass_OverAFileWithAnotherClass_IsRefused()
        {
            Directory.CreateDirectory(HiddenFolder);

            var path = HiddenFolder + "/UeaTempPlayer.cs";

            File.WriteAllText(path, "namespace Elsewhere\n{\n    public class Inventory\n    {\n    }\n}\n");

            var result = UEArchitectureCli.Execute(Parse_Internal("create-class", "class=UeaTempPlayer", "parent=Pawn", "folder=" + HiddenFolder));

            Assert.IsFalse(result.Ok, "Nothing was created: the file holds another class.");

            StringAssert.Contains("another class", string.Join("\n", result.Problems));

            Assert.AreEqual(1, UEArchitectureCli.ExitCodeOf_Internal(result));

            StringAssert.Contains("class Inventory", File.ReadAllText(path), "The file is left alone.");
        }

        [Test]
        public void CreateClass_ForACompiledClass_FinishesItsPrefabOnly()
        {
            using (var kit = new EditorTestKit("Assets/__UEATempNewClassCompiled"))
            {
                // TestPawn stands in for a class written earlier: the script exists and is compiled.
                var script = AssetDatabase.FindAssets("TestPawn t:MonoScript").Select(AssetDatabase.GUIDToAssetPath).First(path => path.EndsWith("/TestPawn.cs", StringComparison.Ordinal));

                var gameMode = kit.Prefab("ArenaGameMode", typeof(TestGameMode)).GetComponent<GameMode>();

                var request = new ClassRequest
                {
                    className = "TestPawn",
                    parentClass = typeof(Pawn).FullName,
                    folder = GeneratorNaming.FolderOf(script),
                    withPrefab = true
                };

                var first = UEArchitectureActions.CreateClass(request, gameMode, "defaultPawn");

                Assert.IsTrue(first.Ok, string.Join("\n", first.Problems));

                Assert.IsFalse(first.Pending, "Nothing waits: the class is compiled.");

                Assert.IsInstanceOf<TestPawn>(gameMode.DefaultPawnPrefab, "Its prefab went into the field.");

                var second = UEArchitectureActions.CreateClass(request, gameMode, "defaultPawn");

                CollectionAssert.IsEmpty(second.Created);

                CollectionAssert.IsEmpty(second.Changed);
            }
        }

        [Test]
        public void TheParents_AreFrameworkClassesAndYours_NeverSealedOrTests()
        {
            var parents = ClassRequestTypes.Parents();

            CollectionAssert.Contains(parents, typeof(GameMode));

            CollectionAssert.Contains(parents, typeof(PlayerController));

            CollectionAssert.DoesNotContain(parents, typeof(TestPawn), "Test classes (and sealed ones) are not offered.");

            Assert.IsFalse(ClassRequestTypes.CanBeParent(typeof(GameMode<,>)), "Generic classes are not parents.");

            Assert.AreEqual(typeof(Pawn), ClassRequestTypes.Find("Pawn"), "Short names of UE Architecture classes work.");
        }

        private static ScriptPlan Plan_Internal(ClassRequest request)
        {
            var scriptRequest = UEArchitectureActions.ScriptRequestOf_Internal(request, out var problem);

            Assert.IsNotNull(scriptRequest, problem);

            return ScriptWriter.Plan(new[] { scriptRequest }, Context_Internal(_ => null), Conventions);
        }

        private static ScriptPlanContext Context_Internal(Func<string, Type> findType)
        {
            var assembly = new ScriptAssembly("Game", string.Empty, new[] { ScriptWriter.RuntimeAssembly, typeof(TestPawn).Assembly.GetName().Name }, true);

            return new ScriptPlanContext(name => findType(name) ?? (name.StartsWith("ModernWestern.", StringComparison.Ordinal) ? PendingSteps.FindClass(name) : null), _ => false, _ => "Game",
                                         _ => assembly);
        }

        private static CliArguments Parse_Internal(string verb, params string[] pairs)
        {
            return CliArguments.Parse(new[] { "Unity.exe", "-uea", verb }.Concat(pairs).ToArray());
        }
    }
}
