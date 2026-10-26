using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using ModernWestern.UEArchitecture.Tests;

namespace ModernWestern.UEArchitecture.EditorTools.Tests
{
    public class MenuTests
    {
        private const string Folder = "Assets/__UEATempMenus";

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
        public void GameObjectMenus_PlaceObjectsInTheRightLevelAndParent()
        {
            var level = _kit.CreateLevel("Other");

            Assert.AreNotEqual(SceneManager.GetActiveScene(), level, "The level is not the active scene.");

            Undo.ClearAll();

            var start = FrameworkMenus.CreatePlayerStart(level);

            Assert.AreEqual(level, start.gameObject.scene, "In the level it was asked for, not the active scene.");

            Assert.AreEqual("Player Start", start.name);

            var parent = new GameObject("Spawns");

            SceneManager.MoveGameObjectToScene(parent, level);

            var child = FrameworkMenus.Create_Internal<PlayerStart>("Player Start", parent, level, true);

            Assert.AreEqual(parent.transform, child.transform.parent);

            Assert.AreEqual(Vector3.zero, child.transform.localPosition, "Aligned to its parent.");

            var second = FrameworkMenus.Create_Internal<PlayerStart>("Player Start", parent, level, true);

            Assert.AreNotEqual(child.name, second.name, "Siblings get unique names.");

            Undo.PerformUndo();

            Assert.IsTrue(second == null, "Undo removes the last one.");
        }

        [Test]
        public void AssetMenus_TakeOnlyCompiledFrameworkScripts()
        {
            var pawnScript = Script_Internal(typeof(TestPawn));

            var levelModeScript = Script_Internal(typeof(TestLevelGameMode));

            var otherScript = Script_Internal(typeof(MenuTests));

            Assert.IsNotNull(pawnScript);

            var types = FrameworkAssetMenus.FrameworkScripts_Internal(new Object[] { pawnScript, levelModeScript, otherScript });

            CollectionAssert.AreEqual(new[] { typeof(TestPawn), typeof(TestLevelGameMode) }, types, "Scripts that are not framework components are left out.");

            var result = UEArchitectureActions.CreateClassPrefab(typeof(TestPawn));

            Assert.IsTrue(result.Ok, string.Join("\n", result.Problems));

            CollectionAssert.Contains(result.Created, Folder + "/Prefabs/Gameplay/TestPawn.prefab");
        }

        [Test]
        public void ContextMenus_ActOnThePrefabAssetOfACopy()
        {
            var level = _kit.CreateLevel("Copies");

            var prefab = _kit.Prefab("ArenaGameMode", typeof(TestGameMode));

            var copy = (GameObject)PrefabUtility.InstantiatePrefab(prefab, level);

            Assert.AreEqual(prefab.GetComponent<GameMode>(), GameModeInfo.AssetOf(copy.GetComponent<GameMode>()), "A copy in a level stands for its prefab.");

            Assert.AreEqual(prefab.GetComponent<GameMode>(), GameModeInfo.AssetOf(prefab.GetComponent<GameMode>()));
        }

        private static MonoScript Script_Internal(System.Type type)
        {
            return AssetDatabase.FindAssets(type.Name + " t:MonoScript")
                                .Select(guid => AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid)))
                                .FirstOrDefault(script => script != null && script.GetClass() == type);
        }
    }
}
