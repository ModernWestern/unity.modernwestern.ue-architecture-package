using System;
using System.Linq;
using System.Collections.Generic;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// What one row of the Game Mode Kit uses.
    /// </summary>
    public enum KitChoiceMode
    {
        /// <summary>
        /// A new class named after the kit ("ArenaPawn"), written by the kit. An existing class with that name is reused.
        /// </summary>
        New,

        /// <summary>
        /// A class of yours (<see cref="KitChoice.existingClass"/>).
        /// </summary>
        Existing,

        /// <summary>
        /// The framework class itself (a plain Pawn, PlayerController...).
        /// </summary>
        Base,

        /// <summary>
        /// Nothing: the slot stays empty (a GameState or PlayerState row falls back to the framework class).
        /// </summary>
        None
    }

    /// <summary>
    /// Where the kit's GameMode is used.
    /// </summary>
    public enum KitDestination
    {
        /// <summary>
        /// Project Settings > UE Architecture > Default GameMode (the settings asset is created when missing).
        /// </summary>
        ProjectDefault,

        /// <summary>
        /// The GameMode Override of one level's World Settings (<see cref="KitRequest.scenePath"/>).
        /// </summary>
        ThisLevel,

        /// <summary>
        /// Nowhere yet.
        /// </summary>
        Nothing
    }

    /// <summary>
    /// One row of the kit: New, Existing (a class of yours), Base (the framework class) or None.
    /// </summary>
    [Serializable]
    public sealed class KitChoice
    {
        public KitChoiceMode mode;

        /// <summary>
        /// The full name of the class for <see cref="KitChoiceMode.Existing"/>.
        /// </summary>
        public string existingClass = string.Empty;

        public KitChoice()
        {
        }

        public KitChoice(KitChoiceMode mode, string existingClass = null)
        {
            this.mode = mode;

            this.existingClass = existingClass ?? string.Empty;
        }
    }

    /// <summary>
    /// A Game Mode set, like the classes Unreal's templates give you: a GameMode with its GameState and PlayerState, a
    /// Player Controller, a default Pawn, a HUD and optionally an AI Controller for the Pawn, each new, existing, the
    /// framework class or none; their prefabs built and wired, the GameMode used as the project default or by one level,
    /// and Player Starts placed. Plain fields: the request is kept in the pending step across the compile.
    /// </summary>
    [Serializable]
    public sealed class KitRequest
    {
        /// <summary>
        /// The set's name: "Arena" gives ArenaGameMode, ArenaPlayerController, ArenaPawn, ArenaHUD...
        /// </summary>
        public string name = string.Empty;

        /// <summary>
        /// Where new scripts go (empty: the scripts folder of Project Settings > UE Architecture > Editor).
        /// </summary>
        public string scriptsFolder = string.Empty;

        /// <summary>
        /// The GameMode: New (named after the set), Existing (one of yours, which keeps its own GameState and PlayerState)
        /// or Base (a plain GameMode).
        /// </summary>
        public KitChoice gameMode = new(KitChoiceMode.New);

        public KitChoice gameState = new(KitChoiceMode.New);

        public KitChoice playerState = new(KitChoiceMode.New);

        public KitChoice playerController = new(KitChoiceMode.New);

        public KitChoice pawn = new(KitChoiceMode.New);

        public KitChoice hud = new(KitChoiceMode.New);

        public KitChoice aiController = new(KitChoiceMode.None);

        /// <summary>
        /// What a new Pawn prefab gets: nothing, a capsule you can see, or the capsule with a NavMeshAgent.
        /// </summary>
        public PawnPreset pawnPreset = PawnPreset.Capsule;

        /// <summary>
        /// A new HUD prefab is a ready Canvas (Screen Space Overlay, scaled, Graphic Raycaster). Needs Unity UI.
        /// </summary>
        public bool hudCanvas = true;

        /// <summary>
        /// What else goes in the HUD prefab, by preset id (empty: nothing). With ObserverVM 2.2 or newer,
        /// "framework-widgets" adds a widget for the GameState and one for the PlayerState when they have Observables.
        /// </summary>
        public string hudPreset = string.Empty;

        public KitDestination useAs = KitDestination.Nothing;

        /// <summary>
        /// The level for <see cref="KitDestination.ThisLevel"/> and for the Player Starts (empty: none).
        /// </summary>
        public string scenePath = string.Empty;

        /// <summary>
        /// Adds Player Starts to <see cref="scenePath"/> until there is one per player at start.
        /// </summary>
        public bool addPlayerStarts = true;

        public StateFiles stateFiles = StateFiles.AsInSettings;

        public KitRequest Clone()
        {
            var json = UnityEngine.JsonUtility.ToJson(this);

            return UnityEngine.JsonUtility.FromJson<KitRequest>(json);
        }
    }

    /// <summary>
    /// One class of the kit, as planned.
    /// </summary>
    internal sealed class KitSlot
    {
        public KitSlot(string row, Type baseType)
        {
            Row = row;

            BaseType = baseType;
        }

        /// <summary>
        /// "Player Controller", "Default Pawn"...
        /// </summary>
        public string Row { get; }

        public Type BaseType { get; }

        /// <summary>
        /// The class used, when it exists (reused, picked, or the framework class).
        /// </summary>
        public Type Type { get; set; }

        /// <summary>
        /// The full name of the class the kit writes, until it is compiled.
        /// </summary>
        public string NewClass { get; set; }

        public bool IsEmpty => Type == null && string.IsNullOrEmpty(NewClass);

        public bool IsNew => Type == null && !string.IsNullOrEmpty(NewClass);

        /// <summary>
        /// "ArenaPawn (new)", "ArenaPawn", "Pawn (framework)" or "none".
        /// </summary>
        public string Describe()
        {
            if (IsNew)
            {
                return NewClass.Substring(NewClass.LastIndexOf('.') + 1) + " (new)";
            }

            if (Type == null)
            {
                return "none";
            }

            return Type == BaseType ? Type.Name + " (framework)" : Type.Name;
        }
    }

    /// <summary>
    /// What the kit will do, in order: the scripts to write (one compile), then the prefabs (AI Controller, Player
    /// Controller, Pawn and HUD before the GameMode that points to them), the fields to fill, where the GameMode is used
    /// and the Player Starts. Pure, so the order and the reuse of existing classes are tested.
    /// </summary>
    internal sealed class KitPlan
    {
        public readonly List<string> Problems = new();

        public readonly List<string> Notes = new();

        public readonly List<ScriptRequest> Scripts = new();

        public KitSlot GameMode;

        public KitSlot GameState;

        public KitSlot PlayerState;

        public KitSlot PlayerController;

        public KitSlot Pawn;

        public KitSlot HUD;

        public KitSlot AIController;

        public string BaseName = string.Empty;

        public bool Ok => Problems.Count == 0;

        /// <summary>
        /// The slots whose prefabs the kit builds or reuses, in build order: the GameMode last, since it points to the others.
        /// </summary>
        public IEnumerable<KitSlot> PrefabOrder
        {
            get
            {
                foreach (var slot in new[] { AIController, PlayerController, Pawn, HUD, GameMode })
                {
                    if (slot != null && !slot.IsEmpty && (slot != AIController || Pawn != null && !Pawn.IsEmpty))
                    {
                        yield return slot;
                    }
                }
            }
        }

        /// <summary>
        /// The fields the kit fills: (owner, field, value).
        /// </summary>
        public IEnumerable<(KitSlot Owner, string Field, KitSlot Value)> Assignments
        {
            get
            {
                if (PlayerController != null && !PlayerController.IsEmpty)
                {
                    yield return (GameMode, "playerController", PlayerController);
                }

                if (Pawn != null && !Pawn.IsEmpty)
                {
                    yield return (GameMode, "defaultPawn", Pawn);
                }

                if (HUD != null && !HUD.IsEmpty)
                {
                    yield return (GameMode, "hud", HUD);
                }

                if (AIController != null && !AIController.IsEmpty && Pawn != null && !Pawn.IsEmpty)
                {
                    yield return (Pawn, "aiController", AIController);
                }
            }
        }

        /// <summary>
        /// True when every class exists (nothing waits for a compile).
        /// </summary>
        public bool Resolved => new[] { GameMode, GameState, PlayerState, PlayerController, Pawn, HUD, AIController }.All(slot => slot == null || !slot.IsNew);

        /// <summary>
        /// "5 scripts, 5 prefabs, 3 fields, World Settings of Boss, 1 Player Start", for the preview.
        /// </summary>
        public string Describe(KitRequest request, int scriptFiles)
        {
            var prefabs = PrefabOrder.Count();

            var fields = Assignments.Count();

            var parts = new List<string>
            {
                scriptFiles == 1 ? "1 script" : $"{scriptFiles} scripts",
                prefabs == 1 ? "1 prefab" : $"{prefabs} prefabs",
                fields == 1 ? "1 field" : $"{fields} fields"
            };

            switch (request.useAs)
            {
                case KitDestination.ProjectDefault:
                    parts.Add("the project's default GameMode");

                    break;

                case KitDestination.ThisLevel:
                    parts.Add("the GameMode Override of " + System.IO.Path.GetFileNameWithoutExtension(request.scenePath));

                    break;
            }

            if (request.addPlayerStarts && !string.IsNullOrEmpty(request.scenePath))
            {
                parts.Add("Player Starts for every player in " + System.IO.Path.GetFileNameWithoutExtension(request.scenePath));
            }

            return string.Join(", ", parts) + ".";
        }
    }

    /// <summary>
    /// Builds the <see cref="KitPlan"/> of a <see cref="KitRequest"/>.
    /// </summary>
    internal static class GameModeKitPlan
    {
        /// <summary>
        /// The plan for the project as it is: classes found in the loaded assemblies, namespaces from the folder's assembly.
        /// </summary>
        public static KitPlan Build(KitRequest request)
        {
            return Build(request, PendingSteps.FindClass, ClassRequestTypes.Find, path => UnityEditor.Compilation.CompilationPipeline.GetAssemblyRootNamespaceFromScriptPath(path),
                         UEArchitectureEditorSettings.Current);
        }

        /// <summary>
        /// The plan, with the project behind functions: <paramref name="findExact"/> finds a class by its full name (a "New"
        /// class that exists is reused), <paramref name="findPicked"/> finds a picked class by its full or short name, and
        /// <paramref name="rootNamespace"/> gives the namespace of a script path.
        /// </summary>
        public static KitPlan Build(KitRequest request, Func<string, Type> findExact, Func<string, Type> findPicked, Func<string, string> rootNamespace, GeneratorConventions conventions)
        {
            var plan = new KitPlan();

            if (request == null || string.IsNullOrWhiteSpace(request.name))
            {
                plan.Problems.Add("Name the set, e.g. Arena: it gives ArenaGameMode, ArenaPlayerController, ArenaPawn and ArenaHUD.");

                return plan;
            }

            var baseName = ClassTemplates.BaseName(CreateFrameworkScripts.ClassName(request.name), "GameMode", "Mode");

            plan.BaseName = baseName;

            var folder = GeneratorNaming.CleanFolder(string.IsNullOrWhiteSpace(request.scriptsFolder) ? conventions.ScriptsFolder : request.scriptsFolder);

            var folderProblem = GeneratorNaming.FolderProblem(folder);

            if (folderProblem.Length > 0)
            {
                plan.Problems.Add(folderProblem);

                return plan;
            }

            string FullName(string className)
            {
                var ns = rootNamespace?.Invoke($"{folder}/{className}.cs") ?? string.Empty;

                return string.IsNullOrEmpty(ns) ? className : ns + "." + className;
            }

            KitSlot Resolve(string row, KitChoice choice, Type baseType, string className, bool allowNone)
            {
                var slot = new KitSlot(row, baseType);

                switch (choice?.mode ?? KitChoiceMode.New)
                {
                    case KitChoiceMode.New:
                    {
                        var fullName = FullName(className);

                        var existing = findExact(fullName);

                        if (existing == null)
                        {
                            slot.NewClass = fullName;
                        }
                        else if (!baseType.IsAssignableFrom(existing))
                        {
                            plan.Problems.Add($"{fullName} exists but is not a {ObjectNames_Internal(baseType)}. Pick another name for the set, or pick the {row} by hand.");
                        }
                        else
                        {
                            slot.Type = existing;

                            plan.Notes.Add($"{existing.Name} already exists: the kit uses it.");
                        }

                        break;
                    }

                    case KitChoiceMode.Existing:
                    {
                        var picked = findPicked(choice.existingClass);

                        if (picked == null || !baseType.IsAssignableFrom(picked) || picked.IsAbstract || picked.ContainsGenericParameters)
                        {
                            plan.Problems.Add($"{row}: \"{choice.existingClass}\" is not a {ObjectNames_Internal(baseType)} class of the project.");
                        }
                        else
                        {
                            slot.Type = picked;
                        }

                        break;
                    }

                    case KitChoiceMode.Base:
                        slot.Type = baseType;

                        break;

                    case KitChoiceMode.None:
                        if (!allowNone)
                        {
                            slot.Type = baseType;
                        }

                        break;
                }

                return slot;
            }

            plan.GameMode = Resolve("GameMode", request.gameMode ?? new KitChoice(KitChoiceMode.New), typeof(GameMode), baseName + "GameMode", false);

            plan.PlayerController = Resolve("Player Controller", request.playerController, typeof(PlayerController), baseName + "PlayerController", true);

            plan.Pawn = Resolve("Default Pawn", request.pawn, typeof(Pawn), baseName + "Pawn", true);

            plan.HUD = Resolve("HUD", request.hud, typeof(HUD), baseName + "HUD", true);

            plan.AIController = Resolve("AI Controller", request.aiController, typeof(AIController), baseName + "AIController", true);

            if (!plan.AIController.IsEmpty && plan.Pawn.IsEmpty)
            {
                plan.Notes.Add("The AI Controller goes into the Default Pawn's AI Controller field; with no Pawn it is left out.");

                plan.AIController = new KitSlot("AI Controller", typeof(AIController));
            }

            if (plan.GameMode.IsNew)
            {
                plan.GameState = Resolve("GameState", request.gameState, typeof(GameState), baseName + "GameState", false);

                plan.PlayerState = Resolve("PlayerState", request.playerState, typeof(PlayerState), baseName + "PlayerState", false);

                var split = request.stateFiles == StateFiles.AsInSettings ? conventions.SplitStateFiles : request.stateFiles == StateFiles.OwnFiles;

                // The GameMode's script writes the new state classes too (in its file or their own).
                plan.Scripts.Add(new ScriptRequest(baseName, typeof(GameMode), folder)
                {
                    SplitStates = split,
                    GameState = plan.GameState.Type,
                    PlayerState = plan.PlayerState.Type
                });
            }
            else if (plan.GameMode.Type != null)
            {
                plan.GameState = new KitSlot("GameState", typeof(GameState)) { Type = GameModeInfo.GameStateType(plan.GameMode.Type) };

                plan.PlayerState = new KitSlot("PlayerState", typeof(PlayerState)) { Type = GameModeInfo.PlayerStateType(plan.GameMode.Type) };

                if (request.gameState?.mode != KitChoiceMode.New || request.playerState?.mode != KitChoiceMode.New)
                {
                    plan.Notes.Add($"{plan.GameMode.Type.Name} already exists, so its GameState and PlayerState stay {plan.GameState.Type.Name} and {plan.PlayerState.Type.Name}.");
                }
            }

            foreach (var slot in new[] { plan.PlayerController, plan.Pawn, plan.HUD, plan.AIController })
            {
                if (slot.IsNew)
                {
                    plan.Scripts.Add(new ScriptRequest(slot.NewClass.Substring(slot.NewClass.LastIndexOf('.') + 1), slot.BaseType, folder));
                }
            }

            if (request.useAs == KitDestination.ThisLevel && string.IsNullOrWhiteSpace(request.scenePath))
            {
                plan.Problems.Add("Name the level that uses the GameMode (scene=Assets/...unity), or use it as the project default.");
            }

            return plan;
        }

        private static string ObjectNames_Internal(Type type)
        {
            return UnityEditor.ObjectNames.NicifyVariableName(type.Name);
        }
    }
}
