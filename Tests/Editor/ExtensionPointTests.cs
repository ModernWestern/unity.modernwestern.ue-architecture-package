using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using ModernWestern.UEArchitecture.Tests;

namespace ModernWestern.UEArchitecture.EditorTools.Tests
{
    /// <summary>
    /// The places other editor assemblies (the ObserverVM integration) plug into: command-line verbs, Game Mode Kit HUD
    /// presets, and pending steps queued by another step's handler.
    /// </summary>
    public class ExtensionPointTests
    {
        private const string Folder = "Assets/__UEATempExtensions";

        private const string TestHelp = "uea-test-verb [value=<text>]";

        private const string FollowKind = "uea.test.follow";

        private static PendingStepStore _handlerStore;

        private EditorTestKit _kit;

        [SetUp]
        public void SetUp()
        {
            _kit = new EditorTestKit(Folder);
        }

        [TearDown]
        public void TearDown()
        {
            UEArchitectureCli.UnregisterVerbs_Internal(TestHelp);

            HudPresets.Unregister("uea-test-preset");

            _kit.Dispose();
        }

        [Test]
        public void RegisteredVerbs_RunAndShowInTheHelp()
        {
            UEArchitectureCli.RegisterVerbs_Internal(TestHelp, (arguments, session) => arguments.Verb == "uea-test-verb" ? new ActionResult("uea-test-verb") { Message = arguments.Get("value") } : null);

            UEArchitectureCli.RegisterVerbs_Internal(TestHelp, (arguments, session) => arguments.Verb == "uea-test-verb" ? new ActionResult("uea-test-verb") { Message = "second " + arguments.Get("value") } : null);

            var result = UEArchitectureCli.Execute(CliArguments.Parse(new[] { "Unity.exe", "-uea", "uea-test-verb", "value=hello" }));

            Assert.IsTrue(result.Ok);

            Assert.AreEqual("second hello", result.Message, "Registering the same verbs again replaces them.");

            StringAssert.Contains(TestHelp, UEArchitectureCli.HelpText);

            UEArchitectureCli.UnregisterVerbs_Internal(TestHelp);

            Assert.IsFalse(UEArchitectureCli.Execute(CliArguments.Parse(new[] { "Unity.exe", "-uea", "uea-test-verb" })).Ok, "Unknown once removed.");
        }

        [Test]
        public void AHudSectionThatThrows_IsLoggedOnceAndSkipped_UntilRegisteredAgain()
        {
            var calls = 0;

            Action<HUD, UnityEditor.SerializedObject> throwing = (_, _) =>
            {
                calls++;

                UnityEditor.EditorGUI.indentLevel = 3;

                throw new InvalidOperationException("uea test section");
            };

            var hud = new GameObject("Section HUD").AddComponent<TestHUD>();

            var indent = UnityEditor.EditorGUI.indentLevel;

            // Only this section: the registered ones draw with EditorGUILayout, which needs an inspector.
            var registered = HUDEditor.ExtraSections.ToList();

            HUDEditor.ExtraSections.Clear();

            try
            {
                HUDEditor.RegisterSection(throwing);

                UnityEngine.TestTools.LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("uea test section"));

                HUDEditor.DrawExtraSections_Internal(hud, new UnityEditor.SerializedObject(hud));

                Assert.AreEqual(indent, UnityEditor.EditorGUI.indentLevel, "The indent level is put back.");

                HUDEditor.DrawExtraSections_Internal(hud, new UnityEditor.SerializedObject(hud));

                Assert.AreEqual(1, calls, "Not drawn again after it threw (one log, not one per repaint).");

                HUDEditor.RegisterSection(throwing);

                UnityEngine.TestTools.LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("uea test section"));

                HUDEditor.DrawExtraSections_Internal(hud, new UnityEditor.SerializedObject(hud));

                Assert.AreEqual(2, calls, "Registering it again gives it a new chance.");
            }
            finally
            {
                HUDEditor.UnregisterSection(throwing);

                HUDEditor.ExtraSections.Clear();

                HUDEditor.ExtraSections.AddRange(registered);

                UnityEditor.EditorGUI.indentLevel = indent;

                UnityEngine.Object.DestroyImmediate(hud.gameObject);
            }
        }

        [Test]
        public void TheKit_AppliesARegisteredHudPreset_AndSaysWhenOneIsMissing()
        {
            Type seenGameState = null;

            GameObject seenHud = null;

            HudPresets.Register(new HudPreset("uea-test-preset", "Test preset", null, (_, _) => true, (op, hud, gameState, playerState) =>
            {
                seenHud = hud;

                seenGameState = gameState;

                op.Result.AddNote("test preset ran");
            }));

            var request = new KitRequest
            {
                name = "Kit",
                gameMode = new KitChoice(KitChoiceMode.Existing, typeof(TestGameMode).FullName),
                playerController = new KitChoice(KitChoiceMode.Existing, typeof(TestController).FullName),
                pawn = new KitChoice(KitChoiceMode.None),
                hud = new KitChoice(KitChoiceMode.Existing, typeof(TestHUD).FullName),
                hudCanvas = false,
                hudPreset = "uea-test-preset"
            };

            var result = UEArchitectureActions.CreateGameModeKit(request);

            Assert.IsTrue(result.Ok, string.Join("\n", result.Problems));

            Assert.IsNotNull(seenHud);

            Assert.IsNotNull(seenHud.GetComponent<TestHUD>());

            Assert.AreEqual(typeof(TestGameState), seenGameState, "The GameMode's own GameState class.");

            CollectionAssert.Contains(result.Notes, "test preset ran");

            request.hudPreset = "nobody-registered-this";

            var missing = UEArchitectureActions.CreateGameModeKit(request);

            Assert.IsTrue(missing.Ok);

            Assert.IsTrue(missing.Notes.Any(note => note.Contains("nobody-registered-this")), string.Join("\n", missing.Notes));

            var parsed = UEArchitectureCli.KitRequest_Internal(CliArguments.Parse(new[] { "Unity.exe", "-uea", "new-gamemode-kit", "name=Kit", "hud-preset=framework-widgets" }), new ActionResult());

            Assert.AreEqual("framework-widgets", parsed.hudPreset);
        }

        [Test]
        public void AStepQueuedByAHandler_IsKept()
        {
            _handlerStore = new PendingStepStore(Path.Combine("Temp", "UEATest_Extensions_" + Guid.NewGuid().ToString("N") + ".json").Replace('\\', '/'));

            try
            {
                var steps = _handlerStore.Load();

                steps.Add(PendingStep.Create("uea.test.queue-another"));

                _handlerStore.Save(steps);

                var environment = new PendingEnvironment(DateTime.UtcNow, false, _ => true, _ => null, _ => null, null);

                var result = PendingSteps.Run_Internal(_handlerStore, "complete-pending", environment);

                Assert.IsTrue(result.Ok, string.Join("\n", result.Problems));

                var left = _handlerStore.Load();

                Assert.AreEqual(1, left.Count, "The first step is done; the one it queued waits.");

                Assert.AreEqual(FollowKind, left[0].kind);

                Assert.IsTrue(result.Pending);
            }
            finally
            {
                if (File.Exists(_handlerStore.Path))
                {
                    File.Delete(_handlerStore.Path);
                }

                _handlerStore = null;
            }
        }

        [PendingStepHandler("uea.test.queue-another")]
        private static void QueueAnother_Internal(PendingStepContext context)
        {
            if (_handlerStore == null)
            {
                return;
            }

            PendingSteps.Enqueue(PendingStep.Create(FollowKind).WithScript("Assets/__UEATempExtensions/Later.cs", "Later"), context.Op.Result, _handlerStore);
        }
    }
}
