using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using Object = UnityEngine.Object;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// New Framework Class (Assets > Create > UE Architecture > Class…), like Unreal's "Pick Parent Class": the common
    /// classes with what each is for, every class of yours to derive from, then the name (checked as you type), the
    /// folder and its namespace, what the class needs (tick, GameState and PlayerState, its prefab), and a list of what
    /// will be created. One compile; the window can be closed while it runs. Opened only from a menu or a button.
    /// </summary>
    internal sealed class NewClassWindow : EditorWindow
    {
        private static readonly (string Label, Type Parent, bool Message, string Description)[] Common =
        {
            ("Game Mode", typeof(GameMode), false, "The rules of a level: which Player Controller, Pawn and HUD each player gets."),
            ("Player Controller", typeof(PlayerController), false, "The player's will: reads input and drives the Pawn."),
            ("AI Controller", typeof(AIController), false, "Drives a Pawn by code: enemies, NPCs, bots."),
            ("Pawn", typeof(Pawn), false, "What a player or an AI controls."),
            ("HUD", typeof(HUD), false, "One player's interface, on the root of a Canvas."),
            ("Game Instance", typeof(GameInstance), false, "The game itself, alive from the first scene until it quits."),
            ("Game Subsystem", typeof(GameInstanceSubsystem), false, "A feature that lives as long as the game: GameInstance.Get<T>()."),
            ("World Subsystem", typeof(WorldSubsystem), false, "A feature that lives as long as the level: World.Get<T>()."),
            ("Loading Screen", typeof(LoadingScreen), false, "Shown while Levels.Open loads a level."),
            ("Save Game", typeof(SaveGame), false, "The data you save in slots, with a version to upgrade old saves."),
            ("Message", null, true, "A struct sent between systems with Messages.Broadcast, without allocating."),
            ("Player Start", typeof(PlayerStart), false, "A Player Start with your own data (a team, an order).")
        };

        [SerializeField] private ClassRequest request = new();

        [SerializeField] private string search = string.Empty;

        [SerializeField] private bool fixReferences;

        [SerializeField] private string status = string.Empty;

        [SerializeField] private Vector2 scroll;

        private string _planKey;

        private ScriptPlan _plan;

        private string _planProblem;

        private Type[] _parents;

        /// <summary>
        /// Opens the window, prefilled: the parent class, and the field the new class's prefab goes into ("Then use it as").
        /// </summary>
        public static NewClassWindow Open(Type parent = null, Object assignTo = null, string propertyPath = null, string folder = null)
        {
            var window = GetWindow<NewClassWindow>(true, "New Framework Class", true);

            window.minSize = new Vector2(460f, 520f);

            var prefill = new ClassRequest();

            if (parent != null)
            {
                prefill.parentClass = parent.FullName;

                prefill.className = GeneratorNaming.InitialClassName(assignTo != null ? assignTo.GetType().Name : null, assignTo == null || Scaffold.IsFrameworkType(assignTo.GetType()),
                                                                     PlayerSettings.productName, parent.Name);
            }

            prefill.folder = folder ?? string.Empty;

            prefill.withPrefab = assignTo != null;

            if (assignTo != null)
            {
                prefill.assignToKey = EditorCompat.ObjectKey(Scaffold.Persistent(assignTo));

                prefill.assignToProperty = propertyPath ?? string.Empty;
            }

            window.request = prefill;

            window.status = string.Empty;

            window.fixReferences = false;

            window._planKey = null;

            window.Show();

            return window;
        }

        [MenuItem("Assets/Create/UE Architecture/Class…", false, 79)]
        private static void OpenFromMenu()
        {
            Open(null, null, null, SelectedFolder_Internal());
        }

        [MenuItem("Assets/UE Architecture/Create Child Class…", false, 1490)]
        private static void CreateChildClass()
        {
            var script = Selection.activeObject as MonoScript;

            Open(script != null ? script.GetClass() : null, null, null, GeneratorNaming.FolderOf(AssetDatabase.GetAssetPath(script)));
        }

        [MenuItem("Assets/UE Architecture/Create Child Class…", true)]
        private static bool CreateChildClassValidate()
        {
            var script = Selection.activeObject as MonoScript;

            return Selection.objects.Length == 1 && script != null && ClassRequestTypes.CanBeParent(script.GetClass());
        }

        private void OnGUI()
        {
            using (var view = new EditorGUILayout.ScrollViewScope(scroll))
            {
                scroll = view.scrollPosition;

                DrawParent_Internal();

                EditorGUILayout.Space(6);

                DrawDetails_Internal();
            }
        }

        private void DrawParent_Internal()
        {
            UEArchitectureStyles.Section("Common classes");

            for (var i = 0; i < Common.Length; i += 2)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (var j = i; j < Math.Min(i + 2, Common.Length); j++)
                    {
                        var item = Common[j];

                        var selected = item.Message ? request.message : !request.message && request.parentClass == item.Parent.FullName;

                        var previous = GUI.backgroundColor;

                        if (selected)
                        {
                            GUI.backgroundColor = UEArchitectureStyles.Accent;
                        }

                        if (GUILayout.Button(new GUIContent(item.Label, item.Description), GUILayout.Height(22f), GUILayout.Width((position.width - 14f) / 2f)))
                        {
                            request.message = item.Message;

                            request.parentClass = item.Parent != null ? item.Parent.FullName : string.Empty;

                            GUI.FocusControl(null);
                        }

                        GUI.backgroundColor = previous;
                    }
                }
            }

            var chosen = Common.FirstOrDefault(item => item.Message ? request.message : !request.message && request.parentClass == item.Parent?.FullName);

            EditorGUILayout.LabelField(chosen.Label != null ? chosen.Description : "Pick a class above, or one of yours below.", UEArchitectureStyles.MutedLabel);

            UEArchitectureStyles.Section("All classes");

            search = EditorGUILayout.TextField(new GUIContent("Search", "Every framework class and every class of yours that derives from one."), search);

            if (string.IsNullOrWhiteSpace(search))
            {
                return;
            }

            _parents ??= ClassRequestTypes.Parents();

            var matches = _parents.Where(type => type.Name.IndexOf(search.Trim(), StringComparison.OrdinalIgnoreCase) >= 0).Take(12).ToArray();

            if (matches.Length == 0)
            {
                EditorGUILayout.LabelField("No class matches.", UEArchitectureStyles.MutedLabel);
            }

            foreach (var type in matches)
            {
                var label = UEArchitectureStyles.TypeName(type, _parents) + (ClassTemplates.IsFrameworkType(type) ? "  (UE Architecture)" : $"  ({type.BaseType?.Name})");

                if (GUILayout.Button(label, EditorStyles.miniButtonLeft))
                {
                    request.message = false;

                    request.parentClass = type.FullName;

                    search = string.Empty;

                    GUI.FocusControl(null);
                }
            }
        }

        private void DrawDetails_Internal()
        {
            var parent = request.Parent;

            UEArchitectureStyles.Section(request.message ? "New Message" : parent != null ? $"New child of {parent.Name}" : "New class");

            request.className = EditorGUILayout.TextField(new GUIContent("Name", "The class name. Spaces are removed: \"arena pawn\" gives ArenaPawn."), request.className);

            using (new EditorGUILayout.HorizontalScope())
            {
                request.folder = EditorGUILayout.TextField(new GUIContent("Folder", "Empty: the scripts folder of Project Settings > UE Architecture > Editor."), request.folder);

                if (GUILayout.Button(new GUIContent("…", "Pick a folder inside Assets/"), EditorStyles.miniButton, GUILayout.Width(24f)))
                {
                    var picked = EditorUtility.OpenFolderPanel("Folder", string.IsNullOrEmpty(request.folder) ? "Assets" : request.folder, string.Empty);

                    var root = Path.GetDirectoryName(Application.dataPath)?.Replace('\\', '/') + "/";

                    picked = picked?.Replace('\\', '/');

                    if (!string.IsNullOrEmpty(picked) && picked.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    {
                        request.folder = picked.Substring(root.Length);
                    }

                    GUIUtility.ExitGUI();
                }
            }

            DrawOptions_Internal(parent);

            var plan = Plan_Internal();

            var main = plan?.Scripts.FirstOrDefault();

            if (main != null)
            {
                var assembly = ScriptWriter.AssemblyOf(main.Path);

                UEArchitectureStyles.Row("Namespace", string.IsNullOrEmpty(main.Namespace) ? "none (global)" : main.Namespace, true);

                UEArchitectureStyles.Row("Assembly", string.IsNullOrEmpty(assembly.AsmdefPath) ? assembly.Name : $"{assembly.Name} ({Path.GetFileName(assembly.AsmdefPath)})", true);
            }

            if (!string.IsNullOrEmpty(_planProblem))
            {
                EditorGUILayout.HelpBox(_planProblem, MessageType.Info);
            }

            if (plan != null)
            {
                foreach (var problem in plan.Problems)
                {
                    var missing = plan.MissingReferences.FirstOrDefault(item => item.Problem == problem);

                    if (missing.Assembly != null)
                    {
                        if (fixReferences)
                        {
                            EditorGUILayout.HelpBox($"Create adds {missing.Assembly} to {Path.GetFileName(missing.AsmdefPath)}.", MessageType.Info);
                        }
                        else if (UEArchitectureStyles.Notice($"{Path.GetFileName(missing.AsmdefPath)} does not reference {missing.Assembly}, so the class would not compile.", MessageType.Error,
                                                             "Add reference"))
                        {
                            fixReferences = EditorUtility.DisplayDialog("Add assembly reference",
                                                                        $"Add {string.Join(", ", plan.MissingReferences.Select(item => item.Assembly).Distinct())} to {missing.AsmdefPath} when the class is created?",
                                                                        "Add", "Cancel");

                            GUIUtility.ExitGUI();
                        }

                        continue;
                    }

                    EditorGUILayout.HelpBox(problem, MessageType.Error);
                }

                DrawWillCreate_Internal(plan, parent);
            }

            EditorGUILayout.Space(6);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("Copy CLI command", "The command line that creates this class (put the Unity Editor before it).")))
                {
                    EditorGUIUtility.systemCopyBuffer = Command_Internal();

                    status = "Copied the command line.";
                }

                GUILayout.FlexibleSpace();

                var blocked = plan == null || plan.Scripts.Count == 0 || plan.Problems.Any(problem => !plan.MissingReferences.Any(item => item.Problem == problem) || !fixReferences);

                using (new EditorGUI.DisabledScope(blocked || EditorApplication.isPlayingOrWillChangePlaymode))
                {
                    if (UEArchitectureStyles.AccentButton(new GUIContent("Create", "Writes the script; the prefab and the field are finished after the compile."), GUILayout.Width(120f)))
                    {
                        var result = UEArchitectureActions.CreateClass(request.Clone(), null, null, fixReferences);

                        ClassField.Run_Internal(() => result);

                        status = result.Ok ? result.Message : string.Join("\n", result.Problems);

                        _planKey = null;

                        GUIUtility.ExitGUI();
                    }
                }
            }

            if (!string.IsNullOrEmpty(status))
            {
                EditorGUILayout.LabelField(status, UEArchitectureStyles.MutedLabel);
            }
        }

        private void DrawOptions_Internal(Type parent)
        {
            if (parent == typeof(GameInstanceSubsystem) || parent == typeof(WorldSubsystem))
            {
                request.tickable = EditorGUILayout.Toggle(new GUIContent("Tickable", "Ticks every frame without a MonoBehaviour: ITickable with [Tick]."), request.tickable);

                if (request.tickable)
                {
                    using (new EditorGUI.IndentLevelScope())
                    {
                        request.tickGroup = (TickGroup)EditorGUILayout.EnumPopup(new GUIContent("Tick Group", "When in the frame it ticks."), request.tickGroup);

                        request.tickOrder = EditorGUILayout.IntField(new GUIContent("Order", "Lower runs first."), request.tickOrder);

                        request.unscaled = EditorGUILayout.Toggle(new GUIContent("Unscaled", "Keeps ticking while Time.timeScale is 0."), request.unscaled);
                    }
                }
            }
            else
            {
                request.tickable = false;
            }

            if (parent == typeof(GameMode))
            {
                request.gameStateClass = StatePopup_Internal("Game State", request.gameStateClass, typeof(GameState));

                request.playerStateClass = StatePopup_Internal("Player State", request.playerStateClass, typeof(PlayerState));

                request.stateFiles = (StateFiles)EditorGUILayout.EnumPopup(new GUIContent("New State Classes", "Where the new GameState and PlayerState classes go."), request.stateFiles);
            }

            if (parent != null && typeof(Component).IsAssignableFrom(parent))
            {
                request.withPrefab = EditorGUILayout.Toggle(new GUIContent("Also create the prefab", "Builds its prefab after the compile, named after the class."),
                                                            request.withPrefab || !string.IsNullOrEmpty(request.assignToKey));
            }
            else
            {
                request.withPrefab = false;
            }

            if (!string.IsNullOrEmpty(request.assignToKey))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    var owner = EditorCompat.FromObjectKey(request.assignToKey);

                    UEArchitectureStyles.Row("Then use it as", owner != null ? $"{ObjectNames.NicifyVariableName(request.assignToProperty)} of {owner.name}" : "(the object is gone)");

                    if (GUILayout.Button("Clear", EditorStyles.miniButton, GUILayout.Width(44f)))
                    {
                        request.assignToKey = string.Empty;

                        request.assignToProperty = string.Empty;
                    }
                }
            }
        }

        private static string StatePopup_Internal(string label, string current, Type baseType)
        {
            var types = TypeCache.GetTypesDerivedFrom(baseType).Where(type => !type.IsAbstract && !type.ContainsGenericParameters && type.GetConstructor(Type.EmptyTypes) != null &&
                                                                              !(type.Assembly.GetName().Name ?? string.Empty).Contains(".Tests"))
                                  .OrderBy(type => type.Name, StringComparer.Ordinal)
                                  .ToList();

            types.Insert(0, baseType);

            var names = new List<string> { "New (named after the GameMode)" };

            names.AddRange(types.Select(type => type == baseType ? baseType.Name + " (framework)" : UEArchitectureStyles.TypeName(type, types)));

            var index = string.IsNullOrEmpty(current) ? 0 : types.FindIndex(type => type.FullName == current) + 1;

            index = EditorGUILayout.Popup(label, Math.Max(0, index), names.ToArray());

            return index <= 0 ? string.Empty : types[index - 1].FullName;
        }

        private void DrawWillCreate_Internal(ScriptPlan plan, Type parent)
        {
            if (plan.Scripts.Count == 0)
            {
                return;
            }

            UEArchitectureStyles.Section("Will create");

            foreach (var script in plan.Scripts)
            {
                EditorGUILayout.LabelField(script.Path, UEArchitectureStyles.MutedLabel);
            }

            if (request.withPrefab && parent != null)
            {
                var prefab = GeneratorNaming.PrefabPath(plan.Scripts[0].ClassName, parent, UEArchitectureEditorSettings.Current);

                EditorGUILayout.LabelField(prefab + " (after the compile)", UEArchitectureStyles.MutedLabel);
            }

            if (!string.IsNullOrEmpty(request.assignToKey))
            {
                EditorGUILayout.LabelField($"Puts the prefab in {ObjectNames.NicifyVariableName(request.assignToProperty)}", UEArchitectureStyles.MutedLabel);
            }
        }

        private ScriptPlan Plan_Internal()
        {
            var key = JsonUtility.ToJson(request);

            if (key == _planKey)
            {
                return _plan;
            }

            _planKey = key;

            var scriptRequest = UEArchitectureActions.ScriptRequestOf_Internal(request, out _planProblem);

            _plan = scriptRequest != null ? ScriptWriter.Plan(new[] { scriptRequest }) : null;

            return _plan;
        }

        private string Command_Internal()
        {
            var pairs = new List<KeyValuePair<string, string>>
            {
                new("class", request.className),
                new("parent", request.message ? string.Empty : request.parentClass),
                new("message", request.message ? "true" : string.Empty),
                new("folder", request.folder),
                new("prefab", request.withPrefab ? "true" : string.Empty),
                new("tickable", request.tickable ? "true" : string.Empty),
                new("tick-group", request.tickable ? request.tickGroup.ToString() : string.Empty),
                new("tick-order", request.tickable && request.tickOrder != 0 ? request.tickOrder.ToString() : string.Empty),
                new("unscaled", request.tickable && request.unscaled ? "true" : string.Empty),
                new("game-state", request.gameStateClass),
                new("player-state", request.playerStateClass),
                new("split", request.stateFiles == StateFiles.AsInSettings ? string.Empty : request.stateFiles == StateFiles.OwnFiles ? "true" : "false"),
                new("fix-references", fixReferences ? "true" : string.Empty)
            };

            var owner = EditorCompat.FromObjectKey(request.assignToKey);

            if (owner != null)
            {
                pairs.Add(new KeyValuePair<string, string>("assign-to", UEArchitectureCli.Address(owner)));

                pairs.Add(new KeyValuePair<string, string>("property", request.assignToProperty));
            }

            return UEArchitectureCli.CommandLine("create-class", pairs.ToArray());
        }

        private static string SelectedFolder_Internal()
        {
            var selected = Selection.activeObject;

            var path = selected != null ? AssetDatabase.GetAssetPath(selected) : string.Empty;

            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }

            return AssetDatabase.IsValidFolder(path) ? path : GeneratorNaming.FolderOf(path);
        }
    }
}
