using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// A small name field that opens under a button, with a live preview of what will be written ("Writes ArenaPawn.cs in
    /// Assets/Scripts"). Enter creates, Esc cancels. It never uses the Project window's rename, so it works with the
    /// Project window closed and on every Unity version.
    /// </summary>
    internal sealed class NamePopup : PopupWindowContent
    {
        private const string FieldName = "UEArchitecture.NamePopup.Name";

        private const float Width = 400f;

        private readonly string _title;

        private readonly string _createLabel;

        private readonly Func<string, NamePreview> _preview;

        private readonly Action<string> _create;

        private readonly Func<string, string> _command;

        private string _name;

        private string _previewedName;

        private NamePreview _current;

        private bool _focused;

        /// <summary>
        /// A popup that creates with <paramref name="create"/>; <paramref name="command"/> (optional) gives the command
        /// line that does the same, for "Copy CLI command".
        /// </summary>
        public NamePopup(string title, string initialName, Func<string, NamePreview> preview, Action<string> create, string createLabel = "Create",
                         Func<string, string> command = null)
        {
            _title = title;

            _name = initialName ?? string.Empty;

            _preview = preview;

            _create = create;

            _createLabel = createLabel;

            _command = command;
        }

        /// <summary>
        /// A popup for a new script deriving from <paramref name="parent"/>, previewed with <see cref="ScriptWriter.Plan(IEnumerable{ScriptRequest})"/>.
        /// </summary>
        public static NamePopup ForScript(string title, string initialName, Type parent, string folder, bool withPrefab, Action<string> create, Func<string, string> command = null)
        {
            return new NamePopup(title, initialName, name => ScriptPreview(name, parent, folder, withPrefab), create, "Create", command);
        }

        /// <summary>
        /// The command line for the name typed now, or empty when the popup has none.
        /// </summary>
        public string CommandFor(string name)
        {
            return _command != null ? _command(name) ?? string.Empty : string.Empty;
        }

        /// <summary>
        /// "Writes ArenaPawn.cs in Assets/Scripts, then ArenaPawn.prefab in Assets/Prefabs/Gameplay after the compile", or
        /// what is wrong with the name.
        /// </summary>
        public static NamePreview ScriptPreview(string name, Type parent, string folder, bool withPrefab)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return new NamePreview("Type a class name.", new[] { "The name is empty." });
            }

            var plan = ScriptWriter.Plan(new[] { new ScriptRequest(name, parent, folder) });

            if (!plan.Ok)
            {
                return new NamePreview(plan.Scripts.Count > 0 ? plan.Describe() : string.Empty, plan.Problems.ToArray());
            }

            var text = plan.Describe();

            if (withPrefab && parent != null && typeof(Component).IsAssignableFrom(parent))
            {
                var prefabPath = GeneratorNaming.PrefabPath(plan.Scripts[0].ClassName, parent, UEArchitectureEditorSettings.Current);

                text += $", then {System.IO.Path.GetFileName(prefabPath)} in {System.IO.Path.GetDirectoryName(prefabPath)?.Replace('\\', '/')} after the compile";
            }

            return new NamePreview(text + ".", Array.Empty<string>());
        }

        public override Vector2 GetWindowSize()
        {
            var current = Current_Internal();

            var lines = 1 + current.Problems.Count;

            return new Vector2(Width, 92f + lines * 30f);
        }

        public override void OnGUI(Rect rect)
        {
            var current = Current_Internal();

            var evt = Event.current;

            if (evt.type == EventType.KeyDown)
            {
                if (evt.keyCode == KeyCode.Escape)
                {
                    evt.Use();

                    editorWindow.Close();

                    return;
                }

                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    evt.Use();

                    TryCreate_Internal(current);

                    return;
                }
            }

            GUILayout.Space(4f);

            EditorGUILayout.LabelField(_title, EditorStyles.boldLabel);

            GUI.SetNextControlName(FieldName);

            _name = EditorGUILayout.TextField(_name);

            if (!_focused)
            {
                EditorGUI.FocusTextInControl(FieldName);

                _focused = true;
            }

            current = Current_Internal();

            EditorGUILayout.LabelField(current.Text, UEArchitectureStyles.MutedLabel);

            var previous = GUI.contentColor;

            GUI.contentColor = UEArchitectureStyles.Error;

            foreach (var problem in current.Problems)
            {
                EditorGUILayout.LabelField(problem, UEArchitectureStyles.Wrapped);
            }

            GUI.contentColor = previous;

            GUILayout.FlexibleSpace();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (_command != null)
                {
                    using (new EditorGUI.DisabledScope(!current.CanCreate))
                    {
                        if (GUILayout.Button(new GUIContent("Copy CLI command", "Copy the arguments that do the same in batch mode (for scripts and agents): put the Unity Editor's path before them."), EditorStyles.miniButton,
                                             GUILayout.Width(120f)))
                        {
                            EditorGUIUtility.systemCopyBuffer = CommandFor(_name);

                            editorWindow.ShowNotification(new GUIContent("Copied"));
                        }
                    }
                }

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Cancel", GUILayout.Width(80f)))
                {
                    editorWindow.Close();

                    GUIUtility.ExitGUI();
                }

                using (new EditorGUI.DisabledScope(!current.CanCreate))
                {
                    if (GUILayout.Button(new GUIContent(_createLabel, "Enter"), GUILayout.Width(90f)))
                    {
                        TryCreate_Internal(current);

                        GUIUtility.ExitGUI();
                    }
                }
            }
        }

        private NamePreview Current_Internal()
        {
            if (_previewedName == _name && _current.Text != null)
            {
                return _current;
            }

            _previewedName = _name;

            _current = _preview != null ? _preview(_name) : new NamePreview(string.Empty, Array.Empty<string>());

            return _current;
        }

        private void TryCreate_Internal(NamePreview current)
        {
            if (!current.CanCreate)
            {
                return;
            }

            var name = _name;

            editorWindow.Close();

            _create?.Invoke(name);
        }
    }

    /// <summary>
    /// What a <see cref="NamePopup"/> shows under the field: what will be written, and why it cannot be yet.
    /// </summary>
    internal readonly struct NamePreview
    {
        public readonly string Text;

        public readonly IReadOnlyList<string> Problems;

        public NamePreview(string text, IReadOnlyList<string> problems)
        {
            Text = text ?? string.Empty;

            Problems = problems ?? Array.Empty<string>();
        }

        public bool CanCreate => Problems.Count == 0;
    }
}
