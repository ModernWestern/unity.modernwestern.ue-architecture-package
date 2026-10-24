using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using System.Collections.Generic;
using ModernWestern.UEArchitecture.Tests;
using UnityEngine.SceneManagement;

namespace ModernWestern.UEArchitecture.EditorTools.Tests
{
    public class PendingStepsTests
    {
        private const string RecordKind = "uea.test.record";

        private static readonly List<string> _recorded = new();

        private PendingStepStore _store;

        [PendingStepHandler(RecordKind)]
        private static void Record_Internal(PendingStepContext context)
        {
            var name = context.Step.Arg("name");

            if (name == "wait")
            {
                context.KeepWaiting("Waiting for the test.");

                return;
            }

            if (name == "fail")
            {
                context.Op.Problem("The test step failed.");

                return;
            }

            _recorded.Add(name);
        }

        [SetUp]
        public void SetUp()
        {
            _recorded.Clear();

            _store = new PendingStepStore(Path.Combine("Temp", "UEATest_PendingSteps_" + Guid.NewGuid().ToString("N") + ".json").Replace('\\', '/'));
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_store.Path))
            {
                File.Delete(_store.Path);
            }
        }

        [Test]
        public void Steps_SurviveAJsonRoundTrip()
        {
            var step = PendingStep.Create(UEArchitectureActions.PrefabForScriptKind)
                                  .WithScript("Assets/Scripts/ArenaPawn.cs", "Game.ArenaPawn")
                                  .WithArg("prefabPath", "Assets/Prefabs/Gameplay/ArenaPawn.prefab")
                                  .After("abc");

            step.targets.Add(new PendingTarget { objectKey = "GlobalObjectId_V1-2-0123456789abcdef0123456789abcdef-123-0", propertyPath = "defaultPawn" });

            step.status = PendingStatus.Blocked;

            step.message = "Because.";

            var back = PendingStepStore.FromJson(PendingStepStore.ToJson(new List<PendingStep> { step })).Single();

            Assert.AreEqual(step.id, back.id);

            Assert.AreEqual(step.kind, back.kind);

            CollectionAssert.AreEqual(new[] { "abc" }, back.dependsOn);

            CollectionAssert.AreEqual(step.scriptPaths, back.scriptPaths);

            CollectionAssert.AreEqual(step.expectedClasses, back.expectedClasses);

            Assert.AreEqual("Assets/Prefabs/Gameplay/ArenaPawn.prefab", back.Arg("prefabPath"));

            Assert.AreEqual("defaultPawn", back.targets.Single().propertyPath);

            Assert.AreEqual(step.targets.Single().objectKey, back.targets.Single().objectKey);

            Assert.AreEqual(PendingStatus.Blocked, back.status);

            Assert.AreEqual("Because.", back.message);

            Assert.That(back.CreatedUtc, Is.EqualTo(step.CreatedUtc).Within(TimeSpan.FromSeconds(1)));
        }

        [Test]
        public void TheStore_SavesLoadsAndDeletesItsFileWhenEmpty()
        {
            Assert.IsFalse(_store.Exists);

            CollectionAssert.IsEmpty(_store.Load());

            _store.Save(new List<PendingStep> { PendingStep.Create(RecordKind) });

            Assert.IsTrue(_store.Exists);

            Assert.AreEqual(1, _store.Load().Count);

            Assert.AreEqual(1, _store.Snapshot().Count);

            Assert.AreSame(_store.Snapshot(), _store.Snapshot(), "Read again only when the file changes.");

            _store.Save(new List<PendingStep>());

            Assert.IsFalse(_store.Exists);

            Assert.AreEqual(0, _store.Snapshot().Count);
        }

        [Test]
        public void Steps_ExpireAfterADay()
        {
            var now = new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);

            var fresh = PendingStep.Create(RecordKind);

            fresh.CreatedUtc = now.AddHours(-1);

            var old = PendingStep.Create(RecordKind);

            old.CreatedUtc = now.AddHours(-25);

            var unreadable = PendingStep.Create(RecordKind);

            unreadable.createdUtc = "yesterday";

            var steps = new List<PendingStep> { fresh, old, unreadable };

            var expired = PendingStepStore.Expire(steps, now, PendingStepStore.MaxAge);

            CollectionAssert.AreEquivalent(new[] { old, unreadable }, expired);

            CollectionAssert.AreEqual(new[] { fresh }, steps);
        }

        [Test]
        public void Steps_RunAfterTheStepsTheyDependOn()
        {
            var gameMode = Step_Internal("gameMode", 1);

            var pawn = Step_Internal("pawn", 2);

            var controller = Step_Internal("controller", 3);

            gameMode.After(pawn.id, controller.id);

            pawn.After(controller.id);

            var ordered = PendingStepStore.Order(new[] { gameMode, pawn, controller }, out var loops);

            CollectionAssert.AreEqual(new[] { controller, pawn, gameMode }, ordered);

            CollectionAssert.IsEmpty(loops);

            var a = Step_Internal("a", 1);

            var b = Step_Internal("b", 2);

            a.After(b.id);

            b.After(a.id);

            var free = Step_Internal("free", 3);

            ordered = PendingStepStore.Order(new[] { a, b, free }, out loops);

            Assert.AreEqual(free, ordered[0]);

            CollectionAssert.AreEquivalent(new[] { a, b }, loops);
        }

        [Test]
        public void AMissingClass_BlocksTheStepAfterACleanCompile()
        {
            var step = PendingStep.Create(RecordKind).WithScript("Assets/Scripts/ArenaPawn.cs", "Game.ArenaPawn");

            step.CreatedUtc = DateTime.UtcNow.AddMinutes(-10);

            var environment = Environment_Internal(DateTime.UtcNow.AddMinutes(-1), classOfScript: _ => typeof(TestPawn));

            var evaluation = PendingSteps.Evaluate(step, environment);

            Assert.IsFalse(evaluation.Ready);

            Assert.AreEqual(PendingStatus.Blocked, evaluation.Status);

            StringAssert.Contains("Game.ArenaPawn", evaluation.Message);

            StringAssert.Contains(typeof(TestPawn).FullName, evaluation.Message);
        }

        [Test]
        public void ANewScript_WaitsForItsCompile()
        {
            var step = PendingStep.Create(RecordKind).WithScript("Assets/Scripts/ArenaPawn.cs", "Game.ArenaPawn");

            var environment = Environment_Internal(DateTime.UtcNow.AddMinutes(-1));

            var evaluation = PendingSteps.Evaluate(step, environment);

            Assert.AreEqual(PendingStatus.Waiting, evaluation.Status);

            StringAssert.Contains("ArenaPawn.cs", evaluation.Message);
        }

        [Test]
        public void DeletedScriptsAndCompileErrors_BlockTheStep()
        {
            var step = PendingStep.Create(RecordKind).WithScript("Assets/Scripts/ArenaPawn.cs", "Game.ArenaPawn");

            var deleted = PendingSteps.Evaluate(step, Environment_Internal(DateTime.UtcNow, scriptExists: false));

            Assert.AreEqual(PendingStatus.Blocked, deleted.Status);

            StringAssert.Contains("deleted", deleted.Message);

            var errors = new Dictionary<string, string> { { "Assets/Scripts/ArenaPawn.cs", "line 3: error CS1002: ; expected" } };

            var broken = PendingSteps.Evaluate(step, Environment_Internal(DateTime.UtcNow, errors: errors));

            Assert.AreEqual(PendingStatus.Blocked, broken.Status);

            StringAssert.Contains("CS1002", broken.Message);
        }

        [Test]
        public void ACompiledClass_MakesTheStepReady()
        {
            var scriptPath = AssetDatabase.FindAssets("TestPawn t:MonoScript").Select(AssetDatabase.GUIDToAssetPath).First(path => path.EndsWith("/TestPawn.cs", StringComparison.Ordinal));

            var step = PendingStep.Create(RecordKind).WithScript(scriptPath, typeof(TestPawn).FullName);

            step.CreatedUtc = DateTime.UtcNow.AddHours(-1);

            var evaluation = PendingSteps.Evaluate(step, PendingEnvironment.Current);

            Assert.IsTrue(evaluation.Ready, evaluation.Message);

            Assert.AreEqual(typeof(TestPawn), evaluation.Classes.Single());
        }

        [Test]
        public void Running_FinishesStepsInOrderAndForgetsThem()
        {
            var result = new ActionResult("test");

            var second = PendingStep.Create(RecordKind).WithArg("name", "second");

            var first = PendingStep.Create(RecordKind).WithArg("name", "first");

            second.After(first.id);

            Assert.IsTrue(PendingSteps.Enqueue(second, result, _store));

            Assert.IsTrue(PendingSteps.Enqueue(first, result, _store));

            Assert.IsTrue(result.Pending);

            var run = PendingSteps.Run_Internal(_store, "complete-pending", Environment_Internal(DateTime.UtcNow));

            Assert.IsTrue(run.Ok, string.Join("\n", run.Problems));

            Assert.IsFalse(run.Pending);

            CollectionAssert.AreEqual(new[] { "first", "second" }, _recorded);

            Assert.IsFalse(_store.Exists, "Finished steps are forgotten.");

            run = PendingSteps.Run_Internal(_store, "complete-pending", Environment_Internal(DateTime.UtcNow));

            Assert.AreEqual("Nothing pending.", run.Message);
        }

        [Test]
        public void Running_KeepsWaitingAndBlockedSteps()
        {
            var result = new ActionResult("test");

            var waiting = PendingStep.Create(RecordKind).WithArg("name", "wait");

            var failing = PendingStep.Create(RecordKind).WithArg("name", "fail");

            var unknown = PendingStep.Create("uea.test.nobody-handles-this");

            var dependent = PendingStep.Create(RecordKind).WithArg("name", "after-fail").After(failing.id);

            foreach (var step in new[] { waiting, failing, unknown, dependent })
            {
                PendingSteps.Enqueue(step, result, _store);
            }

            var run = PendingSteps.Run_Internal(_store, "complete-pending", Environment_Internal(DateTime.UtcNow));

            Assert.IsTrue(run.Pending, "The waiting step still waits.");

            Assert.IsFalse(run.Ok);

            CollectionAssert.IsEmpty(_recorded, "The step after a failed one does not run.");

            var stored = _store.Load().ToDictionary(step => step.id);

            Assert.AreEqual(PendingStatus.Waiting, stored[waiting.id].status);

            Assert.AreEqual("Waiting for the test.", stored[waiting.id].message);

            Assert.AreEqual(PendingStatus.Blocked, stored[failing.id].status);

            StringAssert.Contains("failed", stored[failing.id].message);

            Assert.AreEqual(PendingStatus.Blocked, stored[unknown.id].status);

            Assert.AreEqual(PendingStatus.Blocked, stored[dependent.id].status);

            var cancelled = PendingSteps.Cancel(failing.id, _store);

            Assert.IsTrue(cancelled.Ok);

            CollectionAssert.AreEquivalent(new[] { waiting.id, unknown.id }, _store.Load().Select(step => step.id), "Cancelling a step also drops the steps after it.");
        }

        [Test]
        public void Targets_InAnUnsavedScene_AreRefused()
        {
            if (!string.IsNullOrEmpty(SceneManager.GetActiveScene().path))
            {
                Assert.Inconclusive("Needs the test runner's untitled scene.");
            }

            var holder = new GameObject("World Settings");

            try
            {
                var worldSettings = holder.AddComponent<WorldSettings>();

                var result = new ActionResult("test");

                Assert.IsFalse(PendingSteps.TryTarget(worldSettings, "gameModeOverride", result, out _));

                StringAssert.Contains("Save the scene first", result.Problems.Single());

                Assert.IsEmpty(EditorCompat.ObjectKey(worldSettings));
            }
            finally
            {
                Object.DestroyImmediate(holder);
            }
        }

        [Test]
        public void Targets_OnAssets_AreKeptByKey()
        {
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.FindAssets("TestPawn t:MonoScript").Select(AssetDatabase.GUIDToAssetPath).First(path => path.EndsWith("/TestPawn.cs", StringComparison.Ordinal)));

            var result = new ActionResult("test");

            Assert.IsTrue(PendingSteps.TryTarget(script, "anything", result, out var target), string.Join("\n", result.Problems));

            Assert.AreEqual(script, EditorCompat.FromObjectKey(target.objectKey));

            Assert.IsFalse(EditorCompat.IsSceneObjectKey(target.objectKey));
        }

        private static PendingStep Step_Internal(string name, int sequence)
        {
            var step = PendingStep.Create(RecordKind).WithArg("name", name);

            step.sequence = sequence;

            return step;
        }

        private static PendingEnvironment Environment_Internal(DateTime domainLoadedUtc, bool scriptExists = true, Func<string, Type> classOfScript = null,
                                                              Dictionary<string, string> errors = null)
        {
            return new PendingEnvironment(domainLoadedUtc, false, _ => scriptExists, _ => null, classOfScript ?? (_ => null), errors);
        }
    }
}
