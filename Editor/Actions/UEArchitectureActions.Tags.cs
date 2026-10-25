using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using System.Collections.Generic;

namespace ModernWestern.UEArchitecture.EditorTools
{
    public static partial class UEArchitectureActions
    {
        private const string TagsProperty = "gameplayTags";

        /// <summary>
        /// Adds a gameplay tag to Project Settings > UE Architecture (creating the settings asset when the project has
        /// none). A tag already there, listed or implied by a child, is left as it is.
        /// </summary>
        public static ActionResult AddTag(string tag)
        {
            tag = (tag ?? string.Empty).Trim();

            if (!CheckTagEdit_Internal("add-tag", out var refused))
            {
                return refused;
            }

            using (var op = Scaffold.Begin($"Add tag {tag}", "add-tag"))
            {
                var problem = GameplayTagEdits.NameProblem(tag);

                if (problem != null)
                {
                    op.Problem(problem);

                    return op.Result;
                }

                var current = FrameworkIndex.Settings?.GameplayTags ?? Array.Empty<string>();

                if (GameplayTagEdits.IsKnown(current, tag))
                {
                    op.Result.Message = $"\"{tag}\" is already a project tag.";

                    return op.Result;
                }

                var settings = EnsureSettings_Internal(op);

                if (settings == null)
                {
                    return op.Result;
                }

                var tags = GameplayTagEdits.Add(settings.GameplayTags, tag, out _, out _);

                SetTags_Internal(op, settings, tags);

                op.Result.Result = settings;

                op.Result.Message = $"Added \"{tag}\" to the project tags.";

                return op.Result;
            }
        }

        /// <summary>
        /// Renames a gameplay tag and the tags below it (Status to Condition also gives Condition.Burning), in Project
        /// Settings and in the tag fields of prefabs and other assets. Levels and scripts that use it are listed, never
        /// changed. When an asset, a level or a script uses it, nothing happens unless <paramref name="apply"/> is true: the
        /// result is a preview (Pending) that says what would change. Running it again after the rename changes nothing.
        /// </summary>
        public static ActionResult RenameTag(string from, string to, bool apply = false)
        {
            from = (from ?? string.Empty).Trim();

            to = (to ?? string.Empty).Trim();

            if (!CheckTagEdit_Internal("rename-tag", out var refused))
            {
                return refused;
            }

            var current = FrameworkIndex.Settings?.GameplayTags ?? Array.Empty<string>();

            var fromKnown = GameplayTagEdits.IsKnown(current, from);

            var problem = GameplayTagEdits.RenameProblem(current, from, to);

            if (problem != null)
            {
                return ActionResult.Fail("rename-tag", problem);
            }

            if (!fromKnown && !GameplayTagEdits.IsKnown(current, to))
            {
                return ActionResult.Fail("rename-tag", $"\"{from}\" is not a project tag.");
            }

            // The assets are searched even when the list was renamed already, so a rename stopped half way can finish.
            var usages = GameplayTagUsages.FindWithProgress(from, false);

            if (!apply && !usages.IsEmpty)
            {
                var preview = new ActionResult("rename-tag") { Pending = true };

                preview.Message = $"Rename \"{from}\" to \"{to}\": {usages.RenameSummary()} Nothing was changed: apply it to rename (apply=true on the command line).";

                AddUsageNotes_Internal(preview, usages);

                return preview;
            }

            using (var op = Scaffold.Begin($"Rename tag {from} to {to}", "rename-tag"))
            {
                var settings = FrameworkIndex.Settings;

                if (fromKnown && settings != null)
                {
                    SetTags_Internal(op, settings, GameplayTagEdits.Rename(settings.GameplayTags, from, to, out _, out _));
                }

                var references = 0;

                foreach (var asset in usages.Assets)
                {
                    var count = GameplayTagUsages.Rewrite(op, asset.Key, from, to);

                    if (count > 0)
                    {
                        references += count;

                        op.Result.AddChanged($"{asset.Key}: {TagUsageReport.Plural_Internal(count, "tag field")}");
                    }
                }

                AddUsageNotes_Internal(op.Result, usages, false);

                op.Result.Result = settings;

                if (op.Result.Changed.Count == 0)
                {
                    op.Result.Message = $"\"{from}\" was already renamed to \"{to}\".";
                }
                else
                {
                    var listed = usages.Scenes.Count + usages.Scripts.Count;

                    op.Result.Message = $"Renamed \"{from}\" to \"{to}\"" +
                                        (references > 0 ? $" and updated {TagUsageReport.Plural_Internal(references, "reference")} in {TagUsageReport.Plural_Internal(usages.Assets.Count, "asset")}" : string.Empty) +
                                        (listed > 0 ? ". The levels and scripts listed still use the old name: change them by hand." : ".");
                }

                if (File.Exists(UEArchitectureEditorSettings.instance.TagConstantsPath) && op.Result.Changed.Count > 0)
                {
                    op.Result.AddNote("Generate the C# constants again: the file still has the old name.");
                }

                return op.Result;
            }
        }

        /// <summary>
        /// Removes a gameplay tag and the tags below it from Project Settings (its parent stays). Fields that still hold it
        /// keep their value and show it as "not a project tag". When something uses it, nothing happens unless
        /// <paramref name="apply"/> is true: the result is a preview (Pending) with the usage count.
        /// </summary>
        public static ActionResult RemoveTag(string tag, bool apply = false)
        {
            tag = (tag ?? string.Empty).Trim();

            if (!CheckTagEdit_Internal("remove-tag", out var refused))
            {
                return refused;
            }

            var settings = FrameworkIndex.Settings;

            var current = settings != null ? settings.GameplayTags : Array.Empty<string>();

            if (!GameplayTagEdits.IsKnown(current, tag))
            {
                var nothing = new ActionResult("remove-tag") { Message = $"\"{tag}\" is not a project tag." };

                var nameProblem = GameplayTagEdits.NameProblem(tag);

                if (nameProblem != null)
                {
                    nothing.AddProblem(nameProblem);
                }

                return nothing;
            }

            var usages = GameplayTagUsages.FindWithProgress(tag, false);

            // The tag itself may only be implied by its children ("Status" for Status.Burning): it is not counted below it.
            var below = GameplayTagEdits.Subtree(current, tag).Count(listed => listed != tag);

            if (!apply && !usages.IsEmpty)
            {
                var preview = new ActionResult("remove-tag") { Pending = true };

                preview.Message = $"Delete \"{tag}\"" + (below > 0 ? $" and the {below} tag(s) below it" : string.Empty) +
                                  $": {usages.Summary()} Those uses stay and show it as not a project tag. Nothing was changed: apply it to delete (apply=true on the command line).";

                AddUsageNotes_Internal(preview, usages);

                return preview;
            }

            using (var op = Scaffold.Begin($"Delete tag {tag}", "remove-tag"))
            {
                SetTags_Internal(op, settings, GameplayTagEdits.Remove(current, tag, out _));

                AddUsageNotes_Internal(op.Result, usages);

                op.Result.Result = settings;

                op.Result.Message = $"Deleted \"{tag}\"" + (below > 0 ? $" and {below} tag(s) below it" : string.Empty) +
                                    (usages.IsEmpty ? "." : $". Still used: {usages.Summary()}");

                return op.Result;
            }
        }

        /// <summary>
        /// Where a gameplay tag (and the tags below it) is used: assets, levels and scripts, one note each.
        /// </summary>
        public static ActionResult FindTagUsages(string tag)
        {
            tag = (tag ?? string.Empty).Trim();

            var result = new ActionResult("find-tag-usages");

            var problem = GameplayTagEdits.NameProblem(tag);

            if (problem != null)
            {
                result.AddProblem(problem);

                return result;
            }

            var usages = GameplayTagUsages.FindWithProgress(tag, false);

            AddUsageNotes_Internal(result, usages);

            result.Message = $"\"{tag}\": {usages.Summary()}";

            return result;
        }

        /// <summary>
        /// Writes the C# constants of the project tags (Tags.Status.Burning) to <paramref name="path"/>, or to the file set
        /// in Project Settings > UE Architecture > Editor (GameplayTags.g.cs in the scripts folder by default), in the
        /// root namespace of its assembly unless <paramref name="namespaceName"/> is given. First checks that the folder's
        /// assembly can use UE Architecture; <paramref name="fixReferences"/> adds the missing reference to its asmdef.
        /// An up-to-date file is left alone; a file there that the generator did not write, and tags whose C# names would
        /// clash, are refused. One compile.
        /// </summary>
        public static ActionResult GenerateTagConstants(string path = null, string namespaceName = null, bool fixReferences = false)
        {
            const string verb = "generate-tag-constants";

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return ActionResult.Fail(verb, "Leave Play mode first: scripts compile in Edit mode.");
            }

            path = string.IsNullOrWhiteSpace(path) ? UEArchitectureEditorSettings.instance.TagConstantsPath : path.Trim().Replace('\\', '/');

            if (!path.StartsWith("Assets/", StringComparison.Ordinal) || path.Contains("..") || !path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                return ActionResult.Fail(verb, $"{path} is not a .cs file under Assets/.");
            }

            var result = new ActionResult(verb);

            var ns = namespaceName ?? CompilationPipeline.GetAssemblyRootNamespaceFromScriptPath(path) ?? string.Empty;

            var collisions = new List<string>();

            var source = GameplayTagConstants.Generate(FrameworkIndex.Settings?.GameplayTags ?? Array.Empty<string>(), ns, GameplayTagConstants.DefaultClassName, collisions);

            if (collisions.Count > 0)
            {
                // Writing it would give one of them a name another tag could take later, so code could silently change tag.
                foreach (var collision in collisions)
                {
                    result.AddProblem(collision + " Rename one of them.");
                }

                return result;
            }

            var existed = File.Exists(path);

            var existing = existed ? File.ReadAllText(path) : string.Empty;

            if (existed && !GameplayTagConstants.IsGenerated(existing))
            {
                result.AddProblem($"{path} already exists and was not made by UE Architecture (it does not start with the generator's header), so it was left alone. " +
                                  "Pick another file in Project Settings > UE Architecture > Editor, or move that one.");

                return result;
            }

            var target = ScriptWriter.AssemblyOf(path);

            var assemblyProblem = GameplayTagConstants.AssemblyProblem(target, path);

            if (assemblyProblem != null)
            {
                if (!fixReferences || string.IsNullOrEmpty(target.AsmdefPath))
                {
                    result.AddProblem(assemblyProblem);

                    if (!string.IsNullOrEmpty(target.AsmdefPath))
                    {
                        result.AddNote("Add the missing reference with fix-references=true (the page's \"Add reference\" button), or pick a file in another folder.");
                    }

                    return result;
                }

                result.Merge(AsmdefReferences.Apply(new[] { new MissingReference(target.AsmdefPath, ScriptWriter.RuntimeAssembly, assemblyProblem) }));

                if (!result.Ok)
                {
                    return result;
                }
            }
            else if (!target.Known)
            {
                result.AddNote($"Could not check which assemblies {path} can use (its assembly has not compiled yet).");
            }

            if (existed && existing.Replace("\r\n", "\n") == source)
            {
                result.Message = $"{path} is up to date.";

                return result;
            }

            var fullName = string.IsNullOrEmpty(ns) ? GameplayTagConstants.DefaultClassName : ns + "." + GameplayTagConstants.DefaultClassName;

            var clash = PendingSteps.FindClass(fullName);

            if (!existed && clash != null)
            {
                result.AddProblem($"A class called {fullName} already exists (in {clash.Assembly.GetName().Name}), so the constants would clash with it. Pick another namespace or folder.");

                return result;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? "Assets");

            File.WriteAllText(path, source);

            if (existed)
            {
                result.AddChanged(path);
            }
            else
            {
                result.AddCreated(path);
            }

            AssetDatabase.Refresh();

            var count = GameplayTagTree.Build(FrameworkIndex.Settings?.GameplayTags).Count;

            result.Message = $"Wrote {path} with {TagUsageReport.Plural_Internal(count, "tag")}: use {GameplayTagConstants.DefaultClassName}.Status.Burning instead of GameplayTag.Request(\"Status.Burning\").";

            return result;
        }

        private static bool CheckTagEdit_Internal(string verb, out ActionResult refused)
        {
            refused = EditorApplication.isPlayingOrWillChangePlaymode ? ActionResult.Fail(verb, "Leave Play mode first: tags are edited in Edit mode.") : null;

            return refused == null;
        }

        /// <summary>
        /// Writes the tag list of the settings asset through SerializedObject (Undo, dirty, saved when the gesture ends).
        /// </summary>
        private static void SetTags_Internal(ScaffoldOp op, UEArchitectureSettings settings, IReadOnlyList<string> tags)
        {
            if (settings == null)
            {
                return;
            }

            var serialized = new SerializedObject(settings);

            var list = serialized.FindProperty(TagsProperty);

            var same = list.arraySize == tags.Count;

            for (var i = 0; same && i < tags.Count; i++)
            {
                same = list.GetArrayElementAtIndex(i).stringValue == tags[i];
            }

            if (same)
            {
                return;
            }

            list.arraySize = tags.Count;

            for (var i = 0; i < tags.Count; i++)
            {
                list.GetArrayElementAtIndex(i).stringValue = tags[i];
            }

            serialized.ApplyModifiedProperties();

            op.Touch(settings);

            op.Result.AddChanged($"{AssetDatabase.GetAssetPath(settings)}: {TagsProperty}");
        }

        private static void AddUsageNotes_Internal(ActionResult result, TagUsageReport usages, bool assets = true)
        {
            if (assets)
            {
                foreach (var asset in usages.Assets)
                {
                    result.AddNote($"Asset {asset.Key}: {TagUsageReport.Plural_Internal(asset.Value, "tag field")}");
                }
            }

            foreach (var scene in usages.Scenes)
            {
                result.AddNote($"Level {scene.Key} mentions it {(scene.Value == 1 ? "once" : scene.Value + " times")} (not changed)");
            }

            foreach (var script in usages.Scripts)
            {
                result.AddNote($"Script {script.Key}: {TagUsageReport.Plural_Internal(script.Value, "string")} (not changed)");
            }
        }
    }
}
