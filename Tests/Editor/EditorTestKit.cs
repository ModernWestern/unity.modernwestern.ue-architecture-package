using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ModernWestern.UEArchitecture.EditorTools.Tests
{
    /// <summary>
    /// A temporary sandbox for editor tool tests: generated prefabs and scripts go under one folder of Assets/ (the
    /// editor settings point there until disposed), levels are saved there, and the project's settings asset is put back
    /// as it was. Everything is deleted when disposed.
    /// </summary>
    internal sealed class EditorTestKit : IDisposable
    {
        private const string SettingsFolder = "Assets/Resources";

        private readonly GeneratorConventions _previousConventions;

        private readonly List<string> _levels = new();

        private readonly bool _hadSettings;

        private readonly bool _hadResources;

        private readonly UEArchitectureSettings _settings;

        private readonly GameMode _defaultGameMode;

        private readonly GameInstance _gameInstance;

        private readonly LoadingScreen _loadingScreen;

        private readonly string[] _gameplayTags;

        public EditorTestKit(string folder)
        {
            Folder = folder;

            AssetDatabase.DeleteAsset(folder);

            FrameworkIndex.Invalidate();

            _settings = FrameworkIndex.Settings;

            _hadSettings = _settings != null;

            _hadResources = AssetDatabase.IsValidFolder(SettingsFolder);

            if (_hadSettings)
            {
                _defaultGameMode = _settings.DefaultGameMode;

                _gameInstance = _settings.GameInstancePrefab;

                _loadingScreen = _settings.LoadingScreen;

                _gameplayTags = _settings.GameplayTags.ToArray();
            }

            var defaults = GeneratorConventions.Defaults;

            _previousConventions = UEArchitectureEditorSettings.instance.Swap_Internal(new GeneratorConventions(folder + "/Scripts", folder + "/Prefabs", defaults.GameplayArea,
                                                                                                               defaults.UIArea, defaults.PrefabFolderPattern,
                                                                                                               defaults.PrefabNamePattern, defaults.OverrideFolderPattern));
        }

        public string Folder { get; }

        /// <summary>
        /// Where the level overrides of a level go while the kit is active.
        /// </summary>
        public string LevelFolder(string sceneName) => $"{Folder}/Prefabs/Modes/{sceneName}";

        /// <summary>
        /// A new, empty level saved as Folder/&lt;name&gt;.unity (or Folder/&lt;subfolder&gt;/&lt;name&gt;.unity) and open
        /// next to the test's scene.
        /// </summary>
        public Scene CreateLevel(string name, string subfolder = null)
        {
            var folder = string.IsNullOrEmpty(subfolder) ? Folder : $"{Folder}/{subfolder}";

            Directory.CreateDirectory(folder);

            AssetDatabase.Refresh();

            var path = $"{folder}/{name}.unity";

            // An untitled scene cannot be added next to another untitled one: save a copy of the active scene and empty it.
            Assert.IsTrue(EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), path, true), "Saved the level");

            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);

            foreach (var root in scene.GetRootGameObjects())
            {
                Object.DestroyImmediate(root);
            }

            EditorSceneManager.SaveScene(scene);

            _levels.Add(path);

            return scene;
        }

        /// <summary>
        /// A prefab with <paramref name="types"/> on its root, at Folder/&lt;name&gt;.prefab.
        /// </summary>
        public GameObject Prefab(string name, params Type[] types)
        {
            using (var op = Scaffold.Begin("Test prefab " + name))
            {
                var prefab = op.FindOrCreatePrefab($"{Folder}/{name}.prefab", name, types);

                Assert.IsNotNull(prefab, string.Join("\n", op.Result.Problems));

                return prefab;
            }
        }

        /// <summary>
        /// The children of a dropdown item: childList since Unity 6.x deprecated children (an error by 6000.6), found by
        /// name so the tests compile on every version.
        /// </summary>
        public static UnityEditor.IMGUI.Controls.AdvancedDropdownItem[] Children(UnityEditor.IMGUI.Controls.AdvancedDropdownItem item)
        {
            var type = typeof(UnityEditor.IMGUI.Controls.AdvancedDropdownItem);

            var property = type.GetProperty("childList") ?? type.GetProperty("children");

            return ((System.Collections.IEnumerable)property.GetValue(item)).Cast<UnityEditor.IMGUI.Controls.AdvancedDropdownItem>().ToArray();
        }

        public static void Close(Scene scene)
        {
            if (scene.IsValid() && scene.isLoaded && SceneManager.sceneCount > 1)
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        public void Dispose()
        {
            foreach (var path in _levels)
            {
                Close(SceneManager.GetSceneByPath(path));
            }

            UEArchitectureEditorSettings.instance.Swap_Internal(_previousConventions);

            FrameworkIndex.Invalidate();

            var settings = FrameworkIndex.Settings;

            if (!_hadSettings && settings != null)
            {
                AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(settings));

                if (!_hadResources && AssetDatabase.IsValidFolder(SettingsFolder) && !AssetDatabase.FindAssets(string.Empty, new[] { SettingsFolder }).Any())
                {
                    AssetDatabase.DeleteAsset(SettingsFolder);
                }
            }
            else if (_hadSettings && _settings != null)
            {
                var serialized = new SerializedObject(_settings);

                serialized.FindProperty("defaultGameMode").objectReferenceValue = _defaultGameMode;

                serialized.FindProperty("gameInstance").objectReferenceValue = _gameInstance;

                serialized.FindProperty("loadingScreen").objectReferenceValue = _loadingScreen;

                var tags = serialized.FindProperty("gameplayTags");

                tags.arraySize = _gameplayTags.Length;

                for (var i = 0; i < _gameplayTags.Length; i++)
                {
                    tags.GetArrayElementAtIndex(i).stringValue = _gameplayTags[i];
                }

                serialized.ApplyModifiedPropertiesWithoutUndo();

                AssetDatabase.SaveAssetIfDirty(_settings);
            }

            AssetDatabase.DeleteAsset(Folder);

            Undo.ClearAll();

            FrameworkIndex.Invalidate();
        }
    }
}
