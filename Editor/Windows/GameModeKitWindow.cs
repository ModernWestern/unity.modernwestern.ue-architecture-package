using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// The Game Mode Kit (Window > UE Architecture > New Game Mode…, Assets > Create > UE Architecture > Game Mode Kit…,
    /// a GameMode field's "New Game Mode set…", the Level overlay's "Create a GameMode…"): type a name, choose New,
    /// Existing, Base class or None for each class, the Pawn and HUD presets and where the GameMode is used; one compile
    /// later everything is built and wired. Opened only from a menu or a button.
    /// </summary>
    internal sealed class GameModeKitWindow : EditorWindow
    {
        [SerializeField] private KitRequest request = new();

        [SerializeField] private bool fixReferences;

        [SerializeField] private string status = string.Empty;

        [SerializeField] private Vector2 scroll;

        private string _planKey;

        private KitPlan _plan;

        private ScriptPlan _scripts;

        private readonly Dictionary<Type, Type[]> _classes = new();

        [MenuItem("Window/UE Architecture/New Game Mode…", false, 20)]
        private static void OpenFromWindowMenu()
        {
            Open(SceneManager.GetActiveScene());
        }

        [MenuItem("Assets/Create/UE Architecture/Game Mode Kit…", false, 94)]
        private static void OpenFromCreateMenu()
        {
            var window = Open(SceneManager.GetActiveScene());

            var selected = Selection.activeObject != null ? AssetDatabase.GetAssetPath(Selection.activeObject) : string.Empty;

            if (!string.IsNullOrEmpty(selected))
            {
                window.request.scriptsFolder = AssetDatabase.IsValidFolder(selected) ? selected : GeneratorNaming.FolderOf(selected);
            }
        }

        /// <summary>
        /// Opens the kit for a level ("Create a GameMode…"): the project default when the project has none, else this level.
        /// </summary>
        public static GameModeKitWindow OpenFor(Scene scene)
        {
            return Open(scene);
        }

        /// <summary>
        /// Opens the kit prefilled: used as <paramref name="useAs"/> (null: the project default when there is none yet,
        /// else <paramref name="scene"/>'s own when it is saved).
        /// </summary>
        public static GameModeKitWindow Open(Scene scene, KitDestination? useAs = null)
        {
            var window = GetWindow<GameModeKitWindow>(true, "New Game Mode", true);

            window.minSize = new Vector2(480f, 560f);

            var scenePath = scene.IsValid() ? scene.path : string.Empty;

            var prefill = new KitRequest
            {
                name = ClassTemplates.BaseName(CreateFrameworkScripts.ClassName(PlayerSettings.productName), "GameMode", "Mode"),
                scenePath = scenePath ?? string.Empty,
                pawnPreset = UEArchitectureEditorSettings.instance.DefaultPawnPreset,
                hudCanvas = UiBuilders.Available
            };

            prefill.useAs = useAs ?? (FrameworkIndex.Settings == null || FrameworkIndex.Settings.DefaultGameMode == null
                                          ? KitDestination.ProjectDefault
                                          : string.IsNullOrEmpty(scenePath) ? KitDestination.Nothing : KitDestination.ThisLevel);

            window.request = prefill;

            window.status = string.Empty;

            window.fixReferences = false;

            window._planKey = null;

            window.Show();

            return window;
        }

        private void OnGUI()
        {
            using (var view = new EditorGUILayout.ScrollViewScope(scroll))
            {
                scroll = view.scrollPosition;

                UEArchitectureStyles.Section("Name");

                request.name = EditorGUILayout.TextField(new GUIContent("Name", "\"Arena\" gives ArenaGameMode, ArenaPlayerController, ArenaPawn, ArenaHUD..."), request.name);

                request.scriptsFolder = EditorGUILayout.TextField(new GUIContent("Scripts Folder", "Empty: the scripts folder of Project Settings > UE Architecture > Editor."),
                                                                  request.scriptsFolder);

                var plan = Plan_Internal();

                UEArchitectureStyles.Section("Classes");

                EditorGUILayout.LabelField("New writes a class named after the set; Existing uses one of yours; Base uses the framework class; None leaves the slot empty.",
                                           UEArchitectureStyles.MutedLabel);

                Row_Internal("Game Mode", request.gameMode, typeof(GameMode), plan?.GameMode);

                var gameModeExists = plan?.GameMode != null && !plan.GameMode.IsNew && plan.GameMode.Type != null;

                using (new EditorGUI.DisabledScope(gameModeExists))
                {
                    Row_Internal("Game State", request.gameState, typeof(GameState), plan?.GameState);

                    Row_Internal("Player State", request.playerState, typeof(PlayerState), plan?.PlayerState);
                }

                Row_Internal("Player Controller", request.playerController, typeof(PlayerController), plan?.PlayerController);

                Row_Internal("Default Pawn", request.pawn, typeof(Pawn), plan?.Pawn);

                Row_Internal("HUD", request.hud, typeof(HUD), plan?.HUD);

                Row_Internal("AI Controller", request.aiController, typeof(AIController), plan?.AIController);

                UEArchitectureStyles.Section("Presets");

                request.pawnPreset = (PawnPreset)EditorGUILayout.EnumPopup(new GUIContent("Pawn", "What a new Pawn prefab gets."), request.pawnPreset);
#if !UEA_NAVIGATION
                if (request.pawnPreset == PawnPreset.CapsuleNavAgent)
                {
                    EditorGUILayout.HelpBox("The NavMeshAgent needs Unity's AI module (Package Manager > Built-in > AI); the Pawn gets the capsule only.", MessageType.Info);
                }
#endif
                using (new EditorGUI.DisabledScope(!UiBuilders.Available))
                {
                    request.hudCanvas = EditorGUILayout.Toggle(new GUIContent("HUD is a Canvas", "A new HUD prefab is a Screen Space Overlay Canvas, scaled, with a Graphic Raycaster."),
                                                               request.hudCanvas && UiBuilders.Available);
                }

                if (!UiBuilders.Available)
                {
                    EditorGUILayout.LabelField(UiBuilders.NeedsUnityUI, UEArchitectureStyles.MutedLabel);
                }

                DrawHudPreset_Internal(plan);

                request.stateFiles = (StateFiles)EditorGUILayout.EnumPopup(new GUIContent("New State Classes", "Where the new GameState and PlayerState classes go."), request.stateFiles);

                UEArchitectureStyles.Section("Use it");

                request.useAs = (KitDestination)EditorGUILayout.EnumPopup(new GUIContent("Use As", "The project's default GameMode, one level's GameMode Override, or nothing yet."),
                                                                          request.useAs);

                var sceneAsset = string.IsNullOrEmpty(request.scenePath) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(request.scenePath);

                sceneAsset = (SceneAsset)EditorGUILayout.ObjectField(new GUIContent("Level", "The level that uses it (This level) and gets the Player Starts."), sceneAsset,
                                                                     typeof(SceneAsset), false);

                request.scenePath = sceneAsset != null ? AssetDatabase.GetAssetPath(sceneAsset) : string.Empty;

                request.addPlayerStarts = EditorGUILayout.Toggle(new GUIContent("Player Start per player", "Adds Player Starts to the level until every player has one."),
                                                                 request.addPlayerStarts);

                if (request.useAs == KitDestination.ThisLevel && sceneAsset == null)
                {
                    EditorGUILayout.HelpBox("Save the level first, then pick it: its World Settings will use the GameMode.", MessageType.Warning);
                }

                DrawPreview_Internal(plan);
            }
        }

        /// <summary>
        /// The HUD presets other packages add (ObserverVM: widgets for the Game State and Player State), offered when the
        /// set's state classes have something to show.
        /// </summary>
        private void DrawHudPreset_Internal(KitPlan plan)
        {
            if (HudPresets.All.Count == 0)
            {
                return;
            }

            var gameState = plan?.GameState?.Type;

            var playerState = plan?.PlayerState?.Type;

            var available = HudPresets.All.Where(preset => preset.Available(gameState, playerState)).ToList();

            if (available.Count == 0)
            {
                request.hudPreset = string.Empty;

                EditorGUILayout.LabelField("HUD widgets: give the Game State or Player State an Observable to offer widgets for them.", UEArchitectureStyles.MutedLabel);

                return;
            }

            var labels = new[] { new GUIContent("Nothing else") }.Concat(available.Select(preset => new GUIContent(preset.Label, preset.Tooltip))).ToArray();

            var index = available.FindIndex(preset => string.Equals(preset.Id, request.hudPreset, StringComparison.OrdinalIgnoreCase)) + 1;

            index = EditorGUILayout.Popup(new GUIContent("HUD Content", "What else goes in the HUD prefab."), index, labels);

            request.hudPreset = index <= 0 ? string.Empty : available[index - 1].Id;
        }

        private void Row_Internal(string label, KitChoice choice, Type baseType, KitSlot slot)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                choice.mode = (KitChoiceMode)EditorGUILayout.EnumPopup(label, choice.mode, GUILayout.MinWidth(240f));

                if (choice.mode == KitChoiceMode.Existing)
                {
                    var classes = Classes_Internal(baseType);

                    var names = classes.Select(type => UEArchitectureStyles.TypeName(type, classes)).ToArray();

                    var index = Array.FindIndex(classes, type => type.FullName == choice.existingClass);

                    index = EditorGUILayout.Popup(Math.Max(0, index), names.Length == 0 ? new[] { "(no class of yours)" } : names);

                    choice.existingClass = classes.Length > 0 ? classes[Math.Min(index, classes.Length - 1)].FullName : string.Empty;
                }
                else
                {
                    EditorGUILayout.LabelField(slot != null ? slot.Describe() : string.Empty, UEArchitectureStyles.MutedLabel);
                }
            }
        }

        private Type[] Classes_Internal(Type baseType)
        {
            if (_classes.TryGetValue(baseType, out var known))
            {
                return known;
            }

            known = TypeCache.GetTypesDerivedFrom(baseType)
                             .Where(type => !type.IsAbstract && !type.ContainsGenericParameters && !(type.Assembly.GetName().Name ?? string.Empty).Contains(".Tests"))
                             .OrderBy(type => type.Name, StringComparer.Ordinal)
                             .ToArray();

            _classes[baseType] = known;

            return known;
        }

        private void DrawPreview_Internal(KitPlan plan)
        {
            UEArchitectureStyles.Section("Will create");

            if (plan == null)
            {
                return;
            }

            foreach (var problem in plan.Problems)
            {
                EditorGUILayout.HelpBox(problem, MessageType.Error);
            }

            var scriptProblems = _scripts?.Problems ?? new List<string>();

            foreach (var problem in scriptProblems)
            {
                var missing = _scripts.MissingReferences.FirstOrDefault(item => item.Problem == problem);

                if (missing.Assembly != null && fixReferences)
                {
                    EditorGUILayout.HelpBox($"Create adds {missing.Assembly} to {System.IO.Path.GetFileName(missing.AsmdefPath)}.", MessageType.Info);
                }
                else if (missing.Assembly != null)
                {
                    if (UEArchitectureStyles.Notice(problem, MessageType.Error, "Add references"))
                    {
                        fixReferences = EditorUtility.DisplayDialog("Add assembly references", $"Add the missing references to {missing.AsmdefPath} when the set is created?", "Add", "Cancel");

                        GUIUtility.ExitGUI();
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox(problem, MessageType.Error);
                }
            }

            if (plan.Ok)
            {
                EditorGUILayout.LabelField(plan.Describe(request, _scripts?.Scripts.Count ?? 0), UEArchitectureStyles.Wrapped);

                foreach (var script in _scripts?.Scripts ?? new List<PlannedScript>())
                {
                    EditorGUILayout.LabelField(script.Path, UEArchitectureStyles.MutedLabel);
                }

                foreach (var slot in plan.PrefabOrder)
                {
                    var name = slot.Type != null ? slot.Type.Name : slot.NewClass.Substring(slot.NewClass.LastIndexOf('.') + 1);

                    EditorGUILayout.LabelField(GeneratorNaming.PrefabPath(name, slot.BaseType, UEArchitectureEditorSettings.Current) + (slot.Type == null ? " (after the compile)" : string.Empty),
                                               UEArchitectureStyles.MutedLabel);
                }

                foreach (var note in plan.Notes)
                {
                    EditorGUILayout.LabelField(note, UEArchitectureStyles.MutedLabel);
                }
            }

            EditorGUILayout.Space(6);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("Copy CLI command", "The command line that creates this set (put the Unity Editor before it).")))
                {
                    EditorGUIUtility.systemCopyBuffer = UEArchitectureCli.CommandLine("new-gamemode-kit", UEArchitectureCli.KitPairs(request, fixReferences));

                    status = "Copied the command line.";
                }

                GUILayout.FlexibleSpace();

                var blocked = !plan.Ok || scriptProblems.Any(problem => !fixReferences || !_scripts.MissingReferences.Any(item => item.Problem == problem));

                using (new EditorGUI.DisabledScope(blocked || EditorApplication.isPlayingOrWillChangePlaymode))
                {
                    if (UEArchitectureStyles.AccentButton(new GUIContent("Create", "Writes the new classes (one compile), then builds and wires the prefabs."), GUILayout.Width(120f)))
                    {
                        var result = UEArchitectureActions.CreateGameModeKit(request.Clone(), fixReferences);

                        ClassField.Run_Internal(() => result);

                        status = result.Ok ? result.Message : string.Join("\n", result.Problems);

                        _planKey = null;

                        GUIUtility.ExitGUI();
                    }
                }
            }

            if (!string.IsNullOrEmpty(status))
            {
                EditorGUILayout.LabelField(status, UEArchitectureStyles.Wrapped);
            }
        }

        private KitPlan Plan_Internal()
        {
            var key = JsonUtility.ToJson(request);

            if (key == _planKey)
            {
                return _plan;
            }

            _planKey = key;

            _plan = GameModeKitPlan.Build(request);

            _scripts = _plan.Ok && _plan.Scripts.Count > 0 ? ScriptWriter.Plan(_plan.Scripts) : null;

            return _plan;
        }
    }
}
