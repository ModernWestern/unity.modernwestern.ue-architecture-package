using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using Object = UnityEngine.Object;

namespace ModernWestern.UEArchitecture.EditorTools
{
    public static partial class UEArchitectureActions
    {
        /// <summary>
        /// Make AI Pawn: the Pawn gets its AI controller by itself (Auto Possess AI, <paramref name="possess"/> when it was
        /// Disabled) and its AI Controller slot is filled when empty: with the prefab of <paramref name="controllerType"/>,
        /// else of your only AI Controller class, else of a plain AIController (add your AI script to that prefab, or pick a
        /// class of yours later). Prefabs are built when missing. What is already set stays. One Undo step.
        /// </summary>
        public static ActionResult MakeAIPawn(Pawn pawn, Type controllerType = null, AutoPossessAI possess = AutoPossessAI.PlacedInWorldOrSpawned)
        {
            return MakeAIPawns(new[] { pawn }, controllerType, possess);
        }

        /// <summary>
        /// <see cref="MakeAIPawn"/> on several Pawns (scene objects or prefabs) as one Undo step.
        /// </summary>
        public static ActionResult MakeAIPawns(IEnumerable<Pawn> pawns, Type controllerType = null, AutoPossessAI possess = AutoPossessAI.PlacedInWorldOrSpawned)
        {
            using (var op = Scaffold.Begin("Make AI Pawn", "make-ai-pawn"))
            {
                var list = (pawns ?? Array.Empty<Pawn>()).Where(pawn => pawn != null).Distinct().ToList();

                if (list.Count == 0)
                {
                    op.Problem("Pick a Pawn.");

                    return op.Result;
                }

                if (possess == AutoPossessAI.Disabled)
                {
                    op.Problem("Say when the AI takes the Pawn: placed in the level, spawned, or both.");

                    return op.Result;
                }

                if (controllerType != null && (!typeof(AIController).IsAssignableFrom(controllerType) || controllerType.IsAbstract || controllerType.ContainsGenericParameters))
                {
                    op.Problem($"{controllerType.Name} is not an AI Controller class that can be added to an object.");

                    return op.Result;
                }

                GameObject controllerPrefab = null;

                foreach (var pawn in list)
                {
                    if (!op.CheckEditable(pawn))
                    {
                        continue;
                    }

                    var serialized = new SerializedObject(pawn);

                    if (serialized.FindProperty("autoPossessAI").enumValueIndex == (int)AutoPossessAI.Disabled)
                    {
                        op.Assign(pawn, "autoPossessAI", possess.ToString());
                    }

                    if (serialized.FindProperty("aiController").objectReferenceValue != null)
                    {
                        continue;
                    }

                    if (controllerPrefab == null)
                    {
                        // Like Unreal, the plain AIController unless a class is picked: a project's only AI class may be a sample's.
                        controllerPrefab = CreateClassPrefab_Internal(op, controllerType ?? typeof(AIController), null);

                        if (controllerPrefab == null)
                        {
                            return op.Result;
                        }
                    }

                    op.Assign(pawn, "aiController", controllerPrefab);
                }

                op.Result.Result = list[0];

                op.PingWhenDone(list[0]);

                op.Result.Message = op.Result.Created.Count + op.Result.Changed.Count == 0
                                        ? list.Count == 1 ? $"{list[0].name} is already an AI Pawn." : "Every Pawn is already an AI Pawn."
                                        : list.Count == 1 ? $"{list[0].name} gets its AI controller by itself now." : $"{list.Count} Pawns get their AI controller by themselves now.";

                return op.Result;
            }
        }

        /// <summary>
        /// Sets when a Pawn gets its AI controller by itself (Auto Possess AI).
        /// </summary>
        public static ActionResult SetAutoPossess(Pawn pawn, AutoPossessAI possess)
        {
            using (var op = Scaffold.Begin("Set Auto Possess AI", "set-auto-possess"))
            {
                if (pawn == null)
                {
                    op.Problem("Pick a Pawn.");

                    return op.Result;
                }

                op.Assign(pawn, "autoPossessAI", possess.ToString());

                op.Result.Result = pawn;

                op.Result.Message = op.Result.Changed.Count == 0 ? $"{pawn.name} already uses {ObjectNames.NicifyVariableName(possess.ToString())}." : $"{pawn.name}: Auto Possess AI is {ObjectNames.NicifyVariableName(possess.ToString())}.";

                return op.Result;
            }
        }

        /// <summary>
        /// Sets which player possesses a Pawn placed in a level when it joins (Auto Possess Player): 0 = Player 1, -1 = no
        /// player. That player then gets this Pawn instead of the GameMode's Default Pawn. On a prefab it applies to the
        /// copies placed in levels.
        /// </summary>
        public static ActionResult SetAutoPossessPlayer(Pawn pawn, int playerIndex)
        {
            using (var op = Scaffold.Begin("Set Auto Possess Player", "set-auto-possess-player"))
            {
                if (pawn == null)
                {
                    op.Problem("Pick a Pawn.");

                    return op.Result;
                }

                if (playerIndex < -1 || playerIndex >= MaxPlayers)
                {
                    op.Problem($"Pick a player from 1 to {MaxPlayers}, or none.");

                    return op.Result;
                }

                op.Assign(pawn, "autoPossessPlayer", playerIndex);

                op.Result.Result = pawn;

                var player = GameModeInfo.PlayerLabel(playerIndex);

                op.Result.Message = op.Result.Changed.Count == 0
                                        ? playerIndex < 0 ? $"No player possesses {pawn.name} by itself already." : $"{player} already possesses {pawn.name}."
                                        : playerIndex < 0
                                            ? $"{pawn.name}: no player possesses it by itself now."
                                            : $"{player} possesses {pawn.name} when it joins, instead of getting the Default Pawn.";

                return op.Result;
            }
        }

        /// <summary>
        /// The highest player Auto Possess Player offers.
        /// </summary>
        public const int MaxPlayers = 8;

        /// <summary>
        /// Add Path Following: each object (a Pawn, a NavMeshAgent, a GameObject, in a level or a prefab) gets a NavMeshAgent
        /// when it has none and Path Following, so MoveTo works and its Block Time and Block Distance can be tuned per
        /// prefab. Several prefabs are changed and saved directly (Undo cannot take that back). Objects that have both are
        /// left alone. Needs Unity's AI module.
        /// </summary>
        public static ActionResult AddPathFollowing(IEnumerable<Object> targets)
        {
            using (var op = Scaffold.Begin("Add Path Following", "add-path-following"))
            {
#if UEA_NAVIGATION
                var objects = (targets ?? Array.Empty<Object>()).Select(target => target is Component component ? component.gameObject : target as GameObject)
                                                                .Where(gameObject => gameObject != null).Distinct().ToList();

                if (objects.Count == 0)
                {
                    op.Problem("Pick a Pawn or an object with a NavMeshAgent.");

                    return op.Result;
                }

                var assets = objects.Count(EditorUtility.IsPersistent);

                using (assets > 1 ? Scaffold.EditPrefabsInPlace() : null)
                {
                    foreach (var gameObject in objects)
                    {
                        if (gameObject.GetComponent<UnityEngine.AI.NavMeshAgent>() != null && gameObject.GetComponent<PathFollowing>() != null)
                        {
                            op.Result.AddNote($"{ActionResult.Describe(gameObject)} already has Path Following.");

                            continue;
                        }

                        var where = ActionResult.Describe(gameObject);

                        op.Edit(gameObject, EditIntent.ThisCopy, copy =>
                        {
                            var target = (GameObject)copy;

                            AddOnce_Internal(op, target, typeof(UnityEngine.AI.NavMeshAgent), where);

                            AddOnce_Internal(op, target, typeof(PathFollowing), where);
                        });
                    }
                }

                op.Result.Result = objects[0];

                op.Result.Message = op.Result.Changed.Count == 0 ? "Everything already has Path Following." : $"Added Path Following to {op.Result.Changed.Count(entry => entry.EndsWith("Path Following", StringComparison.Ordinal))} object(s).";
#else
                op.Problem("Path Following needs Unity's AI module (Package Manager > Built-in > AI).");
#endif
                return op.Result;
            }
        }

        /// <summary>
        /// Moves an object of a level onto the closest NavMesh point within 5 m ("Off the NavMesh" [Snap]).
        /// </summary>
        public static ActionResult SnapToNavMesh(Component target)
        {
            using (var op = Scaffold.Begin("Snap to the NavMesh", "snap-to-navmesh"))
            {
                if (target == null || EditorUtility.IsPersistent(target))
                {
                    op.Problem("Pick an object of a level.");

                    return op.Result;
                }

                if (!op.CheckEditable(target))
                {
                    return op.Result;
                }

                var position = target.transform.position;

                if (!LevelSetup.TryNavMesh(position, 5f, out var point))
                {
                    op.Problem($"No NavMesh within 5 m of {target.name}.");

                    return op.Result;
                }

                if ((point - position).sqrMagnitude < 0.000001f)
                {
                    op.Result.Message = $"{target.name} is already on the NavMesh.";

                    return op.Result;
                }

                Undo.RecordObject(target.transform, op.Name);

                target.transform.position = point;

                op.Touch(target);

                op.Result.AddChanged(ActionResult.Describe(target) + ": position");

                op.Result.Message = $"Moved {target.name} by {Vector3.Distance(position, point):0.##} m.";

                return op.Result;
            }
        }

        /// <summary>
        /// The one AI Controller class of the project (not abstract, not in a test assembly), or null when there are none
        /// or several.
        /// </summary>
        internal static Type OnlyAIControllerClass_Internal()
        {
            var classes = TypeCache.GetTypesDerivedFrom<AIController>()
                                   .Where(type => !type.IsAbstract && !type.ContainsGenericParameters && !UEArchitectureSettingsProvider.IsTestType_Internal(type))
                                   .Take(2).ToList();

            return classes.Count == 1 ? classes[0] : null;
        }

        private static void AddOnce_Internal(ScaffoldOp op, GameObject target, Type type, string where)
        {
            if (target.GetComponent(type) != null)
            {
                return;
            }

            var component = UsesUndo_Internal(target) ? Undo.AddComponent(target, type) : target.AddComponent(type);

            if (component == null)
            {
                op.Problem($"Unity could not add {type.Name} to {where}.");

                return;
            }

            ScaffoldOp.ApplyDefaultPresets_Internal(component);

            op.Touch(target);

            op.Result.AddChanged($"{where}: + {ObjectNames.NicifyVariableName(type.Name)}");
        }
    }
}
