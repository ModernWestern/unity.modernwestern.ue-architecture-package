using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;
using ModernWestern.UEArchitecture.Tests;

namespace ModernWestern.UEArchitecture.EditorTools.Tests
{
    /// <summary>
    /// The subsystems list and the levels list of Project Settings > UE Architecture: the file scan of a level, the row
    /// order and build indices, the tick chips, and the Build Settings fixes.
    /// </summary>
    public class ProjectListsTests
    {
        private const string Folder = "Assets/__UEATempLists";

        private const string WorldSettingsGuid = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

        private const string ModePrefabGuid = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

        private const string StartScriptGuid = "cccccccccccccccccccccccccccccccc";

        private const string StartPrefabGuid = "dddddddddddddddddddddddddddddddd";

        private const string PlacedModeGuid = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";

        private const string WorldSettingsPrefabGuid = "ffffffffffffffffffffffffffffffff";

        private EditorTestKit _kit;

        private EditorBuildSettingsScene[] _buildScenes;

        [SetUp]
        public void SetUp()
        {
            _buildScenes = EditorBuildSettings.scenes;

            _kit = new EditorTestKit(Folder);
        }

        [TearDown]
        public void TearDown()
        {
            EditorBuildSettings.scenes = _buildScenes;

            _kit.Dispose();
        }

        [Test]
        public void TheFileScan_ReadsWorldSettingsTheOverrideAndTheStarts()
        {
            var yaml = "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n" +
                       "--- !u!1 &100\nGameObject:\n  m_Name: World Settings\n" +
                       "--- !u!114 &101\nMonoBehaviour:\n  m_GameObject: {fileID: 100}\n  m_Script: {fileID: 11500000, guid: " + WorldSettingsGuid + ", type: 3}\n" +
                       "  gameModeOverride: {fileID: 1144, guid: " + ModePrefabGuid + ", type: 3}\n" +
                       "--- !u!114 &102\nMonoBehaviour:\n  m_Script: {fileID: 11500000, guid: " + StartScriptGuid + ", type: 3}\n" +
                       "--- !u!114 &103 stripped\nMonoBehaviour:\n  m_Script: {fileID: 11500000, guid: " + StartScriptGuid + ", type: 3}\n" +
                       "--- !u!1001 &104\nPrefabInstance:\n  m_SourcePrefab: {fileID: 100100000, guid: " + StartPrefabGuid + ", type: 3}\n";

            var summary = SceneFileScan.Parse(yaml, Role_Internal, guid => "Assets/" + guid.Substring(0, 1) + ".prefab");

            Assert.IsTrue(summary.FromText);

            Assert.IsTrue(summary.HasWorldSettings);

            Assert.AreEqual("Assets/b.prefab", summary.OverrideGameMode);

            Assert.AreEqual(string.Empty, summary.PlacedGameMode);

            Assert.AreEqual(2, summary.PlayerStarts, "The placed start and the prefab copy; the stripped entry is the same object again.");

            var placed = SceneFileScan.Parse(yaml + "--- !u!1001 &105\nPrefabInstance:\n  m_SourcePrefab: {fileID: 100100000, guid: " + PlacedModeGuid + ", type: 3}\n", Role_Internal,
                                             guid => "Assets/" + guid.Substring(0, 1) + ".prefab");

            Assert.AreEqual("Assets/e.prefab", placed.PlacedGameMode);

            var empty = SceneFileScan.Parse("--- binary", Role_Internal, guid => string.Empty);

            Assert.IsFalse(empty.FromText);

            Assert.AreEqual(-1, empty.PlayerStarts);
        }

        [Test]
        public void TheRows_PutBuildScenesFirst_WithTheirIndices_AndResolveTheGameMode()
        {
            var summaries = new Dictionary<string, LevelSummary>
            {
                ["Assets/Menu.unity"] = new() { PlayerStarts = 0 },
                ["Assets/Arena.unity"] = new() { OverrideGameMode = "Assets/Modes/Arena Mode.prefab", HasWorldSettings = true, PlayerStarts = 2 },
                ["Assets/Boss.unity"] = new() { PlacedGameMode = "Assets/Boss/BossMode.prefab", OverrideGameMode = "Assets/Modes/Arena Mode.prefab", PlayerStarts = 1 },
                ["Assets/Test.unity"] = new()
            };

            var build = new List<KeyValuePair<string, bool>>
            {
                new("Assets/Arena.unity", false),
                new("Assets/Menu.unity", true),
                new("Assets/Gone.unity", true),
                new("Assets/Boss.unity", true)
            };

            var rows = LevelList.Rows(build, new[] { "Assets/Test.unity", "Assets/Boss.unity", "Assets/Menu.unity", "Assets/Arena.unity" },
                                      path => summaries.TryGetValue(path, out var summary) ? summary : null, path => path != "Assets/Gone.unity", "Assets/Default Mode.prefab");

            CollectionAssert.AreEqual(new[] { "Assets/Arena.unity", "Assets/Menu.unity", "Assets/Gone.unity", "Assets/Boss.unity", "Assets/Test.unity" }, rows.Select(row => row.Path));

            Assert.AreEqual(-1, rows[0].BuildIndex, "Disabled scenes have no build index.");

            Assert.AreEqual(BuildScenes.Status.Disabled, rows[0].Build);

            Assert.AreEqual(0, rows[1].BuildIndex);

            Assert.IsTrue(rows[2].FileMissing);

            Assert.AreEqual(1, rows[3].BuildIndex, "A missing file takes no index.");

            Assert.AreEqual(GameModeSource.WorldSettings, rows[0].Source);

            Assert.AreEqual("Arena Mode.prefab", rows[0].GameMode);

            Assert.AreEqual(GameModeSource.ProjectDefault, rows[1].Source);

            Assert.AreEqual(GameModeSource.Placed, rows[3].Source, "A placed GameMode wins, like at runtime.");

            Assert.IsTrue(rows[3].HasOverride, "Its override can still be cleared.");

            Assert.AreEqual(-1, rows[4].BuildPosition);

            Assert.IsTrue(rows[4].CanAddToBuild);

            Assert.IsFalse(rows[1].CanBeFirst, "Already first.");

            StringAssert.Contains("build 0", rows[1].Describe());

            var noDefault = LevelList.Rows(build, null, path => summaries.TryGetValue(path, out var summary) ? summary : null, path => true, string.Empty);

            Assert.AreEqual(GameModeSource.None, noDefault[1].Source);
        }

        [Test]
        public void TheSubsystemRows_SayGameOrWorldAndWhenTheyTick()
        {
            var rows = SubsystemList.Rows(new[] { typeof(LateTickerAfter), typeof(UnscaledTicker), typeof(DefaultTicker), typeof(DependentSubsystem), typeof(Subsystem), null });

            CollectionAssert.AreEqual(new[] { typeof(DependentSubsystem), typeof(UnscaledTicker), typeof(DefaultTicker), typeof(LateTickerAfter) }, rows.Select(row => row.Type),
                                      "Game first, then World, by name; abstract classes are left out.");

            Assert.IsNull(rows[0].Tick, "It does not tick.");

            Assert.AreEqual("Tick: Update, unscaled", rows[1].Tick);

            Assert.AreEqual("Tick: Update", rows[2].Tick, "Without [Tick]: Update.");

            Assert.AreEqual("Tick: LateUpdate, order 10", rows[3].Tick);

            Assert.AreEqual("World", rows[3].Kind);

            Assert.IsFalse(SubsystemList.Project().Any(row => row.Type == typeof(DefaultTicker)), "Test classes are left out of the page.");
        }

        [Test]
        public void ARealLevel_IsSummarizedFromItsFile()
        {
            var scene = _kit.CreateLevel("Arena");

            var arena = _kit.Prefab("ArenaGameMode", typeof(TestGameMode));

            Assert.IsTrue(UEArchitectureActions.UseGameModeInLevel(arena, scene).Ok);

            Assert.IsTrue(UEArchitectureActions.AddPlayerStarts(scene, 2).Ok);

            EditorSceneManager.SaveScene(scene);

            FrameworkIndex.Invalidate();

            var summary = FrameworkIndex.LevelSummaryOf(scene.path);

            Assert.IsTrue(summary.HasWorldSettings);

            Assert.AreEqual(AssetDatabase.GetAssetPath(arena), summary.OverrideGameMode);

            Assert.AreEqual(2, summary.PlayerStarts);

            var row = FrameworkIndex.LevelRows().Single(each => each.Path == scene.path);

            Assert.AreEqual(GameModeSource.WorldSettings, row.Source);

            Assert.AreEqual(-1, row.BuildPosition);
        }

        [Test]
        public void BuildFixes_AddEnableAndMoveFirst_Once()
        {
            var first = _kit.CreateLevel("First").path;

            var second = _kit.CreateLevel("Second").path;

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(first, true), new EditorBuildSettingsScene(second, false) };

            var enabled = UEArchitectureActions.AddSceneToBuild(second);

            Assert.IsTrue(enabled.Ok, string.Join("\n", enabled.Problems));

            Assert.AreEqual(1, enabled.Changed.Count);

            Assert.AreEqual(BuildScenes.Status.InBuild, BuildScenes.Of(second));

            Assert.AreEqual(1, EditorBuildSettings.scenes.Count(scene => scene.path == second), "Enabled, not added twice.");

            CollectionAssert.IsEmpty(UEArchitectureActions.AddSceneToBuild(second).Changed);

            var moved = UEArchitectureActions.SetFirstLevel(second);

            Assert.IsTrue(moved.Ok, string.Join("\n", moved.Problems));

            CollectionAssert.AreEqual(new[] { second, first }, EditorBuildSettings.scenes.Select(scene => scene.path));

            CollectionAssert.IsEmpty(UEArchitectureActions.SetFirstLevel(second).Changed);

            Assert.IsFalse(UEArchitectureActions.AddSceneToBuild(Folder + "/Nope.unity").Ok);

            FrameworkIndex.Invalidate();

            Assert.AreEqual(0, FrameworkIndex.LevelRows().Single(row => row.Path == second).BuildIndex);
        }

        [Test]
        public void APlacedWorldSettingsPrefab_GivesItsOwnOverride_UnlessTheLevelChangesIt()
        {
            var yaml = "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n" +
                       "--- !u!1001 &200\nPrefabInstance:\n  m_Modification:\n    m_Modifications:\n" +
                       "    - target: {fileID: 1, guid: " + WorldSettingsPrefabGuid + ", type: 3}\n      propertyPath: m_Name\n      value: World Settings\n      objectReference: {fileID: 0}\n" +
                       "  m_SourcePrefab: {fileID: 100100000, guid: " + WorldSettingsPrefabGuid + ", type: 3}\n";

            string OverrideOf(string guid) => guid == WorldSettingsPrefabGuid ? "Assets/Modes/Arena Mode.prefab" : string.Empty;

            var summary = SceneFileScan.Parse(yaml, Role_Internal, guid => "Assets/World Settings.prefab", OverrideOf);

            Assert.IsTrue(summary.HasWorldSettings);

            Assert.AreEqual("Assets/Modes/Arena Mode.prefab", summary.OverrideGameMode, "The prefab's own override, which the level does not change.");

            var cleared = yaml.Replace("propertyPath: m_Name\n      value: World Settings\n", "propertyPath: gameModeOverride\n      value: \n");

            Assert.AreEqual(string.Empty, SceneFileScan.Parse(cleared, Role_Internal, guid => string.Empty, OverrideOf).OverrideGameMode, "The level cleared it on its copy.");
        }

        [Test]
        public void LevelSummaries_StayCached_UntilSomethingTheyUseChanges()
        {
            var mode = _kit.Prefab("ArenaGameMode", typeof(TestGameMode));

            var world = _kit.Prefab("Level Settings");

            var worldPath = AssetDatabase.GetAssetPath(world);

            var scene = _kit.CreateLevel("Cached");

            Assert.IsNotNull(PrefabUtility.InstantiatePrefab(world, scene));

            EditorSceneManager.SaveScene(scene);

            var path = scene.path;

            EditorTestKit.Close(scene);

            FrameworkIndex.Invalidate();

            var first = FrameworkIndex.LevelSummaryOf(path);

            Assert.IsFalse(first.HasWorldSettings);

            var reads = FrameworkIndex.LevelReads_Internal;

            // An unrelated asset is imported: the level is not read again.
            AssetDatabase.CreateAsset(UnityEngine.ScriptableObject.CreateInstance<TestTagAsset>(), Folder + "/Unrelated.asset");

            Assert.AreSame(first, FrameworkIndex.LevelSummaryOf(path), "Still cached.");

            Assert.AreEqual(reads, FrameworkIndex.LevelReads_Internal);

            // The prefab it uses becomes a World Settings with its own GameMode Override: the level is read again.
            var contents = PrefabUtility.LoadPrefabContents(worldPath);

            try
            {
                var serialized = new SerializedObject(contents.AddComponent<WorldSettings>());

                serialized.FindProperty("gameModeOverride").objectReferenceValue = mode.GetComponent<GameMode>();

                serialized.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(contents, worldPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            var second = FrameworkIndex.LevelSummaryOf(path);

            Assert.IsTrue(second.HasWorldSettings, "Read again after the prefab changed.");

            Assert.AreEqual(AssetDatabase.GetAssetPath(mode), second.OverrideGameMode, "The placed World Settings prefab's own override.");

            Assert.AreEqual(GameModeSource.WorldSettings, FrameworkIndex.LevelRows().Single(row => row.Path == path).Source);
        }

        private static SceneObjectRole Role_Internal(string guid)
        {
            switch (guid)
            {
                case WorldSettingsGuid:
                case WorldSettingsPrefabGuid:
                    return SceneObjectRole.WorldSettings;

                case ModePrefabGuid:
                case PlacedModeGuid:
                    return SceneObjectRole.GameMode;

                case StartScriptGuid:
                case StartPrefabGuid:
                    return SceneObjectRole.PlayerStart;

                default:
                    return SceneObjectRole.Other;
            }
        }
    }
}
