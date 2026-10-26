using System;
using System.Linq;
using NUnit.Framework;
using System.Collections.Generic;
using ModernWestern.UEArchitecture.Tests;

namespace ModernWestern.UEArchitecture.EditorTools.Tests
{
    public class ScriptWriterTests
    {
        private static readonly GeneratorConventions Conventions = GeneratorConventions.Defaults;

        [Test]
        public void Plan_UsesTheFolderNamespaceAndTheTemplate()
        {
            var plan = ScriptWriter.Plan(new[] { new ScriptRequest("arena pawn", typeof(Pawn), "Assets/Game/") }, Context_Internal(rootNamespace: "Game.Gameplay"), Conventions);

            Assert.IsTrue(plan.Ok, string.Join("\n", plan.Problems));

            var script = plan.Scripts.Single();

            Assert.AreEqual("Assets/Game/ArenaPawn.cs", script.Path);

            Assert.AreEqual("Game.Gameplay.ArenaPawn", script.FullName);

            StringAssert.Contains("namespace Game.Gameplay\n{", script.Source);

            StringAssert.Contains("public class ArenaPawn : Pawn", script.Source);

            CollectionAssert.AreEqual(new[] { "Game.Gameplay.ArenaPawn" }, script.DeclaredClasses);

            Assert.AreEqual("Writes ArenaPawn.cs in Assets/Game", plan.Describe());
        }

        [Test]
        public void Plan_UsesTheScriptsFolderByDefault()
        {
            var plan = ScriptWriter.Plan(new[] { new ScriptRequest("SitizensPlayerController", typeof(PlayerController)) }, Context_Internal(), Conventions);

            Assert.AreEqual("Assets/Scripts/SitizensPlayerController.cs", plan.Scripts.Single().Path);

            Assert.AreEqual(CreateFrameworkScripts.Source(CreateFrameworkScripts.Kind.PlayerController, "SitizensPlayerController", string.Empty), plan.Scripts.Single().Source,
                            "The same template as Assets > Create > UE Architecture.");
        }

        [Test]
        public void Plan_GameModeScriptsDeclareTheirStates()
        {
            var plan = ScriptWriter.Plan(new[] { new ScriptRequest("Arena", typeof(GameMode), "Assets/Game") }, Context_Internal(), Conventions);

            var script = plan.Scripts.Single();

            Assert.AreEqual("Assets/Game/ArenaGameMode.cs", script.Path);

            CollectionAssert.AreEqual(new[] { "ArenaGameMode", "ArenaGameState", "ArenaPlayerState" }, script.DeclaredClasses);
        }

        [Test]
        public void Plan_ReportsClassesThatAlreadyExist()
        {
            var existing = new Dictionary<string, Type> { { "Game.ArenaGameState", typeof(TestGameState) } };

            var plan = ScriptWriter.Plan(new[] { new ScriptRequest("Arena", typeof(GameMode), "Assets/Game") }, Context_Internal(rootNamespace: "Game", types: existing), Conventions);

            Assert.IsFalse(plan.Ok);

            StringAssert.Contains("Game.ArenaGameState already exists", plan.Problems.Single());
        }

        [Test]
        public void Plan_ReportsFilesThatAlreadyExist_InsteadOfRenaming()
        {
            var files = new HashSet<string> { "Assets/Game/ArenaPawn.cs" };

            var plan = ScriptWriter.Plan(new[] { new ScriptRequest("ArenaPawn", typeof(Pawn), "Assets/Game") }, Context_Internal(files: files), Conventions);

            Assert.IsFalse(plan.Ok);

            StringAssert.Contains("Assets/Game/ArenaPawn.cs already exists", plan.Problems.Single());

            Assert.AreEqual("Assets/Game/ArenaPawn.cs", plan.Scripts.Single().Path, "Never 'ArenaPawn 1.cs'.");
        }

        [Test]
        public void Plan_ReportsTheSameScriptTwice()
        {
            var plan = ScriptWriter.Plan(new[] { new ScriptRequest("ArenaPawn", typeof(Pawn), "Assets/Game"), new ScriptRequest("arena pawn", typeof(Pawn), "Assets/Game") },
                                         Context_Internal(), Conventions);

            Assert.IsFalse(plan.Ok);

            StringAssert.Contains("twice", plan.Problems.Single());
        }

        [Test]
        public void Plan_ChecksTheFolderAssemblyReferencesTheFramework()
        {
            var assembly = new ScriptAssembly("Game.Gameplay", "Assets/Game/Game.Gameplay.asmdef", new[] { "UnityEngine.UI" }, true);

            var plan = ScriptWriter.Plan(new[] { new ScriptRequest("ArenaPawn", typeof(Pawn), "Assets/Game") }, Context_Internal(assembly: assembly), Conventions);

            Assert.IsFalse(plan.Ok);

            var problem = plan.Problems.Single();

            StringAssert.Contains("UEArchitecture.Runtime", problem);

            StringAssert.Contains("Assets/Game/Game.Gameplay.asmdef", problem);
        }

        [Test]
        public void Plan_AChildOfAUserClass_NeedsItsAssembly()
        {
            var userParent = typeof(ScriptWriterProbeParent);

            var parentAssembly = userParent.Assembly.GetName().Name;

            var withoutParent = new ScriptAssembly("Game", "Assets/Game/Game.asmdef", new[] { ScriptWriter.RuntimeAssembly }, true);

            var plan = ScriptWriter.Plan(new[] { new ScriptRequest("BossHUD", typeof(HUD), "Assets/Game") }, Context_Internal(assembly: withoutParent), Conventions);

            Assert.IsTrue(plan.Ok, "A framework parent only needs the runtime: " + string.Join("\n", plan.Problems));

            plan = ScriptWriter.Plan(new[] { new ScriptRequest("BossDancer", userParent, "Assets/Game") }, Context_Internal(rootNamespace: "Game", assembly: withoutParent), Conventions);

            Assert.IsFalse(plan.Ok);

            StringAssert.Contains(parentAssembly, plan.Problems.Single());

            var source = plan.Scripts.Single().Source;

            StringAssert.Contains("using " + userParent.Namespace + ";", source);

            StringAssert.Contains("public class BossDancer : ScriptWriterProbeParent", source);

            var withParent = new ScriptAssembly("Game", "Assets/Game/Game.asmdef", new[] { ScriptWriter.RuntimeAssembly, parentAssembly }, true);

            plan = ScriptWriter.Plan(new[] { new ScriptRequest("BossDancer", userParent, "Assets/Game") }, Context_Internal(assembly: withParent), Conventions);

            Assert.IsTrue(plan.Ok, string.Join("\n", plan.Problems));
        }

        [Test]
        public void Plan_RefusesSealedParentsAndFoldersOutsideAssets()
        {
            var sealedPlan = ScriptWriter.Plan(new[] { new ScriptRequest("Boss", typeof(TestPawn), "Assets/Game") }, Context_Internal(), Conventions);

            StringAssert.Contains("sealed", sealedPlan.Problems.Single());

            var packagePlan = ScriptWriter.Plan(new[] { new ScriptRequest("Boss", typeof(Pawn), "Packages/com.example/Runtime") }, Context_Internal(), Conventions);

            StringAssert.Contains("Assets/", packagePlan.Problems.Single());
        }

        [Test]
        public void Plan_InTheRealProject_WritesNothing()
        {
            var plan = ScriptWriter.Plan(new[] { new ScriptRequest("UEATempNeverWritten", typeof(Pawn), "Assets/__UEATempScripts") });

            Assert.IsTrue(plan.Ok, string.Join("\n", plan.Problems));

            Assert.IsFalse(System.IO.Directory.Exists("Assets/__UEATempScripts"));

            var clash = ScriptWriter.Plan(new[] { new ScriptRequest("GameMode", typeof(Pawn), "Assets/__UEATempScripts") });

            StringAssert.Contains("is the name of a UE Architecture class", clash.Problems.Single(), "A class called GameMode would hide the framework's in the other scripts.");
        }

        [Test]
        public void Plan_InTheRealProject_EveryTemplateCompiles()
        {
            foreach (CreateFrameworkScripts.Kind kind in Enum.GetValues(typeof(CreateFrameworkScripts.Kind)))
            {
                var request = new ScriptRequest("UEATempNeverWritten" + kind, ClassTemplates.ParentOf(kind), "Assets/__UEATempScripts") { Kind = kind };

                var plan = ScriptWriter.Plan(new[] { request });

                Assert.IsTrue(plan.Ok, $"{kind}: nothing in this project hides the classes its template uses: " + string.Join("\n", plan.Problems));
            }

            Assert.IsFalse(System.IO.Directory.Exists("Assets/__UEATempScripts"));
        }

        [Test]
        public void Plan_RefusesEveryFrameworkName_WhateverTheParent()
        {
            var assembly = new ScriptAssembly("Game", "Assets/Game/Game.asmdef", new[] { ScriptWriter.RuntimeAssembly, typeof(ScriptWriterProbePawn).Assembly.GetName().Name }, true);

            foreach (var name in new[] { "Pawn", "HUD", "World", "GameMode" })
            {
                // A user class as parent: no template, so no name of a template either.
                var plan = ScriptWriter.Plan(new[] { new ScriptRequest(name, typeof(ScriptWriterProbePawn), "Assets/Game") }, Context_Internal(assembly: assembly), Conventions);

                Assert.IsFalse(plan.Ok, $"'public class {name} : ScriptWriterProbePawn' would hide UE Architecture's {name} in the parent's own script.");

                StringAssert.Contains($"\"{name}\" is the name of a UE Architecture class", plan.Problems.Single());

                StringAssert.Contains($"such as My{name}", plan.Problems.Single());
            }

            Assert.IsTrue(ScriptWriter.Plan(new[] { new ScriptRequest("BossPawn", typeof(ScriptWriterProbePawn), "Assets/Game") }, Context_Internal(assembly: assembly), Conventions).Ok);

            Assert.IsTrue(ClassTemplates.IsFrameworkName("Pawn"));

            Assert.IsTrue(ClassTemplates.IsFrameworkName("GameMode"), "GameMode and GameMode<TGameState, TPlayerState>.");

            Assert.IsFalse(ClassTemplates.IsFrameworkName("ArenaPawn"));
        }

        [Test]
        public void Plan_RefusesATemplateWhoseFrameworkClassesAProjectClassHides()
        {
            // A project that already had a PlayerController MonoBehaviour in the global namespace.
            var global = new Dictionary<string, Type> { { "PlayerController", typeof(ScriptWriterProbeParent) } };

            var gameMode = ScriptWriter.Plan(new[] { new ScriptRequest("Arena", typeof(GameMode), "Assets/Game") }, Context_Internal(types: global), Conventions);

            Assert.IsFalse(gameMode.Ok, "OnPostLogin(PlayerController player) would mean the project's class.");

            var problem = gameMode.Problems.Single();

            StringAssert.Contains(typeof(ScriptWriterProbeParent).FullName, problem);

            StringAssert.Contains("UE Architecture's PlayerController in ArenaGameMode.cs", problem);

            StringAssert.Contains("would not compile", problem);

            Assert.IsTrue(ScriptWriter.Plan(new[] { new ScriptRequest("Arena", typeof(Pawn), "Assets/Game") }, Context_Internal(types: global), Conventions).Ok,
                          "The Pawn template does not use PlayerController.");

            var inNamespace = new Dictionary<string, Type> { { "Game.PlayerController", typeof(ScriptWriterProbeParent) } };

            var nested = ScriptWriter.Plan(new[] { new ScriptRequest("ArenaPlayerController", typeof(PlayerController), "Assets/Game") },
                                           Context_Internal(rootNamespace: "Game.Gameplay", types: inNamespace), Conventions);

            StringAssert.Contains("namespace Game", nested.Problems.Single(), "A namespace around the script's wins over the using too.");

            var plainGameMode = new Dictionary<string, Type> { { "GameMode", typeof(ScriptWriterProbeParent) } };

            Assert.IsTrue(ScriptWriter.Plan(new[] { new ScriptRequest("Arena", typeof(GameMode), "Assets/Game") }, Context_Internal(types: plainGameMode), Conventions).Ok,
                          "The template derives from GameMode<A, B>, which a class GameMode without type parameters does not hide.");

            CollectionAssert.Contains(ClassTemplates.ReferencedFrameworkTypeNames(CreateFrameworkScripts.Kind.GameMode), "GameMode`2");

            CollectionAssert.Contains(ClassTemplates.ReferencedFrameworkTypeNames(CreateFrameworkScripts.Kind.TickableWorldSubsystem), "TickGroup", "[Tick(TickGroup.Update)].");

            var hiddenParent = new Dictionary<string, Type> { { nameof(ScriptWriterProbeParent), typeof(TestPawn) } };

            var withParent = new ScriptAssembly("Game", "Assets/Game/Game.asmdef", new[] { ScriptWriter.RuntimeAssembly, typeof(ScriptWriterProbeParent).Assembly.GetName().Name }, true);

            var dancer = ScriptWriter.Plan(new[] { new ScriptRequest("BossDancer", typeof(ScriptWriterProbeParent), "Assets/Game") }, Context_Internal(types: hiddenParent, assembly: withParent),
                                           Conventions);

            StringAssert.Contains("would not compile", dancer.Problems.Single(), "A class of yours can hide the parent too.");
        }

        [Test]
        public void ForParent_NamesAParentCalledLikeAFrameworkClassInFull()
        {
            var source = ClassTemplates.ForParent(typeof(Probes.World), "BossWorld", "Game");

            StringAssert.Contains("public class BossWorld : global::" + typeof(Probes.World).FullName, source, "\"World\" alone would be ambiguous with UE Architecture's.");
        }

        [Test]
        public void Plan_TheCreateMenuMayWriteIntoAnEmbeddedPackage()
        {
            const string folder = "Packages/com.studio.game/Runtime";

            var request = CreateFrameworkScripts.Request_Internal(CreateFrameworkScripts.Kind.Pawn, folder + "/BossPawn.cs");

            Assert.IsTrue(request.AllowPackageFolder, "The Project window's folder is the user's pick.");

            Assert.AreEqual(folder, request.Folder);

            var embedded = ScriptWriter.Plan(new[] { request }, Context_Internal(embedded: folder), Conventions);

            Assert.IsTrue(embedded.Ok, string.Join("\n", embedded.Problems));

            Assert.AreEqual(folder + "/BossPawn.cs", embedded.Scripts.Single().Path);

            var notEmbedded = ScriptWriter.Plan(new[] { CreateFrameworkScripts.Request_Internal(CreateFrameworkScripts.Kind.Pawn, "Packages/com.example/Runtime/BossPawn.cs") },
                                                Context_Internal(embedded: folder), Conventions);

            StringAssert.Contains("read-only", notEmbedded.Problems.Single(), "Packages from the registry, Git or another folder stay read-only.");

            var fromThePicker = ScriptWriter.Plan(new[] { new ScriptRequest("BossPawn", typeof(Pawn), folder) }, Context_Internal(embedded: folder), Conventions);

            StringAssert.Contains("Assets/", fromThePicker.Problems.Single(), "Defaults, the '+' picker and the command line write under Assets/ only.");

            Assert.IsFalse(ScriptWriter.IsEmbeddedPackageFolder("Packages/com.modernwestern.ue-architecture/Editor"), "This package is not embedded in the test project.");

            Assert.IsFalse(ScriptWriter.IsEmbeddedPackageFolder("Assets/Game"));
        }

        [Test]
        public void Write_WritesNothingWhenThePlanHasProblems()
        {
            var plan = ScriptWriter.Plan(new[] { new ScriptRequest("Boss", typeof(Pawn), "Packages/com.example/Runtime") }, Context_Internal(), Conventions);

            var result = ScriptWriter.Write(plan);

            Assert.IsFalse(result.Ok);

            CollectionAssert.IsEmpty(result.Created);
        }

        [Test]
        public void ForParent_UsesTheFrameworkTemplateForFrameworkClasses()
        {
            var source = ClassTemplates.ForParent(typeof(AIController), "GuardAI", null);

            StringAssert.Contains("public class GuardAI : AIController", source);

            StringAssert.Contains("OnPossess", source);

            Assert.AreEqual(CreateFrameworkScripts.Kind.HUD, ClassTemplates.KindOf(typeof(HUD)));

            Assert.IsNull(ClassTemplates.KindOf(typeof(TestHUD)));

            Assert.AreEqual(typeof(LoadingScreen), ClassTemplates.ParentOf(CreateFrameworkScripts.Kind.LoadingScreen));
        }

        [Test]
        public void ForParent_NamesAParentWithTheSameNameInFull()
        {
            var source = ClassTemplates.ForParent(typeof(TestHUD), "TestHUD", "Game");

            StringAssert.Contains("public class TestHUD : global::" + typeof(TestHUD).FullName, source);

            Assert.AreEqual(source.Count(c => c == '{'), source.Count(c => c == '}'));
        }

        [Test]
        public void Plan_RefusesANameTheTemplateUses()
        {
            var pawn = ScriptWriter.Plan(new[] { new ScriptRequest("Pawn", typeof(Pawn), "Assets/Game") }, Context_Internal(), Conventions);

            Assert.IsFalse(pawn.Ok, "'public class Pawn : Pawn' would not compile.");

            StringAssert.Contains("would not compile", pawn.Problems.Single());

            Assert.IsFalse(ScriptWriter.Plan(new[] { new ScriptRequest("Controller", typeof(Pawn), "Assets/Game") }, Context_Internal(), Conventions).Ok,
                           "The Pawn template uses Controller too.");

            Assert.IsFalse(ScriptWriter.Plan(new[] { new ScriptRequest("HUD", typeof(HUD), "Assets/Game") }, Context_Internal(), Conventions).Ok);

            Assert.IsTrue(ScriptWriter.Plan(new[] { new ScriptRequest("ArenaHUD", typeof(HUD), "Assets/Game") }, Context_Internal(), Conventions).Ok);

            CollectionAssert.AreEqual(new[] { "Controller", "Pawn" }, ClassTemplates.ReferencedFrameworkTypes(CreateFrameworkScripts.Kind.Pawn), "Comments do not count.");
        }

        [Test]
        public void Plan_PutsTheStatesInTheirOwnFilesWhenAsked()
        {
            var split = new GeneratorConventions(Conventions.ScriptsFolder, Conventions.PrefabRoot, Conventions.GameplayArea, Conventions.UIArea, Conventions.PrefabFolderPattern,
                                                 Conventions.PrefabNamePattern, Conventions.OverrideFolderPattern, true);

            var plan = ScriptWriter.Plan(new[] { new ScriptRequest("Arena", typeof(GameMode), "Assets/Game") }, Context_Internal(rootNamespace: "Game"), split);

            Assert.IsTrue(plan.Ok, string.Join("\n", plan.Problems));

            CollectionAssert.AreEqual(new[] { "Assets/Game/ArenaGameMode.cs", "Assets/Game/ArenaGameState.cs", "Assets/Game/ArenaPlayerState.cs" }, plan.Scripts.Select(script => script.Path));

            CollectionAssert.AreEqual(new[] { "Game.ArenaGameMode" }, plan.Scripts[0].DeclaredClasses);

            StringAssert.Contains("public class ArenaGameMode : GameMode<ArenaGameState, ArenaPlayerState>", plan.Scripts[0].Source);

            StringAssert.DoesNotContain("class ArenaGameState", plan.Scripts[0].Source);

            StringAssert.Contains("public class ArenaGameState : GameState", plan.Scripts[1].Source);

            StringAssert.Contains("public class ArenaPlayerState : PlayerState", plan.Scripts[2].Source);

            Assert.AreEqual("Writes ArenaGameMode.cs + ArenaGameState.cs + ArenaPlayerState.cs in Assets/Game", plan.Describe());

            var oneFile = ScriptWriter.Plan(new[] { new ScriptRequest("Arena", typeof(GameMode), "Assets/Game") { SplitStates = false } }, Context_Internal(), split);

            Assert.AreEqual(1, oneFile.Scripts.Count, "The request can ask for one file.");
        }

        [Test]
        public void Templates_CoverEveryFrameworkKind()
        {
            var tickable = ClassTemplates.Source(CreateFrameworkScripts.Kind.TickableWorldSubsystem, "CameraRig", null, false, new TickOptions(TickGroup.LateUpdate, -10, true));

            StringAssert.Contains("[Tick(TickGroup.LateUpdate, Order = -10, Unscaled = true)]", tickable);

            StringAssert.Contains("public sealed class CameraRig : WorldSubsystem, ITickable", tickable);

            StringAssert.Contains("public void Tick(float deltaTime)", tickable);

            StringAssert.Contains("[Tick(TickGroup.Update)]", CreateFrameworkScripts.Source(CreateFrameworkScripts.Kind.TickableGameInstanceSubsystem, "Clock", null));

            var save = CreateFrameworkScripts.Source(CreateFrameworkScripts.Kind.SaveGame, "Progress", null);

            StringAssert.Contains("[Serializable]", save);

            StringAssert.Contains("public sealed class Progress : SaveGame", save);

            StringAssert.Contains("public override int Version => 1;", save);

            StringAssert.Contains("protected override void OnAfterLoad(int savedVersion)", save);

            StringAssert.Contains("public struct PlayerDied", CreateFrameworkScripts.Source(CreateFrameworkScripts.Kind.Message, "Player Died", null));

            StringAssert.Contains("base.OnEnable();", CreateFrameworkScripts.Source(CreateFrameworkScripts.Kind.PlayerStart, "TeamStart", null));

            Assert.AreEqual(CreateFrameworkScripts.Kind.PlayerStart, ClassTemplates.KindOf(typeof(PlayerStart)));

            Assert.AreEqual(CreateFrameworkScripts.Kind.SaveGame, ClassTemplates.KindOf(typeof(SaveGame)));

            Assert.AreEqual(typeof(WorldSubsystem), ClassTemplates.ParentOf(CreateFrameworkScripts.Kind.TickableWorldSubsystem));

            Assert.IsNull(ClassTemplates.ParentOf(CreateFrameworkScripts.Kind.Message));

            var message = ScriptWriter.Plan(new[] { new ScriptRequest("player died", null, "Assets/Game") { Kind = CreateFrameworkScripts.Kind.Message } }, Context_Internal(),
                                            Conventions);

            Assert.IsTrue(message.Ok, "A Message has no parent class: " + string.Join("\n", message.Problems));

            Assert.AreEqual("Assets/Game/PlayerDied.cs", message.Scripts.Single().Path);

            var tick = ScriptWriter.Plan(new[]
            {
                new ScriptRequest("Camera Rig", typeof(WorldSubsystem), "Assets/Game") { Kind = CreateFrameworkScripts.Kind.TickableWorldSubsystem, Tick = new TickOptions(TickGroup.FixedUpdate) }
            }, Context_Internal(), Conventions);

            StringAssert.Contains("[Tick(TickGroup.FixedUpdate)]", tick.Scripts.Single().Source);
        }

        [Test]
        public void Plan_AChildOfYourGameMode_IsNamedAndDescribedLikeOne()
        {
            var assembly = new ScriptAssembly("Game", "Assets/Game/Game.asmdef", new[] { ScriptWriter.RuntimeAssembly, typeof(ScriptWriterProbeGameMode).Assembly.GetName().Name }, true);

            var plan = ScriptWriter.Plan(new[] { new ScriptRequest("Boss", typeof(ScriptWriterProbeGameMode), "Assets/Game") }, Context_Internal(assembly: assembly), Conventions);

            Assert.IsTrue(plan.Ok, string.Join("\n", plan.Problems));

            var script = plan.Scripts.Single();

            Assert.AreEqual("BossGameMode", script.ClassName, "Named like a GameMode.");

            StringAssert.Contains("public class BossGameMode : ScriptWriterProbeGameMode", script.Source);

            StringAssert.Contains($"({nameof(TestGameState)}, {nameof(TestPlayerState)})", script.Source, "It says which GameState and PlayerState it keeps.");
        }

        [Test]
        public void CreateMenu_ReportsATakenNameInsteadOfWritingX1()
        {
            const string folder = "Assets/__UEATempCreateMenu";

            System.IO.Directory.CreateDirectory(folder);

            try
            {
                // Written without importing it, so nothing compiles.
                System.IO.File.WriteAllText(folder + "/NewPawn.cs", "// taken\n");

                var result = CreateFrameworkScripts.Create_Internal(CreateFrameworkScripts.Kind.Pawn, folder + "/NewPawn.cs");

                Assert.IsFalse(result.Ok);

                StringAssert.Contains("already exists", result.Problems.Single());

                CollectionAssert.AreEqual(new[] { "NewPawn.cs" }, System.IO.Directory.GetFiles(folder).Select(System.IO.Path.GetFileName), "No 'NewPawn 1.cs'.");
            }
            finally
            {
                System.IO.Directory.Delete(folder, true);

                if (System.IO.File.Exists(folder + ".meta"))
                {
                    System.IO.File.Delete(folder + ".meta");
                }
            }
        }

        private static ScriptPlanContext Context_Internal(string rootNamespace = "", Dictionary<string, Type> types = null, HashSet<string> files = null, ScriptAssembly? assembly = null,
                                                         string embedded = null)
        {
            var target = assembly ?? new ScriptAssembly("Assembly-CSharp", string.Empty, new[] { ScriptWriter.RuntimeAssembly, typeof(TestPawn).Assembly.GetName().Name }, true);

            return new ScriptPlanContext(name => types != null && types.TryGetValue(name, out var type) ? type : null,
                                         path => files != null && files.Contains(path),
                                         _ => rootNamespace,
                                         _ => target,
                                         folder => embedded != null && folder == embedded);
        }
    }

    /// <summary>
    /// A Pawn of a project (not sealed), to plan a child of a user class.
    /// </summary>
    public class ScriptWriterProbePawn : Pawn
    {
    }

    /// <summary>
    /// A class that is neither sealed nor a framework class, to plan children of a user class.
    /// </summary>
    public class ScriptWriterProbeParent
    {
    }

    /// <summary>
    /// A GameMode of a project (not sealed), to plan a child of it.
    /// </summary>
    public class ScriptWriterProbeGameMode : GameMode<TestGameState, TestPlayerState>
    {
    }
}

namespace ModernWestern.UEArchitecture.EditorTools.Tests.Probes
{
    /// <summary>
    /// A class of a project named like a UE Architecture class (World), in its own namespace so the tests' code still
    /// means the framework's.
    /// </summary>
    public class World
    {
    }
}
