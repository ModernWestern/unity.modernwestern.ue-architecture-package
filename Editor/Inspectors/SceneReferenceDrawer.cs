using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// Build Settings helpers: is a scene in the build, and put it there.
    /// </summary>
    internal static class BuildScenes
    {
        public enum Status
        {
            None,
            InBuild,
            Disabled,
            Missing
        }

        public static Status Of(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return Status.None;
            }

            var scene = EditorBuildSettings.scenes.FirstOrDefault(each => each.path == path);

            if (scene == null)
            {
                return Status.Missing;
            }

            return scene.enabled ? Status.InBuild : Status.Disabled;
        }

        /// <summary>
        /// Adds the scene to Build Settings, or enables it if it is there but disabled.
        /// </summary>
        public static void Include(string path)
        {
            var scenes = EditorBuildSettings.scenes.ToList();

            var existing = scenes.FindIndex(each => each.path == path);

            if (existing >= 0)
            {
                scenes[existing] = new EditorBuildSettingsScene(path, true);
            }
            else
            {
                scenes.Add(new EditorBuildSettingsScene(path, true));
            }

            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }

    /// <summary>
    /// Scene picker for <see cref="SceneReference"/>: drag a scene, and fix it in one click if Build Settings lacks it.
    /// </summary>
    [CustomPropertyDrawer(typeof(SceneReference))]
    internal sealed class SceneReferenceDrawer : PropertyDrawer
    {
        private const float NoticeHeight = 30f;

        private const float ThisLevelWidth = 70f;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var line = EditorGUIUtility.singleLineHeight;

            return Problem_Internal(property, out _) == null ? line : line + NoticeHeight + 4f;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var assetProperty = property.FindPropertyRelative("asset");

            var pathProperty = property.FindPropertyRelative("path");

            var lineRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);

            var current = Scene_Internal(assetProperty, pathProperty);

            var ownLevel = OwnLevel_Internal(property);

            var showThisLevel = !string.IsNullOrEmpty(ownLevel) && (current == null || AssetDatabase.GetAssetPath(current) != ownLevel);

            var fieldRect = showThisLevel ? new Rect(lineRect.x, lineRect.y, lineRect.width - ThisLevelWidth - 2f, lineRect.height) : lineRect;

            EditorGUI.BeginProperty(lineRect, label, property);

            EditorGUI.BeginChangeCheck();

            var picked = EditorGUI.ObjectField(fieldRect, label, current, typeof(SceneAsset), false) as SceneAsset;

            if (EditorGUI.EndChangeCheck() || (picked != null && pathProperty.stringValue != AssetDatabase.GetAssetPath(picked)))
            {
                assetProperty.objectReferenceValue = picked;

                pathProperty.stringValue = picked != null ? AssetDatabase.GetAssetPath(picked) : string.Empty;
            }

            if (showThisLevel && GUI.Button(new Rect(lineRect.xMax - ThisLevelWidth, lineRect.y, ThisLevelWidth, lineRect.height),
                                            new GUIContent("This level", "Point to the level this object is in (for example, to reload it)."), EditorStyles.miniButton))
            {
                assetProperty.objectReferenceValue = AssetDatabase.LoadAssetAtPath<SceneAsset>(ownLevel);

                pathProperty.stringValue = ownLevel;
            }

            EditorGUI.EndProperty();

            var problem = Problem_Internal(property, out var fix);

            if (problem == null)
            {
                return;
            }

            var noticeRect = new Rect(position.x + EditorGUIUtility.labelWidth, lineRect.yMax + 2f, position.width - EditorGUIUtility.labelWidth, NoticeHeight);

            var buttonWidth = fix != null ? 120f : 0f;

            EditorGUI.HelpBox(new Rect(noticeRect.x, noticeRect.y, noticeRect.width - buttonWidth - 4f, noticeRect.height), problem, MessageType.Warning);

            if (fix != null && GUI.Button(new Rect(noticeRect.xMax - buttonWidth, noticeRect.y + 5f, buttonWidth, 20f), fix))
            {
                BuildScenes.Include(pathProperty.stringValue);
            }
        }

        /// <summary>
        /// The path of the saved level the inspected object is in, or null (assets, Prefab Mode, unsaved levels, several
        /// objects).
        /// </summary>
        private static string OwnLevel_Internal(SerializedProperty property)
        {
            if (property.serializedObject.isEditingMultipleObjects || !(property.serializedObject.targetObject is Component owner) || EditorUtility.IsPersistent(owner))
            {
                return null;
            }

            var scene = owner.gameObject.scene;

            return scene.IsValid() && !string.IsNullOrEmpty(scene.path) && !EditorSceneManager.IsPreviewScene(scene) ? scene.path : null;
        }

        private static SceneAsset Scene_Internal(SerializedProperty assetProperty, SerializedProperty pathProperty)
        {
            if (assetProperty.objectReferenceValue is SceneAsset asset)
            {
                return asset;
            }

            return string.IsNullOrEmpty(pathProperty.stringValue) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(pathProperty.stringValue);
        }

        private static string Problem_Internal(SerializedProperty property, out string fix)
        {
            fix = null;

            var path = property.FindPropertyRelative("path").stringValue;

            if (property.FindPropertyRelative("asset").objectReferenceValue is SceneAsset asset)
            {
                path = AssetDatabase.GetAssetPath(asset);
            }

            switch (BuildScenes.Of(path))
            {
                case BuildScenes.Status.Missing:
                    fix = "Add to Build";

                    return "Not in Build Settings: it loads in the Editor only.";

                case BuildScenes.Status.Disabled:
                    fix = "Enable in Build";

                    return "Disabled in Build Settings: it loads in the Editor only.";

                default:
                    return null;
            }
        }
    }
}
