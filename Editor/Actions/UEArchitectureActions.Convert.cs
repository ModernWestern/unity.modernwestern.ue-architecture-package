using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ModernWestern.UEArchitecture.EditorTools
{
    public static partial class UEArchitectureActions
    {
        /// <summary>
        /// Pending step kind: after a converted class was renamed and compiled, rename the objects named after the old
        /// class ("GameplayScene" becomes "GameplayGameMode") in the scenes and prefabs that use it.
        /// </summary>
        public const string RenameObjectsKind = "uea.rename-objects";

        /// <summary>
        /// Convert to framework class: turns a MonoBehaviour script into a <paramref name="frameworkBase"/> (GameMode,
        /// PlayerController, AIController, Pawn, HUD, PlayerStart, LoadingScreen or GameInstance) by editing the one line
        /// that names its base class. A GameMode gets GameMode&lt;GameState, PlayerState&gt; with the classes named in
        /// <paramref name="gameState"/> and <paramref name="playerState"/>: existing ones, or new scripts written next to it
        /// (empty = named after the class; <paramref name="states"/> false = a plain GameMode). With
        /// <paramref name="newClassName"/> the class and its file are renamed too (the file keeps its GUID, so every scene
        /// and prefab keeps the component) and, after the compile, the objects named after the old class are renamed.
        /// <para>A dry run (the default) only says what would change; nothing is written. Applying is all or nothing, and
        /// Undo cannot take back script edits.</para>
        /// </summary>
        public static ActionResult ConvertToFrameworkClass(MonoScript script, Type frameworkBase, string newClassName = null, string gameState = null, string playerState = null,
                                                           bool states = true, bool dryRun = true)
        {
            var result = new ActionResult("convert-class");

            var request = ConvertRequestFor_Internal(script, frameworkBase, result);

            if (request == null)
            {
                return result;
            }

            request.NewClassName = newClassName ?? string.Empty;

            request.States = states;

            request.GameState = gameState ?? string.Empty;

            request.PlayerState = playerState ?? string.Empty;

            var plan = ClassConversion.Plan(request);

            var usages = plan.Ok && !plan.AlreadyDone ? FrameworkUsages.Find(script) : null;

            return Convert_Internal(plan, usages, dryRun, result);
        }

        /// <summary>
        /// The conversion request of a compiled MonoBehaviour script, or null with the reason in <paramref name="result"/>
        /// (or its message, when the class already is a <paramref name="frameworkBase"/>).
        /// </summary>
        internal static ConvertRequest ConvertRequestFor_Internal(MonoScript script, Type frameworkBase, ActionResult result)
        {
            if (!ScriptOf_Internal(script, result, out var path, out var type))
            {
                return null;
            }

            if (frameworkBase != null && frameworkBase.IsAssignableFrom(type))
            {
                result.Result = script;

                result.Message = $"{type.Name} already is a {frameworkBase.Name}.";

                return null;
            }

            if (type.BaseType != typeof(MonoBehaviour))
            {
                result.AddProblem($"{type.Name} derives from {type.BaseType?.Name}, not directly from MonoBehaviour: convert {type.BaseType?.Name} instead, or change it by hand.");

                return null;
            }

            if (!ScriptText.TryRead(ClassConversion.PhysicalPath(path), path, out var text, out var problem))
            {
                result.AddProblem(problem);

                return null;
            }

            return new ConvertRequest
            {
                ScriptPath = path,
                Source = text.Text,
                SourceBytes = text.Bytes,
                Bom = text.Bom,
                CurrentType = type,
                ClassName = type.Name,
                Namespace = type.Namespace ?? string.Empty,
                CurrentBase = "MonoBehaviour",
                FrameworkBase = frameworkBase
            };
        }

        /// <summary>
        /// Writes (or with <paramref name="dryRun"/> only describes) a planned conversion. <paramref name="usages"/> are the
        /// scenes and prefabs whose objects are renamed after the compile, when the class is renamed.
        /// </summary>
        internal static ActionResult Convert_Internal(ConvertPlan plan, ScriptUsages usages, bool dryRun, ActionResult result = null)
        {
            result ??= new ActionResult("convert-class");

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                result.AddProblem("Leave Play mode first: converting a class edits its script, and the compile waits for Edit mode.");

                return result;
            }

            if (plan == null)
            {
                result.AddProblem("Nothing to convert.");

                return result;
            }

            if (!plan.Ok)
            {
                foreach (var problem in plan.Problems)
                {
                    result.AddProblem(problem);
                }

                return result;
            }

            if (plan.AlreadyDone)
            {
                result.Message = $"{plan.ClassName} already derives from {plan.FrameworkBase.Name} in its code (the change may be waiting for the compile).";

                return result;
            }

            foreach (var line in plan.Describe())
            {
                result.AddNote(line);
            }

            if (usages != null)
            {
                result.AddNote(usages.Summary());

                foreach (var line in usages.Lines())
                {
                    result.AddNote("  " + line);
                }
            }

            var what = $"{plan.ClassName} to a {plan.FrameworkBase.Name}" + (plan.Renames ? $" called {plan.NewClassName}" : string.Empty);

            if (dryRun)
            {
                result.Message = $"Preview: converts {what}. Nothing was written (pass apply=true, or press Apply, to write it).";

                return result;
            }

            if (!ClassConversion.Apply(plan, result))
            {
                return result;
            }

            if (plan.Renames && usages != null && usages.Count > 0)
            {
                var step = PendingStep.Create(RenameObjectsKind)
                                      .WithScript(plan.NewScriptPath, plan.FullName(plan.NewClassName))
                                      .WithArg("oldName", plan.ClassName)
                                      .WithArg("newName", plan.NewClassName)
                                      .WithArg("scenes", string.Join(";", usages.Scenes))
                                      .WithArg("prefabs", string.Join(";", usages.Prefabs));

                PendingSteps.Enqueue(step, result);
            }

            AssetDatabase.Refresh();

            FrameworkIndex.Invalidate();

            result.Message = $"Converted {what}." + (result.Pending ? " The objects named after the old class are renamed after the compile." : " It takes effect after the compile.") +
                             (plan.BaseCalls.Count > 0 ? " Some methods need a base call: see the notes." : string.Empty);

            return result;
        }

        /// <summary>
        /// Find Usages: the scenes and prefabs under <paramref name="folder"/> (all of Assets/ when empty) that use the
        /// class of <paramref name="script"/>, on their own objects or through a prefab. Reads files only.
        /// </summary>
        public static ActionResult FindUsages(MonoScript script, string folder = null)
        {
            var result = new ActionResult("find-usages");

            if (script == null)
            {
                result.AddProblem("Pick a script.");

                return result;
            }

            var usages = FrameworkUsages.Find(script, string.IsNullOrEmpty(folder) ? null : new[] { GeneratorNaming.CleanFolder(folder) });

            foreach (var line in usages.Lines())
            {
                result.AddNote(line);
            }

            result.Result = script;

            result.Message = $"{script.name}: {usages.Summary()}";

            return result;
        }

        /// <summary>
        /// Change Class: swaps the class of a component for <paramref name="newType"/> in place (its script reference), on an
        /// object of a level or a prefab asset, where the component was added (not one a prefab instance or a variant
        /// inherits). Fields both classes have keep their values; the others are lost and listed. A dry run (the default)
        /// only says what would change. Unity's Undo cannot swap a class back, so the change is not on the Undo stack: Change
        /// Class again restores the class, but the lost fields start empty.
        /// </summary>
        public static ActionResult ChangeComponentClass(Component component, Type newType, bool dryRun = true)
        {
            using (var op = Scaffold.Begin($"Change class to {newType?.Name ?? "?"}", "change-class"))
            {
                if (component == null)
                {
                    op.Problem("Pick a component.");

                    return op.Result;
                }

                if (newType == null || !typeof(MonoBehaviour).IsAssignableFrom(newType) || newType.IsAbstract || newType.ContainsGenericParameters)
                {
                    op.Problem($"{newType?.Name ?? "Nothing"} is not a class that can be added to an object.");

                    return op.Result;
                }

                var oldType = component.GetType();

                var where = ActionResult.Describe(component);

                op.Result.Result = component.gameObject;

                if (oldType == newType)
                {
                    op.Result.Message = $"{where} already is a {newType.Name}.";

                    return op.Result;
                }

                if (!op.CheckEditable(component))
                {
                    return op.Result;
                }

                var inherited = PrefabUtility.GetCorrespondingObjectFromSource(component);

                if (inherited != null)
                {
                    op.Problem($"The {oldType.Name} of {where} comes from {AssetDatabase.GetAssetPath(inherited)}: change its class there (open that prefab), where it was added.");

                    return op.Result;
                }

                var script = ScriptLocator.Find(newType);

                if (script == null)
                {
                    op.Problem($"No script declares {newType.Name} (its file must be called {newType.Name}.cs).");

                    return op.Result;
                }

                if (newType.IsDefined(typeof(DisallowMultipleComponent), true) && component.GetComponents<Component>().Any(other => other != component && other != null && newType.IsInstanceOfType(other)))
                {
                    op.Problem($"{component.gameObject.name} already has a {newType.Name}, and only one is allowed.");

                    return op.Result;
                }

                var newFields = ClassConversion.SerializedFields(newType, false).ToList();

                var lost = new List<string>();

                foreach (var field in ClassConversion.SerializedFields(oldType, false))
                {
                    var kept = newFields.FirstOrDefault(other => other.Name == field.Name);

                    if (kept == null)
                    {
                        lost.Add(field.Name);
                    }
                    else if (kept.FieldType != field.FieldType)
                    {
                        lost.Add($"{field.Name} (becomes a {kept.FieldType.Name})");
                    }
                }

                if (lost.Count > 0)
                {
                    op.Result.AddNote($"Lost with the change (only {oldType.Name} has them): {string.Join(", ", lost)}.");
                }

                if (dryRun)
                {
                    op.Result.Message = $"Preview: {where} becomes a {newType.Name}" + (lost.Count > 0 ? $"; {lost.Count} field(s) are lost." : "; every field is kept.") +
                                        " Nothing was changed (pass apply=true, or confirm, to do it).";

                    return op.Result;
                }

                op.Edit(component, EditIntent.ThisCopy, copy =>
                {
                    var target = (Component)copy;

                    // The swap replaces the component's script object: keep its GameObject, never the old component.
                    var gameObject = target.gameObject;

                    // Unity's Undo does not swap a component's class back, so this is not recorded (the confirmation says so).
                    var serialized = new SerializedObject(target);

                    serialized.FindProperty("m_Script").objectReferenceValue = script;

                    serialized.ApplyModifiedPropertiesWithoutUndo();

                    EditorUtility.SetDirty(gameObject);

                    op.Touch(gameObject);
                });

                if (op.Result.Ok)
                {
                    op.Result.AddChanged($"{where}: {oldType.Name} -> {newType.Name}");

                    op.Result.Message = $"{where} is a {newType.Name} now" + (lost.Count > 0 ? $"; lost: {string.Join(", ", lost)}." : ".") + " " + UndoNote_Internal(lost.Count > 0);
                }

                return op.Result;
            }
        }

        /// <summary>
        /// What Change Class says about going back: when fields are lost, changing the class again does not bring them back.
        /// </summary>
        internal static string UndoNote_Internal(bool fieldsLost)
        {
            return fieldsLost
                       ? "Undo cannot take it back: Change Class again restores the class; the lost fields start empty."
                       : "Undo cannot take it back: Change Class again restores it.";
        }

        /// <summary>
        /// Change Game State and Player State Classes: edits the GameMode&lt;A, B&gt; line of a GameMode script to name
        /// <paramref name="gameState"/> and <paramref name="playerState"/>. A dry run (the default) only shows the change.
        /// </summary>
        public static ActionResult ChangeGameModeStates(MonoScript script, Type gameState, Type playerState, bool dryRun = true)
        {
            var result = new ActionResult("change-gamemode-states");

            if (!ScriptOf_Internal(script, result, out var path, out var type))
            {
                return result;
            }

            if (!typeof(GameMode).IsAssignableFrom(type))
            {
                result.AddProblem($"{type.Name} is not a GameMode.");

                return result;
            }

            CheckState_Internal(gameState, typeof(GameState), result);

            CheckState_Internal(playerState, typeof(PlayerState), result);

            if (!result.Ok)
            {
                return result;
            }

            result.Result = script;

            var physical = ClassConversion.PhysicalPath(path);

            if (!ScriptText.TryRead(physical, path, out var text, out var readProblem))
            {
                result.AddProblem(readProblem);

                return result;
            }

            var source = text.Text;

            var ns = type.Namespace ?? string.Empty;

            var patch = ScriptPatcher.RetargetGameModeStates(source, type.Name, StateReference_Internal(gameState, ns), StateReference_Internal(playerState, ns));

            if (!patch.Ok)
            {
                result.AddProblem(patch.Problem);

                return result;
            }

            if (!patch.Changed)
            {
                result.Message = $"{type.Name} already uses {gameState.Name} and {playerState.Name}.";

                return result;
            }

            var patched = patch.Source;

            if ((ClassTemplates.IsFrameworkType(gameState) || ClassTemplates.IsFrameworkType(playerState)) && ns != ClassConversion.FrameworkNamespace &&
                !ns.StartsWith(ClassConversion.FrameworkNamespace + ".", StringComparison.Ordinal))
            {
                var withUsing = ScriptPatcher.EnsureUsing(patched, ClassConversion.FrameworkNamespace);

                if (!withUsing.Ok)
                {
                    result.AddProblem(withUsing.Problem);

                    return result;
                }

                patched = withUsing.Source;
            }

            result.AddNote($"{Path.GetFileName(path)}, line {patch.Line}:");

            result.AddNote("  - " + patch.Before);

            result.AddNote("  + " + patch.After);

            if (dryRun)
            {
                result.Message = $"Preview: {type.Name} would create {gameState.Name} and {playerState.Name}. Nothing was written (pass apply=true, or press Apply, to write it).";

                return result;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                result.AddProblem("Leave Play mode first: the change edits the script, and the compile waits for Edit mode.");

                return result;
            }

            // Only when the file is still exactly as it was read; written back with the same byte order mark (or none).
            if (!File.Exists(physical) || !ScriptText.SameBytes(File.ReadAllBytes(physical), text.Bytes))
            {
                result.AddProblem($"{path} changed while it was being edited. Nothing was written: try again.");

                return result;
            }

            File.WriteAllBytes(physical, ScriptText.Encode(patched, text.Bom));

            result.AddChanged($"{path}: GameState {gameState.Name}, PlayerState {playerState.Name}");

            AssetDatabase.Refresh();

            result.Message = $"{type.Name} creates {gameState.Name} and {playerState.Name} after the compile.";

            return result;
        }

        private static IEnumerable<string> Paths_Internal(string list)
        {
            return (list ?? string.Empty).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(path => path.Trim()).Where(path => path.Length > 0);
        }

        private static bool ScriptOf_Internal(MonoScript script, ActionResult result, out string path, out Type type)
        {
            path = string.Empty;

            type = null;

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                result.AddProblem("Leave Play mode first: this edits a script, and the compile waits for Edit mode.");

                return false;
            }

            if (script == null)
            {
                result.AddProblem("Pick a script.");

                return false;
            }

            path = AssetDatabase.GetAssetPath(script);

            if (!ClassConversion.IsEditable(path, out var problem))
            {
                result.AddProblem(problem);

                return false;
            }

            type = script.GetClass();

            if (type == null)
            {
                result.AddProblem($"{Path.GetFileName(path)} has no compiled class: fix its compile errors, or name the file like its class.");

                return false;
            }

            if (!typeof(MonoBehaviour).IsAssignableFrom(type))
            {
                result.AddProblem($"{type.Name} is not a MonoBehaviour.");

                return false;
            }

            return true;
        }

        private static void CheckState_Internal(Type state, Type baseType, ActionResult result)
        {
            if (state == null || !baseType.IsAssignableFrom(state) || state.IsAbstract || state.ContainsGenericParameters || state.GetConstructor(Type.EmptyTypes) == null)
            {
                result.AddProblem($"{state?.Name ?? "Nothing"} is not a {baseType.Name} class a GameMode can create (it needs a public parameterless constructor).");
            }
        }

        private static string StateReference_Internal(Type state, string gameModeNamespace)
        {
            if (ClassTemplates.IsFrameworkType(state) || (state.Namespace ?? string.Empty) == gameModeNamespace)
            {
                return state.Name;
            }

            return "global::" + state.FullName.Replace('+', '.');
        }

        /// <summary>
        /// The object names a class rename changes: "GameplayScene" to "GameplayGameMode", and the nicified "Gameplay Scene"
        /// to "Gameplay Game Mode". A name the nicified form leaves as it is ("Arena") keeps its own pair.
        /// </summary>
        internal static Dictionary<string, string> RenamedObjectNames_Internal(string oldName, string newName)
        {
            var names = new Dictionary<string, string>(StringComparer.Ordinal) { [oldName] = newName };

            var nicified = ObjectNames.NicifyVariableName(oldName);

            if (nicified != oldName)
            {
                names[nicified] = ObjectNames.NicifyVariableName(newName);
            }

            return names;
        }

        [PendingStepHandler(RenameObjectsKind)]
        private static void FinishRenameObjects_Internal(PendingStepContext context)
        {
            var type = context.Classes.FirstOrDefault();

            var oldName = context.Step.Arg("oldName", string.Empty);

            var newName = context.Step.Arg("newName", string.Empty);

            if (type == null || oldName.Length == 0 || newName.Length == 0)
            {
                return;
            }

            var names = RenamedObjectNames_Internal(oldName, newName);

            var op = context.Op;

            using (Scaffold.EditPrefabsInPlace())
            {
                foreach (var path in Paths_Internal(context.Step.Arg("prefabs")))
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

                    if (prefab == null)
                    {
                        continue;
                    }

                    foreach (var component in prefab.GetComponentsInChildren(type, true))
                    {
                        if (names.TryGetValue(component.gameObject.name, out var renamed))
                        {
                            var described = ActionResult.Describe(component.gameObject);

                            op.Edit(component.gameObject, EditIntent.ThisCopy, copy =>
                            {
                                ((GameObject)copy).name = renamed;

                                op.Touch(copy);
                            });

                            op.Result.AddChanged($"{described}: renamed to {renamed}");
                        }
                    }
                }
            }

            foreach (var path in Paths_Internal(context.Step.Arg("scenes")))
            {
                var scene = SceneManager.GetSceneByPath(path);

                var opened = false;

                if (!scene.IsValid() || !scene.isLoaded)
                {
                    if (!Application.isBatchMode)
                    {
                        op.Result.AddNote($"Not renamed in {path}: it is not open. Rename the objects called {oldName} there by hand, or open it before the compile.");

                        continue;
                    }

                    scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);

                    opened = true;
                }

                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var component in root.GetComponentsInChildren(type, true))
                    {
                        if (!names.TryGetValue(component.gameObject.name, out var renamed))
                        {
                            continue;
                        }

                        var described = ActionResult.Describe(component.gameObject);

                        Undo.RecordObject(component.gameObject, op.Name);

                        component.gameObject.name = renamed;

                        op.Touch(component.gameObject);

                        op.Result.AddChanged($"{described}: renamed to {renamed}");
                    }
                }

                if (opened)
                {
                    EditorSceneManager.MarkSceneDirty(scene);

                    EditorSceneManager.SaveScene(scene);

                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }
    }
}
