using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEditorInternal;
using System.Collections.Generic;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// The project's tags as a tree view (through <see cref="CompatTreeView"/>, so it compiles on every Unity version).
    /// Row ids come from the tag names, so what is expanded stays expanded when tags change.
    /// </summary>
    internal sealed class GameplayTagTreeView : CompatTreeView
    {
        private readonly Dictionary<int, string> _tags = new();

        private List<GameplayTagNode> _rows = new();

        /// <summary>
        /// Runs on a right click on a row, with its tag.
        /// </summary>
        public Action<string> TagContextClicked;

        public void SetRows(List<GameplayTagNode> rows, bool expandAll)
        {
            _rows = rows ?? new List<GameplayTagNode>();

            Reload();

            if (expandAll)
            {
                ExpandAll();
            }
        }

        /// <summary>
        /// The selected tag, or null.
        /// </summary>
        public string SelectedTag
        {
            get
            {
                var selected = SelectedIds;

                return selected.Count > 0 && _tags.TryGetValue(selected[0], out var tag) ? tag : null;
            }
        }

        public void Select(string tag)
        {
            SetSelection(string.IsNullOrEmpty(tag) ? new List<int>() : new List<int> { IdOf(tag) });

            if (!string.IsNullOrEmpty(tag))
            {
                for (var parent = GameplayTagTree.ParentOf(tag); parent.Length > 0; parent = GameplayTagTree.ParentOf(parent))
                {
                    SetExpanded(IdOf(parent), true);
                }
            }
        }

        public static int IdOf(string tag)
        {
            // Stable across rebuilds and never 0 (the hidden root).
            return (StringComparer.Ordinal.GetHashCode(tag ?? string.Empty) & 0x7FFFFFFE) + 1;
        }

        protected override void AddRows(Action<int, int, string> add)
        {
            _tags.Clear();

            foreach (var node in _rows)
            {
                var id = IdOf(node.Name);

                _tags[id] = node.Name;

                add(id, node.Depth, node.Leaf);
            }
        }

        protected override void ContextClickedItem(int id)
        {
            if (_tags.TryGetValue(id, out var tag))
            {
                TagContextClicked?.Invoke(tag);
            }
        }
    }

    /// <summary>
    /// The tag manager of Project Settings > UE Architecture and Window > UE Architecture > Gameplay Tags: a searchable
    /// tree with Add, Add sub-tag (inline), Rename… (with what it updates), Delete… (with how often it is used) and
    /// Generate C# constants. Every button calls a <see cref="UEArchitectureActions"/> method.
    /// </summary>
    internal sealed class GameplayTagManager
    {
        private const string NewTagField = "UEArchitecture.GameplayTagManager.NewTag";

        private GameplayTagTreeView _tree;

        private string[] _builtFrom;

        private string _builtSearch;

        private string _search = string.Empty;

        private string _newTag;

        private bool _focusNewTag;

        private bool _showList;

        private string _renameRequested;

        private TagUsageReport _renameUsages;

        private string _lastMessage = string.Empty;

        private MessageType _lastType = MessageType.None;

        /// <summary>
        /// Draws the manager. <paramref name="serialized"/> (the settings asset, or the page's stand-in) is used for "Edit
        /// as a list"; <paramref name="externalSearch"/> is the settings page's search, which filters the tree.
        /// </summary>
        public void Draw(SerializedObject serialized, string externalSearch = null)
        {
            var settings = FrameworkIndex.Settings;

            var declared = settings != null ? settings.GameplayTags : Array.Empty<string>();

            _tree ??= new GameplayTagTreeView { TagContextClicked = ShowContextMenu_Internal };

            var search = string.IsNullOrWhiteSpace(externalSearch) ? _search : externalSearch.Trim();

            var all = FrameworkIndex.Tags;

            if (!ReferenceEquals(all, _builtFrom) || search != _builtSearch)
            {
                var first = _builtFrom == null;

                _tree.SetRows(GameplayTagTree.Filter(GameplayTagTree.Build(declared), search), first || search.Length > 0);

                _builtFrom = all;

                _builtSearch = search;
            }

            var selected = _tree.SelectedTag;

            // Rename… opens here, under the Rename… button (popups need a GUI rect), on the repaint after its usages were
            // found: the search runs on the click or the menu item, with a progress bar, never on a repaint.
            var renameNow = _renameRequested;

            var renameUsages = _renameUsages;

            _renameRequested = null;

            _renameUsages = null;

            // The search gets its own row, so the buttons fit in a narrow window.
            if (string.IsNullOrWhiteSpace(externalSearch))
            {
                _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField);
            }
            else
            {
                EditorGUILayout.LabelField($"Showing tags that contain \"{externalSearch.Trim()}\".", UEArchitectureStyles.MutedLabel);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();

                if (GUILayout.Button(new GUIContent("Add", "Add a top-level tag."), EditorStyles.miniButtonLeft, GUILayout.Width(44f)))
                {
                    StartAdding_Internal(string.Empty);
                }

                using (new EditorGUI.DisabledScope(selected == null && renameNow == null))
                {
                    if (GUILayout.Button(new GUIContent("Add sub-tag", "Add a tag below the selected one."), EditorStyles.miniButtonMid, GUILayout.Width(80f)))
                    {
                        StartAdding_Internal(selected + ".");
                    }

                    var renameRect = GUILayoutUtility.GetRect(new GUIContent("Rename…"), EditorStyles.miniButtonMid, GUILayout.Width(64f));

                    if (GUI.Button(renameRect, new GUIContent("Rename…", "Rename it and the tags below it, in Project Settings and in the tag fields of prefabs and assets."), EditorStyles.miniButtonMid))
                    {
                        RequestRename_Internal(selected);

                        GUIUtility.ExitGUI();
                    }
                    else if (renameNow != null && Event.current.type == EventType.Repaint)
                    {
                        GameplayTagPopups.ShowRename(renameRect, renameNow, renameUsages, renamed => _tree.Select(renamed));
                    }
                    else if (renameNow != null)
                    {
                        _renameRequested = renameNow;

                        _renameUsages = renameUsages;
                    }

                    if (GUILayout.Button(new GUIContent("Delete…", "Delete it and the tags below it (asks first, with how often it is used)."), EditorStyles.miniButtonRight, GUILayout.Width(60f)))
                    {
                        // The search and the dialog run on the click; the rest of this event is skipped.
                        GameplayTagPopups.ConfirmDelete(selected);

                        GUIUtility.ExitGUI();
                    }
                }
            }

            DrawAdding_Internal(declared);

            var rows = Mathf.Max(1, _tree.RowCount);

            var height = Mathf.Clamp(rows * EditorGUIUtility.singleLineHeight + 6f, 48f, 320f);

            var treeRect = GUILayoutUtility.GetRect(0f, 10000f, height, height);

            if (all.Length == 0)
            {
                EditorGUI.HelpBox(treeRect, "No tags yet. Press Add, or pick \"New tag…\" in any tag field.", MessageType.None);
            }
            else if (_tree.RowCount == 0)
            {
                EditorGUI.HelpBox(treeRect, "No tag matches the search.", MessageType.None);
            }
            else
            {
                _tree.OnGUI(treeRect);
            }

            foreach (var invalid in GameplayTagTree.Invalid(declared))
            {
                EditorGUILayout.HelpBox($"\"{invalid}\" is not a valid tag: use letters, digits and underscores separated by dots. Fix it in the list below.", MessageType.Warning);
            }

            DrawConstants_Internal();

            if (serialized != null)
            {
                _showList = EditorGUILayout.Foldout(_showList, new GUIContent("Edit as a list", "The raw list of Project Settings: one tag per entry. Parents come by themselves."), true);

                if (_showList)
                {
                    EditorGUILayout.PropertyField(serialized.FindProperty("gameplayTags"), new GUIContent("Tags"), true);
                }
            }
        }

        private void StartAdding_Internal(string prefix)
        {
            _newTag = prefix;

            _focusNewTag = true;
        }

        private void DrawAdding_Internal(IReadOnlyList<string> declared)
        {
            if (_newTag == null)
            {
                return;
            }

            var problem = GameplayTagEdits.NameProblem(_newTag.Trim());

            if (problem == null && GameplayTagEdits.IsKnown(declared, _newTag.Trim()))
            {
                problem = $"\"{_newTag.Trim()}\" is already a project tag.";
            }

            var evt = Event.current;

            var enter = evt.type == EventType.KeyDown && (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) && GUI.GetNameOfFocusedControl() == NewTagField;

            var escape = evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape && GUI.GetNameOfFocusedControl() == NewTagField;

            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.SetNextControlName(NewTagField);

                _newTag = EditorGUILayout.TextField(new GUIContent("New tag", "Letters, digits and underscores separated by dots, like Status.Burning."), _newTag);

                if (_focusNewTag)
                {
                    EditorGUI.FocusTextInControl(NewTagField);

                    _focusNewTag = false;
                }

                using (new EditorGUI.DisabledScope(problem != null))
                {
                    if (GUILayout.Button("Add", EditorStyles.miniButtonLeft, GUILayout.Width(44f)) || enter && problem == null)
                    {
                        var tag = _newTag.Trim();

                        var result = UEArchitectureActions.AddTag(tag);

                        Report_Internal(result);

                        _newTag = null;

                        if (result.Ok)
                        {
                            _builtFrom = null;

                            _tree.Select(tag);
                        }

                        if (enter)
                        {
                            evt.Use();
                        }

                        GUIUtility.ExitGUI();
                    }
                }

                if (GUILayout.Button("Cancel", EditorStyles.miniButtonRight, GUILayout.Width(52f)) || escape)
                {
                    _newTag = null;

                    if (escape)
                    {
                        evt.Use();
                    }

                    GUIUtility.ExitGUI();
                }
            }

            if (problem != null && _newTag.Trim().Length > 0)
            {
                EditorGUILayout.LabelField(problem, UEArchitectureStyles.MutedLabel);
            }
        }

        private void DrawConstants_Internal()
        {
            var path = UEArchitectureEditorSettings.instance.TagConstantsPath;

            EditorGUILayout.LabelField(new GUIContent($"Constants: {path}", "Tags.Status.Burning instead of GameplayTag.Request(\"Status.Burning\"). Change the file in Project Settings > UE Architecture > Editor."),
                                       UEArchitectureStyles.MutedLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();

                if (GUILayout.Button(new GUIContent("Generate C# constants", "Write the tags as C# constants (one compile)."), EditorStyles.miniButton, GUILayout.Width(150f)))
                {
                    var result = UEArchitectureActions.GenerateTagConstants();

                    if (!result.Ok && result.Problems.Any(problem => problem.Contains(ScriptWriter.RuntimeAssembly)) && !Application.isBatchMode &&
                        EditorUtility.DisplayDialog("Add the reference?", result.Problems[0] + "\n\nAdd it to the asmdef now?", "Add reference", "Cancel"))
                    {
                        result = UEArchitectureActions.GenerateTagConstants(null, null, true);
                    }

                    Report_Internal(result);

                    GUIUtility.ExitGUI();
                }
            }

            if (_lastMessage.Length > 0)
            {
                EditorGUILayout.HelpBox(_lastMessage, _lastType);
            }
        }

        /// <summary>
        /// Finds where the tag is used (with a progress bar; Cancel opens nothing), then asks for Rename… to open on the
        /// next repaint.
        /// </summary>
        private void RequestRename_Internal(string tag)
        {
            var usages = GameplayTagUsages.FindWithProgress(tag, true);

            if (usages == null)
            {
                return;
            }

            _renameRequested = tag;

            _renameUsages = usages;

            InternalEditorUtility.RepaintAllViews();
        }

        private void ShowContextMenu_Internal(string tag)
        {
            var menu = new GenericMenu();

            menu.AddItem(new GUIContent("Add sub-tag"), false, () => StartAdding_Internal(tag + "."));

            menu.AddItem(new GUIContent("Rename…"), false, () =>
            {
                _tree.Select(tag);

                RequestRename_Internal(tag);
            });

            menu.AddItem(new GUIContent("Delete…"), false, () => GameplayTagPopups.ConfirmDelete(tag));

            menu.AddSeparator(string.Empty);

            menu.AddItem(new GUIContent("Find usages"), false, () => Report_Internal(UEArchitectureActions.FindTagUsages(tag), true));

            menu.AddItem(new GUIContent("Copy name"), false, () => EditorGUIUtility.systemCopyBuffer = tag);

            menu.ShowAsContext();
        }

        private void Report_Internal(ActionResult result, bool withNotes = false)
        {
            _lastMessage = result.Ok ? result.Message : string.Join("\n", result.Problems);

            if (withNotes && result.Notes.Count > 0)
            {
                _lastMessage += "\n" + string.Join("\n", result.Notes.Take(12)) + (result.Notes.Count > 12 ? $"\n(and {result.Notes.Count - 12} more)" : string.Empty);
            }

            _lastType = result.Ok ? MessageType.Info : MessageType.Warning;
        }
    }

    /// <summary>
    /// Window > UE Architecture > Gameplay Tags: the tag manager of Project Settings in a window of its own.
    /// </summary>
    internal sealed class GameplayTagsWindow : EditorWindow
    {
        private readonly GameplayTagManager _manager = new();

        private Vector2 _scroll;

        [MenuItem("Window/UE Architecture/Gameplay Tags", false, 30)]
        public static void Open()
        {
            var window = GetWindow<GameplayTagsWindow>(false, "Gameplay Tags", true);

            window.minSize = new Vector2(360f, 260f);

            window.Show();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("Gameplay Tags");
        }

        private void OnGUI()
        {
            using (var view = new EditorGUILayout.ScrollViewScope(_scroll))
            {
                _scroll = view.scrollPosition;

                EditorGUILayout.LabelField("The project's gameplay tags. Tag fields in the Inspector offer them; Status.Burning also matches Status.", UEArchitectureStyles.MutedLabel);

                var settings = FrameworkIndex.Settings;

                var serialized = settings != null ? new SerializedObject(settings) : null;

                serialized?.Update();

                _manager.Draw(serialized);

                serialized?.ApplyModifiedProperties();
            }
        }

        private void OnProjectChange()
        {
            Repaint();
        }
    }
}
