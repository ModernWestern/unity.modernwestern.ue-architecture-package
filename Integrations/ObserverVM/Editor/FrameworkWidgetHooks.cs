using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using ModernWestern.UI.ObserverVM;
using ModernWestern.UI.ObserverVM.EditorTools;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// The framework classes a widget can show, as a group of ObserverVM's ViewModel pickers ("Widget for ViewModel…"):
    /// the GameState and PlayerState classes and the subsystems. Test assemblies are left out unless asked for.
    /// </summary>
    public sealed class FrameworkViewModelCatalogProvider : IViewModelCatalogProvider
    {
        public const string GroupName = "Framework (UE Architecture)";

        private readonly bool _includeTests;

        public FrameworkViewModelCatalogProvider(bool includeTestAssemblies = false)
        {
            _includeTests = includeTestAssemblies;
        }

        public string Group => GroupName;

        public IEnumerable<Type> ViewModels()
        {
            var bases = new[] { typeof(GameState), typeof(PlayerState), typeof(WorldSubsystem), typeof(GameInstanceSubsystem) };

            var found = new List<Type> { typeof(GameState), typeof(PlayerState) };

            foreach (var baseType in bases)
            {
                found.AddRange(TypeCache.GetTypesDerivedFrom(baseType).Where(type => !type.ContainsGenericParameters && (!type.IsAbstract || baseType == typeof(GameState) || baseType == typeof(PlayerState))));
            }

            return found.Where(type => ViewModelCatalog.CanBeViewModel(type) && (_includeTests || !UEArchitectureSettingsProvider.IsTestType_Internal(type)))
                        .Distinct().OrderBy(type => type.Name, StringComparer.Ordinal).ToList();
        }
    }

    /// <summary>
    /// Plugs the framework into ObserverVM's editor (compiled only with ObserverVM 2.2 or newer): the widget inspector's
    /// "Bound by" row and a Framework Binder section, the ViewModel pickers' "Framework (UE Architecture)" group,
    /// validation rules for the Validation window and the Play and build checks (with ObserverVM 2.4 or newer, a note on
    /// prefabs whose binding depends on where they are placed), the Framework Binder that widgets made
    /// outside a HUD get, the HUD inspector's "Widgets in this HUD", the Game Mode Kit's HUD preset, the Level overlay
    /// and "-uea validate" problems, and the command line verbs.
    /// </summary>
    [InitializeOnLoad]
    internal static class FrameworkWidgetHooks
    {
        /// <summary>
        /// After ObserverVM's own answer (a parent widget's [BindWidget] field), which wins.
        /// </summary>
        public const int BoundByOrder = BoundByDetector.BuiltInOrder + 50;

        public const string SectionId = "uea.framework-binder";

        public const string KitPresetId = "framework-widgets";

        private static readonly List<IDisposable> _registrations = new();

        static FrameworkWidgetHooks()
        {
            Register_Internal();
        }

        internal static void Register_Internal()
        {
            foreach (var registration in _registrations)
            {
                registration.Dispose();
            }

            _registrations.Clear();

            _registrations.Add(WidgetInspectorExtensions.RegisterBoundBy(BoundByOrder, BoundBy_Internal));

            _registrations.Add(WidgetInspectorExtensions.RegisterSection(SectionId, 200, DrawSection_Internal));

            _registrations.Add(ViewModelCatalog.RegisterProvider(new FrameworkViewModelCatalogProvider()));

            _registrations.Add(WidgetValidationRules.Register(Rule_Internal));

            WidgetCreationHooks.Created -= OnWidgetCreated_Internal;

            WidgetCreationHooks.Created += OnWidgetCreated_Internal;

            ProblemProviders.RegisterLevel(Level_Internal);
#if UEA_OBSERVERVM_INFO
            ProblemProviders.Register(typeof(Widget), Placement_Internal);
#endif

            HUDEditor.RegisterSection(HudWidgetsSection.Draw);

            HudPresets.Register(new HudPreset(KitPresetId, "HUD with Widgets for Game State and Player State",
                                              "Adds a widget for the Game State and one for the Player State inside the HUD (ObserverVM): the HUD binds them by itself.",
                                              (gameState, playerState) => FrameworkWidgetActions.HasBindableMembers(gameState) || FrameworkWidgetActions.HasBindableMembers(playerState),
                                              FrameworkWidgetActions.AddStateWidgets_Internal));

            FrameworkWidgetCli.Register_Internal();
        }

        /// <summary>
        /// The "Bound by" answer: its HUD, its Framework Binder, or nothing (red), with what it will probably get.
        /// </summary>
        internal static BoundBy? BoundBy_Internal(Widget widget)
        {
            var binding = BindingPreview.Of(widget);

            if (binding.Source == BindingSource.None || string.IsNullOrEmpty(binding.Text))
            {
                return null;
            }

            var tone = binding.Severity == 0 ? BoundByTone.Bound : binding.Severity == 1 ? BoundByTone.Hint : binding.Severity == 2 ? BoundByTone.Warning : BoundByTone.Error;

            return new BoundBy(binding.Text, tone, binding.Binder, binding.Tooltip);
        }

        private static void DrawSection_Internal(WidgetInspectorContext context)
        {
            var widgets = context.Targets.Where(widget => widget != null).ToList();

            var unbound = widgets.Where(widget => BindingPreview.Of(widget).NeedsBinder).ToList();

            if (unbound.Count == 0)
            {
                return;
            }

            if (context.IsMultiEdit)
            {
                if (UEArchitectureStyles.Notice($"{unbound.Count} of the selected widgets show a framework object but nothing binds them.", MessageType.Warning, $"Add Framework Binders ({unbound.Count})"))
                {
                    ClassField.Run_Internal(() => FrameworkWidgetActions.AddFrameworkBinders(unbound));

                    GUIUtility.ExitGUI();
                }

                return;
            }

            var widget = unbound[0];

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();

                if (GUILayout.Button(new GUIContent("Add Framework Binder", "Binds this widget to the framework object of its ViewModel type when the level begins."), GUILayout.Width(170f)))
                {
                    ClassField.Run_Internal(() => FrameworkWidgetActions.AddFrameworkBinder(widget));

                    GUIUtility.ExitGUI();
                }

                var scene = widget.gameObject.scene;

                var binding = BindingPreview.Of(widget);

                var only = binding.Set != null && binding.Set.GameModeObject == null && !string.IsNullOrEmpty(scene.path) ? OnlyGameMode_Internal() : null;

                if (only != null && GUILayout.Button(new GUIContent($"Use {only.name} here", "This level has no GameMode: use the project's only GameMode prefab in its World Settings."), GUILayout.Width(170f)))
                {
                    ClassField.Run_Internal(() => UEArchitectureActions.UseGameModeInLevel(only, scene));

                    GUIUtility.ExitGUI();
                }
            }
        }

        /// <summary>
        /// ObserverVM's validation: framework widgets nothing binds (with "Add Framework Binder"), framework widgets of a
        /// level without a GameMode, and HUD widgets nothing matches. Prefabs outside a HUD depend on the level: with
        /// ObserverVM 2.4 or newer they get a note (Info, never a problem), before that they are not reported.
        /// </summary>
        internal static IEnumerable<BindWidgetIssue> Rule_Internal(Widget widget, string location)
        {
            var binding = BindingPreview.Of(widget);

            switch (binding.Source)
            {
                case BindingSource.Nothing:
                {
                    var name = widget.ViewModelType != null ? widget.ViewModelType.Name : "framework object";

                    var fix = new BindWidgetFix("Add Framework Binder", "Binds the widget to the framework object of its ViewModel type when the level begins.", false,
                                                () => FrameworkWidgetActions.ToObserverVM_Internal(FrameworkWidgetActions.AddFrameworkBinder(widget), "Add Framework Binder"));

                    yield return new BindWidgetIssue(widget, BindWidgetIssueSeverity.Warning, $"{widget.name} shows a {name} but is not in a HUD and has no Framework Binder: nothing binds it.", location, fix);

                    break;
                }

                case BindingSource.FrameworkBinder when binding.BinderInsideHud:
                {
                    yield return new BindWidgetIssue(widget, BindWidgetIssueSeverity.Warning, $"{widget.name}: {binding.Text}.", location);

                    break;
                }

                case BindingSource.HUD when binding.HudNotSpawned && binding.IsFrameworkWidget:
                {
                    yield return new BindWidgetIssue(widget, BindWidgetIssueSeverity.Warning, $"{widget.name} is under {binding.Binder.name}, which is placed in the level: no GameMode spawns it, so nothing binds the widget.", location);

                    break;
                }

                case BindingSource.FrameworkBinder when binding.Set != null && binding.Set.GameModeObject == null && !EditorUtility.IsPersistent(widget):
                {
                    var scene = widget.gameObject.scene;

                    var only = !string.IsNullOrEmpty(scene.path) ? OnlyGameMode_Internal() : null;

                    var fix = only != null
                                  ? new BindWidgetFix($"Use {only.name} here", "Uses the project's only GameMode prefab in this level's World Settings.", false,
                                                      () => FrameworkWidgetActions.ToObserverVM_Internal(UEArchitectureActions.UseGameModeInLevel(only, scene), "Use GameMode"))
                                  : null;

                    yield return new BindWidgetIssue(widget, BindWidgetIssueSeverity.Warning,
                                                     $"{LevelName_Internal(scene)} has framework widgets but no GameMode: {widget.name} only gets subsystems and the Game Instance.", location, fix);

                    break;
                }

                case BindingSource.HUD when !binding.Prediction.Matches && !binding.DependsOnPawn && !binding.HudNotSpawned:
                {
                    var name = widget.ViewModelType != null ? widget.ViewModelType.Name : "ViewModel";

                    yield return new BindWidgetIssue(widget, BindWidgetIssueSeverity.Warning, $"Widget<{name}> under {binding.Binder.name}: nothing matches, so the HUD leaves it unbound.", location);

                    break;
                }
#if UEA_OBSERVERVM_INFO
                case BindingSource.DependsOnPlacement:
                {
                    yield return new BindWidgetIssue(widget, BindWidgetIssueSeverity.Info, PlacementNote_Internal(widget, location), location);

                    break;
                }
#endif
            }
        }

        /// <summary>
        /// The Level overlay and "-uea validate": framework widgets of the level nothing binds (with "Bind N widgets"), and
        /// framework widgets in a level without a GameMode.
        /// </summary>
        internal static void Level_Internal(Scene scene, LevelReport report)
        {
            var unbound = new List<Widget>();

            var framework = 0;

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var widget in root.GetComponentsInChildren<Widget>(true))
                {
                    if (widget == null || !BindingPreview.IsFrameworkViewModel(widget.ViewModelType))
                    {
                        continue;
                    }

                    framework++;

                    if (BindingPreview.Of(widget).NeedsBinder)
                    {
                        unbound.Add(widget);
                    }
                }
            }

            if (unbound.Count == 1)
            {
                var widget = unbound[0];

                report.Problems.Add(new Problem(ProblemSeverity.Warning,
                                                $"{widget.name} shows a {widget.ViewModelType.Name} but is not in a HUD and has no Framework Binder: nothing binds it.", widget)
                                        .WithFix("Add Framework Binder", "add-framework-binder", false, () => FrameworkWidgetActions.AddFrameworkBinder(widget)));
            }
            else if (unbound.Count > 1)
            {
                var names = string.Join(", ", unbound.Take(4).Select(widget => widget.name)) + (unbound.Count > 4 ? ", ..." : string.Empty);

                report.Problems.Add(new Problem(ProblemSeverity.Warning, $"{unbound.Count} framework widgets are not in a HUD and have no Framework Binder: nothing binds them ({names}).", unbound[0])
                                        .WithFix($"Bind {unbound.Count} widgets", "add-framework-binders", false, () => FrameworkWidgetActions.AddFrameworkBinders(unbound)));
            }

            if (framework > 0 && report.GameMode == null)
            {
                report.Problems.Add(new Problem(ProblemSeverity.Info, $"{LevelName_Internal(scene)} has framework widgets but no GameMode: they only get subsystems and the Game Instance."));
            }
        }

#if UEA_OBSERVERVM_INFO
        /// <summary>
        /// The Level overlay in Prefab Mode: a note (never a problem) on the framework widgets of a prefab outside any HUD,
        /// whose binding depends on where the prefab is placed.
        /// </summary>
        internal static void Placement_Internal(Component component, List<Problem> problems)
        {
            if (component is not Widget widget || !BindingPreview.IsFrameworkViewModel(widget.ViewModelType))
            {
                return;
            }

            // Only a prefab file or Prefab Mode can depend on placement: objects of a level are left alone at once.
            if (!EditorUtility.IsPersistent(widget) && PrefabStageUtility.GetPrefabStage(widget.gameObject) == null)
            {
                return;
            }

            if (BindingPreview.Of(widget).Source == BindingSource.DependsOnPlacement)
            {
                problems.Add(new Problem(ProblemSeverity.Info, PlacementNote_Internal(widget, null), widget));
            }
        }

        /// <summary>
        /// "Score in ScoreWidget.prefab: what binds it depends on where you place it (inside a HUD, or with a Framework
        /// Binder)."
        /// </summary>
        internal static string PlacementNote_Internal(Widget widget, string location)
        {
            var path = location;

            if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                var stage = PrefabStageUtility.GetPrefabStage(widget.gameObject);

                path = EditorUtility.IsPersistent(widget) ? AssetDatabase.GetAssetPath(widget) : stage != null ? stage.assetPath : null;
            }

            var where = string.IsNullOrEmpty(path) ? string.Empty : $" in {Path.GetFileName(path)}";

            return $"{widget.name}{where}: what binds it depends on where you place it (inside a HUD, or with a Framework Binder).";
        }
#endif

        /// <summary>
        /// Widgets ObserverVM makes for a framework object outside a HUD get a Framework Binder (Project Settings > UE
        /// Architecture > Editor > Add Framework Binders). Only widgets of a level: a prefab may be placed inside a HUD.
        /// </summary>
        internal static void OnWidgetCreated_Internal(Widget widget, WidgetCreationKind kind)
        {
            if (widget == null || kind != WidgetCreationKind.ForViewModel || !UEArchitectureEditorSettings.instance.AutoAddFrameworkBinder)
            {
                return;
            }

            if (EditorUtility.IsPersistent(widget) || !widget.gameObject.scene.IsValid() || EditorSceneManager.IsPreviewScene(widget.gameObject.scene))
            {
                return;
            }

            if (!BindingPreview.Of(widget).NeedsBinder)
            {
                return;
            }

            Undo.AddComponent<FrameworkBinder>(widget.gameObject);

            ProblemProviders.Invalidate();
        }

        private static GameObject OnlyGameMode_Internal()
        {
            var prefabs = FrameworkIndex.PrefabsWith(typeof(GameMode)).Where(prefab => prefab.GetComponent<GameMode>() != null).Take(2).ToList();

            return prefabs.Count == 1 ? prefabs[0] : null;
        }

        private static string LevelName_Internal(Scene scene)
        {
            return string.IsNullOrEmpty(scene.path) ? string.IsNullOrEmpty(scene.name) ? "This level" : scene.name : Path.GetFileName(scene.path);
        }
    }
}
