using System.Linq;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using Object = UnityEngine.Object;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// "Scene references: 9 empty of 15" under a component placed in a level (the GameMode today; reusable by any
    /// inspector): each empty Component or GameObject field gets [Find] (one match is assigned at once, several open a
    /// list), and [Fill N from this scene] assigns every field exactly one object fits, in one Undo step. Fields that are
    /// set are never overwritten; fields with several matches are highlighted.
    /// </summary>
    internal static class SceneReferencesSection
    {
        private const int MaxMenuItems = 40;

        private static readonly Dictionary<Object, HashSet<string>> _ambiguous = new();

        private static readonly GUIContent _findContent = new("Find", "Look in this level for objects that fit this field.");

        /// <summary>
        /// Draws the section for the inspected component; nothing for prefab assets, several objects or a component without
        /// level references.
        /// </summary>
        public static void Draw(SerializedObject serialized)
        {
            if (serialized == null || serialized.isEditingMultipleObjects || !(serialized.targetObject is Component owner))
            {
                return;
            }

            var level = SceneReferenceFill.LevelOf(owner);

            if (!level.IsValid())
            {
                return;
            }

            var slots = SceneReferenceFill.Slots(serialized);

            if (slots.Count == 0)
            {
                return;
            }

            var empty = slots.Where(slot => slot.IsEmpty).ToList();

            UEArchitectureStyles.Section($"Scene references: {empty.Count} empty of {slots.Count}");

            if (empty.Count == 0)
            {
                EditorGUILayout.LabelField("Every reference to an object of this level is set.", UEArchitectureStyles.MutedLabel);

                return;
            }

            _ambiguous.TryGetValue(owner, out var ambiguous);

            // Play mode throws away changes to the level when it stops: Find and Fill wait for Edit mode.
            using var locked = new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode);

            foreach (var slot in empty)
            {
                var several = ambiguous != null && ambiguous.Contains(slot.PropertyPath);

                using (new EditorGUILayout.HorizontalScope())
                {
                    var previous = GUI.contentColor;

                    if (several)
                    {
                        GUI.contentColor = UEArchitectureStyles.Warning;
                    }

                    EditorGUILayout.LabelField(new GUIContent(slot.Label, $"{slot.FieldType.Name} field \"{slot.PropertyPath}\""),
                                               new GUIContent(several ? "several match: pick one" : $"empty ({slot.FieldType.Name})"),
                                               several ? EditorStyles.label : UEArchitectureStyles.MutedLabel);

                    GUI.contentColor = previous;

                    if (GUILayout.Button(_findContent, EditorStyles.miniPullDown, GUILayout.Width(56f)))
                    {
                        Find_Internal(owner, slot, level);

                        GUIUtility.ExitGUI();
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                var command = CliCommand(owner, out var why);

                using (new EditorGUI.DisabledScope(command == null))
                {
                    if (UEArchitectureStyles.SmallButton(new GUIContent("Copy CLI command", command != null ? "Copy the arguments that fill these references in batch mode: put the Unity Editor's path before them." : why)))
                    {
                        EditorGUIUtility.systemCopyBuffer = command;
                    }
                }

                GUILayout.FlexibleSpace();

                var label = new GUIContent($"Fill {empty.Count} from this scene",
                                           "Assign every empty field that exactly one object of this level fits (by type, or by its name). Set fields are never changed; " +
                                           "fields with several matches are highlighted for you to pick.");

                if (UEArchitectureStyles.AccentButton(label, GUILayout.Width(170f)))
                {
                    Fill_Internal(owner);

                    GUIUtility.ExitGUI();
                }
            }
        }

        /// <summary>
        /// The "-uea fill-scene-references" arguments for this component, or null with why not: a level that was never
        /// saved has no address, and the command line fills components placed in levels, not the copy of a prefab open in
        /// Prefab Mode (it would find the prefab file, which cannot point to objects of a level).
        /// </summary>
        internal static string CliCommand(Component owner, out string why)
        {
            why = string.Empty;

            if (owner == null)
            {
                why = "There is no component.";

                return null;
            }

            if (UnityEditor.SceneManagement.PrefabStageUtility.GetPrefabStage(owner.gameObject) != null || UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(owner.gameObject.scene))
            {
                why = "The command line fills components placed in levels; fill this prefab here.";

                return null;
            }

            var address = UEArchitectureCli.Address(owner);

            if (address.Length == 0)
            {
                why = "Save the level first: the command line finds the component in its saved file.";

                return null;
            }

            return UEArchitectureCli.CommandLine("fill-scene-references", new KeyValuePair<string, string>("target", address),
                                                 new KeyValuePair<string, string>("component", owner.GetType().FullName));
        }

        private static void Fill_Internal(Component owner)
        {
            List<FillRow> rows = null;

            ClassField.Run_Internal(() =>
            {
                using (var op = Scaffold.Begin($"Fill the level references of {owner.name}", "fill-scene-references"))
                {
                    rows = UEArchitectureActions.FillSceneReferences_Internal(op, owner, true);

                    Debug.Log("UE Architecture: " + op.Result.Message);

                    return op.Result;
                }
            });

            _ambiguous[owner] = new HashSet<string>((rows ?? new List<FillRow>()).Where(row => row.Outcome == FillOutcome.Ambiguous).Select(row => row.Slot.PropertyPath));
        }

        private static void Find_Internal(Component owner, ReferenceSlot slot, UnityEngine.SceneManagement.Scene level)
        {
            var matches = SceneReferenceFill.Matches(slot, SceneReferenceFill.Candidates(level, owner));

            if (slot.FieldType == typeof(GameObject))
            {
                // Every object is a GameObject: offer the ones whose name is close to the field's first.
                var key = SceneReferenceFill.Normalize(slot.FieldName);

                var close = matches.Where(match => key.Length > 0 && SceneReferenceFill.Normalize(match.Name).Contains(key)).ToList();

                if (close.Count > 0)
                {
                    matches = close;
                }
            }

            var path = slot.PropertyPath;

            if (matches.Count == 1)
            {
                Assign_Internal(owner, path, matches[0].Value);

                return;
            }

            var menu = new GenericMenu();

            if (matches.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent($"Nothing in this level is a {slot.FieldType.Name}"));
            }

            foreach (var match in matches.Take(MaxMenuItems))
            {
                var value = match.Value;

                menu.AddItem(new GUIContent(match.MenuLabel.Replace("/", " > ")), false, () => Assign_Internal(owner, path, value));
            }

            if (matches.Count > MaxMenuItems)
            {
                menu.AddDisabledItem(new GUIContent($"... {matches.Count - MaxMenuItems} more: drag the one you want into the field"));
            }

            menu.ShowAsContext();
        }

        private static void Assign_Internal(Component owner, string path, Object value)
        {
            ClassField.Run_Internal(() => UEArchitectureActions.Assign(owner, path, value));

            if (_ambiguous.TryGetValue(owner, out var ambiguous))
            {
                ambiguous.Remove(path);
            }
        }
    }
}
