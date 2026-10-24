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
    /// <summary>
    /// The command line: <c>Unity -batchmode -projectPath . -executeMethod
    /// ModernWestern.UEArchitecture.EditorTools.UEArchitectureCli.Run -uea &lt;verb&gt; key=value ... -quit</c>.
    /// Prints the <see cref="ActionResult"/> as one JSON line in the log (on standard output with "-logFile -"; also written
    /// to Library/UEArchitecture/LastCliResult.json) and exits with 0 (ok), 1 (problems) or 2 (part of the work still waits:
    /// run "-uea complete-pending" later). Objects are addressed as "Assets/Scenes/Boss.unity#Path/To/Object", a prefab
    /// path ("Assets/Prefabs/Gameplay/ArenaPawn.prefab", optionally "#Child") or any asset path.
    /// <para>Verbs that write scripts finish after the compile: run them with -quit, then run "-uea complete-pending".
    /// Without -quit, in batch mode, the Editor waits for the compile, finishes like "complete-pending" (opening the
    /// levels it fills) and exits by itself; it gives up after timeout=&lt;seconds&gt; (600 by default).</para>
    /// </summary>
    public static class UEArchitectureCli
    {
        public const string ResultPath = "Library/UEArchitecture/LastCliResult.json";

        /// <summary>
        /// The exit code of a run whose work still waits (for a compile that did not come, or for something else).
        /// </summary>
        public const int PendingExitCode = 2;

        /// <summary>
        /// How long a run without -quit waits for its pending steps, by default.
        /// </summary>
        public const int DefaultTimeoutSeconds = 600;

        private const string MethodName = "ModernWestern.UEArchitecture.EditorTools.UEArchitectureCli.Run";

        private const string ExitWhenDoneKey = "UEArchitecture.Cli.ExitWhenDone";

        private const string DeadlineKey = "UEArchitecture.Cli.Deadline";

        private const string Help = "Verbs: complete-pending | list-pending | cancel-pending id=<id> | " +
                                    "create-class-prefab class=<Class> [path=Assets/...prefab] (also create-prefab) | " +
                                    "assign-class target=<address> property=<field> (class=<Class> | prefab=<path> | none=true) [component=<Class>] | " +
                                    "assign-project-class property=<gameInstance|loadingScreen|defaultGameMode> (class=<Class> | prefab=<path> | none=true) | " +
                                    "create-variant target=<address> property=<field> [component=<Class>] [level=<level folder name>] | " +
                                    "save-as-prefab target=<address> property=<field> [component=<Class>] [path=Assets/...prefab] | " +
                                    "revert-override target=<address> property=<field> [component=<Class>] | " +
                                    "assign target=<address> property=<field> value=<address|text|none> [component=<Class>] | " +
                                    "override-gamemode scene=<path> [folder=Assets/...] | set-default-gamemode prefab=<path> | use-gamemode prefab=<path> scene=<path> | " +
                                    "create-gamemode-variant prefab=<path> scene=<path> [folder=Assets/...] | clear-override scene=<path> | create-missing-default-classes target=<GameMode address> [folder=Assets/...] | " +
                                    "fill-scene-references target=<scene address> [component=<Class>] [all=true] | " +
                                    "new-script class=<Name> parent=<Class> [folder=Assets/...] [prefab=true] [assign-to=<address> property=<field> [component=<Class>]] | " +
                                    "remove-files paths=<path;path;...> (moves files a run created to the trash). " +
                                    "Without -quit a run that waits for a compile exits by itself (timeout=<seconds>, 600 by default). " +
                                    "Exit codes: 0 ok, 1 problems, 2 still pending.";

        /// <summary>
        /// The -executeMethod entry point.
        /// </summary>
        public static void Run()
        {
            var arguments = CliArguments.Parse(Environment.GetCommandLineArgs());

            ActionResult result;

            try
            {
                result = Execute(arguments);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);

                result = ActionResult.Fail(arguments.Verb, $"{exception.GetType().Name}: {exception.Message}");
            }

            Report_Internal(result);

            if (!result.Pending)
            {
                EditorApplication.Exit(result.Ok ? 0 : 1);

                return;
            }

            if (!Application.isBatchMode)
            {
                return;
            }

            if (arguments.HasQuit || !PendingSteps.WaitsForThisCompile_Internal())
            {
                // The first pass of the two-pass contract ("2" tells the caller to run "-uea complete-pending" next), or
                // steps that wait for something no compile of this run brings: waiting would not help.
                EditorApplication.Exit(ExitCodeOf_Internal(result));

                return;
            }

            // Survives the domain reload: after the compile the pending steps run (PendingSteps.RunScheduled_Internal), then
            // FinishWaitingRun_Internal exits. The watchdog exits when nothing comes.
            var seconds = int.TryParse(arguments.Get("timeout"), out var parsed) && parsed > 0 ? parsed : DefaultTimeoutSeconds;

            SessionState.SetBool(ExitWhenDoneKey, true);

            SessionState.SetString(DeadlineKey, DateTime.UtcNow.AddSeconds(seconds).ToString("o", System.Globalization.CultureInfo.InvariantCulture));

            CliWaitWatchdog.Start_Internal();
        }

        /// <summary>
        /// True in a command-line run without -quit that waits for its pending steps to exit.
        /// </summary>
        internal static bool WaitsToExit_Internal => Application.isBatchMode && SessionState.GetBool(ExitWhenDoneKey, false);

        /// <summary>
        /// The arguments that run a verb on this project, for "Copy CLI command": -batchmode, the project folder, the verb
        /// and its pairs (quoted when a shell would split them), and -quit. Put the Unity Editor before them (Unity.exe on
        /// Windows, Unity.app/Contents/MacOS/Unity on macOS, "&amp; 'C:\...\Unity.exe'" in PowerShell); without the
        /// executable they paste into any shell, like ObserverVM's.
        /// </summary>
        public static string CommandLine(string verb, params KeyValuePair<string, string>[] pairs)
        {
            var projectFolder = System.IO.Path.GetDirectoryName(Application.dataPath)?.Replace('\\', '/') ?? ".";

            var text = new System.Text.StringBuilder();

            text.Append("-batchmode -projectPath ").Append(Quote_Internal(projectFolder)).Append(" -executeMethod ").Append(MethodName).Append(" -uea ").Append(verb);

            foreach (var pair in pairs ?? Array.Empty<KeyValuePair<string, string>>())
            {
                if (!string.IsNullOrEmpty(pair.Key) && !string.IsNullOrEmpty(pair.Value))
                {
                    text.Append(' ').Append(Quote_Internal(pair.Key + "=" + pair.Value));
                }
            }

            return text.Append(" -quit").ToString();
        }

        /// <summary>
        /// The address of an object for the command line: "Assets/Scenes/Boss.unity#World Settings" for an object of a
        /// saved level, "Assets/Prefabs/UI/HUD.prefab" (or "...#Child") inside a prefab, an asset's path; empty when it
        /// has none (an object of a level that was never saved).
        /// </summary>
        public static string Address(Object target)
        {
            target = Scaffold.Persistent(target);

            if (target == null)
            {
                return string.Empty;
            }

            var gameObject = target is Component component ? component.gameObject : target as GameObject;

            var assetPath = AssetDatabase.GetAssetPath(target);

            if (!string.IsNullOrEmpty(assetPath))
            {
                if (gameObject == null || gameObject.transform.parent == null)
                {
                    return assetPath;
                }

                return assetPath + "#" + Scaffold.RelativePath_Internal(gameObject.transform);
            }

            if (gameObject == null || !gameObject.scene.IsValid() || string.IsNullOrEmpty(gameObject.scene.path))
            {
                return string.Empty;
            }

            var path = gameObject.name;

            for (var parent = gameObject.transform.parent; parent != null; parent = parent.parent)
            {
                path = parent.name + "/" + path;
            }

            return gameObject.scene.path + "#" + path;
        }

        private static string Quote_Internal(string text)
        {
            // Characters bash, cmd or PowerShell would read as their own (# starts a comment, ; ends a command).
            return text.IndexOfAny(new[] { ' ', '\t', '&', '(', ')', '#', ';', ',', '|', '<', '>', '$', '@', '{', '}', '\'' }) >= 0 ? "\"" + text + "\"" : text;
        }

        /// <summary>
        /// Runs one parsed command and returns its result (no exit, no printing): what Run does, testable.
        /// </summary>
        public static ActionResult Execute(CliArguments arguments)
        {
            if (arguments.Problems.Count > 0)
            {
                var invalid = new ActionResult(arguments.Verb);

                foreach (var problem in arguments.Problems)
                {
                    invalid.AddProblem(problem);
                }

                invalid.AddNote(Help);

                return invalid;
            }

            using (var session = new CliSession())
            {
                var result = Dispatch_Internal(arguments, session);

                result.Verb = arguments.Verb;

                return result;
            }
        }

        private static ActionResult Dispatch_Internal(CliArguments arguments, CliSession session)
        {
            ActionResult failed;

            switch (arguments.Verb)
            {
                case "help":
                    var help = new ActionResult("help") { Message = Help };

                    return help;

                case "complete-pending":
                    session.OpenScenesOfPendingSteps();

                    return UEArchitectureActions.CompletePending();

                case "list-pending":
                    return UEArchitectureActions.ListPending();

                case "cancel-pending":
                    return Require_Internal(arguments, out failed, "id") ? UEArchitectureActions.CancelPending(arguments.Get("id")) : failed;

                case "create-prefab":
                case "create-class-prefab":
                {
                    if (!Require_Internal(arguments, out failed, "class"))
                    {
                        return failed;
                    }

                    var type = FindComponentType(arguments.Get("class"), out var problem);

                    return type == null ? ActionResult.Fail(arguments.Verb, problem) : UEArchitectureActions.CreateClassPrefab(type, arguments.Get("path"));
                }

                case "assign":
                {
                    if (!Require_Internal(arguments, out failed, "target", "property", "value"))
                    {
                        return failed;
                    }

                    var result = new ActionResult(arguments.Verb);

                    var owner = session.ResolveOwner(arguments.Get("target"), arguments.Get("property"), arguments.Get("component"), result);

                    var value = session.ResolveValue(arguments.Get("value"), result);

                    if (!result.Ok)
                    {
                        return result;
                    }

                    return UEArchitectureActions.Assign(owner, arguments.Get("property"), value);
                }

                case "assign-class":
                {
                    if (!Require_Internal(arguments, out failed, "target", "property"))
                    {
                        return failed;
                    }

                    var result = new ActionResult(arguments.Verb);

                    var owner = session.ResolveOwner(arguments.Get("target"), arguments.Get("property"), arguments.Get("component"), result);

                    ClassOrPrefab_Internal(arguments, session, result, out var type, out var prefab);

                    if (!result.Ok)
                    {
                        return result;
                    }

                    return UEArchitectureActions.AssignClass(owner, arguments.Get("property"), type, prefab);
                }

                case "assign-project-class":
                {
                    if (!Require_Internal(arguments, out failed, "property"))
                    {
                        return failed;
                    }

                    var result = new ActionResult(arguments.Verb);

                    ClassOrPrefab_Internal(arguments, session, result, out var type, out var prefab);

                    return result.Ok ? UEArchitectureActions.AssignProjectClass(arguments.Get("property"), type, prefab) : result;
                }

                case "create-variant":
                case "save-as-prefab":
                case "revert-override":
                {
                    if (!Require_Internal(arguments, out failed, "target", "property"))
                    {
                        return failed;
                    }

                    var result = new ActionResult(arguments.Verb);

                    var owner = session.ResolveOwner(arguments.Get("target"), arguments.Get("property"), arguments.Get("component"), result);

                    if (!result.Ok)
                    {
                        return result;
                    }

                    switch (arguments.Verb)
                    {
                        case "create-variant":
                            return UEArchitectureActions.CreateVariantAndAssign(owner, arguments.Get("property"), arguments.Get("level"));

                        case "save-as-prefab":
                            return UEArchitectureActions.SaveAsPrefabAndAssign(owner, arguments.Get("property"), arguments.Get("path"));

                        default:
                            return UEArchitectureActions.RevertOverride(owner, arguments.Get("property"));
                    }
                }

                case "create-gamemode-variant":
                {
                    if (!Require_Internal(arguments, out failed, "prefab", "scene"))
                    {
                        return failed;
                    }

                    var result = new ActionResult(arguments.Verb);

                    var prefab = session.Resolve(arguments.Get("prefab"), result) as GameObject;

                    return result.Ok ? UEArchitectureActions.CreateGameModeVariantForLevel(prefab, arguments.Get("scene"), arguments.Get("folder")) : result;
                }

                case "override-gamemode":
                    return Require_Internal(arguments, out failed, "scene") ? UEArchitectureActions.OverrideGameModeForLevel(arguments.Get("scene"), arguments.Get("folder")) : failed;

                case "set-default-gamemode":
                {
                    if (!Require_Internal(arguments, out failed, "prefab"))
                    {
                        return failed;
                    }

                    var result = new ActionResult(arguments.Verb);

                    var prefab = session.Resolve(arguments.Get("prefab"), result) as GameObject;

                    return result.Ok ? UEArchitectureActions.SetProjectDefaultGameMode(prefab) : result;
                }

                case "use-gamemode":
                {
                    if (!Require_Internal(arguments, out failed, "prefab", "scene"))
                    {
                        return failed;
                    }

                    var result = new ActionResult(arguments.Verb);

                    var prefab = session.Resolve(arguments.Get("prefab"), result) as GameObject;

                    return result.Ok ? UEArchitectureActions.UseGameModeInLevel(prefab, arguments.Get("scene")) : result;
                }

                case "clear-override":
                    return Require_Internal(arguments, out failed, "scene") ? UEArchitectureActions.ClearOverride(arguments.Get("scene")) : failed;

                case "create-missing-default-classes":
                {
                    if (!Require_Internal(arguments, out failed, "target"))
                    {
                        return failed;
                    }

                    var result = new ActionResult(arguments.Verb);

                    var gameMode = session.ResolveComponent(arguments.Get("target"), arguments.Get("component"), typeof(GameMode), result) as GameMode;

                    if (result.Ok && gameMode == null)
                    {
                        result.AddProblem($"{arguments.Get("target")} has no GameMode.");
                    }

                    return result.Ok ? UEArchitectureActions.CreateMissingDefaultClasses(gameMode, arguments.Get("folder")) : result;
                }

                case "fill-scene-references":
                {
                    if (!Require_Internal(arguments, out failed, "target"))
                    {
                        return failed;
                    }

                    var result = new ActionResult(arguments.Verb);

                    var component = session.ResolveComponent(arguments.Get("target"), arguments.Get("component"), typeof(GameMode), result);

                    return result.Ok ? UEArchitectureActions.FillSceneReferences(component, !arguments.Flag("all")) : result;
                }

                case "remove-files":
                {
                    if (!Require_Internal(arguments, out failed, "paths"))
                    {
                        return failed;
                    }

                    var paths = arguments.Get("paths").Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(path => path.Trim()).ToArray();

                    return UEArchitectureActions.RemoveCreatedFiles(paths);
                }

                case "new-script":
                {
                    if (!Require_Internal(arguments, out failed, "class", "parent"))
                    {
                        return failed;
                    }

                    var result = new ActionResult(arguments.Verb);

                    var parent = FindClassOrProblem_Internal(arguments.Get("parent"), result);

                    Object assignTo = null;

                    var property = arguments.Get("property");

                    if (!string.IsNullOrEmpty(arguments.Get("assign-to")))
                    {
                        if (string.IsNullOrEmpty(property))
                        {
                            result.AddProblem("assign-to needs property=<field> too.");
                        }
                        else
                        {
                            assignTo = session.ResolveOwner(arguments.Get("assign-to"), property, arguments.Get("component"), result);
                        }
                    }

                    if (!result.Ok)
                    {
                        return result;
                    }

                    return UEArchitectureActions.NewScript(arguments.Get("class"), parent, arguments.Get("folder"), arguments.Flag("prefab") || assignTo != null, assignTo, property);
                }

                default:
                    var unknown = ActionResult.Fail(arguments.Verb, $"Unknown verb \"{arguments.Verb}\".");

                    unknown.AddNote(Help);

                    return unknown;
            }
        }

        /// <summary>
        /// The component class with this full or plain name, or null with the reason (none, or several with that name).
        /// </summary>
        public static Type FindComponentType(string name, out string problem)
        {
            problem = string.Empty;

            if (string.IsNullOrWhiteSpace(name))
            {
                problem = "No class name given.";

                return null;
            }

            name = name.Trim();

            var matches = TypeCache.GetTypesDerivedFrom<Component>()
                                   .Where(type => type.FullName == name || type.Name == name)
                                   .ToArray();

            var exact = matches.Where(type => type.FullName == name).ToArray();

            if (exact.Length == 1)
            {
                return exact[0];
            }

            if (matches.Length == 1)
            {
                return matches[0];
            }

            problem = matches.Length == 0
                          ? $"No component class is called \"{name}\" (is the script compiled?)."
                          : $"Several classes are called \"{name}\": {string.Join(", ", matches.Select(type => type.FullName))}. Use the full name.";

            return null;
        }

        private static Type FindClassOrProblem_Internal(string name, ActionResult result)
        {
            var type = FindComponentType(name, out var problem);

            if (type != null)
            {
                return type;
            }

            // Non-component parents (rare): any class with that full name.
            type = PendingSteps.FindClass(name);

            if (type == null)
            {
                result.AddProblem(problem);
            }

            return type;
        }

        /// <summary>
        /// What a class slot gets from class=, prefab= or none=true; a problem in <paramref name="result"/> otherwise.
        /// </summary>
        private static void ClassOrPrefab_Internal(CliArguments arguments, CliSession session, ActionResult result, out Type type, out GameObject prefab)
        {
            type = null;

            prefab = null;

            if (!string.IsNullOrEmpty(arguments.Get("prefab")))
            {
                prefab = session.Resolve(arguments.Get("prefab"), result) as GameObject;

                if (prefab == null && result.Ok)
                {
                    result.AddProblem($"{arguments.Get("prefab")} is not a prefab.");
                }
            }
            else if (!string.IsNullOrEmpty(arguments.Get("class")))
            {
                type = FindComponentType(arguments.Get("class"), out var problem);

                if (type == null)
                {
                    result.AddProblem(problem);
                }
            }
            else if (!arguments.Flag("none"))
            {
                result.AddProblem($"\"{arguments.Verb}\" needs class=<Class>, prefab=<path>, or none=true to clear the field.");
            }
        }

        private static bool Require_Internal(CliArguments arguments, out ActionResult failed, params string[] keys)
        {
            var missing = keys.Where(key => string.IsNullOrEmpty(arguments.Get(key))).ToArray();

            failed = null;

            if (missing.Length == 0)
            {
                return true;
            }

            failed = ActionResult.Fail(arguments.Verb, $"\"{arguments.Verb}\" needs {string.Join(", ", missing.Select(key => key + "=..."))}.");

            failed.AddNote(Help);

            return false;
        }

        private static void Report_Internal(ActionResult result)
        {
            var json = result.ToJson();

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ResultPath) ?? "Library");

                File.WriteAllText(ResultPath, json);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"UE Architecture: could not write {ResultPath} ({exception.Message}).");
            }

            // Unity sends the Editor's standard output to its log: with "-logFile -" the line reaches standard output.
            Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "{0}", json);
        }

        /// <summary>
        /// The exit code of a result: 0 ok, 1 problems, <see cref="PendingExitCode"/> when part of the work still waits.
        /// </summary>
        internal static int ExitCodeOf_Internal(ActionResult result)
        {
            return result == null ? 1 : !result.Ok ? 1 : result.Pending ? PendingExitCode : 0;
        }

        /// <summary>
        /// A batch run without -quit, after the compile: finishes the pending steps like "-uea complete-pending" (the levels
        /// they fill are opened, saved and closed), prints the result and exits. Steps that still wait (for another step,
        /// or for a compile that this run will not see) exit with <see cref="PendingExitCode"/>.
        /// </summary>
        internal static void FinishWaitingRun_Internal()
        {
            if (!WaitsToExit_Internal || PendingSteps.WaitsForThisCompile_Internal())
            {
                // Steps queued since this domain loaded wait for the compile and reload that are still to come.
                return;
            }

            StopWaiting_Internal();

            ActionResult result;

            try
            {
                result = Execute(CliArguments.Parse(new[] { "-uea", "complete-pending" }));
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);

                result = ActionResult.Fail("complete-pending", $"{exception.GetType().Name}: {exception.Message}");
            }

            if (result.Pending && result.Ok)
            {
                result.AddNote("Some steps still wait (see the notes): run \"-uea complete-pending\" once they can finish.");
            }

            Report_Internal(result);

            EditorApplication.Exit(ExitCodeOf_Internal(result));
        }

        /// <summary>
        /// The watchdog of a run without -quit: nothing came before the deadline (no compile, no reload).
        /// </summary>
        internal static void GiveUpWaiting_Internal()
        {
            if (!WaitsToExit_Internal)
            {
                return;
            }

            StopWaiting_Internal();

            var result = UEArchitectureActions.ListPending();

            result.Verb = "complete-pending";

            result.AddProblem("Gave up waiting: the pending steps did not finish before the timeout (no compile or reload came). Run \"-uea complete-pending\" to finish them.");

            Report_Internal(result);

            EditorApplication.Exit(1);
        }

        /// <summary>
        /// When a run without -quit gives up (UTC), or DateTime.MaxValue when it does not wait.
        /// </summary>
        internal static DateTime Deadline_Internal
        {
            get
            {
                var text = SessionState.GetString(DeadlineKey, string.Empty);

                return DateTime.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var deadline)
                           ? deadline.ToUniversalTime()
                           : DateTime.MaxValue;
            }
        }

        private static void StopWaiting_Internal()
        {
            SessionState.EraseBool(ExitWhenDoneKey);

            SessionState.EraseString(DeadlineKey);

            CliWaitWatchdog.Stop_Internal();
        }

        /// <summary>
        /// A batch run that waited for a compile which failed: prints the blocked steps and exits with 1.
        /// </summary>
        internal static void OnCompileFailed_Internal()
        {
            if (!WaitsToExit_Internal)
            {
                return;
            }

            StopWaiting_Internal();

            var result = UEArchitectureActions.ListPending();

            result.Verb = "complete-pending";

            result.Pending = false;

            result.AddProblem("The scripts did not compile; the pending steps are blocked. Fix the errors, then run \"-uea complete-pending\".");

            Report_Internal(result);

            EditorApplication.Exit(1);
        }
    }

    /// <summary>
    /// Ends a command-line run without -quit that waits too long (see <see cref="UEArchitectureCli"/>). Started again
    /// after every domain reload while the run waits.
    /// </summary>
    [InitializeOnLoad]
    internal static class CliWaitWatchdog
    {
        static CliWaitWatchdog()
        {
            if (UEArchitectureCli.WaitsToExit_Internal)
            {
                Start_Internal();
            }
        }

        public static void Start_Internal()
        {
            EditorApplication.update -= Check_Internal;

            EditorApplication.update += Check_Internal;
        }

        public static void Stop_Internal()
        {
            EditorApplication.update -= Check_Internal;
        }

        private static void Check_Internal()
        {
            if (!UEArchitectureCli.WaitsToExit_Internal)
            {
                Stop_Internal();

                return;
            }

            if (DateTime.UtcNow > UEArchitectureCli.Deadline_Internal)
            {
                UEArchitectureCli.GiveUpWaiting_Internal();
            }
        }
    }

    /// <summary>
    /// A parsed command line: the verb after -uea and its key=value pairs (keys are case-insensitive).
    /// </summary>
    public sealed class CliArguments
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

        private readonly List<string> _problems = new();

        public string Verb { get; private set; } = string.Empty;

        public IReadOnlyDictionary<string, string> Values => _values;

        public IReadOnlyList<string> Problems => _problems;

        /// <summary>
        /// True when the command line also has -quit (Unity quits after the method).
        /// </summary>
        public bool HasQuit { get; private set; }

        /// <summary>
        /// Reads "-uea verb key=value ..." from a full command line. Parsing stops at the next "-flag".
        /// </summary>
        public static CliArguments Parse(IReadOnlyList<string> commandLine)
        {
            var parsed = new CliArguments();

            commandLine ??= Array.Empty<string>();

            parsed.HasQuit = commandLine.Any(argument => string.Equals(argument, "-quit", StringComparison.OrdinalIgnoreCase));

            var start = -1;

            for (var i = 0; i < commandLine.Count; i++)
            {
                if (string.Equals(commandLine[i], "-uea", StringComparison.OrdinalIgnoreCase))
                {
                    start = i;

                    break;
                }
            }

            if (start < 0 || start + 1 >= commandLine.Count || commandLine[start + 1].StartsWith("-", StringComparison.Ordinal))
            {
                parsed._problems.Add("Pass -uea <verb> [key=value ...].");

                return parsed;
            }

            parsed.Verb = commandLine[start + 1].Trim().ToLowerInvariant();

            for (var i = start + 2; i < commandLine.Count; i++)
            {
                var argument = commandLine[i];

                if (argument.StartsWith("-", StringComparison.Ordinal))
                {
                    break;
                }

                var equals = argument.IndexOf('=');

                if (equals <= 0)
                {
                    parsed._problems.Add($"Expected key=value, got \"{argument}\".");

                    continue;
                }

                var key = argument.Substring(0, equals).Trim();

                var value = Unquote_Internal(argument.Substring(equals + 1).Trim());

                if (parsed._values.ContainsKey(key))
                {
                    parsed._problems.Add($"\"{key}\" is given twice.");

                    continue;
                }

                parsed._values[key] = value;
            }

            return parsed;
        }

        public string Get(string key, string fallback = null)
        {
            return _values.TryGetValue(key, out var value) ? value : fallback;
        }

        /// <summary>
        /// True for "true", "yes", "1" and "on".
        /// </summary>
        public bool Flag(string key)
        {
            var value = Get(key);

            return value != null && (value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                                     value == "1" || value.Equals("on", StringComparison.OrdinalIgnoreCase));
        }

        private static string Unquote_Internal(string value)
        {
            if (value.Length >= 2 && (value[0] == '"' && value[value.Length - 1] == '"' || value[0] == '\'' && value[value.Length - 1] == '\''))
            {
                return value.Substring(1, value.Length - 2);
            }

            return value;
        }
    }

    /// <summary>
    /// An object address: "Assets/Scenes/Boss.unity#World Settings", "Assets/Prefabs/UI/HUD.prefab#Canvas/Bar", or an
    /// asset path.
    /// </summary>
    public readonly struct ObjectAddress
    {
        public readonly string AssetPath;

        public readonly string HierarchyPath;

        public ObjectAddress(string assetPath, string hierarchyPath)
        {
            AssetPath = assetPath;

            HierarchyPath = hierarchyPath;
        }

        public bool IsScene => AssetPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase);

        public bool IsPrefab => AssetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// True when the text looks like an address (it starts with Assets/ or Packages/).
        /// </summary>
        public static bool LooksLikeAddress(string text)
        {
            var clean = (text ?? string.Empty).Trim().Replace('\\', '/');

            return clean.StartsWith("Assets/", StringComparison.Ordinal) || clean.StartsWith("Packages/", StringComparison.Ordinal);
        }

        public static bool TryParse(string text, out ObjectAddress address, out string problem)
        {
            address = default;

            problem = string.Empty;

            var clean = (text ?? string.Empty).Trim().Replace('\\', '/');

            if (!LooksLikeAddress(clean))
            {
                problem = $"\"{text}\" is not an address: start with Assets/ (e.g. Assets/Scenes/Boss.unity#World Settings).";

                return false;
            }

            var hash = clean.IndexOf('#');

            var assetPath = hash < 0 ? clean : clean.Substring(0, hash);

            var hierarchy = hash < 0 ? string.Empty : clean.Substring(hash + 1).Trim('/');

            if (string.IsNullOrEmpty(Path.GetExtension(assetPath)))
            {
                problem = $"\"{assetPath}\" has no file extension (.unity, .prefab, .asset...).";

                return false;
            }

            if (hierarchy.Split('/').Any(part => part.Length == 0) && hierarchy.Length > 0)
            {
                problem = $"\"{hierarchy}\" has an empty name between slashes.";

                return false;
            }

            var isScene = assetPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase);

            if (isScene && hierarchy.Length == 0)
            {
                problem = $"Name an object in the scene: {assetPath}#Path/To/Object.";

                return false;
            }

            if (!isScene && hierarchy.Length > 0 && !assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                problem = $"Only scenes and prefabs have objects inside: \"{clean}\".";

                return false;
            }

            address = new ObjectAddress(assetPath, hierarchy);

            return true;
        }

        public override string ToString()
        {
            return string.IsNullOrEmpty(HierarchyPath) ? AssetPath : AssetPath + "#" + HierarchyPath;
        }
    }

    /// <summary>
    /// The scenes and assets one command touches: scenes it opens are opened additively, saved when changed and closed
    /// again; changed assets are saved.
    /// </summary>
    internal sealed class CliSession : IDisposable
    {
        private readonly List<Scene> _opened = new();

        /// <summary>
        /// The object an address names: a scene GameObject, a prefab's root or child, or an asset. Null with a problem.
        /// </summary>
        public Object Resolve(string text, ActionResult result)
        {
            if (!ObjectAddress.TryParse(text, out var address, out var problem))
            {
                result.AddProblem(problem);

                return null;
            }

            if (address.IsScene)
            {
                var scene = OpenScene_Internal(address.AssetPath, result);

                return scene.IsValid() ? Find_Internal(scene.GetRootGameObjects(), address, result) : null;
            }

            if (address.IsPrefab)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(address.AssetPath);

                if (prefab == null)
                {
                    result.AddProblem($"There is no prefab at {address.AssetPath}.");

                    return null;
                }

                if (address.HierarchyPath.Length == 0)
                {
                    return prefab;
                }

                var child = prefab.transform.Find(address.HierarchyPath);

                if (child == null && address.HierarchyPath.StartsWith(prefab.name + "/", StringComparison.Ordinal))
                {
                    child = prefab.transform.Find(address.HierarchyPath.Substring(prefab.name.Length + 1));
                }

                if (child == null)
                {
                    result.AddProblem($"{address.AssetPath} has no \"{address.HierarchyPath}\".");
                }

                return child != null ? child.gameObject : null;
            }

            var asset = AssetDatabase.LoadMainAssetAtPath(address.AssetPath);

            if (asset == null)
            {
                result.AddProblem($"There is no asset at {address.AssetPath}.");
            }

            return asset;
        }

        /// <summary>
        /// The component (or asset) at an address that has <paramref name="propertyPath"/>, optionally of a class.
        /// </summary>
        public Object ResolveOwner(string text, string propertyPath, string componentName, ActionResult result)
        {
            var found = Resolve(text, result);

            if (found == null)
            {
                return null;
            }

            if (!(found is GameObject gameObject))
            {
                if (new SerializedObject(found).FindProperty(propertyPath) == null)
                {
                    result.AddProblem($"{ActionResult.Describe(found)} has no field \"{propertyPath}\".");

                    return null;
                }

                return found;
            }

            foreach (var component in gameObject.GetComponents<Component>())
            {
                if (component == null)
                {
                    continue;
                }

                var type = component.GetType();

                if (!string.IsNullOrEmpty(componentName) && type.Name != componentName && type.FullName != componentName)
                {
                    continue;
                }

                if (new SerializedObject(component).FindProperty(propertyPath) != null)
                {
                    return component;
                }
            }

            var which = string.IsNullOrEmpty(componentName) ? "No component" : $"No {componentName}";

            result.AddProblem($"{which} on {ActionResult.Describe(gameObject)} has a field \"{propertyPath}\".");

            return null;
        }

        /// <summary>
        /// The component at an address: the one of class <paramref name="componentName"/> when given, else the first
        /// <paramref name="preferred"/> (e.g. the GameMode), else the first script on the object.
        /// </summary>
        public Component ResolveComponent(string text, string componentName, Type preferred, ActionResult result)
        {
            var found = Resolve(text, result);

            if (found == null)
            {
                return null;
            }

            if (found is Component direct)
            {
                return direct;
            }

            if (!(found is GameObject gameObject))
            {
                result.AddProblem($"{ActionResult.Describe(found)} is not an object with components.");

                return null;
            }

            var components = gameObject.GetComponents<Component>().Where(component => component != null).ToArray();

            Component pick;

            if (!string.IsNullOrEmpty(componentName))
            {
                pick = components.FirstOrDefault(component => component.GetType().Name == componentName || component.GetType().FullName == componentName);
            }
            else
            {
                pick = components.FirstOrDefault(component => preferred != null && preferred.IsInstanceOfType(component)) ?? components.OfType<MonoBehaviour>().FirstOrDefault();
            }

            if (pick == null)
            {
                var wanted = string.IsNullOrEmpty(componentName) ? preferred?.Name ?? "script" : componentName;

                result.AddProblem($"{ActionResult.Describe(gameObject)} has no {wanted}.");
            }

            return pick;
        }

        /// <summary>
        /// A value for "assign": "none" or empty is null, an address is the object it names, anything else stays text.
        /// </summary>
        public object ResolveValue(string text, ActionResult result)
        {
            var trimmed = (text ?? string.Empty).Trim();

            if (trimmed.Length == 0 || trimmed.Equals("none", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("null", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return ObjectAddress.LooksLikeAddress(trimmed) ? Resolve(trimmed, result) : trimmed;
        }

        /// <summary>
        /// Opens the scenes that hold the targets of waiting pending steps, so they can finish (saved and closed again
        /// when the session ends).
        /// </summary>
        public void OpenScenesOfPendingSteps(PendingStepStore store = null)
        {
            foreach (var path in PendingSteps.ScenesOfTargets_Internal(store))
            {
                OpenScene_Internal(path, new ActionResult());
            }
        }

        public void Dispose()
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);

                if (scene.isDirty && !string.IsNullOrEmpty(scene.path) && (Application.isBatchMode || _opened.Contains(scene)))
                {
                    EditorSceneManager.SaveScene(scene);
                }
            }

            AssetDatabase.SaveAssets();

            foreach (var scene in _opened)
            {
                if (scene.IsValid() && scene.isLoaded && SceneManager.sceneCount > 1)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }

            _opened.Clear();
        }

        private Scene OpenScene_Internal(string path, ActionResult result)
        {
            var scene = SceneManager.GetSceneByPath(path);

            if (scene.IsValid() && scene.isLoaded)
            {
                return scene;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
            {
                result.AddProblem($"There is no scene at {path}.");

                return default;
            }

            scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);

            _opened.Add(scene);

            return scene;
        }

        private static GameObject Find_Internal(GameObject[] roots, ObjectAddress address, ActionResult result)
        {
            var parts = address.HierarchyPath.Split(new[] { '/' }, 2);

            foreach (var root in roots)
            {
                if (root.name != parts[0])
                {
                    continue;
                }

                if (parts.Length == 1)
                {
                    return root;
                }

                var child = root.transform.Find(parts[1]);

                if (child != null)
                {
                    return child.gameObject;
                }
            }

            result.AddProblem($"{address.AssetPath} has no \"{address.HierarchyPath}\". Its root objects: {string.Join(", ", roots.Select(root => root.name))}.");

            return null;
        }
    }
}
