using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ModernWestern.UEArchitecture.EditorTools
{
    public static partial class UEArchitectureCli
    {
        private const string ConvertHelp = "convert-class (script=<Assets/...cs> | class=<Class>) to=<GameMode|PlayerController|AIController|Pawn|HUD|PlayerStart|LoadingScreen|GameInstance> " +
                                           "[rename=<NewName>|true] [game-state=<Class>] [player-state=<Class>] [states=false] [apply=true] (a dry run without apply=true) | " +
                                           "find-usages (script=<path> | class=<Class>) [folder=Assets/...] | " +
                                           "change-class target=<address> class=<NewClass> [component=<Class>] [apply=true] | " +
                                           "change-gamemode-states (script=<path> | class=<GameMode class>) game-state=<Class> player-state=<Class> [apply=true] | ";

        /// <summary>
        /// The verbs that convert and retarget classes, or null for another verb. Verbs that edit scripts are dry runs
        /// unless apply=true.
        /// </summary>
        private static ActionResult DispatchConvert_Internal(CliArguments arguments, CliSession session)
        {
            ActionResult failed;

            switch (arguments.Verb)
            {
                case "convert-class":
                {
                    if (!Require_Internal(arguments, out failed, "to"))
                    {
                        return failed;
                    }

                    var result = new ActionResult(arguments.Verb);

                    var script = Script_Internal(arguments, result);

                    var target = ClassConversion.Bases.FirstOrDefault(type => string.Equals(type.Name, arguments.Get("to"), StringComparison.OrdinalIgnoreCase) ||
                                                                              string.Equals(type.FullName, arguments.Get("to"), StringComparison.Ordinal));

                    if (target == null)
                    {
                        result.AddProblem($"to=\"{arguments.Get("to")}\" is not one of {string.Join(", ", ClassConversion.Bases.Select(type => type.Name))}.");
                    }

                    if (!result.Ok)
                    {
                        return result;
                    }

                    var rename = arguments.Get("rename");

                    string newName = null;

                    if (arguments.Flag("rename"))
                    {
                        newName = ClassConversion.SuggestName(script.GetClass()?.Name, target);
                    }
                    else if (!string.IsNullOrEmpty(rename) && !string.Equals(rename, "false", StringComparison.OrdinalIgnoreCase) && rename != "0" &&
                             !string.Equals(rename, "no", StringComparison.OrdinalIgnoreCase))
                    {
                        newName = rename;
                    }

                    var states = arguments.Get("states") == null || arguments.Flag("states");

                    return UEArchitectureActions.ConvertToFrameworkClass(script, target, newName, arguments.Get("game-state"), arguments.Get("player-state"), states, !arguments.Flag("apply"));
                }

                case "find-usages":
                {
                    var result = new ActionResult(arguments.Verb);

                    var script = Script_Internal(arguments, result);

                    return result.Ok ? UEArchitectureActions.FindUsages(script, arguments.Get("folder")) : result;
                }

                case "change-class":
                {
                    if (!Require_Internal(arguments, out failed, "target", "class"))
                    {
                        return failed;
                    }

                    var result = new ActionResult(arguments.Verb);

                    var component = session.ResolveComponent(arguments.Get("target"), arguments.Get("component"), null, result);

                    var type = FindComponentType(arguments.Get("class"), out var problem);

                    if (type == null)
                    {
                        result.AddProblem(problem);
                    }

                    return result.Ok ? UEArchitectureActions.ChangeComponentClass(component, type, !arguments.Flag("apply")) : result;
                }

                case "change-gamemode-states":
                {
                    if (!Require_Internal(arguments, out failed, "game-state", "player-state"))
                    {
                        return failed;
                    }

                    var result = new ActionResult(arguments.Verb);

                    var script = Script_Internal(arguments, result);

                    var gameState = PendingSteps.FindClass(arguments.Get("game-state")) ?? StateClass_Internal(arguments.Get("game-state"), typeof(GameState), result);

                    var playerState = PendingSteps.FindClass(arguments.Get("player-state")) ?? StateClass_Internal(arguments.Get("player-state"), typeof(PlayerState), result);

                    return result.Ok ? UEArchitectureActions.ChangeGameModeStates(script, gameState, playerState, !arguments.Flag("apply")) : result;
                }

                default:
                    return null;
            }
        }

        /// <summary>
        /// The script of script=&lt;path&gt;, or of the class named by class=&lt;Class&gt;.
        /// </summary>
        private static MonoScript Script_Internal(CliArguments arguments, ActionResult result)
        {
            var path = arguments.Get("script");

            if (!string.IsNullOrEmpty(path))
            {
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path.Trim().Replace('\\', '/'));

                if (script == null)
                {
                    result.AddProblem($"There is no script at {path}.");
                }

                return script;
            }

            if (string.IsNullOrEmpty(arguments.Get("class")))
            {
                result.AddProblem($"\"{arguments.Verb}\" needs script=<Assets/...cs> or class=<Class>.");

                return null;
            }

            var type = FindComponentType(arguments.Get("class"), out var problem);

            if (type == null)
            {
                result.AddProblem(problem);

                return null;
            }

            var found = ScriptLocator.Find(type);

            if (found == null)
            {
                result.AddProblem($"No script declares {type.Name} (its file must be called {type.Name}.cs).");
            }

            return found;
        }

        private static Type StateClass_Internal(string name, Type baseType, ActionResult result)
        {
            var matches = TypeCache.GetTypesDerivedFrom(baseType).Append(baseType).Where(type => type.Name == name || type.FullName == name).Distinct().ToList();

            if (matches.Count == 1)
            {
                return matches[0];
            }

            result.AddProblem(matches.Count == 0
                                  ? $"No {baseType.Name} class is called \"{name}\" (is the script compiled?)."
                                  : $"Several classes are called \"{name}\": {string.Join(", ", matches.Select(type => type.FullName))}. Use the full name.");

            return null;
        }
    }
}
