using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor.SceneManagement;
using ModernWestern.UI.ObserverVM;
using ModernWestern.UI.ObserverVM.EditorTools;
using Object = UnityEngine.Object;
using OvmResult = ModernWestern.UI.ObserverVM.EditorTools.ActionResult;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// A widget "Bind Framework Widgets Below" found, and whether it gets a Framework Binder.
    /// </summary>
    public sealed class BinderCandidate
    {
        public BinderCandidate(Widget widget, bool selected, string reason)
        {
            Widget = widget;

            Selected = selected;

            Reason = reason ?? string.Empty;
        }

        public Widget Widget { get; }

        /// <summary>
        /// Ticked: nothing binds it now. Unticked: something else does (a parent widget, its HUD).
        /// </summary>
        public bool Selected { get; set; }

        /// <summary>
        /// What it will probably get with a binder, or why it is unticked.
        /// </summary>
        public string Reason { get; }
    }

    /// <summary>
    /// One row of a HUD's "Widgets in this HUD" table.
    /// </summary>
    public sealed class HudWidgetRow
    {
        public HudWidgetRow(Widget widget, WidgetBinding binding)
        {
            Widget = widget;

            Binding = binding;
        }

        public Widget Widget { get; }

        public WidgetBinding Binding { get; }

        /// <summary>
        /// A parent widget gives it its ViewModel.
        /// </summary>
        public bool GivenByParent => Binding.Source == BindingSource.None && BindingPreview.BoundByParent(Widget);

        public bool Matches => Binding.Prediction.Matches;
    }

    /// <summary>
    /// Where "Create Widget for This…" puts the widget.
    /// </summary>
    public enum WidgetPlacement
    {
        /// <summary>
        /// Inside a HUD prefab (the HUD binds it).
        /// </summary>
        InsideHud,

        /// <summary>
        /// Under a Canvas of a level, with a Framework Binder.
        /// </summary>
        Canvas,

        /// <summary>
        /// Only the widget's prefab.
        /// </summary>
        PrefabOnly
    }

    /// <summary>
    /// Which copy gets the Framework Binder of a widget that is part of a prefab placed in a level (or nested in the
    /// prefab open in Prefab Mode).
    /// </summary>
    public enum BinderTarget
    {
        /// <summary>
        /// This copy only: the binder is an override on the prefab instance (the default).
        /// </summary>
        Instance,

        /// <summary>
        /// The prefab the copy comes from: every copy of it gets the binder.
        /// </summary>
        Prefab
    }

    /// <summary>
    /// The ObserverVM integration's editor actions, for buttons, menus, the overlay's fixes and the command line ("-uea
    /// predict-binding", "add-framework-binders", "add-hud-widget", "create-widget-for", "hud-widgets"). Each returns a UE
    /// Architecture <see cref="ActionResult"/>, is one Undo step, refuses objects of a level in Play mode, and running it
    /// again changes nothing.
    /// </summary>
    public static class FrameworkWidgetActions
    {
        /// <summary>
        /// Pending step kind: add a widget to a HUD once its new widget class compiles.
        /// </summary>
        public const string HudWidgetKind = "uea-ovm.hud-widget";

        /// <summary>
        /// "What will this widget bind to?": the answer in <see cref="ActionResult.Message"/>, with the slot and class in
        /// the notes. Changes nothing.
        /// </summary>
        public static ActionResult PredictBinding(Widget widget)
        {
            var result = new ActionResult("predict-binding");

            if (widget == null)
            {
                result.AddProblem("Pick a widget.");

                return result;
            }

            var binding = BindingPreview.Of(widget);

            result.Result = widget;

            result.Message = string.IsNullOrEmpty(binding.Text) ? WidgetInspectorExtensions.BoundByOf(widget).Text : binding.Text;

            result.AddNote($"source: {binding.Source}");

            result.AddNote($"slot: {binding.Prediction.Slot}");

            if (binding.Prediction.Type != null)
            {
                result.AddNote($"class: {binding.Prediction.Type.FullName}");
            }

            if (!string.IsNullOrEmpty(binding.Tooltip))
            {
                result.AddNote(binding.Tooltip.Trim());
            }

            return result;
        }

        /// <summary>
        /// The class a script file declares once compiled, or null (not compiled yet, or not imported). Tests replace it.
        /// </summary>
        internal static Func<string, Type> ClassOfScript_Internal = path => AssetDatabase.LoadAssetAtPath<MonoScript>(path)?.GetClass();

        /// <summary>
        /// The widgets under <paramref name="root"/> (itself included) that expect a framework object and have no Framework
        /// Binder: ticked when nothing binds them, unticked when their HUD or a parent widget does, when their HUD is placed
        /// in the level (nothing starts it), or when the prefab is placed inside a HUD prefab (that HUD binds them; a binder
        /// would bind the same player in every player's HUD).
        /// </summary>
        public static List<BinderCandidate> BinderCandidates(GameObject root)
        {
            var candidates = new List<BinderCandidate>();

            if (root == null)
            {
                return candidates;
            }

            var insideHud = HudPrefabContaining_Internal(root);

            foreach (var widget in root.GetComponentsInChildren<Widget>(true))
            {
                if (widget == null || widget.GetComponent<FrameworkBinder>() != null || !BindingPreview.IsFrameworkViewModel(widget.ViewModelType))
                {
                    continue;
                }

                if (BindingPreview.BoundByParent(widget))
                {
                    candidates.Add(new BinderCandidate(widget, false, "Its parent widget gives it its ViewModel."));

                    continue;
                }

                var hud = BindingPreview.HudOf(widget);

                if (hud != null)
                {
                    candidates.Add(new BinderCandidate(widget, false, BindingPreview.IsPlacedInLevel(hud)
                                                                          ? $"{hud.gameObject.name} is placed in the level, not spawned by a GameMode: make it the GameMode's HUD prefab, or add a binder to this widget by itself."
                                                                          : $"{hud.gameObject.name} binds it."));

                    continue;
                }

                if (insideHud != null)
                {
                    candidates.Add(new BinderCandidate(widget, false, $"This prefab is placed inside {insideHud.name}, which binds it: a binder would show the same player in every player's HUD."));

                    continue;
                }

                var binding = BindingPreview.Of(widget);

                candidates.Add(new BinderCandidate(widget, true, binding.Prediction.Matches ? $"Will probably bind to {BindingPreview.Describe(binding.Prediction, binding.Set)}." : binding.Text));
            }

            return candidates;
        }

        /// <summary>
        /// Bind Framework Widgets Below: a Framework Binder on every ticked widget under <paramref name="root"/> (a level
        /// object, the prefab open in Prefab Mode, or a prefab asset), or only on <paramref name="only"/> when given.
        /// Widgets bound by their HUD or a parent widget are never changed. Widgets of a prefab placed in a level get the
        /// binder as an override on that copy, or in the prefab itself with <paramref name="target"/> Prefab. With
        /// <paramref name="dryRun"/>, only lists what it would do. One Undo step.
        /// </summary>
        public static ActionResult AddFrameworkBinders(GameObject root, bool dryRun = false, IEnumerable<Widget> only = null, BinderTarget target = BinderTarget.Instance)
        {
            using (var op = Scaffold.Begin("Bind Framework Widgets Below", "add-framework-binders"))
            {
                if (root == null)
                {
                    op.Problem("Pick a Canvas, a prefab or an object with widgets.");

                    return op.Result;
                }

                var candidates = BinderCandidates(root);

                var chosen = only != null ? new HashSet<Widget>(only.Where(widget => widget != null)) : null;

                var widgets = candidates.Where(candidate => chosen != null ? chosen.Contains(candidate.Widget) && candidate.Selected : candidate.Selected).Select(candidate => candidate.Widget).ToList();

                foreach (var skipped in candidates.Where(candidate => !candidate.Selected))
                {
                    op.Result.AddNote($"Left {ActionResult.Describe(skipped.Widget)} alone: {skipped.Reason}");
                }

                if (dryRun)
                {
                    foreach (var widget in widgets)
                    {
                        op.Result.AddNote($"Would add a Framework Binder to {ActionResult.Describe(widget)}{WhereNote_Internal(widget, target)}.");
                    }

                    op.Result.Message = widgets.Count == 0 ? "Nothing to bind." : $"Would add {widgets.Count} Framework Binder(s).";

                    return op.Result;
                }

                AddBinders_Internal(op, root, widgets, 0, target);

                op.Result.Result = root;

                op.Result.Message = op.Result.Created.Count + op.Result.Changed.Count == 0 ? "Nothing to bind: every framework widget below is bound." : $"Added {op.Result.Changed.Count} Framework Binder(s).";

                return op.Result;
            }
        }

        /// <summary>
        /// A Framework Binder on one widget (<paramref name="playerIndex"/>: 0 is the first player). Nothing happens when
        /// it has one. Refused when its HUD or a parent widget binds it (the binder would fight them). On a prefab placed
        /// in a level the binder is an override on that copy, or goes in the prefab with <paramref name="target"/> Prefab.
        /// </summary>
        public static ActionResult AddFrameworkBinder(Widget widget, int playerIndex = 0, BinderTarget target = BinderTarget.Instance)
        {
            using (var op = Scaffold.Begin("Add Framework Binder", "add-framework-binder"))
            {
                if (widget == null)
                {
                    op.Problem("Pick a widget.");

                    return op.Result;
                }

                if (widget.GetComponent<FrameworkBinder>() != null)
                {
                    op.Result.Message = $"{widget.name} already has a Framework Binder.";

                    return op.Result;
                }

                if (BindingPreview.BoundByParent(widget))
                {
                    op.Problem($"{widget.name} gets its ViewModel from its parent widget: a Framework Binder would replace it. Leave it to the parent.");

                    return op.Result;
                }

                var hud = BindingPreview.HudOf(widget);

                if (hud != null && !BindingPreview.IsPlacedInLevel(hud))
                {
                    op.Problem($"{widget.name} is inside {hud.gameObject.name}, which binds it already: a binder would bind Player {playerIndex + 1} before the HUD can, in every player's HUD.");

                    return op.Result;
                }

                var insideHud = HudPrefabContaining_Internal(widget.transform.root.gameObject);

                if (insideHud != null)
                {
                    op.Problem($"{widget.name}'s prefab is placed inside {insideHud.name}, which binds it already: a binder would bind Player {playerIndex + 1} in every player's HUD.");

                    return op.Result;
                }

                AddBinders_Internal(op, widget.transform.root.gameObject, new List<Widget> { widget }, playerIndex, target);

                op.Result.Result = widget;

                op.Result.Message = op.Result.Changed.Count == 0 ? $"{widget.name} was not changed." : $"{widget.name} is bound by a Framework Binder now.";

                return op.Result;
            }
        }

        /// <summary>
        /// Framework Binders on several widgets (the overlay's "Bind 2 widgets"), as one Undo step.
        /// </summary>
        public static ActionResult AddFrameworkBinders(IEnumerable<Widget> widgets)
        {
            var list = (widgets ?? Array.Empty<Widget>()).Where(widget => widget != null).Distinct().ToList();

            var merged = new ActionResult("add-framework-binders");

            Scaffold.AsOneUndoStep("Bind framework widgets", () =>
            {
                foreach (var widget in list)
                {
                    var single = AddFrameworkBinder(widget);

                    merged.Merge(single);
                }
            });

            merged.Message = merged.Changed.Count == 0 ? "Nothing to bind." : $"Added {merged.Changed.Count} Framework Binder(s).";

            return merged;
        }

        /// <summary>
        /// The widgets of a HUD and what each will probably get (from <paramref name="preview"/>, else the first GameMode
        /// that uses the HUD).
        /// </summary>
        public static List<HudWidgetRow> HudWidgets(HUD hud, GameMode preview = null)
        {
            var rows = new List<HudWidgetRow>();

            if (hud == null)
            {
                return rows;
            }

            foreach (var widget in hud.GetComponentsInChildren<Widget>(true))
            {
                if (widget != null)
                {
                    rows.Add(new HudWidgetRow(widget, BindingPreview.Of(widget, preview)));
                }
            }

            return rows;
        }

        /// <summary>
        /// The "Widgets in this HUD" table as a result: one note per widget ("PlayerCoinsWidget: Player State
        /// (ArenaPlayerState)" or "FooWidget: nothing matches"). Widgets nothing matches are problems with
        /// <paramref name="strict"/>.
        /// </summary>
        public static ActionResult DescribeHudWidgets(HUD hud, GameMode preview = null, bool strict = false)
        {
            var result = new ActionResult("hud-widgets");

            if (hud == null)
            {
                result.AddProblem("Pick a HUD.");

                return result;
            }

            var rows = HudWidgets(hud, preview);

            foreach (var row in rows)
            {
                string line;

                if (row.GivenByParent)
                {
                    line = $"{row.Widget.name}: given by its parent widget";
                }
                else if (row.Binding.BinderInsideHud || row.Binding.HudNotSpawned)
                {
                    line = $"{row.Widget.name}: {row.Binding.Text}";

                    if (strict)
                    {
                        result.AddProblem(line);

                        continue;
                    }
                }
                else if (row.Matches)
                {
                    line = $"{row.Widget.name}: {BindingPreview.SlotLabel(row.Binding.Prediction.Slot)} ({row.Binding.Prediction.Type.Name})";
                }
                else
                {
                    line = $"{row.Widget.name}: nothing matches ({row.Binding.Text})";

                    if (strict)
                    {
                        result.AddProblem(line);

                        continue;
                    }
                }

                result.AddNote(line);
            }

            var users = BindingPreview.GameModesUsing(hud);

            result.Result = hud;

            result.Message = (users.Count == 0 ? $"{hud.gameObject.name} is used by no GameMode yet." : $"{hud.gameObject.name} is used by {string.Join(", ", users.Select(user => $"{user.gameObject.name} ({BindingPreview.Usage_Internal(user)})"))}.") +
                             $" {rows.Count} widget(s).";

            return result;
        }

        /// <summary>
        /// The framework classes a HUD can show a widget for: its GameState, PlayerState, default Pawn, PlayerController,
        /// its own class and the subsystems, only those with Observables, observable lists or Commands.
        /// </summary>
        public static List<Type> WidgetTypesForHud(HUD hud, GameMode preview = null)
        {
            var set = BindingPreview.SetForHud(hud, preview);

            var types = new List<Type> { set.GameState, set.PlayerState, set.Pawn, set.PlayerController, set.HUD };

            // The project's own subsystems: those of test assemblies stay out of the menu.
            types.AddRange(set.WorldSubsystems.Concat(set.GameInstanceSubsystems).Where(type => !UEArchitectureSettingsProvider.IsTestType_Internal(type)).OrderBy(type => type.Name, StringComparer.Ordinal));

            return types.Where(type => type != null && HasBindableMembers(type)).Distinct().ToList();
        }

        /// <summary>
        /// True when a class has something a widget can show: a public Observable, IReadOnlyObservable, ObservableList,
        /// IReadOnlyObservableList or Command (property or field).
        /// </summary>
        public static bool HasBindableMembers(Type type)
        {
            if (type == null)
            {
                return false;
            }

            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (property.GetIndexParameters().Length == 0 && IsBindable_Internal(property.PropertyType))
                {
                    return true;
                }
            }

            return type.GetFields(BindingFlags.Instance | BindingFlags.Public).Any(field => IsBindable_Internal(field.FieldType));
        }

        /// <summary>
        /// Add Widget for ▾ in a HUD: a widget for <paramref name="viewModelType"/> (the GameState, PlayerState, a
        /// subsystem...) inside the HUD's prefab. An existing Widget&lt;T&gt; (or <paramref name="widgetType"/>) is added and
        /// filled at once (Generate UI or Create missing). Otherwise a thin widget class is written in
        /// <paramref name="folder"/> (ObserverVM's scripts folder by default) and the widget is added after the compile
        /// (the result is Pending; from the command line run "-uea complete-pending"). Running it again reuses the widget.
        /// </summary>
        public static ActionResult AddHudWidget(HUD hud, Type viewModelType, Type widgetType = null, string folder = null)
        {
            using (var op = Scaffold.Begin($"Add {(viewModelType != null ? viewModelType.Name : "framework")} widget to HUD", "add-hud-widget"))
            {
                AddHudWidget_Internal(op, hud, viewModelType, widgetType, folder);

                return op.Result;
            }
        }

        /// <summary>
        /// Create Widget for This…: a widget for a framework class, <paramref name="placement"/> inside the HUD
        /// <paramref name="target"/> (default: the HUD of the project's default GameMode), under the Canvas
        /// <paramref name="target"/> with a Framework Binder, or as a prefab only. A class without Observables, observable
        /// lists or Commands gets a message saying what to add. A missing widget class is written first (one compile).
        /// </summary>
        public static ActionResult CreateWidgetFor(Type viewModelType, WidgetPlacement placement, Object target = null, string folder = null)
        {
            using (var op = Scaffold.Begin($"Create widget for {(viewModelType != null ? viewModelType.Name : "a framework class")}", "create-widget-for"))
            {
                if (viewModelType == null)
                {
                    op.Problem("Pick the framework class to show.");

                    return op.Result;
                }

                if (!HasBindableMembers(viewModelType))
                {
                    op.Problem($"{viewModelType.Name} has nothing a widget can show: add a public Observable<T> (\"public Observable<int> Score {{ get; }} = new(0);\"), an ObservableList<T> or a Command to it first.");

                    return op.Result;
                }

                switch (placement)
                {
                    case WidgetPlacement.InsideHud:
                    {
                        var hud = target is HUD direct ? direct : target is GameObject gameObject ? gameObject.GetComponent<HUD>() : null;

                        if (hud == null)
                        {
                            var settings = FrameworkIndex.Settings;

                            hud = settings != null && settings.DefaultGameMode != null ? settings.DefaultGameMode.HUDPrefab : null;
                        }

                        if (hud == null)
                        {
                            op.Problem("Pick the HUD to put the widget in: the project's default GameMode has no HUD.");

                            return op.Result;
                        }

                        AddHudWidget_Internal(op, hud, viewModelType, null, folder);

                        return op.Result;
                    }

                    case WidgetPlacement.Canvas:
                    {
                        var parent = target is Component component ? component.gameObject : target as GameObject;

                        if (parent == null)
                        {
                            op.Problem("Pick the Canvas (or an object under it) to put the widget under.");

                            return op.Result;
                        }

                        if (!op.CheckEditable(parent))
                        {
                            return op.Result;
                        }

                        var widgetClass = SingleWidgetClass_Internal(op, viewModelType, null);

                        if (widgetClass == null)
                        {
                            if (op.Result.Ok)
                            {
                                // No widget class yet: ObserverVM writes one and adds it under the Canvas after the compile;
                                // its creation hook adds the Framework Binder (Project Settings > UE Architecture > Editor).
                                Merge_Internal(op.Result, ObserverVMActions.AddWidgetForViewModel(viewModelType, parent, new AddWidgetOptions { Folder = folder }));
                            }

                            return op.Result;
                        }

                        var added = ObserverVMActions.AddWidgetForViewModel(viewModelType, parent, new AddWidgetOptions { WidgetType = widgetClass });

                        Merge_Internal(op.Result, added);

                        var widget = added.CreatedObjects.Concat(added.ChangedObjects).OfType<GameObject>().Select(created => created.GetComponent(widgetClass) as Widget).FirstOrDefault(found => found != null) ??
                                     parent.GetComponentsInChildren(widgetClass, true).OfType<Widget>().FirstOrDefault();

                        if (widget != null && BindingPreview.HudOf(widget) == null && !BindingPreview.BoundByParent(widget) && widget.GetComponent<FrameworkBinder>() == null)
                        {
                            AddBinders_Internal(op, widget.transform.root.gameObject, new List<Widget> { widget });
                        }

                        return op.Result;
                    }

                    default:
                    {
                        var widgetClass = SingleWidgetClass_Internal(op, viewModelType, null);

                        if (widgetClass != null)
                        {
                            Merge_Internal(op.Result, ObserverVMActions.CreateWidgetPrefab(widgetClass));
                        }
                        else if (op.Result.Ok)
                        {
                            Merge_Internal(op.Result, ObserverVMActions.CreateWidgetForViewModel(viewModelType, null, folder, true));
                        }

                        return op.Result;
                    }
                }
            }
        }

        /// <summary>
        /// The kit's "HUD with Widgets for Game State and Player State": a widget for each of the two classes that has
        /// Observables, observable lists or Commands.
        /// </summary>
        internal static void AddStateWidgets_Internal(ScaffoldOp op, GameObject hudPrefab, Type gameState, Type playerState)
        {
            var hud = hudPrefab != null ? hudPrefab.GetComponent<HUD>() : null;

            if (hud == null)
            {
                op.Result.AddNote("The HUD prefab has no HUD component: no widgets were added.");

                return;
            }

            var types = new[] { gameState, playerState }.Where(HasBindableMembers).Distinct().ToList();

            if (types.Count == 0)
            {
                op.Result.AddNote($"{(gameState != null ? gameState.Name : "The GameState")} and {(playerState != null ? playerState.Name : "the PlayerState")} have nothing to show yet " +
                                  "(no public Observable, ObservableList or Command): the HUD preset added no widgets.");

                return;
            }

            SavingPrefabFiles_Internal(() =>
            {
                foreach (var type in types)
                {
                    AddHudWidget_Internal(op, hud, type, null, null);
                }
            });
        }

        /// <summary>
        /// Runs ObserverVM's actions with prefab files loaded, changed and saved instead of opened in Prefab Mode: for work
        /// nobody is looking at (the kit's gesture, a step that finishes after a compile), whose change must be on disk when
        /// it reports Done.
        /// </summary>
        private static void SavingPrefabFiles_Internal(Action run)
        {
            var openPrefabMode = ObserverVMActions.OpenPrefabMode;

            ObserverVMActions.OpenPrefabMode = false;

            try
            {
                run();
            }
            finally
            {
                ObserverVMActions.OpenPrefabMode = openPrefabMode;
            }
        }

        internal static void AddHudWidget_Internal(ScaffoldOp op, HUD hud, Type viewModelType, Type widgetType, string folder)
        {
            if (hud == null)
            {
                op.Problem("Pick a HUD.");

                return;
            }

            if (viewModelType == null && widgetType == null)
            {
                op.Problem("Pick what the widget shows: the GameState, the PlayerState, a subsystem...");

                return;
            }

            if (!op.CheckEditable(hud))
            {
                return;
            }

            viewModelType ??= ViewModelCatalog.ViewModelOf(widgetType);

            var set = BindingPreview.SetForHud(hud);

            var prediction = BindingPreview.Predict(viewModelType, set);

            if (!prediction.Matches && !typeof(Pawn).IsAssignableFrom(viewModelType))
            {
                op.Result.AddNote($"No framework object of {(string.IsNullOrEmpty(set.GameModeName) ? "this HUD's GameMode" : set.GameModeName)} is a {viewModelType.Name}: the HUD will leave this widget unbound.");
            }

            var existing = widgetType ?? SingleWidgetClass_Internal(op, viewModelType, null);

            if (!op.Result.Ok)
            {
                return;
            }

            if (existing != null)
            {
                Merge_Internal(op.Result, ObserverVMActions.AddWidgetForViewModel(viewModelType, hud.gameObject, new AddWidgetOptions { WidgetType = existing }));

                op.Result.Result = hud;

                return;
            }

            WriteWidgetClassForHud_Internal(op, hud, viewModelType, folder);
        }

        /// <summary>
        /// Writes the thin widget class of <paramref name="viewModelType"/> and queues the step that adds it to the HUD
        /// after the compile (once: a second run finds the file and the step).
        /// </summary>
        private static void WriteWidgetClassForHud_Internal(ScaffoldOp op, HUD hud, Type viewModelType, string folder)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                op.Problem("Leave Play mode first: the widget class is written and added after a compile, in Edit mode.");

                return;
            }

            var plan = CreateWidgetScripts.PlanForViewModel(string.IsNullOrWhiteSpace(folder) ? ObserverVMSettings.instance.ScriptsFolder : folder, null, viewModelType, false);

            if (plan.Problem != null)
            {
                op.Problem(plan.Problem);

                return;
            }

            var queued = PendingSteps.All().Any(step => step.kind == HudWidgetKind && step.scriptPaths.Contains(plan.WidgetPath) && step.status != PendingStatus.Done);

            if (plan.AlreadyExists)
            {
                // A file of that name that declares another class: no compile will bring the widget class, and a queued step
                // would wait for it until it times out.
                var declared = ClassOfScript_Internal?.Invoke(plan.WidgetPath);

                if (declared != null && declared.FullName != plan.WidgetFullName)
                {
                    op.Problem($"{plan.WidgetPath} exists but declares {declared.FullName}, not {plan.WidgetFullName}: rename or move that file, or pick another folder (folder=Assets/...).");

                    return;
                }

                if (queued)
                {
                    op.Result.Pending = true;

                    op.Result.Message = $"{plan.WidgetPath} is written; the widget is added to {hud.gameObject.name} after the compile.";

                    return;
                }

                // The file exists but its class is not compiled: queue the step, the next compile finishes it.
                if (!Queue_Internal(op, hud, viewModelType, plan))
                {
                    return;
                }

                op.Result.Message = $"{plan.WidgetPath} exists; the widget is added to {hud.gameObject.name} after the compile.";

                return;
            }

            if (!PendingSteps.TryTarget(hud, string.Empty, op.Result, out _))
            {
                return;
            }

            var written = CreateWidgetScripts.Write(plan);

            Merge_Internal(op.Result, written);

            if (!written.Ok)
            {
                return;
            }

            if (Queue_Internal(op, hud, viewModelType, plan))
            {
                op.Result.Message = $"Wrote {plan.WidgetPath}; the widget is added to {hud.gameObject.name} after the compile.";
            }
        }

        private static bool Queue_Internal(ScaffoldOp op, HUD hud, Type viewModelType, WidgetScriptPlan plan)
        {
            if (!PendingSteps.TryTarget(hud, string.Empty, op.Result, out var target))
            {
                return false;
            }

            var step = PendingStep.Create(HudWidgetKind).WithScript(plan.WidgetPath, plan.WidgetFullName).WithArg("viewModel", viewModelType.AssemblyQualifiedName);

            step.targets.Add(target);

            return PendingSteps.Enqueue(step, op.Result);
        }

        [PendingStepHandler(HudWidgetKind)]
        internal static void FinishHudWidget_Internal(PendingStepContext context)
        {
            var widgetType = context.Classes.FirstOrDefault();

            if (widgetType == null || !typeof(Widget).IsAssignableFrom(widgetType))
            {
                context.Op.Problem($"{context.Step.expectedClasses.FirstOrDefault()} is not a widget class.");

                return;
            }

            foreach (var target in context.Step.targets)
            {
                var found = context.ResolveTarget(target);

                if (found == null)
                {
                    return;
                }

                var hud = found as HUD ?? (found as GameObject)?.GetComponent<HUD>();

                if (hud == null)
                {
                    context.Op.Problem($"{found.name} has no HUD any more.");

                    return;
                }

                SavingPrefabFiles_Internal(() => AddHudWidget_Internal(context.Op, hud, ViewModelCatalog.ViewModelOf(widgetType), widgetType, null));
            }
        }

        /// <summary>
        /// The one widget class written for a ViewModel, or null when there is none (a problem when there are several).
        /// </summary>
        private static Type SingleWidgetClass_Internal(ScaffoldOp op, Type viewModelType, Type widgetType)
        {
            if (widgetType != null)
            {
                return widgetType;
            }

            var classes = ViewModelCatalog.WidgetsFor(viewModelType);

            if (classes.Count > 1)
            {
                op.Problem($"{viewModelType.Name} has {classes.Count} widget classes ({string.Join(", ", classes.Select(type => type.Name))}): pick one (widget=<class>).");

                return null;
            }

            return classes.FirstOrDefault();
        }

        /// <summary>
        /// Adds a Framework Binder to each widget, all under <paramref name="root"/>: in place for a level object or the
        /// Prefab Mode copy (an override, labelled so, on a prefab placed there; the prefab itself with
        /// <paramref name="target"/> Prefab), through the prefab for a prefab asset (opened in Prefab Mode in the Editor,
        /// changed and saved headless).
        /// </summary>
        private static void AddBinders_Internal(ScaffoldOp op, GameObject root, List<Widget> widgets, int playerIndex = 0, BinderTarget target = BinderTarget.Instance)
        {
            if (widgets.Count == 0)
            {
                return;
            }

            if (EditorUtility.IsPersistent(root))
            {
                AddInPrefab_Internal(op, root.transform.root.gameObject, EditIntent.ThisCopy, widgets.Select(widget => (widget, ActionResult.Describe(widget))).ToList(), playerIndex);

                return;
            }

            // Prefab target: the widgets of each prefab placed here, by the prefab they come from.
            var byPrefab = new Dictionary<GameObject, List<(Widget Widget, string Label)>>();

            foreach (var widget in widgets)
            {
                var copy = PrefabUtility.IsPartOfPrefabInstance(widget);

                if (target == BinderTarget.Prefab && copy)
                {
                    var source = PrefabUtility.GetCorrespondingObjectFromSource(widget);

                    var instanceRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(widget);

                    if (source == null || instanceRoot == null)
                    {
                        op.Problem($"{ActionResult.Describe(widget)} was added to this copy, so its prefab does not have it. Apply it to the prefab first.");

                        continue;
                    }

                    if (!byPrefab.TryGetValue(instanceRoot, out var list))
                    {
                        list = new List<(Widget, string)>();

                        byPrefab.Add(instanceRoot, list);
                    }

                    list.Add((source, ActionResult.Describe(source) + " (in the prefab: every copy)"));

                    continue;
                }

                var label = ActionResult.Describe(widget) + (copy ? " (override on this copy of the prefab)" : string.Empty);

                op.Edit(widget, EditIntent.ThisCopy, edited => AddBinder_Internal(op, (Widget)edited, label, playerIndex));
            }

            foreach (var pair in byPrefab)
            {
                AddInPrefab_Internal(op, pair.Key, EditIntent.SourcePrefab, pair.Value, playerIndex);
            }
        }

        /// <summary>
        /// Adds the binders inside one prefab: <paramref name="editTarget"/> is its root (with ThisCopy) or the root of a
        /// copy placed in a level (with SourcePrefab); <paramref name="widgets"/> are objects of the prefab file. Each is
        /// found in the copy being edited by its place among its siblings and its component order, so two children with the
        /// same name are told apart.
        /// </summary>
        private static void AddInPrefab_Internal(ScaffoldOp op, GameObject editTarget, EditIntent intent, List<(Widget Widget, string Label)> widgets, int playerIndex)
        {
            // Read before the edit: opening Prefab Mode can unload the prefab asset (Unity 6), and its components with it.
            // Same-named widgets would read alike in the result: their place among their siblings tells them apart.
            var repeated = new HashSet<string>(widgets.GroupBy(item => item.Label).Where(group => group.Count() > 1).Select(group => group.Key));

            var places = widgets.Select(item => (Place: WidgetPlace.Of(item.Widget),
                                                 Label: repeated.Contains(item.Label) ? $"{item.Label} (child {item.Widget.transform.GetSiblingIndex() + 1})" : item.Label)).ToList();

            op.Edit(editTarget, intent, copy =>
            {
                var copyRoot = (copy is Component component ? component.gameObject : (GameObject)copy).transform;

                foreach (var (place, label) in places)
                {
                    var widget = place.FindIn(copyRoot);

                    if (widget == null)
                    {
                        op.Problem($"Could not find {label} in its prefab.");

                        continue;
                    }

                    AddBinder_Internal(op, widget, label, playerIndex);
                }
            });
        }

        /// <summary>
        /// " (override on this copy of the prefab)" or " (in its prefab: every copy)" for a widget of a prefab placed in a
        /// level, for dry runs.
        /// </summary>
        private static string WhereNote_Internal(Widget widget, BinderTarget target)
        {
            if (widget == null || EditorUtility.IsPersistent(widget) || !PrefabUtility.IsPartOfPrefabInstance(widget))
            {
                return string.Empty;
            }

            return target == BinderTarget.Prefab ? " (in its prefab: every copy)" : " (override on this copy of the prefab)";
        }

        /// <summary>
        /// The HUD prefab that has <paramref name="root"/>'s prefab (a prefab file, or the one open in Prefab Mode) placed
        /// inside it, or null. Its HUD binds the widgets of that copy.
        /// </summary>
        internal static GameObject HudPrefabContaining_Internal(GameObject root)
        {
            if (root == null)
            {
                return null;
            }

            string path = null;

            if (EditorUtility.IsPersistent(root))
            {
                path = AssetDatabase.GetAssetPath(root);
            }
            else
            {
                var stage = PrefabStageUtility.GetPrefabStage(root);

                if (stage != null)
                {
                    path = stage.assetPath;
                }
            }

            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            foreach (var hudPrefab in FrameworkIndex.PrefabsWith(typeof(HUD)))
            {
                var hudPath = hudPrefab != null ? AssetDatabase.GetAssetPath(hudPrefab) : null;

                if (string.IsNullOrEmpty(hudPath) || hudPath == path)
                {
                    continue;
                }

                if (AssetDatabase.GetDependencies(hudPath, true).Contains(path))
                {
                    return hudPrefab;
                }
            }

            return null;
        }

        /// <summary>
        /// Where a widget is inside its prefab: the sibling index of each object from the root down, its name path and its
        /// position among the components of its class.
        /// </summary>
        private readonly struct WidgetPlace
        {
            private readonly int[] _indices;

            private readonly string _path;

            private readonly Type _type;

            private readonly int _position;

            private WidgetPlace(int[] indices, string path, Type type, int position)
            {
                _indices = indices;

                _path = path;

                _type = type;

                _position = position;
            }

            public static WidgetPlace Of(Widget widget)
            {
                var indices = new List<int>();

                for (var current = widget.transform; current.parent != null; current = current.parent)
                {
                    indices.Insert(0, current.GetSiblingIndex());
                }

                var type = widget.GetType();

                return new WidgetPlace(indices.ToArray(), Scaffold.RelativePath_Internal(widget.transform), type, Array.IndexOf(widget.GetComponents(type), widget));
            }

            /// <summary>
            /// The same widget under another copy of the prefab's root: by place, or by name path when the places differ (a
            /// Prefab Mode copy with unsaved moves). Null when it is not there.
            /// </summary>
            public Widget FindIn(Transform root)
            {
                var found = ByIndices_Internal(root);

                if (found == null || Scaffold.RelativePath_Internal(found) != _path)
                {
                    found = _path.Length == 0 ? root : root.Find(_path);
                }

                if (found == null)
                {
                    return null;
                }

                var candidates = found.GetComponents(_type);

                return _position >= 0 && _position < candidates.Length ? candidates[_position] as Widget : null;
            }

            private Transform ByIndices_Internal(Transform root)
            {
                var current = root;

                foreach (var index in _indices)
                {
                    if (index >= current.childCount)
                    {
                        return null;
                    }

                    current = current.GetChild(index);
                }

                return current;
            }
        }

        private static void AddBinder_Internal(ScaffoldOp op, Widget widget, string label, int playerIndex)
        {
            if (widget.GetComponent<FrameworkBinder>() != null)
            {
                return;
            }

            var gameObject = widget.gameObject;

            var undo = PrefabStageUtility.GetPrefabStage(gameObject) != null || gameObject.scene.IsValid() && !EditorSceneManager.IsPreviewScene(gameObject.scene);

            var binder = undo ? Undo.AddComponent<FrameworkBinder>(gameObject) : gameObject.AddComponent<FrameworkBinder>();

            if (binder == null)
            {
                op.Problem($"Unity could not add a Framework Binder to {label}.");

                return;
            }

            ScaffoldOp.ApplyDefaultPresets_Internal(binder);

            if (playerIndex > 0)
            {
                var serialized = new SerializedObject(binder);

                serialized.FindProperty("playerIndex").intValue = playerIndex;

                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            op.Touch(binder);

            op.Result.AddChanged($"{label}: + Framework Binder{(playerIndex > 0 ? $" (Player {playerIndex + 1})" : string.Empty)}");
        }

        /// <summary>
        /// Copies an ObserverVM result into a UE Architecture one.
        /// </summary>
        internal static void Merge_Internal(ActionResult into, OvmResult from)
        {
            if (into == null || from == null)
            {
                return;
            }

            foreach (var item in from.Created)
            {
                into.AddCreated(item);
            }

            foreach (var item in from.Changed)
            {
                into.AddChanged(item);
            }

            foreach (var item in from.Problems)
            {
                into.AddProblem(item);
            }

            foreach (var item in from.Notes)
            {
                into.AddNote(item);
            }

            into.Pending |= from.Pending;
        }

        /// <summary>
        /// A UE Architecture result as an ObserverVM one (for fixes shown in ObserverVM's Validation window).
        /// </summary>
        internal static OvmResult ToObserverVM_Internal(ActionResult from, string action)
        {
            var result = new OvmResult(action);

            if (from == null)
            {
                return result;
            }

            foreach (var item in from.Created)
            {
                result.AddCreated(item);
            }

            foreach (var item in from.Changed)
            {
                result.AddChanged(item);
            }

            foreach (var item in from.Problems)
            {
                result.AddProblem(item);
            }

            foreach (var item in from.Notes)
            {
                result.AddNote(item);
            }

            result.Pending = from.Pending;

            if (!string.IsNullOrEmpty(from.Message))
            {
                result.Message = from.Message;
            }

            return result;
        }

        private static bool IsBindable_Internal(Type type)
        {
            if (type == typeof(Command))
            {
                return true;
            }

            if (!type.IsGenericType)
            {
                return false;
            }

            var definition = type.GetGenericTypeDefinition();

            return definition == typeof(Command<>) || definition == typeof(Observable<>) || definition == typeof(IReadOnlyObservable<>) || definition == typeof(ObservableList<>) ||
                   definition == typeof(IReadOnlyObservableList<>);
        }
    }
}
