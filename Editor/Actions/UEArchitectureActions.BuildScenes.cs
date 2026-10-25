using System;
using System.Linq;
using UnityEditor;
using System.Collections.Generic;

namespace ModernWestern.UEArchitecture.EditorTools
{
    public static partial class UEArchitectureActions
    {
        /// <summary>
        /// Makes a level the first scene of the build (index 0, the one a player starts in), adding or enabling it when
        /// needed. The other scenes keep their order. Build Settings changes cannot be undone with Undo.
        /// </summary>
        public static ActionResult SetFirstLevel(string scenePath)
        {
            const string verb = "set-first-level";

            if (!CheckBuildLevel_Internal(verb, ref scenePath, out var refused))
            {
                return refused;
            }

            var result = new ActionResult(verb);

            var scenes = EditorBuildSettings.scenes.ToList();

            var first = scenes.FindIndex(scene => scene.enabled);

            if (first >= 0 && scenes[first].path == scenePath && scenes.FindIndex(scene => scene.path == scenePath) == first)
            {
                result.Message = $"{scenePath} is already the first scene of the build.";

                return result;
            }

            scenes.RemoveAll(scene => scene.path == scenePath);

            scenes.Insert(0, new EditorBuildSettingsScene(scenePath, true));

            EditorBuildSettings.scenes = scenes.ToArray();

            result.AddChanged($"Build Settings: {scenePath} first");

            result.Message = $"{scenePath} is the first scene of the build now: players start in it.";

            return result;
        }

        /// <summary>
        /// The levels list of Project Settings > UE Architecture, one note per level: its build index, GameMode and where
        /// that comes from, World Settings and Player Starts, read from the saved files (approximate).
        /// </summary>
        public static ActionResult ListLevels()
        {
            var result = new ActionResult("list-levels");

            var rows = FrameworkIndex.LevelRows();

            foreach (var row in rows)
            {
                result.AddNote(row.Describe());
            }

            var withoutGameMode = rows.Count(row => !row.FileMissing && row.Source == GameModeSource.None);

            result.Message = $"{TagUsageReport.Plural_Internal(rows.Count, "level")}" + (withoutGameMode > 0 ? $", {withoutGameMode} without a GameMode" : string.Empty) +
                             " (from the saved files: approximate, objects inside nested prefabs are not seen).";

            return result;
        }

        /// <summary>
        /// The subsystems list of Project Settings > UE Architecture, one note per class: Game or World, and when it ticks.
        /// </summary>
        public static ActionResult ListSubsystems()
        {
            var result = new ActionResult("list-subsystems");

            var rows = SubsystemList.Project();

            foreach (var row in rows)
            {
                result.AddNote($"{row.Type.FullName}  {row.Kind}" + (row.Tick != null ? "  " + row.Tick : string.Empty));
            }

            result.Message = rows.Count == 0 ? "No subsystems yet." : $"{TagUsageReport.Plural_Internal(rows.Count, "subsystem")}.";

            return result;
        }

        private static bool CheckBuildLevel_Internal(string verb, ref string scenePath, out ActionResult refused)
        {
            refused = null;

            scenePath = (scenePath ?? string.Empty).Trim().Replace('\\', '/');

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                refused = ActionResult.Fail(verb, "Leave Play mode first: Build Settings are changed in Edit mode.");
            }
            else if (scenePath.Length == 0)
            {
                refused = ActionResult.Fail(verb, "Name the level, e.g. Assets/Scenes/Boss.unity.");
            }
            else if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
            {
                refused = ActionResult.Fail(verb, $"There is no scene at {scenePath}.");
            }

            return refused == null;
        }
    }
}
