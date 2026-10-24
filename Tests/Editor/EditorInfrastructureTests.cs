using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;
using ModernWestern.UEArchitecture.Tests;

namespace ModernWestern.UEArchitecture.EditorTools.Tests
{
    public class EditorInfrastructureTests
    {
        private const string Folder = "Assets/__UEATempInfrastructure";

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(Folder);

            FrameworkIndex.Invalidate();
        }

        [Test]
        public void EditorCompat_NameOnCreate_CallsBackWithThePath()
        {
            var action = ScriptableObject.CreateInstance<ProbeAction>();

            try
            {
#if UNITY_6000_4_OR_NEWER
                action.Action(EntityId.None, "Assets/Game/ArenaPawn.cs", null);
#else
                action.Action(0, "Assets/Game/ArenaPawn.cs", null);
#endif

                Assert.AreEqual("Assets/Game/ArenaPawn.cs", action.Named);
            }
            finally
            {
                Object.DestroyImmediate(action);
            }
        }

        [Test]
        public void EditorCompat_FindsObjectsAndKeysAssets()
        {
            var holder = new GameObject("UEA FindAll probe");

            try
            {
                var settings = holder.AddComponent<WorldSettings>();

                CollectionAssert.Contains(EditorCompat.FindAll<WorldSettings>(), settings);

                holder.SetActive(false);

                CollectionAssert.DoesNotContain(EditorCompat.FindAll<WorldSettings>(), settings);

                CollectionAssert.Contains(EditorCompat.FindAll<WorldSettings>(true), settings);

                Assert.AreNotEqual(EditorCompat.SessionKey(settings), EditorCompat.SessionKey(holder));

                EditorCompat.Ping(settings);
            }
            finally
            {
                Object.DestroyImmediate(holder);
            }

            Assert.IsEmpty(EditorCompat.ObjectKey(null));

            Assert.IsNull(EditorCompat.FromObjectKey("not a key"));
        }

        [Test]
        public void ActionResult_CollectsAndPrintsOneJsonLine()
        {
            var result = new ActionResult("create-prefab");

            result.AddCreated("Assets/Prefabs/Gameplay/ArenaPawn.prefab");

            result.AddCreated("Assets/Prefabs/Gameplay/ArenaPawn.prefab");

            var step = new ActionResult("step");

            step.AddChanged("Assets/Prefabs/Gameplay/ArenaGameMode.prefab: defaultPawn");

            step.AddProblem("Something \"quoted\".");

            step.Pending = true;

            result.Merge(step);

            Assert.AreEqual(1, result.Created.Count, "No duplicates.");

            Assert.IsFalse(result.Ok);

            Assert.IsTrue(result.Pending);

            var json = result.ToJson();

            Assert.IsFalse(json.Contains("\n"), "One line.");

            StringAssert.StartsWith("{\"verb\":\"create-prefab\",\"ok\":false,\"pending\":true", json);

            var back = ActionResult.FromJson(json);

            CollectionAssert.AreEqual(result.Problems, back.Problems);

            CollectionAssert.AreEqual(result.Changed, back.Changed);
        }

        [Test]
        public void ThePicker_GroupsPrefabsAndClassesWithoutAPrefab()
        {
            GameObject hero;

            using (var op = Scaffold.Begin("Picker prefabs"))
            {
                hero = op.FindOrCreatePrefab(Folder + "/Hero.prefab", "Hero", new[] { typeof(TestPawn) });
            }

            var candidates = ClassPickerModel.CandidateTypes(typeof(Pawn));

            CollectionAssert.Contains(candidates, typeof(Pawn));

            CollectionAssert.DoesNotContain(candidates, typeof(TestPawn), "Test assemblies are left out.");

            Assert.IsFalse(ClassPickerModel.CandidateTypes(typeof(Controller)).Contains(typeof(Controller)), "Abstract classes are left out.");

            var types = new[] { typeof(Pawn), typeof(TestPawn) };

            var prefabs = ClassPickerModel.Prefabs(typeof(Pawn), new[] { hero }, types);

            Assert.AreEqual("Hero · TestPawn", prefabs.Single().Label);

            Assert.AreEqual(ClassPickKind.Prefab, prefabs.Single().Kind);

            Assert.AreEqual(hero, prefabs.Single().Prefab);

            var classes = ClassPickerModel.ClassesWithoutPrefab(types, new[] { hero }, typeof(Pawn));

            CollectionAssert.AreEqual(new[] { typeof(Pawn) }, classes.Select(pick => pick.Type), "TestPawn has a prefab.");

            var huds = ClassPickerModel.ClassesWithoutPrefab(new[] { typeof(HUD), typeof(TestHUD) }, new[] { hero }, typeof(HUD));

            CollectionAssert.AreEqual(new[] { "HUD", "TestHUD" }, huds.Select(pick => pick.Label));

            CollectionAssert.IsEmpty(ClassPickerModel.Prefabs(typeof(HUD), new[] { hero }, types));
        }

        [Test]
        public void ThePickerDropdown_BuildsItsGroups()
        {
            ClassPick picked = default;

            var dropdown = new ClassPickerDropdown(null, typeof(PlayerController), "Player Controller", pick => picked = pick);

            var root = (UnityEditor.IMGUI.Controls.AdvancedDropdownItem)typeof(ClassPickerDropdown)
                                                                        .GetMethod("BuildRoot", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                                                                        .Invoke(dropdown, null);

            var names = EditorTestKit.Children(root).Select(item => item.name).ToArray();

            Assert.AreEqual("Prefabs", names[0]);

            CollectionAssert.Contains(names, "Classes without a prefab");

            CollectionAssert.Contains(names, ClassPickerDropdown.NewScriptLabel);

            Assert.AreEqual(ClassPickerDropdown.DefaultNoneLabel, names.Last());

            var newScript = EditorTestKit.Children(root).First(item => item.name == ClassPickerDropdown.NewScriptLabel);

            typeof(ClassPickerDropdown).GetMethod("ItemSelected", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(dropdown, new object[] { newScript });

            Assert.AreEqual(ClassPickKind.NewScript, picked.Kind);

            Assert.AreEqual(typeof(PlayerController), picked.Type);
        }

        [Test]
        public void TheNamePopup_PreviewsWhatItWrites()
        {
            var preview = NamePopup.ScriptPreview("Sitizens Player Controller", typeof(PlayerController), "Assets/__UEATempNeverWritten", true);

            Assert.IsTrue(preview.CanCreate, string.Join("\n", preview.Problems));

            StringAssert.StartsWith("Writes SitizensPlayerController.cs in Assets/__UEATempNeverWritten, then SitizensPlayerController.prefab in ", preview.Text);

            Assert.IsFalse(NamePopup.ScriptPreview(" ", typeof(PlayerController), null, true).CanCreate);

            Assert.IsFalse(NamePopup.ScriptPreview("Boss", typeof(TestPawn), null, true).CanCreate, "Sealed parent.");
        }

        [Test]
        public void TheIndex_FindsFrameworkPrefabsAndWhoUsesThem()
        {
            GameObject controller;

            GameObject gameMode;

            using (var op = Scaffold.Begin("Index prefabs"))
            {
                controller = op.FindOrCreatePrefab(Folder + "/TestController.prefab", "TestController", new[] { typeof(TestController) });

                gameMode = op.FindOrCreatePrefab(Folder + "/TestGameMode.prefab", "TestGameMode", new[] { typeof(TestGameMode) });

                op.Assign(gameMode.GetComponent<GameMode>(), "playerController", controller);
            }

            FrameworkIndex.Invalidate();

            CollectionAssert.Contains(FrameworkIndex.PrefabsWith(typeof(PlayerController)), controller);

            CollectionAssert.Contains(FrameworkIndex.PrefabsWith(typeof(Controller)), controller);

            CollectionAssert.DoesNotContain(FrameworkIndex.PrefabsWith(typeof(Pawn)), controller);

            Assert.AreEqual(controller, FrameworkIndex.PrefabWithExactly(typeof(TestController)));

            CollectionAssert.Contains(FrameworkIndex.GameModesUsing(controller), gameMode.GetComponent<GameMode>());

            var version = FrameworkIndex.Version;

            FrameworkIndex.OnAssetsChanged_Internal(new[] { Folder + "/Other.prefab" }, new string[0], new string[0], new string[0]);

            Assert.Greater(FrameworkIndex.Version, version, "A changed prefab clears the index.");

            version = FrameworkIndex.Version;

            FrameworkIndex.OnAssetsChanged_Internal(new[] { Folder + "/Picture.png" }, new string[0], new string[0], new string[0]);

            Assert.AreEqual(version, FrameworkIndex.Version, "Other assets do not.");
        }

        [Test]
        public void TheIndex_FindsTheLevelsThatUseAPrefab()
        {
            GameObject gameMode;

            using (var op = Scaffold.Begin("Level prefab"))
            {
                gameMode = op.FindOrCreatePrefab(Folder + "/TestGameMode.prefab", "TestGameMode", new[] { typeof(TestGameMode) });
            }

            var holder = new GameObject("World Settings");

            try
            {
                var worldSettings = holder.AddComponent<WorldSettings>();

                using (var op = Scaffold.Begin("Use"))
                {
                    op.Assign(worldSettings, "gameModeOverride", gameMode);
                }

                // A copy of the open scene: the scene the tests run in stays as it is.
                Assert.IsTrue(EditorSceneManager.SaveScene(holder.scene, Folder + "/Boss.unity", true));
            }
            finally
            {
                Object.DestroyImmediate(holder);
            }

            FrameworkIndex.Invalidate();

            CollectionAssert.AreEqual(new[] { Folder + "/Boss.unity" }, FrameworkIndex.ScenesReferencing(gameMode));
        }

        [Test]
        public void ProjectTags_AddParentsAndSkipInvalidNames()
        {
            CollectionAssert.AreEqual(new[] { "Status", "Status.Burning", "Status.Frozen", "Team" }, ProjectTags.Build(new[] { "Status.Frozen", "Team", "Status.Burning", "bad..tag", "Team" }));

            CollectionAssert.IsEmpty(ProjectTags.Build(null));

            Assert.IsTrue(ProjectTags.IsKnown(string.Empty), "No tag is always fine.");
        }

        [Test]
        public void TheEditorSettings_GiveUsableConventions()
        {
            var conventions = UEArchitectureEditorSettings.Current;

            Assert.IsEmpty(GeneratorNaming.FolderProblem(GeneratorNaming.PrefabFolder(typeof(Pawn), conventions)));

            if (!File.Exists("ProjectSettings/UEArchitectureEditorSettings.asset"))
            {
                Assert.AreEqual(GeneratorConventions.Defaults.PrefabRoot, conventions.PrefabRoot);

                Assert.AreEqual("Assets/Prefabs/Gameplay/Pawn.prefab", Scaffold.PrefabPathFor(typeof(Pawn)));
            }
        }

        [Test]
        public void TheEditorSettingsExamples_ShowWhatTheToolsWrite()
        {
            var defaults = GeneratorConventions.Defaults;

            var perClass = new GeneratorConventions(defaults.ScriptsFolder, defaults.PrefabRoot, defaults.GameplayArea, defaults.UIArea, "{Root}/{Area}/{Class}", "{Class}Prefab",
                                                    defaults.OverrideFolderPattern);

            var examples = UEArchitectureEditorSettingsProvider.Examples(perClass).ToDictionary(row => row.Label, row => row.Path);

            Assert.AreEqual("Assets/Prefabs/Gameplay/MyPlayerController/MyPlayerControllerPrefab.prefab", examples["Player Controller"],
                            "The folder and the name of the class the tools write, not of PlayerController.");

            Assert.AreEqual(GeneratorNaming.PrefabPath("MyPlayerController", typeof(PlayerController), perClass), examples["Player Controller"]);

            Assert.AreEqual("Assets/Prefabs/UI/MyHUD/MyHUDPrefab.prefab", examples["HUD"]);

            Assert.AreEqual("Assets/Prefabs/Modes/Boss/MyGameModePrefab.prefab", examples["GameMode for the Boss level"]);

            Assert.AreEqual("Assets/Prefabs/Gameplay/MyPlayerController.prefab", UEArchitectureEditorSettingsProvider.Examples(defaults).ToDictionary(row => row.Label, row => row.Path)["Player Controller"]);
        }

        [Test]
        public void LastCreatedFiles_KeepTheLabelOfTheActionThatWroteTheScript()
        {
            var previousPaths = LastCreatedFiles.Paths;

            var previousLabel = LastCreatedFiles.Label;

            try
            {
                var wrote = new ActionResult("new-script") { Message = "Wrote ArenaPawn.cs." };

                wrote.AddCreated("Assets/Game/ArenaPawn.cs");

                LastCreatedFiles.Remember(wrote);

                // The prefab the pending step made after the compile.
                var finished = new ActionResult("complete-pending") { Message = "Finished 1 step(s)." };

                finished.AddCreated("Assets/Prefabs/Gameplay/ArenaPawn.prefab");

                LastCreatedFiles.Remember(finished, true);

                Assert.AreEqual("Wrote ArenaPawn.cs.", LastCreatedFiles.Label, "\"Remove Files Made by the Last Action\" names the action the user ran.");

                CollectionAssert.AreEqual(new[] { "Assets/Game/ArenaPawn.cs", "Assets/Prefabs/Gameplay/ArenaPawn.prefab" }, LastCreatedFiles.Paths);

                var next = new ActionResult("create-class-prefab") { Message = "Created TestHUD.prefab." };

                next.AddCreated("Assets/Prefabs/UI/TestHUD.prefab");

                LastCreatedFiles.Remember(next);

                Assert.AreEqual("Created TestHUD.prefab.", LastCreatedFiles.Label, "A new action replaces the files and the label.");

                CollectionAssert.AreEqual(new[] { "Assets/Prefabs/UI/TestHUD.prefab" }, LastCreatedFiles.Paths);

                LastCreatedFiles.Forget();

                LastCreatedFiles.Remember(finished, true);

                Assert.AreEqual("Finished 1 step(s).", LastCreatedFiles.Label, "With nothing remembered yet, the step's own message.");
            }
            finally
            {
                LastCreatedFiles.Forget();

                if (previousPaths.Length > 0)
                {
                    var previous = new ActionResult { Message = previousLabel };

                    foreach (var path in previousPaths)
                    {
                        previous.AddCreated(path);
                    }

                    LastCreatedFiles.Remember(previous);
                }
            }
        }

        [Test]
        public void EditorCompat_EveryWrapperWorksOnThisUnity()
        {
            var tree = new ProbeTree();

            tree.Reload();

            Assert.AreEqual(2, tree.RowCount, "Two top rows (the child is folded).");

            tree.SetExpanded(1, true);

            tree.Reload();

            Assert.AreEqual(3, tree.RowCount);

            CollectionAssert.IsEmpty(tree.SelectedIds);

            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.FindAssets("TestPawn t:MonoScript").Select(AssetDatabase.GUIDToAssetPath)
                                                                                .First(path => path.EndsWith("/TestPawn.cs", System.StringComparison.Ordinal)));

            var key = EditorCompat.ObjectKey(script);

            Assert.AreEqual(script, EditorCompat.FromObjectKey(key));

            Assert.AreEqual(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(script)), EditorCompat.AssetGuidOfObjectKey(key));

            Assert.IsFalse(EditorCompat.IsSceneObjectKey(key));

            Assert.IsFalse(EditorCompat.IsLevelObject(script));

            var holder = new GameObject("UEA EventSystem probe");

            try
            {
                Assert.IsTrue(EditorCompat.IsLevelObject(holder));

                var eventSystem = System.Type.GetType("UnityEngine.EventSystems.EventSystem, UnityEngine.UI");

                if (eventSystem != null)
                {
                    holder.AddComponent(eventSystem);

                    var module = EditorCompat.AddInputModule(holder);

                    Assert.IsNotNull(module, "An input module for the EventSystem.");

                    Assert.AreSame(module, EditorCompat.AddInputModule(holder), "An existing module is kept.");
                }
            }
            finally
            {
                Object.DestroyImmediate(holder);
            }

            Assert.IsNull(EditorCompat.AddInputModule(null));
        }

        [Test]
        public void TheIndex_ReadsOnlyTheChangedPrefabsAgain()
        {
            GameObject pawn;

            using (var op = Scaffold.Begin("Index"))
            {
                pawn = op.FindOrCreatePrefab(Folder + "/TestPawn.prefab", "TestPawn", new[] { typeof(TestPawn) });
            }

            CollectionAssert.Contains(FrameworkIndex.PrefabsWith(typeof(Pawn)), pawn, "A prefab a tool saved is read without a full scan.");

            GameObject variant;

            using (var op = Scaffold.Begin("Variant"))
            {
                variant = op.CreateVariant(pawn, Folder + "/TestPawnVariant.prefab");
            }

            CollectionAssert.Contains(FrameworkIndex.PrefabsWith(typeof(Pawn)), variant, "Variants are found through the prefab they are made from.");

            Assert.AreEqual(pawn, FrameworkIndex.PrefabWithExactly(typeof(TestPawn)), "A plain prefab before a variant.");

            AssetDatabase.DeleteAsset(Folder + "/TestPawnVariant.prefab");

            FrameworkIndex.OnAssetsChanged_Internal(new string[0], new[] { Folder + "/TestPawnVariant.prefab" }, new string[0], new string[0]);

            CollectionAssert.DoesNotContain(FrameworkIndex.PrefabsWith(typeof(Pawn)), variant);

            CollectionAssert.Contains(FrameworkIndex.PrefabsWith(typeof(Pawn)), pawn);
        }

        private sealed class ProbeTree : CompatTreeView
        {
            protected override void AddRows(System.Action<int, int, string> add)
            {
                add(1, 0, "Arena");

                add(2, 1, "Arena Pawn");

                add(3, 0, "Boss");
            }
        }

        private sealed class ProbeAction : NameOnCreateAction
        {
            public string Named;

            protected override void OnNamed(string pathName, string resourceFile)
            {
                Named = pathName;
            }
        }
    }
}
