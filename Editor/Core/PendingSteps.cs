using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using System.Reflection;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// Marks a static method <c>void Handle(PendingStepContext context)</c> as the code that finishes pending steps of
    /// one kind, e.g. <c>[PendingStepHandler("uea.prefab-for-script")]</c>. Found with TypeCache, so other editor
    /// assemblies (the ObserverVM integration) can add kinds.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class PendingStepHandlerAttribute : Attribute
    {
        public PendingStepHandlerAttribute(string kind)
        {
            Kind = kind;
        }

        public string Kind { get; }
    }

    /// <summary>
    /// What a pending step handler gets: the step, its classes (now compiled, in the order of expectedClasses) and the
    /// Scaffold gesture it runs in.
    /// </summary>
    internal sealed class PendingStepContext
    {
        public PendingStepContext(PendingStep step, Type[] classes, ScaffoldOp op)
        {
            Step = step;

            Classes = classes ?? Type.EmptyTypes;

            Op = op;
        }

        public PendingStep Step { get; }

        public Type[] Classes { get; }

        public ScaffoldOp Op { get; }

        public bool IsWaiting { get; private set; }

        public string WaitMessage { get; private set; }

        /// <summary>
        /// Leaves the step for a later run, e.g. "Open Assets/Scenes/Boss.unity to finish".
        /// </summary>
        public void KeepWaiting(string message)
        {
            IsWaiting = true;

            WaitMessage = message;
        }

        /// <summary>
        /// A target object of the step, or null. When its scene is closed the step keeps waiting with a message saying which
        /// scene to open; when it is gone a problem is added.
        /// </summary>
        public Object ResolveTarget(PendingTarget target)
        {
            var found = EditorCompat.FromObjectKey(target.objectKey);

            if (found != null)
            {
                return found;
            }

            var path = AssetDatabase.GUIDToAssetPath(EditorCompat.AssetGuidOfObjectKey(target.objectKey));

            if (EditorCompat.IsSceneObjectKey(target.objectKey) && !string.IsNullOrEmpty(path) && !SceneManager.GetSceneByPath(path).isLoaded)
            {
                KeepWaiting($"Open {path} to finish (it holds the object to fill).");

                return null;
            }

            Op.Problem($"The object to fill ({(string.IsNullOrEmpty(path) ? "unknown" : path)}, field \"{target.propertyPath}\") no longer exists.");

            return null;
        }
    }

    /// <summary>
    /// What <see cref="PendingSteps"/> needs to know about the project to decide whether a step can run.
    /// </summary>
    internal readonly struct PendingEnvironment
    {
        public readonly DateTime DomainLoadedUtc;

        public readonly bool CompilationFailed;

        public readonly Func<string, bool> ScriptExists;

        public readonly Func<string, Type> FindClass;

        public readonly Func<string, Type> ClassOfScript;

        public readonly IReadOnlyDictionary<string, string> CompileErrors;

        public PendingEnvironment(DateTime domainLoadedUtc, bool compilationFailed, Func<string, bool> scriptExists, Func<string, Type> findClass, Func<string, Type> classOfScript,
                                  IReadOnlyDictionary<string, string> compileErrors)
        {
            DomainLoadedUtc = domainLoadedUtc;

            CompilationFailed = compilationFailed;

            ScriptExists = scriptExists;

            FindClass = findClass;

            ClassOfScript = classOfScript;

            CompileErrors = compileErrors ?? new Dictionary<string, string>();
        }

        /// <summary>
        /// The real project.
        /// </summary>
        public static PendingEnvironment Current => new(PendingStepsHooks.DomainLoadedUtc, EditorUtility.scriptCompilationFailed,
                                                        path => AssetDatabase.LoadAssetAtPath<MonoScript>(path) != null,
                                                        PendingSteps.FindClass,
                                                        path => AssetDatabase.LoadAssetAtPath<MonoScript>(path)?.GetClass(),
                                                        PendingStepsHooks.CompileErrors);
    }

    /// <summary>
    /// Whether a step can run now, and if not why.
    /// </summary>
    internal readonly struct PendingEvaluation
    {
        public readonly bool Ready;

        public readonly PendingStatus Status;

        public readonly string Message;

        public readonly Type[] Classes;

        public PendingEvaluation(bool ready, PendingStatus status, string message, Type[] classes)
        {
            Ready = ready;

            Status = status;

            Message = message;

            Classes = classes;
        }
    }

    /// <summary>
    /// Work that finishes after a script compile (create a prefab for a class that does not exist yet, then assign it).
    /// Steps run by themselves after the domain reload, in dependency order, each in its own Undo step; from the command
    /// line, <c>-uea complete-pending</c> runs them. Steps expire after 24 hours.
    /// </summary>
    public static class PendingSteps
    {
        private static Dictionary<string, Action<PendingStepContext>> _handlers;

        private static bool _running;

        private static bool _scheduled;

        internal static readonly PendingStepStore Store = new(PendingStepStore.DefaultPath);

        /// <summary>
        /// How many steps wait for a compile or for the user. Cheap enough for the UI: the file is read again only when
        /// it changes.
        /// </summary>
        public static int Count => Store.Snapshot().Count;

        /// <summary>
        /// Runs every step that can run now (for -executeMethod and the command line's "complete-pending").
        /// </summary>
        public static ActionResult CompletePending()
        {
            return Run_Internal(Store, "complete-pending");
        }

        /// <summary>
        /// Every stored step, in run order.
        /// </summary>
        internal static List<PendingStep> All(PendingStepStore store = null)
        {
            return PendingStepStore.Order((store ?? Store).Load(), out _);
        }

        /// <summary>
        /// Stores a step to run after the next compile. Refused in Play mode, with a problem in <paramref name="result"/>.
        /// </summary>
        internal static bool Enqueue(PendingStep step, ActionResult result, PendingStepStore store = null)
        {
            store ??= Store;

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                result.AddProblem("Leave Play mode first: work that waits for a compile is only queued in Edit mode.");

                return false;
            }

            if (step == null || string.IsNullOrEmpty(step.kind))
            {
                result.AddProblem("The step to queue has no kind.");

                return false;
            }

            var steps = store.Load();

            steps.RemoveAll(other => other.id == step.id);

            step.sequence = steps.Count == 0 ? 1 : steps.Max(other => other.sequence) + 1;

            if (string.IsNullOrEmpty(step.createdUtc))
            {
                step.CreatedUtc = DateTime.UtcNow;
            }

            step.status = PendingStatus.Waiting;

            steps.Add(step);

            store.Save(steps);

            result.Pending = true;

            result.AddNote($"Queued \"{step.kind}\" ({step.id}): {step.Describe()}");

            return true;
        }

        /// <summary>
        /// Removes a step (and the steps that depend on it). Files it already wrote stay.
        /// </summary>
        internal static ActionResult Cancel(string id, PendingStepStore store = null)
        {
            store ??= Store;

            var result = new ActionResult("cancel-pending");

            var steps = store.Load();

            var removed = new HashSet<string>(StringComparer.Ordinal) { id ?? string.Empty };

            var grew = true;

            while (grew)
            {
                var before = removed.Count;

                foreach (var step in steps.Where(step => step.dependsOn.Any(removed.Contains)))
                {
                    removed.Add(step.id);
                }

                grew = removed.Count > before;
            }

            var count = steps.RemoveAll(step => removed.Contains(step.id));

            if (count == 0)
            {
                result.AddProblem($"No pending step \"{id}\".");

                return result;
            }

            store.Save(steps);

            result.Message = count == 1 ? "Cancelled 1 step." : $"Cancelled {count} steps.";

            return result;
        }

        /// <summary>
        /// A target for a step: the object (its prefab asset when it is shown in Prefab Mode) and the field to fill.
        /// Refused for objects in a scene that was never saved, which cannot be found again after a reload.
        /// </summary>
        internal static bool TryTarget(Object owner, string propertyPath, ActionResult result, out PendingTarget target)
        {
            target = default;

            owner = Scaffold.Persistent(owner);

            if (owner == null)
            {
                result.AddProblem("There is no object to fill after the compile.");

                return false;
            }

            if (EditorCompat.IsInUnsavedScene(owner))
            {
                result.AddProblem($"Save the scene first: {owner.name} is in a scene that was never saved, so it cannot be found again after the compile.");

                return false;
            }

            var key = EditorCompat.ObjectKey(owner);

            if (string.IsNullOrEmpty(key))
            {
                result.AddProblem($"{owner.name} cannot be found again after a compile (it is not saved in a scene or an asset).");

                return false;
            }

            target = new PendingTarget { objectKey = key, propertyPath = propertyPath ?? string.Empty };

            return true;
        }

        /// <summary>
        /// The class with exactly this full name ("Game.ArenaPawn", or "ArenaPawn" without a namespace) in any loaded
        /// assembly, or null.
        /// </summary>
        public static Type FindClass(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
            {
                return null;
            }

            foreach (var assembly in EditorCompat.LoadedAssemblies())
            {
                var type = assembly.GetType(fullName, false);

                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        /// <summary>
        /// Whether a step can run now: its scripts exist, have no compile errors and declare the expected classes.
        /// </summary>
        internal static PendingEvaluation Evaluate(PendingStep step, PendingEnvironment environment)
        {
            var classes = new Type[step.expectedClasses.Length];

            foreach (var script in step.scriptPaths)
            {
                var file = Path.GetFileName(script);

                if (!environment.ScriptExists(script))
                {
                    return new PendingEvaluation(false, PendingStatus.Blocked, $"{file} was deleted ({script}). Put it back or cancel this step.", classes);
                }

                if (environment.CompileErrors.TryGetValue(script, out var error))
                {
                    return new PendingEvaluation(false, PendingStatus.Blocked, $"{file} has compile errors: {error}", classes);
                }
            }

            for (var i = 0; i < classes.Length; i++)
            {
                classes[i] = environment.FindClass(step.expectedClasses[i]);

                if (classes[i] != null)
                {
                    continue;
                }

                var script = i < step.scriptPaths.Length ? step.scriptPaths[i] : step.scriptPaths.LastOrDefault() ?? string.Empty;

                var file = Path.GetFileName(script);

                if (environment.CompilationFailed)
                {
                    return new PendingEvaluation(false, PendingStatus.Blocked, $"Waiting for {file}: the project has compile errors. Fix them in the Console.", classes);
                }

                if (step.CreatedUtc >= environment.DomainLoadedUtc)
                {
                    return new PendingEvaluation(false, PendingStatus.Waiting, $"Waiting for compile: {file}", classes);
                }

                var declared = string.IsNullOrEmpty(script) ? null : environment.ClassOfScript(script);

                var shortName = step.expectedClasses[i].Substring(step.expectedClasses[i].LastIndexOf('.') + 1);

                var found = declared != null ? $"it declares {declared.FullName}" : "Unity found no class in it";

                return new PendingEvaluation(false, PendingStatus.Blocked,
                                             $"{file} compiled, but {found}, not {step.expectedClasses[i]}. A component's file must be called like its class ({shortName}.cs).",
                                             classes);
            }

            return new PendingEvaluation(true, PendingStatus.Waiting, string.Empty, classes);
        }

        internal static ActionResult Run_Internal(PendingStepStore store, string verb, PendingEnvironment? environment = null)
        {
            var result = new ActionResult(verb);

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                result.AddProblem("Leave Play mode first: pending steps finish in Edit mode.");

                return result;
            }

            if (_running)
            {
                result.Pending = true;

                result.Message = "Pending steps are already running.";

                return result;
            }

            _running = true;

            try
            {
                var steps = store.Load();

                var known = new HashSet<string>(steps.Select(step => step.id), StringComparer.Ordinal);

                if (steps.Count == 0)
                {
                    result.Message = "Nothing pending.";

                    return result;
                }

                foreach (var expired in PendingStepStore.Expire(steps, DateTime.UtcNow, PendingStepStore.MaxAge))
                {
                    result.AddNote($"Dropped \"{expired.kind}\" ({expired.id}): it waited more than 24 hours.");
                }

                var ordered = PendingStepStore.Order(steps, out var inLoops);

                var env = environment ?? PendingEnvironment.Current;

                var finished = 0;

                var compileWaits = 0;

                var otherWaits = 0;

                foreach (var step in ordered)
                {
                    if (step.status == PendingStatus.Done)
                    {
                        continue;
                    }

                    if (inLoops.Contains(step))
                    {
                        step.status = PendingStatus.Blocked;

                        step.message = "This step and another one wait for each other. Cancel one of them.";

                        continue;
                    }

                    var waitingOn = step.dependsOn.Select(id => steps.FirstOrDefault(other => other.id == id)).FirstOrDefault(other => other != null && other.status != PendingStatus.Done);

                    if (waitingOn != null)
                    {
                        step.status = waitingOn.status == PendingStatus.Blocked ? PendingStatus.Blocked : PendingStatus.Waiting;

                        step.message = $"Waiting for \"{waitingOn.kind}\" ({waitingOn.id}).";

                        if (step.status == PendingStatus.Waiting)
                        {
                            otherWaits++;
                        }

                        continue;
                    }

                    var evaluation = Evaluate(step, env);

                    if (!evaluation.Ready)
                    {
                        step.status = evaluation.Status;

                        step.message = evaluation.Message;

                        if (step.status == PendingStatus.Waiting)
                        {
                            compileWaits++;
                        }

                        continue;
                    }

                    if (!Handlers_Internal().TryGetValue(step.kind, out var handler))
                    {
                        step.status = PendingStatus.Blocked;

                        step.message = $"No editor code finishes steps of kind \"{step.kind}\" (is the package that queued it still installed?).";

                        continue;
                    }

                    RunStep_Internal(step, evaluation.Classes, handler, result);

                    if (step.status == PendingStatus.Done)
                    {
                        finished++;
                    }
                    else if (step.status == PendingStatus.Waiting)
                    {
                        otherWaits++;
                    }
                }

                steps.RemoveAll(step => step.status == PendingStatus.Done);

                // A handler may queue a step of its own (a widget script written once the HUD exists): keep it.
                steps.AddRange(store.Load().Where(step => !known.Contains(step.id)));

                store.Save(steps);

                foreach (var step in steps.Where(step => step.status == PendingStatus.Blocked))
                {
                    result.AddProblem($"\"{step.kind}\" ({step.id}): {step.message}");
                }

                var waiting = steps.Count(step => step.status == PendingStatus.Waiting);

                result.Pending = waiting > 0;

                foreach (var step in steps.Where(step => step.status == PendingStatus.Waiting))
                {
                    result.AddNote($"\"{step.kind}\" ({step.id}): {step.Describe()}");
                }

                var blocked = steps.Count(step => step.status == PendingStatus.Blocked);

                result.Message = finished == 0 && waiting == 0 && blocked == 0 ? "Nothing pending." : Summary(finished, compileWaits, otherWaits, blocked);

                return result;
            }
            finally
            {
                _running = false;
            }
        }

        /// <summary>
        /// "Finished 1 step(s); 1 waits for a script compile; 2 wait for something else (a level to open, another step:
        /// see the notes); 1 blocked (see the problems)."
        /// </summary>
        internal static string Summary(int finished, int compileWaits, int otherWaits, int blocked)
        {
            var text = $"Finished {finished} step(s)";

            if (compileWaits > 0)
            {
                text += compileWaits == 1 ? "; 1 waits for a script compile" : $"; {compileWaits} wait for a script compile";
            }

            if (otherWaits > 0)
            {
                text += (otherWaits == 1 ? "; 1 waits" : $"; {otherWaits} wait") + " for something else (a level to open, another step: see the notes)";
            }

            if (blocked > 0)
            {
                text += $"; {blocked} blocked (see the problems)";
            }

            return text + ".";
        }

        /// <summary>
        /// True when a stored step was queued after the current script domain loaded: its scripts compile with the next
        /// domain reload, so running the steps now would only find them waiting.
        /// </summary>
        internal static bool WaitsForThisCompile_Internal(PendingStepStore store = null)
        {
            return (store ?? Store).Load().Any(step => step.status == PendingStatus.Waiting && step.scriptPaths.Length > 0 && step.CreatedUtc >= PendingStepsHooks.DomainLoadedUtc);
        }

        /// <summary>
        /// True when a waiting step fills an object of the scene at <paramref name="scenePath"/>, so opening that scene
        /// lets it finish.
        /// </summary>
        internal static bool WaitsForScene_Internal(string scenePath, PendingStepStore store = null)
        {
            store ??= Store;

            if (string.IsNullOrEmpty(scenePath) || !store.Exists)
            {
                return false;
            }

            foreach (var step in store.Snapshot())
            {
                if (step.status != PendingStatus.Waiting)
                {
                    continue;
                }

                foreach (var target in step.targets)
                {
                    if (EditorCompat.IsSceneObjectKey(target.objectKey) &&
                        string.Equals(AssetDatabase.GUIDToAssetPath(EditorCompat.AssetGuidOfObjectKey(target.objectKey)), scenePath, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// The scenes (paths) that hold objects the stored steps fill.
        /// </summary>
        internal static List<string> ScenesOfTargets_Internal(PendingStepStore store = null)
        {
            var scenes = new List<string>();

            foreach (var step in All(store))
            {
                foreach (var target in step.targets)
                {
                    if (!EditorCompat.IsSceneObjectKey(target.objectKey))
                    {
                        continue;
                    }

                    var path = AssetDatabase.GUIDToAssetPath(EditorCompat.AssetGuidOfObjectKey(target.objectKey));

                    if (!string.IsNullOrEmpty(path) && !scenes.Contains(path))
                    {
                        scenes.Add(path);
                    }
                }
            }

            return scenes;
        }

        private static void RunStep_Internal(PendingStep step, Type[] classes, Action<PendingStepContext> handler, ActionResult result)
        {
            using (var op = Scaffold.Begin("Finish " + step.kind, result.Verb))
            {
                var context = new PendingStepContext(step, classes, op);

                try
                {
                    handler(context);
                }
                catch (Exception exception)
                {
                    op.Problem($"{exception.GetType().Name}: {exception.Message}");

                    Debug.LogException(exception);
                }

                // A failed step is reported once, as a blocked step with its problems.
                result.Merge(op.Result, false);

                if (context.IsWaiting && op.Result.Ok)
                {
                    step.status = PendingStatus.Waiting;

                    step.message = context.WaitMessage;
                }
                else if (op.Result.Ok)
                {
                    step.status = PendingStatus.Done;

                    step.message = string.Empty;
                }
                else
                {
                    step.status = PendingStatus.Blocked;

                    step.message = string.Join(" ", op.Result.Problems);
                }
            }
        }

        private static Dictionary<string, Action<PendingStepContext>> Handlers_Internal()
        {
            if (_handlers != null)
            {
                return _handlers;
            }

            _handlers = new Dictionary<string, Action<PendingStepContext>>(StringComparer.Ordinal);

            foreach (var method in TypeCache.GetMethodsWithAttribute<PendingStepHandlerAttribute>())
            {
                var attribute = method.GetCustomAttribute<PendingStepHandlerAttribute>();

                var parameters = method.GetParameters();

                if (attribute == null || !method.IsStatic || parameters.Length != 1 || parameters[0].ParameterType != typeof(PendingStepContext))
                {
                    Debug.LogWarning($"UE Architecture: {method.DeclaringType?.FullName}.{method.Name} must be 'static void {method.Name}(PendingStepContext context)' to handle pending steps.");

                    continue;
                }

                _handlers[attribute.Kind] = (Action<PendingStepContext>)Delegate.CreateDelegate(typeof(Action<PendingStepContext>), method);
            }

            return _handlers;
        }

        /// <summary>
        /// Runs the steps once the Editor is idle (after a domain reload, or when it leaves Play mode).
        /// </summary>
        internal static void ScheduleRun_Internal()
        {
            if (_scheduled || !Store.Exists)
            {
                return;
            }

            _scheduled = true;

            EditorApplication.delayCall += RunScheduled_Internal;
        }

        private static void RunScheduled_Internal()
        {
            _scheduled = false;

            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                // Leaving Play mode or the next domain reload schedules the run again.
                return;
            }

            if (EditorApplication.isUpdating)
            {
                ScheduleRun_Internal();

                return;
            }

            if (UEArchitectureCli.WaitsToExit_Internal)
            {
                // A command-line run without -quit: finish like "complete-pending" (opening the levels it fills) and exit.
                UEArchitectureCli.FinishWaitingRun_Internal();

                return;
            }

            var result = CompletePending();

            LastCreatedFiles.Remember(result, true);

            if (result.Created.Count > 0 || result.Changed.Count > 0 || !result.Ok)
            {
                var summary = $"UE Architecture: {result.Message}" + (result.Ok ? string.Empty : "\n" + string.Join("\n", result.Problems));

                if (result.Ok)
                {
                    Debug.Log(summary);
                }
                else
                {
                    Debug.LogWarning(summary);
                }
            }

        }

        /// <summary>
        /// Marks the steps whose scripts failed to compile as blocked, with the first error.
        /// </summary>
        internal static void MarkCompileErrors_Internal(IReadOnlyDictionary<string, string> errors)
        {
            if (errors.Count == 0 || !Store.Exists)
            {
                return;
            }

            var steps = Store.Load();

            var changed = false;

            foreach (var step in steps)
            {
                foreach (var script in step.scriptPaths)
                {
                    if (!errors.TryGetValue(script, out var error))
                    {
                        continue;
                    }

                    step.status = PendingStatus.Blocked;

                    step.message = $"{Path.GetFileName(script)} has compile errors: {error}";

                    changed = true;

                    break;
                }
            }

            if (changed)
            {
                Store.Save(steps);
            }
        }
    }

    /// <summary>
    /// Hooks the pending steps into the Editor: domain loads, compiles and Play mode.
    /// </summary>
    [InitializeOnLoad]
    internal static class PendingStepsHooks
    {
        private static readonly Dictionary<string, string> _compileErrors = new(StringComparer.OrdinalIgnoreCase);

        static PendingStepsHooks()
        {
            DomainLoadedUtc = DateTime.UtcNow;

            CompilationPipeline.compilationStarted -= OnCompilationStarted_Internal;

            CompilationPipeline.compilationStarted += OnCompilationStarted_Internal;

            CompilationPipeline.assemblyCompilationFinished -= OnAssemblyCompiled_Internal;

            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompiled_Internal;

            CompilationPipeline.compilationFinished -= OnCompilationFinished_Internal;

            CompilationPipeline.compilationFinished += OnCompilationFinished_Internal;

            EditorApplication.playModeStateChanged -= OnPlayModeChanged_Internal;

            EditorApplication.playModeStateChanged += OnPlayModeChanged_Internal;

            EditorSceneManager.sceneOpened -= OnSceneOpened_Internal;

            EditorSceneManager.sceneOpened += OnSceneOpened_Internal;

            PendingSteps.ScheduleRun_Internal();
        }

        /// <summary>
        /// When the current script domain was loaded: classes of scripts written before this have been compiled.
        /// </summary>
        public static DateTime DomainLoadedUtc { get; }

        /// <summary>
        /// The first compile error of each script in the last compile (project-relative paths).
        /// </summary>
        public static IReadOnlyDictionary<string, string> CompileErrors => _compileErrors;

        private static void OnCompilationStarted_Internal(object context)
        {
            _compileErrors.Clear();
        }

        private static void OnAssemblyCompiled_Internal(string assemblyPath, CompilerMessage[] messages)
        {
            var projectRoot = Path.GetDirectoryName(Application.dataPath)?.Replace('\\', '/') + "/";

            foreach (var message in messages)
            {
                if (message.type != CompilerMessageType.Error || string.IsNullOrEmpty(message.file))
                {
                    continue;
                }

                var file = message.file.Replace('\\', '/');

                if (file.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
                {
                    file = file.Substring(projectRoot.Length);
                }

                if (!_compileErrors.ContainsKey(file))
                {
                    _compileErrors[file] = $"line {message.line}: {message.message}";
                }
            }
        }

        private static void OnCompilationFinished_Internal(object context)
        {
            PendingSteps.MarkCompileErrors_Internal(_compileErrors);

            if (_compileErrors.Count > 0)
            {
                UEArchitectureCli.OnCompileFailed_Internal();
            }
        }

        private static void OnPlayModeChanged_Internal(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredEditMode)
            {
                PendingSteps.ScheduleRun_Internal();
            }
        }

        /// <summary>
        /// A step that waited for a level to be opened ("Open Boss.unity to finish") finishes once it is.
        /// </summary>
        private static void OnSceneOpened_Internal(Scene scene, OpenSceneMode mode)
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode && PendingSteps.WaitsForScene_Internal(scene.path))
            {
                PendingSteps.ScheduleRun_Internal();
            }
        }
    }

    /// <summary>
    /// Runs the pending steps after a domain reload.
    /// </summary>
    internal sealed class PendingStepsPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths, bool didDomainReload)
        {
            if (didDomainReload)
            {
                PendingSteps.ScheduleRun_Internal();
            }
        }
    }
}
