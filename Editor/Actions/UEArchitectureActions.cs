using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using Object = UnityEngine.Object;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// Every UE Architecture editor action as one public static method, for buttons, menus, scripts and the command line
    /// (<see cref="UEArchitectureCli"/>). Each returns an <see cref="ActionResult"/>, never opens a dialog, is one Undo
    /// step and can run again safely: what already exists is reused.
    /// </summary>
    public static partial class UEArchitectureActions
    {
        /// <summary>
        /// Pending step kind: build the prefab of a class once its script compiles, then fill the fields it was made for.
        /// </summary>
        public const string PrefabForScriptKind = "uea.prefab-for-script";

        /// <summary>
        /// Runs the work that waited for a script compile.
        /// </summary>
        public static ActionResult CompletePending()
        {
            return PendingSteps.CompletePending();
        }

        /// <summary>
        /// Lists the work that waits for a compile or for the user (one note per step).
        /// </summary>
        public static ActionResult ListPending()
        {
            var result = new ActionResult("list-pending");

            var steps = PendingSteps.All();

            foreach (var step in steps)
            {
                result.AddNote($"{step.id}  {step.kind}  {step.status}: {step.Describe()}");
            }

            result.Pending = steps.Any(step => step.status == PendingStatus.Waiting);

            result.Message = steps.Count == 0 ? "Nothing pending." : steps.Count == 1 ? "1 pending step." : $"{steps.Count} pending steps.";

            return result;
        }

        /// <summary>
        /// Drops a pending step and the steps that depend on it. Files already written stay.
        /// </summary>
        public static ActionResult CancelPending(string id)
        {
            return PendingSteps.Cancel(id);
        }

        /// <summary>
        /// Moves files an action created to the trash, because Undo cannot remove files: prefabs, scripts and other assets
        /// under Assets/, then the folders among <paramref name="paths"/> once they are empty. "scene#object" entries are
        /// skipped (Undo removes those), and the pending steps of a removed script are cancelled. Nothing is deleted for
        /// good: the files can be taken back from the system's trash.
        /// </summary>
        public static ActionResult RemoveCreatedFiles(IEnumerable<string> paths)
        {
            var result = new ActionResult("remove-files");

            var files = new List<string>();

            var folders = new List<string>();

            foreach (var raw in paths ?? Array.Empty<string>())
            {
                var path = (raw ?? string.Empty).Trim().Replace('\\', '/');

                if (path.Length == 0 || path.Contains("#"))
                {
                    continue;
                }

                if (!path.StartsWith("Assets/", StringComparison.Ordinal) || path.Contains(".."))
                {
                    result.AddProblem($"{path} is not under Assets/, so it was left alone.");

                    continue;
                }

                if (AssetDatabase.IsValidFolder(path))
                {
                    folders.Add(path);
                }
                else if (AssetDatabase.LoadMainAssetAtPath(path) != null)
                {
                    files.Add(path);
                }
                else
                {
                    result.AddNote($"{path} is already gone.");
                }
            }

            if (files.Count > 0)
            {
                var failed = new List<string>();

                AssetDatabase.MoveAssetsToTrash(files.ToArray(), failed);

                foreach (var path in files)
                {
                    if (failed.Contains(path))
                    {
                        result.AddProblem($"Could not move {path} to the trash.");
                    }
                    else
                    {
                        result.AddChanged(path + " (moved to the trash)");
                    }
                }

                foreach (var step in PendingSteps.All().Where(step => step.scriptPaths.Any(files.Contains)).ToArray())
                {
                    PendingSteps.Cancel(step.id);

                    result.AddNote($"Cancelled the pending step {step.id}: its script was removed.");
                }
            }

            // Deepest first, so a folder emptied by its subfolder goes too.
            foreach (var folder in folders.OrderByDescending(folder => folder.Length))
            {
                if (System.IO.Directory.Exists(folder) && System.IO.Directory.EnumerateFileSystemEntries(folder).Any())
                {
                    result.AddNote($"{folder} was kept: it holds other files.");

                    continue;
                }

                if (AssetDatabase.MoveAssetToTrash(folder))
                {
                    result.AddChanged(folder + " (moved to the trash)");
                }
            }

            result.Message = result.Changed.Count == 0 ? "Nothing to remove." : $"Moved {result.Changed.Count} file(s) or folder(s) to the trash.";

            if (result.Changed.Count > 0)
            {
                FrameworkIndex.Invalidate();
            }

            return result;
        }

        /// <summary>
        /// The prefab of a class, made if missing: a root with the class's component, named after the class, at
        /// <paramref name="path"/> or, when empty, wherever the class already has a prefab, else at the conventional path
        /// (Project Settings > UE Architecture > Editor, e.g. Assets/Prefabs/Gameplay/ArenaPawn.prefab).
        /// </summary>
        public static ActionResult CreateClassPrefab(Type type, string path = null)
        {
            using (var op = Scaffold.Begin($"Create {type?.Name ?? "class"} prefab", "create-prefab"))
            {
                var prefab = CreateClassPrefab_Internal(op, type, path);

                if (prefab != null)
                {
                    op.Result.Message = op.Result.Created.Count > 0 ? $"Created {AssetDatabase.GetAssetPath(prefab)}." : $"{type.Name} already has a prefab: {AssetDatabase.GetAssetPath(prefab)}.";
                }

                return op.Result;
            }
        }

        /// <summary>
        /// Sets a serialized field like the Inspector does (Undo, prefab overrides). A prefab GameObject given for a
        /// component field is replaced by its component of the field's type. Text values are converted to the field's type
        /// (numbers, true/false, enum names).
        /// </summary>
        public static ActionResult Assign(Object owner, string propertyPath, object value)
        {
            using (var op = Scaffold.Begin($"Set {ObjectNames.NicifyVariableName(propertyPath ?? string.Empty)}", "assign"))
            {
                if (op.Assign(owner, propertyPath, value))
                {
                    op.Result.Result = owner;

                    op.Result.Message = op.Result.Changed.Count > 0 ? $"Set {propertyPath} on {ActionResult.Describe(owner)}." : $"{propertyPath} already had that value.";
                }

                return op.Result;
            }
        }

        /// <summary>
        /// Writes a new script deriving from <paramref name="parent"/> (in <paramref name="folder"/>, or the scripts folder
        /// of the editor settings). With <paramref name="withPrefab"/>, its prefab is built after the compile and put into
        /// <paramref name="propertyPath"/> of <paramref name="assignTo"/> when given. The result is Pending until then; from
        /// the command line, run "-uea complete-pending" afterwards.
        /// </summary>
        public static ActionResult NewScript(string className, Type parent, string folder = null, bool withPrefab = false, Object assignTo = null, string propertyPath = null)
        {
            return NewScript_Internal(className, parent, folder, withPrefab || assignTo != null, assignTo != null ? new[] { assignTo } : Array.Empty<Object>(), propertyPath);
        }

        /// <summary>
        /// <see cref="NewScript"/> for several objects at once (a multi-object Inspector): every owner gets the prefab.
        /// </summary>
        internal static ActionResult NewScript_Internal(string className, Type parent, string folder, bool withPrefab, IReadOnlyList<Object> owners, string propertyPath)
        {
            var plan = ScriptWriter.Plan(new[] { new ScriptRequest(className, parent, folder) });

            if (!plan.Ok)
            {
                var failed = new ActionResult("new-script");

                foreach (var problem in plan.Problems.DefaultIfEmpty("Nothing to write."))
                {
                    failed.AddProblem(problem);
                }

                return failed;
            }

            if (!withPrefab)
            {
                return ScriptWriter.Write(plan);
            }

            var preflight = new ActionResult("new-script");

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                preflight.AddProblem("Leave Play mode first: the prefab is made after the compile, which waits for Edit mode.");

                return preflight;
            }

            if (!typeof(Component).IsAssignableFrom(parent))
            {
                preflight.AddProblem($"{parent.Name} is not a component, so it has no prefab.");

                return preflight;
            }

            var script = plan.Scripts[0];

            var prefabPath = GeneratorNaming.PrefabPath(script.ClassName, parent, UEArchitectureEditorSettings.Current);

            var step = PendingStep.Create(PrefabForScriptKind).WithScript(script.Path, script.FullName).WithArg("prefabPath", prefabPath);

            var assigned = owners?.Where(owner => owner != null).ToArray() ?? Array.Empty<Object>();

            foreach (var owner in assigned)
            {
                if (!PendingSteps.TryTarget(owner, propertyPath, preflight, out var target))
                {
                    return preflight;
                }

                step.targets.Add(target);
            }

            var result = ScriptWriter.Write(plan, new[] { step });

            var where = assigned.Length == 0 ? "." : assigned.Length == 1 ? $" goes into {propertyPath} of {ActionResult.Describe(assigned[0])}." : $" goes into {propertyPath} of {assigned.Length} objects.";

            result.AddNote($"After the compile: {prefabPath}{where}");

            return result;
        }

        internal static GameObject CreateClassPrefab_Internal(ScaffoldOp op, Type type, string path)
        {
            if (type == null)
            {
                op.Problem("Pick a class to make a prefab of.");

                return null;
            }

            if (!typeof(Component).IsAssignableFrom(type))
            {
                op.Problem($"{type.Name} is not a component, so it has no prefab.");

                return null;
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                var existing = FrameworkIndex.PrefabWithExactly(type);

                if (existing != null)
                {
                    op.Result.AddNote("Reused " + AssetDatabase.GetAssetPath(existing));

                    op.Result.Result = existing;

                    op.PingWhenDone(existing);

                    return existing;
                }

                path = Scaffold.PrefabPathFor(type);
            }

            var prefab = op.FindOrCreatePrefab(path, type.Name, new[] { type });

            op.PingWhenDone(prefab);

            return prefab;
        }

        [PendingStepHandler(PrefabForScriptKind)]
        private static void FinishPrefabForScript_Internal(PendingStepContext context)
        {
            var type = context.Classes.FirstOrDefault();

            var prefab = CreateClassPrefab_Internal(context.Op, type, context.Step.Arg("prefabPath"));

            if (prefab == null)
            {
                return;
            }

            foreach (var target in context.Step.targets)
            {
                var owner = context.ResolveTarget(target);

                if (owner != null)
                {
                    context.Op.Assign(owner, target.propertyPath, prefab);
                }
            }
        }
    }

    /// <summary>
    /// The files the last action of this Editor session created, for Assets > UE Architecture > Remove Files Made by the
    /// Last Action (Undo cannot remove files). Kept in SessionState, so it survives the domain reload of a compile.
    /// </summary>
    internal static class LastCreatedFiles
    {
        private const string PathsKey = "UEArchitecture.LastCreatedFiles";

        private const string LabelKey = "UEArchitecture.LastCreatedLabel";

        /// <summary>
        /// The asset paths remembered (files and folders under Assets/).
        /// </summary>
        public static string[] Paths => SessionState.GetString(PathsKey, string.Empty).Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

        /// <summary>
        /// What the action said it did, e.g. "Created ArenaPawn.prefab. Default Pawn of ArenaGameMode now uses ArenaPawn.prefab."
        /// </summary>
        public static string Label => SessionState.GetString(LabelKey, string.Empty);

        /// <summary>
        /// Remembers the files <paramref name="result"/> created, instead of the previous action's (or next to them, with
        /// <paramref name="append"/>: the prefab a pending step made after its script, which keeps the label of the action
        /// that wrote the script). Results that created no file change nothing.
        /// </summary>
        public static void Remember(ActionResult result, bool append = false)
        {
            if (result == null)
            {
                return;
            }

            var created = result.Created.Where(path => !path.Contains("#") && path.StartsWith("Assets/", StringComparison.Ordinal)).ToList();

            if (created.Count == 0)
            {
                return;
            }

            var keepLabel = append && Paths.Length > 0 && Label.Length > 0;

            if (append)
            {
                created = Paths.Concat(created).Distinct().ToList();
            }

            SessionState.SetString(PathsKey, string.Join("\n", created));

            if (!keepLabel)
            {
                SessionState.SetString(LabelKey, string.IsNullOrEmpty(result.Message) ? result.Verb : result.Message);
            }
        }

        public static void Forget()
        {
            SessionState.EraseString(PathsKey);

            SessionState.EraseString(LabelKey);
        }
    }
}
