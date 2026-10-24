using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using System.Collections.Generic;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// One script to write: a class deriving from <see cref="Parent"/>, in <see cref="Folder"/> (the scripts folder of
    /// the editor settings when empty).
    /// </summary>
    internal sealed class ScriptRequest
    {
        public ScriptRequest(string typedName, Type parent, string folder = null)
        {
            TypedName = typedName;

            Parent = parent;

            Folder = folder;
        }

        public string TypedName { get; }

        public Type Parent { get; }

        public string Folder { get; }

        /// <summary>
        /// The template to use when the parent alone does not say it (a tickable subsystem, a Message struct). Null: the
        /// template of the parent, or a plain child of it.
        /// </summary>
        public CreateFrameworkScripts.Kind? Kind { get; set; }

        /// <summary>
        /// When a tickable subsystem ticks (Update, order 0 by default).
        /// </summary>
        public TickOptions? Tick { get; set; }

        /// <summary>
        /// Puts a GameMode's GameState and PlayerState in their own files. Null: as set in Project Settings > UE
        /// Architecture > Editor.
        /// </summary>
        public bool? SplitStates { get; set; }

        /// <summary>
        /// The folder may be inside an embedded package (Packages/com.studio.game/..., kept in the project and writable),
        /// for a folder the user picked in the Project window (Assets > Create > UE Architecture). Generated defaults, the
        /// '+' picker and the command line always write under Assets/.
        /// </summary>
        public bool AllowPackageFolder { get; set; }
    }

    /// <summary>
    /// A script <see cref="ScriptWriter.Plan(IEnumerable{ScriptRequest})"/> will write.
    /// </summary>
    internal sealed class PlannedScript
    {
        public string ClassName;

        public string Path;

        public string Namespace;

        public string Source;

        public Type Parent;

        /// <summary>
        /// Full names of every class the file declares (a GameMode file also declares its GameState and PlayerState).
        /// </summary>
        public string[] DeclaredClasses;

        public string FullName => string.IsNullOrEmpty(Namespace) ? ClassName : Namespace + "." + ClassName;
    }

    /// <summary>
    /// What a set of requests would write, and what stops it. Nothing is written until <see cref="ScriptWriter.Write"/>.
    /// </summary>
    internal sealed class ScriptPlan
    {
        public readonly List<PlannedScript> Scripts = new();

        public readonly List<string> Problems = new();

        public readonly List<string> Notes = new();

        public bool Ok => Problems.Count == 0 && Scripts.Count > 0;

        /// <summary>
        /// "Writes ArenaPawn.cs in Assets/Scripts", for previews.
        /// </summary>
        public string Describe()
        {
            if (Scripts.Count == 0)
            {
                return "Nothing to write.";
            }

            var byFolder = Scripts.GroupBy(script => System.IO.Path.GetDirectoryName(script.Path)?.Replace('\\', '/'));

            return "Writes " + string.Join("; ", byFolder.Select(group => string.Join(" + ", group.Select(script => System.IO.Path.GetFileName(script.Path))) + " in " + group.Key));
        }
    }

    /// <summary>
    /// What the planner needs from the project, so planning can be tested without it.
    /// </summary>
    internal readonly struct ScriptPlanContext
    {
        public readonly Func<string, Type> FindType;

        public readonly Func<string, bool> FileExists;

        public readonly Func<string, string> RootNamespace;

        public readonly Func<string, ScriptAssembly> AssemblyOf;

        /// <summary>
        /// True for a folder inside an embedded package (in the project's Packages/ folder, so writable). Null: none is.
        /// </summary>
        public readonly Func<string, bool> IsEmbeddedPackageFolder;

        public ScriptPlanContext(Func<string, Type> findType, Func<string, bool> fileExists, Func<string, string> rootNamespace, Func<string, ScriptAssembly> assemblyOf,
                                 Func<string, bool> isEmbeddedPackageFolder = null)
        {
            FindType = findType;

            FileExists = fileExists;

            RootNamespace = rootNamespace;

            AssemblyOf = assemblyOf;

            IsEmbeddedPackageFolder = isEmbeddedPackageFolder;
        }

        /// <summary>
        /// The real project.
        /// </summary>
        public static ScriptPlanContext Current => new(PendingSteps.FindClass, path => File.Exists(path) || AssetDatabase.LoadMainAssetAtPath(path) != null,
                                                       CompilationPipeline.GetAssemblyRootNamespaceFromScriptPath, ScriptWriter.AssemblyOf, ScriptWriter.IsEmbeddedPackageFolder);
    }

    /// <summary>
    /// The assembly a script path compiles into: its name, its asmdef (empty for Assembly-CSharp and the other
    /// predefined assemblies) and the assemblies it references.
    /// </summary>
    internal readonly struct ScriptAssembly
    {
        public readonly string Name;

        public readonly string AsmdefPath;

        public readonly string[] References;

        public readonly bool Known;

        public ScriptAssembly(string name, string asmdefPath, string[] references, bool known)
        {
            Name = name;

            AsmdefPath = asmdefPath;

            References = references ?? Array.Empty<string>();

            Known = known;
        }
    }

    /// <summary>
    /// Writes new framework scripts safely: <see cref="Plan(IEnumerable{ScriptRequest})"/> checks everything first
    /// (valid name, no clash with an existing class or file, the namespace of the folder's assembly, and that the
    /// assembly can see the parent class), then <see cref="Write"/> writes every file and refreshes once, so there is a
    /// single compile per gesture.
    /// </summary>
    internal static class ScriptWriter
    {
        public const string RuntimeAssembly = "UEArchitecture.Runtime";

        private static Assembly[] _assemblies;

        public static ScriptPlan Plan(IEnumerable<ScriptRequest> requests)
        {
            return Plan(requests, ScriptPlanContext.Current, UEArchitectureEditorSettings.Current);
        }

        /// <summary>
        /// The plan for <paramref name="requests"/>; writes nothing.
        /// </summary>
        public static ScriptPlan Plan(IEnumerable<ScriptRequest> requests, ScriptPlanContext context, GeneratorConventions conventions)
        {
            var plan = new ScriptPlan();

            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var classes = new HashSet<string>(StringComparer.Ordinal);

            foreach (var request in requests ?? Array.Empty<ScriptRequest>())
            {
                if (request == null)
                {
                    continue;
                }

                var parent = request.Parent;

                var kind = request.Kind ?? ClassTemplates.KindOf(parent);

                if (parent == null && kind != CreateFrameworkScripts.Kind.Message)
                {
                    plan.Problems.Add("Pick the class the new script derives from.");

                    continue;
                }

                if (parent != null && (parent.IsSealed || parent.ContainsGenericParameters || parent.IsGenericType))
                {
                    plan.Problems.Add($"{parent.Name} cannot be a parent: it is {(parent.IsSealed ? "sealed" : "generic")}. Pick another class.");

                    continue;
                }

                var className = ClassTemplates.IsGameModeNamed(kind, parent)
                                    ? CreateFrameworkScripts.FileName(CreateFrameworkScripts.Kind.GameMode, request.TypedName)
                                    : CreateFrameworkScripts.ClassName(request.TypedName);

                var folder = GeneratorNaming.CleanFolder(string.IsNullOrWhiteSpace(request.Folder) ? conventions.ScriptsFolder : request.Folder);

                var folderProblem = GeneratorNaming.FolderProblem(folder);

                if (folderProblem.Length > 0 && request.AllowPackageFolder && !folder.Contains("..") && context.IsEmbeddedPackageFolder != null &&
                    context.IsEmbeddedPackageFolder(folder))
                {
                    folderProblem = string.Empty;
                }

                if (folderProblem.Length > 0)
                {
                    plan.Problems.Add(folderProblem);

                    continue;
                }

                // Whatever the parent: a class called Pawn would hide UE Architecture's Pawn in the other scripts of its
                // namespace (and in every script, from the global namespace), so they would stop compiling.
                var frameworkName = (kind.HasValue ? ClassTemplates.DeclaredClasses(kind.Value, className) : new[] { className }).FirstOrDefault(ClassTemplates.IsFrameworkName);

                if (frameworkName != null)
                {
                    plan.Problems.Add($"\"{frameworkName}\" is the name of a UE Architecture class: a class with this name hides it in your other scripts, so they would not compile. " +
                                      $"Pick another name, such as My{frameworkName}.");

                    continue;
                }

                var rootNamespace = context.RootNamespace($"{folder}/{className}.cs") ?? string.Empty;

                var split = kind == CreateFrameworkScripts.Kind.GameMode && (request.SplitStates ?? conventions.SplitStateFiles);

                // One file per class; a GameMode script with its states in their own files makes three.
                var files = new List<(string Name, string Source, CreateFrameworkScripts.Kind? Kind)>
                {
                    (className, kind.HasValue ? ClassTemplates.Source(kind.Value, className, rootNamespace, split, request.Tick) : ClassTemplates.ForParent(parent, className, rootNamespace), kind)
                };

                if (split)
                {
                    var baseName = ClassTemplates.BaseName(className, "GameMode", "Mode");

                    files.Add((baseName + "GameState", ClassTemplates.Source(CreateFrameworkScripts.Kind.GameState, baseName + "GameState", rootNamespace), CreateFrameworkScripts.Kind.GameState));

                    files.Add((baseName + "PlayerState", ClassTemplates.Source(CreateFrameworkScripts.Kind.PlayerState, baseName + "PlayerState", rootNamespace),
                               CreateFrameworkScripts.Kind.PlayerState));
                }

                foreach (var file in files)
                {
                    var path = $"{folder}/{file.Name}.cs";

                    var declared = (file.Kind.HasValue ? ClassTemplates.DeclaredClasses(file.Kind.Value, file.Name, split) : new[] { file.Name })
                                   .Select(name => string.IsNullOrEmpty(rootNamespace) ? name : rootNamespace + "." + name)
                                   .ToArray();

                    if (!paths.Add(path))
                    {
                        plan.Problems.Add($"{file.Name}.cs is asked for twice in {folder}.");

                        continue;
                    }

                    if (context.FileExists(path))
                    {
                        plan.Problems.Add($"{path} already exists. Pick another name, or use the existing class.");
                    }

                    foreach (var fullName in declared)
                    {
                        if (!classes.Add(fullName))
                        {
                            plan.Problems.Add($"The class {fullName} is asked for twice.");

                            continue;
                        }

                        var existing = context.FindType(fullName);

                        if (existing != null)
                        {
                            plan.Problems.Add($"A class called {fullName} already exists (in {existing.Assembly.GetName().Name}). Pick another name.");
                        }
                    }

                    CheckHiddenNames_Internal(plan, context, file.Kind, parent, file.Name, rootNamespace, path);

                    CheckReferences_Internal(plan, context.AssemblyOf(path), parent, path);

                    plan.Scripts.Add(new PlannedScript
                    {
                        ClassName = file.Name,
                        Path = path,
                        Namespace = rootNamespace,
                        Source = file.Source,
                        Parent = parent,
                        DeclaredClasses = declared
                    });
                }
            }

            return plan;
        }

        /// <summary>
        /// Writes every planned script, queues <paramref name="then"/> (work for after the compile) and refreshes the
        /// project once. Writes nothing when the plan has problems.
        /// </summary>
        public static ActionResult Write(ScriptPlan plan, IEnumerable<PendingStep> then = null, string verb = "new-script")
        {
            var result = new ActionResult(verb);

            if (plan == null || !plan.Ok)
            {
                foreach (var problem in plan?.Problems ?? new List<string> { "Nothing to write." })
                {
                    result.AddProblem(problem);
                }

                if (plan != null && plan.Scripts.Count == 0 && plan.Problems.Count == 0)
                {
                    result.AddProblem("Nothing to write.");
                }

                return result;
            }

            foreach (var script in plan.Scripts.Where(script => File.Exists(script.Path)))
            {
                result.AddProblem($"{script.Path} appeared since the plan was made; nothing was written.");
            }

            if (!result.Ok)
            {
                return result;
            }

            foreach (var script in plan.Scripts)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(script.Path) ?? "Assets");

                File.WriteAllText(script.Path, script.Source);

                result.AddCreated(script.Path);
            }

            foreach (var step in then ?? Array.Empty<PendingStep>())
            {
                PendingSteps.Enqueue(step, result);
            }

            AssetDatabase.Refresh();

            result.Message = plan.Describe() + (result.Pending ? ". The rest finishes after the compile." : ".");

            return result;
        }

        /// <summary>
        /// True for a folder inside an embedded package: one kept in the project's Packages/ folder, which Unity lets you
        /// change (packages from the registry, Git or another folder on disk are read-only).
        /// </summary>
        public static bool IsEmbeddedPackageFolder(string folder)
        {
            var clean = GeneratorNaming.CleanFolder(folder);

            if (!clean.StartsWith("Packages/", StringComparison.Ordinal))
            {
                return false;
            }

            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(clean);

            return package != null && package.source == UnityEditor.PackageManager.PackageSource.Embedded;
        }

        /// <summary>
        /// The assembly a script at <paramref name="scriptPath"/> would compile into.
        /// </summary>
        public static ScriptAssembly AssemblyOf(string scriptPath)
        {
            var asmdef = CompilationPipeline.GetAssemblyDefinitionFilePathFromScriptPath(scriptPath) ?? string.Empty;

            var name = Path.GetFileNameWithoutExtension(CompilationPipeline.GetAssemblyNameFromScriptPath(scriptPath) ?? string.Empty);

            _assemblies ??= CompilationPipeline.GetAssemblies();

            var assembly = _assemblies.FirstOrDefault(candidate => candidate.name == name);

            return new ScriptAssembly(name, asmdef, assembly?.assemblyReferences.Select(reference => reference.name).ToArray(), assembly != null);
        }

        /// <summary>
        /// Refuses a script whose code names a class (a UE Architecture class of its template, or its parent) that a class
        /// of the project would hide: a class with the same name in the script's namespace, a namespace around it, or the
        /// global namespace wins over "using ModernWestern.UEArchitecture;", so the script would not compile (a global
        /// PlayerController MonoBehaviour breaks a new GameMode's OnPostLogin(PlayerController player)).
        /// </summary>
        private static void CheckHiddenNames_Internal(ScriptPlan plan, ScriptPlanContext context, CreateFrameworkScripts.Kind? kind, Type parent, string className, string rootNamespace,
                                                      string path)
        {
            var names = new List<(string Name, Type Meant)>();

            if (kind.HasValue)
            {
                names.AddRange(ClassTemplates.ReferencedFrameworkTypeNames(kind.Value).Select(name => (name, (Type)null)));
            }
            else if (parent != null && !ClassTemplates.TypeReference(parent, className).StartsWith("global::", StringComparison.Ordinal))
            {
                names.Add((parent.Name, parent));
            }

            // The namespaces C# looks in before the file's usings: the script's own, each one around it, then the global one.
            var scopes = new List<string>();

            for (var scope = rootNamespace ?? string.Empty; scope.Length > 0; scope = scope.LastIndexOf('.') < 0 ? string.Empty : scope.Substring(0, scope.LastIndexOf('.')))
            {
                scopes.Add(scope);
            }

            scopes.Add(string.Empty);

            foreach (var (name, meant) in names)
            {
                foreach (var scope in scopes)
                {
                    var found = context.FindType(scope.Length == 0 ? name : scope + "." + name);

                    if (found == null)
                    {
                        continue;
                    }

                    if (found != meant && !ClassTemplates.IsFrameworkType(found))
                    {
                        var shown = name.Split('`')[0];

                        plan.Problems.Add($"Your class {found.FullName} hides {(meant != null ? meant.FullName : "UE Architecture's " + shown)} in {System.IO.Path.GetFileName(path)} " +
                                          $"({(scope.Length == 0 ? "global namespace" : "namespace " + scope)}), so the script would not compile. " +
                                          (scope.Length == 0 ? $"Rename {found.Name}, or put it in a namespace." : $"Rename {found.Name}, or move it out of namespace {scope}."));
                    }

                    // The first class found is the one the script would use.
                    break;
                }
            }
        }

        private static void CheckReferences_Internal(ScriptPlan plan, ScriptAssembly target, Type parent, string path)
        {
            if (!target.Known || string.IsNullOrEmpty(target.Name))
            {
                plan.Notes.Add($"Could not check which assemblies {path} can use (its assembly has not compiled yet).");

                return;
            }

            var needed = new List<string> { RuntimeAssembly };

            var parentAssembly = parent?.Assembly.GetName().Name;

            if (!string.IsNullOrEmpty(parentAssembly) && !needed.Contains(parentAssembly))
            {
                needed.Add(parentAssembly);
            }

            var where = string.IsNullOrEmpty(target.AsmdefPath) ? target.Name : $"{target.Name} ({target.AsmdefPath})";

            foreach (var reference in needed)
            {
                if (reference == target.Name || target.References.Contains(reference))
                {
                    continue;
                }

                if (reference.StartsWith("Assembly-CSharp", StringComparison.Ordinal))
                {
                    plan.Problems.Add($"{parent.Name} is in {reference}, which assemblies with an asmdef cannot use. Pick a folder without an asmdef, or move {parent.Name} into one.");

                    continue;
                }

                plan.Problems.Add($"The assembly {where} does not reference {reference}, so the new script would not compile. Add {reference} to its references, or pick another folder.");
            }
        }
    }
}
