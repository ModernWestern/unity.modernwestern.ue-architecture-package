using System.Linq;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// The tags offered in tag fields: the ones in Project Settings > UE Architecture, with their parents.
    /// </summary>
    internal static class ProjectTags
    {
        public static string[] All()
        {
            var settings = UEArchitectureSettingsProvider.FindAll().FirstOrDefault();

            var names = new SortedSet<string>(System.StringComparer.Ordinal);

            if (settings == null)
            {
                return names.ToArray();
            }

            foreach (var tag in settings.GameplayTags)
            {
                if (!GameplayTagRegistry.IsValidName(tag))
                {
                    continue;
                }

                // "Status.Burning" also offers "Status".
                for (var dot = tag.IndexOf('.'); dot > 0; dot = tag.IndexOf('.', dot + 1))
                {
                    names.Add(tag.Substring(0, dot));
                }

                names.Add(tag);
            }

            return names.ToArray();
        }

        public static bool IsKnown(string tag)
        {
            return string.IsNullOrEmpty(tag) || All().Contains(tag);
        }

        /// <summary>
        /// A menu of every project tag, nested by dots ("Status/Burning"). A tag with children appears inside its own submenu.
        /// </summary>
        public static void ShowMenu(string current, System.Action<string> pick, ICollection<string> exclude = null)
        {
            var tags = All();

            var menu = new GenericMenu();

            if (exclude == null)
            {
                menu.AddItem(new GUIContent("None"), string.IsNullOrEmpty(current), () => pick(string.Empty));

                menu.AddSeparator(string.Empty);
            }

            foreach (var tag in tags)
            {
                if (exclude != null && exclude.Contains(tag))
                {
                    continue;
                }

                var hasChildren = tags.Any(other => other.StartsWith(tag + ".", System.StringComparison.Ordinal));

                var path = tag.Replace('.', '/') + (hasChildren ? "/" + tag.Substring(tag.LastIndexOf('.') + 1) : string.Empty);

                menu.AddItem(new GUIContent(path), tag == current, () => pick(tag));
            }

            if (tags.Length == 0)
            {
                menu.AddDisabledItem(new GUIContent("No tags yet"));
            }

            menu.AddSeparator(string.Empty);

            menu.AddItem(new GUIContent("Edit tags in Project Settings..."), false, () => SettingsService.OpenProjectSettings(UEArchitectureSettingsProvider.SettingsPath));

            menu.ShowAsContext();
        }
    }

    [CustomPropertyDrawer(typeof(GameplayTag))]
    internal sealed class GameplayTagDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var name = property.FindPropertyRelative("name");

            EditorGUI.BeginProperty(position, label, property);

            var field = EditorGUI.PrefixLabel(position, label);

            var current = name.stringValue;

            var known = ProjectTags.IsKnown(current);

            var text = string.IsNullOrEmpty(current) ? "None" : known ? current : current + "  (not in Project Settings)";

            var previous = GUI.color;

            if (!known)
            {
                GUI.color = UEArchitectureStyles.Warning;
            }

            if (EditorGUI.DropdownButton(field, new GUIContent(text), FocusType.Keyboard))
            {
                var target = property.serializedObject;

                var path = name.propertyPath;

                ProjectTags.ShowMenu(current, picked =>
                {
                    target.Update();

                    target.FindProperty(path).stringValue = picked;

                    target.ApplyModifiedProperties();
                });
            }

            GUI.color = previous;

            EditorGUI.EndProperty();
        }
    }

    [CustomPropertyDrawer(typeof(GameplayTagContainer))]
    internal sealed class GameplayTagContainerDrawer : PropertyDrawer
    {
        private const float Spacing = 2f;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var tags = property.FindPropertyRelative("tags");

            var lines = 1 + (tags != null ? tags.arraySize : 0);

            return lines * (EditorGUIUtility.singleLineHeight + Spacing);
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var tags = property.FindPropertyRelative("tags");

            var line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);

            EditorGUI.BeginProperty(position, label, property);

            var field = EditorGUI.PrefixLabel(line, label);

            if (EditorGUI.DropdownButton(field, new GUIContent(tags.arraySize == 0 ? "Add tag" : "Add tag   (" + tags.arraySize + ")"), FocusType.Keyboard))
            {
                var target = property.serializedObject;

                var path = tags.propertyPath;

                var existing = Enumerable.Range(0, tags.arraySize).Select(i => tags.GetArrayElementAtIndex(i).stringValue).ToList();

                ProjectTags.ShowMenu(null, picked =>
                {
                    target.Update();

                    var list = target.FindProperty(path);

                    list.arraySize++;

                    list.GetArrayElementAtIndex(list.arraySize - 1).stringValue = picked;

                    target.ApplyModifiedProperties();
                }, existing);
            }

            var indent = EditorGUIUtility.labelWidth;

            for (var i = 0; i < tags.arraySize; i++)
            {
                line.y += EditorGUIUtility.singleLineHeight + Spacing;

                var tag = tags.GetArrayElementAtIndex(i).stringValue;

                var tagRect = new Rect(line.x + indent, line.y, line.width - indent - 24f, line.height);

                var known = ProjectTags.IsKnown(tag);

                EditorGUI.LabelField(tagRect, known ? tag : tag + "  (not in Project Settings)", known ? EditorStyles.label : UEArchitectureStyles.MutedLabel);

                if (GUI.Button(new Rect(line.xMax - 20f, line.y, 20f, line.height), "×", EditorStyles.miniButton))
                {
                    tags.DeleteArrayElementAtIndex(i);

                    break;
                }
            }

            EditorGUI.EndProperty();
        }
    }
}
