using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// The preview of a script edit, confirmed before anything is written: Convert to framework class (the changed line,
    /// the new GameState and PlayerState files, the rename, methods that need a base call, the scenes and prefabs that
    /// use it) and Change Game State and Player State Classes. "Copy original to clipboard" keeps the old text, because
    /// Undo cannot take back a script edit. Apply writes the plan the preview shows; if the script changed on disk since,
    /// the preview is made again and nothing is written.
    /// </summary>
    internal sealed class ConvertClassWindow : EditorWindow
    {
        private enum Mode
        {
            Convert,
            States
        }

        [SerializeField] private MonoScript script;

        [SerializeField] private string frameworkBase = string.Empty;

        [SerializeField] private Mode mode;

        [SerializeField] private bool rename;

        [SerializeField] private string newName = string.Empty;

        [SerializeField] private bool states = true;

        [SerializeField] private string gameState = string.Empty;

        [SerializeField] private string playerState = string.Empty;

        private ConvertPlan _plan;

        private ActionResult _blocked;

        private ScriptUsages _usages;

        private ActionResult _statesPreview;

        private bool _dirty = true;

        private Vector2 _scroll;

        // The script's last write time when the preview was made: a change on disk makes a new preview.
        private DateTime _stamp;

        private ScriptsNamingCache _naming;

        private ScriptsNamingCache Naming => _naming ??= new ScriptsNamingCache();

        /// <summary>
        /// Convert to <paramref name="target"/>: opens the preview of <paramref name="source"/>'s conversion.
        /// </summary>
        public static void OpenConvert(MonoScript source, Type target)
        {
            var window = CreateInstance<ConvertClassWindow>();

            window.script = source;

            window.frameworkBase = target.FullName;

            window.mode = Mode.Convert;

            var type = source.GetClass();

            var suggested = ClassConversion.SuggestName(type?.Name, target);

            window.newName = suggested ?? type?.Name ?? string.Empty;

            window.rename = suggested != null && ClassConversion.RenamesByDefault(target) && window.Naming.Get(type?.Name, AssetDatabase.GetAssetPath(source)).Count == 0;

            window.titleContent = new GUIContent($"Convert to {target.Name}");

            window._usages = FrameworkUsages.Find(source);

            window.minSize = new Vector2(520f, 360f);

            window.ShowUtility();
        }

        /// <summary>
        /// Change Game State and Player State Classes of a GameMode script.
        /// </summary>
        public static void OpenStates(MonoScript source)
        {
            var window = CreateInstance<ConvertClassWindow>();

            window.script = source;

            window.mode = Mode.States;

            var type = source.GetClass();

            window.gameState = GameModeInfo.GameStateType(type).FullName;

            window.playerState = GameModeInfo.PlayerStateType(type).FullName;

            window.titleContent = new GUIContent("Game State and Player State");

            window.minSize = new Vector2(480f, 260f);

            window.ShowUtility();
        }

        private Type Base => ClassConversion.Bases.FirstOrDefault(type => type.FullName == frameworkBase);

        private void OnGUI()
        {
            if (script == null || script.GetClass() == null)
            {
                EditorGUILayout.HelpBox("The script is gone or does not compile.", MessageType.Info);

                return;
            }

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("Script", script, typeof(MonoScript), false);
            }

            using (var change = new EditorGUI.ChangeCheckScope())
            {
                if (mode == Mode.Convert)
                {
                    DrawConvertOptions_Internal();
                }
                else
                {
                    DrawStateOptions_Internal();
                }

                _dirty |= change.changed;
            }

            // Only on Layout: the preview's rows must stay the same between a Layout event and the events that follow it.
            if (Event.current.type == EventType.Layout)
            {
                _dirty |= ScriptStamp_Internal() != _stamp;

                if (_dirty)
                {
                    _dirty = false;

                    Refresh_Internal();
                }
            }
            else if (_dirty)
            {
                Repaint();
            }

            EditorGUILayout.Space(6f);

            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            foreach (var line in PreviewLines_Internal())
            {
                EditorGUILayout.LabelField(line.Text, line.Problem ? ProblemStyle_Internal() : UEArchitectureStyles.Wrapped);
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.LabelField("Undo cannot take back a script edit: copy the original first if you may want it back.", UEArchitectureStyles.MutedLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Copy original to clipboard"))
                {
                    EditorGUIUtility.systemCopyBuffer = System.IO.File.ReadAllText(ClassConversion.PhysicalPath(AssetDatabase.GetAssetPath(script)));
                }

                if (GUILayout.Button("Copy CLI command"))
                {
                    EditorGUIUtility.systemCopyBuffer = CliCommand_Internal();
                }

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Cancel"))
                {
                    Close();

                    GUIUtility.ExitGUI();
                }

                using (new EditorGUI.DisabledScope(!CanApply_Internal()))
                {
                    if (UEArchitectureStyles.AccentButton(new GUIContent("Apply", "Writes the change shown above.")))
                    {
                        Apply_Internal();

                        GUIUtility.ExitGUI();
                    }
                }
            }
        }

        private void DrawConvertOptions_Internal()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                rename = EditorGUILayout.ToggleLeft("Rename class and file to", rename, GUILayout.Width(170f));

                using (new EditorGUI.DisabledScope(!rename))
                {
                    newName = EditorGUILayout.TextField(newName);
                }
            }

            if (Base != typeof(GameMode))
            {
                return;
            }

            states = EditorGUILayout.ToggleLeft("Name its GameState and PlayerState (GameMode<GameState, PlayerState>)", states);

            if (!states)
            {
                return;
            }

            var stem = ClassConversion.StateStem(rename ? newName : script.GetClass().Name);

            gameState = StateField_Internal("GameState", gameState, stem + "GameState", typeof(GameState));

            playerState = StateField_Internal("PlayerState", playerState, stem + "PlayerState", typeof(PlayerState));
        }

        private string StateField_Internal(string label, string value, string fallback, Type baseType)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var shown = EditorGUILayout.TextField(new GUIContent(label, $"An existing {baseType.Name} class, or the name of a new one written next to the script. Empty = {fallback}."),
                                                      string.IsNullOrEmpty(value) ? fallback : value);

                if (GUILayout.Button("▾", EditorStyles.miniButton, GUILayout.Width(22f)))
                {
                    var menu = new GenericMenu();

                    menu.AddItem(new GUIContent($"New: {fallback}"), false, () => SetState_Internal(baseType, fallback));

                    foreach (var type in Candidates_Internal(baseType))
                    {
                        var picked = type;

                        menu.AddItem(new GUIContent(UEArchitectureStyles.TypeName(type, Candidates_Internal(baseType))), false, () => SetState_Internal(baseType, picked.FullName));
                    }

                    menu.ShowAsContext();
                }

                return shown == fallback ? string.Empty : shown;
            }
        }

        private void SetState_Internal(Type baseType, string value)
        {
            if (baseType == typeof(GameState))
            {
                gameState = value;
            }
            else
            {
                playerState = value;
            }

            _dirty = true;

            Repaint();
        }

        private void DrawStateOptions_Internal()
        {
            gameState = TypePopup_Internal("GameState", gameState, typeof(GameState));

            playerState = TypePopup_Internal("PlayerState", playerState, typeof(PlayerState));
        }

        private static string TypePopup_Internal(string label, string value, Type baseType)
        {
            var types = Candidates_Internal(baseType).ToList();

            var names = types.Select(type => UEArchitectureStyles.TypeName(type, types)).ToArray();

            var index = Mathf.Max(0, types.FindIndex(type => type.FullName == value));

            index = EditorGUILayout.Popup(label, index, names);

            return types.Count > 0 ? types[index].FullName : value;
        }

        private static IEnumerable<Type> Candidates_Internal(Type baseType)
        {
            return TypeCache.GetTypesDerivedFrom(baseType).Append(baseType)
                            .Where(type => !type.IsAbstract && !type.ContainsGenericParameters && type.GetConstructor(Type.EmptyTypes) != null &&
                                           !UEArchitectureSettingsProvider.IsTestType_Internal(type))
                            .OrderBy(type => type == baseType ? 0 : 1).ThenBy(type => type.Name, StringComparer.Ordinal);
        }

        private DateTime ScriptStamp_Internal()
        {
            var path = ClassConversion.PhysicalPath(AssetDatabase.GetAssetPath(script));

            return System.IO.File.Exists(path) ? System.IO.File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        }

        private void Refresh_Internal()
        {
            _plan = null;

            _blocked = null;

            _statesPreview = null;

            _stamp = ScriptStamp_Internal();

            if (mode == Mode.States)
            {
                _statesPreview = UEArchitectureActions.ChangeGameModeStates(script, PendingSteps.FindClass(gameState), PendingSteps.FindClass(playerState));

                return;
            }

            var result = new ActionResult("convert-class");

            var request = UEArchitectureActions.ConvertRequestFor_Internal(script, Base, result);

            if (request == null)
            {
                _blocked = result;

                return;
            }

            request.NewClassName = rename ? newName : string.Empty;

            request.States = states;

            request.GameState = gameState;

            request.PlayerState = playerState;

            // Other scripts naming the class are looked up once per name, not on every keystroke.
            _plan = ClassConversion.Plan(request, new ConvertContext(PendingSteps.FindClass, path => System.IO.File.Exists(ClassConversion.PhysicalPath(path)), Naming.Get));
        }

        private readonly struct PreviewLine
        {
            public readonly string Text;

            public readonly bool Problem;

            public PreviewLine(string text, bool problem)
            {
                Text = text;

                Problem = problem;
            }
        }

        private IEnumerable<PreviewLine> PreviewLines_Internal()
        {
            if (mode == Mode.States)
            {
                if (_statesPreview == null)
                {
                    yield break;
                }

                foreach (var problem in _statesPreview.Problems)
                {
                    yield return new PreviewLine(problem, true);
                }

                foreach (var note in _statesPreview.Notes)
                {
                    yield return new PreviewLine(note, false);
                }

                if (_statesPreview.Ok && _statesPreview.Notes.Count == 0)
                {
                    yield return new PreviewLine(_statesPreview.Message, false);
                }

                yield break;
            }

            if (_blocked != null)
            {
                foreach (var problem in _blocked.Problems)
                {
                    yield return new PreviewLine(problem, true);
                }

                if (_blocked.Ok)
                {
                    yield return new PreviewLine(_blocked.Message, false);
                }

                yield break;
            }

            if (_plan == null)
            {
                yield break;
            }

            foreach (var problem in _plan.Problems)
            {
                yield return new PreviewLine(problem, true);
            }

            if (!_plan.Ok)
            {
                yield break;
            }

            foreach (var line in _plan.Describe())
            {
                yield return new PreviewLine(line, false);
            }

            if (_usages != null)
            {
                yield return new PreviewLine(_usages.Summary(), false);

                foreach (var line in _usages.Lines())
                {
                    yield return new PreviewLine("  " + line, false);
                }
            }
        }

        private bool CanApply_Internal()
        {
            // A preview waiting to be made again is not the one shown: wait for it.
            if (_dirty || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                return false;
            }

            return mode == Mode.States
                       ? _statesPreview != null && _statesPreview.Ok && _statesPreview.Notes.Count > 0
                       : _plan != null && _plan.Ok && !_plan.AlreadyDone;
        }

        private void Apply_Internal()
        {
            var file = System.IO.Path.GetFileName(AssetDatabase.GetAssetPath(script));

            if (!EditorUtility.DisplayDialog("Edit script", $"Write the change to {file}? Undo cannot take it back.", "Apply", "Cancel"))
            {
                return;
            }

            var result = mode == Mode.States
                             ? UEArchitectureActions.ChangeGameModeStates(script, PendingSteps.FindClass(gameState), PendingSteps.FindClass(playerState), false)
                             : ApplyPreviewed_Internal(_plan, _usages, ClassConversion.ScriptsNaming);

            Report(result);

            if (result.Ok)
            {
                Close();
            }
            else
            {
                Naming.Clear();

                _dirty = true;
            }
        }

        /// <summary>
        /// Writes the plan the preview showed, not a new one: if the script changed since, nothing is written. For a rename,
        /// the other scripts are searched again (the preview's search may be old).
        /// </summary>
        internal static ActionResult ApplyPreviewed_Internal(ConvertPlan plan, ScriptUsages usages, Func<string, string, IReadOnlyList<string>> scriptsNaming)
        {
            var result = new ActionResult("convert-class");

            if (plan != null && plan.Ok && plan.Renames)
            {
                var users = scriptsNaming(plan.ClassName, plan.ScriptPath);

                if (users.Count > 0)
                {
                    result.AddProblem($"Other scripts use the name {plan.ClassName} now ({string.Join(", ", users.Take(5).Select(System.IO.Path.GetFileName))}" +
                                      $"{(users.Count > 5 ? "..." : string.Empty)}): renaming would break them. Nothing was written: look at the preview again.");

                    return result;
                }
            }

            return UEArchitectureActions.Convert_Internal(plan, usages, false, result);
        }

        private string CliCommand_Internal()
        {
            var path = AssetDatabase.GetAssetPath(script);

            if (mode == Mode.States)
            {
                return UEArchitectureCli.CommandLine("change-gamemode-states", Pair_Internal("script", path), Pair_Internal("game-state", gameState),
                                                     Pair_Internal("player-state", playerState), Pair_Internal("apply", "true"));
            }

            return UEArchitectureCli.CommandLine("convert-class", Pair_Internal("script", path), Pair_Internal("to", Base?.Name), Pair_Internal("rename", rename ? newName : null),
                                                 Pair_Internal("states", states ? null : "false"), Pair_Internal("game-state", states ? gameState : null),
                                                 Pair_Internal("player-state", states ? playerState : null), Pair_Internal("apply", "true"));
        }

        private static KeyValuePair<string, string> Pair_Internal(string key, string value)
        {
            return new KeyValuePair<string, string>(key, value);
        }

        private static GUIStyle _problemStyle;

        private static GUIStyle ProblemStyle_Internal()
        {
            return _problemStyle ??= new GUIStyle(EditorStyles.label) { wordWrap = true, normal = { textColor = UEArchitectureStyles.Error } };
        }

        /// <summary>
        /// Logs what an action did (or why it stopped), with its notes.
        /// </summary>
        internal static void Report(ActionResult result)
        {
            if (result == null)
            {
                return;
            }

            var notes = result.Notes.Count > 0 ? "\n" + string.Join("\n", result.Notes) : string.Empty;

            if (result.Ok)
            {
                Debug.Log($"UE Architecture: {result}{notes}");
            }
            else
            {
                Debug.LogWarning($"UE Architecture: {string.Join("\n", result.Problems)}{notes}");
            }
        }
    }
}
