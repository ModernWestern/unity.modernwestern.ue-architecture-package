using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using ModernWestern.UI.ObserverVM;
using Object = UnityEngine.Object;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// The integration's menus: GameObject > UE Architecture > Bind Framework Widgets Below (Hierarchy right-click),
    /// Assets > UE Architecture > Bind Framework Widgets Below (a prefab) and Create Widget for This… (a framework
    /// script), and a widget's gear menu > UE Architecture > Add Framework Binder.
    /// </summary>
    internal static class FrameworkWidgetMenus
    {
        private const int Priority = 1500;

        [MenuItem("GameObject/UE Architecture/Bind Framework Widgets Below", false, 14)]
        private static void BindBelow(MenuCommand command)
        {
            // The Hierarchy calls a menu once per selected object: open one window, for the active one.
            if (command.context != null && Selection.gameObjects.Length > 1 && command.context != Selection.activeGameObject)
            {
                return;
            }

            BindFrameworkWidgetsWindow.Open(command.context as GameObject ?? Selection.activeGameObject);
        }

        [MenuItem("GameObject/UE Architecture/Bind Framework Widgets Below", true)]
        private static bool BindBelowValidate() => Selection.activeGameObject != null && !EditorApplication.isPlaying;

        [MenuItem("Assets/UE Architecture/Bind Framework Widgets Below", false, Priority + 31)]
        private static void BindBelowPrefab()
        {
            BindFrameworkWidgetsWindow.Open(SelectedPrefab_Internal());
        }

        [MenuItem("Assets/UE Architecture/Bind Framework Widgets Below", true)]
        private static bool BindBelowPrefabValidate() => SelectedPrefab_Internal() != null;

        [MenuItem("CONTEXT/Widget/UE Architecture/Add Framework Binder", false, Priority)]
        private static void AddBinder(MenuCommand command)
        {
            Report_Internal(FrameworkWidgetActions.AddFrameworkBinder(command.context as Widget));
        }

        [MenuItem("CONTEXT/Widget/UE Architecture/Add Framework Binder", true)]
        private static bool AddBinderValidate(MenuCommand command) =>
            command.context is Widget widget && widget.GetComponent<FrameworkBinder>() == null && !ClassField.IsLocked(new Object[] { widget });

        [MenuItem("Assets/UE Architecture/Create Widget for This…", false, Priority + 32)]
        private static void CreateWidgetFor()
        {
            CreateWidgetForWindow.Open(FrameworkClassOf(Selection.activeObject));
        }

        [MenuItem("Assets/UE Architecture/Create Widget for This…", true)]
        private static bool CreateWidgetForValidate() => Selection.objects.Length == 1 && FrameworkClassOf(Selection.activeObject) != null && !EditorApplication.isPlaying;

        /// <summary>
        /// The framework class of a script (a GameState, PlayerState, Pawn, PlayerController, HUD, GameMode, subsystem or
        /// GameInstance), or null. Plain classes are found by the script's file name.
        /// </summary>
        internal static Type FrameworkClassOf(Object selected)
        {
            if (!(selected is MonoScript script))
            {
                return null;
            }

            var type = script.GetClass();

            if (type == null)
            {
                foreach (var frameworkBase in new[] { typeof(GameState), typeof(PlayerState), typeof(WorldSubsystem), typeof(GameInstanceSubsystem) })
                {
                    type = TypeCache.GetTypesDerivedFrom(frameworkBase).FirstOrDefault(candidate => candidate.Name == script.name);

                    if (type != null)
                    {
                        break;
                    }
                }
            }

            return type != null && !type.ContainsGenericParameters && BindingPreview.IsFrameworkViewModel(type) && !typeof(Widget).IsAssignableFrom(type) ? type : null;
        }

        private static GameObject SelectedPrefab_Internal()
        {
            return Selection.objects.Length == 1 && Selection.activeObject is GameObject prefab && EditorUtility.IsPersistent(prefab) && prefab.GetComponentInChildren<Widget>(true) != null
                       ? prefab
                       : null;
        }

        internal static void Report_Internal(ActionResult result)
        {
            if (result == null)
            {
                return;
            }

            if (result.Ok)
            {
                LastCreatedFiles.Remember(result);

                Debug.Log("UE Architecture: " + result);
            }
            else
            {
                Debug.LogWarning("UE Architecture: " + string.Join("\n", result.Problems));
            }

            ProblemProviders.Invalidate();
        }
    }

    /// <summary>
    /// Bind Framework Widgets Below: the framework widgets under an object, ticked when nothing binds them (unticked when
    /// their HUD or a parent widget does); Apply adds the binders as one Undo step.
    /// </summary>
    internal sealed class BindFrameworkWidgetsWindow : EditorWindow
    {
        private GameObject _root;

        private List<BinderCandidate> _candidates = new();

        private BinderTarget _target = BinderTarget.Instance;

        private Vector2 _scroll;

        public static void Open(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            var window = CreateInstance<BindFrameworkWidgetsWindow>();

            window.titleContent = new GUIContent("Bind Framework Widgets");

            window._root = root;

            window._candidates = FrameworkWidgetActions.BinderCandidates(root);

            window.minSize = new Vector2(460f, 240f);

            window.ShowUtility();
        }

        private void OnGUI()
        {
            if (_root == null)
            {
                EditorGUILayout.HelpBox("The object is gone. Close this window and pick it again.", MessageType.Info);

                return;
            }

            EditorGUILayout.LabelField($"Framework widgets under {_root.name}", UEArchitectureStyles.SectionTitle);

            EditorGUILayout.LabelField("Ticked widgets get a Framework Binder, which binds them to the framework object of their ViewModel type when the level begins.", UEArchitectureStyles.MutedLabel);

            using (var view = new EditorGUILayout.ScrollViewScope(_scroll))
            {
                _scroll = view.scrollPosition;

                if (_candidates.Count == 0)
                {
                    EditorGUILayout.LabelField("No framework widget here needs a binder.", UEArchitectureStyles.MutedLabel);
                }

                foreach (var candidate in _candidates.Where(candidate => candidate.Widget != null))
                {
                    candidate.Selected = EditorGUILayout.ToggleLeft(new GUIContent($"{candidate.Widget.name}  ({candidate.Widget.ViewModelType?.Name})", candidate.Reason), candidate.Selected);

                    EditorGUILayout.LabelField(candidate.Reason, UEArchitectureStyles.MutedLabel);
                }
            }

            var ticked = _candidates.Where(candidate => candidate.Selected && candidate.Widget != null).Select(candidate => candidate.Widget).ToList();

            // Widgets of a prefab placed in the level: the binder is an override on this copy, or goes in the prefab.
            if (ticked.Any(widget => !EditorUtility.IsPersistent(widget) && PrefabUtility.IsPartOfPrefabInstance(widget)))
            {
                _target = (BinderTarget)EditorGUILayout.Popup(new GUIContent("Add to", "Widgets of a prefab placed here: an override on this copy only, or the prefab itself (every copy)."), (int)_target,
                                                              new[] { new GUIContent("This copy (override)"), new GUIContent("The prefab (every copy)") });
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                var address = UEArchitectureCli.Address(_root);

                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(address)))
                {
                    if (GUILayout.Button(new GUIContent("Copy CLI command", "The same, from the command line (every ticked-by-default widget)."), GUILayout.Width(140f)))
                    {
                        EditorGUIUtility.systemCopyBuffer = _target == BinderTarget.Prefab
                                                                ? UEArchitectureCli.CommandLine("add-framework-binders", new KeyValuePair<string, string>("target", address), new KeyValuePair<string, string>("apply-to", "prefab"))
                                                                : UEArchitectureCli.CommandLine("add-framework-binders", new KeyValuePair<string, string>("target", address));
                    }
                }

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Cancel", GUILayout.Width(80f)))
                {
                    Close();
                }

                using (new EditorGUI.DisabledScope(ticked.Count == 0 || EditorApplication.isPlaying))
                {
                    if (UEArchitectureStyles.AccentButton(new GUIContent($"Apply ({ticked.Count})"), GUILayout.Width(100f)))
                    {
                        FrameworkWidgetMenus.Report_Internal(FrameworkWidgetActions.AddFrameworkBinders(_root, false, ticked, _target));

                        Close();

                        GUIUtility.ExitGUI();
                    }
                }
            }
        }
    }

    /// <summary>
    /// Create Widget for This…: a widget for a framework class, inside a HUD, under a Canvas of the level (with a
    /// Framework Binder), or as a prefab only.
    /// </summary>
    internal sealed class CreateWidgetForWindow : EditorWindow
    {
        private Type _type;

        private WidgetPlacement _placement;

        private HUD _hud;

        private GameObject _canvas;

        public static void Open(Type type)
        {
            if (type == null)
            {
                return;
            }

            var window = CreateInstance<CreateWidgetForWindow>();

            window.titleContent = new GUIContent("Create Widget");

            window._type = type;

            var settings = FrameworkIndex.Settings;

            window._hud = settings != null && settings.DefaultGameMode != null ? settings.DefaultGameMode.HUDPrefab : null;

            window._placement = window._hud != null ? WidgetPlacement.InsideHud : WidgetPlacement.PrefabOnly;

            window.minSize = new Vector2(420f, 170f);

            window.ShowUtility();
        }

        private void OnGUI()
        {
            if (_type == null)
            {
                Close();

                return;
            }

            EditorGUILayout.LabelField($"Widget for {_type.Name}", UEArchitectureStyles.SectionTitle);

            if (!FrameworkWidgetActions.HasBindableMembers(_type))
            {
                EditorGUILayout.HelpBox($"{_type.Name} has nothing a widget can show yet. Add a public Observable<T> (for example \"public Observable<int> Score {{ get; }} = new(0);\"), an ObservableList<T> or a Command, then come back.",
                                        MessageType.Info);

                return;
            }

            var labels = new[]
            {
                new GUIContent(_hud != null ? $"Inside {_hud.gameObject.name}" : "Inside a HUD", "The HUD binds it by itself."),
                new GUIContent("Selected Canvas (adds a Framework Binder)", "Under a Canvas of the level, bound by a Framework Binder."),
                new GUIContent("Prefab only", "Only the widget's prefab: place it where you want later.")
            };

            _placement = (WidgetPlacement)EditorGUILayout.Popup(new GUIContent("Placement"), (int)_placement, labels);

            Object target = null;

            switch (_placement)
            {
                case WidgetPlacement.InsideHud:
                    _hud = (HUD)EditorGUILayout.ObjectField(new GUIContent("HUD"), _hud, typeof(HUD), true);

                    target = _hud;

                    break;

                case WidgetPlacement.Canvas:
                    if (_canvas == null && Selection.activeGameObject != null && !EditorUtility.IsPersistent(Selection.activeGameObject))
                    {
                        _canvas = Selection.activeGameObject;
                    }

                    _canvas = (GameObject)EditorGUILayout.ObjectField(new GUIContent("Canvas"), _canvas, typeof(GameObject), true);

                    target = _canvas;

                    break;
            }

            GUILayout.FlexibleSpace();

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Cancel", GUILayout.Width(80f)))
                {
                    Close();
                }

                using (new EditorGUI.DisabledScope(_placement != WidgetPlacement.PrefabOnly && target == null))
                {
                    if (UEArchitectureStyles.AccentButton(new GUIContent("Create"), GUILayout.Width(100f)))
                    {
                        var type = _type;

                        var placement = _placement;

                        Close();

                        FrameworkWidgetMenus.Report_Internal(FrameworkWidgetActions.CreateWidgetFor(type, placement, target));

                        GUIUtility.ExitGUI();
                    }
                }
            }
        }
    }
}
