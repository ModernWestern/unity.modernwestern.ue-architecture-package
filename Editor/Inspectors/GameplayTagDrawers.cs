using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEditor.IMGUI.Controls;
using System.Collections.Generic;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// The tags offered in tag fields: the ones in Project Settings > UE Architecture, with their parents.
    /// </summary>
    internal static class ProjectTags
    {
        /// <summary>
        /// The project's tags with their parents, cached (see <see cref="FrameworkIndex.Tags"/>).
        /// </summary>
        public static string[] All()
        {
            return FrameworkIndex.Tags;
        }

        /// <summary>
        /// The valid tags of a list plus their parents, sorted: "Status.Burning" also gives "Status".
        /// </summary>
        public static string[] Build(IEnumerable<string> gameplayTags)
        {
            var names = new SortedSet<string>(System.StringComparer.Ordinal);

            if (gameplayTags == null)
            {
                return names.ToArray();
            }

            foreach (var tag in gameplayTags)
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
            return FrameworkIndex.IsKnownTag(tag);
        }
    }

    /// <summary>
    /// A GameplayTag field: a searchable picker (with "New tag…"), and a notice with [Add to project] [Pick] when the
    /// value is not a project tag.
    /// </summary>
    [CustomPropertyDrawer(typeof(GameplayTag))]
    internal sealed class GameplayTagDrawer : PropertyDrawer
    {
        private const float NoticeHeight = 38f;

        private static readonly Dictionary<string, AdvancedDropdownState> _states = new();

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var name = property.FindPropertyRelative("name");

            var line = EditorGUIUtility.singleLineHeight;

            return name == null || name.hasMultipleDifferentValues || ProjectTags.IsKnown(name.stringValue) ? line : line + NoticeHeight + 2f;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var name = property.FindPropertyRelative("name");

            var line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);

            EditorGUI.BeginProperty(position, label, property);

            var field = EditorGUI.PrefixLabel(line, label);

            var current = name.stringValue;

            var known = name.hasMultipleDifferentValues || ProjectTags.IsKnown(current);

            var text = name.hasMultipleDifferentValues ? "—" : string.IsNullOrEmpty(current) ? "None" : current;

            var previous = GUI.color;

            if (!known)
            {
                GUI.color = UEArchitectureStyles.Warning;
            }

            if (EditorGUI.DropdownButton(field, new GUIContent(text, known ? current : $"\"{current}\" is not a project tag."), FocusType.Keyboard))
            {
                ShowPicker_Internal(field, property.serializedObject, name.propertyPath, current);
            }

            GUI.color = previous;

            // "New tag…" picked in the dropdown opens here, under the field, once the dropdown is gone.
            GameplayTagPopups.ShowRequested(field, property.serializedObject.targetObject, name.propertyPath);

            if (!known)
            {
                DrawNotice_Internal(new Rect(field.x, line.yMax + 2f, field.width, NoticeHeight), property.serializedObject, name.propertyPath, current);
            }

            EditorGUI.EndProperty();
        }

        private static void DrawNotice_Internal(Rect rect, SerializedObject target, string path, string current)
        {
            const float buttons = 168f;

            var valid = GameplayTagRegistry.IsValidName(current);

            var message = valid ? $"\"{current}\" is not a project tag." : $"\"{current}\" is not a valid tag name.";

            EditorGUI.HelpBox(new Rect(rect.x, rect.y, rect.width - buttons - 4f, rect.height), message, MessageType.Warning);

            var addRect = new Rect(rect.xMax - buttons, rect.y + (rect.height - EditorGUIUtility.singleLineHeight) * 0.5f, 100f, EditorGUIUtility.singleLineHeight);

            using (new EditorGUI.DisabledScope(!valid))
            {
                if (GUI.Button(addRect, new GUIContent("Add to project", "Add it to Project Settings > UE Architecture > Gameplay Tags."), EditorStyles.miniButton))
                {
                    var result = UEArchitectureActions.AddTag(current);

                    if (!result.Ok)
                    {
                        Debug.LogWarning("[UEArchitecture] " + string.Join("\n", result.Problems));
                    }

                    GUIUtility.ExitGUI();
                }
            }

            var pickRect = new Rect(addRect.xMax + 4f, addRect.y, buttons - addRect.width - 4f, addRect.height);

            if (EditorGUI.DropdownButton(pickRect, new GUIContent("Pick"), FocusType.Passive, EditorStyles.miniPullDown))
            {
                ShowPicker_Internal(pickRect, target, path, current);
            }
        }

        private static void ShowPicker_Internal(Rect anchor, SerializedObject target, string path, string current)
        {
            var key = path + "|" + target.targetObject.GetType().FullName;

            if (!_states.TryGetValue(key, out var state))
            {
                state = new AdvancedDropdownState();

                _states[key] = state;
            }

            void Set(string value)
            {
                if (target.targetObject == null)
                {
                    return;
                }

                target.Update();

                target.FindProperty(path).stringValue = value;

                target.ApplyModifiedProperties();
            }

            // "New tag…" starts from the unknown value, or under the current tag ("Status." for a sub-tag).
            var prefill = GameplayTagRegistry.IsValidName(current) && !ProjectTags.IsKnown(current) ? current : string.IsNullOrEmpty(current) ? string.Empty : current + ".";

            new GameplayTagDropdown(state, Set, () => GameplayTagPopups.RequestNewTag(target.targetObject, path, prefill, Set)).Show(anchor);
        }
    }

    /// <summary>
    /// A GameplayTagContainer field: its tags one per line, and a checkbox tree (with "New tag…") to change them. Tags
    /// that are not project tags offer [Add].
    /// </summary>
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

            var title = tags.arraySize == 0 ? "Add tags" : tags.arraySize == 1 ? "1 tag" : tags.arraySize + " tags";

            if (EditorGUI.DropdownButton(field, new GUIContent(title, "Tick the tags it holds."), FocusType.Keyboard))
            {
                ShowChecklist_Internal(field, property.serializedObject, tags.propertyPath);
            }

            // "New tag…" pressed in the checklist opens here, under the field, once the checklist is closed.
            GameplayTagPopups.ShowRequested(field, property.serializedObject.targetObject, tags.propertyPath);

            var indent = EditorGUIUtility.labelWidth;

            for (var i = 0; i < tags.arraySize; i++)
            {
                line.y += EditorGUIUtility.singleLineHeight + Spacing;

                var tag = tags.GetArrayElementAtIndex(i).stringValue;

                var known = ProjectTags.IsKnown(tag);

                var addWidth = known || !GameplayTagRegistry.IsValidName(tag) ? 0f : 44f;

                var tagRect = new Rect(line.x + indent, line.y, line.width - indent - 24f - addWidth, line.height);

                EditorGUI.LabelField(tagRect, known ? tag : tag + "  (not a project tag)", known ? EditorStyles.label : UEArchitectureStyles.MutedLabel);

                if (addWidth > 0f && GUI.Button(new Rect(tagRect.xMax, line.y, addWidth - 4f, line.height), new GUIContent("Add", "Add it to the project tags."), EditorStyles.miniButton))
                {
                    UEArchitectureActions.AddTag(tag);

                    GUIUtility.ExitGUI();
                }

                if (GUI.Button(new Rect(line.xMax - 20f, line.y, 20f, line.height), new GUIContent("×", "Remove it from this container."), EditorStyles.miniButton))
                {
                    tags.DeleteArrayElementAtIndex(i);

                    break;
                }
            }

            EditorGUI.EndProperty();
        }

        private static void ShowChecklist_Internal(Rect anchor, SerializedObject target, string path)
        {
            IReadOnlyList<string> Held()
            {
                if (target.targetObject == null)
                {
                    return Array.Empty<string>();
                }

                target.Update();

                var list = target.FindProperty(path);

                return Enumerable.Range(0, list.arraySize).Select(i => list.GetArrayElementAtIndex(i).stringValue).ToList();
            }

            void Toggle(string tag, bool on)
            {
                if (target.targetObject == null)
                {
                    return;
                }

                target.Update();

                var list = target.FindProperty(path);

                var at = -1;

                for (var i = 0; i < list.arraySize && at < 0; i++)
                {
                    if (list.GetArrayElementAtIndex(i).stringValue == tag)
                    {
                        at = i;
                    }
                }

                if (on && at < 0)
                {
                    list.arraySize++;

                    list.GetArrayElementAtIndex(list.arraySize - 1).stringValue = tag;
                }
                else if (!on && at >= 0)
                {
                    list.DeleteArrayElementAtIndex(at);
                }

                target.ApplyModifiedProperties();
            }

            PopupWindow.Show(anchor, new GameplayTagChecklist(Held, Toggle, () => GameplayTagPopups.RequestNewTag(target.targetObject, path, string.Empty, tag => Toggle(tag, true))));
        }
    }
}
