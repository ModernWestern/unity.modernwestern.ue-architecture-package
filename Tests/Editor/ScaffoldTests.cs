using System.Linq;
using NUnit.Framework;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using ModernWestern.UEArchitecture.Tests;

namespace ModernWestern.UEArchitecture.EditorTools.Tests
{
    public class ScaffoldTests
    {
        private const string Folder = "Assets/__UEATempScaffold";

        private readonly List<GameObject> _sceneObjects = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var sceneObject in _sceneObjects)
            {
                if (sceneObject != null)
                {
                    Object.DestroyImmediate(sceneObject);
                }
            }

            _sceneObjects.Clear();

            AssetDatabase.DeleteAsset(Folder);

            FrameworkIndex.Invalidate();
        }

        [Test]
        public void FindOrCreatePrefab_TwiceMakesOneAsset()
        {
            var path = Folder + "/TestPawn.prefab";

            GameObject first;

            GameObject second;

            using (var op = Scaffold.Begin("Create test pawn"))
            {
                first = op.FindOrCreatePrefab(path, "TestPawn", new[] { typeof(TestPawn) });

                Assert.IsTrue(op.Result.Ok, string.Join("\n", op.Result.Problems));

                CollectionAssert.Contains(op.Result.Created, path);
            }

            using (var op = Scaffold.Begin("Create test pawn again"))
            {
                second = op.FindOrCreatePrefab(path, "TestPawn", new[] { typeof(TestPawn) });

                Assert.IsTrue(op.Result.Ok, string.Join("\n", op.Result.Problems));

                CollectionAssert.DoesNotContain(op.Result.Created, path, "Nothing new the second time.");
            }

            Assert.IsNotNull(first);

            Assert.AreEqual(first, second);

            Assert.IsNotNull(first.GetComponent<TestPawn>());

            Assert.AreEqual(1, AssetDatabase.FindAssets("t:Prefab", new[] { Folder }).Length);
        }

        [Test]
        public void FindOrCreatePrefab_LeavesAPrefabWithAnotherRootClassAlone()
        {
            var path = Folder + "/Thing.prefab";

            using (var op = Scaffold.Begin("Create"))
            {
                op.FindOrCreatePrefab(path, "Thing", new[] { typeof(TestPawn) });
            }

            using (var op = Scaffold.Begin("Create other"))
            {
                Assert.IsNull(op.FindOrCreatePrefab(path, "Thing", new[] { typeof(TestController) }));

                Assert.IsFalse(op.Result.Ok);

                StringAssert.Contains("TestController", op.Result.Problems[0]);
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            Assert.IsNotNull(prefab.GetComponent<TestPawn>());

            Assert.IsNull(prefab.GetComponent<TestController>(), "Untouched.");
        }

        [Test]
        public void FindOrCreatePrefab_OnlyWritesUnderAssets()
        {
            using (var op = Scaffold.Begin("Create"))
            {
                Assert.IsNull(op.FindOrCreatePrefab("Packages/com.modernwestern.ue-architecture/Nope.prefab", "Nope", new[] { typeof(TestPawn) }));

                Assert.IsFalse(op.Result.Ok);

                Assert.IsNull(op.FindOrCreatePrefab(Folder + "/Abstract.prefab", "Abstract", new[] { typeof(Controller) }), "Abstract classes cannot be added.");
            }

            Assert.IsFalse(AssetDatabase.IsValidFolder(Folder), "Nothing was created.");
        }

        [Test]
        public void CreateVariant_KeepsTheLinkToItsBase()
        {
            GameObject basePrefab;

            GameObject variant;

            GameObject again;

            using (var op = Scaffold.Begin("Variant"))
            {
                basePrefab = op.FindOrCreatePrefab(Folder + "/TestPawn.prefab", "TestPawn", new[] { typeof(TestPawn) });

                variant = op.CreateVariant(basePrefab, Folder + "/Modes/Boss/TestPawn.prefab");

                Assert.IsTrue(op.Result.Ok, string.Join("\n", op.Result.Problems));
            }

            using (var op = Scaffold.Begin("Variant again"))
            {
                again = op.CreateVariant(basePrefab, Folder + "/Modes/Boss/TestPawn.prefab");

                Assert.AreEqual(0, op.Result.Created.Count);
            }

            Assert.AreEqual(PrefabAssetType.Variant, PrefabUtility.GetPrefabAssetType(variant));

            Assert.AreEqual(basePrefab, PrefabUtility.GetCorrespondingObjectFromSource(variant));

            Assert.IsTrue(ScaffoldOp.IsVariantOf(variant, basePrefab));

            Assert.AreEqual(variant, again);
        }

        [Test]
        public void CreateVariant_RefusesAnotherAssetAtThePath()
        {
            using (var op = Scaffold.Begin("Variant"))
            {
                var basePrefab = op.FindOrCreatePrefab(Folder + "/TestPawn.prefab", "TestPawn", new[] { typeof(TestPawn) });

                op.FindOrCreatePrefab(Folder + "/Other.prefab", "Other", new[] { typeof(TestPawn) });

                Assert.IsNull(op.CreateVariant(basePrefab, Folder + "/Other.prefab"));

                Assert.IsFalse(op.Result.Ok);
            }
        }

        [Test]
        public void Assign_PutsTheComponentNotTheGameObject()
        {
            GameObject gameModePrefab;

            GameObject controllerPrefab;

            using (var op = Scaffold.Begin("Prefabs"))
            {
                controllerPrefab = op.FindOrCreatePrefab(Folder + "/TestController.prefab", "TestController", new[] { typeof(TestController) });

                gameModePrefab = op.FindOrCreatePrefab(Folder + "/TestGameMode.prefab", "TestGameMode", new[] { typeof(TestGameMode) });
            }

            using (var op = Scaffold.Begin("Assign"))
            {
                Assert.IsTrue(op.Assign(gameModePrefab.GetComponent<GameMode>(), "playerController", controllerPrefab));

                CollectionAssert.IsNotEmpty(op.Result.Changed);
            }

            var reloaded = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/TestGameMode.prefab").GetComponent<GameMode>();

            Assert.AreEqual(controllerPrefab.GetComponent<TestController>(), reloaded.PlayerControllerPrefab);

            StringAssert.Contains(AssetDatabase.AssetPathToGUID(Folder + "/TestController.prefab"), System.IO.File.ReadAllText(Folder + "/TestGameMode.prefab"), "The prefab was saved.");

            using (var op = Scaffold.Begin("Assign again"))
            {
                Assert.IsTrue(op.Assign(reloaded, "playerController", controllerPrefab.GetComponent<TestController>()));

                CollectionAssert.IsEmpty(op.Result.Changed, "Already set: nothing changes.");
            }

            using (var op = Scaffold.Begin("Assign a number"))
            {
                Assert.IsTrue(op.Assign(reloaded, "playersAtStart", "3"));
            }

            Assert.AreEqual(3, reloaded.PlayersAtStart);
        }

        [Test]
        public void Assign_WritesToTheCopyOpenInPrefabMode()
        {
            GameObject gameModePrefab;

            GameObject controllerPrefab;

            using (var op = Scaffold.Begin("Prefabs"))
            {
                controllerPrefab = op.FindOrCreatePrefab(Folder + "/TestController.prefab", "TestController", new[] { typeof(TestController) });

                gameModePrefab = op.FindOrCreatePrefab(Folder + "/TestGameMode.prefab", "TestGameMode", new[] { typeof(TestGameMode) });
            }

            var stage = UnityEditor.SceneManagement.PrefabStageUtility.OpenPrefab(Folder + "/TestGameMode.prefab");

            try
            {
                Assert.IsNotNull(stage);

                using (var op = Scaffold.Begin("Assign in Prefab Mode"))
                {
                    Assert.IsTrue(op.Assign(gameModePrefab.GetComponent<GameMode>(), "playerController", controllerPrefab), string.Join("\n", op.Result.Problems));

                    StringAssert.Contains("Prefab Mode", op.Result.Changed.Single());
                }

                Assert.AreEqual(controllerPrefab.GetComponent<PlayerController>(), stage.prefabContentsRoot.GetComponent<GameMode>().PlayerControllerPrefab,
                                "The open copy is edited, so Prefab Mode does not overwrite the change.");

                Assert.AreEqual(gameModePrefab.GetComponent<GameMode>(), Scaffold.Persistent(stage.prefabContentsRoot.GetComponent<GameMode>()), "And back to the asset.");
            }
            finally
            {
                UnityEditor.SceneManagement.StageUtility.GoToMainStage();
            }
        }

        [Test]
        public void Assign_ReportsWrongValues()
        {
            using (var op = Scaffold.Begin("Prefabs"))
            {
                var pawnPrefab = op.FindOrCreatePrefab(Folder + "/TestPawn.prefab", "TestPawn", new[] { typeof(TestPawn) });

                var gameModePrefab = op.FindOrCreatePrefab(Folder + "/TestGameMode.prefab", "TestGameMode", new[] { typeof(TestGameMode) });

                var gameMode = gameModePrefab.GetComponent<GameMode>();

                Assert.IsFalse(op.Assign(gameMode, "playerController", pawnPrefab), "A Pawn prefab has no PlayerController.");

                Assert.IsFalse(op.Assign(gameMode, "noSuchField", pawnPrefab));

                Assert.IsFalse(op.Assign(gameMode, "playersAtStart", "many"));

                Assert.AreEqual(3, op.Result.Problems.Count);

                Assert.IsNull(gameMode.PlayerControllerPrefab);
            }
        }

        [Test]
        public void Assign_RefusesASceneObjectInAnAsset()
        {
            var sceneController = SceneObject_Internal("Scene Controller").AddComponent<TestController>();

            using (var op = Scaffold.Begin("Assign"))
            {
                var gameMode = op.FindOrCreatePrefab(Folder + "/TestGameMode.prefab", "TestGameMode", new[] { typeof(TestGameMode) }).GetComponent<GameMode>();

                Assert.IsFalse(op.Assign(gameMode, "playerController", sceneController));

                StringAssert.Contains("save that object as a prefab", op.Result.Problems[0].ToLowerInvariant());
            }
        }

        [Test]
        public void Assign_IsOneUndoStep()
        {
            var worldSettings = SceneObject_Internal("World Settings").AddComponent<WorldSettings>();

            GameObject gameModePrefab;

            using (var op = Scaffold.Begin("Prefab"))
            {
                gameModePrefab = op.FindOrCreatePrefab(Folder + "/TestGameMode.prefab", "TestGameMode", new[] { typeof(TestGameMode) });
            }

            using (var op = Scaffold.Begin("Use Test Mode in this level"))
            {
                op.Assign(worldSettings, "gameModeOverride", gameModePrefab);

                op.Assign(worldSettings, "gameModeOverride", gameModePrefab);
            }

            Assert.AreEqual(gameModePrefab.GetComponent<GameMode>(), worldSettings.GameModeOverride);

            Assert.IsTrue(worldSettings.gameObject.scene.isDirty);

            Undo.PerformUndo();

            Assert.IsNull(worldSettings.GameModeOverride, "One Undo reverts the gesture.");
        }

        [Test]
        public void FindOrCreateChild_ReusesAndNeverDuplicates()
        {
            var parent = SceneObject_Internal("Level");

            GameObject first;

            using (var op = Scaffold.Begin("Add start"))
            {
                first = op.FindOrCreateChild(parent, "Player Start", typeof(PlayerStart));

                Assert.AreEqual(1, op.Result.Created.Count);
            }

            using (var op = Scaffold.Begin("Add start again"))
            {
                Assert.AreEqual(first, op.FindOrCreateChild(parent, "Player Start", typeof(PlayerStart)));

                Assert.AreEqual(0, op.Result.Created.Count);

                Assert.IsNull(op.FindOrCreateChild(parent, "Player Start", typeof(WorldSettings)), "Same name, other class: reported.");

                Assert.IsFalse(op.Result.Ok);
            }

            Assert.AreEqual(1, parent.transform.childCount);

            Assert.AreEqual(parent.scene, first.scene);

            using (var op = Scaffold.Begin("Add settings"))
            {
                op.FindOrCreateChild(parent, "World Settings", typeof(WorldSettings));
            }

            Assert.AreEqual(2, parent.transform.childCount);

            Undo.PerformUndo();

            Assert.AreEqual(1, parent.transform.childCount, "Undo removes the created child.");
        }

        [Test]
        public void FindOrCreateChild_OnAPrefabAsset_EditsItsSavedFileOnce()
        {
            GameObject prefab;

            using (var op = Scaffold.Begin("Prefab"))
            {
                prefab = op.FindOrCreatePrefab(Folder + "/Holder.prefab", "Holder", new[] { typeof(TestGameMode) });
            }

            GameObject child;

            using (var op = Scaffold.Begin("Add a Player Start"))
            {
                child = op.FindOrCreateChild(prefab, "Player Start", typeof(PlayerStart));

                Assert.IsNotNull(child, string.Join("\n", op.Result.Problems));

                CollectionAssert.Contains(op.Result.Created, Folder + "/Holder.prefab#Player Start", "Named by the prefab's path.");
            }

            Assert.IsTrue(EditorUtility.IsPersistent(child), "The asset's own child is returned.");

            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/Holder.prefab").transform.Find("Player Start").GetComponent<PlayerStart>(), "Saved.");

            using (var op = Scaffold.Begin("Again"))
            {
                Assert.AreEqual(child, op.FindOrCreateChild(prefab, "Player Start", typeof(PlayerStart)));

                CollectionAssert.IsEmpty(op.Result.Created, "The second run creates nothing.");
            }

            Assert.AreEqual(1, AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/Holder.prefab").transform.childCount);
        }

        [Test]
        public void Edit_ChangesThisCopyOrItsPrefabOnlyWhenAsked()
        {
            GameObject prefab;

            using (var op = Scaffold.Begin("Prefab"))
            {
                prefab = op.FindOrCreatePrefab(Folder + "/TestGameMode.prefab", "TestGameMode", new[] { typeof(TestGameMode) });
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);

            _sceneObjects.Add(instance);

            using (var op = Scaffold.Begin("This copy"))
            {
                Assert.IsTrue(op.Edit(instance.GetComponent<GameMode>(), EditIntent.ThisCopy, copy => op.Assign(copy, "playersAtStart", 4)));

                StringAssert.Contains("override on this copy", op.Result.Changed.Single(), "Labelled as an override.");
            }

            Assert.AreEqual(4, instance.GetComponent<GameMode>().PlayersAtStart);

            Assert.AreEqual(1, prefab.GetComponent<GameMode>().PlayersAtStart, "The prefab keeps its value.");

            using (var op = Scaffold.Begin("The prefab"))
            {
                Assert.IsTrue(op.Edit(instance.GetComponent<GameMode>(), EditIntent.SourcePrefab, copy => op.Assign(copy, "playersAtStart", 2)), string.Join("\n", op.Result.Problems));

                CollectionAssert.Contains(op.Result.Changed, Folder + "/TestGameMode.prefab: playersAtStart");
            }

            Assert.AreEqual(2, AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/TestGameMode.prefab").GetComponent<GameMode>().PlayersAtStart, "Asked for: the prefab changes.");

            Assert.AreEqual(4, instance.GetComponent<GameMode>().PlayersAtStart, "The copy keeps its override.");

            using (var op = Scaffold.Begin("Nothing"))
            {
                Assert.IsFalse(op.Edit(null, EditIntent.ThisCopy, _ => { }));
            }
        }

        private GameObject SceneObject_Internal(string name)
        {
            var created = new GameObject(name);

            _sceneObjects.Add(created);

            return created;
        }

        [Test]
        public void FieldType_FollowsSerializedPaths()
        {
            Assert.AreEqual(typeof(PlayerController), Scaffold.FieldType(typeof(TestGameMode), "playerController"));

            Assert.AreEqual(typeof(string), Scaffold.FieldType(typeof(UEArchitectureSettings), "gameplayTags.Array.data[0]"));

            Assert.AreEqual(typeof(AIController), Scaffold.FieldType(typeof(TestPawn), "aiController"));

            Assert.IsNull(Scaffold.FieldType(typeof(GameMode), "nope"));
        }

        [Test]
        public void Naming_FollowsTheConventions()
        {
            var conventions = GeneratorConventions.Defaults;

            Assert.AreEqual("Assets/Prefabs/Gameplay/TestController.prefab", GeneratorNaming.PrefabPath(typeof(TestController), conventions));

            Assert.AreEqual("Assets/Prefabs/UI/TestHUD.prefab", GeneratorNaming.PrefabPath(typeof(TestHUD), conventions));

            Assert.AreEqual("Assets/Prefabs/Modes/Boss/GameplayGameMode.prefab", GeneratorNaming.OverridePath("Boss", "GameplayGameMode", conventions));

            Assert.AreEqual("Assets/Scripts/SitizensPawn.cs", GeneratorNaming.ScriptPath("SitizensPawn", conventions));

            Assert.AreEqual("Assets/Prefabs/Gameplay/SitizensPlayerController.prefab", GeneratorNaming.PrefabPath("SitizensPlayerController", typeof(PlayerController), conventions));

            var custom = new GeneratorConventions("Assets/Game/Code", "Assets/Game/", "Play", "Interface", "{Root}//{Area}/", "{Class} Prefab", "{Root}/Levels/{Scene}/Modes");

            Assert.AreEqual("Assets/Game/Interface/TestHUD Prefab.prefab", GeneratorNaming.PrefabPath(typeof(TestHUD), custom));

            Assert.AreEqual("Assets/Game/Levels/Boss/Modes/Arena Prefab.prefab", GeneratorNaming.OverridePath("Boss", "Arena", custom));

            Assert.IsEmpty(GeneratorNaming.FolderProblem("Assets/Prefabs"));

            Assert.IsNotEmpty(GeneratorNaming.FolderProblem("Packages/com.example/Prefabs"));

            Assert.IsNotEmpty(GeneratorNaming.FolderProblem("Assets/../Outside"));
        }
    }
}
