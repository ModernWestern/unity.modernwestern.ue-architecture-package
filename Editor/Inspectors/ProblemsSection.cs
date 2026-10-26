using System;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// Draws problems as notices with their one fix, in inspectors and in the Level overlay. Reads the cached results of
    /// <see cref="ProblemProviders"/>: nothing is computed per repaint.
    /// </summary>
    internal static class ProblemsSection
    {
        /// <summary>
        /// Every problem of a component, each with its fix button.
        /// </summary>
        public static void Draw(Component component)
        {
            if (component == null)
            {
                return;
            }

            foreach (var problem in ProblemProviders.For(component))
            {
                DrawProblem(problem);
            }
        }

        /// <summary>
        /// One problem as a notice; its fix runs when pressed. Returns true when the fix ran.
        /// </summary>
        public static bool DrawProblem(Problem problem)
        {
            var type = problem.Severity == ProblemSeverity.Error ? MessageType.Error : problem.Severity == ProblemSeverity.Warning ? MessageType.Warning : MessageType.Info;

            if (!UEArchitectureStyles.Notice(problem.Message, type, problem.HasFix ? problem.FixLabel : null))
            {
                return false;
            }

            Run(problem);

            GUIUtility.ExitGUI();

            return true;
        }

        /// <summary>
        /// Runs a problem's fix from the UI: what went wrong goes to the Console, the caches are cleared.
        /// </summary>
        public static void Run(Problem problem)
        {
            if (problem == null || !problem.HasFix)
            {
                return;
            }

            ClassField.Run_Internal(problem.Fix);

            ProblemProviders.Invalidate();
        }

        /// <summary>
        /// Runs every fix marked safe among <paramref name="problems"/>, as one Undo step. Prefab assets are changed and
        /// saved directly, so the Scene view never switches to Prefab Mode halfway.
        /// </summary>
        public static void RunSafe(IEnumerable<Problem> problems)
        {
            var safe = new List<Problem>();

            var done = new HashSet<string>();

            foreach (var problem in problems)
            {
                // The same fix offered twice (a shared HUD prefab seen from two objects) runs once.
                if (problem.HasFix && problem.SafeForFixAll && !problem.OpensWindow && done.Add(problem.FixId + "|" + problem.Message))
                {
                    safe.Add(problem);
                }
            }

            if (safe.Count == 0)
            {
                return;
            }

            Scaffold.AsOneUndoStep("Fix all safe", () =>
            {
                using (Scaffold.EditPrefabsInPlace())
                {
                    foreach (var problem in safe)
                    {
                        Run(problem);
                    }
                }
            });
        }

        /// <summary>
        /// A short label for a severity: "Error", "Warning" or "Note".
        /// </summary>
        public static string Label(ProblemSeverity severity)
        {
            switch (severity)
            {
                case ProblemSeverity.Error:
                    return "Error";

                case ProblemSeverity.Warning:
                    return "Warning";

                default:
                    return "Note";
            }
        }

        public static Color ColorOf(ProblemSeverity severity)
        {
            return severity == ProblemSeverity.Error ? UEArchitectureStyles.Error : severity == ProblemSeverity.Warning ? UEArchitectureStyles.Warning : UEArchitectureStyles.Muted;
        }
    }
}
