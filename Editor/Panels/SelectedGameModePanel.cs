using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using System.Text.RegularExpressions;
using UnityEditor.SceneManagement;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// Where a Selected GameMode panel is drawn.
    /// </summary>
    internal enum GameModePanelMode
    {
        /// <summary>
        /// In the GameMode's own inspector (slim: no nested panel).
        /// </summary>
        Inspector,

        /// <summary>
        /// In World Settings, for the GameMode the level uses.
        /// </summary>
        Level,

        /// <summary>
        /// In Project Settings > UE Architecture, for the default GameMode.
        /// </summary>
        ProjectDefault
    }

    /// <summary>
    /// How a Selected GameMode panel is drawn.
    /// </summary>
    internal sealed class GameModePanelContext
    {
        public GameModePanelMode Mode;

        /// <summary>
        /// Rows cannot be changed (the level uses the project default: "Override for this level" first).
        /// </summary>
        public bool ReadOnly;

        /// <summary>
        /// The level whose own GameMode this is: the rows' "Variant of X" then makes "Variant for this level".
        /// </summary>
        public string SceneName;
    }

    /// <summary>
    /// Unreal's "Selected GameMode" expander: the default classes of a GameMode (Player Controller, Default Pawn, HUD,
    /// Players at start) editable in place with class fields, its GameState and PlayerState classes, and the level actions
    /// (Override for this level, Clear override, Make project default). Shown in World Settings, in Project Settings and,
    /// slim, in the GameMode inspector. Overridden rows of a variant are bold; right-click reverts them.
    /// </summary>
    internal static class SelectedGameModePanel
    {
        private static readonly Dictionary<Object, SerializedObject> _serialized = new();

        private static readonly HashSet<Object> _pendingSaves = new();

        private static readonly HashSet<GameObject> _editShared = new();

        private static readonly GUIContent _editSharedLabel = new("Edit shared prefab", "Change the shared prefab itself, for every level that uses it. To change it for this level only, use Override for this level.");

        private static readonly GUIContent _playerControllerLabel = new("Player Controller", "The prefab each player gets as their PlayerController (their input). Empty = a plain PlayerController.");

        private static readonly GUIContent _defaultPawnLabel = new("Default Pawn", "The prefab spawned for each player at a Player Start. Empty = players start without a Pawn.");

        private static readonly GUIContent _hudLabel = new("HUD", "The prefab with each player's interface. Empty = no HUD.");

        private static readonly GUIContent _playersLabel = new("Players at start", "Players created when the level begins. Add more later with AddPlayer().");

        /// <summary>
        /// The panel for a GameMode that is not the inspected object (one SerializedObject is kept per GameMode). Edits go
        /// to the copy open in Prefab Mode when its prefab is open there; a prefab asset is saved once the edit is over
        /// (mouse up, text field left), not on every change of a drag.
        /// </summary>
        public static void Draw(GameMode gameMode, GameModePanelContext context)
        {
            if (gameMode == null)
            {
                return;
            }

            var target = Scaffold.Writable(gameMode);

            var serialized = Serialized_Internal(target);

            serialized.Update();

            DrawRows(serialized, context);

            if (serialized.ApplyModifiedProperties() && EditorUtility.IsPersistent(target))
            {
                SaveWhenIdle_Internal(target);
            }
        }

        /// <summary>
        /// Saves a changed prefab asset once no control is being dragged or typed in.
        /// </summary>
        private static void SaveWhenIdle_Internal(Object asset)
        {
            _pendingSaves.Add(asset);

            EditorApplication.update -= SavePending_Internal;

            EditorApplication.update += SavePending_Internal;
        }

        private static void SavePending_Internal()
        {
            if (GUIUtility.hotControl != 0 || EditorGUIUtility.editingTextField)
            {
                return;
            }

            EditorApplication.update -= SavePending_Internal;

            foreach (var asset in _pendingSaves)
            {
                if (asset != null)
                {
                    AssetDatabase.SaveAssetIfDirty(asset);
                }
            }

            _pendingSaves.Clear();
        }

        /// <summary>
        /// The rows of the panel for <paramref name="serialized"/> (a GameMode).
        /// </summary>
        public static void DrawRows(SerializedObject serialized, GameModePanelContext context)
        {
            var options = new ClassFieldOptions { SceneName = context?.SceneName };

            using (new EditorGUI.DisabledScope(context != null && context.ReadOnly))
            {
                ClassField.Layout(serialized.FindProperty("playerController"), _playerControllerLabel, typeof(PlayerController), options);

                ClassField.Layout(serialized.FindProperty("defaultPawn"), _defaultPawnLabel, typeof(Pawn), options);

                PawnNote_Internal(serialized);

                ClassField.Layout(serialized.FindProperty("hud"), _hudLabel, typeof(HUD), options);

                var players = serialized.FindProperty("playersAtStart");

                if (players != null)
                {
                    EditorGUILayout.PropertyField(players, _playersLabel);
                }
            }

            var gameMode = serialized.targetObject;

            if (gameMode == null)
            {
                return;
            }

            StateRow_Internal("GameState", GameModeInfo.GameStateType(gameMode.GetType()), typeof(GameState), gameMode);

            StateRow_Internal("PlayerState", GameModeInfo.PlayerStateType(gameMode.GetType()), typeof(PlayerState), gameMode);
        }

        /// <summary>
        /// The World Settings panel of a level: the GameMode it uses and why, its rows, and the level actions.
        /// </summary>
        public static void DrawForLevel(Scene scene)
        {
            var gameMode = GameModeInfo.Effective(scene, out GameModeSource source);

            UEArchitectureStyles.Section("Selected GameMode");

            switch (source)
            {
                case GameModeSource.Placed:
                    Header_Internal(gameMode, "placed in this level: it wins over World Settings");

                    InstanceNotice_Internal(gameMode);

                    Draw(gameMode, new GameModePanelContext { Mode = GameModePanelMode.Level });

                    return;

                case GameModeSource.WorldSettings:
                {
                    var own = GameModeInfo.IsLevelOverride(gameMode, scene);

                    var ownLevel = VariantLevel(scene, gameMode);

                    Header_Internal(gameMode, own ? "this level's own GameMode" : "a shared prefab, picked in World Settings");

                    var editShared = !own && _editShared.Contains(gameMode.gameObject);

                    Draw(gameMode, new GameModePanelContext { Mode = GameModePanelMode.Level, SceneName = ownLevel, ReadOnly = !own && !editShared });

                    if (!own)
                    {
                        SharedNotice_Internal(gameMode, editShared);

                        OverrideButton_Internal(scene, gameMode);
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.FlexibleSpace();

                        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                        {
                            if (UEArchitectureStyles.SmallButton(new GUIContent("Clear override", "Use the project default GameMode in this level again. The prefab stays.")))
                            {
                                ClassField.Run_Internal(() => UEArchitectureActions.ClearOverride(scene));

                                GUIUtility.ExitGUI();
                            }
                        }

                        var settings = FrameworkIndex.Settings;

                        if ((settings == null || settings.DefaultGameMode != gameMode) &&
                            UEArchitectureStyles.SmallButton(new GUIContent("Make project default", "Use this GameMode in every level that does not pick one.")))
                        {
                            ClassField.Run_Internal(() => UEArchitectureActions.SetProjectDefaultGameMode(gameMode.gameObject));

                            GUIUtility.ExitGUI();
                        }
                    }

                    return;
                }

                case GameModeSource.ProjectDefault:
                    Header_Internal(gameMode, "project default");

                    Draw(gameMode, new GameModePanelContext { Mode = GameModePanelMode.Level, ReadOnly = true });

                    OverrideButton_Internal(scene, gameMode);

                    EditorGUILayout.LabelField(FrameworkIndex.UsageLine(gameMode.gameObject) + " Change it for every level in Project Settings, or override it for this level here.",
                                               UEArchitectureStyles.MutedLabel);

                    return;

                default:
                    EditorGUILayout.HelpBox("No GameMode: this level spawns no players. Give this level its own GameMode, or set a default one for every level in Project Settings.",
                                            MessageType.Info);

                    OverrideButton_Internal(scene, null);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.FlexibleSpace();

                        if (UEArchitectureStyles.SmallButton(new GUIContent("Open Project Settings")))
                        {
                            SettingsService.OpenProjectSettings(UEArchitectureSettingsProvider.SettingsPath);
                        }
                    }

                    return;
            }
        }

        /// <summary>
        /// The level whose folder the rows' "Variant for this level" writes to, for a level's own GameMode: the level's
        /// override folder name (<see cref="GameModeInfo.LevelKey"/>: "Boss", or "Gameplay (Snow)" when another level
        /// called Gameplay has its GameMode in Modes/Gameplay), so two levels never share a variant. Null for a shared
        /// GameMode, whose rows make plain variants.
        /// </summary>
        public static string VariantLevel(Scene scene, GameMode gameMode)
        {
            return GameModeInfo.IsLevelOverride(gameMode, scene) ? GameModeInfo.LevelKey(scene) : null;
        }

        /// <summary>
        /// The panel of the project's default GameMode, on the settings page.
        /// </summary>
        public static void DrawForProject(GameMode gameMode)
        {
            if (gameMode == null)
            {
                return;
            }

            Draw(gameMode, new GameModePanelContext { Mode = GameModePanelMode.ProjectDefault });

            EditorGUILayout.LabelField(FrameworkIndex.UsageLine(gameMode.gameObject) + " Changes here apply to all of them; a level can override it in its World Settings.",
                                       UEArchitectureStyles.MutedLabel);
        }

        /// <summary>
        /// Below a GameMode's own inspector: what editing it changes (a copy of a prefab, a shared prefab) and a shortcut.
        /// </summary>
        public static void DrawFooter(GameMode gameMode)
        {
            if (gameMode == null)
            {
                return;
            }

            if (!EditorUtility.IsPersistent(gameMode))
            {
                InstanceNotice_Internal(gameMode);

                return;
            }

            EditorGUILayout.Space(4);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(FrameworkIndex.UsageLine(gameMode.transform.root.gameObject), UEArchitectureStyles.MutedLabel);

                var settings = FrameworkIndex.Settings;

                if (gameMode.transform.parent == null && (settings == null || settings.DefaultGameMode != gameMode) &&
                    UEArchitectureStyles.SmallButton(new GUIContent("Make project default", "Use this GameMode in every level that does not pick one.")))
                {
                    ClassField.Run_Internal(() => UEArchitectureActions.SetProjectDefaultGameMode(gameMode.gameObject));

                    GUIUtility.ExitGUI();
                }
            }
        }

        private static void Header_Internal(GameMode gameMode, string source)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("GameMode", $"{gameMode.name}  ({source})");

                if (UEArchitectureStyles.SmallButton(new GUIContent("Select", "Select this GameMode.")))
                {
                    Selection.activeObject = gameMode.gameObject;

                    EditorCompat.Ping(gameMode.gameObject);
                }
            }
        }

        private static void OverrideButton_Internal(Scene scene, GameMode current)
        {
            var folder = string.IsNullOrEmpty(scene.path)
                             ? "a folder named after the level"
                             : GeneratorNaming.OverrideFolder(GameModeInfo.LevelKey(scene), UEArchitectureEditorSettings.Current);

            var tooltip = current != null
                              ? $"Make a variant of {current.name} in {folder} (Unreal's GameMode Override) and use it in this level only. Creating the file cannot be undone; running it again reuses it."
                              : $"Make a GameMode prefab in {folder} and use it in this level. Creating the file cannot be undone; running it again reuses it.";

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                {
                    if (!UEArchitectureStyles.AccentButton(new GUIContent("Override for this level", tooltip), GUILayout.Width(180f)))
                    {
                        return;
                    }
                }
            }

            if (string.IsNullOrEmpty(scene.path) && (!EditorSceneManager.SaveScene(scene) || string.IsNullOrEmpty(scene.path)))
            {
                GUIUtility.ExitGUI();
            }

            ClassField.Run_Internal(() => UEArchitectureActions.OverrideGameModeForLevel(scene));

            GUIUtility.ExitGUI();
        }

        /// <summary>
        /// Under a shared GameMode prefab shown for a level: how many levels use it, and the switch that makes its rows
        /// editable (off by default, so a level never changes every other level by mistake).
        /// </summary>
        private static void SharedNotice_Internal(GameMode gameMode, bool editing)
        {
            var prefab = gameMode.transform.root.gameObject;

            var line = FrameworkIndex.SharedLine(prefab);

            EditorGUILayout.HelpBox(editing ? line : line + " Its rows are read-only here: override it for this level, or turn on Edit shared prefab.",
                                    editing ? MessageType.Warning : MessageType.Info);

            var edit = EditorGUILayout.ToggleLeft(_editSharedLabel, editing);

            if (edit == editing)
            {
                return;
            }

            if (edit)
            {
                _editShared.Add(prefab);
            }
            else
            {
                _editShared.Remove(prefab);
            }
        }

        private static void InstanceNotice_Internal(GameMode gameMode)
        {
            if (gameMode == null || EditorUtility.IsPersistent(gameMode) || !PrefabUtility.IsPartOfPrefabInstance(gameMode))
            {
                return;
            }

            var path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameMode);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"Changes here are overrides on this copy of {Path.GetFileName(path)}; the prefab and its other copies keep their values.", UEArchitectureStyles.MutedLabel);

                if (UEArchitectureStyles.SmallButton(new GUIContent("Edit the prefab", "Open " + path + " in Prefab Mode.")))
                {
                    AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<GameObject>(path));

                    GUIUtility.ExitGUI();
                }
            }
        }

        private static void PawnNote_Internal(SerializedObject serialized)
        {
            var pawn = serialized.FindProperty("defaultPawn");

            if (pawn == null || pawn.hasMultipleDifferentValues || pawn.objectReferenceValue != null || serialized.isEditingMultipleObjects)
            {
                return;
            }

            var note = GameModeInfo.TryPlacedPawn(serialized, out var field, out var placed)
                           ? $"Possesses the placed Pawn \"{placed.name}\" from code (field {field})."
                           : "Empty: players start without a Pawn.";

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EditorGUIUtility.labelWidth);

                EditorGUILayout.LabelField(note, UEArchitectureStyles.MutedLabel);
            }
        }

        private static void StateRow_Internal(string label, Type type, Type baseType, Object gameMode)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var tooltip = $"Set by the class: {gameMode.GetType().Name} : GameMode<GameState, PlayerState>. Change it in code.";

                EditorGUILayout.LabelField(new GUIContent(label, tooltip), new GUIContent(type == baseType ? $"{type.Name} (default)" : type.Name, tooltip),
                                           type == baseType ? UEArchitectureStyles.MutedLabel : EditorStyles.label);

                if (UEArchitectureStyles.SmallButton(new GUIContent("Open script", $"Open the script that declares {type.Name}.")))
                {
                    var script = ScriptLocator.Find(type, gameMode);

                    if (script != null)
                    {
                        AssetDatabase.OpenAsset(script);
                    }
                    else
                    {
                        Debug.LogWarning($"UE Architecture: no script in the project declares {type.FullName}.");
                    }

                    GUIUtility.ExitGUI();
                }
            }
        }

        private static SerializedObject Serialized_Internal(Object target)
        {
            if (_serialized.TryGetValue(target, out var cached) && cached != null && cached.targetObject == target)
            {
                return cached;
            }

            if (_serialized.Count > 16)
            {
                _serialized.Clear();
            }

            cached = new SerializedObject(target);

            _serialized[target] = cached;

            return cached;
        }
    }

    /// <summary>
    /// Finds the script that declares a class (for "Open script" on classes that are not components, such as a
    /// GameState declared in its GameMode's file). Searches only when asked, never on repaint.
    /// </summary>
    internal static class ScriptLocator
    {
        public static MonoScript Find(Type type, Object near = null)
        {
            if (type == null)
            {
                return null;
            }

            foreach (var guid in AssetDatabase.FindAssets($"{type.Name} t:MonoScript"))
            {
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid));

                if (script != null && script.GetClass() == type)
                {
                    return script;
                }
            }

            if (near is MonoBehaviour behaviour)
            {
                var own = MonoScript.FromMonoBehaviour(behaviour);

                if (Declares_Internal(own, type))
                {
                    return own;
                }
            }

            foreach (var guid in AssetDatabase.FindAssets("t:MonoScript"))
            {
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid));

                if (Declares_Internal(script, type))
                {
                    return script;
                }
            }

            return null;
        }

        private static bool Declares_Internal(MonoScript script, Type type)
        {
            return script != null && Regex.IsMatch(script.text ?? string.Empty, $@"\b(class|struct)\s+{Regex.Escape(type.Name)}\b");
        }
    }
}
