using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using ModernWestern.UEArchitecture.Tests;

namespace ModernWestern.UEArchitecture.EditorTools.Tests
{
    /// <summary>
    /// Ready HUD and loading screen prefabs, the class picker's ready-made choices, "Find children by name", and their
    /// command-line verbs run twice.
    /// </summary>
    public class UiPrefabTests
    {
        private const string Folder = "Assets/__UEATempUiPrefabs";

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
        public void AHudPrefab_IsACanvasReadyAndNeverHasAnEventSystem()
        {
            Assume.That(UiBuilders.Available, "Needs the Unity UI package.");

            var result = UEArchitectureActions.CreateHudPrefab(typeof(TestHUD));

            Assert.IsTrue(result.Ok, string.Join("\n", result.Problems));

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/Prefabs/UI/TestHUD.prefab");

            Assert.IsNotNull(prefab, string.Join("\n", result.Created));

            Assert.IsNotNull(prefab.GetComponent<TestHUD>());

            var canvas = prefab.GetComponent<Canvas>();

            Assert.AreEqual(RenderMode.ScreenSpaceOverlay, canvas.renderMode);

            var scaler = new SerializedObject(prefab.GetComponent("CanvasScaler"));

            Assert.AreEqual(new Vector2(1920f, 1080f), scaler.FindProperty("m_ReferenceResolution").vector2Value);

            Assert.AreEqual(0.5f, scaler.FindProperty("m_MatchWidthOrHeight").floatValue, 0.001f);

            Assert.IsNotNull(prefab.GetComponent("GraphicRaycaster"));

            Assert.IsNull(prefab.GetComponentInChildren(UiTypes.EventSystem, true), "The level holds the EventSystem, never the HUD.");
        }

        [Test]
        public void ALoadingScreenPrefab_FadesAndDrawsOnTop()
        {
            Assume.That(UiBuilders.Available, "Needs the Unity UI package.");

            var result = UEArchitectureActions.CreateLoadingScreenPrefab(null, null, true);

            Assert.IsTrue(result.Ok, string.Join("\n", result.Problems));

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/Prefabs/UI/LoadingScreen.prefab");

            Assert.IsNotNull(prefab, string.Join("\n", result.Created));

            Assert.AreEqual(100, prefab.GetComponent<Canvas>().sortingOrder);

            var group = prefab.GetComponent<CanvasGroup>();

            Assert.AreEqual(0f, group.alpha, "Starts hidden: it fades in.");

            Assert.AreEqual(group, new SerializedObject(prefab.GetComponent<LoadingScreen>()).FindProperty("canvasGroup").objectReferenceValue, "Wired for the fade.");

            Assert.IsNotNull(prefab.transform.Find("Background"));

            var bar = prefab.transform.Find("Progress Bar");

            Assert.IsNotNull(bar);

            Assert.AreEqual(0 /* Simple */ + 3 /* Filled */, new SerializedObject(bar.GetComponent("Image")).FindProperty("m_Type").intValue, "A Filled image.");

            Assert.AreEqual(prefab.GetComponent<LoadingScreen>(), FrameworkIndex.Settings.LoadingScreen, "Used by the project.");

            ProblemProviders.Invalidate();

            CollectionAssert.IsEmpty(ProblemProviders.For(prefab.GetComponent<LoadingScreen>()), "Nothing to fix.");
        }

        [Test]
        public void UiVerbs_Twice_FromTheCommandLine()
        {
            Assume.That(UiBuilders.Available, "Needs the Unity UI package.");

            var hud = Twice_Internal("create-hud-prefab", "class=" + typeof(TestHUD).FullName);

            CollectionAssert.Contains(hud.Created, Folder + "/Prefabs/UI/TestHUD.prefab");

            var loading = Twice_Internal("create-loading-screen", "use=true");

            CollectionAssert.Contains(loading.Created, Folder + "/Prefabs/UI/LoadingScreen.prefab");
        }

        [Test]
        public void ThePicker_OffersReadyMadeChoices()
        {
            var hud = ClassField.Creators(typeof(HUD)).Select(pick => pick.Label).ToArray();

            if (UiBuilders.Available)
            {
                CollectionAssert.Contains(hud, "New HUD prefab (Canvas ready)");

                CollectionAssert.Contains(ClassField.Creators(typeof(LoadingScreen)).Select(pick => pick.Label).ToArray(), "New default loading screen");
            }

            CollectionAssert.Contains(hud, "New class (more options)…");

            CollectionAssert.Contains(ClassField.Creators(typeof(GameMode)).Select(pick => pick.Label).ToArray(), "New Game Mode set…");

            CollectionAssert.DoesNotContain(ClassField.Creators(typeof(PlayerController)).Select(pick => pick.Label).ToArray(), "New Game Mode set…");
        }

        [Test]
        public void ThePickersReadyHud_FillsTheField()
        {
            Assume.That(UiBuilders.Available, "Needs the Unity UI package.");

            var gameMode = _kit.Prefab("ArenaGameMode", typeof(TestGameMode)).GetComponent<GameMode>();

            var result = UEArchitectureActions.CreateUiPrefabAndAssign_Internal(new Object[] { gameMode }, "hud", typeof(HUD));

            Assert.IsTrue(result.Ok, string.Join("\n", result.Problems));

            Assert.IsNotNull(gameMode.HUDPrefab);

            Assert.IsNotNull(gameMode.HUDPrefab.GetComponent<Canvas>());
        }

        [Test]
        public void ThePickersReadyHud_IsNamedAfterItsGameModeAndNeverShared()
        {
            Assume.That(UiBuilders.Available, "Needs the Unity UI package.");

            var arena = _kit.Prefab("ArenaGameMode", typeof(TestGameMode)).GetComponent<GameMode>();

            var siege = _kit.Prefab("SiegeGameMode", typeof(TestGameMode)).GetComponent<GameMode>();

            Assert.IsTrue(UEArchitectureActions.CreateUiPrefabAndAssign_Internal(new Object[] { arena }, "hud", typeof(HUD)).Ok);

            Assert.IsTrue(UEArchitectureActions.CreateUiPrefabAndAssign_Internal(new Object[] { siege }, "hud", typeof(HUD)).Ok);

            Assert.AreEqual(Folder + "/Prefabs/UI/ArenaHUD.prefab", AssetDatabase.GetAssetPath(arena.HUDPrefab), "Named after the Game Mode set.");

            Assert.AreEqual(Folder + "/Prefabs/UI/SiegeHUD.prefab", AssetDatabase.GetAssetPath(siege.HUDPrefab));

            var again = UEArchitectureActions.CreateUiPrefabAndAssign_Internal(new Object[] { arena }, "hud", typeof(HUD));

            Assert.IsTrue(again.Ok, string.Join("\n", again.Problems));

            Assert.AreEqual(1, again.Created.Count(path => path.EndsWith(".prefab")), "\"New\" makes a new prefab: the existing one is not reused silently.");

            Assert.AreNotEqual(Folder + "/Prefabs/UI/ArenaHUD.prefab", AssetDatabase.GetAssetPath(arena.HUDPrefab));

            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/Prefabs/UI/ArenaHUD.prefab"), "The first one stays.");
        }

        [Test]
        public void ThePickersReadyHud_NeedsNoSavedLevel()
        {
            Assume.That(UiBuilders.Available, "Needs the Unity UI package.");

            if (!string.IsNullOrEmpty(SceneManager.GetActiveScene().path))
            {
                Assert.Inconclusive("Needs the test runner's untitled scene.");
            }

            var holder = new GameObject("Unsaved Mode", typeof(TestGameMode));

            try
            {
                var gameMode = holder.GetComponent<GameMode>();

                // Nothing waits for a compile: the field is filled now, so the level need not be saved first.
                var result = UEArchitectureActions.CreateUiPrefabAndAssign_Internal(new Object[] { gameMode }, "hud", typeof(HUD));

                Assert.IsTrue(result.Ok, string.Join("\n", result.Problems));

                Assert.IsNotNull(gameMode.HUDPrefab);
            }
            finally
            {
                Object.DestroyImmediate(holder);
            }
        }

        [Test]
        public void BatchFixes_ChangePrefabsInPlace()
        {
            Assume.That(UiBuilders.Available, "Needs the Unity UI package.");

            var hud = _kit.Prefab("InPlaceHUD", typeof(TestHUD)).GetComponent<HUD>();

            Assert.IsFalse(Scaffold.EditsPrefabsInPlace);

            using (Scaffold.EditPrefabsInPlace())
            {
                Assert.IsTrue(Scaffold.EditsPrefabsInPlace, "Fix all safe and fix-all change prefab files without opening Prefab Mode.");

                var result = UEArchitectureActions.AddHudCanvas(hud);

                Assert.IsTrue(result.Ok, string.Join("\n", result.Problems));
            }

            Assert.IsFalse(Scaffold.EditsPrefabsInPlace);

            Assert.IsNull(UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage(), "The Scene view stays on the level.");

            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/InPlaceHUD.prefab").GetComponent<Canvas>(), "Saved in the prefab file.");
        }

        [Test]
        public void TheDefaultLoadingScreen_BuiltInAPreviewScene_LeavesTheLevelUntouched()
        {
            Assume.That(UiBuilders.Available, "Needs the Unity UI package.");

            var level = _kit.CreateLevel("Behind");

            var active = SceneManager.GetActiveScene();

            var preview = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();

            try
            {
                SceneManager.SetActiveScene(level);

                var root = new GameObject("Loading", typeof(LoadingScreen));

                SceneManager.MoveGameObjectToScene(root, preview);

                Assert.IsTrue(UiBuilders.BuildLoadingScreen(root, new Vector2Int(1920, 1080), true));

                Assert.IsNotNull(root.transform.Find("Progress Bar"));

                Assert.AreEqual(preview, root.transform.Find("Progress Bar").gameObject.scene);

                Assert.IsFalse(level.isDirty, "The children are made where the screen is, not in the open level.");
            }
            finally
            {
                if (active.IsValid() && active.isLoaded)
                {
                    SceneManager.SetActiveScene(active);
                }

                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(preview);

                Undo.ClearAll();
            }
        }

        [Test]
        public void FindChildrenByName_FillsEmptyFieldsOnly()
        {
            var root = new GameObject("Score HUD", typeof(TestScoreHUD));

            try
            {
                new GameObject("Score").transform.SetParent(root.transform);

                new GameObject("Panel Image").transform.SetParent(root.transform);

                new GameObject("Panel").transform.SetParent(root.transform);

                var hud = root.GetComponent<TestScoreHUD>();

                var result = UEArchitectureActions.FindChildrenByName(hud);

                Assert.IsTrue(result.Ok, string.Join("\n", result.Problems));

                Assert.AreEqual("Score", hud.Score.name, "\"score\" takes the child called Score.");

                Assert.AreEqual("Panel", hud.Panel.name, "\"Panel Image\" is another name.");

                var again = UEArchitectureActions.FindChildrenByName(hud);

                CollectionAssert.IsEmpty(again.Changed, "Fields that are set stay.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void FindChildren_Twice_FromTheCommandLine()
        {
            var prefab = _kit.Prefab("ScoreHUD", typeof(TestScoreHUD));

            using (var op = Scaffold.Begin("Children"))
            {
                op.FindOrCreateChild(prefab, "Score");

                op.FindOrCreateChild(prefab, "Panel");
            }

            Twice_Internal("find-children", "target=" + Folder + "/ScoreHUD.prefab");

            var hud = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/ScoreHUD.prefab").GetComponent<TestScoreHUD>();

            Assert.AreEqual("Score", hud.Score.name);

            Assert.AreEqual("Panel", hud.Panel.name);
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
