using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;
using Object = UnityEngine.Object;

namespace ModernWestern.UEArchitecture.EditorTools
{
    public static partial class UEArchitectureActions
    {
        /// <summary>
        /// Pending step kind: wire a Game Mode Kit once its scripts compile (its request is in the step's arguments).
        /// </summary>
        public const string GameModeKitKind = "uea.gamemode-kit";

        /// <summary>
        /// The Game Mode Kit (Window > UE Architecture > New Game Mode…): writes the set's new classes in one compile,
        /// then builds their prefabs (Player Controller, Pawn with its preset, HUD as a ready Canvas, AI Controller), the
        /// GameMode prefab with every default class filled in, uses it as the project default or in one level, and adds a
        /// Player Start per player. Without new classes it all happens at once. The request is kept in the pending step,
        /// so closing the window or the Editor loses nothing. Running it again fills only what is missing.
        /// </summary>
        public static ActionResult CreateGameModeKit(KitRequest request, bool fixReferences = false)
        {
            const string verb = "new-gamemode-kit";

            if (request == null)
            {
                return ActionResult.Fail(verb, "Describe the Game Mode set to create.");
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return ActionResult.Fail(verb, "Leave Play mode first: the kit writes scripts and prefabs in Edit mode.");
            }

            if (!string.IsNullOrEmpty(request.scenePath) && AssetDatabase.LoadAssetAtPath<SceneAsset>(request.scenePath) == null)
            {
                return ActionResult.Fail(verb, $"There is no saved level at {request.scenePath}. Save the level first.");
            }

            var plan = GameModeKitPlan.Build(request);

            if (!plan.Ok)
            {
                var failed = new ActionResult(verb);

                foreach (var problem in plan.Problems)
                {
                    failed.AddProblem(problem);
                }

                return failed;
            }

            if (plan.Scripts.Count == 0)
            {
                using (var op = Scaffold.Begin($"Create the {plan.BaseName} Game Mode set", verb))
                {
                    foreach (var note in plan.Notes)
                    {
                        op.Result.AddNote(note);
                    }

                    WireKit_Internal(op, plan, request);

                    return op.Result;
                }
            }

            var scripts = ScriptWriter.Plan(plan.Scripts);

            var json = JsonUtility.ToJson(request);

            // Run again before the compile: the scripts are written and their step waits.
            if (scripts.Scripts.Count > 0 && scripts.Scripts.All(script => File.Exists(script.Path)))
            {
                var waiting = new ActionResult(verb);

                var queued = PendingSteps.All().Any(step => step.kind == GameModeKitKind && step.Arg("name") == plan.BaseName);

                if (!queued)
                {
                    PendingSteps.Enqueue(KitStep_Internal(scripts, plan, json), waiting);
                }

                waiting.Pending = true;

                waiting.Message = $"The {plan.BaseName} scripts are written; the set is wired after the compile.";

                return waiting;
            }

            var fixedReferences = new ActionResult(verb);

            // The asmdef is changed only when nothing else stops the scripts.
            var others = scripts.Problems.Any(item => scripts.MissingReferences.All(missing => missing.Problem != item));

            if (scripts.MissingReferences.Count > 0 && fixReferences && !others)
            {
                fixedReferences = AsmdefReferences.Apply(scripts.MissingReferences);

                if (!fixedReferences.Ok)
                {
                    return fixedReferences;
                }

                scripts.Problems.RemoveAll(item => scripts.MissingReferences.Any(missing => missing.Problem == item));
            }

            if (!scripts.Ok)
            {
                var failed = new ActionResult(verb);

                foreach (var problem in scripts.Problems.DefaultIfEmpty("Nothing to write."))
                {
                    failed.AddProblem(problem);
                }

                if (scripts.MissingReferences.Count > 0)
                {
                    failed.AddNote("Add the missing references with fix-references=true (the window's \"Add references\" button), or pick a folder whose assembly has them.");
                }

                return failed;
            }

            var result = ScriptWriter.Write(scripts, new[] { KitStep_Internal(scripts, plan, json) }, verb);

            result.Merge(fixedReferences);

            foreach (var note in plan.Notes)
            {
                result.AddNote(note);
            }

            if (result.Ok)
            {
                result.Message = $"{scripts.Describe()}. After the compile the {plan.BaseName} prefabs are built and wired.";
            }

            return result;
        }

        private static PendingStep KitStep_Internal(ScriptPlan scripts, KitPlan plan, string requestJson)
        {
            var step = PendingStep.Create(GameModeKitKind).WithArg("name", plan.BaseName).WithArg("request", requestJson);

            foreach (var script in scripts.Scripts)
            {
                step.WithScript(script.Path, script.FullName);
            }

            return step;
        }

        [PendingStepHandler(GameModeKitKind)]
        private static void FinishGameModeKit_Internal(PendingStepContext context)
        {
            var request = JsonUtility.FromJson<KitRequest>(context.Step.Arg("request", "{}"));

            var plan = GameModeKitPlan.Build(request);

            foreach (var problem in plan.Problems)
            {
                context.Op.Problem(problem);
            }

            if (!plan.Ok)
            {
                return;
            }

            if (!plan.Resolved)
            {
                context.Op.Problem($"The {plan.BaseName} classes compiled, but some are not where the kit wrote them. Check the Console, then run the kit again.");

                return;
            }

            WireKit_Internal(context.Op, plan, request);
        }

        /// <summary>
        /// Builds (or reuses) the prefabs of a resolved plan in order, fills the empty slots, uses the GameMode where asked
        /// and adds the Player Starts.
        /// </summary>
        internal static void WireKit_Internal(ScaffoldOp op, KitPlan plan, KitRequest request)
        {
            var prefabs = new System.Collections.Generic.Dictionary<KitSlot, GameObject>();

            foreach (var slot in plan.PrefabOrder)
            {
                var type = slot.Type;

                if (type == null)
                {
                    op.Problem($"{slot.Row}: {slot.NewClass} is not compiled yet.");

                    return;
                }

                var prefab = FrameworkIndex.PrefabWithExactly(type);

                if (prefab != null)
                {
                    op.Result.AddNote("Reused " + AssetDatabase.GetAssetPath(prefab));
                }
                else
                {
                    Action<GameObject> configure = null;

                    if (slot == plan.Pawn)
                    {
                        configure = root => ApplyPawnPreset_Internal(root, request.pawnPreset);
                    }
                    else if (slot == plan.HUD && request.hudCanvas)
                    {
                        configure = root =>
                        {
                            if (!UiBuilders.AddScreenCanvas(root, UEArchitectureEditorSettings.instance.ReferenceResolution, false))
                            {
                                op.Result.AddNote($"{type.Name}.prefab has no Canvas: {UiBuilders.NeedsUnityUI}");
                            }
                        };
                    }

                    prefab = op.FindOrCreatePrefab(Scaffold.PrefabPathFor(type), type.Name, new[] { type }, configure);
                }

                if (prefab == null)
                {
                    return;
                }

                prefabs[slot] = prefab;
            }

            foreach (var (owner, field, value) in plan.Assignments)
            {
                if (!prefabs.TryGetValue(owner, out var ownerPrefab) || !prefabs.TryGetValue(value, out var valuePrefab))
                {
                    continue;
                }

                var component = ownerPrefab.GetComponent(owner.Type);

                var current = new SerializedObject(component).FindProperty(field)?.objectReferenceValue;

                var wanted = valuePrefab.GetComponent(value.Type);

                if (current != null && current != wanted)
                {
                    op.Result.AddNote($"{ownerPrefab.name}'s {ObjectNames.NicifyVariableName(field)} keeps {current.name} (it was set; the kit fills only empty slots).");

                    continue;
                }

                op.Assign(component, field, wanted);
            }

            var gameMode = prefabs.TryGetValue(plan.GameMode, out var gameModePrefab) ? gameModePrefab.GetComponent<GameMode>() : null;

            if (gameMode == null)
            {
                return;
            }

            op.Result.Result = gameMode;

            op.PingWhenDone(gameMode.gameObject);

            if (request.useAs == KitDestination.ProjectDefault)
            {
                var settings = EnsureSettings_Internal(op);

                if (settings != null)
                {
                    op.Assign(settings, "defaultGameMode", gameMode);
                }
            }

            if (!string.IsNullOrEmpty(request.scenePath) && (request.useAs == KitDestination.ThisLevel || request.addPlayerStarts))
            {
                using (var level = LevelScope.Open(request.scenePath, op.Result.Verb))
                {
                    if (!level.Ok)
                    {
                        foreach (var problem in level.Failure.Problems)
                        {
                            op.Problem(problem);
                        }

                        return;
                    }

                    var changedBefore = op.Result.Changed.Count + op.Result.Created.Count;

                    if (request.useAs == KitDestination.ThisLevel && CheckNothingPlaced_Internal(op, level.Scene))
                    {
                        var worldSettings = EnsureWorldSettings_Internal(op, level.Scene);

                        if (worldSettings != null)
                        {
                            op.Assign(worldSettings, "gameModeOverride", gameMode);
                        }
                    }

                    if (request.addPlayerStarts)
                    {
                        LevelSetup.AddPlayerStarts(op, level.Scene, Mathf.Max(1, gameMode.PlayersAtStart), true);
                    }

                    if (op.Result.Changed.Count + op.Result.Created.Count > changedBefore && !EditorApplication.isPlaying)
                    {
                        EditorSceneManager.MarkSceneDirty(level.Scene);
                    }
                }
            }

            var made = op.Result.Created.Count(path => path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase));

            op.Result.Message = op.Result.Created.Count + op.Result.Changed.Count == 0
                                    ? $"The {plan.BaseName} set is complete: nothing was missing."
                                    : $"The {plan.BaseName} set is wired: {made} new prefab(s), {op.Result.Changed.Count} change(s).";
        }

        /// <summary>
        /// What a new Pawn prefab gets: a capsule you can see (its collider removed, so it does not fight your movement
        /// code), and with <see cref="PawnPreset.CapsuleNavAgent"/> a NavMeshAgent and Path Following on the root.
        /// </summary>
        internal static void ApplyPawnPreset_Internal(GameObject root, PawnPreset preset)
        {
            if (preset == PawnPreset.Empty)
            {
                return;
            }

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);

            body.name = "Body";

            foreach (var component in body.GetComponents<Component>())
            {
                // The primitive's collider, found by name so the editor needs no physics reference.
                if (component != null && component.GetType().Name.EndsWith("Collider", StringComparison.Ordinal))
                {
                    Object.DestroyImmediate(component);
                }
            }

            // Parenting moves it into the prefab's preview scene.
            body.transform.SetParent(root.transform, false);

            body.transform.localPosition = Vector3.up;

            if (preset != PawnPreset.CapsuleNavAgent)
            {
                return;
            }
#if UEA_NAVIGATION
            if (root.GetComponent<UnityEngine.AI.NavMeshAgent>() == null)
            {
                ScaffoldOp.ApplyDefaultPresets_Internal(root.AddComponent<UnityEngine.AI.NavMeshAgent>());
            }

            if (root.GetComponent<PathFollowing>() == null)
            {
                ScaffoldOp.ApplyDefaultPresets_Internal(root.AddComponent<PathFollowing>());
            }
#endif
        }
    }
}
