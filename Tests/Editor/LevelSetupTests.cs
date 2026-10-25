using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using ModernWestern.UEArchitecture.Tests;
using Object = UnityEngine.Object;

namespace ModernWestern.UEArchitecture.EditorTools.Tests
{
    /// <summary>
    /// New Level, Set Up This Level and Player Start placement: what a new level contains, find-or-add (a second run adds
    /// nothing), one start per player, the ground, one Undo step, and the command line run twice.
    /// </summary>
    public class LevelSetupTests
    {
        private const string Folder = "Assets/__UEATempLevels";

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
        public void ANewLevel_IsPlayableAndInBuildSettings()
        {
            var path = Folder + "/Station 2.unity";

            var result = UEArchitectureActions.CreateLevel(path);

            Assert.IsTrue(result.Ok, string.Join("\n", result.Problems));

            CollectionAssert.Contains(result.Created, path);

            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<SceneAsset>(path));

            Assert.IsFalse(SceneManager.GetSceneByPath(path).isLoaded, "Made next to the open levels and closed again.");

            Assert.IsTrue(EditorBuildSettings.scenes.Any(scene => scene.path == path && scene.enabled), "Added to Build Settings.");

            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);

            try
            {
                Assert.IsNotNull(GameModeInfo.Find<WorldSettings>(scene));

                Assert.AreEqual(1, LevelSetup.Starts_Internal(scene).Count);

                Assert.IsNotNull(GameModeInfo.Find<Camera>(scene));

                Assert.IsNotNull(GameModeInfo.Find<Light>(scene));

                Assert.IsNotNull(GameModeInfo.Find<AudioListener>(scene));

                Assert.IsNotNull(GameModeInfo.Find<Collider>(scene), "A ground plane: the level had no collider.");

                if (UiTypes.EventSystem != null)
                {
                    Assert.IsTrue(LevelSetup.HasAny_Internal(scene, UiTypes.EventSystem), "An EventSystem.");

                    Assert.IsTrue(scene.GetRootGameObjects().Any(root => root.GetComponent("BaseInputModule") != null || root.GetComponents<Component>().Any(component =>
                                                                           component != null && component.GetType().Name.Contains("InputModule"))), "With an input module.");
                }

                var start = LevelSetup.Starts_Internal(scene)[0];

                Assert.AreEqual(0f, start.transform.position.y, 0.01f, "On the ground plane.");
            }
            finally
            {
                EditorTestKit.Close(scene);
            }
        }

        [Test]
        public void SetUpThisLevel_AddsOnlyWhatIsMissing()
        {
            var scene = _kit.CreateLevel("Existing");

            var camera = new GameObject("My Camera", typeof(Camera));

            SceneManager.MoveGameObjectToScene(camera, scene);

            var first = UEArchitectureActions.SetUpLevel(scene);

            Assert.IsTrue(first.Ok, string.Join("\n", first.Problems));

            StringAssert.Contains("Added:", first.Message);

            StringAssert.Contains("already fine: camera", first.Message);

            Assert.AreEqual(1, scene.GetRootGameObjects().Count(root => root.GetComponent<Camera>() != null), "The level's camera is kept, not doubled.");

            Assert.IsNotNull(camera.GetComponent<AudioListener>(), "The AudioListener goes on the level's camera.");

            var second = UEArchitectureActions.SetUpLevel(scene);

            Assert.IsTrue(second.Ok, string.Join("\n", second.Problems));

            CollectionAssert.IsEmpty(second.Created, "A second run adds nothing.");

            CollectionAssert.IsEmpty(second.Changed);

            StringAssert.StartsWith("Existing: Nothing to add", second.Message);
        }

        [Test]
        public void SetUpThisLevel_AddsNoAudioListener_WhenThePlayerBringsOne()
        {
            var pawn = _kit.Prefab("ListeningPawn", typeof(TestPawn), typeof(AudioListener));

            var gameMode = _kit.Prefab("ListeningGameMode", typeof(TestGameMode));

            UEArchitectureActions.AssignClass(gameMode.GetComponent<GameMode>(), "defaultPawn", null, pawn);

            var scene = _kit.CreateLevel("Listening");

            var result = UEArchitectureActions.SetUpLevel(scene, new LevelSetupOptions { GameMode = gameMode });

            Assert.IsTrue(result.Ok, string.Join("\n", result.Problems));

            Assert.IsNull(LevelSetup.FindInScene<AudioListener>(scene), "The Pawn brings one: a second one would make Unity warn about 2 audio listeners.");

            StringAssert.Contains("AudioListener", result.Message, "Said to be fine.");

            var path = Folder + "/Listening New.unity";

            Assert.IsTrue(UEArchitectureActions.CreateLevel(path, gameMode).Ok);

            var created = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);

            try
            {
                Assert.IsNull(LevelSetup.FindInScene<AudioListener>(created), "A new level for that GameMode has none either.");
            }
            finally
            {
                EditorTestKit.Close(created);
            }
        }

        [Test]
        public void SetUpThisLevel_IsOneUndoStep()
        {
            var scene = _kit.CreateLevel("Undoable");

            Undo.ClearAll();

            var result = UEArchitectureActions.SetUpLevel(scene);

            Assert.IsTrue(result.Ok, string.Join("\n", result.Problems));

            Assert.Greater(scene.rootCount, 2);

            Undo.PerformUndo();

            Assert.AreEqual(0, scene.rootCount, "One Undo removes everything it added.");
        }

        [Test]
        public void PlayerStarts_OnePerPlayerAtStart()
        {
            var scene = _kit.CreateLevel("Coop");

            var gameMode = _kit.Prefab("CoopGameMode", typeof(TestGameMode));

            UEArchitectureActions.Assign(gameMode.GetComponent<GameMode>(), "playersAtStart", 3);

            UEArchitectureActions.UseGameModeInLevel(gameMode, scene);

            var result = UEArchitectureActions.AddPlayerStarts(scene);

            Assert.IsTrue(result.Ok, string.Join("\n", result.Problems));

            var starts = LevelSetup.Starts_Internal(scene);

            Assert.AreEqual(3, starts.Count, "As many as the GameMode's players at start.");

            Assert.AreEqual(3, starts.Select(start => start.transform.position).Distinct().Count(), "Side by side, not on top of each other.");

            CollectionAssert.IsEmpty(UEArchitectureActions.AddPlayerStarts(scene).Created, "A second run adds nothing.");

            Assert.AreEqual(1, UEArchitectureActions.AddPlayerStarts(scene, 4).Created.Count(created => created.Contains("#")), "An explicit count adds the missing one.");
        }

        [Test]
        public void PlayerStarts_LandOnTheGroundAndSnap()
        {
            var scene = _kit.CreateLevel("Hills");

            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);

            SceneManager.MoveGameObjectToScene(floor, scene);

            floor.transform.position = new Vector3(0f, 1.5f, 0f);

            floor.transform.localScale = new Vector3(20f, 1f, 20f);

            Physics.SyncTransforms();

            Assert.AreEqual(2f, LevelSetup.Ground(new Vector3(0f, 5f, 0f)).y, 0.01f, "The top of the cube below.");

            Assert.AreEqual(0f, LevelSetup.Ground(new Vector3(50f, 5f, 0f)).y, 0.01f, "Nothing below: y = 0.");

            var start = new GameObject("Start", typeof(PlayerStart)).GetComponent<PlayerStart>();

            SceneManager.MoveGameObjectToScene(start.gameObject, scene);

            start.transform.position = new Vector3(1f, 3.4f, 1f);

            ProblemProviders.Invalidate();

            var floating = ProblemProviders.For(start).FirstOrDefault(problem => problem.FixId == "snap-to-ground");

            Assert.IsNotNull(floating, "Flagged as floating.");

            StringAssert.Contains("1.4 m", floating.Message);

            var snapped = UEArchitectureActions.SnapPlayerStart(start);

            Assert.IsTrue(snapped.Ok, string.Join("\n", snapped.Problems));

            Assert.AreEqual(2f, start.transform.position.y, 0.01f);

            CollectionAssert.IsEmpty(UEArchitectureActions.SnapPlayerStart(start).Changed, "Already there.");
        }

        [Test]
        public void LevelVerbs_Twice_FromTheCommandLine()
        {
            var path = Folder + "/Cli Level.unity";

            Twice_Internal("create-level", "path=" + path);

            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<SceneAsset>(path));

            var scene = _kit.CreateLevel("Bare");

            EditorSceneManager.SaveScene(scene);

            var barePath = scene.path;

            EditorTestKit.Close(scene);

            Twice_Internal("set-up-level", "scene=" + barePath);

            Twice_Internal("add-player-starts", "scene=" + barePath, "count=2");

            var reopened = EditorSceneManager.OpenScene(barePath, OpenSceneMode.Additive);

            try
            {
                Assert.AreEqual(2, LevelSetup.Starts_Internal(reopened).Count, "Saved by the command.");
            }
            finally
            {
                EditorTestKit.Close(reopened);
            }

            Assert.IsFalse(UEArchitectureCli.Execute(Parse_Internal("create-level", "path=Packages/x.unity")).Ok, "Only under Assets/.");
        }

        private static ActionResult Twice_Internal(string verb, params string[] pairs)
        {
            var first = UEArchitectureCli.Execute(Parse_Internal(verb, pairs));

            Assert.IsTrue(first.Ok, $"{verb}: {string.Join("\n", first.Problems)}");

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
