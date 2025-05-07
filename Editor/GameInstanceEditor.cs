using System.IO;
using UnityEditor;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;

namespace ModernWestern.UEArchitecture.Editor
{
    [CustomEditor(typeof(GameInstanceBase), true)]
    public class GameInstanceEditor : UnityEditor.Editor
    {
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance;

        private PropertyInfo _selectedSceneProperty;

        private string[] _sceneNames;
        private string[] _sceneDisplayNames;

        private int _selectedIndex;
        private bool _showFullPath;

        private const string FullPathPrefKey = "GameInstanceEditor_ShowFullPath";

        private void OnEnable()
        {
            _selectedSceneProperty = typeof(GameInstanceBase).GetProperty("SelectedScene", Flags);
            
            _showFullPath = EditorPrefs.GetBool(FullPathPrefKey, false);

            LoadScenes();
        }

        private void LoadScenes()
        {
            var guids = AssetDatabase.FindAssets("t:Scene", new[]
            {
                "Assets"
            });

            var gameInstanceScenePath = ((GameInstanceBase)target).gameObject.scene.path;
            
            var gameInstanceSceneName = Path.GetFileNameWithoutExtension(gameInstanceScenePath);

            var scenePaths = guids.Select(AssetDatabase.GUIDToAssetPath).Where(path =>
                                                                                    path.Contains("/Scenes/") &&
                                                                                    !Path.GetDirectoryName(path)!.Contains("~")).ToList();

            _sceneNames = scenePaths.Select(Path.GetFileNameWithoutExtension).Where(sceneName => sceneName != gameInstanceSceneName).ToArray();

            _selectedIndex = System.Array.IndexOf(_sceneNames, ((GameInstanceBase)target).SelectedScene);

            if (_selectedIndex == -1 && _sceneNames.Length > 0)
            {
                _selectedIndex = 0;
                _selectedSceneProperty.SetValue(target, _sceneNames[_selectedIndex]);
            }

            UpdateDisplayNames(scenePaths);
        }

        private void UpdateDisplayNames(List<string> scenePaths)
        {
            var currentSceneName = ((GameInstanceBase)target).gameObject.scene.name;

            _sceneDisplayNames = scenePaths.Where(path => Path.GetFileNameWithoutExtension(path) != currentSceneName).Select(path => 
                                                                                                                             _showFullPath ? path :
                                                                                                                             Path.GetFileNameWithoutExtension(path)).ToArray();
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            if (_sceneNames == null || _sceneNames.Length == 0)
            {
                EditorGUILayout.HelpBox("No valid scenes found in folders named 'Scenes'.", MessageType.Warning);
                
                return;
            }
            
#region Toggle
            
            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            var newShowFullPath = GUILayout.Toggle(_showFullPath, "Full Path", "Button", GUILayout.ExpandWidth(true));
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();

#endregion
            
            if (newShowFullPath != _showFullPath)
            {
                _showFullPath = newShowFullPath;
                
                EditorPrefs.SetBool(FullPathPrefKey, _showFullPath);
                
                LoadScenes();
            }

            EditorGUI.BeginChangeCheck();

            _selectedIndex = EditorGUILayout.Popup("Select Scene", _selectedIndex, _sceneDisplayNames);

            if (!EditorGUI.EndChangeCheck())
            {
                return;
            }
            
            Undo.RecordObject(target, "Change Selected Scene");

            _selectedSceneProperty.SetValue(target, _sceneNames[_selectedIndex]);

            EditorUtility.SetDirty(target);
        }
    }
}
