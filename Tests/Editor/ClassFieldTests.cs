using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEditor.IMGUI.Controls;
using Object = UnityEngine.Object;
using ModernWestern.UEArchitecture.Tests;

namespace ModernWestern.UEArchitecture.EditorTools.Tests
{
    public class ClassFieldTests
    {
        private const string Folder = "Assets/__UEATempClassField";

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
        public void ThePicker_ShowsOnClassSlotsAndPrefabFieldsOnly()
        {
            Assert.IsTrue(ClassField.ShowsPicker(true, false, false), "A framework slot on a level object (a placed GameMode's Player Controller).");

            Assert.IsTrue(ClassField.ShowsPicker(false, true, false), "A user's field on a prefab asset.");

            Assert.IsFalse(ClassField.ShowsPicker(false, false, false), "A user's field on a level object points to that level (Sitizens' playerCharacter).");

            Assert.IsFalse(ClassField.ShowsPicker(true, true, true), "Hidden when the selected objects hold different values.");

            Assert.IsTrue(Scaffold.IsFrameworkField(Scaffold.FieldOf(typeof(TestGameMode), "playerController")), "Declared by GameMode, even on a subclass.");

            Assert.IsFalse(Scaffold.IsFrameworkField(Scaffold.FieldOf(typeof(TestLevelGameMode), "playerCharacter")));

            Assert.AreEqual("None (players start without a Pawn)", ClassField.NoneLabel("defaultPawn", true));

            Assert.AreEqual("None", ClassField.NoneLabel("defaultPawn", false));

            var probe = new GameObject("Probe");

            try
            {
                StringAssert.Contains("GameMode makes a copy", ClassField.SceneObjectProblem(probe.AddComponent<GameMode>()));
            }
            finally
            {
                Object.DestroyImmediate(probe);
            }
        }

        [Test]
        public void Naming_FindsTheLevelOfAnOverrideFolderAndWhereVariantsGo()
        {
            var conventions = GeneratorConventions.Defaults;

            Assert.AreEqual("Boss", GeneratorNaming.SceneOfOverrideFolder("Assets/Prefabs/Modes/Boss", conventions));

            Assert.IsNull(GeneratorNaming.SceneOfOverrideFolder("Assets/Prefabs/Modes", conventions));

            Assert.IsNull(GeneratorNaming.SceneOfOverrideFolder("Assets/Prefabs/Modes/Boss/Old", conventions));

            Assert.IsNull(GeneratorNaming.SceneOfOverrideFolder("Assets/Prefabs/Gameplay", conventions));

            Assert.AreEqual("Assets/Prefabs/Modes/Boss/ArenaPawn.prefab",
                            GeneratorNaming.VariantPath("Assets/Prefabs/Gameplay/ArenaPawn.prefab", "ArenaPawn", "ArenaPawn", "Assets/Prefabs/Modes/Boss/ArenaGameMode.prefab", null,
                                                        "Assets/Prefabs/Gameplay", conventions), "The owner is a level's own GameMode.");

            Assert.AreEqual("Assets/Prefabs/Modes/Boss/ArenaPawn.prefab",
                            GeneratorNaming.VariantPath("Assets/Prefabs/Gameplay/ArenaPawn.prefab", "ArenaPawn", "ArenaPawn", string.Empty, "Boss", "Assets/Prefabs/Gameplay", conventions),
                            "The field is shown for a level.");

            Assert.AreEqual("Assets/Prefabs/Gameplay/Arena PawnVariant.prefab",
                            GeneratorNaming.VariantPath("Assets/Prefabs/Gameplay/Arena Pawn.prefab", "Arena Pawn", "ArenaPawn", "Assets/Prefabs/Gameplay/ArenaGameMode.prefab", null,
                                                        "Assets/Prefabs/Gameplay", conventions));

            Assert.AreEqual("Assets/Prefabs/Gameplay/PawnVariant.prefab",
                            GeneratorNaming.VariantPath("Packages/com.example/Pawn.prefab", "Pawn", "Pawn", string.Empty, null, "Assets/Prefabs/Gameplay", conventions),
                            "Packages are read-only.");

            Assert.AreEqual("ArenaPlayerController", GeneratorNaming.InitialClassName("ArenaGameMode", false, "My Game", "PlayerController"));

            Assert.AreEqual("SitizensPawn", GeneratorNaming.InitialClassName("WorldSettings", true, "Sitizens", "Pawn"));

            Assert.AreEqual("MyGameMode", GeneratorNaming.InitialClassName(null, true, "", "GameMode"));

            Assert.AreEqual("Assets/Game/Modes", GeneratorNaming.ScriptFolderFor("Assets/Game/Modes/ArenaGameMode.cs", conventions), "Next to the owner's script.");

            Assert.AreEqual("Assets/Scripts", GeneratorNaming.ScriptFolderFor("Packages/com.modernwestern.ue-architecture/Runtime/Gameplay/GameMode.cs", conventions));
        }

        [Test]
        public void AssignClass_BuildsTheClassPrefabOnceAndPutsTheComponent()
        {
            var gameMode = _kit.Prefab("TestGameMode", typeof(TestGameMode)).GetComponent<GameMode>();

            var first = UEArchitectureActions.AssignClass(gameMode, "playerController", typeof(TestController));

            Assert.IsTrue(first.Ok, string.Join("\n", first.Problems));

            var path = Folder + "/Prefabs/Gameplay/TestController.prefab";

            CollectionAssert.Contains(first.Created, path, "Named after the class, in {Root}/Gameplay.");

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            Assert.AreEqual(prefab.GetComponent<TestController>(), gameMode.PlayerControllerPrefab, "The component, not the GameObject.");

            StringAssert.Contains("TestController.prefab", first.Message);

            var second = UEArchitectureActions.AssignClass(gameMode, "playerController", typeof(TestController));

            Assert.IsTrue(second.Ok);

            CollectionAssert.IsEmpty(second.Created, "The second run creates nothing.");

            CollectionAssert.IsEmpty(second.Changed);

            var hud = UEArchitectureActions.AssignClass(gameMode, "hud", typeof(TestHUD));

            CollectionAssert.Contains(hud.Created, Folder + "/Prefabs/UI/TestHUD.prefab", "HUDs go to the UI area.");

            var cleared = UEArchitectureActions.AssignClass(gameMode, "playerController");

            Assert.IsTrue(cleared.Ok);

            Assert.IsNull(gameMode.PlayerControllerPrefab);

            var picked = UEArchitectureActions.AssignClass(gameMode, "playerController", null, prefab);

            Assert.AreEqual(prefab.GetComponent<TestController>(), gameMode.PlayerControllerPrefab);

            CollectionAssert.IsEmpty(picked.Created);

            Assert.IsFalse(UEArchitectureActions.AssignClass(gameMode, "playerController", typeof(TestPawn)).Ok, "A Pawn is not a Player Controller.");

            Assert.IsFalse(UEArchitectureActions.AssignClass(gameMode, "playerController", null, AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/Prefabs/UI/TestHUD.prefab")).Ok);

            Assert.IsFalse(UEArchitectureActions.AssignClass(gameMode, "nope", typeof(TestController)).Ok);

            Assert.AreEqual(prefab.GetComponent<TestController>(), gameMode.PlayerControllerPrefab, "Failed calls change nothing.");
        }

        [Test]
        public void AssignClass_IsOneUndoStepOnALevelObject()
        {
            var scene = _kit.CreateLevel("Pick");

            var holder = new GameObject("World Settings");

            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(holder, scene);

            var worldSettings = holder.AddComponent<WorldSettings>();

            var result = UEArchitectureActions.AssignClass(worldSettings, "gameModeOverride", typeof(TestGameMode));

            Assert.IsTrue(result.Ok, string.Join("\n", result.Problems));

            Assert.IsNotNull(worldSettings.GameModeOverride);

            Undo.PerformUndo();

            Assert.IsNull(worldSettings.GameModeOverride, "Undo empties the field (the prefab file stays).");

            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/Prefabs/Gameplay/TestGameMode.prefab"));
        }

        [Test]
        public void CreateVariantAndAssign_MakesAChildPrefabOnce()
        {
            var basePawn = _kit.Prefab("ArenaPawn", typeof(TestPawn));

            var gameMode = _kit.Prefab("ArenaGameMode", typeof(TestGameMode)).GetComponent<GameMode>();

            UEArchitectureActions.AssignClass(gameMode, "defaultPawn", null, basePawn);

            var first = UEArchitectureActions.CreateVariantAndAssign(gameMode, "defaultPawn");

            Assert.IsTrue(first.Ok, string.Join("\n", first.Problems));

            var variantPath = Folder + "/ArenaPawnVariant.prefab";

            CollectionAssert.Contains(first.Created, variantPath, "Next to the current prefab.");

            var variant = AssetDatabase.LoadAssetAtPath<GameObject>(variantPath);

            Assert.IsTrue(ScaffoldOp.IsVariantOf(variant, basePawn), "A child of the current prefab.");

            Assert.AreEqual(variant.GetComponent<Pawn>(), gameMode.DefaultPawnPrefab);

            UEArchitectureActions.AssignClass(gameMode, "defaultPawn", null, basePawn);

            var again = UEArchitectureActions.CreateVariantAndAssign(gameMode, "defaultPawn");

            CollectionAssert.IsEmpty(again.Created, "The variant is reused.");

            Assert.AreEqual(variant.GetComponent<Pawn>(), gameMode.DefaultPawnPrefab);

            var forLevel = UEArchitectureActions.CreateVariantAndAssign(gameMode, "defaultPawn", "Boss");

            Assert.IsTrue(forLevel.Ok, string.Join("\n", forLevel.Problems));

            CollectionAssert.Contains(forLevel.Created, _kit.LevelFolder("Boss") + "/TestPawn.prefab", "For a level: in its own folder, named after the class.");

            var empty = _kit.Prefab("EmptyGameMode", typeof(TestGameMode)).GetComponent<GameMode>();

            Assert.IsFalse(UEArchitectureActions.CreateVariantAndAssign(empty, "defaultPawn").Ok, "Nothing to make a variant of.");
        }

        [Test]
        public void SaveAsPrefabAndAssign_TurnsALevelObjectIntoThePrefabOfTheSlot()
        {
            var scene = _kit.CreateLevel("Placed");

            var gameModeObject = new GameObject("GameplayGameMode");

            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(gameModeObject, scene);

            var gameMode = gameModeObject.AddComponent<TestGameMode>();

            var controllerObject = new GameObject("Player Input");

            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(controllerObject, scene);

            var sceneController = controllerObject.AddComponent<TestController>();

            Assert.IsTrue(UEArchitectureActions.Assign(gameMode, "playerController", sceneController).Ok, "A placed GameMode accepts a level object.");

            var result = UEArchitectureActions.SaveAsPrefabAndAssign(gameMode, "playerController");

            Assert.IsTrue(result.Ok, string.Join("\n", result.Problems));

            var path = Folder + "/Prefabs/Gameplay/TestController.prefab";

            CollectionAssert.Contains(result.Created, path);

            Assert.IsTrue(EditorUtility.IsPersistent(gameMode.PlayerControllerPrefab), "The slot holds the prefab now.");

            Assert.AreEqual(path, AssetDatabase.GetAssetPath(gameMode.PlayerControllerPrefab));

            Assert.IsTrue(PrefabUtility.IsPartOfPrefabInstance(controllerObject), "The level object became a copy of the prefab.");

            var again = UEArchitectureActions.SaveAsPrefabAndAssign(gameMode, "playerController");

            Assert.IsTrue(again.Ok);

            CollectionAssert.IsEmpty(again.Created, "Already a prefab: nothing new.");
        }

        [Test]
        public void RevertOverride_PutsAVariantRowBackToItsBase()
        {
            var basePrefab = _kit.Prefab("ArenaGameMode", typeof(TestGameMode));

            GameObject variant;

            using (var op = Scaffold.Begin("Variant"))
            {
                variant = op.CreateVariant(basePrefab, Folder + "/BossGameMode.prefab");
            }

            var gameMode = variant.GetComponent<GameMode>();

            UEArchitectureActions.Assign(gameMode, "playersAtStart", 3);

            var property = new SerializedObject(gameMode).FindProperty("playersAtStart");

            Assert.IsTrue(property.prefabOverride, "The variant's row is an override (drawn bold).");

            var result = UEArchitectureActions.RevertOverride(gameMode, "playersAtStart");

            Assert.IsTrue(result.Ok, string.Join("\n", result.Problems));

            Assert.AreEqual(1, gameMode.PlayersAtStart, "Back to the base's value.");

            Assert.IsFalse(new SerializedObject(gameMode).FindProperty("playersAtStart").prefabOverride);

            var nothing = UEArchitectureActions.RevertOverride(gameMode, "playersAtStart");

            Assert.IsTrue(nothing.Ok);

            CollectionAssert.IsEmpty(nothing.Changed);
        }

        [Test]
        public void ThePicker_OffersActionsOnTheCurrentPrefabFirst()
        {
            var pawn = _kit.Prefab("ArenaPawn", typeof(TestPawn));

            var extras = new[]
            {
                new ClassPick(ClassPickKind.Variant, typeof(TestPawn), pawn, "Variant of ArenaPawn"),
                new ClassPick(ClassPickKind.OpenPrefab, typeof(TestPawn), pawn, "Open prefab")
            };

            var dropdown = new ClassPickerDropdown(null, typeof(Pawn), "Pawn", null, ClassPickerDropdown.DefaultNoneLabel, extras);

            var root = (AdvancedDropdownItem)typeof(ClassPickerDropdown).GetMethod("BuildRoot", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                                                                        .Invoke(dropdown, null);

            var names = EditorTestKit.Children(root).Select(item => item.name).ToArray();

            Assert.AreEqual("Variant of ArenaPawn", names[0]);

            Assert.AreEqual("Open prefab", names[1]);

            CollectionAssert.Contains(names, "Prefabs");

            CollectionAssert.Contains(names, ClassPickerDropdown.NewScriptLabel);
        }

        [Test]
        public void ThePicker_TellsApartPrefabsNamedAfterTheSameClass()
        {
            var basePrefab = UEArchitectureActions.CreateClassPrefab(typeof(TestGameMode)).Result as GameObject;

            Assert.IsNotNull(basePrefab);

            GameObject other;

            GameObject boss;

            GameObject arena;

            using (var op = Scaffold.Begin("Level copies"))
            {
                other = op.FindOrCreatePrefab(Folder + "/Other/TestGameMode.prefab", "TestGameMode", new[] { typeof(TestGameMode) });

                boss = op.CreateVariant(basePrefab, _kit.LevelFolder("Boss") + "/TestGameMode.prefab");

                arena = op.CreateVariant(basePrefab, _kit.LevelFolder("Arena") + "/TestGameMode.prefab");
            }

            var conventions = UEArchitectureEditorSettings.Current;

            var types = new[] { typeof(GameMode), typeof(TestGameMode) };

            var main = ClassPickerModel.Prefabs(typeof(GameMode), new[] { basePrefab, other, boss, arena }, types, AssetDatabase.GetAssetPath,
                                                folder => GeneratorNaming.SceneOfOverrideFolder(folder, conventions), "Boss", out var otherLevels);

            var labels = main.Select(pick => pick.Label).ToArray();

            CollectionAssert.AllItemsAreUnique(labels, "No two entries look the same.");

            CollectionAssert.Contains(labels, "TestGameMode (this level)");

            CollectionAssert.Contains(labels, $"TestGameMode  ({Folder}/Prefabs/Gameplay)");

            CollectionAssert.Contains(labels, $"TestGameMode  ({Folder}/Other)");

            Assert.AreEqual("TestGameMode (Arena's own)", otherLevels.Single().Label, "Another level's own copy is listed apart.");

            Assert.AreEqual(arena, otherLevels.Single().Prefab);

            FrameworkIndex.Invalidate();

            Assert.AreEqual(basePrefab, FrameworkIndex.PrefabWithExactly(typeof(TestGameMode)), "The class's prefab is never a level's own copy (nor another folder's).");

            CollectionAssert.Contains(FrameworkIndex.PrefabsWith(typeof(GameMode)), boss, "Variants are indexed through their base prefab.");
        }

        [Test]
        public void RightClick_AddsRevertToUnitysMenuOnlyOnOverriddenClassSlots()
        {
            var controller = _kit.Prefab("TestController", typeof(TestController));

            var basePrefab = _kit.Prefab("ArenaGameMode", typeof(TestGameMode));

            GameObject variant;

            using (var op = Scaffold.Begin("Variant"))
            {
                variant = op.CreateVariant(basePrefab, Folder + "/BossGameMode.prefab");
            }

            var serialized = new SerializedObject(variant.GetComponent<GameMode>());

            Assert.IsNull(ClassField.RevertLabel(serialized.FindProperty("playerController")), "Not overridden.");

            UEArchitectureActions.AssignClass(variant.GetComponent<GameMode>(), "playerController", null, controller);

            UEArchitectureActions.Assign(variant.GetComponent<GameMode>(), "playersAtStart", 2);

            serialized = new SerializedObject(variant.GetComponent<GameMode>());

            Assert.AreEqual("Revert to ArenaGameMode", ClassField.RevertLabel(serialized.FindProperty("playerController")).text);

            Assert.IsNull(ClassField.RevertLabel(serialized.FindProperty("playersAtStart")), "Only class slots get it; Unity's own menu reverts the rest.");
        }

        [Test]
        public void NewScript_OffersTheSameCommandLine()
        {
            var gameMode = _kit.Prefab("ArenaGameMode", typeof(TestGameMode)).GetComponent<GameMode>();

            var command = ClassField.NewScriptCommand("arena pawn", typeof(Pawn), "Assets/Game Scripts", gameMode, "defaultPawn");

            var parsed = CliArguments.Parse(CliTests.SplitLikeAShell(command));

            CollectionAssert.IsEmpty(parsed.Problems);

            Assert.AreEqual("new-script", parsed.Verb);

            Assert.AreEqual("ArenaPawn", parsed.Get("class"));

            Assert.AreEqual(typeof(Pawn).FullName, parsed.Get("parent"));

            Assert.AreEqual("Assets/Game Scripts", parsed.Get("folder"));

            Assert.AreEqual(Folder + "/ArenaGameMode.prefab", parsed.Get("assign-to"));

            Assert.AreEqual("defaultPawn", parsed.Get("property"));

            Assert.AreEqual(typeof(TestGameMode).FullName, parsed.Get("component"));

            Assert.IsTrue(parsed.HasQuit);
        }

        [Test]
        public void SaveAsPrefabAndAssign_NeverOverwritesAnExistingPrefab()
        {
            var scene = _kit.CreateLevel("Placed");

            var gameModeObject = new GameObject("GameplayGameMode");

            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(gameModeObject, scene);

            var gameMode = gameModeObject.AddComponent<TestGameMode>();

            var controllerObject = new GameObject("Player Input");

            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(controllerObject, scene);

            UEArchitectureActions.Assign(gameMode, "playerController", controllerObject.AddComponent<TestController>());

            var pawn = _kit.Prefab("Taken", typeof(TestPawn));

            var refused = UEArchitectureActions.SaveAsPrefabAndAssign(gameMode, "playerController", Folder + "/Taken.prefab");

            Assert.IsFalse(refused.Ok, "A prefab of another class is there.");

            Assert.IsNotNull(pawn.GetComponent<TestPawn>(), "And it is left as it was.");

            Assert.IsNull(pawn.GetComponent<TestController>());

            var existing = _kit.Prefab("Existing", typeof(TestController));

            var reused = UEArchitectureActions.SaveAsPrefabAndAssign(gameMode, "playerController", Folder + "/Existing.prefab");

            Assert.IsTrue(reused.Ok, string.Join("\n", reused.Problems));

            CollectionAssert.IsEmpty(reused.Created);

            Assert.AreEqual(existing.GetComponent<TestController>(), gameMode.PlayerControllerPrefab, "A prefab of the same class is used, not overwritten.");

            Assert.IsFalse(PrefabUtility.IsPartOfPrefabInstance(controllerObject), "The level object is left as it is.");
        }

        [Test]
        public void PrefabModeOwners_AreFoundAgainByTheirPrefabAsset()
        {
            var gameModePrefab = _kit.Prefab("ArenaGameMode", typeof(TestGameMode));

            var stage = UnityEditor.SceneManagement.PrefabStageUtility.OpenPrefab(AssetDatabase.GetAssetPath(gameModePrefab));

            try
            {
                var root = stage.prefabContentsRoot.GetComponent<GameMode>();

                Assert.IsFalse(EditorCompat.IsInUnsavedScene(root), "Prefab Mode's preview scene is not an unsaved level.");

                Assert.IsFalse(EditorCompat.IsLevelObject(root));

                Assert.IsNull(ClassField.SaveUntitledScenes_Internal(new Object[] { root }), "Nothing to save first (and the preview scene is never saved).");

                var result = new ActionResult("test");

                Assert.IsTrue(PendingSteps.TryTarget(root, "defaultPawn", result, out var target), string.Join("\n", result.Problems));

                Assert.AreEqual(gameModePrefab.GetComponent<GameMode>(), EditorCompat.FromObjectKey(target.objectKey), "The step fills the prefab asset.");

                Assert.AreEqual(Folder + "/ArenaGameMode.prefab", UEArchitectureCli.Address(root));

                Assert.IsFalse(ClassField.IsLocked(new Object[] { root }));
            }
            finally
            {
                UnityEditor.SceneManagement.StageUtility.GoToMainStage();
            }
        }

        [Test]
        public void TheDrawer_CoversEveryFrameworkSlotType()
        {
            var drawerTypes = typeof(FrameworkClassDrawer).GetCustomAttributes(typeof(CustomPropertyDrawer), false).Length;

            Assert.AreEqual(7, drawerTypes, "GameMode, PlayerController, AIController, Pawn, HUD, GameInstance and LoadingScreen.");
        }

        [Test]
        public void TheDrawer_UnwrapsOnlyArraysAndLists()
        {
            var vehicle = typeof(ClassFieldProbeGarage).GetField(nameof(ClassFieldProbeGarage.vehicle)).FieldType;

            Assert.AreEqual(typeof(ClassFieldProbeVehicle<ClassFieldProbeConfig>), FrameworkClassDrawer.ElementType(vehicle),
                            "A generic Pawn holds that Pawn, not its type argument: Car prefabs and the '+' stay available.");

            Assert.AreEqual(typeof(TestPawn), FrameworkClassDrawer.ElementType(typeof(ClassFieldProbeGarage).GetField(nameof(ClassFieldProbeGarage.pawns)).FieldType));

            Assert.AreEqual(typeof(Pawn), FrameworkClassDrawer.ElementType(typeof(ClassFieldProbeGarage).GetField(nameof(ClassFieldProbeGarage.spares)).FieldType));

            Assert.AreEqual(typeof(HUD), FrameworkClassDrawer.ElementType(typeof(HUD)));
        }

        [Test]
        public void ClassFields_RefuseObjectsOfAnotherLevel_LikeUnitysField()
        {
            var levelA = _kit.CreateLevel("LevelA");

            var levelB = _kit.CreateLevel("LevelB");

            var owner = new GameObject("GameplayGameMode");

            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(owner, levelA);

            var gameMode = owner.AddComponent<TestLevelGameMode>();

            var near = new GameObject("Player A");

            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(near, levelA);

            var far = new GameObject("Player B");

            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(far, levelB);

            var prefab = _kit.Prefab("ArenaPawn", typeof(TestPawn));

            var previous = UnityEditor.SceneManagement.EditorSceneManager.preventCrossSceneReferences;

            try
            {
                UnityEditor.SceneManagement.EditorSceneManager.preventCrossSceneReferences = true;

                Assert.IsTrue(ClassField.RefusesCrossScene(far.AddComponent<TestPawn>(), gameMode), "A Pawn of Level B in a field of Level A: Unity could not save it.");

                Assert.IsTrue(ClassField.RefusesCrossScene(far, gameMode), "Dropped as a GameObject too.");

                Assert.IsFalse(ClassField.RefusesCrossScene(near.AddComponent<TestPawn>(), gameMode), "The same level.");

                Assert.IsFalse(ClassField.RefusesCrossScene(prefab.GetComponent<TestPawn>(), gameMode), "A prefab.");

                Assert.IsFalse(ClassField.RefusesCrossScene(far, prefab.GetComponent<TestPawn>()), "A prefab's field (its picker takes no level objects anyway).");

                Assert.IsFalse(ClassField.RefusesCrossScene(null, gameMode), "Clearing the field.");

                UnityEditor.SceneManagement.EditorSceneManager.preventCrossSceneReferences = false;

                Assert.IsFalse(ClassField.RefusesCrossScene(far, gameMode), "Allowed when the project allows cross-scene references.");
            }
            finally
            {
                UnityEditor.SceneManagement.EditorSceneManager.preventCrossSceneReferences = previous;
            }
        }
    }

    /// <summary>
    /// A generic Pawn, for the drawer of a field of a closed generic type.
    /// </summary>
    public class ClassFieldProbeVehicle<T> : Pawn
    {
    }

    public class ClassFieldProbeConfig
    {
    }

    /// <summary>
    /// Fields of framework types the drawer covers: a closed generic Pawn, a list and an array.
    /// </summary>
    public class ClassFieldProbeGarage : MonoBehaviour
    {
        public ClassFieldProbeVehicle<ClassFieldProbeConfig> vehicle;

        public System.Collections.Generic.List<TestPawn> pawns;

        public Pawn[] spares;
    }
}
