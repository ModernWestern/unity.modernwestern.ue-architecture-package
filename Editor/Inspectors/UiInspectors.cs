using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using UnityEditorInternal;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// The HUD inspector: its fields, "Canvas on the root" and "EventSystem in this level" with their fixes, and the empty
    /// references a child could fill ("Find children by name"). Other editor assemblies add sections through
    /// <see cref="ExtraSections"/>.
    /// </summary>
    [CustomEditor(typeof(HUD), true)]
    [CanEditMultipleObjects]
    internal sealed class HUDEditor : Editor
    {
        /// <summary>
        /// Sections drawn after the built-in ones, for one HUD (the ObserverVM integration adds its own here).
        /// </summary>
        internal static readonly List<Action<HUD, SerializedObject>> ExtraSections = new();

        private int _version = -1;

        private List<(string Path, Type Type)> _findable = new();

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            if (targets.Length != 1)
            {
                return;
            }

            var hud = (HUD)target;

            ProblemsSection.Draw(hud);

            var level = hud.gameObject.scene.IsValid() && !EditorSceneManager.IsPreviewScene(hud.gameObject.scene) ? hud.gameObject.scene : SceneManager.GetActiveScene();

            var missing = ProblemProviders.ForLevel(level).Problems.FirstOrDefault(problem => problem.FixId == "add-event-system");

            if (missing != null)
            {
                UEArchitectureStyles.Section("EventSystem in this level");

                ProblemsSection.DrawProblem(missing);
            }

            if (_version != ProblemProviders.Version)
            {
                _version = ProblemProviders.Version;

                _findable = UEArchitectureActions.EmptyChildFields(hud).Where(field => UEArchitectureActions.ChildrenNamedLike(hud.transform, Leaf_Internal(field.Path), field.Type).Count == 1)
                                                  .ToList();
            }

            if (_findable.Count > 0)
            {
                UEArchitectureStyles.Section("Empty references");

                foreach (var (path, _) in _findable)
                {
                    UEArchitectureStyles.Row(ObjectNames.NicifyVariableName(Leaf_Internal(path)), "empty: a child has its name", true);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button(new GUIContent($"Find children by name ({_findable.Count})", "Fills each empty field with the child named like it. Fields that are set stay.")))
                    {
                        ClassField.Run_Internal(() => UEArchitectureActions.FindChildrenByName(hud));

                        ProblemProviders.Invalidate();

                        GUIUtility.ExitGUI();
                    }
                }
            }

            foreach (var section in ExtraSections)
            {
                try
                {
                    section(hud, serializedObject);
                }
                catch (ExitGUIException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        private static string Leaf_Internal(string path)
        {
            var dot = path.LastIndexOf('.');

            return dot < 0 ? path : path.Substring(dot + 1);
        }
    }

    /// <summary>
    /// The Loading Screen inspector: its fields, what keeps it from fading or drawing on top (with fixes), and a Preview
    /// slider that shows a copy in the Game view (never saved, the prefab is not changed).
    /// </summary>
    [CustomEditor(typeof(LoadingScreen), true)]
    [CanEditMultipleObjects]
    internal sealed class LoadingScreenEditor : Editor
    {
        private float _progress = 0.5f;

        private LoadingScreen _preview;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            if (targets.Length != 1)
            {
                return;
            }

            var screen = (LoadingScreen)target;

            ProblemsSection.Draw(screen);

            if (EditorApplication.isPlayingOrWillChangePlaymode || UiTypes.Canvas == null || UiTypes.CanvasIn(screen.gameObject) == null)
            {
                StopPreview_Internal();

                return;
            }

            UEArchitectureStyles.Section("Preview");

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();

                _progress = EditorGUILayout.Slider(new GUIContent("Progress", "Shows a copy in the Game view at this progress. The copy is never saved."), _progress, 0f, 1f);

                if (EditorGUI.EndChangeCheck())
                {
                    ShowPreview_Internal(screen);
                }

                if (_preview != null && GUILayout.Button("Stop", EditorStyles.miniButton, GUILayout.Width(44f)))
                {
                    StopPreview_Internal();
                }
            }

            EditorGUILayout.LabelField(_preview != null ? "Showing in the Game view." : "Move the slider to show it in the Game view.", UEArchitectureStyles.MutedLabel);
        }

        private void OnDisable()
        {
            StopPreview_Internal();
        }

        private void ShowPreview_Internal(LoadingScreen screen)
        {
            if (_preview == null)
            {
                // A copy that is never saved and never shown in the Hierarchy: the prefab and the level stay untouched.
                _preview = Instantiate(screen);

                foreach (var transform in _preview.GetComponentsInChildren<Transform>(true))
                {
                    transform.gameObject.hideFlags = HideFlags.HideAndDontSave;
                }

                _preview.gameObject.SetActive(true);

                var group = UiTypes.Get(_preview.gameObject, UiTypes.CanvasGroup);

                if (group != null)
                {
                    var serialized = new SerializedObject(group);

                    serialized.FindProperty("m_Alpha").floatValue = 1f;

                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            try
            {
                _preview.SetProgress_Internal(_progress);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }

            InternalEditorUtility.RepaintAllViews();
        }

        private void StopPreview_Internal()
        {
            if (_preview == null)
            {
                return;
            }

            DestroyImmediate(_preview.gameObject);

            _preview = null;

            InternalEditorUtility.RepaintAllViews();
        }
    }

    /// <summary>
    /// The Player Start inspector: its tag with the tags of the level's other starts, and "Floating 1.4 m above the
    /// ground", "Off the NavMesh" and duplicated tags with their fixes.
    /// </summary>
    [CustomEditor(typeof(PlayerStart), true)]
    [CanEditMultipleObjects]
    internal sealed class PlayerStartEditor : Editor
    {
        private static readonly string[] _excluded = { "m_Script", "startTag" };

        private static int _tagsVersion = -1;

        private static PlayerStart _tagsOf;

        private static List<string> _tags;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
            }

            var tag = serializedObject.FindProperty("startTag");

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(tag, new GUIContent("Start Tag", tag.tooltip));

                var others = OtherTags_Internal((PlayerStart)target);

                using (new EditorGUI.DisabledScope(others.Count == 0 || tag.hasMultipleDifferentValues))
                {
                    if (GUILayout.Button(new GUIContent("Tags", others.Count == 0 ? "No other Player Start of this level has a tag." : "The tags of the level's other Player Starts."),
                                         EditorStyles.miniButton, GUILayout.Width(44f)))
                    {
                        var menu = new GenericMenu();

                        var path = tag.propertyPath;

                        var owners = targets;

                        foreach (var name in others)
                        {
                            menu.AddItem(new GUIContent(name), false, () => AssignTag_Internal(owners, path, name));
                        }

                        menu.ShowAsContext();
                    }
                }
            }

            DrawPropertiesExcluding(serializedObject, _excluded);

            serializedObject.ApplyModifiedProperties();

            if (targets.Length == 1)
            {
                ProblemsSection.Draw((PlayerStart)target);
            }
        }

        /// <summary>
        /// The tags of the level's other Player Starts, sorted, gathered again only when the problems change (never per GUI
        /// event).
        /// </summary>
        internal static List<string> OtherTags_Internal(PlayerStart start)
        {
            if (_tags != null && _tagsVersion == ProblemProviders.Version && _tagsOf == start)
            {
                return _tags;
            }

            _tagsVersion = ProblemProviders.Version;

            _tagsOf = start;

            _tags = start != null && start.gameObject.scene.IsValid()
                        ? LevelSetup.Starts_Internal(start.gameObject.scene).Where(other => other != start && !string.IsNullOrEmpty(other.StartTag)).Select(other => other.StartTag)
                                    .Distinct().OrderBy(name => name, StringComparer.Ordinal).ToList()
                        : new List<string>();

            return _tags;
        }

        /// <summary>
        /// The Tags menu: one tag on every selected Player Start, as one Undo step.
        /// </summary>
        internal static void AssignTag_Internal(IReadOnlyList<Object> owners, string propertyPath, string tag)
        {
            Scaffold.AsOneUndoStep("Set start tag " + tag, () =>
            {
                foreach (var owner in owners)
                {
                    ClassField.Run_Internal(() => UEArchitectureActions.Assign(owner, propertyPath, tag));
                }
            });
        }
    }

    /// <summary>
    /// The Pawn inspector: its fields as usual, plus its problems (Auto Possess AI with a plain AIController).
    /// </summary>
    [CustomEditor(typeof(Pawn), true)]
    [CanEditMultipleObjects]
    internal sealed class PawnEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            if (targets.Length == 1)
            {
                ProblemsSection.Draw((Pawn)target);
            }
        }
    }
}
