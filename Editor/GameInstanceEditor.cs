using System.IO;
using UnityEditor;
using System.Linq;
using System.Reflection;

namespace UEArchitecture.Editor
{
    [CustomEditor(typeof(GameInstanceBase), true)]
    public class GameInstanceEditor : UnityEditor.Editor
    {
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance;

        private PropertyInfo _selectedSceneProperty;

        private string[] _sceneNames;

        private int _selectedIndex;

        private void OnEnable()
        {
            _selectedSceneProperty = typeof(GameInstanceBase).GetProperty("SelectedScene", Flags);
            
            LoadScenes();
        }

        private void LoadScenes()
        {
            var guids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" });
            var gameInstanceScenePath = ((GameInstanceBase)target).gameObject.scene.path;
            var gameInstanceSceneName = Path.GetFileNameWithoutExtension(gameInstanceScenePath);

            _sceneNames = guids
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => !Path.GetDirectoryName(path)!.Contains("~")) // Exclude '~' subfolders
                .Select(Path.GetFileNameWithoutExtension)
                .Where(scene => scene != gameInstanceSceneName) // Exclude current scene
                .ToArray();
            
            // _sceneNames = guids
            //     .Select(AssetDatabase.GUIDToAssetPath)
            //     .Select(Path.GetFileNameWithoutExtension)
            //     .Where(scene => scene != gameInstanceSceneName) // Exclude the current scene
            //     .ToArray();

            _selectedIndex = System.Array.IndexOf(_sceneNames, ((GameInstanceBase)target).SelectedScene);

            if (_selectedIndex == -1)
            {
                _selectedIndex = 0;
                _selectedSceneProperty.SetValue(target, _sceneNames[_selectedIndex]);
            }
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            if (_sceneNames == null || _sceneNames.Length == 0)
            {
                EditorGUILayout.HelpBox("No valid scenes found in 'Assets/Scenes'.", MessageType.Warning);
                return;
            }

            EditorGUI.BeginChangeCheck();

            _selectedIndex = EditorGUILayout.Popup("Select Scene", _selectedIndex, _sceneNames);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(target, "Change Selected Scene");

                _selectedSceneProperty.SetValue(target, _sceneNames[_selectedIndex]);
                
                EditorUtility.SetDirty(target);
            }
        }
    }
}