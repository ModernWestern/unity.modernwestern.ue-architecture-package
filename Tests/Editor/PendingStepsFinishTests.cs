using System;
using System.IO;
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
    /// The real "New script… then fill the field after the compile" step (uea.prefab-for-script), with a class that is
    /// already compiled standing in for the new one: a prefab asset target, a target in a level that is closed while the
    /// step runs, and the command line's way of finishing it.
    /// </summary>
    public class PendingStepsFinishTests
    {
        private const string Folder = "Assets/__UEATempPendingFinish";

        private EditorTestKit _kit;

        private PendingStepStore _store;

        [SetUp]
        public void SetUp()
        {
            _kit = new EditorTestKit(Folder);

            _store = new PendingStepStore(Path.Combine("Temp", "UEATest_PendingFinish_" + Guid.NewGuid().ToString("N") + ".json").Replace('\\', '/'));
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_store.Path))
            {
                File.Delete(_store.Path);
            }

            _kit.Dispose();
        }

        [Test]
        public void ThePrefabForScriptStep_FillsAPrefabAtOnceAndALevelOnceItIsOpen()
        {
            var gameModePrefab = _kit.Prefab("ArenaGameMode", typeof(TestGameMode));

            var scene = _kit.CreateLevel("Boss");

            var holder = new GameObject("GameplayGameMode");

            SceneManager.MoveGameObjectToScene(holder, scene);

            var placed = holder.AddComponent<TestGameMode>();

            Assert.IsTrue(EditorSceneManager.SaveScene(scene));

            var scenePath = scene.path;

            var queued = new ActionResult("test");

            Assert.IsTrue(PendingSteps.TryTarget(gameModePrefab.GetComponent<GameMode>(), "defaultPawn", queued, out var assetTarget), string.Join("\n", queued.Problems));

            Assert.IsTrue(PendingSteps.TryTarget(placed, "defaultPawn", queued, out var levelTarget), string.Join("\n", queued.Problems));

            Assert.IsTrue(EditorCompat.IsSceneObjectKey(levelTarget.objectKey));

            var prefabPath = Folder + "/Prefabs/Gameplay/TestPawn.prefab";

            var step = PendingStep.Create(UEArchitectureActions.PrefabForScriptKind).WithScript(ScriptPath_Internal(), typeof(TestPawn).FullName).WithArg("prefabPath", prefabPath);

            step.CreatedUtc = DateTime.UtcNow.AddHours(-1);

            step.targets.Add(assetTarget);

            step.targets.Add(levelTarget);

            Assert.IsTrue(PendingSteps.Enqueue(step, queued, _store));

            EditorTestKit.Close(scene);

            var environment = new PendingEnvironment(DateTime.UtcNow, false, _ => true, name => name == typeof(TestPawn).FullName ? typeof(TestPawn) : null, _ => typeof(TestPawn), null);

            var first = PendingSteps.Run_Internal(_store, "complete-pending", environment);

            Assert.IsTrue(first.Ok, string.Join("\n", first.Problems));

            Assert.IsTrue(first.Pending, "The level is closed: the step waits for it.");

            StringAssert.Contains("wait", first.Message.ToLowerInvariant());

            StringAssert.DoesNotContain("compile", first.Message, "It waits for a level, not for a compile.");

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

            Assert.IsNotNull(prefab, "The prefab is built at once.");

            Assert.AreEqual(prefab.GetComponent<Pawn>(), gameModePrefab.GetComponent<GameMode>().DefaultPawnPrefab, "The prefab asset target is filled at once.");

            var waiting = _store.Load().Single();

            StringAssert.Contains("Open " + scenePath, waiting.message);

            Assert.IsTrue(PendingSteps.WaitsForScene_Internal(scenePath, _store), "Opening that level runs the step again.");

            CollectionAssert.AreEqual(new[] { scenePath }, PendingSteps.ScenesOfTargets_Internal(_store));

            // What "-uea complete-pending" does: open the levels the steps fill, run them, save and close the levels.
            ActionResult second;

            using (var session = new CliSession())
            {
                session.OpenScenesOfPendingSteps(_store);

                second = PendingSteps.Run_Internal(_store, "complete-pending", environment);
            }

            Assert.IsTrue(second.Ok, string.Join("\n", second.Problems));

            Assert.IsFalse(second.Pending);

            Assert.IsFalse(_store.Exists, "Finished steps are forgotten.");

            Assert.IsFalse(SceneManager.GetSceneByPath(scenePath).isLoaded, "The level it opened is closed again.");

            var reopened = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);

            try
            {
                Assert.AreEqual(prefab.GetComponent<Pawn>(), GameModeInfo.Find<GameMode>(reopened).DefaultPawnPrefab, "The level's GameMode was filled and saved.");
            }
            finally
            {
                EditorTestKit.Close(reopened);
            }
        }

        [Test]
        public void TheSummary_TellsCompileWaitsFromOtherWaits()
        {
            Assert.AreEqual("Finished 1 step(s); 1 waits for a script compile; 2 wait for something else (a level to open, another step: see the notes); 1 blocked (see the problems).",
                            PendingSteps.Summary(1, 1, 2, 1));

            Assert.AreEqual("Finished 0 step(s); 1 waits for something else (a level to open, another step: see the notes).", PendingSteps.Summary(0, 0, 1, 0));
        }

        [Test]
        public void ACommandLineRun_ExitsWithTwoWhenWorkStillWaits()
        {
            Assert.AreEqual(0, UEArchitectureCli.ExitCodeOf_Internal(new ActionResult("x")));

            Assert.AreEqual(1, UEArchitectureCli.ExitCodeOf_Internal(ActionResult.Fail("x", "Broken.")));

            Assert.AreEqual(UEArchitectureCli.PendingExitCode, UEArchitectureCli.ExitCodeOf_Internal(new ActionResult("x") { Pending = true }));

            var problemAndPending = ActionResult.Fail("x", "Broken.");

            problemAndPending.Pending = true;

            Assert.AreEqual(1, UEArchitectureCli.ExitCodeOf_Internal(problemAndPending), "Problems win.");

            Assert.IsFalse(UEArchitectureCli.WaitsToExit_Internal, "Test runs never wait to exit.");

            Assert.AreEqual(DateTime.MaxValue, UEArchitectureCli.Deadline_Internal);
        }

        private static string ScriptPath_Internal()
        {
            return AssetDatabase.FindAssets("TestPawn t:MonoScript").Select(AssetDatabase.GUIDToAssetPath).First(path => path.EndsWith("/TestPawn.cs", StringComparison.Ordinal));
        }
    }
}
