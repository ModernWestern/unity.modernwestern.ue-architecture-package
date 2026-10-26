using System;
using System.Linq;
using UnityEngine;
using System.Collections.Generic;

namespace ModernWestern.UEArchitecture.EditorTools
{
    public static partial class UEArchitectureCli
    {
        private const string PawnHelp = "make-ai-pawn target=<Pawn address;address...> [controller=<Class>] [possess=placed|spawned|both] | " +
                                        "set-auto-possess target=<Pawn address> possess=disabled|placed|spawned|both | " +
                                        "set-auto-possess-player target=<placed Pawn address> player=1..8|none | " +
                                        "add-path-following targets=<address;address...> | snap-to-navmesh target=<address> [component=<Class>] | ";

        /// <summary>
        /// The verbs of Pawn setup (AI and navigation), or null for another verb.
        /// </summary>
        private static ActionResult DispatchPawn_Internal(CliArguments arguments, CliSession session)
        {
            ActionResult failed;

            switch (arguments.Verb)
            {
                case "make-ai-pawn":
                {
                    if (!Require_Internal(arguments, out failed, "target"))
                    {
                        return failed;
                    }

                    var result = new ActionResult(arguments.Verb);

                    var pawns = Pawns_Internal(arguments.Get("target"), session, result);

                    Type controller = null;

                    if (!string.IsNullOrEmpty(arguments.Get("controller")))
                    {
                        controller = FindComponentType(arguments.Get("controller"), out var problem);

                        if (controller == null)
                        {
                            result.AddProblem(problem);
                        }
                    }

                    var possess = Possess_Internal(arguments.Get("possess", "both"), result);

                    return result.Ok ? UEArchitectureActions.MakeAIPawns(pawns, controller, possess) : result;
                }

                case "set-auto-possess":
                {
                    if (!Require_Internal(arguments, out failed, "target", "possess"))
                    {
                        return failed;
                    }

                    var result = new ActionResult(arguments.Verb);

                    var pawn = session.ResolveComponent(arguments.Get("target"), arguments.Get("component"), typeof(Pawn), result) as Pawn;

                    if (result.Ok && pawn == null)
                    {
                        result.AddProblem($"{arguments.Get("target")} has no Pawn.");
                    }

                    var possess = Possess_Internal(arguments.Get("possess"), result);

                    return result.Ok ? UEArchitectureActions.SetAutoPossess(pawn, possess) : result;
                }

                case "set-auto-possess-player":
                {
                    if (!Require_Internal(arguments, out failed, "target", "player"))
                    {
                        return failed;
                    }

                    var result = new ActionResult(arguments.Verb);

                    var pawn = session.ResolveComponent(arguments.Get("target"), arguments.Get("component"), typeof(Pawn), result) as Pawn;

                    if (result.Ok && pawn == null)
                    {
                        result.AddProblem($"{arguments.Get("target")} has no Pawn.");
                    }

                    var index = PlayerIndex_Internal(arguments.Get("player"), result);

                    return result.Ok ? UEArchitectureActions.SetAutoPossessPlayer(pawn, index) : result;
                }

                case "add-path-following":
                {
                    var list = arguments.Get("targets") ?? arguments.Get("target");

                    if (string.IsNullOrEmpty(list))
                    {
                        Require_Internal(arguments, out failed, "targets");

                        return failed;
                    }

                    var result = new ActionResult(arguments.Verb);

                    var objects = Split_Internal(list).Select(address => session.Resolve(address, result)).Where(found => found != null).ToList();

                    return result.Ok ? UEArchitectureActions.AddPathFollowing(objects) : result;
                }

                case "snap-to-navmesh":
                {
                    if (!Require_Internal(arguments, out failed, "target"))
                    {
                        return failed;
                    }

                    var result = new ActionResult(arguments.Verb);

                    var component = session.ResolveComponent(arguments.Get("target"), arguments.Get("component"), typeof(Pawn), result);

                    return result.Ok ? UEArchitectureActions.SnapToNavMesh(component != null ? component.transform : null) : result;
                }

                default:
                    return null;
            }
        }

        /// <summary>
        /// "a;b;c" or "a,b,c" as trimmed parts.
        /// </summary>
        internal static IEnumerable<string> Split_Internal(string list)
        {
            return (list ?? string.Empty).Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(part => part.Trim()).Where(part => part.Length > 0);
        }

        private static List<Pawn> Pawns_Internal(string list, CliSession session, ActionResult result)
        {
            var pawns = new List<Pawn>();

            foreach (var address in Split_Internal(list))
            {
                var found = session.Resolve(address, result);

                var pawn = found is GameObject gameObject ? gameObject.GetComponent<Pawn>() : found as Pawn;

                if (found != null && pawn == null)
                {
                    result.AddProblem($"{address} has no Pawn.");
                }

                if (pawn != null)
                {
                    pawns.Add(pawn);
                }
            }

            return pawns;
        }

        /// <summary>
        /// "1".."8" as a player index (0..7); "none", "disabled" or "-1" as -1.
        /// </summary>
        internal static int PlayerIndex_Internal(string text, ActionResult result)
        {
            var clean = (text ?? string.Empty).Trim().ToLowerInvariant();

            if (clean == "none" || clean == "disabled" || clean == "-1")
            {
                return -1;
            }

            if (int.TryParse(clean, out var number) && number >= 1 && number <= UEArchitectureActions.MaxPlayers)
            {
                return number - 1;
            }

            result.AddProblem($"player=\"{text}\" is not a player from 1 to {UEArchitectureActions.MaxPlayers}, or none.");

            return -1;
        }

        private static AutoPossessAI Possess_Internal(string text, ActionResult result)
        {
            switch ((text ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "disabled":
                case "none":
                    return AutoPossessAI.Disabled;

                case "placed":
                    return AutoPossessAI.PlacedInWorld;

                case "spawned":
                    return AutoPossessAI.Spawned;

                case "both":
                case "":
                    return AutoPossessAI.PlacedInWorldOrSpawned;

                default:
                    result.AddProblem($"possess=\"{text}\" is not disabled, placed, spawned or both.");

                    return AutoPossessAI.Disabled;
            }
        }
    }
}
