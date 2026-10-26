using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace ModernWestern.UEArchitecture.EditorTools
{
    public static partial class UEArchitectureActions
    {
        /// <summary>
        /// Checks levels the way the Level overlay does, for CI ("-uea validate scenes=build"): no GameMode, no Player
        /// Start, no EventSystem for the HUD's buttons, no AudioListener, not in Build Settings, HUDs and loading screens
        /// without a Canvas, and the settings asset outside Resources. <paramref name="scenes"/> is "build" (the enabled
        /// levels of Build Settings), "open", "all" (every level under Assets/) or paths separated by ';'. Errors are
        /// problems (exit code 1); warnings are problems too with <paramref name="strict"/>, notes otherwise. Levels that
        /// are not open are opened next to the others and closed again; nothing is changed.
        /// </summary>
        public static ActionResult Validate(string scenes = "build", bool strict = false)
        {
            var result = new ActionResult("validate");

            var paths = ScenePaths_Internal(scenes, result, out var openUnsaved);

            var counts = new int[3];

            foreach (var problem in BuiltInProblems.Project())
            {
                Report_Internal(result, "Project", problem, strict, counts);
            }

            var checkedLevels = 0;

            foreach (var scene in openUnsaved)
            {
                checkedLevels++;

                ProblemProviders.Invalidate();

                foreach (var problem in ProblemProviders.ForLevel(scene).Problems)
                {
                    Report_Internal(result, string.IsNullOrEmpty(scene.name) ? "Untitled" : scene.name, problem, strict, counts);
                }
            }

            foreach (var path in paths)
            {
                using (var level = LevelScope.Open(path, "validate"))
                {
                    if (!level.Ok)
                    {
                        result.Merge(level.Failure);

                        continue;
                    }

                    checkedLevels++;

                    ProblemProviders.Invalidate();

                    foreach (var problem in ProblemProviders.ForLevel(level.Scene).Problems)
                    {
                        Report_Internal(result, path, problem, strict, counts);
                    }
                }
            }

            ProblemProviders.Invalidate();

            result.Message = $"Checked {checkedLevels} level(s): {counts[2]} error(s), {counts[1]} warning(s), {counts[0]} note(s).";

            return result;
        }

        /// <summary>
        /// Applies every fix marked safe to the levels of <paramref name="scenes"/> (see <see cref="Validate"/>): Player
        /// Starts, EventSystem, AudioListener, a HUD's or loading screen's missing Canvas, a loading screen's Canvas Group
        /// and sorting order. Fixes that change a choice (which GameMode a level uses, which levels are in Build Settings)
        /// or move objects are left for you. Prefab assets are changed directly, never opened in Prefab Mode. Levels that
        /// are not open are opened and closed, and saved only when their own fixes changed them. A second run changes
        /// nothing.
        /// </summary>
        public static ActionResult FixAll(string scenes = "build")
        {
            var result = new ActionResult("fix-all");

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                result.AddProblem("Leave Play mode first: fixes are applied in Edit mode.");

                return result;
            }

            var paths = ScenePaths_Internal(scenes, result, out var openUnsaved);

            var applied = 0;

            // A prefab fix (a HUD's missing Canvas) must not open Prefab Mode halfway through the levels.
            using var inPlace = Scaffold.EditPrefabsInPlace();

            foreach (var scene in openUnsaved)
            {
                applied += FixLevel_Internal(scene, result);
            }

            foreach (var path in paths)
            {
                using (var level = LevelScope.Open(path, "fix-all"))
                {
                    if (!level.Ok)
                    {
                        result.Merge(level.Failure);

                        continue;
                    }

                    var created = result.Created.Count;

                    var changed = result.Changed.Count;

                    applied += FixLevel_Internal(level.Scene, result);

                    // Saved (when the scope closes it) only when this level's own fixes touched it: fixes of a prefab
                    // asset or of Build Settings leave the level as it was.
                    var address = level.Scene.path + "#";

                    if (result.Created.Skip(created).Concat(result.Changed.Skip(changed)).Any(entry => entry.StartsWith(address, StringComparison.Ordinal)))
                    {
                        EditorSceneManager.MarkSceneDirty(level.Scene);
                    }
                }
            }

            ProblemProviders.Invalidate();

            result.Message = applied == 0 ? "Nothing to fix." : $"Applied {applied} fix(es).";

            return result;
        }

        private static int FixLevel_Internal(Scene scene, ActionResult result)
        {
            var applied = 0;

            var done = new HashSet<string>();

            ProblemProviders.Invalidate();

            foreach (var problem in ProblemProviders.ForLevel(scene).Problems.ToList())
            {
                // The same fix offered twice (a shared HUD prefab seen from two objects) runs once.
                if (!problem.HasFix || !problem.SafeForFixAll || problem.OpensWindow || !done.Add(problem.FixId + "|" + problem.Message))
                {
                    continue;
                }

                var fixedResult = problem.Fix();

                if (fixedResult == null)
                {
                    continue;
                }

                if (fixedResult.Created.Count + fixedResult.Changed.Count > 0)
                {
                    applied++;
                }

                result.Merge(fixedResult);
            }

            return applied;
        }

        private static void Report_Internal(ActionResult result, string where, Problem problem, bool strict, int[] counts)
        {
            counts[(int)problem.Severity]++;

            var fix = problem.HasFix && !problem.OpensWindow ? $" (fix: {problem.FixLabel})" : string.Empty;

            var line = $"{where}: {problem.Message}{fix}";

            if (problem.Severity == ProblemSeverity.Error || strict && problem.Severity == ProblemSeverity.Warning)
            {
                result.AddProblem(line);
            }
            else
            {
                result.AddNote(line);
            }
        }

        /// <summary>
        /// The saved levels a "scenes=" argument names, and the open levels that were never saved (only with "open").
        /// </summary>
        internal static List<string> ScenePaths_Internal(string scenes, ActionResult result, out List<Scene> openUnsaved)
        {
            openUnsaved = new List<Scene>();

            var key = string.IsNullOrWhiteSpace(scenes) ? "build" : scenes.Trim();

            var paths = new List<string>();

            switch (key.ToLowerInvariant())
            {
                case "build":
                    paths.AddRange(EditorBuildSettings.scenes.Where(scene => scene.enabled && !string.IsNullOrEmpty(scene.path)).Select(scene => scene.path));

                    break;

                case "open":
                    for (var i = 0; i < SceneManager.sceneCount; i++)
                    {
                        var scene = SceneManager.GetSceneAt(i);

                        if (!scene.isLoaded || EditorSceneManager.IsPreviewScene(scene))
                        {
                            continue;
                        }

                        if (string.IsNullOrEmpty(scene.path))
                        {
                            openUnsaved.Add(scene);
                        }
                        else
                        {
                            paths.Add(scene.path);
                        }
                    }

                    break;

                case "all":
                    paths.AddRange(AssetDatabase.FindAssets("t:Scene", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath));

                    break;

                default:
                    paths.AddRange(key.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(path => path.Trim().Replace('\\', '/')));

                    break;
            }

            if (paths.Count == 0 && openUnsaved.Count == 0)
            {
                result.AddNote(key == "build" ? "Build Settings has no enabled level." : $"No level matches \"{key}\".");
            }

            return paths.Distinct().ToList();
        }
    }
}
