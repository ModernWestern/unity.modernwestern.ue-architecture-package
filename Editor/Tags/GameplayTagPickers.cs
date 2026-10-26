using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEditorInternal;
using UnityEditor.IMGUI.Controls;
using System.Collections.Generic;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// The searchable tag picker of a tag field: the project's tags as a tree (typing "burn" finds Status.Burning), "None"
    /// first and "New tag…" last. A tag with children opens a submenu that starts with the tag itself.
    /// </summary>
    internal sealed class GameplayTagDropdown : AdvancedDropdown
    {
        public const string NoneLabel = "None";

        public const string NewTagLabel = "New tag…";

        private readonly Dictionary<AdvancedDropdownItem, string> _tags = new();

        private readonly Action<string> _picked;

        private readonly Action _newTag;

        private readonly IReadOnlyList<string> _tagList;

        private readonly bool _offerNone;

        /// <summary>
        /// A picker over <paramref name="tags"/> (the project's, with their parents, when null). <paramref name="picked"/>
        /// gets the tag, or an empty string for None; <paramref name="newTag"/> runs for "New tag…".
        /// </summary>
        public GameplayTagDropdown(AdvancedDropdownState state, Action<string> picked, Action newTag, bool offerNone = true, IReadOnlyList<string> tags = null) :
            base(state ?? new AdvancedDropdownState())
        {
            _picked = picked;

            _newTag = newTag;

            _offerNone = offerNone;

            _tagList = tags;

            minimumSize = new Vector2(260f, 300f);
        }

        /// <summary>
        /// The dropdown's items, for tests.
        /// </summary>
        internal AdvancedDropdownItem BuildRoot_Internal()
        {
            return BuildRoot();
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            _tags.Clear();

            var root = new AdvancedDropdownItem("Gameplay Tags");

            if (_offerNone)
            {
                Add_Internal(root, NoneLabel, string.Empty);

                root.AddSeparator();
            }

            var nodes = GameplayTagTree.Build(_tagList ?? ProjectTags.All());

            var parents = new Dictionary<string, AdvancedDropdownItem>(StringComparer.Ordinal);

            foreach (var node in nodes)
            {
                var parent = node.Depth == 0 || !parents.TryGetValue(node.Parent, out var found) ? root : found;

                if (!node.HasChildren)
                {
                    Add_Internal(parent, node.Name, node.Name);

                    continue;
                }

                var folder = new AdvancedDropdownItem(node.Leaf);

                parent.AddChild(folder);

                // The tag itself comes first inside its submenu, so "Status" can be picked as well as "Status.Burning".
                Add_Internal(folder, node.Name, node.Name);

                parents[node.Name] = folder;
            }

            if (nodes.Count == 0)
            {
                root.AddChild(new AdvancedDropdownItem("No tags yet") { enabled = false });
            }

            if (_newTag != null)
            {
                root.AddSeparator();

                var create = new AdvancedDropdownItem(NewTagLabel);

                _tags[create] = null;

                root.AddChild(create);
            }

            return root;
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            if (!_tags.TryGetValue(item, out var tag))
            {
                return;
            }

            if (tag == null)
            {
                _newTag?.Invoke();

                return;
            }

            _picked?.Invoke(tag);
        }

        private void Add_Internal(AdvancedDropdownItem parent, string label, string tag)
        {
            var item = new AdvancedDropdownItem(label);

            _tags[item] = tag;

            parent.AddChild(item);
        }
    }

    /// <summary>
    /// The checkbox tree of a tag container: tick the tags it holds. Searchable; "New tag…" adds a project tag and ticks it.
    /// </summary>
    internal sealed class GameplayTagChecklist : PopupWindowContent
    {
        private const string SearchName = "UEArchitecture.GameplayTagChecklist.Search";

        private readonly Func<IReadOnlyList<string>> _held;

        private readonly Action<string, bool> _toggle;

        private readonly Action _newTag;

        private Vector2 _scroll;

        private string _search = string.Empty;

        private bool _focused;

        /// <summary>
        /// <paramref name="held"/> reads the container's tags now; <paramref name="toggle"/> adds (true) or removes one.
        /// </summary>
        public GameplayTagChecklist(Func<IReadOnlyList<string>> held, Action<string, bool> toggle, Action newTag)
        {
            _held = held;

            _toggle = toggle;

            _newTag = newTag;
        }

        public override Vector2 GetWindowSize()
        {
            var rows = Mathf.Clamp(ProjectTags.All().Length, 3, 14);

            return new Vector2(300f, 74f + rows * (EditorGUIUtility.singleLineHeight + 2f));
        }

        public override void OnGUI(Rect rect)
        {
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            {
                Event.current.Use();

                editorWindow.Close();

                return;
            }

            GUI.SetNextControlName(SearchName);

            _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField);

            if (!_focused)
            {
                EditorGUI.FocusTextInControl(SearchName);

                _focused = true;
            }

            var held = _held?.Invoke() ?? Array.Empty<string>();

            var nodes = GameplayTagTree.Filter(GameplayTagTree.Build(ProjectTags.All()), _search);

            using (var view = new EditorGUILayout.ScrollViewScope(_scroll, GUILayout.ExpandHeight(true)))
            {
                _scroll = view.scrollPosition;

                if (nodes.Count == 0)
                {
                    EditorGUILayout.LabelField(ProjectTags.All().Length == 0 ? "No tags yet." : "No tag matches.", UEArchitectureStyles.MutedLabel);
                }

                foreach (var node in nodes)
                {
                    var on = held.Contains(node.Name);

                    // A child that is ticked makes its parents match too: they show that without being ticked themselves.
                    var implied = !on && held.Any(tag => GameplayTagTree.IsChildOf(tag, node.Name));

                    var rowRect = EditorGUILayout.GetControlRect();

                    rowRect.xMin += node.Depth * 14f;

                    var label = new GUIContent(node.Leaf, implied ? $"{node.Name} (matches through a tag below it)" : node.Name);

                    // A parent of a ticked tag shows as mixed: it matches without being in the container itself.
                    EditorGUI.showMixedValue = implied;

                    var now = EditorGUI.ToggleLeft(rowRect, label, on);

                    EditorGUI.showMixedValue = false;

                    if (now != on)
                    {
                        _toggle?.Invoke(node.Name, now);

                        GUIUtility.ExitGUI();
                    }
                }
            }

            if (_newTag != null && GUILayout.Button(GameplayTagDropdown.NewTagLabel, EditorStyles.miniButton))
            {
                editorWindow.Close();

                _newTag();

                GUIUtility.ExitGUI();
            }
        }
    }

    /// <summary>
    /// The popups of the tag tools: "New tag…" (adds a project tag, then hands it to the field) and Rename… (with what
    /// the rename will update), built on <see cref="NamePopup"/>.
    /// </summary>
    internal static class GameplayTagPopups
    {
        /// <summary>
        /// A request older than this is dropped (its field was scrolled away or closed).
        /// </summary>
        private const double RequestLifetimeSeconds = 3.0;

        /// <summary>
        /// PopupWindow does not open within 0.2 s of closing one over the same place (it takes it for the click that
        /// closed it), so a popup asked for from the checklist waits a little longer.
        /// </summary>
        private const double ReopenDelaySeconds = 0.25;

        private static PopupRequest _request;

        /// <summary>
        /// Asks the tag field <paramref name="path"/> of <paramref name="owner"/> to open "New tag…" under itself on its
        /// next repaint (see <see cref="ShowRequested"/>), prefilled with <paramref name="initial"/> ("Status." to add a
        /// sub-tag). <paramref name="added"/> gets the tag once it is in the project. Used from the tag picker and the
        /// checklist, which cannot open a popup themselves: they are closing, and their coordinates are not the field's.
        /// </summary>
        public static void RequestNewTag(UnityEngine.Object owner, string path, string initial, Action<string> added)
        {
            var popup = new NamePopup("New gameplay tag", initial, Preview_Internal, name =>
            {
                var result = UEArchitectureActions.AddTag(name);

                if (!result.Ok)
                {
                    Debug.LogWarning("[UEArchitecture] " + string.Join("\n", result.Problems));

                    return;
                }

                added?.Invoke(name.Trim());
            }, "Add", name => UEArchitectureCli.CommandLine("add-tag", new KeyValuePair<string, string>("tag", name)));

            _request = new PopupRequest(owner != null ? EditorCompat.SessionKey(owner) : 0, path, popup, EditorApplication.timeSinceStartup);

            EditorApplication.update -= RepaintWhenReady_Internal;

            EditorApplication.update += RepaintWhenReady_Internal;

            InternalEditorUtility.RepaintAllViews();
        }

        /// <summary>
        /// Called by a tag field's drawer on every event: on Repaint, opens the popup asked for this field under
        /// <paramref name="rect"/>. True when it opened one.
        /// </summary>
        public static bool ShowRequested(Rect rect, UnityEngine.Object owner, string path)
        {
            if (_request == null || Event.current == null || Event.current.type != EventType.Repaint)
            {
                return false;
            }

            var age = EditorApplication.timeSinceStartup - _request.Time;

            if (age > RequestLifetimeSeconds)
            {
                _request = null;

                return false;
            }

            if (age < ReopenDelaySeconds || !HasRequest_Internal(owner, path))
            {
                return false;
            }

            var request = _request;

            _request = null;

            PopupWindow.Show(rect, request.Content);

            return true;
        }

        /// <summary>
        /// True while a popup waits for this field, for tests.
        /// </summary>
        internal static bool HasRequest_Internal(UnityEngine.Object owner, string path)
        {
            return _request != null && owner != null && _request.OwnerKey == EditorCompat.SessionKey(owner) && _request.Path == path;
        }

        internal static void ClearRequest_Internal()
        {
            _request = null;
        }

        private static void RepaintWhenReady_Internal()
        {
            if (_request != null && EditorApplication.timeSinceStartup - _request.Time < ReopenDelaySeconds)
            {
                return;
            }

            EditorApplication.update -= RepaintWhenReady_Internal;

            if (_request != null)
            {
                InternalEditorUtility.RepaintAllViews();
            }
        }

        /// <summary>
        /// Shows Rename… for <paramref name="tag"/>: the new name, and what it updates ("Update 14 references in 6 assets;
        /// 2 levels listed, not changed"), from <paramref name="usages"/>, found before (see
        /// <see cref="GameplayTagUsages.FindWithProgress"/>; never on a repaint).
        /// </summary>
        public static void ShowRename(Rect anchor, string tag, TagUsageReport usages, Action<string> renamed = null)
        {
            var popup = new NamePopup($"Rename \"{tag}\"", tag, name =>
            {
                var current = FrameworkIndex.Settings?.GameplayTags ?? Array.Empty<string>();

                var problem = name == tag ? "Type the new name." : GameplayTagEdits.RenameProblem(current, tag, (name ?? string.Empty).Trim());

                var children = GameplayTagEdits.Subtree(current, tag).Count(listed => listed != tag);

                var text = usages.RenameSummary() + (children > 0 ? $" The {TagUsageReport.Plural_Internal(children, "tag")} below it move too." : string.Empty);

                return new NamePreview(text, problem != null ? new[] { problem } : Array.Empty<string>());
            }, name =>
            {
                var result = UEArchitectureActions.RenameTag(tag, name.Trim(), true);

                if (!result.Ok)
                {
                    Debug.LogWarning("[UEArchitecture] " + string.Join("\n", result.Problems));

                    return;
                }

                foreach (var note in result.Notes)
                {
                    Debug.Log("[UEArchitecture] " + note);
                }

                renamed?.Invoke(name.Trim());
            }, "Rename", name => UEArchitectureCli.CommandLine("rename-tag", new KeyValuePair<string, string>("from", tag), new KeyValuePair<string, string>("to", name),
                                                               new KeyValuePair<string, string>("apply", "true")));

            PopupWindow.Show(anchor, popup);
        }

        /// <summary>
        /// Asks before deleting a tag (with how often it is used), then deletes it with the tags below it.
        /// </summary>
        public static bool ConfirmDelete(string tag)
        {
            var usages = GameplayTagUsages.FindWithProgress(tag, true);

            if (usages == null)
            {
                // Cancelled while searching: nothing is deleted.
                return false;
            }

            var current = FrameworkIndex.Settings?.GameplayTags ?? Array.Empty<string>();

            var children = GameplayTagEdits.Subtree(current, tag).Count(listed => listed != tag);

            var message = (children > 0 ? $"The {TagUsageReport.Plural_Internal(children, "tag")} below it go too.\n\n" : string.Empty) +
                          (usages.IsEmpty ? "Nothing uses it." : usages.Summary() + " Those uses stay and show it as \"not a project tag\".");

            if (!EditorUtility.DisplayDialog($"Delete the tag \"{tag}\"?", message, "Delete", "Cancel"))
            {
                return false;
            }

            var result = UEArchitectureActions.RemoveTag(tag, true);

            if (!result.Ok)
            {
                Debug.LogWarning("[UEArchitecture] " + string.Join("\n", result.Problems));
            }

            return result.Ok;
        }

        private static NamePreview Preview_Internal(string name)
        {
            var tag = (name ?? string.Empty).Trim();

            var problem = GameplayTagEdits.NameProblem(tag);

            if (problem != null)
            {
                return new NamePreview(string.Empty, new[] { problem });
            }

            if (ProjectTags.IsKnown(tag))
            {
                return new NamePreview(string.Empty, new[] { $"\"{tag}\" is already a project tag." });
            }

            return new NamePreview($"Adds \"{tag}\" to Project Settings > UE Architecture > Gameplay Tags.", Array.Empty<string>());
        }

        private sealed class PopupRequest
        {
            public PopupRequest(int ownerKey, string path, PopupWindowContent content, double time)
            {
                OwnerKey = ownerKey;

                Path = path;

                Content = content;

                Time = time;
            }

            public int OwnerKey { get; }

            public string Path { get; }

            public PopupWindowContent Content { get; }

            public double Time { get; }
        }
    }
}
