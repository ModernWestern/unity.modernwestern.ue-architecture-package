using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;
using System.Collections.Generic;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// The subsystems list and the levels list of Project Settings > UE Architecture. Rows come from caches
    /// (<see cref="SubsystemList"/>, <see cref="FrameworkIndex.LevelRows"/>), so repaints scan nothing; every fix button
    /// calls a <see cref="UEArchitectureActions"/> method.
    /// </summary>
    internal static class ProjectListsSection
    {
        private const float ChipGap = 4f;

        private static bool _showOtherLevels = true;

        private static string _lastMessage = string.Empty;

        public static void DrawSubsystems(string search)
        {
            UEArchitectureStyles.Section("Subsystems in the project");

            var rows = SubsystemList.Project().Where(row => Matches_Internal(search, row.Type.Name, row.Kind, row.Tick, "Subsystem", "Subsystems")).ToList();

            if (rows.Count == 0)
            {
                EditorGUILayout.LabelField(SubsystemList.Project().Count == 0 ? "None yet." : "No subsystem matches the search.", UEArchitectureStyles.MutedLabel);
            }

            var all = SubsystemList.Project().Select(row => row.Type).ToList();

            foreach (var row in rows)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(new GUIContent(UEArchitectureStyles.TypeName(row.Type, all), row.Type.FullName), GUILayout.MinWidth(120f));

                    Chip_Internal(row.Kind, row.World ? UEArchitectureStyles.Accent : UEArchitectureStyles.Muted,
                                  row.World ? "World subsystem: one per level, created when the level starts." : "Game subsystem: one for the whole game, created with the GameInstance.");

                    if (row.Tick != null)
                    {
                        Chip_Internal(row.Tick, UEArchitectureStyles.Muted, "When it ticks, from its [Tick] attribute (Update without one).");
                    }

                    GUILayout.FlexibleSpace();

                    if (UEArchitectureStyles.SmallButton(new GUIContent("Open", "Open its script.")))
                    {
                        var script = ScriptLocator.Find(row.Type);

                        if (script != null)
                        {
                            AssetDatabase.OpenAsset(script);
                        }
                        else
                        {
                            _lastMessage = $"Could not find the script of {row.Type.Name}.";
                        }

                        GUIUtility.ExitGUI();
                    }

                    if (UEArchitectureStyles.SmallButton(new GUIContent("Ping", "Show its script in the Project window.")))
                    {
                        EditorCompat.Ping(ScriptLocator.Find(row.Type));

                        GUIUtility.ExitGUI();
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();

                if (UEArchitectureStyles.SmallButton(new GUIContent("+ Game subsystem", "New Framework Class, prefilled: one object for the whole game, like Unreal's GameInstanceSubsystem.")))
                {
                    NewClassWindow.Open(typeof(GameInstanceSubsystem));

                    GUIUtility.ExitGUI();
                }

                if (UEArchitectureStyles.SmallButton(new GUIContent("+ World subsystem", "New Framework Class, prefilled: one object per level, like Unreal's WorldSubsystem.")))
                {
                    NewClassWindow.Open(typeof(WorldSubsystem));

                    GUIUtility.ExitGUI();
                }
            }

            EditorGUILayout.LabelField("Subsystems are created by themselves: no prefab, no scene object.", UEArchitectureStyles.MutedLabel);
        }

        public static void DrawLevels(string search)
        {
            UEArchitectureStyles.Section("Levels in the project");

            EditorGUILayout.LabelField("Build Settings first, in order, then the other levels. GameMode, World Settings and starts are read from the saved files: approximate " +
                                       "(objects inside nested prefabs are not seen; open a level for the exact answer).", UEArchitectureStyles.MutedLabel);

            var rows = FrameworkIndex.LevelRows();

            var shown = rows.Where(row => Matches_Internal(search, row.Path, row.GameMode, "Levels", "Level", "Build", "Scenes")).ToList();

            if (rows.Count == 0)
            {
                EditorGUILayout.LabelField("No levels under Assets/ yet.", UEArchitectureStyles.MutedLabel);
            }

            var build = shown.Where(row => row.BuildPosition >= 0).ToList();

            var others = shown.Where(row => row.BuildPosition < 0).ToList();

            foreach (var row in build)
            {
                DrawLevel_Internal(row);
            }

            if (others.Count > 0)
            {
                _showOtherLevels = EditorGUILayout.Foldout(_showOtherLevels, $"Not in Build Settings ({others.Count})", true);

                if (_showOtherLevels)
                {
                    foreach (var row in others)
                    {
                        DrawLevel_Internal(row);
                    }
                }
            }

            if (_lastMessage.Length > 0)
            {
                EditorGUILayout.HelpBox(_lastMessage, MessageType.Info);
            }
        }

        private static void DrawLevel_Internal(LevelRow row)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var index = row.FileMissing ? "missing" : row.BuildIndex >= 0 ? row.BuildIndex.ToString() : row.Build == BuildScenes.Status.Disabled ? "off" : "–";

                EditorGUILayout.LabelField(new GUIContent(index, row.FileMissing ? "In Build Settings, but the file is gone." :
                                                                 row.BuildIndex >= 0 ? "Build index (SceneManager.LoadScene)." :
                                                                 row.Build == BuildScenes.Status.Disabled ? "In Build Settings, disabled." : "Not in Build Settings."),
                                           UEArchitectureStyles.MutedLabel, GUILayout.Width(44f));

                EditorGUILayout.LabelField(new GUIContent(row.Name, row.Path), GUILayout.MinWidth(90f));

                var gameMode = row.Source == GameModeSource.None ? "No GameMode" : row.GameMode;

                var tooltip = row.Source == GameModeSource.None ? "Players get no PlayerController or Pawn here." : $"{row.GameMode}: {GameModeInfo.Describe(row.Source)} (from a file scan).";

                Chip_Internal(gameMode, row.Source == GameModeSource.None ? UEArchitectureStyles.Warning : row.Source == GameModeSource.ProjectDefault ? UEArchitectureStyles.Muted : UEArchitectureStyles.Accent,
                              tooltip);

                if (row.HasWorldSettings)
                {
                    Chip_Internal("World Settings", UEArchitectureStyles.Muted, "The level has World Settings.");
                }

                Chip_Internal(row.PlayerStarts < 0 ? "starts ?" : row.PlayerStarts == 1 ? "1 start" : row.PlayerStarts + " starts", row.PlayerStarts == 0 ? UEArchitectureStyles.Warning : UEArchitectureStyles.Muted,
                              "Player Starts placed in the level (from a file scan).");

                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(row.FileMissing))
                {
                    if (UEArchitectureStyles.SmallButton(new GUIContent("Open", "Open the level (asks to save the open ones first).")))
                    {
                        if (!EditorApplication.isPlayingOrWillChangePlaymode && EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                        {
                            EditorSceneManager.OpenScene(row.Path);
                        }

                        GUIUtility.ExitGUI();
                    }
                }

                if (row.CanAddToBuild && UEArchitectureStyles.SmallButton(new GUIContent(row.Build == BuildScenes.Status.Disabled ? "Enable in Build" : "Add to Build", "Put it in Build Settings.")))
                {
                    Report_Internal(UEArchitectureActions.AddSceneToBuild(row.Path));
                }

                if (row.CanBeFirst && UEArchitectureStyles.SmallButton(new GUIContent("Set as first scene", "Make it build index 0: the scene a player starts in.")))
                {
                    Report_Internal(UEArchitectureActions.SetFirstLevel(row.Path));
                }

                if (row.HasOverride && UEArchitectureStyles.SmallButton(new GUIContent("Use project default", "Clear the GameMode Override of its World Settings (the level is opened, changed and saved).")))
                {
                    Report_Internal(UEArchitectureActions.ClearOverride(row.Path));
                }
            }
        }

        private static void Report_Internal(ActionResult result)
        {
            _lastMessage = result.Ok ? result.Message : string.Join("\n", result.Problems);

            GUIUtility.ExitGUI();
        }

        private static void Chip_Internal(string text, Color color, string tooltip)
        {
            var size = UEArchitectureStyles.ChipSize(text);

            var rect = GUILayoutUtility.GetRect(size.x + 8f, EditorGUIUtility.singleLineHeight, GUILayout.Width(size.x + 8f));

            rect.y += 1f;

            rect.height -= 2f;

            UEArchitectureStyles.DrawChip(rect, text, color);

            GUI.Label(rect, new GUIContent(string.Empty, tooltip));

            GUILayout.Space(ChipGap);
        }

        private static bool Matches_Internal(string search, params string[] words)
        {
            return UEArchitectureStyles.Matches(search, words.Where(word => word != null).ToArray());
        }
    }
}
