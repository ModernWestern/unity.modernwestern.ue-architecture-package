using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// What to convert: a MonoBehaviour script into a framework class (Convert to GameMode, Pawn...).
    /// </summary>
    internal sealed class ConvertRequest
    {
        /// <summary>
        /// The script's asset path ("Assets/Game/GameplayScene.cs").
        /// </summary>
        public string ScriptPath = string.Empty;

        /// <summary>
        /// The script's text as it is on disk.
        /// </summary>
        public string Source = string.Empty;

        /// <summary>
        /// The script's bytes as they are on disk (see <see cref="ScriptText"/>), or null to take them as
        /// <see cref="Source"/> saved as UTF-8 without a byte order mark.
        /// </summary>
        public byte[] SourceBytes;

        /// <summary>
        /// True when the file starts with a UTF-8 byte order mark: the edited file keeps it.
        /// </summary>
        public bool Bom;

        /// <summary>
        /// The compiled class (its fields and methods are checked against the new base class), or null when it is not known.
        /// </summary>
        public Type CurrentType;

        /// <summary>
        /// The class's name and namespace in the code (from <see cref="CurrentType"/> when it is known).
        /// </summary>
        public string ClassName = string.Empty;

        public string Namespace = string.Empty;

        /// <summary>
        /// What it derives from now ("MonoBehaviour").
        /// </summary>
        public string CurrentBase = "MonoBehaviour";

        /// <summary>
        /// The framework class it becomes: GameMode, Pawn, PlayerController, AIController, HUD, PlayerStart, LoadingScreen
        /// or GameInstance.
        /// </summary>
        public Type FrameworkBase;

        /// <summary>
        /// The new name of the class and its file, or empty to keep both.
        /// </summary>
        public string NewClassName = string.Empty;

        /// <summary>
        /// For a GameMode: name its GameState and PlayerState (GameMode&lt;A, B&gt;); false writes a plain ": GameMode".
        /// </summary>
        public bool States = true;

        /// <summary>
        /// For a GameMode: the GameState and PlayerState classes, existing or new (written next to the script). Empty =
        /// named after the class ("GameplayGameState"), reusing a class with that name when there is one.
        /// </summary>
        public string GameState = string.Empty;

        public string PlayerState = string.Empty;
    }

    /// <summary>
    /// The project as a conversion sees it (replaced in tests).
    /// </summary>
    internal readonly struct ConvertContext
    {
        public readonly Func<string, Type> FindType;

        public readonly Func<string, bool> FileExists;

        /// <summary>
        /// The other scripts that name a class (class name, script path to leave out).
        /// </summary>
        public readonly Func<string, string, IReadOnlyList<string>> ScriptsUsing;

        public ConvertContext(Func<string, Type> findType, Func<string, bool> fileExists, Func<string, string, IReadOnlyList<string>> scriptsUsing)
        {
            FindType = findType ?? (_ => null);

            FileExists = fileExists ?? (_ => false);

            ScriptsUsing = scriptsUsing ?? ((_, _) => Array.Empty<string>());
        }

        public static ConvertContext Current => new(PendingSteps.FindClass, path => File.Exists(ClassConversion.PhysicalPath(path)), ClassConversion.ScriptsNaming);
    }

    /// <summary>
    /// The previewed result of a conversion: the edited script, the new files, and what the user should know. Nothing
    /// is written until <see cref="ClassConversion.Apply"/>.
    /// </summary>
    internal sealed class ConvertPlan
    {
        public readonly List<string> Problems = new();

        public readonly List<string> Notes = new();

        /// <summary>
        /// Methods to turn into overrides that call the base class ("OnDestroy: make it protected override and call base.OnDestroy()").
        /// </summary>
        public readonly List<string> BaseCalls = new();

        /// <summary>
        /// New files: GameState and PlayerState scripts.
        /// </summary>
        public readonly List<KeyValuePair<string, string>> NewFiles = new();

        public string ScriptPath = string.Empty;

        public string NewScriptPath = string.Empty;

        public string ClassName = string.Empty;

        public string NewClassName = string.Empty;

        public string Namespace = string.Empty;

        public string BaseText = string.Empty;

        public string OriginalSource = string.Empty;

        /// <summary>
        /// The script's bytes when the plan was made: the file must still have them to be written, and they are put back
        /// as they were if writing fails.
        /// </summary>
        public byte[] OriginalBytes = Array.Empty<byte>();

        /// <summary>
        /// True when the script starts with a UTF-8 byte order mark: the new text is written with it too.
        /// </summary>
        public bool Bom;

        public string NewSource = string.Empty;

        public string Before = string.Empty;

        public string After = string.Empty;

        public int Line;

        /// <summary>
        /// True when the code already derives from the framework class: there is nothing to write.
        /// </summary>
        public bool AlreadyDone;

        public Type FrameworkBase;

        public bool Ok => Problems.Count == 0;

        public bool Renames => !string.IsNullOrEmpty(NewClassName) && NewClassName != ClassName;

        public string FullName(string className) => string.IsNullOrEmpty(Namespace) ? className : Namespace + "." + className;

        /// <summary>
        /// The preview, line by line.
        /// </summary>
        public List<string> Describe()
        {
            var lines = new List<string>();

            if (AlreadyDone)
            {
                lines.Add($"{ClassName} already derives from {FrameworkBase?.Name} in its code.");

                return lines;
            }

            lines.Add($"{Path.GetFileName(ScriptPath)}, line {Line}:");

            lines.Add("  - " + Before);

            lines.Add("  + " + After);

            if (Renames)
            {
                lines.Add($"Renames the class and its file to {NewClassName} ({Path.GetFileName(NewScriptPath)}); the file keeps its GUID, so scenes and prefabs keep the component.");
            }

            foreach (var file in NewFiles)
            {
                lines.Add("Writes " + file.Key);
            }

            foreach (var call in BaseCalls)
            {
                lines.Add("Base call: " + call);
            }

            lines.AddRange(Notes);

            return lines;
        }
    }

    /// <summary>
    /// A script file read for an edit: its text, its exact bytes and whether it starts with a UTF-8 byte order mark. Only
    /// UTF-8 files are edited: any other encoding would lose its accents when written back, so it is refused.
    /// </summary>
    internal sealed class ScriptText
    {
        private static readonly byte[] Preamble = { 0xEF, 0xBB, 0xBF };

        // Throws on bytes that are not UTF-8, instead of turning them into U+FFFD.
        private static readonly UTF8Encoding Strict = new(false, true);

        private static readonly UTF8Encoding Plain = new(false);

        public string Text = string.Empty;

        public byte[] Bytes = Array.Empty<byte>();

        public bool Bom;

        /// <summary>
        /// Reads a script (<paramref name="assetPath"/> is only for the message). False, with the reason, when it cannot be
        /// read or is not saved as UTF-8.
        /// </summary>
        public static bool TryRead(string physicalPath, string assetPath, out ScriptText script, out string problem)
        {
            script = null;

            problem = string.Empty;

            byte[] bytes;

            try
            {
                bytes = File.ReadAllBytes(physicalPath);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                problem = $"{assetPath} cannot be read: {exception.Message}";

                return false;
            }

            if (!TryDecode(bytes, out var text, out var bom))
            {
                problem = $"{assetPath} is not saved as UTF-8; re-save it as UTF-8 first (in your code editor), so its accents and symbols are kept.";

                return false;
            }

            script = new ScriptText { Text = text, Bytes = bytes, Bom = bom };

            return true;
        }

        /// <summary>
        /// Decodes UTF-8 bytes (with or without a byte order mark). False for any other encoding (UTF-16, Windows-1252...).
        /// </summary>
        public static bool TryDecode(byte[] bytes, out string text, out bool bom)
        {
            text = string.Empty;

            bom = bytes.Length >= Preamble.Length && bytes[0] == Preamble[0] && bytes[1] == Preamble[1] && bytes[2] == Preamble[2];

            var start = bom ? Preamble.Length : 0;

            try
            {
                text = Strict.GetString(bytes, start, bytes.Length - start);

                return true;
            }
            catch (DecoderFallbackException)
            {
                return false;
            }
        }

        /// <summary>
        /// The bytes to write for <paramref name="text"/>: UTF-8, with the byte order mark when the file had one.
        /// </summary>
        public static byte[] Encode(string text, bool bom)
        {
            var body = Plain.GetBytes(text ?? string.Empty);

            if (!bom)
            {
                return body;
            }

            var bytes = new byte[Preamble.Length + body.Length];

            Buffer.BlockCopy(Preamble, 0, bytes, 0, Preamble.Length);

            Buffer.BlockCopy(body, 0, bytes, Preamble.Length, body.Length);

            return bytes;
        }

        public static bool SameBytes(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
            {
                return a == b;
            }

            for (var i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// <see cref="ClassConversion.ScriptsNaming"/> asked once per class name, for a window that plans again on every
    /// keystroke (reading every script of the project each time would be slow).
    /// </summary>
    internal sealed class ScriptsNamingCache
    {
        private readonly Func<string, string, IReadOnlyList<string>> _lookup;

        private readonly Dictionary<string, IReadOnlyList<string>> _known = new(StringComparer.Ordinal);

        public ScriptsNamingCache(Func<string, string, IReadOnlyList<string>> lookup = null)
        {
            _lookup = lookup ?? new Func<string, string, IReadOnlyList<string>>(ClassConversion.ScriptsNaming);
        }

        public IReadOnlyList<string> Get(string className, string exceptPath)
        {
            var key = (className ?? string.Empty) + "|" + (exceptPath ?? string.Empty);

            if (!_known.TryGetValue(key, out var found))
            {
                found = _lookup(className, exceptPath);

                _known[key] = found;
            }

            return found;
        }

        public void Clear()
        {
            _known.Clear();
        }
    }

    /// <summary>
    /// Convert to framework class: plans (pure, previewed) and applies the change of a MonoBehaviour script into a
    /// GameMode, Pawn, PlayerController, AIController, HUD, PlayerStart, LoadingScreen or GameInstance: the base class in
    /// its declaration, the GameState and PlayerState of a GameMode (new files or existing classes), and optionally the
    /// class and file name (through AssetDatabase.RenameAsset, so the GUID and every reference stay). All or nothing.
    /// </summary>
    internal static class ClassConversion
    {
        public const string FrameworkNamespace = "ModernWestern.UEArchitecture";

        /// <summary>
        /// The framework classes a MonoBehaviour can become.
        /// </summary>
        public static readonly Type[] Bases =
        {
            typeof(GameMode), typeof(PlayerController), typeof(AIController), typeof(Pawn), typeof(HUD), typeof(PlayerStart), typeof(LoadingScreen), typeof(GameInstance)
        };

        /// <summary>
        /// The class name a conversion suggests: "GameplayScene" becomes "GameplayGameMode"; a name that already ends like
        /// the framework class ("ArenaMode", "HeroPawn") gives null (keep it).
        /// </summary>
        public static string SuggestName(string className, Type frameworkBase)
        {
            if (string.IsNullOrEmpty(className) || frameworkBase == null)
            {
                return null;
            }

            var suffix = frameworkBase.Name;

            if (className.EndsWith(suffix, StringComparison.Ordinal) || frameworkBase == typeof(GameMode) && className.EndsWith("Mode", StringComparison.Ordinal))
            {
                return null;
            }

            var stem = ClassTemplates.BaseName(className, "Scene", "Manager", "Behaviour", "Behavior", "Controller", "Handler", "System", "Script");

            return (stem.Length == 0 ? className : stem) + suffix;
        }

        /// <summary>
        /// True when renaming is the usual choice for this framework class (the Unreal names end with it).
        /// </summary>
        public static bool RenamesByDefault(Type frameworkBase)
        {
            return frameworkBase == typeof(GameMode) || frameworkBase == typeof(PlayerController) || frameworkBase == typeof(AIController) || frameworkBase == typeof(HUD) ||
                   frameworkBase == typeof(GameInstance);
        }

        /// <summary>
        /// The name a GameMode's states are named after: "Gameplay" for GameplayGameMode or GameplayScene.
        /// </summary>
        public static string StateStem(string className)
        {
            var stem = ClassTemplates.BaseName(className ?? string.Empty, "GameMode", "Mode", "Scene", "Manager");

            return stem.Length == 0 ? className : stem;
        }

        public static ConvertPlan Plan(ConvertRequest request)
        {
            return Plan(request, ConvertContext.Current);
        }

        /// <summary>
        /// The plan of a conversion; writes nothing.
        /// </summary>
        public static ConvertPlan Plan(ConvertRequest request, ConvertContext context)
        {
            var plan = new ConvertPlan();

            if (request == null)
            {
                plan.Problems.Add("Nothing to convert.");

                return plan;
            }

            plan.ScriptPath = request.ScriptPath ?? string.Empty;

            plan.ClassName = request.ClassName ?? string.Empty;

            plan.Namespace = request.Namespace ?? string.Empty;

            plan.FrameworkBase = request.FrameworkBase;

            plan.OriginalSource = request.Source ?? string.Empty;

            plan.Bom = request.Bom;

            plan.OriginalBytes = request.SourceBytes ?? ScriptText.Encode(plan.OriginalSource, request.Bom);

            plan.NewScriptPath = plan.ScriptPath;

            if (request.FrameworkBase == null || Array.IndexOf(Bases, request.FrameworkBase) < 0)
            {
                plan.Problems.Add($"Pick what it becomes: {string.Join(", ", Bases.Select(type => type.Name))}.");

                return plan;
            }

            var frameworkBase = request.FrameworkBase;

            // Already converted (perhaps waiting for the compile): nothing to do, so running it again is safe.
            var check = ScriptPatcher.Reparent(plan.OriginalSource, plan.ClassName, frameworkBase.Name, request.CurrentBase);

            if (check.Ok && !check.Changed)
            {
                plan.AlreadyDone = true;

                plan.NewSource = plan.OriginalSource;

                plan.Before = plan.After = check.Before;

                plan.Line = check.Line;

                return plan;
            }

            if (!check.Ok)
            {
                plan.Problems.Add(check.Problem);

                return plan;
            }

            var newName = (request.NewClassName ?? string.Empty).Trim();

            if (newName.Length > 0 && newName != plan.ClassName)
            {
                if (!ScriptPatcher.IsIdentifier(newName))
                {
                    plan.Problems.Add($"\"{newName}\" is not a valid class name.");
                }
                else if (ClassTemplates.IsFrameworkName(newName))
                {
                    plan.Problems.Add($"\"{newName}\" is the name of a UE Architecture class: it would hide it in your other scripts. Pick another name.");
                }
                else
                {
                    plan.NewClassName = newName;

                    plan.NewScriptPath = GeneratorNaming.FolderOf(plan.ScriptPath) + "/" + newName + ".cs";

                    if (context.FileExists(plan.NewScriptPath))
                    {
                        plan.Problems.Add($"{plan.NewScriptPath} already exists. Pick another name.");
                    }

                    var existing = context.FindType(plan.FullName(newName));

                    if (existing != null)
                    {
                        plan.Problems.Add($"A class called {existing.FullName} already exists. Pick another name.");
                    }

                    var users = context.ScriptsUsing(plan.ClassName, plan.ScriptPath);

                    if (users.Count > 0)
                    {
                        plan.Problems.Add($"Other scripts use the name {plan.ClassName} ({string.Join(", ", users.Take(5).Select(Path.GetFileName))}{(users.Count > 5 ? "..." : string.Empty)}): " +
                                          "renaming would break them. Convert without renaming, or rename it in your code editor first.");
                    }
                }
            }

            var finalName = plan.Renames ? plan.NewClassName : plan.ClassName;

            var folder = GeneratorNaming.FolderOf(plan.ScriptPath);

            var baseText = Reference_Internal(frameworkBase, plan.Namespace, context);

            if (frameworkBase == typeof(GameMode) && request.States)
            {
                var stem = StateStem(finalName);

                var gameState = State_Internal(plan, context, request.GameState, stem + "GameState", typeof(GameState), CreateFrameworkScripts.Kind.GameState, folder);

                var playerState = State_Internal(plan, context, request.PlayerState, stem + "PlayerState", typeof(PlayerState), CreateFrameworkScripts.Kind.PlayerState, folder);

                if (gameState != null && playerState != null)
                {
                    baseText = $"{baseText}<{gameState}, {playerState}>";
                }
            }

            plan.BaseText = baseText;

            if (!plan.Ok)
            {
                return plan;
            }

            var patched = ScriptPatcher.Reparent(plan.OriginalSource, plan.ClassName, baseText, request.CurrentBase);

            if (!patched.Ok)
            {
                plan.Problems.Add(patched.Problem);

                return plan;
            }

            var source = patched.Source;

            if (plan.Renames)
            {
                var renamed = ScriptPatcher.RenameClass(source, plan.ClassName, plan.NewClassName);

                if (!renamed.Ok)
                {
                    plan.Problems.Add(renamed.Problem);

                    return plan;
                }

                source = renamed.Source;
            }

            var after = ScriptPatcher.Reparent(source, finalName, baseText, null);

            plan.Before = patched.Before;

            plan.After = after.Ok ? after.Before : patched.After;

            plan.Line = patched.Line;

            if (!baseText.StartsWith("global::", StringComparison.Ordinal) && plan.Namespace != FrameworkNamespace && !plan.Namespace.StartsWith(FrameworkNamespace + ".", StringComparison.Ordinal))
            {
                var withUsing = ScriptPatcher.EnsureUsing(source, FrameworkNamespace);

                if (!withUsing.Ok)
                {
                    plan.Problems.Add(withUsing.Problem);

                    return plan;
                }

                if (withUsing.Changed)
                {
                    plan.Notes.Add($"Adds \"using {FrameworkNamespace};\" at the top.");
                }

                source = withUsing.Source;
            }

            plan.NewSource = source;

            Members_Internal(plan, request.CurrentType, frameworkBase);

            return plan;
        }

        /// <summary>
        /// Writes the plan: the new GameState and PlayerState scripts, the edited script, then the rename (which keeps the
        /// GUID). If any step fails, everything written is put back as it was. One refresh at the end, so one compile.
        /// </summary>
        public static bool Apply(ConvertPlan plan, ActionResult result)
        {
            if (plan == null || !plan.Ok)
            {
                foreach (var problem in plan?.Problems ?? new List<string> { "Nothing to convert." })
                {
                    result.AddProblem(problem);
                }

                return false;
            }

            if (plan.AlreadyDone)
            {
                return true;
            }

            var scriptFile = PhysicalPath(plan.ScriptPath);

            // Byte for byte: an edit, a different encoding or a byte order mark added or removed since the preview all count.
            if (!File.Exists(scriptFile) || !ScriptText.SameBytes(File.ReadAllBytes(scriptFile), plan.OriginalBytes))
            {
                result.AddProblem($"{plan.ScriptPath} changed since the preview was made. Nothing was written: look at the preview again.");

                return false;
            }

            var written = new List<string>();

            var renamed = false;

            try
            {
                foreach (var file in plan.NewFiles)
                {
                    var physical = PhysicalPath(file.Key);

                    if (File.Exists(physical))
                    {
                        throw new IOException($"{file.Key} appeared since the preview was made.");
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(physical) ?? ".");

                    File.WriteAllBytes(physical, ScriptText.Encode(file.Value, false));

                    written.Add(physical);
                }

                File.WriteAllBytes(scriptFile, ScriptText.Encode(plan.NewSource, plan.Bom));

                if (plan.Renames)
                {
                    var guid = AssetDatabase.AssetPathToGUID(plan.ScriptPath);

                    if (!string.IsNullOrEmpty(guid) && AssetDatabase.LoadMainAssetAtPath(plan.ScriptPath) != null)
                    {
                        var error = AssetDatabase.RenameAsset(plan.ScriptPath, plan.NewClassName);

                        if (!string.IsNullOrEmpty(error))
                        {
                            throw new IOException(error);
                        }
                    }
                    else
                    {
                        // A file Unity does not import (a folder ending in "~"): moved with its .meta, if it has one.
                        var target = PhysicalPath(plan.NewScriptPath);

                        File.Move(scriptFile, target);

                        if (File.Exists(scriptFile + ".meta"))
                        {
                            File.Move(scriptFile + ".meta", target + ".meta");
                        }
                    }

                    renamed = true;
                }
            }
            catch (Exception exception)
            {
                if (!renamed && File.Exists(scriptFile))
                {
                    File.WriteAllBytes(scriptFile, plan.OriginalBytes);
                }

                foreach (var path in written)
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }

                result.AddProblem($"Nothing was changed: {exception.Message}");

                return false;
            }

            foreach (var file in plan.NewFiles)
            {
                result.AddCreated(file.Key);
            }

            result.AddChanged($"{plan.ScriptPath}: {plan.ClassName} : {plan.BaseText}");

            if (plan.Renames)
            {
                result.AddChanged($"{plan.ScriptPath} renamed to {plan.NewScriptPath}");
            }

            return true;
        }

        /// <summary>
        /// The file on disk of an asset path ("Packages/com.x/..." resolves to the package's folder).
        /// </summary>
        public static string PhysicalPath(string assetPath)
        {
            var clean = (assetPath ?? string.Empty).Replace('\\', '/');

            if (!clean.StartsWith("Packages/", StringComparison.Ordinal))
            {
                return clean;
            }

            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(clean);

            if (package == null || string.IsNullOrEmpty(package.resolvedPath))
            {
                return clean;
            }

            var root = "Packages/" + package.name;

            return clean.StartsWith(root, StringComparison.Ordinal) ? package.resolvedPath.Replace('\\', '/') + clean.Substring(root.Length) : clean;
        }

        /// <summary>
        /// True when a script may be edited: under Assets/, or in a package that lives in the project or in a local folder.
        /// Packages from the registry or Git are read-only.
        /// </summary>
        public static bool IsEditable(string assetPath, out string problem)
        {
            problem = string.Empty;

            var clean = (assetPath ?? string.Empty).Replace('\\', '/');

            if (clean.StartsWith("Assets/", StringComparison.Ordinal))
            {
                return true;
            }

            if (clean.StartsWith("Packages/", StringComparison.Ordinal))
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(clean);

                if (package != null && (package.source == UnityEditor.PackageManager.PackageSource.Embedded || package.source == UnityEditor.PackageManager.PackageSource.Local))
                {
                    return true;
                }

                problem = $"{clean} is in a read-only package ({package?.name ?? "unknown"}). Copy the script into Assets/ first.";

                return false;
            }

            problem = $"{clean} is not a script of this project.";

            return false;
        }

        /// <summary>
        /// The scripts under Assets/ (and the local package of <paramref name="exceptPath"/>) whose code names
        /// <paramref name="className"/>, except <paramref name="exceptPath"/>. Qualified names (MyGame.GameplayScene),
        /// members called like it and the code inside interpolated strings ($"{nameof(GameplayScene)}") count too: a rename
        /// is refused rather than risk breaking one of them.
        /// </summary>
        public static IReadOnlyList<string> ScriptsNaming(string className, string exceptPath)
        {
            var found = new List<string>();

            if (!ScriptPatcher.IsIdentifier(className))
            {
                return found;
            }

            foreach (var guid in AssetDatabase.FindAssets("t:MonoScript"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);

                if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || string.Equals(path, exceptPath, StringComparison.Ordinal) ||
                    !IsEditable(path, out _))
                {
                    continue;
                }

                var physical = PhysicalPath(path);

                if (File.Exists(physical) && Names(File.ReadAllText(physical), className))
                {
                    found.Add(path);
                }
            }

            return found;
        }

        /// <summary>
        /// True when the code of <paramref name="source"/> names <paramref name="className"/>: alone, qualified
        /// (MyGame.GameplayScene), as a member's name, or inside the holes of an interpolated string. Comments and plain
        /// strings do not count.
        /// </summary>
        public static bool Names(string source, string className)
        {
            if (!ScriptPatcher.IsIdentifier(className))
            {
                return false;
            }

            return System.Text.RegularExpressions.Regex.IsMatch(ScriptPatcher.Mask(source, true), $@"(?<!\w){System.Text.RegularExpressions.Regex.Escape(className)}(?!\w)");
        }

        /// <summary>
        /// The Unity-serialized instance fields a class declares itself (public, or [SerializeField]/[SerializeReference]).
        /// </summary>
        public static IEnumerable<FieldInfo> SerializedFields(Type type, bool declaredOnly)
        {
            for (var current = type; current != null && current != typeof(MonoBehaviour) && current != typeof(object); current = current.BaseType)
            {
                foreach (var field in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (IsSerialized(field))
                    {
                        yield return field;
                    }
                }

                if (declaredOnly)
                {
                    yield break;
                }
            }
        }

        public static bool IsSerialized(FieldInfo field)
        {
            return !field.IsStatic && !field.IsInitOnly && !field.IsLiteral && !field.IsDefined(typeof(NonSerializedAttribute), false) &&
                   (field.IsPublic || field.IsDefined(typeof(SerializeField), false) || field.IsDefined(typeof(SerializeReference), false));
        }

        // How the script names a framework class: by name, or in full when a class of the project with that name would win.
        private static string Reference_Internal(Type type, string scriptNamespace, ConvertContext context)
        {
            for (var scope = scriptNamespace ?? string.Empty; ; scope = scope.LastIndexOf('.') < 0 ? string.Empty : scope.Substring(0, scope.LastIndexOf('.')))
            {
                var found = context.FindType(scope.Length == 0 ? type.Name : scope + "." + type.Name);

                if (found != null && found != type)
                {
                    return "global::" + type.FullName;
                }

                if (scope.Length == 0)
                {
                    break;
                }
            }

            return type.Name;
        }

        // The text naming a GameMode's GameState or PlayerState, and the new file when the class does not exist yet.
        private static string State_Internal(ConvertPlan plan, ConvertContext context, string asked, string fallback, Type baseType, CreateFrameworkScripts.Kind kind, string folder)
        {
            var name = string.IsNullOrWhiteSpace(asked) ? fallback : asked.Trim();

            var existing = (plan.Namespace.Length > 0 ? context.FindType(plan.Namespace + "." + name) : null) ?? context.FindType(name) ??
                           (name == baseType.Name ? baseType : null);

            if (existing != null)
            {
                if (!baseType.IsAssignableFrom(existing) || existing.IsAbstract || existing.ContainsGenericParameters || existing.GetConstructor(Type.EmptyTypes) == null)
                {
                    plan.Problems.Add($"{existing.FullName} is not a {baseType.Name} class a GameMode can create (it needs a public parameterless constructor). Pick another.");

                    return null;
                }

                plan.Notes.Add($"Uses the existing {baseType.Name} {existing.FullName}.");

                if (existing == baseType || ClassTemplates.IsFrameworkType(existing))
                {
                    return Reference_Internal(existing, plan.Namespace, context);
                }

                return existing.Namespace == plan.Namespace || string.IsNullOrEmpty(existing.Namespace) && plan.Namespace.Length == 0
                           ? existing.Name
                           : "global::" + existing.FullName.Replace('+', '.');
            }

            if (!ScriptPatcher.IsIdentifier(name))
            {
                plan.Problems.Add($"\"{name}\" is not a valid class name.");

                return null;
            }

            if (ClassTemplates.IsFrameworkName(name))
            {
                plan.Problems.Add($"\"{name}\" is the name of a UE Architecture class. Pick another name.");

                return null;
            }

            var path = $"{folder}/{name}.cs";

            if (context.FileExists(path) || plan.NewFiles.Any(file => file.Key == path))
            {
                plan.Problems.Add($"{path} already exists but does not declare {name}. Pick another name.");

                return null;
            }

            plan.NewFiles.Add(new KeyValuePair<string, string>(path, ClassTemplates.Source(kind, name, plan.Namespace)));

            return name;
        }

        // Fields that would clash, members that would hide the base class's, and methods that should call it.
        private static void Members_Internal(ConvertPlan plan, Type current, Type frameworkBase)
        {
            var baseFields = SerializedFields(frameworkBase, false).ToList();

            if (current != null)
            {
                foreach (var field in SerializedFields(current, true))
                {
                    if (baseFields.Any(other => other.Name == field.Name))
                    {
                        plan.Problems.Add($"Rename the field \"{field.Name}\" first: {frameworkBase.Name} has a field with that name, and Unity cannot keep both.");
                    }
                }

                const BindingFlags Own = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

                var virtuals = frameworkBase.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                                            .Where(method => (method.IsVirtual || method.IsAbstract) && !method.IsFinal && !method.IsPrivate && !method.IsAssembly &&
                                                             method.DeclaringType != typeof(object) && typeof(MonoBehaviour).IsAssignableFrom(method.DeclaringType) &&
                                                             method.DeclaringType != typeof(MonoBehaviour) && method.DeclaringType != typeof(Behaviour) &&
                                                             method.DeclaringType != typeof(Component))
                                            .ToList();

                foreach (var method in current.GetMethods(Own))
                {
                    var match = virtuals.FirstOrDefault(other => other.Name == method.Name && other.GetParameters().Length == method.GetParameters().Length);

                    if (match == null || method.IsSpecialName)
                    {
                        continue;
                    }

                    var line = match.IsAbstract
                                   ? $"{method.Name}: make it \"{Access_Internal(match)} override\"."
                                   : $"{method.Name}: make it \"{Access_Internal(match)} override\" and call base.{method.Name}(), or {frameworkBase.Name}'s own {method.Name} is skipped.";

                    if (!plan.BaseCalls.Contains(line))
                    {
                        plan.BaseCalls.Add(line);
                    }
                }

                var visible = new HashSet<string>(frameworkBase.GetMembers(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                                           .Where(member => member.DeclaringType != null && ClassTemplates.IsFrameworkType(member.DeclaringType.IsGenericType
                                                                                                                               ? member.DeclaringType.GetGenericTypeDefinition()
                                                                                                                               : member.DeclaringType) &&
                                                            IsVisible_Internal(member))
                                           .Select(member => member.Name), StringComparer.Ordinal);

                foreach (var member in current.GetMembers(Own).Where(member => !(member is MethodInfo method && (method.IsSpecialName || virtuals.Any(other => other.Name == method.Name)))))
                {
                    if (member is Type || member.Name.StartsWith("<", StringComparison.Ordinal) || !visible.Contains(member.Name))
                    {
                        continue;
                    }

                    var note = $"{member.Name} hides {frameworkBase.Name}.{member.Name} (a compiler warning). Rename it if you did not mean the framework's.";

                    if (!plan.Notes.Contains(note))
                    {
                        plan.Notes.Add(note);
                    }
                }
            }

            if (baseFields.Count > 0)
            {
                plan.Notes.Add($"{frameworkBase.Name}'s fields start with their defaults ({string.Join(", ", baseFields.Select(field => ObjectNames.NicifyVariableName(field.Name)))}): fill them in the Inspector.");
            }
        }

        private static string Access_Internal(MethodInfo method)
        {
            return method.IsPublic ? "public" : method.IsFamilyOrAssembly ? "protected internal" : "protected";
        }

        private static bool IsVisible_Internal(MemberInfo member)
        {
            switch (member)
            {
                case FieldInfo field:
                    return field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly;

                case PropertyInfo property:
                    var getter = property.GetGetMethod(true);

                    return getter != null && (getter.IsPublic || getter.IsFamily || getter.IsFamilyOrAssembly);

                case MethodInfo method:
                    return !method.IsSpecialName && (method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly);

                case EventInfo evt:
                    var adder = evt.GetAddMethod(true);

                    return adder != null && (adder.IsPublic || adder.IsFamily || adder.IsFamilyOrAssembly);

                default:
                    return false;
            }
        }
    }
}
