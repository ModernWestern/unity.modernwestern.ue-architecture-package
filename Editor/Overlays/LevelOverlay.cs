using System.Linq;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
#if UNITY_2021_2_OR_NEWER
using UnityEditor.Overlays;
#endif

namespace ModernWestern.UEArchitecture.EditorTools
{
#if UNITY_2021_2_OR_NEWER
    /// <summary>
    /// The Scene view's "Level" overlay, like Unreal's World Settings at a glance: "GameMode: Arena Mode (project
    /// default) · Starts 0/1 · In build", one accent button with the most important fix (Create a GameMode…, Add Player
    /// Start, Add EventSystem...), and the level's problems with "Fix all safe". In Prefab Mode it lists the prefab's
    /// problems. It reads cached results only (see <see cref="ProblemProviders"/>). Hide it from the Scene view's
    /// Overlays menu (the ` key).
    /// </summary>
    [Overlay(typeof(SceneView), "uea-level", "Level", true)]
    internal sealed class LevelOverlay : IMGUIOverlay
    {
        public override void OnGUI()
        {
            LevelOverlayView.Draw();
        }
    }
#endif

    /// <summary>
    /// What the Level overlay draws (kept apart from the Overlay API, which moved between Unity versions).
    /// </summary>
    internal static class LevelOverlayView
    {
        private const float Width = 280f;

        private static bool _expanded;

        private static int _prefabVersion = -1;

        private static GameObject _prefabRoot;

        private static List<Problem> _prefabProblems;

        public static void Draw()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(Width)))
            {
                if (EditorApplication.isPlaying)
                {
                    EditorGUILayout.LabelField("Playing: the level's setup shows in Edit mode.", UEArchitectureStyles.MutedLabel);

                    return;
                }

                var stage = PrefabStageUtility.GetCurrentPrefabStage();

                if (stage != null && stage.prefabContentsRoot != null)
                {
                    DrawPrefab_Internal(stage.prefabContentsRoot);

                    return;
                }

                DrawLevel_Internal(ProblemProviders.ForLevel(SceneManager.GetActiveScene()));
            }
        }

        private static void DrawLevel_Internal(LevelReport report)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("GameMode", GUILayout.Width(70f));

                if (report.GameMode == null)
                {
                    EditorGUILayout.LabelField("none", UEArchitectureStyles.MutedLabel);
                }
                else if (GUILayout.Button(new GUIContent($"{report.GameMode.gameObject.name} ({GameModeInfo.Describe(report.Source)})", "Select it"), EditorStyles.linkLabel))
                {
                    var selected = EditorUtility.IsPersistent(report.GameMode) ? report.GameMode.transform.root.gameObject : report.GameMode.gameObject;

                    Selection.activeObject = selected;

                    EditorCompat.Ping(selected);
                }
            }

            var build = string.IsNullOrEmpty(report.Scene.path) ? "Not saved" : report.InBuild ? "In build" : "Not in build";

            EditorGUILayout.LabelField($"Starts {report.Starts}/{report.Players}  ·  {build}", UEArchitectureStyles.MutedLabel);

            if (report.GameMode != null && report.PlacedPawn != null &&
                GUILayout.Button(new GUIContent(report.PlacedPawnWaits
                                                    ? $"The placed Pawn \"{report.PlacedPawn.gameObject.name}\" waits for {GameModeInfo.PlayerLabel(0)} to join"
                                                    : $"{GameModeInfo.PlayerLabel(0)} possesses the placed Pawn \"{report.PlacedPawn.gameObject.name}\"", "Select it"), UEArchitectureStyles.MutedLabel))
            {
                Selection.activeObject = report.PlacedPawn.gameObject;
            }

            var next = report.Next;

            if (next != null)
            {
                if (UEArchitectureStyles.AccentButton(new GUIContent(next.FixLabel, next.Message)))
                {
                    ProblemsSection.Run(next);

                    GUIUtility.ExitGUI();
                }
            }
            else if (report.Source == GameModeSource.ProjectDefault && !string.IsNullOrEmpty(report.Scene.path))
            {
                if (UEArchitectureStyles.AccentButton(new GUIContent("Override for this level", "Makes a variant of the project's GameMode for this level only and uses it.")))
                {
                    var scene = report.Scene;

                    ClassField.Run_Internal(() => UEArchitectureActions.OverrideGameModeForLevel(scene));

                    ProblemProviders.Invalidate();

                    GUIUtility.ExitGUI();
                }
            }

            DrawProblems_Internal(report.Problems);
        }

        private static void DrawPrefab_Internal(GameObject root)
        {
            EditorGUILayout.LabelField(root.name, EditorStyles.boldLabel);

            var problems = PrefabProblems_Internal(root);

            if (problems.Count == 0)
            {
                EditorGUILayout.LabelField("Nothing to fix in this prefab.", UEArchitectureStyles.MutedLabel);

                return;
            }

            DrawProblems_Internal(problems);
        }

        /// <summary>
        /// The problems of every component of the prefab open in Prefab Mode, gathered again only when the problems change
        /// or another prefab is opened (never per GUI event).
        /// </summary>
        internal static List<Problem> PrefabProblems_Internal(GameObject root)
        {
            if (_prefabProblems == null || _prefabVersion != ProblemProviders.Version || _prefabRoot != root)
            {
                _prefabVersion = ProblemProviders.Version;

                _prefabRoot = root;

                _prefabProblems = root == null
                                      ? new List<Problem>()
                                      : root.GetComponentsInChildren<Component>(true).Where(component => component != null).SelectMany(ProblemProviders.For).ToList();
            }

            return _prefabProblems;
        }

        private static void DrawProblems_Internal(IReadOnlyList<Problem> problems)
        {
            if (problems.Count == 0)
            {
                EditorGUILayout.LabelField("Ready to play.", UEArchitectureStyles.MutedLabel);

                return;
            }

            var counted = problems.Count(problem => problem.Severity != ProblemSeverity.Info);

            var title = counted == 0 ? problems.Count == 1 ? "1 note" : $"{problems.Count} notes" : counted == 1 ? "1 problem" : $"{counted} problems";

            _expanded = EditorGUILayout.Foldout(_expanded, title, true);

            if (!_expanded)
            {
                return;
            }

            foreach (var problem in problems)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    var color = GUI.contentColor;

                    GUI.contentColor = ProblemsSection.ColorOf(problem.Severity);

                    EditorGUILayout.LabelField(ProblemsSection.Label(problem.Severity), EditorStyles.miniBoldLabel, GUILayout.Width(52f));

                    GUI.contentColor = color;

                    if (problem.Context != null && GUILayout.Button(new GUIContent(problem.Message, "Select " + problem.Context.name), UEArchitectureStyles.MutedLabel))
                    {
                        Selection.activeObject = problem.Context;
                    }
                    else if (problem.Context == null)
                    {
                        EditorGUILayout.LabelField(problem.Message, UEArchitectureStyles.MutedLabel);
                    }
                }

                if (problem.HasFix)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.FlexibleSpace();

                        if (UEArchitectureStyles.SmallButton(new GUIContent(problem.FixLabel)))
                        {
                            ProblemsSection.Run(problem);

                            GUIUtility.ExitGUI();
                        }
                    }
                }
            }

            if (problems.Any(problem => problem.HasFix && problem.SafeForFixAll && !problem.OpensWindow))
            {
                if (GUILayout.Button(new GUIContent("Fix all safe", "Runs every fix that only adds what is missing or fills an empty field.")))
                {
                    ProblemsSection.RunSafe(problems.ToList());

                    GUIUtility.ExitGUI();
                }
            }
        }
    }
}
