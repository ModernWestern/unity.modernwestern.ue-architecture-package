using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using System.Collections;
using UnityEngine.TestTools;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using ModernWestern.UEArchitecture.Tests;

namespace ModernWestern.UEArchitecture.EditorTools.Tests
{
    /// <summary>
    /// The problems registry behind the Level overlay, the inspectors and "-uea validate": each built-in problem, its fix,
    /// the cache, the Play-start message, and validate / fix-all run twice.
    /// </summary>
    public class ProblemsTests
    {
        private const string Folder = "Assets/__UEATempProblems";

        private EditorTestKit _kit;

        private EditorBuildSettingsScene[] _buildScenes;

        [SetUp]
        public void SetUp()
        {
            _kit = new EditorTestKit(Folder);

            _buildScenes = EditorBuildSettings.scenes;
        }

        [TearDown]
        public void TearDown()
        {
            EditorBuildSettings.scenes = _buildScenes;

            _kit.Dispose();
        }

        [Test]
        public void ALevelWithoutGameMode_SaysSoAndOffersAWayOut()
        {
            var scene = _kit.CreateLevel("Animations");

            ProblemProviders.Invalidate();

            var report = ProblemProviders.ForLevel(scene);

            Assert.IsNull(report.GameMode);

            var problem = report.Problems.Single(item => item.Message.Contains("has no GameMode"));

            StringAssert.Contains("Animations.unity", problem.Message);

            Assert.IsTrue(problem.HasFix, "Create a GameMode… or Use <the only one> here.");

            Assert.IsFalse(problem.SafeForFixAll, "Choosing a GameMode is never automatic.");

            StringAssert.Contains("Animations.unity has no GameMode: players get no input", PlayStartCheck.Message(scene, null));

            var gameMode = _kit.Prefab("ArenaGameMode", typeof(TestGameMode));

            UEArchitectureActions.UseGameModeInLevel(gameMode, scene);

            Assert.IsNull(PlayStartCheck.Message(scene, null), "With a GameMode: no message.");

            Assert.IsFalse(ProblemProviders.ForLevel(scene).Problems.Any(item => item.Message.Contains("has no GameMode")), "The cache was cleared by the change.");
        }

        [Test]
        public void MissingPlayerStartsEventSystemAndListener_AreFixedInPlace()
        {
            var scene = _kit.CreateLevel("Arena");

            var hud = _kit.Prefab("ArenaHUD", typeof(TestHUD));

            var gameModePrefab = _kit.Prefab("ArenaGameMode", typeof(TestGameMode));

            var gameMode = gameModePrefab.GetComponent<GameMode>();

            UEArchitectureActions.AssignClass(gameMode, "defaultPawn", typeof(TestPawn));

            UEArchitectureActions.AssignClass(gameMode, "hud", null, hud);

            UEArchitectureActions.UseGameModeInLevel(gameModePrefab, scene);

            var camera = new GameObject("Camera", typeof(Camera));

            SceneManager.MoveGameObjectToScene(camera, scene);

            ProblemProviders.Invalidate();

            var problems = ProblemProviders.ForLevel(scene).Problems;

            var start = problems.Single(item => item.FixId == "add-player-starts");

            StringAssert.Contains("No Player Start", start.Message);

            Assert.IsNotNull(problems.SingleOrDefault(item => item.FixId == "add-audio-listener"), "A camera and no AudioListener.");

            Assert.IsNotNull(problems.SingleOrDefault(item => item.FixId == "add-to-build" && item.Severity == ProblemSeverity.Info), "Not in Build Settings.");

            if (UiBuilders.Available)
            {
                var canvas = problems.Single(item => item.FixId == "add-hud-canvas");

                Assert.AreEqual(ProblemSeverity.Warning, canvas.Severity, "A HUD without a Canvas may still draw in another way: a warning, not an error.");
            }

            Assert.AreEqual(start, ProblemProviders.ForLevel(scene).Next, "The most important fix is offered first.");

            var fixedResult = start.Fix();

            Assert.IsTrue(fixedResult.Ok, string.Join("\n", fixedResult.Problems));

            Assert.AreEqual(1, LevelSetup.Starts_Internal(scene).Count);

            ProblemProviders.Invalidate();

            Assert.IsFalse(ProblemProviders.ForLevel(scene).Problems.Any(item => item.FixId == "add-player-starts"));
        }

        [Test]
        public void HudAndLoadingScreenProblems_HaveTheirFixes()
        {
            Assume.That(UiBuilders.Available, "Needs the Unity UI package.");

            var hud = _kit.Prefab("BareHUD", typeof(TestHUD)).GetComponent<HUD>();

            var hudProblem = ProblemProviders.For(hud).Single();

            StringAssert.Contains("has no Canvas", hudProblem.Message);

            Assert.AreEqual(ProblemSeverity.Warning, hudProblem.Severity, "Validate does not fail for a HUD that draws in another way.");

            var hudFix = hudProblem.Fix();

            Assert.IsTrue(hudFix.Ok, string.Join("\n", hudFix.Problems));

            hud = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/BareHUD.prefab").GetComponent<HUD>();

            Assert.IsNotNull(hud.GetComponent<Canvas>(), "The prefab was changed (headless: directly).");

            ProblemProviders.Invalidate();

            CollectionAssert.IsEmpty(ProblemProviders.For(hud));

            var screenPrefab = _kit.Prefab("Loading", typeof(LoadingScreen), typeof(Canvas));

            var screen = screenPrefab.GetComponent<LoadingScreen>();

            ProblemProviders.Invalidate();

            var problems = ProblemProviders.For(screen);

            var group = problems.Single(item => item.FixId == "use-canvas-group");

            StringAssert.Contains("pop on and off", group.Message);

            Assert.AreEqual("Add a Canvas Group", group.FixLabel);

            var order = problems.Single(item => item.FixId == "loading-screen-order");

            StringAssert.Contains("Sorting order 0", order.Message);

            Assert.IsTrue(group.Fix().Ok);

            Assert.IsTrue(order.Fix().Ok);

            screen = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/Loading.prefab").GetComponent<LoadingScreen>();

            Assert.IsNotNull(new SerializedObject(screen).FindProperty("canvasGroup").objectReferenceValue);

            Assert.AreEqual(100, screen.GetComponent<Canvas>().sortingOrder);

            ProblemProviders.Invalidate();

            CollectionAssert.IsEmpty(ProblemProviders.For(screen));
        }

        [Test]
        public void PlayerStartsWithTheSameTag_AreFlagged()
        {
            var scene = _kit.CreateLevel("Teams");

            foreach (var name in new[] { "Red 1", "Red 2" })
            {
                var start = new GameObject(name, typeof(PlayerStart));

                SceneManager.MoveGameObjectToScene(start, scene);

                UEArchitectureActions.Assign(start.GetComponent<PlayerStart>(), "startTag", "Red");
            }

            ProblemProviders.Invalidate();

            var flagged = LevelSetup.Starts_Internal(scene).Count(start => ProblemProviders.For(start).Any(problem => problem.Message.Contains("tagged \"Red\"")));

            Assert.AreEqual(2, flagged);
        }

        [Test]
        public void AutoPossessWithoutController_IsANote()
        {
            var pawn = _kit.Prefab("Guard", typeof(TestPawn)).GetComponent<Pawn>();

            UEArchitectureActions.Assign(pawn, "autoPossessAI", "PlacedInWorld");

            ProblemProviders.Invalidate();

            var note = ProblemProviders.For(pawn).Single();

            Assert.AreEqual(ProblemSeverity.Info, note.Severity);

            StringAssert.Contains("plain AIController", note.Message);
        }

        [Test]
        public void ValidateAndFixAll_Twice_FromTheCommandLine()
        {
            var scene = _kit.CreateLevel("Checked");

            var gameModePrefab = _kit.Prefab("CheckedGameMode", typeof(TestGameMode));

            UEArchitectureActions.AssignClass(gameModePrefab.GetComponent<GameMode>(), "defaultPawn", typeof(TestPawn));

            UEArchitectureActions.UseGameModeInLevel(gameModePrefab, scene);

            EditorSceneManager.SaveScene(scene);

            var path = scene.path;

            EditorTestKit.Close(scene);

            var validate = UEArchitectureCli.Execute(Parse_Internal("validate", "scenes=" + path));

            Assert.IsTrue(validate.Notes.Any(note => note.Contains(path) && note.Contains("No Player Start")), string.Join("\n", validate.Notes));

            Assert.IsTrue(validate.Ok, "Warnings are notes...");

            var strict = UEArchitectureCli.Execute(Parse_Internal("validate", "scenes=" + path, "strict=true"));

            Assert.IsFalse(strict.Ok, "...and problems with strict=true.");

            Assert.AreEqual(1, UEArchitectureCli.ExitCodeOf_Internal(strict));

            var again = UEArchitectureCli.Execute(Parse_Internal("validate", "scenes=" + path));

            CollectionAssert.IsEmpty(again.Created);

            CollectionAssert.IsEmpty(again.Changed, "Validate changes nothing.");

            var first = UEArchitectureCli.Execute(Parse_Internal("fix-all", "scenes=" + path));

            Assert.IsTrue(first.Ok, string.Join("\n", first.Problems));

            Assert.IsTrue(first.Created.Any(created => created.Contains("Player Start")), string.Join("\n", first.Created));

            Assert.IsFalse(EditorBuildSettings.scenes.Any(item => item.path == path), "Build Settings is a choice: fix-all never adds levels to it.");

            var second = UEArchitectureCli.Execute(Parse_Internal("fix-all", "scenes=" + path));

            Assert.IsTrue(second.Ok, string.Join("\n", second.Problems));

            CollectionAssert.IsEmpty(second.Created, "The second run creates nothing.");

            CollectionAssert.IsEmpty(second.Changed, "The second run changes nothing.");

            var after = UEArchitectureCli.Execute(Parse_Internal("validate", "scenes=" + path, "strict=true"));

            Assert.IsTrue(after.Ok, string.Join("\n", after.Problems));
        }

        [Test]
        public void NotInBuildSettings_IsANoteNeverFixedForYou()
        {
            var scene = _kit.CreateLevel("Scratch");

            UEArchitectureActions.UseGameModeInLevel(_kit.Prefab("ScratchGameMode", typeof(TestGameMode)), scene);

            ProblemProviders.Invalidate();

            var report = ProblemProviders.ForLevel(scene);

            var build = report.Problems.Single(item => item.FixId == "add-to-build");

            Assert.AreEqual(ProblemSeverity.Info, build.Severity);

            Assert.IsFalse(build.SafeForFixAll, "Fix all safe would put every sample and test level into the build.");

            Assert.IsTrue(report.Problems.All(item => item.Severity == ProblemSeverity.Info), string.Join("\n", report.Problems));

            Assert.IsNull(report.Next, "A note is never the accent next step.");
        }

        [Test]
        public void FixAll_SavesOnlyTheLevelsItChanged()
        {
            var gameModePrefab = _kit.Prefab("SavedGameMode", typeof(TestGameMode));

            UEArchitectureActions.AssignClass(gameModePrefab.GetComponent<GameMode>(), "defaultPawn", typeof(TestPawn));

            var needsStart = _kit.CreateLevel("NeedsStart");

            UEArchitectureActions.UseGameModeInLevel(gameModePrefab, needsStart);

            EditorSceneManager.SaveScene(needsStart);

            var clean = _kit.CreateLevel("Clean");

            var paths = new[] { needsStart.path, clean.path };

            EditorTestKit.Close(needsStart);

            EditorTestKit.Close(clean);

            var old = new System.DateTime(2001, 1, 1, 0, 0, 0, System.DateTimeKind.Utc);

            System.IO.File.SetLastWriteTimeUtc(paths[1], old);

            var result = UEArchitectureActions.FixAll(string.Join(";", paths));

            Assert.IsTrue(result.Ok, string.Join("\n", result.Problems));

            Assert.IsTrue(result.Created.Any(created => created.Contains("Player Start")), string.Join("\n", result.Created));

            Assert.AreEqual(old, System.IO.File.GetLastWriteTimeUtc(paths[1]), "A level with nothing to fix is not saved again.");
        }

        [Test]
        public void FixAllSafe_IsOneUndoStep()
        {
            var scene = _kit.CreateLevel("OneStep");

            var gameModePrefab = _kit.Prefab("OneStepGameMode", typeof(TestGameMode));

            UEArchitectureActions.AssignClass(gameModePrefab.GetComponent<GameMode>(), "defaultPawn", typeof(TestPawn));

            UEArchitectureActions.UseGameModeInLevel(gameModePrefab, scene);

            var camera = new GameObject("Camera", typeof(Camera));

            SceneManager.MoveGameObjectToScene(camera, scene);

            ProblemProviders.Invalidate();

            ProblemsSection.RunSafe(ProblemProviders.ForLevel(scene).Problems.ToList());

            Assert.AreEqual(1, LevelSetup.Starts_Internal(scene).Count);

            Assert.IsNotNull(LevelSetup.FindInScene<AudioListener>(scene));

            Undo.PerformUndo();

            Assert.AreEqual(0, LevelSetup.Starts_Internal(scene).Count, "One Undo takes back every fix of the gesture.");

            Assert.IsNull(LevelSetup.FindInScene<AudioListener>(scene));
        }

        [Test]
        public void TheTagsMenu_OnSeveralStarts_IsOneUndoStep()
        {
            var scene = _kit.CreateLevel("Tagged");

            var starts = new Object[2];

            for (var i = 0; i < starts.Length; i++)
            {
                var start = new GameObject("Start " + i, typeof(PlayerStart));

                SceneManager.MoveGameObjectToScene(start, scene);

                starts[i] = start.GetComponent<PlayerStart>();
            }

            PlayerStartEditor.AssignTag_Internal(starts, "startTag", "Blue");

            Assert.IsTrue(starts.All(start => ((PlayerStart)start).StartTag == "Blue"));

            Undo.PerformUndo();

            Assert.IsTrue(starts.All(start => string.IsNullOrEmpty(((PlayerStart)start).StartTag)), "One Undo takes the tag back from every start.");
        }

        [Test]
        public void TheInspectorAndOverlayLists_AreGatheredOncePerChange()
        {
            var scene = _kit.CreateLevel("Cached");

            var red = new GameObject("Red", typeof(PlayerStart));

            SceneManager.MoveGameObjectToScene(red, scene);

            UEArchitectureActions.Assign(red.GetComponent<PlayerStart>(), "startTag", "Red");

            var blue = new GameObject("Blue", typeof(PlayerStart)).GetComponent<PlayerStart>();

            SceneManager.MoveGameObjectToScene(blue.gameObject, scene);

            ProblemProviders.Invalidate();

            var tags = PlayerStartEditor.OtherTags_Internal(blue);

            CollectionAssert.AreEqual(new[] { "Red" }, tags);

            Assert.AreSame(tags, PlayerStartEditor.OtherTags_Internal(blue), "Not walked again per GUI event.");

            ProblemProviders.Invalidate();

            Assert.AreNotSame(tags, PlayerStartEditor.OtherTags_Internal(blue), "Walked again after a change.");

            var prefab = _kit.Prefab("CachedHUD", typeof(TestHUD));

            var problems = LevelOverlayView.PrefabProblems_Internal(prefab);

            Assert.AreSame(problems, LevelOverlayView.PrefabProblems_Internal(prefab), "The prefab is not walked again per GUI event.");

            ProblemProviders.Invalidate();

            Assert.AreNotSame(problems, LevelOverlayView.PrefabProblems_Internal(prefab));
        }

        [Test]
        public void TheNavMesh_IsNotLookedForAgainOnEveryHierarchyChange()
        {
            BuiltInProblems.HasNavMesh_Internal();

            var checks = BuiltInProblems.NavMeshChecks_Internal;

            ProblemProviders.Invalidate();

            BuiltInProblems.HasNavMesh_Internal();

            ProblemProviders.Invalidate();

            BuiltInProblems.HasNavMesh_Internal();

            Assert.AreEqual(checks, BuiltInProblems.NavMeshChecks_Internal, "A Hierarchy change does not build the whole NavMesh again.");

            BuiltInProblems.ForgetNavMesh_Internal();

            BuiltInProblems.HasNavMesh_Internal();

            Assert.AreEqual(checks + 1, BuiltInProblems.NavMeshChecks_Internal, "Asked again once a level opens or the project changes.");
        }

        [Test]
        public void AHudWithAUiDocument_HasNoCanvasProblem()
        {
            var hud = _kit.Prefab("ToolkitHUD", typeof(TestHUD), typeof(UnityEngine.UIElements.UIDocument)).GetComponent<HUD>();

            ProblemProviders.Invalidate();

            CollectionAssert.IsEmpty(ProblemProviders.For(hud), "UI Toolkit draws the HUD.");
        }

        [UnityTest]
        public IEnumerator EditingTheProjectSettings_ClearsTheCache()
        {
            var first = _kit.Prefab("FirstGameMode", typeof(TestGameMode));

            var second = _kit.Prefab("SecondGameMode", typeof(TestGameMode));

            Assert.IsTrue(UEArchitectureActions.SetProjectDefaultGameMode(first).Ok);

            var scene = _kit.CreateLevel("Defaults");

            // The Hierarchy and Project events of the setup arrive on the next Editor updates: let them pass first.
            for (var i = 0; i < 5; i++)
            {
                yield return null;
            }

            ProblemProviders.Invalidate();

            Assert.AreEqual(first.GetComponent<GameMode>(), ProblemProviders.ForLevel(scene).GameMode);

            var version = ProblemProviders.Version;

            yield return null;

            Assume.That(ProblemProviders.Version, Is.EqualTo(version), "Nothing else clears the cache meanwhile.");

            // As the Project Settings page does: a property field, applied.
            var settings = new SerializedObject(FrameworkIndex.Settings);

            settings.FindProperty("defaultGameMode").objectReferenceValue = second.GetComponent<GameMode>();

            settings.ApplyModifiedProperties();

            yield return null;

            yield return null;

            Assert.AreEqual(second.GetComponent<GameMode>(), ProblemProviders.ForLevel(scene).GameMode, "The overlay shows the new default.");
        }

        [Test]
        public void TheCache_IsClearedOnChange()
        {
            var before = ProblemProviders.Version;

            ProblemProviders.Invalidate();

            Assert.Greater(ProblemProviders.Version, before);
        }

        private static CliArguments Parse_Internal(string verb, params string[] pairs)
        {
            return CliArguments.Parse(new[] { "Unity.exe", "-uea", verb }.Concat(pairs).ToArray());
        }
    }
}
