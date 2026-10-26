using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using ModernWestern.UEArchitecture.Tests;

namespace ModernWestern.UEArchitecture.EditorTools.Tests
{
    public class LevelActionsTests
    {
        private const string Folder = "Assets/__UEATempLevels";

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
        public void OverrideGameModeForLevel_TwiceMakesOneVariantAndUsesIt()
        {
            var scene = _kit.CreateLevel("Boss");

            var arena = _kit.Prefab("ArenaGameMode", typeof(TestGameMode));

            Assert.IsTrue(UEArchitectureActions.UseGameModeInLevel(arena, scene).Ok);

            var first = UEArchitectureActions.OverrideGameModeForLevel(scene.path);

            Assert.IsTrue(first.Ok, string.Join("\n", first.Problems));

            var path = _kit.LevelFolder("Boss") + "/TestGameMode.prefab";

            CollectionAssert.Contains(first.Created, path, "In the level's own folder, named after the class.");

            var variant = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            Assert.IsTrue(ScaffoldOp.IsVariantOf(variant, arena), "A child of the GameMode the level used.");

            var worldSettings = GameModeInfo.Find<WorldSettings>(scene);

            Assert.AreEqual(variant.GetComponent<GameMode>(), worldSettings.GameModeOverride);

            Assert.AreEqual(variant.GetComponent<GameMode>(), GameModeInfo.Effective(scene, out GameModeSource source));

            Assert.AreEqual(GameModeSource.WorldSettings, source);

            Assert.IsTrue(GameModeInfo.IsLevelOverride(worldSettings.GameModeOverride, "Boss"));

            var second = UEArchitectureActions.OverrideGameModeForLevel(scene.path);

            Assert.IsTrue(second.Ok, string.Join("\n", second.Problems));

            CollectionAssert.IsEmpty(second.Created, "The second run creates nothing.");

            CollectionAssert.IsEmpty(second.Changed);

            Assert.AreEqual(1, AssetDatabase.FindAssets("t:Prefab", new[] { _kit.LevelFolder("Boss") }).Length, "One asset.");
        }

        [Test]
        public void OverrideGameModeForLevel_AddsWorldSettingsInOneUndoStep()
        {
            var scene = _kit.CreateLevel("Plain");

            var arena = _kit.Prefab("ArenaGameMode", typeof(TestGameMode));

            UEArchitectureActions.SetProjectDefaultGameMode(arena);

            Undo.ClearAll();

            Assert.IsNull(GameModeInfo.Find<WorldSettings>(scene));

            var result = UEArchitectureActions.OverrideGameModeForLevel(scene);

            Assert.IsTrue(result.Ok, string.Join("\n", result.Problems));

            var worldSettings = GameModeInfo.Find<WorldSettings>(scene);

            Assert.IsNotNull(worldSettings, "World Settings was added.");

            Assert.AreEqual("World Settings", worldSettings.name);

            Assert.IsTrue(ScaffoldOp.IsVariantOf(worldSettings.GameModeOverride.gameObject, arena), "A variant of the project default.");

            Assert.IsTrue(scene.isDirty);

            Undo.PerformUndo();

            Assert.IsNull(GameModeInfo.Find<WorldSettings>(scene), "One Undo removes World Settings and its assignment.");

            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<GameObject>(_kit.LevelFolder("Plain") + "/TestGameMode.prefab"), "Files are not undone.");
        }

        [Test]
        public void OverrideGameModeForLevel_WithoutAnyGameModeMakesAPlainOne()
        {
            Assume.That(FrameworkIndex.Settings == null || FrameworkIndex.Settings.DefaultGameMode == null, "The project has a default GameMode.");

            var scene = _kit.CreateLevel("Empty");

            var result = UEArchitectureActions.OverrideGameModeForLevel(scene);

            Assert.IsTrue(result.Ok, string.Join("\n", result.Problems));

            var gameMode = GameModeInfo.Find<WorldSettings>(scene).GameModeOverride;

            Assert.AreEqual(typeof(GameMode), gameMode.GetType());

            Assert.AreEqual(_kit.LevelFolder("Empty") + "/GameMode.prefab", AssetDatabase.GetAssetPath(gameMode));

            Assert.AreEqual(PrefabAssetType.Regular, PrefabUtility.GetPrefabAssetType(gameMode.gameObject));
        }

        [Test]
        public void LevelActions_RespectAGameModePlacedInTheLevel()
        {
            var scene = _kit.CreateLevel("Placed");

            var placed = new GameObject("GameplayGameMode");

            SceneManager.MoveGameObjectToScene(placed, scene);

            placed.AddComponent<TestGameMode>();

            var arena = _kit.Prefab("ArenaGameMode", typeof(TestGameMode));

            var result = UEArchitectureActions.OverrideGameModeForLevel(scene);

            Assert.IsFalse(result.Ok);

            StringAssert.Contains("wins over World Settings", result.Problems[0]);

            Assert.IsFalse(UEArchitectureActions.UseGameModeInLevel(arena, scene).Ok);

            Assert.IsNull(GameModeInfo.Find<WorldSettings>(scene), "Nothing was added.");

            Assert.IsFalse(AssetDatabase.IsValidFolder(_kit.LevelFolder("Placed")));
        }

        [Test]
        public void UseClearAndProjectDefault_AreRepeatable()
        {
            var scene = _kit.CreateLevel("Arena");

            var arena = _kit.Prefab("ArenaGameMode", typeof(TestGameMode));

            var used = UEArchitectureActions.UseGameModeInLevel(arena, scene.path);

            Assert.IsTrue(used.Ok, string.Join("\n", used.Problems));

            Assert.AreEqual(arena.GetComponent<GameMode>(), GameModeInfo.Find<WorldSettings>(scene).GameModeOverride);

            var usedAgain = UEArchitectureActions.UseGameModeInLevel(arena, scene.path);

            CollectionAssert.IsEmpty(usedAgain.Created);

            CollectionAssert.IsEmpty(usedAgain.Changed);

            var cleared = UEArchitectureActions.ClearOverride(scene.path);

            Assert.IsTrue(cleared.Ok);

            Assert.IsNull(GameModeInfo.Find<WorldSettings>(scene).GameModeOverride);

            Assert.IsNotNull(GameModeInfo.Find<WorldSettings>(scene), "World Settings stays.");

            CollectionAssert.IsEmpty(UEArchitectureActions.ClearOverride(scene.path).Changed, "Nothing left to clear.");

            var madeDefault = UEArchitectureActions.SetProjectDefaultGameMode(arena);

            Assert.IsTrue(madeDefault.Ok, string.Join("\n", madeDefault.Problems));

            Assert.IsNotNull(FrameworkIndex.Settings, "The settings asset exists (made when missing).");

            Assert.AreEqual(arena.GetComponent<GameMode>(), FrameworkIndex.Settings.DefaultGameMode);

            var defaultAgain = UEArchitectureActions.SetProjectDefaultGameMode(arena);

            CollectionAssert.IsEmpty(defaultAgain.Created);

            CollectionAssert.IsEmpty(defaultAgain.Changed);

            Assert.AreEqual(GameModeSource.ProjectDefault, Source_Internal(scene), "Without an override the level uses the project default.");

            Assert.IsFalse(UEArchitectureActions.SetProjectDefaultGameMode(_kit.Prefab("NotAMode", typeof(TestPawn))).Ok);
        }

        [Test]
        public void CreateGameModeVariantForLevel_UsesTheGivenBase()
        {
            var scene = _kit.CreateLevel("Side");

            var arena = _kit.Prefab("ArenaGameMode", typeof(TestGameMode));

            var result = UEArchitectureActions.CreateGameModeVariantForLevel(arena, scene.path);

            Assert.IsTrue(result.Ok, string.Join("\n", result.Problems));

            var path = _kit.LevelFolder("Side") + "/TestGameMode.prefab";

            Assert.IsTrue(ScaffoldOp.IsVariantOf(AssetDatabase.LoadAssetAtPath<GameObject>(path), arena));

            CollectionAssert.IsEmpty(UEArchitectureActions.CreateGameModeVariantForLevel(arena, scene.path).Created);
        }

        [Test]
        public void CreateMissingDefaultClasses_FillsOnlyTheEmptySlots()
        {
            var gameMode = _kit.Prefab("ArenaGameMode", typeof(TestGameMode)).GetComponent<GameMode>();

            var hud = _kit.Prefab("ArenaHUD", typeof(TestHUD));

            UEArchitectureActions.AssignClass(gameMode, "hud", null, hud);

            var first = UEArchitectureActions.CreateMissingDefaultClasses(gameMode, Folder + "/Defaults");

            Assert.IsTrue(first.Ok, string.Join("\n", first.Problems));

            CollectionAssert.Contains(first.Created, Folder + "/Defaults/PlayerController.prefab");

            CollectionAssert.Contains(first.Created, Folder + "/Defaults/Pawn.prefab");

            Assert.AreEqual(typeof(PlayerController), gameMode.PlayerControllerPrefab.GetType(), "The base class.");

            Assert.AreEqual(typeof(Pawn), gameMode.DefaultPawnPrefab.GetType());

            Assert.AreEqual(hud.GetComponent<HUD>(), gameMode.HUDPrefab, "A set slot stays.");

            var second = UEArchitectureActions.CreateMissingDefaultClasses(gameMode, Folder + "/Defaults");

            CollectionAssert.IsEmpty(second.Created, "The second run creates nothing.");

            CollectionAssert.IsEmpty(second.Changed);
        }

        [Test]
        public void Effective_PicksTheGameModeLikeTheRuntime()
        {
            var scene = _kit.CreateLevel("Order");

            var arena = _kit.Prefab("ArenaGameMode", typeof(GameMode)).GetComponent<GameMode>();

            var fallback = _kit.Prefab("DefaultGameMode", typeof(GameMode)).GetComponent<GameMode>();

            var settings = ScriptableObject.CreateInstance<UEArchitectureSettings>();

            var runtimeSettings = typeof(GameFramework).GetProperty(nameof(GameFramework.Settings), BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            var previous = runtimeSettings.GetValue(null);

            try
            {
                runtimeSettings.SetValue(null, settings);

                AssertSamePick_Internal(scene, settings, null, "No GameMode anywhere.");

                settings.Set_Internal(null, fallback, null);

                AssertSamePick_Internal(scene, settings, fallback, "The project default.");

                var holder = new GameObject("World Settings");

                SceneManager.MoveGameObjectToScene(holder, scene);

                UEArchitectureActions.Assign(holder.AddComponent<WorldSettings>(), "gameModeOverride", arena);

                AssertSamePick_Internal(scene, settings, arena, "World Settings wins over the project default.");

                var placed = new GameObject("Placed GameMode");

                SceneManager.MoveGameObjectToScene(placed, scene);

                var placedGameMode = placed.AddComponent<GameMode>();

                AssertSamePick_Internal(scene, settings, placedGameMode, "A placed GameMode wins over everything.");
            }
            finally
            {
                runtimeSettings.SetValue(null, previous);

                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void TheIndex_CountsTheLevelsThatUseAGameMode()
        {
            var arena = _kit.Prefab("ArenaGameMode", typeof(TestGameMode));

            var other = _kit.Prefab("OtherGameMode", typeof(TestGameMode));

            foreach (var name in new[] { "One", "Two" })
            {
                var scene = _kit.CreateLevel(name);

                UEArchitectureActions.UseGameModeInLevel(arena, scene);

                UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            }

            var third = _kit.CreateLevel("Three");

            UEArchitectureActions.UseGameModeInLevel(other, third);

            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(third);

            FrameworkIndex.Invalidate();

            Assert.AreEqual(2, FrameworkIndex.LevelsUsing(arena));

            Assert.AreEqual(1, FrameworkIndex.LevelsUsing(other));

            StringAssert.Contains("ArenaGameMode.prefab is used by 2 levels", FrameworkIndex.UsageLine(arena));
        }

        [Test]
        public void OverrideGameModeForLevel_KeepsLevelsWithTheSameNameApart()
        {
            var arena = _kit.Prefab("ArenaGameMode", typeof(TestGameMode));

            var desert = _kit.CreateLevel("Gameplay", "Desert");

            UEArchitectureActions.UseGameModeInLevel(arena, desert);

            var first = UEArchitectureActions.OverrideGameModeForLevel(desert);

            Assert.IsTrue(first.Ok, string.Join("\n", first.Problems));

            var desertPath = _kit.LevelFolder("Gameplay") + "/TestGameMode.prefab";

            CollectionAssert.Contains(first.Created, desertPath);

            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(desert);

            var snow = _kit.CreateLevel("Gameplay", "Snow");

            UEArchitectureActions.UseGameModeInLevel(arena, snow);

            Assert.AreEqual("Gameplay", GameModeInfo.LevelKey(desert));

            Assert.AreEqual("Gameplay (Snow)", GameModeInfo.LevelKey(snow), "The class fields of Snow's World Settings make their variants there.");

            var second = UEArchitectureActions.OverrideGameModeForLevel(snow);

            Assert.IsTrue(second.Ok, string.Join("\n", second.Problems));

            var snowPath = _kit.LevelFolder("Gameplay (Snow)") + "/TestGameMode.prefab";

            CollectionAssert.Contains(second.Created, snowPath, "Desert's copy belongs to Desert: Snow gets its own folder.");

            var desertOverride = GameModeInfo.Find<WorldSettings>(desert).GameModeOverride;

            var snowOverride = GameModeInfo.Find<WorldSettings>(snow).GameModeOverride;

            Assert.AreEqual(desertPath, AssetDatabase.GetAssetPath(desertOverride));

            Assert.AreEqual(snowPath, AssetDatabase.GetAssetPath(snowOverride));

            Assert.IsTrue(GameModeInfo.IsLevelOverride(desertOverride, desert));

            Assert.IsTrue(GameModeInfo.IsLevelOverride(snowOverride, snow));

            Assert.IsFalse(GameModeInfo.IsLevelOverride(desertOverride, snow), "Another level's own GameMode is never this level's own.");

            var again = UEArchitectureActions.OverrideGameModeForLevel(snow);

            CollectionAssert.IsEmpty(again.Created, "The second run creates nothing.");

            CollectionAssert.IsEmpty(again.Changed);

            CollectionAssert.AreEqual(new[] { _kit.LevelFolder("Gameplay"), _kit.LevelFolder("Gameplay (Snow)") },
                                      GeneratorNaming.LevelFolders(snow.path, UEArchitectureEditorSettings.Current));
        }

        [Test]
        public void OverrideGameModeForLevel_KeepsLevelsWithTheSameNameAndOtherGameModesApart()
        {
            var coop = _kit.CreateLevel("Main", "Coop");

            var versus = _kit.CreateLevel("Main", "Versus");

            UEArchitectureActions.UseGameModeInLevel(_kit.Prefab("CoopMode", typeof(TestGameMode)), coop);

            UEArchitectureActions.UseGameModeInLevel(_kit.Prefab("VersusMode", typeof(TestLevelGameMode)), versus);

            var first = UEArchitectureActions.OverrideGameModeForLevel(coop);

            Assert.IsTrue(first.Ok, string.Join("\n", first.Problems));

            CollectionAssert.Contains(first.Created, _kit.LevelFolder("Main") + "/TestGameMode.prefab");

            Assert.AreEqual("Main (Versus)", GameModeInfo.LevelKey(versus), "The Override button names the folder the action writes to.");

            var second = UEArchitectureActions.OverrideGameModeForLevel(versus);

            Assert.IsTrue(second.Ok, string.Join("\n", second.Problems));

            CollectionAssert.Contains(second.Created, _kit.LevelFolder("Main (Versus)") + "/TestLevelGameMode.prefab",
                                      "Modes/Main holds Coop's GameMode, even though the file names differ: Versus gets its own folder.");

            var coopOwn = GameModeInfo.Find<WorldSettings>(coop).GameModeOverride;

            var versusOwn = GameModeInfo.Find<WorldSettings>(versus).GameModeOverride;

            var conventions = UEArchitectureEditorSettings.Current;

            Assert.AreEqual("Main", GameModeInfo.LevelKey(coop));

            Assert.AreEqual("Main (Versus)", GameModeInfo.LevelKey(versus));

            Assert.AreEqual(GameModeInfo.LevelKey(coop), GeneratorNaming.SceneOfOverrideFolder(GeneratorNaming.FolderOf(AssetDatabase.GetAssetPath(coopOwn)), conventions),
                            "The picker calls Coop's own copy \"this level\".");

            Assert.AreEqual(GameModeInfo.LevelKey(versus), GeneratorNaming.SceneOfOverrideFolder(GeneratorNaming.FolderOf(AssetDatabase.GetAssetPath(versusOwn)), conventions));

            CollectionAssert.IsEmpty(UEArchitectureActions.OverrideGameModeForLevel(versus).Created, "The second run creates nothing.");

            var coopPawn = PawnVariant_Internal(coop, coopOwn);

            var versusPawn = PawnVariant_Internal(versus, versusOwn);

            Assert.AreEqual(_kit.LevelFolder("Main") + "/TestPawn.prefab", coopPawn);

            Assert.AreEqual(_kit.LevelFolder("Main (Versus)") + "/TestPawn.prefab", versusPawn, "Each level's Pawn variant is its own.");
        }

        [Test]
        public void TwoLevelsWhoseGameModesShareAFolder_GetTheirOwnPawnVariants()
        {
            var coop = _kit.CreateLevel("Main", "Coop");

            var versus = _kit.CreateLevel("Main", "Versus");

            Assert.IsTrue(UEArchitectureActions.CreateGameModeVariantForLevel(_kit.Prefab("CoopMode", typeof(TestGameMode)), coop).Ok);

            // Made by an older version, or by hand: both levels' own GameModes in Modes/Main.
            Assert.IsTrue(UEArchitectureActions.CreateGameModeVariantForLevel(_kit.Prefab("VersusMode", typeof(TestLevelGameMode)), versus, _kit.LevelFolder("Main")).Ok);

            var coopOwn = GameModeInfo.Find<WorldSettings>(coop).GameModeOverride;

            var versusOwn = GameModeInfo.Find<WorldSettings>(versus).GameModeOverride;

            Assert.AreEqual(_kit.LevelFolder("Main"), GeneratorNaming.FolderOf(AssetDatabase.GetAssetPath(versusOwn)));

            Assert.AreNotEqual(SelectedGameModePanel.VariantLevel(coop, coopOwn), SelectedGameModePanel.VariantLevel(versus, versusOwn));

            Assert.AreNotEqual(PawnVariant_Internal(coop, coopOwn), PawnVariant_Internal(versus, versusOwn), "A Pawn edit for one level never changes the other.");
        }

        [Test]
        public void ASharedGameMode_SaysEveryLevelUsingItChanges()
        {
            var arena = _kit.Prefab("ArenaGameMode", typeof(TestGameMode));

            foreach (var name in new[] { "One", "Two" })
            {
                UEArchitectureActions.UseGameModeInLevel(arena, _kit.CreateLevel(name));
            }

            Assert.AreEqual(2, FrameworkIndex.LevelsUsing(arena), "Open levels with unsaved changes are counted as they are now.");

            Assert.AreEqual("Shared prefab: ArenaGameMode.prefab, used by 2 levels. Changes here apply to all of them.", FrameworkIndex.SharedLine(arena));

            var gameMode = arena.GetComponent<GameMode>();

            Assert.IsFalse(GameModeInfo.IsLevelOverride(gameMode, SceneManager.GetSceneByPath(Folder + "/One.unity")), "Not in a level's own folder.");
        }

        [Test]
        public void TryPlacedPawn_FindsAPawnTheGameModePossessesFromCode()
        {
            var scene = _kit.CreateLevel("Sitizens");

            var gameModeObject = new GameObject("GameplayGameMode");

            SceneManager.MoveGameObjectToScene(gameModeObject, scene);

            var gameMode = gameModeObject.AddComponent<TestLevelGameMode>();

            var serialized = new SerializedObject(gameMode);

            Assert.IsFalse(GameModeInfo.TryPlacedPawn(serialized, out _, out _));

            var player = new GameObject("Player");

            SceneManager.MoveGameObjectToScene(player, scene);

            UEArchitectureActions.Assign(gameMode, "playerCharacter", player.AddComponent<TestPawn>());

            serialized.Update();

            Assert.IsTrue(GameModeInfo.TryPlacedPawn(serialized, out var field, out var pawn));

            Assert.AreEqual("playerCharacter", field);

            Assert.AreEqual(player, pawn.gameObject);

            Assert.IsNull(GameModeInfo.AssetOf(gameMode), "A GameMode that only exists in the level has no prefab asset.");
        }

        /// <summary>
        /// "Variant of ArenaPawn for this level" from the level's Selected GameMode panel: where the Pawn variant goes.
        /// </summary>
        private string PawnVariant_Internal(Scene scene, GameMode own)
        {
            var pawn = _kit.Prefab("ArenaPawn", typeof(TestPawn));

            Assert.IsTrue(UEArchitectureActions.AssignClass(own, "defaultPawn", null, pawn).Ok);

            var result = UEArchitectureActions.CreateVariantAndAssign(own, "defaultPawn", SelectedGameModePanel.VariantLevel(scene, own));

            Assert.IsTrue(result.Ok, string.Join("\n", result.Problems));

            CollectionAssert.IsNotEmpty(result.Created, "A new variant, not another level's: " + result.Message);

            return AssetDatabase.GetAssetPath(own.DefaultPawnPrefab);
        }

        private static GameModeSource Source_Internal(Scene scene)
        {
            GameModeInfo.Effective(scene, out GameModeSource source);

            return source;
        }

        private static void AssertSamePick_Internal(Scene scene, UEArchitectureSettings settings, GameMode expected, string message)
        {
            var editorPick = GameModeInfo.Effective(scene, settings, out _);

            Assert.AreEqual(expected, editorPick, "Editor: " + message);

            var spawned = GameMode.Spawn_Internal(scene);

            try
            {
                if (expected == null)
                {
                    Assert.IsNull(spawned, "Runtime: " + message);

                    return;
                }

                Assert.IsNotNull(spawned, "Runtime: " + message);

                if (EditorUtility.IsPersistent(expected))
                {
                    Assert.AreNotEqual(expected, spawned, "The runtime spawns a copy of the prefab.");

                    Assert.AreEqual(expected.name, spawned.name, "Runtime: " + message);

                    Assert.AreEqual(scene, spawned.gameObject.scene);
                }
                else
                {
                    Assert.AreEqual(expected, spawned, "Runtime: " + message);
                }
            }
            finally
            {
                if (spawned != null && !ReferenceEquals(spawned, expected) && !EditorUtility.IsPersistent(spawned))
                {
                    Object.DestroyImmediate(spawned.gameObject);
                }
            }
        }
    }
}
