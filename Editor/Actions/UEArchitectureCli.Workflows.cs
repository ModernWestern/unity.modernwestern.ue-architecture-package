using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using Object = UnityEngine.Object;

namespace ModernWestern.UEArchitecture.EditorTools
{
    public static partial class UEArchitectureCli
    {
        private const string WorkflowHelp = "create-class class=<Name> (parent=<Class> | message=true) [folder=Assets/...] [prefab=true] [tickable=true tick-group=<Group> tick-order=<n> unscaled=true] " +
                                            "[game-state=<Class>] [player-state=<Class>] [split=true|false] [assign-to=<address> property=<field>] [fix-references=true] | " +
                                            "new-gamemode-kit name=<Name> [use=default|level|none] [scene=<path>] [folder=Assets/...] " +
                                            "[gamemode|game-state|player-state|player-controller|pawn|hud|ai-controller=new|base|none|<Class>] [pawn-preset=empty|capsule|capsule-nav] [hud-canvas=true|false] [hud-preset=<id>] " +
                                            "[starts=true|false] [split=true|false] [fix-references=true] | " +
                                            "create-level path=<Assets/...unity> [gamemode=<prefab>] [build=true|false] | set-up-level scene=<path> [gamemode=<prefab>] | " +
                                            "add-player-starts scene=<path> [count=<n>] | create-hud-prefab [class=<Class>] [path=Assets/...prefab] | " +
                                            "create-loading-screen [class=<Class>] [path=Assets/...prefab] [use=true] | find-children target=<address> [component=<Class>] | " +
                                            "validate [scenes=build|open|all|<path;path>] [strict=true] | fix-all [scenes=build|open|all|<path;path>] | ";

        /// <summary>
        /// The verbs of the workflows (classes, Game Mode Kit, levels, UI prefabs, checks), or null for another verb.
        /// </summary>
        private static ActionResult DispatchWorkflow_Internal(CliArguments arguments, CliSession session)
        {
            ActionResult failed;

            switch (arguments.Verb)
            {
                case "create-class":
                {
                    if (!Require_Internal(arguments, out failed, "class"))
                    {
                        return failed;
                    }

                    var result = new ActionResult(arguments.Verb);

                    var request = ClassRequest_Internal(arguments, result);

                    Object assignTo = null;

                    if (!string.IsNullOrEmpty(arguments.Get("assign-to")))
                    {
                        if (string.IsNullOrEmpty(arguments.Get("property")))
                        {
                            result.AddProblem("assign-to needs property=<field> too.");
                        }
                        else
                        {
                            assignTo = session.ResolveOwner(arguments.Get("assign-to"), arguments.Get("property"), arguments.Get("component"), result);
                        }
                    }

                    return result.Ok ? UEArchitectureActions.CreateClass(request, assignTo, arguments.Get("property"), arguments.Flag("fix-references")) : result;
                }

                case "new-gamemode-kit":
                {
                    if (!Require_Internal(arguments, out failed, "name"))
                    {
                        return failed;
                    }

                    var result = new ActionResult(arguments.Verb);

                    var request = KitRequest_Internal(arguments, result);

                    return result.Ok ? UEArchitectureActions.CreateGameModeKit(request, arguments.Flag("fix-references")) : result;
                }

                case "create-level":
                {
                    if (!Require_Internal(arguments, out failed, "path"))
                    {
                        return failed;
                    }

                    var result = new ActionResult(arguments.Verb);

                    var gameMode = OptionalPrefab_Internal(arguments, session, "gamemode", result);

                    return result.Ok ? UEArchitectureActions.CreateLevel(arguments.Get("path"), gameMode, arguments.Get("build") == null || arguments.Flag("build")) : result;
                }

                case "set-up-level":
                {
                    if (!Require_Internal(arguments, out failed, "scene"))
                    {
                        return failed;
                    }

                    var result = new ActionResult(arguments.Verb);

                    var gameMode = OptionalPrefab_Internal(arguments, session, "gamemode", result);

                    return result.Ok ? UEArchitectureActions.SetUpLevel(arguments.Get("scene"), new LevelSetupOptions { GameMode = gameMode }) : result;
                }

                case "add-player-starts":
                {
                    if (!Require_Internal(arguments, out failed, "scene"))
                    {
                        return failed;
                    }

                    var count = 0;

                    if (arguments.Get("count") != null && (!int.TryParse(arguments.Get("count"), out count) || count < 1))
                    {
                        return ActionResult.Fail(arguments.Verb, "count= must be a whole number of 1 or more.");
                    }

                    return UEArchitectureActions.AddPlayerStarts(arguments.Get("scene"), count);
                }

                case "create-hud-prefab":
                case "create-loading-screen":
                {
                    var hud = arguments.Verb == "create-hud-prefab";

                    Type type = null;

                    if (!string.IsNullOrEmpty(arguments.Get("class")))
                    {
                        type = FindComponentType(arguments.Get("class"), out var problem);

                        if (type == null)
                        {
                            return ActionResult.Fail(arguments.Verb, problem);
                        }
                    }

                    return hud ? UEArchitectureActions.CreateHudPrefab(type, arguments.Get("path")) : UEArchitectureActions.CreateLoadingScreenPrefab(type, arguments.Get("path"), arguments.Flag("use"));
                }

                case "find-children":
                {
                    if (!Require_Internal(arguments, out failed, "target"))
                    {
                        return failed;
                    }

                    var result = new ActionResult(arguments.Verb);

                    var component = session.ResolveComponent(arguments.Get("target"), arguments.Get("component"), typeof(HUD), result);

                    return result.Ok ? UEArchitectureActions.FindChildrenByName(component) : result;
                }

                case "validate":
                    return UEArchitectureActions.Validate(arguments.Get("scenes", "build"), arguments.Flag("strict"));

                case "fix-all":
                    return UEArchitectureActions.FixAll(arguments.Get("scenes", "build"));

                default:
                    return null;
            }
        }

        /// <summary>
        /// A class request from create-class's arguments (problems go to <paramref name="result"/>).
        /// </summary>
        internal static ClassRequest ClassRequest_Internal(CliArguments arguments, ActionResult result)
        {
            var request = new ClassRequest
            {
                className = arguments.Get("class", string.Empty),
                parentClass = arguments.Get("parent", string.Empty),
                folder = arguments.Get("folder", string.Empty),
                message = arguments.Flag("message"),
                withPrefab = arguments.Flag("prefab"),
                tickable = arguments.Flag("tickable"),
                unscaled = arguments.Flag("unscaled"),
                gameStateClass = arguments.Get("game-state", string.Empty),
                playerStateClass = arguments.Get("player-state", string.Empty),
                stateFiles = arguments.Get("split") == null ? StateFiles.AsInSettings : arguments.Flag("split") ? StateFiles.OwnFiles : StateFiles.SameFile
            };

            if (!request.message && string.IsNullOrEmpty(request.parentClass))
            {
                result.AddProblem("\"create-class\" needs parent=<Class> (or message=true).");
            }

            var group = arguments.Get("tick-group");

            if (!string.IsNullOrEmpty(group))
            {
                if (Enum.TryParse(group, true, out TickGroup parsed))
                {
                    request.tickGroup = parsed;
                }
                else
                {
                    result.AddProblem($"tick-group=\"{group}\" is not one of {string.Join(", ", Enum.GetNames(typeof(TickGroup)))}.");
                }
            }

            var order = arguments.Get("tick-order");

            if (!string.IsNullOrEmpty(order))
            {
                if (int.TryParse(order, out var parsedOrder))
                {
                    request.tickOrder = parsedOrder;
                }
                else
                {
                    result.AddProblem($"tick-order=\"{order}\" is not a whole number.");
                }
            }

            return request;
        }

        /// <summary>
        /// A Game Mode Kit request from new-gamemode-kit's arguments (problems go to <paramref name="result"/>).
        /// </summary>
        internal static KitRequest KitRequest_Internal(CliArguments arguments, ActionResult result)
        {
            var request = new KitRequest
            {
                name = arguments.Get("name", string.Empty),
                scriptsFolder = arguments.Get("folder", string.Empty),
                scenePath = arguments.Get("scene", string.Empty),
                gameMode = Choice_Internal(arguments, "gamemode", KitChoiceMode.New, result),
                gameState = Choice_Internal(arguments, "game-state", KitChoiceMode.New, result),
                playerState = Choice_Internal(arguments, "player-state", KitChoiceMode.New, result),
                playerController = Choice_Internal(arguments, "player-controller", KitChoiceMode.New, result),
                pawn = Choice_Internal(arguments, "pawn", KitChoiceMode.New, result),
                hud = Choice_Internal(arguments, "hud", KitChoiceMode.New, result),
                aiController = Choice_Internal(arguments, "ai-controller", KitChoiceMode.None, result),
                pawnPreset = UEArchitectureEditorSettings.instance.DefaultPawnPreset,
                hudCanvas = arguments.Get("hud-canvas") == null || arguments.Flag("hud-canvas"),
                hudPreset = arguments.Get("hud-preset", string.Empty),
                addPlayerStarts = arguments.Get("starts") == null || arguments.Flag("starts"),
                stateFiles = arguments.Get("split") == null ? StateFiles.AsInSettings : arguments.Flag("split") ? StateFiles.OwnFiles : StateFiles.SameFile
            };

            switch ((arguments.Get("use") ?? "none").ToLowerInvariant())
            {
                case "default":
                case "project":
                    request.useAs = KitDestination.ProjectDefault;

                    break;

                case "level":
                case "scene":
                    request.useAs = KitDestination.ThisLevel;

                    break;

                case "none":
                    request.useAs = KitDestination.Nothing;

                    break;

                default:
                    result.AddProblem($"use=\"{arguments.Get("use")}\" is not default, level or none.");

                    break;
            }

            switch ((arguments.Get("pawn-preset") ?? string.Empty).ToLowerInvariant())
            {
                case "":
                    break;

                case "empty":
                    request.pawnPreset = PawnPreset.Empty;

                    break;

                case "capsule":
                    request.pawnPreset = PawnPreset.Capsule;

                    break;

                case "capsule-nav":
                    request.pawnPreset = PawnPreset.CapsuleNavAgent;

                    break;

                default:
                    result.AddProblem($"pawn-preset=\"{arguments.Get("pawn-preset")}\" is not empty, capsule or capsule-nav.");

                    break;
            }

            return request;
        }

        /// <summary>
        /// The command-line pairs of a kit request, for "Copy CLI command".
        /// </summary>
        internal static KeyValuePair<string, string>[] KitPairs(KitRequest request, bool fixReferences)
        {
            string Choice(KitChoice choice, KitChoiceMode fallback)
            {
                if (choice == null || choice.mode == fallback)
                {
                    return string.Empty;
                }

                return choice.mode == KitChoiceMode.Existing ? choice.existingClass : choice.mode.ToString().ToLowerInvariant();
            }

            return new[]
            {
                new KeyValuePair<string, string>("name", request.name),
                new KeyValuePair<string, string>("use", request.useAs == KitDestination.ProjectDefault ? "default" : request.useAs == KitDestination.ThisLevel ? "level" : "none"),
                new KeyValuePair<string, string>("scene", request.scenePath),
                new KeyValuePair<string, string>("folder", request.scriptsFolder),
                new KeyValuePair<string, string>("gamemode", Choice(request.gameMode, KitChoiceMode.New)),
                new KeyValuePair<string, string>("game-state", Choice(request.gameState, KitChoiceMode.New)),
                new KeyValuePair<string, string>("player-state", Choice(request.playerState, KitChoiceMode.New)),
                new KeyValuePair<string, string>("player-controller", Choice(request.playerController, KitChoiceMode.New)),
                new KeyValuePair<string, string>("pawn", Choice(request.pawn, KitChoiceMode.New)),
                new KeyValuePair<string, string>("hud", Choice(request.hud, KitChoiceMode.New)),
                new KeyValuePair<string, string>("ai-controller", Choice(request.aiController, KitChoiceMode.None)),
                new KeyValuePair<string, string>("pawn-preset", request.pawnPreset == PawnPreset.Empty ? "empty" : request.pawnPreset == PawnPreset.Capsule ? "capsule" : "capsule-nav"),
                new KeyValuePair<string, string>("hud-canvas", request.hudCanvas ? string.Empty : "false"),
                new KeyValuePair<string, string>("hud-preset", request.hudPreset ?? string.Empty),
                new KeyValuePair<string, string>("starts", request.addPlayerStarts ? string.Empty : "false"),
                new KeyValuePair<string, string>("split", request.stateFiles == StateFiles.AsInSettings ? string.Empty : request.stateFiles == StateFiles.OwnFiles ? "true" : "false"),
                new KeyValuePair<string, string>("fix-references", fixReferences ? "true" : string.Empty)
            };
        }

        private static KitChoice Choice_Internal(CliArguments arguments, string key, KitChoiceMode fallback, ActionResult result)
        {
            var value = arguments.Get(key);

            if (string.IsNullOrWhiteSpace(value))
            {
                return new KitChoice(fallback);
            }

            switch (value.Trim().ToLowerInvariant())
            {
                case "new":
                    return new KitChoice(KitChoiceMode.New);

                case "base":
                    return new KitChoice(KitChoiceMode.Base);

                case "none":
                    return new KitChoice(KitChoiceMode.None);

                default:
                    if (ClassRequestTypes.Find(value) == null)
                    {
                        result.AddProblem($"{key}=\"{value}\": no class with that name (use new, base, none or a class's full name).");
                    }

                    return new KitChoice(KitChoiceMode.Existing, value.Trim());
            }
        }

        private static GameObject OptionalPrefab_Internal(CliArguments arguments, CliSession session, string key, ActionResult result)
        {
            if (string.IsNullOrEmpty(arguments.Get(key)))
            {
                return null;
            }

            var prefab = session.Resolve(arguments.Get(key), result) as GameObject;

            if (prefab == null && result.Ok)
            {
                result.AddProblem($"{arguments.Get(key)} is not a prefab.");
            }

            return prefab;
        }
    }
}
