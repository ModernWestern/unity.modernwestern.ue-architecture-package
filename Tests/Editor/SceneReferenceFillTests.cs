using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using ModernWestern.UEArchitecture.Tests;

namespace ModernWestern.UEArchitecture.EditorTools.Tests
{
    public class SceneReferenceFillTests
    {
        private const string Folder = "Assets/__UEATempSceneReferences";

        private static ReferenceSlot Slot(string field, System.Type type, Object current = null) => new(field, field, type, current);

        private static ReferenceCandidate Candidate(string name, System.Type type) => new(null, type, name, "Level/" + name);

        [Test]
        public void Plan_AssignsOnlyWhatExactlyOneObjectFits()
        {
            var wagon = Candidate("Wagon", typeof(PlayerStart));

            var hud = Candidate("Canvas", typeof(HUD));

            var rows = SceneReferenceFill.Plan(new[] { Slot("start", typeof(PlayerStart)), Slot("hud", typeof(HUD)), Slot("pawn", typeof(Pawn)) }, new[] { wagon, hud });

            Assert.AreEqual(FillOutcome.Assign, rows[0].Outcome);

            Assert.AreSame(wagon, rows[0].Pick, "The only PlayerStart.");

            Assert.AreEqual(FillOutcome.Assign, rows[1].Outcome);

            Assert.AreEqual(FillOutcome.None, rows[2].Outcome, "Nothing is a Pawn.");
        }

        [Test]
        public void Plan_NeverGuessesBetweenSeveralMatches()
        {
            var first = Candidate("Red Start", typeof(PlayerStart));

            var second = Candidate("Blue Start", typeof(PlayerStart));

            var rows = SceneReferenceFill.Plan(new[] { Slot("start", typeof(PlayerStart)) }, new[] { first, second });

            Assert.AreEqual(FillOutcome.Ambiguous, rows[0].Outcome);

            Assert.IsNull(rows[0].Pick);

            Assert.AreEqual(2, rows[0].Matches.Count);

            var named = SceneReferenceFill.Plan(new[] { Slot("blueStart", typeof(PlayerStart)) }, new[] { first, second });

            Assert.AreEqual(FillOutcome.Assign, named[0].Outcome, "The field's name breaks the tie.");

            Assert.AreSame(second, named[0].Pick);

            Assert.AreEqual("same name", named[0].Reason);
        }

        [Test]
        public void Plan_MatchesGameObjectFieldsByUniqueNameOnly()
        {
            var limit = Candidate("Limit Collider", typeof(GameObject));

            var other = Candidate("Floor", typeof(GameObject));

            var rows = SceneReferenceFill.Plan(new[] { Slot("limitCollider", typeof(GameObject)), Slot("ceiling", typeof(GameObject)) }, new[] { limit, other });

            Assert.AreEqual(FillOutcome.Assign, rows[0].Outcome);

            Assert.AreSame(limit, rows[0].Pick);

            Assert.AreEqual(FillOutcome.None, rows[1].Outcome, "Every object is a GameObject: only a name decides.");

            var twins = SceneReferenceFill.Plan(new[] { Slot("limitCollider", typeof(GameObject)) }, new[] { limit, Candidate("LimitCollider", typeof(GameObject)) });

            Assert.AreEqual(FillOutcome.Ambiguous, twins[0].Outcome);
        }

        [Test]
        public void Plan_KeepsSetFieldsAndDoesNotReuseAnObjectByType()
        {
            var holder = new GameObject("UEA plan probe");

            try
            {
                var used = holder.AddComponent<PlayerStart>();

                var only = new ReferenceCandidate(used, typeof(PlayerStart), "Main", "Main");

                var rows = SceneReferenceFill.Plan(new[] { Slot("mainStart", typeof(PlayerStart), used), Slot("otherStart", typeof(PlayerStart)) }, new[] { only });

                Assert.AreEqual(FillOutcome.Kept, rows[0].Outcome, "Set fields are never overwritten.");

                Assert.AreEqual(FillOutcome.Ambiguous, rows[1].Outcome, "The only match is already used by another field.");

                StringAssert.Contains("already in", rows[1].Reason);
            }
            finally
            {
                Object.DestroyImmediate(holder);
            }

            var shared = Candidate("Start", typeof(PlayerStart));

            var both = SceneReferenceFill.Plan(new[] { Slot("first", typeof(PlayerStart)), Slot("second", typeof(PlayerStart)) }, new[] { shared });

            Assert.IsTrue(both.All(row => row.Outcome == FillOutcome.Ambiguous), "One object for two fields is a guess.");
        }

        [Test]
        public void Plan_FillsListElementsOnlyWhenOneObjectIsLeft()
        {
            var a = Candidate("A", typeof(PlayerStart));

            var one = SceneReferenceFill.Plan(new[] { new ReferenceSlot("starts.Array.data[0]", "starts", typeof(PlayerStart), null, "starts") }, new[] { a });

            Assert.AreEqual(FillOutcome.Assign, one[0].Outcome);

            var two = SceneReferenceFill.Plan(new[]
            {
                new ReferenceSlot("starts.Array.data[0]", "starts", typeof(PlayerStart), null, "starts"),
                new ReferenceSlot("starts.Array.data[1]", "starts", typeof(PlayerStart), null, "starts")
            }, new[] { a });

            Assert.IsTrue(two.All(row => row.Outcome == FillOutcome.Ambiguous), "Two empty elements and one object: no guess.");

            Assert.AreEqual("Starts [1]", two[1].Slot.Label);
        }

        [TestCase("m_PlayerCharacter", "playercharacter")]
        [TestCase("Player Character", "playercharacter")]
        [TestCase("player_character", "playercharacter")]
        [TestCase("", "")]
        public void Normalize_IgnoresCaseSpacesAndPrefixes(string name, string expected)
        {
            Assert.AreEqual(expected, SceneReferenceFill.Normalize(name));
        }

        [Test]
        public void FillSceneReferences_FillsALevelInOneUndoStep()
        {
            using (var kit = new EditorTestKit(Folder))
            {
                var scene = kit.CreateLevel("Gameplay");

                var gameMode = Place_Internal(scene, "GameplayGameMode").AddComponent<TestLevelGameMode>();

                var pawn = Place_Internal(scene, "Player").AddComponent<TestPawn>();

                var start = Place_Internal(scene, "Spawn").AddComponent<PlayerStart>();

                var limit = Place_Internal(scene, "LimitCollider");

                var slots = SceneReferenceFill.Slots(new SerializedObject(gameMode));

                CollectionAssert.AreEquivalent(new[] { "playerCharacter", "spawn", "limitCollider", "waypoints.Array.data[0]", "waypoints.Array.data[1]" },
                                               slots.Select(slot => slot.PropertyPath), "The GameMode's own class slots are left out.");

                Undo.ClearAll();

                var result = UEArchitectureActions.FillSceneReferences(gameMode);

                Assert.IsTrue(result.Ok, string.Join("\n", result.Problems));

                Assert.AreEqual(pawn, gameMode.PlayerCharacter);

                Assert.AreEqual(start, gameMode.Spawn);

                Assert.AreEqual(limit, gameMode.LimitCollider, "By its name.");

                Assert.IsTrue(gameMode.Waypoints.All(point => point == null), "Every object has a Transform: list elements are left to pick.");

                StringAssert.Contains("several", result.Message);

                Undo.PerformUndo();

                Assert.IsNull(gameMode.PlayerCharacter, "One Undo reverts the whole fill.");

                Assert.IsNull(gameMode.Spawn);

                Assert.IsNull(gameMode.LimitCollider);

                Assert.AreEqual(3, UEArchitectureActions.FillSceneReferences(gameMode).Changed.Count, "Filled again.");

                var again = UEArchitectureActions.FillSceneReferences(gameMode);

                CollectionAssert.IsEmpty(again.Changed, "A second run changes nothing.");

                Assert.AreEqual(pawn, gameMode.PlayerCharacter);
            }
        }

        [Test]
        public void FillSceneReferences_RefusesPrefabAssets()
        {
            using (var kit = new EditorTestKit(Folder))
            {
                var prefab = kit.Prefab("LevelMode", typeof(TestLevelGameMode));

                var result = UEArchitectureActions.FillSceneReferences(prefab.GetComponent<TestLevelGameMode>());

                Assert.IsFalse(result.Ok);

                StringAssert.Contains("prefab asset", result.Problems[0]);
            }
        }

        [Test]
        public void CopyCliCommand_IsOfferedOnlyWhereTheCommandLineCanFill()
        {
            using (var kit = new EditorTestKit(Folder))
            {
                var scene = kit.CreateLevel("Gameplay");

                var placed = Place_Internal(scene, "GameplayGameMode").AddComponent<TestLevelGameMode>();

                EditorSceneManager.SaveScene(scene);

                var command = SceneReferencesSection.CliCommand(placed, out _);

                Assert.IsNotNull(command, "A GameMode placed in a saved level.");

                var parsed = CliArguments.Parse(CliTests.SplitLikeAShell(command));

                Assert.AreEqual("fill-scene-references", parsed.Verb);

                Assert.AreEqual(scene.path + "#GameplayGameMode", parsed.Get("target"));

                var prefabPath = AssetDatabase.GetAssetPath(kit.Prefab("LevelMode", typeof(TestLevelGameMode)));

                var stage = UnityEditor.SceneManagement.PrefabStageUtility.OpenPrefab(prefabPath);

                try
                {
                    var inPrefabMode = stage.prefabContentsRoot.GetComponent<TestLevelGameMode>();

                    Assert.IsTrue(SceneReferenceFill.LevelOf(inPrefabMode).IsValid(), "The section and \"Fill N from this scene\" work in Prefab Mode.");

                    Assert.IsNull(SceneReferencesSection.CliCommand(inPrefabMode, out var why), "The command line would find the prefab file instead.");

                    StringAssert.Contains("fill this prefab here", why);

                    Assert.IsFalse(UEArchitectureCli.Execute(CliArguments.Parse(new[] { "-uea", "fill-scene-references", "target=" + prefabPath })).Ok,
                                   "Which is why it is not offered.");
                }
                finally
                {
                    UnityEditor.SceneManagement.StageUtility.GoToMainStage();
                }

                var holder = new GameObject("GameMode");

                try
                {
                    // The test's own scene, when it was never saved.
                    if (string.IsNullOrEmpty(holder.scene.path))
                    {
                        Assert.IsNull(SceneReferencesSection.CliCommand(holder.AddComponent<TestLevelGameMode>(), out var unsavedWhy));

                        StringAssert.Contains("Save the level first", unsavedWhy);
                    }
                }
                finally
                {
                    Object.DestroyImmediate(holder);
                }
            }
        }

        private static GameObject Place_Internal(Scene scene, string name)
        {
            var created = new GameObject(name);

            SceneManager.MoveGameObjectToScene(created, scene);

            return created;
        }
    }
}
