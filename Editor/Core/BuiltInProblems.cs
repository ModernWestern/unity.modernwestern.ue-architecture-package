using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// The problems UE Architecture knows about, each with what to do in plain words and at most one fix: levels without
    /// a GameMode, a Player Start, an EventSystem, an AudioListener or a place in Build Settings; HUDs and loading
    /// screens without a Canvas (or a UI Toolkit UIDocument); loading screens that cannot fade; floating Player Starts
    /// and duplicated start tags.
    /// </summary>
    internal static class BuiltInProblems
    {
        /// <summary>
        /// How far above the ground a Player Start may be before it is called floating.
        /// </summary>
        public const float FloatingDistance = 0.3f;

        private static bool _navMeshKnown;

        private static bool _hasNavMesh;

        internal static void Register_Internal()
        {
            EditorApplication.projectChanged -= ForgetNavMesh_Internal;

            EditorApplication.projectChanged += ForgetNavMesh_Internal;

            EditorSceneManager.sceneOpened -= OnSceneOpened_Internal;

            EditorSceneManager.sceneOpened += OnSceneOpened_Internal;

            EditorSceneManager.sceneClosed -= OnSceneClosed_Internal;

            EditorSceneManager.sceneClosed += OnSceneClosed_Internal;

            ProblemProviders.RegisterLevel(Level_Internal);

            ProblemProviders.Register(typeof(GameMode), GameMode_Internal);

            ProblemProviders.Register(typeof(HUD), Hud_Internal);

            ProblemProviders.Register(typeof(Pawn), Pawn_Internal);

            ProblemProviders.Register(typeof(LoadingScreen), LoadingScreen_Internal);

            ProblemProviders.Register(typeof(PlayerStart), PlayerStart_Internal);
        }

        /// <summary>
        /// The project's own problems (not of a level): the settings asset outside a Resources folder.
        /// </summary>
        public static List<Problem> Project()
        {
            var problems = new List<Problem>();

            var settings = FrameworkIndex.AllSettings;

            foreach (var asset in settings)
            {
                var path = AssetDatabase.GetAssetPath(asset);

                var location = UEArchitectureSettingsProvider.LocationProblem(path);

                if (location.Length > 0)
                {
                    problems.Add(new Problem(ProblemSeverity.Error, $"{path}: {location}", asset));
                }
            }

            if (settings.Length > 1)
            {
                problems.Add(new Problem(ProblemSeverity.Warning, $"There are {settings.Length} UE Architecture settings assets; only one is used. Delete the others."));
            }

            return problems;
        }

        private static void Level_Internal(Scene scene, LevelReport report)
        {
            var name = string.IsNullOrEmpty(scene.path) ? scene.name : Path.GetFileName(scene.path);

            if (report.GameMode == null)
            {
                var prefabs = FrameworkIndex.PrefabsWith(typeof(GameMode)).Where(prefab => prefab.GetComponent<GameMode>() != null).ToList();

                var problem = new Problem(ProblemSeverity.Warning, $"{name} has no GameMode: players get no Player Controller, no Pawn and no input.");

                if (prefabs.Count == 1 && !string.IsNullOrEmpty(scene.path))
                {
                    var only = prefabs[0];

                    problem.WithFix($"Use {only.name} here", "use-gamemode", false, () => UEArchitectureActions.UseGameModeInLevel(only, scene));
                }
                else
                {
                    problem.WithWindow("Create a GameMode…", "create-gamemode", () => GameModeKitWindow.OpenFor(scene));
                }

                report.Problems.Add(problem);
            }
            else if (report.GameMode.DefaultPawnPrefab != null && report.Starts < report.Players)
            {
                var message = report.Starts == 0
                                  ? "No Player Start in this level: players spawn where the GameMode is."
                                  : $"{report.Players} players start but there {(report.Starts == 1 ? "is 1 Player Start" : $"are {report.Starts} Player Starts")}: they share them.";

                var players = report.Players;

                report.Problems.Add(new Problem(ProblemSeverity.Warning, message).WithFix(report.Starts == 0 ? "Add Player Start" : "Add Player Starts", "add-player-starts", true,
                                                                                         () => UEArchitectureActions.AddPlayerStarts(scene, players)));
            }

            if (UiTypes.EventSystem != null && !LevelSetup.HasAny_Internal(scene, UiTypes.EventSystem) && UsesUi_Internal(scene, report.GameMode))
            {
                report.Problems.Add(new Problem(ProblemSeverity.Warning, "No EventSystem: buttons in the HUD and in this level will not respond.")
                                        .WithFix("Add EventSystem", "add-event-system", true, () => UEArchitectureActions.AddEventSystem(scene)));
            }

            if (LevelSetup.FindInScene<Camera>(scene) != null && LevelSetup.FindInScene<AudioListener>(scene) == null && !LevelSetup.SpawnsListener_Internal(report.GameMode))
            {
                report.Problems.Add(new Problem(ProblemSeverity.Warning, "No AudioListener: nothing in this level can be heard.")
                                        .WithFix("Add AudioListener", "add-audio-listener", true, () => UEArchitectureActions.AddAudioListener(scene)));
            }

            if (GameModeInfo.Count<WorldSettings>(scene) > 1)
            {
                report.Problems.Add(new Problem(ProblemSeverity.Warning, "This level has more than one World Settings; only the first one is used. Delete the others."));
            }

            if (!string.IsNullOrEmpty(scene.path) && !report.InBuild)
            {
                var path = scene.path;

                // Not safe: samples, test and scratch levels belong outside the build; which levels ship is a choice.
                report.Problems.Add(new Problem(ProblemSeverity.Info, "Not in Build Settings: Levels.Open cannot load it in a build.")
                                        .WithFix("Add to Build", "add-to-build", false, () => UEArchitectureActions.AddSceneToBuild(path)));
            }
        }

        private static void GameMode_Internal(Component component, List<Problem> problems)
        {
            var gameMode = (GameMode)component;

            var hud = gameMode.HUDPrefab;

            if (hud == null || !EditorUtility.IsPersistent(hud) || UiTypes.Canvas == null || UiTypes.DrawsUi(hud.gameObject))
            {
                return;
            }

            // A warning: a HUD may draw in a way the tools cannot see (OnGUI, world-space UI elsewhere).
            var problem = new Problem(ProblemSeverity.Warning, $"The HUD {hud.gameObject.name} has no Canvas: it shows nothing unless it draws in another way.", hud);

            if (UiBuilders.Available)
            {
                problem.WithFix($"Add a Canvas to {hud.gameObject.name}", "add-hud-canvas", true, () => UEArchitectureActions.AddHudCanvas(hud));
            }

            problems.Add(problem);
        }

        private static void Hud_Internal(Component component, List<Problem> problems)
        {
            if (UiTypes.Canvas == null || UiTypes.DrawsUi(component.gameObject))
            {
                return;
            }

            var hud = (HUD)component;

            // A warning: a HUD may draw in a way the tools cannot see (OnGUI, world-space UI elsewhere).
            var problem = new Problem(ProblemSeverity.Warning, $"{hud.gameObject.name} has no Canvas: the HUD shows nothing unless it draws in another way. Put the HUD on the root of your Canvas.", hud);

            if (UiBuilders.Available)
            {
                problem.WithFix("Add Canvas", "add-hud-canvas", true, () => UEArchitectureActions.AddHudCanvas(hud));
            }

            problems.Add(problem);
        }

        private static void Pawn_Internal(Component component, List<Problem> problems)
        {
            var serialized = new SerializedObject(component);

            var autoPossess = serialized.FindProperty("autoPossessAI");

            var controller = serialized.FindProperty("aiController");

            if (autoPossess == null || controller == null || !(component is Pawn pawn))
            {
                return;
            }

            if (autoPossess.enumValueIndex != (int)AutoPossessAI.Disabled && controller.objectReferenceValue == null)
            {
                var only = UEArchitectureActions.OnlyAIControllerClass_Internal();

                var label = only == null ? "Create AI Controller prefab" : FrameworkIndex.PrefabWithExactly(only) != null ? $"Use {only.Name}" : $"Create {only.Name} prefab";

                problems.Add(new Problem(ProblemSeverity.Warning,
                                         $"Auto Possess is on but there is no AI Controller: {component.gameObject.name} gets a plain AIController that does nothing. Pick your AI Controller prefab, or create one.",
                                         component).WithFix(label, "create-ai-controller", false, () => UEArchitectureActions.MakeAIPawn(pawn)));
            }
            else if (autoPossess.enumValueIndex == (int)AutoPossessAI.Disabled && controller.objectReferenceValue != null)
            {
                problems.Add(new Problem(ProblemSeverity.Info,
                                         "An AI Controller is set but Auto Possess AI is Disabled: nothing uses it until your code calls SpawnDefaultController. Turn it on for placed or spawned Pawns.",
                                         component).WithFix("Placed and spawned", "turn-on-auto-possess", false, () => UEArchitectureActions.SetAutoPossess(pawn, AutoPossessAI.PlacedInWorldOrSpawned)));
            }
        }

        private static void LoadingScreen_Internal(Component component, List<Problem> problems)
        {
            var screen = (LoadingScreen)component;

            if (UiTypes.Canvas == null)
            {
                return;
            }

            var canvas = UiTypes.CanvasIn(screen.gameObject);

            // A UI Toolkit screen fades and sorts itself: the Canvas checks below are not for it.
            if (canvas == null && UiTypes.DrawsUi(screen.gameObject))
            {
                return;
            }

            if (canvas == null)
            {
                var problem = new Problem(ProblemSeverity.Error, $"{screen.gameObject.name} has no Canvas: nothing shows while a level loads.", screen);

                if (UiBuilders.Available)
                {
                    problem.WithFix("Build the default screen", "build-loading-screen", true, () => UEArchitectureActions.BuildLoadingScreen(screen));
                }

                problems.Add(problem);

                return;
            }

            var group = new SerializedObject(screen).FindProperty("canvasGroup");

            if (group != null && group.objectReferenceValue == null)
            {
                var own = UiTypes.Get(screen.gameObject, UiTypes.CanvasGroup);

                problems.Add(new Problem(ProblemSeverity.Warning, "Canvas Group is empty: the screen will pop on and off instead of fading.", screen)
                                 .WithFix(own != null ? "Use the one on this object" : "Add a Canvas Group", "use-canvas-group", true, () => UEArchitectureActions.UseCanvasGroup(screen)));
            }

            var order = UiTypes.SortingOrder(canvas);

            if (order < 1)
            {
                problems.Add(new Problem(ProblemSeverity.Info, $"Sorting order {order}: the level's own UI can draw over the loading screen.", screen)
                                 .WithFix("Set to 100", "loading-screen-order", true, () => UEArchitectureActions.SetSortingOrder(screen, 100)));
            }
        }

        private static void PlayerStart_Internal(Component component, List<Problem> problems)
        {
            var start = (PlayerStart)component;

            if (EditorUtility.IsPersistent(start) || !start.gameObject.scene.IsValid())
            {
                return;
            }

            var position = start.transform.position;

            if (LevelSetup.TryGroundBelow(position, out var ground) && position.y - ground.y > FloatingDistance)
            {
                problems.Add(new Problem(ProblemSeverity.Info, $"Floating {position.y - ground.y:0.#} m above the ground: the Pawn falls when it spawns.", start)
                                 .WithFix("Snap to ground", "snap-to-ground", false, () => UEArchitectureActions.SnapPlayerStart(start)));
            }

            if (HasNavMesh_Internal() && !LevelSetup.TryNavMesh(position, 0.5f, out _))
            {
                problems.Add(new Problem(ProblemSeverity.Info, "Off the NavMesh: a Pawn spawned here cannot use MoveTo until it reaches it.", start)
                                 .WithFix("Snap to NavMesh", "snap-to-navmesh", false, () => UEArchitectureActions.SnapPlayerStart(start, true)));
            }

            if (!string.IsNullOrEmpty(start.StartTag) &&
                LevelSetup.Starts_Internal(start.gameObject.scene).Any(other => other != start && other.StartTag == start.StartTag))
            {
                problems.Add(new Problem(ProblemSeverity.Warning, $"Another Player Start in this level is tagged \"{start.StartTag}\": PlayerStart.Find returns only the first one.", start));
            }
        }

        /// <summary>
        /// True when the level or its HUD has UI that takes clicks (a Graphic Raycaster).
        /// </summary>
        private static bool UsesUi_Internal(Scene scene, GameMode gameMode)
        {
            var raycaster = UiTypes.GraphicRaycaster;

            if (raycaster == null)
            {
                return false;
            }

            if (LevelSetup.HasAny_Internal(scene, raycaster))
            {
                return true;
            }

            var hud = gameMode != null ? gameMode.HUDPrefab : null;

            return hud != null && hud.GetComponentInChildren(raycaster, true) != null;
        }

        /// <summary>
        /// Whether there is a NavMesh, asked again only when levels open or close or the project changes (a bake saves an
        /// asset), never per Hierarchy change: the question builds the whole mesh.
        /// </summary>
        internal static bool HasNavMesh_Internal()
        {
            if (!_navMeshKnown)
            {
                _navMeshKnown = true;

                NavMeshChecks_Internal++;

                _hasNavMesh = LevelSetup.HasNavMesh();
            }

            return _hasNavMesh;
        }

        /// <summary>
        /// How many times the NavMesh was looked for (for tests).
        /// </summary>
        internal static int NavMeshChecks_Internal { get; private set; }

        internal static void ForgetNavMesh_Internal()
        {
            _navMeshKnown = false;
        }

        private static void OnSceneOpened_Internal(Scene scene, OpenSceneMode mode)
        {
            ForgetNavMesh_Internal();
        }

        private static void OnSceneClosed_Internal(Scene scene)
        {
            ForgetNavMesh_Internal();
        }
    }
}
