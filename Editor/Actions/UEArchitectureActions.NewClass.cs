using System;
using System.IO;
using System.Linq;
using UnityEditor;
using System.Text.RegularExpressions;
using UnityEngine;
using System.Collections.Generic;
using Object = UnityEngine.Object;

namespace ModernWestern.UEArchitecture.EditorTools
{
    public static partial class UEArchitectureActions
    {
        /// <summary>
        /// Writes a new framework class, like Unreal's "Pick Parent Class" (Assets > Create > UE Architecture > Class…): a
        /// child of any framework class or of one of yours, a tickable subsystem with its [Tick], a GameMode with new or
        /// existing GameState and PlayerState classes, a SaveGame, a Message. With <see cref="ClassRequest.withPrefab"/>
        /// (components only) its prefab is built after the compile and, when <paramref name="assignTo"/> is given, put into
        /// its <paramref name="propertyPath"/>. A folder whose asmdef lacks a reference the code needs is refused, unless
        /// <paramref name="fixReferences"/> adds it. One compile. Running it again writes nothing: the class exists (its
        /// prefab and field are finished if missing), or it already waits for its compile.
        /// </summary>
        public static ActionResult CreateClass(ClassRequest request, Object assignTo = null, string propertyPath = null, bool fixReferences = false)
        {
            const string verb = "create-class";

            if (request == null)
            {
                return ActionResult.Fail(verb, "Describe the class to create.");
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return ActionResult.Fail(verb, "Leave Play mode first: new scripts compile in Edit mode.");
            }

            if (assignTo == null && !string.IsNullOrEmpty(request.assignToKey))
            {
                assignTo = EditorCompat.FromObjectKey(request.assignToKey);

                propertyPath = string.IsNullOrEmpty(propertyPath) ? request.assignToProperty : propertyPath;
            }

            var scriptRequest = ScriptRequestOf_Internal(request, out var problem);

            if (scriptRequest == null)
            {
                return ActionResult.Fail(verb, problem);
            }

            var parent = scriptRequest.Parent;

            var withPrefab = request.withPrefab || assignTo != null;

            if (withPrefab && (parent == null || !typeof(Component).IsAssignableFrom(parent)))
            {
                return ActionResult.Fail(verb, $"{(parent != null ? parent.Name : "A Message")} is not a component, so it has no prefab. Leave \"Also create the prefab\" off.");
            }

            // Run again: the class is written (and maybe compiled). Finish what is missing, or say what it waits for.
            var written = WrittenClass_Internal(scriptRequest);

            if (written != null)
            {
                return Existing_Internal(verb, written, parent, withPrefab, assignTo, propertyPath);
            }

            var plan = ScriptWriter.Plan(new[] { scriptRequest });

            var main = plan.Scripts.FirstOrDefault();

            var fixedReferences = new ActionResult(verb);

            // Other problems first: the asmdef is changed only when the class will be written.
            var others = plan.Problems.Where(item => plan.MissingReferences.All(missing => missing.Problem != item)).ToList();

            if (others.Count > 0 || plan.Scripts.Count == 0)
            {
                var failed = new ActionResult(verb);

                foreach (var item in others.DefaultIfEmpty("Nothing to write."))
                {
                    failed.AddProblem(item);
                }

                return failed;
            }

            if (plan.MissingReferences.Count > 0)
            {
                if (!fixReferences)
                {
                    var refused = new ActionResult(verb);

                    foreach (var item in plan.Problems)
                    {
                        refused.AddProblem(item);
                    }

                    refused.AddNote("Add the missing references with fix-references=true (the window's \"Add reference\" button), or pick a folder whose assembly has them.");

                    return refused;
                }

                fixedReferences = AsmdefReferences.Apply(plan.MissingReferences);

                if (!fixedReferences.Ok)
                {
                    return fixedReferences;
                }

                plan.Problems.RemoveAll(item => plan.MissingReferences.Any(missing => missing.Problem == item));
            }

            if (!plan.Ok)
            {
                var failed = new ActionResult(verb);

                foreach (var item in plan.Problems.DefaultIfEmpty("Nothing to write."))
                {
                    failed.AddProblem(item);
                }

                return failed;
            }

            PendingStep step = null;

            if (withPrefab)
            {
                var prefabPath = GeneratorNaming.PrefabPath(main.ClassName, parent, UEArchitectureEditorSettings.Current);

                step = PendingStep.Create(PrefabForScriptKind).WithScript(main.Path, main.FullName).WithArg("prefabPath", prefabPath);

                if (assignTo != null)
                {
                    var preflight = new ActionResult(verb);

                    if (!PendingSteps.TryTarget(assignTo, propertyPath, preflight, out var target))
                    {
                        return preflight;
                    }

                    step.targets.Add(target);
                }
            }

            var result = ScriptWriter.Write(plan, step != null ? new[] { step } : null, verb);

            result.Merge(fixedReferences);

            if (step != null && result.Ok)
            {
                result.AddNote($"After the compile: {step.Arg("prefabPath")}{(assignTo != null ? $" goes into {propertyPath} of {ActionResult.Describe(assignTo)}" : string.Empty)}.");
            }

            return result;
        }

        /// <summary>
        /// The script request for a class request, or null with the reason.
        /// </summary>
        internal static ScriptRequest ScriptRequestOf_Internal(ClassRequest request, out string problem)
        {
            problem = string.Empty;

            if (string.IsNullOrWhiteSpace(request.className))
            {
                problem = "Type a class name.";

                return null;
            }

            Type parent = null;

            CreateFrameworkScripts.Kind? kind = null;

            if (request.message)
            {
                kind = CreateFrameworkScripts.Kind.Message;
            }
            else
            {
                parent = request.Parent;

                if (parent == null)
                {
                    problem = string.IsNullOrWhiteSpace(request.parentClass)
                                  ? "Pick the class the new one derives from."
                                  : $"No class called \"{request.parentClass}\" can be a parent (is it compiled? use its full name when two classes share it).";

                    return null;
                }

                if (!ClassRequestTypes.CanBeParent(parent) && !ClassTemplates.IsFrameworkType(parent))
                {
                    problem = $"{parent.Name} cannot be a parent: pick a framework class or one of yours that derives from one (not sealed, not generic).";

                    return null;
                }

                if (request.tickable)
                {
                    if (parent == typeof(GameInstanceSubsystem))
                    {
                        kind = CreateFrameworkScripts.Kind.TickableGameInstanceSubsystem;
                    }
                    else if (parent == typeof(WorldSubsystem))
                    {
                        kind = CreateFrameworkScripts.Kind.TickableWorldSubsystem;
                    }
                    else
                    {
                        problem = "Only a Game Instance Subsystem or a World Subsystem can tick by itself. Turn Tickable off, or pick one of them.";

                        return null;
                    }
                }
            }

            Type gameState = null;

            Type playerState = null;

            if (parent == typeof(GameMode))
            {
                if (!StateClass_Internal(request.gameStateClass, typeof(GameState), out gameState, out problem) ||
                    !StateClass_Internal(request.playerStateClass, typeof(PlayerState), out playerState, out problem))
                {
                    return null;
                }
            }

            return new ScriptRequest(request.className, parent, request.folder)
            {
                Kind = kind,
                Tick = request.tickable ? new TickOptions(request.tickGroup, request.tickOrder, request.unscaled) : (TickOptions?)null,
                SplitStates = request.stateFiles == StateFiles.AsInSettings ? (bool?)null : request.stateFiles == StateFiles.OwnFiles,
                GameState = gameState,
                PlayerState = playerState
            };
        }

        private static bool StateClass_Internal(string name, Type baseType, out Type type, out string problem)
        {
            type = null;

            problem = string.Empty;

            if (string.IsNullOrWhiteSpace(name))
            {
                return true;
            }

            type = ClassRequestTypes.Find(name);

            if (type == null || !baseType.IsAssignableFrom(type) || type.IsAbstract || type.ContainsGenericParameters || type.GetConstructor(Type.EmptyTypes) == null)
            {
                problem = $"\"{name}\" is not a {ObjectNames.NicifyVariableName(baseType.Name)} class the GameMode can create (one with a constructor without parameters).";

                type = null;

                return false;
            }

            return true;
        }

        /// <summary>
        /// The script a request names when it is already written (its file exists where the request puts it), or null.
        /// </summary>
        private static PlannedScript WrittenClass_Internal(ScriptRequest request)
        {
            var kind = request.Kind ?? ClassTemplates.KindOf(request.Parent);

            var className = ClassTemplates.IsGameModeNamed(kind, request.Parent)
                                ? CreateFrameworkScripts.FileName(CreateFrameworkScripts.Kind.GameMode, request.TypedName)
                                : CreateFrameworkScripts.ClassName(request.TypedName);

            var folder = GeneratorNaming.CleanFolder(string.IsNullOrWhiteSpace(request.Folder) ? UEArchitectureEditorSettings.Current.ScriptsFolder : request.Folder);

            var path = $"{folder}/{className}.cs";

            if (!File.Exists(path) && AssetDatabase.LoadAssetAtPath<MonoScript>(path) == null)
            {
                return null;
            }

            var ns = UnityEditor.Compilation.CompilationPipeline.GetAssemblyRootNamespaceFromScriptPath(path) ?? string.Empty;

            return new PlannedScript { ClassName = className, Path = path, Namespace = ns, Parent = request.Parent };
        }

        /// <summary>
        /// The class's files already exist: when it is compiled its prefab and field are finished now (a second run
        /// creates nothing); when it waits for its compile the step that finishes it is queued once. A file there that
        /// holds another class is refused: nothing would ever compile into the class asked for.
        /// </summary>
        private static ActionResult Existing_Internal(string verb, PlannedScript main, Type parent, bool withPrefab, Object assignTo, string propertyPath)
        {
            var compiled = PendingSteps.FindClass(main.FullName);

            var other = compiled == null ? OtherClassIn_Internal(main) : null;

            if (other != null)
            {
                return ActionResult.Fail(verb, $"{main.Path} already exists with another class ({other}). Pick another name or folder.");
            }

            if (compiled != null)
            {
                if (parent != null && !parent.IsAssignableFrom(compiled))
                {
                    return ActionResult.Fail(verb, $"{main.Path} already exists and its {compiled.Name} does not derive from {parent.Name}. Pick another name.");
                }

                if (!withPrefab)
                {
                    var exists = new ActionResult(verb) { Message = $"{compiled.Name} already exists ({main.Path})." };

                    exists.AddNote("Reused " + main.Path);

                    return exists;
                }

                using (var op = Scaffold.Begin($"Create {compiled.Name} prefab", verb))
                {
                    var prefab = CreateClassPrefab_Internal(op, compiled, null);

                    if (prefab != null && assignTo != null)
                    {
                        op.Assign(assignTo, propertyPath, prefab);
                    }

                    op.Result.AddNote("Reused " + main.Path);

                    op.Result.Message = op.Result.Created.Count + op.Result.Changed.Count == 0 ? $"{compiled.Name} and its prefab already exist." : $"{compiled.Name} already existed; finished its prefab.";

                    return op.Result;
                }
            }

            var result = new ActionResult(verb);

            var queued = PendingSteps.All().Any(step => step.scriptPaths.Contains(main.Path));

            if (!queued && withPrefab)
            {
                var step = PendingStep.Create(PrefabForScriptKind).WithScript(main.Path, main.FullName)
                                      .WithArg("prefabPath", GeneratorNaming.PrefabPath(main.ClassName, parent, UEArchitectureEditorSettings.Current));

                if (assignTo != null)
                {
                    if (!PendingSteps.TryTarget(assignTo, propertyPath, result, out var target))
                    {
                        return result;
                    }

                    step.targets.Add(target);
                }

                PendingSteps.Enqueue(step, result);
            }

            result.Pending = withPrefab || queued;

            result.AddNote($"{main.Path} is already written and waits for the compile.");

            result.Message = $"{Path.GetFileName(main.Path)} is already written; the rest finishes after the compile.";

            return result;
        }

        /// <summary>
        /// The class an existing script holds instead of the one a request names, or null when it is the requested class
        /// (compiled, or written and waiting for its compile). Compiled: the class Unity found in the file. Not compiled:
        /// the file declares no class or struct with the requested name.
        /// </summary>
        private static string OtherClassIn_Internal(PlannedScript main)
        {
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(main.Path);

            var found = script != null ? script.GetClass() : null;

            if (found != null)
            {
                return found.FullName == main.FullName ? null : found.FullName;
            }

            string source;

            try
            {
                source = File.ReadAllText(main.Path);
            }
            catch (Exception)
            {
                return null;
            }

            if (Regex.IsMatch(source, $@"\b(class|struct)\s+{Regex.Escape(main.ClassName)}\b"))
            {
                return null;
            }

            var declared = Regex.Match(source, @"\b(?:class|struct)\s+([A-Za-z_][A-Za-z0-9_]*)");

            return declared.Success ? declared.Groups[1].Value : "no class " + main.ClassName;
        }
    }
}
